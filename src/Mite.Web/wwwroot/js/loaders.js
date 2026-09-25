// Minimal OBJ / STL / PLY(ascii) readers → { vertices: Float32Array, triangles: Uint32Array }.
// N-gons are fan-triangulated; welding happens in Mite.Core (Mesh Cleanup).

export function parseOBJ(text) {
  const v = [], f = [];
  const lines = text.split(/\r?\n/);
  for (const line of lines) {
    if (line.startsWith('v ')) {
      const p = line.trim().split(/\s+/);
      v.push(+p[1], +p[2], +p[3]);
    } else if (line.startsWith('f ')) {
      const idx = line.trim().split(/\s+/).slice(1).map((t) => {
        const i = parseInt(t.split('/')[0], 10);
        return i < 0 ? v.length / 3 + i : i - 1;
      });
      for (let k = 1; k + 1 < idx.length; k++) f.push(idx[0], idx[k], idx[k + 1]);
    }
  }
  return { vertices: Float32Array.from(v), triangles: Uint32Array.from(f) };
}

export function parseSTL(buffer) {
  const bytes = new Uint8Array(buffer);
  const head = new TextDecoder().decode(bytes.subarray(0, 80)).trim().toLowerCase();
  const dv = new DataView(buffer);
  const isBinary = !(head.startsWith('solid') && bytes.length < 84 + 50) && bytes.length >= 84 && 84 + 50 * dv.getUint32(80, true) === bytes.length;
  const v = [], f = [];
  if (isBinary) {
    const n = dv.getUint32(80, true);
    let o = 84;
    for (let i = 0; i < n; i++) {
      o += 12;
      for (let k = 0; k < 3; k++) { v.push(dv.getFloat32(o, true), dv.getFloat32(o + 4, true), dv.getFloat32(o + 8, true)); o += 12; }
      o += 2;
      f.push(3 * i, 3 * i + 1, 3 * i + 2);
    }
  } else {
    const text = new TextDecoder().decode(bytes);
    const re = /vertex\s+([-+\d.eE]+)\s+([-+\d.eE]+)\s+([-+\d.eE]+)/g;
    let m;
    while ((m = re.exec(text))) v.push(+m[1], +m[2], +m[3]);
    for (let i = 0; i + 2 < v.length / 3; i += 3) f.push(i, i + 1, i + 2);
  }
  return { vertices: Float32Array.from(v), triangles: Uint32Array.from(f) };
}

export function parsePLY(text) {
  const lines = text.split(/\r?\n/);
  let nv = 0, nf = 0, i = 0, props = [];
  for (; i < lines.length; i++) {
    const l = lines[i].trim();
    if (l.startsWith('element vertex')) nv = +l.split(/\s+/)[2];
    else if (l.startsWith('element face')) nf = +l.split(/\s+/)[2];
    else if (l.startsWith('property') && nf === 0) props.push(l.split(/\s+/)[2]);
    else if (l === 'end_header') { i++; break; }
  }
  const xi = props.indexOf('x'), yi = props.indexOf('y'), zi = props.indexOf('z');
  const v = [], f = [];
  for (let k = 0; k < nv; k++, i++) { const p = lines[i].trim().split(/\s+/); v.push(+p[xi], +p[yi], +p[zi]); }
  for (let k = 0; k < nf; k++, i++) {
    const p = lines[i].trim().split(/\s+/).map(Number);
    for (let t = 2; t < p[0]; t++) f.push(p[1], p[t], p[t + 1]);
  }
  return { vertices: Float32Array.from(v), triangles: Uint32Array.from(f) };
}

export async function readMeshFile(file) {
  const name = file.name.toLowerCase();
  if (name.endsWith('.obj')) return parseOBJ(await file.text());
  if (name.endsWith('.stl')) return parseSTL(await file.arrayBuffer());
  if (name.endsWith('.ply')) return parsePLY(await file.text());
  throw new Error('Unsupported file type. Use .obj, .stl or .ply (export from Rhino with _Export).');
}
