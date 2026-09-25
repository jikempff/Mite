#!/usr/bin/env python3
"""Generates the 24x24 Grasshopper component icons for Mite (and the tab icon).

Style follows David Rutten's original Grasshopper icon language as documented
by the recovered icon set (github.com/aidannewsome/grasshopper-icons, SKILL.md):
24 x 24 px with a ~2 px margin, one colour family per icon, no black
outlines (each region outlined in a darker shade of its own fill), light from
the upper left with subtle gradients, white dots for points/nodes, dark
lattice on a light patch for grids, and a soft drop shadow (+1, +1, blur 2,
~30%) applied in raster. Families: Surfaces/curvature amber (#FFC200-#FF7900,
outline #6E0000), Meshes/form finding orange-red (#FF9E05-#D72000, #8E1500),
Math/analysis green (#00C860, #024F00), Parameters/util greys (#C5C5C5-#454545),
fabrication in a timber brown of the same warm range. The tab icon is the
small-scale Mite mark (tools/mite_knot.png) reduced to 24 px.

Drawn at 8x and downsampled with Lanczos. Run: python3 tools/generate_icons.py
"""
import math
import os
from PIL import Image, ImageDraw, ImageFilter

S = 8
SIZE = 24
OUT = os.path.join(os.path.dirname(__file__), "..", "src", "Mite.Grasshopper", "Resources")

FAM = {
    "surface": dict(top="#FFD34D", bot="#FF7900", edge="#6E0000"),
    "mesh": dict(top="#FFB43A", bot="#D72000", edge="#8E1500"),
    "math": dict(top="#7FE39A", bot="#00A64E", edge="#024F00"),
    "param": dict(top="#D9D9D9", bot="#7A7A7A", edge="#353535"),
    "timber": dict(top="#F0C46A", bot="#B8651C", edge="#4A2200"),
}
WHITE = (255, 255, 255, 255)


def hexrgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def P(x, y):
    return (x * S, y * S)


def canvas():
    img = Image.new("RGBA", (SIZE * S, SIZE * S), (0, 0, 0, 0))
    return img, ImageDraw.Draw(img)


def gradient_image(top, bot):
    """Light upper-left to dark lower-right."""
    w = SIZE * S
    g = Image.new("RGBA", (w, w))
    px = g.load()
    a, b = hexrgb(top), hexrgb(bot)
    for y in range(w):
        for x in range(w):
            t = (x + y) / (2.0 * (w - 1))
            px[x, y] = (int(a[0] + (b[0] - a[0]) * t), int(a[1] + (b[1] - a[1]) * t), int(a[2] + (b[2] - a[2]) * t), 255)
    return g


_GRAD = {}


def fill_poly(img, pts, fam, outline=True, width=1.0):
    """Gradient-filled polygon outlined in the family's dark edge colour."""
    key = fam
    if key not in _GRAD:
        _GRAD[key] = gradient_image(FAM[fam]["top"], FAM[fam]["bot"])
    mask = Image.new("L", img.size, 0)
    ImageDraw.Draw(mask).polygon(pts, fill=255)
    img.paste(_GRAD[key], (0, 0), mask)
    if outline:
        ImageDraw.Draw(img).line(pts + [pts[0]], fill=hexrgb(FAM[fam]["edge"]) + (255,), width=int(width * S), joint="curve")


def fill_ellipse(img, box, fam, outline=True):
    key = fam
    if key not in _GRAD:
        _GRAD[key] = gradient_image(FAM[fam]["top"], FAM[fam]["bot"])
    mask = Image.new("L", img.size, 0)
    ImageDraw.Draw(mask).ellipse(box, fill=255)
    img.paste(_GRAD[key], (0, 0), mask)
    if outline:
        ImageDraw.Draw(img).ellipse(box, outline=hexrgb(FAM[fam]["edge"]) + (255,), width=int(S))


def stroke(d, pts, fam, width=1.0, alpha=255):
    d.line(pts, fill=hexrgb(FAM[fam]["edge"]) + (alpha,), width=max(1, int(width * S)), joint="curve")


def dot(d, x, y, r=1.7, fam="surface"):
    d.ellipse([P(x - r, y - r), P(x + r, y + r)], fill=WHITE, outline=hexrgb(FAM[fam]["edge"]) + (255,), width=int(0.7 * S))


