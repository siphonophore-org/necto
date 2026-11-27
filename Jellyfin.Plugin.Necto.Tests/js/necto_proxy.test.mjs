// necto_proxy.test.mjs
// Tests for the Proxy tool: the route rows and their certificate tags, the route editor, the status cards and
// notices, the activity row actions, the page, and the Proxy sections of Settings and Troubleshooting.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { installPage, loadScript, pageView, settle, routeApi, texts, clickButton, clickDialogButton, dialogText } from './support.mjs';

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

test('routeItems names each route, where it forwards, and who may reach it, and ends it with Off or its certificate', async function () {
    var bag = await strings();
    var proxy = await loadScript('necto_proxy.js');
    var items = proxy.routeItems(ROUTES, [{ Id: 'a', Certificate: { Loaded: true, NotAfterUtc: new Date(Date.now() + 60 * 86400000).toISOString() }, Acme: {} }], bag.t);

    assert.equal(items[0].title, 'media.example.com');
    assert.equal(items[0].secondary, 'Jellyfin');
    assert.deepEqual(items[0].tags, []);
    assert.deepEqual(items[0].state, { label: 'HTTPS ready', color: 'green' });
    assert.match(items[0].aside.innerHTML, /HTTPS ready/);
    assert.equal(items[1].secondary, 'http://192.168.1.20:8096');
    assert.deepEqual(items[1].tags.map(function (t) { return t.label; }), ['Local only']);
    assert.deepEqual(items[1].state, { label: 'Off', color: 'gray' });
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
    assert.equal(editor.validate(), 'ErrorEnterHostName');
});

test('a route to Jellyfin picks a preset, Custom opens its fields from the last preset, and one to another service shows every setting', async function () {
    var bag = await strings();
    var proxy = await loadScript('necto_proxy.js');
    var editor = proxy.createRouteEditor(ROUTES[0], { shared: bag, status: null, records: [], hostnames: [] });
    var field = function (name) { return editor.element.querySelector('[data-field="' + name + '"]'); };
    var hidden = function (node) { return node.closest('.stolon-hidden') !== null; };
    var summary = editor.element.querySelector('[data-role="profile-summary"]');
    var radios = function () { return Array.prototype.slice.call(field('Options.Profile').querySelectorAll('input[type="radio"]')); };
    var pick = function (name) {
        radios().forEach(function (r) { r.checked = r.value === name; });
        radios().find(function (r) { return r.value === name; }).dispatchEvent(new globalThis.Event('change', { bubbles: true }));
    };

    assert.deepEqual(radios().map(function (r) { return r.value; }), ['Recommended', 'LowMemory', 'HighBandwidth', 'Custom']);
    assert.equal(radios().find(function (r) { return r.checked; }).value, 'Recommended');
    assert.match(field('Options.Profile').textContent, /Low memory[\s\S]*Holds fewer connections/);
    assert.deepEqual([hidden(summary), hidden(field('Options.Jellyfin.MaxBodyMb')), hidden(field('Options.WebSockets'))], [false, true, true]);
    assert.match(summary.textContent, /Same as Settings/);
    assert.match(summary.textContent, /360 minutes/);

    pick('LowMemory');
    assert.match(summary.textContent, /20 MB/);
    var low = editor.getValue().Options;
    assert.deepEqual([low.Profile, low.MaxBodyMb, low.TimeoutSeconds, low.MediaTimeoutMinutes, low.BackendConnections, low.WebSockets, low.Buffering, low.PreserveHost],
        ['LowMemory', 20, 120, 60, 32, true, false, true]);

    pick('Custom');
    assert.deepEqual([hidden(summary), hidden(field('Options.Jellyfin.MaxBodyMb'))], [true, false]);
    assert.equal(field('Options.Jellyfin.BackendConnections').value, '32');
    field('Options.Jellyfin.MediaTimeoutMinutes').value = '90';
    assert.deepEqual([editor.getValue().Options.Profile, editor.getValue().Options.MediaTimeoutMinutes, editor.getValue().Options.MaxBodyMb], ['Custom', 90, 20]);

    field('Target').value = 'other';
    field('Target').dispatchEvent(new globalThis.Event('change'));
    field('Upstream').value = 'http://192.168.1.20:8080';
    assert.deepEqual([hidden(field('Options.Profile')), hidden(field('Options.WebSockets')), hidden(field('Options.PathPrefix'))], [true, false, false]);
    field('Options.PathPrefix').value = '/qb';
    field('Options.TimeoutSeconds').value = '90';
    field('Options.PreserveHost').checked = false;
    var value = editor.getValue().Options;
    assert.deepEqual([value.PathPrefix, value.TimeoutSeconds, value.PreserveHost, value.WebSockets, value.MaxBodyMb], ['/qb', 90, false, true, null]);
    assert.equal(editor.validate(), null);
    var icons = Array.prototype.map.call(editor.element.querySelectorAll('.stolon-collapsible-tile'), function (n) { return n.textContent; });
    assert.equal(new Set(icons).size, icons.length);
    assert.equal(proxy.profileName(2), 'HighBandwidth');
    assert.equal(proxy.profileName('Seerr'), 'Recommended');
});

