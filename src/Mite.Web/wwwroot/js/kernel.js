// Main-thread client of the kernel worker. Every call returns a promise;
// calls are queued in the worker in order. `restart()` kills a running
// computation (the worker is terminated and rebooted) — WebAssembly cannot
// be interrupted without shared memory, which static hosts cannot enable.

export class Kernel {
  constructor() { this.worker = null; this.pending = new Map(); this.nextId = 1; this.onProgress = null; this.busyCount = 0; }

  async boot(onProgress) {
    this.onProgress = onProgress;
    this.worker = new Worker(new URL('./worker.js', import.meta.url), { type: 'module' });
    this.worker.onmessage = (e) => {
      const d = e.data;
      if (d.progress !== undefined) { this.onProgress?.(d.progress, d.name); return; }
      const p = this.pending.get(d.id);
      if (!p) return;
      this.pending.delete(d.id);
      this.busyCount--;
      if (d.error) p.reject(new Error(d.error)); else p.resolve(d.result);
    };
    this.worker.onerror = (e) => { for (const p of this.pending.values()) p.reject(new Error(e.message || 'worker error')); this.pending.clear(); this.busyCount = 0; };
    return this.call('boot');
  }

  call(method, ...args) {
    const id = this.nextId++;
    this.busyCount++;
    return new Promise((resolve, reject) => {
      this.pending.set(id, { resolve, reject });
      this.worker.postMessage({ id, method, args });
    });
  }

  get busy() { return this.busyCount > 0; }

  /** Abort everything in flight and reboot. Returns when the new worker is ready. */
  async restart(onProgress) {
    this.worker?.terminate();
    for (const p of this.pending.values()) p.reject(new Error('cancelled'));
    this.pending.clear();
    this.busyCount = 0;
    return this.boot(onProgress || this.onProgress);
  }
}
