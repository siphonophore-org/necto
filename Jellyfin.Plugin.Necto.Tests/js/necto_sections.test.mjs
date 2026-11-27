// necto_sections.test.mjs
// Tests for Settings and Troubleshooting: one section per tool from the registry in sidebar order, the Groups
// sections filled and saving.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { installPage, loadScript, pageView, settle, routeApi, texts, clickButton, clickDialogButton, dialogText, statusText, allToolsOn } from './support.mjs';

globalThis.setTimeout = function () { return 1; };
globalThis.clearTimeout = function () {};
globalThis.setInterval = function () { return 1; };
globalThis.clearInterval = function () {};

var TOOLS = ['Groups', 'Notifications', 'Requests', 'Issues', 'Invites', 'Proxy', 'DDNS', 'VPN', 'Scripts', 'Pages', 'Storage'];
var FAMILY = '11111111-2222-3333-4444-555555555555';

// openTool
// Shows Settings or Troubleshooting over a view with its sections container and its status line.
async function openTool(name, opts) {
    opts = opts || {};
    var page = installPage({ config: opts.config || {} });
    var dom = page.dom;
    var container = name === 'settings' ? 'settingsSections' : 'troubleshootingSections';
    var view = pageView(dom, [dom.el('div', { id: 'settingsStatus' }), dom.el('div', { id: container })]);
    dom.document.documentElement.appendChild(view);
    var saved = [];
    globalThis.ApiClient.updatePluginConfiguration = function (id, config) { saved.push(JSON.parse(JSON.stringify(config))); return Promise.resolve(); };
    routeApi(page, opts.answers || {});
    var mod = await loadScript('necto_' + name + '.js');
    mod.default(view);
    view.dispatchEvent(new globalThis.CustomEvent('viewshow'));
    await settle();
    return { page: page, view: view, dom: dom, saved: saved, section: function (key) { return view.querySelector('[data-tool="' + key + '"]'); } };
}

test('nectoSections lists the tools that registered a section, in sidebar order, and leaves out the rest', async function () {
    installPage({});
    var shared = await loadScript('necto_shared.js');
    var section = { build: function () { return { load: function () {}, apply: function () {} }; } };
    shared.registerSection('settings', 'storage', section);
    shared.registerSection('settings', 'ddns', section);
    shared.registerSection('settings', 'dashboard', section);

    var sections = shared.nectoSections('settings');

    assert.deepEqual(sections.map(function (s) { return s.name; }), ['DDNS', 'Storage']);
    assert.equal(sections[1].section, section);
    assert.equal(shared.nectoSections('troubleshooting').find(function (s) { return s.key === 'storage'; }), undefined);
    assert.throws(function () { shared.registerSection('elsewhere', 'storage', {}); });
});

test('Settings draws a collapsible section per tool, with the Groups section filled', async function () {
    var opened = await openTool('settings', { config: { Groups: [{ Id: FAMILY, Name: 'Family' }], GroupSettings: { NewUserGroupId: FAMILY } } });

    var titles = texts(opened.view, '.stolon-collapsible-title');
    assert.deepEqual(titles.filter(function (t) { return TOOLS.indexOf(t) >= 0; }), TOOLS);
    assert.deepEqual(texts(opened.view, '[data-group]'), ['System', 'Services']);
    assert.deepEqual(texts(opened.view, '[data-group-help]'), ['Sets what applies across Necto, whatever the tool.', 'Sets how each tool works, while groups set who may use it.']);
    var order = Array.prototype.map.call(opened.view.querySelector('#settingsSections').children, function (n) { return n.getAttribute('data-group') || n.getAttribute('data-tool') || ''; }).filter(Boolean);
    assert.deepEqual(order.slice(0, 5), ['system', 'groups', 'notifications', 'menulinks', 'services']);
    assert.equal(order[5], 'requests');
    var groups = opened.section('groups');
    assert.deepEqual(texts(groups, '.stolon-subsection-title'), ['New users']);
    assert.equal(groups.querySelector('[data-field="NewUserGroupId"]').value, FAMILY);
    assert.deepEqual(texts(groups.querySelector('[data-field="NewUserGroupId"]'), 'option'), ['Everyone', 'Family']);
    assert.equal(groups.querySelector('[data-field="ShowResetCodes"]'), null);
});

test('Settings opens and keeps open the section another tool asked for', async function () {
    var page = installPage({ config: {} });
    var dom = page.dom;
    var view = pageView(dom, [dom.el('div', { id: 'settingsStatus' }), dom.el('div', { id: 'settingsSections' })]);
    view.setAttribute('data-stolon-tool-param', 'notifications');
    dom.document.documentElement.appendChild(view);
    routeApi(page, {});
    var mod = await loadScript('necto_settings.js');
    mod.default(view);
    view.dispatchEvent(new globalThis.CustomEvent('viewshow'));
    await settle();

    var section = view.querySelector('[data-tool="notifications"]');
    var open = function (node) { return node.querySelector('[aria-expanded]').getAttribute('aria-expanded'); };
    assert.equal(open(section), 'true');
    assert.equal(view.getAttribute('data-stolon-tool-param'), null);
    assert.equal(open(view.querySelector('[data-tool="groups"]')), 'false');
});

