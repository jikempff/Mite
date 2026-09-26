using System;
using System.Collections.Generic;
using System.Linq;
using Mite.Core.Fabrication;
using Mite.Core.Geometry;

namespace Mite.Core.Kinetics;

/// <summary>
/// Elastic reading of a kinetic state: curvatures, strains and strain energy
/// of every lath of a <see cref="ScissorNet"/> configuration, and the energy
/// diagram along the motion.
///
/// Model after Schikore, Schling, Oberbichler &amp; Bauer 2020, "Kinetics and
/// Design of Semi-Compliant Grid Mechanisms" (AAG 2020), §"compliant
/// deformation": the internal strain energy of a lamella grid is
///   Π = ½ ∫ (G I_t κ_x² + E I_y κ_y² + E I_z κ_z²) ds
/// with κ_x the twist rate and κ_y, κ_z the bending curvatures about the two
/// section axes; plotted against the state parameter t it is their
/// "curvature-square diagram" (the material-free Σ∫κ² ds shows the geometry's
/// share), and the state of least Π (+ self-weight potential ρ A g ∫ z ds) is
/// the natural, unactuated configuration of the grid — the kinetic umbrella's
/// predicted natural diameter 4.47 m against 4.43 m in FEM. Curvatures are
/// read in the Darboux frame of the moving surface (do Carmo 1976 §3-2):
/// normal curvature kn and geodesic curvature kg from the turning of the lath
/// polyline at every node split along the node normal and along g = n × t,
/// geodesic torsion τg from the rotation of the normal between consecutive
/// nodes about the segment (N' = −kn T − τg g; on an asymptotic lath the
/// normals are perpendicular to the segment, so the angle between them is
/// exactly ∫ τg ds over the segment; between two hinges a free lath twists
/// uniformly, which is what the kinetic solver enforces at subdivision nodes,
/// so the twist energy is that of the joint-to-joint rotations — a lower bound
/// of ½GJ∫(√−K)² ds along the surface's asymptotic curve, which a lath hinged
/// only at the joints does not follow). Strains use the same fibre distances and
/// Saint-Venant twist length as Lath Analysis (LathProfile.SectionProperties),
/// so a state's utilization is comparable to the static check.
/// </summary>
public static class KineticStrain
{
    public sealed class Options
    {
        /// <summary>Lath section (Flat: A across the lath in the tangent plane, B along the normal; Upright swaps).</summary>
        public LathProfile Profile { get; set; } = new LathProfile(0.1, 0.01, true);

        /// <summary>Allowable strain (default 0.5 %); utilization = peak strain / MaxStrain.</summary>
        public double MaxStrain { get; set; } = 0.005;

        /// <summary>Young's modulus (Pa; 11 GPa timber-ish).</summary>
        public double YoungsModulus { get; set; } = 11e9;

        /// <summary>Shear modulus (Pa); 0 = E / 16 (softwood along the grain).</summary>
        public double ShearModulus { get; set; } = 0.0;

        /// <summary>Density (kg/m³) for the self-weight potential; 0 = ignore self-weight.</summary>
        public double Density { get; set; } = 0.0;

        /// <summary>Gravity (m/s², pointing down).</summary>
        public Vec3d Gravity { get; set; } = new Vec3d(0, 0, -9.81);

        /// <summary>Metres per model unit (section dimensions and geometry are in model units).</summary>
        public double UnitScale { get; set; } = 1.0;
    }

    public sealed class LathResult
    {
        /// <summary>Per node (endpoints 0): geodesic curvature, normal curvature, geodesic torsion (1 / model unit).</summary>
        public double[] GeodesicCurvature = Array.Empty<double>();
        public double[] NormalCurvature = Array.Empty<double>();
        public double[] GeodesicTorsion = Array.Empty<double>();
        /// <summary>Per node: peak strain / MaxStrain.</summary>
        public double[] Utilization = Array.Empty<double>();
        public double MaxUtilization;
        /// <summary>Strain energies (J when UnitScale converts to metres and moduli are in Pa).</summary>
        public double BendingEnergy, TwistEnergy;
        public double Energy => BendingEnergy + TwistEnergy;
        /// <summary>Material-free ∫ (kn² + kg² + τg²) ds in model units.</summary>
        public double CurvatureSquare;
        public double Length;
    }

