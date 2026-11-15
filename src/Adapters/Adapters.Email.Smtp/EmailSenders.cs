using System.Collections.Concurrent;
using MailKit.Net.Smtp;
using Microsoft.Extensions.DependencyInjection;
using MimeKit;
using Shortlist.BuildingBlocks.Abstractions;

namespace Shortlist.Adapters.Email.Smtp;

public sealed class LoggingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _messages = new();

    public IReadOnlyList<EmailMessage> Messages => _messages.ToArray();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        _messages.Enqueue(message);
        while (_messages.Count > 100 && _messages.TryDequeue(out _))
        {
        }

        return Task.CompletedTask;
    }
}

public sealed class SmtpEmailSender(string host, int port, string? user, string? password, bool useTls, string from) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var mime = new MimeMessage();
        mime.From.Add(MailboxAddress.Parse(from));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { TextBody = message.TextBody, HtmlBody = message.HtmlBody }.ToMessageBody();
        using var client = new SmtpClient();
        await client.ConnectAsync(host, port, useTls ? MailKit.Security.SecureSocketOptions.StartTls : MailKit.Security.SecureSocketOptions.None, cancellationToken);
        if (!string.IsNullOrEmpty(user))
        {
            await client.AuthenticateAsync(user, password ?? "", cancellationToken);
        }

        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}

public static class EmailRegistration
{
    public static IServiceCollection AddLogEmail(this IServiceCollection services)
    {
        services.AddSingleton<LoggingEmailSender>();
        services.AddSingleton<IEmailSender>(provider => provider.GetRequiredService<LoggingEmailSender>());
        return services;
    }

    public static IServiceCollection AddSmtpEmail(this IServiceCollection services, string host, int port, string? user, string? password, bool useTls, string from)
    {
        services.AddSingleton<IEmailSender>(_ => new SmtpEmailSender(host, port, user, password, useTls, from));
        return services;
    }
}
