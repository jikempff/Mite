#!/usr/bin/env python3
"""Generates the 24x24 Grasshopper component icons for Mite (and the tab icon)
in the Mite palette: black, white and neon.

Palette (the web app's, src/Mite.Web/wwwroot/css/app.css, and the 1.4.0
`MitePalette`): ink #0a0a0a, white #ffffff, family A neon blue #2a3cff,
family B neon red #ff1f4f, geodesic / form-finding neon green #00d86b,
analysis neon yellow #d9ff00, util grey #bdbdbd. Colours mean the same thing
as in the app and the bench: blue and red are the two lath families, green a
geodesic family or a form-found mesh, yellow an analysis, white a surface or
a cut sheet. The utilization bar of Lath Preview is the app's `UTIL_STOPS`
ramp and the bar of Mesh Colour Map its diverging blue–white–red map.

Drawing rules, after David Rutten's own (McNeel developer guide "Grasshopper
Icons", and his reply in discourse.mcneel.com/t/74786): 24 x 24 px with a
2 px empty border; one or two pixel lines aligned with the pixel grid, no
near-horizontal or near-vertical lines; "region outlines are drawn in a dark
version of the region fill colour" and never in full black ("I no longer
recommend that"); gradients only "to indicate lighting/shadowing, but keep it
subtle" (here 14 % lighter towards the upper left); the drop shadow is a
raster post-process, 1 px right and down, 2 px blur, about a third dark.
Stroke weights follow Material's system-icon rule (2 dp strokes, 1.5 dp for
small detail, nothing thinner): lines are 1.2–2 px here. Black (#0a0a0a) is
reserved for marks — arrows, nodes, text, cut lines — so that a glance tells
the geometry (neon) from the annotation (black). Node dots are white with a
ring in the family's dark shade.

Legibility is checked by tests/Mite.Tests/IconTests.cs: every opaque pixel
is a palette colour, a shade of one, or an anti-aliased blend of two; and at
least a third of every icon's area contrasts 3 : 1 (WCAG 2.1 SC 1.4.11,
non-text contrast) against both a light component capsule (#d2d2d2) and a
dark menu (#2d2d2d) — a white region with a dark rim and neon lines reads on
both. Drawn at 8x and downsampled by area averaging (a box filter: no ringing,
so every pixel is a true blend of the colours drawn).

Run: python3 tools/generate_icons.py (writes src/Mite.Grasshopper/Resources,
the 2x copies for the web app, src/Mite.Web/wwwroot/icons, the bench's
tools/webgen/icons.json and tools/icon_palette.json for the tests).
"""
import json
import math
import os
from PIL import Image, ImageDraw, ImageFilter

S = 8
SIZE = 24
OUT = os.path.join(os.path.dirname(__file__), "..", "src", "Mite.Grasshopper", "Resources")
WEB_OUT = os.path.join(os.path.dirname(__file__), "..", "src", "Mite.Web", "wwwroot", "icons")

INK = "#0a0a0a"
PALETTE = {
    "ink": INK, "white": "#ffffff",
    "blue": "#2a3cff", "red": "#ff1f4f", "green": "#00d86b", "yellow": "#d9ff00", "grey": "#bdbdbd",
}


def hexrgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def rgbhex(c):
    return "#%02x%02x%02x" % tuple(max(0, min(255, int(round(v)))) for v in c)


def shade(h, f):
    """f < 1 darkens towards black, f > 1 lightens towards white (by 1 - 1/f)."""
    c = hexrgb(h)
    if f <= 1:
        return rgbhex(tuple(v * f for v in c))
    t = 1 - 1 / f
    return rgbhex(tuple(v + (255 - v) * t for v in c))


def family(fill, edge=None, line=None, light=None):
    """fill: the region colour; edge: its outline (a dark shade of the fill);
    line: the colour of free lines drawn in this family; light: the top of
    the subtle gradient."""
    return dict(fill=fill, edge=edge or shade(fill, 0.5), line=line or fill, light=light or shade(fill, 1.16))


# One entry per meaning. The old family names (surface, mesh, math, param,
# timber) stay valid so icon functions written for the amber set still draw.
FAM = {
    "white": family(PALETTE["white"], edge="#4a4a4a", line="#4a4a4a", light="#ffffff"),
    # blue is dark (luminance 0.11): its regions get a stronger light from the upper left
    # so they also read on a dark menu — the rim does the work on a light capsule
    "a": family(PALETTE["blue"], light="#a3aaff"),
    "b": family(PALETTE["red"]),
    "g": family(PALETTE["green"], line="#00b85b"),
    "y": family(PALETTE["yellow"], edge="#5f6e00", line="#5f6e00"),
    "grey": family(PALETTE["grey"], edge="#4a4a4a", line="#4a4a4a"),
    "ink": family(INK, edge=INK, line=INK, light="#2a2a2a"),
}
FAM["surface"] = FAM["white"]
FAM["mesh"] = FAM["g"]
FAM["math"] = FAM["y"]
FAM["param"] = FAM["grey"]
FAM["timber"] = FAM["a"]
WHITE = (255, 255, 255, 255)
INK_RGBA = hexrgb(INK) + (255,)

# the app's utilization ramp (green below 0.7, yellow at 1, red above) and its diverging map
UTIL_STOPS = [(0.0, "#00d86b"), (0.54, "#8cf21a"), (0.769, "#d9ff00"), (0.77, "#ff1f4f"), (1.0, "#bf173b")]
DIVERGING_STOPS = [(0.0, "#2a3cff"), (0.5, "#ffffff"), (1.0, "#ff1f4f")]

