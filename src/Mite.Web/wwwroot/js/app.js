// Mite interactive: wires the panel to the kernel (Mite.Core on WebAssembly,
// in a Web Worker) and the three.js viewer.

import { Kernel } from './kernel.js';
import { Viewer, FAMILY_A, FAMILY_B } from './viewer.js';
import { diverging, sequential, utilizationColor, legendGradient, cssRgb } from './colormaps.js';
import { Loft } from './loft.js';
import { readMeshFile } from './loaders.js';
import { drawLathPlot, drawUnroll } from './plots.js';
import { meshToOBJ, curvesToOBJ, unrollToSVG, save } from './export.js';

const $ = (id) => document.getElementById(id);

// ---------------------------------------------------------------------------
// Catalogue
// ---------------------------------------------------------------------------

const SHAPES = {
  saddle: { label: 'Saddle  z = a·x² − b·y²', params: [['size', 1, 4, 2, 0.1], ['a', 0.2, 2, 1, 0.05], ['b', 0.2, 2, 1, 0.05]],
    note: 'K < 0 everywhere. The asymptotic curves are the straight lines x = ±y·√(b/a) + c, so every asymptotic lath is straight and runs border to border.' },
  hyperboloid: { label: 'Hyperboloid of one sheet', params: [['a (waist)', 0.4, 2, 1, 0.05], ['c', 0.4, 2, 1, 0.05], ['height', 0.4, 1.5, 1, 0.05]],
    note: 'A ruled surface: its asymptotic curves are exactly the two families of straight rulings — the sharpest test of the asymptotic tracer (kn = 0, twist only).' },
  monkey: { label: 'Monkey saddle  z = k(x³ − 3xy²)', params: [['size', 1, 4, 2, 0.1], ['k', 0.2, 2, 1, 0.05]],
    note: 'A flat umbilic at the origin with three asymptotic directions: families must swap cleanly around it.' },
  catenoid: { label: 'Catenoid (H = 0)', params: [['c', 0.5, 2, 1, 0.05], ['height', 0.4, 2, 1.2, 0.05]],
    note: 'Minimal surface: H = 0 so k1 = −k2 and the asymptotic families cross at exactly 90°, the ideal asymptotic gridshell.' },
  enneper: { label: 'Enneper patch (H = 0)', params: [['extent', 0.5, 1.6, 1.2, 0.05]],
    note: 'Minimal surface with a self-intersecting far field; keep the extent below ~1.4 for a clean patch.' },
  enneper3: { label: 'Enneper, n-fold (H = 0)', params: [['folds', 2, 5, 3, 1], ['radius', 0.4, 1.2, 0.9, 0.05]],
    note: 'Higher-order Enneper surfaces (Weierstrass g = wⁿ⁻¹). The centre is a flat point where the asymptotic cross turns by 180° per loop, so with 3 or more folds the two families are one family globally and some laths end on their neighbours as T-junctions.' },
  ruled: { label: 'Ruled patch (bilinear)', params: [['size', 1, 4, 2, 0.1], ['twist', 0.1, 2, 0.8, 0.05], ['skew', 0, 1, 0.3, 0.05]],
    note: 'A skew quad spanned bilinearly: doubly ruled, and the two families of straight iso-lines are exactly its asymptotic curves — every asymptotic lath comes out straight.' },
  schwarzd: { label: 'Schwarz D patch (TPMS)', params: [['extent', 0.5, 2, 1, 0.1], ['cells', 12, 40, 24, 2], ['relax', 0, 60, 30, 5]],
    note: 'The diamond surface of Schling\'s asymptotic pavilion: cut from its nodal approximation and relaxed to a soap film (H ≈ 0), so the asymptotic families cross at ~90°. Slower at high cell counts.' },
  gyroid: { label: 'Gyroid patch (TPMS)', params: [['extent', 0.5, 2, 1, 0.1], ['cells', 12, 40, 24, 2], ['relax', 0, 60, 30, 5]],
    note: 'Gyroid patch from its nodal approximation, relaxed to H ≈ 0. Like the Schwarz D, a minimal surface with an asymptotic net of nearly right angles.' },
  wave: { label: 'Wave  z = A·sin(fx)·cos(fy)', params: [['size', 1, 4, 2, 0.1], ['amplitude', 0.05, 0.8, 0.3, 0.01], ['frequency', 0.5, 4, 2, 0.1]],
    note: 'Mixed curvature: elliptic caps and anticlastic saddles between them, separated by K = 0 lines where asymptotic curves fade out.' },
  sphere: { label: 'Sphere', params: [['radius', 0.5, 2, 1, 0.05]],
    note: 'k1 = k2 = 1/r everywhere: every point is an umbilic, so principal directions and curvature lines are undefined; geodesics are great circles.' },
  dome: { label: 'Dome cap', params: [['radius', 0.5, 2, 1, 0.05], ['half-angle °', 20, 90, 60, 5]],
    note: 'K > 0: no asymptotic curves. Geodesic nets and Chebyshev nets are the layouts that apply; geodesics converge towards the top, which Jacobi seeding compensates.' },
  ellipsoid: { label: 'Ellipsoid', params: [['a', 0.5, 2, 1.5, 0.05], ['b', 0.5, 2, 1, 0.05], ['c', 0.3, 2, 0.7, 0.05]],
    note: 'Exactly four umbilics on the a–c section; curvature lines form the classic lemon pattern around them.' },
  torus: { label: 'Torus', params: [['R', 1.5, 4, 3, 0.1], ['r', 0.3, 1.4, 1, 0.05]],
    note: 'K > 0 outside, K < 0 inside, K = 0 on the top and bottom circles. Curvature lines are meridians and parallels; asymptotic curves live on the inner half only.' },
  vault: { label: 'Barrel vault (K = 0)', params: [['R', 0.8, 3, 1.5, 0.05], ['width', 1, 4, 2, 0.1], ['length', 1, 6, 4, 0.1]],
    note: 'Developable: k2 = 0 along the rulings. Curvature lines are rulings and arcs; geodesics are helices — try "from the border" with a border angle.' },
  cone: { label: 'Cone (K = 0)', params: [['radius', 0.5, 2, 1, 0.05], ['height', 0.5, 3, 1.5, 0.05], ['top radius', 0.05, 0.9, 0.15, 0.01]],
    note: 'Developable with a near-singular apex: a projection and tracing stress test.' },
  annulus: { label: 'Annular saddle', params: [['inner r', 0.1, 0.9, 0.4, 0.05], ['outer r', 1, 2, 1.2, 0.05], ['k', 0.2, 2, 1, 0.05]],
    note: 'An inner border: curves must end cleanly on both boundaries.' },
  loft: { label: 'Free-form loft — drag the points', params: [], note: 'Drag the control points in the viewport; the surface, its curvature and the current net follow.' },
  file: { label: 'Your mesh (.obj / .stl / .ply)', params: [], note: 'Export from Rhino with _Export (OBJ, weld on) or drop a Weaverbird mesh; n-gons are triangulated and vertices welded here.' },
};

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
  asymptotic: 'Both families of asymptotic curves (zero normal curvature): laths bend only about their weak axis and twist — stand them upright. Only where K < 0 and the families cross widely enough (min crossing).',
  conjugate: 'The two principal curvature line families: an approximate conjugate net, the layout for planar-quad panels. Undefined at umbilics.',
  geodesic: 'One family of straightest geodesics grown sideways from the seed. Each new geodesic starts at the angle that keeps its strip closest to constant width (Jacobi field); flat laths follow geodesics without in-plane bending.',
  geodesicBoth: 'Two geodesic families crossing at the family angle at the seed, each grown with Jacobi start angles.',
  streamMax: 'Lines of maximum principal curvature, evenly spaced.',
  streamMin: 'Lines of minimum principal curvature, evenly spaced.',
  chebyshev: 'Compass-method Chebyshev net: every edge has the same length — the flat-lattice kinematics of an elastic gridshell. The angles collapse where the net locks.',
  isocurves: 'Level curves of a scalar field on the mesh (marching triangles).',
};

