// necto_proxy_sections.js
// The Proxy sections of Settings and Troubleshooting, which this module adds to the registry when it loads.

import * as nectoModule from './configurationpage?name=necto_shared.js';
import * as fields from './configurationpage?name=necto_netfields.js';

var el = nectoModule.el;

// The keys of the scheduled tasks Troubleshooting runs.
export var CERTIFICATE_TASK = 'NectoProxyCertificates';
export var LOCATION_TASK = 'NectoProxyLocations';
export var EXTERNAL_TASK = 'NectoProxyExternalAccess';

var COUNTRY_MODES = ['Off', 'ServerCountry', 'Block', 'AllowOnly'];
var LOCATIONS = ['Country', 'City', 'Off'];

// advancedSpecs
// Describes the listener and protection settings, by subsection. Each field names its key in the advanced
// settings, its kind, and its label and help keys. An empty field follows the built-in default.
//
// Output
// [object[]]: Each subsection's title key and fields.
export function advancedSpecs() {
    return [
        {
            title: 'ProxyHeaderListener', fields: [
                { key: 'HttpPort', kind: 'int', label: 'ProxyLabelHttpPort', help: 'ProxyHttpPortHelp', min: 1, max: 65535 },
                { key: 'HttpsPort', kind: 'int', label: 'ProxyLabelHttpsPort', help: 'ProxyHttpsPortHelp', min: 1, max: 65535 },
                { key: 'LanAddress', kind: 'text', label: 'ProxyLabelLan', help: 'ProxyLanAddressHelp', placeholder: '192.168.1.10' },
                { key: 'EnableHttp2', kind: 'bool', label: 'ProxyLabelHttp2', help: 'ProxyHttp2Help' }
            ]
        },
        {
            title: 'ProxyHeaderJellyfinSide', fields: [
                { key: 'JellyfinLoopbackOnly', kind: 'bool', label: 'ProxyLabelLoopbackOnly', help: 'ProxyLoopbackOnlyHelp' },
                { key: 'TrustedNetworks', kind: 'lines', label: 'ProxyLabelTrustedNetworks', help: 'ProxyTrustedNetworksHelp', placeholder: '100.64.0.0/10' }
            ]
        },
        {
            title: 'ProxyHeaderSecurity', fields: [
                { key: 'HstsEnabled', kind: 'bool', label: 'ProxyLabelHsts', help: 'ProxyHstsHelp' },
                { key: 'HstsMaxAgeDays', kind: 'int', label: 'ProxyLabelHstsMaxAge', help: 'ProxyHstsMaxAgeHelp', min: 1, max: 730 },
                { key: 'SameOriginFramesOnly', kind: 'bool', label: 'ProxyLabelSameOriginFrames', help: 'ProxySameOriginFramesHelp' }
            ]
        },
        {
            title: 'ProxyHeaderBans', fields: [
                { key: 'BlockedAddresses', kind: 'lines', label: 'ProxyLabelBlockedAddresses', help: 'ProxyBlockedAddressesHelp', placeholder: '203.0.113.0/24' },
                { key: 'BanFailures', kind: 'int', label: 'ProxyLabelBanFailures', help: 'ProxyBanFailuresHelp', min: 0, max: 1000 },
                { key: 'BanWindowMinutes', kind: 'int', label: 'ProxyLabelBanWindow', help: 'ProxyBanWindowHelp', min: 1, max: 1440 },
                { key: 'BanMinutes', kind: 'int', label: 'ProxyLabelBanLength', help: 'ProxyBanLengthHelp', min: 0 }
            ]
        },
        {
            title: 'HeaderLocations', fields: [
                { key: 'CountryMode', kind: 'enum', label: 'ProxyLabelCountryRule', help: 'ProxyCountryRuleHelp', names: COUNTRY_MODES, options: ['Off', 'ProxyOptionServerCountry', 'ProxyOptionBlockCountries', 'ProxyOptionAllowCountries'] },
                { key: 'Countries', kind: 'countries', label: 'HeaderCountries', help: 'ProxyCountriesHelp' },
                { key: 'BlockUnknownCountries', kind: 'bool', label: 'ProxyLabelBlockUnknown', help: 'ProxyBlockUnknownHelp' },
                { key: 'BlockWithoutDatabase', kind: 'bool', label: 'ProxyLabelBlockWithoutDatabase', help: 'ProxyBlockWithoutDatabaseHelp' }
            ]
        },
        {
            title: 'ProxyHeaderRateLimits', fields: [
                { key: 'MaxConnectionsPerIp', kind: 'int', label: 'ProxyLabelMaxConnectionsPerIp', help: 'ProxyMaxConnectionsPerIpHelp', min: 0 },
                { key: 'MaxConnections', kind: 'int', label: 'ProxyLabelMaxConnections', help: 'ProxyMaxConnectionsHelp', min: 0 },
                { key: 'MaxRequestBodyMbWan', kind: 'int', label: 'ProxyLabelMaxUploadWan', help: 'ProxyMaxUploadWanHelp', min: 0 },
                { key: 'MaxRequestBodyMbLan', kind: 'int', label: 'ProxyLabelMaxUploadLan', help: 'ProxyMaxUploadLanHelp', min: 0 }
            ]
        },
        {
            title: 'ProxyHeaderTimeouts', fields: [
                { key: 'KeepAliveSeconds', kind: 'int', label: 'ProxyLabelKeepAlive', help: 'ProxyKeepAliveHelp', min: 5, max: 3600 },
                { key: 'RequestHeadersTimeoutSeconds', kind: 'int', label: 'ProxyLabelHeadersTimeout', help: 'ProxyHeadersTimeoutHelp', min: 1, max: 300 },
                { key: 'MaxRequestHeadersKb', kind: 'int', label: 'ProxyLabelMaxHeaders', help: 'ProxyMaxHeadersHelp', min: 8, max: 1024 },
                { key: 'MinResponseBytesPerSecond', kind: 'int', label: 'ProxyLabelMinResponseRate', help: 'ProxyMinResponseRateHelp', min: 0 },
                { key: 'BackendActivityTimeoutSeconds', kind: 'int', label: 'ProxyLabelBackendTimeout', help: 'ProxyBackendTimeoutHelp', min: 61, max: 86400 }
            ]
        }
    ];
}

