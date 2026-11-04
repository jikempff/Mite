using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Mite.Core.Kinetics;

namespace Mite.Grasshopper;

/// <summary>
/// Supports and drivers of a scissor net as one wire (Kinetic Supports, Kinetic
/// Drivers): points that snap to the nodes of whichever net they are wired into.
/// Not saved with the definition — it is rebuilt from its components.
/// </summary>
public sealed class KineticSetupGoo : GH_Goo<KineticSetup>
{
    public KineticSetupGoo() { }
    public KineticSetupGoo(KineticSetup setup) { Value = setup; }

    public override bool IsValid => Value != null;
    public override string TypeName => "Kinetic Setup";
    public override string TypeDescription => "Supports and drivers of a scissor net (Kinetic Supports, Kinetic Drivers): wire into Net Kinetics or Mechanism Modes";
    public override IGH_Goo Duplicate() => new KineticSetupGoo(Value);
    public override string ToString() => Value == null ? "Null setup" : Describe(Value);

    internal static string Describe(KineticSetup s)
    {
        var parts = new List<string>();
        if (s.Fixed.Count > 0) parts.Add($"{s.Fixed.Count} fixed");
        if (s.Slide.Count > 0) parts.Add($"{s.Slide.Count} sliding");
        if (s.Move.Count > 0) parts.Add($"{s.Move.Count} moved");
        if (s.Cables.Count > 0) parts.Add($"{s.Cables.Count} cable{(s.Cables.Count == 1 ? "" : "s")}");
        return "Kinetic setup: " + (parts.Count == 0 ? "empty" : string.Join(", ", parts));
    }

    /// <summary>
    /// Reads an optional setup input; null when unwired. Anything else wired there
    /// gets an error on <paramref name="owner"/> and returns false.
    /// </summary>
    internal static bool TryRead(GH_Component owner, IGH_DataAccess DA, int index, out KineticSetup? setup)
    {
        setup = null;
        IGH_Goo? goo = null;
        if (!DA.GetData(index, ref goo) || goo == null) return true;
        if (goo is KineticSetupGoo g && g.Value != null) { setup = g.Value; return true; }
        owner.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"{owner.Params.Input[index].Name} expects a Kinetic Supports or Kinetic Drivers output, got {goo.TypeName}.");
        return false;
    }

    /// <summary>Adds a setup's problems to <paramref name="owner"/> as warnings.</summary>
    internal static void Report(GH_Component owner, KineticSetup? setup)
    {
        if (setup == null) return;
        foreach (var p in setup.Problems()) owner.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, p);
    }
}
