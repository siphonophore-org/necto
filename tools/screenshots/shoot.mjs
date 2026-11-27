// Opens a Necto page on the kit's Jellyfin at 2x, signed in, and waits for it to draw. shots.mjs drives it.
// PLAYWRIGHT_CORE points at playwright-core's index.mjs, and CHROMIUM at a Chromium binary.
import { existsSync, readFileSync } from 'node:fs';
import { homedir } from 'node:os';
import { fileURLToPath } from 'node:url';
const STATE_FILE = fileURLToPath(new URL('./.state/server.json', import.meta.url));
const CHROMIUM = process.env.CHROMIUM || `${homedir()}/Library/Caches/ms-playwright/chromium-1148/chrome-mac/Chromium.app/Contents/MacOS/Chromium`;
let chromium;
try {
  ({ chromium } = await import(process.env.PLAYWRIGHT_CORE || 'playwright-core'));
} catch (e) {
  console.error(`Cannot load playwright-core (${e.message}). Set PLAYWRIGHT_CORE to the path of playwright-core's index.mjs.`);
  process.exit(1);
}
if (!existsSync(CHROMIUM)) {
  console.error(`No Chromium at ${CHROMIUM}. Set CHROMIUM to a Chromium binary.`);
  process.exit(1);
}
if (!existsSync(STATE_FILE)) {
  console.error('No server. Run python3 -I server.py up first.');
  process.exit(1);
}
export const state = JSON.parse(readFileSync(STATE_FILE, 'utf8'));
// The address the pages are opened at, so every address a page shows is the demo server's. Each request to it goes
// to the kit's Jellyfin.
export const ORIGIN = 'http://media.example.com';

let browser = null;
async function launch() {
  if (!browser) browser = await chromium.launch({ executablePath: CHROMIUM });
  return browser;
}
export async function close() {
  if (browser) await browser.close();
  browser = null;
}

// The web client's saved sign in, so it opens signed in as the named person, alex unless named.
function credentials(user) {
  const who = user && user !== 'alex' ? state.users[user] : { id: state.userId, token: state.token };
  return JSON.stringify({
    Servers: [{
      Id: state.serverId, Name: state.serverName, ManualAddress: ORIGIN, LocalAddress: ORIGIN, LastConnectionMode: 2,
      ManualAddressOnly: true, UserId: who.id, AccessToken: who.token, DateLastAccessed: Date.now()
    }]
  });
}

// Opens a tab at the given window size, 800x900 unless named, signed in, at a path such as
// /web/#/configurationpage?name=necto&tool=proxy. The errors the page logs collect in errors.
export async function open(path, vp, user) {
  const b = await launch();
  const context = await b.newContext({ viewport: vp || { width: 800, height: 900 }, deviceScaleFactor: 2, colorScheme: 'dark' });
  await context.addInitScript(([creds]) => {
    try { localStorage.setItem('jellyfin_credentials', creds); } catch (e) { /* the page signs in again */ }
  }, [credentials(user)]);
  await context.route((url) => url.origin === ORIGIN, async (route) => {
    const request = route.request();
    try {
      const response = await route.fetch({ url: state.url + request.url().slice(ORIGIN.length), maxRedirects: 0 });
      await route.fulfill({ response });
    } catch (e) {
      await route.abort();
    }
  });
  const p = await context.newPage();
  const errors = [];
  p.on('pageerror', (e) => errors.push(String(e.message)));
  // The live updates socket cannot follow the demo address, and the pages work without it.
  p.on('console', (m) => { if (m.type() === 'error' && !/WebSocket/.test(m.text())) errors.push(m.text()); });
  await p.goto(ORIGIN + path);
  await p.waitForLoadState('networkidle').catch(() => {});
  await p.waitForTimeout(2500);
  // A user page signs in with its own form.
  if (user && await p.locator('input[type=password]:visible').count()) {
    await p.locator('input[type=text]:visible').first().fill(user);
    await p.locator('input[type=password]:visible').first().fill(user === 'alex' ? state.password : state.users[user].password);
    await p.keyboard.press('Enter');
    await p.waitForLoadState('networkidle').catch(() => {});
    await p.waitForTimeout(4000);
  }
  // The docs crop out the dashboard's header, so it is hidden, and nothing shows a focus ring. A user page keeps its own.
  if (!user) await p.addStyleTag({ content: '.skinHeader, header.MuiAppBar-root, header.MuiPaper-root { visibility: hidden !important; }' });
  await p.evaluate(() => document.activeElement && document.activeElement.blur && document.activeElement.blur());
  return { context, p, errors };
}
