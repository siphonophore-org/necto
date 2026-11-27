// necto_ddns_sections.js
// The DDNS sections of Settings and Troubleshooting, which this module adds to the registry when it loads.

import * as nectoModule from './configurationpage?name=necto_shared.js';

var h = nectoModule.h;

// The key of the scheduled task that updates the records.
export var DDNS_UPDATE_TASK = 'NectoDdnsUpdate';

// The choices for how often an unchanged record is sent again, and for the backoff, in hours. 0 is off.
var FORCE_HOURS = [0, 1, 3, 6, 12, 18, 24, 48, 72, 168];
var BACKOFF_FAILURES = [0, 3, 5, 10];
var BACKOFF_HOURS = [1, 6, 12, 24, 48];

// hourOptions
// Lists hour choices with their names, keeping a stored value that is not among them.
//
// Inputs
// hours [number[]]: The choices.
// current [number]: The stored value.
// t [object]: The strings.
// offKey [string]: The key of the name for 0, or null when 0 is not a choice.
//
// Output
// [object[]]: Each choice's value and text.
export function hourOptions(hours, current, t, offKey) {
    var list = hours.slice();
    if (current != null && list.indexOf(current) < 0) list.push(current);
    return list.sort(function (a, b) { return a - b; }).map(function (h) {
        var label = h === 0 && offKey ? t.tr(offKey) : t.plural(h, 'HoursOne', 'HoursMany');
        return { value: String(h), text: label };
    });
}

// failureOptions
// Lists the backoff thresholds with their names, keeping a stored value that is not among them.
//
// Inputs
// current [number]: The stored value.
// t [object]: The strings.
//
// Output
// [object[]]: Each choice's value and text.
export function failureOptions(current, t) {
    var list = BACKOFF_FAILURES.slice();
    if (current != null && list.indexOf(current) < 0) list.push(current);
    return list.sort(function (a, b) { return a - b; }).map(function (n) {
        return { value: String(n), text: n === 0 ? t.tr('Off') : t.plural(n, 'DdnsFailuresOne', 'DdnsFailuresMany') };
    });
}

// readSettings
// Reads the DDNS settings from the section's fields.
//
// Inputs
// f [object]: The section's fields.
//
// Output
// [object]: The settings to post to Ddns/Settings.
export function readSettings(f) {
    return {
        Enabled: !!(f.enabled && f.enabled.input.checked),
        IPv4DetectionUrl: nectoModule.readLines(f.ipv4.input).join('\n'),
        IPv6DetectionUrl: nectoModule.readLines(f.ipv6.input).join('\n'),
        SkipInternalAddresses: !!f.skip.input.checked,
        ForceUpdateHours: parseInt(f.force.input.value, 10) || 0,
        BackoffAfterFailures: parseInt(f.failures.input.value, 10) || 0,
        BackoffHours: parseInt(f.backoff.input.value, 10) || 24,
        RequestTimeoutSeconds: Math.max(1, nectoModule.readNumber(f.timeout.input) || 15),
        Records: []
    };
}

