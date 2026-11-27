// necto_invites_member.js
// The script of a member's invite page at /invites, in the web client's signed in shell. It lists the member's invites
// with their links, makes new ones within what the member's groups allow, and holds the page's browser notification
// switch.

import { startWebClientShell, loadTranslations, createTranslations, createQrCode, webConfirm, createWebPushToggle, webEmptyState, h, copyToClipboard } from './configurationpage?name=necto_shared.js';
import { STATE_KEYS, stateName } from './configurationpage?name=necto_invites.js';

// inviteLink
// Builds an invite's link from the address in Settings, or the server's root the page is open at.
//
// Inputs
// token [string]: The invite's token.
// linkUrl [string]: The address in Settings, or empty.
// baseHref [string]: The page's base address, the server's root.
//
// Output
// [string]: The link.
export function inviteLink(token, linkUrl, baseHref) {
    var root = (linkUrl || String(baseHref || '').replace(/\/+$/, '')).replace(/\/+$/, '');
    return root + '/invites/' + encodeURIComponent(token);
}

// memberRequest
// Builds what the new invite form sends.
//
// Inputs
// form [object]: label, group, uses, and expiry as the form holds them.
//
// Output
// [object]: The request for Invites/Mine.
export function memberRequest(form) {
    var uses = parseInt(form.uses, 10);
    return {
        Label: String(form.label || '').trim(),
        GroupId: form.group || null,
        MaxUses: isNaN(uses) || uses < 0 ? 0 : uses,
        ExpiresAt: form.expiry ? form.expiry + 'T00:00:00' : null,
        Resources: []
    };
}

// inviteLine
// Writes the second line of an invite's row: its state, its group, and its uses.
//
// Inputs
// invite [object]: The invite, as Invites/Mine gives it.
// t [object]: The page's translations.
//
// Output
// [string]: The line.
export function inviteLine(invite, t) {
    var uses = invite.MaxUses > 0 ? t.format('InvitesUsesOf', invite.UsedCount, invite.MaxUses) : t.format('InvitesUses', invite.UsedCount);
    return [t.tr(STATE_KEYS[stateName(invite.State)]), invite.GroupName, uses].filter(Boolean).join(' · ');
}

// iconButton
// Builds one of a row's icon buttons, as the web client's list rows end with.
//
// Inputs
// icon [string]: The Material icon.
// title [string]: The button's title and accessible name.
// action [string]: The data-action the tests and the page find it by.
// onClick [function]: What it does.
//
// Output
// [HTMLButtonElement]: The button.
function iconButton(icon, title, action, onClick) {
    var button = h('button', { type: 'button', is: 'paper-icon-button-light', class: 'listItemButton paper-icon-button-light', title: title, 'aria-label': title, 'data-action': action }, [
        h('span', { class: 'material-icons', 'aria-hidden': 'true', text: icon })
    ]);
    button.addEventListener('click', onClick);
    return button;
}

// bindPushRow
// Draws the browser notification switch as a checkbox row of the web client's settings pages. The section stays
// hidden while the server offers no web push, and on a page that is not HTTPS.
//
// Inputs
// doc [Document]: The page.
// session [object]: The viewer's session from startWebClientShell.
// t [object]: The page's translations.
//
// Output
// [object]: The toggle's handle.
export function bindPushRow(doc, session, t) {
    var section = doc.getElementById('pushSection');
    // The one service worker Notifications serves, at the server's root, so a browser keeps one subscription for
    // every user page.
    var toggle = createWebPushToggle({
        look: 'settings',
        id: 'chkBrowserNotifications',
        label: t.tr('RequestsLabelNotifyInThisBrowser'),
        keyUrl: 'Notifications/Push/Key',
        subscribeUrl: 'Notifications/Push/Subscribe',
        unsubscribeUrl: 'Notifications/Push/Unsubscribe',
        workerUrl: 'Notifications/Push/Worker.js',
        request: session.request,
        failedText: t.tr('NotificationsErrorPush'),
        onChange: function () { sync(); }
    });
    // A browser offers notifications only over HTTPS, so the row shows only there.
    var secure = typeof globalThis.isSecureContext !== 'boolean' || globalThis.isSecureContext;
    var sync = function () { section.hidden = !secure || toggle.getState() === null; };
    doc.getElementById('pushRow').appendChild(toggle.element);
    toggle.refresh().then(sync, sync);
    return toggle;
}

// SIGNED_OUT_FRAME
// The classes of the web client shell's signed out frame, the web client login page's, as Stolon's frame writes them.
var SIGNED_OUT_FRAME = { body: ['stolon-shell-signed-out'], column: ['standalonePage', 'padded-left', 'padded-right', 'padded-bottom-page'], form: ['readOnlyContent', 'margin-auto-x'] };

// followSignInFrame
// Shows the shell's sign in screen in the web client login page's frame, its header with the logo alone and its form's
// column, and the signed in shell again once the viewer is in. Stolon draws a User page's sign in in the signed in frame.
//
// Inputs
// doc [Document]: The page, already started with startWebClientShell.
//
// Output
// [function]: Syncs the frame with the page now, which the observer calls on every change of the gate.
export function followSignInFrame(doc) {
    var gate = doc.querySelector('.stolon-shell-gate');
    var column = doc.querySelector('.stolon-shell-column');
    var form = doc.querySelector('.stolon-shell-form');
    var sync = function () {
        var signingIn = !!gate && !gate.hidden && !!gate.querySelector('.stolon-sign-in');
        SIGNED_OUT_FRAME.body.forEach(function (c) { doc.body.classList.toggle(c, signingIn); });
        SIGNED_OUT_FRAME.column.forEach(function (c) { if (column) column.classList.toggle(c, signingIn); });
        SIGNED_OUT_FRAME.form.forEach(function (c) { if (form) form.classList.toggle(c, signingIn); });
    };
    if (gate && typeof MutationObserver !== 'undefined') {
        new MutationObserver(sync).observe(gate, { childList: true, attributes: true, attributeFilter: ['hidden'] });
    }
    sync();
    return sync;
}

