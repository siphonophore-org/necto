// necto_shared.js
// Wraps the Stolon shared bundle for the Necto pages. It lists the tools, builds the tool app, and wires each
// tool's page through Stolon's page lifecycle with the plugin's strings.
//
// Necto is one page, necto, whose sidebar lists every tool, and each tool is its own registered page that the
// tool app swaps in place. Every page loads the plugin's strings once from TRANSLATIONS_URL, and a page's bind
// and load run once it has them, so the marked markup is in the viewer's language.

// The specifier is relative so it resolves against this module's own URL, and a server hosted under
// a base URL still loads the bundle.
import {
    createShared as baseCreateShared,
    createConfigPage,
    createToolApp,
    setToolTitle,
    refreshTools,
    openTool,
    loadTranslations,
    createTranslations
} from './configurationpage?name=necto_stolon.js';

// Re-exports
// The Stolon builders a tool page uses, so a page takes everything from this one module.
export { openTool };

var PLUGIN_ID = 'a8b5ecb0-fc00-428b-85e8-74b85820166d';

// TRANSLATIONS_URL
// The route that gives the pages their strings in the viewer's language.
export var TRANSLATIONS_URL = 'Necto/Translations';

// The strings once a page has loaded them, so the sidebar names follow the viewer's language.
var tabStrings = null;

// tabName
// Names a sidebar entry in the viewer's language, or in English until the strings have loaded.
//
// Inputs
// key [string]: The key of the entry's name.
// english [string]: The name to show before the strings load.
//
// Output
// [string]: The entry's name.
function tabName(key, english) {
    return tabStrings ? tabStrings.tr(key, english) : english;
}

// nectoTools
// Lists Necto's tools in the order the sidebar shows them, the dashboard first.
//
// Output
// [object[]]: Each tool's key, name, icon, and the registered page it shows.
export function nectoTools() {
    return [
        { key: 'dashboard', name: tabName('TabDashboard', 'Dashboard'), icon: 'dashboard', page: 'necto_dashboard' },
        { key: 'requests', name: tabName('TabRequests', 'Requests'), icon: 'movie_filter', page: 'necto_requests' },
        { key: 'issues', name: tabName('TabIssues', 'Issues'), icon: 'report_problem', page: 'necto_issues' },
        { key: 'invites', name: tabName('TabInvites', 'Invites'), icon: 'mail', page: 'necto_invites' },
        { key: 'groups', name: tabName('TabGroups', 'Groups'), icon: 'groups', page: 'necto_groups' },
        { key: 'proxy', name: tabName('TabProxy', 'Proxy'), icon: 'security', page: 'necto_proxy' },
        { key: 'ddns', name: tabName('TabDdns', 'DDNS'), icon: 'dns', page: 'necto_ddns' },
        { key: 'wireguard', name: tabName('TabWireGuard', 'WireGuard'), icon: 'vpn_lock', page: 'necto_wireguard' },
        { key: 'scripts', name: tabName('TabScripts', 'Scripts'), icon: 'code', page: 'necto_scripts' },
        { key: 'pages', name: tabName('TabPages', 'Custom pages'), icon: 'web', page: 'necto_pages' },
        { key: 'storage', name: tabName('TabStorage', 'Storage'), icon: 'storage', page: 'necto_storage' },
        { key: 'notifications', name: tabName('TabNotifications', 'Notifications'), icon: 'notifications', page: 'necto_notifications' },
        { key: 'settings', name: tabName('Settings', 'Settings'), icon: 'settings', page: 'necto_settings' },
        { key: 'troubleshooting', name: tabName('HeaderTroubleshooting', 'Troubleshooting'), icon: 'bug_report', page: 'necto_troubleshooting' }
    ];
}

// createNectoApp
// Builds the Necto page: Stolon's tool app with every Necto tool, which swaps each tool in place. The app is built
// at once, so it catches the page's first show, and the sidebar names turn to the viewer's language once the
// strings arrive.
//
// Inputs
// view [HTMLElement]: The host page the dashboard passes to the controller.
//
// Output
// [object]: The tool app's handle.
export function createNectoApp(view) {
    var app = createToolApp(view, { storageKey: 'necto', name: 'Necto', tools: nectoTools });
    loadTranslations(TRANSLATIONS_URL).then(function (t) {
        tabStrings = t;
        app.refreshTools();
    }, function () { /* the English names stay */ });
    return app;
}

// createNectoPage
// Wires a Necto tool's page through Stolon's page lifecycle, with the Necto helper bag. The page's bind and load
// wait for the strings, and the marked markup is translated before bind runs.
//
// Inputs
// view [HTMLElement]: The tool's view, which the tool app passes to the controller.
// opts [object]: page is the tool's own page name. bind, load, and onHide are Stolon's, and each is given the
//   Necto bag and the page handle.
//
// Output
// [object]: shared is the Necto helper bag, and page is Stolon's page handle with addCleanup and createPoller.
export function createNectoPage(view, opts) {
    var bag = null;
    var pass = function (fn) { return fn ? function (shared, page) { fn(bag, page); } : undefined; };
    var bound = null;
    var page = createConfigPage(view, {
        pluginId: PLUGIN_ID,
        apiPrefix: 'Necto',
        bind: function () {
            bound = bag.translations.then(function (t) {
                t.apply(view);
                if (opts.bind) opts.bind(bag, page);
            });
        },
        load: function () {
            bound.then(function () { if (opts.load) opts.load(bag, page); });
        },
        onHide: pass(opts.onHide)
    });
    bag = createNectoShared(view, page.shared);
    // Reads the sidebar again, such as after a setting turns a tool on or off.
    bag.onTabsChanged = function () { refreshTools(view); };
    // Sets the heading over the tool, which is the tool's name by default.
    bag.setPageTitle = function (text) { setToolTitle(view, text); };
    return { shared: bag, page: page };
}

// createNectoShared
// Builds the helper bag a Necto page uses. It is Stolon's bag for the Necto route, with the plugin's strings.
//
// Inputs
// view [HTMLElement]: The page the helpers work in.
// bag [object]: Stolon's bag to add the helpers to, such as a page handle's. A new one is built when left out.
//
// Output
// [object]: The helper bag, with every Stolon helper, t, and translations, the promise of the strings.
export function createNectoShared(view, bag) {
    var shared = bag || baseCreateShared(view, PLUGIN_ID, 'Necto');

    // The plugin's strings. t has none until translations resolves, which every page waits for.
    shared.t = createTranslations({});
    shared.translations = loadTranslations(TRANSLATIONS_URL).then(function (t) {
        shared.t = t;
        tabStrings = t;
        return t;
    });

    return shared;
}
