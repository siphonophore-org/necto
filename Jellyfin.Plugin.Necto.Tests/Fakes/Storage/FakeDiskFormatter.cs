using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Services.Storage;

namespace Jellyfin.Plugin.Necto.Tests.Fakes.Storage;

/// <summary>
/// Stands in for the OS's formatter, giving the result a test sets and recording each disk it was asked to erase.
/// </summary>
public sealed class FakeDiskFormatter : IDiskFormatter
{
    /// <inheritdoc />
    public IReadOnlyList<string> FileSystems { get; set; } = Array.Empty<string>();

    /// <inheritdoc />
    public string? MountRoot { get; set; }

    /// <inheritdoc />
    public bool DashesForSpaces { get; set; } = true;

    /// <summary>Gets or sets what each format gives.</summary>
    public FormatResult Result { get; set; } = new(true, null);

    /// <summary>Gets every format asked for, in order.</summary>
    public List<(RawDisk Disk, string Name, string FileSystem)> Calls { get; } = new();

    /// <inheritdoc />
    public FormatResult Format(RawDisk disk, string name, string fileSystem)
    {
        Calls.Add((disk, name, fileSystem));
        return Result;
    }
}
