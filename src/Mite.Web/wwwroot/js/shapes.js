// Shape catalogue: grouped by curvature class, each entry with a symbol
// (a wire drawing of the surface itself, generated from its formula), a
// name, the formula and the kernel parameters.

const { sin, cos, sinh, cosh, sqrt, PI } = Math;

// Curvature classes, coloured like the Gaussian-curvature map: K < 0 blue,
// K > 0 red, K = 0 green, mixed half and half.
export const CLASSES = {
  minimal: { tag: 'H = 0', dot: 'neg' },
  anticlastic: { tag: 'K < 0', dot: 'neg' },
  synclastic: { tag: 'K > 0', dot: 'pos' },
  developable: { tag: 'K = 0', dot: 'zero' },
  mixed: { tag: 'K ≷ 0', dot: 'mixed' },
  free: { tag: 'any', dot: 'free' },
};

export const GROUPS = [
  { title: 'Minimal surfaces', sub: 'mean curvature H = 0 — asymptotic laths cross at 90°', keys: ['catenoid', 'enneper', 'enneper3', 'schwarzd', 'gyroid'] },
  { title: 'Anticlastic & ruled', sub: 'saddle-shaped, K < 0 everywhere', keys: ['saddle', 'hyperboloid', 'ruled', 'monkey', 'annulus'] },
  { title: 'Synclastic', sub: 'dome-shaped, K > 0: geodesic and Chebyshev nets', keys: ['sphere', 'dome', 'ellipsoid'] },
  { title: 'Developable', sub: 'K = 0: bends from a flat sheet', keys: ['vault', 'cone'] },
  { title: 'Mixed curvature', sub: 'regions of both signs, parted by K = 0 lines', keys: ['torus', 'wave'] },
  { title: 'Your own', sub: '', keys: ['loft', 'file'] },
];

