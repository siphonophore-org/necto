// necto_dashboard.js
// The Dashboard tool: a status card for each tool that is on, one world map of the proxy's visitors and the WireGuard
// tunnel ends, the share card, and Necto's recent activity. A card opens the tool it sums up. The page only reads.

import * as nectoModule from './configurationpage?name=necto_shared.js';
import { stateOf as diskState } from './configurationpage?name=necto_storage.js';
import { resultTag as recordTag } from './configurationpage?name=necto_ddns.js';
import { statCards as requestCards } from './configurationpage?name=necto_requests.js';
import { statCards as issueCards } from './configurationpage?name=necto_issues.js';
import { stateCards as inviteCards } from './configurationpage?name=necto_invites.js';
import { stateTag as tunnelTag } from './configurationpage?name=necto_wireguard_common.js';
import { EVERYONE_ID, sameId } from './configurationpage?name=necto_groups.js';

// DASH
// What a card shows in place of a value it could not read.
var DASH = '—';

// PLACEHOLDER
// What a table cell shows until its data arrives.
var PLACEHOLDER = '–';

// CERTIFICATE_WARNING_DAYS
// How close to its expiry a certificate makes the Proxy card ask for attention.
var CERTIFICATE_WARNING_DAYS = 14;

// CARD_STATES
// The state words a card's pill takes, each with its string key and the Stolon badge state that colors it.
var CARD_STATES = {
    failed: { key: 'LabelFailed', state: 'Failed' },
    error: { key: 'HeaderError', state: 'Error' },
    offline: { key: 'StateOffline', state: 'Offline' },
    attention: { key: 'LogLevel.Warning', state: 'Warning' },
    pending: { key: 'StatePending', state: 'Pending' },
    active: { key: 'StateActive', state: 'Active' },
    off: { key: 'Off', state: 'Off' },
    idle: { key: 'PriorityIdle', state: 'Idle' }
};

