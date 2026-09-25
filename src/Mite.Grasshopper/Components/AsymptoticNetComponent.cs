using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.Curvature;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;

namespace Mite.Grasshopper.Components;

public class AsymptoticNetComponent : MiteComponent
{
    public AsymptoticNetComponent()
        : base("Asymptotic Net", "AsymNet",
            "Traces both families of asymptotic curves (zero normal curvature) for gridshell design. " +
            "Asymptotic curves only exist where Gaussian curvature is negative; with AutoSpace the " +
            "whole anticlastic region is filled with evenly spaced curves grown from one seed.",
            "Gridshells", "AsymptoticNet") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDE8");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input mesh", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Seeds", "S", "Seed vertex indices (optional with AutoSpace: the first one starts the net)", GH_ParamAccess.list);
        pManager.AddNumberParameter("Step", "St", "Integration step (0 = automatic: Spacing / 10, or from the mesh edge length)", GH_ParamAccess.item, 0.0);
        pManager.AddIntegerParameter("MaxSteps", "N", "Maximum steps per curve half (0 = automatic from the mesh size)", GH_ParamAccess.item, 0);
        pManager.AddBooleanParameter("AutoSpace", "A", "Fill the anticlastic region with evenly-spaced curves instead of tracing only from seeds", GH_ParamAccess.item, true);
        pManager.AddNumberParameter("Spacing", "Sp", "Target distance between adjacent curves (AutoSpace; 0 = automatic, bounding box / 30)", GH_ParamAccess.item, 0.0);
        pManager.AddPointParameter("SeedPoints", "P", "Seed points (nearest vertex is used); may be combined with Seeds", GH_ParamAccess.list);
        pManager.AddIntegerParameter("MaxCurves", "Mx", "Cap on curves per family (AutoSpace, default 200)", GH_ParamAccess.item, 200);
        pManager.AddBooleanParameter("Continuous", "Ct", "Continuous curves: run to the border even where they come closer than Spacing to a neighbour; only near-coincident traces stop, ending on the neighbour. False stops traces at 0.4 x Spacing for a more even but interrupted layout", GH_ParamAccess.item, true);
        pManager.AddNumberParameter("MinAngle", "An", "Minimum crossing angle between the two families (degrees). Near the K = 0 line the families collapse onto each other; vertices below this angle are left out of the net and of the Anticlastic mask (default 15, 0 = raw K < 0 region)", GH_ParamAccess.item, 15.0);
        pManager[1].Optional = true;
        pManager[6].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("FamilyA", "A", "First asymptotic curve family", GH_ParamAccess.list);
        pManager.AddCurveParameter("FamilyB", "B", "Second asymptotic curve family", GH_ParamAccess.list);
        pManager.AddBooleanParameter("Anticlastic", "K", "Per input vertex: true where asymptotic directions exist (K < 0)", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var input = LoadMesh(DA, 0);
        if (input == null) return;

        var seedIdx = new List<int>();
        var seedPts = new List<Point3d>();
        double stepSize = 0, spacing = 0; int maxSteps = 0, maxCurves = 200; bool autoSpace = true; bool continuous = true;
        DA.GetDataList(1, seedIdx);
        DA.GetData(2, ref stepSize);
        DA.GetData(3, ref maxSteps);
        DA.GetData(4, ref autoSpace);
        DA.GetData(5, ref spacing);
        DA.GetDataList(6, seedPts);
        DA.GetData(7, ref maxCurves);
        DA.GetData(8, ref continuous);
        double minAngle = 15.0;
        DA.GetData(9, ref minAngle);

        var data = input.Data;
        var proj = new MeshProjection(data);
        var seeds = ResolveSeeds(input, proj, seedIdx, seedPts);
        var curvature = PrincipalCurvature.Compute(data);
        var field = AsymptoticCurves.ComputeDirections(curvature, data, minAngle);

        int anticlastic = 0;
        foreach (bool e in field.Exists) if (e) anticlastic++;
        if (anticlastic == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                "The mesh has no anticlastic (K < 0) region, so no asymptotic curves exist.");
            DA.SetDataList(2, new bool[input.RhinoVertexCount]);
            return;
        }

        List<Vec3d[]> familyA, familyB;
        if (autoSpace)
        {
            var opts = new EvenlySpacedNet.Options
            {
                Spacing = spacing, StepSize = stepSize, MaxSteps = maxSteps,
                MaxCurves = Math.Max(1, maxCurves), ShouldCancel = Cancelled, Continuous = continuous
            };
            int firstSeed = -1;
            foreach (int s in seeds) if (field.Exists[s]) { firstSeed = s; break; }
            if (seeds.Count > 0 && firstSeed < 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "No seed lies in the anticlastic region; starting from the region nearest the centroid instead.");

            familyA = EvenlySpacedNet.TraceField(data, field.Family1, field.Exists, firstSeed, opts, field.Family2);
            ReportTracing(opts, "Family A");
            var optsB = new EvenlySpacedNet.Options
            {
                Spacing = spacing, StepSize = stepSize, MaxSteps = maxSteps,
                MaxCurves = Math.Max(1, maxCurves), ShouldCancel = Cancelled, Continuous = continuous
            };
            familyB = EvenlySpacedNet.TraceField(data, field.Family2, field.Exists, firstSeed, optsB, field.Family1);
            ReportTracing(optsB, "Family B");
            if (spacing <= 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Automatic spacing: {opts.ResolvedSpacing:G4}");
        }
        else
        {
            if (seeds.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Provide Seeds / SeedPoints, or enable AutoSpace.");
                return;
            }
            if (seeds.Count > 100)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{seeds.Count} seeds = up to {2 * seeds.Count} traced curves - this can take a long time. " +
                    "Press Esc to cancel. For a full, evenly spaced net use AutoSpace with a single seed instead.");

            var opts = new AsymptoticCurves.Options { StepSize = stepSize, MaxSteps = maxSteps, ShouldCancel = Cancelled, MinCrossingAngle = minAngle };
            familyA = AsymptoticCurves.Trace(data, seeds.ToArray(), curvature, false, opts);
            familyB = AsymptoticCurves.Trace(data, seeds.ToArray(), curvature, true, opts);
            if (Cancelled())
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Cancelled with Esc, output is partial.");
        }

        if (familyA.Count == 0 && familyB.Count == 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                "No asymptotic curves traced. Seeds may lie in regions of non-negative Gaussian curvature, " +
                "or the Spacing may be too large for the anticlastic region.");

        DA.SetDataList(0, MeshConvert.ToCurves(familyA));
        DA.SetDataList(1, MeshConvert.ToCurves(familyB));

        var mask = new bool[input.RhinoVertexCount];
        for (int i = 0; i < mask.Length; i++) mask[i] = field.Exists[input.TopoOfVertex[i]];
        DA.SetDataList(2, mask);
    }
}
