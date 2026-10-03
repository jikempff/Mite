// Colour scales for per-vertex analysis. Diverging maps are symmetric about
// zero and clipped at a percentile so a few extreme vertices do not wash out
// the rest; sequential maps run from the minimum to a percentile maximum.

function lerp(a, b, t) { return a + (b - a) * t; }
function mix(c0, c1, t) { return [lerp(c0[0], c1[0], t), lerp(c0[1], c1[1], t), lerp(c0[2], c1[2], t)]; }

// Stops in 0..1 → rgb 0..1. The values are Mite.Core's MitePalette (one
// palette for Grasshopper, this app and the bench; PaletteTests checks that
// this file matches it): evenly spaced stops cut at equal CIELAB L* steps, so
// lightness changes at a uniform rate (Kovesi 2015) and both ends of the
// diverging map have the same lightness (L* 39.5).
const evenStops = (cols) => cols.map((c, i) => [i / (cols.length - 1), c.map((x) => x / 255)]);
const DIVERGING = evenStops([ // neon blue (family A) – white – crimson
  [42, 60, 255], [83, 98, 253], [118, 130, 252], [151, 159, 251], [183, 189, 249], [215, 218, 248],
  [247, 247, 247],
  [239, 213, 221], [231, 179, 194], [223, 144, 166], [214, 108, 138], [204, 66, 105], [188, 0, 53],
]);
const SEQUENTIAL = evenStops([ // black → neon blue → neon green → white
  [10, 10, 10], [18, 23, 73], [24, 33, 122], [30, 44, 172], [37, 54, 224], [34, 89, 229],
  [24, 127, 192], [16, 157, 164], [8, 185, 137], [1, 212, 112], [93, 231, 160], [176, 244, 208],
  [245, 255, 247],
]);

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

/** Utilization in neon: green below 0.7, neon yellow at 1, neon red above, grey for NaN */
export const UTIL_STOPS = [[0, [0.00, 0.85, 0.42]], [0.54, [0.55, 0.95, 0.10]], [0.769, [0.85, 1.00, 0.00]], [0.77, [1.00, 0.12, 0.31]], [1, [0.75, 0.02, 0.18]]]; // over 0 … 1.3
export function utilizationColor(u) {
  if (!Number.isFinite(u)) return [0.6, 0.6, 0.6];
  if (u < 0.7) return mix([0.00, 0.85, 0.42], [0.55, 0.95, 0.10], u / 0.7);
  if (u < 1.0) return mix([0.55, 0.95, 0.10], [0.85, 1.00, 0.00], (u - 0.7) / 0.3);
  if (u < 1.3) return mix([1.00, 0.12, 0.31], [0.75, 0.02, 0.18], (u - 1.0) / 0.3);
  return [0.35, 0.00, 0.08];
}

export function cssRgb(c) { return `rgb(${Math.round(c[0] * 255)},${Math.round(c[1] * 255)},${Math.round(c[2] * 255)})`; }

export function legendGradient(stops) {
  return 'linear-gradient(90deg,' + stops.map(([t, c]) => `${cssRgb(c)} ${Math.round(t * 100)}%`).join(',') + ')';
}
