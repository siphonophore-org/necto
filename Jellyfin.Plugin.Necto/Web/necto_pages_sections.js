// necto_pages_sections.js
// The Pages sections of Settings and Troubleshooting, which this module adds to the registry when it loads.

import * as nectoModule from './configurationpage?name=necto_shared.js';

var el = nectoModule.el;

// The key of the scheduled task that applies store retention.
export var RETENTION_TASK = 'NectoPagesStoreRetention';

// The most records a store may hold, and the longest retention, as the server allows.
var RECORD_CEILING = 100000;
var RETENTION_CEILING = 3650;

// Ids for fields, so two sections never share one.
var fieldSeq = 0;

// fieldId
// Makes an id for a field in a section.
//
// Inputs
// name [string]: What the field is.
//
// Output
// [string]: An id no other field has.
function fieldId(name) {
    fieldSeq += 1;
    return 'necto-pages-section-' + name + '-' + fieldSeq;
}

// clampInt
// Reads a whole number from a field, within a range.
//
// Inputs
// value [string]: The field's value.
// min [number]: The smallest allowed.
// max [number]: The largest allowed.
// fallback [number]: The number when the field holds none.
//
// Output
// [number]: The number.
export function clampInt(value, min, max, fallback) {
    var n = parseInt(value, 10);
    if (isNaN(n)) return fallback;
    return Math.min(max, Math.max(min, n));
}

// buildSettings
// Fills the Pages section of Settings: the record limit, retention, and notifying a new store starts with.
//
// Inputs
// content [HTMLElement]: The section's content.
// ctx [object]: shared is the Necto helper bag.
//
// Output
// [object]: A handle with load(config) and apply(config).
function buildSettings(content, ctx) {
    var t = ctx.shared.t;
    var limitId = fieldId('limit');
    var retentionId = fieldId('retention');
    var limit = el('input', { is: 'emby-input', type: 'number', id: limitId, min: '1', max: String(RECORD_CEILING), 'data-field': 'NewStoreMaxRecords' });
    var retention = el('input', { is: 'emby-input', type: 'number', id: retentionId, min: '0', max: String(RETENTION_CEILING), 'data-field': 'NewStoreRetentionDays' });
    var notify = el('input', { type: 'checkbox', is: 'emby-checkbox', id: fieldId('notify'), 'data-field': 'NewStoreNotifyOnWrite' });

    var enabled = nectoModule.enabledSwitch(t.tr('LabelEnabled'), t.tr('PagesHelpEnabled'));
    content.appendChild(el('div', { class: 'fieldDescription', text: t.tr('PagesSettingsIntro') }));
    content.appendChild(enabled.element);
    content.appendChild(el('h3', { class: 'stolon-subsection-title', text: t.tr('PagesHeaderNewStores') }));
    content.appendChild(el('div', { class: 'inputContainer' }, [
        el('label', { class: 'inputLabel inputLabelUnfocused', for: limitId, text: t.tr('PagesLabelRecordLimit') }),
        limit,
        el('div', { class: 'fieldDescription', text: t.tr('PagesNewStoreLimitHelp') })
    ]));
    content.appendChild(el('div', { class: 'inputContainer' }, [
        el('label', { class: 'inputLabel inputLabelUnfocused', for: retentionId, text: t.tr('PagesLabelRetentionDays') }),
        retention,
        el('div', { class: 'fieldDescription', text: t.tr('PagesNewStoreRetentionHelp') })
    ]));
    content.appendChild(el('div', { class: 'checkboxContainer checkboxContainer-withDescription' }, [
        el('label', {}, [notify, el('span', { text: t.tr('PagesLabelNotifyAdmins') })]),
        el('div', { class: 'fieldDescription checkboxFieldDescription', text: t.tr('PagesNewStoreNotifyHelp') })
    ]));

    return {
        load: function (config) {
            var s = config.CustomPages || {};
            enabled.input.checked = !!s.Enabled;
            limit.value = String(s.NewStoreMaxRecords > 0 ? s.NewStoreMaxRecords : 1000);
            retention.value = String(s.NewStoreRetentionDays > 0 ? s.NewStoreRetentionDays : 0);
            notify.checked = !!s.NewStoreNotifyOnWrite;
        },
        apply: function (config) {
            config.CustomPages = config.CustomPages || {};
            config.CustomPages.Enabled = !!enabled.input.checked;
            config.CustomPages.NewStoreMaxRecords = clampInt(limit.value, 1, RECORD_CEILING, 1000);
            config.CustomPages.NewStoreRetentionDays = clampInt(retention.value, 0, RETENTION_CEILING, 0);
            config.CustomPages.NewStoreNotifyOnWrite = !!notify.checked;
        }
    };
}

// clearMessage
// Gives the confirm message for clearing a store, by how many records it holds.
//
// Inputs
// count [number]: The records, or undefined when not known.
// t [object]: The strings.
//
// Output
// [string]: The message.
export function clearMessage(count, t) {
    if (typeof count !== 'number') return t.tr('PagesConfirmClearStoreUnknown');
    return t.format(count === 1 ? 'PagesConfirmClearStoreOne' : 'PagesConfirmClearStoreMany', count);
}

