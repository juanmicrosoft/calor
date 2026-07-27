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

    /// <summary>A module with a real call site targeting `add` by name.</summary>
    private const string CallerSource = """
        §M{m003:CallerModule}
          §F{f020:callsAdd:pub}
            §I{i32:x}
            §O{i32}
            §B{total:i32} §C{add} §A x §A INT:1 §/C
            §R total
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

    // ── calor_session_open / calor_session_close ────────────────────────

    [Fact]
    public async Task SessionOpen_ParsesFilesAndReturnsSessionId()
    {
        WriteFile("math.calr", MathSource);
        WriteFile("other.calr", OtherSource);

        var result = await OpenTool.ExecuteAsync(Args(new { directory = _root }));
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
    public async Task SessionOpen_OversizeFile_ReportedAsParseError()
    {
        WriteFile("math.calr", MathSource);
        WriteFile("huge.calr", new string('x', 600 * 1024));

        var payload = Payload(await OpenTool.ExecuteAsync(Args(new { directory = _root })));

        Assert.Equal(2, payload.GetProperty("fileCount").GetInt32());
        Assert.Equal(1, payload.GetProperty("parseErrorFileCount").GetInt32());
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

        var payload = Payload(await WriteTool
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

        var payload = Payload(await WriteTool
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

        var payload = Payload(await WriteTool
            .ExecuteAsync(Args(new { path, content = withoutContract })));

        Assert.True(payload.GetProperty("applied").GetBoolean());
        Assert.Equal("safe_with_warnings", payload.GetProperty("verdict").GetString());
        Assert.NotEmpty(payload.GetProperty("contractVerification").GetProperty("issues").EnumerateArray());
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
    public async Task FileWrite_PreservesUnixFileMode()
    {
        if (OperatingSystem.IsWindows()) return;

        var path = WriteFile("math.calr", MathSource);
        var restricted = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        File.SetUnixFileMode(path, restricted);

        var payload = Payload(await WriteTool
            .ExecuteAsync(Args(new { path, content = MathSourceWithoutAdd })));

        Assert.True(payload.GetProperty("applied").GetBoolean());
        Assert.Equal(restricted, File.GetUnixFileMode(path));
    }

    // ── calor_file_write: auto-heal (D2.5) ──────────────────────────────

    [Fact]
    public async Task FileWrite_HealsForbiddenClosers_AndCapsVerdictAtWarnings()
    {
        var path = WriteFile("math.calr", MathSource);
        // §/F and §/M are hard errors (Calor0830) — the healer must strip
        // them so the write survives instead of rejecting. Healing is not
        // semantics-preserving, so the verdict must not stay plain "safe".
        const string withClosers = """
            §M{m001:MathModule}
              §F{f002:multiply:pub}
                §I{i32:a, i32:b}
                §O{i32}
                §R (* a b)
              §/F
            §/M
            """;

        var payload = Payload(await WriteTool
            .ExecuteAsync(Args(new { path, content = withClosers })));

        Assert.True(payload.GetProperty("applied").GetBoolean());
        Assert.True(payload.GetProperty("healApplied").GetBoolean());
        Assert.Equal("safe_with_warnings", payload.GetProperty("verdict").GetString());
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
    public async Task FileWrite_RemovingCalledFunction_RejectsAsBreaking()
    {
        var mathPath = WriteFile("math.calr", MathSource);
        WriteFile("caller.calr", CallerSource); // real call site: §C{add}
        var sessionId = await OpenSessionAsync();

        var payload = Payload(await WriteTool
            .ExecuteAsync(Args(new { path = mathPath, content = MathSourceWithoutAdd, sessionId })));

        Assert.False(payload.GetProperty("applied").GetBoolean());
        Assert.Equal("breaking", payload.GetProperty("verdict").GetString());
        var dangling = payload.GetProperty("referenceIntegrity").GetProperty("danglingReferences")
            .EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains(dangling, d => d!.Contains("'add'") && d.Contains("caller.calr"));
        Assert.Equal(MathSource, File.ReadAllText(mathPath));
    }

    [Fact]
    public async Task FileWrite_StringMentionOfRemovedFunction_DoesNotBlock()
    {
        // The reference check matches call targets, not raw text: a file that
        // merely mentions the removed function's name inside a string literal
        // must not veto the write.
        var mathPath = WriteFile("math.calr", MathSource);
        WriteFile("prose.calr", """
            §M{m004:ProseModule}
              §F{f030:label:pub}
                §O{str}
                §R STR:"documentation mentions add and f001 in prose"
            """);
        var sessionId = await OpenSessionAsync();

        var payload = Payload(await WriteTool
            .ExecuteAsync(Args(new { path = mathPath, content = MathSourceWithoutAdd, sessionId })));

        Assert.True(payload.GetProperty("applied").GetBoolean());
        Assert.Equal("safe", payload.GetProperty("verdict").GetString());
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
    public async Task FileWrite_AppliedWriteUpdatesSessionState()
    {
        var mathPath = WriteFile("math.calr", MathSource);
        WriteFile("other.calr", OtherSource);
        var sessionId = await OpenSessionAsync();

        // First write removes add, which nothing calls, so it applies.
        var first = Payload(await WriteTool
            .ExecuteAsync(Args(new { path = mathPath, content = MathSourceWithoutAdd, sessionId })));
        Assert.True(first.GetProperty("applied").GetBoolean());

        // A second identical write must see the session's updated state:
        // original now equals the new content, so the edit is a no-op and safe.
        var second = Payload(await WriteTool
            .ExecuteAsync(Args(new { path = mathPath, content = MathSourceWithoutAdd, sessionId })));
        Assert.True(second.GetProperty("applied").GetBoolean());
        Assert.Equal("safe", second.GetProperty("verdict").GetString());
    }

    // ── Warm derived session state (WS3 D3.1) ───────────────────────────

    [Fact]
    public async Task WarmOriginalParse_ReusesCachedParse_WhenHashMatches()
    {
        var mathPath = WriteFile("math.calr", MathSource);
        var sessionId = await OpenSessionAsync();
        var session = _manager.Get(sessionId)!;
        var canonical = CanonicalPath.Resolve(mathPath);
        var cached = session.TryGetFile(canonical);
        Assert.NotNull(cached);

        var warm = FileWriteTool.WarmOriginalParse(session, canonical, MathSource);
        Assert.Same(cached!.Parse, warm);

        // Content mismatch (the stat-preserving-edit scenario): must fall
        // back to a cold parse, never serve the stale cached AST.
        var cold = FileWriteTool.WarmOriginalParse(session, canonical, MathSourceWithoutAdd);
        Assert.NotSame(cached.Parse, cold);
        Assert.True(cold.IsSuccess);
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
    public async Task WarmOriginalParse_ReusesHealedWriteState()
    {
        // A healed write stores the healed content + its parse in the
        // session. The next write's original side reads the healed bytes
        // from disk, so the hash must match the stored state and reuse it.
        var path = WriteFile("math.calr", MathSource);
        var sessionId = await OpenSessionAsync();
        var session = _manager.Get(sessionId)!;

        var withCloser = MathSourceWithoutAdd + "\n§/M\n";
        var payload = Payload(await WriteTool
            .ExecuteAsync(Args(new { path, content = withCloser, sessionId })));
        Assert.True(payload.GetProperty("applied").GetBoolean());
        Assert.True(payload.GetProperty("healApplied").GetBoolean());

        var canonical = CanonicalPath.Resolve(path);
        var onDisk = File.ReadAllText(path);
        var cached = session.TryGetFile(canonical)!;
        Assert.Same(cached.Parse, FileWriteTool.WarmOriginalParse(session, canonical, onDisk));
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
    public async Task FileWrite_WarmProjectReferenceCheck_KeepsVerdictParityAcrossCalls()
    {
        // The project-reference walk consumes the warm call-target index: a
        // repeated breaking write must keep rejecting (index reused across
        // calls), and once the caller changes behind the session's back the
        // same write must apply (index invalidated with its parse state).
        var mathPath = WriteFile("math.calr", MathSource);
        var callerPath = WriteFile("caller.calr", CallerSource);
        var sessionId = await OpenSessionAsync();

        var first = Payload(await WriteTool
            .ExecuteAsync(Args(new { path = mathPath, content = MathSourceWithoutAdd, sessionId })));
        var second = Payload(await WriteTool
            .ExecuteAsync(Args(new { path = mathPath, content = MathSourceWithoutAdd, sessionId })));
        Assert.Equal("breaking", first.GetProperty("verdict").GetString());
        Assert.Equal("breaking", second.GetProperty("verdict").GetString());

        File.WriteAllText(callerPath, OtherSource);
        File.SetLastWriteTimeUtc(callerPath, DateTime.UtcNow.AddSeconds(5));

        var third = Payload(await WriteTool
            .ExecuteAsync(Args(new { path = mathPath, content = MathSourceWithoutAdd, sessionId })));
        Assert.True(third.GetProperty("applied").GetBoolean());
        Assert.Equal("safe", third.GetProperty("verdict").GetString());
    }

    // ── Write telemetry (M-L2 / M-L4 stream) ────────────────────────────

    [Fact]
    public async Task FileWrite_LogsAppliedAttempt_WhenLogConfigured()
    {
        var path = WriteFile("math.calr", MathSource);
        var logPath = Path.Combine(_root, "mcp-writes.jsonl");
        var tool = new FileWriteTool(_manager, _root, writeLogPath: logPath);

        var payload = Payload(await tool.ExecuteAsync(Args(new { path, content = MathSourceWithoutAdd })));
        Assert.True(payload.GetProperty("applied").GetBoolean());

        var records = File.ReadAllLines(logPath).Select(l => JsonDocument.Parse(l).RootElement).ToList();
        Assert.Single(records);
        Assert.Equal("mcp-write/2", records[0].GetProperty("schema").GetString());
        Assert.True(records[0].GetProperty("applied").GetBoolean());
        Assert.Equal("safe", records[0].GetProperty("verdict").GetString());

        // Latency fields (WS3 D3.2): total plus the phase breakdown, all
        // non-negative; refresh is 0 here because the write ran sessionless.
        // The phases are disjoint segments of one monotonic clock sampled
        // last, so their sum can never exceed the total — this deterministic
        // invariant pins the marks' ordering and point-of-sample (a swapped
        // or pre-sampled implementation fails it).
        var latency = records[0].GetProperty("latencyMs").GetInt64();
        var refresh = records[0].GetProperty("refreshMs").GetInt64();
        var check = records[0].GetProperty("checkMs").GetInt64();
        var apply = records[0].GetProperty("applyMs").GetInt64();
        Assert.True(latency >= 0);
        Assert.Equal(0, refresh);
        Assert.True(check >= 0);
        Assert.True(apply >= 0);
        Assert.True(refresh + check + apply <= latency,
            $"phase sum {refresh}+{check}+{apply} exceeds latencyMs {latency}");
    }

    [Fact]
    public async Task FileWrite_LogsRejectAndArchivesPayload()
    {
        var path = WriteFile("math.calr", MathSource);
        var logPath = Path.Combine(_root, "mcp-writes.jsonl");
        var rejectDir = Path.Combine(_root, "rejects");
        var tool = new FileWriteTool(_manager, _root, writeLogPath: logPath, rejectDir: rejectDir);
        const string broken = "§M{m001:Broken}\n  §F{f001:bad:pub\n";

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
        Assert.True(apply <= 1, $"reject applyMs should be ~0, got {apply}");
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