test('a DDNS name locks the host, and a wildcard takes a subdomain in front of its base', async function () {
    var bag = await strings();
    var proxy = await loadScript('necto_proxy.js');
    var names = [{ Hostname: 'home.example.com' }, { Hostname: '*.example.com' }];
    var editor = proxy.createRouteEditor(Object.assign({}, ROUTES[0], { Host: 'media.example.com', DdnsHostname: '*.example.com' }), { shared: bag, status: null, records: [], hostnames: names });
    var field = function (name) { return editor.element.querySelector('[data-field="' + name + '"]'); };
    var hidden = function (name) { return field(name).closest('.inputContainer, .selectContainer').classList.contains('stolon-hidden'); };
    var order = Array.prototype.map.call(editor.element.querySelectorAll('[data-field="DdnsHostname"], [data-field="Subdomain"], [data-field="Host"]'), function (n) { return n.getAttribute('data-field'); });

    assert.deepEqual(order, ['DdnsHostname', 'Subdomain', 'Host']);
    assert.equal(field('Subdomain').value, 'media');
    assert.equal(hidden('Subdomain'), false);
    assert.equal(hidden('Host'), true);
    assert.equal(field('Host').readOnly, true);
    field('Subdomain').value = '*';
    field('Subdomain').dispatchEvent(new globalThis.Event('input'));
    assert.deepEqual([editor.getValue().Host, editor.getValue().DdnsHostname, editor.validate()], ['*.example.com', '*.example.com', null]);
    field('Subdomain').value = 'a.b';
    assert.equal(editor.validate(), 'ProxyErrorEnterSubdomain');

    field('DdnsHostname').value = 'home.example.com';
    field('DdnsHostname').dispatchEvent(new globalThis.Event('change'));
    assert.deepEqual([hidden('Subdomain'), hidden('Host'), field('Host').value, field('Host').readOnly, editor.getValue().Host], [true, false, 'home.example.com', true, 'home.example.com']);
    field('DdnsHostname').value = '';
    field('DdnsHostname').dispatchEvent(new globalThis.Event('change'));
    field('Host').value = 'free.example.org';
    assert.deepEqual([field('Host').readOnly, editor.getValue().Host, editor.getValue().DdnsHostname], [false, 'free.example.org', '']);
    assert.equal(proxy.fitsDdnsName('x.example.com', '*.example.com'), true);
    assert.equal(proxy.hostFor('*.example.com', ''), '');
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
    assert.deepEqual(proxy.notices({ Enabled: false, CountryRule: 'Only visitors from United States can connect.' }, bag.t), []);
});

test('rowAction unbans a ban, never blocks your network, and lets a country through', async function () {
    var proxy = await loadScript('necto_proxy.js');
    assert.deepEqual(proxy.rowAction({ Banned: true, Ip: '203.0.113.5' }), { action: 'unban', value: '203.0.113.5' });
    assert.deepEqual(proxy.rowAction({ Exempt: true, Ip: '192.168.1.2' }), { action: 'block', value: '192.168.1.2', disabled: true, reason: 'ProxyTextNeverBlocked' });
    assert.deepEqual(proxy.rowAction({ Listed: true, Ip: '203.0.113.6' }), { action: 'unblock', value: '203.0.113.6' });
    assert.deepEqual(proxy.rowAction({ Covered: true, Ip: '203.0.113.7' }), { action: 'unblock', value: '203.0.113.7', disabled: true, reason: 'ProxyBlockedByRange' });
    var country = proxy.rowAction({ CountryBlocked: true, CountryCode: 'FR', Country: 'France' });
    assert.equal(country.action, 'unblock-country');
    assert.equal(country.country, 'France');
    assert.equal(country.reason, 'ProxyAllowCountry');
    assert.equal(proxy.rowAction({ Ip: '198.51.100.9' }).action, 'block');
});

