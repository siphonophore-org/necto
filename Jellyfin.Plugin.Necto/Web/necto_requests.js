// necto_requests.js
// The Requests tool. It counts the requests by status, lists them with search, status, and requester filters, acts
// on the rows picked, and opens each request in a sidecar with the actions it can take.

import * as nectoModule from './configurationpage?name=necto_shared.js';

var h = nectoModule.h;
var userImage = nectoModule.userImage;
var usersImageTags = nectoModule.usersImageTags;
export { userImage, usersImageTags };
var esc = nectoModule.escapeHtml;

// The rows a page of the list holds, and the most requesters the filter lists.
export var PAGE_SIZE = 50;
var REQUESTERS_LIMIT = 200;

// FILTERS_KEY
// Where the status and requester filters are kept for the next visit.
export var FILTERS_KEY = 'necto-requests-filters';

// REQUEST_STATES
// Each request status by the name the server sends, with its string key and badge color.
export var REQUEST_STATES = {
    Pending: { key: 'StatePending', color: 'purple' },
    Approved: { key: 'StateApproved', color: 'blue' },
    Failed: { key: 'LabelFailed', color: 'red' },
    PartiallyAvailable: { key: 'RequestsStatusPartial', color: 'orange' },
    Available: { key: 'LabelAvailable', color: 'green' },
    Declined: { key: 'RequestsRequestStatusRejected', color: 'gray' }
};

// The statuses Open counts, which still wait on something.
var OPEN_STATES = ['Pending', 'Approved', 'Failed', 'PartiallyAvailable'];

// statusLabel
// Names a request status in the viewer's language.
//
// Inputs
// status [string]: The status, such as Pending.
// t [object]: The strings.
//
// Output
// [string]: The name, or the status itself when the page does not know it.
export function statusLabel(status, t) {
    return REQUEST_STATES[status] ? t.tr(REQUEST_STATES[status].key) : String(status || '');
}

// statusColor
// Gives a request status's badge color.
//
// Inputs
// status [string]: The status.
//
// Output
// [string]: The color name, gray for a status the page does not know.
export function statusColor(status) {
    return REQUEST_STATES[status] ? REQUEST_STATES[status].color : 'gray';
}

// statOrder
// Lists the count cards and the status filter's choices in order: Open, then each status.
//
// Inputs
// t [object]: The strings.
//
// Output
// [object[]]: Each one's key, label, color, and for Open the statuses it adds up.
export function statOrder(t) {
    return [{ key: 'Open', label: t.tr('StateOpen'), color: '', of: OPEN_STATES }].concat(Object.keys(REQUEST_STATES).map(function (key) {
        return { key: key, label: statusLabel(key, t), color: REQUEST_STATES[key].color };
    }));
}

// statCards
// Turns the Stats answer into the count cards, one per status in its color.
//
// Inputs
// stats [object]: The Requests/Stats answer, with ByStatus.
// t [object]: The strings.
//
// Output
// [object[]]: The cards, each a label, a count, and a color.
export function statCards(stats, t) {
    var by = (stats && stats.ByStatus) || {};
    return Object.keys(REQUEST_STATES).map(function (key) {
        return { label: statusLabel(key, t), count: by[key] || 0, color: REQUEST_STATES[key].color };
    });
}

// statsKey
// Turns the counts into a string, so two reads can be compared to tell whether the list moved.
//
// Inputs
// stats [object]: The Stats answer, or null.
//
// Output
// [string|null]: The key, or null without counts.
export function statsKey(stats) {
    return stats ? JSON.stringify([stats.Total, stats.ByStatus || {}]) : null;
}

// readFilters
// Reads the status and requester filters kept from the last visit.
//
// Inputs
// storage [object]: The browser storage, or null.
//
// Output
// [object]: status, Open unless kept, and requester, empty for everyone.
export function readFilters(storage) {
    var kept = null;
    try { kept = JSON.parse((storage && storage.getItem(FILTERS_KEY)) || 'null'); } catch (e) { kept = null; }
    kept = kept || {};
    return { status: typeof kept.status === 'string' && kept.status ? kept.status : 'Open', requester: typeof kept.requester === 'string' ? kept.requester : '' };
}

// keepFilters
// Keeps the status and requester filters for the next visit.
//
// Inputs
// storage [object]: The browser storage, or null.
// filters [object]: status and requester.
export function keepFilters(storage, filters) {
    try { if (storage) storage.setItem(FILTERS_KEY, JSON.stringify({ status: filters.status || 'Open', requester: filters.requester || '' })); } catch (e) { /* kept for this visit only */ }
}

// requesterOptions
// Turns the requesters into the requester filter's choices, each with how many requests they made.
//
// Inputs
// requesters [object[]]: The Requests/Requesters items.
//
// Output
// [object[]]: Each choice's value and label.
export function requesterOptions(requesters) {
    return (requesters || []).map(function (r) { return { value: r.UserId, label: r.UserName + ' (' + r.Count + ')' }; });
}

