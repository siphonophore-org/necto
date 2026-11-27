using Jellyfin.Plugin.Necto.Services.WireGuard;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.WireGuard;

public class TunnelErrorTests
{
    [Fact]
    public void Plain_RewordsAPortInUse()
    {
        Assert.Equal(
            "Port 51820 is already in use by another program.",
            TunnelError.Plain("wireguard: IPC error -48: failed to set listen_port: listen udp4 :51820: bind: address already in use"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("handshake did not complete")]
    public void Plain_KeepsOtherErrors(string? error)
    {
        Assert.Equal(error, TunnelError.Plain(error));
    }
}
