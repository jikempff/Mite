using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Reflection;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Rhino;
using Rhino.Geometry;
using Mite.Core.Diagnostics;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;

namespace Mite.Grasshopper;

/// <summary>
/// Shared plumbing for Mite components: icon loading, welded mesh intake with
/// diagnostics, Esc cancellation, seed resolution, scale-aware sampling and
/// unit conversion.
/// </summary>
public abstract class MiteComponent : GH_Component
{
    private readonly string _iconResource;

    protected MiteComponent(string name, string nickname, string description, string subCategory, string iconResource)
        : base(name, nickname, description, "Mite", subCategory)
    {
        _iconResource = iconResource;
    }

    protected override Bitmap? Icon
    {
        get
        {
            var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("Mite.Grasshopper.Resources." + _iconResource + ".png");
            return stream != null ? new Bitmap(stream) : null;
        }
    }

    /// <summary>True while the user holds Esc (the core solvers poll this between curves / iterations).</summary>
    protected static bool Cancelled() => GH_Document.IsEscapeKeyDown();

    /// <summary>
    /// Appends the shared Shape / Section inputs (see <see cref="SectionInput"/>)
    /// — always last, so existing definitions keep their wiring.
    /// </summary>
    protected static void RegisterSectionInputs(GH_InputParamManager pManager)
    {
        pManager.AddIntegerParameter("Shape", "Sh", SectionInput.ShapeDescription, GH_ParamAccess.item, 0);
        pManager.AddCurveParameter("Section", "Sc", SectionInput.SectionDescription, GH_ParamAccess.item);
        pManager[pManager.ParamCount - 1].Optional = true;
    }

    /// <summary>
    /// Reads and welds the mesh at the given input; reports empty meshes as
    /// errors and welded seams as a remark. Returns null when unusable.
    /// </summary>
    internal MeshInput? LoadMesh(IGH_DataAccess DA, int index, bool keepQuads = false)
    {
        Mesh? mesh = null;
        if (!DA.GetData(index, ref mesh) || mesh == null) return null;

        var input = MeshConvert.Load(mesh, keepQuads);
        if (input == null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The mesh has no faces.");
            return null;
        }
        if (input.Welded)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"Welded {input.RhinoVertexCount - input.Data.VertexCount} coincident vertices " +
                "(seams / duplicate vertices) so the algorithms see one connected surface.");
        return input;
    }

    /// <summary>
    /// Combines vertex-index seeds (original mesh indices) and point seeds
    /// into unique welded vertex indices, reporting skipped entries.
    /// </summary>
    internal List<int> ResolveSeeds(MeshInput input, MeshProjection proj,
        IReadOnlyList<int>? indices, IReadOnlyList<Point3d>? points)
    {
        var seeds = new List<int>();
        var seen = new HashSet<int>();
        var outOfRange = new List<int>();

        if (indices != null)
            foreach (int s in indices)
            {
                int t = input.ToTopo(s);
                if (t < 0) { outOfRange.Add(s); continue; }
                if (seen.Add(t)) seeds.Add(t);
            }

        if (points != null)
            for (int i = 0; i < points.Count; i++)
            {
                int t = SeedPointVertex(proj, points[i], i);
                if (t < 0) continue;
                if (seen.Add(t)) seeds.Add(t);
            }

        Report(InputAdvice.SeedIndices(outOfRange, input.RhinoVertexCount));
        return seeds;
    }

    /// <summary>
    /// Nearest welded vertex of a seed point; adds a note (with the distance)
    /// when the point lies well off the mesh.
    /// </summary>
    internal int SeedPointVertex(MeshProjection proj, Point3d point, int index)
    {
        var p = MeshConvert.ToVec3d(point);
        int t = proj.NearestVertexGlobal(p);
        if (t < 0) return t;
        var hit = proj.ClosestPoint(p, t);
        Report(InputAdvice.SeedPoint(index, (hit.Point - p).Length, proj.AverageEdgeLength, proj.Mesh.BoundingBoxDiagonal()));
        return t;
    }

    /// <summary>Adds an advice as a runtime message of its level, once per solve.</summary>
    internal void Report(Advice? advice)
    {
        if (advice == null) return;
        var level = advice.Level == AdviceLevel.Error ? GH_RuntimeMessageLevel.Error
            : advice.Level == AdviceLevel.Warning ? GH_RuntimeMessageLevel.Warning
            : GH_RuntimeMessageLevel.Remark;
        if (RuntimeMessages(level).Contains(advice.Message)) return;
        AddRuntimeMessage(level, advice.Message);
    }

    /// <summary>Adds every advice of the list (see <see cref="Report(Advice?)"/>).</summary>
    internal void Report(IEnumerable<Advice> advice)
    {
        foreach (var a in advice) Report(a);
    }

    /// <summary>
    /// Checks a lath section against the lengths of the laths it is applied
    /// to, so unit slips (millimetres in a metre model) are named with the
    /// value to enter. Thickness NaN for components with a width only.
    /// </summary>
    internal void CheckSection(double width, double thickness, IEnumerable<Curve?> curves)
    {
        var lengths = new List<double>();
        foreach (var c in curves) if (c != null && c.IsValid) lengths.Add(c.GetLength());
        var doc = RhinoDoc.ActiveDoc;
        string unit = doc != null ? doc.ModelUnitSystem.ToString() : "model units";
        Report(InputAdvice.SectionScale(width, thickness, lengths, ModelToMeters(), unit));
    }

    /// <summary>
    /// Warns when the evenly-spaced tracer stopped early (cap or Esc); with the
    /// projection, also checks Spacing and Step against the mesh edge and the
    /// size of the region the family can fill (<paramref name="region"/>: the
    /// usable vertices, all when null).
    /// </summary>
    protected void ReportTracing(EvenlySpacedNet.Options opts, string what, MeshProjection? proj = null, bool[]? region = null)
    {
        if (opts.Cancelled)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"{what}: cancelled with Esc, output is partial.");
        if (opts.ReachedMaxCurves)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{what}: stopped at the MaxCurves cap ({opts.MaxCurves}); increase MaxCurves or Spacing for full coverage.");
        if (proj != null)
            Report(InputAdvice.Spacing(opts.ResolvedSpacing, opts.Spacing > 0, opts.ResolvedStepSize, opts.StepSize > 0,
                proj.AverageEdgeLength, InputAdvice.RegionSize(proj.Mesh, region)));
    }

    /// <summary>Chord tolerance for sampling curves onto a mesh (0 = automatic, 2% of the average edge length).</summary>
    protected static double ResolveSampling(double sampling, MeshProjection proj) =>
        sampling > 0 ? sampling : Math.Max(0.02 * proj.AverageEdgeLength, 1e-9);

    /// <summary>Factor converting model units to metres for SI material inputs.</summary>
    protected static double ModelToMeters()
    {
        var doc = RhinoDoc.ActiveDoc;
        if (doc == null) return 1.0;
        double scale = RhinoMath.UnitScale(doc.ModelUnitSystem, UnitSystem.Meters);
        return scale > 0 && !double.IsNaN(scale) ? scale : 1.0;
    }

    /// <summary>Output path for branch i that respects the input's tree structure.</summary>
    protected static GH_Path BranchPath(IGH_DataAccess DA, int i) =>
        DA.ParameterTargetPath(0).AppendElement(i);
}