    public sealed class StateResult
    {
        public double Fold;
        public LathResult[] Laths = Array.Empty<LathResult>();
        public double BendingEnergy, TwistEnergy;
        /// <summary>Total strain energy Π.</summary>
        public double StrainEnergy => BendingEnergy + TwistEnergy;
        /// <summary>Self-weight potential ρ A g ∫ z ds (0 when Density is 0).</summary>
        public double Potential;
        /// <summary>Π + potential: the quantity the natural state minimises.</summary>
        public double Total => StrainEnergy + Potential;
        public double CurvatureSquare;
        public double MaxUtilization;
        public bool Buildable => MaxUtilization <= 1.0;
        /// <summary>Per lath: peak utilization.</summary>
        public double[] LathUtilization => Laths.Select(l => l.MaxUtilization).ToArray();
    }

    /// <summary>Curvatures, strains and energies of one state.</summary>
    public static StateResult Analyze(ScissorNet net, ScissorNet.State state, Options? options = null)
    {
        var opt = options ?? new Options();
        double u = opt.UnitScale;
        var sp = opt.Profile.SectionProperties();
        bool upright = opt.Profile.Upright;
        double E = opt.YoungsModulus, G = opt.ShearModulus > 0 ? opt.ShearModulus : E / 16.0;
        // Section properties in SI
        double IA = sp.IA * Math.Pow(u, 4), IB = sp.IB * Math.Pow(u, 4), J = sp.J * Math.Pow(u, 4), area = sp.Area * u * u;
        double twistFactor = sp.TwistLength / Math.Sqrt(3.0);
        // Fibre pairing as in LathAnalysis: the curvature whose fibre distance is ExtentB bends about the A axis (IA)
        double halfB = sp.ExtentB, halfA = sp.ExtentA;

        var res = new StateResult { Fold = state.Fold, Laths = new LathResult[net.Laths.Count] };
        double gravityDot = opt.Density > 0 ? opt.Gravity.Length : 0.0;
        var down = opt.Gravity.Length > 0 ? opt.Gravity.Normalized() : new Vec3d(0, 0, -1);

        for (int l = 0; l < net.Laths.Count; l++)
        {
            var lath = net.Laths[l];
            int n = lath.Length;
            var r = new LathResult
            {
                GeodesicCurvature = new double[n], NormalCurvature = new double[n], GeodesicTorsion = new double[n], Utilization = new double[n],
            };
            var p = new Vec3d[n]; var nn = new Vec3d[n];
            for (int i = 0; i < n; i++) { p[i] = state.Nodes[lath[i]]; nn[i] = state.Normals[lath[i]].Normalized(); }
            var segLen = new double[Math.Max(n - 1, 0)];
            var segTau = new double[Math.Max(n - 1, 0)];
            for (int i = 0; i + 1 < n; i++)
            {
                var e = p[i + 1] - p[i]; double len = e.Length; segLen[i] = len; r.Length += len;
                if (len < 1e-300) continue;
                var t = e / len;
                // rotation of the normal about the segment: exact ∫τg ds when the normals are ⟂ the segment
                var na = (nn[i] - Vec3d.Dot(nn[i], t) * t).Normalized();
                var nb = (nn[i + 1] - Vec3d.Dot(nn[i + 1], t) * t).Normalized();
                double angle = Math.Atan2(Vec3d.Dot(Vec3d.Cross(na, nb), t), Vec3d.Dot(na, nb));
                // Darboux sign: N' = −τg g with g = N × T  ⇒ τg = −(dN/ds)·g
                var g = Vec3d.Cross(na, t);
                double sign = -Vec3d.Dot(nb - na, g) >= 0 ? 1 : -1;
                segTau[i] = sign * Math.Abs(angle) / len;
            }
            // node curvatures from the turning of the polyline, split in the Darboux frame of the node
            for (int i = 1; i + 1 < n; i++)
            {
                double lp = segLen[i - 1], lq = segLen[i];
                if (lp < 1e-300 || lq < 1e-300) continue;
                Vec3d tp = (p[i] - p[i - 1]) / lp, tq = (p[i + 1] - p[i]) / lq;
                double ds = 0.5 * (lp + lq);
                var kappa = (tq - tp) / ds;                     // curvature vector (|κ| = 2 sin(θ/2)/ds)
                var t = (tp + tq); t = t.LengthSquared > 1e-20 ? t.Normalized() : tq;
                var nrm = nn[i];
                var g = Vec3d.Cross(nrm, t); if (g.LengthSquared > 1e-20) g = g.Normalized();
                r.NormalCurvature[i] = Vec3d.Dot(kappa, nrm);
                r.GeodesicCurvature[i] = Vec3d.Dot(kappa, g);
                r.GeodesicTorsion[i] = 0.5 * (segTau[i - 1] + segTau[i]);
            }
            if (n >= 2) { r.GeodesicTorsion[0] = segTau[0]; r.GeodesicTorsion[n - 1] = segTau[n - 2]; }

            // strains (as Lath Analysis) and energies
            double bend = 0, twist = 0, csq = 0;
            for (int i = 0; i < n; i++)
            {
                double kn = r.NormalCurvature[i], kg = r.GeodesicCurvature[i], tg = r.GeodesicTorsion[i];
                double kB = upright ? kg : kn;   // fibre distance ExtentB, bends about the A axis
                double kA = upright ? kn : kg;   // fibre distance ExtentA, bends about the B axis
                double strain = Math.Max(Math.Abs(kB) * halfB, Math.Max(Math.Abs(kA) * halfA, Math.Abs(tg) * twistFactor));
                r.Utilization[i] = strain / opt.MaxStrain;
                r.MaxUtilization = Math.Max(r.MaxUtilization, r.Utilization[i]);
                if (i > 0 && i + 1 < n)
                {
                    double ds = 0.5 * (segLen[i - 1] + segLen[i]);
                    bend += 0.5 * E * (IA * kB * kB + IB * kA * kA) / (u * u) * (ds * u);   // κ in 1/m, ds in m
                    csq += (kn * kn + kg * kg) * ds;
                }
            }
            for (int i = 0; i + 1 < n; i++)
            {
                twist += 0.5 * G * J * segTau[i] * segTau[i] / (u * u) * (segLen[i] * u);
                csq += segTau[i] * segTau[i] * segLen[i];
                if (gravityDot > 0)
                {
                    // ρ A g h per segment, h measured against gravity from the origin plane
                    double h = -Vec3d.Dot(0.5 * (p[i] + p[i + 1]), down) * u;
                    res.Potential += opt.Density * area * gravityDot * h * (segLen[i] * u);
                }
            }
            r.BendingEnergy = bend; r.TwistEnergy = twist; r.CurvatureSquare = csq;
            res.Laths[l] = r;
            res.BendingEnergy += bend; res.TwistEnergy += twist; res.CurvatureSquare += csq;
            res.MaxUtilization = Math.Max(res.MaxUtilization, r.MaxUtilization);
        }
        return res;
    }

    /// <summary>Every state of a motion.</summary>
    public static List<StateResult> Analyze(ScissorNet net, ScissorNet.Result motion, Options? options = null) =>
        motion.States.Select(s => Analyze(net, s, options)).ToList();

    /// <summary>
    /// Index of the natural state — the least Total (strain energy + potential)
    /// along the motion — or −1 when the diagram is flat (all energies within
    /// 1e-9 relative of each other, or below a nanojoule: a rigid-body
    /// mechanism has no preferred state). Interior minima are what Schikore et al. read off the
    /// curvature-square diagram; a minimum at either end means the grid wants
    /// to go further than the drivers allowed.
    /// </summary>
    public static int NaturalState(IReadOnlyList<StateResult> states)
    {
        if (states.Count == 0) return -1;
        double min = double.MaxValue, max = double.MinValue; int arg = -1;
        for (int i = 0; i < states.Count; i++)
        {
            double t = states[i].Total;
            if (t < min) { min = t; arg = i; }
            if (t > max) max = t;
        }
        if (Math.Abs(max) <= 1e-9 || max - min <= 1e-9 * Math.Abs(max)) return -1;   // nothing stored (below a nanojoule) or flat
        return arg;
    }
}
