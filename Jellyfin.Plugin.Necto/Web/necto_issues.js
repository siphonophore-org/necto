// necto_issues.js
// The Issues tool. It counts the issues by state, lists them with search, state, type, and reporter filters, acts on
// the rows picked, and opens each in a sidecar where it is confirmed, resolved, declined, or reopened. While issues are
// off it says so.

import * as nectoModule from './configurationpage?name=necto_shared.js';

var h = nectoModule.h;
var esc = nectoModule.escapeHtml;

// The rows a page of the list holds, and the most reporters the filter lists.
export var PAGE_SIZE = 50;
var REPORTERS_LIMIT = 200;

// FILTERS_KEY
// Where the state, type, and reporter filters are kept for the next visit.
export var FILTERS_KEY = 'necto-issues-filters';

// ISSUE_STATES
// Each issue state by the name the server sends, with its string key and badge color.
export var ISSUE_STATES = {
    Reported: { key: 'StateReported', color: 'orange' },
    Confirmed: { key: 'StateConfirmed', color: 'red' },
    Resolved: { key: 'StateResolved', color: 'green' },
    Declined: { key: 'StateDeclined', color: 'gray' },
    Rescinded: { key: 'StateRescinded', color: 'gray' }
};

// The verbs an administrator decides an issue with, each with its button, its prompt's title, and its result.
var VERBS = {
    Confirm: { button: 'IssuesButtonConfirm', title: 'IssuesTitleConfirm', toast: 'IssuesToastConfirmed' },
    Resolve: { button: 'IssuesButtonResolve', title: 'IssuesTitleResolve', toast: 'IssuesToastResolved' },
    Decline: { button: 'ButtonDecline', title: 'IssuesTitleDecline', toast: 'IssuesToastDeclined' },
    Reopen: { button: 'IssuesButtonReopen', title: 'IssuesTitleReopen', toast: 'IssuesToastReopened' }
};

// issueTypes
// Lists what a reporter can say is wrong, in the order the issue page offers them.
//
// Inputs
// t [object]: The strings.
//
// Output
// [object[]]: Each type's value and label.
export function issueTypes(t) {
    return [
        { value: 'Video', label: t.tr('Video') }, { value: 'Audio', label: t.tr('Audio') }, { value: 'Subtitles', label: t.tr('Subtitles') },
        { value: 'Metadata', label: t.tr('IssuesTypeMetadata') }, { value: 'WontPlay', label: t.tr('IssuesTypeWontPlay') }, { value: 'Other', label: t.tr('Other') }
    ];
}

// typeName
// Names an issue type in the viewer's language.
//
// Inputs
// value [string]: The type, such as Audio.
// t [object]: The strings.
//
// Output
// [string]: The name, or the type itself when the page does not know it.
export function typeName(value, t) {
    return (issueTypes(t).filter(function (x) { return x.value === value; })[0] || { label: String(value || '') }).label;
}

// stateOf
// Gives an issue state's name and badge color.
//
// Inputs
// state [string]: The state.
// t [object]: The strings.
//
// Output
// [object]: label and color.
export function stateOf(state, t) {
    return ISSUE_STATES[state] ? { label: t.tr(ISSUE_STATES[state].key), color: ISSUE_STATES[state].color } : { label: String(state || ''), color: 'gray' };
}

// isOpen
// Tells whether an issue still waits on an administrator.
//
// Inputs
// issue [object]: The issue.
//
// Output
// [boolean]: True while it is reported or confirmed.
export function isOpen(issue) {
    return issue.State === 'Reported' || issue.State === 'Confirmed';
}

// stateFilters
// Lists the state filter's choices, Open first, then each state.
//
// Inputs
// t [object]: The strings.
//
// Output
// [object[]]: Each choice's value and label.
export function stateFilters(t) {
    return [{ value: 'Open', label: t.tr('StateOpen') }].concat(Object.keys(ISSUE_STATES).map(function (k) {
        return { value: k, label: t.tr(ISSUE_STATES[k].key) };
    }));
}