// isAuto
// Tells whether a setting's default depends on the server's memory, so an empty field follows it.
//
// Inputs
// defaults [object]: The answer from Proxy/Defaults.
// key [string]: The setting.
//
// Output
// [boolean]: Whether the standard and low memory defaults differ.
export function isAuto(defaults, key) {
    return !!(defaults && defaults.Standard && defaults.LowMemory)
        && JSON.stringify(defaults.Standard[key]) !== JSON.stringify(defaults.LowMemory[key]);
}

// readAdvanced
// Reads the advanced fields into the values to save. A value equal to its default is saved as unset, so a better
// default later applies on its own, and an empty Auto field follows the server's memory.
//
// Inputs
// inputs [object]: The field handles by key.
// defaults [object]: The answer from Proxy/Defaults.
// countries [object]: The country chip select.
//
// Output
// [object]: The advanced settings.
export function readAdvanced(inputs, defaults, countries) {
    var values = (defaults && defaults.Values) || {};
    var out = {};
    advancedSpecs().forEach(function (sub) {
        sub.fields.forEach(function (spec) {
            var f = inputs[spec.key];
            if (!f) return;
            var v;
            if (spec.kind === 'bool') v = !!f.input.checked;
            else if (spec.kind === 'int') v = fields.readNumber(f.input);
            else if (spec.kind === 'lines') v = fields.readLines(f.input);
            else if (spec.kind === 'enum') v = f.input.value;
            else if (spec.kind === 'countries') v = countries ? countries.getValue().map(String) : [];
            else v = String(f.input.value || '').trim();
            // Lists and text always go as they are, since the server reads an empty text as unset and refuses null.
            if (spec.kind === 'lines' || spec.kind === 'countries' || spec.kind === 'text') { out[spec.key] = v; return; }
            if (v === null || v === '' || JSON.stringify(v) === JSON.stringify(values[spec.key])) v = null;
            out[spec.key] = v;
        });
    });
    return out;
}

