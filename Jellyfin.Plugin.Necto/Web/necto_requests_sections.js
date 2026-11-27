// necto_requests_sections.js
// The Requests and Issues sections of Settings and Troubleshooting, which this module adds to the registry when it loads.

import * as nectoModule from './configurationpage?name=necto_shared.js';

var el = nectoModule.el;

// The keys of the scheduled tasks Troubleshooting runs.
export var SWEEP_TASK = 'RequestsSweep';
export var SYNC_TASK = 'RequestsSyncProgress';
export var DISCOVERY_TASK = 'RequestsRefreshDiscovery';
export var CLEANUP_TASK = 'RequestsCleanup';

// The names of each choice the server numbers, in its order.
var TYPES = ['Movie', 'Series'];
var REFRESH_MODES = ['ItemFolder', 'FullLibrary', 'Disabled'];
var AVAILABILITY = ['Announced', 'InCinemas', 'Released'];
var SERIES_TYPES = ['Standard', 'Daily', 'Anime'];

// Each number setting's default, used when its field is blank or below its least value.
export var NUMBER_DEFAULTS = {
    AgingAfterDays: 0, AutoDeclineAfterDays: 0, RetentionDays: 90, MovieQuotaDays: 7, SeriesQuotaDays: 7,
    IssueRetentionDays: 90, IssueLimitDays: 1
};

// What each type of server is called, and where it usually listens.
var ARR = {
    Movie: { name: 'Radarr', example: 'http://localhost:7878' },
    Series: { name: 'Sonarr', example: 'http://localhost:8989' }
};

// The languages the blocklist offers, the most common original languages.
var LANGUAGES = ['en', 'es', 'fr', 'de', 'it', 'pt', 'ja', 'ko', 'zh', 'hi', 'ru', 'sv', 'da', 'no', 'nl', 'pl', 'tr', 'th', 'ar', 'he', 'id', 'fi', 'cs', 'el', 'hu', 'uk', 'vi', 'ta', 'te', 'tl'];

// The request page's own rows, by the key the order and hiding know them by, with the key of the name it shows.
export var BUILT_IN_ROWS = [['RecentlyAdded', 'TabLatest'], ['RecommendedForYou', 'RequestsRecommendedForYou'], ['TrendingShows', 'RequestsTrendingShows'],
    ['TrendingMovies', 'RequestsTrendingMovies'], ['UpcomingShows', 'RequestsUpcomingShows'], ['UpcomingMovies', 'RequestsUpcomingMovies'],
    ['PopularShows', 'RequestsPopularShows'], ['PopularMovies', 'RequestsPopularMovies']];

// Ids for fields, so two sections never share one.
var fieldSeq = 0;

// fieldId
// Makes an id for a field in a section.
//
// Inputs
// name [string]: What the field is.
//
// Output
// [string]: An id no other field has.
function fieldId(name) {
    fieldSeq += 1;
    return 'necto-requests-' + name + '-' + fieldSeq;
}

// enumName
// Reads a choice the server may send as its number or its name.
//
// Inputs
// value [number|string]: The stored choice.
// names [string[]]: The choice names in the server's order.
// fallback [string]: The name for an unknown choice. [optional]
//
// Output
// [string]: The choice's name.
export function enumName(value, names, fallback) {
    if (typeof value === 'number' && names[value]) return names[value];
    return names.indexOf(value) >= 0 ? value : (fallback || names[0]);
}

// isWebAddress
// Tells whether an address is http or https, as the server takes them.
//
// Inputs
// url [string]: The address.
//
// Output
// [boolean]: Whether it is a web address.
export function isWebAddress(url) {
    try { return /^https?:$/.test(new URL(String(url || '').trim()).protocol); } catch (e) { return false; }
}

// sameAddress
// Compares two addresses whatever their case and trailing slashes.
//
// Inputs
// a [string]: One address.
// b [string]: The other.
//
// Output
// [boolean]: Whether they are the same.
export function sameAddress(a, b) {
    var tidy = function (u) { return String(u || '').trim().replace(/\/+$/, '').toLowerCase(); };
    return tidy(a) === tidy(b);
}

// hostOf
// Gives the host and port of an address, which tells servers apart, or the whole address when it does not parse.
//
// Inputs
// url [string]: The address.
//
// Output
// [string]: The host.
export function hostOf(url) {
    try { return new URL(url).host; } catch (e) { return url || ''; }
}

// intOf
// Reads a number field, falling back to the default when it is blank, not a number, or below the least value.
//
// Inputs
// input [HTMLInputElement]: The field.
// fallback [number]: The default.
// min [number]: The least value. Defaults to 0.
//
// Output
// [number]: The number.
export function intOf(input, fallback, min) {
    var n = parseInt(input.value, 10);
    return isNaN(n) || n < (min || 0) ? fallback : n;
}

// serverName
// Names a Radarr or Sonarr server, by its own name or its type's.
//
// Inputs
// server [object]: The server.
//
// Output
// [string]: The name.
export function serverName(server) {
    return String(server.Name || '').trim() || ARR[enumName(server.Type, TYPES)].name;
}

// isDefault
// Tells whether a server is its type's default: the one marked, or with none marked the first of its type.
//
// Inputs
// list [object[]]: The servers.
// server [object]: The server, which may not be in the list yet.
//
// Output
// [boolean]: Whether it is the default.
export function isDefault(list, server) {
    var type = enumName(server.Type, TYPES);
    var same = list.filter(function (s) { return enumName(s.Type, TYPES) === type; });
    var marked = same.filter(function (s) { return s.IsDefault; })[0];
    return marked ? marked === server : (same[0] || server) === server;
}

// arrServersError
// Checks the Radarr and Sonarr servers as the server does before a save, so a server that looks set up but sends
// nothing is refused.
//
// Inputs
// servers [object[]]: The servers as edited.
// stored [object[]]: The servers as stored, to tell whether a kept key still has its address.
// t [object]: The strings.
//
// Output
// [string|null]: The first problem, or null when every server is complete.
export function arrServersError(servers, stored, t) {
    var list = (servers || []).filter(Boolean);
    for (var i = 0; i < list.length; i++) {
        var server = list[i];
        var name = serverName(server);
        if (!isWebAddress(server.Url)) return t.format('RequestsErrorArrAddressScheme', name);
        if (String(server.ExternalUrl || '').trim() && !isWebAddress(server.ExternalUrl)) return t.format('RequestsErrorArrExternalScheme', name);
        if (!String(server.ApiKey || '').trim()) return t.format('RequestsErrorArrEnterKey', name);
        // A kept key stays with the address it was saved for, so a moved server needs its key again.
        if (server.ApiKey === nectoModule.SECRET_KEPT && !(stored || []).some(function (s) { return s && s.Id === server.Id && sameAddress(s.Url, server.Url); })) {
            return t.format('RequestsErrorArrEnterKeyAgain', name);
        }
        if (!(server.Profiles || []).length) return t.format('RequestsErrorArrAddProfile', name);
        var incomplete = server.Profiles.filter(function (p) { return !p || !(p.QualityProfileId > 0) || !String(p.RootFolder || '').trim(); })[0];
        if (incomplete !== undefined) {
            return t.format('RequestsErrorArrChooseProfileFolder', incomplete && String(incomplete.Name || '').trim() ? incomplete.Name : t.tr('RequestsValueEachProfile'), name);
        }
    }
    return null;
}

// keptServers
// Copies the stored servers for the editor, each stored key turned into the kept sentinel so it is never sent back.
//
// Inputs
// servers [object[]]: The servers as stored.
//
// Output
// [object[]]: The copies.
export function keptServers(servers) {
    return JSON.parse(JSON.stringify(servers || [])).map(function (s) {
        s.Type = enumName(s.Type, TYPES);
        s.ApiKey = s.ApiKey ? nectoModule.SECRET_KEPT : '';
        s.Profiles = (s.Profiles || []).map(function (p) {
            p.MinimumAvailability = enumName(p.MinimumAvailability, AVAILABILITY, 'Released');
            p.SeriesType = enumName(p.SeriesType, SERIES_TYPES);
            return p;
        });
        return s;
    });
}

// customKey
// Names one of the server's own home rows as the order and hiding know it.
//
// Inputs
// row [object]: The row.
//
// Output
// [string]: The key.
function customKey(row) {
    return 'Custom-' + row.Id;
}