const END_CLASS = [
  { name: 'on the border', color: null },
  { name: 'at the K = 0 line', color: [0.26, 0.45, 0.80] },
  { name: 'on a neighbour (T-junction)', color: [0.96, 0.62, 0.05] },
  { name: 'step limit (closed surface)', color: [0.80, 0.15, 0.15] },
];

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
  frame: null,
  busy: 0, gen: 0, loading: false,
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
const K = (method, ...args) => S.kernel.call(method, ...args);

// ---------------------------------------------------------------------------
// Shape
// ---------------------------------------------------------------------------

function buildShapeUI() {
  const sel = $('shape');
  sel.innerHTML = Object.entries(SHAPES).map(([k, v]) => `<option value="${k}">${v.label}</option>`).join('');
  sel.value = S.shape;
  sel.addEventListener('change', () => { S.shape = sel.value; S.seed = -1; renderShapeParams(); loadShape(); });
  $('res').addEventListener('input', (e) => { $('res').nextElementSibling.value = e.target.value; });
  $('res').addEventListener('change', (e) => { S.res = +e.target.value; loadShape(); });
  renderShapeParams();
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
  $('hud-shape').textContent = `${SHAPES[S.shape].label.split('  ')[0]} · ${stats.vertices} v · size ${fmt(S.size, 2)}`;
  S.curv = null; S.lath = null; S.selected = null; S.frame = null; S.netData = null; S.lathUtil = null;
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
    v.showPoints('umb', flat, 0xf59e0b, 7);
  } else v.clearOverlay('umb');
  v.setWireframe($('wire').checked);
  updateRecipe();
}

