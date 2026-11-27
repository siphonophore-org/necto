// necto_storage.js
// The Storage tool: what needs a look, the space every disk in use holds, one row per disk or share, filtered to disks
// in use or all disks, and a sidecar with each disk's facts, space, alerts, libraries, folders, and forms.

import * as nectoModule from './configurationpage?name=necto_shared.js';
import * as sections from './configurationpage?name=necto_storage_sections.js';

var h = nectoModule.h;

// The string that says what each file system the server can offer is best for, by its name on the server.
export var FILE_SYSTEM_HELP = {
    ext4: 'StorageHelpFsExt4',
    xfs: 'StorageHelpFsXfs',
    btrfs: 'StorageHelpFsBtrfs',
    exfat: 'StorageHelpFsExfat',
    ntfs: 'StorageHelpFsNtfs',
    apfs: 'StorageHelpFsApfs'
};

// The prefix of every Storage string, which the server's error keys leave out.
var KEY_PREFIX = 'Storage';

// serverText
// Gives the text of an error key the server sent, such as ErrorFormatGone.
//
// Inputs
// t [object]: The strings.
// key [string]: The key as the server sends it.
//
// Output
// [string]: The text.
export function serverText(t, key) {
    return t.tr(KEY_PREFIX + key);
}

// stateOf
// Gives the badge text and color of a disk's state.
//
// Inputs
// t [object]: The strings.
// disk [object]: One disk from GET Storage/Disks.
//
// Output
// [object]: { label, color }.
export function stateOf(t, disk) {
    if (disk.State === 'Missing') return { label: t.tr('StorageStateMissing'), color: 'red' };
    if (disk.State === 'Unresponsive') return { label: t.tr('StorageStateUnresponsive'), color: 'orange' };
    if (disk.State === 'Unformatted') return { label: t.tr('StorageStateUnformatted'), color: 'blue' };
    if (disk.State === 'NotMounted') return { label: t.tr('StorageStateNotMounted'), color: 'gray' };
    if (disk.Health === 'Failing') return { label: t.tr('StorageHealthFailing'), color: 'red' };
    if (disk.Level === 'Error') return { label: t.tr('StorageLevelFull'), color: 'red' };
    if (disk.Level === 'Warning') return { label: t.tr('DashboardStateAttention'), color: 'orange' };
    if (!disk.InUse) return { label: t.tr('StorageStateUnused'), color: 'gray' };
    return { label: t.tr('LabelAvailable'), color: 'green' };
}

// nameOf
// Gives the name shown for a disk. The system disk's mount point says little, so it reads System.
//
// Inputs
// t [object]: The strings.
// disk [object]: One disk from GET Storage/Disks.
//
// Output
// [string]: The name.
export function nameOf(t, disk) {
    return disk.IsSystem ? t.tr('LabelSystem') : disk.Name;
}

// isRead
// Tells whether a disk was read, so its space is known.
//
// Inputs
// disk [object]: One disk from GET Storage/Disks.
//
// Output
// [boolean]: true when its size and free space are known.
export function isRead(disk) {
    return disk.State === 'Available' && !!disk.TotalBytes;
}

// typeOf
// Gives the tag for what kind of disk it is: the system disk, a network share, an external disk, or a local one.
//
// Inputs
// t [object]: The strings.
// disk [object]: One disk from GET Storage/Disks.
//
// Output
// [object]: { label, icon, color } for a record list tag.
export function typeOf(t, disk) {
    if (disk.IsSystem) return { label: t.tr('LabelSystem'), icon: 'computer', color: 'gray' };
    if (disk.Kind === 'Share') return { label: t.tr('StorageType.Network'), icon: 'lan', color: 'purple' };
    if (disk.Kind === 'External') return { label: t.tr('External'), icon: 'usb', color: 'blue' };
    return { label: t.tr('LabelLocal'), icon: 'storage', color: 'gray' };
}

// meterOptions
// Builds a usage meter's options for a disk: its used share as the label over the bar, and the bar turning orange
// and red at the disk's own alert points.
//
// Inputs
// disk [object]: One disk from GET Storage/Disks.
//
// Output
// [object]: The options for createUsageMeter.
export function meterOptions(disk) {
    var used = disk.TotalBytes - disk.FreeBytes;
    var options = { bytes: true, used: used, total: disk.TotalBytes, warning: 101, danger: 101 };
    options.label = (disk.TotalBytes ? Math.round(used * 100 / disk.TotalBytes) : 0) + '%';
    if (disk.WarningPercent !== null && disk.WarningPercent !== undefined) options.warning = disk.WarningPercent;
    if (disk.ErrorPercent !== null && disk.ErrorPercent !== undefined) options.danger = disk.ErrorPercent;
    return options;
}

// tagsOf
// Lists a disk's tags: its type, then any missing folders, a hot disk, and a worn SSD. The system disk is already
// named System, so it takes no type tag.
//
// Inputs
// t [object]: The strings.
// disk [object]: One disk from GET Storage/Disks.
//
// Output
// [object[]]: The record list tags.
export function tagsOf(t, disk) {
    var tags = disk.IsSystem ? [] : [typeOf(t, disk)];
    var gone = (disk.MissingFolders || []).length;
    if (gone) tags.push({ label: t.plural(gone, 'StorageFolderMissing', 'StorageFoldersMissing'), icon: 'folder_off', color: 'orange' });
    if (disk.Hot) tags.push({ label: t.tr('StorageAlertHot'), icon: 'thermostat', color: 'red' });
    if (disk.Worn) tags.push({ label: t.tr('StorageAlertWorn'), icon: 'battery_alert', color: 'orange' });
    return tags;
}

