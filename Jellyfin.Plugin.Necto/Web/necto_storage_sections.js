// necto_storage_sections.js
// The Storage sections of Settings and Troubleshooting, which this module adds to the registry when it loads, and the
// alert editors the Storage tool shares with them.

import * as nectoModule from './configurationpage?name=necto_shared.js';

var el = nectoModule.el;

// The key of the scheduled task that checks every disk.
export var CHECK_TASK = 'NectoStorageCheck';

// The range of each unit's sliders, and where a new alert starts. GB free goes to 2 TB in steps of 10, enough for
// any alert worth setting.
export var RANGES = {
    Percent: { min: 50, max: 100, step: 1, warning: 90, error: 95 },
    Gigabytes: { min: 10, max: 2000, step: 10, warning: 100, error: 20 }
};

// The Storage settings a fresh install has.
export var DEFAULTS = {
    WarningStop: { Enabled: true, Value: 90, Unit: 'Percent' },
    ErrorStop: { Enabled: true, Value: 95, Unit: 'Percent' },
    HealthAlerts: { Failing: true, Hot: true, HotCelsius: 60, Worn: true, WornPercent: 90 },
    DiskOverrides: [],
    ShareAddresses: [],
    ScanGuard: 'Affected',
    Remount: true,
    MuteMonitoring: true
};

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
    return 'necto-storage-' + name + '-' + fieldSeq;
}

// storageOf
// Reads the Storage settings out of a config, with the defaults for anything missing.
//
// Inputs
// config [object]: The plugin configuration.
//
// Output
// [object]: The Storage settings.
export function storageOf(config) {
    var s = (config && config.Storage) || {};
    return {
        WarningStop: s.WarningStop || DEFAULTS.WarningStop,
        ErrorStop: s.ErrorStop || DEFAULTS.ErrorStop,
        HealthAlerts: s.HealthAlerts || DEFAULTS.HealthAlerts,
        DiskOverrides: s.DiskOverrides || [],
        ShareAddresses: s.ShareAddresses || [],
        ScanGuard: s.ScanGuard || 'Affected',
        Remount: s.Remount !== false,
        MuteMonitoring: s.MuteMonitoring !== false
    };
}

// named
// Spells a name as the casing list writes it, such as exFAT or APFS, else in capitals, as names like a file system are.
//
// Inputs
// t [object]: The strings.
// text [string]: The name as the server reports it, such as exfat.
//
// Output
// [string]: The name as shown.
export function named(t, text) {
    var lower = String(text).toLowerCase();
    var found = ((t && t.casing) || []).filter(function (word) { return word.toLowerCase() === lower; })[0];
    return found || String(text).toUpperCase();
}

// alertText
// Says how a pair of alerts reads in one line, such as 90% used · 95% used or 100 GB free · 20 GB free.
//
// Inputs
// t [object]: The strings.
// value [object]: { WarningStop, ErrorStop } as the configuration keeps them.
//
// Output
// [string]: The alerts that are on, or Off.
export function alertText(t, value) {
    var parts = [value.WarningStop, value.ErrorStop].filter(function (stop) { return stop && stop.Enabled; }).map(function (stop) {
        var number = Math.round(stop.Value * 10) / 10;
        return stop.Unit === 'Gigabytes' ? t.format('StorageStopGigabytesFree', number) : t.format('StorageStopPercentUsed', number);
    });
    return parts.length ? parts.join(' · ') : t.tr('Off');
}

// orderedStops
// Keeps the Storage full alert past the Storage low alert: more used for Usage, less free for Size. The value that
// moved stops one step short of the other, which moves only when the scale has no room left.
//
// Inputs
// unit [string]: Percent or Gigabytes.
// warning [number]: The Storage low point.
// error [number]: The Storage full point.
// moved [string]: warning or error, the one that changed.
//
// Output
// [object]: { warning, error } in order.
export function orderedStops(unit, warning, error, moved) {
    var range = RANGES[unit];
    // Usage grows toward full and Size shrinks toward full, so the Storage full alert sits one step further that way.
    var toward = unit === 'Gigabytes' ? -range.step : range.step;
    if ((error - warning) * toward >= range.step) return { warning: warning, error: error };
    var clamp = function (value) { return Math.min(range.max, Math.max(range.min, value)); };
    if (moved === 'error') {
        error = clamp(warning + toward);
        // At the end of the scale there is no room past the Storage low alert, so it makes way.
        if (error === warning) warning = clamp(error - toward);
    } else {
        warning = clamp(error - toward);
        if (warning === error) error = clamp(warning + toward);
    }
    return { warning: warning, error: error };
}

