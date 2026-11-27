// necto_pages.test.mjs
// Tests for Pages: the rows, counts, notices, and checks the tool draws, its page and store editors, saving
// through the server's check, its Settings and Troubleshooting sections, the pages field of the group editor, and
// the loading screen of a signed in page.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { installPage, loadScript, pageView, settle, routeApi, texts, clickButton, clickDialogButton, dialogText, strings, allToolsOn } from './support.mjs';

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
    },
    plural: function (count, one, many) {
        return t.format.apply(null, [count === 1 ? one : many, count].concat(Array.prototype.slice.call(arguments, 3)));
    }
};

test('slugs and image names keep only the safe characters, as the server checks them', async function () {
    installPage({});
    var pages = await loadScript('necto_pages.js');

    assert.equal(pages.slugify('  My Page!! '), 'my-page');
    assert.equal(pages.slugify('a_b-c'), 'a_b-c');
    assert.equal(pages.slugify('***'), '');
    assert.equal(pages.sanitizeAssetName('My Logo.PNG'), 'my-logo.png');
    assert.equal(pages.sanitizeAssetName('..'), 'image');
    assert.equal(pages.base64Bytes('AAAA'), 3);
    assert.equal(pages.base64Bytes('AA=='), 1);
    assert.equal(pages.base64Bytes(''), 0);
});

test('a tier reads as a name or a number, and an unknown one falls back', async function () {
    installPage({});
    var pages = await loadScript('necto_pages.js');

    assert.equal(pages.normalizeVisibility(2), 'Admin');
    assert.equal(pages.normalizeVisibility('User'), 'User');
    assert.equal(pages.normalizeVisibility('nope'), 'Anonymous');
    assert.equal(pages.normalizeVisibility(9, 'Admin'), 'Admin');
    assert.equal(pages.normalizeScope(1), 'Own');
    assert.equal(pages.tierLabel('User', t), 'Signed in');
    assert.equal(pages.tierLabel('Admin', t), 'Administrators');
    assert.equal(pages.tierLabel('Anonymous', t), 'Anyone');
});

test('the page rows lead with each page title over its address, end with its tier or Unpublished, and tag system access', async function () {
    installPage({});
    var pages = await loadScript('necto_pages.js');
    var list = [
        { Slug: 'rules', Title: 'House rules', Visibility: 'User' },
        { Slug: 'tools', Title: 'Tools', Visibility: 'Admin', Enabled: false, ApiRoutes: [{ Name: 'rpc' }] },
        { Slug: 'bare', Title: '', Visibility: 'Anonymous' }
    ];

    var rows = pages.pageItems(list, t);

    assert.equal(rows[0].title, 'House rules');
    assert.equal(rows[0].secondary, '/pages/rules');
    assert.equal(rows[2].title, '/pages/bare');
    assert.equal(rows[2].secondary, '');
    assert.deepEqual(rows[0].tags, []);
    assert.deepEqual(rows[0].state, { label: 'Signed in', color: 'blue' });
    assert.match(rows[0].aside.innerHTML, /Signed in/);
    assert.deepEqual(rows[1].state, { label: 'Unpublished', color: 'gray' });
    assert.deepEqual(rows[1].tags.map(function (g) { return g.label; }), ['System access']);
    assert.deepEqual(rows[2].state, { label: 'Anyone', color: 'green' });
    assert.deepEqual(pages.pageCounts(list.slice(0, 2).concat([{ Slug: 'x' }])), { anyone: 1, user: 1, admin: 0, unpublished: 1 });
});

