using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Configuration.Notifications;
using Jellyfin.Plugin.Necto.Services.Notifications;

namespace Jellyfin.Plugin.Necto.Tests.Fakes;

/// <summary>
/// Keeps every email in place of an SMTP server, and refuses one when told to.
/// </summary>
public sealed class FakeEmail : IEmailSender
{
    public List<(string Password, IReadOnlyList<string> To, string Subject, string Body)> Sent { get; } = new();

    public Exception? Failure { get; set; }

    public Task SendAsync(SmtpSettings smtp, string password, IReadOnlyList<string> to, string subject, string body, CancellationToken cancellationToken)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        Sent.Add((password, to, subject, body));
        return Task.CompletedTask;
    }
}
