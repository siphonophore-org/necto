// necto_requests_sections.test.mjs
// Tests for the Requests and Issues sections of Settings and Troubleshooting: the server checks before a save, the
// settings loaded and saved back, the blocklist's Unblock, the server editor's test, and the repairs.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { installPage, loadScript, pageView, settle, texts, clickButton, statusText, strings, paths } from './support.mjs';

globalThis.setTimeout = function () { return 1; };
globalThis.clearTimeout = function () {};
globalThis.setInterval = function () { return 1; };
globalThis.clearInterval = function () {};

// t
// Builds the strings for a test that calls the module's helpers directly.
async function t() {
    var page = installPage({});
    var shared = await loadScript('necto_shared.js');
    return shared.createNectoShared(page.dom.el('div', {})).translations;
}

// radarr
// A complete Radarr server, as stored, its key protected.
function radarr(extra) {
    return Object.assign({ Id: 's1', Type: 'Movie', Name: '', Url: 'http://radarr.local:7878', ExternalUrl: '', ApiKey: 'protected-key', IsDefault: true, SearchOnAdd: true,
        Profiles: [{ Id: 'p1', Name: '1080p', QualityProfileId: 4, RootFolder: '/movies', Tags: [], MinimumAvailability: 'Released' }] }, extra || {});
}

// config
// A plugin configuration with the Requests and Issues settings.
function config() {
    return {
        Other: 'kept',
        Requests: {
            RetentionDays: 30, RefreshOnAvailable: 'FullLibrary', AutoApproveRatings: ['G'], AutoDeclineAfterDays: 14, AutoDeclineReason: 'Too slow.', AgingAfterDays: 3,
            BlockedTitles: [{ Type: 'Movie', Title: 'Cats', Year: 2019, Tmdb: '536869' }], BlockedGenres: ['Horror'], BlockedRatings: ['R'], BlockedKeywords: ['gore'],
            BlockedTags: [{ Id: 210024, Name: 'anime' }], BlockedLanguages: ['ja'], BlockedCollections: [{ Id: 726871, Name: 'Dune Collection' }],
            ArrServers: [radarr()], TmdbApiKey: 'protected-tmdb', HomeRows: [{ Id: 'row1', Title: 'Anime', Type: 'Series', Kind: 'Keyword', Ids: '210024', Names: 'anime', Sort: '' }],
            HomeRowOrder: ['Custom-row1'], HiddenHomeRows: ['PopularMovies'], HideAvailableInRows: true, FullSeriesOnly: true, AllowSpecials: false, MovieQuotaDays: 14, SeriesQuotaDays: 30
        },
        Issues: { Enabled: true, RetentionDays: 60, LimitDays: 2 }
    };
}

// openTool
// Shows Settings or Troubleshooting with the configuration, answering each route by its path whatever its query.
async function openTool(name, cfg, answers) {
    var page = installPage({ config: cfg || {} });
    var dom = page.dom;
    var container = name === 'settings' ? 'settingsSections' : 'troubleshootingSections';
    var view = pageView(dom, [dom.el('div', { id: 'settingsStatus' }), dom.el('div', { id: container })]);
    dom.document.documentElement.appendChild(view);
    var saved = [];
    globalThis.ApiClient.getPluginConfiguration = function () { return Promise.resolve(JSON.parse(JSON.stringify(cfg || {}))); };
    globalThis.ApiClient.updatePluginConfiguration = function (id, c) { saved.push(JSON.parse(JSON.stringify(c))); return Promise.resolve(); };
    globalThis.ApiClient.serverAddress = function () { return 'https://jellyfin.example.com'; };
    var routes = Object.assign({ 'GET Groups/ResetCodes': { Enabled: false } }, answers || {});
    globalThis.ApiClient.fetch = function (options) {
        page.requests.push(options);
        var answer = routes[(options.type || 'GET') + ' ' + String(options.url).split('?')[0]];
        if (typeof answer === 'function') answer = answer(options);
        if (answer instanceof Error) return Promise.reject(answer);
        return Promise.resolve(answer === undefined ? {} : answer);
    };
    var mod = await loadScript('necto_' + name + '.js');
    mod.default(view);
    view.dispatchEvent(new globalThis.CustomEvent('viewshow'));
    await settle();
    return { page: page, view: view, dom: dom, saved: saved, section: function (key) { return view.querySelector('[data-tool="' + key + '"]'); } };
}

