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
    h,
    createShared as baseCreateShared,
    createConfigPage,
    createToolApp,
    setToolTitle,
    refreshToolConfig,
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
    createSubTabs,
    openSidecar,
    promptSidecar,
    createMediaCard,
    createDefinitionList,
    createBanner,
    createSortableCardList,
    createTimeline,
    escapeHtml,
    badgeHtml,
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
    createBanner, createWorldMap, linkWorldMap, createStatusCardGrid, h, bindTileToSwitch, createResetRow,
    createResetGroup, createSaveBars, createInputField, createSelectField, createTextareaField, createCheckboxField,
    createSecretField, readNumber, readLines, createWebPushToggle, WEB_SPINNER, webSelectOptions, webSettingsCheckbox,
    copyToClipboard, createSignIn, createUnauthorized, createNotFound, showScreen, BADGE_STATES, stopWebClientShell, webConfirm,
    openWebDialog, webEmptyState, badgeHtml, createMatrixGrid, createUserChip, createAvatar, createKeyValueEditor
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

// rowBadge
// Builds the state badge at the end of a record row, so every tool shows a row's state in the same place.
//
// Inputs
// state [object]: label and color, where color is a status color name.
//
// Output
// [HTMLElement]: The badge, for the row's aside.
export function rowBadge(state) {
    var badge = h('span', { 'data-role': 'row-state' });
    badge.innerHTML = badgeHtml(state.label, state.color);
    return badge;
}

// userImage
// Gives the address of a user's Jellyfin picture, or nothing for a user without one, so the avatar shows the person
// icon without asking for a picture that is not there.
//
// Inputs
// userId [string]: The user's id.
// tags [object]: Each user's picture tag by compact id, from usersImageTags, or null while they are not read.
//
// Output
// [string]: The address, or empty without a picture or a server connection.
export function userImage(userId, tags) {
    if (!userId || typeof ApiClient === 'undefined' || !ApiClient.getUrl) return '';
    var tag = tags ? tags[String(userId).replace(/-/g, '').toLowerCase()] : null;
    if (!tag) return '';
    return ApiClient.getUrl('Users/' + userId + '/Images/Primary', { height: 96, quality: 90, tag: tag });
}

// usersImageTags
// Reads which users have a Jellyfin picture.
//
// Output
// [Promise]: Resolves with each picture tag by compact user id, empty when the users cannot be read.
export function usersImageTags() {
    if (typeof ApiClient === 'undefined' || !ApiClient.getUsers) return Promise.resolve({});
    return Promise.resolve(ApiClient.getUsers()).then(function (users) {
        var tags = {};
        (users || []).forEach(function (u) { if (u && u.Id && u.PrimaryImageTag) tags[String(u.Id).replace(/-/g, '').toLowerCase()] = u.PrimaryImageTag; });
        return tags;
    }).catch(function () { return {}; });
}

// withAvatar
// Marks a timeline entry with its writer's Jellyfin picture, or the person icon for a writer without one.
//
// Inputs
// item [object]: The timeline entry.
// userId [string]: The writer's id.
// userName [string]: The writer's name, which a screen reader hears for the picture.
// tags [object]: Each user's picture tag by compact id, from usersImageTags, or null while they are not read.
//
// Output
// [object]: The entry, with avatar and avatarLabel, or icon.
export function withAvatar(item, userId, userName, tags) {
    var image = userImage(userId, tags);
    if (image) {
        item.avatar = image;
        item.avatarLabel = userName || '';
    } else {
        item.icon = 'person';
    }
    return item;
}

// isSecurePage
// Tells whether the page is open over HTTPS or on this machine, the only places a browser offers notifications.
//
// Output
// [boolean]: false only when the browser says the page is not a secure context.
export function isSecurePage() {
    return typeof globalThis.isSecureContext !== 'boolean' || globalThis.isSecureContext;
}

// toolName
// Names a Settings or Troubleshooting section that belongs to no tool, in the viewer's language once the strings load.
export { tabName as toolName };

// switchOn
// Builds a tool's visibleWhen, which reads the Enabled switch of its config section. A tool with a switch leaves the
// sidebar until the switch is on.
//
// Inputs
// section [string]: The config section that holds the switch, such as Requests.
//
// Output
// [function]: visibleWhen(config), true while the switch is on.
function switchOn(section) {
    return function (config) { return !!(config && config[section] && config[section].Enabled); };
}