// rowKeys
// Lists every home row's key in the order they show: those the order names, then the rest as the request page has them.
//
// Inputs
// rows [object[]]: The server's own rows.
// order [string[]]: The keys in the order saved.
//
// Output
// [string[]]: The keys.
export function rowKeys(rows, order) {
    var all = BUILT_IN_ROWS.map(function (b) { return b[0]; }).concat((rows || []).map(customKey));
    return (order || []).filter(function (k) { return all.indexOf(k) >= 0; }).concat(all.filter(function (k) { return (order || []).indexOf(k) < 0; }));
}

// newId
// Makes an id for a server, a profile, or a row, letters and digits only, as the server keeps them.
//
// Inputs
// shared [object]: The Necto helper bag, for its GUID maker.
//
// Output
// [string]: The id.
function newId(shared) {
    return shared.generateGuid().replace(/-/g, '');
}

// languageName
// Names a language code in the viewer's language.
//
// Inputs
// code [string]: The code, such as ja.
// t [object]: The strings.
//
// Output
// [string]: The name, or the code where the browser cannot name it.
function languageName(code, t) {
    try { return new Intl.DisplayNames([t.language || 'en', 'en'], { type: 'language' }).of(code) || code; } catch (e) { return code; }
}

// sentence
// Reads a string in sentence case where the strings offer it.
//
// Inputs
// t [object]: The strings.
// key [string]: The string's key.
//
// Output
// [string]: The text.
function sentence(t, key) {
    return typeof t.sentence === 'function' ? t.sentence(key) : t.tr(key);
}

// field
// Builds a labelled field with its description.
//
// Inputs
// id [string]: The control's id.
// label [string]: The label.
// control [HTMLElement]: The input, select, or mount.
// help [string]: The description, or empty. [optional]
// select [boolean]: Whether it is a select. [optional]
//
// Output
// [HTMLElement]: The field.
function field(id, label, control, help, select) {
    return el('div', { class: select ? 'selectContainer' : 'inputContainer' }, [
        el('label', { class: select ? 'selectLabel' : 'inputLabel inputLabelUnfocused', for: id, text: label }),
        control,
        help ? el('div', { class: 'fieldDescription', text: help }) : null
    ]);
}

// checkField
// Builds a checkbox with its label and description.
//
// Inputs
// input [HTMLInputElement]: The checkbox.
// label [string]: The label.
// help [string]: The description, or empty. [optional]
//
// Output
// [HTMLElement]: The field.
function checkField(input, label, help) {
    return el('div', { class: 'checkboxContainer' + (help ? ' checkboxContainer-withDescription' : '') }, [
        el('label', {}, [input, el('span', { text: label })]),
        help ? el('div', { class: 'fieldDescription checkboxFieldDescription', text: help }) : null
    ]);
}

// numberInput
// Builds a number input for a setting.
//
// Inputs
// id [string]: The input's id.
// path [string]: The setting it holds, as its data-field.
// min [number]: The least value. Defaults to 0.
//
// Output
// [HTMLInputElement]: The input.
function numberInput(id, path, min) {
    return el('input', { is: 'emby-input', type: 'number', id: id, 'data-field': path, min: String(min || 0), step: '1' });
}

// checkbox
// Builds a checkbox for a setting.
//
// Inputs
// id [string]: The checkbox's id.
// path [string]: The setting it holds, as its data-field.
//
// Output
// [HTMLInputElement]: The checkbox.
function checkbox(id, path) {
    return el('input', { type: 'checkbox', is: 'emby-checkbox', id: id, 'data-field': path });
}

// linkSentence
// Builds a sentence that holds a link where its {0} placeholder is.
//
// Inputs
// text [string]: The sentence with {0}.
// url [string]: The address the link opens.
// className [string]: The sentence's class.
//
// Output
// [HTMLElement]: The sentence.
function linkSentence(text, url, className) {
    var parts = String(text).split('{0}');
    return el('div', { class: className }, [
        el('span', { text: parts[0] || '' }),
        el('span', { class: 'stolon-url' }, [el('a', { href: url, target: '_blank', rel: 'noopener noreferrer', text: url })]),
        el('span', { text: parts.slice(1).join('{0}') })
    ]);
}

// pageUrl
// Gives the address of one of the user pages on this server, such as /requests/.
//
// Inputs
// path [string]: The page's path, such as requests.
//
// Output
// [string]: The address.
export function pageUrl(path) {
    var origin = ((globalThis.ApiClient && ApiClient.serverAddress) ? ApiClient.serverAddress() : (globalThis.location && globalThis.location.origin) || '');
    return String(origin || '').replace(/\/+$/, '') + '/' + path + '/';
}

