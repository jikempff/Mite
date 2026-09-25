// Mite interactive: wires the panel to the WebAssembly kernel and the viewer.

import { bootMite } from './runtime.js';
import { Viewer, FAMILY_A, FAMILY_B } from './viewer.js';
import { diverging, sequential, utilizationColor, legendGradient } from './colormaps.js';
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
  wave: { label: 'Wave  z = A·sin(fx)·cos(fy)', params: [['size', 1, 4, 2, 0.1], ['amplitude', 0.05, 0.8, 0.3, 0.01], ['frequency', 0.5, 4, 2, 0.1]],
    note: 'Mixed curvature: elliptic caps and anticlastic saddles between them, separated by K = 0 lines where asymptotic curves fade out.' },
  sphere: { label: 'Sphere', params: [['radius', 0.5, 2, 1, 0.05]],
    note: 'k1 = k2 = 1/r everywhere: every point is an umbilic, so principal directions and curvature lines are undefined; geodesics are great circles.' },
  dome: { label: 'Dome cap', params: [['radius', 0.5, 2, 1, 0.05], ['half-angle °', 20, 90, 60, 5]],
    note: 'K > 0: no asymptotic curves. Geodesic nets and Chebyshev nets are the layouts that apply.' },
  ellipsoid: { label: 'Ellipsoid', params: [['a', 0.5, 2, 1.5, 0.05], ['b', 0.5, 2, 1, 0.05], ['c', 0.3, 2, 0.7, 0.05]],
    note: 'Exactly four umbilics on the a–c section; curvature lines form the classic lemon pattern around them.' },
  torus: { label: 'Torus', params: [['R', 1.5, 4, 3, 0.1], ['r', 0.3, 1.4, 1, 0.05]],
    note: 'K > 0 outside, K < 0 inside, K = 0 on the top and bottom circles. Curvature lines are meridians and parallels; asymptotic curves live on the inner half only.' },
  vault: { label: 'Barrel vault (K = 0)', params: [['R', 0.8, 3, 1.5, 0.05], ['width', 1, 4, 2, 0.1], ['length', 1, 6, 4, 0.1]],
    note: 'Developable: k2 = 0 along the rulings. Curvature lines are rulings and arcs; geodesics are helices.' },
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
  anticlastic: 'Vertices where asymptotic directions exist (K < 0). Asymptotic nets fill only this region.',
};

const NET_NOTES = {
  none: '',
  asymptotic: 'Both families of asymptotic curves (zero normal curvature): laths bend only about their weak axis and twist — stand them upright. Only where K < 0.',
  conjugate: 'The two principal curvature line families: an approximate conjugate net, the layout for planar-quad panels. Undefined at umbilics.',
  geodesic: 'One family of straightest geodesics grown sideways from the seed; flat laths follow geodesics without in-plane bending.',
  geodesicBoth: 'Two geodesic families crossing at the family angle at the seed.',
  streamMax: 'Lines of maximum principal curvature, evenly spaced.',
  streamMin: 'Lines of minimum principal curvature, evenly spaced.',
  chebyshev: 'Compass-method Chebyshev net: every edge has the same length — the flat-lattice kinematics of an elastic gridshell. The Angles collapse where the net locks.',
  isocurves: 'Level curves of a scalar field on the mesh (marching triangles).',
};

// ---------------------------------------------------------------------------
// State
// ---------------------------------------------------------------------------

const S = {
  mite: null, viewer: null,
  shape: 'saddle', params: {}, res: 40,
  vertices: null, triangles: null, stats: null, size: 1,
  curv: null, mode: 'shaded', smooth: 2,
  net: 'none', netData: null, seed: -1, seedPoint: null,
  loft: null, file: null,
  lath: null, selected: null,
  frame: null,
  busy: 0,
};

function busy(on, text) {
  S.busy += on ? 1 : -1;
  $('busy').classList.toggle('hidden', S.busy <= 0);
  if (text) $('busy-text').textContent = text;
}
async function withBusy(text, fn) {
  busy(true, text);
  await new Promise((r) => setTimeout(r, 15)); // let the indicator paint
  try { return await fn(); } finally { busy(false); }
}
const fmt = (x, d = 3) => Number.isFinite(x) ? (+x).toFixed(d) : '–';
const pct = (v) => (v / 100) * S.size;

// ---------------------------------------------------------------------------
// Shape
// ---------------------------------------------------------------------------

