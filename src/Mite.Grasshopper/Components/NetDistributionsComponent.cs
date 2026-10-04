using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Rhino.Geometry;
using Mite.Core.Analysis;
using Mite.Core.Geometry;

namespace Mite.Grasshopper.Components;

/// <summary>
/// Spacing, crossing-angle and utilization distributions of a lath net (see
/// Mite.Core.Analysis.NetDistributions for the definitions and sources:
/// Freedman &amp; Diaconis 1981 bins, Pottmann et al. 2010 width tolerances).
/// </summary>
public class NetDistributionsComponent : MiteComponent
{
    public NetDistributionsComponent()
        : base("Net Distributions", "NetDist",
            "The whole spread of a net, not only its extremes: spacing across each family (measured " +
            "perpendicular to the curves, on both sides of every sample), crossing angles between the " +
            "families, and lath utilization — with histograms (Freedman–Diaconis bins) and the share " +
            "within ±5 % / ±7.5 % of the target spacing.",
            "Analysis", "NetDistributions") { }

    public override Guid ComponentGuid => new("6D1E2B7A-3C94-4F0B-9A55-2E8F71C4D0A3");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Mesh the curves lie on (its normals tell the two sides of a curve apart)", GH_ParamAccess.item);
        pManager.AddCurveParameter("A", "A", "Curves of family A", GH_ParamAccess.list);
        pManager.AddCurveParameter("B", "B", "Curves of family B (optional; needed for crossing angles)", GH_ParamAccess.list);
        pManager[2].Optional = true;
        pManager.AddNumberParameter("Spacing", "Sp", "Target spacing the shares refer to (0 = the median of the measured spacings)", GH_ParamAccess.item, 0.0);
        pManager.AddNumberParameter("Utilization", "U", "Utilization values to summarise, e.g. Lath Analysis MaxUtilization (optional)", GH_ParamAccess.list);
        pManager[4].Optional = true;
        pManager.AddIntegerParameter("Bins", "N", "Histogram bins (0 = Freedman–Diaconis: width 2·IQR·n^(−1/3))", GH_ParamAccess.item, 0);
        pManager.AddNumberParameter("Sampling", "S", "Chord deviation for curve sampling (0 = automatic from the mesh edge length)", GH_ParamAccess.item, 0.0);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddNumberParameter("Spacing", "S", "Local spacing at every sample (the tighter side): branch {0} family A, {1} family B", GH_ParamAccess.tree);
        pManager.AddPointParameter("Points", "P", "Sample points the spacings refer to (same tree)", GH_ParamAccess.tree);
        pManager.AddNumberParameter("Angles", "X", "Crossing angle at every A × B crossing, degrees (0..90)", GH_ParamAccess.list);
        pManager.AddPointParameter("Crossings", "Xp", "Crossing points the angles refer to", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Counts", "H", "Histogram counts: {0} spacing A, {1} spacing B, {2} angles, {3} utilization", GH_ParamAccess.tree);
        pManager.AddNumberParameter("Edges", "E", "Histogram bin edges, one more than the counts (same tree)", GH_ParamAccess.tree);
        pManager.AddTextParameter("Report", "R", "Summary statistics of every distribution", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var input = LoadMesh(DA, 0);
        if (input == null) return;
        var curvesA = new List<Curve>(); var curvesB = new List<Curve>(); var util = new List<double>();
        if (!DA.GetDataList(1, curvesA)) return;
        DA.GetDataList(2, curvesB);
        double target = 0, sampling = 0; int bins = 0;
        DA.GetData(3, ref target);
        DA.GetDataList(4, util);
        DA.GetData(5, ref bins);
        DA.GetData(6, ref sampling);
        if (target < 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Spacing must be 0 (median) or positive."); return; }

        var proj = new MeshProjection(input.Data);
        double chord = ResolveSampling(sampling, proj);
        List<Vec3d[]> ToLines(List<Curve> cs) => cs.Where(c => c != null).Select(c => CurveSample.ToPolyline(c, chord)).Where(p => p != null && p.Length >= 2).Select(p => p!).ToList();
        var A = ToLines(curvesA); var B = ToLines(curvesB);

        var spacingTree = new DataTree<double>(); var pointTree = new DataTree<Point3d>();
        var countTree = new DataTree<int>(); var edgeTree = new DataTree<double>();
        var report = new StringBuilder();
        var opts = new NetDistributions.SpacingOptions { Target = target };

        void AddHistogram(int branch, Distribution d, double lo = double.NaN, double hi = double.NaN)
        {
            var path = new GH_Path(branch);
            countTree.EnsurePath(path); edgeTree.EnsurePath(path);
            if (d.Count == 0) return;
            var h = d.Histogram(bins, lo, hi);
            countTree.AddRange(h.Counts, path);
            edgeTree.AddRange(h.Edges, path);
        }

        var families = new[] { ("A", A), ("B", B) };
        for (int f = 0; f < 2; f++)
        {
            var (name, lines) = families[f];
            var path = new GH_Path(f);
            spacingTree.EnsurePath(path); pointTree.EnsurePath(path);
            if (lines.Count < 2) { AddHistogram(f, new Distribution(Array.Empty<double>())); if (lines.Count == 1) report.AppendLine($"Spacing {name}: one curve, nothing to measure."); continue; }
            var r = NetDistributions.FamilySpacing(proj, lines, opts);
            for (int i = 0; i < r.Points.Length; i++)
            {
                double l = r.Left[i], rr = r.Right[i];
                double local = double.IsNaN(l) ? rr : double.IsNaN(rr) ? l : Math.Min(l, rr);
                spacingTree.Add(local, path);
                pointTree.Add(MeshConvert.ToRhinoPoint(r.Points[i]), path);
            }
            AddHistogram(f, r.Spacing);
            report.AppendLine(Summary($"Spacing {name}", r.Spacing) +
                F($" · within ±5 % of {r.Target:G4}: {100 * r.Within5:F0} %, ±7.5 %: {100 * r.Within75:F0} %") +
                (r.OpenSides > 0 ? F($" · {r.OpenSides} of {r.Points.Length} samples with an open side (border curve or gap over {r.MaxDistance:G3})") : ""));
        }

        var angles = new List<double>(); var crossings = new List<Point3d>();
        if (A.Count > 0 && B.Count > 0)
        {
            var ar = NetDistributions.CrossingAngles(A, B);
            angles.AddRange(ar.PerCrossing);
            crossings.AddRange(ar.Points.Select(MeshConvert.ToRhinoPoint));
            AddHistogram(2, ar.Angles, 0, 90);
            report.AppendLine(ar.Angles.Count > 0 ? Summary("Crossing angles (°)", ar.Angles) : "Crossing angles: the families do not cross.");
        }
        else AddHistogram(2, new Distribution(Array.Empty<double>()));

        if (util.Count > 0)
        {
            var ud = new Distribution(util);
            AddHistogram(3, ud, 0, Math.Max(1.5, ud.Max));
            report.AppendLine(Summary("Utilization", ud) + F($" · over 1: {100 * ud.ShareAbove(1):F0} %"));
        }
        else AddHistogram(3, new Distribution(Array.Empty<double>()));

        DA.SetDataTree(0, spacingTree);
        DA.SetDataTree(1, pointTree);
        DA.SetDataList(2, angles);
        DA.SetDataList(3, crossings);
        DA.SetDataTree(4, countTree);
        DA.SetDataTree(5, edgeTree);
        DA.SetData(6, report.ToString().TrimEnd());
    }

    private static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

    private static string Summary(string label, Distribution d) =>
        F($"{label}: n {d.Count} · min {d.Min:G4} · P5 {d.Quantile(0.05):G4} · median {d.Median:G4} · P95 {d.Quantile(0.95):G4} · max {d.Max:G4} · CV {100 * d.CoefficientOfVariation:F1} %");
}
