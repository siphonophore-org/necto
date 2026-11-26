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
        { Kind: 'Peer', Connected: false, State: 'NotAllowed' }
    ]);

    assert.deepEqual(counts, { peers: 2, connections: 0, connected: 1, over: 0, refused: 1 });
    assert.deepEqual(mod.peerLimit({ PeerDataLimit: 5, PeerDataPeriod: 2 }, {}), { bytes: 5, period: 'Week' });
});

// openPage
// Shows the WireGuard tool over a view with its markup.
async function openPage(answers, wireguard) {
    var page = installPage({ config: { WireGuard: wireguard || settings() } });
    var dom = page.dom;
    var ids = ['wireguardStatus', 'wireguardOff', 'wireguardNoSidecar', 'wireguardBody', 'wireguardCards', 'hostError', 'addButtons', 'peerSection', 'connectionSection'];
    var children = ids.map(function (id) { return dom.el('div', { id: id }); });
    children.push(dom.el('button', { id: 'btnAddPeer' }), dom.el('button', { id: 'btnAddConnection' }));
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
    var mod = await loadScript('necto_wireguard.js');
    mod.default(view);
    view.dispatchEvent(new globalThis.CustomEvent('viewshow'));
    await settle(50);
    return { page: page, view: view, dom: dom };
}

test('the tool lists each peer with its owner and state, and a peer the groups leave out says why', async function () {
    var opened = await openPage();
    var rows = opened.view.querySelectorAll('#peerSection .stolon-record-list-row');

    assert.equal(rows.length, 2);
    assert.match(rows[0].textContent, /Phone/);
    assert.match(rows[0].textContent, /viewer/);
    assert.match(rows[0].textContent, /Connected/);
    assert.match(rows[1].textContent, /Over peer count/);
    assert.match(rows[1].textContent, /Router/);
    assert.equal(opened.view.querySelector('#wireguardBody').classList.contains('stolon-hidden'), false);
});

test('with WireGuard off, the tool says so and how to turn it on', async function () {
    var off = settings();
    off.Enabled = false;
    off.Peers = [];
    var opened = await openPage({}, off);

    assert.match(opened.view.querySelector('#wireguardOff').innerHTML, /WireGuard is off\. Turn it on in Settings to add peers\./);
    assert.equal(opened.view.querySelector('#wireguardBody').classList.contains('stolon-hidden'), true);
});

test('Add peer opens an editor sidecar that posts the peer to its route', async function () {
    var opened = await openPage({ 'POST WireGuard/Peers': function (o) { return JSON.parse(o.data); } });
    opened.view.querySelector('#btnAddPeer').click();
    await settle(50);
    var sidecar = opened.dom.document.querySelector('.stolon-sidecar');

    assert.ok(sidecar);
    assert.deepEqual(texts(sidecar, '.stolon-collapsible-title'), ['Peer', 'Limits', 'Advanced']);
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

test('the WireGuard section of Settings shows the download and saves its fields into the config', async function () {
    var opened = await openTool('settings', { 'GET WireGuard/Sidecar': { Installed: true, Version: '1.0.0', Platform: 'linux-amd64', DownloadedUtc: '2026-11-20T00:00:00Z', ReleaseVersion: '1.0.0' } });
    var section = opened.section;

    assert.deepEqual(texts(section, '.stolon-subsection-title'), ['wireguard-go', 'Host', 'Security', 'DNS', 'Peer limits', 'Advanced', 'Connections']);
    assert.match(section.textContent, /Version 1\.0\.0 for linux-amd64/);
    assert.ok(Array.prototype.some.call(section.querySelectorAll('button'), function (b) { return b.textContent === 'Replace'; }));
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

test('the WireGuard section of Troubleshooting lists the tunnels, Restart, and each user against their groups', async function () {
    var opened = await openTool('troubleshooting', {
        'GET WireGuard/Status': [{ Key: 'host', Name: 'Host', State: 'Failed', Error: 'address already in use', Peers: [] }],
        'GET WireGuard/Users': [{ UserId: VIEWER, Name: 'viewer', Used: 2, MayHavePeers: true, MaxPeers: 1, DataLimit: 0 }]
    });
    var section = opened.section;

    assert.deepEqual(texts(section, '.stolon-subsection-title'), ['Tunnels', 'Peers by user']);
    assert.match(section.textContent, /address already in use/);
    assert.match(section.textContent, /2 of 1 peers/);
    clickButton(section, 'Restart');
    await settle(50);
    assert.ok(opened.page.requests.some(function (r) { return r.type === 'POST' && r.url === 'WireGuard/Restart'; }));
});
