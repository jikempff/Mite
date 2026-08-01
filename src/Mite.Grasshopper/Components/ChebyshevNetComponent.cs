using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Rhino.Geometry;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;

namespace Mite.Grasshopper.Components;

public class ChebyshevNetComponent : GH_Component
{
    public ChebyshevNetComponent()
        : base("Chebyshev Net", "ChebNet",
            "Constructs a Chebyshev net: equal edge lengths in both families everywhere. " +
            "This is the kinematics of an elastic gridshell — a flat lattice of constant-length " +
            "laths with rotating joints, bent into shape. Axis curves are geodesics from the seed; " +
            "interior nodes are placed by the compass method.",
            "Mite", "Gridshells") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDEB");

    protected override Bitmap Icon =>
        new Bitmap(Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Mite.Grasshopper.Resources.ChebyshevNet.png")!);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input mesh", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Seed", "S", "Seed vertex index (net origin)", GH_ParamAccess.item, 0);
        pManager.AddVectorParameter("Direction", "D", "First family direction at the seed", GH_ParamAccess.item, Vector3d.XAxis);
        pManager.AddNumberParameter("EdgeLength", "L", "Lath segment length between joints", GH_ParamAccess.item, 1.0);
        pManager.AddIntegerParameter("CountU", "U", "Nodes per side of the seed, first family (default 10)", GH_ParamAccess.item, 10);
        pManager.AddIntegerParameter("CountV", "V", "Nodes per side of the seed, second family (default 10)", GH_ParamAccess.item, 10);
        pManager.AddAngleParameter("Angle", "A", "Angle between families at the seed (default 90 degrees)", GH_ParamAccess.item, Math.PI / 2);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddPointParameter("Points", "P", "Net nodes, one branch per row", GH_ParamAccess.tree);
        pManager.AddCurveParameter("UCurves", "Cu", "Laths of the first family", GH_ParamAccess.list);
        pManager.AddCurveParameter("VCurves", "Cv", "Laths of the second family", GH_ParamAccess.list);
        pManager.AddMeshParameter("NetMesh", "N", "Quad mesh over the valid net cells", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        Mesh? mesh = null;
        int seed = 0, countU = 10, countV = 10;
        var direction = Vector3d.XAxis;
        double edgeLength = 1.0, angle = Math.PI / 2;

        if (!DA.GetData(0, ref mesh) || mesh == null) return;
        DA.GetData(1, ref seed);
        DA.GetData(2, ref direction);
        DA.GetData(3, ref edgeLength);
        DA.GetData(4, ref countU);
        DA.GetData(5, ref countV);
        DA.GetData(6, ref angle);

        if (edgeLength <= 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "EdgeLength must be positive.");
            return;
        }

        var data = MeshConvert.ToMeshData(mesh);
        if (seed < 0 || seed >= data.VertexCount)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Seed must be a vertex index between 0 and {data.VertexCount - 1}.");
            return;
        }

        var opts = new ChebyshevNet.Options
        {
            EdgeLength = edgeLength,
            CountU = countU,
            CountV = countV,
            Angle = angle
        };
        var net = ChebyshevNet.Compute(data,
            seed, new Vec3d(direction.X, direction.Y, direction.Z), opts);

        int nu = net.Points.GetLength(0), nv = net.Points.GetLength(1);

        int validCount = 0;
        var pointTree = new DataTree<Point3d>();
        for (int i = 0; i < nu; i++)
        {
            var path = new GH_Path(i);
            for (int j = 0; j < nv; j++)
            {
                if (!net.Valid[i, j]) continue;
                pointTree.Add(MeshConvert.ToRhinoPoint(net.Points[i, j]), path);
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
    }

    private static List<PolylineCurve> ExtractCurves(ChebyshevNet.Result net, bool alongU)
    {
        int nu = net.Points.GetLength(0), nv = net.Points.GetLength(1);
        int outer = alongU ? nv : nu;
        int inner = alongU ? nu : nv;

        var curves = new List<PolylineCurve>();
        for (int o = 0; o < outer; o++)
        {
            var run = new List<Point3d>();
            for (int k = 0; k < inner; k++)
            {
                int i = alongU ? k : o;
                int j = alongU ? o : k;
                if (net.Valid[i, j])
                {
                    run.Add(MeshConvert.ToRhinoPoint(net.Points[i, j]));
                }
                else
                {
                    if (run.Count > 1) curves.Add(new PolylineCurve(run));
                    run.Clear();
                }
            }
            if (run.Count > 1) curves.Add(new PolylineCurve(run));
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
