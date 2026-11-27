// necto_menulinks.test.mjs
// Tests for the Menu links section: the editor of the web client's side menu in Settings, its sidecar, its Save, and
// what it says when config.json cannot be written.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { installPage, loadScript, pageView, settle, routeApi, texts, clickButton, clickDialogButton } from './support.mjs';

globalThis.setTimeout = function () { return 1; };
globalThis.clearTimeout = function () {};
globalThis.setInterval = function () { return 1; };
globalThis.clearInterval = function () {};

var t = { tr: function (key) { return key; }, format: function (key, a) { return key + ':' + a; } };

var VIEW = {
    Links: [
        { Name: 'Requests', Icon: 'search', Url: '../requests/', Managed: 'requests' },
        { Name: 'Wiki', Icon: 'help', Url: 'https://wiki.example.com', Managed: null },
        { Name: 'Rules', Icon: '', Url: '../pages/rules', Managed: null }
    ],
    Path: '/srv/jellyfin/web/config.json',
    Writable: true,
    Problem: ''
};

// openTool
// Shows Settings or Troubleshooting with the Menu links routes answering.
async function openTool(name, answers) {
    var page = installPage({ config: {} });
    var dom = page.dom;
    var container = name === 'settings' ? 'settingsSections' : 'troubleshootingSections';
    var view = pageView(dom, [dom.el('div', { id: 'settingsStatus' }), dom.el('div', { id: container })]);
    dom.document.documentElement.appendChild(view);
    routeApi(page, Object.assign({ 'GET Necto/MenuLinks': VIEW, 'GET Pages': [{ Id: 'p1', Title: 'Rules', Slug: 'rules' }] }, answers || {}));
    var mod = await loadScript('necto_' + name + '.js');
    mod.default(view);
    view.dispatchEvent(new globalThis.CustomEvent('viewshow'));
    await settle(50);
    return { page: page, view: view, dom: dom, section: view.querySelector('[data-tool="menulinks"]') };
}

test('the icon list narrows to the names that hold what is typed, and offers about 60 common icons', async function () {
    installPage({});
    var links = await loadScript('necto_menulinks_sections.js');
    assert.ok(links.ICONS.length >= 60);
    assert.equal(new Set(links.ICONS).size, links.ICONS.length);
    assert.deepEqual(links.iconMatches('menu'), ['menu_book']);
    assert.ok(links.iconMatches('MOVIE').indexOf('movie_filter') >= 0);
    assert.deepEqual(links.iconMatches('live tv'), ['live_tv']);
    assert.equal(links.iconMatches('').length, links.ICONS.length);
});

test('the address offers the Requests and Invites pages and each page of Pages, from the web client', async function () {
    installPage({});
    var links = await loadScript('necto_menulinks_sections.js');
    var choices = links.addressChoices(t, [{ Slug: 'rules', Title: 'Rules' }, { Title: 'No slug' }]);
    assert.deepEqual(choices.map(function (c) { return c.value; }), ['../requests/', '../invites/', '../pages/rules']);
    assert.equal(choices[2].label, 'MenuLinksOptionPage:Rules');
    assert.equal(links.addressChoices(t, null).length, 2);
});

test('a link needs a name and an address, and an icon of lowercase letters, digits, and underscores', async function () {
    installPage({});
    var links = await loadScript('necto_menulinks_sections.js');
    assert.equal(links.linkProblem({ Name: 'Wiki', Icon: 'help', Url: '/wiki' }), null);
    assert.equal(links.linkProblem({ Name: ' ', Url: '/wiki' }), 'MenuLinksErrorNameRequired');
    assert.equal(links.linkProblem({ Name: 'Wiki', Url: '' }), 'MenuLinksErrorUrlRequired');
    assert.equal(links.linkProblem({ Name: 'Wiki', Icon: 'Help me', Url: '/wiki' }), 'MenuLinksErrorIcon');
    assert.equal(links.managedText({ Managed: 'issues' }, t), 'MenuLinksManagedIssues');
    assert.equal(links.managedText({ Managed: null }, t), '');
});

