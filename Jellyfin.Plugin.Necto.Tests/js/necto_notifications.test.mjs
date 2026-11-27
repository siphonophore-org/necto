// necto_notifications.test.mjs
// Tests for Notifications: the channel list with a test for each, the Settings section that saves through its own
// route, the Troubleshooting section's log and repairs, and the browser notification helper for user pages.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { installPage, loadScript, pageView, settle, routeApi, texts, clickButton, clickDialogButton, statusText } from './support.mjs';

globalThis.setTimeout = function () { return 1; };
globalThis.clearTimeout = function () {};
globalThis.setInterval = function () { return 1; };
globalThis.clearInterval = function () {};

var KEPT = '__JPK_SECRET_KEPT__';

// channels
// The channels Notifications/Channels answers with.
function channels() {
    return [
        { Id: 'browsers', Kind: 'Browsers', Name: '', Enabled: true, Ready: true, AllEvents: false, EventCount: 0 },
        { Id: 'apps', Kind: 'Apps', Name: '', Enabled: false, Ready: true, AllEvents: false, EventCount: 0 },
        { Id: 'administrators', Kind: 'Administrators', Name: '', Enabled: true, Ready: true, AllEvents: true, EventCount: 0 },
        { Id: 'hook', Kind: 'Webhook', Name: 'Home Assistant', Enabled: true, Ready: false, AllEvents: false, EventCount: 2 }
    ];
}

// settings
// The settings Notifications/Settings answers with, every secret hidden.
function settings() {
    return {
        BrowserNotifications: true, JellyfinMessages: true, ServerUrl: 'https://media.example.com', PushContact: '',
        Smtp: { Host: 'smtp.example.com', Port: 587, UseTls: true, Username: 'jellyfin', Password: KEPT, FromAddress: 'jellyfin@example.com', FromName: '' },
        Channels: [
            { Id: 'administrators', Kind: 3, Enabled: true, AllEvents: true, EventKeys: [] },
            { Id: 'activity-log', Kind: 4, Enabled: true, AllEvents: false, EventKeys: [] },
            { Id: 'hook', Kind: 1, Name: 'Home Assistant', Enabled: true, AllEvents: false, EventKeys: ['invites.used'], Url: KEPT, Headers: '' }
        ]
    };
}

// openTool
// Shows Settings or Troubleshooting with the Notifications routes answered.
async function openTool(name, answers) {
    var page = installPage({ config: {} });
    var dom = page.dom;
    var container = name === 'settings' ? 'settingsSections' : 'troubleshootingSections';
    var view = pageView(dom, [dom.el('div', { id: 'settingsStatus' }), dom.el('div', { id: container })]);
    dom.document.documentElement.appendChild(view);
    routeApi(page, Object.assign({
        'GET Notifications/Settings': settings(),
        'GET Notifications/Events': [{ Key: 'invites.used', Name: 'Invite used', ForUsers: true }],
        'GET Notifications/Log': [{ Time: '2026-11-26T12:00:00Z', EventKey: 'invites.used', Title: 'Invite used', Channel: 'Home Assistant', Ok: false, Message: 'Could not send.', Test: false }],
        'GET Notifications/Push': { HasKeys: true, Browsers: [{ UserName: 'viewer', Label: 'Safari · iPhone', CreatedUtc: '2026-11-26T12:00:00Z' }], Held: 2 },
        'GET Groups/ResetCodes': { Enabled: false }
    }, answers || {}));
    var mod = await loadScript('necto_' + name + '.js');
    mod.default(view);
    view.dispatchEvent(new globalThis.CustomEvent('viewshow'));
    await settle(80);
    return { page: page, view: view, dom: dom, section: view.querySelector('[data-tool="notifications"]') };
}

