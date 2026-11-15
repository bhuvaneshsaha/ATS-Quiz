using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shortlist.BuildingBlocks.Abstractions;
using StackExchange.Redis;

namespace Shortlist.Adapters.Cache.Valkey;

public sealed class ValkeyCacheStore(IConnectionMultiplexer multiplexer) : ICacheStore
{
    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken)
    {
        var value = await multiplexer.GetDatabase().StringGetAsync(key);
        return value.IsNullOrEmpty ? default : JsonSerializer.Deserialize<T>(value.ToString());
    }

    public Task SetAsync<T>(string key, T value, TimeSpan lifetime, CancellationToken cancellationToken) =>
        multiplexer.GetDatabase().StringSetAsync(key, JsonSerializer.Serialize(value), lifetime);

    public Task RemoveAsync(string key, CancellationToken cancellationToken) =>
        multiplexer.GetDatabase().KeyDeleteAsync(key);
}

public static class ValkeyRegistration
{
    public static IServiceCollection AddValkeyCache(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(connectionString));
        services.AddSingleton<ICacheStore, ValkeyCacheStore>();
        return services;
    }
}
