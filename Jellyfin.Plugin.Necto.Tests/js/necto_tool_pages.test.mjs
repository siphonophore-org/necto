// necto_tool_pages.test.mjs
// Loads the WireGuard and Scripts tool pages from their own markup, so a test catches a page that cannot draw.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { installPage, loadScript, paths, settle, routeApi, clickButton } from './support.mjs';

globalThis.setTimeout = function () { return 1; };
globalThis.clearTimeout = function () {};
globalThis.setInterval = function () { return 1; };
globalThis.clearInterval = function () {};

var VOID = ['input', 'br', 'link', 'img', 'hr', 'meta'];

// parseMarkup
// Builds fake elements from a page's markup: tags, attributes, and the text of leaf elements. Comments are skipped.
//
// Inputs
// dom [object]: The fake DOM helper.
// html [string]: The markup.
//
// Output
// [object]: The root element, a div holding the markup.
function parseMarkup(dom, html) {
    var root = dom.el('div', {});
    var stack = [root];
    var source = html.replace(/<!--[\s\S]*?-->/g, '');
    var re = /<\/?([a-zA-Z0-9-]+)((?:\s+[^\s=>/]+(?:\s*=\s*(?:"[^"]*"|'[^']*'|[^\s>]+))?)*)\s*(\/?)>|([^<]+)/g;
    var m;
    while ((m = re.exec(source))) {
        var top = stack[stack.length - 1];
        if (m[4] !== undefined) {
            var text = m[4].replace(/\s+/g, ' ').trim();
            if (text && !top.children.length) top.textContent = (top.textContent ? top.textContent + ' ' : '') + text;
            continue;
        }
        var tag = m[1].toLowerCase();
        if (m[0].startsWith('</')) {
            if (stack.length > 1) stack.pop();
            continue;
        }
        var attrs = {};
        (m[2] || '').replace(/([^\s=]+)(?:\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s>]+)))?/g, function (all, name, a, b, c) {
            attrs[name] = a !== undefined ? a : b !== undefined ? b : c !== undefined ? c : '';
            return '';
        });
        var node = dom.el(tag, {});
        Object.keys(attrs).forEach(function (k) {
            if (k === 'class') node.className = attrs[k];
            else node.setAttribute(k, attrs[k]);
        });
        if (tag === 'input' && attrs.type === 'checkbox') node.checked = false;
        top.appendChild(node);
        if (!m[3] && VOID.indexOf(tag) < 0) stack.push(node);
    }
    return root;
}

// openMarkupPage
// Shows a tool page built from its own markup, answering its routes.
async function openMarkupPage(name, config, answers) {
    var page = installPage({ config: config });
    var dom = page.dom;
    // The fake document has no text nodes, so a text node stands in as a span holding the text.
    dom.document.createTextNode = function (text) { var span = dom.el('span', {}); span.textContent = text; return span; };
    var proto = Object.getPrototypeOf(dom.el('div', {}));
    if (!proto.replaceWith) proto.replaceWith = function (next) { this.parentNode.insertBefore(next, this); this.parentNode.removeChild(this); };
    var html = readFileSync(join(paths.configDir, name + '.html'), 'utf8');
    var view = parseMarkup(dom, html).querySelector('[data-role="page"]');
    dom.document.documentElement.appendChild(view);
    globalThis.ApiClient.getUsers = function () { return Promise.resolve([]); };
    globalThis.ApiClient.getCurrentUserId = function () { return 'u'; };
    routeApi(page, answers || {});
    var errors = [];
    var onError = function (e) { errors.push(e); };
    process.on('unhandledRejection', onError);
    var mod = await loadScript(name + '.js');
    mod.default(view);
    view.dispatchEvent(new globalThis.CustomEvent('viewshow'));
    await settle(80);
    process.off('unhandledRejection', onError);
    return { page: page, view: view, dom: dom, errors: errors };
}

