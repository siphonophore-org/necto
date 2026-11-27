// necto_wireguard.js
// The WireGuard tool. It counts the peers and connections, lists them with their state, usage, and limits, and opens
// each in a sidecar with its details, config file, and QR code, and an editor sidecar to add or change one. Who may
// have peers, how many, and their data limit are set on groups, and the tool's settings are in Settings.

import * as nectoModule from './configurationpage?name=necto_shared.js';
import {
    compact, readLimit, createDataLimitField, normalizePeriod, dataText, limitText, speedText,
    parseWgConfig, parseForwards, safeFileName, stateTag, tunnelStateLabel
} from './configurationpage?name=necto_wireguard_common.js';

var h = nectoModule.h;

// TYPE_BADGES
// The color of each kind of row's type badge, and the key of its word.
var TYPE_BADGES = {
    Host: { key: 'LabelServerHost', color: 'gray' },
    Peer: { key: 'WireGuardHeaderPeer', color: 'blue' },
    Connection: { key: 'WireGuardHeaderConnection', color: 'purple' }
};

// tunnelRows
// Turns the peers, the connections, and this server while it hosts into the table's rows, in that order: this server
// first, then the peers, then the connections.
//
// Inputs
// t [object]: The strings.
// cfg [object]: The WireGuard settings.
// ends [object[]]: The tunnel ends from WireGuard/Peers.
// names [object]: user(id) names a peer's owner, used(id, limit) words what a record used, and server is this
//   server's name.
//
// Output
// [object[]]: Rows of { Id, Kind, Name, Line, Detail, State }, State being the state tag.
export function tunnelRows(t, cfg, ends, names) {
    var endOf = function (id) { return (ends || []).filter(function (e) { return compact(e.Id) === compact(id); })[0] || {}; };
    var rows = [];
    if (cfg.Enabled && cfg.HostEnabled) {
        var host = (ends || []).filter(function (e) { return e.Kind === 'Host'; })[0];
        var hostState = host ? host.State : 'Stopped';
        rows.push({
            Id: 'self', Kind: 'Host', Name: names.server || t.tr('LabelServerHost'), Line: cfg.PublicEndpoint || '', Detail: '',
            State: stateTag(t, hostState === 'Running' ? 'Connected' : hostState === 'Failed' ? 'Failed' : 'Offline')
        });
    }
    (cfg.Peers || []).forEach(function (p) {
        var end = endOf(p.Id);
        var detail = [names.used(p.Id, peerLimit(cfg, p))];
        if (end.RemoteAddress) detail.push(end.RemoteAddress);
        if ((p.RoutedSubnets || []).length) detail.push(t.tr('WireGuardTagRouter'));
        rows.push({
            Id: p.Id, Kind: 'Peer', Name: p.Name || p.Address, Line: [names.user(p.UserId), p.Address].filter(Boolean).join(' · '),
            Detail: detail.join(' · '), State: stateTag(t, end.State || (p.Enabled ? 'Offline' : 'Disabled'))
        });
    });
    (cfg.Connections || []).forEach(function (c) {
        var end = endOf(c.Id);
        var detail = [names.used(c.Id, connectionLimit(cfg, c))];
        (c.Forwards || []).forEach(function (f) { detail.push('127.0.0.1:' + f.LocalPort); });
        rows.push({
            Id: c.Id, Kind: 'Connection', Name: c.Name || c.Endpoint, Line: c.Endpoint || '',
            Detail: detail.join(' · '), State: stateTag(t, end.State || (c.Enabled ? 'Offline' : 'Disabled'))
        });
    });
    return rows;
}

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
// Counts what the cards at the top show: the peers and connections, how many of each are connected, and how many
// are over their data limit or left out.
//
// Inputs
// cfg [object]: The WireGuard settings.
// ends [object[]]: The tunnel ends from WireGuard/Peers.
//
// Output
// [object]: { peers, connections, peersConnected, connectionsConnected, over, refused }.
export function countCards(cfg, ends) {
    var list = ends || [];
    var connected = function (kind) { return list.filter(function (e) { return e.Kind === kind && e.Connected; }).length; };
    return {
        peers: (cfg.Peers || []).length,
        connections: (cfg.Connections || []).length,
        peersConnected: connected('Peer'),
        connectionsConnected: connected('Connection'),
        over: list.filter(function (e) { return e.State === 'OverLimit'; }).length,
        refused: list.filter(function (e) { return e.State === 'NotAllowed' || e.State === 'OverPeerCount'; }).length
    };
}

