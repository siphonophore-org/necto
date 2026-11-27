// necto_wireguard.test.mjs
// Tests for WireGuard: the shared helpers, the tool's lists and counts, the Settings section with its wireguard-go
// download, the Troubleshooting section, and the WireGuard section of the group editor.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { installPage, loadScript, pageView, settle, routeApi, texts, clickButton, clickDialogButton, dialogText } from './support.mjs';

globalThis.setTimeout = function () { return 1; };
globalThis.clearTimeout = function () {};
globalThis.setInterval = function () { return 1; };
globalThis.clearInterval = function () {};

var VIEWER = 'aaaaaaaa-0000-0000-0000-000000000001';
var PHONE = 'bbbbbbbb-0000-0000-0000-000000000001';
var LAPTOP = 'bbbbbbbb-0000-0000-0000-000000000002';

// settings
// The WireGuard settings the configuration holds: the host on, two peers of one user, and no connection.
function settings() {
    return {
        Enabled: true, HostEnabled: true, ConnectEnabled: false, PublicEndpoint: 'vpn.example.com', ListenPort: 51820, TunnelSubnet: '10.66.0.0/24',
        HostPublicKey: 'hostkey', UsePresharedKeys: true, ForwardClientAddress: true, DnsEnabled: false, DnsName: 'jellyfin.internal', DnsUpstreams: '1.1.1.1',
        PeerDownloadMbps: 0, PeerUploadMbps: 0, PeerDataLimit: 0, PeerDataPeriod: 'Month', Keepalive: 25, Mtu: 1420,
        Peers: [
            { Id: PHONE, Name: 'Phone', UserId: VIEWER, Address: '10.66.0.2', PublicKey: 'k1', Enabled: true, RoutedSubnets: [] },
            { Id: LAPTOP, Name: 'Laptop', UserId: VIEWER, Address: '10.66.0.3', PublicKey: 'k2', Enabled: true, RoutedSubnets: ['192.168.1.0/24'] }
        ],
        Connections: []
    };
}

test('the helpers read ids, config files, forwards, periods, and file names', async function () {
    installPage({});
    var c = await loadScript('necto_wireguard_common.js');

    assert.equal(c.compact('AAAA-bbbb'), 'aaaabbbb');
    assert.deepEqual(c.parseWgConfig('[Interface]\nPrivateKey = pk\nAddress = 10.0.0.5/32, fd00::5/128\n[Peer]\nPublicKey = remote\nEndpoint = vpn.example.com:51820 # home\nPersistentKeepalive = 15'),
        { PrivateKey: 'pk', Address: '10.0.0.5', PeerPublicKey: 'remote', Endpoint: 'vpn.example.com:51820', Keepalive: 15 });
    assert.deepEqual(c.parseForwards('18096 10.0.0.1:8096\nnot a forward'), [{ LocalPort: 18096, RemoteHost: '10.0.0.1', RemotePort: 8096 }]);
    assert.equal(c.normalizePeriod(4), 'Year');
    assert.equal(c.normalizePeriod('Fortnight'), 'Month');
    assert.equal(c.safeFileName('Living room TV!'), 'Living-room-TV');
    assert.equal(c.speedText(null, 0, 10, 0), '- / 10 Mbps');
});

test('the counts take peers, connections, connected ends, and the ones left out', async function () {
    installPage({});
    var mod = await loadScript('necto_wireguard.js');
    var counts = mod.countCards(settings(), [
        { Kind: 'Host', Connected: true, State: 'Running' },
        { Kind: 'Peer', Connected: true, State: 'Connected' },
        { Kind: 'Peer', Connected: false, State: 'NotAllowed' },
        { Kind: 'Connection', Connected: true, State: 'Connected' }
    ]);

    assert.deepEqual(counts, { peers: 2, connections: 0, peersConnected: 1, connectionsConnected: 1, over: 0, refused: 1 });
    assert.deepEqual(mod.peerLimit({ PeerDataLimit: 5, PeerDataPeriod: 2 }, {}), { bytes: 5, period: 'Week' });
});

