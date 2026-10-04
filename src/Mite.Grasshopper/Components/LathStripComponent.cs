using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;

namespace Mite.Grasshopper.Components;

/// <summary>
/// The strip a thin straight plank forms along each lath: the developable that
/// contains the lath and stands upright on (or lies on) the surface, where it
/// exists, and the least-strained twisted strip where it does not — after
/// Tang et al. 2016, Schling et al. 2022 §3.4, Starostin &amp; van der Heijden
/// 2007 and Freddi et al. 2016 (see Mite.Core.Fabrication.DevelopableStrip).
/// </summary>
public class LathStripComponent : MiteComponent
{
    public LathStripComponent()
        : base("Lath Strip", "Strip",
            "The strip a thin straight plank of width W forms along each lath: the developable that " +
            "contains the lath and stands upright on the surface (asymptotic laths) or lies on it " +
            "(geodesic laths). Its rulings lean by the ratio of twist to bend (r = κg n + τg t upright, " +
            "τg t − κn u flat); its quads are planar, so the flat pattern is exact and comes out as a " +
            "straight band on asymptotic / geodesic laths. Where the twist exceeds the bend (|τ| > |κ|, " +
            "e.g. a lath crossing the waist of a catenoid, or a straight twisted lath) no such developable " +
            "carries a narrow plank: the strip twists there and the plank's edges have to stretch (EdgeStrain).",
            "Fabrication", "LathStrip") { }

