// necto_groups_templates.js
// The template sections of the group editor. A group's template writes Jellyfin's own Access and features and
// Preferences onto its members, and each setting is either left to the member or set by the group.

import * as nectoModule from './configurationpage?name=necto_shared.js';

var h = nectoModule.h;

// Ids for the fields, so each label names its own field.
var fieldSeq = 0;

// sameId
// Compares two ids whatever their case or dashes.
//
// Inputs
// a [string]: One id.
// b [string]: The other id.
//
// Output
// [boolean]: Whether they name the same thing.
function sameId(a, b) {
    var clean = function (v) { return String(v || '').replace(/-/g, '').toLowerCase(); };
    return clean(a) === clean(b);
}

// visibleViews
// Leaves out the libraries a group's Library access hides from its members. Views that are not libraries, such as
// Live TV and Collections, always stay.
//
// Inputs
// views [object[]]: The views, each with Id and Name.
// libraries [object[]]: The server's libraries, each with ItemId.
// access [object]: The Library access row as { inherit, value: { all, ids } }, or null.
//
// Output
// [object[]]: The views members can see.
export function visibleViews(views, libraries, access) {
    if (!access || access.inherit || !access.value || access.value.all) return (views || []).slice();
    var ids = access.value.ids || [];
    var libraryIds = (libraries || []).map(function (l) { return l.ItemId || l.Id; });
    return (views || []).filter(function (v) {
        var isLibrary = libraryIds.some(function (id) { return sameId(id, v.Id); });
        return !isLibrary || ids.some(function (id) { return sameId(id, v.Id); });
    });
}

// mergeOrder
// Puts the order shown back together with the libraries the access row hides, so a hidden library keeps a place.
//
// Inputs
// full [string[]]: Every view id in the last order.
// shown [string[]]: The ids shown, in their order now.
//
// Output
// [string[]]: The shown ids, then the hidden ones in their last order.
export function mergeOrder(full, shown) {
    var rest = (full || []).filter(function (id) { return !(shown || []).some(function (s) { return sameId(s, id); }); });
    return (shown || []).concat(rest);
}

// mergeGrid
// Reads a library checkbox row back with the libraries the access row hides, which keep what they had.
//
// Inputs
// invert [boolean]: Whether the row saves what is left out.
// all [string[]]: Every view id.
// shown [string[]]: The view ids shown.
// checked [string[]]: The ids checked among those shown.
// previous [string[]]: The ids the row saved last.
//
// Output
// [string[]]: The ids to save.
export function mergeGrid(invert, all, shown, checked, previous) {
    var has = function (list, id) { return (list || []).some(function (x) { return sameId(x, id); }); };
    var hidden = (all || []).filter(function (id) { return !has(shown, id); });
    var kept = hidden.filter(function (id) { return has(previous, id); });
    var visible = invert ? (shown || []).filter(function (id) { return !has(checked, id); }) : (checked || []).slice();
    return visible.concat(kept);
}

// HOME_SECTION_COUNT
// How many home screen sections the web client shows.
export var HOME_SECTION_COUNT = 10;

// DEFAULT_HOME_SECTIONS
// Jellyfin's own home layout, used while a group has set none.
export var DEFAULT_HOME_SECTIONS = ['smalllibrarytiles', 'resume', 'resumeaudio', 'resumebook', 'livetv', 'nextup', 'latestmedia', 'none', 'none', 'none'];

