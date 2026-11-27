// necto_invites.test.mjs
// Tests for Invites: the list with each invite's state, the editor with its link and fields, the requests it sends,
// the Settings section, and the user pages' link and signup helpers.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { installPage, loadScript, pageView, settle, routeApi, texts, clickButton, clickDialogButton } from './support.mjs';

globalThis.setTimeout = function () { return 1; };
globalThis.clearTimeout = function () {};
globalThis.setInterval = function () { return 1; };
globalThis.clearInterval = function () {};

var INVITE = '99999999-0000-0000-0000-000000000001';
var FAMILY = '11111111-2222-3333-4444-555555555555';
var EVERYONE = '5a1f0c7e-0000-4000-8000-000000000001';

// invites
// The invites Invites/List answers with.
function invites() {
    return [
        { Id: INVITE, Token: 'abc123', Label: 'Family', Message: 'Welcome', Resources: [{ Title: 'Guide', Url: 'https://example.com/guide' }], HasPin: true, GroupId: FAMILY, GroupName: 'Family', ExpiresAt: null, MaxUses: 5, UsedCount: 2, Enabled: true, State: 0, CreatedBy: '00000000-0000-0000-0000-000000000000', CreatedByName: '' },
        { Id: '99999999-0000-0000-0000-000000000002', Token: 'def', Label: '', GroupName: 'Everyone', MaxUses: 1, UsedCount: 1, Enabled: true, State: 'UsedUp', CreatedByName: 'viewer' }
    ];
}

// openPage
// Shows the Invites tool with its list and its Add invite button.
async function openPage(answers) {
    var page = installPage({ config: { InviteSettings: { LinkUrl: 'https://media.example.com' } } });
    var dom = page.dom;
    var view = pageView(dom, [dom.el('div', { id: 'inviteList' }), dom.el('button', { id: 'btnAddInvite' }), dom.el('span', { id: 'invitesStatus' })]);
    dom.document.documentElement.appendChild(view);
    routeApi(page, Object.assign({
        'GET Invites/List': invites(),
        'GET Groups': [{ Id: EVERYONE, Name: 'Everyone', IsEveryone: true }, { Id: FAMILY, Name: 'Family', Permissions: {} }, { Id: 'blocked', Name: 'Managed', Permissions: { Accounts: { Password: { Enabled: true, ChangeMode: 'Disallowed' } } } }]
    }, answers || {}));
    var mod = await loadScript('necto_invites.js');
    mod.default(view);
    view.dispatchEvent(new globalThis.CustomEvent('viewshow'));
    await settle(50);
    return { page: page, view: view, dom: dom };
}

test('the list shows each invite with its group, uses, state, PIN, and maker', async function () {
    var opened = await openPage();
    var rows = opened.view.querySelectorAll('.stolon-record-list-row');

    assert.equal(rows.length, 2);
    assert.match(rows[0].textContent, /Family[\s\S]*2 of 5 uses[\s\S]*Never expires[\s\S]*Active[\s\S]*PIN/);
    assert.match(rows[1].textContent, /Untitled invite[\s\S]*Used up[\s\S]*viewer/);
});

test('each invite row holds its link in a copy field with a QR button', async function () {
    var opened = await openPage();
    var rows = opened.view.querySelectorAll('.stolon-record-list-row');

    assert.match(rows[0].textContent + rows[0].innerHTML, /https:\/\/media\.example\.com\/invites\/abc123/);
    assert.match(rows[1].textContent + rows[1].innerHTML, /https:\/\/media\.example\.com\/invites\/def/);
});

