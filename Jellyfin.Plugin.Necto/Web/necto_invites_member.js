// necto_invites_member.js
// The script of a member's invite page at /invites, in the web client's signed in shell. It lists the member's invites
// with their links, makes new ones within what the member's groups allow, and holds the page's browser notification
// switch.

import { startWebClientShell, loadTranslations, createTranslations, createQrCode, confirmDialog, createWebPushToggle, h, webSettingsCheckbox, copyToClipboard } from './configurationpage?name=necto_shared.js';
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
// Draws the browser notification switch as a web client settings checkbox, kept in step with Stolon's web push toggle,
// which does the work out of sight. The section stays hidden while the server offers no web push.
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
    var row = doc.getElementById('pushRow');
    row.innerHTML = webSettingsCheckbox('chkBrowserNotifications', t.tr('RequestsLabelNotifyInThisBrowser'), 'browserNotificationsState');
    var box = doc.getElementById('chkBrowserNotifications');
    var line = doc.getElementById('browserNotificationsState');
    // The one service worker Notifications serves, at the server's root, so a browser keeps one subscription for
    // every user page.
    var toggle = createWebPushToggle({
        keyUrl: 'Notifications/Push/Key',
        subscribeUrl: 'Notifications/Push/Subscribe',
        unsubscribeUrl: 'Notifications/Push/Unsubscribe',
        workerUrl: 'Notifications/Push/Worker.js',
        request: session.request,
        failedText: t.tr('NotificationsErrorPush'),
        onChange: function () { sync(); }
    });
    var sync = function () {
        var state = toggle.getState();
        section.hidden = state === null;
        box.checked = state === 'on';
        box.disabled = toggle.button.hidden || toggle.button.disabled;
        line.textContent = Array.prototype.filter.call(toggle.status.childNodes, function (n) {
            return !(n.classList && n.classList.contains('material-icons'));
        }).map(function (n) { return n.textContent; }).join('').trim();
    };
    if (typeof MutationObserver !== 'undefined') {
        new MutationObserver(sync).observe(toggle.element, { subtree: true, childList: true, characterData: true, attributes: true });
    }
    box.addEventListener('change', function () {
        box.checked = toggle.getState() === 'on';
        toggle.button.click();
        sync();
    });
    toggle.refresh().then(sync);
    return toggle;
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
    var session = await startWebClientShell({ translations: 'Necto/Translations' });
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
            byId('memberEmpty').textContent = t.tr('InvitesErrorLoad');
            byId('memberEmpty').hidden = false;
        });
    };

    // draw
    // Draws each invite as a row with its name, its state, group, and uses, and its Copy link, QR, and Delete buttons.
    var draw = function () {
        handles.splice(0).forEach(function (handle) { handle.destroy(); });
        var list = byId('memberList');
        list.textContent = '';
        if (!mine.MayCreate) {
            byId('memberLead').textContent = t.tr('InvitesMemberNotAllowed');
        }

        byId('newInviteForm').hidden = !mine.MayCreate;
        var select = byId('inviteGroup');
        select.textContent = '';
        (mine.Groups || []).forEach(function (g) { select.appendChild(h('option', { value: g.Id, text: g.Name })); });

        byId('memberEmpty').hidden = (mine.Invites || []).length > 0;
        list.hidden = !byId('memberEmpty').hidden;

        (mine.Invites || []).forEach(function (invite) {
            var name = invite.Label || t.tr('InvitesUntitled');
            var link = inviteLink(invite.Token, mine.LinkUrl, doc.baseURI);
            var qr = createQrCode({ value: link, layout: 'inline', label: name, fileName: 'invite.png' });
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
                iconButton('delete', t.tr('Delete'), 'delete', function () {
                    confirmDialog(t.tr('InvitesConfirmDelete'), { title: t.format('DeleteName', name), confirmText: t.tr('Delete'), destructive: true }).then(function (ok) {
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
