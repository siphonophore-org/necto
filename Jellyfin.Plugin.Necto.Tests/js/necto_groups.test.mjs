// necto_groups.test.mjs
// Tests for the Groups tool: the list with Everyone first, the editor sidecar and its sections, saving, and the
// confirm before a delete.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { installPage, loadScript, pageView, settle, routeApi, texts, clickButton, clickDialogButton, dialogText } from './support.mjs';

globalThis.setTimeout = function () { return 1; };
globalThis.clearTimeout = function () {};

var EVERYONE = '5a1f0c7e-0000-4000-8000-000000000001';
var FAMILY = '11111111-2222-3333-4444-555555555555';
var VIEWER = 'aaaaaaaa-0000-0000-0000-000000000001';

// groups
// The groups the Groups route answers with.
function groups() {
    return [
        { Id: EVERYONE, Name: 'Everyone', IsEveryone: true, MemberIds: [], Permissions: { Accounts: { Password: { Enabled: false } } } },
        { Id: FAMILY, Name: 'Family', MemberIds: [VIEWER], Permissions: { Accounts: { Password: { Enabled: true, MinLength: 10, ChangeMode: 1 }, Inactivity: { Enabled: true, Days: 45 }, Expiry: { ExpiresOn: null, Action: 'Disable' }, Sessions: { Enabled: false, Rules: [] } }, Requests: { MayRequest: true, MovieQuota: 3 } } }
    ];
}

// users
// The users the Groups/Users route answers with: one member, one in no group, and an administrator.
function users() {
    return [
        { Id: VIEWER, Name: 'viewer', IsAdministrator: false, GroupIds: [FAMILY] },
        { Id: 'aaaaaaaa-0000-0000-0000-000000000002', Name: 'guest', IsAdministrator: false, GroupIds: [EVERYONE] },
        { Id: 'aaaaaaaa-0000-0000-0000-000000000003', Name: 'admin', IsAdministrator: true, GroupIds: [] }
    ];
}

// openPage
// Shows the Groups tool over a view with its list, its Add group button, and its status line.
async function openPage(answers) {
    var page = installPage({});
    var dom = page.dom;
    var view = pageView(dom, [dom.el('div', { id: 'groupList' }), dom.el('button', { id: 'btnAddGroup' }), dom.el('span', { id: 'groupsStatus' })]);
    dom.document.documentElement.appendChild(view);
    routeApi(page, Object.assign({ 'GET Groups': groups(), 'GET Groups/Users': users(), 'GET Devices': { Items: [{ AppName: 'Infuse' }] } }, answers || {}));
    var mod = await loadScript('necto_groups.js');
    mod.default(view);
    view.dispatchEvent(new globalThis.CustomEvent('viewshow'));
    await settle();
    return { page: page, view: view, dom: dom };
}

// openRow
// Opens the group at a row of the list, and returns its sidecar.
async function openRow(opened, index) {
    opened.view.querySelectorAll('.stolon-record-list-row')[index].click();
    await settle(50);
    return opened.dom.document.querySelector('.stolon-sidecar');
}

// sent
// Lists the requests that change something.
function sent(opened) {
    return opened.page.requests.filter(function (r) { return r.type === 'POST' || r.type === 'PUT' || r.type === 'DELETE'; });
}

test('groupItems puts Everyone first with the users in no group, and each group with its members and rules', async function () {
    installPage({});
    var shared = await loadScript('necto_shared.js');
    var bag = shared.createNectoShared(installPage({}).dom.el('div', {}));
    await bag.translations;
    var mod = await loadScript('necto_groups.js');

    var items = mod.groupItems(groups().reverse(), users(), bag);

    assert.deepEqual(items.map(function (i) { return i.title; }), ['Everyone', 'Family']);
    assert.equal(items[0].secondary, '1 user in no other group');
    assert.equal(items[1].secondary, '1 member');
    assert.deepEqual(items[1].tags.map(function (t) { return t.label; }), ['Password requirements', 'Inactivity']);
});

test('the page lists Everyone and every group from the Groups route', async function () {
    var opened = await openPage();
    var list = opened.view.querySelector('.stolon-record-list');
    assert.ok(list);
    assert.match(list.textContent, /Everyone/);
    assert.match(list.textContent, /Family/);
    assert.equal(opened.view.querySelectorAll('.stolon-record-list-row').length, 2);
});

test('a group opens in a sidecar named for it, with its sections, and Delete, Cancel, and Save on the shelf', async function () {
    var opened = await openPage();
    var sidecar = await openRow(opened, 1);

    assert.equal(sidecar.querySelector('.stolon-sidecar-title').textContent, 'Family');
    assert.deepEqual(texts(sidecar.querySelector('.stolon-sidecar-shelf'), 'button'), ['Delete', 'Cancel', 'Save']);
    assert.deepEqual(texts(sidecar, '.stolon-collapsible-title'), ['Members', 'Account rules', 'Requests', 'Issues', 'Invites', 'WireGuard', 'Scripts', 'Custom pages', 'Notifications']);
    assert.deepEqual(texts(sidecar, '[data-section="accounts"] .stolon-subsection-title'), ['Password requirements', 'Expiry', 'Inactivity', 'Session management']);
    assert.equal(sidecar.querySelectorAll('[data-role="coming"]').length, 4);
    assert.equal(sidecar.querySelector('[data-section="accounts"] [data-role="coming"]'), null);
    assert.equal(sidecar.querySelector('[data-field="Name"]').value, 'Family');
});

