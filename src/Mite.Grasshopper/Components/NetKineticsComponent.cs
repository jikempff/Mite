using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Rhino.Geometry;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;
using Mite.Core.Kinetics;

namespace Mite.Grasshopper.Components;

/// <summary>
/// Kinetic simulation of a scissor-jointed asymptotic net (Wan, Crolla &amp;
/// Schling 2025; Schikore, Schling, Oberbichler &amp; Bauer 2020): joints keep
/// their spacing along the laths, laths stay asymptotic (perpendicular to the
/// moving surface normal) and keep their rest bend as far as the hinges allow;
/// supports, moved points and actuator cables drive the motion from Fold 0 to 1.
/// </summary>
public class NetKineticsComponent : MiteComponent
{
    public NetKineticsComponent()
        : base("Net Kinetics", "Kinetics",
            "Moves a scissor-jointed asymptotic net as a mechanism (Wan, Crolla & Schling 2025; Schikore et al. 2020). " +
            "The crossings of A and B become scissor hinges: the distance between consecutive joints along every lath " +
            "stays constant, every lath segment stays perpendicular to the (moving) surface normal at its ends — so " +
            "upright laths remain straight-unrollable — and laths keep their rest bend with the given Stiffness. " +
            "Drive the motion with Fixed points, Slide points (joints kept on the ground plane, free to slide), points moved To targets, Cables whose Lengths change, or Flatten (press the net onto a plane); Fold 0…1 " +
            "runs the drivers from the rest state to the target in Steps states. Drift, Deviation and Miss tell you " +
            "how well the mechanism can follow (a rigid net cannot follow at all; the exact doubly ruled grids follow to 1e-12). " +
            "Every state is also read elastically (Schikore, Schling, Oberbichler & Bauer 2020): bending and twist strains of the lath section " +
            "(Upright/Width/Thickness or Shape/Section, as Lath Analysis), the strain energy Π = ½∫(GJτ² + EIκ²) ds per state and the natural " +
            "(least-energy) state along the motion.",
            "Kinetics", "NetKinetics") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCE01");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("CurvesA", "A", "First lath family (asymptotic curves)", GH_ParamAccess.list);
        pManager.AddCurveParameter("CurvesB", "B", "Second lath family", GH_ParamAccess.list);
        pManager.AddPointParameter("Fixed", "F", "Support points: the nearest joints are held in place", GH_ParamAccess.list);
        pManager[2].Optional = true;
        pManager.AddPointParameter("Move", "Mv", "Points whose nearest joints are moved (paired with To by index)", GH_ParamAccess.list);
        pManager[3].Optional = true;
        pManager.AddPointParameter("To", "To", "Target position at Fold = 1 for each Move point (interpolated linearly from the rest position)", GH_ParamAccess.list);
        pManager[4].Optional = true;
        pManager.AddLineParameter("Cables", "C", "Actuator cables: each line's ends snap to the nearest joints", GH_ParamAccess.list);
        pManager[5].Optional = true;
        pManager.AddNumberParameter("Lengths", "Le", "Cable length at Fold = 1 for each cable (the rest length is the line's length; the last value repeats)", GH_ParamAccess.list);
        pManager[6].Optional = true;
        pManager.AddNumberParameter("Fold", "Fo", "How far to run the drivers: 0 = rest state (settled), 1 = targets reached", GH_ParamAccess.item, 1.0);
        pManager.AddIntegerParameter("Steps", "St", "Number of states between rest and Fold (each solved from the previous one)", GH_ParamAccess.item, 10);
        pManager.AddNumberParameter("Stiffness", "K", "Bending stiffness of the laths relative to the hinge constraints (0 = laths kink freely at every node, 1 = default, 10 = nearly rigid pre-bent laths)", GH_ParamAccess.item, 1.0);
        pManager.AddNumberParameter("Segment", "Sg", "Longest lath piece between nodes: members longer than this get intermediate nodes so laths can bend between joints (0 = joints only)", GH_ParamAccess.item, 0.0);
        pManager.AddIntegerParameter("Iterations", "It", "Solver iterations per state", GH_ParamAccess.item, 60);
        pManager.AddNumberParameter("Tolerance", "X", "Maximum gap accepted as a crossing (0 = automatic)", GH_ParamAccess.item, 0.0);
        pManager.AddNumberParameter("Sampling", "S", "Chord deviation for curve sampling (0 = automatic: 1/500 of the net size)", GH_ParamAccess.item, 0.0);
        pManager.AddMeshParameter("Mesh", "M", "Optional surface mesh: its normals start the joint normals (otherwise they come from the crossing laths)", GH_ParamAccess.item);
        pManager[14].Optional = true;
        pManager.AddPointParameter("Slide", "Sl", "Ground points: the nearest joints stay on the plane through their rest position (normal SlideNormal) and slide in it", GH_ParamAccess.list);
        pManager[15].Optional = true;
        pManager.AddVectorParameter("SlideNormal", "Sn", "Normal of the sliding planes (default Z: joints stay on the ground)", GH_ParamAccess.item, Vector3d.ZAxis);
        // elastic reading of every state (Schikore et al. 2020): the lath section as on Lath Analysis
        pManager.AddBooleanParameter("Upright", "U", "True: laths stand upright on the surface (asymptotic gridshells); false: flat strips", GH_ParamAccess.item, true);
        pManager.AddNumberParameter("Width", "W", "Strip width, across the curve (default 0.1)", GH_ParamAccess.item, 0.1);
        pManager.AddNumberParameter("Thickness", "T", "Strip thickness (default 0.01)", GH_ParamAccess.item, 0.01);
        pManager.AddNumberParameter("MaxStrain", "E", "Allowable bending strain for the utilization (default 0.005 ≈ timber; 0.002 steel, 0.008 GFRP)", GH_ParamAccess.item, 0.005);
        pManager.AddNumberParameter("Modulus", "Em", "Young's modulus in Pa for the strain energy (default 11 GPa); the shear modulus is taken as E/16", GH_ParamAccess.item, 11e9);
        pManager.AddNumberParameter("Density", "Rho", "Density in kg/m³ for the self-weight potential in the energy diagram (0 = ignore self-weight)", GH_ParamAccess.item, 0.0);
        RegisterSectionInputs(pManager); // 23 Shape, 24 Section — the same profile Lath Sweep builds
        pManager.AddBooleanParameter("Flatten", "Fl", "Press the net flat: the first Fixed joint (or the joint nearest the centre) is held and every lath end slides on a plane of normal SlideNormal that moves from its rest height onto the plane through that joint as Fold goes 0 → 1. " +
            "The flat state is the one the mechanism reaches — a 3-fold Enneper web closes into a hexagon; read the states backwards for the deployment from the flat grid", GH_ParamAccess.item, false);
        RegisterNetInput(pManager, "Its laths are the scissor grid, its surface starts the joint normals (unless Mesh is wired) and its profile family A's section is the one read elastically (unless U / W / T / Shape / Section are wired)"); // 26
        pManager[0].Optional = true;
        pManager[1].Optional = true;
        // deployment from the flat (assembled) state — the kit on the table
        pManager.AddIntegerParameter("Deploy", "Dp", "Deploy the net from its flat state on a table (every lath end rests on a one-sided ground through its rest position: it may lift off, not sink). " +
            "0 = off. 1 = open every scissor to Angle (a hub motor, the hands that open the kit; 90° for a web on a minimal surface): the flat 3-fold Enneper hexagon comes back to its web within a tenth of the joint spacing. " +
            "2 = the Move / To and Cable drivers act with the net on the table — pulling the singular node alone does not deploy a grid, the drivers' miss says so. " +
            "With Flatten on, the net is pressed flat first and deployed from there (the states are the deployment); otherwise the input curves are taken as the flat state (a Flat Kit's flat assembly).", GH_ParamAccess.item, 0);
        pManager.AddNumberParameter("Angle", "An", "Deploy 1: crossing angle (degrees) the scissors are opened to (90 on a minimal surface, where the asymptotic curves cross at right angles). 0 or less = each joint's own crossing angle in the input net — the drive that reproduces any anticlastic design, whose angles change from joint to joint (a hyperbolic paraboloid, a hyperboloid)", GH_ParamAccess.item, 90.0);
        pManager.AddNumberParameter("Kick", "Kk", "Deploy: the flat state is a bifurcation — a solver started exactly flat never leaves the plane — so the start is kicked along the net's softest mode by Kick × the mean joint spacing; the sign picks the branch (the deployment or its mirror image), 0 shows the lock", GH_ParamAccess.item, 0.5);
        // one-wire setups (appended 2026-11-04): their points join Fixed / Slide / Move / To / Cables / Lengths
        pManager.AddGenericParameter("Supports", "Su", "Kinetic Supports output: its fixed and sliding points join Fixed and Slide (its plane normal is used when it has sliding points)", GH_ParamAccess.item); // 30
        pManager.AddGenericParameter("Drivers", "Dr", "Kinetic Drivers output: its moved points and cables join Move / To and Cables / Lengths", GH_ParamAccess.item); // 31
        pManager[30].Optional = true;
        pManager[31].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("A", "A", "Family A laths at Fold (polylines through the joints)", GH_ParamAccess.list);
        pManager.AddCurveParameter("B", "B", "Family B laths at Fold", GH_ParamAccess.list);
        pManager.AddPointParameter("Joints", "J", "Joint positions at Fold", GH_ParamAccess.list);
        pManager.AddVectorParameter("Normals", "N", "Surface normal at each joint at Fold", GH_ParamAccess.list);
        pManager.AddNumberParameter("Angles", "An", "Scissor angle (degrees) between the two laths at each joint at Fold", GH_ParamAccess.list);
        pManager.AddCurveParameter("StatesA", "SA", "Family A laths per state (one branch per state, Fold 0 → Fold)", GH_ParamAccess.tree);
        pManager.AddCurveParameter("StatesB", "SB", "Family B laths per state", GH_ParamAccess.tree);
        pManager.AddNumberParameter("Folds", "Fs", "Fold parameter of each state", GH_ParamAccess.list);
        pManager.AddNumberParameter("Drift", "Dr", "Per state: largest relative change of a joint spacing (0 = perfect scissor joints)", GH_ParamAccess.list);
        pManager.AddNumberParameter("Deviation", "Dv", "Per state: largest angle (degrees) between a lath segment and the tangent plane (0 = exactly asymptotic)", GH_ParamAccess.list);
        pManager.AddNumberParameter("Miss", "Ms", "Per state: largest distance of a moved point from its target, of a cable from its length or of a sliding joint from its plane", GH_ParamAccess.list);
        pManager.AddTextParameter("Report", "R", "Summary of the motion", GH_ParamAccess.item);
        pManager.AddNumberParameter("Utilization", "U", "Per state: peak strain utilization over all laths (bending and twist of the section, as Lath Analysis)", GH_ParamAccess.list);
        pManager.AddNumberParameter("LathUtil", "Ul", "Per state (branch) and lath: peak utilization — A laths first, then B; the last branch goes straight into Lath Preview", GH_ParamAccess.tree);
        pManager.AddNumberParameter("Energy", "En", "Per state: strain energy Π = ½∫(GJτ² + EI κ²) ds in joules, plus the self-weight potential when Density is set", GH_ParamAccess.list);
        pManager.AddNumberParameter("Natural", "Nf", "Fold parameter of the least-energy state along the motion (Schikore et al.'s natural configuration); NaN when the energy is flat", GH_ParamAccess.item);
        pManager.AddBooleanParameter("Buildable", "Ok", "True when every state stays within MaxStrain", GH_ParamAccess.item);
        RegisterNetOutput(pManager, "The net at Fold (kind Kinetic, the profile used, no surface — the moved grid no longer lies on the input surface)"); // 17
        pManager.AddGenericParameter("Motion", "Mo", "The whole solved motion (appended 2026-11-04): wire it into Kinetic State to read the net at any fold — between the solved states, or to bake one", GH_ParamAccess.item); // 18
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var curvesA = new List<Curve>();
        var curvesB = new List<Curve>();
        var fixedPts = new List<Point3d>();
        var movePts = new List<Point3d>();
        var toPts = new List<Point3d>();
        var cables = new List<Line>();
        var lengths = new List<double>();
        double fold = 1.0, stiffness = 1.0, segment = 0.0, tolerance = 0.0, sampling = 0.0;
        int steps = 10, iterations = 60;
        Mesh? mesh = null;
        var slidePts = new List<Point3d>();
        var slideNormal = Vector3d.ZAxis;
        bool upright = true; double width = 0.1, thickness = 0.01, maxStrain = 0.005, modulus = 11e9, density = 0.0;
        int shape = 0; Curve? section = null;
        var netIn = GetNet(DA, 26);
        if (!ReadCurvesOrNet(DA, 0, netIn, curvesA, 0)) return;
        if (!ReadCurvesOrNet(DA, 1, netIn, curvesB, 1)) return;
        DA.GetDataList(2, fixedPts);
        DA.GetDataList(3, movePts);
        DA.GetDataList(4, toPts);
        DA.GetDataList(5, cables);
        DA.GetDataList(6, lengths);
        DA.GetData(7, ref fold);
        DA.GetData(8, ref steps);
        DA.GetData(9, ref stiffness);
        DA.GetData(10, ref segment);
        DA.GetData(11, ref iterations);
        DA.GetData(12, ref tolerance);
        DA.GetData(13, ref sampling);
        DA.GetData(14, ref mesh);
        DA.GetDataList(15, slidePts);
        DA.GetData(16, ref slideNormal);
        if (slideNormal.IsTiny()) slideNormal = Vector3d.ZAxis;
        DA.GetData(17, ref upright);
        DA.GetData(18, ref width);
        DA.GetData(19, ref thickness);
        DA.GetData(20, ref maxStrain);
        DA.GetData(21, ref modulus);
        DA.GetData(22, ref density);
        DA.GetData(23, ref shape);
        DA.GetData(24, ref section);
        bool flatten = false;
        DA.GetData(25, ref flatten);
        int deploy = 0; double deployAngle = 90.0, kick = 0.5;
        DA.GetData(27, ref deploy);
        DA.GetData(28, ref deployAngle);
        DA.GetData(29, ref kick);
        if (!KineticSetupGoo.TryRead(this, DA, 30, out var supportsIn)) return;
        if (!KineticSetupGoo.TryRead(this, DA, 31, out var driversIn)) return;
        var wired = KineticSetup.Merge(supportsIn, driversIn);
        if (wired != null && (wired.HasSupports || wired.HasDrivers))
        {
            KineticSetupGoo.Report(this, wired);
            // pad the own To / Lengths to their partners so the appended pairs stay paired
            while (toPts.Count > 0 && toPts.Count < movePts.Count) toPts.Add(toPts[toPts.Count - 1]);
            while (lengths.Count > 0 && lengths.Count < cables.Count) lengths.Add(lengths[lengths.Count - 1]);
            fixedPts.AddRange(wired.Fixed.Select(MeshConvert.ToRhinoPoint));
            slidePts.AddRange(wired.Slide.Select(MeshConvert.ToRhinoPoint));
            if (wired.Slide.Count > 0 && wired.SlideNormal.HasValue) slideNormal = MeshConvert.ToRhinoVector(wired.SlideNormal.Value);
            movePts.AddRange(wired.Move.Select(MeshConvert.ToRhinoPoint));
            var wto = wired.To.ToList(); while (wto.Count > 0 && wto.Count < wired.Move.Count) wto.Add(wto[wto.Count - 1]);
            toPts.AddRange(wto.Select(MeshConvert.ToRhinoPoint));
            cables.AddRange(wired.Cables.Select(c => new Line(MeshConvert.ToRhinoPoint(c.A), MeshConvert.ToRhinoPoint(c.B))));
            var wl = wired.Lengths.ToList(); while (wl.Count > 0 && wl.Count < wired.Cables.Count) wl.Add(wl[wl.Count - 1]);
            lengths.AddRange(wl);
        }
        if (width <= 0 || thickness <= 0 || maxStrain <= 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Width, Thickness and MaxStrain must be positive.");
            return;
        }
        CheckSection(width, thickness, System.Linq.Enumerable.Concat(curvesA, curvesB));
        Report(Mite.Core.Diagnostics.InputAdvice.KineticMaterial(modulus, maxStrain, density));
        if (!SectionInput.TryBuild(this, shape, section, width, thickness, upright, 0.0, 1.0, curvesA.Count + curvesB.Count, out Mite.Core.Fabrication.LathProfile profile)) return;
        var netProfile = NetProfile(netIn, 0, 17, 18, 19, 23, 24);
        if (netProfile != null) { profile = netProfile.Value; upright = profile.Upright; width = profile.Width; thickness = profile.Thickness; }

