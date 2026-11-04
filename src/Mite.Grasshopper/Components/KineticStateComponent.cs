using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Mite.Core.Gridshells;
using Mite.Core.Kinetics;

namespace Mite.Grasshopper.Components;

/// <summary>
/// One moment of a solved motion: the net at a chosen fold, with its joints,
/// scissor angles and elastic reading, ready to draw, bake or pass on.
/// </summary>
public class KineticStateComponent : MiteComponent
{
    public KineticStateComponent()
        : base("Kinetic State", "State",
            "Reads one moment of a motion solved by Net Kinetics: wire its Motion output here and slide Fold. You get the laths, joints, normals and " +
            "scissor angles at that fold, each lath's strain utilization and the strain energy, and a Mite Net to pass on (Lath Sweep, Net Joints, Flat Kit) — " +
            "without solving the motion again. At a solved state the reading is exact; between two states the net is interpolated, and Drift tells how far " +
            "the joint spacing is then from exact (it falls with the square of the step: raise Steps on Net Kinetics when it matters). " +
            "Animate the Fold slider (right-click › Animate) to record the motion.",
            "Kinetics", "KineticState") { }

    public override Guid ComponentGuid => new("66D5EB75-6902-4B60-87F0-0B1644387652");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddGenericParameter("Motion", "Mo", "A solved motion: Net Kinetics' Motion output", GH_ParamAccess.item);
        pManager.AddNumberParameter("Fold", "Fo", "Where in the motion to read: 0 = the rest state, 1 = the end of a full run (clamped to the solved range)", GH_ParamAccess.item, 1.0);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("A", "A", "Family A laths at Fold (polylines through the nodes)", GH_ParamAccess.list);
        pManager.AddCurveParameter("B", "B", "Family B laths at Fold", GH_ParamAccess.list);
        pManager.AddPointParameter("Joints", "J", "Joint positions at Fold", GH_ParamAccess.list);
        pManager.AddVectorParameter("Normals", "N", "Surface normal at each joint at Fold", GH_ParamAccess.list);
        pManager.AddNumberParameter("Angles", "An", "Scissor angle (degrees) between the two laths at each joint", GH_ParamAccess.list);
        pManager.AddNumberParameter("LathUtil", "Ul", "Each lath's peak strain utilization at Fold (A laths first, then B) — wire into Lath Preview", GH_ParamAccess.list);
        pManager.AddNumberParameter("Utilization", "U", "Peak strain utilization over all laths at Fold (1 = MaxStrain)", GH_ParamAccess.item);
        pManager.AddNumberParameter("Energy", "En", "Strain energy at Fold in joules (plus the self-weight potential when Density is set)", GH_ParamAccess.item);
        pManager.AddNumberParameter("Drift", "Dr", "Largest relative change of a joint spacing at Fold: ≈ 0 at a solved state, the chord error between states", GH_ParamAccess.item);
        RegisterNetOutput(pManager, "The net at Fold (kind Kinetic, the section the motion is read with, no surface)"); // 9
        pManager.AddTextParameter("Report", "R", "Where the reading lies in the motion and what it shows", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        IGH_Goo? goo = null;
        double fold = 1.0;
        if (!DA.GetData(0, ref goo) || goo == null) return;
        DA.GetData(1, ref fold);
        if (goo is not KineticMotionGoo mg || mg.Value == null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Motion expects Net Kinetics' Motion output, got {goo.TypeName}.");
            return;
        }
        var motion = mg.Value;
        var net = motion.Net;
        if (fold < motion.FirstFold - 1e-12 || fold > motion.LastFold + 1e-12)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Fold {fold:0.###} is outside the solved run ({motion.FirstFold:0.##} … {motion.LastFold:0.##}): read at the nearest end.");
        var st = motion.At(fold, out int bracket, out double t);
        double drift = motion.SpacingDrift(st);

        var polys = net.LathPolylines(st);
        var outA = new List<Curve>(); var outB = new List<Curve>();
        for (int l = 0; l < polys.Count; l++) (l < net.CountA ? outA : outB).Add(MeshConvert.ToPolylineCurve(polys[l]));
        var joints = new List<Point3d>(); var normals = new List<Vector3d>(); var angles = new List<double>();
        var ang = net.CrossingAngles(st);
        for (int v = 0; v < net.Nodes.Length; v++)
        {
            if (!net.IsJoint[v]) continue;
            joints.Add(MeshConvert.ToRhinoPoint(st.Nodes[v]));
            normals.Add(MeshConvert.ToRhinoVector(st.Normals[v]));
            angles.Add(ang[v]);
        }

        var sb = new StringBuilder();
        sb.Append($"Kinetic State at Fold {st.Fold:0.###}: ");
        sb.AppendLine(t == 0
            ? $"solved state {bracket} of {motion.States.Count} (exact)"
            : $"between solved states {bracket} and {bracket + 1} of {motion.States.Count} ({t:P0} of the step), joint spacing drift {drift:E1}");
        var finite = angles.Where(a => !double.IsNaN(a)).ToList();
        if (finite.Count > 0) sb.AppendLine($"{joints.Count} joints, scissor angles {finite.Min():0.#}° … {finite.Max():0.#}°");

        DA.SetDataList(0, outA);
        DA.SetDataList(1, outB);
        DA.SetDataList(2, joints);
        DA.SetDataList(3, normals);
        DA.SetDataList(4, angles);
        Mite.Core.Fabrication.LathProfile? profile = null;
        if (motion.Strain != null)
        {
            var e = KineticStrain.Analyze(net, st, motion.Strain);
            DA.SetDataList(5, e.LathUtilization);
            DA.SetData(6, e.MaxUtilization);
            DA.SetData(7, e.Total);
            profile = motion.Strain.Profile;
            sb.AppendLine($"Peak strain utilization {e.MaxUtilization:0.00}{(e.Buildable ? "" : " — above MaxStrain")}, strain energy {e.StrainEnergy:G4} J");
            if (!e.Buildable) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Laths exceed MaxStrain at Fold {st.Fold:0.###}: peak utilization {e.MaxUtilization:0.00}.");
        }
        else sb.AppendLine("The motion carries no section: no elastic reading.");
        DA.SetData(8, drift);
        if (t > 0 && drift > 0.01)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Between states the joint spacing drifts {drift:P1}: raise Steps on Net Kinetics for an exact reading here.");
        var polysA = polys.Take(net.CountA).ToList();
        var polysB = polys.Skip(net.CountA).ToList();
        DA.SetData(9, new MiteNetGoo(new LathNet(null, polysA, polysB, NetKind.Kinetic, net.Scale, profile, profile)));
        DA.SetData(10, sb.ToString().TrimEnd());
    }
}
