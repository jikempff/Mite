using System;
using System.Collections.Generic;
using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;

namespace Mite.Grasshopper.Components;

public class LathAnalysisComponent : MiteComponent
{
    public LathAnalysisComponent()
        : base("Lath Analysis", "LathA",
            "Checks whether gridshell laths can be physically bent along curves on a mesh. " +
            "Decomposes each curve into geodesic curvature (in-surface bend), normal curvature " +
            "(out-of-surface bend), and geodesic torsion (twist), then compares the resulting " +
            "bending strains of a rectangular strip against an allowable material strain.",
            "Analysis", "LathAnalysis") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDEA");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Mesh the laths lie on", GH_ParamAccess.item);
        pManager.AddCurveParameter("Laths", "C", "Lath curves on the mesh (e.g. from Asymptotic Net or Geodesic Net)", GH_ParamAccess.list);
        pManager.AddBooleanParameter("Upright", "U",
            "False: strip lies flat on the surface (geodesic gridshells). " +
            "True: strip stands upright, perpendicular to the surface (asymptotic gridshells).",
            GH_ParamAccess.item, false);
        pManager.AddNumberParameter("Width", "W", "Strip width, across the curve (default 0.1)", GH_ParamAccess.item, 0.1);
        pManager.AddNumberParameter("Thickness", "T", "Strip thickness (default 0.01)", GH_ParamAccess.item, 0.01);
        pManager.AddNumberParameter("MaxStrain", "E", "Allowable bending strain, e.g. sigma/E (default 0.005 ≈ timber; 0.002 steel, 0.008 GFRP)", GH_ParamAccess.item, 0.005);
        pManager.AddNumberParameter("Sampling", "S", "Chord deviation for curve sampling (0 = automatic from the mesh edge length)", GH_ParamAccess.item, 0.0);
        pManager.AddNumberParameter("Window", "Wn", "Arc length over which curvature is measured (0 = automatic: twice the mesh edge length). Larger windows smooth out facet-scale spikes", GH_ParamAccess.item, 0.0);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddBooleanParameter("Buildable", "B", "True if the lath stays within the strain limit (one per input curve)", GH_ParamAccess.list);
        pManager.AddNumberParameter("MaxUtilization", "U", "Peak strain / allowable strain per lath (>1 fails)", GH_ParamAccess.list);
        pManager.AddNumberParameter("Utilization", "u", "Strain utilization per point along each lath", GH_ParamAccess.tree);
        pManager.AddNumberParameter("GeodesicCurvature", "Kg", "In-surface bending per point", GH_ParamAccess.tree);
        pManager.AddNumberParameter("NormalCurvature", "Kn", "Out-of-surface bending per point (surface normal curvature along the lath; 0 on asymptotic curves)", GH_ParamAccess.tree);
        pManager.AddNumberParameter("GeodesicTorsion", "Tg", "Twist rate per point", GH_ParamAccess.tree);
        pManager.AddPointParameter("Points", "P", "Sample points the per-point values refer to", GH_ParamAccess.tree);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var input = LoadMesh(DA, 0);
        if (input == null) return;
        var curves = new List<Curve>();
        if (!DA.GetDataList(1, curves)) return;
        bool upright = false;
        double width = 0.1, thickness = 0.01, maxStrain = 0.005, sampling = 0.0, window = 0.0;
        DA.GetData(2, ref upright);
        DA.GetData(3, ref width);
        DA.GetData(4, ref thickness);
        DA.GetData(5, ref maxStrain);
        DA.GetData(6, ref sampling);
        DA.GetData(7, ref window);

        if (width <= 0 || thickness <= 0 || maxStrain <= 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Width, Thickness, and MaxStrain must be positive.");
            return;
        }

        var proj = new MeshProjection(input.Data);
        double chord = ResolveSampling(sampling, proj);
        var opts = new LathAnalysis.Options { Upright = upright, Width = width, Thickness = thickness, MaxStrain = maxStrain, Window = window };

        var buildable = new List<bool>();
        var maxUtil = new List<double>();
        var utilTree = new DataTree<double>();
        var kgTree = new DataTree<double>();
        var knTree = new DataTree<double>();
        var tgTree = new DataTree<double>();
        var ptTree = new DataTree<Point3d>();
        int skipped = 0;

        for (int c = 0; c < curves.Count; c++)
        {
            var path = BranchPath(DA, c);
            var pts = curves[c] == null ? null : CurveSample.ToPolyline(curves[c], chord);
            if (pts == null || pts.Length < 2)
            {
                skipped++;
                buildable.Add(false);
                maxUtil.Add(double.NaN);
                utilTree.EnsurePath(path); kgTree.EnsurePath(path); knTree.EnsurePath(path); tgTree.EnsurePath(path); ptTree.EnsurePath(path);
                continue;
            }

            var result = LathAnalysis.Analyze(proj, pts, opts);
            buildable.Add(result.Buildable);
            maxUtil.Add(result.MaxUtilization);
            utilTree.AddRange(result.Utilization, path);
            kgTree.AddRange(result.GeodesicCurvature, path);
            knTree.AddRange(result.NormalCurvature, path);
            tgTree.AddRange(result.GeodesicTorsion, path);
            foreach (var p in pts) ptTree.Add(MeshConvert.ToRhinoPoint(p), path);
        }

        if (skipped > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"{skipped} null or degenerate curve(s) reported as not buildable (NaN utilization).");

        DA.SetDataList(0, buildable);
        DA.SetDataList(1, maxUtil);
        DA.SetDataTree(2, utilTree);
        DA.SetDataTree(3, kgTree);
        DA.SetDataTree(4, knTree);
        DA.SetDataTree(5, tgTree);
        DA.SetDataTree(6, ptTree);
    }
}
