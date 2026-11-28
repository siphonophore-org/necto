// necto_ddns.test.mjs
// Tests for the DDNS tool: the record rows and their results, the record editor and the fields each provider uses,
// the run summary, the page drawing its records, and the DDNS sections of Settings and Troubleshooting.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { installPage, loadScript, pageView, settle, routeApi, routeApiByPath, pickRows, rowHtml, texts, clickButton, clickDialogButton, dialogText } from './support.mjs';

globalThis.setTimeout = function () { return 1; };
globalThis.clearTimeout = function () {};
globalThis.setInterval = function () { return 1; };
globalThis.clearInterval = function () {};

var PROVIDERS = [
    { Value: 'Cloudflare', Label: 'Cloudflare', Hint: 'Use an API token.', Txt: true, Follow: true, Fields: { Hostname: 'Hostname', Login: null, Password: 'API token', Zone: 'Zone', Server: false, Ttl: true, IPv6: true, Advanced: [] } },
    { Value: 'DynDns2', Label: 'DynDNS v2', Hint: '', Txt: false, Follow: true, Fields: { Hostname: 'Hostname', Login: 'Username', Password: 'Password', Zone: null, Server: true, Ttl: false, IPv6: false, Advanced: ['wildcard', 'mx', 'backupmx'] } },
    { Value: 'DnsMadeEasy', Label: 'DNS Made Easy', Hint: '', Txt: false, Follow: false, Fields: { Hostname: 'Record ID', Login: 'Username', Password: 'Password', Zone: null, Server: false, Ttl: false, IPv6: true, Advanced: [] } },
    { Value: 'Inwx', Label: 'INWX', Hint: '', Txt: false, Follow: false, Fields: { Hostname: null, Login: 'Username', Password: 'Password', Zone: null, Server: false, Ttl: false, IPv6: true, Advanced: [] } }
];

// strings
// Loads the shared module and its strings, as a page has them.
async function strings() {
    var page = installPage({});
    var shared = await loadScript('necto_shared.js');
    var bag = shared.createNectoShared(pageView(page.dom, []), null);
    return bag.translations.then(function () { return bag; });
}

test('a record row names it once, with its provider, and ends with its state, Off or its last result', async function () {
    var bag = await strings();
    var ddns = await loadScript('necto_ddns.js');
    var first = { Record: { Id: 'a', Name: '', Hostname: 'home.example.com', Provider: 'Cloudflare', Enabled: true, LastAction: 'Updated', LastCheckedUtc: '2026-11-26T10:00:00Z' }, Followed: [], State: 'UpToDate' };
    var second = { Record: { Id: 'b', Name: 'Office', Hostname: '', Provider: 'DynDns2', Enabled: false, FollowProxyHosts: true }, Followed: [{ Hostname: 'media.example.com', LastAction: 'Failed' }, { Hostname: 'x.example.com', LastAction: 'Updated' }], State: 'Off' };

    assert.equal(ddns.recordName(first.Record, PROVIDERS), 'home.example.com');
    assert.equal(ddns.recordLine(first, PROVIDERS, bag.t), 'Cloudflare');
    assert.deepEqual(ddns.recordBadge(first, bag.t), { label: 'Up to date', color: 'green' });
    assert.equal(ddns.recordName(second.Record, PROVIDERS), 'Office');
    assert.equal(ddns.recordLine(second, PROVIDERS, bag.t), 'DynDNS v2 · Follows 2 proxy names');
    assert.deepEqual(ddns.recordBadge(second, bag.t), { label: 'Off', color: 'gray' });
    assert.deepEqual(ddns.stateFilters(bag.t).map(function (f) { return f.label; }), ['Up to date', 'Needs attention', 'Not checked', 'Off']);
});

test('resultTag says a record that never ran is not checked yet', async function () {
    var bag = await strings();
    var ddns = await loadScript('necto_ddns.js');
    assert.equal(ddns.resultTag({}, bag.t).label, 'Not checked yet');
    assert.equal(ddns.resultTag({ LastAction: 'Backing off' }, bag.t).color, 'orange');
});

