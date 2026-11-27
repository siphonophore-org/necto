/*
 * The web client's markup, in one place: every piece of the request page that copies jellyfin-web's own HTML is built
 * here, and the page's script only calls these. The web client's stylesheets draw them, so a class written here is
 * one the web client styles (the plugin checks them against the installed web client at startup: WebClientCheck).
 *
 * Copied from jellyfin-web 12.4 (GPL-2.0-or-later), each builder naming the file it comes from. When the web client
 * changes, compare against that file and update the builder here; the page's logic doesn't need to change.
 *
 * Plain ES5 with no build step, as the rest of the page, loaded before the page's own script.
 */
(function () {
    'use strict';

    // ---------- helpers ----------

    // The page's strings in the viewer's language, from /Requests/Translations: the Jellyfin strings the project file
    // declares, so what the page copies from the web client reads as the web client does, and the plugin's own
    // (Localization/en-us.json) for the rest. setStrings takes the endpoint's answer, { language, strings, casing }.
    // tr takes a Jellyfin key with its English, in case a language lacks it; str takes the plugin's own key, and fills
    // {0}, {1} from the values after it.
    var strings = {}, casing = [], language = '';
    function setStrings(answer) {
        strings = (answer && answer.strings) || {};
        casing = (answer && answer.casing) || [];
        language = (answer && answer.language) || '';
    }
    // A heading in Title Case, as the web client writes its home rows, by Stolon's page script (stolon-page.js) with the
    // plugin's casing list. English only: other languages keep their own casing.
    function title(text) {
        return window.StolonPage ? window.StolonPage.titleCase(String(text || ''), casing, language) : String(text || '');
    }
    function format(text) {
        var args = arguments;
        return String(text).replace(/\{(\d+)\}/g, function (m, i) { return args[+i + 1] === undefined ? m : String(args[+i + 1]); });
    }
    function tr(key, english) { return strings[key] || english; }
    function str(key) {
        var args = Array.prototype.slice.call(arguments, 1);
        return format.apply(null, [strings[key] || key].concat(args));
    }

    // Escapes text for HTML: every builder escapes what it's given, so callers pass plain text.
    function esc(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }
    // A TMDb image from the server's own copy (img/, beside the page), which it keeps after the first time.
    function artwork(u) {
        var m = /^https:\/\/image\.tmdb\.org\/t\/p\/(w\d+|h\d+|original)\/([A-Za-z0-9_-]+\.(?:jpe?g|png|webp))$/.exec(String(u || ''));
        return m ? 'img/' + m[1] + '/' + m[2] : u;
    }
    // Percent-encodes anything that could end the url() early: quotes, brackets, backslashes and control characters.
    function cssUrl(u) {
        u = artwork(u);
        return 'url("' + String(u).replace(/[\x00-\x1f\x7f"'\\()]/g, function (c) { return '%' + ('0' + c.charCodeAt(0).toString(16).toUpperCase()).slice(-2); }) + '")';
    }
    function isDesktop() { return document.documentElement.classList.contains('layout-desktop'); }

    // ---------- loading ----------

    // src/components/loading/loading.ts (12.4): the inside of the loading spinner, four layers of two arcs.
    var SPINNER = [1, 2, 3, 4].map(function (n) {
        return '<div class="mdl-spinner__layer mdl-spinner__layer-' + n + '"><div class="mdl-spinner__circle-clipper mdl-spinner__left"><div class="mdl-spinner__circle mdl-spinner__circleLeft"></div></div>'
            + '<div class="mdl-spinner__circle-clipper mdl-spinner__right"><div class="mdl-spinner__circle mdl-spinner__circleRight"></div></div></div>';
    }).join('');

    // ---------- messages ----------

    // An empty state, in the web client's type and colours (secondaryText) under one of its icons (material-icons list).
    // Its layout is the page's own (emptyState): the web client has no such message for what the page shows.
    function emptyState(title, text, buttonHtml) {
        return '<div class="emptyState"><span class="material-icons list" aria-hidden="true"></span><h2>' + esc(title) + '</h2><div class="secondaryText">' + esc(text) + '</div>' + (buttonHtml || '') + '</div>';
    }
    // A line in the web client's secondary text colour (secondaryText), after any classes of the page's own.
    function secondaryText(text, cls) { return '<div class="' + (cls ? cls + ' ' : '') + 'secondaryText">' + esc(text) + '</div>'; }
    // src/apps/legacy/features/search/components/SearchResults.tsx (12.4): the no-results message's words, in the
    // viewer's language as the web client says them.
    function noResultsText(q) { return format(tr('SearchResultsEmpty', 'Sorry! No results found for "{0}"'), q); }
    // src/elements/emby-button (12.4): a raised button, as the web client's Retry buttons.
    function raisedButton(id, text, style) {
        return '<button type="button" class="emby-button raised" id="' + id + '"' + (style ? ' style="' + style + '"' : '') + '>' + esc(text) + '</button>';
    }

    // A full width raised button, as the web client's forms end with (emby-button raised block), in one of its button
    // colors such as delete.
    function blockButton(id, text, type) {
        return '<button type="button" class="raised button-' + type + ' block emby-button" id="' + id + '">' + esc(text) + '</button>';
    }

    // src/components/toast/toast.ts (12.4): the toast container, made once up front as the live region that announces
    // each toast, and a toast in it, slid in with toastVisible and faded out with toastHide before it is removed.
    // Given an action label, the toast carries one button for it (such as Undo). Returns the function that shows one.
    function toaster(parent) {
        var box = document.createElement('div'), timer = null, removeTimer = null;
        box.className = 'toastContainer';
        box.setAttribute('role', 'status');
        parent.appendChild(box);
        return function (text, actionLabel, action) {
            var t = box.querySelector('.toast');
            if (!t) {
                t = document.createElement('div');
                t.className = 'toast';
                box.appendChild(t);
                // The container slides a toast in once it is marked visible.
                requestAnimationFrame(function () { t.classList.add('toastVisible'); });
            }
            // A toast still showing (or fading out) is reused for the new message and kept from going.
            clearTimeout(timer);
            clearTimeout(removeTimer);
            t.classList.remove('toastHide');
            t.innerHTML = '<span>' + esc(text) + '</span>' + (actionLabel ? '<button type="button">' + esc(actionLabel) + '</button>' : '');
            if (actionLabel) t.querySelector('button').addEventListener('click', function () { clearTimeout(timer); clearTimeout(removeTimer); t.remove(); action(); });
            // As toast.ts: faded out with toastHide, then removed.
            timer = setTimeout(function () { t.classList.add('toastHide'); removeTimer = setTimeout(function () { t.remove(); }, 300); }, actionLabel ? 7000 : 3600);
        };
    }

    // ---------- the app bar's user menu ----------

    // src/components/toolbar/AppUserMenu.tsx (12.4): a MUI MenuItem with its ListItemIcon and text; a link to the web
    // client's page, or (no href) a button the page answers itself, marked with data-menu. id, given, hides the item
    // until the page shows it.
    function userMenuItem(iconPath, menuKey, text, href, id) {
        var inner = '<span class="MuiListItemIcon-root"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="' + iconPath + '"/></svg></span><span>' + esc(text) + '</span>';
        return href
            ? '<li role="none"' + (id ? ' id="' + id + '" class="hide"' : '') + '><a class="MuiMenuItem-root" role="menuitem" tabindex="-1" href="' + esc(href) + '">' + inner + '</a></li>'
            : '<li role="none"><button type="button" class="MuiMenuItem-root" role="menuitem" tabindex="-1" data-menu="' + menuKey + '">' + inner + '</button></li>';
    }
    // src/components/toolbar/AppUserMenu.tsx (12.4): the MUI Divider between its groups.
    function userMenuDivider() { return '<li role="separator"><hr /></li>'; }

    // ---------- the app bar's libraries ----------

    // src/apps/experimental/components/AppToolbar/userViews/UserViewNav.tsx (12.4): Favorites, then the config.json menu
    // links and the user's libraries together, as many as fit the width (8 from MUI's xl breakpoint, 1536px, 5 from lg,
    // 1200px, else 3), the rest under More. All of them stay in the bar when only one would be left over.
    function userViewsShown(count) {
        var max = window.matchMedia('(min-width: 1536px)').matches ? 8 : window.matchMedia('(min-width: 1200px)').matches ? 5 : 3;
        return count > max + 1 ? max : count;
    }

    // The Material icons the web client draws for each of these (@mui/icons-material).
    var NAV_ICONS = {
        Favorite: 'm12 21.35-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5c0 3.78-3.4 6.86-8.55 11.54z',
        Movie: 'm18 4 2 4h-3l-2-4h-2l2 4h-3l-2-4H8l2 4H7L5 4H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V4z',
        MusicNote: 'M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3z',
        Photo: 'M21 19V5c0-1.1-.9-2-2-2H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2M8.5 13.5l2.5 3.01L14.5 12l4.5 6H5z',
        LiveTv: 'M21 6h-7.59l3.29-3.29L16 2l-4 4-4-4-.71.71L10.59 6H3c-1.1 0-2 .89-2 2v12c0 1.1.9 2 2 2h18c1.1 0 2-.9 2-2V8c0-1.11-.9-2-2-2m0 14H3V8h18zM9 10v8l7-4z',
        Tv: 'M21 3H3c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h5v2h8v-2h5c1.1 0 1.99-.9 1.99-2L23 5c0-1.1-.9-2-2-2m0 14H3V5h18z',
        Theaters: 'M18 3v2h-2V3H8v2H6V3H4v18h2v-2h2v2h8v-2h2v2h2V3zM8 17H6v-2h2zm0-4H6v-2h2zm0-4H6V7h2zm10 8h-2v-2h2zm0-4h-2v-2h2zm0-4h-2V7h2z',
        MusicVideo: 'M21 3H3c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h18c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2m0 16H3V5h18zM8 15c0-1.66 1.34-3 3-3 .35 0 .69.07 1 .18V6h5v2h-3v7.03c-.02 1.64-1.35 2.97-3 2.97-1.66 0-3-1.34-3-3',
        Book: 'M18 2H6c-1.1 0-2 .9-2 2v16c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V4c0-1.1-.9-2-2-2M6 4h5v8l-2.5-1.5L6 12z',
        VideoLibrary: 'M4 6H2v14c0 1.1.9 2 2 2h14v-2H4zm16-4H8c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V4c0-1.1-.9-2-2-2m-8 12.5v-9l6 4.5z',
        Queue: 'M4 6H2v14c0 1.1.9 2 2 2h14v-2H4zm16-4H8c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V4c0-1.1-.9-2-2-2m-1 9h-4v4h-2v-4H9V9h4V5h2v4h4z',
        OndemandVideo: 'M21 3H3c-1.11 0-2 .89-2 2v12c0 1.1.89 2 2 2h5v2h8v-2h5c1.1 0 1.99-.9 1.99-2L23 5c0-1.11-.9-2-2-2m0 14H3V5h18zm-5-6-7 4V7z',
        Folder: 'M10 4H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8z',
        ArrowDropDown: 'm7 10 5 5 5-5z',
        Search: 'M15.5 14h-.79l-.28-.27C15.41 12.59 16 11.11 16 9.5 16 5.91 13.09 3 9.5 3S3 5.91 3 9.5 5.91 16 9.5 16c1.61 0 3.09-.59 4.23-1.57l.27.28v.79l5 4.99L20.49 19zm-6 0C7.01 14 5 11.99 5 9.5S7.01 5 9.5 5 14 7.01 14 9.5 11.99 14 9.5 14'
    };
    function svgIcon(name) { return '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="' + NAV_ICONS[name] + '"/></svg>'; }

    // src/apps/experimental/components/LibraryIcon.tsx (12.4): a library's icon by its collection type.
    function libraryIcon(view) {
        var icon = {
            movies: 'Movie', music: 'MusicNote', homevideos: 'Photo', photos: 'Photo', livetv: 'LiveTv', tvshows: 'Tv', trailers: 'Theaters',
            musicvideos: 'MusicVideo', books: 'Book', boxsets: 'VideoLibrary', playlists: 'Queue'
        }[view.CollectionType];
        return svgIcon(icon || (view.CollectionType == null ? 'OndemandVideo' : 'Folder'));
    }

    // src/components/router/appRouter.js getRouteUrl (12.4), for a library in the experimental layout: its own page
    // where it has one, a folder's list otherwise. A hash for the web client's address.
    function libraryRoute(view) {
        var id = encodeURIComponent(view.Id), type = view.CollectionType;
        if (type === 'livetv') return '#/livetv?collectionType=livetv';
        var page = { books: 'books', movies: 'movies', tvshows: 'tv', music: 'music', musicvideos: 'musicvideos', boxsets: 'boxsets', playlists: 'playlists' }[type];
        if (page) return '#/' + page + '?topParentId=' + id + '&collectionType=' + type;
        if (type === 'homevideos') return '#/homevideos?topParentId=' + id;
        if (type == null && view.Type === 'CollectionFolder') return '#/mixed?topParentId=' + id + '&collectionType=mixed';
        return '#/list?parentId=' + id + '&serverId=' + encodeURIComponent(view.ServerId || '');
    }

    // The bar's buttons (MUI text Buttons with a start icon): Favorites, a library, or a menu link, whose icon is a
    // Material Icons ligature. current marks the one this page is, in the primary colour as the web client marks its own.
    function navButton(href, icon, text, current, external) {
        return '<a class="appNavButton"' + (current ? ' aria-current="page"' : '') + ' href="' + esc(href) + '"'
            + (external ? ' target="_blank" rel="noopener noreferrer"' : '') + '><span class="appNavIcon">' + icon + '</span>' + esc(text) + '</a>';
    }
    function linkIcon(name) { return '<span class="material-icons" aria-hidden="true">' + esc(name || 'link') + '</span>'; }
    // More, with MUI's drop-down arrow as its end icon, opening the overflow menu.
    function navMoreButton() {
        return '<button type="button" class="appNavButton" id="navMore" aria-controls="user-view-overflow-menu" aria-haspopup="true" aria-expanded="false">' + esc(tr('ButtonMore', 'More'))
            + '<span class="appNavEndIcon">' + svgIcon('ArrowDropDown') + '</span></button>';
    }
    // UserViewsMenu: a MenuItem for each library or link left over, icon then name.
    function navMenuItem(href, icon, text, external) {
        return '<li role="none"><a class="MuiMenuItem-root" role="menuitem" tabindex="-1" href="' + esc(href) + '"' + (external ? ' target="_blank" rel="noopener noreferrer"' : '')
            + '><span class="MuiListItemIcon-root">' + icon + '</span><span>' + esc(text) + '</span></a></li>';
    }

    // src/apps/experimental/components/drawers/MainDrawerContent.tsx (12.4): a drawer item, icon then name.
    function drawerItem(href, icon, text, current, external) {
        return '<li><a class="appDrawerItem MuiListItemButton-root"' + (current ? ' aria-current="page"' : '') + ' href="' + esc(href) + '"'
            + (external ? ' target="_blank" rel="noopener noreferrer"' : '') + '><span class="appDrawerIcon MuiListItemIcon-root">' + icon + '</span>'
            + '<span class="appDrawerText MuiListItemText-root">' + esc(text) + '</span></a></li>';
    }
    // A group of drawer items (from drawerItem), after a MUI Divider unless it's the first, under a ListSubheader when
    // given one.
    function drawerList(itemsHtml, divider, subheader) {
        if (!itemsHtml) return '';
        var id = subheader ? 'drawer' + subheader.replace(/\W/g, '') : '';
        return (divider ? '<hr class="MuiDivider-root" />' : '') + '<ul class="appDrawerList MuiList-root"' + (id ? ' aria-labelledby="' + id + '"' : '') + '>'
            + (subheader ? '<li class="appDrawerSubheader" id="' + id + '">' + esc(subheader) + '</li>' : '') + itemsHtml + '</ul>';
    }

    // ---------- dialogs ----------

    // src/components/dialogHelper/dialogHelper.js createDialog and src/components/formdialog.scss (12.4): the classes a
    // form dialog (the prompt and form dialogs, scrollY off so only the content scrolls) ends up with once opened.
    var FORM_DIALOG = {
        box: 'focuscontainer dialog dialog-fixedSize dialog-small formDialog',
        header: 'formDialogHeader',
        content: 'formDialogContent smoothScrollY',
        footer: 'formDialogFooter'
    };
    // src/components/dialog/dialog.js showDialog and dialog.template.html (12.4): a centred message dialog with no
    // scrolling, its content as wide as its buttons need.
    var CONFIRM_DIALOG = {
        box: 'focuscontainer dialog formDialog align-items-center justify-content-center dialog-fullscreen-lowres',
        header: 'formDialogHeader formDialogHeader-clear justify-content-center',
        content: 'formDialogContent smoothScrollY no-grow',
        footer: 'formDialogFooter formDialogFooter-clear formDialogFooter-flex',
        // dialog.js: the content's maximum width, from two 150px buttons and their spacing, within the window.
        contentMaxWidth: function (windowWidth) { return Math.min(2 * 150 + 200, windowWidth - 50) + 'px'; },
        bodyStyle: 'padding-top:1em;padding-bottom: 1em; text-align: center;',
        footerStyle: 'margin:1em'
    };
    // The library toolbar's filter popover (apps/experimental/components/library/filter/FilterButton.tsx): MUI
    // Accordions, collapsed until opened, each a column of MUI checkboxes. Its icons are MUI's.
    var ARROW_FORWARD_ICON = '<path d="M6.23 20.23 8 22l10-10L8 2 6.23 3.77 14.46 12z"></path>';
    var CHECKBOX_BLANK_ICON = '<path d="M19 5v14H5V5h14m0-2H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2z"></path>';
    var CHECKBOX_ICON = '<path d="M19 3H5c-1.11 0-2 .9-2 2v14c0 1.1.89 2 2 2h14c1.11 0 2-.9 2-2V5c0-1.1-.89-2-2-2m-9 14-5-5 1.41-1.41L10 14.17l7.59-7.59L19 8z"></path>';
    // classes are the svg's whole class list.
    function muiIcon(path, classes) { return '<svg class="' + classes + '" focusable="false" aria-hidden="true" viewBox="0 0 24 24">' + path + '</svg>'; }
    // Reset Filters, the library toolbar's (MUI's primary text button with the FilterAltOff icon), for the foot of the
    // filter popover.
    function resetFiltersButton(id, label, disabled) {
        return '<button type="button" class="MuiButtonBase-root MuiButton-root MuiButton-text MuiButton-textPrimary MuiButton-sizeMedium rq-resetFilters" id="' + id + '"'
            + (disabled ? ' disabled' : '') + '><span class="MuiButton-startIcon"><svg class="MuiSvgIcon-root" focusable="false" aria-hidden="true" viewBox="0 0 24 24"><path d="M19.79 5.61C20.3 4.95 19.83 4 19 4H6.83l7.97 7.97zM2.81 2.81 1.39 4.22 10 13v6c0 .55.45 1 1 1h2c.55 0 1-.45 1-1v-2.17l5.78 5.78 1.41-1.41z"></path></svg></span><span>' + esc(label) + '</span></button>';
    }

    // A section: its heading a button that opens it, checkboxesHtml (filterCheckbox()'s markup) under it.
    function filterAccordion(id, title, checkboxesHtml) {
        return '<div class="MuiAccordion-root"><h3 class="MuiAccordion-heading"><button type="button" class="MuiAccordionSummary-root" aria-expanded="false" aria-controls="' + id + '-content" id="' + id + '-header">'
            + '<span class="MuiAccordionSummary-content"><p class="MuiTypography-body1">' + esc(title) + '</p></span>'
            + '<span class="MuiAccordionSummary-expandIconWrapper">' + muiIcon(ARROW_FORWARD_ICON, 'MuiSvgIcon-root') + '</span></button></h3>'
            + '<div class="MuiCollapse-root hide" id="' + id + '-content" role="region" aria-labelledby="' + id + '-header"><div class="MuiAccordionDetails-root"><div class="MuiFormGroup-root">'
            + checkboxesHtml + '</div></div></div></div>';
    }
    // A checkbox with its label. attrs are the input's own (data-*).
    function filterCheckbox(attrs, label, checked) {
        return '<label class="MuiFormControlLabel-root"><span class="MuiCheckbox-root"><input type="checkbox" class="PrivateSwitchBase-input"' + attrs + (checked ? ' checked' : '') + ' />'
            + muiIcon(CHECKBOX_BLANK_ICON, 'MuiSvgIcon-root rq-unchecked') + muiIcon(CHECKBOX_ICON, 'MuiSvgIcon-root rq-checked') + '</span>'
            + '<span class="MuiTypography-body1 MuiFormControlLabel-label">' + esc(label) + '</span></label>';
    }
    // The form dialogs' footer (e.g. components/prompt/prompt.template.html): one wide button, raised, as the dialog's submit.
    function formDialogFooter(text, type) {
        return '<button type="button" class="raised button-' + (type || 'submit') + ' block formDialogFooterItem emby-button" id="dialogSubmit"><span>' + esc(text) + '</span></button>';
    }
    // src/components/dialog/dialog.js (12.4): Cancel then the action, as autosized option buttons.
    function confirmDialogFooter(text, type) {
        return '<button type="button" class="btnOption raised formDialogFooterItem formDialogFooterItem-autosize emby-button" id="dialogCancel">' + esc(tr('ButtonCancel', 'Cancel')) + '</button>'
            + '<button type="button" class="btnOption raised formDialogFooterItem formDialogFooterItem-autosize button-' + (type || 'submit') + ' emby-button" id="dialogSubmit">' + esc(text) + '</button>';
    }
    // A form dialog's footer with Cancel beside the action (formDialogFooterItem without autosize), both the same width.
    function cancelDialogFooter(text, type) {
        return '<button type="button" class="btnOption raised formDialogFooterItem emby-button" id="dialogCancel">' + esc(tr('ButtonCancel', 'Cancel')) + '</button>'
            + '<button type="button" class="btnOption raised formDialogFooterItem button-' + (type || 'submit') + ' emby-button" id="dialogSubmit">' + esc(text) + '</button>';
    }
    // A form dialog's footer of several actions side by side, each { id, text, type }, all the same width as Cancel and
    // its action are. type is the web client's button style (submit, delete), or none for a plain raised button.
    function actionsDialogFooter(buttons) {
        return buttons.map(function (b) {
            return '<button type="button" class="btnOption raised formDialogFooterItem' + (b.type ? ' button-' + b.type : '') + ' emby-button" id="' + b.id + '">' + esc(b.text) + '</button>';
        }).join('');
    }
    // src/components/dialog/dialog.template.html (12.4): the message's text; then the page's own place for an error.
    function confirmDialogBody(text) {
        return '<div class="text">' + esc(text) + '</div><div class="formError" id="dialogError" role="alert"></div>';
    }

    // ---------- form fields ----------

    // A select's options, { value, text } each, for filling it again.
    function selectOptions(options) { return options.map(function (o) { return '<option value="' + esc(o.value) + '">' + esc(o.text) + '</option>'; }).join(''); }
    // src/elements/emby-select/emby-select.js (12.4): a select once upgraded, with its label and arrow.
    function selectField(id, label, options) {
        return '<div class="selectContainer"><label class="selectLabel" for="' + id + '">' + esc(label) + '</label>'
            + '<select id="' + id + '" class="emby-select emby-select-withcolor">' + selectOptions(options) + '</select>'
            + '<div class="selectArrowContainer"><span class="selectArrow material-icons keyboard_arrow_down" aria-hidden="true"></span></div></div>';
    }
    // src/elements/emby-textarea/emby-textarea.js (12.4): a textarea with its label above, in an inputContainer, then
    // its field descriptions.
    function textareaField(o) {
        return '<div class="inputContainer"><label class="textareaLabel textareaLabelUnfocused" for="' + o.id + '">' + esc(o.label) + '</label>'
            + '<textarea id="' + o.id + '" class="emby-textarea" rows="' + (o.rows || 3) + '" maxlength="' + o.maxLength + '"' + (o.placeholder ? ' placeholder="' + esc(o.placeholder) + '"' : '') + '></textarea>'
            + (o.descriptions || []).map(function (line) { return typeof line === 'string' ? fieldDescription(line) : fieldDescription(line.text, line.id); }).join('') + '</div>';
    }
    // A field's description line, under it (fieldDescription).
    function fieldDescription(text, id) { return '<div class="fieldDescription"' + (id ? ' id="' + id + '"' : '') + '>' + esc(text) + '</div>'; }
    // A checkbox list's heading and the box its checkboxes go in (e.g. components/libraryoptionseditor), the box
    // holding a line of text until they are known.
    function checkboxListContainer(label, boxId, text) {
        return '<div class="checkboxListContainer"><h3 class="checkboxListLabel">' + esc(label) + '</h3><div id="' + boxId + '" class="secondaryText">' + esc(text) + '</div></div>';
    }
    // One issue as a row of the web client's paper list (listview.js, 12.4), not a button, with its content wrapped
    // (listItem-withContentWrapper): what is wrong as its title, as the dashboard's lists title a row (h3), and when as the
    // second line, its state as a chip at the
    // row's end, then a secondary line and each note under who wrote it, across the row.
    // o: { title, when, chip (html), by, notes: [{ by, text }] }
    function issueRow(o) {
        var notes = (o.notes || []).map(function (n) {
            return '<div class="issueNote"><div class="secondary issueNoteBy">' + esc(n.by) + '</div><div class="issueNoteText">' + esc(n.text) + '</div></div>';
        }).join('');
        return '<div class="listItem listItem-border listItem-withContentWrapper issueRow" data-action="none">'
            + '<div class="listItem-content"><div class="listItemBody two-line"><h3 class="listItemBodyText">' + esc(o.title) + '</h3>'
            + '<div class="listItemBodyText secondary">' + esc(o.when) + '</div></div>'
            + '<div class="listItemAside issueChip">' + o.chip + '</div></div>'
            + (o.by || notes ? '<div class="issueNotes">' + (o.by ? '<div class="secondary issueBy">' + esc(o.by) + '</div>' : '') + notes + '</div>' : '') + '</div>';
    }
    // A group's heading over a form, as the web client's settings pages head theirs (mypreferencesdisplay.html, 12.4):
    // a section title in a vertical section, and its field description under it.
    function noticeBlock(title, text) {
        return '<div class="verticalSection issueNotice"><h2 class="sectionTitle">' + esc(title) + '</h2><div class="fieldDescription">' + esc(text) + '</div></div>';
    }
    // A row of the details page's info (mediaInfo), such as a status chip, with a class the page adds.
    function infoRow(html, cls) { return '<div class="mediaInfo' + (cls ? ' ' + esc(cls) : '') + '">' + html + '</div>'; }
    // Rows in the web client's paper list, with any class the page adds.
    function paperList(rows, cls) { return '<div class="paperList' + (cls ? ' ' + esc(cls) : '') + '">' + rows + '</div>'; }
    // The classes of a checkbox list in the web client's paper list (checkboxList paperList).
    var CHECKBOX_LIST = 'checkboxList paperList checkboxList-paperList';
    // src/elements/emby-checkbox/emby-checkbox.js (12.4): a checkbox's markup once it has upgraded the input. attrs are
    // the input's own (data-*); note, given, follows the label in secondary text.
    function checkbox(attrs, label, note, checked, disabled) {
        return '<label class="emby-checkbox-label"><input type="checkbox" class="emby-checkbox"' + attrs + (checked ? ' checked' : '') + (disabled ? ' disabled' : '') + ' />'
            + '<span class="checkboxLabel">' + esc(label) + (note ? ' <span class="secondaryText">· ' + esc(note) + '</span>' : '') + '</span>'
            + '<span class="checkboxOutline"><span class="material-icons checkboxIcon checkboxIcon-checked check" aria-hidden="true"></span><span class="material-icons checkboxIcon checkboxIcon-unchecked" aria-hidden="true"></span></span></label>';
    }
    // src/controllers/user/display/index.html (12.4): a settings checkbox with its description under it, as the web
    // client's own settings pages set them out (checkboxContainer-withDescription). The description is filled in later.
    function settingsCheckbox(id, label, descriptionId) {
        return '<div class="checkboxContainer checkboxContainer-withDescription">' + checkbox(' id="' + id + '"', label, null, false, true)
            + '<div class="fieldDescription checkboxFieldDescription" id="' + descriptionId + '"></div></div>';
    }
    // A text link button (button-link), as the web client's Select All and Show more. attrs are the button's own.
    function linkButton(attrs, text) { return '<button type="button" class="button-link emby-button"' + attrs + '>' + esc(text) + '</button>'; }

    // ---------- cards ----------
    // src/components/cardbuilder/cardBuilder.js (buildCard, getCardFooterText, getCardTextLines, getHoverMenuHtml,
    // getDefaultText), cardbuilder/utils/builder.ts and src/components/indicators/indicators.js (12.4), cut down to a
    // portrait card and fed this page's titles and people instead of library items.

    // Rows of cards scroll (overflowPortrait); list pages lay them out in a grid (portrait), as the web client's do.
    var ROW = 'overflowPortrait', GRID = 'portrait';

    // cardbuilder/utils/builder.ts getDefaultColorIndex: a colour for a card with no image, from the middle character of its name.
    function getDefaultColorIndex(str) {
        if (!str) return 1;
        var character = String(str.slice(Math.floor(str.length / 2)).charCodeAt(0)), sum = 0;
        for (var i = 0; i < character.length; i++) sum += parseInt(character.charAt(i), 10);
        return parseInt(String(sum).slice(-1), 10) % 5 + 1;
    }
    // cardBuilder.js getDefaultText: the item type's icon (getItemTypeIcon).
    function getDefaultText(type) {
        return '<span class="cardImageIcon material-icons ' + (type === 'Person' ? 'person' : type === 'Series' ? 'tv' : 'movie') + '" aria-hidden="true"></span>';
    }
    // cardBuilder.js buildCard's image: the poster as its background, or the coloured placeholder with the type's icon.
    // Given link (a card's), it is the card's link instead of a plain image (named by its label, title and status,
    // rather than marked as an image), with link.inner laid over the poster.
    function cardImage(url, title, type, link) {
        var tag = link ? 'a' : 'div';
        // While a poster loads, a colour for its type shows behind it, in place of the web client's blurred preview:
        // amber for a movie, blue for a show (movieCardBackground, seriesCardBackground, named as defaultCardBackground).
        // Without a poster, the web client's own colour, picked by title.
        var background = !url ? ' defaultCardBackground defaultCardBackground' + getDefaultColorIndex(title)
            : type === 'Movie' || type === 'Series' ? ' ' + type.toLowerCase() + 'CardBackground' : '';
        // The poster loads as the web client's do (lazyLoader): kept in data-src, and set once the card nears the screen.
        return '<' + tag + (link ? link.attrs : '') + ' class="cardImageContainer coveredImage' + background
            + ' cardContent' + (link ? ' itemAction' : '') + (url ? ' lazy' : '') + '"' + (link ? '' : ' role="img"') + ' aria-label="' + esc(link ? link.label : title) + '"' + (url ? ' data-src="' + esc(url) + '"' : '') + '>'
            + (link ? link.inner : '') + (url ? '' : getDefaultText(type)) + '</' + tag + '>';
    }
    // indicators.js getPlayedIndicatorHtml: the played check for what the library has, and the same round badge in a
    // colour of the page's own (requestedIndicator and the like) for the rest. s: { cls, icon, tag }; the library's keeps the theme's colour.
    function getIndicatorHtml(s) {
        if (!s) return '';
        // A status other than in the library keeps the played indicator's classes, for its shape and any custom CSS
        // aimed at it, and adds its own name in the same form (requestedIndicator, approvedIndicator...).
        return '<div class="playedIndicator indicator' + (s.cls === 'library' ? '' : ' ' + s.cls + 'Indicator') + '" title="' + esc(s.tag) + '"><span class="material-icons indicatorIcon ' + s.icon + '" aria-hidden="true"></span></div>';
    }
    // indicators.js getProgressHtml. p is 0 to 1.
    function getProgressHtml(p) {
        return '<div class="itemProgressBar"><div class="itemProgressBarForeground" style="width:' + Math.round(p * 100) + '%;"></div></div>';
    }
    // cardBuilder.js getHoverMenuHtml's buttons (cardOverlayButton), each answered by the page through data-quick.
    function overlayButton(b, extra) {
        return '<button type="button" class="cardOverlayButton itemAction paper-icon-button-light' + (extra || '') + '" data-quick="' + b.quick + '" data-key="' + b.key + '" title="' + esc(b.title) + '" aria-label="' + esc(b.label) + '">'
            + '<span class="material-icons cardOverlayButtonIcon' + (extra ? ' cardOverlayButtonIcon-hover' : '') + ' ' + b.icon + '" aria-hidden="true"></span></button>';
    }
    // cardBuilder.js buildCard. o: key (the registry entry the card opens) or href, shape (ROW unless given), type, title,
    // image, secondary, indicator, progress, fab (the hover overlay's middle button), buttons (its corner ones; on
    // phones, always shown), personCard and oneLine.
    function buildCard(o) {
        var desktop = isDesktop(), shape = o.shape || ROW;
        var href = esc(o.href || '#'), open = o.key ? ' data-card="' + o.key + '"' : '';
        var html = '<div class="card ' + shape + 'Card' + (o.personCard ? ' personCard' : '') + (desktop ? ' card-hoverable' : '') + ' card-withuserdata">'
            + '<div class="cardBox cardBox-bottompadded"><div class="cardScalable">'
            + '<div class="cardPadder cardPadder-' + shape + '">' + (o.image ? getDefaultText(o.type) : '') + '</div>'
            + cardImage(o.image, o.title, o.type, {
                attrs: ' href="' + href + '"' + open, label: o.title + (o.status ? ', ' + o.status : ''),
                inner: (o.indicator ? '<div class="cardIndicators">' + o.indicator + '</div>' : '')
                    + (o.progress != null ? '<div class="innerCardFooter fullInnerCardFooter innerCardFooterClear">' + getProgressHtml(o.progress) + '</div>' : '')
            });
        var buttons = o.buttons || [];
        if (desktop) {
            // getHoverMenuHtml: the poster dims, the main action in the middle, the rest bottom right.
            if (o.fab || buttons.length) {
                html += '<div class="cardOverlayContainer itemAction">'
                    + (o.fab ? overlayButton(o.fab, ' cardOverlayButton-hover cardOverlayFab-primary') : '')
                    + '<div class="cardOverlayButton-br flex">' + buttons.map(function (b) { return overlayButton(b, ' cardOverlayButton-hover'); }).join('') + '</div></div>';
            }
        } else if (buttons.length) {
            // Phones have no hover: the web client's overlay buttons sit in the corner of the poster.
            html += '<div class="cardOverlayButton-br flex">' + buttons.map(function (b) { return overlayButton(b); }).join('') + '</div>';
        }
        html += '</div>';
        // getCardFooterText with getCardTextLines: centred lines, the title a link, blank lines kept as &nbsp;.
        html += '<div class="cardText cardTextCentered cardText-first"><bdi><a href="' + href + '"' + open + ' class="itemAction textActionButton" title="' + esc(o.title) + '">' + esc(o.title) + '</a></bdi></div>';
        // A card with one line (oneLine) has no second; others keep a blank one so the row's cards line up.
        if (!o.oneLine) html += o.secondary ? '<div class="cardText cardTextCentered cardText-secondary"><bdi>' + esc(o.secondary) + '</bdi></div>' : '<div class="cardText cardTextCentered">&nbsp;</div>';
        return html + '</div></div>';
    }
    // A card's frame around a plain image, with no text: the details page's poster (itemDetails/index.js renderImage,
    // through cardBuilder), and the request form's. extra adds classes and attrs attributes of the page's own.
    function posterCard(shape, url, title, type, extra, attrs) {
        return '<div class="card ' + shape + 'Card' + (extra ? ' ' + extra : '') + '"' + (attrs || '') + '><div class="cardBox"><div class="cardScalable"><div class="cardPadder cardPadder-' + shape + '"></div>'
            + cardImage(url, title, type) + '</div></div></div>';
    }
    // A loading placeholder card, built from the real card's box and both lines of its text (the bars are the page's own),
    // in the shape of the cards it stands in for (ROW unless given).
    function placeholderCard(shape) {
        shape = shape || ROW;
        return '<div class="card ' + shape + 'Card skeleton rq-appear" aria-hidden="true"><div class="cardBox cardBox-bottompadded"><div class="cardScalable"><div class="cardPadder cardPadder-' + shape + '"></div></div>'
            + '<div class="cardText cardTextCentered cardText-first"><span class="skeletonBar"></span></div>'
            + '<div class="cardText cardTextCentered cardText-secondary"><span class="skeletonBar"></span></div></div></div>';
    }
    // MARK: placeholderCards
    function placeholderCards(count, shape) {
        var cards = '';
        for (var i = 0; i < count; i++) cards += placeholderCard(shape);
        return cards;
    }
    // MARK: placeholderGrid
    function placeholderGrid(count, shape) { return '<div class="itemsContainer padded-right vertical-wrap">' + placeholderCards(count, shape) + '</div>'; }
    // MARK: placeholderLines
    function placeholderLines(count) {
        var lines = '';
        for (var i = 0; i < count; i++) lines += '<span class="skeletonBar skeletonLine"></span>';
        return '<span class="rq-appear" aria-hidden="true">' + lines + '</span>';
    }

    // ---------- rows of cards ----------

    // src/elements/emby-scrollbuttons/emby-scrollbuttons.js (12.4): the arrows, top right of a row, for the row with
    // this id; hidden and disabled until the page finds the row runs past the screen.
    function scrollButtons(id) {
        var prev = esc(tr('Previous', 'Previous')), next = esc(tr('Next', 'Next'));
        return '<div class="emby-scrollbuttons padded-right" data-buttons="' + id + '"><button type="button" class="emby-scrollbuttons-button paper-icon-button-light hide" data-scroll="' + id + '" data-dir="-1" title="' + prev + '" aria-label="' + prev + '" disabled><span class="material-icons chevron_left" aria-hidden="true"></span></button>'
            + '<button type="button" class="emby-scrollbuttons-button paper-icon-button-light hide" data-scroll="' + id + '" data-dir="1" title="' + next + '" aria-label="' + next + '" disabled><span class="material-icons chevron_right" aria-hidden="true"></span></button></div>';
    }
    // A row of cards on a page of rows. o: id, title, inner (the cards), scroll (a scrolling row, else a wrapping grid),
    // and listHref and listKey for a heading that opens the row's list page.
    //  - With a list page: the home page's rows (src/components/homesections/homesections.js, the heading a link with
    //    its chevron: sectionTitleContainer-cards and sectionTitleTextButton).
    //  - Otherwise the search page's (src/apps/legacy/features/search/components/SearchResultsRow.tsx and
    //    SectionContainer.tsx), a plain heading.
    // Both run full width, padded in from both edges. The web client's rows scroll by transform; these scroll natively
    // (scrollX hiddenScrollX), with the same padding. The arrows are offered only to a mouse, as the web client's.
    function section(o) {
        var heading;
        if (o.listHref != null) {
            heading = '<div class="sectionTitleContainer sectionTitleContainer-cards padded-left"><a href="' + esc(o.listHref) + '" class="more button-flat button-flat-mini sectionTitleTextButton emby-button" data-list="' + o.listKey + '">'
                + '<h2 class="sectionTitle sectionTitle-cards">' + esc(o.title) + '</h2><span class="material-icons chevron_right" aria-hidden="true"></span></a></div>';
        } else {
            heading = '<h2 class="sectionTitle sectionTitle-cards focuscontainer-x padded-left padded-right">' + esc(o.title) + '</h2>';
        }
        var arrows = o.scroll && isDesktop() ? scrollButtons(o.id) : '';
        return '<div class="verticalSection' + (o.scroll ? ' emby-scroller-container' : '') + '">' + heading + arrows + (o.scroll
            ? '<div class="padded-top-focusscale padded-bottom-focusscale emby-scroller scrollX hiddenScrollX" id="' + o.id + '"><div class="itemsContainer scrollSlider focuscontainer-x">' + o.inner + '</div></div>'
            : '<div class="itemsContainer vertical-wrap" id="' + o.id + '">' + o.inner + '</div>') + '</div>';
    }
    // src/apps/legacy/features/search/components/SearchSuggestions.tsx (12.4): the empty search's suggestions, centred
    // text links (LinkButton) under the Suggestions heading, each to a search for its item. items: { href, text }.
    function searchSuggestions(items) {
        return '<div class="verticalSection searchSuggestions" style="text-align: center;"><div><h2 class="sectionTitle padded-left padded-right">' + esc(tr('Suggestions', 'Suggestions')) + '</h2></div>'
            + '<div class="searchSuggestionsList padded-left padded-right">' + items.map(function (i) {
                return '<div><a class="emby-button button-link" href="' + esc(i.href) + '" style="display: inline-block; padding: 0.5em 1em;">' + esc(i.text) + '</a></div>';
            }).join('') + '</div></div>';
    }
    // A row's loading placeholder: the search page's row with its heading and count cards.
    function placeholderRow(count) {
        var cards = placeholderCards(count);
        return '<div class="verticalSection emby-scroller-container rq-appear" aria-hidden="true">'
            + '<h2 class="sectionTitle sectionTitle-cards focuscontainer-x padded-left padded-right"><span class="skeletonBar"></span></h2>'
            + '<div class="padded-top-focusscale padded-bottom-focusscale emby-scroller scrollX hiddenScrollX"><div class="itemsContainer scrollSlider">' + cards + '</div></div></div>';
    }
    // src/scripts/itembynamedetailpage.js (12.4) renderSections: one of a person's kinds of titles beside the photo, a
    // heading and a wrapping grid, in the details page's #childrenContent.
    function detailGrid(title, inner) {
        return '<div class="verticalSection"><div class="sectionTitleContainer sectionTitleContainer-cards"><h2 class="sectionTitle sectionTitle-cards">' + esc(title) + '</h2></div>'
            + '<div class="itemsContainer padded-right vertical-wrap">' + inner + '</div></div>';
    }
    // itemDetails/index.js renderChildren (12.4): a show's seasons in #childrenContent, a wrapping grid under the
    // section's own Seasons heading.
    function childrenGrid(inner) {
        return '<div class="itemsContainer padded-right vertical-wrap" style="text-align:left">' + inner + '</div>';
    }
    // src/controllers/itemDetails/index.html (12.4)'s Cast & Crew and More Like This sections, filled as the web client
    // fills them (itemDetails/index.js renderCast, renderSimilarItems): the heading, the scroll arrows its emby-scroller
    // adds, the cards, then shown. The scroller is given id, which the arrows scroll. Empty inner hides it again.
    function fillDetailSection(section, id, title, inner) {
        var heading = section.querySelector('.sectionTitle'), items = section.querySelector('.itemsContainer'), scroller = items.parentNode;
        var old = section.querySelector('[data-buttons]');
        if (old) old.remove();
        items.innerHTML = inner;
        if (!inner) { section.classList.add('hide'); return; }
        heading.textContent = title;
        section.classList.add('emby-scroller-container');
        scroller.classList.add('emby-scroller', 'scrollX', 'hiddenScrollX');
        scroller.id = id;
        if (isDesktop()) heading.insertAdjacentHTML('afterend', scrollButtons(id));
        section.classList.remove('hide');
    }

    // ---------- the details page skeleton ----------

    // src/controllers/itemDetails/index.js renderName: the name's heading, then the original title's, hidden until there
    // is one; the page fills them by id.
    var NAME_HTML = '<h1 class="itemName infoText parentNameLast" id="detailName" tabindex="-1"></h1><h4 class="itemName infoText originalTitle hide" id="detailOriginal"></h4>';

    // The web client's details page (src/controllers/itemDetails/index.html, 12.4) as the plugin read it from the
    // installed web client, put in place of view's own copy of it: the page's ids given to the parts it fills, the parts
    // it doesn't use hidden as the web client hides them, its own buttons taken out. False, leaving view as it was, when
    // there's no template or it lacks a part the page needs; missing, given, collects what was lacking.
    function adoptDetailsTemplate(view, template, missing) {
        var page = template && template.content && template.content.querySelector('#itemDetailPage');
        if (!page) return false;
        var root = page.cloneNode(true);
        var parts = {
            itemBackdrop: '.itemBackdrop', detailLogo: '.detailLogo', detailImage: '.detailImageContainer.hide-mobile',
            detailImageMobile: '.detailImageContainer.hide-desktop', nameContainer: '.nameContainer', detailMisc: '.itemMiscInfo-primary',
            detailButtons: '.mainDetailButtons', requestRows: '.trackSelections', detailTagline: '.detailSectionContent .tagline',
            detailOverview: '.detailSectionContent .overview', overviewControls: '.overview-controls', overviewExpand: '.overview-controls .overview-expand',
            detailBorn: '#itemBirthday', detailBirthPlace: '#itemBirthLocation', detailDied: '#itemDeathDate', detailLinks: '.itemExternalLinks',
            detailGroup: '.itemDetailsGroup', detailSections: '.detailPageSecondaryContainer', listChildrenCollapsible: '#listChildrenCollapsible',
            childrenContent: '#listChildrenCollapsible #childrenContent', castCollapsible: '#castCollapsible', similarCollapsible: '#similarCollapsible',
            castHeading: '#castCollapsible .sectionTitle', castItems: '#castCollapsible .itemsContainer',
            similarHeading: '#similarCollapsible .sectionTitle', similarItems: '#similarCollapsible .itemsContainer'
        };
        var found = {}, lacking = [];
        Object.keys(parts).forEach(function (k) { found[k] = root.querySelector(parts[k]); if (!found[k]) lacking.push(parts[k]); });
        if (lacking.length) { if (missing) missing.push.apply(missing, lacking); return false; }
        ['itemBackdrop', 'detailLogo', 'detailImage', 'detailImageMobile', 'detailMisc', 'detailButtons', 'requestRows', 'detailTagline', 'detailOverview',
            'overviewControls', 'overviewExpand', 'detailBorn', 'detailBirthPlace', 'detailDied', 'detailLinks', 'detailGroup', 'detailSections'].forEach(function (k) { found[k].id = k; });
        // What the page shows only once it has something for it starts hidden, as the web client's does.
        ['detailLogo', 'detailMisc', 'requestRows', 'detailTagline', 'overviewControls', 'detailBorn', 'detailBirthPlace', 'detailDied', 'detailLinks'].forEach(function (k) { found[k].classList.add('hide'); });
        Array.prototype.forEach.call(root.querySelectorAll('.itemMiscInfo-secondary, #seriesAirTime'), function (n) { n.classList.add('hide'); });
        found.detailLogo.setAttribute('role', 'img');
        found.nameContainer.innerHTML = NAME_HTML;
        // The web client's own buttons (play, resume...) are for its library; the page puts its own in.
        found.detailButtons.innerHTML = '';
        found.detailOverview.classList.add('detail-clamp-text');
        // Show more, upgraded as the web client's link button is; the page shows and hides its row.
        found.overviewExpand.classList.remove('hide');
        found.overviewExpand.classList.add('button-link', 'emby-button');
        found.overviewExpand.textContent = tr('ShowMore', 'Show more');
        while (view.firstChild) view.removeChild(view.firstChild);
        while (root.firstChild) view.appendChild(root.firstChild);
        return true;
    }

    // ---------- the details page ----------

    // itemDetails/index.js renderName: the name, kept in its own direction.
    function bdi(text) { return text ? '<bdi>' + esc(text) + '</bdi>' : ''; }

    // src/components/mediainfo/mediainfo.js (12.4) getMediaInfoItem: one item of the info row, its own class then
    // mediaInfoItem. html is markup the caller has escaped.
    function mediaInfoItem(html, cls) { return '<div class="' + (cls ? cls + ' mediaInfoItem' : 'mediaInfoItem') + '">' + html + '</div>'; }
    // mediainfo.js: the age rating.
    function officialRating(text) { return mediaInfoItem(esc(text), 'mediaInfoText mediaInfoOfficialRating'); }
    // mediainfo.js: the community rating, a star and the score to one place.
    function starRating(score) { return mediaInfoItem('<span class="material-icons starIcon star" aria-hidden="true"></span>' + Number(score).toFixed(1), 'starRatingContainer'); }
    // mediainfo.js: the critic rating, fresh from 60.
    function criticRating(value) { return mediaInfoItem(esc(String(value)), 'mediaInfoCriticRating mediaInfoCriticRating' + (value >= 60 ? 'Fresh' : 'Rotten')); }
    // A tag in the info row's text style (mediaInfoText) for where a title stands; its colour class (t-*) is the page's own.
    function statusTag(cls, text) { return mediaInfoItem(esc(text), 'mediaInfoText statusTag t-' + cls); }

    // src/controllers/itemDetails/index.html (12.4): the flat detail buttons, an icon each.
    function detailIcon(icon) { return '<div class="detailButton-content"><span class="material-icons detailButton-icon ' + icon + '" aria-hidden="true"></span></div>'; }
    function detailButton(id, icon, title) {
        return '<button type="button" class="button-flat detailButton emby-button"' + (id ? ' id="' + id + '"' : '') + ' title="' + esc(title) + '" aria-label="' + esc(title) + '">' + detailIcon(icon) + '</button>';
    }
    // Where a title stands when there's no request to open, such as one already on its way: a detail button, disabled
    // and greyed as the web client greys a disabled icon button, since there is nothing to select. Its words are its
    // tooltip and what a screen reader reads.
    function detailStatus(icon, title) {
        return '<button type="button" class="button-flat detailButton emby-button rq-detailStatus" disabled title="' + esc(title) + '" aria-label="' + esc(title) + '">' + detailIcon(icon) + '</button>';
    }
    function detailLinkButton(href, icon, title, external) {
        return '<a class="button-flat detailButton emby-button" href="' + esc(href) + '"' + (external ? ' target="_blank" rel="noopener noreferrer"' : '') + ' title="' + esc(title) + '" aria-label="' + esc(title) + '">' + detailIcon(icon) + '</a>';
    }
    // src/elements/emby-ratingbutton/emby-ratingbutton.js (12.4): the favourite button, the heart red once a favourite.
    function favoriteButton(id, isFavorite) {
        var title = isFavorite ? tr('Favorite', 'Favorite') : tr('AddToFavorites', 'Add to favorites');
        return '<button type="button" class="button-flat btnUserRating detailButton emby-button' + (isFavorite ? ' ratingbutton-withrating' : '') + '" id="' + id + '"'
            + ' title="' + title + '" aria-label="' + title + '" aria-pressed="' + isFavorite + '" data-isfavorite="' + isFavorite + '">'
            + '<div class="detailButton-content"><span class="material-icons detailButton-icon favorite' + (isFavorite ? ' ratingbutton-icon-withrating' : '') + '" aria-hidden="true"></span></div></button>';
    }

    // src/components/itemMediaInfo/itemMediaInfo.js (12.4): the Media Info dialog's content, centred (mediaInfoContent):
    // a source with its name and attributes, then its streams (Video, Audio...), each a heading and its attributes,
    // inside it. mediaInfoAttribute is its createAttribute: a bold label, then the value (escaped markup), a line each.
    function mediaInfoContent(sectionsHtml) { return '<div class="mediaInfoContent">' + sectionsHtml + '</div>'; }
    function mediaInfoSource(title, innerHtml) {
        return '<div class="mediaInfoSource"><div><h2 class="mediaInfoStreamType">' + esc(title) + '</h2></div>\n' + innerHtml + '</div>';
    }
    function mediaInfoStream(title, attributesHtml) {
        return '<div class="mediaInfoStream">\n<h2 class="mediaInfoStreamType">' + esc(title) + '</h2>\n' + attributesHtml + '</div>';
    }
    function mediaInfoAttribute(label, valueHtml) {
        return '<span class="mediaInfoLabel">' + esc(label) + '</span><span class="hide">: </span><span class="mediaInfoAttribute">' + valueHtml + '</span>\n<br/>';
    }
    // src/components/itemMetadata/ItemDetailsMetadataList (12.4) in the details group: a label, then the values; links
    // (raw, escaped markup) in a focus container, plain text in its content, at its weight.
    function detailsGroupItem(label, value, raw) {
        return '<div class="detailsGroupItem"><div class="label">' + esc(label) + '</div>' + (raw ? '<div class="focuscontainer-x">' + value : '<div class="content">' + esc(value)) + '</div></div>';
    }
    // itemDetails/index.js renderDirector/renderWriter: each person a link (opened by the page through data-card),
    // the names separated by commas.
    function personLinks(people) {
        return people.map(function (p) {
            return '<span><a href="' + esc(p.href) + '" data-card="' + p.key + '" class="button-link emby-button" style="color:inherit">' + esc(p.name) + '</a></span>';
        }).join('<span>, </span>');
    }
    // The same line of links, each opening a list of titles (a genre's, a studio's) by its data-list key.
    function listLinks(links) {
        return links.map(function (l) {
            return '<span><a href="' + esc(l.href) + '" data-list="' + l.key + '" class="button-link emby-button" style="color:inherit">' + esc(l.name) + '</a></span>';
        }).join('<span>, </span>');
    }
    // itemDetails/index.js renderLinks: the external links line, comma separated. links: [[text, href]].
    function externalLinks(links) {
        return links.map(function (l) { return '<a class="button-link emby-button" href="' + esc(l[1]) + '" target="_blank" rel="noopener noreferrer">' + esc(l[0]) + '</a>'; }).join(', ');
    }

    // A box of limits in the form's paper list, as the Seasons box is, each a line saying what it is and what's left
    // over a determinate MUI LinearProgress of how much is used, coloured as the dashboard's storage bars are
    // (success, warning from 80%, error from 90%). rows: [{ label, amount, percent }].
    function limitsBox(label, rows) {
        return '<div class="checkboxListContainer"><h3 class="checkboxListLabel">' + esc(label) + '</h3><div class="' + CHECKBOX_LIST + ' rq-limits">'
            + rows.map(function (r) {
                var color = r.percent >= 90 ? 'Error' : r.percent >= 80 ? 'Warning' : 'Success';
                return '<div class="rq-limit"><div class="rq-limitHead"><span>' + esc(r.label) + '</span><span class="secondaryText">' + esc(r.amount) + '</span></div>'
                    + '<span class="MuiLinearProgress-root MuiLinearProgress-determinate MuiLinearProgress-color' + color + '" role="progressbar" aria-label="' + esc(r.label)
                    + '" aria-valuemin="0" aria-valuemax="100" aria-valuenow="' + Math.round(r.percent) + '">'
                    + '<span class="MuiLinearProgress-bar" style="transform:translateX(' + (r.percent - 100) + '%)"></span></span></div>';
            }).join('') + '</div></div>';
    }

    window.WebClient = {
        esc: esc, cssUrl: cssUrl, artwork: artwork,
        setStrings: setStrings, tr: tr, str: str, format: format, title: title,
        SPINNER: SPINNER, emptyState: emptyState, secondaryText: secondaryText, noResultsText: noResultsText, raisedButton: raisedButton, blockButton: blockButton, toaster: toaster,
        userMenuItem: userMenuItem, userMenuDivider: userMenuDivider,
        userViewsShown: userViewsShown, svgIcon: svgIcon, libraryIcon: libraryIcon, libraryRoute: libraryRoute, navButton: navButton, linkIcon: linkIcon,
        navMoreButton: navMoreButton, navMenuItem: navMenuItem, drawerItem: drawerItem, drawerList: drawerList,
        FORM_DIALOG: FORM_DIALOG, CONFIRM_DIALOG: CONFIRM_DIALOG, filterAccordion: filterAccordion, filterCheckbox: filterCheckbox, resetFiltersButton: resetFiltersButton, formDialogFooter: formDialogFooter, confirmDialogFooter: confirmDialogFooter, cancelDialogFooter: cancelDialogFooter, actionsDialogFooter: actionsDialogFooter, confirmDialogBody: confirmDialogBody,
        selectField: selectField, selectOptions: selectOptions, textareaField: textareaField, fieldDescription: fieldDescription, checkboxListContainer: checkboxListContainer, CHECKBOX_LIST: CHECKBOX_LIST,
        checkbox: checkbox, settingsCheckbox: settingsCheckbox, linkButton: linkButton, limitsBox: limitsBox,
        ROW: ROW, GRID: GRID, getIndicatorHtml: getIndicatorHtml, buildCard: buildCard,
        posterCard: posterCard, placeholderRow: placeholderRow, placeholderCards: placeholderCards, placeholderGrid: placeholderGrid, placeholderLines: placeholderLines,
        section: section, searchSuggestions: searchSuggestions, paperList: paperList, issueRow: issueRow, noticeBlock: noticeBlock, infoRow: infoRow, detailGrid: detailGrid, childrenGrid: childrenGrid, fillDetailSection: fillDetailSection, adoptDetailsTemplate: adoptDetailsTemplate,
        bdi: bdi, mediaInfoItem: mediaInfoItem, officialRating: officialRating, starRating: starRating, criticRating: criticRating, statusTag: statusTag,
        detailButton: detailButton, detailStatus: detailStatus, detailLinkButton: detailLinkButton, favoriteButton: favoriteButton,
        mediaInfoContent: mediaInfoContent, mediaInfoSource: mediaInfoSource, mediaInfoStream: mediaInfoStream, mediaInfoAttribute: mediaInfoAttribute, detailsGroupItem: detailsGroupItem, personLinks: personLinks, listLinks: listLinks, externalLinks: externalLinks
    };
})();