def arrow(d, x0, y0, x1, y1, fam, width=1.0, head=2.2):
    stroke(d, [P(x0, y0), P(x1, y1)], fam, width)
    ang = math.atan2(y1 - y0, x1 - x0)
    tip = (x1, y1)
    l = (x1 - head * math.cos(ang - 0.5), y1 - head * math.sin(ang - 0.5))
    r = (x1 - head * math.cos(ang + 0.5), y1 - head * math.sin(ang + 0.5))
    d.polygon([P(*tip), P(*l), P(*r)], fill=hexrgb(FAM[fam]["edge"]) + (255,))


def finish(img, name):
    """Drop shadow (+1, +1, blur 2 px, ~30%) beneath, then downsample."""
    alpha = img.split()[3]
    shadow = Image.new("RGBA", img.size, (0, 0, 0, 0))
    sh = Image.new("RGBA", img.size, (25, 25, 25, 255))
    shadow.paste(sh, (S, S), alpha)
    shadow = shadow.filter(ImageFilter.GaussianBlur(1.2 * S))
    r, g, b, a = shadow.split()
    a = a.point(lambda v: int(v * 0.32))
    shadow = Image.merge("RGBA", (r, g, b, a))
    out = Image.alpha_composite(shadow, img)
    out = out.resize((SIZE, SIZE), Image.LANCZOS)
    out.save(os.path.join(OUT, name + ".png"))


# ---------------------------------------------------------------- a surface patch in perspective
PATCH = [(2.5, 8.0), (15.0, 2.5), (21.5, 11.5), (9.0, 21.0)]   # p00, p10, p11, p01 (clockwise on screen)


def uv(u, v, quad=PATCH):
    p00, p10, p11, p01 = quad
    x = (1 - u) * (1 - v) * p00[0] + u * (1 - v) * p10[0] + u * v * p11[0] + (1 - u) * v * p01[0]
    y = (1 - u) * (1 - v) * p00[1] + u * (1 - v) * p10[1] + u * v * p11[1] + (1 - u) * v * p01[1]
    return (x, y)


_CLIP = {}


def patch(img, fam="surface", quad=PATCH):
    fill_poly(img, [P(*p) for p in quad], fam)
    mask = Image.new("L", img.size, 0)
    ImageDraw.Draw(mask).polygon([P(*p) for p in quad], fill=255)
    _CLIP[id(img)] = (img, mask)


def uvline(d, f, fam, width=0.9, n=24, quad=PATCH):
    """f(t) -> (u, v) for t in [0, 1]; clipped to the last patch drawn on this canvas."""
    pts = [P(*uv(*f(i / (n - 1)), quad)) for i in range(n)]
    clip = next((c for c in _CLIP.values() if c[0] is getattr(d, "_image", None)), None)
    if clip is None:
        stroke(d, pts, fam, width)
        return
    img, mask = clip
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    stroke(ImageDraw.Draw(layer), pts, fam, width)
    img.paste(layer, (0, 0), Image.composite(layer.split()[3], Image.new("L", img.size, 0), mask))


def lattice(img, d, fam, nu=3, nv=3, nodes=True, quad=PATCH):
    patch(img, fam, quad)
    for i in range(1, nu):
        stroke(d, [P(*uv(i / nu, 0, quad)), P(*uv(i / nu, 1, quad))], fam, 0.8)
    for j in range(1, nv):
        stroke(d, [P(*uv(0, j / nv, quad)), P(*uv(1, j / nv, quad))], fam, 0.8)
    if nodes:
        for i in range(1, nu):
            for j in range(1, nv):
                x, y = uv(i / nu, j / nv, quad)
                dot(d, x, y, 1.2, fam)


# ---------------------------------------------------------------- a lath band (curved strip with thickness)
def band(img, d, pts, half, depth, fam, cap=True):
    n = len(pts)

    def offset(h, dy=0.0):
        out = []
        for i, (x, y) in enumerate(pts):
            if i == 0:
                tx, ty = pts[1][0] - x, pts[1][1] - y
            elif i == n - 1:
                tx, ty = x - pts[-2][0], y - pts[-2][1]
            else:
                tx, ty = pts[i + 1][0] - pts[i - 1][0], pts[i + 1][1] - pts[i - 1][1]
            L = math.hypot(tx, ty) or 1.0
            nx, ny = -ty / L, tx / L
            out.append((x + nx * h, y + ny * h + dy))
        return out

    top_a, top_b = offset(half), offset(-half)
    if depth > 0:
        bot_b = offset(-half, depth)
        side = [P(*p) for p in top_b] + [P(*p) for p in reversed(bot_b)]
        fill_poly(img, side, fam)
        # darken the side face
        mask = Image.new("L", img.size, 0)
        ImageDraw.Draw(mask).polygon(side, fill=70)
        img.paste(Image.new("RGBA", img.size, (40, 10, 0, 255)), (0, 0), mask)
        if cap:
            cap_poly = [P(*top_a[-1]), P(*top_b[-1]), P(*bot_b[-1]), P(top_a[-1][0], top_a[-1][1] + depth)]
            fill_poly(img, cap_poly, fam)
    top = [P(*p) for p in top_a] + [P(*p) for p in reversed(top_b)]
    fill_poly(img, top, fam)


