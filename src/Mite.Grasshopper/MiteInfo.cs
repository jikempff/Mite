using System;
using System.Drawing;
using System.Reflection;
using Grasshopper.Kernel;

namespace Mite.Grasshopper;

public class MiteInfo : GH_AssemblyInfo
{
    public override string Name => "Mite";
    public override string Description => "Open-source mesh curvature analysis and form-finding toolkit.";
    public override Guid Id => new("A1B2C3D4-E5F6-7890-ABCD-EF1234567890");
    public override string AuthorName => "Mite Contributors";
    public override string AuthorContact => "https://github.com/jikempff/Mite";
    public override string Version => "1.0.3-beta.1";

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
