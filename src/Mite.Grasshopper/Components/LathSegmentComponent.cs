using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Rhino.Geometry;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;

namespace Mite.Grasshopper.Components;

public class LathSegmentComponent : MiteComponent
{
    public LathSegmentComponent()
        : base("Lath Segment", "Segment",
            "Splits laths into pieces that fit stock material length, keeping cuts away from " +
            "net crossings. Consecutive pieces overlap by the splice length, and half-lap splice " +
            "notch solids (Ne for the upstream piece, Ns for the downstream one) are built at each " +
            "cut: subtract them from the swept pieces with Mesh Difference.",
            "Fabrication", "LathSegment") { }

    public override Guid ComponentGuid => new("B1C2D3E4-F5A6-7890-1234-567890ABCDF5");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Reference surface", GH_ParamAccess.item);
        pManager.AddCurveParameter("Curves", "C", "Lath centerlines", GH_ParamAccess.list);
        pManager.AddNumberParameter("StockLength", "St", "Available material length (default 3)", GH_ParamAccess.item, 3.0);
        pManager.AddNumberParameter("Margin", "Ma", "Minimum distance between a cut and a joint (default 0.05)", GH_ParamAccess.item, 0.05);
        pManager.AddPointParameter("Joints", "J", "Crossing points to avoid (from Net Joints; only those on each lath are used)", GH_ParamAccess.list);
        pManager.AddNumberParameter("SpliceLength", "SL", "Half-lap splice overlap length (default 0.15)", GH_ParamAccess.item, 0.15);
        pManager.AddNumberParameter("Width", "W", "Lath width (default 0.1)", GH_ParamAccess.item, 0.1);
        pManager.AddNumberParameter("Thickness", "T", "Lath thickness (default 0.01)", GH_ParamAccess.item, 0.01);
        pManager.AddBooleanParameter("Upright", "U", "Lath orientation, as in Lath Sweep", GH_ParamAccess.item, false);
        pManager.AddNumberParameter("Clearance", "Cl", "Fit clearance (default 0)", GH_ParamAccess.item, 0.0);
        pManager.AddNumberParameter("Sampling", "S", "Chord deviation for curve sampling (0 = automatic from the mesh edge length)", GH_ParamAccess.item, 0.0);
        pManager.AddNumberParameter("Offset", "O", "Gap between the surface and the nearest lath face, as in Lath Sweep (default 0)", GH_ParamAccess.item, 0.0);
        pManager.AddIntegerParameter("Align", "Al", "Where the lath sits along the surface normal: 0 grows from the surface out (default), 1 centred on the surface (its centre line on the curve), 2 grows from the surface in. Offset is the gap to the near face, or the shift of the centre when centred", GH_ParamAccess.item, 0);
        pManager[4].Optional = true;
        pManager.AddBooleanParameter("Balance", "Ba", "False (default): every piece a full stock length and a remainder at the end of the lath. True: the fewest pieces that fit the stock, made equal (a 6.4 m lath on 6 m stock becomes 3.2 + 3.2, not 6 + 0.4), the cuts moved off the joints by Margin; equal pieces cost stock, see Boards", GH_ParamAccess.item, false);
        pManager.AddNumberParameter("Kerf", "Kf", "Width of a saw cut between two pieces cut from one board (default 0)", GH_ParamAccess.item, 0.0);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("Segments", "S", "Lath pieces, one branch per input lath", GH_ParamAccess.tree);
        pManager.AddPointParameter("Cuts", "Cu", "Cut points, one branch per input lath", GH_ParamAccess.tree);
        pManager.AddBoxParameter("NotchesEnd", "Ne", "Splice notches for the upstream piece at each cut", GH_ParamAccess.tree);
        pManager.AddBoxParameter("NotchesStart", "Ns", "Splice notches for the downstream piece at each cut", GH_ParamAccess.tree);
        pManager.AddNumberParameter("Lengths", "L", "Piece lengths, one branch per input lath", GH_ParamAccess.tree);
        pManager.AddIntegerParameter("Board", "Bd", "Stock board each piece is cut from (first-fit decreasing packing of all pieces into boards of StockLength, Kerf between pieces), one branch per input lath", GH_ParamAccess.tree);
        pManager.AddIntegerParameter("Boards", "Nb", "Number of stock boards needed", GH_ParamAccess.item);
        pManager.AddNumberParameter("Material", "Mt", "Share of the bought stock that ends up in the laths: Σ piece lengths / (Boards × StockLength)", GH_ParamAccess.item);
        pManager.AddTextParameter("Report", "R", "Pieces, splices, boards against the lower bound, material and offcuts", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var input = LoadMesh(DA, 0);
        if (input == null) return;
        var curves = new List<Curve>();
        if (!DA.GetDataList(1, curves)) return;
        var joints = new List<Point3d>();
        double stock = 3.0, margin = 0.05, spliceLength = 0.15, width = 0.1, thickness = 0.01, clearance = 0.0, sampling = 0.0, offset = 0.0;
        bool upright = false;
        DA.GetData(2, ref stock);
        DA.GetData(3, ref margin);
        DA.GetDataList(4, joints);
        DA.GetData(5, ref spliceLength);
        DA.GetData(6, ref width);
        DA.GetData(7, ref thickness);
        DA.GetData(8, ref upright);
        DA.GetData(9, ref clearance);
        DA.GetData(10, ref sampling);
        DA.GetData(11, ref offset);
        int align = 0;
        DA.GetData(12, ref align);
        bool balance = false; double kerf = 0.0;
        DA.GetData(13, ref balance);
        DA.GetData(14, ref kerf);

        if (stock <= 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "StockLength must be positive.");
            return;
        }
        if (spliceLength < 0 || spliceLength >= stock)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "SpliceLength must be between 0 and StockLength.");
            return;
        }

        var proj = new MeshProjection(input.Data);
        double chord = ResolveSampling(sampling, proj);
        var profile = new LathProfile(width, thickness, upright, offset).WithAlign(AlignOf(align));

        var jointVecs = new List<Vec3d>();
        foreach (var p in joints) jointVecs.Add(MeshConvert.ToVec3d(p));
        // A joint belongs to a lath when it lies within roughly a lath width of it
        double jointReach = Math.Max(width, 4.0 * chord);

        var segments = new DataTree<Curve>();
        var cuts = new DataTree<Point3d>();
        var notchesEnd = new DataTree<Box>();
        var notchesStart = new DataTree<Box>();
        var lengths = new DataTree<double>();
        var boardTree = new DataTree<int>();
        var pieceLengths = new List<double>();
        var piecePaths = new List<GH_Path>();
        int skipped = 0, splices = 0, laths = 0;
        double minClear = double.MaxValue;

        for (int c = 0; c < curves.Count; c++)
        {
            if (Cancelled()) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Cancelled with Esc, output is partial."); break; }
            var path = BranchPath(DA, c);
            segments.EnsurePath(path); cuts.EnsurePath(path); notchesEnd.EnsurePath(path); notchesStart.EnsurePath(path); lengths.EnsurePath(path); boardTree.EnsurePath(path);

            var line = curves[c] == null ? null : CurveSample.ToPolyline(curves[c], chord);
            if (line == null || line.Length < 2) { skipped++; continue; }
            laths++;

            double[]? jointArcs = jointVecs.Count > 0
                ? LathSegmentation.JointArcLengths(line, jointVecs, jointReach)
                : null;

            var r = LathSegmentation.Segment(line, stock, margin, jointArcs, spliceLength, balance);
            splices += r.CutArcLengths.Length;
            if (jointArcs != null && jointArcs.Length > 0)
                foreach (double cut in r.CutArcLengths) minClear = Math.Min(minClear, jointArcs.Min(j => Math.Abs(j - cut)));

            foreach (var seg in r.Segments)
            {
                if (seg.Length < 2) continue;
                segments.Add(MeshConvert.ToPolylineCurve(seg), path);
                double len = 0;
                for (int i = 1; i < seg.Length; i++) len += (seg[i] - seg[i - 1]).Length;
                lengths.Add(len, path);
                pieceLengths.Add(len); piecePaths.Add(path);
            }

            for (int k = 0; k < r.CutPoints.Length; k++)
            {
                Vec3d cut = r.CutPoints[k];
                Vec3d tangent = TangentAtArc(line, r.CutArcLengths[k]);
                var hit = proj.ClosestPoint(cut, proj.NearestVertexGlobal(cut));

                cuts.Add(MeshConvert.ToRhinoPoint(cut), path);

                if (JointGeometry.TryBuildSpliceNotches(cut, tangent, hit.SmoothNormal,
                        profile, spliceLength, clearance, out NotchSolid ne, out NotchSolid ns))
                {
                    notchesEnd.Add(NetJointsComponent.ToRhinoBox(ne), path);
                    notchesStart.Add(NetJointsComponent.ToRhinoBox(ns), path);
                }
            }
        }

        if (skipped > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"{skipped} null or degenerate curve(s) skipped (empty branches).");

        // every piece of every lath cut from stock boards: first-fit decreasing, a kerf per saw cut
        var pack = CuttingStock.Pack(pieceLengths, stock, Math.Max(0.0, kerf));
        for (int i = 0; i < pieceLengths.Count; i++) boardTree.Add(pack.BoardOf[i], piecePaths[i]);
        if (pack.TooLong > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"{pack.TooLong} piece(s) are longer than StockLength (a piece carries half a splice at each cut: raise StockLength or lower SpliceLength).");
        string report = $"{laths} laths · {pieceLengths.Count} pieces · {splices} splices" +
            (minClear < double.MaxValue ? $" (nearest joint {minClear:0.000}, margin {margin:0.000})" : "") +
            $"\n{pack.Boards.Count} boards of {stock:0.##}" + (pack.Boards.Count > pack.LowerBound ? $" (at least {pack.LowerBound} needed; first-fit decreasing is within 11/9 of the optimum)" : " (the lower bound: no packing does better)") +
            $" · material {100 * pack.Material:0.0} % · offcuts {pack.Waste:0.00}" + (kerf > 0 ? $" incl. kerfs of {kerf:0.###}" : "") +
            (pieceLengths.Count > 0 ? $"\npieces {pieceLengths.Min():0.00} … {pieceLengths.Max():0.00}" : "") +
            (balance ? " · balanced (equal pieces per lath)" : " · full stock lengths with a remainder per lath");

        DA.SetDataTree(0, segments);
        DA.SetDataTree(1, cuts);
        DA.SetDataTree(2, notchesEnd);
        DA.SetDataTree(3, notchesStart);
        DA.SetDataTree(4, lengths);
        DA.SetDataTree(5, boardTree);
        DA.SetData(6, pack.Boards.Count);
        DA.SetData(7, pack.Material);
        DA.SetData(8, report);
    }

    private static Vec3d TangentAtArc(Vec3d[] line, double s)
    {
        double arc = 0;
        for (int i = 1; i < line.Length; i++)
        {
            double len = (line[i] - line[i - 1]).Length;
            if (arc + len >= s && len > 1e-15)
                return (line[i] - line[i - 1]) / len;
            arc += len;
        }
        return (line[line.Length - 1] - line[line.Length - 2]).Normalized();
    }
}
