using System;
using System.Collections.Generic;
using Mite.Core.Geometry;

namespace Mite.Core.Gridshells;

/// <summary>
/// Buildability analysis for gridshell laths. Given a polyline lying on a mesh,
/// decomposes its bending into the Darboux frame: geodesic curvature (in-surface
/// bend), normal curvature (out-of-surface bend), and geodesic torsion (twist).
/// These map directly onto the bending modes of a rectangular strip, so the
/// analysis reports whether a lath of given width, thickness, and material
/// strain limit can physically be bent along the curve.
/// </summary>
public static class LathAnalysis
{
    public class Options
    {
        /// <summary>Strip cross-section dimension lying across the curve (default 0.1).</summary>
        public double Width { get; set; } = 0.1;

        /// <summary>Strip cross-section dimension through the strip (default 0.01).</summary>
        public double Thickness { get; set; } = 0.01;

        /// <summary>Maximum allowable bending strain, e.g. sigma/E (default 0.005).</summary>
        public double MaxStrain { get; set; } = 0.005;

        /// <summary>
        /// False: strip lies flat on the surface (geodesic gridshells) — normal
        /// curvature is the easy bending mode. True: strip stands upright,
        /// perpendicular to the surface (asymptotic gridshells) — geodesic
        /// curvature is the easy mode.
        /// </summary>
        public bool Upright { get; set; }
    }

    public readonly struct Result
    {
        /// <summary>In-surface bending per vertex (endpoints zero).</summary>
        public readonly double[] GeodesicCurvature;

        /// <summary>Out-of-surface bending per vertex (endpoints zero).</summary>
        public readonly double[] NormalCurvature;

        /// <summary>Twist rate per vertex.</summary>
        public readonly double[] GeodesicTorsion;

        /// <summary>Peak strain over all modes per vertex, as a fraction of MaxStrain.</summary>
        public readonly double[] Utilization;

        public readonly double MaxUtilization;
        public readonly bool Buildable;

        public Result(double[] kg, double[] kn, double[] tg, double[] utilization,
            double maxUtilization, bool buildable)
        {
            GeodesicCurvature = kg;
            NormalCurvature = kn;
            GeodesicTorsion = tg;
            Utilization = utilization;
            MaxUtilization = maxUtilization;
            Buildable = buildable;
        }
    }

    /// <summary>Analyzes one lath. Reuse a single MeshProjection for many laths.</summary>
    public static Result Analyze(MeshProjection proj, Vec3d[] polyline, Options? options = null)
    {
        options ??= new Options();
        int n = polyline.Length;

        var kg = new double[n];
        var kn = new double[n];
        var tg = new double[n];
        var util = new double[n];

        if (n < 2)
            return new Result(kg, kn, tg, util, 0.0, true);

        // Smooth surface normals along the lath (face normals jump at facet
        // crossings, which would corrupt the torsion finite difference)
        var normals = new Vec3d[n];
        int hint = FindNearestVertexGlobal(proj.Mesh, polyline[0]);
        for (int i = 0; i < n; i++)
        {
            var hit = proj.ClosestPoint(polyline[i], hint);
            normals[i] = hit.SmoothNormal;
            hint = hit.NearestVertex;
        }

        // Geodesic torsion per segment: tau_g = -(dN/ds) . g with g = N x T
        var segTorsion = new double[n - 1];
        for (int j = 0; j < n - 1; j++)
        {
            Vec3d seg = polyline[j + 1] - polyline[j];
            double len = seg.Length;
            if (len < 1e-15) continue;

            Vec3d t = seg / len;
            Vec3d nAvg = (normals[j] + normals[j + 1]).Normalized();
            Vec3d g = Vec3d.Cross(nAvg, t).Normalized();
            segTorsion[j] = -Vec3d.Dot((normals[j + 1] - normals[j]) / len, g);
        }
        tg[0] = segTorsion[0];
        tg[n - 1] = segTorsion[n - 2];
        for (int i = 1; i < n - 1; i++)
            tg[i] = 0.5 * (segTorsion[i - 1] + segTorsion[i]);

        // Curvature decomposition at interior vertices
        for (int i = 1; i < n - 1; i++)
        {
            Vec3d ePrev = polyline[i] - polyline[i - 1];
            Vec3d eNext = polyline[i + 1] - polyline[i];
            double lPrev = ePrev.Length, lNext = eNext.Length;
            if (lPrev < 1e-15 || lNext < 1e-15) continue;

            Vec3d tPrev = ePrev / lPrev;
            Vec3d tNext = eNext / lNext;

            double dot = Math.Max(-1.0, Math.Min(1.0, Vec3d.Dot(tPrev, tNext)));
            double kappa = Math.Acos(dot) / (0.5 * (lPrev + lNext));

            Vec3d bend = tNext - tPrev;
            if (bend.LengthSquared < 1e-20) continue;
            bend = bend.Normalized();

            Vec3d t = (tPrev + tNext).Normalized();
            Vec3d g = Vec3d.Cross(normals[i], t).Normalized();

            kn[i] = kappa * Vec3d.Dot(bend, normals[i]);
            kg[i] = kappa * Vec3d.Dot(bend, g);
        }

        // Strain per mode. Flat strip: kn bends about the width axis (fiber
        // distance t/2), kg about the surface normal (fiber distance w/2).
        // Upright strip: the two swap. Twist of a thin rectangle: shear ~ tau * t.
        double easyHalf = 0.5 * options.Thickness;
        double hardHalf = 0.5 * options.Width;

        double maxUtil = 0.0;
        for (int i = 0; i < n; i++)
        {
            double easyK = options.Upright ? kg[i] : kn[i];
            double hardK = options.Upright ? kn[i] : kg[i];

            double strain = Math.Max(
                Math.Abs(easyK) * easyHalf,
                Math.Max(Math.Abs(hardK) * hardHalf, Math.Abs(tg[i]) * options.Thickness));

            util[i] = strain / options.MaxStrain;
            if (util[i] > maxUtil) maxUtil = util[i];
        }

        return new Result(kg, kn, tg, util, maxUtil, maxUtil <= 1.0);
    }

    /// <summary>Analyzes many laths against one mesh.</summary>
    public static List<Result> Analyze(MeshData mesh, IEnumerable<Vec3d[]> polylines, Options? options = null)
    {
        var proj = new MeshProjection(mesh);
        var results = new List<Result>();
        foreach (var line in polylines)
            results.Add(Analyze(proj, line, options));
        return results;
    }

    private static int FindNearestVertexGlobal(MeshData mesh, Vec3d p)
    {
        int best = 0;
        double bestDist = double.MaxValue;
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            double d = (mesh.Vertices[i] - p).LengthSquared;
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }
}
