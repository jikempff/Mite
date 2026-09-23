using System;
using System.Collections.Generic;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Rhino.Geometry;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;

namespace Mite.Grasshopper.Components;

public class ChebyshevNetComponent : MiteComponent
{
    public ChebyshevNetComponent()
        : base("Chebyshev Net", "ChebNet",
            "Constructs a Chebyshev net: equal edge lengths in both families everywhere. " +
            "This is the kinematics of an elastic gridshell — a flat lattice of constant-length " +
            "laths with rotating joints, bent into shape. Axis curves are geodesics from the seed; " +
            "interior nodes are placed by the compass method.",
            "Gridshells", "ChebyshevNet") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDEB");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input mesh", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Seed", "S", "Seed vertex index (net origin)", GH_ParamAccess.item, 0);
        pManager.AddVectorParameter("Direction", "D", "First family direction at the seed", GH_ParamAccess.item, Vector3d.XAxis);
        pManager.AddNumberParameter("EdgeLength", "L", "Lath segment length between joints", GH_ParamAccess.item, 1.0);
        pManager.AddIntegerParameter("CountU", "U", "Nodes per side of the seed, first family (default 10)", GH_ParamAccess.item, 10);
        pManager.AddIntegerParameter("CountV", "V", "Nodes per side of the seed, second family (default 10)", GH_ParamAccess.item, 10);
        pManager.AddAngleParameter("Angle", "A", "Angle between families at the seed (default 90 degrees)", GH_ParamAccess.item, Math.PI / 2);
        pManager.AddPointParameter("SeedPoint", "P", "Net origin as a point (overrides Seed; nearest vertex is used)", GH_ParamAccess.item);
        pManager[7].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddPointParameter("Points", "P", "Net nodes, one branch per row", GH_ParamAccess.tree);
        pManager.AddCurveParameter("UCurves", "Cu", "Laths of the first family", GH_ParamAccess.list);
        pManager.AddCurveParameter("VCurves", "Cv", "Laths of the second family", GH_ParamAccess.list);
        pManager.AddMeshParameter("NetMesh", "N", "Quad mesh over the valid net cells", GH_ParamAccess.item);
        pManager.AddNumberParameter("Angles", "An", "Shear angle between the families at each node (degrees); locking occurs near 0 / 180", GH_ParamAccess.tree);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var input = LoadMesh(DA, 0);
        if (input == null) return;
        int seed = 0, countU = 10, countV = 10;
        var direction = Vector3d.XAxis;
        double edgeLength = 1.0, angle = Math.PI / 2;
        Point3d seedPoint = Point3d.Unset;

        DA.GetData(1, ref seed);
        DA.GetData(2, ref direction);
        DA.GetData(3, ref edgeLength);
        DA.GetData(4, ref countU);
        DA.GetData(5, ref countV);
        DA.GetData(6, ref angle);
        DA.GetData(7, ref seedPoint);

        // Angle parameters deliver the raw number; honor the user's Degrees toggle
        if (Params.Input[6] is Param_Number angleParam && angleParam.UseDegrees)
            angle = Rhino.RhinoMath.ToRadians(angle);

