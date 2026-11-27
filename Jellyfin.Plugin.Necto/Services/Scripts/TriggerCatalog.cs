using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration.Scripts;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.Scripts;

/// <summary>
/// The single source for what each trigger supplies: the variables it fills, whether it carries an item, a
/// session, or a user, and therefore which condition fields apply to it. The editor, the validator, the
/// condition evaluator, and the test runs all read from here.
/// </summary>
public static class TriggerCatalog
{
    /// <summary>The prefix every variable carries in a script's environment.</summary>
    public const string EnvironmentPrefix = "JF_";

    /// <summary>The prefix a script secret's variable carries, so secrets cannot shadow event values.</summary>
    public const string SecretPrefix = "SECRET_";

    /// <summary>The prefix every variable read from a webhook request carries.</summary>
    public const string WebhookPrefix = "WEBHOOK_";

    private static HashSet<string>? _reserved;

    private static readonly VariableInfo[] Common =
    {
        new("EVENT", TranslationKeys.ScriptsVariableEvent),
        new("TIMESTAMP", TranslationKeys.ScriptsVariableTimestamp),
        new("LOCAL_DATE", TranslationKeys.ScriptsVariableLocalDate),
        new("LOCAL_TIME", TranslationKeys.ScriptsVariableLocalTime),
        new("HOUR", TranslationKeys.ScriptsVariableHour),
        new("DAY_OF_WEEK", TranslationKeys.ScriptsVariableDayOfWeek),
        new("SCRIPT_NAME", TranslationKeys.ScriptsVariableScriptName),
        new("SERVER_NAME", TranslationKeys.ScriptsVariableServerName),
        new("SERVER_ID", TranslationKeys.ScriptsVariableServerId),
        new("SERVER_VERSION", TranslationKeys.ScriptsVariableServerVersion),
        new("EVENT_JSON", TranslationKeys.ScriptsVariableEventJson),
    };

    private static readonly VariableInfo[] User =
    {
        new("USER_ID", TranslationKeys.ScriptsVariableUserId),
        new("USER_NAME", TranslationKeys.ScriptsVariableUserName),
        new("IS_ADMIN", TranslationKeys.ScriptsVariableIsAdmin),
    };

    private static readonly VariableInfo[] Session =
    {
        new("SESSION_ID", TranslationKeys.ScriptsVariableSessionId),
        new("CLIENT", TranslationKeys.ScriptsVariableClient),
        new("CLIENT_VERSION", TranslationKeys.ScriptsVariableClientVersion),
        new("DEVICE_ID", TranslationKeys.ScriptsVariableDeviceId),
        new("DEVICE_NAME", TranslationKeys.ScriptsVariableDeviceName),
        new("REMOTE_ADDRESS", TranslationKeys.ScriptsVariableRemoteAddress),
        new("IS_LOCAL", TranslationKeys.ScriptsVariableIsLocal),
    };

    private static readonly VariableInfo[] Item =
    {
        new("ITEM_ID", TranslationKeys.ScriptsVariableItemId),
        new("ITEM_NAME", TranslationKeys.ScriptsVariableItemName),
        new("ITEM_KIND", TranslationKeys.ScriptsVariableItemKind),
        new("ITEM_PATH", TranslationKeys.ScriptsVariableItemPath),
        new("LIBRARY_ID", TranslationKeys.ScriptsVariableLibraryId),
        new("LIBRARY_NAME", TranslationKeys.ScriptsVariableLibraryName),
        new("YEAR", TranslationKeys.ScriptsVariableYear),
        new("RUNTIME_SECONDS", TranslationKeys.ScriptsVariableRuntimeSeconds),
        new("SERIES_NAME", TranslationKeys.ScriptsVariableSeriesName),
        new("SEASON_NUMBER", TranslationKeys.ScriptsVariableSeasonNumber),
        new("EPISODE_NUMBER", TranslationKeys.ScriptsVariableEpisodeNumber),
        new("ALBUM", TranslationKeys.ScriptsVariableAlbum),
        new("ARTIST", TranslationKeys.ScriptsVariableArtist),
        new("GENRES", TranslationKeys.ScriptsVariableGenres),
        new("TAGS", TranslationKeys.ScriptsVariableTags),
        new("STUDIOS", TranslationKeys.ScriptsVariableStudios),
        new("OFFICIAL_RATING", TranslationKeys.ScriptsVariableOfficialRating),
        new("COMMUNITY_RATING", TranslationKeys.ScriptsVariableCommunityRating),
        new("VIDEO_WIDTH", TranslationKeys.ScriptsVariableVideoWidth),
        new("VIDEO_HEIGHT", TranslationKeys.ScriptsVariableVideoHeight),
        new("DATE_ADDED", TranslationKeys.ScriptsVariableDateAdded),
        new("DAYS_SINCE_ADDED", TranslationKeys.ScriptsVariableDaysSinceAdded),
        new("HAS_SUBTITLES", TranslationKeys.ScriptsVariableHasSubtitles),
        new("PROVIDER_IMDB", TranslationKeys.ScriptsVariableProviderImdb),
        new("PROVIDER_TMDB", TranslationKeys.ScriptsVariableProviderTmdb),
        new("PROVIDER_TVDB", TranslationKeys.ScriptsVariableProviderTvdb),
    };

