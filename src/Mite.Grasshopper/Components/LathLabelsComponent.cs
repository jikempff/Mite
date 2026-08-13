using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Text;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Mite.Grasshopper.Components;

public class LathLabelsComponent : GH_Component
{
    public LathLabelsComponent()
        : base("Lath Labels", "Labels",
            "Assigns an ID to every lath, places a label point at each midpoint, and builds a " +
            "CSV bill of materials (id, length, optional strain utilization from Lath Analysis). " +
            "Wire the points and labels into a Text Tag component for display.",
            "Mite", "Fabrication") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDF6");

    protected override Bitmap Icon =>
        new Bitmap(Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Mite.Grasshopper.Resources.LathLabels.png")!);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("Curves", "C", "Lath centerlines", GH_ParamAccess.list);
        pManager.AddTextParameter("Prefix", "P", "ID prefix (default \"L\")", GH_ParamAccess.item, "L");
        pManager.AddNumberParameter("Utilization", "U", "Optional per-lath utilization (from Lath Analysis) for the report", GH_ParamAccess.list);
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

        if (!DA.GetDataList(0, curves)) return;
        DA.GetData(1, ref prefix);
        DA.GetDataList(2, utils);

        var points = new List<Point3d>();
        var labels = new List<string>();
        var report = new StringBuilder();
        report.AppendLine(utils.Count == curves.Count
            ? "id,length,utilization"
            : "id,length");

        double totalLength = 0;
        for (int i = 0; i < curves.Count; i++)
        {
            string id = $"{prefix}{i:D3}";
            double length = curves[i].GetLength();
            totalLength += length;

            points.Add(curves[i].PointAtNormalizedLength(0.5));
            labels.Add(id);

            report.Append(id).Append(',')
                .Append(length.ToString("F4", System.Globalization.CultureInfo.InvariantCulture));
            if (utils.Count == curves.Count)
                report.Append(',').Append(utils[i].ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
            report.AppendLine();
        }
        report.AppendLine($"# total laths: {curves.Count}, total length: {totalLength:F3}");

        DA.SetDataList(0, points);
        DA.SetDataList(1, labels);
        DA.SetData(2, report.ToString());
    }
}
