// necto_groups.test.mjs
// Tests for the Groups tool: the list with Everyone first, the editor sidecar and its sections, saving, and the
// confirm before a group is removed.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { installPage, loadScript, pageView, settle, routeApi, texts, clickButton, clickDialogButton, dialogText, allToolsOn } from './support.mjs';

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
    var page = installPage({ config: allToolsOn() });
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
    installPage({ config: allToolsOn() });
    var shared = await loadScript('necto_shared.js');
    var bag = shared.createNectoShared(installPage({ config: allToolsOn() }).dom.el('div', {}));
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

test('a group opens in a sidecar named for it, with its sections, and Remove, Cancel, and Save on the shelf', async function () {
    var opened = await openPage();
    var sidecar = await openRow(opened, 1);

    assert.equal(sidecar.querySelector('.stolon-sidecar-title').textContent, 'Family');
    assert.deepEqual(texts(sidecar.querySelector('.stolon-sidecar-shelf'), 'button'), ['Remove', 'Cancel', 'Save']);
    assert.equal((sidecar.textContent.match(/never holds administrators/g) || []).length, 1);
    assert.match(sidecar.querySelector('[data-section="members"]').textContent, /never holds administrators/);
    assert.deepEqual(texts(sidecar, '.stolon-collapsible-title'), ['Members', 'Account rules', 'Access and features', 'Preferences', 'Requests', 'Issues', 'Invites', 'VPN', 'Scripts', 'Pages', 'Notifications']);
    assert.deepEqual(texts(sidecar, '[data-section="accounts"] .stolon-subsection-title'), ['Password requirements', 'Expiry', 'Inactivity', 'Session management']);
    assert.equal(sidecar.querySelectorAll('[data-role="coming"]').length, 0);
    assert.equal(sidecar.querySelector('[data-section="accounts"] [data-role="coming"]'), null);
    assert.equal(sidecar.querySelector('[data-field="Name"]').value, 'Family');
});

test('Everyone opens with no name field, no members to pick, no Remove, and no word on several groups', async function () {
    var opened = await openPage();
    var sidecar = await openRow(opened, 0);

    assert.equal(sidecar.querySelector('.stolon-sidecar-title').textContent, 'Everyone');
    assert.equal(sidecar.querySelector('[data-field="Name"]'), null);
    assert.deepEqual(texts(sidecar.querySelector('.stolon-sidecar-shelf'), 'button'), ['Cancel', 'Save']);
    assert.match(sidecar.querySelector('[data-section="members"]').textContent, /every user who is in no other group/);
    assert.doesNotMatch(sidecar.textContent, /never holds administrators/);
});

// tile
// Reads the color class of a section's icon tile.
function tile(sidecar, key) {
    var el = sidecar.querySelector('[data-section="' + key + '"] .stolon-collapsible-tile');
    return ['green', 'gray', 'red', 'blue'].filter(function (c) { return el.classList.contains('stolon-' + c); })[0] || 'accent';
}

test('each tool section tile is green while the group gives members the tool and gray while it gives nothing', async function () {
    var opened = await openPage();
    var sidecar = await openRow(opened, 1);

    assert.equal(tile(sidecar, 'members'), 'accent');
    assert.equal(tile(sidecar, 'accounts'), 'accent');
    assert.equal(tile(sidecar, 'requests'), 'green');
    assert.equal(tile(sidecar, 'issues'), 'gray');
    assert.equal(tile(sidecar, 'scripts'), 'gray');
    var report = sidecar.querySelector('[data-field="Issues.MayReport"]');
    report.checked = true;
    report.dispatchEvent(new globalThis.Event('change', { bubbles: true }));
    assert.equal(tile(sidecar, 'issues'), 'green');
    var request = sidecar.querySelector('[data-field="Requests.MayRequest"]');
    request.checked = false;
    request.dispatchEvent(new globalThis.Event('change', { bubbles: true }));
    assert.equal(tile(sidecar, 'requests'), 'gray');
});

// flip
// Sets a checkbox as a click would, with the change event that bubbles from it.
function flip(box, on) {
    box.checked = on;
    box.dispatchEvent(new globalThis.Event('change', { bubbles: true }));
}

