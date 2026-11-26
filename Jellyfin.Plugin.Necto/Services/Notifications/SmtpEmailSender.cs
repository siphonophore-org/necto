using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Configuration.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Necto.Services.Notifications;

/// <summary>
/// Sends email with .NET's SMTP client, encrypted with STARTTLS when the settings ask for it.
/// </summary>
[PluginService(ServiceLifetime.Singleton, ServiceType = typeof(IEmailSender))]
public sealed class SmtpEmailSender : IEmailSender
{
    /// <summary>How long a send may take before it is given up, in milliseconds.</summary>
    public const int TimeoutMs = 30000;

    /// <inheritdoc />
    public async Task SendAsync(SmtpSettings smtp, string password, IReadOnlyList<string> to, string subject, string body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(smtp);
        ArgumentNullException.ThrowIfNull(to);
        using var message = new MailMessage
        {
            From = new MailAddress(smtp.FromAddress, string.IsNullOrWhiteSpace(smtp.FromName) ? "Jellyfin" : smtp.FromName),
            Subject = subject ?? string.Empty,
            Body = body ?? string.Empty,
            SubjectEncoding = Encoding.UTF8,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = false
        };
        foreach (var address in to)
        {
            message.To.Add(new MailAddress(address));
        }

        using var client = new SmtpClient(smtp.Host, smtp.Port)
        {
            EnableSsl = smtp.UseTls,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = TimeoutMs
        };
        if (!string.IsNullOrEmpty(smtp.Username))
        {
            client.Credentials = new NetworkCredential(smtp.Username, password ?? string.Empty);
        }

        await client.SendMailAsync(message, cancellationToken).ConfigureAwait(false);
    }
}