test('the Groups section of Settings saves its own fields into the config', async function () {
    var opened = await openTool('settings', { config: { Groups: [{ Id: FAMILY, Name: 'Family' }], GroupSettings: { NewUserGroupId: FAMILY }, AccountSettings: { ShowResetCodes: false }, Other: 'kept' } });
    var groups = opened.section('groups');
    groups.querySelector('[data-field="NewUserGroupId"]').value = '';

    assert.equal(clickButton(groups, 'Save'), true);
    await settle(50);

    var config = opened.saved[0];
    assert.equal(config.GroupSettings.NewUserGroupId, null);
    assert.equal(config.AccountSettings.ShowResetCodes, false);
    assert.equal(config.Other, 'kept');
});

test('Troubleshooting draws a section per tool, Groups with its repairs', async function () {
    var opened = await openTool('troubleshooting', { answers: { 'GET Groups/ResetCodes': { Enabled: false, Codes: [] } } });

    assert.deepEqual(texts(opened.view, '.stolon-collapsible-title').filter(function (t) { return TOOLS.indexOf(t) >= 0; }), TOOLS);
    var groups = opened.section('groups');
    assert.deepEqual(texts(groups, '.stolon-subsection-title'), ['Password requirements', 'Jellyfin settings', 'Scheduled checks', 'Password resets']);
    assert.deepEqual(texts(groups, '[data-action]'), ['Apply', 'Apply', 'Run now', 'Run now']);
    assert.match(groups.textContent, /Reset codes are off\. Turn on Show reset codes in a group's Password resets to list them\./);
});

test('Apply rules posts to the password rules route and reports how many users moved', async function () {
    var opened = await openTool('troubleshooting', { answers: { 'POST Groups/PasswordRules/Apply': { Moved: 1 }, 'GET Groups/ResetCodes': { Enabled: false } } });
    var groups = opened.section('groups');

    groups.querySelector('[data-action="apply-rules"]').click();
    await settle(50);

    assert.ok(opened.page.requests.some(function (r) { return r.type === 'POST' && r.url === 'Groups/PasswordRules/Apply'; }));
    assert.equal(statusText(groups.querySelector('[data-role="status"]')), '1 user moved.');
});

test('running the expiry checks asks first, and Cancel starts nothing', async function () {
    var started = [];
    var opened = await openTool('troubleshooting', { answers: { 'GET Groups/ResetCodes': { Enabled: false } } });
    globalThis.ApiClient.getScheduledTasks = function () { return Promise.resolve([{ Key: 'NectoAccountRules', Id: 'task-1' }]); };
    globalThis.ApiClient.startScheduledTask = function (id) { started.push(id); return Promise.resolve(); };
    var row = opened.section('groups').querySelector('[data-action="run-account-rules"]');

    row.click();
    await settle(50);
    var dialog = dialogText(opened.dom);
    assert.equal(dialog.title, 'Process expired and inactive users');
    assert.match(dialog.body, /A deleted user cannot come back\./);
    clickDialogButton(opened.dom, 'Cancel');
    await settle(50);
    assert.deepEqual(started, []);

    row.click();
    await settle(50);
    clickDialogButton(opened.dom, 'Run now');
    await settle(50);
    assert.deepEqual(started, ['task-1']);
});

test('pending password resets are listed with their users while reset codes show', async function () {
    var opened = await openTool('troubleshooting', { answers: { 'GET Groups/ResetCodes': { Enabled: true, Codes: [{ UserName: 'viewer', Pin: '123456', ExpirationDate: '2099-01-01T00:00:00Z', Expired: false }] } } });

    var resets = opened.section('groups').querySelector('[data-role="resets"]');
    assert.match(resets.textContent, /viewer/);
    assert.ok(resets.querySelector('.stolon-record-list'));
});

test('resetItems tags each reset as active, expiring, or expired, and lists expired ones last', async function () {
    installPage({});
    var sections = await loadScript('necto_groups_sections.js');
    var bag = (await loadScript('necto_shared.js')).createNectoShared(installPage({}).dom.el('div', {}));
    await bag.translations;
    var now = new Date('2026-11-26T12:00:00Z');

    var items = sections.resetItems([
        { UserName: 'old', Pin: '1', ExpirationDate: '2026-11-26T11:00:00Z', Expired: false },
        { UserName: 'soon', Pin: '2', ExpirationDate: '2026-11-26T12:03:00Z', Expired: false },
        { UserName: 'fresh', Pin: '3', ExpirationDate: '2026-11-26T13:00:00Z', Expired: false },
        { UserName: 'flagged', Pin: '4', ExpirationDate: '2026-11-26T13:00:00Z', Expired: true }
    ], bag, now);

    assert.deepEqual(items.map(function (i) { return [i.title, i.state, i.tags[0].label]; }), [
        ['soon', 'expiring', 'Expiring'], ['fresh', 'active', 'Active'], ['old', 'expired', 'Expired'], ['flagged', 'expired', 'Expired']
    ]);
    assert.deepEqual(sections.resetCards(items, bag.t).map(function (c) { return [c.label, c.count]; }), [['Active', 1], ['Expiring', 1], ['Expired', 2]]);
});

test('pending password resets show their counts and the HTTPS warning', async function () {
    var opened = await openTool('troubleshooting', { answers: { 'GET Groups/ResetCodes': { Enabled: true, Codes: [{ UserName: 'viewer', Pin: '123456', ExpirationDate: '2099-01-01T00:00:00Z', Expired: false }] } } });

    var resets = opened.section('groups').querySelector('[data-role="resets"]');
    assert.match(resets.querySelector('[data-role="reset-cards"]').textContent, /1\s*Active/);
    assert.match(resets.textContent, /Reset codes are sent over this connection\. Use HTTPS so they cannot be read in transit\./);
});

test('the tile of a tool turns green as its switch is turned on, and gray as it is turned off', async function () {
    var config = allToolsOn();
    config.Scripts.Enabled = false;
    var opened = await openTool('settings', { config: config, answers: { 'GET Ddns/Settings': { Enabled: true } } });
    var section = opened.section('scripts');
    var box = section.querySelector('input[type="checkbox"]');
    var tile = section.querySelector('.stolon-collapsible-tile');
    assert.ok(tile.classList.contains('stolon-gray'));

    box.checked = true;
    box.dispatchEvent(new globalThis.Event('change'));
    assert.ok(tile.classList.contains('stolon-green'));

    box.checked = false;
    box.dispatchEvent(new globalThis.Event('change'));
    assert.ok(tile.classList.contains('stolon-gray'));
});

test('each tool with a switch has Enable and its name as its first checkbox, and its tile is green while on and gray while off', async function () {
    var config = allToolsOn();
    config.Storage.Enabled = false;
    var opened = await openTool('settings', { config: config, answers: { 'GET Ddns/Settings': { Enabled: true }, 'GET Proxy/Defaults': {}, 'GET Proxy/Settings': { Enabled: true } } });
    var labels = { requests: 'Enable requests', issues: 'Enable issues', invites: 'Enable invites', proxy: 'Enable proxy', ddns: 'Enable DDNS', wireguard: 'Enable VPN', scripts: 'Enable scripts', pages: 'Enable pages', storage: 'Enable storage' };
    Object.keys(labels).forEach(function (key) {
        var section = opened.section(key);
        var box = section.querySelector('input[type="checkbox"]');
        assert.equal(box.closest('label').textContent.trim(), labels[key], key);
        var tile = section.querySelector('.stolon-collapsible-tile');
        assert.ok(tile.classList.contains(key === 'storage' ? 'stolon-gray' : 'stolon-green'), key);
    });
    assert.ok(!opened.section('groups').querySelector('.stolon-collapsible-tile').classList.contains('stolon-gray'));
});

test('the settings under a switch show only while it is on, and the Save Bar stays', async function () {
    var config = allToolsOn();
    config.Storage.Enabled = false;
    var opened = await openTool('settings', { config: config, answers: { 'GET Ddns/Settings': { Enabled: true } } });
    var section = opened.section('storage');
    var block = section.querySelector('[data-role="switched"]');
    var box = section.querySelector('input[type="checkbox"]');
    assert.ok(block.querySelector('[data-field="ScanGuard"]'));
    assert.ok(block.classList.contains('stolon-hidden'));
    assert.equal(block.querySelector('.stolon-save-bar'), null);
    assert.ok(section.querySelector('.stolon-save-bar'));

    box.checked = true;
    box.dispatchEvent(new globalThis.Event('change'));
    assert.ok(!block.classList.contains('stolon-hidden'));
    assert.ok(!opened.section('scripts').querySelector('[data-role="switched"]').classList.contains('stolon-hidden'));
});

test('no two sections or rows on Settings or Troubleshooting share an icon', async function () {
    var answers = { 'GET Ddns/Settings': { Enabled: true }, 'GET Groups/ResetCodes': { Enabled: false }, 'GET Proxy/Defaults': {}, 'GET Proxy/Settings': { Enabled: true } };
    var settings = await openTool('settings', { config: allToolsOn(), answers: answers });
    var tiles = texts(settings.view, '.stolon-collapsible-section > .stolon-collapsible-header .stolon-collapsible-tile');
    assert.deepEqual(tiles.filter(function (icon, i) { return tiles.indexOf(icon) !== i; }), []);

    var trouble = await openTool('troubleshooting', { config: allToolsOn(), answers: answers });
    var icons = texts(trouble.view, '.stolon-collapsible-section > .stolon-collapsible-header .stolon-collapsible-tile')
        .concat(Array.prototype.map.call(trouble.view.querySelectorAll('.stolon-reset-row:not([data-store]) .stolon-collapsible-tile'), function (n) { return n.textContent; }));
    assert.ok(icons.length > 20);
    assert.deepEqual(icons.filter(function (icon, i) { return icons.indexOf(icon) !== i; }), []);
});