// keptRequester
// Keeps the requester picked while they still have requests, and falls back to everyone once they have none.
//
// Inputs
// requester [string]: The requester picked, or empty.
// requesters [object[]]: The requesters there are.
//
// Output
// [string]: The requester to filter by, or empty for everyone.
export function keptRequester(requester, requesters) {
    return requester && (requesters || []).some(function (r) { return r.UserId === requester; }) ? requester : '';
}

// describeSeasons
// Collapses season numbers into words, such as Seasons 1-3, 5. None is every season.
//
// Inputs
// seasons [number[]]: The season numbers, or null.
// t [object]: The strings.
//
// Output
// [string]: The words.
export function describeSeasons(seasons, t) {
    if (!seasons || !seasons.length) return t.tr('RequestsRequestWholeShow');
    var sorted = seasons.slice().sort(function (a, b) { return a - b; });
    var parts = [];
    for (var i = 0; i < sorted.length; i++) {
        var start = sorted[i];
        while (i + 1 < sorted.length && sorted[i + 1] === sorted[i] + 1) i++;
        parts.push(start === sorted[i] ? String(start) : start + '-' + sorted[i]);
    }
    return sorted.length === 1 ? t.format('NameSeasonNumber', parts[0]) : t.format('RequestsSeasonNumbers', parts.join(', '));
}

// specialNames
// Names a request's specials, each by its name or its number.
//
// Inputs
// request [object]: The request.
// t [object]: The strings.
//
// Output
// [string]: The names, joined.
function specialNames(request, t) {
    return (request.Specials || []).map(function (x) { return x.Name || t.format('RequestsSpecialNumber', x.Number); }).join(', ');
}

// describeWanted
// Says what of a show a request asks for, its seasons or its specials by name.
//
// Inputs
// request [object]: The request.
// t [object]: The strings.
//
// Output
// [string]: The words.
export function describeWanted(request, t) {
    var specials = request.Specials || [];
    if (!specials.length) return describeSeasons(request.Seasons, t);
    return t.format(specials.length === 1 ? 'RequestsWantedSpecial' : 'RequestsWantedSpecials', specialNames(request, t));
}

// profileLabel
// Names a Radarr or Sonarr profile, after its server's name when its type has more than one server.
//
// Inputs
// profiles [object[]]: Every profile, as Requests/Profiles lists them.
// profile [object]: The profile, or null.
//
// Output
// [string|null]: The name, or null for no profile.
export function profileLabel(profiles, profile) {
    if (!profile) return null;
    var servers = (profiles || []).filter(function (p) { return p.Type === profile.Type; }).map(function (p) { return p.ServerId; })
        .filter(function (id, i, all) { return all.indexOf(id) === i; });
    return (servers.length > 1 ? profile.Server + ' · ' : '') + profile.Name;
}

// placedProfile
// Finds the profile a request goes to, as the server does: its own, else the first on its server, else the first of
// its type.
//
// Inputs
// profiles [object[]]: Every profile.
// request [object]: The request.
//
// Output
// [object|null]: The profile, or null when its type has none.
export function placedProfile(profiles, request) {
    var mine = (profiles || []).filter(function (p) { return p.Type === request.Type; });
    return mine.filter(function (p) { return p.Id === request.ProfileId && p.ServerId === request.ServerId; })[0]
        || mine.filter(function (p) { return p.ServerId === request.ServerId; })[0]
        || mine.filter(function (p) { return p.Id === request.ProfileId; })[0]
        || mine[0] || null;
}

// artwork
// Points a TMDb poster at the server's own copy, which the request page keeps.
//
// Inputs
// url [string]: The poster's address.
//
// Output
// [string]: The address to load.
export function artwork(url) {
    var m = /^https:\/\/image\.tmdb\.org\/t\/p\/(w\d+|h\d+|original)\/([A-Za-z0-9_-]+\.(?:jpe?g|png|webp))$/.exec(String(url || ''));
    return m ? '../requests/img/' + m[1] + '/' + m[2] : url;
}

// when
// Writes a moment as a date and a time of day.
//
// Inputs
// iso [string]: The moment.
//
// Output
// [string]: The date and time, or empty when it does not read.
export function when(iso) {
    var d = new Date(iso);
    return !iso || isNaN(d) ? '' : d.toLocaleDateString() + ' ' + d.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
}

// subline
// Writes the line under a request's title, its type, its year, and for a show what it asks for.
//
// Inputs
// r [object]: The request.
// t [object]: The strings.
//
// Output
// [string]: The line.
export function subline(r, t) {
    var parts = [r.Type === 'Series' ? t.tr('LabelShow') : t.tr('Movie')];
    if (r.Year) parts.push(r.Year);
    if (r.Type === 'Series') parts.push(describeWanted(r, t));
    return parts.join(' · ');
}

// requesterLine
// Writes who asked for a request and when, with how many others joined it.
//
// Inputs
// r [object]: The request.
// t [object]: The strings.
//
// Output
// [string]: The line.
export function requesterLine(r, t) {
    var joined = (r.Joined || []).length;
    return [r.UserName, when(r.CreatedUtc), joined ? t.format('RequestsJoinedCount', joined) : ''].filter(Boolean).join(' · ');
}

