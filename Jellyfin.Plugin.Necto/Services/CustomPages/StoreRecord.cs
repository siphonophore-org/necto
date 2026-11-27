using System;
using System.Text.Json.Nodes;

namespace Jellyfin.Plugin.Necto.Services.CustomPages;

/// <summary>
/// One record in a store. Everything but <see cref="Data"/> and <see cref="Label"/> is set by the server, so a page
/// cannot forge an id, a time, or an owner.
/// </summary>
public class StoreRecord
{
    /// <summary>Gets or sets the record's id, set by the server.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets when the record was created.</summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>Gets or sets when the record was last written.</summary>
    public DateTime UpdatedUtc { get; set; }

    /// <summary>Gets or sets the user who created the record, or <c>null</c> for a write by a visitor who was not signed in. A later write never changes it.</summary>
    public string? UserId { get; set; }

    /// <summary>Gets or sets the name a person would call the record, which names it in the activity log. Never interpreted.</summary>
    public string? Label { get; set; }

    /// <summary>Gets or sets the page's payload, whose shape is the page's own.</summary>
    public JsonNode? Data { get; set; }

    /// <summary>
    /// Copies the record, so one handed to a caller never shares the instance the store keeps.
    /// </summary>
    /// <returns>A copy of the record.</returns>
    public StoreRecord Clone() => new()
    {
        Id = Id,
        CreatedUtc = CreatedUtc,
        UpdatedUtc = UpdatedUtc,
        UserId = UserId,
        Label = Label,
        Data = Data?.DeepClone()
    };
}