// accessSpecs
// Describes the Access and features rows, by section, with Jellyfin's own labels.
//
// Inputs
// t [object]: The strings.
//
// Output
// [object[]]: Each section's title and rows. A row's key names its value in the template, and its type its field.
export function accessSpecs(t) {
    return [
        { title: t.tr('HeaderLibraryAccess'), rows: [
            { key: 'LibraryAccess', type: 'library', label: t.tr('OptionEnableAccessToAllLibraries') },
            { key: 'ChannelAccess', type: 'channels', label: t.tr('OptionEnableAccessToAllChannels') }
        ] },
        { title: t.tr('HeaderDeviceAccess'), rows: [
            { key: 'DeviceAccess', type: 'devices', label: t.tr('OptionEnableAccessFromAllDevices') }
        ] },
        { title: t.tr('GroupsTemplateMediaDeletion'), rows: [
            { key: 'EnableContentDeletion', type: 'deletion', label: t.tr('GroupsTemplateDeleteAll') }
        ] },
        { title: t.tr('HeaderPlayback'), rows: [
            { key: 'EnableMediaPlayback', type: 'bool', label: t.tr('OptionAllowMediaPlayback') },
            { key: 'EnableAudioPlaybackTranscoding', type: 'bool', label: t.tr('OptionAllowAudioPlaybackTranscoding') },
            { key: 'EnableVideoPlaybackTranscoding', type: 'bool', label: t.tr('OptionAllowVideoPlaybackTranscoding') },
            { key: 'EnablePlaybackRemuxing', type: 'bool', label: t.tr('OptionAllowVideoPlaybackRemuxing') },
            { key: 'ForceRemoteSourceTranscoding', type: 'bool', label: t.tr('OptionForceRemoteSourceTranscoding') },
            { key: 'RemoteClientBitrateLimit', type: 'bitrate', label: t.tr('LabelRemoteClientBitrateLimit'), help: t.tr('HelpZeroNoLimit') },
            { key: 'SyncPlayAccess', type: 'enum', label: t.tr('LabelSyncPlayAccess'), options: [
                ['CreateAndJoinGroups', t.tr('LabelSyncPlayAccessCreateAndJoinGroups')],
                ['JoinGroups', t.tr('LabelSyncPlayAccessJoinGroups')],
                ['None', t.tr('LabelSyncPlayAccessNone')]
            ] }
        ] },
        { title: t.tr('LiveTV'), rows: [
            { key: 'EnableLiveTvAccess', type: 'bool', label: t.tr('OptionAllowBrowsingLiveTv') },
            { key: 'EnableLiveTvManagement', type: 'bool', label: t.tr('OptionAllowManageLiveTv') }
        ] },
        { title: t.tr('HeaderRemoteControl'), rows: [
            { key: 'EnableRemoteControlOfOtherUsers', type: 'bool', label: t.tr('OptionAllowRemoteControlOthers') },
            { key: 'EnableSharedDeviceControl', type: 'bool', label: t.tr('OptionAllowRemoteSharedDevices') }
        ] },
        { title: t.tr('TabParentalControl'), rows: [
            { key: 'MaxParentalRating', type: 'rating', label: t.tr('LabelMaxParentalRating') },
            { key: 'BlockUnratedItems', type: 'unrated', label: t.tr('HeaderBlockItemsWithNoRating') },
            { key: 'AllowedTags', type: 'tags', label: t.tr('LabelAllowContentWithTags') },
            { key: 'BlockedTags', type: 'tags', label: t.tr('LabelBlockContentWithTags') },
            { key: 'AccessSchedules', type: 'schedule', label: t.tr('HeaderAccessSchedule') }
        ] },
        { title: t.tr('Profile'), rows: [
            { key: 'EnableRemoteAccess', type: 'bool', label: t.tr('AllowRemoteAccess') },
            { key: 'EnableCollectionManagement', type: 'bool', label: t.tr('AllowCollectionManagement') },
            { key: 'EnableSubtitleManagement', type: 'bool', label: t.tr('AllowSubtitleManagement') },
            { key: 'EnableLyricManagement', type: 'bool', label: t.tr('GroupsTemplateLyrics') },
            { key: 'EnableUserPreferenceAccess', type: 'bool', label: t.tr('GroupsTemplatePreferenceAccess') },
            { key: 'EnableContentDownloading', type: 'bool', label: t.tr('OptionAllowContentDownload') },
            { key: 'EnableMediaConversion', type: 'bool', label: t.tr('AllowMediaConversion') },
            { key: 'EnableSyncTranscoding', type: 'bool', label: t.tr('GroupsTemplateSyncTranscoding') },
            { key: 'EnablePublicSharing', type: 'bool', label: t.tr('GroupsTemplatePublicSharing') },
            { key: 'IsDisabled', type: 'bool', label: t.tr('OptionDisableUser'), help: t.tr('GroupsTemplateDisableHelp') },
            { key: 'IsHidden', type: 'bool', label: t.tr('OptionHideUser') },
            { key: 'LoginAttemptsBeforeLockout', type: 'int', label: t.tr('LabelUserLoginAttemptsBeforeLockout'), min: -1, help: t.tr('GroupsTemplateLockoutHelp') },
            { key: 'MaxActiveSessions', type: 'int', label: t.tr('LabelUserMaxActiveSessions'), min: 0, help: t.tr('HelpZeroNoLimit') }
        ] }
    ];
}

