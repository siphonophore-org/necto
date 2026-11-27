// necto_proxy.js
// The Proxy tool. It shows how the proxy stands, lists its routes and opens each in an editor sidecar with its
// upstream, who may reach it, and its certificate, lets Jellyfin trust the proxy, and shows the visitors, bans and
// block list.

import * as nectoModule from './configurationpage?name=necto_shared.js';
import * as fields from './configurationpage?name=necto_netfields.js';

var h = nectoModule.h;

// The names of each choice field's values, in the order the server numbers them.
export var CERTIFICATE_MODES = ['None', 'Automatic', 'Pkcs12File', 'PemFiles', 'SelfSigned'];
export var CHALLENGES = ['Auto', 'Http', 'Dns'];
var PHASES = ['Unmanaged', 'PendingRestart', 'Managed', 'Degraded'];
var ACTIVITY_TYPES = ['User', 'Failure', 'Blocked', 'Seen'];
var ACTIVITY_KEYS = { User: 'ProxyActivityUser', Failure: 'ProxyActivityFailure', Blocked: 'HeaderRefused', Seen: 'ProxyActivitySeen' };
var LOCATIONS = ['Country', 'City', 'Off'];

// How often the page reads the proxy's status and the visitors again, in milliseconds.
export var STATUS_POLL_MS = 10000;
export var VISITORS_POLL_MS = 30000;

// The keys of the route templates, in the order the picker shows them.
export var TEMPLATES = [
    { key: 'jellyfin', title: 'LabelJellyfin', help: 'ProxyTemplateJellyfinHelp' },
    { key: 'blank', title: 'ProxyTemplateBlank', help: 'ProxyTemplateBlankHelp' }
];

// certificateTag
// Turns a route's certificate status into a tag for its row.
//
// Inputs
// status [object]: The route's status from Proxy/Routes, or null.
// route [object]: The route.
// t [object]: The strings.
//
// Output
// [object|null]: The tag.
export function certificateTag(status, route, t) {
    var mode = fields.enumName(route.Certificate && route.Certificate.Mode, CERTIFICATE_MODES);
    if (mode === 'None') return { label: t.tr('ProxyTagNoCertificate'), color: 'gray' };
    var cert = (status && status.Certificate) || {};
    var acme = (status && status.Acme) || {};
    if (acme.Issuing) return { label: t.tr('ProxyTagRequesting'), color: 'purple' };
    if (cert.Loaded) {
        var soon = cert.NotAfterUtc && (new Date(cert.NotAfterUtc) - new Date()) < 14 * 86400000;
        return soon ? { label: t.tr('ProxyTagExpiresSoon'), color: 'orange' } : { label: t.tr('ProxyTagCertificateReady'), color: 'green' };
    }
    if (cert.Error || acme.LastError) return { label: t.tr('ProxyTagCertificateFailed'), color: 'red' };
    return { label: t.tr('ProxyTagCertificateWaiting'), color: 'orange' };
}

// upstreamText
// Names where a route forwards to.
//
// Inputs
// route [object]: The route.
// t [object]: The strings.
//
// Output
// [string]: Jellyfin, or the upstream address.
export function upstreamText(route, t) {
    if (route.Upstream) return route.Upstream;
    return route.LocalHost ? t.format('ProxyJellyfinWithLocal', route.LocalHost) : t.tr('LabelJellyfin');
}

// routeState
// Gives the state a route's row ends with: Off while the route is off, else its certificate's state.
//
// Inputs
// route [object]: The route.
// status [object]: The route's status from Proxy/Routes, or null.
// t [object]: The strings.
//
// Output
// [object]: label and color.
export function routeState(route, status, t) {
    if (!route.Enabled) return { label: t.tr('Off'), color: 'gray' };
    return certificateTag(status, route, t);
}

// routeItems
// Turns the routes into rows for the record list, each with its upstream and who may reach it, and its state at its
// end.
//
// Inputs
// routes [object[]]: The routes from Proxy/Routes.
// statuses [object[]]: Each route's status, by id.
// t [object]: The strings.
//
// Output
// [object[]]: The rows, each carrying its route, status, and state.
export function routeItems(routes, statuses, t) {
    var byId = {};
    (statuses || []).forEach(function (s) { byId[s.Id] = s; });
    return (routes || []).map(function (route) {
        var status = byId[route.Id] || null;
        var tags = [];
        if (route.LocalOnly) tags.push({ label: t.tr('ProxyTagLocalOnly'), icon: 'home', color: 'blue' });
        var state = routeState(route, status, t);
        return { id: route.Id, title: route.Host || t.tr('ProxyNoHost'), secondary: upstreamText(route, t), tags: tags, state: state, aside: nectoModule.rowBadge(state), route: route, status: status };
    });
}

// certificateItems
// Describes a route's certificate as label and value pairs, with the self-signed root's expiry and fingerprint.
//
// Inputs
// status [object]: The route's status.
// shared [object]: The Necto helper bag.
// root [object]: The self-signed root from Proxy/Certificate/Root, or null for any other source.
//
// Output
// [object[]]: The pairs.
export function certificateItems(status, shared, root) {
    var t = shared.t;
    var cert = (status && status.Certificate) || {};
    var acme = (status && status.Acme) || {};
    var items = [];
    if (cert.Loaded) {
        items.push({ label: t.tr('ProxyLabelCovers'), value: (cert.Names || []).join(', ') });
        if (cert.NotAfterUtc) items.push({ label: t.tr('ProxyLabelExpires'), value: shared.formatDate(cert.NotAfterUtc) });
        items.push({ label: t.tr('ProxyLabelIssuedBy'), value: cert.Issuer });
        if ((cert.UncoveredHostnames || []).length) items.push({ label: t.tr('ProxyLabelNotCovered'), value: cert.UncoveredHostnames.join(', ') });
    }
    if (cert.Error) items.push({ label: t.tr('LabelProblem'), value: cert.Error });
    if (acme.LastError) items.push({ label: t.tr('ProxyLabelLastAttempt'), value: acme.LastError });
    if (acme.NextAttemptUtc) items.push({ label: t.tr('ProxyLabelNextAttempt'), value: shared.formatDate(acme.NextAttemptUtc) });
    if ((acme.DnsProviders || []).length) items.push({ label: t.tr('ProxyLabelDnsProvider'), value: acme.DnsProviders.join(', ') });
    if (root && root.NotAfterUtc) items.push({ label: t.tr('ProxyLabelRootExpires'), value: shared.formatDate(root.NotAfterUtc) });
    if (root && root.Fingerprint) items.push({ label: t.tr('ProxyLabelRootFingerprint'), value: root.Fingerprint });
    return items;
}

// newRoute
// Builds an empty route for another service.
//
// Output
// [object]: The route.
export function newRoute() {
    return { Id: '', Enabled: true, Host: '', DdnsHostname: '', Upstream: '', LocalOnly: false, Certificate: { Mode: 'Automatic', Challenge: 'Auto', DnsRecordId: '', Path: '', KeyPath: '', Password: '' } };
}

// PROFILES
// What each Jellyfin preset forwards with, after Jellyfin's reverse proxy guide, as RouteProfiles does on the server.
// A null upload limit or timeout takes the one in Settings.
export var PROFILES = {
    Recommended: { key: 'ProxyPresetRecommended', help: 'ProxyPresetRecommendedHelp', MaxBodyMb: null, TimeoutSeconds: null, MediaTimeoutMinutes: 360, BackendConnections: 256 },
    LowMemory: { key: 'ProxyPresetLowMemory', help: 'ProxyPresetLowMemoryHelp', MaxBodyMb: 20, TimeoutSeconds: 120, MediaTimeoutMinutes: 60, BackendConnections: 32 },
    HighBandwidth: { key: 'ProxyPresetHighBandwidth', help: 'ProxyPresetHighBandwidthHelp', MaxBodyMb: 2048, TimeoutSeconds: 900, MediaTimeoutMinutes: 720, BackendConnections: 512 },
    Custom: { key: 'Custom', help: 'ProxyPresetCustomHelp', MaxBodyMb: null, TimeoutSeconds: null, MediaTimeoutMinutes: 360, BackendConnections: 256 }
};

// PROFILE_NAMES
// The Jellyfin presets in the order the server numbers them.
var PROFILE_NAMES = ['Recommended', 'LowMemory', 'HighBandwidth', 'Custom'];

// defaultOptions
// Builds the forwarding settings of a new route to another service, the proxy's own behavior.
//
// Output
// [object]: The route's Options.
export function defaultOptions() {
    return {
        Profile: 'Recommended',
        WebSockets: true,
        Buffering: false,
        MaxBodyMb: null,
        TimeoutSeconds: null,
        MediaTimeoutMinutes: null,
        BackendConnections: null,
        PreserveHost: true,
        PathPrefix: '',
        RequestHeaders: [],
        ResponseHeaders: []
    };
}

// profileName
// Reads a stored Jellyfin preset as its name, from a name or the server's number.
//
// Inputs
// value [string|number]: The stored Profile.
//
// Output
// [string]: The preset's name, Recommended for anything unknown.
export function profileName(value) {
    if (typeof value === 'number') return PROFILE_NAMES[value] || 'Recommended';
    return PROFILES[value] ? value : 'Recommended';
}

