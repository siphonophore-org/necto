// necto_notifications.js
// The Notifications tool. It lists every channel events go to, the user channels first, each with its test.

import * as nectoModule from './configurationpage?name=necto_shared.js';

var el = nectoModule.el;

// KIND_KEYS
// The string key of each channel kind's name, as Notifications/Channels names the kinds.
export var KIND_KEYS = {
    Browsers: 'LabelBrowserNotifications',
    Apps: 'NotificationsChannelApps',
    Administrators: 'LabelAdministrators',
    ActivityLog: 'NotificationsChannelActivityLog',
    Discord: 'NotificationsKindDiscord',
    Webhook: 'LabelWebhook',
    Email: 'NotificationsKindEmail'
};

// KIND_ICONS
// The icon of each channel kind.
export var KIND_ICONS = {
    Browsers: 'notifications_active',
    Apps: 'chat',
    Administrators: 'admin_panel_settings',
    ActivityLog: 'history',
    Discord: 'forum',
    Webhook: 'webhook',
    Email: 'mail'
};

// channelName
// Names a channel: the name it was given, or its kind's name for one Necto keeps.
//
// Inputs
// channel [object]: The channel from Notifications/Channels.
// t [object]: The strings.
//
// Output
// [string]: The name.
export function channelName(channel, t) {
    return channel.Name || t.tr(KIND_KEYS[channel.Kind] || 'LabelWebhook');
}

// channelSummary
// Says which events a channel gets, in one line.
//
// Inputs
// channel [object]: The channel.
// shared [object]: The Necto helper bag.
//
// Output
// [string]: The line.
export function channelSummary(channel, shared) {
    var t = shared.t;
    if (channel.Kind === 'Browsers' || channel.Kind === 'Apps') return t.tr('NotificationsByGroups');
    if (channel.AllEvents) return t.tr('NotificationsEveryEvent');
    if (!channel.EventCount) return t.tr('NotificationsNoEvents');
    return shared.plural(channel.EventCount, 'NotificationsEventCountOne', 'NotificationsEventCountMany');
}

// channelItems
// Turns the channels into rows, each with its kind, its state, and a Test button.
//
// Inputs
// channels [object[]]: The channels from Notifications/Channels.
// shared [object]: The Necto helper bag.
// onTest [function]: Called with the channel and its button when Test is selected.
//
// Output
// [object[]]: The rows.
export function channelItems(channels, shared, onTest) {
    var t = shared.t;
    return (channels || []).map(function (channel) {
        var tags = [{ label: t.tr(KIND_KEYS[channel.Kind] || 'LabelWebhook'), icon: KIND_ICONS[channel.Kind] || 'send' }];
        if (!channel.Enabled) tags.push({ label: t.tr('Off'), color: 'gray', off: true });
        else if (!channel.Ready) tags.push({ label: t.tr('NotificationsTagNotSetUp'), color: 'orange' });
        var button = el('button', { is: 'emby-button', type: 'button', class: 'raised stolon-button stolon-secondary stolon-small', 'data-action': 'test', 'data-channel': channel.Id }, [el('span', { text: t.tr('ButtonTest') })]);
        button.addEventListener('click', function (e) { e.stopPropagation(); onTest(channel, button); });
        return { id: channel.Id, title: channelName(channel, t), secondary: channelSummary(channel, shared), tags: tags, aside: button, channel: channel };
    });
}

export default function (view) {
    'use strict';

    var shared = null;
    var list = null;
    var loadSeq = 0;

    // report
    // Shows a result on the page's status line.
    function report(message, isError) {
        shared.setStatus('notificationsStatus', message, !!isError, { timeout: 10000 });
    }

    // test
    // Sends a test to one channel and reports what happened.
    function test(channel, button) {
        var t = shared.t;
        button.disabled = true;
        shared.request('Notifications/Channels/' + encodeURIComponent(channel.Id) + '/Test', 'POST').then(function (result) {
            report(channelName(channel, t) + ': ' + ((result && result.Message) || ''), !(result && result.Ok));
        }).catch(function (err) {
            return shared.errorText(err, t.tr('NotificationsErrorTest')).then(function (text) { report(text, true); });
        }).then(function () { button.disabled = false; });
    }

    // loadChannels
    // Reads the channels and draws them. An answer that lands after a newer load is dropped.
    function loadChannels() {
        var mine = ++loadSeq;
        return shared.request('Notifications/Channels').then(function (channels) {
            if (mine !== loadSeq) return;
            var items = channelItems(Array.isArray(channels) ? channels : [], shared, test);
            if (list) list.setItems(items);
            else {
                list = nectoModule.createRecordList({ items: items, label: shared.t.tr('TabNotifications') });
                view.querySelector('#channelList').appendChild(list.element);
            }
        }).catch(function () {
            if (mine === loadSeq) report(shared.t.tr('NotificationsErrorLoad'), true);
        });
    }

    var made = nectoModule.createNectoPage(view, {
        page: 'necto_notifications',
        bind: function () {
            var settings = view.querySelector('#btnChannelSettings');
            if (settings) settings.addEventListener('click', function () { nectoModule.openTool(view, 'settings'); });
        },
        load: function () { loadChannels(); }
    });
    shared = made.shared;
}