// nectoTools
// Lists Necto's tools in the order the sidebar shows them, the dashboard and Groups first. A tool that turns on and
// off has visibleWhen, which matches ToolSwitches.All in the plugin, and a test checks the two agree.
//
// Output
// [object[]]: Each tool's key, name, icon, the registered page it shows, and visibleWhen for a tool with a switch.
export function nectoTools() {
    return [
        { key: 'dashboard', name: tabName('TabDashboard', 'Dashboard'), icon: 'dashboard', page: 'necto_dashboard' },
        { key: 'groups', name: tabName('TabGroups', 'Groups'), icon: 'groups', page: 'necto_groups' },
        { key: 'requests', name: tabName('TabRequests', 'Requests'), icon: 'movie_filter', page: 'necto_requests', visibleWhen: switchOn('Requests') },
        { key: 'issues', name: tabName('TabIssues', 'Issues'), icon: 'report_problem', page: 'necto_issues', visibleWhen: switchOn('Issues') },
        { key: 'invites', name: tabName('TabInvites', 'Invites'), icon: 'mail', page: 'necto_invites', visibleWhen: switchOn('InviteSettings') },
        { key: 'proxy', name: tabName('TabProxy', 'Proxy'), icon: 'security', page: 'necto_proxy', visibleWhen: switchOn('Proxy') },
        { key: 'ddns', name: tabName('TabDdns', 'DDNS'), icon: 'dns', page: 'necto_ddns', visibleWhen: switchOn('Ddns') },
        { key: 'wireguard', name: tabName('TabWireGuard', 'VPN'), icon: 'vpn_lock', page: 'necto_wireguard', visibleWhen: switchOn('WireGuard') },
        { key: 'scripts', name: tabName('TabScripts', 'Scripts'), icon: 'code', page: 'necto_scripts', visibleWhen: switchOn('Scripts') },
        { key: 'pages', name: tabName('TabPages', 'Pages'), icon: 'web', page: 'necto_pages', visibleWhen: switchOn('CustomPages') },
        { key: 'storage', name: tabName('TabStorage', 'Storage'), icon: 'storage', page: 'necto_storage', visibleWhen: switchOn('Storage') },
        { key: 'settings', name: tabName('Settings', 'Settings'), icon: 'settings', page: 'necto_settings' },
        { key: 'troubleshooting', name: tabName('HeaderTroubleshooting', 'Troubleshooting'), icon: 'bug_report', page: 'necto_troubleshooting' }
    ];
}

