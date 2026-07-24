using System.Text.Json;
using Calor.Compiler.Mcp;
using Calor.Compiler.Mcp.Sessions;
using Calor.Compiler.Mcp.Tools;
using Xunit;

namespace Calor.Compiler.Tests.Mcp;

/// <summary>
/// Tests for the project-session tools (calor_session_open/close, loop plan
/// WS2 D2.1) and the transactional file write tool (calor_file_write, D2.4)
/// with auto-heal (D2.5).
/// </summary>
public sealed class SessionAndFileWriteToolTests : IDisposable
{
    private readonly string _root;
    private readonly ProjectSessionManager _manager = new();

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

    private const string OtherSource = """
        §M{m002:OtherModule}
          §F{f010:describe:pub}
            §O{str}
            §R STR:"standalone module"
        """;

    public SessionAndFileWriteToolTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "calor-mcp-session-tests", Guid.NewGuid().ToString("N"));
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

    private async Task<string> OpenSessionAsync()
    {
        var result = await new SessionOpenTool(_manager).ExecuteAsync(Args(new { directory = _root }));
        return Payload(result).GetProperty("sessionId").GetString()!;
    }

    // ── calor_session_open / calor_session_close ────────────────────────

    [Fact]
    public async Task SessionOpen_ParsesFilesAndReturnsSessionId()
    {
        WriteFile("math.calr", MathSource);
        WriteFile("other.calr", OtherSource);

        var result = await new SessionOpenTool(_manager).ExecuteAsync(Args(new { directory = _root }));
        var payload = Payload(result);

        Assert.True(payload.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrEmpty(payload.GetProperty("sessionId").GetString()));
        Assert.Equal(2, payload.GetProperty("fileCount").GetInt32());
        Assert.Equal(0, payload.GetProperty("parseErrorFileCount").GetInt32());
        Assert.Empty(payload.GetProperty("diagnostics").EnumerateArray());
    }

    [Fact]
    public async Task SessionOpen_ReportsParseErrorsAsEnvelopeDiagnostics()
    {
        WriteFile("broken.calr", "§M{m001:Broken}\n  §F{f001:bad:pub\n");

        var payload = Payload(await new SessionOpenTool(_manager).ExecuteAsync(Args(new { directory = _root })));

        Assert.Equal(1, payload.GetProperty("parseErrorFileCount").GetInt32());
        var diagnostics = payload.GetProperty("diagnostics").EnumerateArray().ToList();
        Assert.NotEmpty(diagnostics);
        Assert.Equal("error", diagnostics[0].GetProperty("severity").GetString());
    }

    [Fact]
    public async Task SessionOpen_MissingDirectory_ReturnsError()
    {
        var result = await new SessionOpenTool(_manager)
            .ExecuteAsync(Args(new { directory = Path.Combine(_root, "does-not-exist") }));

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task SessionClose_ClosesOnceThenReportsAlreadyClosed()
    {
        WriteFile("math.calr", MathSource);
        var sessionId = await OpenSessionAsync();
        var closeTool = new SessionCloseTool(_manager);

        var first = Payload(await closeTool.ExecuteAsync(Args(new { sessionId })));
        var second = Payload(await closeTool.ExecuteAsync(Args(new { sessionId })));

        Assert.True(first.GetProperty("closed").GetBoolean());
        Assert.False(second.GetProperty("closed").GetBoolean());
    }

    // ── calor_file_write: transactional apply ───────────────────────────

    [Fact]
    public async Task FileWrite_ValidEdit_AppliesAndUpdatesDisk()
    {
        var path = WriteFile("math.calr", MathSource);

        var payload = Payload(await new FileWriteTool(_manager)
            .ExecuteAsync(Args(new { path, content = MathSourceWithoutAdd })));

        Assert.True(payload.GetProperty("applied").GetBoolean());
        Assert.Equal("safe", payload.GetProperty("verdict").GetString());
        Assert.False(payload.GetProperty("healApplied").GetBoolean());
        Assert.Equal(MathSourceWithoutAdd, File.ReadAllText(path));
    }

    [Fact]
    public async Task FileWrite_ParseError_RejectsAndLeavesFileUntouched()
    {
        var path = WriteFile("math.calr", MathSource);

        var payload = Payload(await new FileWriteTool(_manager)
            .ExecuteAsync(Args(new { path, content = "§M{m001:Broken}\n  §F{f001:bad:pub\n" })));

        Assert.False(payload.GetProperty("applied").GetBoolean());
        Assert.Equal("breaking", payload.GetProperty("verdict").GetString());
        Assert.NotEmpty(payload.GetProperty("compilationResult").GetProperty("errors").EnumerateArray());
        Assert.Equal(MathSource, File.ReadAllText(path));
    }

    [Fact]
    public async Task FileWrite_CreatesMissingFile()
    {
        var path = Path.Combine(_root, "fresh.calr");

        var payload = Payload(await new FileWriteTool(_manager)
            .ExecuteAsync(Args(new { path, content = OtherSource })));

        Assert.True(payload.GetProperty("applied").GetBoolean());
        Assert.True(payload.GetProperty("created").GetBoolean());
        Assert.Equal(OtherSource, File.ReadAllText(path));
    }

    [Fact]
    public async Task FileWrite_NonCalrPath_ReturnsError()
    {
        var result = await new FileWriteTool(_manager)
            .ExecuteAsync(Args(new { path = Path.Combine(_root, "notes.txt"), content = "hello" }));

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task FileWrite_ContractRemoval_AppliesWithWarnings()
    {
        const string withContract = """
            §M{m001:MathModule}
              §F{f001:add:pub}
                §I{i32:x, i32:y}
                §O{i32}
                §Q (>= x INT:0)
                §R (+ x y)
            """;
        const string withoutContract = """
            §M{m001:MathModule}
              §F{f001:add:pub}
                §I{i32:x, i32:y}
                §O{i32}
                §R (+ x y)
            """;
        var path = WriteFile("math.calr", withContract);

        var payload = Payload(await new FileWriteTool(_manager)
            .ExecuteAsync(Args(new { path, content = withoutContract })));

        Assert.True(payload.GetProperty("applied").GetBoolean());
        Assert.Equal("safe_with_warnings", payload.GetProperty("verdict").GetString());
        Assert.NotEmpty(payload.GetProperty("contractVerification").GetProperty("issues").EnumerateArray());
    }

    // ── calor_file_write: auto-heal (D2.5) ──────────────────────────────

    [Fact]
    public async Task FileWrite_HealsForbiddenClosersBeforeChecking()
    {
        var path = WriteFile("math.calr", MathSource);
        // §/F and §/M are hard errors (Calor0830) — the healer must strip
        // them so the write survives instead of rejecting.
        const string withClosers = """
            §M{m001:MathModule}
              §F{f002:multiply:pub}
                §I{i32:a, i32:b}
                §O{i32}
                §R (* a b)
              §/F
            §/M
            """;

        var payload = Payload(await new FileWriteTool(_manager)
            .ExecuteAsync(Args(new { path, content = withClosers })));

        Assert.True(payload.GetProperty("applied").GetBoolean());
        Assert.True(payload.GetProperty("healApplied").GetBoolean());
        var written = payload.GetProperty("writtenContent").GetString()!;
        Assert.DoesNotContain("§/F", written);
        Assert.Equal(written, File.ReadAllText(path));
    }

    [Fact]
    public async Task FileWrite_HealDisabled_RejectsForbiddenClosers()
    {
        var path = WriteFile("math.calr", MathSource);
        const string withClosers = """
            §M{m001:MathModule}
              §F{f002:multiply:pub}
                §I{i32:a, i32:b}
                §O{i32}
                §R (* a b)
              §/F
            """;

        var payload = Payload(await new FileWriteTool(_manager)
            .ExecuteAsync(Args(new { path, content = withClosers, heal = false })));

        Assert.False(payload.GetProperty("applied").GetBoolean());
        Assert.Equal("breaking", payload.GetProperty("verdict").GetString());
        Assert.Equal(MathSource, File.ReadAllText(path));
    }

    // ── calor_file_write: session integration (D2.1) ────────────────────

    [Fact]
    public async Task FileWrite_UnknownSession_ReturnsError()
    {
        var path = WriteFile("math.calr", MathSource);

        var result = await new FileWriteTool(_manager)
            .ExecuteAsync(Args(new { path, content = MathSource, sessionId = "cs-nope" }));

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task FileWrite_PathOutsideSessionRoot_ReturnsError()
    {
        WriteFile("math.calr", MathSource);
        var sessionId = await OpenSessionAsync();
        var outside = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.calr");

        var result = await new FileWriteTool(_manager)
            .ExecuteAsync(Args(new { path = outside, content = MathSource, sessionId }));

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task FileWrite_RemovingSymbolReferencedByOtherFile_RejectsAsBreaking()
    {
        var mathPath = WriteFile("math.calr", MathSource);
        // The reference check uses symbol-id containment, matching the
        // in-file heuristic: this file mentions f001.
        WriteFile("caller.calr", """
            §M{m003:CallerModule}
              §F{f020:label:pub}
                §O{str}
                §R STR:"wraps f001"
            """);
        var sessionId = await OpenSessionAsync();

        var payload = Payload(await new FileWriteTool(_manager)
            .ExecuteAsync(Args(new { path = mathPath, content = MathSourceWithoutAdd, sessionId })));

        Assert.False(payload.GetProperty("applied").GetBoolean());
        Assert.Equal("breaking", payload.GetProperty("verdict").GetString());
        var dangling = payload.GetProperty("referenceIntegrity").GetProperty("danglingReferences")
            .EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains(dangling, d => d!.Contains("f001") && d.Contains("caller.calr"));
        Assert.Equal(MathSource, File.ReadAllText(mathPath));
    }

    [Fact]
    public async Task FileWrite_SessionSeesFilesChangedBehindItsBack()
    {
        var mathPath = WriteFile("math.calr", MathSource);
        var otherPath = WriteFile("other.calr", OtherSource);
        var sessionId = await OpenSessionAsync();

        // Change other.calr behind the session's back so it now references
        // f001. Dirty-state invalidation must pick this up on the next call.
        File.WriteAllText(otherPath, """
            §M{m002:OtherModule}
              §F{f010:describe:pub}
                §O{str}
                §R STR:"now mentions f001"
            """);
        // The stat gate is mtime/size — force a distinct mtime in case the
        // filesystem's timestamp granularity makes the two writes identical.
        File.SetLastWriteTimeUtc(otherPath, DateTime.UtcNow.AddSeconds(5));

        var payload = Payload(await new FileWriteTool(_manager)
            .ExecuteAsync(Args(new { path = mathPath, content = MathSourceWithoutAdd, sessionId })));

        Assert.False(payload.GetProperty("applied").GetBoolean());
        Assert.Equal("breaking", payload.GetProperty("verdict").GetString());
    }

    [Fact]
    public async Task FileWrite_AppliedWriteUpdatesSessionState()
    {
        var mathPath = WriteFile("math.calr", MathSource);
        WriteFile("other.calr", OtherSource);
        var sessionId = await OpenSessionAsync();

        // First write removes f001, which nothing references, so it applies.
        var first = Payload(await new FileWriteTool(_manager)
            .ExecuteAsync(Args(new { path = mathPath, content = MathSourceWithoutAdd, sessionId })));
        Assert.True(first.GetProperty("applied").GetBoolean());

        // A second identical write must see the session's updated state:
        // original now equals the new content, so the edit is a no-op and safe.
        var second = Payload(await new FileWriteTool(_manager)
            .ExecuteAsync(Args(new { path = mathPath, content = MathSourceWithoutAdd, sessionId })));
        Assert.True(second.GetProperty("applied").GetBoolean());
        Assert.Equal("safe", second.GetProperty("verdict").GetString());
    }

    // ── Registration ────────────────────────────────────────────────────

    [Fact]
    public void Handler_RegistersSessionAndFileWriteTools()
    {
        var handler = new McpMessageHandler(verbose: false);
        var tools = handler.GetRegisteredToolNamesForTest();

        Assert.Contains("calor_session_open", tools);
        Assert.Contains("calor_session_close", tools);
        Assert.Contains("calor_file_write", tools);
    }
}
