// necto_invites_member.js
// The script of a member's invite page at /invites. It lists the member's invites with their links, makes new ones
// within what the member's groups allow, and holds the page's browser notification switch.

import { startUserPage, showUserPageView, setUserPageMessage, loadTranslations, createTranslations, createQrCode, confirmDialog, el } from './configurationpage?name=necto_shared.js';
import { createPushToggle } from './configurationpage?name=necto_push.js';
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
    var session = await startUserPage({ translations: 'Necto/Translations' });
    if (!session) return;
    var byId = function (id) { return doc.getElementById(id); };
    var message = byId('memberMessage');
    var handles = [];
    var mine = null;

    byId('pushCard').appendChild(createPushToggle(session, t, doc).element);

    // load
    // Reads the member's invites and draws them, with the form when the groups allow invites.
    var load = function () {
        return session.request('Invites/Mine').then(function (r) { return r.json(); }).then(function (answer) {
            mine = answer;
            draw();
        }).catch(function () { setUserPageMessage(message, 'error', t.tr('InvitesErrorLoad')); });
    };

    // draw
    // Draws each invite as a card part with its state, uses, link, and Delete.
    var draw = function () {
        handles.splice(0).forEach(function (h) { h.destroy(); });
        var list = byId('memberList');
        list.textContent = '';
        if (!mine.MayCreate) {
            byId('memberLead').textContent = t.tr('InvitesMemberNotAllowed');
        }

        byId('newInvite').hidden = !mine.MayCreate;
        var select = byId('inviteGroup');
        select.textContent = '';
        (mine.Groups || []).forEach(function (g) { select.appendChild(el('option', { value: g.Id, text: g.Name })); });

        if (!(mine.Invites || []).length) {
            list.appendChild(el('p', { class: 'stolon-user-page-hint', text: t.tr('InvitesMemberEmpty') }));
        }

        (mine.Invites || []).forEach(function (invite) {
            var qr = createQrCode({ value: inviteLink(invite.Token, mine.LinkUrl, doc.baseURI), layout: 'inline', fileName: 'invite.png' });
            handles.push(qr);
            var remove = el('button', { type: 'button', class: 'raised stolon-button stolon-destructive stolon-fill', 'data-action': 'delete' }, [el('span', { text: t.tr('Delete') })]);
            remove.addEventListener('click', function () {
                confirmDialog(t.tr('InvitesConfirmDelete'), { title: t.format('DeleteName', invite.Label || t.tr('InvitesUntitled')), confirmText: t.tr('Delete'), destructive: true }).then(function (ok) {
                    if (!ok) return;
                    session.request('Invites/Mine/' + invite.Id, { method: 'DELETE' }).then(load);
                });
            });
            var uses = invite.MaxUses > 0 ? t.format('InvitesUsesOf', invite.UsedCount, invite.MaxUses) : t.format('InvitesUses', invite.UsedCount);
            list.appendChild(el('div', { class: 'stolon-user-page-form', 'data-invite': invite.Id }, [
                el('h2', { class: 'stolon-user-page-subtitle', text: invite.Label || t.tr('InvitesUntitled') }),
                el('p', { class: 'stolon-user-page-hint', text: [t.tr(STATE_KEYS[stateName(invite.State)]), invite.GroupName, uses].join(' · ') }),
                qr.element,
                el('div', { class: 'stolon-user-page-actions' }, [remove])
            ]));
        });
    };

    byId('newInviteForm').addEventListener('submit', function (e) {
        e.preventDefault();
        var request = memberRequest({ label: byId('inviteLabel').value, group: byId('inviteGroup').value, uses: byId('inviteUses').value, expiry: byId('inviteExpiry').value });
        byId('createInvite').disabled = true;
        session.request('Invites/Mine', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(request) }).then(function (r) {
            return r.json().then(function (body) {
                if (!r.ok) throw new Error((body && body.Error) || t.tr('InvitesErrorCreate'));
                setUserPageMessage(message, 'success', t.tr('InvitesCreated'));
                byId('inviteLabel').value = '';
                return load();
            });
        }).catch(function (err) {
            setUserPageMessage(message, 'error', err.message || t.tr('InvitesErrorCreate'));
        }).then(function () { byId('createInvite').disabled = false; });
    });

    showUserPageView('invites', doc);
    return load();
}

if (typeof document !== 'undefined' && document.getElementById('newInviteForm')) {
    start(document);
}
