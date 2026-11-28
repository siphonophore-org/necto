// necto_scripts.test.mjs
// Tests for Scripts: the shared labels, the importable examples in the docs, the Settings section, the run history in Troubleshooting, and
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

test('every importable example in the docs names its script and uses known triggers and step types', async function () {
    installPage({});
    var common = await loadScript('necto_scripts_common.js');
    var fs = await import('node:fs');
    var path = await import('node:path');
    var url = await import('node:url');
    var docs = fs.readFileSync(path.join(path.dirname(url.fileURLToPath(import.meta.url)), '..', '..', 'docs', 'Scripts.md'), 'utf8');
    var types = common.operationTypes();
    var blocks = docs.split('### Importable Examples')[1].split('```json').slice(1).map(function (b) { return JSON.parse(b.split('```')[0]); });

    assert.ok(blocks.length >= 17);
    blocks.forEach(function (script) {
        assert.ok(script.Name, JSON.stringify(script));
        assert.ok((script.Triggers || []).length > 0, script.Name);
        assert.ok((script.Operations || []).length > 0, script.Name);
        script.Operations.forEach(function (op) { assert.ok(types[op.Type], script.Name + ' ' + op.Type); });
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

test('the Scripts section of Troubleshooting holds only Clear history, and the History tab reads the runs with its table first', async function () {
    var opened = await openTool('troubleshooting', {
        'GET Scripts/History/Days': { Days: ['2026-11-19', '2026-11-20'], Succeeded: [1, 2], Failed: [0, 1] },
        'GET Scripts/Catalog': { Triggers: [{ Key: 'ItemAdded', Label: 'Item added' }] }
    });

    assert.deepEqual(texts(opened.section, '.stolon-subsection-title'), ['Repairs']);
    var clear = opened.section.querySelector('[data-action="clear-history"]').closest('.stolon-reset-row');
    assert.equal(clear.querySelector('.stolon-collapsible-tile').textContent, 'history_toggle_off');
    assert.equal(opened.page.requests.some(function (r) { return r.url === 'Scripts/History/Days'; }), false);

    var shared = await loadScript('necto_shared.js');
    var bag = shared.createNectoShared(pageView(opened.dom, []));
    await bag.translations;
    var sections = await loadScript('necto_scripts_sections.js');
    var history = sections.createRunHistory(bag, null);
    opened.dom.document.documentElement.appendChild(history.element);
    await history.load();
    await settle(50);
    assert.equal(opened.page.requests.some(function (r) { return r.url === 'Scripts/History/Days'; }), false);
    assert.ok(opened.page.requests.some(function (r) { return String(r.url).indexOf('Scripts/History?') >= 0; }));
    assert.equal(history.element.children[0].getAttribute('data-role'), 'history-table');
    assert.equal(history.element.getAttribute('data-role'), 'run-history');
    history.destroy();
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
