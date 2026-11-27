// necto_dashboard.test.mjs
// Tests for the Dashboard: the status card of each tool that is on, the world map's layers and counts for the proxy
// and WireGuard, the country table, Necto's recent activity, and the page drawing them.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { installPage, loadScript, pageView, settle, routeApi, texts, strings, allToolsOn } from './support.mjs';

globalThis.setTimeout = function () { return 1; };
globalThis.clearTimeout = function () {};
globalThis.setInterval = function () { return 1; };
globalThis.clearInterval = function () {};

// t
// Reads the plugin's US English strings as a page does.
var t = {
    tr: function (key) { return strings[key] === undefined ? key : strings[key]; },
    format: function (key) {
        var args = Array.prototype.slice.call(arguments, 1);
        return t.tr(key).replace(/\{(\d+)\}/g, function (m, i) { return String(args[i]); });
    },
    plural: function (count, one, many) {
        return t.format.apply(null, [count === 1 ? one : many, count].concat(Array.prototype.slice.call(arguments, 3)));
    }
};

// shared
// The helpers the pure functions read.
var shared = { formatDate: function (value) { return 'on ' + new Date(value).toISOString().slice(0, 10); } };

var NOW = new Date('2026-11-26T12:00:00Z');

// days
// A date a number of days from NOW, as the server writes it.
function days(n) {
    return new Date(NOW.getTime() + n * 86400000).toISOString();
}

test('a card is drawn for each tool that is on, in sidebar order, and the map shows while the proxy or WireGuard is on', async function () {
    installPage({});
    var dashboard = await loadScript('necto_dashboard.js');

    assert.deepEqual(dashboard.toolsOn({}), ['groups', 'notifications']);
    assert.deepEqual(dashboard.toolsOn({ Issues: { Enabled: true }, Proxy: { Enabled: true }, WireGuard: { Enabled: true } }),
        ['groups', 'issues', 'proxy', 'wireguard', 'notifications']);
    assert.deepEqual(dashboard.toolsOn(allToolsOn()),
        ['groups', 'requests', 'issues', 'invites', 'proxy', 'ddns', 'wireguard', 'scripts', 'pages', 'storage', 'notifications']);
    assert.equal(dashboard.showsMap({}), false);
    assert.equal(dashboard.showsMap({ Proxy: { Enabled: true } }), true);
    assert.equal(dashboard.showsMap({ WireGuard: { Enabled: true } }), true);
});

// parts
// Reads a card's bar as label and value pairs, for short asserts.
function parts(card) {
    return card.segments.map(function (s) { return s.label + ' ' + s.value; });
}

// shown
// Names the parts of a card's bar that hold anything, as its legend shows them, and checks the legend is on.
function shown(card) {
    assert.equal(card.legend, true);
    return card.segments.filter(function (s) { return s.value > 0; }).map(function (s) { return s.label + ' ' + s.value; }).join(', ');
}

test('every card states its pill in a Stolon badge word and color, and its line says when its bar is empty', async function () {
    installPage({});
    var dashboard = await loadScript('necto_dashboard.js');

    assert.deepEqual(dashboard.setState({}, t, 'pending'), { status: 'Pending', statusColor: 'purple' });
    assert.deepEqual(dashboard.setState({}, t, 'attention'), { status: 'Warning', statusColor: 'orange' });
    assert.deepEqual(dashboard.setState({}, t, 'offline'), { status: 'Offline', statusColor: 'red' });
    assert.deepEqual(dashboard.setState({}, t, 'idle'), { status: 'Idle', statusColor: 'gray' });
    assert.equal(dashboard.emptyLine(t, [{ label: 'Pending', value: 1 }, { label: 'Approved', value: 0 }], 'DashboardEmptyRequests'), '');
    assert.equal(dashboard.emptyLine(t, [{ label: 'Pending', value: 0 }], 'DashboardEmptyRequests'), 'No requests yet');
    assert.deepEqual(dashboard.tally([{ label: 'A', color: 'green' }, { label: 'B' }, { label: 'A', color: 'red' }]),
        [{ label: 'A', value: 2, color: 'green' }, { label: 'B', value: 1, color: 'gray' }]);
});

