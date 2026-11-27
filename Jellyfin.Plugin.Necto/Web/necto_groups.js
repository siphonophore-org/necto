// necto_groups.js
// The Groups tool. It lists the groups, Everyone first, and opens each in an editor sidecar with its name, its
// members, and a collapsible section of permissions per tool, with Delete, Cancel, and Save on the shelf.

import * as nectoModule from './configurationpage?name=necto_shared.js';
import * as nectoTemplates from './configurationpage?name=necto_groups_templates.js';

var h = nectoModule.h;

// EVERYONE_ID
// The id of the Everyone group, which holds every user in no other group.
export var EVERYONE_ID = '5a1f0c7e-0000-4000-8000-000000000001';

// The names of each choice field's values, in the order the server numbers them.
var CHANGE_MODES = ['Allowed', 'InitialOnly', 'Disallowed'];
var EXPIRY_ACTIONS = ['Disable', 'Delete'];
var SESSION_MODES = ['All', 'Only', 'AllExcept'];

// Ids for the editor's fields, so each label names its own field.
var fieldSeq = 0;

// sameId
// Compares two ids whatever their case or dashes.
//
// Inputs
// a [string]: One id.
// b [string]: The other id.
//
// Output
// [boolean]: Whether they name the same thing.
export function sameId(a, b) {
    var clean = function (v) { return String(v || '').replace(/-/g, '').toLowerCase(); };
    return clean(a) === clean(b);
}

// isEveryone
// Tells whether a group is Everyone.
//
// Inputs
// group [object]: The group.
//
// Output
// [boolean]: Whether it is Everyone.
export function isEveryone(group) {
    return !!group && (group.IsEveryone === true || sameId(group.Id, EVERYONE_ID));
}

// enumName
// Reads a choice the server may send as its number or its name.
//
// Inputs
// value [number|string]: The stored choice.
// names [string[]]: The choice names in the server's order.
//
// Output
// [string]: The choice's name, or the first name when it is unknown.
export function enumName(value, names) {
    if (typeof value === 'number') return names[value] || names[0];
    return names.indexOf(value) >= 0 ? value : names[0];
}

// deletesOnExpiry
// Tells whether a group deletes its members when it expires, so a save asks first.
//
// Inputs
// group [object]: The group as the editor gives it.
//
// Output
// [boolean]: True when the group has an expiry date and Delete as its method.
export function deletesOnExpiry(group) {
    var expiry = group && group.Permissions && group.Permissions.Accounts && group.Permissions.Accounts.Expiry;
    return !!(expiry && expiry.ExpiresOn) && enumName(expiry.Action, EXPIRY_ACTIONS) === 'Delete';
}

// grantablePages
// Lists the pages a group can give, the Signed in ones, as Anyone and Administrators pages ignore groups. A page
// the group already gives stays, so a page moved to another tier keeps its grant.
//
// Inputs
// pages [object[]]: The pages from the Pages route.
// picked [string[]]: The ids the group gives now.
//
// Output
// [object[]]: The pages to offer.
export function grantablePages(pages, picked) {
    return (pages || []).filter(function (p) {
        return p.Visibility === 'User' || p.Visibility === 1 || (picked || []).some(function (id) { return sameId(id, p.Id); });
    });
}

// getPath
// Reads a value from an object by a dotted path, such as Accounts.Password.MinLength.
//
// Inputs
// obj [object]: The object.
// path [string]: The dotted path.
//
// Output
// [any]: The value, or undefined when a step is missing.
function getPath(obj, path) {
    return path.split('.').reduce(function (o, k) { return o == null ? undefined : o[k]; }, obj);
}

// setPath
// Writes a value into an object by a dotted path, making each missing step.
//
// Inputs
// obj [object]: The object.
// path [string]: The dotted path.
// value [any]: The value.
function setPath(obj, path, value) {
    var keys = path.split('.');
    var last = keys.pop();
    var target = keys.reduce(function (o, k) {
        if (o[k] == null || typeof o[k] !== 'object') o[k] = {};
        return o[k];
    }, obj);
    target[last] = value;
}

// groupName
// Names a group for the page, Everyone in the viewer's language.
//
// Inputs
// group [object]: The group.
// t [object]: The strings.
//
// Output
// [string]: The name.
export function groupName(group, t) {
    return isEveryone(group) ? t.tr('GroupEveryone') : (group.Name || '');
}

// accountTags
// Lists the account rules a group has on, as tags for its row.
//
// Inputs
// group [object]: The group.
// t [object]: The strings.
//
// Output
// [object[]]: One tag per rule that is on.
export function accountTags(group, t) {
    var a = (group.Permissions && group.Permissions.Accounts) || {};
    var tags = [];
    if (a.Password && a.Password.Enabled) tags.push({ label: t.tr('HeaderPasswordRequirements'), icon: 'password' });
    if (a.Expiry && a.Expiry.ExpiresOn) tags.push({ label: t.tr('HeaderExpiry'), icon: 'event' });
    if (a.Inactivity && a.Inactivity.Enabled) tags.push({ label: t.tr('HeaderInactivity'), icon: 'bedtime' });
    if (a.Sessions && a.Sessions.Enabled) tags.push({ label: t.tr('HeaderSessionManagement'), icon: 'devices' });
    return tags;
}

