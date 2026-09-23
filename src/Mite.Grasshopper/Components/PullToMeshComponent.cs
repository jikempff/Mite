using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.Geometry;

namespace Mite.Grasshopper.Components;

public class PullToMeshComponent : MiteComponent
{
    public PullToMeshComponent()
        : base("Pull To Mesh", "Pull",
            "Projects curves and points onto a mesh by closest point, so hand-drawn or edited " +
            "lath layouts can feed Lath Analysis, Lath Sweep and Net Joints like traced ones. " +
            "Optionally fairs the pulled curves on the surface to remove facet kinks.",
            "Util", "PullToMesh") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDFA");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Target mesh", GH_ParamAccess.item);
        pManager.AddCurveParameter("Curves", "C", "Curves to pull (optional)", GH_ParamAccess.list);
        pManager.AddPointParameter("Points", "P", "Points to pull (optional)", GH_ParamAccess.list);
        pManager.AddNumberParameter("Sampling", "S", "Curve sampling length along the curve (0 = automatic: half the mesh edge length)", GH_ParamAccess.item, 0.0);
        pManager.AddIntegerParameter("Smoothing", "Sm", "On-surface fairing passes for the pulled curves (default 0)", GH_ParamAccess.item, 0);
        pManager[1].Optional = true;
        pManager[2].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("Curves", "C", "Pulled curves (smooth interpolated, on the mesh)", GH_ParamAccess.list);
        pManager.AddPointParameter("Points", "P", "Pulled points", GH_ParamAccess.list);
        pManager.AddVectorParameter("Normals", "N", "Smooth mesh normal at each pulled point", GH_ParamAccess.list);
        pManager.AddNumberParameter("Distances", "D", "Pull distance per point", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var input = LoadMesh(DA, 0);
        if (input == null) return;
        var curves = new List<Curve>();
        var points = new List<Point3d>();
        double sampling = 0.0;
        int smoothing = 0;
        DA.GetDataList(1, curves);
        DA.GetDataList(2, points);
        DA.GetData(3, ref sampling);
        DA.GetData(4, ref smoothing);

        var proj = new MeshProjection(input.Data);
        double step = sampling > 0 ? sampling : 0.5 * proj.AverageEdgeLength;

        var outCurves = new List<Curve?>();
        foreach (var curve in curves)
        {
            if (curve == null) { outCurves.Add(null); continue; }
            double len = curve.GetLength();
            int n = Math.Max(2, (int)Math.Ceiling(len / step) + 1);
            var tParams = curve.DivideByCount(n - 1, true);
            if (tParams == null || tParams.Length < 2) { outCurves.Add(null); continue; }

            var pulled = new Vec3d[tParams.Length];
            int hint = -1;
            for (int i = 0; i < tParams.Length; i++)
            {
                var p = MeshConvert.ToVec3d(curve.PointAt(tParams[i]));
                var hit = proj.ClosestPoint(p, hint);
                pulled[i] = hit.Point;
                hint = hit.NearestVertex;
            }
            if (curve.IsClosed && pulled.Length > 2)
            {
                // DivideByCount on a closed curve returns the start point once
                // and no end point: close the loop by repeating it
                Array.Resize(ref pulled, pulled.Length + 1);
                pulled[pulled.Length - 1] = pulled[0];
            }
            if (smoothing > 0) pulled = CurveFairing.SmoothOnSurface(proj, pulled, smoothing);
            outCurves.Add(MeshConvert.ToCurve(pulled));
        }

        var outPoints = new List<Point3d>(points.Count);
        var normals = new List<Vector3d>(points.Count);
        var distances = new List<double>(points.Count);
        foreach (var p in points)
        {
            var v = MeshConvert.ToVec3d(p);
            var hit = proj.ClosestPoint(v, -1);
            outPoints.Add(MeshConvert.ToRhinoPoint(hit.Point));
            normals.Add(MeshConvert.ToRhinoVector(hit.SmoothNormal));
            distances.Add((hit.Point - v).Length);
        }

        DA.SetDataList(0, outCurves);
        DA.SetDataList(1, outPoints);
        DA.SetDataList(2, normals);
        DA.SetDataList(3, distances);
    }
}
