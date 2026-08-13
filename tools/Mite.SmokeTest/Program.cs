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
        var sphere = Rhino.Geometry.Mesh.CreateFromSphere(
            new Rhino.Geometry.Sphere(Rhino.Geometry.Plane.WorldXY, 1.0), 48, 24);
        var saddle = BuildSaddle(20, 2.0);
        var grid = BuildBumpyGrid(10, 10);

        Check("Icons load for all 21 components", IconsLoad());

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
            c => Expect(MeshOut(c, 0) != null && Numbers(c, 1).Count > 0, "planarized mesh + deviations"));

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
            c => SetInputs(c, (0, saddle.DuplicateMesh()), (1, CenterVertex(saddle)), (2, 0.05), (3, 200)),
            c => Expect(CurveCount(c, 0) >= 1 && CurveCount(c, 1) >= 1,
                $"both families traced: A={CurveCount(c, 0)}, B={CurveCount(c, 1)}"));

        TestComponent("Asymptotic Net (auto-spaced)",
            new Mite.Grasshopper.Components.AsymptoticNetComponent(),
            c => SetInputs(c, (0, saddle.DuplicateMesh()), (2, 0.05), (3, 200), (4, true), (5, 0.3)),
            c => Expect(CurveCount(c, 0) >= 2, $"auto-spaced family A has {CurveCount(c, 0)} curves"));

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
                return Expect(CurveCount(c, 1) >= 3 && CurveCount(c, 2) >= 3, "lath families produced")
                    && Expect(mesh != null && mesh.Faces.Count > 10, $"net mesh has {mesh?.Faces.Count ?? 0} faces");
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
                return Expect(CurveCount(c, 0) == 1 && lengths.Count == 1
                    && System.Math.Abs(lengths[0] - 2 * System.Math.PI) < 0.1,
                    $"equator pattern, length {(lengths.Count > 0 ? lengths[0] : 0):F3}");
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
            c => Expect(Meshes(c, 0).Count == 2, "two colored laths"));

        Console.WriteLine();
        Console.WriteLine($"=== {_passed} passed, {_failed} failed ===");
        return _failed == 0 ? 0 : 1;
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

    private static bool IconsLoad()
    {
        var asm = typeof(Mite.Grasshopper.Components.PrincipalCurvatureComponent).Assembly;
        var comps = asm.GetTypes()
            .Where(t => !t.IsAbstract && typeof(Grasshopper.Kernel.GH_Component).IsAssignableFrom(t))
            .ToList();
        if (comps.Count != 21)
        {
            Console.WriteLine($"      expected 21 components, found {comps.Count}");
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
