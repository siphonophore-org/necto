// necto_notifications_sections.js
// The Notifications sections of Settings and Troubleshooting, which this module adds to the registry when it loads.

import * as nectoModule from './configurationpage?name=necto_shared.js';

var el = nectoModule.el;

// The kinds an administrator may add, by the number the server keeps them as.
export var CHANNEL_KINDS = { Discord: 0, Webhook: 1, Email: 2, Administrators: 3, ActivityLog: 4 };
var KIND_NAMES = ['Discord', 'Webhook', 'Email', 'Administrators', 'ActivityLog'];

// KIND_KEYS
// The string key of each kind's name, and of the button that adds one.
export var KIND_KEYS = { Discord: 'NotificationsKindDiscord', Webhook: 'NotificationsKindWebhook', Email: 'NotificationsKindEmail', Administrators: 'NotificationsKindAdministrators', ActivityLog: 'NotificationsKindActivityLog' };
var ADD_KEYS = { Discord: 'NotificationsButtonAddDiscord', Webhook: 'NotificationsButtonAddWebhook', Email: 'NotificationsButtonAddEmail' };

// Ids for fields, so two sections never share one.
var fieldSeq = 0;

// fieldId
// Makes an id for a field.
//
// Inputs
// name [string]: What the field is.
//
// Output
// [string]: An id no other field has.
function fieldId(name) {
    fieldSeq += 1;
    return 'necto-notifications-' + name + '-' + fieldSeq;
}

// kindName
// Reads a channel's kind, which the server may send as its number or its name.
//
// Inputs
// kind [number|string]: The kind.
//
// Output
// [string]: The kind's name.
export function kindName(kind) {
    return typeof kind === 'number' ? (KIND_NAMES[kind] || 'Webhook') : (KIND_NAMES.indexOf(kind) >= 0 ? kind : 'Webhook');
}

// isBuiltIn
// Tells whether a channel is one Necto keeps, which cannot be removed or renamed.
//
// Inputs
// channel [object]: The channel.
//
// Output
// [boolean]: Whether it is Administrators or the activity log.
export function isBuiltIn(channel) {
    var kind = kindName(channel.Kind);
    return kind === 'Administrators' || kind === 'ActivityLog';
}

// newChannel
// Builds an added channel of a kind, on, with no events.
//
// Inputs
// kind [string]: Discord, Webhook, or Email.
// name [string]: Its name.
//
// Output
// [object]: The channel.
export function newChannel(kind, name) {
    return { Id: '', Name: name, Kind: CHANNEL_KINDS[kind], Enabled: true, AllEvents: false, EventKeys: [], Url: '', Headers: '', Username: '', AvatarUrl: '', UseCustomBody: false, Body: '', Recipients: '' };
}

// channelTitle
// Names a channel in the editor's list.
//
// Inputs
// channel [object]: The channel.
// t [object]: The strings.
//
// Output
// [string]: The name.
export function channelTitle(channel, t) {
    var kind = kindName(channel.Kind);
    if (kind === 'Administrators') return t.tr('NotificationsChannelAdministrators');
    if (kind === 'ActivityLog') return t.tr('NotificationsChannelActivityLog');
    return channel.Name || t.tr(KIND_KEYS[kind]);
}

// eventsLine
// Says which events a channel picks, in one line.
//
// Inputs
// channel [object]: The channel.
// shared [object]: The Necto helper bag.
//
// Output
// [string]: The line.
export function eventsLine(channel, shared) {
    if (channel.AllEvents) return shared.t.tr('NotificationsEveryEvent');
    var count = (channel.EventKeys || []).length;
    return count === 0 ? shared.t.tr('NotificationsNoEvents') : shared.plural(count, 'NotificationsEventCountOne', 'NotificationsEventCountMany');
}

