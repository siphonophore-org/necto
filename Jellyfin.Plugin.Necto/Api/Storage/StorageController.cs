using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Configuration.Storage;
using Jellyfin.Plugin.Necto.Services.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Necto.Api.Storage;

/// <summary>
/// The Storage tool's routes, for administrators only.
/// </summary>
[ApiController]
[Route("Storage")]
[Authorize(Policy = "RequiresElevation")]
public class StorageController : ControllerBase
{
    private readonly StorageService _storage;
    private readonly DiskFormatting _formatting;
    private readonly FolderCreator _folders;

    /// <summary>
    /// Initializes a new instance of the <see cref="StorageController"/> class.
    /// </summary>
    /// <param name="storage">Checks the disks.</param>
    /// <param name="formatting">Formats unformatted disks.</param>
    /// <param name="folders">Makes folders on disks.</param>
    public StorageController(StorageService storage, DiskFormatting formatting, FolderCreator folders)
    {
        _folders = folders;
        ArgumentNullException.ThrowIfNull(storage);
        _storage = storage;
        _formatting = formatting;
    }

    /// <summary>
    /// Gets what formatting offers on this server: the file systems and where a new disk is mounted.
    /// </summary>
    /// <returns>The options.</returns>
    [HttpGet("Format")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<FormatOptions> GetFormatOptions() => Ok(_formatting.Options);

    /// <summary>
    /// Formats an unformatted disk, mounts it, and adds it to a library when one is named. Erases the disk.
    /// </summary>
    /// <param name="request">The disk, its new name and file system, and the library.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What came of it.</returns>
    [HttpPost("Format")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<FormatOutcome>> Format([FromBody] FormatRequest request, CancellationToken cancellationToken)
        => Ok(await _formatting.FormatAsync(request, cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Checks every disk and share the libraries use.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>One entry per disk, missing and unresponsive disks first.</returns>
    [HttpGet("Disks")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<DiskInfo>>> GetDisks(CancellationToken cancellationToken)
        => Ok(await _storage.CheckAsync(cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Makes a folder directly on a mounted disk, and adds it to a library when one is named.
    /// </summary>
    /// <param name="request">The disk, the folder's name, and the library.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What came of it.</returns>
    [HttpPost("Folders")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<FolderOutcome>> CreateFolder([FromBody] FolderRequest request, CancellationToken cancellationToken)
        => Ok(await _folders.CreateAsync(request, cancellationToken).ConfigureAwait(false));
}
