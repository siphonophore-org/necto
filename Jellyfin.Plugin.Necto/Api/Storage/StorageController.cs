using System;
using System.Collections.Generic;
using System.Linq;
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
    {
        var disks = await _storage.CheckAsync(cancellationToken).ConfigureAwait(false);
        var dismissed = Plugin.Instance?.ReadConfiguration(c => c.Storage.DismissedNotices.ToList()) ?? new List<string>();
        foreach (var disk in disks)
        {
            disk.DismissedNotices = DismissedNotices.For(dismissed, disk.MountPoint);
        }

        return Ok(disks);
    }

    /// <summary>
    /// Dismisses one of a disk's notices, so it stops asking for attention until what it says changes.
    /// </summary>
    /// <param name="request">The disk's mount point and the notice's key.</param>
    /// <returns>No content, or a bad request without a mount point or a key.</returns>
    [HttpPost("Notices/Dismiss")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult DismissNotice([FromBody] NoticeDismissal request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.MountPoint) || string.IsNullOrWhiteSpace(request.Key))
        {
            return BadRequest();
        }

        var entry = DismissedNotices.Entry(request.MountPoint, request.Key);
        Plugin.Instance?.MutateConfiguration(c =>
        {
            if (c.Storage.DismissedNotices.Contains(entry, StringComparer.Ordinal))
            {
                return false;
            }

            c.Storage.DismissedNotices.Add(entry);
            return true;
        });
        return NoContent();
    }

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