def curve_pts(x0, y0, x1, y1, bow, n=32):
    out = []
    for i in range(n):
        t = i / (n - 1)
        x = x0 + (x1 - x0) * t
        y = y0 + (y1 - y0) * t
        nx, ny = -(y1 - y0), (x1 - x0)
        L = math.hypot(nx, ny) or 1.0
        b = bow * math.sin(t * math.pi)
        out.append((x + nx / L * b, y + ny / L * b))
    return out


# ================================================================ Curvature (surface family)
def icon_principal_curvature():
    img, d = canvas()
    patch(img)
    # two principal directions as arrows through the centre
    cx, cy = uv(0.5, 0.5)
    ax, ay = uv(0.92, 0.5)
    bx, by = uv(0.5, 0.92)
    arrow(d, cx, cy, ax, ay, "surface", 1.0)
    arrow(d, cx, cy, bx, by, "surface", 1.0)
    dot(d, cx, cy, 1.6)
    finish(img, "PrincipalCurvature")


def icon_gaussian_curvature():
    img, d = canvas()
    patch(img)
    # dome bump: nested contour ellipses around the centre (K > 0)
    for k, (ru, rv) in enumerate(((0.42, 0.42), (0.26, 0.26), (0.11, 0.11))):
        pts = [P(*uv(0.5 + ru * math.cos(a), 0.5 + rv * math.sin(a))) for a in [2 * math.pi * i / 36 for i in range(37)]]
        stroke(d, pts, "surface", 0.8)
    dot(d, *uv(0.5, 0.5), r=1.3)
    finish(img, "GaussianCurvature")


def icon_mean_curvature():
    img, d = canvas()
    patch(img)
    # normal arrow from the centre with a small dome contour
    pts = [P(*uv(0.5 + 0.3 * math.cos(a), 0.5 + 0.3 * math.sin(a))) for a in [2 * math.pi * i / 36 for i in range(37)]]
    stroke(d, pts, "surface", 0.8)
    cx, cy = uv(0.5, 0.5)
    arrow(d, cx, cy, cx, cy - 9.5, "surface", 1.1)
    dot(d, cx, cy, 1.4)
    finish(img, "MeanCurvature")


def icon_streamlines():
    img, d = canvas()
    patch(img)
    for v0 in (0.2, 0.5, 0.8):
        uvline(d, lambda t, v0=v0: (t, v0 + 0.12 * math.sin(t * 2 * math.pi)), "surface", 0.9)
    finish(img, "Streamlines")


def icon_umbilics():
    img, d = canvas()
    patch(img)
    cx, cy = uv(0.5, 0.5)
    for k in range(8):
        a = k * math.pi / 4
        stroke(d, [P(cx + 2.4 * math.cos(a), cy + 2.4 * math.sin(a)), P(cx + 5.6 * math.cos(a), cy + 5.6 * math.sin(a))], "surface", 0.8)
    dot(d, cx, cy, 1.8)
    finish(img, "Umbilics")


# ================================================================ Form finding (mesh family)
def icon_planarization():
    img, d = canvas()
    lattice(img, d, "mesh", 3, 3, nodes=True)
    finish(img, "Planarization")


def icon_minimal_surface():
    img, d = canvas()
    # catenoid-like waist: outline polygon with pinched sides, plus rulings
    n = 20
    left = [(3.0 + 4.0 * (1 - math.cos(math.pi * i / (n - 1)) ** 2) * 0.55, 3 + 18 * i / (n - 1)) for i in range(n)]
    right = [(21.0 - 4.0 * (1 - math.cos(math.pi * i / (n - 1)) ** 2) * 0.55, 3 + 18 * i / (n - 1)) for i in range(n)]
    poly = [P(*p) for p in left] + [P(*p) for p in reversed(right)]
    fill_poly(img, poly, "mesh")
    for y in (7.5, 12, 16.5):
        i = int((y - 3) / 18 * (n - 1))
        stroke(d, [P(*left[i]), P(*right[i])], "mesh", 0.7)
    finish(img, "MinimalSurface")


