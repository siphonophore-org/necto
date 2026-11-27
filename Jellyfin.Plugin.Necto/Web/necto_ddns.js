// necto_ddns.js
// The DDNS tool. It shows the public address the last run found, lists the records with their last result, and opens
// each in an editor sidecar with the fields its provider uses, its update interval, and its status.

import * as nectoModule from './configurationpage?name=necto_shared.js';
import * as fields from './configurationpage?name=necto_netfields.js';

var el = nectoModule.el;

// The outcome labels the update run writes, in its own words, and the state each shows as.
var ACTIONS = {
    'Updated': { key: 'DdnsResultUpdated', state: 'success' },
    'IP Unchanged': { key: 'DdnsResultUnchanged', state: 'success' },
    'No address': { key: 'TextNoAddress', state: 'warning' },
    'Backing off': { key: 'DdnsResultPaused', state: 'warning' },
    'Failed': { key: 'LabelFailed', state: 'error' }
};

// providerLabel
// Names a record's provider as the provider list does.
//
// Inputs
// providers [object[]]: The providers from Ddns/Providers.
// kind [string|number]: The record's provider.
//
// Output
// [string]: The provider's name, or the kind when it is unknown.
export function providerLabel(providers, kind) {
    var found = (providers || []).find(function (p, i) { return p.Value === kind || i === kind; });
    return found ? found.Label : String(kind == null ? '' : kind);
}

// providerOf
// Finds a provider by its kind.
//
// Inputs
// providers [object[]]: The providers.
// kind [string]: The kind.
//
// Output
// [object|null]: The provider.
export function providerOf(providers, kind) {
    return (providers || []).find(function (p) { return p.Value === kind; }) || null;
}

// recordName
// Names a record for the list: its name, else its hostname, else its provider.
//
// Inputs
// record [object]: The record.
// providers [object[]]: The providers.
//
// Output
// [string]: The name.
export function recordName(record, providers) {
    return (record.Name || '').trim() || (record.Hostname || '').trim() || providerLabel(providers, record.Provider);
}

// resultTag
// Turns a record's last outcome into a tag, or none before its first run.
//
// Inputs
// record [object]: The record with its status.
// t [object]: The strings.
//
// Output
// [object|null]: The tag.
export function resultTag(record, t) {
    var action = ACTIONS[record.LastAction];
    if (!action) return record.LastCheckedUtc ? null : { label: t.tr('DdnsResultNotChecked'), color: 'gray' };
    var colors = { success: 'green', warning: 'orange', error: 'red' };
    return { label: t.tr(action.key), color: colors[action.state] };
}

// recordItems
// Turns the records into rows for the record list, each with its provider, its name, its last result, and whether it
// is off.
//
// Inputs
// entries [object[]]: The Records from Ddns/Records, each { Record, Followed }.
// providers [object[]]: The providers.
// t [object]: The strings.
//
// Output
// [object[]]: The rows, each carrying its record.
export function recordItems(entries, providers, t) {
    return (entries || []).map(function (entry) {
        var r = entry.Record;
        var where = r.FollowProxyHosts
            ? t.format((entry.Followed || []).length === 1 ? 'DdnsFollowsHostsOne' : 'DdnsFollowsHostsMany', (entry.Followed || []).length)
            : (r.Hostname || '');
        var tags = [];
        var result = resultTag(r.FollowProxyHosts && (entry.Followed || []).length ? worst(entry.Followed) : r, t);
        if (result) tags.push(result);
        if (!r.Enabled) tags.push({ label: t.tr('Off'), color: 'gray', off: true });
        return {
            id: r.Id,
            title: recordName(r, providers),
            secondary: [providerLabel(providers, r.Provider), where].filter(function (x) { return x; }).join(' · '),
            tags: tags,
            record: r,
            followed: entry.Followed || []
        };
    });
}

// worst
// Picks the followed host whose result matters most: a failure, then a pause, then anything else.
//
// Inputs
// followed [object[]]: The followed hosts with their status.
//
// Output
// [object]: The host's status.
function worst(followed) {
    var rank = { 'Failed': 0, 'Backing off': 1, 'No address': 2 };
    return followed.slice().sort(function (a, b) {
        return (rank[a.LastAction] == null ? 9 : rank[a.LastAction]) - (rank[b.LastAction] == null ? 9 : rank[b.LastAction]);
    })[0];
}

// newRecord
// Builds an empty record with the defaults a new one starts with.
//
// Output
// [object]: The record.
export function newRecord() {
    return { Id: '', Name: '', Provider: 'Cloudflare', Hostname: '', Enabled: true, UpdateIPv4: true, UpdateIPv6: false, Login: '', Password: '', Zone: '', Server: '', Ttl: 1, UpdateEveryMinutes: 15, FollowProxyHosts: false, Proxied: false, Wildcard: false, Mx: '', BackupMx: false, Static: false };
}

