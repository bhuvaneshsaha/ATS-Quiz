using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.DependencyInjection;
using Shortlist.BuildingBlocks.Abstractions;

namespace Shortlist.Adapters.Storage.S3;

public sealed class S3FileStore(IAmazonS3 client, string bucket) : IFileStore
{
    public async Task<StoredFile> SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken)
    {
        var response = await client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType
        }, cancellationToken);
        return new StoredFile(key, contentType, response.ContentLength);
    }

    public async Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        var response = await client.GetObjectAsync(bucket, key, cancellationToken);
        return response.ResponseStream;
    }

    public Task<Uri> GetReadUrlAsync(string key, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        var url = client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = bucket,
            Key = key,
            Expires = DateTime.UtcNow.Add(lifetime),
            Verb = HttpVerb.GET
        });
        return Task.FromResult(new Uri(url));
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken) =>
        client.DeleteObjectAsync(bucket, key, cancellationToken);
}

public static class S3Registration
{
    public static IServiceCollection AddS3FileStore(this IServiceCollection services, string? serviceUrl, string bucket, string region, string accessKey, string secretKey, bool forcePathStyle)
    {
        services.AddSingleton<IAmazonS3>(_ =>
        {
            var config = new AmazonS3Config
            {
                ForcePathStyle = forcePathStyle,
                AuthenticationRegion = region
            };
            if (!string.IsNullOrWhiteSpace(serviceUrl))
            {
                config.ServiceURL = serviceUrl;
            }
            else
            {
                config.RegionEndpoint = RegionEndpoint.GetBySystemName(region);
            }

            return new AmazonS3Client(new BasicAWSCredentials(accessKey, secretKey), config);
        });
        services.AddSingleton<IFileStore>(provider => new S3FileStore(provider.GetRequiredService<IAmazonS3>(), bucket));
        return services;
    }
}
