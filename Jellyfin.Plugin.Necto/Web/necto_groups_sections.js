// necto_groups_sections.js
// The Groups sections of Settings and Troubleshooting, which this module adds to the registry when it loads.

import * as nectoModule from './configurationpage?name=necto_shared.js';

var el = nectoModule.el;

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
// Fills the Groups section of Settings: the group a new user joins, and whether reset codes show.
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
    var resetId = fieldId('show-reset-codes');
    var select = el('select', { is: 'emby-select', id: selectId, 'data-field': 'NewUserGroupId' });
    var resets = el('input', { type: 'checkbox', is: 'emby-checkbox', id: resetId, 'data-field': 'ShowResetCodes' });

    content.appendChild(el('h3', { class: 'stolon-subsection-title', text: t.tr('HeaderNewUsers') }));
    content.appendChild(el('div', { class: 'selectContainer' }, [
        el('label', { class: 'selectLabel', for: selectId, text: t.tr('LabelNewUserGroup') }),
        select,
        el('div', { class: 'fieldDescription', text: t.tr('NewUserGroupHelp') })
    ]));
    content.appendChild(el('h3', { class: 'stolon-subsection-title', text: t.tr('HeaderAccounts') }));
    content.appendChild(el('div', { class: 'checkboxContainer checkboxContainer-withDescription' }, [
        el('label', {}, [resets, el('span', { text: t.tr('LabelShowResetCodes') })]),
        el('div', { class: 'fieldDescription checkboxFieldDescription', text: t.tr('ShowResetCodesHelp') })
    ]));

    return {
        load: function (config) {
            var chosen = (config.GroupSettings && config.GroupSettings.NewUserGroupId) || '';
            select.textContent = '';
            groupOptions(config, t).forEach(function (o) {
                select.appendChild(el('option', { value: o.value, text: o.label }));
            });
            select.value = chosen;
            resets.checked = !!(config.AccountSettings && config.AccountSettings.ShowResetCodes);
        },
        apply: function (config) {
            config.GroupSettings = config.GroupSettings || {};
            config.GroupSettings.NewUserGroupId = select.value || null;
            config.AccountSettings = config.AccountSettings || {};
            config.AccountSettings.ShowResetCodes = !!resets.checked;
        }
    };
}

// resetRow
// Builds one row of a reset group: a label and a description, and a button at its end.
//
// Inputs
// label [string]: What the row does.
// help [string]: One sentence on what follows.
// button [string]: The button's label.
// action [string]: The value of the button's data-action.
//
// Output
// [HTMLElement]: The row.
function resetRow(label, help, button, action) {
    return el('div', { class: 'stolon-reset-row' }, [
        el('div', { class: 'stolon-reset-info' }, [el('div', { text: label }), el('div', { class: 'fieldDescription', text: help })]),
        el('button', { is: 'emby-button', type: 'button', class: 'raised stolon-button stolon-secondary stolon-small', 'data-action': action }, [el('span', { text: button })])
    ]);
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
// Turns the pending password resets into rows for a record list, each with its state tag and the code in a masked
// copy field. Expired codes come last.
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
        var field = nectoModule.createCopyField({ value: code.Pin, secret: true, allowExpose: true, inline: true, label: t.format('ResetCodeFor', code.UserName) });
        return {
            id: String(index),
            title: code.UserName,
            secondary: t.format(state === 'expired' ? 'ExpiredOnDate' : 'ExpiresOnDate', shared.formatDate(code.ExpirationDate)),
            tags: [tags[state]],
            state: state,
            aside: field.element
        };
    });
    return items.filter(function (i) { return i.state !== 'expired'; }).concat(items.filter(function (i) { return i.state === 'expired'; }));
}

// resetCards
// Counts the password resets by state for the cards over their list.
//
// Inputs
// items [object[]]: The rows resetItems gives.
// t [object]: The strings.
//
// Output
// [object[]]: The Active, Expiring, and Expired cards.
export function resetCards(items, t) {
    var counts = { active: 0, expiring: 0, expired: 0 };
    (items || []).forEach(function (i) { counts[i.state]++; });
    return [
        { count: counts.active, label: t.tr('StateActive'), color: 'green' },
        { count: counts.expiring, label: t.tr('GroupsCardExpiring'), color: 'orange' },
        { count: counts.expired, label: t.tr('StateExpired'), color: 'red' }
    ];
}

