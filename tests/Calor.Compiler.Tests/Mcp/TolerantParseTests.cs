using System.Text.Json;
using Calor.Compiler.Mcp;
using Calor.Compiler.Mcp.Sessions;
using Calor.Compiler.Mcp.Tools;
using Xunit;

namespace Calor.Compiler.Tests.Mcp;

/// <summary>
/// Tests for the fault-tolerant parse mode (loop plan WS2 D2.5):
/// CalorSourceHelper.ParseTolerant returns a best-effort partial AST plus
/// diagnostics instead of discarding the tree on the first error, and the
/// write path uses it for declaration-id attribution and project-wide checks.
/// </summary>
public sealed class TolerantParseTests : IDisposable
{
    private readonly string _root;
    private readonly ProjectSessionManager _manager = new();

    private SessionOpenTool OpenTool => new(_manager, _root);
    private FileWriteTool WriteTool => new(_manager, _root);

    private const string MathSource = """
        §M{MathModule}
          §F{add:pub}
            §I{i32:x, i32:y}
            §O{i32}
            §R (+ x y)
          §F{multiply:pub}
            §I{i32:a, i32:b}
            §O{i32}
            §R (* a b)
        """;

    private const string MathSourceWithoutAdd = """
        §M{MathModule}
          §F{multiply:pub}
            §I{i32:a, i32:b}
            §O{i32}
            §R (* a b)
        """;

    /// <summary>First function is intact; the second has a broken body.</summary>
    private const string PartiallyBrokenSource = """
        §M{PartModule}
          §F{good:pub}
            §I{i32:x}
            §O{i32}
            §R (+ x INT:1)
          §F{broken:pub}
            §I{i32:y}
            §O{i32}
            §R (+ y
        """;

    public TolerantParseTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "calor-tolerant-parse-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);

    private (string Path, string Id) RegisterPart()
    {
        var path = WriteFile("part.calr", PartiallyBrokenSource + " INT:1)");
        var store = new Calor.Compiler.Ids.DeclarationIdentityStore(_root);
        var state = store.Sync([path]).Declarations;
        return (path, Assert.Single(state, declaration => declaration.Name == "broken").Id);
    }

    private static JsonElement Payload(McpToolResult result)
    {
        Assert.False(result.IsError, $"Expected success result, got error: {result.Content[0].Text}");
        return JsonDocument.Parse(result.Content[0].Text!).RootElement;
    }

    [Fact]
    public void ParseTolerant_SourceOnly_DoesNotInventPersistentIdentity()
    {
        var result = CalorSourceHelper.ParseTolerant(PartiallyBrokenSource, "part.calr");
        Assert.True(result.IsPartial);
        Assert.All(result.ToEnvelopeDiagnostics(), diagnostic => Assert.Null(diagnostic.DeclarationId));
    }

    [Fact]
    public void ParseTolerant_Garbage_DoesNotThrow()
    {
        var result = CalorSourceHelper.ParseTolerant(" not calor at all {{{ §", "junk.calr");

        Assert.False(result.IsSuccess);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void ParseTolerant_EmptySource_DoesNotThrow()
    {
        var result = CalorSourceHelper.ParseTolerant("", "empty.calr");

        // Either outcome is acceptable for empty input — what is not is a
        // silent failure with neither an AST nor an error.
        Assert.True(result.IsSuccess || result.Errors.Count > 0);
    }

    // ── Adversarial inputs: crash and hang resistance ───────────────────

    [Fact]
    public void ParseTolerant_DeeplyNestedExpression_ReportsDepthCapInsteadOfCrashing()
    {
        // 50k-deep nested prefix expression, well under the 512 KB source
        // cap. Without the parser's depth guard this is an uncatchable
        // StackOverflowException that aborts the whole process.
        var nested = string.Concat(Enumerable.Repeat("(+ ", 50_000))
                     + "INT:1 INT:2"
                     + new string(')', 50_000);
        var source = "module Deep Deep block\n" + "  func deep () → int\n" + "    !\n" + "    return " + nested + "\n";

        var result = CalorSourceHelper.ParseTolerant(source, "deep.calr");

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Contains("nesting exceeds the maximum depth"));
    }

    [Fact]
    public void StrictParse_DeeplyNestedExpression_FailsGracefully()
    {
        var nested = string.Concat(Enumerable.Repeat("(+ ", 50_000))
                     + "INT:1 INT:2"
                     + new string(')', 50_000);
        var source = "module Deep Deep block\n" + "  func deep () → int\n" + "    !\n" + "    return " + nested + "\n";

        var result = CalorSourceHelper.Parse(source, "deep-strict.calr");

        Assert.False(result.IsSuccess);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void ParseTolerant_DeeplyNestedStatements_DoesNotCrashProcess()
    {
        // Deep §IF nesting recurses through the statement parser, outside
        // the expression depth counter — the stack backstop must turn an
        // imminent overflow into a catchable failure, never a process abort.
        var builder = new System.Text.StringBuilder("module DeepIf DeepIf block\n  func deep () → void\n    !\n    return\n");
        var indent = "    ";
        for (var i = 0; i < 4000; i++)
        {
            builder.Append(indent).Append("§IF (== INT:1 INT:1)\n");
            indent += "  ";
        }
        builder.Append(indent).Append("§B{x:i32} INT:1\n");

        var result = CalorSourceHelper.ParseTolerant(builder.ToString(), "deep-if.calr");

        // Any non-crashing outcome is acceptable; the process surviving IS
        // the assertion.
        Assert.NotNull(result);
    }

    [Fact]
    public async Task SessionOpen_FifoNamedCalr_DoesNotHang()
    {
        if (OperatingSystem.IsWindows()) return; // no FIFOs on Windows

        WriteFile("math.calr", MathSource);
        var fifoPath = Path.Combine(_root, "pipe.calr");
        var mkfifo = System.Diagnostics.Process.Start("mkfifo", fifoPath)!;
        mkfifo.WaitForExit();
        Assert.Equal(0, mkfifo.ExitCode);

        // A blocking read on the FIFO would hang forever; the zero-length
        // stat gate must keep the open from ever happening.
        var result = await OpenTool.ExecuteAsync(Args(new { directory = _root }))
            .WaitAsync(TimeSpan.FromSeconds(30));
        var payload = Payload(result);

        Assert.Equal(2, payload.GetProperty("fileCount").GetInt32());
    }
}