test('arrServersError refuses each server the server would refuse, and passes a complete one', async function () {
    var strings = await t();
    var mod = await loadScript('necto_requests_sections.js');
    var shared = await loadScript('necto_shared.js');
    var stored = [radarr()];
    var kept = function (extra) { return Object.assign(radarr({ ApiKey: shared.SECRET_KEPT }), extra || {}); };

    assert.equal(mod.arrServersError([kept()], stored, strings), null);
    assert.equal(mod.arrServersError([], stored, strings), null);
    assert.equal(mod.arrServersError([kept({ Url: 'radarr.local' })], stored, strings), 'Radarr\'s address must start with http:// or https://.');
    assert.equal(mod.arrServersError([kept({ Name: '4K', ExternalUrl: 'ftp://x' })], stored, strings), '4K\'s external URL must start with http:// or https://.');
    assert.equal(mod.arrServersError([kept({ ApiKey: '' })], stored, strings), 'Enter Radarr\'s API key.');
    assert.equal(mod.arrServersError([kept({ Url: 'http://elsewhere:7878' })], stored, strings), 'Enter Radarr\'s API key again for its new address.');
    assert.equal(mod.arrServersError([kept({ Url: 'HTTP://RADARR.LOCAL:7878/' })], stored, strings), null);
    assert.equal(mod.arrServersError([kept({ ApiKey: 'typed', Url: 'http://elsewhere:7878' })], stored, strings), null);
    assert.equal(mod.arrServersError([kept({ Profiles: [] })], stored, strings), 'Add a profile to Radarr, or remove it.');
    assert.equal(mod.arrServersError([kept({ Profiles: [{ Name: '4K', QualityProfileId: 0, RootFolder: '/m' }] })], stored, strings), 'Choose a quality profile and root folder for 4K on Radarr.');
    assert.equal(mod.arrServersError([kept({ Profiles: [{ Name: '', QualityProfileId: 3, RootFolder: '' }] })], stored, strings), 'Choose a quality profile and root folder for each profile on Radarr.');
});

test('keptServers hides each stored key behind the kept sentinel and names each choice', async function () {
    var mod = await loadScript('necto_requests_sections.js');
    var shared = await loadScript('necto_shared.js');

    var servers = mod.keptServers([radarr({ Type: 0 }), radarr({ Id: 's2', Type: 1, ApiKey: '', Profiles: [{ SeriesType: 2 }] })]);

    assert.equal(servers[0].ApiKey, shared.SECRET_KEPT);
    assert.equal(servers[0].Type, 'Movie');
    assert.equal(servers[1].ApiKey, '');
    assert.equal(servers[1].Type, 'Series');
    assert.equal(servers[1].Profiles[0].SeriesType, 'Anime');
});

test('rowKeys puts the saved order first, then the rest as the request page has them', async function () {
    var mod = await loadScript('necto_requests_sections.js');

    var keys = mod.rowKeys([{ Id: 'a' }], ['Custom-a', 'PopularMovies', 'Gone']);

    assert.deepEqual(keys.slice(0, 3), ['Custom-a', 'PopularMovies', 'RecentlyAdded']);
    assert.equal(keys.length, 9);
    assert.equal(keys.indexOf('Gone'), -1);
});

test('a blank or too small number falls back to its default', async function () {
    var mod = await loadScript('necto_requests_sections.js');

    assert.equal(mod.intOf({ value: '' }, 7, 1), 7);
    assert.equal(mod.intOf({ value: '0' }, 7, 1), 7);
    assert.equal(mod.intOf({ value: '0' }, 90), 0);
    assert.equal(mod.intOf({ value: '12' }, 7, 1), 12);
    assert.equal(mod.intOf({ value: 'x' }, 90), 90);
    assert.equal(mod.isWebAddress('https://radarr.example.com'), true);
    assert.equal(mod.isWebAddress('radarr.example.com'), false);
});