// countryName
// Names a country in the viewer's language.
//
// Inputs
// code [string]: The two letter code.
//
// Output
// [string]: The name, or the code when the browser has none.
export function countryName(code) {
    try {
        return new Intl.DisplayNames([document.documentElement.lang || 'en'], { type: 'region' }).of(code) || code;
    } catch (e) {
        return code;
    }
}

// The settings a reset keeps, since they are lists the administrator built rather than tuning.
export var KEPT_ON_RESET = ['TrustedNetworks', 'BlockedAddresses', 'CountryMode', 'Countries'];

// countryNote
// Says what the picked country rule does, where this server is for the server country rule, and what happens while
// the location database is missing.
//
// Inputs
// mode [string]: The country rule, by name.
// status [object]: The answer from Proxy/Status, or empty while it is unknown.
// blocksWithoutDatabase [boolean]: Whether every connection is refused while the database is missing.
// t [object]: The strings.
//
// Output
// [object]: text is the note, and warn whether part of it is a warning.
export function countryNote(mode, status, blocksWithoutDatabase, t) {
    var keys = { Off: 'ProxyCountryModeOff', ServerCountry: 'ProxyCountryModeServer', Block: 'ProxyCountryModeBlock', AllowOnly: 'ProxyCountryModeAllowOnly' };
    var parts = [t.tr(keys[mode] || keys.ServerCountry)];
    var loaded = !!(status && status.CountryDatabaseLoaded);
    var warn = false;
    if (mode === 'ServerCountry' && status && (status.ServerCountry || loaded)) {
        if (status.ServerCountry) parts.push(t.format('ProxyCountryServerIn', countryName(status.ServerCountry)));
        else {
            parts.push(t.tr('ProxyCountryUntilKnown'));
            warn = true;
        }
    }
    if (mode !== 'Off' && status && 'CountryDatabaseLoaded' in status && !loaded) {
        parts.push(t.tr(blocksWithoutDatabase ? 'ProxyDatabaseMissingBlocks' : 'ProxyDatabaseMissingAllows'));
        parts.push(t.tr('ProxyDatabaseMissingFix'));
        warn = true;
    }
    return { text: parts.join(' '), warn: warn };
}

// refusedText
// Sums who the country rules refused since Jellyfin started, the most refused country first.
//
// Inputs
// refused [object]: The count refused by country code from Proxy/Status, with an empty code for unknown.
// t [object]: The strings.
//
// Output
// [string]: The sentence.
export function refusedText(refused, t) {
    refused = refused || {};
    var codes = Object.keys(refused).sort(function (a, b) { return refused[b] - refused[a]; });
    if (!codes.length) return t.tr('ProxyCountryNothingRefused');
    return t.format('ProxyCountryRefusedSince', codes.map(function (code) {
        return t.format('ProxyCountryCount', code ? countryName(code) : t.tr('Unknown'), refused[code]);
    }).join(', '));
}

// locationNote
// Says what the picked location detail costs.
//
// Inputs
// detail [string]: Country, City, or Off.
// t [object]: The strings.
//
// Output
// [string]: The note.
export function locationNote(detail, t) {
    var keys = { Country: 'ProxyLocationNoteCountry', City: 'ProxyLocationNoteCity', Off: 'ProxyLocationNoteOff' };
    return t.tr(keys[detail] || keys.Country);
}

// turnsOnLoopback
// Tells whether a save turns on Only through the proxy, which cuts off every client that uses Jellyfin's own
// address, so the save asks first.
//
// Inputs
// stored [object]: The stored advanced settings.
// checked [boolean]: Whether the field is on.
// defaults [object]: The answer from Proxy/Defaults.
//
// Output
// [boolean]: Whether it was off and is now on.
export function turnsOnLoopback(stored, checked, defaults) {
    var value = stored && stored.JellyfinLoopbackOnly;
    var was = value == null ? !!(defaults && defaults.Values && defaults.Values.JellyfinLoopbackOnly) : !!value;
    return !!checked && !was;
}

