# CSS custom properties of the bench from Mite.Core's MitePalette (numbers.json "palette"),
# so the page is drawn with the same black, white and neon as the web app and Grasshopper.
# Bench-only tones: --wire (mesh wireframes) and --grid (table rules, plot grids).
BENCH_ONLY = {"light": {"wire": "#c8c8c8", "grid": "#ececec"}, "dark": {"wire": "#3a3a3a", "grid": "#1c1c1c"}}

def palette_vars(pal, theme):
    t = pal[theme]
    v = {
        "bg": t["surface"], "surface": t["paper"], "surface2": t["surface2"], "line": t["line"],
        "ink": t["ink"], "muted": t["muted"], "faint": t["faint"],
        "acc": t["famA"], "acc-ink": t["famA"], "fam-b": t["famB"], "fam-g": t["famG"], "neon": t["neon"],
        "good": t["ok"], "warn": t["warn"], "bad": t["warn"],
        **BENCH_ONLY[theme],
    }
    return ";".join(f"--{k}:{c}" for k, c in v.items()) + ";"
