// necto_invite_signup.js
// The script of the sign-up page an invite link opens. It reads the invite, shows the form, and makes the account.

import { startWebClientShell, showUserPageView, loadTranslations, createTranslations, WEB_SPINNER } from './configurationpage?name=necto_shared.js';

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
// resources [object[]]: The links from the sign-up's answer.
//
// Output
// [object[]]: The links to show.
export function safeLinks(resources) {
    return (resources || []).filter(function (r) { return r && r.Url && /^https?:\/\//i.test(r.Url); });
}

// formProblem
// Finds the first thing the sign-up form lacks before it is sent, so the page says what to fix without a round trip.
//
// Inputs
// form [object]: requiresPin, pin, username, password, and confirm as the form holds them.
//
// Output
// [object|null]: The message's key and the field to focus, or null when the form can be sent.
export function formProblem(form) {
    if (form.requiresPin && !/^\d{6}$/.test(String(form.pin || ''))) return { key: 'InvitesErrorPin', field: 'pin' };
    if (!String(form.username || '').trim()) return { key: 'InvitesErrorChooseUsername', field: 'username' };
    if (!form.password) return { key: 'InvitesErrorChoosePassword', field: 'password' };
    if (form.password !== form.confirm) return { key: 'InvitesErrorPasswordsDiffer', field: 'confirm' };
    return null;
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
    doc.getElementById('signupLoading').innerHTML = WEB_SPINNER;
    await startWebClientShell({ translations: 'Necto/Translations' });
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

    if (info.Message) {
        byId('welcome').textContent = info.Message;
        byId('welcome').hidden = false;
    }
    byId('pinField').hidden = !info.RequiresPin;
    showUserPageView('form', doc);

    byId('signupForm').addEventListener('submit', function (e) {
        e.preventDefault();
        var message = byId('formMessage');
        var problem = formProblem({ requiresPin: info.RequiresPin, pin: byId('pin').value, username: byId('username').value, password: byId('password').value, confirm: byId('confirm').value });
        message.textContent = problem ? t.tr(problem.key) : '';
        if (problem) {
            byId(problem.field).focus();
            return;
        }

        var password = byId('password').value;

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
                    a.className = 'raised block emby-button';
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
            message.textContent = (answer && answer.Message) || t.tr('InvitesErrorSignup');
            byId('submit').disabled = false;
        }).catch(function () {
            message.textContent = t.tr('InvitesErrorNetwork');
            byId('submit').disabled = false;
        });
    });
}

if (typeof document !== 'undefined' && document.getElementById('signupForm')) {
    start(document, location);
}
