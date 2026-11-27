// necto_dashboard.test.mjs
// Tests for the Dashboard: the status card of each tool that is on, the world map's layers and counts for the proxy
// and WireGuard, the country table, Necto's recent activity, and the page drawing them.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { installPage, loadScript, pageView, settle, routeApi, texts, strings } from './support.mjs';

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

    assert.deepEqual(dashboard.toolsOn({}), ['requests', 'invites', 'groups', 'ddns', 'scripts', 'pages', 'storage', 'notifications']);
    assert.deepEqual(dashboard.toolsOn({ Issues: { Enabled: true }, Proxy: { Enabled: true }, WireGuard: { Enabled: true } }),
        ['requests', 'issues', 'invites', 'groups', 'proxy', 'ddns', 'wireguard', 'scripts', 'pages', 'storage', 'notifications']);
    assert.equal(dashboard.showsMap({}), false);
    assert.equal(dashboard.showsMap({ Proxy: { Enabled: true } }), true);
    assert.equal(dashboard.showsMap({ WireGuard: { Enabled: true } }), true);
});

test('the Requests card counts those waiting for approval, and failed requests win over pending ones', async function () {
    installPage({});
    var dashboard = await loadScript('necto_dashboard.js');

    var card = dashboard.requestsCard(t, { ByStatus: { Pending: 3, Approved: 2, Available: 5, Failed: 1 } });
    assert.equal(card.value, 3);
    assert.equal(card.status, 'Failed');
    assert.equal(card.statusColor, 'red');
    assert.equal(card.detail, '6 open, 1 failed');
    assert.equal(card.tool, 'requests');
    assert.deepEqual(card.bars.map(function (b) { return b.value; }), [3, 2, 5, 1]);
    assert.equal(dashboard.requestsCard(t, { ByStatus: { Pending: 1 } }).statusColor, 'orange');
    assert.equal(dashboard.requestsCard(t, { ByStatus: {} }).status, 'Idle');
});

test('a card whose route could not be read says so and still opens its tool', async function () {
    installPage({});
    var dashboard = await loadScript('necto_dashboard.js');

    ['requests', 'issues', 'invites', 'proxy', 'ddns', 'wireguard', 'scripts', 'storage', 'notifications'].forEach(function (key) {
        var card = dashboard.cardFor(key, t, shared, {}, null, null, NOW);
        assert.equal(card.status, 'Error', key);
        assert.equal(card.statusColor, 'red', key);
        assert.equal(card.value, '—', key);
        assert.equal(card.tool, key, key);
    });
    assert.equal(dashboard.cardFor('invites', t, shared, {}, {}, null, NOW).status, 'Error');
    assert.equal(dashboard.cardFor('requests', t, shared, {}, { ByStatus: null }, null, NOW).status, 'Idle');
});

test('the Issues and Invites cards count what is open and active', async function () {
    installPage({});
    var dashboard = await loadScript('necto_dashboard.js');

    var issues = dashboard.issuesCard(t, { Reported: 2, Confirmed: 1, Resolved: 4, Declined: 1, Rescinded: 1 });
    assert.equal(issues.value, 3);
    assert.equal(issues.status, 'Open');
    assert.equal(issues.detail, '2 reported, 1 confirmed');
    assert.equal(issues.bars[3].value, 2);
    assert.equal(dashboard.issuesCard(t, { Resolved: 4 }).status, 'Idle');

    var invites = dashboard.invitesCard(t, [{ State: 'Active' }, { State: 'Active' }, { State: 'Expired' }, { State: 'GroupExpired' }, { State: 'UsedUp' }]);
    assert.equal(invites.value, 2);
    assert.equal(invites.detail, '2 of 5 invites active');
    assert.deepEqual(invites.segments.map(function (s) { return s.value; }), [2, 0, 2, 1]);
    assert.equal(dashboard.invitesCard(t, []).status, 'Idle');
});

