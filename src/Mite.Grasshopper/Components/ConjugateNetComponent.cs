using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;

namespace Mite.Grasshopper.Components;

public class ConjugateNetComponent : MiteComponent
{
    public ConjugateNetComponent()
        : base("Conjugate Net", "ConjNet",
            "Traces both families of principal curvature lines with even spacing, giving an " +
            "approximate conjugate net — the curve layout whose quad cells can be planarized " +
            "into a planar-quad (PQ) panelization of the surface.",
            "Gridshells", "ConjugateNet") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDF1");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input mesh", GH_ParamAccess.item);
        pManager.AddNumberParameter("Spacing", "Sp", "Target distance between adjacent curves (0 = automatic, bounding box / 30)", GH_ParamAccess.item, 0.0);
        pManager.AddNumberParameter("Step", "St", "Tracing step (0 = automatic: Spacing / 10)", GH_ParamAccess.item, 0.0);
        pManager.AddIntegerParameter("MaxSteps", "N", "Maximum steps per curve half (0 = automatic)", GH_ParamAccess.item, 0);
        pManager.AddIntegerParameter("Seed", "S", "First seed vertex (-1 = nearest the mesh centroid)", GH_ParamAccess.item, -1);
        pManager.AddPointParameter("SeedPoint", "P", "First seed as a point (overrides Seed)", GH_ParamAccess.item);
        pManager.AddIntegerParameter("MaxCurves", "Mx", "Cap on curves per family (default 200)", GH_ParamAccess.item, 200);
        pManager.AddBooleanParameter("Continuous", "Ct", "Continuous curves: run to the border even where they come closer than Spacing to a neighbour; only near-coincident traces stop, ending on the neighbour. False stops traces at 0.4 x Spacing for a more even but interrupted layout", GH_ParamAccess.item, true);
        pManager[5].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("CurvesA", "A", "Max-curvature direction family", GH_ParamAccess.list);
        pManager.AddCurveParameter("CurvesB", "B", "Min-curvature direction family", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var input = LoadMesh(DA, 0);
        if (input == null) return;

        double spacing = 0, step = 0; int maxSteps = 0, seed = -1, maxCurves = 200; bool continuous = true;
        Point3d seedPoint = Point3d.Unset;
        DA.GetData(1, ref spacing);
        DA.GetData(2, ref step);
        DA.GetData(3, ref maxSteps);
        DA.GetData(4, ref seed);
        DA.GetData(5, ref seedPoint);
        DA.GetData(6, ref maxCurves);
        DA.GetData(7, ref continuous);

        var data = input.Data;
        var proj = new MeshProjection(data);
        int firstSeed = seedPoint.IsValid ? proj.NearestVertexGlobal(MeshConvert.ToVec3d(seedPoint)) : input.ToTopo(seed);

        var opts = new EvenlySpacedNet.Options
        {
            Spacing = spacing, StepSize = step, MaxSteps = maxSteps,
            MaxCurves = Math.Max(1, maxCurves), ShouldCancel = Cancelled, Continuous = continuous
        };

        var net = ConjugateNet.Trace(data, firstSeed, opts);
        ReportTracing(opts, "Conjugate net");
        if (spacing <= 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Automatic spacing: {opts.ResolvedSpacing:G4}");

        DA.SetDataList(0, MeshConvert.ToCurves(net.FamilyA));
        DA.SetDataList(1, MeshConvert.ToCurves(net.FamilyB));

        if (net.FamilyA.Count == 0 && net.FamilyB.Count == 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                "No curves traced. Check Spacing against the mesh size.");
    }
}
