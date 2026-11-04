using System;
using System.Linq;
using Mite.Core.Geometry;
using Mite.Core.Kinetics;
using Xunit;
using Xunit.Abstractions;

namespace Mite.Tests;

/// <summary>
/// Kinetic State reads a solved motion at any fold. The exact motion of the
/// doubly ruled hyperboloid grid (<see cref="ScissorNet.HyperboloidMechanism"/>,
/// β = 20° … 40° over Fold 0 … 1) is the reference: at a solved state the
/// reading is exact; between states it is a chord of the motion whose error
/// and joint-spacing drift fall with the square of the step.
/// </summary>
public class KineticMotionTests
{
    private const int N = 12, Levels = 2;
    private const double Rho = 1.0;
    private readonly ITestOutputHelper _out;
    public KineticMotionTests(ITestOutputHelper output) => _out = output;

    private static double Beta(double fold) => (20 + 20 * fold) * Math.PI / 180;

    private static KineticMotion Motion(int states)
    {
        var (nodes, laths, normals) = ScissorNet.HyperboloidMechanism(N, Levels, Rho, Beta(0));
        var net = new ScissorNet(nodes, laths, N, normals);
        var list = Enumerable.Range(0, states).Select(k =>
        {
            double f = k / (double)(states - 1);
            var (p, _, q) = ScissorNet.HyperboloidMechanism(N, Levels, Rho, Beta(f));
            return new ScissorNet.State { Fold = f, Nodes = p, Normals = q };
        }).ToList();
        return new KineticMotion(net, list, new KineticStrain.Options { Profile = new Mite.Core.Fabrication.LathProfile(0.05, 0.005, true) });
    }

    private static double MaxError(ScissorNet.State s, Vec3d[] truth) => s.Nodes.Zip(truth, (a, b) => (a - b).Length).Max();

    [Fact]
    public void AtASolvedState_TheReadingIsThatStateExactly()
    {
        var m = Motion(9);
        foreach (int k in new[] { 0, 3, 8 })
        {
            double f = k / 8.0;
            var s = m.At(f, out int bracket, out double t);
            Assert.Equal(0, t);
            Assert.Equal(k, bracket);
            Assert.Equal(f, s.Fold, 12);
            var (truth, _, _) = ScissorNet.HyperboloidMechanism(N, Levels, Rho, Beta(f));
            Assert.InRange(MaxError(s, truth), 0, 1e-12);
            Assert.InRange(m.SpacingDrift(s), 0, 1e-12);
        }
        // a reading is a copy: changing it leaves the motion alone
        var c = m.At(0.5); c.Nodes[0] = new Vec3d(99, 99, 99);
        Assert.NotEqual(99, m.At(0.5).Nodes[0].X);
    }

    [Fact]
    public void BetweenStates_TheErrorIsSecondOrderInTheStep()
    {
        // midpoints of a coarse and of a twice finer motion, against the closed form there
        double ErrAndDrift(int states, out double drift)
        {
            var m = Motion(states);
            double worst = 0; drift = 0;
            for (int k = 0; k + 1 < states; k++)
            {
                double f = (k + 0.5) / (states - 1);
                var s = m.At(f, out _, out double t);
                Assert.InRange(t, 0.5 - 1e-9, 0.5 + 1e-9);
                var (truth, _, _) = ScissorNet.HyperboloidMechanism(N, Levels, Rho, Beta(f));
                worst = Math.Max(worst, MaxError(s, truth));
                drift = Math.Max(drift, m.SpacingDrift(s));
            }
            return worst;
        }
        double e1 = ErrAndDrift(5, out double d1), e2 = ErrAndDrift(9, out double d2);
        _out.WriteLine($"midpoint error {e1:E3} → {e2:E3} (×{e1 / e2:F2}), spacing drift {d1:E3} → {d2:E3} (×{d1 / d2:F2})");
        Assert.InRange(e1 / e2, 3.5, 4.5);
        Assert.InRange(d1 / d2, 3.5, 4.5);
        Assert.True(e1 > 1e-6, "the coarse motion should show its chord error");
    }

    [Fact]
    public void FoldsOutsideTheRun_AreClampedToItsEnds()
    {
        var m = Motion(5);
        var lo = m.At(-0.3); var hi = m.At(1.7); var nan = m.At(double.NaN);
        Assert.Equal(0, lo.Fold); Assert.Equal(1, hi.Fold); Assert.Equal(1, nan.Fold);
        var (t0, _, _) = ScissorNet.HyperboloidMechanism(N, Levels, Rho, Beta(0));
        Assert.InRange(MaxError(lo, t0), 0, 1e-12);
    }

    [Fact]
    public void TheElasticReading_OfASolvedState_IsUnchanged()
    {
        var m = Motion(5);
        var solved = m.States[2];
        var a = KineticStrain.Analyze(m.Net, solved, m.Strain);
        var b = KineticStrain.Analyze(m.Net, m.At(solved.Fold), m.Strain);
        Assert.Equal(a.MaxUtilization, b.MaxUtilization, 12);
        Assert.Equal(a.Total, b.Total, 9);
    }

    [Fact]
    public void States_MustRunForward()
    {
        var m = Motion(3);
        var back = m.States.Reverse().ToList();
        Assert.Throws<ArgumentException>(() => new KineticMotion(m.Net, back));
        Assert.Throws<ArgumentException>(() => new KineticMotion(m.Net, Array.Empty<ScissorNet.State>()));
    }
}
