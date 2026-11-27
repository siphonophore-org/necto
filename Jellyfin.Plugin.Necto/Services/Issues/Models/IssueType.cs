using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Services.Issues;

namespace Jellyfin.Plugin.Necto.Services.Issues.Models;

/// <summary>
/// What a reporter says is wrong with an item.
/// </summary>
public enum IssueType
{
    /// <summary>The picture.</summary>
    Video = 0,

    /// <summary>The sound.</summary>
    Audio = 1,

    /// <summary>The subtitles.</summary>
    Subtitles = 2,

    /// <summary>The file is the wrong item, or its details are wrong.</summary>
    Metadata = 3,

    /// <summary>It does not play at all.</summary>
    WontPlay = 4,

    /// <summary>Anything else, which the reporter's note explains.</summary>
    Other = 5
}