// ---------------------------------------------------------------------------
// Seed and direction
// ---------------------------------------------------------------------------

function seedRelevant() { return ['asymptotic', 'conjugate', 'geodesic', 'geodesicBoth', 'streamMax', 'streamMin', 'chebyshev'].includes(S.net); }
function directionRelevant() { return ['geodesic', 'geodesicBoth', 'chebyshev'].includes(S.net); }

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
  else v.showPoints('seed', p, 0xf59e0b, 9);
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
  };
}

async function traceNet() {
  const v = S.viewer;
  S.lath = null; S.selected = null; S.frame = null; S.lathUtil = null;
  $('lath-result').classList.add('hidden');
  $('frame-stats').textContent = '';
  $('net-note').textContent = NET_NOTES[S.net];
  showComponent('gh-net', GH_NET[S.net]);
  $('famB-row').classList.toggle('hidden', ['geodesic', 'streamMax', 'streamMin', 'isocurves'].includes(S.net));
  if (S.net === 'none') {
    v.clearCurves(); v.clearOverlay('ends'); S.netData = null;
    $('net-stats').textContent = ''; $('net-warn').classList.add('hidden'); $('hud-net').textContent = ''; $('quality').classList.add('hidden'); $('spacing-count').textContent = '';
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
    v.setCurves(d.a, d.b);
    v.setFamilyVisible('A', $('famA').checked); v.setFamilyVisible('B', $('famB').checked);
    if ($('crossings').checked && d.crossings) v.showPoints('crossings', d.crossings.points, 0x475569, 3.5); else v.clearOverlay('crossings');
    showEndMarkers();
    const e = d.ends;
    const closed = S.stats.boundaryVertices === 0;
    let s = `A <b>${d.countA}</b>${d.countB ? ` · B <b>${d.countB}</b>` : ''} curves · length <b>${fmt(d.minLength, 2)} … ${fmt(d.maxLength, 2)}</b>` +
      (d.resolvedSpacing ? ` · spacing <b>${fmt(d.resolvedSpacing, 3)}</b>` : '') + ` · ${d.ms} ms`;
    if (d.crossings) s += `\ncrossings <b>${d.crossings.count}</b> · angles <b>${fmt(d.crossings.minAngle, 1)}° … ${fmt(d.crossings.maxAngle, 1)}°</b> · T-junctions <b>${d.crossings.tJunctions}</b>`;
    $('net-stats').innerHTML = s;
    $('spacing-count').textContent = `${d.countA + d.countB} curves`;
    $('net-warn').textContent = d.warnings.join(' ');
    $('net-warn').classList.toggle('hidden', d.warnings.length === 0);
    $('hud-net').textContent = `${S.net} · ${d.countA + d.countB} curves · ${d.ms} ms`;
    drawQuality(d, closed);
    await updateSeedHandle();
    if ($('colorutil').checked) await colourByUtilization();
    updateRecipe();
  });
}