test('the page lists every channel with its kind, its state, and a Test that reports what happened', async function () {
    var page = installPage({});
    var dom = page.dom;
    var view = pageView(dom, [dom.el('div', { id: 'notificationsStatus' }), dom.el('div', { id: 'channelList' }), dom.el('button', { id: 'btnChannelSettings' })]);
    dom.document.documentElement.appendChild(view);
    routeApi(page, { 'GET Notifications/Channels': channels(), 'POST Notifications/Channels/hook/Test': { Ok: false, Message: 'Add an address to the channel first.' } });
    var mod = await loadScript('necto_notifications.js');
    mod.default(view);
    view.dispatchEvent(new globalThis.CustomEvent('viewshow'));
    await settle(50);

    var rows = view.querySelectorAll('.stolon-record-list-row');
    assert.equal(rows.length, 4);
    assert.match(rows[0].textContent, /Browser notifications/);
    assert.match(rows[1].textContent, /Off/);
    assert.match(rows[3].textContent, /Home Assistant[\s\S]*2 events[\s\S]*Not set up/);

    rows[3].querySelector('[data-action="test"]').click();
    await settle(50);
    assert.ok(page.requests.some(function (r) { return r.type === 'POST' && r.url === 'Notifications/Channels/hook/Test'; }));
    assert.equal(statusText(view.querySelector('#notificationsStatus')), 'Home Assistant: Add an address to the channel first.');
});

test('channelSummary says what each channel gets', async function () {
    installPage({});
    var mod = await loadScript('necto_notifications.js');
    var shared = { t: { tr: function (k) { return k; }, plural: function (n, one, many) { return n + (n === 1 ? one : many); } } };

    assert.equal(mod.channelSummary({ Kind: 'Browsers' }, shared), 'NotificationsByGroups');
    assert.equal(mod.channelSummary({ Kind: 'Email', AllEvents: true }, shared), 'NotificationsEveryEvent');
    assert.equal(mod.channelSummary({ Kind: 'Email', EventCount: 0 }, shared), 'NotificationsNoEvents');
    assert.equal(mod.channelSummary({ Kind: 'Email', EventCount: 3 }, shared), '3NotificationsEventCountMany');
});

test('the Settings section fills from its route and saves back through it, keeping hidden secrets as kept', async function () {
    var opened = await openTool('settings', { 'PUT Notifications/Settings': function (o) { return JSON.parse(o.data); } });
    var section = opened.section;

    assert.deepEqual(texts(section, '.stolon-subsection-title'), ['User channels', 'Links', 'Email server', "Administrators' channels"]);
    assert.equal(section.querySelector('[data-field="ServerUrl"]').value, 'https://media.example.com');
    assert.equal(section.querySelector('[data-field="Smtp.Host"]').value, 'smtp.example.com');
    assert.equal(section.querySelectorAll('[data-role="channels"] .stolon-record-list-row').length, 3);

    section.querySelector('[data-field="ServerUrl"]').value = 'https://jellyfin.example.com';
    assert.equal(clickButton(section, 'Save'), true);
    await settle(80);

    var put = opened.page.requests.find(function (r) { return r.type === 'PUT' && r.url === 'Notifications/Settings'; });
    var body = JSON.parse(put.data);
    assert.equal(body.ServerUrl, 'https://jellyfin.example.com');
    assert.equal(body.Smtp.Password, KEPT);
    assert.equal(body.Channels[2].Url, KEPT);
    assert.ok(!opened.page.requests.some(function (r) { return r.type === 'POST' && /Plugins/.test(r.url || ''); }));
});

test('Add webhook opens a channel editor, and Done adds the channel to the list', async function () {
    var opened = await openTool('settings');
    var section = opened.section;
    section.querySelector('[data-action="add-webhook"]').click();
    await settle(50);
    var sidecar = opened.dom.document.querySelector('.stolon-sidecar');

    assert.ok(sidecar.querySelector('[data-field="Url"]'));
    assert.ok(sidecar.querySelector('[data-field="Headers"]'));
    sidecar.querySelector('[data-field="Name"]').value = 'Alerts';
    clickButton(sidecar.querySelector('.stolon-sidecar-shelf'), 'Done');
    await settle(50);

    assert.equal(section.querySelectorAll('[data-role="channels"] .stolon-record-list-row').length, 4);
    assert.match(section.querySelector('[data-role="channels"]').textContent, /Alerts/);
});