// profileItems
// Lists what a Jellyfin preset forwards with, for the summary under the Preset select.
//
// Inputs
// t [object]: The strings helper.
// values [object]: The preset's MaxBodyMb, TimeoutSeconds, MediaTimeoutMinutes, and BackendConnections.
//
// Output
// [object[]]: Definition list items.
export function profileItems(t, values) {
    var body = values.MaxBodyMb == null ? t.tr('ProxyPresetUsesSettings') : values.MaxBodyMb === 0 ? t.tr('LabelNoLimit') : t.format('ProxyValueMb', values.MaxBodyMb);
    return [
        { label: t.tr('ProxyLabelRouteBodyLimit').replace(/ \(.*\)$/, ''), value: body },
        { label: t.tr('ProxyLabelRouteTimeout').replace(/ \(.*\)$/, ''), value: values.TimeoutSeconds == null ? t.tr('ProxyPresetUsesSettings') : t.format('ValueSeconds', values.TimeoutSeconds) },
        { label: t.tr('ProxyLabelMediaTimeout').replace(/ \(.*\)$/, ''), value: t.format('StorageDurationMinutes', values.MediaTimeoutMinutes) },
        { label: t.tr('ProxyLabelBackendConnections'), value: String(values.BackendConnections) }
    ];
}

// subdomainOf
// Reads the part of a host in front of a wildcard DDNS name's base, such as media from media.example.com under
// *.example.com.
//
// Inputs
// host [string]: The host.
// ddnsHostname [string]: The DDNS name, a wildcard or not.
//
// Output
// [string]: The subdomain, or empty when the host is not under the wildcard.
export function subdomainOf(host, ddnsHostname) {
    var name = String(host || '').toLowerCase();
    if (String(ddnsHostname || '').indexOf('*.') !== 0) return '';
    var suffix = ddnsHostname.slice(1);
    return name.length > suffix.length && name.slice(-suffix.length) === suffix ? name.slice(0, -suffix.length) : '';
}

// hostFor
// Builds the host a DDNS name gives: the name itself, or the subdomain in front of a wildcard's base.
//
// Inputs
// ddnsHostname [string]: The DDNS name.
// sub [string]: The subdomain typed for a wildcard.
//
// Output
// [string]: The host, empty while a wildcard has no subdomain.
export function hostFor(ddnsHostname, sub) {
    if (String(ddnsHostname || '').indexOf('*.') !== 0) return ddnsHostname || '';
    var label = String(sub || '').trim().toLowerCase().replace(/\.+$/, '');
    return label ? label + ddnsHostname.slice(1) : '';
}

// fitsDdnsName
// Tells whether a host fits a DDNS name, as the server checks: the name itself, or the wildcard or one name under it.
//
// Inputs
// host [string]: The host.
// ddnsHostname [string]: The DDNS name, or empty for none.
//
// Output
// [boolean]: Whether the host fits.
export function fitsDdnsName(host, ddnsHostname) {
    if (!ddnsHostname) return true;
    if (ddnsHostname.indexOf('*.') !== 0) return host === ddnsHostname;
    var label = subdomainOf(host, ddnsHostname);
    return label === '*' || /^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$/.test(label);
}

