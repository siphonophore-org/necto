// necto_notifications.js
// The Notifications tool. It lists every channel events go to, the user channels first, each with its test.

import * as nectoModule from './configurationpage?name=necto_shared.js';

var h = nectoModule.h;

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
    return shared.t.plural(channel.EventCount, 'NotificationsEventCountOne', 'NotificationsEventCountMany');
}

// sendCounts
// Counts the sends in the log that went and those that failed.
//
// Inputs
// log [object[]]: What Notifications/Log answered.
//
// Output
// [object]: { sent, failed }.
export function sendCounts(log) {
    var failed = (log || []).filter(function (e) { return !e.Ok; }).length;
    return { sent: (log || []).length - failed, failed: failed };
}

// sendCards
// Turns the send counts into the cards over the list.
//
// Inputs
// counts [object]: What sendCounts gives.
// t [object]: The strings.
//
// Output
// [object[]]: The Sent and Failed cards.
export function sendCards(counts, t) {
    return [
        { count: counts.sent, label: t.tr('StateSent'), color: 'green' },
        { count: counts.failed, label: t.tr('LabelFailed'), color: 'red' }
    ];
}

// channelItems
// Turns the channels into rows, each with its kind when it has a name of its own, and at its end its state badge
// while it is off or not set up, then a Test button.
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
        var kind = t.tr(KIND_KEYS[channel.Kind] || 'LabelWebhook');
        // A channel Necto keeps is named by its kind, so its kind tag would only repeat the name.
        var tags = channelName(channel, t) === kind ? [] : [{ label: kind, icon: KIND_ICONS[channel.Kind] || 'send' }];
        var aside = h('div', { class: 'necto-row-aside', 'data-role': 'channel-aside' });
        var state = !channel.Enabled ? ['gray', 'Off'] : !channel.Ready ? ['orange', 'NotificationsTagNotSetUp'] : null;
        if (state) {
            var badge = h('span', { 'data-role': 'channel-state' });
            badge.innerHTML = shared.badge(state[0], t.tr(state[1]));
            aside.appendChild(badge);
        }
        var button = h('button', { is: 'emby-button', type: 'button', class: 'raised stolon-button stolon-secondary', 'data-action': 'test', 'data-channel': channel.Id }, [h('span', { text: t.tr('ButtonTest') })]);
        button.addEventListener('click', function (e) { e.stopPropagation(); onTest(channel, button); });
        aside.appendChild(button);
        return { id: channel.Id, title: channelName(channel, t), secondary: channelSummary(channel, shared), tags: tags, aside: aside, channel: channel };
    });
}

export default function (view) {
    'use strict';

    var shared = null;
    var list = null;
    var sendsDrawn = false;
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
    // Reads the channels and the send log and draws them. An answer that lands after a newer load is dropped.
    function loadChannels() {
        var mine = ++loadSeq;
        // The cards show a dash until the log is read, and keep it when it cannot be.
        if (!sendsDrawn) shared.renderCards('notificationsCards', sendCards({ sent: '–', failed: '–' }, shared.t));
        shared.request('Notifications/Log').then(function (log) {
            if (mine !== loadSeq) return;
            sendsDrawn = true;
            shared.renderCards('notificationsCards', sendCards(sendCounts(Array.isArray(log) ? log : []), shared.t));
        }).catch(function () { /* the dashes stay */ });
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