test('the Requests section of Settings fills every setting from the config', async function () {
    var opened = await openTool('settings', config());
    var section = opened.section('requests');

    assert.deepEqual(texts(section, '.stolon-subsection-title'), ['Rules', 'Quota windows', 'Blocklist', 'Blocked titles', 'Radarr and Sonarr', 'Discovery', 'Home rows', 'Imports']);
    assert.equal(section.querySelector('[data-field="AgingAfterDays"]').value, '3');
    assert.equal(section.querySelector('[data-field="AutoDeclineAfterDays"]').value, '14');
    assert.equal(section.querySelector('[data-field="AutoDeclineReason"]').value, 'Too slow.');
    assert.equal(section.querySelector('[data-field="FullSeriesOnly"]').checked, true);
    assert.equal(section.querySelector('[data-field="MovieQuotaDays"]').value, '14');
    assert.equal(section.querySelector('[data-field="SeriesQuotaDays"]').value, '30');
    assert.equal(section.querySelector('[data-field="RefreshOnAvailable"]').value, 'FullLibrary');
    assert.equal(section.querySelector('[data-field="RetentionDays"]').value, '30');
    assert.equal(section.querySelector('[data-field="HideAvailableInRows"]').checked, true);
    assert.match(section.querySelector('[data-role="blocked-titles"]').textContent, /Cats \(2019\)/);
    assert.match(section.querySelector('[data-role="servers"]').textContent, /Radarr/);
    assert.match(section.textContent, /https:\/\/jellyfin\.example\.com\/requests\//);
});

test('Save writes the Requests settings back, with stored keys kept and the blocked titles left to their route', async function () {
    var cfg = config();
    var opened = await openTool('settings', cfg);
    var section = opened.section('requests');
    section.querySelector('[data-field="MovieQuotaDays"]').value = '';
    section.querySelector('[data-field="SeriesQuotaDays"]').value = '21';
    section.querySelector('[data-field="AllowSpecials"]').checked = true;
    section.querySelector('[data-field="RefreshOnAvailable"]').value = 'Disabled';

    assert.equal(clickButton(section, 'Save'), true);
    await settle(50);

    var shared = await loadScript('necto_shared.js');
    var saved = opened.saved[0];
    var r = saved.Requests;
    assert.equal(saved.Other, 'kept');
    assert.equal(r.MovieQuotaDays, 7);
    assert.equal(r.SeriesQuotaDays, 21);
    assert.equal(r.AllowSpecials, true);
    assert.equal(r.RefreshOnAvailable, 'Disabled');
    assert.equal(r.ArrServers[0].ApiKey, shared.SECRET_KEPT);
    assert.equal(r.TmdbApiKey, shared.SECRET_KEPT);
    assert.deepEqual(r.BlockedTitles, cfg.Requests.BlockedTitles);
    assert.deepEqual(r.AutoApproveRatings, ['G']);
    assert.deepEqual(r.BlockedGenres, ['Horror']);
    assert.deepEqual(r.BlockedTags, [{ Id: 210024, Name: 'anime' }]);
    assert.deepEqual(r.BlockedLanguages, ['ja']);
    assert.deepEqual(r.BlockedCollections, [{ Id: 726871, Name: 'Dune Collection' }]);
    assert.deepEqual(r.HomeRowOrder.slice(0, 2), ['Custom-row1', 'RecentlyAdded']);
    assert.deepEqual(r.HiddenHomeRows, ['PopularMovies']);
    assert.equal(r.HomeRows[0].Title, 'Anime');
    assert.equal(r.AgingAfterDays, 3);
    assert.equal(r.RetentionDays, 30);
});

test('Save refuses a server with no profile before anything is sent, and says why', async function () {
    var cfg = config();
    cfg.Requests.ArrServers = [radarr({ Profiles: [] })];
    var opened = await openTool('settings', cfg);
    var section = opened.section('requests');

    clickButton(section, 'Save');
    await settle(50);

    assert.equal(opened.saved.length, 0);
    assert.match(section.textContent, /Add a profile to Radarr, or remove it\./);
});

test('Unblock allows a title again at once through the blocklist route', async function () {
    var opened = await openTool('settings', config(), { 'POST Requests/Blocklist/Unblock': {} });
    var section = opened.section('requests');

    section.querySelector('[data-unblock="0"]').click();
    await settle(50);

    var post = opened.page.requests.find(function (r) { return r.url === 'Requests/Blocklist/Unblock'; });
    assert.deepEqual(JSON.parse(post.data), { Type: 'Movie', Title: 'Cats', Year: 2019, Tmdb: '536869' });
    assert.doesNotMatch(section.querySelector('[data-role="blocked-titles"]').textContent, /Cats \(2019\)/);
    assert.equal(statusText(section.querySelector('[data-role="blocked-status"]')), 'Cats can be requested again.');
});

test('Add Radarr opens a server editor whose test loads the server\'s profiles and folders', async function () {
    var opened = await openTool('settings', config(), {
        'POST Requests/Arr/Movie/Test': { Ok: true, Version: '5.0', Profiles: [{ Id: 4, Name: 'HD-1080p' }], RootFolders: ['/movies'], Tags: [{ Id: 1, Label: 'kids' }] }
    });
    var section = opened.section('requests');
    section.querySelector('[data-add-server="Movie"]').click();
    await settle(50);
    var sidecar = opened.dom.document.querySelector('.stolon-sidecar');
    assert.equal(sidecar.querySelector('.stolon-sidecar-title').textContent, 'Add Radarr');
    assert.deepEqual(texts(sidecar.querySelector('.stolon-sidecar-shelf'), 'button'), ['Cancel', 'Done']);

    sidecar.querySelector('[data-field="Url"]').value = 'http://radarr.local:7878';
    sidecar.querySelector('[data-field="ApiKey"]').value = 'abc';
    clickButton(sidecar, 'Test and load options');
    await settle(50);

    var post = opened.page.requests.find(function (r) { return r.url === 'Requests/Arr/Movie/Test'; });
    assert.equal(JSON.parse(post.data).ApiKey, 'abc');
    assert.equal(statusText(sidecar.querySelector('[data-role="test-status"]')), 'Connected to Radarr 5.0.');
    assert.match(sidecar.querySelector('[data-role="profiles"]').textContent, /HD-1080p/);
});

test('Done refuses a server with no address and keeps the editor open', async function () {
    var opened = await openTool('settings', config());
    opened.section('requests').querySelector('[data-add-server="Series"]').click();
    await settle(50);
    var sidecar = opened.dom.document.querySelector('.stolon-sidecar');

    clickButton(sidecar.querySelector('.stolon-sidecar-shelf'), 'Done');
    await settle(50);

    assert.ok(opened.dom.document.querySelector('.stolon-sidecar'));
    assert.equal(statusText(sidecar.querySelector('[data-role="status"]')), 'Enter the address.');
});

test('the Issues section of Settings shows its options while issues are on and saves them back', async function () {
    var opened = await openTool('settings', config());
    var section = opened.section('issues');
    var enabled = section.querySelector('[data-field="Issues.Enabled"]');
    assert.equal(enabled.checked, true);
    assert.equal(section.querySelector('[data-field="Issues.RetentionDays"]').value, '60');
    assert.equal(section.querySelector('[data-field="Issues.LimitDays"]').value, '2');
    assert.equal(section.querySelector('[data-role="issue-options"]').classList.contains('stolon-hidden'), false);
    assert.match(section.textContent, /https:\/\/jellyfin\.example\.com\/issues\//);

    enabled.checked = false;
    enabled.dispatchEvent(new Event('change', { bubbles: true }));
    assert.equal(section.querySelector('[data-role="issue-options"]').classList.contains('stolon-hidden'), true);
    section.querySelector('[data-field="Issues.LimitDays"]').value = '0';
    clickButton(section, 'Save');
    await settle(50);

    assert.deepEqual(opened.saved[0].Issues, { Enabled: false, RetentionDays: 60, LimitDays: 1 });
    assert.equal(opened.saved[0].Requests.MovieQuotaDays, 14);
});

test('Troubleshooting runs each Requests task now and refreshes the statuses at once', async function () {
    var started = [];
    var opened = await openTool('troubleshooting', config(), { 'POST Requests/Refresh': { Total: 1 } });
    globalThis.ApiClient.getScheduledTasks = function () { return Promise.resolve([{ Key: 'RequestsSweep', Id: 'sweep' }, { Key: 'RequestsCleanup', Id: 'clean' }]); };
    globalThis.ApiClient.startScheduledTask = function (id) { started.push(id); return Promise.resolve(); };
    globalThis.ApiClient.getScheduledTask = function () { return Promise.resolve({ State: 'Idle' }); };
    var section = opened.section('requests');

    assert.deepEqual(texts(section, '.stolon-reset-info > div:first-child'), ['Verify requests', 'Refresh status', 'Refresh discovery', 'Clean expired requests', 'Refresh statuses now']);
    section.querySelector('[data-action="run-RequestsSweep"]').click();
    await settle(50);
    assert.deepEqual(started, ['sweep']);

    section.querySelector('[data-action="refresh-statuses"]').click();
    await settle(50);
    assert.ok(opened.page.requests.some(function (r) { return r.type === 'POST' && r.url === 'Requests/Refresh'; }));
    assert.equal(statusText(section.querySelector('[data-role="status"]')), 'Statuses refreshed.');

    var issues = opened.section('issues');
    assert.deepEqual(texts(issues, '.stolon-reset-info > div:first-child'), ['Clean expired requests']);
    issues.querySelector('[data-action="run-RequestsCleanup"]').click();
    await settle(50);
    assert.deepEqual(started, ['sweep', 'clean']);
});

test('every Requests and Issues key the new pages name has a string', async function () {
    var tasks = ['RequestsSweep', 'RequestsSyncProgress', 'RequestsRefreshDiscovery', 'RequestsCleanup'];
    var missing = [];
    ['necto_requests.js', 'necto_requests.html', 'necto_issues.js', 'necto_issues.html', 'necto_requests_sections.js', 'necto_groups.js'].forEach(function (file) {
        var text = readFileSync(join(paths.configDir, file), 'utf8');
        (text.match(/'(?:Requests|Issues)[A-Z][A-Za-z]+'|"(?:Requests|Issues)[A-Z][A-Za-z]+"/g) || []).forEach(function (quoted) {
            var key = quoted.slice(1, -1);
            if (tasks.indexOf(key) < 0 && !(key in strings)) missing.push(file + ': ' + key);
        });
    });
    assert.deepEqual(missing, []);
});
