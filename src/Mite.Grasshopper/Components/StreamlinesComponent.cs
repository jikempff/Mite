using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.Curvature;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;
using Mite.Core.Streamlines;

namespace Mite.Grasshopper.Components;

public class StreamlinesComponent : MiteComponent
{
    public StreamlinesComponent()
        : base("Curvature Streamlines", "CurvStream",
            "Traces lines of curvature along the maximum or minimum principal direction, " +
            "from seed vertices / points or filling the mesh with evenly spaced lines (AutoSpace).",
            "Curvature", "Streamlines") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDE4");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input mesh", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Seeds", "S", "Seed vertex indices", GH_ParamAccess.list);
        pManager.AddNumberParameter("Step", "St", "Integration step (0 = automatic from the mesh edge length)", GH_ParamAccess.item, 0.0);
        pManager.AddIntegerParameter("MaxSteps", "N", "Maximum steps per curve half (0 = automatic from the mesh size)", GH_ParamAccess.item, 0);
        pManager.AddBooleanParameter("MaxDir", "Max", "Use max curvature direction (true) or min (false)", GH_ParamAccess.item, true);
        pManager.AddPointParameter("SeedPoints", "P", "Seed points (nearest vertex is used); may be combined with Seeds", GH_ParamAccess.list);
        pManager.AddBooleanParameter("AutoSpace", "A", "Fill the mesh with evenly spaced lines grown from the first seed", GH_ParamAccess.item, false);
        pManager.AddNumberParameter("Spacing", "Sp", "Target distance between adjacent lines (AutoSpace; 0 = automatic)", GH_ParamAccess.item, 0.0);
        pManager.AddIntegerParameter("MaxCurves", "Mx", "Cap on the number of lines (AutoSpace, default 200)", GH_ParamAccess.item, 200);
        pManager.AddBooleanParameter("Continuous", "Ct", "Continuous curves: run to the border even where they come closer than Spacing to a neighbour; only near-coincident traces stop, ending on the neighbour. False stops traces at 0.4 x Spacing for a more even but interrupted layout", GH_ParamAccess.item, true);
        pManager[1].Optional = true;
        pManager[5].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("Streamlines", "C", "Curvature lines (smooth interpolated curves)", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var input = LoadMesh(DA, 0);
        if (input == null) return;

        var seedIdx = new List<int>();
        var seedPts = new List<Point3d>();
        double stepSize = 0; int maxSteps = 0; bool maxDir = true, autoSpace = false; double spacing = 0;
        DA.GetDataList(1, seedIdx);
        DA.GetData(2, ref stepSize);
        DA.GetData(3, ref maxSteps);
        DA.GetData(4, ref maxDir);
        DA.GetDataList(5, seedPts);
        DA.GetData(6, ref autoSpace);
        DA.GetData(7, ref spacing);
        int maxCurves = 200; bool continuous = true;
        DA.GetData(8, ref maxCurves);
        DA.GetData(9, ref continuous);

        var data = input.Data;
        var proj = new MeshProjection(data);
        var seeds = ResolveSeeds(input, proj, seedIdx, seedPts);
        var curvature = PrincipalCurvature.Compute(data);
        var dirs = maxDir ? curvature.D1 : curvature.D2;

        List<Vec3d[]> lines;
        if (autoSpace)
        {
            var opts = new EvenlySpacedNet.Options
            {
                Spacing = spacing, StepSize = stepSize, MaxSteps = maxSteps, ShouldCancel = Cancelled,
                MaxCurves = Math.Max(1, maxCurves), Continuous = continuous
            };
            lines = EvenlySpacedNet.TraceField(data, dirs, null, seeds.Count > 0 ? seeds[0] : -1, opts);
            ReportTracing(opts, "Streamlines");
            if (spacing <= 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Automatic spacing: {opts.ResolvedSpacing:G4}");
        }
        else
        {
            if (seeds.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Provide Seeds or SeedPoints, or enable AutoSpace.");
                return;
            }
            if (seeds.Count > 100)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{seeds.Count} seeds = {seeds.Count} traced streamlines - this can take a long time. " +
                    "Press Esc to cancel. For a full, evenly spaced set use AutoSpace with a single seed instead.");

            var opts = new CurvatureStreamlines.Options
            {
                StepSize = stepSize, MaxSteps = maxSteps, UseMaxCurvature = maxDir, ShouldCancel = Cancelled
            };
            lines = CurvatureStreamlines.Trace(data, seeds.ToArray(), curvature, opts);
            if (Cancelled())
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Cancelled with Esc, output is partial.");
        }

        DA.SetDataList(0, MeshConvert.ToCurves(lines));
    }
}
