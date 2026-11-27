// necto_shared.js
// Wraps the Stolon shared bundle for the Necto pages. It lists the tools, builds the tool app, and wires each
// tool's page through Stolon's page lifecycle with the plugin's strings.
//
// Necto is one page, necto, whose sidebar lists every tool, and each tool is its own registered page that the
// tool app swaps in place. Every page loads the plugin's strings once from TRANSLATIONS_URL, and a page's bind
// and load run once it has them, so the marked markup is in the viewer's language.
//
// Settings and Troubleshooting draw one section per tool from a registry. A tool adds its section with
// registerSection from its own sections module.

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
    createCopyField,
    createPaginatedTable,
    openSidecar,
    promptSidecar,
    createMediaCard,
    createDefinitionList,
    createBanner,
    createSortableCardList,
    createTimeline,
    escapeHtml,
    SECRET_KEPT
} from './configurationpage?name=necto_stolon.js';

// Re-exports
// The Stolon builders a tool page uses, so a page takes everything from this one module.
export { openTool, confirmDialog, openEditorSidecar, createRecordList, createCollapsibleSection, createRepeater, createChipSelect, createCheckboxList, createSaveBar, createCopyField };
// The Stolon builders the tools draw with.
export {
    createUsageMeter, createPieChart, createSpinner, createPillGroup, createDefinitionList, openSidecar,
    createCheckbox, createChoiceGroup, createSliderField, escapeHtml, createSubTabs, createFilePicker, SECRET_KEPT,
    openDialog, promptSidecar, createTagInput, createComboBox, adornField, createUserMultiSelector,
    createSortableCardList, createTimeline, createDataTable, createPaginatedTable, createDurationField,
    createBarChart, createQrCode, createNumberUnitField, createInheritField, generateGuid, downloadJson, pickJsonFile,
    setFieldState, createTranslations as createToolTranslations, createAccessSchedule, createOrderedList,
    createNotifyPreview, bindSecretField, readSecretField, clearSecretField, placeholderHint, bindPlaceholders,
    subscribeWebPush, unsubscribeWebPush, webPushState, webPushLabel, startUserPage, startWebClientShell,
    showUserPageView, setUserPageMessage, loadTranslations, createTranslations, COUNTRY_CODES, createMediaCard,
    createBanner, createWorldMap, linkWorldMap, createStatusCardGrid
} from './configurationpage?name=necto_stolon.js';

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

// toolName
// Names a Settings or Troubleshooting section that belongs to no tool, in the viewer's language once the strings load.
export { tabName as toolName };