    private static readonly VariableInfo[] PlaybackVars =
    {
        new("PLAY_SESSION_ID", TranslationKeys.ScriptsVariablePlaySessionId),
        new("PLAY_METHOD", TranslationKeys.ScriptsVariablePlayMethod),
        new("POSITION_SECONDS", TranslationKeys.ScriptsVariablePositionSeconds),
        new("PROGRESS_PERCENT", TranslationKeys.ScriptsVariableProgressPercent),
        new("IS_PAUSED", TranslationKeys.ScriptsVariableIsPaused),
        new("VIDEO_CODEC", TranslationKeys.ScriptsVariableVideoCodec),
        new("AUDIO_CODEC", TranslationKeys.ScriptsVariableAudioCodec),
        new("TRANSCODE_REASONS", TranslationKeys.ScriptsVariableTranscodeReasons),
    };

    private static readonly VariableInfo[] ForEachItem =
    {
        new("PLAYED_BY_ANYONE", TranslationKeys.ScriptsVariablePlayedByAnyone),
        new("DAYS_SINCE_PLAYED", TranslationKeys.ScriptsVariableDaysSincePlayed),
    };

    private static readonly VariableInfo[] ForEachUser =
    {
        new("DAYS_SINCE_ACTIVE", TranslationKeys.ScriptsVariableDaysSinceActive),
        new("IS_DISABLED", TranslationKeys.ScriptsVariableIsDisabled),
    };

    private static readonly VariableInfo[] WebhookVars =
    {
        new("WEBHOOK_BODY", TranslationKeys.ScriptsVariableWebhookBody),
        new("REMOTE_ADDRESS", TranslationKeys.ScriptsVariableRemoteAddressWebhookVars),
        new("IS_LOCAL", TranslationKeys.ScriptsVariableIsLocalWebhookVars),
    };

    private static readonly VariableInfo[] Stop = { new("PLAYED_TO_COMPLETION", TranslationKeys.ScriptsVariablePlayedToCompletion) };

    private static readonly VariableInfo[] Paused = { new("PAUSED_SECONDS", TranslationKeys.ScriptsVariablePausedSeconds) };

    private static readonly VariableInfo[] Playing = { new("PLAYING_SECONDS", TranslationKeys.ScriptsVariablePlayingSeconds) };

    private static readonly VariableInfo[] UserData =
    {
        new("IS_PLAYED", TranslationKeys.ScriptsVariableIsPlayed),
        new("IS_FAVORITE", TranslationKeys.ScriptsVariableIsFavorite),
        new("PLAY_COUNT", TranslationKeys.ScriptsVariablePlayCount),
    };

    private static readonly VariableInfo[] Collection =
    {
        new("COLLECTION_ID", TranslationKeys.ScriptsVariableCollectionId),
        new("COLLECTION_NAME", TranslationKeys.ScriptsVariableCollectionName),
    };

    private static readonly VariableInfo[] Update = { new("UPDATE_REASON", TranslationKeys.ScriptsVariableUpdateReason) };

    private static readonly VariableInfo[] Failure =
    {
        new("PROVIDER", TranslationKeys.ScriptsVariableProvider),
        new("ERROR_MESSAGE", TranslationKeys.ScriptsVariableErrorMessage),
    };