// statusItems
// Describes a record's last run as label and value pairs.
//
// Inputs
// record [object]: The record with its status.
// shared [object]: The Necto helper bag.
//
// Output
// [object[]]: The pairs.
export function statusItems(record, shared) {
    var t = shared.t;
    var items = [];
    var tag = resultTag(record, t);
    items.push({ label: t.tr('LabelResult'), value: tag ? tag.label : t.tr('DdnsResultNotChecked') });
    if (record.LastStatus) items.push({ label: t.tr('LabelMessage'), value: record.LastStatus });
    if (record.LastCheckedUtc) items.push({ label: t.tr('DdnsLabelLastCheck'), value: shared.formatDate(record.LastCheckedUtc) });
    if (record.LastUpdateUtc) items.push({ label: t.tr('DdnsLabelLastUpdate'), value: shared.formatDate(record.LastUpdateUtc) });
    if (record.LastIPv4) items.push({ label: 'IPv4', value: record.LastIPv4 });
    if (record.LastIPv6) items.push({ label: 'IPv6', value: record.LastIPv6 });
    if (record.BackoffUntilUtc && new Date(record.BackoffUntilUtc) > new Date()) items.push({ label: t.tr('DdnsLabelPausedUntil'), value: shared.formatDate(record.BackoffUntilUtc) });
    return items;
}