// progressLine
// Writes a download's progress and the time it has left, while Radarr or Sonarr is getting it.
//
// Inputs
// r [object]: The request.
// t [object]: The strings.
// now [number]: The time now, in milliseconds. [optional]
//
// Output
// [string]: The line, or empty while nothing downloads.
export function progressLine(r, t, now) {
    if (!r.ProgressText || (r.Status !== 'Approved' && r.Status !== 'PartiallyAvailable')) return '';
    var left = r.DownloadDoneUtc ? new Date(r.DownloadDoneUtc).getTime() - (now === undefined ? Date.now() : now) : 0;
    var minutes = Math.max(1, Math.round(left / 60000));
    if (left <= 0) return r.ProgressText;
    return r.ProgressText + ' · ' + (minutes >= 60 ? t.format('RequestsTimeLeftHours', Math.floor(minutes / 60), minutes % 60) : t.format('RequestsTimeLeftMinutes', minutes));
}

// requestActions
// Lists what an administrator can do with a request in its sidecar, in shelf order.
//
// Inputs
// r [object]: The request.
//
// Output
// [string[]]: remove, available, reject, edit, and approve or retry, as they apply. The header's close button closes
// the sidecar, and the title in the library is a link in the details, so the shelf keeps to what changes the request.
export function requestActions(r) {
    var canApprove = r.Status === 'Pending' || r.Status === 'Failed';
    var open = canApprove || r.Status === 'Approved' || r.Status === 'PartiallyAvailable';
    var out = ['remove'];
    if (open) out.push('available', 'reject');
    if (r.Status === 'Pending') out.push('edit');
    if (canApprove) out.push(r.Status === 'Failed' ? 'retry' : 'approve');
    return out;
}

// bulkResultText
// Words what a bulk action did, a sentence for each outcome.
//
// Inputs
// action [string]: approve, decline, or remove.
// result [object]: The Bulk answer, with Done, Failed, and Skipped.
// t [object]: The strings.
//
// Output
// [object]: text, the sentences, and isError, true when some could not be sent.
export function bulkResultText(action, result, t) {
    result = result || {};
    var text = [t.format(action === 'approve' ? 'RequestsToastTitleApproved' : action === 'decline' ? 'RequestsToastTitleRejected' : 'ToastRemovedCount', result.Done || 0)];
    if (result.Failed) text.push(t.format('RequestsBulkNotSent', result.Failed));
    if (result.Skipped) text.push(t.format(action === 'remove' ? 'RequestsBulkSkippedRemoved' : 'RequestsBulkSkippedDecided', result.Skipped));
    return { text: text.join(' '), isError: !!result.Failed };
}

// statusCell
// Draws a row's status, its badge and a line or two on what is happening, with a download's progress bar.
//
// Inputs
// r [object]: The request.
// shared [object]: The Necto helper bag.
//
// Output
// [string]: The cell's HTML.
export function statusCell(r, shared) {
    var t = shared.t;
    var line = function (text) { return '<div class="stolon-table-item-sub">' + esc(text) + '</div>'; };
    var html = shared.badge(statusColor(r.Status), statusLabel(r.Status, t));
    if (r.Status === 'PartiallyAvailable' && r.AvailableSeasons) html += line(t.format('RequestsHasSeasons', describeSeasons(r.AvailableSeasons, t)));
    var progress = progressLine(r, t);
    if (progress) {
        html += line(progress);
        if (typeof r.Progress === 'number') {
            var percent = Math.max(0, Math.min(100, Math.round(r.Progress * 100)));
            html += '<div class="stolon-progress" role="progressbar" aria-valuemin="0" aria-valuemax="100" aria-valuenow="' + percent + '"><div class="stolon-progress-bar" style="width:' + percent + '%"></div></div>';
        }
    }
    if (r.Note && (r.Status === 'Failed' || r.Status === 'Declined')) html += line(r.Note);
    return html;
}

// detailItems
// Lists a request's details for its sidecar, such as where it goes, its seasons, and its TMDb page. Who asked for it
// and their notes have sections of their own.
//
// Inputs
// r [object]: The request.
// t [object]: The strings.
// goesTo [string|Node]: Where it goes, as text or as the profile picker, or null.
//
// Output
// [object[]]: The label and value pairs.
export function detailItems(r, t, goesTo) {
    var specials = r.Specials && r.Specials.length;
    return [
        goesTo ? { label: t.tr('RequestsLabelGoesTo'), value: goesTo } : null,
        r.Type === 'Series' ? (specials
            ? { label: t.tr('OptionSpecialEpisode'), value: specialNames(r, t) }
            : { label: t.tr('HeaderSeasons'), value: describeWanted(r, t) }) : null,
        r.AvailableSeasons && !specials ? { label: t.tr('RequestsRequestStatusInLibrary'), value: describeSeasons(r.AvailableSeasons, t) } : null,
        r.RetryAfterUtc && r.Status === 'Failed' ? { label: t.tr('RequestsLabelNextRetry'), value: when(r.RetryAfterUtc) } : null,
        r.ProviderIds && r.ProviderIds.Tmdb ? {
            label: 'TMDb',
            value: { text: String(r.ProviderIds.Tmdb), link: 'https://www.themoviedb.org/' + (r.Type === 'Series' ? 'tv' : 'movie') + '/' + encodeURIComponent(r.ProviderIds.Tmdb) }
        } : null
    ].filter(Boolean);
}

