// necto_notifications.test.mjs
// Tests for Notifications: the Settings section with its send counts, a test for each channel, the send log, and the
// browsers, which saves through its own route, the Troubleshooting section's repairs, and the browser notification
// helper for user pages.

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

test('the Settings section counts the sends that went and failed, and tests the user channels and each administrators channel', async function () {
    var opened = await openTool('settings', {
        'GET Notifications/Channels': channels(),
        'GET Notifications/Log': [{ Ok: true }, { Ok: false }, { Ok: true }],
        'POST Notifications/Channels/hook/Test': { Ok: false, Message: 'Add an address to the channel first.' },
        'POST Notifications/Channels/browsers/Test': { Ok: true, Message: 'Sent to 1 browser.' }
    });
    var section = opened.section;

    var cards = section.querySelector('[data-role="send-cards"]').textContent;
    assert.match(cards, /2\s*Sent/);
    assert.match(cards, /1\s*Failed/);
    assert.deepEqual(texts(section.querySelector('[data-role="user-tests"]'), 'button'), ['Test browser notifications', 'Test Jellyfin messages']);

    section.querySelector('[data-role="user-tests"] [data-action="test"]').click();
    await settle(50);
    assert.ok(opened.page.requests.some(function (r) { return r.type === 'POST' && r.url === 'Notifications/Channels/browsers/Test'; }));

    var rows = section.querySelectorAll('[data-role="channels"] .stolon-record-list-row');
    assert.equal(rows.length, 3);
    assert.ok(Array.prototype.every.call(rows, function (row) { return !!row.querySelector('[data-action="test"]'); }));
    rows[2].querySelector('[data-action="test"]').click();
    await settle(50);
    assert.ok(opened.page.requests.some(function (r) { return r.type === 'POST' && r.url === 'Notifications/Channels/hook/Test'; }));
    assert.match(section.textContent, /Home Assistant: Add an address to the channel first\./);
});

test('sendCards shows dashes until the log is read, then the sends that went and failed', async function () {
    installPage({});
    var mod = await loadScript('necto_notifications_sections.js');
    var t = { tr: function (k) { return k; } };

    assert.deepEqual(mod.sendCards(null, t).map(function (c) { return c.count; }), ['–', '–']);
    assert.deepEqual(mod.sendCards([{ Ok: true }, { Ok: false }], t).map(function (c) { return c.count; }), [1, 1]);
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

test('Settings ends with the send log and the browsers in tables, and Troubleshooting holds only its repairs', async function () {
    var settings = await openTool('settings');
    var cells = function (role) {
        return Array.prototype.map.call(settings.section.querySelectorAll('[data-role="' + role + '"] .stolon-table-cell'), function (c) { return c.innerHTML; }).join(' ');
    };
    assert.match(cells('log'), /Invite used[\s\S]*Could not send\.[\s\S]*Failed/);
    assert.match(cells('browsers'), /viewer[\s\S]*Safari/);
    assert.equal(settings.section.querySelector('[data-role="held"]').textContent, '2 Jellyfin messages wait for an app to come online.');
    var titles = texts(settings.section, '.stolon-collapsible-section .stolon-collapsible-title');
    assert.deepEqual(titles.slice(-2), ['Send log', 'Browsers']);

    var opened = await openTool('troubleshooting', { 'POST Notifications/Push/Keys': { Count: 1 } });
    var section = opened.section;
    // The tests live beside their channels in Settings, and the log and browsers show there too.
    assert.equal(section.querySelector('[data-action="test-me"]'), null);
    assert.equal(section.querySelector('[data-role="log"]'), null);
    assert.deepEqual(texts(section, '.stolon-subsection-title'), ['Repairs']);
    assert.deepEqual(Array.prototype.map.call(section.querySelectorAll('[data-action]'), function (b) { return b.getAttribute('data-action'); }), ['clear-log', 'clear-held', 'reset-keys']);

    section.querySelector('[data-action="reset-keys"]').click();
    await settle(50);
    clickDialogButton(opened.dom, 'Replace');
    await settle(50);
    assert.ok(opened.page.requests.some(function (r) { return r.type === 'POST' && r.url === 'Notifications/Push/Keys'; }));
});

test('a custom body lists every placeholder the server fills', async function () {
    installPage({});
    var mod = await loadScript('necto_notifications_sections.js');

    assert.deepEqual(mod.bodyPlaceholders({ tr: function (k) { return k; } }).map(function (p) { return p[0]; }),
        ['title', 'body', 'event', 'eventName', 'level', 'link', 'time', 'color', 'serverURL', 'userId']);
});

test('the Settings section hides browser notifications and their test on a page that is not HTTPS', async function () {
    globalThis.isSecureContext = false;
    try {
        var opened = await openTool('settings', { 'GET Notifications/Channels': channels() });
        var section = opened.section;

        assert.equal(section.querySelector('[data-field="BrowserNotifications"]').closest('.stolon-hidden') !== null, true);
        assert.deepEqual(texts(section.querySelector('[data-role="user-tests"]'), 'button'), ['Test Jellyfin messages']);
    } finally {
        delete globalThis.isSecureContext;
    }
});
