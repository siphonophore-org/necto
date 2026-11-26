// necto_storage.test.mjs
// Tests for Storage: the disk rows, states, and problems the tool draws, the alert rules its editors keep, its
// Settings section saving into the config, and its Troubleshooting section.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { installPage, loadScript, pageView, settle, routeApi, texts, clickButton, clickDialogButton, dialogText, strings } from './support.mjs';

globalThis.setTimeout = function () { return 1; };
globalThis.clearTimeout = function () {};
globalThis.setInterval = function () { return 1; };
globalThis.clearInterval = function () {};

var GB = 1024 * 1024 * 1024;

// t
// Reads the plugin's US English strings as a page does.
var t = {
    tr: function (key) { return strings[key] === undefined ? key : strings[key]; },
    format: function (key) {
        var args = Array.prototype.slice.call(arguments, 1);
        return t.tr(key).replace(/\{(\d+)\}/g, function (m, i) { return String(args[i]); });
    },
    casing: ['exFAT', 'APFS', 'Btrfs']
};

// shared
// The helpers the pure functions read.
var shared = {
    formatSize: function (bytes) { return Math.round(bytes / GB) + ' GB'; },
    formatRelativeTime: function () { return '2 hours ago'; }
};

// disk
// Builds a disk as GET Storage/Disks gives it.
function disk(extra) {
    return Object.assign({
        MountPoint: '/mnt/media', Name: 'media', State: 'Available', Kind: 'Local', InUse: true, Health: 'Unknown', Level: 'None',
        TotalBytes: 1000 * GB, FreeBytes: 400 * GB, Libraries: [{ Name: 'Movies' }], Folders: [{ Path: '/mnt/media/Movies', Library: 'Movies' }],
        MissingFolders: [], ReadOnlyFolders: []
    }, extra || {});
}

test('a disk state reads as its badge, most serious first, in the colors the style guide gives', async function () {
    installPage({});
    var storage = await loadScript('necto_storage.js');

    assert.deepEqual(storage.stateOf(t, disk({ State: 'Missing' })), { label: 'Missing', color: 'red' });
    assert.deepEqual(storage.stateOf(t, disk({ State: 'Unresponsive' })), { label: 'Unresponsive', color: 'orange' });
    assert.deepEqual(storage.stateOf(t, disk({ Health: 'Failing', Level: 'Warning' })), { label: 'Failing', color: 'red' });
    assert.deepEqual(storage.stateOf(t, disk({ Level: 'Warning' })), { label: 'Low', color: 'orange' });
    assert.deepEqual(storage.stateOf(t, disk({ InUse: false })), { label: 'Unused', color: 'gray' });
    assert.equal(storage.stateOf(t, disk()).color, 'green');
});

test('a disk row tags its type and what is wrong, and an unmounted disk shows its device and size', async function () {
    installPage({});
    var storage = await loadScript('necto_storage.js');

    assert.equal(storage.typeOf(t, disk({ Kind: 'Share' })).icon, 'lan');
    assert.equal(storage.typeOf(t, disk({ IsSystem: true })).icon, 'computer');
    var tags = storage.tagsOf(t, disk({ MissingFolders: ['a', 'b'], Hot: true }));
    assert.deepEqual(tags.map(function (tag) { return tag.label; }).slice(1), ['2 folders missing', 'Too hot']);
    assert.equal(storage.secondaryOf(t, shared, disk({ State: 'Unformatted', Device: '/dev/sdb', MountPoint: null, TotalBytes: 2000 * GB })), '/dev/sdb · 2000 GB');
    assert.equal(storage.secondaryOf(t, shared, disk({ State: 'Missing', MissingSinceUtc: '2026-11-26T10:00:00Z' })), '/mnt/media · missing 2 hours ago');
});

test('the problems put what needs a look now first, and a healthy disk has none', async function () {
    installPage({});
    var storage = await loadScript('necto_storage.js');

    var issues = storage.allIssues(t, shared, [
        disk({ Name: 'a', Level: 'Warning', FreeBytes: 100 * GB }),
        disk({ Name: 'b', State: 'Missing', MissingSinceUtc: '2026-11-26T10:00:00Z' }),
        disk({ Name: 'c' })
    ]);

    assert.equal(issues.length, 2);
    assert.equal(issues[0].text, 'b went missing 2 hours ago.');
    assert.equal(issues[0].serious, true);
    assert.equal(issues[1].text, 'a is low on space, 90% used with 100 GB free.');
    assert.deepEqual(storage.issuesOf(t, shared, disk()), []);
});