test('countryRows joins the totals and keeps the latest visit, and locationText says when lookups are off', async function () {
    var bag = await strings();
    var proxy = await loadScript('necto_proxy.js');
    var rows = proxy.countryRows({
        PeopleByCountry: [{ CountryCode: 'FR', Country: 'France', Count: 2, LastUtc: '2026-11-01T00:00:00Z' }],
        FailedByCountry: [{ CountryCode: 'FR', Country: 'France', Count: 1, LastUtc: '2026-11-20T00:00:00Z' }],
        BlockedByCountry: [{ CountryCode: 'CN', Country: 'China', Count: 4, LastUtc: '2026-11-05T00:00:00Z' }]
    });

    assert.deepEqual(rows.map(function (r) { return [r.Code, r.People, r.Failed, r.Blocked, r.LastUtc]; }), [
        ['FR', 2, 1, 0, '2026-11-20T00:00:00Z'],
        ['CN', 0, 0, 4, '2026-11-05T00:00:00Z']
    ]);
    assert.equal(proxy.locationText({ Detail: 'Off', Loaded: false }, bag), 'Location lookups are off.');
    assert.equal(proxy.locationText({ Detail: 2 }, bag), 'Location lookups are off.');
    assert.equal(proxy.locationText({ Detail: 'Country', Loaded: false }, bag), 'Not loaded');
    assert.equal(proxy.showsCity({ Settings: { Location: 'City' } }), true);
    assert.equal(proxy.showsCity({ Settings: { Location: 'Country' } }), false);
});

test('certificateItems adds the self-signed root expiry and fingerprint', async function () {
    var bag = await strings();
    var proxy = await loadScript('necto_proxy.js');
    var items = proxy.certificateItems({ Certificate: { Loaded: true, Names: ['media.example.com'], Issuer: 'Necto' }, Acme: {} }, bag, { NotAfterUtc: '2036-01-01T00:00:00Z', Fingerprint: 'AB:CD' });
    var labels = items.map(function (i) { return i.label; });

    assert.ok(labels.indexOf('Root expires') >= 0);
    assert.equal(items.find(function (i) { return i.label === 'Root SHA-256'; }).value, 'AB:CD');
    assert.equal(proxy.certificateItems({ Certificate: { Loaded: true, Names: [] }, Acme: {} }, bag, null).some(function (i) { return i.label === 'Root SHA-256'; }), false);
});

test('the route editor turns Request certificate off until the proxy can order one', async function () {
    var bag = await strings();
    var proxy = await loadScript('necto_proxy.js');
    var editor = proxy.createRouteEditor(ROUTES[0], { shared: bag, status: null, records: [], hostnames: [], requestable: false });
    var request = editor.element.querySelector('[data-action="request"]');

    assert.equal(request.disabled, true);
    assert.equal(request.getAttribute('title'), 'Turn on the proxy and accept the Let\'s Encrypt terms in Settings first.');
    editor.setRequestable(true);
    assert.equal(request.disabled, false);
});

