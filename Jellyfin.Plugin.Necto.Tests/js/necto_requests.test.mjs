// necto_requests.test.mjs
// Tests for the Requests tool: the counts, the filters kept for the next visit, the words a request is described
// with, the actions each status offers, the bulk actions, the detail sidecar, and the notice that the list moved.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { installPage, loadScript, pageView, settle, texts, clickButton, clickDialogButton, dialogText, statusText } from './support.mjs';

globalThis.setTimeout = function () { return 1; };
globalThis.clearTimeout = function () {};
globalThis.setInterval = function () { return 1; };
globalThis.clearInterval = function () {};

var VIEWER = 'aaaaaaaa-0000-0000-0000-000000000001';

// strings
// Builds the helper bag's strings for a test that calls the module's helpers directly.
async function strings() {
    var page = installPage({});
    var shared = await loadScript('necto_shared.js');
    var bag = shared.createNectoShared(page.dom.el('div', {}));
    return bag.translations;
}

// requestRow
// A row of Requests/All, a Jellyfin item with its request under Request.
function requestRow(request) {
    return { RequestId: request.Id, Request: request };
}

// dune
// A pending movie request.
function dune(extra) {
    return Object.assign({ Id: 'r1', Type: 'Movie', Title: 'Dune', Year: 2021, Status: 'Pending', UserName: 'viewer', UserId: VIEWER, CreatedUtc: '2026-01-02T10:00:00Z', ProfileId: 'p1', ServerId: 's1' }, extra || {});
}

// openPage
// Shows the Requests tool over a view with its markup, answering each route by its path whatever its query.
async function openPage(answers, storage) {
    var page = installPage({ storage: storage });
    var dom = page.dom;
    var view = pageView(dom, [
        dom.el('div', { id: 'requestStats' }),
        dom.el('div', { id: 'requestsChanged', class: 'stolon-hidden' }),
        dom.el('div', { id: 'requestsStatus' }),
        dom.el('div', { id: 'requestsTable' })
    ]);
    dom.document.documentElement.appendChild(view);
    var routes = Object.assign({
        'GET Requests/Stats': { Total: 1, ByStatus: { Pending: 1 } },
        'GET Requests/Requesters': { Items: [{ UserId: VIEWER, UserName: 'viewer', Count: 1 }] },
        'GET Requests/Profiles': [{ Id: 'p1', Name: '1080p', Type: 'Movie', ServerId: 's1', Server: 'Radarr' }, { Id: 'p2', Name: '4K', Type: 'Movie', ServerId: 's1', Server: 'Radarr' }],
        'GET Requests/All': { Items: [requestRow(dune())], TotalCount: 1 }
    }, answers || {});
    globalThis.ApiClient.fetch = function (options) {
        page.requests.push(options);
        var answer = routes[(options.type || 'GET') + ' ' + String(options.url).split('?')[0]];
        if (typeof answer === 'function') answer = answer(options);
        if (answer instanceof Error) return Promise.reject(answer);
        return Promise.resolve(answer === undefined ? {} : answer);
    };
    globalThis.Dashboard.navigate = function (url) { page.navigated = url; };
    var mod = await loadScript('necto_requests.js');
    var handle = mod.default(view);
    view.dispatchEvent(new globalThis.CustomEvent('viewshow'));
    await settle(50);
    return { page: page, view: view, dom: dom, handle: handle, routes: routes };
}

// cellHtml
// Reads the markup every cell of the list holds.
function cellHtml(view) {
    return Array.prototype.map.call(view.querySelectorAll('.stolon-table-cell'), function (c) { return c.innerHTML; }).join(' ');
}

// pick
// Picks the first row of the list, as ticking its checkbox does.
async function pick(opened) {
    var box = opened.view.querySelector('.stolon-filtered-check');
    box.checked = true;
    box.dispatch('change', { bubbles: true });
    await settle(50);
}

// sent
// Lists the requests that change something.
function sent(opened) {
    return opened.page.requests.filter(function (r) { return r.type === 'POST' || r.type === 'DELETE'; });
}

