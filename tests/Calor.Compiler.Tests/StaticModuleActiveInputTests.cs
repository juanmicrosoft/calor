using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xunit;

namespace Calor.Compiler.Tests;

public class StaticModuleActiveInputTests
{
    [Fact]
    public void StructuredMirror_ChangesOnlyTheTwoModuleSourceRows()
    {
        const string original = "tests/TestData/InteropScope/D1/refout-cases.json";
        var root = CliTestHarness.FindRepoRoot();
        var expected = JsonNode.Parse(File.ReadAllText(Path.Combine(root, original)))!;
        var actual = JsonNode.Parse(File.ReadAllText(ActiveSyntaxInputs.ForCurrentTypedInput(
            root, original, ActiveSyntaxInputs.InputKind.Structured)))!;
        MigrateLines(expected["preludes"]!["cls"]!.AsArray());
        var changedCases = 0;
        foreach (var row in expected["cases"]!.AsArray())
        {
            if (row!["kind"]!.GetValue<string>() != "calor")
                continue;
            var source = row["source"]!.AsArray();
            if (source.Any(line => line!.GetValue<string>().Contains("§CL{K:pub:stat}", StringComparison.Ordinal)))
            {
                Assert.Equal("D1-NULL-10", row["id"]!.GetValue<string>());
                MigrateLines(source);
                changedCases++;
            }
        }
        Assert.Equal(1, changedCases);
        Assert.True(JsonNode.DeepEquals(expected, actual),
            "Only the shared cls prelude and D1-NULL-10 source may change; all other rows, observations, proofs and mutants stay exact.");
    }

    [Fact]
    public void TranscriptMirrors_ChangeOnlyModuleFunctionSpellingsInEveryCell()
    {
        var root = CliTestHarness.FindRepoRoot();
        var entries = ActiveSyntaxInputs.TypedRegistrations
            .Where(entry => entry.Value == ActiveSyntaxInputs.InputKind.Transcript
                && !entry.Key.Contains("W1-F6-", StringComparison.Ordinal)).ToArray();
        Assert.Equal(7, entries.Length);
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
                    var migrated = ModuleSpelling(text);
                    if (migrated != text)
                        changedCells++;
                    cell[field] = migrated;
                }
            }
            Assert.Equal(3, changedCells);
            Assert.True(JsonNode.DeepEquals(expected, actual),
                $"{original}: all surface/cell fields must stay equal except approved Calor module/function spellings.");
        }
    }

    [Fact]
    public void PrivatePropertyCorrection_AccountsForEveryChangedTranscriptField()
    {
        var (expected, actual) = Transcripts("W1-F6-rescue");
        var native = expected["mcp-convert"]!["calorSource"]!.GetValue<string>();
        Assert.Contains("§PROP{Count:i32:priv:stat}", native);
        Assert.DoesNotContain("§CSHARP", native);
        Assert.Contains("§CSHARP", expected["cli-convert"]!["calor"]!.GetValue<string>());
        expected["cli-convert"]!["calor"] = native.Replace("m001:ConvertedModule:", "m001:Counter:", StringComparison.Ordinal);
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
            "Private-property repair may change only the explicitly attributed native/rescue/output fields.");
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
                var issues = file["issues"]!.AsArray();
                for (var index = issues.Count - 1; index >= 0; index--)
                {
                    if (issues[index]!.GetValue<string>() == "error:Calor1343:roundtrip-csharp-validation")
                        issues.RemoveAt(index);
                }
            }
        }
        Assert.True(JsonNode.DeepEquals(expected, actual),
            "Only successful project classification/write counts and the resolved compile error may change; warnings stay exact.");
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
    private static void MigrateLines(JsonArray lines)
    {
        for (var index = 0; index < lines.Count; index++)
            lines[index] = ModuleSpelling(lines[index]!.GetValue<string>());
    }

    private static string ModuleSpelling(string source)
    {
        source = Regex.Replace(source, @"§CL\{([^}\r\n]+):stat\}", "§M{$1}");
        return Regex.Replace(source, @"§MT\{([^}\r\n]+):stat\}", "§F{$1}");
    }
}
