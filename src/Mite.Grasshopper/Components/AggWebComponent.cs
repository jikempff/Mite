using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;

namespace Mite.Grasshopper.Components;

/// <summary>
/// AGG web: a quad net of two geodesic lath families with one family of
/// asymptotic node diagonals (Schling, Wang, Hoyer &amp; Pottmann 2022, §2.3) —
/// two families of flat slats lying on the surface and one of upright slats.
/// </summary>
public class AggWebComponent : MiteComponent
{
    public AggWebComponent()
        : base("AGG Web", "AggWeb",
            "Asymptotic–geodesic–geodesic web (Schling, Wang, Hoyer & Pottmann 2022): takes a quad net of two lath families " +
            "(Geodesic Net, Asymptotic Web, or your own curves crossing once per node) and moves its nodes so that both " +
            "families are geodesic — equal opposite angles at every node, Sauer's condition — and one family of node " +
            "diagonals is asymptotic (its edges in the tangent plane the laths define), while staying close to the surface. " +
            "Two families of flat slats on the surface and one of upright slats. Exact on a developable surface with the " +
            "rulings as diagonals (the cylinder web of the paper); elsewhere the surface moves a little and the report says how much.",
            "Gridshells", "AggWeb") { }

    public override Guid ComponentGuid => new("5D8A2F17-6B3E-4C90-8F2A-1E7B9C3D4A62");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Reference surface mesh the web stays close to", GH_ParamAccess.item);
        pManager.AddCurveParameter("A", "A", "Laths of the first family (each crossing every B lath at most once)", GH_ParamAccess.list);
        pManager.AddCurveParameter("B", "B", "Laths of the second family", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Family", "F", "Which diagonal family becomes asymptotic: −1 = the one lying nearer the surface's tangent planes at the start, 0 or 1", GH_ParamAccess.item, -1);
        pManager.AddIntegerParameter("Iterations", "It", "Gauss–Newton iterations (the last third without fairness)", GH_ParamAccess.item, 40);
        pManager.AddNumberParameter("Proximity", "Px", "Weight of staying on the reference surface (0 lets the web float)", GH_ParamAccess.item, 0.3);
        pManager.AddNumberParameter("Fairness", "Fa", "Fairness weight along the three families (second differences)", GH_ParamAccess.item, 0.02);
        pManager.AddNumberParameter("Tolerance", "T", "Largest gap accepted as a crossing (0 = 5 % of the sampling step)", GH_ParamAccess.item, 0.0);
        pManager.AddNumberParameter("Sampling", "S", "Chord length for sampling the input curves (0 = bounding box / 500)", GH_ParamAccess.item, 0.0);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("A", "A", "Geodesic laths of the first family, node to node (flat slats)", GH_ParamAccess.list);
        pManager.AddCurveParameter("B", "B", "Geodesic laths of the second family (flat slats)", GH_ParamAccess.list);
        pManager.AddCurveParameter("Diagonals", "D", "Asymptotic node diagonals (upright slats)", GH_ParamAccess.list);
        pManager.AddCurveParameter("Other", "O", "The other diagonal family (not built)", GH_ParamAccess.list);
        pManager.AddPointParameter("Nodes", "N", "Web nodes", GH_ParamAccess.list);
        pManager.AddVectorParameter("Normals", "Nm", "Web normal at each node (the laths' common principal normal)", GH_ParamAccess.list);
        pManager.AddMeshParameter("Quads", "Q", "The web as a quad mesh on its nodes", GH_ParamAccess.item);
        pManager.AddNumberParameter("Geodesic", "Ge", "Largest difference of opposite angles at a node, degrees (0 = exact G-net)", GH_ParamAccess.item);
        pManager.AddNumberParameter("Asymptotic", "As", "Largest angle between an asymptotic diagonal edge and the web's tangent plane, degrees", GH_ParamAccess.item);
        pManager.AddNumberParameter("Deviation", "Dv", "Largest node distance from the reference surface", GH_ParamAccess.item);
        pManager.AddTextParameter("Report", "R", "Node counts, the family chosen, errors before and after, how far the surface moved", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var input = LoadMesh(DA, 0);
        if (input == null) return;
        var curvesA = new List<Curve>();
        var curvesB = new List<Curve>();
        if (!DA.GetDataList(1, curvesA)) return;
        if (!DA.GetDataList(2, curvesB)) return;
        int family = -1, iterations = 40;
        double proximity = 0.3, fairness = 0.02, tolerance = 0.0, sampling = 0.0;
        DA.GetData(3, ref family);
        DA.GetData(4, ref iterations);
        DA.GetData(5, ref proximity);
        DA.GetData(6, ref fairness);
        DA.GetData(7, ref tolerance);
        DA.GetData(8, ref sampling);

        if (sampling <= 0)
        {
            var bbox = BoundingBox.Empty;
            foreach (var c in curvesA) if (c != null) bbox.Union(c.GetBoundingBox(false));
            foreach (var c in curvesB) if (c != null) bbox.Union(c.GetBoundingBox(false));
            sampling = bbox.IsValid ? Math.Max(bbox.Diagonal.Length / 500.0, 1e-9) : 0.01;
        }
        var familyA = NetJointsComponent.SampleAligned(curvesA, sampling);
        var familyB = NetJointsComponent.SampleAligned(curvesB, sampling);
        var crossings = NetIntersections.Find(familyA, familyB, tolerance);
        if (crossings.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No crossings between A and B: the web needs a quad net of two crossing families (Geodesic Net or Asymptotic Web output, for instance).");
            return;
        }
        var topo = NetTopology.Build(familyA, familyB, crossings);
        var net = AggWeb.QuadNet.FromTopology(topo);
        if (net.RegularNodes == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"{net.Nodes.Count} nodes but none with two neighbours in each family: nothing to optimise. Each A lath should cross each B lath once; raise Tolerance if true crossings were missed.");
            return;
        }
        if (net.LabelConflicts > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"The diagonals could not be split into two consistent families everywhere ({net.LabelConflicts} conflicts, e.g. around a singular node): the family labels are local there.");

        var data = input.Data;
        var r = AggWeb.Optimize(data, net, new AggWeb.Options
        {
            AsymptoticFamily = family,
            Iterations = Math.Max(1, iterations),
            Proximity = Math.Max(0, proximity),
            Fairness = Math.Max(0, fairness),
            ShouldCancel = Cancelled,
        });

        double spacing = net.Spacing;
        var lines = new List<string>
        {
            $"{r.A.Count} + {r.B.Count} geodesic laths · {net.Nodes.Count} nodes ({net.RegularNodes} regular) · {net.Quads.Count} quads · spacing {spacing:G4}",
            $"asymptotic diagonals: family {r.AsymptoticFamily}{(family < 0 ? " (chosen: nearer the surface's tangent planes)" : "")} · {r.Asymptotic.Count} chains, {r.Final.AsymptoticEdges} edges",
            $"geodesic (opposite angles) {r.Initial.GeodesicError:0.###}° → {r.Final.GeodesicError:0.####}° (mean {r.Final.MeanGeodesicError:0.####}°) · laths' normals agree within {r.Final.BisectorError:0.###}°",
            $"asymptotic (edge to tangent plane) {r.Initial.AsymptoticError:0.###}° → {r.Final.AsymptoticError:0.####}° (mean {r.Final.MeanAsymptoticError:0.####}°)",
            $"against the mesh: web normal within {r.Final.SurfaceNormalError:0.###}° of the mesh normal, asymptotic edges within {r.Final.SurfaceAsymptoticError:0.###}° of its tangent plane",
            $"surface moved {r.Final.MeanDeviation:G3} mean, {r.Final.MaxDeviation:G3} max ({r.Final.MaxDeviation / Math.Max(spacing, 1e-12):P1} of the spacing); nodes moved up to {r.MaxMove:G3} · {r.Iterations} iterations",
        };
        if (r.Initial.MaxDeviation > spacing && family < 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"The input net starts {r.Initial.MaxDeviation:G3} from the mesh (more than one spacing): the automatic Family choice reads the mesh normals there and may pick the wrong diagonal — set Family 0 or 1 if the diagonals come out across the asymptotic direction.");
        if (r.Final.GeodesicError > 1 || r.Final.AsymptoticError > 1)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"No AGG web close to this surface with these laths (geodesic {r.Final.GeodesicError:0.##}°, asymptotic {r.Final.AsymptoticError:0.##}°): lower Proximity to let the surface change more, try the other Family, raise Iterations, or start from a geodesic net whose diagonals already follow the asymptotic directions.");

        DA.SetDataList(0, MeshConvert.ToCurves(r.A));
        DA.SetDataList(1, MeshConvert.ToCurves(r.B));
        DA.SetDataList(2, MeshConvert.ToCurves(r.Asymptotic));
        DA.SetDataList(3, MeshConvert.ToCurves(r.Other));
        DA.SetDataList(4, r.Nodes.Select(MeshConvert.ToRhinoPoint));
        DA.SetDataList(5, r.Normals.Select(MeshConvert.ToRhinoVector));
        var qm = new Mesh();
        foreach (var p in r.Nodes) qm.Vertices.Add(MeshConvert.ToRhinoPoint(p));
        foreach (var q in net.Quads) qm.Faces.AddFace(q[0], q[1], q[2], q[3]);
        qm.Normals.ComputeNormals();
        DA.SetData(6, qm);
        DA.SetData(7, r.Final.GeodesicError);
        DA.SetData(8, r.Final.AsymptoticError);
        DA.SetData(9, r.Final.MaxDeviation);
        DA.SetData(10, string.Join("\n", lines));
    }
}
