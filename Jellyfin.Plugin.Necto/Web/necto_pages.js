// necto_pages.js
// The Custom pages tool. It lists the pages, their images, and the stores pages write to, and edits each in a sidecar.

import * as nectoModule from './configurationpage?name=necto_shared.js';

var el = nectoModule.el;

// The tiers a page, an image, or a store's access takes, lowest first.
export var VISIBILITIES = ['Anonymous', 'User', 'Admin'];

// The read scopes of a store.
export var SCOPES = ['All', 'Own'];

// The retention choices a store offers, in days. 0 keeps records forever.
export var RETENTION_PRESETS = [0, 1, 7, 30, 90, 180, 365];

// The largest image the page uploads, the same as the server's limit.
export var MAX_ASSET_BYTES = 5 * 1024 * 1024;

// The most records a store may hold, and the longest retention, as the server allows.
export var RECORD_CEILING = 100000;
export var RETENTION_CEILING = 3650;

// The source panes of a page that is not a single file, by the field each edits.
var PANE = { html: 'Html', css: 'Css', js: 'Js' };

// Ids for fields, so two editors never share one.
var fieldSeq = 0;

// fieldId
// Makes an id for a field.
//
// Inputs
// name [string]: What the field is.
//
// Output
// [string]: An id no other field has.
function fieldId(name) {
    fieldSeq += 1;
    return 'necto-pages-' + name + '-' + fieldSeq;
}

// slugify
// Turns text into a slug of lower case letters, digits, dashes, and underscores.
//
// Inputs
// value [string]: The text.
//
// Output
// [string]: The slug, empty when nothing usable is left.
export function slugify(value) {
    return String(value || '')
        .toLowerCase()
        .replace(/[^a-z0-9-_]+/g, '-')
        .replace(/^-+|-+$/g, '');
}

// normalizeVisibility
// Reads a tier the server sent as a name or a number.
//
// Inputs
// value [string|number]: The tier.
// fallback [string]: The tier to use when it names none. Defaults to Anonymous.
//
// Output
// [string]: Anonymous, User, or Admin.
export function normalizeVisibility(value, fallback) {
    if (typeof value === 'number') return VISIBILITIES[value] || fallback || 'Anonymous';
    return VISIBILITIES.indexOf(value) >= 0 ? value : (fallback || 'Anonymous');
}

// normalizeScope
// Reads a store's read scope the server sent as a name or a number.
//
// Inputs
// value [string|number]: The scope.
//
// Output
// [string]: All or Own.
export function normalizeScope(value) {
    if (typeof value === 'number') return SCOPES[value] || 'All';
    return SCOPES.indexOf(value) >= 0 ? value : 'All';
}

// tierLabel
// Names a tier in the viewer's language.
//
// Inputs
// value [string]: The tier.
// t [object]: The strings.
//
// Output
// [string]: The tier's name.
export function tierLabel(value, t) {
    var v = normalizeVisibility(value);
    if (v === 'Admin') return t.tr('NotificationsChannelAdministrators');
    if (v === 'User') return t.tr('PagesTierUser');
    return t.tr('PagesTierAnyone');
}

// sanitizeAssetName
// Turns a file name into an image name of lower case letters, digits, dots, dashes, and underscores.
//
// Inputs
// name [string]: The file name.
//
// Output
// [string]: The image name, image when nothing usable is left.
export function sanitizeAssetName(name) {
    var clean = String(name || '').toLowerCase().replace(/[^a-z0-9._-]+/g, '-').replace(/^[-.]+|[-.]+$/g, '');
    return clean || 'image';
}

// base64Bytes
// Works out how many bytes a Base64 text holds.
//
// Inputs
// b64 [string]: The Base64 text.
//
// Output
// [number]: The byte count.
export function base64Bytes(b64) {
    if (!b64) return 0;
    var padding = b64.endsWith('==') ? 2 : (b64.endsWith('=') ? 1 : 0);
    return Math.floor(b64.length * 3 / 4) - padding;
}

// newPage
// Builds a page as Add page starts it.
//
// Output
// [object]: The page, published, for Anyone, with empty sources.
export function newPage() {
    return { Id: '', Slug: '', Title: '', Visibility: 'Anonymous', Html: '', Css: '', Js: '', Document: '', SingleFile: false, Unsandboxed: false, ApiRoutes: [], Enabled: true };
}

// newStore
// Builds a store as Add store starts it, with the record limit, retention, and notify the settings give a new store.
//
// Inputs
// settings [object]: The Custom pages settings.
//
// Output
// [object]: The store.
export function newStore(settings) {
    var s = settings || {};
    return {
        Name: '',
        Enabled: true,
        ReadAccess: 'Admin',
        ReadScope: 'All',
        WriteAccess: 'User',
        AllowEditOwn: false,
        MaxRecords: s.NewStoreMaxRecords > 0 ? s.NewStoreMaxRecords : 1000,
        RetentionDays: s.NewStoreRetentionDays > 0 ? s.NewStoreRetentionDays : 0,
        NotifyOnWrite: !!s.NewStoreNotifyOnWrite
    };
}

// pageCounts
// Counts the pages by tier, with unpublished pages on their own.
//
// Inputs
// pages [object[]]: The pages.
//
// Output
// [object]: anyone, user, admin, and unpublished counts.
export function pageCounts(pages) {
    var c = { anyone: 0, user: 0, admin: 0, unpublished: 0 };
    (pages || []).forEach(function (p) {
        if (p.Enabled === false) { c.unpublished++; return; }
        var v = normalizeVisibility(p.Visibility);
        if (v === 'User') c.user++;
        else if (v === 'Admin') c.admin++;
        else c.anyone++;
    });
    return c;
}

// pageItems
// Turns the pages into record list rows, each named by its slug with its title and tier.
//
// Inputs
// pages [object[]]: The pages.
// t [object]: The strings.
//
// Output
// [object[]]: The rows, each carrying its page's index.
export function pageItems(pages, t) {
    return (pages || []).map(function (p, index) {
        var tags = [{ label: tierLabel(p.Visibility, t), color: normalizeVisibility(p.Visibility) === 'Anonymous' ? 'green' : (normalizeVisibility(p.Visibility) === 'User' ? 'blue' : 'purple') }];
        if (p.Enabled === false) tags.push({ label: t.tr('PagesStatusUnpublished'), color: 'gray' });
        if (p.Unsandboxed || (p.ApiRoutes || []).length) tags.push({ label: t.tr('PagesTagSystemAccess'), color: 'orange' });
        return { id: String(index), index: index, title: '/pages/' + (p.Slug || ''), secondary: p.Title || '', tags: tags };
    });
}