test('the Groups and Pages cards read the configuration', async function () {
    installPage({});
    var dashboard = await loadScript('necto_dashboard.js');
    var config = {
        Groups: [{ MemberIds: ['A', 'b'] }, { MemberIds: ['a'] }],
        CustomPages: { Pages: [{ Enabled: true }, { Enabled: false }, {}] }
    };

    var groups = dashboard.groupsCard(t, config);
    assert.equal(groups.value, 2);
    assert.equal(groups.detail, '2 members in groups');
    assert.equal(dashboard.groupsCard(t, { Groups: [{ MemberIds: ['a'] }] }).detail, '1 member in a group');
    var pages = dashboard.pagesCard(t, config);
    assert.equal(pages.value, 2);
    assert.equal(pages.detail, '2 of 3 pages on');
});

test('the Proxy card shows whether it runs and asks for attention as its first certificate nears expiry', async function () {
    installPage({});
    var dashboard = await loadScript('necto_dashboard.js');
    var route = function (host, active, notAfter, error) {
        return { Host: host, Active: active, Certificate: { Loaded: !!notAfter, NotAfterUtc: notAfter, Error: error || '' } };
    };

    var card = dashboard.proxyCard(t, shared, { Running: true, Routes: [route('a.example.com', true, days(60)), route('b.example.com', false, days(30))] }, NOW);
    assert.equal(card.value, 1);
    assert.equal(card.status, 'Running');
    assert.equal(card.detail, 'Next certificate expires ' + new Date(days(30)).toLocaleDateString());
    assert.deepEqual(card.dots, [{ label: 'a.example.com', color: 'green' }, { label: 'b.example.com', color: 'gray' }]);

    var soon = dashboard.proxyCard(t, shared, { Running: true, Routes: [route('a.example.com', true, days(5))] }, NOW);
    assert.equal(soon.status, 'Needs attention');
    assert.equal(soon.statusColor, 'orange');
    var expired = dashboard.proxyCard(t, shared, { Running: true, Routes: [route('a.example.com', true, days(-1))] }, NOW);
    assert.equal(expired.status, 'Expired');
    assert.match(expired.detail, /^A certificate expired/);
    assert.equal(dashboard.proxyCard(t, shared, { Running: false, Routes: [] }, NOW).status, 'Stopped');
    var failed = dashboard.proxyCard(t, shared, { Running: false, Error: 'Port 443 is in use.', Routes: [route('a.example.com', true, null, 'Bad key')] }, NOW);
    assert.equal(failed.status, 'Failed');
    assert.equal(failed.detail, 'Port 443 is in use.');
    assert.equal(failed.dots[0].color, 'red');
    assert.equal(dashboard.proxyCard(t, shared, { Running: true, Routes: [] }, NOW).detail, 'No certificates loaded');
});

test('the DDNS card puts a dot per record and per name it follows, in the color of its last result', async function () {
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
    assert.equal(card.value, 3);
    assert.deepEqual(card.dots.map(function (d) { return d.label; }), ['home.example.com', 'a.example.com', 'b.example.com', 'off.example.com']);
    assert.equal(card.dots[3].color, 'gray');
    assert.equal(card.dots[0].color, 'green');
    assert.equal(card.detail, '2 of 3 records up to date');
    var empty = dashboard.ddnsCard(t, { Records: [] });
    assert.equal(empty.value, 0);
    assert.equal(empty.detail, 'No records yet');
});

test('the WireGuard card counts the connected peers and connections, and a failed tunnel turns it red', async function () {
    installPage({});
    var dashboard = await loadScript('necto_dashboard.js');
    var map = { Tunnels: [
        { Kind: 'Host', Name: 'Host', State: 'Running', Connected: true },
        { Kind: 'Peer', Name: 'Phone', State: 'Connected', Connected: true },
        { Kind: 'Peer', Name: 'Laptop', State: 'OverLimit', Connected: false },
        { Kind: 'Connection', Name: 'Office', State: 'Offline', Connected: false }
    ] };

    var card = dashboard.wireGuardCard(t, map);
    assert.equal(card.value, 1);
    assert.equal(card.detail, '1 of 3 tunnels connected');
    assert.equal(card.status, 'Connected');
    assert.deepEqual(card.dots.map(function (d) { return d.color; }), ['green', 'orange', 'gray']);
    map.Tunnels[3].State = 'Failed';
    assert.equal(dashboard.wireGuardCard(t, map).status, 'Failed');
    assert.equal(dashboard.wireGuardCard(t, { Tunnels: [{ Kind: 'Host', State: 'Running' }] }).status, 'Running');
    assert.equal(dashboard.wireGuardCard(t, { Tunnels: [] }).status, 'Offline');
});

