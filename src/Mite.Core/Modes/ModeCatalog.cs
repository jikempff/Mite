// Mode menus for the Grasshopper components.
//
// Sources:
// - Norman 1983, "Design rules based on analyses of human error" (Commun. ACM
//   26(4)): mode errors happen "in systems that do not provide clear feedback
//   of their current state"; remedies are to remove modes, to mark each mode
//   distinctly, or to use different commands per mode.
// - Raskin 2000, The Humane Interface, ch. 3: an interface is not modal "as
//   long as the user is fully aware of its current state" — the state has to
//   sit at the locus of attention (here: on the component itself).
// - McNeel, "Custom Component Options" (developer.rhino3d.com): a menu item
//   per option, RecordUndoEvent before the change, ExpireSolution after, and
//   the component Message to show options that are otherwise hidden.
//
// Mite keeps every mode as a plain input (so saved definitions and wires keep
// working); this catalogue is the single description of what each value
// means. The Grasshopper layer turns it into a right-click menu that writes
// the input's own value, a Message line under the component, and value lists
// on demand; the tests check it against the component sources.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mite.Core.Modes;

/// <summary>How a mode input is typed in Grasshopper.</summary>
public enum ModeKind
{
    /// <summary>A Boolean input; option values are 0 (false) and 1 (true).</summary>
    Boolean,
    /// <summary>An Integer input; option values are the integers themselves.</summary>
    Integer,
}

/// <summary>One choice of a mode input.</summary>
public sealed class ModeOption
{
    public ModeOption(int value, string label, string shortLabel, string hint = "")
    {
        Value = value;
        Label = label;
        Short = shortLabel;
        Hint = hint;
    }

    /// <summary>The input value (0/1 for Boolean inputs).</summary>
    public int Value { get; }

    /// <summary>Menu text, e.g. "Web from the border".</summary>
    public string Label { get; }

    /// <summary>Text shown in the component's Message line; empty = say nothing for this value.</summary>
    public string Short { get; }

    /// <summary>Tooltip of the menu item.</summary>
    public string Hint { get; }
}

/// <summary>A component input whose values are modes rather than quantities.</summary>
public sealed class ModeInput
{
    public ModeInput(string input, ModeKind kind, int defaultValue, IReadOnlyList<ModeOption> options,
        string? activeWhenInput = null, int activeWhenValue = 1, string? otherShort = null)
    {
        Input = input;
        Kind = kind;
        Default = defaultValue;
        Options = options;
        ActiveWhenInput = activeWhenInput;
        ActiveWhenValue = activeWhenValue;
        OtherShort = otherShort;
    }

    /// <summary>Input name (Grasshopper parameter Name, not the nickname).</summary>
    public string Input { get; }

    public ModeKind Kind { get; }

    /// <summary>The input's registered default value.</summary>
    public int Default { get; }

    public IReadOnlyList<ModeOption> Options { get; }

    /// <summary>The mode only matters while this other mode input has <see cref="ActiveWhenValue"/>.</summary>
    public string? ActiveWhenInput { get; }

    public int ActiveWhenValue { get; }

    /// <summary>
    /// Format for values outside <see cref="Options"/> ({0} = the value), for
    /// open-ended inputs such as Symmetry (n = force n-fold); null = the input
    /// is a closed set and other values are reported as "Name = value?".
    /// </summary>
    public string? OtherShort { get; }

    /// <summary>The option with this value, or null.</summary>
    public ModeOption? Find(int value) => Options.FirstOrDefault(o => o.Value == value);

    /// <summary>Message text for one value (may be empty).</summary>
    public string ShortFor(int value)
    {
        var o = Find(value);
        if (o != null) return o.Short;
        return OtherShort != null
            ? string.Format(System.Globalization.CultureInfo.InvariantCulture, OtherShort, value)
            : Input + " = " + value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "?";
    }

