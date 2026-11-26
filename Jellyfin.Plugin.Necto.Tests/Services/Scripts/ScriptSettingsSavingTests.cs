using System;
using Jellyfin.Plugin.Necto.Configuration.Scripts;
using Jellyfin.Plugin.Necto.Services.Scripts;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Scripts;

/// <summary>
/// A configuration save fills in what a page leaves out of the scripts and refuses settings that could not run.
/// </summary>
public class ScriptSettingsSavingTests
{
    /// <summary>
    /// A new script gets an id and a save with no Scripts part takes the defaults.
    /// True: the page may leave out ids, and another tool's save still works.
    /// False: two new scripts share an empty id, or a save from another tool fails.
    /// </summary>
    [Fact]
    public void Saving_FillsInIdsAndDefaults()
    {
        var saved = ScriptSettingsSaving.Saving(new ScriptSettings { Scripts = { new ScriptDefinition { Name = " Notify " } } }, null, null);

        Assert.NotEqual(Guid.Empty, saved.Scripts[0].Id);
        Assert.Equal("Notify", saved.Scripts[0].Name);
        Assert.Equal(4, ScriptSettingsSaving.Saving(null, null, null).MaxConcurrentRuns);
    }

    /// <summary>
    /// A setting out of range refuses the save.
    /// True: the runner never starts with no slots.
    /// False: scripts never run.
    /// </summary>
    [Fact]
    public void Saving_RefusesOutOfRangeSettings()
        => Assert.Throws<ArgumentException>(() => ScriptSettingsSaving.Saving(new ScriptSettings { MaxConcurrentRuns = 0 }, null, null));
}
