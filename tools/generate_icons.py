#!/usr/bin/env python3
"""Generates the 24x24 Grasshopper component icons for Mite.

Branding: white line pictograms (thin strokes) on transparent background,
matching the existing set. Drawn at 8x and downsampled for anti-aliasing.
Run: python tools/generate_icons.py
"""
import math
import os
from PIL import Image, ImageDraw

S = 8          # supersampling
SIZE = 24      # final size
W = 1.6 * S    # stroke width
WHITE = (255, 255, 255, 255)

OUT = os.path.join(os.path.dirname(__file__), "..", "src", "Mite.Grasshopper", "Resources")


def canvas():
    img = Image.new("RGBA", (SIZE * S, SIZE * S), (0, 0, 0, 0))
    return img, ImageDraw.Draw(img)


def save(img, name):
    img = img.resize((SIZE, SIZE), Image.LANCZOS)
    img.save(os.path.join(OUT, name + ".png"))


def P(x, y):
    return (x * S, y * S)


def arc_points(cx, cy, r, a0, a1, n=24):
    return [P(cx + r * math.cos(a0 + (a1 - a0) * i / (n - 1)),
              cy + r * math.sin(a0 + (a1 - a0) * i / (n - 1))) for i in range(n)]


def icon_conjugate_net():
    # two crossing curve families: arcs bowed up vs arcs bowed down
    img, d = canvas()
    for k in range(3):
        y = 7 + k * 5
        d.line([P(3, y), P(12, y - 2.6), P(21, y)], fill=WHITE, width=int(W * 0.9))
    for k in range(3):
        y = 9.5 + k * 5
        d.line([P(3, y), P(12, y + 2.6), P(21, y)], fill=WHITE, width=int(W * 0.9))
    save(img, "ConjugateNet")


def icon_umbilics():
    # point with radiating direction lines (star) on a small surface patch
    img, d = canvas()
    cx, cy = 12, 12
    d.ellipse([P(10.7, 10.7), P(13.3, 13.3)], fill=WHITE)
    for k in range(8):
        a = k * math.pi / 4
        d.line([P(cx + 2.6 * math.cos(a), cy + 2.6 * math.sin(a)),
                P(cx + 5.4 * math.cos(a), cy + 5.4 * math.sin(a))], fill=WHITE, width=int(W * 0.8))
    # corner surface ticks
    d.line([P(3, 20), P(9, 20)], fill=WHITE, width=int(W * 0.7))
    d.line([P(15, 4), P(21, 4)], fill=WHITE, width=int(W * 0.7))
    save(img, "Umbilics")


def icon_gridshell_analysis():
    # grid arch with a downward load arrow
    img, d = canvas()
    d.line(arc_points(10, 22, 7.5, math.pi + 0.25, -0.25, 32), fill=WHITE, width=int(W))
    for x in (6.5, 10, 13.5):
        d.line([P(x, 15.6), P(x, 21.3)], fill=WHITE, width=int(W * 0.7))
    d.line([P(19, 4), P(19, 11)], fill=WHITE, width=int(W))
    d.polygon([P(17.4, 9.8), P(20.6, 9.8), P(19, 12.6)], fill=WHITE)
    save(img, "GridshellAnalysis")


def icon_lath_unroll():
    # curved band -> arrow -> straight band
    img, d = canvas()
    d.arc([P(1.5, 3), P(11.5, 13)], 200, 340, fill=WHITE, width=int(W))
    d.arc([P(1.5, 6), P(11.5, 16)], 200, 340, fill=WHITE, width=int(W))
    d.line([P(13.5, 8), P(16.5, 8)], fill=WHITE, width=int(W * 0.8))
    d.polygon([P(16, 6.8), P(18, 8), P(16, 9.2)], fill=WHITE)
    d.line([P(3, 18.5), P(21, 18.5)], fill=WHITE, width=int(W))
    d.line([P(3, 21.5), P(21, 21.5)], fill=WHITE, width=int(W))
    d.line([P(3, 18.5), P(3, 21.5)], fill=WHITE, width=int(W))
    d.line([P(21, 18.5), P(21, 21.5)], fill=WHITE, width=int(W))
    save(img, "LathUnroll")


def icon_lath_labels():
    # price-tag label with a dot
    img, d = canvas()
    d.polygon([P(4, 10), P(13, 10), P(20, 16), P(13, 22), P(4, 22)], outline=WHITE, width=int(W))
    d.ellipse([P(6.6, 14.6), P(9.4, 17.4)], fill=WHITE)
    d.line([P(12, 2), P(19, 2)], fill=WHITE, width=int(W))
    d.line([P(12, 5), P(17, 5)], fill=WHITE, width=int(W))
    save(img, "LathLabels")


def icon_lath_segment():
    # strip with two scissor-cut tick marks
    img, d = canvas()
    d.line([P(2, 10), P(22, 10)], fill=WHITE, width=int(W))
    d.line([P(2, 14), P(22, 14)], fill=WHITE, width=int(W))
    d.line([P(2, 10), P(2, 14)], fill=WHITE, width=int(W))
    d.line([P(22, 10), P(22, 14)], fill=WHITE, width=int(W))
    for x in (9, 15.5):
        d.line([P(x, 7.5), P(x, 16.5)], fill=WHITE, width=int(W * 0.8))
        d.ellipse([P(x - 1.6, 5.2), P(x + 0.2, 7.0)], outline=WHITE, width=int(W * 0.6))
        d.ellipse([P(x - 0.2, 5.2), P(x + 1.6, 7.0)], outline=WHITE, width=int(W * 0.6))
    save(img, "LathSegment")


