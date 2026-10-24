using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Calor.Compiler.Migration;
using Calor.Compiler.SelfCheck;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.Yaml;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// #1585, #1664: the website states converter support levels only through the
/// <c>&lt;SupportLevel feature="…" /&gt;</c> MDX component, which renders the level from
/// <c>website/public/data/feature-support.json</c>, the verbatim output of
/// <c>calor feature-check --list</c>. Prose cannot state a level by hand.
/// <para>The scan reads every published page (<c>website/content/**/*.md</c> and <c>*.mdx</c>,
/// the changelog excepted as a historical record) as a Markdig syntax tree, not as lines of text.
/// A statement is a paragraph, heading, list-item paragraph, table row, fenced or indented code
/// block, front matter, or the Markdown inside an HTML/JSX block. Its text is the concatenation of
/// its literal, code-span and entity inlines; emphasis, links, code-span backticks and line breaks
/// are structure, so <c>(*full*)</c>, <c>(**`full`**)</c> and a claim wrapped across two lines read
/// the same as <c>full</c>. Inline HTML is read twice, once removed and once as a word break.</para>
/// <para>Rule: a statement is next to a feature when it, the statement before it in its section, or
/// any heading that encloses it names a registry feature (in a code span, as a hyphenated name, as
/// the plain words of a multi-word name such as "string interpolation" or "partial properties", or
/// as a single-word name such as "records") or mentions <c>feature-check</c>/<c>supportLevel</c>.
/// A single-word name that is also a common word ("interfaces", "methods"; "if"/"for" only with a
/// noun such as "statement" or "loop") counts in its own statement only. Such a
/// statement may not contain a support-level word in any case (full, fully, partial, partially,
/// partly, notsupported, unsupported, manualrequired, "not supported", "no support", "manual
/// intervention", "manual required"), except inside an allowlisted passage (<see cref="NotClaims"/>),
/// which must occur exactly once on its page. Text is NFKC-normalized and invisible format
/// characters are removed first; footnotes, link titles, reference definitions and JSX attributes
/// (entities decoded) are read. The text between a prose component and the one before it must name
/// that component's feature and no other; a table component must name its row's first cell. Component counts per page are pinned.
/// The level vocabulary is fixed: a synonym ("complete support") is not detected.</para>
/// </summary>
public sealed class WebsiteFeatureSupportParityTests
{
    /// <summary>
    /// Known gaps between the registry and converter behavior, as listed on the feature-check page
    /// under "Known gaps between the registry and ... behavior". The page renders the registry
    /// level for each one with the component and qualifies it. This list and the page's list must
    /// match exactly, so neither can drift from the other.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> KnownGaps = new Dictionary<string, string>
    {
        ["yield-return"] = "The registry says Full, which holds for iterator methods only. A `yield` in a property " +
                           "or indexer getter is the separate `iterator-accessor` entry (NotSupported) and stays C#.",
    };

    /// <summary>Passages whose level words state no support level for a feature: (page, Markdown
    /// source of the passage, reason). The passage is read with the same parser as the page, and
    /// only its own words are excused, not the rest of its statement. Every entry must be used.</summary>
    internal static readonly (string Page, string Passage, string Why)[] NotClaims =
    [
        ("cli/convert.mdx", "claiming a fully successful\nconversion", "a successful conversion, not a level"),
        ("cli/convert.mdx", "Calor has no partial members either", "C#'s `partial` modifier, not a level"),
        ("cli/convert.mdx", "C# 13 `partial`\nproperty", "C#'s `partial` modifier, not a level"),
        ("cli/feature-check.mdx", "calor feature-check --list --level partial",
            "an example invocation of the --level option, not a level for a feature"),
        ("cli/feature-check.mdx", "`--level` filters the registry to `full`, `partial`, `notsupported`, or\n`manualrequired`.",
            "lists the --level option's values"),
        ("cli/feature-check.mdx", "Conversion can still preserve an unsupported construct as an interop\nblock.",
            "'unsupported construct' is any construct the converter keeps as C#, not a registry level"),
        ("cli/mcp.mdx", "whether the result is fully native", "an all-native conversion outcome, not a level"),
        ("cli/review-packet.mdx", "`timeout`, `unsupported`, and `unavailable`", "proof statuses, not converter levels"),
        ("guides/csharp-interoperability.mdx", "partial properties",
            "'partial properties' is the C# construct (partial-property), not a support level"),
        ("guides/csharp-interoperability.mdx", "output is not fully native Calor", "the converter's own outcome text (a pinned W1 transcript)"),
        ("guides/csharp-interoperability.mdx", "(unsupported construct)", "the converter's own preservation-path text (a pinned W1 transcript)"),
        ("guides/csharp-interoperability.mdx", "Unsupported interpolation holes",
            "names the holes outside the native subset; the row's level is the registry's, on the feature-check page"),
        ("syntax-reference/calls.mdx", "the converter's full list", "the complete list, not a level"),
        ("syntax-reference/effects.mdx", "Effect rows do not yet fully verify callbacks", "effect verification, not a converter level"),
        ("cli/convert.mdx", "Partly converted beats nothing converted", "a partly converted file, not a level"),
        ("cli/mcp.mdx", "Partial results look like this:", "results a tool could not complete, not a level"),
        ("guides/adding-calor-to-existing-projects.mdx", "waives the full effect guarantee", "the whole effect guarantee, not a level"),
        ("guides/csharp-interoperability.mdx", "The full list of supported\nshapes", "the complete list, not a level"),
        ("syntax-reference/structure-tags.mdx", "inline parameter declarations are NOT supported inside\n`§LAM` headers",
            "Calor's own lambda syntax, not a converter level"),
        // Proof statuses and Calor's own modifiers next to a common word such as "fields" or "class".
        ("guides/adding-calor-to-existing-projects.mdx", "Read any `PARTIAL` residual", "an incomplete query answer, not a level"),
        ("guides/dependent-types-tutorial.mdx", "is reported `Unsupported`", "a proof status, not a converter level"),
        ("guides/verification-guarantees.mdx", "are `unsupported`, not\n  guessed", "a proof status, not a converter level"),
        ("philosophy/static-verification.mdx", "forms outside the positive whitelist report\n`unsupported`", "a proof status, not a converter level"),
        ("syntax-reference/refinement-types.mdx", "so a proof about one is\nreported `Unsupported`", "a proof status, not a converter level"),
        ("syntax-reference/inheritance.mdx", "`partial` | Partial class - split across files", "Calor's `partial` modifier, not a level"),
        ("syntax-reference/inheritance.mdx", "static class modifier not fully implemented", "a Calor modifier, not a converter level"),
        ("syntax-reference/inheritance.mdx", "Static and partial class modifiers are not fully implemented",
            "Calor's modifiers, not a converter level"),
        ("syntax-reference/structure-tags.mdx", "accept the full set", "the whole set of accessibility levels, not a level"),
    ];

