// necto_wireguard_sections.js
// The WireGuard sections of Settings and Troubleshooting, which this module adds to the registry when it loads.
// Settings holds the master switch with the wireguard-go download, the host, its DNS and limits, and the connection
// defaults. Troubleshooting holds the tunnels' state, a restart, and each user's peers against their groups.

import * as nectoModule from './configurationpage?name=necto_shared.js';
import { readCount, createDataLimitField, normalizePeriod, dataText, tunnelStateLabel } from './configurationpage?name=necto_wireguard_common.js';

var el = nectoModule.el;

// Ids for fields, so two sections never share one.
var fieldSeq = 0;

function fieldId(name) {
    fieldSeq += 1;
    return 'necto-wireguard-' + name + '-' + fieldSeq;
}

// sidecarText
// Says what wireguard-go this server has, such as "Version 1.0.0 for linux-amd64, downloaded Nov 20, 2026.".
//
// Inputs
// info [object]: The answer of WireGuard/Sidecar.
// shared [object]: The Necto helper bag.
//
// Output
// [string]: One sentence.
export function sidecarText(info, shared) {
    var t = shared.t;
    if (!info || !info.Platform) return t.tr('WireGuardSidecarNoPlatform');
    if (!info.Installed) return t.format('WireGuardSidecarMissing', info.ReleaseVersion, info.Platform);
    return t.format('WireGuardSidecarInstalled', info.Version || '?', info.Platform, info.DownloadedUtc ? shared.formatDate(info.DownloadedUtc) : '?');
}

// downloadSidecar
// Downloads wireguard-go for this server, showing the work on a button.
//
// Inputs
// shared [object]: The Necto helper bag.
// button [HTMLButtonElement]: The button that started it, or null.
//
// Output
// [Promise]: Resolves with the sidecar's description, and rejects with the reason.
export function downloadSidecar(shared, button) {
    var label = button ? button.textContent : '';
    if (button) { button.disabled = true; button.textContent = shared.t.tr('WireGuardDownloading'); }
    return shared.request('WireGuard/Sidecar/Download', 'POST').finally(function () {
        if (button) { button.disabled = false; button.textContent = label; }
    });
}

// checkbox
// A checkbox with its label and an optional description.
function checkbox(t, label, help) {
    var input = el('input', { type: 'checkbox', is: 'emby-checkbox', id: fieldId('check') });
    var element = el('div', { class: 'checkboxContainer' + (help ? ' checkboxContainer-withDescription' : '') }, [
        el('label', {}, [input, el('span', { text: t.tr(label) })]),
        help ? el('div', { class: 'fieldDescription checkboxFieldDescription', text: t.tr(help) }) : null
    ]);
    return { element: element, input: input };
}

// input
// A text or number input with its label and an optional description and placeholder.
function input(t, label, type, help, placeholder) {
    var id = fieldId('input');
    var box = el('input', { is: 'emby-input', type: type, id: id, autocomplete: 'off', placeholder: placeholder || null, min: type === 'number' ? '0' : null });
    var element = el('div', { class: 'inputContainer' }, [
        el('label', { for: id, text: t.tr(label) }),
        box,
        help ? el('div', { class: 'fieldDescription', text: t.tr(help) }) : null
    ]);
    return { element: element, input: box };
}

// labelled
// Puts a control under a label, with an optional description.
function labelled(t, label, control, help) {
    return el('div', { class: 'inputContainer' }, [
        el('label', { text: t.tr(label) }),
        control,
        help ? el('div', { class: 'fieldDescription', text: t.tr(help) }) : null
    ]);
}