// buildSettings
// Fills the DDNS section of Settings: the public IP lookup, sending an unchanged address again, the backoff, and the
// request timeout. It saves through Ddns/Settings, which leaves the records alone.
//
// Inputs
// content [HTMLElement]: The section's content.
// ctx [object]: shared is the Necto helper bag.
//
// Output
// [object]: A handle with load and save.
function buildSettings(content, ctx) {
    var shared = ctx.shared;
    var t = shared.t;
    var f = {
        enabled: nectoModule.createCheckboxField({ label: t.tr('DdnsLabelEnable'), help: t.tr('DdnsHelpEnabled'), field: 'Enabled' }),
        ipv4: nectoModule.createTextareaField({ label: t.tr('DdnsLabelIpv4Urls'), help: t.tr('DdnsIpv4UrlsHelp'), field: 'IPv4DetectionUrl', placeholder: 'https://api.ipify.org' }),
        ipv6: nectoModule.createTextareaField({ label: t.tr('DdnsLabelIpv6Urls'), help: t.tr('DdnsIpv6UrlsHelp'), field: 'IPv6DetectionUrl', placeholder: 'https://api6.ipify.org' }),
        skip: nectoModule.createCheckboxField({ label: t.tr('DdnsLabelSkipInternal'), help: t.tr('DdnsSkipInternalHelp'), field: 'SkipInternalAddresses' }),
        force: nectoModule.createSelectField({ label: t.tr('DdnsLabelForceUpdate'), help: t.tr('DdnsForceUpdateHelp'), field: 'ForceUpdateHours' }),
        failures: nectoModule.createSelectField({ label: t.tr('DdnsLabelBackoffAfter'), help: t.tr('DdnsBackoffAfterHelp'), field: 'BackoffAfterFailures' }),
        backoff: nectoModule.createSelectField({ label: t.tr('DdnsLabelBackoffFor'), help: t.tr('DdnsBackoffForHelp'), field: 'BackoffHours' }),
        timeout: nectoModule.createInputField({ label: t.tr('DdnsLabelRequestTimeout'), help: t.tr('DdnsRequestTimeoutHelp'), field: 'RequestTimeoutSeconds', type: 'number', min: 1, max: 300 })
    };
    var status = h('div', { class: 'stolon-status-text', role: 'status' });
    content.appendChild(status);
    content.appendChild(f.enabled.element);
    content.appendChild(h('h3', { class: 'stolon-subsection-title', text: t.tr('DdnsHeaderPublicIp') }));
    [f.ipv4, f.ipv6, f.skip].forEach(function (x) { content.appendChild(x.element); });
    content.appendChild(h('h3', { class: 'stolon-subsection-title', text: t.tr('DdnsHeaderUpdates') }));
    [f.force, f.failures, f.backoff, f.timeout].forEach(function (x) { content.appendChild(x.element); });

    return {
        fields: f,
        load: function () {
            return shared.request('Ddns/Settings').then(function (s) {
                f.enabled.input.checked = !!s.Enabled;
                f.ipv4.input.value = String(s.IPv4DetectionUrl || '');
                f.ipv6.input.value = String(s.IPv6DetectionUrl || '');
                f.skip.input.checked = s.SkipInternalAddresses !== false;
                f.force.setOptions(hourOptions(FORCE_HOURS, s.ForceUpdateHours || 0, t, 'Off'), s.ForceUpdateHours || 0);
                f.failures.setOptions(failureOptions(s.BackoffAfterFailures, t), s.BackoffAfterFailures);
                f.backoff.setOptions(hourOptions(BACKOFF_HOURS, s.BackoffHours || 24, t, null), s.BackoffHours || 24);
                f.timeout.input.value = String(s.RequestTimeoutSeconds || 15);
            }).catch(function (err) {
                return shared.errorText(err, t.tr('DdnsErrorLoadSettings')).then(function (text) { shared.setStatus(status, text, true); });
            });
        },
        apply: function () {},
        save: function () {
            return shared.request('Ddns/Settings', 'POST', readSettings(f)).catch(function (err) {
                return shared.errorText(err, t.tr('DdnsErrorSaveSettings')).then(function (text) { throw new Error(text); });
            });
        }
    };
}

