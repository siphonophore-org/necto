// necto_netfields.js
// The field builders the DDNS and Proxy pages and their Settings and Troubleshooting sections share: Jellyfin
// inputs, checkboxes and selects with a label and a description, reset rows, and reading a spec of fields.

import * as nectoModule from './configurationpage?name=necto_shared.js';

var el = nectoModule.el;

// Ids for fields, so a label always names its own field.
var fieldSeq = 0;

// fieldId
// Makes an id no other field has.
//
// Inputs
// name [string]: What the field is.
//
// Output
// [string]: The id.
export function fieldId(name) {
    fieldSeq += 1;
    return 'necto-net-' + name + '-' + fieldSeq;
}

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
    return text ? el('div', { class: className || 'fieldDescription', text: text }) : null;
}

// inputField
// Builds a labelled Jellyfin input with its description.
//
// Inputs
// opts [object]: label, help, type (text, number, password, url), placeholder, min, max, field (its data-field), and
//   value.
//
// Output
// [object]: element is the container and input the input.
export function inputField(opts) {
    var id = fieldId(opts.field || 'input');
    var input = el('input', {
        is: 'emby-input',
        type: opts.type || 'text',
        id: id,
        'data-field': opts.field || null,
        placeholder: opts.placeholder || null,
        min: opts.min == null ? null : String(opts.min),
        max: opts.max == null ? null : String(opts.max),
        autocomplete: opts.type === 'password' ? 'new-password' : null
    });
    if (opts.value != null) input.value = String(opts.value);
    var element = el('div', { class: 'inputContainer' }, [
        el('label', { class: 'inputLabel inputLabelUnfocused', for: id, text: opts.label }),
        input,
        describe(opts.help)
    ]);
    return { element: element, input: input };
}

// secretField
// Builds a password input for a stored secret, with a button that clears the stored value. The caller binds it with
// shared.bindSecretField(input, hasStored, { clearButton: clear }).
//
// Inputs
// opts [object]: label, help, field, and clearText, the button's label.
//
// Output
// [object]: element is the container, input the input, and clear the button.
export function secretField(opts) {
    var made = inputField({ label: opts.label, help: opts.help, field: opts.field, type: 'password' });
    var clear = el('button', { is: 'emby-button', type: 'button', class: 'raised stolon-button stolon-secondary stolon-small stolon-hidden', 'data-action': 'clear-' + (opts.field || 'secret') }, [el('span', { text: opts.clearText })]);
    made.element.appendChild(clear);
    made.clear = clear;
    return made;
}

// textareaField
// Builds a labelled text area, one value per line.
//
// Inputs
// opts [object]: label, help, placeholder, field, and value as an array of lines or a text.
//
// Output
// [object]: element is the container and input the text area.
export function textareaField(opts) {
    var id = fieldId(opts.field || 'lines');
    var input = el('textarea', { is: 'emby-textarea', id: id, rows: '4', 'data-field': opts.field || null, placeholder: opts.placeholder || null });
    input.value = Array.isArray(opts.value) ? opts.value.join('\n') : (opts.value || '');
    var element = el('div', { class: 'inputContainer' }, [
        el('label', { class: 'inputLabel inputLabelUnfocused', for: id, text: opts.label }),
        input,
        describe(opts.help)
    ]);
    return { element: element, input: input };
}

// checkField
// Builds a Jellyfin checkbox with its description.
//
// Inputs
// opts [object]: label, help, field, and checked.
//
// Output
// [object]: element is the container and input the checkbox.
export function checkField(opts) {
    var input = el('input', { type: 'checkbox', is: 'emby-checkbox', id: fieldId(opts.field || 'check'), 'data-field': opts.field || null });
    input.checked = !!opts.checked;
    var element = el('div', { class: 'checkboxContainer' + (opts.help ? ' checkboxContainer-withDescription' : '') }, [
        el('label', {}, [input, el('span', { text: opts.label })]),
        describe(opts.help, 'fieldDescription checkboxFieldDescription')
    ]);
    return { element: element, input: input };
}

// selectField
// Builds a labelled Jellyfin select.
//
// Inputs
// opts [object]: label, help, field, options as [{ value, label }], and value.
//
// Output
// [object]: element is the container, input the select, and setOptions(options, value) refills it.
export function selectField(opts) {
    var id = fieldId(opts.field || 'select');
    var input = el('select', { is: 'emby-select', id: id, 'data-field': opts.field || null });
    var help = describe(opts.help || ' ');
    var setOptions = function (options, value) {
        input.textContent = '';
        (options || []).forEach(function (o) { input.appendChild(el('option', { value: o.value, text: o.label })); });
        if (value != null) input.value = String(value);
    };
    setOptions(opts.options, opts.value);
    if (!opts.help) help.textContent = '';
    var element = el('div', { class: 'selectContainer' }, [
        el('label', { class: 'selectLabel', for: id, text: opts.label }),
        input,
        help
    ]);
    return { element: element, input: input, help: help, setOptions: setOptions };
}

// readNumber
// Reads a whole number from a field, or null when it is blank or not a number.
//
// Inputs
// input [HTMLInputElement]: The field.
//
// Output
// [number|null]: The number.
export function readNumber(input) {
    var raw = String(input.value || '').trim();
    if (raw === '') return null;
    var n = parseInt(raw, 10);
    return isNaN(n) ? null : n;
}

// readLines
// Reads a text area as its non-empty trimmed lines.
//
// Inputs
// input [HTMLTextAreaElement]: The text area.
//
// Output
// [string[]]: The lines.
export function readLines(input) {
    return String(input.value || '').split(/\r?\n/).map(function (x) { return x.trim(); }).filter(function (x) { return x; });
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
