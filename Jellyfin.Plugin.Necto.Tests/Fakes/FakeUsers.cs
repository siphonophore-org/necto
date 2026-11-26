using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Users;

namespace Jellyfin.Plugin.Necto.Tests.Fakes;

/// <summary>
/// A stand-in for Jellyfin's user manager that keeps real accounts, policies, and activity dates, so a test follows
/// what a service does to each user.
/// </summary>
public sealed class FakeUsers
{
    public const string DefaultProvider = "Jellyfin.Server.Implementations.Users.DefaultAuthenticationProvider";

    public FakeUsers()
    {
        var (manager, stub) = InterfaceStub.Create<IUserManager>();
        Manager = manager;
        Stub = stub;
        stub.Answer("GetUsers", _ => Users.Values.ToList());
        stub.Answer("get_Users", _ => Users.Values.ToList());
        stub.Answer("GetUserById", args => Users.GetValueOrDefault((Guid)args[0]!));
        stub.Answer("GetUserDto", args =>
        {
            var user = (User)args[0]!;
            return new UserDto
            {
                Id = user.Id,
                Name = user.Username,
                Policy = Copy(Policies[user.Id]),
                LastActivityDate = LastActivity.GetValueOrDefault(user.Id),
                PrimaryImageTag = user.Id.ToString("N")[..6]
            };
        });
        stub.Answer("UpdatePolicyAsync", args =>
        {
            var id = (Guid)args[0]!;
            var policy = Copy((UserPolicy)args[1]!);
            Policies[id] = policy;
            var user = Users[id];
            user.AuthenticationProviderId = policy.AuthenticationProviderId;
            user.SetPermission(PermissionKind.IsDisabled, policy.IsDisabled);
            user.SetPermission(PermissionKind.IsAdministrator, policy.IsAdministrator);
            return Task.CompletedTask;
        });
        stub.Answer("DeleteUserAsync", args =>
        {
            var id = (Guid)args[0]!;
            Users.Remove(id);
            Deleted.Add(id);
            return Task.CompletedTask;
        });
    }

    public IUserManager Manager { get; }

    public InterfaceStub Stub { get; }

    public Dictionary<Guid, User> Users { get; } = new();

    public Dictionary<Guid, UserPolicy> Policies { get; } = new();

    public Dictionary<Guid, DateTime?> LastActivity { get; } = new();

    public List<Guid> Deleted { get; } = new();

    public User Add(string name, bool admin = false, DateTime? lastActivity = null, string provider = DefaultProvider)
    {
        var user = new User(name, provider, "Jellyfin.Server.Implementations.Users.DefaultPasswordResetProvider") { Id = Guid.NewGuid() };
        user.SetPermission(PermissionKind.IsAdministrator, admin);
        Users[user.Id] = user;
        Policies[user.Id] = new UserPolicy { IsAdministrator = admin, AuthenticationProviderId = provider };
        LastActivity[user.Id] = lastActivity;
        return user;
    }

    public bool IsDisabled(Guid id) => Policies[id].IsDisabled;

    private static UserPolicy Copy(UserPolicy policy) => JsonSerializer.Deserialize<UserPolicy>(JsonSerializer.Serialize(policy))!;
}
