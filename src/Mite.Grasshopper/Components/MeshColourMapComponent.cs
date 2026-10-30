using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.Geometry;
using Mite.Core.Visualisation;

namespace Mite.Grasshopper.Components;

/// <summary>
/// Colours a mesh with Mite's shared colour maps (<see cref="ColourMap"/>: the
/// same tables the web app and the bench use) and builds a legend: a coloured
/// strip beside the mesh with nice tick values (Heckbert 1990) so the colours
/// can be read in the viewport — wire Points and Labels into a Text Tag.
/// </summary>
public class MeshColourMapComponent : MiteComponent
{
    public MeshColourMapComponent()
        : base("Mesh Colour Map", "ColourMap",
            "Colours a mesh by per-vertex values in one step (no Bounds / Remap / Gradient chain), with a legend. " +
            "The diverging palette is centred on zero, which suits signed curvature: neon blue negative, " +
            "white zero, neon red positive — the same colours as the web app and the bench.",
            "Util", "MeshColourMap") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDFB");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input mesh", GH_ParamAccess.item);
        pManager.AddNumberParameter("Values", "V", "One value per mesh vertex (e.g. K, H, K1)", GH_ParamAccess.list);
        pManager.AddIntervalParameter("Domain", "D", "Value range to map (empty = automatic, robust 2%..98% percentiles)", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Palette", "P", "0 = diverging neon blue – white – neon red (centred on 0), 1 = sequential black – blue – green – white, 2 = utilisation traffic light (green below 0.7, yellow at 1, red above)", GH_ParamAccess.item, 0);
        pManager[2].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Coloured mesh", GH_ParamAccess.item);
        pManager.AddIntervalParameter("Domain", "D", "Value range that was mapped", GH_ParamAccess.item);
        pManager.AddColourParameter("Colours", "C", "Colour per vertex", GH_ParamAccess.list);
        pManager.AddMeshParameter("Legend", "L", "Colour bar beside the mesh (a coloured strip, bottom = Domain start, top = Domain end)", GH_ParamAccess.item);
        pManager.AddNumberParameter("Ticks", "T", "Nice tick values inside the domain (0 is one of them when the domain contains it); the domain ends are the strip's ends", GH_ParamAccess.list);
        pManager.AddTextParameter("Labels", "Lb", "Tick labels, then the two end labels — pair with Points in a Text Tag", GH_ParamAccess.list);
        pManager.AddPointParameter("Points", "Pt", "Where each label belongs: one point per tick right of the strip, then the bottom and top ends", GH_ParamAccess.list);
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
                $"Values needs one number per vertex: got {values.Count}, mesh has {mesh.Vertices.Count}. " +
                "Every Mite curvature output is per vertex; a per-face list needs Face Values → Vertex Values first.");
            return;
        }
        if (palette < 0 || palette > 2)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Palette {palette} is not one of 0, 1, 2: using the diverging palette.");
            palette = 0;
        }
        var pal = (ColourMap.Palette)palette;

        double? lo = null, hi = null;
        if (domain.IsValid && Math.Abs(domain.Length) > 0) { lo = domain.T0; hi = domain.T1; }
        var mapped = ColourMap.Map(values, pal, lo, hi);
        if (pal == ColourMap.Palette.Utilisation && lo.HasValue)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "The utilisation palette maps values directly (0 … 1.3, limit at 1); Domain is ignored.");

        var colored = mesh.DuplicateMesh();
        colored.VertexColors.Clear();
        var colors = new List<Color>(values.Count);
        for (int i = 0; i < values.Count; i++)
        {
            var c = ToColor(mapped.Colours[i]);
            colors.Add(c);
            colored.VertexColors.Add(c);
        }

        // legend: a strip as tall as the mesh, standing to its right (+X of the bounding box), in the world XZ plane
        var legend = Legend.Build(mapped.Lo, mapped.Hi, pal, 64);
        var bb = mesh.GetBoundingBox(true);
        double height = Math.Max(bb.Max.Z - bb.Min.Z, 0.25 * bb.Diagonal.Length);
        if (height <= 0) height = 1;
        double width = height / 12;
        var origin = new Vec3d(bb.Max.X + 2 * width, bb.Min.Y, bb.Min.Z);
        var strip = Legend.BuildStrip(legend, origin, new Vec3d(0, 0, 1), new Vec3d(1, 0, 0), height, width);
        var stripMesh = new Mesh();
        foreach (var v in strip.Mesh.Vertices) stripMesh.Vertices.Add(v.X, v.Y, v.Z);
        foreach (var f in strip.Mesh.Faces) stripMesh.Faces.AddFace(f[0], f[1], f[2], f[3]);
        foreach (var c in strip.VertexColours) stripMesh.VertexColors.Add(ToColor(c));
        stripMesh.Normals.ComputeNormals();

        var labels = legend.Labels.ToList();
        labels.Add(legend.LoLabel); labels.Add(legend.HiLabel);
        var points = strip.TickPoints.Select(p => new Point3d(p.X, p.Y, p.Z)).ToList();
        points.Add(new Point3d(strip.LoPoint.X, strip.LoPoint.Y, strip.LoPoint.Z));
        points.Add(new Point3d(strip.HiPoint.X, strip.HiPoint.Y, strip.HiPoint.Z));

        DA.SetData(0, colored);
        DA.SetData(1, new Interval(mapped.Lo, mapped.Hi));
        DA.SetDataList(2, colors);
        DA.SetData(3, stripMesh);
        DA.SetDataList(4, legend.Ticks);
        DA.SetDataList(5, labels);
        DA.SetDataList(6, points);
    }

    internal static Color ToColor(ColourMap.Rgb c) => Color.FromArgb(c.R, c.G, c.B);
}
