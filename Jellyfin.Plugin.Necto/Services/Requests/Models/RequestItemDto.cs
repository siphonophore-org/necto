using System;
using MediaBrowser.Model.Dto;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>
/// A request as Jellyfin describes an item, so any app that reads Jellyfin's items can read it: the library's own
/// item, as the viewer sees it, once the title is there, or the title as requested until then. Where Jellyfin leaves
/// them empty, its Status is the request's (Pending, Approved, Declined, Available, Failed or PartiallyAvailable) and
/// its CompletionPercentage the download's progress; Jellyfin's own values are never replaced. The request itself rides along
/// under <see cref="Request"/>, which apps that only know <see cref="BaseItemDto"/> leave alone.
/// </summary>
public class RequestItemDto : BaseItemDto
{
    /// <summary>Gets or sets the request's id, which is <see cref="BaseItemDto.Id"/> too until the title is in the library.</summary>
    public Guid RequestId { get; set; }

    /// <summary>Gets or sets the request, as the viewer may see it.</summary>
    public MediaRequest? Request { get; set; }
}