    /// <summary>SupportLevel components per page. A changed count means one was added or removed.</summary>
    private static readonly IReadOnlyDictionary<string, int> PinnedComponents = new Dictionary<string, int>
    {
        ["cli/convert.mdx"] = 1,
        ["cli/feature-check.mdx"] = 15,
        ["guides/csharp-interoperability.mdx"] = 1,
        ["syntax-reference/string-operations.mdx"] = 1,
    };

    internal const string ExportPath = "website/public/data/feature-support.json";

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables().UseYamlFrontMatter().UseEmphasisExtras().UseFootnotes().Build();

    // The component's exact grammar (a tag, not prose). Anything else starting <SupportLevel fails.
    private static readonly Regex ComponentTag = new(@"^<SupportLevel feature=""(?<f>[a-z0-9-]+)""(?<w> form=""warning"")? />$");

    private static readonly HashSet<string> LevelWords = new(StringComparer.Ordinal)
    {
        "full", "fully", "partial", "partially", "partly", "notsupported", "unsupported", "manualrequired",
    };

    private static readonly (string, string)[] LevelPairs =
    [
        ("not", "supported"), ("no", "support"), ("manual", "intervention"), ("manual", "required"), ("manually", "required"),
    ];

    internal sealed record Component(string Page, int Line, string Tag, string? Feature, bool Warning, bool Anchored);

    internal sealed record Finding(string Page, int Line, string Message)
    {
        public override string ToString() => $"{Page}:{Line}: {Message}";
    }

    internal sealed record ScanResult(List<Finding> Findings, List<Component> Components, HashSet<int> UsedExcuses);

    [Fact]
    public void NoPageStatesASupportLevelOutsideTheComponent()
    {
        var result = ScanDirectory(ContentRoot());
        Assert.True(result.Findings.Count == 0, string.Join(Environment.NewLine, result.Findings));
    }

    [Fact]
    public void EveryAllowlistedPassageIsStillOnItsPage()
    {
        var result = ScanDirectory(ContentRoot());
        var unused = NotClaims.Where((_, i) => !result.UsedExcuses.Contains(i)).Select(n => $"{n.Page}: {n.Passage}").ToList();
        Assert.True(unused.Count == 0, string.Join(Environment.NewLine, unused));
    }

    [Fact]
    public void ComponentCoverageIsPinned()
    {
        var result = ScanDirectory(ContentRoot());
        var counts = result.Components.GroupBy(c => c.Page).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(PinnedComponents.OrderBy(p => p.Key, StringComparer.Ordinal),
            counts.OrderBy(p => p.Key, StringComparer.Ordinal));
        // The feature-check table is the bulk of the levels; each row renders its own feature's level.
        var page = Read("cli/feature-check.mdx");
        Assert.Equal(13, Regex.Matches(page, @"(?m)^\| `(?<f>[a-z0-9-]+)` \| <SupportLevel feature=""\k<f>"" /> \|").Count);
    }

