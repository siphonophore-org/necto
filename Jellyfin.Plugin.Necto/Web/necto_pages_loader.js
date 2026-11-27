// necto_pages_loader.js
// The script of a custom page's loading screen. It asks for the page with this server's sign-ins in turn and writes it
// into the document, or shows Stolon's sign in, not authorized, or not found screen in the web client's look.

import { startWebClientShell, stopWebClientShell, createSignIn, createUnauthorized, createNotFound, showScreen, WEB_SPINNER } from './configurationpage?name=necto_shared.js';

// normalizeId
// Gives a server id without dashes, in lower case.
//
// Inputs
// id [string]: The id.
//
// Output
// [string]: The id to compare.
function normalizeId(id) {
    return String(id || '').replace(/-/g, '').toLowerCase();
}

// originOf
// Gives the origin of an address, or empty when it does not parse.
//
// Inputs
// address [string]: The address.
//
// Output
// [string]: The origin in lower case, or empty.
function originOf(address) {
    try { return new URL(String(address)).origin.toLowerCase(); } catch (e) { return ''; }
}

// tokensFor
// Lists this server's saved tokens, matched by its id or by an address on this exact origin, so another server's
// token is never sent here.
//
// Inputs
// servers [object[]]: The web client's saved servers.
// serverId [string]: This server's id, or empty when it is not known.
// origin [string]: The page's origin.
//
// Output
// [string[]]: The tokens, each once, in the order the web client keeps them.
export function tokensFor(servers, serverId, origin) {
    var here = String(origin || '').toLowerCase();
    var wanted = normalizeId(serverId);
    var found = [];
    (Array.isArray(servers) ? servers : []).forEach(function (server) {
        if (!server || !server.AccessToken) return;
        var matches = !!wanted && normalizeId(server.Id) === wanted;
        if (!matches) {
            matches = [server.ManualAddress, server.LocalAddress, server.RemoteAddress].some(function (a) { return a && originOf(a) === here; });
        }

        if (matches && found.indexOf(server.AccessToken) < 0) found.push(server.AccessToken);
    });
    return found;
}

// readServers
// Reads the web client's saved servers. A change to their shape leaves none, so the page asks to sign in.
//
// Output
// [object[]]: The servers.
function readServers() {
    try {
        var raw = localStorage.getItem('jellyfin_credentials');
        return raw ? (JSON.parse(raw) || {}).Servers || [] : [];
    } catch (e) {
        return [];
    }
}

// screenFor
// Says what an answer for the page's content means.
//
// Inputs
// status [number]: The answer's status code.
//
// Output
// [string]: next to try the next sign-in, denied, missing, or page.
export function screenFor(status) {
    if (status === 401) return 'next';
    if (status === 403) return 'denied';
    if (status === 404) return 'missing';
    return 'page';
}

// spinnerScreen
// Builds the web client's loading spinner as a screen, shown while a sign-in from the page is tried.
//
// Inputs
// doc [Document]: The page.
// label [string]: The spinner's accessible name.
//
// Output
// [object]: The screen, { element, destroy }.
function spinnerScreen(doc, label) {
    var element = doc.createElement('div');
    element.className = 'docspinner mdl-spinner mdlSpinnerActive';
    element.setAttribute('role', 'status');
    element.setAttribute('aria-label', label);
    element.innerHTML = WEB_SPINNER;
    return { element: element, destroy: function () { element.remove(); } };
}

// start
// Runs the loading screen. The shell stays signed out, since the page it fetches is written over it.
//
// Inputs
// doc [Document]: The page.
// loc [Location]: The page's address.
//
// Output
// [Promise]: Resolves once the page or a screen shows.
export async function start(doc, loc) {
    var data = doc.getElementById('pageLoading');
    var slug = data.getAttribute('data-slug');
    var tier = data.getAttribute('data-tier');
    var denied = data.getAttribute('data-denied');
    var signInMessage = data.getAttribute('data-sign-in');
    var expiredMessage = data.getAttribute('data-expired');
    var label = (doc.querySelector('[role="status"]') || data).getAttribute('aria-label') || '';
    await startWebClientShell({ translations: 'Necto/Translations', follow: false });
    var prefix = loc.pathname.split('/pages/')[0];
    var contentUrl = prefix + '/pages/' + encodeURIComponent(slug) + '/' + tier;
    var info = await fetch(prefix + '/System/Info/Public', { headers: { Accept: 'application/json' } })
        .then(function (r) { return r.ok ? r.json() : null; })
        .catch(function () { return null; });
    // The tokens stay inside this function, so the page written into this document cannot read them off the window.
    var tokens = tokensFor(readServers(), info && info.Id, loc.origin);
    var hadSignIn = tokens.length > 0;

    var signIn = function () {
        var screen = createSignIn({
            serverId: info && info.Id ? String(info.Id) : '',
            serverName: info && info.ServerName ? String(info.ServerName) : null,
            message: hadSignIn ? expiredMessage : signInMessage,
            onSignedIn: function (result) {
                screen.destroy();
                showScreen(spinnerScreen(doc, label));
                tokens.push(result.AccessToken);
                attempt(tokens.length - 1);
            }
        });
        showScreen(screen);
    };

    var attempt = function (index) {
        if (index >= tokens.length) return signIn();
        return fetch(contentUrl, { headers: { Authorization: 'MediaBrowser Token="' + tokens[index] + '"', Accept: 'text/html' } }).then(function (r) {
            var next = screenFor(r.status);
            // A token no longer valid here, such as one from an earlier sign in, gives way to the next.
            if (next === 'next') return attempt(index + 1);
            if (next === 'denied') return showScreen(createUnauthorized({ message: denied }));
            if (next === 'missing') return showScreen(createNotFound());
            return r.text().then(function (html) {
                stopWebClientShell();
                doc.open();
                doc.write(html);
                doc.close();
            });
        }).catch(signIn);
    };

    return attempt(0);
}

if (typeof document !== 'undefined' && document.getElementById('pageLoading')) {
    start(document, location);
}
