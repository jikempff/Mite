using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;

namespace Mite.Grasshopper.Components;

public class GeodesicPathComponent : MiteComponent
{
    public GeodesicPathComponent()
        : base("Geodesic Path", "GeoPath",
            "Shortest path on a mesh between two points: the straight lath you would lay from " +
            "A to B. Graph search finds the route, curve shortening on the surface straightens it " +
            "into a geodesic. Points are matched pairwise (the last is reused when lists differ).",
            "Gridshells", "GeodesicPath") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDFC");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input mesh", GH_ParamAccess.item);
        pManager.AddPointParameter("From", "A", "Start points", GH_ParamAccess.list);
        pManager.AddPointParameter("To", "B", "End points", GH_ParamAccess.list);
        pManager.AddNumberParameter("Sampling", "S", "Point spacing of the result (0 = automatic: half the mesh edge length)", GH_ParamAccess.item, 0.0);
        pManager.AddIntegerParameter("Iterations", "I", "Maximum straightening passes per level (default 500)", GH_ParamAccess.item, 500);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("Paths", "G", "Geodesic paths (smooth interpolated curves)", GH_ParamAccess.list);
        pManager.AddNumberParameter("Lengths", "L", "Geodesic length per path", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var input = LoadMesh(DA, 0);
        if (input == null) return;
        var from = new List<Point3d>();
        var to = new List<Point3d>();
        double sampling = 0.0;
        int iterations = 500;
        if (!DA.GetDataList(1, from)) return;
        if (!DA.GetDataList(2, to)) return;
        DA.GetData(3, ref sampling);
        DA.GetData(4, ref iterations);
        if (from.Count == 0 || to.Count == 0) return;

        var proj = new MeshProjection(input.Data);
        var opts = new ShortestPath.Options { SampleLength = sampling, MaxIterations = Math.Max(1, iterations) };

        var curves = new List<Curve?>();
        var lengths = new List<double>();
        int count = Math.Max(from.Count, to.Count);
        int failed = 0;
        for (int i = 0; i < count; i++)
        {
            if (Cancelled()) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Cancelled with Esc, output is partial."); break; }
            var a = from[Math.Min(i, from.Count - 1)];
            var b = to[Math.Min(i, to.Count - 1)];
            var r = ShortestPath.Compute(proj, MeshConvert.ToVec3d(a), MeshConvert.ToVec3d(b), opts);
            if (r == null) { failed++; curves.Add(null); lengths.Add(double.NaN); continue; }
            curves.Add(MeshConvert.ToCurve(r.Value.Points));
            lengths.Add(r.Value.Length);
        }
        if (failed > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{failed} path(s) could not be found (points on disconnected mesh parts).");

        DA.SetDataList(0, curves);
        DA.SetDataList(1, lengths);
    }
}