// alertOrderError
// Says why a pair of alerts cannot be saved, such as a Storage full alert that fires before the Storage low alert.
//
// Inputs
// t [object]: The strings.
// value [object]: { WarningStop, ErrorStop }.
//
// Output
// [string]: The reason, or an empty string.
export function alertOrderError(t, value) {
    var w = value.WarningStop;
    var e = value.ErrorStop;
    if (!w || !e || !w.Enabled || !e.Enabled) return '';
    var gigabytes = w.Unit === 'Gigabytes';
    var inOrder = gigabytes ? e.Value < w.Value : e.Value > w.Value;
    return inOrder ? '' : t.tr(gigabytes ? 'StorageErrorAlertOrderGigabytes' : 'StorageErrorAlertOrderPercent');
}

// panelOf
// Builds an alert's panel: its icon in its color and its name, a slider, and what the point means.
//
// Inputs
// icon [string]: The Material icon.
// tone [string]: warning or error, the icon's color.
// label [string]: The alert's name.
// point [string]: What passing the point means.
//
// Output
// [object]: { element, body }, where body takes the sliders before the description.
function panelOf(icon, tone, label, point) {
    var body = el('div', { class: 'stolon-panel-body' }, [el('div', { class: 'fieldDescription', text: point })]);
    var panel = el('div', { class: 'stolon-panel necto-storage-alert-panel' }, [
        el('div', { class: 'stolon-panel-header necto-storage-alert-header' }, [
            el('span', { class: 'material-icons necto-storage-alert-' + tone, 'aria-hidden': 'true', text: icon }),
            el('span', { text: label })
        ]),
        body
    ]);
    return { element: panel, body: body };
}

