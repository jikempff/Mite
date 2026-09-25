// Free-form loft: S sections × P control points, interpolated with
// Catmull–Rom splines in both directions into a triangle grid. The control
// points are what the user drags in the viewport.

function catmullRom(p0, p1, p2, p3, t) {
  const t2 = t * t, t3 = t2 * t;
  return [0, 1, 2].map((k) => 0.5 * ((2 * p1[k]) + (-p0[k] + p2[k]) * t + (2 * p0[k] - 5 * p1[k] + 4 * p2[k] - p3[k]) * t2 + (-p0[k] + 3 * p1[k] - 3 * p2[k] + p3[k]) * t3));
}

function splineSample(ctrl, n) { // open Catmull–Rom through all control points, n samples
  const m = ctrl.length, out = [];
  for (let s = 0; s < n; s++) {
    const u = (s / (n - 1)) * (m - 1);
    const i = Math.min(m - 2, Math.floor(u)), t = u - i;
    const p0 = ctrl[Math.max(0, i - 1)], p1 = ctrl[i], p2 = ctrl[i + 1], p3 = ctrl[Math.min(m - 1, i + 2)];
    out.push(catmullRom(p0, p1, p2, p3, t));
  }
  return out;
}

export class Loft {
  constructor(sections = 4, points = 6, size = 3) {
    this.sections = sections;
    this.points = points;
    this.ctrl = [];
    for (let s = 0; s < sections; s++) {
      const row = [];
      const y = -size / 2 + (size * s) / (sections - 1);
      for (let p = 0; p < points; p++) {
        const x = -size / 2 + (size * p) / (points - 1);
        const z = 0.45 * Math.sin((p / (points - 1)) * Math.PI * 1.6 + s * 0.7) * Math.cos(s * 0.9) + 0.25 * Math.cos(x * 1.3) * (s - 1.5) * 0.4;
        row.push([x, y, z]);
      }
      this.ctrl.push(row);
    }
  }

  flatControls() { return this.ctrl.flat(); }

  setControl(id, p) {
    const s = Math.floor(id / this.points), k = id % this.points;
    this.ctrl[s][k] = p.slice();
  }

  /** @returns {{vertices: Float32Array, triangles: Uint32Array}} */
  build(nu = 40, nv = 30) {
    // sample each section along u, then interpolate across sections along v
    const rows = this.ctrl.map((row) => splineSample(row, nu + 1));
    const cols = [];
    for (let i = 0; i <= nu; i++) cols.push(splineSample(rows.map((r) => r[i]), nv + 1));
    const vertices = new Float32Array((nu + 1) * (nv + 1) * 3);
    for (let j = 0; j <= nv; j++)
      for (let i = 0; i <= nu; i++) {
        const p = cols[i][j], k = (j * (nu + 1) + i) * 3;
        vertices[k] = p[0]; vertices[k + 1] = p[1]; vertices[k + 2] = p[2];
      }
    const tris = [];
    for (let j = 0; j < nv; j++)
      for (let i = 0; i < nu; i++) {
        const a = j * (nu + 1) + i, b = a + 1, c = b + nu + 1, d = a + nu + 1;
        tris.push(a, b, c, a, c, d);
      }
    return { vertices, triangles: Uint32Array.from(tris) };
  }
}