// buildSettings
// Fills the WireGuard section of Settings. Turning on Enable WireGuard asks to download wireguard-go first when the
// server has none.
//
// Inputs
// content [HTMLElement]: The section's content.
// ctx [object]: shared is the Necto helper bag.
//
// Output
// [object]: A handle with load(config) and apply(config).
export function buildSettings(content, ctx) {
    var shared = ctx.shared;
    var t = shared.t;
    var status = el('div', { class: 'stolon-status-text', role: 'status', 'data-role': 'status' });
    var enabled = checkbox(t, 'ScriptsLabelEnabled', 'WireGuardEnabledHelp');
    var sidecarLine = el('div', { class: 'fieldDescription', 'data-role': 'sidecar' });
    var replace = el('button', { is: 'emby-button', type: 'button', class: 'raised stolon-button secondary small', 'data-action': 'replace-sidecar' }, [el('span', { text: t.tr('WireGuardButtonReplace') })]);
    var info = null;

    var host = checkbox(t, 'WireGuardLabelHostEnabled', 'WireGuardHostEnabledHelp');
    var endpoint = input(t, 'WireGuardLabelPublicEndpoint', 'text', 'WireGuardPublicEndpointHelp', 'vpn.example.com');
    var port = input(t, 'WireGuardLabelListenPort', 'number', 'WireGuardListenPortHelp');
    var subnet = input(t, 'WireGuardLabelTunnelSubnet', 'text', 'WireGuardTunnelSubnetHelp', '10.66.0.0/24');
    var keyField = nectoModule.createCopyField({ label: t.tr('WireGuardLabelHostPublicKey'), copyLabel: t.tr('WireGuardLabelHostPublicKey'), mono: true });
    var keyRow = labelled(t, 'WireGuardLabelHostPublicKey', keyField.element);
    var psk = checkbox(t, 'WireGuardLabelPresharedKeys', 'WireGuardPresharedKeysHelp');
    var forward = checkbox(t, 'WireGuardLabelForwardClientAddress', 'WireGuardForwardClientAddressHelp');
    var dns = checkbox(t, 'WireGuardLabelDnsEnabled', 'WireGuardDnsEnabledHelp');
    var dnsName = input(t, 'LabelName', 'text', 'WireGuardDnsNameHelp', 'jellyfin.internal');
    var dnsUp = input(t, 'WireGuardLabelDnsUpstreams', 'text', 'WireGuardDnsUpstreamsHelp', '1.1.1.1, 1.0.0.1');
    var dnsFields = el('div', { class: 'stolon-indent' }, [dnsName.element, dnsUp.element]);
    var peerDown = input(t, 'WireGuardLabelDownloadMbps', 'number');
    var peerUp = input(t, 'WireGuardLabelUploadMbps', 'number');
    var peerData = createDataLimitField(t, null);
    var keepalive = input(t, 'WireGuardLabelKeepalive', 'number', 'WireGuardKeepaliveHelp');
    var mtu = input(t, 'WireGuardLabelMtu', 'number', 'WireGuardMtuHelp');
    var connect = checkbox(t, 'WireGuardLabelConnectEnabled', 'WireGuardConnectEnabledHelp');
    var connDown = input(t, 'WireGuardLabelDownloadMbps', 'number');
    var connUp = input(t, 'WireGuardLabelUploadMbps', 'number');
    var connData = createDataLimitField(t, null);

    [
        status,
        enabled.element,
        el('h3', { class: 'stolon-subsection-title', text: t.tr('WireGuardHeaderSidecar') }),
        sidecarLine,
        el('div', { class: 'stolon-reset-group' }, [el('div', { class: 'stolon-reset-row' }, [
            el('div', { class: 'stolon-reset-info' }, [el('div', { text: t.tr('WireGuardTitleReplace') }), el('div', { class: 'fieldDescription', text: t.tr('WireGuardSidecarHelp') })]),
            replace
        ])]),
        el('h3', { class: 'stolon-subsection-title', text: t.tr('WireGuardTabHost') }),
        host.element, endpoint.element, port.element, subnet.element,
        el('h3', { class: 'stolon-subsection-title', text: t.tr('WireGuardHeaderSecurity') }),
        keyRow, psk.element, forward.element,
        el('h3', { class: 'stolon-subsection-title', text: t.tr('WireGuardHeaderDns') }),
        dns.element, dnsFields,
        el('h3', { class: 'stolon-subsection-title', text: t.tr('WireGuardHeaderPeerLimits') }),
        el('div', { class: 'fieldDescription', text: t.tr('WireGuardPeerLimitsHelp') }),
        peerDown.element, peerUp.element, labelled(t, 'WireGuardLabelPeerDataLimit', peerData.element),
        el('h3', { class: 'stolon-subsection-title', text: t.tr('TabAdvanced') }),
        keepalive.element, mtu.element,
        el('h3', { class: 'stolon-subsection-title', text: t.tr('WireGuardHeaderConnections') }),
        connect.element,
        el('div', { class: 'fieldDescription', text: t.tr('WireGuardConnectionLimitsHelp') }),
        connDown.element, connUp.element, labelled(t, 'WireGuardLabelConnectionDataLimit', connData.element)
    ].forEach(function (node) { content.appendChild(node); });

    // report
    // Shows a result on the section's status line.
    function report(message, isError) {
        shared.setStatus(status, message, !!isError, { timeout: 10000 });
    }

    // showSidecar
    // Draws what wireguard-go the server has, and names the button Download or Replace.
    function showSidecar(next) {
        info = next;
        sidecarLine.textContent = sidecarText(info, shared);
        replace.querySelector('span').textContent = t.tr(info && info.Installed ? 'WireGuardButtonReplace' : 'Download');
        replace.disabled = !(info && info.Platform);
    }

    function loadSidecar() {
        return shared.request('WireGuard/Sidecar').then(showSidecar).catch(function () { showSidecar(null); });
    }

    // fetchSidecar
    // Downloads wireguard-go and reports the result.
    //
    // Output
    // [Promise]: Resolves true once downloaded, and false when it failed.
    function fetchSidecar() {
        report(t.tr('WireGuardDownloading'), false);
        return downloadSidecar(shared, replace).then(function (next) {
            showSidecar(next);
            report(t.tr('WireGuardToastDownloaded'), false);
            return true;
        }).catch(function (err) {
            return shared.errorText(err, t.tr('WireGuardErrorDownload')).then(function (text) {
                report(text, true);
                return false;
            });
        });
    }

    // Turning WireGuard on asks for the download first, and stays off when it is declined or fails.
    enabled.input.addEventListener('change', function () {
        if (!enabled.input.checked || (info && info.Installed)) return;
        nectoModule.confirmDialog(t.tr('WireGuardConfirmDownload'), {
            title: t.tr('WireGuardTitleDownload'),
            confirmText: t.tr('Download')
        }).then(function (yes) {
            if (!yes) {
                enabled.input.checked = false;
                return;
            }
            return fetchSidecar().then(function (ok) { if (!ok) enabled.input.checked = false; });
        });
    });

    replace.addEventListener('click', function () {
        if (!(info && info.Installed)) { fetchSidecar(); return; }
        nectoModule.confirmDialog(t.tr('WireGuardConfirmReplace'), {
            title: t.tr('WireGuardTitleReplace'),
            confirmText: t.tr('WireGuardButtonReplace')
        }).then(function (yes) { if (yes) fetchSidecar(); });
    });

    var reflectDns = function () { dnsFields.classList.toggle('stolon-hidden', !dns.input.checked); };
    dns.input.addEventListener('change', reflectDns);

    return {
        load: function (config) {
            var w = config.WireGuard || {};
            enabled.input.checked = !!w.Enabled;
            host.input.checked = !!w.HostEnabled;
            endpoint.input.value = w.PublicEndpoint || '';
            port.input.value = w.ListenPort || 51820;
            subnet.input.value = w.TunnelSubnet || '';
            keyField.setValue(w.HostPublicKey || '');
            keyRow.classList.toggle('stolon-hidden', !w.HostPublicKey);
            psk.input.checked = w.UsePresharedKeys !== false;
            forward.input.checked = w.ForwardClientAddress !== false;
            dns.input.checked = !!w.DnsEnabled;
            dnsName.input.value = w.DnsName || '';
            dnsUp.input.value = w.DnsUpstreams || '';
            peerDown.input.value = w.PeerDownloadMbps || 0;
            peerUp.input.value = w.PeerUploadMbps || 0;
            peerData.setValue({ bytes: w.PeerDataLimit || 0, period: w.PeerDataPeriod });
            keepalive.input.value = w.Keepalive != null ? w.Keepalive : 25;
            mtu.input.value = w.Mtu || 1420;
            connect.input.checked = !!w.ConnectEnabled;
            connDown.input.value = w.ConnectionDownloadMbps || 0;
            connUp.input.value = w.ConnectionUploadMbps || 0;
            connData.setValue({ bytes: w.ConnectionDataLimit || 0, period: w.ConnectionDataPeriod });
            reflectDns();
            loadSidecar();
        },
        apply: function (config) {
            var w = config.WireGuard = config.WireGuard || {};
            w.Enabled = !!enabled.input.checked;
            w.HostEnabled = !!host.input.checked;
            w.PublicEndpoint = endpoint.input.value.trim();
            w.ListenPort = readCount(port.input, 51820);
            w.TunnelSubnet = subnet.input.value.trim();
            w.UsePresharedKeys = !!psk.input.checked;
            w.ForwardClientAddress = !!forward.input.checked;
            w.DnsEnabled = !!dns.input.checked;
            w.DnsName = dnsName.input.value.trim();
            w.DnsUpstreams = dnsUp.input.value.trim();
            w.PeerDownloadMbps = readCount(peerDown.input, 0);
            w.PeerUploadMbps = readCount(peerUp.input, 0);
            w.PeerDataLimit = peerData.getValue().bytes;
            w.PeerDataPeriod = normalizePeriod(peerData.getValue().period);
            w.Keepalive = readCount(keepalive.input, 25);
            w.Mtu = readCount(mtu.input, 1420);
            w.ConnectEnabled = !!connect.input.checked;
            w.ConnectionDownloadMbps = readCount(connDown.input, 0);
            w.ConnectionUploadMbps = readCount(connUp.input, 0);
            w.ConnectionDataLimit = connData.getValue().bytes;
            w.ConnectionDataPeriod = normalizePeriod(connData.getValue().period);
        }
    };
}