// storeItems
// Turns the stores into record list rows, each with its access and, once known, its record count.
//
// Inputs
// stores [object[]]: The stores.
// counts [object]: Record counts by lower case store name.
// t [object]: The strings.
//
// Output
// [object[]]: The rows, each carrying its store's index.
export function storeItems(stores, counts, t) {
    return (stores || []).map(function (s, index) {
        var count = counts[(s.Name || '').toLowerCase()];
        var tags = [
            { label: t.format('PagesTagRead', tierLabel(normalizeVisibility(s.ReadAccess, 'Admin'), t)), color: 'blue' },
            { label: t.format('PagesTagWrite', tierLabel(normalizeVisibility(s.WriteAccess, 'User'), t)), color: normalizeVisibility(s.WriteAccess, 'User') === 'Anonymous' ? 'orange' : 'blue' }
        ];
        if (s.Enabled === false) tags.push({ label: t.tr('Disabled'), color: 'gray' });
        var secondary = typeof count === 'number' ? t.format('PagesUsageRecords', count, s.MaxRecords || 1000) : '';
        return { id: String(index), index: index, title: s.Name || '', secondary: secondary, tags: tags };
    });
}

// storeCounts
// Counts the stores, their records, and the ones open to writes from visitors who are not signed in.
//
// Inputs
// stores [object[]]: The stores.
// counts [object]: Record counts by lower case store name.
//
// Output
// [object]: stores, records, and open counts.
export function storeCounts(stores, counts) {
    var total = 0;
    var open = 0;
    (stores || []).forEach(function (s) {
        var c = counts[(s.Name || '').toLowerCase()];
        if (typeof c === 'number') total += c;
        if (normalizeVisibility(s.WriteAccess, 'User') === 'Anonymous' && s.Enabled !== false) open++;
    });
    return { stores: (stores || []).length, records: total, open: open };
}

// storeWarnings
// Works out which store notices show for the editor's choices.
//
// Inputs
// store [object]: ReadAccess, ReadScope, WriteAccess, and Enabled as the editor has them.
//
// Output
// [object]: anonymousWrite, scopeAllWrite, and showScope.
export function storeWarnings(store) {
    var read = normalizeVisibility(store.ReadAccess, 'Admin');
    var write = normalizeVisibility(store.WriteAccess, 'User');
    var enabled = store.Enabled !== false;
    return {
        anonymousWrite: enabled && write === 'Anonymous',
        // An Administrators store shows every reader everything, so scope narrows nobody.
        showScope: read !== 'Admin',
        // With All records, anybody who clears both tiers may change any record.
        scopeAllWrite: enabled && read !== 'Admin' && write !== 'Admin' && normalizeScope(store.ReadScope) === 'All'
    };
}

// readRetention
// Reads the retention the editor's choice and custom field give.
//
// Inputs
// choice [string]: The picked preset, or custom.
// custom [string]: The custom number of days.
//
// Output
// [number]: The retention in days, 0 to keep forever, at most RETENTION_CEILING.
export function readRetention(choice, custom) {
    if (choice !== 'custom') return parseInt(choice, 10) || 0;
    var days = parseInt(custom, 10);
    if (!days || days < 1) return 0;
    return Math.min(RETENTION_CEILING, days);
}

// readRecordLimit
// Reads a record limit from a field, within 1 and RECORD_CEILING.
//
// Inputs
// value [string]: The field's value.
//
// Output
// [number]: The limit, 1000 when it is not a number.
export function readRecordLimit(value) {
    return Math.min(RECORD_CEILING, Math.max(1, parseInt(value, 10) || 1000));
}

// pageProblem
// Finds the first reason a page cannot be saved among the others.
//
// Inputs
// page [object]: The edited page.
// pages [object[]]: Every page, the edited one at index.
// index [number]: The edited page's place, or -1 for a new one.
//
// Output
// [string|null]: The key of the problem, or null.
export function pageProblem(page, pages, index) {
    if (!page.Slug) return 'PagesErrorEnterSlug';
    var clash = (pages || []).some(function (other, i) {
        return i !== index && (other.Slug || '').toLowerCase() === page.Slug.toLowerCase();
    });
    if (clash) return 'PagesErrorSlugTaken';
    if (['asset', 'store', 'stores', 'configuration'].indexOf(page.Slug) >= 0) return 'PagesErrorSlugReservedHere';
    return null;
}

// storeProblem
// Finds the first reason a store cannot be saved among the others.
//
// Inputs
// store [object]: The edited store.
// stores [object[]]: Every store, the edited one at index.
// index [number]: The edited store's place, or -1 for a new one.
//
// Output
// [string|null]: The key of the problem, or null.
export function storeProblem(store, stores, index) {
    if (!store.Name) return 'PagesErrorEnterName';
    var clash = (stores || []).some(function (other, i) {
        return i !== index && (other.Name || '').toLowerCase() === store.Name.toLowerCase();
    });
    return clash ? 'PagesErrorStoreNameTaken' : null;
}

// serverOrigin
// Gives the server's address, for the addresses the editors show.
//
// Output
// [string]: The address with no trailing slash.
function serverOrigin() {
    var address = (typeof ApiClient !== 'undefined' && ApiClient.serverAddress)
        ? ApiClient.serverAddress()
        : (typeof window !== 'undefined' && window.location ? window.location.origin : '');
    return String(address || '').replace(/\/+$/, '');
}

