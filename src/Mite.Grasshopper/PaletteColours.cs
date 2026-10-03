using System.Drawing;
using Mite.Core.Display;

namespace Mite.Grasshopper;

/// <summary>Mite.Core's palette (black, white and neon) as System.Drawing colours.</summary>
internal static class PaletteColours
{
    public static Color ToColor(this Rgb c) => Color.FromArgb(c.R, c.G, c.B);
    public static Color Utilization(double u) => MitePalette.Utilization(u).ToColor();
    public static Color Diverging(double t) => MitePalette.Sample(MitePalette.Diverging, t).ToColor();
    public static Color Sequential(double t) => MitePalette.Sample(MitePalette.Sequential, t).ToColor();
}
