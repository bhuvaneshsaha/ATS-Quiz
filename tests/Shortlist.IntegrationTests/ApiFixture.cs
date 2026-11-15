using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Shortlist.IntegrationTests;

public sealed class ShortlistApi : WebApplicationFactory<Program>
{
    public HttpClient Client { get; private set; } = null!;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = "Host=127.0.0.1;Port=5432;Database=shortlist_test;Username=shortlist;Password=shortlist",
                ["Workspace:Id"] = "019a0000-0000-7000-8000-000000000001",
                ["Seed:AdminEmail"] = "admin@shortlist.local",
                ["Seed:AdminPassword"] = "Change-Me-Admin-1",
                ["Seed:AdminDisplayName"] = "Avery Quinn",
                ["Email:Provider"] = "Log",
                ["Storage:Provider"] = "FileSystem",
                ["Storage:FileSystem:Root"] = Path.Combine(Path.GetTempPath(), "shortlist-tests"),
                ["Storage:FileSystem:PublicBaseUrl"] = "http://localhost:5080/api/core/v1/files",
                ["Cache:Provider"] = "Memory",
                ["Pdf:Provider"] = "Html",
                ["Jobs:OutboxSeconds"] = "1",
                ["Jobs:MaintenanceSeconds"] = "30",
                ["App:PublicBaseUrl"] = "http://localhost:5080"
            });
        });
    }

    public HttpClient CreateCookieClient()
    {
        Client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        return Client;
    }
}

public sealed class ApiSession(HttpClient client)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task SignIn(string email = "admin@shortlist.local", string password = "Change-Me-Admin-1")
    {
        var response = await client.PostAsJsonAsync("/api/identity/v1/auth/sign-in", new { email, password });
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Sign-in failed {(int)response.StatusCode}: {body}");
        }
    }

    public async Task<JsonElement> Send(HttpMethod method, string url, object? body = null, bool idempotent = false)
    {
        var token = await Token();
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-XSRF-TOKEN", token);
        if (idempotent)
        {
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: Json);
        }

        var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{method} {url} failed {(int)response.StatusCode}: {text}");
        }

        return string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    public Task<JsonElement> Post(string url, object body, bool idempotent = false) => Send(HttpMethod.Post, url, body, idempotent);

    public async Task<JsonElement> Get(string url)
    {
        var response = await client.GetAsync(url);
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"GET {url} failed {(int)response.StatusCode}: {text}");
        }

        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private async Task<string> Token()
    {
        var response = await client.GetAsync("/api/identity/v1/auth/xsrf");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("token").GetString() ?? throw new InvalidOperationException("Missing antiforgery token.");
    }
}
