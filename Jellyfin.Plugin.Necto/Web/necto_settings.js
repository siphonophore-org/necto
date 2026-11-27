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
import './configurationpage?name=necto_menulinks_sections.js';

export default function (view) {
    'use strict';

    var entries = [];
    var tiles = {};
    var bars = null;

    // saveSection
    // Saves one section's fields, through the section's own save when it has one, such as a tool whose settings
    // save through its own route, and otherwise into a fresh copy of the config. The sidebar follows the saved
    // config at once, so a tool turned on or off joins or leaves it.
    //
    // Inputs
    // shared [object]: The Necto helper bag.
    // handle [object]: The section's handle.
    //
    // Output
    // [Promise]: Resolves with the saved line the section gives, if any, once the save landed.
    function saveSection(shared, handle) {
        if (handle.save) {
            return handle.save().then(function (saved) {
                shared.onConfigChanged(null);
                return typeof saved === 'string' ? saved : undefined;
            });
        }
        return shared.saveConfigWith(function (config) { handle.apply(config); }).then(function (config) {
            shared.onConfigChanged(config);
        });
    }

    // bindPage
    // Draws every section once, each filled section ending in its own Save Bar, and the tile of each tool with a
    // switch following its switch, the first checkbox of its section.
    function bindPage(shared, page) {
        bars = nectoModule.createSaveBars(page);
        entries = nectoModule.drawSections(view.querySelector('#settingsSections'), 'settings', shared, page, function (content, handle) {
            // Each section clamps its own numbers on save, so a value out of range never blocks Save.
            bars.add(content, { form: null, onSave: function () { return saveSection(shared, handle); } });
        });
        nectoModule.nectoTools().filter(function (tool) { return tool.visibleWhen; }).forEach(function (tool) {
            var entry = entries.find(function (e) { return e.key === tool.key; });
            var box = entry && entry.collapsible.element.querySelector('input[type="checkbox"]');
            if (box) tiles[tool.key] = nectoModule.bindTileToSwitch(entry.collapsible.element, box);
        });
    }

    // loadPage
    // Reads the config and fills every section from it, each time the page shows. A tile follows its switch once
    // the section has loaded, since some sections read their own route.
    function loadPage(shared) {
        shared.getConfig().then(function (config) {
            entries.forEach(function (entry) {
                if (!entry.handle) return;
                Promise.resolve(entry.handle.load(config)).then(function () {
                    if (tiles[entry.key]) tiles[entry.key].refresh();
                }, function () { /* the section reports its own failure */ });
            });
            if (bars) bars.clean();
        }).catch(function () {
            shared.setStatus('settingsStatus', shared.t.tr('ErrorLoadSettings'), true);
        });
    }

    nectoModule.createNectoPage(view, { page: 'necto_settings', bind: bindPage, load: loadPage });
}