// secondaryOf
// Gives a disk row's second line: the mount point, or for a disk with nothing mounted, its device and size, and how
// long it has been missing.
//
// Inputs
// t [object]: The strings.
// shared [object]: The Necto helper bag.
// disk [object]: One disk from GET Storage/Disks.
//
// Output
// [string]: The line.
export function secondaryOf(t, shared, disk) {
    // A disk with nothing mounted has no mount point, so its device and size say which one it is.
    var unmounted = disk.State === 'Unformatted' || disk.State === 'NotMounted';
    var line = unmounted
        ? [disk.Device, disk.TotalBytes ? shared.formatSize(disk.TotalBytes) : null].filter(Boolean).join(' · ')
        : disk.MountPoint;
    if (disk.MissingSinceUtc) line += ' · ' + t.format('StorageMissingSince', shared.formatRelativeTime(new Date(disk.MissingSinceUtc)));
    return line;
}

// issuesOf
// Lists what needs a look on a disk the libraries use, each as one sentence. A problem its row already shows, in its
// badge, its tags, or its bar, is marked inRow, so the notice above the list leaves it out.
//
// Inputs
// t [object]: The strings.
// shared [object]: The Necto helper bag.
// disk [object]: A disk in use.
//
// Output
// [object[]]: { text, serious, inRow } for each problem, serious being true for what needs a look now.
export function issuesOf(t, shared, disk) {
    var name = nameOf(t, disk);
    var issues = [];
    if (disk.State === 'Missing') {
        var since = disk.MissingSinceUtc ? shared.formatRelativeTime(new Date(disk.MissingSinceUtc)) : '';
        issues.push({ text: t.format('StorageIssueMissing', name, since), serious: true, inRow: true });
    } else if (disk.State === 'Unresponsive') {
        issues.push({ text: t.format('StorageIssueUnresponsive', name), serious: true, inRow: true });
    }
    if (disk.State !== 'Available') return issues;
    var used = disk.TotalBytes ? Math.round((disk.TotalBytes - disk.FreeBytes) * 100 / disk.TotalBytes) : 0;
    if (disk.Health === 'Failing') issues.push({ text: t.format('StorageIssueFailing', name), serious: true, inRow: true });
    if (disk.Level === 'Error') issues.push({ text: t.format('StorageIssueFull', name, used, shared.formatSize(disk.FreeBytes)), serious: true, inRow: true });
    if (disk.Level === 'Warning') issues.push({ text: t.format('StorageIssueLow', name, used, shared.formatSize(disk.FreeBytes)), serious: false, inRow: true });
    if (disk.Hot) issues.push({ text: t.format('StorageIssueHot', name, disk.TemperatureCelsius), serious: false, inRow: true });
    if (disk.Worn) issues.push({ text: t.format('StorageIssueWorn', name, disk.WearPercent), serious: false, inRow: true });
    if ((disk.MissingFolders || []).length) issues.push({ text: t.format('StorageIssueFolders', name), serious: false, inRow: true });
    // Jellyfin's own folders it cannot write to stop it working, so they come first and red.
    var readOnly = disk.ReadOnlyFolders || [];
    var own = (disk.Folders || []).filter(function (f) { return f.Server && readOnly.indexOf(f.Path) >= 0; });
    own.forEach(function (f) { issues.push({ text: t.format('StorageIssueServerReadOnly', t.tr(f.Library), f.Path), serious: true }); });
    if (readOnly.length > own.length) issues.push({ text: t.format('StorageIssueReadOnly', name), serious: false });
    return issues;
}

// allIssues
// Lists what needs a look on every disk in use, the serious problems first.
//
// Inputs
// t [object]: The strings.
// shared [object]: The Necto helper bag.
// used [object[]]: The disks in use.
//
// Output
// [object[]]: { text, serious } for each problem.
export function allIssues(t, shared, used) {
    var issues = [];
    used.forEach(function (disk) { issues = issues.concat(issuesOf(t, shared, disk)); });
    return issues.sort(function (a, b) { return (b.serious ? 1 : 0) - (a.serious ? 1 : 0); });
}

// pageIssues
// Lists what needs a look that no disk row shows, such as a folder Jellyfin cannot write to, the serious ones first.
//
// Inputs
// t [object]: The strings.
// shared [object]: The Necto helper bag.
// used [object[]]: The disks in use.
//
// Output
// [object[]]: { text, serious } for each problem.
export function pageIssues(t, shared, used) {
    return allIssues(t, shared, used).filter(function (issue) { return !issue.inRow; });
}

// spaceOf
// Adds up the size, used, and free space of the disks in use that could be read. Free takes the color of the
// fullest disk's alert.
//
// Inputs
// used [object[]]: The disks in use.
//
// Output
// [object|null]: { total, free, color, count }, or null when no disk could be read.
export function spaceOf(used) {
    var read = used.filter(isRead);
    if (!read.length) return null;
    return {
        total: read.reduce(function (sum, d) { return sum + d.TotalBytes; }, 0),
        free: read.reduce(function (sum, d) { return sum + d.FreeBytes; }, 0),
        color: read.some(function (d) { return d.Level === 'Error'; }) ? 'red' : read.some(function (d) { return d.Level === 'Warning'; }) ? 'orange' : 'green',
        count: read.length
    };
}

// librariesOf
// Lists the library names a disk or folder can join, without Collections, which Jellyfin keeps for itself.
//
// Inputs
// folders [object[]]: The virtual folders Jellyfin gives.
//
// Output
// [string[]]: The names, sorted.
export function librariesOf(folders) {
    return (folders || []).filter(function (l) { return l.CollectionType !== 'boxsets'; })
        .map(function (l) { return l.Name; }).filter(Boolean).sort();
}

