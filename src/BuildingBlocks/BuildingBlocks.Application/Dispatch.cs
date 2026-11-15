using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.DependencyInjection;
using Shortlist.BuildingBlocks.Abstractions;

namespace Shortlist.BuildingBlocks.Application;

public sealed record Error(string Code, string Title, int Status, string? Detail = null)
{
    public const int BadRequest = 400;
    public const int Forbidden = 403;
    public const int NotFound = 404;
    public const int Conflict = 409;
    public const int PreconditionFailed = 412;

    public static Error NotFoundError(string code, string title) => new(code, title, NotFound);
    public static Error ConflictError(string code, string title) => new(code, title, Conflict);
    public static Error ForbiddenError(string code, string title) => new(code, title, Forbidden);
    public static Error Validation(string code, string title) => new(code, title, BadRequest);
    public static Error Precondition(string code, string title) => new(code, title, PreconditionFailed);
}

public class Result
{
    public Error? Error { get; }
    public bool IsSuccess => Error is null;

    protected Result()
    {
    }

    protected Result(Error error) => Error = error;

    public static Result Ok() => new();
    public static Result Fail(Error error) => new(error);
    public static implicit operator Result(Error error) => Fail(error);
}

public sealed class Result<T> : Result
{
    public T? Value { get; }

    private Result(T value) => Value = value;

    private Result(Error error) : base(error)
    {
    }

    public static Result<T> Ok(T value) => new(value);
    public static new Result<T> Fail(Error error) => new(error);
    public static Result<T> Or(T? value, Error error) => value is null ? Fail(error) : Ok(value);

    public static implicit operator Result<T>(T value) => Ok(value);
    public static implicit operator Result<T>(Error error) => Fail(error);
}

public sealed record FieldError(string Field, string Message);

public sealed class RequestValidationException(IReadOnlyList<FieldError> errors) : Exception("The request is not valid.")
{
    public IReadOnlyList<FieldError> Errors { get; } = errors;
}

public interface ICommand<TResponse>;

public interface IQuery<TResponse>;

public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    Task<TResponse> Handle(TCommand command, CancellationToken cancellationToken);
}

public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    Task<TResponse> Handle(TQuery query, CancellationToken cancellationToken);
}

public interface IDispatcher
{
    Task<TResponse> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken);
    Task<TResponse> Query<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken);
}

public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor);

public static class CursorCodec
{
    public static string Encode(DateTimeOffset createdAt, Guid id)
    {
        var raw = $"{createdAt.UtcDateTime:O}|{id:D}";
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(raw))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static bool TryDecode(string? cursor, out DateTimeOffset createdAt, out Guid id)
    {
        createdAt = default;
        id = default;
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return false;
        }

        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
            var raw = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            var parts = raw.Split('|', 2);
            return parts.Length == 2
                && DateTimeOffset.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out createdAt)
                && Guid.TryParse(parts[1], out id);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public static class HandlerRegistration
{
    public static IServiceCollection AddHandlersFrom(this IServiceCollection services, System.Reflection.Assembly assembly)
    {
        var types = assembly.GetTypes().Where(type => type is { IsAbstract: false, IsInterface: false });
        foreach (var type in types)
        {
            foreach (var iface in type.GetInterfaces())
            {
                if (!iface.IsGenericType)
                {
                    continue;
                }

                var definition = iface.GetGenericTypeDefinition();
                if (definition == typeof(ICommandHandler<,>)
                    || definition == typeof(IQueryHandler<,>)
                    || definition == typeof(IIntegrationEventHandler<>))
                {
                    services.AddScoped(iface, type);
                }
            }
        }

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        return services;
    }
}

internal sealed class Dispatcher(IServiceProvider services) : IDispatcher
{
    public Task<TResponse> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken) =>
        Invoke<TResponse>(command, cancellationToken, transactional: true);

    public Task<TResponse> Query<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken) =>
        Invoke<TResponse>(query, cancellationToken, transactional: false);

    private async Task<TResponse> Invoke<TResponse>(object request, CancellationToken cancellationToken, bool transactional)
    {
        await ValidateAsync(request, cancellationToken);

        var requestType = request.GetType();
        var handlerType = transactional
            ? typeof(ICommandHandler<,>).MakeGenericType(requestType, typeof(TResponse))
            : typeof(IQueryHandler<,>).MakeGenericType(requestType, typeof(TResponse));
        var handler = services.GetRequiredService(handlerType);
        var method = handlerType.GetMethod("Handle")
            ?? throw new InvalidOperationException($"Handler {handlerType.Name} has no Handle method.");

        if (!transactional)
        {
            return await InvokeHandler<TResponse>(handler, method, request, cancellationToken);
        }

        var stores = services.GetServices<ITransactionalStore>().ToList();
        foreach (var store in stores)
        {
            await store.BeginAsync(cancellationToken);
        }

        try
        {
            var result = await InvokeHandler<TResponse>(handler, method, request, cancellationToken);
            foreach (var store in stores)
            {
                await store.CommitAsync(cancellationToken);
            }

            return result;
        }
        catch
        {
            foreach (var store in stores)
            {
                await store.RollbackAsync(cancellationToken);
            }

            throw;
        }
    }

    private static async Task<TResponse> InvokeHandler<TResponse>(
        object handler,
        System.Reflection.MethodInfo method,
        object request,
        CancellationToken cancellationToken)
    {
        var task = (Task<TResponse>)method.Invoke(handler, [request, cancellationToken])!;
        return await task;
    }

    private async Task ValidateAsync(object request, CancellationToken cancellationToken)
    {
        var validatorType = typeof(IValidator<>).MakeGenericType(request.GetType());
        var validators = services.GetServices(validatorType).Cast<IValidator>().ToList();
        if (validators.Count == 0)
        {
            return;
        }

        var context = new ValidationContext<object>(request);
        var failures = new List<ValidationFailure>();
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(context, cancellationToken);
            failures.AddRange(result.Errors);
        }

        if (failures.Count == 0)
        {
            return;
        }

        throw new RequestValidationException(failures
            .Select(failure => new FieldError(ToCamel(failure.PropertyName), failure.ErrorMessage))
            .ToList());
    }

    private static string ToCamel(string name)
    {
        if (string.IsNullOrEmpty(name) || char.IsLower(name[0]))
        {
            return name;
        }

        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddBuildingBlocksApplication(this IServiceCollection services)
    {
        services.AddScoped<IDispatcher, Dispatcher>();
        return services;
    }
}
