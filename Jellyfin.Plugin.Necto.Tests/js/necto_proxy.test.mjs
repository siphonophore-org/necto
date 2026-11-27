// necto_proxy.test.mjs
// Tests for the Proxy tool: the route rows and their certificate tags, the route editor, the status cards and
// notices, the activity row actions, the page, and the Proxy sections of Settings and Troubleshooting.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { installPage, loadScript, pageView, settle, routeApi, texts, clickButton } from './support.mjs';

globalThis.setTimeout = function () { return 1; };
globalThis.clearTimeout = function () {};
globalThis.setInterval = function () { return 1; };
globalThis.clearInterval = function () {};

// strings
// Loads the shared module and its strings, as a page has them.
async function strings() {
    var page = installPage({});
    var shared = await loadScript('necto_shared.js');
    var bag = shared.createNectoShared(pageView(page.dom, []), null);
    return bag.translations.then(function () { return bag; });
}

var ROUTES = [
    { Id: 'a', Enabled: true, Host: 'media.example.com', Upstream: '', LocalOnly: false, Certificate: { Mode: 'Automatic', Challenge: 'Auto' } },
    { Id: 'b', Enabled: false, Host: 'home.example.com', Upstream: 'http://192.168.1.20:8096', LocalOnly: true, Certificate: { Mode: 'None' } }
];

test('routeItems names each route, where it forwards, its certificate, and who may reach it', async function () {
    var bag = await strings();
    var proxy = await loadScript('necto_proxy.js');
    var items = proxy.routeItems(ROUTES, [{ Id: 'a', Certificate: { Loaded: true, NotAfterUtc: new Date(Date.now() + 60 * 86400000).toISOString() }, Acme: {} }], bag.t);

    assert.equal(items[0].title, 'media.example.com');
    assert.equal(items[0].secondary, 'This Jellyfin');
    assert.deepEqual(items[0].tags.map(function (t) { return t.label; }), ['HTTPS ready']);
    assert.equal(items[1].secondary, 'http://192.168.1.20:8096');
    assert.deepEqual(items[1].tags.map(function (t) { return t.label; }), ['HTTP only', 'Local only', 'Off']);
});

test('certificateTag tells a certificate expiring soon, one being requested, and one that failed', async function () {
    var bag = await strings();
    var proxy = await loadScript('necto_proxy.js');
    var route = ROUTES[0];
    assert.equal(proxy.certificateTag({ Certificate: { Loaded: true, NotAfterUtc: new Date(Date.now() + 86400000).toISOString() }, Acme: {} }, route, bag.t).label, 'Expires soon');
    assert.equal(proxy.certificateTag({ Certificate: {}, Acme: { Issuing: true } }, route, bag.t).color, 'purple');
    assert.equal(proxy.certificateTag({ Certificate: {}, Acme: { LastError: 'no' } }, route, bag.t).color, 'red');
    assert.equal(proxy.certificateTag(null, route, bag.t).label, 'No certificate yet');
});

test('the route editor reads an upstream only for another service and shows what each source uses', async function () {
    var bag = await strings();
    var proxy = await loadScript('necto_proxy.js');
    var editor = proxy.createRouteEditor(ROUTES[1], { shared: bag, status: null, records: [{ Id: 'r1', Name: 'Cloudflare' }], hostnames: [] });
    var field = function (name) { return editor.element.querySelector('[data-field="' + name + '"]'); };
    var hidden = function (name) { return field(name).closest('.inputContainer, .checkboxContainer, .selectContainer').classList.contains('stolon-hidden'); };

    assert.equal(editor.getValue().Upstream, 'http://192.168.1.20:8096');
    assert.equal(hidden('Certificate.Challenge'), true);
    field('Certificate.Mode').value = 'Automatic';
    field('Certificate.Mode').dispatchEvent(new globalThis.Event('change'));
    assert.equal(hidden('Certificate.Challenge'), false);
    field('Certificate.Challenge').value = 'Http';
    field('Certificate.Challenge').dispatchEvent(new globalThis.Event('change'));
    assert.equal(editor.validate(), 'ProxyErrorHttpChallengeNeedsInternet');

    field('Target').value = 'jellyfin';
    field('Target').dispatchEvent(new globalThis.Event('change'));
    assert.equal(editor.getValue().Upstream, '');
    field('Host').value = '';
    assert.equal(editor.validate(), 'ProxyErrorRouteNeedsHost');
});