// fieldRow
// Builds a labelled input with an optional description.
//
// Inputs
// kind [string]: input, select, or textarea.
// label [string]: The label.
// attrs [object]: The control's attributes.
// help [string]: The description under it. [optional]
//
// Output
// [object]: row, the container, and control, the field.
function fieldRow(kind, label, attrs, help) {
    var id = fieldId(attrs['data-field'] || kind);
    var control;
    if (kind === 'select') control = el('select', Object.assign({ is: 'emby-select', id: id }, attrs));
    else if (kind === 'textarea') control = el('textarea', Object.assign({ id: id }, attrs));
    else control = el('input', Object.assign({ is: 'emby-input', type: 'text', id: id }, attrs));
    var row = el('div', { class: kind === 'select' ? 'selectContainer' : 'inputContainer' }, [
        kind === 'textarea' ? null : el('label', { class: kind === 'select' ? 'selectLabel' : 'inputLabel inputLabelUnfocused', for: id, text: label }),
        control,
        help ? el('div', { class: 'fieldDescription', text: help }) : null
    ]);
    if (kind === 'textarea') control.setAttribute('aria-label', label);
    return { row: row, control: control };
}

// checkRow
// Builds a checkbox with its description.
//
// Inputs
// label [string]: The label.
// help [string]: The description. [optional]
// field [string]: The data-field name.
//
// Output
// [object]: row, the container, and control, the checkbox.
function checkRow(label, help, field) {
    var control = el('input', { type: 'checkbox', is: 'emby-checkbox', id: fieldId(field), 'data-field': field });
    var row = el('div', { class: 'checkboxContainer' + (help ? ' checkboxContainer-withDescription' : '') }, [
        el('label', {}, [control, el('span', { text: label })]),
        help ? el('div', { class: 'fieldDescription checkboxFieldDescription', text: help }) : null
    ]);
    return { row: row, control: control };
}

// tierOptions
// Fills a select with the tiers, the highest first, as the server names them.
//
// Inputs
// select [HTMLSelectElement]: The select.
// t [object]: The strings.
function tierOptions(select, t) {
    ['Admin', 'User', 'Anonymous'].forEach(function (v) {
        select.appendChild(el('option', { value: v, text: tierLabel(v, t) }));
    });
}

// setHidden
// Hides or shows a node.
function setHidden(node, hidden) {
    node.classList.toggle('stolon-hidden', !!hidden);
}

// createPageEditor
// Builds the body of a page's sidecar: its slug and address, title, tier, publishing, mode, system access and server
// side routes, and its source.
//
// Inputs
// page [object]: The page to edit, or a new one.
// shared [object]: The Necto helper bag.
//
// Output
// [object]: A handle with element, getValue, setStatus, and destroy. getValue returns the page with the editor's values.
export function createPageEditor(page, shared) {
    var t = shared.t;
    var draft = JSON.parse(JSON.stringify(page || newPage()));
    var currentPane = 'html';
    var single = !!draft.SingleFile;
    var status = el('div', { class: 'stolon-status-text', role: 'status', 'data-role': 'status' });
    var root = el('div', { 'data-role': 'page-editor' }, [status]);

    var slug = fieldRow('input', t.tr('PagesLabelSlug'), { 'data-field': 'Slug', autocomplete: 'off' });
    slug.control.value = draft.Slug || '';
    var url = nectoModule.createCopyField({ label: t.tr('PagesLabelUrl'), copyLabel: t.tr('PagesCopyUrl') });
    var urlRow = el('div', { class: 'inputContainer', 'data-role': 'url' }, [el('div', { class: 'inputLabel', text: t.tr('PagesLabelUrl') }), url.element]);
    var title = fieldRow('input', t.tr('LabelTitle'), { 'data-field': 'Title', autocomplete: 'off' });
    title.control.value = draft.Title || '';
    var visibility = fieldRow('select', t.tr('PagesLabelVisibility'), { 'data-field': 'Visibility' }, t.tr('PagesVisibilityHelp'));
    tierOptions(visibility.control, t);
    visibility.control.value = normalizeVisibility(draft.Visibility);
    var groupsNote = el('div', { class: 'stolon-info', 'data-role': 'groups-note', text: t.tr('PagesGroupsNote') });
    var published = checkRow(t.tr('PagesLabelPublished'), t.tr('PagesPublishedHelp'), 'Enabled');
    published.control.checked = draft.Enabled !== false;
    var singleFile = checkRow(t.tr('PagesLabelSingleFile'), t.tr('PagesSingleFileHelp'), 'SingleFile');
    singleFile.control.checked = single;
    var system = checkRow(t.tr('PagesLabelAllowSystemAccess'), t.tr('PagesAllowSystemAccessHelp'), 'Unsandboxed');
    system.control.checked = !!draft.Unsandboxed || (draft.ApiRoutes || []).length > 0;

    // The stored password never comes to the browser. A stored one shows as SECRET_KEPT, which the server keeps.
    var routes = nectoModule.createRepeater({
        fields: [
            { key: 'Name', label: t.tr('LabelName'), placeholder: 'name' },
            { key: 'Url', label: t.tr('PagesLabelTargetUrl'), placeholder: 'http://192.168.1.10:8080/path', width: '2fr' },
            { key: 'Username', label: t.tr('LabelUsername'), placeholder: t.tr('PagesPlaceholderOptional') },
            { key: 'Password', label: t.tr('LabelPassword'), type: 'secret', placeholder: t.tr('PagesPlaceholderOptional'), clearable: true, clearText: t.tr('PagesButtonClearPassword') }
        ],
        value: (draft.ApiRoutes || []).map(function (r) {
            return { Name: r.Name || '', Url: r.Url || '', Username: r.Username || '', Password: r.Password ? nectoModule.SECRET_KEPT : '' };
        }),
        addText: t.tr('PagesButtonAddRoute'),
        rowName: t.tr('PagesRowRoute'),
        emptyText: t.tr('PagesEmptyRoutes')
    });
    var routesHelp = el('div', { class: 'fieldDescription' });
    fillWithCode(routesHelp, t.tr('PagesRoutesHelp'), ["serverFetch('name', options)"]);
    var routesRow = el('div', { class: 'stolon-indent', 'data-role': 'routes' }, [
        el('h3', { class: 'stolon-subsection-title', text: t.tr('PagesHeaderRoutes') }),
        routesHelp,
        routes.element
    ]);

    var source = fieldRow('select', t.tr('LabelSource'), { 'data-field': 'Source' });
    [['html', 'HTML'], ['css', 'CSS'], ['js', 'JavaScript']].forEach(function (o) {
        source.control.appendChild(el('option', { value: o[0], text: o[1] }));
    });
    var code = el('textarea', { class: 'stolon-code', spellcheck: 'false', 'data-field': 'Code', 'aria-label': t.tr('PagesLabelPageSource'), rows: '16' });
    var hint = el('div', { class: 'fieldDescription', 'data-role': 'hint' });
    var codeRow = el('div', { class: 'inputContainer' }, [code, hint]);

    [slug.row, urlRow, title.row, visibility.row, groupsNote, published.row, singleFile.row, system.row, routesRow, source.row, codeRow]
        .forEach(function (node) { root.appendChild(node); });

    // field
    // Names the page field the source box shows.
    function field() { return single ? 'Document' : PANE[currentPane]; }

    // paneHint
    // Gives the hint under the source box for what it shows.
    function paneHint() {
        if (single) return t.tr('PagesHintSingleFile');
        if (currentPane === 'css') return t.tr('PagesHintCss');
        if (currentPane === 'js') return t.tr('PagesHintJs');
        return t.tr('PagesHintHtml');
    }

    // loadSource
    // Shows the source of the pane in the box.
    function loadSource() {
        code.value = draft[field()] || '';
        hint.textContent = paneHint();
    }

    // flushSource
    // Keeps what the box holds in the draft.
    function flushSource() {
        draft[field()] = code.value;
    }

    // reflect
    // Shows the address, the groups note, the routes, and the source picker for the editor's choices.
    function reflect() {
        var s = slugify(slug.control.value);
        url.setValue(s ? serverOrigin() + '/pages/' + s : '');
        setHidden(urlRow, !s);
        setHidden(groupsNote, visibility.control.value !== 'User');
        setHidden(routesRow, !system.control.checked);
        setHidden(source.row, single);
    }

    slug.control.addEventListener('input', reflect);
    visibility.control.addEventListener('change', reflect);
    system.control.addEventListener('change', reflect);
    singleFile.control.addEventListener('change', function () {
        flushSource();
        single = singleFile.control.checked;
        loadSource();
        reflect();
    });
    source.control.addEventListener('change', function () {
        flushSource();
        currentPane = source.control.value;
        loadSource();
    });
    loadSource();
    reflect();

    return {
        element: root,
        getValue: function () {
            flushSource();
            var out = JSON.parse(JSON.stringify(draft));
            out.Slug = slugify(slug.control.value);
            out.Title = title.control.value.trim();
            out.Visibility = visibility.control.value;
            out.Enabled = !!published.control.checked;
            out.SingleFile = single;
            out.Unsandboxed = !!system.control.checked;
            out.ApiRoutes = system.control.checked ? routes.getValue().map(function (r) {
                return { Name: String(r.Name || '').trim(), Url: String(r.Url || '').trim(), Username: r.Username || '', Password: r.Password || '' };
            }).filter(function (r) { return r.Name || r.Url; }) : [];
            return out;
        },
        setStatus: function (message, isError) { shared.setStatus(status, message, !!isError); },
        destroy: function () { routes.destroy(); url.destroy(); }
    };
}