# the colours a reader may find in an icon: every fill, edge, line, gradient top, side shade and ramp stop
ALLOWED = sorted({v for f in FAM.values() for v in (f["fill"], f["edge"], f["line"], f["light"], shade(f["fill"], 0.78))}
                 | {c for _, c in UTIL_STOPS} | {c for _, c in DIVERGING_STOPS})


LIVE = (SIZE - 4) / SIZE   # drawings use 0..24 coordinates; they land in the 20 x 20 live area inside the 2 px border


def P(x, y):
    return ((2 + x * LIVE) * S, (2 + y * LIVE) * S)


def canvas():
    img = Image.new("RGBA", (SIZE * S, SIZE * S), (0, 0, 0, 0))
    return img, ImageDraw.Draw(img)


def gradient_image(top, bot, box):
    """Light upper-left to the fill colour lower-right across the region's own box (subtle)."""
    w = SIZE * S
    g = Image.new("RGBA", (w, w))
    px = g.load()
    a, b = hexrgb(top), hexrgb(bot)
    x0, y0, x1, y1 = box
    span = max(1.0, (x1 - x0) + (y1 - y0))
    for y in range(w):
        for x in range(w):
            t = max(0.0, min(1.0, ((x - x0) + (y - y0)) / span))
            px[x, y] = (int(a[0] + (b[0] - a[0]) * t), int(a[1] + (b[1] - a[1]) * t), int(a[2] + (b[2] - a[2]) * t), 255)
    return g


_GRAD = {}


def _grad(fam, pts):
    xs = [p[0] for p in pts]
    ys = [p[1] for p in pts]
    box = (min(xs), min(ys), max(xs), max(ys))
    key = (FAM[fam]["light"], FAM[fam]["fill"], box)
    if key not in _GRAD:
        _GRAD[key] = gradient_image(key[0], key[1], box)
    return _GRAD[key]


def fill_poly(img, pts, fam, outline=True, width=1.2):
    """Gradient-filled polygon outlined in the family's dark edge colour."""
    mask = Image.new("L", img.size, 0)
    ImageDraw.Draw(mask).polygon(pts, fill=255)
    img.paste(_grad(fam, pts), (0, 0), mask)
    if outline:
        ImageDraw.Draw(img).line(pts + [pts[0]], fill=hexrgb(FAM[fam]["edge"]) + (255,), width=int(width * S), joint="curve")


def fill_ellipse(img, box, fam, outline=True):
    mask = Image.new("L", img.size, 0)
    ImageDraw.Draw(mask).ellipse(box, fill=255)
    img.paste(_grad(fam, [box[0], box[1]]), (0, 0), mask)
    if outline:
        ImageDraw.Draw(img).ellipse(box, outline=hexrgb(FAM[fam]["edge"]) + (255,), width=int(1.2 * S))


def stroke(d, pts, fam, width=1.2, alpha=255, rim=False, dark=False):
    """A free line in the family's line colour (neon for the families, dark grey
    on white). rim=True draws it on a line in the family's edge colour one pixel
    wider: a line on the bare canvas (not on a patch) needs the dark rim to read
    on a light capsule, since no flat colour contrasts 3 : 1 with both canvases."""
    if rim:
        d.line(pts, fill=hexrgb(FAM[fam]["edge"]) + (alpha,), width=max(1, int((width + 1.0) * S)), joint="curve")
    d.line(pts, fill=hexrgb(FAM[fam]["edge" if dark else "line"]) + (alpha,), width=max(1, int(width * S)), joint="curve")


def halo(d, draw_fn, rim=0.8):
    """Draw with draw_fn(layer_draw) in ink on a white halo `rim` px wide, so
    the mark reads on a dark menu as well as on a light capsule."""
    img = d._image
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    draw_fn(ImageDraw.Draw(layer))
    k = 2 * int(rim * S) + 1
    rim_mask = layer.split()[3].filter(ImageFilter.MaxFilter(k))
    img.paste(Image.new("RGBA", img.size, WHITE), (0, 0), rim_mask)
    img.paste(layer, (0, 0), layer)


def mark(d, pts, width=1.5):
    """A line in ink (with its white halo): cut lines, text, axes."""
    halo(d, lambda ld: ld.line(pts, fill=INK_RGBA, width=max(1, int(width * S)), joint="curve"))


def dot(d, x, y, r=1.7, fam="surface", ink=False):
    """A node: white with a ring in the family's edge colour; ink=True for a solid black node on a white halo."""
    if ink:
        halo(d, lambda ld: ld.ellipse([P(x - r, y - r), P(x + r, y + r)], fill=INK_RGBA), 0.7)
        return
    d.ellipse([P(x - r, y - r), P(x + r, y + r)], fill=WHITE, outline=hexrgb(FAM[fam]["edge"]) + (255,), width=int(0.7 * S))


def arrow(d, x0, y0, x1, y1, fam="ink", width=1.5, head=2.4):
    """An arrow; in ink on a white halo unless a family is named."""
    col = INK_RGBA if fam == "ink" else hexrgb(FAM[fam]["line"]) + (255,)
    ang = math.atan2(y1 - y0, x1 - x0)
    tip = (x1, y1)
    l = (x1 - head * math.cos(ang - 0.5), y1 - head * math.sin(ang - 0.5))
    r = (x1 - head * math.cos(ang + 0.5), y1 - head * math.sin(ang + 0.5))

    def draw(ld):
        ld.line([P(x0, y0), P(x1, y1)], fill=col, width=max(1, int(width * S)), joint="curve")
        ld.polygon([P(*tip), P(*l), P(*r)], fill=col)

    if fam == "ink":
        halo(d, draw)
    else:
        draw(d)