test('the Requests card counts those waiting for approval with a part per status as the Requests tool colors it', async function () {
    installPage({});
    var dashboard = await loadScript('necto_dashboard.js');

    var card = dashboard.requestsCard(t, { ByStatus: { Pending: 3, Approved: 2, Available: 5, Failed: 1 } });
    assert.equal(card.value, 3);
    assert.equal(card.status, 'Failed');
    assert.equal(card.statusColor, 'red');
    assert.equal(card.tool, 'requests');
    assert.deepEqual([card.detail, shown(card)], ['', 'Pending 3, Approved 2, Failed 1, Available 5']);
    assert.equal(card.segments[0].color, 'purple');
    var pending = dashboard.requestsCard(t, { ByStatus: { Pending: 1 } });
    assert.deepEqual([pending.status, pending.statusColor], ['Pending', 'purple']);
    assert.equal(dashboard.requestsCard(t, { ByStatus: { Approved: 1 } }).status, 'Active');
    var none = dashboard.requestsCard(t, { ByStatus: {} });
    assert.deepEqual([none.status, none.detail], ['Idle', 'No requests yet']);
});

test('a card whose route could not be read says so and still opens its tool', async function () {
    installPage({});
    var dashboard = await loadScript('necto_dashboard.js');

    ['groups', 'requests', 'issues', 'invites', 'proxy', 'ddns', 'wireguard', 'scripts', 'storage', 'notifications'].forEach(function (key) {
        var card = dashboard.cardFor(key, t, shared, {}, null, null, NOW);
        assert.equal(card.status, 'Error', key);
        assert.equal(card.statusColor, 'red', key);
        assert.equal(card.value, '—', key);
        assert.deepEqual(card.segments, [], key);
        assert.equal(card.tool, key, key);
    });
    assert.equal(dashboard.cardFor('invites', t, shared, {}, {}, null, NOW).status, 'Error');
    assert.equal(dashboard.cardFor('requests', t, shared, {}, { ByStatus: null }, null, NOW).status, 'Idle');
});

test('the Issues card counts the open reports, and the Invites card puts a used up invite in gray, not as active', async function () {
    installPage({});
    var dashboard = await loadScript('necto_dashboard.js');

    var issues = dashboard.issuesCard(t, { Reported: 2, Confirmed: 1, Resolved: 4, Declined: 1, Rescinded: 1 });
    assert.equal(issues.value, 3);
    assert.deepEqual([issues.status, issues.statusColor], ['Pending', 'purple']);
    assert.deepEqual(parts(issues), ['Reported 2', 'Confirmed 1', 'Resolved 4', 'Declined 1', 'Rescinded 1']);
    assert.equal(dashboard.issuesCard(t, { Resolved: 4 }).status, 'Idle');

    var invites = dashboard.invitesCard(t, [{ State: 'Active' }, { State: 'Active' }, { State: 'Expired' }, { State: 'GroupExpired' }, { State: 'UsedUp' }]);
    assert.equal(invites.value, 2);
    assert.equal(invites.status, 'Active');
    assert.deepEqual([invites.detail, shown(invites)], ['', 'Active 2, Expired 1, Group expired 1, Used up 1']);
    var usedUp = dashboard.invitesCard(t, [{ State: 'UsedUp' }]);
    assert.equal(usedUp.value, 0);
    assert.deepEqual([usedUp.detail, shown(usedUp)], ['', 'Used up 1']);
    assert.equal(usedUp.segments.filter(function (s) { return s.value; })[0].color, 'gray');
    assert.equal(usedUp.status, 'Idle');
    assert.equal(dashboard.invitesCard(t, []).detail, 'No invites yet');
});

