// Downloads: mesh and curves as OBJ, a lath pattern as SVG, the view as PNG,
// and the Grasshopper recipe as text.

function download(name, blob) {
  const a = document.createElement('a');
  a.href = URL.createObjectURL(blob);
  a.download = name;
  document.body.appendChild(a);
  a.click();
  setTimeout(() => { URL.revokeObjectURL(a.href); a.remove(); }, 500);
}

export function meshToOBJ(vertices, triangles, name = 'mite-mesh') {
  const out = [`# Mite ${name}`, `o ${name}`];
  for (let i = 0; i < vertices.length; i += 3) out.push(`v ${vertices[i]} ${vertices[i + 1]} ${vertices[i + 2]}`);
  for (let i = 0; i < triangles.length; i += 3) out.push(`f ${triangles[i] + 1} ${triangles[i + 1] + 1} ${triangles[i + 2] + 1}`);
  return out.join('\n') + '\n';
}

export function curvesToOBJ(familyA, familyB) {
  const out = ['# Mite curve families as polylines (l records)'];
  let base = 1;
  const emit = (curves, tag) => {
    curves.forEach((pts, k) => {
      out.push(`o ${tag}_${String(k).padStart(3, '0')}`);
      for (const p of pts) out.push(`v ${p[0]} ${p[1]} ${p[2]}`);
      out.push('l ' + pts.map((_, i) => base + i).join(' '));
      base += pts.length;
    });
  };
  emit(familyA || [], 'A');
  emit(familyB || [], 'B');
  return out.join('\n') + '\n';
}

export function unrollToSVG(lath, unitsPerMm = 1) {
  const pts = [...lath.unrollA, ...lath.unrollB];
  let minX = Infinity, maxX = -Infinity, minY = Infinity, maxY = -Infinity;
  for (const [x, y] of pts) { minX = Math.min(minX, x); maxX = Math.max(maxX, x); minY = Math.min(minY, y); maxY = Math.max(maxY, y); }
  const s = 1 / unitsPerMm; // model units → mm
  const W = (maxX - minX) * s, H = (maxY - minY) * s;
  const P = ([x, y]) => `${((x - minX) * s).toFixed(3)},${((maxY - y) * s).toFixed(3)}`;
  const outline = [...lath.unrollA.map(P), ...[...lath.unrollB].reverse().map(P)].join(' ');
  const center = lath.unrollCenter.map(P).join(' ');
  return `<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" width="${W.toFixed(2)}mm" height="${H.toFixed(2)}mm" viewBox="0 0 ${W.toFixed(3)} ${H.toFixed(3)}">
  <polygon points="${outline}" fill="none" stroke="#000" stroke-width="0.2"/>
  <polyline points="${center}" fill="none" stroke="#888" stroke-width="0.1" stroke-dasharray="2 1"/>
</svg>
`;
}

export const save = {
  text: (name, text, type = 'text/plain') => download(name, new Blob([text], { type })),
  png: (name, dataUrl) => { const a = document.createElement('a'); a.href = dataUrl; a.download = name; a.click(); },
};
