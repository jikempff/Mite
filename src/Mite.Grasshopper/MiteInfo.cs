using System;
using System.Drawing;
using System.Reflection;
using Grasshopper.Kernel;

namespace Mite.Grasshopper;

public class MiteInfo : GH_AssemblyInfo
{
    public override string Name => "Mite";
    public override string Description => "Mesh curvature analysis, form finding, gridshell nets and lath fabrication.";
    public override Guid Id => new("A1B2C3D4-E5F6-7890-ABCD-EF1234567890");
    public override string AuthorName => "Mite Contributors";
    public override string AuthorContact => "https://github.com/jikempff/Mite";
    public override string Version => "1.2.5";

    public override Bitmap? Icon
    {
        get
        {
            var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("Mite.Grasshopper.Resources.Mite_Tab.png");
            return stream != null ? new Bitmap(stream) : null;
        }
    }
}

/// <summary>
/// Registers the Mite logo as the icon of the "Mite" ribbon tab and "M" as
/// its short symbol. Without this Grasshopper shows the tab with a plain
/// letter in the tab header and tooltip; GH_AssemblyInfo.Icon alone only
/// covers the plugin list.
/// </summary>
public class MitePriority : GH_AssemblyPriority
{
    public override GH_LoadingInstruction PriorityLoad()
    {
        var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Mite.Grasshopper.Resources.Mite_Tab.png");
        if (stream != null)
        {
            global::Grasshopper.Instances.ComponentServer.AddCategoryIcon("Mite", new Bitmap(stream));
        }
        global::Grasshopper.Instances.ComponentServer.AddCategorySymbolName("Mite", 'M');
        global::Grasshopper.Instances.ComponentServer.AddCategoryShortName("Mite", "Mite");
        return GH_LoadingInstruction.Proceed;
    }
}
