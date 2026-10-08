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
            "A to B. Method 1 (default) gives the exact geodesic of the mesh by edge flips (FlipOut, " +
            "Sharp & Crane 2020): straight across every face and edge, never through a cone tip, " +
            "through a saddle vertex only when that is shorter. Method 0 is the older curve shortening. " +
            "Points are matched pairwise (the last is reused when lists differ).",
            "Gridshells", "GeodesicPath") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDFC");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input mesh", GH_ParamAccess.item);
        pManager.AddPointParameter("From", "A", "Start points", GH_ParamAccess.list);
        pManager.AddPointParameter("To", "B", "End points", GH_ParamAccess.list);
        pManager.AddNumberParameter("Sampling", "S", "Point spacing of the result. Method 1: 0 = the exact polyline through every mesh-edge crossing; " +
            "a spacing > 0 resamples it into a smooth interpolated curve. Method 0: 0 = half the mesh edge length", GH_ParamAccess.item, 0.0);
        pManager.AddIntegerParameter("Iterations", "I", "Method 0 only: maximum straightening passes per level (default 500). Method 1 runs until the path is a geodesic", GH_ParamAccess.item, 500);
        pManager.AddIntegerParameter("Method", "Me", "0 = curve shortening (the only method up to 1.3.1: approximate, can cut corners and pick the wrong side of a vertex); " +
            "1 = exact geodesic by edge flips (FlipOut, Sharp & Crane 2020) started from a Steiner-point path (default)", GH_ParamAccess.item, 1);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("Paths", "G", "Geodesic paths: the exact polyline (Method 1, Sampling 0) or smooth interpolated curves", GH_ParamAccess.list);
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
        int method = 1;
        if (Params.Input.Count > 5) DA.GetData(5, ref method);
        if (from.Count == 0 || to.Count == 0) return;

        if (method != 0)
        {
            var fromV = from.ConvertAll(MeshConvert.ToVec3d);
            var toV = to.ConvertAll(MeshConvert.ToVec3d);
            var exact = FlipGeodesic.Compute(input.Data, fromV, toV, out string? reason);
            if (exact != null)
            {
                var curvesE = new List<Curve?>();
                var lengthsE = new List<double>();
                int missing = 0;
                foreach (var r in exact)
                {
                    if (r == null) { missing++; curvesE.Add(null); lengthsE.Add(double.NaN); continue; }
                    if (r.Points.Length < 2) curvesE.Add(null);
                    else if (sampling > 0)
                        curvesE.Add(MeshConvert.ToCurve(ShortestPath.Resample(r.Points, sampling)));
                    else
                        curvesE.Add(MeshConvert.ToPolylineCurve(r.Points));
                    lengthsE.Add(r.Length);
                }
                if (missing > 0)
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        $"{missing} path(s) could not be found (points on disconnected mesh parts).");
                DA.SetDataList(0, curvesE);
                DA.SetDataList(1, lengthsE);
                return;
            }
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"Exact geodesics need a manifold triangle mesh ({reason}); used curve shortening (Method 0) instead. Mesh Cleanup may fix the mesh.");
        }

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
