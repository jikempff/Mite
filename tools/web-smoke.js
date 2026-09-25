// Headless smoke test of the browser app (src/Mite.Web). Regression checks:
//
//  1. Boot race — change the shape while the first shape is still loading.
//     Before 1.2.6 the default net was traced with the placeholder size, so
//     the spacing was 4 % of 1 instead of 4 % of the mesh and the app sat
//     on "tracing…" for minutes. The trace must now finish and its spacing
//     must scale with the mesh (~0.226 on the default catenoid).
//  2. Mid-trace shape switches still resolve.
//  3. Component strips name the plugin component of the active mode / net
//     and no icon fails to load.
//
// Usage (needs a published site and a static server):
//   dotnet publish src/Mite.Web -c Release -o /tmp/miteweb -p:RunAOTCompilation=false
//   (cd /tmp/miteweb/wwwroot && python3 -m http.server 8765 &)
//   npm i playwright        # once; the browser comes from PLAYWRIGHT_BROWSERS_PATH
//   node tools/web-smoke.js [http://localhost:8765]
//
// Exits 1 on the first failed check.

const { chromium } = require('playwright');

const base = process.argv[2] || 'http://localhost:8765';
const fails = [];
const check = (ok, what) => { console.log((ok ? 'PASS ' : 'FAIL ') + what); if (!ok) fails.push(what); };

(async () => {
  const browser = await chromium.launch({
    executablePath: process.env.CHROMIUM || undefined,
    args: ['--use-gl=swiftshader', '--enable-webgl', '--ignore-gpu-blocklist'],
  });
  const page = await browser.newPage({ viewport: { width: 1600, height: 1000 } });
  const errors = [];
  page.on('pageerror', (e) => errors.push(e.message));
  await page.goto(`${base}/index.html?v=${Date.now()}`);
  await page.waitForFunction(() => document.getElementById('boot').classList.contains('hidden'), null, { timeout: 180000 });

  // 1. shape change during the first load
  await page.selectOption('#shape', 'catenoid');
  const idle = async (ms) => page.waitForFunction(() => document.getElementById('busy').classList.contains('hidden'), null, { timeout: ms }).then(() => true, () => false);
  check(await idle(120000), 'shape change during the first load finishes (no boot race)');
  const st = await page.evaluate(() => ({ spacing: window.__mite.netData?.resolvedSpacing, size: window.__mite.size, net: window.__mite.net, curves: (window.__mite.netData?.countA || 0) + (window.__mite.netData?.countB || 0) }));
  check(st.net === 'asymptotic' && st.curves > 0, `default asymptotic net traced (${st.curves} curves)`);
  check(st.spacing > 0.03 * st.size && st.spacing < 0.05 * st.size, `spacing scales with the loaded mesh (${st.spacing?.toFixed(3)} of size ${st.size?.toFixed(2)})`);

  // 2. switch mid-trace
  await page.selectOption('#shape', 'hyperboloid');
  await page.waitForTimeout(1200);
  await page.selectOption('#shape', 'torus');
  check(await idle(180000), 'switching the shape mid-trace resolves');
  check((await page.evaluate(() => window.__mite.shape)) === 'torus', 'last shape wins');

  // 3. component strips and icons
  await page.click('#nets [data-net=geodesicBoth]');
  check(await idle(180000), 'geodesic net traces');
  const strip = await page.$eval('#gh-net', (e) => ({ hidden: e.classList.contains('hidden'), text: e.textContent, src: e.querySelector('img').getAttribute('src') }));
  check(!strip.hidden && /Geodesic Net/.test(strip.text) && /GeodesicNet\.png$/.test(strip.src), `net strip names the component (${strip.text.trim()})`);
  await page.click('#modes [data-mode=K]');
  await page.waitForTimeout(500);
  const ms = await page.$eval('#gh-mode', (e) => ({ hidden: e.classList.contains('hidden'), text: e.textContent }));
  check(!ms.hidden && /Gaussian Curvature/.test(ms.text), `mode strip names the component (${ms.text.trim()})`);
  await page.click('#modes [data-mode=shaded]');
  await page.waitForTimeout(300);
  check(await page.$eval('#gh-mode', (e) => e.classList.contains('hidden')), 'mode strip hides for plain shading');
  const broken = await page.$$eval('img', (imgs) => imgs.filter((i) => !i.complete || i.naturalWidth === 0).map((i) => i.getAttribute('src')));
  check(broken.length === 0, `all icons load (${broken.join(', ') || 'none broken'})`);
  check(errors.length === 0, `no page errors (${errors.join(' | ') || 'none'})`);

  await browser.close();
  console.log(`${fails.length === 0 ? 'OK' : fails.length + ' FAILED'}`);
  process.exit(fails.length ? 1 : 0);
})();
