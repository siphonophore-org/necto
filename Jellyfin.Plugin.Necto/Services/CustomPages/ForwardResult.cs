using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Necto.Services.CustomPages;

/// <summary>
/// The outcome of forwarding a route: the target's status, body, and the headers that may be relayed.
/// </summary>
public sealed class ForwardResult
{
    /// <summary>Gets or sets the status code the target gave.</summary>
    public int StatusCode { get; set; }

#pragma warning disable CA1819 // The body is opaque bytes handed straight to a file result.
    /// <summary>Gets or sets the response body.</summary>
    public byte[] Body { get; set; } = Array.Empty<byte>();
#pragma warning restore CA1819

    /// <summary>Gets or sets the response content type, when the target gave one.</summary>
    public string? ContentType { get; set; }

    /// <summary>Gets or sets why forwarding failed, or <see cref="ForwardError.None"/> when the target answered.</summary>
    public ForwardError Error { get; set; }

    /// <summary>Gets the response headers that may be relayed to the caller.</summary>
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Builds the result of a failed forward, with the gateway status the caller answers with.
    /// </summary>
    /// <param name="error">Why forwarding failed.</param>
    /// <returns>The failed result.</returns>
    public static ForwardResult Failed(ForwardError error) => new()
    {
        Error = error,
        StatusCode = error == ForwardError.TimedOut ? 504 : 502
    };
}
