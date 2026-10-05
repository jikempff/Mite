using System.Text.RegularExpressions;
using Mite.Core.Gridshells;
using Mite.Core.Modes;

namespace Mite.Tests;

/// <summary>
/// The mode catalogue behind the right-click menus, the Message line and the
/// value lists (ROADMAP B, "mode menus"). The catalogue is checked against
/// the Grasshopper component sources, so a renamed input, a changed default
/// or a new mode input that is not catalogued fails here, not in Rhino.
/// </summary>
public class ModeCatalogTests
{
    // ---------- catalogue integrity ----------

    [Fact]
    public void EveryMode_HasUniqueOptions_AndItsDefaultAmongThem()
    {
        foreach (var comp in ModeCatalog.Components)
        foreach (var m in ModeCatalog.For(comp))
        {
            var values = m.Options.Select(o => o.Value).ToList();
            Assert.True(values.Distinct().Count() == values.Count, $"{comp}.{m.Input}: duplicate values");
            Assert.True(m.Options.Select(o => o.Label).Distinct().Count() == values.Count, $"{comp}.{m.Input}: duplicate labels");
            var shorts = m.Options.Select(o => o.Short).Where(s => s.Length > 0).ToList();
            Assert.True(shorts.Distinct().Count() == shorts.Count, $"{comp}.{m.Input}: duplicate message words");
            Assert.True(m.Find(m.Default) != null, $"{comp}.{m.Input}: default {m.Default} is not an option");
            if (m.Kind == ModeKind.Boolean)
                Assert.Equal(new[] { 0, 1 }, values.OrderBy(v => v).ToArray());
            if (m.ActiveWhenInput != null)
            {
                var ctrl = ModeCatalog.For(comp).FirstOrDefault(c => c.Input == m.ActiveWhenInput);
                Assert.True(ctrl != null, $"{comp}.{m.Input}: controlling input {m.ActiveWhenInput} is not a mode");
                Assert.True(ctrl!.Find(m.ActiveWhenValue) != null, $"{comp}.{m.Input}: enabling value is not an option of {ctrl.Input}");
            }
        }
    }

    [Fact]
    public void EveryComponent_SaysSomethingAtItsDefaults()
    {
        foreach (var comp in ModeCatalog.Components)
            Assert.False(string.IsNullOrEmpty(ModeCatalog.Message(comp, _ => null)), comp);
        Assert.Empty(ModeCatalog.For("Principal Curvature"));
        Assert.Equal("", ModeCatalog.Message("Principal Curvature", _ => null));
    }

    // ---------- the Message line ----------

    private static Func<string, int?> Values(params (string input, int value)[] set) =>
        name => set.Where(s => s.input == name).Select(s => (int?)s.value).FirstOrDefault();

