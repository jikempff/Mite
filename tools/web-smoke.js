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
//  4. The section choice reaches the kernel (1.2.5's binding dropped it):
//     a round bar sweeps as a tube and gets its own utilization.
//  5. Layouts: the border web ends every curve on the border (no T-junctions),
//     the fill still traces, the legend explains the viewport markers.
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
  const pickShape = async (k) => { await page.click('#shape-current'); await page.click(`#shape-list [data-shape=${k}]`); };
  page.on('pageerror', (e) => errors.push(e.message));
  await page.goto(`${base}/index.html?v=${Date.now()}`);
  await page.waitForFunction(() => document.getElementById('boot').classList.contains('hidden'), null, { timeout: 180000 });

  // 1. shape change during the first load
  await pickShape('catenoid');
  const idle = async (ms) => page.waitForFunction(() => document.getElementById('busy').classList.contains('hidden'), null, { timeout: ms }).then(() => true, () => false);
  check(await idle(120000), 'shape change during the first load finishes (no boot race)');
  const st = await page.evaluate(() => ({ spacing: window.__mite.netData?.resolvedSpacing, size: window.__mite.size, net: window.__mite.net, curves: (window.__mite.netData?.countA || 0) + (window.__mite.netData?.countB || 0) }));
  check(st.net === 'asymptotic' && st.curves > 0, `default asymptotic net traced (${st.curves} curves)`);
  check(st.spacing > 0.03 * st.size && st.spacing < 0.05 * st.size, `spacing scales with the loaded mesh (${st.spacing?.toFixed(3)} of size ${st.size?.toFixed(2)})`);

  // 2. switch mid-trace
  await pickShape('hyperboloid');
  await page.waitForTimeout(1200);
  await pickShape('torus');
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
  // 5. layouts: the default symmetric web has every end on the border or at a
  //    node and no T-junction; the border web and the evenly spaced fill still trace
  await pickShape('catenoid'); await idle(180000);
  await page.click('#nets [data-net=asymptotic]'); await idle(180000);
  const sym = await page.evaluate(() => ({ layout: window.__mite.layout, web: window.__mite.netData?.web, ends: window.__mite.netData?.endClasses || [], t: window.__mite.netData?.crossings?.tJunctions }));
  check(sym.layout === 3 && sym.web && sym.web.rotational && sym.web.quads > 50, `symmetric web on the catenoid is rotational (${sym.web?.quads} quads)`);
  check(sym.ends.every((c) => c === 0) && sym.t === 0, 'symmetric web: no T-junctions, no stray ends');
  check(sym.web.diagonalError[0] < 1e-3, `rotational web: meridian diagonals are geodesic (${sym.web.diagonalError[0].toExponential(1)})`);
  await page.click("#layouts [data-layout='1']"); await idle(180000);
  const web = await page.evaluate(() => ({ layout: window.__mite.layout, ends: window.__mite.netData?.endClasses || [], n: (window.__mite.netData?.countA || 0) + (window.__mite.netData?.countB || 0) }));
  check(web.layout === 1 && web.n > 20 && web.ends.every((c) => c === 0), `border web: ${web.n} curves, every end on the border (${web.ends.filter((c) => c !== 0).length} not)`);
  await page.click("#layouts [data-layout='0']"); await idle(180000);
  const fill = await page.evaluate(() => ({ layout: window.__mite.layout, n: (window.__mite.netData?.countA || 0) + (window.__mite.netData?.countB || 0), legend: document.getElementById('end-legend').textContent }));
  check(fill.layout === 0 && fill.n > 20, `evenly spaced fill traces (${fill.n} curves)`);
  check(/seed/.test(fill.legend), 'marker legend names the seed');
  await page.click("#layouts [data-layout='3']"); await idle(180000);

  // 6. one section for every lath; the section choice reaches the kernel
  await page.check('#solid'); await idle(180000);
  const rect = await page.evaluate(() => ({ faces: window.__mite.viewer.overlayGroup.children.find((c) => c.userData.tag === 'sweepAll')?.geometry.index.count || 0, util: window.__mite.lathUtil?.length }));
  await page.selectOption('#section', '1'); await idle(180000);
  const round = await page.evaluate(() => window.__mite.viewer.overlayGroup.children.find((c) => c.userData.tag === 'sweepAll')?.geometry.index.count || 0);
  check(rect.faces > 0 && rect.util > 20, `every lath swept and checked (${rect.util} laths)`);
  check(round > 3 * rect.faces, `round bars sweep as tubes (${round} vs ${rect.faces} indices)`);
  await page.uncheck('#solid'); await page.selectOption('#section', '0'); await idle(180000);

  // 7. structure: equilibrium and the joint model
  await page.click('#frame'); await idle(180000);
  const fr = await page.evaluate(() => window.__mite.frame);
  check(!fr.error && fr.equilibriumError < 1e-8 && Math.abs(fr.reactionSum - fr.totalLoad) < 1e-6 * fr.totalLoad, `frame in equilibrium (${fr.error || fr.equilibriumError?.toExponential(1)})`);
  check(fr.joints > 50, `every crossing is a frame node (${fr.joints} joints)`);

  // 8. kinetics on the 3-fold Enneper: a coarse symmetric web moves
  await pickShape('enneper3'); await idle(180000);
  await page.click('.presets [data-sp="8"]'); await idle(180000);
  await page.click('#kinrun'); await idle(300000);
  const kin = await page.evaluate(() => window.__mite.kin);
  check(kin && !kin.error && kin.states.length >= 5, `kinetics ran (${kin?.error || kin?.states.length + ' states'})`);
  const kl = kin?.states[kin.states.length - 1];
  check(kl && kl.height < 1e-3 * kin.states[0].height && kl.drift < 5e-3 && kl.converged, `the web presses flat (height ${kin?.states[0].height.toFixed(2)} → ${kl?.height.toExponential(1)}, drift ${kl?.drift.toExponential(1)})`);
  // the flat state is a hexagon: the support function of the flat nodes repeats every 60°
  const hex = await page.evaluate(() => {
    const st = window.__mite.kin.states.at(-1), pts = [...st.a, ...st.b].flat();
    const h = (t) => Math.max(...pts.map((q) => q[0] * Math.cos(t) + q[1] * Math.sin(t)));
    let worst = 0;
    for (let d = 0; d < 60; d += 5) { const v = [0, 1, 2, 3, 4, 5].map((k) => h((d + 60 * k) * Math.PI / 180)); worst = Math.max(worst, (Math.max(...v) - Math.min(...v)) / Math.max(...v)); }
    return worst;
  });
  check(hex < 0.04, `the flat web is a hexagon (support function 60°-periodic to ${(hex * 100).toFixed(2)} %)`);

  const broken = await page.$$eval('img', (imgs) => imgs.filter((i) => !i.complete || i.naturalWidth === 0).map((i) => i.getAttribute('src')));
  check(broken.length === 0, `all icons load (${broken.join(', ') || 'none broken'})`);
  check(errors.length === 0, `no page errors (${errors.join(' | ') || 'none'})`);

  await browser.close();
  console.log(`${fails.length === 0 ? 'OK' : fails.length + ' FAILED'}`);
  process.exit(fails.length ? 1 : 0);
})();
