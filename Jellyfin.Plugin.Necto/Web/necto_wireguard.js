// necto_wireguard.js
// The WireGuard tool. It counts the peers and connections, lists them with their state, usage, and limits, and opens
// each in a sidecar with its details, config file, and QR code, and an editor sidecar to add or change one. Who may
// have peers, how many, and their data limit are set on groups, and the tool's settings are in Settings.

import * as nectoModule from './configurationpage?name=necto_shared.js';
import {
    compact, readLimit, createDataLimitField, normalizePeriod, dataText, limitText, speedText, periodLabel,
    parseWgConfig, parseForwards, safeFileName, stateTag, tunnelStateLabel
} from './configurationpage?name=necto_wireguard_common.js';

var h = nectoModule.h;

// peerLimit
// A peer's data limit and its period, its own or the host's default.
//
// Inputs
// cfg [object]: The WireGuard settings.
// p [object]: The peer.
//
// Output
// [object]: { bytes, period }.
export function peerLimit(cfg, p) {
    return p.DataLimit != null
        ? { bytes: p.DataLimit, period: normalizePeriod(p.DataPeriod) }
        : { bytes: cfg.PeerDataLimit || 0, period: normalizePeriod(cfg.PeerDataPeriod) };
}

// connectionLimit
// A connection's data limit and its period, its own or the Connect default.
//
// Inputs
// cfg [object]: The WireGuard settings.
// c [object]: The connection.
//
// Output
// [object]: { bytes, period }.
export function connectionLimit(cfg, c) {
    return c.DataLimit != null
        ? { bytes: c.DataLimit, period: normalizePeriod(c.DataPeriod) }
        : { bytes: cfg.ConnectionDataLimit || 0, period: normalizePeriod(cfg.ConnectionDataPeriod) };
}

// countCards
// Counts what the cards at the top show: peers, connections, how many are connected, and how many are left out.
//
// Inputs
// cfg [object]: The WireGuard settings.
// ends [object[]]: The tunnel ends from WireGuard/Peers.
//
// Output
// [object]: { peers, connections, connected, over, refused }.
export function countCards(cfg, ends) {
    var list = ends || [];
    return {
        peers: (cfg.Peers || []).length,
        connections: (cfg.Connections || []).length,
        connected: list.filter(function (e) { return e.Kind !== 'Host' && e.Connected; }).length,
        over: list.filter(function (e) { return e.State === 'OverLimit'; }).length,
        refused: list.filter(function (e) { return e.State === 'NotAllowed' || e.State === 'OverPeerCount'; }).length
    };
}

// connectCard
// The Connect card. It follows the Connect to hosts switch, and reads Failed while any connection's tunnel failed.
//
// Inputs
// t [object]: The strings.
// cfg [object]: The WireGuard settings.
// tunnels [object[]]: The running tunnels from WireGuard/Status.
//
// Output
// [object]: A card, or null when Connect is off and there are no connections.
export function connectCard(t, cfg, tunnels) {
    if (!cfg.ConnectEnabled && !(cfg.Connections || []).length) return null;
    var failed = (tunnels || []).some(function (x) { return x.Key !== 'host' && x.State === 'Failed'; });
    var label = t.tr('WireGuardLabelConnectEnabled');
    if (!cfg.ConnectEnabled) return { count: t.tr('StateInactive'), label: label, color: 'gray' };
    return failed ? { count: t.tr('LabelFailed'), label: label, color: 'red' } : { count: t.tr('StateActive'), label: label, color: 'green' };
}

