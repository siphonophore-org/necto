using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.CustomPages;
using Jellyfin.Plugin.Necto.Services.Configuration;
using Jellyfin.Plugin.Necto.Services.CustomPages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Api.CustomPages;

/// <summary>
/// The administrator routes of the Custom pages tool: the page list for groups, store counts and clearing, and the
/// check a save runs first. Every route has two segments or more, so no page slug is shadowed.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("Pages")]
[Produces(MediaTypeNames.Application.Json)]
public class PagesController : ControllerBase
{
    // Reads a configuration the page posts, Pascal cased, with tiers as names or numbers.
    // Jellyfin's own options, which read its ids written without dashes, as the page sends them back.
    private static readonly JsonSerializerOptions ConfigurationJson = new(Jellyfin.Extensions.Json.JsonDefaults.Options)
    {
        PropertyNameCaseInsensitive = true
    };

    // Answers in camel case whatever the server's own API options are, as the store routes do.
    private static readonly JsonSerializerOptions ResponseJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IStoreService _stores;
    private readonly IConfigurationStore _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="PagesController"/> class.
    /// </summary>
    /// <param name="stores">Holds the store records.</param>
    /// <param name="configuration">Reads the settings in effect.</param>
    public PagesController(IStoreService stores, IConfigurationStore configuration)
    {
        _stores = stores ?? throw new ArgumentNullException(nameof(stores));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <summary>
    /// Lists every page with its id, title, slug, and tier, for the group editor.
    /// </summary>
    /// <returns>The pages in the order the tool lists them.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<PageSummary>> ListPages()
        => _configuration.Read(static c => (c.CustomPages?.Pages ?? new List<CustomPage>())
            .Select(p => new PageSummary { Id = p.Id, Title = p.Title, Slug = p.Slug, Visibility = p.Visibility, Enabled = p.Enabled })
            .ToList());

    /// <summary>
    /// Reports how many records a store holds, for the tool and Troubleshooting. A page can never learn the size of a
    /// store it cannot read here.
    /// </summary>
    /// <param name="name">The store's name.</param>
    /// <returns>The record count and the store's limit, or 404 for a store that is unknown or does not accept calls.</returns>
    [HttpGet("Stores/{name}/Stats")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult StoreStats(string name)
    {
        var store = _stores.Find(name);
        if (store is null)
        {
            return NotFound();
        }

        NoStore();
        return Json(new StoreStatsResponse { Count = _stores.Count(store), Limit = StoreService.EffectiveMaxRecords(store) });
    }

    /// <summary>
    /// Deletes every record in a store. A page's helper never reaches it, since emptying a store is an administrator's act.
    /// </summary>
    /// <param name="name">The store's name.</param>
    /// <returns>How many records were deleted, or 404 for a store that is unknown or does not accept calls.</returns>
    [HttpPost("Stores/{name}/Clear")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult StoreClear(string name)
    {
        var store = _stores.Find(name);
        if (store is null)
        {
            return NotFound();
        }

        NoStore();
        return Json(new StoreClearResponse { Removed = _stores.Clear(store, User.Identity?.Name) });
    }

    /// <summary>
    /// Reports whether a configuration would save, and why not, without saving it. Jellyfin answers a refused save
    /// with no reason, so the tool asks here first. The save runs the same checks on its own.
    /// </summary>
    /// <param name="cancellationToken">Stops reading the body.</param>
    /// <returns>Whether it would save, with the reason in the viewer's language when it would not.</returns>
    [HttpPost("Configuration/Check")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckConfiguration(CancellationToken cancellationToken)
    {
        NoStore();
        var language = StolonTranslations.RequestLanguage(Request);

        CustomPagesSettings? incoming;
        try
        {
            var whole = await JsonSerializer.DeserializeAsync<PluginConfiguration>(Request.Body, ConfigurationJson, cancellationToken)
                .ConfigureAwait(false);
            incoming = whole?.CustomPages;
        }
        catch (JsonException)
        {
            incoming = null;
        }

        if (incoming is null)
        {
            return Json(new ConfigurationCheckResponse { Ok = false, Message = StolonTranslations.Get(TranslationKeys.PagesErrorConfigUnreadable, language) });
        }

        var stored = _configuration.Read(static config => config.CustomPages) ?? new CustomPagesSettings();

        string? message;
        try
        {
            CustomPagesSaving.Normalize(incoming);
            ConfigurationValidator.Validate(incoming, language);
            message = RouteSecrets.Check(incoming, stored, language);
        }
        catch (ArgumentException ex)
        {
            message = ex.Message;
        }

        return Json(new ConfigurationCheckResponse { Ok = message is null, Message = message });
    }

    /// <summary>
    /// Serializes an answer in camel case.
    /// </summary>
    private ContentResult Json(object value)
        => Content(JsonSerializer.Serialize(value, ResponseJson), "application/json; charset=utf-8");

    /// <summary>
    /// Keeps an answer out of every cache.
    /// </summary>
    private void NoStore()
    {
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
    }
}