// openPage
// Shows the WireGuard tool over a view with its markup. onView hears the view before the tool shows. [optional]
async function openPage(answers, wireguard, onView) {
    var page = installPage({ config: { WireGuard: wireguard || settings() } });
    var dom = page.dom;
    var ids = ['wireguardStatus', 'wireguardOff', 'wireguardNoSidecar', 'wireguardBody', 'wireguardCards', 'hostError', 'wireguardBothOff', 'wireguardTable'];
    var children = ids.map(function (id) { return dom.el('div', { id: id }); });
    var view = pageView(dom, children);
    dom.document.documentElement.appendChild(view);
    globalThis.ApiClient.getUsers = function () { return Promise.resolve([{ Id: VIEWER, Name: 'viewer' }]); };
    globalThis.ApiClient.getCurrentUserId = function () { return VIEWER; };
    routeApi(page, Object.assign({
        'GET WireGuard/Peers': [
            { Kind: 'Host', State: 'Running', Connected: true },
            { Id: PHONE, Kind: 'Peer', State: 'Connected', Connected: true, RemoteAddress: '198.51.100.7' },
            { Id: LAPTOP, Kind: 'Peer', State: 'OverPeerCount', Connected: false }
        ],
        'GET WireGuard/Status': [{ Key: 'host', State: 'Running', Peers: [] }],
        'GET WireGuard/Usage': { Bytes: {}, Blocked: [], Refused: {} },
        'GET WireGuard/Sidecar': { Installed: true, Version: '1.0.0', Platform: 'linux-amd64', ReleaseVersion: '1.0.0' }
    }, answers || {}));
    if (onView) onView(view);
    var mod = await loadScript('necto_wireguard.js');
    mod.default(view);
    view.dispatchEvent(new globalThis.CustomEvent('viewshow'));
    await settle(50);
    return { page: page, view: view, dom: dom };
}

test('the tool lists this server and each peer in one table, with Add peer in its toolbar', async function () {
    var opened = await openPage();
    var rows = opened.view.querySelectorAll('#wireguardTable .stolon-table-row');

    assert.equal(rows.length, 3);
    assert.equal(rows[0].querySelectorAll('.stolon-table-cell').length >= 3, true);
    assert.ok(opened.view.querySelector('.stolon-table-controls #btnAddPeer'));
    assert.equal(opened.view.querySelector('#wireguardBody').classList.contains('stolon-hidden'), false);
});

test('with WireGuard off, the tool says so and how to turn it on, with no count cards', async function () {
    var off = settings();
    off.Enabled = false;
    var opened = await openPage({}, off);

    assert.equal(opened.view.querySelector('#wireguardCards').classList.contains('stolon-hidden'), true);
    off.Peers = [];
    opened = await openPage({}, off);

    assert.match(opened.view.querySelector('#wireguardOff').innerHTML, /VPN is off\. Turn it on in Settings to add peers\./);
    assert.equal(opened.view.querySelector('#wireguardBody').classList.contains('stolon-hidden'), true);
});

test('with Host peers and Connect to hosts both off and nothing listed, the tool says how to add peers', async function () {
    var both = settings();
    both.HostEnabled = false;
    both.Peers = [];
    var opened = await openPage({}, both);

    assert.match(opened.view.querySelector('#wireguardBothOff').innerHTML, /Peers and connections are off\. Turn them on in Settings to add them\./);
    assert.equal(opened.view.querySelector('#wireguardCards').classList.contains('stolon-hidden'), true);
    assert.equal(opened.view.querySelector('#wireguardBothOff').classList.contains('stolon-hidden'), false);

    var hosting = await openPage();
    assert.equal(hosting.view.querySelector('#wireguardBothOff').classList.contains('stolon-hidden'), true);
    assert.equal(hosting.view.querySelector('#wireguardCards').classList.contains('stolon-hidden'), false);
});

test('the table rows name this server, each peer, and each connection with their lines and state', async function () {
    installPage({});
    var mod = await loadScript('necto_wireguard.js');
    var t = { tr: function (k) { return k; } };
    var cfg = {
        Enabled: true, HostEnabled: true, PublicEndpoint: 'vpn.example.com:51820',
        Peers: [{ Id: 'p1', Name: 'Phone', Address: '10.66.0.2', UserId: 'u1', Enabled: true }],
        Connections: [{ Id: 'c1', Name: 'Office', Endpoint: 'office.example.com:51820', Enabled: false, Forwards: [{ LocalPort: 8443 }] }]
    };
    var names = { user: function () { return 'viewer'; }, used: function () { return '0 B this month'; }, server: 'Living Room' };
    var rows = mod.tunnelRows(t, cfg, [{ Id: 'p1', Kind: 'Peer', State: 'Connected', RemoteAddress: '198.51.100.7' }], names);

    assert.deepEqual(rows.map(function (r) { return r.Kind; }), ['Host', 'Peer', 'Connection']);
    assert.equal(rows[0].Name, 'Living Room');
    assert.equal(rows[1].Line, 'viewer · 10.66.0.2');
    assert.equal(rows[1].Detail, '0 B this month · 198.51.100.7');
    assert.equal(rows[1].State.color, 'green');
    assert.equal(rows[2].Detail, '0 B this month · 127.0.0.1:8443');
    assert.equal(rows[2].State.off, true);
    assert.deepEqual(mod.tunnelRows(t, Object.assign({}, cfg, { HostEnabled: false }), [], names).map(function (r) { return r.Kind; }), ['Peer', 'Connection']);
});

