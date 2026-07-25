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
        §M{m001:MathModule}
          §F{f001:add:pub}
            §I{i32:x, i32:y}
            §O{i32}
            §R (+ x y)
          §F{f002:multiply:pub}
            §I{i32:a, i32:b}
            §O{i32}
            §R (* a b)
        """;

    private const string MathSourceWithoutAdd = """
        §M{m001:MathModule}
          §F{f002:multiply:pub}
            §I{i32:a, i32:b}
            §O{i32}
            §R (* a b)
        """;

    /// <summary>First function is intact; the second has a broken body.</summary>
    private const string PartiallyBrokenSource = """
        §M{m010:PartModule}
          §F{f040:good:pub}
            §I{i32:x}
            §O{i32}
            §R (+ x INT:1)
          §F{f041:broken:pub}
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

    private static JsonElement Payload(McpToolResult result)
    {
        Assert.False(result.IsError, $"Expected success result, got error: {result.Content[0].Text}");
        return JsonDocument.Parse(result.Content[0].Text!).RootElement;
    }

    // ── ParseTolerant unit behavior ─────────────────────────────────────

    [Fact]
    public void ParseTolerant_ValidSource_IsPlainSuccess()
    {
        var result = CalorSourceHelper.ParseTolerant(MathSource, "math.calr");

        Assert.True(result.IsSuccess);
        Assert.False(result.IsPartial);
        Assert.NotNull(result.Ast);
    }

    [Fact]
    public void ParseTolerant_BrokenFunction_ReturnsPartialAstWithIntactDeclarations()
    {
        var result = CalorSourceHelper.ParseTolerant(PartiallyBrokenSource, "part.calr");

        Assert.False(result.IsSuccess);
        Assert.True(result.IsPartial);
        Assert.NotNull(result.Ast);
        Assert.NotEmpty(result.Errors);
        // The intact declaration must survive recovery.
        Assert.Contains(result.Ast!.Functions, f => f.Name == "good");
    }

    [Fact]
    public void ParseTolerant_EnvelopeDiagnostics_CarryEnclosingDeclarationId()
    {
        var result = CalorSourceHelper.ParseTolerant(PartiallyBrokenSource, "part.calr");
        Assert.True(result.IsPartial);

        var diagnostics = result.ToEnvelopeDiagnostics();

        Assert.NotEmpty(diagnostics);
        // The parse error sits inside f041's body; span containment against
        // the partial AST must attribute it there.
        Assert.Contains(diagnostics, d => d.DeclarationId == "f041");
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

    // ── Write path integration ──────────────────────────────────────────

    [Fact]
    public async Task FileWrite_RejectDiagnostics_IncludeDeclarationId()
    {
        var path = WriteFile("part.calr", MathSource);

        var payload = Payload(await WriteTool
            .ExecuteAsync(Args(new { path, content = PartiallyBrokenSource, heal = false })));

        Assert.False(payload.GetProperty("applied").GetBoolean());
        Assert.Equal("breaking", payload.GetProperty("verdict").GetString());
        var errors = payload.GetProperty("compilationResult").GetProperty("errors").EnumerateArray().ToList();
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e =>
            e.TryGetProperty("declarationId", out var id) && id.GetString() == "f041");
    }

    [Fact]
    public async Task FileWrite_BrokenNeighborWithRealCall_StillVetoesRemoval()
    {
        // The neighbor has a parse error in a later function, but its intact
        // earlier function really calls add — the partial AST must carry the
        // call so the removal is still rejected.
        var mathPath = WriteFile("math.calr", MathSource);
        WriteFile("broken-caller.calr", """
            §M{m011:BrokenCaller}
              §F{f050:usesAdd:pub}
                §I{i32:x}
                §O{i32}
                §B{total:i32} §C{add} §A x §A INT:1 §/C
                §R total
              §F{f051:broken:pub}
                §I{i32:y}
                §O{i32}
                §R (+ y
            """);
        var sessionId = Payload(await OpenTool.ExecuteAsync(Args(new { directory = _root })))
            .GetProperty("sessionId").GetString()!;

        var payload = Payload(await WriteTool
            .ExecuteAsync(Args(new { path = mathPath, content = MathSourceWithoutAdd, sessionId })));

        Assert.False(payload.GetProperty("applied").GetBoolean());
        Assert.Equal("breaking", payload.GetProperty("verdict").GetString());
        var dangling = payload.GetProperty("referenceIntegrity").GetProperty("danglingReferences")
            .EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains(dangling, d => d!.Contains("'add'") && d.Contains("broken-caller.calr") && d.Contains("parse errors"));
    }

    [Fact]
    public async Task FileWrite_BrokenNeighborWithStringMentionOnly_DoesNotBlock()
    {
        // A broken neighbor that only mentions the removed function inside a
        // string literal must not veto: with a partial AST available, the
        // check uses call targets, not raw text.
        var mathPath = WriteFile("math.calr", MathSource);
        WriteFile("broken-prose.calr", """
            §M{m012:BrokenProse}
              §F{f060:label:pub}
                §O{str}
                §R STR:"mentions add in prose only"
              §F{f061:broken:pub}
                §I{i32:y}
                §O{i32}
                §R (+ y
            """);
        var sessionId = Payload(await OpenTool.ExecuteAsync(Args(new { directory = _root })))
            .GetProperty("sessionId").GetString()!;

        var payload = Payload(await WriteTool
            .ExecuteAsync(Args(new { path = mathPath, content = MathSourceWithoutAdd, sessionId })));

        Assert.True(payload.GetProperty("applied").GetBoolean());
    }

    [Fact]
    public async Task SessionOpen_BrokenFile_DiagnosticsCarryDeclarationId()
    {
        WriteFile("part.calr", PartiallyBrokenSource);

        var payload = Payload(await OpenTool.ExecuteAsync(Args(new { directory = _root })));

        Assert.Equal(1, payload.GetProperty("parseErrorFileCount").GetInt32());
        var diagnostics = payload.GetProperty("diagnostics").EnumerateArray().ToList();
        Assert.NotEmpty(diagnostics);
        Assert.Contains(diagnostics, d =>
            d.TryGetProperty("declarationId", out var id) && id.GetString() == "f041");
    }
}
