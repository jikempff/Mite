// three.js viewport: the analysed mesh with per-vertex colours or a zebra
// shader, curve families as fat lines, crossing points, umbilics, principal
// direction glyphs, a selected lath (with optional swept solid), the
// deformed net, and draggable loft control points.

import * as THREE from 'three';
import { OrbitControls } from '../vendor/OrbitControls.js';
import { Line2 } from '../vendor/lines/Line2.js';
import { LineMaterial } from '../vendor/lines/LineMaterial.js';
import { LineGeometry } from '../vendor/lines/LineGeometry.js';
import { LineSegments2 } from '../vendor/lines/LineSegments2.js';
import { LineSegmentsGeometry } from '../vendor/lines/LineSegmentsGeometry.js';

export const FAMILY_A = 0x0f766e; // teal
export const FAMILY_B = 0x9d2f6b; // plum
const SELECT = 0xf59e0b;

export class Viewer {
  constructor(canvas) {
    this.canvas = canvas;
    this.renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true, preserveDrawingBuffer: true });
    this.renderer.setPixelRatio(Math.min(2, window.devicePixelRatio || 1));
    this.scene = new THREE.Scene();
    this.camera = new THREE.PerspectiveCamera(35, 1, 0.01, 1000);
    this.camera.up.set(0, 0, 1);
    this.controls = new OrbitControls(this.camera, canvas);
    this.controls.enableDamping = true;
    this.controls.dampingFactor = 0.12;

    const hemi = new THREE.HemisphereLight(0xffffff, 0x8a8f99, 1.1);
    const key = new THREE.DirectionalLight(0xffffff, 1.4);
    key.position.set(3, -4, 6);
    const fill = new THREE.DirectionalLight(0xffffff, 0.5);
    fill.position.set(-4, 3, 2);
    this.scene.add(hemi, key, fill);

    this.root = new THREE.Group();
    this.scene.add(this.root);
    this.curvesGroup = new THREE.Group();
    this.overlayGroup = new THREE.Group();
    this.handlesGroup = new THREE.Group();
    this.root.add(this.curvesGroup, this.overlayGroup, this.handlesGroup);

    this.mesh = null;
    this.wire = null;
    this.curveObjects = []; // {line, family, index, points}
    this.lineMaterials = [];
    this.selected = null;
    this.onPickCurve = null;
    this.onPickVertex = null;
    this.onHandleDrag = null;
    this.onHandleDragEnd = null;
    this.raycaster = new THREE.Raycaster();
    this.raycaster.params.Line2 = { threshold: 6 };
    this.pointer = new THREE.Vector2();
    this.dragging = null;
    this.dark = false;
    this.zebra = false;
    this.sceneRadius = 1;

    this._bindPointer();
    const ro = new ResizeObserver(() => this.resize());
    ro.observe(canvas.parentElement);
    this.resize();
    this.renderer.setAnimationLoop(() => this.render());
  }

  setTheme(dark) {
    this.dark = dark;
    this.renderer.setClearColor(0x000000, 0);
    if (this.mesh) this.mesh.material.needsUpdate = true;
  }

  resize() {
    const el = this.canvas.parentElement;
    const w = el.clientWidth, h = el.clientHeight;
    if (!w || !h) return;
    this.renderer.setSize(w, h, false);
    this.camera.aspect = w / h;
    this.camera.updateProjectionMatrix();
    for (const m of this.lineMaterials) m.resolution.set(w, h);
  }

  render() {
    this.controls.update();
    this.renderer.render(this.scene, this.camera);
  }

  // ---- mesh ---------------------------------------------------------------

  setMesh(vertices, triangles, { fit = true } = {}) {
    if (this.mesh) { this.root.remove(this.mesh); this.mesh.geometry.dispose(); }
    if (this.wire) { this.root.remove(this.wire); this.wire.geometry.dispose(); this.wire = null; }
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.BufferAttribute(vertices, 3));
    g.setIndex(new THREE.BufferAttribute(triangles, 1));
    g.computeVertexNormals();
    const colors = new Float32Array(vertices.length).fill(0.86);
    g.setAttribute('color', new THREE.BufferAttribute(colors, 3));
    const mat = this._material();
    this.mesh = new THREE.Mesh(g, mat);
    this.root.add(this.mesh);
    g.computeBoundingSphere();
    this.sceneRadius = g.boundingSphere.radius || 1;
    this.center = g.boundingSphere.center.clone();
    this.setWireframe(this.wireVisible);
    if (fit) this.fit();
  }

  updateMeshPositions(vertices) {
    if (!this.mesh) return;
    const attr = this.mesh.geometry.getAttribute('position');
    attr.array.set(vertices);
    attr.needsUpdate = true;
    this.mesh.geometry.computeVertexNormals();
    this.mesh.geometry.computeBoundingSphere();
    if (this.wire) { this.root.remove(this.wire); this.wire.geometry.dispose(); this.wire = null; this.setWireframe(this.wireVisible); }
  }

  _material() {
    const mat = new THREE.MeshStandardMaterial({
      vertexColors: true, side: THREE.DoubleSide, roughness: 0.75, metalness: 0.0, flatShading: false,
      polygonOffset: true, polygonOffsetFactor: 4, polygonOffsetUnits: 6,
    });
    const self = this;
    mat.onBeforeCompile = (shader) => {
      shader.uniforms.uZebra = { value: self.zebra ? 1 : 0 };
      shader.uniforms.uStripes = { value: 18.0 };
      mat.userData.shader = shader;
      shader.fragmentShader = shader.fragmentShader
        .replace('#include <common>', '#include <common>\nuniform float uZebra; uniform float uStripes;')
        .replace('#include <dithering_fragment>', `#include <dithering_fragment>
          if (uZebra > 0.5) {
            vec3 n = normalize(normal);
            vec3 v = normalize(vViewPosition);
            vec3 r = reflect(-v, n);
            float s = sin((r.x + 0.35 * r.y) * uStripes);
            float band = smoothstep(-0.08, 0.08, s);
            gl_FragColor = vec4(mix(vec3(0.08), vec3(0.96), band), 1.0);
          }`);
    };
    mat.customProgramCacheKey = () => 'mite-zebra';
    return mat;
  }

  setZebra(on) {
    this.zebra = on;
    const sh = this.mesh?.material.userData.shader;
    if (sh) sh.uniforms.uZebra.value = on ? 1 : 0;
  }

  setColors(colors) {
    if (!this.mesh) return;
    const attr = this.mesh.geometry.getAttribute('color');
    if (colors) attr.array.set(colors); else attr.array.fill(0.86);
    attr.needsUpdate = true;
  }

  setWireframe(on) {
    this.wireVisible = on;
    if (!this.mesh) return;
    if (on && !this.wire) {
      const eg = new THREE.EdgesGeometry(this.mesh.geometry, 1);
      this.wire = new THREE.LineSegments(eg, new THREE.LineBasicMaterial({ color: this.dark ? 0x9aa3ad : 0x3b4048, transparent: true, opacity: 0.35 }));
      this.root.add(this.wire);
    }
    if (this.wire) this.wire.visible = on;
  }

  setMeshOpacity(a) {
    if (!this.mesh) return;
    this.mesh.material.transparent = a < 1;
    this.mesh.material.opacity = a;
    this.mesh.material.needsUpdate = true;
  }

  fit() {
    const r = this.sceneRadius, c = this.center || new THREE.Vector3();
    this.controls.target.copy(c);
    const dist = r / Math.sin((this.camera.fov * Math.PI) / 360) * 1.05;
    const dir = new THREE.Vector3(-0.55, -0.85, 0.6).normalize();
    this.camera.position.copy(c).addScaledVector(dir, dist);
    this.camera.near = Math.max(0.001, dist / 200);
    this.camera.far = dist * 20;
    this.camera.updateProjectionMatrix();
    this.controls.update();
  }

  // ---- curves -----------------------------------------------------------------

  clearCurves() {
    for (const o of this.curveObjects) { this.curvesGroup.remove(o.line); o.line.geometry.dispose(); }
    this.curveObjects = [];
    this.lineMaterials = this.lineMaterials.filter((m) => m.userData.keep);
    this.selected = null;
    this.clearOverlay('crossings');
    this.clearOverlay('deformed');
    this.clearOverlay('sweep');
  }

  _lineMaterial(color, width) {
    const m = new LineMaterial({ color, linewidth: width, worldUnits: false, alphaToCoverage: true });
    m.resolution.set(this.canvas.clientWidth, this.canvas.clientHeight);
    this.lineMaterials.push(m);
    return m;
  }

  setCurves(familyA, familyB, { widthA = 2.2, widthB = 2.2 } = {}) {
    this.clearCurves();
    const add = (curves, family, color, width) => {
      curves.forEach((pts, index) => {
        if (pts.length < 2) return;
        const flat = new Float32Array(pts.length * 3);
        pts.forEach((p, i) => { flat[3 * i] = p[0]; flat[3 * i + 1] = p[1]; flat[3 * i + 2] = p[2]; });
        const g = new LineGeometry();
        g.setPositions(flat);
        const line = new Line2(g, this._lineMaterial(color, width));
        line.computeLineDistances();
        line.userData = { family, index };
        this.curvesGroup.add(line);
        this.curveObjects.push({ line, family, index, points: pts, color });
      });
    };
    add(familyA || [], 'A', FAMILY_A, widthA);
    add(familyB || [], 'B', FAMILY_B, widthB);
  }

  colorCurves(colorsA, colorsB) { // arrays of [r,g,b] per curve, or null to reset
    for (const o of this.curveObjects) {
      const c = o.family === 'A' ? colorsA?.[o.index] : colorsB?.[o.index];
      if (c) o.line.material.color.setRGB(c[0], c[1], c[2]); else o.line.material.color.setHex(o.color);
      o.line.material.needsUpdate = true;
    }
  }

  selectCurve(obj) {
    if (this.selected) { const s = this.selected; s.line.material.color.setHex(s.color); s.line.material.linewidth = 2.2; }
    this.selected = obj;
    if (obj) { obj.line.material.color.setHex(SELECT); obj.line.material.linewidth = 4; }
  }

  // ---- overlays -----------------------------------------------------------------

  clearOverlay(tag) {
    const rm = this.overlayGroup.children.filter((c) => c.userData.tag === tag);
    for (const c of rm) { this.overlayGroup.remove(c); c.geometry?.dispose(); }
  }

  showPoints(tag, flat, color, size) {
    this.clearOverlay(tag);
    if (!flat || !flat.length) return;
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.BufferAttribute(Float32Array.from(flat), 3));
    const m = new THREE.PointsMaterial({ color, size, sizeAttenuation: false, depthTest: true });
    const p = new THREE.Points(g, m);
    p.userData.tag = tag;
    this.overlayGroup.add(p);
  }

  showSegments(tag, flat, color, width = 1.2) {
    this.clearOverlay(tag);
    if (!flat || !flat.length) return;
    const g = new LineSegmentsGeometry();
    g.setPositions(Float32Array.from(flat));
    const m = this._lineMaterial(color, width);
    const l = new LineSegments2(g, m);
    l.userData.tag = tag;
    this.overlayGroup.add(l);
  }

  showPolylines(tag, curves, color, width = 1.6, dashed = false) {
    this.clearOverlay(tag);
    for (const pts of curves) {
      if (pts.length < 2) continue;
      const flat = new Float32Array(pts.length * 3);
      pts.forEach((p, i) => { flat[3 * i] = p[0]; flat[3 * i + 1] = p[1]; flat[3 * i + 2] = p[2]; });
      const g = new LineGeometry(); g.setPositions(flat);
      const m = this._lineMaterial(color, width);
      if (dashed) { m.dashed = true; m.dashSize = 0.05 * this.sceneRadius; m.gapSize = 0.03 * this.sceneRadius; m.defines.USE_DASH = ''; }
      const line = new Line2(g, m); line.computeLineDistances();
      line.userData.tag = tag;
      this.overlayGroup.add(line);
    }
  }

  showSolid(tag, vertices, triangles, color) {
    this.clearOverlay(tag);
    if (!vertices?.length) return;
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.BufferAttribute(Float32Array.from(vertices), 3));
    g.setIndex(new THREE.BufferAttribute(Uint32Array.from(triangles), 1));
    g.computeVertexNormals();
    const m = new THREE.MeshStandardMaterial({ color, roughness: 0.6, side: THREE.DoubleSide });
    const mesh = new THREE.Mesh(g, m);
    mesh.userData.tag = tag;
    this.overlayGroup.add(mesh);
  }

  /** Principal direction glyphs: short segments along d1 (teal) and d2 (plum) at a subset of vertices */
  showDirections(vertices, d1, d2, normals, every, length) {
    const segA = [], segB = [];
    const n = vertices.length / 3;
    for (let i = 0; i < n; i += every) {
      const px = vertices[3 * i], py = vertices[3 * i + 1], pz = vertices[3 * i + 2];
      const ox = normals[3 * i] * length * 0.15, oy = normals[3 * i + 1] * length * 0.15, oz = normals[3 * i + 2] * length * 0.15;
      const h = length / 2;
      segA.push(px + ox - d1[3 * i] * h, py + oy - d1[3 * i + 1] * h, pz + oz - d1[3 * i + 2] * h, px + ox + d1[3 * i] * h, py + oy + d1[3 * i + 1] * h, pz + oz + d1[3 * i + 2] * h);
      segB.push(px + ox - d2[3 * i] * h, py + oy - d2[3 * i + 1] * h, pz + oz - d2[3 * i + 2] * h, px + ox + d2[3 * i] * h, py + oy + d2[3 * i + 1] * h, pz + oz + d2[3 * i + 2] * h);
    }
    this.showSegments('dirA', segA, FAMILY_A, 1.4);
    this.showSegments('dirB', segB, FAMILY_B, 1.4);
  }

  hideDirections() { this.clearOverlay('dirA'); this.clearOverlay('dirB'); }

  // ---- loft handles -------------------------------------------------------------

  setHandles(points, radius) { // points: [[x,y,z], ...] with ids = index
    this.clearHandles();
    const geo = new THREE.SphereGeometry(radius, 16, 12);
    points.forEach((p, i) => {
      const m = new THREE.Mesh(geo, new THREE.MeshStandardMaterial({ color: 0x1f2937, roughness: 0.4, emissive: 0x000000 }));
      m.position.set(p[0], p[1], p[2]);
      m.userData = { handle: i };
      this.handlesGroup.add(m);
    });
  }

  clearHandles() {
    for (const h of [...this.handlesGroup.children]) { this.handlesGroup.remove(h); h.material.dispose(); }
  }

  // ---- picking ------------------------------------------------------------------

  _setPointer(e) {
    const r = this.canvas.getBoundingClientRect();
    this.pointer.set(((e.clientX - r.left) / r.width) * 2 - 1, -((e.clientY - r.top) / r.height) * 2 + 1);
    this.raycaster.setFromCamera(this.pointer, this.camera);
  }

  _bindPointer() {
    let down = null;
    this.canvas.addEventListener('pointerdown', (e) => {
      down = { x: e.clientX, y: e.clientY, t: performance.now() };
      if (e.button !== 0 || !this.handlesGroup.children.length) return;
      this._setPointer(e);
      const hit = this.raycaster.intersectObjects(this.handlesGroup.children, false)[0];
      if (hit) {
        const plane = new THREE.Plane().setFromNormalAndCoplanarPoint(this.camera.getWorldDirection(new THREE.Vector3()), hit.object.position);
        this.dragging = { obj: hit.object, plane, offset: hit.object.position.clone().sub(hit.point) };
        this.controls.enabled = false;
        this.canvas.setPointerCapture(e.pointerId);
        hit.object.material.emissive.setHex(0xf59e0b);
      }
    });
    this.canvas.addEventListener('pointermove', (e) => {
      if (this.dragging) {
        this._setPointer(e);
        const p = new THREE.Vector3();
        if (this.raycaster.ray.intersectPlane(this.dragging.plane, p)) {
          p.add(this.dragging.offset);
          this.dragging.obj.position.copy(p);
          this.onHandleDrag?.(this.dragging.obj.userData.handle, [p.x, p.y, p.z]);
        }
        return;
      }
      if (this.handlesGroup.children.length) {
        this._setPointer(e);
        const hit = this.raycaster.intersectObjects(this.handlesGroup.children, false)[0];
        this.canvas.style.cursor = hit ? 'grab' : '';
      }
    });
    const up = (e) => {
      if (this.dragging) {
        this.dragging.obj.material.emissive.setHex(0x000000);
        const id = this.dragging.obj.userData.handle;
        this.dragging = null;
        this.controls.enabled = true;
        this.onHandleDragEnd?.(id);
        return;
      }
      if (!down) return;
      const moved = Math.hypot(e.clientX - down.x, e.clientY - down.y);
      const quick = performance.now() - down.t < 400;
      down = null;
      if (moved > 4 || !quick || e.button !== 0) return;
      this._setPointer(e);
      const lines = this.curveObjects.map((o) => o.line);
      if (lines.length && !e.shiftKey) {
        const hit = this.raycaster.intersectObjects(lines, false)[0];
        if (hit) { const o = this.curveObjects.find((c) => c.line === hit.object); this.onPickCurve?.(o); return; }
      }
      if (this.mesh) {
        const hit = this.raycaster.intersectObject(this.mesh, false)[0];
        if (hit) this.onPickVertex?.([hit.point.x, hit.point.y, hit.point.z], e.shiftKey);
        else this.onPickCurve?.(null);
      }
    };
    this.canvas.addEventListener('pointerup', up);
  }

  screenshot() { this.render(); return this.canvas.toDataURL('image/png'); }
}
