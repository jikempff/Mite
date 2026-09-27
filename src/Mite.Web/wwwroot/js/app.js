// Mite interactive: wires the panel to the kernel (Mite.Core on WebAssembly,
// in a Web Worker) and the three.js viewer.

import { Kernel } from './kernel.js';
import { Viewer, FAMILY_A, FAMILY_B, FAMILY_G, NEON } from './viewer.js';
import { diverging, sequential, utilizationColor, legendGradient, cssRgb, UTIL_STOPS } from './colormaps.js';
import { Loft } from './loft.js';
import { readMeshFile } from './loaders.js';
import { drawLathPlot, drawUnroll } from './plots.js';
import { SHAPES, GROUPS, CLASSES, symbolSVG } from './shapes.js';
import { meshToOBJ, curvesToOBJ, unrollToSVG, save } from './export.js';

const $ = (id) => document.getElementById(id);

// ---------------------------------------------------------------------------
// Catalogue
// ---------------------------------------------------------------------------

const MODE_NOTES = {
  shaded: 'Plain shading.',
  K: 'Gaussian curvature K = k1·k2 (angle defect over mixed Voronoi areas). Blue K < 0: saddle-shaped, asymptotic curves exist. Red K > 0: dome-shaped.',
  H: 'Mean curvature H = (k1 + k2)/2 from the cotangent Laplacian. H = 0 is a minimal (soap-film) surface.',
  k1: 'Maximum principal curvature (Rusinkiewicz per-face fit, tensor-smoothed over the rings set below).',
  k2: 'Minimum principal curvature. On a developable surface it is 0.',
  radius: 'Smallest bending radius 1/max(|k1|,|k2|): compare with the radius your lath or panel can take.',
  zebra: 'Reflection lines. Kinked stripes mean a tangent break (G0), smooth stripes G2. View-dependent, computed in the shader.',
  anticlastic: 'Usable anticlastic region: K < 0 and the two asymptotic families crossing at more than 15°. Asymptotic nets fill only this region.',
};

const NET_NOTES = {
  none: '',
  asymptotic: 'Both families of asymptotic curves (zero normal curvature): laths bend only about their weak axis and twist — stand them upright. Symmetric web (Schling): nodes every spacing along the two asymptotic curves through the seed, every other node the crossing of the curves through them — an asymptotic parameterisation is fixed by exactly that, so the web is a clean quad net with no stubs and no T-junctions, and it keeps the surface\'s symmetry. On a minimal surface the laths cross at 90° (identical joints); on a surface of revolution the meridian diagonals are geodesics (an AAG web). Border / seed-cross webs and the evenly spaced fill are the older layouts.',
  conjugate: 'The two principal curvature line families: an approximate conjugate net, the layout for planar-quad panels. Undefined at umbilics.',
  geodesic: 'One family of straightest geodesics. Web: seeded every spacing along the border (or along the perpendicular geodesic through the seed), each run until it leaves the mesh — neighbours converge where K > 0 and diverge where K < 0 (Jacobi), so the strips are only even where the surface allows. Fill: grown sideways from the seed, each new geodesic starting at the angle that keeps its strip closest to constant width (Jacobi field), stopped where strips close. Flat laths follow geodesics without in-plane bending.',
  geodesicBoth: 'Two geodesic families crossing at the family angle at the seed, each grown with Jacobi start angles.',
  streamMax: 'Lines of maximum principal curvature, evenly spaced.',
  streamMin: 'Lines of minimum principal curvature, evenly spaced.',
  chebyshev: 'Compass-method Chebyshev net: every edge has the same length — the flat-lattice kinematics of an elastic gridshell. The angles collapse where the net locks.',
  isocurves: 'Level curves of a scalar field on the mesh (marching triangles).',
};

const END_CLASS = [
  { name: 'on the border', color: null, what: 'the curve reached the mesh border — no marker' },
  { name: 'at the K = 0 line', color: [0.16, 0.24, 1.00], what: 'the curve stopped where the asymptotic directions cease to exist (K ≥ 0 or the families cross below the minimum angle): there is no asymptotic curve beyond' },
  { name: 'on a neighbour (T-junction)', color: [0.85, 1.00, 0.00], what: 'the curve stopped on a neighbouring curve of its own family: the evenly spaced fill does this wherever the spacing closes; a web only where the family converges until two laths would touch' },
  { name: 'step limit', color: [1.00, 0.12, 0.31], what: 'the curve hit the step budget without reaching a border (closed or very long surface)' },
];
const SEED_COLOR = [0.00, 0.85, 0.42];

// ---------------------------------------------------------------------------
// State
// ---------------------------------------------------------------------------

const S = {
  kernel: null, viewer: null,
  shape: 'saddle', params: {}, res: 40,
  vertices: null, triangles: null, stats: null, size: 1, center: [0, 0, 0],
  curv: null, mode: 'shaded', smooth: 2,
  net: 'none', netData: null, seed: -1, seedPoint: null, seedNormal: [0, 0, 1], dir: [1, 0.35, 0],
  loft: null, file: null,
  lath: null, selected: null, lathUtil: null,
  frame: null, aag: null, kin: null, kinTimer: null, drive: 'flatten', picked: [], picking: false,
  busy: 0, gen: 0, loading: false, layout: 3,
};

function busy(on, text) {
  S.busy += on ? 1 : -1;
  $('busy').classList.toggle('hidden', S.busy <= 0);
  if (text) $('busy-text').textContent = text;
}
async function withBusy(text, fn) {
  busy(true, text);
  try { return await fn(); } finally { busy(false); }
}
const fmt = (x, d = 3) => Number.isFinite(x) ? (+x).toFixed(d) : '–';
const pct = (v) => (v / 100) * S.size;
// metres per model unit: the real span (Shape block) over the larger horizontal extent
function toMetres() { if (!S.stats) return 1; const dx = S.stats.max[0] - S.stats.min[0], dy = S.stats.max[1] - S.stats.min[1]; return (+$('span').value) / Math.max(1e-9, dx, dy); }
const mmToModel = (mm) => mm / 1000 / toMetres();
const K = (method, ...args) => S.kernel.call(method, ...args);

// ---------------------------------------------------------------------------
// Shape
// ---------------------------------------------------------------------------

function shapeCard(key, big = false) {
  const sh = SHAPES[key], c = CLASSES[sh.cls];
  return `<span class="sym${big ? ' big' : ''}">${symbolSVG(key, big ? 56 : 44)}</span>` +
    `<span class="txt"><span class="nm">${sh.name}<i class="cls ${c.dot}" title="${c.tag}"></i><em>${c.tag}</em></span>` +
    sh.formula.map((f) => `<span class="fx">${f}</span>`).join('') + '</span>';
}

function buildShapeUI() {
  const cur = $('shape-current'), list = $('shape-list');
  list.innerHTML = GROUPS.map((g) => `<div class="sgroup"><h3>${g.title}${g.sub ? `<small>${g.sub}</small>` : ''}</h3>` +
    g.keys.map((k) => `<button class="sitem" role="option" data-shape="${k}">${shapeCard(k)}</button>`).join('') + '</div>').join('');
  const open = (on) => { list.classList.toggle('hidden', !on); cur.setAttribute('aria-expanded', on ? 'true' : 'false'); cur.classList.toggle('open', on); };
  cur.addEventListener('click', () => open(list.classList.contains('hidden')));
  list.addEventListener('click', (e) => {
    const b = e.target.closest('[data-shape]'); if (!b) return;
    open(false);
    selectShape(b.dataset.shape);
  });
  document.addEventListener('keydown', (e) => { if (e.key === 'Escape') open(false); });
  $('res').addEventListener('input', (e) => { $('res').nextElementSibling.value = e.target.value; });
  $('res').addEventListener('change', (e) => { S.res = +e.target.value; loadShape(); });
  renderShapeParams();
}

function selectShape(key) {
  if (!SHAPES[key]) return;
  S.shape = key; S.seed = -1;
  renderShapeParams();
  loadShape();
}