    private static readonly VariableInfo[] Streams =
    {
        new("ACTIVE_STREAMS", TranslationKeys.ScriptsVariableActiveStreams),
        new("ACTIVE_TRANSCODES", TranslationKeys.ScriptsVariableActiveTranscodes),
    };

    private static readonly VariableInfo[] Recording =
    {
        new("TIMER_ID", TranslationKeys.ScriptsVariableTimerId),
        new("PROGRAM_ID", TranslationKeys.ScriptsVariableProgramId),
    };

    private static readonly VariableInfo[] Activity =
    {
        new("ACTIVITY_NAME", TranslationKeys.ScriptsVariableActivityName),
        new("ACTIVITY_TYPE", TranslationKeys.ScriptsVariableActivityType),
        new("ACTIVITY_OVERVIEW", TranslationKeys.ScriptsVariableActivityOverview),
        new("ACTIVITY_SEVERITY", TranslationKeys.ScriptsVariableActivitySeverity),
        new("USER_ID", TranslationKeys.ScriptsVariableUserIdActivity),
    };

    private static readonly VariableInfo[] Device =
    {
        new("DEVICE_ID", TranslationKeys.ScriptsVariableDeviceId),
        new("DEVICE_NAME", TranslationKeys.ScriptsVariableDeviceNameDevice),
    };

    private static readonly VariableInfo[] LibrarySettings = { new("LIBRARY_PATH", TranslationKeys.ScriptsVariableLibraryPath) };

    private static readonly VariableInfo[] Settings = { new("SETTINGS_KEY", TranslationKeys.ScriptsVariableSettingsKey) };

    private static readonly VariableInfo[] AuthFailure =
    {
        new("USERNAME_ATTEMPTED", TranslationKeys.ScriptsVariableUsernameAttempted),
        new("USER_ID", TranslationKeys.ScriptsVariableUserIdAuthFailure),
    };

    private static readonly VariableInfo[] TaskStart =
    {
        new("TASK_NAME", TranslationKeys.ScriptsVariableTaskName),
        new("TASK_KEY", TranslationKeys.ScriptsVariableTaskKey),
        new("TASK_CATEGORY", TranslationKeys.ScriptsVariableTaskCategory),
    };

    private static readonly VariableInfo[] TaskEnd =
    {
        new("TASK_STATUS", TranslationKeys.ScriptsVariableTaskStatus),
        new("TASK_ERROR", TranslationKeys.ScriptsVariableTaskError),
        new("TASK_DURATION_SECONDS", TranslationKeys.ScriptsVariableTaskDurationSeconds),
    };

    private static readonly VariableInfo[] PluginVars =
    {
        new("PLUGIN_ID", TranslationKeys.ScriptsVariablePluginId),
        new("PLUGIN_NAME", TranslationKeys.ScriptsVariablePluginName),
        new("PLUGIN_VERSION", TranslationKeys.ScriptsVariablePluginVersion),
    };

    private static readonly VariableInfo[] ItemAll = Item.Concat(ForEachItem).ToArray();

    private static readonly VariableInfo[] UserAll = User.Concat(ForEachUser).ToArray();

