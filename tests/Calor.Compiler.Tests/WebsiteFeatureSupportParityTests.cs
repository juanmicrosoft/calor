using System.Text.RegularExpressions;
using Calor.Compiler.Migration;
using Calor.Compiler.SelfCheck;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// #1585: every converter support level the website states equals the <see cref="FeatureSupport"/>
/// registry. The scan reads every published page (<c>website/content/**/*.md</c> and <c>*.mdx</c>,
/// the changelog excepted as a historical record).
/// <para>A claim is one of:</para>
/// <list type="bullet">
/// <item>a row of a table whose header has a <c>Level</c> column: one claim per backticked name in the
/// first cell;</item>
/// <item>prose: <c>`calor feature-check X` reports **L**</c>, <c>`X` is L</c>, or <c>`X` (L)</c>;</item>
/// <item>in a fenced block, a <c>"feature"</c>/<c>"name"</c> value followed by a <c>"supportLevel"</c>
/// value (the command's JSON output);</item>
/// <item>the converter's warning text, quoted anywhere: <c>Feature 'X' is partially supported</c>
/// (or <c>fully</c>/<c>not supported</c>, <c>requires manual intervention</c>).</item>
/// </list>
/// <para>Guard against unread claims: on any line that names a registry feature (backticked, quoted,
/// bracketed, or any hyphenated registry name) or mentions <c>feature-check</c>/<c>supportLevel</c>,
/// and on every line of a fence that does, each
/// level word (Full, Partial, NotSupported, ManualRequired, in any case) and each support phrase
/// ("fully supported", "not supported", "full support", ...) must belong to a claim. Anything else
/// fails unless listed in <see cref="NotClaims"/>. Per-page claim counts are pinned.</para>
/// </summary>
public sealed class WebsiteFeatureSupportParityTests
{
    private const string Levels = "Full|Partial|NotSupported|ManualRequired";

    /// <summary>
    /// Known gaps between the registry and converter behavior, as listed on the feature-check page
    /// under "Known gaps between the registry and ... behavior". The page states the registry
    /// level for each one and qualifies it; the claim is still compared with the registry. This
    /// list and the page's list must match exactly, so neither can drift from the other.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> KnownGaps = new Dictionary<string, string>
    {
        ["yield-return"] = "The registry says Full, which holds for iterator methods only. A `yield` in a property " +
                           "or indexer getter is the separate `iterator-accessor` entry (NotSupported) and stays C#.",
    };

    /// <summary>Passages whose level words state no support level for a feature (page, exact text
    /// within one line). Each excuses only its own level words, not the rest of its paragraph.</summary>
    private static readonly (string Page, string Line, string Why)[] NotClaims =
    [
        ("cli/feature-check.mdx", "0.25 converter (not yet released) reports them. `Partial` means some shapes",
            "defines the word Partial"),
        ("guides/csharp-interoperability.mdx",
            "| `yield` in a property or indexer getter; partial properties | Member preserved | `iterator-accessor`, `partial-property` |",
            "'partial properties' is the C# construct (partial-property), not a support level"),
        ("guides/csharp-interoperability.mdx", "interpolation is not fully supported",
            "restates, in words, the Partial claim made in the same sentence"),
    ];

    /// <summary>Claims per page. A changed count means a claim was added, removed, or no longer parses.</summary>
    private static readonly IReadOnlyDictionary<string, int> PinnedClaims = new Dictionary<string, int>
    {
        ["cli/convert.mdx"] = 1,
        ["cli/feature-check.mdx"] = 15,
        ["guides/csharp-interoperability.mdx"] = 1,
        ["syntax-reference/string-operations.mdx"] = 1,
    };

    internal sealed record Claim(string Page, int Line, string Feature, string Level, string Shape);

