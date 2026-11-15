using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.DependencyInjection;
using Shortlist.BuildingBlocks.Abstractions;

namespace Shortlist.Adapters.Storage.AzureBlob;

public sealed class AzureBlobFileStore(BlobContainerClient container) : IFileStore
{
    public async Task<StoredFile> SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken)
    {
        var blob = container.GetBlobClient(key);
        await blob.UploadAsync(content, new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } }, cancellationToken);
        return new StoredFile(key, contentType, content.Length);
    }

    public async Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        var blob = container.GetBlobClient(key);
        var response = await blob.DownloadStreamingAsync(cancellationToken: cancellationToken);
        return response.Value.Content;
    }

    public Task<Uri> GetReadUrlAsync(string key, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        var blob = container.GetBlobClient(key);
        var uri = blob.GenerateSasUri(BlobSasPermissions.Read, DateTimeOffset.UtcNow.Add(lifetime));
        return Task.FromResult(uri);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken) =>
        container.GetBlobClient(key).DeleteIfExistsAsync(cancellationToken: cancellationToken);
}

public static class AzureBlobRegistration
{
    public static IServiceCollection AddAzureBlobFileStore(this IServiceCollection services, string connectionString, string container)
    {
        services.AddSingleton<IFileStore>(_ =>
        {
            var client = new BlobContainerClient(connectionString, container);
            client.CreateIfNotExists();
            return new AzureBlobFileStore(client);
        });
        return services;
    }
}
