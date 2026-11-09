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
/// geodesic diagonals (Schling, Wang, Hoyer &amp; Pottmann 2022) — through the
/// nodes, or through the midpoints of the asymptotic laths as the tri-hex web
/// of the AGH timber gridshell (Schling, Wan, Wang &amp; D'Acunto 2023; Wan,
/// D'Acunto &amp; Schling 2024), with an optional constant crossing angle of
/// the asymptotic laths (60° there; Wang &amp; Pottmann 2022 for the surface
/// class this implies).
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
            "planar: three families of straight, flat slats (two upright, one lying on the surface). Geodesics = 1 runs the " +
            "geodesic family through the midpoints of the asymptotic laths instead (the tri-hex web of the AGH timber " +
            "gridshell: every joint takes two laths, cells alternate triangles and hexagons); Angle asks for a constant " +
            "crossing angle of the two asymptotic families (60° in the AGH gridshell; 90° is a minimal surface).",
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
        pManager.AddIntegerParameter("Refine", "Rf",
            "AAG: smooth laths through the joints (Schling, Wang, Hoyer & Pottmann 2022, §3.4). Every web edge is split into 2^Refine pieces " +
            "and the fine net re-optimised so the refined laths stay asymptotic (upright) or geodesic (flat) while the joints stay put. " +
            "0 = off (laths are joint-to-joint chords), 1 = halves, 2 = quarters (the paper's choice, about five times slower)", GH_ParamAccess.item, 0);
        pManager.AddIntegerParameter("Geodesics", "Gd", "Where the geodesic family runs: 0 = node to node (AAG web, valence-6 nodes), 1 = through the midpoints of the asymptotic laths (tri-hex web, every joint two laths)", GH_ParamAccess.item, 0);
        pManager.AddNumberParameter("Angle", "An", "AAG: target crossing angle (degrees, 0–90) of the two asymptotic families at every node, 0 = free. 60 for the AGH layout; the surface becomes one with k1/k2 = −tan²(Angle/2)", GH_ParamAccess.item, 0.0);
        pManager[1].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("A", "A", "Asymptotic laths of the first family (upright strips)", GH_ParamAccess.list);
        pManager.AddCurveParameter("B", "B", "Asymptotic laths of the second family (upright strips)", GH_ParamAccess.list);
        pManager.AddCurveParameter("G", "G", "The geodesic family (flat strips): node diagonals, or with Geodesics = 1 the chains through the lath midpoints; with AAG off, the geodesic candidates", GH_ParamAccess.list);
        pManager.AddPointParameter("Nodes", "N", "Web nodes (every A × B crossing)", GH_ParamAccess.list);
        pManager.AddMeshParameter("Quads", "Q", "The web as a quad mesh on its nodes", GH_ParamAccess.item);
        pManager.AddTextParameter("Report", "R", "Rays, symmetry, node count, diagonal geodesic error and — with AAG — star and geodesic errors and how far the surface moved", GH_ParamAccess.item);
        pManager.AddMeshParameter("Surface", "Sf", "AAG with Refine > 0: the refined quad mesh the smooth laths lie on (cladding, strip normals)", GH_ParamAccess.item);
        pManager.AddNumberParameter("Angles", "An", "Crossing angle (degrees) of the two asymptotic laths at every node, in Nodes order", GH_ParamAccess.list);
        pManager.AddPointParameter("Midpoints", "Mp", "Geodesics = 1: the crossings of the geodesic laths with the asymptotic laths (one per lath edge; after AAG, where the optimisation put them)", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var input = LoadMesh(DA, 0);
        if (input == null) return;
        Point3d seedPt = Point3d.Unset;
        double spacing = 0; int symmetry = -1, iterations = 16, diagonal = 0, refine = 0, geodesics = 0; bool aag = false; double proximity = 0.3, angle = 0;
        DA.GetData(1, ref seedPt);
        DA.GetData(2, ref spacing);
        DA.GetData(3, ref symmetry);
        DA.GetData(4, ref aag);
        DA.GetData(5, ref iterations);
        DA.GetData(6, ref proximity);
        DA.GetData(7, ref diagonal);
        DA.GetData(8, ref refine);
        DA.GetData(9, ref geodesics);
        DA.GetData(10, ref angle);
        bool midpoint = geodesics == 1;
        if (geodesics != 0 && geodesics != 1) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Geodesics {geodesics} is not a mode (0 node to node, 1 midpoints): using 0.");
        if (angle < 0 || angle > 90) { AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Angle {angle:0.#} is outside 0–90 (the angle between two lines): ignored."); angle = 0; }

        var data = input.Data;
        var proj = new MeshProjection(data);
        int seed = seedPt.IsValid ? proj.NearestVertexGlobal(MeshConvert.ToVec3d(seedPt)) : -1;
        ReportProgress("directions", 1, 50);
        var pc = PrincipalCurvature.Compute(data);
        var web = AsymptoticWeb.Build(data, pc, seed, new AsymptoticWeb.Options
        {
            Spacing = spacing, Symmetry = symmetry, ShouldCancel = Cancelled,
            Progress = Progress("web", 0.02, aag ? 0.4 : 1)
        });
        foreach (var n in web.Notes) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, n);
        // Node distance against the mesh edge and the saddle region (the same rules as the nets' Spacing)
        var saddle = new bool[data.VertexCount];
        for (int i = 0; i < saddle.Length; i++) saddle[i] = pc.K1[i] * pc.K2[i] < 0;
        Report(Mite.Core.Diagnostics.InputAdvice.Spacing(web.Spacing, spacing > 0, 0, false, proj.AverageEdgeLength,
            Mite.Core.Diagnostics.InputAdvice.RegionSize(data, saddle), "Spacing", "nodes"));
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
        if (midpoint) lines.Add($"tri-hex: {web.MidChains.Count} mid chains through {web.MidChains.Sum(c => c.Length)} lath midpoints · chord-midpoint |kg|·spacing {web.MidGeodesicError[0]:G3} / {web.MidGeodesicError[1]:G3} (chord midpoints zigzag by O(spacing²): run AAG to place the crossings)");

        List<Vec3d[]> a = web.A, b = web.B, g = new();
        var nodes = web.Nodes.ToArray();
        var angles = new List<double>();
        var mids = new List<Vec3d>();
        int fam = diagonal == 1 ? 1 : 0;
        if (midpoint)
        {
            for (int i = 0; i < web.MidChains.Count; i++) if (web.MidChainFamily[i] == fam) g.Add(web.MidChains[i]);
            var seenEdge = new HashSet<(int, int)>();
            for (int i = 0; i < web.MidChainEdges.Count; i++)
                for (int k = 0; k < web.MidChainEdges[i].Length; k++)
                {
                    var (p, q) = web.MidChainEdges[i][k];
                    if (seenEdge.Add(p < q ? (p, q) : (q, p))) mids.Add(web.MidChains[i][k]);
                }
        }
        else
            for (int i = 0; i < web.Diagonals.Count; i++) if (web.DiagonalFamily[i] == fam) g.Add(web.Diagonals[i]);
        Mesh? fineMesh = null;
        if (aag)
        {
            var r = AagWeb.Optimize(data, web, new AagWeb.Options { Iterations = Math.Max(1, iterations), Proximity = Math.Max(0, proximity), Progress = Progress("AAG", 0.4, 1), GeodesicFamily = fam, Midpoint = midpoint, CrossingAngle = angle, ShouldCancel = Cancelled });
            a = r.A; b = r.B; g = r.G; nodes = r.Nodes;
            angles.AddRange(r.CrossingAngles);
            if (midpoint) { mids.Clear(); mids.AddRange(r.Midpoints); }
            lines.Add($"AAG{(midpoint ? " tri-hex" : "")}: geodesic {r.InitialGeodesicError:0.##}° → {r.GeodesicError:0.###}° · stars {r.InitialStarError:0.##}° → {r.StarError:0.###}°" +
                (midpoint ? $" · laths asymptotic at the midpoints {r.InitialAsymptoticError:0.##}° → {r.AsymptoticError:0.###}° · midpoints shifted {r.MeanMidpointShift:P1} of the spacing (max {r.MaxMidpointShift:P1})" : "") +
                $" · surface moved {r.MeanDeviation:G3} mean, {r.MaxDeviation:G3} max · nodes moved {r.MeanNodeMove:G3} mean, {r.MaxNodeMove:G3} max · {r.Iterations} iterations");
            lines.Add($"crossing angle: {r.InitialAngleMin:0.#}–{r.InitialAngleMax:0.#}° (mean {r.InitialAngleMean:0.#}°) → {r.AngleMin:0.#}–{r.AngleMax:0.#}° (mean {r.AngleMean:0.#}°)" + (angle > 0 ? $" · target {angle:0.#}°" : ""));
            if (r.GeodesicError > 1 || r.StarError > 1 || (midpoint && r.AsymptoticError > 1))
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, angle > 0
                    ? $"No AAG web with {angle:0.#}° crossings exists near this surface (a constant angle needs k1/k2 = −{Math.Tan(angle * Math.PI / 360):0.###}² everywhere): lower Proximity, raise Iterations, or set Angle 0."
                    : "The surface carries no AAG web close to it: lower Proximity to let it change more, try the other Diagonal family, or start from a surface of revolution.");
            if (refine > 0 && midpoint)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Refine applies to node-to-node webs (Geodesics 0); the tri-hex web is left as chords.");
            else if (refine > 0)
            {
                var f = AagRefinement.Refine(data, web, r, new AagRefinement.Options { Levels = Math.Min(3, refine), ShouldCancel = Cancelled });
                a = f.A; b = f.B; g = f.G;
                lines.Add($"Refined ×{f.Subdivisions}: laths turn ≤ {f.MaxTurning:0.#}° per step (chords {f.InitialMaxTurning:0.#}°) · stars {f.StarError:0.###}° · geodesic {f.GeodesicError:0.###}° · " +
                          $"|kn|·spacing ≤ {f.MaxNormalCurvature:G2}, |kg|·spacing ≤ {f.MaxGeodesicCurvature:G2} · joints moved ≤ {f.JointShift:G2} · {f.Vertices.Length} fine vertices");
                fineMesh = new Mesh();
                foreach (var p in f.Vertices) fineMesh.Vertices.Add(MeshConvert.ToRhinoPoint(p));
                foreach (var q in f.Quads) fineMesh.Faces.AddFace(q[0], q[1], q[2], q[3]);
                fineMesh.Normals.ComputeNormals();
            }
        }
        else
        {
            // crossing angles of the web as traced: chords of the two laths through each node
            for (int i = 0; i < web.Nodes.Count; i++)
            {
                var na = web.NeighborsA[i]; var nb = web.NeighborsB[i];
                if (na.Length == 0 || nb.Length == 0) { angles.Add(double.NaN); continue; }
                var ta = na.Length >= 2 ? web.Nodes[na[1]] - web.Nodes[na[0]] : web.Nodes[na[0]] - web.Nodes[i];
                var tb = nb.Length >= 2 ? web.Nodes[nb[1]] - web.Nodes[nb[0]] : web.Nodes[nb[0]] - web.Nodes[i];
                angles.Add(ta.Length < 1e-12 || tb.Length < 1e-12 ? double.NaN : Math.Acos(Math.Min(1, Math.Abs(Vec3d.Dot(ta, tb)) / (ta.Length * tb.Length))) * 180 / Math.PI);
            }
            var ok = angles.Where(x => !double.IsNaN(x)).ToList();
            if (ok.Count > 0) lines.Add($"crossing angle: {ok.Min():0.#}–{ok.Max():0.#}° (mean {ok.Average():0.#}°)");
            if (refine > 0) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Refine smooths the laths of the AAG web; without AAG the laths are already the traced curves.");
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
        if (fineMesh != null) DA.SetData(6, fineMesh);
        DA.SetDataList(7, angles);
        DA.SetDataList(8, mids.Select(MeshConvert.ToRhinoPoint));
    }
}
