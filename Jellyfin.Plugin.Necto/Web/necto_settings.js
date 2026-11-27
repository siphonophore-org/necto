// necto_settings.js
// The Settings tool. It draws one collapsible section per tool from the registry, each saving on its own.

import * as nectoModule from './configurationpage?name=necto_shared.js';
import './configurationpage?name=necto_groups_sections.js';
import './configurationpage?name=necto_wireguard_sections.js';
import './configurationpage?name=necto_scripts_sections.js';
import './configurationpage?name=necto_storage_sections.js';
import './configurationpage?name=necto_pages_sections.js';
import './configurationpage?name=necto_notifications_sections.js';
import './configurationpage?name=necto_invites_sections.js';
import './configurationpage?name=necto_ddns_sections.js';
import './configurationpage?name=necto_proxy_sections.js';
import './configurationpage?name=necto_requests_sections.js';

export default function (view) {
    'use strict';

    var entries = [];
    var bars = [];

    // saveSection
    // Saves one section's fields, through the section's own save when it has one, such as a tool whose settings
    // save through its own route, and otherwise into a fresh copy of the config.
    //
    // Inputs
    // shared [object]: The Necto helper bag.
    // handle [object]: The section's handle.
    //
    // Output
    // [Promise]: Resolves once the save landed.
    function saveSection(shared, handle) {
        var saving = handle.save ? handle.save() : shared.saveConfigWith(function (config) { handle.apply(config); });
        return saving.then(function (saved) {
            // A tool turned on or off joins or leaves the sidebar at once.
            return shared.getConfig().then(function (config) {
                nectoModule.setToolsOff(config);
                if (shared.onTabsChanged) shared.onTabsChanged();
                return saved;
            }, function () { return saved; });
        });
    }

    // bindPage
    // Draws every section once, each filled section ending in its own Save Bar.
    function bindPage(shared, page) {
        page.guardUnsaved(function () { return bars.some(function (b) { return b.dirty; }); });
        entries = nectoModule.drawSections(view.querySelector('#settingsSections'), 'settings', shared, page, function (content, handle) {
            var entry = { dirty: false, bar: null };
            entry.bar = nectoModule.createSaveBar({
                fill: true,
                onSave: function () {
                    return saveSection(shared, handle).then(function () {
                        entry.dirty = false;
                    });
                }
            });
            content.appendChild(entry.bar.element);
            var mark = function (e) {
                if (entry.bar.element.contains(e.target)) return;
                entry.dirty = true;
                entry.bar.setDirty(true);
            };
            content.addEventListener('input', mark);
            content.addEventListener('change', mark);
            bars.push(entry);
        });
    }

    // loadPage
    // Reads the config and fills every section from it, each time the page shows.
    function loadPage(shared) {
        shared.getConfig().then(function (config) {
            entries.forEach(function (entry) { if (entry.handle) entry.handle.load(config); });
            bars.forEach(function (b) { b.dirty = false; b.bar.setDirty(false); });
        }).catch(function () {
            shared.setStatus('settingsStatus', shared.t.tr('ErrorLoadSettings'), true);
        });
    }

    nectoModule.createNectoPage(view, { page: 'necto_settings', bind: bindPage, load: loadPage });
}