// MAP_LAYERS
// Each kind the map marks, with the tool that must be on for it to show, the count it reads from a country, its
// color, and the icon its column shows on a narrow table. The server's own country shows while either tool is on, as
// a mark on the map only, since a count of one says nothing a column would.
var MAP_LAYERS = [
    { key: 'visitors', tool: 'proxy', field: 'Visitors', label: 'HeaderVisitors', color: 'blue', icon: 'person' },
    { key: 'failed', tool: 'proxy', field: 'Failed', label: 'LabelFailed', color: 'red', icon: 'no_accounts' },
    { key: 'blocked', tool: 'proxy', field: 'Blocked', label: 'HeaderRefused', color: 'purple', icon: 'block' },
    { key: 'peers', tool: 'wireguard', field: 'Peers', label: 'HeaderPeers', color: 'green', icon: 'devices' },
    { key: 'connections', tool: 'wireguard', field: 'Connections', label: 'HeaderConnections', color: 'orange', icon: 'lan' },
    { key: 'server', tool: 'any', field: 'Server', label: 'DashboardLayerServer', color: 'yellow', icon: 'dns', column: false, outline: true }
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

// setState
// Gives a card its pill, in the word and the Stolon badge color of one of the card states.
//
// Inputs
// card [object]: The card.
// t [object]: The strings.
// state [string]: A key of CARD_STATES.
//
// Output
// [object]: The card.
export function setState(card, t, state) {
    var entry = CARD_STATES[state];
    card.status = t.tr(entry.key);
    card.statusColor = nectoModule.BADGE_STATES.stateColors[entry.state];
    return card;
}

// emptyLine
// The card's detail line, which says the card has nothing to count while every part of its bar is 0.
//
// Inputs
// t [object]: The strings.
// segments [object[]]: The bar's parts, each with a label and a value.
// emptyKey [string]: The key of the line shown while every part is 0.
//
// Output
// [string]: Such as "No requests yet", or empty while any part holds something.
export function emptyLine(t, segments, emptyKey) {
    return segments.some(function (s) { return s.value > 0; }) ? '' : t.tr(emptyKey);
}

// tally
// Counts items into bar parts by their label, keeping the first color and order each label is met in.
//
// Inputs
// tags [object[]]: One { label, color } per item.
//
// Output
// [object[]]: The parts, each { label, value, color }.
export function tally(tags) {
    var parts = [];
    tags.forEach(function (tag) {
        var part = parts.filter(function (p) { return p.label === tag.label; })[0];
        if (part) part.value++;
        else parts.push({ label: tag.label, value: 1, color: tag.color || 'gray' });
    });
    return parts;
}

// fromCountCards
// Turns a tool page's count cards into a card's bar, leaving out an untinted total such as Open.
//
// Inputs
// cards [object[]]: The count cards, each { label, count, color }.
//
// Output
// [object[]]: The bar's parts, each { label, value, color }.
export function fromCountCards(cards) {
    return cards.filter(function (c) { return !!c.color; }).map(function (c) { return { label: c.label, value: Number(c.count) || 0, color: c.color }; });
}

// barParts
// Keeps the parts of a card's bar that hold anything, so the bar and its legend never list a state at 0.
//
// Inputs
// segments [object[]]: The bar's parts, each with a label and a value.
//
// Output
// [object[]]: The parts whose value is above 0, in their order.
export function barParts(segments) {
    return (segments || []).filter(function (s) { return (Number(s.value) || 0) > 0; });
}

// showsLegend
// Whether a card lists its bar's parts under it. A bar of one part that says what the card's number already says,
// such as Published under "2 published", needs no legend.
//
// Inputs
// parts [object[]]: The bar's parts that hold anything, from barParts.
// valueLabel [string]: The word after the card's number, or empty.
//
// Output
// [boolean]: true when the legend adds something.
export function showsLegend(parts, valueLabel) {
    return parts.length > 0;
}

// withBar
// Gives a card its bar and legend from the parts that hold anything. With nothing to count it takes no bar, only the
// line that says so.
//
// Inputs
// card [object]: The card, with its valueLabel already set.
// t [object]: The strings.
// segments [object[]]: The bar's parts.
// emptyKey [string]: The key of the line shown while every part is 0.
//
// Output
// [object]: The card.
function withBar(card, t, segments, emptyKey) {
    var parts = barParts(segments);
    card.segments = parts.length ? parts : null;
    card.legend = showsLegend(parts, card.valueLabel);
    card.detail = emptyLine(t, parts, emptyKey);
    return card;
}

// failedCard
// Turns a card whose route could not be read into one that says so.
//
// Inputs
// t [object]: The strings.
// card [object]: The card's icon, title, and tool.
//
// Output
// [object]: The card, with a dash for its value, an empty bar, and a red status.
function failedCard(t, card) {
    return setState(Object.assign(card, { value: DASH, segments: null, legend: false, detail: t.tr('DashboardCardFailed') }), t, 'error');
}

// toolCard
// Starts a tool's card with the tool's own icon and name.
//
// Inputs
// key [string]: The tool's key, as nectoTools lists it, or notifications.
//
// Output
// [object]: The card's icon, title, tool, and open, the tool and the section a click opens.
function toolCard(key) {
    // Notifications has no page of its own, so its card opens its section of Settings.
    if (key === 'notifications') return { icon: 'notifications', title: nectoModule.toolName('TabNotifications', 'Notifications'), tool: key, open: ['settings', 'notifications'] };
    var tool = nectoModule.nectoTools().filter(function (x) { return x.key === key; })[0];
    return { icon: tool.icon, title: tool.name, tool: key, open: [key] };
}

// sum
// Adds up numbers.
//
// Inputs
// values [number[]]: The numbers.
//
// Output
// [number]: The total.
function sum(values) {
    return (values || []).reduce(function (total, n) { return total + (Number(n) || 0); }, 0);
}

// requestsCard
// Sums up the requests: those waiting for approval as the number, and a column per day of the last 14 as tall as the
// requests made on it. The line under the title counts the failed requests.
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
    var days = stats.Days || {};
    card.value = pending;
    card.valueLabel = t.tr('StatePending').toLowerCase();
    var total = Object.keys(by).reduce(function (n, k) { return n + (Number(by[k]) || 0); }, 0);
    // Requests made in the last 14 days show as a chart. With none made lately, the card shows its states as every
    // other card does, or the line that there are none.
    if (sum(days.Made) > 0) {
        card.bars = dayBars(days.Days, days.Made, null, function (day, made) { return t.format('DashboardRequestsOnDay', day, made); });
        card.segments = null;
        card.legend = false;
        card.detail = by.Failed > 0 ? t.format('ScheduledTaskFailedWithName', by.Failed) : '';
    } else {
        card.bars = null;
        withBar(card, t, fromCountCards(requestCards(stats, t)), 'DashboardEmptyRequests');
    }
    if (by.Failed > 0) return setState(card, t, 'failed');
    if (pending > 0) return setState(card, t, 'pending');
    if ((by.Approved || 0) + (by.PartiallyAvailable || 0) > 0) return setState(card, t, 'active');
    return setState(card, t, 'idle');
}

