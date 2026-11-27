// necto_dashboard.js
// The Dashboard tool: a status card for each tool that is on, one world map of the proxy's visitors and the WireGuard
// tunnel ends, the share card, and Necto's recent activity. A card opens the tool it sums up. The page only reads.

import * as nectoModule from './configurationpage?name=necto_shared.js';
import { stateOf as diskState } from './configurationpage?name=necto_storage.js';
import { resultTag as recordTag } from './configurationpage?name=necto_ddns.js';

// EMPTY_CHART
// The flat gray line a card shows when it has nothing to chart, so every card keeps the same shape and the cards in
// a row line up.
var EMPTY_CHART = { sparkline: [0, 0], sparklineColor: 'gray' };

// DASH
// What a card shows in place of a value it could not read.
var DASH = '—';

// CERTIFICATE_WARNING_DAYS
// How close to its expiry a certificate makes the Proxy card ask for attention.
var CERTIFICATE_WARNING_DAYS = 14;

// MAP_LAYERS
// Each kind the map marks, with the tool that must be on for it to show, the count it reads from a country, and its
// color. The server's own country shows while either tool is on.
var MAP_LAYERS = [
    { key: 'visitors', tool: 'proxy', field: 'Visitors', label: 'HeaderVisitors', color: 'blue' },
    { key: 'failed', tool: 'proxy', field: 'Failed', label: 'HeaderFailedSignIns', color: 'red' },
    { key: 'blocked', tool: 'proxy', field: 'Blocked', label: 'HeaderRefused', color: 'purple' },
    { key: 'peers', tool: 'wireguard', field: 'Peers', label: 'HeaderPeers', color: 'green' },
    { key: 'connections', tool: 'wireguard', field: 'Connections', label: 'HeaderConnections', color: 'orange' },
    { key: 'server', tool: 'any', field: 'Server', label: 'DashboardLayerServer', color: 'yellow' }
];

// TYPE_TOOLS
// Which tool wrote an activity log entry, by the start of its type, most specific first. Event entries from
// Notifications carry the event's key, such as Necto.proxy.banned, so the tool names are matched in any case.
var TYPE_TOOLS = [
    ['necto.proxy', 'proxy'], ['necto.ddns', 'ddns'], ['necto.scripts', 'scripts'], ['necto.storage', 'storage'],
    ['necto.pages', 'pages'], ['necto.invite', 'invites'], ['necto.notifications', 'notifications'],
    ['necto.wireguard', 'wireguard'], ['necto.requests', 'requests'], ['necto.issues', 'issues'],
    ['requests.issue', 'issues'], ['requests', 'requests'], ['wireguard', 'wireguard'], ['storage', 'storage'],
    ['scripts', 'scripts'], ['necto.', 'groups']
];

// failedCard
// Turns a card whose route could not be read into one that says so.
//
// Inputs
// t [object]: The strings.
// card [object]: The card's icon, title, and tool.
//
// Output
// [object]: The card, with a dash for its value and a red status.
function failedCard(t, card) {
    return Object.assign(card, EMPTY_CHART, { value: DASH, status: t.tr('HeaderError'), statusColor: 'red', detail: t.tr('DashboardCardFailed') });
}

// toolCard
// Starts a tool's card with the tool's own icon and name.
//
// Inputs
// key [string]: The tool's key, as nectoTools lists it.
//
// Output
// [object]: The card's icon, title, and tool.
function toolCard(key) {
    var tool = nectoModule.nectoTools().filter(function (x) { return x.key === key; })[0];
    return { icon: tool.icon, title: tool.name, tool: key };
}

// sum
// Adds up the values of a card's bars or segments.
//
// Inputs
// parts [object[]]: The parts, each with a value.
//
// Output
// [number]: The total.
function sum(parts) {
    return parts.reduce(function (total, part) { return total + (part.value || 0); }, 0);
}

// requestsCard
// Sums up the requests: those waiting for approval as the number, and a column per state.
//
// Inputs
// t [object]: The strings.
// stats [object]: What Requests/Stats answered, or null when it could not be read.
//
// Output
// [object]: The status card's options, with tool for the tool it opens.
export function requestsCard(t, stats) {
    var card = toolCard('requests');
    if (!stats) return failedCard(t, card);
    var by = stats.ByStatus || {};
    var pending = by.Pending || 0;
    var failed = by.Failed || 0;
    var open = pending + (by.Approved || 0) + (by.PartiallyAvailable || 0) + failed;
    card.bars = [
        { label: t.tr('StatePending'), value: pending, color: 'orange' },
        { label: t.tr('StateApproved'), value: by.Approved || 0, color: 'purple' },
        { label: t.tr('LabelAvailable'), value: (by.Available || 0) + (by.PartiallyAvailable || 0), color: 'green' },
        { label: t.tr('LabelFailed'), value: failed, color: 'red' }
    ];
    card.value = pending;
    card.valueLabel = t.tr('StatePending').toLowerCase();
    card.detail = t.format('DashboardRequestsLine', open, failed);
    if (failed > 0) return Object.assign(card, { status: t.tr('LabelFailed'), statusColor: 'red' });
    if (pending > 0) return Object.assign(card, { status: t.tr('StatePending'), statusColor: 'orange' });
    return Object.assign(card, { status: t.tr('PriorityIdle'), statusColor: 'gray' });
}

