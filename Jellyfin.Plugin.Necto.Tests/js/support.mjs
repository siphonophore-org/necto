// support.mjs
// Shared setup for the config page tests. It installs Stolon's fake DOM, stubs the Jellyfin globals the pages
// read, serves the plugin's US English strings, and loads a page script from the plugin's Web folder through the loader hook.

import { register } from 'node:module';
import { pathToFileURL, fileURLToPath } from 'node:url';
import { join, dirname } from 'node:path';
import { existsSync, readFileSync } from 'node:fs';

var realSetTimeout = globalThis.setTimeout;
var here = dirname(fileURLToPath(import.meta.url));
var projectDir = join(here, '..', '..', 'Jellyfin.Plugin.Necto');
var configDir = join(projectDir, 'Web');
// Stolon's checkout: STOLON_ROOT when set, as in CI, else the one beside this repository.
var baseRoot = process.env.STOLON_ROOT || join(here, '..', '..', '..', 'stolon');
var baseBundle = join(baseRoot, 'dist', 'stolon.js');
var baseEntry = pathToFileURL(baseBundle).href;
var fakeDomPath = join(baseRoot, 'Siphonophore.Stolon.Tests', 'js', 'fake-dom.mjs');

if (!existsSync(fakeDomPath)) {
    throw new Error('Stolon is not checked out next to this repository. Expected ' + baseRoot + '.');
}

// The pages load the bundle Stolon ships, so a test runs the same code a plugin receives.
if (!existsSync(baseBundle)) {
    throw new Error('Stolon has no bundle. Run scripts/bundle.sh in ' + baseRoot + '.');
}

register(pathToFileURL(join(here, 'loader.mjs')).href, { parentURL: import.meta.url, data: { configDir: configDir, baseEntry: baseEntry } });

var fakeDom = await import(pathToFileURL(fakeDomPath).href);

// usEnglish
// Builds the strings the Translations route answers with in US English: the plugin's own, and the Jellyfin
// strings the project declares, read from the copy of Jellyfin's strings in Stolon.
//
// Output
// [object]: The strings by key.
function usEnglish() {
    var strings = {};
    var jellyfin = {
        web: JSON.parse(readFileSync(join(baseRoot, 'dist', 'jellyfin', 'web', 'en-us.json'), 'utf8')),
        server: JSON.parse(readFileSync(join(baseRoot, 'dist', 'jellyfin', 'server', 'en-us.json'), 'utf8'))
    };
    var project = readFileSync(join(projectDir, 'Jellyfin.Plugin.Necto.csproj'), 'utf8');
    for (var match of project.matchAll(/<JellyfinString Include="([^"]+)"( Source="server")?/g)) {
        strings[match[1]] = jellyfin[match[2] ? 'server' : 'web'][match[1]];
    }
    return Object.assign(strings, JSON.parse(readFileSync(join(projectDir, 'Localization', 'en-us.json'), 'utf8')));
}

export var strings = usEnglish();

// casingList
// Reads the plugin's casing list, the words its text keeps as written.
//
// Output
// [string[]]: The words, or an empty list when the plugin has none.
function casingList() {
    var file = join(projectDir, 'Localization', 'casing.json');
    return existsSync(file) ? JSON.parse(readFileSync(file, 'utf8')) : [];
}

// The pages load their strings with fetch, which answers here as StolonTranslations.Serve does, in US English.
globalThis.fetch = function (url) {
    if (String(url).indexOf('Necto/Translations') < 0) return Promise.reject(new Error('No route for ' + url));
    var body = { language: 'en-us', strings: strings, casing: casingList() };
    return Promise.resolve({ ok: true, json: function () { return Promise.resolve(body); } });
};

// The fake element has no dataset or remove, which the page scripts use.
var elementProto = Object.getPrototypeOf(new fakeDom.FakeElement({}, 'div'));
if (!Object.getOwnPropertyDescriptor(elementProto, 'dataset')) {
    Object.defineProperty(elementProto, 'dataset', {
        get: function () {
            var el = this;
            var attr = function (key) { return 'data-' + key.replace(/[A-Z]/g, function (c) { return '-' + c.toLowerCase(); }); };
            return new Proxy({}, {
                get: function (target, key) { var v = el.getAttribute(attr(String(key))); return v === null ? undefined : v; },
                set: function (target, key, value) { el.setAttribute(attr(String(key)), value); return true; },
                has: function (target, key) { return el.hasAttribute(attr(String(key))); },
                deleteProperty: function (target, key) { el.removeAttribute(attr(String(key))); return true; }
            });
        }
    });
}
if (!elementProto.remove) {
    elementProto.remove = function () { if (this.parentNode) this.parentNode.removeChild(this); };
}