function renderShapeParams() {
  const def = SHAPES[S.shape];
  const box = $('shape-params');
  box.innerHTML = '';
  S.params = {};
  def.params.forEach(([name, min, max, val, step], i) => {
    S.params[i] = val;
    const row = document.createElement('div');
    row.className = 'row slider';
    row.innerHTML = `<label>${name}</label><input type="range" min="${min}" max="${max}" step="${step}" value="${val}"><output>${val}</output>`;
    const inp = row.querySelector('input');
    inp.addEventListener('input', () => { row.querySelector('output').value = inp.value; S.params[i] = +inp.value; loadShape({ light: true }); });
    inp.addEventListener('change', () => { S.params[i] = +inp.value; loadShape(); });
    box.appendChild(row);
  });
  $('shape-current').innerHTML = shapeCard(S.shape, true) + '<span class="chev" aria-hidden="true">▾</span>';
  document.querySelectorAll('#shape-list [data-shape]').forEach((b) => b.classList.toggle('on', b.dataset.shape === S.shape));
  $('file-row').classList.toggle('hidden', S.shape !== 'file');
  $('res').closest('.row').classList.toggle('hidden', S.shape === 'file');
  $('shape-note').textContent = def.note;
}

let lightPending = false, lightRunning = false;
async function loadShape({ light = false } = {}) {
  if (!S.kernel) return;
  if (light) { // slider or handle being dragged: coalesce, latest wins
    lightPending = true;
    if (lightRunning) return;
    lightRunning = true;
    try { while (lightPending) { lightPending = false; await applyShape(true); } } finally { lightRunning = false; }
    return;
  }
  await withBusy('building shape…', () => applyShape(false));
}

async function applyShape(light) {
  const gen = ++S.gen;
  // While a shape is in flight, S.size / S.stats still describe the previous
  // mesh (or the placeholder size 1 at boot). traceNet() refuses to run in
  // that window: a trace issued then would queue behind the new mesh in the
  // worker and run with a spacing scaled to the wrong size — at boot 4 % of
  // 1 instead of 4 % of the mesh, i.e. hundreds of near-endless curves and
  // an app stuck on "tracing…". The load itself re-traces when it lands.
  S.loading = true;
  try { await applyShapeInner(gen, light); }
  finally { if (gen === S.gen) S.loading = false; } // a superseded load leaves the flag to its successor
}

async function applyShapeInner(gen, light) {
  let stats;
  if (S.shape === 'loft') {
    if (!S.loft) S.loft = new Loft(4, 6, 3);
    const g = S.loft.build(Math.max(12, S.res), Math.max(9, Math.round(S.res * 0.75)));
    stats = await K('loadMesh', g.vertices, g.triangles, false);
  } else if (S.shape === 'file') {
    if (!S.file) { $('mesh-stats').textContent = 'Choose or drop a file.'; return; }
    stats = await K('loadMesh', S.file.vertices, S.file.triangles, $('weld').checked);
  } else {
    const p = SHAPES[S.shape].params.map((_, i) => S.params[i] ?? 0);
    const res = S.shape === 'torus' || S.shape === 'hyperboloid' || S.shape === 'catenoid' ? Math.round(S.res * 1.5) : S.res;
    stats = await K('shape', S.shape, p[0] || 0, p[1] || 0, p[2] || 0, res);
  }
  if (gen !== S.gen) return; // superseded
  S.stats = stats;
  S.vertices = await K('vertices');
  S.triangles = await K('triangles');
  if (gen !== S.gen) return;
  const d = [0, 1, 2].map((k) => stats.max[k] - stats.min[k]);
  S.size = Math.hypot(...d) || 1;
  S.center = [0, 1, 2].map((k) => 0.5 * (stats.max[k] + stats.min[k]));
  const fit = !light && !(S.shape === 'loft' && S.viewer.mesh);
  S.viewer.setMesh(S.vertices, S.triangles, { fit });
  if (S.shape === 'loft') S.viewer.setHandles(S.loft.flatControls(), 0.012 * S.size); else S.viewer.clearHandles();
  $('mesh-stats').innerHTML = `<b>${stats.vertices}</b> vertices · <b>${stats.faces}</b> triangles · avg edge <b>${fmt(stats.avgEdge)}</b> · border vertices <b>${stats.boundaryVertices}</b>` +
    (stats.welded ? ` · welded <b>${stats.welded}</b>` : '') + (stats.removedFaces ? ` · removed <b>${stats.removedFaces}</b> faces` : '') + ` · ${stats.ms} ms`;
  $('hud-shape').textContent = `${SHAPES[S.shape].name} · ${stats.vertices} v · size ${fmt(S.size, 2)}`;
  S.curv = null; S.lath = null; S.selected = null; S.frame = null; S.netData = null; S.lathUtil = null; S.aag = null; S.picked = [];
  stopKinetics(true); S.viewer.clearOverlay('picked'); S.viewer.clearOverlay('supports'); S.viewer.clearOverlay('sweepAll');
  S.viewer.clearCurves(); S.viewer.clearOverlay('ends'); S.viewer.clearOverlay('seed');
  $('lath-result').classList.add('hidden');
  $('frame-stats').textContent = '';
  updateSpacingReadout();
  S.loading = false; // the mesh, size and stats are current from here on
  await applyMode();
  if (S.seed >= S.stats.vertices) S.seed = -1;
  if (!light) await updateSeedHandle();
  if (!light && S.net !== 'none') await traceNet();
  if (light && S.net !== 'none') scheduleNet();
}

let netTimer = null;
function scheduleNet() { clearTimeout(netTimer); netTimer = setTimeout(() => traceNet(), 350); }

// ---------------------------------------------------------------------------
// Analysis colouring
// ---------------------------------------------------------------------------

async function ensureCurvature() {
  if (!S.curv) {
    const gen = S.gen;
    const c = await K('curvature', S.smooth, 0.05);
    if (gen !== S.gen) return null;
    S.curv = c;
    const n = c.k1.length;
    $('curv-stats').innerHTML = `k1 <b>${fmt(Math.min(...c.k1), 2)} … ${fmt(Math.max(...c.k1), 2)}</b> · k2 <b>${fmt(Math.min(...c.k2), 2)} … ${fmt(Math.max(...c.k2), 2)}</b>\n` +
      `usable anticlastic <b>${c.anticlastic}</b> / ${n} vertices · umbilics <b>${c.umbilics.length}</b> · ${c.ms} ms`;
  }
  return S.curv;
}

// Which plugin component each web control runs — shown as an icon + name
// under the chip rows so the app reads as a front end of the Grasshopper tab.
const GH_MODE = {
  shaded: null, K: ['GaussianCurvature', 'Gaussian Curvature'], H: ['MeanCurvature', 'Mean Curvature'],
  k1: ['PrincipalCurvature', 'Principal Curvature', 'K1'], k2: ['PrincipalCurvature', 'Principal Curvature', 'K2'],
  radius: ['PrincipalCurvature', 'Principal Curvature', '1 / max |k|'], zebra: null, anticlastic: ['AsymptoticNet', 'Asymptotic Net', 'Anticlastic output'],
};
const GH_NET = {
  none: null, asymptotic: ['AsymptoticNet', 'Asymptotic Net'], conjugate: ['ConjugateNet', 'Conjugate Net'],
  geodesic: ['GeodesicNet', 'Geodesic Net', 'one family'], geodesicBoth: ['GeodesicNet', 'Geodesic Net', 'two families'],
  streamMax: ['Streamlines', 'Curvature Streamlines', 'MaxDir on'], streamMin: ['Streamlines', 'Curvature Streamlines', 'MaxDir off'],
  chebyshev: ['ChebyshevNet', 'Chebyshev Net'], isocurves: ['MeshIsocurves', 'Mesh Isocurves'],
};
function showComponent(stripId, entry) {
  const el = $(stripId); if (!el) return;
  el.classList.toggle('hidden', !entry);
  if (!entry) return;
  el.querySelector('img').src = `icons/${entry[0]}.png`;
  el.querySelector('span').innerHTML = `Grasshopper <b>${entry[1]}</b>` + (entry[2] ? ` <span class="unit">${entry[2]}</span>` : '');
}