test('the Notifications tile follows the events each list holds, and clearing them saves none', async function () {
    var list = groups();
    list[1].Permissions.Notifications = { AllEvents: false, EventKeys: ['requests.approved', 'issues.updated'] };
    var events = [{ Key: 'requests.approved', Name: 'Request approved', Tool: 'requests' }, { Key: 'issues.updated', Name: 'Issue resolved', Tool: 'issues' }];
    var opened = await openPage({ 'GET Groups': list, 'GET Notifications/Events': events, ['PUT Groups/' + FAMILY]: function (o) { return JSON.parse(o.data); } });
    var sidecar = await openRow(opened, 1);
    var section = sidecar.querySelector('[data-section="notifications"]');

    assert.equal(tile(sidecar, 'notifications'), 'green');
    ['requests.approved', 'issues.updated'].forEach(function (key) {
        var picked = section.querySelector('.stolon-filtered-check[data-id="' + key + '"]');
        assert.equal(picked.checked, true);
        flip(picked, false);
    });
    assert.equal(tile(sidecar, 'notifications'), 'gray');
    var every = section.querySelector('[data-field="Notifications.AllEvents"]');
    flip(every, true);
    assert.equal(tile(sidecar, 'notifications'), 'green');
    flip(every, false);
    assert.equal(tile(sidecar, 'notifications'), 'gray');
    clickButton(sidecar.querySelector('.stolon-sidecar-shelf'), 'Save');
    await settle(50);
    assert.deepEqual(JSON.parse(sent(opened)[0].data).Permissions.Notifications.EventKeys, []);
});

test('the Jellyfin settings tiles follow their switch, and Preferences hides while the group sets nothing', async function () {
    var opened = await openPage();
    var sidecar = await openRow(opened, 1);
    var prefs = sidecar.querySelector('[data-section="template-preferences"]');

    assert.equal(tile(sidecar, 'template-access'), 'gray');
    assert.ok(prefs.classList.contains('stolon-hidden'));
    var on = sidecar.querySelector('[data-field="Template.Enabled"]');
    on.checked = true;
    on.dispatchEvent(new globalThis.Event('change', { bubbles: true }));
    assert.equal(tile(sidecar, 'template-access'), 'green');
    assert.equal(prefs.classList.contains('stolon-hidden'), false);
    assert.equal(tile(sidecar, 'template-preferences'), 'green');
});

test('grantsAny holds while a switch is on or a list holds an entry, and eventGroups sorts events by tool without the tools that are off', async function () {
    installPage({ config: allToolsOn() });
    var mod = await loadScript('necto_groups.js');

    assert.equal(mod.grantsAny({ Scripts: { AllScripts: false, ScriptIds: [] } }, ['Scripts.AllScripts', 'Scripts.ScriptIds']), false);
    assert.equal(mod.grantsAny({ Scripts: { AllScripts: false, ScriptIds: ['a'] } }, ['Scripts.AllScripts', 'Scripts.ScriptIds']), true);
    assert.equal(mod.grantsAny({}, ['Invites.MayCreate']), false);
    var events = [
        { Key: 'storage.disk_missing', Name: 'Disk missing', Tool: 'storage' },
        { Key: 'requests.created', Name: 'New request', Tool: 'requests' },
        { Key: 'proxy.banned', Name: 'Address banned', Tool: 'proxy' }
    ];
    var all = mod.eventGroups(events, null);
    assert.deepEqual(all.map(function (g) { return g.name; }), ['Requests', 'Proxy', 'Storage']);
    assert.deepEqual(mod.eventGroups(events, { Requests: { Enabled: true } }).map(function (g) { return g.key; }), ['requests']);
});