// preferenceSpecs
// Describes the Preferences rows, by section, with the labels of a user's own preference pages.
//
// Inputs
// t [object]: The strings.
//
// Output
// [object[]]: Each section's title and rows.
export function preferenceSpecs(t) {
    return [
        { title: t.tr('Subtitles'), rows: [
            { key: 'SubtitleMode', type: 'enum', label: t.tr('LabelSubtitlePlaybackMode'), options: [
                ['Default', t.tr('Default')], ['Smart', t.tr('Smart')], ['OnlyForced', t.tr('OnlyForcedSubtitles')], ['Always', t.tr('AlwaysPlaySubtitles')], ['None', t.tr('None')]
            ], optionHelp: {
                Default: t.tr('DefaultSubtitlesHelp'), Smart: t.tr('SmartSubtitlesHelp'), OnlyForced: t.tr('OnlyForcedSubtitlesHelp'),
                Always: t.tr('AlwaysPlaySubtitlesHelp'), None: t.tr('NoSubtitlesHelp')
            } },
            { key: 'SubtitleLanguagePreference', type: 'culture', label: t.tr('LabelPreferredSubtitleLanguage') },
            { key: 'RememberSubtitleSelections', type: 'bool', label: t.tr('RememberSubtitleSelections') }
        ] },
        { title: t.tr('TitlePlayback'), rows: [
            { key: 'AudioLanguagePreference', type: 'culture', label: t.tr('LabelAudioLanguagePreference') },
            { key: 'PlayDefaultAudioTrack', type: 'bool', label: t.tr('LabelPlayDefaultAudioTrack') },
            { key: 'RememberAudioSelections', type: 'bool', label: t.tr('RememberAudioSelections') },
            { key: 'EnableNextEpisodeAutoPlay', type: 'bool', label: t.tr('PlayNextEpisodeAutomatically') },
            { key: 'CastReceiverId', type: 'cast', label: t.tr('LabelChromecastVersion') }
        ] },
        { title: t.tr('Display'), rows: [
            { key: 'DisplayMissingEpisodes', type: 'bool', label: t.tr('DisplayMissingEpisodesWithinSeasons') },
            { key: 'DisplayCollectionsView', type: 'bool', label: t.tr('GroupsTemplateCollectionsView') }
        ] },
        { title: t.tr('Home'), rows: [
            { key: 'HomeSections', type: 'homesections', label: t.tr('GroupsTemplateHomeSections') },
            { key: 'HidePlayedInLatest', type: 'bool', label: t.tr('GroupsTemplateHideWatched') },
            { key: 'OrderedViews', type: 'order', label: t.tr('GroupsTemplateLibraryOrder') },
            { key: 'MyMediaExcludes', type: 'libgrid', invert: true, label: t.tr('GroupsTemplateHomeLibraries') },
            { key: 'LatestItemsExcludes', type: 'libgrid', invert: true, label: t.tr('GroupsTemplateLatestLibraries') },
            { key: 'GroupedFolders', type: 'libgrid', source: 'groupable', label: t.tr('GroupsTemplateGroupedFolders') }
        ] }
    ];
}

// homeSectionOptions
// Lists the home screen section choices, in the web client's order and words.
//
// Inputs
// t [object]: The strings.
//
// Output
// [string[][]]: Each choice's value and label.
export function homeSectionOptions(t) {
    return [
        ['smalllibrarytiles', t.tr('HeaderMyMedia')], ['librarybuttons', t.tr('HeaderMyMediaSmall')], ['activerecordings', t.tr('HeaderActiveRecordings')],
        ['resume', t.tr('HeaderContinueWatching')], ['resumeaudio', t.tr('HeaderContinueListening')], ['resumebook', t.tr('HeaderContinueReading')],
        ['latestmedia', t.tr('HeaderLatestMedia')], ['nextup', t.tr('NextUp')], ['livetv', t.tr('LiveTV')], ['none', t.tr('None')]
    ];
}