test('statCards counts each status in its color', async function () {
    var t = await strings();
    var mod = await loadScript('necto_requests.js');

    var cards = mod.statCards({ ByStatus: { Pending: 2, Approved: 1, Failed: 1, PartiallyAvailable: 1, Available: 5, Declined: 3 } }, t);

    assert.deepEqual(cards.map(function (c) { return c.label; }), ['Pending', 'Approved', 'Failed', 'Partial', 'Available', 'Rejected']);
    assert.deepEqual(cards.map(function (c) { return c.count; }), [2, 1, 1, 1, 5, 3]);
    assert.deepEqual(cards.map(function (c) { return c.color; }), ['purple', 'blue', 'red', 'orange', 'green', 'gray']);
    assert.deepEqual(mod.statCards(null, t).map(function (c) { return c.count; }), [0, 0, 0, 0, 0, 0]);
});

test('the filters are kept for the next visit and fall back to Open and everyone', async function () {
    var mod = await loadScript('necto_requests.js');
    var store = {};
    var storage = { getItem: function (k) { return store[k] || null; }, setItem: function (k, v) { store[k] = v; } };

    assert.deepEqual(mod.readFilters(storage), { status: 'Open', requester: '' });
    mod.keepFilters(storage, { status: 'Failed', requester: VIEWER });
    assert.deepEqual(mod.readFilters(storage), { status: 'Failed', requester: VIEWER });
    store[mod.FILTERS_KEY] = '{not json';
    assert.deepEqual(mod.readFilters(storage), { status: 'Open', requester: '' });
    assert.deepEqual(mod.readFilters(null), { status: 'Open', requester: '' });
    assert.doesNotThrow(function () { mod.keepFilters({ setItem: function () { throw new Error('full'); } }, { status: 'All' }); });
});

test('the requester filter lists each requester with a count, and a requester with none left falls back to everyone', async function () {
    var mod = await loadScript('necto_requests.js');
    var list = [{ UserId: VIEWER, UserName: 'viewer', Count: 3 }];

    assert.deepEqual(mod.requesterOptions(list), [{ value: VIEWER, label: 'viewer (3)' }]);
    assert.equal(mod.keptRequester(VIEWER, list), VIEWER);
    assert.equal(mod.keptRequester('gone', list), '');
    assert.equal(mod.keptRequester('', list), '');
});

test('seasons and specials read as words', async function () {
    var t = await strings();
    var mod = await loadScript('necto_requests.js');

    assert.equal(mod.describeSeasons([5, 1, 2, 3], t), 'Seasons 1-3, 5');
    assert.notEqual(mod.describeSeasons([2], t), mod.describeSeasons([2, 3], t));
    assert.equal(mod.describeSeasons(null, t), 'All seasons');
    assert.equal(mod.describeWanted({ Specials: [{ Name: 'Pilot' }, { Number: 4 }] }, t), 'Specials: Pilot, Special 4');
    assert.equal(mod.subline({ Type: 'Series', Year: 2020, Seasons: [1, 2] }, t), 'Show · 2020 · Seasons 1-2');
    assert.equal(mod.subline({ Type: 'Movie', Year: 2021 }, t), 'Movie · 2021');
});

test('a request goes to its own profile, else its server\'s first, and profiles are named for their server when a type has two', async function () {
    var mod = await loadScript('necto_requests.js');
    var profiles = [
        { Id: 'a', Name: '1080p', Type: 'Movie', ServerId: 's1', Server: 'Radarr' },
        { Id: 'b', Name: '4K', Type: 'Movie', ServerId: 's2', Server: 'Radarr 4K' },
        { Id: 'c', Name: 'HD', Type: 'Series', ServerId: 's3', Server: 'Sonarr' }
    ];

    assert.equal(mod.placedProfile(profiles, { Type: 'Movie', ProfileId: 'b', ServerId: 's2' }).Id, 'b');
    assert.equal(mod.placedProfile(profiles, { Type: 'Movie', ProfileId: 'gone', ServerId: 's2' }).Id, 'b');
    assert.equal(mod.placedProfile(profiles, { Type: 'Movie' }).Id, 'a');
    assert.equal(mod.placedProfile([], { Type: 'Movie' }), null);
    assert.equal(mod.profileLabel(profiles, profiles[1]), 'Radarr 4K · 4K');
    assert.equal(mod.profileLabel(profiles, profiles[2]), 'HD');
    assert.equal(mod.profileLabel(profiles, null), null);
});

