// Web Worker hosting the .NET/WebAssembly kernel so long traces never block
// the page. Messages: {id, method, args} → {id, result} | {id, error};
// boot progress arrives as {progress, name}; the percent done of a long solve as {working, stage}.

import { bootMite } from './runtime.js';

let mite = null;

self.onmessage = async (e) => {
  const { id, method, args } = e.data;
  try {
    if (method === 'boot') {
      mite = await bootMite((n, name) => self.postMessage({ progress: n, name }), (working, stage) => self.postMessage({ working, stage }));
      self.postMessage({ id, result: { bootMs: mite.bootMs, version: mite.version() } });
      return;
    }
    if (!mite) throw new Error('kernel not booted');
    const r = mite[method](...(args || []));
    const transfer = [];
    if (r && r.buffer instanceof ArrayBuffer && ArrayBuffer.isView(r)) transfer.push(r.buffer);
    self.postMessage({ id, result: r }, transfer);
  } catch (err) {
    self.postMessage({ id, error: String(err?.message || err) });
  }
};