export default function (view) {
    'use strict';

    var shared = null;
    var page = null;
    var t = null;
    var cfg = null;
    var users = [];
    var ends = [];
    var tunnels = [];
    var usage = { Bytes: {}, Blocked: [], Refused: {} };
    var sidecar = null;
    var peerList = null;
    var connectionList = null;
    var loadSeq = 0;
    var serverName = '';
    var SELF_ID = 'self';

    function $(id) { return view.querySelector('#' + id); }

    // report
    // Shows a result on the page's status line.
    function report(message, isError) {
        shared.setStatus('wireguardStatus', message, !!isError, { timeout: 10000 });
    }

    function userName(id) {
        var u = users.filter(function (x) { return compact(x.Id) === compact(id); })[0];
        return u ? u.Name : t.tr('WireGuardUnknownUser');
    }

    function endOf(id) { return ends.filter(function (e) { return compact(e.Id) === compact(id); })[0] || {}; }

    function bytesOf(id, period) { return ((usage.Bytes || {})[compact(id)] || {})[period || 'Month'] || 0; }

    function peerState(p) {
        return ((tunnels.filter(function (x) { return x.Key === 'host'; })[0] || { Peers: [] }).Peers || [])
            .filter(function (x) { return x.PublicKey === p.PublicKey; })[0] || {};
    }

    function connectionState(c) {
        var tunnel = tunnels.filter(function (x) { return x.Key === compact(c.Id); })[0];
        return tunnel ? Object.assign({ State: tunnel.State, Error: tunnel.Error }, (tunnel.Peers || [])[0] || {}) : {};
    }

    // usageAside
    // A Usage Meter of a limited peer or connection at the end of its row, which turns orange then red as it nears
    // its limit. A record with no limit gets none.
    function usageAside(id, limit) {
        if (!(limit.bytes > 0)) return null;
        var meter = nectoModule.createUsageMeter({
            label: t.tr('LabelUsage'),
            used: bytesOf(id, limit.period),
            total: limit.bytes,
            bytes: true,
            usedText: t.tr('WireGuardUsedOfTotal') + '/' + periodLabel(t, limit.period).toLowerCase()
        });
        return h('div', { class: 'necto-wireguard-meter' }, [meter.element]);
    }

    // usedText
    // What a peer or connection used in its limit's window, such as 1.2 GB of 10 GB/month.
    function usedText(id, limit) {
        var used = shared.formatSize(bytesOf(id, limit.bytes > 0 ? limit.period : 'Month'));
        return limit.bytes > 0
            ? t.tr('WireGuardUsedOfTotal').replace('{used}', used).replace('{total}', dataText(t, shared, limit.bytes, limit.period))
            : t.tr('WireGuardUsageThisMonth').replace('{0}', used);
    }

    // section
    // Draws a list in a collapsible section of the page, open by default.
    function section(holder, title, icon, list) {
        holder.innerHTML = '';
        var made = nectoModule.createCollapsibleSection({ title: title, icon: icon, collapsed: false, content: list.element });
        holder.appendChild(made.element);
        return made;
    }

    // render
    // Draws the cards, the add buttons, and both lists from what the page last loaded.
    function render() {
        var w = cfg;
        var on = !!w.Enabled;
        shared.setVisible('wireguardOff', !on);
        $('wireguardOff').innerHTML = on ? '' : shared.emptySection(t.tr('WireGuardEmptyOff'));
        var noSidecar = $('wireguardNoSidecar');
        if (!noSidecar.querySelector('.stolon-banner')) {
            var warning = noSidecar.textContent.trim();
            noSidecar.textContent = '';
            noSidecar.appendChild(nectoModule.createBanner({ kind: 'warn', text: warning, dismissible: false }).element);
        }
        shared.setVisible('wireguardNoSidecar', on && sidecar && !sidecar.Installed);
        shared.setVisible('wireguardBody', on || (w.Peers || []).length > 0 || (w.Connections || []).length > 0);

        var counts = countCards(w, ends);
        var host = ends.filter(function (e) { return e.Kind === 'Host'; })[0];
        var cards = [];
        if (w.HostEnabled) cards.push({ count: tunnelStateLabel(t, host ? host.State : 'Stopped'), label: t.tr('LabelServerHost'), color: host && host.State === 'Running' ? 'green' : host && host.State === 'Failed' ? 'red' : 'gray' });
        var connect = connectCard(t, w, tunnels);
        if (connect) cards.push(connect);
        cards.push({ count: counts.peers, label: t.tr('HeaderPeers') });
        cards.push({ count: counts.connections, label: t.tr('HeaderConnections') });
        cards.push({ count: counts.connected, label: t.tr('StateConnected'), color: counts.connected ? 'green' : 'gray' });
        cards.push({ count: counts.over, label: t.tr('WireGuardTagOverLimit'), color: counts.over ? 'red' : 'gray' });
        if (counts.refused) cards.push({ count: counts.refused, label: t.tr('WireGuardCardLeftOut'), color: 'orange' });
        shared.renderCards('wireguardCards', cards);

        var hostTunnel = tunnels.filter(function (x) { return x.Key === 'host'; })[0];
        var error = hostTunnel && hostTunnel.State !== 'Running' ? hostTunnel.Error : (on && w.HostEnabled && !hostTunnel && sidecar && sidecar.Installed ? t.tr('WireGuardErrorNoTunnel') : '');
        $('hostError').textContent = error || '';
        shared.setVisible('hostError', !!error);

        shared.setVisible('btnAddPeer', on && !!w.HostEnabled);
        shared.setVisible('btnAddConnection', on && !!w.ConnectEnabled);
        shared.setVisible('addButtons', on && (!!w.HostEnabled || !!w.ConnectEnabled));
        var bothOff = on && !w.HostEnabled && !w.ConnectEnabled && !(w.Peers || []).length && !(w.Connections || []).length;
        $('wireguardBothOff').innerHTML = bothOff ? shared.emptySection(t.tr('WireGuardEmptyBothOff')) : '';
        shared.setVisible('wireguardBothOff', bothOff);

        // A narrow list, such as on a phone, drops each row's tunnel address.
        var narrow = !!(globalThis.matchMedia && globalThis.matchMedia('(max-width: 600px)').matches);
        peerList.setItems((w.Peers || []).map(function (p) {
            var end = endOf(p.Id);
            var tags = [stateTag(t, end.State || (p.Enabled ? 'Offline' : 'Disabled'))];
            if (!narrow) tags.push({ label: p.Address, icon: 'lan' });
            if (end.RemoteAddress) tags.push({ label: end.RemoteAddress, icon: 'place' });
            if ((p.RoutedSubnets || []).length) tags.push({ label: t.tr('WireGuardTagRouter'), icon: 'router' });
            return { id: p.Id, title: p.Name || p.Address, secondary: userName(p.UserId), tags: tags, aside: usageAside(p.Id, peerLimit(w, p)) };
        }));
        // This server leads the connections while it hosts, placed by its public address. It opens Settings.
        var self = [];
        if (on && w.HostEnabled) {
            var hostState = host ? host.State : 'Stopped';
            self.push({
                id: SELF_ID,
                title: serverName || t.tr('LabelServerHost'),
                secondary: w.PublicEndpoint || '-',
                tags: [stateTag(t, hostState === 'Running' ? 'Connected' : hostState === 'Failed' ? 'Failed' : 'Offline'), { label: t.tr('LabelServerHost'), icon: 'dns' }]
            });
        }
        connectionList.setItems(self.concat((w.Connections || []).map(function (c) {
            var end = endOf(c.Id);
            var tags = [stateTag(t, end.State || (c.Enabled ? 'Offline' : 'Disabled'))];
            (c.Forwards || []).forEach(function (f) { tags.push({ label: '127.0.0.1:' + f.LocalPort, icon: 'swap_horiz' }); });
            return { id: c.Id, title: c.Name || c.Endpoint, secondary: c.Endpoint, tags: tags, aside: usageAside(c.Id, connectionLimit(w, c)) };
        })));
        shared.setVisible('peerSection', !!w.HostEnabled || (w.Peers || []).length > 0);
        shared.setVisible('connectionSection', !!w.ConnectEnabled || (w.Connections || []).length > 0);
    }

    // load
    // Reads the settings, the users, the tunnels, and the usage, then draws the page. An answer that lands after a
    // newer load is dropped.
    function load() {
        var mine = ++loadSeq;
        return Promise.all([
            shared.getConfig(),
            ApiClient.getUsers(),
            shared.request('WireGuard/Peers').catch(function () { return []; }),
            shared.request('WireGuard/Status').catch(function () { return []; }),
            shared.request('WireGuard/Usage').catch(function () { return { Bytes: {}, Blocked: [], Refused: {} }; }),
            shared.request('WireGuard/Sidecar').catch(function () { return null; }),
            ApiClient.getPublicSystemInfo().catch(function () { return {}; })
        ]).then(function (r) {
            if (mine !== loadSeq) return;
            cfg = (r[0] && r[0].WireGuard) || {};
            users = (r[1] || []).map(function (u) { return { Id: u.Id, Name: u.Name }; });
            ends = r[2] || [];
            tunnels = r[3] || [];
            usage = r[4] || usage;
            sidecar = r[5];
            serverName = (r[6] && r[6].ServerName) || '';
            render();
        }).catch(function () {
            if (mine === loadSeq) report(t.tr('WireGuardErrorLoad'), true);
        });
    }

    // fetchPeerConfig
    // Reads a peer's config file as text.
    function fetchPeerConfig(id) {
        return ApiClient.fetch({ url: ApiClient.getUrl('WireGuard/Peers/' + id + '/Config'), type: 'GET', dataType: 'text' });
    }

    function downloadText(fileName, text) {
        var url = URL.createObjectURL(new Blob([text], { type: 'text/plain' }));
        var a = document.createElement('a');
        a.href = url;
        a.download = fileName;
        document.body.appendChild(a);
        a.click();
        a.remove();
        setTimeout(function () { URL.revokeObjectURL(url); }, 1000);
    }

    function subsection(key) { return h('h3', { class: 'stolon-subsection-title', text: t.tr(key) }); }

    // openPeer
    // Opens a peer's details in a sidecar: its state, owner, address, handshake, source, usage, and limits, its
    // config file, and a QR code of it, with Edit and Download.
    function openPeer(id) {
        var peer = (cfg.Peers || []).filter(function (p) { return p.Id === id; })[0];
        if (!peer) return;
        var state = peerState(peer);
        var end = endOf(peer.Id);
        var tag = stateTag(t, end.State || 'Offline');
        var body = h('div');
        body.appendChild(subsection('LabelDetails'));
        body.appendChild(nectoModule.createDefinitionList({
            emptyText: '-',
            items: [
                { label: t.tr('LabelStatus'), value: { text: '', badge: { label: tag.label, color: tag.color || 'gray' } } },
                { label: t.tr('WireGuardLabelOwner'), value: userName(peer.UserId) },
                { label: t.tr('LabelAddress'), value: peer.Address, mono: true },
                { label: t.tr('WireGuardLabelRoutedSubnets'), value: (peer.RoutedSubnets || []).join(', ') || null, mono: true },
                { label: t.tr('WireGuardLabelLastHandshake'), value: state.LastHandshakeUtc ? shared.formatDate(state.LastHandshakeUtc) : null },
                { label: t.tr('LabelSource'), value: state.Endpoint || null, mono: true },
                { label: t.tr('LabelUsage'), value: usedText(peer.Id, peerLimit(cfg, peer)) },
                { label: t.tr('WireGuardLabelSpeedLimit'), value: speedText(peer.DownloadMbps, cfg.PeerDownloadMbps, peer.UploadMbps, cfg.PeerUploadMbps) }
            ]
        }).element);
        var qrHost = h('div');
        var configHost = h('div');
        body.appendChild(subsection('WireGuardHeaderQrCode'));
        body.appendChild(qrHost);
        body.appendChild(subsection('WireGuardLabelConfigFile'));
        body.appendChild(configHost);
        var configText = null;
        fetchPeerConfig(peer.Id).then(function (text) {
            configText = text;
            qrHost.appendChild(nectoModule.createQrCode({ value: text, showValue: false, label: t.tr('WireGuardQrLabel'), fileName: safeFileName(peer.Name) + '.png' }).element);
            configHost.appendChild(h('pre', { class: 'stolon-code stolon-code-block', text: text }));
        }, function () {
            qrHost.innerHTML = shared.emptySection(t.tr('WireGuardErrorNoPeerConfig'));
        });

        nectoModule.openSidecar({
            title: peer.Name || peer.Address,
            body: body,
            size: 'large',
            page: page,
            buttons: [
                { text: t.tr('Edit'), icon: 'edit', className: 'stolon-button secondary', action: function (close) { close(); editPeer(peer); } },
                { text: t.tr('Download'), icon: 'download', className: 'stolon-button primary', action: function () { if (configText) downloadText(safeFileName(peer.Name) + '.conf', configText); } }
            ]
        });
    }

    // field
    // A labelled text or number input, with an optional description and placeholder.
    function field(labelKey, type, value, helpKey, placeholder) {
        var id = 'necto-wg-' + Math.random().toString(36).slice(2);
        var input = h('input', { is: 'emby-input', type: type, id: id, autocomplete: 'off', min: type === 'number' ? '0' : null, placeholder: placeholder || null });
        input.value = value == null ? '' : value;
        return {
            element: h('div', { class: 'inputContainer' }, [
                h('label', { for: id, text: t.tr(labelKey) }),
                input,
                helpKey ? h('div', { class: 'fieldDescription', text: t.tr(helpKey) }) : null
            ]),
            input: input
        };
    }

    // textArea
    // A labelled code area with a description.
    function textArea(labelKey, helpKey, value, placeholder, rows) {
        var id = 'necto-wg-' + Math.random().toString(36).slice(2);
        var area = h('textarea', { class: 'stolon-code stolon-code-block', id: id, rows: String(rows || 3), spellcheck: 'false', placeholder: placeholder || null });
        area.value = value || '';
        return {
            element: h('div', { class: 'inputContainer' }, [
                h('label', { for: id, text: t.tr(labelKey) }),
                area,
                h('div', { class: 'fieldDescription', text: t.tr(helpKey) })
            ]),
            input: area
        };
    }

    // labelled
    // Puts a control under a label, as a form field.
    function labelled(labelKey, control, helpKey) {
        return h('div', { class: 'inputContainer' }, [
            h('label', { text: t.tr(labelKey) }),
            control,
            helpKey ? h('div', { class: 'fieldDescription', text: t.tr(helpKey) }) : null
        ]);
    }

    // collapsible
    // A collapsible section holding a group of an editor's fields, closed unless it is the first.
    function collapsible(titleKey, children, open) {
        var content = h('div', {}, children.map(function (c) { return c.element || c; }));
        return nectoModule.createCollapsibleSection({ title: t.tr(titleKey), content: content, collapsed: !open }).element;
    }

    // inheritSpeed
    // A speed limit in Mbps that takes the default until it is overridden.
    function inheritSpeed(labelKey, own, inherited) {
        var input = h('input', { is: 'emby-input', type: 'number', min: '0' });
        var made = nectoModule.createInheritField({
            field: input,
            inheritedValue: inherited || 0,
            inheritedText: limitText(null, inherited || 0, 'Mbps'),
            value: { inherit: own == null, value: own == null ? (inherited || 0) : own }
        });
        return {
            element: labelled(labelKey, made.element),
            read: function () {
                var v = made.getValue();
                if (v.inherit) return null;
                var n = parseInt(v.value, 10);
                return isNaN(n) || n < 0 ? 0 : n;
            }
        };
    }

    // inheritData
    // A data limit and its period that takes the default until it is overridden.
    function inheritData(own, inherited) {
        var limit = createDataLimitField(t, own || inherited);
        var made = nectoModule.createInheritField({
            field: limit,
            inheritedValue: inherited,
            inheritedText: dataText(t, shared, inherited.bytes, inherited.period),
            value: { inherit: !own, value: own || inherited }
        });
        return {
            element: labelled('WireGuardLabelDataLimit', made.element),
            read: function () {
                var v = made.getValue();
                return v.inherit ? null : v.value;
            }
        };
    }

    // saveRecord
    // Sends a peer or connection to its route, and reloads the page once it is stored.
    //
    // Output
    // [Promise]: Resolves true once saved, and false with the reason on the status line to keep the sidecar open.
    function saveRecord(route, isNew, id, values, status) {
        var request = isNew ? shared.request(route, 'POST', values) : shared.request(route + '/' + id, 'PUT', values);
        return request.then(function () {
            report(t.tr('ToastSaved'), false);
            load();
            return true;
        }).catch(function (err) {
            return shared.errorText(err, t.tr('WireGuardErrorSave')).then(function (text) {
                shared.setStatus(status, text, true);
                return false;
            });
        });
    }

    // removeRecord
    // Asks first, then removes a peer or connection and closes its sidecar.
    function removeRecord(route, id, name, messageKey, close) {
        nectoModule.confirmDialog(t.tr(messageKey), {
            title: t.format('TitleRemoveName', name),
            confirmText: t.tr('ButtonRemove'),
            destructive: true
        }).then(function (yes) {
            if (!yes) return;
            shared.request(route + '/' + id, 'DELETE').then(function () {
                close('removed');
                report(t.format('ToastRemovedCount', name), false);
                load();
            }).catch(function (err) {
                shared.errorText(err, t.tr('WireGuardErrorSave')).then(function (text) { report(text, true); });
            });
        });
    }

    // editPeer
    // Opens a peer, or a new one, in an editor sidecar with its Peer, Limits, and Advanced sections.
    function editPeer(existing) {
        var isNew = !existing;
        var peer = existing || { UserId: ApiClient.getCurrentUserId(), Name: '', Address: '', RoutedSubnets: [], Enabled: true, PublicKey: '' };
        var status = h('div', { class: 'stolon-status-text', role: 'status' });
        var name = field('LabelName', 'text', peer.Name, null, t.tr('WireGuardPlaceholderPeerName'));
        var owner = shared.createUserSelector({ value: peer.UserId, includeEmptyOption: false });
        var address = field('LabelAddress', 'text', peer.Address, null, t.tr('WireGuardPlaceholderNextFree'));
        var subnets = field('WireGuardLabelRoutedSubnets', 'text', (peer.RoutedSubnets || []).join(', '), 'WireGuardRoutedSubnetsHelp', '192.168.1.0/24');
        var enabled = nectoModule.createCheckbox({ label: t.tr('LabelEnabled'), checked: peer.Enabled !== false });
        var publicKey = field('WireGuardLabelPublicKey', 'text', isNew ? '' : peer.PublicKey, isNew ? 'WireGuardPublicKeyHelp' : null, isNew ? t.tr('WireGuardPlaceholderMadeForYou') : '');
        publicKey.input.readOnly = !isNew;
        var down = inheritSpeed('WireGuardLabelDownloadMbps', peer.DownloadMbps, cfg.PeerDownloadMbps);
        var up = inheritSpeed('WireGuardLabelUploadMbps', peer.UploadMbps, cfg.PeerUploadMbps);
        var data = inheritData(peer.DataLimit != null ? { bytes: peer.DataLimit, period: normalizePeriod(peer.DataPeriod) } : null, { bytes: cfg.PeerDataLimit || 0, period: normalizePeriod(cfg.PeerDataPeriod) });
        var body = h('div', {}, [
            status,
            collapsible('WireGuardHeaderPeer', [name, labelled('WireGuardLabelOwner', owner.element, 'WireGuardOwnerHelp'), address, publicKey, enabled], true),
            collapsible('HeaderLimits', [down, up, data]),
            collapsible('TabAdvanced', [subnets])
        ]);

        function read() {
            var d = data.read();
            return {
                Name: name.input.value.trim(),
                UserId: owner.getValue(),
                Address: address.input.value.trim(),
                RoutedSubnets: subnets.input.value.split(',').map(function (s) { return s.trim(); }).filter(Boolean),
                Enabled: !!enabled.input.checked,
                PublicKey: publicKey.input.value.trim(),
                DownloadMbps: down.read(),
                UploadMbps: up.read(),
                DataLimit: d ? d.bytes : null,
                DataPeriod: d ? d.period : normalizePeriod(peer.DataPeriod)
            };
        }

        nectoModule.openEditorSidecar({
            title: isNew ? t.tr('WireGuardButtonAddPeer') : t.tr('WireGuardTitleEditPeer'),
            body: body,
            page: page,
            isNew: isNew,
            ready: owner.ready,
            serialize: function () { return JSON.stringify(read()); },
            saveText: t.tr('Save'),
            deleteText: t.tr('ButtonRemove'),
            onSave: function () {
                var values = read();
                if (!values.Name) {
                    shared.setStatus(status, t.tr('ErrorEnterName'), true);
                    return false;
                }
                return saveRecord('WireGuard/Peers', isNew, peer.Id, values, status);
            },
            onDelete: function (close) { removeRecord('WireGuard/Peers', peer.Id, peer.Name, 'WireGuardConfirmRemovePeer', close); },
            onClose: function () { if (owner.destroy) owner.destroy(); }
        });
    }

    // openConnection
    // Opens a connection's details in a sidecar, with this server's public key to give the remote host, and Edit.
    function openConnection(id) {
        var c = (cfg.Connections || []).filter(function (x) { return x.Id === id; })[0];
        if (!c) return;
        var state = connectionState(c);
        var end = endOf(c.Id);
        var tag = stateTag(t, end.State || 'Offline');
        var body = h('div');
        body.appendChild(subsection('LabelDetails'));
        body.appendChild(nectoModule.createDefinitionList({
            emptyText: '-',
            items: [
                { label: t.tr('LabelStatus'), value: { text: '', badge: { label: tag.label, color: tag.color || 'gray' } } },
                { label: t.tr('WireGuardLabelRemoteHost'), value: c.Endpoint, mono: true },
                { label: t.tr('WireGuardLabelOurAddress'), value: c.Address, mono: true },
                { label: t.tr('WireGuardLabelForwards'), value: (c.Forwards || []).map(function (f) { return '127.0.0.1:' + f.LocalPort + ' → ' + f.RemoteHost + ':' + f.RemotePort; }).join('\n') || null, mono: true },
                { label: t.tr('WireGuardLabelLastHandshake'), value: state.LastHandshakeUtc ? shared.formatDate(state.LastHandshakeUtc) : null },
                { label: t.tr('WireGuardLabelLastError'), value: state.State === 'Failed' ? state.Error : null },
                { label: t.tr('LabelUsage'), value: usedText(c.Id, connectionLimit(cfg, c)) },
                { label: t.tr('WireGuardLabelSpeedLimit'), value: speedText(c.DownloadMbps, cfg.ConnectionDownloadMbps, c.UploadMbps, cfg.ConnectionUploadMbps) }
            ]
        }).element);
        body.appendChild(subsection('WireGuardLabelOurPublicKey'));
        body.appendChild(nectoModule.createCopyField({ label: t.tr('WireGuardLabelOurPublicKey'), copyLabel: t.tr('WireGuardLabelOurPublicKey'), value: c.PublicKey || '', mono: true }).element);
        body.appendChild(h('div', { class: 'fieldDescription', text: t.tr('WireGuardOurPublicKeyHelp') }));

        nectoModule.openSidecar({
            title: c.Name || c.Endpoint,
            body: body,
            size: 'large',
            page: page,
            buttons: [{ text: t.tr('Edit'), icon: 'edit', className: 'stolon-button primary', action: function (close) { close(); editConnection(c); } }]
        });
    }

    // editConnection
    // Opens a connection, or a new one, in an editor sidecar with its import, Connection, Forwards, Limits, and
    // Advanced sections. A pasted config file fills in the fields.
    function editConnection(existing) {
        var isNew = !existing;
        var c = existing || { Name: '', Endpoint: '', PeerPublicKey: '', Address: '', Keepalive: 25, Enabled: true, Forwards: [] };
        var status = h('div', { class: 'stolon-status-text', role: 'status' });
        var importArea = textArea('WireGuardLabelConfigFile', 'WireGuardImportConfigHelp', '', '', 6);
        var name = field('LabelName', 'text', c.Name, null, t.tr('WireGuardPlaceholderConnectionName'));
        var endpoint = field('WireGuardLabelRemoteHost', 'text', c.Endpoint, null, 'vpn.example.com:51820');
        var peerKey = field('WireGuardLabelRemotePublicKey', 'text', c.PeerPublicKey, null, '');
        var psk = field('WireGuardLabelPresharedKey', 'password', '', null, c.PresharedKey ? t.tr('WireGuardPlaceholderKept') : t.tr('PlaceholderOptional'));
        var address = field('WireGuardLabelOurAddress', 'text', c.Address, null, '10.0.0.5');
        var privateKey = field('LabelPrivateKey', 'password', '', 'WireGuardPrivateKeyHelp', c.PrivateKey ? t.tr('WireGuardPlaceholderKept') : t.tr('WireGuardPlaceholderMadeForYou'));
        var forwards = textArea('WireGuardLabelForwards', 'WireGuardForwardsHelp', (c.Forwards || []).map(function (f) { return f.LocalPort + ' ' + f.RemoteHost + ':' + f.RemotePort; }).join('\n'), '18096 10.0.0.1:8096', 3);
        var enabled = nectoModule.createCheckbox({ label: t.tr('LabelEnabled'), checked: c.Enabled !== false });
        var keepalive = field('WireGuardLabelKeepalive', 'number', c.Keepalive, null, '25');
        var down = inheritSpeed('WireGuardLabelDownloadMbps', c.DownloadMbps, cfg.ConnectionDownloadMbps);
        var up = inheritSpeed('WireGuardLabelUploadMbps', c.UploadMbps, cfg.ConnectionUploadMbps);
        var data = inheritData(c.DataLimit != null ? { bytes: c.DataLimit, period: normalizePeriod(c.DataPeriod) } : null, { bytes: cfg.ConnectionDataLimit || 0, period: normalizePeriod(cfg.ConnectionDataPeriod) });
        var body = h('div', {}, [
            status,
            collapsible('WireGuardHeaderImport', [importArea]),
            collapsible('WireGuardHeaderConnection', [name, endpoint, peerKey, psk, address, privateKey, enabled], true),
            collapsible('WireGuardLabelForwards', [forwards]),
            collapsible('HeaderLimits', [down, up, data]),
            collapsible('TabAdvanced', [keepalive])
        ]);

        importArea.input.addEventListener('input', function () {
            var parsed = parseWgConfig(importArea.input.value);
            if (parsed.Endpoint) endpoint.input.value = parsed.Endpoint;
            if (parsed.PeerPublicKey) peerKey.input.value = parsed.PeerPublicKey;
            if (parsed.PresharedKey) psk.input.value = parsed.PresharedKey;
            if (parsed.Address) address.input.value = parsed.Address;
            if (parsed.PrivateKey) privateKey.input.value = parsed.PrivateKey;
            if (parsed.Keepalive != null) keepalive.input.value = parsed.Keepalive;
        });

        function read() {
            var d = data.read();
            return {
                Name: name.input.value.trim(),
                Endpoint: endpoint.input.value.trim(),
                PeerPublicKey: peerKey.input.value.trim(),
                Address: address.input.value.trim(),
                Forwards: parseForwards(forwards.input.value),
                Enabled: !!enabled.input.checked,
                Keepalive: readLimit(keepalive.input) == null ? 25 : readLimit(keepalive.input),
                DownloadMbps: down.read(),
                UploadMbps: up.read(),
                DataLimit: d ? d.bytes : null,
                DataPeriod: d ? d.period : normalizePeriod(c.DataPeriod),
                NewPresharedKey: psk.input.value.trim(),
                NewPrivateKey: privateKey.input.value.trim()
            };
        }

        nectoModule.openEditorSidecar({
            title: isNew ? t.tr('WireGuardButtonAddConnection') : t.tr('WireGuardTitleEditConnection'),
            body: body,
            page: page,
            isNew: isNew,
            serialize: function () { return JSON.stringify(read()); },
            saveText: t.tr('Save'),
            deleteText: t.tr('ButtonRemove'),
            onSave: function () {
                var values = read();
                if (!values.Name || !values.Endpoint || !values.PeerPublicKey || !values.Address) {
                    shared.setStatus(status, t.tr('WireGuardErrorConnectionFields'), true);
                    return false;
                }
                return saveRecord('WireGuard/Connections', isNew, c.Id, values, status);
            },
            onDelete: function (close) { removeRecord('WireGuard/Connections', c.Id, c.Name, 'WireGuardConfirmRemoveConnection', close); }
        });
    }

    var made = nectoModule.createNectoPage(view, {
        page: 'necto_wireguard',
        bind: function (s, p) {
            t = s.t;
            peerList = nectoModule.createRecordList({ label: t.tr('HeaderPeers'), emptyText: t.tr('WireGuardEmptyPeers'), onOpen: function (item) { openPeer(item.id); } });
            connectionList = nectoModule.createRecordList({ label: t.tr('HeaderConnections'), emptyText: t.tr('WireGuardEmptyConnections'), onOpen: function (item) {
                if (item.id === SELF_ID) nectoModule.openTool(view, 'settings');
                else openConnection(item.id);
            } });
            section($('peerSection'), t.tr('HeaderPeers'), 'devices', peerList);
            section($('connectionSection'), t.tr('HeaderConnections'), 'link', connectionList);
            $('btnAddPeer').addEventListener('click', function () { editPeer(null); });
            $('btnAddConnection').addEventListener('click', function () { editConnection(null); });
            p.addCleanup(function () {
                if (peerList) peerList.destroy();
                if (connectionList) connectionList.destroy();
            });
        },
        load: function () { load(); }
    });
    shared = made.shared;
    page = made.page;
}
