using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;

namespace Mite.Grasshopper.Components;

public class LathSweepComponent : MiteComponent
{
    public LathSweepComponent()
        : base("Lath Sweep", "Sweep",
            "Extrudes curves lying on a mesh into solid laths with a rectangular profile. " +
            "The profile rides in the surface frame: flat laths hug the surface (geodesic " +
            "gridshells), upright laths stand on edge (asymptotic gridshells). " +
            "Outputs stay index-aligned with the input curves (null where a curve could not be swept).",
            "Fabrication", "LathSweep") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDEC");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Reference surface the curves lie on", GH_ParamAccess.item);
        pManager.AddCurveParameter("Curves", "C", "Lath centerlines on the surface", GH_ParamAccess.list);
        pManager.AddNumberParameter("Width", "W", "Profile width across the curve (default 0.1)", GH_ParamAccess.item, 0.1);
        pManager.AddNumberParameter("Thickness", "T", "Profile thickness through the strip (default 0.01)", GH_ParamAccess.item, 0.01);
        pManager.AddBooleanParameter("Upright", "U", "False: strip lies flat on the surface. True: strip stands upright on edge", GH_ParamAccess.item, false);
        pManager.AddNumberParameter("Offset", "O", "Gap between the surface and the nearest strip face (default 0)", GH_ParamAccess.item, 0.0);
        pManager.AddNumberParameter("Sampling", "S", "Chord deviation for curve sampling (0 = automatic from the mesh edge length)", GH_ParamAccess.item, 0.0);
        pManager.AddIntegerParameter("Shape", "Sh", "Section shape applied to every curve: 0 = rectangle W × T, 1 = round bar of diameter W, 2 = custom closed planar Section curve (its plane X = across / Y = surface normal; Upright swaps them)", GH_ParamAccess.item, 0);
        pManager.AddCurveParameter("Section", "Sc", "Closed planar section curve for Shape = 2 (drawn around the origin of its own plane, any units of the model)", GH_ParamAccess.item);
        pManager[8].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddMeshParameter("Laths", "L", "Swept lath solids as closed meshes, one per input curve", GH_ParamAccess.list);
        pManager.AddCurveParameter("Centerlines", "C", "Strip centerline per lath (lifted by the offset and half the depth)", GH_ParamAccess.list);
        pManager.AddPlaneParameter("Frames", "F", "Start frame per lath (X along the lath, Z along the surface normal)", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var input = LoadMesh(DA, 0);
        if (input == null) return;
        var curves = new List<Curve>();
        if (!DA.GetDataList(1, curves)) return;
        double width = 0.1, thickness = 0.01, offset = 0.0, sampling = 0.0;
        bool upright = false;
        DA.GetData(2, ref width);
        DA.GetData(3, ref thickness);
        DA.GetData(4, ref upright);
        DA.GetData(5, ref offset);
        DA.GetData(6, ref sampling);
        int shape = 0;
        DA.GetData(7, ref shape);
        Curve? section = null;
        DA.GetData(8, ref section);

        if (width <= 0 || thickness <= 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Width and Thickness must be positive.");
            return;
        }

        var proj = new MeshProjection(input.Data);
        double chord = ResolveSampling(sampling, proj);
        LathProfile profile;
        switch (shape)
        {
            case 1:
                profile = LathProfile.Round(width, 24, offset);
                if (upright) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Round sections have no upright/flat distinction; Upright is ignored.");
                break;
            case 2:
                if (section == null || !section.IsClosed || !section.TryGetPlane(out Plane sectionPlane))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Shape 2 needs a closed planar Section curve.");
                    return;
                }
                var poly = section.ToPolyline(0, 0, 0.05, 0.0, 0.0, Math.Max(1e-6, 0.01 * section.GetLength()), 0.0, 0.0, true)?.ToPolyline();
                if (poly == null || poly.Count < 4)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Section curve could not be converted to a polygon.");
                    return;
                }
                var pts2d = new List<(double, double)>();
                foreach (var pt in poly)
                {
                    sectionPlane.ClosestParameter(pt, out double sx, out double sy);
                    pts2d.Add((sx, sy));
                }
                try { profile = LathProfile.Custom(pts2d, upright, offset); }
                catch (ArgumentException ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message); return; }
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Custom section {profile.Width:G4} × {profile.Thickness:G4} (plane X × plane Y) applied to all {curves.Count} curves.");
                break;
            default:
                profile = new LathProfile(width, thickness, upright, offset);
                break;
        }

        var laths = new List<Mesh?>();
        var centerlines = new List<Curve?>();
        var frames = new List<Plane?>();
        int failed = 0;
        foreach (var curve in curves)
        {
            if (Cancelled()) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Cancelled with Esc, output is partial."); break; }
            var line = curve == null ? null : CurveSample.ToPolyline(curve, chord);
            var result = line == null ? null : StripSweep.Sweep(proj, line, profile);
            if (result.HasValue)
            {
                var r = result.Value;
                laths.Add(MeshConvert.ToRhinoMesh(r.Mesh));
                var centers = r.Centers;
                if (line!.Length > 3 && (line[0] - line[line.Length - 1]).LengthSquared < 1e-18)
                {
                    // Closed lath: the sweep drops the duplicate seam point, restore it
                    Array.Resize(ref centers, centers.Length + 1);
                    centers[centers.Length - 1] = centers[0];
                }
                centerlines.Add(MeshConvert.ToPolylineCurve(centers));
                frames.Add(new Plane(MeshConvert.ToRhinoPoint(r.Centers[0]),
                    MeshConvert.ToRhinoVector(r.Tangents[0]), MeshConvert.ToRhinoVector(r.Sideways[0])));
            }
            else
            {
                failed++;
                laths.Add(null);
                centerlines.Add(null);
                frames.Add(null);
            }
        }

        if (failed > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"{failed} curve(s) could not be swept (null, degenerate or too short); null placeholders keep the indices aligned.");

        DA.SetDataList(0, laths);
        DA.SetDataList(1, centerlines);
        DA.SetDataList(2, frames);
    }
}