// field
// Builds a labelled input with its description.
//
// Inputs
// opts [object]: label, help, type, value, placeholder, and data, the input's data-field.
//
// Output
// [object]: wrap, the container, and input.
function field(opts) {
    var id = fieldId(opts.data || 'field');
    var input = opts.type === 'textarea'
        ? el('textarea', { is: 'emby-textarea', id: id, rows: '4', class: 'emby-textarea', 'data-field': opts.data })
        : el('input', { is: 'emby-input', type: opts.type || 'text', id: id, placeholder: opts.placeholder || null, 'data-field': opts.data, min: opts.min || null });
    input.value = opts.value == null ? '' : String(opts.value);
    var wrap = el('div', { class: 'inputContainer' }, [
        el('label', { class: 'inputLabel inputLabelUnfocused', for: id, text: opts.label }),
        input,
        opts.help ? el('div', { class: 'fieldDescription', text: opts.help }) : null
    ]);
    return { wrap: wrap, input: input };
}

// clearButton
// Builds the button that clears a stored secret on the next save, placed under its field.
//
// Inputs
// wrap [HTMLElement]: The field's container.
// t [object]: The strings.
//
// Output
// [HTMLButtonElement]: The button, for bindSecretField's clearButton.
function clearButton(wrap, t) {
    var button = el('button', { is: 'emby-button', type: 'button', class: 'raised stolon-button stolon-secondary stolon-small', 'data-action': 'clear-secret' }, [el('span', { text: t.tr('NotificationsButtonClear') })]);
    wrap.appendChild(button);
    return button;
}

// check
// Builds a checkbox with its description.
//
// Inputs
// opts [object]: label, help, checked, and data, the input's data-field.
//
// Output
// [object]: wrap, the container, and input.
function check(opts) {
    var input = el('input', { type: 'checkbox', is: 'emby-checkbox', id: fieldId(opts.data || 'check'), 'data-field': opts.data });
    input.checked = !!opts.checked;
    var wrap = el('div', { class: 'checkboxContainer' + (opts.help ? ' checkboxContainer-withDescription' : '') }, [
        el('label', {}, [input, el('span', { text: opts.label })]),
        opts.help ? el('div', { class: 'fieldDescription checkboxFieldDescription', text: opts.help }) : null
    ]);
    return { wrap: wrap, input: input };
}