test('the store rows show who reads and writes and how full each store is, and the counts flag open stores', async function () {
    installPage({});
    var pages = await loadScript('necto_pages.js');
    var stores = [
        { Name: 'queue', ReadAccess: 'User', WriteAccess: 'Anonymous', MaxRecords: 50 },
        { Name: 'off', ReadAccess: 'Admin', WriteAccess: 'Anonymous', Enabled: false }
    ];

    var rows = pages.storeItems(stores, { queue: 4 }, t);

    assert.equal(rows[0].secondary, '4 of 50 records');
    assert.deepEqual(rows[0].tags.map(function (g) { return g.label; }), ['Read by Signed in', 'Written by Anyone']);
    assert.equal(rows[0].tags[1].color, 'orange');
    assert.equal(rows[0].aside, null);
    assert.equal(rows[1].secondary, '');
    assert.deepEqual(rows[1].state, { label: 'Off', color: 'gray' });
    assert.deepEqual(pages.storeCounts(stores, { queue: 4 }), { stores: 2, records: 4, open: 1 });
});

test('a store shows its warnings and scope only where they apply', async function () {
    installPage({});
    var pages = await loadScript('necto_pages.js');

    assert.deepEqual(pages.storeWarnings({ ReadAccess: 'Admin', WriteAccess: 'Anonymous', ReadScope: 'All' }), { anonymousWrite: true, showScope: false, scopeAllWrite: false });
    assert.deepEqual(pages.storeWarnings({ ReadAccess: 'User', WriteAccess: 'User', ReadScope: 'All' }), { anonymousWrite: false, showScope: true, scopeAllWrite: true });
    assert.deepEqual(pages.storeWarnings({ ReadAccess: 'User', WriteAccess: 'User', ReadScope: 'Own' }).scopeAllWrite, false);
    assert.equal(pages.storeWarnings({ ReadAccess: 'User', WriteAccess: 'Anonymous', Enabled: false }).anonymousWrite, false);
});

test('retention and the record limit come into the server range, and a new store takes the Settings values', async function () {
    installPage({});
    var pages = await loadScript('necto_pages.js');

    assert.equal(pages.readRetention('30', ''), 30);
    assert.equal(pages.readRetention('custom', '45'), 45);
    assert.equal(pages.readRetention('custom', '0'), 0);
    assert.equal(pages.readRetention('custom', '99999'), 3650);
    assert.equal(pages.readRecordLimit('-5'), 1);
    assert.equal(pages.readRecordLimit('500000'), 100000);
    assert.equal(pages.readRecordLimit('x'), 1000);
    var store = pages.newStore({ NewStoreMaxRecords: 200, NewStoreRetentionDays: 7, NewStoreNotifyOnWrite: true });
    assert.equal(store.MaxRecords, 200);
    assert.equal(store.RetentionDays, 7);
    assert.equal(store.NotifyOnWrite, true);
    assert.equal(store.ReadAccess, 'Admin');
    assert.equal(store.WriteAccess, 'User');
});

test('a page or store that clashes, is empty, or takes a reserved slug is refused before saving', async function () {
    installPage({});
    var pages = await loadScript('necto_pages.js');
    var list = [{ Slug: 'rules' }, { Slug: 'tools' }];

    assert.equal(pages.pageProblem({ Slug: '' }, list, -1), 'PagesErrorEnterSlug');
    assert.equal(pages.pageProblem({ Slug: 'Rules' }, list, 1), 'PagesErrorSlugTaken');
    assert.equal(pages.pageProblem({ Slug: 'rules' }, list, 0), null);
    assert.equal(pages.pageProblem({ Slug: 'asset' }, list, -1), 'PagesErrorSlugReservedHere');
    assert.equal(pages.storeProblem({ Name: '' }, [], -1), 'ErrorEnterName');
    assert.equal(pages.storeProblem({ Name: 'Queue' }, [{ Name: 'queue' }], -1), 'PagesErrorStoreNameTaken');
});

test('an uploaded image replaces one with its name and keeps that tier', async function () {
    installPage({});
    var pages = await loadScript('necto_pages.js');
    var assets = [{ Name: 'logo.png', ContentType: 'image/png', DataBase64: 'AAAA', Visibility: 'User' }];

    assert.equal(pages.mergeAsset(assets, 'Logo.png', 'data:image/png;base64,BBBB'), true);
    assert.equal(pages.mergeAsset(assets, 'banner.jpg', 'data:image/jpeg;base64,CCCC'), true);
    assert.equal(pages.mergeAsset(assets, 'bad.png', 'not a data address'), false);

    assert.equal(assets.length, 2);
    assert.deepEqual(assets[0], { Name: 'logo.png', ContentType: 'image/png', DataBase64: 'BBBB', Visibility: 'User' });
    assert.equal(assets[1].Visibility, 'Anonymous');
});