async function applyMode() {
  const v = S.viewer;
  showComponent('gh-mode', GH_MODE[S.mode]);
  const legend = $('legend');
  v.setZebra(S.mode === 'zebra');
  $('mode-note').textContent = MODE_NOTES[S.mode];
  const needCurv = !(S.mode === 'shaded' || S.mode === 'zebra') || $('dirs').checked || $('umb').checked;
  const c = needCurv ? await ensureCurvature() : null;
  if (needCurv && !c) return;
  if (S.mode === 'shaded' || S.mode === 'zebra') { v.setColors(null); legend.classList.add('hidden'); }
  else {
    let res, title;
    if (S.mode === 'K') { res = diverging(c.k); title = 'K'; }
    else if (S.mode === 'H') { res = diverging(c.h); title = 'H'; }
    else if (S.mode === 'k1') { res = diverging(c.k1); title = 'k1'; }
    else if (S.mode === 'k2') { res = diverging(c.k2); title = 'k2'; }
    else if (S.mode === 'radius') {
      const r = c.k1.map((a, i) => 1 / Math.max(1e-9, Math.max(Math.abs(a), Math.abs(c.k2[i]))));
      const cap = 3 * S.size;
      res = sequential(r.map((x) => Math.min(x, cap)), 0, null, 0.9); title = 'ρ min';
    } else if (S.mode === 'anticlastic') {
      const cols = new Float32Array(c.k.length * 3);
      const usable = new Set(); // vertices with usable directions: d1 of the asymptotic field is not exposed, use K<0 && anticlastic count proxy
      for (let i = 0; i < c.k.length; i++) { const on = c.k[i] < 0; cols[3 * i] = on ? 0.42 : 0.9; cols[3 * i + 1] = on ? 0.62 : 0.9; cols[3 * i + 2] = on ? 0.84 : 0.88; }
      res = { colors: cols, min: 'K ≥ 0', max: 'K < 0', stops: [[0, [0.9, 0.9, 0.88]], [1, [0.42, 0.62, 0.84]]] }; title = 'anticlastic';
    }
    v.setColors(res.colors);
    $('legend-bar').style.background = legendGradient(res.stops);
    $('legend-min').textContent = typeof res.min === 'number' ? fmt(res.min, 2) : res.min;
    $('legend-max').textContent = typeof res.max === 'number' ? fmt(res.max, 2) : res.max;
    $('legend-title').textContent = title;
    legend.classList.remove('hidden');
  }
  if ($('dirs').checked && c) {
    const n = c.k1.length;
    const every = Math.max(1, Math.round(n / 900));
    v.showDirections(S.vertices, c.d1, c.d2, c.normals, every, S.stats.avgEdge * 1.4);
  } else v.hideDirections();
  if ($('umb').checked && c) {
    const flat = [];
    for (const i of c.umbilics) flat.push(S.vertices[3 * i], S.vertices[3 * i + 1], S.vertices[3 * i + 2]);
    v.showPoints('umb', flat, NEON.yellow, 8);
  } else v.clearOverlay('umb');
  v.setWireframe($('wire').checked);
  if (!S.kin && !S.aag) v.setMeshOpacity(surfaceOpacity());
  updateRecipe();
}

// ---------------------------------------------------------------------------
// Seed and direction
// ---------------------------------------------------------------------------

// A web seeded from the border needs no seed point; the seed cross and the fill start there
function seedRelevant() { return ['asymptotic', 'conjugate', 'geodesic', 'geodesicBoth', 'streamMax', 'streamMin', 'chebyshev'].includes(S.net) && !(S.layout === 1 && S.net !== 'chebyshev'); }
function directionRelevant() { return ['geodesic', 'geodesicBoth', 'chebyshev'].includes(S.net) && seedRelevant(); }

async function updateSeedHandle() {
  const v = S.viewer;
  v.clearDirectionHandle(); v.clearOverlay('seed');
  if (!seedRelevant() || !S.vertices) return;
  let idx = S.seed;
  if (idx < 0) idx = await K('nearestVertex', S.center[0], S.center[1], S.center[2]);
  if (idx < 0) return;
  const p = [S.vertices[3 * idx], S.vertices[3 * idx + 1], S.vertices[3 * idx + 2]];
  S.seedPoint = p;
  const c = await ensureCurvature();
  S.seedNormal = c ? [c.normals[3 * idx], c.normals[3 * idx + 1], c.normals[3 * idx + 2]] : [0, 0, 1];
  $('seed-label').textContent = S.seed >= 0 ? `vertex ${S.seed}` : `auto (vertex ${idx})`;
  if (directionRelevant()) v.setDirectionHandle(p, S.seedNormal, S.dir, 0.12 * S.size);
  else v.showPoints('seed', p, NEON.green, 11);
}

// ---------------------------------------------------------------------------
// Nets
// ---------------------------------------------------------------------------

function spacingAbs() { return pct(+$('spacing').value); }
function updateSpacingReadout() {
  $('spacing-out').textContent = fmt(spacingAbs(), 3);
  $('edge-out').textContent = fmt(pct(+$('edge').value), 3);
}

function netOptions() {
  return {
    spacing: spacingAbs(), step: 0, continuous: $('continuous').checked, maxCurves: 300,
    seed: S.seed, direction: S.dir, edgeLength: pct(+$('edge').value), count: +$('count').value,
    angleDeg: +$('famangle').value, levels: +$('levels').value, field: $('isofield').value,
    minAngle: +$('minangle').value, fromBorder: $('fromborder').checked, borderAngle: +$('borderangle').value, jacobi: $('jacobi').checked,
    layout: S.net === 'asymptotic' ? S.layout : (S.layout === 3 ? 1 : S.layout),
    symmetry: +$('symmetry').value,
  };
}

async function traceNet() {
  const v = S.viewer;
  S.lath = null; S.selected = null; S.frame = null; S.lathUtil = null; S.aag = null;
  stopKinetics(true); S.viewer.setMeshOpacity(surfaceOpacity()); showUtilLegend(false);
  $('lath-result').classList.add('hidden');
  $('frame-stats').textContent = ''; $('laths-stats').textContent = ''; $('aag-stats').classList.add('hidden');
  v.clearOverlay('supports'); v.clearOverlay('deformed'); v.clearOverlay('sweepAll');
  $('net-note').textContent = NET_NOTES[S.net];
  showComponent('gh-net', GH_NET[S.net]);
  $('famB-row').classList.toggle('hidden', ['geodesic', 'streamMax', 'streamMin', 'isocurves'].includes(S.net));
  if (S.net === 'none') {
    v.clearCurves(); v.clearOverlay('ends'); S.netData = null;
    $('net-stats').textContent = ''; $('net-warn').classList.add('hidden'); $('hud-net').textContent = ''; $('quality').classList.add('hidden'); $('spacing-count').textContent = '';
    $('end-legend')?.classList.add('hidden');
    await updateSeedHandle(); updateRecipe(); return;
  }
  if (!S.stats || S.loading) return; // the pending shape load traces the net itself
  const gen = S.gen;
  v.ghostCurves(true);
  await withBusy('tracing…', async () => {
    let d;
    try { d = await K('net', S.net, netOptions()); }
    catch (e) { if (gen !== S.gen) return; $('net-warn').textContent = String(e.message || e); $('net-warn').classList.remove('hidden'); v.clearCurves(); return; }
    if (gen !== S.gen) return;
    S.netData = d;
    const showG = !!d.web && $('diagonals').checked && d.g?.length > 0;
    await K('useDiagonals', showG);
    drawNetCurves();
    if ($('crossings').checked && d.crossings) v.showPoints('crossings', d.crossings.points, v.ink(), 3.5); else v.clearOverlay('crossings');
    showEndMarkers();
    const closed = S.stats.boundaryVertices === 0;
    let s = `A <b>${d.countA}</b>${d.countB ? ` · B <b>${d.countB}</b>` : ''} laths · length <b>${fmt(d.minLength, 2)} … ${fmt(d.maxLength, 2)}</b>` +
      (d.resolvedSpacing ? ` · spacing <b>${fmt(d.resolvedSpacing, 3)}</b> (${fmt(d.resolvedSpacing * toMetres(), 2)} m)` : '') + ` · ${d.ms} ms`;
    if (d.crossings) s += `\ncrossings <b>${d.crossings.count}</b> · angles <b>${fmt(d.crossings.minAngle, 1)}° … ${fmt(d.crossings.maxAngle, 1)}°</b> · T-junctions <b>${d.crossings.tJunctions}</b>`;
    if (d.web) {
      const w = d.web;
      const sym = w.rotational ? 'surface of revolution (rotational AAG web)' : w.symmetry > 1 ? `${w.symmetry}-fold — one sector traced, the rest rotated (nodes repeat within ${fmt(w.symmetryError * toMetres() * 1000, 1)} mm)` : 'none used';
      s += `\nweb: <b>${w.nodes}</b> nodes · <b>${w.quads}</b> quads · ${w.singular ? `singular seed with <b>${w.rays}</b> rays` : 'regular seed'} · symmetry: <b>${sym}</b>`;
      s += `\ndiagonals: geodesic error ${fmt(w.diagonalError[0], 3)} (G) / ${fmt(w.diagonalError[1], 3)} — |kg|·spacing, 0 = geodesic`;
    }
    $('net-stats').innerHTML = s;
    $('spacing-count').textContent = `${d.countA + d.countB} laths`;
    const warns = [...d.warnings, ...(d.web?.notes || [])];
    $('net-warn').textContent = warns.join(' ');
    $('net-warn').classList.toggle('hidden', warns.length === 0);
    $('hud-net').textContent = `${S.net}${d.web ? ' web' : ''} · ${d.countA + d.countB} laths · ${d.ms} ms`;
    drawQuality(d, closed);
    await updateSeedHandle();
    showNetParams();
    if ($('colorutil').checked || $('solid').checked) await runLathAll();
    updateRecipe();
  });
}