test('while the proxy is off the page says so in place of its cards and sections, and keeps its routes listed', async function () {
    var page = installPage({});
    var dom = page.dom;
    var view = pageView(dom, ['proxyOff', 'proxyNotices', 'proxyCards', 'proxyRoutes', 'proxyActions', 'proxyJellyfin', 'proxyVisitors'].map(function (id) { return dom.el('div', { id: id }); }).concat([dom.el('button', { id: 'btnAddRoute' }), dom.el('span', { id: 'proxyStatus' })]));
    dom.document.documentElement.appendChild(view);
    routeApi(page, {
        'GET Proxy/Routes': { Routes: ROUTES, Status: [] },
        'GET Proxy/Status': { Enabled: false, Routes: [] },
        'GET Proxy/Visitors': { Recording: false, Activity: [], Banned: [], PeopleByCountry: [] }
    });
    var titles = [];
    view.addEventListener('stolontooltitle', function (e) { titles.push(e.detail); });
    var mod = await loadScript('necto_proxy.js');
    mod.default(view);
    view.dispatchEvent(new globalThis.CustomEvent('viewshow'));
    await settle();

    assert.equal(titles[titles.length - 1], 'Proxy');
    assert.ok(view.querySelector('#proxyRoutes').textContent.indexOf('media.example.com') >= 0);
    assert.match(view.querySelector('#proxyOff').innerHTML, /The proxy is off\. Turn it on in Settings to forward its routes\./);
    assert.equal(view.querySelector('#proxyOff').classList.contains('stolon-hidden'), false);
    assert.equal(view.querySelector('#proxyNotices').classList.contains('stolon-hidden'), true);
    ['proxyCards', 'proxyActions', 'proxyJellyfin', 'proxyVisitors'].forEach(function (id) {
        assert.equal(view.querySelector('#' + id).classList.contains('stolon-hidden'), true, id);
    });
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
    // An empty LAN address goes as an empty text, since the server refuses null for it.
    assert.equal(out.LanAddress, '');
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
    assert.ok(view.querySelector('[data-tool="ddns"]').textContent.indexOf('Public IPv4') < 0);
});

// openProxyPage
// Shows the Proxy page over answers for its routes, recording each timer the page starts.
//
// Inputs
// answers [object]: Extra or changed answers by 'METHOD path'.
//
// Output
// [object]: page, dom, view, and timers, each { fn, ms } the page scheduled.
async function openProxyPage(answers) {
    var page = installPage({});
    var dom = page.dom;
    var timers = [];
    globalThis.setTimeout = function (fn, ms) { timers.push({ fn: fn, ms: ms }); return timers.length; };
    globalThis.setInterval = function (fn, ms) { timers.push({ fn: fn, ms: ms }); return timers.length; };
    var view = pageView(dom, ['proxyOff', 'proxyNotices', 'proxyCards', 'proxyRoutes', 'proxyActions', 'proxyJellyfin', 'proxyVisitors'].map(function (id) { return dom.el('div', { id: id }); }).concat([dom.el('button', { id: 'btnAddRoute' }), dom.el('span', { id: 'proxyStatus' })]));
    dom.document.documentElement.appendChild(view);
    routeApi(page, Object.assign({
        'GET Proxy/Routes': { Routes: ROUTES, Status: [] },
        'GET Proxy/Status': { Enabled: true, Running: true, Routes: [], TakeoverPhase: 'Unmanaged' },
        'GET Proxy/Settings': { AcceptTerms: true },
        'GET Proxy/Visitors': { Recording: true, Activity: [], Banned: [], PeopleByCountry: [], Settings: { Location: 'Country' } }
    }, answers || {}));
    var mod = await loadScript('necto_proxy.js');
    mod.default(view);
    view.dispatchEvent(new globalThis.CustomEvent('viewshow'));
    await settle();
    return { page: page, dom: dom, view: view, timers: timers };
}

// posts
// Lists the POSTs a page sent to a route.
function posts(opened, url) {
    return opened.page.requests.filter(function (r) { return r.type === 'POST' && r.url === url; });
}

test('Trust the proxy asks first, and Cancel sends nothing', async function () {
    var opened = await openProxyPage();
    opened.view.querySelector('[data-action="trust"]').click();
    await settle(50);

    var dialog = dialogText(opened.dom);
    assert.equal(dialog.title, 'Trust the proxy');
    assert.match(dialog.body, /restarts once/);
    clickDialogButton(opened.dom, 'Cancel');
    await settle(50);
    assert.equal(posts(opened, 'Proxy/Trust').length, 0);

    opened.view.querySelector('[data-action="trust"]').click();
    await settle(50);
    clickDialogButton(opened.dom, 'Trust');
    await settle(50);
    assert.equal(posts(opened, 'Proxy/Trust').length, 1);
});

