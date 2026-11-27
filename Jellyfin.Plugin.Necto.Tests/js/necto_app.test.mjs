// necto_app.test.mjs
// Tests for the Necto page: the tools its sidebar lists, in order, with the dashboard first.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { installPage, loadScript, pageView, settle, sidebar } from './support.mjs';

globalThis.setTimeout = function () { return 1; };
globalThis.clearTimeout = function () {};

var KEYS = ['dashboard', 'groups', 'requests', 'issues', 'invites', 'proxy', 'ddns', 'wireguard', 'scripts', 'pages', 'storage', 'settings', 'troubleshooting'];
var NAMES = ['Dashboard', 'Groups', 'Requests', 'Issues', 'Invites', 'Proxy', 'DDNS', 'VPN', 'Scripts', 'Pages', 'Storage', 'Settings', 'Troubleshooting'];
var ICONS = ['dashboard', 'groups', 'movie_filter', 'report_problem', 'mail', 'security', 'dns', 'vpn_lock', 'code', 'web', 'storage', 'settings', 'bug_report'];

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

test('nectoTools lists the 13 tools in sidebar order, each with its icon and its own page', async function () {
    installPage({});
    var shared = await loadScript('necto_shared.js');
    var tools = shared.nectoTools();
    assert.equal(tools.length, 13);
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

test('toolIsOn reads the switch of each tool from its config section, and the always on tools have none', async function () {
    installPage({});
    var shared = await loadScript('necto_shared.js');
    Object.keys(SECTIONS).forEach(function (key) {
        var on = {};
        on[SECTIONS[key]] = { Enabled: true };
        assert.equal(shared.toolIsOn({}, key), false, key);
        assert.equal(shared.toolIsOn(on, key), true, key);
    });
    assert.deepEqual(shared.nectoTools().filter(function (tool) { return tool.visibleWhen; }).map(function (tool) { return tool.key; }), Object.keys(SECTIONS));
    ['dashboard', 'groups', 'settings', 'troubleshooting'].forEach(function (key) {
        assert.equal(shared.toolIsOn({}, key), true);
    });
});

test('the sidebar drops every optional tool while all are off, keeping the always on ones', async function () {
    var view = await openApp({});
    await settle();
    assert.deepEqual(sidebar(view).names, ['Dashboard', 'Groups', 'Settings', 'Troubleshooting']);
});

test('the sidebar has its fold button', async function () {
    var view = await openApp();
    assert.ok(view.querySelector('.stolon-tool-shell-fold'));
});

