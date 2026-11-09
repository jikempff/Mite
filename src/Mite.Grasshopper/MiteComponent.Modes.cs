// Mode menus (ROADMAP B): every mode input listed in Mite.Core.Modes.ModeCatalog
// gets a right-click submenu that writes the input's own value, a Message line
// under the component that names the active modes, and an "Attach value lists"
// command. The inputs themselves are unchanged, so saved definitions, wires and
// typed values keep working.
//
// Sources: Norman 1983 (Commun. ACM 26(4)) — mode errors come from missing
// feedback of the current state, so each mode is marked on the component;
// Raskin 2000 (The Humane Interface, ch. 3) — the state belongs at the locus of
// attention; McNeel, "Custom Component Options" — menu item, RecordUndoEvent,
// ExpireSolution, Message.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using Mite.Core.Modes;

namespace Mite.Grasshopper;

public abstract partial class MiteComponent
{
    private readonly string _modeKey;

    /// <summary>The catalogued mode inputs of this component.</summary>
    internal IReadOnlyList<ModeInput> Modes => ModeCatalog.For(_modeKey);

    private IGH_Param? FindInput(string name) => Params.Input.FirstOrDefault(p => p.Name == name);

    /// <summary>The mode line shown under the component after every solution (called from AfterSolveInstance, once the progress text is cleared).</summary>
    private void ShowModes()
    {
        if (Modes.Count > 0)
        {
            Message = ModeCatalog.Message(_modeKey, CurrentValue);
            // An input set for a mode that is off gets a remark naming the mode it waits for
            Report(ModeCatalog.InactiveInputs(_modeKey, CurrentValue, IsSetByUser));
        }
    }

    /// <summary>
    /// True when the named input carries a value the user chose: a wire, or a
    /// typed value other than the registered default in the catalogue.
    /// </summary>
    private bool IsSetByUser(string input)
    {
        var p = FindInput(input);
        if (p == null) return false;
        if (p.SourceCount > 0) return true;
        var dep = ModeCatalog.DependentsOf(_modeKey).FirstOrDefault(d => d.Input == input);
        if (dep == null || !dep.Neutral.HasValue) return false;
        IEnumerable<IGH_Goo> data = p is GH_PersistentParam<GH_Number> pn ? pn.PersistentData.AllData(true)
            : p is GH_PersistentParam<GH_Integer> pi ? pi.PersistentData.AllData(true)
            : Enumerable.Empty<IGH_Goo>();
        foreach (var goo in data)
        {
            double v = goo switch { GH_Number n => n.Value, GH_Integer i => i.Value, _ => double.NaN };
            if (!double.IsNaN(v) && Math.Abs(v - dep.Neutral.Value) > 1e-12 * Math.Max(1, Math.Abs(dep.Neutral.Value))) return true;
        }
        return false;
    }

    /// <summary>
    /// The value an input carries now: its typed (persistent) value when
    /// unwired, otherwise what arrived on the wire; <see cref="ModeCatalog.Mixed"/>
    /// when several different values arrive; null when nothing is known yet.
    /// </summary>
    private int? CurrentValue(string input)
    {
        var p = FindInput(input);
        if (p == null) return null;
        var values = new HashSet<int>();
        IEnumerable<IGH_Goo> data = p.SourceCount == 0 && p is GH_PersistentParam<GH_Boolean> pb
            ? pb.PersistentData.AllData(true)
            : p.SourceCount == 0 && p is GH_PersistentParam<GH_Integer> pi
                ? pi.PersistentData.AllData(true)
                : p.VolatileData.AllData(true);
        foreach (var goo in data)
            if (TryModeValue(goo, out int v)) values.Add(v);
        if (values.Count == 0) return null;
        return values.Count > 1 ? ModeCatalog.Mixed : values.First();
    }

    private static bool TryModeValue(IGH_Goo? goo, out int value)
    {
        value = 0;
        switch (goo)
        {
            case GH_Boolean b: value = b.Value ? 1 : 0; return true;
            case GH_Integer i: value = i.Value; return true;
            case GH_Number n: value = (int)Math.Round(n.Value); return true;
            default: return goo != null && goo.CastTo(out value);
        }
    }

    /// <summary>The value list feeding this input, when it is the only source.</summary>
    private static GH_ValueList? SingleValueList(IGH_Param p) =>
        p.SourceCount == 1 ? p.Sources[0] as GH_ValueList : null;

