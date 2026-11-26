using System;
using System.IO;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Necto.Api.CustomPages;
using Jellyfin.Plugin.Necto.Configuration.CustomPages;
using Jellyfin.Plugin.Necto.Services.CustomPages;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Jellyfin.Plugin.Necto.Tests.Fakes.CustomPages;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Api.CustomPages;

/// <summary>
/// Tests for the store endpoints on <see cref="CustomPagesController"/>. The store's tiers are resolved
/// here from the calling credentials, so this is where an anonymous caller, a user, an administrator,
/// and an API key are told apart.
/// </summary>
public class StoreEndpointTests
{
    private static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private enum Credential
    {
        None,
        User,
        Admin,
        ApiKey,
        Invalid
    }

    private static (CustomPagesController Controller, RecordingStoreService Stores) Create(Credential credential)
    {
        var stores = new RecordingStoreService();
        var user = credential is Credential.User or Credential.Admin ? TestController.User(Alice, credential == Credential.Admin) : null;
        var authorization = credential == Credential.Invalid
            ? TestController.Authorization(null, failure: new SecurityException("bad token"))
            : TestController.Authorization(user, credential == Credential.ApiKey);

        return (TestController.Create(stores: stores, authorization: authorization), stores);
    }

    private static PageStore Store(
        PageVisibility read = PageVisibility.Admin,
        PageVisibility write = PageVisibility.User,
        StoreScope scope = StoreScope.All,
        bool editOwn = false)
        => new PageStore
        {
            Name = "downloads",
            ReadAccess = read,
            ReadScope = scope,
            WriteAccess = write,
            AllowEditOwn = editOwn
        };

    private static void SetBody(CustomPagesController controller, string json)
    {
        controller.Response.Body = new MemoryStream();
        controller.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
    }

    private static int StatusOf(IActionResult result) => result switch
    {
        StatusCodeResult status => status.StatusCode,
        ObjectResult obj => obj.StatusCode ?? 200,
        ContentResult => 200,
        _ => -1
    };

    // MARK: Unknown stores

    [Fact]
    public async Task Read_UnknownStore_NotFound()
    {
        var (controller, _) = Create(Credential.Admin);
        Assert.Equal(404, StatusOf(await controller.StoreRead("nope", null)));
    }

    // MARK: Read authorization

    [Fact]
    public async Task Read_AnonymousCallerOnGatedStore_AsksForSignIn()
    {
        // 401 rather than 403, so the page's own sign in prompt is the natural next step.
        var (controller, stores) = Create(Credential.None);
        stores.Stores["downloads"] = Store();

        Assert.Equal(401, StatusOf(await controller.StoreRead("downloads", null)));
    }

    [Fact]
    public async Task Read_SignedInButBelowTier_IsForbidden()
    {
        var (controller, stores) = Create(Credential.User);
        stores.Stores["downloads"] = Store();

        Assert.Equal(403, StatusOf(await controller.StoreRead("downloads", null)));
    }

    [Fact]
    public async Task Read_OwnScope_IsNarrowedToTheirOwnRecords()
    {
        var (controller, stores) = Create(Credential.User);
        stores.Stores["downloads"] = Store(read: PageVisibility.User, scope: StoreScope.Own);

        await controller.StoreRead("downloads", null);

        Assert.Equal(new Guid?[] { Alice }, stores.Reads);
    }

    [Fact]
    public async Task Read_OwnScope_StillShowsAnAdministratorEverything()
    {
        var (controller, stores) = Create(Credential.Admin);
        stores.Stores["downloads"] = Store(read: PageVisibility.User, scope: StoreScope.Own);

        await controller.StoreRead("downloads", null);

        Assert.Equal(new Guid?[] { null }, stores.Reads);
    }

    [Fact]
    public async Task Read_Administrator_SeesEveryRecord()
    {
        var (controller, stores) = Create(Credential.Admin);
        stores.Stores["downloads"] = Store();

        await controller.StoreRead("downloads", null);

        Assert.Equal(new Guid?[] { null }, stores.Reads);
    }

    [Fact]
    public async Task Read_ApiKey_SeesEveryRecord()
    {
        // A Jellyfin API key is administrator issued and already carries administrator reach, and a
        // store exists so something outside the browser can pick work up and write results back.
        var (controller, stores) = Create(Credential.ApiKey);
        stores.Stores["downloads"] = Store();

        await controller.StoreRead("downloads", null);

        Assert.Equal(new Guid?[] { null }, stores.Reads);
    }