// resetChanges
// Lists the stored advanced settings a reset puts back to their defaults, leaving out the kept lists.
//
// Inputs
// advanced [object]: The stored advanced settings.
//
// Output
// [object[]]: Each changed setting's key and label key, in the order Settings shows them.
export function resetChanges(advanced) {
    advanced = advanced || {};
    var out = [];
    advancedSpecs().forEach(function (sub) {
        sub.fields.forEach(function (spec) {
            if (KEPT_ON_RESET.indexOf(spec.key) >= 0) return;
            if (advanced[spec.key] != null) out.push({ key: spec.key, label: spec.label });
        });
    });
    return out;
}

// resetAdvanced
// Gives the advanced settings after a reset: only the kept lists, so every other value follows its default.
//
// Inputs
// advanced [object]: The stored advanced settings.
//
// Output
// [object]: The settings to save.
export function resetAdvanced(advanced) {
    advanced = advanced || {};
    var out = {};
    KEPT_ON_RESET.forEach(function (key) { if (advanced[key] != null) out[key] = advanced[key]; });
    return out;
}

// nameItems
// Describes the public name as label and value pairs: the name, the addresses it points to, and this server's
// public address.
//
// Inputs
// name [object]: The Name from Proxy/Status, or null.
// t [object]: The strings.
//
// Output
// [object[]]: The pairs, or none when no public name is set.
export function nameItems(name, t) {
    if (!name || !name.Hostname) return [];
    return [
        { label: t.tr('ProxyCardPublicName'), value: name.Hostname },
        { label: t.tr('ProxyLabelPointsTo'), value: (name.Addresses || []).join(', ') || t.tr('None') },
        { label: t.tr('ProxyLabelThisServer'), value: name.PublicAddress || t.tr('Unknown') }
    ];
}