test('the Connections card counts connected connections, red while one failed and gray while connecting is off', async function () {
    installPage({});
    var mod = await loadScript('necto_wireguard.js');
    var t = { tr: function (k) { return k; } };
    var on = { ConnectEnabled: true, Connections: [{}, {}] };
    var counts = { connections: 2, connectionsConnected: 1 };

    assert.equal(mod.connectionsCard({ ConnectEnabled: false, Connections: [] }, [], { connections: 0, connectionsConnected: 0 }, t), null);
    assert.deepEqual(mod.connectionsCard(on, [{ Key: 'host', State: 'Failed' }], counts, t), { count: '1 / 2', label: 'HeaderConnections', color: 'green' });
    assert.equal(mod.connectionsCard(on, [{ Key: 'abc', State: 'Failed' }], counts, t).color, 'red');
    assert.equal(mod.connectionsCard({ ConnectEnabled: false }, [{ Key: 'abc', State: 'Failed' }], counts, t).color, 'gray');
});

test('a peer opens in a sidecar of tiled sections, and a peer with no config leaves out its file', async function () {
    var opened = await openPage({ ['GET WireGuard/Peers/' + PHONE + '/Config']: function () { return Promise.reject(new Error('none')); } });
    opened.view.querySelectorAll('#wireguardTable .stolon-table-row')[1].click();
    await settle(50);
    var sidecar = opened.dom.document.querySelector('.stolon-sidecar');

    assert.deepEqual(texts(sidecar, '.stolon-collapsible-title'), ['Details', 'QR code', 'Config file']);
    assert.deepEqual(texts(sidecar, '.stolon-collapsible-tile'), ['info', 'qr_code_2', 'description']);
    assert.equal(sidecar.querySelector('.stolon-subsection-title'), null);
});

test('with WireGuard off, the heading names VPN, since the tool leaves the sidebar', async function () {
    var off = settings();
    off.Enabled = false;
    var titles = [];
    await openPage({}, off, function (view) { view.addEventListener('stolontooltitle', function (e) { titles.push(e.detail); }); });

    assert.equal(titles[titles.length - 1], 'VPN');
});

test('Add peer opens an editor sidecar that posts the peer to its route', async function () {
    var opened = await openPage({ 'POST WireGuard/Peers': function (o) { return JSON.parse(o.data); } });
    opened.view.querySelector('#btnAddPeer').click();
    await settle(50);
    var sidecar = opened.dom.document.querySelector('.stolon-sidecar');

    assert.ok(sidecar);
    assert.deepEqual(texts(sidecar, '.stolon-collapsible-title'), ['Peer', 'Limits', 'Advanced']);
    assert.deepEqual(texts(sidecar, '.stolon-collapsible-tile'), ['devices', 'speed', 'tune']);
    sidecar.querySelector('input[type="text"]').value = 'Tablet';
    clickButton(sidecar.querySelector('.stolon-sidecar-shelf'), 'Save');
    await settle(50);

    var post = opened.page.requests.find(function (r) { return r.type === 'POST' && r.url === 'WireGuard/Peers'; });
    assert.ok(post);
    assert.equal(JSON.parse(post.data).Name, 'Tablet');
});

// openTool
// Shows Settings or Troubleshooting with the WireGuard settings.
async function openTool(name, answers) {
    var page = installPage({ config: { WireGuard: settings() } });
    var dom = page.dom;
    var container = name === 'settings' ? 'settingsSections' : 'troubleshootingSections';
    var view = pageView(dom, [dom.el('div', { id: 'settingsStatus' }), dom.el('div', { id: container })]);
    dom.document.documentElement.appendChild(view);
    var saved = [];
    globalThis.ApiClient.updatePluginConfiguration = function (id, config) { saved.push(JSON.parse(JSON.stringify(config))); return Promise.resolve(); };
    routeApi(page, Object.assign({ 'GET Groups/ResetCodes': { Enabled: false } }, answers || {}));
    var mod = await loadScript('necto_' + name + '.js');
    mod.default(view);
    view.dispatchEvent(new globalThis.CustomEvent('viewshow'));
    await settle(50);
    return { page: page, view: view, dom: dom, saved: saved, section: view.querySelector('[data-tool="wireguard"]') };
}

