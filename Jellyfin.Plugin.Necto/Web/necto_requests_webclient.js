/*
 * The request page's strings and artwork addresses. The web client's markup the page builds with is Stolon's
 * (web-client-markup), which the page's script loads first.
 *
 * Plain ES5 with no build step, as the rest of the page, loaded before the page's own script.
 */
(function () {
    'use strict';

    // The page's strings in the viewer's language, from /Necto/Translations: the Jellyfin strings the project file
    // declares, so what the page copies from the web client reads as the web client does, and the plugin's own
    // (Localization/en-us.json) for the rest. setStrings takes { language, strings, casing }. tr takes a Jellyfin key
    // with its English, in case a language lacks it. str takes the plugin's own key, and fills {0}, {1} from the values
    // after it.
    var strings = {}, casing = [], language = '';
    function setStrings(answer) {
        strings = (answer && answer.strings) || {};
        casing = (answer && answer.casing) || [];
        language = (answer && answer.language) || '';
    }
    // A heading in Title Case, as the web client writes its home rows, by Stolon's page script (stolon-page.js) with the
    // plugin's casing list. English only, as other languages keep their own casing.
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

    // A TMDb image from the server's own copy (img/, beside the page), which it keeps after the first time.
    function artwork(u) {
        var m = /^https:\/\/image\.tmdb\.org\/t\/p\/(w\d+|h\d+|original)\/([A-Za-z0-9_-]+\.(?:jpe?g|png|webp))$/.exec(String(u || ''));
        return m ? 'img/' + m[1] + '/' + m[2] : u;
    }

    window.WebClient = { setStrings: setStrings, tr: tr, str: str, format: format, title: title, artwork: artwork };
})();
