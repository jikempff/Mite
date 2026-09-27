using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.Curvature;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;

namespace Mite.Grasshopper.Components;

/// <summary>
/// Schling's symmetric asymptotic web and, optionally, its AAG version with
/// geodesic diagonals (Schling, Wang, Hoyer &amp; Pottmann 2022).
/// </summary>
public class AsymptoticWebComponent : MiteComponent
{
    public AsymptoticWebComponent()
        : base("Asymptotic Web", "AsymWeb",
            "Schling's asymptotic web: nodes every Spacing along the two asymptotic curves through the seed, every other " +
            "node the crossing of the curves through them — a quad net with no stubs and no T-junctions that keeps the " +
            "surface's symmetry. A seed on a flat point (3-fold Enneper, monkey saddle) becomes a singular node where 2n rays " +
            "meet; a surface of revolution gets the rotational web whose meridian diagonals are geodesics (an AAG web). " +
            "With AAG on, the web is optimised so that one family of node diagonals is geodesic and every node star stays " +
            "planar: three families of straight, flat slats (two upright, one lying on the surface).",
            "Gridshells", "AsymptoticWeb") { }

    public override Guid ComponentGuid => new("7C1E5A2B-4D3F-4B8E-9A61-2F0C8D7E5B31");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Surface mesh (negatively curved where the web should run)", GH_ParamAccess.item);
        pManager.AddPointParameter("Seed", "S", "Seed point (optional: the mesh point nearest the bounding-box centre; a symmetric surface's centre)", GH_ParamAccess.item);
        pManager.AddNumberParameter("Spacing", "Sp", "Node distance along the two curves through the seed (0 = 4 % of the mesh diagonal)", GH_ParamAccess.item, 0.0);
        pManager.AddIntegerParameter("Symmetry", "Sy", "−1 = detect a rotation about the seed normal or a surface of revolution, 0 = none, n = force n-fold", GH_ParamAccess.item, -1);
        pManager.AddBooleanParameter("AAG", "G", "Optimise into an AAG web (geodesic diagonals, planar node stars); the surface moves slightly", GH_ParamAccess.item, false);
        pManager.AddIntegerParameter("Iterations", "It", "AAG optimisation iterations", GH_ParamAccess.item, 16);
        pManager.AddNumberParameter("Proximity", "Px", "AAG: weight of staying on the reference surface (0 lets it float)", GH_ParamAccess.item, 0.3);
        pManager.AddIntegerParameter("Diagonal", "D", "Which diagonal family becomes geodesic: 0 = the straighter one, 1 = the other", GH_ParamAccess.item, 0);
        pManager[1].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("A", "A", "Asymptotic laths of the first family (upright strips)", GH_ParamAccess.list);
        pManager.AddCurveParameter("B", "B", "Asymptotic laths of the second family (upright strips)", GH_ParamAccess.list);
        pManager.AddCurveParameter("G", "G", "Node diagonals of the geodesic family (flat strips); with AAG off, the geodesic candidates", GH_ParamAccess.list);
        pManager.AddPointParameter("Nodes", "N", "Web nodes (every A × B crossing)", GH_ParamAccess.list);
        pManager.AddMeshParameter("Quads", "Q", "The web as a quad mesh on its nodes", GH_ParamAccess.item);
        pManager.AddTextParameter("Report", "R", "Rays, symmetry, node count, diagonal geodesic error and — with AAG — star and geodesic errors and how far the surface moved", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var input = LoadMesh(DA, 0);
        if (input == null) return;
        Point3d seedPt = Point3d.Unset;
        double spacing = 0; int symmetry = -1, iterations = 16, diagonal = 0; bool aag = false; double proximity = 0.3;
        DA.GetData(1, ref seedPt);
        DA.GetData(2, ref spacing);
        DA.GetData(3, ref symmetry);
        DA.GetData(4, ref aag);
        DA.GetData(5, ref iterations);
        DA.GetData(6, ref proximity);
        DA.GetData(7, ref diagonal);

        var data = input.Data;
        var proj = new MeshProjection(data);
        int seed = seedPt.IsValid ? proj.NearestVertexGlobal(MeshConvert.ToVec3d(seedPt)) : -1;
        var pc = PrincipalCurvature.Compute(data);
        var web = AsymptoticWeb.Build(data, pc, seed, new AsymptoticWeb.Options { Spacing = spacing, Symmetry = symmetry, ShouldCancel = Cancelled });
        foreach (var n in web.Notes) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, n);
        if (web.Nodes.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No web: the seed has no asymptotic directions (K ≥ 0) — move it onto the saddle-shaped part.");
            return;
        }

        var lines = new List<string>
        {
            $"{web.A.Count} + {web.B.Count} laths · {web.Nodes.Count} nodes · {web.Quads.Count} quads · spacing {web.Spacing:G4}",
            web.RotationalWeb ? "surface of revolution: rotational web (meridian diagonals geodesic)"
                : $"{(web.Singular ? $"singular seed, {web.Rays} rays" : "regular seed")} · symmetry {(web.SymmetryOrder > 1 ? $"{web.SymmetryOrder}-fold (nodes repeat within {web.SymmetryError:G3})" : "none used")}",
            $"diagonal geodesic error |kg|·spacing: {web.DiagonalGeodesicError[0]:G3} (family 0) / {web.DiagonalGeodesicError[1]:G3}",
        };

        List<Vec3d[]> a = web.A, b = web.B, g = new();
        var nodes = web.Nodes.ToArray();
        int fam = diagonal == 1 ? 1 : 0;
        for (int i = 0; i < web.Diagonals.Count; i++) if (web.DiagonalFamily[i] == fam) g.Add(web.Diagonals[i]);
        if (aag)
        {
            var r = AagWeb.Optimize(data, web, new AagWeb.Options { Iterations = Math.Max(1, iterations), Proximity = Math.Max(0, proximity), GeodesicFamily = fam, ShouldCancel = Cancelled });
            a = r.A; b = r.B; g = r.G; nodes = r.Nodes;
            lines.Add($"AAG: geodesic {r.InitialGeodesicError:0.##}° → {r.GeodesicError:0.###}° · stars {r.InitialStarError:0.##}° → {r.StarError:0.###}° · surface moved {r.MeanDeviation:G3} mean, {r.MaxDeviation:G3} max · {r.Iterations} iterations");
            if (r.GeodesicError > 1 || r.StarError > 1)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "The surface carries no AAG web close to it: lower Proximity to let it change more, try the other Diagonal family, or start from a surface of revolution.");
        }

        DA.SetDataList(0, MeshConvert.ToCurves(a));
        DA.SetDataList(1, MeshConvert.ToCurves(b));
        DA.SetDataList(2, MeshConvert.ToCurves(g));
        DA.SetDataList(3, nodes.Select(MeshConvert.ToRhinoPoint));
        var qm = new Mesh();
        foreach (var p in nodes) qm.Vertices.Add(MeshConvert.ToRhinoPoint(p));
        foreach (var q in web.Quads) qm.Faces.AddFace(q[0], q[1], q[2], q[3]);
        qm.Normals.ComputeNormals();
        DA.SetData(4, qm);
        DA.SetData(5, string.Join("\n", lines));
    }
}