def icon_lath_preview():
    # strip + gauge bar with pointer
    img, d = canvas()
    d.line([P(2, 5), P(22, 5)], fill=WHITE, width=int(W))
    d.line([P(2, 9), P(22, 9)], fill=WHITE, width=int(W))
    d.line([P(2, 5), P(2, 9)], fill=WHITE, width=int(W))
    d.line([P(22, 5), P(22, 9)], fill=WHITE, width=int(W))
    # gauge
    d.line([P(3, 18), P(21, 18)], fill=WHITE, width=int(W))
    for x in (3, 9, 15, 21):
        d.line([P(x, 16.6), P(x, 19.4)], fill=WHITE, width=int(W * 0.6))
    d.polygon([P(14.2, 13.4), P(15.8, 13.4), P(15, 16)], fill=WHITE)
    save(img, "LathPreview")


def icon_mesh_cleanup():
    # grid with a sparkle (clean)
    img, d = canvas()
    for x in (4, 9.5, 15):
        d.line([P(x, 8), P(x, 21)], fill=WHITE, width=int(W * 0.7))
    for y in (8, 14.5, 21):
        d.line([P(2.5, y), P(16.5, y)], fill=WHITE, width=int(W * 0.7))
    # sparkle
    cx, cy = 18.5, 6
    d.line([P(cx, cy - 3.4), P(cx, cy + 3.4)], fill=WHITE, width=int(W * 0.8))
    d.line([P(cx - 3.4, cy), P(cx + 3.4, cy)], fill=WHITE, width=int(W * 0.8))
    d.line([P(cx - 1.7, cy - 1.7), P(cx + 1.7, cy + 1.7)], fill=WHITE, width=int(W * 0.6))
    d.line([P(cx - 1.7, cy + 1.7), P(cx + 1.7, cy - 1.7)], fill=WHITE, width=int(W * 0.6))
    save(img, "MeshCleanup")



def icon_lath_sweep():
    """A profile swept along a curved path: an extruded solid lath."""
    img, d = canvas()
    n = 48
    pts = []
    for i in range(n):
        t = i / (n - 1)
        x = 3.2 + 17.6 * t
        y = 14.8 - 6.2 * t + 2.6 * math.sin(t * math.pi)
        pts.append((x, y))

    def offset(half, dy=0.0):
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
            out.append(P(x + nx * half, y + ny * half + dy))
        return out

    half = 2.2
    depth = 2.8
    top_a, top_b = offset(half), offset(-half)
    bot_b = offset(-half, depth)

    d.line(top_a, fill=WHITE, width=int(W * 0.8), joint="curve")
    d.line(top_b, fill=WHITE, width=int(W * 0.8), joint="curve")
    d.line(bot_b, fill=WHITE, width=int(W * 0.8), joint="curve")
    for i in (0, n - 1):
        d.line([top_a[i], top_b[i]], fill=WHITE, width=int(W * 0.8))
        d.line([top_b[i], bot_b[i]], fill=WHITE, width=int(W * 0.8))
    save(img, "LathSweep")


def icon_net_joints():
    """Two crossing laths in a lap joint: the lower band is notched away."""
    img, d = canvas()
    x0, x1 = 9.2, 14.8        # vertical band rails
    y0, y1 = 9.2, 14.8        # horizontal band rails
    gap = 1.3                 # visible notch clearance beyond the crossing band

    # Horizontal band: interrupted at the crossing, with the notch cut visible
    for y in (y0, y1):
        d.line([P(2, y), P(x0 - gap, y)], fill=WHITE, width=int(W * 0.85))
        d.line([P(x1 + gap, y), P(22, y)], fill=WHITE, width=int(W * 0.85))
    d.line([P(2, y0), P(2, y1)], fill=WHITE, width=int(W * 0.85))
    d.line([P(22, y0), P(22, y1)], fill=WHITE, width=int(W * 0.85))
    # the notch end faces
    d.line([P(x0 - gap, y0), P(x0 - gap, y1)], fill=WHITE, width=int(W * 0.85))
    d.line([P(x1 + gap, y0), P(x1 + gap, y1)], fill=WHITE, width=int(W * 0.85))

    # Vertical band: continuous, passing over the notch
    for x in (x0, x1):
        d.line([P(x, 2), P(x, 22)], fill=WHITE, width=int(W * 0.85))
    d.line([P(x0, 2), P(x1, 2)], fill=WHITE, width=int(W * 0.85))
    d.line([P(x0, 22), P(x1, 22)], fill=WHITE, width=int(W * 0.85))
    save(img, "NetJoints")


if __name__ == "__main__":
    icon_conjugate_net()
    icon_umbilics()
    icon_gridshell_analysis()
    icon_lath_unroll()
    icon_lath_labels()
    icon_lath_segment()
    icon_lath_preview()
    icon_mesh_cleanup()
    icon_lath_sweep()
    icon_net_joints()
    print("icons written to", os.path.abspath(OUT))
