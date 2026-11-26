// necto_scripts_common.js
// Shared by the Scripts tool and its Settings and Troubleshooting sections: the page wrapper, the strings, and the
// labels the server's enum names display as.

import {
    createNectoPage, createChipSelect, createTagInput, createComboBox, adornField, createCheckboxList, createUserMultiSelector, createSortableCardList,
    createChoiceGroup, createCopyField, createTimeline, createDefinitionList, createRepeater, createDataTable,
    createRecordList, createSaveBar, createSliderField, createDurationField, createBarChart, createCollapsibleSection,
    confirmDialog, promptSidecar, openSidecar, generateGuid, downloadJson, pickJsonFile, setFieldState, SECRET_KEPT, openTool
} from './configurationpage?name=necto_shared.js';

export {
    createRepeater, createDataTable, createChipSelect, createTagInput, createComboBox, adornField, createCheckboxList, createUserMultiSelector, createSortableCardList,
    createChoiceGroup, createCopyField, createRecordList, createSaveBar, createSliderField, createDurationField,
    createBarChart, createCollapsibleSection, confirmDialog, promptSidecar, openSidecar, generateGuid, downloadJson, pickJsonFile,
    setFieldState, SECRET_KEPT, openTool
};

// The plugin's strings once a page has them. Until then a key reads as its fallback.
var strings = null;

// t
// Reads the plugin's strings, in the viewer's language once a Scripts page has loaded them.
//
// Output
// [object]: { tr(key, fallback), format(key, ...values), title(key), language }.
export var t = {
    tr: function (key, fallback) { return strings ? strings.tr(key, fallback) : (fallback === undefined ? key : fallback); },
    format: function () { return strings ? strings.format.apply(strings, arguments) : arguments[0]; },
    title: function (key) { return strings && strings.title ? strings.title(key) : t.tr(key); },
    get language() { return strings ? strings.language : 'en-us'; }
};

// useStrings
// Takes the strings a Necto page loaded, so t reads them.
//
// Inputs
// shared [object]: The Necto helper bag, whose t holds the strings.
export function useStrings(shared) {
    strings = shared.t;
}

// applyRichStrings
// Fills marked text whose {0}, {1} stand for the element's own child elements in order, such as a code sample,
// which keep their markup. createNectoPage translates the rest of the markup.
//
// Inputs
// root [HTMLElement]: The page.
export function applyRichStrings(root) {
    root.querySelectorAll('[data-str-rich]').forEach(function (node) {
        var key = node.getAttribute('data-str-rich');
        if (!strings || !strings.has || !strings.has(key)) return;
        if (!node.richParts) node.richParts = Array.prototype.map.call(node.children, function (child) { return child; });
        var text = strings.tr(key);
        node.textContent = '';
        text.split(/(\{\d+\})/).forEach(function (part) {
            var match = /^\{(\d+)\}$/.exec(part);
            var child = match ? node.richParts[+match[1]] : null;
            node.appendChild(child || document.createTextNode(part));
        });
    });
}

// createScriptsPage
// Wires the Scripts tool through createNectoPage. bind and load are given Stolon's helper bag and the page handle,
// once the strings have loaded and the marked markup is translated.
//
// Inputs
// view [HTMLElement]: The tool's view.
// options [object]: bind, load, and isDirty.
//
// Output
// [object]: The page handle, with shared as the Necto helper bag.
export function createScriptsPage(view, options) {
    var made = createNectoPage(view, {
        page: 'necto_scripts',
        bind: function (shared, page) {
            useStrings(shared);
            applyRichStrings(view);
            if (options.isDirty) page.guardUnsaved(options.isDirty);
            if (options.bind) options.bind(shared, page);
        },
        load: function (shared, page) {
            useStrings(shared);
            if (options.load) options.load(shared, page);
        }
    });
    made.page.shared = made.shared;
    return made.page;
}

// Fields compared with "at least" and "at most".
export var NUMERIC_FIELDS = {
    Year: true, VideoHeight: true, CommunityRating: true, DaysSinceAdded: true, DaysSincePlayed: true, DaysSinceActive: true,
    ProgressPercent: true, ActiveStreams: true, ActiveTranscodes: true, Hour: true
};

