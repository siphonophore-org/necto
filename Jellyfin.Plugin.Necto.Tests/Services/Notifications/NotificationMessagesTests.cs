using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration.Notifications;
using Jellyfin.Plugin.Necto.Services.Notifications;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Notifications;

/// <summary>
/// An event is written once as a message every channel fills in its own way, with an absolute link when the server's
/// address is set.
/// </summary>
public class NotificationMessagesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A relative address is joined to the server's address, and an absolute one is kept.
    /// True: Discord and email get a link they can open.
    /// False: a link would point at nothing outside the server.
    /// </summary>
    [Theory]
    [InlineData("web/#/details?id=1", "https://media.example.com", "https://media.example.com/web/#/details?id=1")]
    [InlineData("/invites", "https://media.example.com/", "https://media.example.com/invites")]
    [InlineData("https://other.example.com/x", "https://media.example.com", "https://other.example.com/x")]
    [InlineData("invites", "", "invites")]
    [InlineData(null, "https://media.example.com", "https://media.example.com/web/")]
    [InlineData(null, "", null)]
    [InlineData("javascript:alert(1)", "https://media.example.com", null)]
    public void Link_JoinsTheServerAddress(string? url, string server, string? expected)
    {
        Assert.Equal(expected, NotificationMessages.Link(url, server));
    }

    /// <summary>
    /// The message carries the event's words as placeholders, its level's color, its key, and its data.
    /// True: Discord escapes the event's text, and a webhook gets the event's key and data.
    /// False: an event's text could break a post, or a webhook could not tell events apart.
    /// </summary>
    [Fact]
    public void Build_CarriesTheEventAsValuesAndData()
    {
        var e = new NectoEvent("invites.locked", "Invite locked", "Too many PINs") { Level = NectoEventLevel.Warning, Data = new Dictionary<string, string> { ["inviteId"] = "abc" } };

        var message = NotificationMessages.Build(e, new NotificationSettings { ServerUrl = "https://media.example.com" }, "Invite locked", Now);

        Assert.Equal("{title}", message.Title);
        Assert.Equal("Invite locked", message.Values["title"]);
        Assert.Equal("Too many PINs", message.Values["body"]);
        Assert.Equal(NotificationMessages.WarningColor, message.Color);
        Assert.Equal("invites.locked", message.Data["event"]!.GetValue<string>());
        Assert.Equal("abc", message.Data["inviteId"]!.GetValue<string>());
        Assert.Equal("https://media.example.com/web/", message.LinkUrl);
        Assert.Equal(Now, message.Timestamp);
    }

    /// <summary>
    /// The level picks the embed's color.
    /// True: a failure reads red and a warning orange in Discord.
    /// False: every event would look the same.
    /// </summary>
    [Theory]
    [InlineData(NectoEventLevel.Info, NotificationMessages.InfoColor)]
    [InlineData(NectoEventLevel.Warning, NotificationMessages.WarningColor)]
    [InlineData(NectoEventLevel.Error, NotificationMessages.ErrorColor)]
    public void Color_FollowsTheLevel(NectoEventLevel level, int color)
    {
        Assert.Equal(color, NotificationMessages.Color(level));
    }

    /// <summary>
    /// An email's subject is the title on one line, and its body ends with the link.
    /// True: an email reads well in any client and opens the event's page.
    /// False: a title with a line break could inject headers, or the link would be missing.
    /// </summary>
    [Fact]
    public void Email_TitleIsOneLine_AndBodyEndsWithTheLink()
    {
        var e = new NectoEvent("invites.used", "Invite\r\nused", "viewer joined") { Url = "invites" };

        var (subject, body) = NotificationMessages.Email(e, new NotificationSettings { ServerUrl = "https://media.example.com" });

        Assert.DoesNotContain('\n', subject);
        Assert.DoesNotContain('\r', subject);
        Assert.Equal("viewer joined\n\nhttps://media.example.com/invites", body);
    }

    /// <summary>
    /// The event's hold, one device, and image reach the message.
    /// True: a reminder shows on one app, a dated one is not held, and a poster shows in Discord and the browser.
    /// False: every message would be held and shown everywhere, with no image.
    /// </summary>
    [Fact]
    public void Build_CarriesHoldOneDeviceAndImage()
    {
        var e = new NectoEvent("requests.aging", "Waiting", string.Empty) { Hold = false, OneDevice = true, ImageUrl = "https://example.com/p.jpg" };

        var message = NotificationMessages.Build(e, null, "Waiting", Now);

        Assert.False(message.Hold);
        Assert.True(message.OneDevice);
        Assert.Equal("https://example.com/p.jpg", message.ThumbnailUrl);
    }
}