// tunnelItems
// Turns the running tunnels into rows: the host and each connection, with its state and last error.
//
// Inputs
// tunnels [object[]]: The answer of WireGuard/Status.
// t [object]: The strings.
//
// Output
// [object[]]: The rows for a record list.
export function tunnelItems(tunnels, t) {
    return (tunnels || []).map(function (tunnel) {
        var color = tunnel.State === 'Running' ? 'green' : tunnel.State === 'Failed' ? 'red' : 'purple';
        var connected = (tunnel.Peers || []).filter(function (p) { return p.Connected; }).length;
        return {
            id: tunnel.Key,
            title: tunnel.Key === 'host' ? t.tr('WireGuardTabHost') : tunnel.Name,
            secondary: tunnel.Error || t.format('WireGuardTunnelConnected', connected),
            tags: [{ label: tunnelStateLabel(t, tunnel.State), color: color }]
        };
    });
}

// allowanceText
// Says what a user's groups give them, such as "2 of 3 peers, 500 GB/month together".
//
// Inputs
// row [object]: A row of WireGuard/Users.
// shared [object]: The Necto helper bag.
//
// Output
// [string]: The allowance.
export function allowanceText(row, shared) {
    var t = shared.t;
    if (row.IsAdministrator) return t.format('WireGuardAllowanceAdmin', row.Used);
    if (!row.MayHavePeers) return t.format('WireGuardAllowanceNone', row.Used);
    var count = row.MaxPeers > 0 ? t.format('WireGuardAllowanceCount', row.Used, row.MaxPeers) : t.format('WireGuardAllowanceAny', row.Used);
    return row.DataLimit > 0 ? count + ', ' + dataText(t, shared, row.DataLimit, row.DataPeriod) : count;
}