// buildRequestSettings
// Fills the Requests section of Settings: rules, quota windows, the blocklist, Radarr and Sonarr, discovery, and imports.
//
// Inputs
// content [HTMLElement]: The section's content.
// ctx [object]: shared is the Necto helper bag, and page the page handle.
//
// Output
// [object]: A handle with load(config), apply(config), and destroy.
function buildRequestSettings(content, ctx) {
    var shared = ctx.shared;
    var page = ctx.page;
    var t = shared.t;
    var chips = {};
    var inputs = {};
    var servers = [];
    var rows = [];
    var order = [];
    var hidden = [];
    var blocked = [];
    var serverList = null;
    var blockedList = null;
    var rowCards = null;
    var handles = [];
    var suggestions = null;

    // changed
    // Tells the Settings page the section has changes, for edits made outside its fields, such as in a sidecar.
    function changed() {
        content.dispatchEvent(new Event('change', { bubbles: true }));
    }

    // sub
    // Adds a subsection title to the section.
    function sub(key) {
        content.appendChild(el('h3', { class: 'stolon-subsection-title', text: t.tr(key) }));
    }

    // numberField
    // Adds a number setting to the section, with its description by key or as text already in the viewer's language.
    function numberField(path, label, help, min, helpText) {
        var id = fieldId(path);
        inputs[path] = numberInput(id, path, min);
        content.appendChild(field(id, t.tr(label), inputs[path], helpText || (help ? t.tr(help) : '')));
    }

    // checkSetting
    // Adds an on or off setting to the section.
    function checkSetting(path, label, help) {
        inputs[path] = checkbox(fieldId(path), path);
        content.appendChild(checkField(inputs[path], t.tr(label), help ? t.tr(help) : ''));
    }

    // chipField
    // Adds a list setting as a chip select, which takes a typed value on Enter or picks from what the server offers.
    function chipField(path, label, help, opts) {
        var made = nectoModule.createChipSelect(Object.assign({ onChange: changed }, opts));
        var input = made.element.querySelector('input');
        if (input) input.setAttribute('aria-label', t.tr(label));
        chips[path] = made;
        handles.push(made);
        content.appendChild(el('div', { class: 'inputContainer', 'data-field': path }, [
            el('div', { class: 'inputLabel', text: t.tr(label) }),
            made.element,
            help ? el('div', { class: 'fieldDescription', text: t.tr(help) }) : null
        ]));
        return made;
    }

    // tmdbChoices
    // Reads what TMDb offers for a kind of pick, such as keywords by name or a type's streaming services.
    function tmdbChoices(kind, type, query) {
        var route = kind === 'Provider'
            ? 'Requests/Providers?type=' + encodeURIComponent(type)
            : 'Requests/HomeRows/Choices?' + new URLSearchParams({ kind: kind, type: type, query: query || '' }).toString();
        return shared.request(route).then(function (list) {
            return (Array.isArray(list) ? list : []).map(function (c) { return { value: String(c.Id), label: c.Name }; });
        }, function () { return []; });
    }

    // tmdbSearch
    // Builds a search for a chip select that asks TMDb once typing pauses, and drops an answer to an earlier question.
    function tmdbSearch(kind) {
        var asked = 0;
        var later = shared.debounce(function (mine, q, cb) {
            tmdbChoices(kind, 'Movie', q).then(function (list) { if (mine === asked) cb(list); });
        }, 300);
        return function (q, cb) {
            var mine = ++asked;
            if ((q || '').trim().length < 2) { cb([]); return; }
            later(mine, q, cb);
        };
    }

    // loadSuggestions
    // Reads the server's age ratings and the genres Jellyfin and TMDb know, once, for the chip selects to offer.
    function loadSuggestions() {
        if (suggestions) return suggestions;
        var names = function (list) {
            return (list || []).map(function (x) { return x && (x.Name || (typeof x === 'string' ? x : '')); }).filter(Boolean);
        };
        var api = globalThis.ApiClient || {};
        suggestions = Promise.all([
            api.getParentalRatings ? api.getParentalRatings().then(names, function () { return []; }) : Promise.resolve([]),
            api.getJSON ? api.getJSON(api.getUrl('Genres', { SortBy: 'SortName' })).then(function (r) { return names(r && r.Items); }, function () { return []; }) : Promise.resolve([]),
            shared.request('Requests/Genres').then(function (list) { return Array.isArray(list) ? names(list) : []; }, function () { return []; })
        ]).then(function (all) {
            var unique = function (list) { return list.filter(function (n, i) { return list.indexOf(n) === i; }).sort(function (a, b) { return a.localeCompare(b); }); };
            return { ratings: unique(all[0]), genres: unique(all[1].concat(all[2])) };
        });
        return suggestions;
    }

    // The switch, first, with where users request.
    inputs.Enabled = checkbox(fieldId('requests-enabled'), 'Requests.Enabled');
    content.appendChild(el('div', { class: 'checkboxContainer checkboxContainer-withDescription' }, [
        el('label', {}, [inputs.Enabled, el('span', { text: t.tr('LabelEnabled') })]),
        linkSentence(t.tr('RequestsHelpEnabled'), pageUrl('requests'), 'fieldDescription checkboxFieldDescription')
    ]));

    // Rules
    sub('RequestsHeaderRules');
    chipField('AutoApproveRatings', 'RequestsLabelAutoApproveRated', 'RequestsHelpAutoApproveRated', { allowCreate: true, placeholder: t.tr('RequestsPlaceholderTypeToAdd') });
    numberField('AgingAfterDays', 'RequestsLabelRemindAfterDays', null, 0, t.format('RequestsHelpAging', t.tr('RequestsEventAging')));
    numberField('AutoDeclineAfterDays', 'RequestsLabelRejectAfterDays', 'RequestsHelpRejectAfter', 0);
    var reasonId = fieldId('reason');
    inputs.AutoDeclineReason = el('input', { is: 'emby-input', type: 'text', id: reasonId, 'data-field': 'AutoDeclineReason', maxlength: '500' });
    content.appendChild(field(reasonId, t.tr('RequestsLabelAutoRejectReason'), inputs.AutoDeclineReason, t.tr('RequestsHelpAutoRejectReason')));
    checkSetting('FullSeriesOnly', 'RequestsLabelFullSeriesOnly', 'RequestsHelpFullSeriesOnly');
    checkSetting('AllowSpecials', 'RequestsLabelAllowSpecials', 'RequestsHelpAllowSpecials');

    // Quota windows
    sub('RequestsHeaderQuotaWindows');
    content.appendChild(el('div', { class: 'fieldDescription', text: t.tr('RequestsHelpQuotaWindows') }));
    numberField('MovieQuotaDays', 'RequestsLabelMovieQuotaDays', 'RequestsHelpMovieQuotaDays', 1);
    numberField('SeriesQuotaDays', 'RequestsLabelSeriesQuotaDays', 'RequestsHelpSeriesQuotaDays', 1);

    // Blocklist
    sub('RequestsHeaderBlocklist');
    content.appendChild(el('div', { class: 'fieldDescription', text: t.tr('RequestsHelpBlocklist') }));
    chipField('BlockedGenres', 'RequestsLabelBlockedGenres', null, { allowCreate: true, placeholder: t.tr('RequestsPlaceholderTypeToAdd') });
    chipField('BlockedRatings', 'RequestsLabelBlockedRatings', null, { allowCreate: true, placeholder: t.tr('RequestsPlaceholderTypeToAdd') });
    chipField('BlockedKeywords', 'RequestsLabelBlockedWords', 'RequestsHelpBlockedWords', { allowCreate: true, placeholder: t.tr('RequestsPlaceholderTypeToAdd') });
    chipField('BlockedTags', 'RequestsLabelBlockedTmdbKeywords', 'RequestsHelpBlockedKeywords', { placeholder: t.tr('RequestsPlaceholderTypeToFind'), searchFn: tmdbSearch('Keyword') });
    chipField('BlockedLanguages', 'RequestsLabelBlockedLanguages', 'RequestsHelpBlockedLanguages', {
        placeholder: t.tr('RequestsPlaceholderChooseLanguages'),
        options: LANGUAGES.map(function (c) { return { value: c, label: languageName(c, t) }; })
    });
    chipField('BlockedCollections', 'RequestsLabelBlockedCollections', 'RequestsHelpBlockedCollections', { placeholder: t.tr('RequestsPlaceholderTypeToFind'), searchFn: tmdbSearch('Collection') });
    content.appendChild(el('h3', { class: 'stolon-subsection-title', text: t.tr('RequestsHeaderBlockedTitles') }));
    content.appendChild(el('div', { class: 'fieldDescription', text: t.tr('RequestsHelpBlockedTitles') }));
    var blockedMount = el('div', { 'data-role': 'blocked-titles' });
    var blockedStatus = el('div', { class: 'stolon-status-text', role: 'status', 'data-role': 'blocked-status' });
    content.appendChild(blockedMount);
    content.appendChild(blockedStatus);

    // Radarr and Sonarr
    sub('RequestsLabelRadarrAndSonarr');
    content.appendChild(el('div', { class: 'fieldDescription', text: t.tr('RequestsHelpServers') }));
    var serverMount = el('div', { 'data-role': 'servers' });
    content.appendChild(serverMount);
    content.appendChild(el('div', { class: 'stolon-card-list-actions' }, ['Movie', 'Series'].map(function (type) {
        return el('button', { is: 'emby-button', type: 'button', class: 'raised stolon-button stolon-secondary stolon-small', 'data-add-server': type }, [
            el('span', { class: 'material-icons', 'aria-hidden': 'true', text: 'add' }),
            el('span', { text: t.tr(type === 'Movie' ? 'RequestsButtonAddRadarr' : 'RequestsButtonAddSonarr') })
        ]);
    })));

    // Discovery
    sub('RequestsHeaderDiscovery');
    content.appendChild(el('div', { class: 'fieldDescription' }, [el('img', { src: '../requests/tmdb.svg', alt: 'TMDB', width: '60' })]));
    content.appendChild(el('div', { class: 'fieldDescription', text: t.tr('RequestsTmdbNotice') }));
    var keyId = fieldId('tmdb-key');
    inputs.TmdbApiKey = el('input', { is: 'emby-input', type: 'password', id: keyId, 'data-field': 'TmdbApiKey', autocomplete: 'off' });
    var clearKey = el('button', { is: 'emby-button', type: 'button', class: 'raised stolon-button stolon-secondary stolon-small stolon-hidden', 'data-action': 'clear-tmdb-key' }, [el('span', { text: t.tr('RequestsButtonClearSavedKey') })]);
    content.appendChild(el('div', { class: 'inputContainer' }, [
        el('label', { class: 'inputLabel inputLabelUnfocused', for: keyId, text: t.tr('RequestsLabelTmdbApiKey') }),
        inputs.TmdbApiKey,
        el('div', { class: 'fieldDescription', text: t.tr('RequestsHelpTmdbKey') }),
        clearKey
    ]));
    checkSetting('HideAvailableInRows', 'RequestsLabelHideAvailable', 'RequestsHelpHideAvailable');
    content.appendChild(el('h3', { class: 'stolon-subsection-title', text: t.tr('RequestsHeaderHomeRows') }));
    content.appendChild(el('div', { class: 'fieldDescription', text: t.tr('RequestsHelpHomeRows') }));
    var rowMount = el('div', { class: 'stolon-card-list', 'data-role': 'home-rows' });
    var rowEditorEl = el('div', { 'data-role': 'home-row-editor' });
    content.appendChild(rowMount);
    content.appendChild(el('div', { class: 'stolon-hidden' }, [rowEditorEl]));
    content.appendChild(el('div', { class: 'stolon-card-list-actions' }, [
        el('button', { is: 'emby-button', type: 'button', class: 'raised stolon-button stolon-secondary stolon-small', 'data-action': 'add-row' }, [
            el('span', { class: 'material-icons', 'aria-hidden': 'true', text: 'add' }),
            el('span', { text: t.tr('RequestsButtonAddRow') })
        ])
    ]));

    // Imports
    sub('RequestsHeaderImports');
    var scanId = fieldId('scan');
    inputs.RefreshOnAvailable = el('select', { is: 'emby-select', id: scanId, 'data-field': 'RefreshOnAvailable' });
    [['ItemFolder', 'RequestsOptionScanTitleFolder'], ['FullLibrary', 'AllLibraries'], ['Disabled', 'RequestsOptionScanNone']].forEach(function (o) {
        inputs.RefreshOnAvailable.appendChild(el('option', { value: o[0], text: t.tr(o[1]) }));
    });
    content.appendChild(field(scanId, t.tr('RequestsLabelScanAfterDownload'), inputs.RefreshOnAvailable, t.tr('RequestsHelpScan'), true));
    numberField('RetentionDays', 'RequestsLabelKeepFinishedDays', 'RequestsHelpKeepFinished', 0);

    // renderBlocked
    // Draws a row for each blocked title, with its type, the ids it is blocked by, and Unblock.
    function renderBlocked() {
        var items = blocked.map(function (b, i) {
            var ids = [['TMDb', b.Tmdb], ['TVDB', b.Tvdb], ['IMDb', b.Imdb]].filter(function (x) { return x[1]; }).map(function (x) { return x[0] + ' ' + x[1]; });
            return {
                id: String(i) + ':' + b.Title,
                title: b.Title + (b.Year ? ' (' + b.Year + ')' : ''),
                secondary: [enumName(b.Type, TYPES) === 'Series' ? t.tr('LabelShow') : t.tr('Movie')].concat(ids).join(' · '),
                aside: el('button', { is: 'emby-button', type: 'button', class: 'raised stolon-button stolon-secondary stolon-small', 'data-unblock': String(i), 'aria-label': t.format('RequestsLabelUnblockTitle', b.Title) }, [el('span', { text: t.tr('ButtonUnblock') })])
            };
        });
        if (blockedList) blockedList.destroy();
        blockedMount.textContent = '';
        blockedList = nectoModule.createRecordList({ items: items, label: t.tr('RequestsHeaderBlockedTitles'), emptyText: t.tr('RequestsEmptyBlockedTitles'), offText: t.tr('Off') });
        blockedMount.appendChild(blockedList.element);
    }

    // unblock
    // Allows a blocked title again at once, as blocking is done from the request page, without waiting for Save.
    function unblock(index, btn) {
        var b = blocked[index];
        if (!b) return Promise.resolve();
        btn.disabled = true;
        return shared.request('Requests/Blocklist/Unblock', 'POST', { Type: enumName(b.Type, TYPES), Title: b.Title, Year: b.Year, Tmdb: b.Tmdb, Tvdb: b.Tvdb, Imdb: b.Imdb }).then(function () {
            blocked = blocked.filter(function (x) { return x !== b; });
            renderBlocked();
            shared.setStatus(blockedStatus, t.format('RequestsToastUnblockedRequestable', b.Title), false);
        }, function (err) {
            btn.disabled = false;
            return shared.errorText(err, t.tr('RequestsErrorUnblock')).then(function (text) { shared.setStatus(blockedStatus, text, true, { timeout: 0 }); });
        });
    }

    // renderServers
    // Draws a row for each server: its name, where it is, the default tag, and its profiles. A row opens its editor.
    function renderServers() {
        var items = servers.map(function (s, i) {
            var arr = ARR[s.Type];
            var named = !!String(s.Name || '').trim() && s.Name.trim().toLowerCase() !== arr.name.toLowerCase();
            var tags = (isDefault(servers, s) ? [{ label: t.tr('Default'), icon: 'star' }] : [])
                .concat((s.Profiles || []).map(function (p) { return { label: p.Name || t.tr('RequestsValueUnnamed') }; }));
            return {
                id: s.Id || String(i),
                index: i,
                title: serverName(s),
                secondary: (named ? arr.name + ' · ' : '') + (hostOf(s.Url) || t.tr('TextNoAddress')) + ((s.Profiles || []).length ? '' : ' · ' + t.tr('RequestsValueNoProfiles')),
                tags: tags
            };
        });
        if (serverList) serverList.setItems(items);
        else {
            serverList = nectoModule.createRecordList({ items: items, label: t.tr('RequestsLabelRadarrAndSonarr'), emptyText: t.tr('RequestsEmptyServers'), onOpen: function (item) { openServer(item.index); } });
            serverMount.appendChild(serverList.element);
        }
    }

    // openServer
    // Opens a Radarr or Sonarr server in an editor sidecar: the default, its name, addresses, and key, search on add,
    // a test that loads its profiles, folders, and tags, and its profiles. Done keeps the changes until Save.
    //
    // Inputs
    // index [number]: The server's place in the list, or -1 for a new one.
    // type [string]: Movie or Series, for a new one.
    function openServer(index, type) {
        var isNew = index < 0;
        var server = isNew
            ? { Id: newId(shared), Type: type, Name: '', Url: '', ExternalUrl: '', ApiKey: '', SearchOnAdd: true, Profiles: [] }
            : JSON.parse(JSON.stringify(servers[index]));
        var arr = ARR[server.Type];
        var isMovie = server.Type === 'Movie';
        var options = { profiles: [], folders: [], tags: [] };
        var profiles = null;
        var wasDefault = isDefault(servers, isNew ? server : servers[index]);
        var savedKey = server.ApiKey === nectoModule.SECRET_KEPT;
        var savedUrl = server.Url || '';
        var ids = { def: fieldId('default'), name: fieldId('name'), url: fieldId('url'), ext: fieldId('external'), key: fieldId('key'), search: fieldId('search') };
        var defaultBox = checkbox(ids.def, 'IsDefault');
        var nameInput = el('input', { is: 'emby-input', type: 'text', id: ids.name, 'data-field': 'Name', maxlength: '100', placeholder: arr.name });
        var urlInput = el('input', { is: 'emby-input', type: 'url', id: ids.url, 'data-field': 'Url', placeholder: arr.example });
        var extInput = el('input', { is: 'emby-input', type: 'url', id: ids.ext, 'data-field': 'ExternalUrl', maxlength: '500', placeholder: 'https://' + arr.name.toLowerCase() + '.example.com' });
        var keyInput = el('input', { is: 'emby-input', type: 'password', id: ids.key, 'data-field': 'ApiKey', autocomplete: 'off' });
        var searchBox = checkbox(ids.search, 'SearchOnAdd');
        var testStatus = el('div', { class: 'stolon-status-text', role: 'status', 'data-role': 'test-status' });
        var doneStatus = el('div', { class: 'stolon-status-text', role: 'status', 'data-role': 'status' });
        var profileMount = el('div', { 'data-role': 'profiles' });
        var testButton = el('button', { is: 'emby-button', type: 'button', class: 'raised stolon-button stolon-secondary stolon-small', 'data-action': 'test' }, [el('span', { text: t.tr('RequestsButtonTestLoadOptions') })]);
        var body = el('div', { 'data-role': 'server-editor' }, [
            el('h3', { class: 'stolon-subsection-title', text: t.tr('TabServer') }),
            checkField(defaultBox, t.tr('RequestsLabelDefaultServer'), t.tr(isMovie ? 'RequestsHelpDefaultServerMovie' : 'RequestsHelpDefaultServerShow')),
            field(ids.name, t.tr('LabelName'), nameInput, t.format('RequestsHelpServerName', arr.name)),
            field(ids.url, t.tr('LabelAddress'), urlInput, t.format('RequestsHelpServerAddress', arr.name)),
            field(ids.ext, t.tr('RequestsLabelExternalUrl'), extInput, t.format('RequestsHelpExternalUrl', arr.name)),
            field(ids.key, sentence(t, 'HeaderApiKey'), keyInput, t.format('RequestsHelpArrApiKey', arr.name)),
            checkField(searchBox, t.tr('RequestsLabelSearchOnAdd'), t.tr('RequestsHelpSearchOnAdd')),
            el('div', { class: 'inputContainer' }, [testButton, testStatus]),
            el('h3', { class: 'stolon-subsection-title', text: t.tr('RequestsHeaderProfiles') }),
            el('div', { class: 'fieldDescription', text: t.format('RequestsHelpArrProfiles', arr.name) }),
            profileMount,
            doneStatus
        ]);
        defaultBox.checked = wasDefault;
        nameInput.value = server.Name || '';
        urlInput.value = server.Url || '';
        extInput.value = server.ExternalUrl || '';
        searchBox.checked = server.SearchOnAdd !== false;
        shared.bindSecretField(keyInput, savedKey);
        if (server.ApiKey && !savedKey) keyInput.value = server.ApiKey;
        // A kept key holds only for the address it was saved with, so a new address asks for the key again.
        urlInput.addEventListener('input', function () {
            if (savedKey && !keyInput.value) shared.bindSecretField(keyInput, sameAddress(urlInput.value, savedUrl));
        });
        if (!server.Profiles.length) server.Profiles.push({ Id: newId(shared), Name: t.tr('Default'), QualityProfileId: 0, RootFolder: '', Tags: [], MinimumAvailability: 'Released', SeriesType: 'Standard', SeasonFolder: true });

        // choices
        // Lists a select's choices, keeping every saved value choosable when the last test did not list it.
        function choices(list, saved) {
            var all = list.map(function (o) { return { value: String(o.value), label: o.label }; });
            saved.filter(Boolean).forEach(function (value) {
                if (!all.some(function (o) { return o.value === String(value); })) all.unshift({ value: String(value), label: t.format('RequestsValueSaved', value) });
            });
            return all;
        }

        // tagItem
        // Names a profile's tag by the last test, or by its id when the test did not list it.
        function tagItem(id) {
            return options.tags.filter(function (x) { return x.value === id; })[0] || { value: id, label: t.format('RequestsValueSavedTag', id) };
        }

        // tagControl
        // Builds a profile's tags as a chip select of the server's own, picked by label and kept by id.
        function tagControl(value, change) {
            var picker = nectoModule.createChipSelect({
                options: options.tags,
                placeholder: options.tags.length ? t.tr('RequestsPlaceholderChooseTags') : t.tr('RequestsPlaceholderNoTags'),
                onChange: change
            });
            picker.setValue((value || []).map(tagItem));
            return {
                element: picker.element,
                input: picker.element.querySelector('input'),
                getValue: function () { return picker.getValue().map(Number).filter(function (x) { return x > 0; }); },
                setValue: function (next) { picker.setValue((next || []).map(tagItem)); },
                destroy: function () { picker.destroy(); }
            };
        }

        // toItem
        // Turns a profile into the repeater's values.
        function toItem(p) {
            return {
                Id: p.Id, Name: p.Name || '', QualityProfileId: p.QualityProfileId > 0 ? String(p.QualityProfileId) : '', RootFolder: p.RootFolder || '',
                Tags: p.Tags || [], MinimumAvailability: enumName(p.MinimumAvailability, AVAILABILITY, 'Released'), SeriesType: enumName(p.SeriesType, SERIES_TYPES),
                SeasonFolder: p.SeasonFolder === false ? 'false' : 'true'
            };
        }

        // fromItem
        // Turns the repeater's values back into a profile, over the profile it was.
        function fromItem(item) {
            var had = server.Profiles.filter(function (x) { return x.Id === item.Id; })[0];
            var p = { Id: item.Id || newId(shared), Name: String(item.Name || '').trim(), QualityProfileId: parseInt(item.QualityProfileId, 10) || 0, RootFolder: item.RootFolder || '', Tags: item.Tags || [] };
            if (isMovie) p.MinimumAvailability = item.MinimumAvailability || 'Released';
            else { p.SeriesType = item.SeriesType || 'Standard'; p.SeasonFolder = item.SeasonFolder !== 'false'; }
            return Object.assign({}, had || {}, p);
        }

        // buildProfiles
        // Draws the profiles as a card each, with the choices the last test brought.
        function buildProfiles() {
            if (profiles) profiles.destroy();
            var tested = options.profiles.length > 0;
            var pairs = function (list) { return list.map(function (o) { return { value: o[0], label: t.tr(o[1]) }; }); };
            profiles = nectoModule.createRepeater({
                layout: 'card',
                titleText: t.tr('RequestsProfileIndex'),
                addText: t.tr('RequestsButtonAddProfile'),
                minRows: 1,
                value: server.Profiles.map(toItem),
                newItem: function () { return { Id: newId(shared) }; },
                fields: [
                    { key: 'Name', label: t.tr('LabelName'), required: true, requiredText: t.tr('RequestsErrorNameEachProfile'), placeholder: '1080p' },
                    {
                        key: 'QualityProfileId', label: t.tr('RequestsLabelQualityProfile'), type: 'select', required: true, requiredText: t.tr('RequestsErrorChooseQualityProfile'),
                        placeholder: tested ? t.tr('RequestsPlaceholderChooseProfile') : t.tr('RequestsPlaceholderTestToLoad'),
                        options: choices(options.profiles, server.Profiles.map(function (p) { return p.QualityProfileId > 0 ? p.QualityProfileId : ''; }))
                    },
                    {
                        key: 'RootFolder', label: t.tr('RequestsLabelRootFolder'), type: 'select', required: true, requiredText: t.tr('RequestsErrorChooseRootFolder'),
                        placeholder: tested ? t.tr('RequestsPlaceholderChooseFolder') : t.tr('RequestsPlaceholderTestToLoad'),
                        options: choices(options.folders, server.Profiles.map(function (p) { return p.RootFolder; }))
                    },
                    { key: 'Tags', label: t.tr('Tags'), type: 'custom', render: tagControl }
                ].concat(isMovie
                    ? [{ key: 'MinimumAvailability', label: t.tr('RequestsLabelMinimumAvailability'), type: 'select', options: pairs([['Released', 'RequestsLabelReleased'], ['InCinemas', 'RequestsOptionInCinemas'], ['Announced', 'RequestsOptionAnnounced']]) }]
                    : [
                        { key: 'SeriesType', label: t.tr('RequestsLabelSeriesType'), type: 'select', options: pairs([['Standard', 'RequestsOptionStandard'], ['Daily', 'OptionDaily'], ['Anime', 'Anime']]) },
                        { key: 'SeasonFolder', label: t.tr('RequestsLabelSeasonFolders'), type: 'select', options: pairs([['true', 'Yes'], ['false', 'No']]) }
                    ])
            });
            profileMount.textContent = '';
            profileMount.appendChild(profiles.element);
        }

        // read
        // Reads the editor into the server.
        function read() {
            // Marked only when changed, since the first server is the default without being marked.
            if (defaultBox.checked !== wasDefault) server.IsDefault = defaultBox.checked;
            server.Name = nameInput.value.trim();
            server.Url = urlInput.value.trim();
            server.ExternalUrl = extInput.value.trim();
            server.SearchOnAdd = !!searchBox.checked;
            if (profiles) server.Profiles = profiles.getValue().map(fromItem);
        }

        // test
        // Tries the address and key, and fills the profile choices from what the server offers.
        function test(quiet) {
            read();
            if (!server.Url) { shared.setStatus(testStatus, t.tr('RequestsErrorEnterAddressFirst'), true, { timeout: 0 }); return Promise.resolve(); }
            if (!quiet) shared.setStatus(testStatus, t.tr('RequestsMessageConnecting'), false, { timeout: 0 });
            return shared.request('Requests/Arr/' + server.Type + '/Test', 'POST', { Url: server.Url, ApiKey: shared.readSecretField(keyInput), ServerId: server.Id }).then(function (result) {
                if (!result || !result.Ok) {
                    shared.setStatus(testStatus, (result && result.Error) || t.format('RequestsErrorCouldNotReach', arr.name), true, { timeout: 0 });
                    return;
                }
                options.profiles = (result.Profiles || []).map(function (p) { return { value: p.Id, label: p.Name }; });
                options.folders = (result.RootFolders || []).map(function (f) { return { value: f, label: f }; });
                options.tags = (result.Tags || []).map(function (x) { return { value: x.Id, label: x.Label }; });
                read();
                buildProfiles();
                if (!quiet) shared.setStatus(testStatus, t.format('RequestsToastConnectedTo', arr.name + (result.Version ? ' ' + result.Version : '')), false, { timeout: 0 });
            }).catch(function () { shared.setStatus(testStatus, t.tr('RequestsErrorTestNotRun'), true, { timeout: 0 }); });
        }

        // done
        // Keeps the server on the page once it has what it needs to send anything.
        function done() {
            read();
            var key = shared.readSecretField(keyInput);
            var problem = !server.Url ? t.tr('RequestsErrorEnterAddress')
                : !isWebAddress(server.Url) ? t.format('RequestsErrorArrAddressScheme', serverName(server))
                : server.ExternalUrl && !isWebAddress(server.ExternalUrl) ? t.tr('RequestsErrorExternalUrlScheme')
                : !key ? t.tr('RequestsErrorEnterApiKey')
                : server.Profiles.some(function (p) { return !(p.QualityProfileId > 0) || !p.RootFolder; }) ? t.tr('RequestsErrorTestThenChoose')
                : server.Profiles.some(function (p) { return !p.Name; }) ? t.tr('RequestsErrorNameEachProfile')
                : '';
            if (problem) {
                if (profiles) profiles.isValid();
                shared.setStatus(doneStatus, problem, true, { timeout: 0 });
                return false;
            }
            server.ApiKey = key;
            if (isNew) servers.push(server); else servers[index] = server;
            // One default per type, so marking this one stands the others down.
            if (server.IsDefault) servers.forEach(function (s) { if (s !== server && s.Type === server.Type) s.IsDefault = false; });
            renderServers();
            changed();
            return true;
        }

        // remove
        // Asks first, then takes the server off the page until Save.
        function remove(close) {
            nectoModule.confirmDialog(t.format('RequestsConfirmRemoveServer', arr.name), { title: t.format('TitleRemoveName', serverName(server)), confirmText: t.tr('ButtonRemove'), destructive: true }).then(function (ok) {
                if (!ok) return;
                servers.splice(index, 1);
                renderServers();
                changed();
                close();
            });
        }

        buildProfiles();
        testButton.addEventListener('click', function () { test(false); });
        if (server.Url && server.ApiKey) test(true);

        return nectoModule.openEditorSidecar({
            page: page,
            title: isNew ? t.tr(isMovie ? 'RequestsButtonAddRadarr' : 'RequestsButtonAddSonarr') : serverName(server),
            body: body,
            isNew: isNew,
            saveText: t.tr('ButtonDone'),
            deleteText: t.tr('ButtonRemove'),
            serialize: function () { read(); return JSON.stringify([server, shared.readSecretField(keyInput)]); },
            onSave: done,
            onDelete: isNew ? undefined : remove,
            onClose: function () { if (profiles) profiles.destroy(); profiles = null; }
        });
    }

    // rowKinds
    // Lists what a home row's titles can be picked by.
    function rowKinds() {
        return [['Genre', t.tr('Genre')], ['Keyword', t.tr('RequestsLabelKeyword')], ['Company', t.tr('Studio')], ['Network', t.tr('RequestsLabelNetwork')], ['Provider', t.tr('RequestsLabelStreamingService')]];
    }

    // rowSorts
    // Lists the orders a home row can take.
    function rowSorts() {
        return [['', t.tr('RequestsSortPopular')], ['TopRated', t.tr('RequestsSortTopRated')], ['Newest', t.tr('RequestsSortNewest')], ['Oldest', t.tr('RequestsSortOldest')], ['Title', t.tr('LabelTitle')]];
    }

    // labelOf
    // Finds a choice's label by its value.
    function labelOf(list, key) {
        return (list.filter(function (x) { return x[0] === key; })[0] || [key, key])[1];
    }

    // builtInRow
    // Finds one of the request page's own rows by its key.
    function builtInRow(key) {
        return BUILT_IN_ROWS.filter(function (b) { return b[0] === key; })[0];
    }

    // rowCard
    // Describes a home row's card: its name, and what it lists or that it is built in. A hidden one is dimmed.
    function rowCard(key) {
        var builtIn = builtInRow(key);
        if (builtIn) {
            var off = hidden.indexOf(key) >= 0;
            return { title: sentence(t, builtIn[1]), subtitle: off ? t.tr('RequestsValueBuiltIn') + ' · ' + t.tr('RequestsValueHidden') : t.tr('RequestsValueBuiltIn'), dot: 'none', disabled: off };
        }
        var r = rows[rows.map(customKey).indexOf(key)] || {};
        var names = String(r.Names || '').split('|').filter(Boolean);
        return {
            title: r.Title,
            subtitle: [enumName(r.Type, TYPES) === 'Series' ? t.tr('Shows') : t.tr('Movies'), labelOf(rowKinds(), r.Kind), labelOf(rowSorts(), r.Sort || '')].join(' · '),
            badges: (names.length ? names : String(r.Ids || '').split(',').filter(Boolean)).map(function (n) { return { label: n }; }),
            dot: 'none'
        };
    }

    // rowEditor
    // Fills the open card: a built-in row's Show checkbox, or Edit row for one of the server's own.
    function rowEditor(key) {
        rowEditorEl.textContent = '';
        if (builtInRow(key)) {
            var box = el('input', { type: 'checkbox', is: 'emby-checkbox', 'data-show-row': key });
            box.checked = hidden.indexOf(key) < 0;
            rowEditorEl.appendChild(checkField(box, t.tr('RequestsLabelShowOnRequestPage'), t.tr('RequestsHelpHiddenRow')));
            return;
        }
        rowEditorEl.appendChild(el('div', { class: 'stolon-card-list-actions' }, [
            el('button', { is: 'emby-button', type: 'button', class: 'raised stolon-button stolon-secondary stolon-small', 'data-edit-row': String(rows.map(customKey).indexOf(key)) }, [
                el('span', { class: 'material-icons', 'aria-hidden': 'true', text: 'edit' }),
                el('span', { text: t.tr('RequestsButtonEditRow') })
            ])
        ]));
    }

    // renderRows
    // Draws every home row in order as a sortable card list.
    function renderRows() {
        var keys = rowKeys(rows, order);
        if (rowCards) {
            rowCards.setItems(keys);
            if (rowCards.getSelected() >= 0) rowEditor(keys[rowCards.getSelected()]);
            return;
        }
        rowCards = nectoModule.createSortableCardList(rowMount, {
            items: keys,
            editor: rowEditorEl,
            render: rowCard,
            onSelect: function (i) { rowEditor(rowKeys(rows, order)[i]); },
            onMove: function (from, to) {
                var next = rowKeys(rows, order);
                next.splice(to, 0, next.splice(from, 1)[0]);
                order = next;
                changed();
            }
        });
        rowCards.render();
    }

    // openRow
    // Opens a home row in an editor sidecar: its heading, movies or shows, what picks its titles, and its order.
    //
    // Inputs
    // index [number]: The row's place in the list, or -1 for a new one.
    function openRow(index) {
        var isNew = index < 0;
        var row = isNew ? { Id: newId(shared), Title: '', Type: 'Movie', Kind: 'Genre', Ids: '', Names: '', Sort: '' } : JSON.parse(JSON.stringify(rows[index]));
        row.Type = enumName(row.Type, TYPES);
        var ids = { title: fieldId('row-title'), type: fieldId('row-type'), kind: fieldId('row-kind'), sort: fieldId('row-sort') };
        var select = function (id, list) {
            var s = el('select', { is: 'emby-select', id: id });
            list.forEach(function (o) { s.appendChild(el('option', { value: o[0], text: o[1] })); });
            return s;
        };
        var titleInput = el('input', { is: 'emby-input', type: 'text', id: ids.title, 'data-field': 'Title', maxlength: '60' });
        var typeSelect = select(ids.type, [['Movie', t.tr('Movies')], ['Series', t.tr('Shows')]]);
        var kindSelect = select(ids.kind, rowKinds());
        var sortSelect = select(ids.sort, rowSorts());
        var picksMount = el('div', { 'data-role': 'row-picks' });
        var hint = el('div', { class: 'fieldDescription', 'data-role': 'row-hint' });
        var status = el('div', { class: 'stolon-status-text', role: 'status', 'data-role': 'status' });
        var body = el('div', { 'data-role': 'row-editor' }, [
            field(ids.title, t.tr('RequestsLabelHeading'), titleInput, t.tr('RequestsHelpRowHeading')),
            field(ids.type, t.tr('RequestsLabelLists'), typeSelect, '', true),
            field(ids.kind, t.tr('RequestsLabelPickedBy'), kindSelect, '', true),
            el('div', { class: 'inputContainer' }, [el('div', { class: 'inputLabel', text: t.tr('RequestsLabelAnyOf') }), picksMount, hint]),
            field(ids.sort, t.tr('RequestsLabelOrder'), sortSelect, '', true),
            status
        ]);
        titleInput.value = row.Title || '';
        typeSelect.value = row.Type;
        kindSelect.value = row.Kind || 'Genre';
        sortSelect.value = row.Sort || '';
        var picks = null;
        var listed = {};

        // makePicks
        // Builds the picks for the kind and type chosen: genres and services listed whole, keywords and studios found
        // as typed, and networks by TMDb id.
        function makePicks(values) {
            if (picks) picks.destroy();
            var kind = kindSelect.value;
            var type = typeSelect.value;
            var searched = kind === 'Keyword' || kind === 'Company';
            picks = nectoModule.createChipSelect({
                value: values || [],
                allowCreate: kind === 'Network',
                placeholder: kind === 'Network' ? t.tr('RequestsPlaceholderNetworkId') : t.tr('RequestsPlaceholderTypeToFind'),
                searchFn: searched ? function (q, cb) { if ((q || '').trim().length < 2) { cb([]); return; } tmdbChoices(kind, type, q).then(cb); } : undefined
            });
            picksMount.textContent = '';
            picksMount.appendChild(picks.element);
            var input = picks.element.querySelector('input');
            if (input) input.setAttribute('aria-label', t.tr('RequestsLabelAnyOf'));
            hint.textContent = kind === 'Network' ? t.tr('RequestsHelpNetworkId') : '';
            if (!searched && kind !== 'Network') {
                var key = kind + type;
                var mine = picks;
                (listed[key] || (listed[key] = tmdbChoices(kind, type))).then(function (list) {
                    if (!list.length) delete listed[key];
                    if (picks === mine) mine.setOptions(list);
                });
            }
        }

        // syncKinds
        // Keeps Network for shows only.
        function syncKinds() {
            var network = kindSelect.querySelector('option[value="Network"]');
            if (network) network.disabled = typeSelect.value !== 'Series';
            if (typeSelect.value !== 'Series' && kindSelect.value === 'Network') kindSelect.value = 'Genre';
        }

        var names = String(row.Names || '').split('|');
        syncKinds();
        makePicks(String(row.Ids || '').split(',').filter(Boolean).map(function (id, i) { return { value: id, label: names[i] || id }; }));
        typeSelect.addEventListener('change', function () { syncKinds(); makePicks([]); });
        kindSelect.addEventListener('change', function () { makePicks([]); });

        // done
        // Keeps the row on the page once it has a heading and a pick.
        function done() {
            var items = picks.getItems().filter(function (p) { return /^\d+$/.test(String(p.value)); });
            row.Title = titleInput.value.trim();
            row.Type = typeSelect.value;
            row.Kind = kindSelect.value;
            row.Sort = sortSelect.value;
            row.Ids = items.map(function (p) { return p.value; }).join(',');
            row.Names = items.map(function (p) { return String(p.label).replace(/\|/g, ' '); }).join('|');
            var problem = !row.Title ? t.tr('RequestsErrorEnterHeading') : !items.length ? t.tr('RequestsErrorPickOne') : '';
            if (problem) {
                shared.setStatus(status, problem, true, { timeout: 0 });
                return false;
            }
            if (isNew) rows.push(row); else rows[index] = row;
            renderRows();
            changed();
            return true;
        }

        return nectoModule.openEditorSidecar({
            page: page,
            title: isNew ? t.tr('RequestsButtonAddRow') : row.Title,
            body: body,
            size: 'medium',
            isNew: isNew,
            saveText: t.tr('ButtonDone'),
            deleteText: t.tr('ButtonRemove'),
            serialize: function () { return JSON.stringify([titleInput.value, typeSelect.value, kindSelect.value, sortSelect.value, picks ? picks.getValue() : []]); },
            onSave: done,
            onDelete: isNew ? undefined : function (close) {
                rows.splice(index, 1);
                renderRows();
                changed();
                close();
            },
            onClose: function () { if (picks) picks.destroy(); }
        });
    }

    content.addEventListener('click', function (e) {
        var target = e.target.closest ? e.target : null;
        if (!target) return;
        var hit;
        if ((hit = target.closest('[data-unblock]'))) { unblock(parseInt(hit.getAttribute('data-unblock'), 10), hit); return; }
        if ((hit = target.closest('[data-add-server]'))) { openServer(-1, hit.getAttribute('data-add-server')); return; }
        if ((hit = target.closest('[data-edit-row]'))) { openRow(parseInt(hit.getAttribute('data-edit-row'), 10)); return; }
        if (target.closest('[data-action="add-row"]')) { openRow(-1); return; }
        if (target.closest('[data-action="clear-tmdb-key"]')) { shared.clearSecretField(inputs.TmdbApiKey); clearKey.classList.add('stolon-hidden'); changed(); }
    });
    content.addEventListener('change', function (e) {
        var box = e.target && e.target.closest ? e.target.closest('[data-show-row]') : null;
        if (!box) return;
        var key = box.getAttribute('data-show-row');
        var at = hidden.indexOf(key);
        if (box.checked && at >= 0) hidden.splice(at, 1);
        if (!box.checked && at < 0) hidden.push(key);
        if (rowCards) rowCards.render();
    });

    return {
        load: function (config) {
            var r = (config && config.Requests) || {};
            var list = function (v) { return (v || []).map(String); };
            inputs.Enabled.checked = !!r.Enabled;
            chips.AutoApproveRatings.setValue(list(r.AutoApproveRatings));
            chips.BlockedGenres.setValue(list(r.BlockedGenres));
            chips.BlockedRatings.setValue(list(r.BlockedRatings));
            chips.BlockedKeywords.setValue(list(r.BlockedKeywords));
            chips.BlockedTags.setValue((r.BlockedTags || []).map(function (x) { return { value: String(x.Id), label: x.Name }; }));
            chips.BlockedCollections.setValue((r.BlockedCollections || []).map(function (x) { return { value: String(x.Id), label: x.Name }; }));
            chips.BlockedLanguages.setValue((r.BlockedLanguages || []).map(function (c) { return { value: c, label: languageName(c, t) }; }));
            inputs.AgingAfterDays.value = String(r.AgingAfterDays || 0);
            inputs.AutoDeclineAfterDays.value = String(r.AutoDeclineAfterDays || 0);
            inputs.AutoDeclineReason.value = r.AutoDeclineReason || '';
            inputs.FullSeriesOnly.checked = !!r.FullSeriesOnly;
            inputs.AllowSpecials.checked = !!r.AllowSpecials;
            inputs.MovieQuotaDays.value = String(typeof r.MovieQuotaDays === 'number' ? r.MovieQuotaDays : NUMBER_DEFAULTS.MovieQuotaDays);
            inputs.SeriesQuotaDays.value = String(typeof r.SeriesQuotaDays === 'number' ? r.SeriesQuotaDays : NUMBER_DEFAULTS.SeriesQuotaDays);
            inputs.RetentionDays.value = String(typeof r.RetentionDays === 'number' ? r.RetentionDays : NUMBER_DEFAULTS.RetentionDays);
            inputs.RefreshOnAvailable.value = enumName(r.RefreshOnAvailable, REFRESH_MODES);
            inputs.HideAvailableInRows.checked = !!r.HideAvailableInRows;
            shared.bindSecretField(inputs.TmdbApiKey, !!r.TmdbApiKey, { clearButton: clearKey });
            clearKey.classList.toggle('stolon-hidden', !r.TmdbApiKey);
            blocked = JSON.parse(JSON.stringify(r.BlockedTitles || []));
            servers = keptServers(r.ArrServers);
            rows = JSON.parse(JSON.stringify(r.HomeRows || []));
            order = (r.HomeRowOrder || []).slice();
            hidden = (r.HiddenHomeRows || []).slice();
            renderBlocked();
            renderServers();
            renderRows();
            loadSuggestions().then(function (s) {
                chips.AutoApproveRatings.setOptions(s.ratings);
                chips.BlockedRatings.setOptions(s.ratings);
                chips.BlockedGenres.setOptions(s.genres);
            });
        },
        apply: function (config) {
            config.Requests = config.Requests || {};
            var r = config.Requests;
            r.Enabled = !!inputs.Enabled.checked;
            var problem = arrServersError(servers, r.ArrServers, t);
            if (problem) throw new Error(problem);
            var named = function (path) {
                return chips[path].getItems().filter(function (p) { return /^\d+$/.test(String(p.value)); }).map(function (p) { return { Id: Number(p.value), Name: String(p.label) }; });
            };
            var list = function (path) { return chips[path].getValue().map(String); };
            r.AutoApproveRatings = list('AutoApproveRatings');
            r.AgingAfterDays = intOf(inputs.AgingAfterDays, NUMBER_DEFAULTS.AgingAfterDays);
            r.AutoDeclineAfterDays = intOf(inputs.AutoDeclineAfterDays, NUMBER_DEFAULTS.AutoDeclineAfterDays);
            r.AutoDeclineReason = inputs.AutoDeclineReason.value.trim();
            r.FullSeriesOnly = !!inputs.FullSeriesOnly.checked;
            r.AllowSpecials = !!inputs.AllowSpecials.checked;
            r.MovieQuotaDays = intOf(inputs.MovieQuotaDays, NUMBER_DEFAULTS.MovieQuotaDays, 1);
            r.SeriesQuotaDays = intOf(inputs.SeriesQuotaDays, NUMBER_DEFAULTS.SeriesQuotaDays, 1);
            r.BlockedGenres = list('BlockedGenres');
            r.BlockedRatings = list('BlockedRatings');
            r.BlockedKeywords = list('BlockedKeywords');
            r.BlockedTags = named('BlockedTags');
            r.BlockedCollections = named('BlockedCollections');
            r.BlockedLanguages = list('BlockedLanguages');
            r.ArrServers = JSON.parse(JSON.stringify(servers));
            r.TmdbApiKey = shared.readSecretField(inputs.TmdbApiKey);
            r.HideAvailableInRows = !!inputs.HideAvailableInRows.checked;
            r.HomeRows = JSON.parse(JSON.stringify(rows));
            r.HomeRowOrder = rowKeys(rows, order);
            r.HiddenHomeRows = hidden.slice();
            r.RefreshOnAvailable = enumName(inputs.RefreshOnAvailable.value, REFRESH_MODES);
            r.RetentionDays = intOf(inputs.RetentionDays, NUMBER_DEFAULTS.RetentionDays);
        },
        destroy: function () {
            handles.splice(0).forEach(function (h) { if (h && h.destroy) h.destroy(); });
            if (serverList) { serverList.destroy(); serverList = null; }
            if (blockedList) { blockedList.destroy(); blockedList = null; }
            if (rowCards) { rowCards.destroy(); rowCards = null; }
        }
    };
}