// fieldGroups
// The condition fields, grouped for the field picker.
//
// Output
// [Array]: { label, fields: [{ key, label }] } for each group.
export function fieldGroups() {
    return [
        { label: t.tr('ScriptsFieldGroupWho'), fields: [
            { key: 'User', label: t.tr('LabelUser') },
            { key: 'IsAdmin', label: t.tr('ScriptsFieldIsAdmin') },
            { key: 'UsernameAttempted', label: t.tr('ScriptsFieldUsernameAttempted') }
        ] },
        { label: t.tr('ScriptsFieldGroupWhere'), fields: [
            { key: 'Client', label: t.tr('ScriptsFieldClient') },
            { key: 'ClientVersion', label: t.tr('ScriptsFieldClientVersion') },
            { key: 'DeviceName', label: t.tr('ScriptsFieldDeviceName') },
            { key: 'DeviceId', label: t.tr('ScriptsFieldDeviceId') },
            { key: 'RemoteAddress', label: t.tr('ScriptsFieldRemoteAddress') },
            { key: 'IsLocal', label: t.tr('ScriptsFieldIsLocal') }
        ] },
        { label: t.tr('ScriptsFieldGroupWhat'), fields: [
            { key: 'Library', label: t.tr('TasksLibraryCategory') },
            { key: 'ItemName', label: t.tr('ScriptsFieldItemName') },
            { key: 'SeriesName', label: t.tr('ScriptsFieldSeriesName') },
            { key: 'ItemPath', label: t.tr('ScriptsFieldItemPath') },
            { key: 'Genre', label: t.tr('Genre') },
            { key: 'Tag', label: t.tr('LabelTag') },
            { key: 'Studio', label: t.tr('Studio') },
            { key: 'OfficialRating', label: t.tr('ParentalRating') },
            { key: 'Year', label: t.tr('LabelYear') },
            { key: 'VideoHeight', label: t.tr('ScriptsFieldVideoHeight') },
            { key: 'CommunityRating', label: t.tr('CommunityRating') },
            { key: 'DaysSinceAdded', label: t.tr('ScriptsFieldDaysSinceAdded') },
            { key: 'HasSubtitles', label: t.tr('ScriptsFieldHasSubtitles') },
            { key: 'CollectionName', label: t.tr('LabelCollection') }
        ] },
        { label: t.tr('ScriptsFieldGroupForEachItem'), fields: [
            { key: 'PlayedByAnyone', label: t.tr('ScriptsFieldPlayedByAnyone') },
            { key: 'DaysSincePlayed', label: t.tr('ScriptsFieldDaysSincePlayed') }
        ] },
        { label: t.tr('ScriptsFieldGroupForEachUser'), fields: [
            { key: 'DaysSinceActive', label: t.tr('ScriptsFieldDaysSinceActive') },
            { key: 'IsDisabled', label: t.tr('ScriptsFieldIsDisabled') }
        ] },
        { label: t.tr('TitlePlayback'), fields: [
            { key: 'PlayMethod', label: t.tr('LabelPlayMethod') },
            { key: 'ProgressPercent', label: t.tr('ScriptsFieldProgressPercent') },
            { key: 'PlayedToCompletion', label: t.tr('ScriptsFieldPlayedToCompletion') },
            { key: 'TranscodeReasons', label: t.tr('ScriptsFieldTranscodeReasons') },
            { key: 'VideoCodec', label: t.tr('LabelVideoCodec') },
            { key: 'AudioCodec', label: t.tr('LabelAudioCodec') },
            { key: 'ActiveStreams', label: t.tr('ScriptsFieldActiveStreams') },
            { key: 'ActiveTranscodes', label: t.tr('ScriptsFieldActiveTranscodes') },
            { key: 'IsPlayed', label: t.tr('ScriptsFieldIsPlayed') },
            { key: 'IsFavorite', label: t.tr('ScriptsFieldIsFavorite') }
        ] },
        { label: t.tr('ScriptsFieldGroupWhen'), fields: [
            { key: 'Hour', label: t.tr('ScriptsFieldHour') },
            { key: 'DayOfWeek', label: t.tr('LabelAccessDay') }
        ] },
        { label: t.tr('LabelSystem'), fields: [
            { key: 'TaskKey', label: t.tr('ScriptsLabelScheduledTask') },
            { key: 'TaskStatus', label: t.tr('ScriptsFieldTaskStatus') },
            { key: 'TaskCategory', label: t.tr('ScriptsFieldTaskCategory') },
            { key: 'PluginName', label: t.tr('ScriptsFieldPluginName') },
            { key: 'Provider', label: t.tr('ScriptsFieldProvider') },
            { key: 'UpdateReason', label: t.tr('ScriptsFieldUpdateReason') },
            { key: 'ActivityType', label: t.tr('ScriptsFieldActivityType') },
            { key: 'ActivitySeverity', label: t.tr('ScriptsFieldActivitySeverity') }
        ] }
    ];
}