// buildSettings
// Fills the Notifications section of Settings: the user channels, the server's address and push contact, the email
// server, and the administrators' channels, each edited in a sidecar. It saves through Notifications/Settings, so no
// secret reaches the page.
//
// Inputs
// content [HTMLElement]: The section's content.
// ctx [object]: shared is the Necto helper bag, and page the page handle.
//
// Output
// [object]: A handle with load, apply, save, and destroy.
function buildSettings(content, ctx) {
    var shared = ctx.shared;
    var t = shared.t;
    var settings = null;
    var events = [];
    var list = null;
    var status = el('div', { class: 'stolon-status-text', role: 'status' });

    var browsers = check({ label: t.tr('NotificationsChannelBrowsers'), help: t.tr('NotificationsBrowsersHelp'), data: 'BrowserNotifications' });
    var apps = check({ label: t.tr('NotificationsChannelApps'), help: t.tr('NotificationsAppsHelp'), data: 'JellyfinMessages' });
    var server = field({ label: t.tr('NotificationsLabelServerUrl'), help: t.tr('NotificationsServerUrlHelp'), placeholder: 'https://jellyfin.example.com', data: 'ServerUrl' });
    var contact = field({ label: t.tr('NotificationsLabelPushContact'), help: t.tr('NotificationsPushContactHelp'), placeholder: 'mailto:admin@example.com', data: 'PushContact' });
    var host = field({ label: t.tr('NotificationsLabelSmtpHost'), placeholder: 'smtp.example.com', data: 'Smtp.Host' });
    var port = field({ label: t.tr('NotificationsLabelSmtpPort'), type: 'number', min: '1', data: 'Smtp.Port', help: t.tr('NotificationsSmtpPortHelp') });
    var tls = check({ label: t.tr('NotificationsLabelSmtpTls'), help: t.tr('NotificationsSmtpTlsHelp'), data: 'Smtp.UseTls' });
    var user = field({ label: t.tr('NotificationsLabelSmtpUser'), data: 'Smtp.Username', help: t.tr('NotificationsSmtpUserHelp') });
    var password = field({ label: t.tr('NotificationsLabelSmtpPassword'), type: 'password', data: 'Smtp.Password' });
    var passwordClear = clearButton(password.wrap, t);
    var from = field({ label: t.tr('NotificationsLabelFromAddress'), type: 'email', placeholder: 'jellyfin@example.com', data: 'Smtp.FromAddress' });
    var fromName = field({ label: t.tr('NotificationsLabelFromName'), placeholder: 'Jellyfin', data: 'Smtp.FromName' });
    var listBox = el('div', { 'data-role': 'channels' });
    var addRow = el('div', { class: 'stolon-card-list-actions' }, ['Discord', 'Webhook', 'Email'].map(function (kind) {
        var button = el('button', { is: 'emby-button', type: 'button', class: 'raised stolon-button stolon-secondary', 'data-action': 'add-' + kind.toLowerCase() }, [
            el('span', { class: 'material-icons', 'aria-hidden': 'true', text: 'add' }),
            el('span', { text: t.tr(ADD_KEYS[kind]) })
        ]);
        button.addEventListener('click', function () { openChannel(newChannel(kind, t.tr(KIND_KEYS[kind])), true); });
        return button;
    }));

    [
        status,
        el('h3', { class: 'stolon-subsection-title', text: t.tr('NotificationsHeaderUsers') }),
        el('div', { class: 'fieldDescription', text: t.tr('NotificationsUsersHelp') }),
        browsers.wrap, apps.wrap,
        el('h3', { class: 'stolon-subsection-title', text: t.tr('InvitesHeaderLinks') }),
        server.wrap, contact.wrap,
        el('h3', { class: 'stolon-subsection-title', text: t.tr('NotificationsHeaderEmail') }),
        host.wrap, port.wrap, tls.wrap, user.wrap, password.wrap, from.wrap, fromName.wrap,
        el('h3', { class: 'stolon-subsection-title', text: t.tr('NotificationsHeaderChannels') }),
        el('div', { class: 'fieldDescription', text: t.tr('NotificationsChannelsHelp') }),
        listBox, addRow
    ].forEach(function (node) { content.appendChild(node); });

    // changed
    // Tells the section's Save Bar that something changed outside its fields.
    function changed() {
        content.dispatchEvent(new CustomEvent('change', { bubbles: true }));
    }

    // drawChannels
    // Draws the administrators' channels, each opening its editor.
    function drawChannels() {
        var items = (settings.Channels || []).map(function (c, i) {
            var tags = [{ label: t.tr(KIND_KEYS[kindName(c.Kind)]), icon: 'send' }];
            if (!c.Enabled) tags.push({ label: t.tr('Off'), color: 'gray', off: true });
            return { id: String(i), title: channelTitle(c, t), secondary: eventsLine(c, shared), tags: tags, channel: c };
        });
        if (list) list.setItems(items);
        else {
            list = nectoModule.createRecordList({ items: items, label: t.tr('NotificationsHeaderChannels'), onOpen: function (item) { openChannel(item.channel, false); } });
            listBox.appendChild(list.element);
        }
    }

    // openChannel
    // Opens a channel in an editor sidecar. Done keeps the changes in the section until Save.
    function openChannel(channel, isNew) {
        var editor = createChannelEditor(JSON.parse(JSON.stringify(channel)), { shared: shared, events: events, smtpSet: !!(host.input.value && from.input.value) });
        nectoModule.openEditorSidecar({
            title: isNew ? t.tr('NotificationsTitleNewChannel') : channelTitle(channel, t),
            body: editor.element,
            page: ctx.page,
            isNew: isNew || isBuiltIn(channel),
            serialize: function () { return JSON.stringify(editor.getValue()); },
            saveText: t.tr('NotificationsButtonDone'),
            deleteText: t.tr('ButtonRemove'),
            actions: kindName(channel.Kind) === 'Administrators' ? [] : [{ text: t.tr('Preview'), onClick: function () { editor.preview(); } }],
            onSave: function () {
                var problem = editor.validate();
                if (problem) { editor.setStatus(t.tr(problem), true); return false; }
                var value = editor.getValue();
                var at = settings.Channels.indexOf(channel);
                if (at >= 0) settings.Channels[at] = value; else settings.Channels.push(value);
                drawChannels();
                changed();
                return true;
            },
            onDelete: function (close) {
                nectoModule.confirmDialog(t.tr('NotificationsConfirmRemove'), { title: t.format('NotificationsRemoveName', channelTitle(channel, t)), confirmText: t.tr('ButtonRemove'), destructive: true }).then(function (ok) {
                    if (!ok) return;
                    settings.Channels = settings.Channels.filter(function (c) { return c !== channel; });
                    drawChannels();
                    changed();
                    close('deleted');
                });
            },
            onClose: function () { editor.destroy(); }
        });
    }

    // fill
    // Writes the settings into the fields.
    function fill() {
        browsers.input.checked = settings.BrowserNotifications !== false;
        apps.input.checked = settings.JellyfinMessages !== false;
        server.input.value = settings.ServerUrl || '';
        contact.input.value = settings.PushContact || '';
        var smtp = settings.Smtp || {};
        host.input.value = smtp.Host || '';
        port.input.value = String(smtp.Port || 587);
        tls.input.checked = smtp.UseTls !== false;
        user.input.value = smtp.Username || '';
        from.input.value = smtp.FromAddress || '';
        fromName.input.value = smtp.FromName || '';
        nectoModule.bindSecretField(password.input, smtp.Password === nectoModule.SECRET_KEPT || !!smtp.Password, { clearButton: passwordClear });
        drawChannels();
    }

    // read
    // Reads the fields back into the settings.
    function read() {
        var out = JSON.parse(JSON.stringify(settings));
        out.BrowserNotifications = !!browsers.input.checked;
        out.JellyfinMessages = !!apps.input.checked;
        out.ServerUrl = server.input.value.trim();
        out.PushContact = contact.input.value.trim();
        out.Smtp = {
            Host: host.input.value.trim(),
            Port: parseInt(port.input.value, 10) || 587,
            UseTls: !!tls.input.checked,
            Username: user.input.value.trim(),
            Password: nectoModule.readSecretField(password.input),
            FromAddress: from.input.value.trim(),
            FromName: fromName.input.value.trim()
        };
        return out;
    }

    return {
        load: function () {
            return Promise.all([shared.request('Notifications/Settings'), shared.request('Notifications/Events').catch(function () { return []; })]).then(function (answers) {
                settings = answers[0] || {};
                settings.Channels = Array.isArray(settings.Channels) ? settings.Channels : [];
                settings.Smtp = settings.Smtp || {};
                events = Array.isArray(answers[1]) ? answers[1] : [];
                fill();
            }).catch(function (err) {
                return shared.errorText(err, t.tr('NotificationsErrorLoadSettings')).then(function (text) { shared.setStatus(status, text, true); });
            });
        },
        apply: function () { },
        save: function () {
            if (!settings) return Promise.reject(new Error(t.tr('NotificationsErrorLoadSettings')));
            return shared.request('Notifications/Settings', 'PUT', read()).then(function (saved) {
                settings = saved || settings;
                fill();
            }).catch(function (err) {
                return shared.errorText(err, t.tr('NotificationsErrorSaveSettings')).then(function (text) { throw new Error(text); });
            });
        },
        destroy: function () { if (list) { list.destroy(); list = null; } }
    };
}

