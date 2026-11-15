using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shortlist.BuildingBlocks.Abstractions;
using Shortlist.BuildingBlocks.Infrastructure;
using Shortlist.Modules.Assessments.Infrastructure;
using Shortlist.Modules.Core.Infrastructure;
using Shortlist.Modules.Identity.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);
var connectionString = builder.Configuration["ConnectionStrings:Database"]
    ?? "Host=127.0.0.1;Port=5432;Database=shortlist;Username=shortlist;Password=shortlist";
builder.Services.AddSingleton<ITenantContext>(new FixedTenant(Guid.Parse(builder.Configuration["Workspace:Id"] ?? "019a0000-0000-7000-8000-000000000001")));
builder.Services.AddDbContext<IdentityDbContext>(options => Postgres.UseShortlist(options, connectionString));
builder.Services.AddDbContext<CoreDbContext>(options => Postgres.UseShortlist(options, connectionString));
builder.Services.AddDbContext<AssessmentsDbContext>(options => Postgres.UseShortlist(options, connectionString));

var app = builder.Build();
await using var scope = app.Services.CreateAsyncScope();
await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
await scope.ServiceProvider.GetRequiredService<CoreDbContext>().Database.MigrateAsync();
await scope.ServiceProvider.GetRequiredService<AssessmentsDbContext>().Database.MigrateAsync();
Console.WriteLine("Migrations applied.");

internal sealed class FixedTenant(Guid id) : ITenantContext
{
    public Guid WorkspaceId => id;
}
