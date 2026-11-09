// Mesh intake: what a mesh must be for the curvature and the tracers to read
// it as one surface, and the note that says "run Mesh Cleanup" with the numbers
// when it is not (ROADMAP B, runtime messages follow-up (b)).
//
// The algorithms assume a two-manifold with a consistent orientation: every
// edge borders one or two faces and the faces around a vertex form one fan
// (Botsch, Kobbelt, Pauly, Alliez & Lévy 2010, Polygon Mesh Processing §1.3;
// Guéziec, Taubin, Lazarus & Horn 2001 on the cutting that would repair a
// non-manifold edge). Where that fails the discrete operators are undefined
// and the tracers stop or turn; the message names the place and the repair.
// The census itself is MeshCleanup.Inspect (one pass over the faces); the
// coincident vertices of an unwelded soup are welded on the way in and said
// separately, so they never appear here.
using System;
using System.Collections.Generic;
using Mite.Core.Geometry;

namespace Mite.Core.Diagnostics;

public static partial class InputAdvice
{
    /// <summary>
    /// Notes on a mesh input that is not one consistently oriented manifold
    /// sheet: non-manifold edges, winding conflicts, several parts. Each says
    /// what the algorithms do there and that Mesh Cleanup reports / repairs it.
    /// Empty for an ordinary mesh.
    /// </summary>
    /// <param name="info">The census of the mesh as it arrived (<see cref="MeshCleanup.Inspect"/>).</param>
    /// <param name="faceCount">Faces in the mesh (for the share of a small part).</param>
    public static List<Advice> MeshIntake(MeshCleanup.Diagnostics info, int faceCount)
    {
        var list = new List<Advice>();
        if (info == null) return list;

        int nm = info.NonManifoldEdges.Length;
        if (nm > 0)
        {
            var (a, b) = info.NonManifoldEdges[0];
            list.Add(new Advice("mesh-non-manifold", AdviceLevel.Warning,
                $"{nm} mesh edge{(nm == 1 ? " is" : "s are")} shared by three or more faces (a fin, an internal wall or two " +
                $"sheets joined along a seam; the first between vertices {a} and {b}). The surface is not a single sheet there: " +
                "curvature is read from the faces round each vertex and a trace follows a face to its one neighbour across " +
                "every edge, so the estimators and the curves stop or turn at those edges. " +
                "Run Mesh Cleanup on the mesh (its NonManifold output marks the edges) and delete the extra faces, " +
                "or split the mesh into its sheets and feed one.",
                nm, 0, double.NaN));
        }

        if (info.OrientationConflicts > 0)
        {
            list.Add(new Advice("mesh-winding-mixed", AdviceLevel.Warning,
                $"{info.OrientationConflicts} edge{(info.OrientationConflicts == 1 ? " joins" : "s join")} faces that wind the opposite way, " +
                "so the normal flips across them: the mean curvature changes sign, Upright laths change side and the " +
                "asymptotic families swap their handedness on the far side. " +
                "Run Mesh Cleanup with UnifyWinding on (it reverses the faces of the minority side) and feed its Mesh.",
                info.OrientationConflicts, 0, double.NaN));
        }

        if (info.Components > 1)
        {
            list.Add(new Advice("mesh-several-parts", AdviceLevel.Warning,
                $"The mesh is {info.Components} separate parts ({info.ClosedComponents} closed, {info.BorderLoops} border loops in all). " +
                "A curve stays inside the part it starts in, so parts without a seed get no curves and the spacing is not kept " +
                "across the gaps between parts. If they are one surface with cracks, run Mesh Cleanup with Cracks set to the " +
                "gap width (it welds coincident vertices and snaps the border vertices onto the facing edges); " +
                "to work on one part, feed that part alone.",
                info.Components, 1, double.NaN));
        }

        return list;
    }
}