test('the Notifications section lists events under their tool by name only, and hides them while every event goes', async function () {
    var opened = await openPage({ 'GET Notifications/Events': [
        { Key: 'requests.created', Name: 'New request', Tool: 'requests' },
        { Key: 'storage.disk_missing', Name: 'Disk missing', Tool: 'storage' }
    ] });
    var sidecar = await openRow(opened, 1);
    var section = sidecar.querySelector('[data-section="notifications"]');
    var events = section.querySelector('[data-field="Notifications.EventKeys"]');

    assert.deepEqual(texts(events, '.stolon-subsection-title'), ['Requests', 'Storage']);
    assert.doesNotMatch(events.textContent, /requests\.created|storage\.disk_missing/);
    var every = section.querySelector('[data-field="Notifications.AllEvents"]');
    every.checked = true;
    every.dispatchEvent(new globalThis.Event('change', { bubbles: true }));
    assert.ok(events.classList.contains('stolon-hidden'));
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

test('groupCounts counts the groups, the users in one, the users only Everyone holds, and those expiring soon', async function () {
    installPage({ config: allToolsOn() });
    var mod = await loadScript('necto_groups.js');
    var now = new Date(2026, 10, 26, 12, 0, 0);

    var counts = mod.groupCounts(groups(), [
        { IsAdministrator: true, GroupIds: [], ExpiresOn: '2026-11-27T00:00:00' },
        { GroupIds: [FAMILY], ExpiresOn: '2026-12-03T00:00:00' },
        { GroupIds: [EVERYONE], ExpiresOn: '2026-12-04T00:00:00' },
        { GroupIds: [EVERYONE.replace(/-/g, '')] }
    ], now);

    assert.deepEqual(counts, { groups: 1, members: 1, loose: 2, expiring: 1 });
});

test('the page shows the group count cards over the list', async function () {
    var page = installPage({ config: allToolsOn() });
    var dom = page.dom;
    var view = pageView(dom, [dom.el('div', { id: 'groupCards' }), dom.el('div', { id: 'groupList' }), dom.el('button', { id: 'btnAddGroup' }), dom.el('span', { id: 'groupsStatus' })]);
    dom.document.documentElement.appendChild(view);
    routeApi(page, { 'GET Groups': groups(), 'GET Groups/Users': users(), 'GET Devices': { Items: [] } });
    var mod = await loadScript('necto_groups.js');
    mod.default(view);
    view.dispatchEvent(new globalThis.CustomEvent('viewshow'));
    await settle();

    var text = view.querySelector('#groupCards').textContent;
    assert.match(text, /1\s*Groups/);
    assert.match(text, /1\s*Members/);
    assert.match(text, /1\s*No group/);
    assert.match(text, /0\s*Expiring/);
});

test('deletesOnExpiry holds only for a group with an expiry date and Delete as its method', async function () {
    installPage({ config: allToolsOn() });
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

test('Remove asks first with a title naming the group and says what follows, and Cancel sends nothing', async function () {
    var opened = await openPage();
    var sidecar = await openRow(opened, 1);
    clickButton(sidecar.querySelector('.stolon-sidecar-shelf'), 'Remove');
    await settle(50);

    var dialog = dialogText(opened.dom);
    assert.equal(dialog.title, 'Remove Family');
    assert.equal(dialog.body, 'Its members keep their accounts and their other groups. A member in no other group takes Everyone.');
    assert.doesNotMatch(dialog.body, /\?/);
    clickDialogButton(opened.dom, 'Cancel');
    await settle(50);
    assert.equal(sent(opened).length, 0);
});

test('Remove confirmed sends the delete to the group\'s route and reports it', async function () {
    var opened = await openPage();
    var sidecar = await openRow(opened, 1);
    clickButton(sidecar.querySelector('.stolon-sidecar-shelf'), 'Remove');
    await settle(50);
    clickDialogButton(opened.dom, 'Remove');
    await settle(50);

    var del = sent(opened)[0];
    assert.equal(del.type, 'DELETE');
    assert.equal(del.url, 'Groups/' + FAMILY);
    assert.match(opened.view.querySelector('#groupsStatus').textContent, /Group removed\./);
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
    installPage({ config: allToolsOn() });
    var mod = await loadScript('necto_groups.js');
    var clients = mod.knownClients([{ AppName: 'Infuse' }, { AppName: 'infuse' }], [{ Clients: ['Jellyfin Web'] }]);

    assert.deepEqual(clients, ['Infuse', 'Jellyfin Web']);
    assert.deepEqual(mod.overlappingClients(clients, [{ Mode: 'All', Clients: [] }, { Mode: 'Only', Clients: ['Infuse'] }]), ['Infuse']);
    assert.deepEqual(mod.overlappingClients(clients, [{ Mode: 'Only', Clients: ['Infuse'] }, { Mode: 'AllExcept', Clients: ['Infuse'] }]), []);
    assert.equal(mod.enumName(2, ['Allowed', 'InitialOnly', 'Disallowed']), 'Disallowed');
    assert.equal(mod.sameId(EVERYONE.toUpperCase().replace(/-/g, ''), EVERYONE), true);
});

test('a new group starts with the updates about members own requests and issues, and nothing else', async function () {
    installPage({ config: allToolsOn() });
    var mod = await loadScript('necto_groups.js');
    var group = mod.newGroup();

    assert.equal(group.Name, '');
    assert.deepEqual(group.MemberIds, []);
    assert.deepEqual(group.Permissions.Notifications.EventKeys, ['requests.approved', 'requests.declined', 'requests.available', 'issues.updated']);
    assert.equal(group.Permissions.Notifications.AllEvents, false);
});
