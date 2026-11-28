// necto_scripts_sections.js
// The Scripts sections of Settings and Troubleshooting, which this module adds to the registry when it loads.
// Settings holds how many runs go at once, the default command timeout, and how many runs the history keeps.
// Troubleshooting holds Clear history. The run history, the runs in a table, is built here for the Scripts page. The
// chart of each day's runs is on the Dashboard's Scripts card.

import * as nectoModule from './configurationpage?name=necto_shared.js';
import {
    createSliderField, createDurationField, confirmDialog, openSidecar,
    renderRunSummary, renderRunSteps, renderRunVariables, formatDuration, useStrings, t
} from './configurationpage?name=necto_scripts_common.js';

var h = nectoModule.h;

// field
// Builds a labelled mount for a Stolon control, with its description.
//
// Inputs
// label [string]: The field's label.
// help [string]: One sentence under it.
// control [object]: The control's handle.
//
// Output
// [HTMLElement]: The field.
function field(label, help, control) {
    return h('div', { class: 'stolon-field' }, [
        h('label', { text: label }),
        control.element,
        h('div', { class: 'fieldDescription', text: help })
    ]);
}

// buildSettings
// Fills the Scripts section of Settings: runs at once, the default command timeout, and the runs kept.
//
// Inputs
// content [HTMLElement]: The section's content.
// ctx [object]: shared is the Necto helper bag.
//
// Output
// [object]: A handle with load(config) and apply(config).
export function buildSettings(content, ctx) {
    useStrings(ctx.shared);
    var concurrent = createSliderField({ min: 1, max: 32, label: t.tr('ScriptsLabelRunsAtOnce') });
    var timeout = createDurationField({ units: ['m', 's'], min: 1, max: 3600, required: true });
    var history = createSliderField({ min: 0, max: 5000, label: t.tr('ScriptsLabelHistorySize') });
    var enabled = nectoModule.createCheckboxField({ label: t.tr('ScriptsLabelEnable'), help: t.tr('ScriptsHelpEnabled'), field: 'Enabled' });
    content.appendChild(enabled.element);
    content.appendChild(h('h3', { class: 'stolon-subsection-title', text: t.tr('ScriptsLabelRuns') }));
    content.appendChild(field(t.tr('ScriptsLabelRunsAtOnce'), t.tr('ScriptsHelpRunsAtOnce'), concurrent));
    content.appendChild(field(t.tr('ScriptsLabelDefaultTimeout'), t.tr('ScriptsHelpDefaultTimeout'), timeout));
    content.appendChild(h('h3', { class: 'stolon-subsection-title', text: t.tr('ScriptsTabHistory') }));
    content.appendChild(field(t.tr('ScriptsLabelHistorySize'), t.tr('ScriptsHelpHistorySize'), history));

    return {
        load: function (config) {
            var s = config.Scripts || {};
            enabled.input.checked = !!s.Enabled;
            concurrent.setValue(s.MaxConcurrentRuns || 4);
            timeout.setValue(s.DefaultTimeoutSeconds || 60);
            history.setValue(s.HistorySize == null ? 200 : s.HistorySize);
        },
        apply: function (config) {
            config.Scripts = config.Scripts || {};
            config.Scripts.Enabled = !!enabled.input.checked;
            config.Scripts.MaxConcurrentRuns = concurrent.getValue();
            config.Scripts.DefaultTimeoutSeconds = timeout.getValue();
            config.Scripts.HistorySize = history.getValue();
        }
    };
}