// createRecordEditor
// Builds the body of a record's sidecar: its name, provider and the fields that provider uses, which addresses it
// keeps, how often, whether it follows the proxy's host names, its finer settings, and its last result.
//
// Inputs
// record [object]: The record to edit, or a new one.
// ctx [object]: shared is the Necto helper bag, providers the providers, and followed the hosts a following record
//   covers.
//
// Output
// [object]: A handle with element, getValue, validate, setStatus, and destroy.
export function createRecordEditor(record, ctx) {
    var shared = ctx.shared;
    var t = shared.t;
    var draft = JSON.parse(JSON.stringify(record || newRecord()));
    var status = el('div', { class: 'stolon-status-text', role: 'status', 'data-role': 'status' });
    var root = el('div', { 'data-role': 'record-editor' }, [status]);
    var handles = [];

    var name = fields.inputField({ label: t.tr('LabelName'), field: 'Name', value: draft.Name, help: t.tr('DdnsNameHelp') });
    var enabled = fields.checkField({ label: t.tr('DdnsLabelEnabled'), field: 'Enabled', checked: draft.Enabled !== false, help: t.tr('DdnsEnabledHelp') });
    var provider = fields.selectField({
        label: t.tr('LabelProvider'),
        field: 'Provider',
        options: (ctx.providers || []).map(function (p) { return { value: p.Value, label: p.Label }; }),
        value: draft.Provider
    });
    var follow = fields.checkField({ label: t.tr('DdnsLabelFollowProxy'), field: 'FollowProxyHosts', checked: !!draft.FollowProxyHosts, help: t.tr('DdnsFollowProxyHelp') });
    var hostname = fields.inputField({ label: t.tr('DdnsLabelHostname'), field: 'Hostname', value: draft.Hostname, placeholder: 'home.example.com' });
    var login = fields.secretField({ label: t.tr('GuideProviderLogin'), field: 'Login', clearText: t.tr('ButtonClearSaved') });
    var password = fields.secretField({ label: t.tr('LabelPassword'), field: 'Password', help: t.tr('DdnsLimitedTokenHelp'), clearText: t.tr('ButtonClearSaved') });
    var zone = fields.inputField({ label: t.tr('DdnsLabelZone'), field: 'Zone', value: draft.Zone, placeholder: 'example.com' });
    var ipv4 = fields.checkField({ label: t.tr('DdnsLabelUpdateIpv4'), field: 'UpdateIPv4', checked: draft.UpdateIPv4 !== false });
    var ipv6 = fields.checkField({ label: t.tr('DdnsLabelUpdateIpv6'), field: 'UpdateIPv6', checked: !!draft.UpdateIPv6, help: t.tr('DdnsUpdateFamiliesHelp') });
    var every = fields.inputField({ label: t.tr('DdnsLabelUpdateEvery'), field: 'UpdateEveryMinutes', type: 'number', min: 5, value: draft.UpdateEveryMinutes || 15, help: t.tr('DdnsUpdateEveryHelp') });
    var server = fields.inputField({ label: t.tr('DdnsLabelServerOverride'), field: 'Server', value: draft.Server, placeholder: 'Default', help: t.tr('DdnsServerOverrideHelp') });
    var ttl = fields.inputField({ label: t.tr('DdnsLabelTtl'), field: 'Ttl', type: 'number', min: 1, value: draft.Ttl || 1, help: t.tr('DdnsTtlHelp') });
    var proxied = fields.checkField({ label: t.tr('DdnsLabelProxied'), field: 'Proxied', checked: !!draft.Proxied, help: t.tr('DdnsProxiedHelp') });
    var wildcard = fields.checkField({ label: t.tr('DdnsLabelWildcard'), field: 'Wildcard', checked: !!draft.Wildcard, help: t.tr('DdnsWildcardHelp') });
    var isStatic = fields.checkField({ label: t.tr('DdnsLabelStaticDns'), field: 'Static', checked: !!draft.Static, help: t.tr('DdnsStaticDnsHelp') });
    var mx = fields.inputField({ label: t.tr('DdnsLabelMxHost'), field: 'Mx', value: draft.Mx, placeholder: 'mail.example.com', help: t.tr('DdnsMxHostHelp') });
    var backupMx = fields.checkField({ label: t.tr('DdnsLabelBackupMx'), field: 'BackupMx', checked: !!draft.BackupMx });

    shared.bindSecretField(login.input, !!draft.Login, { clearButton: login.clear });
    shared.bindSecretField(password.input, !!draft.Password, { clearButton: password.clear });

    [name, enabled, provider].forEach(function (f) { root.appendChild(f.element); });
    root.appendChild(el('h3', { class: 'stolon-subsection-title', text: t.tr('DdnsHeaderName') }));
    [follow, hostname, login, password, zone].forEach(function (f) { root.appendChild(f.element); });
    root.appendChild(el('h3', { class: 'stolon-subsection-title', text: t.tr('DdnsHeaderUpdates') }));
    [ipv4, ipv6, every].forEach(function (f) { root.appendChild(f.element); });
    var advanced = el('div', {}, [server, ttl, proxied, wildcard, isStatic, mx, backupMx].map(function (f) { return f.element; }));
    var advancedSection = nectoModule.createCollapsibleSection({ title: t.tr('DdnsHeaderAdvanced'), icon: 'tune', collapsed: true, content: advanced });
    root.appendChild(advancedSection.element);
    handles.push(advancedSection);

    if (draft.Id) {
        var statusList = el('div', { 'data-role': 'record-status' });
        root.appendChild(el('h3', { class: 'stolon-subsection-title', text: t.tr('DdnsHeaderStatus') }));
        root.appendChild(statusList);
        var rows = draft.FollowProxyHosts && (ctx.followed || []).length
            ? (ctx.followed || []).map(function (f) {
                var tag = resultTag(f, t);
                return { label: f.Hostname, value: (tag ? tag.label : t.tr('DdnsResultNotChecked')) + (f.LastStatus ? ' · ' + f.LastStatus : '') };
            })
            : statusItems(draft, shared);
        var statusDl = nectoModule.createDefinitionList({ items: rows });
        statusList.appendChild(statusDl.element);
        handles.push(statusDl);
    }

    // showFields
    // Shows only what the picked provider uses, with its own labels and hint.
    function showFields() {
        var spec = fields_of(provider.input.value);
        var f = spec.Fields || {};
        provider.help.textContent = spec.Hint || '';
        var set = function (field, label) {
            field.element.classList.toggle('stolon-hidden', !label);
            if (label) field.element.querySelector('label').textContent = label;
        };
        set(hostname, follow.input.checked ? null : f.Hostname);
        set(login, f.Login);
        set(password, f.Password);
        set(zone, f.Zone);
        server.element.classList.toggle('stolon-hidden', !f.Server);
        ttl.element.classList.toggle('stolon-hidden', !f.Ttl);
        ipv6.element.classList.toggle('stolon-hidden', f.IPv6 === false);
        var adv = f.Advanced || [];
        wildcard.element.classList.toggle('stolon-hidden', adv.indexOf('wildcard') < 0);
        isStatic.element.classList.toggle('stolon-hidden', adv.indexOf('static') < 0);
        mx.element.classList.toggle('stolon-hidden', adv.indexOf('mx') < 0);
        backupMx.element.classList.toggle('stolon-hidden', adv.indexOf('backupmx') < 0);
    }

    // fields_of
    // Gives a provider's spec, or an empty one.
    function fields_of(kind) {
        return providerOf(ctx.providers, kind) || { Fields: {} };
    }

    provider.input.addEventListener('change', showFields);
    follow.input.addEventListener('change', showFields);
    showFields();

    return {
        element: root,
        getValue: function () {
            var spec = fields_of(provider.input.value).Fields || {};
            var adv = spec.Advanced || [];
            var out = JSON.parse(JSON.stringify(draft));
            out.Name = name.input.value.trim();
            out.Enabled = !!enabled.input.checked;
            out.Provider = provider.input.value;
            out.FollowProxyHosts = !!follow.input.checked;
            out.Hostname = spec.Hostname ? hostname.input.value.trim() : '';
            out.Login = spec.Login ? shared.readSecretField(login.input) : '';
            out.Password = spec.Password ? shared.readSecretField(password.input) : '';
            out.Zone = spec.Zone ? zone.input.value.trim() : '';
            out.UpdateIPv4 = !!ipv4.input.checked;
            out.UpdateIPv6 = spec.IPv6 === false ? false : !!ipv6.input.checked;
            out.UpdateEveryMinutes = Math.max(5, fields.readNumber(every.input) || 15);
            out.Server = spec.Server ? server.input.value.trim() : '';
            out.Ttl = spec.Ttl ? Math.max(1, fields.readNumber(ttl.input) || 1) : out.Ttl;
            out.Proxied = !!proxied.input.checked;
            out.Wildcard = adv.indexOf('wildcard') >= 0 && !!wildcard.input.checked;
            out.Static = adv.indexOf('static') >= 0 && !!isStatic.input.checked;
            out.Mx = adv.indexOf('mx') >= 0 ? mx.input.value.trim() : '';
            out.BackupMx = adv.indexOf('backupmx') >= 0 && !!backupMx.input.checked;
            return out;
        },
        validate: function () {
            var value = this.getValue();
            var spec = fields_of(value.Provider).Fields || {};
            if (spec.Hostname && !value.FollowProxyHosts && !value.Hostname) return 'DdnsErrorEnterHostname';
            if (!value.UpdateIPv4 && !value.UpdateIPv6) return 'DdnsErrorSelectFamily';
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

// outcomeMessage
// Sums an update run for the status line: how many were updated, skipped, or failed, and any warning.
//
// Inputs
// outcome [object]: The run's outcome from Ddns/Update.
// shared [object]: The Necto helper bag.
//
// Output
// [object]: text is the message and error whether anything failed.
export function outcomeMessage(outcome, shared) {
    var t = shared.t;
    var records = (outcome && outcome.Records) || [];
    var failed = records.filter(function (r) { return !r.Success && !r.Skipped; }).length;
    var updated = records.filter(function (r) { return r.Success && !r.Skipped; }).length;
    var warnings = (outcome && outcome.Warnings) || [];
    if (records.length === 0) return { text: warnings.length ? warnings.join(' ') : t.tr('DdnsNoRecordsToUpdate'), error: warnings.length > 0 };
    var parts = [];
    if (updated) parts.push(shared.plural(updated, 'DdnsRunUpdatedOne', 'DdnsRunUpdatedMany'));
    if (failed) parts.push(shared.plural(failed, 'DdnsRunFailedOne', 'DdnsRunFailedMany'));
    if (!parts.length) parts.push(t.tr('DdnsRunNothingChanged'));
    return { text: parts.concat(warnings).join(' '), error: failed > 0 };
}

export default function (view) {
    'use strict';

    var shared = null;
    var page = null;
    var list = null;
    var addressList = null;
    var providers = [];
    var loadSeq = 0;

    // report
    // Shows a result on the page's status line.
    function report(message, isError) {
        shared.setStatus('ddnsStatus', message, !!isError, { timeout: 10000 });
    }

    // showAddress
    // Shows the public addresses the last run found, and why one is missing.
    function showAddress(data) {
        var t = shared.t;
        var notices = view.querySelector('#ddnsNotices');
        notices.textContent = '';
        if (data.SecretsPlaintext) notices.appendChild(el('div', { class: 'stolon-warn', text: t.tr('DdnsKeyStoreUnavailable') }));
        if (data.DetectionMessage) notices.appendChild(el('div', { class: 'stolon-warn', text: data.DetectionMessage }));
        var items = [
            { label: t.tr('DdnsLabelPublicIpv4'), value: data.DetectedIPv4 || t.tr('TextNotFound') },
            { label: t.tr('DdnsLabelPublicIpv6'), value: data.DetectedIPv6 || t.tr('TextNotFound') }
        ];
        if (addressList) addressList.setItems(items);
        else {
            addressList = nectoModule.createDefinitionList({ items: items });
            view.querySelector('#ddnsAddress').appendChild(addressList.element);
        }
    }

    // loadRecords
    // Reads the providers once and the records each time, then draws them. A late answer is dropped.
    function loadRecords() {
        var mine = ++loadSeq;
        var ready = providers.length ? Promise.resolve(providers) : shared.request('Ddns/Providers');
        return Promise.all([ready, shared.request('Ddns/Records')]).then(function (answers) {
            if (mine !== loadSeq) return;
            providers = answers[0] || [];
            var data = answers[1] || {};
            showAddress(data);
            var items = recordItems(data.Records, providers, shared.t);
            if (list) list.setItems(items);
            else {
                list = nectoModule.createRecordList({
                    items: items,
                    label: shared.t.tr('TabDdns'),
                    emptyText: shared.t.tr('DdnsEmptyRecords'),
                    onOpen: function (item) { openRecord(item.record, item.followed); }
                });
                view.querySelector('#ddnsRecords').appendChild(list.element);
            }
        }).catch(function (err) {
            if (mine !== loadSeq) return;
            shared.errorText(err, shared.t.tr('DdnsErrorLoadRecords')).then(function (text) { report(text, true); });
        });
    }

    // saveRecord
    // Saves the editor's record, a new one by POST and an existing one by PUT.
    //
    // Output
    // [Promise]: Resolves true once saved, and false to keep the sidecar open.
    function saveRecord(editor, isNew) {
        var problem = editor.validate();
        if (problem) {
            editor.setStatus(shared.t.tr(problem), true);
            return Promise.resolve(false);
        }
        var value = editor.getValue();
        var request = isNew ? shared.request('Ddns/Records', 'POST', value) : shared.request('Ddns/Records/' + encodeURIComponent(value.Id), 'PUT', value);
        return request.then(function () {
            report(shared.t.tr('DdnsToastRecordSaved'), false);
            loadRecords();
            return true;
        }).catch(function (err) {
            return shared.errorText(err, shared.t.tr('DdnsErrorSaveRecord')).then(function (text) {
                editor.setStatus(text, true);
                return false;
            });
        });
    }

    // removeRecord
    // Asks first, then removes the record and closes its sidecar. The DNS name stays at the provider.
    function removeRecord(record, close) {
        var t = shared.t;
        nectoModule.confirmDialog(t.tr('DdnsConfirmRemoveRecord'), {
            title: t.format('TitleRemoveName', recordName(record, providers)),
            confirmText: t.tr('ButtonRemove'),
            destructive: true
        }).then(function (confirmed) {
            if (!confirmed) return;
            shared.request('Ddns/Records/' + encodeURIComponent(record.Id), 'DELETE').then(function () {
                close('removed');
                report(t.tr('DdnsToastRecordRemoved'), false);
                loadRecords();
            }).catch(function (err) {
                shared.errorText(err, t.tr('DdnsErrorRemoveRecord')).then(function (text) { report(text, true); });
            });
        });
    }

    // openRecord
    // Opens a record, or a new one, in the editor sidecar.
    function openRecord(record, followed) {
        var t = shared.t;
        var isNew = !record;
        var editor = createRecordEditor(record || newRecord(), { shared: shared, providers: providers, followed: followed || [] });
        nectoModule.openEditorSidecar({
            title: isNew ? t.tr('DdnsTitleNewRecord') : recordName(record, providers),
            body: editor.element,
            page: page,
            isNew: isNew,
            serialize: function () { return JSON.stringify(editor.getValue()); },
            saveText: t.tr('Save'),
            deleteText: t.tr('ButtonRemove'),
            onSave: function () { return saveRecord(editor, isNew); },
            onDelete: function (close) { removeRecord(record, close); },
            onClose: function () { editor.destroy(); }
        });
    }

    // updateNow
    // Checks every enabled record now and sums the run on the status line.
    function updateNow() {
        var btn = view.querySelector('#btnUpdateNow');
        btn.disabled = true;
        report(shared.t.tr('DdnsUpdating'), false);
        return shared.request('Ddns/Update', 'POST').then(function (outcome) {
            var message = outcomeMessage(outcome, shared);
            report(message.text, message.error);
            loadRecords();
        }).catch(function (err) {
            return shared.errorText(err, shared.t.tr('DdnsErrorUpdate')).then(function (text) { report(text, true); });
        }).then(function () { btn.disabled = false; });
    }

    var made = nectoModule.createNectoPage(view, {
        page: 'necto_ddns',
        bind: function () {
            view.querySelector('#btnAddRecord').addEventListener('click', function () { openRecord(null); });
            view.querySelector('#btnUpdateNow').addEventListener('click', updateNow);
        },
        load: function () { loadRecords(); }
    });
    shared = made.shared;
    page = made.page;
}
