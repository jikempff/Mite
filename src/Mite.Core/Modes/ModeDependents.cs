// Inputs that only take effect in one mode, and the note that says so when
// they are set while the mode is off (ROADMAP B, runtime messages follow-up
// (e): "Spacing has no effect without AutoSpace"-type notes from the
// catalogue's dependencies).
//
// Norman 1983 (Design rules based on analyses of human error, Commun. ACM
// 26(4)): mode errors come from acting as if the system were in another mode
// than it is; the remedies are feedback of the state and a warning when an
// action does not apply in the current mode. Nielsen 1994 (heuristics 1 and
// 5): visibility of system status, error prevention — tell the user before
// they look for the effect. Shneiderman 1982 / Neusesser & Sunwall 2023 for
// the wording: what is ignored, under which setting it would apply, the two
// ways out. The dependencies of the mode inputs themselves are already in
// ModeCatalog (ActiveWhenInput); this table adds the numeric and geometry
// inputs that hang on a mode, with the registered default so a value left at
// its default stays silent.
using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Diagnostics;

namespace Mite.Core.Modes;

/// <summary>An input (number, integer or geometry) that is used only while a mode input has one of the given values.</summary>
public sealed class ModeDependent
{
    public ModeDependent(string input, string activeWhenInput, int[] activeWhenValues, double? neutral, string neutralText)
    {
        Input = input; ActiveWhenInput = activeWhenInput; ActiveWhenValues = activeWhenValues;
        Neutral = neutral; NeutralText = neutralText;
    }

    /// <summary>The input's registered name.</summary>
    public string Input { get; }
    /// <summary>The mode input that enables it (catalogued on the same component).</summary>
    public string ActiveWhenInput { get; }
    /// <summary>The values of that mode under which the input is read.</summary>
    public int[] ActiveWhenValues { get; }
    /// <summary>The registered default (null for a geometry input: it counts as set when wired).</summary>
    public double? Neutral { get; }
    /// <summary>How the default reads in the message ("0 (automatic)").</summary>
    public string NeutralText { get; }
}

public static partial class ModeCatalog
{
    private static ModeDependent Dep(string input, string when, int value, double? neutral, string? neutralText = null) =>
        new(input, when, new[] { value }, neutral, neutralText ?? (neutral.HasValue ? neutral.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "unwired"));

    private static ModeDependent Dep(string input, string when, int[] values, double? neutral, string? neutralText = null) =>
        new(input, when, values, neutral, neutralText ?? (neutral.HasValue ? neutral.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "unwired"));

    // The section curve is read only for Shape 2 (SectionInput.TryBuild).
    private static readonly ModeDependent SectionDep = Dep("Section", "Shape", 2, null);

    private static ModeDependent[] AutoSpaced(params ModeDependent[] more) =>
        new[] { Dep("Spacing", "AutoSpace", 1, 0, "0 (automatic)"), Dep("MaxCurves", "AutoSpace", 1, 200) }.Concat(more).ToArray();

    private static readonly Dictionary<string, ModeDependent[]> Dependents = new()
    {
        ["Curvature Streamlines"] = AutoSpaced(),
        ["Asymptotic Net"] = AutoSpaced(),
        ["Geodesic Net"] = AutoSpaced(Dep("BorderAngle", "FromBorder", 1, 0, "0 (perpendicular to the border)")),
        ["Asymptotic Web"] = new[]
        {
            Dep("Iterations", "AAG", 1, 16),
            Dep("Proximity", "AAG", 1, 0.3),
            Dep("Angle", "AAG", 1, 0, "0 (free)"),
        },
        ["Net Kinetics"] = new[]
        {
            Dep("Angle", "Deploy", 1, 90),
            Dep("Kick", "Deploy", new[] { 1, 2 }, 0.5),
            SectionDep,
        },
        ["Lath Sweep"] = new[] { SectionDep },
        ["Lath Analysis"] = new[] { SectionDep },
        ["Gridshell Analysis"] = new[] { SectionDep },
        ["Mite Net"] = new[] { SectionDep },
    };

