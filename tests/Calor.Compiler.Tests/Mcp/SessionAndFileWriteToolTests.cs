using System.Text.Json;
using Calor.Compiler.Mcp;
using Calor.Compiler.Mcp.Sessions;
using Calor.Compiler.Mcp.Tools;
using Xunit;

namespace Calor.Compiler.Tests.Mcp;

/// <summary>
/// Tests for the project-session tools (calor_session_open/close, loop plan
/// WS2 D2.1) and the transactional file write tool (calor_file_write, D2.4)
/// with auto-heal (D2.5) and canonical-path write confinement.
/// </summary>
public sealed class SessionAndFileWriteToolTests : IDisposable
{
    private readonly string _root;
    private readonly ProjectSessionManager _manager = new();

    // Both tools are rooted at the test directory: confinement is relative
    // to the server root (the working directory in production).
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

    private const string OtherSource = """
        module OtherModule OtherModule block
          func describe () → string
            !
            return "standalone module"
        """;

    /// <summary>A module with a real call site targeting `add` by name.</summary>
    private const string CallerSource = """
        module CallerModule CallerModule block
          func callsAdd (int:x) → int
            !
            let int:total = add(x, 1)
            return total
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
        var result = await OpenTool.ExecuteAsync(Args(new { directory = _root }));
        return Payload(result).GetProperty("sessionId").GetString()!;
    }

    [Fact]
    public async Task SessionOpen_ReportsParseErrorsAsEnvelopeDiagnostics()
    {
        WriteFile("broken.calr", "§M{Broken}\n  §F{f001:bad:pub\n");

        var payload = Payload(await OpenTool.ExecuteAsync(Args(new { directory = _root })));

        Assert.Equal(1, payload.GetProperty("parseErrorFileCount").GetInt32());
        var diagnostics = payload.GetProperty("diagnostics").EnumerateArray().ToList();
        Assert.NotEmpty(diagnostics);
        Assert.Equal("error", diagnostics[0].GetProperty("severity").GetString());
    }

    [Fact]
    public async Task SessionOpen_MissingDirectory_ReturnsError()
    {
        var result = await OpenTool
            .ExecuteAsync(Args(new { directory = Path.Combine(_root, "does-not-exist") }));

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task SessionOpen_OutsideServerRoot_ReturnsError()
    {
        var outside = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"calor-outside-root-{Guid.NewGuid():N}")).FullName;
        try
        {
            var result = await OpenTool.ExecuteAsync(Args(new { directory = outside }));
            Assert.True(result.IsError);
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
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

    [Fact]
    public async Task FileWrite_ParseError_RejectsAndLeavesFileUntouched()
    {
        var path = WriteFile("math.calr", MathSource);

        var payload = Payload(await WriteTool
            .ExecuteAsync(Args(new { path, content = "§M{Broken}\n  §F{f001:bad:pub\n" })));

        Assert.False(payload.GetProperty("applied").GetBoolean());
        Assert.Equal("breaking", payload.GetProperty("verdict").GetString());
        Assert.NotEmpty(payload.GetProperty("compilationResult").GetProperty("errors").EnumerateArray());
        Assert.Equal(MathSource, File.ReadAllText(path));
    }

    [Fact]
    public async Task FileWrite_CreatesMissingFile()
    {
        var path = Path.Combine(_root, "fresh.calr");

        var payload = Payload(await WriteTool
            .ExecuteAsync(Args(new { path, content = OtherSource })));

        Assert.True(payload.GetProperty("applied").GetBoolean());
        Assert.True(payload.GetProperty("created").GetBoolean());
        Assert.Equal(OtherSource, File.ReadAllText(path));
    }

    [Fact]
    public async Task FileWrite_NonCalrPath_ReturnsError()
    {
        var result = await WriteTool
            .ExecuteAsync(Args(new { path = Path.Combine(_root, "notes.txt"), content = "hello" }));

        Assert.True(result.IsError);
    }

    // ── calor_file_write: write confinement ─────────────────────────────

    [Fact]
    public async Task FileWrite_NoSession_OutsideWriteRoot_ReturnsError()
    {
        var outside = Path.Combine(Path.GetTempPath(), $"calor-noconfine-{Guid.NewGuid():N}", "escape.calr");

        var result = await WriteTool.ExecuteAsync(Args(new { path = outside, content = OtherSource }));

        Assert.True(result.IsError);
        Assert.False(File.Exists(outside));
        Assert.False(Directory.Exists(Path.GetDirectoryName(outside)!));
    }

    [Fact]
    public async Task FileWrite_SymlinkedSubdirectoryEscape_ReturnsError()
    {
        if (OperatingSystem.IsWindows()) return; // symlink creation needs elevation on Windows

        var outside = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"calor-symlink-target-{Guid.NewGuid():N}")).FullName;
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(_root, "link"), outside);

            // Lexically inside the root; physically outside via the symlink.
            var result = await WriteTool.ExecuteAsync(Args(new
            {
                path = Path.Combine(_root, "link", "escape.calr"),
                content = OtherSource
            }));

            Assert.True(result.IsError);
            Assert.False(File.Exists(Path.Combine(outside, "escape.calr")));
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public async Task FileWrite_HealDisabled_RejectsForbiddenClosers()
    {
        var path = WriteFile("math.calr", MathSource);
        const string withClosers = """
            §M{MathModule}
              §F{multiply:pub}
                §I{i32:a, i32:b}
                §O{i32}
                §R (* a b)
              §/F
            """;

        var payload = Payload(await WriteTool
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

        var result = await WriteTool
            .ExecuteAsync(Args(new { path, content = MathSource, sessionId = "cs-nope" }));

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task FileWrite_PathOutsideSessionRoot_ReturnsError()
    {
        WriteFile("math.calr", MathSource);
        var sessionId = await OpenSessionAsync();
        var outside = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.calr");

        var result = await WriteTool
            .ExecuteAsync(Args(new { path = outside, content = MathSource, sessionId }));

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task FileWrite_SessionSeesFilesChangedBehindItsBack()
    {
        var mathPath = WriteFile("math.calr", MathSource);
        var otherPath = WriteFile("other.calr", OtherSource);
        var sessionId = await OpenSessionAsync();

        // Change other.calr behind the session's back so it now really calls
        // add. Dirty-state invalidation must pick this up on the next call.
        File.WriteAllText(otherPath, CallerSource);
        // The invalidation gate is a stat check: force a distinct mtime so
        // this test is deterministic across filesystems with coarse
        // timestamp granularity. (A same-size edit that also preserves the
        // stat is a documented non-detection — BuildStateCache semantics.)
        File.SetLastWriteTimeUtc(otherPath, DateTime.UtcNow.AddSeconds(5));

        var payload = Payload(await WriteTool
            .ExecuteAsync(Args(new { path = mathPath, content = MathSourceWithoutAdd, sessionId })));

        Assert.False(payload.GetProperty("applied").GetBoolean());
        Assert.Equal("breaking", payload.GetProperty("verdict").GetString());
    }

    [Fact]
    public async Task WarmOriginalParse_HitsAcrossPathCasing_OnCaseInsensitivePlatforms()
    {
        // A client may address `math.calr` as `MATH.calr` on macOS/Windows;
        // the warm cache must not be silently defeated by the casing. The
        // canonical path here is computed exactly the way the integrated
        // write path computes it, so a normalization regression in
        // CheckAndApplyAsync's inputs fails this test.
        if (OperatingSystem.IsLinux())
            return;

        WriteFile("math.calr", MathSource);
        var sessionId = await OpenSessionAsync();
        var session = _manager.Get(sessionId)!;

        var upperCased = Path.Combine(_root, "MATH.calr");
        var canonical = CanonicalPath.Resolve(upperCased);
        var cached = session.TryGetFile(canonical);
        Assert.NotNull(cached);

        var warm = FileWriteTool.WarmOriginalParse(session, canonical, MathSource);
        Assert.Same(cached!.Parse, warm);
    }

    [Fact]
    public async Task UpdateFile_CaseVariantOfExistingEntry_ReplacesInsteadOfDuplicating()
    {
        // Writing through a differently-cased name on a case-insensitive
        // filesystem must replace the enumerated entry, not insert a
        // phantom duplicate that project-wide checks would treat as a
        // neighbor. (The distinct-file guard for case-sensitive volumes is
        // exercised via DirectoryHasExactEntry: here the directory has no
        // exact "MATH.calr" entry, so the variant key is reused.)
        if (OperatingSystem.IsLinux())
            return;

        var mathPath = WriteFile("math.calr", MathSource);
        var sessionId = await OpenSessionAsync();
        var session = _manager.Get(sessionId)!;
        var countBefore = session.SnapshotFiles().Count;

        // The write path always hands UpdateFile the canonical
        // (symlink-resolved) path; mirror that here.
        var upperCased = CanonicalPath.Resolve(Path.Combine(_root, "MATH.calr"));
        session.UpdateFile(upperCased, MathSourceWithoutAdd,
            CalorSourceHelper.ParseTolerant(MathSourceWithoutAdd, upperCased));

        Assert.Equal(countBefore, session.SnapshotFiles().Count);
        var state = session.TryGetFile(CanonicalPath.Resolve(mathPath))!;
        Assert.Equal(MathSourceWithoutAdd, state.Source);
    }

    [Fact]
    public async Task Refresh_FileGrownPastCap_BecomesOversizeStub()
    {
        // "A session refuses to load what a tool refuses to accept" must
        // hold for the file's whole session lifetime: a file that grows
        // past 512 KB after open goes through the same oversize guard as
        // Load instead of being read and indexed in full.
        var path = WriteFile("math.calr", MathSource);
        var sessionId = await OpenSessionAsync();
        var session = _manager.Get(sessionId)!;

        File.WriteAllText(path, new string('x', 600 * 1024));
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(5));
        session.Refresh();

        var state = session.TryGetFile(CanonicalPath.Resolve(path))!;
        Assert.Equal("", state.Source);
        Assert.False(state.Parse.IsSuccess);
        Assert.StartsWith("oversize:", state.ContentHash, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SessionFileState_CallTargets_ComputedOncePerParseState()
    {
        var callerPath = WriteFile("caller.calr", CallerSource);
        var sessionId = await OpenSessionAsync();
        var session = _manager.Get(sessionId)!;
        var canonical = CanonicalPath.Resolve(callerPath);

        var state = session.TryGetFile(canonical)!;
        Assert.Contains("add", state.CallTargets);
        Assert.Same(state.CallTargets, state.CallTargets);

        // A reparse (stat + content change) must produce a fresh index from
        // the new AST — the warm index never outlives its parse state.
        File.WriteAllText(callerPath, OtherSource);
        File.SetLastWriteTimeUtc(callerPath, DateTime.UtcNow.AddSeconds(5));
        session.Refresh();

        var reparsed = session.TryGetFile(canonical)!;
        Assert.NotSame(state, reparsed);
        Assert.DoesNotContain("add", reparsed.CallTargets);
    }

    [Fact]
    public async Task FileWrite_LogsRejectAndArchivesPayload()
    {
        var path = WriteFile("math.calr", MathSource);
        var logPath = Path.Combine(_root, "mcp-writes.jsonl");
        var rejectDir = Path.Combine(_root, "rejects");
        var tool = new FileWriteTool(_manager, _root, writeLogPath: logPath, rejectDir: rejectDir);
        const string broken = "§M{Broken}\n  §F{f001:bad:pub\n";

        var payload = Payload(await tool.ExecuteAsync(Args(new { path, content = broken, heal = false })));
        Assert.False(payload.GetProperty("applied").GetBoolean());

        var record = JsonDocument.Parse(File.ReadAllLines(logPath).Single()).RootElement;
        Assert.Equal("mcp-write/2", record.GetProperty("schema").GetString());
        Assert.False(record.GetProperty("applied").GetBoolean());
        Assert.Equal("breaking", record.GetProperty("verdict").GetString());
        var rejectPayloadPath = record.GetProperty("rejectPayload").GetString();
        Assert.NotNull(rejectPayloadPath);
        var archived = JsonDocument.Parse(File.ReadAllText(rejectPayloadPath!)).RootElement;
        Assert.Equal(broken, archived.GetProperty("rejectedContent").GetString());

        // Reject-path latency record (WS3 D3.2): the apply block is skipped
        // entirely, so applyMs measures nothing but the skipped branch;
        // the phase-sum invariant holds on rejects too.
        var latency = record.GetProperty("latencyMs").GetInt64();
        var check = record.GetProperty("checkMs").GetInt64();
        var apply = record.GetProperty("applyMs").GetInt64();
        // Loose bound: on reject only a skipped branch sits between the two
        // clock samples, but a GC pause or scheduler preemption can land
        // there — 50 ms keeps the intent (apply did not run) flake-free.
        Assert.True(apply <= 50, $"reject applyMs should be ~0, got {apply}");
        Assert.True(record.GetProperty("refreshMs").GetInt64() + check + apply <= latency);
    }

    [Fact]
    public async Task FileWrite_NoLogConfigured_WritesNoTelemetry()
    {
        var path = WriteFile("math.calr", MathSource);

        Payload(await WriteTool.ExecuteAsync(Args(new { path, content = MathSourceWithoutAdd })));

        Assert.False(File.Exists(Path.Combine(_root, "mcp-writes.jsonl")));
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