test('each status offers its own actions in shelf order', async function () {
    var mod = await loadScript('necto_requests.js');

    assert.deepEqual(mod.requestActions({ Status: 'Pending' }), ['remove', 'available', 'reject', 'edit', 'approve']);
    assert.deepEqual(mod.requestActions({ Status: 'Failed' }), ['remove', 'available', 'reject', 'retry']);
    assert.deepEqual(mod.requestActions({ Status: 'Approved', LibraryItemId: 'x' }), ['remove', 'available', 'reject']);
    assert.deepEqual(mod.requestActions({ Status: 'Available', LibraryItemId: 'x' }), ['remove']);
    assert.deepEqual(mod.requestActions({ Status: 'Declined' }), ['remove']);
});

test('a bulk result says what was done, what was not sent, and what was skipped', async function () {
    var t = await strings();
    var mod = await loadScript('necto_requests.js');

    assert.deepEqual(mod.bulkResultText('approve', { Done: 2, Failed: 1, Skipped: 1 }, t), { text: '2 approved. 1 could not be sent to Radarr or Sonarr. 1 skipped, as already decided.', isError: true });
    assert.deepEqual(mod.bulkResultText('remove', { Done: 3, Skipped: 1 }, t), { text: '3 removed. 1 skipped, as already removed.', isError: false });
    assert.equal(mod.bulkResultText('decline', { Done: 1 }, t).text, '1 rejected.');
});

test('a download shows its progress and the time it has left', async function () {
    var t = await strings();
    var mod = await loadScript('necto_requests.js');
    var now = Date.parse('2026-01-01T10:00:00Z');

    assert.equal(mod.progressLine({ Status: 'Approved', ProgressText: '40%', DownloadDoneUtc: '2026-01-01T10:05:00Z' }, t, now), '40% · 5 min left');
    assert.equal(mod.progressLine({ Status: 'Approved', ProgressText: '40%', DownloadDoneUtc: '2026-01-01T11:30:00Z' }, t, now), '40% · 1 hr 30 min left');
    assert.equal(mod.progressLine({ Status: 'Pending', ProgressText: '40%' }, t, now), '');
    assert.equal(mod.artwork('https://image.tmdb.org/t/p/w500/abc.jpg'), '../requests/img/w500/abc.jpg');
    assert.equal(mod.artwork('https://example.com/a.jpg'), 'https://example.com/a.jpg');
});

test('the page draws the counts and reads the list on the kept filters', async function () {
    var opened = await openPage({}, { 'necto-requests-filters': JSON.stringify({ status: 'Failed', requester: VIEWER }) });

    assert.deepEqual(texts(opened.view.querySelector('#requestStats'), '.stolon-card-label'), ['Pending', 'Approved', 'Failed', 'Partial', 'Available', 'Rejected']);
    var list = opened.page.requests.find(function (r) { return String(r.url).indexOf('Requests/All?') === 0; });
    assert.ok(list);
    assert.match(list.url, /filter=Failed/);
    assert.match(list.url, new RegExp('user=' + VIEWER));
    assert.match(cellHtml(opened.view), /Dune/);
});

test('a requester with no requests left falls back to everyone before the list loads', async function () {
    var opened = await openPage({}, { 'necto-requests-filters': JSON.stringify({ status: 'Open', requester: 'gone' }) });

    var list = opened.page.requests.find(function (r) { return String(r.url).indexOf('Requests/All?') === 0; });
    assert.doesNotMatch(list.url, /user=/);
    assert.equal(JSON.parse(opened.page.store['necto-requests-filters']).requester, '');
});

test('a request opens in a sidecar with its actions, and Approve sends it with the profile picked in Goes to', async function () {
    var opened = await openPage({ 'POST Requests/r1/Approve': { Request: dune({ Status: 'Approved' }) } });

    opened.view.querySelector('.stolon-table-row').click();
    await settle(50);
    var sidecar = opened.dom.document.querySelector('.stolon-sidecar');
    assert.equal(sidecar.querySelector('.stolon-sidecar-title').textContent, 'Dune');
    assert.deepEqual(texts(sidecar.querySelector('.stolon-sidecar-shelf'), 'button'), ['Remove', 'Mark available', 'Reject', 'Edit', 'Approve']);
    var picker = sidecar.querySelector('select');
    assert.deepEqual(texts(picker, 'option'), ['1080p', '4K']);
    picker.value = 'p2';

    clickButton(sidecar.querySelector('.stolon-sidecar-shelf'), 'Approve');
    await settle(50);

    var post = sent(opened)[0];
    assert.equal(post.url, 'Requests/r1/Approve?profileId=p2');
    assert.equal(statusText(opened.view.querySelector('#requestsStatus')), 'Approved.');
});