    /// <summary>The mode-dependent inputs of a component (empty when it has none).</summary>
    public static IReadOnlyList<ModeDependent> DependentsOf(string component) =>
        Dependents.TryGetValue(component, out var d) ? d : Array.Empty<ModeDependent>();

    /// <summary>Names of the components that have mode-dependent inputs.</summary>
    public static IReadOnlyList<string> ComponentsWithDependents => Dependents.Keys.ToList();

    /// <summary>
    /// True when the dependent input is read with the given values: its mode
    /// has one of the enabling values and is itself active.
    /// </summary>
    public static bool IsActive(string component, ModeDependent dep, Func<string, int?> valueOf)
    {
        var ctrl = For(component).FirstOrDefault(m => m.Input == dep.ActiveWhenInput);
        if (ctrl == null) return true;
        if (!IsActive(component, ctrl, valueOf)) return false;
        int cv = valueOf(ctrl.Input) ?? ctrl.Default;
        return cv == Mixed || Array.IndexOf(dep.ActiveWhenValues, cv) >= 0;
    }

    /// <summary>
    /// Remarks for every input that is set but not read in the current
    /// modes: a mode input holding a value other than its default while its
    /// controlling mode is off, and a dependent input that <paramref name="isSet"/>
    /// (wired, or typed away from its default). Each names the setting under
    /// which the input applies and the two ways out.
    /// </summary>
    /// <param name="valueOf">Current value of a mode input (null = unknown, reads as the default).</param>
    /// <param name="isSet">True when the named dependent input carries a value the user chose.</param>
    public static List<Advice> InactiveInputs(string component, Func<string, int?> valueOf, Func<string, bool> isSet)
    {
        var list = new List<Advice>();
        var modes = For(component);
        foreach (var m in modes)
        {
            if (m.ActiveWhenInput == null || IsActive(component, m, valueOf)) continue;
            int v = valueOf(m.Input) ?? m.Default;
            if (v == m.Default) continue;
            var ctrl = modes.First(c => c.Input == m.ActiveWhenInput);
            string now = v == Mixed ? "several values" : "\"" + m.LabelFor(v) + "\"";
            list.Add(new Advice("input-not-used-in-mode", AdviceLevel.Remark,
                $"{m.Input} {now} is not used now: it applies only while {ctrl.Input} is \"{ctrl.LabelFor(m.ActiveWhenValue)}\" " +
                $"(it is {Describe(component, ctrl, valueOf)}). Set {ctrl.Input} to \"{ctrl.LabelFor(m.ActiveWhenValue)}\" to use it, " +
                $"or leave {m.Input} at \"{m.LabelFor(m.Default)}\".",
                v, m.Default, m.Default));
        }
        foreach (var d in DependentsOf(component))
        {
            if (IsActive(component, d, valueOf) || !isSet(d.Input)) continue;
            var ctrl = modes.FirstOrDefault(c => c.Input == d.ActiveWhenInput);
            if (ctrl == null) continue;
            string when = string.Join("\" or \"", d.ActiveWhenValues.Select(ctrl.LabelFor));
            list.Add(new Advice("input-not-used-in-mode", AdviceLevel.Remark,
                $"{d.Input} is not used now: it applies only while {ctrl.Input} is \"{when}\" " +
                $"(it is {Describe(component, ctrl, valueOf)}). Set {ctrl.Input} to \"{when}\" to use it, " +
                $"or leave {d.Input} {(d.Neutral.HasValue ? "at " + d.NeutralText : "unwired")}.",
                double.NaN, double.NaN, d.Neutral ?? double.NaN));
        }
        return list;
    }

    /// <summary>"\"Seeds only\"" for an active mode, or the mode it waits for when it is itself inactive.</summary>
    private static string Describe(string component, ModeInput ctrl, Func<string, int?> valueOf)
    {
        if (!IsActive(component, ctrl, valueOf))
        {
            var up = For(component).First(c => c.Input == ctrl.ActiveWhenInput);
            return $"itself not used while {up.Input} is {Describe(component, up, valueOf)}";
        }
        int v = valueOf(ctrl.Input) ?? ctrl.Default;
        return v == Mixed ? "mixed" : "\"" + ctrl.LabelFor(v) + "\"";
    }
}
