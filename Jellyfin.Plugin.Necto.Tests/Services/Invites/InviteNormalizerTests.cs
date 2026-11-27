using System;
using Jellyfin.Plugin.Necto.Configuration.Invites;
using Jellyfin.Plugin.Necto.Services.Invites;
using Siphonophore.Stolon;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Invites;

/// <summary>
/// A whole configuration save keeps each invite's stored PIN, whatever it sends.
/// </summary>
public class InviteNormalizerTests
{
    /// <summary>
    /// The kept marker, or any other value a save sends, gives way to the stored PIN, and a new invite gets none.
    /// True: a page that read every PIN as the marker never wipes or replaces one.
    /// False: saving any page would turn every PIN into the marker, and no invitee could sign up.
    /// </summary>
    [Fact]
    public void KeepPins_PutsEachStoredPinBack()
    {
        var kept = new Invite { Id = Guid.NewGuid(), PinHash = "$PBKDF2$stored" };
        var stored = new[] { kept };
        var incoming = new[]
        {
            new Invite { Id = kept.Id, PinHash = SecretProtector.KeptSentinel },
            new Invite { Id = Guid.NewGuid(), PinHash = "$PBKDF2$sent" }
        };

        InviteNormalizer.KeepPins(incoming, stored);

        Assert.Equal("$PBKDF2$stored", incoming[0].PinHash);
        Assert.Empty(incoming[1].PinHash);
    }
}