test('the sidecar leads with the requester and their notes, then everyone who joined, each with their picture', async function () {
    var opened = await openPage();
    opened.handle.showRequest(dune({
        Status: 'Declined', RequesterNote: 'For movie night', DecidedBy: 'admin', DecidedUtc: '2026-01-03T10:00:00Z', Note: 'Already on the shelf',
        Joined: [{ UserId: 'u3', UserName: 'sam', JoinedUtc: '2026-01-04T10:00:00Z' }, { UserId: 'u2', UserName: 'riley', JoinedUtc: '2026-01-03T09:00:00Z' }, { UserId: '', UserName: '', JoinedUtc: '2026-01-05T10:00:00Z' }]
    }));
    await settle(50);
    var sidecar = opened.dom.document.querySelector('.stolon-sidecar');
    var requester = sidecar.querySelector('[data-role="requester"]');
    var joined = sidecar.querySelector('[data-role="joined"]');

    assert.equal(requester.querySelector('.stolon-user-chip-name').textContent, 'viewer');
    assert.match(requester.querySelector('.stolon-user-chip-text').textContent, /^Requested /);
    assert.deepEqual(Array.prototype.map.call(requester.querySelectorAll('.stolon-timeline-title'), function (n) { return n.textContent; }), ['viewer', 'admin rejected the request']);
    assert.match(requester.textContent, /For movie night[\s\S]*Already on the shelf/);
    assert.deepEqual(Array.prototype.map.call(joined.querySelectorAll('.stolon-user-chip-name'), function (n) { return n.textContent; }), ['riley', 'sam']);
    assert.doesNotMatch(sidecar.querySelector('.stolon-definition-list, dl') ? sidecar.querySelector('.stolon-definition-list, dl').textContent : '', /Requester|Also wanted by/);
    assert.equal(sidecar.querySelectorAll('.stolon-collapsible-tile').length, new Set(Array.prototype.map.call(sidecar.querySelectorAll('.stolon-collapsible-tile'), function (n) { return n.textContent; })).size);
});

test('a request nobody joined has no joined section, and one with no note shows only the requester', async function () {
    var opened = await openPage();
    opened.handle.showRequest(dune());
    await settle(50);
    var sidecar = opened.dom.document.querySelector('.stolon-sidecar');

    assert.equal(sidecar.querySelector('[data-role="joined"]'), null);
    assert.equal(sidecar.querySelector('[data-role="requester"] .stolon-timeline'), null);
    assert.ok(sidecar.querySelector('[data-role="requester"] .stolon-user-chip'));
});

test('Remove asks first with the title, and Cancel sends nothing', async function () {
    var opened = await openPage();
    opened.handle.showRequest(dune());
    await settle(50);

    clickButton(opened.dom.document.querySelector('.stolon-sidecar-shelf'), 'Remove');
    await settle(50);
    var dialog = dialogText(opened.dom);
    assert.equal(dialog.title, 'Remove Dune');
    assert.equal(dialog.body, 'viewer no longer sees the request.');
    clickDialogButton(opened.dom, 'Cancel');
    await settle(50);
    assert.equal(sent(opened).length, 0);

    clickButton(opened.dom.document.querySelector('.stolon-sidecar-shelf'), 'Remove');
    await settle(50);
    clickDialogButton(opened.dom, 'Remove');
    await settle(50);
    assert.equal(sent(opened)[0].type, 'DELETE');
    assert.equal(sent(opened)[0].url, 'Requests/r1');
});