    /// <summary>Reads a value-list expression ("True", "False", "1", "-1") as a mode value.</summary>
    internal static int? ParseExpression(string? expression)
    {
        if (expression == null) return null;
        string e = expression.Trim().Trim('"');
        if (string.Equals(e, "true", StringComparison.OrdinalIgnoreCase)) return 1;
        if (string.Equals(e, "false", StringComparison.OrdinalIgnoreCase)) return 0;
        return int.TryParse(e, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : null;
    }

    protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalComponentMenuItems(menu);
        var modes = Modes;
        if (modes.Count == 0) return;
        Menu_AppendSeparator(menu);

        bool anyUnwired = false;
        foreach (var mode in modes)
        {
            var p = FindInput(mode.Input);
            if (p == null) continue;
            int? cur = CurrentValue(mode.Input);
            var list = SingleValueList(p);
            bool settable = p.SourceCount == 0 || list != null;
            anyUnwired |= p.SourceCount == 0;

            string now = cur == ModeCatalog.Mixed ? "mixed" : mode.LabelFor(cur ?? mode.Default);
            var head = Menu_AppendItem(menu, mode.Input + ": " + now);
            if (!ModeCatalog.IsActive(_modeKey, mode, CurrentValue))
            {
                var ctrl = modes.First(m => m.Input == mode.ActiveWhenInput);
                head.Text += "  (not used now)";
                head.ToolTipText = $"Only used when {ctrl.Input} is \"{ctrl.LabelFor(mode.ActiveWhenValue)}\".";
            }
            else
            {
                head.ToolTipText = p.Description;
            }

            foreach (var option in mode.Options)
            {
                int value = option.Value;
                // A user's own value list may lack an option: that item stays greyed.
                bool can = p.SourceCount == 0 ||
                           (list != null && list.ListItems.Any(i => ParseExpression(i.Expression) == value));
                var item = Menu_AppendItem(head.DropDown, option.Label, (_, _) => SetMode(mode, value),
                    can, (cur ?? mode.Default) == value);
                if (option.Hint.Length > 0) item.ToolTipText = option.Hint;
            }
            if (cur.HasValue && cur != ModeCatalog.Mixed && mode.Find(cur.Value) == null)
                Menu_AppendItem(head.DropDown, mode.LabelFor(cur.Value) + " (typed value)", null, false, true);
            if (!settable)
            {
                Menu_AppendSeparator(head.DropDown);
                Menu_AppendItem(head.DropDown, "Set by a wire: change the source", null, false);
            }
        }

        var attach = Menu_AppendItem(menu, "Attach value lists", (_, _) => AttachValueLists(), anyUnwired);
        attach.ToolTipText = "Wire a named drop-down list into every unwired mode input, set to its current value.";
    }

    /// <summary>Writes a mode: the input's typed value when unwired, the selection of a feeding value list otherwise.</summary>
    private void SetMode(ModeInput mode, int value)
    {
        var p = FindInput(mode.Input);
        if (p == null) return;
        string undo = "Mite " + mode.Input;
        var doc = OnPingDocument();
        if (p.SourceCount == 0)
        {
            switch (p)
            {
                case GH_PersistentParam<GH_Boolean> pb:
                    doc?.UndoUtil.RecordPersistentDataEvent(undo, pb);
                    pb.PersistentData.Clear();
                    pb.SetPersistentData(new GH_Boolean(value != 0));
                    break;
                case GH_PersistentParam<GH_Integer> pi:
                    doc?.UndoUtil.RecordPersistentDataEvent(undo, pi);
                    pi.PersistentData.Clear();
                    pi.SetPersistentData(new GH_Integer(value));
                    break;
                default:
                    return;
            }
            p.ExpireSolution(true);
            return;
        }

        var list = SingleValueList(p);
        if (list == null) return;
        int index = list.ListItems.FindIndex(item => ParseExpression(item.Expression) == value);
        if (index < 0) return;
        list.RecordUndoEvent(undo);
        list.SelectItem(index);
        list.ExpireSolution(true);
    }

    /// <summary>Adds a drop-down value list, set to the current value, to every unwired mode input (one undo step).</summary>
    private void AttachValueLists()
    {
        var doc = OnPingDocument();
        if (doc == null) return;
        var targets = Modes
            .Select(m => (mode: m, param: FindInput(m.Input)))
            .Where(t => t.param != null && t.param.SourceCount == 0)
            .ToList();
        if (targets.Count == 0) return;

        var record = doc.UndoUtil.CreateWireEvent("Attach mode value lists", targets.Select(t => t.param!));
        var created = new List<IGH_DocumentObject>();
        foreach (var (mode, param) in targets)
        {
            int cur = CurrentValue(mode.Input) is int v && v != ModeCatalog.Mixed ? v : mode.Default;
            var list = new GH_ValueList();
            list.CreateAttributes();
            list.Name = mode.Input;
            list.NickName = mode.Input;
            list.Description = $"{Name} · {mode.Input} (Mite mode list)";
            list.ListMode = GH_ValueListMode.DropDown;
            list.ListItems.Clear();
            foreach (var option in mode.Options)
                list.ListItems.Add(new GH_ValueListItem(option.Label, mode.Expression(option.Value)));
            if (mode.Find(cur) == null)
                list.ListItems.Add(new GH_ValueListItem(mode.LabelFor(cur), mode.Expression(cur)));
            list.SelectItem(list.ListItems.FindIndex(item => ParseExpression(item.Expression) == cur));

            list.Attributes.ExpireLayout();
            list.Attributes.PerformLayout();
            var grip = param!.Attributes.Bounds;
            var size = list.Attributes.Bounds.Size;
            list.Attributes.Pivot = new PointF(grip.Left - size.Width - 40, grip.Top + 0.5f * (grip.Height - size.Height));
            list.Attributes.ExpireLayout();

            doc.AddObject(list, false);
            param.AddSource(list);
            created.Add(list);
        }

        var add = doc.UndoUtil.CreateAddObjectEvent("Attach mode value lists", created);
        foreach (var action in add.Actions) record.AddAction(action);
        doc.UndoServer.PushUndoRecord(record);
        ExpireSolution(true);
    }
}
