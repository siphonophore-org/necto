// necto_invites.js
// The Invites tool. It counts the invites by state and lists them with search and a state filter, each row with its
// state, Copy link, and its QR code, and enables, disables, or removes the rows picked. Each invite opens in an editor
// sidecar with its link, its group, limits, PIN, message, and links, with Enable or Disable, Duplicate, and Remove.

import * as nectoModule from './configurationpage?name=necto_shared.js';

var h = nectoModule.h;

// EVERYONE_ID
// The id of the Everyone group, which an invite names for no group.
var EVERYONE_ID = '5a1f0c7e-0000-4000-8000-000000000001';

// The state of each invite, by the number the server keeps it as, with its badge. Expired covers an invite past its
// own expiry date and one whose group has expired. Each state has its own color, from Stolon's badge states where it
// has one, so the count cards and the Dashboard's bar tell them apart.
var STATES = ['Active', 'Locked', 'Expired', 'UsedUp', 'Disabled'];
var STATE_ORDER = ['Active', 'Locked', 'Disabled', 'Expired', 'UsedUp'];
var BADGE_COLORS = nectoModule.BADGE_STATES.stateColors;
var STATE_COLORS = { Active: BADGE_COLORS.Active, Locked: BADGE_COLORS.Locked, Expired: BADGE_COLORS.Expired, UsedUp: 'gray', Disabled: BADGE_COLORS.Disabled };

// STATE_KEYS
// The string key of each state's badge.
export var STATE_KEYS = { Active: 'StateActive', Locked: 'StateLocked', Expired: 'StateExpired', UsedUp: 'StateUsedUp', Disabled: 'Disabled' };

// Ids for the editor's fields, so each label names its own field.
var fieldSeq = 0;

