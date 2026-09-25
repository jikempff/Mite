using System;
using System.Collections.Generic;
using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.Analysis;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;

namespace Mite.Grasshopper.Components;

public class GridshellAnalysisComponent : MiteComponent
{
    public GridshellAnalysisComponent()
        : base("Gridshell Analysis", "GridFE",
            "Linear static analysis of a lath network as a 3D beam frame. Laths become " +
            "Euler-Bernoulli beams with the strip section, coupled at net crossings; " +
            "supports are fixed points. Geometry is converted from the document units to " +
            "metres internally, so E / Allowable are in Pa and Load in N/m whatever the model units. " +
            "A first-order sanity check, not a full FE package.",
            "Analysis", "GridshellAnalysis") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDF3");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Reference surface the net lies on", GH_ParamAccess.item);
        pManager.AddCurveParameter("Curves", "C", "Lath centerlines (net curves)", GH_ParamAccess.list);
        pManager.AddPointParameter("Joints", "J", "Crossing points where laths are coupled (from Net Joints or Net Topology nodes). Lath ends lying on another lath (T-junctions) are coupled automatically", GH_ParamAccess.list);
        pManager.AddPointParameter("Supports", "S", "Fixed support points", GH_ParamAccess.list);
        pManager.AddVectorParameter("Load", "L", "Force per unit length in N/m applied along every lath (default: 1 kN/m downward)", GH_ParamAccess.item, new Vector3d(0, 0, -1000));
        pManager.AddNumberParameter("E", "E", "Young's modulus in Pa (default 11 GPa ≈ timber)", GH_ParamAccess.item, 11e9);
        pManager.AddNumberParameter("Allowable", "Al", "Allowable combined stress in Pa (default 20 MPa)", GH_ParamAccess.item, 20e6);
        pManager.AddNumberParameter("Width", "W", "Lath width in model units (default 0.1)", GH_ParamAccess.item, 0.1);
        pManager.AddNumberParameter("Thickness", "T", "Lath thickness in model units (default 0.01)", GH_ParamAccess.item, 0.01);
        pManager.AddBooleanParameter("Upright", "U", "Lath orientation, as in Lath Sweep", GH_ParamAccess.item, false);
        pManager.AddNumberParameter("Scale", "Sc", "Deformation display scale (default 1)", GH_ParamAccess.item, 1.0);
        pManager.AddNumberParameter("Sampling", "Sa", "Chord deviation for curve sampling (0 = automatic)", GH_ParamAccess.item, 0.0);
        pManager.AddNumberParameter("MaxSegment", "Ms", "Beam element size in model units (0 = automatic: twice the mesh edge length)", GH_ParamAccess.item, 0.0);
        RegisterSectionInputs(pManager); // 13 Shape, 14 Section — beam section properties follow the profile
        pManager[2].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("Deformed", "D", "Deformed lath curves at the given display scale (one per input curve)", GH_ParamAccess.list);
        pManager.AddNumberParameter("MaxDisplacement", "Dm", "Largest node displacement (model units)", GH_ParamAccess.item);
        pManager.AddNumberParameter("Utilization", "u", "Stress utilization per lath element, one branch per lath (>1 fails)", GH_ParamAccess.tree);
        pManager.AddNumberParameter("MaxUtilization", "Um", "Peak utilization over the network", GH_ParamAccess.item);
        pManager.AddNumberParameter("LathUtilization", "Ul", "Peak utilization per lath (one per input curve; wire into Lath Preview)", GH_ParamAccess.list);
        pManager.AddPointParameter("Nodes", "N", "Frame nodes after merging", GH_ParamAccess.list);
        pManager.AddVectorParameter("Displacements", "Dv", "Displacement vector per node (model units)", GH_ParamAccess.list);
        pManager.AddNumberParameter("Axial", "Nx", "Axial force per element in N (tension positive), one branch per lath", GH_ParamAccess.tree);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var input = LoadMesh(DA, 0);
        if (input == null) return;
        var curves = new List<Curve>();
        if (!DA.GetDataList(1, curves)) return;
        var joints = new List<Point3d>();
        var supports = new List<Point3d>();
        DA.GetDataList(2, joints);
        if (!DA.GetDataList(3, supports)) return;
        var load = new Vector3d(0, 0, -1000);
        double e = 11e9, allowable = 20e6, width = 0.1, thickness = 0.01, scale = 1.0, sampling = 0.0, maxSegment = 0.0;
        bool upright = false;
        DA.GetData(4, ref load);
        DA.GetData(5, ref e);
        DA.GetData(6, ref allowable);
        DA.GetData(7, ref width);
        DA.GetData(8, ref thickness);
        DA.GetData(9, ref upright);
        DA.GetData(10, ref scale);
        DA.GetData(11, ref sampling);
        DA.GetData(12, ref maxSegment);
        int shape = 0;
        DA.GetData(13, ref shape);
        Curve? section = null;
        DA.GetData(14, ref section);

        if (supports.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "At least one support point is required.");
            return;
        }
        if (width <= 0 || thickness <= 0 || e <= 0 || allowable <= 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Width, Thickness, E and Allowable must be positive.");
            return;
        }

        var proj = new MeshProjection(input.Data);
        double chord = ResolveSampling(sampling, proj);
        if (maxSegment <= 0) maxSegment = 2.0 * proj.AverageEdgeLength;

        // Everything geometric goes to metres so the SI material inputs apply
        double s = ModelToMeters();
        if (Math.Abs(s - 1.0) > 1e-12)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Model units converted to metres (×{s:G4}) for the analysis.");
        var meshM = Scale(input.Data, s);
        // Section in metres too: A, I and J come from the profile (rectangle,
        // round bar or custom polygon — LathProfile.SectionProperties)
        if (!SectionInput.TryBuild(this, shape, section, width, thickness, upright, 0.0, s, curves.Count, out LathProfile profile)) return;

        var laths = new List<Vec3d[]>();
        var lathIndex = new List<int>();
        int skipped = 0;
        for (int c = 0; c < curves.Count; c++)
        {
            var line = curves[c] == null ? null : CurveSample.ToPolyline(curves[c], chord);
            if (line == null || line.Length < 2) { skipped++; continue; }
            var sub = Subdivide(line, maxSegment);
            for (int i = 0; i < sub.Length; i++) sub[i] = s * sub[i];
            laths.Add(sub);
            lathIndex.Add(c);
        }
        if (laths.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No usable lath curves.");
            return;
        }
        if (skipped > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"{skipped} null or degenerate curve(s) skipped.");

        long elements = 0;
        foreach (var l in laths) elements += l.Length - 1;
        if (elements > 100_000)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{elements} beam elements — this can take a while. Increase MaxSegment to coarsen the model.");

        var jointPts = new List<Vec3d>();
        foreach (var p in joints) jointPts.Add(s * MeshConvert.ToVec3d(p));
        var supportPts = new List<Vec3d>();
        foreach (var p in supports) supportPts.Add(s * MeshConvert.ToVec3d(p));

        FrameAnalysis.Result result;
        try
        {
            result = FrameAnalysis.Compute(meshM, laths, jointPts, supportPts,
                profile,
                MeshConvert.ToVec3d(load),
                new FrameAnalysis.Options { E = e, AllowableStress = allowable });
        }
        catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            return;
        }

        if (result.SupportNodeCount < supports.Count)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{supports.Count - result.SupportNodeCount} support point(s) did not snap to any lath node; " +
                "place supports on the lath curves (e.g. curve end points or joints).");

        // Deformed curves: each lath's own sampled points moved by their node's displacement
        var nodeOf = new Dictionary<(int, int), int>();
        for (int n = 0; n < result.NodeMap.Length; n++)
            foreach (var (c, i) in result.NodeMap[n])
                if (!nodeOf.ContainsKey((c, i)))
                    nodeOf.Add((c, i), n);

        double inv = 1.0 / s;
        var deformed = new Curve?[curves.Count];
        for (int c = 0; c < laths.Count; c++)
        {
            var pts = new List<Point3d>();
            for (int i = 0; i < laths[c].Length; i++)
            {
                Vec3d disp = nodeOf.TryGetValue((c, i), out int node) ? result.Displacements[node] : Vec3d.Zero;
                pts.Add(MeshConvert.ToRhinoPoint(inv * (laths[c][i] + scale * disp)));
            }
            if (pts.Count > 1) deformed[lathIndex[c]] = new PolylineCurve(pts);
        }

        // Per-lath utilization (element -> its input curve branch)
        var utilTree = new DataTree<double>();
        var axialTree = new DataTree<double>();
        var lathMax = new double[curves.Count];
        for (int c = 0; c < curves.Count; c++) { var p = BranchPath(DA, c); utilTree.EnsurePath(p); axialTree.EnsurePath(p); }
        for (int el = 0; el < result.Utilization.Length; el++)
        {
            var (c, _) = result.ElementSource[el];
            int ci = lathIndex[c];
            var path = BranchPath(DA, ci);
            utilTree.Add(result.Utilization[el], path);
            axialTree.Add(result.AxialForce[el], path);
            if (result.Utilization[el] > lathMax[ci]) lathMax[ci] = result.Utilization[el];
        }

        var nodes = new List<Point3d>(result.Nodes.Length);
        var disps = new List<Vector3d>(result.Nodes.Length);
        for (int n = 0; n < result.Nodes.Length; n++)
        {
            nodes.Add(MeshConvert.ToRhinoPoint(inv * result.Nodes[n]));
            disps.Add(MeshConvert.ToRhinoVector(inv * result.Displacements[n]));
        }

        DA.SetDataList(0, deformed);
        DA.SetData(1, result.MaxDisplacement * inv);
        DA.SetDataTree(2, utilTree);
        DA.SetData(3, result.MaxUtilization);
        DA.SetDataList(4, lathMax);
        DA.SetDataList(5, nodes);
        DA.SetDataList(6, disps);
        DA.SetDataTree(7, axialTree);

        if (result.MaxUtilization > 1.0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"Peak utilization {result.MaxUtilization:F2} exceeds 1 — the network is overstressed somewhere.");
    }

    private static MeshData Scale(MeshData mesh, double s)
    {
        if (Math.Abs(s - 1.0) < 1e-15) return mesh;
        var v = new Vec3d[mesh.VertexCount];
        for (int i = 0; i < v.Length; i++) v[i] = s * mesh.Vertices[i];
        return new MeshData(v, mesh.Faces);
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
