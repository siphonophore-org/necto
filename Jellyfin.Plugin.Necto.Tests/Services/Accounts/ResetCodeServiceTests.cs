using System;
using System.IO;
using Jellyfin.Plugin.Necto.Services.Accounts;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Accounts;

/// <summary>
/// The reset code reader reads the files Jellyfin's own reset provider writes, skips what does not parse, and marks
/// codes past their expiry.
/// </summary>
public sealed class ResetCodeServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "necto-resets-" + Guid.NewGuid().ToString("N"));

    public ResetCodeServiceTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    private ResetCodeService Service()
    {
        var (paths, stub) = InterfaceStub.Create<IApplicationPaths>();
        stub.Answer("get_ProgramDataPath", _ => _dir);
        return new ResetCodeService(paths, NullLogger<ResetCodeService>.Instance);
    }

    /// <summary>
    /// Files shaped as Jellyfin writes them are read, the latest expiry first.
    /// True: Troubleshooting lists each pending code with its user.
    /// False: an administrator would need file access to pass on a code.
    /// </summary>
    [Fact]
    public void ReadAll_ReadsJellyfinsFiles()
    {
        File.WriteAllText(Path.Combine(_dir, "passwordreset_abc.json"), "{\"Pin\":\"A1B2C3\",\"UserName\":\"viewer\",\"ExpirationDate\":\"2099-01-01T00:00:00Z\",\"PinFile\":\"/x\"}");
        File.WriteAllText(Path.Combine(_dir, "passwordreset_old.json"), "{\"Pin\":\"D4E5F6\",\"UserName\":\"guest\",\"ExpirationDate\":\"2001-01-01T00:00:00Z\"}");
        File.WriteAllText(Path.Combine(_dir, "unrelated.json"), "{}");

        var codes = Service().ReadAll();

        Assert.Equal(2, codes.Count);
        Assert.Equal(("viewer", "A1B2C3", false), (codes[0].UserName, codes[0].Pin, codes[0].Expired));
        Assert.Equal(("guest", true), (codes[1].UserName, codes[1].Expired));
    }

    /// <summary>
    /// A file that does not parse, or has no code, is skipped.
    /// True: one broken file never hides the others.
    /// False: a broken file would fail the whole list.
    /// </summary>
    [Fact]
    public void ReadAll_SkipsBrokenFiles()
    {
        File.WriteAllText(Path.Combine(_dir, "passwordreset_bad.json"), "not json");
        File.WriteAllText(Path.Combine(_dir, "passwordreset_nopin.json"), "{\"UserName\":\"viewer\"}");

        Assert.Empty(Service().ReadAll());
    }
}
