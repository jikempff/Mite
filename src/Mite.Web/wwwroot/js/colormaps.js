// Colour scales for per-vertex analysis. Diverging maps are symmetric about
// zero and clipped at a percentile so a few extreme vertices do not wash out
// the rest; sequential maps run from the minimum to a percentile maximum.

function lerp(a, b, t) { return a + (b - a) * t; }
function mix(c0, c1, t) { return [lerp(c0[0], c1[0], t), lerp(c0[1], c1[1], t), lerp(c0[2], c1[2], t)]; }

// Stops in 0..1 → rgb 0..1
const DIVERGING = [ // blue – light – red (colour-blind safe, print safe)
  [0.00, [0.13, 0.31, 0.58]],
  [0.25, [0.42, 0.62, 0.84]],
  [0.50, [0.95, 0.95, 0.93]],
  [0.75, [0.90, 0.50, 0.40]],
  [1.00, [0.60, 0.10, 0.14]],
];
const SEQUENTIAL = [ // deep teal → sand (light theme friendly)
  [0.00, [0.05, 0.22, 0.30]],
  [0.35, [0.06, 0.45, 0.50]],
  [0.65, [0.45, 0.72, 0.60]],
  [1.00, [0.97, 0.91, 0.70]],
];

export function sample(stops, t) {
  t = Math.max(0, Math.min(1, t));
  for (let i = 1; i < stops.length; i++) {
    if (t <= stops[i][0]) {
      const [t0, c0] = stops[i - 1], [t1, c1] = stops[i];
      return mix(c0, c1, (t - t0) / Math.max(1e-9, t1 - t0));
    }
  }
  return stops[stops.length - 1][1];
}

export function percentile(values, p, abs = false) {
  const v = Array.from(values, abs ? Math.abs : (x) => x).filter((x) => Number.isFinite(x)).sort((a, b) => a - b);
  if (!v.length) return 0;
  const i = Math.min(v.length - 1, Math.max(0, Math.floor(p * (v.length - 1))));
  return v[i];
}

/** Diverging colouring about zero; returns {colors: Float32Array(n*3), min, max, range} */
export function diverging(values, clipPercentile = 0.97) {
  const r = percentile(values, clipPercentile, true) || 1e-9;
  const n = values.length, out = new Float32Array(n * 3);
  for (let i = 0; i < n; i++) {
    const t = 0.5 + 0.5 * Math.max(-1, Math.min(1, values[i] / r));
    const c = sample(DIVERGING, t);
    out[3 * i] = c[0]; out[3 * i + 1] = c[1]; out[3 * i + 2] = c[2];
  }
  return { colors: out, min: -r, max: r, stops: DIVERGING, kind: 'diverging' };
}

/** Sequential colouring from min to a percentile maximum */
export function sequential(values, lo = null, hi = null, clipPercentile = 0.97) {
  const finite = Array.from(values).filter(Number.isFinite);
  const min = lo ?? Math.min(...finite);
  const max = hi ?? percentile(finite, clipPercentile);
  const n = values.length, out = new Float32Array(n * 3);
  for (let i = 0; i < n; i++) {
    const t = (values[i] - min) / Math.max(1e-12, max - min);
    const c = Number.isFinite(values[i]) ? sample(SEQUENTIAL, t) : [0.6, 0.6, 0.6];
    out[3 * i] = c[0]; out[3 * i + 1] = c[1]; out[3 * i + 2] = c[2];
  }
  return { colors: out, min, max, stops: SEQUENTIAL, kind: 'sequential' };
}

/** Utilization: green below 0.7, amber to 1, red above, grey for NaN */
export function utilizationColor(u) {
  if (!Number.isFinite(u)) return [0.6, 0.6, 0.6];
  if (u < 0.7) return mix([0.10, 0.55, 0.35], [0.55, 0.75, 0.20], u / 0.7);
  if (u < 1.0) return mix([0.55, 0.75, 0.20], [0.95, 0.60, 0.10], (u - 0.7) / 0.3);
  if (u < 1.3) return mix([0.95, 0.60, 0.10], [0.80, 0.15, 0.15], (u - 1.0) / 0.3);
  return [0.55, 0.05, 0.10];
}

export function cssRgb(c) { return `rgb(${Math.round(c[0] * 255)},${Math.round(c[1] * 255)},${Math.round(c[2] * 255)})`; }

export function legendGradient(stops) {
  return 'linear-gradient(90deg,' + stops.map(([t, c]) => `${cssRgb(c)} ${Math.round(t * 100)}%`).join(',') + ')';
}