// manageKey
// Names the switch that says whether the group sets a row, such as ManageLibraryAccess.
//
// Inputs
// row [object]: The row.
//
// Output
// [string]: The switch's name in the template.
export function manageKey(row) {
    return 'Manage' + row.key;
}

// rowValue
// Reads a row's value out of a template part, in the shape its field takes.
//
// Inputs
// part [object]: The Access or Preferences part of a template.
// row [object]: The row.
//
// Output
// [object]: { manage, value }.
export function rowValue(part, row) {
    part = part || {};
    var manage = part[manageKey(row)] === true;
    var value;
    switch (row.type) {
        case 'library': value = { all: part.EnableAllFolders !== false, ids: (part.EnabledFolders || []).slice() }; break;
        case 'channels': value = { all: part.EnableAllChannels !== false, ids: (part.EnabledChannels || []).slice() }; break;
        case 'devices': value = { all: part.EnableAllDevices !== false, ids: (part.EnabledDevices || []).slice() }; break;
        case 'deletion': value = { all: part.EnableContentDeletion === true, ids: (part.EnableContentDeletionFromFolders || []).slice() }; break;
        case 'rating': value = ratingValue(part.MaxParentalRating, part.MaxParentalSubRating); break;
        case 'bitrate': value = part.RemoteClientBitrateLimit > 0 ? part.RemoteClientBitrateLimit / 1000000 : 0; break;
        case 'homesections': value = (part.HomeSections && part.HomeSections.length ? part.HomeSections : DEFAULT_HOME_SECTIONS).slice(); break;
        case 'bool': value = part[row.key] === true || (part[row.key] == null && row.fallback === true); break;
        case 'int': value = part[row.key] || 0; break;
        default: value = part[row.key] == null ? (Array.isArray(part[row.key]) ? [] : part[row.key]) : part[row.key];
    }
    return { manage: manage, value: value };
}

// ratingValue
// Names a rating score and its sub score as one choice value, such as 13 or 13:1.
//
// Inputs
// score [number]: The rating score, or null for none.
// sub [number]: The sub score, or null.
//
// Output
// [string]: The value, or an empty string for none.
export function ratingValue(score, sub) {
    if (score == null) return '';
    return sub == null ? String(score) : score + ':' + sub;
}

// parseRating
// Reads a choice value that ratingValue made back into its score and sub score.
//
// Inputs
// value [string]: The choice value.
//
// Output
// [object]: { score, sub }, each null when not set.
export function parseRating(value) {
    if (value === '' || value == null) return { score: null, sub: null };
    var parts = String(value).split(':');
    var score = parseInt(parts[0], 10);
    var sub = parts.length > 1 ? parseInt(parts[1], 10) : NaN;
    return { score: isNaN(score) ? null : score, sub: isNaN(sub) || isNaN(score) ? null : sub };
}

// ratingChoices
// Lists the parental ratings as choices, one per score and sub score. Ratings that share both are one choice
// named after each of them, as Jellyfin's own parental control page names them.
//
// Inputs
// ratings [object[]]: The ratings from the server.
//
// Output
// [string[][]]: Each choice as [value, name].
export function ratingChoices(ratings) {
    var list = [];
    var byValue = {};
    (ratings || []).forEach(function (r) {
        var rs = r.RatingScore;
        var score = rs ? rs.Score : r.Value;
        if (score == null) return;
        var value = ratingValue(score, rs && rs.SubScore != null ? rs.SubScore : null);
        if (byValue[value]) {
            byValue[value][1] += '/' + r.Name;
            return;
        }
        byValue[value] = [value, r.Name];
        list.push(byValue[value]);
    });
    return list;
}