// userCounts
// Counts the users for the cards over the list: administrators, active, inactive past their groups' window,
// disabled, and those whose groups expire them within 7 days.
//
// Inputs
// users [object[]]: The users from Groups/Users.
// now [Date]: The time to count at.
//
// Output
// [object]: { admins, active, inactive, disabled, expiring }.
export function userCounts(users, now) {
    var counts = { admins: 0, active: 0, inactive: 0, disabled: 0, expiring: 0 };
    var today = new Date(now.getTime());
    today.setHours(0, 0, 0, 0);
    var soon = new Date(today.getTime());
    soon.setDate(soon.getDate() + 7);
    (users || []).forEach(function (u) {
        if (u.IsAdministrator) { counts.admins++; return; }
        if (u.ExpiresOn) {
            var due = new Date(String(u.ExpiresOn).slice(0, 10) + 'T00:00:00');
            if (due >= today && due <= soon) counts.expiring++;
        }
        if (u.IsDisabled) { counts.disabled++; return; }
        var last = u.LastActivityDate ? new Date(u.LastActivityDate).getTime() : NaN;
        if (u.InactiveDays > 0 && !isNaN(last) && last < now.getTime() - u.InactiveDays * 86400000) counts.inactive++;
        else counts.active++;
    });
    return counts;
}

// userCards
// Turns the user counts into the cards over the list.
//
// Inputs
// counts [object]: What userCounts gives.
// t [object]: The strings.
//
// Output
// [object[]]: The cards.
export function userCards(counts, t) {
    return [
        { count: counts.admins, label: t.tr('LabelAdministrators'), color: 'blue' },
        { count: counts.active, label: t.tr('StateActive'), color: 'green' },
        { count: counts.inactive, label: t.tr('StateInactive'), color: 'yellow' },
        { count: counts.disabled, label: t.tr('Disabled'), color: 'red' },
        { count: counts.expiring, label: t.tr('GroupsCardExpiring'), color: 'purple' }
    ];
}

// groupItems
// Turns the groups into rows for the record list, Everyone first, each with its member count and its rules.
// Everyone counts the users no group holds.
//
// Inputs
// groups [object[]]: The groups from Groups, Everyone among them.
// users [object[]]: The users from Groups/Users.
// shared [object]: The Necto helper bag.
//
// Output
// [object[]]: The rows, each carrying its group.
export function groupItems(groups, users, shared) {
    var t = shared.t;
    var list = (groups || []).slice().sort(function (a, b) { return (isEveryone(b) ? 1 : 0) - (isEveryone(a) ? 1 : 0); });
    var loose = (users || []).filter(function (u) {
        return !u.IsAdministrator && (u.GroupIds || []).some(function (id) { return sameId(id, EVERYONE_ID); });
    }).length;
    return list.map(function (group) {
        var secondary = isEveryone(group)
            ? shared.t.plural(loose, 'LabelLooseUsersOne', 'LabelLooseUsersMany')
            : shared.t.plural((group.MemberIds || []).length, 'LabelMemberCountOne', 'LabelMemberCountMany');
        return {
            id: group.Id,
            title: groupName(group, t),
            secondary: secondary,
            tags: accountTags(group, t),
            group: group
        };
    });
}

