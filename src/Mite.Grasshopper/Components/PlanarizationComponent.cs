using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.FormFinding;
using Mite.Core.Geometry;

namespace Mite.Grasshopper.Components;

public class PlanarizationComponent : GH_Component
{
    public PlanarizationComponent()
        : base("Planarize Mesh", "Planarize",
            "Iterative quad planarization by projecting onto best-fit planes.",
            "Mite", "FormFinding") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDE5");

    protected override Bitmap Icon =>
        new Bitmap(Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Mite.Grasshopper.Resources.Planarization.png")!);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Input quad mesh", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Iterations", "I", "Max iterations (default 100)", GH_ParamAccess.item, 100);
        pManager.AddNumberParameter("Tolerance", "T", "Convergence tolerance (default 1e-6)", GH_ParamAccess.item, 1e-6);
        pManager.AddBooleanParameter("Fixed", "F", "Fixed vertex flags", GH_ParamAccess.list);
        pManager[3].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Planarized mesh", GH_ParamAccess.item);
        pManager.AddNumberParameter("Deviation", "D", "Planarity deviation per face", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Iterations", "I", "Iterations performed", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        Mesh? mesh = null;
        int maxIter = 100;
        double tol = 1e-6;
        var fixedList = new List<bool>();

        if (!DA.GetData(0, ref mesh) || mesh == null) return;
        DA.GetData(1, ref maxIter);
        DA.GetData(2, ref tol);
        DA.GetDataList(3, fixedList);

        var data = MeshConvert.ToMeshDataKeepQuads(mesh);

        bool[] fixedVerts;
        if (fixedList.Count == 0)
        {
            fixedVerts = new bool[data.VertexCount];
        }
        else if (fixedList.Count == data.VertexCount)
        {
            fixedVerts = fixedList.ToArray();
        }
        else
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"Fixed needs one flag per vertex: got {fixedList.Count}, mesh has {data.VertexCount}.");
            return;
        }

        var opts = new Planarization.Options { MaxIterations = maxIter, Tolerance = tol };
        var result = Planarization.Compute(data, fixedVerts, opts);

        var outMesh = mesh.DuplicateMesh();
        for (int i = 0; i < result.Vertices.Length; i++)
            outMesh.Vertices.SetVertex(i, MeshConvert.ToRhinoPoint(result.Vertices[i]));
        outMesh.Normals.ComputeNormals();

        DA.SetData(0, outMesh);
        DA.SetDataList(1, result.FaceDeviations);
        DA.SetData(2, result.Iterations);
    }
}
