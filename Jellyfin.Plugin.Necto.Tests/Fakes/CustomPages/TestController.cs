using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Necto.Api.CustomPages;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Services.CustomPages;
using Jellyfin.Plugin.Necto.Services.Groups;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Tests.Fakes.CustomPages;

/// <summary>
/// Builds the Custom pages controller over stand-ins, with a caller a test picks and groups from a configuration.
/// </summary>
public static class TestController
{
    /// <summary>
    /// Makes a user with an id, as an administrator or not.
    /// </summary>
    public static User User(Guid id, bool admin = false)
    {
        var user = new User("caller", "Default", "Default") { Id = id };
        user.SetPermission(PermissionKind.IsAdministrator, admin);
        return user;
    }

    /// <summary>
    /// Builds an authorization stand-in that answers with the given caller, or throws the given exception.
    /// </summary>
    public static IAuthorizationContext Authorization(User? user, bool isApiKey = false, Exception? failure = null)
    {
        var (auth, stub) = InterfaceStub.Create<IAuthorizationContext>();
        stub.Answer("GetAuthorizationInfo", _ => failure is null
            ? Task.FromResult(new AuthorizationInfo { User = user, IsApiKey = isApiKey })
            : Task.FromException<AuthorizationInfo>(failure));
        return auth;
    }

    /// <summary>
    /// Builds the administrator controller over stores and a configuration, with an empty request and response.
    /// </summary>
    public static PagesController CreateAdmin(IStoreService? stores = null, PluginConfiguration? config = null)
    {
        var controller = new PagesController(stores ?? new RecordingStoreService(), new FakeConfigurationStore(config ?? new PluginConfiguration()))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.Response.Body = new MemoryStream();
        return controller;
    }

    /// <summary>
    /// Builds the controller with an empty request and response.
    /// </summary>
    public static CustomPagesController Create(
        IPageService? pages = null,
        IStoreService? stores = null,
        IAuthorizationContext? authorization = null,
        IHttpClientFactory? http = null,
        IServerApplicationPaths? paths = null,
        PluginConfiguration? config = null,
        SecretProtector? secrets = null)
    {
        var store = new FakeConfigurationStore(config ?? new PluginConfiguration());
        var users = new FakeUsers();
        var serverPaths = paths ?? InterfaceStub.Create<IServerApplicationPaths>().Proxy;
        var controller = new CustomPagesController(
            pages ?? new StubPageService(),
            stores ?? new RecordingStoreService(),
            authorization ?? Authorization(null),
            http ?? new StubHttpClientFactory(),
            serverPaths,
            new PermissionService(store, users.Manager),
            secrets ?? new SecretProtector("Necto.Tests", NullLogger.Instance))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.Response.Body = new MemoryStream();
        return controller;
    }
}
