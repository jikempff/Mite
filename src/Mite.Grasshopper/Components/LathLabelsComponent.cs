using System;
using System.Collections.Generic;
using System.Text;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Mite.Grasshopper.Components;

public class LathLabelsComponent : MiteComponent
{
    public LathLabelsComponent()
        : base("Lath Labels", "Labels",
            "Assigns an ID to every lath, places a label point at each midpoint, and builds a " +
            "CSV bill of materials (id, length, optional strain utilization from Lath Analysis). " +
            "Wire the points and labels into a Text Tag component for display.",
            "Fabrication", "LathLabels") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDF6");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("Curves", "C", "Lath centerlines", GH_ParamAccess.list);
        pManager.AddTextParameter("Prefix", "P", "ID prefix (default \"L\")", GH_ParamAccess.item, "L");
        pManager.AddNumberParameter("Utilization", "U", "Optional per-lath utilization (from Lath Analysis / Gridshell Analysis) for the report", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Start", "N", "First ID number (default 0; use e.g. 100 for a second family)", GH_ParamAccess.item, 0);
        pManager[2].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddPointParameter("Points", "P", "Label anchor points (lath midpoints)", GH_ParamAccess.list);
        pManager.AddTextParameter("Labels", "L", "Lath IDs", GH_ParamAccess.list);
        pManager.AddTextParameter("Report", "R", "CSV bill of materials", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var curves = new List<Curve>();
        var utils = new List<double>();
        string prefix = "L";
        int start = 0;

        if (!DA.GetDataList(0, curves)) return;
        DA.GetData(1, ref prefix);
        DA.GetDataList(2, utils);
        DA.GetData(3, ref start);

        bool withUtil = utils.Count == curves.Count && utils.Count > 0;
        if (utils.Count > 0 && !withUtil)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"Utilization count ({utils.Count}) does not match the curve count ({curves.Count}); the report omits it.");

        var points = new List<Point3d?>();
        var labels = new List<string>();
        var report = new StringBuilder();
        report.AppendLine(withUtil ? "id,length,utilization" : "id,length");

        double totalLength = 0;
        for (int i = 0; i < curves.Count; i++)
        {
            string id = $"{prefix}{start + i:D3}";
            labels.Add(id);
            if (curves[i] == null)
            {
                points.Add(null);
                report.Append(id).AppendLine(",");
                continue;
            }
            double length = curves[i].GetLength();
            totalLength += length;

            points.Add(curves[i].PointAtNormalizedLength(0.5));

            report.Append(id).Append(',')
                .Append(length.ToString("F4", System.Globalization.CultureInfo.InvariantCulture));
            if (withUtil)
                report.Append(',').Append(utils[i].ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
            report.AppendLine();
        }
        report.AppendLine($"# total laths: {curves.Count}, total length: {totalLength:F3}");

        DA.SetDataList(0, points);
        DA.SetDataList(1, labels);
        DA.SetData(2, report.ToString());
    }
}
