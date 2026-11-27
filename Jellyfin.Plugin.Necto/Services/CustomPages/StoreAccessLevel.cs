namespace Jellyfin.Plugin.Necto.Services.CustomPages;

/// <summary>
/// What a caller is allowed to reach in a store.
/// </summary>
public enum StoreAccessLevel
{
    /// <summary>The caller may not touch the store at all.</summary>
    None,

    /// <summary>The caller may only reach records they created.</summary>
    Own,

    /// <summary>The caller may reach every record in the store.</summary>
    All
}
