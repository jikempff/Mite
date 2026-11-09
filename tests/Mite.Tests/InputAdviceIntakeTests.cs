using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using Mite.Core.Diagnostics;
using Mite.Core.Geometry;
using Mite.Core.Modes;

namespace Mite.Tests;

/// <summary>
/// Runtime messages, round two (ROADMAP B follow-ups a, b, d, e): the mesh
/// intake census says "run Mesh Cleanup" with the numbers and stays silent on
/// clean meshes; material and load inputs typed in another unit are named
/// with the value to enter, and that value passes; an input set for a mode
/// that is off gets a remark naming the mode it waits for; the dependency
/// table agrees with the component sources.
/// </summary>
public class InputAdviceIntakeTests
{
    private static T Invariant<T>(Func<T> f)
    {
        var old = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try { return f(); } finally { CultureInfo.CurrentCulture = old; }
    }

    // ------------------------------------------------------------ (b) mesh intake

    private static List<Advice> Intake(MeshData m) => InputAdvice.MeshIntake(MeshCleanup.Inspect(m), m.FaceCount);

    [Fact]
    public void CleanMeshes_RaiseNoIntakeNote()
    {
        var holes = new[] { (0.0, 0.0, 0.4) };
        var cases = new (string name, MeshData mesh)[]
        {
            ("saddle", TestMeshes.CreateSaddle(30, 2.0)),
            ("sphere (closed)", TestMeshes.CreateUnitSphere(12)),
            ("torus (closed)", TestMeshes.CreateTorus()),
            ("hyperboloid", TestMeshes.CreateHyperboloid()),
            ("catenoid", TestMeshes.CreateCatenoid()),
            ("enneper 3", TestMeshes.CreateEnneper(3)),
            ("cone with apex", TestMeshes.CreatePolyCone()),
            ("annulus with a hole", TestMeshes.CreatePerforated((x, y) => new Vec3d(x, y, 0.5 * (x * x - y * y)), -1.2, 1.2, -1.2, 1.2, 40, 40, holes, out _)),
            ("quad grid", TestMeshes.CreateQuadGrid(6, 6)),
            ("Schwarz D", TestMeshes.CreateSchwarzD(0, Math.PI, 12, 2)),
        };
        foreach (var (name, mesh) in cases)
        {
            var info = MeshCleanup.Inspect(mesh);
            Assert.True(info.Components == 1, $"{name}: {info.Components} parts");
            Assert.True(Intake(mesh).Count == 0, name + ": " + string.Join(" | ", Intake(mesh)));
        }
        // the hole counts as a border loop, not as a part
        Assert.Equal(2, MeshCleanup.Inspect(cases[7].mesh).BorderLoops);
    }

    [Fact]
    public void NonManifoldFin_IsCountedAndNamed()
    {
        // A saddle with a fin: a new vertex above each of three consecutive interior edges, one extra face per edge
        var saddle = TestMeshes.CreateSaddle(12);
        var v = saddle.Vertices.ToList();
        var f = saddle.Faces.Select(x => (int[])x.Clone()).ToList();
        var info0 = MeshCleanup.Inspect(saddle);
        Assert.Empty(info0.NonManifoldEdges);
        // interior edges: shared by exactly two faces
        var edgeUse = new Dictionary<(int, int), int>();
        foreach (var face in saddle.Faces)
            for (int i = 0; i < face.Length; i++)
            {
                int a = face[i], b = face[(i + 1) % face.Length];
                var k = (Math.Min(a, b), Math.Max(a, b));
                edgeUse[k] = edgeUse.TryGetValue(k, out int c) ? c + 1 : 1;
            }
        var interior = edgeUse.Where(e => e.Value == 2).Select(e => e.Key).OrderBy(e => e.Item1).ThenBy(e => e.Item2).Take(3).ToArray();
        foreach (var (a, b) in interior)
        {
            v.Add(0.5 * (saddle.Vertices[a] + saddle.Vertices[b]) + new Vec3d(0, 0, 0.3));
            f.Add(new[] { a, b, v.Count - 1 });
        }
        var fin = new MeshData(v.ToArray(), f.ToArray());
        var info = MeshCleanup.Inspect(fin);
        Assert.Equal(3, info.NonManifoldEdges.Length);
        Assert.Equal(interior, info.NonManifoldEdges);
        var adv = Invariant(() => Intake(fin));
        var nm = Assert.Single(adv, a => a.Code == "mesh-non-manifold");
        Assert.Equal(AdviceLevel.Warning, nm.Level);
        Assert.Equal(3, nm.Measured);
        Assert.StartsWith("3 mesh edges are shared by three or more faces", nm.Message);
        Assert.Contains($"vertices {interior[0].Item1} and {interior[0].Item2}", nm.Message);
        Assert.Contains("Mesh Cleanup", nm.Message);
        // Mesh Cleanup reports the same edges
        Assert.Equal(3, MeshCleanup.Compute(fin).Info.NonManifoldEdges.Length);
    }

