using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Mite.Tests;

/// <summary>
/// Release hygiene: the numbers a release states in several places must agree,
/// so a merge or a version bump that forgets one of them fails here instead of
/// on Food4Rhino.
///
///  - one version in both csproj files, MiteInfo.Version, dist/manifest.yml,
///    the first numbered "### x.y.z" heading of the README changelog (an
///    "### Unreleased" heading above it is allowed), the docs/FOOD4RHINO.md
///    title, its `yak push` line and its newest "Version notes", and the
///    Mite Bench header (tools/webgen/template.html);
///  - the component count stated in README, FOOD4RHINO.md, the bench header
///    and the Windows smoke test equals the number of component classes in
///    src/Mite.Grasshopper, and FOOD4RHINO's panel count the number of
///    sub-categories;
///  - every component is listed under its panel in README, and its icon is
///    drawn by tools/generate_icons.py, embedded by the csproj and on disk.
///
/// The tests read the sources as text: Mite.Grasshopper needs the Rhino
/// assemblies, which the test project does not reference.
/// </summary>
public class ReleaseConsistencyTests
{
    internal static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "Mite.slnx"))) d = d.Parent;
        Assert.True(d != null, "repository root (Mite.slnx) not found above " + AppContext.BaseDirectory);
        return d!.FullName;
    }

    private static string Read(params string[] rel) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(rel).ToArray()));

    private static string One(string text, string pattern, string where)
    {
        var m = Regex.Match(text, pattern, RegexOptions.Multiline);
        Assert.True(m.Success, $"{where}: pattern {pattern} not found");
        return m.Groups[1].Value;
    }

    /// <summary>Component classes of the Grasshopper project: (class, display name, panel, icon).</summary>
    internal static List<(string cls, string name, string panel, string icon)> Components()
    {
        var list = new List<(string, string, string, string)>();
        var dir = Path.Combine(Root(), "src", "Mite.Grasshopper");
        foreach (var f in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            if (f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
                f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)) continue;
            var s = File.ReadAllText(f);
            foreach (Match m in Regex.Matches(s, @"public\s+(?:sealed\s+)?class\s+(\w+)\s*:\s*(?:MiteComponent|GH_Component)\b"))
            {
                // base("Name", "Nick", "Description …", "Panel", "Icon") { …
                var b = Regex.Match(s, @":\s*base\(\s*""([^""]+)""");
                var tail = Regex.Match(s, @"""([^""]+)""\s*,\s*""(\w+)""\s*\)\s*\{");
                Assert.True(b.Success && tail.Success, $"{Path.GetFileName(f)}: cannot read the base(...) call of {m.Groups[1].Value}");
                list.Add((m.Groups[1].Value, b.Groups[1].Value, tail.Groups[1].Value, tail.Groups[2].Value));
            }
        }
        return list;
    }

    [Fact]
    public void Version_IsTheSameEverywhere()
    {
        var v = new Dictionary<string, string>
        {
            ["src/Mite.Core/Mite.Core.csproj"] = One(Read("src", "Mite.Core", "Mite.Core.csproj"), @"<Version>([^<]+)</Version>", "Mite.Core.csproj"),
            ["src/Mite.Grasshopper/Mite.Grasshopper.csproj"] = One(Read("src", "Mite.Grasshopper", "Mite.Grasshopper.csproj"), @"<Version>([^<]+)</Version>", "Mite.Grasshopper.csproj"),
            ["MiteInfo.Version"] = One(Read("src", "Mite.Grasshopper", "MiteInfo.cs"), @"Version\s*=>\s*""([^""]+)""", "MiteInfo.cs"),
            ["dist/manifest.yml"] = One(Read("dist", "manifest.yml"), @"^version:\s*(\S+)\s*$", "manifest.yml"),
            ["README changelog"] = One(Read("README.md").Split(new[] { "## Changelog" }, StringSplitOptions.None).Last(), @"^###\s+(\d+\.\d+\.\d+)\s*$", "README changelog"),
            ["FOOD4RHINO title"] = One(Read("docs", "FOOD4RHINO.md"), @"^#\s+Food4Rhino listing\s+\S+\s+Mite\s+(\d+\.\d+\.\d+)\s*$", "FOOD4RHINO.md title"),
            ["FOOD4RHINO yak push"] = One(Read("docs", "FOOD4RHINO.md"), @"yak push dist/mite-(\d+\.\d+\.\d+)-rh8_0-any\.yak", "FOOD4RHINO.md yak push line"),
            ["FOOD4RHINO version notes"] = One(Read("docs", "FOOD4RHINO.md"), @"^##\s+Version notes\s+\S+\s+(\d+\.\d+\.\d+)\s*$", "FOOD4RHINO.md version notes"),
            ["bench header"] = One(Read("tools", "webgen", "template.html"), @"version\s+(\d+\.\d+\.\d+)\s+·", "template.html header"),
        };
        var distinct = v.Values.Distinct().ToList();
        Assert.True(distinct.Count == 1, "versions disagree: " + string.Join(", ", v.Select(kv => kv.Key + " = " + kv.Value)));
        Assert.Matches(@"^\d+\.\d+\.\d+$", distinct[0]);
    }

    [Fact]
    public void ComponentCount_MatchesTheClasses()
    {
        var comps = Components();
        int n = comps.Count;
        Assert.True(n > 0, "no component classes found");
        Assert.Equal(comps.Count, comps.Select(c => c.name).Distinct().Count());

        var stated = new Dictionary<string, int>
        {
            ["README project table"] = int.Parse(One(Read("README.md"), @"Grasshopper plugin \((\d+) components\)", "README")),
            ["FOOD4RHINO description"] = int.Parse(One(Read("docs", "FOOD4RHINO.md"), @"(\d+)\s+components in", "FOOD4RHINO.md")),
            ["bench header"] = int.Parse(One(Read("tools", "webgen", "template.html"), @"·\s*(\d+)\s+components", "template.html")),
            ["smoke test: icons"] = int.Parse(One(Read("tools", "Mite.SmokeTest", "Program.cs"), @"Icons load for all (\d+) components", "SmokeTest")),
            ["smoke test: expected"] = int.Parse(One(Read("tools", "Mite.SmokeTest", "Program.cs"), @"expected (\d+) components", "SmokeTest")),
        };
        foreach (var kv in stated)
            Assert.True(kv.Value == n, $"{kv.Key} states {kv.Value} components, the Grasshopper project has {n}");
    }

    [Fact]
    public void PanelCount_AndReadmeList_MatchTheClasses()
    {
        var comps = Components();
        var panels = comps.Select(c => c.panel).Distinct().ToList();

        string[] words = { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve" };
        var w = One(Read("docs", "FOOD4RHINO.md"), @"components in (\w+) panels", "FOOD4RHINO.md panels");
        int stated = int.TryParse(w, out var k) ? k : Array.IndexOf(words, w.ToLowerInvariant());
        Assert.True(stated == panels.Count, $"FOOD4RHINO.md says {w} panels, the components use {panels.Count}: {string.Join(", ", panels)}");

        // README "## Grasshopper Components": "- **Panel** — Name, Name, …"
        var section = Read("README.md").Split(new[] { "## Grasshopper Components" }, StringSplitOptions.None)[1].Split(new[] { "\n## " }, StringSplitOptions.None)[0];
        var listed = new Dictionary<string, HashSet<string>>();
        foreach (Match m in Regex.Matches(section, @"^- \*\*([^*]+)\*\*\s+\S\s+(.+)$", RegexOptions.Multiline))
            listed[m.Groups[1].Value.Trim()] = new HashSet<string>(m.Groups[2].Value.Split(',').Select(x => x.Trim()));
        var missing = comps.Where(c => !listed.TryGetValue(c.panel, out var set) || !set.Contains(c.name))
                           .Select(c => $"{c.name} ({c.panel})").ToList();
        Assert.True(missing.Count == 0, "README component list misses: " + string.Join(", ", missing));
        int listedCount = listed.Values.Sum(s => s.Count);
        Assert.True(listedCount == comps.Count, $"README lists {listedCount} components, the project has {comps.Count}");
    }

    [Fact]
    public void EveryComponentIcon_IsDrawnEmbeddedAndOnDisk()
    {
        var comps = Components();
        var csproj = Read("src", "Mite.Grasshopper", "Mite.Grasshopper.csproj");
        var embedded = new HashSet<string>(Regex.Matches(csproj, @"<EmbeddedResource Include=""Resources\\(\w+)\.png""").Cast<Match>().Select(m => m.Groups[1].Value));
        var drawn = new HashSet<string>(Regex.Matches(Read("tools", "generate_icons.py"), @"finish\(img,\s*""(\w+)""\)").Cast<Match>().Select(m => m.Groups[1].Value));
        var all = One(Read("tools", "generate_icons.py"), @"^ALL\s*=\s*\[([^\]]+)\]", "generate_icons.py ALL");
        int allCount = all.Split(',').Count(x => x.Trim().Length > 0);

        foreach (var c in comps)
        {
            Assert.True(embedded.Contains(c.icon), $"{c.cls}: icon {c.icon}.png is not an EmbeddedResource");
            Assert.True(drawn.Contains(c.icon), $"{c.cls}: icon {c.icon} is not drawn by tools/generate_icons.py");
            Assert.True(File.Exists(Path.Combine(Root(), "src", "Mite.Grasshopper", "Resources", c.icon + ".png")), $"{c.cls}: Resources/{c.icon}.png missing");
        }
        Assert.True(embedded.Contains("Mite_Tab"), "the tab icon is not embedded");
        Assert.True(allCount == comps.Count + 1, $"generate_icons.py ALL has {allCount} entries, expected {comps.Count} components + the tab");
        Assert.True(embedded.Count == comps.Count + 1, $"the csproj embeds {embedded.Count} icons, expected {comps.Count} components + the tab");
    }

    [Fact]
    public void PythonBytecode_IsIgnored()
    {
        var lines = Read(".gitignore").Split('\n').Select(l => l.Trim()).ToList();
        Assert.Contains("__pycache__/", lines);
        Assert.Contains("*.pyc", lines);
    }
}
