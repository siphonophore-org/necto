// necto_wireguard.js
// The WireGuard tool. It wires the page, which shows an empty state that says the tool is coming.

import * as nectoModule from './configurationpage?name=necto_shared.js';

export default function (view) {
    nectoModule.createNectoPage(view, { page: 'necto_wireguard' });
}
