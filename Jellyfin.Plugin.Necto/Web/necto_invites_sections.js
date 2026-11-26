// necto_invites_sections.js
// The Invites sections of Settings and Troubleshooting, which this module adds to the registry when it loads.

import * as nectoModule from './configurationpage?name=necto_shared.js';

var el = nectoModule.el;

// The key of the task that turns off expired invites.
export var INVITE_EXPIRY_TASK = 'NectoInviteExpiry';

// Ids for fields, so two sections never share one.
var fieldSeq = 0;

// numberField
// Builds a labelled number input with its description.
//
// Inputs
// label [string]: The label.
// help [string]: The description.
// data [string]: The input's data-field.
// min [number]: The least value.
//
// Output
// [object]: wrap, the container, and input.
function numberField(label, help, data, min) {
    fieldSeq += 1;
    var id = 'necto-invites-' + data + '-' + fieldSeq;
    var input = el('input', { is: 'emby-input', type: 'number', id: id, min: String(min), 'data-field': data });
    return {
        input: input,
        wrap: el('div', { class: 'inputContainer' }, [
            el('label', { class: 'inputLabel inputLabelUnfocused', for: id, text: label }),
            input,
            el('div', { class: 'fieldDescription', text: help })
        ])
    };
}

// readCount
// Reads a count from a field, 0 or more, or a fallback when it is not a number.
//
// Inputs
// input [HTMLInputElement]: The field.
// fallback [number]: The value for a field that holds no number.
// min [number]: The least value.
//
// Output
// [number]: The count.
export function readCount(input, fallback, min) {
    var n = parseInt(input.value, 10);
    return isNaN(n) ? fallback : Math.max(min, n);
}

// buildSettings
// Fills the Invites section of Settings: the address links start with, the signup rate limit, and the wrong PINs
// that lock an invite.
//
// Inputs
// content [HTMLElement]: The section's content.
// ctx [object]: shared is the Necto helper bag.
//
// Output
// [object]: A handle with load(config) and apply(config).
function buildSettings(content, ctx) {
    var t = ctx.shared.t;
    fieldSeq += 1;
    var linkId = 'necto-invites-link-' + fieldSeq;
    var link = el('input', { is: 'emby-input', type: 'url', id: linkId, placeholder: 'https://jellyfin.example.com', 'data-field': 'LinkUrl' });
    var count = numberField(t.tr('InvitesLabelRateCount'), t.tr('InvitesRateCountHelp'), 'RateLimitCount', 0);
    var windowField = numberField(t.tr('InvitesLabelRateWindow'), t.tr('InvitesRateWindowHelp'), 'RateLimitWindowMinutes', 0);
    var pins = numberField(t.tr('InvitesLabelPinAttempts'), t.tr('InvitesPinAttemptsHelp'), 'MaxPinAttempts', 1);

    [
        el('h3', { class: 'stolon-subsection-title', text: t.tr('InvitesHeaderLinks') }),
        el('div', { class: 'inputContainer' }, [
            el('label', { class: 'inputLabel inputLabelUnfocused', for: linkId, text: t.tr('InvitesLabelLinkUrl') }),
            link,
            el('div', { class: 'fieldDescription', text: t.tr('InvitesLinkUrlHelp') })
        ]),
        el('h3', { class: 'stolon-subsection-title', text: t.tr('InvitesHeaderLimits') }),
        el('div', { class: 'fieldDescription', text: t.tr('InvitesLimitsHelp') }),
        count.wrap, windowField.wrap, pins.wrap
    ].forEach(function (node) { content.appendChild(node); });

    return {
        load: function (config) {
            var s = config.InviteSettings || {};
            link.value = s.LinkUrl || '';
            count.input.value = String(s.RateLimitCount == null ? 10 : s.RateLimitCount);
            windowField.input.value = String(s.RateLimitWindowMinutes == null ? 60 : s.RateLimitWindowMinutes);
            pins.input.value = String(s.MaxPinAttempts || 5);
        },
        apply: function (config) {
            config.InviteSettings = config.InviteSettings || {};
            config.InviteSettings.LinkUrl = link.value.trim().replace(/\/+$/, '');
            config.InviteSettings.RateLimitCount = readCount(count.input, 10, 0);
            config.InviteSettings.RateLimitWindowMinutes = readCount(windowField.input, 60, 0);
            config.InviteSettings.MaxPinAttempts = readCount(pins.input, 5, 1);
        }
    };
}

// buildTroubleshooting
// Fills the Invites section of Troubleshooting: running the expiry task now.
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
    var run = el('button', { is: 'emby-button', type: 'button', class: 'raised stolon-button stolon-secondary stolon-small', 'data-action': 'run-invite-expiry' }, [el('span', { text: t.tr('ButtonRunTask') })]);
    [
        status,
        el('h3', { class: 'stolon-subsection-title', text: t.tr('HeaderScheduledChecks') }),
        el('div', { class: 'stolon-reset-group' }, [
            el('div', { class: 'stolon-reset-row' }, [
                el('div', { class: 'stolon-reset-info' }, [el('div', { text: t.tr('InvitesTaskExpire') }), el('div', { class: 'fieldDescription', text: t.tr('InvitesTaskExpireDescription') })]),
                run
            ])
        ])
    ].forEach(function (node) { content.appendChild(node); });

    run.addEventListener('click', function () {
        shared.runTask(run, INVITE_EXPIRY_TASK, t.tr('ButtonRunTask')).then(function (poller) { pollers.push(poller); }).catch(function (err) {
            return shared.errorText(err, t.tr('ErrorRunTask')).then(function (text) { shared.setStatus(status, text, true); });
        });
    });

    return {
        load: function () { },
        destroy: function () { pollers.splice(0).forEach(function (p) { if (p && p.cancel) p.cancel(); }); }
    };
}

nectoModule.registerSection('settings', 'invites', { build: buildSettings });
nectoModule.registerSection('troubleshooting', 'invites', { build: buildTroubleshooting });