// params: [name, min, max, default, step]; formula: one or two lines
export const SHAPES = {
  catenoid: { name: 'Catenoid', cls: 'minimal', formula: ['ρ = c · cosh(z / c)'], params: [['c', 0.5, 2, 1, 0.05], ['height', 0.4, 2, 1.2, 0.05]],
    note: 'The minimal surface of revolution: k1 = −k2, so the asymptotic families cross at exactly 90°. The symmetric web is rotational and its meridian diagonals are geodesics — an AAG web as it stands.' },
  enneper: { name: 'Enneper surface', cls: 'minimal', formula: ['x = u − u³/3 + uv²,  y = v − v³/3 + u²v', 'z = u² − v²'], params: [['extent', 0.5, 1.6, 1.2, 0.05]],
    note: 'Classical Enneper surface: its asymptotic lines are u ± v = const and two of them are straight. It self-intersects beyond an extent of about 1.4.' },
  enneper3: { name: 'Enneper, n-fold', cls: 'minimal', formula: ['Weierstrass  f = 1,  g = wⁿ⁻¹'], params: [['folds', 2, 5, 3, 1], ['radius', 0.4, 1.2, 0.9, 0.05]],
    note: 'Higher-order Enneper surfaces. The centre is a flat point: 2n asymptotic rays leave it and the families swap across them. The symmetric web makes it a singular node of valence 2n and repeats one sector n times.' },
  schwarzd: { name: 'Schwarz D', cls: 'minimal', formula: ['sin x sin y sin z + sin x cos y cos z', '+ cos x sin y cos z + cos x cos y sin z = 0'], params: [['extent', 0.5, 2, 1, 0.1], ['cells', 12, 40, 24, 2], ['relax', 0, 60, 30, 5]],
    note: 'The diamond surface of Schling\'s asymptotic pavilion: cut from its nodal approximation and relaxed to a soap film (H ≈ 0). Slower at high cell counts.' },
  gyroid: { name: 'Gyroid', cls: 'minimal', formula: ['sin x cos y + sin y cos z + sin z cos x = 0'], params: [['extent', 0.5, 2, 1, 0.1], ['cells', 12, 40, 24, 2], ['relax', 0, 60, 30, 5]],
    note: 'Gyroid patch from its nodal approximation, relaxed to H ≈ 0: an asymptotic net of nearly right angles.' },
  saddle: { name: 'Hyperbolic paraboloid', cls: 'anticlastic', formula: ['z = a x² − b y²'], params: [['size', 1, 4, 2, 0.1], ['a', 0.2, 2, 1, 0.05], ['b', 0.2, 2, 1, 0.05]],
    note: 'Doubly ruled: the asymptotic curves are the straight lines x = ±y·√(b/a) + c, so every asymptotic lath is straight and runs border to border.' },
  hyperboloid: { name: 'Hyperboloid of one sheet', cls: 'anticlastic', formula: ['(x² + y²) / a² − z² / c² = 1'], params: [['a (waist)', 0.4, 2, 1, 0.05], ['c', 0.4, 2, 1, 0.05], ['height', 0.4, 1.5, 1, 0.05]],
    note: 'Doubly ruled: its asymptotic curves are exactly the two families of straight rulings — the sharpest test of the tracer, and the exact scissor mechanism of Net Kinetics.' },
  ruled: { name: 'Bilinear patch', cls: 'anticlastic', formula: ['X = (1−u)(1−v) P₀₀ + u(1−v) P₁₀', '+ (1−u)v P₀₁ + uv P₁₁'], params: [['size', 1, 4, 2, 0.1], ['twist', 0.1, 2, 0.8, 0.05], ['skew', 0, 1, 0.3, 0.05]],
    note: 'A skew quad spanned bilinearly: doubly ruled, and its straight iso-lines are exactly its asymptotic curves.' },
  monkey: { name: 'Monkey saddle', cls: 'anticlastic', formula: ['z = k (x³ − 3 x y²)'], params: [['size', 1, 4, 2, 0.1], ['k', 0.2, 2, 1, 0.05]],
    note: 'A flat point at the origin with three asymptotic directions: a singular node of valence six in the symmetric web.' },
  annulus: { name: 'Annular saddle', cls: 'anticlastic', formula: ['z = k (x² − y²),  rᵢ ≤ ρ ≤ rₒ'], params: [['inner r', 0.1, 0.9, 0.4, 0.05], ['outer r', 1, 2, 1.2, 0.05], ['k', 0.2, 2, 1, 0.05]],
    note: 'A saddle with a hole: curves must end cleanly on both boundaries.' },
  sphere: { name: 'Sphere', cls: 'synclastic', formula: ['x² + y² + z² = r²'], params: [['radius', 0.5, 2, 1, 0.05]],
    note: 'k1 = k2 = 1/r everywhere: every point is an umbilic, principal directions are undefined; geodesics are great circles.' },
  dome: { name: 'Dome cap', cls: 'synclastic', formula: ['x² + y² + z² = r²,  polar angle ≤ θ'], params: [['radius', 0.5, 2, 1, 0.05], ['half-angle °', 20, 90, 60, 5]],
    note: 'K > 0: no asymptotic curves. Geodesic and Chebyshev nets apply; geodesics converge towards the top, which Jacobi seeding compensates.' },
  ellipsoid: { name: 'Ellipsoid', cls: 'synclastic', formula: ['x²/a² + y²/b² + z²/c² = 1'], params: [['a', 0.5, 2, 1.5, 0.05], ['b', 0.5, 2, 1, 0.05], ['c', 0.3, 2, 0.7, 0.05]],
    note: 'Exactly four umbilics on the a–c section; curvature lines form the classic lemon pattern around them.' },
  vault: { name: 'Barrel vault', cls: 'developable', formula: ['z = √(R² − x²) − R'], params: [['R', 0.8, 3, 1.5, 0.05], ['width', 1, 4, 2, 0.1], ['length', 1, 6, 4, 0.1]],
    note: 'Developable: k2 = 0 along the rulings. Curvature lines are rulings and arcs; geodesics are helices — try the border web with a border angle.' },
  cone: { name: 'Cone frustum', cls: 'developable', formula: ['ρ = r₀ + (r₁ − r₀) · z / h'], params: [['radius', 0.5, 2, 1, 0.05], ['height', 0.5, 3, 1.5, 0.05], ['top radius', 0.05, 0.9, 0.15, 0.01]],
    note: 'Developable with a near-singular apex: a projection and tracing stress test.' },
  torus: { name: 'Torus', cls: 'mixed', formula: ['(√(x² + y²) − R)² + z² = r²'], params: [['R', 1.5, 4, 3, 0.1], ['r', 0.3, 1.4, 1, 0.05]],
    note: 'K > 0 outside, K < 0 inside, K = 0 on the top and bottom circles; asymptotic curves live on the inner half only.' },
  wave: { name: 'Wave', cls: 'mixed', formula: ['z = A · sin(f x) · cos(f y)'], params: [['size', 1, 4, 2, 0.1], ['amplitude', 0.05, 0.8, 0.3, 0.01], ['frequency', 0.5, 4, 2, 0.1]],
    note: 'Elliptic caps and anticlastic saddles separated by K = 0 lines where asymptotic curves fade out.' },
  loft: { name: 'Free-form loft', cls: 'free', formula: ['Catmull–Rom through 4 × 6 points — drag them'], params: [],
    note: 'Drag the control points in the viewport; the surface, its curvature and the current net follow.' },
  file: { name: 'Your mesh', cls: 'free', formula: ['.obj  ·  .stl  ·  .ply'], params: [],
    note: 'Export from Rhino with _Export (OBJ, weld on) or drop a Weaverbird mesh; n-gons are triangulated and vertices welded here.' },
};

