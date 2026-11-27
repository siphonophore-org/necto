// necto_troubleshooting.js
// The Troubleshooting tool. It draws one collapsible section per tool from the registry, with its checks and
// repairs, and a tool with none yet shows that it is coming.

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
    // Reloads what each section shows, each time the page shows.
    function loadPage() {
        entries.forEach(function (entry) { if (entry.handle && entry.handle.load) entry.handle.load(); });
    }

    nectoModule.createNectoPage(view, { page: 'necto_troubleshooting', bind: bindPage, load: loadPage });
}
