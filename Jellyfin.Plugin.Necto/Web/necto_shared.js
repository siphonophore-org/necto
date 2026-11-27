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
    openDialog, promptSidecar, bindSidecar, createTagInput, createComboBox, adornField, createUserMultiSelector,
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


// TABLE_PAST
// The past tense of each bulk verb, as the result strings name it, such as InvitesBulkEnabledOne.
var TABLE_PAST = { enable: 'Enabled', disable: 'Disabled', remove: 'Removed', update: 'Updated', export: 'Exported', unban: 'Unbanned' };

// readTableQuery
// Reads a table's request, such as Groups/All?skip=0&take=50&search=kids&filter=On, into a query.
//
// Inputs
// url [string]: The address the table asked for.
//
// Output
// [object]: Each parameter by name, such as { skip, take, search, filter }.
export function readTableQuery(url) {
    var query = {};
    String(url || '').split('?').slice(1).join('?').split('&').forEach(function (pair) {
        if (!pair) return;
        var at = pair.indexOf('=');
        var key = decodeURIComponent(at < 0 ? pair : pair.slice(0, at));
        query[key] = decodeURIComponent((at < 0 ? '' : pair.slice(at + 1)).replace(/\+/g, ' '));
    });
    return query;
}

// tablePageOf
// Answers one page of a table from rows the page already holds, as a table route would: the rows every filter keeps
// whose text holds the search, then the page asked for.
//
// Inputs
// rows [object[]]: Every row.
// query [object]: skip, take, search, and each filter's value by name, from readTableQuery.
// textOf [function]: textOf(row) gives the text the search looks in.
// filters [object[]]: Each filter's name and test(row, value). A value of All or empty keeps every row.
//
// Output
// [object]: { Items, TotalCount }.
export function tablePageOf(rows, query, textOf, filters) {
    var q = String(query.search || '').trim().toLowerCase();
    var found = (rows || []).filter(function (row) {
        var kept = (filters || []).every(function (f) {
            var value = query[f.name || 'filter'];
            return !value || value === 'All' || !f.test || f.test(row, value);
        });
        return kept && (!q || String(textOf(row) || '').toLowerCase().indexOf(q) >= 0);
    });
    var skip = Number(query.skip) || 0;
    var take = Number(query.take) || found.length;
    return { Items: found.slice(skip, skip + take), TotalCount: found.length };
}

// tableStatusHtml
// Draws a row's status cell: its one state badge, then at most two icon buttons, side by side.
//
// Inputs
// badge [object]: label and color of the state badge, or none.
// buttons [object[]]: action, icon, and label for each button, with title for its tooltip, disabled, and destructive,
//   or none. Only the first two are drawn.
// id [string]: The row's id, which each button carries.
//
// Output
// [string]: The cell's HTML.
export function tableStatusHtml(badge, buttons, id) {
    var html = badge ? '<span data-role="row-state">' + badgeHtml(badge.label, badge.color) + '</span>' : '';
    (buttons || []).slice(0, 2).forEach(function (b) {
        html += '<button is="emby-button" type="button" class="stolon-row-btn' + (b.destructive ? ' stolon-destructive' : '') + '" data-row-action="' + escapeHtml(b.action) + '" data-id="' + escapeHtml(id) + '" title="' + escapeHtml(b.title || b.label) + '" aria-label="' + escapeHtml(b.label) + '"' + (b.disabled ? ' disabled' : '') + '>'
            + '<span class="material-icons" aria-hidden="true">' + escapeHtml(b.icon) + '</span></button>';
    });
    return '<div class="necto-row-aside">' + html + '</div>';
}

// tableInfoHtml
// Draws a row's main cell: its icon tile, when it has no thumbnail, then its name over its one secondary line.
//
// Inputs
// title [string]: The name.
// line [string]: The line under it, or empty.
// icon [string]: The Material icon of its tile, or empty for none.
//
// Output
// [string]: The cell's HTML.
export function tableInfoHtml(title, line, icon) {
    return (icon ? '<span class="stolon-thumb stolon-table-thumb stolon-round" data-role="row-icon"><span class="stolon-thumb-placeholder" aria-hidden="true"><span class="material-icons">' + escapeHtml(icon) + '</span></span></span>' : '')
        + '<div class="stolon-table-item-info"><div class="stolon-table-item-title">' + escapeHtml(title) + '</div>'
        + (line ? '<div class="stolon-table-item-sub">' + escapeHtml(line) + '</div>' : '')
        + '</div>';
}

