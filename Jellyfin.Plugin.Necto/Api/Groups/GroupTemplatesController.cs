using System;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Services.Policies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Necto.Api.Groups;

/// <summary>
/// The route that writes each user's group Jellyfin settings onto them. It is for administrators.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("Groups")]
[Produces(MediaTypeNames.Application.Json)]
public class GroupTemplatesController : ControllerBase
{
    private readonly PolicyTemplateService _templates;

    /// <summary>
    /// Initializes a new instance of the <see cref="GroupTemplatesController"/> class.
    /// </summary>
    /// <param name="templates">Writes the templates.</param>
    public GroupTemplatesController(PolicyTemplateService templates)
    {
        _templates = templates ?? throw new ArgumentNullException(nameof(templates));
    }

    /// <summary>
    /// Writes each user's template onto their Jellyfin policy and preferences.
    /// </summary>
    /// <param name="cancellationToken">Stops the run between users.</param>
    /// <returns>How many users took a template.</returns>
    [HttpPost("Templates/Apply")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<CountResult>> ApplyTemplates(CancellationToken cancellationToken)
        => Ok(new CountResult(await _templates.ApplyAllAsync(null, cancellationToken).ConfigureAwait(false)));
}