test('Settings draws Menu links after the tools, with each link as a row with its icon, name, and address', async function () {
    var opened = await openTool('settings');
    var section = opened.section;
    assert.ok(section);
    assert.equal(section.querySelector('.stolon-collapsible-title').textContent, 'Menu links');
    assert.equal(section.querySelector('.stolon-collapsible-tile .material-icons').textContent, 'add_link');
    var rows = section.querySelectorAll('.necto-menu-link');
    assert.equal(rows.length, 3);
    assert.deepEqual(texts(section, '.necto-menu-link-url'), ['../requests/', 'https://wiki.example.com', '../pages/rules']);
    assert.deepEqual(Array.prototype.map.call(rows, function (r) { return r.querySelector('.stolon-collapsible-tile .material-icons').textContent; }), ['search', 'help', 'link']);
    assert.match(rows[0].querySelector('.necto-menu-link-badge').innerHTML, /MenuLinksBadgeManaged|Managed/);
    assert.equal(rows[1].querySelector('.necto-menu-link-badge'), null);
});

test('moving a link down and saving puts the links in their new order to Necto/MenuLinks', async function () {
    var puts = [];
    var opened = await openTool('settings', { 'PUT Necto/MenuLinks': function (options) { puts.push(JSON.parse(options.data)); return VIEW; } });
    var section = opened.section;
    var down = section.querySelectorAll('.stolon-ordered-list button, ol button').length
        ? Array.prototype.filter.call(section.querySelectorAll('button'), function (b) { return /down/i.test(b.getAttribute('aria-label') || b.title || ''); })
        : [];
    assert.ok(down.length >= 1);
    down[0].click();
    await settle();

    assert.equal(clickButton(section, 'Save'), true);
    await settle(50);

    assert.equal(puts.length, 1);
    assert.deepEqual(puts[0].map(function (l) { return l.Url; }), ['https://wiki.example.com', '../requests/', '../pages/rules']);
    assert.deepEqual(Object.keys(puts[0][0]).sort(), ['Icon', 'Name', 'Url']);
});

test('the sidecar of a managed link has no Remove and keeps its address, while another link can be removed', async function () {
    var puts = [];
    var opened = await openTool('settings', { 'PUT Necto/MenuLinks': function (options) { puts.push(JSON.parse(options.data)); return VIEW; } });
    var section = opened.section;
    var edits = section.querySelectorAll('[data-action="edit"]');

    edits[0].click();
    await settle();
    var sidecar = opened.dom.document.querySelector('.stolon-sidecar');
    assert.ok(sidecar);
    assert.match(sidecar.textContent, /MenuLinksManagedRequests|Requests keeps this link/);
    assert.equal(sidecar.querySelector('[data-field="Url"]').disabled, true);
    var shelf = sidecar.querySelector('.stolon-sidecar-shelf');
    assert.equal(texts(shelf, 'button').indexOf('Remove'), -1);
    sidecar.querySelector('[data-field="Name"]').value = 'Ask';
    sidecar.querySelector('[data-icon="movie"]').click();
    assert.equal(sidecar.querySelector('[data-field="Icon"]').value, 'movie');
    clickButton(shelf, 'Done');
    await settle();
    assert.equal(section.querySelectorAll('.necto-menu-link')[0].querySelector('.material-icons').textContent, 'movie');

    section.querySelectorAll('[data-action="edit"]')[1].click();
    await settle();
    sidecar = Array.prototype.slice.call(opened.dom.document.querySelectorAll('.stolon-sidecar')).pop();
    assert.equal(clickButton(sidecar.querySelector('.stolon-sidecar-shelf'), 'Remove'), true);
    await settle();
    clickDialogButton(opened.dom, 'Remove');
    await settle(50);
    assert.equal(section.querySelectorAll('.necto-menu-link').length, 2);

    clickButton(section, 'Save');
    await settle(50);
    assert.deepEqual(puts[0], [{ Name: 'Ask', Icon: 'movie', Url: '../requests/' }, { Name: 'Rules', Icon: '', Url: '../pages/rules' }]);
});

test('a config.json that cannot be written says so in Settings, and Troubleshooting has no Menu links section', async function () {
    var readOnly = Object.assign({}, VIEW, { Writable: false, Problem: 'The web client\'s config.json cannot be written.' });
    var settings = await openTool('settings', { 'GET Necto/MenuLinks': readOnly });
    var problem = settings.section.querySelector('[data-role="problem"]');
    assert.equal(problem.classList.contains('stolon-hidden'), false);
    assert.match(problem.textContent, /cannot be written/);
    assert.equal(settings.section.querySelector('[data-action="add-link"]').disabled, true);

    var trouble = await openTool('troubleshooting', { 'GET Necto/MenuLinks': readOnly });
    assert.equal(trouble.section, null);
});
