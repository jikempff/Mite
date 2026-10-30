// Colour scales for per-vertex analysis — Mite's shared colour language.
// The tables come from Mite.Core (ColourMap, generated into colormap-lut.js by
// the bench generator): the same diverging / sequential / utilisation colours
// the Grasshopper plugin (Mesh Colour Map, Lath Preview) and the bench page use.
// Diverging maps are symmetric about zero on the robust 2 %–98 % percentile
// band (as Mesh Colour Map's automatic domain), so a few extreme vertices do
// not wash out the rest; sequential maps run from the minimum to a percentile
// maximum. Legend ticks follow Heckbert's nice numbers (ticks / label).

import { DIVERGING_LUT, SEQUENTIAL_LUT, UTILISATION_LUT, UTILISATION_OVER, NAN_GREY,
         diverging as divergingHex, sequential as sequentialHex, utilisation as utilisationHex, hexToRgb,
         ticks, tickStep, decimals, label } from './colormap-lut.js';

function lerp(a, b, t) { return a + (b - a) * t; }
function mix(c0, c1, t) { return [lerp(c0[0], c1[0], t), lerp(c0[1], c1[1], t), lerp(c0[2], c1[2], t)]; }

// legend stops ([t, rgb 0..1]) from a table, every 16th entry
function stopsOf(lut) { const s = []; for (let i = 0; i < lut.length; i += 16) s.push([i / (lut.length - 1), hexToRgb(lut[i])]); if ((lut.length - 1) % 16) s.push([1, hexToRgb(lut[lut.length - 1])]); return s; }
export const DIVERGING = stopsOf(DIVERGING_LUT);
export const SEQUENTIAL = stopsOf(SEQUENTIAL_LUT);

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

/** Diverging colouring about zero on the symmetric 2 %–98 % band (Mesh Colour Map's rule); returns {colors: Float32Array(n*3), min, max} */
export function diverging(values, clipPercentile = 0.98) {
  const lo = percentile(values, 1 - clipPercentile), hi = percentile(values, clipPercentile);
  const r = Math.max(Math.abs(lo), Math.abs(hi)) || 1e-9;
  const n = values.length, out = new Float32Array(n * 3);
  for (let i = 0; i < n; i++) {
    const v = values[i];
    const c = Number.isFinite(v) ? hexToRgb(divergingHex((v + r) / (2 * r))) : hexToRgb(NAN_GREY);
    out[3 * i] = c[0]; out[3 * i + 1] = c[1]; out[3 * i + 2] = c[2];
  }
  return { colors: out, min: -r, max: r, stops: DIVERGING, kind: 'diverging' };
}

/** Sequential colouring from min to a percentile maximum */
export function sequential(values, lo = null, hi = null, clipPercentile = 0.98) {
  const finite = Array.from(values).filter(Number.isFinite);
  const min = lo ?? Math.min(...finite);
  const max = hi ?? percentile(finite, clipPercentile);
  const n = values.length, out = new Float32Array(n * 3);
  for (let i = 0; i < n; i++) {
    const t = (values[i] - min) / Math.max(1e-12, max - min);
    const c = Number.isFinite(values[i]) ? hexToRgb(sequentialHex(t)) : hexToRgb(NAN_GREY);
    out[3 * i] = c[0]; out[3 * i + 1] = c[1]; out[3 * i + 2] = c[2];
  }
  return { colors: out, min, max, stops: SEQUENTIAL, kind: 'sequential' };
}

/** Utilisation: green below 0.7, neon yellow at 1, neon red just above, dark red to 1.3 and beyond, grey for NaN — the table Lath Preview uses */
export const UTIL_STOPS = (() => { const s = []; const n = UTILISATION_LUT.length; for (let i = 0; i < n; i += 10) { if (i === 200) s.push([199 / n, hexToRgb(UTILISATION_LUT[199])]); s.push([i / n, hexToRgb(UTILISATION_LUT[i])]); } s.push([1, hexToRgb(UTILISATION_OVER)]); return s; })(); // over 0 … 1.3, the step kept sharp at 1
export function utilizationColor(u) { return hexToRgb(utilisationHex(u)); }

export function cssRgb(c) { return `rgb(${Math.round(c[0] * 255)},${Math.round(c[1] * 255)},${Math.round(c[2] * 255)})`; }

export function legendGradient(stops) {
  return 'linear-gradient(90deg,' + stops.map(([t, c]) => `${cssRgb(c)} ${(t * 100).toFixed(1)}%`).join(',') + ')';
}

/** Nice ticks inside [lo, hi] for a legend bar: [{pos: 0..1, label}] (Heckbert 1990; 0 is always a tick when inside) */
export function legendTicks(lo, hi) {
  if (!(hi > lo)) return [];
  const d = decimals(tickStep(lo, hi));
  return ticks(lo, hi).map((t) => ({ pos: (t - lo) / (hi - lo), label: label(t, d) }));
}

export { ticks, tickStep, decimals, label };
