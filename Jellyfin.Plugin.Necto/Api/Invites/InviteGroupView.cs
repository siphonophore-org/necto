using System;

namespace Jellyfin.Plugin.Necto.Api.Invites;

/// <summary>
/// A group a member's invites may join.
/// </summary>
/// <param name="Id">The group's id. Everyone's means the account joins no group.</param>
/// <param name="Name">The group's name.</param>
public sealed record InviteGroupView(Guid Id, string Name);