    private static readonly Regex Reports = new(@"`calor feature-check (?<f>[a-z0-9-]+)` reports \*\*(?<l>" + Levels + @")\*\*");
    private static readonly Regex IsLevel = new(@"`(?<f>[a-z0-9-]+)` is (?<l>" + Levels + @")\b");
    private static readonly Regex Parenthesized = new(@"`(?<f>[a-z0-9-]+)` \((?<l>" + Levels + @")\)");
    private static readonly Regex FenceName = new(@"""(?:feature|name)""\s*:\s*""(?<f>[^""]+)""");
    private static readonly Regex FenceLevel = new(@"""supportLevel""\s*:\s*""(?<l>[^""]*)""");
    // Every way of stating a level that the guard counts. Claims account for the level words.
    // Prose and tables spell levels as the enum names; the command's JSON output uses lower case.
    private static readonly Regex LevelWord = new(@"(?<![\w-])(?:" + Levels + @")(?![\w-])");
    private static readonly Regex AnyCaseLevelWord = new(@"(?<![\w-])(?:" + Levels + @")(?![\w-])", RegexOptions.IgnoreCase);
    private static readonly Regex SupportPhrase = new(
        @"\b(?:fully|partially|not|partly)\s+supported\b|\b(?:full|partial|no)\s+support\b|\bmanual(?:ly)?\s+required\b" +
        @"|\brequires\s+manual\s+intervention\b",
        RegexOptions.IgnoreCase);
    // The converter's own warning text (ConversionContext): "Feature 'X' is partially supported",
    // "... is not supported", "... requires manual intervention". A page quoting it states a level.
    private static readonly Regex Diagnostic = new(
        @"Feature '(?<f>[a-z0-9-]+)' (?:is (?<p>fully|partially|not) supported|(?<m>requires manual intervention))",
        RegexOptions.IgnoreCase);
    private static readonly Regex Backticked = new("`(?<n>[a-z0-9-]+)`");
    // A registry name written inside other code or quotes: 'out-var', [out-var], "out-var".
    private static readonly Regex Quoted = new(@"['\[""](?<n>[a-z0-9-]+)['\]""]");
    private static readonly Regex HyphenatedToken = new(@"(?<![\w-])(?<n>[a-z0-9]+(?:-[a-z0-9]+)+)(?![\w-])");
    private static readonly Regex LevelOption = new(@"--level\s+\S+");
    private static readonly Regex ListItem = new(@"^(?:[-*+]|\d+\.)\s");
    private static readonly Regex TableRule =new(@"^\|(?:\s*:?-+:?\s*\|)+$");

    [Fact]
    public void EveryStatedSupportLevelMatchesTheRegistry()
    {
        var (claims, unattributed) = ScanDirectory(Path.Combine(RepoRoot(), "website", "content"));
        Assert.Empty(unattributed);
        Assert.Empty(Mismatches(claims));
    }

    [Fact]
    public void ClaimCoverageIsPinned()
    {
        var (claims, _) = ScanDirectory(Path.Combine(RepoRoot(), "website", "content"));
        var counts = claims.GroupBy(c => c.Page).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(PinnedClaims.OrderBy(p => p.Key, StringComparer.Ordinal),
            counts.OrderBy(p => p.Key, StringComparer.Ordinal));
        // The feature-check table is the bulk of the claims; it must parse as a table.
        Assert.Equal(13, claims.Count(c => c.Shape == "table"));
    }

    [Fact]
    public void KnownGapsListMatchesTheFeatureCheckPage()
    {
        var text = File.ReadAllText(PagePath("cli/feature-check.mdx"));
        Assert.Equal(KnownGaps.Keys.Order(StringComparer.Ordinal), ListedGaps(text).Order(StringComparer.Ordinal));
        foreach (var (feature, reason) in KnownGaps)
        {
            Assert.NotNull(FeatureSupport.GetFeatureInfo(feature));
            Assert.False(string.IsNullOrWhiteSpace(reason));
        }
    }