    [Fact]
    public async Task Read_InvalidToken_FallsBackToAnonymous()
    {
        var (controller, stores) = Create(Credential.Invalid);
        stores.Stores["downloads"] = Store();

        Assert.Equal(401, StatusOf(await controller.StoreRead("downloads", null)));
    }

    [Fact]
    public async Task Read_SetsNoStoreHeaders()
    {
        var (controller, stores) = Create(Credential.Admin);
        stores.Stores["downloads"] = Store();

        await controller.StoreRead("downloads", null);

        Assert.Equal("no-store", controller.Response.Headers["Cache-Control"]);
        Assert.Equal("nosniff", controller.Response.Headers["X-Content-Type-Options"]);
    }

    // MARK: Write authorization

    [Fact]
    public async Task Write_BelowWriteTier_IsRefused()
    {
        var (controller, stores) = Create(Credential.None);
        stores.Stores["downloads"] = Store();
        SetBody(controller, "{\"data\":{\"url\":\"x\"}}");

        Assert.Equal(401, StatusOf(await controller.StoreWrite("downloads", CancellationToken.None)));
        await Task.CompletedTask;
        Assert.Empty(stores.Creates);
    }

    [Fact]
    public async Task Write_NoId_CreatesOwnedByCaller()
    {
        var (controller, stores) = Create(Credential.User);
        stores.Stores["downloads"] = Store();
        stores.CreateResult = (StoreWriteResult.Ok, new StoreRecord { Id = "r1" });
        SetBody(controller, "{\"data\":{\"url\":\"x\"}}");

        Assert.Equal(200, StatusOf(await controller.StoreWrite("downloads", CancellationToken.None)));
        Assert.Equal(new[] { Alice }, stores.Creates);
    }

    [Fact]
    public async Task Write_AnonymousStore_AcceptsUnauthenticatedCreate()
    {
        var (controller, stores) = Create(Credential.None);
        stores.Stores["suggestions"] = Store(read: PageVisibility.Admin, write: PageVisibility.Anonymous);
        stores.CreateResult = (StoreWriteResult.Ok, new StoreRecord { Id = "r1" });
        SetBody(controller, "{\"data\":{\"url\":\"x\"}}");

        Assert.Equal(200, StatusOf(await controller.StoreWrite("suggestions", CancellationToken.None)));
        Assert.Equal(new[] { Guid.Empty }, stores.Creates);
    }

    [Fact]
    public async Task Write_WithId_ByASubmitterWithoutEditOwn_IsForbidden()
    {
        // The submitter may create a record and read it back, but the status an administrator writes
        // onto it is not theirs to rewrite.
        var (controller, stores) = Create(Credential.User);
        stores.Stores["downloads"] = Store(read: PageVisibility.User, scope: StoreScope.Own);
        SetBody(controller, "{\"id\":\"r1\",\"data\":{\"status\":\"done\"}}");

        Assert.Equal(403, StatusOf(await controller.StoreWrite("downloads", CancellationToken.None)));
        Assert.Empty(stores.Updates);
    }

    [Fact]
    public async Task Write_WithId_ByASubmitterWithEditOwn_IsNarrowedToTheirOwn()
    {
        var (controller, stores) = Create(Credential.User);
        stores.Stores["downloads"] = Store(read: PageVisibility.User, scope: StoreScope.Own, editOwn: true);
        stores.UpdateResult = (StoreWriteResult.Ok, new StoreRecord { Id = "r1" });
        SetBody(controller, "{\"id\":\"r1\",\"data\":{\"note\":\"typo\"}}");

        await controller.StoreWrite("downloads", CancellationToken.None);

        Assert.Equal(new[] { ("r1", (Guid?)Alice) }, stores.Updates);
    }

    [Fact]
    public async Task Write_WithId_ByAnAdministrator_ReachesEveryRecord()
    {
        var (controller, stores) = Create(Credential.Admin);
        stores.Stores["downloads"] = Store();
        stores.UpdateResult = (StoreWriteResult.Ok, new StoreRecord { Id = "r1" });
        SetBody(controller, "{\"id\":\"r1\",\"data\":{\"status\":\"done\"}}");

        await controller.StoreWrite("downloads", CancellationToken.None);

        Assert.Equal(new[] { ("r1", (Guid?)null) }, stores.Updates);
    }

    [Fact]
    public async Task Write_IsCaseInsensitiveAboutTheEnvelope()
    {
        var (controller, stores) = Create(Credential.Admin);
        stores.Stores["downloads"] = Store();
        stores.UpdateResult = (StoreWriteResult.Ok, new StoreRecord { Id = "r1" });
        SetBody(controller, "{\"Id\":\"r1\",\"Data\":{\"status\":\"done\"}}");

        await controller.StoreWrite("downloads", CancellationToken.None);

        Assert.Equal(new[] { ("r1", (Guid?)null) }, stores.Updates);
    }