// issuesCard
// Sums up the issue reports: the open ones as the number, and a column per state.
//
// Inputs
// t [object]: The strings.
// stats [object]: What Issues/Stats answered, by state name, or null when it could not be read.
//
// Output
// [object]: The status card's options, with tool for the tool it opens.
export function issuesCard(t, stats) {
    var card = toolCard('issues');
    if (!stats) return failedCard(t, card);
    var reported = stats.Reported || 0;
    var confirmed = stats.Confirmed || 0;
    card.bars = [
        { label: t.tr('StateReported'), value: reported, color: 'orange' },
        { label: t.tr('StateConfirmed'), value: confirmed, color: 'purple' },
        { label: t.tr('StateResolved'), value: stats.Resolved || 0, color: 'green' },
        { label: t.tr('StateDeclined'), value: (stats.Declined || 0) + (stats.Rescinded || 0), color: 'gray' }
    ];
    card.value = reported + confirmed;
    card.valueLabel = t.tr('StateOpen').toLowerCase();
    card.detail = t.format('DashboardIssuesLine', reported, confirmed);
    if (card.value > 0) return Object.assign(card, { status: t.tr('StateOpen'), statusColor: 'orange' });
    return Object.assign(card, { status: t.tr('PriorityIdle'), statusColor: 'gray' });
}

// invitesCard
// Sums up the invite links: the active ones as the number, and one bar split by state.
//
// Inputs
// t [object]: The strings.
// invites [object[]]: What Invites/List answered, or null when it could not be read.
//
// Output
// [object]: The status card's options, with tool for the tool it opens.
export function invitesCard(t, invites) {
    var card = toolCard('invites');
    if (!Array.isArray(invites)) return failedCard(t, card);
    var count = function (states) { return invites.filter(function (i) { return states.indexOf(i.State) >= 0; }).length; };
    var active = count(['Active']);
    card.segments = [
        { label: t.tr('StateActive'), value: active, color: 'green' },
        { label: t.tr('StateLocked'), value: count(['Locked']), color: 'orange' },
        { label: t.tr('StateExpired'), value: count(['Expired', 'GroupExpired']), color: 'gray' },
        { label: t.tr('StateUsedUp'), value: count(['UsedUp']), color: 'blue' }
    ];
    card.value = active;
    card.valueLabel = t.tr('StateActive').toLowerCase();
    card.detail = t.format('DashboardInvitesLine', active, invites.length);
    if (active > 0) return Object.assign(card, { status: t.tr('StateActive'), statusColor: 'green' });
    return Object.assign(card, { status: t.tr('PriorityIdle'), statusColor: 'gray' });
}

// groupsCard
// Sums up the groups: how many there are besides Everyone, and how many users are in one.
//
// Inputs
// t [object]: The strings.
// config [object]: The plugin configuration.
//
// Output
// [object]: The status card's options, with tool for the tool it opens.
export function groupsCard(t, config) {
    var card = Object.assign(toolCard('groups'), EMPTY_CHART);
    var groups = (config && config.Groups) || [];
    var members = {};
    groups.forEach(function (g) { (g.MemberIds || []).forEach(function (id) { members[String(id).toLowerCase()] = true; }); });
    var count = Object.keys(members).length;
    card.value = groups.length;
    card.detail = t.format(count === 1 ? 'DashboardMembersOne' : 'DashboardMembersMany', count);
    return card;
}

// soonestCertificate
// Finds the loaded certificate of the proxy's routes that expires first.
//
// Inputs
// status [object]: What Proxy/Status answered.
//
// Output
// [Date|null]: When it expires, or null when no route has one.
function soonestCertificate(status) {
    var soonest = null;
    (status.Routes || []).forEach(function (route) {
        var cert = route.Certificate || {};
        if (!cert.Loaded || !cert.NotAfterUtc) return;
        var at = new Date(cert.NotAfterUtc);
        if (!isNaN(at) && (!soonest || at < soonest)) soonest = at;
    });
    return soonest;
}