// createRouteEditor
// Builds the body of a route's sidecar: its host name, where it forwards to, whether only the local network may
// reach it, and its certificate with its status and actions.
//
// Inputs
// route [object]: The route to edit, or a new one.
// ctx [object]: shared is the Necto helper bag, status the route's status, records the DDNS records that can publish
//   TXT records, hostnames the names the DDNS records keep, requestable whether the proxy runs with the Let's Encrypt
//   terms accepted, root the self-signed root, and onAction(action, route) runs Request certificate, Test route, Upload
//   and Download root.
//
// Output
// [object]: A handle with element, getValue, validate, setStatus, setRouteStatus, setRoot, setRequestable, and destroy.
export function createRouteEditor(route, ctx) {
    var shared = ctx.shared;
    var t = shared.t;
    var draft = JSON.parse(JSON.stringify(route || newRoute()));
    draft.Certificate = draft.Certificate || {};
    var status = h('div', { class: 'stolon-status-text', role: 'status', 'data-role': 'status' });
    var root = h('div', { 'data-role': 'route-editor' }, [status]);
    var handles = [];

    var host = nectoModule.createInputField({ label: t.tr('LabelHostName'), field: 'Host', value: draft.Host, placeholder: 'media.example.com', help: t.tr('ProxyHostHelp') });
    var names = (ctx.hostnames || []).map(function (name) { return name.Hostname; });
    var lockedTo = draft.DdnsHostname || '';
    if (lockedTo && names.indexOf(lockedTo) < 0) names.push(lockedTo);
    var ddnsNames = nectoModule.createSelectField({
        label: t.tr('ProxyLabelDdnsName'),
        field: 'DdnsHostname',
        options: [{ value: '', text: t.tr('None') }].concat(names.map(function (name) { return { value: name, text: name }; })),
        value: lockedTo,
        help: t.tr('ProxyDdnsNameHelp')
    });
    var subdomain = nectoModule.createInputField({ label: t.tr('LabelHostName'), field: 'Subdomain', value: subdomainOf(draft.Host, lockedTo), placeholder: 'media', suffix: lockedTo.indexOf('*.') === 0 ? lockedTo.slice(1) : '', help: t.tr('ProxySubdomainHelp') });
    var enabled = nectoModule.createCheckboxField({ label: t.tr('ProxyLabelRouteEnabled'), field: 'Enabled', checked: draft.Enabled !== false, help: t.tr('ProxyRouteEnabledHelp') });
    var target = nectoModule.createSelectField({
        label: t.tr('ProxyLabelForwardTo'),
        field: 'Target',
        options: [{ value: 'jellyfin', text: t.tr('LabelJellyfin') }, { value: 'other', text: t.tr('ProxyOptionAnotherService') }],
        value: draft.Upstream ? 'other' : 'jellyfin'
    });
    var upstream = nectoModule.createInputField({ label: t.tr('ProxyLabelUpstream'), field: 'Upstream', value: draft.Upstream, placeholder: 'http://192.168.1.20:8096', help: t.tr('ProxyUpstreamHelp') });
    var localOnly = nectoModule.createCheckboxField({ label: t.tr('ProxyLabelLocalOnly'), field: 'LocalOnly', checked: !!draft.LocalOnly, help: t.tr('ProxyLocalOnlyHelp') });
    var localHost = nectoModule.createInputField({ label: t.tr('ProxyLabelLocalAddress'), field: 'LocalHost', value: draft.LocalHost || '', placeholder: 'home.example.com', help: t.tr('ProxyLocalAddressHelp') });

    // A route to Jellyfin forwards with a preset, and a route to another service with each setting shown as it is.
    var opts = Object.assign(defaultOptions(), draft.Options || {});
    opts.Profile = profileName(opts.Profile);
    var number = function (input) { var n = parseInt(input.value, 10); return input.value.trim() === '' || isNaN(n) ? null : n; };
    var text = function (n) { return n == null ? '' : String(n); };
    var profile = nectoModule.createChoiceGroup(null, {
        label: t.tr('ProxyLabelPreset'),
        value: opts.Profile,
        choices: PROFILE_NAMES.map(function (name) { return { value: name, name: t.tr(PROFILES[name].key), text: t.tr(PROFILES[name].help) }; })
    });
    profile.element.setAttribute('data-field', 'Options.Profile');
    handles.push(profile);
    var profileHelp = h('div', { class: 'fieldDescription', text: t.tr('ProxyPresetHelp') });
    var custom = opts.Profile === 'Custom' ? opts : PROFILES[opts.Profile];
    var jfBody = nectoModule.createInputField({ label: t.tr('ProxyLabelRouteBodyLimit'), field: 'Options.Jellyfin.MaxBodyMb', type: 'number', min: 0, value: text(custom.MaxBodyMb), help: t.tr('ProxyRouteBodyLimitHelp') });
    var jfTimeout = nectoModule.createInputField({ label: t.tr('ProxyLabelRouteTimeout'), field: 'Options.Jellyfin.TimeoutSeconds', type: 'number', min: 1, max: 86400, value: text(custom.TimeoutSeconds), help: t.tr('ProxyRouteTimeoutHelp') });
    var jfMedia = nectoModule.createInputField({ label: t.tr('ProxyLabelMediaTimeout'), field: 'Options.Jellyfin.MediaTimeoutMinutes', type: 'number', min: 1, max: 1440, value: text(custom.MediaTimeoutMinutes), help: t.tr('ProxyMediaTimeoutHelp') });
    var jfConnections = nectoModule.createInputField({ label: t.tr('ProxyLabelBackendConnections'), field: 'Options.Jellyfin.BackendConnections', type: 'number', min: 1, max: 4096, value: text(custom.BackendConnections), help: t.tr('ProxyBackendConnectionsHelp') });
    var summary = nectoModule.createDefinitionList({ items: [] });
    handles.push(summary);
    summary.element.setAttribute('data-role', 'profile-summary');
    var customBody = h('div', { class: 'stolon-stack', 'data-role': 'profile-custom' }, [jfBody.element, jfTimeout.element, jfMedia.element, jfConnections.element]);
    var jellyfinPart = h('div', { class: 'stolon-stack', 'data-role': 'jellyfin-forwarding' }, [profileHelp, profile.element, summary.element, customBody]);

    var webSockets = nectoModule.createCheckboxField({ label: t.tr('ProxyLabelWebSockets'), field: 'Options.WebSockets', checked: opts.WebSockets !== false, help: t.tr('ProxyWebSocketsHelp') });
    var buffering = nectoModule.createCheckboxField({ label: t.tr('ProxyLabelBuffering'), field: 'Options.Buffering', checked: !!opts.Buffering, help: t.tr('ProxyBufferingHelp') });
    var bodyLimit = nectoModule.createInputField({ label: t.tr('ProxyLabelRouteBodyLimit'), field: 'Options.MaxBodyMb', type: 'number', min: 0, value: text(opts.MaxBodyMb), help: t.tr('ProxyRouteBodyLimitHelp') });
    var timeout = nectoModule.createInputField({ label: t.tr('ProxyLabelRouteTimeout'), field: 'Options.TimeoutSeconds', type: 'number', min: 1, max: 86400, value: text(opts.TimeoutSeconds), help: t.tr('ProxyRouteTimeoutHelp') });
    var preserveHost = nectoModule.createCheckboxField({ label: t.tr('ProxyLabelPreserveHost'), field: 'Options.PreserveHost', checked: opts.PreserveHost !== false, help: t.tr('ProxyPreserveHostHelp') });
    var pathPrefix = nectoModule.createInputField({ label: t.tr('ProxyLabelPathPrefix'), field: 'Options.PathPrefix', value: opts.PathPrefix || '', placeholder: '/radarr', help: t.tr('ProxyPathPrefixHelp') });
    var headerEditor = function (values) {
        return nectoModule.createKeyValueEditor({
            values: (values || []).map(function (x) { return { key: x.Name, value: x.Value }; }),
            keyLabel: t.tr('LabelName'), valueLabel: t.tr('LabelValue'),
            keyPlaceholder: 'X-Example', addText: t.tr('ProxyButtonAddHeader')
        });
    };
    var requestHeaders = headerEditor(opts.RequestHeaders);
    var responseHeaders = headerEditor(opts.ResponseHeaders);
    handles.push(requestHeaders, responseHeaders);
    var headerBlock = function (label, help, editor, role) {
        return h('div', { class: 'inputContainer', 'data-role': role }, [
            h('div', { class: 'inputLabel', text: label }),
            editor.element,
            h('div', { class: 'fieldDescription', text: help })
        ]);
    };
    var otherPart = h('div', { class: 'stolon-stack', 'data-role': 'service-forwarding' }, [
        webSockets.element, buffering.element, bodyLimit.element, timeout.element, preserveHost.element, pathPrefix.element,
        headerBlock(t.tr('ProxyLabelRequestHeaders'), t.tr('ProxyRequestHeadersHelp'), requestHeaders, 'request-headers'),
        headerBlock(t.tr('ProxyLabelResponseHeaders'), t.tr('ProxyResponseHeadersHelp'), responseHeaders, 'response-headers')
    ]);
    var forwardingPart = h('div', { class: 'stolon-stack' }, [jellyfinPart, otherPart]);

    // showProfile
    // Shows what the picked preset forwards with, or the fields to set under Custom.
    function showProfile() {
        var name = profile.get();
        var isCustom = name === 'Custom';
        customBody.classList.toggle('stolon-hidden', !isCustom);
        summary.element.classList.toggle('stolon-hidden', isCustom);
        if (!isCustom) summary.setItems(profileItems(t, PROFILES[name]));
    }

    // Custom starts from the preset picked before it, so its fields begin from working values.
    var lastProfile = opts.Profile;
    profile.element.addEventListener('change', function () {
        if (profile.get() === 'Custom' && lastProfile !== 'Custom') {
            var from = PROFILES[lastProfile];
            jfBody.input.value = text(from.MaxBodyMb);
            jfTimeout.input.value = text(from.TimeoutSeconds);
            jfMedia.input.value = text(from.MediaTimeoutMinutes);
            jfConnections.input.value = text(from.BackendConnections);
        }
        lastProfile = profile.get();
        showProfile();
    });
    showProfile();

    // readOptions
    // Reads the forwarding settings as the server keeps them, for a route to Jellyfin or to another service.
    //
    // Inputs
    // jellyfin [boolean]: Whether the route forwards to Jellyfin.
    function readOptions(jellyfin) {
        var headers = function (editor) { return editor.getValue().filter(function (x) { return x.key; }).map(function (x) { return { Name: String(x.key).trim(), Value: String(x.value || '').trim() }; }); };
        if (jellyfin) {
            var name = profile.get();
            var p = name === 'Custom'
                ? { MaxBodyMb: number(jfBody.input), TimeoutSeconds: number(jfTimeout.input), MediaTimeoutMinutes: number(jfMedia.input), BackendConnections: number(jfConnections.input) }
                : PROFILES[name];
            return Object.assign(defaultOptions(), {
                Profile: name,
                MaxBodyMb: p.MaxBodyMb,
                TimeoutSeconds: p.TimeoutSeconds,
                MediaTimeoutMinutes: p.MediaTimeoutMinutes,
                BackendConnections: p.BackendConnections
            });
        }
        return {
            Profile: profile.get(),
            WebSockets: !!webSockets.input.checked,
            Buffering: !!buffering.input.checked,
            MaxBodyMb: number(bodyLimit.input),
            TimeoutSeconds: number(timeout.input),
            MediaTimeoutMinutes: null,
            BackendConnections: null,
            PreserveHost: !!preserveHost.input.checked,
            PathPrefix: pathPrefix.input.value.trim(),
            RequestHeaders: headers(requestHeaders),
            ResponseHeaders: headers(responseHeaders)
        };
    }
    var mode = nectoModule.createSelectField({
        label: t.tr('LabelSource'),
        field: 'Certificate.Mode',
        options: [
            { value: 'Automatic', text: t.tr('ProxyOptionLetsEncrypt') },
            { value: 'Pkcs12File', text: t.tr('ProxyOptionPkcs12File') },
            { value: 'PemFiles', text: t.tr('ProxyOptionPemFiles') },
            { value: 'SelfSigned', text: t.tr('ProxyOptionSelfSigned') },
            { value: 'None', text: t.tr('ProxyOptionNoCertificate') }
        ],
        value: fields.enumName(draft.Certificate.Mode, CERTIFICATE_MODES)
    });
    var challenge = nectoModule.createSelectField({
        label: t.tr('ProxyLabelChallenge'),
        field: 'Certificate.Challenge',
        options: [
            { value: 'Auto', text: t.tr('ProxyOptionChallengeAuto') },
            { value: 'Http', text: t.tr('ProxyOptionChallengeHttp') },
            { value: 'Dns', text: t.tr('ProxyOptionChallengeDns') }
        ],
        value: fields.enumName(draft.Certificate.Challenge, CHALLENGES)
    });
    var wildcard = nectoModule.createCheckboxField({ label: t.tr('ProxyLabelWildcard'), field: 'Certificate.Wildcard', checked: !!draft.Certificate.Wildcard, help: t.format('ProxyWildcardHelp', (draft.Host || '').replace(/^\*\./, '') || 'example.com') });
    var sharedNote = h('div', { class: 'fieldDescription', 'data-role': 'shared-certificate' });
    var record = nectoModule.createSelectField({
        label: t.tr('ProxyLabelDnsRecord'),
        field: 'Certificate.DnsRecordId',
        options: [{ value: '', text: t.tr('ProxyOptionRecordByZone') }].concat((ctx.records || []).map(function (r) { return { value: r.Id, text: r.Name || r.Hostname || r.Zone || r.Id }; })),
        value: draft.Certificate.DnsRecordId || '',
        help: t.tr('ProxyDnsRecordHelp')
    });
    var path = nectoModule.createInputField({ label: t.tr('ProxyLabelCertificateFile'), field: 'Certificate.Path', value: draft.Certificate.Path, placeholder: '/etc/ssl/media.example.com.pfx', help: t.tr('ProxyCertificateFileHelp') });
    var keyPath = nectoModule.createInputField({ label: t.tr('ProxyLabelPrivateKeyFile'), field: 'Certificate.KeyPath', value: draft.Certificate.KeyPath, placeholder: '/etc/ssl/media.example.com.key' });
    var password = nectoModule.createSecretField({ label: t.tr('LabelPassword'), field: 'Certificate.Password', help: t.tr('ProxyCertificatePasswordHelp'), stored: !!draft.Certificate.Password, clearText: t.tr('ButtonClearSaved') });

    var upload = h('div', { 'data-role': 'upload' });
    var uploadCert = h('input', { type: 'file', 'data-field': 'UploadCertificate', accept: '.pfx,.p12,.pem,.crt,.cer' });
    var uploadKey = h('input', { type: 'file', 'data-field': 'UploadKey', accept: '.pem,.key' });
    var uploadPassword = nectoModule.createInputField({ label: t.tr('LabelCertificatePassword'), field: 'UploadPassword', type: 'password' });
    [
        h('h3', { class: 'stolon-subsection-title', text: t.tr('ProxyHeaderUpload') }),
        h('div', { class: 'fieldDescription', text: t.tr('ProxyUploadHelp') }),
        h('label', { class: 'selectLabel', text: t.tr('ProxyLabelUploadFile') }), uploadCert,
        h('label', { class: 'selectLabel', text: t.tr('LabelPrivateKey') }), uploadKey,
        uploadPassword.element
    ].forEach(function (n) { upload.appendChild(n); });

    var certStatus = h('div', { 'data-role': 'certificate-status' });
    var certList = nectoModule.createDefinitionList({ items: [] });
    certStatus.appendChild(certList.element);
    handles.push(certList);
    var actions = h('div', { class: 'stolon-button-row' });
    var actionButton = function (action, text) {
        var btn = h('button', { is: 'emby-button', type: 'button', class: 'raised stolon-button stolon-secondary', 'data-action': action }, [h('span', { text: text })]);
        btn.addEventListener('click', function () { if (ctx.onAction) ctx.onAction(action, draft, btn, handle); });
        actions.appendChild(btn);
        return btn;
    };
    var uploadBtn = actionButton('upload', t.tr('Upload'));
    var requestBtn = actionButton('request', t.tr('ProxyButtonRequestCertificate'));
    var rootCert = ctx.root || null;
    var lastStatus = ctx.status;
    var rootBtn = actionButton('root', t.tr('ProxyButtonDownloadRoot'));
    var testBtn = actionButton('test', t.tr('ProxyButtonTestRoute'));
    upload.appendChild(h('div', { class: 'stolon-button-row' }, [uploadBtn]));

    // The route and its certificate are the editor's two steps, each led by its tile.
    // The DDNS name decides the host, so it comes first.
    var routePart = h('div', { class: 'stolon-stack' });
    if (names.length) routePart.appendChild(ddnsNames.element);
    routePart.appendChild(subdomain.element);
    routePart.appendChild(host.element);
    [enabled, target, upstream, localHost, localOnly].forEach(function (f) { routePart.appendChild(f.element); });
    var forwarding = nectoModule.createCollapsibleSection({ title: t.tr('ProxyHeaderForwarding'), icon: 'tune', collapsible: false, content: forwardingPart });
    forwarding.element.setAttribute('data-section', 'forwarding');
    var certPart = h('div', { class: 'stolon-stack' });
    certPart.appendChild(sharedNote);
    [mode, wildcard, challenge, record, path, keyPath, password].forEach(function (f) { certPart.appendChild(f.element); });
    [certStatus, upload, actions].forEach(function (n) { certPart.appendChild(n); });
    [
        nectoModule.createCollapsibleSection({ title: t.tr('PagesRowRoute'), icon: 'alt_route', collapsible: false, content: routePart }),
        forwarding,
        nectoModule.createCollapsibleSection({ title: t.tr('ProxyLabelUploadFile'), icon: 'verified', collapsible: false, content: certPart })
    ].forEach(function (part) {
        handles.push(part);
        root.appendChild(part.element);
    });

    // reflect
    // Shows only what the picked choices use.
    function reflect() {
        var m = mode.input.value;
        var other = target.input.value === 'other';
        upstream.element.classList.toggle('stolon-hidden', !other);
        localHost.element.classList.toggle('stolon-hidden', other);
        // A route under another route's wildcard uses that certificate, so its own source is hidden.
        var shared = !!(lastStatus && lastStatus.SharedFrom);
        sharedNote.textContent = shared ? t.format('ProxyCertificateShared', lastStatus.SharedFrom) : '';
        sharedNote.classList.toggle('stolon-hidden', !shared);
        [mode, wildcard, challenge, record, path, keyPath, password].forEach(function (f) { f.element.classList.toggle('stolon-hidden', shared); });
        wildcard.element.classList.toggle('stolon-hidden', shared || m !== 'Automatic');
        var domain = host.input.value.trim().toLowerCase().replace(/^\*\./, '') || 'example.com';
        if (wildcard.help) wildcard.help.textContent = t.format('ProxyWildcardHelp', domain);
        jellyfinPart.classList.toggle('stolon-hidden', other);
        otherPart.classList.toggle('stolon-hidden', !other);
        challenge.element.classList.toggle('stolon-hidden', shared || m !== 'Automatic');
        record.element.classList.toggle('stolon-hidden', shared || m !== 'Automatic' || challenge.input.value === 'Http');
        path.element.classList.toggle('stolon-hidden', shared || (m !== 'Pkcs12File' && m !== 'PemFiles'));
        keyPath.element.classList.toggle('stolon-hidden', shared || m !== 'PemFiles');
        password.element.classList.toggle('stolon-hidden', shared || (m !== 'Pkcs12File' && m !== 'PemFiles'));
        upload.classList.toggle('stolon-hidden', !draft.Id || (m !== 'Pkcs12File' && m !== 'PemFiles'));
        requestBtn.classList.toggle('stolon-hidden', !draft.Id || m !== 'Automatic');
        rootBtn.classList.toggle('stolon-hidden', !draft.Id || m !== 'SelfSigned');
        testBtn.classList.toggle('stolon-hidden', !draft.Id);
        path.element.querySelector('label').textContent = t.tr(m === 'PemFiles' ? 'ProxyLabelChainFile' : 'ProxyLabelCertificateFile');
    }

    // lockHost
    // Locks the host name to the picked DDNS name. A wildcard takes a subdomain in front of its base instead, and None
    // frees the field.
    function lockHost() {
        var picked = ddnsNames.input.value;
        var wild = picked.indexOf('*.') === 0;
        // A wildcard name takes one field, the subdomain with the base shown after it, in place of the host name.
        subdomain.element.classList.toggle('stolon-hidden', !wild);
        host.element.classList.toggle('stolon-hidden', wild);
        host.input.readOnly = !!picked;
        if (wild) {
            subdomain.setSuffix(picked.slice(1));
            host.input.value = hostFor(picked, subdomain.input.value);
        } else if (picked) {
            host.input.value = picked;
        }
    }

    ddnsNames.input.addEventListener('change', function () {
        if (ddnsNames.input.value.indexOf('*.') === 0) subdomain.input.value = subdomainOf(host.input.value, ddnsNames.input.value);
        lockHost();
    });
    subdomain.input.addEventListener('input', lockHost);
    host.input.addEventListener('input', function () { reflect(); });
    lockHost();
    [mode, challenge, target].forEach(function (f) { f.input.addEventListener('change', reflect); });
    mode.input.addEventListener('change', function () { if (handle) handle.setRouteStatus(lastStatus); });
    reflect();

    var handle = {
        element: root,
        uploadInputs: { certificate: uploadCert, key: uploadKey, password: uploadPassword.input },
        getValue: function () {
            var out = JSON.parse(JSON.stringify(draft));
            var m = mode.input.value;
            out.DdnsHostname = ddnsNames.input.value;
            out.Host = (out.DdnsHostname ? hostFor(out.DdnsHostname, subdomain.input.value) : host.input.value).trim().toLowerCase();
            out.Enabled = !!enabled.input.checked;
            out.Upstream = target.input.value === 'other' ? upstream.input.value.trim() : '';
            out.LocalOnly = !!localOnly.input.checked;
            out.LocalHost = target.input.value === 'other' ? '' : localHost.input.value.trim().toLowerCase();
            out.Options = readOptions(target.input.value !== 'other');
            out.Certificate = {
                Mode: m,
                Challenge: challenge.input.value,
                Wildcard: m === 'Automatic' && !!wildcard.input.checked,
                DnsRecordId: record.input.value,
                Path: m === 'Pkcs12File' || m === 'PemFiles' ? path.input.value.trim() : '',
                KeyPath: m === 'PemFiles' ? keyPath.input.value.trim() : '',
                Password: m === 'Pkcs12File' || m === 'PemFiles' ? password.getValue() : ''
            };
            return out;
        },
        validate: function () {
            var value = this.getValue();
            if (!value.Host) return 'ErrorEnterHostName';
            if (value.DdnsHostname.indexOf('*.') === 0 && !fitsDdnsName(value.Host, value.DdnsHostname)) return 'ProxyErrorEnterSubdomain';
            if (target.input.value === 'other' && !/^https?:\/\/[^/]+\/?$/i.test(value.Upstream)) return 'ProxyErrorEnterUpstream';
            if (target.input.value === 'other' && (!requestHeaders.isValid() || !responseHeaders.isValid())) return 'ProxyErrorEnterHeaders';
            if (value.Certificate.Mode === 'Automatic' && value.Certificate.Challenge === 'Http' && (value.LocalOnly || value.Host.indexOf('*') === 0)) return 'ProxyErrorHttpChallengeNeedsInternet';
            return null;
        },
        setStatus: function (message, isError) {
            shared.setStatus(status, message, !!isError);
        },
        setRouteStatus: function (next) {
            lastStatus = next;
            reflect();
            var shownRoot = mode.input.value === 'SelfSigned' ? rootCert : null;
            var items = certificateItems(next, shared, shownRoot);
            certList.setItems(items);
            certStatus.classList.toggle('stolon-hidden', !items.length);
        },
        setRoot: function (next) {
            rootCert = next || null;
            this.setRouteStatus(lastStatus);
        },
        setRequestable: function (on) {
            requestBtn.disabled = !on;
            if (on) requestBtn.removeAttribute('title');
            else requestBtn.setAttribute('title', t.tr('ProxyRequestNeedsProxy'));
        },
        destroy: function () {
            handles.splice(0).forEach(function (h) { if (h && h.destroy) h.destroy(); });
        }
    };
    handle.setRouteStatus(ctx.status);
    handle.setRequestable(ctx.requestable !== false);
    return handle;
}

