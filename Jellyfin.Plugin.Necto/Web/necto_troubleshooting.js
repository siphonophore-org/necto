// necto_troubleshooting.js
// The Troubleshooting tool. It draws one collapsible section per tool from the registry, with its checks and
// repairs.

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

    // bindPage
    // Draws every section once, and stops their work when the page hides.
    function bindPage(shared, page) {
        entries = nectoModule.drawSections(view.querySelector('#troubleshootingSections'), 'troubleshooting', shared, page);
        page.addCleanup(function () {
            entries.forEach(function (entry) { if (entry.handle && entry.handle.destroy) entry.handle.destroy(); });
        });
    }

    // loadPage
    // Reloads what each section shows, each time the page shows, and hides the section of each tool that is off.
    function loadPage(shared) {
        entries.forEach(function (entry) { if (entry.handle && entry.handle.load) entry.handle.load(); });
        shared.getConfig().then(function (config) {
            nectoModule.setToolsOff(config);
            entries.forEach(function (entry) {
                if (entry.collapsible) entry.collapsible.element.classList.toggle('stolon-hidden', nectoModule.isToolOff(entry.key));
            });
        }, function () { /* every section stays */ });
    }

    nectoModule.createNectoPage(view, { page: 'necto_troubleshooting', bind: bindPage, load: loadPage });
}