// createAlertEditor
// Builds the full disk alerts: a Storage low and a Storage full checkbox, what they measure, and a panel with a slider
// for each alert that is on. The Storage full alert is kept past the Storage low alert while both are on.
//
// Inputs
// t [object]: The strings.
// options [object]: heading false leaves out the label over the checkboxes. onChange is called after each change. [optional]
//
// Output
// [object]: { element, getValue(), setValue(value), errorText(), destroy() }, where a value is { WarningStop, ErrorStop }.
export function createAlertEditor(t, options) {
    var opts = options || {};
    var unit = 'Percent';
    var ordering = false;
    var checks = el('div', { class: 'stolon-field-row necto-storage-alert-checks', role: 'group', 'aria-label': t.tr('TabStorage') });
    var pillGroup = el('div', { class: 'stolon-pill-group', role: 'group', 'aria-label': t.tr('StorageLabelMeasurement') }, [
        el('button', { type: 'button', class: 'stolon-pill', 'data-value': 'Percent', 'aria-pressed': 'false', text: t.tr('LabelUsage') }),
        el('button', { type: 'button', class: 'stolon-pill', 'data-value': 'Gigabytes', 'aria-pressed': 'false', text: t.tr('LabelSize') })
    ]);
    var unitField = el('div', { class: 'stolon-field necto-storage-alert-unit' }, [el('label', { text: t.tr('StorageLabelMeasurement') }), pillGroup]);
    var panels = el('div', { class: 'stolon-field-row necto-storage-alert-panels' });
    var root = el('div', { class: 'necto-storage-alert-editor', 'data-role': 'alert-editor' }, [
        el('div', { class: 'stolon-field' }, [opts.heading === false ? null : el('label', { text: t.tr('TabStorage') }), checks]),
        unitField,
        panels
    ]);

    var alerts = {};
    [
        { stop: 'warning', icon: 'warning', label: t.tr('StorageAlertLow'), help: t.tr('StorageHelpWarningAt'), point: t.tr('StorageHelpWarningPoint') },
        { stop: 'error', icon: 'error', label: t.tr('StorageAlertFull'), help: t.tr('StorageHelpErrorAt'), point: t.tr('StorageHelpErrorPoint') }
    ].forEach(function (alert) {
        var check = nectoModule.createCheckbox({
            label: alert.label,
            description: alert.help,
            size: 'large',
            checked: true,
            onChange: function () {
                show();
                keepOrder(alert.stop);
                changed();
            }
        });
        check.element.setAttribute('data-stop', alert.stop);
        checks.appendChild(check.element);
        var panel = panelOf(alert.icon, alert.stop, alert.label, alert.point);

        // One slider per unit, so switching units keeps where each one sits.
        var sliders = {};
        Object.keys(RANGES).forEach(function (name) {
            var range = RANGES[name];
            sliders[name] = nectoModule.createSliderField({
                min: range.min,
                max: range.max,
                step: range.step,
                value: range[alert.stop],
                suffix: name === 'Gigabytes' ? ' ' + t.tr('StorageUnitGigabytesFree') : t.tr('StorageUnitPercentUsed'),
                label: alert.label,
                onChange: function () {
                    keepOrder(alert.stop);
                    changed();
                }
            });
            panel.body.insertBefore(sliders[name].element, panel.body.lastChild);
        });
        panels.appendChild(panel.element);
        alerts[alert.stop] = { check: check, panel: panel.element, sliders: sliders };
    });

    var pills = nectoModule.createPillGroup(pillGroup, {
        onChange: function (next) {
            if (!next || next === unit) return;
            unit = next;
            show();
            keepOrder('error');
            changed();
        }
    });

    // isOn
    // Reads whether one alert is on.
    function isOn(stop) { return !!alerts[stop].check.getValue(); }

    // slider
    // Gives one alert's slider for the current unit.
    function slider(stop) { return alerts[stop].sliders[unit]; }

    // changed
    // Tells the owner and the page that the alerts changed, since the pills raise no change event of their own.
    function changed() {
        root.dispatchEvent(new CustomEvent('change', { bubbles: true }));
        if (opts.onChange) opts.onChange();
    }

    // show
    // Shows each alert's panel while it is on, only the current unit's slider in it, and Measurement while either is on.
    function show() {
        ['warning', 'error'].forEach(function (stop) {
            alerts[stop].panel.classList.toggle('stolon-hidden', !isOn(stop));
            Object.keys(RANGES).forEach(function (name) {
                alerts[stop].sliders[name].element.classList.toggle('stolon-hidden', name !== unit);
            });
        });
        unitField.classList.toggle('stolon-hidden', !isOn('warning') && !isOn('error'));
    }

    // keepOrder
    // Keeps the Storage full alert past the Storage low alert while both are on.
    function keepOrder(moved) {
        if (ordering || !isOn('warning') || !isOn('error')) return;
        var next = orderedStops(unit, slider('warning').getValue(), slider('error').getValue(), moved);
        ordering = true;
        slider('warning').setValue(next.warning);
        slider('error').setValue(next.error);
        ordering = false;
    }

    // readStop
    // Reads one alert as the configuration keeps it.
    function readStop(stop) {
        return { Enabled: isOn(stop), Value: Number(slider(stop).getValue()), Unit: unit };
    }

    // writeStop
    // Shows one alert from the configuration.
    function writeStop(stop, value) {
        var range = RANGES[unit];
        var at = value && typeof value.Value === 'number' ? value.Value : range[stop];
        alerts[stop].check.setValue(!value || value.Enabled !== false);
        slider(stop).setValue(Math.min(range.max, Math.max(range.min, at)));
    }

    show();
    pills.set(unit);

    return {
        element: root,
        getValue: function () { return { WarningStop: readStop('warning'), ErrorStop: readStop('error') }; },
        setValue: function (value) {
            value = value || {};
            unit = value.WarningStop && value.WarningStop.Unit === 'Gigabytes' ? 'Gigabytes' : 'Percent';
            pills.set(unit);
            writeStop('warning', value.WarningStop);
            writeStop('error', value.ErrorStop);
            show();
        },
        errorText: function () { return alertOrderError(t, this.getValue()); },
        destroy: function () {
            if (pills.destroy) pills.destroy();
            ['warning', 'error'].forEach(function (stop) {
                alerts[stop].check.destroy();
                Object.keys(RANGES).forEach(function (name) { alerts[stop].sliders[name].destroy(); });
            });
        }
    };
}

