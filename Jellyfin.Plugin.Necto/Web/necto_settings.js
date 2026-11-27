// necto_settings.js
// The Settings tool. It draws the System sections and then each tool's, as collapsible sections from the registry, each
// saving on its own.

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
    var reveals = {};
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
    // Draws every section once, each filled section ending in its own Save Bar. The tile of each tool with a switch,
    // the first checkbox of its section, follows the switch, and the settings under the switch show while it is on.
    function bindPage(shared, page) {
        var saveBars = {};
        bars = nectoModule.createSaveBars(page);
        entries = nectoModule.drawSections(view.querySelector('#settingsSections'), 'settings', shared, page, function (content, handle, tool) {
            // Each section clamps its own numbers on save, so a value out of range never blocks Save.
            saveBars[tool.key] = bars.add(content, { form: null, onSave: function () { return saveSection(shared, handle); } });
        });
        nectoModule.nectoTools().filter(function (tool) { return tool.visibleWhen; }).forEach(function (tool) {
            var entry = entries.find(function (e) { return e.key === tool.key; });
            var box = entry && entry.collapsible.element.querySelector('input[type="checkbox"]');
            if (!box) return;
            tiles[tool.key] = nectoModule.bindTileToSwitch(entry.collapsible.element, box);
            reveals[tool.key] = revealUnder(box, saveBars[tool.key] && saveBars[tool.key].element);
        });
    }

    // revealUnder
    // Moves the settings after a section's switch, up to its Save Bar, into one block that shows while the switch is on.
    //
    // Inputs
    // box [HTMLInputElement]: The section's switch.
    // end [HTMLElement]: The section's Save Bar, which stays outside the block. [optional]
    //
    // Output
    // [function]: Shows or hides the block by the switch, for after code sets it.
    function revealUnder(box, end) {
        var content = box.closest('[data-role="section-content"]');
        var start = box;
        while (start.parentNode !== content) start = start.parentNode;
        var block = nectoModule.h('div', { class: 'stolon-stack', 'data-role': 'switched' });
        while (start.nextSibling && start.nextSibling !== end) block.appendChild(start.nextSibling);
        content.insertBefore(block, end || null);
        var show = function () { block.classList.toggle('stolon-hidden', !box.checked); };
        box.addEventListener('change', show);
        show();
        return show;
    }

    // openAsked
    // Opens and scrolls to the section another tool asked for, such as the Dashboard's Notifications card, once.
    function openAsked() {
        var key = view.getAttribute('data-stolon-tool-param');
        if (!key) return;
        view.removeAttribute('data-stolon-tool-param');
        var entry = entries.find(function (e) { return e.key === key; });
        if (!entry) return;
        entry.collapsible.setValue(true);
        if (entry.collapsible.element.scrollIntoView) entry.collapsible.element.scrollIntoView({ block: 'start' });
    }

    // loadPage
    // Reads the config and fills every section from it, each time the page shows. A tile follows its switch once
    // the section has loaded, since some sections read their own route.
    function loadPage(shared) {
        openAsked();
        shared.getConfig().then(function (config) {
            entries.forEach(function (entry) {
                if (!entry.handle) return;
                Promise.resolve(entry.handle.load(config)).then(function () {
                    if (tiles[entry.key]) tiles[entry.key].refresh();
                    if (reveals[entry.key]) reveals[entry.key]();
                }, function () { /* the section reports its own failure */ });
            });
            if (bars) bars.clean();
        }).catch(function () {
            shared.setStatus('settingsStatus', shared.t.tr('ErrorLoadSettings'), true);
        });
    }

    nectoModule.createNectoPage(view, { page: 'necto_settings', bind: bindPage, load: loadPage });
}
