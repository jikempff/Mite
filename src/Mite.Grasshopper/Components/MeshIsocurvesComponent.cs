using System;
using System.Collections.Generic;
using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.Analysis;

namespace Mite.Grasshopper.Components;

public class MeshIsocurvesComponent : MiteComponent
{
    public MeshIsocurvesComponent()
        : base("Mesh Isocurves", "Iso",
            "Extracts isocurves of a per-vertex scalar field (curvature, utilization, height, ...) " +
            "at the given levels. Wire Gaussian Curvature with level 0 to outline the anticlastic " +
            "regions where asymptotic laths can exist. One branch per level.",
            "Analysis", "MeshIsocurves") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDF9");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input mesh", GH_ParamAccess.item);
        pManager.AddNumberParameter("Values", "V", "One value per input mesh vertex", GH_ParamAccess.list);
        pManager.AddNumberParameter("Levels", "L", "Iso levels (default 0)", GH_ParamAccess.list, 0.0);
        pManager.AddBooleanParameter("Smooth", "S", "Output smooth interpolated curves instead of polylines (default false)", GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("Curves", "C", "Isocurves, one branch per level", GH_ParamAccess.tree);
        pManager.AddNumberParameter("Lengths", "L", "Length per isocurve", GH_ParamAccess.tree);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var input = LoadMesh(DA, 0);
        if (input == null) return;
        var values = new List<double>();
        var levels = new List<double>();
        bool smooth = false;
        if (!DA.GetDataList(1, values)) return;
        DA.GetDataList(2, levels);
        DA.GetData(3, ref smooth);
        if (levels.Count == 0) levels.Add(0.0);

        if (values.Count != input.RhinoVertexCount)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"Values needs one number per vertex: got {values.Count}, mesh has {input.RhinoVertexCount}.");
            return;
        }

        // Collapse onto welded vertices (average where duplicates disagree)
        var topoValues = new double[input.Data.VertexCount];
        var counts = new int[input.Data.VertexCount];
        for (int i = 0; i < values.Count; i++)
        {
            int t = input.TopoOfVertex[i];
            double v = values[i];
            if (double.IsNaN(v)) continue;
            topoValues[t] += v;
            counts[t]++;
        }
        for (int t = 0; t < topoValues.Length; t++)
            if (counts[t] > 0) topoValues[t] /= counts[t];

        var result = MeshIsocurves.Compute(input.Data, topoValues, levels);

        var curves = new DataTree<Curve>();
        var lengths = new DataTree<double>();
        for (int k = 0; k < levels.Count; k++)
        {
            var path = BranchPath(DA, k);
            curves.EnsurePath(path);
            lengths.EnsurePath(path);
            foreach (var line in result[k])
            {
                Curve? c = smooth ? MeshConvert.ToCurve(line) : MeshConvert.ToPolylineCurve(line);
                if (c == null) continue;
                curves.Add(c, path);
                lengths.Add(c.GetLength(), path);
            }
        }

        DA.SetDataTree(0, curves);
        DA.SetDataTree(1, lengths);
    }
}
