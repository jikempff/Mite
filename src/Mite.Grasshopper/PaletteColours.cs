using System;
using System.Collections.Generic;
using System.Drawing;
using Rhino.ApplicationSettings;
using Rhino.Display;
using Mite.Core.Display;

namespace Mite.Grasshopper;

/// <summary>Mite.Core's palette (black, white and neon) as System.Drawing colours.</summary>
internal static class PaletteColours
{
    public static Color ToColor(this Rgb c) => Color.FromArgb(c.R, c.G, c.B);
    public static Rgb ToRgb(this Color c) => new Rgb(c.R, c.G, c.B);
    public static Color Utilization(double u) => MitePalette.Utilization(u).ToColor();
    public static Color Diverging(double t) => MitePalette.Sample(MitePalette.Diverging, t).ToColor();
    public static Color Sequential(double t) => MitePalette.Sample(MitePalette.Sequential, t).ToColor();
}

/// <summary>
/// The palette's roles against the Rhino viewport that is being drawn
/// (Mite.Core <see cref="ViewportPalette"/>): the display mode's fill — its
/// solid colour, the mean of its gradient, or the application's viewport
/// background when the mode uses it — decides whether the families are drawn
/// in the page colours (white or black backgrounds) or darkened / lightened
/// to WCAG's 3:1 (Rhino's grey). One set of colours is kept per background.
/// </summary>
internal static class ViewportColours
{
    private static readonly Dictionary<int, Color[]> _cache = new Dictionary<int, Color[]>();

    /// <summary>The background the pipeline clears to; the application's viewport colour when it cannot be read (bitmap, renderer, transparent fills, no pipeline).</summary>
    public static Rgb Background(DisplayPipeline? pipeline)
    {
        try
        {
            var a = pipeline?.DisplayPipelineAttributes;
            if (a != null)
            {
                switch (a.FillMode)
                {
                    case DisplayPipelineAttributes.FrameBufferFillMode.SolidColor:
                    case DisplayPipelineAttributes.FrameBufferFillMode.Gradient2Color:
                    case DisplayPipelineAttributes.FrameBufferFillMode.Gradient4Color:
                        a.GetFill(out Color tl, out Color bl, out Color tr, out Color br);
                        return ViewportPalette.Average(new[] { tl.ToRgb(), bl.ToRgb(), tr.ToRgb(), br.ToRgb() });
                }
            }
        }
        catch (Exception) { }
        return ApplicationBackground();
    }

    /// <summary>Options › Appearance › Colors › Viewport background (what display modes set to "use application settings" draw on).</summary>
    public static Rgb ApplicationBackground()
    {
        try { return AppearanceSettings.ViewportBackgroundColor.ToRgb(); }
        catch (Exception) { return new Rgb(160, 160, 160); }
    }

    /// <summary>Every role's colour against a background, indexed by <see cref="ViewportRole"/>.</summary>
    public static Color[] For(Rgb background)
    {
        int key = background.GetHashCode();
        lock (_cache)
        {
            if (!_cache.TryGetValue(key, out var colours))
            {
                var rgb = ViewportPalette.Colours(background);
                colours = new Color[rgb.Length];
                for (int i = 0; i < rgb.Length; i++) colours[i] = rgb[i].ToColor();
                if (_cache.Count > 64) _cache.Clear();
                _cache[key] = colours;
            }
            return colours;
        }
    }

    /// <summary>A role's colour against the background of the pipeline being drawn.</summary>
    public static Color Of(ViewportRole role, DisplayPipeline? pipeline) => For(Background(pipeline))[(int)role];

    /// <summary>A role's colour against the application's viewport background (for baked layers).</summary>
    public static Color OfApplication(ViewportRole role) => For(ApplicationBackground())[(int)role];
}