test('an invite opens with its link, its fields, and Turn off, Duplicate, Delete, Cancel, and Save', async function () {
    var opened = await openPage();
    opened.view.querySelectorAll('.stolon-record-list-row')[0].click();
    await settle(50);
    var sidecar = opened.dom.document.querySelector('.stolon-sidecar');

    assert.match(sidecar.querySelector('[data-role="link"]').textContent + sidecar.querySelector('[data-role="link"]').innerHTML, /https:\/\/media\.example\.com\/invites\/abc123/);
    assert.equal(sidecar.querySelector('[data-field="Label"]').value, 'Family');
    assert.equal(sidecar.querySelector('[data-field="GroupId"]').value, FAMILY);
    assert.deepEqual(texts(sidecar.querySelector('[data-field="GroupId"]'), 'option'), ['Group for new users', 'Everyone', 'Family']);
    assert.deepEqual(texts(sidecar.querySelector('.stolon-sidecar-shelf'), 'button'), ['Delete', 'Turn off', 'Duplicate', 'Cancel', 'Save']);
});

test('Save keeps a stored PIN, and Turn off posts to the invite', async function () {
    var opened = await openPage({ ['PUT Invites/' + INVITE]: {}, ['POST Invites/' + INVITE + '/Enabled']: {} });
    opened.view.querySelectorAll('.stolon-record-list-row')[0].click();
    await settle(50);
    var sidecar = opened.dom.document.querySelector('.stolon-sidecar');
    sidecar.querySelector('[data-field="MaxUses"]').value = '10';

    clickButton(sidecar.querySelector('.stolon-sidecar-shelf'), 'Save');
    await settle(50);
    var put = opened.page.requests.find(function (r) { return r.type === 'PUT'; });
    var body = JSON.parse(put.data);
    assert.equal(body.MaxUses, 10);
    assert.equal(body.Pin, null);
    assert.deepEqual(body.Resources, [{ Title: 'Guide', Url: 'https://example.com/guide' }]);

    opened.view.querySelectorAll('.stolon-record-list-row')[0].click();
    await settle(50);
    clickButton(opened.dom.document.querySelector('.stolon-sidecar .stolon-sidecar-shelf'), 'Turn off');
    await settle(50);
    var off = opened.page.requests.find(function (r) { return r.type === 'POST' && r.url === 'Invites/' + INVITE + '/Enabled'; });
    assert.deepEqual(JSON.parse(off.data), { Enabled: false });
});

test('Add invite posts a new invite, and a short PIN keeps the sidecar open', async function () {
    var opened = await openPage({ 'POST Invites': {} });
    opened.view.querySelector('#btnAddInvite').click();
    await settle(50);
    var sidecar = opened.dom.document.querySelector('.stolon-sidecar');
    sidecar.querySelector('[data-field="Pin"]').value = '12';

    clickButton(sidecar.querySelector('.stolon-sidecar-shelf'), 'Save');
    await settle(50);
    assert.ok(!opened.page.requests.some(function (r) { return r.type === 'POST' && r.url === 'Invites'; }));
    assert.match(sidecar.querySelector('[data-role="status"]').textContent, /six digits/);

    sidecar.querySelector('[data-field="Pin"]').value = '123456';
    clickButton(sidecar.querySelector('.stolon-sidecar-shelf'), 'Save');
    await settle(50);
    var post = opened.page.requests.find(function (r) { return r.type === 'POST' && r.url === 'Invites'; });
    assert.equal(JSON.parse(post.data).Pin, '123456');
});

test('Delete asks first, then deletes the invite', async function () {
    var opened = await openPage({ ['DELETE Invites/' + INVITE]: {} });
    opened.view.querySelectorAll('.stolon-record-list-row')[0].click();
    await settle(50);
    clickButton(opened.dom.document.querySelector('.stolon-sidecar .stolon-sidecar-shelf'), 'Delete');
    await settle(50);
    clickDialogButton(opened.dom, 'Delete');
    await settle(50);

    assert.ok(opened.page.requests.some(function (r) { return r.type === 'DELETE' && r.url === 'Invites/' + INVITE; }));
});