test('the page reads the status every 10 seconds and the visitors every 30, and keeps the Jellyfin section', async function () {
    var opened = await openProxyPage();
    var count = function (url) { return opened.page.requests.filter(function (r) { return r.url === url; }).length; };
    var tick = function (ms) { return opened.timers.filter(function (x) { return x.ms === ms; }).pop(); };
    var before = count('Proxy/Status');
    var section = opened.view.querySelector('#proxyJellyfin').firstChild;

    assert.ok(tick(10000), 'a 10 second poller');
    assert.ok(tick(30000), 'a 30 second poller');
    tick(10000).fn();
    await settle(50);
    assert.equal(count('Proxy/Status'), before + 1);
    assert.equal(opened.view.querySelector('#proxyJellyfin').firstChild, section);
    var seen = count('Proxy/Visitors');
    tick(30000).fn();
    await settle(50);
    assert.equal(count('Proxy/Visitors'), seen + 1);
});

test('the visitor tables show the city, when each ban began, and why a row cannot act', async function () {
    var opened = await openProxyPage({
        'GET Proxy/Visitors': {
            Recording: true,
            Settings: { Location: 'City' },
            Banned: [{ Ip: '203.0.113.9', Country: 'France', Failures: 5, BannedUtc: '2026-11-20T10:00:00Z' }],
            Activity: [
                { Type: 'Seen', Ip: '192.168.1.2', IsLocal: true, Exempt: true, LastUtc: '2026-11-20T10:00:00Z' },
                { Type: 'Blocked', Ip: '203.0.113.7', Country: 'France', CountryCode: 'FR', City: 'Paris', Covered: true, LastUtc: '2026-11-20T10:00:00Z' }
            ],
            PeopleByCountry: [{ CountryCode: 'FR', Country: 'France', Count: 1, LastUtc: '2026-11-20T10:00:00Z' }]
        }
    });
    var visitors = opened.view.querySelector('#proxyVisitors');
    var headers = texts(visitors, '.stolon-data-label');

    assert.ok(headers.indexOf('City') >= 0);
    assert.ok(headers.indexOf('Banned') >= 0);
    assert.ok(visitors.textContent.indexOf('Paris') >= 0);
    var titles = Array.prototype.map.call(visitors.querySelectorAll('button'), function (b) { return b.disabled ? b.getAttribute('title') : null; });
    assert.ok(titles.indexOf('Your network and trusted networks are never blocked.') >= 0);
    assert.ok(titles.indexOf('Blocked by a range in the block list in Settings.') >= 0);
});

test('unblocking a country asks first and names it', async function () {
    var opened = await openProxyPage({
        'GET Proxy/Visitors': {
            Recording: true,
            Settings: { Location: 'Country' },
            Banned: [],
            Activity: [{ Type: 'Blocked', Ip: '203.0.113.8', Country: 'France', CountryCode: 'FR', CountryBlocked: true, LastUtc: '2026-11-20T10:00:00Z' }]
        }
    });
    var unblock = Array.prototype.find.call(opened.view.querySelectorAll('#proxyVisitors button'), function (b) { return b.textContent === 'Unblock'; });
    assert.equal(unblock.getAttribute('title'), 'Allow connections from France');
    unblock.click();
    await settle(50);

    assert.equal(dialogText(opened.dom).title, 'Unblock connections from France');
    assert.equal(posts(opened, 'Proxy/Visitors/UnblockCountry').length, 0);
    clickDialogButton(opened.dom, 'Unblock');
    await settle(50);
    assert.equal(JSON.parse(posts(opened, 'Proxy/Visitors/UnblockCountry')[0].data).Value, 'FR');
});

test('Request certificate asks first, then orders the route\'s certificate', async function () {
    var opened = await openProxyPage();
    opened.view.querySelectorAll('.stolon-record-list-row')[0].click();
    await settle(50);
    var request = opened.dom.document.querySelector('.stolon-sidecar [data-action="request"]');
    assert.ok(request, 'the sidecar has Request certificate');
    assert.equal(request.disabled, false);
    request.click();
    await settle(50);

    assert.equal(dialogText(opened.dom).title, 'Request a certificate for media.example.com');
    clickDialogButton(opened.dom, 'Request certificate');
    await settle(50);
    assert.equal(posts(opened, 'Proxy/Routes/a/Certificate/Request').length, 1);
});

