using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Mite.Grasshopper.Components;

public class MeshColourMapComponent : MiteComponent
{
    public MeshColourMapComponent()
        : base("Mesh Colour Map", "ColourMap",
            "Colours a mesh by per-vertex values in one step (no Bounds / Remap / Gradient chain). " +
            "Diverging palettes are centred on zero, which suits signed curvature: blue negative, " +
            "white zero, red positive.",
            "Util", "MeshColourMap") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDFB");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input mesh", GH_ParamAccess.item);
        pManager.AddNumberParameter("Values", "V", "One value per mesh vertex (e.g. K, H, K1)", GH_ParamAccess.list);
        pManager.AddIntervalParameter("Domain", "D", "Value range to map (empty = automatic, robust 2%..98% percentiles)", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Palette", "P", "0 = diverging blue-white-red (centred on 0), 1 = viridis-like sequential, 2 = traffic light (green-yellow-red)", GH_ParamAccess.item, 0);
        pManager[2].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Coloured mesh", GH_ParamAccess.item);
        pManager.AddIntervalParameter("Domain", "D", "Value range that was mapped", GH_ParamAccess.item);
        pManager.AddColourParameter("Colours", "C", "Colour per vertex", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        Mesh? mesh = null;
        if (!DA.GetData(0, ref mesh) || mesh == null) return;
        var values = new List<double>();
        if (!DA.GetDataList(1, values)) return;
        Interval domain = Interval.Unset;
        int palette = 0;
        DA.GetData(2, ref domain);
        DA.GetData(3, ref palette);

        if (values.Count != mesh.Vertices.Count)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"Values needs one number per vertex: got {values.Count}, mesh has {mesh.Vertices.Count}.");
            return;
        }

        double lo, hi;
        if (domain.IsValid && Math.Abs(domain.Length) > 0)
        {
            lo = Math.Min(domain.T0, domain.T1); hi = Math.Max(domain.T0, domain.T1);
        }
        else
        {
            var sorted = new List<double>();
            foreach (double v in values) if (!double.IsNaN(v) && !double.IsInfinity(v)) sorted.Add(v);
            if (sorted.Count == 0) { lo = -1; hi = 1; }
            else
            {
                sorted.Sort();
                lo = sorted[(int)(0.02 * (sorted.Count - 1))];
                hi = sorted[(int)(0.98 * (sorted.Count - 1))];
                if (palette == 0)
                {
                    double m = Math.Max(Math.Abs(lo), Math.Abs(hi));
                    lo = -m; hi = m;
                }
                if (hi - lo < 1e-300) { lo -= 1; hi += 1; }
            }
        }

        var colored = mesh.DuplicateMesh();
        colored.VertexColors.Clear();
        var colors = new List<Color>(values.Count);
        for (int i = 0; i < values.Count; i++)
        {
            double v = values[i];
            double t = double.IsNaN(v) ? 0.5 : (v - lo) / (hi - lo);
            var c = Palette(palette, Math.Max(0.0, Math.Min(1.0, t)));
            colors.Add(c);
            colored.VertexColors.Add(c);
        }

        DA.SetData(0, colored);
        DA.SetData(1, new Interval(lo, hi));
        DA.SetDataList(2, colors);
    }

    private static Color Palette(int palette, double t)
    {
        switch (palette)
        {
            case 1:
                return Lerp(new[]
                {
                    Color.FromArgb(68, 1, 84), Color.FromArgb(59, 82, 139), Color.FromArgb(33, 145, 140),
                    Color.FromArgb(94, 201, 98), Color.FromArgb(253, 231, 37)
                }, t);
            case 2:
                return Lerp(new[] { Color.FromArgb(46, 160, 67), Color.FromArgb(230, 200, 40), Color.FromArgb(220, 60, 50) }, t);
            default:
                return Lerp(new[] { Color.FromArgb(33, 102, 172), Color.FromArgb(247, 247, 247), Color.FromArgb(178, 24, 43) }, t);
        }
    }

    private static Color Lerp(Color[] stops, double t)
    {
        double x = t * (stops.Length - 1);
        int i = Math.Min(stops.Length - 2, (int)Math.Floor(x));
        double f = x - i;
        Color a = stops[i], b = stops[i + 1];
        return Color.FromArgb(
            (int)Math.Round(a.R + (b.R - a.R) * f),
            (int)Math.Round(a.G + (b.G - a.G) * f),
            (int)Math.Round(a.B + (b.B - a.B) * f));
    }
}