def icon_force_density():
    img, d = canvas()
    # hanging net between two anchors
    for sag in (5.5, 9.5):
        stroke(d, [P(4 + 16 * i / 24, 5 + sag * math.sin(math.pi * i / 24)) for i in range(25)], "mesh", 0.9)
    for x in (8, 12, 16):
        t = (x - 4) / 16
        stroke(d, [P(x, 5 + 5.5 * math.sin(math.pi * t)), P(x, 5 + 9.5 * math.sin(math.pi * t))], "mesh", 0.7)
        dot(d, x, 5 + 9.5 * math.sin(math.pi * t), 1.2, "mesh")
    dot(d, 4, 5, 1.7, "mesh")
    dot(d, 20, 5, 1.7, "mesh")
    finish(img, "ForceDensity")


def icon_dynamic_relaxation():
    img, d = canvas()
    for sag in (4.5, 8.0, 11.5):
        stroke(d, [P(4 + 16 * i / 24, 4 + sag * math.sin(math.pi * i / 24)) for i in range(25)], "mesh", 0.85)
    arrow(d, 12, 6.5, 12, 18.5, "mesh", 1.0)
    dot(d, 4, 4, 1.7, "mesh")
    dot(d, 20, 4, 1.7, "mesh")
    finish(img, "DynamicRelaxation")


# ================================================================ Gridshells (surface family)
def icon_asymptotic_net():
    img, d = canvas()
    patch(img)
    for c in (-0.6, -0.3, 0.0, 0.3, 0.6):
        uvline(d, lambda t, c=c: (t, t + c + 0.08 * math.sin(t * math.pi)), "surface", 0.85)
        uvline(d, lambda t, c=c: (t, 1 - t + c - 0.08 * math.sin(t * math.pi)), "surface", 0.85)
    finish(img, "AsymptoticNet")


def icon_geodesic_net():
    img, d = canvas()
    patch(img)
    for c in (0.15, 0.38, 0.62, 0.85):
        uvline(d, lambda t, c=c: (t, c + 0.09 * math.sin(t * math.pi) * (1 if c < 0.5 else -1)), "surface", 0.95)
    finish(img, "GeodesicNet")


def icon_chebyshev_net():
    img, d = canvas()
    patch(img)
    # sheared lattice with nodes
    for i in range(1, 4):
        uvline(d, lambda t, i=i: (i / 4 + 0.18 * t, t), "surface", 0.8, n=2)
    for j in range(1, 4):
        uvline(d, lambda t, j=j: (t, j / 4 + 0.18 * t), "surface", 0.8, n=2)
    for i in range(1, 4):
        for j in range(1, 4):
            # intersection of the sheared lines
            u = (i / 4 + 0.18 * (j / 4)) / (1 - 0.18 * 0.18)
            v = j / 4 + 0.18 * u
            dot(d, *uv(u, v), r=1.1)
    finish(img, "ChebyshevNet")


def icon_conjugate_net():
    img, d = canvas()
    patch(img)
    for c in (0.2, 0.5, 0.8):
        uvline(d, lambda t, c=c: (t, c + 0.1 * math.sin(t * math.pi)), "surface", 0.85)
        uvline(d, lambda t, c=c: (c - 0.1 * math.sin(t * math.pi), t), "surface", 0.85)
    finish(img, "ConjugateNet")


def icon_geodesic_path():
    img, d = canvas()
    patch(img)
    uvline(d, lambda t: (0.12 + 0.76 * t, 0.85 - 0.7 * t - 0.16 * math.sin(t * math.pi)), "surface", 1.1)
    dot(d, *uv(0.12, 0.85), r=1.7)
    dot(d, *uv(0.88, 0.15), r=1.7)
    finish(img, "GeodesicPath")


# ================================================================ Analysis (math family)
def icon_lath_analysis():
    img, d = canvas()
    # a bent strip with its bending arrow
    pts = curve_pts(3, 17, 21, 6, 4.0)
    band(img, d, pts, 1.8, 0, "math")
    arrow(d, 12, 20.5, 12, 15.2, "math", 0.9, 1.8)
    finish(img, "LathAnalysis")