// buildSettings
// Fills the Proxy section of Settings: turning the proxy on, the Let's Encrypt terms, the visitor history, and the
// listener and protection values. It saves through Proxy/Settings, which leaves the routes alone.
//
// Inputs
// content [HTMLElement]: The section's content.
// ctx [object]: shared is the Necto helper bag.
//
// Output
// [object]: A handle with load and save.
function buildSettings(content, ctx) {
    var shared = ctx.shared;
    var t = shared.t;
    var defaults = null;
    var stored = null;
    var proxyStatus = null;
    var inputs = {};
    var countries = null;
    var countryText = el('div', { class: 'fieldDescription', 'data-role': 'country-note' });
    var refusedLine = el('div', { class: 'fieldDescription', 'data-role': 'country-refused' });
    var locationLine = el('div', { class: 'fieldDescription', 'data-role': 'location-note' });
    var status = el('div', { class: 'stolon-status-text', role: 'status' });
    var enabled = fields.checkField({ label: t.tr('ProxyLabelEnableProxy'), help: t.tr('ProxyEnableHelp'), field: 'Enabled' });
    var terms = fields.checkField({ label: t.tr('ProxyLabelAcceptTerms'), help: t.tr('ProxyAcceptTermsHelp'), field: 'AcceptTerms' });
    var history = fields.checkField({ label: t.tr('ProxyLabelRecordHistory'), help: t.tr('ProxyRecordHistoryHelp'), field: 'Visitors.Enabled' });
    var days = fields.inputField({ label: t.tr('ProxyLabelHistoryDays'), help: t.tr('ProxyHistoryDaysHelp'), field: 'Visitors.HistoryDays', type: 'number', min: 1, max: 3650 });
    var location = fields.selectField({
        label: t.tr('HeaderLocations'), help: t.tr('ProxyLocationsHelp'), field: 'Visitors.Location',
        options: [{ value: 'Country', label: t.tr('HeaderCountry') }, { value: 'City', label: t.tr('ProxyOptionCountryAndCity') }, { value: 'Off', label: t.tr('Off') }]
    });

    [status, enabled.element, terms.element].forEach(function (n) { content.appendChild(n); });
    content.appendChild(el('h3', { class: 'stolon-subsection-title', text: t.tr('HeaderVisitors') }));
    [history, days, location].forEach(function (f) { content.appendChild(f.element); });
    content.appendChild(locationLine);
    advancedSpecs().forEach(function (sub) {
        content.appendChild(el('h3', { class: 'stolon-subsection-title', text: t.tr(sub.title) }));
        sub.fields.forEach(function (spec) {
            var f;
            if (spec.kind === 'bool') f = fields.checkField({ label: t.tr(spec.label), help: t.tr(spec.help), field: spec.key });
            else if (spec.kind === 'lines') f = fields.textareaField({ label: t.tr(spec.label), help: t.tr(spec.help), field: spec.key, placeholder: spec.placeholder });
            else if (spec.kind === 'enum') f = fields.selectField({ label: t.tr(spec.label), help: t.tr(spec.help), field: spec.key, options: spec.names.map(function (n, i) { return { value: n, label: t.tr(spec.options[i]) }; }) });
            else if (spec.kind === 'countries') {
                countries = nectoModule.createChipSelect({
                    options: nectoModule.COUNTRY_CODES.map(function (code) { return { value: code, label: countryName(code) }; }),
                    value: [],
                    placeholder: t.tr('ProxySearchCountries'),
                    noResultsText: t.tr('ProxyEmptyCountriesMatch')
                });
                f = { element: el('div', { 'data-field': spec.key }, [el('div', { class: 'selectLabel', text: t.tr(spec.label) }), countries.element, fields.describe(t.tr(spec.help))]), input: null };
            } else f = fields.inputField({ label: t.tr(spec.label), help: t.tr(spec.help), field: spec.key, type: spec.kind === 'int' ? 'number' : 'text', min: spec.min, max: spec.max, placeholder: spec.placeholder });
            inputs[spec.key] = f;
            content.appendChild(f.element);
            if (spec.key === 'CountryMode') {
                content.appendChild(countryText);
                content.appendChild(refusedLine);
            }
        });
    });

    // showNotes
    // Shows the note for the picked country rule and location detail, with who was refused since start, and hides
    // the country picker while the rule is off.
    function showNotes() {
        var mode = inputs.CountryMode.input.value;
        var note = countryNote(mode, proxyStatus, !!inputs.BlockWithoutDatabase.input.checked, t);
        countryText.textContent = note.text;
        countryText.className = note.warn ? 'fieldDescription stolon-warn' : 'fieldDescription';
        refusedLine.textContent = proxyStatus ? refusedText(proxyStatus.CountryRefused, t) : '';
        refusedLine.classList.toggle('stolon-hidden', !proxyStatus || mode === 'Off');
        inputs.Countries.element.classList.toggle('stolon-hidden', mode === 'Off');
        locationLine.textContent = locationNote(location.input.value, t);
    }

    [inputs.CountryMode.input, inputs.BlockWithoutDatabase.input, location.input].forEach(function (input) {
        input.addEventListener('change', showNotes);
    });

    // fill
    // Shows the stored values, and the default where one is unset.
    function fill(settings) {
        stored = settings;
        var a = settings.Advanced || {};
        var values = (defaults && defaults.Values) || {};
        enabled.input.checked = !!settings.Enabled;
        terms.input.checked = !!settings.AcceptTerms;
        var v = settings.Visitors || {};
        history.input.checked = v.Enabled !== false;
        days.input.value = String(v.HistoryDays || 90);
        location.input.value = fields.enumName(v.Location, LOCATIONS);
        advancedSpecs().forEach(function (sub) {
            sub.fields.forEach(function (spec) {
                var f = inputs[spec.key];
                var value = a[spec.key];
                var auto = isAuto(defaults, spec.key);
                if (value == null && !auto) value = values[spec.key];
                if (spec.kind === 'bool') f.input.checked = !!value;
                else if (spec.kind === 'lines') f.input.value = (value || []).join('\n');
                else if (spec.kind === 'enum') f.input.value = fields.enumName(value, spec.names);
                else if (spec.kind === 'countries') countries.setValue((value || []).map(function (code) { return { value: code, label: countryName(code) }; }));
                else {
                    f.input.value = value == null ? '' : String(value);
                    if (auto) f.input.setAttribute('placeholder', t.tr('Auto'));
                }
            });
        });
        showNotes();
    }

    // send
    // Saves the history through its own route first, which loads the location database it needs and prunes, then
    // the rest through Proxy/Settings.
    function send(body) {
        return shared.request('Proxy/Visitors/Settings', 'POST', body.Visitors).then(function () {
            return shared.request('Proxy/Settings', 'POST', body);
        }).then(function (result) {
            if (result && result.Restart) shared.setStatus(status, result.Restart, false);
            if (result && result.Settings) fill(result.Settings);
            if (shared.onTabsChanged) shared.onTabsChanged();
        }, function (err) {
            return shared.errorText(err, t.tr('ProxyErrorSaveSettings')).then(function (text) { throw new Error(text); });
        });
    }

    return {
        load: function () {
            return Promise.all([
                shared.request('Proxy/Defaults'),
                shared.request('Proxy/Settings'),
                shared.request('Proxy/Status').catch(function () { return null; })
            ]).then(function (answers) {
                defaults = answers[0];
                proxyStatus = answers[2] || null;
                fill(answers[1] || {});
            }).catch(function (err) {
                return shared.errorText(err, t.tr('ProxyErrorLoadSettings')).then(function (text) { shared.setStatus(status, text, true); });
            });
        },
        apply: function () {},
        save: function () {
            var body = JSON.parse(JSON.stringify(stored || {}));
            body.Enabled = !!enabled.input.checked;
            body.AcceptTerms = !!terms.input.checked;
            body.Visitors = { Enabled: !!history.input.checked, HistoryDays: Math.max(1, fields.readNumber(days.input) || 90), Location: location.input.value };
            body.Advanced = readAdvanced(inputs, defaults, countries);
            body.Routes = [];
            if (!turnsOnLoopback(stored && stored.Advanced, inputs.JellyfinLoopbackOnly.input.checked, defaults)) return send(body);
            return nectoModule.confirmDialog(t.tr('ProxyConfirmLoopbackOnly'), { title: t.tr('ProxyTitleLoopbackOnly'), confirmText: t.tr('ButtonTurnOn') }).then(function (ok) {
                if (!ok) throw new Error(t.tr('ProxyErrorNothingSaved'));
                return send(body);
            });
        }
    };
}