// tableLine
// Joins the parts of a row's secondary line, leaving out the empty ones.
//
// Inputs
// parts [string[]]: The parts.
//
// Output
// [string]: The line, such as "Cloudflare · home.example.com".
export function tableLine(parts) {
    return (parts || []).filter(Boolean).join(' · ');
}

// createToolTable
// Builds a tool's table of records in the one layout every tool uses: search first, then the filters, then the tool's
// own actions such as Add, at the end of the toolbar. Picked rows show the bulk bar with Select all, the count, the
// bulk actions, and Reload at its end. A table with no bulk actions picks no rows and ends its toolbar with Reload. Each row has an optional thumbnail, its name over its lines, and a status cell
// with its badges and icon buttons. A row opens with onRowClick. The empty and no match lines show inside the table.
// The rows come from a route that takes skip, take, search, and each filter, or from rows the page already holds.
//
// Inputs
// view [HTMLElement]: The tool's view.
// shared [object]: The Necto helper bag.
// o [object]:
//   containerId : the id of the element the table fills [required]
//   endpoint    : the route, such as Invites/All, or rows, a function that gives every row, for rows the page holds
//   idKey       : a row's id, as a property name or a function (default Id)
//   search      : the search box's placeholder, such as Search invites
//   filters     : [{ name, label, options, initialValue, onChange(value), test(row, value) }], whose first choice
//                 is All, which means no filter. name is the query parameter, filter by default. test filters rows
//                 the page holds.
//   textOf      : textOf(row) gives the text the search looks in, for rows the page holds (default name and lines)
//   empty       : { message, filteredMessage }, such as No invites yet. and No invites match. [required]
//   title       : title(row) gives the row's name [required]
//   lines       : lines(row) gives the parts of the one secondary line under the name, joined with a dot
//   image       : image(row) gives the thumbnail's address, with imageShape, for a table with thumbnails
//   icon        : icon(row) gives the Material icon of the row's tile, for a table without thumbnails
//   status      : status(row) gives the one state badge of the status cell
//   buttons     : buttons(row) gives at most two icon buttons after the badge, each { action, icon, label, title,
//                 disabled, destructive }
//   onButton    : onButton(action, row, button) runs when one is chosen
//   add         : [{ id, icon, text, variant, onClick }] for the end of the toolbar. variant is primary by default
//   bulk        : [{ verb, text, variant }] for the bulk bar, and onBulk(verb, rows) runs when one is chosen
//   onRowClick, onReload, rowClass, pageSize (default 50)
//
// Output
// [object]: The paginated table's handle, with addButton(id) for a toolbar action, getRowById(id), setNone(true)
//   while the tool has no records at all, so the table says No X yet. whatever the filter, and destroy, which also
//   empties the container.
export function createToolTable(view, shared, o) {
    var t = shared.t;
    var bag = Object.create(shared);
    bag.apiPrefix = '';
    var filters = (o.filters || []).map(function (f) {
        var name = f.name || 'filter';
        return {
            name: name,
            label: f.label,
            options: f.options || [],
            allValue: 'All',
            initialValue: f.initialValue || 'All',
            onChange: f.onChange,
            buildParams: function (value) { var p = {}; p[name] = value === 'All' ? null : value; return p; }
        };
    });
    if (o.rows) {
        var textOf = o.textOf || function (row) { return [o.title(row)].concat(o.lines ? o.lines(row) : []).join(' '); };
        bag.apiRequest = function (url) {
            return Promise.resolve(tablePageOf(o.rows(), readTableQuery(url), textOf, o.filters || []));
        };
    }
    var bulk = o.bulk || [];
    var idKey = o.idKey || 'Id';
    var idOfRow = function (row) { return String(typeof idKey === 'function' ? idKey(row) : row[idKey]); };
    var columns = [{
        key: 'Name', type: 'custom',
        className: o.image || o.icon ? 'stolon-table-cell-with-thumb' : '',
        image: o.image,
        imageShape: o.imageShape,
        render: function (row) { return tableInfoHtml(o.title(row), tableLine(o.lines ? o.lines(row) : []), o.icon && !o.image ? o.icon(row) : ''); }
    }];
    if (o.status || o.buttons) {
        columns.push({
            key: 'State', type: 'custom', className: 'stolon-table-cell-status',
            render: function (row) { return tableStatusHtml(o.status ? o.status(row) : null, o.buttons ? o.buttons(row) : null, idOfRow(row)); }
        });
    }
    var buttons = {};
    var table = createPaginatedTable(view, bag, {
        containerId: o.containerId,
        endpoint: o.endpoint || 'rows',
        selection: { enabled: bulk.length > 0, idKey: idKey, onSelectionChange: onSelection },
        search: { enabled: true, placeholder: o.search },
        filters: filters,
        pagination: { pageSize: o.pageSize || 50 },
        emptyState: o.empty,
        rowClass: o.rowClass,
        columns: columns,
        actions: { onRowClick: o.onRowClick, onReload: o.onReload }
    });

    // onSelection
    // Turns the bulk actions on while rows are picked.
    function onSelection(ids) {
        Object.keys(buttons).forEach(function (key) { if (buttons[key].hasAttribute('data-bulk')) buttons[key].disabled = !ids.length; });
    }

    var controls = table.getControlsContainer();
    (o.add || []).forEach(function (a) {
        var button = h('button', { is: 'emby-button', type: 'button', id: a.id, class: 'raised stolon-button stolon-' + (a.variant || 'primary') }, [
            a.icon ? h('span', { class: 'material-icons', 'aria-hidden': 'true', text: a.icon }) : null,
            h('span', { text: a.text })
        ]);
        button.addEventListener('click', function () { a.onClick(button); });
        buttons[a.id] = button;
        if (controls) controls.appendChild(button);
    });

    // A table with no bulk actions picks no rows, so Reload goes at the end of its toolbar instead of its bulk bar.
    if (!bulk.length && controls) {
        var reload = h('button', { is: 'emby-button', type: 'button', class: 'stolon-row-btn stolon-table-reload-btn', title: t.tr('Refresh'), 'aria-label': t.tr('Refresh') }, [
            h('span', { class: 'material-icons', 'aria-hidden': 'true', text: 'refresh' })
        ]);
        reload.addEventListener('click', function () {
            table.reload().then(function () { if (o.onReload) o.onReload(); }, function () { if (o.onReload) o.onReload(); });
        });
        buttons.reload = reload;
        controls.appendChild(reload);
    }

    var actions = table.getBulkActionsContainer();
    if (actions && bulk.length) {
        bulk.forEach(function (b) {
            var button = h('button', { is: 'emby-button', type: 'button', class: 'raised stolon-button stolon-' + (b.variant || 'secondary'), 'data-bulk': b.verb, disabled: true }, [h('span', { text: b.text })]);
            buttons['bulk-' + b.verb] = button;
            actions.appendChild(button);
        });
        actions.addEventListener('click', function (e) {
            var button = e.target.closest('[data-bulk]');
            if (button && !button.disabled && o.onBulk) o.onBulk(button.getAttribute('data-bulk'), table.getSelectedItems());
        });
    }

    var container = view.querySelector('#' + o.containerId);
    var rowById = function (id) { return table.getItems().filter(function (row) { return idOfRow(row) === String(id); })[0] || null; };
    var onClick = function (e) {
        var button = e.target.closest('[data-row-action]');
        if (!button || !o.onButton) return;
        e.stopPropagation();
        var row = rowById(button.getAttribute('data-id'));
        if (row) o.onButton(button.getAttribute('data-row-action'), row, button);
    };
    // The row's buttons answer before the row opens, so choosing one never opens the row too.
    if (container) container.addEventListener('click', onClick, true);
    // A tool with no records at all says so, as No X yet., even while a kept filter would say none match.
    var none = false;
    var paintNone = function () {
        var empty = container ? container.querySelector('.stolon-list-empty') : null;
        if (none && empty) empty.textContent = o.empty.message;
    };
    ['load', 'reload', 'refresh', 'setFilter', 'setSearch'].forEach(function (name) {
        var run = table[name];
        table[name] = function () {
            return run.apply(table, arguments).then(function (result) { paintNone(); return result; });
        };
    });
    // A filter whose choices arrive later is drawn then, so the tool's actions and Reload move back to the end.
    var setFilterOptions = table.setFilterOptions;
    table.setFilterOptions = function () {
        setFilterOptions.apply(table, arguments);
        if (!controls) return;
        (o.add || []).forEach(function (a) { controls.appendChild(buttons[a.id]); });
        if (buttons.reload) controls.appendChild(buttons.reload);
    };
    var destroy = table.destroy;
    table.setNone = function (value) { none = !!value; paintNone(); };
    table.addButton = function (id) { return buttons[id] || null; };
    table.getRowById = rowById;
    table.destroy = function () {
        destroy();
        if (container) {
            container.removeEventListener('click', onClick, true);
            container.innerHTML = '';
        }
    };
    return table;
}