test('a sent request links to its page in Radarr and to the title in the library, which leaves the shelf', async function () {
    var opened = await openPage({ 'GET Requests/r1/ArrLink': { Url: 'https://radarr.example.com/movie/1', Server: 'Radarr' } });
    opened.handle.showRequest(dune({ Status: 'Approved', ArrId: 7, LibraryItemId: 'lib1' }));
    await settle(50);

    var sidecar = opened.dom.document.querySelector('.stolon-sidecar');
    assert.match(sidecar.textContent, /Open in Radarr/);
    assert.deepEqual(texts(sidecar.querySelector('.stolon-sidecar-shelf'), 'button'), ['Remove', 'Mark available', 'Reject']);
    var library = Array.prototype.filter.call(sidecar.querySelectorAll('.stolon-sidecar-body a'), function (a) { return a.textContent === 'Open in library'; })[0];
    library.click();
    assert.equal(opened.page.navigated, 'details?id=lib1');
});

test('bulk approve posts the rows picked, and bulk remove asks first', async function () {
    var opened = await openPage({ 'POST Requests/Bulk/approve': { Done: 1 } });
    await pick(opened);
    var approve = opened.view.querySelector('[data-bulk="approve"]');
    assert.equal(approve.disabled, false);

    approve.click();
    await settle(50);
    var post = sent(opened)[0];
    assert.equal(post.url, 'Requests/Bulk/approve');
    assert.deepEqual(JSON.parse(post.data).Ids, ['r1']);
    assert.equal(statusText(opened.view.querySelector('#requestsStatus')), '1 approved.');
});

test('bulk remove asks first with how many it removes', async function () {
    var opened = await openPage();
    await pick(opened);

    opened.view.querySelector('[data-bulk="remove"]').click();
    await settle(50);
    var dialog = dialogText(opened.dom);
    assert.equal(dialog.title, 'Remove 1 request');
    clickDialogButton(opened.dom, 'Cancel');
    await settle(50);
    assert.equal(sent(opened).length, 0);
});

test('the notice shows when the counts move while rows are picked, and Show latest loads the list', async function () {
    var opened = await openPage();
    await pick(opened);
    opened.routes['GET Requests/Stats'] = { Total: 1, ByStatus: { Approved: 1 } };

    await opened.handle.poll();
    await settle(50);

    var notice = opened.view.querySelector('#requestsChanged');
    assert.equal(notice.classList.contains('stolon-hidden'), false);
    assert.match(notice.textContent, /Requests have changed since this list loaded\./);
    var before = opened.page.requests.filter(function (r) { return String(r.url).indexOf('Requests/All?') === 0; }).length;
    clickButton(notice, 'Show latest');
    await settle(50);
    assert.equal(notice.classList.contains('stolon-hidden'), true);
    assert.ok(opened.page.requests.filter(function (r) { return String(r.url).indexOf('Requests/All?') === 0; }).length > before);
});

test('Reject asks for a reason in a sidecar and sends it with the reject', async function () {
    var opened = await openPage({ 'POST Requests/r1/Decline': { Request: dune({ Status: 'Declined' }) } });
    opened.handle.showRequest(dune());
    await settle(50);

    clickButton(opened.dom.document.querySelector('.stolon-sidecar-shelf'), 'Reject');
    await settle(50);
    var sidecars = opened.dom.document.querySelectorAll('.stolon-sidecar');
    var prompt = sidecars[sidecars.length - 1];
    assert.equal(prompt.querySelector('.stolon-sidecar-title').textContent, 'Reject Dune');
    prompt.querySelector('textarea').value = 'Not on any service.';
    clickButton(prompt.querySelector('.stolon-sidecar-shelf'), 'Reject');
    await settle(50);

    var post = sent(opened)[0];
    assert.equal(post.url, 'Requests/r1/Decline');
    assert.deepEqual(JSON.parse(post.data), { Reason: 'Not on any service.' });
    assert.equal(statusText(opened.view.querySelector('#requestsStatus')), 'Rejected.');
});

test('Edit saves the profile and the note of a waiting request', async function () {
    var opened = await openPage({ 'POST Requests/r1': {} });
    opened.handle.showRequest(dune({ RequesterNote: 'Please' }));
    await settle(50);

    clickButton(opened.dom.document.querySelector('.stolon-sidecar-shelf'), 'Edit');
    await settle(50);
    var editor = opened.dom.document.querySelector('.stolon-sidecar');
    assert.equal(editor.querySelector('.stolon-sidecar-title').textContent, 'Edit Dune');
    assert.equal(editor.querySelector('textarea').value, 'Please');
    editor.querySelector('select').value = 'p2';
    editor.querySelector('textarea').value = 'In 4K';
    clickButton(editor.querySelector('.stolon-sidecar-shelf'), 'Save');
    await settle(50);

    var post = sent(opened)[0];
    assert.equal(post.url, 'Requests/r1');
    assert.deepEqual(JSON.parse(post.data), { Seasons: null, ProfileId: 'p2', Note: 'In 4K' });
});