// issuesCard
// Sums up the issue reports: the open ones as the number, and a part per state as the Issues tool counts it.
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
    card.value = (stats.Reported || 0) + (stats.Confirmed || 0);
    card.valueLabel = t.tr('StateOpen').toLowerCase();
    withBar(card, t, fromCountCards(issueCards(stats, t)), 'DashboardEmptyIssues');
    return setState(card, t, card.value > 0 ? 'pending' : 'idle');
}

// invitesCard
// Sums up the invite links: the active ones as the number, and a part per state as the Invites tool counts it.
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
    var segments = fromCountCards(inviteCards(invites, t));
    var active = segments[0].value;
    card.value = active;
    card.valueLabel = t.tr('StateActive').toLowerCase();
    withBar(card, t, segments, 'DashboardEmptyInvites');
    return setState(card, t, active > 0 ? 'active' : 'idle');
}

// groupsCard
// Sums up the groups: how many there are besides Everyone as the number, and the users as members of a group, users
// in no group, and administrators, who are never in a group.
//
// Inputs
// t [object]: The strings.
// config [object]: The plugin configuration.
// users [object[]]: What Groups/Users answered, or null when it could not be read.
//
// Output
// [object]: The status card's options, with tool for the tool it opens.
export function groupsCard(t, config, users) {
    var card = toolCard('groups');
    if (!Array.isArray(users)) return failedCard(t, card);
    var admins = users.filter(function (u) { return u.IsAdministrator; }).length;
    var people = users.filter(function (u) { return !u.IsAdministrator; });
    var grouped = people.filter(function (u) { return (u.GroupIds || []).some(function (id) { return !sameId(id, EVERYONE_ID); }); }).length;
    card.value = ((config && config.Groups) || []).filter(function (g) { return !sameId(g.Id, EVERYONE_ID); }).length;
    card.valueLabel = t.tr('DashboardValueGroups');
    withBar(card, t, [
        { label: t.tr('HeaderMembers'), value: grouped, color: 'green' },
        { label: t.tr('ScriptsSummaryNoGroups'), value: people.length - grouped, color: 'gray' },
        { label: t.tr('LabelAdmins'), value: admins, color: 'blue' }
    ], 'DashboardEmptyUsers');
    return setState(card, t, grouped > 0 ? 'active' : 'idle');
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
// Sums up the proxy: its active routes as the number, a part per route state, and its first certificate to expire
// once that is close.
//
// Inputs
// t [object]: The strings.
// shared [object]: The helper bag.
// status [object]: What Proxy/Status answered, or null when it could not be read.
// now [Date]: The time now.
//
// Output
// [object]: The status card's options, with tool for the tool it opens.
// proxyAnswer
// Joins what Proxy/Status and Proxy/Routes/All answered into the status the Proxy card reads, with each route's state.
//
// Inputs
// answer [any]: Both answers, or one status, or null.
//
// Output
// [object]: The status with RouteStates, or null when the status could not be read.
export function proxyAnswer(answer) {
    if (!Array.isArray(answer)) return answer || null;
    var status = answer[0];
    if (!status) return null;
    var page = answer[1] || {};
    return Object.assign({}, status, { RouteStates: (page.Items || []).map(function (row) { return row.State; }) });
}

export function proxyCard(t, shared, status, now) {
    var card = toolCard('proxy');
    if (!status) return failedCard(t, card);
    // The route states the Proxy page shows, in its order and colors.
    var states = (status.RouteStates || []);
    var count = function (state) { return states.filter(function (s) { return s === state; }).length; };
    var segments = [
        { label: t.tr('ProxyStateHttps'), value: count('Https'), color: 'green' },
        { label: t.tr('ProxyStateHttp'), value: count('Http'), color: 'blue' },
        { label: t.tr('ProxyStateIncomplete'), value: count('Incomplete'), color: 'purple' },
        { label: t.tr('ProxyStateUntrusted'), value: count('Untrusted'), color: 'orange' },
        { label: t.tr('Off'), value: count('Off'), color: 'gray' }
    ];
    card.value = segments[0].value;
    card.valueLabel = t.tr('ProxyStateHttps');
    withBar(card, t, segments, 'DashboardEmptyRoutes');
    var soonest = soonestCertificate(status);
    var days = soonest ? (soonest - now) / 86400000 : null;
    if (status.Error) return setState(Object.assign(card, { detail: status.Error }), t, 'failed');
    if (!status.Running) return setState(card, t, 'offline');
    // The day is enough for a certificate, and keeps the line inside the card.
    if (days !== null && days < 0) return setState(Object.assign(card, { detail: t.format('DashboardCertificateExpired', soonest.toLocaleDateString()) }), t, 'failed');
    if (days !== null && days < CERTIFICATE_WARNING_DAYS) return setState(Object.assign(card, { detail: t.format('DashboardCertificateExpires', soonest.toLocaleDateString()) }), t, 'attention');
    if (segments[2].value + segments[3].value > 0) return setState(card, t, 'attention');
    return setState(card, t, segments[0].value + segments[1].value > 0 ? 'active' : 'idle');
}

// ddnsCard
// Sums up the DDNS records, counting every name a record follows: those up to date as the number, and a part per
// last result, with the records that are off as their own part.
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
        if (entry.Followed && entry.Followed.length) records = records.concat(entry.Followed.map(function (f) { return Object.assign({ Enabled: entry.Record.Enabled }, f); }));
        else records.push(entry.Record);
    });
    var off = { label: t.tr('Off'), color: 'gray' };
    // Every good result reads Up to date, so Updated and Up to date share one part. The warnings share a part, and so do
    // the errors, so each part of the bar has its own color.
    var good = { label: t.tr('DdnsResultUnchanged'), color: 'green' };
    var warned = { label: t.tr('LogLevel.Warning'), color: 'orange' };
    var failed = { label: t.tr('LabelFailed'), color: 'red' };
    var tags = records.map(function (r) {
        if (r.Enabled === false) return off;
        var tag = recordTag(r, t);
        if (!tag || tag.color === 'green') return good;
        if (tag.color === 'orange') return warned;
        if (tag.color === 'red') return failed;
        return tag;
    });
    var order = ['green', 'purple', 'orange', 'red', 'gray'];
    var segments = tally(tags.filter(function (tag) { return tag !== off; })).sort(function (a, b) { return order.indexOf(a.color) - order.indexOf(b.color); });
    var offCount = tags.filter(function (tag) { return tag === off; }).length;
    if (offCount) segments.push({ label: off.label, value: offCount, color: off.color });
    card.value = tags.filter(function (tag) { return tag === good; }).length;
    card.valueLabel = good.label.toLowerCase();
    withBar(card, t, segments, 'DashboardNoRecords');
    var colors = tags.map(function (tag) { return tag.color; });
    if (colors.indexOf('red') >= 0) return setState(card, t, 'failed');
    if (colors.indexOf('orange') >= 0) return setState(card, t, 'attention');
    if (records.length > offCount) return setState(card, t, 'active');
    return setState(card, t, records.length ? 'off' : 'idle');
}