test('the WireGuard section of Settings saves its fields into the config, and Replace wireguard-go is in Troubleshooting', async function () {
    var sidecar = { 'GET WireGuard/Sidecar': { Installed: true, Version: '1.0.0', Platform: 'linux-amd64', DownloadedUtc: '2026-11-20T00:00:00Z', ReleaseVersion: '1.0.0' } };
    var trouble = await openTool('troubleshooting', sidecar);
    var row = trouble.section.querySelector('[data-action="replace-sidecar"]').closest('.stolon-reset-row');
    assert.match(row.textContent, /Version 1\.0\.0 for linux-amd64/);
    assert.equal(row.querySelector('[data-action="replace-sidecar"]').textContent, 'Replace');

    var opened = await openTool('settings', sidecar);
    var section = opened.section;

    assert.deepEqual(texts(section, '[data-part] > .stolon-collapsible-header .stolon-collapsible-title'), ['Host', 'Security', 'DNS', 'Peer limits', 'Advanced', 'Connections']);
    assert.equal(section.querySelector('[data-action="replace-sidecar"]'), null);
    var port = Array.prototype.find.call(section.querySelectorAll('input[type="number"]'), function (i) { return i.value === '51820'; });
    port.value = '51900';
    clickButton(section, 'Save');
    await settle(50);

    assert.equal(opened.saved[0].WireGuard.ListenPort, 51900);
    assert.equal(opened.saved[0].WireGuard.Peers.length, 2);
});

test('turning WireGuard on without wireguard-go asks to download it, and Cancel leaves it off', async function () {
    var opened = await openTool('settings', { 'GET WireGuard/Sidecar': { Installed: false, Platform: 'linux-amd64', ReleaseVersion: '1.0.0' } });
    var enable = opened.section.querySelector('input[type="checkbox"]');
    enable.checked = false;
    enable.checked = true;
    enable.dispatchEvent(new globalThis.CustomEvent('change'));
    await settle(50);

    assert.deepEqual(dialogText(opened.dom).title, 'Download wireguard-go');
    clickDialogButton(opened.dom, 'Cancel');
    await settle(50);
    assert.equal(enable.checked, false);
    assert.equal(opened.page.requests.some(function (r) { return r.url === 'WireGuard/Sidecar/Download'; }), false);
});

test('the WireGuard section of Troubleshooting holds only Restart, and the VPN page lists each user against their groups', async function () {
    var opened = await openTool('troubleshooting', {
        'GET WireGuard/Users': [{ UserId: VIEWER, Name: 'viewer', Used: 2, MayHavePeers: true, MaxPeers: 1, DataLimit: 0 }]
    });
    var section = opened.section;

    assert.deepEqual(texts(section, '.stolon-subsection-title'), ['Repairs']);
    assert.equal(opened.page.requests.some(function (r) { return r.url === 'WireGuard/Users'; }), false);
    clickButton(section, 'Restart');
    await settle(50);
    assert.ok(opened.page.requests.some(function (r) { return r.type === 'POST' && r.url === 'WireGuard/Restart'; }));

    var bag = (await loadScript('necto_shared.js')).createNectoShared(pageView(opened.dom, []));
    await bag.translations;
    var mod = await loadScript('necto_wireguard_sections.js');
    var peers = mod.createPeersByUser(bag);
    opened.dom.document.documentElement.appendChild(peers.element);
    await peers.load();
    await settle(50);
    var cells = Array.prototype.map.call(peers.element.querySelectorAll('.stolon-table-cell'), function (c) { return c.innerHTML; }).join(' ');
    assert.equal(peers.element.getAttribute('data-role'), 'peers-by-user');
    assert.match(cells, /viewer[\s\S]*2 of 1 peers[\s\S]*Over peer count/);
});

test('downloading wireguard-go gives the button back its own children, so its label can change after', async function () {
    var page = installPage({});
    var mod = await loadScript('necto_wireguard_sections.js');
    var doc = page.dom.document;
    var button = doc.createElement('button');
    var span = doc.createElement('span');
    span.textContent = 'Download';
    button.appendChild(span);
    var shared = { t: { tr: function (k) { return k; } }, request: function () { return Promise.resolve({ Installed: true }); } };

    await mod.downloadSidecar(shared, button);

    assert.equal(button.querySelector('span'), span);
    assert.equal(button.disabled, false);
});