// fieldLabel
// The label of one condition field.
//
// Inputs
// key [string]: The field, such as DeviceName.
//
// Output
// [string]: The label, or an empty string for a field this version does not know.
export function fieldLabel(key) {
    var found = '';
    fieldGroups().forEach(function (group) {
        group.fields.forEach(function (f) { if (f.key === key) found = f.label; });
    });
    return found;
}

// operatorLabels
// The condition operators and how each reads.
//
// Output
// [object]: The label of each operator, by its name.
export function operatorLabels() {
    return {
        Is: t.tr('ScriptsOperatorIs'),
        IsNot: t.tr('ScriptsOperatorIsNot'),
        Contains: t.tr('ScriptsOperatorContains'),
        DoesNotContain: t.tr('ScriptsOperatorDoesNotContain'),
        StartsWith: t.tr('ScriptsOperatorStartsWith'),
        MatchesRegex: t.tr('ScriptsOperatorMatchesRegex'),
        AtLeast: t.tr('ScriptsOperatorAtLeast'),
        AtMost: t.tr('ScriptsOperatorAtMost')
    };
}

// operationTypes
// The step types, each with its label, its group in the step picker, and what the event must carry for the step
// to act. A step whose need is missing reports Skipped.
//
// Output
// [object]: { label, group, needs } for each step type, by its name.
export function operationTypes() {
    var run = t.tr('ScriptsLabelRun');
    var sessions = t.tr('ScriptsTriggerGroupSessions');
    var items = t.tr('Items');
    var users = t.tr('HeaderUsers');
    var server = t.tr('TabServer');
    return {
        RunCommand: { label: t.tr('ScriptsStepRunCommand'), group: run, needs: null },
        HttpRequest: { label: t.tr('ScriptsStepHttpRequest'), group: run, needs: null },
        RunScript: { label: t.tr('ScriptsStepRunScript'), group: run, needs: null },
        Wait: { label: t.tr('ScriptsStepWait'), group: run, needs: null },
        StopScript: { label: t.tr('ScriptsStepStopScript'), group: run, needs: null },
        SendMessage: { label: t.tr('SendMessage'), group: sessions, needs: null },
        PausePlayback: { label: t.tr('ScriptsStepPausePlayback'), group: sessions, needs: 'session' },
        StopPlayback: { label: t.tr('StopPlayback'), group: sessions, needs: 'session' },
        MarkPlayed: { label: t.tr('MarkPlayed'), group: items, needs: 'useritem' },
        MarkUnplayed: { label: t.tr('MarkUnplayed'), group: items, needs: 'useritem' },
        SetFavorite: { label: t.tr('ScriptsStepSetFavorite'), group: items, needs: 'useritem' },
        AddTag: { label: t.tr('ScriptsStepAddTag'), group: items, needs: 'item' },
        RemoveTag: { label: t.tr('ScriptsStepRemoveTag'), group: items, needs: 'item' },
        AddToCollection: { label: t.tr('AddToCollection'), group: items, needs: 'item' },
        RemoveFromCollection: { label: t.tr('RemoveFromCollection'), group: items, needs: 'item' },
        RefreshMetadata: { label: t.tr('RefreshMetadata'), group: items, needs: 'item' },
        SetUserDisabled: { label: t.tr('ScriptsStepSetUserDisabled'), group: users, needs: 'user' },
        SignOutUser: { label: t.tr('ScriptsStepSignOutUser'), group: users, needs: 'user' },
        ScanLibraries: { label: t.tr('ScriptsStepScanLibraries'), group: server, needs: null },
        RunScheduledTask: { label: t.tr('ScriptsStepRunScheduledTask'), group: server, needs: null },
        WriteActivityLog: { label: t.tr('ScriptsStepWriteActivityLog'), group: server, needs: null },
        AppendToFile: { label: t.tr('ScriptsStepAppendToFile'), group: server, needs: null }
    };
}