// wireGuardCard
// Sums up WireGuard: the connected peers and connections as the number, and a part per tunnel state as the VPN tool
// tags it. A failed tunnel puts its error on the line under the title.
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
    var order = ['green', 'orange', 'yellow', 'pink', 'red', 'teal', 'gray'];
    var segments = tally(ends.map(function (e) {
        var tag = tunnelTag(t, e.State);
        return { label: tag.label, color: tag.color };
    })).sort(function (a, b) { return order.indexOf(a.color) - order.indexOf(b.color); });
    withBar(card, t, segments, 'DashboardEmptyTunnels');
    var failed = (host && host.State === 'Failed') ? host : ends.filter(function (e) { return e.State === 'Failed'; })[0];
    if (failed) {
        if (failed.Error) card.detail = failed.Error;
        return setState(card, t, 'failed');
    }
    if (connected > 0 || (host && host.State === 'Running')) return setState(card, t, 'active');
    return setState(card, t, 'off');
}

// scriptsCard
// Sums up the scripts: the runs of the last 14 days as the number, and a column per day as tall as its runs, red on a
// day with a failed run and green otherwise. The line under the title counts the failed runs.
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
    var ok = sum(days.Succeeded);
    var failed = sum(days.Failed);
    card.value = ok + failed;
    card.valueLabel = t.tr('DashboardValueRuns');
    card.bars = ok + failed > 0 ? dayBars(days.Days, days.Succeeded, days.Failed, function (day, runs, failed) { return t.format('DashboardRunsOnDay', day, runs, failed); }) : null;
    card.segments = null;
    card.legend = false;
    card.detail = failed > 0 ? t.format('ScheduledTaskFailedWithName', failed) : (ok > 0 ? '' : t.tr('DashboardEmptyRuns'));
    if (failed > 0) return setState(card, t, 'failed');
    return setState(card, t, ok > 0 ? 'active' : 'idle');
}

