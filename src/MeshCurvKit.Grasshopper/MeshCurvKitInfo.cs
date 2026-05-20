using System;
using System.Drawing;
using Grasshopper.Kernel;

namespace MeshCurvKit.Grasshopper;

public class MeshCurvKitInfo : GH_AssemblyInfo
{
    public override string Name => "MeshCurvKit";
    public override string Description => "Open-source mesh curvature analysis and form-finding toolkit.";
    public override Guid Id => new("A1B2C3D4-E5F6-7890-ABCD-EF1234567890");
    public override string AuthorName => "MeshCurvKit Contributors";
    public override string AuthorContact => "https://github.com/jikempff/MeshCurvKit";
    public override string Version => "0.1.0";
    public override Bitmap? Icon => null;
}