test('the record editor shows only the fields the provider uses and reads them back', async function () {
    var bag = await strings();
    var ddns = await loadScript('necto_ddns.js');
    var editor = ddns.createRecordEditor({ Id: 'a', Provider: 'DynDns2', Hostname: 'home.example.com', Login: 'stored', Password: 'stored', Enabled: true, UpdateIPv4: true, UpdateEveryMinutes: 30 }, { shared: bag, providers: PROVIDERS });
    var field = function (name) { return editor.element.querySelector('[data-field="' + name + '"]'); };
    var hidden = function (name) { return field(name).closest('.inputContainer, .checkboxContainer, .selectContainer').classList.contains('stolon-hidden'); };

    assert.equal(hidden('Zone'), true);
    assert.equal(hidden('Login'), false);
    assert.equal(hidden('UpdateIPv6'), true);
    assert.equal(hidden('Mx'), false);

    field('UpdateEveryMinutes').value = '2';
    var value = editor.getValue();
    assert.equal(value.Provider, 'DynDns2');
    assert.equal(value.Zone, '');
    assert.equal(value.UpdateIPv6, false);
    assert.equal(value.UpdateEveryMinutes, 5);
    assert.equal(value.Login, '__JPK_SECRET_KEPT__');

    field('Provider').value = 'Cloudflare';
    field('Provider').dispatchEvent(new globalThis.Event('change'));
    assert.equal(hidden('Zone'), false);
    assert.equal(hidden('Login'), true);
    assert.equal(editor.getValue().Login, '');
});

test('a record that follows the proxy needs no hostname, and one with no address family is refused', async function () {
    var bag = await strings();
    var ddns = await loadScript('necto_ddns.js');
    var editor = ddns.createRecordEditor({ Provider: 'Cloudflare', Hostname: '', UpdateIPv4: true }, { shared: bag, providers: PROVIDERS });
    assert.equal(editor.validate(), 'ErrorEnterHostName');
    var follow = editor.element.querySelector('[data-field="FollowProxyHosts"]');
    follow.checked = true;
    follow.dispatchEvent(new globalThis.Event('change'));
    assert.equal(editor.validate(), null);
    editor.element.querySelector('[data-field="UpdateIPv4"]').checked = false;
    assert.equal(editor.validate(), 'DdnsErrorSelectFamily');
});

