// necto_issues.js
// The Issues tool. It counts the issues by state, lists them with search, state, and type filters, and opens each in
// a sidecar where it is confirmed, resolved, declined, or reopened. While issues are off it says so.

import * as nectoModule from './configurationpage?name=necto_shared.js';

var el = nectoModule.el;
var esc = nectoModule.escapeHtml;

// The rows a page of the list holds.
export var PAGE_SIZE = 50;

// FILTERS_KEY
// Where the state and type filters are kept for the next visit.
export var FILTERS_KEY = 'necto-issues-filters';

// ISSUE_STATES
// Each issue state by the name the server sends, with its string key and badge color.
export var ISSUE_STATES = {
    Reported: { key: 'IssuesStateReported', color: 'orange' },
    Confirmed: { key: 'IssuesStateConfirmed', color: 'red' },
    Resolved: { key: 'IssuesStateResolved', color: 'green' },
    Declined: { key: 'IssuesStateDeclined', color: 'gray' },
    Rescinded: { key: 'IssuesStateRescinded', color: 'gray' }
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
// Turns the Stats answer into the count cards: Open untinted as the reported and confirmed issues, then each state.
//
// Inputs
// by [object]: The Issues/Stats answer, a count by state.
// t [object]: The strings.
//
// Output
// [object[]]: The cards, each a label, a count, and a color.
export function statCards(by, t) {
    by = by || {};
    return [{ label: t.tr('StateOpen'), count: (by.Reported || 0) + (by.Confirmed || 0), color: '' }]
        .concat(['Reported', 'Confirmed', 'Resolved', 'Declined'].map(function (k) {
            var s = stateOf(k, t);
            return { label: s.label, count: by[k] || 0, color: s.color };
        }));
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
// Reads the state and type filters kept from the last visit.
//
// Inputs
// storage [object]: The browser storage, or null.
//
// Output
// [object]: state, Open unless kept, and type, empty for every type.
export function readFilters(storage) {
    var kept = null;
    try { kept = JSON.parse((storage && storage.getItem(FILTERS_KEY)) || 'null'); } catch (e) { kept = null; }
    kept = kept || {};
    return { state: typeof kept.state === 'string' && kept.state ? kept.state : 'Open', type: typeof kept.type === 'string' ? kept.type : '' };
}

// keepFilters
// Keeps the state and type filters for the next visit.
//
// Inputs
// storage [object]: The browser storage, or null.
// filters [object]: state and type.
export function keepFilters(storage, filters) {
    try { if (storage) storage.setItem(FILTERS_KEY, JSON.stringify({ state: filters.state || 'Open', type: filters.type || '' })); } catch (e) { /* kept for this visit only */ }
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

export default function (view) {
    'use strict';

    var shared = null;
    var page = null;
    var table = null;
    var filters = { state: 'Open', type: '' };

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

    // loadStats
    // Reads the counts and draws the cards.
    function loadStats() {
        return shared.request('Issues/Stats').then(function (by) {
            shared.renderCards('issueStats', statCards(by, shared.t));
        }).catch(function () { /* the counts are a nicety */ });
    }

    // createTable
    // Builds the list with its search and its state and type filters.
    function createTable() {
        var t = shared.t;
        var bag = Object.create(shared);
        bag.apiPrefix = '';
        table = nectoModule.createPaginatedTable(view, bag, {
            containerId: 'issuesTable',
            endpoint: 'Issues/All',
            selection: { enabled: false, idKey: 'Id' },
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
                }
            ],
            pagination: { pageSize: PAGE_SIZE },
            emptyState: { message: t.tr('IssuesEmptyMatch') },
            columns: [
                {
                    key: 'Title', type: 'custom', className: 'stolon-table-cell-with-thumb',
                    image: posterOf,
                    imageShape: 'portrait',
                    render: function (issue) {
                        return '<div class="stolon-table-item-info"><div class="stolon-table-item-title">' + esc(titleOf(issue, t)) + '</div>'
                            + '<div class="stolon-table-item-sub">' + esc(subline(issue, t)) + '</div></div>';
                    }
                },
                {
                    key: 'Reporters', type: 'custom', render: function (issue) {
                        return '<div class="stolon-table-item-info"><div>' + esc((issue.Reporters || []).map(function (r) { return r.UserName; }).join(', ')) + '</div>'
                            + '<div class="stolon-table-item-sub">' + esc(t.format('IssuesValueUpdatedOn', when(issue.UpdatedUtc))) + '</div></div>';
                    }
                },
                { key: 'State', type: 'custom', className: 'stolon-table-cell-status', render: stateCell }
            ],
            actions: { onRowClick: function (issue) { showIssue(issue); } }
        });
    }

    // destroyTable
    // Takes the list down when the page hides.
    function destroyTable() {
        if (table) { table.destroy(); table = null; }
        var mount = view.querySelector('#issuesTable');
        if (mount) mount.innerHTML = '';
    }

    // reload
    // Reads the counts and the list again from the top, building the list the first time.
    function reload() {
        loadStats();
        if (!table) { createTable(); return table.load(); }
        return table.reload();
    }

    // act
    // Confirms, resolves, declines, or reopens an issue, asking first for an optional note its reporters see.
    //
    // Inputs
    // issue [object]: The issue.
    // verb [string]: Confirm, Resolve, Decline, or Reopen.
    // close [function]: Closes the issue's sidecar once done.
    // status [HTMLElement]: Where the sidecar says what went wrong.
    function act(issue, verb, close, status) {
        var t = shared.t;
        return nectoModule.promptSidecar([{ name: 'Note', label: t.tr('IssuesLabelNoteShown'), type: 'textarea', placeholder: t.tr('PlaceholderOptional') }], {
            title: t.format(VERBS[verb].title, titleOf(issue, t)),
            submitText: t.tr(VERBS[verb].button)
        }).then(function (values) {
            if (!values) return null;
            return shared.request('Issues/' + issue.Id + '/' + verb, 'POST', { Note: values.Note || '' }).then(function () {
                close();
                shared.setStatus('issuesStatus', t.tr(VERBS[verb].toast), false);
                reload();
            });
        }).catch(function (err) {
            return shared.errorText(err, t.tr('IssuesErrorUpdate')).then(function (text) { shared.setStatus(status, text, true, { timeout: 0 }); });
        });
    }

    // showIssue
    // Opens an issue in a sidecar: its poster, title, and state, a notice when its file changed, its details, its
    // notes, and its decisions. Radarr's or Sonarr's page for it is linked once found.
    function showIssue(issue) {
        var t = shared.t;
        var s = stateOf(issue.State, t);
        var card = nectoModule.createMediaCard({
            layout: 'row',
            shape: 'portrait',
            image: posterOf(issue),
            title: titleOf(issue, t),
            subtitle: subline(issue, t),
            status: { label: s.label, color: s.color },
            badges: issue.FileChanged ? [{ label: t.tr('IssuesLabelFileChanged'), color: 'orange' }] : []
        });
        var items = detailItems(issue, t);
        var details = nectoModule.createDefinitionList({ items: items });
        var notes = (issue.Notes || []).length ? nectoModule.createTimeline({ items: noteItems(issue.Notes) }) : null;
        var status = el('div', { class: 'stolon-status-text', role: 'alert', 'data-role': 'status' });
        var body = el('div', { class: 'necto-detail', 'data-role': 'issue-detail' }, [
            card.element,
            issue.FileChanged ? el('div', { class: 'stolon-warn', text: t.tr('IssuesHelpFileChanged') }) : null,
            details.element,
            notes ? el('h3', { class: 'stolon-subsection-title', text: t.tr('LabelNotes') }) : null,
            notes ? notes.element : null,
            status
        ]);
        var buttons = [];
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
    }

    // loadPage
    // Reads whether issues are on, then the counts and the list on the kept filters.
    function loadPage() {
        filters = readFilters(storage());
        page.addCleanup(destroyTable);
        view.querySelector('#issuesOff').innerHTML = shared.emptySection(shared.t.tr('IssuesMessageOff'));
        return shared.getConfig().then(function (config) {
            var on = !!(config && config.Issues && config.Issues.Enabled);
            showOff(!on);
            if (on) return reload();
            return null;
        }).catch(function () {
            shared.setStatus('issuesStatus', shared.t.tr('IssuesErrorLoad'), true, { timeout: 0 });
        });
    }

    var made = nectoModule.createNectoPage(view, { page: 'necto_issues', load: loadPage });
    shared = made.shared;
    page = made.page;

    return { reload: function () { return reload(); }, showIssue: function (issue) { return showIssue(issue); } };
}