test('the Groups card counts the groups and the users in one beside those only Everyone holds, leaving out administrators', async function () {
    installPage({});
    var dashboard = await loadScript('necto_dashboard.js');
    var config = { Groups: [{ Id: '5a1f0c7e-0000-4000-8000-000000000001' }, { Id: 'family' }], CustomPages: { Pages: [{ Enabled: true }, { Enabled: false }, {}] } };
    var users = [
        { IsAdministrator: true, GroupIds: [] },
        { GroupIds: ['family'] },
        { GroupIds: ['5a1f0c7e000040008000000000000001'] }
    ];

    var groups = dashboard.groupsCard(t, config, users);
    assert.equal(groups.value, 1);
    assert.deepEqual([groups.detail, shown(groups)], ['', 'Members 1, No group 1']);
    assert.equal(groups.status, 'Active');
    assert.equal(dashboard.groupsCard(t, config, []).detail, 'No users yet');
    var pages = dashboard.pagesCard(t, config);
    assert.equal(pages.value, 2);
    assert.deepEqual([pages.detail, shown(pages)], ['', 'Published 2, Unpublished 1']);
    assert.equal(dashboard.pagesCard(t, { CustomPages: { Pages: [{ Enabled: false }] } }).status, 'Off');
});

test('the Proxy card counts its active routes and asks for attention as its first certificate nears expiry', async function () {
    installPage({});
    var dashboard = await loadScript('necto_dashboard.js');
    var route = function (host, active, notAfter, error) {
        return { Host: host, Active: active, Certificate: { Loaded: !!notAfter, NotAfterUtc: notAfter, Error: error || '' } };
    };

    var card = dashboard.proxyCard(t, shared, { Running: true, Routes: [route('a.example.com', true, days(60)), route('b.example.com', false, days(30))] }, NOW);
    assert.equal(card.value, 1);
    assert.equal(card.status, 'Active');
    assert.deepEqual([card.detail, shown(card)], ['', 'Active 1, Off 1']);

    var soon = dashboard.proxyCard(t, shared, { Running: true, Routes: [route('a.example.com', true, days(5))] }, NOW);
    assert.deepEqual([soon.status, soon.statusColor], ['Warning', 'orange']);
    assert.equal(soon.detail, 'Next certificate expires ' + new Date(days(5)).toLocaleDateString());
    var expired = dashboard.proxyCard(t, shared, { Running: true, Routes: [route('a.example.com', true, days(-1))] }, NOW);
    assert.equal(expired.status, 'Failed');
    assert.match(expired.detail, /^A certificate expired/);
    var stopped = dashboard.proxyCard(t, shared, { Running: false, Routes: [] }, NOW);
    assert.deepEqual([stopped.status, stopped.statusColor, stopped.detail], ['Offline', 'red', 'No routes yet']);
    var failed = dashboard.proxyCard(t, shared, { Running: false, Error: 'Port 443 is in use.', Routes: [route('a.example.com', true, null, 'Bad key')] }, NOW);
    assert.equal(failed.status, 'Failed');
    assert.equal(failed.detail, 'Port 443 is in use.');
    assert.deepEqual(parts(failed), ['Active 0', 'Failed 1', 'Off 0']);
});

test('the DDNS card counts every record, an off record among them, and a part per last result', async function () {
    installPage({});
    var dashboard = await loadScript('necto_dashboard.js');
    var answer = {
        Records: [
            { Record: { Name: 'Home', Hostname: 'home.example.com', LastAction: 'Updated', LastCheckedUtc: days(0) }, Followed: [] },
            { Record: { Name: 'Proxy', FollowProxyHosts: true }, Followed: [
                { Hostname: 'a.example.com', LastAction: 'Failed', LastCheckedUtc: days(0) },
                { Hostname: 'b.example.com', LastAction: 'Updated', LastCheckedUtc: days(0) }
            ] },
            { Record: { Name: 'Off', Hostname: 'off.example.com', Enabled: false }, Followed: [] }
        ]
    };

    var card = dashboard.ddnsCard(t, answer);
    assert.equal(card.value, 4);
    assert.deepEqual(card.segments.map(function (s) { return s.color; }), ['green', 'red', 'gray']);
    assert.equal(card.segments[2].label, 'Off');
    assert.deepEqual([card.detail, shown(card)], ['', 'Up to date 2, Failed 1, Off 1']);
    assert.equal(card.status, 'Failed');

    var onlyOff = dashboard.ddnsCard(t, { Records: [{ Record: { Hostname: 'demo.example.com', Enabled: false, LastAction: '' }, Followed: [] }] });
    assert.equal(onlyOff.value, 1);
    assert.deepEqual([onlyOff.detail, shown(onlyOff)], ['', 'Off 1']);
    assert.equal(onlyOff.status, 'Off');
    var empty = dashboard.ddnsCard(t, { Records: [] });
    assert.deepEqual([empty.value, empty.detail, empty.status], [0, 'No records yet', 'Idle']);
});

