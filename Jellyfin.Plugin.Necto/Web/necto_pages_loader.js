// necto_pages_loader.js
// The script of a custom page's loading screen. It asks for the page with this server's sign-ins in turn and writes it
// into the document, or shows Stolon's sign in, not authorized, or not found screen in the web client's look.

import { startWebClientShell, createSignIn, createUnauthorized, createNotFound, showScreen, WEB_SPINNER } from './configurationpage?name=necto_shared.js';

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

// start
// Runs the loading screen.
//
// Inputs
// doc [Document]: The page.
// loc [Location]: The page's address.
//
// Output
// [Promise]: Resolves once the page or a screen shows.
export async function start(doc, loc) {
    var loading = doc.getElementById('pageLoading');
    loading.innerHTML = WEB_SPINNER;
    await startWebClientShell({ translations: 'Necto/Translations' });
    var prefix = loc.pathname.split('/pages/')[0];
    var contentUrl = prefix + '/pages/' + encodeURIComponent(loading.getAttribute('data-slug')) + '/' + loading.getAttribute('data-tier');
    var info = await fetch(prefix + '/System/Info/Public', { headers: { Accept: 'application/json' } })
        .then(function (r) { return r.ok ? r.json() : null; })
        .catch(function () { return null; });
    // The tokens stay inside this function, so the page written into this document cannot read them off the window.
    var tokens = tokensFor(readServers(), info && info.Id, loc.origin);

    var show = function (screen) {
        loading.hidden = true;
        showScreen(screen);
    };

    var signIn = function () {
        var screen = createSignIn({
            serverId: info && info.Id ? String(info.Id) : '',
            serverName: info && info.ServerName ? String(info.ServerName) : null,
            onSignedIn: function (result) {
                screen.destroy();
                loading.hidden = false;
                tokens.push(result.AccessToken);
                attempt(tokens.length - 1);
            }
        });
        show(screen);
    };

    var attempt = function (index) {
        if (index >= tokens.length) return signIn();
        return fetch(contentUrl, { headers: { Authorization: 'MediaBrowser Token="' + tokens[index] + '"', Accept: 'text/html' } }).then(function (r) {
            var next = screenFor(r.status);
            // A token no longer valid here, such as one from an earlier sign in, gives way to the next.
            if (next === 'next') return attempt(index + 1);
            if (next === 'denied') return show(createUnauthorized({ message: loading.getAttribute('data-denied') }));
            if (next === 'missing') return show(createNotFound());
            return r.text().then(function (html) {
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