    [Fact]
    public void TheWebsiteExportIsTheRegistry()
    {
        var root = RepoRoot();
        var work = Path.Combine(Path.GetTempPath(), "calor-feature-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var (exit, stdout, stderr) = CliTestHarness.RunCli(work, "feature-check", "--list");
            Assert.True(exit == 0, stderr);
            var committed = File.ReadAllText(Path.Combine(root, ExportPath)).Replace("\r\n", "\n");
            Assert.True(ExportMatches(committed, stdout),
                $"{ExportPath} differs from `calor feature-check --list`; regenerate it with that command.");
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
        // Every registry entry is in the export at its registry level, and nothing else is.
        var levels = ExportLevels(File.ReadAllText(Path.Combine(root, ExportPath)));
        Assert.Equal(FeatureSupport.GetAllFeatures().ToDictionary(f => f.Name, f => f.Support.ToString().ToLowerInvariant())
            .OrderBy(p => p.Key, StringComparer.Ordinal), levels.OrderBy(p => p.Key, StringComparer.Ordinal));
    }

    [Fact]
    public void AnEditedExportFailsTheComparison()
    {
        var committed = File.ReadAllText(Path.Combine(RepoRoot(), ExportPath)).Replace("\r\n", "\n");
        Assert.True(ExportMatches(committed, committed));
        var moved = committed.Replace("\"name\": \"in-parameter\"", "\"name\": \"in-parameterx\"");
        Assert.NotEqual(committed, moved);
        Assert.False(ExportMatches(moved, committed));
    }

    [Fact]
    public void KnownGapsListMatchesTheFeatureCheckPage()
    {
        var text = Read("cli/feature-check.mdx");
        var gaps = ListedGaps(text);
        Assert.Equal(KnownGaps.Keys.Order(StringComparer.Ordinal), gaps.Keys.Order(StringComparer.Ordinal));
        foreach (var (feature, reason) in KnownGaps)
        {
            Assert.NotNull(FeatureSupport.GetFeatureInfo(feature));
            Assert.False(string.IsNullOrWhiteSpace(reason));
            // The gap's bullet renders the feature's registry level; it never states one by hand.
            Assert.Contains($"<SupportLevel feature=\"{feature}\" />", gaps[feature]);
        }
    }

    [Fact]
    public void AKnownGapAddedToThePageWithoutAReasonFails()
    {
        var text = Read("cli/feature-check.mdx");
        var anchor = "- `yield-return` is <SupportLevel feature=\"yield-return\" />";
        Assert.Contains(anchor, text);
        var edited = text.Replace(anchor, "- `record` is <SupportLevel feature=\"record\" /> even for positional records.\n" + anchor);
        Assert.NotEqual(KnownGaps.Keys.Order(StringComparer.Ordinal), ListedGaps(edited).Keys.Order(StringComparer.Ordinal));
    }

    // Negative controls: a level stated by hand, in any shape a review of #1637 found or this parser
    // could plausibly miss, must fail on a real page.
    private const string Heading = "### Counts, Partial Results, and the JSON Envelope\n";

    [Theory]
    // A hand-written level in the table instead of the component.
    [InlineData("cli/feature-check.mdx", "| `in-parameter` | <SupportLevel feature=\"in-parameter\" /> |", "| `in-parameter` | Full |")]
    [InlineData("cli/feature-check.mdx", "| `iterator-accessor` | <SupportLevel feature=\"iterator-accessor\" /> |", "| `iterator-accessor` | **NotSupported** |")]
    // #1637 round 1-4 bypasses: parentheses, other case, emphasis and code inside parentheses.
    [InlineData("cli/feature-check.mdx", "`iterator-accessor` (<SupportLevel feature=\"iterator-accessor\" />)", "`iterator-accessor` (NotSupported)")]
    [InlineData("cli/feature-check.mdx", "`iterator-accessor` (<SupportLevel feature=\"iterator-accessor\" />)", "`iterator-accessor` (**Full**)")]
    [InlineData("cli/convert.mdx", Heading, "Declarations are kept: `partial-property` (full).\n\n" + Heading)]
    [InlineData("cli/convert.mdx", Heading, "Declarations are kept: `partial-property` (FULL).\n\n" + Heading)]
    [InlineData("cli/convert.mdx", Heading, "Declarations are kept: `partial-property` (`full`).\n\n" + Heading)]
    [InlineData("cli/convert.mdx", Heading, "Declarations are kept: `partial-property` ( **partial** ).\n\n" + Heading)]
    [InlineData("cli/convert.mdx", Heading, "Declarations are kept: `partial-property` (*full*).\n\n" + Heading)]
    [InlineData("cli/convert.mdx", Heading, "Declarations are kept: `partial-property` (_full_).\n\n" + Heading)]
    [InlineData("cli/convert.mdx", Heading, "Declarations are kept: `partial-property` (__full__).\n\n" + Heading)]
    [InlineData("cli/convert.mdx", Heading, "Declarations are kept: `partial-property` (**`full`**).\n\n" + Heading)]
    [InlineData("cli/convert.mdx", Heading, "Declarations are kept: `partial-property` (~~partial~~ full).\n\n" + Heading)]
    // Reworded or predicative claims.
    [InlineData("syntax-reference/string-operations.mdx", "reports **<SupportLevel feature=\"string-interpolation\" />**", "is reported as **Full**")]
    [InlineData("guides/csharp-interoperability.mdx", "reports **<SupportLevel feature=\"string-interpolation\" />**", "reports **NotSupported**")]
    [InlineData("cli/feature-check.mdx", "`iterator-accessor` (<SupportLevel feature=\"iterator-accessor\" />).", "`iterator-accessor` (<SupportLevel feature=\"iterator-accessor\" />). `iterator-accessor` has Full support.")]
    [InlineData("cli/feature-check.mdx", "`iterator-accessor` (<SupportLevel feature=\"iterator-accessor\" />).", "`iterator-accessor` (<SupportLevel feature=\"iterator-accessor\" />). `partial-property` is fully supported.")]
    [InlineData("cli/feature-check.mdx", "`iterator-accessor` (<SupportLevel feature=\"iterator-accessor\" />).", "`iterator-accessor` (<SupportLevel feature=\"iterator-accessor\" />). `iterator-accessor` is full.")]
    [InlineData("cli/feature-check.mdx", "`iterator-accessor` (<SupportLevel feature=\"iterator-accessor\" />).", "`iterator-accessor` (<SupportLevel feature=\"iterator-accessor\" />). `iterator-accessor` is FULL.")]
    [InlineData("cli/feature-check.mdx", "`iterator-accessor` (<SupportLevel feature=\"iterator-accessor\" />).", "`iterator-accessor` (<SupportLevel feature=\"iterator-accessor\" />). iterator-accessor is unsupported.")]
    [InlineData("cli/feature-check.mdx", "`iterator-accessor` (<SupportLevel feature=\"iterator-accessor\" />).", "`iterator-accessor` (<SupportLevel feature=\"iterator-accessor\" />). [out-var] needs manual intervention.")]
    // #1637 round 3: a claim wrapped across a soft line break is one statement.
    [InlineData("cli/convert.mdx", Heading, "The converter handles declarations: `partial-property` is\nfully supported.\n\n" + Heading)]
    // The plain words of a multi-word feature name, with no code span.
    [InlineData("cli/convert.mdx", Heading, "String interpolation is fully supported.\n\n" + Heading)]
    // A converter warning reworded by hand instead of the component's warning form.
    [InlineData("cli/convert.mdx", "<SupportLevel feature=\"out-var\" form=\"warning\" />", "`[out-var] Feature 'out-var' is partially supported…`")]
    [InlineData("cli/convert.mdx", "<SupportLevel feature=\"out-var\" form=\"warning\" />", "`[out-var] out-var is Full…`")]
    // Words split by structure: emphasis, inline HTML, an entity.
    [InlineData("cli/convert.mdx", Heading, "Declarations are kept: `partial-property` is F**ull**.\n\n" + Heading)]
    [InlineData("cli/convert.mdx", Heading, "Declarations are kept: `partial-property` is Fu<span></span>ll.\n\n" + Heading)]
    [InlineData("cli/convert.mdx", Heading, "Declarations are kept: `partial-property` is &#70;ull.\n\n" + Heading)]
    [InlineData("cli/convert.mdx", Heading, "Declarations are kept: `partial-property` is<br/>Full.\n\n" + Heading)]
    // Other block shapes: heading, quote, nested list, JSX block, front matter, table without a Level column.
    [InlineData("cli/convert.mdx", Heading, "### `partial-property` is Full\n\n" + Heading)]
    [InlineData("cli/convert.mdx", Heading, "> `partial-property` is Full.\n\n" + Heading)]
    [InlineData("cli/convert.mdx", Heading, "- outer\n  - `partial-property` is Full.\n\n" + Heading)]
    [InlineData("cli/convert.mdx", Heading, "<Callout type=\"note\">\n`partial-property` is Full.\n</Callout>\n\n" + Heading)]
    [InlineData("cli/convert.mdx", Heading, "<Callout type=\"note\" title=\"`partial-property` is Full\">\nText.\n</Callout>\n\n" + Heading)]
    [InlineData("cli/convert.mdx", Heading, "| Construct | Status |\n|:--|:--|\n| `partial-property` | Full |\n\n" + Heading)]
    [InlineData("cli/convert.mdx", "title: \"convert\"\n", "title: \"convert\"\ndescription: \"`out-var` is Full\"\n")]
    // Codex round 1 (#1664): a plain single-word feature name, context from the heading or the paragraph
    // before, footnotes, link titles, reference definitions, encoded JSX attributes, invisible and
    // fullwidth characters.
    [InlineData("cli/convert.mdx", Heading, "Records are fully supported.\n\n" + Heading)]
    [InlineData("cli/convert.mdx", Heading, "Feature 'record' is fully supported.\n\n" + Heading)]
    [InlineData("cli/convert.mdx", Heading, "### `partial-property`\n\nFully supported.\n\n" + Heading)]
    [InlineData("cli/convert.mdx", Heading, "The converter keeps `partial-property` declarations.\n\nThis feature has full support.\n\n" + Heading)]
    [InlineData("cli/convert.mdx", Heading, "Details[^status].\n\n[^status]: partial-property (Full)\n\n" + Heading)]
    [InlineData("cli/convert.mdx", Heading, "See [details](/docs/ \"partial-property is Full\").\n\n" + Heading)]
    [InlineData("cli/convert.mdx", Heading, "See [details][status].\n\n[status]: /docs/ \"partial-property is Full\"\n\n" + Heading)]
    [InlineData("cli/convert.mdx", Heading, "<Callout title=\"partial-property is &#70;ull\">\nText.\n</Callout>\n\n" + Heading)]
    [InlineData("cli/convert.mdx", Heading, "Declarations are kept: `partial-property` is Fu\u200Bll.\n\n" + Heading)]
    [InlineData("cli/convert.mdx", Heading, "Declarations are kept: `partial-property` is Fu&shy;ll.\n\n" + Heading)]
    [InlineData("cli/convert.mdx", Heading, "Declarations are kept: `partial-property` is \uFF26\uFF55\uFF4C\uFF4C.\n\n" + Heading)]
    [InlineData("cli/convert.mdx", "title: \"convert\"\n", "title: \"convert\"\ndescription: \"Records are fully supported\"\n")]
    // Codex round 2 (#1664): common-word and plural names, keyword nouns, and a nested heading.
    [InlineData("cli/convert.mdx", Heading, "Interfaces are fully supported.\n\n" + Heading)]
    [InlineData("cli/convert.mdx", Heading, "Partial properties are fully supported.\n\n" + Heading)]
    [InlineData("cli/convert.mdx", Heading, "For loops are partially supported.\n\n" + Heading)]
    [InlineData("cli/convert.mdx", Heading, "## `record`\n\n### Conversion status\n\nFully supported.\n\n" + Heading)]
    // Codex round 1: an allowlisted passage copied next to a new claim is not excused.
    [InlineData("guides/csharp-interoperability.mdx", "## Next steps\n", "`in-parameter` (unsupported construct).\n\n## Next steps\n")]
    [InlineData("guides/csharp-interoperability.mdx", "## Next steps\n",
        "Unsupported interpolation holes include all `string-interpolation-to-string` expressions.\n\n## Next steps\n")]
    // An MDX expression next to a feature name cannot be read, so it fails.
    [InlineData("cli/convert.mdx", Heading, "Declarations are kept: `partial-property` is {'Fu' + 'll'}.\n\n" + Heading)]
    // Fenced output of the command, and a text line inside a fence that names the command.
    [InlineData("cli/feature-check.mdx", "calor feature-check --list\n", "calor feature-check --list\nSupport level: Full\n")]
    [InlineData("cli/feature-check.mdx", "## Interop Features in 0.25\n",
        "```json\n{\n  \"data\": {\n    \"feature\": \"iterator-accessor\",\n    \"supportLevel\": \"notsupported\"\n  }\n}\n```\n\n## Interop Features in 0.25\n")]
    // An addition next to an allowlisted passage is not excused by it.
    [InlineData("cli/feature-check.mdx", "Conversion can still preserve an unsupported construct as an interop\nblock.",
        "Conversion can still preserve an unsupported construct as an interop\nblock; `partial-property` is fully supported.")]
    [InlineData("cli/feature-check.mdx", "calor feature-check --list --level partial\n", "calor feature-check --list --level partial\n# out-var: partial\n")]
    // A level word next to a feature that the page used to state without one.
    [InlineData("cli/feature-check.mdx", "`Partial` means some shapes", "`partial-property` is `Partial`; that means some shapes")]
    public void AHandWrittenLevelFailsTheCheck(string page, string from, string to)
    {
        var text = Read(page);
        Assert.Contains(from, text);
        Assert.Empty(Scan(page, text).Findings);
        Assert.NotEmpty(Scan(page, text.Replace(from, to)).Findings);
    }

    [Theory]
    // The component must name a registry feature, in its exact grammar, that its statement names.
    [InlineData("cli/feature-check.mdx", "| `ref-parameter` | <SupportLevel feature=\"ref-parameter\" /> |", "| `ref-parameter` | <SupportLevel feature=\"in-parameter\" /> |", "names `in-parameter`")]
    [InlineData("cli/feature-check.mdx", "| `local-function` | <SupportLevel feature=\"local-function\" /> |", "| `local-functions` | <SupportLevel feature=\"local-functions\" /> |", "not a FeatureSupport registry name")]
    [InlineData("cli/feature-check.mdx", "<SupportLevel feature=\"yield-return\" />", "<SupportLevel feature='yield-return' />", "not the component's form")]
    [InlineData("cli/feature-check.mdx", "<SupportLevel feature=\"yield-return\" />", "<SupportLevel feature=\"yield-return\" form=\"short\" />", "not the component's form")]
    [InlineData("cli/convert.mdx", "<SupportLevel feature=\"out-var\" form=\"warning\" />", "<SupportLevel feature=\"in-parameter\" form=\"warning\" />", "a Full feature has no converter warning")]
    [InlineData("syntax-reference/string-operations.mdx", "`calor feature-check string-interpolation` reports **<SupportLevel feature=\"string-interpolation\" />**",
        "The command reports **<SupportLevel feature=\"string-interpolation\" />**", "names `string-interpolation`")]
    [InlineData("cli/feature-check.mdx", "- `yield-return` is <SupportLevel feature=\"yield-return\" />",
        "- `record` is <SupportLevel feature=\"in-parameter\" /> (compare `in-parameter`). `yield-return` is <SupportLevel feature=\"yield-return\" />",
        "names `in-parameter`, which is not the one feature named just before it")]
    [InlineData("cli/feature-check.mdx", "- `yield-return` is <SupportLevel feature=\"yield-return\" /> for iterator *methods* only.",
        "- `yield-return` is <SupportLevel feature=\"yield-return\" /> for iterator *methods* only. Records are <SupportLevel feature=\"yield-return\" />.",
        "names `yield-return`, which is not the one feature named just before it")]
    public void AMisusedComponentFailsTheCheck(string page, string from, string to, string message)
    {
        var text = Read(page);
        Assert.Contains(from, text);
        Assert.Empty(Scan(page, text).Findings);
        Assert.Contains(Scan(page, text.Replace(from, to)).Findings, f => f.Message.Contains(message, StringComparison.Ordinal));
    }

    [Fact]
    public void AnMdxCommentRendersNothingAndStatesNothing()
    {
        var text = Read("cli/convert.mdx");
        Assert.Contains(Heading, text);
        Assert.Empty(Scan("cli/convert.mdx", text.Replace(Heading, "{/* `partial-property` is Full */}\n\n" + Heading)).Findings);
        // In code the same characters are shown, not hidden, and are read.
        Assert.NotEmpty(Scan("cli/convert.mdx", text.Replace(Heading, "`{/* partial-property is Full */}`\n\n" + Heading)).Findings);
    }

    [Theory]
    [InlineData("guides/new-page.md")]
    [InlineData("guides/new-page.mdx")]
    public void ANewPageWithAHandWrittenLevelFailsTheCheck(string page)
    {
        var root = Path.Combine(Path.GetTempPath(), "calor-parity-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "guides"));
            File.WriteAllText(Path.Combine(root, page), "# New\n\n`calor feature-check record` reports **Full**.\n");
            // The changelog is a historical record and is not read.
            File.WriteAllText(Path.Combine(root, "changelog.mdx"), "`record` is Full.\n");
            var finding = Assert.Single(ScanDirectory(root).Findings);
            Assert.Equal(page, finding.Page);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    internal static bool ExportMatches(string committed, string generated) =>
        committed.Replace("\r\n", "\n") == generated.Replace("\r\n", "\n");

    internal static Dictionary<string, string> ExportLevels(string json)
    {
        var levels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (level, features) in JsonNode.Parse(json)!["data"]!["features"]!.AsObject())
            foreach (var feature in features!.AsArray())
                levels.Add((string)feature!["name"]!, level);
        return levels;
    }

    private static ScanResult ScanDirectory(string content)
    {
        var all = new ScanResult([], [], []);
        var pages = Directory.GetFiles(content, "*.*", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(".md", StringComparison.Ordinal) || p.EndsWith(".mdx", StringComparison.Ordinal));
        foreach (var path in pages.Order(StringComparer.Ordinal))
        {
            var page = Path.GetRelativePath(content, path).Replace('\\', '/');
            // Historical release records (the changelog) state the levels of past versions.
            if (WebsiteExampleChecker.HistoricalExclusions.ContainsKey(page))
                continue;
            var result = Scan(page, File.ReadAllText(path));
            all.Findings.AddRange(result.Findings);
            all.Components.AddRange(result.Components);
            all.UsedExcuses.UnionWith(result.UsedExcuses);
        }
        return all;
    }

    internal static ScanResult Scan(string page, string text)
    {
        var result = new ScanResult([], [], []);
        var source = WithoutMdxComments(text.Replace("\r\n", "\n"));
        var statements = new List<Statement>();
        Walk(Markdown.Parse(source, Pipeline), 0, statements, new int[1]);
        CheckStatements(page, statements, result);
        foreach (var component in result.Components)
        {
            if (component.Feature is null)
                result.Findings.Add(new(page, component.Line, $"{component.Tag} is not the component's form <SupportLevel feature=\"name\" /> " +
                                                              "or <SupportLevel feature=\"name\" form=\"warning\" />"));
            else if (FeatureSupport.GetFeatureInfo(component.Feature) is not { } info)
                result.Findings.Add(new(page, component.Line, $"{component.Tag}: `{component.Feature}` is not a FeatureSupport registry name"));
            else if (component.Warning && info.Support == SupportLevel.Full)
                result.Findings.Add(new(page, component.Line, $"{component.Tag}: a Full feature has no converter warning"));
            else if (!component.Anchored)
                result.Findings.Add(new(page, component.Line, $"{component.Tag} names `{component.Feature}`, which is not the one feature named " +
                                                              "just before it (in a table row: the row's first cell)"));
        }
        return result;
    }

    /// <summary>One statement: a paragraph, heading, table row, code block, front matter, link
    /// reference definition, or a paragraph inside a JSX block or footnote. Section counts headings.</summary>
    internal sealed record Statement(int Line, Text Text, bool Raw, bool Heading, int Section, HashSet<string>? RowAnchor, int Level = 0);

    /// <summary>Collects every statement in document order. lineOffset maps a re-parsed HTML block back to page lines.</summary>
    private static void Walk(ContainerBlock container, int lineOffset, List<Statement> statements, int[] section)
    {
        foreach (var block in container)
        {
            var line = block.Line + 1 + lineOffset;
            switch (block)
            {
                case Table table:
                    foreach (var row in table.OfType<TableRow>())
                    {
                        var cells = row.OfType<TableCell>().Select(c => Render(c.Descendants<LeafBlock>())).ToList();
                        statements.Add(new(row.Line + 1 + lineOffset, Text.Join(cells, " | "), false, false, section[0],
                            cells.Count > 0 ? Mentions(cells[0]) : []));
                    }
                    break;
                case YamlFrontMatterBlock front:
                    statements.Add(new(line, Text.Raw(front.Lines.ToString()), true, false, section[0], null));
                    break;
                case HtmlBlock html:
                    // MDX JSX blocks (<Callout>…</Callout>) hold Markdown that Markdig keeps raw. A leading
                    // zero-width space makes the same text parse as a paragraph with inline HTML and
                    // Markdown, so a level inside a JSX block or attribute is read like any other.
                    Walk(Markdown.Parse("​" + html.Lines.ToString(), Pipeline), line - 1, statements, section);
                    break;
                case CodeBlock code: // fenced and indented
                    statements.Add(new(line, Text.Raw(code.Lines.ToString()), true, false, section[0], null));
                    break;
                case LinkReferenceDefinition definition: // "[label]: url "title"" renders nothing but a link's title
                    statements.Add(new(line, Text.Raw(definition.Label + " " + definition.Title), true, false, section[0], null));
                    break;
                case HeadingBlock heading:
                    section[0]++;
                    statements.Add(new(line, Render([heading]), false, true, section[0], null, heading.Level));
                    break;
                case LeafBlock leaf when leaf.Inline is not null: // paragraphs
                    statements.Add(new(line, Render([leaf]), false, false, section[0], null));
                    break;
                case ContainerBlock nested: // lists, list items, quotes, footnotes, reference groups
                    Walk(nested, lineOffset, statements, section);
                    break;
            }
        }
    }

    private static void CheckStatements(string page, List<Statement> statements, ScanResult result)
    {
        // A common word that is also a registry name ("interfaces", "for loops") names that feature in
        // its own statement; only the other forms carry over to the statements around it.
        var own = statements.Select(s => Mentions(s.Text, commonWords: true).Count > 0 || NamesTheCommand(s.Text)).ToList();
        var context = statements.Select(s => Mentions(s.Text, commonWords: false).Count > 0 || NamesTheCommand(s.Text)).ToList();
        // The headings that enclose each statement: its section heading and every heading above it
        // with a smaller level, so "## `record`" then "### Status" still encloses "Fully supported."
        var enclosing = new List<List<int>>();
        var stack = new List<int>();
        for (var i = 0; i < statements.Count; i++)
        {
            if (statements[i].Heading)
            {
                stack.RemoveAll(j => statements[j].Level >= statements[i].Level);
                stack.Add(i);
            }
            enclosing.Add([.. stack]);
        }
        // A level is "next to" a feature when the feature is named in the same statement, in the
        // statement just before it in its section, or in any heading that encloses it.
        bool Near(int i) => own[i] || (i > 0 && context[i - 1] && statements[i - 1].Section == statements[i].Section)
                            || enclosing[i].Any(j => context[j]);
        var excused = ExcusesFor(page, statements, result);
        for (var i = 0; i < statements.Count; i++)
        {
            var statement = statements[i];
            foreach (var component in Components(page, statement))
                result.Components.Add(component);
            if (!Near(i) && !(statement.Raw && QuotesSingleWordFeature(statement.Text.Joined)))
                continue;
            if (statement.Text.Expression)
                result.Findings.Add(new(page, statement.Line,
                    $"an MDX expression next to a feature name, which the parity check cannot read: {statement.Text.Joined.Trim()}"));
            var terms = new[] { statement.Text.Joined, statement.Text.Spaced }
                .SelectMany(t => LevelTerms(Remove(Normalize(t), excused.GetValueOrDefault(i, [])))).Distinct().ToList();
            if (terms.Count > 0)
                result.Findings.Add(new(page, statement.Line,
                    $"states a support level ({string.Join(", ", terms)}) outside <SupportLevel>: {statement.Text.Joined.Trim()}"));
        }
    }

    /// <summary>Each allowlisted passage must occur exactly once on its page (in one statement); it is
    /// removed from that statement only. A second copy anywhere on the page fails, so a passage
    /// cannot be reused to excuse a new claim.</summary>
    private static Dictionary<int, List<string>> ExcusesFor(string page, List<Statement> statements, ScanResult result)
    {
        var excused = new Dictionary<int, List<string>>();
        for (var e = 0; e < NotClaims.Length; e++)
        {
            var (excusedPage, passage, _) = NotClaims[e];
            if (excusedPage != page)
                continue;
            var hits = new List<(int Statement, string Form)>();
            for (var i = 0; i < statements.Count; i++)
            {
                var forms = statements[i].Raw ? [Normalize(passage)] : PassageForms(passage);
                foreach (var form in forms.Distinct())
                {
                    var count = Occurrences(Normalize(statements[i].Raw ? statements[i].Text.Joined : statements[i].Text.Joined), form)
                                + (statements[i].Raw ? 0 : Occurrences(Normalize(statements[i].Text.Spaced), form));
                    if (count > 0)
                        hits.Add((i, form));
                }
            }
            var where = hits.Select(h => h.Statement).Distinct().ToList();
            var once = where.Count == 1 && OccursOnce(statements[where[0]], hits.Where(h => h.Statement == where[0]).Select(h => h.Form));
            if (where.Count == 0)
                continue; // reported as unused
            result.UsedExcuses.Add(e);
            if (!once)
            {
                result.Findings.Add(new(page, statements[where[0]].Line, $"the allowlisted passage \"{passage}\" appears more than once on the page"));
                continue;
            }
            excused.TryAdd(where[0], []);
            excused[where[0]].AddRange(hits.Select(h => h.Form));
        }
        return excused;
    }

    private static bool OccursOnce(Statement statement, IEnumerable<string> forms) =>
        forms.Distinct().All(f => Occurrences(Normalize(statement.Text.Joined), f) <= 1 && Occurrences(Normalize(statement.Text.Spaced), f) <= 1);

    private static string[] PassageForms(string passage)
    {
        var text = Render(Markdown.Parse(passage, Pipeline).Descendants<LeafBlock>());
        return [Normalize(text.Joined), Normalize(text.Spaced)];
    }

    private static int Occurrences(string text, string form)
    {
        var count = 0;
        for (var at = text.IndexOf(form, StringComparison.Ordinal); form.Length > 0 && at >= 0; at = text.IndexOf(form, at + form.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    private static string Remove(string text, List<string> forms)
    {
        foreach (var form in forms.Distinct())
            text = text.Replace(form, " \u0003 ", StringComparison.Ordinal);
        return text;
    }

    /// <summary>The statement's components. A prose component is anchored when the text since the
    /// previous component names its feature and no other (common words such as "methods" aside); a table component when its row's first
    /// cell names it. The warning form names its feature in its own text.</summary>
    private static IEnumerable<Component> Components(string page, Statement statement)
    {
        var placeholders = new List<int>();
        for (var at = statement.Text.Joined.IndexOf('\u0001'); at >= 0; at = statement.Text.Joined.IndexOf('\u0001', at + 1))
            placeholders.Add(at);
        for (var k = 0; k < statement.Text.Tags.Count; k++)
        {
            var tag = statement.Text.Tags[k].Tag;
            var match = ComponentTag.Match(tag);
            var feature = match.Success ? match.Groups["f"].Value : null;
            var warning = match.Success && match.Groups["w"].Success;
            bool anchored;
            if (feature is null)
                anchored = false;
            else if (warning)
                anchored = true;
            else if (statement.RowAnchor is not null)
                anchored = statement.RowAnchor.Contains(feature);
            else
            {
                // The text since the previous component (or the statement's start) must name this
                // feature and no other, with the same recognition the level scan uses.
                var from = k == 0 || k - 1 >= placeholders.Count ? 0 : placeholders[k - 1] + 1;
                var to = k < placeholders.Count ? placeholders[k] : statement.Text.Joined.Length;
                var segment = statement.Text.Joined[from..to];
                var codes = statement.Text.CodeSpans.Where(c => segment.Contains(c, StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
                var named = Mentions(new Text(segment, segment, [], codes, false), commonWords: false);
                anchored = named.Count == 1 && named.Contains(feature);
            }
            yield return new Component(page, statement.Line, tag, feature, warning, anchored);
        }
    }

    /// <summary>A statement's text, read two ways: inline HTML removed (so "Fu&lt;span&gt;&lt;/span&gt;ll" is
    /// "Full") and inline HTML as a word break with its tag text kept (so "is&lt;br/&gt;Full" is "is Full").
    /// Code spans contribute their content; emphasis, links and line breaks contribute nothing but
    /// their children.</summary>
    internal sealed record Text(string Joined, string Spaced, List<(int Line, string Tag)> Tags, HashSet<string> CodeSpans, bool Expression)
    {
        public static Text Join(List<Text> parts, string separator) => new(
            string.Join(separator, parts.Select(p => p.Joined)), string.Join(separator, parts.Select(p => p.Spaced)),
            parts.SelectMany(p => p.Tags).ToList(), parts.SelectMany(p => p.CodeSpans).ToHashSet(StringComparer.Ordinal),
            parts.Any(p => p.Expression));

        /// <summary>Code, front matter and reference definitions: their characters as written.</summary>
        public static Text Raw(string body) => new(Clean(body), Clean(body), [], [], false);
    }

    private static Text Render(IEnumerable<LeafBlock> leaves)
    {
        var joined = new StringBuilder();
        var spaced = new StringBuilder();
        var tags = new List<(int, string)>();
        var codes = new HashSet<string>(StringComparer.Ordinal);
        var literals = new StringBuilder();
        var expression = false;
        void Piece(string s) { s = Clean(s); joined.Append(s); spaced.Append(' ').Append(s); }
        void Visit(Inline inline)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    var s = literal.Content.ToString();
                    // MDX evaluates an unescaped brace as a JavaScript expression (checked below).
                    if (!(literal.IsFirstCharacterEscaped && s.Length == 1))
                        literals.Append(s);
                    Piece(s);
                    break;
                case CodeInline code:
                    codes.Add(code.Content);
                    Piece(code.Content);
                    break;
                case HtmlEntityInline entity:
                    Piece(entity.Transcoded.ToString());
                    break;
                case LineBreakInline:
                    joined.Append(' ');
                    spaced.Append(' ');
                    break;
                case HtmlInline html:
                    if (html.Tag.StartsWith("<SupportLevel", StringComparison.Ordinal))
                    {
                        tags.Add((html.Line + 1, html.Tag));
                        joined.Append(" \u0001 ");
                        spaced.Append(" \u0001 ");
                    }
                    else
                    {
                        // JSX attributes render as text (a Callout title): read them with entities decoded.
                        var decoded = Clean(WebUtility.HtmlDecode(html.Tag));
                        expression |= decoded.Contains('{') || decoded.Contains('}');
                        spaced.Append(' ').Append(decoded).Append(' ');
                    }
                    break;
                case AutolinkInline autolink:
                    Piece(autolink.Url);
                    break;
                case LinkInline link: // link text or image alt, then the title shown as a tooltip
                    foreach (var child in link)
                        Visit(child);
                    if (!string.IsNullOrEmpty(link.Title))
                        Piece(" " + link.Title + " ");
                    break;
                case ContainerInline children: // emphasis
                    foreach (var child in children)
                        Visit(child);
                    break;
            }
        }
        foreach (var leaf in leaves)
        {
            if (leaf.Inline is null)
                continue;
            foreach (var inline in leaf.Inline)
                Visit(inline);
            joined.Append(' ');
            spaced.Append(' ');
        }
        // Any brace outside code is an MDX expression (comments were removed from the source first),
        // which this check cannot read.
        var bare = literals.ToString();
        return new Text(joined.ToString(), spaced.ToString(), tags, codes, expression || bare.Contains('{') || bare.Contains('}'));
    }

    /// <summary>Compatibility normalization (fullwidth letters become ASCII) and removal of invisible
    /// format characters (zero-width space, soft hyphen, word joiner), so neither hides a word.</summary>
    private static string Clean(string text) =>
        string.Concat(text.Normalize(NormalizationForm.FormKC).Where(c => char.GetUnicodeCategory(c) != UnicodeCategory.Format));

    /// <summary>Blanks MDX comments (<c>{/* … */}</c>), which render nothing, outside code; line numbers are kept.</summary>
    private static string WithoutMdxComments(string source)
    {
        var document = Markdown.Parse(source, Pipeline);
        var code = document.Descendants<CodeBlock>().Select(c => (c.Span.Start, c.Span.End))
            .Concat(document.Descendants<CodeInline>().Select(c => (c.Span.Start, c.Span.End))).ToList();
        var chars = source.ToCharArray();
        for (var start = source.IndexOf("{/*", StringComparison.Ordinal); start >= 0; start = source.IndexOf("{/*", start + 1, StringComparison.Ordinal))
        {
            if (code.Any(c => start >= c.Start && start <= c.End))
                continue;
            var end = source.IndexOf("*/}", start + 3, StringComparison.Ordinal);
            if (end < 0)
                break;
            for (var i = start; i < end + 3; i++)
                if (chars[i] != '\n')
                    chars[i] = ' ';
            start = end;
        }
        return new string(chars);
    }

    /// <summary>The registry features a statement names: a code span equal to a registry name, a
    /// hyphenated registry name in the text, or the words of a multi-word registry name.</summary>
    private static HashSet<string> Mentions(Text text, bool commonWords = true)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var code in text.CodeSpans)
            foreach (var token in Tokens(code).Append(code))
                if (FeatureSupport.GetFeatureInfo(token) is not null)
                    found.Add(token);
        foreach (var rendered in new[] { text.Joined, text.Spaced })
        {
            foreach (var token in Tokens(rendered))
                if (token.Contains('-') && FeatureSupport.GetFeatureInfo(token) is not null)
                    found.Add(token);
            var words = Words(rendered, splitFeatures: true);
            for (var i = 0; i < words.Count; i++)
                foreach (var singular in Singulars(words[i]))
                {
                    if (SingleWordNames.Value.Contains(singular))
                        found.Add(singular);
                    else if (commonWords && CommonWords.Contains(singular))
                        found.Add(singular);
                    // "if statements", "for loops": a keyword names its feature only with its noun.
                    else if (commonWords && KeywordNouns.TryGetValue(singular, out var nouns) && i + 1 < words.Count
                             && Singulars(words[i + 1]).Any(nouns.Contains))
                        found.Add(singular);
                }
            foreach (var name in MultiWordNames.Value)
                for (var i = 0; i + name.Parts.Length <= words.Count; i++)
                    if (name.Parts.Select((p, k) => words[i + k] == p || (k == name.Parts.Length - 1 && Singulars(words[i + k]).Contains(p))).All(b => b))
                        found.Add(name.Name);
        }
        return found;
    }

    // Single-word registry names that are also ordinary English or general programming words. As plain
    // text they name their feature in their own statement ("Interfaces are fully supported") but do not
    // put the statements around them in scope ("the full method"); in a code span they always do.
    private static readonly HashSet<string> CommonWords = new(StringComparer.Ordinal)
    {
        "class", "method", "property", "field", "constructor", "attributes", "dynamic", "unsafe", "fixed",
        "volatile", "namespace", "parallel", "span", "memory", "interface", "generics",
    };

    // Keywords that are registry names but are also function words: plain text names the feature
    // only when the keyword is followed by one of its nouns.
    private static readonly Dictionary<string, string[]> KeywordNouns = new(StringComparer.Ordinal)
    {
        ["if"] = ["statement", "condition", "block"],
        ["for"] = ["loop", "statement"],
        ["foreach"] = ["loop", "statement"],
        ["while"] = ["loop", "statement"],
        ["switch"] = ["statement", "expression"],
    };

    private static readonly Lazy<HashSet<string>> SingleWordNames = new(() =>
        FeatureSupport.GetAllFeatures().Select(f => f.Name)
            .Where(n => n.All(char.IsLetterOrDigit) && !CommonWords.Contains(n) && !KeywordNouns.ContainsKey(n))
            .ToHashSet(StringComparer.Ordinal));

    /// <summary>A word and its possible singular forms: records, classes, properties.</summary>
    private static string[] Singulars(string word) =>
    [
        word, word.EndsWith('s') ? word[..^1] : word, word.EndsWith("es") ? word[..^2] : word,
        word.EndsWith("ies") ? word[..^3] + "y" : word,
    ];

    private static readonly Lazy<List<(string Name, string[] Parts)>> MultiWordNames = new(() =>
        FeatureSupport.GetAllFeatures().Select(f => f.Name).Where(n => n.Contains('-'))
            .Select(n => (n, n.Split('-'))).ToList());

    /// <summary>Maximal runs of letters, digits and hyphens.</summary>
    private static List<string> Tokens(string text)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        foreach (var c in text + " ")
        {
            if (char.IsLetterOrDigit(c) || c == '-')
                current.Append(c);
            else if (current.Length > 0)
            {
                var token = current.ToString().Trim('-');
                if (token.Length > 0)
                    tokens.Add(token);
                current.Clear();
            }
        }
        return tokens;
    }

    /// <summary>Lower-case words. A registry name stays one word (or, with splitFeatures, its parts);
    /// any other hyphenated token is split into its parts.</summary>
    private static List<string> Words(string text, bool splitFeatures)
    {
        var words = new List<string>();
        foreach (var token in Tokens(text))
        {
            if (token.Contains('-') && FeatureSupport.GetFeatureInfo(token) is not null && !splitFeatures)
                words.Add("\u0002feature");
            else
                words.AddRange(token.ToLowerInvariant().Split('-', StringSplitOptions.RemoveEmptyEntries));
        }
        return words;
    }

    private static List<string> LevelTerms(string text)
    {
        var words = Words(text, splitFeatures: false);
        var terms = words.Where(LevelWords.Contains).ToList();
        for (var i = 0; i + 1 < words.Count; i++)
            if (LevelPairs.Contains((words[i], words[i + 1])))
                terms.Add(words[i] + " " + words[i + 1]);
        return terms;
    }

    private static bool NamesTheCommand(Text text) => new[] { text.Joined, text.Spaced }.Any(t =>
        Tokens(t).Any(token => token == "feature-check" || token.Equals("supportLevel", StringComparison.OrdinalIgnoreCase)));

    // A quoted single-word registry name in code ("record", 'record'), as in the command's JSON output.
    private static bool QuotesSingleWordFeature(string body)
    {
        for (var open = body.IndexOfAny(['"', '\'']); open >= 0 && open + 1 < body.Length; open = body.IndexOfAny(['"', '\''], open + 1))
        {
            var close = body.IndexOf(body[open], open + 1);
            if (close > open + 1 && FeatureSupport.GetFeatureInfo(body[(open + 1)..close]) is not null)
                return true;
        }
        return false;
    }

    private static string Normalize(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Each bullet under the paragraph that starts "Known gaps between the registry": its first
    /// code span, mapped to the bullet's Markdown source.</summary>
    internal static Dictionary<string, string> ListedGaps(string text)
    {
        text = text.Replace("\r\n", "\n");
        var document = Markdown.Parse(text, Pipeline);
        var blocks = document.ToList();
        var start = blocks.FindIndex(b => b is ParagraphBlock p && p.Inline?.FirstChild is EmphasisInline e
                                          && Render([p]).Joined.TrimStart().StartsWith("Known gaps between the registry", StringComparison.Ordinal)
                                          && e.DelimiterCount == 2);
        Assert.True(start >= 0, "the feature-check page no longer has its known-gaps list");
        var list = blocks.Skip(start + 1).TakeWhile(b => b is not HeadingBlock).OfType<ListBlock>().FirstOrDefault();
        Assert.NotNull(list);
        var gaps = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in list.OfType<ListItemBlock>())
        {
            var code = item.Descendants<CodeInline>().FirstOrDefault();
            if (code is not null)
                gaps[code.Content] = text.Substring(item.Span.Start, item.Span.Length);
        }
        return gaps;
    }

    private static string ContentRoot() => Path.Combine(RepoRoot(), "website", "content");

    private static string Read(string page) => File.ReadAllText(Path.Combine(ContentRoot(), page)).Replace("\r\n", "\n");

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "website", "content")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory.FullName;
    }
}
