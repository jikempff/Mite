using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.Kinetics;

namespace Mite.Grasshopper.Components;

/// <summary>What moves a scissor net: points moved to targets and cables, as one wire.</summary>
public class KineticDriversComponent : MiteComponent
{
    public KineticDriversComponent()
        : base("Kinetic Drivers", "Drivers",
            "What moves a scissor net, as one wire: Move points take their nearest joints to the To targets (paired by index, the last target repeats) and Cables " +
            "between two joints shorten or lengthen to Lengths — both reached at Fold = 1 on Net Kinetics. The points snap to the net's nearest joints. " +
            "Chain with Kinetic Supports through Setup, or wire both into Net Kinetics.",
            "Kinetics", "KineticDrivers") { }

    public override Guid ComponentGuid => new("CD02CAFF-7A6C-4A08-930B-938888BF101C");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddPointParameter("Move", "Mv", "Points whose nearest joints are moved (paired with To by index)", GH_ParamAccess.list);
        pManager.AddPointParameter("To", "To", "Target of each Move point at Fold = 1 (the last repeats)", GH_ParamAccess.list);
        pManager.AddLineParameter("Cables", "C", "Actuator cables: each line's ends snap to the nearest joints", GH_ParamAccess.list);
        pManager.AddNumberParameter("Lengths", "Le", "Length of each cable at Fold = 1 (the rest length is the line's; the last value repeats)", GH_ParamAccess.list);
        pManager.AddGenericParameter("Setup", "Su", "Another Kinetic Supports or Kinetic Drivers output to combine with (optional)", GH_ParamAccess.item);
        for (int i = 0; i < 5; i++) pManager[i].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddGenericParameter("Drivers", "Dr", "The drivers as one wire: into Net Kinetics' Drivers input", GH_ParamAccess.item);
        pManager.AddLineParameter("Paths", "Pa", "Each Move point to its target, for preview", GH_ParamAccess.list);
        pManager.AddTextParameter("Report", "R", "What the setup drives", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var move = new List<Point3d>(); var to = new List<Point3d>(); var cables = new List<Line>(); var lengths = new List<double>();
        DA.GetDataList(0, move);
        DA.GetDataList(1, to);
        DA.GetDataList(2, cables);
        DA.GetDataList(3, lengths);
        if (!KineticSetupGoo.TryRead(this, DA, 4, out var chained)) return;
        var own = new KineticSetup(move: move.Select(MeshConvert.ToVec3d), to: to.Select(MeshConvert.ToVec3d),
            cables: cables.Select(l => (MeshConvert.ToVec3d(l.From), MeshConvert.ToVec3d(l.To))), lengths: lengths);
        var setup = KineticSetup.Merge(own, chained);
        if (!setup.HasDrivers)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "No Move points or Cables: nothing drives the net (Net Kinetics only settles it).");
        KineticSetupGoo.Report(this, setup);
        var paths = new List<Line>();
        for (int i = 0; i < setup.Move.Count && setup.To.Count > 0; i++)
            paths.Add(new Line(MeshConvert.ToRhinoPoint(setup.Move[i]), MeshConvert.ToRhinoPoint(setup.To[Math.Min(i, setup.To.Count - 1)])));
        DA.SetData(0, new KineticSetupGoo(setup));
        DA.SetDataList(1, paths);
        DA.SetData(2, KineticSetupGoo.Describe(setup));
    }
}
