using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Mite.Grasshopper.Components;

public class LathPreviewComponent : GH_Component
{
    public LathPreviewComponent()
        : base("Lath Preview", "Preview",
            "Colors swept laths by utilization (e.g. from Lath Analysis or Gridshell Analysis): " +
            "green within limits, yellow near the limit, red beyond it.",
            "Mite", "Fabrication") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDF7");

    protected override Bitmap Icon =>
        new Bitmap(Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Mite.Grasshopper.Resources.LathPreview.png")!);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Laths", "L", "Swept lath meshes (from Lath Sweep)", GH_ParamAccess.list);
        pManager.AddNumberParameter("Utilization", "U", "Per-lath utilization values (1.0 = at the limit)", GH_ParamAccess.list);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddMeshParameter("Laths", "L", "Colored lath meshes", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var laths = new List<Mesh>();
        var utils = new List<double>();

        if (!DA.GetDataList(0, laths)) return;
        if (!DA.GetDataList(1, utils)) return;

        if (utils.Count != laths.Count)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"Utilization count ({utils.Count}) does not match lath count ({laths.Count}); extra entries clamp to the ends.");

        var colored = new List<Mesh>();
        for (int i = 0; i < laths.Count; i++)
        {
            double u = utils[Math.Min(i, utils.Count - 1)];
            var color = UtilizationColor(u);
            var m = laths[i].DuplicateMesh();
            m.VertexColors.Clear();
            for (int v = 0; v < m.Vertices.Count; v++)
                m.VertexColors.Add(color);
            colored.Add(m);
        }

        DA.SetDataList(0, colored);
    }

    internal static Color UtilizationColor(double u)
    {
        // 0 -> green, 0.7 -> yellow, 1.0 -> red, >1.3 -> dark red
        if (u <= 0.7)
        {
            double t = u / 0.7;
            return Lerp(Color.FromArgb(46, 160, 67), Color.FromArgb(230, 200, 40), t);
        }
        if (u <= 1.0)
        {
            double t = (u - 0.7) / 0.3;
            return Lerp(Color.FromArgb(230, 200, 40), Color.FromArgb(220, 60, 50), t);
        }
        double d = Math.Min(1.0, (u - 1.0) / 0.3);
        return Lerp(Color.FromArgb(220, 60, 50), Color.FromArgb(120, 20, 20), d);
    }

    private static Color Lerp(Color a, Color b, double t)
    {
        t = Math.Max(0.0, Math.Min(1.0, t));
        return Color.FromArgb(
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));
    }
}