// ---------------------------------------------------------------------------
// Symbols: the surface drawn as iso-parameter wires in an isometric view
// ---------------------------------------------------------------------------

function enneperN(n, r, phi) {
  const m = 2 * n - 1, rm = Math.pow(r, m);
  return [r * cos(phi) - rm * cos(m * phi) / m, -r * sin(phi) - rm * sin(m * phi) / m, 2 * Math.pow(r, n) * cos(n * phi) / n];
}

const PARAM = {
  catenoid: { u: [0, 2 * PI], v: [-1.2, 1.2], f: (u, v) => [cosh(v) * cos(u), cosh(v) * sin(u), v], nu: 12, nv: 5 },
  enneper: { u: [-1.2, 1.2], v: [-1.2, 1.2], f: (u, v) => [u - u ** 3 / 3 + u * v * v, v - v ** 3 / 3 + v * u * u, u * u - v * v], rot: PI / 4, nu: 8, nv: 8 },
  enneper3: { u: [0, 2 * PI], v: [0.08, 0.9], f: (p, r) => enneperN(3, r, p), nu: 12, nv: 4 },
  saddle: { u: [-1, 1], v: [-1, 1], f: (u, v) => [u, v, u * u - v * v] },
  hyperboloid: { u: [0, 2 * PI], v: [-1, 1], f: (u, v) => [cosh(v) * cos(u), cosh(v) * sin(u), 1.2 * sinh(v)], nu: 12, nv: 5 },
  ruled: { u: [0, 1], v: [0, 1], f: (u, v) => {
    const P00 = [-1, -1, 0], P10 = [1, -1, 0.8], P01 = [-0.7, 1, 0.6], P11 = [1.3, 1, -0.5];
    return [0, 1, 2].map((k) => (1 - u) * (1 - v) * P00[k] + u * (1 - v) * P10[k] + (1 - u) * v * P01[k] + u * v * P11[k]);
  } },
  monkey: { u: [-1, 1], v: [-1, 1], f: (u, v) => [u, v, 0.8 * (u ** 3 - 3 * u * v * v)] },
  annulus: { u: [0, 2 * PI], v: [0.4, 1.2], f: (u, v) => { const x = v * cos(u), y = v * sin(u); return [x, y, 0.8 * (x * x - y * y)]; }, nu: 14, nv: 3 },
  sphere: { u: [0, 2 * PI], v: [0, PI], f: (u, v) => [sin(v) * cos(u), sin(v) * sin(u), -cos(v)], nu: 10, nv: 7 },
  dome: { u: [0, 2 * PI], v: [0, PI / 3], f: (u, v) => [sin(v) * cos(u), sin(v) * sin(u), cos(v)], nu: 12, nv: 4 },
  ellipsoid: { u: [0, 2 * PI], v: [0, PI], f: (u, v) => [1.5 * sin(v) * cos(u), sin(v) * sin(u), -0.7 * cos(v)], nu: 10, nv: 7 },
  vault: { u: [-1, 1], v: [0, 2.6], f: (x, y) => [x, y, sqrt(1.5 * 1.5 - x * x) - 1.5 + 0.35], nu: 6, nv: 7 },
  cone: { u: [0, 2 * PI], v: [0, 1], f: (u, v) => { const r = 1 + (0.15 - 1) * v; return [r * cos(u), r * sin(u), 1.5 * v]; }, nu: 12, nv: 4 },
  torus: { u: [0, 2 * PI], v: [0, 2 * PI], f: (u, v) => [(3 + cos(v)) * cos(u), (3 + cos(v)) * sin(u), sin(v)], nu: 16, nv: 8 },
  wave: { u: [-1, 1], v: [-1, 1], f: (u, v) => [u, v, 0.35 * sin(2 * u) * cos(2 * v)], nu: 8, nv: 8 },
  loft: { u: [-1, 1], v: [-1, 1], f: (u, v) => [u, v, 0.35 * sin(1.6 * u + 0.4) * cos(1.2 * v) + 0.15 * v], nu: 6, nv: 4 },
};