    [Fact]
    public async Task Write_MalformedBody_IsBadRequest()
    {
        var (controller, stores) = Create(Credential.Admin);
        stores.Stores["downloads"] = Store();
        SetBody(controller, "{ not json");

        Assert.Equal(400, StatusOf(await controller.StoreWrite("downloads", CancellationToken.None)));
    }

    [Fact]
    public async Task Write_OversizedBody_IsRefusedBeforeParsing()
    {
        var (controller, stores) = Create(Credential.Admin);
        stores.Stores["downloads"] = Store();
        SetBody(controller, "{\"data\":\"" + new string('x', StoreService.MaxRecordBytes + 8192) + "\"}");

        Assert.Equal(413, StatusOf(await controller.StoreWrite("downloads", CancellationToken.None)));
        Assert.Empty(stores.Creates);
    }

    [Fact]
    public async Task Write_FullStore_IsConflict()
    {
        var (controller, stores) = Create(Credential.User);
        stores.Stores["downloads"] = Store();
        stores.CreateResult = (StoreWriteResult.Full, (StoreRecord?)null);
        SetBody(controller, "{\"data\":{\"url\":\"x\"}}");

        Assert.Equal(409, StatusOf(await controller.StoreWrite("downloads", CancellationToken.None)));
    }

    [Fact]
    public async Task Write_StoreOverItsSizeBudget_IsInsufficientStorage()
    {
        var (controller, stores) = Create(Credential.User);
        stores.Stores["downloads"] = Store();
        stores.CreateResult = (StoreWriteResult.OverBudget, (StoreRecord?)null);
        SetBody(controller, "{\"data\":{\"url\":\"x\"}}");

        Assert.Equal(507, StatusOf(await controller.StoreWrite("downloads", CancellationToken.None)));
    }

    [Fact]
    public async Task Write_DeclaredOversizedBody_IsRefusedUnread()
    {
        var (controller, stores) = Create(Credential.User);
        stores.Stores["downloads"] = Store();
        SetBody(controller, "{}");
        controller.Request.ContentLength = StoreService.MaxRecordBytes + 8192;

        Assert.Equal(413, StatusOf(await controller.StoreWrite("downloads", CancellationToken.None)));
    }

    // MARK: Delete

    [Fact]
    public async Task Delete_WithoutModifyAccess_IsForbidden()
    {
        var (controller, stores) = Create(Credential.User);
        stores.Stores["downloads"] = Store();
        SetBody(controller, "{\"id\":\"r1\"}");

        Assert.Equal(403, StatusOf(await controller.StoreDelete("downloads", CancellationToken.None)));
        Assert.Empty(stores.Deletes);
    }

    [Fact]
    public async Task Delete_WithoutAnId_IsBadRequest()
    {
        var (controller, stores) = Create(Credential.Admin);
        stores.Stores["downloads"] = Store();
        SetBody(controller, "{}");

        Assert.Equal(400, StatusOf(await controller.StoreDelete("downloads", CancellationToken.None)));
    }

    [Fact]
    public async Task Delete_ByOwner_IsNarrowedToTheirOwn()
    {
        var (controller, stores) = Create(Credential.User);
        stores.Stores["downloads"] = Store(read: PageVisibility.User, scope: StoreScope.Own, editOwn: true);
        stores.DeleteResult = true;
        SetBody(controller, "{\"id\":\"r1\"}");

        Assert.Equal(204, StatusOf(await controller.StoreDelete("downloads", CancellationToken.None)));
        Assert.Equal(new[] { ("r1", (Guid?)Alice) }, stores.Deletes);
    }

    [Fact]
    public async Task Delete_MissingRecord_IsNotFound()
    {
        var (controller, stores) = Create(Credential.Admin);
        stores.Stores["downloads"] = Store();
        stores.DeleteResult = false;
        SetBody(controller, "{\"id\":\"r1\"}");

        Assert.Equal(404, StatusOf(await controller.StoreDelete("downloads", CancellationToken.None)));
    }

    // MARK: Clear

    [Fact]
    public void Clear_NamesTheAdministratorForTheActivityLog()
    {
        var (controller, stores) = Create(Credential.Admin);
        var store = Store();
        stores.Stores["downloads"] = store;
        controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(
            new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "Johnny") }, "Test"));

        controller.StoreClear("downloads");

        Assert.Equal(new[] { (store, (string?)"Johnny") }, stores.Clears);
    }
}