def finish(img, name):
    """Drop shadow (+1, +1, blur 2 px, ~32%) beneath, then downsample."""
    alpha = img.split()[3]
    shadow = Image.new("RGBA", img.size, (0, 0, 0, 0))
    sh = Image.new("RGBA", img.size, (10, 10, 10, 255))
    shadow.paste(sh, (S, S), alpha)
    shadow = shadow.filter(ImageFilter.GaussianBlur(1.2 * S))
    r, g, b, a = shadow.split()
    a = a.point(lambda v: int(v * 0.32))
    shadow = Image.merge("RGBA", (r, g, b, a))
    out = Image.alpha_composite(shadow, img)
    os.makedirs(OUT, exist_ok=True)
    out.resize((SIZE, SIZE), Image.BOX).save(os.path.join(OUT, name + ".png"))
    # 2x copy for the web app (crisp on HiDPI screens, shown at 16-20 px)
    os.makedirs(WEB_OUT, exist_ok=True)
    out.resize((2 * SIZE, 2 * SIZE), Image.BOX).save(os.path.join(WEB_OUT, name + ".png"))


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


def uvline(d, f, fam, width=1.2, n=24, quad=PATCH):
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


def lattice(img, d, fam, nu=3, nv=3, nodes=True, quad=PATCH, famU=None, famV=None):
    """A patch with nu x nv cells; the u lines in famU (default the patch family), the v lines in famV.
    Lines of the patch's own family are drawn in its dark shade (a dark lattice on the patch)."""
    patch(img, fam, quad)
    famU = famU or fam
    famV = famV or famU
    for i in range(1, nu):
        stroke(d, [P(*uv(i / nu, 0, quad)), P(*uv(i / nu, 1, quad))], famU, 1.2, dark=famU == fam)
    for j in range(1, nv):
        stroke(d, [P(*uv(0, j / nv, quad)), P(*uv(1, j / nv, quad))], famV, 1.2, dark=famV == fam)
    if nodes:
        for i in range(1, nu):
            for j in range(1, nv):
                x, y = uv(i / nu, j / nv, quad)
                dot(d, x, y, 1.2, famU)


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
        # the side face in the family's dark shade (lighting from the upper left)
        mask = Image.new("L", img.size, 0)
        ImageDraw.Draw(mask).polygon(side, fill=255)
        img.paste(Image.new("RGBA", img.size, hexrgb(shade(FAM[fam]["fill"], 0.78)) + (255,)), (0, 0), mask)
        ImageDraw.Draw(img).line(side + [side[0]], fill=hexrgb(FAM[fam]["edge"]) + (255,), width=int(1.2 * S), joint="curve")
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


def ramp(d, box, stops, n=18):
    """A colour bar sampled from (t, '#hex') stops, left to right, inside box = (x0, y0, x1, y1) in icon units."""
    x0, y0, x1, y1 = box
    for i in range(n):
        t = (i + 0.5) / n
        col = hexrgb(stops[-1][1]) + (255,)
        for k in range(1, len(stops)):
            if t <= stops[k][0]:
                (t0, c0), (t1, c1) = stops[k - 1], stops[k]
                f = (t - t0) / max(1e-9, t1 - t0)
                a, b = hexrgb(c0), hexrgb(c1)
                col = tuple(int(a[j] + (b[j] - a[j]) * f) for j in range(3)) + (255,)
                break
        d.rectangle([P(x0 + (x1 - x0) * i / n, y0), P(x0 + (x1 - x0) * (i + 1) / n, y1)], fill=col)
    d.rectangle([P(x0, y0), P(x1, y1)], outline=hexrgb("#4a4a4a") + (255,), width=int(0.8 * S))


# ================================================================ Curvature (white surface, blue / red marks)
def icon_principal_curvature():
    img, d = canvas()
    patch(img)
    # the two principal directions through the centre: k1 blue, k2 red
    cx, cy = uv(0.5, 0.5)
    ax, ay = uv(0.94, 0.5)
    bx, by = uv(0.5, 0.94)
    ax2, ay2 = uv(0.06, 0.5)
    bx2, by2 = uv(0.5, 0.06)
    stroke(d, [P(ax2, ay2), P(cx, cy)], "a", 1.4)
    stroke(d, [P(bx2, by2), P(cx, cy)], "b", 1.4)
    arrow(d, cx, cy, ax, ay, "a", 1.4, 2.4)
    arrow(d, cx, cy, bx, by, "b", 1.4, 2.4)
    dot(d, cx, cy, 1.5, ink=True)
    finish(img, "PrincipalCurvature")


def icon_gaussian_curvature():
    img, d = canvas()
    patch(img)
    # a dome bump: nested contours around the centre (K > 0), blue
    for ru in (0.42, 0.26, 0.11):
        pts = [P(*uv(0.5 + ru * math.cos(a), 0.5 + ru * math.sin(a))) for a in [2 * math.pi * i / 36 for i in range(37)]]
        stroke(d, pts, "a", 1.2)
    dot(d, *uv(0.5, 0.5), r=1.3, ink=True)
    finish(img, "GaussianCurvature")


def icon_mean_curvature():
    img, d = canvas()
    patch(img)
    # one contour and the normal arrow from the centre
    pts = [P(*uv(0.5 + 0.3 * math.cos(a), 0.5 + 0.3 * math.sin(a))) for a in [2 * math.pi * i / 36 for i in range(37)]]
    stroke(d, pts, "a", 1.2)
    cx, cy = uv(0.5, 0.5)
    arrow(d, cx, cy, cx, cy - 9.5, "ink", 1.5, 2.4)
    dot(d, cx, cy, 1.4, ink=True)
    finish(img, "MeanCurvature")