// RUN_DAYS
// How many days the Scripts, Requests, and Notifications cards chart.
var RUN_DAYS = 14;

// dayBars
// Turns counts per day into a card's columns, oldest first, always 14 of them so an idle fortnight still shows its
// baseline. A day with a failure is red and any other green.
//
// Inputs
// labels [string[]]: Each day as yyyy-MM-dd, oldest first.
// good [number[]]: What went well on each day.
// bad [number[]]: What failed on each day, or null when nothing can fail.
// label [function]: Names a column from its day, its total, and its failures.
//
// Output
// [object[]]: One { label, value, color } per day.
export function dayBars(labels, good, bad, label) {
    labels = (labels || []).slice(-RUN_DAYS);
    good = (good || []).slice(-RUN_DAYS);
    bad = (bad || []).slice(-RUN_DAYS);
    var count = Math.max(labels.length, good.length, bad.length);
    var bars = [];
    for (var i = 0; i < count; i++) {
        var failed = Number(bad[i]) || 0;
        var total = (Number(good[i]) || 0) + failed;
        bars.push({ label: label(labels[i] || '', total, failed), value: total, color: failed > 0 ? 'red' : 'green' });
    }
    while (bars.length < RUN_DAYS) bars.unshift({ label: '', value: 0, color: 'green' });
    return bars;
}

// sendDays
// Counts the send log by the viewer's local day over the last 14 days, the sends that went and those that failed.
//
// Inputs
// log [object[]]: The send log entries, each with Time in UTC and Ok.
// now [Date]: The time now.
//
// Output
// [object]: Days as yyyy-MM-dd, Sent, and Failed, oldest first, 14 entries each.
export function sendDays(log, now) {
    var out = { Days: [], Sent: [], Failed: [] };
    var start = new Date(now.getFullYear(), now.getMonth(), now.getDate() - (RUN_DAYS - 1));
    var pad = function (n) { return (n < 10 ? '0' : '') + n; };
    for (var i = 0; i < RUN_DAYS; i++) {
        var day = new Date(start.getFullYear(), start.getMonth(), start.getDate() + i);
        out.Days.push(day.getFullYear() + '-' + pad(day.getMonth() + 1) + '-' + pad(day.getDate()));
        out.Sent.push(0);
        out.Failed.push(0);
    }
    (log || []).forEach(function (entry) {
        var time = new Date(entry.Time);
        if (isNaN(time.getTime())) return;
        var local = new Date(time.getFullYear(), time.getMonth(), time.getDate());
        var index = Math.round((local - start) / 86400000);
        if (index < 0 || index >= RUN_DAYS) return;
        if (entry.Ok) out.Sent[index]++;
        else out.Failed[index]++;
    });
    return out;
}

// pagesCard
// Sums up the custom pages: the published ones as the number, beside the unpublished ones.
//
// Inputs
// t [object]: The strings.
// config [object]: The plugin configuration.
//
// Output
// [object]: The status card's options, with tool for the tool it opens.
export function pagesCard(t, config) {
    var card = toolCard('pages');
    var pages = (config && config.CustomPages && config.CustomPages.Pages) || [];
    var on = pages.filter(function (p) { return p.Enabled !== false; }).length;
    card.value = on;
    card.valueLabel = t.tr('PagesLabelPublished').toLowerCase();
    withBar(card, t, [
        { label: t.tr('PagesLabelPublished'), value: on, color: 'green' },
        { label: t.tr('PagesStatusUnpublished'), value: pages.length - on, color: 'gray' }
    ], 'DashboardEmptyPages');
    if (on > 0) return setState(card, t, 'active');
    return setState(card, t, pages.length ? 'off' : 'idle');
}

