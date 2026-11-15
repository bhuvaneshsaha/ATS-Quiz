using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using Serilog;
using Shortlist.Adapters.Cache.Memory;
using Shortlist.Adapters.Cache.Valkey;
using Shortlist.Adapters.Email.Smtp;
using Shortlist.Adapters.Jobs.Quartz;
using Shortlist.Adapters.Pdf.Playwright;
using Shortlist.Adapters.Storage.AzureBlob;
using Shortlist.Adapters.Storage.FileSystem;
using Shortlist.Adapters.Storage.S3;
using Shortlist.BuildingBlocks.Abstractions;
using Shortlist.BuildingBlocks.Application;
using Shortlist.BuildingBlocks.Infrastructure;
using Shortlist.Modules.Assessments.Api;
using Shortlist.Modules.Assessments.Application;
using Shortlist.Modules.Assessments.Infrastructure;
using Shortlist.Modules.Core.Api;
using Shortlist.Modules.Core.Application;
using Shortlist.Modules.Core.Contracts;
using Shortlist.Modules.Core.Infrastructure;
using Shortlist.Modules.Identity.Api;
using Shortlist.Modules.Identity.Application;
using Shortlist.Modules.Identity.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((context, configuration) => configuration.ReadFrom.Configuration(context.Configuration).WriteTo.Console());

var connectionString = builder.Configuration.GetConnectionString("Database")
    ?? throw new InvalidOperationException("ConnectionStrings:Database is required.");
builder.Services.AddOptions<WorkspaceOptions>().Bind(builder.Configuration.GetSection(WorkspaceOptions.Section)).Validate(options => options.Id != Guid.Empty, "Workspace:Id is required.").ValidateOnStart();
builder.Services.AddOptions<SeedOptions>().Bind(builder.Configuration.GetSection(SeedOptions.Section));

builder.Services.AddBuildingBlocksApplication();
builder.Services.AddModuleInterceptors();
builder.Services.AddShortlistSecurity();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IEventBus, InProcessEventBus>();

AddContext<IdentityDbContext>(builder.Services, connectionString);
AddContext<CoreDbContext>(builder.Services, connectionString);
AddContext<AssessmentsDbContext>(builder.Services, connectionString);

builder.Services.AddIdentity<StaffUser, StaffRole>(options =>
{
    options.Password.RequiredLength = 12;
    options.Password.RequireDigit = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.User.RequireUniqueEmail = true;
    options.SignIn.RequireConfirmedEmail = false;
}).AddEntityFrameworkStores<IdentityDbContext>().AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "shortlist.staff";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});
builder.Services.AddAuthentication().AddCookie("Candidate", options =>
{
    options.Cookie.Name = "shortlist.candidate";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromHours(4);
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
});

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
    options.Cookie.Name = ".Shortlist.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("candidate-auth", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});

builder.Services.AddHandlersFrom(typeof(CreateOpening).Assembly);
builder.Services.AddHandlersFrom(typeof(CoreOutboxStore).Assembly);
builder.Services.AddHandlersFrom(typeof(SignIn).Assembly);
builder.Services.AddHandlersFrom(typeof(IdentityStartupSync).Assembly);
builder.Services.AddHandlersFrom(typeof(CreateQuestion).Assembly);
builder.Services.AddHandlersFrom(typeof(AttemptFinalizer).Assembly);

builder.Services.AddSingleton<IPermissionSource, IdentityPermissionSource>();
builder.Services.AddSingleton<IPermissionSource, CorePermissionSource>();
builder.Services.AddSingleton<IPermissionSource, AssessmentsPermissionSource>();
builder.Services.AddSingleton<IIntegrationEventMapper, CoreEventMapper>();
builder.Services.AddSingleton<IIntegrationEventMapper, AssessmentsEventMapper>();
builder.Services.AddScoped<IOutboxStore, IdentityOutboxStore>();
builder.Services.AddScoped<IOutboxStore, CoreOutboxStore>();
builder.Services.AddScoped<IOutboxStore, AssessmentsOutboxStore>();
builder.Services.AddScoped<AccessService>();
builder.Services.AddScoped<IPermissionChecker>(provider => provider.GetRequiredService<AccessService>());
builder.Services.AddScoped<IRoleAssignments>(provider => provider.GetRequiredService<AccessService>());
builder.Services.AddScoped<IUserLookup>(provider => provider.GetRequiredService<AccessService>());
builder.Services.AddScoped<IIdempotencyStore, IdempotencyStore>();
builder.Services.AddScoped<IApplicationReader, CoreApplicationReader>();
builder.Services.AddScoped<IStartupSync, IdentityStartupSync>();
builder.Services.AddScoped<IStartupSync, StageCatalogueSync>();
builder.Services.AddScoped<AttemptFinalizer>();
builder.Services.AddScoped<IMaintenanceJob, CloseOverdueAttemptsJob>();
builder.Services.AddScoped<IMaintenanceJob, ExpireInvitationsJob>();
builder.Services.AddHostedService<StartupSyncHostedService>();

switch (builder.Configuration["Cache:Provider"])
{
    case "Valkey":
        builder.Services.AddValkeyCache(builder.Configuration["Cache:Valkey:ConnectionString"] ?? "localhost:6379");
        break;
    default:
        builder.Services.AddMemoryCacheStore();
        break;
}

