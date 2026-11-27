// necto_shared.js
// Wraps the Stolon shared bundle for the Necto pages. It lists the tools, builds the tool app, and wires each
// tool's page through Stolon's page lifecycle with the plugin's strings.
//
// Necto is one page, necto, whose sidebar lists every tool, and each tool is its own registered page that the
// tool app swaps in place. Every page loads the plugin's strings once from TRANSLATIONS_URL, and a page's bind
// and load run once it has them, so the marked markup is in the viewer's language.
//
// Settings and Troubleshooting draw one section per tool from a registry. A tool adds its section with
// registerSection from its own sections module, and a tool with none shows that it is coming.

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
    createTranslations,
    confirmDialog,
    openEditorSidecar,
    createRecordList,
    createCollapsibleSection,
    createRepeater,
    createChipSelect,
    createCheckboxList,
    createSaveBar,
    createCopyField
} from './configurationpage?name=necto_stolon.js';

// Re-exports
// The Stolon builders a tool page uses, so a page takes everything from this one module.
export { openTool, confirmDialog, openEditorSidecar, createRecordList, createCollapsibleSection, createRepeater, createChipSelect, createCheckboxList, createSaveBar, createCopyField };

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

// el
// Builds an element with its attributes and children, as the pages draw their markup. A Jellyfin element such as
// emby-input is made with its is option, so it upgrades.
//
// Inputs
// tag [string]: The tag, such as div.
// attrs [object]: Attributes by name. class sets the class, text the text, is the Jellyfin element, and true an empty
//   attribute. null, undefined, and false are left out. [optional]
// children [Node[]]: The children, in order. Empty entries are skipped. [optional]
//
// Output
// [HTMLElement]: The element.
export function el(tag, attrs, children) {
    attrs = attrs || {};
    var node = attrs.is ? document.createElement(tag, { is: attrs.is }) : document.createElement(tag);
    Object.keys(attrs).forEach(function (key) {
        var value = attrs[key];
        if (value === null || value === undefined || value === false) return;
        if (key === 'class') node.className = value;
        else if (key === 'text') node.textContent = value;
        else node.setAttribute(key, value === true ? '' : String(value));
    });
    (children || []).forEach(function (child) { if (child) node.appendChild(child); });
    return node;
}

// The sections each tool added, by kind and then by tool key.
var sectionRegistry = { settings: {}, troubleshooting: {} };

// registerSection
// Adds a tool's section to Settings or Troubleshooting. A tool calls it once from its sections module.
//
// Inputs
// kind [string]: settings or troubleshooting.
// key [string]: The tool's key, as nectoTools lists it.
// section [object]: build(content, ctx) fills the section's content and returns a handle. ctx holds shared and
//   page. A Settings handle has load(config) and apply(config), which writes its fields into a config. A
//   Troubleshooting handle has load(), and either may have destroy().
export function registerSection(kind, key, section) {
    if (!sectionRegistry[kind]) throw new Error('No section kind ' + kind);
    sectionRegistry[kind][key] = section;
}

// nectoSections
// Lists the tools Settings or Troubleshooting draws a section for, in sidebar order, each with the section it
// added or null when the tool is coming. The dashboard, Settings, and Troubleshooting draw none.
//
// Inputs
// kind [string]: settings or troubleshooting.
//
// Output
// [object[]]: Each tool's key, name, icon, and section.
export function nectoSections(kind) {
    var registered = sectionRegistry[kind] || {};
    return nectoTools().filter(function (tool) {
        return ['dashboard', 'settings', 'troubleshooting'].indexOf(tool.key) < 0;
    }).map(function (tool) {
        return { key: tool.key, name: tool.name, icon: tool.icon, section: registered[tool.key] || null };
    });
}

// drawSections
// Draws a collapsible section per tool into a container, with the content each tool's section fills, or the coming
// state for a tool with none.
//
// Inputs
// container [HTMLElement]: Where the sections go.
// kind [string]: settings or troubleshooting.
// shared [object]: The Necto helper bag.
// page [object]: The page handle.
// addFooter [function]: Called with each filled section's content and handle, to add what ends it, such as a Save Bar. [optional]
//
// Output
// [object[]]: One entry per tool, with its key, collapsible, and handle, which is null while the tool is coming.
export function drawSections(container, kind, shared, page, addFooter) {
    var t = shared.t;
    container.innerHTML = '';
    return nectoSections(kind).map(function (tool) {
        var content = el('div', { 'data-role': 'section-content' });
        var section = createCollapsibleSection({ title: tool.name, icon: tool.icon, collapsed: true, content: content });
        section.element.setAttribute('data-tool', tool.key);
        container.appendChild(section.element);
        if (!tool.section) {
            section.setBadge({ label: t.tr('BadgeComing'), color: 'gray' });
            content.innerHTML = shared.emptySection(t.tr('MessageToolComing'), { inline: true });
            return { key: tool.key, collapsible: section, handle: null };
        }
        var handle = tool.section.build(content, { shared: shared, page: page });
        if (addFooter) addFooter(content, handle, tool);
        return { key: tool.key, collapsible: section, handle: handle };
    });
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

    // request
    // Calls a Necto route by its own path, such as Groups/Users, since every tool has a route of its own.
    //
    // Inputs
    // route [string]: The path after the server address.
    // method [string]: The HTTP method. Defaults to GET.
    // data [any]: The body, sent as JSON.
    //
    // Output
    // [Promise]: Resolves with the answer's JSON.
    shared.request = function (route, method, data) {
        return shared.apiRequest.call({ apiPrefix: '' }, route, method, data);
    };

    // plural
    // Writes a text with a count in the right form, such as "1 user moved." or "3 users moved.".
    //
    // Inputs
    // count [number]: How many there are, filled in as {0}.
    // one [string]: The key of the text for exactly one.
    // many [string]: The key of the text for any other count.
    //
    // Output
    // [string]: The text in the viewer's language.
    shared.plural = function (count, one, many) {
        return shared.t.format(count === 1 ? one : many, count);
    };

    // runTask
    // Starts a Jellyfin scheduled task by its key and follows its progress on a button.
    //
    // Inputs
    // btn [HTMLButtonElement]: The button that shows the progress.
    // key [string]: The task's key.
    // label [string]: The button's label once the task is done.
    // onComplete [function]: Called once the task is done.
    //
    // Output
    // [Promise]: Resolves with the progress handle, and rejects when the task could not start.
    shared.runTask = function (btn, key, label, onComplete) {
        return ApiClient.getScheduledTasks().then(function (tasks) {
            var task = (tasks || []).find(function (t) { return t.Key === key; });
            if (!task) throw new Error('No task ' + key);
            return ApiClient.startScheduledTask(task.Id);
        }).then(function () {
            return shared.pollTaskProgress(btn, key, label, onComplete);
        });
    };

    return shared;
}

// Re-exports for Notifications, Invites, and the group templates
// The Stolon builders those tools use, kept apart from the list above.
export {
    createInheritField,
    createAccessSchedule,
    createTagInput,
    createOrderedList,
    createQrCode,
    createNotifyPreview,
    openSidecar,
    bindSecretField,
    readSecretField,
    clearSecretField,
    SECRET_KEPT,
    placeholderHint,
    bindPlaceholders,
    subscribeWebPush,
    unsubscribeWebPush,
    webPushState,
    webPushLabel,
    startUserPage,
    startWebClientShell,
    showUserPageView,
    setUserPageMessage,
    loadTranslations,
    createTranslations
} from './configurationpage?name=necto_stolon.js';