// buildTroubleshooting
// Fills the Groups section of Troubleshooting: applying the password rules, running the account tasks now, and the
// pending password resets.
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
    var resetList = null;
    var pollers = [];
    var status = el('div', { class: 'stolon-status-text', role: 'status', 'data-role': 'status' });
    var resets = el('div', { 'data-role': 'resets' });
    var apply = resetRow(t.tr('TaskApplyPasswordRules'), t.tr('HelpApplyPasswordRules'), t.tr('ButtonApplyRules'), 'apply-rules');
    var templates = resetRow(t.tr('GroupsTaskApplyTemplates'), t.tr('GroupsTaskApplyTemplatesDescription'), t.tr('GroupsButtonApplyTemplates'), 'apply-templates');
    var expired = resetRow(t.tr('TaskProcessExpired'), t.tr('HelpProcessExpiredTask'), t.tr('ButtonRunTask'), 'run-account-rules');
    var sessions = resetRow(t.tr('TaskCleanSessions'), t.tr('HelpCleanSessionsTask'), t.tr('ButtonRunTask'), 'run-session-cleanup');

    [
        status,
        el('h3', { class: 'stolon-subsection-title', text: t.tr('HeaderPasswordRequirements') }),
        el('div', { class: 'stolon-reset-group' }, [apply]),
        el('h3', { class: 'stolon-subsection-title', text: t.tr('GroupsHeaderTemplates') }),
        el('div', { class: 'stolon-reset-group' }, [templates]),
        el('h3', { class: 'stolon-subsection-title', text: t.tr('HeaderScheduledChecks') }),
        el('div', { class: 'stolon-reset-group' }, [expired, sessions]),
        el('h3', { class: 'stolon-subsection-title', text: t.tr('HeaderPasswordResets') }),
        resets
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
            report(moved === 0 ? t.tr('ToastNoUsersMoved') : shared.plural(moved, 'ToastUsersMovedOne', 'ToastUsersMovedMany'), false);
        }).catch(function (err) {
            return shared.errorText(err, t.tr('ErrorApplyRules')).then(function (text) { report(text, true); });
        });
    }

    // runTask
    // Starts a scheduled task and follows it on its button.
    function runTask(action, key) {
        return shared.runTask(button(action), key, t.tr('ButtonRunTask')).then(function (poller) {
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

    // showResets
    // Draws the pending password resets, or why none show.
    function showResets(result) {
        if (resetList) { resetList.destroy(); resetList = null; }
        resets.textContent = '';
        if (!result || !result.Enabled) {
            resets.appendChild(el('div', { class: 'stolon-info', text: t.tr('ResetsDisabled') }));
            return;
        }
        var warning = el('div', { class: 'stolon-warn', text: t.tr('ResetsHttpsWarning') });
        if (!(result.Codes || []).length) {
            resets.innerHTML = shared.emptySection(t.tr('EmptyPendingResets'), { inline: true });
            resets.insertBefore(warning, resets.firstChild);
            return;
        }
        var items = resetItems(result.Codes, shared, new Date());
        var cards = el('div', { class: 'stolon-cards compact', 'data-role': 'reset-cards' });
        resets.appendChild(cards);
        resets.appendChild(warning);
        shared.renderCards(cards, resetCards(items, t));
        resetList = nectoModule.createRecordList({ items: items, label: t.tr('HeaderPasswordResets') });
        resets.appendChild(resetList.element);
    }

    // loadResets
    // Reads the pending password resets and draws them.
    function loadResets() {
        return shared.request('Groups/ResetCodes').then(showResets).catch(function (err) {
            return shared.errorText(err, t.tr('ErrorLoadResets')).then(function (text) { report(text, true); });
        });
    }

    button('apply-rules').addEventListener('click', applyRules);
    button('apply-templates').addEventListener('click', function () {
        shared.request('Groups/Templates/Apply', 'POST').then(function (result) {
            report(shared.plural((result && result.Count) || 0, 'GroupsTemplatesAppliedOne', 'GroupsTemplatesAppliedMany'), false);
        }).catch(function (err) {
            return shared.errorText(err, t.tr('GroupsErrorApplyTemplates')).then(function (text) { report(text, true); });
        });
    });
    button('run-account-rules').addEventListener('click', runAccountRules);
    button('run-session-cleanup').addEventListener('click', function () { runTask('run-session-cleanup', SESSION_CLEANUP_TASK); });

    return {
        load: loadResets,
        destroy: function () {
            pollers.splice(0).forEach(function (p) { if (p && p.cancel) p.cancel(); });
            if (resetList) { resetList.destroy(); resetList = null; }
        }
    };
}

nectoModule.registerSection('settings', 'groups', { build: buildSettings });
nectoModule.registerSection('troubleshooting', 'groups', { build: buildTroubleshooting });