def icon_streamlines():
    img, d = canvas()
    patch(img)
    # three streamlines of the maximum direction (blue) crossed by one of the minimum (red)
    for v0 in (0.2, 0.5, 0.8):
        uvline(d, lambda t, v0=v0: (t, v0 + 0.12 * math.sin(t * 2 * math.pi)), "a", 1.3)
    uvline(d, lambda t: (0.5 - 0.06 * math.sin(t * 2 * math.pi), t), "b", 1.3)
    finish(img, "Streamlines")


def icon_umbilics():
    img, d = canvas()
    patch(img)
    # the star of an umbilic (every direction principal), red, on a black point
    cx, cy = uv(0.5, 0.5)
    for k in range(8):
        a = k * math.pi / 4
        stroke(d, [P(cx + 2.6 * math.cos(a), cy + 2.6 * math.sin(a)), P(cx + 6.0 * math.cos(a), cy + 6.0 * math.sin(a))], "b", 1.3)
    dot(d, cx, cy, 1.8, ink=True)
    finish(img, "Umbilics")


def icon_test_shape():
    """An analytic test surface: a torus seen in parallels and meridians with a tick for the exact value."""
    img, d = canvas()
    patch(img)
    # parallels (u) and meridians (v) of the chart as the two coordinate families
    for v0 in (0.25, 0.5, 0.75):
        uvline(d, lambda t, v0=v0: (t, v0), "surface", 0.8)
    for u0 in (0.25, 0.5, 0.75):
        uvline(d, lambda t, u0=u0: (u0, t), "surface", 0.8)
    # the check mark: the truth is known here
    stroke(d, [P(14.5, 4.5), P(17, 7.2), P(21.5, 2.2)], "surface", 1.3)
    finish(img, "TestShape")


# ================================================================ Form finding (mesh family)
# ================================================================ Form finding (green mesh)
def icon_planarization():
    img, d = canvas()
    lattice(img, d, "g", 3, 3, nodes=True)
    finish(img, "Planarization")


def icon_minimal_surface():
    img, d = canvas()
    # catenoid-like waist: outline polygon with pinched sides, plus rulings
    n = 20
    left = [(3.0 + 4.0 * (1 - math.cos(math.pi * i / (n - 1)) ** 2) * 0.55, 3 + 18 * i / (n - 1)) for i in range(n)]
    right = [(21.0 - 4.0 * (1 - math.cos(math.pi * i / (n - 1)) ** 2) * 0.55, 3 + 18 * i / (n - 1)) for i in range(n)]
    poly = [P(*p) for p in left] + [P(*p) for p in reversed(right)]
    fill_poly(img, poly, "g")
    for y in (7.5, 12, 16.5):
        i = int((y - 3) / 18 * (n - 1))
        d.line([P(*left[i]), P(*right[i])], fill=hexrgb(FAM["g"]["edge"]) + (255,), width=int(1.1 * S))
    finish(img, "MinimalSurface")


def icon_force_density():
    img, d = canvas()
    # a hanging net between two anchors (black), green cables, white nodes
    for sag in (5.5, 9.5):
        stroke(d, [P(4 + 16 * i / 24, 5 + sag * math.sin(math.pi * i / 24)) for i in range(25)], "g", 1.5, rim=True)
    for x in (8, 12, 16):
        t = (x - 4) / 16
        stroke(d, [P(x, 5 + 5.5 * math.sin(math.pi * t)), P(x, 5 + 9.5 * math.sin(math.pi * t))], "g", 1.2, rim=True)
        dot(d, x, 5 + 9.5 * math.sin(math.pi * t), 1.2, "g")
    dot(d, 4, 5, 1.7, ink=True)
    dot(d, 20, 5, 1.7, ink=True)
    finish(img, "ForceDensity")


def icon_dynamic_relaxation():
    img, d = canvas()
    # the same cable settling through three states, the motion arrow in ink
    for sag in (4.5, 8.0, 11.5):
        stroke(d, [P(4 + 16 * i / 24, 4 + sag * math.sin(math.pi * i / 24)) for i in range(25)], "g", 1.4, rim=True)
    arrow(d, 12, 6.0, 12, 18.5, "ink", 1.5, 2.4)
    dot(d, 4, 4, 1.7, ink=True)
    dot(d, 20, 4, 1.7, ink=True)
    finish(img, "DynamicRelaxation")


# ================================================================ Gridshells (white surface, family A blue, B red, G green)
def icon_asymptotic_net():
    img, d = canvas()
    patch(img)
    for c in (-0.5, 0.0, 0.5):
        uvline(d, lambda t, c=c: (t, t + c + 0.08 * math.sin(t * math.pi)), "a", 1.4)
    for c in (-0.5, 0.0, 0.5):
        uvline(d, lambda t, c=c: (t, 1 - t + c - 0.08 * math.sin(t * math.pi)), "b", 1.4)
    finish(img, "AsymptoticNet")


def icon_asymptotic_web():
    """Schling's symmetric web: six rays from the singular centre node (A), the
    arcs through the ray nodes (B), nodes marked."""
    img, d = canvas()
    patch(img)
    cx, cy = 0.5, 0.5
    for k in range(6):
        a = math.pi / 6 + k * math.pi / 3
        uvline(d, lambda t, a=a: (cx + 0.5 * t * math.cos(a), cy + 0.5 * t * math.sin(a)), "a", 1.3, n=8)
    for r in (0.2, 0.36):
        for k in range(6):
            a0 = math.pi / 6 + k * math.pi / 3
            a1 = a0 + math.pi / 3
            uvline(d, lambda t, a0=a0, a1=a1, r=r: (cx + r * math.cos(a0 + t * (a1 - a0)) * (1 - 0.12 * math.sin(math.pi * t)),
                                                   cy + r * math.sin(a0 + t * (a1 - a0)) * (1 - 0.12 * math.sin(math.pi * t))), "b", 1.2, n=12)
    for r in (0.2, 0.36):
        for k in range(6):
            a = math.pi / 6 + k * math.pi / 3
            x, y = uv(cx + r * math.cos(a), cy + r * math.sin(a))
            dot(d, x, y, 1.0, "a")
    x, y = uv(cx, cy)
    dot(d, x, y, 1.6, ink=True)
    finish(img, "AsymptoticWeb")