// shared
// The helpers the editors read.
function sharedBag() {
    return { t: t, setStatus: function () {} };
}

test('the page editor reads its fields, keeps each source pane, and keeps a stored route password', async function () {
    installPage({});
    var pages = await loadScript('necto_pages.js');
    var editor = pages.createPageEditor({
        Id: 'p1', Slug: 'rules', Title: 'Rules', Visibility: 'User', Html: '<p>hi</p>', Css: 'p{}', Js: '', Enabled: true,
        ApiRoutes: [{ Name: 'rpc', Url: 'http://nas.example.com/rpc', Username: 'u', Password: 'stored' }]
    }, sharedBag());
    var root = editor.element;

    var visibilityHelp = root.querySelector('[data-field="Visibility"]').parentNode.querySelectorAll('.fieldDescription');
    assert.equal(visibilityHelp.length, 1);
    assert.equal(visibilityHelp[0].textContent, 'Opens for administrators and the groups that give it in Groups.');
    assert.equal(root.querySelector('.stolon-info'), null);
    assert.equal(root.querySelector('[data-field="Source"]'), null);
    assert.equal(root.querySelector('[data-field="Unsandboxed"]').checked, true);
    root.querySelector('[data-field="Slug"]').value = 'House Rules';
    var tabs = root.querySelectorAll('[data-role="source-tabs"] [role="tab"]');
    assert.deepEqual(Array.prototype.map.call(tabs, function (n) { return n.textContent; }), ['HTML', 'CSS', 'JavaScript']);
    tabs[1].click();
    assert.equal(root.querySelector('[data-role="hint"]').textContent, 'Styles the page from a style element.');
    root.querySelector('[data-field="Code"]').value = 'body{}';

    var value = editor.getValue();
    assert.equal(value.Id, 'p1');
    assert.equal(value.Slug, 'house-rules');
    assert.equal(value.Css, 'body{}');
    assert.equal(value.Html, '<p>hi</p>');
    assert.equal(value.ApiRoutes.length, 1);
    assert.equal(value.ApiRoutes[0].Password, '__JPK_SECRET_KEPT__');
    editor.destroy();
});

test('the page editor drops the routes when system access is off and edits the document when single file is on', async function () {
    installPage({});
    var pages = await loadScript('necto_pages.js');
    var editor = pages.createPageEditor({ Slug: 'p', Visibility: 'Anonymous', ApiRoutes: [{ Name: 'rpc', Url: 'http://x.example.com' }], Unsandboxed: true }, sharedBag());
    var root = editor.element;

    var system = root.querySelector('[data-field="Unsandboxed"]');
    system.checked = false;
    system.dispatchEvent(new globalThis.Event('change'));
    var single = root.querySelector('[data-field="SingleFile"]');
    single.checked = true;
    single.dispatchEvent(new globalThis.Event('change'));
    root.querySelector('[data-field="Code"]').value = '<!DOCTYPE html>';

    var value = editor.getValue();
    assert.deepEqual(value.ApiRoutes, []);
    assert.equal(value.SingleFile, true);
    assert.equal(value.Document, '<!DOCTYPE html>');
    assert.equal(root.querySelector('[data-field="Visibility"]').parentNode.querySelector('.fieldDescription').textContent, 'Sets who may open the page.');
    assert.equal(root.querySelector('[data-role="source-tabs"]').className.indexOf('stolon-hidden') >= 0, true);
    editor.destroy();
});