// createStoreEditor
// Builds the body of a store's sidecar: its name and address, read and write tiers, scope, edits, limit, retention,
// notifying, and how full it is.
//
// Inputs
// store [object]: The store to edit, or a new one.
// count [number]: How many records it holds, or undefined when not known.
// shared [object]: The Necto helper bag.
//
// Output
// [object]: A handle with element, getValue, setStatus, and destroy.
export function createStoreEditor(store, count, shared) {
    var t = shared.t;
    var draft = JSON.parse(JSON.stringify(store));
    var status = el('div', { class: 'stolon-status-text', role: 'status', 'data-role': 'status' });
    var root = el('div', { 'data-role': 'store-editor' }, [status]);

    var name = fieldRow('input', t.tr('LabelName'), { 'data-field': 'Name', autocomplete: 'off' });
    name.control.value = draft.Name || '';
    var url = nectoModule.createCopyField({ label: t.tr('PagesLabelUrl'), copyLabel: t.tr('PagesCopyUrl') });
    var urlRow = el('div', { class: 'inputContainer', 'data-role': 'url' }, [el('div', { class: 'inputLabel', text: t.tr('PagesLabelUrl') }), url.element]);
    var enabled = checkRow(t.tr('PagesLabelStoreEnabled'), t.tr('PagesStoreEnabledHelp'), 'Enabled');
    enabled.control.checked = draft.Enabled !== false;
    var read = fieldRow('select', t.tr('PagesLabelRead'), { 'data-field': 'ReadAccess' }, t.tr('PagesReadHelp'));
    tierOptions(read.control, t);
    read.control.value = normalizeVisibility(draft.ReadAccess, 'Admin');
    var scope = fieldRow('select', t.tr('PagesLabelScope'), { 'data-field': 'ReadScope' }, t.tr('PagesScopeHelp'));
    scope.control.appendChild(el('option', { value: 'All', text: t.tr('PagesScopeAll') }));
    scope.control.appendChild(el('option', { value: 'Own', text: t.tr('PagesScopeOwn') }));
    scope.control.value = normalizeScope(draft.ReadScope);
    var write = fieldRow('select', t.tr('PagesLabelWrite'), { 'data-field': 'WriteAccess' }, t.tr('PagesWriteHelp'));
    tierOptions(write.control, t);
    write.control.value = normalizeVisibility(draft.WriteAccess, 'User');
    var anonWarn = el('div', { class: 'stolon-warn', 'data-role': 'anonymous-write', text: t.tr('PagesWarnAnonymousWrite') });
    var scopeWarn = el('div', { class: 'stolon-warn', 'data-role': 'scope-all-write', text: t.tr('PagesWarnScopeAllWrite') });
    var editOwn = checkRow(t.tr('PagesLabelEditOwn'), t.tr('PagesEditOwnHelp'), 'AllowEditOwn');
    editOwn.control.checked = !!draft.AllowEditOwn;
    var limit = fieldRow('input', t.tr('PagesLabelRecordLimit'), { type: 'number', min: '1', max: String(RECORD_CEILING), 'data-field': 'MaxRecords' }, t.tr('PagesRecordLimitHelp'));
    limit.control.value = String(draft.MaxRecords || 1000);
    var retention = fieldRow('select', t.tr('PagesLabelRetention'), { 'data-field': 'Retention' }, t.tr('PagesRetentionHelp'));
    RETENTION_PRESETS.forEach(function (days) {
        var text = days === 0 ? t.tr('PagesRetentionKeep') : (days === 1 ? t.format('PagesRetentionDay', days) : t.format('PagesRetentionDays', days));
        retention.control.appendChild(el('option', { value: String(days), text: text }));
    });
    retention.control.appendChild(el('option', { value: 'custom', text: t.tr('Custom') }));
    var days = String(draft.RetentionDays || 0);
    var preset = RETENTION_PRESETS.map(String).indexOf(days) >= 0;
    retention.control.value = preset ? days : 'custom';
    var custom = fieldRow('input', t.tr('PagesLabelRetentionDays'), { type: 'number', min: '1', max: String(RETENTION_CEILING), 'data-field': 'RetentionDays' });
    custom.control.value = preset ? '' : days;
    var notify = checkRow(t.tr('PagesLabelNotifyAdmins'), t.tr('PagesNotifyHelp'), 'NotifyOnWrite');
    notify.control.checked = !!draft.NotifyOnWrite;
    var meter = nectoModule.createUsageMeter({ label: t.tr('PagesCardRecords'), usedText: t.tr('PagesUsageMeter') });
    var meterRow = el('div', { class: 'inputContainer', 'data-role': 'usage' }, [meter.element]);

    [name.row, urlRow, enabled.row, read.row, scope.row, write.row, anonWarn, scopeWarn, editOwn.row, limit.row, retention.row, custom.row, notify.row, meterRow]
        .forEach(function (node) { root.appendChild(node); });

    // reflect
    // Shows the address, the scope, the warnings, the custom retention, and the usage for the editor's choices.
    function reflect() {
        var n = slugify(name.control.value);
        url.setValue(n ? serverOrigin() + '/pages/store/' + n + '/read' : '');
        setHidden(urlRow, !n);
        var w = storeWarnings({ ReadAccess: read.control.value, WriteAccess: write.control.value, ReadScope: scope.control.value, Enabled: enabled.control.checked });
        setHidden(scope.row, !w.showScope);
        setHidden(anonWarn, !w.anonymousWrite);
        setHidden(scopeWarn, !w.scopeAllWrite);
        setHidden(custom.row, retention.control.value !== 'custom');
        // A store never saved has no records on the server to count.
        setHidden(meterRow, typeof count !== 'number');
        if (typeof count === 'number') meter.setValue({ used: count, total: readRecordLimit(limit.control.value) });
    }

    root.addEventListener('input', reflect);
    root.addEventListener('change', reflect);
    reflect();

    return {
        element: root,
        getValue: function () {
            var out = JSON.parse(JSON.stringify(draft));
            out.Name = slugify(name.control.value);
            out.Enabled = !!enabled.control.checked;
            out.ReadAccess = read.control.value;
            // An Administrators store shows every reader everything, so the scope it keeps is All.
            out.ReadScope = read.control.value === 'Admin' ? 'All' : scope.control.value;
            out.WriteAccess = write.control.value;
            out.AllowEditOwn = !!editOwn.control.checked;
            out.MaxRecords = readRecordLimit(limit.control.value);
            out.RetentionDays = readRetention(retention.control.value, custom.control.value);
            out.NotifyOnWrite = !!notify.control.checked;
            return out;
        },
        setStatus: function (message, isError) { shared.setStatus(status, message, !!isError); },
        destroy: function () { url.destroy(); meter.destroy(); }
    };
}