def icon_agg_web():
    """AGG web: a rhombic lattice of two geodesic families (thin, bowed) with
    one family of straight asymptotic diagonals (bold), nodes where all three meet."""
    img, d = canvas()
    patch(img)
    # geodesic families: u + v = c and u − v = c, slightly bowed
    for c in (0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 1.75):
        uvline(d, lambda t, c=c: (t, c - t + 0.05 * math.sin(t * math.pi)), "surface", 0.8)
    for c in (-0.75, -0.5, -0.25, 0.0, 0.25, 0.5, 0.75):
        uvline(d, lambda t, c=c: (t, t + c - 0.05 * math.sin(t * math.pi)), "surface", 0.8)
    # asymptotic diagonals: the straight u = const lines through the nodes
    for u in (0.25, 0.5, 0.75):
        uvline(d, lambda t, u=u: (u, t), "surface", 1.3, n=2)
    for u in (0.25, 0.5, 0.75):
        for v in (0.25, 0.5, 0.75):
            x, y = uv(u, v)
            dot(d, x, y, 1.1, "surface")
    finish(img, "AggWeb")


def icon_geodesic_net():
    img, d = canvas()
    patch(img)
    for c in (0.15, 0.38, 0.62, 0.85):
        uvline(d, lambda t, c=c: (t, c + 0.09 * math.sin(t * math.pi) * (1 if c < 0.5 else -1)), "g", 1.4)
    finish(img, "GeodesicNet")


def icon_chebyshev_net():
    img, d = canvas()
    patch(img)
    # a sheared lattice of equal edges with its nodes
    for i in range(1, 4):
        uvline(d, lambda t, i=i: (i / 4 + 0.18 * t, t), "a", 1.2, n=2)
    for j in range(1, 4):
        uvline(d, lambda t, j=j: (t, j / 4 + 0.18 * t), "b", 1.2, n=2)
    for i in range(1, 4):
        for j in range(1, 4):
            u = (i / 4 + 0.18 * (j / 4)) / (1 - 0.18 * 0.18)
            v = j / 4 + 0.18 * u
            dot(d, *uv(u, v), r=1.1, fam="a")
    finish(img, "ChebyshevNet")


def icon_conjugate_net():
    img, d = canvas()
    patch(img)
    for c in (0.2, 0.5, 0.8):
        uvline(d, lambda t, c=c: (t, c + 0.1 * math.sin(t * math.pi)), "a", 1.3)
    for c in (0.2, 0.5, 0.8):
        uvline(d, lambda t, c=c: (c - 0.1 * math.sin(t * math.pi), t), "b", 1.3)
    finish(img, "ConjugateNet")


def icon_geodesic_path():
    img, d = canvas()
    patch(img)
    uvline(d, lambda t: (0.12 + 0.76 * t, 0.85 - 0.7 * t - 0.16 * math.sin(t * math.pi)), "g", 1.6)
    dot(d, *uv(0.12, 0.85), r=1.7, ink=True)
    dot(d, *uv(0.88, 0.15), r=1.7, ink=True)
    finish(img, "GeodesicPath")


# ================================================================ Analysis (yellow)
def icon_lath_analysis():
    img, d = canvas()
    # a bent strip with its bending arrow
    pts = curve_pts(3, 17, 21, 6, 4.0)
    band(img, d, pts, 2.0, 0, "y")
    arrow(d, 12, 21.0, 12, 15.4, "ink", 1.5, 2.2)
    finish(img, "LathAnalysis")


def icon_net_distributions():
    img, d = canvas()
    # a histogram of spacings: bars around the target, a target tick above
    heights = [4.0, 9.0, 15.0, 12.0, 6.5, 3.0]
    x = 3.0
    for h in heights:
        fill_poly(img, [P(x, 21), P(x + 3.0, 21), P(x + 3.0, 21 - h), P(x, 21 - h)], "math")
        x += 3.0
    stroke(d, [P(2.0, 21.2), P(22.0, 21.2)], "ink", 1.2)
    arrow(d, 10.5, 1.5, 10.5, 4.6, "ink", 1.2, 2.0)
    finish(img, "NetDistributions")


def icon_gridshell_analysis():
    img, d = canvas()
    # arch lattice with a load arrow
    n = 30
    outer = [(3 + 18 * i / (n - 1), 21 - 12 * math.sin(math.pi * i / (n - 1))) for i in range(n)]
    inner = [(5 + 14 * i / (n - 1), 21 - 8.5 * math.sin(math.pi * i / (n - 1))) for i in range(n)]
    poly = [P(*p) for p in outer] + [P(*p) for p in reversed(inner)]
    fill_poly(img, poly, "y")
    for i in (7, 15, 22):
        d.line([P(*outer[i]), P(*inner[i])], fill=hexrgb(FAM["y"]["edge"]) + (255,), width=int(1.0 * S))
    arrow(d, 12, 2.5, 12, 8.4, "ink", 1.5, 2.2)
    finish(img, "GridshellAnalysis")