// statCards
// Turns the Stats answer into the count cards, one per state in its color.
//
// Inputs
// by [object]: The Issues/Stats answer, a count by state.
// t [object]: The strings.
//
// Output
// [object[]]: The cards, each a label, a count, and a color.
export function statCards(by, t) {
    by = by || {};
    return Object.keys(ISSUE_STATES).map(function (k) {
        var s = stateOf(k, t);
        return { label: s.label, count: by[k] || 0, color: s.color };
    });
}

// totalOf
// Adds up the issues in every state.
//
// Inputs
// by [object]: The Issues/Stats answer, a count by state.
//
// Output
// [number]: How many issues there are.
export function totalOf(by) {
    return Object.keys(by || {}).reduce(function (n, k) { return n + (Number(by[k]) || 0); }, 0);
}

// reporterOptions
// Turns the reporters into the reporter filter's choices, each with how many issues they reported.
//
// Inputs
// reporters [object[]]: The Issues/Reporters items.
//
// Output
// [object[]]: Each choice's value and label.
export function reporterOptions(reporters) {
    return (reporters || []).map(function (r) { return { value: r.UserId, label: r.UserName + ' (' + r.Count + ')' }; });
}

// BULK_VERBS
// The decisions the bulk bar offers, each with its button's string key and class.
export var BULK_VERBS = [['Resolve', 'IssuesButtonResolve', 'stolon-primary'], ['Confirm', 'IssuesButtonConfirm', 'stolon-secondary'], ['Decline', 'ButtonDecline', 'stolon-destructive']];

// canDecide
// Tells whether a decision applies to an issue in its state, so the bulk bar skips the rest.
//
// Inputs
// issue [object]: The issue.
// verb [string]: Confirm, Resolve, Decline, or Reopen.
//
// Output
// [boolean]: Whether the issue takes it.
export function canDecide(issue, verb) {
    return issueActions(issue).indexOf(verb) >= 0;
}

// issueActions
// Lists the decisions an issue's sidecar offers, in shelf order.
//
// Inputs
// issue [object]: The issue.
//
// Output
// [string[]]: Decline, Confirm, and Resolve for an open issue, Reopen for a closed one.
export function issueActions(issue) {
    if (!isOpen(issue)) return ['Reopen'];
    return issue.State === 'Reported' ? ['Decline', 'Confirm', 'Resolve'] : ['Decline', 'Resolve'];
}

// readFilters
// Reads the state, type, and reporter filters kept from the last visit.
//
// Inputs
// storage [object]: The browser storage, or null.
//
// Output
// [object]: state, Open unless kept, type, empty for every type, and reporter, empty for everyone.
export function readFilters(storage) {
    var kept = null;
    try { kept = JSON.parse((storage && storage.getItem(FILTERS_KEY)) || 'null'); } catch (e) { kept = null; }
    kept = kept || {};
    return {
        state: typeof kept.state === 'string' && kept.state ? kept.state : 'Open',
        type: typeof kept.type === 'string' ? kept.type : '',
        reporter: typeof kept.reporter === 'string' ? kept.reporter : ''
    };
}

// keepFilters
// Keeps the state, type, and reporter filters for the next visit.
//
// Inputs
// storage [object]: The browser storage, or null.
// filters [object]: state, type, and reporter.
export function keepFilters(storage, filters) {
    try { if (storage) storage.setItem(FILTERS_KEY, JSON.stringify({ state: filters.state || 'Open', type: filters.type || '', reporter: filters.reporter || '' })); } catch (e) { /* kept for this visit only */ }
}

// titleOf
// Says what an issue is about, as the server does: the show with S1E2 for an episode, the name and year for a movie.
//
// Inputs
// issue [object]: The issue.
// t [object]: The strings.
//
// Output
// [string]: The title.
export function titleOf(issue, t) {
    if (issue.ItemType === 'Episode' && issue.SeriesName && issue.SeasonNumber != null && issue.EpisodeNumber != null) {
        return t.format('IssuesEpisodeTitleFormat', issue.SeriesName, issue.SeasonNumber, issue.EpisodeNumber);
    }
    var title = String(issue.Title || '');
    var suffix = issue.Year ? ' (' + issue.Year + ')' : '';
    return title + (suffix && (issue.ItemType === 'Movie' || issue.ItemType === 'MusicVideo') && title.slice(-suffix.length) !== suffix ? suffix : '');
}

