// necto_netfields.js
// The helpers the Proxy page and its Settings section share: a field's description, and reading a choice the server
// sends as its number or its name.

import * as nectoModule from './configurationpage?name=necto_shared.js';

var h = nectoModule.h;

// describe
// Builds a field's description, or nothing when there is none.
//
// Inputs
// text [string]: The description.
// className [string]: The class. Defaults to fieldDescription. [optional]
//
// Output
// [HTMLElement|null]: The description.
export function describe(text, className) {
    return text ? h('div', { class: className || 'fieldDescription', text: text }) : null;
}

// enumName
// Reads a choice the server may send as its number or its name.
//
// Inputs
// value [number|string]: The stored choice.
// names [string[]]: The choice names in the server's order.
//
// Output
// [string]: The choice's name, or the first name when it is unknown.
export function enumName(value, names) {
    if (typeof value === 'number') return names[value] || names[0];
    return names.indexOf(value) >= 0 ? value : names[0];
}