// buildTroubleshooting
// Fills the Proxy section of Troubleshooting: the proxy's checks and restart, the tasks, the repairs, and how to
// turn the proxy off without the dashboard.
//
// Inputs
// content [HTMLElement]: The section's content.
// ctx [object]: shared is the Necto helper bag.
//
// Output
// [object]: A handle with load and destroy.
function buildTroubleshooting(content, ctx) {
    var shared = ctx.shared;
    var t = shared.t;
    var pollers = [];
    var status = el('div', { class: 'stolon-status-text', role: 'status', 'data-role': 'status' });
    var state = nectoModule.createDefinitionList({ items: [] });
    var reach = nectoModule.createDefinitionList({ items: [] });
    var recovery = el('div', { class: 'stolon-info' });
    [
        status,
        el('h3', { class: 'stolon-subsection-title', text: t.tr('ProxyHeaderState') }),
        state.element,
        el('h3', { class: 'stolon-subsection-title', text: t.tr('HeaderChecks') }),
        el('div', { class: 'stolon-reset-group' }, [
            nectoModule.resetRow({ icon: 'travel_explore', label: t.tr('ProxyTestOutside'), help: t.tr('ProxyTestOutsideHelp'), button: t.tr('ProxyButtonTest'), action: 'reach' }),
            nectoModule.resetRow({ icon: 'public', label: t.tr('ProxyTaskExternal'), help: t.tr('ProxyTaskExternalHelp'), button: t.tr('ButtonRunTask'), action: 'task-external' }),
            nectoModule.resetRow({ icon: 'verified_user', label: t.tr('ProxyTaskCertificates'), help: t.tr('ProxyTaskCertificatesHelp'), button: t.tr('ButtonRunTask'), action: 'task-certificates' }),
            nectoModule.resetRow({ icon: 'map', label: t.tr('ProxyTaskLocations'), help: t.tr('ProxyTaskLocationsHelp'), button: t.tr('ButtonRunTask'), action: 'task-locations' })
        ]),
        reach.element,
        el('h3', { class: 'stolon-subsection-title', text: t.tr('HeaderRepairs') }),
        el('div', { class: 'stolon-reset-group' }, [
            nectoModule.resetRow({ icon: 'restart_alt', label: t.tr('ProxyRestart'), help: t.tr('ProxyRestartHelp'), button: t.tr('Restart'), action: 'restart' }),
            nectoModule.resetRow({ icon: 'undo', label: t.tr('ProxyUndoTrust'), help: t.tr('ProxyUndoTrustHelp'), button: t.tr('ButtonUndo'), action: 'undo', destructive: true }),
            nectoModule.resetRow({ icon: 'settings_backup_restore', label: t.tr('ProxyResetDefaults'), help: t.tr('ProxyResetDefaultsHelp'), button: t.tr('Reset'), action: 'reset', destructive: true }),
            nectoModule.resetRow({ icon: 'delete_sweep', label: t.tr('ProxyClearHistory'), help: t.tr('ProxyClearHistoryHelp'), button: t.tr('ButtonClear'), action: 'clear', destructive: true })
        ]),
        el('h3', { class: 'stolon-subsection-title', text: t.tr('ProxyHeaderRecovery') }),
        recovery
    ].forEach(function (n) { content.appendChild(n); });

    // report
    // Shows a result on the section's status line.
    function report(message, isError) {
        shared.setStatus(status, message, !!isError, { timeout: 10000 });
    }

    // button
    // Finds one of the section's buttons by its action.
    function button(action) {
        return content.querySelector('[data-action="' + action + '"]');
    }

    // fail
    // Shows why a request failed.
    function fail(fallback) {
        return function (err) {
            return shared.errorText(err, t.tr(fallback)).then(function (text) { report(text, true); });
        };
    }

    // showReach
    // Shows the last check from outside.
    function showReach(result) {
        var items = [];
        if (result && result.CheckedUtc) {
            items.push({ label: t.tr('ProxyLabelChecked'), value: shared.formatDate(result.CheckedUtc) });
            items.push({ label: t.tr('ProxyLabelReachable'), value: result.Reachable === true ? t.tr('Yes') : result.Reachable === false ? t.tr('No') : t.tr('Unknown') });
            if (result.Error) items.push({ label: t.tr('LabelProblem'), value: result.Error });
            (result.Nodes || []).forEach(function (n) { items.push({ label: n.Location || t.tr('Unknown'), value: n.Ok ? t.tr('StateConnected') : n.Error }); });
        }
        reach.setItems(items);
    }

    // load
    // Reads the proxy's status and the defaults.
    function load() {
        return Promise.all([shared.request('Proxy/Status'), shared.request('Proxy/Defaults')]).then(function (answers) {
            var s = answers[0] || {};
            var d = answers[1] || {};
            var items = [
                { label: t.tr('TabProxy'), value: t.tr(s.Running ? 'StateRunning' : 'StateStopped') },
                { label: t.tr('ProxyLabelListening'), value: (s.Addresses || []).join(', ') || t.tr('None') },
                { label: t.tr('LabelJellyfin'), value: s.Backend },
                { label: t.tr('ProxyLabelJellyfinTrusts'), value: t.tr(s.JellyfinTrustsProxy ? 'Yes' : 'No') },
                { label: t.tr('ProxyLabelLan'), value: s.LanAddress || s.DetectedLanAddress || t.tr('Unknown') },
                { label: t.tr('ProxyLabelLocationDatabase'), value: t.tr(s.CountryDatabaseLoaded ? 'ProxyLoaded' : 'ProxyNotLoaded') },
                { label: t.tr('ProxyLabelServerCountry'), value: s.ServerCountry || t.tr('Unknown') },
                { label: t.tr('ProxyLabelMemory'), value: t.format(d.IsLowMemory ? 'ProxyMemoryLow' : 'ProxyMemoryStandard', d.MemoryMb) }
            ];
            items = items.concat(nameItems(s.Name, t));
            if (s.Error) items.push({ label: t.tr('LabelProblem'), value: s.Error });
            state.setItems(items);
            showReach(s.Reachability);
            recovery.textContent = t.format('ProxyRecoveryHelp', s.DisableFlagPath || 'DISABLE');
        }).catch(fail('ProxyErrorLoad'));
    }

    // runTask
    // Starts a scheduled task and follows it on its button.
    function runTask(action, key) {
        shared.runTask(button(action), key, t.tr('ButtonRunTask'), load).then(function (p) { pollers.push(p); }).catch(fail('ErrorRunTask'));
    }

    // confirmThen
    // Asks first, then runs a repair.
    function confirmThen(message, title, verb, run) {
        nectoModule.confirmDialog(t.tr(message), { title: t.tr(title), confirmText: t.tr(verb), destructive: true }).then(function (ok) { if (ok) run(); });
    }

    button('reach').addEventListener('click', function () {
        report(t.tr('ProxyTesting'), false);
        shared.request('Proxy/Reachability', 'POST').then(function (result) {
            showReach(result);
            report(result.Reachable ? t.tr('ProxyReachable') : (result.Error || t.tr('ProxyUnreachable')), !result.Reachable);
        }).catch(fail('ProxyErrorReach'));
    });
    button('task-external').addEventListener('click', function () { runTask('task-external', EXTERNAL_TASK); });
    button('task-certificates').addEventListener('click', function () { runTask('task-certificates', CERTIFICATE_TASK); });
    button('task-locations').addEventListener('click', function () { runTask('task-locations', LOCATION_TASK); });
    button('restart').addEventListener('click', function () {
        shared.request('Proxy/Restart', 'POST').then(function (s) {
            report(s.Running ? t.tr('ProxyRestarted') : (s.Error || t.tr('StateStopped')), !s.Running);
            load();
        }).catch(fail('ProxyErrorRestart'));
    });
    button('undo').addEventListener('click', function () {
        confirmThen('ProxyConfirmUndoTrust', 'ProxyUndoTrust', 'ButtonUndo', function () {
            shared.request('Proxy/Trust/Undo', 'POST').then(function (r) { report(r.Message || t.tr('ProxyDone'), !r.Success); load(); }).catch(fail('ProxyErrorTrust'));
        });
    });
    button('reset').addEventListener('click', function () {
        shared.request('Proxy/Settings').then(function (s) {
            var changes = resetChanges(s.Advanced);
            if (!changes.length) {
                report(t.tr('ProxyResetNothing'), false);
                return null;
            }
            var context = el('div', {}, [
                el('div', { class: 'fieldDescription', text: t.tr('ProxyResetChanges') }),
                el('ul', {}, changes.map(function (c) { return el('li', { text: t.tr(c.label) }); }))
            ]);
            return nectoModule.confirmDialog(t.tr('ProxyConfirmResetDefaults'), { title: t.tr('ProxyTitleResetDefaults'), confirmText: t.tr('Reset'), destructive: true, context: context }).then(function (ok) {
                if (!ok) return null;
                s.Advanced = resetAdvanced(s.Advanced);
                s.Routes = [];
                return shared.request('Proxy/Settings', 'POST', s).then(function () { report(t.tr('ProxyToastDefaultsReset'), false); load(); });
            });
        }).catch(fail('ProxyErrorSaveSettings'));
    });
    button('clear').addEventListener('click', function () {
        confirmThen('ProxyConfirmClearHistory', 'ProxyTitleClearHistory', 'ButtonClear', function () {
            shared.request('Proxy/Visitors/History', 'DELETE').then(function () { report(t.tr('ToastHistoryCleared'), false); }).catch(fail('ErrorClearHistory'));
        });
    });

    return {
        load: load,
        destroy: function () {
            pollers.splice(0).forEach(function (p) { if (p && p.cancel) p.cancel(); });
            state.destroy();
            reach.destroy();
        }
    };
}

nectoModule.registerSection('settings', 'proxy', { build: buildSettings });
nectoModule.registerSection('troubleshooting', 'proxy', { build: buildTroubleshooting });
