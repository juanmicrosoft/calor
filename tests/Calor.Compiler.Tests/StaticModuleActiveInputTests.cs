using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xunit;

namespace Calor.Compiler.Tests;

public class StaticModuleActiveInputTests
{
    [Fact]
    public void StructuredMirror_ChangesOnlyApprovedSourceIdsAndTwoModuleRows()
    {
        const string original = "tests/TestData/InteropScope/D1/refout-cases.json";
        var root = CliTestHarness.FindRepoRoot();
        var expected = JsonNode.Parse(File.ReadAllText(Path.Combine(root, original)))!;
        var actual = JsonNode.Parse(File.ReadAllText(ActiveSyntaxInputs.ForCurrentTypedInput(
            root, original, ActiveSyntaxInputs.InputKind.Structured)))!;
        foreach (var (name, prelude) in expected["preludes"]!.AsObject())
            ProjectLines(prelude!.AsArray(), moduleSpelling: name == "cls");
        var changedCases = 0;
        foreach (var row in expected["cases"]!.AsArray())
        {
            if (row!["kind"]!.GetValue<string>() != "calor")
                continue;
            var source = row["source"]!.AsArray();
            var moduleRow = source.Any(line =>
                line!.GetValue<string>().Contains("§CL{c1:K:pub:stat}", StringComparison.Ordinal));
            if (moduleRow)
            {
                Assert.Equal("D1-NULL-10", row["id"]!.GetValue<string>());
                changedCases++;
            }
            ProjectLines(source, moduleRow);
            if (row["warmup"] is JsonArray warmup)
                ProjectLines(warmup);
        }
        Assert.Equal(1, changedCases);
        Assert.True(JsonNode.DeepEquals(expected, actual),
            "Only source IDs and the cls/D1-NULL-10 module spellings may change; all non-source fields, observations, proofs and mutants stay exact.");
    }

    [Fact]
    public void TranscriptMirrors_ChangeOnlyApprovedCurrentSourceInEveryCell()
    {
        var root = CliTestHarness.FindRepoRoot();
        var entries = ActiveSyntaxInputs.TypedRegistrations
            .Where(entry => entry.Value == ActiveSyntaxInputs.InputKind.Transcript
                && !entry.Key.Contains("W1-F6-", StringComparison.Ordinal)).ToArray();
        Assert.Equal(8, entries.Length);
        foreach (var (original, kind) in entries)
        {
            var expected = JsonNode.Parse(File.ReadAllText(Path.Combine(root, original)))!;
            var actual = JsonNode.Parse(File.ReadAllText(ActiveSyntaxInputs.ForCurrentTypedInput(root, original, kind)))!;
            var changedCells = 0;
            foreach (var (_, cell) in expected.AsObject())
            {
                foreach (var field in new[] { "calor", "calorSource" })
                {
                    if (cell?[field] is not JsonValue value || !value.TryGetValue<string>(out var text))
                        continue;
                    var migrated = ModuleSpelling(IdFreeFrozenControl(text));
                    if (original.EndsWith("/W1-F3-local-functions.json", StringComparison.Ordinal))
                    {
                        const string local = "        §F{Clamp} (i32:v) -> i32\n          §R (? (< v 0) 0 v)\n";
                        Assert.Equal(1, migrated.Split(local, StringSplitOptions.None).Length - 1);
                        migrated = migrated.Replace(local,
                            "        §B{Clamp:System.Func<i32, i32>} §LAM{static:v:i32} (? (< v 0) 0 v) §/LAM\n",
                            StringComparison.Ordinal);
                    }
                    migrated = ProjectApprovedTypeSites(original, migrated);
                    if (migrated != text)
                        changedCells++;
                    cell[field] = migrated;
                }
            }
            Assert.Equal(3, changedCells);
            Change(expected["cli-migrate"]!["conversionPaths"]!, "roundTrip", "deferred", "verified");
            Assert.True(JsonNode.DeepEquals(expected, actual),
                $"{original}: only source IDs, module/function spellings, the exact W1-F3 Clamp lambda projection, attributed type sites and completed project validation may change.");
        }
    }

