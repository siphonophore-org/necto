// necto_issues.test.mjs
// Tests for the Issues tool: the counts, the filters kept for the next visit, the words an issue is described with,
// the decisions each state offers, the issue sidecar, and the feature off state.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { installPage, loadScript, pageView, settle, texts, clickButton, statusText } from './support.mjs';

globalThis.setTimeout = function () { return 1; };
globalThis.clearTimeout = function () {};

// strings
// Builds the helper bag's strings for a test that calls the module's helpers directly.
async function strings() {
    var page = installPage({});
    var shared = await loadScript('necto_shared.js');
    var bag = shared.createNectoShared(page.dom.el('div', {}));
    return bag.translations;
}

// issue
// A reported issue with an episode.
function issue(extra) {
    return Object.assign({
        Id: 'i1', ItemId: 'item1', SeriesId: 'show1', ItemType: 'Episode', Title: 'The Long Night', SeriesName: 'Tidewater Station', SeasonNumber: 1, EpisodeNumber: 2,
        State: 'Reported', Reporters: [{ UserName: 'viewer', Type: 'Audio' }, { UserName: 'guest', Type: 'Audio' }, { UserName: 'kid', Type: 'Subtitles' }],
        CreatedUtc: '2026-01-02T10:00:00Z', UpdatedUtc: '2026-01-03T10:00:00Z', Path: '/media/show/s01e02.mkv',
        Notes: [{ User: 'viewer', Text: 'Sound drops out.', Time: '2026-01-02T10:00:00Z' }]
    }, extra || {});
}

// openPage
// Shows the Issues tool over a view with its markup, answering each route by its path whatever its query.
async function openPage(config, answers, storage) {
    var page = installPage({ config: config, storage: storage });
    var dom = page.dom;
    var view = pageView(dom, [
        dom.el('div', { id: 'issuesOff', class: 'stolon-hidden' }),
        dom.el('div', { id: 'issueStats' }),
        dom.el('div', { id: 'issuesStatus' }),
        dom.el('div', { id: 'issuesTable' })
    ]);
    dom.document.documentElement.appendChild(view);
    var routes = Object.assign({
        'GET Issues/Stats': { Reported: 2, Confirmed: 1, Resolved: 4, Declined: 1, Rescinded: 0 },
        'GET Issues/All': { Items: [issue()], TotalCount: 1 }
    }, answers || {});
    globalThis.ApiClient.fetch = function (options) {
        page.requests.push(options);
        var answer = routes[(options.type || 'GET') + ' ' + String(options.url).split('?')[0]];
        if (typeof answer === 'function') answer = answer(options);
        if (answer instanceof Error) return Promise.reject(answer);
        return Promise.resolve(answer === undefined ? {} : answer);
    };
    globalThis.Dashboard.navigate = function (url) { page.navigated = url; };
    var mod = await loadScript('necto_issues.js');
    var handle = mod.default(view);
    view.dispatchEvent(new globalThis.CustomEvent('viewshow'));
    await settle();
    return { page: page, view: view, dom: dom, handle: handle };
}

// sent
// Lists the requests that change something.
function sent(opened) {
    return opened.page.requests.filter(function (r) { return r.type === 'POST'; });
}

test('statCards counts Open as the reported and confirmed issues, then each state in its color, Reported in yellow as on the user page', async function () {
    var t = await strings();
    var mod = await loadScript('necto_issues.js');

    var cards = mod.statCards({ Reported: 2, Confirmed: 1, Resolved: 4, Declined: 1 }, t);

    assert.deepEqual(cards.map(function (c) { return c.label; }), ['Open', 'Reported', 'Confirmed', 'Resolved', 'Declined']);
    assert.deepEqual(cards.map(function (c) { return c.count; }), [3, 2, 1, 4, 1]);
    assert.deepEqual(cards.map(function (c) { return c.color; }), ['', 'yellow', 'red', 'green', 'gray']);
});

test('the state filter offers Open and every state, and the type filter every type', async function () {
    var t = await strings();
    var mod = await loadScript('necto_issues.js');

    assert.deepEqual(mod.stateFilters(t).map(function (o) { return o.label; }), ['Open', 'Reported', 'Confirmed', 'Resolved', 'Declined', 'Rescinded']);
    assert.deepEqual(mod.issueTypes(t).map(function (o) { return o.label; }), ['Video', 'Audio', 'Subtitles', 'Wrong item or metadata', 'Does not play', 'Other']);
    assert.equal(mod.typeName('WontPlay', t), 'Does not play');
    assert.equal(mod.typeName('Unknown', t), 'Unknown');
});

test('the filters are kept for the next visit and fall back to Open and every type', async function () {
    var mod = await loadScript('necto_issues.js');
    var store = {};
    var storage = { getItem: function (k) { return store[k] || null; }, setItem: function (k, v) { store[k] = v; } };

    assert.deepEqual(mod.readFilters(storage), { state: 'Open', type: '' });
    mod.keepFilters(storage, { state: 'All', type: 'Audio' });
    assert.deepEqual(mod.readFilters(storage), { state: 'All', type: 'Audio' });
    store[mod.FILTERS_KEY] = 'oops';
    assert.deepEqual(mod.readFilters(storage), { state: 'Open', type: '' });
});

