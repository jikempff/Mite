using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;

namespace Mite.Grasshopper.Components;

public class LathSweepComponent : GH_Component
{
    public LathSweepComponent()
        : base("Lath Sweep", "Sweep",
            "Extrudes curves lying on a mesh into solid laths with a rectangular profile. " +
            "The profile rides in the surface frame: flat laths hug the surface (geodesic " +
            "gridshells), upright laths stand on edge (asymptotic gridshells).",
            "Mite", "Fabrication") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDEC");

    protected override Bitmap Icon =>
        new Bitmap(Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Mite.Grasshopper.Resources.LathSweep.png")!);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Reference surface the curves lie on", GH_ParamAccess.item);
        pManager.AddCurveParameter("Curves", "C", "Lath centerlines on the surface", GH_ParamAccess.list);
        pManager.AddNumberParameter("Width", "W", "Profile width across the curve (default 0.1)", GH_ParamAccess.item, 0.1);
        pManager.AddNumberParameter("Thickness", "T", "Profile thickness through the strip (default 0.01)", GH_ParamAccess.item, 0.01);
        pManager.AddBooleanParameter("Upright", "U", "False: strip lies flat on the surface. True: strip stands upright on edge", GH_ParamAccess.item, false);
        pManager.AddNumberParameter("Offset", "O", "Gap between the surface and the nearest strip face (default 0)", GH_ParamAccess.item, 0.0);
        pManager.AddNumberParameter("Tolerance", "S", "Chord deviation for curve sampling (default 0.01)", GH_ParamAccess.item, 0.01);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddMeshParameter("Laths", "L", "Swept lath solids as meshes, one per input curve", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        Mesh? mesh = null;
        var curves = new List<Curve>();
        double width = 0.1, thickness = 0.01, offset = 0.0, tolerance = 0.01;
        bool upright = false;

        if (!DA.GetData(0, ref mesh) || mesh == null) return;
        if (!DA.GetDataList(1, curves)) return;
        DA.GetData(2, ref width);
        DA.GetData(3, ref thickness);
        DA.GetData(4, ref upright);
        DA.GetData(5, ref offset);
        DA.GetData(6, ref tolerance);

        if (width <= 0 || thickness <= 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Width and Thickness must be positive.");
            return;
        }

        var data = MeshConvert.ToMeshData(mesh);
        var proj = new MeshProjection(data);
        var profile = new LathProfile(width, thickness, upright, offset);

        var laths = new List<Mesh>();
        int failed = 0;
        foreach (var curve in curves)
        {
            var line = CurveSample.ToPolyline(curve, tolerance);
            if (line == null) { failed++; continue; }

            var result = StripSweep.Sweep(proj, line, profile);
            if (result.HasValue)
                laths.Add(MeshConvert.ToRhinoMesh(result.Value.Mesh));
            else
                failed++;
        }

        if (failed > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"{failed} curve(s) could not be swept (degenerate or too short).");

        DA.SetDataList(0, laths);
    }
}