test('the store editor keeps All for an Administrators store and reads a custom retention', async function () {
    installPage({});
    var pages = await loadScript('necto_pages.js');
    var editor = pages.createStoreEditor({ Name: 'queue', ReadAccess: 'Admin', ReadScope: 'Own', WriteAccess: 'User', MaxRecords: 10, RetentionDays: 45 }, 3, sharedBag());
    var root = editor.element;

    assert.equal(root.querySelector('[data-field="Retention"]').value, 'custom');
    assert.equal(root.querySelector('[data-field="RetentionDays"]').value, '45');

    var value = editor.getValue();
    assert.equal(value.ReadScope, 'All');
    assert.equal(value.RetentionDays, 45);
    assert.equal(value.MaxRecords, 10);
    editor.destroy();
});

// openTool
// Shows Settings or Troubleshooting over a view, answering its routes, and finds the Pages section.
async function openTool(name, config, answers) {
    var page = installPage({ config: config });
    var dom = page.dom;
    var container = name === 'settings' ? 'settingsSections' : 'troubleshootingSections';
    var view = pageView(dom, [dom.el('div', { id: 'settingsStatus' }), dom.el('div', { id: container })]);
    dom.document.documentElement.appendChild(view);
    var saved = [];
    globalThis.ApiClient.updatePluginConfiguration = function (id, next) { saved.push(JSON.parse(JSON.stringify(next))); return Promise.resolve(); };
    routeApi(page, answers || {});
    var mod = await loadScript('necto_' + name + '.js');
    mod.default(view);
    view.dispatchEvent(new globalThis.CustomEvent('viewshow'));
    await settle();
    return { page: page, dom: dom, view: view, saved: saved, section: view.querySelector('[data-tool="pages"]') };
}

test('the Pages section of Settings shows what a new store starts with and saves only those fields', async function () {
    var opened = await openTool('settings', { CustomPages: { Pages: [{ Slug: 'kept' }], NewStoreMaxRecords: 250, NewStoreRetentionDays: 7 } });
    var section = opened.section;

    assert.deepEqual(texts(section, '.stolon-subsection-title'), ['New stores']);
    assert.equal(section.querySelector('[data-field="NewStoreMaxRecords"]').value, '250');
    section.querySelector('[data-field="NewStoreRetentionDays"]').value = '99999';
    section.querySelector('[data-field="NewStoreNotifyOnWrite"]').checked = true;

    assert.equal(clickButton(section, 'Save'), true);
    await settle(50);

    var config = opened.saved[0];
    assert.equal(config.CustomPages.Pages[0].Slug, 'kept');
    assert.equal(config.CustomPages.NewStoreMaxRecords, 250);
    assert.equal(config.CustomPages.NewStoreRetentionDays, 3650);
    assert.equal(config.CustomPages.NewStoreNotifyOnWrite, true);
});

test('the Pages section of Troubleshooting lists each store with its records and asks before Clear', async function () {
    var cleared = [];
    var opened = await openTool('troubleshooting', { CustomPages: { Stores: [{ Name: 'queue', MaxRecords: 50 }] } }, {
        'GET Pages/Stores/queue/Stats': { count: 3, limit: 50 },
        'POST Pages/Stores/queue/Clear': function (r) { cleared.push(r); return { removed: 3 }; }
    });
    var section = opened.section;
    section.querySelector('[data-tool="pages"]');
    var load = opened.view.querySelector('[data-tool="pages"]');
    load.querySelector('.stolon-collapsible-header').click();
    await settle(50);

    assert.deepEqual(texts(section, '.stolon-subsection-title'), ['Stores', 'Scheduled checks', 'Settings check']);
    var row = section.querySelector('[data-store="queue"]');
    assert.match(row.textContent, /3 of 50 records/);
    row.querySelector('button').click();
    await settle(50);
    assert.equal(dialogText(opened.dom).title, 'Clear queue');
    assert.equal(clickDialogButton(opened.dom, 'Clear'), true);
    await settle(50);

    assert.equal(cleared.length, 1);
});

