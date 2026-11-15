using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Shortlist.BuildingBlocks.Abstractions;

namespace Shortlist.Adapters.Jobs.Quartz;

public sealed class OutboxDispatchJob(IServiceScopeFactory scopes) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<IEventBus>();
        foreach (var store in scope.ServiceProvider.GetServices<IOutboxStore>())
        {
            await store.DispatchBatchAsync(bus, cancellationToken);
        }
    }
}

public sealed class MaintenanceQuartzJob(IServiceScopeFactory scopes) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        foreach (var job in scope.ServiceProvider.GetServices<IMaintenanceJob>())
        {
            await job.RunAsync(cancellationToken);
        }
    }
}

public static class QuartzRegistration
{
    public static IServiceCollection AddShortlistQuartz(this IServiceCollection services, IConfiguration configuration)
    {
        var outboxSeconds = Math.Max(1, configuration.GetValue("Jobs:OutboxSeconds", 1));
        var maintenanceSeconds = Math.Max(5, configuration.GetValue("Jobs:MaintenanceSeconds", 60));
        services.AddQuartz(quartz =>
        {
            quartz.AddJob<OutboxDispatchJob>(job => job.WithIdentity("outbox"));
            quartz.AddTrigger(trigger => trigger.ForJob("outbox").WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromSeconds(outboxSeconds)).RepeatForever()));
            quartz.AddJob<MaintenanceQuartzJob>(job => job.WithIdentity("maintenance"));
            quartz.AddTrigger(trigger => trigger.ForJob("maintenance").WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromSeconds(maintenanceSeconds)).RepeatForever()));
        });
        services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
        return services;
    }
}
