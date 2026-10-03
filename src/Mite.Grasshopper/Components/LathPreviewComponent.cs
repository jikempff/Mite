using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Mite.Grasshopper.Components;

public class LathPreviewComponent : MiteComponent
{
    public LathPreviewComponent()
        : base("Lath Preview", "Preview",
            "Colors swept laths by utilization (e.g. from Lath Analysis or Gridshell Analysis): " +
            "green within limits, neon yellow near the limit, neon red beyond it (the web app's colours).",
            "Fabrication", "LathPreview") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDF7");

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
                $"Utilization count ({utils.Count}) does not match the lath count ({laths.Count}); " +
                "laths beyond the list reuse its last value. Use the per-lath outputs (Lath Analysis U, Gridshell Analysis Ul).");

        var colored = new List<Mesh?>();
        for (int i = 0; i < laths.Count; i++)
        {
            if (laths[i] == null) { colored.Add(null); continue; }
            double u = utils.Count > 0 ? utils[Math.Min(i, utils.Count - 1)] : 0.0;
            // NaN (a lath that could not be analyzed) shows neutral grey, not a green pass
            var color = UtilizationColor(u);
            var m = laths[i].DuplicateMesh();
            m.VertexColors.Clear();
            for (int v = 0; v < m.Vertices.Count; v++)
                m.VertexColors.Add(color);
            colored.Add(m);
        }

        DA.SetDataList(0, colored);
    }

    /// <summary>
    /// Mite's utilization colours (Mite.Core MitePalette, identical to the web app):
    /// green → neon yellow below the limit, a lightness drop to neon red at 1.0,
    /// darker red to 1.3, dark red beyond, grey for NaN.
    /// </summary>
    internal static Color UtilizationColor(double u) => PaletteColours.Utilization(u);
}