    private static readonly Dictionary<ConditionField, string> FieldVariables = new()
    {
        [ConditionField.User] = "USER_ID",
        [ConditionField.Library] = "LIBRARY_ID",
        [ConditionField.ItemName] = "ITEM_NAME",
        [ConditionField.SeriesName] = "SERIES_NAME",
        [ConditionField.Client] = "CLIENT",
        [ConditionField.DeviceName] = "DEVICE_NAME",
        [ConditionField.PlayMethod] = "PLAY_METHOD",
        [ConditionField.IsLocal] = "IS_LOCAL",
        [ConditionField.PlayedToCompletion] = "PLAYED_TO_COMPLETION",
        [ConditionField.UsernameAttempted] = "USERNAME_ATTEMPTED",
        [ConditionField.TaskKey] = "TASK_KEY",
        [ConditionField.TaskStatus] = "TASK_STATUS",
        [ConditionField.Hour] = "HOUR",
        [ConditionField.DayOfWeek] = "DAY_OF_WEEK",
        [ConditionField.ItemPath] = "ITEM_PATH",
        [ConditionField.Genre] = "GENRES",
        [ConditionField.Tag] = "TAGS",
        [ConditionField.Studio] = "STUDIOS",
        [ConditionField.OfficialRating] = "OFFICIAL_RATING",
        [ConditionField.Year] = "YEAR",
        [ConditionField.VideoHeight] = "VIDEO_HEIGHT",
        [ConditionField.CommunityRating] = "COMMUNITY_RATING",
        [ConditionField.ProgressPercent] = "PROGRESS_PERCENT",
        [ConditionField.TranscodeReasons] = "TRANSCODE_REASONS",
        [ConditionField.VideoCodec] = "VIDEO_CODEC",
        [ConditionField.AudioCodec] = "AUDIO_CODEC",
        [ConditionField.RemoteAddress] = "REMOTE_ADDRESS",
        [ConditionField.DeviceId] = "DEVICE_ID",
        [ConditionField.ClientVersion] = "CLIENT_VERSION",
        [ConditionField.IsAdmin] = "IS_ADMIN",
        [ConditionField.TaskCategory] = "TASK_CATEGORY",
        [ConditionField.PluginName] = "PLUGIN_NAME",
        [ConditionField.IsFavorite] = "IS_FAVORITE",
        [ConditionField.IsPlayed] = "IS_PLAYED",
        [ConditionField.CollectionName] = "COLLECTION_NAME",
        [ConditionField.Provider] = "PROVIDER",
        [ConditionField.UpdateReason] = "UPDATE_REASON",
        [ConditionField.ActiveTranscodes] = "ACTIVE_TRANSCODES",
        [ConditionField.ActiveStreams] = "ACTIVE_STREAMS",
        [ConditionField.ActivityType] = "ACTIVITY_TYPE",
        [ConditionField.ActivitySeverity] = "ACTIVITY_SEVERITY",
        [ConditionField.DaysSinceAdded] = "DAYS_SINCE_ADDED",
        [ConditionField.HasSubtitles] = "HAS_SUBTITLES",
        [ConditionField.PlayedByAnyone] = "PLAYED_BY_ANYONE",
        [ConditionField.DaysSincePlayed] = "DAYS_SINCE_PLAYED",
        [ConditionField.DaysSinceActive] = "DAYS_SINCE_ACTIVE",
        [ConditionField.IsDisabled] = "IS_DISABLED",
    };

