using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.Kinetics;

namespace Mite.Grasshopper.Components;

/// <summary>How a scissor net is held: fixed and sliding points as one wire.</summary>
public class KineticSupportsComponent : MiteComponent
{
    public KineticSupportsComponent()
        : base("Kinetic Supports", "Supports",
            "How a scissor net is held, as one wire: Fixed points hold their nearest joints, Slide points keep theirs on a plane of normal SlideNormal, free to slide on it. " +
            "Wire the same Supports into Mechanism Modes (which motions they leave free) and Net Kinetics (the motion they allow) — the points snap to each net's nearest joints. " +
            "Chain several setups through Setup to combine them.",
            "Kinetics", "KineticSupports") { }

    public override Guid ComponentGuid => new("778A9EB9-78F8-4593-B6C6-E0F663639E45");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddPointParameter("Fixed", "F", "Support points: the nearest joints are held in place", GH_ParamAccess.list);
        pManager.AddPointParameter("Slide", "Sl", "Points whose nearest joints stay on a plane of normal SlideNormal, free to slide on it", GH_ParamAccess.list);
        pManager.AddVectorParameter("SlideNormal", "Sn", "Normal of the sliding plane (default world Z)", GH_ParamAccess.item, Vector3d.ZAxis);
        pManager.AddGenericParameter("Setup", "Su", "Another Kinetic Supports or Kinetic Drivers output to combine with (optional)", GH_ParamAccess.item);
        pManager[0].Optional = true;
        pManager[1].Optional = true;
        pManager[3].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddGenericParameter("Supports", "Su", "The supports as one wire: into Net Kinetics' or Mechanism Modes' Supports input", GH_ParamAccess.item);
        pManager.AddPointParameter("Points", "P", "Every support point (fixed, then sliding), for preview", GH_ParamAccess.list);
        pManager.AddTextParameter("Report", "R", "What the setup holds", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var fixedPts = new List<Point3d>(); var slidePts = new List<Point3d>(); var normal = Vector3d.ZAxis;
        DA.GetDataList(0, fixedPts);
        DA.GetDataList(1, slidePts);
        DA.GetData(2, ref normal);
        if (!KineticSetupGoo.TryRead(this, DA, 3, out var chained)) return;
        if (normal.IsTiny()) { AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "SlideNormal is zero: using world Z."); normal = Vector3d.ZAxis; }
        var own = new KineticSetup(fixedPts.Select(MeshConvert.ToVec3d), slidePts.Select(MeshConvert.ToVec3d),
            slidePts.Count > 0 ? MeshConvert.ToVec3d(normal) : (Mite.Core.Geometry.Vec3d?)null);
        var setup = KineticSetup.Merge(own, chained);
        if (!setup.HasSupports && !setup.HasDrivers)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "No points: a net with no supports floats (its rigid-body motions are removed, nothing is held).");
        KineticSetupGoo.Report(this, setup);
        DA.SetData(0, new KineticSetupGoo(setup));
        DA.SetDataList(1, setup.Fixed.Concat(setup.Slide).Select(MeshConvert.ToRhinoPoint));
        DA.SetData(2, KineticSetupGoo.Describe(setup) + (setup.Slide.Count > 0 && setup.SlideNormal.HasValue ? $"; sliding plane normal ({setup.SlideNormal.Value.X:0.###}, {setup.SlideNormal.Value.Y:0.###}, {setup.SlideNormal.Value.Z:0.###})" : ""));
    }
}
