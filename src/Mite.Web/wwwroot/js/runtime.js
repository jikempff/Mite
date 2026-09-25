// Boots the .NET WebAssembly runtime that carries Mite.Core and wraps the
// exported API (Api.cs) so the rest of the page works with plain objects.
//
// Static hosts (GitHub Pages) do not compress .wasm, so the published .gz
// companions are fetched instead and inflated in the browser with
// DecompressionStream: ~2 MB over the wire instead of ~6 MB.

import { dotnet } from '../_framework/dotnet.js';

const GZ_TYPES = new Set(['assembly', 'pdb', 'dotnetwasm', 'js-module-native', 'js-module-runtime', 'js-module-threads', 'icu', 'vfs', 'symbols']);

async function fetchGz(uri) {
  const gz = await fetch(uri + '.gz', { cache: 'force-cache' });
  if (!gz.ok) return fetch(uri);
  const buf = await gz.arrayBuffer();
  const bytes = new Uint8Array(buf);
  let body;
  if (bytes.length > 2 && bytes[0] === 0x1f && bytes[1] === 0x8b && typeof DecompressionStream !== 'undefined') {
    body = new Response(buf).body.pipeThrough(new DecompressionStream('gzip'));
  } else {
    body = buf; // host already decoded it (Content-Encoding) or no DecompressionStream
  }
  const type = uri.endsWith('.wasm') ? 'application/wasm' : uri.endsWith('.js') ? 'text/javascript' : uri.endsWith('.json') ? 'application/json' : 'application/octet-stream';
  return new Response(body, { status: 200, headers: { 'Content-Type': type } });
}

export async function bootMite(onProgress) {
  const t0 = performance.now();
  let loaded = 0;
  const runtime = await dotnet
    .withDiagnosticTracing(false)
    .withConfig({ disableIntegrityCheck: true })
    .withResourceLoader((type, name, defaultUri, integrity, behavior) => {
      loaded++;
      onProgress?.(loaded, name);
      if (GZ_TYPES.has(type)) return fetchGz(defaultUri);
      return defaultUri;
    })
    .create();
  const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
  const api = exports.Mite.Web.MiteApi;
  const bootMs = Math.round(performance.now() - t0);

  const J = (s) => JSON.parse(s);
  return {
    bootMs,
    version: () => api.Version(),
    shape: (name, p1, p2, p3, res) => J(api.Shape(name, p1, p2, p3, res)),
    loadMesh: (vertices, triangles, weld) => J(api.LoadMesh(Array.from(vertices), Array.from(triangles), !!weld)),
    vertices: () => Float32Array.from(api.Vertices()),
    triangles: () => Uint32Array.from(api.Triangles()),
    nearestVertex: (x, y, z) => api.NearestVertex(x, y, z),
    curvature: (radius, umbilicTol) => J(api.Curvature(radius, umbilicTol)),
    net: (kind, opts) => J(api.Net(kind, JSON.stringify(opts))),
    lath: (poly, width, thickness, upright, maxStrain, sweep) => J(api.Lath(Array.from(poly), width, thickness, !!upright, maxStrain, !!sweep)),
    frame: (width, thickness, upright, load, toMetres, sampling) => J(api.Frame(width, thickness, !!upright, load, toMetres, sampling)),
  };
}