// fillWithCode
// Writes a sentence into a node with each {0}, {1} placeholder shown as code.
//
// Inputs
// node [HTMLElement]: The node.
// text [string]: The sentence.
// codes [string[]]: The code for each placeholder, in order.
export function fillWithCode(node, text, codes) {
    node.textContent = '';
    String(text).split(/(\{\d+\})/).forEach(function (part) {
        var m = /^\{(\d+)\}$/.exec(part);
        if (m && codes[+m[1]] !== undefined) node.appendChild(el('code', { text: codes[+m[1]] }));
        else if (part) node.appendChild(el('span', { text: part }));
    });
}

// readFileAsDataUrl
// Reads a file as a data: address.
//
// Inputs
// file [File]: The file.
//
// Output
// [Promise]: Resolves with the address.
function readFileAsDataUrl(file) {
    return new Promise(function (resolve, reject) {
        var reader = new FileReader();
        reader.onload = function () { resolve(reader.result); };
        reader.onerror = function () { reject(reader.error); };
        reader.readAsDataURL(file);
    });
}

// mergeAsset
// Adds an uploaded image to the list, or replaces one with the same name and keeps its tier.
//
// Inputs
// assets [object[]]: The images, changed in place.
// name [string]: The file name.
// dataUrl [string]: The file as a data: address.
//
// Output
// [boolean]: false when the address could not be read.
export function mergeAsset(assets, name, dataUrl) {
    var m = /^data:([^;]+);base64,(.*)$/.exec(String(dataUrl || ''));
    if (!m) return false;
    var clean = sanitizeAssetName(name);
    var entry = { Name: clean, ContentType: m[1], DataBase64: m[2], Visibility: 'Anonymous' };
    var existing = assets.findIndex(function (a) { return (a.Name || '').toLowerCase() === clean.toLowerCase(); });
    if (existing >= 0) {
        entry.Visibility = normalizeVisibility(assets[existing].Visibility);
        assets[existing] = entry;
    } else {
        assets.push(entry);
    }
    return true;
}