        if (edgeLength <= 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "EdgeLength must be positive.");
            return;
        }

        var data = input.Data;
        int topoSeed = seedPoint.IsValid
            ? new MeshProjection(data).NearestVertexGlobal(MeshConvert.ToVec3d(seedPoint))
            : input.ToTopo(seed);
        if (topoSeed < 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Seed must be a vertex index between 0 and {input.RhinoVertexCount - 1}, or provide a SeedPoint.");
            return;
        }
        if (edgeLength < 0.5 * new MeshProjection(data).AverageEdgeLength)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                "EdgeLength is smaller than the mesh edges; the compass construction will be noisy. Use a finer mesh or a larger EdgeLength.");

        var opts = new ChebyshevNet.Options
        {
            EdgeLength = edgeLength,
            CountU = countU,
            CountV = countV,
            Angle = angle
        };
        ChebyshevNet.Result net;
        try
        {
            net = ChebyshevNet.Compute(data,
                topoSeed, new Vec3d(direction.X, direction.Y, direction.Z), opts);
        }
        catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            return;
        }

        int nu = net.Points.GetLength(0), nv = net.Points.GetLength(1);

        int validCount = 0;
        var pointTree = new DataTree<Point3d>();
        var angleTree = new DataTree<double>();
        for (int i = 0; i < nu; i++)
        {
            var path = BranchPath(DA, i);
            for (int j = 0; j < nv; j++)
            {
                if (!net.Valid[i, j]) continue;
                pointTree.Add(MeshConvert.ToRhinoPoint(net.Points[i, j]), path);
                angleTree.Add(ShearAngle(net, i, j), path);
                validCount++;
            }
        }

        if (validCount <= 1)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                "Net could not grow from the seed. Check EdgeLength against the mesh size.");

        DA.SetDataTree(0, pointTree);
        DA.SetDataList(1, ExtractCurves(net, alongU: true));
        DA.SetDataList(2, ExtractCurves(net, alongU: false));
        DA.SetData(3, BuildNetMesh(net));
        DA.SetDataTree(4, angleTree);
    }

    /// <summary>Angle (degrees) between the two lath directions at a node, from its valid neighbors.</summary>
    private static double ShearAngle(ChebyshevNet.Result net, int i, int j)
    {
        int nu = net.Points.GetLength(0), nv = net.Points.GetLength(1);
        Vec3d p = net.Points[i, j];
        Vec3d? du = null, dv = null;
        if (i + 1 < nu && net.Valid[i + 1, j]) du = net.Points[i + 1, j] - p;
        else if (i > 0 && net.Valid[i - 1, j]) du = p - net.Points[i - 1, j];
        if (j + 1 < nv && net.Valid[i, j + 1]) dv = net.Points[i, j + 1] - p;
        else if (j > 0 && net.Valid[i, j - 1]) dv = p - net.Points[i, j - 1];
        if (du == null || dv == null) return double.NaN;
        double d = Vec3d.Dot(du.Value.Normalized(), dv.Value.Normalized());
        return Math.Acos(Math.Max(-1.0, Math.Min(1.0, d))) * 180.0 / Math.PI;
    }

    private static List<Curve> ExtractCurves(ChebyshevNet.Result net, bool alongU)
    {
        int nu = net.Points.GetLength(0), nv = net.Points.GetLength(1);
        int outer = alongU ? nv : nu;
        int inner = alongU ? nu : nv;

        var curves = new List<Curve>();
        void Flush(List<Point3d> run)
        {
            if (run.Count > 1)
            {
                var c = CurveBuild.Interpolated(run);
                if (c != null) curves.Add(c);
            }
            run.Clear();
        }

        for (int o = 0; o < outer; o++)
        {
            var run = new List<Point3d>();
            for (int k = 0; k < inner; k++)
            {
                int i = alongU ? k : o;
                int j = alongU ? o : k;
                if (net.Valid[i, j])
                    run.Add(MeshConvert.ToRhinoPoint(net.Points[i, j]));
                else
                    Flush(run);
            }
            Flush(run);
        }
        return curves;
    }

    private static Mesh BuildNetMesh(ChebyshevNet.Result net)
    {
        int nu = net.Points.GetLength(0), nv = net.Points.GetLength(1);
        var mesh = new Mesh();
        var index = new int[nu, nv];

        for (int i = 0; i < nu; i++)
            for (int j = 0; j < nv; j++)
                index[i, j] = net.Valid[i, j]
                    ? mesh.Vertices.Add(MeshConvert.ToRhinoPoint(net.Points[i, j]))
                    : -1;

        for (int i = 0; i < nu - 1; i++)
            for (int j = 0; j < nv - 1; j++)
                if (index[i, j] >= 0 && index[i + 1, j] >= 0 &&
                    index[i + 1, j + 1] >= 0 && index[i, j + 1] >= 0)
                    mesh.Faces.AddFace(index[i, j], index[i + 1, j], index[i + 1, j + 1], index[i, j + 1]);

        mesh.Normals.ComputeNormals();
        return mesh;
    }
}
