// necto_menulinks_sections.js
// The Menu links section of Settings. It edits the custom links in the web client's side menu, which live in the
// menuLinks of its config.json, and says when that file cannot be saved.

import * as nectoModule from './configurationpage?name=necto_shared.js';

var h = nectoModule.h;

// MENU_LINKS_ROUTE
// The route that reads and saves the links.
export var MENU_LINKS_ROUTE = 'Necto/MenuLinks';

// ICONS
// The Material icons the picker offers. Any other Material icon name can be typed.
export var ICONS = [
    'link', 'open_in_new', 'home', 'public', 'language', 'search', 'explore', 'star', 'favorite', 'bookmark',
    'info', 'help', 'support', 'contact_support', 'chat', 'forum', 'mail', 'notifications', 'campaign', 'event',
    'calendar_month', 'schedule', 'history', 'movie', 'movie_filter', 'theaters', 'live_tv', 'tv', 'videocam', 'music_note',
    'library_music', 'headphones', 'podcasts', 'radio', 'photo', 'photo_library', 'book', 'menu_book', 'auto_stories', 'sports_esports',
    'videogame_asset', 'download', 'cloud', 'storage', 'dns', 'dashboard', 'insights', 'analytics', 'bar_chart', 'settings',
    'build', 'code', 'terminal', 'security', 'lock', 'vpn_lock', 'person', 'group', 'groups', 'person_add',
    'report_problem', 'bug_report', 'request_page', 'shopping_cart', 'payments', 'description', 'article', 'web', 'apps', 'extension'
];

// iconMatches
// Lists the icons whose name holds the query, every one for a blank query.
//
// Inputs
// query [string]: What is typed in the icon field.
//
// Output
// [string[]]: The icon names, in the picker's order.
export function iconMatches(query) {
    var q = String(query || '').trim().toLowerCase().replace(/\s+/g, '_');
    return q ? ICONS.filter(function (name) { return name.indexOf(q) >= 0; }) : ICONS.slice();
}

// addressChoices
// Lists the addresses the Address field offers: the Requests and Invites user pages, and each page of Pages. They are
// written from the web client's own address, /web/, so they work under a base path too.
//
// Inputs
// t [object]: The strings.
// pages [object[]]: What Pages answered, each with Slug and Title, or null.
//
// Output
// [object[]]: Each choice's value and label.
export function addressChoices(t, pages) {
    var list = [
        { value: '../requests/', label: t.tr('MenuLinksOptionRequests') },
        { value: '../invites/', label: t.tr('MenuLinksOptionInvites') }
    ];
    (Array.isArray(pages) ? pages : []).forEach(function (p) {
        var slug = p && (p.Slug || p.slug);
        if (!slug) return;
        list.push({ value: '../pages/' + slug, label: t.format('MenuLinksOptionPage', p.Title || p.title || slug) });
    });
    return list;
}

// linkProblem
// Says what is missing from a link before it goes in the list, as a key.
//
// Inputs
// link [object]: Name, Icon, and Url.
//
// Output
// [string|null]: The key of the problem, or null.
export function linkProblem(link) {
    if (!link || !String(link.Name || '').trim()) return 'MenuLinksErrorNameRequired';
    if (!String(link.Url || '').trim()) return 'MenuLinksErrorUrlRequired';
    var icon = String(link.Icon || '').trim();
    if (icon && !/^[a-z0-9_]+$/.test(icon)) return 'MenuLinksErrorIcon';
    return null;
}

// managedText
// Says which tool keeps a link, for its sidecar, or empty for a link no tool keeps.
//
// Inputs
// link [object]: The link, with Managed.
// t [object]: The strings.
//
// Output
// [string]: The sentence.
export function managedText(link, t) {
    if (!link || !link.Managed) return '';
    return t.tr(link.Managed === 'issues' ? 'MenuLinksManagedIssues' : 'MenuLinksManagedRequests');
}

