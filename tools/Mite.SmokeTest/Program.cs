using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MiteSmokeTest;

/// <summary>
/// Headless smoke test: boots Rhino via Rhino.Inside, instantiates every Mite
/// Grasshopper component with real Rhino meshes, solves them, and checks the
/// outputs. Verifies the component layer (params, icons, conversions) that the
/// unit tests cannot reach. Run from a machine with a licensed Rhino 8.
/// </summary>
internal static class Program
{
    private const string RhinoSystemDir = @"C:\Program Files\Rhino 8\System";

    [STAThread]
    private static int Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            string name = new AssemblyName(args.Name).Name + ".dll";
            string path = Path.Combine(RhinoSystemDir, name);
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };

        try
        {
            return Run();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FATAL: {ex}");
            return 1;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run()
    {
        using var core = new Rhino.Runtime.InProcess.RhinoCore(
            new[] { "/NOSPLASH" }, Rhino.Runtime.InProcess.WindowStyle.NoWindow);

        // Load the Grasshopper plugin so GH types have their runtime
        Rhino.PlugIns.PlugIn.LoadPlugIn(new Guid("b45a29b1-4343-4035-989e-044e8580d9cf"));

        return Tests.RunAll();
    }
}

internal static class Tests
{
    private static int _passed, _failed;

    public static int RunAll()
    {
        // The frame analysis converts model units to metres; keep the analytic
        // checks below in metres whatever document Rhino.Inside opened
        if (Rhino.RhinoDoc.ActiveDoc != null)
            Rhino.RhinoDoc.ActiveDoc.ModelUnitSystem = Rhino.UnitSystem.Meters;

        var sphere = Rhino.Geometry.Mesh.CreateFromSphere(
            new Rhino.Geometry.Sphere(Rhino.Geometry.Plane.WorldXY, 1.0), 48, 24);
        var saddle = BuildSaddle(20, 2.0);
        var grid = BuildBumpyGrid(10, 10);

        Check("Icons load for all 31 components and the Mite Net parameter", IconsLoad());
        Check("Icons load for all 31 components", IconsLoad());

        TestComponent("Principal Curvature",
            new Mite.Grasshopper.Components.PrincipalCurvatureComponent(),
            c => SetInputs(c, (0, sphere.DuplicateMesh())),
            c =>
            {
                double k1 = Numbers(c, 0).Average();
                return Expect(Math.Abs(Math.Abs(k1) - 1.0) < 0.15, $"mean |K1| ~ 1, got {k1:F3}")
                    && Expect(Numbers(c, 0).Count == sphere.Vertices.Count, "one K1 per vertex");
            });

        TestComponent("Gaussian Curvature",
            new Mite.Grasshopper.Components.GaussianCurvatureComponent(),
            c => SetInputs(c, (0, sphere.DuplicateMesh())),
            c =>
            {
                double k = Numbers(c, 0).Average();
                return Expect(Math.Abs(k - 1.0) < 0.15, $"mean K ~ 1 on unit sphere, got {k:F3}");
            });

        TestComponent("Mean Curvature",
            new Mite.Grasshopper.Components.MeanCurvatureComponent(),
            c => SetInputs(c, (0, sphere.DuplicateMesh())),
            c =>
            {
                double h = Numbers(c, 0).Average();
                return Expect(Math.Abs(Math.Abs(h) - 1.0) < 0.15, $"mean |H| ~ 1 on unit sphere, got {h:F3}");
            });

        TestComponent("Curvature Streamlines",
            new Mite.Grasshopper.Components.StreamlinesComponent(),
            c => SetInputs(c, (0, saddle.DuplicateMesh()), (1, CenterVertex(saddle)), (2, 0.05), (3, 200)),
            c => Expect(CurveCount(c, 0) >= 1, $"streamline traced, got {CurveCount(c, 0)}"));

        TestComponent("Planarize Mesh",
            new Mite.Grasshopper.Components.PlanarizationComponent(),
            c => SetInputs(c, (0, grid.DuplicateMesh()), (1, 50)),
            c =>
            {
                // Method 1 (default): the bumpy grid is planar to the tolerance in a few iterations and Moved is reported
                var dev = Numbers(c, 1);
                return Expect(MeshOut(c, 0) != null && dev.Count > 0, "planarized mesh + deviations")
                    && Expect(dev.Max() < 1e-5, $"guided projection planar to 1e-5, got {dev.Max():E2}")
                    && Expect(Numbers(c, 2)[0] <= 30, $"in at most 30 iterations, took {Numbers(c, 2)[0]}")
                    && Expect(Numbers(c, 3).Count == 1 && Numbers(c, 3)[0] > 0, "Moved output");
            });

        TestComponent("Planarize Mesh (Method 0, projection)",
            new Mite.Grasshopper.Components.PlanarizationComponent(),
            c => SetInputs(c, (0, grid.DuplicateMesh()), (1, 50), (5, 0)),
            c => Expect(MeshOut(c, 0) != null && Numbers(c, 1).Count > 0 && Numbers(c, 2)[0] == 50, "projection runs its 50 iterations"));

        TestComponent("Minimal Surface",
            new Mite.Grasshopper.Components.MinimalSurfaceComponent(),
            c =>
            {
                var m = grid.DuplicateMesh();
                m.Faces.ConvertQuadsToTriangles();
                SetInputs(c, (0, m), (2, 50));
                SetBoolList(c, 1, BorderFlags(m));
            },
            c => Expect(MeshOut(c, 0) != null, "minimal surface mesh produced"));

        TestComponent("Force Density Method",
            new Mite.Grasshopper.Components.ForceDensityComponent(),
            c =>
            {
                var m = grid.DuplicateMesh();
                m.Faces.ConvertQuadsToTriangles();
                SetInputs(c, (0, m));
                SetNumberList(c, 1, Enumerable.Repeat(1.0, 2000).ToArray());
                SetBoolList(c, 3, BorderFlags(m));
            },
            c => Expect(MeshOut(c, 0) != null, "equilibrium mesh produced"));

        TestComponent("Asymptotic Net (seeded)",
            new Mite.Grasshopper.Components.AsymptoticNetComponent(),
            c => SetInputs(c, (0, saddle.DuplicateMesh()), (1, CenterVertex(saddle)), (2, 0.05), (3, 200), (4, false)),
            c => Expect(CurveCount(c, 0) >= 1 && CurveCount(c, 1) >= 1,
                $"both families traced: A={CurveCount(c, 0)}, B={CurveCount(c, 1)}"));

        TestComponent("Asymptotic Net (auto-spaced)",
            new Mite.Grasshopper.Components.AsymptoticNetComponent(),
            c => SetInputs(c, (0, saddle.DuplicateMesh()), (2, 0.05), (3, 200), (4, true), (5, 0.3)),
            c => Expect(CurveCount(c, 0) >= 2, $"auto-spaced family A has {CurveCount(c, 0)} curves"));

        TestComponent("Asymptotic Net (guide curve)",
            new Mite.Grasshopper.Components.AsymptoticNetComponent(),
            c =>
            {
                // the diagonal x = y of the saddle z = x² − y² is one asymptotic line: a
                // guide along it seeds the other family every Spacing along the diagonal
                var bb = saddle.GetBoundingBox(true);
                var guide = new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(bb.Min.X, bb.Min.Y, 0), new Rhino.Geometry.Point3d(bb.Max.X, bb.Max.Y, 0));
                SetInputs(c, (0, saddle.DuplicateMesh()), (2, 0.05), (3, 200), (4, true), (5, 0.3), (11, guide));
            },
            c => Expect(CurveCount(c, 0) + CurveCount(c, 1) >= 4,
                $"guide web: A={CurveCount(c, 0)}, B={CurveCount(c, 1)} (one family along the guide, the other through its nodes)"));

        TestComponent("Asymptotic Web",
            new Mite.Grasshopper.Components.AsymptoticWebComponent(),
            c => SetInputs(c, (0, saddle.DuplicateMesh()), (2, 0.2)),
            c => Expect(CurveCount(c, 0) >= 3 && CurveCount(c, 1) >= 3 && MeshOut(c, 4) != null && MeshOut(c, 4)!.Faces.Count > 20,
                $"web: A={CurveCount(c, 0)}, B={CurveCount(c, 1)}, quads={MeshOut(c, 4)?.Faces.Count ?? 0}"));

        TestComponent("Asymptotic Web (AAG)",
            new Mite.Grasshopper.Components.AsymptoticWebComponent(),
            c => SetInputs(c, (0, saddle.DuplicateMesh()), (2, 0.25), (4, true)),
            c => Expect(CurveCount(c, 2) >= 3, $"geodesic diagonals: {CurveCount(c, 2)}"));

