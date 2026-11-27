// necto_templates.test.mjs
// Tests for the group templates: each row reads and writes its value in the template, the editor draws Access and
// features and Preferences, a row set by the group is saved as managed, and a header counts the rows the group sets.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { join, dirname } from 'node:path';
import { installPage, loadScript, pageView, settle, routeApi, texts, clickButton } from './support.mjs';

var policies = join(dirname(fileURLToPath(import.meta.url)), '..', '..', 'Jellyfin.Plugin.Necto', 'Configuration', 'Policies');

globalThis.setTimeout = function () { return 1; };
globalThis.clearTimeout = function () {};

var EVERYONE = '5a1f0c7e-0000-4000-8000-000000000001';
var FAMILY = '11111111-2222-3333-4444-555555555555';
var KIDS = '11111111-2222-3333-4444-666666666666';

// openPage
// Shows the Groups tool with Family and Kids, and the events Notifications lists.
async function openPage(answers) {
    var page = installPage({ config: { InviteSettings: { Enabled: true } } });
    var dom = page.dom;
    var view = pageView(dom, [dom.el('div', { id: 'groupList' }), dom.el('button', { id: 'btnAddGroup' }), dom.el('span', { id: 'groupsStatus' })]);
    dom.document.documentElement.appendChild(view);
    routeApi(page, Object.assign({
        'GET Groups': [
            { Id: EVERYONE, Name: 'Everyone', IsEveryone: true, MemberIds: [], Permissions: {} },
            { Id: FAMILY, Name: 'Family', MemberIds: [], Permissions: { Notifications: { AllEvents: false, EventKeys: ['invites.used'] } }, Template: { Enabled: true, Access: { ManageEnableRemoteAccess: true, EnableRemoteAccess: false }, Preferences: {} } },
            { Id: KIDS, Name: 'Kids', MemberIds: [], Permissions: {} }
        ],
        'GET Groups/Users': [],
        'GET Devices': { Items: [] },
        'GET Notifications/Events': [
            { Key: 'invites.used', Name: 'Invite used', Tool: 'invites', ForUsers: true },
            { Key: 'invites.expired', Name: 'Invites expired', Tool: 'invites', ForUsers: false }
        ]
    }, answers || {}));
    var mod = await loadScript('necto_groups.js');
    mod.default(view);
    view.dispatchEvent(new globalThis.CustomEvent('viewshow'));
    await settle();
    return { page: page, view: view, dom: dom, mod: mod };
}

test('rowValue and setRowValue read and write each kind of row in the template', async function () {
    installPage({});
    var templates = await loadScript('necto_groups_templates.js');
    var part = { ManageLibraryAccess: true, EnableAllFolders: false, EnabledFolders: ['a'], RemoteClientBitrateLimit: 8000000, ManageMaxParentalRating: true, MaxParentalRating: 13 };

    assert.deepEqual(templates.rowValue(part, { key: 'LibraryAccess', type: 'library' }), { manage: true, value: { all: false, ids: ['a'] } });
    assert.deepEqual(templates.rowValue(part, { key: 'RemoteClientBitrateLimit', type: 'bitrate' }).value, 8);
    assert.equal(templates.rowValue(part, { key: 'MaxParentalRating', type: 'rating' }).value, '13');
    assert.deepEqual(templates.rowValue({}, { key: 'HomeSections', type: 'homesections' }).value, templates.DEFAULT_HOME_SECTIONS);

    var out = {};
    templates.setRowValue(out, { key: 'LibraryAccess', type: 'library' }, { manage: true, value: { all: true, ids: ['a'] } });
    templates.setRowValue(out, { key: 'RemoteClientBitrateLimit', type: 'bitrate' }, { manage: true, value: '2.5' });
    templates.setRowValue(out, { key: 'MaxParentalRating', type: 'rating' }, { manage: false, value: '' });
    templates.setRowValue(out, { key: 'LoginAttemptsBeforeLockout', type: 'int', min: -1 }, { manage: true, value: '-5' });
    assert.deepEqual(out, {
        ManageLibraryAccess: true, EnableAllFolders: true, EnabledFolders: [],
        ManageRemoteClientBitrateLimit: true, RemoteClientBitrateLimit: 2500000,
        ManageMaxParentalRating: false, MaxParentalRating: null, MaxParentalSubRating: null,
        ManageLoginAttemptsBeforeLockout: true, LoginAttemptsBeforeLockout: -1
    });
});