function traceNetStatsOnly() {
  const d = S.netData; if (!d) return;
  if (S.lath && S.selected) analyseSelected();
}

function currentFamilies() {
  const d = S.netData; if (!d) return { a: [], b: [], g: [] };
  if (S.aag) return { a: S.aag.a, b: S.aag.b, g: S.aag.g };
  const showG = !!d.web && $('diagonals').checked;
  return { a: d.a, b: d.b, g: showG ? (d.g || []) : [] };
}

function drawNetCurves() {
  const v = S.viewer, f = currentFamilies();
  v.setCurves(f.a, f.b, { familyG: f.g });
  v.setFamilyVisible('A', $('famA').checked); v.setFamilyVisible('B', $('famB').checked); v.setFamilyVisible('G', $('famG').checked);
  $('famG-row').classList.toggle('hidden', !f.g.length);
  if (S.lathUtil) colourLaths(S.lathUtil);
}

function showUtilLegend(on, title = 'lath utilization · 1 = limit') {
  $('ulegend').classList.toggle('hidden', !on);
  if (on) { $('ulegend-bar').style.background = legendGradient(UTIL_STOPS); $('ulegend-title').textContent = title; }
}

function colourLaths(utils, title) {
  showUtilLegend(true, title);
  const f = currentFamilies();
  const na = f.a.length, nb = f.b.length;
  S.viewer.colorCurves(utils.slice(0, na).map(utilizationColor), utils.slice(na, na + nb).map(utilizationColor), utils.slice(na + nb).map(utilizationColor));
  if (S.selected) S.viewer.selectCurve(S.selected);
}

async function runAag() {
  if (!S.netData?.web) { $('aag-stats').textContent = 'Trace a symmetric asymptotic web first.'; $('aag-stats').classList.remove('hidden'); return; }
  const gen = S.gen;
  await withBusy('optimising the AAG web…', async () => {
    const r = await K('aag', { iterations: 16, proximity: 0.3, family: 0 });
    if (gen !== S.gen) return;
    const el = $('aag-stats'); el.classList.remove('hidden');
    if (r.error) { el.innerHTML = `<span class="bad">${r.error}</span>`; return; }
    S.aag = r; $('diagonals').checked = true;
    S.lathUtil = null;
    drawNetCurves();
    S.viewer.setMeshOpacity(Math.min(0.45, surfaceOpacity())); // the optimised nodes sit a few mm off the reference surface
    const good = r.geodesicError < 1 && r.starError < 1;
    el.innerHTML = `AAG web · ${r.iterations} iterations · ${r.ms} ms\n` +
      `geodesic diagonals <b>${fmt(r.initialGeodesicError, 1)}° → ${fmt(r.geodesicError, 2)}°</b> · asymptotic stars <b>${fmt(r.initialStarError, 2)}° → ${fmt(r.starError, 2)}°</b>\n` +
      `surface moved <b>${fmt(r.meanDeviation * toMetres() * 1000, 1)} mm</b> mean · <b>${fmt(r.maxDeviation * toMetres() * 1000, 1)} mm</b> max` +
      (good ? ' <span class="ok">✓ every slat straight and flat</span>' : ' <span class="bad">— this surface carries no AAG web close to it; try a surface of revolution or a coarser spacing</span>');
    if ($('colorutil').checked || $('solid').checked) await runLathAll();
    updateRecipe();
  });
}

// The markers in the viewport, explained next to the numbers: only the
// kinds that are actually there, plus the seed knob (which is always there
// for the seeded nets). The quality card keeps the counts.
function renderEndLegend(d) {
  const el = $('end-legend'); if (!el) return;
  const items = [];
  if (d?.endClasses?.length) {
    const counts = [0, 0, 0, 0];
    for (const c of d.endClasses) counts[c] = (counts[c] || 0) + 1;
    for (let k = 1; k < END_CLASS.length; k++) if (counts[k]) items.push(`<div><i style="background:${cssRgb(END_CLASS[k].color)}"></i><b>${counts[k]} end${counts[k] > 1 ? 's' : ''} ${END_CLASS[k].name}</b> — ${END_CLASS[k].what}</div>`);
  }
  if (seedRelevant()) items.push(`<div><i class="knob" style="background:${cssRgb(SEED_COLOR)}"></i><b>seed</b> — the green dot is where the net starts (shift-click the surface to move it)${directionRelevant() ? '; the knob on the ring sets the first direction (drag it)' : ''}</div>`);
  if ($('crossings').checked && d?.crossings) items.push('<div><i style="background:var(--ink)"></i><b>crossings</b> — where the two families meet (joints)</div>');
  el.innerHTML = items.join('');
  el.classList.toggle('hidden', items.length === 0);
}

function showEndMarkers() {
  const d = S.netData; const v = S.viewer;
  renderEndLegend(d);
  if (!d || !d.endPoints?.length) { v.clearOverlay('ends'); return; }
  const pts = [], cols = [];
  for (let i = 0; i < d.endClasses.length; i++) {
    const cls = END_CLASS[d.endClasses[i]];
    if (!cls.color) continue; // border ends need no marker
    pts.push(d.endPoints[3 * i], d.endPoints[3 * i + 1], d.endPoints[3 * i + 2]);
    cols.push(...cls.color);
  }
  v.showPoints('ends', pts, 0xffffff, 7, cols);
}

function drawHistogram(canvas, bins, color, marker = null) {
  const dpr = Math.min(2, window.devicePixelRatio || 1);
  const w = canvas.clientWidth, h = canvas.clientHeight;
  canvas.width = Math.round(w * dpr); canvas.height = Math.round(h * dpr);
  const ctx = canvas.getContext('2d'); ctx.setTransform(dpr, 0, 0, dpr, 0, 0); ctx.clearRect(0, 0, w, h);
  const max = Math.max(1, ...bins);
  const bw = w / bins.length;
  bins.forEach((b, i) => { const bh = (b / max) * (h - 6); ctx.fillStyle = color; ctx.fillRect(i * bw + 1, h - bh, bw - 2, bh); });
  if (marker != null) { ctx.strokeStyle = getComputedStyle(document.documentElement).getPropertyValue('--ink'); ctx.setLineDash([3, 2]); ctx.beginPath(); ctx.moveTo(marker * w, 0); ctx.lineTo(marker * w, h); ctx.stroke(); ctx.setLineDash([]); }
}

function drawQuality(d, closed) {
  const q = $('quality');
  const hasW = d.widths && d.widths.histogram?.length, hasA = d.angleHistogram?.length && d.crossings;
  if (!hasW && !hasA) { q.classList.add('hidden'); return; }
  q.classList.remove('hidden');
  const famColor = getComputedStyle(document.documentElement).getPropertyValue('--famA').trim() || '#0f766e';
  if (hasW) {
    $('q-width-txt').textContent = `${fmt(d.widths.min, 2)} … ${fmt(d.widths.max, 2)} · mean ${fmt(d.widths.mean / (d.resolvedSpacing || 1), 2)} × spacing · variation ${(d.widths.cv * 100).toFixed(0)} %`;
    drawHistogram($('q-width'), d.widths.histogram, famColor, 0.5);
  } else { $('q-width-txt').textContent = 'n/a'; drawHistogram($('q-width'), [0], famColor); }
  if (hasA) {
    $('q-angle-txt').textContent = `${fmt(d.crossings.minAngle, 0)}° … ${fmt(d.crossings.maxAngle, 0)}°`;
    drawHistogram($('q-angle'), d.angleHistogram, getComputedStyle(document.documentElement).getPropertyValue('--famB').trim() || '#9d2f6b');
  } else { $('q-angle-txt').textContent = 'one family'; drawHistogram($('q-angle'), [0], famColor); }
  const e = d.ends;
  const rows = [[`border`, e.border, null], [`K = 0 line`, e.regionEdge, END_CLASS[1].color], [`on a neighbour (T)`, e.onCurve, END_CLASS[2].color], [closed ? 'step limit' : 'floating', e.floating, END_CLASS[3].color], ['closed loops', e.closed, null]];
  $('q-ends').innerHTML = rows.filter((r) => r[1] > 0 || r[0] === 'border').map(([n, c, col]) => `<div>${col ? `<i style="background:${cssRgb(col)}"></i>` : '<i style="background:var(--line)"></i>'}${n}: <b>${c}</b></div>`).join('');
  $('q-build-txt').textContent = S.lathUtil ? `${Math.round(100 * S.lathUtil.filter((u) => u <= 1).length / S.lathUtil.length)} %` : '—';
  $('q-build').innerHTML = S.lathUtil ? `<i style="width:${Math.round(100 * S.lathUtil.filter((u) => u <= 1).length / S.lathUtil.length)}%"></i>` : '';
}