        TestComponent("Asymptotic Web (AAG, refined)",
            new Mite.Grasshopper.Components.AsymptoticWebComponent(),
            c => SetInputs(c, (0, saddle.DuplicateMesh()), (2, 0.25), (4, true), (8, 1)),
            c => Expect(CurveCount(c, 2) >= 3 && MeshOut(c, 6) != null && MeshOut(c, 6)!.Faces.Count == 4 * (MeshOut(c, 4)?.Faces.Count ?? -1),
                $"refined: G={CurveCount(c, 2)}, fine quads={MeshOut(c, 6)?.Faces.Count ?? 0}, web quads={MeshOut(c, 4)?.Faces.Count ?? 0}"));
        // AGG Web: the exact cylinder web (helices at ±50°, the rulings as diagonals) on a
        // cylinder mesh must come out as it went in — both errors near zero
        TestComponent("AGG Web (cylinder web)",
            new Mite.Grasshopper.Components.AggWebComponent(),
            c =>
            {
                var cylNet = Mite.Core.Gridshells.AggWeb.QuadNet.Cylinder(1.0, 50, 0.15, 10, 8);
                Rhino.Geometry.Curve[] Laths(bool familyA) => cylNet.Chains(familyA)
                    .Select(ch => (Rhino.Geometry.Curve)new Rhino.Geometry.PolylineCurve(ch.Select(i => new Rhino.Geometry.Point3d(cylNet.Nodes[i].X, cylNet.Nodes[i].Y, cylNet.Nodes[i].Z))))
                    .ToArray();
                var cyl = Rhino.Geometry.Mesh.CreateFromCylinder(
                    new Rhino.Geometry.Cylinder(new Rhino.Geometry.Circle(new Rhino.Geometry.Plane(new Rhino.Geometry.Point3d(0, 0, -1.5), Rhino.Geometry.Vector3d.ZAxis), 1.0), 3.0), 24, 128);
                SetInputs(c, (0, cyl), (3, -1), (4, 20));
                SetCurveList(c, 1, Laths(true));
                SetCurveList(c, 2, Laths(false));
            },
            c =>
            {
                var ge = Numbers(c, 7); var asy = Numbers(c, 8);
                return Expect(CurveCount(c, 0) == 8 && CurveCount(c, 1) == 10 && CurveCount(c, 2) >= 10 && Points(c, 4).Count == 80,
                        $"8 + 10 laths, ≥ 10 asymptotic diagonals, 80 nodes: got {CurveCount(c, 0)} + {CurveCount(c, 1)}, {CurveCount(c, 2)}, {Points(c, 4).Count}")
                    && Expect(ge.Count == 1 && ge[0] < 0.05 && asy.Count == 1 && asy[0] < 0.05,
                        $"geodesic {(ge.Count > 0 ? ge[0] : -1):G3}°, asymptotic {(asy.Count > 0 ? asy[0] : -1):G3}° (cylinder caps included in the mesh)");
            });

        TestComponent("Geodesic Net",
            new Mite.Grasshopper.Components.GeodesicNetComponent(),
            c =>
            {
                SetInputs(c, (0, sphere.DuplicateMesh()), (1, 0), (3, 0.05), (4, 140));
                SetVectorList(c, 2, new Rhino.Geometry.Vector3d(0, 1, 0));
            },
            c => Expect(CurveCount(c, 0) >= 1, $"geodesic traced, got {CurveCount(c, 0)}"));

        TestComponent("Chebyshev Net",
            new Mite.Grasshopper.Components.ChebyshevNetComponent(),
            c => SetInputs(c, (0, sphere.DuplicateMesh()), (1, 0), (3, 0.2), (4, 5), (5, 5)),
            c =>
            {
                var mesh = MeshOut(c, 3);
                var angles = Numbers(c, 4); var cells = Numbers(c, 5);
                return Expect(CurveCount(c, 1) >= 3 && CurveCount(c, 2) >= 3, "lath families produced")
                    && Expect(mesh != null && mesh.Faces.Count > 10, $"net mesh has {mesh?.Faces.Count ?? 0} faces")
                    && Expect(Texts(c, 5).FirstOrDefault()?.Contains("edge-length error") == true, "report text");
            });

        // MinAngle (input 8, appended 2026-10-19): a 12 × 12 patch on the unit
        // sphere shears below 30° at its corners; with MinAngle 30° every
        // reported angle stays within [30°, 150°] and the report counts the
        // refused nodes
        TestComponent("Chebyshev Net (MinAngle)",
            new Mite.Grasshopper.Components.ChebyshevNetComponent(),
            c => SetInputs(c, (0, sphere.DuplicateMesh()), (1, 0), (3, 0.2), (4, 12), (5, 12), (8, Math.PI / 6)),
            c =>
            {
                var angles = Numbers(c, 4).Where(a => !double.IsNaN(a)).ToList();
                string report = Texts(c, 5).FirstOrDefault() ?? "";
                return Expect(angles.Count > 50 && angles.All(a => a >= 30 - 1e-6 && a <= 150 + 1e-6),
                        $"all {angles.Count} angles within [30°, 150°], min {(angles.Count > 0 ? angles.Min() : 0):F1}")
                    && Expect(!report.Contains("locked by MinAngle 0;"), $"locked nodes reported: {report}");
                    && Expect(angles.Count > 10 && angles.TrueForAll(a => double.IsNaN(a) || (a > 0 && a < 180)), $"{angles.Count} node angles")
                    && Expect(cells.Count > 10 && cells.TrueForAll(k => k > 0), $"{cells.Count} cells read K > 0 on the sphere (Hazzidakis sum {cells.Sum():F3})");
            });

        TestComponent("Lath Analysis",
            new Mite.Grasshopper.Components.LathAnalysisComponent(),
            c =>
            {
                SetInputs(c, (0, sphere.DuplicateMesh()));
                var circle = new Rhino.Geometry.Circle(Rhino.Geometry.Plane.WorldXY, 1.0);
                SetCurveList(c, 1, circle.ToNurbsCurve());
            },
            c =>
            {
                var utils = Numbers(c, 1);
                return Expect(Bools(c, 0).Count == 1, "one buildable flag per lath")
                    && Expect(utils.Count == 1 && utils[0] > 0, $"utilization computed: {(utils.Count > 0 ? utils[0] : 0):F2}");
            });

        TestComponent("Lath Sweep",
            new Mite.Grasshopper.Components.LathSweepComponent(),
            c =>
            {
                SetInputs(c, (0, sphere.DuplicateMesh()), (2, 0.1), (3, 0.02));
                var circle = new Rhino.Geometry.Circle(Rhino.Geometry.Plane.WorldXY, 1.0);
                SetCurveList(c, 1, circle.ToNurbsCurve());
            },
            c =>
            {
                var laths = Meshes(c, 0);
                return Expect(laths.Count == 1 && laths[0].Faces.Count > 20,
                    $"one swept lath, got {laths.Count}");
            });