    [Fact]
    public void PrivatePropertyCorrection_AccountsForEveryChangedTranscriptField()
    {
        var (expected, actual) = Transcripts("W1-F6-rescue");
        var native = expected["mcp-convert"]!["calorSource"]!.GetValue<string>();
        Assert.Contains("§PROP{p002:Count:i32:priv:stat}", native);
        Assert.DoesNotContain("§CSHARP", native);
        Assert.Contains("§CSHARP", expected["cli-convert"]!["calor"]!.GetValue<string>());
        native = IdFreeFrozenControl(native);
        native = ProjectApprovedTypeSites("W1-F6-rescue.json", native);
        expected["mcp-convert"]!["calorSource"] = native;
        expected["cli-convert"]!["calor"] = native.Replace("§M{ConvertedModule:", "§M{Counter:", StringComparison.Ordinal);
        expected["cli-convert"]!["stdout"] = JsonNode.Parse("""["✓ Conversion successful","Output: <dir>/Counter.calr"]""");
        var json = expected["cli-convert-json"]!;
        Change(json, "interopPreservationCount", 1, 0);
        Change(json, "lossCount", 1, 0);
        Assert.Single(json["losses"]!.AsArray());
        Assert.Equal("round-trip-failure", json["losses"]![0]!["trigger"]!.GetValue<string>());
        json["losses"] = new JsonArray();
        NativePaths(json["conversionPaths"]!, "preserved", "rescue");
        var migrate = expected["cli-migrate"]!;
        Change(migrate, "exit", 1, 0);
        Change(migrate["conversionPaths"]!, "outcome", "refused", "native");
        Change(migrate["conversionPaths"]!, "roundTrip", "deferred", "verified");
        var mcp = expected["mcp-convert"]!;
        Change(mcp, "isError", true, false);
        Change(mcp, "success", false, true);
        Assert.Equal("error:Calor1343:roundtrip-csharp-validation",
            Assert.Single(mcp["issueCodes"]!.AsArray())!.GetValue<string>());
        mcp["issueCodes"] = new JsonArray();
        Change(mcp["paths"]!, "outcome", "refused", "native");
        Change(mcp["paths"]!, "roundTrip", "failed", "verified");
        var passthrough = expected["mcp-convert-passthroughOnError"]!;
        Change(passthrough, "interopPreservations", 1, 0);
        Assert.Equal("round-trip-failure", Assert.Single(passthrough["locations"]!.AsArray())!["trigger"]!.GetValue<string>());
        passthrough["locations"] = new JsonArray();
        NativePaths(passthrough["paths"]!, "preserved", "passthrough");
        Assert.Contains("§CSHARP", passthrough["calorSource"]!.GetValue<string>());
        passthrough["calorSource"] = native;
        Assert.True(JsonNode.DeepEquals(expected, actual),
            "Private-property repair, attributed type sites and completed project validation may change only the explicitly attributed fields.");
    }

    [Fact]
    public void PrivatePropertyProjectCorrection_PreservesWarningsAndAllOtherCells()
    {
        var (expected, actual) = Transcripts("W1-F6-project");
        Change(expected["cli-migrate"]!, "exit", 1, 0);
        Change(expected["cli-migrate"]!, "calrFilesWritten", 0, 2);
        Change(expected["mcp-batch"]!, "failedFiles", 2, 0);
        Change(expected["mcp-batch"]!, "isError", true, false);
        Change(expected["mcp-migrate"]!, "isError", true, false);
        foreach (var (_, surface) in expected.AsObject())
        {
            Assert.Equal(2, surface!["files"]!.AsArray().Count);
            foreach (var file in surface["files"]!.AsArray())
            {
                Change(file!["conversionPaths"]!, "outcome", "refused", "native");
                Change(file["conversionPaths"]!, "roundTrip", "deferred", "verified");
                var issues = file["issues"]!.AsArray();
                for (var index = issues.Count - 1; index >= 0; index--)
                {
                    if (issues[index]!.GetValue<string>() == "error:Calor1343:roundtrip-csharp-validation")
                        issues.RemoveAt(index);
                }
            }
        }
        Assert.True(JsonNode.DeepEquals(expected, actual),
            "Only successful project classification/write counts, completed validation and the resolved compile error may change; warnings stay exact.");
    }

