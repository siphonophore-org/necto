// necto_invites.js
// The Invites tool. It lists every invite link with its state, and opens each in an editor sidecar with its link and
// QR code, its group, limits, PIN, message, and links, with Turn on or Turn off, Duplicate, and Delete.

import * as nectoModule from './configurationpage?name=necto_shared.js';

var el = nectoModule.el;

// EVERYONE_ID
// The id of the Everyone group, which an invite names for no group.
var EVERYONE_ID = '5a1f0c7e-0000-4000-8000-000000000001';

// The state of each invite, by the number the server keeps it as, with its badge.
var STATES = ['Active', 'Locked', 'Expired', 'GroupExpired', 'UsedUp'];
var STATE_COLORS = { Active: 'green', Locked: 'gray', Expired: 'orange', GroupExpired: 'orange', UsedUp: 'gray' };

// STATE_KEYS
// The string key of each state's badge.
export var STATE_KEYS = { Active: 'StateActive', Locked: 'StateLocked', Expired: 'StateExpired', GroupExpired: 'StateGroupExpired', UsedUp: 'StateUsedUp' };

// Ids for the editor's fields, so each label names its own field.
var fieldSeq = 0;

// stateName
// Reads an invite's state, which the server may send as its number or its name.
//
// Inputs
// state [number|string]: The state.
//
// Output
// [string]: The state's name.
export function stateName(state) {
    return typeof state === 'number' ? (STATES[state] || 'Active') : (STATES.indexOf(state) >= 0 ? state : 'Active');
}

// inviteLink
// Builds an invite's link from the address in Settings, or the server's address the dashboard is open at.
//
// Inputs
// token [string]: The invite's token.
// linkUrl [string]: The address in Settings, or empty.
// serverAddress [string]: The server's address.
//
// Output
// [string]: The link.
export function inviteLink(token, linkUrl, serverAddress) {
    var root = String(linkUrl || serverAddress || '').replace(/\/+$/, '');
    return root + '/invites/' + encodeURIComponent(token);
}

// usesText
// Says how many accounts an invite made, out of its limit.
//
// Inputs
// invite [object]: The invite.
// t [object]: The strings.
//
// Output
// [string]: Such as "2 of 5 uses".
export function usesText(invite, t) {
    return invite.MaxUses > 0 ? t.format('InvitesUsesOf', invite.UsedCount, invite.MaxUses) : t.format('InvitesUses', invite.UsedCount);
}

// inviteItems
// Turns the invites into rows, each with its group, uses, expiry, state, and PIN, and its link in a copy field with a
// QR button.
//
// Inputs
// invites [object[]]: The invites from Invites/List.
// shared [object]: The Necto helper bag.
// linkOf [function]: Gives an invite's link, or is left out for rows without one.
//
// Output
// [object[]]: The rows, each carrying its invite.
export function inviteItems(invites, shared, linkOf) {
    var t = shared.t;
    return (invites || []).map(function (invite) {
        var state = stateName(invite.State);
        var parts = [invite.GroupName, usesText(invite, t)];
        parts.push(invite.ExpiresAt ? t.format('ExpiresOnDate', shared.formatDate(invite.ExpiresAt)) : t.tr('InvitesNeverExpires'));
        var tags = [{ label: t.tr(STATE_KEYS[state]), color: STATE_COLORS[state] }];
        if (invite.HasPin) tags.push({ label: t.tr('InvitesLabelPin'), icon: 'pin' });
        if (invite.CreatedByName) tags.push({ label: invite.CreatedByName, icon: 'person' });
        var title = invite.Label || t.tr('InvitesUntitled');
        var aside = linkOf && invite.Token
            ? nectoModule.createQrCode({ value: linkOf(invite), layout: 'inline', label: title, fileName: 'invite.png' })
            : null;
        return { id: invite.Id, title: title, secondary: parts.join(' · '), tags: tags, aside: aside, invite: invite };
    });
}

// stateCards
// Counts the invites in each state, for the cards over the list.
//
// Inputs
// invites [object[]]: The invites.
// t [object]: The strings.
//
// Output
// [object[]]: One card per state, with its count, label, and color.
export function stateCards(invites, t) {
    return STATES.map(function (state) {
        var count = (invites || []).filter(function (i) { return stateName(i.State) === state; }).length;
        return { count: count, label: t.tr(STATE_KEYS[state]), color: STATE_COLORS[state] };
    });
}

// groupExpired
// Tells whether a group's expiry date has come, so signups into it are refused.
//
// Inputs
// group [object]: The group, or null.
// now [Date]: The time now.
//
// Output
// [boolean]: Whether it has expired.
export function groupExpired(group, now) {
    var due = group && group.Permissions && group.Permissions.Accounts && group.Permissions.Accounts.Expiry && group.Permissions.Accounts.Expiry.ExpiresOn;
    return !!due && String(due).slice(0, 10) <= (now || new Date()).toISOString().slice(0, 10);
}