    private static readonly Dictionary<ScriptTrigger, TriggerInfo> Triggers = new[]
    {
        PlaybackTrigger(ScriptTrigger.PlaybackStart, TranslationKeys.ScriptsTriggerPlaybackStart, TranslationKeys.ScriptsTriggerPlaybackStartDescription),
        PlaybackTrigger(ScriptTrigger.PlaybackStop, TranslationKeys.ScriptsTriggerPlaybackStop, TranslationKeys.ScriptsTriggerPlaybackStopDescription, Stop),
        PlaybackTrigger(ScriptTrigger.PlaybackPause, TranslationKeys.ScriptsTriggerPlaybackPause, TranslationKeys.ScriptsTriggerPlaybackPauseDescription),
        PlaybackTrigger(ScriptTrigger.PlaybackResume, TranslationKeys.ScriptsTriggerPlaybackResume, TranslationKeys.ScriptsTriggerPlaybackResumeDescription),
        PlaybackTrigger(ScriptTrigger.PlaybackPausedFor, TranslationKeys.ScriptsTriggerPlaybackPausedFor, TranslationKeys.ScriptsTriggerPlaybackPausedForDescription, Paused),
        PlaybackTrigger(ScriptTrigger.PlaybackPlayingFor, TranslationKeys.ScriptsTriggerPlaybackPlayingFor, TranslationKeys.ScriptsTriggerPlaybackPlayingForDescription, Playing),
        PlaybackTrigger(ScriptTrigger.PlaybackProgressReached, TranslationKeys.ScriptsTriggerPlaybackProgressReached, TranslationKeys.ScriptsTriggerPlaybackProgressReachedDescription),
        PlaybackTrigger(ScriptTrigger.SwitchedToTranscode, TranslationKeys.ScriptsTriggerSwitchedToTranscode, TranslationKeys.ScriptsTriggerSwitchedToTranscodeDescription),
        PlaybackTrigger(ScriptTrigger.StreamCountReached, TranslationKeys.ScriptsTriggerStreamCountReached, TranslationKeys.ScriptsTriggerStreamCountReachedDescription),
        PlaybackTrigger(ScriptTrigger.TranscodeCountReached, TranslationKeys.ScriptsTriggerTranscodeCountReached, TranslationKeys.ScriptsTriggerTranscodeCountReachedDescription),

        Build(ScriptTrigger.ItemAdded, TranslationKeys.ScriptsTriggerItemAdded, TranslationKeys.TasksLibraryCategory, TranslationKeys.ScriptsTriggerItemAddedDescription, true, false, Item),
        Build(ScriptTrigger.ItemUpdated, TranslationKeys.ScriptsTriggerItemUpdated, TranslationKeys.TasksLibraryCategory, TranslationKeys.ScriptsTriggerItemUpdatedDescription, true, false, Item, Update),
        Build(ScriptTrigger.ItemRemoved, TranslationKeys.ScriptsTriggerItemRemoved, TranslationKeys.TasksLibraryCategory, TranslationKeys.ScriptsTriggerItemRemovedDescription, true, false, Item),
        Build(ScriptTrigger.CollectionCreated, TranslationKeys.ScriptsTriggerCollectionCreated, TranslationKeys.TasksLibraryCategory, TranslationKeys.ScriptsTriggerCollectionCreatedDescription, true, false, Item, Collection),
        Build(ScriptTrigger.ItemAddedToCollection, TranslationKeys.ScriptsTriggerItemAddedToCollection, TranslationKeys.TasksLibraryCategory, TranslationKeys.ScriptsTriggerItemAddedToCollectionDescription, true, false, Item, Collection),
        Build(ScriptTrigger.ItemRemovedFromCollection, TranslationKeys.ScriptsTriggerItemRemovedFromCollection, TranslationKeys.TasksLibraryCategory, TranslationKeys.ScriptsTriggerItemRemovedFromCollectionDescription, true, false, Item, Collection),
        Build(ScriptTrigger.SubtitleDownloadFailed, TranslationKeys.ScriptsTriggerSubtitleDownloadFailed, TranslationKeys.TasksLibraryCategory, TranslationKeys.ScriptsTriggerSubtitleDownloadFailedDescription, true, false, Item, Failure),
        Build(ScriptTrigger.LyricDownloadFailed, TranslationKeys.ScriptsTriggerLyricDownloadFailed, TranslationKeys.TasksLibraryCategory, TranslationKeys.ScriptsTriggerLyricDownloadFailedDescription, true, false, Item, Failure),
        Build(ScriptTrigger.MetadataRefreshStarted, TranslationKeys.ScriptsTriggerMetadataRefreshStarted, TranslationKeys.TasksLibraryCategory, TranslationKeys.ScriptsTriggerMetadataRefreshStartedDescription, true, false, Item),
        Build(ScriptTrigger.MetadataRefreshCompleted, TranslationKeys.ScriptsTriggerMetadataRefreshCompleted, TranslationKeys.TasksLibraryCategory, TranslationKeys.ScriptsTriggerMetadataRefreshCompletedDescription, true, false, Item),
        Build(ScriptTrigger.LibrarySettingsChanged, TranslationKeys.ScriptsTriggerLibrarySettingsChanged, TranslationKeys.TasksLibraryCategory, TranslationKeys.ScriptsTriggerLibrarySettingsChangedDescription, false, false, LibrarySettings),

        Build(ScriptTrigger.RecordingScheduled, TranslationKeys.ScriptsTriggerRecordingScheduled, TranslationKeys.LiveTV, TranslationKeys.ScriptsTriggerRecordingScheduledDescription, true, false, Recording, Item),
        Build(ScriptTrigger.RecordingCancelled, TranslationKeys.ScriptsTriggerRecordingCancelled, TranslationKeys.LiveTV, TranslationKeys.ScriptsTriggerRecordingCancelledDescription, true, false, Recording, Item),
        Build(ScriptTrigger.SeriesRecordingScheduled, TranslationKeys.ScriptsTriggerSeriesRecordingScheduled, TranslationKeys.LiveTV, TranslationKeys.ScriptsTriggerSeriesRecordingScheduledDescription, true, false, Recording, Item),
        Build(ScriptTrigger.SeriesRecordingCancelled, TranslationKeys.ScriptsTriggerSeriesRecordingCancelled, TranslationKeys.LiveTV, TranslationKeys.ScriptsTriggerSeriesRecordingCancelledDescription, true, false, Recording, Item),

        Build(ScriptTrigger.MarkedPlayed, TranslationKeys.ScriptsTriggerMarkedPlayed, TranslationKeys.HeaderActivity, TranslationKeys.ScriptsTriggerMarkedPlayedDescription, true, false, User, Item, UserData),
        Build(ScriptTrigger.MarkedUnplayed, TranslationKeys.ScriptsTriggerMarkedUnplayed, TranslationKeys.HeaderActivity, TranslationKeys.ScriptsTriggerMarkedUnplayedDescription, true, false, User, Item, UserData),
        Build(ScriptTrigger.FavoriteChanged, TranslationKeys.ScriptsTriggerFavoriteChanged, TranslationKeys.HeaderActivity, TranslationKeys.ScriptsTriggerFavoriteChangedDescription, true, false, User, Item, UserData),

        Build(ScriptTrigger.SessionStarted, TranslationKeys.ScriptsTriggerSessionStarted, TranslationKeys.ScriptsTriggerGroupSessions, TranslationKeys.ScriptsTriggerSessionStartedDescription, false, true, User, Session),
        Build(ScriptTrigger.SessionEnded, TranslationKeys.ScriptsTriggerSessionEnded, TranslationKeys.ScriptsTriggerGroupSessions, TranslationKeys.ScriptsTriggerSessionEndedDescription, false, true, User, Session),
        Build(ScriptTrigger.AuthenticationSuccess, TranslationKeys.LabelSignedIn, TranslationKeys.ScriptsTriggerGroupSessions, TranslationKeys.ScriptsTriggerAuthenticationSuccessDescription, false, true, User, Session),
        Build(ScriptTrigger.NewDeviceSeen, TranslationKeys.ScriptsTriggerNewDeviceSeen, TranslationKeys.ScriptsTriggerGroupSessions, TranslationKeys.ScriptsTriggerNewDeviceSeenDescription, false, true, User, Session),
        Build(ScriptTrigger.ClientConnected, TranslationKeys.ScriptsTriggerClientConnected, TranslationKeys.ScriptsTriggerGroupSessions, TranslationKeys.ScriptsTriggerClientConnectedDescription, false, true, User, Session),
        Build(ScriptTrigger.DeviceSettingsChanged, TranslationKeys.ScriptsTriggerDeviceSettingsChanged, TranslationKeys.ScriptsTriggerGroupSessions, TranslationKeys.ScriptsTriggerDeviceSettingsChangedDescription, false, false, Device),
        Build(ScriptTrigger.AuthenticationFailure, TranslationKeys.ScriptsTriggerAuthenticationFailure, TranslationKeys.ScriptsTriggerGroupSessions, TranslationKeys.ScriptsTriggerAuthenticationFailureDescription, false, false, AuthFailure, Session.Where(v => v.Name != "SESSION_ID")),

        Build(ScriptTrigger.UserCreated, TranslationKeys.ScriptsTriggerUserCreated, TranslationKeys.HeaderAccounts, TranslationKeys.ScriptsTriggerUserCreatedDescription, false, false, User),
        Build(ScriptTrigger.UserDeleted, TranslationKeys.ActivityUsersDeletedOne, TranslationKeys.HeaderAccounts, TranslationKeys.ScriptsTriggerUserDeletedDescription, false, false, User),
        Build(ScriptTrigger.UserUpdated, TranslationKeys.ScriptsTriggerUserUpdated, TranslationKeys.HeaderAccounts, TranslationKeys.ScriptsTriggerUserUpdatedDescription, false, false, User),
        Build(ScriptTrigger.UserLockedOut, TranslationKeys.NotificationOptionUserLockedOut, TranslationKeys.HeaderAccounts, TranslationKeys.ScriptsTriggerUserLockedOutDescription, false, false, User),
        Build(ScriptTrigger.UserPasswordChanged, TranslationKeys.ScriptsTriggerUserPasswordChanged, TranslationKeys.HeaderAccounts, TranslationKeys.ScriptsTriggerUserPasswordChangedDescription, false, false, User),

        Build(ScriptTrigger.ServerStarted, TranslationKeys.ScriptsTriggerServerStarted, TranslationKeys.LabelSystem, TranslationKeys.ScriptsTriggerServerStartedDescription, false, false),
        Build(ScriptTrigger.PendingRestart, TranslationKeys.ScriptsTriggerPendingRestart, TranslationKeys.LabelSystem, TranslationKeys.ScriptsTriggerPendingRestartDescription, false, false),
        Build(ScriptTrigger.TaskStarted, TranslationKeys.ScriptsTriggerTaskStarted, TranslationKeys.LabelSystem, TranslationKeys.ScriptsTriggerTaskStartedDescription, false, false, TaskStart),
        Build(ScriptTrigger.TaskCompleted, TranslationKeys.ScriptsTriggerTaskCompleted, TranslationKeys.LabelSystem, TranslationKeys.ScriptsTriggerTaskCompletedDescription, false, false, TaskStart, TaskEnd),
        Build(ScriptTrigger.ActivityLogged, TranslationKeys.ScriptsTriggerActivityLogged, TranslationKeys.LabelSystem, TranslationKeys.ScriptsTriggerActivityLoggedDescription, false, false, Activity),
        Build(ScriptTrigger.ServerSettingsChanged, TranslationKeys.ScriptsTriggerServerSettingsChanged, TranslationKeys.LabelSystem, TranslationKeys.ScriptsTriggerServerSettingsChangedDescription, false, false, Settings),
        Build(ScriptTrigger.NetworkChanged, TranslationKeys.ScriptsTriggerNetworkChanged, TranslationKeys.LabelSystem, TranslationKeys.ScriptsTriggerNetworkChangedDescription, false, false),
        Build(ScriptTrigger.PluginInstalling, TranslationKeys.ScriptsTriggerPluginInstalling, TranslationKeys.LabelSystem, TranslationKeys.ScriptsTriggerPluginInstallingDescription, false, false, PluginVars),
        Build(ScriptTrigger.PluginInstallationCancelled, TranslationKeys.ScriptsTriggerPluginInstallationCancelled, TranslationKeys.LabelSystem, TranslationKeys.ScriptsTriggerPluginInstallationCancelledDescription, false, false, PluginVars),
        Build(ScriptTrigger.PluginInstalled, TranslationKeys.NotificationOptionPluginInstalled, TranslationKeys.LabelSystem, TranslationKeys.ScriptsTriggerPluginInstalledDescription, false, false, PluginVars),
        Build(ScriptTrigger.PluginUpdated, TranslationKeys.ScriptsTriggerPluginUpdated, TranslationKeys.LabelSystem, TranslationKeys.ScriptsTriggerPluginUpdatedDescription, false, false, PluginVars),
        Build(ScriptTrigger.PluginUninstalled, TranslationKeys.NotificationOptionPluginUninstalled, TranslationKeys.LabelSystem, TranslationKeys.ScriptsTriggerPluginUninstalledDescription, false, false, PluginVars),

        Build(ScriptTrigger.Schedule, TranslationKeys.ScriptsTriggerSchedule, TranslationKeys.Schedule, TranslationKeys.ScriptsTriggerScheduleDescription, false, false),
        Build(ScriptTrigger.Webhook, TranslationKeys.ScriptsTriggerWebhook, TranslationKeys.ScriptsTriggerGroupOnDemand, TranslationKeys.ScriptsTriggerWebhookDescription, false, false, WebhookVars),
        Build(ScriptTrigger.Manual, TranslationKeys.ButtonRunTask, TranslationKeys.ScriptsTriggerGroupOnDemand, TranslationKeys.ScriptsTriggerManualDescription, false, false) with { Selectable = false },
    }.ToDictionary(t => t.Trigger);