// typesOf
// Lists what the reporters say is wrong, each type once.
//
// Inputs
// issue [object]: The issue.
// t [object]: The strings.
//
// Output
// [string]: The types, joined.
export function typesOf(issue, t) {
    return (issue.Reporters || []).map(function (r) { return r.Type; }).filter(function (x, i, all) { return all.indexOf(x) === i; })
        .map(function (x) { return typeName(x, t); }).join(', ');
}

// reportersLine
// Writes who reported an issue and when it last changed.
//
// Inputs
// issue [object]: The issue.
// t [object]: The strings.
//
// Output
// [string]: The line.
export function reportersLine(issue, t) {
    var names = (issue.Reporters || []).map(function (r) { return r.UserName; }).join(', ');
    return [names, t.format('IssuesValueUpdatedOn', when(issue.UpdatedUtc))].filter(Boolean).join(' · ');
}

// subline
// Writes the line under an issue's title, an episode's own name and the reporters' types.
//
// Inputs
// issue [object]: The issue.
// t [object]: The strings.
//
// Output
// [string]: The line.
export function subline(issue, t) {
    return [issue.ItemType === 'Episode' ? issue.Title : '', typesOf(issue, t)].filter(Boolean).join(' · ');
}

// when
// Writes a moment as a date and a time of day.
//
// Inputs
// iso [string]: The moment.
//
// Output
// [string]: The date and time, or empty when it does not read.
function when(iso) {
    var d = new Date(iso);
    return !iso || isNaN(d) ? '' : d.toLocaleDateString() + ' ' + d.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
}

// noteItems
// Turns an issue's notes into timeline entries in the order they were written, each with its writer and time.
//
// Inputs
// notes [object[]]: The notes, as User, Text, and Time.
//
// Output
// [object[]]: The entries.
export function noteItems(notes) {
    return (notes || []).map(function (n) {
        return { title: n.User || '', time: when(n.Time), datetime: n.Time || undefined, body: n.Text || '' };
    });
}

// detailItems
// Lists an issue's details for its sidecar: its reporters and their types, when it was reported, who decided it, and its file.
//
// Inputs
// issue [object]: The issue.
// t [object]: The strings.
//
// Output
// [object[]]: The label and value pairs.
export function detailItems(issue, t) {
    return [
        { label: t.tr('IssuesLabelReportedBy'), value: (issue.Reporters || []).map(function (r) { return t.format('IssuesValueReporterType', r.UserName, typeName(r.Type, t)); }).join(', ') },
        { label: t.tr('IssuesLabelReportedOn'), value: when(issue.CreatedUtc) },
        issue.DecidedBy ? { label: t.tr('IssuesLabelDecidedBy'), value: issue.DecidedBy } : null,
        issue.Path ? { label: t.tr('File'), value: { text: issue.Path, mono: true } } : null
    ].filter(Boolean);
}

// bulkResultText
// Words what a bulk decision did, a sentence for each outcome.
//
// Inputs
// done [number]: How many it decided.
// failed [number]: How many the server refused.
// skipped [number]: How many did not take the decision in their state.
// t [object]: The strings.
//
// Output
// [object]: text, the sentences, and isError, true when some failed.
export function bulkResultText(done, failed, skipped, t) {
    var text = [t.plural(done, 'IssuesBulkDoneOne', 'IssuesBulkDoneMany')];
    if (failed) text.push(t.plural(failed, 'IssuesBulkFailedOne', 'IssuesBulkFailedMany'));
    if (skipped) text.push(t.plural(skipped, 'IssuesBulkSkippedOne', 'IssuesBulkSkippedMany'));
    return { text: text.join(' '), isError: !!failed };
}