test('the channel editor refuses a channel with no name', async function () {
    installPage({});
    var mod = await loadScript('necto_notifications_sections.js');
    var shared = { t: { tr: function (k) { return k; }, format: function (k) { return k; }, plural: function (n, one, many) { return n === 1 ? one : many; } }, setStatus: function () {} };
    var editor = mod.createChannelEditor(mod.newChannel('Email', ''), { shared: shared, events: [], smtpSet: true });

    assert.equal(editor.validate(), 'NotificationsErrorChannelName');
    assert.equal(mod.kindName(2), 'Email');
    assert.equal(mod.isBuiltIn({ Kind: 4 }), true);
});

test('the channel editor offers what posts carry, the requester off on a new channel, and saves each switch', async function () {
    installPage({});
    var mod = await loadScript('necto_notifications_sections.js');
    var shared = { t: { tr: function (k) { return k; }, format: function (k) { return k; }, plural: function (n, one, many) { return n === 1 ? one : many; } }, setStatus: function () {} };
    var hook = mod.newChannel('Webhook', 'Hook');
    var editor = mod.createChannelEditor(hook, { shared: shared, events: [], smtpSet: true });

    assert.equal(hook.ShowRequester, false);
    assert.equal(hook.ShowPoster, true);
    assert.equal(editor.element.querySelector('[data-field="ShowRequester"]').checked, false);
    assert.ok(editor.element.querySelector('[data-field="ShowRecord"]'));
    editor.element.querySelector('[data-field="ShowRequester"]').checked = true;
    editor.element.querySelector('[data-field="ShowSeasons"]').checked = false;
    var saved = editor.getValue();
    assert.equal(saved.ShowRequester, true);
    assert.equal(saved.ShowSeasons, false);
    assert.equal(saved.ShowNote, true);

    assert.deepEqual(mod.postFields('Discord').map(function (f) { return f.key; }), ['ShowPoster', 'ShowOverview', 'ShowRequester', 'ShowJoined', 'ShowSeasons', 'ShowNote', 'ShowTime']);
    assert.deepEqual(mod.postFields('Email').map(function (f) { return f.key; }), ['ShowRequester']);
    assert.deepEqual(mod.postFields('Administrators'), []);
});

test('the Troubleshooting section lists the send log and the browsers, and asks before new push keys', async function () {
    var opened = await openTool('troubleshooting', { 'POST Notifications/Push/Keys': { Count: 1 } });
    var section = opened.section;

    assert.match(section.querySelector('[data-role="log"]').textContent, /Invite used[\s\S]*Could not send\./);
    assert.match(section.querySelector('[data-role="browsers"]').textContent, /viewer[\s\S]*Safari/);
    assert.equal(section.querySelector('[data-role="held"]').textContent, '2 Jellyfin messages wait for an app to come online.');

    section.querySelector('[data-action="reset-keys"]').click();
    await settle(50);
    clickDialogButton(opened.dom, 'Make new keys');
    await settle(50);
    assert.ok(opened.page.requests.some(function (r) { return r.type === 'POST' && r.url === 'Notifications/Push/Keys'; }));
});

test('a custom body lists every placeholder the server fills', async function () {
    installPage({});
    var mod = await loadScript('necto_notifications_sections.js');

    assert.deepEqual(mod.bodyPlaceholders({ tr: function (k) { return k; } }).map(function (p) { return p[0]; }),
        ['title', 'body', 'event', 'eventName', 'level', 'link', 'time', 'color', 'serverURL', 'userId']);
});