test('the Scripts, Storage, and Notifications cards show failures first', async function () {
    installPage({});
    var dashboard = await loadScript('necto_dashboard.js');

    var scripts = dashboard.scriptsCard(t, { Days: ['a', 'b'], Succeeded: [3, 1], Failed: [0, 2] });
    assert.equal(scripts.value, 2);
    assert.equal(scripts.detail, '6 runs, 2 failed in 14 days');
    assert.deepEqual(scripts.sparkline, [3, 3]);
    assert.equal(scripts.sparklineColor, 'red');
    assert.equal(dashboard.scriptsCard(t, { Succeeded: [1], Failed: [0] }).status, 'Healthy');
    assert.equal(dashboard.scriptsCard(t, { Succeeded: [], Failed: [] }).status, 'Idle');

    var storage = dashboard.storageCard(t, [
        { Name: 'media', State: 'Available', InUse: true, Health: 'Good', Level: 'None' },
        { Name: 'backup', State: 'Missing', InUse: true }
    ]);
    assert.equal(storage.value, 2);
    assert.equal(storage.detail, '1 of 2 disks need attention');
    assert.equal(storage.statusColor, 'red');
    assert.deepEqual(storage.dots.map(function (d) { return d.color; }), ['green', 'red']);
    assert.equal(dashboard.storageCard(t, [{ Name: 'media', State: 'Available', InUse: true }]).status, 'Healthy');

    var sends = dashboard.notificationsCard(t, [{ Ok: true }, { Ok: false }, { Ok: true }]);
    assert.equal(sends.value, 3);
    assert.equal(sends.detail, '2 sent, 1 failed');
    assert.equal(sends.statusColor, 'orange');
    assert.equal(dashboard.notificationsCard(t, [{ Ok: false }]).status, 'Failed');
    assert.equal(dashboard.notificationsCard(t, []).status, 'Idle');
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

    var rows = dashboard.countryRows(map, layers, ['visitors', 'blocked'], function (code) { return code === 'DE' ? 'Germany' : ''; });
    assert.deepEqual(rows.map(function (r) { return [r.code, r.name, r.total]; }), [['DE', 'Germany', 7], ['US', 'United States', 4]]);
    assert.deepEqual(dashboard.countryRows(map, layers, ['failed'], function () { return ''; }).map(function (r) { return r.code; }), ['US']);
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
    assert.equal(items[1].status, 'blue');
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
    var opened = await openDashboard({ Groups: [] }, {
        'GET Dashboard/Activity': [{ Date: days(0), Name: 'Group created', Type: 'Necto.GroupCreated', Severity: 'Information' }]
    });

    var titles = texts(opened.view, '#dashboardCards .stolon-status-card-title');
    assert.deepEqual(titles, ['Requests', 'Invites', 'Groups', 'DDNS', 'Scripts', 'Pages', 'Storage', 'Notifications']);
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
    ['Country', 'Visitors', 'Failed sign-ins', 'Refused', 'This server'].forEach(function (label) { assert.ok(headings.indexOf(label) >= 0, label); });
    assert.equal(headings.indexOf('Peers'), -1);
    assert.match(mapHost.textContent, /United States/);
    assert.match(mapHost.textContent, /IP geolocation by DB-IP/);
    var shareHost = opened.view.querySelector('#dashboardShare');
    assert.equal(shareHost.classList.contains('stolon-hidden'), false);
    assert.match(shareHost.textContent, /Public/);
    assert.ok(texts(opened.view, '#dashboardCards .stolon-status-card-title').indexOf('Proxy') >= 0);
});
