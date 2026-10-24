using System;
using System.Linq;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;
using Xunit;
using Xunit.Abstractions;

namespace Mite.Tests;

/// <summary>
/// The Gauss–Newton rows of <see cref="AggWeb"/> against finite differences of
/// the cost: the hard rows (Sauer's angle condition with the derivative of the
/// unit edge vectors, the asymptotic diagonal with the derivative of its
/// normalisation) must be exact; the soft rows that freeze the closest point
/// and the reference normal (proximity, guide) are first-order only, which
/// is the usual guided-projection approximation and shows as a small mismatch.
/// </summary>
public class AggWebJacobianTests
{
    private readonly ITestOutputHelper _out;
    public AggWebJacobianTests(ITestOutputHelper o) { _out = o; }

    [Theory]
    [InlineData(0.0, 0.0, 0.0, 0.0, 0, 1e-6)]     // angle + asymptotic rows
    [InlineData(0.0, 0.0, 0.0, 0.0, 1, 1e-6)]     // asymptotic rows alone
    [InlineData(0.0, 0.0, 0.0, 0.0, 2, 1e-6)]     // angle rows alone
    [InlineData(0.0, 0.0, 0.05, 0.0, 3, 1e-9)]    // fairness
    [InlineData(0.0, 0.0, 0.0, 0.05, 3, 1e-9)]    // border hold (tangential, frozen plane)
    [InlineData(0.3, 0.0, 0.0, 0.0, 3, 1e-3)]     // proximity (frozen closest point)
    [InlineData(0.0, 0.3, 0.0, 0.0, 3, 1e-3)]     // reference guide (frozen normal)
    public void GradientMatchesFiniteDifferences(double prox, double guide, double fair, double hold, int skip, double tolerance)
    {
        var mesh = TestMeshes.CreateCylinder(1.0, 3.0, 128, 24);
        var exact = AggWeb.QuadNet.Cylinder(1.0, 50, 0.15, 6, 5);
        var rnd = new Random(3);
        var nodes = exact.Nodes.Select(p => p + 0.05 * 0.15 * new Vec3d(rnd.NextDouble() * 2 - 1, rnd.NextDouble() * 2 - 1, rnd.NextDouble() * 2 - 1)).ToList();
        var net = AggWeb.QuadNet.FromNeighbors(nodes, exact.NeighborsA, exact.NeighborsB);
        double worst = 0; int worstK = -1; double worstA = 0, worstF = 0;
        var opt = new AggWeb.Options { Proximity = prox, Guide = guide, Fairness = fair, BorderHold = hold, AsymptoticFamily = exact.RulingFamily, SkipGeodesicRows = skip == 1 || skip == 3, SkipAsymptoticRows = skip == 2 || skip == 3 };
        opt.Probe = (cost, grad) =>
        {
            var x0 = net.Nodes.SelectMany(p => new[] { p.X / net.Spacing, p.Y / net.Spacing, p.Z / net.Spacing }).ToArray();
            var g = grad(x0);
            double eps = 1e-6;
            for (int k = 0; k < x0.Length; k++)
            {
                var xp = (double[])x0.Clone(); xp[k] += eps; var xm = (double[])x0.Clone(); xm[k] -= eps;
                double fd = (cost(xp) - cost(xm)) / (2 * eps) / 2;
                double e = Math.Abs(fd - g[k]);
                if (e > worst) { worst = e; worstK = k; worstA = g[k]; worstF = fd; }
            }
        };
        AggWeb.Optimize(mesh, net, opt);
        _out.WriteLine($"worst |analytic − FD| = {worst:G3} at unknown {worstK} (analytic {worstA:G6}, FD {worstF:G6})");
        Assert.InRange(worst, 0, tolerance);
    }
}
