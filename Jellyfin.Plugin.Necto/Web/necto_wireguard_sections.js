// necto_wireguard_sections.js
// The WireGuard sections of Settings and Troubleshooting, which this module adds to the registry when it loads.
// Settings holds the master switch with the wireguard-go download, the host, its DNS and limits, and the connection
// defaults. Troubleshooting holds the tunnels' state, a restart, and each user's peers against their groups.

import * as nectoModule from './configurationpage?name=necto_shared.js';
import { readCount, createDataLimitField, normalizePeriod, dataText, tunnelStateLabel } from './configurationpage?name=necto_wireguard_common.js';

var h = nectoModule.h;

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
    // Only the label changes, so the button keeps its own span for the next label.
    var spans = button ? button.querySelectorAll('span') : [];
    var labelEl = spans.length ? spans[spans.length - 1] : button;
    var label = labelEl ? labelEl.textContent : '';
    if (button) { button.disabled = true; labelEl.textContent = shared.t.tr('WireGuardDownloading'); }
    return shared.request('WireGuard/Sidecar/Download', 'POST').finally(function () {
        if (button) { button.disabled = false; labelEl.textContent = label; }
    });
}

// labelled
// Puts a control under a label, with an optional description.
function labelled(t, label, control, help) {
    return h('div', { class: 'inputContainer' }, [
        h('label', { text: t.tr(label) }),
        control,
        help ? h('div', { class: 'fieldDescription', text: t.tr(help) }) : null
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
    var status = h('div', { class: 'stolon-status-text', role: 'status', 'data-role': 'status' });
    var enabled = nectoModule.createCheckboxField({ label: t.tr('WireGuardLabelEnable'), help: t.tr('WireGuardEnabledHelp') });
    var sidecarLine = h('div', { class: 'fieldDescription', 'data-role': 'sidecar' });
    var replaceRow = nectoModule.createResetRow({ icon: 'swap_horiz', label: t.tr('WireGuardTitleReplace'), help: t.tr('WireGuardSidecarHelp'), button: t.tr('WireGuardButtonReplace'), action: 'replace-sidecar' });
    var replace = replaceRow.button;
    var info = null;

    var host = nectoModule.createCheckboxField({ label: t.tr('WireGuardLabelHostEnabled'), help: t.tr('WireGuardHostEnabledHelp') });
    var endpoint = nectoModule.createInputField({ label: t.tr('WireGuardLabelPublicEndpoint'), help: t.tr('WireGuardPublicEndpointHelp'), placeholder: 'vpn.example.com', autocomplete: 'off' });
    var port = nectoModule.createInputField({ label: t.tr('WireGuardLabelListenPort'), type: 'number', min: 0, help: t.tr('WireGuardListenPortHelp'), autocomplete: 'off' });
    var subnet = nectoModule.createInputField({ label: t.tr('WireGuardLabelTunnelSubnet'), help: t.tr('WireGuardTunnelSubnetHelp'), placeholder: '10.66.0.0/24', autocomplete: 'off' });
    var keyField = nectoModule.createCopyField({ label: t.tr('WireGuardLabelHostPublicKey'), copyLabel: t.tr('WireGuardLabelHostPublicKey'), mono: true });
    var keyRow = labelled(t, 'WireGuardLabelHostPublicKey', keyField.element);
    var psk = nectoModule.createCheckboxField({ label: t.tr('WireGuardLabelPresharedKeys'), help: t.tr('WireGuardPresharedKeysHelp') });
    var forward = nectoModule.createCheckboxField({ label: t.tr('WireGuardLabelForwardClientAddress'), help: t.tr('WireGuardForwardClientAddressHelp') });
    var dns = nectoModule.createCheckboxField({ label: t.tr('WireGuardLabelDnsEnabled'), help: t.tr('WireGuardDnsEnabledHelp') });
    var dnsName = nectoModule.createInputField({ label: t.tr('LabelName'), help: t.tr('WireGuardDnsNameHelp'), placeholder: 'jellyfin.internal', autocomplete: 'off' });
    var dnsUp = nectoModule.createInputField({ label: t.tr('WireGuardLabelDnsUpstreams'), help: t.tr('WireGuardDnsUpstreamsHelp'), placeholder: '1.1.1.1, 1.0.0.1', autocomplete: 'off' });
    var dnsFields = h('div', { class: 'stolon-indent' }, [dnsName.element, dnsUp.element]);
    var peerDown = nectoModule.createInputField({ label: t.tr('WireGuardLabelDownloadMbps'), type: 'number', min: 0, autocomplete: 'off' });
    var peerUp = nectoModule.createInputField({ label: t.tr('WireGuardLabelUploadMbps'), type: 'number', min: 0, autocomplete: 'off' });
    var peerData = createDataLimitField(t, null, t.tr('WireGuardLabelPeerDataLimit'));
    var keepalive = nectoModule.createInputField({ label: t.tr('WireGuardLabelKeepalive'), type: 'number', min: 0, help: t.tr('WireGuardKeepaliveHelp'), autocomplete: 'off' });
    var mtu = nectoModule.createInputField({ label: t.tr('WireGuardLabelMtu'), type: 'number', min: 0, help: t.tr('WireGuardMtuHelp'), autocomplete: 'off' });
    var connect = nectoModule.createCheckboxField({ label: t.tr('WireGuardLabelConnectEnabled'), help: t.tr('WireGuardConnectEnabledHelp') });
    var connDown = nectoModule.createInputField({ label: t.tr('WireGuardLabelDownloadMbps'), type: 'number', min: 0, autocomplete: 'off' });
    var connUp = nectoModule.createInputField({ label: t.tr('WireGuardLabelUploadMbps'), type: 'number', min: 0, autocomplete: 'off' });
    var connData = createDataLimitField(t, null, t.tr('WireGuardLabelConnectionDataLimit'));

    // part
    // Builds a closed collapsible part of the section, led by its icon tile, holding the nodes given.
    function part(title, icon, nodes) {
        var fold = nectoModule.createCollapsibleSection({ title: t.tr(title), icon: icon, collapsed: true, content: h('div', { class: 'stolon-stack' }, nodes) });
        fold.element.setAttribute('data-part', title);
        return fold.element;
    }

    [
        status,
        enabled.element,
        part('WireGuardHeaderSidecar', 'download', [
            sidecarLine,
            nectoModule.createResetGroup([
                replaceRow
            ]).element
        ]),
        part('LabelServerHost', 'router', [host.element, endpoint.element, port.element, subnet.element]),
        part('WireGuardHeaderSecurity', 'key', [keyRow, psk.element, forward.element]),
        part('WireGuardHeaderDns', 'language', [dns.element, dnsFields]),
        part('WireGuardHeaderPeerLimits', 'data_usage', [
            h('div', { class: 'fieldDescription', text: t.tr('WireGuardPeerLimitsHelp') }),
            peerDown.element, peerUp.element, peerData.element
        ]),
        part('TabAdvanced', 'tune', [keepalive.element, mtu.element]),
        part('HeaderConnections', 'cable', [
            connect.element,
            h('div', { class: 'fieldDescription', text: t.tr('WireGuardConnectionLimitsHelp') }),
            connDown.element, connUp.element, connData.element
        ])
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
        (replace.querySelector('span') || replace).textContent = t.tr(info && info.Installed ? 'WireGuardButtonReplace' : 'Download');
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
            title: tunnel.Key === 'host' ? t.tr('LabelServerHost') : tunnel.Name,
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
    var status = h('div', { class: 'stolon-status-text', role: 'status', 'data-role': 'status' });
    var restartRow = nectoModule.createResetRow({ icon: 'autorenew', label: t.tr('WireGuardRestartTitle'), help: t.tr('WireGuardRestartHelp'), button: t.tr('Restart'), action: 'restart' });
    var restart = restartRow.button;
    var tunnelList = nectoModule.createRecordList({ label: t.tr('WireGuardHeaderTunnels'), emptyText: t.tr('WireGuardEmptyTunnels') });
    var userList = nectoModule.createRecordList({ label: t.tr('WireGuardHeaderPeersByUser'), emptyText: t.tr('EmptyUsers') });

    [
        status,
        h('h3', { class: 'stolon-subsection-title', text: t.tr('WireGuardHeaderTunnels') }),
        tunnelList.element,
        nectoModule.createResetGroup([
            restartRow
        ]).element,
        h('h3', { class: 'stolon-subsection-title', text: t.tr('WireGuardHeaderPeersByUser') }),
        h('div', { class: 'fieldDescription', text: t.tr('WireGuardPeersByUserHelp') }),
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