    /// <summary>Gets the variables a run started by another script's failure also receives.</summary>
    public static IReadOnlyList<VariableInfo> FailureVariables { get; } = new VariableInfo[]
    {
        new("FAILED_SCRIPT_NAME", TranslationKeys.ScriptsVariableFailedScriptName),
        new("FAILED_STEP", TranslationKeys.ScriptsVariableFailedStep),
        new("FAILED_MESSAGE", TranslationKeys.ScriptsVariableFailedMessage),
        new("FAILED_COUNT", TranslationKeys.ScriptsVariableFailedCount),
    };

    /// <summary>Gets every trigger, in editor order.</summary>
    public static IReadOnlyList<TriggerInfo> All { get; } = Triggers.Values.ToList();

    /// <summary>
    /// Gets whether a name is one the plugin fills itself, so a step may not save its output under it.
    /// </summary>
    /// <param name="name">The variable name without the prefix.</param>
    /// <returns>True when the name is taken.</returns>
    public static bool IsReserved(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.StartsWith(SecretPrefix, StringComparison.Ordinal)
            || name.StartsWith(WebhookPrefix, StringComparison.Ordinal)
            || ReservedNames().Contains(name);
    }

    private static HashSet<string> ReservedNames()
        => _reserved ??= All
            .SelectMany(t => t.Variables)
            .Concat(ItemAll)
            .Concat(UserAll)
            .Concat(FailureVariables)
            .Select(v => v.Name)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Gets what a trigger supplies.
    /// </summary>
    /// <param name="trigger">The trigger.</param>
    /// <returns>The trigger's description.</returns>
    public static TriggerInfo Get(ScriptTrigger trigger) => Triggers[trigger];