def icon_mesh_isocurves():
    img, d = canvas()
    patch(img)
    # level lines of a scalar field, yellow with a dark rim so they read on white
    for ru in (0.42, 0.19):
        pts = [P(*uv(0.5 + ru * math.cos(a), 0.5 + ru * math.sin(a))) for a in [2 * math.pi * i / 36 for i in range(37)]]
        d.line(pts, fill=hexrgb(FAM["y"]["edge"]) + (255,), width=int(2.2 * S), joint="curve")
        d.line(pts, fill=hexrgb(FAM["y"]["fill"]) + (255,), width=int(1.2 * S), joint="curve")
    finish(img, "MeshIsocurves")


# ================================================================ Fabrication (family-coloured laths, white sheets)
def icon_lath_sweep():
    img, d = canvas()
    pts = curve_pts(3, 15.5, 21, 7, 3.2)
    band(img, d, pts, 2.0, 3.0, "a")
    finish(img, "LathSweep")


def icon_net_joints():
    img, d = canvas()
    # a B lath notched where the A lath crosses it
    fill_poly(img, [P(2, 9.5), P(9, 9.5), P(9, 14.5), P(2, 14.5)], "b")
    fill_poly(img, [P(15, 9.5), P(22, 9.5), P(22, 14.5), P(15, 14.5)], "b")
    fill_poly(img, [P(9.5, 2), P(14.5, 2), P(14.5, 22), P(9.5, 22)], "a")
    finish(img, "NetJoints")


def icon_lath_unroll():
    img, d = canvas()
    # the bent lath above, its straight pattern on the sheet below
    pts = curve_pts(2.5, 10, 12.5, 3, 2.6)
    band(img, d, pts, 1.6, 0, "a")
    fill_poly(img, [P(3, 17), P(21, 17), P(21, 21), P(3, 21)], "white")
    arrow(d, 15.5, 7.5, 19.5, 12.5, "ink", 1.4, 2.0)
    finish(img, "LathUnroll")


