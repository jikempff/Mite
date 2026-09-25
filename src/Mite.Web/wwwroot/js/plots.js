// Small 2D canvases: the lath curvature plot (kn, kg, tg, utilization along
// arc length) and the unrolled cutting pattern.

import { utilizationColor, cssRgb } from './colormaps.js';

function setup(canvas) {
  const dpr = Math.min(2, window.devicePixelRatio || 1);
  const w = canvas.clientWidth, h = canvas.clientHeight;
  if (canvas.width !== Math.round(w * dpr) || canvas.height !== Math.round(h * dpr)) { canvas.width = Math.round(w * dpr); canvas.height = Math.round(h * dpr); }
  const ctx = canvas.getContext('2d');
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  ctx.clearRect(0, 0, w, h);
  return { ctx, w, h };
}

function css(name) { return getComputedStyle(document.documentElement).getPropertyValue(name).trim(); }

export function drawLathPlot(canvas, lath) {
  const { ctx, w, h } = setup(canvas);
  if (!lath) return;
  const pad = { l: 34, r: 10, t: 10, b: 22 };
  const pw = w - pad.l - pad.r, ph = h - pad.t - pad.b;
  const arc = lath.arc, L = arc[arc.length - 1] || 1;
  const series = [
    { v: lath.kn, color: css('--famA') || '#0f766e', label: 'kn' },
    { v: lath.kg, color: css('--famB') || '#9d2f6b', label: 'kg' },
    { v: lath.tg, color: css('--muted') || '#6b7280', label: 'tg' },
  ];
  let amax = 1e-9;
  for (const s of series) for (const x of s.v) if (Number.isFinite(x)) amax = Math.max(amax, Math.abs(x));
  const X = (a) => pad.l + (a / L) * pw;
  const Y = (v) => pad.t + ph / 2 - (v / amax) * (ph / 2) * 0.92;

  // utilization band along the bottom (green→red)
  const bandH = 5;
  for (let i = 0; i + 1 < arc.length; i++) {
    ctx.fillStyle = cssRgb(utilizationColor(lath.utilization[i]));
    ctx.fillRect(X(arc[i]), h - pad.b + 4, Math.max(1, X(arc[i + 1]) - X(arc[i]) + 0.5), bandH);
  }
  // axes
  ctx.strokeStyle = css('--line') || '#ddd'; ctx.lineWidth = 1;
  ctx.beginPath(); ctx.moveTo(pad.l, Y(0)); ctx.lineTo(w - pad.r, Y(0)); ctx.stroke();
  ctx.fillStyle = css('--muted') || '#6b7280'; ctx.font = '10px Sohne, Helvetica, Arial, sans-serif'; ctx.textAlign = 'right';
  ctx.fillText((+amax.toPrecision(2)).toString(), pad.l - 4, pad.t + 8);
  ctx.fillText('0', pad.l - 4, Y(0) + 3);
  ctx.fillText((-amax).toPrecision(2), pad.l - 4, pad.t + ph);
  ctx.textAlign = 'left';
  ctx.fillText('0', pad.l, h - pad.b + 18); ctx.textAlign = 'right'; ctx.fillText('L = ' + L.toFixed(3), w - pad.r, h - pad.b + 18);
  // series
  for (const s of series) {
    ctx.strokeStyle = s.color; ctx.lineWidth = 1.6; ctx.beginPath();
    let started = false;
    for (let i = 0; i < arc.length; i++) {
      const v = s.v[i]; if (!Number.isFinite(v)) { started = false; continue; }
      const x = X(arc[i]), y = Y(v);
      if (!started) { ctx.moveTo(x, y); started = true; } else ctx.lineTo(x, y);
    }
    ctx.stroke();
  }
  // legend
  ctx.font = '11px Sohne, Helvetica, Arial, sans-serif'; ctx.textAlign = 'left';
  let lx = pad.l + 4;
  for (const s of series) { ctx.fillStyle = s.color; ctx.fillRect(lx, pad.t, 12, 3); ctx.fillStyle = css('--muted') || '#6b7280'; ctx.fillText(s.label, lx + 16, pad.t + 5); lx += 44; }
}

export function drawUnroll(canvas, lath) {
  const { ctx, w, h } = setup(canvas);
  if (!lath || !lath.unrollA?.length) return;
  const pts = [...lath.unrollA, ...lath.unrollB];
  let minX = Infinity, maxX = -Infinity, minY = Infinity, maxY = -Infinity;
  for (const [x, y] of pts) { minX = Math.min(minX, x); maxX = Math.max(maxX, x); minY = Math.min(minY, y); maxY = Math.max(maxY, y); }
  const pad = 10;
  const sx = (w - 2 * pad) / Math.max(1e-9, maxX - minX), sy = (h - 2 * pad) / Math.max(1e-9, maxY - minY);
  const s = Math.min(sx, sy);
  const ox = pad + ((w - 2 * pad) - s * (maxX - minX)) / 2, oy = pad + ((h - 2 * pad) - s * (maxY - minY)) / 2;
  const P = ([x, y]) => [ox + (x - minX) * s, h - (oy + (y - minY) * s)];
  ctx.fillStyle = css('--surface2') || '#f3f4f6';
  ctx.beginPath();
  lath.unrollA.forEach((p, i) => { const q = P(p); i ? ctx.lineTo(q[0], q[1]) : ctx.moveTo(q[0], q[1]); });
  [...lath.unrollB].reverse().forEach((p) => { const q = P(p); ctx.lineTo(q[0], q[1]); });
  ctx.closePath(); ctx.fill();
  ctx.strokeStyle = css('--famA') || '#0f766e'; ctx.lineWidth = 1.5; ctx.stroke();
  ctx.strokeStyle = css('--muted') || '#6b7280'; ctx.setLineDash([4, 3]); ctx.beginPath();
  lath.unrollCenter.forEach((p, i) => { const q = P(p); i ? ctx.lineTo(q[0], q[1]) : ctx.moveTo(q[0], q[1]); });
  ctx.stroke(); ctx.setLineDash([]);
  ctx.fillStyle = css('--muted') || '#6b7280'; ctx.font = '10px Sohne, Helvetica, Arial, sans-serif';
  ctx.fillText(`flat ${lath.flatLength.toFixed(3)} × bow ${lath.bow.toFixed(3)}`, pad, h - 4);
}