// createHealthEditor
// Builds the disk health alerts: a Failing checkbox on its own row, a Too hot and a Worn out checkbox, and a panel
// with a slider for Too hot and Worn out while each is on.
//
// Inputs
// t [object]: The strings.
// onChange [function]: Called after each change. [optional]
//
// Output
// [object]: { element, getValue(), setValue(value), destroy() }, where a value is the configuration's HealthAlerts.
export function createHealthEditor(t, onChange) {
    var failingRow = el('div', { class: 'necto-storage-alert-failing' });
    var checksRow = el('div', { class: 'stolon-field-row necto-storage-alert-checks', role: 'group', 'aria-label': t.tr('StorageLabelHealth') });
    var panels = el('div', { class: 'stolon-field-row necto-storage-alert-panels' });
    var root = el('div', { class: 'necto-storage-alert-editor', 'data-role': 'health-editor' }, [
        el('div', { class: 'stolon-field' }, [el('label', { text: t.tr('StorageLabelHealth') }), failingRow, checksRow]),
        panels
    ]);
    var checks = {};
    var sliders = {};
    var boxes = {};

    // changed
    // Tells the owner and the page that the alerts changed.
    function changed() {
        root.dispatchEvent(new CustomEvent('change', { bubbles: true }));
        if (onChange) onChange();
    }

    // show
    // Shows each slider's panel while its alert is on.
    function show() {
        Object.keys(boxes).forEach(function (key) { boxes[key].classList.toggle('stolon-hidden', !checks[key].getValue()); });
    }

    [
        { key: 'Failing', label: t.tr('StorageHealthFailing'), help: t.tr('StorageHelpAlertFailing') },
        { key: 'Hot', label: t.tr('StorageAlertHot'), help: t.tr('StorageHelpAlertHot'), icon: 'thermostat', tone: 'error', point: t.tr('StorageHelpHotPoint'), min: 40, max: 80, start: 60, suffix: ' °C' },
        { key: 'Worn', label: t.tr('StorageAlertWorn'), help: t.tr('StorageHelpAlertWorn'), icon: 'battery_alert', tone: 'warning', point: t.tr('StorageHelpWornPoint'), min: 50, max: 100, start: 90, suffix: t.tr('StorageUnitPercentUsed') }
    ].forEach(function (alert) {
        checks[alert.key] = nectoModule.createCheckbox({
            label: alert.label,
            description: alert.help,
            size: 'large',
            checked: true,
            onChange: function () {
                show();
                changed();
            }
        });
        checks[alert.key].element.setAttribute('data-alert', alert.key);
        // Failing has no setting of its own, so it sits on its own row above the two alerts that do.
        (alert.min ? checksRow : failingRow).appendChild(checks[alert.key].element);
        if (!alert.min) return;
        var panel = panelOf(alert.icon, alert.tone, alert.label, alert.point);
        sliders[alert.key] = nectoModule.createSliderField({ min: alert.min, max: alert.max, step: 1, value: alert.start, suffix: alert.suffix, label: alert.label, onChange: changed });
        panel.body.insertBefore(sliders[alert.key].element, panel.body.lastChild);
        panels.appendChild(panel.element);
        boxes[alert.key] = panel.element;
    });

    show();
    return {
        element: root,
        getValue: function () {
            return {
                Failing: !!checks.Failing.getValue(),
                Hot: !!checks.Hot.getValue(),
                HotCelsius: Number(sliders.Hot.getValue()),
                Worn: !!checks.Worn.getValue(),
                WornPercent: Number(sliders.Worn.getValue())
            };
        },
        setValue: function (value) {
            value = value || {};
            checks.Failing.setValue(value.Failing !== false);
            checks.Hot.setValue(value.Hot !== false);
            checks.Worn.setValue(value.Worn !== false);
            sliders.Hot.setValue(Math.min(80, Math.max(40, value.HotCelsius || 60)));
            sliders.Worn.setValue(Math.min(100, Math.max(50, value.WornPercent || 90)));
            show();
        },
        destroy: function () {
            Object.keys(checks).forEach(function (key) { checks[key].destroy(); });
            Object.keys(sliders).forEach(function (key) { sliders[key].destroy(); });
        }
    };
}