test('a parental rating keeps its sub score, and ratings that share both scores are one choice', async function () {
    installPage({});
    var templates = await loadScript('necto_groups_templates.js');
    var row = { key: 'MaxParentalRating', type: 'rating' };

    assert.deepEqual(templates.ratingChoices([
        { Name: 'PG-13', RatingScore: { Score: 13, SubScore: null } },
        { Name: 'TV-14', RatingScore: { Score: 13, SubScore: null } },
        { Name: 'DE-12', RatingScore: { Score: 13, SubScore: 1 } },
        { Name: 'FSK-16', RatingScore: { score: 16, subScore: 0 } },
        { Name: 'Old', Value: 18 },
        { Name: 'Unrated' }
    ]), [['13', 'PG-13/TV-14'], ['13:1', 'DE-12'], ['16:0', 'FSK-16'], ['18', 'Old']]);
    assert.equal(templates.rowValue({ MaxParentalRating: 13, MaxParentalSubRating: 1 }, row).value, '13:1');
    assert.equal(templates.rowValue({ MaxParentalRating: null, MaxParentalSubRating: 1 }, row).value, '');

    var out = {};
    templates.setRowValue(out, row, { manage: true, value: '13:1' });
    assert.deepEqual([out.MaxParentalRating, out.MaxParentalSubRating], [13, 1]);
    templates.setRowValue(out, row, { manage: true, value: '18' });
    assert.deepEqual([out.MaxParentalRating, out.MaxParentalSubRating], [18, null]);
});

test('the home screen rows list only the libraries Library access leaves members, and hidden ones keep their state', async function () {
    installPage({});
    var templates = await loadScript('necto_groups_templates.js');
    var views = [{ Id: 'aa', Name: 'Movies' }, { Id: 'bb', Name: 'Shows' }, { Id: 'tv', Name: 'Live TV' }];
    var libraries = [{ ItemId: 'AA' }, { ItemId: 'bb' }];

    assert.deepEqual(templates.visibleViews(views, libraries, null).map(function (v) { return v.Name; }), ['Movies', 'Shows', 'Live TV']);
    assert.deepEqual(templates.visibleViews(views, libraries, { inherit: true, value: { all: false, ids: [] } }).length, 3);
    assert.deepEqual(templates.visibleViews(views, libraries, { inherit: false, value: { all: true, ids: [] } }).length, 3);
    assert.deepEqual(templates.visibleViews(views, libraries, { inherit: false, value: { all: false, ids: ['aa'] } }).map(function (v) { return v.Name; }), ['Movies', 'Live TV']);

    assert.deepEqual(templates.mergeOrder(['aa', 'bb', 'tv'], ['tv', 'aa']), ['tv', 'aa', 'bb']);
    assert.deepEqual(templates.mergeGrid(true, ['aa', 'bb', 'tv'], ['aa', 'tv'], ['aa'], ['bb']), ['tv', 'bb']);
    assert.deepEqual(templates.mergeGrid(true, ['aa', 'bb', 'tv'], ['aa', 'tv'], ['aa', 'tv'], []), []);
    assert.deepEqual(templates.mergeGrid(false, ['aa', 'bb'], ['aa'], ['aa'], ['bb']), ['aa', 'bb']);
});

