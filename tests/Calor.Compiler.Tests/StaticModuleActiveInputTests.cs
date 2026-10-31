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
                    if (migrated != text)
                        changedCells++;
                    cell[field] = migrated;
                }
            }
            Assert.Equal(3, changedCells);
            Change(expected["cli-migrate"]!["conversionPaths"]!, "roundTrip", "deferred", "verified");
            Assert.True(JsonNode.DeepEquals(expected, actual),
                $"{original}: only source IDs, module/function spellings, the exact W1-F3 Clamp lambda projection and completed project validation may change.");
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
            "Private-property repair and completed project validation may change only the explicitly attributed fields.");
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
