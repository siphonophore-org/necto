// necto.js
// The page the menu opens. It builds Stolon's tool app with every Necto tool, the dashboard first.

import * as nectoModule from './configurationpage?name=necto_shared.js';

export default function (view) {
    nectoModule.createNectoApp(view);
}