// installPage
// Installs a fresh fake DOM and the Jellyfin globals a page script reads.
//
// Inputs
// opts [object]: config is what ApiClient returns as the plugin configuration. storage seeds localStorage.
// calls is an array that collects each LibraryMenu.setTabs call, which a Necto page never makes.
//
// Output
// [object]: The fake DOM helper from Stolon, plus the stubbed ApiClient requests it recorded.
export function installPage(opts) {
    opts = opts || {};
    var dom = fakeDom.installFakeDom();
    dom.document.getElementById = function (id) { return dom.document.documentElement.querySelector('#' + id); };
    var requests = [];
    var store = Object.assign({}, opts.storage || {});
    globalThis.localStorage = {
        getItem: function (key) { return Object.prototype.hasOwnProperty.call(store, key) ? store[key] : null; },
        setItem: function (key, value) { store[key] = String(value); }
    };
    globalThis.ApiClient = {
        getUrl: function (path) { return path; },
        fetch: function (options) { requests.push(options); return Promise.resolve({}); },
        getPluginConfiguration: function () { return Promise.resolve(opts.config || {}); },
        updatePluginConfiguration: function () { return Promise.resolve(); },
        getPublicSystemInfo: function () { return Promise.resolve({ ServerName: 'Test Server' }); }
    };
    globalThis.LibraryMenu = { setTabs: function (key, index, getTabs) { (opts.calls || []).push({ key: key, index: index, tabs: getTabs() }); } };
    globalThis.Dashboard = { alert: function () { throw new Error('Dashboard.alert must not be used.'); } };
    return { dom: dom, requests: requests, store: store };
}

// loadScript
// Imports one of the plugin's Web scripts, so a test calls its exports.
//
// Inputs
// name [string]: The file name, such as necto_shared.js.
//
// Output
// [Promise]: The module namespace.
export function loadScript(name) {
    return import(pathToFileURL(join(configDir, name)).href);
}

export var paths = { configDir: configDir, baseRoot: baseRoot };

// allToolsOn
// Gives a plugin config with every tool that has a switch turned on, for a page whose tools should all show.
//
// Output
// [object]: The config.
export function allToolsOn() {
    return {
        Requests: { Enabled: true }, Issues: { Enabled: true }, InviteSettings: { Enabled: true }, Proxy: { Enabled: true },
        Ddns: { Enabled: true }, WireGuard: { Enabled: true }, Scripts: { Enabled: true }, CustomPages: { Enabled: true },
        Storage: { Enabled: true }
    };
}

// flush
// Waits for the promise chains a call started to settle.
export async function flush() {
    for (var i = 0; i < 8; i++) await new Promise(function (resolve) { setImmediate(resolve); });
}

// settle
// Waits real time for the module imports a page started to land, then for the promise chains after them.
//
// Inputs
// ms [number]: How long to wait. Defaults to 150.
export async function settle(ms) {
    await new Promise(function (resolve) { realSetTimeout(resolve, ms || 150); });
    await flush();
}

// clickDialogButton
// Clicks the button with this text in the dialog that is open. A sidecar is not a dialog.
//
// Inputs
// dom [object]: The fake DOM helper from installPage.
// text [string]: The button's label.
//
// Output
// [boolean]: Whether a button with that label was found.
export function clickDialogButton(dom, text) {
    var dialog = dom.document.querySelector('.stolon-dialog:not(.stolon-sidecar)');
    if (!dialog) return false;
    var button = Array.from(dialog.querySelectorAll('button')).find(function (b) { return b.textContent === text; });
    if (!button) return false;
    button.click();
    return true;
}

// dialogText
// Reads the title and the text under it of the dialog that is open, never a sidecar: the description of a compact one, or the subtitle and
// the context of a full one.
//
// Inputs
// dom [object]: The fake DOM helper from installPage.
//
// Output
// [object]: The title and the text under it, or null when no dialog is open.
export function dialogText(dom) {
    var dialog = dom.document.querySelector('.stolon-dialog:not(.stolon-sidecar)');
    if (!dialog) return null;
    var title = dialog.querySelector('.stolon-modal-title');
    var parts = ['.stolon-modal-description', '.stolon-modal-subtitle', '.stolon-modal-context'].map(function (s) {
        var el = dialog.querySelector(s);
        return el ? el.textContent : '';
    }).filter(Boolean);
    return { title: title ? title.textContent : '', body: parts.join(' ') };
}

