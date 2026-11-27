// necto_app.test.mjs
// Tests for the Necto page: the tools its sidebar lists, in order, with the dashboard first, and the coming state
// of each tool that is not built.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { installPage, loadScript, pageView, settle, sidebar } from './support.mjs';

globalThis.setTimeout = function () { return 1; };
globalThis.clearTimeout = function () {};

var KEYS = ['dashboard', 'requests', 'issues', 'invites', 'groups', 'proxy', 'ddns', 'wireguard', 'scripts', 'pages', 'storage', 'notifications', 'settings', 'troubleshooting'];
var NAMES = ['Dashboard', 'Requests', 'Issues', 'Invites', 'Groups', 'Proxy', 'DDNS', 'WireGuard', 'Scripts', 'Custom pages', 'Storage', 'Notifications', 'Settings', 'Troubleshooting'];
var ICONS = ['dashboard', 'movie_filter', 'report_problem', 'mail', 'groups', 'security', 'dns', 'vpn_lock', 'code', 'web', 'storage', 'notifications', 'settings', 'bug_report'];

// openApp
// Builds the Necto page over a fresh view and returns the view.
async function openApp() {
    var page = installPage({});
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
    assert.deepEqual(sidebar(view).names, NAMES);
});

test('the sidebar has its fold button', async function () {
    var view = await openApp();
    assert.ok(view.querySelector('.stolon-tool-shell-fold'));
});

test('each tool page that is not built shows the empty state that says the tool is coming', async function () {
    for (var key of KEYS.filter(function (k) { return ['groups', 'invites', 'notifications', 'wireguard', 'scripts', 'pages', 'storage', 'settings', 'troubleshooting'].indexOf(k) < 0; })) {
        var page = installPage({});
        var empty = page.dom.el('div', { class: 'stolon-empty-section', 'data-str': 'MessageToolComing' });
        var view = pageView(page.dom, [empty]);
        page.dom.document.documentElement.appendChild(view);
        var controller = await loadScript('necto_' + key + '.js');
        controller.default(view);
        view.dispatchEvent(new globalThis.CustomEvent('viewshow'));
        await settle();
        assert.equal(empty.textContent, 'This tool is coming.', key);
    }
});