    /// <summary>Menu text for one value.</summary>
    public string LabelFor(int value)
    {
        var o = Find(value);
        if (o != null) return o.Label;
        return OtherShort != null
            ? string.Format(System.Globalization.CultureInfo.InvariantCulture, OtherShort, value)
            : value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>The value as Grasshopper reads it from a value-list expression ("True"/"False" or the integer).</summary>
    public string Expression(int value) => Kind == ModeKind.Boolean
        ? (value != 0 ? "True" : "False")
        : value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Every mode input of the Mite components, keyed by the component's
/// Grasshopper name. Inputs keep their registered type and default; only
/// their presentation changes.
/// </summary>
public static class ModeCatalog
{
    private static ModeInput Bool(string input, bool def, ModeOption off, ModeOption on,
        string? activeWhen = null) =>
        new(input, ModeKind.Boolean, def ? 1 : 0, new[] { off, on }, activeWhen, 1);

    private static ModeOption O(int v, string label, string shortLabel, string hint = "") => new(v, label, shortLabel, hint);

    // Shared definitions (the same input means the same thing everywhere).
    private static ModeInput Layout(string? activeWhen) => new("Layout", ModeKind.Integer, 0, new[]
    {
        O(0, "Evenly spaced fill", "Fill", "Curves inserted and stopped to keep the spacing (T-junctions)"),
        O(1, "Web from the border", "Border web", "Seeds every Spacing along the border; every curve runs border to border"),
        O(2, "Web from the seed cross", "Cross web", "Seeds every Spacing along the crossing curve through the seed"),
    }, activeWhen, 1);

    private static ModeInput Continuous(string? activeWhen) => Bool("Continuous", true,
        O(0, "Classic (stop at 0.4 × Spacing)", "Classic", "Traces stop near a neighbour: more even spacing, laths end mid-surface"),
        O(1, "Continuous (run to the border)", "Continuous", "Traces run on until they practically merge with a neighbour"),
        activeWhen);

    private static ModeInput AutoSpace(bool def, string fillHint) => Bool("AutoSpace", def,
        O(0, "Seeds only", "Seeds", "Trace only the curves through the given seeds"),
        O(1, "Fill the surface", "", fillHint));

    private static ModeInput Upright(bool def, string flat = "Flat on the surface", string upright = "Upright on edge") => Bool("Upright", def,
        O(0, flat, "Flat", "Lath width lies in the tangent plane (geodesic gridshells)"),
        O(1, upright, "Upright", "Lath width along the surface normal (asymptotic gridshells)"));

    private static readonly ModeInput Shape = new("Shape", ModeKind.Integer, 0, new[]
    {
        O(0, "Rectangle W × T", "Rectangle"),
        O(1, "Round bar, diameter W", "Round"),
        O(2, "Custom Section curve", "Custom section", "Closed planar curve wired to Section"),
    });

    private static readonly Dictionary<string, ModeInput[]> Table = new()
    {
        ["Curvature Streamlines"] = new[]
        {
            Bool("MaxDir", true, O(0, "Min curvature direction", "Min"), O(1, "Max curvature direction", "Max")),
            AutoSpace(false, "Evenly spaced lines grown from the first seed"),
            Layout("AutoSpace"),
            Continuous("AutoSpace"),
        },
        ["Asymptotic Net"] = new[]
        {
            AutoSpace(true, "Evenly spaced curves over the whole anticlastic region"),
            Layout("AutoSpace"),
            Continuous("AutoSpace"),
        },
        ["Asymptotic Web"] = new[]
        {
            new ModeInput("Symmetry", ModeKind.Integer, -1, new[]
            {
                O(-1, "Detect", "Auto symmetry", "A rotation about the seed normal or a surface of revolution"),
                O(0, "None", "No symmetry"),
                O(2, "2-fold", "2-fold"), O(3, "3-fold", "3-fold"), O(4, "4-fold", "4-fold"), O(6, "6-fold", "6-fold"),
            }, otherShort: "{0}-fold"),
            Bool("AAG", false, O(0, "Asymptotic web", ""), O(1, "AAG web (optimised)", "AAG",
                "Geodesic diagonals and planar node stars; the surface moves slightly")),
            new ModeInput("Diagonal", ModeKind.Integer, 0, new[]
            {
                O(0, "Straighter diagonal geodesic", ""),
                O(1, "Other diagonal geodesic", "Other diagonal"),
            }, "AAG", 1),
            new ModeInput("Refine", ModeKind.Integer, 0, new[]
            {
                O(0, "Joint-to-joint chords", ""),
                O(1, "Refine ×2 (halves)", "Refine ×2", "Every web edge split in two and re-optimised"),
                O(2, "Refine ×4 (quarters)", "Refine ×4", "The paper's choice, about five times slower"),
            }, "AAG", 1),
        },
        ["Geodesic Net"] = new[]
        {
            AutoSpace(false, "Evenly spaced geodesics of one family from the first seed and direction"),
            Layout("AutoSpace"),
            Bool("FromBorder", false, O(0, "Grow from the seed", ""), O(1, "Start on the border", "From border",
                "Geodesics leave the border edge nearest the seed every Spacing"), "AutoSpace"),
            Continuous("AutoSpace"),
        },
        ["Conjugate Net"] = new[]
        {
            Layout(null),
            Continuous(null),
        },
        ["Lath Analysis"] = new[] { Upright(false), Shape },
        ["Gridshell Analysis"] = new[]
        {
            Upright(false),
            Shape,
            new ModeInput("SupportType", ModeKind.Integer, 0, new[]
            {
                O(0, "Fixed supports (6 DOF held)", "Fixed"),
                O(1, "Pinned supports (rotations free)", "Pinned"),
            }),
        },
        ["Lath Sweep"] = new[] { Upright(false), Shape },
        ["Net Joints"] = new[] { Upright(false, "Flat (lap notch)", "Upright (egg-crate slot)") },
        ["Lath Unroll"] = new[] { Upright(false) },
        ["Lath Segment"] = new[] { Upright(false) },
        ["Net Kinetics"] = new[]
        {
            Upright(true), Shape,
            Bool("Flatten", false, O(0, "Drive the net", ""), O(1, "Press flat", "Flat",
                "The anchor joint is held and every lath end slides onto its plane as Fold goes 0 → 1")),
        },
        ["Flat Kit"] = new[]
        {
            Upright(true),
            Bool("Write", false, O(0, "Preview only", ""), O(1, "Write the files to Folder", "Write")),
        },
        ["Lath Strip"] = new[] { Upright(false) },
        ["Mite Net"] = new[] { Upright(false), Shape },
        ["Geodesic Path"] = new[]
        {
            new ModeInput("Method", ModeKind.Integer, 1, new[]
            {
                O(0, "Curve shortening (approximate)", "Curve shortening", "Dijkstra, then Laplacian smoothing: the only method up to 1.3.1"),
                O(1, "Exact geodesic (edge flips)", "Exact", "FlipOut (Sharp & Crane 2020) from a Steiner-point path"),
            }),
        },
        ["Mesh Isocurves"] = new[] { Bool("Smooth", false, O(0, "Polylines", "Polylines"), O(1, "Smooth curves", "Smooth")) },
        ["Mesh Cleanup"] = new[] { Bool("UnifyWinding", true, O(0, "Keep face winding", ""), O(1, "Unify face winding", "Unify winding")) },
        ["Mesh Colour Map"] = new[]
        {
            new ModeInput("Palette", ModeKind.Integer, 0, new[]
            {
                O(0, "Diverging (centred on 0)", "Diverging"),
                O(1, "Sequential", "Sequential"),
                O(2, "Traffic light", "Traffic light"),
            }),
        },
    };

    /// <summary>Names of the components that have mode inputs.</summary>
    public static IReadOnlyList<string> Components => Table.Keys.ToList();

    /// <summary>The mode inputs of a component (empty when it has none).</summary>
    public static IReadOnlyList<ModeInput> For(string component) =>
        Table.TryGetValue(component, out var m) ? m : Array.Empty<ModeInput>();

    /// <summary>
    /// True when the mode matters with the given values (its controlling
    /// input, if any, has the enabling value). <paramref name="valueOf"/>
    /// returns null for an unknown value, which counts as the default.
    /// </summary>
    public static bool IsActive(string component, ModeInput mode, Func<string, int?> valueOf)
    {
        if (mode.ActiveWhenInput == null) return true;
        var ctrl = For(component).FirstOrDefault(m => m.Input == mode.ActiveWhenInput);
        if (ctrl == null) return true;
        if (!IsActive(component, ctrl, valueOf)) return false;
        int cv = valueOf(ctrl.Input) ?? ctrl.Default;
        return cv == Mixed || cv == mode.ActiveWhenValue;
    }

    /// <summary>
    /// The component's Message line: the short labels of the active modes,
    /// in catalogue order (the layout before its refinements, which is also
    /// the menu order), joined by " · ". A null from <paramref name="valueOf"/>
    /// reads as the default; <see cref="Mixed"/> marks an input that carries
    /// several different values.
    /// </summary>
    public static string Message(string component, Func<string, int?> valueOf)
    {
        var parts = new List<string>();
        foreach (var m in For(component))
        {
            if (!IsActive(component, m, valueOf)) continue;
            int v = valueOf(m.Input) ?? m.Default;
            string s = v == Mixed ? m.Input + " mixed" : m.ShortFor(v);
            if (s.Length > 0) parts.Add(s);
        }
        return string.Join(" · ", parts);
    }

    /// <summary>Sentinel value for an input that receives several different values.</summary>
    public const int Mixed = int.MinValue;
}
