using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Rhino.Geometry;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;

namespace Mite.Grasshopper.Components;

public class LathAnalysisComponent : GH_Component
{
    public LathAnalysisComponent()
        : base("Lath Analysis", "LathA",
            "Checks whether gridshell laths can be physically bent along curves on a mesh. " +
            "Decomposes each curve into geodesic curvature (in-surface bend), normal curvature " +
            "(out-of-surface bend), and geodesic torsion (twist), then compares the resulting " +
            "bending strains of a rectangular strip against an allowable material strain.",
            "Mite", "Gridshells") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDEA");

    protected override Bitmap Icon =>
        new Bitmap(Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Mite.Grasshopper.Resources.LathAnalysis.png")!);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Mesh the laths lie on", GH_ParamAccess.item);
        pManager.AddCurveParameter("Laths", "C", "Lath curves on the mesh (e.g. from Asymptotic Net or Geodesic Net)", GH_ParamAccess.list);
        pManager.AddBooleanParameter("Upright", "Up",
            "False: strip lies flat on the surface (geodesic gridshells). " +
            "True: strip stands upright, perpendicular to the surface (asymptotic gridshells).",
            GH_ParamAccess.item, false);
        pManager.AddNumberParameter("Width", "W", "Strip width, across the curve (default 0.1)", GH_ParamAccess.item, 0.1);
        pManager.AddNumberParameter("Thickness", "T", "Strip thickness (default 0.01)", GH_ParamAccess.item, 0.01);
        pManager.AddNumberParameter("MaxStrain", "E", "Allowable bending strain, e.g. sigma/E (default 0.005)", GH_ParamAccess.item, 0.005);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddBooleanParameter("Buildable", "B", "True if the lath stays within the strain limit", GH_ParamAccess.list);
        pManager.AddNumberParameter("MaxUtilization", "U", "Peak strain / allowable strain per lath (>1 fails)", GH_ParamAccess.list);
        pManager.AddNumberParameter("Utilization", "u", "Strain utilization per point along each lath", GH_ParamAccess.tree);
        pManager.AddNumberParameter("GeodesicCurvature", "Kg", "In-surface bending per point", GH_ParamAccess.tree);
        pManager.AddNumberParameter("NormalCurvature", "Kn", "Out-of-surface bending per point", GH_ParamAccess.tree);
        pManager.AddNumberParameter("GeodesicTorsion", "Tg", "Twist rate per point", GH_ParamAccess.tree);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        Mesh? mesh = null;
        var curves = new List<Curve>();
        bool upright = false;
        double width = 0.1, thickness = 0.01, maxStrain = 0.005;

        if (!DA.GetData(0, ref mesh) || mesh == null) return;
        if (!DA.GetDataList(1, curves)) return;
        DA.GetData(2, ref upright);
        DA.GetData(3, ref width);
        DA.GetData(4, ref thickness);
        DA.GetData(5, ref maxStrain);

        if (width <= 0 || thickness <= 0 || maxStrain <= 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Width, Thickness, and MaxStrain must be positive.");
            return;
        }

        var data = MeshConvert.ToMeshData(mesh);
        var proj = new MeshProjection(data);
        var opts = new LathAnalysis.Options
        {
            Upright = upright,
            Width = width,
            Thickness = thickness,
            MaxStrain = maxStrain
        };

        var buildable = new List<bool>();
        var maxUtil = new List<double>();
        var utilTree = new DataTree<double>();
        var kgTree = new DataTree<double>();
        var knTree = new DataTree<double>();
        var tgTree = new DataTree<double>();

        for (int c = 0; c < curves.Count; c++)
        {
            var pts = SamplePoints(curves[c]);
            var result = LathAnalysis.Analyze(proj, pts, opts);

            buildable.Add(result.Buildable);
            maxUtil.Add(result.MaxUtilization);

            var path = new GH_Path(c);
            utilTree.AddRange(result.Utilization, path);
            kgTree.AddRange(result.GeodesicCurvature, path);
            knTree.AddRange(result.NormalCurvature, path);
            tgTree.AddRange(result.GeodesicTorsion, path);
        }

        DA.SetDataList(0, buildable);
        DA.SetDataList(1, maxUtil);
        DA.SetDataTree(2, utilTree);
        DA.SetDataTree(3, kgTree);
        DA.SetDataTree(4, knTree);
        DA.SetDataTree(5, tgTree);
    }

    private static Vec3d[] SamplePoints(Curve curve)
    {
        // Native polylines keep their vertices; other curves are sampled densely
        if (curve.TryGetPolyline(out Polyline poly))
        {
            var pts = new Vec3d[poly.Count];
            for (int i = 0; i < poly.Count; i++)
                pts[i] = new Vec3d(poly[i].X, poly[i].Y, poly[i].Z);
            return pts;
        }

        var tParams = curve.DivideByCount(100, true);
        if (tParams == null || tParams.Length < 2) return Array.Empty<Vec3d>();

        var sampled = new Vec3d[tParams.Length];
        for (int i = 0; i < tParams.Length; i++)
        {
            var p = curve.PointAt(tParams[i]);
            sampled[i] = new Vec3d(p.X, p.Y, p.Z);
        }
        return sampled;
    }
}