// start
// Runs the page once the viewer is signed in.
//
// Inputs
// doc [Document]: The page.
//
// Output
// [Promise]: Resolves once the invites are drawn.
export async function start(doc) {
    var t = await loadTranslations('Necto/Translations').catch(function () { return createTranslations({}); });
    t.apply(doc.body);
    var started = startWebClientShell({ translations: 'Necto/Translations' });
    followSignInFrame(doc);
    var session = await started;
    if (!session) return;
    var byId = function (id) { return doc.getElementById(id); };
    var toast = function (text) { session.toast.show(text); };
    var handles = [];
    var mine = null;

    bindPushRow(doc, session, t);

    // load
    // Reads the member's invites and draws them, with the form when the groups allow invites.
    var load = function () {
        return session.request('Invites/Mine').then(function (r) {
            if (!r.ok) throw new Error('HTTP ' + r.status);
            return r.json();
        }).then(function (answer) {
            mine = answer;
            draw();
        }).catch(function () {
            byId('memberList').hidden = true;
            byId('memberEmpty').innerHTML = webEmptyState(t.tr('InvitesErrorLoad'), '');
            byId('memberEmpty').hidden = false;
        });
    };

    // draw
    // Draws each invite as a row with its name, its state, group, and uses, and its Copy link, QR, and Remove buttons.
    var draw = function () {
        handles.splice(0).forEach(function (handle) { handle.destroy(); });
        var list = byId('memberList');
        list.textContent = '';
        var invites = mine.Invites || [];
        if (!mine.MayCreate) {
            byId('memberLead').textContent = t.tr(invites.length ? 'InvitesMemberNotAllowed' : 'InvitesErrorNotAllowed');
        }

        byId('newInviteForm').hidden = !mine.MayCreate;
        var select = byId('inviteGroup');
        select.textContent = '';
        (mine.Groups || []).forEach(function (g) { select.appendChild(h('option', { value: g.Id, text: g.Name })); });

        // With no invites the lead already says the groups allow none, so the empty state shows only for a member who may make one.
        byId('memberEmpty').innerHTML = webEmptyState(t.tr('InvitesEmpty'), '');
        byId('memberEmpty').hidden = invites.length > 0 || !mine.MayCreate;
        list.hidden = invites.length === 0;

        invites.forEach(function (invite) {
            var name = invite.Label || t.tr('InvitesUntitled');
            var link = inviteLink(invite.Token, mine.LinkUrl, doc.baseURI);
            // The code alone, opened from the row's own QR button, which matches the row's other web client buttons.
            var qr = createQrCode({ value: link, layout: 'inline', showValue: false, label: name, fileName: 'invite.png' });
            handles.push(qr);
            list.appendChild(h('div', { class: 'listItem listItem-border', 'data-invite': invite.Id }, [
                h('span', { class: 'material-icons listItemIcon listItemIcon-transparent', 'aria-hidden': 'true', text: 'mail' }),
                h('div', { class: 'listItemBody two-line' }, [
                    h('h3', { class: 'listItemBodyText', text: name }),
                    h('div', { class: 'listItemBodyText secondary', text: inviteLine(invite, t) })
                ]),
                iconButton('content_copy', t.tr('InvitesButtonCopyLink'), 'copy', function () {
                    copyToClipboard(link).then(function (ok) { toast(t.tr(ok ? 'InvitesLinkCopied' : 'InvitesErrorCopy')); });
                }),
                iconButton('qr_code_2', t.tr('InvitesButtonShowQr'), 'qr', function () { qr.open(); }),
                iconButton('delete', t.tr('ButtonRemove'), 'remove', function () {
                    webConfirm(t.tr('InvitesConfirmDelete'), { title: t.format('TitleRemoveName', name), confirmText: t.tr('ButtonRemove'), cancelText: t.tr('ButtonCancel'), destructive: true }).then(function (ok) {
                        if (!ok) return;
                        session.request('Invites/Mine/' + invite.Id, { method: 'DELETE' }).then(function (r) {
                            toast(t.tr(r.ok ? 'InvitesDeleted' : 'InvitesErrorDelete'));
                            return load();
                        }, function () { toast(t.tr('InvitesErrorDelete')); });
                    });
                })
            ]));
        });
    };

    byId('newInviteForm').addEventListener('submit', function (e) {
        e.preventDefault();
        var request = memberRequest({ label: byId('inviteLabel').value, group: byId('inviteGroup').value, uses: byId('inviteUses').value, expiry: byId('inviteExpiry').value });
        byId('createInvite').disabled = true;
        session.request('Invites/Mine', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(request) }).then(function (r) {
            return r.json().catch(function () { return null; }).then(function (answer) {
                if (!r.ok) throw new Error((answer && answer.Error) || t.tr('InvitesErrorCreate'));
                toast(t.tr('InvitesCreated'));
                byId('inviteLabel').value = '';
                return load();
            });
        }).catch(function (err) {
            toast(err.message || t.tr('InvitesErrorCreate'));
        }).then(function () { byId('createInvite').disabled = false; });
    });

    return load();
}

if (typeof document !== 'undefined' && document.getElementById('newInviteForm')) {
    start(document);
}
