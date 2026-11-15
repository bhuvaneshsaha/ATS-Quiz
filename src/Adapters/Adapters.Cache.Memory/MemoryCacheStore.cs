using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Shortlist.BuildingBlocks.Abstractions;

namespace Shortlist.Adapters.Cache.Memory;

public sealed class MemoryCacheStore : ICacheStore
{
    private readonly ConcurrentDictionary<string, (object? Value, DateTimeOffset Expires)> _entries = new();

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken)
    {
        if (_entries.TryGetValue(key, out var entry) && entry.Expires > DateTimeOffset.UtcNow && entry.Value is T value)
        {
            return Task.FromResult<T?>(value);
        }

        _entries.TryRemove(key, out _);
        return Task.FromResult<T?>(default);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        _entries[key] = (value, DateTimeOffset.UtcNow.Add(lifetime));
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        _entries.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}

public static class MemoryCacheRegistration
{
    public static IServiceCollection AddMemoryCacheStore(this IServiceCollection services)
    {
        services.AddSingleton<MemoryCacheStore>();
        services.AddSingleton<ICacheStore>(provider => provider.GetRequiredService<MemoryCacheStore>());
        return services;
    }
}
