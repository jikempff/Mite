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
using System.Linq;
using Mite.Core.Fabrication;
using Mite.Core.Gridshells;

namespace Mite.Grasshopper;

/// <summary>
/// Shared plumbing for Mite components: icon loading, welded mesh intake with
/// diagnostics, Esc cancellation, seed resolution, scale-aware sampling and
/// unit conversion.
/// </summary>
public abstract partial class MiteComponent : GH_Component
{
    private readonly string _iconResource;

    protected MiteComponent(string name, string nickname, string description, string subCategory, string iconResource)
        : base(name, nickname, description, "Mite", subCategory)
    {
        _iconResource = iconResource;
        _modeKey = name;
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

    public const string NetInputDescription =
        "Mite Net (from a net component or Mite Net): supplies the surface, the laths and — when it carries one — " +
        "the profile in one wire. Anything wired beside it (Mesh, Curves, section inputs) overrides that part of the net";

    /// <summary>Appends the optional Net input (always last, so saved definitions keep their wiring).</summary>
    protected static void RegisterNetInput(GH_InputParamManager pManager, string? extra = null)
    {
        pManager.AddParameter(new MiteNetParameter(), "Net", "Nt",
            NetInputDescription + (extra != null ? ". " + extra : ""), GH_ParamAccess.item);
        pManager[pManager.ParamCount - 1].Optional = true;
    }

    /// <summary>Appends the Net output (always last).</summary>
    protected static void RegisterNetOutput(GH_OutputParamManager pManager, string? extra = null)
    {
        pManager.AddParameter(new MiteNetParameter(), "Net", "Nt",
            "The whole net in one wire (surface + both families): plug it into Net Joints, Net Topology, Lath Sweep, " +
            "Lath Analysis, Gridshell Analysis, Lath Segment, Lath Unroll, Lath Labels or Net Kinetics; Mite Net adds a profile and reports the lath ends" +
            (extra != null ? ". " + extra : ""), GH_ParamAccess.item);
    }

    /// <summary>The net at the given input, or null when nothing is wired.</summary>
    internal static LathNet? GetNet(IGH_DataAccess DA, int index)
    {
        MiteNetGoo? goo = null;
        return DA.GetData(index, ref goo) && goo?.Value != null ? goo.Value : null;
    }

    /// <summary>True when the input has a wire (persistent values do not count).</summary>
    internal bool IsWired(int index) => Params.Input[index].SourceCount > 0;

    /// <summary>
    /// The net's profile for global lath <paramref name="lath"/> when the net
    /// carries one and none of the given section inputs is wired; otherwise null
    /// (the component's own section inputs apply).
    /// </summary>
    internal LathProfile? NetProfile(LathNet? net, int lath, params int[] sectionInputs)
    {
        if (net == null || net.ProfileA == null) return null;
        foreach (int i in sectionInputs) if (IsWired(i)) return null;
        return lath < net.LathCount ? net.ProfileOf(lath) : net.ProfileA;
    }

    /// <summary>
    /// The mesh input when wired, else the Net's surface; reports an error
    /// when neither is there and <paramref name="required"/> is set.
    /// </summary>
    internal MeshInput? LoadMeshOrNet(IGH_DataAccess DA, int index, LathNet? net, bool required = true)
    {
        var input = LoadMesh(DA, index);
        if (input != null) return input;
        if (net?.Surface != null) return MeshConvert.FromData(net.Surface);
        if (required)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                net != null ? "The Net carries no surface: wire a Mesh." : "Wire a Mesh, or a Mite Net that carries its surface.");
        return null;
    }

    /// <summary>
    /// Curves from the input when wired; else the Net's laths as exact
    /// polylines (family 0 = A, 1 = B, -1 = A then B). Returns false (with a
    /// warning when <paramref name="required"/>) when there are none.
    /// </summary>
    internal bool ReadCurvesOrNet(IGH_DataAccess DA, int index, LathNet? net, List<Curve> curves, int family, bool required = true) =>
        ReadCurvesOrNet(DA, index, net, curves, family, out _, required);

    /// <inheritdoc cref="ReadCurvesOrNet(IGH_DataAccess, int, LathNet?, List{Curve}, int, bool)"/>
    /// <param name="fromNet">True when the curves are the net's laths (so curve i is global lath i for family -1).</param>
    internal bool ReadCurvesOrNet(IGH_DataAccess DA, int index, LathNet? net, List<Curve> curves, int family, out bool fromNet, bool required = true)
    {
        fromNet = false;
        curves.Clear();
        if (DA.GetDataList(index, curves) && curves.Count > 0) return true;
        curves.Clear();
        if (net != null)
        {
            IEnumerable<Vec3d[]> laths = family == 0 ? net.FamilyA : family == 1 ? net.FamilyB : net.Laths();
            foreach (var l in laths) curves.Add(MeshConvert.ToPolylineCurve(l));
            fromNet = true;
            if (curves.Count > 0 || family == 1) return true;
        }
        if (required)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Wire the curves, or a Mite Net.");
        return false;
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

    /// <summary>The Layout input of the field-based net components: 0 fill, 1 border web, 2 cross web, 3 farthest-point fill.</summary>
    protected static NetLayout ToNetLayout(int layout) =>
        layout == 1 ? NetLayout.WebBorder : layout == 2 ? NetLayout.WebCross : layout == 3 ? NetLayout.FillFarthest : NetLayout.Fill;

    /// <summary>
    /// Warns when the evenly-spaced tracer stopped early (cap or Esc); with the
    /// projection, also checks Spacing and Step against the mesh edge and the
    /// size of the region the family can fill (<paramref name="region"/>: the
    /// usable vertices, all when null).
    /// </summary>
    protected void ReportTracing(EvenlySpacedNet.Options opts, string what, MeshProjection? proj = null, bool[]? region = null)
    {
        if (opts.Layout == NetLayout.FillFarthest && opts.RejectedSeeds > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"{what}: {opts.RejectedSeeds} farthest-point seed(s) could not grow a curve of the minimum length (2 x Spacing); those gaps stay open, up to {opts.LargestGap:G3} wide.");
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