test('an issue is named as the server names it, with what the reporters say is wrong', async function () {
    var t = await strings();
    var mod = await loadScript('necto_issues.js');

    assert.equal(mod.titleOf(issue(), t), 'Tidewater Station · S1E2');
    assert.equal(mod.titleOf({ ItemType: 'Movie', Title: 'Dune', Year: 2021 }, t), 'Dune (2021)');
    assert.equal(mod.titleOf({ ItemType: 'Movie', Title: 'Dune (2021)', Year: 2021 }, t), 'Dune (2021)');
    assert.equal(mod.subline(issue(), t), 'The Long Night · Audio, Subtitles');
    assert.deepEqual(mod.noteItems(issue().Notes).map(function (n) { return [n.title, n.body]; }), [['viewer', 'Sound drops out.']]);
});

test('an open issue offers Decline, Confirm, and Resolve, and a closed one Reopen', async function () {
    var mod = await loadScript('necto_issues.js');

    assert.deepEqual(mod.issueActions({ State: 'Reported' }), ['Decline', 'Confirm', 'Resolve']);
    assert.deepEqual(mod.issueActions({ State: 'Confirmed' }), ['Decline', 'Resolve']);
    assert.deepEqual(mod.issueActions({ State: 'Resolved' }), ['Reopen']);
    assert.deepEqual(mod.issueActions({ State: 'Rescinded' }), ['Reopen']);
});

test('while issues are off the page says so and reads no issues', async function () {
    var opened = await openPage({ Issues: { Enabled: false } });

    var off = opened.view.querySelector('#issuesOff');
    assert.equal(off.classList.contains('stolon-hidden'), false);
    assert.match(off.innerHTML, /Issues is off\. Turn it on in Settings to take reports\./);
    assert.equal(opened.view.querySelector('#issuesTable').classList.contains('stolon-hidden'), true);
    assert.equal(opened.page.requests.filter(function (r) { return String(r.url).indexOf('Issues') === 0; }).length, 0);
});

test('while issues are on the page draws the counts and reads the list on the kept filters', async function () {
    var opened = await openPage({ Issues: { Enabled: true } }, {}, { 'necto-issues-filters': JSON.stringify({ state: 'Confirmed', type: 'Audio' }) });

    assert.equal(opened.view.querySelector('#issuesOff').classList.contains('stolon-hidden'), true);
    assert.deepEqual(texts(opened.view.querySelector('#issueStats'), '.stolon-card-count'), ['3', '2', '1', '4', '1']);
    var list = opened.page.requests.find(function (r) { return String(r.url).indexOf('Issues/All?') === 0; });
    assert.match(list.url, /filter=Confirmed/);
    assert.match(list.url, /type=Audio/);
});

test('an issue opens in a sidecar with its notes, its decisions, and its Sonarr link', async function () {
    var opened = await openPage({ Issues: { Enabled: true } }, { 'GET Issues/i1/ArrLink': { Url: 'https://sonarr.example.com/series/tidewater', Server: 'Sonarr' } });

    opened.view.querySelector('.stolon-table-row').click();
    await settle(50);
    var sidecar = opened.dom.document.querySelector('.stolon-sidecar');
    assert.equal(sidecar.querySelector('.stolon-sidecar-title').textContent, 'Tidewater Station · S1E2');
    assert.deepEqual(texts(sidecar.querySelector('.stolon-sidecar-shelf'), 'button'), ['Decline', 'Open in library', 'Confirm', 'Resolve']);
    assert.match(sidecar.textContent, /Sound drops out\./);
    assert.match(sidecar.textContent, /Open in Sonarr/);

    clickButton(sidecar.querySelector('.stolon-sidecar-shelf'), 'Open in library');
    assert.equal(opened.page.navigated, 'details?id=item1');
});

test('a file changed since the report is flagged in the sidecar', async function () {
    var opened = await openPage({ Issues: { Enabled: true } });
    opened.handle.showIssue(issue({ FileChanged: true }));
    await settle(50);

    var sidecar = opened.dom.document.querySelector('.stolon-sidecar');
    assert.match(sidecar.querySelector('.stolon-warn').textContent, /File changed since the report/);
});

test('Resolve asks for a note and posts it to the issue\'s Resolve route', async function () {
    var opened = await openPage({ Issues: { Enabled: true } }, { 'POST Issues/i1/Resolve': issue({ State: 'Resolved' }) });
    opened.handle.showIssue(issue());
    await settle(50);

    clickButton(opened.dom.document.querySelector('.stolon-sidecar-shelf'), 'Resolve');
    await settle(50);
    var sidecars = opened.dom.document.querySelectorAll('.stolon-sidecar');
    var prompt = sidecars[sidecars.length - 1];
    assert.equal(prompt.querySelector('.stolon-sidecar-title').textContent, 'Resolve Tidewater Station · S1E2');
    prompt.querySelector('textarea').value = 'Replaced the file.';
    clickButton(prompt.querySelector('.stolon-sidecar-shelf'), 'Resolve');
    await settle(50);

    var post = sent(opened)[0];
    assert.equal(post.url, 'Issues/i1/Resolve');
    assert.deepEqual(JSON.parse(post.data), { Note: 'Replaced the file.' });
    assert.equal(statusText(opened.view.querySelector('#issuesStatus')), 'Resolved.');
});

test('a closed issue opens with Reopen only', async function () {
    var opened = await openPage({ Issues: { Enabled: true } });
    opened.handle.showIssue(issue({ State: 'Declined', DecidedBy: 'admin' }));
    await settle(50);

    var sidecar = opened.dom.document.querySelector('.stolon-sidecar');
    assert.deepEqual(texts(sidecar.querySelector('.stolon-sidecar-shelf'), 'button'), ['Open in library', 'Reopen']);
    assert.match(sidecar.textContent, /Decided by/);
});