function showNetParams() {
  document.querySelectorAll('#net-params [data-for]').forEach((el) => {
    el.classList.toggle('hidden', !el.dataset.for.split(' ').includes(S.net));
  });
  // the symmetric web is an asymptotic layout
  document.querySelectorAll('#layouts [data-only]').forEach((b) => b.classList.toggle('hidden', !b.dataset.only.split(' ').includes(S.net)));
  const effLayout = S.net === 'asymptotic' ? S.layout : (S.layout === 3 ? 1 : S.layout);
  document.querySelectorAll('#layouts button').forEach((b) => b.classList.toggle('on', +b.dataset.layout === effLayout));
  const web = S.net === 'asymptotic' && S.layout === 3;
  $('sym-row').classList.toggle('hidden', !web);
  $('aag-row').classList.toggle('hidden', !web);
  const geo = ['geodesic', 'geodesicBoth'].includes(S.net);
  $('borderangle-row').classList.toggle('hidden', !(geo && ($('fromborder').checked || effLayout === 1)));
  // the seed row only when a seed is used; "from the border" and Jacobi angles belong to the fill
  const seedRow = $('seed-label')?.closest('.row'); if (seedRow) seedRow.classList.toggle('hidden', !seedRelevant());
  $('fromborder').closest('.chk').classList.toggle('dim', effLayout !== 0);
  $('jacobi').closest('.chk').classList.toggle('dim', effLayout !== 0);
  $('continuous-row').classList.toggle('dim', effLayout !== 0);
}

// ---------------------------------------------------------------------------
// Laths: one section for every curve
// ---------------------------------------------------------------------------

function surfaceOpacity() { return +($('opacity')?.value ?? 1); }

function lathOpts() {
  return { width: mmToModel(+$('width').value), thickness: mmToModel(+$('thick').value), upright: $('upright').checked, maxStrain: +$('strain').value / 100, section: +($('section')?.value ?? 0), align: +($('align')?.value ?? 0) };
}

async function analyseSelected() {
  const sel = S.selected;
  if (!sel) return;
  const o = lathOpts();
  const gen = S.gen;
  const upright = sel.family === 'G' ? false : o.upright;
  const L = await K('lath', sel.points.flat(), o.width, o.thickness, upright, o.maxStrain, false, o.section);
  if (gen !== S.gen || S.selected !== sel) return;
  S.lath = L;
  $('lath-result').classList.remove('hidden');
  const mk = (arr) => { let m = 0; for (const x of arr) if (Number.isFinite(x)) m = Math.max(m, Math.abs(x)); return m; };
  const m = toMetres();
  $('lath-stats').innerHTML = `family ${sel.family} #${sel.index} (${upright ? 'upright' : 'flat'}) · length <b>${fmt(L.length * m, 2)} m</b> · flat <b>${fmt(L.flatLength * m, 2)} m</b> · bow <b>${fmt(L.bow * m * 1000, 0)} mm</b>\n` +
    `max |kn| <b>${fmt(mk(L.kn) / m, 3)}</b> · max |kg| <b>${fmt(mk(L.kg) / m, 3)}</b> · max |tg| <b>${fmt(mk(L.tg) / m, 3)}</b> 1/m · peak utilization <b class="${L.buildable ? 'ok' : 'bad'}">${fmt(L.maxUtilization, 2)} ${L.buildable ? '✓ buildable' : '✗ over the strain limit'}</b>`;
  drawLathPlot($('plot'), L);
  drawUnroll($('unroll'), L);
  updateRecipe();
}

async function runLathAll() {
  if (!S.netData) return;
  const o = lathOpts();
  const gen = S.gen, data = S.netData, aag = S.aag;
  const sweep = $('solid').checked;
  let r;
  await withBusy(sweep ? 'sweeping every lath…' : 'checking every lath…', async () => {
    r = await K('lathAll', o.width, o.thickness, o.upright, o.maxStrain, o.section, sweep, o.align);
  });
  if (!r || gen !== S.gen || S.netData !== data || S.aag !== aag) return;
  S.lathUtil = r.utilization;
  if ($('colorutil').checked) colourLaths(r.utilization, 'lath strain utilization · 1 = limit'); else { S.viewer.colorCurves(null, null, null); showUtilLegend(false); }
  if (sweep && r.sweepVertices.length) S.viewer.showSolid('sweepAll', r.sweepVertices, r.sweepFaces, S.viewer.dark ? 0xe6e6e6 : 0x1a1a1a); else S.viewer.clearOverlay('sweepAll');
  const n = r.utilization.length, m = toMetres();
  $('laths-stats').innerHTML = `<b>${n}</b> laths · total <b>${fmt(r.totalLength * m, 1)} m</b> of ${$('width').value} × ${$('thick').value} mm · buildable <b class="${r.buildable === n ? 'ok' : 'bad'}">${r.buildable} / ${n}</b> · peak utilization <b>${fmt(r.maxUtilization, 2)}</b> · ${r.ms} ms`;
  drawQuality(data, S.stats.boundaryVertices === 0);
}

// ---------------------------------------------------------------------------
// Structure
// ---------------------------------------------------------------------------

function supportCandidates() {
  // lath ends and crossings of the current net
  const f = currentFamilies(), pts = [];
  for (const l of [...f.a, ...f.b, ...f.g]) { if (l.length) { pts.push(l[0], l[l.length - 1]); } }
  const x = S.netData?.crossings?.points; if (x) for (let i = 0; i < x.length; i += 3) pts.push([x[i], x[i + 1], x[i + 2]]);
  const w = S.netData?.web?.nodePoints; if (w) for (let i = 0; i < w.length; i += 3) pts.push([w[i], w[i + 1], w[i + 2]]);
  return pts;
}

function togglePicked(p) {
  const cands = supportCandidates();
  let best = null, bd = Infinity;
  for (const c of cands) { const d = Math.hypot(c[0] - p[0], c[1] - p[1], c[2] - p[2]); if (d < bd) { bd = d; best = c; } }
  if (!best || bd > 0.06 * S.size) return;
  const i = S.picked.findIndex((q) => Math.hypot(q[0] - best[0], q[1] - best[1], q[2] - best[2]) < 1e-9);
  if (i >= 0) S.picked.splice(i, 1); else S.picked.push(best);
  showPicked();
}

function showPicked() {
  $('pick-count').textContent = S.picked.length ? `${S.picked.length} picked` : '';
  if (S.picked.length) S.viewer.showPoints('picked', S.picked.flat(), NEON.green, 12); else S.viewer.clearOverlay('picked');
}

function frameOptions() {
  const o = lathOpts();
  const jsel = $('joints').value;
  return {
    width: o.width, thickness: o.thickness, shape: o.section, toMetres: toMetres(),
    lineLoad: +$('load').value * 1000, areaLoad: +$('area').value * 1000, density: +$('material').value,
    supports: $('supports').value, lowestBand: +$('band').value / 100, picked: S.picked.flat(),
    supportType: +$('suptype').value, jointStiffness: jsel === 'k' ? +$('jk').value * 1000 : +jsel,
  };
}