test('the Pages tool holds no settings, and a page saves only after the server check passes', async function () {
    var page = installPage({ config: { CustomPages: { Pages: [{ Id: 'p1', Slug: 'rules', Title: 'Rules', Visibility: 'User' }], Assets: [], Stores: [] } } });
    var dom = page.dom;
    var saved = [];
    globalThis.ApiClient.updatePluginConfiguration = function (id, next) { saved.push(JSON.parse(JSON.stringify(next))); return Promise.resolve(); };
    var checked = [];
    routeApi(page, {
        'POST Pages/Configuration/Check': function (r) { checked.push(r); return { ok: false, message: 'Two pages use the slug rules.' }; }
    });
    var view = pageView(dom, [dom.el('div', { id: 'pagesStatus' }), dom.el('div', { id: 'pagesTabs' })]);
    dom.document.documentElement.appendChild(view);
    var mod = await loadScript('necto_pages.js');
    mod.default(view);
    view.dispatchEvent(new globalThis.CustomEvent('viewshow'));
    await settle();

    assert.equal(view.querySelector('[data-tool]'), null);
    var rows = view.querySelectorAll('#pagesTabs .stolon-record-list-row');
    assert.equal(rows.length, 1);
    assert.match(rows[0].textContent, /\/pages\/rules/);

    rows[0].click();
    await settle(50);
    var sidecar = dom.document.querySelector('.stolon-sidecar');
    assert.equal(sidecar.querySelector('.stolon-sidecar-title').textContent, 'Rules');
    sidecar.querySelector('[data-field="Title"]').value = 'Changed';
    assert.equal(clickButton(sidecar, 'Save'), true);
    await settle(50);

    assert.equal(checked.length, 1);
    assert.equal(saved.length, 0);
    assert.match(sidecar.querySelector('[data-role="status"]').textContent, /Two pages use the slug rules\./);
});

test('the group editor offers the pages from the Pages route under Selective, hidden under Permissive', async function () {
    var page = installPage({ config: allToolsOn() });
    routeApi(page, { 'GET Pages': [{ Id: 'p1', Title: 'Rules', Slug: 'rules' }] });
    var groups = await loadScript('necto_groups.js');
    var shared = { t: t, request: function (route) { return globalThis.ApiClient.fetch({ url: route, type: 'GET' }); }, setStatus: function () {}, createUserMultiSelector: function () { return { element: page.dom.el('div', {}), ready: Promise.resolve(), getValue: function () { return []; } }; } };
    var editor = groups.createGroupEditor({ Id: 'g1', Name: 'Family', MemberIds: [], Permissions: { CustomPages: { AllPages: false, PageIds: ['p1'] } } }, { shared: shared, users: [], groups: [], devices: [] });
    await editor.ready;
    await settle(50);

    var section = editor.element.querySelector('[data-section="pages"]');
    assert.equal(section.querySelector('[data-role="coming"]'), null);
    var field = section.querySelector('[data-field="CustomPages.PageIds"]');
    assert.equal(field.className.indexOf('stolon-hidden') < 0, true);
    assert.deepEqual(editor.getValue().Permissions.CustomPages.PageIds, ['p1']);
    var scope = section.querySelector('[data-field="CustomPages.AllPages"]');
    assert.equal(scope.querySelector('input[value="some"]').checked, true);
    var permissive = scope.querySelector('input[value="all"]');
    permissive.checked = true;
    permissive.dispatchEvent(new globalThis.Event('change', { bubbles: true }));
    assert.equal(field.parentNode.className.indexOf('stolon-hidden') >= 0, true);
    assert.equal(editor.getValue().Permissions.CustomPages.AllPages, true);
    editor.destroy();
});

test('each image row draws Remove as a destructive row button', async function () {
    var page = installPage({ config: { CustomPages: { Pages: [], Assets: [{ Name: 'logo.png', ContentType: 'image/png', DataBase64: 'AAAA', Visibility: 'Anonymous' }], Stores: [] } } });
    var dom = page.dom;
    routeApi(page, {});
    var view = pageView(dom, [dom.el('div', { id: 'pagesStatus' }), dom.el('div', { id: 'pagesTabs' })]);
    dom.document.documentElement.appendChild(view);
    var mod = await loadScript('necto_pages.js');
    mod.default(view);
    view.dispatchEvent(new globalThis.CustomEvent('viewshow'));
    await settle();
    clickButton(view, 'Images');
    await settle(50);

    var remove = view.querySelector('[data-asset="logo.png"] [data-action="remove-asset"]');
    assert.ok(remove);
    assert.match(remove.className, /stolon-row-btn/);
    assert.match(remove.className, /stolon-destructive/);
    assert.equal(remove.getAttribute('title'), 'Remove');
});

