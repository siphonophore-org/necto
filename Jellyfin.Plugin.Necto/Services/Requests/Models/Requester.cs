using System;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>
/// Someone who has made requests, for filtering the dashboard's list by requester.
/// </summary>
/// <param name="UserId">Their user id.</param>
/// <param name="UserName">Their name, as on their latest request.</param>
/// <param name="Count">How many requests of theirs there are.</param>
public record Requester(Guid UserId, string UserName, int Count);