    [Fact]
    public void SeveralParts_AreCounted_AndOneCleanPartIsSilent()
    {
        var s = TestMeshes.CreateSaddle(10);
        int n = s.VertexCount;
        var v = s.Vertices.Concat(s.Vertices.Select(p => p + new Vec3d(5, 0, 0))).Concat(TestMeshes.CreateUnitSphere(6).Vertices.Select(p => p + new Vec3d(0, 5, 0))).ToArray();
        var sphere = TestMeshes.CreateUnitSphere(6);
        var f = s.Faces.Concat(s.Faces.Select(x => x.Select(i => i + n).ToArray()))
            .Concat(sphere.Faces.Select(x => x.Select(i => i + 2 * n).ToArray())).ToArray();
        var three = new MeshData(v, f);
        var info = MeshCleanup.Inspect(three);
        Assert.Equal(3, info.Components);
        Assert.Equal(1, info.ClosedComponents);
        Assert.Equal(2, info.BorderLoops);
        var adv = Invariant(() => Intake(three));
        var parts = Assert.Single(adv);
        Assert.Equal("mesh-several-parts", parts.Code);
        Assert.Equal(3, parts.Measured);
        Assert.StartsWith("The mesh is 3 separate parts (1 closed, 2 border loops in all)", parts.Message);
        Assert.Contains("Cracks", parts.Message);
        Assert.Empty(Intake(s));
    }

    [Fact]
    public void MixedWinding_IsCounted_AndUnifyWindingClearsIt()
    {
        var sphere = TestMeshes.CreateUnitSphere(8);
        var rnd = new Random(5);
        int flipped = 0;
        var faces = sphere.Faces.Select(x => { if (rnd.NextDouble() < 0.3) { flipped++; return x.Reverse().ToArray(); } return x; }).ToArray();
        var mixed = new MeshData(sphere.Vertices, faces);
        var info = MeshCleanup.Inspect(mixed);
        Assert.True(info.OrientationConflicts > flipped, $"{info.OrientationConflicts} conflicting edges for {flipped} flipped faces");
        Assert.Empty(info.NonManifoldEdges);
        Assert.Equal(1, info.Components);
        var adv = Invariant(() => Intake(mixed));
        var w = Assert.Single(adv);
        Assert.Equal("mesh-winding-mixed", w.Code);
        Assert.Equal(info.OrientationConflicts, w.Measured);
        Assert.Contains("UnifyWinding", w.Message);
        // The repair the message names clears the note
        var repaired = MeshCleanup.Compute(mixed, 0, true).Mesh;
        Assert.Empty(Intake(repaired));
        Assert.Equal(0, MeshCleanup.Inspect(repaired).OrientationConflicts);
    }

    // ------------------------------------------------------------ (d) material units