test('statusCards and notices sum how the proxy stands', async function () {
    var bag = await strings();
    var proxy = await loadScript('necto_proxy.js');
    var status = { Enabled: true, Running: false, Error: 'Port 443 is in use.', TakeoverPhase: 'Managed', Routes: [{ Id: 'a', Active: true }], Name: { Hostname: 'media.example.com', Resolves: false, Error: 'It points elsewhere.' }, EveryoneLooksLocal: true };
    var cards = proxy.statusCards(status, ROUTES, bag.t);
    assert.deepEqual(cards.map(function (c) { return c.count; }), ['Stopped', '1 / 2', 'Trusted', 'Points elsewhere']);
    var list = proxy.notices(status, bag.t).map(function (n) { return n.text; });
    assert.ok(list.indexOf('Port 443 is in use.') >= 0);
    assert.ok(list.indexOf('It points elsewhere.') >= 0);
    assert.equal(proxy.notices({ Enabled: false }, bag.t)[0].text, 'The proxy is off. Turn it on in Settings to forward its routes.');
});

test('rowAction unbans a ban, never blocks your network, and lets a country through', async function () {
    var proxy = await loadScript('necto_proxy.js');
    assert.deepEqual(proxy.rowAction({ Banned: true, Ip: '203.0.113.5' }), { action: 'unban', value: '203.0.113.5' });
    assert.equal(proxy.rowAction({ Exempt: true, Ip: '192.168.1.2' }), null);
    assert.deepEqual(proxy.rowAction({ Listed: true, Ip: '203.0.113.6' }), { action: 'unblock', value: '203.0.113.6' });
    assert.equal(proxy.rowAction({ Covered: true, Ip: '203.0.113.7' }), null);
    assert.equal(proxy.rowAction({ CountryBlocked: true, CountryCode: 'FR', Country: 'France' }).action, 'unblock-country');
    assert.equal(proxy.rowAction({ Ip: '198.51.100.9' }).action, 'block');
});

test('the page draws its notices, cards and a row per route', async function () {
    var page = installPage({});
    var dom = page.dom;
    var view = pageView(dom, ['proxyNotices', 'proxyCards', 'proxyRoutes', 'proxyJellyfin', 'proxyVisitors'].map(function (id) { return dom.el('div', { id: id }); }).concat([dom.el('button', { id: 'btnAddRoute' }), dom.el('span', { id: 'proxyStatus' })]));
    dom.document.documentElement.appendChild(view);
    routeApi(page, {
        'GET Proxy/Routes': { Routes: ROUTES, Status: [] },
        'GET Proxy/Status': { Enabled: false, Routes: [] },
        'GET Proxy/Visitors': { Recording: false, Activity: [], Banned: [], PeopleByCountry: [] }
    });
    var mod = await loadScript('necto_proxy.js');
    mod.default(view);
    view.dispatchEvent(new globalThis.CustomEvent('viewshow'));
    await settle();

    assert.ok(view.querySelector('#proxyRoutes').textContent.indexOf('media.example.com') >= 0);
    assert.ok(view.querySelector('#proxyNotices').textContent.indexOf('The proxy is off.') >= 0);
    assert.deepEqual(texts(view, '.stolon-collapsible-title'), ['Jellyfin', 'Visitors']);
});