switch (builder.Configuration["Storage:Provider"])
{
    case "S3":
        builder.Services.AddS3FileStore(
            builder.Configuration["Storage:S3:ServiceUrl"],
            builder.Configuration["Storage:S3:Bucket"] ?? "shortlist",
            builder.Configuration["Storage:S3:Region"] ?? "us-east-1",
            builder.Configuration["Storage:S3:AccessKey"] ?? "",
            builder.Configuration["Storage:S3:SecretKey"] ?? "",
            builder.Configuration.GetValue("Storage:S3:ForcePathStyle", true));
        break;
    case "AzureBlob":
        builder.Services.AddAzureBlobFileStore(
            builder.Configuration["Storage:AzureBlob:ConnectionString"] ?? "",
            builder.Configuration["Storage:AzureBlob:Container"] ?? "shortlist");
        break;
    default:
        builder.Services.AddFileSystemFileStore(
            builder.Configuration["Storage:FileSystem:Root"] ?? "data/files",
            builder.Configuration["Storage:FileSystem:PublicBaseUrl"] ?? "http://localhost:5080/api/core/v1/files");
        break;
}

if (builder.Configuration["Email:Provider"] == "Smtp")
{
    builder.Services.AddSmtpEmail(
        builder.Configuration["Email:Smtp:Host"] ?? "localhost",
        builder.Configuration.GetValue("Email:Smtp:Port", 25),
        builder.Configuration["Email:Smtp:User"],
        builder.Configuration["Email:Smtp:Password"],
        builder.Configuration.GetValue("Email:Smtp:UseTls", true),
        builder.Configuration["Email:From"] ?? "noreply@shortlist.local");
}
else
{
    builder.Services.AddLogEmail();
}

if (builder.Configuration["Pdf:Provider"] == "Playwright")
{
    builder.Services.AddPlaywrightPdf();
}
else
{
    builder.Services.AddHtmlPdf();
}

builder.Services.AddShortlistQuartz(builder.Configuration);
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");
builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation())
    .WithMetrics(metrics => metrics.AddAspNetCoreInstrumentation());

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(options => options.AddPolicy("dev", policy =>
        policy.WithOrigins("http://localhost:4200").AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
}

var app = builder.Build();
app.UseShortlistExceptions();
app.UseSerilogRequestLogging();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'";
    await next();
});
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
if (app.Environment.IsDevelopment())
{
    app.UseCors("dev");
}

app.UseRateLimiter();
app.UseAuthentication();
app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/api"))
    {
        await next();
        return;
    }

    var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
    var unsafeMethod = HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsPut(context.Request.Method) || HttpMethods.IsPatch(context.Request.Method) || HttpMethods.IsDelete(context.Request.Method);
    var metadata = context.GetEndpoint()?.Metadata.GetMetadata<IAntiforgeryMetadata>();
    if (unsafeMethod && metadata is not { RequiresValidation: false })
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await Results.Problem(title: "Antiforgery validation failed.", statusCode: StatusCodes.Status400BadRequest, extensions: new Dictionary<string, object?> { ["code"] = "antiforgery.invalid" }).ExecuteAsync(context);
            return;
        }
    }

    var tokens = antiforgery.GetAndStoreTokens(context);
    if (!string.IsNullOrEmpty(tokens.RequestToken))
    {
        context.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken, new CookieOptions
        {
            HttpOnly = false,
            SameSite = SameSiteMode.Lax,
            Secure = context.Request.IsHttps,
            Path = "/"
        });
    }

    await next();
});
app.UseAuthorization();

app.MapOpenApi();
app.MapScalarApiReference();
app.MapHealthChecks("/health");
var exposeDevMailbox = app.Configuration["Email:Provider"] != "Smtp";
if (builder.Configuration.GetValue("Modules:Identity:Enabled", true))
{
    IdentityEndpoints.Map(app.MapGroup("/api/identity"), exposeDevMailbox);
}

if (builder.Configuration.GetValue("Modules:Core:Enabled", true))
{
    CoreEndpoints.Map(app.MapGroup("/api/core"));
}

if (builder.Configuration.GetValue("Modules:Assessments:Enabled", true))
{
    AssessmentEndpoints.Map(app.MapGroup("/api/assessments"));
}

app.MapFallback(async context =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    var index = Path.Combine(app.Environment.WebRootPath ?? "", "index.html");
    if (File.Exists(index))
    {
        context.Response.ContentType = "text/html";
        await context.Response.SendFileAsync(index);
        return;
    }

    context.Response.StatusCode = StatusCodes.Status404NotFound;
});

app.Run();

static void AddContext<TContext>(IServiceCollection services, string connectionString) where TContext : DbContext
{
    services.AddDbContext<TContext>((provider, options) =>
    {
        Postgres.UseShortlist(options, connectionString);
        options.AddInterceptors(provider.GetRequiredService<ModuleSaveChangesInterceptor>());
    });
    services.AddScoped<ITransactionalStore>(provider => new DbContextTransaction(provider.GetRequiredService<TContext>()));
}

public sealed class DatabaseHealthCheck(IdentityDbContext identity, CoreDbContext core, AssessmentsDbContext assessments) : Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck
{
    public async Task<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult> CheckHealthAsync(Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var ok = await identity.Database.CanConnectAsync(cancellationToken)
            && await core.Database.CanConnectAsync(cancellationToken)
            && await assessments.Database.CanConnectAsync(cancellationToken);
        return ok
            ? Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy()
            : Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("A database connection failed.");
    }
}

public sealed class StartupSyncHostedService(IServiceProvider services, IHostEnvironment environment) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        if (environment.IsEnvironment("Test"))
        {
            await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync(cancellationToken);
            await scope.ServiceProvider.GetRequiredService<CoreDbContext>().Database.MigrateAsync(cancellationToken);
            await scope.ServiceProvider.GetRequiredService<AssessmentsDbContext>().Database.MigrateAsync(cancellationToken);
        }

        foreach (var sync in scope.ServiceProvider.GetServices<IStartupSync>().OrderBy(item => item.Order))
        {
            await sync.SyncAsync(cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public partial class Program;