def icon_lath_strip():
    """A plank twisting along its lath: a ribbon whose apparent width swings
    through zero, its oblique rulings drawn across it."""
    img, d = canvas()
    n = 7
    xs = [2.5 + 19.0 * i / (n - 1) for i in range(n)]
    def edge(i, side):
        t = i / (n - 1)
        h = 5.2 * math.cos(math.pi * (0.15 + 0.85 * t))        # apparent half-width, turns over
        lean = 1.6 * math.sin(math.pi * t)                      # rulings lean along the lath
        yc = 12 - 2.5 * math.sin(math.pi * t)
        return (xs[i] + side * lean * 0.5, yc - side * h)
    for i in range(n - 1):
        quad = [P(*edge(i, 1)), P(*edge(i + 1, 1)), P(*edge(i + 1, -1)), P(*edge(i, -1))]
        # the far face, once the plank has turned over, in the other family's colour
        # (a darkened face read as nothing on a dark menu)
        fill_poly(img, quad, "b" if i >= (n - 1) // 2 else "timber", width=0.7)
    for i in range(1, n - 1):
        stroke(d, [P(*edge(i, 1)), P(*edge(i, -1))], "b" if i >= (n - 1) // 2 else "timber", 0.7)
    finish(img, "LathStrip")


def icon_lath_segment():
    img, d = canvas()
    fill_poly(img, [P(2, 10), P(22, 10), P(22, 14.5), P(2, 14.5)], "a")
    for x in (9, 15):
        mark(d, [P(x, 7.2), P(x, 17.3)], 1.3)
        dot(d, x, 6.0, 1.2, ink=True)
    finish(img, "LathSegment")


def icon_lath_preview():
    img, d = canvas()
    fill_poly(img, [P(2, 4), P(22, 4), P(22, 8.5), P(2, 8.5)], "a")
    # the utilization bar of the app: green below 0.7, yellow at the limit, red above
    ramp(d, (3, 14, 21, 19), UTIL_STOPS)
    d.polygon([P(13.3, 10.4), P(16.7, 10.4), P(15, 13.6)], fill=INK_RGBA)
    finish(img, "LathPreview")


def icon_lath_labels():
    img, d = canvas()
    fill_poly(img, [P(3, 8), P(13, 8), P(21, 15), P(13, 22), P(3, 22)], "white")
    dot(d, 7, 15, 1.6, ink=True)
    mark(d, [P(11, 3), P(20, 3)], 1.6)
    mark(d, [P(11, 5.8), P(17, 5.8)], 1.6)
    finish(img, "LathLabels")


def icon_net_topology():
    img, d = canvas()
    lattice(img, d, "white", 3, 3, nodes=True, famU="a", famV="b")
    finish(img, "NetTopology")


def icon_mite_net():
    """Mite Net: the two lath families on a patch gathered into one wire — a
    white plug dot on the right edge with a single outgoing lead."""
    img, d = canvas()
    q = [(2.5, 7.5), (13.0, 3.0), (18.5, 11.0), (8.0, 20.5)]
    patch(img, "surface", q)
    for c in (0.25, 0.5, 0.75):
        uvline(d, lambda t, c=c: (c, t), "surface", 0.85, quad=q)
        uvline(d, lambda t, c=c: (t, c), "surface", 0.85, quad=q)
    stroke(d, [P(18.5, 11.0), P(22.0, 11.0)], "surface", 1.1)
    dot(d, 18.5, 11.0, 1.9, "surface")
    finish(img, "MiteNet")


def icon_mite_net_param():
    """The Mite Net parameter: the same lattice in the parameter greys."""
    img, d = canvas()
    q = [(3.0, 8.0), (14.0, 3.5), (21.0, 12.0), (10.0, 20.5)]
    lattice(img, d, "param", 3, 3, nodes=True, quad=q)
    finish(img, "MiteNetParam")


# ================================================================ Kinetics (mesh / form-finding family)
# ================================================================ Kinetics
def icon_net_kinetics():
    """A rhombic scissor lattice sheared by the fold: A rods blue, B rods red, white hinge dots, a fold arrow."""
    img, d = canvas()
    quad = [(2.5, 6.5), (13.5, 6.5), (21.5, 19.5), (10.5, 19.5)]   # parallelogram = the sheared grid
    lattice(img, d, "white", 3, 3, nodes=True, quad=quad, famU="a", famV="b")
    arrow(d, 13.5, 3.4, 21.0, 3.4, "ink", 1.4, 3.2)
    finish(img, "NetKinetics")


def icon_mechanism_modes():
    """The free motions of a net: the scissor lattice at rest, the outline of its mode (where it can lean,
    sharing the held bottom edge), and a two-headed arrow: it can go either way."""
    img, d = canvas()
    rest = [(2.5, 9.0), (14.5, 9.0), (14.5, 21.0), (2.5, 21.0)]
    moved = [(9.0, 9.0), (21.0, 9.0), (14.5, 21.0), (2.5, 21.0)]
    halo(d, lambda ld: ld.line([P(*q) for q in moved + moved[:1]], fill=INK_RGBA, width=max(1, int(1.1 * S)), joint="curve"), 0.5)
    lattice(img, d, "white", 3, 3, nodes=True, quad=rest, famU="a", famV="b")
    arrow(d, 13.0, 4.0, 4.0, 4.0, "ink", 1.4, 3.2)
    arrow(d, 10.0, 4.0, 19.0, 4.0, "ink", 1.4, 3.2)
    finish(img, "MechanismModes")


def icon_kinetic_state():
    """One moment of a motion: the sheared scissor lattice under a slider (the Fold), its knob part-way."""
    img, d = canvas()
    quad = [(4.5, 9.0), (15.0, 9.0), (21.0, 21.0), (10.5, 21.0)]
    lattice(img, d, "white", 3, 3, nodes=True, quad=quad, famU="a", famV="b")
    mark(d, [P(3.0, 4.0), P(21.0, 4.0)], 1.2)
    dot(d, 13.5, 4.0, 2.3, ink=True)
    finish(img, "KineticState")


# ================================================================ Util (grey)
def icon_mesh_cleanup():
    img, d = canvas()
    q = [(2.5, 10.0), (13.0, 5.5), (19.0, 13.0), (8.0, 21.0)]
    lattice(img, d, "grey", 3, 3, nodes=False, quad=q)
    cx, cy = 18.5, 5.5
    for a in (0, math.pi / 2):
        mark(d, [P(cx + 3.2 * math.cos(a), cy + 3.2 * math.sin(a)), P(cx - 3.2 * math.cos(a), cy - 3.2 * math.sin(a))], 1.3)
    for a in (math.pi / 4, -math.pi / 4):
        mark(d, [P(cx + 1.8 * math.cos(a), cy + 1.8 * math.sin(a)), P(cx - 1.8 * math.cos(a), cy - 1.8 * math.sin(a))], 1.1)
    finish(img, "MeshCleanup")


def icon_pull_to_mesh():
    img, d = canvas()
    q = [(2.5, 13.0), (15.0, 9.0), (21.5, 16.0), (9.0, 21.5)]
    patch(img, "grey", q)
    pts = [P(4 + 16 * i / 30, 5 + 1.4 * math.sin(i / 30 * 2 * math.pi + 1)) for i in range(31)]
    stroke(d, pts, "a", 1.5, rim=True)
    for x in (8, 16):
        arrow(d, x, 7.5, x, 12.5, "ink", 1.3, 1.9)
    finish(img, "PullToMesh")


def icon_mesh_colour_map():
    img, d = canvas()
    q = [(2.5, 11.0), (15.0, 6.5), (21.5, 14.0), (9.0, 21.5)]
    patch(img, "white", q)
    # the diverging map: blue – white – red
    ramp(d, (3, 2.5, 21, 5.5), DIVERGING_STOPS)
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
    # a white halo (about 1 px at 24 px) so the knot reads on a dark ribbon too
    tile.paste(Image.new("RGBA", src.size, WHITE), (0, 0), mask.filter(ImageFilter.MaxFilter(2 * int(0.9 * max(src.size) / SIZE) + 1)))
    tile.paste(Image.new("RGBA", src.size, hexrgb(INK) + (255,)), (0, 0), mask)
    sc = (SIZE - 4) / max(src.size)
    im = tile.resize((max(1, round(src.width * sc)), max(1, round(src.height * sc))), Image.LANCZOS)
    out = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    out.paste(im, ((SIZE - im.width) // 2, (SIZE - im.height) // 2), im)
    os.makedirs(OUT, exist_ok=True)
    out.save(os.path.join(OUT, "Mite_Tab.png"))
    os.makedirs(WEB_OUT, exist_ok=True)
    big2 = Image.new("RGBA", (2 * SIZE, 2 * SIZE), (0, 0, 0, 0))
    sc2 = 2 * (SIZE - 4) / max(src.size)
    im2 = tile.resize((max(1, round(src.width * sc2)), max(1, round(src.height * sc2))), Image.LANCZOS)
    big2.paste(im2, ((2 * SIZE - im2.width) // 2, (2 * SIZE - im2.height) // 2), im2)
    big2.save(os.path.join(WEB_OUT, "Mite_Tab.png"))


def icon_flat_kit():
    """The kit: the flat hexagonal grid (rays A, the ring B) above a white strip with two slots."""
    img, d = canvas()
    cx, cy, r = 12.0, 8.5, 6.8
    ring = [P(cx + r * math.cos(k * math.pi / 3), cy + r * math.sin(k * math.pi / 3)) for k in range(7)]
    stroke(d, ring, "b", 1.3, rim=True)
    for k in range(6):
        a = math.pi / 6 + k * math.pi / 3
        stroke(d, [P(cx, cy), P(cx + 0.87 * r * math.cos(a), cy + 0.87 * r * math.sin(a))], "a", 1.3, rim=True)
    dot(d, cx, cy, 1.3, ink=True)
    fill_poly(img, [P(2, 17.5), P(7.2, 17.5), P(7.2, 19.2), P(8.8, 19.2), P(8.8, 17.5), P(15.2, 17.5), P(15.2, 19.2), P(16.8, 19.2),
                    P(16.8, 17.5), P(22, 17.5), P(22, 21.5), P(2, 21.5)], "white")
    finish(img, "FlatKit")


def icon_net_trim():
    """Cookie cutter: the patch carries a faint net; a closed loop on it holds the part that is kept, drawn full."""
    img, d = canvas()
    patch(img)
    cu, cv, ru, rv = 0.5, 0.5, 0.36, 0.30
    inside = lambda u, v: ((u - cu) / ru) ** 2 + ((v - cv) / rv) ** 2 < 1
    for c in (-0.6, -0.3, 0.0, 0.3, 0.6):
        for f in (lambda t, c=c: (t, t + c), lambda t, c=c: (t, 1 - t + c)):
            pts = [(f(i / 31), i) for i in range(32)]
            faint = [P(*uv(u, v)) for (u, v), i in pts if 0 <= v <= 1]
            if len(faint) > 1:
                stroke(d, faint, "surface", 0.4, alpha=60)
            run = []
            for (u, v), i in pts:
                if 0 <= v <= 1 and inside(u, v):
                    run.append(P(*uv(u, v)))
                elif len(run) > 1:
                    stroke(d, run, "surface", 1.0); run = []
                else:
                    run = []
            if len(run) > 1:
                stroke(d, run, "surface", 1.0)
    loop = [P(*uv(cu + ru * math.cos(k * math.pi / 24), cv + rv * math.sin(k * math.pi / 24))) for k in range(49)]
    stroke(d, loop, "surface", 1.3)
    finish(img, "NetTrim")




def write_icons_json(path):
    """The bench's icon table: every 24 px icon as a data URI, keyed by its resource name."""
    import base64
    out = {}
    for name in sorted(os.listdir(OUT)):
        if name.endswith(".png"):
            with open(os.path.join(OUT, name), "rb") as f:
                out[name[:-4]] = "data:image/png;base64," + base64.b64encode(f.read()).decode("ascii")
    with open(path, "w") as f:
        json.dump(out, f, indent=1)
        f.write("\n")


def write_palette_json(path):
    """The colours a reader may find in the icons (for the tests and the bench)."""
    with open(path, "w") as f:
        json.dump({"palette": PALETTE, "allowed": ALLOWED, "util": UTIL_STOPS, "diverging": DIVERGING_STOPS}, f, indent=1)
        f.write("\n")

def icon_web_propagation():
    """A web grown from a strip: the two drawn rows bold at the near edge, the
    propagated rows lighter behind them, zig-zag asymptotic laths between
    consecutive rows (a triangle strip pattern)."""
    img, d = canvas()
    patch(img)
    rows = [0.12, 0.36, 0.6, 0.84]
    n = 4
    for k, v in enumerate(rows):
        bold = k < 2
        uvline(d, lambda t, v=v: (t, v + 0.05 * math.sin(math.pi * t)), "surface", 1.4 if bold else 0.7, n=12)
    for k in range(len(rows) - 1):
        v0, v1 = rows[k], rows[k + 1]
        pts = []
        for j in range(n):
            u0 = j / (n - 1) + 0.14 * (k % 2)
            u1 = j / (n - 1) + 0.14 * ((k + 1) % 2)
            pts.append(uv(min(1, u0), v0 + 0.05 * math.sin(math.pi * min(1, u0))))
            pts.append(uv(min(1, u1), v1 + 0.05 * math.sin(math.pi * min(1, u1))))
        for i in range(len(pts) - 1):
            stroke(d, [P(*pts[i]), P(*pts[i + 1])], "surface", 1.0 if k == 0 else 0.55)
    for j in range(n):
        for k in (0, 1):
            u = min(1, j / (n - 1) + 0.14 * (k % 2))
            x, y = uv(u, rows[k] + 0.05 * math.sin(math.pi * u))
            dot(d, x, y, 1.1, "surface")
    finish(img, "WebPropagation")


ALL = [icon_principal_curvature, icon_gaussian_curvature, icon_mean_curvature, icon_streamlines, icon_umbilics, icon_test_shape,
       icon_planarization, icon_minimal_surface, icon_force_density, icon_dynamic_relaxation, icon_asymptotic_net, icon_asymptotic_web,
       icon_agg_web, icon_web_propagation, icon_geodesic_net, icon_chebyshev_net, icon_conjugate_net, icon_geodesic_path, icon_net_trim,
       icon_mite_net, icon_mite_net_param, icon_lath_analysis, icon_gridshell_analysis, icon_net_distributions, icon_mesh_isocurves,
       icon_lath_sweep, icon_net_joints, icon_lath_unroll, icon_lath_strip, icon_lath_segment, icon_lath_preview,
       icon_lath_labels, icon_flat_kit, icon_net_topology, icon_net_kinetics, icon_mechanism_modes, icon_kinetic_state, icon_mesh_cleanup, icon_pull_to_mesh,
       icon_mesh_colour_map, icon_tab]

if __name__ == "__main__":
    for fn in ALL:
        fn()
    write_palette_json(os.path.join(os.path.dirname(__file__), "icon_palette.json"))
    write_icons_json(os.path.join(os.path.dirname(__file__), "webgen", "icons.json"))
    print(len(ALL), "icons written to", os.path.abspath(OUT))