// createIconPicker
// Builds the Icon field: a text field that takes any Material icon name, over a grid of the common icons that the
// typed text narrows. Selecting an icon puts its name in the field.
//
// Inputs
// opts [object]: id is the field's id, value the starting name, and onChange(value) runs on each change.
//
// Output
// [object]: element, input, getValue, setValue.
export function createIconPicker(opts) {
    var input = h('input', { is: 'emby-input', type: 'text', id: opts.id, 'data-field': 'Icon', autocomplete: 'off', spellcheck: 'false', placeholder: opts.placeholder || '' });
    var preview = h('span', { class: 'material-icons necto-icon-preview', 'aria-hidden': 'true' });
    var grid = h('div', { class: 'necto-icon-grid', role: 'listbox', 'aria-label': opts.label || '' });

    // draw
    // Shows the icons the typed text matches, the picked one pressed.
    function draw() {
        var value = input.value.trim();
        preview.textContent = value || 'link';
        grid.textContent = '';
        var matches = iconMatches(ICONS.indexOf(value) >= 0 ? '' : value);
        matches.forEach(function (name) {
            var btn = h('button', {
                is: 'emby-button', type: 'button', class: 'stolon-row-btn necto-icon-choice', title: name, 'aria-label': name,
                role: 'option', 'aria-selected': name === value ? 'true' : 'false', 'data-icon': name
            }, [h('span', { class: 'material-icons', 'aria-hidden': 'true', text: name })]);
            btn.addEventListener('click', function () {
                input.value = name;
                draw();
                if (opts.onChange) opts.onChange(name);
            });
            grid.appendChild(btn);
        });
    }

    input.addEventListener('input', function () {
        draw();
        if (opts.onChange) opts.onChange(input.value.trim());
    });
    input.value = opts.value || '';
    draw();

    return {
        element: h('div', { class: 'necto-icon-picker' }, [h('div', { class: 'necto-icon-field' }, [preview, input]), grid]),
        input: input,
        getValue: function () { return input.value.trim(); },
        setValue: function (v) { input.value = v || ''; draw(); }
    };
}

var seq = 0;