test('the Proxy Settings section saves a value equal to its default as unset', async function () {
    var proxy = await loadScript('necto_proxy_sections.js');
    var inputs = {};
    proxy.advancedSpecs().forEach(function (sub) {
        sub.fields.forEach(function (spec) {
            inputs[spec.key] = { input: { value: '', checked: false } };
        });
    });
    inputs.HttpsPort.input.value = '443';
    inputs.KeepAliveSeconds.input.value = '30';
    inputs.TrustedNetworks.input.value = '10.0.0.0/8\n\n';
    inputs.CountryMode.input.value = 'Block';
    var out = proxy.readAdvanced(inputs, { Values: { HttpsPort: 443, KeepAliveSeconds: 120, CountryMode: 'ServerCountry' } }, { getValue: function () { return ['FR']; } });

    assert.equal(out.HttpsPort, null);
    assert.equal(out.KeepAliveSeconds, 30);
    assert.deepEqual(out.TrustedNetworks, ['10.0.0.0/8']);
    assert.equal(out.CountryMode, 'Block');
    assert.deepEqual(out.Countries, ['FR']);
    assert.equal(proxy.isAuto({ Standard: { MaxConnections: 4000 }, LowMemory: { MaxConnections: 500 } }, 'MaxConnections'), true);
});

test('Settings and Troubleshooting draw the Proxy and DDNS sections, filled from their routes', async function () {
    var page = installPage({ config: {} });
    var dom = page.dom;
    var view = pageView(dom, [dom.el('div', { id: 'settingsStatus' }), dom.el('div', { id: 'settingsSections' })]);
    dom.document.documentElement.appendChild(view);
    routeApi(page, {
        'GET Proxy/Settings': { Enabled: true, AcceptTerms: false, Advanced: { HttpsPort: 8443 }, Visitors: { Enabled: true, HistoryDays: 30, Location: 'City' } },
        'GET Proxy/Defaults': { Values: { HttpPort: 80, HttpsPort: 443 } },
        'GET Ddns/Settings': { ForceUpdateHours: 0, BackoffAfterFailures: 3, BackoffHours: 24, RequestTimeoutSeconds: 15 }
    });
    var mod = await loadScript('necto_settings.js');
    mod.default(view);
    view.dispatchEvent(new globalThis.CustomEvent('viewshow'));
    await settle();

    var section = view.querySelector('[data-tool="proxy"]');
    assert.equal(section.querySelector('[data-field="Enabled"]').checked, true);
    assert.equal(section.querySelector('[data-field="HttpsPort"]').value, '8443');
    assert.equal(section.querySelector('[data-field="HttpPort"]').value, '80');
    assert.equal(section.querySelector('[data-field="Visitors.Location"]').value, 'City');
    section.querySelector('[data-field="AcceptTerms"]').checked = true;
    section.querySelector('[data-field="AcceptTerms"]').dispatchEvent(new globalThis.Event('change', { bubbles: true }));
    assert.equal(clickButton(section, 'Save'), true);
    await settle(50);

    var post = page.requests.find(function (r) { return r.type === 'POST' && r.url === 'Proxy/Settings'; });
    assert.ok(post);
    var body = JSON.parse(post.data);
    assert.equal(body.AcceptTerms, true);
    assert.equal(body.Advanced.HttpsPort, 8443);
    assert.equal(body.Advanced.HttpPort, null);
    assert.ok(page.requests.find(function (r) { return r.type === 'POST' && r.url === 'Proxy/Visitors/Settings'; }));
});

test('the Proxy Troubleshooting section shows the state and the recovery file', async function () {
    var page = installPage({ config: {} });
    var dom = page.dom;
    var view = pageView(dom, [dom.el('div', { id: 'troubleshootingSections' })]);
    dom.document.documentElement.appendChild(view);
    routeApi(page, {
        'GET Proxy/Status': { Running: true, Addresses: ['http://[::]:80'], Backend: 'http://127.0.0.1:8096/', DisableFlagPath: '/config/Jellyfin.Plugin.Necto.Proxy/DISABLE', Reachability: {} },
        'GET Proxy/Defaults': { IsLowMemory: false, MemoryMb: 8192 },
        'GET Ddns/Records': { Records: [] }
    });
    var mod = await loadScript('necto_troubleshooting.js');
    mod.default(view);
    view.dispatchEvent(new globalThis.CustomEvent('viewshow'));
    await settle();

    var section = view.querySelector('[data-tool="proxy"]');
    assert.ok(section.textContent.indexOf('/config/Jellyfin.Plugin.Necto.Proxy/DISABLE') >= 0);
    assert.ok(section.textContent.indexOf('Running') >= 0);
    assert.ok(view.querySelector('[data-tool="ddns"]').textContent.indexOf('Public IPv4') >= 0);
});