// statusText
// Reads the message a status element shows, without the icon Stolon draws beside it.
//
// Inputs
// el [object]: The status element.
//
// Output
// [string]: The message, or an empty string when it shows none.
export function statusText(el) {
    var message = el && el.querySelector ? el.querySelector('.stolon-status-message') : null;
    return message ? message.textContent : '';
}

// pageView
// Builds a page view whose content sits in content-primary, where the page's tool shell takes it.
//
// Inputs
// dom [object]: The fake DOM helper from installPage.
// children [HTMLElement[]]: The page's content.
//
// Output
// [HTMLElement]: The view.
export function pageView(dom, children) {
    return dom.el('div', { class: 'page' }, [dom.el('div', { class: 'content-primary' }, children)]);
}

// sidebar
// Reads the tool shell sidebar of a shown page.
//
// Inputs
// view [HTMLElement]: The page view.
//
// Output
// [object]: names and hrefs of every entry in order, and current, the index of the entry marked current.
export function sidebar(view) {
    var entries = Array.prototype.slice.call(view.querySelectorAll('.stolon-tool-shell-item')).filter(function (e) { return e.getAttribute('data-key'); });
    return {
        names: entries.map(function (e) { return e.querySelector('.stolon-menu-label').textContent; }),
        hrefs: entries.map(function (e) { return e.getAttribute('href'); }),
        current: entries.findIndex(function (e) { return e.getAttribute('aria-current') === 'page'; })
    };
}

// routeApi
// Answers the page's API requests by method and path, and records each one on the page.
//
// Inputs
// page [object]: The page from installPage.
// answers [object]: The answer for each 'METHOD path', such as 'GET Groups'. An answer may be a function of the
//   request, or an Error to reject with. A request with no answer gets an empty object.
export function routeApi(page, answers) {
    globalThis.ApiClient.fetch = function (options) {
        page.requests.push(options);
        var answer = answers[(options.type || 'GET') + ' ' + options.url];
        if (typeof answer === 'function') answer = answer(options);
        if (answer instanceof Error) return Promise.reject(answer);
        return Promise.resolve(answer === undefined ? {} : answer);
    };
}

// routeApiByPath
// Answers each request by its method and path whatever its query, such as a table's GET Ddns/Records/All?skip=0.
//
// Inputs
// page [object]: The page from installPage.
// answers [object]: Each answer by method and path. A function is called with the request.
export function routeApiByPath(page, answers) {
    globalThis.ApiClient.fetch = function (options) {
        page.requests.push(options);
        var answer = answers[(options.type || 'GET') + ' ' + String(options.url).split('?')[0]];
        if (typeof answer === 'function') answer = answer(options);
        if (answer instanceof Error) return Promise.reject(answer);
        return Promise.resolve(answer === undefined ? {} : answer);
    };
}

// pickRows
// Ticks the checkbox of each table row at these places under a root.
//
// Inputs
// root [object]: Where the table is.
// places [number[]]: The rows' places, from 0.
export function pickRows(root, places) {
    var rows = root.querySelectorAll('.stolon-table-row');
    places.forEach(function (i) {
        var box = rows[i].querySelector('.stolon-table-row-checkbox');
        box.checked = true;
        box.dispatchEvent(new globalThis.Event('change', { bubbles: true }));
    });
}

// texts
// Reads the text of every element a selector matches under a root.
//
// Inputs
// root [object]: Where to look.
// selector [string]: The selector.
//
// Output
// [string[]]: Each match's text, in order.
export function texts(root, selector) {
    return Array.prototype.map.call(root.querySelectorAll(selector), function (e) { return e.textContent; });
}

// clickButton
// Clicks the button with this text under a root.
//
// Inputs
// root [object]: Where to look.
// text [string]: The button's label.
//
// Output
// [boolean]: Whether a button with that label was found.
export function clickButton(root, text) {
    var button = Array.prototype.find.call(root.querySelectorAll('button'), function (b) { return b.textContent === text; });
    if (!button) return false;
    button.click();
    return true;
}
