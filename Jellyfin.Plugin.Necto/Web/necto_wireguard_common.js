// necto_wireguard_common.js
// Shared by the WireGuard tool and its Settings and Troubleshooting sections: ids, limits and their periods, the
// data limit field, config file parsing, and the words for a tunnel's state.

import { createNumberUnitField } from './configurationpage?name=necto_shared.js';

// The windows a data limit counts over, in the order the period select lists them.
export var PERIODS = ['Hour', 'Day', 'Week', 'Month', 'Year'];

// The units a data amount is entered in. Each factor is in bytes, counted in 1024s as Stolon's size format counts
// them, so a 10 GB limit reads back as 10 GB.
var DATA_UNITS = [
    { value: 'MB', label: 'MB', factor: 1048576 },
    { value: 'GB', label: 'GB', factor: 1073741824 },
    { value: 'TB', label: 'TB', factor: 1099511627776 }
];

// compact
// A user, peer, or connection id in the form the server uses as a key: lowercase, without dashes.
//
// Inputs
// id [string]: The id, with or without dashes.
//
// Output
// [string]: The compact id.
export function compact(id) {
    return String(id || '').replace(/-/g, '').toLowerCase();
}

// readLimit
// Reads an optional limit field, where blank means the default.
//
// Inputs
// input [HTMLInputElement]: The field.
//
// Output
// [number|null]: The limit, or null when blank.
export function readLimit(input) {
    var text = String(input.value || '').trim();
    if (!text) return null;
    var n = parseInt(text, 10);
    return isNaN(n) || n < 0 ? null : n;
}

// readCount
// Reads a required whole number field.
//
// Inputs
// input [HTMLInputElement]: The field.
// fallback [number]: The value for a blank or unreadable field.
//
// Output
// [number]: The number, never below 0.
export function readCount(input, fallback) {
    var n = parseInt(input.value, 10);
    return isNaN(n) || n < 0 ? fallback : n;
}

// periodLabel
// Names a period in the viewer's language, such as Month.
//
// Inputs
// t [object]: The strings.
// period [string]: Hour, Day, Week, Month, or Year.
//
// Output
// [string]: The period's name.
export function periodLabel(t, period) {
    if (period === 'Hour') return t.tr('WireGuardPeriodHour');
    if (period === 'Day') return t.tr('WireGuardPeriodDay');
    if (period === 'Week') return t.tr('WireGuardPeriodWeek');
    if (period === 'Year') return t.tr('LabelYear');
    return t.tr('WireGuardPeriodMonth');
}

// normalizePeriod
// Reads a period the server sent as a name or a number.
//
// Inputs
// value [string|number]: The period.
//
// Output
// [string]: The period's name, Month when unknown.
export function normalizePeriod(value) {
    if (typeof value === 'number') return PERIODS[value] || 'Month';
    return PERIODS.indexOf(value) >= 0 ? value : 'Month';
}

// dataText
// Describes a data limit, such as 10 GB/month, or - for no limit.
//
// Inputs
// t [object]: The strings.
// shared [object]: The helper bag, for its size format.
// bytes [number]: The limit in bytes. 0 is none.
// period [string]: The period it counts over.
//
// Output
// [string]: The description.
export function dataText(t, shared, bytes, period) {
    if (!(bytes > 0)) return '-';
    return shared.formatSize(bytes) + '/' + periodLabel(t, normalizePeriod(period)).toLowerCase();
}

// limitText
// Describes a limit that may fall back to a default.
//
// Inputs
// own [number|null]: The record's own limit, or null for the default.
// fallback [number]: The default.
// unit [string]: The unit after the number.
//
// Output
// [string]: Such as 50 Mbps, or - for no limit.
export function limitText(own, fallback, unit) {
    var value = own != null ? own : fallback;
    return value > 0 ? value + ' ' + unit : '-';
}

// speedText
// A download and upload limit as one value, such as 50 Mbps / 10 Mbps, or - when neither is set.
//
// Inputs
// downOwn, downDefault, upOwn, upDefault [number|null]: Each direction's own limit and its default.
//
// Output
// [string]: The two limits.
export function speedText(downOwn, downDefault, upOwn, upDefault) {
    var down = limitText(downOwn, downDefault, 'Mbps');
    var up = limitText(upOwn, upDefault, 'Mbps');
    return down === '-' && up === '-' ? '-' : down + ' / ' + up;
}