// ATTENTION_COLORS
// The disk state colors that ask for attention: missing, full, failing, unresponsive, or nearly full.
var ATTENTION_COLORS = ['red', 'purple', 'pink', 'yellow', 'teal', 'orange'];

// storageCard
// Sums up the disks Jellyfin uses, as the Storage tool counts them: how many there are, and a part per state.
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
    var used = disks.filter(function (d) { return d.InUse; });
    var states = used.map(function (d) { return diskState(t, d); });
    var order = ['green', 'orange', 'teal', 'yellow', 'red', 'purple', 'blue', 'gray'];
    card.value = used.length;
    card.valueLabel = t.tr('DashboardInUse');
    withBar(card, t, tally(states).sort(function (a, b) { return order.indexOf(a.color) - order.indexOf(b.color); }), 'DashboardEmptyDisks');
    var colors = states.map(function (s) { return s.color; });
    if (colors.some(function (c) { return ATTENTION_COLORS.indexOf(c) >= 0; })) return setState(card, t, 'attention');
    return setState(card, t, used.length ? 'active' : 'idle');
}

// notificationsCard
// Sums up the send log: the sends that went as the number, and a column per day of the last 14 as tall as its sends,
// red on a day with a failed send. The line under the title counts the failed sends.
//
// Inputs
// t [object]: The strings.
// log [object[]]: What Notifications/Log answered, newest first, or null when it could not be read.
// now [Date]: The time now. [optional]
//
// Output
// [object]: The status card's options, with tool for the tool it opens.
export function notificationsCard(t, log, now) {
    var card = toolCard('notifications');
    if (!Array.isArray(log)) return failedCard(t, card);
    var failed = log.filter(function (e) { return !e.Ok; }).length;
    var sent = log.length - failed;
    var days = sendDays(log, now || new Date());
    card.value = sent;
    card.valueLabel = t.tr('StateSent').toLowerCase();
    card.bars = log.length ? dayBars(days.Days, days.Sent, days.Failed, function (day, total, bad) { return t.format('DashboardSendsOnDay', day, total - bad, bad); }) : null;
    card.segments = null;
    card.legend = false;
    card.detail = failed > 0 ? t.format('ScheduledTaskFailedWithName', failed) : (log.length ? '' : t.tr('DashboardEmptySends'));
    if (log.length && !log[0].Ok) return setState(card, t, 'failed');
    if (failed > 0) return setState(card, t, 'attention');
    return setState(card, t, log.length ? 'active' : 'idle');
}

// toolsOn
// Lists the tools that get a card: every tool in the sidebar but the dashboard, Settings, and Troubleshooting, without
// each tool that is turned off, and Notifications last.
//
// Inputs
// config [object]: The plugin configuration.
//
// Output
// [string[]]: The tool keys, in sidebar order.
export function toolsOn(config) {
    var never = { dashboard: true, settings: true, troubleshooting: true };
    return nectoModule.nectoTools().map(function (tool) { return tool.key; }).filter(function (key) {
        return !never[key] && nectoModule.toolIsOn(config, key);
    }).concat(['notifications']);
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
// The route each card reads, by tool. Pages builds its card from the configuration, and WireGuard's card reads the
// map.
var CARD_READS = {
    groups: 'Groups/Users',
    requests: 'Requests/Stats',
    issues: 'Issues/Stats',
    invites: 'Invites/List',
    proxy: ['Proxy/Status', 'Proxy/Routes/All?skip=0&take=1000'],
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
        case 'groups': return groupsCard(t, config, answer);
        case 'proxy': return proxyCard(t, shared, proxyAnswer(answer), now);
        case 'ddns': return ddnsCard(t, answer);
        case 'wireguard': return wireGuardCard(t, map);
        case 'scripts': return scriptsCard(t, answer);
        case 'pages': return pagesCard(t, config);
        case 'storage': return storageCard(t, answer);
        default: return notificationsCard(t, answer, now);
    }
}

