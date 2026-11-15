using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Shortlist.BuildingBlocks.Abstractions;

namespace Shortlist.Adapters.Pdf.Playwright;

public sealed class HtmlPdfRenderer : IPdfRenderer
{
    public string ContentType => "text/html";
    public string FileExtension => "html";
    public Task<byte[]> RenderHtmlAsync(string html, CancellationToken cancellationToken) =>
        Task.FromResult(System.Text.Encoding.UTF8.GetBytes(html));
}

public sealed class PlaywrightPdfRenderer : IPdfRenderer
{
    public string ContentType => "application/pdf";
    public string FileExtension => "pdf";

    public async Task<byte[]> RenderHtmlAsync(string html, CancellationToken cancellationToken)
    {
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync();
        await page.SetContentAsync(html);
        return await page.PdfAsync();
    }
}

public static class PdfRegistration
{
    public static IServiceCollection AddHtmlPdf(this IServiceCollection services)
    {
        services.AddSingleton<IPdfRenderer, HtmlPdfRenderer>();
        return services;
    }

    public static IServiceCollection AddPlaywrightPdf(this IServiceCollection services)
    {
        services.AddSingleton<IPdfRenderer, PlaywrightPdfRenderer>();
        return services;
    }
}
