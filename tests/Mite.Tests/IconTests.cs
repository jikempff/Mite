using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Mite.Tests;

/// <summary>
/// The component icons (tools/generate_icons.py → src/Mite.Grasshopper/Resources):
/// one 24 px icon per component in the Mite palette (black, white and neon),
/// drawn after Rutten's rules (2 px border, outlines in a dark shade of the
/// fill, raster drop shadow) and legible on a light component capsule and on
/// a dark menu (WCAG 2.1 SC 1.4.11, 3 : 1 for graphical objects). The checks
/// read the PNG files directly with a small decoder, so they run anywhere.
/// </summary>
public class IconTests
{
    private static readonly string Root = FindRoot();
    private static readonly string Resources = Path.Combine(Root, "src", "Mite.Grasshopper", "Resources");

    // the Mite palette: the web app's CSS (src/Mite.Web/wwwroot/css/app.css) and MitePalette
    private static readonly Dictionary<string, string> Palette = new()
    {
        ["ink"] = "#0a0a0a", ["white"] = "#ffffff", ["blue"] = "#2a3cff", ["red"] = "#ff1f4f",
        ["green"] = "#00d86b", ["yellow"] = "#d9ff00", ["grey"] = "#bdbdbd",
    };
    private static readonly (byte r, byte g, byte b) LightCapsule = (0xd2, 0xd2, 0xd2);   // a Grasshopper component body
    private static readonly (byte r, byte g, byte b) DarkMenu = (0x2d, 0x2d, 0x2d);       // a dark-mode menu / ribbon

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Mite.Grasshopper", "Resources")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found above " + AppContext.BaseDirectory);
    }

    // ---- a minimal PNG reader (8-bit RGB / RGBA, non-interlaced: what PIL writes) ----

    private sealed class Png
    {
        public int Width, Height;
        public byte[] Rgba = Array.Empty<byte>();   // 4 bytes per pixel, row-major
        public (byte r, byte g, byte b, byte a) this[int x, int y]
        {
            get { int i = 4 * (y * Width + x); return (Rgba[i], Rgba[i + 1], Rgba[i + 2], Rgba[i + 3]); }
        }
    }

    private static Png ReadPng(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == (byte)'P' && bytes[2] == (byte)'N' && bytes[3] == (byte)'G', path + " is not a PNG");
        int pos = 8, width = 0, height = 0, colourType = -1, bitDepth = 0, interlace = 0;
        var idat = new MemoryStream();
        while (pos + 8 <= bytes.Length)
        {
            int len = (bytes[pos] << 24) | (bytes[pos + 1] << 16) | (bytes[pos + 2] << 8) | bytes[pos + 3];
            string type = System.Text.Encoding.ASCII.GetString(bytes, pos + 4, 4);
            int data = pos + 8;
            if (type == "IHDR")
            {
                width = (bytes[data] << 24) | (bytes[data + 1] << 16) | (bytes[data + 2] << 8) | bytes[data + 3];
                height = (bytes[data + 4] << 24) | (bytes[data + 5] << 16) | (bytes[data + 6] << 8) | bytes[data + 7];
                bitDepth = bytes[data + 8]; colourType = bytes[data + 9]; interlace = bytes[data + 12];
            }
            else if (type == "IDAT") idat.Write(bytes, data, len);
            else if (type == "IEND") break;
            pos = data + len + 4;
        }
        Assert.Equal(8, bitDepth);
        Assert.True(colourType == 6 || colourType == 2, path + ": colour type " + colourType + " (expected RGB or RGBA)");
        Assert.Equal(0, interlace);
        int channels = colourType == 6 ? 4 : 3;
        idat.Position = 0;
        var raw = new MemoryStream();
        using (var z = new ZLibStream(idat, CompressionMode.Decompress)) z.CopyTo(raw);
        var scan = raw.ToArray();
        int stride = width * channels;
        var prev = new byte[stride];
        var cur = new byte[stride];
        var png = new Png { Width = width, Height = height, Rgba = new byte[4 * width * height] };
        int p = 0;
        for (int y = 0; y < height; y++)
        {
            byte filter = scan[p++];
            Array.Copy(scan, p, cur, 0, stride);
            p += stride;
            for (int i = 0; i < stride; i++)
            {
                int a = i >= channels ? cur[i - channels] : 0;
                int b = prev[i];
                int c = i >= channels ? prev[i - channels] : 0;
                int x = cur[i];
                switch (filter)
                {
                    case 1: x += a; break;
                    case 2: x += b; break;
                    case 3: x += (a + b) / 2; break;
                    case 4:
                    {
                        int pp = a + b - c, pa = Math.Abs(pp - a), pb = Math.Abs(pp - b), pc = Math.Abs(pp - c);
                        x += pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
                        break;
                    }
                }
                cur[i] = (byte)x;
            }
            for (int xpix = 0; xpix < width; xpix++)
            {
                int o = 4 * (y * width + xpix);
                png.Rgba[o] = cur[xpix * channels];
                png.Rgba[o + 1] = cur[xpix * channels + 1];
                png.Rgba[o + 2] = cur[xpix * channels + 2];
                png.Rgba[o + 3] = channels == 4 ? cur[xpix * channels + 3] : (byte)255;
            }
            (prev, cur) = (cur, prev);
        }
        return png;
    }

    // ---- colour helpers (sRGB → relative luminance, WCAG contrast) ----

    private static (byte r, byte g, byte b) Hex(string h) =>
        (Convert.ToByte(h.Substring(1, 2), 16), Convert.ToByte(h.Substring(3, 2), 16), Convert.ToByte(h.Substring(5, 2), 16));

    private static double Channel(byte v)
    {
        double c = v / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static double Luminance((byte r, byte g, byte b) c) => 0.2126 * Channel(c.r) + 0.7152 * Channel(c.g) + 0.0722 * Channel(c.b);

    private static double Contrast((byte r, byte g, byte b) a, (byte r, byte g, byte b) b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return la >= lb ? (la + 0.05) / (lb + 0.05) : (lb + 0.05) / (la + 0.05);
    }

    private static (byte r, byte g, byte b) Over((byte r, byte g, byte b, byte a) p, (byte r, byte g, byte b) bg)
    {
        double a = p.a / 255.0;
        return ((byte)Math.Round(p.r * a + bg.r * (1 - a)), (byte)Math.Round(p.g * a + bg.g * (1 - a)), (byte)Math.Round(p.b * a + bg.b * (1 - a)));
    }

    private static double Dist((byte r, byte g, byte b) p, (double r, double g, double b) q) =>
        Math.Sqrt((p.r - q.r) * (p.r - q.r) + (p.g - q.g) * (p.g - q.g) + (p.b - q.b) * (p.b - q.b));

    /// <summary>Distance from p to the segment a–b in RGB (an anti-aliased blend of two colours lies on it).</summary>
    private static double SegmentDistance((byte r, byte g, byte b) p, (byte r, byte g, byte b) a, (byte r, byte g, byte b) b)
    {
        double abx = b.r - a.r, aby = b.g - a.g, abz = b.b - a.b;
        double l2 = abx * abx + aby * aby + abz * abz;
        double t = l2 == 0 ? 0 : Math.Max(0, Math.Min(1, ((p.r - a.r) * abx + (p.g - a.g) * aby + (p.b - a.b) * abz) / l2));
        return Dist(p, (a.r + abx * t, a.g + aby * t, a.b + abz * t));
    }

    /// <summary>Distance from p to the triangle a b c in RGB (an anti-aliased pixel where three colours meet lies in it) — Ericson, Real-Time Collision Detection §5.1.5.</summary>
    private static double TriangleDistance((byte r, byte g, byte b) p, (byte r, byte g, byte b) a, (byte r, byte g, byte b) b, (byte r, byte g, byte b) c)
    {
        double abx = b.r - a.r, aby = b.g - a.g, abz = b.b - a.b;
        double acx = c.r - a.r, acy = c.g - a.g, acz = c.b - a.b;
        double apx = p.r - a.r, apy = p.g - a.g, apz = p.b - a.b;
        double d1 = abx * apx + aby * apy + abz * apz, d2 = acx * apx + acy * apy + acz * apz;
        if (d1 <= 0 && d2 <= 0) return Dist(p, (a.r, a.g, a.b));
        double bpx = p.r - b.r, bpy = p.g - b.g, bpz = p.b - b.b;
        double d3 = abx * bpx + aby * bpy + abz * bpz, d4 = acx * bpx + acy * bpy + acz * bpz;
        if (d3 >= 0 && d4 <= d3) return Dist(p, (b.r, b.g, b.b));
        double vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0) { double v = d1 / (d1 - d3); return Dist(p, (a.r + v * abx, a.g + v * aby, a.b + v * abz)); }
        double cpx = p.r - c.r, cpy = p.g - c.g, cpz = p.b - c.b;
        double d5 = abx * cpx + aby * cpy + abz * cpz, d6 = acx * cpx + acy * cpy + acz * cpz;
        if (d6 >= 0 && d5 <= d6) return Dist(p, (c.r, c.g, c.b));
        double vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0) { double w = d2 / (d2 - d6); return Dist(p, (a.r + w * acx, a.g + w * acy, a.b + w * acz)); }
        double va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0) { double w = (d4 - d3) / ((d4 - d3) + (d5 - d6)); return Dist(p, (b.r + w * (c.r - b.r), b.g + w * (c.g - b.g), b.b + w * (c.b - b.b))); }
        double denom = 1.0 / (va + vb + vc);
        double vv = vb * denom, ww = vc * denom;
        return Dist(p, (a.r + abx * vv + acx * ww, a.g + aby * vv + acy * ww, a.b + abz * vv + acz * ww));
    }

    // ---- what the generator declares ----

    private static JsonDocument PaletteJson() => JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "tools", "icon_palette.json")));

    private static List<(byte r, byte g, byte b)> AllowedColours()
    {
        using var doc = PaletteJson();
        return doc.RootElement.GetProperty("allowed").EnumerateArray().Select(e => Hex(e.GetString()!)).ToList();
    }

    private static IEnumerable<(string file, string icon)> ComponentIcons()
    {
        foreach (var file in Directory.GetFiles(Path.Combine(Root, "src", "Mite.Grasshopper", "Components"), "*Component.cs").OrderBy(f => f))
        {
            var text = File.ReadAllText(file);
            // the icon resource is the last string argument of the base(...) call
            var m = Regex.Match(text, @":\s*base\((?<args>.*?)\)\s*\{", RegexOptions.Singleline);
            Assert.True(m.Success, Path.GetFileName(file) + ": no base(...) call found");
            var strings = Regex.Matches(m.Groups["args"].Value, "\"([^\"]*)\"").Select(s => s.Groups[1].Value).ToList();
            Assert.True(strings.Count >= 5, Path.GetFileName(file) + ": base call has " + strings.Count + " strings");
            yield return (Path.GetFileName(file), strings[^1]);
        }
    }

    private static IEnumerable<string> IconFiles() => Directory.GetFiles(Resources, "*.png").OrderBy(f => f);

    // ---- the tests ----

    [Fact]
    public void TheGeneratorDeclaresTheMitePalette()
    {
        using var doc = PaletteJson();
        var declared = doc.RootElement.GetProperty("palette").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
        foreach (var (name, hex) in Palette)
            Assert.True(declared.TryGetValue(name, out var d) && string.Equals(d, hex, StringComparison.OrdinalIgnoreCase), $"palette {name}: generator has {declared.GetValueOrDefault(name)}, the app has {hex}");
        // every allowed colour is a palette colour, a shade of one (towards black or white) or a blend of two
        // (the utilization ramp passes through green–yellow)
        var basics = Palette.Values.Select(Hex).ToList();
        foreach (var c in AllowedColours())
        {
            double best = basics.Min(b => Math.Min(SegmentDistance(c, b, (0, 0, 0)), SegmentDistance(c, b, (255, 255, 255))));
            for (int i = 0; i < basics.Count; i++)
                for (int j = i + 1; j < basics.Count; j++)
                    best = Math.Min(best, SegmentDistance(c, basics[i], basics[j]));
            Assert.True(best <= 12, $"allowed colour #{c.r:x2}{c.g:x2}{c.b:x2} is {best:0.0} from every shade and blend of the palette");
        }
    }

    [Fact]
    public void EveryComponentHasAnIcon_EmbeddedAndOnTheBench()
    {
        var csproj = File.ReadAllText(Path.Combine(Root, "src", "Mite.Grasshopper", "Mite.Grasshopper.csproj"));
        using var bench = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "tools", "webgen", "icons.json")));
        var icons = ComponentIcons().ToList();
        Assert.True(icons.Count >= 30, "components found: " + icons.Count);
        Assert.Equal(icons.Count, icons.Select(i => i.icon).Distinct().Count());   // one icon per component
        foreach (var (file, icon) in icons.Append(("MiteInfo.cs", "Mite_Tab")))
        {
            var path = Path.Combine(Resources, icon + ".png");
            Assert.True(File.Exists(path), $"{file}: icon {icon}.png is missing");
            var png = ReadPng(path);
            Assert.True(png.Width == 24 && png.Height == 24, $"{icon}: {png.Width} x {png.Height}, Grasshopper icons are 24 x 24");
            Assert.Contains($"Resources\\{icon}.png", csproj);
            var web = ReadPng(Path.Combine(Root, "src", "Mite.Web", "wwwroot", "icons", icon + ".png"));
            Assert.True(web.Width == 48 && web.Height == 48, $"{icon}: the web copy is {web.Width} px, expected 48");
            Assert.True(bench.RootElement.TryGetProperty(icon, out var uri) && uri.GetString()!.StartsWith("data:image/png;base64,"), $"{icon}: not in tools/webgen/icons.json");
        }
    }

    [Fact]
    public void IconsUseOnlyThePalette_ItsShadesAndTheirBlends()
    {
        var allowed = AllowedColours();
        Assert.True(allowed.Count >= 20);
        foreach (var path in IconFiles())
        {
            var png = ReadPng(path);
            var name = Path.GetFileNameWithoutExtension(path);
            int opaque = 0, crossings = 0;
            double worst = 0;
            string worstAt = "";
            for (int y = 0; y < png.Height; y++)
                for (int x = 0; x < png.Width; x++)
                {
                    var p = png[x, y];
                    if (p.a < 250) continue;   // the shadow and the anti-aliased silhouette are translucent
                    opaque++;
                    var c = (p.r, p.g, p.b);
                    double d = double.MaxValue;
                    for (int i = 0; i < allowed.Count && d > 0.5; i++)
                        for (int j = i; j < allowed.Count && d > 0.5; j++)
                            d = Math.Min(d, SegmentDistance(c, allowed[i], allowed[j]));
                    if (d > 12) crossings++;   // where three colours meet in one pixel (a crossing, a dot on a line)
                    if (d > 12)
                        for (int i = 0; i < allowed.Count && d > 12; i++)
                            for (int j = i + 1; j < allowed.Count && d > 12; j++)
                                for (int k = j + 1; k < allowed.Count && d > 12; k++)
                                    d = Math.Min(d, TriangleDistance(c, allowed[i], allowed[j], allowed[k]));
                    if (d > worst) { worst = d; worstAt = $"({x}, {y}) #{p.r:x2}{p.g:x2}{p.b:x2}"; }
                }
            Assert.True(opaque > 40, $"{name}: only {opaque} opaque pixels");
            // every opaque pixel is a palette shade, a point of a region's gradient or an anti-aliased blend of two
            // or three palette colours (the Lanczos downsampling adds a little ringing)
            Assert.True(worst <= 32, $"{name}: pixel {worstAt} is {worst:0.0} (RGB) from every blend of two or three palette colours");
            Assert.True(crossings <= 0.3 * opaque, $"{name}: {crossings} of {opaque} opaque pixels are not a palette colour or a blend of two");
        }
    }

    /// <summary>
    /// Legibility on both canvases: of the icon's pixels within 3 px of its
    /// silhouette (where the shape is told from the background), at least a
    /// fifth must contrast 3 : 1 with a light component capsule and at least a
    /// fifth with a dark menu. No single flat colour can do both (the two
    /// canvases are 8.9 : 1 apart), so the icons rely on white regions with
    /// dark rims, lit faces and haloed ink marks — this is what the test pins.
    /// </summary>
    [Fact]
    public void IconsReadOnALightCapsuleAndOnADarkMenu()
    {
        var report = new List<string>();
        foreach (var path in IconFiles())
        {
            var png = ReadPng(path);
            var name = Path.GetFileNameWithoutExtension(path);
            int w = png.Width, h = png.Height;
            bool Covered(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && png[x, y].a >= 128;
            int band = 0, okLight = 0, okDark = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (!Covered(x, y)) continue;
                    bool near = false;
                    for (int dy = -3; dy <= 3 && !near; dy++)
                        for (int dx = -3; dx <= 3 && !near; dx++)
                            if (!Covered(x + dx, y + dy)) near = true;
                    if (!near) continue;
                    band++;
                    var p = png[x, y];
                    if (Contrast(Over(p, LightCapsule), LightCapsule) >= 3) okLight++;
                    if (Contrast(Over(p, DarkMenu), DarkMenu) >= 3) okDark++;
                }
            Assert.True(band > 30, $"{name}: {band} silhouette pixels");
            double light = (double)okLight / band, dark = (double)okDark / band;
            report.Add($"{name} light {light:0.00} dark {dark:0.00}");
            Assert.True(light >= 0.2, $"{name}: only {light:0.00} of the silhouette band contrasts 3:1 on a light capsule");
            Assert.True(dark >= 0.2, $"{name}: only {dark:0.00} of the silhouette band contrasts 3:1 on a dark menu");
        }
        Assert.True(report.Count >= 31, string.Join("; ", report));
    }

    [Fact]
    public void IconsKeepTheTwoPixelBorder_OnlyTheShadowEntersIt()
    {
        foreach (var path in IconFiles())
        {
            var png = ReadPng(path);
            var name = Path.GetFileNameWithoutExtension(path);
            int worst = 0, inside = 0;
            for (int y = 0; y < png.Height; y++)
                for (int x = 0; x < png.Width; x++)
                {
                    bool border = x < 2 || y < 2 || x >= png.Width - 2 || y >= png.Height - 2;
                    if (border) worst = Math.Max(worst, png[x, y].a);
                    else if (png[x, y].a >= 128) inside++;
                }
            // Rutten: a 2 px empty border, the drop shadow (1 px offset, 2 px blur, a third dark) may fade into it
            Assert.True(worst <= 64, $"{name}: a border pixel has alpha {worst}, only the shadow (≤ 64) may enter the 2 px border");
            Assert.True(inside >= 60, $"{name}: {inside} pixels in the live area");
        }
    }

    [Fact]
    public void TheTabIcon_IsTheKnotInInkWithAWhiteHalo()
    {
        var png = ReadPng(Path.Combine(Resources, "Mite_Tab.png"));
        int ink = 0, white = 0;
        for (int y = 0; y < 24; y++)
            for (int x = 0; x < 24; x++)
            {
                var p = png[x, y];
                if (p.a < 200) continue;
                if (p.r < 40 && p.g < 40 && p.b < 40) ink++;
                if (p.r > 215 && p.g > 215 && p.b > 215) white++;
            }
        Assert.True(ink >= 60, "ink pixels: " + ink);
        Assert.True(white >= 12, "halo pixels: " + white);   // the 1 px halo makes it read on a dark ribbon
    }
}
