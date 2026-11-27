using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Services.Invites;

namespace Jellyfin.Plugin.Necto.Api.Invites;

/// <summary>
/// What a member's invite page shows.
/// </summary>
/// <param name="MayCreate">Whether the member's groups allow invites.</param>
/// <param name="MaxInvites">How many open invites the member may have. 0 is no limit.</param>
/// <param name="Groups">The groups the member's invites may join.</param>
/// <param name="LinkUrl">The start of invite links, or empty for the address the page is open at.</param>
/// <param name="Invites">The member's invites.</param>
public sealed record MemberInvitesView(bool MayCreate, int MaxInvites, IReadOnlyList<InviteGroupView> Groups, string LinkUrl, IReadOnlyList<InviteSummary> Invites);