async function runFrame() {
  if (!S.netData) { $('frame-stats').textContent = 'Trace a net first.'; return; }
  await withBusy('solving frame…', async () => {
    const opts = frameOptions();
    const gen = S.gen;
    const r = await K('frame', opts);
    if (gen !== S.gen) return;
    S.frame = r;
    if (r.error) { $('frame-stats').innerHTML = `<span class="bad">${r.error}</span>`; S.viewer.clearOverlay('supports'); S.viewer.clearOverlay('deformed'); return; }
    const jointTxt = opts.jointStiffness < 0 ? 'rigid' : opts.jointStiffness === 0 ? 'hinged' : `k = ${fmt(opts.jointStiffness / 1000, 1)} kN·m/rad`;
    $('frame-stats').innerHTML = `nodes <b>${r.nodes}</b> · joints <b>${r.joints}</b> (${jointTxt}) · elements <b>${r.elements}</b> · supports <b>${r.supports}</b> (${opts.supportType ? 'pinned' : 'fixed'}) · ${r.ms} ms\n` +
      `load <b>${fmt(r.totalLoad / 1000, 2)} kN</b> = reactions <b>${fmt(r.reactionSum / 1000, 2)} kN</b> (equilibrium ${r.equilibriumError.toExponential(0)})\n` +
      `max deflection <b>${(r.maxDisplacement * 1000).toFixed(1)} mm</b> · peak utilization <b class="${r.maxUtilization <= 1 ? 'ok' : 'bad'}">${fmt(r.maxUtilization, 2)}</b> · max torsion <b>${fmt(r.maxTorsion, 1)} N·m</b>` +
      (r.floatingLaths ? `\n<span class="bad">${r.floatingLaths} lath${r.floatingLaths > 1 ? 's' : ''} reach no support through the net and were left out</span>` : '');
    S.viewer.showPoints('supports', r.supportPoints, S.viewer.ink(), 10);
    colourLaths(r.lathUtilization, 'frame stress utilization · 1 = allowable');
    // deformed at a readable scale: largest displacement drawn as 5 % of the size
    if (r.maxDisplacement > 0) { const k = 0.05 * S.size / (r.maxDisplacement / toMetres()); $('defscale').value = k >= 10 ? Math.round(k) : +k.toPrecision(2); }
    showDeformed();
    updateRecipe();
  });
}

function showDeformed() {
  const v = S.viewer;
  if (!S.frame || S.frame.error || !$('deformed').checked) { v.clearOverlay('deformed'); return; }
  const k = +$('defscale').value || 1;
  const curves = S.frame.deformed.map((pts) => pts.map((q) => [q[0] + k * q[3], q[1] + k * q[4], q[2] + k * q[5]]));
  v.showPolylines('deformed', curves, v.ink(), 1.1, false);
}

// ---------------------------------------------------------------------------
// Kinetics
// ---------------------------------------------------------------------------

function stopKinetics(clear = false) {
  if (S.kinTimer) { clearInterval(S.kinTimer); S.kinTimer = null; $('kplay').textContent = 'play'; }
  if (clear && S.kin) { S.kin = null; $('kin-result').classList.add('hidden'); S.viewer.setMeshOpacity(surfaceOpacity()); }
}

async function runKinetics() {
  if (!S.netData || S.net !== 'asymptotic') { $('kin-note').textContent = 'Kinetics needs an asymptotic net (two families): pick Asymptotic in the Net block.'; return; }
  stopKinetics();
  const o = lathOpts();
  const opts = { drive: S.drive, amplitude: +$('kamp').value, stiffness: +$('kstiff').value, steps: +$('ksteps').value, width: o.width, thickness: o.thickness, maxStrain: o.maxStrain, toMetres: toMetres() };
  const gen = S.gen;
  await withBusy('moving the mechanism…', async () => {
    const r = await K('kinetics', opts);
    if (gen !== S.gen) return;
    if (r.error) { $('kin-note').innerHTML = `<span class="bad">${r.error}</span>`; return; }
    S.kin = r;
    $('kin-result').classList.remove('hidden');
    $('kfold').max = r.states.length - 1; $('kfold').value = r.states.length - 1;
    showKinState(r.states.length - 1);
    drawKinPlot();
  });
}

function showKinState(k) {
  const r = S.kin; if (!r) return;
  const st = r.states[Math.max(0, Math.min(r.states.length - 1, k))];
  S.viewer.setMeshOpacity(k === 0 ? surfaceOpacity() : Math.min(0.18, surfaceOpacity()));
  S.viewer.setCurves(st.a, st.b);
  if ($('colorutil').checked && S.kin.states.some((x) => x.utilization > 0)) {} // per-state colouring is per net only
  $('kfold').nextElementSibling.value = st.fold.toFixed(2);
  const m = toMetres();
  $('kin-stats').innerHTML = `${r.nodes} nodes · ${r.joints} joints · ${r.laths} laths · ${r.drivers} driven · ${r.ms} ms\n` +
    `fold <b>${st.fold.toFixed(2)}</b> · height <b>${fmt(st.height * m, 2)} m</b> · span <b>${fmt(st.span * m, 2)} m</b> · crossing <b>${fmt(st.minAngle, 0)}° … ${fmt(st.maxAngle, 0)}°</b>\n` +
    `joint drift <b>${st.drift.toExponential(1)}</b> · off asymptotic <b>${fmt(st.asymptotic, 2)}°</b> · driver miss <b>${fmt(st.miss * m * 1000, 0)} mm</b>` +
    (st.utilization > 0 ? ` · strain utilization <b class="${st.utilization <= 1 ? 'ok' : 'bad'}">${fmt(st.utilization, 2)}</b>` : '') + (r.natural === k ? ' · <b>natural state</b> (least energy)' : '') + (st.converged ? '' : ' · <span class="bad">not converged</span>');
}

function drawKinPlot() {
  const r = S.kin, c = $('kin-plot'); if (!r) return;
  const dpr = Math.min(2, window.devicePixelRatio || 1);
  const w = c.clientWidth, h = c.clientHeight;
  c.width = Math.round(w * dpr); c.height = Math.round(h * dpr);
  const ctx = c.getContext('2d'); ctx.setTransform(dpr, 0, 0, dpr, 0, 0); ctx.clearRect(0, 0, w, h);
  const css = (n) => getComputedStyle(document.documentElement).getPropertyValue(n).trim();
  const n = r.states.length, X = (i) => 8 + (i / Math.max(1, n - 1)) * (w - 16);
  const series = [
    [r.states.map((s) => s.height), css('--ink')],
    [r.states.map((s) => s.minAngle), css('--famA')],
    [r.states.map((s) => s.utilization), css('--famB')],
  ];
  for (const [vals, col] of series) {
    const lo = Math.min(...vals), hi = Math.max(...vals), span = Math.max(1e-12, hi - lo);
    ctx.strokeStyle = col; ctx.lineWidth = 1.8; ctx.beginPath();
    vals.forEach((v, i) => { const y = h - 8 - ((v - lo) / span) * (h - 16); if (i) ctx.lineTo(X(i), y); else ctx.moveTo(X(i), y); });
    ctx.stroke();
  }
  const k = +$('kfold').value;
  ctx.strokeStyle = css('--muted'); ctx.setLineDash([3, 2]); ctx.beginPath(); ctx.moveTo(X(k), 0); ctx.lineTo(X(k), h); ctx.stroke(); ctx.setLineDash([]);
}

// ---------------------------------------------------------------------------
// Recipe
// ---------------------------------------------------------------------------