// phaseText
// Says where trusting the proxy stands.
//
// Inputs
// status [object]: The proxy's status.
// t [object]: The strings.
//
// Output
// [object]: text is the sentence and state the badge state.
export function phaseText(status, t) {
    var phase = fields.enumName(status && status.TakeoverPhase, PHASES);
    if (phase === 'Managed') return { text: t.tr('ProxyTrustOn'), label: t.tr('ProxyBadgeTrusted'), color: 'green' };
    if (phase === 'PendingRestart') return { text: t.tr('ProxyTrustPending'), label: t.tr('ProxyBadgePending'), color: 'purple' };
    return { text: t.tr('ProxyTrustOff'), label: t.tr('ProxyBadgeNotTrusted'), color: 'gray' };
}

// statusCards
// Sums the proxy's state for the cards at the top of the page.
//
// Inputs
// status [object]: The proxy's status.
// routes [object[]]: The routes.
// t [object]: The strings.
//
// Output
// [object[]]: The cards.
export function statusCards(status, routes, t) {
    var active = (status.Routes || []).filter(function (r) { return r.Active; }).length;
    var name = status.Name || {};
    return [
        { count: t.tr(status.Running ? 'StateRunning' : 'StateStopped'), label: t.tr('TabProxy'), color: status.Running ? 'green' : (status.Enabled ? 'red' : 'gray') },
        { count: active + ' / ' + (routes || []).length, label: t.tr('ProxyCardRoutes'), color: 'blue' },
        { count: phaseText(status, t).label, label: t.tr('LabelJellyfin'), color: phaseText(status, t).color },
        { count: name.Hostname ? t.tr(name.Resolves ? 'ProxyCardNameOk' : 'ProxyCardNameWrong') : t.tr('ProxyCardNameNone'), label: t.tr('ProxyCardPublicName'), color: name.Hostname ? (name.Resolves ? 'green' : 'orange') : 'gray' }
    ];
}

