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
        pManager.AddIntegerParameter("Seeds", "S", "Seed vertex indices (optional when AutoSpace is on)", GH_ParamAccess.list);
        pManager.AddNumberParameter("Step", "St", "Step size (default 0.01)", GH_ParamAccess.item, 0.01);
        pManager.AddIntegerParameter("MaxSteps", "N", "Maximum integration steps (default 1000)", GH_ParamAccess.item, 1000);
        pManager.AddBooleanParameter("AutoSpace", "A", "Fill the anticlastic region with evenly-spaced curves instead of tracing only from seeds", GH_ParamAccess.item, false);
        pManager.AddNumberParameter("Spacing", "Sp", "Target distance between adjacent curves (AutoSpace only)", GH_ParamAccess.item, 0.0);
        pManager[1].Optional = true;
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
        bool autoSpace = false;
        double spacing = 0.0;

        if (!DA.GetData(0, ref mesh) || mesh == null) return;
        DA.GetDataList(1, seeds);
        DA.GetData(2, ref stepSize);
        DA.GetData(3, ref maxSteps);
        DA.GetData(4, ref autoSpace);
        DA.GetData(5, ref spacing);

        var data = MeshConvert.ToMeshData(mesh);
        var curvature = PrincipalCurvature.Compute(data);

        List<Mite.Core.Geometry.Vec3d[]> familyA, familyB;

        if (autoSpace)
        {
            if (spacing <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "AutoSpace requires a positive Spacing.");
                return;
            }

            var field = AsymptoticCurves.ComputeDirections(curvature);
            var opts = new EvenlySpacedNet.Options
            {
                Spacing = spacing,
                StepSize = stepSize,
                MaxSteps = maxSteps,
                ShouldCancel = GH_Document.IsEscapeKeyDown
            };
            int firstSeed = seeds.Count > 0 ? seeds[0] : -1;
            familyA = EvenlySpacedNet.TraceField(data, field.Family1, field.Exists, firstSeed, opts, field.Family2);
            familyB = EvenlySpacedNet.TraceField(data, field.Family2, field.Exists, firstSeed, opts, field.Family1);
        }
        else
        {
            if (seeds.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Provide Seeds, or enable AutoSpace with a Spacing.");
                return;
            }
            if (seeds.Count > 100)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{seeds.Count} seeds = up to {2 * seeds.Count} traced curves - this can take a long time. " +
                    "Press Esc to cancel. For a full, evenly spaced net use AutoSpace with a single seed instead.");

            var opts = new AsymptoticCurves.Options
            {
                StepSize = stepSize,
                MaxSteps = maxSteps,
                ShouldCancel = GH_Document.IsEscapeKeyDown
            };
            familyA = AsymptoticCurves.Trace(data, seeds.ToArray(), curvature, false, opts);
            familyB = AsymptoticCurves.Trace(data, seeds.ToArray(), curvature, true, opts);
        }

        if (familyA.Count == 0 && familyB.Count == 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                "No asymptotic curves traced. Seeds may lie in regions of non-negative Gaussian curvature.");

        DA.SetDataList(0, ToCurves(familyA));
        DA.SetDataList(1, ToCurves(familyB));
    }

    private static List<Curve> ToCurves(List<Mite.Core.Geometry.Vec3d[]> lines)
    {
        var curves = new List<Curve>();
        foreach (var line in lines)
        {
            var pts = new List<Point3d>(line.Length);
            foreach (var p in line)
                pts.Add(MeshConvert.ToRhinoPoint(p));
            var c = CurveBuild.Interpolated(pts);
            if (c != null) curves.Add(c);
        }
        return curves;
    }
}
