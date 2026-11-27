// necto_groups_sections.js
// The Groups sections of Settings and Troubleshooting, which this module adds to the registry when it loads, and the
// Password resets section of the Groups page.

import * as nectoModule from './configurationpage?name=necto_shared.js';

var h = nectoModule.h;

// The keys of the scheduled tasks Troubleshooting runs.
export var ACCOUNT_RULES_TASK = 'NectoAccountRules';
export var SESSION_CLEANUP_TASK = 'NectoSessionCleanup';

// A reset code this close to its expiry shows as expiring.
var RESET_SOON_MS = 5 * 60 * 1000;

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
    return 'necto-groups-' + name + '-' + fieldSeq;
}

// groupOptions
// Lists the choices for the group a new user joins: Everyone, then each group by name.
//
// Inputs
// config [object]: The plugin configuration.
// t [object]: The strings.
//
// Output
// [object[]]: Each choice's value and label. Everyone's value is empty.
export function groupOptions(config, t) {
    var groups = ((config && config.Groups) || []).slice().sort(function (a, b) {
        return String(a.Name).localeCompare(String(b.Name));
    });
    return [{ value: '', label: t.tr('GroupEveryone') }].concat(groups.map(function (g) {
        return { value: g.Id, label: g.Name };
    }));
}

// buildSettings
// Fills the Groups section of Settings: the group a new user joins.
//
// Inputs
// content [HTMLElement]: The section's content.
// ctx [object]: shared is the Necto helper bag.
//
// Output
// [object]: A handle with load(config) and apply(config).
function buildSettings(content, ctx) {
    var t = ctx.shared.t;
    var selectId = fieldId('new-user-group');
    var select = h('select', { is: 'emby-select', id: selectId, 'data-field': 'NewUserGroupId' });

    content.appendChild(h('h3', { class: 'stolon-subsection-title', text: t.tr('HeaderNewUsers') }));
    content.appendChild(h('div', { class: 'selectContainer' }, [
        h('label', { class: 'selectLabel', for: selectId, text: t.tr('LabelNewUserGroup') }),
        select,
        h('div', { class: 'fieldDescription', text: t.tr('NewUserGroupHelp') })
    ]));

    return {
        load: function (config) {
            var chosen = (config.GroupSettings && config.GroupSettings.NewUserGroupId) || '';
            select.textContent = '';
            groupOptions(config, t).forEach(function (o) {
                select.appendChild(h('option', { value: o.value, text: o.label }));
            });
            select.value = chosen;
        },
        apply: function (config) {
            config.GroupSettings = config.GroupSettings || {};
            config.GroupSettings.NewUserGroupId = select.value || null;
        }
    };
}

// resetState
// Says whether a password reset is active, expiring in the next five minutes, or expired.
//
// Inputs
// code [object]: One reset from Groups/ResetCodes.
// now [Date]: The time to compare against.
//
// Output
// [string]: active, expiring, or expired.
export function resetState(code, now) {
    var due = code.ExpirationDate ? new Date(code.ExpirationDate) : null;
    if (code.Expired || (due && due <= now)) return 'expired';
    if (due && due - now <= RESET_SOON_MS) return 'expiring';
    return 'active';
}

// resetItems
// Turns the pending password resets into rows for a table, each with its state badge and its code. Expired codes
// come last.
//
// Inputs
// codes [object[]]: The resets from Groups/ResetCodes.
// shared [object]: The Necto helper bag.
// now [Date]: The time to compare against.
//
// Output
// [object[]]: The rows, each with its state.
export function resetItems(codes, shared, now) {
    var t = shared.t;
    var tags = {
        active: { label: t.tr('StateActive'), icon: 'schedule', color: 'green' },
        expiring: { label: t.tr('GroupsCardExpiring'), icon: 'timer', color: 'orange' },
        expired: { label: t.tr('StateExpired'), icon: 'timer_off', color: 'red' }
    };
    var items = (codes || []).map(function (code, index) {
        var state = resetState(code, now);
        return {
            id: String(index),
            title: code.UserName,
            secondary: t.format(state === 'expired' ? 'ExpiredOnDate' : 'ExpiresOnDate', shared.formatDate(code.ExpirationDate)),
            tags: [tags[state]],
            state: state,
            pin: code.Pin
        };
    });
    return items.filter(function (i) { return i.state !== 'expired'; }).concat(items.filter(function (i) { return i.state === 'expired'; }));
}

