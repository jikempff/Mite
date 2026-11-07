// Headless check of the Mite Bench page (site/index.html) and its review
// tools. Counts the self-test chips (PASS / FAIL), then drives the find box,
// the facets, the keyboard and the GitHub issue link like a reviewer would.
//
// Usage (after `dotnet run -c Release && python3 build.py`):
//   node bench-smoke.js [site/index.html] [light|dark]
// Needs the playwright package (npm i playwright, or a global install); the
// browser comes from PLAYWRIGHT_BROWSERS_PATH. Exits 1 on any failed check.

const path = require('path');
let chromium;
try { ({ chromium } = require('playwright')); } catch (e) { ({ chromium } = require(path.join(process.execPath, '../../lib/node_modules/playwright'))); }

const file = path.resolve(process.argv[2] || path.join(__dirname, 'site/index.html'));
const scheme = process.argv[3] === 'dark' ? 'dark' : 'light';
const fails = [];
const check = (ok, what) => { console.log((ok ? 'PASS ' : 'FAIL ') + what); if (!ok) fails.push(what); };

(async () => {
  const browser = await chromium.launch({ executablePath: process.env.CHROMIUM_PATH || undefined });
  const page = await browser.newPage({ colorScheme: scheme, viewport: { width: 1280, height: 900 } });
  const errors = [];
  page.on('pageerror', e => errors.push(e.message));
  let opened = null;
  await page.exposeFunction('__opened', u => { opened = u; });
  await page.addInitScript(() => { window.open = u => { window.__opened(u); return null; }; });
  await page.goto('file://' + file);
  await page.waitForTimeout(500);

  const chips = await page.$$eval('.checks .chip', cs => cs.map(c => c.textContent));
  const pass = chips.filter(c => c === 'PASS').length, fail = chips.filter(c => c === 'FAIL').length;
  console.log(`self-tests: ${pass} PASS, ${fail} FAIL (${scheme})`);
  check(fail === 0 && pass > 0, 'every self-test chip passes');

  const total = await page.$$eval('article.card', c => c.length);
  const visible = () => page.$$eval('article.card', cs => cs.filter(c => !c.hidden && c.offsetParent !== null).map(c => c.id));
  const status = () => page.textContent('#flt-status');
  check((await visible()).length === total, `all ${total} cards visible at start`);
  check((await status()) === `All ${total} components`, 'status line at start: ' + await status());

  // find box: AND of terms, accent / case / typography folded
  await page.fill('#flt-q', 'asymptotic');
  let v = await visible();
  check(v.includes('asymptotic-net') && v.length < total, `"asymptotic" keeps Asymptotic Net (${v.length} cards)`);
  await page.fill('#flt-q', 'ASYMPTOTIC "lath sweep"');
  const v2 = await visible();
  check(v2.length <= v.length, `adding a term never widens the result (${v.length} \u2192 ${v2.length})`);
  await page.fill('#flt-q', 'colour map');
  check((await visible()).includes('mesh-colour-map'), '"colour map" finds Mesh Colour Map');
  await page.fill('#flt-q', 'x2 - y2');
  check((await visible()).length > 0, '"x2 - y2" matches "x\u00b2 \u2212 y\u00b2"');
  check(page.url().includes('?q=x2%20-%20y2'), 'the query is in the page URL: ' + page.url().split('index.html')[1]);
  await page.fill('#flt-q', 'zzqx');
  check((await visible()).length === 0 && await page.isVisible('.finder-empty'), 'no match shows the empty state');
  check(await page.$$eval('.nav a[data-nav]', as => as.every(a => a.hidden || a.offsetParent === null)), 'nav links hide with their cards');
  await page.click('.finder-empty button');
  check((await visible()).length === total && (await page.inputValue('#flt-q')) === '', 'empty-state button clears everything');

  // facets with query previews
  const counts = await page.$$eval('[data-status]', bs => Object.fromEntries(bs.map(b => [b.dataset.status, +b.querySelector('.n').textContent])));
  check(counts.all === total && counts.failing === fail && counts.unreviewed + counts.reviewed === total, 'facet counts: ' + JSON.stringify(counts));
  check(await page.isDisabled('[data-status="failing"]') === (fail === 0), 'a facet with no hits is disabled (no dead end)');
  await page.selectOption('#flt-tab', 'Kinetics');
  v = await visible();
  // the Kinetics tab grew from one card (1.3.1) to Net Kinetics + Mechanism Modes, Kinetic State, Kinetic Supports, Kinetic Drivers, Flat Kit
  check(v.length >= 1 && v.includes('net-kinetics') && v.every(id => !['asymptotic-net', 'principal-curvature', 'lath-sweep'].includes(id)), 'tab facet: Kinetics \u2192 the kinetics cards only (' + v.join(', ') + ')');
  const opts = await page.$$eval('#flt-tab option', os => os.map(o => o.textContent));
  check(opts[0] === `All tabs (${total})`, 'tab options carry counts: ' + opts.join(', '));
  await page.selectOption('#flt-tab', 'all');

  // keyboard: / j k x n ? Esc, and nothing while typing
  await page.click('h1');
  await page.keyboard.press('j');
  let act = await page.evaluate(() => document.activeElement.closest('article') && document.activeElement.closest('article').id);
  check(act === 'principal-curvature', 'j moves to the first card: ' + act);
  await page.keyboard.press('j');
  act = await page.evaluate(() => document.activeElement.closest('article').id);
  check(act === 'gaussian-curvature', 'j again: ' + act);
  await page.keyboard.press('k');
  act = await page.evaluate(() => document.activeElement.closest('article').id);
  check(act === 'principal-curvature', 'k goes back: ' + act);
  await page.keyboard.press('f');
  const ft = await page.textContent('#toast');
  check(fail > 0 ? true : ft === 'No visible component has a failing self-test', 'f with nothing failing says so: ' + ft);
  await page.keyboard.press('x');
  check(await page.isChecked('[data-ok="principal-curvature"]'), 'x ticks "Makes sense"');
  check((await page.textContent('#done')) === '1', 'the progress counter follows the tick');
  check((await page.textContent('[data-status="reviewed"] .n')) === '1', 'the Reviewed count follows the tick');
  await page.keyboard.press('n');
  check(await page.evaluate(() => document.activeElement.dataset.note === 'principal-curvature'), 'n focuses the note');
  await page.keyboard.type('jx? looks right');
  check((await page.inputValue('[data-note="principal-curvature"]')) === 'jx? looks right' && await page.isChecked('[data-ok="principal-curvature"]'), 'letters typed in a note are text, not shortcuts');
  await page.keyboard.press('Escape');
  await page.keyboard.press('/');
  check(await page.evaluate(() => document.activeElement.id === 'flt-q'), '/ focuses the find box');
  await page.keyboard.type('geodesic');
  await page.keyboard.press('Escape');
  check((await page.inputValue('#flt-q')) === '' && (await visible()).length === total, 'Esc clears the find box');
  await page.keyboard.press('Escape');
  await page.keyboard.press('?');
  check(await page.evaluate(() => document.querySelector('.keys-dlg').open), '? opens the shortcut list');
  await page.click('#keys-on');
  await page.keyboard.press('Escape');
  await page.click('h1');
  await page.keyboard.press('j');
  check(await page.evaluate(() => !document.activeElement.closest('article')), 'with single keys off, j does nothing');
  await page.click('#keys');
  await page.click('#keys-on');
  await page.keyboard.press('Escape');
  check(await page.evaluate(() => localStorage.getItem('mite-bench:keys')) === 'on', 'the switch is remembered');

  // GitHub issue draft
  await page.click('#issue');
  await page.waitForTimeout(200);
  check(!!opened && opened.startsWith('https://github.com/jikempff/Mite/issues/new?title='), 'issue link opened: ' + (opened || '').slice(0, 80));
  const u = new URL(opened);
  check(!u.searchParams.has('labels') && !u.searchParams.has('assignees'), 'no labels / assignees (they would 404 for other reviewers)');
  const body = u.searchParams.get('body') || '';
  check(body.includes('- [x] **Principal Curvature** \u2014 jx? looks right'), 'the body is a task list with the note');
  check(u.searchParams.get('title') === `Mite Bench review: 1/${total} make sense`, 'title: ' + u.searchParams.get('title'));
  check(opened.length <= 8000, 'link length ' + opened.length + ' \u2264 8000');

  // a failing self-test (simulated on one chip): facet, f, and the issue body
  const failName = await page.evaluate(() => {
    const li = document.querySelector('#lath-sweep .checks li'); const c = li.querySelector('.chip');
    c.classList.remove('pass'); c.classList.add('fail'); c.textContent = 'FAIL'; return li.querySelector('b').textContent; });
  await page.fill('#flt-q', 'x'); await page.fill('#flt-q', '');
  check((await page.textContent('[data-status="failing"] .n')) === String(fail + 1) && !(await page.isDisabled('[data-status="failing"]')), 'the Failing facet counts the new failure');
  await page.click('[data-status="failing"]');
  v = await visible();
  check(v.length === fail + 1 && v.includes('lath-sweep'), 'Failing shows it: ' + v.join(','));
  await page.click('[data-status="all"]');
  await page.click('h1');
  await page.keyboard.press('f');
  check(await page.evaluate(() => document.activeElement.closest('article') && document.activeElement.closest('article').id) === 'lath-sweep', 'f jumps to the failing card');
  opened = null;
  await page.click('#issue');
  await page.waitForTimeout(200);
  const body2 = new URL(opened).searchParams.get('body');
  check(body2.includes('### Failing self-tests') && body2.includes('- **Lath Sweep**: ' + failName), 'the issue lists the failing test first');

  // a filtered view from the URL
  await page.goto('file://' + file + '?show=reviewed&tab=Curvature');
  await page.waitForTimeout(300);
  v = await visible();
  check(v.length === 1 && v[0] === 'principal-curvature', 'URL ?show=reviewed&tab=Curvature restores the view: ' + v.join(','));

  check(errors.length === 0, 'no page errors' + (errors.length ? ': ' + errors.join(' | ') : ''));
  await browser.close();
  console.log(fails.length ? `${fails.length} FAILED` : 'all checks passed');
  process.exit(fails.length ? 1 : 0);
})().catch(e => { console.error(e); process.exit(1); });
