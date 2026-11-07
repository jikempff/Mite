// three.js viewport: the analysed mesh with per-vertex colours or a zebra
// shader, curve families as fat lines, crossing points, umbilics, principal
// direction glyphs, a selected lath (with optional swept solid), the
// deformed net, draggable loft control points, coloured net cells (the
// Chebyshev angle map) and HTML text labels pinned to surface points
// (isocurve levels), hidden while the surface is in front of them.

import * as THREE from 'three';
import { OrbitControls } from '../vendor/OrbitControls.js';
import { Line2 } from '../vendor/lines/Line2.js';
import { LineMaterial } from '../vendor/lines/LineMaterial.js';
import { LineGeometry } from '../vendor/lines/LineGeometry.js';
import { LineSegments2 } from '../vendor/lines/LineSegments2.js';
import { LineSegmentsGeometry } from '../vendor/lines/LineSegmentsGeometry.js';

import { palette, colour as paletteColour, rgb as paletteRgb, hex as paletteHex } from './palette.js';

// Colours are roles, not numbers: every object the viewer draws carries the
// role it was drawn with ('famA', 'famB', 'famG', 'seed', 'mark', 'ink',
// 'solid', 'warn', …) and setTheme() resolves the roles again through
// Mite.Core's palette (palette.js, generated) — the dark page gets the dark
// theme's family colours instead of the light theme's. A '#rrggbb' string or
// a 0xrrggbb number passes through unchanged and is not re-themed.

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
    this.onDirectionDrag = null;
    this.onDirectionDragEnd = null;
    this.onHover = null;
    this.hovered = null;
    this.dirHandle = null;
    this.raycaster = new THREE.Raycaster();
    this.raycaster.params.Line2 = { threshold: 6 };
    this.labels = []; // {p: Vector3, el, visible}
    this.labelLayer = null;
    this._labelTick = 0;
    this.pointer = new THREE.Vector2();
    this.dragging = null;
    this.dark = false;
    this.theme = palette(false);
    this.zebra = false;
    this.sceneRadius = 1;

    this._bindPointer();
    const ro = new ResizeObserver(() => this.resize());
    ro.observe(canvas.parentElement);
    this.resize();
    this.renderer.setAnimationLoop(() => this.render());
  }

  /** Ink of the current theme: black on white, white on black (selection, seed, supports). */
  ink() { return this.colour('ink'); }

  /** A role's colour in the current theme as the 0xrrggbb number three.js takes ('#rrggbb' and numbers pass through). */
  colour(role) { return paletteColour(this.theme, role); }
  /** A role's colour in the current theme as '#rrggbb'. */
  hex(role) { return paletteHex(this.theme, role); }

  /**
   * Switches the page theme: the families, marks, seeds, handles, wireframe and
   * every overlay drawn with a role take the theme's colours (Mite.Core
   * MitePalette, dark page: famA #5b6bff, famB #ff3d6a, famG #00ff85);
   * per-curve overrides (colorCurves) and raw colours stay as they are.
   */
  setTheme(dark) {
    this.dark = dark;
    this.theme = palette(dark);
    this.renderer.setClearColor(0x000000, 0);
    if (this.mesh) this.mesh.material.needsUpdate = true;
    for (const o of this.curveObjects) this._applyCurveColour(o);
    if (this.wire) this.wire.material.color.setHex(this.colour('ink'));
    const retheme = (obj) => {
      const m = obj.material; if (!m) return;
      if (typeof m.userData?.role === 'string') { m.color.setHex(this.colour(m.userData.role)); m.needsUpdate = true; }
      if (obj.userData?.roles) { obj.geometry.getAttribute('color').array.set(this._rgbOfRoles(obj.userData.roles)); obj.geometry.getAttribute('color').needsUpdate = true; }
    };
    this.overlayGroup.children.forEach(retheme);
    this.handlesGroup.children.forEach(retheme);
  }

  /** Flat [r, g, b, …] (0..1) of one role per point. */
  _rgbOfRoles(roles) {
    const out = new Float32Array(roles.length * 3);
    roles.forEach((r, i) => { const c = typeof r === 'string' ? paletteRgb(this.theme, r) : r; out[3 * i] = c[0]; out[3 * i + 1] = c[1]; out[3 * i + 2] = c[2]; });
    return out;
  }

  /** Paint the page colour behind the scene (on) or keep it transparent (off): a recorded video has no alpha, so a transparent canvas would come out black. */
  opaqueBackground(on, cssColour) {
    if (on) this.renderer.setClearColor(new THREE.Color(cssColour || this.hex('paper')), 1);
    else this.renderer.setClearColor(0x000000, 0);
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
    if (this.labels.length) this._placeLabels();
  }

  // ---- mesh ---------------------------------------------------------------

  setMesh(vertices, triangles, { fit = true } = {}) {
    if (this.mesh) { this.root.remove(this.mesh); this.mesh.geometry.dispose(); }
    if (this.wire) { this.root.remove(this.wire); this.wire.geometry.dispose(); this.wire = null; }
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.BufferAttribute(vertices, 3));
    g.setIndex(new THREE.BufferAttribute(triangles, 1));
    g.computeVertexNormals();
    const colors = new Float32Array(vertices.length); this._fillMeshGrey(colors);
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

  /** The uncoloured surface: the palette's mesh grey (#dbdbdb, the same on both pages — a lit object, not ink). */
  _fillMeshGrey(arr) { const c = paletteRgb(this.theme, 'mesh'); for (let i = 0; i + 2 < arr.length; i += 3) { arr[i] = c[0]; arr[i + 1] = c[1]; arr[i + 2] = c[2]; } }

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
    if (colors) attr.array.set(colors); else this._fillMeshGrey(attr.array);
    attr.needsUpdate = true;
  }

  setWireframe(on) {
    this.wireVisible = on;
    if (!this.mesh) return;
    if (on && !this.wire) {
      const eg = new THREE.EdgesGeometry(this.mesh.geometry, 1);
      this.wire = new THREE.LineSegments(eg, new THREE.LineBasicMaterial({ color: this.colour('ink'), transparent: true, opacity: 0.35 }));
      this.root.add(this.wire);
    }
    if (this.wire) this.wire.visible = on;
  }

  setMeshOpacity(a) {
    if (!this.mesh) return;
    const see = a < 0.999;
    // a see-through surface must not write depth, or the laths and curves behind it stay hidden;
    // drawn after the opaque laths (renderOrder) so they show through it
    this.mesh.material.transparent = see;
    this.mesh.material.opacity = a;
    this.mesh.material.depthWrite = !see;
    this.mesh.renderOrder = see ? 10 : 0;
    this.mesh.visible = a > 0.001;
    this.mesh.material.needsUpdate = true;
    if (this.wire) this.wire.renderOrder = see ? 11 : 0;
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

  _hover(o) {
    if (this.hovered === o) return;
    if (this.hovered && this.hovered !== this.selected) { this.hovered.line.material.linewidth = 2.2; }
    this.hovered = o;
    if (o && o !== this.selected) o.line.material.linewidth = 3.4;
    this.canvas.style.cursor = o ? 'pointer' : '';
    this.onHover?.(o);
  }

  clearCurves() {
    this.hovered = null;
    for (const o of this.curveObjects) { this.curvesGroup.remove(o.line); o.line.geometry.dispose(); }
    this.curveObjects = [];
    this.lineMaterials = this.lineMaterials.filter((m) => m.userData.keep);
    this.selected = null;
    this.clearOverlay('crossings');
    this.clearOverlay('deformed');
    this.clearOverlay('sweep');
  }

  _lineMaterial(role, width) {
    const m = new LineMaterial({ color: this.colour(role), linewidth: width, worldUnits: false, alphaToCoverage: false });
    m.userData.role = role;
    m.resolution.set(this.canvas.clientWidth, this.canvas.clientHeight);
    this.lineMaterials.push(m);
    return m;
  }

  /** A curve's colour: its override (colorCurves), or its family's role in the current theme. */
  _applyCurveColour(o) {
    if (o === this.selected) return;
    const c = o.override;
    if (c) o.line.material.color.setRGB(c[0], c[1], c[2]); else o.line.material.color.setHex(this.colour(o.role));
    o.line.material.needsUpdate = true;
  }

  setCurves(familyA, familyB, { widthA = 2.2, widthB = 2.2, familyG = null } = {}) {
    this.clearCurves();
    const add = (curves, family, role, width) => {
      curves.forEach((pts, index) => {
        if (pts.length < 2) return;
        const flat = new Float32Array(pts.length * 3);
        pts.forEach((p, i) => { flat[3 * i] = p[0]; flat[3 * i + 1] = p[1]; flat[3 * i + 2] = p[2]; });
        const g = new LineGeometry();
        g.setPositions(flat);
        const line = new Line2(g, this._lineMaterial(role, width));
        line.computeLineDistances();
        line.userData = { family, index };
        this.curvesGroup.add(line);
        this.curveObjects.push({ line, family, index, points: pts, role, override: null });
      });
    };
    add(familyA || [], 'A', 'famA', widthA);
    add(familyB || [], 'B', 'famB', widthB);
    add(familyG || [], 'G', 'famG', 1.8);
  }

  colorCurves(colorsA, colorsB, colorsG = null) { // arrays of [r,g,b] per curve, or null to reset to the family colours
    this.colorsG = colorsG;
    for (const o of this.curveObjects) {
      o.override = (o.family === 'A' ? colorsA?.[o.index] : o.family === 'B' ? colorsB?.[o.index] : this.colorsG?.[o.index]) || null;
      this._applyCurveColour(o);
    }
  }

  selectCurve(obj) {
    if (this.selected) { const s = this.selected; this.selected = null; this._applyCurveColour(s); s.line.material.linewidth = 2.2; }
    this.selected = obj;
    if (obj) { obj.line.material.color.setHex(this.ink()); obj.line.material.linewidth = 4.5; }
  }

  // ---- overlays -----------------------------------------------------------------

  clearOverlay(tag) {
    const rm = this.overlayGroup.children.filter((c) => c.userData.tag === tag);
    for (const c of rm) { this.overlayGroup.remove(c); c.geometry?.dispose(); }
  }

  setOverlayVisible(tag, visible) {
    for (const c of this.overlayGroup.children) if (c.userData.tag === tag) c.visible = visible;
  }

  /** Points in one role (or raw colour), or with `colors`: one role per point (re-themed) or flat [r, g, b, …] (kept as given). */
  showPoints(tag, flat, role, size, colors = null) {
    this.clearOverlay(tag);
    if (!flat || !flat.length) return;
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.BufferAttribute(Float32Array.from(flat), 3));
    const roles = Array.isArray(colors) && typeof colors[0] === 'string' ? colors : null;
    if (colors) g.setAttribute('color', new THREE.BufferAttribute(roles ? this._rgbOfRoles(roles) : Float32Array.from(colors), 3));
    const m = new THREE.PointsMaterial({ color: colors ? 0xffffff : this.colour(role), size, sizeAttenuation: false, depthTest: true, vertexColors: !!colors });
    if (!colors) m.userData.role = role;
    const p = new THREE.Points(g, m);
    p.userData.tag = tag;
    if (roles) p.userData.roles = roles;
    this.overlayGroup.add(p);
  }

  /** Dim or restore the current curve families (used while a new net is computing) */
  ghostCurves(on) {
    for (const o of this.curveObjects) { o.line.material.transparent = true; o.line.material.opacity = on ? 0.3 : 1; o.line.material.needsUpdate = true; }
  }

  setFamilyVisible(family, visible) {
    for (const o of this.curveObjects) if (o.family === family) o.line.visible = visible;
  }

  // ---- seed direction handle ----------------------------------------------------

  /** Arrow at the seed showing the start direction; its tip is draggable in the tangent plane */
  setDirectionHandle(seed, normal, dir, length) {
    this.clearDirectionHandle();
    const p = new THREE.Vector3(...seed), n = new THREE.Vector3(...normal).normalize();
    let d = new THREE.Vector3(...dir); d.addScaledVector(n, -d.dot(n)); if (d.lengthSq() < 1e-12) d.set(1, 0, 0).addScaledVector(n, -n.x); d.normalize();
    const tip = p.clone().addScaledVector(d, length);
    const g = new LineGeometry(); g.setPositions([p.x, p.y, p.z, tip.x, tip.y, tip.z]);
    const m = this._lineMaterial('ink', 3); m.userData.keep = true;
    const line = new Line2(g, m); line.computeLineDistances(); line.userData.tag = 'dirline';
    this.overlayGroup.add(line);
    const knob = new THREE.Mesh(new THREE.SphereGeometry(length * 0.12, 16, 12), this._solidMaterial('seed', 0.4));
    knob.position.copy(tip);
    knob.userData = { handle: -1, kind: 'dir', origin: p, normal: n, length };
    this.handlesGroup.add(knob);
    const base = new THREE.Mesh(new THREE.SphereGeometry(length * 0.08, 12, 10), this._solidMaterial('solid'));
    base.position.copy(p); base.userData.tag = 'dirbase';
    this.overlayGroup.add(base);
    this.dirHandle = knob;
  }

  clearDirectionHandle() {
    if (this.dirHandle) { this.handlesGroup.remove(this.dirHandle); this.dirHandle = null; }
    this.clearOverlay('dirline'); this.clearOverlay('dirbase');
  }

  _updateDirectionLine() {
    const k = this.dirHandle; if (!k) return;
    const l = this.overlayGroup.children.find((c) => c.userData.tag === 'dirline');
    if (l) { l.geometry.setPositions([k.userData.origin.x, k.userData.origin.y, k.userData.origin.z, k.position.x, k.position.y, k.position.z]); l.computeLineDistances(); }
  }

  // ---- camera presets ----------------------------------------------------------------

  view(kind) {
    const r = this.sceneRadius, c = this.center || new THREE.Vector3();
    const dist = r / Math.sin((this.camera.fov * Math.PI) / 360) * 1.05;
    const dirs = { iso: [-0.55, -0.85, 0.6], top: [0, -0.0001, 1], front: [0, -1, 0.0001], side: [1, 0, 0.0001] };
    const dir = new THREE.Vector3(...(dirs[kind] || dirs.iso)).normalize();
    this.controls.target.copy(c);
    this.camera.position.copy(c).addScaledVector(dir, dist);
    this.camera.updateProjectionMatrix();
    this.controls.update();
  }

  /** A lit material in a role (handles, swept solids); the role is kept for setTheme. */
  _solidMaterial(role, roughness = 0.6, side = THREE.FrontSide) {
    const m = new THREE.MeshStandardMaterial({ color: this.colour(role), roughness, emissive: 0x000000, side });
    m.userData.role = role;
    return m;
  }

  showSegments(tag, flat, role, width = 1.2) {
    this.clearOverlay(tag);
    if (!flat || !flat.length) return;
    const g = new LineSegmentsGeometry();
    g.setPositions(Float32Array.from(flat));
    const m = this._lineMaterial(role, width);
    const l = new LineSegments2(g, m);
    l.userData.tag = tag;
    this.overlayGroup.add(l);
  }

  showPolylines(tag, curves, role, width = 1.6, dashed = false) {
    this.clearOverlay(tag);
    for (const pts of curves) {
      if (pts.length < 2) continue;
      const flat = new Float32Array(pts.length * 3);
      pts.forEach((p, i) => { flat[3 * i] = p[0]; flat[3 * i + 1] = p[1]; flat[3 * i + 2] = p[2]; });
      const g = new LineGeometry(); g.setPositions(flat);
      const m = this._lineMaterial(role, width);
      if (dashed) { m.dashed = true; m.dashSize = 0.05 * this.sceneRadius; m.gapSize = 0.03 * this.sceneRadius; m.defines.USE_DASH = ''; }
      const line = new Line2(g, m); line.computeLineDistances();
      line.userData.tag = tag;
      this.overlayGroup.add(line);
    }
  }

  showSolid(tag, vertices, triangles, role) {
    this.clearOverlay(tag);
    if (!vertices?.length) return;
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.BufferAttribute(Float32Array.from(vertices), 3));
    g.setIndex(new THREE.BufferAttribute(Uint32Array.from(triangles), 1));
    g.computeVertexNormals();
    const m = this._solidMaterial(role, 0.6, THREE.DoubleSide);
    const mesh = new THREE.Mesh(g, m);
    mesh.userData.tag = tag;
    this.overlayGroup.add(mesh);
  }

  /**
   * Polygon faces with one colour each (fan-triangulated, vertices duplicated per face so the
   * colour is flat), slightly see-through so the laths underneath stay visible; a thin outline
   * per face marks the panel edges.
   */
  showFaces(tag, vertices, faces, colors, opacity = 0.85) {
    this.clearOverlay(tag); this.clearOverlay(tag + 'Edges');
    if (!vertices?.length || !faces?.length) return;
    const pos = [], col = [], edges = [], idx = [];
    for (let f = 0; f < faces.length; f++) {
      const ring = faces[f], c = colors[f] || [0.5, 0.5, 0.5];
      const base = pos.length / 3;
      for (const vi of ring) { pos.push(vertices[3 * vi], vertices[3 * vi + 1], vertices[3 * vi + 2]); col.push(c[0], c[1], c[2]); }
      for (let k = 0; k < ring.length; k++) {
        const a = ring[k], b = ring[(k + 1) % ring.length];
        edges.push(vertices[3 * a], vertices[3 * a + 1], vertices[3 * a + 2], vertices[3 * b], vertices[3 * b + 1], vertices[3 * b + 2]);
      }
      for (let k = 1; k + 1 < ring.length; k++) idx.push(base, base + k, base + k + 1);
    }
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.BufferAttribute(Float32Array.from(pos), 3));
    g.setAttribute('color', new THREE.BufferAttribute(Float32Array.from(col), 3));
    g.setIndex(new THREE.BufferAttribute(Uint32Array.from(idx), 1));
    g.computeVertexNormals();
    const m = new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.7, side: THREE.DoubleSide, transparent: opacity < 0.999, opacity, depthWrite: opacity >= 0.999 });
    const mesh = new THREE.Mesh(g, m);
    mesh.userData.tag = tag; mesh.renderOrder = 5;
    this.overlayGroup.add(mesh);
    this.showSegments(tag + 'Edges', edges, 'ink', 0.8);
  }

  /**
   * Net cells coloured per node: flat xyz nodes, four node indices per cell
   * (counter-clockwise), one [r, g, b] per node. Drawn as two triangles per
   * cell, double-sided, pushed in front of the surface.
   */
  showCells(tag, nodes, quads, colors) {
    this.clearOverlay(tag);
    if (!nodes?.length || !quads?.length) return;
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.BufferAttribute(Float32Array.from(nodes), 3));
    const idx = [];
    for (let q = 0; q + 3 < quads.length; q += 4) {
      const a = quads[q], b = quads[q + 1], c = quads[q + 2], d = quads[q + 3];
      idx.push(a, b, c, a, c, d);
    }
    g.setIndex(idx);
    const col = new Float32Array(nodes.length);
    for (let i = 0; i < nodes.length / 3; i++) { const c = colors[i] || [0.6, 0.6, 0.6]; col[3 * i] = c[0]; col[3 * i + 1] = c[1]; col[3 * i + 2] = c[2]; }
    g.setAttribute('color', new THREE.BufferAttribute(col, 3));
    g.computeVertexNormals();
    // the cells are chords of the surface and sag below it by up to κL²/8: a large depth
    // offset keeps them in front; a little transparency keeps the laths readable
    const m = new THREE.MeshStandardMaterial({ vertexColors: true, side: THREE.DoubleSide, roughness: 0.8, metalness: 0, transparent: true, opacity: 0.88, polygonOffset: true, polygonOffsetFactor: -8, polygonOffsetUnits: -400 });
    const mesh = new THREE.Mesh(g, m);
    mesh.userData.tag = tag;
    this.overlayGroup.add(mesh);
  }

  /**
   * Text labels pinned to surface points: items = [{p: [x, y, z], text}].
   * HTML elements in a layer over the canvas, re-projected every frame; a
   * label whose point is hidden behind the surface is faded (occlusion by a
   * ray from the camera, re-tested a few times a second while the view moves).
   */
  showLabels(tag, items) {
    this.clearLabels(tag);
    if (!items?.length) return;
    if (!this.labelLayer) {
      this.labelLayer = document.createElement('div');
      this.labelLayer.className = 'labels';
      this.canvas.parentElement.appendChild(this.labelLayer);
    }
    for (const it of items) {
      const el = document.createElement('span');
      el.className = 'label';
      el.textContent = it.text;
      this.labelLayer.appendChild(el);
      this.labels.push({ tag, p: new THREE.Vector3(it.p[0], it.p[1], it.p[2]), el, occluded: false, checked: null });
    }
    this._placeLabels(true);
  }

  clearLabels(tag) {
    const keep = [];
    for (const l of this.labels) { if (tag && l.tag !== tag) keep.push(l); else l.el.remove(); }
    this.labels = keep;
  }

  _placeLabels(force = false) {
    const r = this.canvas.getBoundingClientRect();
    const camPos = this.camera.position;
    const now = performance.now();
    const occlude = this.mesh && (force || now - this._labelTick > 150);
    if (occlude) this._labelTick = now;
    const v = new THREE.Vector3(), dir = new THREE.Vector3();
    for (const l of this.labels) {
      v.copy(l.p).project(this.camera);
      const behind = v.z > 1 || v.z < -1;
      if (occlude) {
        dir.copy(l.p).sub(camPos);
        const dist = dir.length(); dir.divideScalar(dist);
        this.raycaster.set(camPos, dir);
        const hit = this.raycaster.intersectObject(this.mesh, false)[0];
        l.occluded = !!hit && hit.distance < dist - 1e-3 * this.sceneRadius;
      }
      const x = (v.x * 0.5 + 0.5) * r.width, y = (-v.y * 0.5 + 0.5) * r.height;
      l.el.style.transform = `translate(${x.toFixed(1)}px, ${y.toFixed(1)}px)`;
      l.el.style.display = behind ? 'none' : '';
      l.el.classList.toggle('dim', l.occluded);
    }
  }

  /** Principal direction glyphs: short segments along d1 (family A) and d2 (family B) at a subset of vertices */
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
    this.showSegments('dirA', segA, 'famA', 1.4);
    this.showSegments('dirB', segB, 'famB', 1.4);
  }

  hideDirections() { this.clearOverlay('dirA'); this.clearOverlay('dirB'); }

  // ---- loft handles -------------------------------------------------------------

  setHandles(points, radius) { // points: [[x,y,z], ...] with ids = index
    this.clearHandles();
    const geo = new THREE.SphereGeometry(radius, 16, 12);
    points.forEach((p, i) => {
      const m = new THREE.Mesh(geo, this._solidMaterial('solid', 0.4));
      m.position.set(p[0], p[1], p[2]);
      m.userData = { handle: i };
      this.handlesGroup.add(m);
    });
  }

  clearHandles() {
    for (const h of [...this.handlesGroup.children]) { if (h.userData.kind === 'dir') continue; this.handlesGroup.remove(h); h.material.dispose(); }
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
        const isDir = hit.object.userData.kind === 'dir';
        const planeNormal = isDir ? hit.object.userData.normal : this.camera.getWorldDirection(new THREE.Vector3());
        const plane = new THREE.Plane().setFromNormalAndCoplanarPoint(planeNormal, isDir ? hit.object.userData.origin : hit.object.position);
        this.dragging = { obj: hit.object, plane, offset: isDir ? new THREE.Vector3() : hit.object.position.clone().sub(hit.point) };
        this.controls.enabled = false;
        this.canvas.setPointerCapture(e.pointerId);
        hit.object.material.emissive.setHex(this.colour('highlight'));
      }
    });
    this.canvas.addEventListener('pointermove', (e) => {
      if (this.dragging) {
        this._setPointer(e);
        const p = new THREE.Vector3();
        if (this.raycaster.ray.intersectPlane(this.dragging.plane, p)) {
          p.add(this.dragging.offset);
          const u = this.dragging.obj.userData;
          if (u.kind === 'dir') {
            const d = p.clone().sub(u.origin); d.addScaledVector(u.normal, -d.dot(u.normal));
            if (d.lengthSq() < 1e-12) return;
            d.normalize();
            this.dragging.obj.position.copy(u.origin).addScaledVector(d, u.length);
            this._updateDirectionLine();
            this.onDirectionDrag?.([d.x, d.y, d.z]);
            return;
          }
          this.dragging.obj.position.copy(p);
          this.onHandleDrag?.(u.handle, [p.x, p.y, p.z]);
        }
        return;
      }
      this._setPointer(e);
      if (this.handlesGroup.children.length) {
        const hit = this.raycaster.intersectObjects(this.handlesGroup.children, false)[0];
        this.canvas.style.cursor = hit ? 'grab' : '';
        if (hit) { this._hover(null); return; }
      }
      if (this.curveObjects.length && this.onHover) {
        const now = performance.now();
        if (now - (this._lastHover || 0) > 60) {
          this._lastHover = now;
          const hit = this.raycaster.intersectObjects(this.curveObjects.filter((o) => o.line.visible).map((o) => o.line), false)[0];
          this._hover(hit ? this.curveObjects.find((c) => c.line === hit.object) : null);
        }
      }
    });
    const up = (e) => {
      if (this.dragging) {
        this.dragging.obj.material.emissive.setHex(0x000000);
        const u = this.dragging.obj.userData;
        this.dragging = null;
        this.controls.enabled = true;
        if (u.kind === 'dir') this.onDirectionDragEnd?.(); else this.onHandleDragEnd?.(u.handle);
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