// choiceOf
// Builds one stolon-choice radio card.
//
// Inputs
// name [string]: The radio group's name.
// value [string]: The choice's value.
// label [string]: The choice's name.
// help [string]: What it does.
//
// Output
// [HTMLElement]: The card.
function choiceOf(name, value, label, help) {
    return el('label', { class: 'stolon-choice' }, [
        el('input', { type: 'radio', name: name, value: value }),
        el('span', { class: 'stolon-choice-name', text: label }),
        el('span', { class: 'stolon-choice-text', text: help })
    ]);
}

// checkboxOf
// Builds a Jellyfin checkbox with its description.
//
// Inputs
// field [string]: The setting it holds, as data-field.
// label [string]: Its label.
// help [string]: Its description.
//
// Output
// [object]: { element, input }.
function checkboxOf(field, label, help) {
    var input = el('input', { type: 'checkbox', is: 'emby-checkbox', id: fieldId(field), 'data-field': field });
    var element = el('div', { class: 'checkboxContainer checkboxContainer-withDescription' }, [
        el('label', {}, [input, el('span', { text: label })]),
        el('div', { class: 'fieldDescription checkboxFieldDescription', text: help })
    ]);
    return { element: element, input: input };
}

// buildSettings
// Fills the Storage section of Settings: the full disk and health alerts every disk uses unless it has its own, when
// library scans are blocked, remounting, and real-time monitoring.
//
// Inputs
// content [HTMLElement]: The section's content.
// ctx [object]: shared is the Necto helper bag.
//
// Output
// [object]: A handle with load(config), apply(config), and destroy.
function buildSettings(content, ctx) {
    var t = ctx.shared.t;
    var alerts = createAlertEditor(t);
    var health = createHealthEditor(t);
    var radio = fieldId('scan-guard');
    var choices = el('div', { class: 'stolon-choice-group', role: 'radiogroup', 'aria-label': t.tr('StorageHeaderLibraryScans'), 'data-field': 'ScanGuard' }, [
        choiceOf(radio, 'Affected', t.tr('StorageScanGuardAffected'), t.tr('StorageHelpScanGuardAffected')),
        choiceOf(radio, 'All', t.tr('StorageScanGuardAll'), t.tr('StorageHelpScanGuardAll')),
        choiceOf(radio, 'Off', t.tr('StorageScanGuardOff'), t.tr('StorageHelpScanGuardOff'))
    ]);
    var remount = checkboxOf('Remount', t.tr('StorageLabelRemount'), t.tr('StorageHelpRemount'));
    var mute = checkboxOf('MuteMonitoring', t.tr('StorageLabelMuteMonitoring'), t.tr('StorageHelpMuteMonitoring'));
    var enabled = nectoModule.enabledSwitch(t.tr('LabelEnabled'), t.tr('StorageHelpEnabled'));

    [
        enabled.element,
        el('h3', { class: 'stolon-subsection-title', text: t.tr('Alerts') }),
        alerts.element,
        health.element,
        el('h3', { class: 'stolon-subsection-title', text: t.tr('StorageSectionMissingDisks') }),
        el('div', { class: 'stolon-field' }, [
            el('label', { text: t.tr('StorageHeaderLibraryScans') }),
            choices,
            el('div', { class: 'fieldDescription', text: t.tr('StorageHelpScanGuardScope') })
        ]),
        remount.element,
        mute.element
    ].forEach(function (node) { content.appendChild(node); });
    var scanGuard = nectoModule.createChoiceGroup(choices, {
        onChange: function () { choices.dispatchEvent(new CustomEvent('change', { bubbles: true })); }
    });

    return {
        load: function (config) {
            var s = storageOf(config);
            enabled.input.checked = !!(config && config.Storage && config.Storage.Enabled);
            alerts.setValue({ WarningStop: s.WarningStop, ErrorStop: s.ErrorStop });
            health.setValue(s.HealthAlerts);
            scanGuard.set(s.ScanGuard);
            remount.input.checked = s.Remount;
            mute.input.checked = s.MuteMonitoring;
        },
        apply: function (config) {
            var problem = alerts.errorText();
            if (problem) throw new Error(problem);
            var value = alerts.getValue();
            config.Storage = config.Storage || {};
            config.Storage.Enabled = !!enabled.input.checked;
            config.Storage.WarningStop = value.WarningStop;
            config.Storage.ErrorStop = value.ErrorStop;
            config.Storage.HealthAlerts = health.getValue();
            config.Storage.ScanGuard = scanGuard.get() || 'Affected';
            config.Storage.Remount = !!remount.input.checked;
            config.Storage.MuteMonitoring = !!mute.input.checked;
        },
        destroy: function () {
            alerts.destroy();
            health.destroy();
            scanGuard.destroy();
        }
    };
}