test('the group editor offers only Signed in pages, and keeps a page the group already gives', async function () {
    installPage({});
    var groups = await loadScript('necto_groups.js');
    var pages = [
        { Id: 'open', Visibility: 'Anonymous' }, { Id: 'members', Visibility: 'User' }, { Id: 'staff', Visibility: 'Admin' },
        { Id: 'numbered', Visibility: 1 }, { Id: 'moved', Visibility: 'Anonymous' }
    ];

    assert.deepEqual(groups.grantablePages(pages, ['moved']).map(function (p) { return p.Id; }), ['members', 'numbered', 'moved']);
    assert.deepEqual(groups.grantablePages(null, []), []);
});

test('the loading screen sends only this server\'s sign-ins, each once, and reads each answer', async function () {
    installPage({});
    var loader = await loadScript('necto_pages_loader.js');
    var servers = [
        { Id: 'abc-123', AccessToken: 'one' },
        { Id: 'other', AccessToken: 'two', ManualAddress: 'https://elsewhere.example.com' },
        { Id: 'other2', AccessToken: 'three', LocalAddress: 'https://media.example.com:443/jf' },
        { Id: 'ABC123', AccessToken: 'one' },
        { Id: 'abc123' },
        null
    ];

    assert.deepEqual(loader.tokensFor(servers, 'abc123', 'https://media.example.com'), ['one', 'three']);
    assert.deepEqual(loader.tokensFor(servers, '', 'https://nowhere.example.com'), []);
    assert.deepEqual(loader.tokensFor('broken', 'abc123', 'https://media.example.com'), []);
    assert.equal(loader.screenFor(401), 'next');
    assert.equal(loader.screenFor(403), 'denied');
    assert.equal(loader.screenFor(404), 'missing');
    assert.equal(loader.screenFor(200), 'page');
});

test('the loading screen tries the stored sign-in, then asks to sign in again with the expired sentence', async function () {
    var page = installPage({ storage: { jellyfin_credentials: JSON.stringify({ Servers: [{ Id: 'abc123', AccessToken: 'stale' }] }) } });
    var loader = await loadScript('necto_pages_loader.js');
    var doc = page.dom.document;
    var data = doc.createElement('div');
    data.id = 'pageLoading';
    data.setAttribute('data-slug', 'family');
    data.setAttribute('data-tier', 'user');
    data.setAttribute('data-denied', 'No.');
    data.setAttribute('data-sign-in', 'Sign in to open.');
    data.setAttribute('data-expired', 'Expired.');
    doc.body.appendChild(data);
    var gate = doc.createElement('div');
    gate.className = 'stolon-user-page-gate stolon-shell-gate';
    doc.body.appendChild(gate);
    var asked = [];
    globalThis.fetch = function (url, init) {
        asked.push({ url: String(url), auth: init && init.headers && init.headers.Authorization });
        if (String(url).indexOf('System/Info/Public') >= 0) return Promise.resolve({ ok: true, status: 200, json: function () { return Promise.resolve({ Id: 'abc123', ServerName: 'Home' }); } });
        return Promise.resolve({ ok: false, status: 401, json: function () { return Promise.resolve({}); }, text: function () { return Promise.resolve(''); } });
    };

    await loader.start(doc, { pathname: '/pages/family', origin: 'http://localhost' });

    assert.ok(asked.some(function (a) { return a.url === '/pages/family/user' && a.auth === 'MediaBrowser Token="stale"'; }));
    var text = gate.textContent;
    assert.ok(text.indexOf('Expired.') >= 0, 'the sign in says the stored sign-in expired: ' + text);
});
