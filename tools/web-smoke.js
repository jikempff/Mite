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
//  4c. The theme toggle recolours the 3D viewer through Mite.Core's palette:
//     dark-theme family colours on the black page, marks and seed per theme.
//  5. Layouts: the border web ends every curve on the border (no T-junctions),
//     the guide web crosses at every node of the section through the seed,
//     the fill still traces, the legend explains the viewport markers.
//  6.–8. Lath sections, structure, kinetics.
//  9. Kinetic strain per state: one utilization per lath and state, the
//     envelope over the motion, and lath colours that follow the fold slider.
//  5b. Chebyshev angle map (node angles, cell integrals, coloured cells, lock
//     angle recolours in place) and isocurve labels (one per curve, its level).
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
    // software WebGL: newer Chrome needs the explicit opt-in on a machine without a GPU (a CI runner)
    args: ['--use-gl=swiftshader', '--enable-unsafe-swiftshader', '--enable-webgl', '--ignore-gpu-blocklist'],
  });
  console.log(`browser: ${process.env.CHROMIUM || 'Playwright Chromium'} ${browser.version()}`);
  const page = await browser.newPage({ viewport: { width: 1600, height: 1000 } });
  const errors = [];
  const consoleErrors = [];
  const pickShape = async (k) => { await page.click('#shape-current'); await page.click(`#shape-list [data-shape=${k}]`); };
  page.on('pageerror', (e) => errors.push(e.message));
  page.on('console', (m) => { if (m.type() === 'error' && !/status of 404/.test(m.text())) consoleErrors.push(m.text()); });
  page.on('crash', () => consoleErrors.push('the page crashed'));
  await page.goto(`${base}/index.html?v=${Date.now()}`);
  const booted = await page.waitForFunction(() => document.getElementById('boot').classList.contains('hidden'), null, { timeout: 180000 }).then(() => true, () => false);
  if (!booted) {
    // say why, instead of a bare timeout: the boot line, WebGL, and every error the page or its worker logged
    const why = await page.evaluate(() => ({
      text: document.getElementById('boot-text')?.textContent,
      webgl: !!document.createElement('canvas').getContext('webgl2'),
    })).catch((e) => ({ text: 'page not readable: ' + e.message }));
    check(false, `the app boots within 3 min (stuck at "${why.text}", WebGL2 ${why.webgl ? 'available' : 'NOT available'})`);
    for (const e of [...new Set([...errors, ...consoleErrors])].slice(0, 12)) console.log('  error: ' + e.slice(0, 400));
    await browser.close();
    process.exit(1);
  }

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
  // 4b. isolated umbilics: the triaxial ellipsoid 1.5 × 1 × 0.7 has exactly four,
  //     all lemons (index +½) at (±1.264, 0, ±0.377); the torus has none
  await pickShape('ellipsoid'); await idle(180000);
  await page.check('#umb'); await idle(180000);
  const umb = await page.evaluate(() => { const c = window.__mite.curv; return c && { n: c.umbilicIndex.length, sum: c.umbilicIndex.reduce((a, b) => a + b, 0), pts: c.umbilicPoints }; });
  const x0 = 1.5 * Math.sqrt((2.25 - 1) / (2.25 - 0.49)), z0 = 0.7 * Math.sqrt((1 - 0.49) / (2.25 - 0.49));
  const worst = umb && umb.n ? Math.max(...[...Array(umb.n).keys()].map((i) => Math.abs(Math.abs(umb.pts[3 * i]) - x0) + Math.abs(umb.pts[3 * i + 1]) + Math.abs(Math.abs(umb.pts[3 * i + 2]) - z0))) : 99;
  check(umb && umb.n === 4 && Math.abs(umb.sum - 2) < 1e-9 && worst < 0.15, `ellipsoid: four isolated umbilics, index sum 2, at (±${x0.toFixed(3)}, 0, ±${z0.toFixed(3)}) (${umb?.n} found, sum ${umb?.sum}, worst offset ${worst.toFixed(3)})`);
  // 4c. the theme toggle recolours the viewer through Mite.Core's palette (palette.js): the dark page gets the dark
  //     theme's family colours (famA #5b6bff, not the light theme's #2a3cff as before 2026-11-07), the umbilic marks
  //     turn neon yellow (ink on white), and the seed the brand neon; toggling back restores the light colours
  const themeColours = () => page.evaluate(() => {
    const v = window.__mite.viewer, hex = (c) => '#' + c.getHex().toString(16).padStart(6, '0');
    const line = (fam) => { const o = v.curveObjects.find((c) => c.family === fam); return o && hex(o.line.material.color); };
    const overlay = (tag) => { const o = v.overlayGroup.children.find((c) => c.userData.tag === tag); return o && hex(o.material.color); };
    return { theme: document.documentElement.dataset.theme || 'light', dark: v.dark, A: line('A'), B: line('B'), umb: overlay('umb'), lemon: overlay('umbLemon'), seed: v.dirHandle ? hex(v.dirHandle.material.color) : overlay('seed'), wire: v.wire && hex(v.wire.material.color), css: getComputedStyle(document.documentElement).getPropertyValue('--famA').trim() };
  });
  const themeLight = await themeColours();
  await page.click('#theme'); await page.waitForTimeout(300);
  const themeDark = await themeColours();
  await page.click('#theme'); await page.waitForTimeout(300);
  const themeBack = await themeColours();
  const light = { A: '#2a3cff', B: '#ff1f4f', umb: '#0a0a0a', lemon: '#ff1f4f', seed: '#00a653' }, darkExp = { A: '#5b6bff', B: '#ff3d6a', umb: '#d9ff00', lemon: '#ff3d6a', seed: '#00ff85' };
  const same = (a, e) => Object.keys(e).every((k) => a[k] === e[k]);
  check(themeLight.theme === 'light' && same(themeLight, light) && themeDark.theme === 'dark' && themeDark.dark && same(themeDark, darkExp) && themeDark.css === darkExp.A,
    `theme toggle recolours the families, marks and seed per theme (light A ${themeLight.A} B ${themeLight.B} marks ${themeLight.umb} seed ${themeLight.seed} → dark A ${themeDark.A} B ${themeDark.B} marks ${themeDark.umb} seed ${themeDark.seed})`);
  check(themeBack.theme === 'light' && same(themeBack, light) && themeBack.wire === (themeLight.wire || themeBack.wire), 'toggling back restores the light theme colours');
  await pickShape('torus'); await idle(180000);
  const umbT = await page.evaluate(() => window.__mite.curv && window.__mite.curv.umbilicIndex.length);
  check(umbT === 0, `torus: no isolated umbilic (${umbT})`);
  await page.uncheck('#umb');

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
  // the guide web: nodes every Spacing along the horizontal section through the
  // seed, both families through every node, every curve rim to rim
  await page.click("#layouts [data-layout='4']"); await idle(180000);
  const gd = await page.evaluate(() => {
    const d = window.__mite.netData; const nodes = []; for (let i = 0; i + 2 < (d?.guideNodes || []).length; i += 3) nodes.push([d.guideNodes[i], d.guideNodes[i + 1], d.guideNodes[i + 2]]);
    const dist = (p, c) => { let b = 1e9; for (let i = 0; i + 1 < c.length; i++) { const a = c[i], q = c[i + 1]; const ab = [q[0] - a[0], q[1] - a[1], q[2] - a[2]]; const l2 = ab[0] ** 2 + ab[1] ** 2 + ab[2] ** 2; const t = l2 < 1e-30 ? 0 : Math.max(0, Math.min(1, ((p[0] - a[0]) * ab[0] + (p[1] - a[1]) * ab[1] + (p[2] - a[2]) * ab[2]) / l2)); b = Math.min(b, Math.hypot(a[0] + t * ab[0] - p[0], a[1] + t * ab[1] - p[1], a[2] + t * ab[2] - p[2])); } return b; };
    const through = Math.max(...nodes.map((p) => Math.max(Math.min(...d.a.map((c) => dist(p, c))), Math.min(...d.b.map((c) => dist(p, c))))));
    return { layout: window.__mite.layout, nodes: nodes.length, a: d?.countA || 0, b: d?.countB || 0, ends: d?.endClasses || [], through, spacing: d?.resolvedSpacing, guide: d?.guide?.length || 0 };
  });
  check(gd.layout === 4 && gd.guide > 10 && gd.nodes >= 10 && gd.a === gd.nodes && gd.b === gd.nodes, `guide web on the catenoid: ${gd.nodes} nodes along the section through the seed, ${gd.a} + ${gd.b} curves`);
  check(gd.through < 1e-3 * gd.spacing && gd.ends.every((c) => c === 0), `guide web: both families through every node (within ${gd.through?.toExponential(1)}), every end on the rim (${gd.ends.filter((c) => c !== 0).length} not)`);
  await page.click("#layouts [data-layout='0']"); await idle(180000);
  const fill = await page.evaluate(() => ({ layout: window.__mite.layout, n: (window.__mite.netData?.countA || 0) + (window.__mite.netData?.countB || 0), legend: document.getElementById('end-legend').textContent }));
  check(fill.layout === 0 && fill.n > 20, `evenly spaced fill traces (${fill.n} curves)`);
  check(/seed/.test(fill.legend), 'marker legend names the seed');
  await page.click("#layouts [data-layout='3']"); await idle(180000);

  // 5b. panels: the curvature-line net of the catenoid (meridians and parallels — a conjugate net on a
  //     surface of revolution) gives planar panels; the asymptotic web the skewest ones (Liu et al. 2006);
  //     planarizing the asymptotic panels makes them flat only by leaving the surface
  await page.click('#nets [data-net=conjugate]'); await idle(180000);
  await page.check('#panels'); await idle(180000);
  const pc = await page.evaluate(() => window.__mite.panels);
  check(pc && !pc.error && pc.quads > 50 && pc.meanPlanarity < 0.004 && pc.planarShare > 0.9, `curvature-line panels are planar (${pc?.error || `${pc?.quads} quads, mean planarity ${pc?.meanPlanarity?.toExponential(1)}, ${Math.round(100 * (pc?.planarShare || 0))} % ≤ 0.01`})`);
  const panelMesh = await page.evaluate(() => window.__mite.viewer.overlayGroup.children.find((c) => c.userData.tag === 'panels')?.geometry.index.count || 0);
  check(panelMesh >= 3 * (pc?.quads || 0), `panels drawn in the viewport (${panelMesh} indices)`);
  await page.click('#planarize'); await idle(180000);
  const pq = await page.evaluate(() => window.__mite.panels);
  const size = await page.evaluate(() => window.__mite.size);
  check(pq && pq.planarized && pq.planarizedMaxDeviation < 0.2 * pq.maxDeviation && pq.maxMove < 0.01 * size, `curvature-line panels planarize almost in place (deviation ${pq?.maxDeviation?.toExponential(1)} → ${pq?.planarizedMaxDeviation?.toExponential(1)}, nodes moved up to ${pq?.maxMove?.toExponential(1)}, ${pq?.planarizeIterations} iterations)`);
  await page.click('#nets [data-net=asymptotic]'); await idle(180000);
  const pa = await page.evaluate(() => window.__mite.panels);
  check(pa && !pa.error && pa.quads > 50 && pa.meanPlanarity > 4 * (pc?.meanPlanarity || 0) && pa.meanPlanarity > 0.004, `asymptotic panels are skew (mean planarity ${pa?.meanPlanarity?.toExponential(1)} vs ${pc?.meanPlanarity?.toExponential(1)} for curvature lines)`);
  await page.click('#planarize'); await idle(180000);
  const pp = await page.evaluate(() => window.__mite.panels);
  check(pp && pp.planarized && pp.maxMove > 20 * (pq?.maxMove || 0) && pp.maxOffSurface > 10 * (pq?.maxOffSurface || 0), `asymptotic panels flatten only by leaving the surface (nodes moved up to ${pp?.maxMove?.toExponential(1)} vs ${pq?.maxMove?.toExponential(1)}; deviation ${pp?.maxDeviation?.toExponential(1)} → ${pp?.planarizedMaxDeviation?.toExponential(1)} after ${pp?.planarizeIterations} iterations)`);
  const recipe = await page.$eval('#recipe', (e) => e.textContent);
  check(/Net Topology .*Planarize Mesh/.test(recipe), 'recipe names Net Topology → Planarize Mesh');
  await page.uncheck('#panels'); await idle(60000);
  check(await page.evaluate(() => !window.__mite.panels && !window.__mite.viewer.overlayGroup.children.some((c) => c.userData.tag === 'panels')), 'panels clear when switched off');
  // 5b. the Chebyshev angle map: every node carries its shear angle, every
  //     cell its Hazzidakis integral; the cells are drawn coloured, the lock
  //     angle recolours without a new trace; isocurves carry their level and
  //     one label each
  await page.click('#nets [data-net=chebyshev]'); await idle(180000);
  const cheb = await page.evaluate(() => {
    const c = window.__mite.netData?.chebyshev, cells = window.__mite.viewer.overlayGroup.children.find((o) => o.userData.tag === 'cells');
    return c && { nodes: c.nodeCount, cells: c.cellCount, angles: c.angles.length, finite: c.angles.filter((w) => w > 0 && w < 180).length, min: c.minAngle, max: c.maxAngle, total: c.totalCurvature,
      k: c.cellCurvature.length, drawn: cells ? cells.geometry.index.count : 0, legend: document.getElementById('clegend').classList.contains('hidden') ? '' : document.getElementById('clegend').textContent, stats: document.getElementById('net-stats').textContent };
  });
  check(cheb && cheb.nodes > 20 && cheb.cells > 10 && cheb.angles === cheb.nodes && cheb.k === cheb.cells, `Chebyshev net carries ${cheb?.nodes} node angles and ${cheb?.cells} cell integrals`);
  // the default net on the catenoid (8 nodes per side) runs into the catenoid's curvature and locks at its far corners:
  // the angles must stay strictly inside (0°, 180°) and the locked count must be exactly the nodes outside the lock band
  check(cheb && cheb.finite >= cheb.nodes - 4 && cheb.min > 0 && cheb.max < 180 && cheb.min < 90 && cheb.max > 90, `shear angles ${cheb?.min.toFixed(1)}° … ${cheb?.max.toFixed(1)}° around the 90° seed`);
  const lockCount = await page.evaluate(() => { const c = window.__mite.netData.chebyshev; return { outside: c.angles.filter((w) => w >= 0 && (w < 45 || w > 135)).length, stated: +(document.getElementById('net-stats').textContent.match(/(\d+) locked/) || [0, -1])[1] }; });
  check(lockCount.outside === lockCount.stated, `locked count is the nodes outside 45° … 135° (${lockCount.stated})`);
  check(cheb && cheb.total < 0 && Math.abs(cheb.total) < 4 * Math.PI * Math.tanh(1.2), `the catenoid's cells read negative curvature, less than the whole band's Gauss image 4π tanh 1.2 (∬K dA = ${cheb?.total.toFixed(3)})`);
  check(cheb && cheb.drawn === 6 * cheb.cells && /shear angle/.test(cheb.legend) && /Hazzidakis/.test(cheb.stats), `angle map drawn (${cheb?.drawn / 6} cells) with its legend`);
  const before = await page.$eval('#net-stats', (e) => +(e.textContent.match(/(\d+) locked/) || [0, -1])[1]);
  await page.$eval('#lockangle', (e) => { e.value = '80'; e.dispatchEvent(new Event('input', { bubbles: true })); });
  await page.waitForTimeout(200);
  const after = await page.evaluate(() => ({ locked: +(document.getElementById('net-stats').textContent.match(/(\d+) locked/) || [0, -1])[1], legend: document.getElementById('clegend').textContent, busy: !document.getElementById('busy').classList.contains('hidden') }));
  check(after.locked > before && after.locked >= 0 && /80°/.test(after.legend) && !after.busy, `lock angle 45° → 80° recolours in place: ${before} → ${after.locked} nodes locked`);
  await page.$eval('#lockangle', (e) => { e.value = '45'; e.dispatchEvent(new Event('input', { bubbles: true })); });
  await page.uncheck('#anglemap'); await page.waitForTimeout(100);
  check(await page.evaluate(() => !window.__mite.viewer.overlayGroup.children.some((o) => o.userData.tag === 'cells') && document.getElementById('clegend').classList.contains('hidden')), 'angle map off removes the cells and the legend');
  await page.check('#anglemap');
  await page.click('#nets [data-net=isocurves]'); await idle(180000);
  const iso = await page.evaluate(() => {
    const d = window.__mite.netData, labels = [...document.querySelectorAll('.labels .label')];
    return { curves: d.countA, levels: d.curveLevels?.length, values: d.levelValues?.length, labels: labels.length, texts: labels.map((l) => +l.textContent),
      onLevel: labels.every((l) => d.levelValues.some((v) => Math.abs(v - +l.textContent) <= 0.5 * Math.pow(10, -(l.textContent.split('.')[1] || '').length))) };
  });
  check(iso.levels === iso.curves && iso.values === 12 && iso.curves >= 12, `every isocurve carries its level (${iso.curves} curves, ${iso.values} levels)`);
  check(iso.labels === Math.min(400, iso.curves) && iso.onLevel, `one label per isocurve showing its level (${iso.labels})`);
  await page.uncheck('#isolabels'); await page.waitForTimeout(100);
  check((await page.$$eval('.labels .label', (l) => l.length)) === 0, 'labels off removes them');
  await page.check('#isolabels');
  await page.click('#nets [data-net=asymptotic]'); await idle(180000);
  check((await page.$$eval('.labels .label', (l) => l.length)) === 0, 'another net clears the labels');

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

  // 7b. pieces: the catenoid web cut into 6 m stock with a 300 mm splice, a 150 mm joint margin, a 3 mm kerf
  await page.click('#piecesrun'); await idle(180000);
  const pcs = await page.evaluate(() => {
    const r = window.__mite.pieces, o = { stock: +document.getElementById('pstock').value, splice: +document.getElementById('psplice').value, margin: +document.getElementById('pmargin').value, kerf: +document.getElementById('pkerf').value };
    const toM = (() => { const s = window.__mite.stats; const dx = s.max[0] - s.min[0], dy = s.max[1] - s.min[1]; return (+document.getElementById('span').value) / Math.max(dx, dy); })();
    const len = (pts) => { let L = 0; for (let i = 1; i < pts.length; i++) L += Math.hypot(pts[i][0] - pts[i - 1][0], pts[i][1] - pts[i - 1][1], pts[i][2] - pts[i - 1][2]); return L; };
    const laths = [...window.__mite.netData.a, ...window.__mite.netData.b];
    const lathLen = laths.reduce((a, l) => a + len(l), 0);
    const boardLoads = r.boardPieces.map((b) => b.reduce((a, i) => a + r.pieceLengths[i], 0) + (b.length - 1) * (o.kerf / 1000 / toM));
    const overlays = window.__mite.viewer.overlayGroup.children.filter((c) => /^piece/.test(c.userData.tag)).length;
    const cuts = window.__mite.viewer.overlayGroup.children.find((c) => c.userData.tag === 'cuts')?.geometry.attributes.position.count || 0;
    return { r, o, toM, lathLen, longestM: r.longestPiece * toM, shortestM: r.shortestPiece * toM, minClearMm: r.minClearance * toM * 1000, spliceM: o.splice / 1000,
      maxLoad: Math.max(...boardLoads) * toM, pieceSum: r.pieceLengths.reduce((a, b) => a + b, 0), overlays, cuts,
      stats: document.getElementById('pieces-stats').textContent, nLaths: laths.length };
  });
  check(pcs.r && !pcs.r.error && pcs.r.laths === pcs.nLaths && pcs.r.pieces > pcs.r.laths, `laths cut into pieces (${pcs.r?.error || `${pcs.r.laths} laths → ${pcs.r.pieces} pieces, ${pcs.r.splices} splices`})`);
  check(pcs.longestM <= pcs.o.stock + 1e-6, `every piece fits the stock (longest ${pcs.longestM.toFixed(2)} of ${pcs.o.stock} m)`);
  check(pcs.r.joints > 50 && pcs.minClearMm >= pcs.o.margin - 1e-6, `every cut clears the joints by the margin (nearest ${pcs.minClearMm.toFixed(0)} ≥ ${pcs.o.margin} mm, ${pcs.r.joints} joints)`);
  // the pieces carry the laths plus half a splice at each side of every cut
  check(Math.abs(pcs.pieceSum - (pcs.lathLen + pcs.r.splices * pcs.spliceM / pcs.toM)) < 1e-6 * pcs.lathLen, `pieces add up to the laths plus the splices (${(pcs.pieceSum * pcs.toM).toFixed(2)} = ${(pcs.lathLen * pcs.toM).toFixed(2)} + ${pcs.r.splices} × ${pcs.spliceM} m)`);
  check(pcs.r.boards >= pcs.r.lowerBound && pcs.r.boards <= Math.ceil(11 / 9 * pcs.r.lowerBound + 6 / 9) && pcs.maxLoad <= pcs.o.stock + 1e-6, `boards packed within the FFD guarantee (${pcs.r.boards} boards, lower bound ${pcs.r.lowerBound}, fullest ${pcs.maxLoad.toFixed(2)} m)`);
  check(Math.abs(pcs.r.material - pcs.pieceSum / (pcs.r.boards * pcs.o.stock / pcs.toM)) < 1e-9 && pcs.r.material > 0.5 && pcs.r.material <= 1, `material figure is Σ pieces / (boards × stock) = ${(100 * pcs.r.material).toFixed(1)} %`);
  check(pcs.overlays >= 4 && pcs.cuts === pcs.r.splices && /boards/.test(pcs.stats), `pieces drawn alternately with the cuts marked (${pcs.overlays} overlays, ${pcs.cuts} cut marks)`);
  await page.check('#pnotches'); await idle(180000);
  const pn = await page.evaluate(() => ({ r: window.__mite.pieces, solid: window.__mite.viewer.overlayGroup.children.find((c) => c.userData.tag === 'notches')?.geometry.index.count || 0 }));
  check(pn.r.lapNotches === 2 * (pn.r.joints - pn.r.mixedCrossings) && pn.r.spliceNotches === 2 * pn.r.splices && pn.solid === 36 * (pn.r.lapNotches + pn.r.spliceNotches), `notch solids: two per crossing and two per cut (${pn.r.lapNotches} + ${pn.r.spliceNotches}, ${pn.solid / 36} boxes)`);
  await page.uncheck('#pnotches'); await idle(180000);
  // balanced pieces: never more pieces, and every piece well above the remainder the greedy cut leaves
  await page.check('#pbalance'); await idle(180000);
  const pb = await page.evaluate(() => { const r = window.__mite.pieces; return { pieces: r.pieces, shortest: r.shortestPiece, boards: r.boards, lb: r.lowerBound }; });
  check(pb.pieces === pcs.r.pieces && pb.shortest > pcs.r.shortestPiece && pb.boards >= pb.lb, `balanced cut: same ${pb.pieces} pieces, shortest ${(pb.shortest * pcs.toM).toFixed(2)} m instead of ${pcs.shortestM.toFixed(2)} m, ${pb.boards} boards (was ${pcs.r.boards})`);
  await page.uncheck('#pbalance'); await idle(180000);
  await page.click('#dl-recipe');
  check(/Lath Segment: .*StockLength/.test(await page.$eval('#recipe', (e) => e.textContent)), 'recipe names Lath Segment with the stock length');

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
  // record: the motion there and back, captured from the viewport and downloaded as a video
  {
    const canRecord = await page.evaluate(() => !document.getElementById('krecord').disabled);
    if (canRecord) {
      const dl = page.waitForEvent('download', { timeout: 120000 });
      await page.click('#krecord');
      const file = await dl;
      await page.waitForFunction(() => window.__mite.recording && window.__mite.recording.done, null, { timeout: 120000 });
      const rec = await page.evaluate(() => ({ ...window.__mite.recording, states: window.__mite.kin.states.length, label: document.getElementById('krecord').textContent, bg: window.__mite.viewer.renderer.getClearAlpha() }));
      check(rec.bytes > 20000 && rec.frames === 2 * rec.states + 16 && /\.(webm|mp4)$/.test(file.suggestedFilename()),
        `record downloads the motion there and back (${file.suggestedFilename()}, ${(rec.bytes / 1024).toFixed(0)} kB, ${rec.frames} frames for ${rec.states} states, ${rec.mime})`);
      check(rec.label === 'record' && rec.bg === 0, 'after recording the button and the transparent viewport are back');
    } else check(false, 'record button available (MediaRecorder in Chromium)');
  }

  // 9. the kit of the pressed web: strips, joints, a hub and a zip
  await page.click('#kitrun'); await idle(300000);
  const kit = await page.evaluate(() => window.__mite.kit);
  check(kit && !kit.error && kit.strips > 20 && kit.hubs === 1 && kit.sheets >= 1 && kit.zip.length > 1000 && kit.assemblySvg.startsWith('<svg'), `kit built (${kit?.error || `${kit?.strips} strips, ${kit?.joints} joints, ${kit?.hubs} hub, ${kit?.sheets} sheets`})`);
  check(kit && kit.flatness < 1e-3 && kit.slotMin > 0, `kit is flat with slots (${kit?.flatness?.toExponential(1)}, ${kit?.slotMin})`);
  // 10. nesting: every strip on the bed, the material use reported against the area bound; a small bed splits the long strips with laps clear of the joints
  check(kit && kit.tooLong === 0 && kit.sheets >= kit.areaBound && kit.utilisation > 0.3 && kit.utilisation < 0.95 && kit.lapClashes === 0, `kit nested (${kit?.nesting})`);
  const statsText = await page.$eval('#kit-stats', (e) => e.textContent);
  check(/material used \d+ %/.test(statsText), `the kit summary states the material use (${statsText.match(/material used [^·]*/)?.[0]})`);
  await page.fill('#ksheetw', '300'); await page.fill('#ksheeth', '200');
  await page.click('#kitrun'); await idle(300000);
  const kit2 = await page.evaluate(() => window.__mite.kit);
  check(kit2 && !kit2.error && kit2.tooLong === 0 && kit2.split > 0 && kit2.pieces > kit2.split && kit2.lapClashes === 0 && kit2.splice >= 20, `a 300 × 200 bed splits the long strips with laps between the joints (${kit2?.nesting})`);
  await page.click('#ksplit');
  await page.click('#kitrun'); await idle(300000);
  const kit3 = await page.evaluate(() => window.__mite.kit);
  check(kit3 && !kit3.error && kit3.split === 0 && kit3.tooLong > 0 && kit3.strips === kit2.strips, `with splitting off the long strips go on their own drawing (${kit3?.tooLong} of ${kit3?.strips})`);
  await page.click('#ksplit'); await page.fill('#ksheetw', '1000'); await page.fill('#ksheeth', '600');

  // 9. strain per state (José's priority 3, 2026-10-02): every state carries each lath's utilization, the envelope is
  //    their maximum over the motion, and the laths on screen wear the colour of the state shown, of the envelope, or
  //    of their family
  const env = await page.evaluate(() => {
    const k = window.__mite.kin, n = k.envelope.length;
    let maxErr = 0, stateOk = true;
    for (let l = 0; l < n; l++) {
      const m = Math.max(...k.states.map((s) => s.lathUtil[l]));
      maxErr = Math.max(maxErr, Math.abs(m - k.envelope[l]));
      if (k.states[k.envelopeState[l]].lathUtil[l] !== k.envelope[l]) stateOk = false;
    }
    const lens = k.states.every((s) => s.lathUtil.length === n && s.a.length + s.b.length === n);
    const buildable = k.envelope.filter((u) => u <= 1).length;
    return { n, laths: k.laths, maxErr, stateOk, lens, buildable, reported: k.buildableLaths, worst: k.worstLath, worstU: k.envelope[k.worstLath], peak: Math.max(...k.envelope), sources: [...new Set(k.envelopeSource)] };
  });
  check(env.n === env.laths && env.lens, `every state carries one utilization per lath (${env.n} laths)`);
  check(env.maxErr === 0 && env.stateOk, `envelope = max over the states, at its stated state (error ${env.maxErr})`);
  check(env.buildable === env.reported && env.worstU === env.peak, `buildable through the motion ${env.reported} / ${env.n}, worst lath ${env.worst} at ${env.peak?.toFixed(2)} (${env.sources.join(', ')})`);
  const colours = async () => page.evaluate(() => {
    const S = window.__mite, k = S.kin, st = k.states[S.kinShown], na = st.a.length, col = window.__miteUtilColour;
    let dState = 0, dEnv = 0, dFam = 0;
    for (const o of S.viewer.curveObjects) {
      const l = (o.family === 'A' ? 0 : na) + o.index, c = o.line.material.color;
      const d = (x) => Math.max(Math.abs(c.r - x[0]), Math.abs(c.g - x[1]), Math.abs(c.b - x[2]));
      const fam = c.clone().setHex(S.viewer.colour(o.role));   // the family's theme colour as three.js stores it (colour-managed)
      dState = Math.max(dState, d(col(st.lathUtil[l]))); dEnv = Math.max(dEnv, d(col(k.envelope[l]))); dFam = Math.max(dFam, d([fam.r, fam.g, fam.b]));
    }
    return { dState, dEnv, dFam, legend: document.getElementById('ulegend').classList.contains('hidden') ? '' : document.getElementById('ulegend-title').textContent, bar: document.getElementById('ulegend-bar').getBoundingClientRect().width, shown: S.kinShown };
  });
  await page.$eval('#kfold', (e) => { e.value = 0; e.dispatchEvent(new Event('input')); });
  const c0 = await colours();
  check(c0.shown === 0 && c0.dState < 1e-6 && /fold 0\.00/.test(c0.legend), `fold 0: laths coloured by their strain in that state (Δ ${c0.dState.toExponential(1)}, legend "${c0.legend}")`);
  check(c0.bar >= 100, `the colour bar of the legend is a bar, not a swatch (${c0.bar.toFixed(0)} px wide)`);
  await page.click('#kcolour [data-kc=envelope]');
  const c1 = await colours();
  check(c1.dEnv < 1e-6 && /envelope/.test(c1.legend), `envelope colouring (Δ ${c1.dEnv.toExponential(1)}, legend "${c1.legend}")`);
  await page.click('#kcolour [data-kc=off]');
  const c2 = await colours();
  check(c2.dFam < 1e-6 && c2.legend === '', 'family colours back, legend hidden');
  await page.click('#kcolour [data-kc=state]');
  const plot = await page.evaluate(() => ({ p: window.__mite.kinPlot, cap: document.getElementById('kin-caption').textContent }));
  check(plot.p?.elastic && plot.p.uMax >= 1.2 && /strain energy/.test(plot.cap), `energy diagram drawn (utilization axis 0 … ${plot.p?.uMax?.toFixed(2)}, Π up to ${plot.p?.eMax?.toFixed(1)} J)`);
  // the hub's six rays end on two bolted plates (the Kit block's default): a plate per hub, rays at angles that do not move while deploying
  check(kit && kit.plates === 1 && kit.plateDiameter > 0 && kit.plateAngleDrift < 1 && kit.platelessHubs === 0 && kit.zip.length > 1000,
    `kit hub plate (${kit?.plates} plate set, Ø ${kit?.plateDiameter?.toFixed(4)} model units, rays move ${kit?.plateAngleDrift?.toExponential(1)}°${kit?.plateCramped ? ', cramped' : ''})`);

  const broken = await page.$$eval('img', (imgs) => imgs.filter((i) => !i.complete || i.naturalWidth === 0).map((i) => i.getAttribute('src')));
  check(broken.length === 0, `all icons load (${broken.join(', ') || 'none broken'})`);
  check(errors.length === 0, `no page errors (${errors.join(' | ') || 'none'})`);

  await browser.close();
  console.log(`${fails.length === 0 ? 'OK' : fails.length + ' FAILED'}`);
  process.exit(fails.length ? 1 : 0);
})();