    // Negative controls: one deliberately wrong support level (or an unknown or unparsed claim) on
    // a real page must fail the check.
    [Theory]
    [InlineData("cli/feature-check.mdx", "| `in-parameter` | Full |", "| `in-parameter` | Partial |", "in-parameter")]
    [InlineData("cli/feature-check.mdx", "| `iterator-accessor` | NotSupported |", "| `iterator-accessor` | Full |", "iterator-accessor")]
    [InlineData("cli/feature-check.mdx", "| `ref-parameter` | Partial |", "| `ref-parameter`, `in-parameter` | Partial |", "in-parameter")]
    [InlineData("cli/feature-check.mdx", "`iterator-accessor` (NotSupported)", "`iterator-accessor` (Partial)", "iterator-accessor")]
    [InlineData("cli/feature-check.mdx", "`yield-return` is Full", "`yield-return` is Partial", "yield-return")]
    [InlineData("cli/feature-check.mdx", "| `local-function` | Partial |", "| `local-functions` | Partial |", "local-functions")]
    [InlineData("cli/feature-check.mdx", "| `out-var` | Partial |", "| `out-var` | Mostly |", "out-var")]
    [InlineData("syntax-reference/string-operations.mdx", "reports **Partial**", "reports **Full**", "string-interpolation")]
    [InlineData("guides/csharp-interoperability.mdx", "reports **Partial**", "reports **NotSupported**", "string-interpolation")]
    [InlineData("cli/convert.mdx", "Feature 'out-var' is partially supported", "Feature 'out-var' is fully supported", "out-var")]
    [InlineData("cli/convert.mdx", "Feature 'out-var' is partially supported", "Feature 'out-var' requires manual intervention", "out-var")]
    public void AWrongSupportLevelOnAPageFailsTheCheck(string page, string from, string to, string feature)
    {
        var text = File.ReadAllText(PagePath(page));
        Assert.Contains(from, text);
        var (claims, unattributed) = Scan(page, text.Replace(from, to));
        Assert.Empty(unattributed);
        Assert.Contains(Mismatches(claims), f => f.Contains($"`{feature}`", StringComparison.Ordinal));
        // The unmodified page passes, so the failure comes from the edit alone.
        Assert.Empty(Mismatches(Scan(page, text).Claims));
    }

    [Fact]
    public void AWrongLevelInFencedFeatureCheckOutputFailsTheCheck()
    {
        var text = File.ReadAllText(PagePath("cli/feature-check.mdx"));
        var fence = "\n```json\n{\n  \"data\": {\n    \"feature\": \"iterator-accessor\",\n    \"found\": true,\n" +
                    "    \"supportLevel\": \"full\"\n  }\n}\n```\n";
        var (claims, unattributed) = Scan("cli/feature-check.mdx", text + fence);
        Assert.Empty(unattributed);
        Assert.Contains(Mismatches(claims), f => f.Contains("`iterator-accessor` is stated as full", StringComparison.Ordinal));
        // The same output with the registry's level passes.
        var right = Scan("cli/feature-check.mdx", text + fence.Replace("\"full\"", "\"notsupported\""));
        Assert.Empty(right.Unattributed);
        Assert.Empty(Mismatches(right.Claims));
    }

    [Theory]
    [InlineData("syntax-reference/string-operations.mdx", "`calor feature-check string-interpolation` reports **Partial**",
        "`calor feature-check string-interpolation` is reported as **Full**")]
    [InlineData("cli/feature-check.mdx", "`iterator-accessor` (NotSupported)", "`iterator-accessor` (**Full**)")]
    [InlineData("cli/feature-check.mdx", "`iterator-accessor` (NotSupported).", "`iterator-accessor` (NotSupported). `iterator-accessor` has Full support.")]
    [InlineData("cli/feature-check.mdx", "`iterator-accessor` (NotSupported).", "`iterator-accessor` (NotSupported). `partial-property` is fully supported.")]
    [InlineData("cli/feature-check.mdx", "calor feature-check --list\n", "calor feature-check --list\nSupport level: Full\n")]
    [InlineData("cli/feature-check.mdx", "`iterator-accessor` (NotSupported).", "`iterator-accessor` (NotSupported). `iterator-accessor` is full.")]
    [InlineData("cli/feature-check.mdx", "`iterator-accessor` (NotSupported).", "`iterator-accessor` (NotSupported). `iterator-accessor` is FULL.")]
    [InlineData("cli/convert.mdx", "`[out-var] Feature 'out-var' is partially supported…`", "`[out-var] out-var is Full…`")]
    // Codex round 3: a claim wrapped across a soft line break is one statement.
    [InlineData("cli/convert.mdx", "### Counts, Partial Results, and the JSON Envelope\n",
        "The converter handles declarations without preserving raw C#: `partial-property` is\nfully supported.\n\n### Counts, Partial Results, and the JSON Envelope\n")]
    [InlineData("cli/feature-check.mdx", "0.25 converter (not yet released) reports them. `Partial` means some shapes",
        "0.25 converter (not yet released) reports them. `Partial` means some shapes; `partial-property` is fully supported")]
    public void ALevelInAnUnrecognizedShapeFailsTheCheck(string page, string from, string to)
    {
        var text = File.ReadAllText(PagePath(page));
        Assert.Contains(from, text);
        Assert.NotEmpty(Scan(page, text.Replace(from, to)).Unattributed);
    }