function updateRecipe() {
  const lines = ['Mite for Grasshopper — reproduce this view', `mesh: ${SHAPES[S.shape].name} (${SHAPES[S.shape].formula.join(' ')})` + (S.shape !== 'loft' && S.shape !== 'file' ? ` (${SHAPES[S.shape].params.map((p, i) => `${p[0]} = ${S.params[i]}`).join(', ')})` : '')];
  if (S.mode !== 'shaded' && S.mode !== 'zebra') {
    const comp = { K: 'Gaussian Curvature → K', H: 'Mean Curvature → H', k1: 'Principal Curvature → K1', k2: 'Principal Curvature → K2', radius: 'Principal Curvature → K1, K2 (1/max)', anticlastic: 'Asymptotic Net → Anticlastic (K)' }[S.mode];
    lines.push(`analysis: ${comp} → Mesh Colour Map (Radius = ${S.smooth})`);
  }
  if (S.net !== 'none') {
    const o = netOptions();
    const comp = { asymptotic: 'Asymptotic Net', conjugate: 'Conjugate Net', geodesic: 'Geodesic Net', geodesicBoth: 'Geodesic Net ×2', streamMax: 'Curvature Streamlines (MaxDir = True)', streamMin: 'Curvature Streamlines (MaxDir = False)', chebyshev: 'Chebyshev Net', isocurves: 'Mesh Isocurves' }[S.net];
    let p = `${comp}: `;
    if (S.net === 'chebyshev') p += `L = ${fmt(o.edgeLength)}, Count = ${o.count}, Angle = ${o.angleDeg}°`;
    else if (S.net === 'isocurves') p += `field ${o.field}, ${o.levels} levels`;
    else if (o.layout === 3) p = `Asymptotic Web: Spacing = ${fmt(o.spacing)}, Symmetry = ${o.symmetry} (−1 detect), Seed = ${S.seed >= 0 ? S.seed : 'auto'}` + (S.aag ? ' → AAG Web (Iterations 16, Proximity 0.3)' : $('diagonals').checked ? ' → Diagonals (G)' : '');
    else p += `AutoSpace = True, Spacing = ${fmt(o.spacing)}, Layout = ${o.layout} (${["evenly spaced fill", "web from the border", "web from the seed cross"][o.layout]}), Continuous = ${o.continuous}, Step = 0 (auto)`;
    if (S.net === 'asymptotic' && o.layout !== 3) p += `, MinAngle = ${o.minAngle}`;
    if (S.seed >= 0) p += `, Seed = ${S.seed}`;
    if (directionRelevant()) p += `, Direction = (${o.direction.map((x) => x.toFixed(2)).join(', ')})`;
    if ((S.net === 'geodesic' || S.net === 'geodesicBoth') && o.fromBorder) p += `, FromBorder = True, BorderAngle = ${o.borderAngle}`;
    lines.push(p);
  }
  if (S.lathUtil || S.lath) {
    const o = lathOpts();
    lines.push(`every lath (model units, span ${$('span').value} m): Lath Analysis / Lath Sweep: Width = ${fmt(o.width, 4)} (${$('width').value} mm), Thickness = ${fmt(o.thickness, 4)} (${$('thick').value} mm), Upright = ${o.upright} (G flat), MaxStrain = ${o.maxStrain}, Shape = ${o.section}`);
    lines.push('Lath Unroll: same Width / Upright → Patterns');
  }
  if (S.frame && !S.frame.error) {
    const f = frameOptions();
    lines.push(`Gridshell Analysis: Supports = ${f.supports === 'border' ? 'lath ends on the border' : f.supports === 'lowest' ? `lath ends in the lowest ${$('band').value} %` : `${S.picked.length} picked points`}, SupportType = ${f.supportType}, JointStiffness = ${f.jointStiffness < 0 ? -1 : f.jointStiffness} N·m/rad, Density = ${f.density}, AreaLoad = ${f.areaLoad} N/m², Load = (0,0,−${f.lineLoad}) N/m`);
  }
  if (S.kin) lines.push(`Net Kinetics: drive ${S.drive}, amplitude ${$('kamp').value}, Stiffness = ${$('kstiff').value}, Steps = ${$('ksteps').value}`);
  $('recipe').textContent = lines.join('\n');
}

// ---------------------------------------------------------------------------
// Boot
// ---------------------------------------------------------------------------

function bootProgress(n, name) {
  $('boot-bar').style.width = Math.min(100, (n / 18) * 100) + '%';
  $('boot-text').textContent = name;
}