// createDataLimitField
// A data limit as one Stolon Number Unit Field: the amount, its unit of MB, GB, or TB, and the period it counts
// over as a second select in the same row.
//
// Inputs
// t [object]: The strings.
// value [object]: { bytes, period } to start with. bytes 0 is no limit.
//
// Output
// [object]: { element, getValue, setValue }, where getValue gives { bytes, period }.
export function createDataLimitField(t, value) {
    var amount = createNumberUnitField({ units: DATA_UNITS, unit: 'GB', min: 0, valueLabel: t.tr('WireGuardLabelAmount'), unitLabel: t.tr('WireGuardLabelUnit') });
    var select = document.createElement('select');
    select.setAttribute('aria-label', t.tr('WireGuardLabelPeriod'));
    PERIODS.forEach(function (p) {
        var option = document.createElement('option');
        option.value = p;
        option.textContent = periodLabel(t, p);
        select.appendChild(option);
    });

    // A slash between the unit and the period reads the row as an amount per period, such as 10 GB / Day.
    var slash = document.createElement('span');
    slash.className = 'necto-wireguard-slash';
    slash.textContent = '/';
    slash.setAttribute('aria-hidden', 'true');
    var row = amount.element.querySelector('.stolon-number-unit-row') || amount.element;
    row.appendChild(slash);
    row.appendChild(select);

    function setValue(next) {
        var v = next || {};
        var bytes = Number(v.bytes) || 0;
        // No limit reads as 0 GB rather than the largest unit that divides 0, which is TB.
        if (bytes > 0) amount.setBaseValue(bytes);
        else amount.setValue({ value: 0, unit: 'GB' });
        select.value = normalizePeriod(v.period);
    }

    function getValue() {
        var base = amount.getValue().base;
        return { bytes: base > 0 ? Math.round(base) : 0, period: select.value };
    }

    setValue(value);
    return { element: amount.element, getValue: getValue, setValue: setValue };
}

// parseWgConfig
// Reads the fields a connection needs from a WireGuard configuration file.
//
// Inputs
// text [string]: The file's text.
//
// Output
// [object]: PrivateKey, Address, PeerPublicKey, PresharedKey, Endpoint, and Keepalive, each when found.
export function parseWgConfig(text) {
    var out = {};
    var section = '';
    String(text || '').split(/\r?\n/).forEach(function (raw) {
        var line = raw.replace(/#.*$/, '').trim();
        var head = /^\[(\w+)\]$/.exec(line);
        if (head) { section = head[1].toLowerCase(); return; }
        var m = /^(\w+)\s*=\s*(.+)$/.exec(line);
        if (!m) return;
        var k = m[1].toLowerCase();
        var v = m[2].trim();
        if (section === 'interface' && k === 'privatekey') out.PrivateKey = v;
        if (section === 'interface' && k === 'address') out.Address = v.split(',')[0].trim().split('/')[0];
        if (section === 'peer' && k === 'publickey' && !out.PeerPublicKey) out.PeerPublicKey = v;
        if (section === 'peer' && k === 'presharedkey') out.PresharedKey = v;
        if (section === 'peer' && k === 'endpoint') out.Endpoint = v;
        if (section === 'peer' && k === 'persistentkeepalive') out.Keepalive = parseInt(v, 10) || 0;
    });
    return out;
}

// parseForwards
// Reads one forward per line, as the local port, a space, then the remote host and port.
//
// Inputs
// text [string]: The lines, such as 18096 10.0.0.1:8096.
//
// Output
// [object[]]: Each forward's LocalPort, RemoteHost, and RemotePort. A line that does not read is left out.
export function parseForwards(text) {
    return String(text || '').split(/\r?\n/).map(function (line) {
        var m = /^\s*(\d+)\s+(\S+):(\d+)\s*$/.exec(line);
        return m ? { LocalPort: parseInt(m[1], 10), RemoteHost: m[2], RemotePort: parseInt(m[3], 10) } : null;
    }).filter(Boolean);
}

// safeFileName
// Turns a peer's name into a file name a device accepts, of at most 15 characters.
//
// Inputs
// name [string]: The peer's name.
//
// Output
// [string]: The file name without its extension.
export function safeFileName(name) {
    return String(name || 'peer').replace(/[^A-Za-z0-9_=+.-]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 15) || 'peer';
}

// stateTag
// The tag that says whether a peer or connection is connected, off, over its limit, or left out by its groups.
//
// Inputs
// t [object]: The strings.
// state [string]: Connected, Offline, Disabled, OverLimit, NotAllowed, OverPeerCount, or Failed.
//
// Output
// [object]: { label, icon, color, off } for a record list tag.
export function stateTag(t, state) {
    if (state === 'OverLimit') return { label: t.tr('WireGuardTagOverLimit'), icon: 'block', color: 'red' };
    if (state === 'NotAllowed') return { label: t.tr('WireGuardTagNotAllowed'), icon: 'block', color: 'orange' };
    if (state === 'OverPeerCount') return { label: t.tr('WireGuardTagOverPeerCount'), icon: 'block', color: 'orange' };
    if (state === 'Failed') return { label: t.tr('LabelFailed'), icon: 'error', color: 'red' };
    if (state === 'Disabled') return { label: t.tr('Disabled'), icon: 'pause', off: true };
    return state === 'Connected'
        ? { label: t.tr('ProxyConnected'), icon: 'link', color: 'green' }
        : { label: t.tr('WireGuardTagOffline'), icon: 'link_off', color: 'gray' };
}

// tunnelStateLabel
// Names a tunnel state in the viewer's language.
//
// Inputs
// t [object]: The strings.
// state [string]: Starting, Running, Failed, or Stopped.
//
// Output
// [string]: The state's name.
export function tunnelStateLabel(t, state) {
    if (state === 'Running') return t.tr('ProxyCardRunning');
    if (state === 'Starting') return t.tr('WireGuardStatusStarting');
    if (state === 'Failed') return t.tr('LabelFailed');
    return t.tr('ProxyCardStopped');
}