    [Fact]
    public void OrdinaryMaterials_RaiseNoNote()
    {
        // (E, allowable, density, area load): the component defaults, C24 timber, S235 steel, E23 GFRP, a stiff plastic, CFRP
        var ok = new (double e, double al, double rho, double q)[]
        {
            (11e9, 20e6, 0, 0), (11e9, 24e6, 420, 1500), (210e9, 235e6, 7850, 750), (23e9, 240e6, 1900, 500),
            (3e9, 40e6, 1050, 100), (150e9, 1500e6, 1600, 5000), (7e9, 14e6, 350, 50), (24e9, 80e6, 1080, 1e5),
        };
        foreach (var (e, al, rho, q) in ok)
            Assert.True(InputAdvice.Material(e, al, rho, q).Count == 0, $"{e} {al} {rho} {q}: " + string.Join(" | ", InputAdvice.Material(e, al, rho, q)));
        Assert.Empty(InputAdvice.KineticMaterial(11e9, 0.005, 0));
        Assert.Empty(InputAdvice.KineticMaterial(210e9, 0.002, 7850));
        Assert.Empty(InputAdvice.KineticMaterial(23e9, 0.015, 1900));
        Assert.Empty(InputAdvice.KineticMaterial(1e9, 0.04, 950));
    }

    [Theory]
    [InlineData(11, "modulus-in-another-unit", 11e9)]          // GPa typed
    [InlineData(200, "modulus-in-another-unit", 200e9)]        // GPa typed
    [InlineData(11000, "modulus-in-another-unit", 11e9)]       // N/mm² typed
    [InlineData(210000, "modulus-in-another-unit", 210e9)]     // N/mm² typed
    [InlineData(2.1e14, "modulus-beyond-materials", 2.1e11)]   // one factor of 1 000 too many
    public void ModulusInAnotherUnit_SuggestsTheValueThatPasses(double e, string code, double suggested)
    {
        var adv = Invariant(() => InputAdvice.Modulus("E", e));
        Assert.NotNull(adv);
        Assert.Equal(code, adv!.Code);
        Assert.Equal(AdviceLevel.Warning, adv.Level);
        Assert.Equal(suggested, adv.Suggested, suggested * 1e-9);
        Assert.Contains(InputAdvice.FormatEntry(suggested), adv.Message);
        Assert.Null(InputAdvice.Modulus("E", adv.Suggested));
    }

    [Theory]
    [InlineData(20, "strength-in-another-unit", 20e6)]         // MPa typed
    [InlineData(235, "strength-in-another-unit", 235e6)]
    [InlineData(0.024, "strength-in-another-unit", 24e6)]      // GPa typed
    [InlineData(24000, "strength-in-another-unit", 24e6)]      // kPa typed
    [InlineData(2.4e10, "strength-beyond-materials", 2.4e7)]
    public void StrengthInAnotherUnit_SuggestsTheValueThatPasses(double s, string code, double suggested)
    {
        var adv = Invariant(() => InputAdvice.Strength("Allowable", s));
        Assert.NotNull(adv);
        Assert.Equal(code, adv!.Code);
        Assert.Equal(suggested, adv.Suggested, suggested * 1e-9);
        Assert.Contains(InputAdvice.FormatEntry(suggested), adv.Message);
        Assert.Null(InputAdvice.Strength("Allowable", adv.Suggested));
    }

    [Fact]
    public void FailureStrain_CatchesThePairThatPassesSingly()
    {
        // 11 GPa with 0.2 MPa: both in range on their own, the strain 1.8e-5 is no material's
        var adv = Invariant(() => InputAdvice.Material(11e9, 2e5, 0, 0));
        var r = Assert.Single(adv);
        Assert.Equal("failure-strain-off", r.Code);
        Assert.Equal(2e5 / 11e9, r.Measured, 1e-15);
        Assert.Equal(2e8, r.Suggested, 1e-3);            // Allowable × 1 000 fits; E / 1 000 = 11 MPa does not
        Assert.Contains("1.8e-5", r.Message);
        Assert.Contains("Allowable 200e6 (200 MPa) with this E", r.Message);
        Assert.DoesNotContain("either", r.Message);
        Assert.Empty(InputAdvice.Material(11e9, r.Suggested, 0, 0));
        // 1 GPa plastic with 240 MPa: ratio 0.24 — too high; both corrections fit
        var hi = Invariant(() => InputAdvice.Material(1e9, 240e6, 0, 0));
        var h = Assert.Single(hi);
        Assert.Equal("failure-strain-off", h.Code);
        Assert.Contains("enter either E 1e12", h.Message);
        // a slip already named by its own check is not named twice
        var twice = InputAdvice.Material(11, 20, 0, 0);
        Assert.Equal(2, twice.Count);
        Assert.DoesNotContain(twice, a => a.Code == "failure-strain-off");
    }