test('the country rule note names the server country, warns while the database is missing, and sums who was refused', async function () {
    var bag = await strings();
    var sections = await loadScript('necto_proxy_sections.js');
    var t = bag.t;

    assert.equal(sections.countryNote('Off', { CountryDatabaseLoaded: false }, false, t).text, 'Lets every country connect.');
    var known = sections.countryNote('ServerCountry', { CountryDatabaseLoaded: true, ServerCountry: 'FR' }, false, t);
    assert.match(known.text, /This server is in /);
    assert.equal(known.warn, false);
    var missing = sections.countryNote('Block', { CountryDatabaseLoaded: false }, true, t);
    assert.match(missing.text, /every internet connection is refused/);
    assert.equal(missing.warn, true);
    assert.match(sections.countryNote('AllowOnly', { CountryDatabaseLoaded: false }, false, t).text, /everyone can connect/);
    assert.equal(sections.countryNote('Block', null, false, t).warn, false);
    assert.equal(sections.refusedText({}, t), 'The country rules refused no one since Jellyfin started.');
    assert.match(sections.refusedText({ CN: 2, '': 5 }, t), /^Since Jellyfin started, the country rules refused 5 from Unknown, 2 from .+\.$/);
    assert.match(sections.locationNote('City', t), /130 MB/);
});

test('a reset keeps the trusted networks, block list and country rules, and lists what goes back', async function () {
    var sections = await loadScript('necto_proxy_sections.js');
    var stored = { HttpsPort: 8443, KeepAliveSeconds: 30, TrustedNetworks: ['100.64.0.0/10'], BlockedAddresses: ['203.0.113.0/24'], CountryMode: 'Block', Countries: ['CN'], HstsEnabled: null };

    assert.deepEqual(sections.resetChanges(stored).map(function (c) { return c.key; }), ['HttpsPort', 'KeepAliveSeconds']);
    assert.deepEqual(sections.resetAdvanced(stored), { TrustedNetworks: ['100.64.0.0/10'], BlockedAddresses: ['203.0.113.0/24'], CountryMode: 'Block', Countries: ['CN'] });
    assert.deepEqual(sections.resetChanges({}), []);
});

test('turnsOnLoopback is true only when Only through the proxy goes from off to on', async function () {
    var sections = await loadScript('necto_proxy_sections.js');
    var defaults = { Values: { JellyfinLoopbackOnly: false } };

    assert.equal(sections.turnsOnLoopback({}, true, defaults), true);
    assert.equal(sections.turnsOnLoopback({ JellyfinLoopbackOnly: true }, true, defaults), false);
    assert.equal(sections.turnsOnLoopback({}, false, defaults), false);
    assert.equal(sections.turnsOnLoopback({}, true, { Values: { JellyfinLoopbackOnly: true } }), false);
});

test('nameItems shows the public name, where it points, and this server\'s address', async function () {
    var bag = await strings();
    var sections = await loadScript('necto_proxy_sections.js');
    var items = sections.nameItems({ Hostname: 'media.example.com', Addresses: ['198.51.100.1'], PublicAddress: '203.0.113.2' }, bag.t);

    assert.deepEqual(items.map(function (i) { return i.value; }), ['media.example.com', '198.51.100.1', '203.0.113.2']);
    assert.deepEqual(items.map(function (i) { return i.label; }), ['Public name', 'Points to', 'This server\'s public address']);
    assert.deepEqual(sections.nameItems({ Hostname: '' }, bag.t), []);
});

// openSettings
// Shows Settings with the Proxy section over the given stored settings.
async function openSettings(settings) {
    var page = installPage({ config: {} });
    var dom = page.dom;
    var view = pageView(dom, [dom.el('div', { id: 'settingsStatus' }), dom.el('div', { id: 'settingsSections' })]);
    dom.document.documentElement.appendChild(view);
    routeApi(page, {
        'GET Proxy/Settings': settings,
        'GET Proxy/Defaults': { Values: { HttpPort: 80, HttpsPort: 443, JellyfinLoopbackOnly: false, CountryMode: 'ServerCountry' } },
        'GET Proxy/Status': { CountryDatabaseLoaded: true, ServerCountry: 'FR', CountryRefused: { CN: 3 } },
        'GET Ddns/Settings': {}
    });
    var mod = await loadScript('necto_settings.js');
    mod.default(view);
    view.dispatchEvent(new globalThis.CustomEvent('viewshow'));
    await settle();
    return { page: page, dom: dom, view: view, section: view.querySelector('[data-tool="proxy"]') };
}