// buildIssueSettings
// Fills the Issues section of Settings: whether issues are on, how long closed ones stay, and the limit's window.
//
// Inputs
// content [HTMLElement]: The section's content.
// ctx [object]: shared is the Necto helper bag.
//
// Output
// [object]: A handle with load(config) and apply(config).
function buildIssueSettings(content, ctx) {
    var t = ctx.shared.t;
    var enabled = checkbox(fieldId('issues-enabled'), 'Issues.Enabled');
    var keepId = fieldId('issues-keep');
    var windowId = fieldId('issues-window');
    var keep = numberInput(keepId, 'Issues.RetentionDays', 0);
    var limitWindow = numberInput(windowId, 'Issues.LimitDays', 1);
    var options = el('div', { class: 'stolon-indent', 'data-role': 'issue-options' }, [
        field(keepId, t.tr('RequestsLabelKeepClosedIssuesDays'), keep, t.tr('RequestsHelpKeepClosedIssues')),
        field(windowId, t.tr('RequestsLabelIssueQuotaDays'), limitWindow, t.tr('IssuesHelpLimitWindow'))
    ]);
    var help = linkSentence(t.tr('IssuesHelpEnable'), pageUrl('issues'), 'fieldDescription checkboxFieldDescription');
    content.appendChild(el('div', { class: 'checkboxContainer checkboxContainer-withDescription' }, [
        el('label', {}, [enabled, el('span', { text: t.tr('IssuesLabelEnable') })]),
        help
    ]));
    content.appendChild(options);

    // reflect
    // Shows the issue options while issues are on.
    function reflect() {
        options.classList.toggle('stolon-hidden', !enabled.checked);
    }
    enabled.addEventListener('change', reflect);

    return {
        load: function (config) {
            var i = (config && config.Issues) || {};
            enabled.checked = !!i.Enabled;
            keep.value = String(typeof i.RetentionDays === 'number' ? i.RetentionDays : NUMBER_DEFAULTS.IssueRetentionDays);
            limitWindow.value = String(typeof i.LimitDays === 'number' && i.LimitDays > 0 ? i.LimitDays : NUMBER_DEFAULTS.IssueLimitDays);
            reflect();
        },
        apply: function (config) {
            config.Issues = config.Issues || {};
            config.Issues.Enabled = !!enabled.checked;
            config.Issues.RetentionDays = intOf(keep, NUMBER_DEFAULTS.IssueRetentionDays);
            config.Issues.LimitDays = intOf(limitWindow, NUMBER_DEFAULTS.IssueLimitDays, 1);
        }
    };
}