export var STATUS_BADGES = { Succeeded: 'green', Failed: 'red', Skipped: 'gray' };

// renderRunSteps
// Builds a run's steps as a Base Timeline, each with its status, step type, message, and any captured output.
//
// Inputs
// shared [object]: The helper bag from createShared.
// run [object]: A run from the history or a test.
//
// Output
// [HTMLElement]: The timeline, or an empty state when the script has no steps.
export function renderRunSteps(shared, run) {
    var timeline = createTimeline({
        emptyText: t.tr('ScriptsEmptyRunSteps'),
        statusLabels: { green: t.tr('ScriptsStatusSucceeded'), gray: t.tr('ScriptsStatusSkipped') },
        items: (run.Operations || []).map(function (step) {
            var types = operationTypes();
            return {
                title: types[step.Type] ? types[step.Type].label : step.Type,
                body: step.Message || '',
                pre: step.Output || '',
                status: STATUS_BADGES[step.Status] || 'gray'
            };
        })
    });
    return timeline.element;
}

// renderRunSummary
// Builds a run's result, trigger, start, and length as a Base Definition List in columns.
//
// Inputs
// shared [object]: The helper bag from createShared.
// run [object]: A run from the history or a test.
// details [Array]: More { label, value } items after the result, such as the trigger and the start [optional].
//
// Output
// [HTMLElement]: The list.
export function renderRunSummary(shared, run, details) {
    return createDefinitionList({
        layout: 'columns',
        items: [{ label: t.tr('ScriptsLabelResult'), value: { badge: { label: run.Succeeded ? t.tr('ScriptsStatusSucceeded') : t.tr('LabelFailed'), color: run.Succeeded ? 'green' : 'red' } } }]
            .concat(details || [])
            .concat([{ label: t.tr('ScriptsLabelLength'), value: formatDuration(shared, run.DurationMs) }])
    }).element;
}

// renderRunVariables
// Builds a run's variables as a Base Definition List, named as a command reads them.
//
// Inputs
// shared [object]: The helper bag from createShared.
// run [object]: A run from the history.
//
// Output
// [HTMLElement]: The list of variables.
export function renderRunVariables(shared, run) {
    var variables = run.Variables || {};
    var names = Object.keys(variables).sort();
    if (!names.length) {
        var empty = document.createElement('div');
        empty.innerHTML = shared.emptySection(t.tr('ScriptsEmptyVariables'), { inline: true });
        return empty;
    }
    return createDefinitionList({
        items: names.map(function (name) {
            var value = variables[name] === null || variables[name] === undefined ? '' : String(variables[name]);
            return { label: 'JF_' + name, value: { text: value, mono: true } };
        })
    }).element;
}

// formatDuration
// Shows a run's length in milliseconds under a second, and as Base's duration text above it.
//
// Inputs
// shared [object]: The helper bag from createShared.
// ms [number]: The length in milliseconds.
//
// Output
// [string]: The length as text.
export function formatDuration(shared, ms) {
    return ms < 1000 ? t.format('ScriptsDurationMilliseconds', ms) : shared.formatDuration(Math.round(ms / 1000));
}
