// necto_table.test.mjs
// Tests for the one table every tool builds with createToolTable: its toolbar order, its rows, its filters, paging the
// rows a page holds, and the bulk results runBulk words.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { installPage, loadScript, pageView, settle, rowHtml, pickRows, texts, clickDialogButton, dialogText } from './support.mjs';

globalThis.setTimeout = function () { return 1; };
globalThis.clearTimeout = function () {};

// shared
// Loads the shared module and a helper bag with the plugin's strings.
async function shared() {
    var page = installPage({});
    var mod = await loadScript('necto_shared.js');
    var view = pageView(page.dom, [page.dom.el('div', { id: 'status' }), page.dom.el('div', { id: 'things' })]);
    page.dom.document.documentElement.appendChild(view);
    var bag = mod.createNectoShared(view, null);
    await bag.translations;
    return { page: page, mod: mod, view: view, bag: bag };
}

var ROWS = [
    { Id: 'a', Name: 'Alpha', Line: 'first', Enabled: true, Kind: 'One' },
    { Id: 'b', Name: 'Beta', Line: 'second', Enabled: false, Kind: 'Two' },
    { Id: 'c', Name: 'Gamma', Line: 'third', Enabled: true, Kind: 'Two' }
];

// build
// Builds a table of ROWS with every part a tool table has.
function build(s, extra) {
    return s.mod.createToolTable(s.view, s.bag, Object.assign({
        containerId: 'things',
        rows: function () { return ROWS; },
        search: 'Search things',
        filters: [{ label: 'Kind', options: [{ value: 'One', label: 'One' }, { value: 'Two', label: 'Two' }], test: function (row, value) { return row.Kind === value; } }],
        empty: { message: 'No things yet.', filteredMessage: 'No things match.' },
        icon: function () { return 'star'; },
        title: function (row) { return row.Name; },
        lines: function (row) { return [row.Line, row.Kind]; },
        status: function (row) { return { label: row.Enabled ? 'On' : 'Off', color: row.Enabled ? 'green' : 'gray' }; },
        buttons: function () { return [{ action: 'a', icon: 'one', label: 'One' }, { action: 'b', icon: 'two', label: 'Two' }, { action: 'c', icon: 'three', label: 'Three' }]; },
        add: [{ id: 'btnAddThing', icon: 'add', text: 'Add thing', onClick: function () {} }],
        bulk: [{ verb: 'remove', text: 'Remove', variant: 'destructive' }],
        onBulk: function () {}
    }, extra || {}));
}

test('the toolbar holds search, then the filters with All first, then Add at its end', async function () {
    var s = await shared();
    var table = build(s);
    await table.load();
    var controls = s.view.querySelector('.stolon-table-controls');
    var kids = Array.prototype.map.call(controls.children, function (c) { return c.tagName.toLowerCase() + (c.id ? '#' + c.id : ''); });

    assert.equal(controls.querySelector('input').getAttribute('placeholder'), 'Search things');
    assert.deepEqual(texts(controls, 'select option'), ['All', 'One', 'Two']);
    assert.equal(kids[kids.length - 1], 'button#btnAddThing');
    assert.deepEqual(texts(s.view.querySelector('.stolon-table-bulk-actions'), '[data-bulk]'), ['Remove']);
});

test('each row has its icon, its name over one line, one badge, and at most two buttons', async function () {
    var s = await shared();
    var table = build(s);
    await table.load();
    var html = rowHtml(s.view.querySelectorAll('.stolon-table-row')[0]);

    assert.match(html, /data-role="row-icon"[\s\S]*star[\s\S]*Alpha/);
    assert.equal((html.match(/stolon-table-item-sub/g) || []).length, 1);
    assert.match(html, /first · One/);
    assert.equal((html.match(/data-role="row-state"/g) || []).length, 1);
    assert.equal((html.match(/data-row-action/g) || []).length, 2);
});

test('a table with no bulk actions picks no rows and ends its toolbar with Reload', async function () {
    var s = await shared();
    var table = build(s, { bulk: [] });
    await table.load();
    var controls = s.view.querySelector('.stolon-table-controls');

    assert.equal(s.view.querySelector('.stolon-table-row-checkbox'), null);
    assert.ok(controls.children[controls.children.length - 1].classList.contains('stolon-table-reload-btn'));
});

test('the rows a page holds are searched, filtered, and paged as a route would', async function () {
    var s = await shared();
    var query = s.mod.readTableQuery('Things/All?skip=0&take=50&search=gam%20ma&filter=Two');
    var text = function (row) { return row.Name + ' ' + row.Line; };
    var filters = [{ test: function (row, value) { return row.Kind === value; } }];

    assert.deepEqual(query, { skip: '0', take: '50', search: 'gam ma', filter: 'Two' });
    assert.deepEqual(s.mod.tablePageOf(ROWS, { filter: 'Two' }, text, filters).Items.map(function (r) { return r.Id; }), ['b', 'c']);
    assert.deepEqual(s.mod.tablePageOf(ROWS, { search: 'SECOND' }, text, filters).Items.map(function (r) { return r.Id; }), ['b']);
    assert.equal(s.mod.tablePageOf(ROWS, { filter: 'All' }, text, filters).TotalCount, 3);
    var page = s.mod.tablePageOf(ROWS, { skip: 1, take: 1 }, text, filters);
    assert.deepEqual([page.Items[0].Id, page.TotalCount], ['b', 3]);
});

test('with no rows the frame says No X yet, and while a filter finds none it says No X match', async function () {
    var s = await shared();
    var table = build(s, { rows: function () { return []; } });
    await table.load();
    assert.match(s.view.querySelector('#things').textContent, /No things yet\./);
    await table.setFilter('One');
    assert.match(s.view.querySelector('#things').textContent, /No things match\./);
    table.setNone(true);
    assert.match(s.view.querySelector('#things').textContent, /No things yet\./);
});

test('runBulk asks before a removal, runs each row that takes it, and words the result', async function () {
    var s = await shared();
    var calls = [];
    var said = [];
    var done = s.mod.runBulk(s.bag, {
        verb: 'remove', rows: ROWS, prefix: 'InvitesBulk',
        fits: function (row) { return row.Id !== 'b'; },
        confirm: { text: 'Gone for good.', title: 'Remove 3 things' },
        call: function (row) { calls.push(row.Id); return row.Id === 'c' ? Promise.reject(new Error('no')) : Promise.resolve(); },
        report: function (text, isError) { said.push([text, isError]); }
    });
    await settle(20);
    assert.equal(dialogText(s.page.dom).title, 'Remove 3 things');
    clickDialogButton(s.page.dom, 'Remove');
    assert.equal(await done, 1);

    assert.deepEqual(calls, ['a', 'c']);
    assert.equal(said[said.length - 1][1], true);
    assert.match(said[said.length - 1][0], /Removed 1 invite\. Could not change 1 invite\./);
});