export default function (view) {
    'use strict';

    var shared = null;
    var page = null;
    var settings = { Pages: [], Assets: [], Stores: [] };
    var counts = {};
    var tabs = null;
    var pageList = null;
    var storeList = null;
    var picker = null;
    var assetFields = [];
    var panels = {};
    var loadSeq = 0;

    // report
    // Shows a result on the page's status line.
    function report(message, isError) {
        shared.setStatus('pagesStatus', message, !!isError, { timeout: 10000 });
    }

    // persist
    // Saves a change to the Custom pages settings into a fresh copy of the config, after the server says it would take it.
    //
    // Inputs
    // mutate [function]: Changes the settings it is given.
    // fallback [string]: The reason shown when the save fails with none of its own.
    //
    // Output
    // [Promise]: Resolves once saved, and rejects with an Error that holds the reason.
    function persist(mutate, fallback) {
        var refused = null;
        return shared.getConfig().then(function (fresh) {
            fresh.CustomPages = fresh.CustomPages || {};
            ['Pages', 'Assets', 'Stores'].forEach(function (k) { fresh.CustomPages[k] = fresh.CustomPages[k] || []; });
            mutate(fresh.CustomPages);
            // Jellyfin answers a refused save with no reason, so the server is asked first. The save checks again.
            return shared.request('Pages/Configuration/Check', 'POST', fresh).catch(function () { return null; }).then(function (result) {
                if (result && result.ok === false) {
                    refused = result.message || fallback;
                    throw new Error(refused);
                }
                return shared.saveConfig(fresh);
            });
        }).then(function () {
            return shared.getConfig();
        }).then(function (saved) {
            settings = saved.CustomPages || settings;
            drawAll();
        }, function () {
            throw new Error(refused || fallback);
        });
    }

    // loadCounts
    // Reads how many records each saved store holds, then redraws the stores.
    function loadCounts() {
        var mine = loadSeq;
        return Promise.all((settings.Stores || []).filter(function (s) { return s.Name; }).map(function (s) {
            var key = s.Name.toLowerCase();
            return shared.request('Pages/Stores/' + encodeURIComponent(s.Name) + '/Stats').then(function (stats) {
                counts[key] = stats && typeof stats.count === 'number' ? stats.count : 0;
            }).catch(function () { /* a disabled store answers 404 */ });
        })).then(function () {
            if (mine === loadSeq) drawStores();
        });
    }

    // drawPages
    // Draws the page counts and the list of pages.
    function drawPages() {
        var panel = panels.pages;
        if (!panel) return;
        var t = shared.t;
        var c = pageCounts(settings.Pages);
        shared.renderCards(panel.querySelector('[data-role="page-counts"]'), [
            { count: c.anyone, label: t.tr('PagesTierAnyone'), color: 'green' },
            { count: c.user, label: t.tr('PagesTierUser'), color: 'blue' },
            { count: c.admin, label: t.tr('NotificationsChannelAdministrators'), color: 'purple' },
            { count: c.unpublished, label: t.tr('PagesStatusUnpublished'), color: 'gray' }
        ]);
        var items = pageItems(settings.Pages, t);
        if (pageList) pageList.setItems(items);
        else {
            pageList = nectoModule.createRecordList({ items: items, label: t.tr('TabPages'), emptyText: t.tr('PagesEmptyPages'), onOpen: function (item) { openPage(item.index); } });
            panel.querySelector('[data-role="page-list"]').appendChild(pageList.element);
        }
    }

    // drawAssets
    // Draws the image counts and one row per image, with its path, tier, and Delete.
    function drawAssets() {
        var panel = panels.assets;
        if (!panel) return;
        var t = shared.t;
        var assets = settings.Assets || [];
        var images = 0;
        var other = 0;
        var bytes = 0;
        assets.forEach(function (a) {
            if ((a.ContentType || '').indexOf('image/') === 0) images++;
            else other++;
            bytes += base64Bytes(a.DataBase64);
        });
        shared.renderCards(panel.querySelector('[data-role="asset-counts"]'), [
            { count: images, label: t.tr('Images'), color: 'blue' },
            { count: other, label: t.tr('Other'), color: 'purple' },
            { count: shared.formatSize(bytes), label: t.tr('LabelSize'), color: 'gray' }
        ]);
        var list = panel.querySelector('[data-role="asset-list"]');
        assetFields.splice(0).forEach(function (f) { f.destroy(); });
        list.textContent = '';
        if (!assets.length) {
            list.innerHTML = shared.emptySection(t.tr('PagesEmptyImages'), { inline: true });
            return;
        }
        assets.forEach(function (a) { list.appendChild(assetRow(a)); });
    }

    // assetRow
    // Builds one image row: the thumbnail, the name over its path, the tier, and Delete.
    function assetRow(a) {
        var t = shared.t;
        // Set as a property, never as markup, so a stored value cannot leave the attribute.
        var thumb = el('img', { class: 'stolon-asset-thumb', alt: '' });
        thumb.src = 'data:' + (a.ContentType || '') + ';base64,' + (a.DataBase64 || '');
        var path = nectoModule.createCopyField({ value: 'asset/' + a.Name, inline: true, size: 'small', label: t.tr('LabelPath'), copyLabel: t.tr('PagesCopyPath') });
        assetFields.push(path);
        var select = el('select', { is: 'emby-select', title: t.tr('PagesLabelVisibility'), 'aria-label': t.tr('PagesLabelVisibility'), 'data-field': 'Visibility' });
        tierOptions(select, t);
        select.value = normalizeVisibility(a.Visibility);
        select.addEventListener('change', function () { setAssetVisibility(a.Name, select.value); });
        var remove = el('button', { is: 'emby-button', type: 'button', class: 'stolon-row-btn', title: t.tr('Delete'), 'aria-label': t.tr('Delete'), 'data-action': 'delete-asset' }, [
            el('span', { class: 'material-icons', 'aria-hidden': 'true', text: 'delete' })
        ]);
        remove.addEventListener('click', function () { deleteAsset(a.Name); });
        return el('div', { class: 'stolon-asset-item', 'data-asset': a.Name }, [
            thumb,
            el('div', { class: 'stolon-asset-info' }, [el('div', { class: 'stolon-asset-name', text: a.Name }), path.element]),
            el('div', { class: 'stolon-asset-actions' }, [select, remove])
        ]);
    }

    // drawStores
    // Draws the store counts and the list of stores.
    function drawStores() {
        var panel = panels.stores;
        if (!panel) return;
        var t = shared.t;
        var c = storeCounts(settings.Stores, counts);
        shared.renderCards(panel.querySelector('[data-role="store-counts"]'), [
            { count: c.stores, label: t.tr('PagesTabStores'), color: 'blue' },
            { count: c.records, label: t.tr('PagesCardRecords'), color: 'green' },
            { count: c.open, label: t.tr('PagesCardOpenToWrites'), color: c.open > 0 ? 'orange' : 'gray' }
        ]);
        var items = storeItems(settings.Stores, counts, t);
        if (storeList) storeList.setItems(items);
        else {
            storeList = nectoModule.createRecordList({ items: items, label: t.tr('PagesTabStores'), emptyText: t.tr('PagesEmptyStores'), onOpen: function (item) { openStore(item.index); } });
            panel.querySelector('[data-role="store-list"]').appendChild(storeList.element);
        }
    }

    // drawAll
    // Draws every tab that has opened.
    function drawAll() {
        drawPages();
        drawAssets();
        drawStores();
    }

    // openPage
    // Opens a page, or a new one, in the editor sidecar.
    //
    // Inputs
    // index [number]: The page's place, or -1 for a new one.
    function openPage(index) {
        var t = shared.t;
        var isNew = index < 0;
        var original = isNew ? newPage() : settings.Pages[index];
        var editor = createPageEditor(original, shared);
        nectoModule.openEditorSidecar({
            title: isNew ? t.tr('PagesTitleNewPage') : '/pages/' + (original.Slug || ''),
            body: editor.element,
            page: page,
            isNew: isNew,
            serialize: function () { return JSON.stringify(editor.getValue()); },
            saveText: t.tr('Save'),
            deleteText: t.tr('Delete'),
            onSave: function () {
                var value = editor.getValue();
                var problem = pageProblem(value, settings.Pages, index);
                if (problem) {
                    editor.setStatus(t.tr(problem), true);
                    return Promise.resolve(false);
                }
                return persist(function (s) {
                    var at = isNew ? -1 : s.Pages.findIndex(function (p) { return p.Id && p.Id === original.Id; });
                    if (at < 0 && !isNew) at = index;
                    if (at < 0) s.Pages.push(value);
                    else s.Pages[at] = value;
                }, t.tr('PagesErrorSavePage')).then(function () {
                    report(t.tr('PagesToastPageSaved'), false);
                    return true;
                }, function (err) {
                    editor.setStatus(err.message, true);
                    return false;
                });
            },
            onDelete: function (close) { deletePage(original, close); },
            onClose: function () { editor.destroy(); }
        });
    }

    // deletePage
    // Asks first, then deletes a page and closes its sidecar.
    function deletePage(original, close) {
        var t = shared.t;
        nectoModule.confirmDialog(t.tr('PagesConfirmDeletePage'), {
            title: t.format('DeleteName', '/pages/' + (original.Slug || '')),
            confirmText: t.tr('Delete'),
            destructive: true
        }).then(function (ok) {
            if (!ok) return;
            persist(function (s) {
                s.Pages = s.Pages.filter(function (p) { return original.Id ? p.Id !== original.Id : p.Slug !== original.Slug; });
            }, t.tr('PagesErrorDeletePage')).then(function () {
                close('deleted');
                report(t.tr('PagesToastPageDeleted'), false);
            }, function (err) { report(err.message, true); });
        });
    }

    // addAssetFiles
    // Reads each picked file and saves it as an image, replacing one with the same name.
    //
    // Inputs
    // files [File[]]: The files the picker took, each under the size limit.
    // refused [string]: The picker's refusals for the files it turned away, or empty.
    function addAssetFiles(files, refused) {
        var t = shared.t;
        if (!files.length) return;
        var skipped = [];
        var read = [];
        Promise.all(files.map(function (file) {
            return readFileAsDataUrl(file).then(function (dataUrl) {
                read.push({ name: file.name, dataUrl: dataUrl });
            }, function () { skipped.push(file.name); });
        })).then(function () {
            if (!read.length) {
                report(t.format('PagesErrorReadFiles', skipped.join(', ')), true);
                return;
            }
            persist(function (s) {
                read.forEach(function (f) { if (!mergeAsset(s.Assets, f.name, f.dataUrl)) skipped.push(f.name); });
            }, t.tr('PagesErrorSaveImages')).then(function () {
                var note = (skipped.length ? ' ' + t.format('PagesErrorReadFiles', skipped.join(', ')) : '') + (refused ? ' ' + refused : '');
                report(t.tr('PagesToastImagesUploaded') + note, false);
            }, function (err) { report(err.message, true); });
        });
    }

    // deleteAsset
    // Asks first, then deletes an image.
    function deleteAsset(name) {
        var t = shared.t;
        nectoModule.confirmDialog(t.tr('PagesConfirmDeleteAsset'), {
            title: t.format('DeleteName', name),
            confirmText: t.tr('Delete'),
            destructive: true
        }).then(function (ok) {
            if (!ok) return;
            persist(function (s) {
                s.Assets = s.Assets.filter(function (a) { return a.Name !== name; });
            }, t.tr('PagesErrorSaveImages')).then(function () {
                report(t.tr('PagesToastImageDeleted'), false);
            }, function (err) { report(err.message, true); });
        });
    }

    // setAssetVisibility
    // Saves an image's tier.
    function setAssetVisibility(name, value) {
        var t = shared.t;
        persist(function (s) {
            s.Assets.forEach(function (a) { if (a.Name === name) a.Visibility = normalizeVisibility(value); });
        }, t.tr('PagesErrorSaveImages')).then(function () {
            report(t.tr('PagesToastImageSaved'), false);
        }, function (err) { report(err.message, true); });
    }

    // openStore
    // Opens a store, or a new one, in the editor sidecar.
    //
    // Inputs
    // index [number]: The store's place, or -1 for a new one.
    function openStore(index) {
        var t = shared.t;
        var isNew = index < 0;
        var original = isNew ? newStore(settings) : settings.Stores[index];
        var editor = createStoreEditor(original, isNew ? undefined : counts[(original.Name || '').toLowerCase()], shared);
        nectoModule.openEditorSidecar({
            title: isNew ? t.tr('PagesTitleNewStore') : original.Name,
            body: editor.element,
            page: page,
            isNew: isNew,
            serialize: function () { return JSON.stringify(editor.getValue()); },
            saveText: t.tr('Save'),
            deleteText: t.tr('Delete'),
            onSave: function () {
                var value = editor.getValue();
                var problem = storeProblem(value, settings.Stores, index);
                if (problem) {
                    editor.setStatus(t.tr(problem), true);
                    return Promise.resolve(false);
                }
                return persist(function (s) {
                    var at = isNew ? -1 : s.Stores.findIndex(function (x) { return (x.Name || '').toLowerCase() === (original.Name || '').toLowerCase(); });
                    if (at < 0) s.Stores.push(value);
                    else s.Stores[at] = value;
                }, t.tr('PagesErrorSaveStore')).then(function () {
                    report(t.tr('PagesToastStoreSaved'), false);
                    loadCounts();
                    return true;
                }, function (err) {
                    editor.setStatus(err.message, true);
                    return false;
                });
            },
            onDelete: function (close) { deleteStore(original, close); },
            onClose: function () { editor.destroy(); }
        });
    }

    // deleteStore
    // Asks first, then removes a store and closes its sidecar. Its records stay on disk.
    function deleteStore(original, close) {
        var t = shared.t;
        nectoModule.confirmDialog(t.tr('PagesConfirmRemoveStore'), {
            title: t.format('NotificationsRemoveName', original.Name),
            confirmText: t.tr('ButtonRemove'),
            destructive: true
        }).then(function (ok) {
            if (!ok) return;
            persist(function (s) {
                s.Stores = s.Stores.filter(function (x) { return (x.Name || '').toLowerCase() !== (original.Name || '').toLowerCase(); });
            }, t.tr('PagesErrorSaveStore')).then(function () {
                close('deleted');
                report(t.tr('PagesToastStoreRemoved'), false);
            }, function (err) { report(err.message, true); });
        });
    }

    // renderTab
    // Builds a tab's panel the first time it opens.
    function renderTab(tab, panel) {
        var t = shared.t;
        panels[tab.id] = panel;
        if (tab.id === 'pages') {
            var pagesIntro = el('div', { class: 'fieldDescription' });
            fillWithCode(pagesIntro, t.tr('PagesPagesIntro'), ['/pages/{slug}']);
            var addPage = el('button', { is: 'emby-button', type: 'button', class: 'raised stolon-button stolon-primary', 'data-action': 'add-page' }, [
                el('span', { class: 'material-icons', 'aria-hidden': 'true', text: 'add' }), el('span', { text: t.tr('PagesButtonAddPage') })
            ]);
            addPage.addEventListener('click', function () { openPage(-1); });
            [pagesIntro, el('div', { class: 'stolon-cards compact', 'data-role': 'page-counts' }), el('div', { 'data-role': 'page-list' }), el('div', { class: 'stolon-card-list-actions' }, [addPage])]
                .forEach(function (n) { panel.appendChild(n); });
            drawPages();
        } else if (tab.id === 'assets') {
            var assetsIntro = el('div', { class: 'fieldDescription' });
            fillWithCode(assetsIntro, t.tr('PagesAssetsIntro'), ['asset/logo.png']);
            var assetsInfo = el('div', { class: 'stolon-info' });
            fillWithCode(assetsInfo, t.tr('PagesAssetsVisibilityInfo'), ['/pages/asset/{name}']);
            var refused = '';
            picker = nectoModule.createFilePicker({
                accept: 'image/*',
                maxSize: MAX_ASSET_BYTES,
                multiple: true,
                dropText: t.tr('PagesDropImages'),
                sizeLimitText: t.tr('PagesErrorImageTooLarge'),
                onChange: function (files) {
                    if (!files.length) return;
                    addAssetFiles(files, refused);
                    refused = '';
                    picker.setValue([]);
                },
                onError: function (messages) {
                    // The picker calls onChange in the same turn, so a refusal never outlives its pick.
                    refused = messages.join(' ');
                    setTimeout(function () { refused = ''; }, 0);
                }
            });
            [assetsIntro, assetsInfo, el('div', { class: 'stolon-cards compact', 'data-role': 'asset-counts' }), picker.element, el('div', { class: 'stolon-asset-list', 'data-role': 'asset-list' })]
                .forEach(function (n) { panel.appendChild(n); });
            drawAssets();
        } else if (tab.id === 'stores') {
            var storesIntro = el('div', { class: 'fieldDescription' });
            fillWithCode(storesIntro, t.tr('PagesStoresIntro'), ["pageStore.read('name')", "pageStore.write('name', data)"]);
            var addStore = el('button', { is: 'emby-button', type: 'button', class: 'raised stolon-button stolon-primary', 'data-action': 'add-store' }, [
                el('span', { class: 'material-icons', 'aria-hidden': 'true', text: 'add' }), el('span', { text: t.tr('PagesButtonAddStore') })
            ]);
            addStore.addEventListener('click', function () { openStore(-1); });
            [storesIntro, el('div', { class: 'stolon-cards compact', 'data-role': 'store-counts' }), el('div', { 'data-role': 'store-list' }), el('div', { class: 'stolon-card-list-actions' }, [addStore])]
                .forEach(function (n) { panel.appendChild(n); });
            drawStores();
        }
    }

    // bindPage
    // Builds the tabs once the strings are in.
    function bindPage() {
        var t = shared.t;
        tabs = nectoModule.createSubTabs({
            label: t.tr('TabPages'),
            tabs: [
                { id: 'pages', label: t.tr('PagesTabPages') },
                { id: 'assets', label: t.tr('Images') },
                { id: 'stores', label: t.tr('PagesTabStores') }
            ],
            render: renderTab
        });
        view.querySelector('#pagesTabs').appendChild(tabs.element);
        page.addCleanup(function () {
            assetFields.splice(0).forEach(function (f) { f.destroy(); });
        });
    }

    // loadPage
    // Reads the settings and the store counts, then draws every tab that has opened.
    function loadPage() {
        var mine = ++loadSeq;
        shared.getConfig().then(function (config) {
            if (mine !== loadSeq) return;
            settings = config.CustomPages || { Pages: [], Assets: [], Stores: [] };
            ['Pages', 'Assets', 'Stores'].forEach(function (k) { settings[k] = settings[k] || []; });
            drawAll();
            return loadCounts();
        }).catch(function () {
            if (mine === loadSeq) report(shared.t.tr('ErrorLoadSettings'), true);
        });
    }

    var made = nectoModule.createNectoPage(view, { page: 'necto_pages', bind: bindPage, load: loadPage });
    shared = made.shared;
    page = made.page;
}
