// Mite Net: assembles, edits and inspects a gridshell net (roadmap B, "one
// wire"). Assemble → Model → Disassemble in one component, after the model
// objects of structural plug-ins (Karamba3D); the per-family profile covers
// the asymptotic–geodesic hybrid of Schling, Wan, Wang & D'Acunto 2023 (A
// upright, B flat). End kinds as LathNet.Ends: border / on a lath / free.
using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;

namespace Mite.Grasshopper.Components;

public class MiteNetComponent : MiteComponent
{
    public MiteNetComponent()
        : base("Mite Net", "Net",
            "Builds a Mite Net from a surface and two lath families, or edits one: set the lath profile (per family with UprightB), " +
            "replace a part, or join two nets (NetB's first family becomes family B — two geodesic webs, or an asymptotic family with geodesics). " +
            "Also takes the net apart and classifies every lath end: on the border, on another lath (T-junction) or free.",
            "Gridshells", "MiteNet") { }

    public override Guid ComponentGuid => new("3F6B2D84-9C1E-4A57-B0E3-8D2A61C7F5E9");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddParameter(new MiteNetParameter(), "Net", "Nt", "Net to start from (optional): every other input replaces that part of it", GH_ParamAccess.item);
        pManager.AddMeshParameter("Mesh", "M", "Reference surface", GH_ParamAccess.item);
        pManager.AddCurveParameter("CurvesA", "A", "Family A laths", GH_ParamAccess.list);
        pManager.AddCurveParameter("CurvesB", "B", "Family B laths", GH_ParamAccess.list);
        pManager.AddParameter(new MiteNetParameter(), "NetB", "Nb", "A second net whose family A becomes family B (e.g. a second Geodesic Net at another angle)", GH_ParamAccess.item);
        pManager.AddNumberParameter("Width", "W", "Lath profile width (default 0.1)", GH_ParamAccess.item, 0.1);
        pManager.AddNumberParameter("Thickness", "T", "Lath profile thickness (default 0.01)", GH_ParamAccess.item, 0.01);
        pManager.AddBooleanParameter("Upright", "U", "False: laths lie flat on the surface; true: they stand upright (asymptotic gridshells)", GH_ParamAccess.item, false);
        pManager.AddNumberParameter("Offset", "O", "Gap between the surface and the nearest lath face (default 0)", GH_ParamAccess.item, 0.0);
        RegisterSectionInputs(pManager); // 9 Shape, 10 Section
        pManager.AddBooleanParameter("UprightB", "Ub", "Orientation of family B when it differs from A (asymptotic–geodesic hybrid: A upright, B flat); unset = as Upright", GH_ParamAccess.item);
        pManager.AddNumberParameter("Sampling", "S", "Chord deviation for sampling wired curves (0 = automatic from the mesh edge length)", GH_ParamAccess.item, 0.0);
        pManager.AddNumberParameter("Tolerance", "X", "Distance within which a lath end counts as on the border or on another lath (0 = automatic: exact for traced nets, the sampling chord for wired curves)", GH_ParamAccess.item, 0.0);
        foreach (int i in new[] { 0, 1, 2, 3, 4, 11 }) pManager[i].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        RegisterNetOutput(pManager); // 0
        pManager.AddMeshParameter("Mesh", "M", "The net's surface", GH_ParamAccess.item);
        pManager.AddCurveParameter("CurvesA", "A", "Family A laths (polylines)", GH_ParamAccess.list);
        pManager.AddCurveParameter("CurvesB", "B", "Family B laths (polylines)", GH_ParamAccess.list);
        pManager.AddPointParameter("Joints", "J", "Crossings between the families (the joints)", GH_ParamAccess.list);
        pManager.AddPointParameter("BorderEnds", "Eb", "Lath ends on the surface border (supports)", GH_ParamAccess.list);
        pManager.AddPointParameter("LathEnds", "El", "Lath ends on another lath (T-junctions; orange in the preview)", GH_ParamAccess.list);
        pManager.AddPointParameter("FreeEnds", "Ef", "Lath ends that are neither (red in the preview)", GH_ParamAccess.list);
        pManager.AddTextParameter("Report", "R", "Summary: laths, lengths, joints, ends, profile", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var baseNet = GetNet(DA, 0);
        var netB = GetNet(DA, 4);

        // surface
        MeshData? surface = baseNet?.Surface;
        var mi = LoadMesh(DA, 1);
        if (mi != null) surface = mi.Data;
        if (surface == null && netB?.Surface != null) surface = netB.Surface;

        // chord for wired curves
        double sampling = 0, tolerance = 0;
        DA.GetData(12, ref sampling);
        DA.GetData(13, ref tolerance);
        var curvesA = new List<Curve>();
        var curvesB = new List<Curve>();
        bool wiredA = DA.GetDataList(2, curvesA) && curvesA.Count > 0;
        bool wiredB = DA.GetDataList(3, curvesB) && curvesB.Count > 0;
        double chord = sampling;
        if (chord <= 0 && (wiredA || wiredB))
        {
            if (surface != null) chord = ResolveSampling(0, new MeshProjection(surface));
            else
            {
                var box = BoundingBox.Empty;
                foreach (var c in curvesA.Concat(curvesB)) if (c != null) box.Union(c.GetBoundingBox(false));
                chord = box.IsValid ? Math.Max(box.Diagonal.Length / 500.0, 1e-9) : 0.01;
            }
        }

        var famA = wiredA ? CurveSample.ToPolylines(curvesA, chord)
            : baseNet != null ? baseNet.FamilyA.ToList() : new List<Vec3d[]>();
        var famB = wiredB ? CurveSample.ToPolylines(curvesB, chord)
            : netB != null ? netB.FamilyA.ToList() : baseNet != null ? baseNet.FamilyB.ToList() : new List<Vec3d[]>();
        if (wiredB && netB != null)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "CurvesB is wired: NetB is ignored.");

