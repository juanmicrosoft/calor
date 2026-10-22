using System.Text.RegularExpressions;
using Calor.Compiler.Migration;
using Calor.Compiler.SelfCheck;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// #1585: every converter support level the website states equals the <see cref="FeatureSupport"/>
/// registry. A claim is a table row whose header has a <c>Level</c> column (the feature-check page),
/// or one of three prose shapes: <c>`calor feature-check X` reports **Level**</c>,
/// <c>`X` is Level</c>, and <c>`X` (Level)</c>. A level word written in any other strong form
/// (bold, parenthesized, backticked, a table cell, "is Level") that is not part of a claim fails,
/// so a new claim shape cannot slip past the comparison. Per-page claim counts are pinned.
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

    /// <summary>Level-word uses that state no support level for a feature (page, exact line text).</summary>
    private static readonly (string Page, string Line)[] NotClaims =
    [
        ("cli/feature-check.mdx", "0.25 converter (not yet released) reports them. `Partial` means some shapes"),
    ];

    /// <summary>Claims per page. A changed count means a claim was added, removed, or no longer parses.</summary>
    private static readonly IReadOnlyDictionary<string, int> PinnedClaims = new Dictionary<string, int>
    {
        ["cli/feature-check.mdx"] = 15,
        ["guides/csharp-interoperability.mdx"] = 1,
        ["syntax-reference/string-operations.mdx"] = 1,
    };

    internal sealed record Claim(string Page, int Line, string Feature, string Level, string Shape);

    private static readonly Regex Reports = new(@"`calor feature-check (?<f>[a-z0-9-]+)` reports \*\*(?<l>" + Levels + @")\*\*");
    private static readonly Regex IsLevel = new(@"`(?<f>[a-z0-9-]+)` is (?<l>" + Levels + @")\b");
    private static readonly Regex Parenthesized = new(@"`(?<f>[a-z0-9-]+)` \((?<l>" + Levels + @")\)");
    private static readonly Regex Strong = new(@"\*\*(?:" + Levels + @")\*\*|\((?:" + Levels + @")\)|`(?:" + Levels + @")`|\bis (?:" + Levels + @")\b");
    private static readonly Regex LevelCell = new(@"^(?:" + Levels + @")$");

    [Fact]
    public void EveryStatedSupportLevelMatchesTheRegistry()
    {
        var (claims, unattributed) = ScanWebsite();
        Assert.Empty(unattributed);
        Assert.Empty(Mismatches(claims));
    }

    [Fact]
    public void ClaimCoverageIsPinned()
    {
        var (claims, _) = ScanWebsite();
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
    [InlineData("cli/feature-check.mdx", "`iterator-accessor` (NotSupported)", "`iterator-accessor` (Partial)", "iterator-accessor")]
    [InlineData("cli/feature-check.mdx", "`yield-return` is Full", "`yield-return` is Partial", "yield-return")]
    [InlineData("cli/feature-check.mdx", "| `local-function` | Partial |", "| `local-functions` | Partial |", "local-functions")]
    [InlineData("cli/feature-check.mdx", "| `out-var` | Partial |", "| `out-var` | Mostly |", "out-var")]
    [InlineData("syntax-reference/string-operations.mdx", "reports **Partial**", "reports **Full**", "string-interpolation")]
    [InlineData("guides/csharp-interoperability.mdx", "reports **Partial**", "reports **NotSupported**", "string-interpolation")]
    public void AWrongSupportLevelOnAPageFailsTheCheck(string page, string from, string to, string feature)
    {
        var text = File.ReadAllText(PagePath(page));
        Assert.Contains(from, text);
        var (claims, unattributed) = Scan(page, text.Replace(from, to));
        Assert.Empty(unattributed);
        var failures = Mismatches(claims);
        Assert.Contains(failures, f => f.Contains($"`{feature}`", StringComparison.Ordinal));
        // The unmodified page passes, so the failure comes from the edit alone.
        Assert.Empty(Mismatches(Scan(page, text).Claims));
    }

    [Theory]
    [InlineData("syntax-reference/string-operations.mdx", "`calor feature-check string-interpolation` reports **Partial**",
        "`calor feature-check string-interpolation` is reported as **Full**")]
    [InlineData("cli/feature-check.mdx", "`iterator-accessor` (NotSupported)", "`iterator-accessor` (**Full**)")]
    public void ALevelInAnUnrecognizedShapeFailsTheCheck(string page, string from, string to)
    {
        var text = File.ReadAllText(PagePath(page));
        Assert.Contains(from, text);
        Assert.NotEmpty(Scan(page, text.Replace(from, to)).Unattributed);
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
            if (info is null)
                failures.Add($"{where}: `{claim.Feature}` is not a FeatureSupport registry name");
            else if (!Enum.TryParse<SupportLevel>(claim.Level, ignoreCase: false, out var level)
                     || !Enum.IsDefined(level) || level.ToString() != claim.Level)
                failures.Add($"{where}: `{claim.Feature}` states '{claim.Level}', which is not a support level");
            else if (level != info.Support)
                failures.Add($"{where}: `{claim.Feature}` is stated as {claim.Level}; the registry says {info.Support}");
        }
        return failures;
    }

    private static (List<Claim> Claims, List<string> Unattributed) ScanWebsite()
    {
        var content = Path.Combine(RepoRoot(), "website", "content");
        var claims = new List<Claim>();
        var unattributed = new List<string>();
        foreach (var path in Directory.GetFiles(content, "*.mdx", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
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
        var inFence = false;
        int? levelColumn = null;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }
            if (inFence)
                continue;

            var found = 0;
            var strongInCells = 0;
            if (line.TrimStart().StartsWith('|'))
            {
                var cells = Cells(line);
                var next = i + 1 < lines.Length ? lines[i + 1].Trim() : "";
                if (Regex.IsMatch(next, @"^\|(?:\s*:?-+:?\s*\|)+$"))
                {
                    var index = cells.FindIndex(c => c.Equals("Level", StringComparison.OrdinalIgnoreCase));
                    levelColumn = index >= 0 ? index : null;
                    continue;
                }
                if (Regex.IsMatch(line.Trim(), @"^\|(?:\s*:?-+:?\s*\|)+$"))
                    continue;
                if (levelColumn is int column && column < cells.Count)
                {
                    var feature = Regex.Match(cells[0], "`(?<f>[a-z0-9-]+)`");
                    if (feature.Success)
                    {
                        claims.Add(new Claim(page, i + 1, feature.Groups["f"].Value, cells[column], "table"));
                        found++;
                    }
                }
                strongInCells = cells.Count(c => LevelCell.IsMatch(c));
            }
            else
            {
                levelColumn = null;
            }

            foreach (var (regex, shape) in new[] { (Reports, "reports"), (IsLevel, "is"), (Parenthesized, "parenthesized") })
                foreach (Match m in regex.Matches(line))
                {
                    claims.Add(new Claim(page, i + 1, m.Groups["f"].Value, m.Groups["l"].Value, shape));
                    found++;
                }

            var strong = Strong.Matches(line).Count + strongInCells;
            if (strong > found && !NotClaims.Contains((page, line.Trim())))
                unattributed.Add($"{page}:{i + 1}: a support level in a form the parity check does not read: {line.Trim()}");
        }
        return (claims, unattributed);
    }

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
