using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;

namespace Mite.Grasshopper.Components;

public class GeodesicNetComponent : GH_Component
{
    public GeodesicNetComponent()
        : base("Geodesic Net", "GeoNet",
            "Traces straightest geodesics from seed vertices along given directions for gridshell design. " +
            "Geodesics extend both ways from each seed; if fewer directions than seeds are supplied, the last is reused.",
            "Mite", "Gridshells") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDE9");

    protected override Bitmap Icon =>
        new Bitmap(Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Mite.Grasshopper.Resources.GeodesicNet.png")!);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input mesh", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Seeds", "S", "Seed vertex indices", GH_ParamAccess.list);
        pManager.AddVectorParameter("Directions", "D", "Initial direction per seed (last reused if fewer)", GH_ParamAccess.list);
        pManager.AddNumberParameter("Step", "St", "Step size (default 0.01)", GH_ParamAccess.item, 0.01);
        pManager.AddIntegerParameter("MaxSteps", "N", "Maximum integration steps (default 1000)", GH_ParamAccess.item, 1000);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("Geodesics", "G", "Geodesic curves as polylines", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        Mesh? mesh = null;
        var seeds = new List<int>();
        var directions = new List<Vector3d>();
        double stepSize = 0.01;
        int maxSteps = 1000;

        if (!DA.GetData(0, ref mesh) || mesh == null) return;
        if (!DA.GetDataList(1, seeds)) return;
        if (!DA.GetDataList(2, directions)) return;
        DA.GetData(3, ref stepSize);
        DA.GetData(4, ref maxSteps);

        var data = MeshConvert.ToMeshData(mesh);

        var dirs = new Vec3d[directions.Count];
        for (int i = 0; i < directions.Count; i++)
            dirs[i] = new Vec3d(directions[i].X, directions[i].Y, directions[i].Z);

        var opts = new GeodesicCurves.Options { StepSize = stepSize, MaxSteps = maxSteps };
        var lines = GeodesicCurves.Trace(data, seeds.ToArray(), dirs, opts);

        var curves = new List<PolylineCurve>();
        foreach (var line in lines)
        {
            var pts = new List<Point3d>(line.Length);
            foreach (var p in line)
                pts.Add(MeshConvert.ToRhinoPoint(p));
            if (pts.Count > 1)
                curves.Add(new PolylineCurve(pts));
        }
        DA.SetDataList(0, curves);
    }
}