// proxyCard
// Sums up the proxy: whether it runs, its active routes as the number, a dot per route, and when its first
// certificate expires.
//
// Inputs
// t [object]: The strings.
// shared [object]: The helper bag, for formatDate.
// status [object]: What Proxy/Status answered, or null when it could not be read.
// now [Date]: The time now.
//
// Output
// [object]: The status card's options, with tool for the tool it opens.
export function proxyCard(t, shared, status, now) {
    var card = toolCard('proxy');
    if (!status) return failedCard(t, card);
    var routes = status.Routes || [];
    card.value = routes.filter(function (r) { return r.Active; }).length;
    card.dots = routes.map(function (r) {
        var broken = r.Certificate && r.Certificate.Error;
        return { label: r.Host, color: broken ? 'red' : (r.Active ? 'green' : 'gray') };
    });
    if (!card.dots.length) Object.assign(card, EMPTY_CHART);
    var soonest = soonestCertificate(status);
    var days = soonest ? (soonest - now) / 86400000 : null;
    if (soonest === null) card.detail = t.tr('DashboardNoCertificates');
    // The day is enough for a certificate, and keeps the line inside the card.
    else if (days < 0) card.detail = t.format('DashboardCertificateExpired', new Date(soonest).toLocaleDateString());
    else card.detail = t.format('DashboardCertificateExpires', new Date(soonest).toLocaleDateString());
    if (status.Error) return Object.assign(card, { status: t.tr('LabelFailed'), statusColor: 'red', detail: status.Error });
    if (!status.Running) return Object.assign(card, { status: t.tr('StateStopped'), statusColor: 'gray' });
    if (days !== null && days < 0) return Object.assign(card, { status: t.tr('StateExpired'), statusColor: 'red' });
    if (days !== null && days < CERTIFICATE_WARNING_DAYS) return Object.assign(card, { status: t.tr('DashboardStateAttention'), statusColor: 'orange' });
    return Object.assign(card, { status: t.tr('StateRunning'), statusColor: 'green' });
}

// ddnsCard
// Sums up the DDNS records: those that are on as the number, and a dot per record and per name it follows, in the
// color of its last result.
//
// Inputs
// t [object]: The strings.
// answer [object]: What Ddns/Records answered, or null when it could not be read.
//
// Output
// [object]: The status card's options, with tool for the tool it opens.
export function ddnsCard(t, answer) {
    var card = toolCard('ddns');
    if (!answer) return failedCard(t, card);
    var records = [];
    (answer.Records || []).forEach(function (entry) {
        if (!entry || !entry.Record) return;
        if (entry.Followed && entry.Followed.length) records = records.concat(entry.Followed);
        else records.push(entry.Record);
    });
    var on = records.filter(function (r) { return r.Enabled !== false; });
    card.value = on.length;
    if (!records.length) return Object.assign(card, EMPTY_CHART, { detail: t.tr('DashboardNoRecords'), status: t.tr('PriorityIdle'), statusColor: 'gray' });
    card.dots = records.map(function (r) {
        var tag = r.Enabled === false ? null : recordTag(r, t);
        return { label: r.Hostname || r.Name, color: tag ? tag.color : 'gray' };
    });
    var colors = card.dots.map(function (d) { return d.color; });
    var good = on.filter(function (r) { var tag = recordTag(r, t); return tag && tag.color === 'green'; }).length;
    card.detail = t.format('DashboardRecordsLine', good, on.length);
    if (colors.indexOf('red') >= 0) return Object.assign(card, { status: t.tr('LabelFailed'), statusColor: 'red' });
    if (colors.indexOf('orange') >= 0) return Object.assign(card, { status: t.tr('DashboardStateAttention'), statusColor: 'orange' });
    if (on.length) return Object.assign(card, { status: t.tr('DashboardStateHealthy'), statusColor: 'green' });
    return Object.assign(card, { status: t.tr('Off'), statusColor: 'gray' });
}

// TUNNEL_COLORS
// The dot color of a tunnel end's state: green connected, orange held back by its limits, red failed, and gray off.
var TUNNEL_COLORS = { Connected: 'green', OverLimit: 'orange', NotAllowed: 'orange', OverPeerCount: 'orange', Failed: 'red' };