def icon_gridshell_analysis():
    img, d = canvas()
    # arch lattice with a load arrow
    n = 30
    outer = [(3 + 18 * i / (n - 1), 21 - 12 * math.sin(math.pi * i / (n - 1))) for i in range(n)]
    inner = [(5 + 14 * i / (n - 1), 21 - 8.5 * math.sin(math.pi * i / (n - 1))) for i in range(n)]
    poly = [P(*p) for p in outer] + [P(*p) for p in reversed(inner)]
    fill_poly(img, poly, "math")
    for i in (7, 15, 22):
        stroke(d, [P(*outer[i]), P(*inner[i])], "math", 0.7)
    arrow(d, 12, 2.5, 12, 8.0, "math", 1.0, 1.8)
    finish(img, "GridshellAnalysis")


def icon_mesh_isocurves():
    img, d = canvas()
    patch(img, "math")
    for ru in (0.42, 0.27, 0.12):
        pts = [P(*uv(0.5 + ru * math.cos(a), 0.5 + ru * math.sin(a))) for a in [2 * math.pi * i / 36 for i in range(37)]]
        stroke(d, pts, "math", 0.85)
    finish(img, "MeshIsocurves")


# ================================================================ Fabrication (timber family)
def icon_lath_sweep():
    img, d = canvas()
    pts = curve_pts(3, 15.5, 21, 7, 3.2)
    band(img, d, pts, 2.0, 3.0, "timber")
    finish(img, "LathSweep")


def icon_net_joints():
    img, d = canvas()
    # horizontal band, notched where the vertical band crosses
    fill_poly(img, [P(2, 9.5), P(9, 9.5), P(9, 14.5), P(2, 14.5)], "timber")
    fill_poly(img, [P(15, 9.5), P(22, 9.5), P(22, 14.5), P(15, 14.5)], "timber")
    fill_poly(img, [P(9.5, 2), P(14.5, 2), P(14.5, 22), P(9.5, 22)], "timber")
    finish(img, "NetJoints")


def icon_lath_unroll():
    img, d = canvas()
    pts = curve_pts(2.5, 10, 12.5, 3, 2.6)
    band(img, d, pts, 1.6, 0, "timber")
    fill_poly(img, [P(3, 17), P(21, 17), P(21, 21), P(3, 21)], "timber")
    arrow(d, 15.5, 7.5, 19.5, 12.5, "timber", 0.9, 1.8)
    finish(img, "LathUnroll")


def icon_lath_segment():
    img, d = canvas()
    fill_poly(img, [P(2, 10), P(22, 10), P(22, 14.5), P(2, 14.5)], "timber")
    for x in (9, 15):
        stroke(d, [P(x, 7.2), P(x, 17.3)], "timber", 0.8)
        dot(d, x, 6.2, 1.2, "timber")
    finish(img, "LathSegment")


def icon_lath_preview():
    img, d = canvas()
    fill_poly(img, [P(2, 4), P(22, 4), P(22, 8.5), P(2, 8.5)], "timber")
    # utilization bar: three steps of the family gradient
    for i, (t0, t1) in enumerate(((0, 6), (6, 12), (12, 18))):
        c0, c1 = hexrgb(FAM["timber"]["top"]), hexrgb(FAM["timber"]["bot"])
        f = i / 2
        col = tuple(int(c0[k] + (c1[k] - c0[k]) * f) for k in range(3)) + (255,)
        d.rectangle([P(3 + t0, 14), P(3 + t1, 19)], fill=col)
    d.rectangle([P(3, 14), P(21, 19)], outline=hexrgb(FAM["timber"]["edge"]) + (255,), width=int(0.8 * S))
    d.polygon([P(13.8, 10.6), P(16.2, 10.6), P(15, 13.4)], fill=hexrgb(FAM["timber"]["edge"]) + (255,))
    finish(img, "LathPreview")


def icon_lath_labels():
    img, d = canvas()
    fill_poly(img, [P(3, 8), P(13, 8), P(21, 15), P(13, 22), P(3, 22)], "timber")
    dot(d, 7, 15, 1.6, "timber")
    stroke(d, [P(11, 3), P(20, 3)], "timber", 1.0)
    stroke(d, [P(11, 5.6), P(17, 5.6)], "timber", 1.0)
    finish(img, "LathLabels")