// buildTroubleshooting
// Fills the DDNS section of Troubleshooting: why the last run could not find the public IP, running the update now,
// and resetting a record.
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
    var status = h('div', { class: 'stolon-status-text', role: 'status', 'data-role': 'status' });
    var detection = h('div', { class: 'stolon-error stolon-hidden', role: 'status', 'data-role': 'detection' });
    var record = nectoModule.createSelectField({ label: t.tr('DdnsLabelRecord'), field: 'ResetRecord', help: t.tr('DdnsResetRecordHelp') });
    var rows = nectoModule.createResetGroup([
        nectoModule.createResetRow({ icon: 'schedule', label: t.tr('DdnsTaskUpdate'), help: t.tr('DdnsTaskUpdateHelp'), button: t.tr('ButtonRunTask'), action: 'run-update' }),
        nectoModule.createResetRow({ icon: 'published_with_changes', label: t.tr('DdnsButtonCheckAll'), help: t.tr('DdnsCheckAllHelp'), button: t.tr('ButtonRunTask'), action: 'update-now' })
    ]).element;
    var resetRow = nectoModule.createResetRow({ icon: 'replay', label: t.tr('DdnsResetRecord'), help: t.tr('DdnsResetRecordRowHelp'), button: t.tr('Reset'), action: 'reset-record' }).element;
    [
        status,
        detection,
        h('h3', { class: 'stolon-subsection-title', text: t.tr('HeaderChecks') }),
        rows,
        h('h3', { class: 'stolon-subsection-title', text: t.tr('HeaderRepairs') }),
        record.element,
        h('div', { class: 'stolon-reset-group' }, [resetRow])
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

    // load
    // Reads the records, and why the last run could not find the public IP.
    function load() {
        return shared.request('Ddns/Records').then(function (data) {
            detection.textContent = data.DetectionMessage || '';
            detection.classList.toggle('stolon-hidden', !data.DetectionMessage);
            record.setOptions((data.Records || []).map(function (entry) {
                var r = entry.Record;
                return { value: r.Id, text: (r.Name || r.Hostname || r.Id) };
            }));
            button('reset-record').disabled = !(data.Records || []).length;
        }).catch(function (err) {
            return shared.errorText(err, t.tr('DdnsErrorLoadRecords')).then(function (text) { report(text, true); });
        });
    }

    button('run-update').addEventListener('click', function () {
        shared.runScheduledTask(button('run-update'), DDNS_UPDATE_TASK, t.tr('ButtonRunTask'), load).then(function (p) { pollers.push(p); }).catch(function (err) {
            return shared.errorText(err, t.tr('DdnsErrorRunTask')).then(function (text) { report(text, true); });
        });
    });
    button('update-now').addEventListener('click', function () {
        report(t.tr('DdnsUpdating'), false);
        shared.request('Ddns/Update', 'POST').then(function (outcome) {
            var records = (outcome && outcome.Records) || [];
            var failed = records.filter(function (r) { return !r.Success && !r.Skipped; });
            var lines = records.map(function (r) { return r.Hostname + ': ' + (r.Message || r.Action); });
            report(lines.length ? lines.join(' ') : t.tr('DdnsNoRecordsToUpdate'), failed.length > 0);
            load();
        }).catch(function (err) {
            return shared.errorText(err, t.tr('DdnsErrorUpdate')).then(function (text) { report(text, true); });
        });
    });
    button('reset-record').addEventListener('click', function () {
        var id = record.input.value;
        if (!id) return;
        nectoModule.confirmDialog(t.tr('DdnsConfirmResetRecord'), {
            title: t.format('DdnsTitleResetRecord', record.input.options[record.input.selectedIndex].textContent),
            confirmText: t.tr('Reset'),
            destructive: true
        }).then(function (confirmed) {
            if (!confirmed) return;
            shared.request('Ddns/Records/' + encodeURIComponent(id) + '/Reset', 'POST').then(function () {
                report(t.tr('DdnsToastRecordReset'), false);
            }).catch(function (err) {
                return shared.errorText(err, t.tr('DdnsErrorResetRecord')).then(function (text) { report(text, true); });
            });
        });
    });

    return {
        load: load,
        destroy: function () {
            pollers.splice(0).forEach(function (p) { if (p && p.cancel) p.cancel(); });
        }
    };
}

nectoModule.registerSection('settings', 'ddns', { build: buildSettings });
nectoModule.registerSection('troubleshooting', 'ddns', { build: buildTroubleshooting });
