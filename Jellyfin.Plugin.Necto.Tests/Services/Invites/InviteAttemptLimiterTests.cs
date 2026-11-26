using System;
using Jellyfin.Plugin.Necto.Services.Invites;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Invites;

/// <summary>
/// The limiter in front of the signup page counts each address's calls per route in a sliding window, refuses past the
/// limit, and never tracks more addresses than its table holds.
/// </summary>
public class InviteAttemptLimiterTests
{
    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>
    /// Past the limit a signup is refused until the window passes.
    /// True: one address cannot try more than ten signups in fifteen minutes.
    /// False: a script could guess PINs and user names without end.
    /// </summary>
    [Fact]
    public void Redeem_OverTheLimit_IsRefusedUntilTheWindowPasses()
    {
        var clock = new ManualClock();
        var limiter = new InviteAttemptLimiter(clock);

        for (var i = 0; i < InviteAttemptLimiter.MaxRedeemAttempts; i++)
        {
            Assert.True(limiter.TryAcquire(InviteEndpoint.Redeem, "203.0.113.5"));
        }

        Assert.False(limiter.TryAcquire(InviteEndpoint.Redeem, "203.0.113.5"));
        Assert.True(limiter.RetryAfterSeconds(InviteEndpoint.Redeem, "203.0.113.5") > 0);

        clock.Now += InviteAttemptLimiter.Window;
        Assert.True(limiter.TryAcquire(InviteEndpoint.Redeem, "203.0.113.5"));
    }

    /// <summary>
    /// Each address and each route has its own budget.
    /// True: one visitor at the limit never blocks another, and opening the page never uses up signups.
    /// False: one busy visitor would lock everyone out.
    /// </summary>
    [Fact]
    public void Limits_ArePerClientAndPerEndpoint()
    {
        var limiter = new InviteAttemptLimiter(new ManualClock());
        for (var i = 0; i < InviteAttemptLimiter.MaxRedeemAttempts; i++)
        {
            limiter.TryAcquire(InviteEndpoint.Redeem, "203.0.113.5");
        }

        Assert.False(limiter.TryAcquire(InviteEndpoint.Redeem, "203.0.113.5"));
        Assert.True(limiter.TryAcquire(InviteEndpoint.Redeem, "203.0.113.6"));
        Assert.True(limiter.TryAcquire(InviteEndpoint.Info, "203.0.113.5"));
    }

    /// <summary>
    /// A refused call is not counted, so the oldest counted call frees one slot as it ages out.
    /// True: a visitor who keeps retrying gets back in once the window moves.
    /// False: retrying would keep a visitor locked out for as long as they try.
    /// </summary>
    [Fact]
    public void RefusedAttempts_AreNotCounted()
    {
        var clock = new ManualClock();
        var limiter = new InviteAttemptLimiter(clock);
        for (var i = 0; i < InviteAttemptLimiter.MaxRedeemAttempts; i++)
        {
            limiter.TryAcquire(InviteEndpoint.Redeem, "c");
            clock.Now += TimeSpan.FromSeconds(1);
        }

        for (var i = 0; i < 50; i++)
        {
            Assert.False(limiter.TryAcquire(InviteEndpoint.Redeem, "c"));
        }

        clock.Now = clock.Now - TimeSpan.FromSeconds(InviteAttemptLimiter.MaxRedeemAttempts) + InviteAttemptLimiter.Window + TimeSpan.FromMilliseconds(1);
        Assert.True(limiter.TryAcquire(InviteEndpoint.Redeem, "c"));
        Assert.False(limiter.TryAcquire(InviteEndpoint.Redeem, "c"));
    }

    /// <summary>
    /// The table holds at most its capacity, forgetting the address seen least recently.
    /// True: a flood of addresses cannot grow the server's memory without end.
    /// False: rotating addresses would fill memory.
    /// </summary>
    [Fact]
    public void Table_IsCappedAndForgetsTheLeastRecentlySeenClient()
    {
        var clock = new ManualClock();
        var limiter = new InviteAttemptLimiter(clock, capacity: 3);
        for (var i = 0; i < InviteAttemptLimiter.MaxRedeemAttempts; i++)
        {
            limiter.TryAcquire(InviteEndpoint.Redeem, "a");
        }

        Assert.False(limiter.TryAcquire(InviteEndpoint.Redeem, "a"));
        foreach (var c in new[] { "b", "c", "d" })
        {
            clock.Now += TimeSpan.FromSeconds(1);
            Assert.True(limiter.TryAcquire(InviteEndpoint.Redeem, c));
        }

        Assert.Equal(3, limiter.TrackedClients);
    }

    /// <summary>
    /// Addresses whose last call left the window are dropped as calls arrive.
    /// True: the table empties itself on a quiet server.
    /// False: every address ever seen would stay in memory.
    /// </summary>
    [Fact]
    public void ExpiredClients_AreDroppedAsRequestsArrive()
    {
        var clock = new ManualClock();
        var limiter = new InviteAttemptLimiter(clock);
        for (var i = 0; i < 500; i++)
        {
            limiter.TryAcquire(InviteEndpoint.Info, "10.0.0." + i);
        }

        clock.Now += InviteAttemptLimiter.Window + TimeSpan.FromSeconds(1);
        limiter.TryAcquire(InviteEndpoint.Info, "fresh");

        Assert.Equal(1, limiter.TrackedClients);
    }

    /// <summary>
    /// Addresses in one IPv6 /64 share one budget.
    /// True: a visitor cannot escape the limit by rotating through their own prefix.
    /// False: an IPv6 visitor would have no limit.
    /// </summary>
    [Fact]
    public void RotatingAddressesInOneIPv6Prefix_ShareOneBudget()
    {
        var limiter = new InviteAttemptLimiter(new ManualClock());
        for (var i = 1; i <= InviteAttemptLimiter.MaxRedeemAttempts; i++)
        {
            Assert.True(limiter.TryAcquire(InviteEndpoint.Redeem, ClientAddress.Key(System.Net.IPAddress.Parse("2001:db8::" + i.ToString("x", System.Globalization.CultureInfo.InvariantCulture)))));
        }

        Assert.False(limiter.TryAcquire(InviteEndpoint.Redeem, ClientAddress.Key(System.Net.IPAddress.Parse("2001:db8::ffff:1"))));
        Assert.True(limiter.TryAcquire(InviteEndpoint.Redeem, ClientAddress.Key(System.Net.IPAddress.Parse("2001:db8:0:1::1"))));
    }

    /// <summary>
    /// The proxy warning is claimed once.
    /// True: the server log warns about an untrusted proxy one time.
    /// False: every invite call would write the warning.
    /// </summary>
    [Fact]
    public void ClaimProxyWarning_IsTrueOnce()
    {
        var limiter = new InviteAttemptLimiter(new ManualClock());

        Assert.True(limiter.ClaimProxyWarning());
        Assert.False(limiter.ClaimProxyWarning());
    }
}