// sectionSpecs
// Describes the permission sections of the editor after Account rules, one per tool, in sidebar order. Each field
// names its path in the group's permissions. A field with reveals shows them while it is on, and fallback is its
// value while the group has none.
//
// Output
// [object[]]: Each section's tool key, title key, icon, and fields.
export function sectionSpecs() {
    return [
        {
            key: 'requests', title: 'TabRequests', icon: 'movie_filter', fields: [
                { path: 'Requests.MayRequest', kind: 'check', label: 'LabelMayRequest', reveals: [
                    { path: 'Requests.Movies', kind: 'check', label: 'Movies', fallback: true },
                    { path: 'Requests.Series', kind: 'check', label: 'Series', fallback: true },
                    { path: 'Requests.Unreleased', kind: 'check', label: 'RequestsLabelUnreleasedTitles', help: 'RequestsHelpUnreleasedTitles', fallback: true },
                    { path: 'Requests.AutoApproveMovies', kind: 'check', label: 'RequestsLabelAutoApproveMovies' },
                    { path: 'Requests.AutoApproveSeries', kind: 'check', label: 'RequestsLabelAutoApproveSeries' },
                    { path: 'Requests.MaxOpenRequests', kind: 'number', label: 'RequestsLabelOpenRequestsAtOnce', help: 'RequestsHelpOpenAtOnce', min: 0, fallback: 10 },
                    { path: 'Requests.MovieQuota', kind: 'number', label: 'LabelMovieQuota', help: 'RequestsHelpMovieQuota', min: 0 },
                    { path: 'Requests.SeriesQuota', kind: 'number', label: 'LabelSeriesQuota', help: 'RequestsHelpSeriesQuota', min: 0 },
                    { path: 'Requests.MaxSeasons', kind: 'number', label: 'RequestsLabelSeasonsPerSeries', help: 'RequestsHelpSeasonsPerSeries', min: 0 },
                    { path: 'Requests.MovieProfiles', kind: 'profiles', type: 'Movie', label: 'RequestsLabelMovieProfiles', help: 'RequestsHelpProfileList' },
                    { path: 'Requests.SeriesProfiles', kind: 'profiles', type: 'Series', label: 'RequestsLabelSeriesProfiles', help: 'RequestsHelpProfileList' },
                    { path: 'Requests.MaySeeOthers', kind: 'check', label: 'RequestsLabelSeeAll', help: 'RequestsHelpSeeAll' },
                    { path: 'Requests.MaySeeRequesters', kind: 'check', label: 'RequestsTagSeeRequesters', help: 'RequestsHelpSeeRequesters' }
                ] },
                { path: 'Requests.MayManage', kind: 'check', label: 'LabelMayManageRequests', help: 'RequestsHelpManage' }
            ]
        },
        {
            key: 'issues', title: 'TabIssues', icon: 'report_problem', fields: [
                { path: 'Issues.MayReport', kind: 'check', label: 'IssuesLabelMayReport', reveals: [
                    { path: 'Issues.Limit', kind: 'number', label: 'RequestsLabelIssueQuota', help: 'IssuesHelpLimit', min: 0, fallback: 10 }
                ] },
                { path: 'Issues.MayManage', kind: 'check', label: 'LabelMayManageIssues', help: 'IssuesHelpManage' }
            ]
        },
        {
            key: 'invites', title: 'TabInvites', icon: 'mail', fields: [
                { path: 'Invites.MayCreate', kind: 'check', label: 'LabelMayCreateInvites', reveals: [
                    { path: 'Invites.MaxInvites', kind: 'number', label: 'LabelMaxInvites', help: 'MaxInvitesHelp', min: 0 },
                    { path: 'Invites.AnyGroup', kind: 'check', label: 'LabelAnyGroup' },
                    { path: 'Invites.TargetGroupIds', kind: 'groups', label: 'LabelInviteGroups', help: 'InviteGroupsHelp' }
                ] }
            ]
        },
        {
            key: 'wireguard', title: 'TabWireGuard', icon: 'vpn_lock', fields: [
                { path: 'WireGuard.MayHavePeers', kind: 'check', label: 'LabelMayHavePeers', reveals: [
                    { path: 'WireGuard.MaxPeers', kind: 'number', label: 'LabelMaxPeers', help: 'MaxPeersHelp', min: 0 },
                    { path: 'WireGuard.DataLimit', kind: 'gigabytes', label: 'WireGuardLabelMemberDataLimit', help: 'WireGuardMemberDataLimitHelp', min: 0 },
                    { path: 'WireGuard.DataPeriod', kind: 'select', label: 'WireGuardLabelPeriod', names: ['Hour', 'Day', 'Week', 'Month', 'Year'], options: ['WireGuardPeriodHour', 'WireGuardPeriodDay', 'WireGuardPeriodWeek', 'WireGuardPeriodMonth', 'LabelYear'] }
                ] }
            ]
        },
        {
            key: 'scripts', title: 'TabScripts', icon: 'code', fields: [
                { path: 'Scripts.AllScripts', kind: 'check', label: 'LabelAllScripts', hides: [
                    { path: 'Scripts.ScriptIds', kind: 'records', label: 'ScriptsLabelGroupScripts', help: 'ScriptsGroupScriptsHelp', source: 'Scripts/Names' }
                ] }
            ]
        },
        {
            key: 'pages', title: 'TabPages', icon: 'web', fields: [
                { path: 'CustomPages.AllPages', kind: 'check', label: 'LabelAllPages', help: 'PagesAllPagesHelp' },
                { path: 'CustomPages.PageIds', kind: 'pages', label: 'PagesLabelGroupPages', help: 'PagesGroupPagesHelp', hiddenBy: 'CustomPages.AllPages' }
            ]
        },
        { key: 'notifications', title: 'TabNotifications', icon: 'notifications', fields: [
            { path: 'Notifications.AllEvents', kind: 'check', label: 'LabelAllEvents', help: 'NotificationsAllEventsHelp' },
            { path: 'Notifications.EventKeys', kind: 'events', label: 'NotificationsGroupEvents', help: 'NotificationsGroupEventsHelp' },
            { path: 'Notifications.BrowserNotifications', kind: 'check', label: 'LabelBrowserNotifications', help: 'NotificationsGroupBrowsersHelp', fallback: true },
            { path: 'Notifications.JellyfinMessages', kind: 'check', label: 'NotificationsChannelApps', help: 'NotificationsGroupAppsHelp', fallback: true }
        ] }
    ];
}

// accountSpecs
// Describes the Account rules subsections: password requirements, expiry, inactivity, and session management.
//
// Output
// [object[]]: Each subsection's title key and fields.
export function accountSpecs() {
    return [
        {
            title: 'HeaderPasswordRequirements', fields: [
                { path: 'Accounts.Password.Enabled', kind: 'check', label: 'LabelEnforcePasswordRequirements', help: 'EnforcePasswordRequirementsHelp', reveals: [
                    { path: 'Accounts.Password.DisallowEmpty', kind: 'check', label: 'LabelDisallowEmptyPasswords', help: 'DisallowEmptyPasswordsHelp' },
                    { path: 'Accounts.Password.MinLength', kind: 'number', label: 'LabelMinimumLength', min: 1, fallback: 8 },
                    { path: 'Accounts.Password.ChangeMode', kind: 'select', label: 'LabelPasswordChanges', help: 'PasswordChangesHelp', names: CHANGE_MODES, options: ['OptionAllowed', 'OptionInitialPasswordOnly', 'OptionDisallowed'] },
                    { path: 'Accounts.Password.RequireUppercase', kind: 'check', label: 'LabelRequireCapitalLetter' },
                    { path: 'Accounts.Password.RequireNumber', kind: 'check', label: 'LabelRequireNumber' },
                    { path: 'Accounts.Password.RequireSymbol', kind: 'check', label: 'LabelRequireSymbol' }
                ] }
            ]
        },
        {
            title: 'HeaderExpiry', fields: [
                { path: 'Accounts.Expiry.ExpiresOn', kind: 'expiry', label: 'LabelExpireMembersOnDate', help: 'ExpireMembersOnDateHelp', reveals: [
                    { path: 'Accounts.Expiry.ExpiresOn', kind: 'date', label: 'LabelExpiryDate', help: 'ExpiryDateHelp' },
                    { path: 'Accounts.Expiry.Action', kind: 'select', label: 'LabelExpirationMethod', help: 'ExpirationMethodHelp', names: EXPIRY_ACTIONS, options: ['OptionDisableAccount', 'OptionDeleteAccount'] }
                ] }
            ]
        },
        {
            title: 'HeaderInactivity', fields: [
                { path: 'Accounts.Inactivity.Enabled', kind: 'check', label: 'LabelDisableInactiveUsers', help: 'DisableInactiveUsersHelp', reveals: [
                    { path: 'Accounts.Inactivity.Days', kind: 'number', label: 'LabelInactiveDays', help: 'InactiveDaysHelp', min: 1, fallback: 30 }
                ] }
            ]
        },
        {
            title: 'HeaderSessionManagement', fields: [
                { path: 'Accounts.Sessions.Enabled', kind: 'check', label: 'LabelCleanUpOldSessions', help: 'CleanUpOldSessionsHelp', reveals: [
                    { path: 'Accounts.Sessions.Rules', kind: 'sessions', help: 'SessionRulesHelp' }
                ] }
            ]
        }
    ];
}