// wireGuardCard
// Sums up WireGuard: the connected peers and connections as the number, and a dot per tunnel end.
//
// Inputs
// t [object]: The strings.
// map [object]: What Dashboard/Map answered, or null when it could not be read.
//
// Output
// [object]: The status card's options, with tool for the tool it opens.
export function wireGuardCard(t, map) {
    var card = toolCard('wireguard');
    if (!map) return failedCard(t, card);
    var tunnels = map.Tunnels || [];
    var host = tunnels.filter(function (e) { return e.Kind === 'Host'; })[0];
    var ends = tunnels.filter(function (e) { return e.Kind !== 'Host'; });
    var connected = ends.filter(function (e) { return e.Connected; }).length;
    card.value = connected;
    card.valueLabel = t.tr('StateConnected').toLowerCase();
    card.detail = t.format('DashboardTunnelsLine', connected, ends.length);
    card.dots = ends.map(function (e) { return { label: e.Name, color: TUNNEL_COLORS[e.State] || 'gray' }; });
    if (!card.dots.length) Object.assign(card, EMPTY_CHART);
    var failed = (host && host.State === 'Failed') || ends.some(function (e) { return e.State === 'Failed'; });
    if (failed) return Object.assign(card, { status: t.tr('LabelFailed'), statusColor: 'red' });
    if (connected > 0) return Object.assign(card, { status: t.tr('StateConnected'), statusColor: 'green' });
    if (host && host.State === 'Running') return Object.assign(card, { status: t.tr('StateRunning'), statusColor: 'green' });
    return Object.assign(card, { status: t.tr('StateOffline'), statusColor: 'gray' });
}

// scriptsCard
// Sums up the scripts: the failed runs of the last 14 days as the number, with the runs of each day as a line.
//
// Inputs
// t [object]: The strings.
// days [object]: What Scripts/History/Days answered, or null when it could not be read.
//
// Output
// [object]: The status card's options, with tool for the tool it opens.
export function scriptsCard(t, days) {
    var card = toolCard('scripts');
    if (!days) return failedCard(t, card);
    var ok = days.Succeeded || [];
    var bad = days.Failed || [];
    var failed = sum(bad.map(function (n) { return { value: n }; }));
    var runs = failed + sum(ok.map(function (n) { return { value: n }; }));
    card.value = failed;
    card.valueLabel = t.tr('LabelFailed').toLowerCase();
    card.detail = t.format('DashboardRunsLine', runs, failed);
    if (runs > 0) {
        card.sparkline = ok.map(function (n, i) { return (n || 0) + (bad[i] || 0); });
        if (failed > 0) card.sparklineColor = 'red';
    } else {
        Object.assign(card, EMPTY_CHART);
    }
    if (failed > 0) return Object.assign(card, { status: t.tr('LabelFailed'), statusColor: 'red' });
    if (runs > 0) return Object.assign(card, { status: t.tr('DashboardStateHealthy'), statusColor: 'green' });
    return Object.assign(card, { status: t.tr('PriorityIdle'), statusColor: 'gray' });
}

// pagesCard
// Sums up the custom pages: those that are on as the number, out of how many there are.
//
// Inputs
// t [object]: The strings.
// config [object]: The plugin configuration.
//
// Output
// [object]: The status card's options, with tool for the tool it opens.
export function pagesCard(t, config) {
    var card = Object.assign(toolCard('pages'), EMPTY_CHART);
    var pages = (config && config.CustomPages && config.CustomPages.Pages) || [];
    var on = pages.filter(function (p) { return p.Enabled !== false; }).length;
    card.value = on;
    card.detail = t.format('DashboardPagesLine', on, pages.length);
    return card;
}

// storageCard
// Sums up the disks: how many there are, and a dot per disk in the color of its state, as the Storage tool shows it.
//
// Inputs
// t [object]: The strings.
// disks [object[]]: What Storage/Disks answered, or null when it could not be read.
//
// Output
// [object]: The status card's options, with tool for the tool it opens.
export function storageCard(t, disks) {
    var card = toolCard('storage');
    if (!Array.isArray(disks)) return failedCard(t, card);
    var states = disks.map(function (d) { return diskState(t, d); });
    card.value = disks.length;
    card.dots = disks.map(function (d, i) { return { label: (d.IsSystem ? t.tr('LabelSystem') : d.Name) + ', ' + states[i].label, color: states[i].color }; });
    if (!card.dots.length) Object.assign(card, EMPTY_CHART);
    var colors = states.map(function (s) { return s.color; });
    var attention = colors.filter(function (c) { return c === 'red' || c === 'orange'; }).length;
    card.detail = t.format('DashboardDisksLine', attention, disks.length);
    if (colors.indexOf('red') >= 0) return Object.assign(card, { status: t.tr('DashboardStateAttention'), statusColor: 'red' });
    if (colors.indexOf('orange') >= 0) return Object.assign(card, { status: t.tr('DashboardStateAttention'), statusColor: 'orange' });
    if (disks.length) return Object.assign(card, { status: t.tr('DashboardStateHealthy'), statusColor: 'green' });
    return Object.assign(card, { status: t.tr('PriorityIdle'), statusColor: 'gray' });
}

