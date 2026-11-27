using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Api.CustomPages;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Jellyfin.Plugin.Necto.Configuration.CustomPages;
using Jellyfin.Plugin.Necto.Services.CustomPages;
using Jellyfin.Plugin.Necto.Tests.Fakes.CustomPages;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Api.CustomPages;

/// <summary>
/// <c>POST /Pages/Configuration/Check</c> tells the Pages tool why a save would be refused, since Jellyfin
/// answers a refused save with no reason.
/// </summary>
public class ConfigurationCheckTests
{
    private static PagesController Create(string body)
    {
        var controller = TestController.CreateAdmin();
        controller.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        return controller;
    }

    private static async Task<JsonElement> Check(string body)
    {
        var result = await Create(body).CheckConfiguration(CancellationToken.None);
        var content = Assert.IsType<ContentResult>(result);
        return JsonDocument.Parse(content.Content!).RootElement;
    }

    // The shape the dashboard posts: Pascal cased, tiers as names.
    private static string Page(string slug, string routeName, string password)
        => "{\"Slug\":\"" + slug + "\",\"Visibility\":\"Anonymous\",\"Enabled\":true,\"Unsandboxed\":true,"
            + "\"ApiRoutes\":[{\"Name\":\"" + routeName + "\",\"Url\":\"http://nas:9091/rpc\",\"Password\":\"" + password + "\"}]}";

    [Fact]
    public async Task UnpairableKeptPassword_ReportsTheActualReason()
    {
        var json = await Check("{\"CustomPages\":{\"Pages\":[" + Page("tx", "rpc", Siphonophore.Stolon.SecretProtector.KeptSentinel) + "]}}");

        Assert.False(json.GetProperty("ok").GetBoolean());
        var message = json.GetProperty("message").GetString();
        Assert.Contains("rpc", message, System.StringComparison.Ordinal);
        Assert.Contains("Enter the password again", message, System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidConfiguration_IsAccepted()
    {
        var json = await Check("{\"CustomPages\":{\"Pages\":[" + Page("tx", "rpc", "a new password") + "]}}");

        Assert.True(json.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task ValidatorRefusal_ReportsTheValidatorsReason()
    {
        var json = await Check("{\"CustomPages\":{\"Pages\":[" + Page("dup", "a", "x") + "," + Page("dup", "b", "y") + "]}}");

        Assert.False(json.GetProperty("ok").GetBoolean());
        Assert.Contains("dup", json.GetProperty("message").GetString(), System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task NumericTiers_AreReadToo()
    {
        var json = await Check("{\"CustomPages\":{\"Pages\":[{\"Slug\":\"p\",\"Visibility\":0,\"Enabled\":true}]}}");

        Assert.True(json.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task UnreadableBody_IsReportedNotThrown()
    {
        var json = await Check("{ not json");

        Assert.False(json.GetProperty("ok").GetBoolean());
    }

    /// <summary>
    /// The whole configuration the dashboard reads back holds Jellyfin's ids written without dashes, and the check
    /// reads them.
    /// True: a page saves on a server with groups and invites.
    /// False: every save says the settings could not be read.
    /// </summary>
    [Fact]
    public async Task IdsWithoutDashes_AreRead()
    {
        var json = await Check("{\"Everyone\":{\"Id\":\"5a1f0c7e000040008000000000000001\",\"Name\":\"Everyone\",\"MemberIds\":[]},"
            + "\"Groups\":[{\"Id\":\"0b9a23b3307c475298d761a7538d8141\",\"Name\":\"Family\",\"MemberIds\":[\"6f1f0c7e000040008000000000000002\"]}],"
            + "\"CustomPages\":{\"Pages\":[" + Page("tx", "rpc", "a new password") + "]}}");

        Assert.True(json.GetProperty("ok").GetBoolean(), json.ToString());
    }

    [Fact]
    public void Check_RequiresElevation()
    {
        var method = typeof(PagesController).GetMethod(nameof(PagesController.CheckConfiguration))!;
        var authorize = typeof(PagesController).GetCustomAttributes<AuthorizeAttribute>().Single();

        Assert.Equal("RequiresElevation", authorize.Policy);
        Assert.Empty(method.GetCustomAttributes<AllowAnonymousAttribute>());
    }
}
