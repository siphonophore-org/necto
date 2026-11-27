using System;
using System.Collections.Generic;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Services.MenuLinks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Api.MenuLinks;

/// <summary>
/// The routes of the Menu links editor in Settings: the custom links in the web client's side menu, which live in
/// its config.json.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("Necto/MenuLinks")]
[Produces(MediaTypeNames.Application.Json)]
public class MenuLinksController : ControllerBase
{
    private readonly MenuLinksService _links;

    /// <summary>
    /// Initializes a new instance of the <see cref="MenuLinksController"/> class.
    /// </summary>
    /// <param name="links">Reads and saves the links.</param>
    public MenuLinksController(MenuLinksService links)
    {
        _links = links ?? throw new ArgumentNullException(nameof(links));
    }

    /// <summary>Gives the web client's menu links, where they live, and whether they can be saved.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The links.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<MenuLinksView>> Get(CancellationToken cancellationToken)
        => Ok(await _links.ReadAsync(StolonTranslations.RequestLanguage(Request), cancellationToken).ConfigureAwait(false));

    /// <summary>Saves the web client's menu links in their order. Every other key of config.json stays.</summary>
    /// <param name="links">The links.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The links as saved, or 400 with the reason.</returns>
    [HttpPut]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<MenuLinksView>> Save([FromBody] List<MenuLink>? links, CancellationToken cancellationToken)
    {
        var (view, error) = await _links.SaveAsync(links, StolonTranslations.RequestLanguage(Request), cancellationToken).ConfigureAwait(false);
        return view is null ? BadRequest(new ApiError(error ?? string.Empty)) : Ok(view);
    }
}
