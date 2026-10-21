using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;

namespace Mite.Grasshopper.Components;

/// <summary>
/// Cookie-cuts a net: the curves of a net traced on a larger mesh are cut to a
/// boundary drawn on or above the surface (Schling's design surface "larger
/// than the actual structure", AAG 2018 §2.2; the webs "cropped by a 4x4 m
/// square boundary", CAD 2022 §4.2). Core: <see cref="NetTrim"/>.
/// </summary>
public class NetTrimComponent : MiteComponent
{
    public NetTrimComponent()
        : base("Net Trim", "NetTrim",
            "Cookie-cuts a net to a boundary: trace the families on a larger mesh (the curves are " +
            "cleanest away from the mesh border), draw the outline of the structure as closed curves " +
            "on or above the surface, and keep only what lies inside. Every curve is cut exactly where " +
            "it crosses the boundary; a second loop inside the first cuts a hole. With Inside points " +
            "you choose the regions to keep instead; Invert keeps the outside (a hole in the net).",
            "Gridshells", "NetTrim") { }

    public override Guid ComponentGuid => new("D1C7B399-32AE-40A1-89B3-9F58949B5719");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("CurvesA", "A", "First family of net curves (polylines on the mesh)", GH_ParamAccess.list);
        pManager.AddCurveParameter("CurvesB", "B", "Second family (optional)", GH_ParamAccess.list);
        pManager[1].Optional = true;
        pManager.AddMeshParameter("Mesh", "M", "The mesh the net was traced on", GH_ParamAccess.item);
        pManager.AddCurveParameter("Boundary", "Bd", "Boundary curves: closed loops on or above the mesh (several loops: a loop inside another cuts a hole). " +
            "An open curve cuts like a knife and separates the surface only when both ends reach the mesh border", GH_ParamAccess.list);
        pManager.AddVectorParameter("Direction", "D", "Projection direction for the boundary: zero (default) pulls each point to the closest mesh point; " +
            "a vector projects along it (a plan curve drawn above the surface: 0,0,-1)", GH_ParamAccess.item, Vector3d.Zero);
        pManager.AddPointParameter("Inside", "In", "Reference points: the regions containing them are kept. Empty (default): the regions an odd number of loops away from the mesh border", GH_ParamAccess.list);
        pManager[5].Optional = true;
        pManager.AddBooleanParameter("Invert", "Iv", "Keep what is outside the boundary instead (cut a hole into the net)", GH_ParamAccess.item, false);
        pManager.AddNumberParameter("MinLength", "Ml", "Drop pieces shorter than this (default 0)", GH_ParamAccess.item, 0.0);
        pManager.AddNumberParameter("Sampling", "S", "Chord deviation for curve sampling (0 = automatic: 1/500 of the net size)", GH_ParamAccess.item, 0.0);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("CurvesA", "A", "Family A pieces inside the boundary", GH_ParamAccess.list);
        pManager.AddCurveParameter("CurvesB", "B", "Family B pieces inside the boundary", GH_ParamAccess.list);
        pManager.AddIntegerParameter("SourceA", "Ia", "Input curve index of each A piece", GH_ParamAccess.list);
        pManager.AddIntegerParameter("SourceB", "Ib", "Input curve index of each B piece", GH_ParamAccess.list);
        pManager.AddPointParameter("Cuts", "C", "The curve ends made by the cut (on the boundary)", GH_ParamAccess.list);
        pManager.AddCurveParameter("Pulled", "P", "The boundary as pulled onto the mesh", GH_ParamAccess.list);
        pManager.AddTextParameter("Report", "R", "Loops, regions, curves cut and removed", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var curvesA = new List<Curve>();
        var curvesB = new List<Curve>();
        var boundary = new List<Curve>();
        var insidePts = new List<Point3d>();
        var direction = Vector3d.Zero;
        bool invert = false;
        double minLength = 0.0, sampling = 0.0;
        if (!DA.GetDataList(0, curvesA)) return;
        DA.GetDataList(1, curvesB);
        var input = LoadMesh(DA, 2);
        if (input == null) return;
        if (!DA.GetDataList(3, boundary)) return;
        DA.GetData(4, ref direction);
        DA.GetDataList(5, insidePts);
        DA.GetData(6, ref invert);
        DA.GetData(7, ref minLength);
        DA.GetData(8, ref sampling);

        if (boundary.Count == 0 || boundary.All(c => c == null))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No boundary curve: the net is returned uncut.");
            DA.SetDataList(0, curvesA);
            DA.SetDataList(1, curvesB);
            return;
        }

        var proj = new MeshProjection(input.Data);
        if (sampling <= 0)
        {
            var bbox = BoundingBox.Empty;
            foreach (var c in curvesA) if (c != null) bbox.Union(c.GetBoundingBox(false));
            foreach (var c in curvesB) if (c != null) bbox.Union(c.GetBoundingBox(false));
            sampling = bbox.IsValid ? Math.Max(bbox.Diagonal.Length / 500.0, 1e-9) : 0.01;
        }
        var famA = NetJointsComponent.SampleAligned(curvesA, sampling);
        var famB = NetJointsComponent.SampleAligned(curvesB, sampling);

        // the boundary is sampled finer than a mesh edge so the loop follows the facets
        double loopChord = Math.Min(sampling, 0.25 * proj.AverageEdgeLength);
        var loops = new List<Vec3d[]>();
        foreach (var c in boundary)
        {
            if (c == null) continue;
            var pts = CurveSample.ToPolyline(c, loopChord);
            if (pts == null || pts.Length < 2) continue;
            if (c.IsClosed && (pts[0] - pts[pts.Length - 1]).Length > 1e-12)
            {
                var closed = pts.ToList(); closed.Add(pts[0]); pts = closed.ToArray();
            }
            loops.Add(pts);
        }

        var options = new NetTrim.Options
        {
            Direction = MeshConvert.ToVec3d(direction),
            Inside = insidePts.Count > 0 ? insidePts.ConvertAll(MeshConvert.ToVec3d) : null,
            Invert = invert,
            MinLength = Math.Max(0.0, minLength),
        };
        var families = new List<IReadOnlyList<Vec3d[]>> { famA, famB };
        var result = NetTrim.Trim(proj, families, loops, options);

        if (result.BoundaryOffMesh > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{result.BoundaryOffMesh} boundary sample(s) did not project onto the mesh along the Direction: the loop has gaps there and may not separate the surface. Use a direction that hits the mesh, or Direction 0 for closest-point pulling.");
        if (result.InsideRegions == 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                "The boundary does not enclose any region of this mesh: nothing is kept. Closed loops must lie within the mesh (or cross its border), an open cut must run border to border; give an Inside point to pick the region.");
        else
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, result.Report);
        if (result.DroppedShort > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"{result.DroppedShort} piece(s) shorter than MinLength {minLength} dropped.");

        DA.SetDataList(0, MeshConvert.ToCurves(result.Families[0]));
        DA.SetDataList(1, MeshConvert.ToCurves(result.Families[1]));
        DA.SetDataList(2, result.Source[0]);
        DA.SetDataList(3, result.Source[1]);
        DA.SetDataList(4, result.Cuts.ConvertAll(MeshConvert.ToRhinoPoint));
        DA.SetDataList(5, MeshConvert.ToCurves(result.Boundary.Where(l => l.Length >= 2)));
        DA.SetData(6, result.Report);
    }
}