test('the Scripts tool draws a saved script from its own markup', async function () {
    var opened = await openMarkupPage('necto_scripts', {
        Scripts: { Scripts: [{ Id: 'cccccccc000000000000000000000001', Name: 'Notify', Enabled: true, AllGroups: false, GroupIds: ['g'], Triggers: ['ItemAdded'], ItemKinds: [], Conditions: [], Operations: [{ Type: 'RunCommand', Command: '/bin/true' }], Secrets: [] }] }
    }, {
        'GET Scripts/Catalog': { Triggers: [{ Key: 'ItemAdded', Label: 'Item added', Category: 'Library', HasItem: true, Variables: [] }], Fields: [], ItemKinds: [], Collections: [], Libraries: [], Users: [], Tasks: [], ForEachVariables: { Items: [], Users: [] }, FailureVariables: [] },
        'GET Groups': [{ Id: 'e', Name: 'Everyone', IsEveryone: true }, { Id: 'g', Name: 'Family' }],
        'GET Scripts/Status': []
    });

    assert.deepEqual(opened.errors, []);
    assert.match(opened.view.querySelector('#selectScript').innerHTML, /Notify/);
    var tiles = Array.prototype.map.call(opened.view.querySelectorAll('[data-fold] > .stolon-collapsible-header .stolon-collapsible-tile, [data-fold] .stolon-collapsible-tile'), function (n) { return n.textContent; });
    assert.deepEqual(Array.from(new Set(tiles)), ['description', 'bolt', 'groups', 'rule', 'format_list_bulleted', 'key', 'settings', 'science']);
    assert.equal(opened.view.querySelectorAll('[data-fold] .stolon-collapsible-content.stolon-flow').length, 8);
    assert.deepEqual(Array.prototype.map.call(opened.view.querySelectorAll('.stolon-selector-actions button'), function (b) { return b.id; }),
        ['btnNewScript', 'btnRenameScript', 'btnRemoveScript', 'btnImportScripts', 'btnExportScripts']);
    assert.equal(opened.view.querySelector('#scriptEditor').classList.contains('stolon-hidden'), false);
    assert.equal(opened.view.querySelector('#scriptGroupScope input[value="some"]').checked, true);
    assert.equal(opened.view.querySelector('#scriptGroupPicks').classList.contains('stolon-hidden'), false);
    assert.match(opened.view.querySelector('#groupList').textContent, /Everyone/);
    assert.equal(opened.view.querySelector('#groupList .stolon-filtered-check[data-id="g"]').checked, true);
    assert.equal(opened.view.querySelector('#groupWarn').classList.contains('stolon-hidden'), true);
});

test('a script is Permissive until Restrictive is selected, then saves the groups picked', async function () {
    var saved = [];
    var opened = await openMarkupPage('necto_scripts', {
        Scripts: { Scripts: [{ Id: 'cccccccc000000000000000000000001', Name: 'Notify', Enabled: true, Triggers: ['ItemAdded'], ItemKinds: [], Conditions: [], Operations: [{ Type: 'RunCommand', Command: '/bin/true' }], Secrets: [] }] }
    }, {
        'GET Scripts/Catalog': { Triggers: [{ Key: 'ItemAdded', Label: 'Item added', Category: 'Library', HasItem: true, Variables: [] }], Fields: [], ItemKinds: [], Collections: [], Libraries: [], Users: [], Tasks: [], ForEachVariables: { Items: [], Users: [] }, FailureVariables: [] },
        'GET Groups': [{ Id: 'e', Name: 'Everyone', IsEveryone: true }, { Id: 'g', Name: 'Family' }],
        'GET Scripts/Status': []
    });
    globalThis.ApiClient.updatePluginConfiguration = function (id, config) { saved.push(JSON.parse(JSON.stringify(config))); return Promise.resolve(); };
    var view = opened.view;
    var all = view.querySelector('#scriptGroupScope input[value="all"]');
    var some = view.querySelector('#scriptGroupScope input[value="some"]');

    assert.equal(all.checked, true);
    assert.equal(view.querySelector('#scriptGroupPicks').classList.contains('stolon-hidden'), true);
    some.checked = true;
    all.checked = false;
    some.dispatchEvent(new globalThis.Event('change', { bubbles: true }));
    assert.equal(view.querySelector('#scriptGroupPicks').classList.contains('stolon-hidden'), false);
    assert.equal(view.querySelector('#groupWarn').classList.contains('stolon-hidden'), false);
    var family = view.querySelector('#groupList .stolon-filtered-check[data-id="g"]');
    family.checked = true;
    family.dispatchEvent(new globalThis.Event('change', { bubbles: true }));
    assert.equal(view.querySelector('#groupWarn').classList.contains('stolon-hidden'), true);

    clickButton(view, 'Save');
    await settle(80);
    var script = saved[saved.length - 1].Scripts.Scripts[0];
    assert.equal(script.AllGroups, false);
    assert.deepEqual(script.GroupIds, ['g']);
});

test('the WireGuard tool draws from its own markup', async function () {
    var opened = await openMarkupPage('necto_wireguard', { WireGuard: { Enabled: true, HostEnabled: true, Peers: [], Connections: [] } }, {
        'GET WireGuard/Peers': [], 'GET WireGuard/Status': [], 'GET WireGuard/Usage': { Bytes: {}, Blocked: [], Refused: {} },
        'GET WireGuard/Sidecar': { Installed: false, Platform: 'linux-amd64', ReleaseVersion: '1.0.0' }
    });

    assert.deepEqual(opened.errors, []);
    assert.equal(opened.view.querySelector('#wireguardNoSidecar').classList.contains('stolon-hidden'), false);
    assert.equal(opened.view.querySelector('#btnAddPeer').classList.contains('stolon-hidden'), false);
});
