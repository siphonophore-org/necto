// necto_groups.js
// The Groups tool. It lists the groups, Everyone first, and opens each in an editor sidecar with its name, its
// members, its account rules, its access and preferences, and a collapsible section per tool, with Remove
// and Save on the shelf.

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
    if (a.Expiry && a.Expiry.ExpiresOn) tags.push({ label: t.tr('HeaderExpiry'), icon: 'event_busy' });
    if (a.Inactivity && a.Inactivity.Enabled) tags.push({ label: t.tr('HeaderInactivity'), icon: 'bedtime' });
    if (a.Sessions && a.Sessions.Enabled) tags.push({ label: t.tr('HeaderSessionManagement'), icon: 'logout' });
    if (a.Resets && a.Resets.ShowCodes) tags.push({ label: t.tr('HeaderPasswordResets'), icon: 'lock_reset' });
    return tags;
}

// groupCounts
// Counts the groups and their users for the cards over the list: the groups besides Everyone, the users in one, the
// users only Everyone holds, and those whose groups expire them within 7 days. Administrators are in no group.
//
// Inputs
// groups [object[]]: The groups from Groups, Everyone among them.
// users [object[]]: The users from Groups/Users.
// now [Date]: The time to count at.
//
// Output
// [object]: { groups, members, loose, expiring }.
export function groupCounts(groups, users, now) {
    var counts = { groups: (groups || []).filter(function (g) { return !isEveryone(g); }).length, members: 0, loose: 0, expiring: 0 };
    var today = new Date(now.getTime());
    today.setHours(0, 0, 0, 0);
    var soon = new Date(today.getTime());
    soon.setDate(soon.getDate() + 7);
    (users || []).forEach(function (u) {
        if (u.IsAdministrator) return;
        if ((u.GroupIds || []).some(function (id) { return !sameId(id, EVERYONE_ID); })) counts.members++;
        else counts.loose++;
        if (u.ExpiresOn) {
            var due = new Date(String(u.ExpiresOn).slice(0, 10) + 'T00:00:00');
            if (due >= today && due <= soon) counts.expiring++;
        }
    });
    return counts;
}