// notices
// Lists what the administrator must know about the proxy, one sentence each, with its kind.
//
// Inputs
// status [object]: The proxy's status.
// t [object]: The strings.
//
// Output
// [object[]]: Each notice's class and text.
export function notices(status, t) {
    var out = [];
    if (status.DisableFlagPresent) out.push({ cls: 'stolon-warn', text: t.format('ProxyDisableFlagNotice', status.DisableFlagPath) });
    if (status.Error) out.push({ cls: 'stolon-error', text: status.Error });
    if (status.FolderProblem) out.push({ cls: 'stolon-error', text: status.FolderProblem });
    if (status.EveryoneLooksLocal) out.push({ cls: 'stolon-warn', text: t.tr('ProxyEveryoneLocalNotice') });
    if (status.JellyfinExposedAt) out.push({ cls: 'stolon-warn', text: t.format('ProxyExposedNotice', status.JellyfinExposedAt) });
    if (status.TakeoverError) out.push({ cls: 'stolon-error', text: status.TakeoverError });
    if (status.Name && status.Name.Hostname && status.Name.Error) out.push({ cls: 'stolon-warn', text: status.Name.Error });
    return out;
}

// rowAction
// Picks the one action an activity row offers: unban a banned address, unblock a listed one, let a country through,
// or block it. Your network is never blocked, so its Block is off, and an address blocked by a range has its Unblock
// off, each with the reason.
//
// Inputs
// row [object]: The activity row.
//
// Output
// [object]: action and value, disabled when the button is off, and reason, the key of the button's tooltip.
export function rowAction(row) {
    if (row.Banned) return { action: 'unban', value: row.Ip };
    if (row.Exempt) return { action: 'block', value: row.Ip, disabled: true, reason: 'ProxyTextNeverBlocked' };
    if (row.Listed) return { action: 'unblock', value: row.Ip };
    if (row.Covered) return { action: 'unblock', value: row.Ip, disabled: true, reason: 'ProxyBlockedByRange' };
    if (row.CountryBlocked) return { action: 'unblock-country', value: row.CountryCode, country: row.Country || row.CountryCode, reason: 'ProxyAllowCountry' };
    return { action: 'block', value: row.Ip };
}

// countryRows
// Joins the totals by country into one row per country, with the latest visit any of them saw.
//
// Inputs
// data [object]: The answer from Proxy/Visitors.
//
// Output
// [object[]]: One row per country.
export function countryRows(data) {
    var rows = {};
    var add = function (list, field) {
        (list || []).forEach(function (c) {
            var row = rows[c.CountryCode] || (rows[c.CountryCode] = { Code: c.CountryCode, Country: c.Country || c.CountryCode, People: 0, Failed: 0, Blocked: 0, LastUtc: null });
            row[field] += c.Count || 0;
            if (c.LastUtc && (!row.LastUtc || new Date(c.LastUtc) > new Date(row.LastUtc))) row.LastUtc = c.LastUtc;
        });
    };
    add(data.PeopleByCountry, 'People');
    add(data.FailedByCountry, 'Failed');
    add(data.BlockedByCountry, 'Blocked');
    return Object.keys(rows).map(function (k) { return rows[k]; });
}

// locationText
// Says how the location database stands, or that lookups are off.
//
// Inputs
// loc [object]: The location database status from Proxy/Visitors, or null.
// shared [object]: The Necto helper bag.
//
// Output
// [string]: The sentence, or empty when there is no status.
export function locationText(loc, shared) {
    var t = shared.t;
    if (!loc) return '';
    if (fields.enumName(loc.Detail, LOCATIONS) === 'Off') return t.tr('ProxyLocationOff');
    if (loc.Downloading) return t.tr('ProxyLocationDownloading');
    if (loc.Error) return loc.Error;
    if (loc.Loaded) return loc.BuiltUtc ? t.format('ProxyLocationLoadedOn', shared.formatDate(loc.BuiltUtc)) : t.tr('ProxyLoaded');
    return t.tr('ProxyNotLoaded');
}

// showsCity
// Tells whether the visitor history looks up cities, so the activity table shows them.
//
// Inputs
// data [object]: The answer from Proxy/Visitors.
//
// Output
// [boolean]: Whether Locations is set to City.
export function showsCity(data) {
    return fields.enumName(data && data.Settings && data.Settings.Location, LOCATIONS) === 'City';
}