    /// <summary>
    /// Gets the variable a condition field reads.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <returns>The variable name without the prefix.</returns>
    public static string VariableFor(ConditionField field) => FieldVariables[field];

    /// <summary>
    /// Gets whether a trigger supplies the value a condition field reads.
    /// </summary>
    /// <param name="trigger">The trigger.</param>
    /// <param name="field">The field.</param>
    /// <returns>True when the field applies to the trigger.</returns>
    public static bool Supplies(ScriptTrigger trigger, ConditionField field)
    {
        var name = VariableFor(field);
        return Get(trigger).Variables.Any(v => v.Name == name);
    }

    /// <summary>
    /// Gets whether a run supplies the value a condition field reads, counting what running for each item or
    /// user adds to the event.
    /// </summary>
    /// <param name="trigger">The trigger.</param>
    /// <param name="field">The field.</param>
    /// <param name="forEach">What the run is for, beyond the event itself.</param>
    /// <returns>True when the field applies.</returns>
    public static bool Supplies(ScriptTrigger trigger, ConditionField field, ForEachMode forEach)
    {
        if (Supplies(trigger, field))
        {
            return true;
        }

        var name = VariableFor(field);
        return ForEachVariables(forEach).Any(v => v.Name == name);
    }

    /// <summary>
    /// Gets whether a script set to run for each item or user does so on a trigger: only when the event has no
    /// item, or no user, of its own.
    /// </summary>
    /// <param name="trigger">The trigger.</param>
    /// <param name="forEach">The script's setting.</param>
    /// <returns>True when the event is expanded.</returns>
    public static bool Expands(ScriptTrigger trigger, ForEachMode forEach)
        => forEach switch
        {
            ForEachMode.Items => !Get(trigger).HasItem,
            ForEachMode.Users => !Get(trigger).HasUser,
            _ => false
        };

    /// <summary>
    /// Gets the variables running for each item or user adds to an event.
    /// </summary>
    /// <param name="forEach">The mode.</param>
    /// <returns>The variables, empty for <see cref="ForEachMode.None"/>.</returns>
    public static IReadOnlyList<VariableInfo> ForEachVariables(ForEachMode forEach)
        => forEach switch
        {
            ForEachMode.Items => ItemAll,
            ForEachMode.Users => UserAll,
            _ => Array.Empty<VariableInfo>()
        };

    private static TriggerInfo PlaybackTrigger(ScriptTrigger trigger, string label, string description, params VariableInfo[] extra)
        => Build(trigger, label, TranslationKeys.TitlePlayback, description, true, true, User, Session, Item, PlaybackVars, Streams, extra);

    private static TriggerInfo Build(ScriptTrigger trigger, string label, string category, string description, bool hasItem, bool hasSession, params IEnumerable<VariableInfo>[] groups)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var variables = Common.Concat(groups.SelectMany(g => g)).Where(v => seen.Add(v.Name)).ToList();
        return new TriggerInfo(trigger, label, category, description, hasItem, hasSession, variables);
    }
}