        var kind = wiredA || wiredB ? NetKind.Custom
            : baseNet != null && netB != null && baseNet.Kind != netB.Kind ? NetKind.Custom
            : baseNet?.Kind ?? netB?.Kind ?? NetKind.Custom;

        // profile: set from the inputs when any section input is wired or the net has none
        LathProfile? profileA = baseNet?.ProfileA, profileB = baseNet?.ProfileB;
        bool sectionWired = new[] { 5, 6, 7, 8, 9, 10, 11 }.Any(IsWired);
        if (sectionWired || profileA == null)
        {
            double width = 0.1, thickness = 0.01, offset = 0.0; bool upright = false; int shape = 0; Curve? section = null;
            DA.GetData(5, ref width);
            DA.GetData(6, ref thickness);
            DA.GetData(7, ref upright);
            DA.GetData(8, ref offset);
            DA.GetData(9, ref shape);
            DA.GetData(10, ref section);
            if (width <= 0 || thickness <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Width and Thickness must be positive.");
                return;
            }
            if (!SectionInput.TryBuild(this, shape, section, width, thickness, upright, offset, 1.0, famA.Count + famB.Count, out LathProfile pa)) return;
            profileA = pa;
            profileB = pa;
            bool uprightB = upright;
            if (DA.GetData(11, ref uprightB) && uprightB != upright)
            {
                if (!SectionInput.TryBuild(this, shape, section, width, thickness, uprightB, offset, 1.0, famB.Count, out LathProfile pb)) return;
                profileB = pb;
            }
        }

        var net = new LathNet(surface, famA, famB, kind, baseNet?.Spacing ?? netB?.Spacing ?? 0, profileA, profileB);
        if (net.LathCount == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No laths: wire a Net or curves.");
            return;
        }

        double endTol = tolerance > 0 ? tolerance : wiredA || wiredB ? Math.Max(chord, net.DefaultEndTolerance()) : 0.0;
        var ends = net.Ends(endTol);
        var border = ends.Where(e => e.Kind == LathEndKind.Border).Select(e => MeshConvert.ToRhinoPoint(e.Point)).ToList();
        var onLath = ends.Where(e => e.Kind == LathEndKind.OnLath).Select(e => MeshConvert.ToRhinoPoint(e.Point)).ToList();
        var free = ends.Where(e => e.Kind == LathEndKind.Free).Select(e => MeshConvert.ToRhinoPoint(e.Point)).ToList();
        if (surface == null)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "No surface: border ends cannot be told apart from free ends.");
        if (free.Count > 0 && surface != null)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"{free.Count} lath end(s) are neither on the border nor on another lath (red dots).");

        DA.SetData(0, new MiteNetGoo(net));
        DA.SetData(1, surface != null ? MeshConvert.ToRhinoMesh(surface) : null);
        DA.SetDataList(2, net.FamilyA.Select(MeshConvert.ToPolylineCurve));
        DA.SetDataList(3, net.FamilyB.Select(MeshConvert.ToPolylineCurve));
        DA.SetDataList(4, net.Joints().Select(x => MeshConvert.ToRhinoPoint(x.Point)));
        DA.SetDataList(5, border);
        DA.SetDataList(6, onLath);
        DA.SetDataList(7, free);
        DA.SetData(8, net.Summary(endTol));
    }
}
