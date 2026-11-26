// necto_invite_signup.js
// The script of the signup page an invite link opens. It reads the invite, shows the form, and makes the account.

import { startUserPage, showUserPageView, setUserPageMessage, loadTranslations, createTranslations } from './configurationpage?name=necto_shared.js';

// tokenOf
// Reads the invite's token from the page's address, the part after /invites/.
//
// Inputs
// path [string]: The page's path.
//
// Output
// [string]: The token, or empty.
export function tokenOf(path) {
    var match = /\/invites\/([^/?#]+)/i.exec(path || '');
    return match ? decodeURIComponent(match[1]) : '';
}

// safeLinks
// Keeps only the links a button may open, the absolute http and https ones.
//
// Inputs
// resources [object[]]: The links from the signup's answer.
//
// Output
// [object[]]: The links to show.
export function safeLinks(resources) {
    return (resources || []).filter(function (r) { return r && r.Url && /^https?:\/\//i.test(r.Url); });
}

// start
// Runs the page: reads the invite, then makes the account when the form is sent.
//
// Inputs
// doc [Document]: The page.
// loc [Location]: The page's address.
//
// Output
// [Promise]: Resolves once the page shows its first view.
export async function start(doc, loc) {
    var t = await loadTranslations('Necto/Translations').catch(function () { return createTranslations({}); });
    t.apply(doc.body);
    await startUserPage({ translations: 'Necto/Translations' });
    var token = tokenOf(loc.pathname);
    var base = 'invites/' + encodeURIComponent(token);
    var query = '?language=' + encodeURIComponent(t.language || 'en-us');
    var byId = function (id) { return doc.getElementById(id); };

    var info = null;
    try {
        info = await fetch(base + '/Info' + query, { headers: { Accept: 'application/json' } }).then(function (r) { return r.json(); });
    } catch (e) {
        info = null;
    }

    if (!info || !info.Valid) {
        if (info && info.Throttled && info.Error) byId('invalidMessage').textContent = info.Error;
        showUserPageView('invalid', doc);
        return;
    }

    if (info.Message) byId('welcome').textContent = info.Message;
    byId('pinField').hidden = !info.RequiresPin;
    showUserPageView('form', doc);

    byId('signupForm').addEventListener('submit', function (e) {
        e.preventDefault();
        var message = byId('formMessage');
        setUserPageMessage(message, 'info', '');
        var password = byId('password').value;
        if (password !== byId('confirm').value) {
            setUserPageMessage(message, 'error', t.tr('InvitesErrorPasswordsDiffer'));
            return;
        }

        byId('submit').disabled = true;
        fetch(base + '/Redeem' + query, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
            body: JSON.stringify({ Pin: byId('pin').value, Username: byId('username').value, Password: password })
        }).then(function (r) { return r.json(); }).then(function (answer) {
            if (answer && answer.Success) {
                byId('doneHeading').textContent = (byId('username').value || '').trim();
                byId('doneMessage').textContent = answer.Message || '';
                var links = byId('resources');
                links.textContent = '';
                safeLinks(answer.Resources).forEach(function (r) {
                    var a = doc.createElement('a');
                    a.className = 'raised stolon-button stolon-secondary stolon-large stolon-fill';
                    a.href = r.Url;
                    a.target = '_blank';
                    a.rel = 'noopener noreferrer';
                    a.textContent = r.Title || r.Url;
                    links.appendChild(a);
                });
                links.hidden = links.children.length === 0;
                showUserPageView('done', doc);
                return;
            }
            setUserPageMessage(message, 'error', (answer && answer.Message) || t.tr('InvitesErrorSignup'));
            byId('submit').disabled = false;
        }).catch(function () {
            setUserPageMessage(message, 'error', t.tr('InvitesErrorNetwork'));
            byId('submit').disabled = false;
        });
    });
}

if (typeof document !== 'undefined' && document.getElementById('signupForm')) {
    start(document, location);
}
