// necto_scripts.js
// The Scripts tool: the script picker and the editor for one script's triggers, filters, conditions, steps,
// secrets, and behavior, with Save, Test, and Run now. A script runs for every user, or for the groups it picks.

import {
    createScriptsPage, createRepeater, createChipSelect, createTagInput, createComboBox, adornField, createCheckboxList, createUserMultiSelector, createSortableCardList,
    createChoiceGroup, createCopyField, createSaveBar, createSliderField, createDurationField,
    createCollapsibleSection, confirmDialog, promptSidecar, openSidecar, generateGuid, downloadJson, pickJsonFile, setFieldState, SECRET_KEPT,
    fieldGroups, fieldLabel, NUMERIC_FIELDS, operatorLabels, operationTypes, renderRunSummary, renderRunSteps, t
} from './configurationpage?name=necto_scripts_common.js';
import { createRunHistory } from './configurationpage?name=necto_scripts_sections.js';
import { createToolTabs } from './configurationpage?name=necto_shared.js';

export default function (view) {
    'use strict';

    var tracker = null;
    var history = null;
    var folds = {};
    var page = createScriptsPage(view, {
        bind: bind,
        load: load,
        isDirty: function () { return !!(tracker && tracker.isDirty()); }
    });
    var Shared = page.shared;

    var DAYS = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];
    var FIXED_VALUES = {
        PlayMethod: ['DirectPlay', 'DirectStream', 'Transcode'],
        IsLocal: ['true', 'false'],
        PlayedToCompletion: ['true', 'false'],
        IsAdmin: ['true', 'false'],
        IsPlayed: ['true', 'false'],
        IsFavorite: ['true', 'false'],
        HasSubtitles: ['true', 'false'],
        PlayedByAnyone: ['true', 'false'],
        IsDisabled: ['true', 'false'],
        TaskStatus: ['Completed', 'Failed', 'Cancelled', 'Aborted'],
        DayOfWeek: DAYS,
        UpdateReason: ['MetadataImport', 'ImageUpdate', 'MetadataDownload', 'MetadataEdit'],
        ActivitySeverity: ['Trace', 'Debug', 'Information', 'Warning', 'Error', 'Critical'],
        Hour: Array.from({ length: 24 }, function (_, h) { return String(h); })
    };
    var HTTP_METHODS = ['POST', 'GET', 'PUT', 'PATCH', 'DELETE'];

    // Who a Send message step reaches.
    function messageTargets() {
        return [
            { value: 'EventSession', label: t.tr('ScriptsTargetEventSession') },
            { value: 'UserSessions', label: t.tr('ScriptsTargetUserSessions') },
            { value: 'AllSessions', label: t.tr('ScriptsTargetAllSessions') },
            { value: 'AdminSessions', label: t.tr('ScriptsTargetAdminSessions') }
        ];
    }

    // A day of the week in the viewer's language.
    function dayLabel(day) {
        var labels = {
            Monday: t.tr('Monday'), Tuesday: t.tr('Tuesday'), Wednesday: t.tr('Wednesday'), Thursday: t.tr('Thursday'),
            Friday: t.tr('Friday'), Saturday: t.tr('Saturday'), Sunday: t.tr('Sunday')
        };
        return labels[day] || day;
    }

    // Steps whose output a later step can use.
    var HAS_OUTPUT = { RunCommand: true, HttpRequest: true };
    // The script's numbers, each a Base Slider or Duration Field: the mount, the script property, what it shows
    // while the property is missing, and for a trigger's number the trigger and the panel that holds it.
    // A minutes property is edited as a duration and stored in whole minutes.
    function scriptNumbers() {
        return [
            { mount: 'pausedMinutesMount', key: 'PausedForMinutes', fallback: 10, trigger: 'PlaybackPausedFor', block: 'pausedOptions', minutes: true, min: 1, max: 1440 },
            { mount: 'playingMinutesMount', key: 'PlayingForMinutes', fallback: 60, trigger: 'PlaybackPlayingFor', block: 'playingOptions', minutes: true, min: 1, max: 1440 },
            { mount: 'progressPercentMount', key: 'ProgressPercentThreshold', fallback: 90, trigger: 'PlaybackProgressReached', block: 'progressOptions', slider: { min: 1, max: 99, suffix: '%', label: t.tr('ScriptsLabelPercentage') } },
            { mount: 'streamCountMount', key: 'StreamCountThreshold', fallback: 3, trigger: 'StreamCountReached', block: 'streamOptions', slider: { min: 1, max: 100, label: t.tr('ScriptsLabelStreams') } },
            { mount: 'transcodeCountMount', key: 'TranscodeCountThreshold', fallback: 2, trigger: 'TranscodeCountReached', block: 'transcodeOptions', slider: { min: 1, max: 100, label: t.tr('LabelTranscodes') } },
            { mount: 'forEachLimitMount', key: 'ForEachLimit', fallback: 50, slider: { min: 1, max: 500, label: t.tr('ScriptsLabelForEachLimit') } },
            { mount: 'cooldownMount', key: 'CooldownSeconds', fallback: 0, units: ['h', 'm', 's'], min: 0, max: 86400 }
        ];
    }
    // A step's numbers, each a Base Slider or Duration Field in the step editor.
    function stepNumbers() {
        return {
            TimeoutSeconds: { units: ['m', 's'], min: 0, max: 3600 },
            Retries: { slider: { min: 0, max: 5, label: t.tr('ScriptsLabelRetries') } },
            DisplaySeconds: { slider: { min: 0, max: 300, suffix: ' s', label: t.tr('ScriptsLabelSecondsShown') } },
            WaitSeconds: { units: ['m', 's'], min: 1, max: 3600 }
        };
    }

    var catalog = null;
    var scripts = [];
    var selectedId = null;
    var triggerList = null;
    var itemKindSelect = null;
    // The groups, from the Groups tool, to name the ones that run each script.
    var nectoGroups = [];
    var matchChoice = null;
    var scopeChoice = null;
    var operationList = null;
    var conditionRepeater = null;
    var conditionsQuiet = false;
    var secretRepeater = null;
    var variableRows = [];
    var saveBar = null;
    var numberControls = {};
    var editorControls = [];
    // The script's variable names, offered by the step condition's variable field.
    var variableNames = [];
    var variableCombo = null;
    var webhookField = null;
    var groupPicker = null;
    var suppressWidgetEvents = false;

    function el(id) { return view.querySelector('#' + id); }
    function esc(value) { return Shared.escapeHtml(value === null || value === undefined ? '' : String(value)); }
    function compactId(id) { return String(id || '').replace(/-/g, '').toLowerCase(); }

    function current() {
        return scripts.find(function (s) { return s.Id === selectedId; }) || null;
    }

    function triggerInfo(key) {
        return catalog.Triggers.find(function (t) { return t.Key === key; }) || null;
    }

    function fieldInfo(key) {
        return catalog.Fields.find(function (f) { return f.Key === key; }) || null;
    }

    function quoted(t) { return '"' + t.Label + '"'; }

    function selectedTriggers(script) {
        return (script.Triggers || []).map(triggerInfo).filter(Boolean);
    }

    function has(script, trigger) { return (script.Triggers || []).indexOf(trigger) >= 0; }

    // Running for each item or user only happens on an event with no item, or no user, of its own.
    function expands(script, t) {
        if (script.ForEach === 'Items') return !t.HasItem;
        if (script.ForEach === 'Users') return !t.HasUser;
        return false;
    }

    function carriesItem(script, t) { return t.HasItem || script.ForEach === 'Items'; }
    function carriesUser(script, t) { return t.HasUser || script.ForEach === 'Users'; }

    function supplies(script, t, info) {
        return info.Triggers.indexOf(t.Key) >= 0 || (expands(script, t) && (info.ForEach || []).indexOf(script.ForEach) >= 0);
    }

    function newToken() {
        var bytes = new Uint8Array(24);
        window.crypto.getRandomValues(bytes);
        return Array.from(bytes, function (b) { return ('0' + b.toString(16)).slice(-2); }).join('');
    }

    function ensureWebhookToken(script) {
        if (script && has(script, 'Webhook') && !script.WebhookToken) script.WebhookToken = newToken();
    }

    // narrow
    // Reads whether the page is as narrow as a phone.
    function narrow() {
        return !!(globalThis.matchMedia && globalThis.matchMedia('(max-width: 600px)').matches);
    }

    // Every edit goes through here: it refreshes the counts, the picker, and the Save Bar's unsaved line.
    function edited() {
        renderCounts();
        renderSelector();
        renderSummaries();
        if (!tracker) return;
        var dirty = tracker.check();
        if (saveBar && saveBar.getState() !== 'saving') saveBar.setDirty(dirty);
    }

    // numberControl
    // Builds a Base Slider, or a Duration Field in seconds, for one number.
    //
    // Inputs
    // spec [object]: { slider } with the Slider's min, max, suffix, and label, or { units, min, max } for a duration.
    // onChange [function]: Called with the new value, in seconds for a duration.
    //
    // Output
    // [object]: The control handle.
    function numberControl(spec, onChange) {
        if (spec.slider) return createSliderField(Object.assign({ onChange: onChange }, spec.slider));
        return createDurationField({
            units: spec.minutes ? ['h', 'm'] : spec.units,
            min: spec.minutes ? spec.min * 60 : spec.min,
            max: spec.minutes ? spec.max * 60 : spec.max,
            required: true,
            onChange: onChange
        });
    }

    // createNumbers
    // Mounts the script's number controls. Each writes its script property as it changes.
    function createNumbers() {
        scriptNumbers().forEach(function (spec) {
            var control = numberControl(spec, function (value) {
                if (suppressWidgetEvents || !current()) return;
                var number = value === null || value === undefined ? 0 : value;
                current()[spec.key] = spec.minutes ? Math.round(number / 60) : number;
                edited();
            });
            numberControls[spec.key] = control;
            el(spec.mount).appendChild(control.element);
        });
    }

    // showNumbers
    // Shows the script's numbers in their controls, and each trigger's panel while its trigger is checked.
    function showNumbers() {
        var script = current();
        suppressWidgetEvents = true;
        scriptNumbers().forEach(function (spec) {
            var value = orDefault(script[spec.key], spec.fallback);
            numberControls[spec.key].setValue(spec.minutes ? value * 60 : value);
            if (spec.block) Shared.setVisible(spec.block, has(script, spec.trigger));
        });
        suppressWidgetEvents = false;
    }

    // ---- Counts and selector ----

    function renderCounts() {
        var enabled = scripts.filter(function (s) { return s.Enabled; }).length;
        Shared.renderCards('scriptCounts', [
            { count: scripts.length, label: t.tr('TabScripts'), color: 'blue' },
            { count: enabled, label: t.tr('LabelEnabled'), color: 'green' },
            { count: scripts.length - enabled, label: t.tr('Disabled'), color: 'gray' }
        ]);
    }

    function renderSelector() {
        var select = el('selectScript');
        select.innerHTML = scripts.map(function (s) {
            var name = s.Name || t.tr('ScriptsScriptUntitled');
            return '<option value="' + esc(s.Id) + '">' + esc(s.Enabled ? name : t.format('ScriptsScriptNameDisabled', name)) + '</option>';
        }).join('');
        if (selectedId) select.value = selectedId;
        el('emptyState').innerHTML = scripts.length ? '' : Shared.emptySection(t.tr('ScriptsEmptyScripts'));
        Shared.setVisible('emptyState', scripts.length === 0);
        Shared.setVisible('scriptEditor', scripts.length > 0);
        el('btnRemoveScript').disabled = scripts.length === 0;
        el('btnRenameScript').disabled = scripts.length === 0;
    }

    function selectScript(id) {
        selectedId = id;
        renderEditor();
    }

    // ---- Editor ----

    function renderEditor() {
        var script = current();
        if (!script) return;
        suppressWidgetEvents = true;
        el('scriptDescription').value = script.Description || '';
        el('scriptEnabled').checked = !!script.Enabled;
        triggerList.setValue(script.Triggers || []);
        itemKindSelect.setValue(kindItems(script.ItemKinds));
        matchChoice.set(script.Match || 'All');
        suppressWidgetEvents = false;
        el('forEachMode').value = script.ForEach || 'None';
        el('onFailureScript').innerHTML = '<option value="">' + esc(t.tr('ScriptsOptionNothing')) + '</option>' + scripts.filter(function (s) { return s.Id !== script.Id; }).map(function (s) {
            return '<option value="' + esc(s.Id) + '">' + esc(s.Name || t.tr('ScriptsScriptUntitled')) + '</option>';
        }).join('');
        el('onFailureScript').value = compactId(script.OnFailureScriptId);
        renderTriggerOptions();
        renderConditions();
        renderOperations();
        renderSecrets();
        renderVariables();
        renderTestTriggers();
        renderWarnings();
        renderSummaries();
    }

    // ---- Sections ----

    // foldSections
    // Turns each marked section into a Stolon Collapsible Section led by its icon tile, its blocks a wide gap apart.
    // Each starts closed and says what it holds under its title.
    function foldSections() {
        view.querySelectorAll('.stolon-section[data-fold]').forEach(function (section) {
            var fold = createCollapsibleSection({
                title: section.querySelector('.stolon-section-title').textContent.trim(),
                icon: section.getAttribute('data-fold-icon'),
                collapsed: section.getAttribute('data-fold-state') === 'closed'
            });
            // The blocks move straight into the section, since the section content's own padding would double its own.
            var source = section.querySelector('.stolon-section-content');
            while (source.firstChild) fold.content.appendChild(source.firstChild);
            fold.content.classList.add('stolon-flow');
            fold.element.setAttribute('data-fold', section.getAttribute('data-fold'));
            section.parentNode.replaceChild(fold.element, section);
            folds[section.getAttribute('data-fold')] = fold;
        });
        page.addCleanup(function () {
            Object.keys(folds).forEach(function (key) { folds[key].destroy(); });
            folds = {};
        });
    }

    // shortDuration
    // Writes seconds as hours, minutes, and seconds, such as "1h 30m".
    //
    // Inputs
    // seconds [number]: The length.
    //
    // Output
    // [string]: The length as text.
    function shortDuration(seconds) {
        var parts = [[Math.floor(seconds / 3600), 'h'], [Math.floor(seconds % 3600 / 60), 'm'], [seconds % 60, 's']];
        return parts.filter(function (part) { return part[0] > 0; }).map(function (part) { return part[0] + part[1]; }).join(' ');
    }

    // counted
    // Picks the string for none, one, or many, and fills the count into many.
    function counted(count, none, one, many) {
        return count === 0 ? t.tr(none) : t.plural(count, one, many);
    }

    // shown
    // Whether a warning in the editor is showing.
    function shown(id) {
        var node = el(id);
        return !!node && !node.classList.contains('stolon-hidden') && node.textContent.trim() !== '';
    }

    // ignoredLabel
    // Names the triggers that ignore the script's users, by their shared category, such as "Schedule ignored", or by
    // their count when they span several.
    //
    // Inputs
    // script [object]: The open script.
    //
    // Output
    // [string]: The badge text.
    function ignoredLabel(script) {
        var ignored = selectedTriggers(script).filter(function (t) { return !carriesUser(script, t); });
        var categories = ignored.map(function (t) { return t.Category; }).filter(function (c, i, all) { return all.indexOf(c) === i; });
        return categories.length === 1 ? t.format('ScriptsBadgeIgnored', categories[0]) : t.format('ScriptsBadgeTriggersIgnored', ignored.length);
    }

    // renderSummaries
    // Writes what each section holds under its title, with the script's description under General and the trigger
    // to test with under Testing, marks whether the script is enabled, and names a section's warning in its badge.
    function renderSummaries() {
        var script = current();
        if (!script || !folds.general) return;

        folds.general.setSubtitle(script.Description || null);
        folds.general.setBadge(script.Enabled ? { label: t.tr('LabelEnabled'), color: 'green' } : { label: t.tr('Disabled'), color: 'gray' });
        folds.triggers.setSubtitle(counted((script.Triggers || []).length, 'ScriptsSummaryNoTriggers', 'ScriptsSummaryTriggersOne', 'ScriptsSummaryTriggersMany'));
        // The item kinds, only while they can matter. Every kind is green, and a chosen few are blue with their count.
        var kinds = (script.ItemKinds || []).length;
        folds.triggers.setBadge(!needsKinds(script) ? null : kinds === 0
            ? { label: t.tr('ScriptsBadgeAllKinds'), color: 'green' }
            : { label: t.plural(kinds, 'ScriptsBadgeKindsOne', 'ScriptsBadgeKindsMany'), color: 'blue' });
        folds.steps.setSubtitle(counted((script.Operations || []).length, 'ScriptsSummaryNoSteps', 'ScriptsSummaryStepsOne', 'ScriptsSummaryStepsMany'));

        folds.users.setSubtitle(script.AllGroups !== false ? t.tr('GroupsScopePermissive')
            : counted((script.GroupIds || []).length, 'ScriptsSummaryNoGroups', 'ScriptsSummaryGroupsOne', 'ScriptsSummaryGroupsMany'));
        folds.users.setBadge(shown('userWarn') ? { label: ignoredLabel(script), color: 'orange' } : null);
        renderGroups(script);

        // With one condition, All and Any match the same, so only a count of two or more names the match.
        folds.conditions.setSubtitle(counted((script.Conditions || []).length, 'ScriptsSummaryNoConditions', 'ScriptsSummaryConditionsOne',
            script.Match === 'Any' ? 'ScriptsSummaryConditionsAny' : 'ScriptsSummaryConditionsAll'));
        // How many conditions some selected trigger never supplies a value for.
        var never = (script.Conditions || []).filter(function (c) { return !!conditionWarning(script, c); }).length;
        folds.conditions.setBadge(never ? { label: t.plural(never, 'ScriptsBadgeNeverMatchesOne', 'ScriptsBadgeNeverMatchesMany'), color: 'orange' } : null);

        folds.secrets.setSubtitle(counted((script.Secrets || []).length, 'ScriptsSummaryNoSecrets', 'ScriptsSummarySecretsOne', 'ScriptsSummarySecretsMany'));

        var mode = el('forEachMode');
        var behavior = [mode.options[mode.selectedIndex] ? mode.options[mode.selectedIndex].textContent : ''];
        if (script.CooldownSeconds > 0) behavior.push(t.format('ScriptsSummaryCooldown', shortDuration(script.CooldownSeconds)));
        var onFailure = scripts.find(function (s) { return s.Id === compactId(script.OnFailureScriptId); });
        if (onFailure) behavior.push(t.format('ScriptsSummaryOnFailure', onFailure.Name || t.tr('ScriptsScriptUntitled')));
        folds.behavior.setSubtitle(behavior.filter(Boolean).join(', '));
        // Running for each library item needs item kinds, and running for each does nothing when every trigger has its own.
        var noKinds = script.ForEach === 'Items' && !(script.ItemKinds || []).length;
        folds.behavior.setBadge(!shown('forEachWarn') ? null : { label: t.tr(noKinds ? 'ScriptsBadgeNoKinds' : 'ScriptsBadgeForEachIgnored'), color: 'orange' });

        var test = el('testTrigger');
        folds.testing.setSubtitle(test.options[test.selectedIndex] ? t.format('ScriptsSummaryTestWith', test.options[test.selectedIndex].textContent) : null);
    }

    // needsKinds
    // Whether item kinds can matter to the script: a selected trigger carries an item, or it runs for each library item.
    function needsKinds(script) {
        return script.ForEach === 'Items' || selectedTriggers(script).some(function (t) { return t.HasItem; });
    }

    // dropUnusedKinds
    // Clears the item kinds once nothing the script runs on carries an item, since they would filter nothing.
    function dropUnusedKinds() {
        var script = current();
        if (needsKinds(script) || !(script.ItemKinds || []).length) return;
        script.ItemKinds = [];
        suppressWidgetEvents = true;
        itemKindSelect.setValue([]);
        suppressWidgetEvents = false;
    }

    function onTriggersChanged() {
        ensureWebhookToken(current());
        dropUnusedKinds();
        renderTriggerOptions();
        renderConditions();
        renderOperationCards();
        renderVariables();
        renderTestTriggers();
        renderWarnings();
        edited();
    }

    function kindItems(kinds) {
        return (kinds || []).map(function (k) {
            var match = catalog.ItemKinds.find(function (x) { return x.Value === k; });
            return { value: k, label: match ? match.Label : k };
        });
    }

    function renderWarnings() {
        var script = current();
        var triggers = selectedTriggers(script);

        Shared.setVisible('itemKindOptions', needsKinds(script));

        var library = triggers.filter(function (t) { return t.Category === 'Library' && t.HasItem; });
        var kindWarn = library.length > 0 && (script.ItemKinds || []).length === 0;
        el('itemKindWarn').textContent = kindWarn ? t.format('ScriptsWarnNoItemKinds', library.map(quoted).join(', ')) : '';
        Shared.setVisible('itemKindWarn', kindWarn);

        var noUser = triggers.filter(function (t) { return !carriesUser(script, t); });
        var userWarn = script.AllGroups === false && noUser.length > 0;
        var ignored = noUser.map(quoted).join(', ');
        el('userWarn').textContent = !userWarn ? '' : noUser.length === 1 ? t.format('ScriptsWarnUsersIgnoredOne', ignored) : t.format('ScriptsWarnUsersIgnoredMany', ignored);
        Shared.setVisible('userWarn', userWarn);

        var mode = script.ForEach || 'None';
        Shared.setVisible('forEachLimitRow', mode !== 'None');
        var eachWarn = '';
        if (mode === 'Items' && (script.ItemKinds || []).length === 0) {
            eachWarn = t.tr('ScriptsWarnForEachNoKinds');
        } else if (mode !== 'None' && triggers.length && !triggers.some(function (t) { return expands(script, t); })) {
            eachWarn = mode === 'Items' ? t.tr('ScriptsWarnForEachItemsAlready') : t.tr('ScriptsWarnForEachUsersAlready');
        }
        el('forEachWarn').textContent = eachWarn;
        Shared.setVisible('forEachWarn', !!eachWarn);
    }

    // groupOptions
    // Lists the groups a script can pick, Everyone first under its name in the viewer's language.
    //
    // Output
    // [object[]]: Each group's Id and Name.
    function groupOptions() {
        var everyone = nectoGroups.filter(function (g) { return g.IsEveryone; });
        return everyone.concat(nectoGroups.filter(function (g) { return !g.IsEveryone; })).map(function (g) {
            return { Id: compactId(g.Id), Name: g.IsEveryone ? t.tr('GroupEveryone') : g.Name };
        });
    }

    // renderGroups
    // Shows whether the open script is Permissive or Restrictive, and the groups it picks while Restrictive.
    //
    // Inputs
    // script [object]: The open script.
    function renderGroups(script) {
        var all = script.AllGroups !== false;
        if (scopeChoice) scopeChoice.set(all ? 'all' : 'some');
        var picked = (script.GroupIds || []).map(compactId);
        if (groupPicker && groupPicker.getValue().map(compactId).join() !== picked.join()) {
            var quiet = suppressWidgetEvents;
            suppressWidgetEvents = true;
            groupPicker.setValue(picked);
            suppressWidgetEvents = quiet;
        }
        Shared.setVisible('scriptGroupPicks', !all);
        Shared.setVisible('groupWarn', !all && !(script.GroupIds || []).length);
    }

    // ---- Trigger options ----

    function renderTriggerOptions() {
        var script = current();
        showNumbers();
        Shared.setVisible('scheduleOptions', has(script, 'Schedule'));
        Shared.setVisible('webhookOptions', has(script, 'Webhook'));
        if (has(script, 'Webhook')) renderWebhook();
        el('scheduleStart').textContent = has(script, 'Schedule') ? scheduleStart(script) : '';
        Shared.setVisible('scheduleStart', has(script, 'Schedule'));
    }

    // The schedule a script's task starts with, until it is changed under Scheduled Tasks.
    function scheduleStart(script) {
        if (script.ScheduleMode === 'Interval') return t.format('ScriptsScheduleStartInterval', script.ScheduleIntervalMinutes || 60);
        if (script.ScheduleMode === 'None' || script.ScheduleMode === 'Monthly') return t.tr('ScriptsScheduleStartNone');
        var days = script.ScheduleDays || [];
        var time = script.ScheduleTime || '03:00';
        return days.length ? t.format('ScriptsScheduleStartDays', days.map(dayLabel).join(', '), time) : t.format('ScriptsScheduleStartDaily', time);
    }

    // Shows what the script holds, so a cleared field reads as empty rather than as a default it does not have.
    function orDefault(value, fallback) {
        return value === undefined || value === null ? fallback : value;
    }

    function renderWebhook() {
        var path = 'Scripts/Webhook/' + (current().WebhookToken || '');
        var url = window.ApiClient && window.ApiClient.getUrl ? window.ApiClient.getUrl(path) : '/' + path;
        webhookField.setValue(url);
        el('webhookExample').textContent =
            'curl -X POST "' + url + '" \\\n' +
            '  -H \'Authorization: MediaBrowser Token="YOUR_API_KEY"\' \\\n' +
            '  -H "Content-Type: application/json" \\\n' +
            '  -d \'{"title": "Example"}\'';
    }

    // ---- Conditions ----

    function valueOptions(field) {
        if (field === 'User') return catalog.Users.map(function (u) { return { value: u.Id, label: u.Name }; });
        if (field === 'Library') return catalog.Libraries.map(function (l) { return { value: l.Id, label: l.Name }; });
        if (field === 'TaskKey') return catalog.Tasks.map(function (t) { return { value: t.Key, label: t.Name }; });
        if (field === 'CollectionName') return catalog.Collections.map(function (c) { return { value: c.Name, label: c.Name }; });
        if (field === 'DayOfWeek') return DAYS.map(function (d) { return { value: d, label: dayLabel(d) }; });
        if (FIXED_VALUES[field]) return FIXED_VALUES[field].map(function (v) { return { value: v, label: v }; });
        return [];
    }

    function labelled(field, values) {
        var options = valueOptions(field);
        return (values || []).map(function (v) {
            var match = options.find(function (o) { return String(o.value).toLowerCase() === String(v).toLowerCase(); });
            return match || { value: v, label: v };
        });
    }

    function conditionWarning(script, condition) {
        var triggers = selectedTriggers(script);
        if (!triggers.length) return '';
        var info = fieldInfo(condition.Field);
        if (!info) return '';
        var missing = triggers.filter(function (t) { return !supplies(script, t, info); });
        if (!missing.length) return '';
        if (missing.length === triggers.length) return t.tr('ScriptsWarnConditionNoTrigger');
        var names = missing.map(quoted).join(', ');
        return script.Match === 'Any' ? t.format('ScriptsWarnConditionNeverAny', names) : t.format('ScriptsWarnConditionNeverAll', names);
    }

    // conditionItems
    // Turns a script's conditions into Repeater rows. The value cell carries its field, so it knows which choices to offer.
    //
    // Inputs
    // conditions [Array]: The script's conditions.
    //
    // Output
    // [Array]: One row per condition.
    function conditionItems(conditions) {
        return (conditions || []).map(function (c) {
            return { Field: c.Field, Operator: c.Operator, Values: { field: c.Field, values: (c.Values || []).slice() } };
        });
    }

    // conditionValueField
    // The Repeater's value cell: a number box for a numeric field, a Base Chip Select for a field with choices, which
    // lists the choices that match as you type and shows their labels, such as user names, while storing their values,
    // and a Base Tag Input for any other field.
    //
    // Inputs
    // value [object]: The cell value, as { field, values }.
    // change [function]: Tells the Repeater the cell changed.
    //
    // Output
    // [object]: The cell's element, input, getValue, setValue, and destroy.
    function conditionValueField(value, change) {
        var state = value || { field: null, values: [] };
        var control = null;

        // Draws the control for the row's field, in place of the one before it.
        function build() {
            var before = control;
            if (NUMERIC_FIELDS[state.field]) {
                var input = document.createElement('input');
                input.setAttribute('is', 'emby-input');
                input.type = 'number';
                input.setAttribute('aria-label', t.tr('LabelValue'));
                input.value = state.values.length ? state.values[0] : '';
                input.addEventListener('input', function () {
                    state.values = input.value === '' ? [] : [input.value];
                    change();
                });
                var holder = document.createElement('div');
                holder.appendChild(input);
                var adorned = adornField(input);
                control = { element: holder.firstElementChild, input: input, destroy: function () { adorned.destroy(); } };
                swap(before);
                return;
            }
            var choices = valueOptions(state.field);
            if (choices.length) {
                var shown = labelled(state.field, state.values);
                // Each chip keeps the value as stored, so a saved condition reads back unchanged.
                var picker = createChipSelect({
                    value: state.values.map(function (v, i) { return { value: v, label: shown[i].label }; }),
                    options: choices,
                    placeholder: choices[0].label,
                    allowCreate: true,
                    pill: true,
                    onChange: function (values) {
                        state.values = values.map(String);
                        change();
                    }
                });
                var search = picker.element.querySelector('input');
                search.setAttribute('aria-label', t.tr('LabelValue'));
                control = { element: picker.element, input: search, destroy: function () { picker.destroy(); } };
                swap(before);
                return;
            }
            var tags = createTagInput({
                values: state.values,
                placeholder: t.tr('ScriptsPlaceholderConditionValue'),
                inputLabel: t.tr('LabelValue'),
                onChange: function (values) {
                    state.values = values;
                    change();
                }
            });
            control = { element: tags.element, input: tags.input, destroy: function () { tags.destroy(); } };
            swap(before);
        }

        // Puts the new control where the old one was, so it stays the cell's own child.
        function swap(before) {
            if (!before) return;
            if (before.element.parentNode) before.element.parentNode.replaceChild(control.element, before.element);
            before.destroy();
        }

        build();
        return {
            get element() { return control.element; },
            get input() { return control.input; },
            getValue: function () { return { field: state.field, values: state.values.slice() }; },
            setValue: function (next) {
                state = next || { field: null, values: [] };
                state.values = (state.values || []).slice();
                build();
            },
            destroy: function () { if (control) control.destroy(); }
        };
    }

    // onConditionsChanged
    // Reads the Repeater back into the script, and clears a row's values when its field changes.
    //
    // Inputs
    // items [Array]: The Repeater's rows.
    function onConditionsChanged(items) {
        if (conditionsQuiet) return;
        var reset = false;
        items.forEach(function (item) {
            if (item.Values && item.Values.field === item.Field) return;
            item.Values = { field: item.Field, values: [] };
            if (NUMERIC_FIELDS[item.Field] && item.Operator === 'Is') item.Operator = 'AtLeast';
            if (!NUMERIC_FIELDS[item.Field] && (item.Operator === 'AtLeast' || item.Operator === 'AtMost')) item.Operator = 'Is';
            reset = true;
        });
        current().Conditions = items.map(function (item) {
            return { Field: item.Field, Operator: item.Operator, Values: item.Values.values };
        });
        renderConditionWarnings();
        edited();
        // A new field offers other choices, so its row is drawn again.
        if (reset) setTimeout(renderConditions, 0);
    }

    // renderConditionWarnings
    // Lists the conditions that a selected trigger never supplies a value for.
    function renderConditionWarnings() {
        var script = current();
        var host = el('conditionWarnings');
        host.innerHTML = '';
        script.Conditions.forEach(function (condition, index) {
            var warning = conditionWarning(script, condition);
            if (!warning) return;
            var note = document.createElement('div');
            note.className = 'stolon-warn';
            note.textContent = t.format('ScriptsWarnConditionNumbered', index + 1, warning);
            host.appendChild(note);
        });
    }

    // renderConditions
    // Draws the script's conditions with Base's Repeater.
    function renderConditions() {
        var script = current();
        if (!conditionRepeater) {
            var operators = operatorLabels();
            conditionRepeater = createRepeater({
                fields: [
                    {
                        key: 'Field', label: t.tr('ScriptsLabelField'), type: 'select', width: '1fr',
                        options: fieldGroups().reduce(function (all, group) {
                            return all.concat(group.fields.map(function (f) { return { value: f.key, label: f.label }; }));
                        }, [])
                    },
                    {
                        key: 'Operator', label: t.tr('ScriptsLabelOperator'), type: 'select', width: '1fr',
                        options: Object.keys(operators).map(function (o) { return { value: o, label: operators[o] }; })
                    },
                    { key: 'Values', label: t.tr('LabelValue'), type: 'custom', width: '2fr', render: conditionValueField }
                ],
                addText: t.tr('ScriptsButtonAddCondition'),
                rowName: t.tr('ScriptsRowCondition'),
                emptyText: t.tr('ScriptsEmptyConditions'),
                onChange: onConditionsChanged
            });
            el('conditionList').appendChild(conditionRepeater.element);
        }
        conditionsQuiet = true;
        conditionRepeater.setValue(conditionItems(script.Conditions));
        conditionsQuiet = false;
        renderConditionWarnings();
    }

    // ---- Steps ----

    function operationSummary(op) {
        switch (op.Type) {
            case 'RunCommand': return op.Command || t.tr('ScriptsSummaryNoCommand');
            case 'HttpRequest': return (op.Method || 'POST') + ' ' + (op.Url || t.tr('ScriptsSummaryNoUrl'));
            case 'SendMessage': return op.Text || t.tr('ScriptsSummaryNoMessage');
            case 'StopPlayback': return op.Text ? t.tr('ScriptsSummaryMessageThenStop') : t.tr('ScriptsSummaryStopSession');
            case 'AddTag': case 'RemoveTag': return op.Tag || t.tr('ScriptsSummaryNoTag');
            case 'AddToCollection': case 'RemoveFromCollection': {
                var c = catalog.Collections.find(function (x) { return compactId(x.Id) === compactId(op.CollectionId); });
                return c ? c.Name : t.tr('ScriptsSummaryNoCollection');
            }
            case 'RunScript': {
                var s = scripts.find(function (x) { return compactId(x.Id) === compactId(op.ScriptId); });
                return s ? s.Name : t.tr('ScriptsSummaryNoScript');
            }
            case 'RunScheduledTask': {
                var task = catalog.Tasks.find(function (x) { return x.Key === op.TaskKey; });
                return task ? task.Name : t.tr('ScriptsSummaryNoTask');
            }
            case 'AppendToFile': return op.FilePath || t.tr('ScriptsSummaryNoFile');
            case 'Wait': return t.format('ValueSeconds', op.WaitSeconds || 0);
            case 'SetFavorite': return op.Favorite ? t.tr('ScriptsSummaryAddFavorite') : t.tr('ScriptsSummaryRemoveFavorite');
            case 'SetUserDisabled': return op.Disable ? t.tr('ScriptsOptionDisableTheUser') : t.tr('ScriptsOptionEnableUser');
            case 'WriteActivityLog': return op.Title || t.tr('ScriptsSummaryActivityEntry');
            case 'RefreshMetadata': return op.FullRefresh ? t.tr('ScriptsLabelFullRefresh') : t.tr('ScriptsSummaryStandardRefresh');
            case 'StopScript': return t.tr('ScriptsSummaryStopScript');
            default: return '';
        }
    }

    function operationWarning(script, op) {
        var types = operationTypes();
        var needs = types[op.Type] && types[op.Type].needs;
        if (op.Type === 'SendMessage') needs = op.Target === 'EventSession' ? 'session' : (op.Target === 'UserSessions' ? 'user' : null);
        var triggers = selectedTriggers(script);
        if (!needs || !triggers.length) return '';
        var lacks = function (t) {
            if (needs === 'session') return !t.HasSession;
            if (needs === 'item') return !carriesItem(script, t);
            if (needs === 'user') return !carriesUser(script, t);
            return !carriesItem(script, t) || !carriesUser(script, t);
        };
        var missing = triggers.filter(lacks);
        if (!missing.length) return '';
        var names = missing.map(quoted).join(', ');
        var one = missing.length === 1;
        if (needs === 'session') return one ? t.format('ScriptsWarnStepSkippedSessionOne', names) : t.format('ScriptsWarnStepSkippedSessionMany', names);
        if (needs === 'item') return one ? t.format('ScriptsWarnStepSkippedItemOne', names) : t.format('ScriptsWarnStepSkippedItemMany', names);
        if (needs === 'user') return one ? t.format('ScriptsWarnStepSkippedUserOne', names) : t.format('ScriptsWarnStepSkippedUserMany', names);
        return one ? t.format('ScriptsWarnStepSkippedUserItemOne', names) : t.format('ScriptsWarnStepSkippedUserItemMany', names);
    }

    function newOperation(type) {
        return {
            Type: type, ContinueOnError: false,
            Command: '', Arguments: '', RunThroughShell: false, WorkingDirectory: '', TimeoutSeconds: 0,
            Method: 'POST', Url: '', Headers: '', Body: '', ContentType: 'application/json', Retries: 0,
            Title: '', Text: '', Target: 'EventSession', DisplaySeconds: type === 'SendMessage' ? 10 : 0,
            TaskKey: '', FullRefresh: false, ReplaceAll: false, WaitSeconds: type === 'Wait' ? 5 : 0,
            Tag: '', CollectionId: '', Favorite: true, Disable: true, ScriptId: '', FilePath: '',
            OutputName: '', OutputJsonPath: '', OnlyIfVariable: '', OnlyIfOperator: 'Is', OnlyIfValue: ''
        };
    }

    function renderOperations() {
        var script = current();
        operationList.setItems(script.Operations);
        operationList.setSelected(-1);
        renderOperationCards();
    }

    function renderOperationCards() {
        var script = current();
        el('operationEmpty').innerHTML = script.Operations.length ? '' : Shared.emptySection(t.tr('ScriptsEmptySteps'), { inline: true });
        Shared.setVisible('operationEmpty', script.Operations.length === 0);
    }

    // A step's text field, or with opts.number a mount for the Base control STEP_NUMBERS names for its key.
    function input(op, key, label, opts) {
        opts = opts || {};
        var id = 'opField' + key;
        var value = op[key] === null || op[key] === undefined ? '' : op[key];
        var field = opts.number
            ? '<div data-number-mount="' + key + '"></div>'
            : opts.textarea
                ? '<textarea is="emby-textarea" class="emby-textarea stolon-mono" id="' + id + '" data-key="' + key + '" rows="' + (opts.rows || 3) + '"' +
                    (opts.placeholder ? ' placeholder="' + esc(opts.placeholder) + '"' : '') + '>' + esc(value) + '</textarea>'
                : '<input is="emby-input" type="text" id="' + id + '" data-key="' + key + '" value="' + esc(value) + '"' +
                    (opts.placeholder ? ' placeholder="' + esc(opts.placeholder) + '"' : '') + ' />';
        // A Base control sits in a plain field, since Jellyfin stretches every input inside an inputContainer.
        // A text field the server fills placeholders in lists the script's variables under it, to insert at the cursor.
        var hint = opts.number || opts.variables === false ? '' : Shared.placeholderHint(variableRows.map(function (v) { return [v.Name, v.Description]; }),
            { open: '{{', close: '}}', label: t.tr('ScriptsHeaderVariables') });
        return '<div class="' + (opts.number ? 'stolon-field' : 'inputContainer') + '"><label' + (opts.number ? '' : ' for="' + id + '"') + '>' + esc(label) + '</label>' + field +
            (opts.help ? '<div class="fieldDescription">' + esc(opts.help) + '</div>' : '') + hint + '</div>';
    }

    // mountStepNumbers
    // Puts a Base control in each number mount of the open step's editor, writing the step as it changes.
    //
    // Inputs
    // op [object]: The open step.
    function mountStepNumbers(op) {
        el('operationEditor').querySelectorAll('[data-number-mount]').forEach(function (mount) {
            var key = mount.getAttribute('data-number-mount');
            var control = numberControl(stepNumbers()[key], function (value) {
                op[key] = value === null || value === undefined ? 0 : value;
                edited();
            });
            control.setValue(op[key] || 0);
            mount.appendChild(control.element);
            editorControls.push(control);
        });
    }

    // mountVariableField
    // Puts a Base Combo Box for the step condition's variable in its mount, offering the script's variable names.
    //
    // Inputs
    // op [object]: The open step.
    function mountVariableField(op) {
        var combo = createComboBox({
            value: op.OnlyIfVariable || '',
            suggestions: variableNames,
            placeholder: t.tr('ScriptsPlaceholderStepVariable'),
            onChange: function (value) {
                var fixed = upperName(value);
                if (fixed !== value) combo.setValue(fixed);
                op.OnlyIfVariable = fixed;
                edited();
            }
        });
        combo.input.id = 'opFieldOnlyIfVariable';
        combo.input.classList.add('stolon-mono');
        var mount = el('onlyIfVariableMount');
        mount.parentNode.replaceChild(combo.element, mount);
        editorControls.push(combo);
        variableCombo = combo;
    }

    // upperName
    // A variable name as the server keys it: capitals, digits, and underscores, without braces, $ or JF_.
    function upperName(text) {
        return String(text || '').toUpperCase().replace(/^\{+|\}+$|^\$/g, '').replace(/^JF_/, '').replace(/[^A-Z0-9_]/g, '_');
    }

    function checkbox(op, key, label, help) {
        return '<div class="checkboxContainer' + (help ? ' checkboxContainer-withDescription' : '') + '"><label class="emby-checkbox-label">' +
            '<input type="checkbox" is="emby-checkbox" data-key="' + key + '"' + (op[key] ? ' checked' : '') + ' />' +
            '<span class="checkboxLabel">' + esc(label) + '</span></label>' +
            (help ? '<div class="fieldDescription checkboxFieldDescription">' + esc(help) + '</div>' : '') + '</div>';
    }

    function select(op, key, label, options, opts) {
        opts = opts || {};
        return '<div class="selectContainer"><label>' + esc(label) + '</label>' +
            '<select is="emby-select" data-key="' + key + '"' + (opts.bool ? ' data-bool="1"' : '') + '>' +
            options.map(function (o) {
                return '<option value="' + esc(o.value) + '"' + (String(op[key]) === String(o.value) ? ' selected' : '') + '>' + esc(o.label) + '</option>';
            }).join('') + '</select>' + (opts.help ? '<div class="fieldDescription">' + esc(opts.help) + '</div>' : '') + '</div>';
    }

    function note(text) {
        return '<div class="fieldDescription">' + esc(text) + '</div>';
    }

    // richText
    // A string whose {0}, {1} stand for code samples, as HTML with each sample in a code element.
    //
    // Inputs
    // text [string]: The translated string.
    // codes [Array]: The code samples, in placeholder order.
    //
    // Output
    // [string]: The HTML.
    function richText(text, codes) {
        return esc(text).replace(/\{(\d+)\}/g, function (match, index) {
            return codes[+index] === undefined ? match : '<code>' + esc(codes[+index]) + '</code>';
        });
    }

    function operationFields(op) {
        switch (op.Type) {
            case 'RunCommand':
                return '<div class="stolon-choice-group" role="radiogroup" aria-label="' + esc(t.tr('ScriptsLabelCommandMode')) + '">' +
                        '<label class="stolon-choice"><input type="radio" name="commandMode" value="direct"' + (op.RunThroughShell ? '' : ' checked') + ' />' +
                            '<span class="stolon-choice-name">' + esc(t.tr('ScriptsChoiceRunDirectly')) + '</span><span class="stolon-choice-text">' + esc(t.tr('ScriptsHelpRunDirectly')) + '</span></label>' +
                        '<label class="stolon-choice"><input type="radio" name="commandMode" value="shell"' + (op.RunThroughShell ? ' checked' : '') + ' />' +
                            '<span class="stolon-choice-name">' + esc(t.tr('ScriptsChoiceThroughShell')) + '</span><span class="stolon-choice-text">' + esc(t.tr('ScriptsHelpThroughShell')) + '</span></label>' +
                    '</div>' +
                    input(op, 'Command', op.RunThroughShell ? t.tr('ScriptsLabelShellLine') : t.tr('ScriptsLabelCommand'), {
                        placeholder: op.RunThroughShell ? '/scripts/notify.sh "$JF_ITEM_NAME" >> /tmp/log.txt' : '/scripts/notify.sh',
                        help: op.RunThroughShell
                            ? t.tr('ScriptsHelpShellLine')
                            : t.tr('ScriptsHelpCommand')
                    }) +
                    (op.RunThroughShell ? '' : '<button is="emby-button" type="button" class="raised stolon-button stolon-secondary" id="btnCheckPath"><span>' + esc(t.tr('ScriptsButtonCheckPath')) + '</span></button>') +
                    (op.RunThroughShell ? '' : input(op, 'Arguments', t.tr('ScriptsLabelArguments'), {
                        textarea: true,
                        placeholder: '--title\n{{ITEM_NAME}}',
                        help: t.tr('ScriptsHelpArguments')
                    })) +
                    input(op, 'WorkingDirectory', t.tr('ScriptsLabelWorkingFolder'), { placeholder: t.tr('ScriptsPlaceholderWorkingFolder'), help: t.tr('ScriptsHelpWorkingFolder') }) +
                    input(op, 'TimeoutSeconds', t.tr('ScriptsLabelTimeout'), { number: true, help: t.tr('ScriptsHelpStepTimeout') });
            case 'HttpRequest':
                return select(op, 'Method', t.tr('ScriptsLabelMethod'), HTTP_METHODS.map(function (m) { return { value: m, label: m }; })) +
                    input(op, 'Url', t.tr('LabelUrl'), { placeholder: 'https://example.com/hook?item={{ITEM_NAME:url}}' }) +
                    input(op, 'Headers', t.tr('ScriptsLabelHeaders'), { textarea: true, rows: 2, placeholder: 'Authorization: Bearer {{SECRET_TOKEN}}' }) +
                    input(op, 'ContentType', t.tr('LabelContentType'), { placeholder: 'application/json', variables: false }) +
                    input(op, 'Body', t.tr('LabelBody'), { textarea: true, rows: 5, placeholder: '{ "text": "{{USER_NAME:json}} started {{ITEM_NAME:json}}" }' }) +
                    input(op, 'Retries', t.tr('ScriptsLabelRetries'), { number: true, help: t.tr('ScriptsHelpRetries') });
            case 'SendMessage':
                return select(op, 'Target', t.tr('ScriptsLabelSendTo'), messageTargets()) +
                    input(op, 'Title', t.tr('LabelTitle')) +
                    input(op, 'Text', t.tr('LabelMessage'), { textarea: true, rows: 2 }) +
                    input(op, 'DisplaySeconds', t.tr('ScriptsLabelShowFor'), { number: true, help: t.tr('ScriptsHelpShowFor') });
            case 'StopPlayback':
                return input(op, 'Text', t.tr('ScriptsLabelStopMessage'), { textarea: true, rows: 2, help: t.tr('ScriptsHelpOptional') }) +
                    input(op, 'Title', t.tr('LabelMessageTitle'), { help: t.tr('ScriptsHelpOptional') }) +
                    input(op, 'DisplaySeconds', t.tr('ScriptsLabelShowFor'), { number: true });
            case 'PausePlayback':
                return note(t.tr('ScriptsHelpPausePlayback'));
            case 'MarkPlayed':
            case 'MarkUnplayed':
                return note(t.tr('ScriptsHelpEventItemForUser'));
            case 'SetFavorite':
                return select(op, 'Favorite', t.tr('Favorite'), [{ value: 'true', label: t.tr('ScriptsOptionAddFavorite') }, { value: 'false', label: t.tr('ScriptsOptionRemoveFavorite') }], { bool: true, help: t.tr('ScriptsHelpEventItemForUser') });
            case 'AddTag':
            case 'RemoveTag':
                return input(op, 'Tag', t.tr('LabelTag'), { placeholder: t.tr('ScriptsPlaceholderTag'), help: t.tr('ScriptsHelpTag') });
            case 'AddToCollection':
            case 'RemoveFromCollection':
                return select(op, 'CollectionId', t.tr('LabelCollection'), [{ value: '', label: catalog.Collections.length ? t.tr('ScriptsOptionChooseCollection') : t.tr('ScriptsOptionNoCollections') }].concat(catalog.Collections.map(function (c) {
                    return { value: c.Id, label: c.Name };
                })));
            case 'RefreshMetadata':
                return checkbox(op, 'FullRefresh', t.tr('ScriptsLabelFullRefresh'), t.tr('ScriptsHelpFullRefresh')) +
                    checkbox(op, 'ReplaceAll', t.tr('ScriptsLabelReplaceAll'));
            case 'SetUserDisabled':
                return select(op, 'Disable', t.tr('HeaderAction'), [{ value: 'true', label: t.tr('ScriptsOptionDisableTheUser') }, { value: 'false', label: t.tr('ScriptsOptionEnableUser') }], { bool: true, help: t.tr('ScriptsHelpSetUserDisabled') });
            case 'SignOutUser':
                return note(t.tr('ScriptsHelpSignOutUser'));
            case 'ScanLibraries':
                return note(t.tr('ScriptsHelpScanLibraries'));
            case 'RunScript':
                return select(op, 'ScriptId', t.tr('ScriptsLabelScript'), [{ value: '', label: t.tr('ScriptsOptionChooseScript') }].concat(scripts.filter(function (s) { return s.Id !== selectedId; }).map(function (s) {
                    return { value: s.Id, label: s.Name };
                })), { help: t.tr('ScriptsHelpRunScript') });
            case 'RunScheduledTask':
                return select(op, 'TaskKey', t.tr('ScriptsLabelScheduledTask'), [{ value: '', label: t.tr('ScriptsOptionChooseTask') }].concat(catalog.Tasks.map(function (task) {
                    return { value: task.Key, label: task.Name };
                })));
            case 'WriteActivityLog':
                return input(op, 'Title', t.tr('ScriptsLabelEntryName'), { placeholder: t.tr('ScriptsPlaceholderEntryName') }) +
                    input(op, 'Text', t.tr('LabelDetails'), { textarea: true, rows: 2 });
            case 'AppendToFile':
                return input(op, 'FilePath', t.tr('File'), { placeholder: '/config/log/scripts.log', help: t.tr('ScriptsHelpAppendFile'), variables: false }) +
                    input(op, 'Text', t.tr('ScriptsLabelLineToAppend'), { textarea: true, rows: 2, placeholder: '{{LOCAL_DATE}} {{LOCAL_TIME}} {{EVENT}} {{ITEM_NAME}}' });
            case 'Wait':
                return input(op, 'WaitSeconds', t.tr('ScriptsLabelWaitFor'), { number: true });
            case 'StopScript':
                return note(t.tr('ScriptsHelpStopScript'));
            default:
                return '';
        }
    }

    // A Base Field for a Field Row: a label over a text box or a select.
    function rowField(id, label, control) {
        return '<div class="stolon-field"><label for="' + id + '">' + esc(label) + '</label>' + control + '</div>';
    }

    // What every step has beyond its own fields: a condition, and for a command or HTTP request a saved output,
    // each a Base Field Row under a Subsection Title.
    function stepExtras(op) {
        var operators = operatorLabels();
        var http = op.Type === 'HttpRequest';
        var condition = '<h3 class="stolon-subsection-title">' + esc(t.tr('ScriptsHeaderStepCondition')) + '</h3><div class="stolon-field-row">' +
            rowField('opFieldOnlyIfVariable', t.tr('ScriptsLabelVariable'), '<div id="onlyIfVariableMount"></div>') +
            rowField('opFieldOnlyIfOperator', t.tr('ScriptsLabelOperator'), '<select is="emby-select" id="opFieldOnlyIfOperator" data-key="OnlyIfOperator">' + Object.keys(operators).map(function (o) {
                return '<option value="' + o + '"' + (o === (op.OnlyIfOperator || 'Is') ? ' selected' : '') + '>' + esc(operators[o]) + '</option>';
            }).join('') + '</select>') +
            rowField('opFieldOnlyIfValue', t.tr('LabelValue'), '<input is="emby-input" type="text" id="opFieldOnlyIfValue" data-key="OnlyIfValue" value="' + esc(op.OnlyIfValue) + '" placeholder="' + esc(t.tr('ScriptsPlaceholderStepValue')) + '" />') +
            '</div><div class="fieldDescription">' + esc(t.tr('ScriptsHelpStepCondition')) + '</div>';
        var output = HAS_OUTPUT[op.Type]
            ? '<h3 class="stolon-subsection-title">' + esc(http ? t.tr('ScriptsHeaderSaveResponse') : t.tr('ScriptsHeaderSaveOutput')) + '</h3><div class="stolon-field-row">' +
                rowField('opFieldOutputName', t.tr('ScriptsLabelSaveAs'), '<input is="emby-input" type="text" id="opFieldOutputName" class="stolon-mono" data-key="OutputName" data-upper="1" value="' + esc(op.OutputName) + '" placeholder="' + esc(t.tr('ScriptsPlaceholderSaveAs')) + '" />') +
                rowField('opFieldOutputJsonPath', t.tr('ScriptsLabelJsonField'), '<input is="emby-input" type="text" id="opFieldOutputJsonPath" data-key="OutputJsonPath" value="' + esc(op.OutputJsonPath) + '" placeholder="' + esc(t.tr('ScriptsPlaceholderJsonField')) + '" />') +
                '</div><div class="fieldDescription">' + richText(http ? t.tr('ScriptsHelpSaveResponse') : t.tr('ScriptsHelpSaveOutput'), ['{{NAME}}', '{{NAME_STATUS}}']) + '</div>'
            : '';
        return condition + output;
    }

    function renderOperationEditor(index) {
        var script = current();
        var op = script.Operations[index];
        var editor = el('operationEditor');
        editorControls.forEach(function (control) { control.destroy(); });
        editorControls = [];
        if (!op) { editor.innerHTML = ''; return; }
        var warning = operationWarning(script, op);
        editor.innerHTML =
            (warning ? '<div class="stolon-warn">' + esc(warning) + '</div>' : '') +
            operationFields(op) +
            stepExtras(op) +
            checkbox(op, 'ContinueOnError', t.tr('ScriptsLabelContinueOnError'), t.tr('ScriptsHelpContinueOnError')) +
            '<button is="emby-button" type="button" class="raised stolon-button stolon-destructive" id="btnRemoveOperation"><span>' + esc(t.tr('ScriptsButtonRemoveStep')) + '</span></button>';
        mountStepNumbers(op);
        mountVariableField(op);
        var mode = editor.querySelector('.stolon-choice-group');
        if (mode) {
            createChoiceGroup(mode, {
                onChange: function (value) {
                    op.RunThroughShell = value === 'shell';
                    renderOperationEditor(index);
                    renderOperationCards();
                    edited();
                }
            });
        }
    }

    function selectedOperation() {
        var index = operationList.getSelected();
        return index >= 0 ? current().Operations[index] : null;
    }

    function readOperationField(target) {
        var op = selectedOperation();
        if (!op) return null;
        var key = target.getAttribute('data-key');
        if (target.getAttribute('data-upper')) {
            var caret = target.selectionStart;
            target.value = upperName(target.value);
            try { target.selectionStart = target.selectionEnd = caret; } catch (e) { /* not a text field */ }
        }

        if (target.type === 'checkbox') op[key] = target.checked;
        else if (target.getAttribute('data-bool')) op[key] = target.value === 'true';
        else op[key] = target.value;
        // A changed command has not been checked yet.
        if (key === 'Command') setFieldState(target, '', '');
        return key;
    }

    // checkPath
    // Asks the server whether the command's path runs, and shows the answer under the command as a Base Field Message.
    function checkPath() {
        var op = selectedOperation();
        var command = el('opFieldCommand');
        if (!op || !command) return;
        setFieldState(command, 'info', t.tr('ScriptsStatusChecking'));
        Shared.request('Scripts/CheckPath?path=' + encodeURIComponent(op.Command || ''), 'GET').then(function (check) {
            var ok = check && check.Exists && !check.IsDirectory && check.IsExecutable;
            setFieldState(command, ok ? 'success' : 'error', check ? check.Message : t.tr('ScriptsErrorNoAnswer'));
        }).catch(function () {
            setFieldState(command, 'error', t.tr('ScriptsErrorCheckPath'));
        });
    }

    // ---- Secrets ----

    // secretItems
    // Turns a script's secrets into Repeater rows. A stored value stays on the server, and its row holds the kept
    // sentinel. Each row keeps PreviousName, the name the secret was loaded with, so a renamed secret keeps its value.
    //
    // Inputs
    // secrets [Array]: The script's secrets.
    //
    // Output
    // [Array]: One row per secret.
    function secretItems(secrets) {
        return (secrets || []).map(function (secret) {
            return { Name: secret.Name, Value: secret.Value, PreviousName: secret.PreviousName || '' };
        });
    }

    // secretNameProblem
    // Checks a secret's name: capital letters, digits, and underscores, and no other secret of the script with it.
    function secretNameProblem(value, item, index) {
        if (!value) return '';
        if (!/^[A-Z0-9_]{1,40}$/.test(value)) return t.tr('ScriptsErrorSecretName');
        var rows = secretRepeater ? secretRepeater.getValue() : [];
        var same = rows.filter(function (row) { return row.Name === value; }).length;
        return same > 1 ? t.tr('ScriptsErrorSecretNameTaken') : '';
    }

    // renderSecrets
    // Draws the script's secrets with Base's Repeater, each a name and a Secret field.
    function renderSecrets() {
        if (!secretRepeater) {
            secretRepeater = createRepeater({
                fields: [
                    {
                        key: 'Name', label: t.tr('LabelName'), placeholder: t.tr('ScriptsPlaceholderSecretName'), width: 'minmax(9rem, 1fr)', required: true, validate: secretNameProblem,
                        // Names are capital letters, digits, and underscores, so typing fixes them as they go.
                        transform: function (text) { return text.toUpperCase().replace(/[^A-Z0-9_]/g, '_'); }
                    },
                    { key: 'Value', label: t.tr('LabelValue'), type: 'secret', placeholder: t.tr('LabelValue'), width: 'minmax(11rem, 2fr)', clearable: true }
                ],
                addText: t.tr('ScriptsButtonAddSecret'),
                addSize: 'medium',
                rowName: t.tr('ScriptsRowSecret'),
                emptyText: t.tr('ScriptsEmptySecrets'),
                onChange: onSecretsChanged
            });
            el('secretMount').appendChild(secretRepeater.element);
        }
        secretRepeater.setValue(secretItems(current().Secrets));
    }

    // onSecretsChanged
    // Reads the Repeater back into the script.
    //
    // Inputs
    // items [Array]: The Repeater's rows.
    function onSecretsChanged(items) {
        current().Secrets = items.map(function (item) {
            var secret = { Name: item.Name || '', Value: item.Value || '' };
            if (item.PreviousName) secret.PreviousName = item.PreviousName;
            return secret;
        });
        renderVariables();
        edited();
    }

    // ---- Variables and test ----

    // renderVariables
    // Gathers the variables the open script's steps can use: its triggers', its for-each, its steps' saved outputs,
    // the failure variables when another script runs it on failure, and its secrets. Each step text field lists them.
    function renderVariables() {
        var script = current();
        var triggers = selectedTriggers(script);
        if (!triggers.length) triggers = catalog.Triggers;
        var seen = {};
        var rows = [];
        var add = function (v) {
            if (seen[v.Name]) return;
            seen[v.Name] = true;
            rows.push(v);
        };
        triggers.forEach(function (t) { t.Variables.forEach(add); });
        if (script.ForEach === 'Items' || script.ForEach === 'Users') (catalog.ForEachVariables[script.ForEach] || []).forEach(add);
        (script.Operations || []).forEach(function (op, index) {
            if (!HAS_OUTPUT[op.Type] || !op.OutputName) return;
            add({ Name: op.OutputName, Description: t.format('ScriptsVariableStepSaved', index + 1) });
            add({ Name: op.OutputName + '_STATUS', Description: op.Type === 'HttpRequest' ? t.format('ScriptsVariableStepHttpStatus', index + 1) : t.format('ScriptsVariableStepExitCode', index + 1) });
        });
        if (scripts.some(function (x) { return compactId(x.OnFailureScriptId) === script.Id; })) (catalog.FailureVariables || []).forEach(add);
        (script.Secrets || []).forEach(function (secret) {
            if (secret.Name) add({ Name: 'SECRET_' + secret.Name, Description: t.format('ScriptsVariableSecret', secret.Name) });
        });
        variableRows = rows;
        variableNames = rows.map(function (v) { return v.Name; });
        if (variableCombo && variableCombo.element.isConnected) variableCombo.setSuggestions(variableNames);
    }

    function renderTestTriggers() {
        var script = current();
        var triggers = selectedTriggers(script);
        if (!triggers.length) triggers = catalog.Triggers;
        var selectEl = el('testTrigger');
        var previous = selectEl.value;
        selectEl.innerHTML = triggers.map(function (t) {
            return '<option value="' + esc(t.Key) + '">' + esc(t.Label) + '</option>';
        }).join('');
        if (triggers.some(function (t) { return t.Key === previous; })) selectEl.value = previous;
        renderSummaries();
    }

    function runTest() {
        var script = current();
        var problem = validate(script);
        if (problem) {
            Shared.setStatus('scriptStatus', problem, true);
            return;
        }

        var button = el('btnTestScript');
        button.disabled = true;
        var trigger = triggerInfo(el('testTrigger').value);
        var body = document.createElement('div');
        body.innerHTML = '<div class="stolon-info">' + esc(trigger ? t.format('ScriptsTestRunning', trigger.Label) : t.tr('ScriptsTestRunningNoTrigger')) + '</div>';
        openSidecar({
            title: t.format('ScriptsTitleTest', script.Name || t.tr('ScriptsScriptUntitled')),
            body: body,
            size: 'large',
            page: page
        });
        Shared.request('Scripts/Test', 'POST', { Script: script, Trigger: el('testTrigger').value }).then(function (run) {
            body.innerHTML = '';
            body.appendChild(renderRunSummary(Shared, run));
            body.appendChild(renderRunSteps(Shared, run));
        }).catch(function () {
            body.innerHTML = '<div class="stolon-error">' + esc(t.tr('ScriptsErrorTestFailed')) + '</div>';
        }).finally(function () {
            button.disabled = false;
        });
    }

    function runNow() {
        var script = current();
        if (!script) return;
        if (tracker && tracker.isDirty()) {
            Shared.setStatus('scriptStatus', t.tr('ScriptsErrorSaveFirst'), true);
            return;
        }

        var button = el('btnRunScript');
        button.disabled = true;
        Shared.request('Scripts/Run/' + script.Id, 'POST').then(function () {
            Shared.setStatus('scriptStatus', t.tr('ScriptsStatusRunStarted'), false, { timeout: 8000 });
        }).catch(function () {
            Shared.setStatus('scriptStatus', t.tr('ScriptsErrorRunFailed'), true);
        }).finally(function () {
            button.disabled = false;
        });
    }

    // ---- Save ----

    function validate(script) {
        if (!script.Name || !script.Name.trim()) return t.tr('ScriptsValidateNameMissing');
        var label = script.Name;
        for (var c = 0; c < script.Conditions.length; c++) {
            var condition = script.Conditions[c];
            var field = fieldLabel(condition.Field) || condition.Field;
            if (!(condition.Values || []).length) return t.format('ScriptsValidateConditionValue', label, field);
            if ((condition.Operator === 'AtLeast' || condition.Operator === 'AtMost') && condition.Values.some(function (v) { return isNaN(parseFloat(v)); })) {
                return t.format('ScriptsValidateConditionNumbers', label, field, operatorLabels()[condition.Operator]);
            }
        }

        var secretNames = {};
        for (var s = 0; s < (script.Secrets || []).length; s++) {
            var name = script.Secrets[s].Name;
            if (!/^[A-Z0-9_]{1,40}$/.test(name)) return t.format('ScriptsValidateSecretNames', label);
            if (secretNames[name]) return t.format('ScriptsValidateSecretTwice', label, name);
            secretNames[name] = true;
        }

        var outside = function (value, min, max) { return !(value >= min && value <= max); };
        if (outside(script.CooldownSeconds || 0, 0, 86400)) return t.format('ScriptsValidateCooldown', label);
        if (script.Triggers.indexOf('PlaybackPausedFor') >= 0 && outside(script.PausedForMinutes, 1, 1440)) return t.format('ScriptsValidatePausedFor', label);
        if (script.Triggers.indexOf('PlaybackPlayingFor') >= 0 && outside(script.PlayingForMinutes, 1, 1440)) return t.format('ScriptsValidatePlayingFor', label);
        if (script.Triggers.indexOf('PlaybackProgressReached') >= 0 && outside(script.ProgressPercentThreshold, 1, 99)) return t.format('ScriptsValidatePercentage', label);
        if (script.Triggers.indexOf('StreamCountReached') >= 0 && outside(script.StreamCountThreshold, 1, 100)) return t.format('ScriptsValidateStreamCount', label);
        if (script.Triggers.indexOf('TranscodeCountReached') >= 0 && outside(script.TranscodeCountThreshold, 1, 100)) return t.format('ScriptsValidateTranscodeCount', label);
        if (script.ForEach === 'Items' && !(script.ItemKinds || []).length) return t.format('ScriptsValidateForEachKinds', label);
        if (script.ForEach !== 'None' && outside(script.ForEachLimit, 1, 500)) return t.format('ScriptsValidateForEachLimit', label);

        var saved = {};
        for (var i = 0; i < script.Operations.length; i++) {
            var op = script.Operations[i];
            var n = i + 1;
            if (op.OutputName && HAS_OUTPUT[op.Type]) {
                if (!/^[A-Z0-9_]{1,40}$/.test(op.OutputName)) return t.format('ScriptsValidateStepOutputName', label, n);
                if (saved[op.OutputName]) return t.format('ScriptsValidateStepOutputTaken', label, n, op.OutputName, saved[op.OutputName]);
                saved[op.OutputName] = n;
            }
            if (op.OnlyIfVariable && !/^[A-Z0-9_]{1,80}$/.test(op.OnlyIfVariable)) return t.format('ScriptsValidateStepConditionName', label, n);
            if (op.OnlyIfVariable && (op.OnlyIfOperator === 'AtLeast' || op.OnlyIfOperator === 'AtMost') && !/\{\{/.test(op.OnlyIfValue || '') && isNaN(parseFloat(op.OnlyIfValue))) {
                return t.format('ScriptsValidateStepConditionNumber', label, n);
            }
            if (op.Type === 'RunCommand' && !(op.Command || '').trim()) return t.format('ScriptsValidateStepCommand', label, n);
            if (op.Type === 'HttpRequest' && !/^https?:\/\//i.test((op.Url || '').trim())) return t.format('ScriptsValidateStepUrl', label, n);
            if (op.Type === 'SendMessage' && !(op.Text || '').trim()) return t.format('ScriptsValidateStepMessage', label, n);
            if (op.Type === 'RunScheduledTask' && !op.TaskKey) return t.format('ScriptsValidateStepTask', label, n);
            if ((op.Type === 'AddTag' || op.Type === 'RemoveTag') && !(op.Tag || '').trim()) return t.format('ScriptsValidateStepTag', label, n);
            if ((op.Type === 'AddToCollection' || op.Type === 'RemoveFromCollection') && !op.CollectionId) return t.format('ScriptsValidateStepCollection', label, n);
            if (op.Type === 'RunScript' && !op.ScriptId) return t.format('ScriptsValidateStepScript', label, n);
            if (op.Type === 'AppendToFile' && !/^(\/|[A-Za-z]:\\)/.test((op.FilePath || '').trim())) return t.format('ScriptsValidateStepFile', label, n);
            if (op.Type === 'Wait' && outside(op.WaitSeconds, 1, 3600)) return t.format('ScriptsValidateStepWait', label, n);
            if (op.Type === 'RunCommand' && outside(op.TimeoutSeconds || 0, 0, 3600)) return t.format('ScriptsValidateStepTimeout', label, n);
            if (op.Type === 'HttpRequest' && outside(op.Retries || 0, 0, 5)) return t.format('ScriptsValidateStepRetries', label, n);
            if ((op.Type === 'SendMessage' || op.Type === 'StopPlayback') && outside(op.DisplaySeconds || 0, 0, 300)) return t.format('ScriptsValidateStepShowFor', label, n);
        }

        return '';
    }

    // save
    // The Save Bar's save: checks every script, saves them, and reloads them.
    //
    // Output
    // [Promise]: Resolves once saved, and rejects with the reason it did not save, which the Save Bar shows.
    function save() {
        for (var i = 0; i < scripts.length; i++) {
            var problem = validate(scripts[i]);
            if (problem) {
                if (scripts[i].Id !== selectedId) {
                    selectScript(scripts[i].Id);
                    renderSelector();
                }
                return Promise.reject(new Error(problem));
            }
        }

        return Shared.saveConfigWith(function (config) {
            // Only a command or an HTTP request has an output to save.
            config.Scripts = config.Scripts || {};
            config.Scripts.Scripts = scripts.map(function (script) {
                var copy = JSON.parse(JSON.stringify(script));
                copy.Operations.forEach(function (op) { if (!HAS_OUTPUT[op.Type]) { op.OutputName = ''; op.OutputJsonPath = ''; } });
                return copy;
            });
        }).then(function () {
            // Reload so secrets come back as stored and new IDs settle.
            return reloadScripts();
        }, function () {
            throw new Error(t.tr('ScriptsErrorSaveRefused'));
        });
    }

    // ---- New, rename, remove, import, export ----

    function blankScript(name) {
        return {
            Id: compactId(generateGuid()), Name: name || t.tr('ScriptsScriptNewName'), Description: '', Enabled: true, AllGroups: true, GroupIds: [], Triggers: [], ItemKinds: [],
            Match: 'All', Conditions: [], Operations: [], CooldownSeconds: 0, Secrets: [],
            PausedForMinutes: 10, PlayingForMinutes: 60, ScheduleMode: 'None', ScheduleIntervalMinutes: 60,
            ScheduleTime: '03:00', ScheduleDays: [], ScheduleMonthDay: 1,
            ForEach: 'None', ForEachLimit: 50, OnFailureScriptId: '', WebhookToken: '',
            StreamCountThreshold: 3, TranscodeCountThreshold: 2, ProgressPercentThreshold: 90
        };
    }

    // Fills a script from a template or an imported file, keeping only what this version knows, so a file from
    // a newer version or a hand edit cannot leave the editor unable to render.
    function sanitize(source, base) {
        var operators = operatorLabels();
        var types = operationTypes();
        var s = Object.assign(base, {
            Name: String(source.Name || base.Name),
            Description: String(source.Description || ''),
            Triggers: (source.Triggers || base.Triggers).filter(triggerInfo),
            ItemKinds: (source.ItemKinds || []).filter(function (k) { return catalog.ItemKinds.some(function (x) { return x.Value === k; }); }),
            Match: source.Match === 'Any' ? 'Any' : 'All',
            Conditions: (source.Conditions || []).filter(function (c) {
                return c && fieldLabel(c.Field) && operators[c.Operator];
            }).map(function (c) {
                return { Field: c.Field, Operator: c.Operator, Values: Array.isArray(c.Values) ? c.Values.map(String) : [] };
            }),
            Operations: (source.Operations || []).filter(function (op) { return op && types[op.Type]; }).map(function (op) {
                var filled = Object.assign(newOperation(op.Type), op);
                ['OutputName', 'OutputJsonPath', 'OnlyIfVariable', 'OnlyIfValue'].forEach(function (key) { filled[key] = filled[key] === null || filled[key] === undefined ? '' : String(filled[key]); });
                if (!operators[filled.OnlyIfOperator]) filled.OnlyIfOperator = 'Is';
                return filled;
            }),
            Secrets: (source.Secrets || []).filter(function (x) { return x && x.Name; }).map(function (x) {
                return { Name: String(x.Name).toUpperCase(), Value: '' };
            })
        });
        ['CooldownSeconds', 'PausedForMinutes', 'PlayingForMinutes', 'ScheduleIntervalMinutes', 'ScheduleMonthDay',
            'ForEachLimit', 'StreamCountThreshold', 'TranscodeCountThreshold', 'ProgressPercentThreshold'].forEach(function (key) {
            if (source[key] !== undefined) s[key] = parseInt(source[key], 10) || 0;
        });
        if (['Interval', 'Daily', 'Monthly', 'None'].indexOf(source.ScheduleMode) >= 0) s.ScheduleMode = source.ScheduleMode;
        if (typeof source.ScheduleTime === 'string') s.ScheduleTime = source.ScheduleTime;
        if (Array.isArray(source.ScheduleDays)) s.ScheduleDays = source.ScheduleDays.filter(function (d) { return DAYS.indexOf(d) >= 0; });
        if (['None', 'Items', 'Users'].indexOf(source.ForEach) >= 0) s.ForEach = source.ForEach;
        s.OnFailureScriptId = /^[0-9a-f]{32}$/.test(compactId(source.OnFailureScriptId)) ? compactId(source.OnFailureScriptId) : '';
        // The webhook address is never carried over: a copy or an import gets its own.
        s.WebhookToken = '';
        ensureWebhookToken(s);
        return s;
    }

    function addScript(script, message) {
        scripts.push(script);
        selectedId = script.Id;
        edited();
        renderEditor();
        if (message) Shared.setStatus('transferStatus', message, false, { timeout: 0 });
    }

    // newScript
    // Asks for a name and a description in a sidecar, and adds a blank script with them.
    function newScript() {
        promptSidecar([
            { name: 'name', label: t.tr('LabelName'), value: '', placeholder: t.tr('ScriptsPlaceholderNewName') },
            { name: 'description', label: t.tr('ScriptsLabelDescription'), value: '', placeholder: t.tr('ScriptsPlaceholderNewDescription') }
        ], { title: t.tr('ScriptsScriptNewName'), submitText: t.tr('Create') }).then(function (values) {
            if (!values) return;
            var name = (values.name || '').trim() || t.tr('ScriptsScriptNewName');
            var script = sanitize({ Name: name }, blankScript(name));
            script.Description = (values.description || '').trim().slice(0, 500);
            addScript(script, '');
        });
    }

    // renameScript
    // Asks for the open script's new name in a sidecar, and keeps it for Save.
    function renameScript() {
        var script = current();
        if (!script) return;
        var name = script.Name || t.tr('ScriptsScriptUntitled');
        promptSidecar([
            { name: 'name', label: t.tr('LabelName'), value: script.Name || '', placeholder: t.tr('ScriptsPlaceholderNewName') }
        ], { title: t.format('TitleRenameName', name), submitText: t.tr('ButtonRename') }).then(function (values) {
            var next = values ? (values.name || '').trim().slice(0, 200) : '';
            if (!next || next === script.Name) return;
            script.Name = next;
            renderEditor();
            edited();
        });
    }

    // removeScript
    // Asks first, then removes the open script and saves only that, so edits to other scripts wait for Save.
    function removeScript() {
        var script = current();
        if (!script) return;
        var name = script.Name || t.tr('ScriptsScriptUntitled');
        confirmDialog(t.tr('ScriptsConfirmRemoveScript'), { title: t.format('TitleRemoveName', name), confirmText: t.tr('ButtonRemove'), destructive: true }).then(function (ok) {
            if (!ok) return;
            var wasClean = !(tracker && tracker.isDirty());
            Shared.saveConfigWith(function (config) {
                config.Scripts = config.Scripts || {};
                config.Scripts.Scripts = (config.Scripts.Scripts || []).filter(function (s) { return compactId(s.Id) !== script.Id; });
            }, 'transferStatus', { success: t.tr('ScriptsStatusRemoved'), error: t.tr('ScriptsErrorRemoveRefused') }).then(function () {
                scripts = scripts.filter(function (s) { return s.Id !== script.Id; });
                selectedId = scripts.length ? scripts[0].Id : null;
                if (wasClean && tracker) tracker.markClean();
                edited();
                if (selectedId) renderEditor();
            }).catch(function () { /* the status line already says why */ });
        });
    }

    function exportScripts() {
        if (!scripts.length) {
            Shared.setStatus('transferStatus', t.tr('ScriptsErrorNoScriptsToExport'), true);
            return;
        }

        var now = new Date();
        var stamp = now.getFullYear() + '-' + String(now.getMonth() + 1).padStart(2, '0') + '-' + String(now.getDate()).padStart(2, '0');
        var exported = JSON.parse(JSON.stringify(scripts)).map(function (s) {
            s.Secrets = (s.Secrets || []).map(function (x) { return { Name: x.Name, Value: '' }; });
            s.WebhookToken = '';
            return s;
        });
        downloadJson('necto-scripts-' + stamp + '.json', { Format: 'necto-scripts', Version: 1, Scripts: exported });
        var unsaved = !!(tracker && tracker.isDirty());
        var exported = scripts.length === 1
            ? (unsaved ? t.tr('ScriptsStatusExportedOneUnsaved') : t.tr('ScriptsStatusExportedOne'))
            : (unsaved ? t.format('ScriptsStatusExportedManyUnsaved', scripts.length) : t.format('ScriptsStatusExportedMany', scripts.length));
        Shared.setStatus('transferStatus', exported, false);
    }

    // Accepts an export file, a bare array of scripts, or one script.
    function readImport(data) {
        var list = Array.isArray(data) ? data : (data && Array.isArray(data.Scripts) ? data.Scripts : (data && data.Name !== undefined ? [data] : null));
        if (!list) return null;
        return list.filter(function (s) { return s && typeof s === 'object'; }).map(function (s) {
            var id = /^[0-9a-f]{32}$/.test(compactId(s.Id)) ? compactId(s.Id) : compactId(generateGuid());
            var script = sanitize(s, blankScript(String(s.Name || t.tr('ScriptsScriptImportedName'))));
            script.Id = id;
            script.Enabled = false;
            return script;
        });
    }

    function importScripts() {
        pickJsonFile().then(function (data) {
            if (data === null) return;
            var incoming = readImport(data);
            if (!incoming || !incoming.length) {
                Shared.setStatus('transferStatus', t.tr('ScriptsErrorImportEmpty'), true);
                return;
            }

            var replacing = incoming.filter(function (s) {
                return scripts.some(function (e) { return compactId(e.Id) === compactId(s.Id); });
            });
            var replacingNames = replacing.map(function (s) { return '"' + s.Name + '"'; }).join(', ');
            var message = [
                t.plural(incoming.length, 'ScriptsConfirmImportOne', 'ScriptsConfirmImportMany'),
                !replacing.length ? '' : replacing.length === 1 ? t.format('ScriptsConfirmImportReplacingOne', replacingNames) : t.format('ScriptsConfirmImportReplacingMany', replacing.length, replacingNames),
                t.tr('ScriptsConfirmImportReview')
            ].filter(Boolean).join(' ');
            return confirmDialog(message, { title: t.tr('ScriptsDialogImportScripts'), confirmText: t.tr('ScriptsButtonImport') }).then(function (ok) {
                if (!ok) return;
                incoming.forEach(function (s) {
                    var index = scripts.findIndex(function (e) { return compactId(e.Id) === compactId(s.Id); });
                    // A script replaced in place keeps the address other services already call.
                    if (index >= 0 && has(s, 'Webhook') && scripts[index].WebhookToken) s.WebhookToken = scripts[index].WebhookToken;
                    if (index >= 0) scripts[index] = s;
                    else scripts.push(s);
                });
                selectedId = incoming[0].Id;
                edited();
                renderEditor();
                Shared.setStatus('transferStatus', t.format('ScriptsStatusImported', incoming.length), false, { timeout: 0 });
            });
        }).catch(function () {
            Shared.setStatus('transferStatus', t.tr('ScriptsErrorImportJson'), true);
        });
    }

    // ---- Wiring ----

    function bind() {
        foldSections();
        // Scripts holds the picker and the editor, and History each recent day's runs.
        history = createRunHistory(Shared, page);
        if (el('scriptsTabs')) {
            createToolTabs(el('scriptsTabs'), {
                label: t.tr('TabScripts'),
                tabs: [
                    { id: 'scripts', label: t.tr('TabScripts') },
                    { id: 'history', label: t.tr('ScriptsTabHistory'), element: history.element, open: history.load }
                ]
            });
        }
        page.addCleanup(function () { history.destroy(); });
        el('selectScript').addEventListener('change', function () { selectScript(this.value); });
        el('btnNewScript').addEventListener('click', newScript);
        el('btnRenameScript').addEventListener('click', renameScript);
        el('btnRemoveScript').addEventListener('click', removeScript);
        el('btnImportScripts').addEventListener('click', importScripts);
        el('btnExportScripts').addEventListener('click', exportScripts);

        el('scriptDescription').addEventListener('input', function () { current().Description = this.value; edited(); });
        el('scriptEnabled').addEventListener('change', function () { current().Enabled = this.checked; edited(); });
        createNumbers();
        el('btnNewWebhook').addEventListener('click', function () {
            confirmDialog(t.tr('ScriptsConfirmNewAddress'), { title: t.tr('ScriptsTitleNewWebhookAddress'), confirmText: t.tr('ScriptsButtonMakeNewAddress') }).then(function (ok) {
                if (!ok) return;
                current().WebhookToken = newToken();
                renderWebhook();
                edited();
            });
        });
        el('forEachMode').addEventListener('change', function () {
            current().ForEach = this.value;
            dropUnusedKinds();
            renderConditions();
            renderOperationCards();
            renderVariables();
            renderWarnings();
            edited();
        });
        el('onFailureScript').addEventListener('change', function () { current().OnFailureScriptId = this.value; edited(); });
        matchChoice = createChoiceGroup(el('conditionMatch'), {
            onChange: function (value) {
                if (suppressWidgetEvents) return;
                current().Match = value;
                renderConditions();
                edited();
            }
        });

        // Steps: one editor moves into whichever card is open.
        var groups = {};
        var types = operationTypes();
        Object.keys(types).forEach(function (type) { (groups[types[type].group] = groups[types[type].group] || []).push(type); });
        el('newOperationType').innerHTML = Object.keys(groups).map(function (g) {
            return '<optgroup label="' + esc(g) + '">' + groups[g].map(function (type) {
                return '<option value="' + type + '">' + esc(types[type].label) + '</option>';
            }).join('') + '</optgroup>';
        }).join('');

        operationList = createSortableCardList(el('operationList'), {
            items: [],
            editor: el('operationEditor'),
            escapeHtml: Shared.escapeHtml,
            render: function (op) {
                var warning = operationWarning(current(), op);
                var operators = operatorLabels();
                var stepTypes = operationTypes();
                var badges = [];
                if (op.OnlyIfVariable) badges.push({ label: t.format('ScriptsBadgeStepCondition', op.OnlyIfVariable, operators[op.OnlyIfOperator] || operators.Is, op.OnlyIfValue === '' ? t.tr('ScriptsBadgeEmpty') : op.OnlyIfValue), cls: 'blue' });
                if (op.OutputName && HAS_OUTPUT[op.Type]) badges.push({ label: t.format('ScriptsBadgeSaves', op.OutputName), cls: 'purple' });
                if (op.ContinueOnError) badges.push({ label: t.tr('ScriptsBadgeContinuesOnError'), cls: 'gray' });
                if (warning) badges.push({ label: t.tr('ScriptsBadgeSometimesSkipped'), cls: 'orange' });
                return {
                    icon: stepTypes[op.Type] ? stepTypes[op.Type].icon : 'extension',
                    title: stepTypes[op.Type] ? stepTypes[op.Type].label : op.Type,
                    subtitle: operationSummary(op),
                    // A command, an address, or a file path reads in the mono font, and words in the plain one.
                    mono: (op.Type === 'RunCommand' && !!op.Command) || (op.Type === 'HttpRequest' && !!op.Url) || (op.Type === 'AppendToFile' && !!op.FilePath),
                    badges: badges,
                    dot: 'none'
                };
            },
            onSelect: function (index) { renderOperationEditor(index); },
            onMove: function (from, to) {
                var ops = current().Operations;
                var moved = ops.splice(from, 1)[0];
                ops.splice(to, 0, moved);
                operationList.setItems(ops);
                edited();
            }
        });

        el('btnAddOperation').addEventListener('click', function () {
            var ops = current().Operations;
            ops.push(newOperation(el('newOperationType').value));
            operationList.setItems(ops);
            operationList.setSelected(ops.length - 1);
            renderOperationCards();
            edited();
        });

        Shared.bindPlaceholders(el('operationEditor'));
        el('operationEditor').addEventListener('input', function (e) {
            if (e.target.matches('[data-key]') && e.target.type !== 'checkbox' && e.target.tagName !== 'SELECT') {
                if (readOperationField(e.target) === 'OutputName') renderVariables();
                edited();
            }
        });

        el('operationEditor').addEventListener('change', function (e) {
            // A number leaves its control, so the step's card shows it.
            if (e.target.closest('[data-number-mount]')) { operationList.render(); return; }
            if (!e.target.matches('[data-key]')) return;
            readOperationField(e.target);
            operationList.render();
            edited();
        });

        el('operationEditor').addEventListener('click', function (e) {
            if (e.target.closest('#btnCheckPath')) { checkPath(); return; }
            if (e.target.closest('#btnRemoveOperation')) {
                var index = operationList.getSelected();
                var ops = current().Operations;
                ops.splice(index, 1);
                operationList.setItems(ops);
                operationList.setSelected(-1);
                renderOperationCards();
                edited();
            }
        });

        saveBar = createSaveBar({ fill: true, onSave: save });
        // The bar takes the mount's place, so no wrapper sits between it and the page.
        el('saveBarMount').replaceWith(saveBar.element);
        el('btnTestScript').addEventListener('click', runTest);
        el('testTrigger').addEventListener('change', renderSummaries);
        el('btnRunScript').addEventListener('click', runNow);
        webhookField = createCopyField({ label: t.tr('LabelWebhookAddress') });
        el('webhookUrlMount').appendChild(webhookField.element);

        Shared.initCollapsibles();
    }

    function createWidgets() {
        triggerList = createCheckboxList({
            items: catalog.Triggers,
            idKey: 'Key',
            placeholder: t.tr('ScriptsPlaceholderSearchTriggers'),
            searchKeys: ['Label', 'Description', 'Category'],
            // Every trigger shows at once, with the page scrolling, rather than in a box that scrolls inside.
            maxHeight: null,
            // A description wraps at a word, so the whole sentence reads.
            wrapSublabels: true,
            // A phone leaves out the category, so the description has the row's width.
            renderRow: function (t) { return { label: t.Label, sublabel: t.Description, note: narrow() ? null : t.Category }; },
            onChange: function (ids) {
                if (suppressWidgetEvents) return;
                // Keep the catalog's order so the stored list reads the same however it was clicked.
                current().Triggers = catalog.Triggers.map(function (t) { return t.Key; }).filter(function (k) { return ids.indexOf(k) >= 0; });
                onTriggersChanged();
            }
        });
        el('triggerMount').appendChild(triggerList.element);

        itemKindSelect = createChipSelect({
            options: catalog.ItemKinds.map(function (k) { return { value: k.Value, label: k.Label }; }),
            placeholder: t.tr('ScriptsPlaceholderItemKinds'),
            onChange: function (values) {
                if (suppressWidgetEvents) return;
                current().ItemKinds = values;
                renderWarnings();
                edited();
            }
        });
        el('itemKindMount').appendChild(itemKindSelect.element);

        groupPicker = createCheckboxList({
            items: groupOptions(),
            idKey: 'Id',
            matchKey: compactId,
            search: false,
            renderRow: function (g) { return { label: g.Name }; },
            onChange: function (ids) {
                if (suppressWidgetEvents) return;
                current().GroupIds = ids.map(compactId);
                renderWarnings();
                renderSummaries();
                edited();
            }
        });
        el('groupList').appendChild(groupPicker.element);
        scopeChoice = createChoiceGroup(el('scriptGroupScope'), {
            onChange: function (value) {
                if (suppressWidgetEvents) return;
                current().AllGroups = value === 'all';
                renderWarnings();
                renderSummaries();
                edited();
            }
        });


        tracker = Shared.createChangeTracker({
            serialize: function () { return JSON.stringify(scripts); }
        });
    }

    // Reads the scripts back from the server. The configuration holds only the kept sentinel for a stored
    // secret, never its value. PreviousName lets the server find a renamed secret's stored value.
    function reloadScripts() {
        return Shared.getConfig().then(function (config) {
            scripts = ((config.Scripts && config.Scripts.Scripts) || []).map(function (s) {
                var script = sanitize(s, blankScript(s.Name));
                script.Id = compactId(s.Id);
                script.Enabled = !!s.Enabled;
                script.WebhookToken = s.WebhookToken || script.WebhookToken;
                script.AllGroups = s.AllGroups !== false;
                script.GroupIds = (s.GroupIds || []).map(compactId);
                script.Secrets = (s.Secrets || []).map(function (x) {
                    return { Name: x.Name, Value: x.Value ? SECRET_KEPT : '', PreviousName: x.Name };
                });
                return script;
            });

            if (!scripts.some(function (s) { return s.Id === selectedId; })) {
                selectedId = scripts.length ? scripts[0].Id : null;
            }

            if (tracker) tracker.markClean();
            edited();
            if (selectedId) renderEditor();
        });
    }

    function load() {
        // History reads again each time the page shows, once its tab has opened.
        if (history && history.element.parentNode) history.load();
        Promise.all([Shared.request('Scripts/Catalog', 'GET'), Shared.request('Groups').catch(function () { return []; })]).then(function (results) {
            catalog = results[0];
            nectoGroups = results[1] || [];
            if (!triggerList) createWidgets();
            return reloadScripts();
        }).catch(function () {
            Shared.setStatus('scriptStatus', t.tr('ScriptsErrorLoadScripts'), true, { timeout: 0 });
        });
    }
}
