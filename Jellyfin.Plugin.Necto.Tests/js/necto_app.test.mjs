// necto_app.test.mjs
// Tests for the Necto page: the tools its sidebar lists, in order, with the dashboard first.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { installPage, loadScript, pageView, settle, sidebar } from './support.mjs';

globalThis.setTimeout = function () { return 1; };
globalThis.clearTimeout = function () {};

var KEYS = ['dashboard', 'groups', 'requests', 'issues', 'invites', 'proxy', 'ddns', 'wireguard', 'scripts', 'pages', 'storage', 'notifications', 'settings', 'troubleshooting'];
var NAMES = ['Dashboard', 'Groups', 'Requests', 'Issues', 'Invites', 'Proxy', 'DDNS', 'VPN', 'Scripts', 'Pages', 'Storage', 'Notifications', 'Settings', 'Troubleshooting'];
var ICONS = ['dashboard', 'groups', 'movie_filter', 'report_problem', 'mail', 'security', 'dns', 'vpn_lock', 'code', 'web', 'storage', 'notifications', 'settings', 'bug_report'];

// The config sections of the tools with a switch, as the plugin's ToolSwitches lists them.
var SECTIONS = { requests: 'Requests', issues: 'Issues', invites: 'InviteSettings', proxy: 'Proxy', ddns: 'Ddns', wireguard: 'WireGuard', scripts: 'Scripts', pages: 'CustomPages', storage: 'Storage' };

// allOn
// Gives a config with every tool turned on, but the ones named off.
function allOn(off) {
    var config = {};
    Object.keys(SECTIONS).forEach(function (key) { config[SECTIONS[key]] = { Enabled: (off || []).indexOf(key) < 0 }; });
    return config;
}

// openApp
// Builds the Necto page over a fresh view and returns the view.
async function openApp(config) {
    var page = installPage({ config: config || allOn() });
    var view = pageView(page.dom, []);
    page.dom.document.documentElement.appendChild(view);
    var shared = await loadScript('necto_shared.js');
    shared.createNectoApp(view);
    return view;
}

test('nectoTools lists the 14 tools in sidebar order, each with its icon and its own page', async function () {
    installPage({});
    var shared = await loadScript('necto_shared.js');
    var tools = shared.nectoTools();
    assert.equal(tools.length, 14);
    assert.deepEqual(tools.map(function (tool) { return tool.key; }), KEYS);
    assert.deepEqual(tools.map(function (tool) { return tool.name; }), NAMES);
    assert.deepEqual(tools.map(function (tool) { return tool.icon; }), ICONS);
    assert.deepEqual(tools.map(function (tool) { return tool.page; }), KEYS.map(function (key) { return 'necto_' + key; }));
});

test('the sidebar lists the dashboard first, then every tool', async function () {
    var view = await openApp();
    await settle();
    assert.deepEqual(sidebar(view).names, NAMES);
});

test('the sidebar leaves Issues out while Enable issues is off', async function () {
    var view = await openApp(allOn(['issues']));
    await settle();
    assert.deepEqual(sidebar(view).names, NAMES.filter(function (n) { return n !== 'Issues'; }));
});

test('the sidebar leaves out Proxy and WireGuard while each is off', async function () {
    var view = await openApp(allOn(['proxy', 'wireguard']));
    await settle();
    assert.deepEqual(sidebar(view).names, NAMES.filter(function (n) { return n !== 'Proxy' && n !== 'VPN'; }));
});

test('setToolsOff turns each tool with an on switch off until it is on', async function () {
    installPage({});
    var shared = await loadScript('necto_shared.js');
    var off = shared.setToolsOff({});
    assert.deepEqual(Object.keys(off), Object.keys(SECTIONS));
    assert.ok(Object.keys(off).every(function (key) { return off[key] === true; }));
    assert.deepEqual(shared.setToolsOff(allOn(['wireguard', 'storage'])), {
        requests: false, issues: false, invites: false, proxy: false, ddns: false, wireguard: true, scripts: false, pages: false, storage: true
    });
    assert.ok(shared.isToolOff('storage') && !shared.isToolOff('groups'));
});

test('TOOL_SWITCHES names each switch by its config section, and the always on tools have none', async function () {
    installPage({});
    var shared = await loadScript('necto_shared.js');
    assert.deepEqual(shared.TOOL_SWITCHES, SECTIONS);
    ['dashboard', 'groups', 'notifications', 'settings', 'troubleshooting'].forEach(function (key) {
        assert.equal(shared.toolIsOn({}, key), true);
    });
});

test('the sidebar drops every optional tool while all are off, keeping the always on ones', async function () {
    var view = await openApp({});
    await settle();
    assert.deepEqual(sidebar(view).names, ['Dashboard', 'Groups', 'Notifications', 'Settings', 'Troubleshooting']);
});

test('resetRow draws the icon tile before the row text, red for a row that loses data', async function () {
    installPage({});
    var shared = await loadScript('necto_shared.js');
    var row = shared.resetRow({ icon: 'delete_sweep', label: 'Clear history', help: 'Deletes every visit.', button: 'Clear', action: 'clear', destructive: true });
    var tile = row.children[0];
    assert.ok(row.classList.contains('stolon-reset-row'));
    assert.ok(tile.classList.contains('stolon-collapsible-tile') && tile.classList.contains('stolon-red'));
    assert.equal(tile.querySelector('.material-icons').textContent, 'delete_sweep');
    assert.ok(row.children[1].classList.contains('stolon-reset-info'));
    var button = row.querySelector('button[data-action="clear"]');
    assert.ok(button.classList.contains('stolon-destructive'));
    assert.equal(button.textContent, 'Clear');

    var plain = shared.resetRow({ icon: 'schedule', label: 'Run', help: 'Runs it.', button: 'Run now', action: 'run' });
    assert.ok(!plain.children[0].classList.contains('stolon-red'));
    assert.ok(plain.querySelector('button').classList.contains('stolon-secondary'));
    var empty = shared.resetRow({ icon: 'delete_sweep', label: 'Clear', help: 'Clears it.', button: 'Clear', action: 'clear', destructive: true, empty: true });
    assert.ok(empty.children[0].classList.contains('stolon-gray'));
    var own = shared.resetRow({ icon: 'restart_alt', label: 'Restart', help: 'Restarts it.', button: row.querySelector('button') });
    assert.equal(own.querySelector('button').getAttribute('data-action'), 'clear');
});

test('the sidebar has its fold button', async function () {
    var view = await openApp();
    assert.ok(view.querySelector('.stolon-tool-shell-fold'));
});