// runBulk
// Runs a bulk action on the rows picked that take it, one after another, asking first before a removal, and says
// how many it changed. The result strings are named after a prefix, such as InvitesBulk for InvitesBulkEnabledOne,
// InvitesBulkFailedOne, and InvitesBulkSkippedEnableOne, each with a Many form. A row another verb skips is counted
// by InvitesBulkSkippedOne.
//
// Inputs
// shared [object]: The Necto helper bag.
// o [object]: verb, enable, disable, remove, update, export, or unban, rows, the rows picked, prefix, the strings' prefix,
//   call(row), which runs it on one row and returns a promise, fits(row), which tells whether a row takes it (by
//   default Enable skips the enabled rows and Disable the disabled ones, by enabled(row)), confirm, { text, title }
//   for the question before a removal, and report(text, isError).
//
// Output
// [Promise]: Resolves with how many it changed, or null when the question was turned down.
export function runBulk(shared, o) {
    var t = shared.t;
    var rows = o.rows || [];
    if (!rows.length) return Promise.resolve(0);
    var enabled = o.enabled || function (row) { return !!row.Enabled; };
    var fits = o.fits || function (row) {
        if (o.verb === 'enable') return !enabled(row);
        if (o.verb === 'disable') return enabled(row);
        return true;
    };
    var picked = rows.filter(fits);
    var ask = o.confirm
        ? confirmDialog(o.confirm.text, { title: o.confirm.title, confirmText: o.confirm.button || t.tr('ButtonRemove'), destructive: o.verb === 'remove' })
        : Promise.resolve(true);
    return ask.then(function (ok) {
        if (!ok) return null;
        if (o.report) o.report(t.tr('TextWorking'), false);
        var done = 0;
        var failed = 0;
        return picked.reduce(function (chain, row) {
            return chain.then(function () {
                return Promise.resolve().then(function () { return o.call(row); }).then(function () { done += 1; }, function () { failed += 1; });
            });
        }, Promise.resolve()).then(function () {
            var p = o.prefix;
            var past = TABLE_PAST[o.verb] || 'Updated';
            var text = [t.plural(done, p + past + 'One', p + past + 'Many')];
            if (failed) text.push(t.plural(failed, p + 'FailedOne', p + 'FailedMany'));
            var skipped = rows.length - picked.length;
            if (skipped) {
                var kind = o.verb === 'enable' ? 'Enable' : o.verb === 'disable' ? 'Disable' : '';
                text.push(t.plural(skipped, p + 'Skipped' + kind + 'One', p + 'Skipped' + kind + 'Many'));
            }
            if (o.report) o.report(text.join(' '), failed > 0);
            return done;
        });
    });
}