function buildShapeUI() {
  const sel = $('shape');
  sel.innerHTML = Object.entries(SHAPES).map(([k, v]) => `<option value="${k}">${v.label}</option>`).join('');
  sel.value = S.shape;
  sel.addEventListener('change', () => { S.shape = sel.value; renderShapeParams(); loadShape(); });
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

let lightTimer = null;
async function loadShape({ light = false } = {}) {
  if (!S.mite) return;
  if (light) { // slider is being dragged: rebuild shape + colours only, coalesced
    if (lightTimer) return;
    lightTimer = requestAnimationFrame(() => { lightTimer = null; applyShape(true); });
    return;
  }
  await withBusy('building shape…', () => applyShape(false));
}

function applyShape(light) {
  const m = S.mite;
  let stats;
  if (S.shape === 'loft') {
    if (!S.loft) S.loft = new Loft(4, 6, 3);
    const g = S.loft.build(Math.max(12, S.res), Math.max(9, Math.round(S.res * 0.75)));
    stats = m.loadMesh(g.vertices, g.triangles, false);
  } else if (S.shape === 'file') {
    if (!S.file) { $('mesh-stats').textContent = 'Choose or drop a file.'; return; }
    stats = m.loadMesh(S.file.vertices, S.file.triangles, $('weld').checked);
  } else {
    const p = SHAPES[S.shape].params.map((_, i) => S.params[i] ?? 0);
    const res = S.shape === 'torus' || S.shape === 'hyperboloid' || S.shape === 'catenoid' ? Math.round(S.res * 1.5) : S.res;
    stats = m.shape(S.shape, p[0] || 0, p[1] || 0, p[2] || 0, res);
  }
  S.stats = stats;
  S.vertices = m.vertices();
  S.triangles = m.triangles();
  const d = [0, 1, 2].map((k) => stats.max[k] - stats.min[k]);
  S.size = Math.hypot(...d) || 1;
  const fit = !light && !(S.shape === 'loft' && S.viewer.mesh);
  S.viewer.setMesh(S.vertices, S.triangles, { fit });
  if (S.shape === 'loft') S.viewer.setHandles(S.loft.flatControls(), 0.012 * S.size); else S.viewer.clearHandles();
  $('mesh-stats').innerHTML = `<b>${stats.vertices}</b> vertices · <b>${stats.faces}</b> triangles · avg edge <b>${fmt(stats.avgEdge)}</b> · border vertices <b>${stats.boundaryVertices}</b>` +
    (stats.welded ? ` · welded <b>${stats.welded}</b>` : '') + (stats.removedFaces ? ` · removed <b>${stats.removedFaces}</b> faces` : '') + ` · ${stats.ms} ms`;
  $('hud-shape').textContent = `${SHAPES[S.shape].label.split('  ')[0]} · ${stats.vertices} v · size ${fmt(S.size, 2)}`;
  S.curv = null; S.lath = null; S.selected = null; S.frame = null; S.netData = null;
  S.viewer.clearCurves();
  $('lath-result').classList.add('hidden');
  $('frame-stats').textContent = '';
  applyMode();
  if (!light && S.net !== 'none') traceNet();
  if (light && S.net !== 'none') scheduleNet();
}

let netTimer = null;
function scheduleNet() { clearTimeout(netTimer); netTimer = setTimeout(() => traceNet(), 350); }

// ---------------------------------------------------------------------------
// Analysis colouring
// ---------------------------------------------------------------------------

function ensureCurvature() {
  if (!S.curv) {
    S.curv = S.mite.curvature(S.smooth, 0.05);
    const c = S.curv;
    const n = c.k1.length;
    $('curv-stats').innerHTML = `k1 <b>${fmt(Math.min(...c.k1), 2)} … ${fmt(Math.max(...c.k1), 2)}</b> · k2 <b>${fmt(Math.min(...c.k2), 2)} … ${fmt(Math.max(...c.k2), 2)}</b>\n` +
      `anticlastic (K &lt; 0) <b>${c.anticlastic}</b> / ${n} vertices · umbilics <b>${c.umbilics.length}</b> · ${c.ms} ms`;
  }
  return S.curv;
}

function applyMode() {
  const v = S.viewer;
  const legend = $('legend');
  v.setZebra(S.mode === 'zebra');
  $('mode-note').textContent = MODE_NOTES[S.mode];
  if (S.mode === 'shaded' || S.mode === 'zebra') { v.setColors(null); legend.classList.add('hidden'); }
  else {
    const c = ensureCurvature();
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
  // glyphs
  if ($('dirs').checked) {
    const c = ensureCurvature();
    const n = c.k1.length;
    const every = Math.max(1, Math.round(n / 900));
    v.showDirections(S.vertices, c.d1, c.d2, c.normals, every, S.stats.avgEdge * 1.4);
  } else v.hideDirections();
  if ($('umb').checked) {
    const c = ensureCurvature();
    const flat = [];
    for (const i of c.umbilics) flat.push(S.vertices[3 * i], S.vertices[3 * i + 1], S.vertices[3 * i + 2]);
    v.showPoints('umb', flat, 0xf59e0b, 7);
  } else v.clearOverlay('umb');
  v.setWireframe($('wire').checked);
  if (S.mode !== 'shaded' && S.mode !== 'zebra' && S.curv) v.setMeshOpacity(1);
  updateRecipe();
}

// ---------------------------------------------------------------------------
// Nets
// ---------------------------------------------------------------------------

function netOptions() {
  const a = (+$('dirangle').value * Math.PI) / 180;
  return {
    spacing: pct(+$('spacing').value), step: 0, continuous: $('continuous').checked, maxCurves: 300,
    seed: S.seed, direction: [Math.cos(a), Math.sin(a), 0], edgeLength: pct(+$('edge').value), count: +$('count').value,
    angleDeg: +$('famangle').value, levels: +$('levels').value, field: $('isofield').value,
  };
}

async function traceNet() {
  const v = S.viewer;
  S.lath = null; S.selected = null; S.frame = null;
  $('lath-result').classList.add('hidden');
  $('frame-stats').textContent = '';
  $('net-note').textContent = NET_NOTES[S.net];
  if (S.net === 'none') { v.clearCurves(); S.netData = null; $('net-stats').textContent = ''; $('net-warn').classList.add('hidden'); $('hud-net').textContent = ''; updateRecipe(); return; }
  await withBusy('tracing…', () => {
    let d;
    try { d = S.mite.net(S.net, netOptions()); }
    catch (e) { $('net-warn').textContent = String(e.message || e); $('net-warn').classList.remove('hidden'); v.clearCurves(); return; }
    S.netData = d;
    v.setCurves(d.a, d.b);
    if ($('crossings').checked && d.crossings) v.showPoints('crossings', d.crossings.points, 0x475569, 3.5);
    const e = d.ends;
    let s = `A <b>${d.countA}</b>${d.countB ? ` · B <b>${d.countB}</b>` : ''} curves · length <b>${fmt(d.minLength, 2)} … ${fmt(d.maxLength, 2)}</b>` +
      (d.resolvedSpacing ? ` · spacing <b>${fmt(d.resolvedSpacing, 3)}</b>` : '') + `\nends: border <b>${e.border}</b>` + (e.regionEdge ? ` · K = 0 line <b>${e.regionEdge}</b>` : '') + ` · on a neighbour <b>${e.onCurve}</b> · ${S.stats.boundaryVertices === 0 ? 'step limit' : 'floating'} <b class="${e.floating && S.stats.boundaryVertices ? 'bad' : ''}">${e.floating}</b> · closed loops <b>${e.closed}</b>`;
    if (d.crossings) s += `\ncrossings <b>${d.crossings.count}</b> · angles <b>${fmt(d.crossings.minAngle, 1)}° … ${fmt(d.crossings.maxAngle, 1)}°</b> · T-junctions <b>${d.crossings.tJunctions}</b>`;
    s += ` · ${d.ms} ms`;
    $('net-stats').innerHTML = s;
    $('net-warn').textContent = d.warnings.join(' ');
    $('net-warn').classList.toggle('hidden', d.warnings.length === 0);
    $('hud-net').textContent = `${S.net} · ${d.countA + d.countB} curves · ${d.ms} ms`;
    if ($('colorutil').checked) colourByUtilization();
    updateRecipe();
  });
}

function showNetParams() {
  document.querySelectorAll('#net-params [data-for]').forEach((el) => {
    el.classList.toggle('hidden', !el.dataset.for.split(' ').includes(S.net));
  });
}

// ---------------------------------------------------------------------------
// Lath
// ---------------------------------------------------------------------------

function lathOpts() {
  return { width: pct(+$('width').value), thickness: pct(+$('thick').value), upright: $('upright').checked, maxStrain: +$('strain').value / 100 };
}

function analyseSelected() {
  const sel = S.selected;
  if (!sel) return;
  const o = lathOpts();
  const flat = sel.points.flat();
  S.lath = S.mite.lath(flat, o.width, o.thickness, o.upright, o.maxStrain, $('solid').checked);
  const L = S.lath;
  $('lath-result').classList.remove('hidden');
  const mk = (arr) => { let m = 0; for (const x of arr) if (Number.isFinite(x)) m = Math.max(m, Math.abs(x)); return m; };
  $('lath-stats').innerHTML = `family ${sel.family} #${sel.index} · length <b>${fmt(L.length)}</b> · flat <b>${fmt(L.flatLength)}</b> · bow <b>${fmt(L.bow)}</b>\n` +
    `max |kn| <b>${fmt(mk(L.kn), 2)}</b> · max |kg| <b>${fmt(mk(L.kg), 2)}</b> · max |tg| <b>${fmt(mk(L.tg), 2)}</b> · peak utilization <b class="${L.buildable ? 'ok' : 'bad'}">${fmt(L.maxUtilization, 2)} ${L.buildable ? '✓ buildable' : '✗ over the strain limit'}</b>`;
  drawLathPlot($('plot'), L);
  drawUnroll($('unroll'), L);
  if ($('solid').checked && L.sweepVertices.length) S.viewer.showSolid('sweep', L.sweepVertices, L.sweepFaces, 0xf59e0b); else S.viewer.clearOverlay('sweep');
  updateRecipe();
}

function colourByUtilization() {
  if (!S.netData) return;
  const o = lathOpts();
  const col = (curves) => curves.map((pts) => {
    const r = S.mite.lath(pts.flat(), o.width, o.thickness, o.upright, o.maxStrain, false);
    return utilizationColor(r.maxUtilization);
  });
  S.viewer.colorCurves(col(S.netData.a), col(S.netData.b));
  if (S.selected) S.viewer.selectCurve(S.selected);
}

// ---------------------------------------------------------------------------
// Frame
// ---------------------------------------------------------------------------

async function runFrame() {
  if (!S.netData) { $('frame-stats').textContent = 'Trace a net first.'; return; }
  await withBusy('solving frame…', () => {
    const o = lathOpts();
    const metres = +$('scale').value / S.size;
    const r = S.mite.frame(o.width, o.thickness, o.upright, +$('load').value * 1000, metres, 0);
    S.frame = r;
    if (r.error) { $('frame-stats').innerHTML = `<span class="bad">${r.error}</span>`; return; }
    $('frame-stats').innerHTML = `nodes <b>${r.nodes}</b> · elements <b>${r.elements}</b> · supports <b>${r.supports}</b> · ${r.ms} ms\n` +
      `max deflection <b>${(r.maxDisplacement * 1000).toFixed(1)} mm</b> · peak utilization <b class="${r.maxUtilization <= 1 ? 'ok' : 'bad'}">${fmt(r.maxUtilization, 2)}</b>`;
    const na = S.netData.countA;
    S.viewer.colorCurves(r.lathUtilization.slice(0, na).map(utilizationColor), r.lathUtilization.slice(na).map(utilizationColor));
    showDeformed();
  });
}

function showDeformed() {
  const v = S.viewer;
  if (!S.frame || S.frame.error || !$('deformed').checked) { v.clearOverlay('deformed'); return; }
  const k = +$('defscale').value || 1;
  const laths = [...S.netData.a, ...S.netData.b];
  // deformed[] are per resampled lath; resample the drawn curves the same way is not available here,
  // so rebuild from the resampled displacement arrays: they carry positions? No: displacements only.
  // We reconstruct positions by resampling the original curve to the same count.
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
    if (S.seed >= 0) p += `, Seed = ${S.seed}`;
    if (S.net === 'geodesic' || S.net === 'geodesicBoth') p += `, Direction = (${o.direction.map((x) => x.toFixed(2)).join(', ')})`;
    lines.push(p);
  }
  if (S.lath) {
    const o = lathOpts();
    lines.push(`Lath Analysis: Width = ${fmt(o.width)}, Thickness = ${fmt(o.thickness)}, Upright = ${o.upright}, MaxStrain = ${o.maxStrain}`);
    lines.push('Lath Unroll: same Width / Upright → Patterns');
  }
  if (S.frame && !S.frame.error) lines.push(`Gridshell Analysis: Supports = border ends, Load = (0,0,-${$('load').value} kN/m), model ${$('scale').value} m across`);
  $('recipe').textContent = lines.join('\n');
}

// ---------------------------------------------------------------------------
// Boot
// ---------------------------------------------------------------------------

async function main() {
  const dark = matchMedia('(prefers-color-scheme: dark)').matches;
  if (dark) document.documentElement.dataset.theme = 'dark';
  $('theme').addEventListener('click', () => {
    const d = document.documentElement.dataset.theme === 'dark';
    document.documentElement.dataset.theme = d ? 'light' : 'dark';
    S.viewer?.setTheme(!d);
    if (S.lath) { drawLathPlot($('plot'), S.lath); drawUnroll($('unroll'), S.lath); }
  });

  S.viewer = new Viewer($('gl'));
  S.viewer.setTheme(dark);
  buildShapeUI();
  showNetParams();

  const total = 18;
  S.mite = await bootMite((n, name) => {
    $('boot-bar').style.width = Math.min(100, (n / total) * 100) + '%';
    $('boot-text').textContent = name;
  });
  $('version').textContent = S.mite.version() + ' · wasm · boot ' + S.mite.bootMs + ' ms';
  $('boot').classList.add('hidden');

  // viewer interactions
  S.viewer.onPickCurve = (o) => {
    S.selected = o;
    S.viewer.selectCurve(o);
    if (o) analyseSelected(); else { $('lath-result').classList.add('hidden'); S.viewer.clearOverlay('sweep'); }
  };
  S.viewer.onPickVertex = (p, shift) => {
    if (!shift) return;
    S.seed = S.mite.nearestVertex(p[0], p[1], p[2]);
    S.seedPoint = p;
    $('seed-label').textContent = `vertex ${S.seed}`;
    S.viewer.showPoints('seed', [S.vertices[3 * S.seed], S.vertices[3 * S.seed + 1], S.vertices[3 * S.seed + 2]], 0xf59e0b, 9);
    if (S.net !== 'none') traceNet();
  };
  $('seed-clear').addEventListener('click', () => { S.seed = -1; $('seed-label').textContent = 'auto (centre)'; S.viewer.clearOverlay('seed'); if (S.net !== 'none') traceNet(); });
  S.viewer.onHandleDrag = (id, p) => { S.loft.setControl(id, p); loadShape({ light: true }); };
  S.viewer.onHandleDragEnd = () => { if (S.net !== 'none') scheduleNet(); };

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
    inp.addEventListener('input', () => { inp.nextElementSibling.value = inp.value; });
    inp.addEventListener('change', () => { if (S.net !== 'none') traceNet(); });
  });
  $('isofield').addEventListener('change', () => { if (S.net === 'isocurves') traceNet(); });
  $('continuous').addEventListener('change', () => { if (S.net !== 'none') traceNet(); });
  $('crossings').addEventListener('change', () => {
    if (S.netData?.crossings && $('crossings').checked) S.viewer.showPoints('crossings', S.netData.crossings.points, 0x475569, 3.5); else S.viewer.clearOverlay('crossings');
  });

  // lath
  document.querySelectorAll('#lath-block input[type=range]').forEach((inp) => {
    inp.addEventListener('input', () => { inp.nextElementSibling.value = inp.value; });
    inp.addEventListener('change', () => { if (S.selected) analyseSelected(); if ($('colorutil').checked) colourByUtilization(); });
  });
  $('upright').addEventListener('change', () => { if (S.selected) analyseSelected(); if ($('colorutil').checked) colourByUtilization(); });
  $('solid').addEventListener('change', () => { if (S.selected) analyseSelected(); });
  $('colorutil').addEventListener('change', () => { if ($('colorutil').checked) colourByUtilization(); else { S.viewer.colorCurves(null, null); if (S.selected) S.viewer.selectCurve(S.selected); } });

  // frame
  $('frame').addEventListener('click', runFrame);
  $('deformed').addEventListener('change', showDeformed);
  $('defscale').addEventListener('change', showDeformed);
  for (const id of ['load', 'scale']) $(id).addEventListener('input', (e) => { e.target.nextElementSibling.value = e.target.value; });

  // file
  const loadFile = async (file) => {
    try {
      const g = await readMeshFile(file);
      S.file = g;
      $('shape').value = 'file'; S.shape = 'file'; renderShapeParams();
      await loadShape();
    } catch (err) { alert(err.message); }
  };
  $('file').addEventListener('change', (e) => { if (e.target.files[0]) loadFile(e.target.files[0]); });
  $('weld').addEventListener('change', () => { if (S.shape === 'file') loadShape(); });
  const stage = $('stage');
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

  window.addEventListener('keydown', (e) => { if (e.key === 'f' && !e.target.closest('input,select,textarea')) S.viewer.fit(); });

  await loadShape();
  // a first net so the page shows what it is about
  $('nets').querySelector('[data-net=asymptotic]').click();
  window.__mite = S; // for tests
}

main().catch((e) => { console.error(e); $('boot-text').textContent = 'Failed to start: ' + (e.message || e); });
