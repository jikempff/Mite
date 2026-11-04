using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;
using Mite.Core.Kinetics;
using Mite.Grasshopper.Components;

namespace Mite.Grasshopper;

/// <summary>
/// The scissor net of two lath families, built the same way by every
/// component of the Kinetics panel: curves sampled at a chord deviation, their
/// crossings found, Net Topology's nodes and laths, joint normals from the
/// surface when there is one.
/// </summary>
internal static class KineticsInput
{
    /// <summary>
    /// Builds the net, or returns null with an error on <paramref name="owner"/>.
    /// sampling ≤ 0 = 1/500 of the curves' bounding-box diagonal.
    /// </summary>
    internal static ScissorNet? BuildNet(GH_Component owner, List<Curve> curvesA, List<Curve> curvesB,
        MeshData? surface, double sampling, double tolerance, double segment)
    {
        if (sampling <= 0)
        {
            var bbox = BoundingBox.Empty;
            foreach (var c in curvesA) if (c != null) bbox.Union(c.GetBoundingBox(false));
            foreach (var c in curvesB) if (c != null) bbox.Union(c.GetBoundingBox(false));
            sampling = bbox.IsValid ? Math.Max(bbox.Diagonal.Length / 500.0, 1e-9) : 0.01;
        }
        var familyA = NetJointsComponent.SampleAligned(curvesA, sampling);
        var familyB = NetJointsComponent.SampleAligned(curvesB, sampling);
        var crossings = NetIntersections.FindAll(familyA, familyB, tolerance);
        if (crossings.Count == 0)
        {
            owner.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No crossings found between the families: nothing to hinge.");
            return null;
        }
        var topo = NetTopology.Build(familyA, familyB, crossings);
        Func<Vec3d, Vec3d>? normalAt = null;
        if (surface != null)
        {
            var proj = new MeshProjection(surface);
            normalAt = p => proj.ClosestPoint(p, -1).SmoothNormal;
        }
        try { return ScissorNet.FromTopology(topo, familyA.Count + familyB.Count, familyA.Count, Math.Max(0, segment), normalAt); }
        catch (ArgumentException ex) { owner.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message); return null; }
    }
}