    [Fact]
    public void DensityAndAreaLoad_NameTheirUnits()
    {
        var d = Invariant(() => InputAdvice.Density(0.45));
        Assert.Equal("density-in-another-unit", d!.Code);
        Assert.Equal(450, d.Suggested);
        Assert.Contains("enter 450", d.Message);
        Assert.Null(InputAdvice.Density(450));
        Assert.Null(InputAdvice.Density(0));
        Assert.Equal("density-beyond-materials", InputAdvice.Density(7.85e6)!.Code);
        Assert.Equal(7850, InputAdvice.Density(7.85e6)!.Suggested);

        var q = Invariant(() => InputAdvice.AreaLoad(1.5));
        Assert.Equal("area-load-in-another-unit", q!.Code);
        Assert.Equal(AdviceLevel.Remark, q.Level);
        Assert.Equal(1500, q.Suggested);
        Assert.Contains("enter 1500", q.Message);
        Assert.Null(InputAdvice.AreaLoad(1500));
        Assert.Null(InputAdvice.AreaLoad(0));
        var big = InputAdvice.AreaLoad(1.5e6);
        Assert.Equal("area-load-beyond-loads", big!.Code);
        Assert.Equal(1500, big.Suggested);
    }

    [Theory]
    [InlineData(0.5, "strain-in-another-unit", 0.005)]   // percent typed
    [InlineData(5, "strain-in-another-unit", 0.005)]     // per mille typed
    [InlineData(0.2, "strain-in-another-unit", 0.002)]
    [InlineData(1e-5, "strain-tiny", double.NaN)]
    public void Strain_NamesPercentAndPerMille(double s, string code, double suggested)
    {
        var adv = Invariant(() => InputAdvice.Strain("MaxStrain", s));
        Assert.Equal(code, adv!.Code);
        if (double.IsNaN(suggested)) Assert.True(double.IsNaN(adv.Suggested));
        else
        {
            Assert.Equal(suggested, adv.Suggested, 1e-12);
            Assert.Contains("enter " + InputAdvice.Format(suggested), adv.Message);
            Assert.Null(InputAdvice.Strain("MaxStrain", adv.Suggested));
        }
        // Net Kinetics: all three at once, each with its own fix
        var k = InputAdvice.KineticMaterial(11, 0.5, 0.45);
        Assert.Equal(new[] { "modulus-in-another-unit", "strain-in-another-unit", "density-in-another-unit" }, k.Select(a => a.Code).ToArray());
        Assert.Equal(new[] { 11e9, 0.005, 450 }, k.Select(a => a.Suggested).ToArray());
        Assert.Empty(InputAdvice.KineticMaterial(k[0].Suggested, k[1].Suggested, k[2].Suggested));
    }

    [Fact]
    public void Formats_ReadAsTyped()
    {
        Invariant(() =>
        {
            Assert.Equal("11e9", InputAdvice.FormatEntry(11e9));
            Assert.Equal("11e9", InputAdvice.FormatEntry(1.1e10));
            Assert.Equal("20e6", InputAdvice.FormatEntry(20e6));
            Assert.Equal("210e9", InputAdvice.FormatEntry(2.1e11));
            Assert.Equal("1500", InputAdvice.FormatEntry(1500));
            Assert.Equal("250e3", InputAdvice.FormatEntry(2.5e5));
            Assert.Equal("1e12", InputAdvice.FormatEntry(999.6e9));
            Assert.Equal("11 GPa", InputAdvice.FormatStress(11e9));
            Assert.Equal("20 MPa", InputAdvice.FormatStress(20e6));
            Assert.Equal("24 kPa", InputAdvice.FormatStress(24000));
            Assert.Equal("20 Pa", InputAdvice.FormatStress(20));
            Assert.Equal("0.0018", InputAdvice.FormatRatio(0.00182));
            Assert.Equal("1.8e-5", InputAdvice.FormatRatio(1.818e-5));
            return 0;
        });
    }