    private static (JsonNode Expected, JsonNode Actual) Transcripts(string id)
    {
        var root = CliTestHarness.FindRepoRoot();
        var path = $"tests/TestData/InteropScope/W1/transcripts/{id}.json";
        return (JsonNode.Parse(File.ReadAllText(Path.Combine(root, path)))!,
            JsonNode.Parse(File.ReadAllText(ActiveSyntaxInputs.ForCurrentTypedInput(
                root, path, ActiveSyntaxInputs.InputKind.Transcript)))!);
    }

    [Fact]
    public void ApprovedTypeProjection_LeavesUnattributedTextExact()
    {
        Assert.Equal(8, ApprovedTypeSites.Count);
        Assert.Equal(32, ApprovedTypeSites.Values.Sum(sites => sites.Length));
        const string unrelated = "  §R \"i32 str f64 any ?str\"\n  §RAW\n  string str = \"i32\";\n  §/RAW\n";
        foreach (var (file, sites) in ApprovedTypeSites)
        {
            var source = "\n" + string.Join("\n", sites.Select(site => site.Before)) + "\n" + unrelated;
            var expected = "\n" + string.Join("\n", sites.Select(site => site.After)) + "\n" + unrelated;
            Assert.Equal(expected, ProjectApprovedTypeSites(file, source));
        }
        Assert.Equal(unrelated, ProjectApprovedTypeSites("W1-F1-ref-return-expression.json", unrelated));
    }