test('the WireGuard card counts the connected peers and connections, and a failed tunnel turns it red', async function () {
    installPage({});
    var dashboard = await loadScript('necto_dashboard.js');
    var map = { Tunnels: [
        { Kind: 'Host', Name: 'Host', State: 'Running', Connected: true },
        { Kind: 'Peer', Name: 'Phone', State: 'Connected', Connected: true },
        { Kind: 'Peer', Name: 'Laptop', State: 'NotAllowed', Connected: false },
        { Kind: 'Connection', Name: 'Office', State: 'Offline', Connected: false }
    ] };

    var card = dashboard.wireGuardCard(t, map);
    assert.equal(card.value, 1);
    assert.equal(card.status, 'Active');
    assert.deepEqual(card.segments.map(function (s) { return s.color; }), ['green', 'orange', 'gray']);
    map.Tunnels[3].State = 'Failed';
    assert.equal(dashboard.wireGuardCard(t, map).status, 'Failed');
    assert.equal(dashboard.wireGuardCard(t, { Tunnels: [{ Kind: 'Host', State: 'Running' }] }).status, 'Active');
    var none = dashboard.wireGuardCard(t, { Tunnels: [] });
    assert.deepEqual([none.status, none.detail], ['Off', 'No peers or connections yet']);
});

test('the Scripts, Storage, and Notifications cards show failures first', async function () {
    installPage({});
    var dashboard = await loadScript('necto_dashboard.js');

    var scripts = dashboard.scriptsCard(t, { Days: ['a', 'b'], Succeeded: [3, 1], Failed: [0, 2] });
    assert.equal(scripts.value, 2);
    assert.deepEqual([scripts.detail, shown(scripts)], ['', 'Succeeded 4, Failed 2']);
    assert.equal(scripts.status, 'Failed');
    assert.equal(dashboard.scriptsCard(t, { Succeeded: [1], Failed: [0] }).status, 'Active');
    assert.equal(dashboard.scriptsCard(t, { Succeeded: [], Failed: [] }).detail, 'No runs in 14 days');

    var storage = dashboard.storageCard(t, [
        { Name: 'media', State: 'Available', InUse: true, Health: 'Good', Level: 'None' },
        { Name: 'backup', State: 'Missing', InUse: true },
        { Name: 'spare', State: 'Available', InUse: false, Level: 'None' }
    ]);
    assert.equal(storage.value, 2);
    assert.deepEqual(storage.segments.map(function (s) { return s.color; }), ['green', 'red']);
    assert.deepEqual([storage.status, storage.statusColor], ['Warning', 'orange']);
    assert.equal(dashboard.storageCard(t, [{ Name: 'media', State: 'Available', InUse: true }]).status, 'Active');
    assert.equal(dashboard.storageCard(t, [{ Name: 'spare', State: 'Available', InUse: false }]).detail, 'No disks in use');

    var sends = dashboard.notificationsCard(t, [{ Ok: true }, { Ok: false }, { Ok: true }]);
    assert.equal(sends.value, 3);
    assert.deepEqual([sends.detail, shown(sends)], ['', 'Sent 2, Failed 1']);
    assert.equal(sends.statusColor, 'orange');
    assert.equal(dashboard.notificationsCard(t, [{ Ok: false }]).status, 'Failed');
    assert.equal(dashboard.notificationsCard(t, []).detail, 'No sends yet');
});

