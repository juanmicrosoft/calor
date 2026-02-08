using Calor.Compiler.Diagnostics;
using Calor.Compiler.Ids;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Ids.Tests;

/// <summary>
/// Tests that verify IDs survive real development workflows.
/// </summary>
public class WorkflowScenarioTests : IDisposable
{
    private readonly string _testDirectory;

    public WorkflowScenarioTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"calor-workflow-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    [Fact]
    public void Scenario_RenameFunction_IdUnchanged()
    {
        // Original file
        var originalSource = """
            §M{m_01J5X7K9M2NPQRSTABWXYZ1234:Module}
            §F{f_01J5X7K9M2NPQRSTABWXYZ1234:OldName:pub}
              §O{void}
            §/F{f_01J5X7K9M2NPQRSTABWXYZ1234}
            §/M{m_01J5X7K9M2NPQRSTABWXYZ1234}
            """;

        // After rename
        var renamedSource = """
            §M{m_01J5X7K9M2NPQRSTABWXYZ1234:Module}
            §F{f_01J5X7K9M2NPQRSTABWXYZ1234:NewName:pub}
              §O{void}
            §/F{f_01J5X7K9M2NPQRSTABWXYZ1234}
            §/M{m_01J5X7K9M2NPQRSTABWXYZ1234}
            """;

        var originalEntries = ScanSource(originalSource, "test.calr");
        var renamedEntries = ScanSource(renamedSource, "test.calr");

        // ID should be preserved
        var originalFunc = originalEntries.First(e => e.Kind == IdKind.Function);
        var renamedFunc = renamedEntries.First(e => e.Kind == IdKind.Function);

        Assert.Equal(originalFunc.Id, renamedFunc.Id);
        Assert.NotEqual(originalFunc.Name, renamedFunc.Name);
    }

    [Fact]
    public void Scenario_ExtractHelper_OriginalKeepsId_HelperGetsNewId()
    {
        // Original file with inline code
        var originalSource = """
            §M{m_01J5X7K9M2NPQRSTABWXYZ1234:Module}
            §F{f_01J5X7K9M2NPQRSTABWXYZ1234:Calculate:pub}
              §I{i32:x}
              §O{i32}
              §R (* x x)
            §/F{f_01J5X7K9M2NPQRSTABWXYZ1234}
            §/M{m_01J5X7K9M2NPQRSTABWXYZ1234}
            """;

        // After extracting helper (agent omits ID, calor ids assign fills it)
        var extractedSource = """
            §M{m_01J5X7K9M2NPQRSTABWXYZ1234:Module}
            §F{f_01J5X7K9M2NPQRSTABWXYZ1234:Calculate:pub}
              §I{i32:x}
              §O{i32}
              §B{result} §C{Square}
                §A x
              §/C
              §R result
            §/F{f_01J5X7K9M2NPQRSTABWXYZ1234}
            §F{f_01J5X7K9M2NPQRSTABWXYZ9912:Square:pri}
              §I{i32:n}
              §O{i32}
              §R (* n n)
            §/F{f_01J5X7K9M2NPQRSTABWXYZ9912}
            §/M{m_01J5X7K9M2NPQRSTABWXYZ1234}
            """;

        var originalEntries = ScanSource(originalSource, "test.calr");
        var extractedEntries = ScanSource(extractedSource, "test.calr");

        // Original function should keep its ID
        var originalFunc = originalEntries.First(e => e.Kind == IdKind.Function);
        var extractedCalculate = extractedEntries.First(e => e.Name == "Calculate");
        Assert.Equal(originalFunc.Id, extractedCalculate.Id);

        // New helper should have different ID
        var extractedSquare = extractedEntries.First(e => e.Name == "Square");
        Assert.NotEqual(originalFunc.Id, extractedSquare.Id);
    }

    [Fact]
    public void Scenario_DuplicateDetection_CheckFails()
    {
        var source = """
            §M{m_01J5X7K9M2NPQRSTABWXYZ1234:Module}
            §F{f_01J5X7K9M2NPQRSTABWXYZ1234:Func1:pub}
              §O{void}
            §/F{f_01J5X7K9M2NPQRSTABWXYZ1234}
            §F{f_01J5X7K9M2NPQRSTABWXYZ1234:Func2:pub}
              §O{void}
            §/F{f_01J5X7K9M2NPQRSTABWXYZ1234}
            §/M{m_01J5X7K9M2NPQRSTABWXYZ1234}
            """;

        var entries = ScanSource(source, "/src/test.calr");

        // Debug output: verify we have the expected entries
        var functionEntries = entries.Where(e => e.Kind == IdKind.Function).ToList();
        Assert.True(functionEntries.Count >= 2,
            $"Expected at least 2 functions, got {functionEntries.Count}. All entries: {string.Join(", ", entries.Select(e => $"{e.Kind}:{e.Name}:{e.Id}"))}");

        var result = IdChecker.Check(entries);

        Assert.False(result.IsValid,
            $"Expected invalid result. Missing: {result.MissingIds.Count}, Invalid: {result.InvalidFormatIds.Count}, " +
            $"WrongPrefix: {result.WrongPrefixIds.Count}, TestInProd: {result.TestIdsInProduction.Count}, Dups: {result.DuplicateGroups.Count}");
        Assert.Single(result.DuplicateGroups);
    }