test('a server refusal keeps the sidecar open and shows a reason there', async function () {
    var opened = await openPage({ 'POST Requests/r1/Approve': new Error('refused') });
    opened.handle.showRequest(dune());
    await settle(50);

    clickButton(opened.dom.document.querySelector('.stolon-sidecar-shelf'), 'Approve');
    await settle(50);

    var sidecar = opened.dom.document.querySelector('.stolon-sidecar');
    assert.ok(sidecar);
    assert.match(sidecar.querySelector('[data-role="status"]').textContent, /Could not update the request\./);
});

test('each row names who asked and when under its title, and the bulk bar takes full size buttons', async function () {
    var opened = await openPage();

    assert.match(cellHtml(opened.view), /viewer · /);
    assert.equal(opened.view.querySelectorAll('.stolon-table-row .stolon-table-cell').length >= 2, true);
    assert.ok(Array.prototype.every.call(opened.view.querySelectorAll('[data-bulk]'), function (b) { return !/stolon-small/.test(b.className); }));
});

test('with no requests at all the table frame says No requests yet, whatever the kept filter, and keeps its toolbar', async function () {
    var opened = await openPage({ 'GET Requests/Stats': { Total: 0, ByStatus: {} }, 'GET Requests/All': { Items: [], TotalCount: 0 } }, { 'necto-requests-filters': JSON.stringify({ status: 'Failed' }) });
    await settle(50);

    var table = opened.view.querySelector('#requestsTable');
    assert.equal(table.classList.contains('stolon-hidden'), false);
    assert.match(table.textContent, /No requests yet\./);
    assert.doesNotMatch(table.textContent, /No requests match/);
    assert.ok(table.querySelector('.stolon-table-controls input'));
});

test('a filter that finds no request says No requests match, and the list with All says No requests yet', async function () {
    var filtered = await openPage({ 'GET Requests/All': { Items: [], TotalCount: 0 } }, { 'necto-requests-filters': JSON.stringify({ status: 'Failed' }) });
    assert.match(filtered.view.querySelector('#requestsTable').textContent, /No requests match\./);
    var all = await openPage({ 'GET Requests/All': { Items: [], TotalCount: 0 } }, { 'necto-requests-filters': JSON.stringify({ status: 'All' }) });
    assert.match(all.view.querySelector('#requestsTable').textContent, /No requests yet\./);
});

test('the sidecar shows the status once, in its header, and a movie with one profile edits its note alone', async function () {
    var opened = await openPage({ 'GET Requests/Profiles': [{ Id: 'p1', Name: '1080p', Type: 'Movie', ServerId: 's1', Server: 'Radarr' }] });
    opened.handle.showRequest(dune());
    await settle(50);

    var sidecar = opened.dom.document.querySelector('.stolon-sidecar');
    assert.equal((sidecar.textContent.match(/Pending/g) || []).length, 1);
    assert.deepEqual(texts(sidecar.querySelector('.stolon-sidecar-shelf'), 'button'), ['Remove', 'Mark available', 'Reject', 'Edit note', 'Approve']);
});

test('userImage asks for a picture only for a user who has one, and usersImageTags reads them from the users', async function () {
    installPage({});
    var mod = await loadScript('necto_requests.js');
    globalThis.ApiClient.getUrl = function (path, query) { return path + '?tag=' + query.tag; };
    globalThis.ApiClient.getUsers = function () { return Promise.resolve([{ Id: 'AAAA-1', PrimaryImageTag: 'p1' }, { Id: 'bbbb2' }]); };

    var tags = await mod.usersImageTags();

    assert.deepEqual(tags, { aaaa1: 'p1' });
    assert.equal(mod.userImage('AAAA-1', tags), 'Users/AAAA-1/Images/Primary?tag=p1');
    assert.equal(mod.userImage('bbbb2', tags), '');
    assert.equal(mod.userImage('bbbb2', null), '');
});