    public override Guid ComponentGuid => new("5D92D9ED-2685-4D6D-8A5E-448437BF81A8");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Reference surface the laths lie on", GH_ParamAccess.item);
        pManager.AddCurveParameter("Curves", "C", "Lath centrelines on the surface", GH_ParamAccess.list);
        pManager.AddNumberParameter("Width", "W", "Plank width (default 0.1)", GH_ParamAccess.item, 0.1);
        pManager.AddBooleanParameter("Upright", "U", "False: the plank lies flat on the surface (geodesic laths). True: it stands upright on edge (asymptotic laths)", GH_ParamAccess.item, false);
        pManager.AddNumberParameter("MaxLean", "L",
            "Largest lean of a ruling from square, degrees (default 45: the developable regime |κ| ≥ |τ| of a narrow plank, Freddi et al. 2016). " +
            "90 accepts any developable that clears its edge of regression; 0 gives the square strip of Lath Unroll", GH_ParamAccess.item, 45.0);
        pManager.AddNumberParameter("Gap", "G", "Gap between laid-out patterns (default 0.05)", GH_ParamAccess.item, 0.05);
        pManager.AddNumberParameter("Sampling", "S", "Chord deviation for curve sampling (0 = automatic from the mesh edge length)", GH_ParamAccess.item, 0.0);
        pManager.AddNumberParameter("Window", "Wn", "Arc length either side of a station over which its ruling is measured (0 = automatic: four mean mesh edges; rulings are a second-derivative quantity and need more smoothing than Lath Analysis's curvature)", GH_ParamAccess.item, 0.0);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddMeshParameter("Strips", "S", "3D strip per lath: one quad per segment, planar on the developable", GH_ParamAccess.list);
        pManager.AddLineParameter("Rulings", "R", "Rulings across the strip, one branch per lath", GH_ParamAccess.tree);
        pManager.AddCurveParameter("Edges", "E", "The two long edges of each strip, one branch per lath", GH_ParamAccess.tree);
        pManager.AddCurveParameter("Patterns", "P", "Closed flat cutting patterns, laid out in a row (exact on the developable; where the strip twists, laid down with the lath's in-plane turning — an approximation)", GH_ParamAccess.list);
        pManager.AddNumberParameter("Developable", "D", "Share of each lath (0–1, by length) on which the strip is the developable", GH_ParamAccess.list);
        pManager.AddNumberParameter("EdgeStrain", "Es", "Membrane strain the plank's edges need where the strip twists (0 on the developable; (Wτ)²/8 on a straight twisted lath)", GH_ParamAccess.list);
        pManager.AddNumberParameter("Bow", "Bw", "Largest distance of the developed centreline from straight (0 = the pattern is a straight band)", GH_ParamAccess.list);
        pManager.AddTextParameter("Report", "Rp", "Summary per lath", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var input = LoadMesh(DA, 0);
        if (input == null) return;
        var curves = new List<Curve>();
        if (!DA.GetDataList(1, curves)) return;
        double width = 0.1, lean = 45.0, gap = 0.05, sampling = 0.0, window = 0.0;
        bool upright = false;
        DA.GetData(2, ref width);
        DA.GetData(3, ref upright);
        DA.GetData(4, ref lean);
        DA.GetData(5, ref gap);
        DA.GetData(6, ref sampling);
        DA.GetData(7, ref window);
        if (width <= 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Width must be positive.");
            return;
        }
        if (lean < 0 || lean > 90)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "MaxLean is clamped to 0…90°.");
            lean = Math.Max(0, Math.Min(90, lean));
        }

        var proj = new MeshProjection(input.Data);
        double chord = ResolveSampling(sampling, proj);
        var opts = new DevelopableStrip.Options
        {
            Width = width,
            Upright = upright,
            MaxLean = lean,
            Window = window > 0 ? window : 4.0 * proj.AverageEdgeLength,
        };

        var strips = new List<Mesh?>();
        var rulings = new DataTree<Line>();
        var edges = new DataTree<Curve>();
        var patterns = new List<Curve?>();
        var share = new List<double>();
        var strain = new List<double>();
        var bow = new List<double>();
        var report = new List<string>();
        double cursor = 0;
        int failed = 0, twisted = 0;

        for (int c = 0; c < curves.Count; c++)
        {
            if (Cancelled()) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Cancelled with Esc, output is partial."); break; }
            var path = BranchPath(DA, c);
            var line = curves[c] == null ? null : CurveSample.ToPolyline(curves[c], chord);
            var r = line != null ? DevelopableStrip.Build(proj, line, opts) : null;
            if (r == null)
            {
                failed++;
                strips.Add(null); patterns.Add(null);
                share.Add(double.NaN); strain.Add(double.NaN); bow.Add(double.NaN);
                report.Add($"{c}: not built (null, degenerate or too short)");
                rulings.EnsurePath(path); edges.EnsurePath(path);
                continue;
            }
            if (!r.Developable) twisted++;

            strips.Add(MeshConvert.ToRhinoMesh(r.Strip));
            for (int k = 0; k < r.Centre.Length; k++)
                rulings.Add(new Line(MeshConvert.ToRhinoPoint(r.EdgeA[k]), MeshConvert.ToRhinoPoint(r.EdgeB[k])), path);
            edges.Add(Polyline(r.EdgeA, Vec3d.Zero), path);
            edges.Add(Polyline(r.EdgeB, Vec3d.Zero), path);

            // Lay the patterns out in a row along +x, each starting at the cursor
            double minX = r.FlatA.Concat(r.FlatB).Min(p => p.X);
            double maxX = r.FlatA.Concat(r.FlatB).Max(p => p.X);
            double minY = r.FlatA.Concat(r.FlatB).Min(p => p.Y);
            var move = new Vec3d(cursor - minX, -minY, 0);
            cursor += (maxX - minX) + gap;
            var outline = new List<Point3d>();
            foreach (var p in r.FlatA) outline.Add(MeshConvert.ToRhinoPoint(p + move));
            for (int k = r.FlatB.Length - 1; k >= 0; k--) outline.Add(MeshConvert.ToRhinoPoint(r.FlatB[k] + move));
            outline.Add(outline[0]);
            patterns.Add(new PolylineCurve(outline));

            share.Add(r.DevelopableFraction);
            strain.Add(r.EdgeStrain);
            bow.Add(r.Bow);
            report.Add($"{c}: developable {r.DevelopableFraction:P0} of {r.Length:0.###}" +
                (r.Developable ? "" : $", twisted part needs edge strain {r.EdgeStrain:0.####}") +
                $"; rulings lean ≤ {Math.Min(r.MaxDevelopableLean, 90):0.#}° from square; pattern bow {r.Bow:0.####}" +
                (r.Note.Length > 0 ? " — " + r.Note : ""));
        }

        if (failed > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"{failed} curve(s) could not be built (null, degenerate or too short); nulls keep the indices aligned.");
        if (twisted > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"{twisted} lath(s) are not developable over their whole length (twist exceeds bend): see EdgeStrain and Report.");

        DA.SetDataList(0, strips);
        DA.SetDataTree(1, rulings);
        DA.SetDataTree(2, edges);
        DA.SetDataList(3, patterns);
        DA.SetDataList(4, share);
        DA.SetDataList(5, strain);
        DA.SetDataList(6, bow);
        DA.SetDataList(7, report);
    }

    private static Curve Polyline(Vec3d[] pts, Vec3d move) =>
        new PolylineCurve(pts.Select(p => MeshConvert.ToRhinoPoint(p + move)));
}