test('Everyone opens with no name field, no members to pick, and no Delete', async function () {
    var opened = await openPage();
    var sidecar = await openRow(opened, 0);

    assert.equal(sidecar.querySelector('.stolon-sidecar-title').textContent, 'Everyone');
    assert.equal(sidecar.querySelector('[data-field="Name"]'), null);
    assert.deepEqual(texts(sidecar.querySelector('.stolon-sidecar-shelf'), 'button'), ['Cancel', 'Save']);
    assert.match(sidecar.querySelector('[data-section="members"]').textContent, /every user who is in no other group/);
});

test('Save puts the edited group to its own route with the fields the editor shows', async function () {
    var opened = await openPage({ ['PUT Groups/' + FAMILY]: function (o) { return JSON.parse(o.data); } });
    var sidecar = await openRow(opened, 1);
    sidecar.querySelector('[data-field="Name"]').value = 'Friends';
    sidecar.querySelector('[data-field="Accounts.Password.MinLength"]').value = '12';
    sidecar.querySelector('[data-field="Issues.MayReport"]').checked = true;

    assert.equal(clickButton(sidecar.querySelector('.stolon-sidecar-shelf'), 'Save'), true);
    await settle(50);

    var put = sent(opened)[0];
    assert.equal(put.type, 'PUT');
    assert.equal(put.url, 'Groups/' + FAMILY);
    var body = JSON.parse(put.data);
    assert.equal(body.Name, 'Friends');
    assert.deepEqual(body.MemberIds, [VIEWER]);
    assert.equal(body.Permissions.Accounts.Password.MinLength, 12);
    assert.equal(body.Permissions.Accounts.Password.ChangeMode, 'InitialOnly');
    assert.equal(body.Permissions.Accounts.Inactivity.Days, 45);
    assert.equal(body.Permissions.Accounts.Expiry.ExpiresOn, null);
    assert.equal(body.Permissions.Issues.MayReport, true);
    assert.equal(body.Permissions.Requests.MovieQuota, 3);
});

test('Add group posts a new group, and a blank name keeps the sidecar open with a message', async function () {
    var opened = await openPage();
    opened.view.querySelector('#btnAddGroup').click();
    await settle(50);
    var sidecar = opened.dom.document.querySelector('.stolon-sidecar');
    assert.equal(sidecar.querySelector('.stolon-sidecar-title').textContent, 'New group');
    assert.deepEqual(texts(sidecar.querySelector('.stolon-sidecar-shelf'), 'button'), ['Cancel', 'Save']);

    clickButton(sidecar.querySelector('.stolon-sidecar-shelf'), 'Save');
    await settle(50);
    assert.equal(sent(opened).length, 0);
    assert.match(sidecar.querySelector('[data-role="status"]').textContent, /Enter the group name\./);

    sidecar.querySelector('[data-field="Name"]').value = 'Kids';
    clickButton(sidecar.querySelector('.stolon-sidecar-shelf'), 'Save');
    await settle(50);
    var post = sent(opened)[0];
    assert.equal(post.type, 'POST');
    assert.equal(post.url, 'Groups');
    assert.equal(JSON.parse(post.data).Name, 'Kids');
});

test('an expiry turned on with no date is refused before anything is sent', async function () {
    var opened = await openPage();
    var sidecar = await openRow(opened, 1);
    sidecar.querySelector('[data-field="Accounts.Expiry.ExpiresOn"][type="checkbox"]').checked = true;

    clickButton(sidecar.querySelector('.stolon-sidecar-shelf'), 'Save');
    await settle(50);

    assert.equal(sent(opened).length, 0);
    assert.match(sidecar.querySelector('[data-role="status"]').textContent, /Select an expiry date/);
});

test('Delete asks first with a title naming the group and says what follows, and Cancel sends nothing', async function () {
    var opened = await openPage();
    var sidecar = await openRow(opened, 1);
    clickButton(sidecar.querySelector('.stolon-sidecar-shelf'), 'Delete');
    await settle(50);

    var dialog = dialogText(opened.dom);
    assert.equal(dialog.title, 'Delete Family');
    assert.equal(dialog.body, 'Its members keep their other groups. A member in no other group takes Everyone.');
    assert.doesNotMatch(dialog.body, /\?/);
    clickDialogButton(opened.dom, 'Cancel');
    await settle(50);
    assert.equal(sent(opened).length, 0);
});

test('Delete confirmed sends the delete to the group\'s route and reports it', async function () {
    var opened = await openPage();
    var sidecar = await openRow(opened, 1);
    clickButton(sidecar.querySelector('.stolon-sidecar-shelf'), 'Delete');
    await settle(50);
    clickDialogButton(opened.dom, 'Delete');
    await settle(50);

    var del = sent(opened)[0];
    assert.equal(del.type, 'DELETE');
    assert.equal(del.url, 'Groups/' + FAMILY);
    assert.match(opened.view.querySelector('#groupsStatus').textContent, /Group deleted\./);
});

test('overlappingClients names a client more than one session rule covers', async function () {
    installPage({});
    var mod = await loadScript('necto_groups.js');
    var clients = mod.knownClients([{ AppName: 'Infuse' }, { AppName: 'infuse' }], [{ Clients: ['Jellyfin Web'] }]);

    assert.deepEqual(clients, ['Infuse', 'Jellyfin Web']);
    assert.deepEqual(mod.overlappingClients(clients, [{ Mode: 'All', Clients: [] }, { Mode: 'Only', Clients: ['Infuse'] }]), ['Infuse']);
    assert.deepEqual(mod.overlappingClients(clients, [{ Mode: 'Only', Clients: ['Infuse'] }, { Mode: 'AllExcept', Clients: ['Infuse'] }]), []);
    assert.equal(mod.enumName(2, ['Allowed', 'InitialOnly', 'Disallowed']), 'Disallowed');
    assert.equal(mod.sameId(EVERYONE.toUpperCase().replace(/-/g, ''), EVERYONE), true);
});