// notificationsCard
// Sums up the send log: the sends it holds as the number, with one bar of those that went and those that failed.
//
// Inputs
// t [object]: The strings.
// log [object[]]: What Notifications/Log answered, newest first, or null when it could not be read.
//
// Output
// [object]: The status card's options, with tool for the tool it opens.
export function notificationsCard(t, log) {
    var card = toolCard('notifications');
    if (!Array.isArray(log)) return failedCard(t, card);
    var failed = log.filter(function (e) { return !e.Ok; }).length;
    card.value = log.length;
    card.segments = [
        { label: t.tr('StateSent'), value: log.length - failed, color: 'green' },
        { label: t.tr('LabelFailed'), value: failed, color: 'red' }
    ];
    card.detail = t.format('DashboardSendsLine', log.length - failed, failed);
    if (log.length && !log[0].Ok) return Object.assign(card, { status: t.tr('LabelFailed'), statusColor: 'red' });
    if (failed > 0) return Object.assign(card, { status: t.tr('DashboardStateAttention'), statusColor: 'orange' });
    if (log.length) return Object.assign(card, { status: t.tr('DashboardStateHealthy'), statusColor: 'green' });
    return Object.assign(card, { status: t.tr('PriorityIdle'), statusColor: 'gray' });
}

// toolsOn
// Lists the tools that get a card: every tool in the sidebar but the dashboard, Settings, and Troubleshooting, without
// Issues, Proxy, or WireGuard while it is off.
//
// Inputs
// config [object]: The plugin configuration.
//
// Output
// [string[]]: The tool keys, in sidebar order.
export function toolsOn(config) {
    var off = {
        dashboard: true, settings: true, troubleshooting: true,
        issues: !(config && config.Issues && config.Issues.Enabled),
        proxy: !(config && config.Proxy && config.Proxy.Enabled),
        wireguard: !(config && config.WireGuard && config.WireGuard.Enabled)
    };
    return nectoModule.nectoTools().map(function (tool) { return tool.key; }).filter(function (key) { return !off[key]; });
}

// showsMap
// Whether the world map shows, which is while the proxy or WireGuard is on.
//
// Inputs
// config [object]: The plugin configuration.
//
// Output
// [boolean]: true while either is on.
export function showsMap(config) {
    return !!((config && config.Proxy && config.Proxy.Enabled) || (config && config.WireGuard && config.WireGuard.Enabled));
}

// CARD_READS
// The route each card reads, by tool. A tool with none builds its card from the configuration, and WireGuard's card
// reads the map.
var CARD_READS = {
    requests: 'Requests/Stats',
    issues: 'Issues/Stats',
    invites: 'Invites/List',
    proxy: 'Proxy/Status',
    ddns: 'Ddns/Records',
    scripts: 'Scripts/History/Days',
    storage: 'Storage/Disks',
    notifications: 'Notifications/Log'
};

// cardFor
// Builds a tool's card from what its route answered. An answer it cannot read gives the card that says so.
//
// Inputs
// key [string]: The tool's key.
// t [object]: The strings.
// shared [object]: The helper bag.
// config [object]: The plugin configuration.
// answer [any]: What the tool's route answered, or null when it could not be read.
// map [object]: What Dashboard/Map answered, or null.
// now [Date]: The time now.
//
// Output
// [object]: The status card's options, with tool for the tool it opens.
export function cardFor(key, t, shared, config, answer, map, now) {
    try {
        return buildCard(key, t, shared, config, answer, map, now);
    } catch (err) {
        // An answer of an unexpected shape fails only its own card.
        return failedCard(t, toolCard(key));
    }
}

// buildCard
// Picks the card builder of a tool and builds its card, without the guard cardFor puts around it.
//
// Inputs
// key [string]: The tool's key.
// t [object]: The strings.
// shared [object]: The helper bag.
// config [object]: The plugin configuration.
// answer [any]: What the tool's route answered, or null when it could not be read.
// map [object]: What Dashboard/Map answered, or null.
// now [Date]: The time now.
//
// Output
// [object]: The status card's options, with tool for the tool it opens.
function buildCard(key, t, shared, config, answer, map, now) {
    switch (key) {
        case 'requests': return requestsCard(t, answer);
        case 'issues': return issuesCard(t, answer);
        case 'invites': return invitesCard(t, answer);
        case 'groups': return groupsCard(t, config);
        case 'proxy': return proxyCard(t, shared, answer, now);
        case 'ddns': return ddnsCard(t, answer);
        case 'wireguard': return wireGuardCard(t, map);
        case 'scripts': return scriptsCard(t, answer);
        case 'pages': return pagesCard(t, config);
        case 'storage': return storageCard(t, answer);
        default: return notificationsCard(t, answer);
    }
}