// knownClients
// Lists the clients with a device on the server and the clients the rules name, so a saved client stays listed.
//
// Inputs
// devices [object[]]: The server's devices.
// rules [object[]]: The session rules.
//
// Output
// [string[]]: The client names, sorted, each once whatever its case.
export function knownClients(devices, rules) {
    var seen = {};
    var out = [];
    var add = function (name) {
        if (name && !seen[name.toLowerCase()]) { seen[name.toLowerCase()] = true; out.push(name); }
    };
    (devices || []).forEach(function (d) { add(d.AppName); });
    (rules || []).forEach(function (r) { (r.Clients || []).forEach(add); });
    return out.sort(function (a, b) { return a.localeCompare(b); });
}

// overlappingClients
// Names the clients more than one rule covers, where the shortest window applies.
//
// Inputs
// clients [string[]]: The known clients.
// rules [object[]]: The rules, as the editor reads them.
//
// Output
// [string[]]: The clients covered twice or more.
export function overlappingClients(clients, rules) {
    return clients.filter(function (client) {
        return (rules || []).filter(function (r) {
            if (r.Mode === 'All') return true;
            var listed = (r.Clients || []).some(function (c) { return c.toLowerCase() === client.toLowerCase(); });
            return r.Mode === 'Only' ? listed : !listed;
        }).length > 1;
    });
}