// noteItems
// Lists a request's notes for its timeline, oldest first: the requester's note, then the decision with its reason.
//
// Inputs
// r [object]: The request.
// t [object]: The strings.
//
// Output
// [object[]]: The timeline items, empty when the request has no note and no decision.
export function noteItems(r, t, tags) {
    var items = [];
    if (r.RequesterNote) items.push(nectoModule.withAvatar({ title: r.UserName, time: when(r.CreatedUtc), datetime: r.CreatedUtc || undefined, body: r.RequesterNote }, r.UserId, r.UserName, tags));
    if (r.DecidedBy) {
        var declined = r.Status === 'Declined';
        items.push({
            title: t.format(declined ? 'RequestsNoteRejected' : 'RequestsNoteApproved', r.DecidedBy),
            time: when(r.DecidedUtc),
            datetime: r.DecidedUtc || undefined,
            body: declined ? (r.Note || '') : '',
            icon: declined ? 'block' : 'check',
            status: declined ? 'gray' : 'blue'
        });
    }
    return items;
}

// joinerItems
// Lists the people who asked for a request's title after it was made, oldest first.
//
// Inputs
// r [object]: The request.
//
// Output
// [object[]]: Each { userId, name, when }, without a joiner the viewer may not see.
export function joinerItems(r) {
    return (r.Joined || []).filter(function (j) { return j && j.UserName; }).slice().sort(function (a, b) {
        return String(a.JoinedUtc || '').localeCompare(String(b.JoinedUtc || ''));
    }).map(function (j) { return { userId: j.UserId, name: j.UserName, when: when(j.JoinedUtc) }; });
}

