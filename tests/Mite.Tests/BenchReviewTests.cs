using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Jint;
using Xunit;

namespace Mite.Tests;

/// <summary>
/// The Mite Bench review tools (tools/webgen/review.js): find / filter with
/// query previews, keyboard map, URL state and the GitHub issue draft. The
/// pure functions of the script run here under Jint (a .NET JavaScript
/// interpreter), so the page logic is checked on every platform without a
/// browser; tools/webgen/bench-smoke.js drives the built page in Chromium.
/// Rules checked (sources in the header of review.js): Hearst 2009 ch. 8
/// (counts next to facet values equal the result of picking them; no dead
/// ends), WCAG 2.1 SC 2.1.4 (single-key shortcuts can be turned off and never
/// fire in text fields), GitHub's issues/new query (title and body only;
/// a URL over the budget is cut at whole lines with a pointer to the
/// clipboard).
/// </summary>
public class BenchReviewTests
{
    static readonly Lazy<string> Script = new(() =>
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "tools", "webgen", "review.js"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "tools", "webgen", "review.js"));
    });

    static Engine NewEngine()
    {
        var e = new Engine();
        e.Execute(Script.Value);
        return e;
    }

    static JsonElement Js(Engine e, string expr) =>
        JsonDocument.Parse(e.Evaluate("JSON.stringify(" + expr + ")").AsString()).RootElement;

    static string Str(Engine e, string expr) => e.Evaluate(expr).AsString();

    static string Lit(string s) => JsonSerializer.Serialize(s);

    const string Cards = @"[
      { id: 'asymptotic-net', tab: 'Gridshells', text: 'Asymptotic Net AsymNet saddle z = x² − y² Spacing', failing: 1, reviewed: true, note: '' },
      { id: 'geodesic-net', tab: 'Gridshells', text: 'Geodesic Net GeoNet Jacobi start angles', failing: 0, reviewed: false, note: 'check spacing' },
      { id: 'lath-sweep', tab: 'Fabrication', text: 'Lath Sweep LathSweep Shape Section', failing: 0, reviewed: true, note: '  ' },
      { id: 'mesh-colour-map', tab: 'Util', text: 'Mesh Colour Map ColourMap Palette', failing: 2, reviewed: false, note: '' }]";

    [Theory]
    [InlineData("Mesh Colour Map", "mesh colour map")]
    [InlineData("Schöne Fläche", "schone flache")]
    [InlineData("z = x² − y²", "z = x2 - y2")]
    [InlineData("  A\t·  B ", "a b")]
    [InlineData("Lath–Sweep", "lath-sweep")]
    public void Fold_IgnoresCaseAccentsAndTypography(string input, string expected)
    {
        var e = NewEngine();
        Assert.Equal(expected, Str(e, "MiteReview.fold(" + Lit(input) + ")"));
    }

    [Fact]
    public void ParseQuery_SplitsTermsAndKeepsQuotedPhrases()
    {
        var e = NewEngine();
        var t = Js(e, "MiteReview.parseQuery('asym \"Lath   Sweep\" x²')").EnumerateArray().Select(x => x.GetString()).ToArray();
        Assert.Equal(new[] { "asym", "lath sweep", "x2" }, t);
        Assert.Equal(0, Js(e, "MiteReview.parseQuery('  \"\" ')").GetArrayLength());
        Assert.True(e.Evaluate("MiteReview.matches('Geodesic Net GeoNet', MiteReview.parseQuery('net GEO'))").AsBoolean());
        Assert.False(e.Evaluate("MiteReview.matches('Geodesic Net GeoNet', MiteReview.parseQuery('net lath'))").AsBoolean());
        Assert.True(e.Evaluate("MiteReview.matches('saddle z = x² − y²', MiteReview.parseQuery('\"x2 - y2\"'))").AsBoolean());
    }

    [Fact]
    public void Filter_FacetCounts_AreTheResultsOfPickingThem()
    {
        var e = NewEngine();
        e.Execute("var C = " + Cards + ";");
        var r = Js(e, "MiteReview.filter(C, {})");
        Assert.Equal(4, r.GetProperty("visible").GetArrayLength());
        var sc = r.GetProperty("statusCounts");
        Assert.Equal(4, sc.GetProperty("all").GetInt32());
        Assert.Equal(2, sc.GetProperty("failing").GetInt32());
        Assert.Equal(2, sc.GetProperty("unreviewed").GetInt32());
        Assert.Equal(2, sc.GetProperty("reviewed").GetInt32());
        Assert.Equal(1, sc.GetProperty("noted").GetInt32()); // a blank note is no note
        var tc = r.GetProperty("tabCounts");
        Assert.Equal(2, tc.GetProperty("Gridshells").GetInt32());
        Assert.Equal(1, tc.GetProperty("Fabrication").GetInt32());

        // text + facets
        var v = Js(e, "MiteReview.filter(C, { q: 'net', status: 'failing', tab: 'Gridshells' }).visible");
        Assert.Equal(new[] { "asymptotic-net" }, v.EnumerateArray().Select(x => x.GetString()).ToArray());
        var t2 = Js(e, "MiteReview.filter(C, { status: 'failing' }).tabCounts");
        Assert.Equal(0, t2.GetProperty("Fabrication").GetInt32()); // present, at 0: shown disabled
        Assert.Equal(1, t2.GetProperty("Util").GetInt32());

        // every preview count equals the size of the result of choosing that value
        string[] statuses = { "all", "failing", "unreviewed", "reviewed", "noted" };
        string[] tabs = { "all", "Gridshells", "Fabrication", "Util" };
        string[] queries = { "", "net", "colour", "x2 - y2", "zz", "\"lath sweep\"" };
        int checkedCount = 0;
        foreach (var q in queries)
        foreach (var s in statuses)
        foreach (var tab in tabs)
        {
            var res = Js(e, $"MiteReview.filter(C, {{ q: {Lit(q)}, status: '{s}', tab: {Lit(tab)} }})");
            int n = res.GetProperty("visible").GetArrayLength();
            Assert.Equal(n, res.GetProperty("statusCounts").GetProperty(s).GetInt32());
            Assert.Equal(n, res.GetProperty("tabCounts").GetProperty(tab).GetInt32());
            checkedCount++;
        }
        Assert.Equal(queries.Length * statuses.Length * tabs.Length, checkedCount);

        // unknown values fall back to "all" instead of hiding everything
        Assert.Equal(4, Js(e, "MiteReview.filter(C, { status: 'bogus' }).visible").GetArrayLength());
    }

    [Fact]
    public void StatusLine_NamesTheCountAndTheActiveFilters()
    {
        var e = NewEngine();
        e.Execute("var C = " + Cards + ";");
        Assert.Equal("All 4 components", Str(e, "MiteReview.statusLine(MiteReview.filter(C, {}), 4)"));
        Assert.Equal("1 of 4 components · Failing · Gridshells · “net”",
            Str(e, "MiteReview.statusLine(MiteReview.filter(C, { q: 'Net', status: 'failing', tab: 'Gridshells' }), 4)"));
        Assert.Equal("0 of 4 components · “zz”", Str(e, "MiteReview.statusLine(MiteReview.filter(C, { q: 'zz' }), 4)"));
    }

    [Theory]
    [InlineData("/", false, false, true, "search")]
    [InlineData("j", false, false, true, "next")]
    [InlineData("k", false, false, true, "prev")]
    [InlineData("x", false, false, true, "toggle")]
    [InlineData("n", false, false, true, "note")]
    [InlineData("f", false, false, true, "nextFailing")]
    [InlineData("?", false, false, true, "help")]
    [InlineData("j", true, false, true, null)]        // typing in a note or the find box
    [InlineData("/", true, false, true, null)]
    [InlineData("Escape", true, false, true, "leave")]
    [InlineData("Escape", false, false, true, null)]  // Esc outside a field belongs to the browser / dialog
    [InlineData("j", false, true, true, null)]        // Ctrl/Alt/Meta chords are left alone
    [InlineData("j", false, false, false, null)]      // SC 2.1.4: single keys switched off
    [InlineData("?", false, false, false, null)]
    [InlineData("Escape", true, false, false, "leave")]
    [InlineData("J", false, false, true, null)]       // Shift+j is not j
    [InlineData("toString", false, false, true, null)]
    public void KeyAction_FollowsTheShortcutRules(string key, bool editable, bool ctrl, bool on, string? expected)
    {
        var e = NewEngine();
        var v = e.Evaluate($"MiteReview.keyAction({{ key: {Lit(key)}, ctrlKey: {(ctrl ? "true" : "false")}, altKey: false, metaKey: false, editable: {(editable ? "true" : "false")} }}, {(on ? "true" : "false")})");
        if (expected == null) Assert.True(v.IsNull(), key + " gave " + v);
        else Assert.Equal(expected, v.AsString());
    }

    [Fact]
    public void Step_MovesWithinTheVisibleCards()
    {
        var e = NewEngine();
        e.Execute("var I = ['a', 'b', 'c'];");
        Assert.Equal(0, (int)e.Evaluate("MiteReview.step(I, null, 1)").AsNumber());
        Assert.Equal(2, (int)e.Evaluate("MiteReview.step(I, null, -1)").AsNumber());
        Assert.Equal(2, (int)e.Evaluate("MiteReview.step(I, 'b', 1)").AsNumber());
        Assert.Equal(2, (int)e.Evaluate("MiteReview.step(I, 'c', 1)").AsNumber());  // stops at the end
        Assert.Equal(0, (int)e.Evaluate("MiteReview.step(I, 'a', -1)").AsNumber());
        Assert.Equal(0, (int)e.Evaluate("MiteReview.step(I, 'hidden', 1)").AsNumber());
        Assert.Equal(-1, (int)e.Evaluate("MiteReview.step([], null, 1)").AsNumber());
    }

    [Fact]
    public void UrlState_RoundTrips_AndDropsDefaultsAndUnknowns()
    {
        var e = NewEngine();
        Assert.Equal("", Str(e, "MiteReview.toSearch({ q: '  ', status: 'all', tab: 'all' })"));
        var s = Str(e, "MiteReview.toSearch({ q: 'x² & y', status: 'failing', tab: 'Form Finding' })");
        Assert.Equal("?q=x%C2%B2%20%26%20y&show=failing&tab=Form%20Finding", s);
        var back = Js(e, $"MiteReview.fromSearch({Lit(s)}, ['Curvature', 'Form Finding'])");
        Assert.Equal("x² & y", back.GetProperty("q").GetString());
        Assert.Equal("failing", back.GetProperty("status").GetString());
        Assert.Equal("Form Finding", back.GetProperty("tab").GetString());
        var bad = Js(e, "MiteReview.fromSearch('?q=lath+sweep&show=nope&tab=Nowhere&x=%E0%A4%A', ['Curvature'])");
        Assert.Equal("lath sweep", bad.GetProperty("q").GetString());
        Assert.Equal("all", bad.GetProperty("status").GetString());
        Assert.Equal("all", bad.GetProperty("tab").GetString());
    }

    const string Review = @"{ generated: 'Generated from the Mite 1.3.0 sources ', cards: [
      { name: 'Asymptotic Net', tab: 'Gridshells', reviewed: true, note: '', failing: [{ label: 'Saddle families', detail: 'A 31, B 30' }] },
      { name: 'Geodesic Net', tab: 'Gridshells', reviewed: false, note: 'check   spacing\nnear the rim', failing: [] },
      { name: 'Lath Sweep', tab: 'Fabrication', reviewed: true, note: '', failing: [] },
      { name: 'Mesh Colour Map', tab: 'Util', reviewed: false, note: ' ', failing: [{ label: 'Legend', detail: '`x`' }, { label: 'Palette', detail: '' }] }] }";

    [Fact]
    public void IssueMarkdown_IsATaskListWithFailingTestsFirst()
    {
        var e = NewEngine();
        e.Execute("var R = " + Review + ";");
        var md = Str(e, "MiteReview.issueMarkdown(R)");
        var expected = string.Join("\n",
            "**2 of 4** components marked as making sense · **3** self-tests failing · Generated from the Mite 1.3.0 sources",
            "",
            "### Failing self-tests",
            "",
            "- **Asymptotic Net**: Saddle families — `A 31, B 30`",
            "- **Mesh Colour Map**: Legend — `'x'`",
            "- **Mesh Colour Map**: Palette",
            "",
            "### Components",
            "",
            "- [x] **Asymptotic Net**",
            "- [ ] **Geodesic Net** — check spacing near the rim",
            "- [x] **Lath Sweep**",
            "",
            "Not reviewed yet (1): Mesh Colour Map.") + "\n";
        Assert.Equal(expected, md);
        Assert.Equal("Mite Bench review: 2/4 make sense, 3 self-tests failing", Str(e, "MiteReview.issueTitle(R)"));
    }

    [Fact]
    public void IssueUrl_ShortReview_CarriesTheWholeBody()
    {
        var e = NewEngine();
        e.Execute("var R = " + Review + "; var md = MiteReview.issueMarkdown(R); var L = MiteReview.issueUrl('jikempff/Mite', MiteReview.issueTitle(R), md);");
        var url = Str(e, "L.url");
        Assert.True(e.Evaluate("L.complete").AsBoolean());
        Assert.StartsWith("https://github.com/jikempff/Mite/issues/new?title=", url);
        var q = ParseQuery(url);
        Assert.Equal(new[] { "title", "body" }, q.Select(p => p.Key).ToArray()); // no labels / assignees: they 404 without triage rights
        Assert.Equal(Str(e, "md"), q[1].Value);
        Assert.Equal(Str(e, "MiteReview.issueTitle(R)"), q[0].Value);
    }

    [Fact]
    public void IssueUrl_LongReview_IsCutAtWholeLinesWithinTheBudget()
    {
        var e = NewEngine();
        // 29 components with long notes full of characters that expand when encoded
        e.Execute(@"var cards = []; for (var i = 0; i < 29; i++) cards.push({ name: 'Component ' + i, tab: 'T', reviewed: i % 2 == 0,
            note: ('é²−&#% note ' + i + ' ').repeat(25), failing: i == 7 ? [{ label: 'first failing', detail: '1' }] : [] });
            var md = MiteReview.issueMarkdown({ generated: '', cards: cards });");
        int bodyLength = (int)e.Evaluate("md.length").AsNumber();
        Assert.True(bodyLength > 10000, "body " + bodyLength);
        foreach (var budget in new[] { 8000, 3000 })
        {
            e.Execute($"var L = MiteReview.issueUrl('jikempff/Mite', 'Mite Bench review', md, {budget});");
            var url = Str(e, "L.url");
            Assert.False(e.Evaluate("L.complete").AsBoolean());
            Assert.True(url.Length <= budget, $"{url.Length} > {budget}");
            Assert.True(url.Length > budget - 1200, $"{url.Length} wastes the budget {budget}"); // one note line is ~1000 encoded
            int kept = (int)e.Evaluate("L.keptLines").AsNumber();
            var body = ParseQuery(url)[1].Value;
            var md = Str(e, "md").Split('\n');
            Assert.StartsWith(string.Join("\n", md.Take(kept)) + "\n\n_The review was too long for a link (" + bodyLength + " characters)", body);
            Assert.Contains("- **Component 7**: first failing", body); // failing tests come first and survive the cut
        }
        e.Execute("var L0 = MiteReview.issueUrl('jikempff/Mite', 'Mite Bench review', md);");
        Assert.True(Str(e, "L0.url").Length <= (int)e.Evaluate("MiteReview.URL_BUDGET").AsNumber());
    }

    static System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>> ParseQuery(string url)
    {
        var query = url.Substring(url.IndexOf('?') + 1);
        return query.Split('&').Select(kv =>
        {
            var i = kv.IndexOf('=');
            return new System.Collections.Generic.KeyValuePair<string, string>(kv.Substring(0, i), Uri.UnescapeDataString(kv.Substring(i + 1)));
        }).ToList();
    }
}