export default function (view) {
    'use strict';

    var shared = null;
    var page = null;
    var list = null;
    var routes = [];
    var statuses = [];
    var records = [];
    var hostnames = [];
    var proxyStatus = {};
    var visitors = null;
    var loadSeq = 0;
    var refreshSeq = 0;
    var acceptTerms = false;
    var jellyfin = null;
    var openEditor = null;

    // report
    // Shows a result on the page's status line.
    function report(message, isError) {
        shared.setStatus('proxyStatus', message, !!isError, { timeout: 10000 });
    }

    // showStatus
    // Draws the notices, the cards, and the Jellyfin section from the proxy's status.
    function showStatus(status) {
        var t = shared.t;
        proxyStatus = status || {};
        var box = view.querySelector('#proxyNotices');
        box.textContent = '';
        var list = notices(proxyStatus, t);
        list.forEach(function (n) {
            box.appendChild(nectoModule.createBanner({ kind: n.cls.replace('stolon-', ''), text: n.text, dismissible: false }).element);
        });
        shared.setVisible(box, list.length > 0);
        shared.renderCards('proxyCards', statusCards(proxyStatus, routes, t));
        drawJellyfin();
        showOff(!proxyStatus.Enabled);
    }

    // showOff
    // Shows the off state while the proxy is off: one line pointing to Settings in place of the cards, Add route,
    // Jellyfin, and Visitors. Routes already added stay listed. The heading names the tool itself, since a tool that
    // is off leaves the sidebar the heading takes its name from.
    function showOff(off) {
        var t = shared.t;
        if (shared.setPageTitle) shared.setPageTitle(off ? t.tr('TabProxy') : '');
        var box = view.querySelector('#proxyOff');
        box.innerHTML = off ? shared.emptySection(t.tr('ProxyOffNotice')) : '';
        shared.setVisible(box, off);
        shared.setVisible('proxyCards', !off);
        shared.setVisible('proxyActions', !off);
        shared.setVisible('proxyRoutes', !off || routes.length > 0);
        if (jellyfin) shared.setVisible(jellyfin.section.element, !off);
        if (visitors) shared.setVisible(visitors.element, !off);
    }

    // drawJellyfin
    // Draws whether Jellyfin trusts the proxy, as the section's badge, with Trust as an action row and Undo under it
    // once Jellyfin trusts it. The section is built once and only its state changes after, so a refresh never folds it
    // or clears its status line.
    function drawJellyfin() {
        var t = shared.t;
        var phase = phaseText(proxyStatus, t);
        var managed = phase.color !== 'gray';
        if (!jellyfin) {
            var status = h('div', { class: 'stolon-status-text', role: 'status', 'data-role': 'trust-status' });
            var trustRow = nectoModule.createResetRow({ icon: 'verified_user', label: t.tr('ProxyButtonTrust'), help: t.tr('ProxyTrustHelp'), button: t.tr('ProxyButtonTrustShort'), action: 'trust' });
            var undoRow = nectoModule.createResetRow({ icon: 'undo', label: t.tr('ProxyUndoTrust'), help: t.tr('ProxyUndoTrustHelp'), button: t.tr('ButtonUndo'), action: 'undo', destructive: true });
            var trust = trustRow.button;
            var undo = undoRow.button;
            var content = h('div', { class: 'stolon-stack' }, [status, nectoModule.createResetGroup([trustRow, undoRow]).element]);
            var section = nectoModule.createCollapsibleSection({ title: t.tr('LabelJellyfin'), icon: 'shield', collapsed: managed, content: content });
            // The section takes its holder's place, so the page's column spaces it like its neighbors.
            section.element.id = 'proxyJellyfin';
            var holderJellyfin = view.querySelector('#proxyJellyfin');
            holderJellyfin.parentNode.replaceChild(section.element, holderJellyfin);
            jellyfin = { section: section, trust: trust, undo: undo, undoRow: undoRow, status: status, busy: false };
            trust.addEventListener('click', function () {
                nectoModule.confirmDialog(t.tr('ProxyConfirmTrust'), { title: t.tr('ProxyButtonTrust'), confirmText: t.tr('ProxyButtonTrustShort') }).then(function (ok) {
                    if (ok) runTrust('Proxy/Trust', trust, status);
                });
            });
            undo.addEventListener('click', function () {
                nectoModule.confirmDialog(t.tr('ProxyConfirmUndoTrust'), { title: t.tr('ProxyUndoTrust'), confirmText: t.tr('ButtonUndo'), destructive: true }).then(function (ok) {
                    if (ok) runTrust('Proxy/Trust/Undo', undo, status);
                });
            });
        }
        jellyfin.section.setBadge({ label: phase.label, color: phase.color });
        (jellyfin.trust.querySelector('span') || jellyfin.trust).textContent = t.tr(managed ? 'ProxyButtonTrustAgain' : 'ProxyButtonTrustShort');
        // Undo only means something once Jellyfin trusts the proxy, so its row shows only then.
        shared.setVisible(jellyfin.undoRow.element, managed);
        if (!jellyfin.busy) jellyfin.undo.disabled = !managed;
    }

    // runTrust
    // Trusts the proxy in Jellyfin, or undoes it, and says what happened.
    function runTrust(route, btn, status) {
        jellyfin.busy = true;
        btn.disabled = true;
        shared.setStatus(status, shared.t.tr('TextWorking'), false);
        return shared.request(route, 'POST').then(function (result) {
            shared.setStatus(status, (result && result.Message) || shared.t.tr('ProxyDone'), !(result && result.Success));
            loadAll();
        }).catch(function (err) {
            return shared.errorText(err, shared.t.tr('ProxyErrorTrust')).then(function (text) { shared.setStatus(status, text, true); });
        }).then(function () {
            jellyfin.busy = false;
            btn.disabled = false;
            drawJellyfin();
        });
    }

    // requestable
    // Tells whether a certificate can be requested now: the proxy runs and the Let's Encrypt terms are accepted.
    function requestable() {
        return !!proxyStatus.Running && acceptTerms;
    }

    // showRoutes
    // Draws the route rows, and the open editor's certificate status when its route is among them.
    function showRoutes(answer) {
        routes = (answer && answer.Routes) || [];
        statuses = (answer && answer.Status) || [];
        var items = routeItems(routes, statuses, shared.t);
        var none = items.length === 0;
        [['#proxyNoRoutes', !none], ['#proxyRoutes', none], ['#proxyActions', none]].forEach(function (pair) {
            var node = view.querySelector(pair[0]);
            if (node) node.classList.toggle('stolon-hidden', pair[1]);
        });
        if (list) list.setItems(items);
        else {
            list = nectoModule.createRecordList({
                items: items,
                label: shared.t.tr('ProxyHeaderRoutes'),
                emptyText: shared.t.tr('ProxyEmptyRoutes'),
                onOpen: function (item) { openRoute(item.route, item.status); }
            });
            view.querySelector('#proxyRoutes').appendChild(list.element);
        }
        if (openEditor && openEditor.id) {
            var mine = statuses.find(function (s) { return s.Id === openEditor.id; });
            if (mine) openEditor.editor.setRouteStatus(mine);
        }
    }

    // refresh
    // Reads the routes and the status again, so a certificate being issued or Jellyfin restarting shows on its own.
    // An answer that lands after a later read began is dropped.
    function refresh() {
        var mine = ++refreshSeq;
        var loading = loadSeq;
        return Promise.all([shared.request('Proxy/Routes'), shared.request('Proxy/Status')]).then(function (answers) {
            if (mine !== refreshSeq || loading !== loadSeq) return;
            showRoutes(answers[0]);
            showStatus(answers[1] || {});
            if (openEditor) openEditor.editor.setRequestable(requestable());
        }).catch(function () { /* the next tick tries again */ });
    }

    // loadAll
    // Reads the routes, the status, the DDNS records and names, then draws them. A late answer is dropped.
    function loadAll() {
        var mine = ++loadSeq;
        return Promise.all([
            shared.request('Proxy/Routes'),
            shared.request('Proxy/Status'),
            shared.request('Ddns/Records').catch(function () { return { Records: [] }; }),
            shared.request('Ddns/Providers').catch(function () { return []; }),
            shared.request('Proxy/DdnsHostnames').catch(function () { return []; }),
            shared.request('Proxy/Settings').catch(function () { return {}; })
        ]).then(function (answers) {
            if (mine !== loadSeq) return;
            acceptTerms = !!(answers[5] && answers[5].AcceptTerms);
            var txt = {};
            (Array.isArray(answers[3]) ? answers[3] : []).forEach(function (p) { if (p.Txt) txt[p.Value] = true; });
            records = ((answers[2] && answers[2].Records) || []).map(function (e) { return e.Record; }).filter(function (r) { return txt[r.Provider]; });
            hostnames = Array.isArray(answers[4]) ? answers[4] : [];
            showRoutes(answers[0]);
            showStatus(answers[1] || {});
            if (openEditor) openEditor.editor.setRequestable(requestable());
            if (visitors) visitors.load();
        }).catch(function (err) {
            if (mine !== loadSeq) return;
            shared.errorText(err, shared.t.tr('ProxyErrorLoad')).then(function (text) { report(text, true); });
        });
    }

    // saveRoute
    // Saves the editor's route, a new one by POST and an existing one by PUT.
    function saveRoute(editor, isNew) {
        var problem = editor.validate();
        if (problem) {
            editor.setStatus(shared.t.tr(problem), true);
            return Promise.resolve(false);
        }
        var value = editor.getValue();
        var request = isNew ? shared.request('Proxy/Routes', 'POST', value) : shared.request('Proxy/Routes/' + encodeURIComponent(value.Id), 'PUT', value);
        return request.then(function (result) {
            report(result && result.Restart ? result.Restart : shared.t.tr('ProxyToastRouteSaved'), false);
            loadAll();
            return true;
        }).catch(function (err) {
            return shared.errorText(err, shared.t.tr('ProxyErrorSaveRoute')).then(function (text) {
                editor.setStatus(text, true);
                return false;
            });
        });
    }

    // removeRoute
    // Asks first, then removes the route and its certificate files. The service it forwarded to stays.
    function removeRoute(route, close) {
        var t = shared.t;
        nectoModule.confirmDialog(t.tr('ProxyConfirmRemoveRoute'), {
            title: t.format('TitleRemoveName', route.Host || t.tr('ProxyNoHost')),
            confirmText: t.tr('ButtonRemove'),
            destructive: true
        }).then(function (confirmed) {
            if (!confirmed) return;
            shared.request('Proxy/Routes/' + encodeURIComponent(route.Id), 'DELETE').then(function () {
                close('removed');
                report(t.tr('ProxyToastRouteRemoved'), false);
                loadAll();
            }).catch(function (err) {
                shared.errorText(err, t.tr('ProxyErrorRemoveRoute')).then(function (text) { report(text, true); });
            });
        });
    }

    // uploadCertificate
    // Sends the picked certificate files for a route, then shows its status.
    function uploadCertificate(route, editor) {
        var t = shared.t;
        var inputs = editor.uploadInputs;
        var file = inputs.certificate.files && inputs.certificate.files[0];
        if (!file) {
            editor.setStatus(t.tr('ProxyErrorChooseCertificateFile'), true);
            return Promise.resolve();
        }
        var form = new FormData();
        form.append('certificate', file);
        if (inputs.key.files && inputs.key.files[0]) form.append('key', inputs.key.files[0]);
        if (inputs.password.value) form.append('password', inputs.password.value);
        editor.setStatus(t.tr('ProxyUploading'), false);
        return fetch(ApiClient.getUrl('Proxy/Routes/' + encodeURIComponent(route.Id) + '/Certificate'), {
            method: 'POST',
            headers: { 'Authorization': 'MediaBrowser Token="' + ApiClient.accessToken() + '"' },
            body: form
        }).then(function (response) {
            if (!response.ok) throw response;
            return response.json();
        }).then(function (result) {
            editor.setStatus(t.tr('ProxyToastCertificateUploaded'), false);
            var mine = ((result && result.Status) || []).find(function (s) { return s.Id === route.Id; });
            editor.setRouteStatus(mine);
            loadAll();
        }).catch(function (err) {
            return shared.errorText(err, t.tr('ProxyErrorUpload')).then(function (text) { editor.setStatus(text, true); });
        });
    }

    // downloadRoot
    // Saves the self-signed root as a file a device can install.
    function downloadRoot(editor) {
        return shared.request('Proxy/Certificate/Root').then(function (root) {
            var blob = new Blob([root.Pem], { type: 'application/x-pem-file' });
            var link = document.createElement('a');
            link.href = URL.createObjectURL(blob);
            link.download = 'necto-root.crt';
            link.click();
            editor.setStatus(shared.t.format('ProxyRootFingerprint', root.Fingerprint), false);
            editor.setRoot(root);
        }).catch(function (err) {
            return shared.errorText(err, shared.t.tr('ProxyErrorRoot')).then(function (text) { editor.setStatus(text, true); });
        });
    }

    // requestCertificate
    // Asks first, since Let's Encrypt limits how often it issues, then orders a new certificate for the route.
    function requestCertificate(route, editor) {
        var t = shared.t;
        if (!requestable()) return Promise.resolve();
        return nectoModule.confirmDialog(t.tr('ProxyConfirmRequestCertificate'), { title: t.format('ProxyTitleRequestCertificate', route.Host || t.tr('ProxyNoHost')), confirmText: t.tr('ProxyButtonRequestCertificate') }).then(function (ok) {
            if (!ok) return;
            editor.setStatus(t.tr('ProxyRequesting'), false);
            return shared.request('Proxy/Routes/' + encodeURIComponent(route.Id) + '/Certificate/Request', 'POST').then(function (result) {
                editor.setStatus(result.Message, !result.Ok);
                editor.setRouteStatus((result.Status || []).find(function (s) { return s.Id === route.Id; }));
            }).catch(function (err) {
                return shared.errorText(err, t.tr('ProxyErrorRequest')).then(function (text) { editor.setStatus(text, true); });
            });
        });
    }

    // onEditorAction
    // Runs an action from a route's sidecar.
    function onEditorAction(action, route, btn, editor) {
        var t = shared.t;
        btn.disabled = true;
        var done = function () { btn.disabled = false; };
        if (action === 'upload') return uploadCertificate(route, editor).then(done);
        if (action === 'root') return downloadRoot(editor).then(done);
        if (action === 'request') return requestCertificate(route, editor).then(function () { editor.setRequestable(requestable()); });
        editor.setStatus(t.tr('ProxyTesting'), false);
        return shared.request('Proxy/Routes/' + encodeURIComponent(route.Id) + '/Test', 'POST').then(function (result) {
            editor.setStatus(result.Ok ? t.tr('ProxyRouteAnswered') : result.Message, !result.Ok);
        }).catch(function (err) {
            return shared.errorText(err, t.tr('ProxyErrorTestRoute')).then(function (text) { editor.setStatus(text, true); });
        }).then(done);
    }

    // openRoute
    // Opens a route, or a new one from a template, in the editor sidecar.
    function openRoute(route, status) {
        var t = shared.t;
        var isNew = !route || !route.Id;
        var editor = createRouteEditor(route || newRoute(), { shared: shared, status: status, records: records, hostnames: hostnames, requestable: requestable(), onAction: onEditorAction });
        var mine = { id: isNew ? '' : route.Id, editor: editor };
        openEditor = mine;
        if (!isNew && fields.enumName(route.Certificate && route.Certificate.Mode, CERTIFICATE_MODES) === 'SelfSigned') {
            shared.request('Proxy/Certificate/Root').then(function (root) {
                if (openEditor === mine) editor.setRoot(root);
            }).catch(function () { /* the root is made once the proxy starts */ });
        }
        nectoModule.openEditorSidecar({
            title: isNew ? t.tr('ProxyTitleNewRoute') : (route.Host || t.tr('ProxyNoHost')),
            body: editor.element,
            page: page,
            isNew: isNew,
            serialize: function () { return JSON.stringify(editor.getValue()); },
            saveText: t.tr('Save'),
            deleteText: t.tr('ButtonRemove'),
            onSave: function () { return saveRoute(editor, isNew); },
            onDelete: function (close) { removeRoute(route, close); },
            onClose: function () {
                if (openEditor === mine) openEditor = null;
                editor.destroy();
            }
        });
    }

    // pickTemplate
    // Shows the route templates, and opens the editor with the one picked.
    function pickTemplate() {
        var t = shared.t;
        shared.request('Proxy/Templates').then(function (data) {
            var group = h('div', { class: 'stolon-choice-group', role: 'radiogroup', 'aria-label': t.tr('ProxyLabelTemplate') });
            TEMPLATES.forEach(function (tpl, i) {
                var input = h('input', { type: 'radio', name: 'necto-route-template', value: tpl.key, checked: i === 0 });
                group.appendChild(h('label', { class: 'stolon-choice' }, [
                    input,
                    h('span', { class: 'stolon-choice-name', text: t.tr(tpl.title) }),
                    h('span', { class: 'stolon-choice-text', text: t.tr(tpl.help) })
                ]));
            });
            var domain = nectoModule.createInputField({ label: t.tr('LabelDomain'), field: 'Domain', value: data.Domain || '', placeholder: 'example.com', help: t.tr('ProxyDomainHelp') });
            var choices = nectoModule.createChoiceGroup(group);
            var body = h('div', {}, [h('div', { class: 'fieldDescription', text: t.tr('ProxyTemplatesHelp') }), group, domain.element]);
            nectoModule.openSidecar({
                title: t.tr('ProxyTitleNewRoute'),
                body: body,
                buttons: [
                    {
                        text: t.tr('Next'), className: 'raised stolon-button stolon-primary', action: function (close) {
                            var key = choices.get() || 'jellyfin';
                            var zone = domain.input.value.trim().toLowerCase();
                            var built = (data.Templates || []).find(function (x) { return x.Key === key; });
                            var route = JSON.parse(JSON.stringify((built && built.Route) || newRoute()));
                            route.Id = '';
                            if (key === 'jellyfin') { route.Host = zone; route.LocalHost = zone ? 'home.' + zone : ''; }
                            close();
                            openRoute(route, null);
                        }
                    }
                ],
                onClose: function () { choices.destroy(); }
            });
        }).catch(function (err) {
            shared.errorText(err, t.tr('ProxyErrorLoad')).then(function (text) { report(text, true); });
        });
    }

    // createVisitors
    // Builds the Visitors section: the bans, what each address did, and the countries.
    function createVisitors() {
        var t = shared.t;
        var content = h('div', {}, [h('div', { class: 'fieldDescription', text: t.tr('ProxyVisitorsHelp') })]);
        var off = h('div', { class: 'stolon-info stolon-hidden', text: t.tr('ProxyVisitorsOff') });
        var status = h('div', { class: 'stolon-status-text', role: 'status' });
        var bannedHost = h('div', {});
        var activityHost = h('div', {});
        var countryHost = h('div', {});
        var attribution = h('div', { class: 'fieldDescription' });
        var location = h('div', { class: 'fieldDescription', 'data-role': 'location' });
        [off, status,
            h('h3', { class: 'stolon-subsection-title', text: t.tr('ProxyHeaderBanned') }), bannedHost,
            h('h3', { class: 'stolon-subsection-title', text: t.tr('HeaderActivity') }), activityHost,
            h('h3', { class: 'stolon-subsection-title', text: t.tr('HeaderCountries') }), countryHost,
            location, attribution].forEach(function (n) { content.appendChild(n); });
        var section = nectoModule.createCollapsibleSection({ title: t.tr('HeaderVisitors'), icon: 'travel_explore', collapsed: true, content: content });
        section.element.id = 'proxyVisitors';
        var holderVisitors = view.querySelector('#proxyVisitors');
        holderVisitors.parentNode.replaceChild(section.element, holderVisitors);

        var button = function (label, variant, act, name) {
            var b = h('button', { is: 'emby-button', type: 'button', class: 'raised stolon-button stolon-small ' + variant, 'aria-label': name }, [h('span', { text: label })]);
            if (act && act.reason) b.setAttribute('title', act.action === 'unblock-country' ? t.format(act.reason, act.country) : t.tr(act.reason));
            if (act && !act.disabled) b.addEventListener('click', function () { visitorAction(act); });
            else b.disabled = true;
            return b;
        };
        var place = function (r) { return r.IsLocal ? t.tr('LabelLocal') : (r.Country || t.tr('Unknown')); };
        var banned = nectoModule.createDataTable({
            columns: [
                { key: 'Ip', label: t.tr('LabelAddress'), icon: 'lan' },
                { key: 'Country', label: t.tr('HeaderCountry'), icon: 'flag', value: function (b) { return b.Country || t.tr('Unknown'); } },
                { key: 'Failures', label: t.tr('ProxyColumnFailures'), icon: 'error', align: 'end' },
                { key: 'BannedUtc', label: t.tr('ProxyHeaderBanned'), icon: 'block', value: function (b) { return b.BannedUtc ? shared.formatDate(b.BannedUtc) : ''; } },
                { key: 'UntilUtc', label: t.tr('ProxyColumnEnds'), icon: 'event', value: function (b) { return b.UntilUtc ? shared.formatDate(b.UntilUtc) : t.tr('ProxyUntilLifted'); } },
                { key: 'action', label: t.tr('HeaderAction'), sortable: false, render: function (b) { return button(t.tr('ProxyButtonUnban'), 'stolon-secondary', { action: 'unban', value: b.Ip }, t.format('ProxyButtonUnbanAddress', b.Ip)); } }
            ],
            rows: [],
            rowKey: 'Ip',
            label: t.tr('ProxyHeaderBanned'),
            emptyText: t.tr('ProxyEmptyBanned')
        });
        bannedHost.appendChild(banned.element);
        var activity = nectoModule.createDataTable({
            columns: [
                { key: 'Type', label: t.tr('ProxyColumnType'), icon: 'category', value: function (r) { return t.tr(ACTIVITY_KEYS[fields.enumName(r.Type, ACTIVITY_TYPES)]); } },
                { key: 'User', label: t.tr('LabelUser'), icon: 'person' },
                { key: 'Ip', label: t.tr('LabelAddress'), icon: 'lan' },
                { key: 'Device', label: t.tr('LabelDevice'), icon: 'devices' },
                { key: 'Host', label: t.tr('LabelHostName'), icon: 'dns', value: function (r) { return (r.Host || '').replace(/^https?:\/\//, ''); } },
                { key: 'place', label: t.tr('HeaderCountry'), icon: 'flag', value: place },
                { key: 'city', label: t.tr('ProxyColumnCity'), icon: 'location_city', hidden: true, value: function (r) { return r.IsLocal ? '' : (r.City || ''); } },
                { key: 'LastUtc', label: t.tr('ProxyColumnLastSeen'), icon: 'schedule', value: function (r) { return shared.formatDate(r.LastUtc); } },
                {
                    key: 'action', label: t.tr('HeaderAction'), sortable: false, render: function (r) {
                        var act = rowAction(r);
                        var labels = { unban: 'ProxyButtonUnban', unblock: 'ButtonUnblock', 'unblock-country': 'ButtonUnblock', block: 'ButtonBlock' };
                        return button(t.tr(labels[act.action]), act.action === 'block' ? 'stolon-destructive' : 'stolon-secondary', act, t.format('ProxyButtonActOn', r.Ip));
                    }
                }
            ],
            rows: [],
            rowKey: function (r) { return r.Type + '|' + r.Ip + '|' + r.User + '|' + r.Host; },
            filters: [
                { name: 'type', label: t.tr('ProxyColumnType'), field: 'kind', options: ACTIVITY_TYPES.map(function (k) { return { value: k, label: t.tr(ACTIVITY_KEYS[k]) }; }) },
                { name: 'place', label: t.tr('HeaderCountry'), field: 'placeCode', options: [] }
            ],
            pageSize: 25,
            maxHeight: '32rem',
            label: t.tr('HeaderActivity'),
            emptyText: t.tr('ProxyEmptyActivity')
        });
        activityHost.appendChild(activity.element);
        var countries = nectoModule.createDataTable({
            columns: [
                { key: 'Country', label: t.tr('HeaderCountry'), icon: 'flag' },
                { key: 'People', label: t.tr('People'), icon: 'group', align: 'end' },
                { key: 'Failed', label: t.tr('HeaderFailedSignIns'), icon: 'error', align: 'end' },
                { key: 'Blocked', label: t.tr('HeaderRefused'), icon: 'block', align: 'end' },
                { key: 'LastUtc', label: t.tr('ProxyColumnLastSeen'), icon: 'schedule', value: function (c) { return c.LastUtc ? shared.formatDate(c.LastUtc) : ''; } }
            ],
            rows: [],
            rowKey: 'Code',
            label: t.tr('HeaderCountries'),
            emptyText: t.tr('ProxyEmptyCountries')
        });
        countryHost.appendChild(countries.element);
        var loads = 0;

        // visitorAction
        // Asks before blocking an address or letting a whole country through, then runs a row's action and reloads
        // the tables.
        function visitorAction(act) {
            var routes = { unban: 'Proxy/Visitors/Unban', unblock: 'Proxy/Visitors/Unblock', 'unblock-country': 'Proxy/Visitors/UnblockCountry', block: 'Proxy/Visitors/Block' };
            var go = function () {
                return shared.request(routes[act.action], 'POST', { Value: act.value }).then(function () {
                    shared.setStatus(status, t.tr('ProxyDone'), false, { timeout: 6000 });
                    load();
                }).catch(function (err) {
                    return shared.errorText(err, t.tr('ProxyErrorVisitorAction')).then(function (text) { shared.setStatus(status, text, true); });
                });
            };
            if (act.action === 'block') {
                return nectoModule.confirmDialog(t.tr('ProxyConfirmBlock'), { title: t.format('ProxyTitleBlock', act.value), confirmText: t.tr('ButtonBlock'), destructive: true }).then(function (ok) { if (ok) return go(); });
            }
            if (act.action === 'unblock-country') {
                return nectoModule.confirmDialog(t.tr('ProxyConfirmUnblockCountry'), { title: t.format('ProxyTitleUnblockCountry', act.country), confirmText: t.tr('ButtonUnblock') }).then(function (ok) { if (ok) return go(); });
            }
            return go();
        }

        // placeOptions
        // Lists the places the activity rows come from, your network first, for the place filter.
        function placeOptions(rows) {
            var seen = {};
            var out = [];
            rows.forEach(function (r) {
                if (seen[r.placeCode]) return;
                seen[r.placeCode] = true;
                out.push({ value: r.placeCode, label: r.placeCode === 'local' ? t.tr('LabelLocal') : (r.Country || t.tr('Unknown')) });
            });
            return out.sort(function (a, b) { return (a.value === 'local' ? -1 : 0) - (b.value === 'local' ? -1 : 0) || a.label.localeCompare(b.label); });
        }

        // load
        // Reads the visitors and draws the tables. An answer that lands after a later read began is dropped.
        function load() {
            var mine = ++loads;
            return shared.request('Proxy/Visitors').then(function (data) {
                if (mine !== loads) return;
                off.classList.toggle('stolon-hidden', !!data.Recording);
                banned.setRows(data.Banned || []);
                var rows = (data.Activity || []).map(function (r) {
                    return Object.assign({ kind: fields.enumName(r.Type, ACTIVITY_TYPES), placeCode: r.IsLocal ? 'local' : (r.CountryCode || '') }, r);
                });
                activity.setColumnHidden('city', !showsCity(data));
                activity.setRows(rows);
                activity.setFilterOptions('place', placeOptions(rows));
                location.textContent = locationText(data.Location, shared);
                countries.setRows(countryRows(data));
                attribution.textContent = data.Attribution || '';
            }).catch(function (err) {
                if (mine !== loads) return;
                return shared.errorText(err, t.tr('ProxyErrorLoadVisitors')).then(function (text) { shared.setStatus(status, text, true); });
            });
        }

        return { element: section.element, load: load, destroy: function () { banned.destroy(); activity.destroy(); countries.destroy(); section.destroy && section.destroy(); } };
    }

    var made = nectoModule.createNectoPage(view, {
        page: 'necto_proxy',
        bind: function (bag, pg) {
            view.querySelector('#btnAddRoute').addEventListener('click', pickTemplate);
            var first = view.querySelector('#btnAddFirstRoute');
            if (first) first.addEventListener('click', pickTemplate);
            visitors = createVisitors();
            pg.addCleanup(function () {
                if (visitors) visitors.destroy();
            });
        },
        load: function (bag, pg) {
            loadAll();
            pg.createPoller(refresh, STATUS_POLL_MS).start(false);
            pg.createPoller(function () { return visitors ? visitors.load() : null; }, VISITORS_POLL_MS).start(false);
        }
    });
    shared = made.shared;
    page = made.page;
}
