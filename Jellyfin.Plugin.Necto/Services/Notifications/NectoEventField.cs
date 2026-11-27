namespace Jellyfin.Plugin.Necto.Services.Notifications;

/// <summary>
/// One field a Discord or webhook post carries, such as a request's seasons. A channel's post settings name the
/// fields it sends by key, and a field no setting names is always sent.
/// </summary>
/// <param name="Key">The field's key, which a webhook's JSON carries it under, such as <c>seasons</c>.</param>
/// <param name="Name">The field's name in a Discord embed, in the server's language.</param>
/// <param name="Value">The field's value, or empty. A webhook gets <c>null</c> for an empty one.</param>
/// <param name="Inline"><c>true</c> to show the field beside others in a Discord embed.</param>
public sealed record NectoEventField(string Key, string Name, string Value, bool Inline = true)
{
    /// <summary>Gets a value indicating whether a Discord embed shows the field under its text, not as a field.</summary>
    public bool InText { get; init; }

    /// <summary>Gets a value indicating whether the value is JSON, which a webhook carries as JSON and Discord leaves out.</summary>
    public bool Json { get; init; }
}
