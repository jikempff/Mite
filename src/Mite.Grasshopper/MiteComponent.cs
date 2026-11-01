using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Rhino;
using Rhino.Geometry;
using Mite.Core;
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

    // ---- progress in the Message line ------------------------------------
    //
    // A solve runs on the UI thread, so nothing repaints by itself until it
    // ends. The core solvers report their progress between units of work;
    // the handler below writes it into the component's Message ("tracing A
    // 42 %") and paints the canvas at once with Instances.RedrawCanvas(),
    // which "immediately invalidates and paints the canvas" (David Rutten,
    // McNeel forum, "A very difficult problem for UI", 2019) — throttled to
    // one paint per PaintInterval so a tight loop does not spend its time
    // painting. Following Nielsen's response-time limits (Usability
    // Engineering, 1993: nothing under 1 s, percent-done past 10 s; Myers
    // 1985 for the percentage itself) nothing is shown during the first
    // second, so quick solves never flicker. The Message the component had
    // when the first report arrived (a mode word set before or during the
    // solve, say) is put back afterwards.

    /// <summary>Milliseconds between two canvas paints while a solve reports progress.</summary>
    public static int PaintInterval { get; set; } = 250;

    /// <summary>Milliseconds a solve must have run before its progress is shown.</summary>
    public static int ShowAfter { get; set; } = 1000;

    private string? _messageBeforeSolve;
    private bool _progressShown;
    private readonly Stopwatch _solveClock = new Stopwatch();
    private long _lastPaint = -1;
    private string _lastShown = "";

    protected override void BeforeSolveInstance()
    {
        base.BeforeSolveInstance();
        _progressShown = false;
        _lastPaint = -1;
        _lastShown = "";
        _solveClock.Restart();
    }

    protected override void AfterSolveInstance()
    {
        if (_progressShown)
        {
            Message = _messageBeforeSolve;
            _progressShown = false;
            try { Attributes?.ExpireLayout(); } catch { }
        }
        _solveClock.Stop();
        base.AfterSolveInstance();
    }

    /// <summary>
    /// A progress handler for one stage of this solve, covering the window
    /// [from, to] of the whole. <paramref name="stage"/> replaces the stage
    /// word the core sends (null keeps it). Pass it as the Progress option
    /// of a core solver.
    /// </summary>
    protected ProgressHandler Progress(string? stage = null, double from = 0.0, double to = 1.0)
    {
        double a = Math.Min(from, to), b = Math.Max(from, to);
        return (f, coreStage) => ShowProgress(a + (b - a) * Math.Max(0, Math.Min(1, f)), stage ?? coreStage);
    }

    /// <summary>Progress of a per-item loop in the component itself: <paramref name="done"/> of <paramref name="total"/> items.</summary>
    protected void ReportProgress(string stage, int done, int total) =>
        ShowProgress(total > 0 ? (double)done / total : 1.0, stage);

    private void ShowProgress(double fraction, string stage)
    {
        long now = _solveClock.ElapsedMilliseconds;
        if (now < ShowAfter) return;
        if (_lastPaint >= 0 && now - _lastPaint < PaintInterval && fraction < 1) return;
        string text = ProgressScope.Text(fraction, stage);
        if (text == _lastShown) return;
        if (!_progressShown) _messageBeforeSolve = Message;
        _lastShown = text;
        _lastPaint = now;
        Message = text;
        _progressShown = true;
        try
        {
            Attributes?.ExpireLayout();
            if (Instances.ActiveCanvas != null) Instances.RedrawCanvas();
        }
        catch
        {
            // no canvas (headless Rhino.Compute, a closed document): the
            // message is still set and shows when the canvas next paints
        }
    }

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