export default function (view) {
    'use strict';

    var shared = null;
    var page = null;
    var table = null;
    // Whether no one has made a request at all, so the table says No requests yet whatever its filter.
    var none = false;
    var banner = null;
    var filters = { status: 'Open', requester: '' };
    var requesters = [];
    var tableRequesters = null;
    var imageTags = null;
    var profiles = [];
    var shownStats = null;
    var requestersTotal = null;

    // storage
    // Gives the browser storage, or null where the browser refuses it.
    function storage() {
        try { return globalThis.localStorage || null; } catch (e) { return null; }
    }

    // report
    // Shows a result on the page's status line.
    function report(message, isError, sticky) {
        shared.setStatus('requestsStatus', message, !!isError, sticky ? { timeout: 0 } : undefined);
    }

    // failed
    // Shows why a call failed, the server's reason when it gave one, on a status line.
    function failed(err, fallback, status) {
        return shared.errorText(err, shared.t.tr(fallback)).then(function (text) {
            shared.setStatus(status || 'requestsStatus', text, true, { timeout: 0 });
        });
    }

    // loadStats
    // Reads the counts and draws the cards.
    function loadStats() {
        return shared.request('Requests/Stats').then(function (stats) {
            stats = stats || {};
            shared.renderCards('requestStats', statCards(stats, shared.t));
            none = !stats.Total;
            if (table) table.setNone(none);
            return stats;
        }).catch(function () { return null; });
    }

    // loadRequesters
    // Reads everyone who made requests, for the requester filter, and drops a kept requester who has none left.
    function loadRequesters() {
        return shared.request('Requests/Requesters?limit=' + REQUESTERS_LIMIT).then(function (answer) {
            requesters = (answer && answer.Items) || [];
            var kept = keptRequester(filters.requester, requesters);
            if (kept !== filters.requester) { filters.requester = kept; keepFilters(storage(), filters); }
        }).catch(function () { /* the filter keeps its last list */ });
    }

    // requestersKey
    // Turns the requester choices into a string, to tell when the filter needs new ones.
    function requestersKey() {
        return JSON.stringify(requesterOptions(requesters));
    }

    // setChanged
    // Shows or hides the notice that the list moved since it loaded.
    function setChanged(on) {
        view.querySelector('#requestsChanged').classList.toggle('stolon-hidden', !on);
    }

    // markShown
    // Notes the counts the list now matches and hides the notice.
    function markShown(stats) {
        setChanged(false);
        var seen = function (s) { shownStats = statsKey(s); if (s && requestersTotal === null) requestersTotal = s.Total; };
        if (stats !== undefined) { seen(stats); return; }
        loadStats().then(seen);
    }

    // createTable
    // Builds the list with its search, its two filters, and its bulk actions.
    function createTable() {
        var t = shared.t;
        tableRequesters = requestersKey();
        table = nectoModule.createToolTable(view, shared, {
            containerId: 'requestsTable',
            endpoint: 'Requests/All',
            idKey: 'RequestId',
            search: t.tr('RequestsSearchRequests'),
            filters: [
                {
                    label: t.tr('HeaderStatus'),
                    options: statOrder(t).map(function (s) { return { value: s.key, label: s.label }; }),
                    initialValue: filters.status,
                    onChange: function (value) { filters.status = value; keepFilters(storage(), filters); markShown(); }
                },
                {
                    name: 'user',
                    label: t.tr('RequestsLabelRequester'),
                    options: requesterOptions(requesters),
                    allLabel: t.tr('GroupEveryone'),
                    initialValue: filters.requester,
                    onChange: function (value) { filters.requester = value; keepFilters(storage(), filters); markShown(); }
                }
            ],
            pageSize: PAGE_SIZE,
            empty: { message: t.tr('RequestsEmpty'), filteredMessage: t.tr('RequestsEmptyMatch') },
            image: function (item) { return item.Request && item.Request.PosterUrl ? artwork(item.Request.PosterUrl) : null; },
            imageShape: 'portrait',
            title: function (item) { return (item.Request || {}).Title; },
            lines: function (item) {
                var r = item.Request || {};
                return [subline(r, t), requesterLine(r, t), r.RequesterNote ? '“' + r.RequesterNote + '”' : ''];
            },
            statusFooter: function (item) { return statusCell(item.Request || {}, shared); },
            bulk: [{ verb: 'approve', text: t.tr('RequestsButtonApprove'), variant: 'primary' }, { verb: 'decline', text: t.tr('RequestsButtonReject') }, { verb: 'remove', text: t.tr('ButtonRemove'), variant: 'destructive' }],
            onBulk: function (verb) { bulk(verb); },
            onRowClick: function (item) { if (item.Request) showRequest(item.Request); },
            onReload: function () { markShown(); }
        });
    }

    // destroyTable
    // Takes the list down when the page hides.
    function destroyTable() {
        if (table) { table.destroy(); table = null; }
    }

    // loadTable
    // Loads the list from the top, building it the first time, and gives the requester filter new choices when they changed.
    function loadTable() {
        if (!table) {
            createTable();
            table.setNone(none);
            return table.load();
        }
        if (tableRequesters !== requestersKey()) {
            tableRequesters = requestersKey();
            table.setFilterOptions(requesterOptions(requesters), 'user');
        }
        return table.reload();
    }

    // reload
    // Reads the counts, the requesters, and the list again from the top.
    function reload() {
        usersImageTags().then(function (tags) { imageTags = tags; });
        return Promise.all([loadStats(), loadRequesters()]).then(function (results) {
            if (results[0]) requestersTotal = results[0].Total;
            return loadTable().then(function () { markShown(results[0]); });
        });
    }

    // quiet
    // Tells whether the list can load again without losing anything: one page, nothing picked or searched, no
    // sidecar or dialog open, and focus outside the list.
    function quiet() {
        var mount = view.querySelector('#requestsTable');
        var search = mount.querySelector('input[type="search"]');
        return table.getItems().length <= PAGE_SIZE
            && !table.getSelectedIds().length
            && !(search && search.value)
            && !document.querySelector('.stolon-dialog, .stolon-sidecar')
            && !mount.contains(document.activeElement);
    }

    // poll
    // Reads the counts every 30 seconds, and the list when something moved or a row downloads. A busy list shows the
    // notice instead.
    function poll() {
        if (document.hidden) return null;
        return loadStats().then(function (stats) {
            if (!stats || !table) return null;
            var who = stats.Total !== requestersTotal
                ? loadRequesters().then(function () { requestersTotal = stats.Total; })
                : Promise.resolve();
            return who.then(function () {
                if (!table) return null;
                var changed = statsKey(stats) !== shownStats;
                var renamed = tableRequesters !== requestersKey();
                var downloading = table.getItems().some(function (item) { return item && item.Request && typeof item.Request.Progress === 'number'; });
                if (!changed && !renamed && !downloading) return null;
                if (quiet()) return loadTable().then(function () { markShown(stats); });
                if (changed || renamed) setChanged(true);
                return null;
            });
        });
    }

    // act
    // Approves, rejects with a reason, marks available, or removes one request, asking first where it matters.
    //
    // Inputs
    // r [object]: The request.
    // action [string]: approve, decline, available, or remove.
    // close [function]: Closes the request's sidecar once done. [optional]
    // profile [string]: The profile to send it with. [optional]
    // status [HTMLElement]: Where to say what went wrong. [optional]
    function act(r, action, close, profile, status) {
        var t = shared.t;
        var run;
        if (action === 'decline') {
            run = nectoModule.promptSidecar([{ name: 'Reason', label: t.format('RequestsLabelReasonShownTo', r.UserName), type: 'textarea' }], { title: t.format('RequestsLabelRejectTitle', r.Title), submitText: t.tr('RequestsButtonReject') })
                .then(function (values) { return values && shared.request('Requests/' + r.Id + '/Decline', 'POST', { Reason: values.Reason || '' }); });
        } else if (action === 'remove') {
            run = nectoModule.confirmDialog(t.format('RequestsConfirmRemoveRequest', r.UserName), { title: t.format('TitleRemoveName', r.Title), confirmText: t.tr('ButtonRemove'), destructive: true })
                .then(function (ok) { return ok ? shared.request('Requests/' + r.Id, 'DELETE').then(function (answer) { return answer || {}; }) : null; });
        } else if (action === 'available') {
            run = nectoModule.confirmDialog(t.format('RequestsConfirmMarkAvailable', r.UserName), { title: t.format('RequestsHeaderMarkAvailable', r.Title), confirmText: t.tr('RequestsButtonMarkAvailable') })
                .then(function (ok) { return ok ? shared.request('Requests/' + r.Id + '/Available', 'POST').then(function (answer) { return answer || {}; }) : null; });
        } else {
            run = shared.request('Requests/' + r.Id + '/Approve' + (profile ? '?profileId=' + encodeURIComponent(profile) : ''), 'POST').then(function (answer) { return answer || {}; });
        }
        return run.then(function (result) {
            if (!result) return;
            if (close) close();
            var done = result.Request;
            if (action === 'approve' && done && done.Status === 'Failed') report(t.format('RequestsToastApprovedNotSent', done.Note || ''), true, true);
            else report(t.tr(action === 'approve' ? 'RequestsToastApproved' : action === 'decline' ? 'RequestsToastRejected' : action === 'available' ? 'RequestsToastMarkedAvailable' : 'RequestsToastRemoved'), false);
            reload();
        }).catch(function (err) {
            return failed(err, 'RequestsErrorUpdateRequest', status);
        });
    }

    // bulk
    // Approves, rejects, or removes every row picked, asking first for a reject's reason or a removal.
    function bulk(action) {
        var t = shared.t;
        var ids = table ? table.getSelectedIds() : [];
        if (!ids.length) return Promise.resolve();
        var ask = action === 'decline'
            ? nectoModule.promptSidecar([{ name: 'Reason', label: t.tr('RequestsLabelReasonShownToRequesters'), type: 'textarea' }], { title: t.plural(ids.length, 'RequestsHeaderRejectRequest', 'RequestsHeaderRejectRequests'), submitText: t.tr('RequestsButtonReject') })
            : action === 'remove'
                ? nectoModule.confirmDialog(t.tr('RequestsConfirmRemoveRequests'), { title: shared.t.plural(ids.length, 'RequestsHeaderRemoveCountOne', 'RequestsHeaderRemoveCountMany'), confirmText: t.tr('ButtonRemove'), destructive: true }).then(function (ok) { return ok ? {} : null; })
                : Promise.resolve({});
        return ask.then(function (values) {
            if (!values) return null;
            report(t.tr('TextWorking'), false);
            return shared.request('Requests/Bulk/' + action, 'POST', { Ids: ids, Reason: values.Reason || '' }).then(function (result) {
                var said = bulkResultText(action, result, t);
                report(said.text, said.isError);
                return reload();
            }).catch(function (err) {
                return failed(err, 'RequestsErrorUpdateRequests');
            });
        });
    }

    // profilePicker
    // Builds the Goes to select for a request not yet sent, when its type has several profiles.
    //
    // Output
    // [HTMLSelectElement|null]: The select, or null when there is nothing to pick.
    function profilePicker(r, id) {
        var choices = profiles.filter(function (p) { return p.Type === r.Type; });
        if (choices.length < 2) return null;
        var placed = placedProfile(profiles, r);
        var picker = h('select', { is: 'emby-select', id: id || null, 'aria-label': shared.t.tr('RequestsLabelGoesTo') });
        choices.forEach(function (p) {
            var option = h('option', { value: p.Id, text: profileLabel(profiles, p) });
            if (placed && p.Id === placed.Id) option.selected = true;
            picker.appendChild(option);
        });
        if (placed) picker.value = placed.Id;
        return picker;
    }

    // noteOnly
    // Tells whether a request's editor holds its note alone, a movie with no profile to pick.
    function noteOnly(r) {
        return r.Type !== 'Series' && profiles.filter(function (p) { return p.Type === r.Type; }).length < 2;
    }

    // showRequest
    // Opens a request in a sidecar: its poster, title, and status, its details, its overview, and its actions.
    // The title in the library is linked in the details, and Radarr's or Sonarr's page for it once found.
    function showRequest(r) {
        var t = shared.t;
        var canApprove = r.Status === 'Pending' || r.Status === 'Failed';
        var picker = canApprove && !r.ArrId ? profilePicker(r) : null;
        var goesTo = picker || profileLabel(profiles, placedProfile(profiles, r));
        var card = nectoModule.createMediaCard({
            layout: 'row',
            shape: 'portrait',
            image: r.PosterUrl ? artwork(r.PosterUrl) : null,
            title: r.Title,
            subtitle: subline(r, t),
            detail: [
                r.Status === 'PartiallyAvailable' && r.AvailableSeasons ? t.format('RequestsHasSeasons', describeSeasons(r.AvailableSeasons, t)) : '',
                progressLine(r, t),
                r.Note && r.Status === 'Declined' ? r.Note : ''
            ].filter(Boolean).join(' · ') || null,
            error: r.Note && r.Status === 'Failed' ? r.Note : null
        });
        var items = detailItems(r, t, goesTo);
        var sidecar = null;
        if (r.LibraryItemId) {
            var library = h('a', { is: 'emby-linkbutton', class: 'button-link', href: '#', text: t.tr('ButtonOpenInLibrary') });
            library.addEventListener('click', function (e) {
                e.preventDefault();
                if (sidecar) sidecar.close();
                Dashboard.navigate('details?id=' + r.LibraryItemId);
            });
            items.push({ label: t.tr('LabelJellyfin'), value: library });
        }
        var details = nectoModule.createDefinitionList({ items: items });
        var status = h('div', { class: 'stolon-status-text', role: 'alert', 'data-role': 'status' });
        var parts = [card, details];

        // The requester with their picture, then the notes on the request.
        var requester = nectoModule.createUserChip({ name: r.UserName, image: userImage(r.UserId, imageTags), text: t.format('RequestsValueRequestedOn', when(r.CreatedUtc)) });
        var notes = noteItems(r, t, imageTags);
        var timeline = notes.length ? nectoModule.createTimeline({ items: notes }) : null;
        var requesterBody = h('div', { class: 'stolon-stack', 'data-role': 'requester' }, [requester.element, timeline ? timeline.element : null]);
        var requesterSection = nectoModule.createCollapsibleSection({ title: t.tr('RequestsLabelRequester'), icon: 'person', collapsible: false, content: requesterBody });
        parts.push(requester, requesterSection);
        if (timeline) parts.push(timeline);

        // Everyone who asked for the same title after it was made, hidden while nobody did.
        var joiners = joinerItems(r);
        var joinedSection = null;
        if (joiners.length) {
            var joinedBody = h('div', { class: 'stolon-stack', 'data-role': 'joined' });
            joiners.forEach(function (j) {
                var chip = nectoModule.createUserChip({ name: j.name, image: userImage(j.userId, imageTags), text: t.format('RequestsValueJoinedOn', j.when) });
                parts.push(chip);
                joinedBody.appendChild(chip.element);
            });
            joinedSection = nectoModule.createCollapsibleSection({ title: t.tr('RequestsLabelAlsoWantedBy'), icon: 'group_add', collapsible: false, content: joinedBody });
            parts.push(joinedSection);
        }

        var body = h('div', { class: 'stolon-stack', 'data-role': 'request-detail' }, [
            card.element,
            details.element,
            r.Overview ? h('div', { class: 'fieldDescription', text: r.Overview }) : null,
            requesterSection.element,
            joinedSection ? joinedSection.element : null,
            status
        ]);

        var labels = {
            remove: ['ButtonRemove', 'stolon-destructive'],
            available: ['RequestsButtonMarkAvailable', 'stolon-secondary'], reject: ['RequestsButtonReject', 'stolon-secondary'],
            edit: [noteOnly(r) ? 'RequestsButtonEditNote' : 'Edit', 'stolon-secondary'], approve: ['RequestsButtonApprove', 'stolon-primary'], retry: ['Retry', 'stolon-primary']
        };
        var run = {
            remove: function (close) { act(r, 'remove', close, null, status); },
            available: function (close) { act(r, 'available', close, null, status); },
            reject: function (close) { act(r, 'decline', close, null, status); },
            edit: function (close) { close(); editRequest(r); },
            approve: function (close) { act(r, 'approve', close, picker && picker.value, status); },
            retry: function (close) { act(r, 'approve', close, picker && picker.value, status); }
        };
        sidecar = nectoModule.openSidecar({
            page: page,
            size: 'large',
            title: r.Title,
            badge: { label: statusLabel(r.Status, t), color: statusColor(r.Status) },
            body: body,
            buttons: requestActions(r).map(function (key) {
                return { text: t.tr(labels[key][0]), className: 'stolon-button ' + labels[key][1], action: function (close) { run[key](close); } };
            }),
            onClose: function () { parts.forEach(function (part) { if (part && part.destroy) part.destroy(); }); }
        });

        if (r.ArrId) {
            shared.request('Requests/' + r.Id + '/ArrLink').then(function (link) {
                if (!link || !link.Url || !sidecar.isOpen()) return;
                details.setItems(items.concat([{ label: link.Server, value: { text: t.format('ButtonOpenIn', link.Server), link: link.Url } }]));
            }).catch(function () { /* no link, then */ });
        }
        return sidecar;
    }

    // editRequest
    // Opens a waiting request in an editor sidecar: its profile, a show's seasons, and its note.
    function editRequest(r) {
        var t = shared.t;
        var series = r.Type === 'Series';
        var placed = placedProfile(profiles, r);
        var picker = profilePicker(r, 'necto-request-profile');
        var note = h('textarea', { is: 'emby-textarea', id: 'necto-request-note', rows: '3', maxlength: '500' });
        note.value = r.RequesterNote || '';
        var status = h('div', { class: 'stolon-status-text', role: 'alert', 'data-role': 'status' });
        var seasons = null;
        var seasonsRead = false;
        var seasonsLoaded = Promise.resolve();
        var body = h('div', { 'data-role': 'request-editor' }, [
            picker ? h('div', { class: 'selectContainer' }, [h('label', { class: 'selectLabel', for: 'necto-request-profile', text: t.tr('Profile') }), picker]) : null
        ]);
        if (series) {
            seasons = nectoModule.createChipSelect({ placeholder: t.tr('RequestsMessageLoadingSeasons') });
            body.appendChild(h('div', { class: 'inputContainer', 'data-field': 'Seasons' }, [
                h('label', { class: 'inputLabel', text: t.tr('HeaderSeasons') }),
                seasons.element,
                h('div', { class: 'fieldDescription', text: t.tr('RequestsHelpEditSeasons') })
            ]));
            seasonsLoaded = shared.request('Requests/Titles/Seasons', 'POST', { Title: r.Title, Year: r.Year, ProviderIds: r.ProviderIds }).then(function (answer) {
                var mine = function (n) { return !r.Seasons || r.Seasons.indexOf(n) >= 0; };
                var open = ((answer && answer.Seasons) || []).filter(function (s) { return !s.InLibrary && (!s.Requested || mine(s.Number)); })
                    .map(function (s) { return { value: String(s.Number), label: t.format('NameSeasonNumber', s.Number) }; });
                seasons.setOptions(open);
                seasonsRead = true;
                seasons.setValue(r.Seasons ? open.filter(function (o) { return mine(Number(o.value)); }) : []);
                var input = seasons.element.querySelector('input');
                if (input) { input.placeholder = open.length ? t.tr('RequestsPlaceholderChooseSeasons') : t.tr('RequestsPlaceholderNoSeasons'); input.setAttribute('aria-label', t.tr('HeaderSeasons')); }
            }).catch(function () {
                var input = seasons.element.querySelector('input');
                if (input) input.placeholder = t.tr('RequestsErrorLoadSeasons');
            });
        }
        body.appendChild(h('div', { class: 'inputContainer' }, [h('label', { class: 'inputLabel', for: 'necto-request-note', text: t.tr('RequestsLabelRequestNote') }), note]));
        body.appendChild(status);

        // save
        // Saves the request, and keeps the editor open with the server's reason when it refuses.
        function save() {
            // A show keeps its seasons until they are read and when none is picked, so a clear never widens it.
            var picked = seasonsRead ? seasons.getValue().map(Number).filter(function (n) { return n > 0; }) : [];
            return shared.request('Requests/' + r.Id, 'POST', {
                Seasons: !series ? null : picked.length ? picked : (r.Seasons || null),
                ProfileId: picker ? picker.value : (placed ? placed.Id : null),
                Note: note.value
            }).then(function () {
                report(t.tr('ToastSaved'), false);
                reload();
                return true;
            }, function (err) {
                return failed(err, 'RequestsErrorUpdateRequest', status).then(function () { return false; });
            });
        }

        return nectoModule.openEditorSidecar({
            page: page,
            title: t.format('RequestsEditTitle', r.Title),
            size: 'medium',
            isNew: true,
            body: body,
            ready: seasonsLoaded,
            saveText: t.tr('Save'),
            serialize: function () { return JSON.stringify([picker ? picker.value : '', seasons ? seasons.getValue() : [], note.value]); },
            onSave: save,
            onClose: function () { if (seasons) seasons.destroy(); }
        });
    }

    // bindPage
    // Draws the notice that the list moved, with Show latest.
    function bindPage() {
        banner = nectoModule.createBanner({
            kind: 'info',
            text: shared.t.tr('RequestsMessageRequestsChanged'),
            dismissible: false,
            actions: [{ text: shared.t.tr('RequestsButtonShowLatest'), onClick: function () { reload(); } }]
        });
        view.querySelector('#requestsChanged').appendChild(banner.element);
    }

    // loadPage
    // Reads the profiles, the requesters, and the list on the kept filters, then keeps them fresh while the page shows.
    function loadPage() {
        filters = readFilters(storage());
        shared.request('Requests/Profiles').then(function (list) { profiles = Array.isArray(list) ? list : []; }, function () { profiles = []; });
        page.addCleanup(destroyTable);
        loadRequesters().then(loadTable).then(function () { markShown(); });
        page.createPoller(poll, 30000).start(false);
        var onVisible = function () { if (!document.hidden) poll(); };
        document.addEventListener('visibilitychange', onVisible);
        page.addCleanup(function () { document.removeEventListener('visibilitychange', onVisible); });
    }

    var made = nectoModule.createNectoPage(view, { page: 'necto_requests', bind: bindPage, load: loadPage });
    shared = made.shared;
    page = made.page;

    return { reload: function () { return reload(); }, poll: function () { return poll(); }, bulk: function (action) { return bulk(action); }, showRequest: function (r) { return showRequest(r); } };
}