// createGroupEditor
// Builds the body of a group's sidecar: its name, its members, Account rules, and a section per tool.
//
// Inputs
// group [object]: The group to edit, or a new one.
// ctx [object]: shared is the Necto helper bag, users the server's users, groups every group, devices the
//   server's devices, for the session rules' clients, profiles the Radarr and Sonarr profiles, read when left out,
//   and config the plugin configuration, whose tools that are off get no section. [optional]
//
// Output
// [object]: A handle with element, getValue, validate, setStatus, ready, and destroy. getValue returns the group
//   with the editor's values, and validate the key of the first problem, or null.
export function createGroupEditor(group, ctx) {
    var shared = ctx.shared;
    var t = shared.t;
    var draft = JSON.parse(JSON.stringify(group || {}));
    draft.Permissions = draft.Permissions || {};
    var everyone = isEveryone(draft);
    var handles = [];
    var readers = [];
    var expiryBox = null;
    var ready = Promise.resolve();
    var waits = [];
    var profileList = null;
    var status = h('div', { class: 'stolon-status-text', role: 'status', 'data-role': 'status' });
    var root = h('div', { 'data-role': 'group-editor' }, [status]);

    // nextId
    // Makes an id for one of the editor's fields.
    function nextId(name) {
        fieldSeq += 1;
        return 'necto-group-' + name + '-' + fieldSeq;
    }

    // section
    // Adds a collapsible section to the editor, closed unless opts says otherwise.
    function section(title, icon, content, opts) {
        var made = nectoModule.createCollapsibleSection(Object.assign({ title: title, icon: icon, collapsed: true, content: content }, opts || {}));
        root.appendChild(made.element);
        handles.push(made);
        return made;
    }

    if (!everyone) {
        var nameId = nextId('name');
        var nameInput = h('input', { is: 'emby-input', type: 'text', id: nameId, 'data-field': 'Name', required: true });
        nameInput.value = draft.Name || '';
        root.appendChild(h('div', { class: 'inputContainer' }, [
            h('label', { class: 'inputLabel inputLabelUnfocused', for: nameId, text: t.tr('LabelName') }),
            nameInput
        ]));
        readers.push(function (out) { out.Name = nameInput.value.trim(); });
    }

    // Members
    var membersContent = h('div', {}, [h('div', { class: 'fieldDescription', text: t.tr(everyone ? 'HelpEveryoneMembers' : 'HelpMembers') })]);
    section(t.tr('HeaderMembers'), 'group', membersContent, { collapsed: false }).element.setAttribute('data-section', 'members');
    if (!everyone) {
        var members = shared.createUserMultiSelector({
            adminFilter: 'exclude',
            search: true,
            value: draft.MemberIds || [],
            fetchUsers: function () { return Promise.resolve(ctx.users || []); }
        });
        membersContent.appendChild(members.element);
        ready = members.ready;
        handles.push(members);
        // The selector gives ids without dashes, so each is mapped back to the id the server sent.
        readers.push(function (out) {
            out.MemberIds = members.getValue().map(function (id) {
                var user = (ctx.users || []).find(function (u) { return sameId(u.Id, id); });
                return user ? user.Id : id;
            });
        });
    }

    // describe
    // Builds a field's description, or nothing when it has none.
    function describe(spec, className) {
        return spec.help ? h('div', { class: className || 'fieldDescription', text: t.tr(spec.help) }) : null;
    }

    // drawField
    // Draws one field of a section, and the fields it reveals under it while it is on.
    function drawField(spec, into) {
        var id = nextId('field');
        var value = getPath(draft.Permissions, spec.path);
        var input = null;

        if (spec.kind === 'check' || spec.kind === 'expiry') {
            input = h('input', { type: 'checkbox', is: 'emby-checkbox', id: id, 'data-field': spec.path });
            input.checked = value == null ? !!spec.fallback : !!value;
            into.appendChild(h('div', { class: 'checkboxContainer' + (spec.help ? ' checkboxContainer-withDescription' : '') }, [
                h('label', {}, [input, h('span', { text: t.tr(spec.label) })]),
                describe(spec, 'fieldDescription checkboxFieldDescription')
            ]));
            if (spec.kind === 'check') readers.push(function (out) { setPath(out.Permissions, spec.path, !!input.checked); });
            else expiryBox = input;
        } else if (spec.kind === 'number' || spec.kind === 'date') {
            input = h('input', { is: 'emby-input', type: spec.kind, id: id, 'data-field': spec.path, min: spec.kind === 'number' ? String(spec.min || 0) : null });
            into.appendChild(h('div', { class: 'inputContainer' }, [
                h('label', { class: 'inputLabel inputLabelUnfocused', for: id, text: t.tr(spec.label) }),
                input,
                describe(spec)
            ]));
            if (spec.kind === 'number') {
                input.value = value == null ? String(spec.fallback || 0) : String(value);
                readers.push(function (out) {
                    var n = parseInt(input.value, 10);
                    setPath(out.Permissions, spec.path, isNaN(n) || n < (spec.min || 0) ? (spec.fallback || 0) : n);
                });
            } else {
                input.value = value ? String(value).slice(0, 10) : '';
                readers.push(function (out) {
                    setPath(out.Permissions, spec.path, expiryBox && expiryBox.checked && input.value ? input.value + 'T00:00:00' : null);
                });
            }
        } else if (spec.kind === 'select') {
            input = h('select', { is: 'emby-select', id: id, 'data-field': spec.path });
            spec.names.forEach(function (name, i) { input.appendChild(h('option', { value: name, text: t.tr(spec.options[i]) })); });
            input.value = enumName(value, spec.names);
            into.appendChild(h('div', { class: 'selectContainer' }, [
                h('label', { class: 'selectLabel', for: id, text: t.tr(spec.label) }),
                input,
                describe(spec)
            ]));
            readers.push(function (out) { setPath(out.Permissions, spec.path, enumName(input.value, spec.names)); });
        } else if (spec.kind === 'groups') {
            var list = nectoModule.createCheckboxList({
                items: (ctx.groups || []).filter(function (g) { return !sameId(g.Id, draft.Id); }).map(function (g) { return { Id: g.Id, Name: groupName(g, t) }; }),
                idKey: 'Id',
                value: value || [],
                renderRow: function (g) { return { label: g.Name }; }
            });
            into.appendChild(h('div', { 'data-field': spec.path }, [h('div', { class: 'selectLabel', text: t.tr(spec.label) }), list.element, describe(spec)]));
            handles.push(list);
            readers.push(function (out) { setPath(out.Permissions, spec.path, list.getValue()); });
        } else if (spec.kind === 'gigabytes') {
            input = h('input', { is: 'emby-input', type: 'number', id: id, 'data-field': spec.path, min: '0', step: 'any' });
            input.value = value ? String(Math.round((value / 1073741824) * 100) / 100) : '0';
            into.appendChild(h('div', { class: 'inputContainer' }, [
                h('label', { class: 'inputLabel inputLabelUnfocused', for: id, text: t.tr(spec.label) }),
                input,
                describe(spec)
            ]));
            readers.push(function (out) {
                var gb = parseFloat(input.value);
                setPath(out.Permissions, spec.path, isNaN(gb) || gb <= 0 ? 0 : Math.round(gb * 1073741824));
            });
        } else if (spec.kind === 'records') {
            var records = nectoModule.createCheckboxList({
                fetchItems: function () { return shared.request(spec.source).catch(function () { return []; }); },
                idKey: 'Id',
                matchKey: function (v) { return String(v || '').replace(/-/g, '').toLowerCase(); },
                value: (value || []).map(function (v) { return String(v).replace(/-/g, '').toLowerCase(); }),
                renderRow: function (r) { return { label: r.Name }; }
            });
            into.appendChild(h('div', { 'data-field': spec.path }, [h('div', { class: 'selectLabel', text: t.tr(spec.label) }), records.element, describe(spec)]));
            handles.push(records);
            readers.push(function (out) { setPath(out.Permissions, spec.path, records.getValue()); });
        } else if (spec.kind === 'profiles') {
            drawProfiles(into, value || [], spec);
        } else if (spec.kind === 'sessions') {
            drawSessions(into, value || [], spec);
        } else if (spec.kind === 'events') {
            var events = nectoModule.createCheckboxList({
                items: ctx.events || [],
                idKey: 'Key',
                value: value || [],
                renderRow: function (e) { return { label: e.Name, sublabel: e.Key }; },
                emptyMessage: t.tr('NotificationsNoUserEvents')
            });
            into.appendChild(h('div', { 'data-field': spec.path }, [h('div', { class: 'selectLabel', text: t.tr(spec.label) }), events.element, describe(spec)]));
            handles.push(events);
            readers.push(function (out) { setPath(out.Permissions, spec.path, events.getValue()); });
        } else if (spec.kind === 'pages') {
            var pages = nectoModule.createCheckboxList({
                fetchItems: function () { return shared.request('Pages').then(function (all) { return grantablePages(all, value); }).catch(function () { return []; }); },
                idKey: 'Id',
                value: value || [],
                searchKeys: ['Title', 'Slug'],
                emptyMessage: t.tr('PagesEmptyGroupPages'),
                renderRow: function (p) { return { label: p.Title || ('/pages/' + p.Slug), sublabel: '/pages/' + p.Slug }; }
            });
            var pagesRow = h('div', { 'data-field': spec.path }, [h('div', { class: 'selectLabel', text: t.tr(spec.label) }), pages.element, describe(spec)]);
            into.appendChild(pagesRow);
            handles.push(pages);
            ready = Promise.all([ready, pages.ready]);
            readers.push(function (out) { setPath(out.Permissions, spec.path, pages.getValue()); });
            var every = into.querySelector('[data-field="' + spec.hiddenBy + '"]');
            if (every) {
                var hide = function () { pagesRow.classList.toggle('stolon-hidden', every.checked); };
                every.addEventListener('change', hide);
                hide();
            }
        }

        // Fields a checkbox hides while it is on, such as the scripts a group runs while it runs every script.
        if (spec.hides) {
            var rest = h('div', { class: 'stolon-indent' });
            spec.hides.forEach(function (child) { drawField(child, rest); });
            into.appendChild(rest);
            var hideRest = function () { rest.classList.toggle('stolon-hidden', input.checked); };
            input.addEventListener('change', hideRest);
            hideRest();
        }

        if (spec.reveals) {
            var indent = h('div', { class: 'stolon-indent' });
            spec.reveals.forEach(function (child) { drawField(child, indent); });
            into.appendChild(indent);
            var reflect = function () { indent.classList.toggle('stolon-hidden', !input.checked); };
            input.addEventListener('change', reflect);
            reflect();
        }
    }

    // drawProfiles
    // Draws the Radarr or Sonarr profiles of one type as a checkbox list, where none picked is every profile.
    function drawProfiles(into, value, spec) {
        var list = nectoModule.createCheckboxList({
            fetchItems: function () {
                return profiles().then(function (all) { return all.filter(function (p) { return p.Type === spec.type; }); });
            },
            idKey: 'Id',
            value: value,
            search: false,
            emptyMessage: t.tr('RequestsEmptyProfiles'),
            renderRow: function (p) { return { label: p.Name, sublabel: p.Server }; }
        });
        into.appendChild(h('div', { 'data-field': spec.path }, [h('div', { class: 'selectLabel', text: t.tr(spec.label) }), list.element, describe(spec)]));
        handles.push(list);
        waits.push(list.ready);
        readers.push(function (out) { setPath(out.Permissions, spec.path, list.getValue()); });
    }

    // profiles
    // Reads the Radarr and Sonarr profiles once for the editor, or takes the ones the page read.
    //
    // Output
    // [Promise]: Resolves with every profile, or none when they could not be read.
    function profiles() {
        if (!profileList) {
            profileList = Promise.resolve(ctx.profiles || shared.request('Requests/Profiles')).then(function (all) {
                return Array.isArray(all) ? all : [];
            }, function () { return []; });
        }
        return profileList;
    }

    // drawSessions
    // Draws the session rules as a Repeater of cards, each with its clients, and the overlap warning under it.
    function drawSessions(into, rules, spec) {
        var clients = knownClients(ctx.devices, rules);
        var warn = h('div', { class: 'stolon-warn stolon-hidden', 'data-role': 'overlap' });
        var clientField = function (value, change) {
            var chips = nectoModule.createChipSelect({ options: clients, value: value || [], placeholder: t.tr('SearchClients'), noResultsText: t.tr('EmptyClientsMatch'), onChange: change });
            return {
                element: chips.element,
                input: chips.element.querySelector('input'),
                getValue: function () { return chips.getValue().map(String); },
                setValue: function (next) { chips.setValue(next || []); },
                destroy: chips.destroy
            };
        };
        var read = null;
        var updateOverlap = function () {
            if (!read) return;
            var overlapped = overlappingClients(clients, read());
            warn.textContent = overlapped.length ? t.format('SessionOverlapWarning', overlapped.join(', ')) : '';
            warn.classList.toggle('stolon-hidden', overlapped.length === 0);
        };
        var repeater = nectoModule.createRepeater({
            layout: 'card',
            titleText: t.tr('RuleTitle'),
            addText: t.tr('ButtonAddRule'),
            emptyText: t.tr('EmptyRules'),
            fields: [
                { key: 'mode', label: t.tr('LabelAppliesTo'), type: 'select', options: [
                    { value: 'All', label: t.tr('OptionAllClients') },
                    { value: 'Only', label: t.tr('OptionOnlyTheseClients') },
                    { value: 'AllExcept', label: t.tr('OptionAllExceptTheseClients') }
                ] },
                { key: 'clients', label: t.tr('LabelClients'), type: 'custom', render: clientField },
                { key: 'days', label: t.tr('LabelDaysBeforeLogout'), type: 'number', min: 1, placeholder: '30' }
            ],
            value: rules.map(function (r) {
                return { mode: enumName(r.Mode, SESSION_MODES), clients: (r.Clients || []).slice(), days: r.Days > 0 ? r.Days : 30 };
            }),
            onChange: updateOverlap
        });
        read = function () {
            return repeater.getValue().map(function (item) {
                var mode = enumName(item.mode, SESSION_MODES);
                var days = parseInt(item.days, 10);
                return { Mode: mode, Clients: mode === 'All' ? [] : (item.clients || []).map(String), Days: isNaN(days) || days < 1 ? 30 : days };
            });
        };
        into.appendChild(h('div', { 'data-field': spec.path }, [describe(spec), repeater.element, warn]));
        updateOverlap();
        handles.push(repeater);
        readers.push(function (out) { setPath(out.Permissions, spec.path, read()); });
    }

    // Account rules
    var accountsContent = h('div', {}, [h('div', { class: 'fieldDescription', text: t.tr('HelpAccountRulesMerge') })]);
    accountSpecs().forEach(function (sub) {
        accountsContent.appendChild(h('h3', { class: 'stolon-subsection-title', text: t.tr(sub.title) }));
        sub.fields.forEach(function (spec) { drawField(spec, accountsContent); });
    });
    section(t.tr('HeaderAccountRules'), 'manage_accounts', accountsContent).element.setAttribute('data-section', 'accounts');

    // Templates
    var templatesDrawn = nectoTemplates.drawTemplateSections(section, draft, ctx, readers, handles);
    ready = Promise.all([ready, templatesDrawn]);

    // One section per tool, without the tools that are off. Their permissions stay as they were.
    sectionSpecs().filter(function (spec) { return !ctx.config || nectoModule.toolIsOn(ctx.config, spec.key); }).forEach(function (spec) {
        var content = h('div', {}, [
            h('div', { class: 'fieldDescription', text: t.tr('HelpPermissionMerge') })
        ]);
        spec.fields.forEach(function (field) { drawField(field, content); });
        section(t.tr(spec.title), spec.icon, content).element.setAttribute('data-section', spec.key);
    });
    ready = Promise.all([ready].concat(waits)).then(function () {});

    return {
        element: root,
        ready: ready,
        getValue: function () {
            var out = JSON.parse(JSON.stringify(draft));
            readers.forEach(function (read) { read(out); });
            if (everyone) out.MemberIds = [];
            return out;
        },
        validate: function () {
            var value = this.getValue();
            if (!everyone && !value.Name) return 'ErrorGroupNameRequired';
            if (expiryBox && expiryBox.checked && !getPath(value.Permissions, 'Accounts.Expiry.ExpiresOn')) return 'ErrorSelectExpiration';
            return null;
        },
        setStatus: function (message, isError) {
            shared.setStatus(status, message, !!isError);
        },
        destroy: function () {
            handles.splice(0).forEach(function (h) { if (h && h.destroy) h.destroy(); });
        }
    };
}