// setRowValue
// Writes a row's field value into a template part.
//
// Inputs
// part [object]: The Access or Preferences part of a template, changed in place.
// row [object]: The row.
// entry [object]: { manage, value }, as rowValue gives it.
export function setRowValue(part, row, entry) {
    part[manageKey(row)] = !!entry.manage;
    var v = entry.value;
    switch (row.type) {
        case 'library': part.EnableAllFolders = !!v.all; part.EnabledFolders = v.all ? [] : (v.ids || []); break;
        case 'channels': part.EnableAllChannels = !!v.all; part.EnabledChannels = v.all ? [] : (v.ids || []); break;
        case 'devices': part.EnableAllDevices = !!v.all; part.EnabledDevices = v.all ? [] : (v.ids || []); break;
        case 'deletion': part.EnableContentDeletion = !!v.all; part.EnableContentDeletionFromFolders = v.all ? [] : (v.ids || []); break;
        case 'rating': {
            var rating = parseRating(v);
            part.MaxParentalRating = rating.score;
            part.MaxParentalSubRating = rating.sub;
            break;
        }
        case 'bitrate': part.RemoteClientBitrateLimit = Math.max(0, Math.round((parseFloat(v) || 0) * 1000000)); break;
        case 'int': part[row.key] = isNaN(parseInt(v, 10)) ? 0 : Math.max(row.min || 0, parseInt(v, 10)); break;
        case 'bool': part[row.key] = !!v; break;
        default: part[row.key] = v;
    }
}

// loadSources
// Reads the libraries, channels, devices, ratings, languages, cast receivers, and views the rows list.
//
// Inputs
// shared [object]: The Necto helper bag.
//
// Output
// [Promise]: Resolves with each list by name, empty where the server did not answer.
export function loadSources(shared) {
    var api = globalThis.ApiClient;
    var safe = function (fn) {
        try { return Promise.resolve(fn()).then(function (r) { return r || []; }, function () { return []; }); } catch (e) { return Promise.resolve([]); }
    };
    var items = function (r) { return (r && r.Items) || (Array.isArray(r) ? r : []); };
    var me = function () { return api.getCurrentUserId ? api.getCurrentUserId() : ''; };
    return Promise.all([
        safe(function () { return api.getVirtualFolders(); }),
        safe(function () { return api.getJSON(api.getUrl('Channels')).then(items); }),
        safe(function () { return shared.request('Devices').then(items); }),
        safe(function () { return api.getParentalRatings(); }),
        safe(function () { return api.getCultures(); }),
        safe(function () { return api.getServerConfiguration().then(function (c) { return (c && c.CastReceiverApplications) || []; }); }),
        safe(function () { return api.getJSON(api.getUrl('Users/' + me() + '/Views', { includeHidden: true })).then(items); }),
        safe(function () { return api.getJSON(api.getUrl('Users/' + me() + '/GroupingOptions')).then(items); })
    ]).then(function (r) {
        return { libraries: r[0], channels: r[1], devices: r[2], ratings: r[3], cultures: r[4], casts: r[5], views: r[6], groupable: r[7] };
    });
}

