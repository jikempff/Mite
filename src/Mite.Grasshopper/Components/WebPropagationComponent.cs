using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;

namespace Mite.Grasshopper.Components;

/// <summary>
/// AAG web grown from a drawn boundary strip, surface-free (Wang, Almaskin
/// &amp; Pottmann 2025, propagation algorithms, §3.3).
/// </summary>
public class WebPropagationComponent : MiteComponent
{
    public WebPropagationComponent()
        : base("Web Propagation", "WebProp",
            "Grows an AAG web from two neighbouring geodesic rows you draw — no surface needed, the web is the design " +
            "(Wang, Almaskin & Pottmann 2025). Row by row: every node star stays planar exactly (the new vertex lies on the " +
            "ruling of the strip's rectifying developable), every new row is a discrete geodesic (its osculating planes " +
            "contain the node normals), and the remaining freedom repeats the strip's triangles (Offset 0) or makes them " +
            "equilateral (Offset 1). A = U laths and B = W laths (asymptotic, upright strips), G = the rows (geodesic, flat " +
            "strips). With AAG on, the whole web is optimised once more with fairness, as in the paper.",
            "Gridshells", "WebPropagation") { }

    public override Guid ComponentGuid => new("3B9D7F1A-6C2E-4A5B-8D43-0E7F2C9A1B64");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("First", "V0", "First row of the strip: a polyline (its vertices are the nodes) or any curve (divided into Nodes)", GH_ParamAccess.item);
        pManager.AddCurveParameter("Second", "V1", "The neighbouring row: same number of nodes; its vertex j sits between vertices j and j+1 of the first row (either order and direction is detected)", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Nodes", "N", "Nodes per row (0 = the polylines' own vertices; both rows must then have the same count)", GH_ParamAccess.item, 0);
        pManager.AddIntegerParameter("Rows", "R", "Rows to add beyond the strip", GH_ParamAccess.item, 10);
        pManager.AddNumberParameter("Offset", "O", "Shape of the new triangles: 0 repeats the strip's own triangles at each position (constant node spacing along the asymptotic laths), > 0 asks for equilateral triangles of that height factor (1 = equilateral)", GH_ParamAccess.item, 0.0);
        pManager.AddNumberParameter("Fairness", "F", "Weight of the second differences along every new row (0 = none)", GH_ParamAccess.item, 0.01);
        pManager.AddBooleanParameter("AAG", "G", "Optimise the finished web globally (planar stars, geodesic rows, fairness) as the paper does after propagation", GH_ParamAccess.item, false);
        pManager.AddIntegerParameter("Iterations", "It", "Iterations of the global AAG optimisation", GH_ParamAccess.item, 16);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("A", "A", "Asymptotic laths of the U family (upright strips), one per node of the strip", GH_ParamAccess.list);
        pManager.AddCurveParameter("B", "B", "Asymptotic laths of the W family (upright strips)", GH_ParamAccess.list);
        pManager.AddCurveParameter("G", "G", "The rows: geodesic laths (flat strips), the two given ones first", GH_ParamAccess.list);
        pManager.AddPointParameter("Nodes", "N", "Web nodes, row by row", GH_ParamAccess.list);
        pManager.AddMeshParameter("Quads", "Q", "The A × B quad net on the nodes (the rows are its diagonals)", GH_ParamAccess.item);
        pManager.AddNumberParameter("Errors", "E", "Per new row: largest angle (degrees) between the row's osculating plane and the node normal — 0 for a discrete geodesic", GH_ParamAccess.list);
        pManager.AddTextParameter("Report", "Rp", "Rows built, node count, star and geodesic errors, and — with AAG — how far the global step moved the nodes", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        Curve? c0 = null, c1 = null;
        int nodes = 0, rows = 10, iterations = 16;
        double offset = 0, fairness = 0.01;
        bool aag = false;
        if (!DA.GetData(0, ref c0) || c0 == null) return;
        if (!DA.GetData(1, ref c1) || c1 == null) return;
        DA.GetData(2, ref nodes);
        DA.GetData(3, ref rows);
        DA.GetData(4, ref offset);
        DA.GetData(5, ref fairness);
        DA.GetData(6, ref aag);
        DA.GetData(7, ref iterations);

        var v0 = RowPoints(c0, nodes);
        var v1 = RowPoints(c1, nodes);
        if (v0 == null || v1 == null || v0.Length < 3)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Each row needs at least three nodes: draw polylines or set Nodes.");
            return;
        }
        if (v0.Length != v1.Length)
        {
            // different vertex counts: resample both by the first row's count
            int count = Math.Max(3, v0.Length);
            v0 = RowPoints(c0, count); v1 = RowPoints(c1, count);
            if (v0 == null || v1 == null || v0.Length != v1.Length)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The two rows have different node counts: set Nodes to divide both alike.");
                return;
            }
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"The rows had different vertex counts; both were divided into {count} nodes.");
        }
        if (!WebPropagation.Align(v0, v1, out var row0, out var row1, out bool reversed))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The two rows cannot be paired.");
            return;
        }
        if (reversed) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Rows reversed so that the second row's vertices sit between consecutive vertices of the first.");

        var res = WebPropagation.Propagate(row0, row1, new WebPropagation.Options
        {
            Rows = Math.Max(0, rows), Offset = Math.Max(0, offset), Fairness = Math.Max(0, fairness), ShouldCancel = Cancelled,
        });
        foreach (var note in res.Notes) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, note);
        if (res.Stopped != null && res.RowsBuilt < rows)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Propagation stopped after {res.RowsBuilt} of {rows} rows: {res.Stopped}. Fewer rows, a smoother strip, or Offset 1 (equilateral) usually go further.");
        if (res.RowsBuilt == 0 && rows > 0) return;

        var web = res.Web;
        var lines = new List<string>
        {
            $"{res.Rows.Count} rows × {row0.Length} nodes · {web.Nodes.Count} nodes · {web.A.Count} U laths, {web.B.Count} W laths · {web.Quads.Count} quads",
            $"propagation: planar stars to {res.MaxStarError:0.###e0}°, geodesic rows to {res.MaxGeodesicError:0.###e0}° (max over the new rows)",
        };
        List<Vec3d[]> a = web.A, b = web.B, g = web.Diagonals.Where((_, i) => web.DiagonalFamily[i] == 0).ToList();
        var nodeList = web.Nodes.ToArray();
        if (aag)
        {
            var r = AagWeb.Optimize(web, new AagWeb.Options { Iterations = Math.Max(1, iterations), GeodesicFamily = 0, ShouldCancel = Cancelled });
            double moved = r.Nodes.Zip(web.Nodes, (p, q) => (p - q).Length).DefaultIfEmpty(0).Max();
            a = r.A; b = r.B; g = r.G; nodeList = r.Nodes;
            lines.Add($"AAG: geodesic {r.InitialGeodesicError:0.###e0}° → {r.GeodesicError:0.###e0}° · stars {r.InitialStarError:0.###e0}° → {r.StarError:0.###e0}° · nodes moved up to {moved:G3} · {r.Iterations} iterations");
        }

        DA.SetDataList(0, MeshConvert.ToCurves(a));
        DA.SetDataList(1, MeshConvert.ToCurves(b));
        DA.SetDataList(2, MeshConvert.ToCurves(g));
        DA.SetDataList(3, nodeList.Select(MeshConvert.ToRhinoPoint));
        var qm = new Mesh();
        foreach (var p in nodeList) qm.Vertices.Add(MeshConvert.ToRhinoPoint(p));
        foreach (var q in web.Quads) qm.Faces.AddFace(q[0], q[1], q[2], q[3]);
        qm.Normals.ComputeNormals();
        DA.SetData(4, qm);
        DA.SetDataList(5, res.GeodesicError);
        DA.SetData(6, string.Join("\n", lines));
    }

    /// <summary>The row's nodes: polyline vertices when count is 0 and the curve is a polyline, otherwise count points by arc length.</summary>
    private static Vec3d[]? RowPoints(Curve curve, int count)
    {
        if (count <= 0 && curve.TryGetPolyline(out var pl) && pl.Count >= 3)
            return pl.Select(MeshConvert.ToVec3d).ToArray();
        int n = count > 1 ? count : 24;
        var pts = curve.DivideByCount(n - 1, true);
        if (pts == null || pts.Length < 3) return null;
        return pts.Select(t => MeshConvert.ToVec3d(curve.PointAt(t))).ToArray();
    }
}
