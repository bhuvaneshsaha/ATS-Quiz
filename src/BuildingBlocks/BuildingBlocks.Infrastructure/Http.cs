using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shortlist.BuildingBlocks.Abstractions;
using Shortlist.BuildingBlocks.Application;

namespace Shortlist.BuildingBlocks.Infrastructure;

public static class ResultHttp
{
    public static IResult ToHttpResult(this Result result) =>
        result.IsSuccess ? Results.NoContent() : Problem(result.Error!);

    public static IResult ToHttpResult<T>(this Result<T> result) =>
        result.IsSuccess ? Results.Ok(result.Value) : Problem(result.Error!);

    public static IResult ToCreatedResult<T>(this Result<T> result, Func<T, string> location)
    {
        if (!result.IsSuccess || result.Value is null)
        {
            return Problem(result.Error ?? Error.NotFoundError("result.empty", "The result was empty."));
        }

        return Results.Created(location(result.Value), result.Value);
    }

    public static IResult Problem(Error error)
    {
        return Results.Problem(
            title: error.Title,
            detail: error.Detail ?? error.Title,
            statusCode: error.Status,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }

    public static IResult ValidationProblem(IReadOnlyList<FieldError> errors, HttpContext http)
    {
        var grouped = errors
            .GroupBy(error => error.Field)
            .ToDictionary(group => group.Key, group => group.Select(error => error.Message).ToArray());

        return Results.Problem(
            title: "The request is not valid.",
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "validation.failed",
                ["errors"] = grouped,
                ["traceId"] = http.TraceIdentifier
            });
    }
}

public sealed class IdempotentAttribute : Attribute;

public static class EndpointConventions
{
    public static RouteHandlerBuilder RequirePermission(this RouteHandlerBuilder builder, string permission) =>
        builder.RequireAuthorization(permission);

    public static RouteHandlerBuilder WithIdempotency(this RouteHandlerBuilder builder) =>
        builder.WithMetadata(new IdempotentAttribute()).AddEndpointFilter<IdempotencyFilter>();
}

public sealed class IdempotencyFilter(IIdempotencyStore store, ICurrentUser currentUser) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (http.GetEndpoint()?.Metadata.GetMetadata<IdempotentAttribute>() is null)
        {
            return await next(context);
        }

        if (!http.Request.Headers.TryGetValue("Idempotency-Key", out var keyValues) || string.IsNullOrWhiteSpace(keyValues))
        {
            return ResultHttp.Problem(Error.Validation("idempotency.key_required", "Send an Idempotency-Key header."));
        }

        var key = keyValues.ToString();
        var hash = await HashBodyAsync(http.Request, context.HttpContext.RequestAborted);
        var hit = await store.LookupAsync(key, currentUser.UserId, hash, context.HttpContext.RequestAborted);
        if (hit.Status == IdempotencyStatus.Conflict)
        {
            return ResultHttp.Problem(Error.ConflictError("idempotency.conflict", "This idempotency key was already used with a different request."));
        }

        if (hit.Status == IdempotencyStatus.Replay)
        {
            return new CapturedResult(hit.ResponseStatus ?? StatusCodes.Status200OK, hit.ContentType ?? "application/json", Encoding.UTF8.GetBytes(hit.Body ?? ""));
        }

        var executed = await next(context);
        if (executed is not IResult result)
        {
            return executed;
        }

        var original = http.Response.Body;
        await using var buffer = new MemoryStream();
        http.Response.Body = buffer;
        await result.ExecuteAsync(http);
        var bytes = buffer.ToArray();
        http.Response.Body = original;
        var contentType = http.Response.ContentType ?? "application/json";
        await store.SaveAsync(key, currentUser.UserId, hash, http.Response.StatusCode, contentType, Encoding.UTF8.GetString(bytes), context.HttpContext.RequestAborted);
        return new CapturedResult(http.Response.StatusCode, contentType, bytes);
    }

    private static async Task<string> HashBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        request.EnableBuffering();
        request.Body.Position = 0;
        using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync(cancellationToken);
        request.Body.Position = 0;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(body));
        return Convert.ToHexString(bytes);
    }
}

public sealed class CapturedResult(int statusCode, string contentType, byte[] body) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = contentType;
        await httpContext.Response.Body.WriteAsync(body);
    }
}

public static class ExceptionMapping
{
    public static void UseShortlistExceptions(this WebApplication app)
    {
        app.Use(async (http, next) =>
        {
            try
            {
                await next();
            }
            catch (RequestValidationException exception)
            {
                if (http.Response.HasStarted)
                {
                    throw;
                }

                http.Response.StatusCode = StatusCodes.Status400BadRequest;
                await ResultHttp.ValidationProblem(exception.Errors, http).ExecuteAsync(http);
            }
            catch (DomainRuleException exception)
            {
                if (http.Response.HasStarted)
                {
                    throw;
                }

                await ResultHttp.Problem(Error.ConflictError(exception.Code, exception.Message)).ExecuteAsync(http);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                var logger = http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Shortlist");
                logger.LogError(exception, "Unhandled exception {TraceId}", http.TraceIdentifier);
                if (http.Response.HasStarted)
                {
                    throw;
                }

                http.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await Results.Problem(
                    title: "Something went wrong.",
                    statusCode: StatusCodes.Status500InternalServerError,
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = "server.error",
                        ["traceId"] = http.TraceIdentifier
                    }).ExecuteAsync(http);
            }
        });
    }
}

public sealed class InProcessEventBus(IServiceProvider services) : IEventBus
{
    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        var handlers = services.GetServices<IIntegrationEventHandler<TEvent>>();
        foreach (var handler in handlers)
        {
            await handler.HandleAsync(@event, cancellationToken);
        }
    }
}