async function main() {
  const dark = matchMedia('(prefers-color-scheme: dark)').matches;
  if (dark) document.documentElement.dataset.theme = 'dark';
  $('theme').addEventListener('click', () => {
    const d = document.documentElement.dataset.theme === 'dark';
    document.documentElement.dataset.theme = d ? 'light' : 'dark';
    S.viewer?.setTheme(!d);
    if (S.lath) { drawLathPlot($('plot'), S.lath); drawUnroll($('unroll'), S.lath); }
    if (S.netData) drawQuality(S.netData, S.stats.boundaryVertices === 0);
    if (S.kin) drawKinPlot();
    if (S.frame && !S.frame.error) { S.viewer.showPoints('supports', S.frame.supportPoints, S.viewer.ink(), 10); showDeformed(); }
  });

  S.viewer = new Viewer($('gl'));
  S.viewer.setTheme(dark);
  buildShapeUI();
  showNetParams();

  S.kernel = new Kernel();
  const info = await S.kernel.boot(bootProgress);
  $('version').textContent = info.version + ' · wasm worker · boot ' + info.bootMs + ' ms';
  $('boot').classList.add('hidden');

  // cancel: kill the worker and reboot, then reload the current shape
  $('cancel').addEventListener('click', async () => {
    busy(true, 'restarting kernel…');
    try { await S.kernel.restart(); S.busy = 1; await applyShape(false); } finally { busy(false); }
  });

  // viewer interactions
  S.viewer.onPickCurve = (o) => {
    if (S.picking) return;
    S.selected = o;
    S.viewer.selectCurve(o);
    if (o) analyseSelected(); else { $('lath-result').classList.add('hidden'); S.viewer.clearOverlay('sweep'); }
  };
  S.viewer.onHover = (o) => {
    if (!o) { $('hud-hover').textContent = ''; return; }
    const pts = o.points; let L = 0; for (let i = 1; i < pts.length; i++) L += Math.hypot(pts[i][0] - pts[i - 1][0], pts[i][1] - pts[i - 1][1], pts[i][2] - pts[i - 1][2]);
    const f = currentFamilies();
    const u = S.lathUtil?.[(o.family === 'A' ? 0 : o.family === 'B' ? f.a.length : f.a.length + f.b.length) + o.index];
    $('hud-hover').textContent = `family ${o.family} #${o.index} · length ${fmt(L, 3)}` + (u != null ? ` · utilization ${fmt(u, 2)}` : '') + ' · click for the lath';
  };
  S.viewer.onPickVertex = async (p, shift) => {
    if (S.picking && !shift) { togglePicked(p); return; }
    if (!shift) return;
    S.seed = await K('nearestVertex', p[0], p[1], p[2]);
    await updateSeedHandle();
    if (S.net !== 'none') traceNet();
  };
  $('seed-clear').addEventListener('click', async () => { S.seed = -1; await updateSeedHandle(); if (S.net !== 'none') traceNet(); });
  S.viewer.onHandleDrag = (id, p) => { S.loft.setControl(id, p); loadShape({ light: true }); };
  S.viewer.onHandleDragEnd = () => { if (S.net !== 'none') scheduleNet(); };
  S.viewer.onDirectionDrag = (d) => { S.dir = d; };
  S.viewer.onDirectionDragEnd = () => { if (S.net !== 'none') traceNet(); };
  document.querySelectorAll('.cam button').forEach((b) => b.addEventListener('click', () => S.viewer.view(b.dataset.view)));

  // analysis
  $('modes').addEventListener('click', (e) => {
    const b = e.target.closest('button'); if (!b) return;
    $('modes').querySelectorAll('button').forEach((x) => x.classList.toggle('on', x === b));
    S.mode = b.dataset.mode; applyMode();
  });
  $('smooth').addEventListener('input', (e) => { e.target.nextElementSibling.value = e.target.value; });
  $('smooth').addEventListener('change', (e) => { S.smooth = +e.target.value; S.curv = null; applyMode(); });
  for (const id of ['dirs', 'umb', 'wire']) $(id).addEventListener('change', applyMode);
  $('opacity').addEventListener('input', (e) => { e.target.nextElementSibling.value = e.target.value; if (!S.kin || +$('kfold').value === 0) S.viewer.setMeshOpacity(surfaceOpacity()); });

  // nets
  $('nets').addEventListener('click', (e) => {
    const b = e.target.closest('button'); if (!b) return;
    $('nets').querySelectorAll('button').forEach((x) => x.classList.toggle('on', x === b));
    S.net = b.dataset.net; showNetParams();
    $('upright').checked = S.net === 'asymptotic'; // asymptotic laths stand upright, geodesic laths lie flat
    traceNet();
  });
  $('layouts').addEventListener('click', (e) => {
    const b = e.target.closest('button'); if (!b) return;
    $('layouts').querySelectorAll('button').forEach((x) => x.classList.toggle('on', x === b));
    S.layout = +b.dataset.layout;
    showNetParams();
    if (S.net !== 'none') traceNet();
  });
  document.querySelectorAll('#net-params input[type=range]').forEach((inp) => {
    inp.addEventListener('input', () => { if (inp.nextElementSibling?.tagName === 'OUTPUT' && !inp.nextElementSibling.id) inp.nextElementSibling.value = inp.value; updateSpacingReadout(); });
    inp.addEventListener('change', () => { if (S.net !== 'none') traceNet(); });
  });
  document.querySelectorAll('.presets [data-sp]').forEach((b) => b.addEventListener('click', () => { $('spacing').value = b.dataset.sp; updateSpacingReadout(); if (S.net !== 'none') traceNet(); }));
  $('isofield').addEventListener('change', () => { if (S.net === 'isocurves') traceNet(); });
  for (const id of ['continuous', 'jacobi']) $(id).addEventListener('change', () => { if (S.net !== 'none') traceNet(); });
  $('fromborder').addEventListener('change', () => { showNetParams(); if (S.net !== 'none') traceNet(); });
  $('crossings').addEventListener('change', () => {
    if (S.netData?.crossings && $('crossings').checked) S.viewer.showPoints('crossings', S.netData.crossings.points, S.viewer.ink(), 3.5); else S.viewer.clearOverlay('crossings');
  });
  $('famA').addEventListener('change', () => S.viewer.setFamilyVisible('A', $('famA').checked));
  $('famB').addEventListener('change', () => S.viewer.setFamilyVisible('B', $('famB').checked));

  // laths
  document.querySelectorAll('#lath-block input[type=range]').forEach((inp) => {
    inp.addEventListener('input', () => { inp.nextElementSibling.value = inp.value; });
    inp.addEventListener('change', () => { if (S.selected) analyseSelected(); if ($('colorutil').checked || $('solid').checked) runLathAll(); });
  });
  for (const id of ['upright', 'section', 'align']) $(id).addEventListener('change', () => { if (S.selected) analyseSelected(); if ($('colorutil').checked || $('solid').checked) runLathAll(); });
  $('solid').addEventListener('change', () => { if ($('solid').checked || $('colorutil').checked) runLathAll(); else S.viewer.clearOverlay('sweepAll'); });
  $('colorutil').addEventListener('change', () => { if ($('colorutil').checked) runLathAll(); else { S.lathUtil = null; S.viewer.colorCurves(null, null, null); showUtilLegend(false); if (S.selected) S.viewer.selectCurve(S.selected); if (S.netData) drawQuality(S.netData, S.stats.boundaryVertices === 0); } });
  $('span').addEventListener('input', (e) => { e.target.nextElementSibling.value = e.target.value; });
  $('span').addEventListener('change', () => { if (S.netData) { traceNetStatsOnly(); if ($('colorutil').checked || $('solid').checked) runLathAll(); } });

  // web / AAG
  $('diagonals').addEventListener('change', async () => {
    if (!S.netData?.web) return;
    if (!$('diagonals').checked) S.aag = null;
    await K('useDiagonals', $('diagonals').checked && !S.aag);
    S.lathUtil = null; drawNetCurves();
    if ($('colorutil').checked || $('solid').checked) runLathAll();
  });
  $('aag').addEventListener('click', runAag);
  $('symmetry').addEventListener('change', () => { if (S.net === 'asymptotic') traceNet(); });
  $('famG').addEventListener('change', () => S.viewer.setFamilyVisible('G', $('famG').checked));

  // structure
  $('frame').addEventListener('click', runFrame);
  $('deformed').addEventListener('change', showDeformed);
  $('defscale').addEventListener('change', showDeformed);
  for (const id of ['load', 'area', 'band', 'jk']) $(id).addEventListener('input', (e) => { e.target.nextElementSibling.value = e.target.value; });
  const structRows = () => {
    $('band-row').classList.toggle('hidden', $('supports').value !== 'lowest');
    $('pick-row').classList.toggle('hidden', $('supports').value !== 'picked');
    $('jk-row').classList.toggle('hidden', $('joints').value !== 'k');
    if ($('supports').value !== 'picked' && S.picking) { S.picking = false; $('pick').classList.remove('on'); }
  };
  $('supports').addEventListener('change', structRows);
  $('joints').addEventListener('change', structRows);
  structRows();
  $('pick').addEventListener('click', () => { S.picking = !S.picking; $('pick').classList.toggle('on', S.picking); $('pick').textContent = S.picking ? 'picking… (click again to stop)' : 'pick supports'; showPicked(); });
  $('pick-clear').addEventListener('click', () => { S.picked = []; showPicked(); });

  // kinetics
  $('drives').addEventListener('click', (e) => {
    const b = e.target.closest('button'); if (!b) return;
    $('drives').querySelectorAll('button').forEach((x) => x.classList.toggle('on', x === b));
    S.drive = b.dataset.drive;
    // each drive has its own natural amplitude: 1 = completely flat for the press, ±0.35 for the others
    const amp = S.drive === 'flatten' ? 1 : 0.35;
    $('kamp').value = amp; $('kamp').nextElementSibling.value = amp;
  });
  for (const id of ['kamp', 'kstiff', 'ksteps']) $(id).addEventListener('input', (e) => { e.target.nextElementSibling.value = e.target.value; });
  $('kinrun').addEventListener('click', runKinetics);
  $('kfold').addEventListener('input', () => { stopKinetics(); showKinState(+$('kfold').value); drawKinPlot(); });
  $('kplay').addEventListener('click', () => {
    if (S.kinTimer) { stopKinetics(); return; }
    if (!S.kin) return;
    let k = +$('kfold').value, dir = 1;
    $('kplay').textContent = 'pause';
    S.kinTimer = setInterval(() => {
      const n = S.kin.states.length;
      k += dir; if (k >= n - 1) { k = n - 1; dir = -1; } else if (k <= 0) { k = 0; dir = 1; }
      $('kfold').value = k; showKinState(k); drawKinPlot();
    }, 140);
  });

  // file
  const loadFile = async (file) => {
    try {
      const g = await readMeshFile(file);
      S.file = g; S.seed = -1;
      S.shape = 'file'; renderShapeParams();
      await loadShape();
    } catch (err) { alert(err.message); }
  };
  $('file').addEventListener('change', (e) => { if (e.target.files[0]) loadFile(e.target.files[0]); });
  $('weld').addEventListener('change', () => { if (S.shape === 'file') loadShape(); });
  let dragDepth = 0;
  document.addEventListener('dragenter', (e) => { e.preventDefault(); dragDepth++; $('drop').classList.remove('hidden'); });
  document.addEventListener('dragleave', () => { if (--dragDepth <= 0) { dragDepth = 0; $('drop').classList.add('hidden'); } });
  document.addEventListener('dragover', (e) => e.preventDefault());
  document.addEventListener('drop', (e) => { e.preventDefault(); dragDepth = 0; $('drop').classList.add('hidden'); if (e.dataTransfer.files[0]) loadFile(e.dataTransfer.files[0]); });

  // export
  $('dl-mesh').addEventListener('click', () => save.text(`mite-${S.shape}.obj`, meshToOBJ(S.vertices, S.triangles, S.shape)));
  $('dl-curves').addEventListener('click', () => { if (S.netData) save.text(`mite-${S.net}.obj`, curvesToOBJ(S.netData.a, S.netData.b)); });
  $('dl-pattern').addEventListener('click', () => { if (S.lath?.unrollA?.length) save.text('mite-lath-pattern.svg', unrollToSVG(S.lath), 'image/svg+xml'); });
  $('dl-png').addEventListener('click', () => save.png('mite-view.png', S.viewer.screenshot()));
  $('dl-recipe').addEventListener('click', () => { updateRecipe(); $('recipe').classList.toggle('hidden'); });

  window.addEventListener('keydown', (e) => { if (e.key === 'f' && !e.target.closest('input,select,textarea')) S.viewer.view('iso'); });

  // Default net: select it before the first shape loads so the load itself
  // traces it (with the loaded mesh's size) instead of a second, racing call.
  const first = $('nets').querySelector('[data-net=asymptotic]');
  $('nets').querySelectorAll('button').forEach((x) => x.classList.toggle('on', x === first));
  S.net = 'asymptotic'; showNetParams(); $('upright').checked = true;
  $('band-row').classList.add('hidden');
  window.__mite = S; // for tests
  window.__miteSelectShape = (k) => selectShape(k);
  await loadShape();
}

main().catch((e) => { console.error(e); $('boot-text').textContent = 'Failed to start: ' + (e.message || e); });
