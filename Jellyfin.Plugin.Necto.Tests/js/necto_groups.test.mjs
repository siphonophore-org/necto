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
    assert.deepEqual(texts(sidecar, '.stolon-collapsible-title'), ['Members', 'Account rules', 'Access and features', 'Preferences', 'Requests', 'Issues', 'Invites', 'WireGuard', 'Scripts', 'Custom pages', 'Notifications']);
    assert.deepEqual(texts(sidecar, '[data-section="accounts"] .stolon-subsection-title'), ['Password requirements', 'Expiry', 'Inactivity', 'Session management']);
    assert.equal(sidecar.querySelectorAll('[data-role="coming"]').length, 0);
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

test('userCounts counts administrators, active, inactive, disabled, and soon expiring users', async function () {
    installPage({});
    var mod = await loadScript('necto_groups.js');
    var now = new Date(2026, 10, 26, 12, 0, 0);

    var counts = mod.userCounts([
        { IsAdministrator: true, ExpiresOn: '2026-11-27T00:00:00' },
        { InactiveDays: 30, LastActivityDate: new Date(2026, 10, 20).toISOString() },
        { InactiveDays: 30, LastActivityDate: new Date(2026, 9, 1).toISOString(), ExpiresOn: '2026-12-03T00:00:00' },
        { IsDisabled: true, ExpiresOn: '2026-11-26T00:00:00' },
        { ExpiresOn: '2026-12-04T00:00:00' },
        { ExpiresOn: '2026-11-25T00:00:00' }
    ], now);

    assert.deepEqual(counts, { admins: 1, active: 3, inactive: 1, disabled: 1, expiring: 2 });
});

test('the page shows the user count cards over the list', async function () {
    var page = installPage({});
    var dom = page.dom;
    var view = pageView(dom, [dom.el('div', { id: 'groupUserCards' }), dom.el('div', { id: 'groupList' }), dom.el('button', { id: 'btnAddGroup' }), dom.el('span', { id: 'groupsStatus' })]);
    dom.document.documentElement.appendChild(view);
    routeApi(page, { 'GET Groups': groups(), 'GET Groups/Users': users(), 'GET Devices': { Items: [] } });
    var mod = await loadScript('necto_groups.js');
    mod.default(view);
    view.dispatchEvent(new globalThis.CustomEvent('viewshow'));
    await settle();

    var text = view.querySelector('#groupUserCards').textContent;
    assert.match(text, /1\s*Administrators/);
    assert.match(text, /2\s*Active/);
    assert.match(text, /Inactive/);
    assert.match(text, /Expiring/);
});

test('deletesOnExpiry holds only for a group with an expiry date and Delete as its method', async function () {
    installPage({});
    var mod = await loadScript('necto_groups.js');
    function group(expiry) { return { Permissions: { Accounts: { Expiry: expiry } } }; }

    assert.equal(mod.deletesOnExpiry(group({ ExpiresOn: '2026-12-01T00:00:00', Action: 'Delete' })), true);
    assert.equal(mod.deletesOnExpiry(group({ ExpiresOn: '2026-12-01T00:00:00', Action: 1 })), true);
    assert.equal(mod.deletesOnExpiry(group({ ExpiresOn: '2026-12-01T00:00:00', Action: 'Disable' })), false);
    assert.equal(mod.deletesOnExpiry(group({ ExpiresOn: null, Action: 'Delete' })), false);
    assert.equal(mod.deletesOnExpiry({ Permissions: {} }), false);
});