    // ------------------------------------------------------------ (e) inputs set for a mode that is off

    private static Func<string, int?> Values(params (string, int)[] v) => name =>
    {
        foreach (var (n, x) in v) if (n == name) return x;
        return null;
    };

    private static Func<string, bool> Set(params string[] names) => n => names.Contains(n);

    [Fact]
    public void ModeSetWhileItsControllerIsOff_GetsOneRemark()
    {
        var adv = Invariant(() => ModeCatalog.InactiveInputs("Asymptotic Net", Values(("AutoSpace", 0), ("Layout", 1)), Set()));
        var a = Assert.Single(adv);
        Assert.Equal("input-not-used-in-mode", a.Code);
        Assert.Equal(AdviceLevel.Remark, a.Level);
        Assert.Equal("Layout \"Web from the border\" is not used now: it applies only while AutoSpace is \"Fill the surface\" (it is \"Seeds only\"). " +
                     "Set AutoSpace to \"Fill the surface\" to use it, or leave Layout at \"Evenly spaced fill\".", a.Message);
        // at its default: silent; controller on: silent; unknown values read as defaults
        Assert.Empty(ModeCatalog.InactiveInputs("Asymptotic Net", Values(("AutoSpace", 0), ("Layout", 0)), Set()));
        Assert.Empty(ModeCatalog.InactiveInputs("Asymptotic Net", Values(("AutoSpace", 1), ("Layout", 1)), Set()));
        Assert.Empty(ModeCatalog.InactiveInputs("Asymptotic Net", Values(("Layout", 2)), Set()));          // AutoSpace defaults to on
        Assert.Single(ModeCatalog.InactiveInputs("Geodesic Net", Values(("Layout", 2)), Set()));           // defaults to off there
        Assert.Empty(ModeCatalog.InactiveInputs("Conjugate Net", Values(("Layout", 2)), Set()));           // no controller
        Assert.Empty(ModeCatalog.InactiveInputs("Mesh Isocurves", Values(("Smooth", 1)), Set("Smooth")));
    }

    [Fact]
    public void DependentInputs_AreNamedWithTheModeTheyWaitFor()
    {
        // Spacing typed while AutoSpace is off
        var adv = Invariant(() => ModeCatalog.InactiveInputs("Asymptotic Net", Values(("AutoSpace", 0)), Set("Spacing")));
        var a = Assert.Single(adv);
        Assert.Equal("Spacing is not used now: it applies only while AutoSpace is \"Fill the surface\" (it is \"Seeds only\"). " +
                     "Set AutoSpace to \"Fill the surface\" to use it, or leave Spacing at 0 (automatic).", a.Message);
        Assert.Empty(ModeCatalog.InactiveInputs("Asymptotic Net", Values(("AutoSpace", 1)), Set("Spacing", "MaxCurves")));
        Assert.Equal(2, ModeCatalog.InactiveInputs("Asymptotic Net", Values(("AutoSpace", 0)), Set("Spacing", "MaxCurves")).Count);

        // A chain: BorderAngle waits for FromBorder, which waits for AutoSpace
        var chain = Invariant(() => ModeCatalog.InactiveInputs("Geodesic Net", Values(("AutoSpace", 0), ("FromBorder", 1)), Set("BorderAngle")));
        Assert.Equal(2, chain.Count);
        Assert.StartsWith("FromBorder \"Start on the border\" is not used now: it applies only while AutoSpace is \"Fill the surface\"", chain[0].Message);
        Assert.Contains("BorderAngle is not used now: it applies only while FromBorder is \"Start on the border\" (it is itself not used while AutoSpace is \"Seeds only\")", chain[1].Message);
        Assert.Empty(ModeCatalog.InactiveInputs("Geodesic Net", Values(("AutoSpace", 1), ("FromBorder", 1)), Set("BorderAngle")));
        Assert.Single(ModeCatalog.InactiveInputs("Geodesic Net", Values(("AutoSpace", 1), ("FromBorder", 0)), Set("BorderAngle")));

        // Several enabling values: Kick applies to Deploy 1 and 2, Angle to 1 only
        Assert.Empty(ModeCatalog.InactiveInputs("Net Kinetics", Values(("Deploy", 2)), Set("Kick")));
        Assert.Single(ModeCatalog.InactiveInputs("Net Kinetics", Values(("Deploy", 2)), Set("Angle")));
        var kick = Invariant(() => ModeCatalog.InactiveInputs("Net Kinetics", Values(("Deploy", 0)), Set("Kick")));
        Assert.Contains("while Deploy is \"Open every scissor to Angle\" or \"Drivers on the table\"", Assert.Single(kick).Message);

        // A wired Section with Shape 0, on every component that has the pair
        foreach (var comp in new[] { "Lath Sweep", "Lath Analysis", "Gridshell Analysis", "Net Kinetics", "Mite Net" })
        {
            var s = Invariant(() => ModeCatalog.InactiveInputs(comp, Values(("Shape", 0)), Set("Section")));
            Assert.Equal("Section is not used now: it applies only while Shape is \"Custom Section curve\" (it is \"Rectangle W × T\"). " +
                         "Set Shape to \"Custom Section curve\" to use it, or leave Section unwired.", Assert.Single(s).Message);
            Assert.Empty(ModeCatalog.InactiveInputs(comp, Values(("Shape", 2)), Set("Section")));
        }
        // Mixed values on the controller: nothing is said (some branches use the input)
        Assert.Empty(ModeCatalog.InactiveInputs("Asymptotic Net", Values(("AutoSpace", ModeCatalog.Mixed)), Set("Spacing")));
    }