// validFolderName
// Tells whether a folder name can be made at the top of a disk.
//
// Inputs
// name [string]: The name, trimmed.
//
// Output
// [boolean]: true when it holds no slash or reserved character and is not a dot name.
export function validFolderName(name) {
    return !!name && !/[\\/:*?"<>|]/.test(name) && name !== '.' && name !== '..' && !/\.$/.test(name);
}

// validVolumeName
// Tells whether a name can be given to a disk being formatted.
//
// Inputs
// name [string]: The name, trimmed.
//
// Output
// [boolean]: true for up to 11 letters, numbers, spaces, dashes, or underscores, starting with a letter or number.
export function validVolumeName(name) {
    return /^[A-Za-z0-9][A-Za-z0-9 _-]{0,10}$/.test(name);
}

// withOverride
// Puts a disk's own alerts into the Storage settings, or takes them out to use the global alerts.
//
// Inputs
// storage [object]: The Storage settings, changed in place.
// mountPoint [string]: The disk.
// own [object|null]: { WarningStop, ErrorStop }, or null to use the global alerts.
export function withOverride(storage, mountPoint, own) {
    var rest = (storage.DiskOverrides || []).filter(function (o) { return o.MountPoint !== mountPoint; });
    if (own) rest.push({ MountPoint: mountPoint, WarningStop: own.WarningStop, ErrorStop: own.ErrorStop });
    storage.DiskOverrides = rest;
}

// withAddress
// Puts a share's address into the Storage settings, or takes it out when blank.
//
// Inputs
// storage [object]: The Storage settings, changed in place.
// mountPoint [string]: The share.
// address [string]: Where it is mounted from, or empty.
export function withAddress(storage, mountPoint, address) {
    var rest = (storage.ShareAddresses || []).filter(function (a) { return a.MountPoint !== mountPoint; });
    if (address) rest.push({ MountPoint: mountPoint, Address: address });
    storage.ShareAddresses = rest;
}

export default function (view) {
    'use strict';

    var shared = null;
    var page = null;
    var t = null;
    var list = null;
    var tabs = null;
    var disks = [];
    var checkedAt = null;
    var spinner = null;
    var checking = false;
    var sidecar = null;

    // $
    // Finds an element of the page by its id.
    function $(id) { return view.querySelector('#' + id); }

    // token
    // Reads one of Stolon's color tokens, for a chart that takes a color code.
    function token(name) {
        return getComputedStyle(view).getPropertyValue(name).trim();
    }

    // actionRow
    // Builds the end of a sidecar section that saves: why it cannot, then its button.
    //
    // Inputs
    // label [string]: The button's label.
    // destructive [boolean]: Whether the button loses data.
    //
    // Output
    // [object]: { element, button, fail(text) }.
    function actionRow(label, destructive) {
        var problem = h('div', { class: 'stolon-error stolon-hidden necto-storage-actions-why', role: 'status' });
        var button = h('button', { is: 'emby-button', type: 'button', class: 'raised stolon-button ' + (destructive ? 'stolon-destructive' : 'stolon-primary') }, [h('span', { text: label })]);
        var row = h('div', { class: 'necto-storage-actions' }, [problem, button]);
        return {
            element: row,
            button: button,
            fail: function (text) {
                problem.textContent = text || '';
                problem.classList.toggle('stolon-hidden', !text);
            }
        };
    }

    // section
    // Builds a sidecar section led by its icon tile, always open, whose content stacks its parts.
    //
    // Inputs
    // title [string]: The section's title.
    // icon [string]: The Material icon of its tile.
    //
    // Output
    // [object]: { element, content }.
    function section(title, icon) {
        var content = h('div', { class: 'stolon-stack' });
        var made = nectoModule.createCollapsibleSection({ title: title, icon: icon, collapsible: false, content: content });
        return { element: made.element, content: content };
    }

    // asideOf
    // Builds the end of a disk's row: a usage meter of its space when the disk was read, then its one state badge.
    function asideOf(disk) {
        var state = stateOf(t, disk);
        var badge = h('span', { class: 'necto-storage-disk-badge' });
        badge.innerHTML = shared.badge(state.color, state.label);
        var aside = h('div', { class: 'necto-storage-disk-aside' });
        if (isRead(disk)) aside.appendChild(nectoModule.createUsageMeter(meterOptions(disk)).element);
        aside.appendChild(badge);
        return aside;
    }

    // itemOf
    // Turns a disk into a record list row.
    function itemOf(disk) {
        return { id: disk.MountPoint || disk.Device, title: nameOf(t, disk), secondary: secondaryOf(t, shared, disk), tags: tagsOf(t, disk), aside: asideOf(disk), asideFill: true, disk: disk };
    }

    // stopItem
    // Gives one alert as a fact: the alert as set, and the used space at which it fires on this disk.
    function stopItem(disk, stop, percent, name) {
        if (!stop || !stop.Enabled || percent === null || percent === undefined) return null;
        var value = Math.round(stop.Value * 10) / 10;
        var setting = stop.Unit === 'Gigabytes' ? t.format('StorageStopGigabytesFree', value) : t.format('StorageStopPercentUsed', value);
        return { label: name, value: t.format('StorageStopAt', setting, shared.formatSize(Math.round(disk.TotalBytes * percent / 100))) };
    }

    // spaceSection
    // Builds the sidecar's space: a donut of used and free space, with the share used in its center and each part in
    // its legend.
    function spaceSection(disk) {
        var used = disk.TotalBytes - disk.FreeBytes;
        var usedColor = disk.Level === 'Error' ? token('--stolon-red-fg') : disk.Level === 'Warning' ? token('--stolon-orange-fg') : token('--stolon-blue-fg');
        var chart = nectoModule.createPieChart({
            label: t.tr('StorageLabelSpace'),
            donut: true,
            size: 'small',
            centerText: Math.round(used * 100 / disk.TotalBytes) + '%',
            centerLabel: t.tr('StorageLabelUsed'),
            emptyText: t.tr('StorageNoticeDiskUnused'),
            format: function (value) { return shared.formatSize(value); },
            slices: [
                { label: t.tr('StorageLabelUsed'), value: used, color: usedColor },
                { label: t.tr('StorageLabelFree'), value: disk.FreeBytes, color: token('--stolon-gray-fg') }
            ]
        });
        var part = section(t.tr('StorageLabelSpace'), 'pie_chart');
        part.element.setAttribute('data-role', 'disk-space');
        part.content.appendChild(chart.element);
        return part.element;
    }

    // librariesSection
    // Builds the sidecar's libraries, one tag each.
    function librariesSection(disk) {
        var part = section(t.tr('HeaderLibraries'), 'video_library');
        part.content.appendChild(h('div', { class: 'stolon-tags' }, disk.Libraries.map(function (l) { return h('span', { class: 'stolon-tag stolon-tag-pill', text: l.Name }); })));
        return part.element;
    }

    // foldersSection
    // Builds the sidecar's folders as a table: whether each was found, the folder, and its library.
    function foldersSection(disk) {
        var part = section(t.tr('Folders'), 'folder');
        part.element.setAttribute('data-role', 'disk-folders');
        var gone = disk.State !== 'Available';
        var rows = disk.Folders.map(function (folder) {
            var missing = gone || disk.MissingFolders.indexOf(folder.Path) >= 0;
            var readOnly = (disk.ReadOnlyFolders || []).indexOf(folder.Path) >= 0;
            // One of Jellyfin's own folders is named as the dashboard names it, such as Cache.
            return {
                Path: folder.Path,
                Owner: folder.Server ? t.tr(folder.Library) : folder.Library,
                State: missing ? { label: t.tr('TextNotFound'), color: 'orange' }
                    : readOnly ? { label: t.tr('StorageFolderReadOnly'), color: 'orange' }
                    : { label: t.tr('StorageFolderFound'), color: 'green' }
            };
        });
        var table = nectoModule.createDataTable({
            columns: [
                { key: 'State', label: t.tr('LabelStatus'), icon: 'check_circle', value: function (r) { return r.State.label; }, render: function (r) { return nectoModule.rowBadge(r.State); } },
                { key: 'Path', label: t.tr('LabelFolder'), icon: 'folder', render: function (r) { return h('span', { class: 'stolon-mono', text: r.Path }); } },
                { key: 'Owner', label: t.tr('LabelUsage'), icon: 'video_library' }
            ],
            rows: rows,
            rowKey: 'Path',
            label: t.tr('Folders'),
            emptyText: t.tr('StorageEmptyFolders')
        });
        part.content.appendChild(table.element);
        return part.element;
    }

    // alertsSection
    // Builds the sidecar's full disk alerts: the points they fire at on this disk, then the global ones, or the disk's
    // own while Use its own alerts is on. Its save joins the sidecar's Save.
    //
    // Inputs
    // disk [object]: The disk.
    // savers [function[]]: The sidecar's saves, which this section adds its own to.
    function alertsSection(disk, savers) {
        var part = section(t.tr('Alerts'), 'notifications_active');
        part.element.setAttribute('data-role', 'disk-alerts');
        var holder = part.content;
        if (isRead(disk)) {
            var points = [
                stopItem(disk, disk.WarningStop, disk.WarningPercent, t.tr('StorageAlertLow')),
                stopItem(disk, disk.ErrorStop, disk.ErrorPercent, t.tr('StorageAlertFull'))
            ].filter(Boolean);
            if (points.length) holder.appendChild(nectoModule.createDefinitionList({ items: points }).element);
        }
        shared.getConfig().then(function (config) {
            var storage = sections.storageOf(config);
            var globals = { WarningStop: storage.WarningStop, ErrorStop: storage.ErrorStop };
            var own = storage.DiskOverrides.filter(function (o) { return o.MountPoint === disk.MountPoint; })[0];
            var editor = sections.createAlertEditor(t, { heading: false, onChange: changed });
            editor.setValue(own ? { WarningStop: own.WarningStop, ErrorStop: own.ErrorStop } : globals);
            var indent = h('div', { class: 'stolon-indent' }, [editor.element]);
            var toggle = nectoModule.createCheckboxField({
                label: t.tr('StorageLabelOwnAlerts'),
                help: t.tr('StorageHelpOwnAlerts'),
                checked: !!own
            });
            toggle.input.addEventListener('change', changed);
            [toggle.element, indent].forEach(function (node) { holder.appendChild(node); });

            // changed
            // Shows the editor while the disk has its own alerts.
            function changed() {
                indent.classList.toggle('stolon-hidden', !toggle.input.checked);
            }
            changed();

            savers.push(function () {
                var on = !!toggle.input.checked;
                var value = editor.getValue();
                var error = on ? editor.errorText() : '';
                if (error) return Promise.reject(new Error(error));
                return shared.saveConfigWith(function (latest) {
                    latest.Storage = latest.Storage || {};
                    withOverride(latest.Storage, disk.MountPoint, on ? value : null);
                });
            });
        }).catch(function () {
            holder.appendChild(h('div', { class: 'stolon-error', text: t.tr('StorageErrorLoadSettings') }));
        });
        return part.element;
    }

    // addressSection
    // Builds the sidecar's share address: where the share is mounted from, so a missing share can be mounted again.
    // Its save joins the sidecar's Save, which checks the disks again and so remounts the share when it is missing.
    function addressSection(disk, savers) {
        var windows = /^[A-Za-z]:|^\\\\/.test(disk.MountPoint);
        var part = section(t.tr('StorageHeaderShareAddress'), 'lan');
        var field = nectoModule.createInputField({
            label: t.tr('LabelAddress'),
            help: t.tr(windows ? 'StorageHelpShareAddressWindows' : 'StorageHelpShareAddress'),
            placeholder: windows ? '\\\\nas\\Media' : 'smb://nas.example.com/Media',
            value: disk.ShareAddress || ''
        });
        field.input.classList.add('stolon-mono');
        field.input.setAttribute('spellcheck', 'false');
        part.content.appendChild(field.element);
        savers.push(function () {
            var address = field.input.value.trim();
            return shared.saveConfigWith(function (latest) {
                latest.Storage = latest.Storage || {};
                withAddress(latest.Storage, disk.MountPoint, address);
            });
        });
        return part.element;
    }

    // librarySelect
    // Builds the Add to library select, with None first.
    function librarySelect(libraries) {
        var id = 'necto-storage-library-' + Math.random().toString(36).slice(2, 8);
        var select = h('select', { is: 'emby-select', id: id }, [h('option', { value: '', text: t.tr('None') })].concat(libraries.map(function (name) {
            return h('option', { value: name, text: name });
        })));
        return {
            element: h('div', { class: 'selectContainer' }, [h('label', { class: 'selectLabel', for: id, text: t.tr('StorageLabelAddToLibrary') }), select]),
            select: select
        };
    }

    // newFolderSection
    // Builds the sidecar's New folder form: a name, made at the top of the disk, and an optional library to add it
    // to. Once it is made the sidecar closes and the disks are checked again.
    function newFolderSection(disk) {
        var part = section(t.tr('StorageHeaderNewFolder'), 'create_new_folder');
        part.element.setAttribute('data-role', 'new-folder');
        ApiClient.getVirtualFolders().catch(function () { return []; }).then(function (found) {
            var id = 'necto-storage-folder-' + Math.random().toString(36).slice(2, 8);
            var input = h('input', { is: 'emby-input', type: 'text', id: id, maxlength: '100' });
            var library = librarySelect(librariesOf(found));
            part.content.appendChild(h('div', { class: 'stolon-field-row necto-storage-folder-fields' }, [
                h('div', { class: 'inputContainer' }, [
                    h('label', { class: 'inputLabel inputLabelUnfocused', for: id, text: t.tr('StorageLabelFolderName') }),
                    input,
                    h('div', { class: 'fieldDescription', text: t.tr('StorageHelpFolderName') })
                ]),
                library.element
            ]));
            var row = actionRow(t.tr('StorageButtonCreateFolder'));
            part.content.appendChild(row.element);

            row.button.addEventListener('click', function () {
                var name = input.value.trim();
                if (!validFolderName(name)) {
                    row.fail(t.tr('StorageErrorFolderName'));
                    return;
                }
                row.fail('');
                row.button.disabled = true;
                shared.request('Storage/Folders', 'POST', { MountPoint: disk.MountPoint, Name: name, Library: library.select.value || null }).then(function (outcome) {
                    if (outcome && outcome.Created && outcome.ErrorKey) {
                        // Made, but the library refused it, so the sidecar stays to say so.
                        check();
                        row.fail(serverText(t, outcome.ErrorKey));
                        return;
                    }
                    if (outcome && outcome.Created) {
                        closeDisk();
                        check();
                        return;
                    }
                    row.fail(outcome && outcome.ErrorKey ? serverText(t, outcome.ErrorKey) : t.format('StorageErrorFolderFailed', (outcome && outcome.Error) || ''));
                }).catch(function (err) {
                    return shared.errorText(err, t.tr('StorageErrorFolderRequest')).then(row.fail);
                }).then(function () {
                    row.button.disabled = false;
                });
            });
        });
        return part.element;
    }

    // formatSection
    // Builds the sidecar's format form for a disk with nothing mounted: its new name, its file system, and an optional
    // library, with where it will be mounted. Format asks for a name to be typed first, since it erases the disk.
    function formatSection(disk) {
        var part = section(t.tr('StorageHeaderFormat'), 'cleaning_services');
        part.element.setAttribute('data-role', 'format');
        Promise.all([
            shared.request('Storage/Format'),
            ApiClient.getVirtualFolders().catch(function () { return []; })
        ]).then(function (loaded) {
            var options = loaded[0] || {};
            var systems = options.FileSystems || [];
            if (!systems.length) {
                part.content.appendChild(h('div', { class: 'stolon-info', text: t.tr('StorageNoticeFormatUnavailable') }));
                return;
            }

            var nameId = 'necto-storage-volume-' + Math.random().toString(36).slice(2, 8);
            var input = h('input', { is: 'emby-input', type: 'text', id: nameId, maxlength: '11' });
            input.value = 'Media';
            part.content.appendChild(h('div', { class: 'inputContainer' }, [
                h('label', { class: 'inputLabel inputLabelUnfocused', for: nameId, text: t.tr('Name') }),
                input,
                h('div', { class: 'fieldDescription', text: t.tr('StorageHelpVolumeName') })
            ]));

            // One card per file system, each saying what it is best for.
            var radio = 'necto-storage-fs-' + Math.random().toString(36).slice(2, 8);
            var choices = h('div', { class: 'stolon-choice-group', role: 'radiogroup', 'aria-label': t.tr('StorageLabelFileSystem') }, systems.map(function (value) {
                return h('label', { class: 'stolon-choice' }, [
                    h('input', { type: 'radio', name: radio, value: value }),
                    h('span', { class: 'stolon-choice-name', text: sections.named(t, value) }),
                    h('span', { class: 'stolon-choice-text', text: FILE_SYSTEM_HELP[value] ? t.tr(FILE_SYSTEM_HELP[value]) : '' })
                ]);
            }));
            part.content.appendChild(h('div', { class: 'stolon-field' }, [h('label', { text: t.tr('StorageLabelFileSystem') }), choices]));
            var system = nectoModule.createChoiceGroup(choices, {});
            system.set(systems[0]);

            var library = librarySelect(librariesOf(loaded[1]));
            part.content.appendChild(library.element);

            // Where it will be mounted, which follows the name.
            var where = h('div', { class: 'stolon-warn' });
            part.content.appendChild(where);
            var describe = function () {
                var name = input.value.trim();
                var at = options.MountRoot ? options.MountRoot + '/' + (options.DashesForSpaces ? name.replace(/ /g, '-') : name) : '';
                where.textContent = options.MountRoot ? t.format('StorageNoticeFormatMountAt', at) : t.tr('StorageNoticeFormatDriveLetter');
            };
            input.addEventListener('input', describe);
            describe();

            // A disk that holds data is erased first, so the button says so and the device name must be typed.
            var erase = disk.State === 'NotMounted';
            var action = t.tr(erase ? 'StorageButtonEraseFormat' : 'StorageButtonFormatDisk');
            var row = actionRow(action, true);
            part.content.appendChild(row.element);

            row.button.addEventListener('click', function () {
                var name = input.value.trim();
                if (!validVolumeName(name)) {
                    row.fail(t.tr('StorageErrorFormatName'));
                    return;
                }
                row.fail('');
                var label = disk.Model || disk.Device;
                var device = String(disk.Device).replace(/^\/dev\//, '');
                var fileSystem = sections.named(t, system.get());
                var message = erase
                    ? t.format('StorageConfirmErase', label, fileSystem, (disk.Contents || []).join(', '))
                    : t.format('StorageConfirmFormat', label, fileSystem);
                var typed = erase ? device : name;
                nectoModule.confirmDialog(message, {
                    title: erase ? t.format('StorageTitleErase', label) : t.format('StorageTitleFormat', label),
                    confirmText: t.tr(erase ? 'StorageButtonErase' : 'StorageHeaderFormat'),
                    destructive: true,
                    typeToConfirm: typed,
                    typeLabel: t.format('StorageLabelTypeToConfirm', typed)
                }).then(function (yes) {
                    if (!yes) return;
                    row.button.disabled = true;
                    (row.button.querySelector('span') || row.button).textContent = t.tr('StorageStatusFormatting');
                    return shared.request('Storage/Format', 'POST', {
                        Device: disk.Device,
                        Name: name,
                        FileSystem: system.get(),
                        Erase: erase,
                        Library: library.select.value || null
                    }).then(function (outcome) {
                        if (outcome && outcome.Formatted && outcome.ErrorKey) {
                            // Formatted and mounted, but the library refused the folder, so the sidecar stays to say so.
                            check();
                            row.fail(serverText(t, outcome.ErrorKey));
                            return;
                        }
                        if (outcome && outcome.Formatted) {
                            closeDisk();
                            check();
                            return;
                        }
                        row.fail(outcome && outcome.ErrorKey ? serverText(t, outcome.ErrorKey) : t.format('StorageErrorFormatFailed', (outcome && outcome.Error) || ''));
                    });
                }).catch(function (err) {
                    return shared.errorText(err, t.tr('StorageErrorFormatRequest')).then(row.fail);
                }).then(function () {
                    row.button.disabled = false;
                    (row.button.querySelector('span') || row.button).textContent = action;
                });
            });
        }).catch(function () {
            part.content.appendChild(h('div', { class: 'stolon-error', text: t.tr('StorageErrorLoadFormat') }));
        });
        return part.element;
    }

    // libraryMount
    // Tells whether a folder is where a library expects one of its disks.
    function libraryMount(path) {
        return !!path && disks.some(function (d) { return d.InUse && d.MountPoint === path; });
    }

    // healthFacts
    // Lists what a disk's SMART data says: its health as a badge first, then its temperature, power-on time, and
    // wear when it reports them. A disk with no SMART data, such as a share, shows none.
    function healthFacts(disk) {
        if (!disk.Health || disk.Health === 'Unknown') return [];
        var good = disk.Health === 'Good';
        var facts = [{ label: t.tr('StorageLabelHealth'), value: { badge: { label: t.tr(good ? 'StorageHealthGood' : 'StorageHealthFailing'), color: good ? 'green' : 'red' } } }];
        if (disk.TemperatureCelsius !== null && disk.TemperatureCelsius !== undefined) {
            // Red past the Too hot alert, as the row's tag says.
            var celsius = t.format('StorageValueCelsius', disk.TemperatureCelsius);
            facts.push({ label: t.tr('StorageLabelTemperature'), value: disk.Hot ? { badge: { label: celsius, color: 'red' } } : celsius });
        }
        if (disk.PowerOnHours) facts.push({ label: t.tr('StorageLabelPowerOnTime'), value: t.format('HoursMany', disk.PowerOnHours.toLocaleString()) });
        if (disk.WearPercent !== null && disk.WearPercent !== undefined) {
            // Green while most of the rated writes are left, orange past half, red past the Worn out alert.
            var wearColor = disk.Worn ? 'red' : disk.WearPercent >= 50 ? 'orange' : 'green';
            facts.push({ label: t.tr('StorageLabelWear'), value: { badge: { label: t.format('StorageValueWear', disk.WearPercent), color: wearColor } } });
        }
        return facts;
    }

    // closeDisk
    // Closes the disk's sidecar, when one is open.
    function closeDisk() {
        if (sidecar) sidecar.close();
        sidecar = null;
    }

    // openDisk
    // Opens a sidecar with one disk's facts, space, alerts, libraries, folders, and forms. A disk with settings, its
    // alerts or its share address, saves them with Save on the sidecar's shelf.
    function openDisk(disk) {
        var body = h('div', { class: 'stolon-stack', 'data-role': 'disk' });
        var savers = [];
        var state = stateOf(t, disk);
        var health = healthFacts(disk);
        // The status shows as the sidecar's badge, so the facts start with its health.
        var facts = [];
        if (health.length) facts.push(health.shift());
        facts.push({ label: t.tr('LabelType'), value: typeOf(t, disk).label });
        if (disk.FileSystem) facts.push({ label: t.tr('StorageLabelFileSystem'), value: sections.named(t, disk.FileSystem) });
        if (disk.TotalBytes) facts.push({ label: t.tr('LabelSize'), value: shared.formatSize(disk.TotalBytes) });
        if (disk.Contents && disk.Contents.length) facts.push({ label: t.tr('StorageLabelHolds'), value: disk.Contents.join(', ') });
        if (disk.MountsAt) facts.push({ label: t.tr('StorageLabelMountsAt'), value: disk.MountsAt, mono: true });
        if (disk.MountPoint) facts.push({ label: t.tr('StorageLabelMountPoint'), value: disk.MountPoint, mono: true });
        if (disk.Device) facts.push({ label: t.tr('LabelDevice'), value: disk.Device, mono: true });
        if (disk.Model) facts.push({ label: t.tr('StorageLabelModel'), value: disk.Model });
        health.forEach(function (fact) { facts.push(fact); });
        body.appendChild(nectoModule.createDefinitionList({ items: facts }).element);

        if (isRead(disk)) {
            body.appendChild(spaceSection(disk));
        } else if (disk.MissingSinceUtc) {
            var key = disk.State === 'Missing' ? 'StorageNoticeDiskMissingSince' : 'StorageNoticeDiskUnresponsiveSince';
            body.appendChild(h('div', { class: 'stolon-error', text: t.format(key, shared.formatDate(disk.MissingSinceUtc)) }));
        }

        if (disk.State === 'Unformatted') {
            body.appendChild(h('div', { class: 'stolon-info', text: t.tr('StorageNoticeDiskUnformatted') }));
            body.appendChild(formatSection(disk));
        } else if (disk.State === 'NotMounted' && libraryMount(disk.MountsAt)) {
            // A library's own disk, dropped off its mount point, is mounted again rather than erased.
            body.appendChild(h('div', { class: 'stolon-error', text: t.format('StorageNoticeLibraryDisk', disk.MountsAt) }));
        } else if (disk.State === 'NotMounted') {
            body.appendChild(h('div', { class: 'stolon-warn', text: t.tr('StorageNoticeDiskNotMounted') }));
            body.appendChild(formatSection(disk));
        } else if (disk.InUse) {
            // A share, or a disk gone missing, can be given the address it is mounted from.
            if (disk.TakesAddress && (disk.Kind === 'Share' || disk.State !== 'Available')) body.appendChild(addressSection(disk, savers));
            body.appendChild(alertsSection(disk, savers));
            if (disk.Libraries.length) body.appendChild(librariesSection(disk));
            body.appendChild(foldersSection(disk));
            if (disk.State === 'Available') body.appendChild(newFolderSection(disk));
        } else {
            body.appendChild(alertsSection(disk, savers));
            body.appendChild(h('div', { class: 'stolon-info', text: t.tr('StorageNoticeDiskUnused') }));
            body.appendChild(newFolderSection(disk));
        }

        // Why the last Save failed, at the end of the body, over the shelf.
        var problem = h('div', { class: 'stolon-error stolon-hidden', role: 'status', 'data-role': 'save-problem' });
        body.appendChild(problem);
        var takesSave = disk.State !== 'Unformatted' && disk.State !== 'NotMounted';

        closeDisk();
        sidecar = nectoModule.openSidecar({
            title: nameOf(t, disk),
            subtitle: disk.MountPoint || disk.Device,
            badge: { label: state.label, color: state.color },
            body: body,
            size: 'large',
            page: page,
            autofocus: false,
            buttons: takesSave ? [
                { text: t.tr('Save'), className: 'raised stolon-button stolon-primary', action: function (close, dismiss, button) { saveDisk(savers, problem, button); } }
            ] : undefined,
            onClose: function () { sidecar = null; }
        });
    }

    // saveDisk
    // Runs the sidecar's saves one after another, then closes it and checks the disks again. A save that fails keeps
    // the sidecar open and says why.
    //
    // Inputs
    // savers [function[]]: Each returns its save's promise.
    // problem [HTMLElement]: The line that says why a save failed.
    // button [HTMLElement]: Save, turned off while the saves run.
    function saveDisk(savers, problem, button) {
        problem.classList.add('stolon-hidden');
        button.disabled = true;
        return savers.reduce(function (done, save) {
            return done.then(save);
        }, Promise.resolve()).then(function () {
            closeDisk();
            check();
        }, function (err) {
            return shared.errorText(err, t.tr('StorageErrorSaveSettings')).then(function (text) {
                problem.textContent = text;
                problem.classList.remove('stolon-hidden');
            });
        }).then(function () {
            button.disabled = false;
        });
    }

    // renderAttention
    // Draws the notice above the list for what needs a look that no disk row shows: one line per problem, red when any
    // needs a look now, else orange. With nothing of the kind it shows nothing.
    function renderAttention(used) {
        var banner = $('storageAttention');
        banner.textContent = '';
        var issues = pageIssues(t, shared, used);
        shared.setVisible(banner, issues.length > 0);
        if (!issues.length) return;
        var kind = issues.some(function (i) { return i.serious; }) ? 'error' : 'warn';
        var items = issues.length > 1 ? issues.map(function (issue) { return issue.text; }) : null;
        banner.appendChild(nectoModule.createBanner({ kind: kind, text: issues.length === 1 ? issues[0].text : '', items: items, dismissible: false }).element);
    }

    // renderSpace
    // Draws how many disks Jellyfin uses, and the used and free space of those that could be read, added up.
    function renderSpace(used) {
        var cards = $('storageSpaceCards');
        var space = spaceOf(used);
        cards.classList.toggle('stolon-hidden', !space);
        cards.textContent = '';
        if (!space) return;
        shared.renderCards(cards, [
            { label: t.tr('StorageHeaderDisks'), count: used.length, color: 'blue' },
            { label: t.tr('StorageLabelUsed'), count: shared.formatSize(space.total - space.free) },
            { label: t.tr('StorageLabelFree'), count: shared.formatSize(space.free), color: space.color }
        ]);
    }

    // render
    // Draws the notice and the space cards for the disks in use, and the rows the filter shows.
    function render() {
        var used = disks.filter(function (d) { return d.InUse; });
        renderAttention(used);
        renderSpace(used);
        var shown = tabs.getValue() === 'all' ? disks : used;
        list.setItems(shown.map(itemOf));
    }

    // showChecked
    // Remembers when the disks were checked, for the Refresh button's hover, or shows why the check failed.
    function showChecked(error) {
        var status = $('storageCheckStatus');
        status.textContent = error || '';
        status.classList.toggle('stolon-hidden', !error);
        if (!error) checkedAt = new Date();
    }

    // setChecking
    // Swaps the Refresh button's icon for a spinner while a check runs, and back when it ends.
    function setChecking(busy) {
        var button = $('btnStorageCheck');
        button.disabled = busy;
        $('storageRefreshIcon').classList.toggle('stolon-hidden', busy);
        if (busy && !spinner) {
            spinner = nectoModule.createSpinner({ size: 'small', label: t.tr('StorageStatusChecking') });
            button.insertBefore(spinner.element, button.firstChild);
        } else if (!busy && spinner) {
            spinner.destroy();
            spinner = null;
        }
    }

    // check
    // Asks the server to check every disk, then draws the result.
    function check() {
        if (checking) return;
        checking = true;
        setChecking(true);
        shared.request('Storage/Disks').then(function (result) {
            disks = result || [];
            render();
            showChecked(null);
        }).catch(function (err) {
            return shared.errorText(err, t.tr('StorageErrorCheckDisks')).then(showChecked);
        }).then(function () {
            checking = false;
            setChecking(false);
        });
    }

    // refreshButton
    // Builds Refresh, the icon at the end of the Disks header that checks every disk again. Its icon turns into a
    // spinner while a check runs.
    function refreshButton() {
        return h('button', { is: 'emby-button', type: 'button', id: 'btnStorageCheck', class: 'stolon-row-btn', title: t.tr('Refresh'), 'aria-label': t.tr('Refresh') }, [
            h('span', { id: 'storageRefreshIcon', class: 'material-icons', 'aria-hidden': 'true', text: 'refresh' })
        ]);
    }

    // bindPage
    // Draws the Disks section once: Refresh at the end of its header, and its sub-tabs for the disks Jellyfin uses and
    // every disk over the disk list.
    function bindPage(bag, handle) {
        shared = bag;
        page = handle;
        t = shared.t;
        list = nectoModule.createRecordList({
            label: t.tr('StorageHeaderDisks'),
            emptyText: t.tr('StorageEmptyDisks'),
            onOpen: function (item) { openDisk(item.disk); }
        });
        tabs = nectoModule.createSubTabs({
            label: t.tr('StorageHeaderDisks'),
            tabs: [{ id: 'used', label: t.tr('LabelJellyfin') }, { id: 'all', label: t.tr('All') }],
            value: 'used',
            render: function () { return null; },
            onChange: function (id) {
                tabs.getPanel(id).appendChild(list.element);
                render();
            }
        });
        tabs.getPanel('used').appendChild(list.element);
        var status = h('div', { id: 'storageCheckStatus', class: 'stolon-error stolon-hidden', role: 'status' });
        var refresh = refreshButton();
        var content = h('div', { class: 'stolon-stack' }, [status, tabs.element]);
        var disksSection = nectoModule.createCollapsibleSection({ title: t.tr('StorageHeaderDisks'), icon: 'storage', collapsible: false, content: content, aside: refresh });
        $('storageDisks').appendChild(disksSection.element);
        refresh.addEventListener('click', check);
        refresh.addEventListener('mouseenter', function () {
            this.title = checkedAt ? t.format('StorageLastChecked', shared.formatRelativeTime(checkedAt)) : t.tr('Refresh');
        });
        page.addCleanup(closeDisk);
    }

    nectoModule.createNectoPage(view, { page: 'necto_storage', bind: bindPage, load: function () { check(); } });
}