test('the editor\'s library order shows only the libraries the template gives, and saves the hidden ones too', async function () {
    var family = { Id: FAMILY, Name: 'Family', MemberIds: [], Permissions: {}, Template: { Enabled: true, Access: { ManageLibraryAccess: true, EnableAllFolders: false, EnabledFolders: ['aa'] }, Preferences: { ManageOrderedViews: true, OrderedViews: ['bb', 'aa', 'tv'] } } };
    var opened = await openPage({
        'GET Groups': [{ Id: EVERYONE, Name: 'Everyone', IsEveryone: true, MemberIds: [], Permissions: {} }, family],
        ['PUT Groups/' + FAMILY]: function (o) { return JSON.parse(o.data); }
    });
    var getJSON = globalThis.ApiClient.getJSON;
    globalThis.ApiClient.getJSON = function (url) {
        if (String(url).indexOf('Library/MediaFolders') >= 0) return Promise.resolve({ Items: [{ Id: 'aa', Name: 'Movies' }, { Id: 'bb', Name: 'Shows' }] });
        if (String(url).indexOf('/Views') >= 0) return Promise.resolve({ Items: [{ Id: 'aa', Name: 'Movies' }, { Id: 'bb', Name: 'Shows' }, { Id: 'tv', Name: 'Live TV' }] });
        return getJSON ? getJSON.apply(this, arguments) : Promise.resolve([]);
    };
    opened.view.querySelectorAll('.stolon-record-list-row')[1].click();
    await settle(80);
    var sidecar = opened.dom.document.querySelector('.stolon-sidecar');

    var order = sidecar.querySelector('[data-field="Preferences.OrderedViews"]').textContent;
    assert.match(order, /Movies/);
    assert.match(order, /Live TV/);
    assert.doesNotMatch(order, /Shows/);

    clickButton(sidecar.querySelector('.stolon-sidecar-shelf'), 'Save');
    await settle(80);
    var body = JSON.parse(opened.page.requests.find(function (r) { return r.type === 'PUT'; }).data);
    assert.deepEqual(body.Template.Preferences.OrderedViews, ['aa', 'tv', 'bb']);
});

test('the subtitle mode row explains the picked mode under its select', async function () {
    var family = { Id: FAMILY, Name: 'Family', MemberIds: [], Permissions: {}, Template: { Enabled: true, Access: {}, Preferences: { ManageSubtitleMode: true, SubtitleMode: 'Smart' } } };
    var opened = await openPage({ 'GET Groups': [{ Id: EVERYONE, Name: 'Everyone', IsEveryone: true, MemberIds: [], Permissions: {} }, family] });
    opened.view.querySelectorAll('.stolon-record-list-row')[1].click();
    await settle(80);
    var row = opened.dom.document.querySelector('.stolon-sidecar [data-field="Preferences.SubtitleMode"]');
    var help = row.querySelector('[data-role="option-help"]');

    assert.equal(help.textContent, 'Subtitles matching the language preference will be loaded when the audio is in a foreign language.');
    var select = row.querySelector('select');
    select.value = 'None';
    select.dispatchEvent(new globalThis.Event('change'));
    assert.equal(help.textContent, 'Subtitles will not be loaded by default. They can still be turned on manually during playback.');
});

test('every template row names a field the server keeps, with its Manage switch', async function () {
    installPage({});
    var templates = await loadScript('necto_groups_templates.js');
    var t = { tr: function (k) { return k; } };
    var keys = [];
    templates.accessSpecs(t).concat(templates.preferenceSpecs(t)).forEach(function (s) { s.rows.forEach(function (r) { keys.push(templates.manageKey(r)); }); });

    var server = (readFileSync(join(policies, 'AccessTemplate.cs'), 'utf8') + readFileSync(join(policies, 'PreferencesTemplate.cs'), 'utf8'))
        .match(/public bool Manage\w+/g).map(function (m) { return m.replace('public bool ', ''); });

    assert.equal(new Set(keys).size, keys.length);
    assert.deepEqual(keys.slice().sort(), server.slice().sort());
});

