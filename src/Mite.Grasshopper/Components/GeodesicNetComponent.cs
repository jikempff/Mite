using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;

namespace Mite.Grasshopper.Components;

public class GeodesicNetComponent : MiteComponent
{
    public GeodesicNetComponent()
        : base("Geodesic Net", "GeoNet",
            "Traces straightest geodesics from seeds along given directions for gridshell design. " +
            "Geodesics extend both ways from each seed; with AutoSpace one family fills the mesh " +
            "with evenly spaced geodesics grown from the first seed and direction.",
            "Gridshells", "GeodesicNet") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDE9");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input mesh", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Seeds", "S", "Seed vertex indices", GH_ParamAccess.list);
        pManager.AddVectorParameter("Directions", "D", "Initial direction per seed (last reused if fewer)", GH_ParamAccess.list, Vector3d.XAxis);
        pManager.AddNumberParameter("Step", "St", "Integration step (0 = automatic)", GH_ParamAccess.item, 0.0);
        pManager.AddIntegerParameter("MaxSteps", "N", "Maximum steps per curve half (0 = automatic from the mesh size)", GH_ParamAccess.item, 0);
        pManager.AddBooleanParameter("AutoSpace", "A", "Fill the surface with evenly-spaced geodesics of one family, grown from the first seed and direction", GH_ParamAccess.item, false);
        pManager.AddNumberParameter("Spacing", "Sp", "Target distance between adjacent curves (AutoSpace; 0 = automatic)", GH_ParamAccess.item, 0.0);
        pManager.AddPointParameter("SeedPoints", "P", "Seed points (nearest vertex is used); may be combined with Seeds", GH_ParamAccess.list);
        pManager.AddIntegerParameter("MaxCurves", "Mx", "Cap on the number of curves (AutoSpace, default 200)", GH_ParamAccess.item, 200);
        pManager[1].Optional = true;
        pManager[7].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("Geodesics", "G", "Geodesic curves (smooth interpolated curves)", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var input = LoadMesh(DA, 0);
        if (input == null) return;

        var seedIdx = new List<int>();
        var seedPts = new List<Point3d>();
        var directions = new List<Vector3d>();
        double stepSize = 0, spacing = 0; int maxSteps = 0, maxCurves = 200; bool autoSpace = false;
        DA.GetDataList(1, seedIdx);
        DA.GetDataList(2, directions);
        DA.GetData(3, ref stepSize);
        DA.GetData(4, ref maxSteps);
        DA.GetData(5, ref autoSpace);
        DA.GetData(6, ref spacing);
        DA.GetDataList(7, seedPts);
        DA.GetData(8, ref maxCurves);

        var data = input.Data;
        var proj = new MeshProjection(data);
        if (directions.Count == 0) directions.Add(Vector3d.XAxis);

        // Seeds and directions are paired by position (the last direction is
        // reused when there are fewer), so resolve them together and keep the
        // pairing when a seed is skipped
        var seeds = new List<int>();
        var dirs = new List<Vec3d>();
        int skipped = 0;
        for (int i = 0; i < seedIdx.Count; i++)
        {
            int t = input.ToTopo(seedIdx[i]);
            if (t < 0) { skipped++; continue; }
            seeds.Add(t);
            dirs.Add(MeshConvert.ToVec3d(directions[Math.Min(i, directions.Count - 1)]));
        }
        for (int i = 0; i < seedPts.Count; i++)
        {
            int t = proj.NearestVertexGlobal(MeshConvert.ToVec3d(seedPts[i]));
            if (t < 0) { skipped++; continue; }
            seeds.Add(t);
            dirs.Add(MeshConvert.ToVec3d(directions[Math.Min(seedIdx.Count + i, directions.Count - 1)]));
        }
        if (skipped > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"{skipped} out-of-range seed(s) skipped.");
        if (seeds.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Provide at least one seed (Seeds or SeedPoints).");
            return;
        }

        List<Vec3d[]> lines;
        if (autoSpace)
        {
            var opts = new EvenlySpacedNet.Options
            {
                Spacing = spacing, StepSize = stepSize, MaxSteps = maxSteps,
                MaxCurves = Math.Max(1, maxCurves), ShouldCancel = Cancelled
            };
            lines = EvenlySpacedNet.TraceGeodesics(data, seeds[0], dirs[0], opts);
            ReportTracing(opts, "Geodesics");
            if (spacing <= 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Automatic spacing: {opts.ResolvedSpacing:G4}");
        }
        else
        {
            if (seeds.Count > 100)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{seeds.Count} seeds = {seeds.Count} traced geodesics - this can take a long time. " +
                    "Press Esc to cancel. For a full, evenly spaced net use AutoSpace with a single seed instead.");

            var opts = new GeodesicCurves.Options { StepSize = stepSize, MaxSteps = maxSteps, ShouldCancel = Cancelled };
            lines = GeodesicCurves.Trace(data, seeds.ToArray(), dirs.ToArray(), opts);
            if (Cancelled())
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Cancelled with Esc, output is partial.");
        }

        if (lines.Count == 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No geodesics traced; check the seeds and directions.");
        DA.SetDataList(0, MeshConvert.ToCurves(lines));
    }
}