        if (movePts.Count > 0 && toPts.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Move points need To targets (one per point).");
            return;
        }
        if (cables.Count > 0 && lengths.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Cables need target Lengths (at Fold = 1).");
            return;
        }
        if (movePts.Count == 0 && cables.Count == 0 && !flatten && deploy != 1)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "No driver (Move/To or Cables): the net is only settled onto the hinge constraints.");
        if (deploy == 2 && movePts.Count == 0 && cables.Count == 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Deploy 2 needs a driver: a Move / To point (the node to pull) or a Cable.");

        var surface = mesh != null ? MeshConvert.Load(mesh)?.Data : netIn?.Surface;
        var net = KineticsInput.BuildNet(this, curvesA, curvesB, surface, sampling, tolerance, segment);
        if (net == null) return;

        // drivers
        var fixedIdx = fixedPts.Select(p => net.NearestNode(MeshConvert.ToVec3d(p))).Distinct().ToList();
        var slideIdx = slidePts.Select(p => net.NearestNode(MeshConvert.ToVec3d(p))).Where(i => !fixedIdx.Contains(i)).Distinct().ToList();
        var driven = new List<int>();
        var targetEnd = new List<Vec3d>();
        for (int i = 0; i < movePts.Count; i++)
        {
            int n = net.NearestNode(MeshConvert.ToVec3d(movePts[i]));
            if (fixedIdx.Contains(n) || driven.Contains(n)) continue;
            driven.Add(n);
            targetEnd.Add(MeshConvert.ToVec3d(toPts[Math.Min(i, toPts.Count - 1)]));
        }
        var cablePairs = new List<(int p, int q)>();
        var cableRest = new List<double>();
        var cableEnd = new List<double>();
        for (int i = 0; i < cables.Count; i++)
        {
            int p = net.NearestNode(MeshConvert.ToVec3d(cables[i].From));
            int q = net.NearestNode(MeshConvert.ToVec3d(cables[i].To));
            if (p == q) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Cable {i} snaps to a single joint and is ignored."); continue; }
            cablePairs.Add((p, q));
            cableRest.Add((net.Nodes[p] - net.Nodes[q]).Length);
            cableEnd.Add(lengths[Math.Min(i, lengths.Count - 1)]);
        }
        double snap = 0;
        foreach (var p in fixedPts) snap = Math.Max(snap, (net.Nodes[net.NearestNode(MeshConvert.ToVec3d(p))] - MeshConvert.ToVec3d(p)).Length);
        foreach (var p in movePts) snap = Math.Max(snap, (net.Nodes[net.NearestNode(MeshConvert.ToVec3d(p))] - MeshConvert.ToVec3d(p)).Length);
        if (snap > 2 * net.Scale)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"A Fixed / Move point is {snap:G3} away from the nearest joint (mean joint spacing {net.Scale:G3}).");

        var restStart = driven.Select(n => net.Nodes[n]).ToArray();
        // the input net's own crossing angles: the deployment targets when Angle ≤ 0 (read before any press re-bases the net)
        var designAngles = deploy == 1 && deployAngle <= 0 ? net.RestCrossingAngles() : null;
        if (designAngles != null && net.IsFlat())
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Angle ≤ 0 opens every scissor to its crossing angle in the input net, and the input net is flat: nothing opens. Feed the curved design with Flatten = true (it is pressed flat first), or give the Angle.");
        var opt = new ScissorNet.Options
        {
            Fixed = fixedIdx,
            Sliding = slideIdx,
            SlideNormal = MeshConvert.ToVec3d(slideNormal),
            Driven = driven,
            TargetsAt = t => restStart.Select((s, i) => s + t * (targetEnd[i] - s)).ToArray(),
            Cables = cablePairs,
            CableLengthsAt = t => cableRest.Select((r, i) => r + t * (cableEnd[i] - r)).ToArray(),
            Fold = Math.Max(0, Math.Min(1, fold)),
            Steps = Math.Max(1, steps),
            Fairness = Math.Max(0, stiffness),
            MaxIterations = Math.Max(1, iterations),
            Cancel = Cancelled,
            Progress = Progress("folding", flatten ? 0.3 : 0, 1),
        };
        int anchor = fixedIdx.Count > 0 ? fixedIdx[0] : net.NearestNode(net.Nodes.Aggregate(Vec3d.Zero, (a, b) => a + b) / net.Nodes.Length);
        var normal = MeshConvert.ToVec3d(slideNormal);
        string deployLine = "";
        ScissorNet.Result? press = null;
        if (flatten)
        {
            var pressOpt = opt;
            if (deploy != 0)
            {
                // the press is a separate motion: its sliding planes must not carry into the deployment
                pressOpt = new ScissorNet.Options
                {
                    Fixed = fixedIdx, Sliding = slideIdx, SlideNormal = normal, Fold = 1.0, Steps = Math.Max(1, steps),
                    Fairness = Math.Max(0, stiffness), MaxIterations = Math.Max(1, iterations), Cancel = Cancelled,
                };
            }
            ReportProgress("pressing flat", 1, 100);
            net.PressFlat(pressOpt, anchor, normal);
            if (deploy == 0) { slideIdx = pressOpt.Sliding.ToList(); fixedIdx = pressOpt.Fixed.ToList(); }
            else
            {
                press = net.Solve(pressOpt);
                if (press.Cancelled) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Cancelled with Esc during the press."); return; }
                var flat = press.Last;
                deployLine = $"Pressed flat first: joint spacing drift {flat.LengthDrift:E1}, height left {flat.Nodes.Max(p => Math.Abs(Vec3d.Dot(p - net.Nodes[anchor], normal))):G3}; ";
                net = net.AtState(flat);
                restStart = driven.Select(n => net.Nodes[n]).ToArray();
                opt.TargetsAt = t => restStart.Select((s, i) => s + t * (targetEnd[i] - s)).ToArray();
            }
        }
        if (deploy != 0)
        {
            bool wasFlat = net.IsFlat();
            if (!wasFlat) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "The net is not flat (normals differ by more than 2°): it is deployed from its current state anyway.");
            if (wasFlat && kick == 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Kick 0 on a flat net: the flat state is a bifurcation and the solver cannot leave the plane (the deployment branches are tangent to the softest mode, which a Gauss–Newton step never excites). Set Kick to ±0.5 to deploy.");
            try
            {
                if (deploy == 1)
                {
                    (Vec3d[] positions, Vec3d[] normals, double stiffness)? mode;
                    if (designAngles != null)
                    {
                        var da = designAngles;
                        net = net.DeployFromFlat(opt, anchor, normal, v => da[v], kick, out mode);
                        var targets = opt.Scissors.Select(v => da[v]).Where(a => !double.IsNaN(a)).Select(a => a * 180 / Math.PI).ToList();
                        deployLine += $"Deploy 1: {opt.Scissors.Count} scissors opened to their design angles ({(targets.Count > 0 ? targets.Min() : 0):0.#}° … {(targets.Count > 0 ? targets.Max() : 0):0.#}°) on a table of {opt.Ground.Count} lath ends";
                    }
                    else
                    {
                        net = net.DeployFromFlat(opt, anchor, normal, deployAngle * Math.PI / 180, kick, out mode);
                        deployLine += $"Deploy 1: {opt.Scissors.Count} scissors opened to {deployAngle:0.#}° on a table of {opt.Ground.Count} lath ends";
                    }
                    if (mode.HasValue) deployLine += $", kicked {kick:G3} × spacing along the softest mode (stiffness {mode.Value.stiffness:E1})";
                }
                else
                {
                    net = net.OnTable(opt, normal, kick);
                    deployLine += $"Deploy 2: {driven.Count} moved joints and {cablePairs.Count} cables drive the net on a table of {opt.Ground.Count} lath ends";
                    if (net.LastMode.HasValue || kick != 0) deployLine += $", kicked {kick:G3} × spacing along the softest mode";
                }
            }
            catch (ArgumentException ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message); return; }
            slideIdx = opt.Sliding.ToList(); fixedIdx = opt.Fixed.ToList();
        }
        var result = net.Solve(opt);
        if (result.Cancelled) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Cancelled with Esc, the motion is partial.");

        var last = result.Last;
        var polys = net.LathPolylines(last);
        var outA = new List<Curve>(); var outB = new List<Curve>();
        for (int l = 0; l < polys.Count; l++) (l < net.CountA ? outA : outB).Add(MeshConvert.ToPolylineCurve(polys[l]));
        var joints = new List<Point3d>(); var normals = new List<Vector3d>(); var angles = new List<double>();
        var ang = net.CrossingAngles(last);
        for (int v = 0; v < net.Nodes.Length; v++)
        {
            if (!net.IsJoint[v]) continue;
            joints.Add(MeshConvert.ToRhinoPoint(last.Nodes[v]));
            normals.Add(MeshConvert.ToRhinoVector(last.Normals[v]));
            angles.Add(ang[v]);
        }

        var statesA = new DataTree<Curve>(); var statesB = new DataTree<Curve>();
        var folds = new List<double>(); var drift = new List<double>(); var dev = new List<double>(); var miss = new List<double>();
        for (int k = 0; k < result.States.Count; k++)
        {
            var st = result.States[k];
            var path = BranchPath(DA, k);
            var ps = net.LathPolylines(st);
            for (int l = 0; l < ps.Count; l++) (l < net.CountA ? statesA : statesB).Add(MeshConvert.ToPolylineCurve(ps[l]), path);
            folds.Add(st.Fold); drift.Add(st.LengthDrift); dev.Add(st.AsymptoticDeviation); miss.Add(Math.Max(st.TargetMiss, Math.Max(st.CableMiss, st.SlideMiss)));
        }

        // elastic reading of every state
        double toMeters = ModelToMeters();
        var strainOpt = new KineticStrain.Options { Profile = profile, MaxStrain = maxStrain, YoungsModulus = modulus, Density = Math.Max(0, density), UnitScale = toMeters };
        var elastic = KineticStrain.Analyze(net, result, strainOpt);
        var utilPerState = elastic.Select(e => e.MaxUtilization).ToList();
        var energyPerState = elastic.Select(e => e.Total).ToList();
        var lathUtil = new DataTree<double>();
        for (int k = 0; k < elastic.Count; k++) lathUtil.AddRange(elastic[k].LathUtilization, BranchPath(DA, k));
        int natural = KineticStrain.NaturalState(elastic);
        double naturalFold = natural >= 0 ? result.States[natural].Fold : double.NaN;
        bool buildable = elastic.All(e => e.Buildable);
        var envelope = KineticStrain.Envelope(elastic);
        if (!buildable)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Laths exceed MaxStrain during the motion: peak utilization {utilPerState.Max():0.00} at Fold {result.States[utilPerState.IndexOf(utilPerState.Max())].Fold:0.##}.");

        if (deploy != 0 && last.GroundMiss > 0.01 * net.Scale)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Lath ends sink {last.GroundMiss:G3} into the table: the drivers push the net through it.");
        int notConverged = result.States.Count(s => !s.Converged);
        if (notConverged > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"{notConverged} state(s) stopped at the iteration cap: raise Iterations or Steps, or lower Stiffness.");
        if (last.LengthDrift > 0.01)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Joint spacing drifts by {last.LengthDrift:P1} at Fold {last.Fold:0.##}: the drivers ask for more than the mechanism allows.");

        var sb = new StringBuilder();
        sb.AppendLine($"Net Kinetics: {net.Laths.Count} laths ({net.CountA} A + {net.Laths.Count - net.CountA} B), {net.IsJoint.Count(j => j)} scissor joints, {net.Nodes.Length} nodes, mean joint spacing {net.Scale:G4}");
        sb.AppendLine($"Drivers: {fixedIdx.Count} fixed, {slideIdx.Count} sliding, {driven.Count} moved, {cablePairs.Count} cables; {result.States.Count} states to Fold {last.Fold:0.##}, stiffness {stiffness:G3}");
        sb.AppendLine($"Rest state settled: joints moved up to {result.States[0].Nodes.Zip(net.Nodes, (a, b) => (a - b).Length).Max():G3}, deviation {result.States[0].AsymptoticDeviation:0.###}° from asymptotic");
        sb.AppendLine($"At Fold {last.Fold:0.##}: joint spacing drift {last.LengthDrift:E2}, asymptotic deviation {last.AsymptoticDeviation:0.###}°, driver miss {Math.Max(last.TargetMiss, last.CableMiss):G3}, {last.Iterations} iterations{(last.Converged ? "" : " (cap)")}");
        sb.AppendLine($"Solver: {result.Unknowns} unknowns in 3 × 3 blocks, {result.Factorizations} factorizations over the motion, fill {result.FactorBlocks} blocks, pattern analysed {result.PatternAnalyses}×, {result.SolverMs} ms");
        sb.AppendLine($"Scissor angles at Fold {last.Fold:0.##}: {angles.Where(a => !double.IsNaN(a)).DefaultIfEmpty(double.NaN).Min():0.#}° … {angles.Where(a => !double.IsNaN(a)).DefaultIfEmpty(double.NaN).Max():0.#}°");
        if (deploy != 0)
        {
            sb.AppendLine(deployLine);
            double rise = last.Nodes.Max(p => Vec3d.Dot(p, normal)) - last.Nodes.Min(p => Vec3d.Dot(p, normal));
            sb.AppendLine($"Deployed at Fold {last.Fold:0.##}: rise {rise:G4} above the table, {last.GroundContacts} of {opt.Ground.Count} lath ends on it, sunk {last.GroundMiss:E1}" +
                (deploy == 1 ? $", scissors within {last.ScissorMiss:0.#}° of {(designAngles != null ? "their design angles" : $"{deployAngle:0.#}°")}" : $", driver miss {Math.Max(last.TargetMiss, last.CableMiss):G3}"));
            if (deploy == 2 && driven.Count > 0 && last.TargetMiss > 0.25 * targetEnd.Zip(restStart, (a, b) => (a - b).Length).DefaultIfEmpty(0).Max())
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"The pull is resisted: the moved joint misses its target by {last.TargetMiss:G3}. A flat asymptotic grid deploys along its softest mode, which does not move a singular node relative to the lath ends — open the scissors (Deploy 1) or lift the lath ends (Flatten read backwards) instead.");
        }
        var eLast = elastic[elastic.Count - 1];
        sb.AppendLine($"Elastic ({(profile.Kind == Mite.Core.Fabrication.SectionKind.Rectangle ? (upright ? "upright" : "flat") + $" {width:G3} × {thickness:G3}" : profile.Kind.ToString().ToLowerInvariant())}, E {modulus / 1e9:G3} GPa, limit {maxStrain:P1}): utilization {utilPerState[0]:0.00} at rest → peak {utilPerState.Max():0.00} at Fold {result.States[utilPerState.IndexOf(utilPerState.Max())].Fold:0.##}; strain energy {elastic[0].StrainEnergy:G4} J → {eLast.StrainEnergy:G4} J (bending {eLast.BendingEnergy:G3}, twist {eLast.TwistEnergy:G3}){(density > 0 ? $", self-weight potential {eLast.Potential:G4} J" : "")}");
        if (envelope.GoverningLath >= 0)
        {
            int wl = envelope.GoverningLath;
            string lathName = wl < net.CountA ? $"A{wl}" : $"B{wl - net.CountA}";
            string source = envelope.LathSource[wl] switch
            {
                KineticStrain.StrainSource.NormalCurvature => "bending from kn",
                KineticStrain.StrainSource.GeodesicCurvature => "bending from kg",
                KineticStrain.StrainSource.Twist => "twist",
                _ => "unstrained",
            };
            sb.AppendLine($"Through the motion: {envelope.BuildableLaths} of {net.Laths.Count} laths stay within MaxStrain in every state; worst lath {lathName} (index {wl} in LathUtil) at {envelope.MaxUtilization:0.00}, Fold {result.States[envelope.GoverningState].Fold:0.##}, {source}");
        }
        sb.Append(natural >= 0 ? $"Natural state (least energy along the motion): Fold {naturalFold:0.##}{(natural == 0 || natural == elastic.Count - 1 ? " (at the end of the range: the grid wants to go further)" : "")}" : "Natural state: none — the energy is flat along the motion (rigid-body mechanism)");

        DA.SetDataList(0, outA);
        DA.SetDataList(1, outB);
        DA.SetDataList(2, joints);
        DA.SetDataList(3, normals);
        DA.SetDataList(4, angles);
        DA.SetDataTree(5, statesA);
        DA.SetDataTree(6, statesB);
        DA.SetDataList(7, folds);
        DA.SetDataList(8, drift);
        DA.SetDataList(9, dev);
        DA.SetDataList(10, miss);
        DA.SetData(11, sb.ToString());
        DA.SetDataList(12, utilPerState);
        DA.SetDataTree(13, lathUtil);
        DA.SetDataList(14, energyPerState);
        DA.SetData(15, naturalFold);
        DA.SetData(16, buildable);
        var polysA = polys.Take(net.CountA).ToList();
        var polysB = polys.Skip(net.CountA).ToList();
        DA.SetData(17, new MiteNetGoo(new LathNet(null, polysA, polysB, NetKind.Kinetic, netIn?.Spacing ?? 0,
            netProfile == null ? profile : netIn!.ProfileA, netProfile == null ? profile : netIn!.ProfileOf(netIn.CountA))));
        DA.SetData(18, new KineticMotionGoo(new KineticMotion(net, result.States, strainOpt)));
    }
}