test('the editor draws the Jellyfin sections and saves the template, the preferences mode, and the notification methods', async function () {
    var opened = await openPage({ ['PUT Groups/' + FAMILY]: function (o) { return JSON.parse(o.data); } });
    opened.view.querySelectorAll('.stolon-record-list-row')[1].click();
    await settle(80);
    var sidecar = opened.dom.document.querySelector('.stolon-sidecar');

    assert.ok(texts(sidecar, '.stolon-collapsible-title').indexOf('Jellyfin access') >= 0);
    assert.ok(texts(sidecar, '.stolon-collapsible-title').indexOf('Jellyfin preferences') >= 0);
    assert.equal(sidecar.querySelector('[data-field="Template.Enabled"]'), null);
    assert.ok(sidecar.querySelector('[data-field="Access.EnableRemoteAccess"]'));
    assert.match(sidecar.querySelector('[data-section="template-access"] > .stolon-collapsible-header').textContent, /1 set by group/);
    assert.match(sidecar.querySelector('[data-section="access-profile"] > .stolon-collapsible-header').textContent, /1 set by group/);
    assert.equal(sidecar.querySelector('[data-section="access-playback"] > .stolon-collapsible-header').textContent.indexOf('set by group'), -1);
    var delivery = sidecar.querySelector('[data-field="Notifications.Delivery"]');
    assert.match(delivery.textContent, /Invite used/);
    assert.doesNotMatch(delivery.textContent, /Invites expired/);
    var browser = delivery.querySelectorAll('input[type="checkbox"]')[0];
    browser.checked = false;
    browser.dispatchEvent(new globalThis.Event('change', { bubbles: true }));
    assert.equal(sidecar.querySelector('[data-field="Template.PreferencesMode"] input[value="Maintain"]').checked, true);

    clickButton(sidecar.querySelector('.stolon-sidecar-shelf'), 'Save');
    await settle(80);

    var put = opened.page.requests.find(function (r) { return r.type === 'PUT'; });
    var body = JSON.parse(put.data);
    assert.equal(body.Template.Enabled, true);
    assert.equal(body.Template.Access.ManageEnableRemoteAccess, true);
    assert.equal(body.Template.Access.EnableRemoteAccess, false);
    assert.equal(body.Template.Access.ManageIsHidden, false);
    assert.deepEqual(body.Permissions.Notifications.BrowserEventKeys, []);
    assert.deepEqual(body.Permissions.Notifications.MessageEventKeys, ['invites.used']);
    assert.equal(body.Template.PreferencesMode, 'Maintain');
});

test('a template stored with its rows off shows every row as the member\'s, and saves them so', async function () {
    var family = { Id: FAMILY, Name: 'Family', MemberIds: [], Permissions: {}, Template: { Enabled: false, Access: { ManageIsHidden: true, IsHidden: true }, Preferences: {} } };
    var opened = await openPage({ 'GET Groups': [{ Id: EVERYONE, Name: 'Everyone', IsEveryone: true, MemberIds: [], Permissions: {} }, family], ['PUT Groups/' + FAMILY]: function (o) { return JSON.parse(o.data); } });
    opened.view.querySelectorAll('.stolon-record-list-row')[1].click();
    await settle(80);
    var sidecar = opened.dom.document.querySelector('.stolon-sidecar');

    assert.equal(sidecar.querySelector('[data-section="template-access"] .stolon-collapsible-badge'), null);
    clickButton(sidecar.querySelector('.stolon-sidecar-shelf'), 'Save');
    await settle(80);
    var body = JSON.parse(opened.page.requests.find(function (r) { return r.type === 'PUT'; }).data);
    assert.equal(body.Template.Enabled, true);
    assert.equal(body.Template.Access.ManageIsHidden, false);
    assert.equal(body.Template.Access.IsHidden, true);
});