// groupOrder
// Lists the ids of groups in their order, for Groups/Order.
//
// Inputs
// groups [object[]]: The groups, without Everyone, in the new order.
//
// Output
// [string[]]: Their ids.
export function groupOrder(groups) {
    return (groups || []).filter(function (g) { return !isEveryone(g); }).map(function (g) { return g.Id; });
}

export default function (view) {
    'use strict';

    var shared = null;
    var page = null;
    var list = null;
    var groups = [];
    var users = [];
    var devices = [];
    var events = [];
    var profiles = [];
    var config = null;
    var loadSeq = 0;

    // report
    // Shows a result on the page's status line.
    function report(message, isError) {
        shared.setStatus('groupsStatus', message, !!isError, { timeout: 10000 });
    }

    // loadGroups
    // Reads the groups, the users, the devices, and the Radarr and Sonarr profiles, then draws the list. An answer that lands after a newer load is dropped.
    function loadGroups() {
        var mine = ++loadSeq;
        return Promise.all([
            shared.request('Groups'),
            shared.request('Groups/Users'),
            shared.request('Devices').catch(function () { return { Items: [] }; }),
            shared.request('Notifications/Events').catch(function () { return []; }),
            shared.request('Requests/Profiles').catch(function () { return []; }),
            shared.getConfig().catch(function () { return null; })
        ]).then(function (answers) {
            if (mine !== loadSeq) return;
            groups = answers[0] || [];
            users = answers[1] || [];
            devices = (answers[2] && answers[2].Items) || [];
            events = Array.isArray(answers[3]) ? answers[3] : [];
            profiles = Array.isArray(answers[4]) ? answers[4] : [];
            config = answers[5];
            shared.renderCards('groupUserCards', userCards(userCounts(users, new Date()), shared.t));
            var items = groupItems(groups, users, shared);
            if (list) list.setItems(items);
            else {
                list = nectoModule.createRecordList({ items: items, label: shared.t.tr('TabGroups'), onOpen: function (item) { openGroup(item.group); } });
                view.querySelector('#groupList').appendChild(list.element);
            }
        }).catch(function () {
            if (mine === loadSeq) report(shared.t.tr('ErrorLoadGroups'), true);
        });
    }

    // confirmDeleteOnExpiry
    // Asks first when the group deletes its members on expiry, since that cannot be undone.
    //
    // Output
    // [Promise]: Resolves true to go on with the save.
    function confirmDeleteOnExpiry(value) {
        if (!deletesOnExpiry(value)) return Promise.resolve(true);
        var t = shared.t;
        return nectoModule.confirmDialog(t.format('GroupsConfirmSaveDeleteOnExpiry', String(value.Permissions.Accounts.Expiry.ExpiresOn).slice(0, 10)), {
            title: t.format('GroupsSaveName', value.Name || t.tr('TitleNewGroup')),
            confirmText: t.tr('Save'),
            destructive: true
        });
    }

    // saveGroup
    // Saves the editor's group, a new one by POST and an existing one by PUT. A group that deletes its members on
    // expiry asks first.
    //
    // Output
    // [Promise]: Resolves true once saved, and false to keep the sidecar open.
    function saveGroup(editor, isNew) {
        var problem = editor.validate();
        if (problem) {
            editor.setStatus(shared.t.tr(problem), true);
            return Promise.resolve(false);
        }
        var value = editor.getValue();
        return confirmDeleteOnExpiry(value).then(function (confirmed) {
            return confirmed ? sendGroup(editor, isNew, value) : false;
        });
    }

    // sendGroup
    // Sends the group to its route and reloads the list.
    //
    // Output
    // [Promise]: Resolves true once saved, and false to keep the sidecar open.
    function sendGroup(editor, isNew, value) {
        var request = isNew ? shared.request('Groups', 'POST', value) : shared.request('Groups/' + value.Id, 'PUT', value);
        return request.then(function () {
            report(shared.t.tr('ToastGroupSaved'), false);
            loadGroups();
            return true;
        }).catch(function (err) {
            return shared.errorText(err, shared.t.tr('ErrorSaveGroup')).then(function (text) {
                editor.setStatus(text, true);
                return false;
            });
        });
    }

    // deleteGroup
    // Asks first, then deletes the group and closes its sidecar.
    function deleteGroup(group, close) {
        var t = shared.t;
        nectoModule.confirmDialog(t.tr('ConfirmDeleteGroup'), {
            title: t.format('DeleteName', group.Name),
            confirmText: t.tr('Delete'),
            destructive: true
        }).then(function (confirmed) {
            if (!confirmed) return;
            shared.request('Groups/' + group.Id, 'DELETE').then(function () {
                close('deleted');
                report(t.tr('ToastGroupDeleted'), false);
                loadGroups();
            }).catch(function (err) {
                shared.errorText(err, t.tr('ErrorDeleteGroup')).then(function (text) { report(text, true); });
            });
        });
    }

    // openGroup
    // Opens a group, or a new one, in the editor sidecar.
    //
    // Inputs
    // group [object]: The group, or null for a new one.
    function openGroup(group) {
        var t = shared.t;
        var isNew = !group;
        var editor = createGroupEditor(group || newGroup(), { shared: shared, users: users, groups: groups, devices: devices, events: events, profiles: profiles, config: config });
        nectoModule.openEditorSidecar({
            title: isNew ? t.tr('TitleNewGroup') : groupName(group, t),
            body: editor.element,
            page: page,
            isNew: isNew || isEveryone(group),
            ready: editor.ready,
            serialize: function () { return JSON.stringify(editor.getValue()); },
            saveText: t.tr('Save'),
            deleteText: t.tr('Delete'),
            onSave: function () { return saveGroup(editor, isNew); },
            onDelete: function (close) { deleteGroup(group, close); },
            onClose: function () { editor.destroy(); }
        });
    }

    // openOrder
    // Opens the group order in a sidecar, where the first group with a template on gives a member theirs.
    function openOrder() {
        var t = shared.t;
        var ordered = groups.filter(function (g) { return !isEveryone(g); });
        var list = nectoModule.createOrderedList({
            items: ordered,
            render: function (g) { return h('span', { text: g.Name }); },
            label: function (g) { return g.Name; }
        });
        var status = h('div', { class: 'stolon-status-text', role: 'status' });
        var body = h('div', {}, [h('div', { class: 'fieldDescription', text: t.tr('GroupsOrderHelp') }), status, list.element]);
        nectoModule.openEditorSidecar({
            title: t.tr('GroupsButtonOrder'),
            body: body,
            page: page,
            isNew: true,
            size: 'medium',
            serialize: function () { return JSON.stringify(groupOrder(list.getItems())); },
            saveText: t.tr('Save'),
            onSave: function () {
                return shared.request('Groups/Order', 'PUT', groupOrder(list.getItems())).then(function () {
                    report(t.tr('GroupsOrderSaved'), false);
                    loadGroups();
                    return true;
                }).catch(function (err) {
                    return shared.errorText(err, t.tr('GroupsErrorSaveOrder')).then(function (text) {
                        shared.setStatus(status, text, true);
                        return false;
                    });
                });
            },
            onClose: function () { list.destroy(); }
        });
    }

    var made = nectoModule.createNectoPage(view, {
        page: 'necto_groups',
        bind: function () {
            view.querySelector('#btnAddGroup').addEventListener('click', function () { openGroup(null); });
            var order = view.querySelector('#btnOrderGroups');
            if (order) order.addEventListener('click', openOrder);
        },
        load: function () { loadGroups(); }
    });
    shared = made.shared;
    page = made.page;
}

// newGroup
// Starts a new group. Its members hear what happens to their own requests and issue reports, as Everyone's do on a
// fresh install, and it gives nothing else until it is set.
//
// Output
// [object]: The group the editor opens with.
export function newGroup() {
    return {
        Name: '',
        MemberIds: [],
        Permissions: { Notifications: { AllEvents: false, EventKeys: ['requests.approved', 'requests.declined', 'requests.available', 'issues.updated'] } }
    };
}