// connectionsCard
// The Connections card: how many connections are connected out of all of them. It is red while any connection's
// tunnel failed, gray while Connect to hosts is off, and green while one is connected.
//
// Inputs
// cfg [object]: The WireGuard settings.
// tunnels [object[]]: The running tunnels from WireGuard/Status.
// counts [object]: What countCards gave.
// t [object]: The strings.
//
// Output
// [object]: The card, or null while Connect to hosts is off and there are no connections.
export function connectionsCard(cfg, tunnels, counts, t) {
    if (!cfg.ConnectEnabled && !counts.connections) return null;
    var failed = (tunnels || []).some(function (x) { return x.Key !== 'host' && x.State === 'Failed'; });
    var color = !cfg.ConnectEnabled ? 'gray' : failed ? 'red' : counts.connectionsConnected ? 'green' : 'blue';
    return { count: counts.connectionsConnected + ' / ' + counts.connections, label: t.tr('HeaderConnections'), color: color };
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
    var table = null;
    var rows = [];
    var loadSeq = 0;
    var serverName = '';

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

    // usedText
    // What a peer or connection used in its limit's window, such as 1.2 GB of 10 GB/month.
    function usedText(id, limit) {
        var used = shared.formatSize(bytesOf(id, limit.bytes > 0 ? limit.period : 'Month'));
        return limit.bytes > 0
            ? t.tr('WireGuardUsedOfTotal').replace('{used}', used).replace('{total}', dataText(t, shared, limit.bytes, limit.period))
            : t.tr('WireGuardUsageThisMonth').replace('{0}', used);
    }

    // render
    // Draws the cards, the add buttons, and both lists from what the page last loaded.
    function render() {
        var w = cfg;
        var on = !!w.Enabled;
        shared.setVisible('wireguardOff', !on);
        // A tool that is off leaves the sidebar the heading takes its name from, so the heading names it itself.
        if (shared.setPageTitle) shared.setPageTitle(on ? '' : t.tr('TabWireGuard'));
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
        if (w.HostEnabled || counts.peers) cards.push({ count: counts.peersConnected + ' / ' + counts.peers, label: t.tr('HeaderPeers'), color: counts.peersConnected ? 'green' : 'blue' });
        var connections = connectionsCard(w, tunnels, counts, t);
        if (connections) cards.push(connections);
        cards.push({ count: counts.over, label: t.tr('WireGuardTagOverLimit'), color: counts.over ? 'red' : 'gray' });
        if (counts.refused) cards.push({ count: counts.refused, label: t.tr('WireGuardCardLeftOut'), color: 'orange' });
        shared.renderCards('wireguardCards', cards);
        // The counts mean nothing while WireGuard or both of its halves are off, so they hide then.
        shared.setVisible('wireguardCards', on && (!!w.HostEnabled || !!w.ConnectEnabled));

        var hostTunnel = tunnels.filter(function (x) { return x.Key === 'host'; })[0];
        var error = hostTunnel && hostTunnel.State !== 'Running' ? hostTunnel.Error : (on && w.HostEnabled && !hostTunnel && sidecar && sidecar.Installed ? t.tr('WireGuardErrorNoTunnel') : '');
        // The reason the host cannot run shows as an error banner under the cards.
        var hostError = $('hostError');
        hostError.textContent = '';
        if (error) hostError.appendChild(nectoModule.createBanner({ kind: 'error', text: error, dismissible: false }).element);
        shared.setVisible(hostError, !!error);

        var bothOff = on && !w.HostEnabled && !w.ConnectEnabled && !(w.Peers || []).length && !(w.Connections || []).length;
        $('wireguardBothOff').innerHTML = bothOff ? shared.emptySection(t.tr('WireGuardEmptyBothOff')) : '';
        shared.setVisible('wireguardBothOff', bothOff);

        rows = tunnelRows(t, w, ends, { user: userName, used: usedText, server: serverName });
        if (!table) createTable();
        shared.setVisible('wireguardTable', !bothOff);
        shared.setVisible(table.addButton('btnAddPeer'), on && !!w.HostEnabled);
        shared.setVisible(table.addButton('btnAddConnection'), on && !!w.ConnectEnabled);
        table.refresh();
    }

    // typeBadge
    // The badge that says whether a row is this server, a peer, or a connection.
    function typeBadge(row) {
        var type = TYPE_BADGES[row.Kind];
        return { label: t.tr(type.key), color: type.color };
    }

    // createTable
    // Builds the one table of this server, the peers, and the connections, with its search, its type filter, Add
    // peer and Add connection, and Enable, Disable, and Remove for the rows picked. The rows come from what the page
    // loaded.
    function createTable() {
        table = nectoModule.createToolTable(view, shared, {
            containerId: 'wireguardTable',
            rows: function () { return rows; },
            search: t.tr('WireGuardSearch'),
            filters: [{
                name: 'type',
                label: t.tr('LabelType'),
                options: [{ value: 'Peer', label: t.tr('HeaderPeers') }, { value: 'Connection', label: t.tr('HeaderConnections') }],
                test: function (row, value) { return row.Kind === value; }
            }],
            empty: { message: t.tr('WireGuardEmptyNone'), filteredMessage: t.tr('WireGuardEmptyMatch') },
            title: function (row) { return row.Name; },
            lines: function (row) { return [row.Line, row.Detail]; },
            status: function (row) { return [typeBadge(row), { label: row.State.label, color: row.State.color || 'gray' }]; },
            add: [
                { id: 'btnAddPeer', icon: 'add', text: t.tr('WireGuardButtonAddPeer'), onClick: function () { editPeer(null); } },
                { id: 'btnAddConnection', icon: 'add_link', text: t.tr('WireGuardButtonAddConnection'), variant: 'secondary', onClick: function () { editConnection(null); } }
            ],
            bulk: [{ verb: 'enable', text: t.tr('EnablePlugin'), variant: 'primary' }, { verb: 'disable', text: t.tr('DisablePlugin') }, { verb: 'remove', text: t.tr('ButtonRemove'), variant: 'destructive' }],
            onBulk: function (verb, picked) { bulk(verb, picked); },
            onRowClick: function (row) {
                if (row.Kind === 'Host') nectoModule.openTool(view, 'settings');
                else if (row.Kind === 'Peer') openPeer(row.Id);
                else openConnection(row.Id);
            },
            onReload: function () { load(); }
        });
    }

    // bulk
    // Enables, disables, or removes the peers and connections picked, asking first before a removal. This server's
    // own row is left out, since Settings turns it on and off.
    //
    // Output
    // [Promise]: Resolves once the page is read again.
    function bulk(verb, picked) {
        var chosen = (picked || []).filter(function (row) { return row.Kind !== 'Host'; });
        var recordOf = function (row) {
            var list = row.Kind === 'Peer' ? cfg.Peers : cfg.Connections;
            return (list || []).filter(function (x) { return x.Id === row.Id; })[0] || null;
        };
        var routeOf = function (row) { return (row.Kind === 'Peer' ? 'WireGuard/Peers/' : 'WireGuard/Connections/') + row.Id; };
        return nectoModule.runBulk(shared, {
            verb: verb,
            rows: chosen,
            prefix: 'WireGuardBulk',
            enabled: function (row) { var r = recordOf(row); return !!(r && r.Enabled !== false); },
            confirm: verb === 'remove' ? { text: t.tr('WireGuardConfirmRemoveMany'), title: t.plural(chosen.length, 'WireGuardHeaderRemoveOne', 'WireGuardHeaderRemoveMany') } : null,
            call: function (row) {
                if (verb === 'remove') return shared.request(routeOf(row), 'DELETE');
                var record = recordOf(row);
                if (!record) return Promise.reject(new Error('gone'));
                return shared.request(routeOf(row), 'PUT', Object.assign({}, record, { Enabled: verb === 'enable' }));
            },
            report: report
        }).then(function (done) { if (done !== null) return load(); });
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

    // part
    // A section of a detail sidecar, led by its icon tile and always open, holding the nodes given.
    function part(key, icon, nodes) {
        var content = h('div', { class: 'stolon-stack' }, nodes);
        return nectoModule.createCollapsibleSection({ title: t.tr(key), icon: icon, collapsible: false, content: content }).element;
    }

    // openPeer
    // Opens a peer's details in a sidecar: its state, owner, address, handshake, source, usage, and limits, its
    // config file, and a QR code of it, with Edit and Download.
    function openPeer(id) {
        var peer = (cfg.Peers || []).filter(function (p) { return p.Id === id; })[0];
        if (!peer) return;
        var state = peerState(peer);
        var end = endOf(peer.Id);
        var tag = stateTag(t, end.State || 'Offline');
        var body = h('div', { class: 'stolon-stack' });
        body.appendChild(part('LabelDetails', 'info', [nectoModule.createDefinitionList({
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
        }).element]));
        var qrHost = h('div');
        var configHost = h('div');
        var configPart = part('WireGuardLabelConfigFile', 'description', [configHost]);
        body.appendChild(part('WireGuardHeaderQrCode', 'qr_code_2', [qrHost]));
        body.appendChild(configPart);
        var configText = null;
        fetchPeerConfig(peer.Id).then(function (text) {
            configText = text;
            qrHost.appendChild(nectoModule.createQrCode({ value: text, showValue: false, label: t.tr('WireGuardQrLabel'), fileName: safeFileName(peer.Name) + '.png' }).element);
            configHost.appendChild(h('pre', { class: 'stolon-code stolon-code-block', text: text }));
        }, function () {
            // With no config there is no file to show, so its section leaves and the QR code's says why.
            qrHost.innerHTML = shared.emptySection(t.tr('WireGuardErrorNoPeerConfig'));
            configPart.classList.add('stolon-hidden');
        });

        nectoModule.openSidecar({
            title: peer.Name || peer.Address,
            body: body,
            size: 'large',
            page: page,
            buttons: [
                { text: t.tr('Edit'), icon: 'edit', className: 'stolon-button stolon-secondary', action: function (close) { close(); editPeer(peer); } },
                { text: t.tr('Download'), icon: 'download', className: 'stolon-button stolon-primary', action: function () { if (configText) downloadText(safeFileName(peer.Name) + '.conf', configText); } }
            ]
        });
    }

    // field
    // A Stolon input field with its label, an optional description, and a valid example value.
    function field(labelKey, type, value, helpKey, placeholder) {
        return nectoModule.createInputField({
            label: t.tr(labelKey),
            type: type,
            value: value == null ? '' : value,
            min: type === 'number' ? 0 : undefined,
            placeholder: placeholder || undefined,
            autocomplete: 'off',
            help: helpKey ? t.tr(helpKey) : undefined
        });
    }

    // textArea
    // A Stolon text area in the code font, with its label and description.
    function textArea(labelKey, helpKey, value, placeholder, rows) {
        var made = nectoModule.createTextareaField({ label: t.tr(labelKey), help: t.tr(helpKey), value: value || '', placeholder: placeholder || undefined, rows: rows || 3 });
        made.input.classList.add('stolon-mono');
        made.input.setAttribute('spellcheck', 'false');
        return made;
    }

    // collapsible
    // A section of an editor's fields, led by its icon tile, its fields a wide gap apart, closed unless it is the first.
    function collapsible(titleKey, icon, children, open) {
        var made = nectoModule.createCollapsibleSection({ title: t.tr(titleKey), icon: icon, collapsed: !open });
        children.forEach(function (c) { made.content.appendChild(c.element || c); });
        made.content.classList.add('stolon-flow');
        return made.element;
    }

    // inheritSpeed
    // A speed limit in Mbps that takes the default until it is overridden.
    function inheritSpeed(labelKey, own, inherited) {
        var input = h('input', { is: 'emby-input', type: 'number', min: '0' });
        var made = nectoModule.createInheritField({
            field: input,
            inheritedValue: inherited || 0,
            inheritedText: inherited > 0 ? limitText(null, inherited, 'Mbps') : t.tr('LabelNoLimit'),
            label: t.tr(labelKey),
            fieldLabel: t.tr(labelKey),
            value: { inherit: own == null, value: own == null ? (inherited || 0) : own }
        });
        return {
            element: made.element,
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
            inheritedText: inherited.bytes > 0 ? dataText(t, shared, inherited.bytes, inherited.period) : t.tr('LabelNoLimit'),
            label: t.tr('WireGuardLabelDataLimit'),
            fieldLabel: t.tr('WireGuardLabelDataLimit'),
            value: { inherit: !own, value: own || inherited }
        });
        return {
            element: made.element,
            read: function () {
                var v = made.getValue();
                return v.inherit ? null : v.value;
            }
        };
    }

    // ownerField
    // The owner of a peer, picked from the server's users.
    function ownerField(userId) {
        var made = nectoModule.createSelectField({
            label: t.tr('WireGuardLabelOwner'),
            help: t.tr('WireGuardOwnerHelp'),
            options: users.map(function (u) { return { value: u.Id, text: u.Name }; })
        });
        var match = users.filter(function (u) { return compact(u.Id) === compact(userId); })[0];
        if (match) made.input.value = match.Id;
        return made;
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
        var owner = ownerField(peer.UserId);
        var address = field('LabelAddress', 'text', peer.Address, null, t.tr('WireGuardPlaceholderNextFree'));
        var subnets = field('WireGuardLabelRoutedSubnets', 'text', (peer.RoutedSubnets || []).join(', '), 'WireGuardRoutedSubnetsHelp', '192.168.1.0/24');
        var enabled = nectoModule.createCheckboxField({ label: t.tr('LabelEnabled'), checked: peer.Enabled !== false });
        var publicKey = field('WireGuardLabelPublicKey', 'text', isNew ? '' : peer.PublicKey, isNew ? 'WireGuardPublicKeyHelp' : null, isNew ? t.tr('WireGuardPlaceholderMadeForYou') : '');
        publicKey.input.readOnly = !isNew;
        var down = inheritSpeed('WireGuardLabelDownloadMbps', peer.DownloadMbps, cfg.PeerDownloadMbps);
        var up = inheritSpeed('WireGuardLabelUploadMbps', peer.UploadMbps, cfg.PeerUploadMbps);
        var data = inheritData(peer.DataLimit != null ? { bytes: peer.DataLimit, period: normalizePeriod(peer.DataPeriod) } : null, { bytes: cfg.PeerDataLimit || 0, period: normalizePeriod(cfg.PeerDataPeriod) });
        var body = h('div', {}, [
            status,
            collapsible('WireGuardHeaderPeer', 'devices', [name, owner, address, publicKey, enabled], true),
            collapsible('HeaderLimits', 'speed', [down, up, data]),
            collapsible('TabAdvanced', 'tune', [subnets])
        ]);

        function read() {
            var d = data.read();
            return {
                Name: name.input.value.trim(),
                UserId: owner.input.value,
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
            onDelete: function (close) { removeRecord('WireGuard/Peers', peer.Id, peer.Name, 'WireGuardConfirmRemovePeer', close); }
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
        var body = h('div', { class: 'stolon-stack' });
        body.appendChild(part('LabelDetails', 'info', [nectoModule.createDefinitionList({
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
        }).element]));
        body.appendChild(part('WireGuardLabelOurPublicKey', 'key', [
            nectoModule.createCopyField({ label: t.tr('WireGuardLabelOurPublicKey'), copyLabel: t.tr('WireGuardLabelOurPublicKey'), value: c.PublicKey || '', mono: true }).element,
            h('div', { class: 'fieldDescription', text: t.tr('WireGuardOurPublicKeyHelp') })
        ]));

        nectoModule.openSidecar({
            title: c.Name || c.Endpoint,
            body: body,
            size: 'large',
            page: page,
            buttons: [{ text: t.tr('Edit'), icon: 'edit', className: 'stolon-button stolon-primary', action: function (close) { close(); editConnection(c); } }]
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
        var enabled = nectoModule.createCheckboxField({ label: t.tr('LabelEnabled'), checked: c.Enabled !== false });
        var keepalive = field('WireGuardLabelKeepalive', 'number', c.Keepalive, null, '25');
        var down = inheritSpeed('WireGuardLabelDownloadMbps', c.DownloadMbps, cfg.ConnectionDownloadMbps);
        var up = inheritSpeed('WireGuardLabelUploadMbps', c.UploadMbps, cfg.ConnectionUploadMbps);
        var data = inheritData(c.DataLimit != null ? { bytes: c.DataLimit, period: normalizePeriod(c.DataPeriod) } : null, { bytes: cfg.ConnectionDataLimit || 0, period: normalizePeriod(cfg.ConnectionDataPeriod) });
        var body = h('div', {}, [
            status,
            collapsible('WireGuardHeaderConnection', 'link', [name, endpoint, peerKey, psk, address, privateKey, enabled], true),
            collapsible('WireGuardHeaderImport', 'content_paste', [importArea]),
            collapsible('WireGuardLabelForwards', 'swap_horiz', [forwards]),
            collapsible('HeaderLimits', 'speed', [down, up, data]),
            collapsible('TabAdvanced', 'tune', [keepalive])
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
            p.addCleanup(function () {
                if (table) table.destroy();
                table = null;
            });
        },
        load: function () { load(); }
    });
    shared = made.shared;
    page = made.page;
}