    [Theory]
    [InlineData("Asymptotic Net", "", "Fill · Continuous")]
    [InlineData("Asymptotic Net", "AutoSpace=0", "Seeds")]
    [InlineData("Asymptotic Net", "AutoSpace=0;Layout=1;Continuous=0", "Seeds")]   // inactive modes stay silent
    [InlineData("Asymptotic Net", "Layout=1;Continuous=0", "Border web · Classic")]
    [InlineData("Geodesic Net", "", "Seeds")]
    [InlineData("Geodesic Net", "AutoSpace=1;FromBorder=1;Layout=2", "Cross web · From border · Continuous")]
    [InlineData("Curvature Streamlines", "", "Max · Seeds")]
    [InlineData("Curvature Streamlines", "MaxDir=0;AutoSpace=1", "Min · Fill · Continuous")]
    [InlineData("Conjugate Net", "Layout=1", "Border web · Continuous")]
    [InlineData("Asymptotic Web", "", "Auto symmetry")]
    [InlineData("Asymptotic Web", "Symmetry=5;Diagonal=1", "5-fold")]               // Diagonal needs AAG
    [InlineData("Asymptotic Web", "Symmetry=0;AAG=1;Diagonal=1", "No symmetry · AAG · Other diagonal")]
    [InlineData("Lath Sweep", "", "Flat · Rectangle")]
    [InlineData("Lath Sweep", "Upright=1;Shape=2", "Upright · Custom section")]
    [InlineData("Net Kinetics", "", "Upright · Rectangle")]
    [InlineData("Gridshell Analysis", "SupportType=1;Shape=1", "Flat · Round · Pinned")]
    [InlineData("Net Joints", "Upright=1", "Upright")]
    [InlineData("Mesh Colour Map", "Palette=2", "Traffic light")]
    [InlineData("Mesh Colour Map", "Palette=7", "Palette = 7?")]                  // closed set: flag the stray value
    [InlineData("Mesh Isocurves", "", "Polylines")]
    [InlineData("Mesh Cleanup", "", "Unify winding")]
    public void Message_NamesTheActiveModes(string comp, string set, string expected)
    {
        var pairs = set.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Split('=')).Select(kv => (kv[0], int.Parse(kv[1]))).ToArray();
        Assert.Equal(expected, ModeCatalog.Message(comp, Values(pairs)));
    }

    [Fact]
    public void Message_MixedValues_AreSaid_AndAMixedSwitchKeepsItsDependents()
    {
        Assert.Equal("Layout mixed · Continuous",
            ModeCatalog.Message("Asymptotic Net", Values(("Layout", ModeCatalog.Mixed))));
        Assert.Equal("AutoSpace mixed · Fill · Continuous",
            ModeCatalog.Message("Asymptotic Net", Values(("AutoSpace", ModeCatalog.Mixed))));
    }

    [Fact]
    public void ValueListExpressions_AreWhatGrasshopperParses()
    {
        var auto = ModeCatalog.For("Asymptotic Net").Single(m => m.Input == "AutoSpace");
        Assert.Equal("True", auto.Expression(1));
        Assert.Equal("False", auto.Expression(0));
        var sym = ModeCatalog.For("Asymptotic Web").Single(m => m.Input == "Symmetry");
        Assert.Equal("-1", sym.Expression(-1));
        Assert.Equal("5-fold", sym.LabelFor(5));
    }

    [Fact]
    public void LayoutOptions_FollowTheCoreEnum()
    {
        // The components map 0/1/2 to Fill / WebBorder / WebCross.
        Assert.Equal(0, (int)NetLayout.Fill);
        Assert.Equal(1, (int)NetLayout.WebBorder);
        Assert.Equal(2, (int)NetLayout.WebCross);
        foreach (var comp in new[] { "Asymptotic Net", "Geodesic Net", "Conjugate Net", "Curvature Streamlines" })
        {
            var layout = ModeCatalog.For(comp).Single(m => m.Input == "Layout");
            Assert.Equal(new[] { 0, 1, 2 }, layout.Options.Select(o => o.Value).ToArray());
            Assert.Contains("border", layout.Find(1)!.Label);
            Assert.Contains("cross", layout.Find(2)!.Label);
        }
    }

    // ---------- the catalogue agrees with the component sources ----------

    private sealed record SourceInput(string Component, string Name, string Type, string Description, string Default);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Mite.slnx"))) dir = dir.Parent;
        Assert.True(dir != null, "repository root (Mite.slnx) not found above the test binaries");
        return dir!.FullName;
    }

    private static readonly Regex ParamRx = new(
        @"Add(?<type>Boolean|Integer)Parameter\(\s*""(?<name>[^""]+)""\s*,\s*""[^""]*""\s*,(?<desc>(?:(?!GH_ParamAccess).)*?),\s*GH_ParamAccess\.item\s*,\s*(?<def>[^)]+?)\s*\)",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static string ResolveDescription(string expr, string root)
    {
        expr = expr.Trim();
        if (expr == "SectionInput.ShapeDescription")
        {
            var src = File.ReadAllText(Path.Combine(root, "src", "Mite.Grasshopper", "SectionInput.cs"));
            var m = Regex.Match(src, @"ShapeDescription\s*=\s*(?<v>.*?""\s*);", RegexOptions.Singleline);
            expr = m.Groups["v"].Value;
        }
        // Concatenated literals: keep the text between quotes.
        return string.Concat(Regex.Matches(expr, @"""((?:[^""\\]|\\.)*)""").Select(x => x.Groups[1].Value));
    }

    private static List<SourceInput> ReadSources()
    {
        string root = RepoRoot();
        string baseSrc = File.ReadAllText(Path.Combine(root, "src", "Mite.Grasshopper", "MiteComponent.cs"));
        var list = new List<SourceInput>();
        foreach (var file in Directory.GetFiles(Path.Combine(root, "src", "Mite.Grasshopper", "Components"), "*.cs"))
        {
            string src = File.ReadAllText(file);
            var name = Regex.Match(src, @":\s*base\(\s*""(?<n>[^""]+)""");
            if (!name.Success) continue;
            string comp = name.Groups["n"].Value;
            foreach (Match m in ParamRx.Matches(src))
            {
                // Only inputs (RegisterInputParams), not outputs.
                int reg = src.IndexOf("RegisterOutputParams", StringComparison.Ordinal);
                if (reg >= 0 && m.Index > reg) continue;
                list.Add(new SourceInput(comp, m.Groups["name"].Value, m.Groups["type"].Value,
                    ResolveDescription(m.Groups["desc"].Value, root), m.Groups["def"].Value.Trim()));
            }
            if (src.Contains("RegisterSectionInputs(pManager)"))
                foreach (Match m in ParamRx.Matches(baseSrc))
                    list.Add(new SourceInput(comp, m.Groups["name"].Value, m.Groups["type"].Value,
                        ResolveDescription(m.Groups["desc"].Value, root), m.Groups["def"].Value.Trim()));
        }
        return list;
    }

    private static int ParseDefault(string s) => s switch
    {
        "true" => 1,
        "false" => 0,
        _ => int.Parse(s.Replace("−", "-"), System.Globalization.CultureInfo.InvariantCulture),
    };

    [Fact]
    public void Catalogue_MatchesTheComponentSources()
    {
        var sources = ReadSources();
        Assert.True(sources.Count > 50, $"only {sources.Count} item inputs parsed from the component sources");
        int checkedModes = 0;
        foreach (var comp in ModeCatalog.Components)
        foreach (var mode in ModeCatalog.For(comp))
        {
            var src = sources.SingleOrDefault(s => s.Component == comp && s.Name == mode.Input);
            Assert.True(src != null, $"{comp}: no item input named {mode.Input} in the sources");
            Assert.Equal(mode.Kind == ModeKind.Boolean ? "Boolean" : "Integer", src!.Type);
            Assert.True(ParseDefault(src.Default) == mode.Default, $"{comp}.{mode.Input}: source default {src.Default}, catalogue {mode.Default}");
            if (mode.Kind == ModeKind.Integer)
            {
                string desc = src.Description.Replace("−", "-");
                foreach (var o in mode.Options)
                {
                    bool coveredByN = mode.OtherShort != null && o.Value > 1;
                    Assert.True(coveredByN || Regex.IsMatch(desc, $@"(?<![\d-]){Regex.Escape(o.Value.ToString())} ="),
                        $"{comp}.{mode.Input}: the tooltip does not explain {o.Value} (\"{desc}\")");
                }
            }
            checkedModes++;
        }
        Assert.True(checkedModes >= 30, $"{checkedModes} modes checked");
    }

    [Fact]
    public void EveryModeLikeInput_IsCatalogued()
    {
        // Item Booleans are switches; item Integers whose tooltip enumerates
        // "0 = …" and "1 = …" are modes. Each must have a menu.
        var missing = ReadSources()
            .Where(s => s.Type == "Boolean" ||
                        (Regex.IsMatch(s.Description, @"(?<![\d\-−])0 =") && Regex.IsMatch(s.Description, @"(?<![\d\-−])1 =")))
            .Where(s => ModeCatalog.For(s.Component).All(m => m.Input != s.Name))
            .Select(s => s.Component + "." + s.Name)
            .ToList();
        Assert.True(missing.Count == 0, "not in ModeCatalog: " + string.Join(", ", missing));
    }
}