// buildTroubleshooting
// Fills the Pages section of Troubleshooting: each store with its records and Clear, running retention now,
// and checking the saved settings.
//
// Inputs
// content [HTMLElement]: The section's content.
// ctx [object]: shared is the Necto helper bag.
//
// Output
// [object]: A handle with load and destroy.
function buildTroubleshooting(content, ctx) {
    var shared = ctx.shared;
    var t = shared.t;
    var pollers = [];
    var status = el('div', { class: 'stolon-status-text', role: 'status', 'data-role': 'status' });
    var stores = el('div', { class: 'stolon-reset-group', 'data-role': 'stores' });
    var retention = nectoModule.resetRow({ icon: 'auto_delete', label: t.tr('PagesTaskRetentionName'), help: t.tr('PagesTaskRetentionDescription'), button: t.tr('ButtonRunTask'), action: 'run-retention' });
    var check = nectoModule.resetRow({ icon: 'fact_check', label: t.tr('PagesLabelCheckSettings'), help: t.tr('PagesCheckSettingsHelp'), button: t.tr('PagesButtonCheck'), action: 'check-settings' });

    [
        status,
        el('h3', { class: 'stolon-subsection-title', text: t.tr('PagesTabStores') }),
        stores,
        el('h3', { class: 'stolon-subsection-title', text: t.tr('HeaderScheduledChecks') }),
        el('div', { class: 'stolon-reset-group' }, [retention]),
        el('h3', { class: 'stolon-subsection-title', text: t.tr('PagesHeaderSettingsCheck') }),
        el('div', { class: 'stolon-reset-group' }, [check])
    ].forEach(function (node) { content.appendChild(node); });

    // report
    // Shows a result on the section's status line.
    function report(message, isError) {
        shared.setStatus(status, message, !!isError, { timeout: 10000 });
    }

    // button
    // Finds one of the section's buttons by its action.
    function button(action) {
        return content.querySelector('[data-action="' + action + '"]');
    }

    // clearStore
    // Asks first, then deletes every record in a store and reports how many went.
    function clearStore(name, count) {
        return nectoModule.confirmDialog(clearMessage(count, t), {
            title: t.format('PagesTitleClearStore', name),
            confirmText: t.tr('ButtonClear'),
            destructive: true
        }).then(function (ok) {
            if (!ok) return null;
            return shared.request('Pages/Stores/' + encodeURIComponent(name) + '/Clear', 'POST').then(function (result) {
                var removed = (result && result.removed) || 0;
                report(shared.plural(removed, 'PagesToastRecordRemoved', 'PagesToastRecordsRemoved'), false);
                return loadStores();
            }).catch(function (err) {
                return shared.errorText(err, t.tr('PagesErrorClearStore')).then(function (text) { report(text, true); });
            });
        });
    }

    // loadStores
    // Reads the stores and their record counts, and draws one row each with Clear.
    function loadStores() {
        return shared.getConfig().then(function (config) {
            var list = ((config.CustomPages && config.CustomPages.Stores) || []).filter(function (s) { return s.Name; });
            return Promise.all(list.map(function (s) {
                return shared.request('Pages/Stores/' + encodeURIComponent(s.Name) + '/Stats').then(function (stats) {
                    return { store: s, count: stats && typeof stats.count === 'number' ? stats.count : 0, limit: stats && stats.limit };
                }, function () { return { store: s }; });
            }));
        }).then(function (rows) {
            stores.textContent = '';
            if (!rows.length) {
                stores.innerHTML = shared.emptySection(t.tr('PagesEmptyStoresHere'), { inline: true });
                return;
            }
            rows.forEach(function (row) {
                var help = typeof row.count === 'number'
                    ? t.format('PagesUsageRecords', row.count, row.limit || row.store.MaxRecords || 1000)
                    : t.tr('PagesStoreDisabledHelp');
                var node = nectoModule.resetRow({ icon: 'delete_sweep', label: row.store.Name, help: help, button: t.tr('ButtonClear'), action: 'clear-store', destructive: true, empty: row.count === 0 });
                node.setAttribute('data-store', row.store.Name);
                var btn = node.querySelector('button');
                btn.disabled = typeof row.count !== 'number';
                btn.addEventListener('click', function () { clearStore(row.store.Name, row.count); });
                stores.appendChild(node);
            });
        }).catch(function (err) {
            return shared.errorText(err, t.tr('PagesErrorLoadStores')).then(function (text) { report(text, true); });
        });
    }

    // runRetention
    // Starts the retention task and follows it on its button.
    function runRetention() {
        return shared.runTask(button('run-retention'), RETENTION_TASK, t.tr('ButtonRunTask'), loadStores).then(function (poller) {
            pollers.push(poller);
        }).catch(function (err) {
            return shared.errorText(err, t.tr('ErrorRunTask')).then(function (text) { report(text, true); });
        });
    }

    // checkSettings
    // Asks the server whether the saved Pages settings would save again, and reports why not.
    function checkSettings() {
        return shared.getConfig().then(function (config) {
            return shared.request('Pages/Configuration/Check', 'POST', config);
        }).then(function (result) {
            if (result && result.ok) report(t.tr('PagesToastSettingsValid'), false);
            else report((result && result.message) || t.tr('PagesErrorCheckSettings'), true);
        }).catch(function (err) {
            return shared.errorText(err, t.tr('PagesErrorCheckSettings')).then(function (text) { report(text, true); });
        });
    }

    button('run-retention').addEventListener('click', runRetention);
    button('check-settings').addEventListener('click', checkSettings);

    return {
        load: loadStores,
        destroy: function () {
            pollers.splice(0).forEach(function (p) { if (p && p.cancel) p.cancel(); });
        }
    };
}

nectoModule.registerSection('settings', 'pages', { build: buildSettings });
nectoModule.registerSection('troubleshooting', 'pages', { build: buildTroubleshooting });
