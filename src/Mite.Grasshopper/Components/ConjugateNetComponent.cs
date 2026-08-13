using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.Gridshells;

namespace Mite.Grasshopper.Components;

public class ConjugateNetComponent : GH_Component
{
    public ConjugateNetComponent()
        : base("Conjugate Net", "ConjNet",
            "Traces both families of principal curvature lines with even spacing, giving an " +
            "approximate conjugate net — the curve layout whose quad cells can be planarized " +
            "into a planar-quad (PQ) panelization of the surface.",
            "Mite", "Gridshells") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDF1");

    protected override Bitmap Icon =>
        new Bitmap(Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Mite.Grasshopper.Resources.ConjugateNet.png")!);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input mesh", GH_ParamAccess.item);
        pManager.AddNumberParameter("Spacing", "Sp", "Target distance between adjacent curves", GH_ParamAccess.item, 0.1);
        pManager.AddNumberParameter("Step", "St", "Tracing step size (default: Spacing / 10)", GH_ParamAccess.item, 0.01);
        pManager.AddIntegerParameter("MaxSteps", "N", "Maximum integration steps per curve half", GH_ParamAccess.item, 1000);
        pManager.AddIntegerParameter("Seed", "S", "First seed vertex (-1 = nearest the mesh centroid)", GH_ParamAccess.item, -1);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("CurvesA", "A", "Max-curvature direction family", GH_ParamAccess.list);
        pManager.AddCurveParameter("CurvesB", "B", "Min-curvature direction family", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        Mesh? mesh = null;
        double spacing = 0.1, step = 0.01;
        int maxSteps = 1000, seed = -1;

        if (!DA.GetData(0, ref mesh) || mesh == null) return;
        DA.GetData(1, ref spacing);
        DA.GetData(2, ref step);
        DA.GetData(3, ref maxSteps);
        DA.GetData(4, ref seed);

        if (spacing <= 0 || step <= 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Spacing and Step must be positive.");
            return;
        }

        var data = MeshConvert.ToMeshData(mesh);
        var opts = new EvenlySpacedNet.Options
        {
            Spacing = spacing,
            StepSize = step,
            MaxSteps = maxSteps,
            ShouldCancel = GH_Document.IsEscapeKeyDown
        };

        var net = ConjugateNet.Trace(data, seed, opts);

        DA.SetDataList(0, CurveOut(net.FamilyA));
        DA.SetDataList(1, CurveOut(net.FamilyB));

        if (net.FamilyA.Count == 0 && net.FamilyB.Count == 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                "No curves traced. Check Spacing against the mesh size.");
    }

    private static List<Curve> CurveOut(List<Core.Geometry.Vec3d[]> lines)
    {
        var curves = new List<Curve>();
        foreach (var line in lines)
        {
            var pts = new List<Point3d>(line.Length);
            foreach (var p in line) pts.Add(MeshConvert.ToRhinoPoint(p));
            var c = CurveBuild.Interpolated(pts);
            if (c != null) curves.Add(c);
        }
        return curves;
    }
}