const ISO = (p) => [(p[0] - p[1]) * 0.866, (p[0] + p[1]) * 0.5 - p[2]];

function fitPaths(paths, size, pad) {
  let x0 = Infinity, x1 = -Infinity, y0 = Infinity, y1 = -Infinity;
  for (const pl of paths) for (const [x, y] of pl) { x0 = Math.min(x0, x); x1 = Math.max(x1, x); y0 = Math.min(y0, y); y1 = Math.max(y1, y); }
  const s = (size - 2 * pad) / Math.max(1e-9, x1 - x0, y1 - y0);
  const ox = (size - s * (x1 - x0)) / 2, oy = (size - s * (y1 - y0)) / 2;
  return paths.map((pl) => pl.map(([x, y]) => [ox + (x - x0) * s, oy + (y - y0) * s]));
}

const d = (pl) => pl.map(([x, y], i) => `${i ? 'L' : 'M'}${x.toFixed(1)} ${y.toFixed(1)}`).join('');

function wires(key) {
  const P = PARAM[key];
  const nu = P.nu ?? 7, nv = P.nv ?? 7, steps = 28;
  const lines = [], border = [];
  const cr = cos(P.rot || 0), sr = sin(P.rot || 0);
  const at = (a, b) => { const q = P.f(P.u[0] + a * (P.u[1] - P.u[0]), P.v[0] + b * (P.v[1] - P.v[0])); return ISO([cr * q[0] - sr * q[1], sr * q[0] + cr * q[1], q[2]]); };
  for (let i = 0; i <= nu; i++) { const pl = []; for (let k = 0; k <= steps; k++) pl.push(at(i / nu, k / steps)); (i === 0 || i === nu ? border : lines).push(pl); }
  for (let j = 0; j <= nv; j++) { const pl = []; for (let k = 0; k <= steps; k++) pl.push(at(k / steps, j / nv)); (j === 0 || j === nv ? border : lines).push(pl); }
  return { lines, border };
}