// nectoTools
// Lists Necto's tools in the order the sidebar shows them, the dashboard and Groups first.
//
// Output
// [object[]]: Each tool's key, name, icon, and the registered page it shows.
export function nectoTools() {
    return [
        { key: 'dashboard', name: tabName('TabDashboard', 'Dashboard'), icon: 'dashboard', page: 'necto_dashboard' },
        { key: 'groups', name: tabName('TabGroups', 'Groups'), icon: 'groups', page: 'necto_groups' },
        { key: 'requests', name: tabName('TabRequests', 'Requests'), icon: 'movie_filter', page: 'necto_requests' },
        { key: 'issues', name: tabName('TabIssues', 'Issues'), icon: 'report_problem', page: 'necto_issues' },
        { key: 'invites', name: tabName('TabInvites', 'Invites'), icon: 'mail', page: 'necto_invites' },
        { key: 'proxy', name: tabName('TabProxy', 'Proxy'), icon: 'security', page: 'necto_proxy' },
        { key: 'ddns', name: tabName('TabDdns', 'DDNS'), icon: 'dns', page: 'necto_ddns' },
        { key: 'wireguard', name: tabName('TabWireGuard', 'VPN'), icon: 'vpn_lock', page: 'necto_wireguard' },
        { key: 'scripts', name: tabName('TabScripts', 'Scripts'), icon: 'code', page: 'necto_scripts' },
        { key: 'pages', name: tabName('TabPages', 'Pages'), icon: 'web', page: 'necto_pages' },
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
    // The is attribute, set below, lets the web client upgrade the element once it is in the page. Upgrading at
    // creation breaks elements that need a parent, such as emby-input.
    var node = document.createElement(tag);
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

// TOOL_SWITCHES
// The tools that turn on and off, by key, each with the config section that holds its Enabled switch, in sidebar
// order. It matches ToolSwitches.All in the plugin, and a test checks the two agree. Every other tool is always on.
export var TOOL_SWITCHES = {
    requests: 'Requests',
    issues: 'Issues',
    invites: 'InviteSettings',
    proxy: 'Proxy',
    ddns: 'Ddns',
    wireguard: 'WireGuard',
    scripts: 'Scripts',
    pages: 'CustomPages',
    storage: 'Storage'
};

// toolIsOn
// Reads whether a tool is on in a config. A tool with no switch is always on.
//
// Inputs
// config [object]: The plugin configuration.
// key [string]: The tool's key.
//
// Output
// [boolean]: true while the tool is on.
export function toolIsOn(config, key) {
    var section = TOOL_SWITCHES[key];
    if (!section) return true;
    return !!(config && config[section] && config[section].Enabled);
}

// The tools turned off in Settings, by key, which leave the sidebar.
var toolsOff = {};

// setToolsOff
// Reads which tools are turned off in a config, so the sidebar leaves them out. Each tool in TOOL_SWITCHES is off
// until its switch is on.
//
// Inputs
// config [object]: The plugin configuration.
//
// Output
// [object]: The tools turned off, by key.
export function setToolsOff(config) {
    toolsOff = {};
    Object.keys(TOOL_SWITCHES).forEach(function (key) { toolsOff[key] = !toolIsOn(config, key); });
    return toolsOff;
}

// isToolOff
// Says whether a tool was off in the config setToolsOff last read.
//
// Inputs
// key [string]: The tool's key.
//
// Output
// [boolean]: true while the tool is off.
export function isToolOff(key) {
    return !!toolsOff[key];
}

// enabledSwitch
// Builds a tool's Enabled switch, the first checkbox of its Settings section, whose tile follows it.
//
// Inputs
// label [string]: The checkbox's label.
// help [string|HTMLElement]: The sentence under it, or a description already built.
//
// Output
// [object]: element is the field, and input the checkbox.
export function enabledSwitch(label, help) {
    var input = el('input', { type: 'checkbox', is: 'emby-checkbox', 'data-field': 'Enabled' });
    var element = el('div', { class: 'checkboxContainer checkboxContainer-withDescription', 'data-role': 'enabled' }, [
        el('label', {}, [input, el('span', { text: label })]),
        typeof help === 'string' ? el('div', { class: 'fieldDescription checkboxFieldDescription', text: help }) : help
    ]);
    return { element: element, input: input };
}

// resetRow
// Builds one row of a reset group: its icon tile, what it does, one sentence on what follows, and its button. The
// tile is red for a row that loses data, and gray while there is nothing to reset.
//
// Inputs
// opts [object]: icon is the Material icon that names the action. label is what the row does, and help what follows
//   from it. button is the button's label, or a button already built. action is the button's data-action.
//   destructive marks a row that loses data, and empty one with nothing to reset, whose tile is gray. [optional]
//
// Output
// [HTMLElement]: The row.
export function resetRow(opts) {
    var button = opts.button && typeof opts.button === 'object'
        ? opts.button
        : el('button', { is: 'emby-button', type: 'button', class: 'raised stolon-button ' + (opts.destructive ? 'stolon-destructive' : 'stolon-secondary') + ' stolon-small', 'data-action': opts.action }, [el('span', { text: opts.button })]);
    return el('div', { class: 'stolon-reset-row' }, [
        el('span', { class: 'stolon-collapsible-tile' + (opts.empty ? ' stolon-gray' : opts.destructive ? ' stolon-red' : ''), 'aria-hidden': 'true' }, [
            el('span', { class: 'material-icons', text: opts.icon })
        ]),
        el('div', { class: 'stolon-reset-info' }, [el('div', { text: opts.label }), el('div', { class: 'fieldDescription', text: opts.help })]),
        button
    ]);
}

// sidebarTools
// Lists the tools the sidebar shows, which are the tools not turned off in Settings.
//
// Output
// [object[]]: The tools, as nectoTools lists them.
export function sidebarTools() {
    return nectoTools().filter(function (tool) { return !toolsOff[tool.key]; });
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
//   page. A Settings handle has load(config) and apply(config), which writes its fields into a config, or save()
//   in place of apply for a tool that saves through its own route. A
//   Troubleshooting handle has load(), and either may have destroy().
export function registerSection(kind, key, section) {
    if (!sectionRegistry[kind]) throw new Error('No section kind ' + kind);
    sectionRegistry[kind][key] = section;
}

// nectoSections
// Lists the tools Settings or Troubleshooting draws a section for, in sidebar order, each with the section it
// added. A tool that added none is left out, as are the dashboard, Settings, and Troubleshooting. A section that
// belongs to no tool, such as Menu links, names its own name and icon and follows the tools.
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
    }).filter(function (tool) {
        return !!registered[tool.key];
    }).map(function (tool) {
        return { key: tool.key, name: tool.name, icon: tool.icon, section: registered[tool.key] };
    }).concat(Object.keys(registered).filter(function (key) {
        return !nectoTools().some(function (tool) { return tool.key === key; }) && registered[key].icon;
    }).map(function (key) {
        var section = registered[key];
        return { key: key, name: typeof section.name === 'function' ? section.name() : section.name, icon: section.icon, section: section };
    }));
}

