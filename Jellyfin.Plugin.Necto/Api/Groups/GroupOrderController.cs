using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Services.Configuration;
using Jellyfin.Plugin.Necto.Services.Policies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Api.Groups;

/// <summary>
/// The routes for the order of the groups, which decides whose template a member of several groups takes, and for
/// writing the templates onto every user. Every route is for administrators.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("Groups")]
[Produces(MediaTypeNames.Application.Json)]
public class GroupOrderController : ControllerBase
{
    private readonly IConfigurationStore _store;
    private readonly PolicyTemplateService _templates;

    /// <summary>
    /// Initializes a new instance of the <see cref="GroupOrderController"/> class.
    /// </summary>
    /// <param name="store">Reads and saves the groups.</param>
    /// <param name="templates">Writes the templates.</param>
    public GroupOrderController(IConfigurationStore store, PolicyTemplateService templates)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _templates = templates ?? throw new ArgumentNullException(nameof(templates));
    }

    /// <summary>
    /// Puts the groups in a new order, then writes each user's template onto them. Everyone stays first.
    /// </summary>
    /// <param name="ids">Every group's id, without Everyone, in the new order.</param>
    /// <param name="cancellationToken">Stops the writing of templates.</param>
    /// <returns>204, or 400 when the ids are not every group once.</returns>
    [HttpPut("Order")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> SetOrder([FromBody] List<Guid> ids, CancellationToken cancellationToken)
    {
        var ok = false;
        _store.Mutate(c =>
        {
            ok = GroupOrder.Apply(c.Groups, ids);
            return ok;
        });

        if (!ok)
        {
            return BadRequest(new ApiError(StolonTranslations.Get(TranslationKeys.GroupsErrorOrder, StolonTranslations.RequestLanguage(Request))));
        }

        await _templates.ApplyAllAsync(null, cancellationToken).ConfigureAwait(false);
        return NoContent();
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