        TestComponent("Net Joints",
            new Mite.Grasshopper.Components.NetJointsComponent(),
            c =>
            {
                var plane = new Rhino.Geometry.Mesh();
                plane.Vertices.Add(-2, -2, 0);
                plane.Vertices.Add(2, -2, 0);
                plane.Vertices.Add(2, 2, 0);
                plane.Vertices.Add(-2, 2, 0);
                plane.Faces.AddFace(0, 1, 2, 3);
                SetInputs(c, (0, plane), (3, 0.2), (4, 0.05));
                // Non-concurrent lines: each crossing lands at a distinct location
                SetCurveList(c, 1,
                    new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(-1, -1, 0), new Rhino.Geometry.Point3d(1, 1, 0)),
                    new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(0.5, -1, 0), new Rhino.Geometry.Point3d(0.5, 1, 0)));
                SetCurveList(c, 2,
                    new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(-1, 0, 0), new Rhino.Geometry.Point3d(1, 0, 0)));
            },
            c =>
            {
                var pts = Points(c, 0);
                return Expect(pts.Count == 2, $"2 crossings, got {pts.Count}")
                    && Expect(Boxes(c, 3).Count == 2 && Boxes(c, 4).Count == 2, "notch solids for both families");
            });

        TestComponent("Mesh Cleanup",
            new Mite.Grasshopper.Components.MeshCleanupComponent(),
            c => SetInputs(c, (0, sphere.DuplicateMesh()), (1, 0.0), (2, true)),
            c => Expect(MeshOut(c, 0) != null && MeshOut(c, 0)!.Faces.Count > 0,
                "cleaned mesh produced"));

        TestComponent("Mesh Cleanup (inside-out sphere, Sliver 1)",
            new Mite.Grasshopper.Components.MeshCleanupComponent(),
            c =>
            {
                var inside = sphere.DuplicateMesh();
                inside.Flip(true, true, true);
                SetInputs(c, (0, inside), (1, 0.0), (2, true), (3, 1.0));
            },
            c =>
            {
                var m = MeshOut(c, 0);
                int flipped = c.Params.Output[4].VolatileData.AllData(true)
                    .OfType<Grasshopper.Kernel.Types.GH_Integer>().Select(i => i.Value).FirstOrDefault();
                string report = Texts(c, 7).FirstOrDefault() ?? "";
                int outward = 0;
                if (m != null)
                {
                    m.FaceNormals.ComputeFaceNormals();
                    for (int i = 0; i < m.Faces.Count; i++)
                        if (m.FaceNormals[i] * (Rhino.Geometry.Vector3d)m.Faces.GetFaceCenter(i) > 0) outward++;
                }
                return Expect(m != null && flipped > 0 && outward == m.Faces.Count,
                    $"turned outward: {flipped} faces reversed, {outward} of {m?.Faces.Count} face normals point out; report: {report.Replace('\n', ' ')}");
            });

        TestComponent("Gridshell Analysis (two layers: upper lath pulls, lower lath twists by e·F)",
            new Mite.Grasshopper.Components.GridshellAnalysisComponent(),
            c =>
            {
                var plane = new Rhino.Geometry.Mesh();
                plane.Vertices.Add(-2, -2, 0);
                plane.Vertices.Add(6, -2, 0);
                plane.Vertices.Add(6, 6, 0);
                plane.Vertices.Add(-2, 6, 0);
                plane.Faces.AddFace(0, 1, 2, 3);
                // A: cantilever along x on layer 0; B: along y on layer 1, hanging on A's tip;
                // load 300 N/m along y: B pulls on A's tip one lath depth (0.02) above A's axis
                SetInputs(c, (0, plane), (4, new Rhino.Geometry.Vector3d(0, 300, 0)), (7, 0.1), (8, 0.02), (12, 100.0), (22, 1));
                SetCurveList(c, 1,
                    new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(0, 0, 0), new Rhino.Geometry.Point3d(4, 0, 0)),
                    new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(4, 0, 0), new Rhino.Geometry.Point3d(4, 2, 0)));
                SetPointList(c, 2, new Rhino.Geometry.Point3d(4, 0, 0));
                SetPointList(c, 3, new Rhino.Geometry.Point3d(0, 0, 0));
                SetPersistent(c, 21, 0, 1);
            },
            c =>
            {
                // Align 1 (centred): A's axis on the surface, B's one depth (0.02) above it;
                // torque on A = e · q · b = 0.02 · 300 · 2 = 12 N·m — the reaction moment's x component
                var moments = Vectors(c, 11);
                string report = c.Params.Output[10].VolatileData.AllData(true).OfType<Grasshopper.Kernel.Types.GH_String>().Select(t => t.Value).FirstOrDefault() ?? "";
                double mx = moments.Count > 0 ? System.Math.Abs(moments[0].X) : double.NaN;
                return Expect(moments.Count == 1 && System.Math.Abs(mx - 12.0) < 1e-6, $"reaction torque {mx:F6} N·m vs e·q·b = 12")
                    && Expect(report.Contains("eccentricity 20 mm"), $"report names the stack: {report.Replace('\n', ' ')}");
            });

        TestComponent("Conjugate Net",
            new Mite.Grasshopper.Components.ConjugateNetComponent(),
            c => SetInputs(c, (0, saddle.DuplicateMesh()), (1, 0.25), (2, 0.05), (3, 300)),
            c => Expect(CurveCount(c, 0) >= 1 && CurveCount(c, 1) >= 1,
                $"both families traced: A={CurveCount(c, 0)}, B={CurveCount(c, 1)}"));

        TestComponent("Umbilics",
            new Mite.Grasshopper.Components.UmbilicsComponent(),
            c => SetInputs(c, (0, sphere.DuplicateMesh()), (1, 0.1)),
            c => Expect(Points(c, 0).Count > sphere.Vertices.Count / 2,
                $"sphere is mostly umbilical: {Points(c, 0).Count} points"));

        // triaxial ellipsoid 1.5 × 1 × 0.7: exactly four isolated umbilics of index +½ at
        // (±1.264, 0, ±0.377), the Umbilics / Index outputs appended in 1.4
        var ellipsoid = sphere.DuplicateMesh();
        ellipsoid.Transform(Rhino.Geometry.Transform.Scale(Rhino.Geometry.Plane.WorldXY, 1.5, 1.0, 0.7));
        TestComponent("Umbilics (ellipsoid)",
            new Mite.Grasshopper.Components.UmbilicsComponent(),
            c => SetInputs(c, (0, ellipsoid), (1, 0.05), (2, 0.0)),
            c =>
            {
                var u = Points(c, 3); var ix = Numbers(c, 4);
                double x0 = 1.5 * Math.Sqrt((2.25 - 1.0) / (2.25 - 0.49)), z0 = 0.7 * Math.Sqrt((1.0 - 0.49) / (2.25 - 0.49));
                double worst = u.Count == 0 ? 99 : u.Max(p => Math.Min(Math.Abs(Math.Abs(p.X) - x0), 99) + Math.Abs(p.Y) + Math.Abs(Math.Abs(p.Z) - z0));
                return Expect(u.Count == 4 && ix.All(i => Math.Abs(i - 0.5) < 1e-9) && worst < 0.15,
                    $"four lemons at (±{x0:F3}, 0, ±{z0:F3}): {u.Count} located, indices {string.Join(" ", ix)}, worst offset {worst:F3}");
            });

        TestComponent("Test Shape (sphere: exact K = H = 1 at every vertex, poles included)",
            new Mite.Grasshopper.Components.TestShapeComponent(),
            c => SetInputs(c, (0, "sphere"), (4, 24)),
            c =>
            {
                var m = MeshOut(c, 0); var K = Numbers(c, 1); var H = Numbers(c, 2); var K1 = Numbers(c, 3);
                bool exact = K.Count == m?.Vertices.Count && K.All(k => System.Math.Abs(k - 1) < 1e-6) && H.All(h => System.Math.Abs(h - 1) < 1e-6) && K1.All(k => System.Math.Abs(k - 1) < 1e-6);
                return Expect(exact, $"{K.Count} vertices, K = {K.Min():F6} … {K.Max():F6}, H = {H.Min():F6} … {H.Max():F6}");
            });

        TestComponent("Test Shape (hyperboloid: asymptotic directions are the rulings, K = −1 on the throat)",
            new Mite.Grasshopper.Components.TestShapeComponent(),
            c => SetInputs(c, (0, "hyperboloid"), (4, 24)),
            c =>
            {
                var m = MeshOut(c, 0)!; var K = Numbers(c, 1); var A1 = Vectors(c, 7);
                int throat = Enumerable.Range(0, m.Vertices.Count).OrderBy(i => System.Math.Abs(m.Vertices[i].Z)).First();
                bool ok = System.Math.Abs(K[throat] + 1) < 1e-9 && A1.Count == m.Vertices.Count && A1.All(a => System.Math.Abs(a.Length - 1) < 1e-9);
                return Expect(ok, $"K at the throat {K[throat]:F9}, {A1.Count} unit asymptotic directions");
            });

        TestComponent("Gridshell Analysis",
            new Mite.Grasshopper.Components.GridshellAnalysisComponent(),
            c =>
            {
                var plane = new Rhino.Geometry.Mesh();
                plane.Vertices.Add(-2, -2, 0);
                plane.Vertices.Add(2, -2, 0);
                plane.Vertices.Add(2, 2, 0);
                plane.Vertices.Add(-2, 2, 0);
                plane.Faces.AddFace(0, 1, 2, 3);
                SetInputs(c, (0, plane), (7, 0.1), (8, 0.02));
                SetCurveList(c, 1,
                    new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(-1.5, -1, 0), new Rhino.Geometry.Point3d(1.5, -1, 0)),
                    new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(-1.5, 0, 0), new Rhino.Geometry.Point3d(1.5, 0, 0)),
                    new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(-1.5, 1, 0), new Rhino.Geometry.Point3d(1.5, 1, 0)));
                SetPointList(c, 3,
                    new Rhino.Geometry.Point3d(-1.5, -1, 0), new Rhino.Geometry.Point3d(-1.5, 0, 0),
                    new Rhino.Geometry.Point3d(-1.5, 1, 0));
            },
            c =>
            {
                double maxDisp = Numbers(c, 1).FirstOrDefault();
                // Cantilever UDL theory: delta = w L^4 / (8 E I) with w=1000 N/m,
                // L=3, E=11 GPa, I = 0.1*0.02^3/12 for the flat 0.1x0.02 lath
                double I = 0.1 * System.Math.Pow(0.02, 3) / 12.0;
                double expected = 1000.0 * System.Math.Pow(3.0, 4) / (8.0 * 11e9 * I);
                double err = System.Math.Abs(maxDisp - expected) / expected;
                return Expect(CurveCount(c, 0) == 3, "3 deformed laths")
                    && Expect(err < 0.05, $"deflection {maxDisp:F3} vs analytic {expected:F3} ({err:P1})");
            });

        TestComponent("Net Distributions",
            new Mite.Grasshopper.Components.NetDistributionsComponent(),
            c =>
            {
                // latitudes at polar angles 60°..120° every 15° (A) and four meridians (B):
                // the cut across a latitude follows the meridian, so every spacing is the
                // chord 2 sin(7.5°) = 0.26105, and the families cross at 90°
                var lats = new System.Collections.Generic.List<Rhino.Geometry.Curve>();
                for (int k = 0; k < 5; k++)
                {
                    double th = (60 + 15 * k) * System.Math.PI / 180;
                    var pl = new Rhino.Geometry.Plane(new Rhino.Geometry.Point3d(0, 0, System.Math.Cos(th)), Rhino.Geometry.Vector3d.ZAxis);
                    lats.Add(new Rhino.Geometry.Circle(pl, System.Math.Sin(th)).ToNurbsCurve());
                }
                var mers = new System.Collections.Generic.List<Rhino.Geometry.Curve>();
                for (int k = 0; k < 4; k++)
                {
                    double ph = k * System.Math.PI / 4;
                    var pl = new Rhino.Geometry.Plane(Rhino.Geometry.Point3d.Origin, new Rhino.Geometry.Vector3d(System.Math.Cos(ph), System.Math.Sin(ph), 0), Rhino.Geometry.Vector3d.ZAxis);
                    mers.Add(new Rhino.Geometry.Circle(pl, 1.0).ToNurbsCurve());
                }
                SetInputs(c, (0, sphere.DuplicateMesh()), (6, 0.001));
                SetCurveList(c, 1, lats.ToArray());
                SetCurveList(c, 2, mers.ToArray());
            },
            c =>
            {
                var sp = Numbers(c, 0).Where(v => !double.IsNaN(v)).OrderBy(v => v).ToList();
                double med = sp.Count > 0 ? sp[sp.Count / 2] : 0;
                double expected = 2 * System.Math.Sin(7.5 * System.Math.PI / 180);
                var ang = Numbers(c, 2);
                return Expect(System.Math.Abs(med - expected) < 0.01 * expected, $"median spacing {med:F4} vs 2 sin 7.5° = {expected:F4}")
                    && Expect(ang.Count == 40 && ang.All(a => a > 89), $"{ang.Count} crossings (expected 5 × 4 × 2 = 40), all near 90°")
                    && Expect(Texts(c, 6).Count == 1 && Texts(c, 6)[0].Contains("Spacing A"), "report written");
            });

        TestComponent("Lath Unroll",
            new Mite.Grasshopper.Components.LathUnrollComponent(),
            c =>
            {
                SetInputs(c, (0, sphere.DuplicateMesh()), (2, 0.1));
                var circle = new Rhino.Geometry.Circle(Rhino.Geometry.Plane.WorldXY, 1.0);
                SetCurveList(c, 1, circle.ToNurbsCurve());
            },
            c =>
            {
                var lengths = Numbers(c, 2);
                var pattern = c.Params.Output[0].VolatileData.AllData(true)
                    .OfType<Grasshopper.Kernel.Types.GH_Curve>().Select(x => x.Value).FirstOrDefault();
                var bbox = pattern?.GetBoundingBox(false) ?? Rhino.Geometry.BoundingBox.Empty;
                // A closed lath opens at its seam into a band ~2π long and 0.1 wide
                return Expect(CurveCount(c, 0) == 1 && lengths.Count == 1
                    && System.Math.Abs(lengths[0] - 2 * System.Math.PI) < 0.1,
                    $"equator pattern, length {(lengths.Count > 0 ? lengths[0] : 0):F3}")
                    && Expect(pattern != null && pattern.IsClosed && bbox.Diagonal.Length > 5.0 && bbox.Diagonal.Length < 7.0,
                        $"pattern is a closed band (diagonal {bbox.Diagonal.Length:F2})");
            });

        TestComponent("Lath Strip",
            new Mite.Grasshopper.Components.LathStripComponent(),
            c =>
            {
                SetInputs(c, (0, sphere.DuplicateMesh()), (2, 0.1));
                var circle = new Rhino.Geometry.Circle(Rhino.Geometry.Plane.WorldXY, 1.0);
                SetCurveList(c, 1, circle.ToNurbsCurve());
            },
            c =>
            {
                // A flat plank on the equator: the tangent developable is the cylinder band,
                // developable all round, no edge strain, a straight pattern ~2π long
                var dev = Numbers(c, 4);
                var strain = Numbers(c, 5);
                var pattern = c.Params.Output[3].VolatileData.AllData(true)
                    .OfType<Grasshopper.Kernel.Types.GH_Curve>().Select(x => x.Value).FirstOrDefault();
                var bbox = pattern?.GetBoundingBox(false) ?? Rhino.Geometry.BoundingBox.Empty;
                return Expect(dev.Count == 1 && dev[0] > 0.95 && strain.Count == 1 && strain[0] < 1e-3,
                    $"equator strip developable {(dev.Count > 0 ? dev[0] : 0):P0}, edge strain {(strain.Count > 0 ? strain[0] : 1):E1}")
                    && Expect(pattern != null && pattern.IsClosed && bbox.Diagonal.Length > 5.0 && bbox.Diagonal.Length < 7.0,
                        $"pattern is a closed band (diagonal {bbox.Diagonal.Length:F2})");
            });

        TestComponent("Lath Segment",
            new Mite.Grasshopper.Components.LathSegmentComponent(),
            c =>
            {
                var plane = new Rhino.Geometry.Mesh();
                plane.Vertices.Add(-6, -2, 0);
                plane.Vertices.Add(6, -2, 0);
                plane.Vertices.Add(6, 2, 0);
                plane.Vertices.Add(-6, 2, 0);
                plane.Faces.AddFace(0, 1, 2, 3);
                SetInputs(c, (0, plane), (2, 2.0), (3, 0.05));
                SetCurveList(c, 1,
                    new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(-5, 0, 0), new Rhino.Geometry.Point3d(5, 0, 0)));
            },
            c => Expect(CurveCount(c, 0) >= 4 && Points(c, 1).Count >= 3,
                $"10-unit lath in 2-unit stock: {CurveCount(c, 0)} segments, {Points(c, 1).Count} cuts"));

        TestComponent("Lath Labels",
            new Mite.Grasshopper.Components.LathLabelsComponent(),
            c =>
            {
                SetInputs(c, (1, "A"));
                SetCurveList(c, 0,
                    new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(0, 0, 0), new Rhino.Geometry.Point3d(1, 0, 0)),
                    new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(0, 1, 0), new Rhino.Geometry.Point3d(1, 1, 0)));
            },
            c => Expect(Points(c, 0).Count == 2 && Texts(c, 1).Count == 2 && Texts(c, 1)[0] == "A000",
                "two labeled laths + CSV report"));

        TestComponent("Lath Preview",
            new Mite.Grasshopper.Components.LathPreviewComponent(),
            c =>
            {
                var m1 = new Rhino.Geometry.Mesh();
                m1.Vertices.Add(0, 0, 0); m1.Vertices.Add(1, 0, 0); m1.Vertices.Add(0, 1, 0);
                m1.Faces.AddFace(0, 1, 2);
                var m2 = m1.DuplicateMesh();
                m2.Translate(new Rhino.Geometry.Vector3d(0, 2, 0));
                SetMeshList(c, 0, m1, m2);
                SetNumberList(c, 1, new[] { 0.5, 1.5 });
            },
            c =>
            {
                var ms = Meshes(c, 0);
                // Mite.Core MitePalette.Utilization: 0.5 → green side, 1.5 → dark red (89, 0, 20), as in the web app
                var c0 = ms.Count == 2 ? ms[0].VertexColors[0] : System.Drawing.Color.Empty;
                var c1 = ms.Count == 2 ? ms[1].VertexColors[0] : System.Drawing.Color.Empty;
                var e0 = Mite.Core.Display.MitePalette.Utilization(0.5);
                return Expect(ms.Count == 2, "two colored laths")
                    && Expect(c0.R == e0.R && c0.G == e0.G && c0.B == e0.B, $"u = 0.5 in the palette's green ({c0.R},{c0.G},{c0.B})")
                    && Expect(c1.R == 89 && c1.G == 0 && c1.B == 20, $"u = 1.5 dark red ({c1.R},{c1.G},{c1.B})");
            });

        TestComponent("Dynamic Relaxation",
            new Mite.Grasshopper.Components.DynamicRelaxationComponent(),
            c => SetInputs(c, (0, grid.DuplicateMesh()), (2, 10.0), (4, new Rhino.Geometry.Vector3d(0, 0, -1)), (8, 3000)),
            c =>
            {
                var m = MeshOut(c, 0);
                var bb = m?.GetBoundingBox(false) ?? Rhino.Geometry.BoundingBox.Empty;
                return Expect(m != null && bb.Min.Z < -0.5, $"net sags under gravity (min z {bb.Min.Z:F2})")
                    && Expect(Bools(c, 5).FirstOrDefault(), "converged");
            });

        TestComponent("Dynamic Relaxation (elastic laths)",
            new Mite.Grasshopper.Components.DynamicRelaxationComponent(),
            c =>
            {
                // a quad strip of 5 columns × 16 rows, length 1, pushed to a chord of 0.8: the
                // column laths buckle into the Euler elastica (rise 0.2663 of the length)
                var m = new Rhino.Geometry.Mesh();
                int nx = 4, ny = 16;
                var coreVerts = new System.Collections.Generic.List<Mite.Core.Geometry.Vec3d>();
                var coreFaces = new System.Collections.Generic.List<int[]>();
                for (int j = 0; j <= ny; j++)
                    for (int i = 0; i <= nx; i++)
                    {
                        m.Vertices.Add(i * 0.2, j * 0.8 / ny, 0.05 * Math.Sin(Math.PI * j / ny));
                        coreVerts.Add(new Mite.Core.Geometry.Vec3d(i * 0.2, j * 0.8 / ny, 0.05 * Math.Sin(Math.PI * j / ny)));
                    }
                for (int j = 0; j < ny; j++)
                    for (int i = 0; i < nx; i++)
                    {
                        m.Faces.AddFace(j * (nx + 1) + i, j * (nx + 1) + i + 1, (j + 1) * (nx + 1) + i + 1, (j + 1) * (nx + 1) + i);
                        coreFaces.Add(new[] { j * (nx + 1) + i, j * (nx + 1) + i + 1, (j + 1) * (nx + 1) + i + 1, (j + 1) * (nx + 1) + i });
                    }
                var fixedFlags = new bool[m.Vertices.Count];
                for (int i = 0; i <= nx; i++) { fixedFlags[i] = true; fixedFlags[ny * (nx + 1) + i] = true; }
                SetInputs(c, (0, m), (2, 1e6 * ny), (8, 200000), (9, 1e-8), (10, 1.0));
                SetBoolList(c, 1, fixedFlags);
                // rest lengths: 1/ny along the columns (index difference nx + 1), 0.2 along the rows
                var edges = new Mite.Core.Geometry.MeshData(coreVerts.ToArray(), coreFaces.ToArray()).BuildEdges();
                SetNumberList(c, 11, edges.Select(e => e.v1 - e.v0 == 1 ? 0.2 : 1.0 / ny).ToArray());
            },
            c =>
            {
                var mo = MeshOut(c, 0);
                var bb = mo?.GetBoundingBox(false) ?? Rhino.Geometry.BoundingBox.Empty;
                return Expect(mo != null && Math.Abs(bb.Max.Z - 0.2663) < 0.002, $"elastica rise 0.2663 (got {bb.Max.Z:F4})")
                    && Expect(Bools(c, 5).FirstOrDefault(), "converged")
                    && Expect(CurveCount(c, 6) == 22, $"22 laths (got {CurveCount(c, 6)})");
            });

        TestComponent("Mesh Isocurves",
            new Mite.Grasshopper.Components.MeshIsocurvesComponent(),
            c =>
            {
                var m = saddle.DuplicateMesh();
                SetInputs(c, (0, m));
                // z as the field: the z = 0 level set of x^2 - y^2 is the two diagonals
                SetNumberList(c, 1, Enumerable.Range(0, m.Vertices.Count).Select(i => (double)m.Vertices[i].Z).ToArray());
                SetNumberList(c, 2, new[] { 0.0 });
            },
            c => Expect(CurveCount(c, 0) >= 2, $"diagonal isocurves, got {CurveCount(c, 0)}"));

        TestComponent("Pull To Mesh",
            new Mite.Grasshopper.Components.PullToMeshComponent(),
            c =>
            {
                SetInputs(c, (0, sphere.DuplicateMesh()));
                SetCurveList(c, 1,
                    new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(-2, 0, 0.5), new Rhino.Geometry.Point3d(2, 0, 0.5)));
                SetPointList(c, 2, new Rhino.Geometry.Point3d(3, 0, 0));
            },
            c =>
            {
                var pts = Points(c, 1);
                return Expect(CurveCount(c, 0) == 1, "pulled curve")
                    && Expect(pts.Count == 1 && System.Math.Abs(pts[0].DistanceTo(Rhino.Geometry.Point3d.Origin) - 1.0) < 0.05,
                        "point pulled onto the unit sphere");
            });

        TestComponent("Mesh Colour Map",
            new Mite.Grasshopper.Components.MeshColourMapComponent(),
            c =>
            {
                var m = saddle.DuplicateMesh();
                SetInputs(c, (0, m));
                SetNumberList(c, 1, Enumerable.Range(0, m.Vertices.Count).Select(i => (double)m.Vertices[i].Z).ToArray());
            },
            c =>
            {
                var m = MeshOut(c, 0);
                int zero = -1;
                if (m != null) for (int i = 0; i < m.Vertices.Count; i++) if (System.Math.Abs(m.Vertices[i].Z) < 1e-12) { zero = i; break; }
                var cz = zero >= 0 && m != null ? m.VertexColors[zero] : System.Drawing.Color.Empty;
                return Expect(m != null && m.VertexColors.Count == saddle.Vertices.Count, "one colour per vertex")
                    && Expect(zero < 0 || (cz.R == 247 && cz.G == 247 && cz.B == 247), $"z = 0 is the diverging map's white centre ({cz.R},{cz.G},{cz.B})");
            });

        TestComponent("Geodesic Path",
            new Mite.Grasshopper.Components.GeodesicPathComponent(),
            c =>
            {
                SetInputs(c, (0, sphere.DuplicateMesh()));
                SetPointList(c, 1, new Rhino.Geometry.Point3d(1, 0, 0));
                SetPointList(c, 2, new Rhino.Geometry.Point3d(0, 1, 0));
            },
            c =>
            {
                var len = Numbers(c, 1).FirstOrDefault();
                return Expect(CurveCount(c, 0) == 1 && System.Math.Abs(len - System.Math.PI / 2) < 0.05,
                    $"quarter great circle, length {len:F3}");
            });

        TestComponent("Geodesic Path (Method 0, curve shortening)",
            new Mite.Grasshopper.Components.GeodesicPathComponent(),
            c =>
            {
                SetInputs(c, (0, sphere.DuplicateMesh()), (5, 0));
                SetPointList(c, 1, new Rhino.Geometry.Point3d(1, 0, 0));
                SetPointList(c, 2, new Rhino.Geometry.Point3d(0, 1, 0));
            },
            c =>
            {
                var len = Numbers(c, 1).FirstOrDefault();
                return Expect(CurveCount(c, 0) == 1 && System.Math.Abs(len - System.Math.PI / 2) < 0.05,
                    $"quarter great circle by curve shortening, length {len:F3}");
            });

        TestComponent("Net Topology",
            new Mite.Grasshopper.Components.NetTopologyComponent(),
            c =>
            {
                SetCurveList(c, 0,
                    new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(0, 1, 0), new Rhino.Geometry.Point3d(4, 1, 0)),
                    new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(0, 2, 0), new Rhino.Geometry.Point3d(4, 2, 0)));
                SetCurveList(c, 1,
                    new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(1, 0, 0), new Rhino.Geometry.Point3d(1, 4, 0)),
                    new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(2, 0, 0), new Rhino.Geometry.Point3d(2, 4, 0)));
            },
            c =>
            {
                // the four laths bound one planar panel: a quad with planarity 0
                var panels = Meshes(c, 9).FirstOrDefault();
                var pl = Numbers(c, 10);
                return Expect(Points(c, 0).Count == 4 && CurveCount(c, 2) == 12 && panels != null && panels.Faces.Count == 1 && panels.Faces[0].IsQuad && pl.Count == 1 && pl[0] < 1e-12,
                    $"4 nodes / 12 members / 1 planar quad panel, got {Points(c, 0).Count} / {CurveCount(c, 2)} / {panels?.Faces.Count} faces, planarity {(pl.Count > 0 ? pl[0] : double.NaN)}");
            });

        // Net Trim: three asymptotic lines x − y = c of the saddle, cookie-cut by a plan circle of
        // radius 0.6 drawn above the surface and projected down; each line keeps one piece whose
        // ends lie on the circle at x = c/2 ± ½√(0.72 − c²)
        TestComponent("Net Trim (plan circle projected down)",
            new Mite.Grasshopper.Components.NetTrimComponent(),
            c =>
            {
                Rhino.Geometry.Curve LineOn(double k)
                {
                    double x0 = System.Math.Max(-1, k - 1), x1 = System.Math.Min(1, k + 1);   // x − y = k on z = x² − y²
                    return new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(x0, x0 - k, 2 * k * x0 - k * k),
                        new Rhino.Geometry.Point3d(x1, x1 - k, 2 * k * x1 - k * k));
                }
                var lines = new[] { -0.4, 0.0, 0.4 }.Select(LineOn).ToArray();
                SetCurveList(c, 0, lines);
                SetInputs(c, (2, saddle.DuplicateMesh()), (4, new Rhino.Geometry.Vector3d(0, 0, -1)));
                SetCurveList(c, 3, new Rhino.Geometry.Circle(new Rhino.Geometry.Plane(new Rhino.Geometry.Point3d(0, 0, 3), Rhino.Geometry.Vector3d.ZAxis), 0.6).ToNurbsCurve());
            },
            c =>
            {
                var cuts = Points(c, 4);
                bool onCircle = cuts.Count == 6 && cuts.All(q => System.Math.Abs(System.Math.Sqrt(q.X * q.X + q.Y * q.Y) - 0.6) < 0.03);
                return Expect(CurveCount(c, 0) == 3 && onCircle,
                    $"3 pieces with 6 cut ends on the circle, got {CurveCount(c, 0)} pieces / {cuts.Count} cuts");
            });

        // Net Kinetics: a 3 × 3 planar rhombic lattice (lazy tongs) sheared by moving one corner;
        // rods stay straight and joint spacing constant (exact mechanism)
        TestComponent("Net Kinetics",
            new Mite.Grasshopper.Components.NetKineticsComponent(),
            c =>
            {
                SetCurveList(c, 0,
                    new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(0, 0, 0), new Rhino.Geometry.Point3d(2, 0, 0)),
                    new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(0, 1, 0), new Rhino.Geometry.Point3d(2, 1, 0)),
                    new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(0, 2, 0), new Rhino.Geometry.Point3d(2, 2, 0)));
                SetCurveList(c, 1,
                    new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(0, 0, 0), new Rhino.Geometry.Point3d(0, 2, 0)),
                    new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(1, 0, 0), new Rhino.Geometry.Point3d(1, 2, 0)),
                    new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(2, 0, 0), new Rhino.Geometry.Point3d(2, 2, 0)));
                SetPointList(c, 2, new Rhino.Geometry.Point3d(0, 0, 0), new Rhino.Geometry.Point3d(2, 0, 0));
                SetPointList(c, 3, new Rhino.Geometry.Point3d(0, 2, 0));
                SetPointList(c, 4, new Rhino.Geometry.Point3d(1, Math.Sqrt(3), 0));   // shear to 60°
                SetInputs(c, (8, 4));
            },
            c => Expect(CurveCount(c, 0) == 3 && Points(c, 2).Count == 9 && Numbers(c, 8).Max() < 1e-6,
                $"3 A laths / 9 joints / drift < 1e-6, got {CurveCount(c, 0)} / {Points(c, 2).Count} / {Numbers(c, 8).Max():E1}"));

        // Net Kinetics, Deploy 1: the same flat lattice on a table, every scissor opened to 60° without a kick:
        // a grid of straight rods shears in its plane (the lazy tongs is exact), the joints read 60°, the spacing holds
        TestComponent("Net Kinetics (deploy: scissors to 60°)",
            new Mite.Grasshopper.Components.NetKineticsComponent(),
            c =>
            {
                SetCurveList(c, 0,
                    new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(0, 0, 0), new Rhino.Geometry.Point3d(2, 0, 0)),
                    new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(0, 1, 0), new Rhino.Geometry.Point3d(2, 1, 0)),
                    new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(0, 2, 0), new Rhino.Geometry.Point3d(2, 2, 0)));
                SetCurveList(c, 1,
                    new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(0, 0, 0), new Rhino.Geometry.Point3d(0, 2, 0)),
                    new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(1, 0, 0), new Rhino.Geometry.Point3d(1, 2, 0)),
                    new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(2, 0, 0), new Rhino.Geometry.Point3d(2, 2, 0)));
                SetPointList(c, 2, new Rhino.Geometry.Point3d(0, 0, 0));
                SetInputs(c, (8, 4), (27, 1), (28, 60.0), (29, 0.0));
            },
            c => Expect(Points(c, 2).Count == 9 && Numbers(c, 4).All(a => System.Math.Abs(a - 60) < 0.5) && Numbers(c, 8).Max() < 1e-6,
                $"9 joints / scissors at 60° / drift < 1e-6, got {Points(c, 2).Count} / {Numbers(c, 4).Min():F2}…{Numbers(c, 4).Max():F2} / {Numbers(c, 8).Max():E1}"));

        // Flat Kit: the same flat lattice is its own flat state; 6 strips, 9 joints, half-width slots at the spacing
        TestComponent("Flat Kit",
            new Mite.Grasshopper.Components.FlatKitComponent(),
            c =>
            {
                SetCurveList(c, 0,
                    new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(0, 0, 0), new Rhino.Geometry.Point3d(2, 0, 0)),
                    new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(0, 1, 0), new Rhino.Geometry.Point3d(2, 1, 0)),
                    new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(0, 2, 0), new Rhino.Geometry.Point3d(2, 2, 0)));
                SetCurveList(c, 1,
                    new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(0, 0, 0), new Rhino.Geometry.Point3d(0, 2, 0)),
                    new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(1, 0, 0), new Rhino.Geometry.Point3d(1, 2, 0)),
                    new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(2, 0, 0), new Rhino.Geometry.Point3d(2, 2, 0)));
                SetInputs(c, (5, 0.2), (6, 0.02));
            },
            c => Expect(CurveCount(c, 0) == 3 && CurveCount(c, 1) == 3 && Points(c, 2).Count == 9 && CurveCount(c, 6) == 6,
                $"3 + 3 flat laths / 9 joints / 6 strip outlines, got {CurveCount(c, 0)} + {CurveCount(c, 1)} / {Points(c, 2).Count} / {CurveCount(c, 6)}"));

        // Mite Net: one wire from the net to the fabrication components. The two
        // lines of family A and one of family B on a 4 × 4 plate: 2 joints; B ends
        // on the border (x = ±2), A's second lath starts on B (a T-junction)
        Check("Mite Net → Net Joints / Lath Labels / Net Topology (one wire)", NetWire());
        // Mode menus (ROADMAP B): the catalogue names live inputs of the right type and default
        Check("Mode menus: every catalogued input exists with its type and default", ModesMatch());

        TestComponent("Mode menus: Message shows the active modes after a solve",
            new Mite.Grasshopper.Components.LathSweepComponent(),
            c =>
            {
                SetInputs(c, (0, sphere.DuplicateMesh()), (2, 0.1), (3, 0.02), (4, true));
                var circle = new Rhino.Geometry.Circle(Rhino.Geometry.Plane.WorldXY, 1.0);
                SetCurveList(c, 1, circle.ToNurbsCurve());
            },
            c => Expect(c.Message == "Upright · Rectangle", $"Message \"Upright · Rectangle\", got \"{c.Message}\""));
        // Flat Kit (nesting): the same lattice at 1 : 1 in a metre model is 2000 mm of strip on a 600 × 400 mm sheet:
        // every strip is split into lapped pieces (the Report says so), and with Splice < 0 the six strips stay whole
        TestComponent("Flat Kit (split strips)",
            new Mite.Grasshopper.Components.FlatKitComponent(),
            c =>
            {
                SetCurveList(c, 0,
                    new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(0, 0, 0), new Rhino.Geometry.Point3d(2, 0, 0)),
                    new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(0, 1, 0), new Rhino.Geometry.Point3d(2, 1, 0)),
                    new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(0, 2, 0), new Rhino.Geometry.Point3d(2, 2, 0)));
                SetCurveList(c, 1,
                    new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(0, 0, 0), new Rhino.Geometry.Point3d(0, 2, 0)),
                    new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(1, 0, 0), new Rhino.Geometry.Point3d(1, 2, 0)),
                    new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(2, 0, 0), new Rhino.Geometry.Point3d(2, 2, 0)));
                SetInputs(c, (5, 0.05), (6, 0.006), (18, 0.0));
            },
            c => Expect(CurveCount(c, 6) > 6 && Texts(c, 12).Count == 1 && Texts(c, 12)[0].Contains("split into") && Texts(c, 11)[0].Contains("splice"),
                $"more than 6 strip outlines and a Report with the split, got {CurveCount(c, 6)} outlines; {string.Join(" | ", Texts(c, 12))}"));
        // Flat Kit (hub plates): six rays at 60° end at one hub, each crossed once at radius 1; two bolted plates
        // → 12 strips, 7 joints (1 hub), 2 hub-plate curves in place (outline + bolt), and the strip outlines plus
        // 2 plate outlines and 2 bolt holes on the sheets
        TestComponent("Flat Kit (hub plates)",
            new Mite.Grasshopper.Components.FlatKitComponent(),
            c =>
            {
                var rays = new System.Collections.Generic.List<Rhino.Geometry.Curve>(); var bars = new System.Collections.Generic.List<Rhino.Geometry.Curve>();
                for (int k = 0; k < 6; k++)
                {
                    double a = Math.PI * k / 3, ca = Math.Cos(a), sa = Math.Sin(a);
                    rays.Add(new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(0, 0, 0), new Rhino.Geometry.Point3d(2 * ca, 2 * sa, 0)));
                    bars.Add(new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(ca + 0.5 * sa, sa - 0.5 * ca, 0), new Rhino.Geometry.Point3d(ca - 0.5 * sa, sa + 0.5 * ca, 0)));
                }
                SetCurveList(c, 0, rays.ToArray());
                SetCurveList(c, 1, bars.ToArray());
                SetInputs(c, (5, 0.05), (6, 0.005), (18, 2));
            },
            c => Expect(CurveCount(c, 0) == 6 && CurveCount(c, 1) == 6 && Points(c, 2).Count == 7 && CurveCount(c, 14) == 2 && CurveCount(c, 6) == 16,
                $"6 + 6 laths / 7 joints / 2 hub-plate curves / 16 cut outlines, got {CurveCount(c, 0)} + {CurveCount(c, 1)} / {Points(c, 2).Count} / {CurveCount(c, 14)} / {CurveCount(c, 6)}"));

        Console.WriteLine();
        Console.WriteLine($"=== {_passed} passed, {_failed} failed ===");
        return _failed == 0 ? 0 : 1;
    }

    private static bool NetWire()
    {
        try
        {
            var doc = new Grasshopper.Kernel.GH_Document();
            var build = new Mite.Grasshopper.Components.MiteNetComponent();
            var joints = new Mite.Grasshopper.Components.NetJointsComponent();
            var labels = new Mite.Grasshopper.Components.LathLabelsComponent();
            var topo = new Mite.Grasshopper.Components.NetTopologyComponent();
            doc.AddObject(build, false); doc.AddObject(joints, false); doc.AddObject(labels, false); doc.AddObject(topo, false);
            var plane = new Rhino.Geometry.Mesh();
            plane.Vertices.Add(-2, -2, 0); plane.Vertices.Add(2, -2, 0); plane.Vertices.Add(2, 2, 0); plane.Vertices.Add(-2, 2, 0);
            plane.Faces.AddFace(0, 1, 2, 3);
            SetInputs(build, (1, plane), (7, true));   // Upright wired as a value: the net carries an upright profile
            SetCurveList(build, 2,
                new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(-1, -1, 0), new Rhino.Geometry.Point3d(1, 1, 0)),
                new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(1.5, 0, 0), new Rhino.Geometry.Point3d(1.5, 1, 0)));
            SetCurveList(build, 3,
                new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(-2, 0, 0), new Rhino.Geometry.Point3d(2, 0, 0)));
            joints.Params.Input[11].AddSource(build.Params.Output[0]);
            labels.Params.Input[4].AddSource(build.Params.Output[0]);
            topo.Params.Input[5].AddSource(build.Params.Output[0]);
            foreach (var c in new Grasshopper.Kernel.GH_Component[] { build, joints, labels, topo }) { c.CollectData(); c.ComputeData(); }
            foreach (var c in new Grasshopper.Kernel.GH_Component[] { build, joints, labels, topo })
            {
                var errors = c.RuntimeMessages(Grasshopper.Kernel.GH_RuntimeMessageLevel.Error);
                if (errors.Count > 0) return Expect(false, $"no error on {c.Name}: {errors[0]}");
            }
            return Expect(Points(build, 5).Count == 2, $"2 border ends (B0), got {Points(build, 5).Count}")
                && Expect(Points(build, 6).Count == 1, $"1 T-junction end (A1 on B0), got {Points(build, 6).Count}")
                && Expect(Points(build, 7).Count == 3, $"3 free ends (A0 both, A1's top), got {Points(build, 7).Count}")
                && Expect(Points(joints, 0).Count == 2, $"2 crossings through the Net wire, got {Points(joints, 0).Count}")
                && Expect(Texts(labels, 1).SequenceEqual(new[] { "LA000", "LA001", "LB000" }), $"family IDs, got {string.Join(" ", Texts(labels, 1))}")
                && Expect(Points(topo, 0).Count == 2, $"2 topology nodes, got {Points(topo, 0).Count}");
        }
        catch (Exception ex) { return Expect(false, ex.Message); }
    }

    // ---------- harness ----------

    private static void TestComponent(
        string name, Grasshopper.Kernel.GH_Component comp,
        Action<Grasshopper.Kernel.GH_Component> setup,
        Func<Grasshopper.Kernel.GH_Component, bool> verify)
    {
        try
        {
            var doc = new Grasshopper.Kernel.GH_Document();
            doc.AddObject(comp, false);
            setup(comp);

            comp.CollectData();
            comp.ComputeData();

            var errors = comp.RuntimeMessages(Grasshopper.Kernel.GH_RuntimeMessageLevel.Error);
            if (errors.Count > 0)
            {
                Check(name, false, $"runtime error: {errors[0]}");
                return;
            }

            Check(name, verify(comp));
        }
        catch (Exception ex)
        {
            Check(name, false, ex.Message);
        }
    }

    private static void Check(string name, bool ok, string? detail = null)
    {
        if (ok) { _passed++; Console.WriteLine($"PASS  {name}"); }
        else { _failed++; Console.WriteLine($"FAIL  {name}{(detail != null ? " -- " + detail : "")}"); }
    }

    private static bool Expect(bool ok, string what)
    {
        if (!ok) Console.WriteLine($"      expected {what}");
        return ok;
    }

    private static bool ModesMatch()
    {
        var asm = typeof(Mite.Grasshopper.Components.PrincipalCurvatureComponent).Assembly;
        bool ok = true; int n = 0;
        foreach (var t in asm.GetTypes().Where(t => !t.IsAbstract && typeof(Grasshopper.Kernel.GH_Component).IsAssignableFrom(t)))
        {
            var comp = (Grasshopper.Kernel.GH_Component)Activator.CreateInstance(t)!;
            foreach (var m in Mite.Core.Modes.ModeCatalog.For(comp.Name))
            {
                n++;
                var p = comp.Params.Input.FirstOrDefault(q => q.Name == m.Input);
                int? def = p switch
                {
                    Grasshopper.Kernel.Parameters.Param_Boolean b when m.Kind == Mite.Core.Modes.ModeKind.Boolean =>
                        b.PersistentData.AllData(true).OfType<Grasshopper.Kernel.Types.GH_Boolean>().Select(g => g.Value ? 1 : 0).Cast<int?>().FirstOrDefault(),
                    Grasshopper.Kernel.Parameters.Param_Integer i when m.Kind == Mite.Core.Modes.ModeKind.Integer =>
                        i.PersistentData.AllData(true).OfType<Grasshopper.Kernel.Types.GH_Integer>().Select(g => g.Value).Cast<int?>().FirstOrDefault(),
                    _ => null,
                };
                if (def != m.Default)
                {
                    Console.WriteLine($"      {comp.Name}.{m.Input}: param {p?.GetType().Name ?? "missing"}, default {def?.ToString() ?? "?"}, catalogue {m.Default}");
                    ok = false;
                }
            }
        }
        return Expect(ok && n >= 30, $"30+ catalogued mode inputs on live components, checked {n}");
    }

    private static bool IconsLoad()
    {
        var asm = typeof(Mite.Grasshopper.Components.PrincipalCurvatureComponent).Assembly;
        var comps = asm.GetTypes()
            .Where(t => !t.IsAbstract && typeof(Grasshopper.Kernel.GH_Component).IsAssignableFrom(t))
            .ToList();
        if (comps.Count != 31)
        {
            Console.WriteLine($"      expected 31 components, found {comps.Count}");
            return false;
        }
        var param = new Mite.Grasshopper.MiteNetParameter();
        var paramIcon = typeof(Grasshopper.Kernel.GH_DocumentObject)
            .GetProperty("Icon", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(param);
        if (paramIcon is not System.Drawing.Bitmap pb || pb.Width != 24)
        {
            Console.WriteLine("      icon failed for MiteNetParameter");
            return false;
        }

        foreach (var t in comps)
        {
            var comp = (Grasshopper.Kernel.GH_Component)Activator.CreateInstance(t)!;
            var prop = typeof(Grasshopper.Kernel.GH_Component)
                .GetProperty("Icon", BindingFlags.NonPublic | BindingFlags.Instance)!;
            if (prop.GetValue(comp) is not System.Drawing.Bitmap bmp || bmp.Width != 24)
            {
                Console.WriteLine($"      icon failed for {t.Name}");
                return false;
            }
        }
        return true;
    }

    // ---------- input helpers ----------

    // Persistent data is always set by clearing first, then adding. Calling
    // SetPersistentData through dynamic with a scalar value resolves to the
    // single-item overload SetPersistentData(T), which APPENDS (per its own
    // docstring) — on a param with a registration default that leaves two
    // items, the item-access iterator then runs SolveInstance twice and every
    // SetDataList output gets one branch per run (doubled results).
    private static void SetPersistent(Grasshopper.Kernel.GH_Component comp, int index, params object[] values)
    {
        dynamic p = comp.Params.Input[index];
        p.PersistentData.Clear();
        foreach (object v in values)
            p.AddPersistentData(v);
    }

    private static void SetInputs(Grasshopper.Kernel.GH_Component comp, params (int Index, object Value)[] inputs)
    {
        foreach (var (index, value) in inputs)
            SetPersistent(comp, index, value);
    }

    private static void SetNumberList(Grasshopper.Kernel.GH_Component comp, int index, double[] values) =>
        SetPersistent(comp, index, values.Cast<object>().ToArray());

    private static void SetBoolList(Grasshopper.Kernel.GH_Component comp, int index, bool[] values) =>
        SetPersistent(comp, index, values.Cast<object>().ToArray());

    private static void SetVectorList(Grasshopper.Kernel.GH_Component comp, int index, params Rhino.Geometry.Vector3d[] values) =>
        SetPersistent(comp, index, values.Cast<object>().ToArray());

    private static void SetCurveList(Grasshopper.Kernel.GH_Component comp, int index, params Rhino.Geometry.Curve[] values) =>
        SetPersistent(comp, index, values.Cast<object>().ToArray());

    private static void SetPointList(Grasshopper.Kernel.GH_Component comp, int index, params Rhino.Geometry.Point3d[] values) =>
        SetPersistent(comp, index, values.Cast<object>().ToArray());

    private static void SetMeshList(Grasshopper.Kernel.GH_Component comp, int index, params Rhino.Geometry.Mesh[] values) =>
        SetPersistent(comp, index, values.Cast<object>().ToArray());

    // ---------- output helpers ----------

    private static System.Collections.Generic.List<double> Numbers(Grasshopper.Kernel.GH_Component comp, int index) =>
        comp.Params.Output[index].VolatileData.AllData(true)
            .OfType<Grasshopper.Kernel.Types.GH_Number>().Select(n => n.Value).ToList();

    private static System.Collections.Generic.List<Rhino.Geometry.Vector3d> Vectors(Grasshopper.Kernel.GH_Component comp, int index) =>
        comp.Params.Output[index].VolatileData.AllData(true)
            .OfType<Grasshopper.Kernel.Types.GH_Vector>().Select(v => v.Value).ToList();

    private static System.Collections.Generic.List<bool> Bools(Grasshopper.Kernel.GH_Component comp, int index) =>
        comp.Params.Output[index].VolatileData.AllData(true)
            .OfType<Grasshopper.Kernel.Types.GH_Boolean>().Select(b => b.Value).ToList();

    private static int CurveCount(Grasshopper.Kernel.GH_Component comp, int index) =>
        comp.Params.Output[index].VolatileData.AllData(true)
            .OfType<Grasshopper.Kernel.Types.GH_Curve>().Count();

    private static Rhino.Geometry.Mesh? MeshOut(Grasshopper.Kernel.GH_Component comp, int index) =>
        comp.Params.Output[index].VolatileData.AllData(true)
            .OfType<Grasshopper.Kernel.Types.GH_Mesh>().Select(m => m.Value).FirstOrDefault();

    private static System.Collections.Generic.List<Rhino.Geometry.Mesh> Meshes(Grasshopper.Kernel.GH_Component comp, int index) =>
        comp.Params.Output[index].VolatileData.AllData(true)
            .OfType<Grasshopper.Kernel.Types.GH_Mesh>().Select(m => m.Value).ToList();

    private static System.Collections.Generic.List<Rhino.Geometry.Point3d> Points(Grasshopper.Kernel.GH_Component comp, int index) =>
        comp.Params.Output[index].VolatileData.AllData(true)
            .OfType<Grasshopper.Kernel.Types.GH_Point>().Select(p => p.Value).ToList();

    private static System.Collections.Generic.List<Rhino.Geometry.Box> Boxes(Grasshopper.Kernel.GH_Component comp, int index) =>
        comp.Params.Output[index].VolatileData.AllData(true)
            .OfType<Grasshopper.Kernel.Types.GH_Box>().Select(b => b.Value).ToList();

    private static System.Collections.Generic.List<string> Texts(Grasshopper.Kernel.GH_Component comp, int index) =>
        comp.Params.Output[index].VolatileData.AllData(true)
            .OfType<Grasshopper.Kernel.Types.GH_String>().Select(s => s.Value).ToList();

    // ---------- geometry ----------

    private static int CenterVertex(Rhino.Geometry.Mesh mesh)
    {
        var centroid = new Rhino.Geometry.Point3d(0, 0, 0);
        int best = 0;
        double bestDist = double.MaxValue;
        for (int i = 0; i < mesh.Vertices.Count; i++)
        {
            double d = ((Rhino.Geometry.Point3d)mesh.Vertices[i]).DistanceTo(centroid);
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }

    private static Rhino.Geometry.Mesh BuildSaddle(int div, double size)
    {
        var mesh = new Rhino.Geometry.Mesh();
        for (int j = 0; j <= div; j++)
            for (int i = 0; i <= div; i++)
            {
                double x = size * (i / (double)div - 0.5);
                double y = size * (j / (double)div - 0.5);
                mesh.Vertices.Add(x, y, x * x - y * y);
            }
        for (int j = 0; j < div; j++)
            for (int i = 0; i < div; i++)
            {
                int a = j * (div + 1) + i, b = a + 1, c = b + div + 1, d = a + div + 1;
                mesh.Faces.AddFace(a, b, c);
                mesh.Faces.AddFace(a, c, d);
            }
        mesh.Normals.ComputeNormals();
        return mesh;
    }

    private static Rhino.Geometry.Mesh BuildBumpyGrid(int nx, int ny)
    {
        var mesh = new Rhino.Geometry.Mesh();
        var rng = new Random(42);
        for (int j = 0; j <= ny; j++)
            for (int i = 0; i <= nx; i++)
                mesh.Vertices.Add(i, j, rng.NextDouble() * 0.3);
        for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                int a = j * (nx + 1) + i, b = a + 1, c = b + nx + 1, d = a + nx + 1;
                mesh.Faces.AddFace(a, b, c, d);
            }
        mesh.Normals.ComputeNormals();
        return mesh;
    }

    private static bool[] BorderFlags(Rhino.Geometry.Mesh mesh)
    {
        var flags = new bool[mesh.Vertices.Count];
        var naked = mesh.GetNakedEdgePointStatus();
        if (naked != null)
            for (int i = 0; i < flags.Length && i < naked.Length; i++)
                flags[i] = naked[i];
        return flags;
    }
}