// mapLayers
// Lists the map's layers for the tools that are on.
//
// Inputs
// t [object]: The strings.
// map [object]: What Dashboard/Map answered.
//
// Output
// [object[]]: Each layer's key, label, color, and the country field it counts.
export function mapLayers(t, map) {
    return MAP_LAYERS.filter(function (layer) {
        if (layer.tool === 'proxy') return !!map.Proxy;
        if (layer.tool === 'wireguard') return !!map.WireGuard;
        return true;
    }).map(function (layer) {
        return { key: layer.key, label: t.tr(layer.label), color: layer.color, field: layer.field };
    });
}

// mapCounts
// Turns the map's countries into the world map's counts, by country code and then by layer.
//
// Inputs
// map [object]: What Dashboard/Map answered.
// layers [object[]]: The layers, from mapLayers.
//
// Output
// [object]: { CODE: { layerKey: count } }, with only the layers that have a count.
export function mapCounts(map, layers) {
    var counts = {};
    (map.Countries || []).forEach(function (country) {
        layers.forEach(function (layer) {
            var value = country[layer.field];
            var count = value === true ? 1 : (Number(value) || 0);
            if (count > 0) (counts[country.CountryCode] = counts[country.CountryCode] || {})[layer.key] = count;
        });
    });
    return counts;
}

// countryRows
// One row per country for the table under the map, counting only the layers that are on, most first.
//
// Inputs
// map [object]: What Dashboard/Map answered.
// layers [object[]]: The layers, from mapLayers.
// active [string[]]: The keys of the layers that are on.
// nameOf [function]: Names a country by its code, for one the location database did not name.
//
// Output
// [object[]]: Rows of { code, name, total } and a count per layer key, without countries that have nothing.
export function countryRows(map, layers, active, nameOf) {
    var shown = layers.filter(function (layer) { return active.indexOf(layer.key) >= 0; });
    return (map.Countries || []).map(function (country) {
        var row = { code: country.CountryCode, name: country.Country || nameOf(country.CountryCode) || country.CountryCode, total: 0 };
        layers.forEach(function (layer) {
            var value = country[layer.field];
            row[layer.key] = value === true ? 1 : (Number(value) || 0);
        });
        shown.forEach(function (layer) { row.total += row[layer.key]; });
        return row;
    }).filter(function (row) { return row.total > 0; }).sort(function (a, b) {
        return b.total - a.total || a.name.localeCompare(b.name);
    });
}

// toolOfType
// Names the tool that wrote an activity log entry.
//
// Inputs
// type [string]: The entry's type.
//
// Output
// [string|null]: The tool's key, or null for one the page does not know.
export function toolOfType(type) {
    var lower = String(type || '').toLowerCase();
    for (var i = 0; i < TYPE_TOOLS.length; i++) {
        if (lower.indexOf(TYPE_TOOLS[i][0]) === 0) return TYPE_TOOLS[i][1];
    }
    return null;
}

// SEVERITY_STATUS
// The timeline status of an entry's severity: red for errors, orange for warnings, and blue for the rest.
var SEVERITY_STATUS = { Critical: 'red', Error: 'red', Warning: 'orange' };

// activityItems
// Turns Necto's activity log entries into timeline events, each marked with its tool's icon and its severity.
//
// Inputs
// entries [object[]]: What Dashboard/Activity answered, newest first.
// shared [object]: The helper bag, for formatDate.
//
// Output
// [object[]]: Timeline items of { time, datetime, title, body, status, icon }.
export function activityItems(entries, shared) {
    var icons = {};
    nectoModule.nectoTools().forEach(function (tool) { icons[tool.key] = tool.icon; });
    return (Array.isArray(entries) ? entries : []).map(function (entry) {
        var tool = toolOfType(entry.Type);
        var item = {
            time: shared.formatDate(entry.Date),
            datetime: entry.Date,
            title: entry.Name,
            status: SEVERITY_STATUS[entry.Severity] || 'blue'
        };
        if (entry.Overview) item.body = entry.Overview;
        if (tool && icons[tool]) item.icon = icons[tool];
        return item;
    });
}

