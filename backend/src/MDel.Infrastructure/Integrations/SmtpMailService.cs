using MailKit.Net.Smtp;
using MailKit.Security;
using MDel.Application.Abstractions;
using MDel.Domain;
using Microsoft.Extensions.Logging;
using MimeKit;
using AppMailMessage = MDel.Application.Abstractions.MailMessage;

namespace MDel.Infrastructure.Integrations;

/// <summary>
/// Trimitere email prin SMTP (Exchange: receive connector de relay intern sau client submission pe 587).
/// Utilizatorul SMTP este opțional – relay-ul anonim intern e frecvent în Exchange on-prem.
/// </summary>
public sealed class SmtpMailService(ISettingsService settings, ILogger<SmtpMailService> logger) : IMailService
{
    public async Task SendAsync(AppMailMessage message, CancellationToken ct)
    {
        var config = await LoadAsync(ct);
        if (string.IsNullOrWhiteSpace(config.From))
            throw new InvalidOperationException("SMTP is not configured: set the sender address in Administration.");

        var mime = new MimeMessage();
        mime.From.Add(MailboxAddress.Parse(config.From));
        foreach (var to in message.To.Distinct(StringComparer.OrdinalIgnoreCase)) mime.To.Add(MailboxAddress.Parse(to));
        foreach (var cc in message.Cc ?? []) mime.Cc.Add(MailboxAddress.Parse(cc));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.HtmlBody }.ToMessageBody();

        using var client = await ConnectAsync(config, ct);
        await client.SendAsync(mime, ct);
        await client.DisconnectAsync(quit: true, ct);

        logger.LogInformation("Mail sent to {Recipients}: {Subject}", string.Join(", ", message.To), message.Subject);
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct)
    {
        try
        {
            var config = await LoadAsync(ct);
            using var client = await ConnectAsync(config, ct);
            var tls = client.IsSecure ? "TLS" : "no TLS";
            var auth = client.IsAuthenticated ? $"authenticated as {config.User}" : "anonymous relay";
            await client.DisconnectAsync(quit: true, ct);
            return new ConnectionTestResult(true, $"Connected to {config.Host}:{config.Port} ({tls}, {auth}).");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SMTP connection test failed");
            return new ConnectionTestResult(false, $"SMTP connection failed: {ex.Message}");
        }
    }

    private static async Task<SmtpClient> ConnectAsync(SmtpConfig config, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.Host))
            throw new InvalidOperationException("SMTP is not configured: set the host in Administration.");

        var client = new SmtpClient { Timeout = 30_000 };
        try
        {
            var security = !config.UseTls
                ? SecureSocketOptions.None
                : config.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;

            await client.ConnectAsync(config.Host, config.Port, security, ct);
            if (!string.IsNullOrWhiteSpace(config.User))
            {
                await client.AuthenticateAsync(config.User, config.Password ?? string.Empty, ct);
            }

            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private async Task<SmtpConfig> LoadAsync(CancellationToken ct)
    {
        var user = await settings.GetAsync(SettingKeys.SmtpUser, ct);
        return new SmtpConfig(
            await settings.GetAsync(SettingKeys.SmtpHost, ct) ?? string.Empty,
            await settings.GetIntAsync(SettingKeys.SmtpPort, 25, ct),
            await settings.GetBoolAsync(SettingKeys.SmtpUseTls, true, ct),
            user,
            string.IsNullOrWhiteSpace(user) ? null : await settings.GetSecretAsync(SettingKeys.SmtpPassword, ct),
            await settings.GetAsync(SettingKeys.SmtpFromAddress, ct));
    }

    private sealed record SmtpConfig(string Host, int Port, bool UseTls, string? User, string? Password, string? From);
}