test('the map keeps only the layers of the tools that are on, and counts the server as one', async function () {
    installPage({});
    var dashboard = await loadScript('necto_dashboard.js');
    var map = {
        Proxy: true,
        WireGuard: false,
        Countries: [
            { CountryCode: 'US', Country: 'United States', Visitors: 4, Failed: 1, Blocked: 0, Peers: 0, Connections: 0, Server: true },
            { CountryCode: 'DE', Country: '', Visitors: 0, Failed: 0, Blocked: 7, Peers: 0, Connections: 0, Server: false }
        ]
    };

    var layers = dashboard.mapLayers(t, map);
    assert.deepEqual(layers.map(function (l) { return l.key; }), ['visitors', 'failed', 'blocked', 'server']);
    assert.deepEqual(layers.map(function (l) { return l.label; }), ['Visitors', 'Failed sign-ins', 'Refused', 'This server']);
    assert.deepEqual(dashboard.mapCounts(map, layers), { US: { visitors: 4, failed: 1, server: 1 }, DE: { blocked: 7 } });
    assert.deepEqual(dashboard.mapLayers(t, { WireGuard: true }).map(function (l) { return l.key; }), ['peers', 'connections', 'server']);
    assert.deepEqual(dashboard.mapLayers(t, { Proxy: { Enabled: false }, WireGuard: { Enabled: true } }).map(function (l) { return l.key; }), ['peers', 'connections', 'server']);
    assert.deepEqual(dashboard.placeholderRow(dashboard.mapLayers(t, { WireGuard: true })), { code: '', name: '–', peers: '–', connections: '–' });

    var rows = dashboard.countryRows(map, layers, ['visitors', 'blocked'], function (code) { return code === 'DE' ? 'Germany' : ''; });
    assert.deepEqual(rows.map(function (r) { return [r.code, r.name, r.total]; }), [['DE', 'Germany', 7], ['US', 'United States', 4]]);
    assert.deepEqual(dashboard.countryRows(map, layers, ['failed'], function () { return ''; }).map(function (r) { return r.code; }), ['US']);

    // Until the location database is loaded the table keeps its dashes rather than saying it has no locations.
    var loading = { LocationLoaded: false, Countries: [] };
    assert.deepEqual(dashboard.tableRows(loading, layers, ['visitors'], function () { return ''; }), [dashboard.placeholderRow(layers)]);
    assert.deepEqual(dashboard.tableRows({ LocationLoaded: true, Countries: [] }, layers, ['visitors'], function () { return ''; }), []);
    assert.equal(dashboard.tableRows(Object.assign({ LocationLoaded: false }, map), layers, ['visitors'], function () { return ''; }).length, 1);
    assert.equal(dashboard.tableRows(Object.assign({ LocationLoaded: false }, map), layers, ['visitors'], function () { return ''; })[0].code, 'US');
});

test('recent activity names each entry by its tool and marks errors and warnings', async function () {
    installPage({});
    var dashboard = await loadScript('necto_dashboard.js');

    assert.equal(dashboard.toolOfType('Necto.Proxy.AddressBlocked'), 'proxy');
    assert.equal(dashboard.toolOfType('Necto.proxy.banned'), 'proxy');
    assert.equal(dashboard.toolOfType('Requests.Approved'), 'requests');
    assert.equal(dashboard.toolOfType('Requests.IssueReported'), 'issues');
    assert.equal(dashboard.toolOfType('WireGuard'), 'wireguard');
    assert.equal(dashboard.toolOfType('Storage.DiskMissing'), 'storage');
    assert.equal(dashboard.toolOfType('Scripts.Script'), 'scripts');
    assert.equal(dashboard.toolOfType('Necto.GroupCreated'), 'groups');
    assert.equal(dashboard.toolOfType('SessionStarted'), null);

    var items = dashboard.activityItems([
        { Date: days(0), Name: 'Disk missing', Overview: 'media is missing', Type: 'Storage.DiskMissing', Severity: 'Error' },
        { Date: days(-1), Name: 'Something', Type: 'Other', Severity: 'Information' }
    ], shared);
    assert.deepEqual(items[0], { time: 'on ' + days(0).slice(0, 10), datetime: days(0), title: 'Disk missing', body: 'media is missing', status: 'red', icon: 'storage' });
    assert.equal(items[1].status, undefined);
    assert.equal(items[1].icon, undefined);
    assert.equal(items[1].body, undefined);
});