test('inviteRequest and inviteLink build what the server and the invitee get', async function () {
    installPage({});
    var mod = await loadScript('necto_invites.js');

    assert.equal(mod.inviteLink('a b', '', 'https://media.example.com/'), 'https://media.example.com/invites/a%20b');
    assert.equal(mod.inviteLink('t', 'https://proxy.example.com/', 'http://10.0.0.2:8096'), 'https://proxy.example.com/invites/t');
    assert.deepEqual(mod.inviteRequest({ label: ' Family ', group: '', uses: '-3', expiry: '2026-12-31', pin: '', pinKept: true, resources: [{ title: '', url: '' }] }),
        { Label: 'Family', Message: '', GroupId: null, ExpiresAt: '2026-12-31T00:00:00', MaxUses: 0, Pin: null, Resources: [] });
    assert.equal(mod.stateName(4), 'UsedUp');
});

test('the Invites section of Settings saves the link address and the limits into the config', async function () {
    var page = installPage({ config: { InviteSettings: { LinkUrl: '', RateLimitCount: 10, RateLimitWindowMinutes: 60, MaxPinAttempts: 5 } } });
    var dom = page.dom;
    var view = pageView(dom, [dom.el('div', { id: 'settingsStatus' }), dom.el('div', { id: 'settingsSections' })]);
    dom.document.documentElement.appendChild(view);
    var saved = [];
    globalThis.ApiClient.updatePluginConfiguration = function (id, config) { saved.push(JSON.parse(JSON.stringify(config))); return Promise.resolve(); };
    routeApi(page, {});
    var mod = await loadScript('necto_settings.js');
    mod.default(view);
    view.dispatchEvent(new globalThis.CustomEvent('viewshow'));
    await settle(80);
    var section = view.querySelector('[data-tool="invites"]');
    section.querySelector('[data-field="LinkUrl"]').value = 'https://media.example.com/';
    section.querySelector('[data-field="MaxPinAttempts"]').value = '0';

    clickButton(section, 'Save');
    await settle(80);

    assert.equal(saved[0].InviteSettings.LinkUrl, 'https://media.example.com');
    assert.equal(saved[0].InviteSettings.MaxPinAttempts, 1);
    assert.equal(saved[0].InviteSettings.RateLimitCount, 10);
});

test('the signup page reads the token from its address and keeps only web links', async function () {
    installPage({});
    var signup = await loadScript('necto_invite_signup.js');
    var member = await loadScript('necto_invites_member.js');

    assert.equal(signup.tokenOf('/jellyfin/invites/abc123'), 'abc123');
    assert.equal(signup.tokenOf('/invites/'), '');
    assert.deepEqual(signup.safeLinks([{ Url: 'https://example.com' }, { Url: 'javascript:alert(1)' }, null]), [{ Url: 'https://example.com' }]);
    assert.equal(member.inviteLink('t', '', 'https://media.example.com/'), 'https://media.example.com/invites/t');
    assert.deepEqual(member.memberRequest({ label: 'x', group: FAMILY, uses: '2', expiry: '' }), { Label: 'x', GroupId: FAMILY, MaxUses: 2, ExpiresAt: null, Resources: [] });
});

test('the counts, the expired group warning, and the http warning follow the invites and the settings', async function () {
    installPage({});
    var mod = await loadScript('necto_invites.js');
    var sections = await loadScript('necto_invites_sections.js');
    var t = { tr: function (k) { return k; } };

    var cards = mod.stateCards(invites(), t);
    assert.deepEqual(cards.map(function (c) { return c.count; }), [1, 0, 0, 0, 1]);
    assert.equal(cards[0].color, 'green');
    var now = new Date('2026-11-26T12:00:00Z');
    assert.equal(mod.groupExpired({ Permissions: { Accounts: { Expiry: { ExpiresOn: '2026-11-26T00:00:00' } } } }, now), true);
    assert.equal(mod.groupExpired({ Permissions: { Accounts: { Expiry: { ExpiresOn: '2026-12-01T00:00:00' } } } }, now), false);
    assert.equal(mod.groupExpired(null, now), false);
    assert.equal(sections.linksInsecure('http://media.example.com', 'https:'), true);
    assert.equal(sections.linksInsecure('', 'http:'), true);
    assert.equal(sections.linksInsecure('https://media.example.com', 'http:'), false);
});