// mapLayers
// Lists the map's layers for the tools that are on.
//
// Inputs
// t [object]: The strings.
// map [object]: What Dashboard/Map answered, or the plugin configuration before the map is read.
//
// Output
// [object[]]: Each layer's key, label, color, icon, the country field it counts, and whether the table gives it a column.
export function mapLayers(t, map) {
    return MAP_LAYERS.filter(function (layer) {
        if (layer.tool === 'proxy') return !!(map.Proxy === true || (map.Proxy && map.Proxy.Enabled));
        if (layer.tool === 'wireguard') return !!(map.WireGuard === true || (map.WireGuard && map.WireGuard.Enabled));
        return true;
    }).map(function (layer) {
        return { key: layer.key, label: t.tr(layer.label), color: layer.color, field: layer.field, icon: layer.icon, column: layer.column !== false, outline: !!layer.outline };
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

// tableRows
// The country table's rows. While the location database is still loading and no country is known yet, the table
// keeps its row of dashes, so it never says it has no locations before it can know.
//
// Inputs
// map [object]: What Dashboard/Map answered.
// layers [object[]]: The layers, from mapLayers.
// active [string[]]: The keys of the layers that are on.
// nameOf [function]: Names a country by its code.
//
// Output
// [object[]]: The rows.
export function tableRows(map, layers, active, nameOf) {
    var rows = countryRows(map, layers, active, nameOf);
    return rows.length || map.LocationLoaded !== false ? rows : [placeholderRow(layers)];
}

// placeholderRow
// The country table's one row while the map is read, a dash in every column.
//
// Inputs
// layers [object[]]: The layers, from mapLayers.
//
// Output
// [object]: The row.
export function placeholderRow(layers) {
    var row = { code: '', name: PLACEHOLDER };
    layers.filter(function (layer) { return layer.column !== false; }).forEach(function (layer) { row[layer.key] = PLACEHOLDER; });
    return row;
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
// The timeline status of an entry's severity: red for errors and orange for warnings. Other entries take none.
var SEVERITY_STATUS = { Critical: 'red', Error: 'red', Warning: 'orange' };

// activityItems
// Turns Necto's activity log entries into timeline events, each with how long ago it happened, marked with its tool's
// icon, and with its severity when it is a warning or an error.
//
// Inputs
// entries [object[]]: What Dashboard/Activity answered, newest first.
// shared [object]: The helper bag, for formatRelativeTime.
//
// Output
// [object[]]: Timeline items of { time, datetime, title, body, status, icon }.
export function activityItems(entries, shared) {
    var icons = {};
    nectoModule.nectoTools().forEach(function (tool) { icons[tool.key] = tool.icon; });
    icons.notifications = 'notifications';
    return (Array.isArray(entries) ? entries : []).map(function (entry) {
        var tool = toolOfType(entry.Type);
        var item = {
            time: shared.formatRelativeTime(new Date(entry.Date)),
            datetime: entry.Date,
            title: entry.Name
        };
        if (SEVERITY_STATUS[entry.Severity]) item.status = SEVERITY_STATUS[entry.Severity];
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
        // Every card takes one width, so ten cards never read as two kinds, and at most two share a row, so each card
        // stays wide enough for its bar and legend.
        grid = nectoModule.createStatusCardGrid({ minWidth: 300, maxColumns: 2, even: true });
        getEl('dashboardCards').appendChild(grid.element);
        grid.setCards(cards.map(function (card) {
            var options = Object.assign({}, card);
            delete options.tool;
            delete options.open;
            options.onClick = function () { nectoModule.openTool(view, card.open[0], card.open[1]); };
            return options;
        }));
    }

    // Fills the country table for the layers that are on, each with its column.
    function showLayers(active) {
        if (!countries || !mapData) return;
        layers.filter(function (layer) { return layer.column; }).forEach(function (layer) { countries.setColumnHidden(layer.key, active.indexOf(layer.key) < 0); });
        countries.setRows(tableRows(mapData, layers, active, map.countryName));
    }

    function buildMap(t) {
        var body = nectoModule.h('div', { class: 'stolon-stack' });
        mapNote = nectoModule.h('div', { class: 'stolon-info stolon-hidden', text: t.tr('DashboardMapLoading') });
        mapHost = nectoModule.h('div');
        tableHost = nectoModule.h('div');
        attribution = nectoModule.h('a', { class: 'button-link', href: 'https://db-ip.com', target: '_blank', rel: 'noopener noreferrer' });
        body.appendChild(nectoModule.h('div', { class: 'fieldDescription', text: t.tr('DashboardMapHelp') }));
        body.appendChild(mapNote);
        body.appendChild(mapHost);
        body.appendChild(tableHost);
        body.appendChild(nectoModule.h('div', { class: 'fieldDescription' }, [attribution]));
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
            layers: layers.map(function (l) { return { key: l.key, label: l.label, color: l.color, outline: !!l.outline }; }),
            label: t.tr('HeaderLocations'),
            onLayersChange: showLayers
        });
        mapHost.appendChild(map.element);
        countries = nectoModule.createDataTable({
            // The country takes twice a count's share, so a name such as United States fits on a phone, and each count
            // turns into its icon heading on a narrow table.
            columns: [{ key: 'name', label: t.tr('HeaderCountry'), width: 'minmax(7rem, 2fr)' }].concat(layers.filter(function (l) { return l.column; }).map(function (l) {
                return { key: l.key, label: l.label, icon: l.icon, align: 'end', width: 'minmax(3rem, 1fr)' };
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

    // Builds the map and its table again when the layers change. The viewer's own choice of layers stays until then.
    function useLayers(t, next) {
        layers = next;
        var keys = layers.map(function (l) { return l.key; }).join();
        if (keys === layerSet) return false;
        layerSet = keys;
        buildLayers(t);
        return true;
    }

    // Shows the map's table with a dash in every column until the map is read.
    function prepareMap(shared, config) {
        if (useLayers(shared.t, mapLayers(shared.t, config)) || !mapData) countries.setRows([placeholderRow(layers)]);
    }

    // Draws the map for the tools that are on. A tool that is off takes its layers with it.
    function drawMap(shared, data) {
        mapData = data;
        useLayers(shared.t, mapLayers(shared.t, data));
        map.setCounts(mapCounts(data, layers));
        shared.setVisible(mapNote, !data.LocationLoaded);
        attribution.textContent = data.Attribution || '';
        showLayers(map.getActive());
    }

    function buildShare(t) {
        var body = nectoModule.h('div', { class: 'stolon-stack stolon-narrow' });
        shareNote = nectoModule.h('div', { class: 'fieldDescription', text: t.tr('DashboardShareNote') });
        shareHomeOnly = nectoModule.h('div', { class: 'stolon-warn stolon-hidden', text: t.tr('DashboardShareHomeOnly') });
        qr = nectoModule.createQrCode({ label: t.tr('DashboardShareQrLabel'), fileName: 'jellyfin-qr.png' });
        shareButton = nectoModule.h('button', { is: 'emby-button', type: 'button', class: 'raised stolon-button stolon-secondary stolon-hidden' }, [
            nectoModule.h('span', { class: 'material-icons', 'aria-hidden': 'true', text: 'share' }),
            nectoModule.h('span', { text: t.tr('Share') })
        ]);
        shareButton.addEventListener('click', function () {
            navigator.share({ title: 'Jellyfin', url: qr.getValue() }).catch(function () { /* closed without sharing */ });
        });
        body.appendChild(shareNote);
        body.appendChild(shareHomeOnly);
        body.appendChild(qr.element);
        body.appendChild(nectoModule.h('div', { class: 'stolon-button-row' }, [shareButton]));
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
        timeline = nectoModule.createTimeline({ items: [], emptyText: t.tr('DashboardEmptyActivity') });
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
        if (status) shared.setVisible(status, false);
        shared.getConfig().then(function (config) {
            if (load !== loads) return null;
            var keys = toolsOn(config);
            var mapOn = showsMap(config);
            shared.setVisible('dashboardMap', mapOn);
            if (mapOn) prepareMap(shared, config);
            return Promise.all([
                Promise.all(keys.map(function (key) {
                    var what = CARD_READS[key];
                    if (!what) return Promise.resolve(null);
                    return Array.isArray(what) ? Promise.all(what.map(function (route) { return read(shared, route); })) : read(shared, what);
                })),
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
            if (status) {
                status.textContent = t.tr('DashboardErrorLoad');
                shared.setVisible(status, true);
            }
        });
    }
}