// buildTaskSection
// Fills a Troubleshooting section with a row per scheduled task, each run now on its own button, and any extra rows.
//
// Inputs
// content [HTMLElement]: The section's content.
// ctx [object]: shared is the Necto helper bag.
// tasks [object[]]: Each task's key, icon, label key, and help key.
// extra [function]: Adds rows of its own to the group and wires them, given the group and report. [optional]
//
// Output
// [object]: A handle with load and destroy.
function buildTaskSection(content, ctx, tasks, extra) {
    var shared = ctx.shared;
    var t = shared.t;
    var pollers = [];
    var status = el('div', { class: 'stolon-status-text', role: 'status', 'data-role': 'status' });
    var group = el('div', { class: 'stolon-reset-group' });
    content.appendChild(status);
    content.appendChild(el('h3', { class: 'stolon-subsection-title', text: t.tr('HeaderScheduledChecks') }));
    content.appendChild(group);

    // report
    // Shows a result on the section's status line.
    function report(message, isError) {
        shared.setStatus(status, message, !!isError, { timeout: 10000 });
    }

    tasks.forEach(function (task) {
        var row = nectoModule.resetRow({ icon: task.icon, label: t.tr(task.label), help: t.tr(task.help), button: t.tr('ButtonRunTask'), action: 'run-' + task.key });
        group.appendChild(row);
        var btn = row.querySelector('button');
        btn.addEventListener('click', function () {
            shared.runTask(btn, task.key, t.tr('ButtonRunTask')).then(function (poller) {
                pollers.push(poller);
            }).catch(function (err) {
                return shared.errorText(err, t.tr('ErrorRunTask')).then(function (text) { report(text, true); });
            });
        });
    });
    if (extra) extra(group, report);

    return {
        load: function () {},
        destroy: function () {
            pollers.splice(0).forEach(function (p) { if (p && p.cancel) p.cancel(); });
        }
    };
}

