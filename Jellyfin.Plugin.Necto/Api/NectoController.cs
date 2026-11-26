using System.Net.Mime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Api;

/// <summary>
/// The routes of the Necto page itself, which serve its strings.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("Necto")]
[Produces(MediaTypeNames.Application.Json)]
public class NectoController : ControllerBase
{
    /// <summary>
    /// Gives the pages their strings in a language, the declared Jellyfin strings and the plugin's own. The strings
    /// are not secret, so the route allows anonymous callers.
    /// </summary>
    /// <param name="language">The language, such as de or pt-BR. Left out, it gives US English.</param>
    /// <returns>The strings by key, or 304 when the page already has them.</returns>
    [HttpGet("Translations")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    public IActionResult Translations([FromQuery] string? language)
        => StolonTranslations.Serve(language, Request, Response);
}
