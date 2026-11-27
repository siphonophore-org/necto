// necto_scripts_sections.js
// The Scripts sections of Settings and Troubleshooting, which this module adds to the registry when it loads.
// Settings holds how many runs go at once, the default command timeout, and how many runs the history keeps.
// Troubleshooting holds the run history, with a chart of each day's runs, the runs in a table, a run's steps and
// variables in a sidecar, and Clear history.

import * as nectoModule from './configurationpage?name=necto_shared.js';
import {
    createDataTable, createBarChart, createSliderField, createDurationField, confirmDialog, openSidecar,
    renderRunSummary, renderRunSteps, renderRunVariables, formatDuration, useStrings, t
} from './configurationpage?name=necto_scripts_common.js';

var el = nectoModule.el;

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
    return el('div', { class: 'stolon-field' }, [
        el('label', { text: label }),
        control.element,
        el('div', { class: 'fieldDescription', text: help })
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
    content.appendChild(el('h3', { class: 'stolon-subsection-title', text: t.tr('ScriptsLabelRuns') }));
    content.appendChild(field(t.tr('ScriptsLabelRunsAtOnce'), t.tr('ScriptsHelpRunsAtOnce'), concurrent));
    content.appendChild(field(t.tr('ScriptsLabelDefaultTimeout'), t.tr('ScriptsHelpDefaultTimeout'), timeout));
    content.appendChild(el('h3', { class: 'stolon-subsection-title', text: t.tr('ScriptsTabHistory') }));
    content.appendChild(field(t.tr('ScriptsLabelHistorySize'), t.tr('ScriptsHelpHistorySize'), history));

    return {
        load: function (config) {
            var s = config.Scripts || {};
            concurrent.setValue(s.MaxConcurrentRuns || 4);
            timeout.setValue(s.DefaultTimeoutSeconds || 60);
            history.setValue(s.HistorySize == null ? 200 : s.HistorySize);
        },
        apply: function (config) {
            config.Scripts = config.Scripts || {};
            config.Scripts.MaxConcurrentRuns = concurrent.getValue();
            config.Scripts.DefaultTimeoutSeconds = timeout.getValue();
            config.Scripts.HistorySize = history.getValue();
        }
    };
}

// buildTroubleshooting
// Fills the Scripts section of Troubleshooting with the run history.
//
// Inputs
// content [HTMLElement]: The section's content.
// ctx [object]: shared is the Necto helper bag, and page the page handle.
//
// Output
// [object]: A handle with load and destroy.
export function buildTroubleshooting(content, ctx) {
    var Shared = ctx.shared;
    var page = ctx.page;
    var cleanups = [];
    useStrings(Shared);
    var status = el('div', { class: 'stolon-status-text', role: 'status', 'data-role': 'status' });
    var chartHost = el('div', { 'data-role': 'history-chart' });
    var tableHost = el('div', { 'data-role': 'history-table' });
    content.appendChild(el('h3', { class: 'stolon-subsection-title', text: t.tr('ScriptsTabHistory') }));
    content.appendChild(chartHost);
    content.appendChild(status);
    content.appendChild(tableHost);

    var table = null;
    var chart = null;
    var chartData = null;
    var triggerLabels = {};

    function triggerLabel(key) { return triggerLabels[key] || key; }
    function runName(run) { return run.IsTest ? t.format('ScriptsRunNameTest', run.ScriptName) : run.ScriptName; }

    // Short enough for one line in the table: "Oct 25, 10:27:20 PM".
    function when(value) {
        var date = new Date(value);
        return isNaN(date) ? '' : date.toLocaleString(t.language, { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit', second: '2-digit' });
    }

    // dayLabel
    // Writes a day as a short label, such as "Nov 16", or "Sun" on a phone. The day is the server's own, so it is
    // read without a time zone.
    //
    // Inputs
    // day [string]: The day as yyyy-MM-dd.
    // phone [boolean]: Whether to write only the weekday.
    //
    // Output
    // [string]: The label, or the input for a day that cannot be read.
    function dayLabel(day, phone) {
        var parts = String(day || '').split('-').map(Number);
        if (parts.length !== 3 || parts.some(isNaN)) return String(day || '');
        var format = phone ? { weekday: 'short' } : { month: 'short', day: 'numeric' };
        return new Date(parts[0], parts[1] - 1, parts[2]).toLocaleDateString(t.language, format);
    }

    // drawChart
    // Draws the days as succeeded and failed bars. A phone shows the last 7 days by weekday, without the numbers over the bars.
    function drawChart() {
        if (!chartData) return;
        var phone = window.matchMedia('(max-width: 600px)').matches;
        var keep = phone ? 7 : 14;
        var days = (chartData.Days || []).slice(-keep);
        var succeeded = (chartData.Succeeded || []).slice(-keep);
        var failed = (chartData.Failed || []).slice(-keep);
        var empty = succeeded.concat(failed).every(function (n) { return !n; });
        if (chart) chart.destroy();
        chart = createBarChart({
            labels: empty ? [] : days.map(function (day) { return dayLabel(day, phone); }),
            series: empty ? [] : [
                { name: t.tr('ScriptsStatusSucceeded'), color: 'green', values: succeeded },
                { name: t.tr('LabelFailed'), color: 'red', values: failed }
            ],
            height: 180,
            valueLabels: !phone,
            max: Math.max.apply(null, succeeded.concat(failed, [0])) <= 4 ? 4 : undefined,
            label: t.tr('ScriptsChartRunsLabel'),
            emptyText: t.tr('ScriptsChartRunsEmpty')
        });
        var host = chartHost;
        host.innerHTML = '';
        host.appendChild(chart.element);
    }

    // loadChart
    // Reads each recent day's runs from the server and draws them.
    function loadChart() {
        Shared.request('Scripts/History/Days', 'GET').then(function (data) {
            chartData = data || {};
            drawChart();
        }).catch(function () {
            Shared.setStatus(status, t.tr('ScriptsErrorLoadChart'), true, { timeout: 0 });
        });
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
        openSidecar({
            title: runName(run),
            body: body,
            size: 'large',
            page: page
        });
    }

    // fetchRuns
    // Reads one page of runs for the table from the History route.
    //
    // Inputs
    // query [object]: The table's search, filters, page, and page size.
    //
    // Output
    // [Promise]: The page, as { Items, TotalCount }.
    function fetchRuns(query) {
        var filter = (query.filters.run || [])[0] || '';
        var params = '?Skip=' + ((query.page - 1) * query.pageSize) + '&Take=' + query.pageSize
            + (query.search ? '&Search=' + encodeURIComponent(query.search) : '')
            + (filter ? '&Filter=' + encodeURIComponent(filter) : '');
        return Shared.request('Scripts/History' + params, 'GET');
    }

    // clearHistory
    // Asks first, then deletes every recorded run.
    function clearHistory() {
        confirmDialog(t.tr('ScriptsConfirmClearHistory'), { title: t.tr('ScriptsTitleClearHistory'), confirmText: t.tr('ButtonClear'), destructive: true }).then(function (ok) {
            if (!ok) return;
            Shared.request('Scripts/History', 'DELETE').then(function () {
                Shared.setStatus(status, t.tr('ToastHistoryCleared'), false);
                if (table) table.reload();
                loadChart();
            }).catch(function () {
                Shared.setStatus(status, t.tr('ErrorClearHistory'), true);
            });
        });
    }

    // createTable
    // Draws the runs with Base's Data Table, as tall as its rows. More rows load as the page scrolls.
    //
    // Inputs
    // scripts [Array]: The scripts, offered as filters.
    function createTable(scripts) {
        if (table) table.destroy();
        var host = tableHost;
        host.innerHTML = '';
        table = createDataTable({
            label: t.tr('ScriptsLabelRuns'),
            fetch: fetchRuns,
            rowKey: 'Id',
            pageSize: 50,
            search: { placeholder: t.tr('ScriptsPlaceholderSearchRuns') },
            filters: [{
                name: 'run',
                label: t.tr('ScriptsLabelRuns'),
                options: [
                    { value: 'failed', label: t.tr('LabelFailed') },
                    { value: 'test', label: t.tr('HeaderTests') }
                ].concat(scripts.map(function (s) { return { value: s.Id, label: s.Name }; }))
            }],
            actions: [{ id: 'clear', label: t.tr('ScriptsTitleClearHistory'), icon: 'delete', variant: 'destructive', onClick: clearHistory }],
            emptyText: t.tr('ScriptsEmptyRuns'),
            widthKey: 'scripts-history',
            columns: [
                { key: 'ScriptName', label: t.tr('ScriptsLabelRun'), width: '2fr', sortable: false, render: runName },
                { key: 'Trigger', label: t.tr('ScriptsLabelTrigger'), width: '1.5fr', sortable: false, render: function (run) { return triggerLabel(run.Trigger); } },
                { key: 'StartedUtc', label: t.tr('ScriptsLabelStarted'), width: '1.5fr', sortable: false, render: function (run) { return when(run.StartedUtc); } },
                { key: 'DurationMs', label: t.tr('LabelDuration'), align: 'end', sortable: false, render: function (run) { return formatDuration(Shared, run.DurationMs); } },
                {
                    key: 'Succeeded', label: t.tr('LabelResult'), width: '8rem', sortable: false, render: function (run) {
                        var cell = document.createElement('span');
                        cell.innerHTML = Shared.badge(run.Succeeded ? 'green' : 'red', run.Succeeded ? t.tr('ScriptsStatusSucceeded') : t.tr('LabelFailed'));
                        return cell;
                    }
                }
            ],
            onOpen: showRun
        });
        host.appendChild(table.element);

        // A phone keeps the run and its result. The dialog shows the trigger, the start, and the length.
        var phone = window.matchMedia('(max-width: 600px)');
        function fit() {
            if (!table) return;
            table.setColumnHidden('Trigger', phone.matches);
            table.setColumnHidden('StartedUtc', phone.matches);
            table.setColumnHidden('DurationMs', phone.matches);
        }
        fit();
        phone.addEventListener('change', fit);
        phone.addEventListener('change', drawChart);
        cleanups.push(function () {
            phone.removeEventListener('change', fit);
            phone.removeEventListener('change', drawChart);
        });
    }

    return {
        load: function () {
            loadChart();
            Promise.all([Shared.getConfig(), Shared.request('Scripts/Catalog', 'GET')]).then(function (results) {
                (results[1].Triggers || []).forEach(function (t) { triggerLabels[t.Key] = t.Label; });
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
            if (chart) { chart.destroy(); chart = null; }
        }
    };
}

nectoModule.registerSection('settings', 'scripts', { build: buildSettings });
nectoModule.registerSection('troubleshooting', 'scripts', { build: buildTroubleshooting });
