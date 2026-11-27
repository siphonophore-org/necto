using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Configuration.Notifications;

namespace Jellyfin.Plugin.Necto.Services.Notifications;

/// <summary>
/// Sends one email through an SMTP server.
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Sends one email to several addresses.
    /// </summary>
    /// <param name="smtp">The server and the sender.</param>
    /// <param name="password">The server's password in plain text, or empty for none.</param>
    /// <param name="to">The addresses.</param>
    /// <param name="subject">The subject.</param>
    /// <param name="body">The text.</param>
    /// <param name="cancellationToken">Stops the send.</param>
    /// <returns>A task that completes once the server took the email. It throws when the server refused it.</returns>
    Task SendAsync(SmtpSettings smtp, string password, IReadOnlyList<string> to, string subject, string body, CancellationToken cancellationToken);
}
