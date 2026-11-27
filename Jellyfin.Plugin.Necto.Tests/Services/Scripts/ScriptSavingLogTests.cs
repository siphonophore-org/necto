using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Configuration.Scripts;
using Jellyfin.Plugin.Necto.Services.Scripts;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Scripts;

/// <summary>
/// Once the server starts, a configuration save writes vault and key ring problems to the server log.
/// </summary>
[Collection(nameof(ScriptSavingLogTests))]
public sealed class ScriptSavingLogTests
{
    /// <summary>
    /// A save after the start writes a vault it cannot read to the server log.
    /// True: an administrator sees in the log why a script secret was not kept.
    /// False: a broken vault is replaced with no trace.
    /// </summary>
    [Fact]
    public async Task Start_SendsSaveProblemsToTheServerLog()
    {
        using var folder = new TempFolder();
        File.WriteAllText(folder.File("Necto.Scripts.Secrets.json"), "{ not json");
        var (paths, stub) = InterfaceStub.Create<IApplicationPaths>();
        stub.Answer("get_PluginConfigurationsPath", _ => folder.Path);
        var logger = new RecordingLogger<ScriptSavingLog>();
        try
        {
            await new ScriptSavingLog(logger).StartAsync(CancellationToken.None);

            ScriptSettingsSaving.Saving(new ScriptSettings(), null, paths);

            Assert.Same(logger, ScriptSettingsSaving.Logger);
            Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("Necto.Scripts.Secrets.json", System.StringComparison.Ordinal));
        }
        finally
        {
            ScriptSettingsSaving.Logger = NullLogger.Instance;
        }
    }
}