// createRunHistory
// Builds the History tab of the Scripts page: the runs in a table, each opening its result, steps, and variables in a
// sidecar. The chart of each recent day's runs is on the Dashboard's Scripts card.
//
// Inputs
// Shared [object]: The Necto helper bag.
// page [object]: The page handle, for the sidecar.
//
// Output
// [object]: element, load(), and destroy().
export function createRunHistory(Shared, page) {
    var cleanups = [];
    useStrings(Shared);
    var status = h('div', { class: 'stolon-status-text', role: 'status', 'data-role': 'history-status' });
    var tableHost = h('div', { id: 'scriptsHistoryTable', 'data-role': 'history-table' });
    // The table comes first, so its toolbar sits where every other tab's does, then any error.
    var element = h('div', { class: 'stolon-stack', 'data-role': 'run-history' }, [tableHost, status]);

    var table = null;
    var triggerLabels = {};

    function triggerLabel(key) { return triggerLabels[key] || key; }
    function runName(run) { return run.IsTest ? t.format('ScriptsRunNameTest', run.ScriptName) : run.ScriptName; }

    // Short enough for one line in the table: "Oct 25, 10:27:20 PM".
    function when(value) {
        var date = new Date(value);
        return isNaN(date) ? '' : date.toLocaleString(t.language, { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit', second: '2-digit' });
    }

    // Opens one run's result, steps, and variables in a dialog.
    function showRun(run) {
        var body = document.createElement('div');
        body.appendChild(renderRunSummary(Shared, run, [
            { label: t.tr('ScriptsLabelTrigger'), value: triggerLabel(run.Trigger) },
            { label: t.tr('ScriptsLabelStarted'), value: when(run.StartedUtc) }
        ]));
        if (run.Summary) {
            var summary = document.createElement('div');
            summary.className = 'fieldDescription';
            summary.textContent = run.Summary;
            body.appendChild(summary);
        }
        [[t.tr('ScriptsHeaderSteps'), renderRunSteps(Shared, run)], [t.tr('ScriptsHeaderVariables'), renderRunVariables(Shared, run)]].forEach(function (part) {
            var title = document.createElement('h3');
            title.className = 'stolon-subsection-title';
            title.textContent = part[0];
            body.appendChild(title);
            body.appendChild(part[1]);
        });
        openSidecar({ title: runName(run), body: body, size: 'large', page: page });
    }

    // createTable
    // Draws the runs with Stolon's Paginated Table over the History route, each row its run, its trigger, when it
    // started and how long it took, and its result. More rows load as the page scrolls.
    //
    // Inputs
    // scripts [Array]: The scripts, offered as filters.
    function createTable(scripts) {
        if (table) table.destroy();
        tableHost.innerHTML = '';
        var bag = Object.create(Shared);
        bag.apiPrefix = '';
        table = nectoModule.createPaginatedTable(element, bag, {
            containerId: 'scriptsHistoryTable',
            endpoint: 'Scripts/History',
            search: { enabled: true, placeholder: t.tr('ScriptsPlaceholderSearchRuns') },
            filters: {
                name: 'filter',
                label: t.tr('ScriptsLabelRuns'),
                options: [
                    { value: 'failed', label: t.tr('LabelFailed') },
                    { value: 'test', label: t.tr('HeaderTests') }
                ].concat(scripts.map(function (s) { return { value: s.Id, label: s.Name }; }))
            },
            pagination: { pageSize: 50 },
            emptyState: { message: t.tr('ScriptsEmptyRuns'), filteredMessage: t.tr('ScriptsEmptyRunsMatch') },
            columns: [
                {
                    key: 'ScriptName', type: 'custom', label: t.tr('ScriptsLabelRun'),
                    render: function (run) {
                        var line = [triggerLabel(run.Trigger), when(run.StartedUtc), formatDuration(Shared, run.DurationMs)].filter(Boolean).join(' · ');
                        return '<div class="stolon-table-item-info"><div class="stolon-table-item-title">' + nectoModule.escapeHtml(runName(run)) + '</div>'
                            + '<div class="stolon-table-item-sub">' + nectoModule.escapeHtml(line) + '</div></div>';
                    }
                },
                {
                    key: 'Succeeded', type: 'custom', label: t.tr('LabelResult'), className: 'stolon-table-cell-status',
                    render: function (run) { return Shared.badge(run.Succeeded ? 'green' : 'red', run.Succeeded ? t.tr('ScriptsStatusSucceeded') : t.tr('LabelFailed')); }
                }
            ],
            actions: { onRowClick: showRun }
        });
        table.load().catch(function () { Shared.setStatus(status, t.tr('ScriptsErrorLoadHistory'), true, { timeout: 0 }); });
    }

    return {
        element: element,
        load: function () {
            return Promise.all([Shared.getConfig(), Shared.request('Scripts/Catalog', 'GET')]).then(function (results) {
                (results[1].Triggers || []).forEach(function (x) { triggerLabels[x.Key] = x.Label; });
                // The catalog lists only triggers a script can pick, so a Run now run takes its label here.
                if (!triggerLabels.Manual) triggerLabels.Manual = t.tr('ButtonRunTask');
                createTable((results[0].Scripts && results[0].Scripts.Scripts) || []);
            }).catch(function () {
                Shared.setStatus(status, t.tr('ScriptsErrorLoadHistory'), true, { timeout: 0 });
            });
        },
        destroy: function () {
            cleanups.splice(0).forEach(function (fn) { fn(); });
            if (table) { table.destroy(); table = null; }
        }
    };
}

// buildTroubleshooting
// Fills the Scripts section of Troubleshooting with Clear history. The runs show on the Scripts page.
//
// Inputs
// content [HTMLElement]: The section's content.
// ctx [object]: shared is the Necto helper bag.
//
// Output
// [object]: A handle with load.
export function buildTroubleshooting(content, ctx) {
    var Shared = ctx.shared;
    useStrings(Shared);
    var status = h('div', { class: 'stolon-status-text', role: 'status', 'data-role': 'status' });
    var clearRow = nectoModule.createResetRow({ icon: 'history_toggle_off', label: t.tr('ScriptsTitleClearHistory'), help: t.tr('ScriptsHelpClearHistory'), button: t.tr('ButtonClear'), action: 'clear-history', destructive: true, onClick: function () { clearHistory(); } });
    content.appendChild(status);
    content.appendChild(h('h3', { class: 'stolon-subsection-title', text: t.tr('HeaderRepairs') }));
    content.appendChild(nectoModule.createResetGroup([clearRow]).element);

    // clearHistory
    // Asks first, then deletes every recorded run.
    function clearHistory() {
        confirmDialog(t.tr('ScriptsConfirmClearHistory'), { title: t.tr('ScriptsTitleClearHistory'), confirmText: t.tr('ButtonClear'), destructive: true }).then(function (ok) {
            if (!ok) return;
            Shared.request('Scripts/History', 'DELETE').then(function () {
                Shared.setStatus(status, t.tr('ToastHistoryCleared'), false);
            }).catch(function () {
                Shared.setStatus(status, t.tr('ErrorClearHistory'), true);
            });
        });
    }

    return { load: function () {} };
}

nectoModule.registerSection('settings', 'scripts', { build: buildSettings });
nectoModule.registerSection('troubleshooting', 'scripts', { build: buildTroubleshooting });