test('saving a group that deletes its members on expiry asks first, and Cancel sends nothing', async function () {
    var deleting = groups();
    deleting[1].Permissions.Accounts.Expiry = { ExpiresOn: '2026-12-01T00:00:00', Action: 'Delete' };
    var opened = await openPage({ 'GET Groups': deleting, ['PUT Groups/' + FAMILY]: function (o) { return JSON.parse(o.data); } });
    var sidecar = await openRow(opened, 1);

    clickButton(sidecar.querySelector('.stolon-sidecar-shelf'), 'Save');
    await settle(50);
    var dialog = dialogText(opened.dom);
    assert.equal(dialog.title, 'Save Family');
    assert.equal(dialog.body, 'Its members and their data are deleted on 2026-12-01. This cannot be undone.');
    clickDialogButton(opened.dom, 'Cancel');
    await settle(50);
    assert.equal(sent(opened).length, 0);

    clickButton(sidecar.querySelector('.stolon-sidecar-shelf'), 'Save');
    await settle(50);
    clickDialogButton(opened.dom, 'Save');
    await settle(50);
    assert.equal(sent(opened)[0].type, 'PUT');
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

test('the Requests and Issues sections hold every permission, built, with the profiles of each type to pick from', async function () {
    var opened = await openPage({ 'GET Requests/Profiles': [
        { Id: 'p1', Name: '1080p', Type: 'Movie', ServerId: 's1', Server: 'Radarr' },
        { Id: 'p2', Name: '4K', Type: 'Movie', ServerId: 's1', Server: 'Radarr' },
        { Id: 'p3', Name: 'HD', Type: 'Series', ServerId: 's2', Server: 'Sonarr' }
    ] });
    var sidecar = await openRow(opened, 1);
    var requests = sidecar.querySelector('[data-section="requests"]');
    var issues = sidecar.querySelector('[data-section="issues"]');

    assert.equal(requests.querySelector('.stolon-badge'), null);
    assert.deepEqual(Array.prototype.map.call(requests.querySelectorAll('[data-field]'), function (f) { return f.getAttribute('data-field'); }), [
        'Requests.MayRequest', 'Requests.Movies', 'Requests.Series', 'Requests.Unreleased', 'Requests.AutoApproveMovies', 'Requests.AutoApproveSeries',
        'Requests.MaxOpenRequests', 'Requests.MovieQuota', 'Requests.SeriesQuota', 'Requests.MaxSeasons', 'Requests.MovieProfiles', 'Requests.SeriesProfiles',
        'Requests.MaySeeOthers', 'Requests.MaySeeRequesters', 'Requests.MayManage'
    ]);
    assert.deepEqual(Array.prototype.map.call(issues.querySelectorAll('[data-field]'), function (f) { return f.getAttribute('data-field'); }), ['Issues.MayReport', 'Issues.Limit', 'Issues.MayManage']);
    assert.match(requests.textContent, /Allow requests/);
    assert.match(requests.textContent, /See all requests/);
    assert.match(issues.textContent, /Allow reports/);
    assert.equal(requests.querySelector('[data-field="Requests.Movies"]').checked, true);
    assert.equal(requests.querySelector('[data-field="Requests.MaxOpenRequests"]').value, '10');
    assert.equal(issues.querySelector('[data-field="Issues.Limit"]').value, '10');
    assert.deepEqual(texts(requests.querySelector('[data-field="Requests.MovieProfiles"]'), '.stolon-check-label'), ['1080p', '4K']);
    assert.deepEqual(texts(requests.querySelector('[data-field="Requests.SeriesProfiles"]'), '.stolon-check-label'), ['HD']);
});

test('Save sends the Requests and Issues permissions the editor shows, the profiles picked among them', async function () {
    var opened = await openPage({
        'GET Requests/Profiles': [{ Id: 'p1', Name: '1080p', Type: 'Movie', ServerId: 's1', Server: 'Radarr' }, { Id: 'p2', Name: '4K', Type: 'Movie', ServerId: 's1', Server: 'Radarr' }],
        ['PUT Groups/' + FAMILY]: function (o) { return JSON.parse(o.data); }
    });
    var sidecar = await openRow(opened, 1);
    sidecar.querySelector('[data-field="Requests.Series"]').checked = false;
    sidecar.querySelector('[data-field="Requests.AutoApproveMovies"]').checked = true;
    sidecar.querySelector('[data-field="Requests.MaxSeasons"]').value = '2';
    sidecar.querySelector('[data-field="Requests.MaySeeRequesters"]').checked = true;
    sidecar.querySelector('[data-field="Issues.Limit"]').value = '-1';
    var box = sidecar.querySelector('[data-field="Requests.MovieProfiles"] .stolon-check-row-input[data-id="p2"]');
    box.checked = true;
    box.dispatch('change', { bubbles: true });

    clickButton(sidecar.querySelector('.stolon-sidecar-shelf'), 'Save');
    await settle(50);

    var body = JSON.parse(sent(opened)[0].data);
    assert.equal(body.Permissions.Requests.MayRequest, true);
    assert.equal(body.Permissions.Requests.Movies, true);
    assert.equal(body.Permissions.Requests.Series, false);
    assert.equal(body.Permissions.Requests.AutoApproveMovies, true);
    assert.equal(body.Permissions.Requests.MaxSeasons, 2);
    assert.equal(body.Permissions.Requests.MaySeeRequesters, true);
    assert.deepEqual(body.Permissions.Requests.MovieProfiles, ['p2']);
    assert.deepEqual(body.Permissions.Requests.SeriesProfiles, []);
    assert.equal(body.Permissions.Issues.Limit, 10);
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

test('a new group starts with the updates about members own requests and issues, and nothing else', async function () {
    installPage({});
    var mod = await loadScript('necto_groups.js');
    var group = mod.newGroup();

    assert.equal(group.Name, '');
    assert.deepEqual(group.MemberIds, []);
    assert.deepEqual(group.Permissions.Notifications.EventKeys, ['requests.approved', 'requests.declined', 'requests.available', 'issues.updated']);
    assert.equal(group.Permissions.Notifications.AllEvents, false);
});
