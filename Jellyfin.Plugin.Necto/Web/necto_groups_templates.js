// necto_groups_templates.js
// The template sections of the group editor. A group's template writes Jellyfin's own Access and features and
// Preferences onto its members, and each setting is either left to the member or set by the group.

import * as nectoModule from './configurationpage?name=necto_shared.js';

var el = nectoModule.el;

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
            ] },
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
        case 'rating': value = part.MaxParentalRating == null ? '' : String(part.MaxParentalRating); break;
        case 'bitrate': value = part.RemoteClientBitrateLimit > 0 ? part.RemoteClientBitrateLimit / 1000000 : 0; break;
        case 'homesections': value = (part.HomeSections && part.HomeSections.length ? part.HomeSections : DEFAULT_HOME_SECTIONS).slice(); break;
        case 'bool': value = part[row.key] === true || (part[row.key] == null && row.fallback === true); break;
        case 'int': value = part[row.key] || 0; break;
        default: value = part[row.key] == null ? (Array.isArray(part[row.key]) ? [] : part[row.key]) : part[row.key];
    }
    return { manage: manage, value: value };
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
        case 'rating': part.MaxParentalRating = v === '' || v == null ? null : parseInt(v, 10); break;
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
// Adds the template's sections to a group editor: whether the group sets Jellyfin settings, then Access and features
// and Preferences, each row left to the member or set by the group.
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
    var on = el('input', { type: 'checkbox', is: 'emby-checkbox', id: onId, 'data-field': 'Template.Enabled' });
    on.checked = template.Enabled === true;
    var accessBody = el('div', { 'data-role': 'template-access' });
    var prefsBody = el('div', { 'data-role': 'template-preferences' });
    var intro = el('div', {}, [
        el('div', { class: 'fieldDescription', text: t.tr('GroupsTemplateHelp') }),
        el('div', { class: 'checkboxContainer checkboxContainer-withDescription' }, [
            el('label', {}, [on, el('span', { text: t.tr('GroupsTemplateEnable') })]),
            el('div', { class: 'fieldDescription checkboxFieldDescription', text: t.tr('GroupsTemplateEnableHelp') })
        ]),
        accessBody
    ]);
    section(t.tr('GroupsTemplateAccessTitle'), 'admin_panel_settings', intro).element.setAttribute('data-section', 'template-access');
    section(t.tr('GroupsTemplatePreferencesTitle'), 'tune', prefsBody).element.setAttribute('data-section', 'template-preferences');
    readers.push(function (out) { out.Template = out.Template || {}; out.Template.Enabled = !!on.checked; });

    var reflect = function () {
        accessBody.classList.toggle('stolon-hidden', !on.checked);
        prefsBody.classList.toggle('stolon-hidden', !on.checked);
    };
    on.addEventListener('change', reflect);
    reflect();

    return loadSources(shared).then(function (sources) {
        drawPart(accessBody, accessSpecs(t), 'Access', template.Access, sources);
        drawPart(prefsBody, preferenceSpecs(t), 'Preferences', template.Preferences, sources);
    });

    // drawPart
    // Draws one part's sections and rows, and reads them back into the group.
    function drawPart(into, specs, partName, part, sources) {
        specs.forEach(function (spec) {
            into.appendChild(el('h3', { class: 'stolon-subsection-title', text: spec.title }));
            spec.rows.forEach(function (row) {
                var start = rowValue(part, row);
                var field = buildField(row, start.value, sources);
                var inherit = nectoModule.createInheritField({
                    field: field,
                    value: { inherit: !start.manage, value: start.value },
                    inheritText: t.tr('GroupsTemplateMember'),
                    overrideText: t.tr('LabelGroup'),
                    label: row.label
                });
                handles.push(inherit);
                into.appendChild(el('div', { class: 'necto-template-row', 'data-field': partName + '.' + row.key }, [
                    el('div', { class: 'selectLabel', text: row.label }),
                    inherit.element,
                    row.help ? el('div', { class: 'fieldDescription', text: row.help }) : null
                ]));
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
                var box = el('input', { type: 'checkbox', is: 'emby-checkbox', id: id });
                box.checked = !!value;
                return wrap(el('label', {}, [box, el('span', { text: t.tr('GroupsTemplateOn') })]), function () { return box.checked; }, function (v) { box.checked = !!v; });
            }
            case 'int':
            case 'bitrate': {
                var num = el('input', { is: 'emby-input', type: 'number', id: id, min: String(row.min == null ? 0 : row.min), step: row.type === 'bitrate' ? '0.1' : '1', 'aria-label': row.label });
                num.value = String(value || 0);
                return wrap(el('div', { class: 'inputContainer' }, [num]), function () { return num.value; }, function (v) { num.value = String(v || 0); });
            }
            case 'enum':
            case 'culture':
            case 'cast':
            case 'rating': {
                var select = el('select', { is: 'emby-select', id: id, 'aria-label': row.label });
                choices(row, sources).forEach(function (o) { select.appendChild(el('option', { value: o[0], text: o[1] })); });
                select.value = value == null ? '' : String(value);
                return wrap(el('div', { class: 'selectContainer' }, [select]), function () { return select.value; }, function (v) { select.value = v == null ? '' : String(v); });
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
            case 'order': return libraryOrder(value, sources.views);
            case 'libgrid': return libraryGrid(row, value, row.source === 'groupable' ? sources.groupable : sources.views);
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
        var seen = {};
        return [['', t.tr('None')]].concat((sources.ratings || []).filter(function (r) {
            var score = r.RatingScore ? r.RatingScore.Score : r.Value;
            if (score == null || seen[score]) return false;
            seen[score] = true;
            return true;
        }).map(function (r) { return [String(r.RatingScore ? r.RatingScore.Score : r.Value), r.Name]; }));
    }

    // allOrSome
    // Builds a field of an All checkbox over a list of libraries, channels, or devices, shown while All is off.
    function allOrSome(row, value, sources) {
        var list = row.type === 'channels' ? sources.channels : row.type === 'devices' ? sources.devices : sources.libraries;
        var idKey = row.type === 'devices' ? 'Id' : row.type === 'channels' ? 'Id' : 'ItemId';
        var box = el('input', { type: 'checkbox', is: 'emby-checkbox' });
        box.checked = !!value.all;
        var picks = nectoModule.createCheckboxList({
            items: (list || []).map(function (i) { return { Id: i[idKey] || i.Id, Name: i.Name || i.AppName || i.Id }; }),
            idKey: 'Id',
            value: value.ids || [],
            renderRow: function (i) { return { label: i.Name }; },
            emptyMessage: t.tr('GroupsTemplateNothingToList')
        });
        handles.push(picks);
        var indent = el('div', { class: 'stolon-indent' }, [picks.element]);
        var show = function () { indent.classList.toggle('stolon-hidden', box.checked); };
        box.addEventListener('change', show);
        show();
        return wrap(el('div', {}, [el('label', {}, [box, el('span', { text: row.label })]), indent]),
            function () { return { all: box.checked, ids: picks.getValue() }; },
            function (v) { box.checked = !!(v && v.all); picks.setValue((v && v.ids) || []); show(); });
    }

    // homeSections
    // Builds the ten home screen section selects.
    function homeSections(value) {
        var options = homeSectionOptions(t);
        var selects = [];
        var box = el('div', { class: 'necto-template-home' });
        for (var i = 0; i < HOME_SECTION_COUNT; i++) {
            var select = el('select', { is: 'emby-select', 'aria-label': t.format('LabelHomeScreenSectionValue', i + 1) });
            options.forEach(function (o) { select.appendChild(el('option', { value: o[0], text: o[1] })); });
            select.value = (value && value[i]) || 'none';
            selects.push(select);
            box.appendChild(el('div', { class: 'selectContainer' }, [el('label', { class: 'selectLabel', text: t.format('LabelHomeScreenSectionValue', i + 1) }), select]));
        }
        return wrap(box, function () { return selects.map(function (s) { return s.value; }); }, function (v) { selects.forEach(function (s, i) { s.value = (v && v[i]) || 'none'; }); });
    }

    // libraryOrder
    // Builds the home screen library order as an ordered list of the views.
    function libraryOrder(value, views) {
        var ordered = function (ids) {
            var known = (views || []).slice();
            var first = (ids || []).map(function (id) { return known.find(function (v) { return sameId(v.Id, id); }); }).filter(Boolean);
            return first.concat(known.filter(function (v) { return first.indexOf(v) < 0; }));
        };
        var list = nectoModule.createOrderedList({
            items: ordered(value),
            render: function (v) { return el('span', { text: v.Name }); },
            label: function (v) { return v.Name; }
        });
        handles.push(list);
        return wrap(list.element, function () { return list.getItems().map(function (v) { return v.Id; }); }, function (v) { list.setItems(ordered(v)); });
    }

    // libraryGrid
    // Builds a checkbox list of views. Inverted rows list what shows, and save what is left out.
    function libraryGrid(row, value, views) {
        var all = (views || []).map(function (v) { return { Id: v.Id, Name: v.Name }; });
        var toChecked = function (ids) {
            ids = ids || [];
            return row.invert ? all.filter(function (v) { return !ids.some(function (id) { return sameId(id, v.Id); }); }).map(function (v) { return v.Id; }) : ids;
        };
        var grid = nectoModule.createCheckboxList({ items: all, idKey: 'Id', value: toChecked(value), renderRow: function (i) { return { label: i.Name }; }, emptyMessage: t.tr('GroupsTemplateNothingToList') });
        handles.push(grid);
        return wrap(grid.element, function () {
            var checked = grid.getValue();
            return row.invert ? all.filter(function (v) { return checked.indexOf(v.Id) < 0; }).map(function (v) { return v.Id; }) : checked;
        }, function (v) { grid.setValue(toChecked(v)); });
    }
}