// inviteRequest
// Builds what the editor sends for an invite. A PIN left as stored is sent as null, so it stays.
//
// Inputs
// form [object]: label, message, group, expiry, uses, pin, pinKept, and resources as the editor holds them.
//
// Output
// [object]: The request for Invites.
export function inviteRequest(form) {
    var uses = parseInt(form.uses, 10);
    return {
        Label: String(form.label || '').trim(),
        Message: String(form.message || '').trim(),
        GroupId: form.group && form.group !== EVERYONE_ID ? form.group : (form.group === EVERYONE_ID ? EVERYONE_ID : null),
        ExpiresAt: form.expiry ? form.expiry + 'T00:00:00' : null,
        MaxUses: isNaN(uses) || uses < 0 ? 0 : uses,
        Pin: form.pinKept ? null : String(form.pin || '').trim(),
        Resources: (form.resources || []).filter(function (r) { return r && (r.title || r.url); }).map(function (r) { return { Title: String(r.title || '').trim(), Url: String(r.url || '').trim() }; })
    };
}

// groupChoices
// Lists the groups an invite may join: the group for new users, Everyone, then each group by name, without the
// groups that disallow password changes.
//
// Inputs
// groups [object[]]: The groups from Groups.
// t [object]: The strings.
//
// Output
// [object[]]: Each choice's value and label. The group for new users has an empty value.
export function groupChoices(groups, t) {
    var blocks = function (g) {
        var p = g.Permissions && g.Permissions.Accounts && g.Permissions.Accounts.Password;
        return !!(p && p.Enabled && (p.ChangeMode === 'Disallowed' || p.ChangeMode === 2));
    };
    var real = (groups || []).filter(function (g) { return g.Id !== EVERYONE_ID && !g.IsEveryone && !blocks(g); })
        .sort(function (a, b) { return String(a.Name).localeCompare(String(b.Name)); });
    return [{ value: '', label: t.tr('LabelNewUserGroup') }, { value: EVERYONE_ID, label: t.tr('GroupEveryone') }]
        .concat(real.map(function (g) { return { value: g.Id, label: g.Name }; }));
}

