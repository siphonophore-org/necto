// necto_storage.js
// The Storage tool: what needs a look, the space every disk in use holds, one row per disk or share, filtered to disks
// in use or all disks, and a sidecar with each disk's facts, space, alerts, libraries, folders, and forms.

import * as nectoModule from './configurationpage?name=necto_shared.js';
import * as sections from './configurationpage?name=necto_storage_sections.js';

var el = nectoModule.el;

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
    if (disk.Level === 'Warning') return { label: t.tr('StorageLevelLow'), color: 'orange' };
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
// Builds a usage meter's options for a disk, turning orange and red at the disk's own alert points.
//
// Inputs
// disk [object]: One disk from GET Storage/Disks.
//
// Output
// [object]: The options for createUsageMeter.
export function meterOptions(disk) {
    var options = { bytes: true, used: disk.TotalBytes - disk.FreeBytes, total: disk.TotalBytes, warning: 101, danger: 101 };
    if (disk.WarningPercent !== null && disk.WarningPercent !== undefined) options.warning = disk.WarningPercent;
    if (disk.ErrorPercent !== null && disk.ErrorPercent !== undefined) options.danger = disk.ErrorPercent;
    return options;
}

// tagsOf
// Lists a disk's tags: its type, then any missing folders, a hot disk, and a worn SSD.
//
// Inputs
// t [object]: The strings.
// disk [object]: One disk from GET Storage/Disks.
//
// Output
// [object[]]: The record list tags.
export function tagsOf(t, disk) {
    var tags = [typeOf(t, disk)];
    var gone = (disk.MissingFolders || []).length;
    if (gone) tags.push({ label: gone === 1 ? t.tr('StorageFolderMissing') : t.format('StorageFoldersMissing', gone), icon: 'folder_off', color: 'orange' });
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
// Lists what needs a look on a disk the libraries use, each as one sentence.
//
// Inputs
// t [object]: The strings.
// shared [object]: The Necto helper bag.
// disk [object]: A disk in use.
//
// Output
// [object[]]: { text, serious } for each problem, serious being true for what needs a look now.
export function issuesOf(t, shared, disk) {
    var name = nameOf(t, disk);
    var issues = [];
    if (disk.State === 'Missing') {
        var since = disk.MissingSinceUtc ? shared.formatRelativeTime(new Date(disk.MissingSinceUtc)) : '';
        issues.push({ text: t.format('StorageIssueMissing', name, since), serious: true });
    } else if (disk.State === 'Unresponsive') {
        issues.push({ text: t.format('StorageIssueUnresponsive', name), serious: true });
    }
    if (disk.State !== 'Available') return issues;
    var used = disk.TotalBytes ? Math.round((disk.TotalBytes - disk.FreeBytes) * 100 / disk.TotalBytes) : 0;
    if (disk.Health === 'Failing') issues.push({ text: t.format('StorageIssueFailing', name), serious: true });
    if (disk.Level === 'Error') issues.push({ text: t.format('StorageIssueFull', name, used, shared.formatSize(disk.FreeBytes)), serious: true });
    if (disk.Level === 'Warning') issues.push({ text: t.format('StorageIssueLow', name, used, shared.formatSize(disk.FreeBytes)), serious: false });
    if (disk.Hot) issues.push({ text: t.format('StorageIssueHot', name, disk.TemperatureCelsius), serious: false });
    if (disk.Worn) issues.push({ text: t.format('StorageIssueWorn', name, disk.WearPercent), serious: false });
    if ((disk.MissingFolders || []).length) issues.push({ text: t.format('StorageIssueFolders', name), serious: false });
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
    var filter = null;
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
        var problem = el('div', { class: 'stolon-error stolon-hidden', role: 'status' });
        var button = el('button', { is: 'emby-button', type: 'button', class: 'raised stolon-button ' + (destructive ? 'destructive' : 'primary') }, [el('span', { text: label })]);
        var row = el('div', { class: 'necto-storage-actions' }, [problem, button]);
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
    // Builds a sidecar section with its heading.
    function section(title) {
        return el('div', { class: 'necto-storage-section' }, [el('h3', { class: 'stolon-subsection-title', text: title })]);
    }

    // asideOf
    // Builds the end of a disk's row: its state badge, and a usage meter when the disk was read.
    function asideOf(disk) {
        var aside = el('div', { class: 'necto-storage-disk-aside' });
        var state = stateOf(t, disk);
        var badge = el('span');
        badge.innerHTML = shared.badge(state.color, state.label);
        aside.appendChild(badge);
        if (isRead(disk)) aside.appendChild(nectoModule.createUsageMeter(meterOptions(disk)).element);
        return aside;
    }

    // itemOf
    // Turns a disk into a record list row.
    function itemOf(disk) {
        return { id: disk.MountPoint || disk.Device, title: nameOf(t, disk), secondary: secondaryOf(t, shared, disk), tags: tagsOf(t, disk), aside: asideOf(disk), disk: disk };
    }

    // stopCard
    // Gives one alert as a count card: the used space at which it fires on this disk, labeled with the alert as set.
    function stopCard(disk, stop, percent, color, name) {
        if (!stop || !stop.Enabled || percent === null || percent === undefined) return null;
        var value = Math.round(stop.Value * 10) / 10;
        var setting = stop.Unit === 'Gigabytes' ? t.format('StorageStopGigabytesFree', value) : t.format('StorageStopPercentUsed', value);
        return { label: name + ' · ' + setting, count: shared.formatSize(Math.round(disk.TotalBytes * percent / 100)), color: color };
    }

    // spaceSection
    // Builds the sidecar's space: a pie chart of used and free space beside the Size, Used, and Free cards, and the
    // Storage low and Storage full cards below them.
    function spaceSection(disk) {
        var used = disk.TotalBytes - disk.FreeBytes;
        var percent = Math.round(used * 100 / disk.TotalBytes);
        var usedColor = disk.Level === 'Error' ? token('--stolon-red-fg') : disk.Level === 'Warning' ? token('--stolon-orange-fg') : token('--stolon-blue-fg');
        var chart = nectoModule.createPieChart({
            label: t.tr('StorageLabelSpace'),
            donut: true,
            size: 'medium',
            showLegend: false,
            centerText: percent + '%',
            centerLabel: t.tr('StorageLabelUsed'),
            emptyText: t.tr('StorageNoticeDiskUnused'),
            format: function (value) { return shared.formatSize(value); },
            slices: [
                { label: t.tr('StorageLabelUsed'), value: used, color: usedColor },
                { label: t.tr('StorageLabelFree'), value: disk.FreeBytes, color: token('--stolon-gray-fg') }
            ]
        });
        var figures = el('div', { class: 'necto-storage-figures' });
        var cards = el('div', { class: 'stolon-cards' });
        figures.appendChild(cards);
        var freeColor = disk.Level === 'Error' ? 'red' : disk.Level === 'Warning' ? 'orange' : 'green';
        shared.renderCards(cards, [
            { label: t.tr('LabelSize'), count: shared.formatSize(disk.TotalBytes) },
            { label: t.tr('StorageLabelUsed'), count: shared.formatSize(used) },
            { label: t.tr('StorageLabelFree'), count: shared.formatSize(disk.FreeBytes), color: freeColor }
        ]);
        // The alerts sit on their own row, which is left out while both are off.
        var stops = [
            stopCard(disk, disk.WarningStop, disk.WarningPercent, 'orange', t.tr('StorageAlertLow')),
            stopCard(disk, disk.ErrorStop, disk.ErrorPercent, 'red', t.tr('StorageAlertFull'))
        ].filter(Boolean);
        if (stops.length) {
            var alertCards = el('div', { class: 'stolon-cards' });
            figures.appendChild(alertCards);
            shared.renderCards(alertCards, stops);
        }
        return el('div', { class: 'necto-storage-space' }, [chart.element, figures]);
    }

    // librariesSection
    // Builds the sidecar's libraries, one tag each.
    function librariesSection(disk) {
        var part = section(t.tr('HeaderLibraries'));
        part.appendChild(el('div', { class: 'stolon-tags' }, disk.Libraries.map(function (l) { return el('span', { class: 'stolon-tag stolon-tag-pill', text: l.Name }); })));
        return part;
    }

    // foldersSection
    // Builds the sidecar's folders as a table: whether each was found, the folder, and its library.
    function foldersSection(disk) {
        var part = section(t.tr('Folders'));
        var gone = disk.State !== 'Available';
        var rows = disk.Folders.map(function (folder) {
            var missing = gone || disk.MissingFolders.indexOf(folder.Path) >= 0;
            var readOnly = (disk.ReadOnlyFolders || []).indexOf(folder.Path) >= 0;
            var badge = missing ? shared.badge('orange', t.tr('TextNotFound'))
                : readOnly ? shared.badge('orange', t.tr('StorageFolderReadOnly'))
                : shared.badge('green', t.tr('StorageFolderFound'));
            // One of Jellyfin's own folders is named as the dashboard names it, such as Cache.
            var owner = folder.Server ? t.tr(folder.Library) : folder.Library;
            return '<tr><td>' + badge + '</td><td class="stolon-mono">' + nectoModule.escapeHtml(folder.Path) + '</td><td>' + nectoModule.escapeHtml(owner) + '</td></tr>';
        }).join('');
        var wrap = el('div', { class: 'stolon-static-table-scroll' });
        wrap.innerHTML = '<table class="stolon-static-table"><thead><tr><th>' + nectoModule.escapeHtml(t.tr('LabelStatus')) + '</th><th>'
            + nectoModule.escapeHtml(t.tr('LabelFolder')) + '</th><th>' + nectoModule.escapeHtml(t.tr('LabelUsage')) + '</th></tr></thead><tbody>' + rows + '</tbody></table>';
        part.appendChild(wrap);
        return part;
    }

    // alertsSection
    // Builds the sidecar's full disk alerts: the global ones, or the disk's own while Use its own alerts is on, with
    // Save. Saving closes the sidecar and checks the disks again.
    function alertsSection(disk) {
        var part = section(t.tr('Alerts'));
        part.setAttribute('data-role', 'disk-alerts');
        shared.getConfig().then(function (config) {
            var storage = sections.storageOf(config);
            var globals = { WarningStop: storage.WarningStop, ErrorStop: storage.ErrorStop };
            var own = storage.DiskOverrides.filter(function (o) { return o.MountPoint === disk.MountPoint; })[0];
            var editor = sections.createAlertEditor(t, { heading: false, onChange: changed });
            editor.setValue(own ? { WarningStop: own.WarningStop, ErrorStop: own.ErrorStop } : globals);
            var inherited = el('div', { class: 'fieldDescription', text: t.format('StorageInheritedAlerts', sections.alertText(t, globals)) });
            var indent = el('div', { class: 'stolon-indent' }, [editor.element]);
            var toggle = nectoModule.createCheckbox({
                label: t.tr('StorageLabelOwnAlerts'),
                description: t.tr('StorageHelpOwnAlerts'),
                size: 'large',
                checked: !!own,
                onChange: changed
            });
            var row = actionRow(t.tr('Save'));
            [toggle.element, inherited, indent, row.element].forEach(function (node) { part.appendChild(node); });
            var start = JSON.stringify([!!own, editor.getValue()]);

            // changed
            // Shows the editor while the disk has its own alerts, and Save once they differ from what was saved.
            function changed() {
                var on = !!toggle.getValue();
                indent.classList.toggle('stolon-hidden', !on);
                inherited.classList.toggle('stolon-hidden', on);
                row.button.disabled = JSON.stringify([on, editor.getValue()]) === start;
            }
            changed();

            row.button.addEventListener('click', function () {
                var on = !!toggle.getValue();
                var value = editor.getValue();
                var error = on ? editor.errorText() : '';
                row.fail(error);
                if (error) return;
                row.button.disabled = true;
                shared.saveConfigWith(function (latest) {
                    latest.Storage = latest.Storage || {};
                    withOverride(latest.Storage, disk.MountPoint, on ? value : null);
                }).then(function () {
                    closeDisk();
                    check();
                }).catch(function (err) {
                    row.button.disabled = false;
                    return shared.errorText(err, t.tr('StorageErrorSaveSettings')).then(row.fail);
                });
            });
        }).catch(function () {
            part.appendChild(el('div', { class: 'stolon-error', text: t.tr('StorageErrorLoadSettings') }));
        });
        return part;
    }

    // addressSection
    // Builds the sidecar's share address: where the share is mounted from, so a missing share can be mounted again.
    // Saving closes the sidecar and checks the disks again, which remounts the share when it is missing.
    function addressSection(disk) {
        var windows = /^[A-Za-z]:|^\\\\/.test(disk.MountPoint);
        var part = section(t.tr('StorageHeaderShareAddress'));
        var input = el('input', { is: 'emby-input', type: 'text', class: 'stolon-mono', spellcheck: 'false', 'aria-label': t.tr('StorageHeaderShareAddress'), placeholder: windows ? '\\\\nas\\Media' : 'smb://nas.example.com/Media' });
        input.value = disk.ShareAddress || '';
        part.appendChild(el('div', { class: 'inputContainer' }, [input, el('div', { class: 'fieldDescription', text: t.tr(windows ? 'StorageHelpShareAddressWindows' : 'StorageHelpShareAddress') })]));
        var row = actionRow(t.tr('Save'));
        part.appendChild(row.element);
        var start = input.value.trim();
        row.button.disabled = true;
        input.addEventListener('input', function () { row.button.disabled = input.value.trim() === start; });
        row.button.addEventListener('click', function () {
            var address = input.value.trim();
            row.button.disabled = true;
            shared.saveConfigWith(function (latest) {
                latest.Storage = latest.Storage || {};
                withAddress(latest.Storage, disk.MountPoint, address);
            }).then(function () {
                closeDisk();
                check();
            }).catch(function (err) {
                row.button.disabled = false;
                return shared.errorText(err, t.tr('StorageErrorSaveSettings')).then(row.fail);
            });
        });
        return part;
    }

    // librarySelect
    // Builds the Add to library select, with None first.
    function librarySelect(libraries) {
        var id = 'necto-storage-library-' + Math.random().toString(36).slice(2, 8);
        var select = el('select', { is: 'emby-select', id: id }, [el('option', { value: '', text: t.tr('None') })].concat(libraries.map(function (name) {
            return el('option', { value: name, text: name });
        })));
        return {
            element: el('div', { class: 'selectContainer' }, [el('label', { class: 'selectLabel', for: id, text: t.tr('StorageLabelAddToLibrary') }), select]),
            select: select
        };
    }

    // newFolderSection
    // Builds the sidecar's New folder form: a name, made at the top of the disk, and an optional library to add it
    // to. Once it is made the sidecar closes and the disks are checked again.
    function newFolderSection(disk) {
        var part = section(t.tr('StorageHeaderNewFolder'));
        part.setAttribute('data-role', 'new-folder');
        ApiClient.getVirtualFolders().catch(function () { return []; }).then(function (found) {
            var id = 'necto-storage-folder-' + Math.random().toString(36).slice(2, 8);
            var input = el('input', { is: 'emby-input', type: 'text', id: id, maxlength: '100' });
            var library = librarySelect(librariesOf(found));
            part.appendChild(el('div', { class: 'stolon-field-row necto-storage-folder-fields' }, [
                el('div', { class: 'inputContainer' }, [
                    el('label', { class: 'inputLabel inputLabelUnfocused', for: id, text: t.tr('StorageLabelFolderName') }),
                    input,
                    el('div', { class: 'fieldDescription', text: t.tr('StorageHelpFolderName') })
                ]),
                library.element
            ]));
            var row = actionRow(t.tr('StorageButtonCreateFolder'));
            part.appendChild(row.element);

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
        return part;
    }

    // formatSection
    // Builds the sidecar's format form for a disk with nothing mounted: its new name, its file system, and an optional
    // library, with where it will be mounted. Format asks for a name to be typed first, since it erases the disk.
    function formatSection(disk) {
        var part = section(t.tr('StorageHeaderFormat'));
        part.setAttribute('data-role', 'format');
        Promise.all([
            shared.request('Storage/Format'),
            ApiClient.getVirtualFolders().catch(function () { return []; })
        ]).then(function (loaded) {
            var options = loaded[0] || {};
            var systems = options.FileSystems || [];
            if (!systems.length) {
                part.appendChild(el('div', { class: 'stolon-info', text: t.tr('StorageNoticeFormatUnavailable') }));
                return;
            }

            var nameId = 'necto-storage-volume-' + Math.random().toString(36).slice(2, 8);
            var input = el('input', { is: 'emby-input', type: 'text', id: nameId, maxlength: '11' });
            input.value = 'Media';
            part.appendChild(el('div', { class: 'inputContainer' }, [
                el('label', { class: 'inputLabel inputLabelUnfocused', for: nameId, text: t.tr('Name') }),
                input,
                el('div', { class: 'fieldDescription', text: t.tr('StorageHelpVolumeName') })
            ]));

            // One card per file system, each saying what it is best for.
            var radio = 'necto-storage-fs-' + Math.random().toString(36).slice(2, 8);
            var choices = el('div', { class: 'stolon-choice-group', role: 'radiogroup', 'aria-label': t.tr('StorageLabelFileSystem') }, systems.map(function (value) {
                return el('label', { class: 'stolon-choice' }, [
                    el('input', { type: 'radio', name: radio, value: value }),
                    el('span', { class: 'stolon-choice-name', text: sections.named(t, value) }),
                    el('span', { class: 'stolon-choice-text', text: FILE_SYSTEM_HELP[value] ? t.tr(FILE_SYSTEM_HELP[value]) : '' })
                ]);
            }));
            part.appendChild(el('div', { class: 'stolon-field' }, [el('label', { text: t.tr('StorageLabelFileSystem') }), choices]));
            var system = nectoModule.createChoiceGroup(choices, {});
            system.set(systems[0]);

            var library = librarySelect(librariesOf(loaded[1]));
            part.appendChild(library.element);

            // Where it will be mounted, which follows the name.
            var where = el('div', { class: 'stolon-warn' });
            part.appendChild(where);
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
            part.appendChild(row.element);

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
            part.appendChild(el('div', { class: 'stolon-error', text: t.tr('StorageErrorLoadFormat') }));
        });
        return part;
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
    // Opens a sidecar with one disk's facts, space, alerts, libraries, folders, and forms.
    function openDisk(disk) {
        var body = el('div', { class: 'necto-storage-disk', 'data-role': 'disk' });
        var state = stateOf(t, disk);
        var health = healthFacts(disk);
        var facts = [{ label: t.tr('LabelStatus'), value: { badge: { label: state.label, color: state.color } } }];
        if (health.length) facts.push(health.shift());
        facts.push({ label: t.tr('LabelType'), value: typeOf(t, disk).label });
        if (disk.FileSystem) facts.push({ label: t.tr('StorageLabelFileSystem'), value: sections.named(t, disk.FileSystem) });
        if ((disk.State === 'Unformatted' || disk.State === 'NotMounted') && disk.TotalBytes) facts.push({ label: t.tr('LabelSize'), value: shared.formatSize(disk.TotalBytes) });
        if (disk.Contents && disk.Contents.length) facts.push({ label: t.tr('StorageLabelHolds'), value: disk.Contents.join(', ') });
        if (disk.MountsAt) facts.push({ label: t.tr('StorageLabelMountsAt'), value: disk.MountsAt, mono: true });
        if (disk.MountPoint) facts.push({ label: t.tr('StorageLabelMountPoint'), value: disk.MountPoint, mono: true });
        if (disk.Device) facts.push({ label: t.tr('LabelDevice'), value: disk.Device, mono: true });
        if (disk.Model) facts.push({ label: t.tr('StorageLabelModel'), value: disk.Model });
        health.forEach(function (fact) { facts.push(fact); });
        body.appendChild(nectoModule.createDefinitionList({ layout: 'columns', items: facts }).element);

        if (isRead(disk)) {
            body.appendChild(spaceSection(disk));
        } else if (disk.MissingSinceUtc) {
            var key = disk.State === 'Missing' ? 'StorageNoticeDiskMissingSince' : 'StorageNoticeDiskUnresponsiveSince';
            body.appendChild(el('div', { class: 'stolon-error', text: t.format(key, shared.formatDate(disk.MissingSinceUtc)) }));
        }

        if (disk.State === 'Unformatted') {
            body.appendChild(el('div', { class: 'stolon-info', text: t.tr('StorageNoticeDiskUnformatted') }));
            body.appendChild(formatSection(disk));
        } else if (disk.State === 'NotMounted' && libraryMount(disk.MountsAt)) {
            // A library's own disk, dropped off its mount point, is mounted again rather than erased.
            body.appendChild(el('div', { class: 'stolon-error', text: t.format('StorageNoticeLibraryDisk', disk.MountsAt) }));
        } else if (disk.State === 'NotMounted') {
            body.appendChild(el('div', { class: 'stolon-warn', text: t.tr('StorageNoticeDiskNotMounted') }));
            body.appendChild(formatSection(disk));
        } else if (disk.InUse) {
            // A share, or a disk gone missing, can be given the address it is mounted from.
            if (disk.TakesAddress && (disk.Kind === 'Share' || disk.State !== 'Available')) body.appendChild(addressSection(disk));
            body.appendChild(alertsSection(disk));
            if (disk.Libraries.length) body.appendChild(librariesSection(disk));
            body.appendChild(foldersSection(disk));
            if (disk.State === 'Available') body.appendChild(newFolderSection(disk));
        } else {
            body.appendChild(alertsSection(disk));
            body.appendChild(el('div', { class: 'stolon-info', text: t.tr('StorageNoticeDiskUnused') }));
            body.appendChild(newFolderSection(disk));
        }

        closeDisk();
        sidecar = nectoModule.openSidecar({
            title: nameOf(t, disk),
            subtitle: disk.MountPoint || disk.Device,
            badge: { label: state.label, color: state.color },
            body: body,
            size: 'large',
            page: page,
            autofocus: false,
            onClose: function () { sidecar = null; }
        });
    }

    // renderAttention
    // Draws the notice above the list: one line per problem, red when any needs a look now, else orange, or one green
    // line when every disk the libraries use is healthy.
    function renderAttention(used) {
        var banner = $('storageAttention');
        banner.textContent = '';
        var issues = allIssues(t, shared, used);
        var kind = !issues.length ? 'success' : issues.some(function (i) { return i.serious; }) ? 'error' : 'warn';
        banner.className = 'stolon-' + kind + (issues.length ? ' necto-storage-attention' : '');
        var text = !issues.length ? (used.length ? t.tr('StorageNoticeAllHealthy') : t.tr('StorageNoticeNoDisks')) : issues.length === 1 ? issues[0].text : '';
        var made = nectoModule.createBanner({ kind: kind, text: text, dismissible: false });
        banner.appendChild(made.element);
        if (issues.length > 1) {
            var body = made.element.querySelector('.stolon-banner-text') || made.element;
            body.appendChild(el('ul', {}, issues.map(function (issue) { return el('li', { text: issue.text }); })));
        }
    }

    // renderSpace
    // Draws the size, used, and free space of every disk in use that could be read, added up.
    function renderSpace(used) {
        var cards = $('storageSpaceCards');
        var space = spaceOf(used);
        cards.classList.toggle('stolon-hidden', !space);
        cards.textContent = '';
        if (!space) return;
        var detail = space.count === 1 ? t.tr('StorageSpaceOneDisk') : t.format('StorageSpaceDisks', space.count);
        shared.renderCards(cards, [
            { label: t.tr('LabelSize') + ' · ' + detail, count: shared.formatSize(space.total) },
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
        var shown = filter.get() === 'all' ? disks : used;
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

    // bindPage
    // Draws the disk list and wires the filter and Refresh once.
    function bindPage(bag, handle) {
        shared = bag;
        page = handle;
        t = shared.t;
        list = nectoModule.createRecordList({
            label: t.tr('StorageHeaderDisks'),
            emptyText: t.tr('StorageEmptyDisks'),
            onOpen: function (item) { openDisk(item.disk); }
        });
        $('storageDiskList').appendChild(list.element);
        filter = nectoModule.createPillGroup($('storageDiskFilter'), { onChange: render });
        filter.set('used');
        $('btnStorageCheck').addEventListener('click', check);
        $('btnStorageCheck').addEventListener('mouseenter', function () {
            this.title = checkedAt ? t.format('StorageLastChecked', shared.formatRelativeTime(checkedAt)) : '';
        });
        page.addCleanup(closeDisk);
    }

    nectoModule.createNectoPage(view, { page: 'necto_storage', bind: bindPage, load: function () { check(); } });
}