    [Theory]
    [InlineData("guides/new-page.md")]
    [InlineData("guides/new-page.mdx")]
    public void ANewPageWithAWrongLevelFailsTheCheck(string page)
    {
        var root = Path.Combine(Path.GetTempPath(), "calor-parity-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "guides"));
            File.WriteAllText(Path.Combine(root, page), "# New\n\n`calor feature-check record` reports **Full**.\n");
            File.WriteAllText(Path.Combine(root, "changelog.mdx"), "`record` is Full.\n");
            var (claims, unattributed) = ScanDirectory(root);
            Assert.Empty(unattributed);
            var claim = Assert.Single(claims);
            Assert.Equal(page, claim.Page);
            Assert.Contains(Mismatches(claims), f => f.Contains("`record` is stated as Full; the registry says NotSupported", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AKnownGapAddedToThePageWithoutAReasonFails()
    {
        var text = File.ReadAllText(PagePath("cli/feature-check.mdx"));
        var anchor = "- `yield-return` is Full";
        Assert.Contains(anchor, text);
        var edited = text.Replace(anchor, "- `record` is NotSupported even for positional records.\n" + anchor);
        Assert.NotEqual(KnownGaps.Keys.Order(StringComparer.Ordinal), ListedGaps(edited).Order(StringComparer.Ordinal));
    }

    internal static List<string> Mismatches(IEnumerable<Claim> claims)
    {
        var failures = new List<string>();
        foreach (var claim in claims)
        {
            var where = $"{claim.Page}:{claim.Line}";
            var info = FeatureSupport.GetFeatureInfo(claim.Feature);
            // The command's JSON prints the level in lower case; prose and tables use the enum name.
            var level = Enum.GetValues<SupportLevel>().Cast<SupportLevel?>().FirstOrDefault(l =>
                claim.Shape == "output" ? l.ToString()!.ToLowerInvariant() == claim.Level : l.ToString() == claim.Level);
            if (info is null)
                failures.Add($"{where}: `{claim.Feature}` is not a FeatureSupport registry name");
            else if (level is null)
                failures.Add($"{where}: `{claim.Feature}` states '{claim.Level}', which is not a support level");
            else if (level != info.Support)
                failures.Add($"{where}: `{claim.Feature}` is stated as {claim.Level}; the registry says {info.Support}");
        }
        return failures;
    }

    private static (List<Claim> Claims, List<string> Unattributed) ScanDirectory(string content)
    {
        var claims = new List<Claim>();
        var unattributed = new List<string>();
        var pages = Directory.GetFiles(content, "*.*", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(".md", StringComparison.Ordinal) || p.EndsWith(".mdx", StringComparison.Ordinal));
        foreach (var path in pages.Order(StringComparer.Ordinal))
        {
            var page = Path.GetRelativePath(content, path).Replace('\\', '/');
            // Historical release records (the changelog) state the levels of past versions.
            if (WebsiteExampleChecker.HistoricalExclusions.ContainsKey(page))
                continue;
            var (c, u) = Scan(page, File.ReadAllText(path));
            claims.AddRange(c);
            unattributed.AddRange(u);
        }
        return (claims, unattributed);
    }

    internal static (List<Claim> Claims, List<string> Unattributed) Scan(string page, string text)
    {
        var claims = new List<Claim>();
        var unattributed = new List<string>();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        int? levelColumn = null;
        // Prose is read a paragraph (or list item) at a time, so a claim wrapped across soft line
        // breaks is one statement: "`x` is" + "fully supported." reads as "`x` is fully supported."
        var paragraph = new List<string>();
        var paragraphLine = 0;
        void Flush()
        {
            if (paragraph.Count > 0)
                CheckStatement(page, paragraphLine, string.Join(" ", paragraph.Select(l => l.Trim())), 0, claims, unattributed);
            paragraph.Clear();
        }
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                Flush();
                var end = Array.FindIndex(lines, i + 1, l => l.TrimStart().StartsWith("```", StringComparison.Ordinal));
                end = end < 0 ? lines.Length : end;
                ScanFence(page, lines, i + 1, end, claims, unattributed);
                i = end;
                levelColumn = null;
                continue;
            }

            if (!line.TrimStart().StartsWith('|'))
            {
                levelColumn = null;
                var trimmed = line.TrimStart();
                if (trimmed.Length == 0 || trimmed.StartsWith('#') || ListItem.IsMatch(trimmed))
                    Flush();
                if (trimmed.Length == 0)
                    continue;
                if (paragraph.Count == 0)
                    paragraphLine = i + 1;
                paragraph.Add(line);
                if (trimmed.StartsWith('#'))
                    Flush();
                continue;
            }

            Flush();
            var found = 0;
            var cells = Cells(line);
            var next = i + 1 < lines.Length ? lines[i + 1].Trim() : "";
            if (TableRule.IsMatch(next))
            {
                var index = cells.FindIndex(c => c.Equals("Level", StringComparison.OrdinalIgnoreCase));
                levelColumn = index >= 0 ? index : null;
                continue;
            }
            if (TableRule.IsMatch(line.Trim()))
                continue;
            if (levelColumn is int column && column < cells.Count)
            {
                foreach (Match feature in Backticked.Matches(cells[0]))
                {
                    claims.Add(new Claim(page, i + 1, feature.Groups["n"].Value, cells[column], "table"));
                    found++;
                }
                // A non-level value in the Level column is itself a claim (and a mismatch).
                if (found > 0 && !LevelWord.IsMatch(cells[column]))
                    found = 0;
                else if (found > 0)
                    found = 1;
            }
            CheckStatement(page, i + 1, line.Trim(), found, claims, unattributed);
        }
        Flush();
        return (claims, unattributed);
    }

    // A level word in another case is a statement when used predicatively ("`x` is full",
    // "reports FULL"); "the full list" or "partial members" is ordinary English.
    private static readonly Regex PredicativeLevel = new(
        @"\b(?:is|are|was|be|as|reports|level)\s*:?\s+(?:\*\*|`)?(?<l>full|partial|notsupported|manualrequired)(?![\w-])", RegexOptions.IgnoreCase);

    private static int Stated(string text)
    {
        text = LevelOption.Replace(text, "");
        var canonical = LevelWord.Matches(text).Count;
        var otherCase = PredicativeLevel.Matches(text).Count(m => !LevelWord.IsMatch(m.Groups["l"].Value));
        return canonical + otherCase + SupportPhrase.Matches(text).Count;
    }

    /// <summary>Reads the prose claims in one statement (a paragraph, list item, or table row) and
    /// fails any level word or support phrase in it that no claim accounts for.</summary>
    private static void CheckStatement(string page, int lineNo, string statement, int found, List<Claim> claims, List<string> unattributed)
    {
        foreach (var regex in new[] { Reports, IsLevel, Parenthesized })
            foreach (Match m in regex.Matches(statement))
            {
                claims.Add(new Claim(page, lineNo, m.Groups["f"].Value, m.Groups["l"].Value,
                    regex == Reports ? "reports" : regex == IsLevel ? "is" : "parenthesized"));
                found++;
            }

        // A quoted converter warning counts its phrase as the claim's level word.
        foreach (Match m in Diagnostic.Matches(statement))
        {
            var level = m.Groups["m"].Success ? "ManualRequired"
                : m.Groups["p"].Value.ToLowerInvariant() switch { "fully" => "Full", "partially" => "Partial", _ => "NotSupported" };
            claims.Add(new Claim(page, lineNo, m.Groups["f"].Value, level, "diagnostic"));
            found++;
        }

        if (!MentionsFeatures(statement))
            return;
        // Any case: "`x` is full" states a level as much as "`x` is Full" does.
        var stated = Stated(statement);
        // An allowlisted passage excuses only its own level words, not the rest of the paragraph.
        foreach (var excused in NotClaims.Where(n => n.Page == page && statement.Contains(n.Line, StringComparison.Ordinal)))
            stated -= Stated(excused.Line);
        if (stated > found)
            unattributed.Add($"{page}:{lineNo}: a support level in a form the parity check does not read: {statement}");
    }

    /// <summary>Fenced lines [start, end). A fence that mentions a feature or the command is checked
    /// line by line: every level word must be a <c>"supportLevel"</c> value paired with a feature.</summary>
    private static void ScanFence(string page, string[] lines, int start, int end, List<Claim> claims, List<string> unattributed)
    {
        var body = lines.Skip(start).Take(end - start).ToArray();
        if (!body.Any(MentionsFeatures))
            return;
        string? feature = null;
        for (var k = 0; k < body.Length; k++)
        {
            var line = body[k];
            var name = FenceName.Match(line);
            if (name.Success)
                feature = name.Groups["f"].Value;
            var found = 0;
            var level = FenceLevel.Match(line);
            if (level.Success && feature is not null)
            {
                claims.Add(new Claim(page, start + k + 1, feature, level.Groups["l"].Value, "output"));
                feature = null;
                found = 1;
            }
            foreach (Match m in Diagnostic.Matches(line))
            {
                var quoted = m.Groups["m"].Success ? "ManualRequired"
                    : m.Groups["p"].Value.ToLowerInvariant() switch { "fully" => "Full", "partially" => "Partial", _ => "NotSupported" };
                claims.Add(new Claim(page, start + k + 1, m.Groups["f"].Value, quoted, "diagnostic"));
                found++;
            }
            var stated = AnyCaseLevelWord.Matches(LevelOption.Replace(line, "")).Count + SupportPhrase.Matches(line).Count
                         + (level.Success && found == 0 ? 1 : 0);
            if (stated > found)
                unattributed.Add($"{page}:{start + k + 1}: a support level in a fence the parity check does not read: {line.Trim()}");
        }
    }

    private static bool MentionsFeatures(string line) =>
        line.Contains("feature-check", StringComparison.Ordinal)
        || line.Contains("supportLevel", StringComparison.Ordinal)
        || new[] { Backticked, Quoted, HyphenatedToken }.Any(r =>
            r.Matches(line).Any(m => FeatureSupport.GetFeatureInfo(m.Groups["n"].Value) is not null))
        || FenceName.Matches(line).Any(m => FeatureSupport.GetFeatureInfo(m.Groups["f"].Value) is not null);

    /// <summary>The first backticked name of each bullet under "Known gaps between the registry".</summary>
    internal static List<string> ListedGaps(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var start = Array.FindIndex(lines, l => l.StartsWith("**Known gaps between the registry", StringComparison.Ordinal));
        Assert.True(start >= 0, "the feature-check page no longer has its known-gaps list");
        var gaps = new List<string>();
        for (var i = start + 1; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("#", StringComparison.Ordinal) || lines[i].StartsWith("**", StringComparison.Ordinal))
                break;
            var bullet = Regex.Match(lines[i], "^- `(?<f>[a-z0-9-]+)`");
            if (bullet.Success)
                gaps.Add(bullet.Groups["f"].Value);
        }
        return gaps;
    }

    private static List<string> Cells(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith('|')) trimmed = trimmed[1..];
        if (trimmed.EndsWith('|')) trimmed = trimmed[..^1];
        return trimmed.Split('|').Select(c => c.Trim()).ToList();
    }

    private static string PagePath(string page) => Path.Combine(RepoRoot(), "website", "content", page);

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "website", "content")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory.FullName;
    }
}
