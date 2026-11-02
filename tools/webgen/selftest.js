// Headless run of the Mite Bench self-tests (tools/webgen/site/index.html).
//
// Opens the built page in Chromium once per colour scheme, reads every
// self-test chip (li[data-check] .chip) and prints
//   light: 223 PASS, 0 FAIL, 0 not run, 0 page errors
// Exits 1 when any check fails or does not run, when the page throws, or
// when the page has no self-tests at all. CI runs it after build.py.
//
// Usage:
//   cd tools/webgen && dotnet run -c Release && python3 build.py
//   npm i playwright        # once; or set NODE_PATH to an install of it
//   node tools/webgen/selftest.js [path/to/index.html] [--min N]
//
// --min N also fails when fewer than N self-tests are on the page, so a
// notes.py edit that drops a component's checks does not pass silently.

const path = require('path');
const { chromium } = require('playwright');

const args = process.argv.slice(2);
let min = 1, file = path.join(__dirname, 'site', 'index.html');
for (let i = 0; i < args.length; i++) {
  if (args[i] === '--min') min = parseInt(args[++i], 10);
  else file = args[i];
}
const url = 'file://' + path.resolve(file);

(async () => {
  const exe = process.env.CHROMIUM_PATH || undefined;   // e.g. /opt/pw-browsers/chromium
  const browser = await chromium.launch(exe ? { executablePath: exe } : {});
  let bad = false;
  const lines = [];
  for (const scheme of ['light', 'dark']) {
    const ctx = await browser.newContext({ colorScheme: scheme, viewport: { width: 1280, height: 900 } });
    const page = await ctx.newPage();
    const errors = [];
    page.on('pageerror', e => errors.push(e.message));
    await page.goto(url, { waitUntil: 'load' });
    await page.waitForTimeout(500);
    const r = await page.evaluate(() => {
      const lis = [...document.querySelectorAll('li[data-check]')];
      const out = { total: lis.length, pass: 0, fail: 0, failed: [] };
      for (const li of lis) {
        const t = (li.querySelector('.chip') || {}).textContent;
        if (t === 'PASS') out.pass++;
        else if (t === 'FAIL') {
          out.fail++;
          const card = li.closest('.card');
          out.failed.push(((card && card.id) || '?') + ': ' + ((li.querySelector('b') || {}).textContent || '') + ' — ' + ((li.querySelector('.ct') || {}).textContent || ''));
        }
      }
      return out;
    });
    const notRun = r.total - r.pass - r.fail;
    const line = `${scheme}: ${r.pass} PASS, ${r.fail} FAIL, ${notRun} not run, ${errors.length} page errors`;
    console.log(line);
    lines.push(line);
    r.failed.forEach(f => console.log('  FAIL ' + f));
    errors.forEach(e => console.log('  page error: ' + e));
    if (r.fail || notRun || errors.length || r.total < min) bad = true;
    if (r.total < min) console.log(`  only ${r.total} self-tests on the page, expected at least ${min}`);
    await ctx.close();
  }
  await browser.close();
  if (process.env.GITHUB_STEP_SUMMARY) {
    require('fs').appendFileSync(process.env.GITHUB_STEP_SUMMARY, '### Mite Bench self-tests\n\n' + lines.map(l => '- ' + l).join('\n') + '\n');
  }
  process.exit(bad ? 1 : 0);
})().catch(e => { console.error(e); process.exit(1); });