test('turning on Only through the proxy asks before it saves, and Cancel saves nothing', async function () {
    var opened = await openSettings({ Enabled: true, Advanced: {}, Visitors: { Enabled: true, HistoryDays: 90, Location: 'Country' } });
    var box = opened.section.querySelector('[data-field="JellyfinLoopbackOnly"]');
    box.checked = true;
    box.dispatchEvent(new globalThis.Event('change', { bubbles: true }));
    clickButton(opened.section, 'Save');
    await settle(50);

    assert.equal(dialogText(opened.dom).title, 'Turn on Only through the proxy');
    clickDialogButton(opened.dom, 'Cancel');
    await settle(50);
    assert.equal(posts(opened, 'Proxy/Settings').length, 0);

    clickButton(opened.section, 'Save');
    await settle(50);
    clickDialogButton(opened.dom, 'Turn on');
    await settle(50);
    assert.equal(JSON.parse(posts(opened, 'Proxy/Settings')[0].data).Advanced.JellyfinLoopbackOnly, true);
});

test('the country rule and location lookup describe the pick under their selects, and hide the countries while the rule is off', async function () {
    var opened = await openSettings({ Enabled: true, Advanced: { CountryMode: 'ServerCountry' }, Visitors: { Enabled: true, HistoryDays: 90, Location: 'City' } });
    var note = opened.section.querySelector('[data-role="country-note"]');

    assert.match(note.textContent, /This server is in /);
    assert.ok(note.closest('.selectContainer').querySelector('[data-field="CountryMode"]'));
    assert.equal(opened.section.querySelector('[data-role="country-refused"]'), null);
    assert.match(opened.section.querySelector('[data-role="location-note"]').textContent, /130 MB/);
    var mode = opened.section.querySelector('[data-field="CountryMode"]');
    mode.value = 'Off';
    mode.dispatchEvent(new globalThis.Event('change', { bubbles: true }));
    assert.equal(note.textContent, 'Lets every country connect.');
    assert.equal(opened.section.querySelector('[data-field="Countries"]').classList.contains('stolon-hidden'), true);
});

test('Reset to defaults in Troubleshooting lists what goes back and keeps the lists', async function () {
    var page = installPage({ config: {} });
    var dom = page.dom;
    var view = pageView(dom, [dom.el('div', { id: 'troubleshootingSections' })]);
    dom.document.documentElement.appendChild(view);
    routeApi(page, {
        'GET Proxy/Status': { Running: true, Reachability: {} },
        'GET Proxy/Defaults': { IsLowMemory: false, MemoryMb: 8192 },
        'GET Proxy/Settings': { Enabled: true, Advanced: { KeepAliveSeconds: 30, TrustedNetworks: ['100.64.0.0/10'], BlockedAddresses: ['203.0.113.0/24'], CountryMode: 'Block', Countries: ['CN'] } },
        'GET Ddns/Records': { Records: [] }
    });
    var mod = await loadScript('necto_troubleshooting.js');
    mod.default(view);
    view.dispatchEvent(new globalThis.CustomEvent('viewshow'));
    await settle();
    var opened = { page: page };
    view.querySelector('[data-tool="proxy"] [data-action="reset"]').click();
    await settle(50);

    var dialog = dialogText(dom);
    assert.equal(dialog.title, 'Reset the proxy settings');
    assert.match(dialog.body, /trusted networks, block list and country rules stay/);
    clickDialogButton(dom, 'Reset');
    await settle(50);
    var advanced = JSON.parse(posts(opened, 'Proxy/Settings')[0].data).Advanced;
    assert.deepEqual(advanced, { TrustedNetworks: ['100.64.0.0/10'], BlockedAddresses: ['203.0.113.0/24'], CountryMode: 'Block', Countries: ['CN'] });
});