// buildTroubleshooting
// Fills the Storage section of Troubleshooting: checking every disk now, and putting the Storage settings back to
// their defaults.
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

    [
        status,
        el('h3', { class: 'stolon-subsection-title', text: t.tr('HeaderScheduledChecks') }),
        el('div', { class: 'stolon-reset-group' }, [nectoModule.resetRow({ icon: 'health_and_safety', label: t.tr('StorageTaskCheckStorage'), help: t.tr('StorageTaskCheckStorageDescription'), button: t.tr('ButtonRunTask'), action: 'run-check' })]),
        el('h3', { class: 'stolon-subsection-title', text: t.tr('Settings') }),
        el('div', { class: 'stolon-reset-group' }, [nectoModule.resetRow({ icon: 'settings_backup_restore', label: t.tr('StorageTaskResetSettings'), help: t.tr('StorageHelpResetSettings'), button: t.tr('Reset'), action: 'reset-settings', destructive: true })])
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

    // runCheck
    // Starts the Check storage task and follows it on its button.
    function runCheck() {
        return shared.runTask(button('run-check'), CHECK_TASK, t.tr('ButtonRunTask')).then(function (poller) {
            pollers.push(poller);
        }).catch(function (err) {
            return shared.errorText(err, t.tr('ErrorRunTask')).then(function (text) { report(text, true); });
        });
    }

    // resetSettings
    // Asks first, then puts every Storage setting back to its default, each disk's own alerts and addresses with them.
    function resetSettings() {
        return nectoModule.confirmDialog(t.tr('StorageConfirmResetSettings'), {
            title: t.tr('StorageTaskResetSettings'),
            confirmText: t.tr('Reset'),
            destructive: true
        }).then(function (confirmed) {
            if (!confirmed) return;
            return shared.saveConfigWith(function (config) {
                config.Storage = JSON.parse(JSON.stringify(DEFAULTS));
            }).then(function () {
                report(t.tr('StorageToastSettingsReset'), false);
            }).catch(function (err) {
                return shared.errorText(err, t.tr('StorageErrorResetSettings')).then(function (text) { report(text, true); });
            });
        });
    }

    button('run-check').addEventListener('click', runCheck);
    button('reset-settings').addEventListener('click', resetSettings);

    return {
        load: function () {},
        destroy: function () {
            pollers.splice(0).forEach(function (p) { if (p && p.cancel) p.cancel(); });
        }
    };
}

nectoModule.registerSection('settings', 'storage', { build: buildSettings });
nectoModule.registerSection('troubleshooting', 'storage', { build: buildTroubleshooting });