// createInviteEditor
// Builds the body of an invite's sidecar: its link and QR code once it exists, then its fields.
//
// Inputs
// invite [object]: The invite, or a new one.
// ctx [object]: shared is the Necto helper bag, groups every group, and link the invite's link or empty.
//
// Output
// [object]: A handle with element, getValue, validate, setStatus, and destroy.
export function createInviteEditor(invite, ctx) {
    var shared = ctx.shared;
    var t = shared.t;
    var handles = [];
    var status = el('div', { class: 'stolon-status-text', role: 'status', 'data-role': 'status' });
    var root = el('div', { 'data-role': 'invite-editor' }, [status]);
    var nextId = function (name) { fieldSeq += 1; return 'necto-invite-' + name + '-' + fieldSeq; };

    // input
    // Adds a labelled input with its description.
    var input = function (label, type, value, data, help, placeholder) {
        var id = nextId(data);
        var node = type === 'textarea'
            ? el('textarea', { is: 'emby-textarea', class: 'emby-textarea', id: id, rows: '3', 'data-field': data })
            : el('input', { is: 'emby-input', type: type, id: id, 'data-field': data, min: type === 'number' ? '0' : null, placeholder: placeholder || null });
        node.value = value == null ? '' : String(value);
        root.appendChild(el('div', { class: 'inputContainer' }, [
            el('label', { class: 'inputLabel inputLabelUnfocused', for: id, text: label }),
            node,
            help ? el('div', { class: 'fieldDescription', text: help }) : null
        ]));
        return node;
    };

    if (ctx.link) {
        root.appendChild(el('h3', { class: 'stolon-subsection-title', text: t.tr('InvitesHeaderLink') }));
        var qr = nectoModule.createQrCode({ value: ctx.link, layout: 'inline', fileName: 'invite.png' });
        handles.push(qr);
        root.appendChild(el('div', { 'data-role': 'link' }, [qr.element]));
        root.appendChild(el('h3', { class: 'stolon-subsection-title', text: t.tr('InvitesHeaderSettings') }));
    }

    var label = input(t.tr('LabelName'), 'text', invite.Label, 'Label', t.tr('InvitesNameAdminHelp'));
    var message = input(t.tr('InvitesLabelMessage'), 'textarea', invite.Message, 'Message', t.tr('InvitesMessageHelp'));

    var groupId = nextId('group');
    var group = el('select', { is: 'emby-select', id: groupId, 'data-field': 'GroupId' });
    groupChoices(ctx.groups, t).forEach(function (o) { group.appendChild(el('option', { value: o.value, text: o.label })); });
    group.value = invite.GroupId || '';
    var expiredWarn = el('div', { class: 'stolon-warn', 'data-role': 'group-expired', text: t.tr('InvitesGroupExpiredWarning') });
    root.appendChild(el('div', { class: 'selectContainer' }, [
        el('label', { class: 'selectLabel', for: groupId, text: t.tr('LabelGroup') }),
        group,
        el('div', { class: 'fieldDescription', text: t.tr('InvitesGroupHelp') })
    ]));
    root.appendChild(expiredWarn);
    // checkGroup
    // Shows the warning while the picked group, or the group for new users, has expired.
    var checkGroup = function () {
        var id = group.value || ctx.newUserGroupId;
        var picked = (ctx.groups || []).find(function (g) { return g.Id === id; });
        expiredWarn.classList.toggle('stolon-hidden', !groupExpired(picked));
    };
    group.addEventListener('change', checkGroup);
    checkGroup();

    var uses = input(t.tr('InvitesLabelMaxUses'), 'number', invite.MaxUses == null ? 1 : invite.MaxUses, 'MaxUses', t.tr('HelpZeroNoLimit'));
    var expiry = input(t.tr('LabelExpiryDate'), 'date', invite.ExpiresAt ? String(invite.ExpiresAt).slice(0, 10) : '', 'ExpiresAt', t.tr('InvitesExpiryAdminHelp'));
    var pin = input(t.tr('InvitesLabelPin'), 'text', '', 'Pin', t.tr('InvitesPinHelp'), '123456');
    pin.setAttribute('inputmode', 'numeric');
    pin.setAttribute('maxlength', '6');
    if (invite.HasPin) {
        var clearPin = el('button', { is: 'emby-button', type: 'button', class: 'raised stolon-button stolon-secondary stolon-small', 'data-action': 'clear-pin' }, [el('span', { text: t.tr('InvitesButtonRemovePin') })]);
        pin.parentNode.appendChild(clearPin);
        nectoModule.bindSecretField(pin, true, { clearButton: clearPin });
    }

    root.appendChild(el('h3', { class: 'stolon-subsection-title', text: t.tr('InvitesHeaderResources') }));
    root.appendChild(el('div', { class: 'fieldDescription', text: t.tr('InvitesResourcesHelp') }));
    var resources = nectoModule.createRepeater({
        fields: [
            { key: 'title', label: t.tr('InvitesLabelResourceTitle'), type: 'text', placeholder: t.tr('InvitesResourceTitlePlaceholder') },
            { key: 'url', label: t.tr('LabelAddress'), type: 'text', placeholder: 'https://example.com/guide' }
        ],
        value: (invite.Resources || []).map(function (r) { return { title: r.Title, url: r.Url }; }),
        addText: t.tr('InvitesButtonAddResource'),
        emptyText: t.tr('InvitesEmptyResources')
    });
    handles.push(resources);
    root.appendChild(resources.element);

    return {
        element: root,
        getValue: function () {
            var raw = invite.HasPin ? nectoModule.readSecretField(pin) : pin.value;
            var kept = raw === nectoModule.SECRET_KEPT;
            return inviteRequest({ label: label.value, message: message.value, group: group.value, expiry: expiry.value, uses: uses.value, pin: kept ? '' : raw, pinKept: kept, resources: resources.getValue() });
        },
        validate: function () {
            var value = this.getValue();
            if (value.Pin && !/^\d{6}$/.test(value.Pin)) return 'InvitesErrorPin';
            if (value.Resources.some(function (r) { return !r.Title || !/^https?:\/\//i.test(r.Url); })) return 'InvitesErrorResource';
            return null;
        },
        setStatus: function (text, isError) { shared.setStatus(status, text, !!isError); },
        destroy: function () { handles.splice(0).forEach(function (h) { if (h && h.destroy) h.destroy(); }); }
    };
}

export default function (view) {
    'use strict';

    var shared = null;
    var page = null;
    var list = null;
    var invites = [];
    var groups = [];
    var linkUrl = '';
    var newUserGroupId = '';
    var loadSeq = 0;

    // report
    // Shows a result on the page's status line.
    function report(message, isError) {
        shared.setStatus('invitesStatus', message, !!isError, { timeout: 10000 });
    }

    // serverAddress
    // Gives the server's address as the dashboard reaches it.
    function serverAddress() {
        try { return ApiClient.serverAddress(); } catch (e) { return ''; }
    }

    // loadInvites
    // Reads the invites, the groups, and the link address, then draws the list.
    function loadInvites() {
        var mine = ++loadSeq;
        return Promise.all([
            shared.request('Invites/List'),
            shared.request('Groups').catch(function () { return []; }),
            shared.getConfig().catch(function () { return {}; })
        ]).then(function (answers) {
            if (mine !== loadSeq) return;
            invites = Array.isArray(answers[0]) ? answers[0] : [];
            groups = Array.isArray(answers[1]) ? answers[1] : [];
            linkUrl = (answers[2] && answers[2].InviteSettings && answers[2].InviteSettings.LinkUrl) || '';
            newUserGroupId = (answers[2] && answers[2].GroupSettings && answers[2].GroupSettings.NewUserGroupId) || '';
            if (view.querySelector('#inviteCards')) shared.renderCards(view.querySelector('#inviteCards'), stateCards(invites, shared.t));
            var items = inviteItems(invites, shared, function (invite) { return inviteLink(invite.Token, linkUrl, serverAddress()); });
            if (list) list.setItems(items);
            else {
                list = nectoModule.createRecordList({ items: items, label: shared.t.tr('TabInvites'), emptyText: shared.t.tr('InvitesEmpty'), onOpen: function (item) { openInvite(item.invite); } });
                view.querySelector('#inviteList').appendChild(list.element);
            }
        }).catch(function () {
            if (mine === loadSeq) report(shared.t.tr('InvitesErrorLoad'), true);
        });
    }

    // fail
    // Shows a failed call's reason in the editor.
    function fail(editor, key) {
        return function (err) {
            return shared.errorText(err, shared.t.tr(key)).then(function (text) { editor.setStatus(text, true); return false; });
        };
    }

    // openInvite
    // Opens an invite, or a new one, in the editor sidecar.
    function openInvite(invite) {
        var t = shared.t;
        var isNew = !invite;
        var editor = createInviteEditor(invite || { MaxUses: 1 }, { shared: shared, groups: groups, newUserGroupId: newUserGroupId, link: isNew ? '' : inviteLink(invite.Token, linkUrl, serverAddress()) });
        var actions = isNew ? [] : [
            { text: t.tr(invite.Enabled ? 'ButtonTurnOff' : 'ButtonTurnOn'), onClick: function (handle) {
                shared.request('Invites/' + invite.Id + '/Enabled', 'POST', { Enabled: !invite.Enabled }).then(function () {
                    handle.close('saved');
                    report(t.tr(invite.Enabled ? 'InvitesTurnedOff' : 'InvitesTurnedOn'), false);
                    loadInvites();
                }).catch(fail(editor, 'InvitesErrorSave'));
            } },
            { text: t.tr('ButtonDuplicate'), onClick: function (handle) {
                shared.request('Invites/' + invite.Id + '/Duplicate', 'POST').then(function () {
                    handle.close('saved');
                    report(t.tr('InvitesDuplicated'), false);
                    loadInvites();
                }).catch(fail(editor, 'InvitesErrorSave'));
            } }
        ];
        nectoModule.openEditorSidecar({
            title: isNew ? t.tr('InvitesMemberNew') : (invite.Label || t.tr('InvitesUntitled')),
            body: editor.element,
            page: page,
            isNew: isNew,
            serialize: function () { return JSON.stringify(editor.getValue()); },
            saveText: t.tr('Save'),
            deleteText: t.tr('Delete'),
            actions: actions,
            onSave: function () {
                var problem = editor.validate();
                if (problem) { editor.setStatus(t.tr(problem), true); return Promise.resolve(false); }
                var request = isNew ? shared.request('Invites', 'POST', editor.getValue()) : shared.request('Invites/' + invite.Id, 'PUT', editor.getValue());
                return request.then(function () {
                    report(t.tr(isNew ? 'InvitesCreated' : 'InvitesSaved'), false);
                    loadInvites();
                    return true;
                }).catch(fail(editor, 'InvitesErrorSave'));
            },
            onDelete: function (close) {
                nectoModule.confirmDialog(t.tr('InvitesConfirmDelete'), { title: t.format('DeleteName', invite.Label || t.tr('InvitesUntitled')), confirmText: t.tr('Delete'), destructive: true }).then(function (ok) {
                    if (!ok) return;
                    shared.request('Invites/' + invite.Id, 'DELETE').then(function () {
                        close('deleted');
                        report(t.tr('InvitesDeleted'), false);
                        loadInvites();
                    }).catch(fail(editor, 'InvitesErrorDelete'));
                });
            },
            onClose: function () { editor.destroy(); }
        });
    }

    var made = nectoModule.createNectoPage(view, {
        page: 'necto_invites',
        bind: function () {
            var add = view.querySelector('#btnAddInvite');
            if (add) add.addEventListener('click', function () { openInvite(null); });
        },
        load: function () { loadInvites(); }
    });
    shared = made.shared;
    page = made.page;
}