    [Theory]
    [InlineData("type")]
    [InlineData("scope")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    public void ApprovedTypeProjection_RejectsChangedOrUnaccountedSites(string mutation)
    {
        var sites = ApprovedTypeSites["W1-F1-notnullwhen.json"];
        var first = sites[0].Before;
        var source = "\n" + string.Join("\n", sites.Select(site => site.Before)) + "\n";
        var changed = mutation switch
        {
            "type" => source.Replace(first, first.Replace("i32", "i64", StringComparison.Ordinal), StringComparison.Ordinal),
            "scope" => source.Replace(first, " " + first, StringComparison.Ordinal),
            "missing" => source.Replace(first + "\n", "", StringComparison.Ordinal),
            "duplicate" => source + first + "\n",
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        Assert.NotEqual(source, changed);
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
            ProjectApprovedTypeSites("W1-F1-notnullwhen.json", changed));
    }

    private static string ProjectApprovedTypeSites(string original, string source)
    {
        if (!ApprovedTypeSites.TryGetValue(Path.GetFileName(original), out var sites))
            return source;
        foreach (var (before, after) in sites)
        {
            var exactLine = "\n" + before + "\n";
            Assert.Equal(1, source.Split(exactLine, StringSplitOptions.None).Length - 1);
            source = source.Replace(exactLine, "\n" + after + "\n", StringComparison.Ordinal);
        }
        return source;
    }

    // Finite source-line attribution, never a general alias normalizer or a
    // projection inferred from the active transcripts being checked.
    private static readonly Dictionary<string, (string Before, string After)[]> ApprovedTypeSites = new(StringComparer.Ordinal)
    {
        ["W1-F1-notnullwhen.json"] =
        [
            ("        §I{i32:id}", "        §I{int:id}"),
            ("        §I{?str:name:out}[@NotNullWhen(true)]", "        §I{string?:name:out}[@NotNullWhen(true)]")
        ],
        ["W1-F1-outvar-statement.json"] =
        [
            ("      §F{ParseOrZero:pub} (str:text) -> i32", "      §F{ParseOrZero:pub} (string:text) -> int"),
            ("        §B{~n:i32} `default`", "        §B{~n:int} `default`")
        ],
        ["W1-F1-refout.json"] =
        [
            ("      §F{ParseOr:pub} (str:text, i32:fallback) -> i32", "      §F{ParseOr:pub} (string:text, int:fallback) -> int"),
            ("        §B{~n:i32} `default`", "        §B{~n:int} `default`"),
            ("      §F{CountOf:pub} (Dict<str, i32>:stock, str:item) -> i32", "      §F{CountOf:pub} (Dict<string, int>:stock, string:item) -> int"),
            ("        §B{~count:i32} `default`", "        §B{~count:int} `default`"),
            ("      §F{IsNumber:pub} (str:text) -> bool", "      §F{IsNumber:pub} (string:text) -> bool"),
            ("      §F{Bump:pub} (i32:x:ref)", "      §F{Bump:pub} (int:x:ref)"),
            ("      §F{Twice:pub} (i32:x:in) -> i32", "      §F{Twice:pub} (int:x:in) -> int"),
            ("      §F{Run:pub} () -> i32", "      §F{Run:pub} () -> int"),
            ("        §B{~total:i32} 1", "        §B{~total:int} 1")
        ],
        ["W1-F2-interpolation.json"] =
        [
            ("      §F{Line:pub} (str:item, f64:amount) -> str", "      §F{Line:pub} (string:item, double:amount) -> string"),
            ("      §F{Kind:pub} (FormattableString:f) -> str", "      §F{Kind:pub} (FormattableString:f) -> string"),
            ("      §F{Kind:pub} (any:o) -> str", "      §F{Kind:pub} (object:o) -> string"),
            ("      §F{Pick:pub} (i32:x) -> str", "      §F{Pick:pub} (int:x) -> string"),
            ("      §F{Build:pub} (i32:x) -> str", "      §F{Build:pub} (int:x) -> string")
        ],
        ["W1-F3-local-functions.json"] =
        [
            ("      §F{Area:pub} (i32:w, i32:h) -> i32", "      §F{Area:pub} (int:w, int:h) -> int"),
            ("        §B{Clamp:System.Func<i32, i32>} §LAM{static:v:i32} (? (< v 0) 0 v) §/LAM",
             "        §B{Clamp:System.Func<int, int>} §LAM{static:v:int} (? (< v 0) 0 v) §/LAM")
        ],
        ["W1-F4-iterator-accessor.json"] =
        [
            ("      §FLD{i32:Start:pub} = 3", "      §FLD{int:Start:pub} = 3"),
            ("      §MT{First:pub} () -> i32", "      §MT{First:pub} () -> int")
        ],
        ["W1-F5-arrays.json"] =
        [
            ("      §F{Pick:pub} (bool:wide, i32:v) -> i32[,]", "      §F{Pick:pub} (bool:wide, int:v) -> int[,]"),
            ("        §R §IF wide → §ARR2D{i32} §ROW v (+ v 1) §/ARR2D §EL → §ARR2D{i32} §ROW v §/ARR2D",
             "        §R §IF wide → §ARR2D{int} §ROW v (+ v 1) §/ARR2D §EL → §ARR2D{int} §ROW v §/ARR2D"),
            ("      §F{Sum:pub} (i32[]:xs, i32:start) -> i32", "      §F{Sum:pub} (int[]:xs, int:start) -> int"),
            ("        §B{~s:i32} start", "        §B{~s:int} start"),
            ("      §F{Total:pub} (i32:v) -> i32", "      §F{Total:pub} (int:v) -> int"),
            ("        §R §C{Sum} §A §ARR{i32} v (* v 2) §/ARR §A §IDX2D §C{Pick} §A true §A v §/C 0 1 §/C",
             "        §R §C{Sum} §A §ARR{int} v (* v 2) §/ARR §A §IDX2D §C{Pick} §A true §A v §/C 0 1 §/C"),
            ("      §F{Mixed:pub} () -> i32", "      §F{Mixed:pub} () -> int")
        ],
        ["W1-F6-rescue.json"] =
        [
            ("      §FLD{i32:count:priv:stat}", "      §FLD{int:count:priv:stat}"),
            ("      §PROP{Count:i32:priv:stat}", "      §PROP{Count:int:priv:stat}"),
            ("      §MT{Next:pub} () -> i32", "      §MT{Next:pub} () -> int")
        ]
    };

    private static void NativePaths(JsonNode paths, string oldOutcome, string oldPath)
    {
        Change(paths, "outcome", oldOutcome, "native");
        Change(paths, oldPath, 1, 0);
        Assert.Equal(1, paths["triggers"]!["round-trip-failure"]!.GetValue<int>());
        Assert.Single(paths["triggers"]!.AsObject());
        paths["triggers"] = new JsonObject();
    }

    private static void Change<T>(JsonNode parent, string field, T before, T after)
    {
        Assert.Equal(before, parent[field]!.GetValue<T>());
        parent[field] = System.Text.Json.JsonSerializer.SerializeToNode(after);
    }
    private static void ProjectLines(JsonArray lines, bool moduleSpelling = false)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            var source = IdFreeFrozenControl(lines[index]!.GetValue<string>());
            lines[index] = moduleSpelling ? ModuleSpelling(source) : source;
        }
    }

    // Finite ID vocabulary of these frozen controls only. Never infer replacements
    // from an active mirror or rewrite bodies, bindings, observations or diagnostics.
    internal static string IdFreeFrozenControl(string source)
    {
        foreach (var (tag, ids) in new (string Tag, string Ids)[]
        {
            ("M", "m001 m1"),
            ("NS", "ns001 ns003 ns1 ns2 ns_global"),
            ("CL", "c001 c002 c003 c004 c005 c006 c008 c012 c014 c016 c1 c2"),
            ("F", "f0 f001 f002 f003 f007 f008 f009 f1 f2 f3 f6 f7 f8 f9"),
            ("MT", "m0 m001 m002 m003 m004 m005 m006 m007 m008 m009 m011 m013 m015 m017 m1 m2 m9 mt001 mt002 mt003 mt004 mt005 mt1"),
            ("CTOR", "ctor001 ctor009 k1"),
            ("DEL", "del003 del011"),
            ("IFACE", "i001"),
            ("IXER", "ix005"),
            ("PROP", "p001 p002 p003 p005 p010 p1 pr1"),
            ("EACH", "each003 each008 each009"),
            ("L", "for003 l1"),
            ("LAM", "lam018"),
            ("LIST", "list002 items"),
            ("ARR", "arrI32010")
        })
        {
            foreach (var id in ids.Split(' '))
                source = source.Replace($"§{tag}{{{id}:", $"§{tag}{{", StringComparison.Ordinal);
        }
        foreach (var id in new[] { "i1", "if003", "if004", "if005", "if006", "if007", "if008", "if010", "if1", "tern000" })
            source = source.Replace($"§IF{{{id}}}", "§IF", StringComparison.Ordinal);
        return source
            .Replace("§PROOF{pr1}", "§PROOF{}", StringComparison.Ordinal)
            .Replace("§ARR2D{arr2d003:arr2d003:i32}", "§ARR2D{i32}", StringComparison.Ordinal)
            .Replace("§ARR2D{arr2d004:two:i32}", "§ARR2D{i32}", StringComparison.Ordinal)
            .Replace("§ARR2D{arr2d005:arr2d005:i32}", "§ARR2D{i32}", StringComparison.Ordinal)
            .Replace("§/ARR2D{arr2d003}", "§/ARR2D", StringComparison.Ordinal)
            .Replace("§/ARR2D{arr2d005}", "§/ARR2D", StringComparison.Ordinal)
            .Replace("§/ARR{arrI32010}", "§/ARR", StringComparison.Ordinal)
            .Replace("§/LIST{list002}", "§/LIST", StringComparison.Ordinal)
            .Replace("§/LIST{items}", "§/LIST", StringComparison.Ordinal)
            .Replace("§/LAM{lam018}", "§/LAM", StringComparison.Ordinal);
    }

    private static string ModuleSpelling(string source)
    {
        source = Regex.Replace(source, @"§CL\{([^}\r\n]+):stat\}", "§M{$1}");
        return Regex.Replace(source, @"§MT\{([^}\r\n]+):stat\}", "§F{$1}");
    }
}