export default function (view) {
    'use strict';

    var grid = null;
    var map = null;
    var countries = null;
    var mapHost = null;
    var tableHost = null;
    var mapNote = null;
    var attribution = null;
    var layers = [];
    var mapData = null;
    var layerSet = null;
    var share = null;
    var qr = null;
    var shareNote = null;
    var shareHomeOnly = null;
    var shareButton = null;
    var timeline = null;
    // Each load counts up, so an answer that lands after a later load began is dropped.
    var loads = 0;

    nectoModule.createNectoPage(view, {
        page: 'necto_dashboard',
        bind: function (shared) { bind(shared); },
        load: function (shared, page) {
            load(shared);
            page.createPoller(function () { return refresh(shared); }, 60000).start(false);
        },
        onHide: function () { clearCards(); }
    });

    function getEl(id) { return view.querySelector('#' + id); }

    // Reads a route, answering null when it fails, so one card that cannot load never hides the others.
    function read(shared, path) {
        return shared.request(path, 'GET').catch(function () { return null; });
    }

    function clearCards() {
        if (grid) grid.destroy();
        grid = null;
        var holder = getEl('dashboardCards');
        if (holder) holder.innerHTML = '';
    }

    function drawCards(cards) {
        clearCards();
        // The grid goes on the page before its cards, so its rows are balanced to the width they have.
        grid = nectoModule.createStatusCardGrid({ minWidth: 240 });
        getEl('dashboardCards').appendChild(grid.element);
        grid.setCards(cards.map(function (card) {
            var options = Object.assign({}, card);
            delete options.tool;
            options.onClick = function () { nectoModule.openTool(view, card.tool); };
            return options;
        }));
    }

    // Fills the country table for the layers that are on, each with its column.
    function showLayers(active) {
        if (!countries || !mapData) return;
        layers.forEach(function (layer) { countries.setColumnHidden(layer.key, active.indexOf(layer.key) < 0); });
        countries.setRows(countryRows(mapData, layers, active, map.countryName));
    }

    function buildMap(t) {
        var body = nectoModule.el('div', { class: 'necto-detail' });
        mapNote = nectoModule.el('div', { class: 'stolon-info stolon-hidden', text: t.tr('DashboardMapLoading') });
        mapHost = nectoModule.el('div');
        tableHost = nectoModule.el('div');
        attribution = nectoModule.el('a', { class: 'button-link', href: 'https://db-ip.com', target: '_blank', rel: 'noopener noreferrer' });
        body.appendChild(nectoModule.el('div', { class: 'fieldDescription', text: t.tr('DashboardMapHelp') }));
        body.appendChild(mapNote);
        body.appendChild(mapHost);
        body.appendChild(tableHost);
        body.appendChild(nectoModule.el('div', { class: 'fieldDescription' }, [attribution]));
        var section = nectoModule.createCollapsibleSection({ title: t.tr('HeaderLocations'), icon: 'public', collapsible: false, content: body });
        getEl('dashboardMap').appendChild(section.element);
    }

    // Builds the map and its country table with a layer and a column for each kind the tools that are on give.
    function buildLayers(t) {
        if (map) map.destroy();
        if (countries) countries.destroy();
        mapHost.innerHTML = '';
        tableHost.innerHTML = '';
        map = nectoModule.createWorldMap({
            layers: layers.map(function (l) { return { key: l.key, label: l.label, color: l.color }; }),
            label: t.tr('HeaderLocations'),
            onLayersChange: showLayers
        });
        mapHost.appendChild(map.element);
        countries = nectoModule.createDataTable({
            columns: [{ key: 'name', label: t.tr('HeaderCountry') }].concat(layers.map(function (l) {
                return { key: l.key, label: l.label, align: 'end' };
            })),
            rows: [],
            rowKey: 'code',
            selectable: true,
            label: t.tr('HeaderCountries'),
            emptyText: t.tr('DashboardEmptyCountries')
        });
        tableHost.appendChild(countries.element);
        nectoModule.linkWorldMap(map, countries, { field: 'code', rowKey: 'code' });
    }

    // Draws the map for the tools that are on. A tool that is off takes its layers with it, and the viewer's own
    // choice of layers stays until the tools change.
    function drawMap(shared, data) {
        mapData = data;
        layers = mapLayers(shared.t, data);
        var keys = layers.map(function (l) { return l.key; }).join();
        if (keys !== layerSet) {
            layerSet = keys;
            buildLayers(shared.t);
        }
        map.setCounts(mapCounts(data, layers));
        shared.setVisible(mapNote, !data.LocationLoaded);
        attribution.textContent = data.Attribution || '';
        showLayers(map.getActive());
    }

    function buildShare(t) {
        var body = nectoModule.el('div', { class: 'necto-dashboard-share' });
        shareNote = nectoModule.el('div', { class: 'fieldDescription', text: t.tr('DashboardShareNote') });
        shareHomeOnly = nectoModule.el('div', { class: 'stolon-warn stolon-hidden', text: t.tr('DashboardShareHomeOnly') });
        qr = nectoModule.createQrCode({ label: t.tr('DashboardShareQrLabel'), fileName: 'jellyfin-qr.png' });
        shareButton = nectoModule.el('button', { is: 'emby-button', type: 'button', class: 'raised stolon-button secondary small stolon-hidden' }, [
            nectoModule.el('span', { class: 'material-icons', 'aria-hidden': 'true', text: 'share' }),
            nectoModule.el('span', { text: t.tr('Share') })
        ]);
        shareButton.addEventListener('click', function () {
            navigator.share({ title: 'Jellyfin', url: qr.getValue() }).catch(function () { /* closed without sharing */ });
        });
        body.appendChild(shareNote);
        body.appendChild(shareHomeOnly);
        body.appendChild(qr.element);
        body.appendChild(nectoModule.el('div', { class: 'stolon-button-row' }, [shareButton]));
        share = nectoModule.createCollapsibleSection({ title: t.tr('DashboardHeaderShare'), icon: 'qr_code_2', collapsible: false, content: body });
        getEl('dashboardShare').appendChild(share.element);
    }

    // Shows the address people use from the internet. The card stays hidden until the proxy has a name.
    function drawShare(shared, answer) {
        var t = shared.t;
        var url = (answer && answer.Url) || '';
        shared.setVisible('dashboardShare', !!url);
        if (!url) return;
        qr.setValue(url);
        share.setBadge(answer.HomeOnly ? { label: t.tr('DashboardBadgeHomeOnly'), color: 'orange' } : { label: t.tr('DashboardBadgePublic'), color: 'green' });
        shared.setVisible(shareNote, !answer.HomeOnly);
        shared.setVisible(shareHomeOnly, !!answer.HomeOnly);
        shared.setVisible(shareButton, typeof navigator !== 'undefined' && typeof navigator.share === 'function');
    }

    function buildActivity(t) {
        timeline = nectoModule.createTimeline({ items: [], emptyText: t.tr('DashboardEmptyActivity'), maxHeight: '32rem' });
        var section = nectoModule.createCollapsibleSection({ title: t.tr('DashboardHeaderActivity'), icon: 'history', collapsible: false, content: timeline.element });
        getEl('dashboardActivity').appendChild(section.element);
    }

    function bind(shared) {
        var t = shared.t;
        buildMap(t);
        buildShare(t);
        buildActivity(t);
    }

    // Reads the map and the activity again, which change while the page is open. The cards are read on each show,
    // since some of their routes check disks or look names up.
    function refresh(shared) {
        var load = loads;
        var mapOn = !getEl('dashboardMap').classList.contains('stolon-hidden');
        return Promise.all([mapOn ? read(shared, 'Dashboard/Map') : Promise.resolve(null), read(shared, 'Dashboard/Activity')]).then(function (answers) {
            if (load !== loads) return;
            if (answers[0]) drawMap(shared, answers[0]);
            timeline.setItems(activityItems(answers[1], shared));
        });
    }

    function load(shared) {
        var load = ++loads;
        var t = shared.t;
        var status = getEl('dashboardStatus');
        if (status) status.textContent = '';
        shared.getConfig().then(function (config) {
            if (load !== loads) return null;
            var keys = toolsOn(config);
            var mapOn = showsMap(config);
            shared.setVisible('dashboardMap', mapOn);
            return Promise.all([
                Promise.all(keys.map(function (key) { return CARD_READS[key] ? read(shared, CARD_READS[key]) : Promise.resolve(null); })),
                mapOn ? read(shared, 'Dashboard/Map') : Promise.resolve(null),
                config.Proxy && config.Proxy.Enabled ? read(shared, 'Proxy/PublicAddress') : Promise.resolve(null),
                read(shared, 'Dashboard/Activity')
            ]).then(function (answers) {
                if (load !== loads) return;
                var now = new Date();
                drawCards(keys.map(function (key, i) { return cardFor(key, t, shared, config, answers[0][i], answers[1], now); }));
                if (answers[1]) drawMap(shared, answers[1]);
                drawShare(shared, answers[2]);
                timeline.setItems(activityItems(answers[3], shared));
            });
        }).catch(function () {
            if (load !== loads) return;
            clearCards();
            if (status) status.textContent = t.tr('DashboardErrorLoad');
        });
    }
}
