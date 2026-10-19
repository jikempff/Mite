using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Rhino;
using Rhino.Geometry;
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
        int skipped = 0;

        if (indices != null)
            foreach (int s in indices)
            {
                int t = input.ToTopo(s);
                if (t < 0) { skipped++; continue; }
                if (seen.Add(t)) seeds.Add(t);
            }

        if (points != null)
            foreach (var p in points)
            {
                int t = proj.NearestVertexGlobal(MeshConvert.ToVec3d(p));
                if (t < 0) { skipped++; continue; }
                if (seen.Add(t)) seeds.Add(t);
            }

        if (skipped > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"{skipped} out-of-range seed(s) skipped.");
        return seeds;
    }

    /// <summary>
    /// Registers the optional Guide input of the net components (appended
    /// last so saved definitions keep their wiring).
    /// </summary>
    protected static void RegisterGuideInput(GH_InputParamManager pManager, string family)
    {
        int i = pManager.AddCurveParameter("Guide", "Gd",
            $"Guide (seed) curve: when given, {family} is a web seeded from it instead of the border or the seed cross, whatever Layout says. " +
            "The curve is pulled onto the mesh and nodes are placed every Spacing of arc length along it (centred on an open curve; " +
            "L / round(L / Spacing) apart on a closed one); one curve runs through every node, border to border, so both families of a net " +
            "cross at every node — Schling's layout (the node distance along the seed curve is the repeated parameter). Neighbouring curves " +
            "are then Spacing × sin(angle to the guide) apart. A guide along one family's own curve seeds only the other family. " +
            "Geodesics leave the guide with the first Direction carried along it by parallel transport (Pottmann et al. 2010). " +
            "Gaps elsewhere are filled as for the other webs.", GH_ParamAccess.item);
        pManager[i].Optional = true;
    }

    /// <summary>Reads the Guide input as a polyline for the core (null when unset).</summary>
    protected Vec3d[]? ReadGuide(IGH_DataAccess DA, int index, MeshProjection proj)
    {
        Curve? guide = null;
        if (!DA.GetData(index, ref guide) || guide == null) return null;
        var line = CurveSample.ToPolyline(guide, ResolveSampling(0, proj));
        if (line == null || line.Length < 2)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Guide curve could not be sampled; using Layout instead.");
            return null;
        }
        double len = 0;
        for (int i = 1; i < line.Length; i++) len += (line[i] - line[i - 1]).Length;
        if (len < 1e-9 * proj.AverageEdgeLength) return null;
        return line;
    }

    /// <summary>Remark on where the guide nodes landed (count and spacing).</summary>
    protected void ReportGuide(EvenlySpacedNet.Options opts, string family)
    {
        if (opts.Guide == null) return;
        if (opts.GuideNodes.Count == 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"{family}: the guide is shorter than Spacing or lies outside the usable region; no node could be placed.");
        else
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"{family}: {opts.GuideNodes.Count} node(s) along the guide, {opts.ResolvedSpacing:G4} apart.");
    }

    /// <summary>Warns when the evenly-spaced tracer stopped early (cap or Esc).</summary>
    protected void ReportTracing(EvenlySpacedNet.Options opts, string what)
    {
        if (opts.Cancelled)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"{what}: cancelled with Esc, output is partial.");
        if (opts.ReachedMaxCurves)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{what}: stopped at the MaxCurves cap ({opts.MaxCurves}); increase MaxCurves or Spacing for full coverage.");
    }

    /// <summary>Chord tolerance for sampling curves onto a mesh (0 = automatic, 2% of the average edge length).</summary>
    /// <summary>Align input (0 outside, 1 centred, 2 inside) → <see cref="Mite.Core.Fabrication.LathAlign"/>.</summary>
    protected static Mite.Core.Fabrication.LathAlign AlignOf(int align) => align switch
    {
        1 => Mite.Core.Fabrication.LathAlign.Centred,
        2 => Mite.Core.Fabrication.LathAlign.Inside,
        _ => Mite.Core.Fabrication.LathAlign.Outside,
    };

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
