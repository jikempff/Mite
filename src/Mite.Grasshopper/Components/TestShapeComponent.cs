using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.Geometry;

namespace Mite.Grasshopper.Components;

/// <summary>
/// An analytic test surface with its exact curvature at every vertex, so the
/// curvature components can be checked against the truth inside Grasshopper
/// (the same shapes, meshes and closed forms the bench page and the test
/// suite use: do Carmo 1976 §3-3 for the parametric shapes, Goldman 2005 for
/// the implicit nodal surfaces).
/// </summary>
public class TestShapeComponent : MiteComponent
{
    public TestShapeComponent()
        : base("Test Shape", "Shape",
            "Builds one of Mite's analytic test surfaces (sphere, torus, ellipsoid, hyperboloid, catenoid, Enneper, " +
            "monkey saddle, cone, vault, annulus, wave, Schwarz D, gyroid …) as a mesh together with the exact " +
            "Gaussian, mean and principal curvatures, principal and asymptotic directions at every vertex, from " +
            "the closed-form parametrisation — the ground truth to wire next to Principal / Gaussian / Mean Curvature " +
            "and Umbilics. The mesh is the one the bench page and the test suite measure.",
            "Curvature", "TestShape") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCE21");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter("Name", "N",
            "Shape: saddle, sphere, torus, hyperboloid, monkey, ellipsoid, catenoid, enneper, vault, cone, dome, annulus, wave, " +
            "enneper3, ruled, schwarzd, gyroid. Parameters P1–P3 (0 = default): saddle size, a, b (z = a x² − b y²); " +
            "sphere r; dome r, half-angle°; ellipsoid a, b, c; torus R, r; hyperboloid a, c, height; catenoid c, height; " +
            "enneper radius; vault R, width, length; cone r, height, top r; annulus r in, r out, k; wave size, amplitude, frequency; " +
            "monkey size, k; enneper3 folds, radius; ruled size, twist, skew; schwarzd / gyroid extent (× π), cells, relaxation iterations (< 0: none)",
            GH_ParamAccess.item, "torus");
        pManager.AddNumberParameter("P1", "P1", "First shape parameter (0 = default)", GH_ParamAccess.item, 0.0);
        pManager.AddNumberParameter("P2", "P2", "Second shape parameter (0 = default)", GH_ParamAccess.item, 0.0);
        pManager.AddNumberParameter("P3", "P3", "Third shape parameter (0 = default)", GH_ParamAccess.item, 0.0);
        pManager.AddIntegerParameter("Resolution", "R", "Segments along the longer direction (default 40)", GH_ParamAccess.item, 40);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "The test surface as a triangle mesh (vertex normals along x_u × x_v)", GH_ParamAccess.item);
        pManager.AddNumberParameter("K", "K", "Exact Gaussian curvature per vertex", GH_ParamAccess.list);
        pManager.AddNumberParameter("H", "H", "Exact mean curvature per vertex, in Mite's sign (a sphere with outward normals: +1/r)", GH_ParamAccess.list);
        pManager.AddNumberParameter("K1", "K1", "Exact larger principal curvature per vertex", GH_ParamAccess.list);
        pManager.AddNumberParameter("K2", "K2", "Exact smaller principal curvature per vertex", GH_ParamAccess.list);
        pManager.AddVectorParameter("D1", "D1", "Exact unit principal direction of K1 (a line: the sign is arbitrary)", GH_ParamAccess.list);
        pManager.AddVectorParameter("D2", "D2", "Exact unit principal direction of K2", GH_ParamAccess.list);
        pManager.AddVectorParameter("A1", "A1", "Exact unit asymptotic direction where K ≤ 0 (zero vector where K > 0)", GH_ParamAccess.list);
        pManager.AddVectorParameter("A2", "A2", "The other exact asymptotic direction (equal to A1 where K = 0)", GH_ParamAccess.list);
        pManager.AddPointParameter("UV", "UV", "Chart parameters (u, v, 0) of every vertex (empty for the implicit shapes)", GH_ParamAccess.list);
        pManager.AddTextParameter("Report", "Rp", "What was built and how exact the values are", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        string name = "torus"; double p1 = 0, p2 = 0, p3 = 0; int resolution = 40;
        DA.GetData(0, ref name); DA.GetData(1, ref p1); DA.GetData(2, ref p2); DA.GetData(3, ref p3); DA.GetData(4, ref resolution);
        name = (name ?? "").Trim().ToLowerInvariant();
        if (Array.IndexOf(AnalyticShapes.Names, name) < 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Unknown shape '{name}'. Use one of: {string.Join(", ", AnalyticShapes.Names)}.");
            return;
        }
        if (resolution < 4)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Resolution {resolution} raised to 4, the smallest grid.");
            resolution = 4;
        }

        var surface = AnalyticShapes.Surface(name, p1, p2, p3);
        MeshData mesh;
        AnalyticSurface.Curvature[] truth;
        var uvOut = new List<Point3d>();
        string how;
        if (surface.IsParametric)
        {
            mesh = surface.Mesh(resolution, out var uv, out _);
            truth = new AnalyticSurface.Curvature[uv.Length];
            int nudged = 0;
            for (int i = 0; i < uv.Length; i++)
            {
                truth[i] = surface.At(uv[i].u, uv[i].v);
                if (truth[i].Degenerate)
                {
                    // a collapsed pole or the centre of a polar chart: the chart is singular there, the surface is not —
                    // take the limit a step of 1e-7 of the parameter range inside the domain
                    double dv = 1e-7 * (surface.V1 - surface.V0) * (uv[i].v <= surface.V0 ? 1 : -1);
                    truth[i] = surface.At(uv[i].u, uv[i].v + dv);
                    nudged++;
                }
                uvOut.Add(new Point3d(uv[i].u, uv[i].v, 0));
            }
            how = $"closed-form parametrisation x(u, v) with exact derivatives (do Carmo §3-3): K, H, k1, k2 and the directions are exact to round-off" +
                  (nudged > 0 ? $"; {nudged} pole vertex/vertices evaluated 1e-7 of the chart inside the singular row" : "");
        }
        else
        {
            mesh = AnalyticShapes.Build(name, p1, p2, p3, resolution);
            truth = new AnalyticSurface.Curvature[mesh.VertexCount];
            double worstF = 0;
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                truth[i] = surface.AtPoint(mesh.Vertices[i]);
                worstF = Math.Max(worstF, Math.Abs(surface.F!(mesh.Vertices[i])));
            }
            bool relaxed = !(p3 < 0);
            how = $"curvature of the nodal function F = 0 at the mesh vertices (Goldman 2005): exact on the nodal surface itself" +
                  (relaxed ? $"; this mesh was relaxed towards H = 0 (P3 ≥ 0), so its vertices sit up to |F| = {worstF:G3} off the nodal surface and the values are approximate — set P3 = −1 for the unrelaxed nodal mesh"
                           : $"; the marching-tetrahedra vertices interpolate F linearly along the cell edges, |F| ≤ {worstF:G3}");
            if (relaxed) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Relaxed nodal surface: the curvature is that of the nodal approximation, not exact here. P3 = −1 keeps the nodal mesh.");
        }

        Vec3d Safe(Vec3d v) => double.IsNaN(v.X) ? Vec3d.Zero : v;
        double SafeD(double d) => double.IsNaN(d) ? 0 : d;
        DA.SetData(0, MeshConvert.ToRhinoMesh(mesh));
        DA.SetDataList(1, truth.Select(t => SafeD(t.K)).ToList());
        DA.SetDataList(2, truth.Select(t => SafeD(t.H)).ToList());
        DA.SetDataList(3, truth.Select(t => SafeD(t.K1)).ToList());
        DA.SetDataList(4, truth.Select(t => SafeD(t.K2)).ToList());
        DA.SetDataList(5, truth.Select(t => MeshConvert.ToRhinoVector(Safe(t.D1))).ToList());
        DA.SetDataList(6, truth.Select(t => MeshConvert.ToRhinoVector(Safe(t.D2))).ToList());
        DA.SetDataList(7, truth.Select(t => MeshConvert.ToRhinoVector(Safe(t.A1))).ToList());
        DA.SetDataList(8, truth.Select(t => MeshConvert.ToRhinoVector(Safe(t.A2))).ToList());
        DA.SetDataList(9, uvOut);

        int degenerate = truth.Count(t => t.Degenerate);
        int anticlastic = truth.Count(t => !t.Degenerate && t.K < -1e-12), flat = truth.Count(t => !t.Degenerate && Math.Abs(t.K) <= 1e-12);
        var kAbs = truth.Where(t => !t.Degenerate).Select(t => Math.Max(Math.Abs(t.K1), Math.Abs(t.K2))).DefaultIfEmpty(0);
        string report = $"{name}: {mesh.VertexCount} vertices, {mesh.FaceCount} triangles; K < 0 at {anticlastic}, K = 0 at {flat}, K > 0 at {mesh.VertexCount - anticlastic - flat - degenerate} vertices; " +
                        $"largest |k| {kAbs.Max():G4}; {how}.";
        DA.SetData(10, report);
        Message = name;
    }
}