// drawTemplateSections
// Adds the Jellyfin settings sections to a group editor: whether the group sets them, then Access and features and
// Preferences, each row left to the member or set by the group. Preferences shows only while the group sets them.
//
// Inputs
// section [function]: The editor's section(title, icon, content) adder.
// draft [object]: The group being edited.
// ctx [object]: shared is the Necto helper bag.
// readers [function[]]: The editor's readers, each given the group to fill.
// handles [object[]]: The editor's handles, destroyed with it.
//
// Output
// [Promise]: Resolves once the rows are drawn.
export function drawTemplateSections(section, draft, ctx, readers, handles) {
    var shared = ctx.shared;
    var t = shared.t;
    var template = draft.Template = draft.Template || {};
    template.Access = template.Access || {};
    template.Preferences = template.Preferences || {};

    var onId = 'necto-template-on-' + (++fieldSeq);
    var on = h('input', { type: 'checkbox', is: 'emby-checkbox', id: onId, 'data-field': 'Template.Enabled' });
    on.checked = template.Enabled === true;
    var accessBody = h('div', { 'data-role': 'template-access' });
    var prefsBody = h('div', { 'data-role': 'template-preferences' });
    var intro = h('div', {}, [
        h('div', { class: 'checkboxContainer checkboxContainer-withDescription' }, [
            h('label', {}, [on, h('span', { text: t.tr('GroupsTemplateEnable') })]),
            h('div', { class: 'fieldDescription checkboxFieldDescription', text: t.tr('GroupsTemplateEnableHelp') })
        ]),
        accessBody
    ]);
    var access = section(t.tr('GroupsTemplateAccessTitle'), 'admin_panel_settings', intro);
    access.element.setAttribute('data-section', 'template-access');
    var prefs = section(t.tr('GroupsTemplatePreferencesTitle'), 'tune', prefsBody);
    prefs.element.setAttribute('data-section', 'template-preferences');
    readers.push(function (out) { out.Template = out.Template || {}; out.Template.Enabled = !!on.checked; });
    // Both tiles follow the switch. Preferences has nothing to show while it is off, so the section hides.
    handles.push(nectoModule.bindTileToSwitch(access.element, on));
    handles.push(nectoModule.bindTileToSwitch(prefs.element, on));

    var reflect = function () {
        accessBody.classList.toggle('stolon-hidden', !on.checked);
        prefs.element.classList.toggle('stolon-hidden', !on.checked);
    };
    on.addEventListener('change', reflect);
    reflect();

    var libraryAccess = null;
    var viewRefreshers = [];
    var refreshViews = function () { viewRefreshers.forEach(function (f) { f(); }); };
    var shownViews = function (views, sources) { return visibleViews(views, sources.libraries, libraryAccess ? libraryAccess.getValue() : null); };

    return loadSources(shared).then(function (sources) {
        drawPart(accessBody, accessSpecs(t), 'Access', template.Access, sources);
        drawPart(prefsBody, preferenceSpecs(t), 'Preferences', template.Preferences, sources);
    });

    // drawPart
    // Draws one part's sections and rows, and reads them back into the group.
    function drawPart(into, specs, partName, part, sources) {
        specs.forEach(function (spec) {
            into.appendChild(h('h3', { class: 'stolon-subsection-title', text: spec.title }));
            spec.rows.forEach(function (row) {
                var start = rowValue(part, row);
                var field = buildField(row, start.value, sources);
                var inherit = nectoModule.createInheritField({
                    field: field,
                    value: { inherit: !start.manage, value: start.value },
                    inheritText: t.tr('GroupsTemplateMember'),
                    overrideText: t.tr('LabelGroup'),
                    label: row.label,
                    onChange: row.type === 'library' ? refreshViews : undefined
                });
                handles.push(inherit);
                var rowElement = h('div', { class: 'necto-template-row', 'data-field': partName + '.' + row.key }, [
                    h('div', { class: 'selectLabel', text: row.label }),
                    inherit.element,
                    row.help ? h('div', { class: 'fieldDescription', text: row.help }) : null
                ]);
                if (row.type === 'library') {
                    libraryAccess = inherit;
                    rowElement.addEventListener('change', refreshViews);
                }
                into.appendChild(rowElement);
                readers.push(function (out) {
                    out.Template = out.Template || {};
                    out.Template[partName] = out.Template[partName] || {};
                    var got = inherit.getValue();
                    setRowValue(out.Template[partName], row, { manage: !got.inherit, value: got.value });
                });
            });
        });
    }

    // buildField
    // Builds the field a row's value is edited in, as an input or a handle the inherit field takes.
    function buildField(row, value, sources) {
        var id = 'necto-template-' + (++fieldSeq);
        switch (row.type) {
            case 'bool': {
                var box = h('input', { type: 'checkbox', is: 'emby-checkbox', id: id });
                box.checked = !!value;
                return wrap(h('label', {}, [box, h('span', { text: t.tr('GroupsTemplateOn') })]), function () { return box.checked; }, function (v) { box.checked = !!v; });
            }
            case 'int':
            case 'bitrate': {
                var num = h('input', { is: 'emby-input', type: 'number', id: id, min: String(row.min == null ? 0 : row.min), step: row.type === 'bitrate' ? '0.1' : '1', 'aria-label': row.label });
                num.value = String(value || 0);
                return wrap(h('div', { class: 'inputContainer' }, [num]), function () { return num.value; }, function (v) { num.value = String(v || 0); });
            }
            case 'enum':
            case 'culture':
            case 'cast':
            case 'rating': {
                var select = h('select', { is: 'emby-select', id: id, 'aria-label': row.label });
                choices(row, sources).forEach(function (o) { select.appendChild(h('option', { value: o[0], text: o[1] })); });
                select.value = value == null ? '' : String(value);
                var optionHelp = row.optionHelp ? h('div', { class: 'fieldDescription', 'data-role': 'option-help' }) : null;
                var showHelp = function () { if (optionHelp) optionHelp.textContent = row.optionHelp[select.value] || ''; };
                select.addEventListener('change', showHelp);
                showHelp();
                return wrap(h('div', { class: 'selectContainer' }, [select, optionHelp]), function () { return select.value; }, function (v) { select.value = v == null ? '' : String(v); showHelp(); });
            }
            case 'tags': {
                var tags = nectoModule.createTagInput({ values: value || [], inputLabel: row.label });
                handles.push(tags);
                return { element: tags.element, getValue: function () { return tags.getValue(); }, setValue: function (v) { tags.setValue(v || []); } };
            }
            case 'schedule': {
                var schedule = nectoModule.createAccessSchedule({ value: value || [] });
                handles.push(schedule);
                return { element: schedule.element, getValue: function () { return schedule.getValue(); }, setValue: function (v) { schedule.setValue(v || []); } };
            }
            case 'unrated': {
                var unrated = nectoModule.createCheckboxList({
                    items: [['Book', 'Books'], ['ChannelContent', 'Channels'], ['LiveTvChannel', 'LiveTV'], ['Movie', 'Movies'], ['Music', 'TabMusic'], ['Trailer', 'Trailers'], ['Series', 'Shows']].map(function (p) { return { Id: p[0], Name: t.tr(p[1]) }; }),
                    idKey: 'Id',
                    value: value || [],
                    renderRow: function (i) { return { label: i.Name }; }
                });
                handles.push(unrated);
                return { element: unrated.element, getValue: function () { return unrated.getValue(); }, setValue: function (v) { unrated.setValue(v || []); } };
            }
            case 'homesections': return homeSections(value);
            case 'order': return libraryOrder(value, sources);
            case 'libgrid': return libraryGrid(row, value, row.source === 'groupable' ? sources.groupable : sources.views, sources);
            default: return allOrSome(row, value, sources);
        }
    }

    // wrap
    // Makes a field handle from an element and its reader and writer.
    function wrap(element, get, set) {
        return { element: element, getValue: get, setValue: set };
    }

    // choices
    // Lists a select row's choices.
    function choices(row, sources) {
        if (row.type === 'enum') return row.options;
        if (row.type === 'culture') {
            return [['', t.tr('AnyLanguage')]].concat((sources.cultures || []).map(function (c) { return [c.ThreeLetterISOLanguageName, c.DisplayName]; }));
        }
        if (row.type === 'cast') {
            return (sources.casts.length ? sources.casts : [{ Id: 'F007D354', Name: 'Stable' }, { Id: '6F511C87', Name: 'Unstable' }]).map(function (c) { return [c.Id, c.Name]; });
        }
        return [['', t.tr('None')]].concat(ratingChoices(sources.ratings));
    }

    // allOrSome
    // Builds a field of an All checkbox over a list of libraries, channels, or devices, shown while All is off.
    function allOrSome(row, value, sources) {
        var list = row.type === 'channels' ? sources.channels : row.type === 'devices' ? sources.devices : sources.libraries;
        var idKey = row.type === 'devices' ? 'Id' : row.type === 'channels' ? 'Id' : 'ItemId';
        var box = h('input', { type: 'checkbox', is: 'emby-checkbox' });
        box.checked = !!value.all;
        var picks = nectoModule.createCheckboxList({
            items: (list || []).map(function (i) { return { Id: i[idKey] || i.Id, Name: i.Name || i.AppName || i.Id }; }),
            idKey: 'Id',
            value: value.ids || [],
            renderRow: function (i) { return { label: i.Name }; },
            emptyMessage: t.tr('GroupsTemplateNothingToList')
        });
        handles.push(picks);
        var indent = h('div', { class: 'stolon-indent' }, [picks.element]);
        var show = function () { indent.classList.toggle('stolon-hidden', box.checked); };
        box.addEventListener('change', show);
        show();
        return wrap(h('div', {}, [h('label', {}, [box, h('span', { text: row.label })]), indent]),
            function () { return { all: box.checked, ids: picks.getValue() }; },
            function (v) { box.checked = !!(v && v.all); picks.setValue((v && v.ids) || []); show(); });
    }

    // homeSections
    // Builds the ten home screen section selects.
    function homeSections(value) {
        var options = homeSectionOptions(t);
        var selects = [];
        var box = h('div', { class: 'necto-template-home' });
        for (var i = 0; i < HOME_SECTION_COUNT; i++) {
            var select = h('select', { is: 'emby-select', 'aria-label': t.format('LabelHomeScreenSectionValue', i + 1) });
            options.forEach(function (o) { select.appendChild(h('option', { value: o[0], text: o[1] })); });
            select.value = (value && value[i]) || 'none';
            selects.push(select);
            box.appendChild(h('div', { class: 'selectContainer' }, [h('label', { class: 'selectLabel', text: t.format('LabelHomeScreenSectionValue', i + 1) }), select]));
        }
        return wrap(box, function () { return selects.map(function (s) { return s.value; }); }, function (v) { selects.forEach(function (s, i) { s.value = (v && v[i]) || 'none'; }); });
    }

    // libraryOrder
    // Builds the home screen library order as an ordered list of the views members can see. A hidden library keeps
    // its place after the shown ones.
    function libraryOrder(value, sources) {
        var views = sources.views || [];
        var ordered = function (ids) {
            var first = (ids || []).map(function (id) { return views.find(function (v) { return sameId(v.Id, id); }); }).filter(Boolean);
            return first.concat(views.filter(function (v) { return first.indexOf(v) < 0; }));
        };
        var full = ordered(value).map(function (v) { return v.Id; });
        var shown = function (ids) { return shownViews(ordered(ids), sources); };
        var list = nectoModule.createOrderedList({
            items: shown(full),
            render: function (v) { return h('span', { text: v.Name }); },
            label: function (v) { return v.Name; }
        });
        handles.push(list);
        var read = function () { return mergeOrder(full, list.getItems().map(function (v) { return v.Id; })); };
        viewRefreshers.push(function () { full = read(); list.setItems(shown(full)); });
        return wrap(list.element, read, function (v) { full = ordered(v).map(function (x) { return x.Id; }); list.setItems(shown(full)); });
    }

    // libraryGrid
    // Builds a checkbox list of the views members can see. Inverted rows list what shows, and save what is left out.
    // A hidden library keeps what it had.
    function libraryGrid(row, value, views, sources) {
        var all = (views || []).map(function (v) { return { Id: v.Id, Name: v.Name }; });
        var allIds = all.map(function (v) { return v.Id; });
        var saved = (value || []).slice();
        var shown = function () { return shownViews(all, sources).map(function (v) { return v.Id; }); };
        var lastShown = shown();
        var toChecked = function (ids) {
            ids = ids || [];
            return row.invert ? all.filter(function (v) { return !ids.some(function (id) { return sameId(id, v.Id); }); }).map(function (v) { return v.Id; }) : ids;
        };
        var grid = nectoModule.createCheckboxList({ fetchItems: function () { return shownViews(all, sources); }, idKey: 'Id', value: toChecked(saved), renderRow: function (i) { return { label: i.Name }; }, emptyMessage: t.tr('GroupsTemplateNothingToList') });
        handles.push(grid);
        var read = function () {
            return mergeGrid(row.invert, allIds, lastShown, grid.getValue(), saved);
        };
        viewRefreshers.push(function () {
            saved = read();
            lastShown = shown();
            grid.refresh().then(function () { grid.setValue(toChecked(saved)); });
        });
        return wrap(grid.element, read, function (v) { saved = (v || []).slice(); grid.setValue(toChecked(saved)); });
    }
}