test('the space adds up every disk that was read and takes the color of the fullest', async function () {
    installPage({});
    var storage = await loadScript('necto_storage.js');

    var space = storage.spaceOf([disk(), disk({ Level: 'Error', FreeBytes: 10 * GB }), disk({ State: 'Missing', TotalBytes: null })]);

    assert.deepEqual(space, { total: 2000 * GB, free: 410 * GB, color: 'red', count: 2 });
    assert.equal(storage.spaceOf([disk({ State: 'Missing' })]), null);
});

test('folder and volume names are checked as the server checks them', async function () {
    installPage({});
    var storage = await loadScript('necto_storage.js');

    assert.equal(storage.validFolderName('Movies'), true);
    ['', '.', '..', 'a/b', 'a:b', 'name.'].forEach(function (name) { assert.equal(storage.validFolderName(name), false, name); });
    assert.equal(storage.validVolumeName('Media 2'), true);
    ['', ' Media', 'TwelveChars1', 'Média'].forEach(function (name) { assert.equal(storage.validVolumeName(name), false, name); });
});

test('a disk own alerts and a share address replace only that disk entry', async function () {
    installPage({});
    var storage = await loadScript('necto_storage.js');
    var settings = { DiskOverrides: [{ MountPoint: '/mnt/a' }, { MountPoint: '/mnt/media' }], ShareAddresses: [{ MountPoint: '/mnt/media', Address: 'smb://old' }] };
    var own = { WarningStop: { Enabled: true, Value: 70, Unit: 'Percent' }, ErrorStop: { Enabled: true, Value: 80, Unit: 'Percent' } };

    storage.withOverride(settings, '/mnt/media', own);
    storage.withAddress(settings, '/mnt/media', '');

    assert.deepEqual(settings.DiskOverrides.map(function (o) { return o.MountPoint; }), ['/mnt/a', '/mnt/media']);
    assert.equal(settings.DiskOverrides[1].WarningStop.Value, 70);
    assert.deepEqual(settings.ShareAddresses, []);
    storage.withOverride(settings, '/mnt/media', null);
    assert.deepEqual(settings.DiskOverrides.map(function (o) { return o.MountPoint; }), ['/mnt/a']);
});

test('an error key from the server reads as its Storage string', async function () {
    installPage({});
    var storage = await loadScript('necto_storage.js');

    assert.equal(storage.serverText(t, 'ErrorFormatGone'), 'This disk is not unformatted anymore, so it was not touched.');
    assert.equal(storage.meterOptions(disk({ WarningPercent: 90, ErrorPercent: 95 })).danger, 95);
    assert.equal(storage.meterOptions(disk()).warning, 101);
});

test('the Storage full alert is kept past the Storage low alert on both scales', async function () {
    installPage({});
    var sections = await loadScript('necto_storage_sections.js');

    assert.deepEqual(sections.orderedStops('Percent', 90, 95, 'error'), { warning: 90, error: 95 });
    assert.deepEqual(sections.orderedStops('Percent', 90, 85, 'error'), { warning: 90, error: 91 });
    assert.deepEqual(sections.orderedStops('Percent', 100, 100, 'warning'), { warning: 99, error: 100 });
    assert.deepEqual(sections.orderedStops('Gigabytes', 100, 200, 'error'), { warning: 100, error: 90 });
    assert.equal(sections.alertOrderError(t, { WarningStop: { Enabled: true, Value: 95, Unit: 'Percent' }, ErrorStop: { Enabled: true, Value: 90, Unit: 'Percent' } }), 'Set Storage full higher than Storage low.');
    assert.equal(sections.alertOrderError(t, { WarningStop: { Enabled: false, Value: 95, Unit: 'Percent' }, ErrorStop: { Enabled: true, Value: 90, Unit: 'Percent' } }), '');
    assert.equal(sections.alertText(t, { WarningStop: { Enabled: true, Value: 90, Unit: 'Percent' }, ErrorStop: { Enabled: true, Value: 20, Unit: 'Gigabytes' } }), '90% used · 20 GB free');
    assert.equal(sections.named(t, 'exfat'), 'exFAT');
    assert.equal(sections.named(t, 'ext4'), 'EXT4');
    assert.equal(sections.storageOf({}).ScanGuard, 'Affected');
    assert.equal(sections.storageOf({ Storage: { Remount: false } }).Remount, false);
});

