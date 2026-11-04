using Grasshopper.Kernel.Types;
using Mite.Core.Kinetics;

namespace Mite.Grasshopper;

/// <summary>
/// A solved motion (Net Kinetics' Motion output) as Grasshopper data: the
/// net, its states and the section they are read with. Wired into Kinetic
/// State to read one moment of the motion. Not saved with the definition — it
/// is recomputed from Net Kinetics.
/// </summary>
public sealed class KineticMotionGoo : GH_Goo<KineticMotion>
{
    public KineticMotionGoo() { }
    public KineticMotionGoo(KineticMotion motion) { Value = motion; }

    public override bool IsValid => Value != null;
    public override string TypeName => "Kinetic Motion";
    public override string TypeDescription => "A solved motion of a scissor net (from Net Kinetics): read it at any fold with Kinetic State";
    public override IGH_Goo Duplicate() => new KineticMotionGoo(Value);
    public override string ToString() => Value == null ? "Null motion"
        : $"Motion: {Value.Net.Laths.Count} laths, {Value.States.Count} states, Fold {Value.FirstFold:0.##} → {Value.LastFold:0.##}";
}
