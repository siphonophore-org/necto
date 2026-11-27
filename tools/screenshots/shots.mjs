// Takes every docs/screenshots image from the kit's Jellyfin at 2x, dark theme, demo data.
// Usage: node shots.mjs [Name,Name...] to take all or only the named ones. Each PNG goes to docs/screenshots/.
import { mkdirSync } from 'node:fs';
import { dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { open, close, state } from './shoot.mjs';
const OUT = fileURLToPath(new URL('../../docs/screenshots/', import.meta.url));
const only = process.argv[2];
const PAD = 8;
const DESKTOP = { width: 800, height: 1000 };
const PHONE = { width: 375, height: 900 };
const SIDECAR = { width: 800, height: 1100 };

const tool = (key) => `/web/#/configurationpage?name=necto&tool=${key}`;

// Saves the box of one element, with a little room around it, and no taller than max.
async function element(p, handle, file, pad = PAD, max = 100000) {
  const r = await p.evaluate((el) => { const b = el.getBoundingClientRect(); return { x: b.left, y: b.top + scrollY, w: b.width, h: b.height }; }, handle);
  r.h = Math.min(r.h, max);
  const width = await p.evaluate(() => document.documentElement.clientWidth);
  const x = Math.max(0, r.x - pad);
  mkdirSync(dirname(OUT + file), { recursive: true });
  await p.screenshot({ path: OUT + file, fullPage: true, clip: { x, y: Math.max(0, r.y - pad), width: Math.min(width - x, r.w + 2 * pad), height: r.h + 2 * pad } });
}
// Saves one element by itself, however far down the page it sits.
async function alone(handle, file) {
  mkdirSync(dirname(OUT + file), { recursive: true });
  await handle.screenshot({ path: OUT + file });
}
const $ = (p, selector) => p.evaluateHandle((s) => [...document.querySelectorAll(s)].find((e) => e.offsetParent !== null) || document.querySelector(s), selector);
// The tool's heading and content, without the sidebar or the Jellyfin header. A long page is cut at max.
async function main(p, file, max) { await element(p, await $(p, '.stolon-tool-shell-main'), file, PAD, max); }
// A user page's content.
async function userPage(p, file, max = 1400) {
  const width = await p.evaluate(() => document.documentElement.clientWidth);
  const height = await p.evaluate(() => document.documentElement.scrollHeight);
  mkdirSync(dirname(OUT + file), { recursive: true });
  await p.screenshot({ path: OUT + file, fullPage: true, clip: { x: 0, y: 0, width, height: Math.min(height, max) } });
}
// Clicks the first visible element of a selector whose text holds a string.
async function clickText(p, selector, text) {
  const done = await p.evaluate(([s, t]) => {
    const el = [...document.querySelectorAll(s)].find((e) => e.offsetParent !== null && e.textContent.includes(t));
    if (el) el.click();
    return !!el;
  }, [selector, text]);
  if (!done) throw new Error(`Nothing at ${selector} reads ${text}`);
  await p.waitForTimeout(1500);
}
// Opens the row that reads a text, and returns the open sidecar's panel.
async function row(p, text, selector = '.stolon-openable, .stolon-item-row') {
  await clickText(p, selector, text);
  return $(p, '.stolon-sidecar-panel');
}
// Opens or closes the collapsible section whose title reads a text, and returns the section.
async function section(p, title) {
  const h = await p.evaluateHandle((t) => {
    const titled = (x, name) => x.offsetParent && x.querySelector('.stolon-collapsible-title')?.textContent.trim() === name;
    const all = [...document.querySelectorAll('.stolon-collapsible-header')];
    // The Pages tool's section reads Custom pages on builds from before its rename.
    const header = all.find((x) => titled(x, t)) || (t === 'Pages' ? all.find((x) => titled(x, 'Custom pages')) : null);
    if (!header) throw new Error('No section ' + t);
    const box = header.closest('.stolon-collapsible-section');
    header.click();
    return box;
  }, title);
  await p.waitForTimeout(2500);
  return h;
}

const TOOLS = [
  ['Dashboard', 'dashboard'], ['Requests', 'requests'], ['Issues', 'issues'], ['Invites', 'invites'], ['Groups', 'groups'],
  ['Proxy', 'proxy'], ['DDNS', 'ddns'], ['WireGuard', 'wireguard'], ['Scripts', 'scripts'], ['Pages', 'pages'],
  ['Storage', 'storage']
];
const SECTIONS = ['Requests', 'Issues', 'Invites', 'Groups', 'Proxy', 'DDNS', 'WireGuard', 'Scripts', 'Pages', 'Storage', 'Notifications'];
const title = (s) => s.replace(/\b\w/g, (c) => c.toUpperCase()).replace('Ddns', 'DDNS').replace('Wireguard', 'WireGuard');

const shots = {};
for (const [name, key] of TOOLS) {
  shots[name] = { path: tool(key), vp: DESKTOP, act: (p) => main(p, `${name}.png`, 2000) };
  shots[`${name} Phone`] = { path: tool(key), vp: PHONE, act: (p) => main(p, `${name} Phone.png`, 1500) };
}
// Each sidecar at a window height that fits what it shows.
const sidecars = {
  'Request Detail': ['requests', 760, (p) => row(p, 'Metropolis')],
  'Issue Detail': ['issues', 760, (p) => row(p, 'Harbor Lights')],
  'Invite Editor': ['invites', 1100, (p) => row(p, 'Cousins')],
  'Group Editor': ['groups', 1300, async (p) => {
    const panel = await row(p, 'Family');
    await section(p, 'Members');
    await section(p, 'Requests');
    return panel;
  }],
  'Group Access': ['groups', 1300, async (p) => {
    const panel = await row(p, 'Kids');
    await section(p, 'Members');
    await section(p, 'Access');
    await section(p, 'Parental control');
    return panel;
  }],
  'Route Editor': ['proxy', 1100, (p) => row(p, 'media.example.com')],
  'Record Editor': ['ddns', 1100, (p) => row(p, 'Home')],
  'Peer Editor': ['wireguard', 1000, (p) => row(p, 'Travel router')],
  'Page Editor': ['pages', 1100, (p) => row(p, 'House rules')],
  'Disk Detail': ['storage', 1100, (p) => row(p, 'System')],
  'Channel Editor': ['settings', 1100, async (p) => { await section(p, 'Notifications'); return row(p, 'House chat'); }]
};
for (const [name, [key, height, find]] of Object.entries(sidecars)) {
  shots[name] = {
    path: tool(key), vp: { width: 800, height }, act: async (p) => {
      const panel = await find(p);
      await p.evaluate(() => document.activeElement && document.activeElement.blur && document.activeElement.blur());
      await element(p, panel, `${name}.png`, 0);
    }
  };
}
shots['Script Steps'] = {
  path: tool('scripts'), vp: { width: 800, height: 1600 }, act: async (p) => {
    await p.evaluate(() => {
      const select = [...document.querySelectorAll('select')].find((s) => s.offsetParent && [...s.options].some((o) => o.text === 'Too many streams'));
      select.value = [...select.options].find((o) => o.text === 'Too many streams').value;
      select.dispatchEvent(new Event('change', { bubbles: true }));
    });
    await p.waitForTimeout(1200);
    await section(p, 'Triggers');
    await element(p, await section(p, 'Steps'), 'Script Steps.png');
  }
};
for (const s of SECTIONS) {
  shots[`settings/${title(s)}`] = { path: tool('settings'), vp: { width: 800, height: 1200 }, act: async (p) => alone(await section(p, s === 'WireGuard' ? 'VPN' : s), `settings/${title(s)}.png`) };
  shots[`troubleshooting/${title(s)}`] = { path: tool('troubleshooting'), vp: { width: 800, height: 1200 }, act: async (p) => alone(await section(p, s === 'WireGuard' ? 'VPN' : s), `troubleshooting/${title(s)}.png`) };
}
// The Pages tool's Images and Stores tabs.
for (const tab of ['Images', 'Stores']) {
  shots[`Pages ${tab}`] = { path: tool('pages'), vp: DESKTOP, act: async (p) => { await clickText(p, '[role=tab], .stolon-subtab, .stolon-sub-tab, button', tab); await main(p, `Pages ${tab}.png`, 2000); } };
}
shots['Settings'] = { path: tool('settings'), vp: DESKTOP, act: async (p) => { await main(p, 'Settings.png'); } };
shots['Troubleshooting'] = { path: tool('troubleshooting'), vp: DESKTOP, act: async (p) => { await main(p, 'Troubleshooting.png'); } };
shots['Requests Page'] = { path: '/requests/', vp: { width: 1280, height: 900 }, user: 'jordan', act: (p) => userPage(p, 'Requests Page.png', 1000) };
shots['Requests Page Phone'] = { path: '/requests/', vp: PHONE, user: 'jordan', act: (p) => userPage(p, 'Requests Page Phone.png', 1000) };

// The hosted pages: an invite's sign-up page, a member's invites, and a custom page with no CSS of its own.
async function inviteToken(label) {
  const answer = await fetch(state.url + '/Invites/List', { headers: { Authorization: `MediaBrowser Token="${state.token}"` } });
  const found = (await answer.json()).find((i) => i.Label === label);
  if (!found) throw new Error('No invite ' + label);
  return '/invites/' + found.Token;
}
shots['Sign-up Page'] = { path: () => inviteToken('Cousins'), vp: { width: 1280, height: 900 }, user: 'jordan', keepForm: true, act: (p) => userPage(p, 'Sign-up Page.png', 900) };
shots['Sign-up Page Phone'] = { path: () => inviteToken('Cousins'), vp: PHONE, user: 'jordan', keepForm: true, act: (p) => userPage(p, 'Sign-up Page Phone.png', 900) };
shots['Member Invites'] = { path: '/invites', vp: { width: 1280, height: 900 }, user: 'jordan', act: (p) => userPage(p, 'Member Invites.png', 1300) };
shots['Custom Page'] = { path: '/pages/status', vp: { width: 1280, height: 500 }, user: 'jordan', direct: true, act: (p) => userPage(p, 'Custom Page.png', 500) };

let failed = 0;
for (const [name, s] of Object.entries(shots)) {
  if (only && !only.split(',').includes(name)) continue;
  let opened = null;
  try {
    opened = await open(typeof s.path === 'function' ? await s.path() : s.path, s.vp, s.user, s.keepForm, s.direct);
    const { p, errors } = opened;
    await s.act(p);
    console.log(name, errors.length ? JSON.stringify(errors) : 'ok');
  } catch (e) {
    failed++;
    console.log(name, 'FAILED', e.message.split('\n')[0]);
  }
  if (opened) await opened.context.close();
}
await close();
process.exit(failed ? 1 : 0);