// openTool
// Shows Settings or Troubleshooting over a view with its sections container.
async function openTool(name, config, answers) {
    var page = installPage({ config: config });
    var dom = page.dom;
    var container = name === 'settings' ? 'settingsSections' : 'troubleshootingSections';
    var view = pageView(dom, [dom.el('div', { id: 'settingsStatus' }), dom.el('div', { id: container })]);
    dom.document.documentElement.appendChild(view);
    var saved = [];
    globalThis.ApiClient.updatePluginConfiguration = function (id, next) { saved.push(JSON.parse(JSON.stringify(next))); return Promise.resolve(); };
    routeApi(page, answers || {});
    var mod = await loadScript('necto_' + name + '.js');
    mod.default(view);
    view.dispatchEvent(new globalThis.CustomEvent('viewshow'));
    await settle();
    return { dom: dom, view: view, saved: saved, section: view.querySelector('[data-tool="storage"]') };
}

test('the Storage section of Settings shows the saved settings and saves its own fields', async function () {
    var opened = await openTool('settings', { Storage: { ScanGuard: 'All', Remount: false, MuteMonitoring: true }, Other: 'kept' });
    var section = opened.section;

    assert.deepEqual(texts(section, '.stolon-subsection-title'), ['Alerts', 'Missing disks']);
    assert.equal(section.querySelector('[data-field="Remount"]').checked, false);
    assert.equal(section.querySelector('[data-field="ScanGuard"] input[value="All"]').checked, true);
    section.querySelector('[data-field="Remount"]').checked = true;

    assert.equal(clickButton(section, 'Save'), true);
    await settle(50);

    var config = opened.saved[0];
    assert.equal(config.Other, 'kept');
    assert.equal(config.Storage.Remount, true);
    assert.equal(config.Storage.ScanGuard, 'All');
    assert.equal(config.Storage.WarningStop.Value, 90);
    assert.equal(config.Storage.HealthAlerts.HotCelsius, 60);
});

test('the Storage section of Troubleshooting asks before it resets the settings, then saves the defaults', async function () {
    var opened = await openTool('troubleshooting', { Storage: { Remount: false, DiskOverrides: [{ MountPoint: '/mnt/media' }] } });
    var section = opened.section;

    assert.deepEqual(texts(section, '.stolon-subsection-title'), ['Scheduled checks', 'Settings']);
    section.querySelector('[data-action="reset-settings"]').click();
    await settle(50);
    assert.equal(dialogText(opened.dom).title, 'Reset the Storage settings');
    assert.equal(clickDialogButton(opened.dom, 'Reset'), true);
    await settle(50);

    var config = opened.saved[0];
    assert.equal(config.Storage.Remount, true);
    assert.deepEqual(config.Storage.DiskOverrides, []);
});

test('the tool checks every disk, says what needs a look, and lists the disks in use', async function () {
    var page = installPage({ config: {} });
    var dom = page.dom;
    var ids = ['storageAttention', 'storageSpaceCards', 'storageCheckStatus', 'storageDiskList'];
    var children = ids.map(function (id) { return dom.el('div', { id: id }); });
    var filterEl = dom.el('div', { id: 'storageDiskFilter', class: 'stolon-pill-group' }, [
        dom.el('button', { class: 'stolon-pill', 'data-value': 'used' }),
        dom.el('button', { class: 'stolon-pill', 'data-value': 'all' })
    ]);
    var refresh = dom.el('button', { id: 'btnStorageCheck' }, [dom.el('span', { id: 'storageRefreshIcon' })]);
    var view = pageView(dom, children.concat([filterEl, refresh]));
    dom.document.documentElement.appendChild(view);
    routeApi(page, {
        'GET Storage/Disks': [
            disk({ Name: 'media', State: 'Missing', TotalBytes: null, FreeBytes: null, MissingSinceUtc: '2026-11-26T10:00:00Z' }),
            disk({ MountPoint: '/mnt/spare', Name: 'spare', InUse: false })
        ]
    });
    var mod = await loadScript('necto_storage.js');
    mod.default(view);
    view.dispatchEvent(new globalThis.CustomEvent('viewshow'));
    await settle();

    assert.ok(page.requests.some(function (r) { return r.url === 'Storage/Disks'; }));
    var attention = view.querySelector('#storageAttention');
    assert.equal(attention.className.indexOf('stolon-error') >= 0, true);
    assert.match(attention.textContent, /^media went missing /);
    var rows = view.querySelectorAll('#storageDiskList .stolon-record-list-row');
    assert.equal(rows.length, 1);
    assert.match(rows[0].textContent, /media/);
    assert.match(rows[0].textContent, /Local/);

    rows[0].click();
    await settle(50);
    var sidecar = dom.document.querySelector('.stolon-sidecar');
    assert.equal(sidecar.querySelector('.stolon-sidecar-title').textContent, 'media');
    assert.ok(sidecar.querySelector('[data-role="disk-alerts"]'));
    assert.equal(sidecar.querySelector('[data-role="new-folder"]'), null);
});