// createChannelEditor
// Builds the body of an administrators' channel's sidecar: its name and switch, what it sends to, and its events.
//
// Inputs
// channel [object]: The channel, a copy the editor may change.
// ctx [object]: shared is the Necto helper bag, events every event, and smtpSet whether an SMTP server is set.
//
// Output
// [object]: A handle with element, getValue, validate, setStatus, preview, and destroy.
export function createChannelEditor(channel, ctx) {
    var shared = ctx.shared;
    var t = shared.t;
    var kind = kindName(channel.Kind);
    var handles = [];
    var readers = [];
    var status = el('div', { class: 'stolon-status-text', role: 'status' });
    var previewBox = el('div', { 'data-role': 'preview' });
    var root = el('div', { 'data-role': 'channel-editor', 'data-kind': kind }, [status]);

    if (!isBuiltIn(channel)) {
        var name = field({ label: t.tr('LabelName'), value: channel.Name, data: 'Name' });
        root.appendChild(name.wrap);
        readers.push(function (out) { out.Name = name.input.value.trim(); });
    }

    var on = check({ label: t.tr('NotificationsLabelChannelOn'), checked: channel.Enabled !== false, data: 'Enabled' });
    root.appendChild(on.wrap);
    readers.push(function (out) { out.Enabled = !!on.input.checked; });

    if (kind === 'Discord' || kind === 'Webhook') {
        var url = field({ label: t.tr(kind === 'Discord' ? 'NotificationsLabelDiscordUrl' : 'NotificationsLabelWebhookUrl'), placeholder: kind === 'Discord' ? 'https://discord.com/api/webhooks/...' : 'https://hooks.example.com/jellyfin', data: 'Url', help: t.tr('NotificationsUrlHelp') });
        root.appendChild(url.wrap);
        nectoModule.bindSecretField(url.input, channel.Url === nectoModule.SECRET_KEPT);
        readers.push(function (out) { out.Url = nectoModule.readSecretField(url.input); });
        if (kind === 'Discord') {
            var username = field({ label: t.tr('NotificationsLabelDiscordName'), placeholder: 'Jellyfin', value: channel.Username, data: 'Username' });
            var avatar = field({ label: t.tr('NotificationsLabelDiscordAvatar'), placeholder: 'https://example.com/avatar.png', value: channel.AvatarUrl, data: 'AvatarUrl' });
            root.appendChild(username.wrap);
            root.appendChild(avatar.wrap);
            readers.push(function (out) { out.Username = username.input.value.trim(); out.AvatarUrl = avatar.input.value.trim(); });
        } else {
            var headers = field({ label: t.tr('NotificationsLabelHeaders'), type: 'textarea', data: 'Headers', help: t.tr('NotificationsHeadersHelp') });
            root.appendChild(headers.wrap);
            nectoModule.bindSecretField(headers.input, channel.Headers === nectoModule.SECRET_KEPT, { clearButton: clearButton(headers.wrap, t) });
            readers.push(function (out) { out.Headers = nectoModule.readSecretField(headers.input); });
        }
        var custom = check({ label: t.tr('NotificationsLabelCustomBody'), help: t.tr('NotificationsCustomBodyHelp'), checked: channel.UseCustomBody, data: 'UseCustomBody' });
        var body = field({ label: t.tr('NotificationsLabelBody'), type: 'textarea', value: channel.Body, data: 'Body', help: t.tr('NotificationsBodyHelp') });
        var indent = el('div', { class: 'stolon-indent' }, [body.wrap]);
        root.appendChild(custom.wrap);
        root.appendChild(indent);
        var reveal = function () { indent.classList.toggle('stolon-hidden', !custom.input.checked); };
        custom.input.addEventListener('change', reveal);
        reveal();
        readers.push(function (out) { out.UseCustomBody = !!custom.input.checked; out.Body = body.input.value; });
    } else if (kind === 'Email') {
        if (!ctx.smtpSet) root.appendChild(el('div', { class: 'stolon-warn', text: t.tr('NotificationsResultNoSmtp') }));
        var recipients = field({ label: t.tr('NotificationsLabelRecipients'), type: 'textarea', value: channel.Recipients, data: 'Recipients', help: t.tr('NotificationsRecipientsHelp'), placeholder: 'admin@example.com' });
        root.appendChild(recipients.wrap);
        readers.push(function (out) { out.Recipients = recipients.input.value; });
    } else if (kind === 'Administrators') {
        root.appendChild(el('div', { class: 'fieldDescription', text: t.tr('NotificationsAdministratorsHelp') }));
    } else {
        root.appendChild(el('div', { class: 'fieldDescription', text: t.tr('NotificationsActivityLogHelp') }));
    }

    root.appendChild(el('h3', { class: 'stolon-subsection-title', text: t.tr('NotificationsGroupEvents') }));
    var all = check({ label: t.tr('NotificationsEveryEvent'), checked: channel.AllEvents, data: 'AllEvents' });
    var picks = nectoModule.createCheckboxList({
        items: ctx.events || [],
        idKey: 'Key',
        value: channel.EventKeys || [],
        renderRow: function (e) { return { label: e.Name, sublabel: e.Key }; },
        search: true,
        placeholder: t.tr('NotificationsSearchEvents'),
        emptyMessage: t.tr('NotificationsNoEventsListed')
    });
    handles.push(picks);
    var pickBox = el('div', { class: 'stolon-indent' }, [picks.element]);
    root.appendChild(all.wrap);
    root.appendChild(pickBox);
    var showPicks = function () { pickBox.classList.toggle('stolon-hidden', all.input.checked); };
    all.input.addEventListener('change', showPicks);
    showPicks();
    readers.push(function (out) { out.AllEvents = !!all.input.checked; out.EventKeys = picks.getValue(); });
    root.appendChild(previewBox);

    var previewHandle = null;

    return {
        element: root,
        getValue: function () {
            var out = JSON.parse(JSON.stringify(channel));
            readers.forEach(function (r) { r(out); });
            return out;
        },
        validate: function () {
            var value = this.getValue();
            if (!isBuiltIn(value) && !value.Name) return 'NotificationsErrorChannelName';
            return null;
        },
        setStatus: function (message, isError) { shared.setStatus(status, message, !!isError); },
        preview: function () {
            var self = this;
            return shared.request('Notifications/Preview', 'POST', self.getValue()).then(function (preview) {
                if (previewHandle) previewHandle.setPreview(preview);
                else {
                    previewHandle = nectoModule.createNotifyPreview({ preview: preview });
                    handles.push(previewHandle);
                    previewBox.appendChild(previewHandle.element);
                }
            }).catch(function (err) {
                return shared.errorText(err, t.tr('NotificationsErrorPreview')).then(function (text) { self.setStatus(text, true); });
            });
        },
        destroy: function () { handles.splice(0).forEach(function (h) { if (h && h.destroy) h.destroy(); }); }
    };
}