// a TPMS as stacked contour lines: its level set on horizontal slices of the
// cell, like a topographic drawing, with the cell's outline faint
function tpms(fn) {
  const L = PI, N = 26, K = 6, segs = [], cube = [];
  for (let k = 0; k <= K; k++) {
    const z = (k / K) * L;
    const val = (i, j) => fn([(i / N) * L, (j / N) * L, z]);
    for (let i = 0; i < N; i++)
      for (let j = 0; j < N; j++) {
        const c = [[i, j], [i + 1, j], [i + 1, j + 1], [i, j + 1]].map(([a, b]) => ({ a, b, v: val(a, b) }));
        const pts = [];
        for (let e = 0; e < 4; e++) {
          const p = c[e], q = c[(e + 1) % 4];
          if ((p.v < 0) !== (q.v < 0)) { const t = p.v / (p.v - q.v); pts.push([((p.a + t * (q.a - p.a)) / N) * L, ((p.b + t * (q.b - p.b)) / N) * L, z]); }
        }
        for (let m = 0; m + 1 < pts.length; m += 2) segs.push([ISO(pts[m]), ISO(pts[m + 1])]);
      }
  }
  const V = [[0, 0, 0], [L, 0, 0], [L, L, 0], [0, L, 0], [0, 0, L], [L, 0, L], [L, L, L], [0, L, L]];
  for (const [a, b] of [[0, 1], [1, 2], [2, 3], [3, 0], [4, 5], [5, 6], [6, 7], [7, 4], [0, 4], [1, 5], [2, 6], [3, 7]]) cube.push([ISO(V[a]), ISO(V[b])]);
  return { lines: segs, border: cube, faintBorder: true };
}

const TPMS = {
  schwarzd: ([x, y, z]) => sin(x) * sin(y) * sin(z) + sin(x) * cos(y) * cos(z) + cos(x) * sin(y) * cos(z) + cos(x) * cos(y) * sin(z),
  gyroid: ([x, y, z]) => sin(x) * cos(y) + sin(y) * cos(z) + sin(z) * cos(x),
};

const cache = new Map();

/** SVG markup of a shape's symbol, drawn in currentColor. */
export function symbolSVG(key, size = 44) {
  const id = key + ':' + size;
  if (cache.has(id)) return cache.get(id);
  let svg;
  if (key === 'file') {
    svg = `<svg viewBox="0 0 44 44" width="${size}" height="${size}" aria-hidden="true"><path d="M12 6h14l8 8v24H12z" fill="none" stroke="currentColor" stroke-width="1.3"/><path d="M26 6v8h8" fill="none" stroke="currentColor" stroke-width="1.1"/><path d="M16 32l5-10 5 6 4-7 3 11z M21 22l5 6 M16 32h17" fill="none" stroke="currentColor" stroke-width="0.9"/></svg>`;
  } else {
    const w = TPMS[key] ? tpms(TPMS[key]) : wires(key);
    const all = fitPaths([...w.border, ...w.lines], size, 3);
    const border = all.slice(0, w.border.length), lines = all.slice(w.border.length);
    const extra = key === 'loft'
      ? [[-1, -1], [0, -1], [1, -1], [-1, 1], [0, 1], [1, 1]].map(([u, v]) => ISO(PARAM.loft.f(u, v)))
      : null;
    let dots = '';
    if (extra) {
      const fitted = fitPaths([...w.border, ...w.lines, extra], size, 3).pop();
      dots = fitted.map(([x, y]) => `<circle cx="${x.toFixed(1)}" cy="${y.toFixed(1)}" r="1.6" fill="var(--neon)" stroke="currentColor" stroke-width="0.6"/>`).join('');
    }
    svg = `<svg viewBox="0 0 ${size} ${size}" width="${size}" height="${size}" aria-hidden="true">` +
      `<path d="${lines.map(d).join('')}" fill="none" stroke="currentColor" stroke-width="${w.faintBorder ? 0.9 : 0.7}" stroke-opacity="${w.faintBorder ? 0.9 : 0.55}" stroke-linejoin="round"/>` +
      `<path d="${border.map(d).join('')}" fill="none" stroke="currentColor" stroke-width="${w.faintBorder ? 0.6 : 1.2}" stroke-opacity="${w.faintBorder ? 0.35 : 1}" stroke-linejoin="round"/>${dots}</svg>`;
  }
  cache.set(id, svg);
  return svg;
}