def icon_net_topology():
    img, d = canvas()
    lattice(img, d, "timber", 3, 3, nodes=True)
    finish(img, "NetTopology")


# ================================================================ Util (parameter greys)
def icon_mesh_cleanup():
    img, d = canvas()
    q = [(2.5, 10.0), (13.0, 5.5), (19.0, 13.0), (8.0, 21.0)]
    lattice(img, d, "param", 3, 3, nodes=False, quad=q)
    cx, cy = 18.5, 5.5
    for a in (0, math.pi / 2):
        stroke(d, [P(cx + 3.2 * math.cos(a), cy + 3.2 * math.sin(a)), P(cx - 3.2 * math.cos(a), cy - 3.2 * math.sin(a))], "param", 0.9)
    for a in (math.pi / 4, -math.pi / 4):
        stroke(d, [P(cx + 1.8 * math.cos(a), cy + 1.8 * math.sin(a)), P(cx - 1.8 * math.cos(a), cy - 1.8 * math.sin(a))], "param", 0.7)
    finish(img, "MeshCleanup")


def icon_pull_to_mesh():
    img, d = canvas()
    q = [(2.5, 13.0), (15.0, 9.0), (21.5, 16.0), (9.0, 21.5)]
    patch(img, "param", q)
    pts = [P(4 + 16 * i / 30, 5 + 1.4 * math.sin(i / 30 * 2 * math.pi + 1)) for i in range(31)]
    stroke(d, pts, "param", 1.0)
    for x in (8, 16):
        arrow(d, x, 7.5, x, 12.5, "param", 0.8, 1.7)
    finish(img, "PullToMesh")


def icon_mesh_colour_map():
    img, d = canvas()
    q = [(2.5, 11.0), (15.0, 6.5), (21.5, 14.0), (9.0, 21.5)]
    patch(img, "surface", q)
    # colour bar in the surface family (light to dark = low to high)
    c0, c1 = hexrgb(FAM["surface"]["top"]), hexrgb(FAM["surface"]["bot"])
    for i in range(6):
        f = i / 5
        col = tuple(int(c0[k] + (c1[k] - c0[k]) * f) for k in range(3)) + (255,)
        d.rectangle([P(3 + 3 * i, 2.5), P(6 + 3 * i, 5.5)], fill=col)
    d.rectangle([P(3, 2.5), P(21, 5.5)], outline=hexrgb(FAM["surface"]["edge"]) + (255,), width=int(0.8 * S))
    finish(img, "MeshColourMap")


def icon_tab():
    """Tab icon: the small-scale Mite mark (tools/mite_knot.png — the knot
    alone, black on transparent, from mite_logo_small.eps) reduced to 24 px.
    The strokes are thickened first so the six lobes still read at this size;
    the full hexagon-tile logo stays the package icon (icon.png)."""
    src = Image.open(os.path.join(os.path.dirname(__file__), "mite_knot.png")).convert("RGBA")
    src = src.crop(src.getchannel("A").getbbox())
    mask = src.getchannel("A").filter(ImageFilter.MaxFilter(9))
    tile = Image.new("RGBA", src.size, (0, 0, 0, 0))
    tile.paste(Image.new("RGBA", src.size, (15, 15, 15, 255)), (0, 0), mask)
    sc = SIZE / max(src.size)
    im = tile.resize((max(1, round(src.width * sc)), max(1, round(src.height * sc))), Image.LANCZOS)
    out = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    out.paste(im, ((SIZE - im.width) // 2, (SIZE - im.height) // 2), im)
    out.save(os.path.join(OUT, "Mite_Tab.png"))


ALL = [icon_principal_curvature, icon_gaussian_curvature, icon_mean_curvature, icon_streamlines, icon_umbilics,
       icon_planarization, icon_minimal_surface, icon_force_density, icon_dynamic_relaxation,
       icon_asymptotic_net, icon_geodesic_net, icon_chebyshev_net, icon_conjugate_net, icon_geodesic_path,
       icon_lath_analysis, icon_gridshell_analysis, icon_mesh_isocurves,
       icon_lath_sweep, icon_net_joints, icon_lath_unroll, icon_lath_segment, icon_lath_preview, icon_lath_labels, icon_net_topology,
       icon_mesh_cleanup, icon_pull_to_mesh, icon_mesh_colour_map, icon_tab]

if __name__ == "__main__":
    for fn in ALL:
        fn()
    print(len(ALL), "icons written to", os.path.abspath(OUT))