function showEndMarkers() {
  const d = S.netData; const v = S.viewer;
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
  $('borderangle-row').classList.toggle('hidden', !(directionRelevant() && $('fromborder').checked && S.net !== 'chebyshev'));
}

// ---------------------------------------------------------------------------
// Lath
// ---------------------------------------------------------------------------

function lathOpts() {
  return { width: pct(+$('width').value), thickness: pct(+$('thick').value), upright: $('upright').checked, maxStrain: +$('strain').value / 100, section: +($('section')?.value ?? 0) };
}

async function analyseSelected() {
  const sel = S.selected;
  if (!sel) return;
  const o = lathOpts();
  const gen = S.gen;
  const L = await K('lath', sel.points.flat(), o.width, o.thickness, o.upright, o.maxStrain, $('solid').checked, o.section);
  if (gen !== S.gen || S.selected !== sel) return;
  S.lath = L;
  $('lath-result').classList.remove('hidden');
  const mk = (arr) => { let m = 0; for (const x of arr) if (Number.isFinite(x)) m = Math.max(m, Math.abs(x)); return m; };
  $('lath-stats').innerHTML = `family ${sel.family} #${sel.index} · length <b>${fmt(L.length)}</b> · flat <b>${fmt(L.flatLength)}</b> · bow <b>${fmt(L.bow)}</b>\n` +
    `max |kn| <b>${fmt(mk(L.kn), 2)}</b> · max |kg| <b>${fmt(mk(L.kg), 2)}</b> · max |tg| <b>${fmt(mk(L.tg), 2)}</b> · peak utilization <b class="${L.buildable ? 'ok' : 'bad'}">${fmt(L.maxUtilization, 2)} ${L.buildable ? '✓ buildable' : '✗ over the strain limit'}</b>`;
  drawLathPlot($('plot'), L);
  drawUnroll($('unroll'), L);
  if ($('solid').checked && L.sweepVertices.length) S.viewer.showSolid('sweep', L.sweepVertices, L.sweepFaces, 0xf59e0b); else S.viewer.clearOverlay('sweep');
  updateRecipe();
}

async function colourByUtilization() {
  if (!S.netData) return;
  const o = lathOpts();
  const gen = S.gen, data = S.netData;
  const utils = [];
  await withBusy('checking every lath…', async () => {
    for (const pts of [...data.a, ...data.b]) {
      const r = await K('lath', pts.flat(), o.width, o.thickness, o.upright, o.maxStrain, false, o.section);
      if (gen !== S.gen || S.netData !== data) return;
      utils.push(r.maxUtilization);
    }
  });
  if (gen !== S.gen || S.netData !== data) return;
  S.lathUtil = utils;
  const na = data.a.length;
  S.viewer.colorCurves(utils.slice(0, na).map(utilizationColor), utils.slice(na).map(utilizationColor));
  if (S.selected) S.viewer.selectCurve(S.selected);
  drawQuality(data, S.stats.boundaryVertices === 0);
}

// ---------------------------------------------------------------------------
// Frame
// ---------------------------------------------------------------------------

