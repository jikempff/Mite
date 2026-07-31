using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.Curvature;
using Mite.Core.Streamlines;
using Mite.Core.Geometry;

namespace Mite.Grasshopper.Components;

public class StreamlinesComponent : GH_Component
{
    public StreamlinesComponent()
        : base("Curvature Streamlines", "CurvStream",
            "Traces streamlines along principal curvature directions.",
            "Mite", "Curvature") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDE4");

    protected override Bitmap Icon =>
        new Bitmap(Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Mite.Grasshopper.Resources.Streamlines.png")!);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input mesh", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Seeds", "S", "Seed vertex indices", GH_ParamAccess.list);
        pManager.AddNumberParameter("Step", "St", "Step size (default 0.01)", GH_ParamAccess.item, 0.01);
        pManager.AddIntegerParameter("MaxSteps", "N", "Maximum integration steps (default 1000)", GH_ParamAccess.item, 1000);
        pManager.AddBooleanParameter("MaxDir", "Max", "Use max curvature direction (true) or min (false)", GH_ParamAccess.item, true);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("Streamlines", "C", "Curvature streamlines as polylines", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        Mesh? mesh = null;
        var seeds = new List<int>();
        double stepSize = 0.01;
        int maxSteps = 1000;
        bool maxDir = true;

        if (!DA.GetData(0, ref mesh) || mesh == null) return;
        if (!DA.GetDataList(1, seeds)) return;
        DA.GetData(2, ref stepSize);
        DA.GetData(3, ref maxSteps);
        DA.GetData(4, ref maxDir);

        var data = MeshConvert.ToMeshData(mesh);
        var curvature = PrincipalCurvature.Compute(data);

        var opts = new CurvatureStreamlines.Options
        {
            StepSize = stepSize,
            MaxSteps = maxSteps,
            UseMaxCurvature = maxDir
        };

        var lines = CurvatureStreamlines.Trace(data, seeds.ToArray(), curvature, opts);

        var polylines = new List<PolylineCurve>();
        foreach (var line in lines)
        {
            var pts = new List<Point3d>(line.Length);
            foreach (var p in line)
                pts.Add(MeshConvert.ToRhinoPoint(p));
            if (pts.Count > 1)
                polylines.Add(new PolylineCurve(pts));
        }
        DA.SetDataList(0, polylines);
    }
}