// groupCards
// Turns the counts into the cards over the list.
//
// Inputs
// counts [object]: What groupCounts gives.
// t [object]: The strings.
//
// Output
// [object[]]: The cards.
export function groupCards(counts, t) {
    return [
        { count: counts.groups, label: t.tr('TabGroups'), color: 'blue' },
        { count: counts.members, label: t.tr('HeaderMembers'), color: 'green' },
        { count: counts.loose, label: t.tr('ScriptsSummaryNoGroups'), color: 'gray' },
        { count: counts.expiring, label: t.tr('GroupsCardExpiring'), color: 'orange' }
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
// value while the group has none. grants names the paths that give the tool to members, which color the tile.
//
// Output
// [object[]]: Each section's tool key, title key, icon, grants, and fields.
export function sectionSpecs() {
    return [
        {
            key: 'requests', title: 'TabRequests', icon: 'movie_filter', grants: ['Requests.MayRequest', 'Requests.MayManage'], fields: [
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
            key: 'issues', title: 'TabIssues', icon: 'report_problem', grants: ['Issues.MayReport', 'Issues.MayManage'], fields: [
                { path: 'Issues.MayReport', kind: 'check', label: 'IssuesLabelMayReport', reveals: [
                    { path: 'Issues.Limit', kind: 'number', label: 'RequestsLabelIssueQuota', help: 'IssuesHelpLimit', min: 0, fallback: 10 }
                ] },
                { path: 'Issues.MayManage', kind: 'check', label: 'LabelMayManageIssues', help: 'IssuesHelpManage' }
            ]
        },
        {
            key: 'invites', title: 'TabInvites', icon: 'mail', grants: ['Invites.MayCreate'], fields: [
                { path: 'Invites.MayCreate', kind: 'check', label: 'LabelMayCreateInvites', reveals: [
                    { path: 'Invites.MaxInvites', kind: 'number', label: 'LabelMaxInvites', help: 'MaxInvitesHelp', min: 0 },
                    { path: 'Invites.AnyGroup', kind: 'scope', label: 'LabelInviteGroups', allText: 'GroupsInvitesPermissiveHelp', someText: 'GroupsInvitesRestrictiveHelp', picks: [
                        { path: 'Invites.TargetGroupIds', kind: 'groups', label: 'LabelInviteGroups' }
                    ] }
                ] }
            ]
        },
        {
            key: 'wireguard', title: 'TabWireGuard', icon: 'vpn_lock', grants: ['WireGuard.MayHavePeers'], fields: [
                { path: 'WireGuard.MayHavePeers', kind: 'check', label: 'LabelMayHavePeers', reveals: [
                    { path: 'WireGuard.MaxPeers', kind: 'number', label: 'LabelMaxPeers', help: 'MaxPeersHelp', min: 0 },
                    { path: 'WireGuard.DataLimit', kind: 'gigabytes', label: 'WireGuardLabelMemberDataLimit', help: 'WireGuardMemberDataLimitHelp', min: 0 },
                    { path: 'WireGuard.DataPeriod', kind: 'select', label: 'WireGuardLabelPeriod', names: ['Hour', 'Day', 'Week', 'Month', 'Year'], options: ['WireGuardPeriodHour', 'WireGuardPeriodDay', 'WireGuardPeriodWeek', 'WireGuardPeriodMonth', 'LabelYear'] }
                ] }
            ]
        },
        {
            key: 'pages', title: 'TabPages', icon: 'web', grants: ['CustomPages.AllPages', 'CustomPages.PageIds'], fields: [
                { path: 'CustomPages.AllPages', kind: 'scope', label: 'PagesLabelGroupPages', allText: 'GroupsPagesPermissiveHelp', someText: 'GroupsPagesRestrictiveHelp', picks: [
                    { path: 'CustomPages.PageIds', kind: 'pages', label: 'PagesLabelGroupPages' }
                ] }
            ]
        },
        { key: 'notifications', title: 'TabNotifications', icon: 'notifications', grants: ['Notifications.BrowserEventKeys', 'Notifications.MessageEventKeys'], fields: [
            { path: 'Notifications', kind: 'delivery', help: 'NotificationsGroupDeliveryHelp' }
        ] }
    ];
}

// accountSpecs
// Describes the Account rules sections: password requirements, expiry, inactivity, session management, and password
// resets. The first field of each turns the rule on.
//
// Output
// [object[]]: Each section's key, icon, title key, and fields.
export function accountSpecs() {
    return [
        {
            key: 'password', icon: 'password', title: 'HeaderPasswordRequirements', fields: [
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
            key: 'expiry', icon: 'event_busy', title: 'HeaderExpiry', fields: [
                { path: 'Accounts.Expiry.ExpiresOn', kind: 'expiry', label: 'LabelExpireMembersOnDate', help: 'ExpireMembersOnDateHelp', reveals: [
                    { path: 'Accounts.Expiry.ExpiresOn', kind: 'date', label: 'LabelExpiryDate', help: 'ExpiryDateHelp' },
                    { path: 'Accounts.Expiry.Action', kind: 'select', label: 'LabelExpirationMethod', help: 'ExpirationMethodHelp', names: EXPIRY_ACTIONS, options: ['OptionDisableAccount', 'OptionDeleteAccount'] }
                ] }
            ]
        },
        {
            key: 'inactivity', icon: 'bedtime', title: 'HeaderInactivity', fields: [
                { path: 'Accounts.Inactivity.Enabled', kind: 'check', label: 'LabelDisableInactiveUsers', help: 'DisableInactiveUsersHelp', reveals: [
                    { path: 'Accounts.Inactivity.Days', kind: 'number', label: 'LabelInactiveDays', help: 'InactiveDaysHelp', min: 1, fallback: 30 }
                ] }
            ]
        },
        {
            key: 'sessions', icon: 'logout', title: 'HeaderSessionManagement', fields: [
                { path: 'Accounts.Sessions.Enabled', kind: 'check', label: 'LabelCleanUpOldSessions', help: 'CleanUpOldSessionsHelp', reveals: [
                    { path: 'Accounts.Sessions.Rules', kind: 'sessions' }
                ] }
            ]
        },
        {
            key: 'resets', icon: 'lock_reset', title: 'HeaderPasswordResets', fields: [
                { path: 'Accounts.Resets.ShowCodes', kind: 'check', label: 'LabelShowResetCodes', help: 'ShowResetCodesHelp' }
            ]
        }
    ];
}

// grantsAny
// Tells whether a group gives members anything in a tool, so its section's tile shows green.
//
// Inputs
// permissions [object]: The group's permissions.
// paths [string[]]: The paths that give the tool, each a switch or a list.
//
// Output
// [boolean]: True when any switch is on or any list holds an entry.
export function grantsAny(permissions, paths) {
    return (paths || []).some(function (path) {
        var value = getPath(permissions || {}, path);
        return Array.isArray(value) ? value.length > 0 : !!value;
    });
}

// eventGroups
// Sorts the events into one group per tool that is on, in sidebar order.
//
// Inputs
// events [object[]]: The events from Notifications/Events, each with Key, Name, and Tool.
// config [object]: The plugin configuration, or null to keep every tool.
//
// Output
// [object[]]: Each group's tool key, its name, and its events.
export function eventGroups(events, config) {
    return nectoModule.nectoTools().filter(function (tool) {
        return !config || nectoModule.toolIsOn(config, tool.key);
    }).map(function (tool) {
        return { key: tool.key, name: tool.name, events: (events || []).filter(function (e) { return e.Tool === tool.key; }) };
    }).filter(function (group) { return group.events.length > 0; });
}

// COMMON_CLIENTS
// The names Jellyfin's own and the most used clients sign in with, listed even before one has a device here.
var COMMON_CLIENTS = ['Jellyfin Web', 'Jellyfin Android', 'Jellyfin Android TV', 'Jellyfin iOS', 'Jellyfin Media Player', 'Jellyfin Roku', 'Swiftfin iOS', 'Swiftfin tvOS', 'Findroid', 'Finamp', 'Infuse', 'Kodi', 'Streamyfin'];

// knownClients
// Lists the clients with a device or a session on the server, the common clients, and the clients the rules name, so
// a saved client stays listed.
//
// Inputs
// devices [object[]]: The server's devices.
// rules [object[]]: The session rules.
// sessions [object[]]: The server's sessions, each with Client. [optional]
//
// Output
// [string[]]: The client names, sorted, each once whatever its case.
export function knownClients(devices, rules, sessions) {
    var seen = {};
    var out = [];
    var add = function (name) {
        if (name && !seen[name.toLowerCase()]) { seen[name.toLowerCase()] = true; out.push(name); }
    };
    (devices || []).forEach(function (d) { add(d.AppName); });
    (sessions || []).forEach(function (s) { add(s.Client); });
    COMMON_CLIENTS.forEach(add);
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

// deliveryLists
// Reads a group's events by delivery method. A stored group with no lists yet reads its events by the methods it had,
// as the server moves them.
//
// Inputs
// notifications [object]: The group's Notifications permissions.
// events [object[]]: The events from Notifications/Events, each with Key and ForUsers.
//
// Output
// [object]: { browser, message }, each a list of event keys.
export function deliveryLists(notifications, events) {
    var n = notifications || {};
    var forUsers = (events || []).filter(function (e) { return e.ForUsers; }).map(function (e) { return e.Key; });
    var stored = n.AllEvents ? forUsers : (n.EventKeys || []);
    var read = function (list, on) { return n.ByMethod === true ? (list || []).slice() : (on === false ? [] : stored.slice()); };
    return { browser: read(n.BrowserEventKeys, n.BrowserNotifications), message: read(n.MessageEventKeys, n.JellyfinMessages) };
}

// otherGroupName
// Names the group a user is in besides the one being edited, so the member list shows who would move here.
//
// Inputs
// user [object]: The user, with GroupIds from Groups/Users.
// group [object]: The group being edited.
// groups [object[]]: Every group.
// t [object]: The strings.
//
// Output
// [string]: The other group's name, or null when the user is in no other group.
export function otherGroupName(user, group, groups, t) {
    var other = (user.GroupIds || []).find(function (id) { return !sameId(id, EVERYONE_ID) && !sameId(id, group && group.Id); });
    var found = other ? (groups || []).find(function (g) { return sameId(g.Id, other); }) : null;
    return found ? groupName(found, t) : null;
}

// createGroupEditor
// Builds the body of a group's sidecar: its name, its members, Account rules, Access, Preferences,
// and a section per tool.
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
    var membersContent = h('div', { class: 'stolon-stack' }, [h('div', { class: 'fieldDescription', text: t.tr(everyone ? 'HelpEveryoneMembers' : 'HelpMembers') })]);
    section(t.tr('HeaderMembers'), 'group', membersContent, { collapsed: false }).element.setAttribute('data-section', 'members');
    if (!everyone) {
        var members = shared.createUserMultiSelector({
            adminFilter: 'exclude',
            search: true,
            noteFor: function (user) {
                var known = (ctx.users || []).find(function (u) { return sameId(u.Id, user.Id || user.id); });
                return known ? otherGroupName(known, draft, ctx.groups, t) : null;
            },
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
            into.appendChild(h('div', { 'data-field': spec.path }, [list.element, describe(spec)]));
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
        } else if (spec.kind === 'delivery') {
            drawDelivery(into, value || {}, spec);
        } else if (spec.kind === 'scope') {
            var scope = nectoModule.createChoiceGroup(null, {
                label: t.tr(spec.label),
                value: (value == null ? !!spec.fallback : !!value) ? 'all' : 'some',
                choices: [
                    { value: 'all', name: t.tr('GroupsScopePermissive'), text: t.tr(spec.allText) },
                    { value: 'some', name: t.tr('GroupsScopeRestrictive'), text: t.tr(spec.someText) }
                ]
            });
            scope.element.setAttribute('data-field', spec.path);
            into.appendChild(scope.element);
            handles.push(scope);
            readers.push(function (out) { setPath(out.Permissions, spec.path, scope.get() === 'all'); });
            var picks = h('div', { class: 'stolon-indent stolon-stack' });
            spec.picks.forEach(function (child) { drawField(child, picks); });
            into.appendChild(picks);
            var showPicks = function () { picks.classList.toggle('stolon-hidden', scope.get() === 'all'); };
            scope.element.addEventListener('change', showPicks);
            showPicks();
        } else if (spec.kind === 'pages') {
            var pages = nectoModule.createCheckboxList({
                fetchItems: function () { return shared.request('Pages').then(function (all) { return grantablePages(all, value); }).catch(function () { return []; }); },
                idKey: 'Id',
                value: value || [],
                searchKeys: ['Title', 'Slug'],
                emptyMessage: t.tr('PagesEmptyGroupPages'),
                renderRow: function (p) { return { label: p.Title || ('/pages/' + p.Slug), sublabel: '/pages/' + p.Slug }; }
            });
            var pagesRow = h('div', { 'data-field': spec.path }, [pages.element, describe(spec)]);
            into.appendChild(pagesRow);
            handles.push(pages);
            ready = Promise.all([ready, pages.ready]);
            readers.push(function (out) { setPath(out.Permissions, spec.path, pages.getValue()); });
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

    // drawDelivery
    // Draws the events members may get, under the tool that sends each, with a switch per delivery method. An event
    // with both off is off for members. Events of a tool that is off keep their picks.
    function drawDelivery(into, value, spec) {
        var groups = eventGroups((ctx.events || []).filter(function (e) { return e.ForUsers; }), ctx.config);
        var start = deliveryLists(value, ctx.events);
        var rows = [];
        groups.forEach(function (group) {
            group.events.forEach(function (e) { rows.push({ key: e.Key, label: e.Name, hint: group.name }); });
        });
        var cells = {};
        rows.forEach(function (row) {
            cells[row.key] = { browser: start.browser.indexOf(row.key) >= 0, message: start.message.indexOf(row.key) >= 0 };
        });
        var grid = nectoModule.createMatrixGrid({
            rows: rows,
            columns: [
                { key: 'browser', label: t.tr('NotificationsMethodBrowser'), icon: 'notifications_active', type: 'toggle' },
                { key: 'message', label: t.tr('LabelJellyfin'), icon: 'chat', type: 'toggle' }
            ],
            value: cells,
            rowHeader: t.tr('NotificationsGroupEvents'),
            label: t.tr('NotificationsGroupEvents'),
            emptyText: t.tr('NotificationsNoUserEvents'),
            onChange: function () { root.dispatchEvent(new Event('change')); }
        });
        handles.push(grid);
        into.appendChild(h('div', { class: 'stolon-stack', 'data-field': 'Notifications.Delivery' }, [describe(spec), grid.element]));
        var shown = rows.map(function (r) { return r.key; });
        readers.push(function (out) {
            var now = grid.getValue();
            var pick = function (list, column) {
                return list.filter(function (k) { return shown.indexOf(k) < 0; }).concat(shown.filter(function (k) { return now[k] && now[k][column] === true; }));
            };
            setPath(out.Permissions, 'Notifications.BrowserEventKeys', pick(start.browser, 'browser'));
            setPath(out.Permissions, 'Notifications.MessageEventKeys', pick(start.message, 'message'));
            setPath(out.Permissions, 'Notifications.ByMethod', true);
            setPath(out.Permissions, 'Notifications.AllEvents', false);
            setPath(out.Permissions, 'Notifications.EventKeys', []);
        });
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
        var clients = knownClients(ctx.devices, rules, ctx.sessions);
        var warn = h('div', { class: 'stolon-warn stolon-hidden', 'data-role': 'overlap' });
        // The clients seen on this server are listed to pick from, and any other name can be added under them.
        var clientField = function (value, change) {
            var picked = (value || []).map(String);
            var known = clients.concat(picked.filter(function (c) { return !clients.some(function (k) { return k.toLowerCase() === c.toLowerCase(); }); }));
            var list = nectoModule.createCheckboxList({
                items: known.map(function (c) { return { Id: c, Name: c }; }),
                idKey: 'Id',
                value: picked.filter(function (c) { return known.indexOf(c) >= 0; }),
                search: known.length > 8,
                emptyMessage: t.tr('GroupsNoClientsSeen'),
                renderRow: function (c) { return { label: c.Name }; },
                onChange: change
            });
            var other = nectoModule.createTagInput({ values: [], inputLabel: t.tr('GroupsLabelOtherClient'), placeholder: t.tr('GroupsPlaceholderOtherClient'), onChange: change });
            var element = h('div', { class: 'stolon-stack' }, [list.element, h('div', {}, [h('div', { class: 'selectLabel', text: t.tr('GroupsLabelOtherClient') }), other.element])]);
            return {
                element: element,
                input: other.element.querySelector('input'),
                getValue: function () {
                    var all = list.getValue().map(String);
                    other.getValue().forEach(function (c) { if (all.indexOf(c) < 0) all.push(c); });
                    return all;
                },
                setValue: function (next) { list.setValue((next || []).filter(function (c) { return known.indexOf(c) >= 0; })); other.setValue((next || []).filter(function (c) { return known.indexOf(c) < 0; })); },
                destroy: function () { list.destroy(); other.destroy(); }
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

    // Account rules, one section per rule, each tile following the switch that turns the rule on.
    var accountsContent = h('div', { class: 'stolon-stack' }, [h('div', { class: 'fieldDescription', text: t.tr('HelpAccountRulesMerge') })]);
    var accounts = section(t.tr('HeaderAccountRules'), 'manage_accounts', accountsContent);
    accounts.element.setAttribute('data-section', 'accounts');
    var ruleSwitches = accountSpecs().map(function (sub) {
        var content = h('div');
        sub.fields.forEach(function (spec) { drawField(spec, content); });
        var rule = nectoModule.createCollapsibleSection({ title: t.tr(sub.title), icon: sub.icon, collapsed: true, content: content });
        rule.element.setAttribute('data-section', 'accounts-' + sub.key);
        accountsContent.appendChild(rule.element);
        handles.push(rule);
        var lead = content.querySelector('[data-field="' + sub.fields[0].path + '"]');
        handles.push(nectoModule.bindTileToSwitch(rule.element, lead));
        return lead;
    });
    var paintAccounts = function () {
        accounts.setIconColor(ruleSwitches.some(function (box) { return box.checked; }) ? 'green' : 'gray');
    };
    accountsContent.addEventListener('change', paintAccounts);
    paintAccounts();

    // Templates
    var templatesDrawn = nectoTemplates.drawTemplateSections(section, draft, ctx, readers, handles);
    ready = Promise.all([ready, templatesDrawn]);

    // One section per tool, without the tools that are off. Their permissions stay as they were. Each tile shows
    // green while the group gives members anything in the tool.
    var tiles = [];
    sectionSpecs().filter(function (spec) { return !ctx.config || nectoModule.toolIsOn(ctx.config, spec.key); }).forEach(function (spec) {
        var content = h('div');
        spec.fields.forEach(function (field) { drawField(field, content); });
        var made = section(t.tr(spec.title), spec.icon, content);
        made.element.setAttribute('data-section', spec.key);
        tiles.push({ section: made, grants: spec.grants });
    });
    var paintTiles = function () {
        var permissions = current().Permissions;
        tiles.forEach(function (tile) { tile.section.setIconColor(grantsAny(permissions, tile.grants) ? 'green' : 'gray'); });
    };
    root.addEventListener('change', paintTiles);
    paintTiles();
    ready = Promise.all([ready].concat(waits)).then(paintTiles);

    // current
    // Reads the group with the editor's values.
    function current() {
        var out = JSON.parse(JSON.stringify(draft));
        readers.forEach(function (read) { read(out); });
        if (everyone) out.MemberIds = [];
        return out;
    }

    return {
        element: root,
        ready: ready,
        getValue: current,
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

export default function (view) {
    'use strict';

    var shared = null;
    var page = null;
    var list = null;
    var groups = [];
    var users = [];
    var devices = [];
    var sessions = [];
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
    // Reads the groups, the users, the devices and sessions, and the Radarr and Sonarr profiles, then draws the list. An answer that lands after a newer load is dropped.
    function loadGroups() {
        var mine = ++loadSeq;
        // The cards show a dash until the groups and users are read.
        if (!list) shared.renderCards('groupCards', groupCards({ groups: '–', members: '–', loose: '–', expiring: '–' }, shared.t));
        return Promise.all([
            shared.request('Groups'),
            shared.request('Groups/Users'),
            shared.request('Devices').catch(function () { return { Items: [] }; }),
            shared.request('Notifications/Events').catch(function () { return []; }),
            shared.request('Requests/Profiles').catch(function () { return []; }),
            shared.getConfig().catch(function () { return null; }),
            shared.request('Sessions').catch(function () { return []; })
        ]).then(function (answers) {
            if (mine !== loadSeq) return;
            groups = answers[0] || [];
            users = answers[1] || [];
            devices = (answers[2] && answers[2].Items) || [];
            events = Array.isArray(answers[3]) ? answers[3] : [];
            profiles = Array.isArray(answers[4]) ? answers[4] : [];
            config = answers[5];
            sessions = Array.isArray(answers[6]) ? answers[6] : [];
            shared.renderCards('groupCards', groupCards(groupCounts(groups, users, new Date()), shared.t));
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

    // removeGroup
    // Asks first, then removes the group and closes its sidecar. Its members and their accounts stay.
    function removeGroup(group, close) {
        var t = shared.t;
        nectoModule.confirmDialog(t.tr('ConfirmRemoveGroup'), {
            title: t.format('TitleRemoveName', group.Name),
            confirmText: t.tr('ButtonRemove'),
            destructive: true
        }).then(function (confirmed) {
            if (!confirmed) return;
            shared.request('Groups/' + group.Id, 'DELETE').then(function () {
                close('deleted');
                report(t.tr('ToastGroupRemoved'), false);
                loadGroups();
            }).catch(function (err) {
                shared.errorText(err, t.tr('ErrorRemoveGroup')).then(function (text) { report(text, true); });
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
        var editor = createGroupEditor(group || newGroup(), { shared: shared, users: users, groups: groups, devices: devices, sessions: sessions, events: events, profiles: profiles, config: config });
        nectoModule.openEditorSidecar({
            title: isNew ? t.tr('TitleNewGroup') : groupName(group, t),
            body: editor.element,
            page: page,
            isNew: isNew || isEveryone(group),
            ready: editor.ready,
            serialize: function () { return JSON.stringify(editor.getValue()); },
            saveText: t.tr('Save'),
            deleteText: t.tr('ButtonRemove'),
            onSave: function () { return saveGroup(editor, isNew); },
            onDelete: function (close) { removeGroup(group, close); },
            onClose: function () { editor.destroy(); }
        });
    }

    var made = nectoModule.createNectoPage(view, {
        page: 'necto_groups',
        bind: function () {
            view.querySelector('#btnAddGroup').addEventListener('click', function () { openGroup(null); });
        },
        load: function () { loadGroups(); }
    });
    shared = made.shared;
    page = made.page;
}

// OWN_UPDATES
// The events about a member's own requests and issues, which a new group sends by both methods.
var OWN_UPDATES = ['requests.approved', 'requests.declined', 'requests.available', 'issues.updated'];

// newGroup
// Starts a new group. Its members hear what happens to their own requests and issue reports, as Everyone's do on a
// fresh install. Pages and Invite groups start Permissive, and it gives nothing else until it is set.
//
// Output
// [object]: The group the editor opens with.
export function newGroup() {
    return {
        Name: '',
        MemberIds: [],
        Permissions: {
            CustomPages: { AllPages: true, PageIds: [] },
            Invites: { AnyGroup: true, TargetGroupIds: [] },
            Notifications: { ByMethod: true, BrowserEventKeys: OWN_UPDATES.slice(), MessageEventKeys: OWN_UPDATES.slice() }
        }
    };
}