    // ------------------------------------------------------------ the table agrees with the sources

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Mite.slnx"))) dir = dir.Parent;
        Assert.True(dir != null, "repository root (Mite.slnx) not found above the test binaries");
        return dir!.FullName;
    }

    private static readonly Regex ItemRx = new(
        @"Add(?<type>Number|Integer|Curve|Boolean)Parameter\(\s*""(?<name>[^""]+)""\s*,\s*""[^""]*""\s*,(?:(?!GH_ParamAccess).)*?,\s*GH_ParamAccess\.item\s*(?:,\s*(?<def>[^)]+?)\s*)?\)",
        RegexOptions.Singleline | RegexOptions.Compiled);

    [Fact]
    public void DependentsTable_MatchesTheComponentSources()
    {
        string root = RepoRoot();
        string baseSrc = File.ReadAllText(Path.Combine(root, "src", "Mite.Grasshopper", "MiteComponent.cs"));
        var sources = new Dictionary<string, Dictionary<string, (string type, string? def)>>();
        foreach (var file in Directory.GetFiles(Path.Combine(root, "src", "Mite.Grasshopper", "Components"), "*.cs"))
        {
            string src = File.ReadAllText(file);
            var name = Regex.Match(src, @":\s*base\(\s*""(?<n>[^""]+)""");
            if (!name.Success) continue;
            int reg = src.IndexOf("RegisterOutputParams", StringComparison.Ordinal);
            var inputs = new Dictionary<string, (string, string?)>();
            foreach (Match m in ItemRx.Matches(src))
                if (reg < 0 || m.Index < reg)
                    inputs[m.Groups["name"].Value] = (m.Groups["type"].Value, m.Groups["def"].Success ? m.Groups["def"].Value.Trim() : null);
            if (src.Contains("RegisterSectionInputs(pManager)"))
                foreach (Match m in ItemRx.Matches(baseSrc))
                    inputs[m.Groups["name"].Value] = (m.Groups["type"].Value, m.Groups["def"].Success ? m.Groups["def"].Value.Trim() : null);
            sources[name.Groups["n"].Value] = inputs;
        }
        int checkedDeps = 0;
        foreach (var comp in ModeCatalog.ComponentsWithDependents)
        {
            Assert.True(sources.ContainsKey(comp), $"no component source named {comp}");
            var modes = ModeCatalog.For(comp);
            foreach (var d in ModeCatalog.DependentsOf(comp))
            {
                Assert.True(sources[comp].TryGetValue(d.Input, out var src), $"{comp}: no item input named {d.Input}");
                var ctrl = modes.SingleOrDefault(m => m.Input == d.ActiveWhenInput);
                Assert.True(ctrl != null, $"{comp}.{d.Input}: {d.ActiveWhenInput} is not a catalogued mode of {comp}");
                foreach (int v in d.ActiveWhenValues)
                    Assert.True(ctrl!.Find(v) != null, $"{comp}.{d.Input}: {d.ActiveWhenInput} has no option {v}");
                if (d.Neutral.HasValue)
                {
                    Assert.True(src.type == "Number" || src.type == "Integer", $"{comp}.{d.Input} is a {src.type} input but has a numeric default in the table");
                    Assert.True(src.def != null, $"{comp}.{d.Input}: the source registers no default");
                    double regDefault = double.Parse(src.def!.Replace("−", "-"), NumberStyles.Float, CultureInfo.InvariantCulture);
                    Assert.True(Math.Abs(regDefault - d.Neutral.Value) < 1e-12, $"{comp}.{d.Input}: registered default {src.def}, table {d.Neutral}");
                }
                else Assert.Equal("Curve", src.type);
                // the tooltip names the mode the input hangs on
                checkedDeps++;
            }
        }
        Assert.True(checkedDeps >= 15, $"{checkedDeps} dependent inputs checked");
    }

    // ------------------------------------------------------------ wording

    [Fact]
    public void NewMessages_AvoidBlameWords_AndCarryAFixWithANumber()
    {
        var all = new List<Advice>();
        all.AddRange(InputAdvice.Material(11, 20, 0.45, 1.5));
        all.AddRange(InputAdvice.Material(2.1e14, 2.4e10, 7.85e6, 1.5e6));
        all.AddRange(InputAdvice.Material(11e9, 2e5, 0, 0));
        all.AddRange(InputAdvice.KineticMaterial(11e9, 0.5, 0));
        all.Add(InputAdvice.Strain("MaxStrain", 1e-5)!);
        var sphere = TestMeshes.CreateUnitSphere(6);
        var two = new MeshData(sphere.Vertices.Concat(sphere.Vertices.Select(p => p + new Vec3d(3, 0, 0))).ToArray(),
            sphere.Faces.Concat(sphere.Faces.Select(f => f.Select(i => i + sphere.VertexCount).ToArray())).ToArray());
        all.AddRange(InputAdvice.MeshIntake(MeshCleanup.Inspect(two), two.FaceCount));
        var mixed = new MeshData(sphere.Vertices, sphere.Faces.Select((f, i) => i % 4 == 0 ? f.Reverse().ToArray() : f).ToArray());
        all.AddRange(InputAdvice.MeshIntake(MeshCleanup.Inspect(mixed), mixed.FaceCount));
        var v = sphere.Vertices.Append(new Vec3d(0, 0, 2)).ToArray();
        var fin = new MeshData(v, sphere.Faces.Append(new[] { sphere.Faces[0][0], sphere.Faces[0][1], v.Length - 1 }).ToArray());
        all.AddRange(InputAdvice.MeshIntake(MeshCleanup.Inspect(fin), fin.FaceCount));
        all.AddRange(ModeCatalog.InactiveInputs("Geodesic Net", Values(("AutoSpace", 0), ("FromBorder", 1)), Set("BorderAngle", "Spacing")));
        Assert.True(all.Count >= 17, all.Count.ToString());
        string[] blame = { "invalid", "illegal", "bad ", "error", "fatal", "wrong" };
        string[] fixVerbs = { "enter", "Set ", "Run ", "run ", "feed", "check", "Check", "leave" };
        foreach (var a in all)
        {
            foreach (var w in blame) Assert.DoesNotContain(w, a.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(fixVerbs.Any(x => a.Message.Contains(x)), "no fix in: " + a.Message);
            // numbers, not adjectives — a mode note names the mode instead
            if (a.Code != "input-not-used-in-mode") Assert.Matches("[0-9]", a.Message);
        }
    }
}
