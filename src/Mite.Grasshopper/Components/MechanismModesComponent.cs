using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Rhino.Geometry;
using Mite.Core.Geometry;
using Mite.Core.Kinetics;

namespace Mite.Grasshopper.Components;

/// <summary>
/// How a scissor-jointed net can move before any driver is set: its softest
/// motions, which of them are mechanisms, and each one drawn displaced.
/// </summary>
public class MechanismModesComponent : MiteComponent
{
    public MechanismModesComponent()
        : base("Mechanism Modes", "Modes",
            "How a scissor-jointed net can move, before you set any driver. The crossings of A and B become scissor hinges (as in Net Kinetics); " +
            "the net's softest motions are found with their first-order cost, and a motion that costs nothing — the hinges, the joint spacing and the " +
            "asymptotic condition all allow it — is a mechanism. Supports and singularities decide how many there are (Schling & Schikore 2023): " +
            "a hyperboloid grid has exactly one, holding a whole ring of it leaves none, a flat grid of straight rods shears in its plane and twists into a hypar. " +
            "Each mode is drawn displaced so you can see which way the net wants to go; feed a mechanism's direction to Net Kinetics as the motion to drive.",
            "Kinetics", "MechanismModes") { }

    public override Guid ComponentGuid => new("6582956E-9B7F-4633-9C1F-DD82BD7C3524");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("CurvesA", "A", "First lath family", GH_ParamAccess.list);
        pManager.AddCurveParameter("CurvesB", "B", "Second lath family", GH_ParamAccess.list);
        pManager.AddPointParameter("Fixed", "F", "Support points: the nearest joints are held (each one removes motions)", GH_ParamAccess.list);
        pManager.AddPointParameter("Slide", "Sl", "Points whose nearest joints stay on a plane of normal SlideNormal, free to slide on it", GH_ParamAccess.list);
        pManager.AddVectorParameter("SlideNormal", "Sn", "Normal of the sliding plane (default world Z)", GH_ParamAccess.item, Vector3d.ZAxis);
        pManager.AddIntegerParameter("Count", "N", "Number of modes, softest first (default 3)", GH_ParamAccess.item, 3);
        pManager.AddNumberParameter("Amplitude", "Am", "How far each mode is drawn: the largest node displacement in model units (0 = one mean joint spacing)", GH_ParamAccess.item, 0.0);
        pManager.AddNumberParameter("Stiffness", "K", "Bending stiffness of the laths relative to the hinges, as in Net Kinetics (0 = laths kink freely at every node)", GH_ParamAccess.item, 1.0);
        pManager.AddNumberParameter("Segment", "Sg", "Longest lath piece between nodes (0 = joints only), as in Net Kinetics", GH_ParamAccess.item, 0.0);
        pManager.AddNumberParameter("Tolerance", "X", "Maximum gap accepted as a crossing (0 = automatic)", GH_ParamAccess.item, 0.0);
        pManager.AddNumberParameter("Sampling", "S", "Chord deviation for curve sampling (0 = automatic)", GH_ParamAccess.item, 0.0);
        pManager.AddMeshParameter("Mesh", "M", "Surface whose normals start the joint normals (optional; default from the crossing laths)", GH_ParamAccess.item);
        RegisterNetInput(pManager, "Its laths are the scissor grid and its surface starts the joint normals (unless Mesh is wired)"); // 12
        pManager.AddGenericParameter("Supports", "Su", "Kinetic Supports output: its fixed and sliding points join Fixed and Slide (its plane normal is used when it has sliding points)", GH_ParamAccess.item); // 13
        pManager[0].Optional = true;
        pManager[1].Optional = true;
        pManager[2].Optional = true;
        pManager[3].Optional = true;
        pManager[11].Optional = true;
        pManager[13].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("ModesA", "MA", "Family A laths moved along each mode (one branch per mode, softest first)", GH_ParamAccess.tree);
        pManager.AddCurveParameter("ModesB", "MB", "Family B laths moved along each mode", GH_ParamAccess.tree);
        pManager.AddPointParameter("Joints", "J", "The scissor joints at rest", GH_ParamAccess.list);
        pManager.AddVectorParameter("Motion", "Mo", "Per mode (branch): each joint's displacement, scaled to Amplitude", GH_ParamAccess.tree);
        pManager.AddNumberParameter("Stiffness", "K", "Per mode: first-order cost relative to the net's mean stiffness (≈ 0 for a mechanism)", GH_ParamAccess.list);
        pManager.AddBooleanParameter("Mechanism", "Me", "Per mode: true when it costs nothing — the net moves this way freely", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Mechanisms", "Nm", "How many of the computed modes are mechanisms (raise Count when every mode is one)", GH_ParamAccess.item);
        pManager.AddTextParameter("Report", "R", "Summary: the modes, their cost, how much of each moves out of the surface", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var curvesA = new List<Curve>(); var curvesB = new List<Curve>();
        var fixedPts = new List<Point3d>(); var slidePts = new List<Point3d>();
        var slideNormal = Vector3d.ZAxis;
        int count = 3; double amplitude = 0, stiffness = 1, segment = 0, tolerance = 0, sampling = 0;
        Mesh? mesh = null;
        var netIn = GetNet(DA, 12);
        if (!ReadCurvesOrNet(DA, 0, netIn, curvesA, 0)) return;
        if (!ReadCurvesOrNet(DA, 1, netIn, curvesB, 1)) return;
        DA.GetDataList(2, fixedPts);
        DA.GetDataList(3, slidePts);
        DA.GetData(4, ref slideNormal);
        if (slideNormal.IsTiny()) slideNormal = Vector3d.ZAxis;
        DA.GetData(5, ref count);
        DA.GetData(6, ref amplitude);
        DA.GetData(7, ref stiffness);
        DA.GetData(8, ref segment);
        DA.GetData(9, ref tolerance);
        DA.GetData(10, ref sampling);
        DA.GetData(11, ref mesh);
        if (!KineticSetupGoo.TryRead(this, DA, 13, out var supportsIn)) return;
        if (supportsIn != null)
        {
            KineticSetupGoo.Report(this, supportsIn);
            fixedPts.AddRange(supportsIn.Fixed.Select(MeshConvert.ToRhinoPoint));
            slidePts.AddRange(supportsIn.Slide.Select(MeshConvert.ToRhinoPoint));
            if (supportsIn.Slide.Count > 0 && supportsIn.SlideNormal.HasValue) slideNormal = MeshConvert.ToRhinoVector(supportsIn.SlideNormal.Value);
            if (supportsIn.HasDrivers) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "The setup's drivers are not used here: Mechanism Modes shows the motions available before driving.");
        }
        if (count < 1) { AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Count {count} computes nothing: using 1."); count = 1; }
        if (count > 24) { AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Count {count} capped at 24 (each mode costs a full inverse iteration)."); count = 24; }

        var surface = mesh != null ? MeshConvert.Load(mesh)?.Data : netIn?.Surface;
        var net = KineticsInput.BuildNet(this, curvesA, curvesB, surface, sampling, tolerance, segment);
        if (net == null) return;

        var fixedIdx = fixedPts.Select(p => net.NearestNode(MeshConvert.ToVec3d(p))).Distinct().ToList();
        var slideIdx = slidePts.Select(p => net.NearestNode(MeshConvert.ToVec3d(p))).Where(i => !fixedIdx.Contains(i)).Distinct().ToList();
        var supports = new ScissorNet.Options
        {
            Fixed = fixedIdx, Sliding = slideIdx, SlideNormal = MeshConvert.ToVec3d(slideNormal), Fairness = Math.Max(0, stiffness),
        };
        ReportProgress("modes", 0, count);
        var res = MechanismModes.Compute(net, supports, new MechanismModes.Options { Count = count });
        if (res.Modes.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The mode solve failed (the stiffness matrix could not be factored): check that the families cross and that the net has free joints.");
            return;
        }
        double amp = amplitude > 0 ? amplitude : net.Scale;

        var modesA = new DataTree<Curve>(); var modesB = new DataTree<Curve>(); var motion = new DataTree<Vector3d>();
        for (int k = 0; k < res.Modes.Count; k++)
        {
            var path = BranchPath(DA, k);
            var st = MechanismModes.Displaced(net, res.Modes[k], amp);
            var polys = net.LathPolylines(st);
            for (int l = 0; l < polys.Count; l++) (l < net.CountA ? modesA : modesB).Add(MeshConvert.ToPolylineCurve(polys[l]), path);
            for (int v = 0; v < net.Nodes.Length; v++)
                if (net.IsJoint[v]) motion.Add(MeshConvert.ToRhinoVector(amp * res.Modes[k].Displacements[v]), path);
        }
        var joints = Enumerable.Range(0, net.Nodes.Length).Where(v => net.IsJoint[v]).Select(v => MeshConvert.ToRhinoPoint(net.Nodes[v])).ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"Mechanism Modes: {net.Laths.Count} laths ({net.CountA} A + {net.Laths.Count - net.CountA} B), {joints.Count} scissor joints, {fixedIdx.Count} held, {slideIdx.Count} sliding; mean joint spacing {net.Scale:G4}");
        for (int k = 0; k < res.Modes.Count; k++)
        {
            var m = res.Modes[k];
            sb.AppendLine($"mode {k}: {(m.IsMechanism ? "MECHANISM" : "stiff")} — cost {m.RelativeStiffness:E1} of the mean, {m.OutOfPlane:P0} of the motion out of the surface");
        }
        string verdict = res.Mechanisms == 0
            ? "The net does not move freely: every motion strains it (the supports lock it, or the laths' bending holds it). Release Fixed points or lower Stiffness to see what would move."
            : res.MoreMayExist
                ? $"All {res.Mechanisms} computed modes are mechanisms: there may be more — raise Count. Hold joints (Fixed) to remove the motions you do not want."
                : $"{res.Mechanisms} mechanism{(res.Mechanisms == 1 ? "" : "s")}: the net moves {(res.Mechanisms == 1 ? "one way" : $"{res.Mechanisms} independent ways")} for free; gap to the first stiff mode ×{res.Gap:E1}.";
        sb.Append(verdict);
        if (res.Mechanisms == 0) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, verdict);
        else if (res.MoreMayExist) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, verdict);

        DA.SetDataTree(0, modesA);
        DA.SetDataTree(1, modesB);
        DA.SetDataList(2, joints);
        DA.SetDataTree(3, motion);
        DA.SetDataList(4, res.Modes.Select(m => m.RelativeStiffness));
        DA.SetDataList(5, res.Modes.Select(m => m.IsMechanism));
        DA.SetData(6, res.Mechanisms);
        DA.SetData(7, sb.ToString());
    }
}