// toolIsOn
// Reads whether a tool is on in a config, by its visibleWhen. A tool with no switch is always on.
//
// Inputs
// config [object]: The plugin configuration.
// key [string]: The tool's key.
//
// Output
// [boolean]: true while the tool is on.
export function toolIsOn(config, key) {
    var tool = nectoTools().find(function (entry) { return entry.key === key; });
    return !tool || !tool.visibleWhen || tool.visibleWhen(config);
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

// SYSTEM_SECTIONS
// The Settings and Troubleshooting sections that apply across Necto, in the order the System group shows them. Every
// other section belongs to one tool and goes under Services.
var SYSTEM_SECTIONS = ['groups', 'notifications', 'menulinks'];

// nectoSections
// Lists the sections Settings or Troubleshooting draws, the System ones first and then each tool's in sidebar order,
// each with the section it added and its group. A tool that added none is left out, as are the dashboard, Settings, and
// Troubleshooting. A section that belongs to no tool, such as Menu links, names its own name and icon.
//
// Inputs
// kind [string]: settings or troubleshooting.
//
// Output
// [object[]]: Each section's key, name, icon, section, and group, system or services.
export function nectoSections(kind) {
    var registered = sectionRegistry[kind] || {};
    var tools = nectoTools();
    var entry = function (key) {
        var tool = tools.find(function (x) { return x.key === key; });
        var section = registered[key];
        var name = tool ? tool.name : (typeof section.name === 'function' ? section.name() : section.name);
        return { key: key, name: name, icon: tool ? tool.icon : section.icon, section: section, group: SYSTEM_SECTIONS.indexOf(key) >= 0 ? 'system' : 'services' };
    };
    var system = SYSTEM_SECTIONS.filter(function (key) {
        return registered[key] && (tools.some(function (x) { return x.key === key; }) || registered[key].icon);
    });
    var services = tools.filter(function (tool) {
        return ['dashboard', 'settings', 'troubleshooting'].indexOf(tool.key) < 0 && SYSTEM_SECTIONS.indexOf(tool.key) < 0 && !!registered[tool.key];
    }).map(function (tool) { return tool.key; });
    return system.concat(services).map(entry);
}

// SECTION_GROUPS
// The heading and intro of each group of sections, by page.
var SECTION_GROUPS = {
    system: { title: 'LabelSystem', settings: 'HelpSettingsSystem', troubleshooting: 'HelpTroubleshootingSystem' },
    services: { title: 'HeaderServices', settings: 'HelpSettingsServices', troubleshooting: 'HelpTroubleshootingServices' }
};

// drawSections
// Draws a collapsible section per tool into a container, under the heading of its group, System or Services, with the
// content each tool's section fills.
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
    var group = null;
    return nectoSections(kind).map(function (tool) {
        // Each group opens with its heading and one line on what it holds.
        if (tool.group !== group) {
            group = tool.group;
            var heads = SECTION_GROUPS[group];
            container.appendChild(h('h3', { class: 'stolon-subsection-title', 'data-group': group, text: shared.t.tr(heads.title) }));
            container.appendChild(h('div', { class: 'fieldDescription', 'data-group-help': group, text: shared.t.tr(heads[kind]) }));
        }
        var content = h('div', { class: 'stolon-stack', 'data-role': 'section-content' });
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
// at once, so it catches the page's first show, reads the configuration for each tool's visibleWhen, and the sidebar
// names turn to the viewer's language once the strings arrive.
//
// Inputs
// view [HTMLElement]: The host page the dashboard passes to the controller.
//
// Output
// [object]: The tool app's handle.
export function createNectoApp(view) {
    var app = createToolApp(view, { storageKey: 'necto', name: 'Necto', pluginId: PLUGIN_ID, tools: nectoTools });
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
    // Tells the tool app the configuration changed, such as after a setting turns a tool on or off.
    bag.onConfigChanged = function (config) { refreshToolConfig(view, config); };
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
        return shared.apiRequest(route, method, data, { prefix: '' });
    };

    return shared;
}


// rowsPage
// Answers one page of a table from rows the page already holds, as a paged route would: the rows the search and the
// filters keep, then the page asked for.
//
// Inputs
// rows [object[]]: Every row.
// url [string]: The address the table asked for, such as rows?skip=0&take=50&search=phone&filter=failed.
// options [object]: text(row) gives what the search looks in, and keep(row, query) whether a row passes the
//   filters.
//
// Output
// [object]: { Items, TotalCount }.
export function rowsPage(rows, url, options) {
    var query = {};
    String(url || '').split('?').slice(1).join('?').split('&').forEach(function (pair) {
        if (!pair) return;
        var at = pair.indexOf('=');
        query[decodeURIComponent(at < 0 ? pair : pair.slice(0, at))] = decodeURIComponent((at < 0 ? '' : pair.slice(at + 1)).replace(/\+/g, ' '));
    });
    var opts = options || {};
    var search = String(query.search || '').trim().toLowerCase();
    var found = (rows || []).filter(function (row) {
        if (opts.keep && !opts.keep(row, query)) return false;
        return !search || String(opts.text ? opts.text(row) : '').toLowerCase().indexOf(search) >= 0;
    });
    var skip = Number(query.skip) || 0;
    var take = Number(query.take) || found.length;
    return { Items: found.slice(skip, skip + take), TotalCount: found.length };
}

// createRowsTable
// Builds Stolon's Paginated Table over rows the page reads itself, with the same look as every tool table: the search,
// the filters, a status badge column, and the empty text inside the frame. The table shows "Loading" until the first
// rows are set.
//
// Inputs
// shared [object]: The Necto helper bag.
// options [object]: The Paginated Table options, without containerId or endpoint, plus text(row) for the search and
//   keep(row, query) for the filters.
//
// Output
// [object]: element to place, setRows(rows) to draw them, table, and destroy().
export function createRowsTable(shared, options) {
    var rows = [];
    var loaded = false;
    var id = 'necto-rows-' + (++rowsTableSeq);
    var host = h('div', { id: id });
    var element = h('div', {}, [host]);
    var bag = Object.create(shared);
    bag.apiPrefix = '';
    bag.apiRequest = function (url) { return Promise.resolve(rowsPage(rows, url, options)); };
    var table = createPaginatedTable(element, bag, Object.assign({ pagination: { pageSize: 50 } }, options, { containerId: id, endpoint: 'rows' }));
    return {
        element: element,
        table: table,
        setRows: function (next) {
            rows = next || [];
            var run = loaded ? table.refresh() : table.load();
            loaded = true;
            return Promise.resolve(run).catch(function () { /* the rows are local, so nothing can fail */ });
        },
        destroy: function () { table.destroy(); }
    };
}

var rowsTableSeq = 0;

// createToolTabs
// Splits a tool page into sub-tabs, as Pages has. Everything already in the host moves into the first tab, so the
// page's main list keeps its place and ids. Each later tab holds the element it names, and its open runs the first
// time the tab is picked.
//
// Inputs
// host [HTMLElement]: The element whose children become the first tab.
// options [object]: label for the tab list, and tabs, each { id, label, element, open }. The first tab takes no
//   element.
//
// Output
// [object]: The Stolon sub-tabs handle.
export function createToolTabs(host, options) {
    var first = Array.prototype.slice.call(host.childNodes || host.children || []);
    var specs = {};
    options.tabs.forEach(function (tab) { specs[tab.id] = tab; });
    var tabs = createSubTabs({
        label: options.label,
        tabs: options.tabs.map(function (tab) { return { id: tab.id, label: tab.label }; }),
        render: function (tab, panel) {
            panel.classList.add('stolon-stack');
            var spec = specs[tab.id];
            if (spec === options.tabs[0]) first.forEach(function (node) { panel.appendChild(node); });
            else if (spec.element) panel.appendChild(spec.element);
            if (spec.open) spec.open();
        }
    });
    tabs.element.setAttribute('data-role', 'tool-tabs');
    host.appendChild(tabs.element);
    return tabs;
}