test('Follow the proxy shows only for a provider whose hostname holds a name, and the hostname help says so', async function () {
    var bag = await strings();
    var ddns = await loadScript('necto_ddns.js');
    var editor = ddns.createRecordEditor({ Provider: 'DnsMadeEasy', Hostname: '12345', FollowProxyHosts: true, UpdateIPv4: true }, { shared: bag, providers: PROVIDERS });
    var field = function (name) { return editor.element.querySelector('[data-field="' + name + '"]'); };
    var hidden = function (name) { return field(name).closest('.inputContainer, .checkboxContainer, .selectContainer').classList.contains('stolon-hidden'); };
    var help = function () { return field('Hostname').closest('.inputContainer').querySelector('.fieldDescription').textContent; };

    assert.equal(hidden('FollowProxyHosts'), true);
    assert.equal(hidden('Hostname'), false);
    assert.equal(editor.getValue().FollowProxyHosts, false);
    assert.match(help(), /record's ID, not its name/);
    field('Provider').value = 'Inwx';
    field('Provider').dispatchEvent(new globalThis.Event('change'));
    assert.equal(hidden('FollowProxyHosts'), true);
    assert.equal(hidden('Hostname'), true);
    field('Provider').value = 'Cloudflare';
    field('Provider').dispatchEvent(new globalThis.Event('change'));
    assert.equal(hidden('FollowProxyHosts'), false);
    assert.equal(help(), 'Keeps this DNS name pointed at this server.');
});

test('Follow the proxy hides while the proxy is off, unless the record already follows it', async function () {
    var bag = await strings();
    var ddns = await loadScript('necto_ddns.js');
    var hidden = function (editor) { return editor.element.querySelector('[data-field="FollowProxyHosts"]').closest('.checkboxContainer').classList.contains('stolon-hidden'); };
    var off = ddns.createRecordEditor({ Provider: 'Cloudflare', Hostname: 'home.example.com', UpdateIPv4: true }, { shared: bag, providers: PROVIDERS, proxyOn: false });
    var following = ddns.createRecordEditor({ Provider: 'Cloudflare', FollowProxyHosts: true, UpdateIPv4: true }, { shared: bag, providers: PROVIDERS, proxyOn: false });
    var on = ddns.createRecordEditor({ Provider: 'Cloudflare', Hostname: 'home.example.com', UpdateIPv4: true }, { shared: bag, providers: PROVIDERS, proxyOn: true });

    assert.equal(hidden(off), true);
    assert.equal(off.getValue().FollowProxyHosts, false);
    assert.equal(hidden(following), false);
    assert.equal(following.getValue().FollowProxyHosts, true);
    assert.equal(hidden(on), false);
});

test('the record editor has one name field and sections led by icon tiles', async function () {
    var bag = await strings();
    var ddns = await loadScript('necto_ddns.js');
    var editor = ddns.createRecordEditor({ Id: 'a', Name: 'Old name', Provider: 'Cloudflare', Hostname: 'home.example.com', UpdateIPv4: true }, { shared: bag, providers: PROVIDERS });
    var tiles = Array.prototype.map.call(editor.element.querySelectorAll('.stolon-collapsible-tile'), function (n) { return n.textContent; });

    assert.equal(editor.element.querySelector('[data-field="Name"]'), null);
    assert.equal(editor.element.querySelectorAll('.stolon-subsection-title').length, 0);
    assert.deepEqual(tiles, ['dns', 'update', 'tune', 'history']);
    assert.equal(new Set(tiles).size, tiles.length);
    assert.equal(editor.getValue().Name, '');
});

test('countCards counts the records, those up to date and those that need attention', async function () {
    var bag = await strings();
    var ddns = await loadScript('necto_ddns.js');
    var cards = ddns.countCards({
        Records: [
            { Record: { Enabled: true, LastAction: 'Updated' }, Followed: [], State: 'UpToDate' },
            { Record: { Enabled: true, LastAction: 'Failed' }, Followed: [], State: 'Attention' },
            { Record: { Enabled: true, FollowProxyHosts: true }, Followed: [{ LastAction: 'Backing off' }, { LastAction: 'Updated' }], State: 'Attention' },
            { Record: { Enabled: false, LastAction: 'Failed' }, Followed: [], State: 'Off' }
        ],
        DetectedIPv4: '203.0.113.10',
        DetectedIPv6: ''
    }, bag.t);

    assert.deepEqual(cards.map(function (c) { return [c.label, c.count]; }), [
        ['Up to date', 1], ['Needs attention', 2], ['Not checked', 0], ['Off', 1]
    ]);
    assert.deepEqual(cards.map(function (c) { return c.color; }), ['green', 'orange', 'gray', 'gray']);
});

test('addressCards gives the public addresses in a row of their own, gray when not found', async function () {
    var bag = await strings();
    var ddns = await loadScript('necto_ddns.js');
    var cards = ddns.addressCards({ DetectedIPv4: '203.0.113.10', DetectedIPv6: '' }, bag.t);

    assert.deepEqual(cards.map(function (c) { return [c.label, c.count, c.color]; }), [
        ['Public IPv4', '203.0.113.10', 'blue'], ['Public IPv6', 'Not found', 'gray']
    ]);
});

test('the provider field links its setup guide on the docs site', async function () {
    var bag = await strings();
    var ddns = await loadScript('necto_ddns.js');
    var editor = ddns.createRecordEditor({ Provider: 'Cloudflare', UpdateIPv4: true }, { shared: bag, providers: PROVIDERS });
    var link = editor.element.querySelector('[data-role="provider-guide"]');

    assert.equal(link.getAttribute('href'), 'https://necto.siphonophore.org/providers/Cloudflare/');
    assert.equal(link.textContent, 'Setup guide for Cloudflare');
    assert.equal(link.getAttribute('target'), '_blank');
    assert.equal(ddns.guideUrl('DynDns2'), 'https://necto.siphonophore.org/providers/DynDns2/');
});

// openDdns
// Shows the DDNS tool over its markup with these records.
async function openDdns(entries) {
    var page = installPage({});
    var dom = page.dom;
    var view = pageView(dom, [dom.el('div', { id: 'ddnsNotices' }), dom.el('div', { id: 'ddnsCards' }), dom.el('div', { id: 'ddnsAddresses' }), dom.el('div', { id: 'ddnsStatus' }), dom.el('div', { id: 'ddnsRecords' })]);
    dom.document.documentElement.appendChild(view);
    routeApiByPath(page, {
        'GET Ddns/Providers': PROVIDERS,
        'GET Ddns/Records': { Records: entries, DetectedIPv4: '203.0.113.10', DetectedIPv6: '', SecretsPlaintext: true },
        'GET Ddns/Records/All': { Items: entries, TotalCount: entries.length }
    });
    var mod = await loadScript('necto_ddns.js');
    mod.default(view);
    view.dispatchEvent(new globalThis.CustomEvent('viewshow'));
    await settle(50);
    return { page: page, view: view, dom: dom };
}

var RECORDS = [
    { Record: { Id: 'a', Hostname: 'home.example.com', Provider: 'Cloudflare', Enabled: true, LastAction: 'Updated' }, Followed: [], State: 'UpToDate' },
    { Record: { Id: 'b', Name: 'Cabin', Hostname: 'cabin.example.com', Provider: 'DynDns2', Enabled: false }, Followed: [], State: 'Off' }
];

test('the page draws the cards with the public address and a table row per record', async function () {
    var opened = await openDdns(RECORDS);
    var view = opened.view;

    assert.ok(view.querySelector('#ddnsAddresses').textContent.indexOf('203.0.113.10') >= 0);
    var rows = view.querySelectorAll('#ddnsRecords .stolon-table-row');
    assert.equal(rows.length, 2);
    assert.match(rowHtml(rows[0]), /home\.example\.com[\s\S]*Cloudflare[\s\S]*Up to date/);
    assert.match(rowHtml(rows[1]), /Cabin[\s\S]*Off/);
    assert.equal(view.querySelectorAll('#ddnsNotices .stolon-warn').length, 1);
    assert.ok(opened.page.requests.some(function (r) { return /^Ddns\/Records\/All\?/.test(r.url); }));
});

test('the toolbar has search, the state filter, and Add record, and the bulk bar Enable, Disable, and Remove', async function () {
    var opened = await openDdns(RECORDS);
    var controls = opened.view.querySelector('#ddnsRecords .stolon-table-controls');

    assert.equal(controls.querySelector('input').getAttribute('placeholder'), 'Search records');
    assert.deepEqual(texts(controls, 'select option'), ['All', 'Up to date', 'Needs attention', 'Not checked', 'Off']);
    assert.deepEqual(texts(controls, 'button.stolon-button'), ['addAdd record']);
    assert.deepEqual(texts(opened.view.querySelector('.stolon-table-bulk-actions'), '[data-bulk]'), ['Enable', 'Disable', 'Remove']);
});

test('with no records the table frame says No records yet, with no loose line or second Add record', async function () {
    var opened = await openDdns([]);
    var view = opened.view;

    assert.match(view.querySelector('#ddnsRecords').textContent, /No records yet\./);
    assert.doesNotMatch(view.textContent, /Select Add record/);
    assert.equal(view.querySelectorAll('#btnAddRecord').length, 1);
    assert.ok(view.querySelector('.stolon-table-controls #btnAddRecord'));
});

test('bulk Disable puts each record picked with Enabled off, skipping the one already off', async function () {
    var opened = await openDdns(RECORDS);
    pickRows(opened.view, [0, 1]);
    opened.view.querySelector('[data-bulk="disable"]').click();
    await settle(50);

    var puts = opened.page.requests.filter(function (r) { return r.type === 'PUT'; });
    assert.deepEqual(puts.map(function (r) { return r.url; }), ['Ddns/Records/a']);
    assert.equal(JSON.parse(puts[0].data).Enabled, false);
    assert.match(opened.view.querySelector('#ddnsStatus').textContent, /Disabled 1 record\. Skipped 1 record already disabled\./);
});

test('bulk Remove asks first, then removes every record picked', async function () {
    var opened = await openDdns(RECORDS);
    pickRows(opened.view, [0, 1]);
    opened.view.querySelector('[data-bulk="remove"]').click();
    await settle(50);
    assert.equal(dialogText(opened.dom).title, 'Remove 2 records');
    clickDialogButton(opened.dom, 'Remove');
    await settle(50);

    assert.equal(opened.page.requests.filter(function (r) { return r.type === 'DELETE'; }).length, 2);
});

test('the DDNS Settings section fills from its route and saves through it', async function () {
    var page = installPage({ config: {} });
    var dom = page.dom;
    var view = pageView(dom, [dom.el('div', { id: 'settingsStatus' }), dom.el('div', { id: 'settingsSections' })]);
    dom.document.documentElement.appendChild(view);
    routeApi(page, {
        'GET Ddns/Settings': { IPv4DetectionUrl: 'https://a.example.com\nhttps://b.example.com', IPv6DetectionUrl: '', SkipInternalAddresses: true, ForceUpdateHours: 0, BackoffAfterFailures: 3, BackoffHours: 24, RequestTimeoutSeconds: 15 },
        'GET Proxy/Settings': {}, 'GET Proxy/Defaults': { Values: {} }
    });
    var mod = await loadScript('necto_settings.js');
    mod.default(view);
    view.dispatchEvent(new globalThis.CustomEvent('viewshow'));
    await settle();

    var section = view.querySelector('[data-tool="ddns"]');
    assert.deepEqual(texts(section, '.stolon-subsection-title'), ['Public IP', 'Updates']);
    assert.equal(section.querySelector('[data-field="IPv4DetectionUrl"]').value, 'https://a.example.com\nhttps://b.example.com');
    section.querySelector('[data-field="RequestTimeoutSeconds"]').value = '30';
    section.querySelector('[data-field="RequestTimeoutSeconds"]').dispatchEvent(new globalThis.Event('input', { bubbles: true }));
    assert.equal(clickButton(section, 'Save'), true);
    await settle(50);

    var post = page.requests.find(function (r) { return r.type === 'POST' && r.url === 'Ddns/Settings'; });
    assert.ok(post);
    assert.equal(JSON.parse(post.data).RequestTimeoutSeconds, 30);
});

test('outcomeMessage counts the records updated and failed', async function () {
    var bag = await strings();
    var sections = await loadScript('necto_ddns_sections.js');
    assert.deepEqual(sections.outcomeMessage({ Records: [{ Success: true }, { Success: false }, { Success: true, Skipped: true }], Warnings: [] }, bag), { text: '1 record updated. 1 record failed.', error: true });
    assert.deepEqual(sections.outcomeMessage({ Records: [{ Success: true, Skipped: true }], Warnings: [] }, bag), { text: 'Every record is up to date.', error: false });
    assert.equal(sections.outcomeMessage({ Records: [], Warnings: [] }, bag).text, 'No records to update.');
});