async function runFrame() {
  if (!S.netData) { $('frame-stats').textContent = 'Trace a net first.'; return; }
  await withBusy('solving frame…', async () => {
    const o = lathOpts();
    const metres = +$('scale').value / S.size;
    const gen = S.gen;
    const r = await K('frame', o.width, o.thickness, o.upright, +$('load').value * 1000, metres, 0, o.section);
    if (gen !== S.gen) return;
    S.frame = r;
    if (r.error) { $('frame-stats').innerHTML = `<span class="bad">${r.error}</span>`; return; }
    $('frame-stats').innerHTML = `nodes <b>${r.nodes}</b> · elements <b>${r.elements}</b> · supports <b>${r.supports}</b> · ${r.ms} ms\n` +
      `max deflection <b>${(r.maxDisplacement * 1000).toFixed(1)} mm</b> · peak utilization <b class="${r.maxUtilization <= 1 ? 'ok' : 'bad'}">${fmt(r.maxUtilization, 2)}</b>`;
    const na = S.netData.countA;
    S.viewer.colorCurves(r.lathUtilization.slice(0, na).map(utilizationColor), r.lathUtilization.slice(na).map(utilizationColor));
    showDeformed();
    updateRecipe();
  });
}

function showDeformed() {
  const v = S.viewer;
  if (!S.frame || S.frame.error || !$('deformed').checked) { v.clearOverlay('deformed'); return; }
  const k = +$('defscale').value || 1;
  const laths = [...S.netData.a, ...S.netData.b];
  const curves = S.frame.deformed.map((disp, c) => {
    const pts = resample(laths[c], disp.length);
    return pts.map((p, i) => [p[0] + k * disp[i][0], p[1] + k * disp[i][1], p[2] + k * disp[i][2]]);
  });
  v.showPolylines('deformed', curves, 0x64748b, 1.4, true);
}

function resample(pts, n) {
  const arc = [0];
  for (let i = 1; i < pts.length; i++) arc.push(arc[i - 1] + Math.hypot(pts[i][0] - pts[i - 1][0], pts[i][1] - pts[i - 1][1], pts[i][2] - pts[i - 1][2]));
  const L = arc[arc.length - 1];
  const out = [];
  let j = 0;
  for (let k = 0; k < n; k++) {
    const t = (L * k) / Math.max(1, n - 1);
    while (j + 1 < arc.length - 1 && arc[j + 1] < t) j++;
    const seg = Math.max(1e-12, arc[j + 1] - arc[j]);
    const u = Math.min(1, Math.max(0, (t - arc[j]) / seg));
    out.push([0, 1, 2].map((d) => pts[j][d] + (pts[j + 1][d] - pts[j][d]) * u));
  }
  return out;
}

// ---------------------------------------------------------------------------
// Recipe
// ---------------------------------------------------------------------------