// stateName
// Reads an invite's state, which the server may send as its number or its name. GroupExpired reads as Expired.
//
// Inputs
// state [number|string]: The state.
//
// Output
// [string]: The state's name.
export function stateName(state) {
    if (state === 'GroupExpired') return 'Expired';
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

// PAGE_SIZE
// The rows a page of the list holds.
export var PAGE_SIZE = 50;

// expiryDay
// Writes an invite's expiry as its day alone, in the viewer's locale, since an invite stops at the start of its day.
//
// Inputs
// iso [string]: The expiry, such as 2026-12-31T00:00:00Z.
//
// Output
// [string]: The day, such as 12/31/2026.
export function expiryDay(iso) {
    var day = String(iso || '').slice(0, 10);
    var d = new Date(day + 'T00:00:00');
    return isNaN(d) ? day : d.toLocaleDateString();
}

// inviteLine
// Writes the line under an invite's link: its group, whether the group has expired, uses, expiry, PIN, and maker.
//
// Inputs
// invite [object]: The invite.
// shared [object]: The Necto helper bag.
//
// Output
// [string]: The line, such as "Family · 2 of 5 uses · Never expires".
export function inviteLine(invite, shared) {
    var t = shared.t;
    return [
        invite.GroupName,
        invite.GroupExpired ? t.tr('InvitesValueGroupExpired') : '',
        usesText(invite, t),
        invite.ExpiresAt ? t.format('ExpiresOnDate', expiryDay(invite.ExpiresAt)) : t.tr('InvitesNeverExpires'),
        invite.HasPin ? t.tr('InvitesValueHasPin') : '',
        invite.CreatedByName ? t.format('InvitesValueMadeBy', invite.CreatedByName) : ''
    ].filter(Boolean).join(' · ');
}

// stateFilters
// Lists the state filter's choices, in the words and order of the count cards.
//
// Inputs
// t [object]: The strings.
//
// Output
// [object[]]: Each choice's value and label.
export function stateFilters(t) {
    return STATE_ORDER.map(function (state) { return { value: state, label: t.tr(STATE_KEYS[state]) }; });
}

// canChange
// Tells whether a bulk change applies to an invite, so Enable skips the invites already enabled and Disable the ones
// already disabled.
//
// Inputs
// invite [object]: The invite.
// verb [string]: enable, disable, or remove.
//
// Output
// [boolean]: Whether the invite takes it.
export function canChange(invite, verb) {
    if (verb === 'enable') return !invite.Enabled;
    if (verb === 'disable') return !!invite.Enabled;
    return true;
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
    return STATE_ORDER.map(function (state) {
        var count = (invites || []).filter(function (i) { return stateName(i.State) === state; }).length;
        return { count: count, label: t.tr(STATE_KEYS[state]), color: STATE_COLORS[state] };
    });
}

// groupExpired
// Tells whether a group's expiry date has come, which expires the invites into it.
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
    var status = h('div', { class: 'stolon-status-text', role: 'status', 'data-role': 'status' });
    var root = h('div', { class: 'stolon-stack', 'data-role': 'invite-editor' }, [status]);
    var nextId = function (name) { fieldSeq += 1; return 'necto-invite-' + name + '-' + fieldSeq; };
    var target = root;

    // step
    // Adds one step of the editor, led by its icon tile, and sends the fields after it into the step.
    var step = function (title, icon) {
        var content = h('div', { class: 'stolon-stack' });
        var section = nectoModule.createCollapsibleSection({ title: title, icon: icon, collapsible: false, content: content });
        handles.push(section);
        root.appendChild(section.element);
        target = content;
        return content;
    };

    // input
    // Adds a labelled input with its description.
    var input = function (label, type, value, data, help, placeholder) {
        var id = nextId(data);
        var node = type === 'textarea'
            ? h('textarea', { is: 'emby-textarea', class: 'emby-textarea', id: id, rows: '3', 'data-field': data })
            : h('input', { is: 'emby-input', type: type, id: id, 'data-field': data, min: type === 'number' ? '0' : null, placeholder: placeholder || null });
        node.value = value == null ? '' : String(value);
        target.appendChild(h('div', { class: 'inputContainer' }, [
            h('label', { class: 'inputLabel inputLabelUnfocused', for: id, text: label }),
            node,
            help ? h('div', { class: 'fieldDescription', text: help }) : null
        ]));
        return node;
    };

    if (ctx.link) {
        var qr = nectoModule.createQrCode({ value: ctx.link, layout: 'inline', fileName: 'invite.png' });
        handles.push(qr);
        step(t.tr('InvitesHeaderLink'), 'link').appendChild(h('div', { 'data-role': 'link' }, [qr.element]));
    }
    step(t.tr('InvitesHeaderSettings'), 'mail');

    var label = input(t.tr('LabelName'), 'text', invite.Label, 'Label', t.tr('InvitesNameAdminHelp'));
    var message = input(t.tr('InvitesLabelMessage'), 'textarea', invite.Message, 'Message', t.tr('InvitesMessageHelp'));

    var groupId = nextId('group');
    var group = h('select', { is: 'emby-select', id: groupId, 'data-field': 'GroupId' });
    groupChoices(ctx.groups, t).forEach(function (o) { group.appendChild(h('option', { value: o.value, text: o.label })); });
    group.value = invite.GroupId || '';
    var expiredWarn = h('div', { class: 'stolon-warn', 'data-role': 'group-expired', text: t.tr('InvitesGroupExpiredWarning') });
    target.appendChild(h('div', { class: 'selectContainer' }, [
        h('label', { class: 'selectLabel', for: groupId, text: t.tr('LabelGroup') }),
        group,
        h('div', { class: 'fieldDescription', text: t.tr('InvitesGroupHelp') })
    ]));
    target.appendChild(expiredWarn);
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
    var pinField = nectoModule.createSecretField({
        label: t.tr('InvitesLabelPin'), help: t.tr('InvitesPinHelp'), id: nextId('Pin'), field: 'Pin',
        stored: !!invite.HasPin, clearText: t.tr('InvitesButtonRemovePin'), allowExpose: true
    });
    handles.push(pinField);
    var pin = pinField.input;
    pin.setAttribute('inputmode', 'numeric');
    pin.setAttribute('maxlength', '6');
    target.appendChild(pinField.element);

    step(t.tr('InvitesHeaderResources'), 'menu_book').appendChild(h('div', { class: 'fieldDescription', text: t.tr('InvitesResourcesHelp') }));
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
    target.appendChild(resources.element);

    return {
        element: root,
        getValue: function () {
            var raw = pinField.getValue();
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
    var table = null;
    var qr = null;
    var groups = [];
    var linkUrl = '';
    var newUserGroupId = '';
    var loadSeq = 0;

    // report
    // Shows a result on the page's status line.
    function report(message, isError) {
        shared.setStatus('invitesStatus', message, !!isError, isError ? { timeout: 0 } : { timeout: 10000 });
    }

    // serverAddress
    // Gives the server's address as the dashboard reaches it.
    function serverAddress() {
        try { return ApiClient.serverAddress(); } catch (e) { return ''; }
    }

    // linkOf
    // Gives an invite's link.
    function linkOf(invite) {
        return inviteLink(invite.Token, linkUrl, serverAddress());
    }

    // loadCounts
    // Reads the invites, the groups, and the link address, then draws the count cards.
    function loadCounts() {
        var mine = ++loadSeq;
        return Promise.all([
            shared.request('Invites/List'),
            shared.request('Groups').catch(function () { return []; }),
            shared.getConfig().catch(function () { return {}; })
        ]).then(function (answers) {
            if (mine !== loadSeq) return;
            groups = Array.isArray(answers[1]) ? answers[1] : [];
            linkUrl = (answers[2] && answers[2].InviteSettings && answers[2].InviteSettings.LinkUrl) || '';
            newUserGroupId = (answers[2] && answers[2].GroupSettings && answers[2].GroupSettings.NewUserGroupId) || '';
            shared.renderCards('inviteCards', stateCards(Array.isArray(answers[0]) ? answers[0] : [], shared.t));
        }).catch(function () {
            if (mine === loadSeq) report(shared.t.tr('InvitesErrorLoad'), true);
        });
    }

    // stateBadge
    // Gives an invite's state badge.
    function stateBadge(invite) {
        var state = stateName(invite.State);
        return { label: shared.t.tr(STATE_KEYS[state]), color: STATE_COLORS[state] };
    }

    // createTable
    // Builds the list with its search, its state filter, Add invite, and its bulk actions.
    function createTable() {
        var t = shared.t;
        table = nectoModule.createToolTable(view, shared, {
            containerId: 'invitesTable',
            endpoint: 'Invites/All',
            search: t.tr('InvitesSearchPlaceholder'),
            filters: [{ label: t.tr('HeaderStatus'), options: stateFilters(t) }],
            pageSize: PAGE_SIZE,
            empty: { message: t.tr('InvitesEmpty'), filteredMessage: t.tr('InvitesEmptyMatch') },
            title: function (invite) { return invite.Label || t.tr('InvitesUntitled'); },
            icon: function () { return 'mail'; },
            lines: function (invite) { return [inviteLine(invite, shared)]; },
            status: stateBadge,
            buttons: function (invite) {
                return invite.Token ? [{ action: 'copy-link', icon: 'content_copy', label: t.tr('InvitesButtonCopyLink') }, { action: 'show-qr', icon: 'qr_code_2', label: t.tr('InvitesButtonShowQr') }] : [];
            },
            onButton: rowAction,
            add: [{ id: 'btnAddInvite', icon: 'add', text: t.tr('InvitesButtonAdd'), onClick: function () { openInvite(null); } }],
            bulk: [{ verb: 'enable', text: t.tr('EnablePlugin'), variant: 'primary' }, { verb: 'disable', text: t.tr('DisablePlugin') }, { verb: 'remove', text: t.tr('ButtonRemove'), variant: 'destructive' }],
            onBulk: function (verb) { bulk(verb); },
            onRowClick: function (invite) { openInvite(invite); },
            onReload: function () { loadCounts(); }
        });
    }

    // rowAction
    // Copies an invite's link or opens its QR code, for the buttons at the end of its row.
    function rowAction(action, invite) {
        var t = shared.t;
        if (action === 'copy-link') {
            shared.copyToClipboard(linkOf(invite)).then(function (ok) { report(t.tr(ok ? 'InvitesLinkCopied' : 'InvitesErrorCopyLink'), !ok); });
        } else if (action === 'show-qr') {
            showQr(invite);
        }
    }

    // showQr
    // Opens an invite's QR code in a sidecar.
    function showQr(invite) {
        if (qr) qr.destroy();
        qr = nectoModule.createQrCode({ value: linkOf(invite), layout: 'inline', showValue: false, label: invite.Label || shared.t.tr('InvitesUntitled'), fileName: 'invite.png' });
        return qr.open();
    }

    // destroyTable
    // Takes the list and any open QR code down when the page hides.
    function destroyTable() {
        if (qr) { qr.destroy(); qr = null; }
        if (table) { table.destroy(); table = null; }
    }

    // loadInvites
    // Reads the counts and the list again from the top, building the list the first time.
    function loadInvites() {
        return loadCounts().then(function () {
            if (!table) { createTable(); return table.load(); }
            return table.reload();
        }).catch(function () { report(shared.t.tr('InvitesErrorLoad'), true); });
    }

    // bulk
    // Enables, disables, or removes every row picked that takes it, asking first before a removal, and says how
    // many it changed.
    function bulk(verb) {
        var t = shared.t;
        var picked = table ? table.getSelectedItems() : [];
        return nectoModule.runBulk(shared, {
            verb: verb,
            rows: picked,
            prefix: 'InvitesBulk',
            fits: function (invite) { return canChange(invite, verb); },
            confirm: verb === 'remove' ? { text: t.tr('InvitesConfirmRemoveMany'), title: t.plural(picked.length, 'InvitesHeaderRemoveOne', 'InvitesHeaderRemoveMany') } : null,
            call: function (invite) {
                return verb === 'remove'
                    ? shared.request('Invites/' + invite.Id, 'DELETE')
                    : shared.request('Invites/' + invite.Id + '/Enabled', 'POST', { Enabled: verb === 'enable' });
            },
            report: report
        }).then(function (done) { if (done !== null) return loadInvites(); });
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
        var editor = createInviteEditor(invite || { MaxUses: 1 }, { shared: shared, groups: groups, newUserGroupId: newUserGroupId, link: isNew ? '' : linkOf(invite) });
        var actions = isNew ? [] : [
            { text: t.tr(invite.Enabled ? 'DisablePlugin' : 'EnablePlugin'), onClick: function (handle) {
                shared.request('Invites/' + invite.Id + '/Enabled', 'POST', { Enabled: !invite.Enabled }).then(function () {
                    handle.close('saved');
                    report(t.tr(invite.Enabled ? 'InvitesDisabled' : 'InvitesEnabled'), false);
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
            badge: isNew ? null : stateBadge(invite),
            body: editor.element,
            page: page,
            isNew: isNew,
            serialize: function () { return JSON.stringify(editor.getValue()); },
            saveText: t.tr('Save'),
            deleteText: t.tr('ButtonRemove'),
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
                nectoModule.confirmDialog(t.tr('InvitesConfirmDelete'), { title: t.format('TitleRemoveName', invite.Label || t.tr('InvitesUntitled')), confirmText: t.tr('ButtonRemove'), destructive: true }).then(function (ok) {
                    if (!ok) return;
                    shared.request('Invites/' + invite.Id, 'DELETE').then(function () {
                        close('deleted');
                        report(t.tr('InvitesRemoved'), false);
                        loadInvites();
                    }).catch(fail(editor, 'InvitesErrorRemove'));
                });
            },
            onClose: function () { editor.destroy(); }
        });
    }

    var made = nectoModule.createNectoPage(view, {
        page: 'necto_invites',
        load: function () {
            page.addCleanup(destroyTable);
            return loadInvites();
        }
    });
    shared = made.shared;
    page = made.page;

    return { reload: function () { return loadInvites(); }, bulk: function (verb) { return bulk(verb); }, showQr: function (invite) { return showQr(invite); } };
}
