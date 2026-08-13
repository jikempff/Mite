using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Rhino.Geometry;
using Mite.Core.Analysis;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;

namespace Mite.Grasshopper.Components;

public class GridshellAnalysisComponent : GH_Component
{
    public GridshellAnalysisComponent()
        : base("Gridshell Analysis", "GridFE",
            "Linear static analysis of a lath network as a 3D beam frame. Laths become " +
            "Euler-Bernoulli beams with the strip section, coupled at net crossings; " +
            "supports are fixed points. Reports displacements and per-lath stress " +
            "utilization — a first-order sanity check, not a full FE package.",
            "Mite", "Analysis") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDF3");

    protected override Bitmap Icon =>
        new Bitmap(Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Mite.Grasshopper.Resources.GridshellAnalysis.png")!);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Reference surface the net lies on", GH_ParamAccess.item);
        pManager.AddCurveParameter("Curves", "C", "Lath centerlines (net curves)", GH_ParamAccess.list);
        pManager.AddPointParameter("Joints", "J", "Crossing points where laths are coupled (from Net Joints)", GH_ParamAccess.list);
        pManager.AddPointParameter("Supports", "S", "Fixed support points", GH_ParamAccess.list);
        pManager.AddVectorParameter("Load", "L", "Force per unit length applied along every lath (default: 1 kN/m downward)", GH_ParamAccess.item, new Vector3d(0, 0, -1000));
        pManager.AddNumberParameter("E", "E", "Young's modulus in Pa (default 11 GPa ≈ timber)", GH_ParamAccess.item, 11e9);
        pManager.AddNumberParameter("Allowable", "Al", "Allowable combined stress in Pa (default 20 MPa)", GH_ParamAccess.item, 20e6);
        pManager.AddNumberParameter("Width", "W", "Lath width (default 0.1)", GH_ParamAccess.item, 0.1);
        pManager.AddNumberParameter("Thickness", "T", "Lath thickness (default 0.01)", GH_ParamAccess.item, 0.01);
        pManager.AddBooleanParameter("Upright", "U", "Lath orientation, as in Lath Sweep", GH_ParamAccess.item, false);
        pManager.AddNumberParameter("Scale", "Sc", "Deformation display scale (default 1)", GH_ParamAccess.item, 1.0);
        pManager.AddNumberParameter("Sampling", "Sa", "Chord deviation for curve sampling (default 0.01)", GH_ParamAccess.item, 0.01);
        pManager.AddNumberParameter("MaxSegment", "Ms", "Beam element size: polylines are subdivided so no segment exceeds this (default 0.1; 0 = no subdivision)", GH_ParamAccess.item, 0.1);
        pManager[2].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("Deformed", "D", "Deformed lath curves at the given display scale", GH_ParamAccess.list);
        pManager.AddNumberParameter("MaxDisplacement", "Dm", "Largest node displacement (model units)", GH_ParamAccess.item);
        pManager.AddNumberParameter("Utilization", "u", "Stress utilization per lath element, one branch per lath (>1 fails)", GH_ParamAccess.tree);
        pManager.AddNumberParameter("MaxUtilization", "Um", "Peak utilization over the network", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        Mesh? mesh = null;
        var curves = new List<Curve>();
        var joints = new List<Point3d>();
        var supports = new List<Point3d>();
        var load = new Vector3d(0, 0, -1000);
        double e = 11e9, allowable = 20e6, width = 0.1, thickness = 0.01, scale = 1.0, sampling = 0.01, maxSegment = 0.1;
        bool upright = false;

        if (!DA.GetData(0, ref mesh) || mesh == null) return;
        if (!DA.GetDataList(1, curves)) return;
        DA.GetDataList(2, joints);
        if (!DA.GetDataList(3, supports)) return;
        DA.GetData(4, ref load);
        DA.GetData(5, ref e);
        DA.GetData(6, ref allowable);
        DA.GetData(7, ref width);
        DA.GetData(8, ref thickness);
        DA.GetData(9, ref upright);
        DA.GetData(10, ref scale);
        DA.GetData(11, ref sampling);
        DA.GetData(12, ref maxSegment);

        if (supports.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "At least one support point is required.");
            return;
        }

        var data = MeshConvert.ToMeshData(mesh);
        var sampled = CurveSample.ToPolylines(curves, sampling);
        // Beam accuracy needs element subdivision; a straight LineCurve would
        // otherwise become a single element with lumped end loads
        var laths = new List<Vec3d[]>();
        foreach (var line in sampled)
            laths.Add(Subdivide(line, maxSegment));
        if (laths.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No usable lath curves.");
            return;
        }

        var jointPts = new List<Vec3d>();
        foreach (var p in joints) jointPts.Add(new Vec3d(p.X, p.Y, p.Z));
        var supportPts = new List<Vec3d>();
        foreach (var p in supports) supportPts.Add(new Vec3d(p.X, p.Y, p.Z));

        FrameAnalysis.Result result;
        try
        {
            result = FrameAnalysis.Compute(data, laths, jointPts, supportPts,
                new LathProfile(width, thickness, upright),
                new Vec3d(load.X, load.Y, load.Z),
                new FrameAnalysis.Options { E = e, AllowableStress = allowable });
        }
        catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            return;
        }

        // Deformed curves from the node map
        var nodeOf = new Dictionary<(int, int), int>();
        for (int n = 0; n < result.NodeMap.Length; n++)
            foreach (var (c, i) in result.NodeMap[n])
                if (!nodeOf.ContainsKey((c, i)))
                    nodeOf.Add((c, i), n);

        var deformed = new List<Curve>();
        for (int c = 0; c < laths.Count; c++)
        {
            var pts = new List<Point3d>();
            for (int i = 0; i < laths[c].Length; i++)
            {
                Vec3d pos = nodeOf.TryGetValue((c, i), out int node)
                    ? result.Nodes[node] + scale * result.Displacements[node]
                    : laths[c][i];
                pts.Add(MeshConvert.ToRhinoPoint(pos));
            }
            if (pts.Count > 1) deformed.Add(new PolylineCurve(pts));
        }

        // Utilization per lath (element -> its curve branch)
        var utilTree = new DataTree<double>();
        for (int el = 0; el < result.Utilization.Length; el++)
        {
            var (c, _) = result.ElementSource[el];
            utilTree.Add(result.Utilization[el], new GH_Path(c));
        }

        DA.SetDataList(0, deformed);
        DA.SetData(1, result.MaxDisplacement);
        DA.SetDataTree(2, utilTree);
        DA.SetData(3, result.MaxUtilization);

        if (result.MaxUtilization > 1.0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"Peak utilization {result.MaxUtilization:F2} exceeds 1 — the network is overstressed somewhere.");
    }

    private static Vec3d[] Subdivide(Vec3d[] line, double maxSegment)
    {
        if (maxSegment <= 0) return line;
        var pts = new List<Vec3d> { line[0] };
        for (int i = 1; i < line.Length; i++)
        {
            Vec3d seg = line[i] - line[i - 1];
            int div = Math.Max(1, (int)Math.Ceiling(seg.Length / maxSegment));
            for (int k = 1; k <= div; k++)
                pts.Add(line[i - 1] + (double)k / div * seg);
        }
        return pts.ToArray();
    }
}