function updateRecipe() {
  const lines = ['Mite for Grasshopper — reproduce this view', `mesh: ${SHAPES[S.shape].label}` + (S.shape !== 'loft' && S.shape !== 'file' ? ` (${SHAPES[S.shape].params.map((p, i) => `${p[0]} = ${S.params[i]}`).join(', ')})` : '')];
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
    else p += `AutoSpace = True, Spacing = ${fmt(o.spacing)}, Continuous = ${o.continuous}, Step = 0 (auto)`;
    if (S.net === 'asymptotic') p += `, MinAngle = ${o.minAngle}`;
    if (S.seed >= 0) p += `, Seed = ${S.seed}`;
    if (directionRelevant()) p += `, Direction = (${o.direction.map((x) => x.toFixed(2)).join(', ')})`;
    if ((S.net === 'geodesic' || S.net === 'geodesicBoth') && o.fromBorder) p += `, FromBorder = True, BorderAngle = ${o.borderAngle}`;
    lines.push(p);
  }
  if (S.lath) {
    const o = lathOpts();
    lines.push(`Lath Analysis: Width = ${fmt(o.width)}, Thickness = ${fmt(o.thickness)}, Upright = ${o.upright}, MaxStrain = ${o.maxStrain}`);
    lines.push(`Lath Sweep: Width = ${fmt(o.width)}, Thickness = ${fmt(o.thickness)}, Upright = ${o.upright}, Shape = ${o.section} (0 rectangle, 1 round bar)`);
    lines.push('Lath Unroll: same Width / Upright → Patterns');
  }
  if (S.frame && !S.frame.error) lines.push(`Gridshell Analysis: Supports = border ends, Load = (0,0,-${$('load').value} kN/m), model ${$('scale').value} m across`);
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
    S.selected = o;
    S.viewer.selectCurve(o);
    if (o) analyseSelected(); else { $('lath-result').classList.add('hidden'); S.viewer.clearOverlay('sweep'); }
  };
  S.viewer.onHover = (o) => {
    if (!o) { $('hud-hover').textContent = ''; return; }
    const pts = o.points; let L = 0; for (let i = 1; i < pts.length; i++) L += Math.hypot(pts[i][0] - pts[i - 1][0], pts[i][1] - pts[i - 1][1], pts[i][2] - pts[i - 1][2]);
    const u = S.lathUtil?.[(o.family === 'A' ? 0 : S.netData.a.length) + o.index];
    $('hud-hover').textContent = `family ${o.family} #${o.index} · length ${fmt(L, 3)}` + (u != null ? ` · utilization ${fmt(u, 2)}` : '') + ' · click for the lath';
  };
  S.viewer.onPickVertex = async (p, shift) => {
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

  // nets
  $('nets').addEventListener('click', (e) => {
    const b = e.target.closest('button'); if (!b) return;
    $('nets').querySelectorAll('button').forEach((x) => x.classList.toggle('on', x === b));
    S.net = b.dataset.net; showNetParams();
    $('upright').checked = S.net === 'asymptotic'; // asymptotic laths stand upright, geodesic laths lie flat
    traceNet();
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
    if (S.netData?.crossings && $('crossings').checked) S.viewer.showPoints('crossings', S.netData.crossings.points, 0x475569, 3.5); else S.viewer.clearOverlay('crossings');
  });
  $('famA').addEventListener('change', () => S.viewer.setFamilyVisible('A', $('famA').checked));
  $('famB').addEventListener('change', () => S.viewer.setFamilyVisible('B', $('famB').checked));

  // lath
  document.querySelectorAll('#lath-block input[type=range]').forEach((inp) => {
    inp.addEventListener('input', () => { inp.nextElementSibling.value = inp.value; });
    inp.addEventListener('change', () => { if (S.selected) analyseSelected(); if ($('colorutil').checked) colourByUtilization(); });
  });
  $('upright').addEventListener('change', () => { if (S.selected) analyseSelected(); if ($('colorutil').checked) colourByUtilization(); });
  $('section')?.addEventListener('change', () => { if (S.selected) analyseSelected(); });
  $('solid').addEventListener('change', () => { if (S.selected) analyseSelected(); });
  $('colorutil').addEventListener('change', () => { if ($('colorutil').checked) colourByUtilization(); else { S.lathUtil = null; S.viewer.colorCurves(null, null); if (S.selected) S.viewer.selectCurve(S.selected); if (S.netData) drawQuality(S.netData, S.stats.boundaryVertices === 0); } });

  // frame
  $('frame').addEventListener('click', runFrame);
  $('deformed').addEventListener('change', showDeformed);
  $('defscale').addEventListener('change', showDeformed);
  for (const id of ['load', 'scale']) $(id).addEventListener('input', (e) => { e.target.nextElementSibling.value = e.target.value; });

  // file
  const loadFile = async (file) => {
    try {
      const g = await readMeshFile(file);
      S.file = g; S.seed = -1;
      $('shape').value = 'file'; S.shape = 'file'; renderShapeParams();
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
  window.__mite = S; // for tests
  await loadShape();
}

main().catch((e) => { console.error(e); $('boot-text').textContent = 'Failed to start: ' + (e.message || e); });
