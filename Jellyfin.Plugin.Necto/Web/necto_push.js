// necto_push.js
// Browser notifications for the user pages. Every user page registers the one service worker Notifications serves,
// at the server's root, so a browser keeps one subscription for all of them.

import { subscribeWebPush, unsubscribeWebPush, webPushState, webPushLabel } from './configurationpage?name=necto_shared.js';

// WORKER_URL
// The service worker every user page shares, relative to the server's root.
export var WORKER_URL = 'Notifications/Push/Worker.js';

// workerScope
// Works out the scope the worker is registered at: the server's root, under any base path.
//
// Inputs
// baseHref [string]: The page's base address, the server's root.
//
// Output
// [string]: The scope's path, ending in a slash.
export function workerScope(baseHref) {
    try {
        var path = new URL(baseHref).pathname;
        return path.endsWith('/') ? path : path + '/';
    } catch (e) {
        return '/';
    }
}

// registration
// Registers the shared service worker, or finds it already registered.
//
// Inputs
// doc [Document]: The page, whose base is the server's root.
//
// Output
// [Promise]: Resolves with the registration, or null where the browser has no service workers.
export function registration(doc) {
    var nav = globalThis.navigator;
    if (!nav || !nav.serviceWorker) return Promise.resolve(null);
    var base = (doc || globalThis.document).baseURI;
    return nav.serviceWorker.register(new URL(WORKER_URL, base).href, { scope: workerScope(base) });
}

// createPushToggle
// Builds a user page's browser notification switch: where it stands, and a button that turns notifications on or off
// for the signed in viewer in this browser.
//
// Inputs
// session [object]: The user page session, whose request calls the server as the viewer.
// t [object]: The strings.
// doc [Document]: The page.
//
// Output
// [object]: { element, refresh }.
export function createPushToggle(session, t, doc) {
    doc = doc || globalThis.document;
    var text = doc.createElement('p');
    text.className = 'stolon-user-page-hint';
    var button = doc.createElement('button');
    button.type = 'button';
    button.className = 'raised stolon-button stolon-secondary stolon-large stolon-fill';
    var box = doc.createElement('div');
    box.className = 'stolon-user-page-actions';
    box.appendChild(text);
    box.appendChild(button);
    var state = 'off';
    var key = null;
    var reg = registration(doc).catch(function () { return null; });

    var post = function (path, sub) {
        return session.request(path, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(sub) });
    };

    var show = function (result) {
        state = result.state;
        text.textContent = result.message;
        button.textContent = state === 'on' ? t.tr('NotificationsButtonPushOff') : t.tr('NotificationsButtonPushOn');
        button.hidden = !(state === 'on' || state === 'off');
        return result;
    };

    var refresh = function () {
        return session.request('Notifications/Push/Key').then(function (r) { return r.ok ? r.json() : null; }).then(function (answer) {
            key = answer && answer.PublicKey;
            if (!key) {
                box.hidden = true;
                return null;
            }

            box.hidden = false;
            return reg.then(function (r) { return webPushState(r); }).then(show);
        }).catch(function () { box.hidden = true; });
    };

    button.addEventListener('click', function () {
        button.disabled = true;
        var opts = {
            registration: reg,
            publicKey: key,
            save: function (sub) { sub.label = sub.label || webPushLabel(); return post('Notifications/Push/Subscribe', sub); },
            remove: function (sub) { return post('Notifications/Push/Unsubscribe', sub); }
        };
        (state === 'on' ? unsubscribeWebPush(opts) : subscribeWebPush(opts)).then(show).catch(function () {
            text.textContent = t.tr('NotificationsErrorPush');
        }).then(function () { button.disabled = false; });
    });

    refresh();
    return { element: box, refresh: refresh };
}
