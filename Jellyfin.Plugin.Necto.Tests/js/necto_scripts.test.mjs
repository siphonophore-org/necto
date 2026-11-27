// necto_scripts.test.mjs
// Tests for Scripts: the shared labels and templates, the Settings section, the run history in Troubleshooting, and
// the Scripts section of the group editor.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { installPage, loadScript, pageView, settle, routeApi, texts, clickButton, allToolsOn } from './support.mjs';

globalThis.setTimeout = function () { return 1; };
globalThis.clearTimeout = function () {};
globalThis.setInterval = function () { return 1; };
globalThis.clearInterval = function () {};
globalThis.matchMedia = globalThis.matchMedia || function () { return { matches: false, addEventListener: function () {}, removeEventListener: function () {} }; };
globalThis.window = globalThis.window || globalThis;
globalThis.window.matchMedia = globalThis.matchMedia;

var SCRIPT = 'cccccccc-0000-0000-0000-000000000001';

// openTool
// Shows Settings or Troubleshooting with the Scripts settings.
async function openTool(name, answers) {
    var page = installPage({ config: { Scripts: { MaxConcurrentRuns: 6, DefaultTimeoutSeconds: 90, HistorySize: 300, Scripts: [{ Id: SCRIPT, Name: 'Notify' }] } } });
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
    return { page: page, view: view, dom: dom, saved: saved, section: view.querySelector('[data-tool="scripts"]') };
}

test('the step types and operators read in the viewer\'s language once a page has the strings', async function () {
    installPage({});
    var shared = await loadScript('necto_shared.js');
    var common = await loadScript('necto_scripts_common.js');
    var bag = shared.createNectoShared(pageView(installPage({}).dom, []));
    await bag.translations;
    common.useStrings(bag);

    assert.equal(common.operationTypes().RunCommand.label, 'Run command');
    assert.equal(common.operatorLabels().AtLeast, 'is at least');
    assert.ok(common.fieldGroups().length > 0);
});

test('every step type leads its card with an icon of its own, never a number', async function () {
    installPage({});
    var common = await loadScript('necto_scripts_common.js');
    var icons = Object.values(common.operationTypes()).map(function (type) { return type.icon; });

    assert.ok(icons.every(function (icon) { return typeof icon === 'string' && icon.length > 0; }));
    assert.equal(new Set(icons).size, icons.length);
});

test('every template builds a script with a name and steps of known types', async function () {
    installPage({});
    var common = await loadScript('necto_scripts_common.js');
    var templates = await loadScript('necto_scripts_templates.js');
    var types = common.operationTypes();
    var list = templates.templates();

    assert.ok(list.length > 1);
    list.forEach(function (item) {
        var built = item.build();
        (built.Operations || []).forEach(function (op) { assert.ok(types[op.Type], item.key + ' ' + op.Type); });
    });
});

test('the Scripts section of Settings saves its three settings into the Scripts part of the config', async function () {
    var opened = await openTool('settings');
    var section = opened.section;

    assert.deepEqual(texts(section, '.stolon-subsection-title'), ['Runs', 'History']);
    clickButton(section, 'Save');
    await settle(50);

    var scripts = opened.saved[0].Scripts;
    assert.equal(scripts.MaxConcurrentRuns, 6);
    assert.equal(scripts.DefaultTimeoutSeconds, 90);
    assert.equal(scripts.HistorySize, 300);
    assert.equal(scripts.Scripts.length, 1);
});

test('the Scripts section of Troubleshooting reads the run history and its chart', async function () {
    var opened = await openTool('troubleshooting', {
        'GET Scripts/History/Days': { Days: ['2026-11-19', '2026-11-20'], Succeeded: [1, 2], Failed: [0, 1] },
        'GET Scripts/Catalog': { Triggers: [{ Key: 'ItemAdded', Label: 'Item added' }] }
    });

    assert.deepEqual(texts(opened.section, '.stolon-subsection-title'), ['History', 'Repairs']);
    var clear = opened.section.querySelector('[data-action="clear-history"]').closest('.stolon-reset-row');
    assert.equal(clear.querySelector('.stolon-collapsible-tile').textContent, 'history_toggle_off');
    assert.ok(opened.page.requests.some(function (r) { return r.url === 'Scripts/History/Days'; }));
    assert.ok(opened.page.requests.some(function (r) { return r.url === 'Scripts/Catalog'; }));
});

test('the group editor has no Scripts section, since each script picks its own groups', async function () {
    var page = installPage({ config: allToolsOn() });
    var dom = page.dom;
    routeApi(page, { 'GET Scripts/Names': [{ Id: SCRIPT.replace(/-/g, ''), Name: 'Notify' }] });
    var shared = await loadScript('necto_shared.js');
    var groupsModule = await loadScript('necto_groups.js');
    var bag = shared.createNectoShared(pageView(dom, []));
    await bag.translations;
    var editor = groupsModule.createGroupEditor({ Id: 'g', Name: 'Family', MemberIds: [], Permissions: { Scripts: { AllScripts: false, ScriptIds: [SCRIPT] } } }, { shared: bag, users: [], groups: [] });
    dom.document.documentElement.appendChild(editor.element);
    await settle(50);

    assert.equal(editor.element.querySelector('[data-section="scripts"]'), null);
    var wireguard = editor.element.querySelector('[data-section="wireguard"]');
    assert.ok(wireguard.querySelector('[data-field="WireGuard.DataLimit"]'));
    assert.equal(wireguard.querySelector('[data-role="coming"]'), null);
});