// buildRequestTroubleshooting
// Fills the Requests section of Troubleshooting: each scheduled task run now, and the statuses refreshed at once.
//
// Inputs
// content [HTMLElement]: The section's content.
// ctx [object]: shared is the Necto helper bag.
//
// Output
// [object]: A handle with load and destroy.
function buildRequestTroubleshooting(content, ctx) {
    var shared = ctx.shared;
    var t = shared.t;
    return buildTaskSection(content, ctx, [
        { key: SWEEP_TASK, icon: 'manage_search', label: 'RequestsTaskSweepName', help: 'RequestsTaskSweepDescription' },
        { key: SYNC_TASK, icon: 'sync', label: 'RequestsTaskSyncName', help: 'RequestsTaskSyncDescription' },
        { key: DISCOVERY_TASK, icon: 'explore', label: 'RequestsTaskDiscoveryName', help: 'RequestsTaskDiscoveryDescription' },
        { key: CLEANUP_TASK, icon: 'cleaning_services', label: 'RequestsTaskCleanupName', help: 'RequestsTaskCleanupDescription' }
    ], function (group, report) {
        var row = nectoModule.resetRow({ icon: 'refresh', label: t.tr('RequestsLabelRefreshStatuses'), help: t.tr('RequestsHelpRefreshStatuses'), button: t.tr('RequestsButtonRefreshNow'), action: 'refresh-statuses' });
        group.appendChild(row);
        var btn = row.querySelector('button');
        btn.addEventListener('click', function () {
            btn.disabled = true;
            report(t.tr('RequestsMessageRefreshing'), false);
            return shared.request('Requests/Refresh', 'POST').then(function () {
                report(t.tr('RequestsToastStatusesRefreshed'), false);
            }).catch(function (err) {
                return shared.errorText(err, t.tr('RequestsErrorRefreshStatuses')).then(function (text) { report(text, true); });
            }).then(function () { btn.disabled = false; });
        });
    });
}

// buildIssueTroubleshooting
// Fills the Issues section of Troubleshooting: the clean up run now, which clears closed issues and marks changed files.
//
// Inputs
// content [HTMLElement]: The section's content.
// ctx [object]: shared is the Necto helper bag.
//
// Output
// [object]: A handle with load and destroy.
function buildIssueTroubleshooting(content, ctx) {
    return buildTaskSection(content, ctx, [{ key: CLEANUP_TASK, icon: 'cleaning_services', label: 'RequestsTaskCleanupName', help: 'IssuesHelpCleanupTask' }]);
}

nectoModule.registerSection('settings', 'requests', { build: buildRequestSettings });
nectoModule.registerSection('settings', 'issues', { build: buildIssueSettings });
nectoModule.registerSection('troubleshooting', 'requests', { build: buildRequestTroubleshooting });
nectoModule.registerSection('troubleshooting', 'issues', { build: buildIssueTroubleshooting });