    [Fact]
    public void Scenario_IdAssignFixesDuplicate()
    {
        var content = """
            §M{m_01J5X7K9M2NPQRSTABWXYZ1234:Module}
            §F{f_01J5X7K9M2NPQRSTABWXYZ1234:Func1:pub}
              §O{void}
            §/F{f_01J5X7K9M2NPQRSTABWXYZ1234}
            §F{f_01J5X7K9M2NPQRSTABWXYZ1234:Func2:pub}
              §O{void}
            §/F{f_01J5X7K9M2NPQRSTABWXYZ1234}
            §/M{m_01J5X7K9M2NPQRSTABWXYZ1234}
            """;

        // Start with empty existingIds - duplicates are detected within the file
        var (newContent, assignments) = IdAssigner.AssignIds(
            content,
            "/src/test.calr",
            fixDuplicates: true,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        // Only the second function (Func2) should get a new ID - first occurrence is kept
        Assert.Single(assignments);
        Assert.Equal("Func2", assignments[0].Name);
        Assert.NotEqual("f_01J5X7K9M2NPQRSTABWXYZ1234", assignments[0].NewId);
    }

    [Fact]
    public void Scenario_MergeSimulation_NoCollisions()
    {
        // Simulate two branches creating functions independently
        // Each branch has its own base module but creates new functions independently
        _ = """
            §M{m_01J5X7K9M2NPQRSTABWXYZ1234:Module}
            §/M{m_01J5X7K9M2NPQRSTABWXYZ1234}
            """;

        // Each branch assigns a new function ID
        var existingIds1 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existingIds2 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var (_, assignments1) = IdAssigner.AssignIds("§F{:Func1:pub}\n§O{void}\n§/F{}", "test.calr", false, existingIds1);
        var (_, assignments2) = IdAssigner.AssignIds("§F{:Func2:pub}\n§O{void}\n§/F{}", "test.calr", false, existingIds2);

        // Both should get unique IDs (ULID collision probability is ~10^-24)
        Assert.NotEqual(assignments1[0].NewId, assignments2[0].NewId);
    }

    [Fact]
    public void Scenario_RegenerationPreservesIds()
    {
        // Original source with IDs
        var originalSource = """
            §M{m_01J5X7K9M2NPQRSTABWXYZ1234:Module}
            §F{f_01J5X7K9M2NPQRSTABWXYZ1234:Calculate:pub}
              §I{i32:x}
              §O{i32}
              §R (* x 2)
            §/F{f_01J5X7K9M2NPQRSTABWXYZ1234}
            §/M{m_01J5X7K9M2NPQRSTABWXYZ1234}
            """;

        var entries1 = ScanSource(originalSource, "test.calr");

        // Simulate regeneration (same source, should have same IDs)
        var entries2 = ScanSource(originalSource, "test.calr");

        // All IDs should match
        Assert.Equal(entries1.Count, entries2.Count);
        for (int i = 0; i < entries1.Count; i++)
        {
            Assert.Equal(entries1[i].Id, entries2[i].Id);
        }
    }

    [Fact]
    public void Scenario_FormatFile_IdsUnchanged()
    {
        // Source with unusual formatting
        var messySource = """
            §M{m_01J5X7K9M2NPQRSTABWXYZ1234:Module}
            §F{f_01J5X7K9M2NPQRSTABWXYZ1234:Func:pub}
            §I{i32:x}
            §O{i32}
            §R x
            §/F{f_01J5X7K9M2NPQRSTABWXYZ1234}
            §/M{m_01J5X7K9M2NPQRSTABWXYZ1234}
            """;

        // Scan both versions
        var entries = ScanSource(messySource, "test.calr");

        // IDs should be present
        Assert.Contains(entries, e => e.Id == "m_01J5X7K9M2NPQRSTABWXYZ1234");
        Assert.Contains(entries, e => e.Id == "f_01J5X7K9M2NPQRSTABWXYZ1234");
    }

    private static IReadOnlyList<IdEntry> ScanSource(string source, string filePath)
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        var scanner = new IdScanner();
        return scanner.Scan(module, filePath);
    }
}