// openLinkEditor
// Opens a link in a sidecar: its name, icon, and address. A link a tool keeps has no Remove and keeps its address.
//
// Inputs
// ctx [object]: shared, page, link (null for a new one), pages, and onDone(link) and onRemove().
function openLinkEditor(ctx) {
    var shared = ctx.shared;
    var t = shared.t;
    var link = ctx.link || { Name: '', Icon: '', Url: '' };
    var isNew = !ctx.link;
    seq += 1;
    var nameId = 'necto-menulink-name-' + seq;
    var iconId = 'necto-menulink-icon-' + seq;
    var name = h('input', { is: 'emby-input', type: 'text', id: nameId, 'data-field': 'Name', maxlength: '100' });
    name.value = link.Name || '';
    var icon = createIconPicker({ id: iconId, value: link.Icon || '', placeholder: t.tr('MenuLinksPlaceholderIcon'), label: t.tr('MenuLinksLabelIcon') });
    var address = nectoModule.createComboBox({ value: link.Url || '', suggestions: addressChoices(t, ctx.pages), placeholder: 'https://example.com' });
    address.input.setAttribute('aria-label', t.tr('LabelAddress'));
    address.input.setAttribute('data-field', 'Url');
    if (link.Managed) address.input.disabled = true;
    var problem = h('div', { class: 'stolon-error stolon-hidden', role: 'status' });
    var managed = managedText(link, t);

    var body = h('div', {}, [
        managed ? h('div', { class: 'stolon-info', text: managed }) : null,
        problem,
        h('div', { class: 'inputContainer' }, [
            h('label', { class: 'inputLabel inputLabelUnfocused', for: nameId, text: t.tr('Name') }),
            name,
            h('div', { class: 'fieldDescription', text: t.tr('MenuLinksHelpName') })
        ]),
        h('div', { class: 'inputContainer' }, [
            h('label', { class: 'inputLabel', for: iconId, text: t.tr('MenuLinksLabelIcon') }),
            icon.element,
            h('div', { class: 'fieldDescription', text: t.tr('MenuLinksHelpIcon') })
        ]),
        h('div', { class: 'inputContainer' }, [
            h('div', { class: 'inputLabel', text: t.tr('LabelAddress') }),
            address.element,
            h('div', { class: 'fieldDescription', text: t.tr('MenuLinksHelpAddress') })
        ])
    ]);

    // value
    // Reads the link from the fields.
    function value() {
        return { Name: name.value.trim(), Icon: icon.getValue(), Url: link.Managed ? link.Url : String(address.getValue() || '').trim(), Managed: link.Managed || null };
    }

    nectoModule.openEditorSidecar({
        title: isNew ? t.tr('MenuLinksTitleNew') : (link.Name || t.tr('MenuLinksTitleNew')),
        body: body,
        page: ctx.page,
        size: 'medium',
        isNew: isNew || !!link.Managed,
        serialize: function () { return JSON.stringify(value()); },
        saveText: t.tr('ButtonDone'),
        deleteText: t.tr('ButtonRemove'),
        onSave: function () {
            var next = value();
            var key = linkProblem(next);
            problem.textContent = key ? t.tr(key) : '';
            problem.classList.toggle('stolon-hidden', !key);
            if (key) return false;
            ctx.onDone(next);
            return true;
        },
        onDelete: function (close) {
            nectoModule.confirmDialog(t.tr('MenuLinksConfirmRemove'), {
                title: t.format('TitleRemoveName', link.Name),
                confirmText: t.tr('ButtonRemove'),
                destructive: true
            }).then(function (confirmed) {
                if (!confirmed) return;
                ctx.onRemove();
                close();
            });
        },
        onClose: function () { address.destroy(); }
    });
}

// linkRow
// Draws one link in the list: its icon, its name with a Managed badge for a link a tool keeps, its address, and an
// Edit button.
//
// Inputs
// link [object]: The link.
// t [object]: The strings.
// onEdit [function]: Opens the link.
// badgeHtml [string]: The Managed badge, as the helper bag's badge draws it. [optional]
//
// Output
// [HTMLElement]: The row.
export function linkRow(link, t, onEdit, badgeHtml) {
    var edit = h('button', { is: 'emby-button', type: 'button', class: 'stolon-row-btn', title: t.tr('MenuLinksButtonEdit'), 'aria-label': t.tr('MenuLinksButtonEdit'), 'data-action': 'edit' }, [
        h('span', { class: 'material-icons', 'aria-hidden': 'true', text: 'edit' })
    ]);
    edit.addEventListener('click', onEdit);
    var badge = h('span', { class: 'necto-menu-link-badge' });
    if (link.Managed && badgeHtml) badge.innerHTML = badgeHtml;
    return h('div', { class: 'necto-menu-link', 'data-url': link.Url }, [
        h('span', { class: 'stolon-collapsible-tile', 'aria-hidden': 'true' }, [h('span', { class: 'material-icons', text: link.Icon || 'link' })]),
        h('div', { class: 'necto-menu-link-text' }, [
            h('div', { class: 'necto-menu-link-name' }, [
                h('span', { text: link.Name }),
                link.Managed ? badge : null
            ]),
            h('div', { class: 'fieldDescription necto-menu-link-url', text: link.Url })
        ]),
        edit
    ]);
}