// logItems
// Turns the send log into rows, newest first, each with its channel and what happened.
//
// Inputs
// entries [object[]]: The entries from Notifications/Log.
// shared [object]: The Necto helper bag.
//
// Output
// [object[]]: The rows.
export function logItems(entries, shared) {
    var t = shared.t;
    return (entries || []).map(function (e, i) {
        var tags = [{ label: e.Ok ? t.tr('NotificationsTagSent') : t.tr('LabelFailed'), color: e.Ok ? 'green' : 'red' }];
        if (e.Test) tags.push({ label: t.tr('NotificationsButtonTest'), color: 'blue' });
        return {
            id: String(i),
            title: e.Title || e.EventKey,
            secondary: [shared.formatDate ? shared.formatDate(e.Time) : e.Time, e.Channel, e.Message].filter(Boolean).join(' · '),
            tags: tags
        };
    });
}

// browserItems
// Turns the browsers users turned notifications on in into rows.
//
// Inputs
// browsers [object[]]: The browsers from Notifications/Push.
// shared [object]: The Necto helper bag.
//
// Output
// [object[]]: The rows.
export function browserItems(browsers, shared) {
    return (browsers || []).map(function (b, i) {
        return { id: String(i), title: b.UserName || shared.t.tr('NotificationsUnknownUser'), secondary: [b.Label, shared.formatDate ? shared.formatDate(b.CreatedUtc) : b.CreatedUtc].filter(Boolean).join(' · ') };
    });
}

