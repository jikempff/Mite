using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.Analysis;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;

namespace Mite.Grasshopper.Components;

public class GridshellAnalysisComponent : MiteComponent
{
    public GridshellAnalysisComponent()
        : base("Gridshell Analysis", "GridFE",
            "Linear static analysis of a lath network as a 3D beam frame. Laths become " +
            "Euler-Bernoulli beams with the strip section, coupled at net crossings (rigid, " +
            "scissor hinge or semi-rigid about the surface normal); supports are fixed or pinned points; " +
            "loads per metre of lath, self-weight, area load and point loads; linear (Euler) buckling of the loaded net: " +
            "the load factor at which the first mode buckles and its shape. Geometry is converted from the document units to " +
            "metres internally, so E / Allowable are in Pa and Load in N/m whatever the model units. " +
            "A first-order sanity check, not a full FE package. A second family (CurvesB) may carry its own " +
            "section (WidthB, ThicknessB, UprightB): upright asymptotic lamellas with flat geodesic slats — the " +
            "asymptotic–geodesic hybrid — are analysed with each lath's own A, I and J.",
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
        pManager.AddIntegerParameter("SupportType", "St", "0 = fixed (all six degrees of freedom), 1 = pinned (translations held, rotations free)", GH_ParamAccess.item, 0);
        pManager.AddNumberParameter("JointStiffness", "Jk", "Rotational stiffness of the joints about the surface normal in N·m/rad: −1 = rigid (default), 0 = free scissor hinge (a bolt along the normal), > 0 semi-rigid bolted joint", GH_ParamAccess.item, -1.0);
        pManager.AddNumberParameter("Density", "ρ", "Material density in kg/m³ for self-weight (0 = none; timber ≈ 450, steel 7850)", GH_ParamAccess.item, 0.0);
        pManager.AddNumberParameter("AreaLoad", "q", "Vertical area load in N/m² on the mesh area (snow, cladding), shared by the laths per metre of length (0 = none)", GH_ParamAccess.item, 0.0);
        pManager.AddPointParameter("LoadPoints", "Lp", "Points where concentrated forces act (attached to the nearest node)", GH_ParamAccess.list);
        pManager.AddVectorParameter("Forces", "F", "Force in N per load point (one per point, or one for all)", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Layer", "Ly", "Layer of each lath along the surface normal, one per curve or one for all: 0 the layer on the surface, 1 the layer stacked on it, 2 the next … Laths of different layers meeting at a joint keep their own axes one lath depth apart and are bolted through a rigid link, so the forces handed between them act with that lever arm (torsion of the lower lath under a horizontal pull of the upper one). Empty = every axis on its curve, the single-node joint of earlier versions", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Align", "Al", "Where layer 0 sits along the surface normal when Layer is given, as in Lath Sweep: 0 grows from the surface out (default), 1 centred on the surface, 2 grows from the surface in (layers then stack inward)", GH_ParamAccess.item, 0);
        pManager[2].Optional = true;
        pManager[19].Optional = true;
        pManager[20].Optional = true;
        pManager[21].Optional = true;
        RegisterNetInput(pManager, "With a Net, empty Joints couple every contact of the net and empty Supports hold every lath end on the border; " +
            "the frame takes family A's profile unless W / T / U / Shape / Section are wired"); // 23
        pManager.AddIntegerParameter("Buckling", "Bk", "Number of linear buckling modes to compute from the axial forces of the solve (0 = none, default 1). λ × the applied loads is the Euler critical load of the net; below ~3 the design is governed by stability", GH_ParamAccess.item, 1);
        pManager[0].Optional = true;
        pManager[1].Optional = true;
        pManager[2].Optional = true;
        pManager[3].Optional = true;
        // Family B with its own section (appended 2026-10-27, after Buckling): unset = as family A
        pManager.AddCurveParameter("CurvesB", "Cb", "Second lath family with its own section (optional; Curves is then family A). Outputs list A's laths first, then B's", GH_ParamAccess.list);
        pManager.AddNumberParameter("WidthB", "Wb", "Width of family B in model units (unset: as Width)", GH_ParamAccess.item);
        pManager.AddNumberParameter("ThicknessB", "Tb", "Thickness of family B in model units (unset: as Thickness)", GH_ParamAccess.item);
        pManager.AddBooleanParameter("UprightB", "Ub", "Orientation of family B when it differs from A (unset: as Upright): upright asymptotic A with flat geodesic B is the asymptotic–geodesic hybrid", GH_ParamAccess.item);
        pManager[25].Optional = true;
        pManager[26].Optional = true;
        pManager[27].Optional = true;
        pManager[28].Optional = true;
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
        pManager.AddPointParameter("SupportNodes", "Sn", "The frame nodes the supports hold", GH_ParamAccess.list);
        pManager.AddVectorParameter("Reactions", "R", "Reaction force in N at each support node", GH_ParamAccess.list);
        pManager.AddTextParameter("Report", "Rp", "Totals: applied load, reactions, equilibrium error, joints, supports", GH_ParamAccess.item);
        pManager.AddVectorParameter("ReactionMoments", "Rm", "Reaction moment in N·m at each support node (zero at pinned supports)", GH_ParamAccess.list);
        pManager.AddNumberParameter("BucklingFactor", "λ", "Linear buckling load factor of the first mode: the applied loads × λ make the net buckle elastically (null when nothing is in compression or no positive factor converged)", GH_ParamAccess.item);
        pManager.AddNumberParameter("Factors", "λs", "Load factors of the computed buckling modes, ascending", GH_ParamAccess.list);
        pManager.AddCurveParameter("Mode", "Bm", "First buckling mode: the laths displaced by the mode shape, largest node offset = Scale × 5 % of the net's size (shape only, the amplitude is arbitrary)", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var netIn = GetNet(DA, 23);
        var input = LoadMeshOrNet(DA, 0, netIn);
        if (input == null) return;
        var curves = new List<Curve>();
        if (!ReadCurvesOrNet(DA, 1, netIn, curves, -1, out bool fromNet)) return;
        var joints = new List<Point3d>();
        var supports = new List<Point3d>();
        DA.GetDataList(2, joints);
        DA.GetDataList(3, supports);
        if (fromNet && netIn != null)
        {
            if (joints.Count == 0)
                foreach (var x in netIn.Contacts()) joints.Add(MeshConvert.ToRhinoPoint(x.Point));
            if (supports.Count == 0)
            {
                foreach (var end in netIn.Ends())
                    if (end.Kind == LathEndKind.Border) supports.Add(MeshConvert.ToRhinoPoint(end.Point));
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Supports: the {supports.Count} lath ends on the border of the net.");
            }
        }
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
        int supportType = 0; double jointK = -1, density = 0, areaLoad = 0;
        DA.GetData(15, ref supportType);
        DA.GetData(16, ref jointK);
        DA.GetData(17, ref density);
        DA.GetData(18, ref areaLoad);
        var loadPts = new List<Point3d>(); var forces = new List<Vector3d>();
        DA.GetDataList(19, loadPts);
        DA.GetDataList(20, forces);
        var layers = new List<int>();
        DA.GetDataList(21, layers);
        int align = 0;
        DA.GetData(22, ref align);
        int buckling = 1;
        DA.GetData(24, ref buckling);
        var curvesB = new List<Curve>();
        DA.GetDataList(25, curvesB);
        double widthB = width, thicknessB = thickness; bool uprightB = upright;
        if (!DA.GetData(26, ref widthB)) widthB = width;
        if (!DA.GetData(27, ref thicknessB)) thicknessB = thickness;
        if (!DA.GetData(28, ref uprightB)) uprightB = upright;
        int countA = curves.Count;
        bool hybrid = curvesB.Count > 0;
        if (hybrid)
        {
            if (fromNet) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "CurvesB is appended to the Net's laths; the net's own families keep their profiles unless W / T / U / Shape / Section are wired.");
            curves = curves.Concat(curvesB).ToList();
        }

        if (supports.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "At least one support point is required.");
            return;
        }
        if (width <= 0 || thickness <= 0 || e <= 0 || allowable <= 0 || widthB <= 0 || thicknessB <= 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Width, Thickness (and WidthB / ThicknessB), E and Allowable must be positive.");
            return;
        }
        CheckSection(width, thickness, curves);

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
        if (!SectionInput.TryBuild(this, shape, section, width, thickness, upright, 0.0, s, hybrid ? countA : curves.Count, out LathProfile profile)) return;
        LathProfile profileB = profile;
        if (hybrid && !SectionInput.TryBuild(this, shape, section, widthB, thicknessB, uprightB, 0.0, s, curvesB.Count, out profileB)) return;
        // A net that carries its profiles hands one section per family: the
        // frame takes A's and B's each for its own laths (the hybrid of a net)
        var netProfile = NetProfile(netIn, 0, 7, 8, 9, 13, 14);
        LathProfile? netProfileB = netIn != null && netIn.CountB > 0 ? NetProfile(netIn, netIn.CountA, 7, 8, 9, 13, 14) : null;
        if (netProfile != null && fromNet && !hybrid)
        {
            profile = netProfile.Value.Scaled(s);
            countA = netIn!.CountA;
            profileB = netProfileB?.Scaled(s) ?? profile;
            hybrid = netProfileB != null && LathNet.Describe(netProfileB) != LathNet.Describe(netProfile);
        }
        else if (netProfile != null)
        {
            profile = netProfile.Value.Scaled(s);
            if (!hybrid) profileB = profile;
            if (netProfileB != null && LathNet.Describe(netProfileB) != LathNet.Describe(netProfile))
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "The net's B profile differs from A's; with Curves wired the frame uses family A's profile for them (wire CurvesB for a second section).");
        }

        var laths = new List<Vec3d[]>();
        var lathIndex = new List<int>();
        var profiles = new List<LathProfile>();
        int skipped = 0;
        for (int c = 0; c < curves.Count; c++)
        {
            var line = curves[c] == null ? null : CurveSample.ToPolyline(curves[c], chord);
            if (line == null || line.Length < 2) { skipped++; continue; }
            var sub = Subdivide(line, maxSegment);
            for (int i = 0; i < sub.Length; i++) sub[i] = s * sub[i];
            laths.Add(sub);
            lathIndex.Add(c);
            profiles.Add(c < countA ? profile : profileB);
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

        // Layers: lath axes stacked along the normal, one lath depth per layer
        // (metres, like the profile); empty = every axis on its curve
        List<double>? axisOffsets = null;
        if (layers.Count > 0)
        {
            if (layers.Count != 1 && layers.Count != curves.Count)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Layer has {layers.Count} values for {curves.Count} curves: give one per curve or one for all.");
                return;
            }
            var aligned = profile.WithAlign(AlignOf(align));
            double depth = aligned.NormalHigh - aligned.NormalLow;
            double sign = align == 2 ? -1.0 : 1.0;
            axisOffsets = new List<double>(laths.Count);
            foreach (int ci in lathIndex)
            {
                int layer = Math.Max(0, layers[layers.Count == 1 ? 0 : ci]);
                axisOffsets.Add(aligned.Lift + sign * layer * depth);
            }
        }

        List<(Vec3d, Vec3d)>? pointLoads = null;
        if (loadPts.Count > 0 && forces.Count > 0)
        {
            pointLoads = new List<(Vec3d, Vec3d)>();
            for (int i = 0; i < loadPts.Count; i++)
                pointLoads.Add((s * MeshConvert.ToVec3d(loadPts[i]), MeshConvert.ToVec3d(forces[Math.Min(i, forces.Count - 1)])));
        }

        FrameAnalysis.Result result;
        try
        {
            result = FrameAnalysis.Compute(meshM, laths, jointPts, supportPts,
                profiles,
                MeshConvert.ToVec3d(load),
                new FrameAnalysis.Options
                {
                    E = e, AllowableStress = allowable,
                    Support = supportType == 1 ? FrameAnalysis.SupportKind.Pinned : FrameAnalysis.SupportKind.Fixed,
                    JointRotationalStiffness = jointK < 0 ? double.PositiveInfinity : jointK,
                    Density = Math.Max(0, density), AreaLoad = areaLoad,
                    PointLoads = pointLoads,
                    AxisOffsets = axisOffsets,
                    BucklingModes = Math.Max(0, buckling),
                    Progress = Progress(),
                });
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
        var supNodes = new List<Point3d>(); var reacts = new List<Vector3d>(); var moments = new List<Vector3d>();
        foreach (int n in result.SupportNodes) { supNodes.Add(MeshConvert.ToRhinoPoint(inv * result.Nodes[n])); reacts.Add(MeshConvert.ToRhinoVector(result.Reactions[n])); moments.Add(MeshConvert.ToRhinoVector(result.ReactionMoments[n])); }
        DA.SetDataList(8, supNodes);
        DA.SetDataList(9, reacts);
        DA.SetDataList(11, moments);
        var sumR = Vec3d.Zero; foreach (int n in result.SupportNodes) sumR = sumR + result.Reactions[n];
        string families = "";
        if (hybrid)
        {
            double peakA = 0, peakB = 0;
            for (int c = 0; c < curves.Count; c++) { if (c < countA) peakA = Math.Max(peakA, lathMax[c]); else peakB = Math.Max(peakB, lathMax[c]); }
            string Sec(LathProfile p) => p.Kind == SectionKind.Rectangle ? $"{p.Width / s:G4} × {p.Thickness / s:G4} {(p.Upright ? "upright" : "flat")}" : p.Kind == SectionKind.Round ? $"round Ø {p.Width / s:G4}" : $"custom {p.Width / s:G4} × {p.Thickness / s:G4} {(p.Upright ? "upright" : "flat")}";
            families = $"\nfamily A {countA} laths {Sec(profile)}, peak utilization {peakA:0.00} · family B {curves.Count - countA} laths {Sec(profileB)}, peak utilization {peakB:0.00}" +
                (profile.Upright != profileB.Upright ? " · asymptotic–geodesic hybrid (one family upright, one flat)" : "");
        }
        string buck = buckling <= 0 ? "buckling not computed"
            : result.BucklingFactors.Length == 0 ? (result.BucklingConverged ? "buckling: nothing in compression, no Euler buckling under this load" : "buckling: no positive factor converged")
            : $"buckling factor λ = {result.BucklingFactors[0]:0.###} (modes: {string.Join(", ", result.BucklingFactors.Select(f => f.ToString("0.###")))}; " +
              $"{(result.BucklingMissed == 0 ? "Sturm check: none missed below λ" : result.BucklingMissed < 0 ? "Sturm check unavailable" : $"Sturm check: {result.BucklingMissed} lower mode(s) missed")}" +
              $"{(result.BucklingConverged ? "" : ", not converged")})";
        string stack = axisOffsets == null ? "lath axes on the curves"
            : $"{layers.Distinct().Count()} layer(s) · joint eccentricity {result.Eccentricity * 1000:0.#} mm · moment equilibrium error {result.MomentEquilibriumError:E1}";
        DA.SetData(10, $"load {result.TotalLoad.Z / 1000:0.###} kN (vertical) · reactions {sumR.Z / 1000:0.###} kN · equilibrium error {result.EquilibriumError:E1}\n" +
            $"{result.Nodes.Length} nodes · {result.Utilization.Length} elements · {result.JointCount} joints · {result.SupportNodeCount} supports ({(supportType == 1 ? "pinned" : "fixed")}) · joints {(jointK < 0 ? "rigid" : jointK == 0 ? "hinged" : $"k = {jointK:G3} N·m/rad")} · {stack}\n" +
            $"max displacement {result.MaxDisplacement * 1000:0.#} mm · peak utilization {result.MaxUtilization:0.00}" + families + $"\n{buck}");

        if (result.BucklingFactors.Length > 0)
        {
            DA.SetData(12, result.BucklingFactors[0]);
            DA.SetDataList(13, result.BucklingFactors);
            // mode shape at a readable amplitude: 5 % of the net's bounding diagonal, times Scale
            var mn = new Vec3d(double.MaxValue, double.MaxValue, double.MaxValue); var mx = new Vec3d(double.MinValue, double.MinValue, double.MinValue);
            foreach (var p in result.Nodes) { mn = new Vec3d(Math.Min(mn.X, p.X), Math.Min(mn.Y, p.Y), Math.Min(mn.Z, p.Z)); mx = new Vec3d(Math.Max(mx.X, p.X), Math.Max(mx.Y, p.Y), Math.Max(mx.Z, p.Z)); }
            double amp = 0.05 * (mx - mn).Length * scale;
            var mode = result.BucklingModes[0];
            var modeCurves = new Curve?[curves.Count];
            for (int c = 0; c < laths.Count; c++)
            {
                var pts = new List<Point3d>();
                for (int i = 0; i < laths[c].Length; i++)
                {
                    Vec3d d = nodeOf.TryGetValue((c, i), out int node) ? mode[node] : Vec3d.Zero;
                    pts.Add(MeshConvert.ToRhinoPoint(inv * (laths[c][i] + amp * d)));
                }
                if (pts.Count > 1) modeCurves[lathIndex[c]] = new PolylineCurve(pts);
            }
            DA.SetDataList(14, modeCurves);
            if (result.BucklingFactors[0] < 1.0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Buckling factor {result.BucklingFactors[0]:F2} is below 1 — the net buckles under this load before any lath is overstressed. Stiffen the section, add supports or shorten the laths between joints.");
            else if (result.BucklingFactors[0] < 3.0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"Buckling factor {result.BucklingFactors[0]:F2}: elastic stability governs; a factor of 3 or more is customary for gridshells before imperfections and nonlinearity.");
            if (result.BucklingMissed > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"The Sturm sequence check finds {result.BucklingMissed} buckling mode(s) below the factor reported — the iteration missed the lowest mode; raise Buckling and check the result.");
            if (!result.BucklingConverged)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "The buckling eigenvalue iteration did not converge; the factors shown are approximate.");
        }
        else if (buckling > 0 && !result.BucklingConverged)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No positive buckling factor converged — the net may buckle under the reversed load first; check the axial forces.");

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
