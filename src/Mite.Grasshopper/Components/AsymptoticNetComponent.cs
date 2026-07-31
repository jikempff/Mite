using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.Curvature;
using Mite.Core.Gridshells;

namespace Mite.Grasshopper.Components;

public class AsymptoticNetComponent : GH_Component
{
    public AsymptoticNetComponent()
        : base("Asymptotic Net", "AsymNet",
            "Traces both families of asymptotic curves (zero normal curvature) for gridshell design. " +
            "Asymptotic curves only exist where Gaussian curvature is negative.",
            "Mite", "Gridshells") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDE8");

    protected override Bitmap Icon =>
        new Bitmap(Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Mite.Grasshopper.Resources.AsymptoticNet.png")!);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input mesh", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Seeds", "S", "Seed vertex indices", GH_ParamAccess.list);
        pManager.AddNumberParameter("Step", "St", "Step size (default 0.01)", GH_ParamAccess.item, 0.01);
        pManager.AddIntegerParameter("MaxSteps", "N", "Maximum integration steps (default 1000)", GH_ParamAccess.item, 1000);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("FamilyA", "A", "First asymptotic curve family", GH_ParamAccess.list);
        pManager.AddCurveParameter("FamilyB", "B", "Second asymptotic curve family", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        Mesh? mesh = null;
        var seeds = new List<int>();
        double stepSize = 0.01;
        int maxSteps = 1000;

        if (!DA.GetData(0, ref mesh) || mesh == null) return;
        if (!DA.GetDataList(1, seeds)) return;
        DA.GetData(2, ref stepSize);
        DA.GetData(3, ref maxSteps);

        var data = MeshConvert.ToMeshData(mesh);
        var curvature = PrincipalCurvature.Compute(data);

        var opts = new AsymptoticCurves.Options { StepSize = stepSize, MaxSteps = maxSteps };
        var familyA = AsymptoticCurves.Trace(data, seeds.ToArray(), curvature, false, opts);
        var familyB = AsymptoticCurves.Trace(data, seeds.ToArray(), curvature, true, opts);

        if (familyA.Count == 0 && familyB.Count == 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                "No asymptotic curves traced. Seeds may lie in regions of non-negative Gaussian curvature.");

        DA.SetDataList(0, ToPolylines(familyA));
        DA.SetDataList(1, ToPolylines(familyB));
    }

    private static List<PolylineCurve> ToPolylines(List<Mite.Core.Geometry.Vec3d[]> lines)
    {
        var curves = new List<PolylineCurve>();
        foreach (var line in lines)
        {
            var pts = new List<Point3d>(line.Length);
            foreach (var p in line)
                pts.Add(MeshConvert.ToRhinoPoint(p));
            if (pts.Count > 1)
                curves.Add(new PolylineCurve(pts));
        }
        return curves;
    }
}
