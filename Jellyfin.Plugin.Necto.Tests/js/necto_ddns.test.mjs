// necto_ddns.test.mjs
// Tests for the DDNS tool: the record rows and their results, the record editor and the fields each provider uses,
// the run summary, the page drawing its records, and the DDNS sections of Settings and Troubleshooting.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { installPage, loadScript, pageView, settle, routeApi, texts, clickButton } from './support.mjs';

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

test('recordItems names each record, its provider and host, its last result, and whether it is off', async function () {
    var bag = await strings();
    var ddns = await loadScript('necto_ddns.js');
    var items = ddns.recordItems([
        { Record: { Id: 'a', Name: '', Hostname: 'home.example.com', Provider: 'Cloudflare', Enabled: true, LastAction: 'Updated', LastCheckedUtc: '2026-11-26T10:00:00Z' }, Followed: [] },
        { Record: { Id: 'b', Name: 'Office', Hostname: '', Provider: 'DynDns2', Enabled: false, FollowProxyHosts: true }, Followed: [{ Hostname: 'media.example.com', LastAction: 'Failed' }, { Hostname: 'x.example.com', LastAction: 'Updated' }] }
    ], PROVIDERS, bag.t);

    assert.equal(items[0].title, 'home.example.com');
    assert.equal(items[0].secondary, 'Cloudflare · home.example.com');
    assert.deepEqual(items[0].tags.map(function (t) { return t.label; }), ['Updated']);
    assert.equal(items[1].title, 'Office');
    assert.equal(items[1].secondary, 'DynDNS v2 · Follows 2 proxy names');
    assert.deepEqual(items[1].tags.map(function (t) { return t.label; }), ['Failed', 'Off']);
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
    assert.equal(editor.validate(), 'DdnsErrorEnterHostname');
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
    assert.match(help(), /record ID, not a name/);
    field('Provider').value = 'Inwx';
    field('Provider').dispatchEvent(new globalThis.Event('change'));
    assert.equal(hidden('FollowProxyHosts'), true);
    assert.equal(hidden('Hostname'), true);
    field('Provider').value = 'Cloudflare';
    field('Provider').dispatchEvent(new globalThis.Event('change'));
    assert.equal(hidden('FollowProxyHosts'), false);
    assert.equal(help(), 'The DNS name this record keeps pointed at this server.');
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

test('outcomeMessage counts the records updated and failed', async function () {
    var bag = await strings();
    var ddns = await loadScript('necto_ddns.js');
    assert.deepEqual(ddns.outcomeMessage({ Records: [{ Success: true }, { Success: false }, { Success: true, Skipped: true }], Warnings: [] }, bag), { text: '1 record updated. 1 record failed.', error: true });
    assert.deepEqual(ddns.outcomeMessage({ Records: [{ Success: true, Skipped: true }], Warnings: [] }, bag), { text: 'Every record is up to date.', error: false });
    assert.equal(ddns.outcomeMessage({ Records: [], Warnings: [] }, bag).text, 'No records to update.');
});

test('the page draws the public address and a row per record', async function () {
    var page = installPage({});
    var dom = page.dom;
    var view = pageView(dom, [
        dom.el('div', { id: 'ddnsNotices' }), dom.el('div', { id: 'ddnsAddress' }), dom.el('div', { id: 'ddnsRecords' }),
        dom.el('button', { id: 'btnAddRecord' }), dom.el('button', { id: 'btnUpdateNow' }), dom.el('span', { id: 'ddnsStatus' })
    ]);
    dom.document.documentElement.appendChild(view);
    routeApi(page, {
        'GET Ddns/Providers': PROVIDERS,
        'GET Ddns/Records': { Records: [{ Record: { Id: 'a', Hostname: 'home.example.com', Provider: 'Cloudflare', Enabled: true }, Followed: [] }], DetectedIPv4: '203.0.113.10', DetectedIPv6: '', SecretsPlaintext: true }
    });
    var mod = await loadScript('necto_ddns.js');
    mod.default(view);
    view.dispatchEvent(new globalThis.CustomEvent('viewshow'));
    await settle();

    assert.ok(view.querySelector('#ddnsAddress').textContent.indexOf('203.0.113.10') >= 0);
    assert.ok(view.querySelector('#ddnsRecords').textContent.indexOf('home.example.com') >= 0);
    assert.equal(view.querySelectorAll('#ddnsNotices .stolon-warn').length, 1);
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
