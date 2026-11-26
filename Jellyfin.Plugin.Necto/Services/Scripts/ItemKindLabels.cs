using System.Collections.Generic;
using Jellyfin.Data.Enums;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.Scripts;

/// <summary>
/// Readable names for item kinds. The stored value stays the <see cref="BaseItemKind"/> name, and this only changes
/// what the editor shows, such as "Collection" for BoxSet.
/// </summary>
public static class ItemKindLabels
{
    private static readonly Dictionary<BaseItemKind, string> Keys = new()
    {
        [BaseItemKind.AggregateFolder] = TranslationKeys.ScriptsItemKindAggregateFolder,
        [BaseItemKind.Audio] = TranslationKeys.Audio,
        [BaseItemKind.AudioBook] = TranslationKeys.ScriptsItemKindAudioBook,
        [BaseItemKind.BasePluginFolder] = TranslationKeys.ScriptsItemKindBasePluginFolder,
        [BaseItemKind.Book] = TranslationKeys.ScriptsItemKindBook,
        [BaseItemKind.BoxSet] = TranslationKeys.LabelCollection,
        [BaseItemKind.Channel] = TranslationKeys.ScriptsItemKindChannel,
        [BaseItemKind.ChannelFolderItem] = TranslationKeys.ScriptsItemKindChannelFolderItem,
        [BaseItemKind.CollectionFolder] = TranslationKeys.TasksLibraryCategory,
        [BaseItemKind.Episode] = TranslationKeys.Episode,
        [BaseItemKind.Folder] = TranslationKeys.LabelFolder,
        [BaseItemKind.Genre] = TranslationKeys.Genre,
        [BaseItemKind.ManualPlaylistsFolder] = TranslationKeys.ScriptsItemKindManualPlaylistsFolder,
        [BaseItemKind.Movie] = TranslationKeys.Movie,
        [BaseItemKind.LiveTvChannel] = TranslationKeys.ScriptsItemKindLiveTvChannel,
        [BaseItemKind.LiveTvProgram] = TranslationKeys.ScriptsItemKindLiveTvProgram,
        [BaseItemKind.MusicAlbum] = TranslationKeys.MusicAlbum,
        [BaseItemKind.MusicArtist] = TranslationKeys.MusicArtist,
        [BaseItemKind.MusicGenre] = TranslationKeys.ScriptsItemKindMusicGenre,
        [BaseItemKind.MusicVideo] = TranslationKeys.MusicVideo,
        [BaseItemKind.Person] = TranslationKeys.Person,
        [BaseItemKind.Photo] = TranslationKeys.Photo,
        [BaseItemKind.PhotoAlbum] = TranslationKeys.ScriptsItemKindPhotoAlbum,
        [BaseItemKind.Playlist] = TranslationKeys.LabelPlaylist,
        [BaseItemKind.PlaylistsFolder] = TranslationKeys.ScriptsItemKindManualPlaylistsFolder,
        [BaseItemKind.Program] = TranslationKeys.ScriptsItemKindProgram,
        [BaseItemKind.Recording] = TranslationKeys.ScriptsItemKindRecording,
        [BaseItemKind.Season] = TranslationKeys.Season,
        [BaseItemKind.Series] = TranslationKeys.Series,
        [BaseItemKind.Studio] = TranslationKeys.Studio,
        [BaseItemKind.Trailer] = TranslationKeys.Trailer,
        [BaseItemKind.TvChannel] = TranslationKeys.ScriptsItemKindTvChannel,
        [BaseItemKind.TvProgram] = TranslationKeys.ScriptsItemKindTvProgram,
        [BaseItemKind.UserRootFolder] = TranslationKeys.ScriptsItemKindUserRootFolder,
        [BaseItemKind.UserView] = TranslationKeys.ScriptsItemKindUserView,
        [BaseItemKind.Video] = TranslationKeys.Video,
        [BaseItemKind.Year] = TranslationKeys.LabelYear,
    };

    /// <summary>
    /// Gets the readable name of an item kind.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <param name="language">The language, or <c>null</c> for US English.</param>
    /// <returns>The name, such as "Music video" for MusicVideo, or the kind itself when it has no name.</returns>
    public static string For(BaseItemKind kind, string? language = null)
        => Keys.TryGetValue(kind, out var key) ? StolonTranslations.Get(key, language) : kind.ToString();
}