// resetRow
// Builds one row of a reset group: a label and a description, and a button at its end.
//
// Inputs
// label [string]: What the row does.
// help [string]: One sentence on what follows.
// button [string]: The button's label.
// action [string]: The value of the button's data-action.
// destructive [boolean]: Whether the button loses data.
//
// Output
// [HTMLElement]: The row.
function resetRow(label, help, button, action, destructive) {
    return el('div', { class: 'stolon-reset-row' }, [
        el('div', { class: 'stolon-reset-info' }, [el('div', { text: label }), el('div', { class: 'fieldDescription', text: help })]),
        el('button', { is: 'emby-button', type: 'button', class: 'raised stolon-button ' + (destructive ? 'stolon-destructive' : 'stolon-secondary') + ' stolon-small', 'data-action': action }, [el('span', { text: button })])
    ]);
}

// buildTroubleshooting
// Fills the Notifications section of Troubleshooting: a test to the administrator, the send log, the browsers with
// notifications on, the held messages, and new push keys.
//
// Inputs
// content [HTMLElement]: The section's content.
// ctx [object]: shared is the Necto helper bag.
//
// Output
// [object]: A handle with load and destroy.
function buildTroubleshooting(content, ctx) {
    var shared = ctx.shared;
    var t = shared.t;
    var logList = null;
    var browserList = null;
    var status = el('div', { class: 'stolon-status-text', role: 'status', 'data-role': 'status' });
    var logBox = el('div', { 'data-role': 'log' });
    var browserBox = el('div', { 'data-role': 'browsers' });
    var heldLine = el('div', { class: 'fieldDescription', 'data-role': 'held' });

    [
        status,
        el('h3', { class: 'stolon-subsection-title', text: t.tr('NotificationsHeaderTests') }),
        el('div', { class: 'stolon-reset-group' }, [
            resetRow(t.tr('NotificationsTestMe'), t.tr('NotificationsTestMeHelp'), t.tr('NotificationsButtonTest'), 'test-me', false)
        ]),
        el('h3', { class: 'stolon-subsection-title', text: t.tr('NotificationsHeaderLog') }),
        el('div', { class: 'fieldDescription', text: t.tr('NotificationsLogHelp') }),
        logBox,
        el('div', { class: 'stolon-card-list-actions' }, [
            el('button', { is: 'emby-button', type: 'button', class: 'raised stolon-button stolon-secondary', 'data-action': 'refresh-log' }, [el('span', { class: 'material-icons', 'aria-hidden': 'true', text: 'refresh' }), el('span', { text: t.tr('Refresh') })]),
            el('button', { is: 'emby-button', type: 'button', class: 'raised stolon-button stolon-secondary', 'data-action': 'clear-log' }, [el('span', { text: t.tr('NotificationsButtonClearLog') })])
        ]),
        el('h3', { class: 'stolon-subsection-title', text: t.tr('NotificationsHeaderBrowsers') }),
        browserBox,
        heldLine,
        el('div', { class: 'stolon-reset-group' }, [
            resetRow(t.tr('NotificationsClearHeld'), t.tr('NotificationsClearHeldHelp'), t.tr('NotificationsButtonClear'), 'clear-held', true),
            resetRow(t.tr('NotificationsResetKeys'), t.tr('NotificationsResetKeysHelp'), t.tr('NotificationsButtonResetKeys'), 'reset-keys', true)
        ])
    ].forEach(function (node) { content.appendChild(node); });

    // report
    // Shows a result on the section's status line.
    function report(message, isError) {
        shared.setStatus(status, message, !!isError, { timeout: 10000 });
    }

    // button
    // Finds one of the section's buttons by its action.
    function button(action) {
        return content.querySelector('[data-action="' + action + '"]');
    }

    // fail
    // Reports a failed call with the server's reason.
    function fail(key) {
        return function (err) { return shared.errorText(err, t.tr(key)).then(function (text) { report(text, true); }); };
    }

    // loadLog
    // Reads the send log and draws it.
    function loadLog() {
        return shared.request('Notifications/Log').then(function (entries) {
            var items = logItems(Array.isArray(entries) ? entries : [], shared);
            if (logList) logList.setItems(items);
            else {
                logList = nectoModule.createRecordList({ items: items, label: t.tr('NotificationsHeaderLog'), emptyText: t.tr('NotificationsEmptyLog'), maxHeight: 420 });
                logBox.appendChild(logList.element);
            }
        }).catch(fail('NotificationsErrorLoadLog'));
    }

    // loadPush
    // Reads the browsers and the held count and draws them.
    function loadPush() {
        return shared.request('Notifications/Push').then(function (push) {
            var items = browserItems(push && Array.isArray(push.Browsers) ? push.Browsers : [], shared);
            if (browserList) browserList.setItems(items);
            else {
                browserList = nectoModule.createRecordList({ items: items, label: t.tr('NotificationsHeaderBrowsers'), emptyText: t.tr('NotificationsEmptyBrowsers') });
                browserBox.appendChild(browserList.element);
            }
            heldLine.textContent = shared.plural((push && push.Held) || 0, 'NotificationsHeldOne', 'NotificationsHeldMany');
        }).catch(fail('NotificationsErrorLoadPush'));
    }

    // confirmThen
    // Asks first, then runs a call and reports its count.
    function confirmThen(message, title, confirmText, run) {
        return nectoModule.confirmDialog(message, { title: title, confirmText: confirmText, destructive: true }).then(function (ok) {
            if (ok) return run();
        });
    }

    button('test-me').addEventListener('click', function () {
        Promise.all(['browsers', 'apps'].map(function (id) {
            return shared.request('Notifications/Channels/' + id + '/Test', 'POST').catch(function () { return { Ok: false, Message: t.tr('NotificationsErrorTest') }; });
        })).then(function (results) {
            report(t.tr('NotificationsChannelBrowsers') + ': ' + results[0].Message + ' ' + t.tr('NotificationsChannelApps') + ': ' + results[1].Message, !(results[0].Ok || results[1].Ok));
            loadLog();
        });
    });
    button('refresh-log').addEventListener('click', loadLog);
    button('clear-log').addEventListener('click', function () {
        shared.request('Notifications/Log', 'DELETE').then(function () { report(t.tr('NotificationsLogCleared'), false); loadLog(); }).catch(fail('NotificationsErrorClear'));
    });
    button('clear-held').addEventListener('click', function () {
        confirmThen(t.tr('NotificationsConfirmClearHeld'), t.tr('NotificationsClearHeld'), t.tr('NotificationsButtonClear'), function () {
            return shared.request('Notifications/Held', 'DELETE').then(function (r) {
                report(shared.plural((r && r.Count) || 0, 'NotificationsHeldClearedOne', 'NotificationsHeldClearedMany'), false);
                loadPush();
            }).catch(fail('NotificationsErrorClear'));
        });
    });
    button('reset-keys').addEventListener('click', function () {
        confirmThen(t.tr('NotificationsConfirmResetKeys'), t.tr('NotificationsResetKeys'), t.tr('NotificationsButtonResetKeys'), function () {
            return shared.request('Notifications/Push/Keys', 'POST').then(function () {
                report(t.tr('NotificationsKeysReset'), false);
                loadPush();
            }).catch(fail('NotificationsErrorResetKeys'));
        });
    });

    return {
        load: function () { return Promise.all([loadLog(), loadPush()]); },
        destroy: function () {
            if (logList) { logList.destroy(); logList = null; }
            if (browserList) { browserList.destroy(); browserList = null; }
        }
    };
}

nectoModule.registerSection('settings', 'notifications', { build: buildSettings });
nectoModule.registerSection('troubleshooting', 'notifications', { build: buildTroubleshooting });