// drawSections
// Draws a collapsible section per tool into a container, with the content each tool's section fills.
//
// Inputs
// container [HTMLElement]: Where the sections go.
// kind [string]: settings or troubleshooting.
// shared [object]: The Necto helper bag.
// page [object]: The page handle.
// addFooter [function]: Called with each filled section's content and handle, to add what ends it, such as a Save Bar. [optional]
//
// Output
// [object[]]: One entry per tool, with its key, collapsible, and handle.
export function drawSections(container, kind, shared, page, addFooter) {
    container.innerHTML = '';
    return nectoSections(kind).map(function (tool) {
        var content = el('div', { 'data-role': 'section-content' });
        var section = createCollapsibleSection({ title: tool.name, icon: tool.icon, collapsed: true, content: content });
        section.element.setAttribute('data-tool', tool.key);
        container.appendChild(section.element);
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
    var app = createToolApp(view, { storageKey: 'necto', name: 'Necto', tools: sidebarTools });
    if (typeof ApiClient !== 'undefined' && ApiClient.getPluginConfiguration) {
        ApiClient.getPluginConfiguration(PLUGIN_ID).then(function (config) {
            setToolsOff(config);
            app.refreshTools();
        }, function () { /* every tool stays */ });
    }
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
            followToolsOff(view);
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

// followToolsOff
// Reads again which tools are on each time a tool shows, so a tool turned on or off elsewhere, such as from another
// browser, joins or leaves the sidebar without a reload.
//
// Inputs
// view [HTMLElement]: The tool's view.
function followToolsOff(view) {
    if (typeof ApiClient === 'undefined' || !ApiClient.getPluginConfiguration) return;
    var was = JSON.stringify(toolsOff);
    ApiClient.getPluginConfiguration(PLUGIN_ID).then(function (config) {
        if (JSON.stringify(setToolsOff(config)) !== was) refreshTools(view);
    }, function () { /* the sidebar stays as it is */ });
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

