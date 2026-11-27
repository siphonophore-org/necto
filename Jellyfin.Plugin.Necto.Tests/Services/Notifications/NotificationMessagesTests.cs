using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Jellyfin.Plugin.Necto.Configuration.Notifications;
using Jellyfin.Plugin.Necto.Services.Notifications;
using Siphonophore.Stolon;
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

    /// <summary>
    /// An event's own tag replaces the key's, and an event without one is tagged by its key.
    /// True: a newer update about one request replaces only that request's notification.
    /// False: every new request would replace the last one on a device.
    /// </summary>
    [Fact]
    public void Build_Tag_IsTheEventsOrItsKeys()
    {
        var tagged = new NectoEvent("requests.created", "New", string.Empty) { Tag = "requests-1" };

        Assert.Equal("requests-1", NotificationMessages.Build(tagged, null, "New", Now).Tag);
        Assert.Equal("necto-requests.created", NotificationMessages.Build(tagged with { Tag = null }, null, "New", Now).Tag);
    }

    /// <summary>
    /// A webhook post carries the fields its channel shows, as keys, null when empty, the poster and the whole request,
    /// and leaves out what the channel turns off, while placeholders only a custom body reads stay out of it.
    /// True: a webhook post carries what the old Requests posts did, and nothing it turned off.
    /// False: posts would carry everything, or nothing beyond the title.
    /// </summary>
    [Fact]
    public void Post_Webhook_CarriesTheFieldsItsChannelShows()
    {
        var channel = new NotificationChannel { Kind = NotificationChannelKind.Webhook, Url = "https://hooks.example.com", ShowSeasons = false, ShowJoined = true };

        var body = JsonNode.Parse(StolonNotifier.Preview(channel, NotificationMessages.Post(Request(), channel, null, "New request", Now)).Body)!.AsObject();

        Assert.Equal("Please", (string?)body["note"]);
        Assert.Null(body["joined"]);
        Assert.True(body.ContainsKey("joined"));
        Assert.False(body.ContainsKey("seasons"));
        Assert.Equal("https://example.com/p.jpg", (string?)body["poster"]);
        Assert.Equal("Movie", (string?)body["request"]!["Type"]);
        Assert.False(body.ContainsKey("secret"));
        Assert.NotNull(body["time"]);
    }

    /// <summary>
    /// A post that leaves the requester out sends the event told without naming them, and one that shows them names them.
    /// True: Requester off, the default, keeps names out of Discord, and on, a post says who asked.
    /// False: every post would name who asked.
    /// </summary>
    [Fact]
    public void Post_Requester_PicksTheNamedOrAnonymousEvent()
    {
        var hidden = new NotificationChannel { Kind = NotificationChannelKind.Discord, Url = "https://discord.com/api/webhooks/1/x" };
        var shown = new NotificationChannel { Kind = NotificationChannelKind.Discord, Url = "https://discord.com/api/webhooks/1/x", ShowRequester = true };

        var anonymous = NotificationMessages.Post(Request(), hidden, null, "New request", Now);
        var named = NotificationMessages.Post(Request(), shown, null, "New request", Now);

        Assert.Equal("Someone requested Dune.", anonymous.Values["body"]);
        Assert.DoesNotContain(anonymous.Fields, f => f.Name == "Requester");
        Assert.Equal("viewer requested Dune.", named.Values["body"]);
        Assert.Contains(named.Fields, f => f.Name == "Requester" && f.Value == "viewer");
        Assert.Equal("Someone requested Dune.", NotificationMessages.Email(NotificationMessages.For(Request(), new NotificationChannel { Kind = NotificationChannelKind.Email }), null).Body);
    }

    /// <summary>
    /// A Discord post shows the summary under its text, the rest as fields, and drops the poster and time when off.
    /// True: a Discord embed reads as the old Requests one did, and its switches work.
    /// False: the summary would be a field, or a poster would show with Poster off.
    /// </summary>
    [Fact]
    public void Post_Discord_PutsTheSummaryInTheTextAndHonoursPosterAndTime()
    {
        var channel = new NotificationChannel { Kind = NotificationChannelKind.Discord, Url = "https://discord.com/api/webhooks/1/x", ShowPoster = false, ShowTime = false };

        var message = NotificationMessages.Post(Request(), channel, null, "New request", Now);

        Assert.Equal("{body}\n\n{overview}", message.Text);
        Assert.Null(message.ThumbnailUrl);
        Assert.Null(message.Timestamp);
        Assert.Contains(message.Fields, f => f.Name == "Notes" && f.Value == "Please" && !f.Inline);
        Assert.DoesNotContain(message.Fields, f => f.Name == "request");
    }

    /// <summary>
    /// A custom body gets every field and the poster as a placeholder, and the whole request as JSON, whatever the switches.
    /// True: a custom body can use {seasons}, {poster}, and {request} as the old Requests bodies did.
    /// False: a custom body would lose the request's values.
    /// </summary>
    [Fact]
    public void Post_CustomBody_GetsEveryField()
    {
        var channel = new NotificationChannel { Kind = NotificationChannelKind.Webhook, Url = "https://hooks.example.com", UseCustomBody = true, Body = "{\"n\":\"{note}\",\"p\":\"{poster}\",\"r\":{request}}", ShowNote = false };

        var body = JsonNode.Parse(StolonNotifier.Preview(channel, NotificationMessages.Post(Request(), channel, null, "New request", Now)).Body)!.AsObject();

        Assert.Equal("Please", (string?)body["n"]);
        Assert.Equal("https://example.com/p.jpg", (string?)body["p"]);
        Assert.Equal("Dune", (string?)body["r"]!["Title"]);
    }

    /// <summary>
    /// A channel shows each field its setting names, the requester off by default, and any field no setting names.
    /// True: a new channel posts what the old Requests channels did by default.
    /// False: a new channel would name requesters, or drop a tool's own fields.
    /// </summary>
    [Fact]
    public void Shows_FollowsTheChannelsSettings()
    {
        var channel = new NotificationChannel();

        Assert.True(channel.Shows(PostFields.Poster));
        Assert.True(channel.Shows(PostFields.Overview));
        Assert.False(channel.Shows(PostFields.User));
        Assert.False(channel.Shows(PostFields.Joined));
        Assert.True(channel.Shows(PostFields.Seasons));
        Assert.True(channel.Shows(PostFields.Note));
        Assert.True(channel.Shows(PostFields.Time));
        Assert.True(channel.Shows(PostFields.Request));
        Assert.True(channel.Shows(PostFields.Issue));
        Assert.True(channel.Shows("issueType"));
    }

    private static NectoEvent Request()
    {
        var fields = new[]
        {
            new NectoEventField(PostFields.Overview, "Overview", "Spice.", Inline: false) { InText = true },
            new NectoEventField(PostFields.User, "Requester", "viewer"),
            new NectoEventField(PostFields.Joined, "Joined", string.Empty),
            new NectoEventField(PostFields.Seasons, "Seasons", "Season 1"),
            new NectoEventField(PostFields.Note, "Notes", "Please", Inline: false),
            new NectoEventField(PostFields.Request, PostFields.Request, "{\"Title\":\"Dune\",\"Type\":\"Movie\"}") { Json = true }
        };
        var named = new NectoEvent("requests.created", "New request: Dune", "viewer requested Dune.")
        {
            UserId = Guid.NewGuid(),
            ImageUrl = "https://example.com/p.jpg",
            Values = new Dictionary<string, string> { ["secret"] = "kept for custom bodies" },
            Fields = fields
        };
        return named with
        {
            Anonymous = named with
            {
                UserId = null,
                Body = "Someone requested Dune.",
                Fields = fields.Select(f => f.Key == PostFields.User ? f with { Value = "Someone" } : f).ToArray()
            }
        };
    }
}