test('every text field of the Proxy settings is sent as text, never null', async function () {
    var proxy = await loadScript('necto_proxy_sections.js');
    var inputs = {};
    var texts = [];
    proxy.advancedSpecs().forEach(function (sub) {
        sub.fields.forEach(function (spec) {
            inputs[spec.key] = { input: { value: '', checked: false } };
            if (spec.kind === 'text') texts.push(spec.key);
        });
    });

    var out = proxy.readAdvanced(inputs, { Values: {} }, null);

    assert.ok(texts.length > 0);
    texts.forEach(function (key) { assert.equal(typeof out[key], 'string', key); });
});

test('the Jellyfin section offers Trust, and Undo only once trusted, as rows with their tiles, and its badge says where trust stands', async function () {
    var opened = await openProxyPage();
    var section = opened.view.querySelector('#proxyJellyfin');

    assert.deepEqual(Array.prototype.map.call(section.querySelectorAll('.stolon-reset-row .stolon-collapsible-tile'), function (n) { return n.textContent; }), ['verified_user', 'undo']);
    assert.equal(section.querySelectorAll('.stolon-collapsible-content > div > .fieldDescription').length, 0);
    assert.equal(section.querySelector('[data-role="trust-state"]'), null);
    assert.equal(section.querySelector('[data-action="undo"]').disabled, true);
    assert.ok(section.querySelector('[data-action="undo"]').closest('.stolon-reset-row').classList.contains('stolon-hidden'));
});

test('the Proxy section of Settings folds each group of settings into its own part with a tile', async function () {
    var opened = await openSettings({ Enabled: true, Advanced: {}, Visitors: { Enabled: true, HistoryDays: 90, Location: 'Country' } });
    var parts = opened.section.querySelectorAll('[data-part]');

    assert.equal(parts.length, 8);
    var icons = Array.prototype.map.call(parts, function (p) { return p.querySelector('.stolon-collapsible-tile').textContent; });
    assert.equal(new Set(icons).size, 8);
    assert.equal(opened.section.querySelectorAll('.stolon-subsection-title').length, 0);
    assert.ok(opened.section.querySelector('[data-part="HeaderLocations"] [data-field="CountryMode"]'));
});

test('a route to Jellyfin takes a local address, a wildcard certificate names its domain, and a route under another wildcard shows it uses that', async function () {
    var bag = await strings();
    var proxy = await loadScript('necto_proxy.js');
    var route = Object.assign({}, ROUTES[0], { Host: 'kelp.ink', LocalHost: 'home.kelp.ink', Certificate: { Mode: 'Automatic', Challenge: 'Dns', Wildcard: true } });
    var editor = proxy.createRouteEditor(route, { shared: bag, status: null, records: [], hostnames: [] });
    var field = function (name) { return editor.element.querySelector('[data-field="' + name + '"]'); };
    var hidden = function (node) { return node.closest('.stolon-hidden') !== null; };

    assert.equal(field('LocalHost').value, 'home.kelp.ink');
    assert.equal(hidden(field('LocalHost')), false);
    assert.equal(field('Certificate.Wildcard').checked, true);
    assert.match(field('Certificate.Wildcard').closest('.checkboxContainer, .inputContainer, div').parentNode.textContent, /Covers kelp\.ink and every name under it\. Every route under kelp\.ink uses this certificate\./);
    var value = editor.getValue();
    assert.deepEqual([value.LocalHost, value.Certificate.Wildcard], ['home.kelp.ink', true]);
    assert.equal(proxy.upstreamText(value, bag.t), 'Jellyfin, and home.kelp.ink at home');

    field('Target').value = 'other';
    field('Target').dispatchEvent(new globalThis.Event('change'));
    assert.equal(hidden(field('LocalHost')), true);
    assert.equal(editor.getValue().LocalHost, '');

    var app = proxy.createRouteEditor(Object.assign({}, ROUTES[0], { Id: 'a', Host: 'app.kelp.ink' }), { shared: bag, status: { Id: 'a', SharedFrom: 'kelp.ink', Certificate: {}, Acme: {} }, records: [], hostnames: [] });
    var note = app.element.querySelector('[data-role="shared-certificate"]');
    assert.equal(note.textContent, 'Uses the wildcard certificate of kelp.ink.');
    assert.equal(hidden(note), false);
    assert.equal(hidden(app.element.querySelector('[data-field="Certificate.Mode"]')), true);
});