// openDashboard
// Shows the Dashboard over a view with its markup's holders and the routes answered.
async function openDashboard(config, answers) {
    var page = installPage({ config: config });
    var dom = page.dom;
    globalThis.getComputedStyle = function () { return { getPropertyValue: function () { return '#888888'; } }; };
    var ids = ['dashboardStatus', 'dashboardCards', 'dashboardMap', 'dashboardShare', 'dashboardActivity'];
    var view = pageView(dom, ids.map(function (id) { return dom.el('div', { id: id, class: id === 'dashboardMap' || id === 'dashboardShare' ? 'stolon-hidden' : '' }); }));
    dom.document.documentElement.appendChild(view);
    routeApi(page, answers || {});
    var mod = await loadScript('necto_dashboard.js');
    mod.default(view);
    view.dispatchEvent(new globalThis.CustomEvent('viewshow'));
    await settle();
    return { page: page, view: view };
}

test('the page draws a card per tool that is on and hides the map and share card while the proxy and WireGuard are off', async function () {
    var config = Object.assign(allToolsOn(), { Groups: [], Issues: { Enabled: false }, Proxy: { Enabled: false }, WireGuard: { Enabled: false } });
    var opened = await openDashboard(config, {
        'GET Dashboard/Activity': [{ Date: days(0), Name: 'Group created', Type: 'Necto.GroupCreated', Severity: 'Information' }]
    });

    var titles = texts(opened.view, '#dashboardCards .stolon-status-card-title');
    assert.deepEqual(titles, ['Groups', 'Requests', 'Invites', 'DDNS', 'Scripts', 'Pages', 'Storage', 'Notifications']);
    assert.ok(opened.view.querySelector('#dashboardMap').classList.contains('stolon-hidden'));
    assert.ok(opened.view.querySelector('#dashboardShare').classList.contains('stolon-hidden'));
    var urls = opened.page.requests.map(function (r) { return r.url; });
    assert.equal(urls.indexOf('Dashboard/Map'), -1);
    assert.equal(urls.indexOf('Proxy/PublicAddress'), -1);
    assert.match(opened.view.querySelector('#dashboardActivity').textContent, /Group created/);
});

test('with the proxy on the page shows the map, the country table, and the share card with its address', async function () {
    var opened = await openDashboard({ Proxy: { Enabled: true } }, {
        'GET Dashboard/Map': { Proxy: true, WireGuard: false, LocationLoaded: true, Attribution: 'IP geolocation by DB-IP', Countries: [
            { CountryCode: 'US', Country: 'United States', Visitors: 4, Failed: 0, Blocked: 0, Peers: 0, Connections: 0, Server: true }
        ], Tunnels: [] },
        'GET Proxy/PublicAddress': { Url: 'https://media.example.com', HomeOnly: false },
        'GET Proxy/Status': { Running: true, Routes: [] }
    });

    var mapHost = opened.view.querySelector('#dashboardMap');
    assert.equal(mapHost.classList.contains('stolon-hidden'), false);
    var layerButtons = mapHost.querySelectorAll('.stolon-world-layer');
    assert.deepEqual(Array.prototype.map.call(layerButtons, function (b) { return b.getAttribute('data-value'); }), ['visitors', 'failed', 'blocked', 'server']);
    var headings = texts(mapHost, '.stolon-data-heading').join('|');
    ['Country', 'Visitors', 'Failed sign-ins', 'Refused'].forEach(function (label) { assert.ok(headings.indexOf(label) >= 0, label); });
    assert.equal(headings.indexOf('This server'), -1);
    assert.equal(headings.indexOf('Peers'), -1);
    assert.match(mapHost.textContent, /United States/);
    assert.match(mapHost.textContent, /IP geolocation by DB-IP/);
    var shareHost = opened.view.querySelector('#dashboardShare');
    assert.equal(shareHost.classList.contains('stolon-hidden'), false);
    assert.match(shareHost.textContent, /Public/);
    assert.ok(texts(opened.view, '#dashboardCards .stolon-status-card-title').indexOf('Proxy') >= 0);
});