// createPendingResets
// Builds the Password resets tab of the Groups page: each member's pending reset in a table, opening its code in a
// sidecar to copy. While no group shows reset codes, it says how to turn them on.
//
// Inputs
// shared [object]: The Necto helper bag.
// page [object]: The page handle, for the sidecar.
//
// Output
// [object]: element, load(), and destroy().
export function createPendingResets(shared, page) {
    var t = shared.t;
    var esc = nectoModule.escapeHtml;
    var status = h('div', { class: 'stolon-status-text', role: 'status' });
    var table = nectoModule.createRowsTable(shared, {
        search: { enabled: false },
        text: function (row) { return row.title; },
        emptyState: { message: t.tr('EmptyPendingResets') },
        columns: [
            {
                key: 'title', type: 'custom', label: t.tr('LabelUser'),
                render: function (row) {
                    return '<div class="stolon-table-item-info"><div class="stolon-table-item-title">' + esc(row.title) + '</div>'
                        + '<div class="stolon-table-item-sub">' + esc(row.secondary) + '</div></div>';
                }
            },
            { key: 'state', type: 'custom', label: t.tr('LabelStatus'), className: 'stolon-table-cell-status', render: function (row) { return shared.badge(row.tags[0].color, row.tags[0].label); } }
        ],
        actions: {
            onRowClick: function (row) {
                var field = nectoModule.createCopyField({ value: row.pin, secret: true, allowExpose: true, label: t.format('ResetCodeFor', row.title) });
                nectoModule.openSidecar({ title: row.title, body: h('div', { class: 'stolon-stack' }, [field.element, h('div', { class: 'fieldDescription', text: row.secondary })]), page: page });
            }
        }
    });
    var off = h('div', { class: 'stolon-info stolon-hidden', 'data-role': 'resets-off', text: t.tr('ResetsDisabled') });
    var warning = h('div', { class: 'stolon-warn', text: t.tr('ResetsHttpsWarning') });
    var element = h('div', { class: 'stolon-stack', 'data-role': 'password-resets' }, [off, warning, status, table.element]);

    return {
        element: element,
        load: function () {
            return shared.request('Groups/ResetCodes').then(function (result) {
                var on = !!(result && result.Enabled);
                shared.setVisible(off, !on);
                shared.setVisible(warning, on);
                shared.setVisible(table.element, on);
                if (on) return table.setRows(resetItems(result.Codes, shared, new Date()));
            }).catch(function (err) {
                return shared.errorText(err, t.tr('ErrorLoadResets')).then(function (text) { shared.setStatus(status, text, true); });
            });
        },
        destroy: function () { table.destroy(); }
    };
}

// buildTroubleshooting
// Fills the Groups section of Troubleshooting: applying the password rules and the group Jellyfin settings, and
// running the account tasks now. The pending password resets show on the Groups page.
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
    var apply = nectoModule.createResetRow({ icon: 'password', label: t.tr('TaskApplyPasswordRules'), help: t.tr('HelpApplyPasswordRules'), button: t.tr('ButtonApply'), action: 'apply-rules' }).element;
    var templates = nectoModule.createResetRow({ icon: 'library_add_check', label: t.tr('GroupsTaskApplyTemplates'), help: t.tr('GroupsTaskApplyTemplatesDescription'), button: t.tr('ButtonApply'), action: 'apply-templates' }).element;
    var expired = nectoModule.createResetRow({ icon: 'event_busy', label: t.tr('TaskProcessExpired'), help: t.tr('HelpProcessExpiredTask'), button: t.tr('ButtonRunTask'), action: 'run-account-rules', destructive: true }).element;
    var sessions = nectoModule.createResetRow({ icon: 'devices', label: t.tr('TaskCleanSessions'), help: t.tr('HelpCleanSessionsTask'), button: t.tr('ButtonRunTask'), action: 'run-session-cleanup' }).element;

    [
        status,
        h('h3', { class: 'stolon-subsection-title', text: t.tr('HeaderPasswordRequirements') }),
        h('div', { class: 'stolon-reset-group' }, [apply]),
        h('h3', { class: 'stolon-subsection-title', text: t.tr('GroupsHeaderTemplates') }),
        h('div', { class: 'stolon-reset-group' }, [templates]),
        h('h3', { class: 'stolon-subsection-title', text: t.tr('HeaderScheduledChecks') }),
        h('div', { class: 'stolon-reset-group' }, [expired, sessions])
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

    // applyRules
    // Moves users onto or off password rules, and reports how many moved.
    function applyRules() {
        return shared.request('Groups/PasswordRules/Apply', 'POST').then(function (result) {
            var moved = (result && result.Moved) || 0;
            report(moved === 0 ? t.tr('ToastNoUsersMoved') : shared.t.plural(moved, 'ToastUsersMovedOne', 'ToastUsersMovedMany'), false);
        }).catch(function (err) {
            return shared.errorText(err, t.tr('ErrorApplyRules')).then(function (text) { report(text, true); });
        });
    }

    // runTask
    // Starts a scheduled task and follows it on its button.
    function runTask(action, key) {
        return shared.runScheduledTask(button(action), key, t.tr('ButtonRunTask')).then(function (poller) {
            pollers.push(poller);
        }).catch(function (err) {
            return shared.errorText(err, t.tr('ErrorRunTask')).then(function (text) { report(text, true); });
        });
    }

    // runAccountRules
    // Asks first, then runs the expiry and inactivity task, which may delete users.
    function runAccountRules() {
        return nectoModule.confirmDialog(t.tr('ConfirmRunAccountRules'), {
            title: t.tr('TaskProcessExpired'),
            confirmText: t.tr('ButtonRunTask'),
            destructive: true
        }).then(function (confirmed) {
            if (confirmed) return runTask('run-account-rules', ACCOUNT_RULES_TASK);
        });
    }

    button('apply-rules').addEventListener('click', applyRules);
    button('apply-templates').addEventListener('click', function () {
        shared.request('Groups/Templates/Apply', 'POST').then(function (result) {
            report(shared.t.plural((result && result.Count) || 0, 'GroupsTemplatesAppliedOne', 'GroupsTemplatesAppliedMany'), false);
        }).catch(function (err) {
            return shared.errorText(err, t.tr('GroupsErrorApplyTemplates')).then(function (text) { report(text, true); });
        });
    });
    button('run-account-rules').addEventListener('click', runAccountRules);
    button('run-session-cleanup').addEventListener('click', function () { runTask('run-session-cleanup', SESSION_CLEANUP_TASK); });

    return {
        load: function () {},
        destroy: function () {
            pollers.splice(0).forEach(function (p) { if (p && p.cancel) p.cancel(); });
        }
    };
}

nectoModule.registerSection('settings', 'groups', { build: buildSettings });
nectoModule.registerSection('troubleshooting', 'groups', { build: buildTroubleshooting });