// buildTroubleshooting
// Fills the WireGuard section of Troubleshooting: the tunnels with their state and errors, Restart tunnels, and each
// user's peers against what their groups give.
//
// Inputs
// content [HTMLElement]: The section's content.
// ctx [object]: shared is the Necto helper bag.
//
// Output
// [object]: A handle with load and destroy.
export function buildTroubleshooting(content, ctx) {
    var shared = ctx.shared;
    var t = shared.t;
    var status = el('div', { class: 'stolon-status-text', role: 'status', 'data-role': 'status' });
    var restart = el('button', { is: 'emby-button', type: 'button', class: 'raised stolon-button secondary small', 'data-action': 'restart' }, [el('span', { text: t.tr('Restart') })]);
    var tunnelList = nectoModule.createRecordList({ label: t.tr('WireGuardHeaderTunnels'), emptyText: t.tr('WireGuardEmptyTunnels') });
    var userList = nectoModule.createRecordList({ label: t.tr('WireGuardHeaderPeersByUser'), emptyText: t.tr('ScriptsEmptyUsers') });

    [
        status,
        el('h3', { class: 'stolon-subsection-title', text: t.tr('WireGuardHeaderTunnels') }),
        tunnelList.element,
        el('div', { class: 'stolon-reset-group' }, [el('div', { class: 'stolon-reset-row' }, [
            el('div', { class: 'stolon-reset-info' }, [el('div', { text: t.tr('WireGuardRestartTitle') }), el('div', { class: 'fieldDescription', text: t.tr('WireGuardRestartHelp') })]),
            restart
        ])]),
        el('h3', { class: 'stolon-subsection-title', text: t.tr('WireGuardHeaderPeersByUser') }),
        el('div', { class: 'fieldDescription', text: t.tr('WireGuardPeersByUserHelp') }),
        userList.element
    ].forEach(function (node) { content.appendChild(node); });

    function report(message, isError) {
        shared.setStatus(status, message, !!isError, { timeout: 10000 });
    }

    function load() {
        shared.request('WireGuard/Status').then(function (tunnels) { tunnelList.setItems(tunnelItems(tunnels, t)); }).catch(function () {
            report(t.tr('WireGuardErrorLoad'), true);
        });
        shared.request('WireGuard/Users').then(function (rows) {
            userList.setItems((rows || []).map(function (row) {
                return { id: row.UserId, title: row.Name, secondary: allowanceText(row, shared) };
            }));
        }).catch(function () { report(t.tr('WireGuardErrorLoad'), true); });
    }

    restart.addEventListener('click', function () {
        restart.disabled = true;
        shared.request('WireGuard/Restart', 'POST').then(function () {
            report(t.tr('WireGuardToastRestarted'), false);
            load();
        }).catch(function (err) {
            return shared.errorText(err, t.tr('WireGuardErrorRestart')).then(function (text) { report(text, true); });
        }).finally(function () { restart.disabled = false; });
    });

    return {
        load: load,
        destroy: function () {
            tunnelList.destroy();
            userList.destroy();
        }
    };
}

nectoModule.registerSection('settings', 'wireguard', { build: buildSettings });
nectoModule.registerSection('troubleshooting', 'wireguard', { build: buildTroubleshooting });