export default function (view) {
    'use strict';

    var shared = null;
    var page = null;
    var table = null;
    var filters = { state: 'Open', type: '', reporter: '' };
    var reporters = [];
    var tableReporters = null;

    // storage
    // Gives the browser storage, or null where the browser refuses it.
    function storage() {
        try { return globalThis.localStorage || null; } catch (e) { return null; }
    }

    // posterOf
    // Gives the address of an issue's poster: an episode's show's, else its own.
    function posterOf(issue) {
        return ApiClient.getUrl('Items/' + (issue.SeriesId || issue.ItemId) + '/Images/Primary', { fillHeight: 240, quality: 90 });
    }

    // stateCell
    // Draws a row's state badge, and File changed when the file changed since the report.
    function stateCell(issue) {
        var s = stateOf(issue.State, shared.t);
        return shared.badge(s.color, s.label) + (issue.FileChanged ? ' ' + shared.badge('orange', shared.t.tr('IssuesLabelFileChanged')) : '');
    }

    // report
    // Shows a result on the page's status line.
    function report(message, isError) {
        shared.setStatus('issuesStatus', message, !!isError, isError ? { timeout: 0 } : undefined);
    }

    // showEmpty
    // Shows No issues yet in place of the list while there are none at all.
    function showEmpty(none) {
        view.querySelector('#issuesEmpty').classList.toggle('stolon-hidden', !none);
        view.querySelector('#issuesTable').classList.toggle('stolon-hidden', none);
    }

    // loadStats
    // Reads the counts and draws the cards.
    function loadStats() {
        return shared.request('Issues/Stats').then(function (by) {
            shared.renderCards('issueStats', statCards(by, shared.t));
            showEmpty(!totalOf(by));
        }).catch(function () { /* the counts are a nicety */ });
    }

    // loadReporters
    // Reads everyone who reported issues, for the reporter filter, and drops a kept reporter who has none left.
    function loadReporters() {
        return shared.request('Issues/Reporters?limit=' + REPORTERS_LIMIT).then(function (answer) {
            reporters = (answer && answer.Items) || [];
            var kept = filters.reporter && reporters.some(function (r) { return r.UserId === filters.reporter; }) ? filters.reporter : '';
            if (kept !== filters.reporter) { filters.reporter = kept; keepFilters(storage(), filters); }
        }).catch(function () { /* the filter keeps its last list */ });
    }

    // createTable
    // Builds the list with its search, its state, type, and reporter filters, and its bulk actions.
    function createTable() {
        var t = shared.t;
        var bag = Object.create(shared);
        bag.apiPrefix = '';
        tableReporters = JSON.stringify(reporterOptions(reporters));
        table = nectoModule.createPaginatedTable(view, bag, {
            containerId: 'issuesTable',
            endpoint: 'Issues/All',
            selection: { enabled: true, idKey: 'Id', onSelectionChange: onSelection },
            search: { enabled: true, placeholder: t.tr('IssuesSearchPlaceholder') },
            filters: [
                {
                    label: t.tr('HeaderStatus'),
                    options: stateFilters(t),
                    allValue: 'All',
                    initialValue: filters.state || 'All',
                    onChange: function (value) { filters.state = value; keepFilters(storage(), filters); },
                    buildParams: function (value) { return { filter: value || null }; }
                },
                {
                    name: 'type',
                    label: t.tr('IssuesLabelType'),
                    options: issueTypes(t),
                    allLabel: t.tr('IssuesFilterEveryType'),
                    initialValue: filters.type,
                    onChange: function (value) { filters.type = value; keepFilters(storage(), filters); },
                    buildParams: function (value) { return { type: value }; }
                },
                {
                    name: 'user',
                    label: t.tr('IssuesLabelReporter'),
                    options: reporterOptions(reporters),
                    allLabel: t.tr('GroupEveryone'),
                    initialValue: filters.reporter,
                    onChange: function (value) { filters.reporter = value; keepFilters(storage(), filters); },
                    buildParams: function (value) { return { user: value }; }
                }
            ],
            pagination: { pageSize: PAGE_SIZE },
            emptyState: { message: t.tr('IssuesEmpty'), filteredMessage: t.tr('IssuesEmptyMatch') },
            columns: [
                {
                    key: 'Title', type: 'custom', className: 'stolon-table-cell-with-thumb',
                    image: posterOf,
                    imageShape: 'portrait',
                    render: function (issue) {
                        return '<div class="stolon-table-item-info"><div class="stolon-table-item-title">' + esc(titleOf(issue, t)) + '</div>'
                            + '<div class="stolon-table-item-sub">' + esc(subline(issue, t)) + '</div>'
                            + '<div class="stolon-table-item-sub">' + esc(reportersLine(issue, t)) + '</div></div>';
                    }
                },
                { key: 'State', type: 'custom', className: 'stolon-table-cell-status', render: stateCell }
            ],
            actions: { onRowClick: function (issue) { showIssue(issue); } }
        });

        var actions = table.getBulkActionsContainer();
        if (actions) {
            BULK_VERBS.forEach(function (b) {
                actions.appendChild(h('button', { is: 'emby-button', type: 'button', class: 'raised stolon-button ' + b[2], 'data-bulk': b[0], disabled: true }, [h('span', { text: t.tr(b[1]) })]));
            });
            actions.addEventListener('click', function (e) {
                var btn = e.target.closest('[data-bulk]');
                if (btn && !btn.disabled) bulk(btn.getAttribute('data-bulk'));
            });
        }
    }

    // onSelection
    // Turns the bulk actions on while rows are picked.
    function onSelection(ids) {
        view.querySelectorAll('[data-bulk]').forEach(function (btn) { btn.disabled = !ids.length; });
    }

    // destroyTable
    // Takes the list down when the page hides.
    function destroyTable() {
        if (table) { table.destroy(); table = null; }
        var mount = view.querySelector('#issuesTable');
        if (mount) mount.innerHTML = '';
    }

    // reload
    // Reads the counts, the reporters, and the list again from the top, building the list the first time.
    function reload() {
        loadStats();
        return loadReporters().then(function () {
            if (!table) { createTable(); return table.load(); }
            var key = JSON.stringify(reporterOptions(reporters));
            if (key !== tableReporters) { tableReporters = key; table.setFilterOptions(reporterOptions(reporters), 'user'); }
            return table.reload();
        });
    }

    // askNote
    // Asks for the note the reporters see with a decision, which may be left blank.
    function askNote(title, verb) {
        var t = shared.t;
        return nectoModule.promptSidecar([{ name: 'Note', label: t.tr('IssuesLabelNoteShown'), type: 'textarea' }], { title: title, submitText: t.tr(VERBS[verb].button) });
    }

    // act
    // Confirms, resolves, declines, or reopens an issue, asking first for the note its reporters see.
    //
    // Inputs
    // issue [object]: The issue.
    // verb [string]: Confirm, Resolve, Decline, or Reopen.
    // close [function]: Closes the issue's sidecar once done.
    // status [HTMLElement]: Where the sidecar says what went wrong.
    function act(issue, verb, close, status) {
        var t = shared.t;
        return askNote(t.format(VERBS[verb].title, titleOf(issue, t)), verb).then(function (values) {
            if (!values) return null;
            return shared.request('Issues/' + issue.Id + '/' + verb, 'POST', { Note: values.Note || '' }).then(function () {
                close();
                report(t.tr(VERBS[verb].toast), false);
                reload();
            });
        }).catch(function (err) {
            return shared.errorText(err, t.tr('IssuesErrorUpdate')).then(function (text) { shared.setStatus(status, text, true, { timeout: 0 }); });
        });
    }

    // bulk
    // Confirms, resolves, or declines every row picked that takes it, with one note, and says how many it decided.
    function bulk(verb) {
        var t = shared.t;
        var picked = table ? table.getSelectedItems() : [];
        if (!picked.length) return Promise.resolve();
        var fits = picked.filter(function (issue) { return canDecide(issue, verb); });
        var title = t.plural(picked.length, 'IssuesHeaderBulkOne', 'IssuesHeaderBulkMany', t.tr(VERBS[verb].button));
        return askNote(title, verb).then(function (values) {
            if (!values) return null;
            report(t.tr('TextWorking'), false);
            var done = 0;
            var failedCount = 0;
            return fits.reduce(function (chain, issue) {
                return chain.then(function () {
                    return shared.request('Issues/' + issue.Id + '/' + verb, 'POST', { Note: values.Note || '' })
                        .then(function () { done += 1; }, function () { failedCount += 1; });
                });
            }, Promise.resolve()).then(function () {
                var said = bulkResultText(done, failedCount, picked.length - fits.length, t);
                report(said.text, said.isError);
                return reload();
            });
        });
    }

    // showIssue
    // Opens an issue in a sidecar: its poster and title, a notice when its file changed, its details, its notes, and
    // its decisions. Radarr's or Sonarr's page for it is linked once found.
    function showIssue(issue) {
        var t = shared.t;
        var s = stateOf(issue.State, t);
        var card = nectoModule.createMediaCard({
            layout: 'row',
            shape: 'portrait',
            image: posterOf(issue),
            title: titleOf(issue, t),
            subtitle: subline(issue, t),
            badges: issue.FileChanged ? [{ label: t.tr('IssuesLabelFileChanged'), color: 'orange' }] : []
        });
        var items = detailItems(issue, t);
        var details = nectoModule.createDefinitionList({ items: items });
        var notes = (issue.Notes || []).length ? nectoModule.createTimeline({ items: noteItems(issue.Notes) }) : null;
        var status = h('div', { class: 'stolon-status-text', role: 'alert', 'data-role': 'status' });
        var body = h('div', { class: 'stolon-stack', 'data-role': 'issue-detail' }, [
            card.element,
            issue.FileChanged ? h('div', { class: 'stolon-warn', text: t.tr('IssuesHelpFileChanged') }) : null,
            details.element,
            notes ? nectoModule.createCollapsibleSection({ title: t.tr('LabelNotes'), icon: 'forum', collapsible: false, content: notes.element }).element : null,
            status
        ]);
        var buttons = [{ text: t.tr('ButtonCancel'), className: 'stolon-button stolon-secondary', action: function (close) { close(); } }];
        if (issue.ItemId) buttons.push({ text: t.tr('ButtonOpenInLibrary'), className: 'stolon-button stolon-secondary', action: function (close) { close(); Dashboard.navigate('details?id=' + issue.ItemId); } });
        issueActions(issue).forEach(function (verb) {
            var kind = verb === 'Decline' ? 'stolon-destructive' : verb === 'Confirm' ? 'stolon-secondary' : 'stolon-primary';
            var button = { text: t.tr(VERBS[verb].button), className: 'stolon-button ' + kind, action: function (close) { act(issue, verb, close, status); } };
            if (verb === 'Decline') buttons.unshift(button);
            else buttons.push(button);
        });
        var sidecar = nectoModule.openSidecar({
            page: page,
            size: 'large',
            title: titleOf(issue, t),
            badge: { label: s.label, color: s.color },
            body: body,
            buttons: buttons,
            onClose: function () { card.destroy(); details.destroy(); if (notes) notes.destroy(); }
        });

        shared.request('Issues/' + issue.Id + '/ArrLink').then(function (link) {
            if (!link || !link.Url || !sidecar.isOpen()) return;
            details.setItems(items.concat([{ label: link.Server, value: { text: t.format('ButtonOpenIn', link.Server), link: link.Url } }]));
        }).catch(function () { /* no link, then */ });
        return sidecar;
    }

    // showOff
    // Shows the feature off state while issues are off, and the counts and list while they are on.
    function showOff(off) {
        view.querySelector('#issuesOff').classList.toggle('stolon-hidden', !off);
        view.querySelector('#issueStats').classList.toggle('stolon-hidden', off);
        view.querySelector('#issuesTable').classList.toggle('stolon-hidden', off);
        if (off) view.querySelector('#issuesEmpty').classList.add('stolon-hidden');
    }

    // loadPage
    // Reads whether issues are on, then the counts and the list on the kept filters.
    function loadPage() {
        filters = readFilters(storage());
        page.addCleanup(destroyTable);
        view.querySelector('#issuesOff').innerHTML = shared.emptySection(shared.t.tr('IssuesMessageOff'));
        view.querySelector('#issuesEmpty').innerHTML = shared.emptySection(shared.t.tr('IssuesEmpty'));
        return shared.getConfig().then(function (config) {
            var on = !!(config && config.Issues && config.Issues.Enabled);
            showOff(!on);
            if (on) return reload();
            return null;
        }).catch(function () {
            report(shared.t.tr('IssuesErrorLoad'), true);
        });
    }

    var made = nectoModule.createNectoPage(view, { page: 'necto_issues', load: loadPage });
    shared = made.shared;
    page = made.page;

    return { reload: function () { return reload(); }, showIssue: function (issue) { return showIssue(issue); }, bulk: function (verb) { return bulk(verb); } };
}
