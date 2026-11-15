using Microsoft.Extensions.DependencyInjection;
using Shortlist.BuildingBlocks.Abstractions;

namespace Shortlist.Adapters.Storage.FileSystem;

public sealed class FileSystemFileStore(string root, string publicBaseUrl) : IFileStore
{
    public async Task<StoredFile> SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken)
    {
        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = File.Create(path);
        await content.CopyToAsync(file, cancellationToken);
        return new StoredFile(key, contentType, file.Length);
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken) =>
        Task.FromResult<Stream>(File.OpenRead(PathFor(key)));

    public Task<Uri> GetReadUrlAsync(string key, TimeSpan lifetime, CancellationToken cancellationToken) =>
        Task.FromResult(new Uri($"{publicBaseUrl.TrimEnd('/')}/{key}"));

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        var path = PathFor(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string PathFor(string key)
    {
        var path = Path.GetFullPath(Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar)));
        var fullRoot = Path.GetFullPath(root);
        if (!path.StartsWith(fullRoot, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The file key escapes the store.");
        }

        return path;
    }
}

public static class FileSystemRegistration
{
    public static IServiceCollection AddFileSystemFileStore(this IServiceCollection services, string root, string publicBaseUrl)
    {
        Directory.CreateDirectory(root);
        services.AddSingleton<IFileStore>(_ => new FileSystemFileStore(root, publicBaseUrl));
        return services;
    }
}