// buildSettings
// Fills the Menu links section of Settings: the links in order, each opened in a sidecar, with Add menu link below.
// It saves through Necto/MenuLinks, which writes config.json.
//
// Inputs
// content [HTMLElement]: The section's content.
// ctx [object]: shared is the Necto helper bag, and page the page handle.
//
// Output
// [object]: A handle with load, save, and destroy.
function buildSettings(content, ctx) {
    var shared = ctx.shared;
    var t = shared.t;
    var links = [];
    var pages = null;
    var writable = true;
    var problem = h('div', { class: 'stolon-warn stolon-hidden', role: 'status', 'data-role': 'problem' });
    var empty = h('div', { 'data-role': 'empty' });
    var add = h('button', { is: 'emby-button', type: 'button', class: 'raised stolon-button stolon-secondary', 'data-action': 'add-link' }, [
        h('span', { class: 'material-icons', 'aria-hidden': 'true', text: 'add' }),
        h('span', { text: t.tr('MenuLinksButtonAdd') })
    ]);
    var list = nectoModule.createOrderedList({
        items: [],
        render: function (link, index) {
            return linkRow(link, t, function () { open(index); }, shared.badge('blue', t.tr('MenuLinksBadgeManaged')));
        },
        label: function (link) { return link.Name; },
        onMove: function (from, to, items) {
            links = items.slice();
            changed();
        }
    });

    content.appendChild(h('div', { class: 'fieldDescription', text: t.tr('MenuLinksIntro') }));
    content.appendChild(problem);
    content.appendChild(list.element);
    content.appendChild(empty);
    content.appendChild(h('div', { class: 'stolon-button-row' }, [add]));

    // changed
    // Tells the Settings page the section has changes, for edits made in a sidecar or by a move.
    function changed() {
        content.dispatchEvent(new Event('change', { bubbles: true }));
    }

    // draw
    // Shows the links, or the empty sentence, and whether they can be saved.
    function draw() {
        list.setItems(links.slice());
        empty.innerHTML = links.length ? '' : shared.emptySection(t.tr('MenuLinksEmpty'), { inline: true });
        add.disabled = !writable;
    }

    // open
    // Opens a link in its sidecar, or a new one for index -1.
    function open(index) {
        openLinkEditor({
            shared: shared,
            page: ctx.page,
            link: index >= 0 ? links[index] : null,
            pages: pages,
            onDone: function (link) {
                if (index >= 0) links[index] = link; else links.push(link);
                draw();
                changed();
            },
            onRemove: function () {
                links.splice(index, 1);
                draw();
                changed();
            }
        });
    }

    add.addEventListener('click', function () { open(-1); });

    // show
    // Takes what the route answered.
    function show(view) {
        links = (view && Array.isArray(view.Links) ? view.Links : []).map(function (l) {
            return { Name: l.Name || '', Icon: l.Icon || '', Url: l.Url || '', Managed: l.Managed || null };
        });
        writable = !!(view && view.Writable);
        problem.textContent = (view && view.Problem) || '';
        problem.classList.toggle('stolon-hidden', !problem.textContent);
        draw();
    }

    return {
        load: function () {
            shared.request('Pages').then(function (list) { pages = list; }, function () { pages = []; });
            return shared.request(MENU_LINKS_ROUTE).then(show).catch(function (err) {
                return shared.errorText(err, t.tr('MenuLinksErrorLoad')).then(function (text) {
                    writable = false;
                    problem.textContent = text;
                    problem.classList.remove('stolon-hidden');
                    draw();
                });
            });
        },
        apply: function () {},
        save: function () {
            var body = links.map(function (l) { return { Name: l.Name, Icon: l.Icon, Url: l.Url }; });
            return shared.request(MENU_LINKS_ROUTE, 'PUT', body).then(function (view) {
                show(view);
                return view;
            }).catch(function (err) {
                return shared.errorText(err, t.tr('MenuLinksErrorSave')).then(function (text) { throw new Error(text); });
            });
        },
        destroy: function () { list.destroy(); }
    };
}

var section = { name: function () { return nectoModule.toolName('MenuLinksTitle', 'Menu links'); }, icon: 'add_link' };
nectoModule.registerSection('settings', 'menulinks', Object.assign({ build: buildSettings }, section));
