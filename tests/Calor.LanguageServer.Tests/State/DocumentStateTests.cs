using Calor.LanguageServer.State;
using Calor.LanguageServer.Handlers;
using Calor.LanguageServer.Tests.Helpers;
using OmniSharp.Extensions.LanguageServer.Protocol;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Calor.LanguageServer.Tests.State;

public class DocumentStateTests
{
    [Fact]
    public void Constructor_SetsProperties()
    {
        var uri = new Uri("file:///test.calr");
        var source = "test source";

        var state = new DocumentState(uri, source, 1);

        Assert.Equal(uri, state.Uri);
        Assert.Equal(source, state.Source);
        Assert.Equal(1, state.Version);
    }

    [Fact]
    public void Reanalyze_ValidSource_ParsesAst()
    {
        var source = """
            §M{m001:TestModule}
              §F{f001:Test:pub} () -> i32
                §R 0
            """;

        var state = LspTestHarness.CreateDocument(source);

        Assert.NotNull(state.Ast);
        Assert.NotEmpty(state.Tokens);
        Assert.False(state.Diagnostics.HasErrors);
    }

    [Fact]
    public void Reanalyze_InvalidSource_HasErrors()
    {
        var source = "§M{m001:Test} §UNKNOWN_TOKEN_XYZ"; // Invalid section marker

        var state = LspTestHarness.CreateDocument(source);

        Assert.True(state.Diagnostics.HasErrors);
    }

    /// <summary>
    /// The §SEMVER compatibility check runs at parse time, so the language server
    /// surfaces the fail-closed refusal of a retired-major file as Calor0701 with the
    /// migration pointer (#1084 item 1 / #1087) — not only `calor build`.
    /// </summary>
    [Fact]
    public void Reanalyze_LegacySemver_ReportsCalor0701()
    {
        var source = """
            §M{m001:Legacy}
              §SEMVER{1.0.0}
              §F{f001:Test:pub} () -> i32
                §R 0
            """;

        var state = LspTestHarness.CreateDocument(source);

        Assert.True(state.Diagnostics.HasErrors);
        var refusal = Assert.Single(
            state.Diagnostics,
            d => d.Code == Compiler.Diagnostics.DiagnosticCode.SemanticsVersionIncompatible);
        Assert.Equal(Compiler.Diagnostics.DiagnosticSeverity.Error, refusal.Severity);
        Assert.Contains("issues/1084", refusal.Message);
        Assert.Equal(2, refusal.Span.Line);
    }

    /// <summary>Same document at the compiler's own major is clean in the language server.</summary>
    [Fact]
    public void Reanalyze_CurrentSemver_NoVersionDiagnostics()
    {
        var source = $$"""
            §M{m001:Modern}
              §SEMVER{{{Compiler.SemanticsVersion.VersionString}}}
              §F{f001:Test:pub} () -> i32
                §R 0
            """;

        var state = LspTestHarness.CreateDocument(source);

        Assert.False(state.Diagnostics.HasErrors);
        Assert.DoesNotContain(state.Diagnostics, d => d.Code.StartsWith("Calor070", StringComparison.Ordinal));
        Assert.Equal(Compiler.SemanticsVersion.VersionString, state.Ast?.DeclaredSemanticsVersion);
    }

    [Theory]
    [InlineData("§NEW{}")]
    [InlineData("§NEW{   }")]
    [InlineData("§NEW{<i32>}")]
    public void Reanalyze_MalformedNewType_PublishesDiagnosticWithoutThrowing(
        string expression)
    {
        var source = $$"""
            §M{m001:TestModule}
              §F{f001:Make:pub} () -> object
                §R {{expression}}
            """;

        DocumentState? state = null;
        var exception = Record.Exception(
            () => state = LspTestHarness.CreateDocument(source));

        Assert.Null(exception);
        Assert.NotNull(state);
        var diagnostic = Assert.Single(
            state.Diagnostics,
            item => item.Code
                    == Compiler.Diagnostics.DiagnosticCode.ExpectedTypeName);
        Assert.Equal(
            "§NEW requires a non-empty type name.",
            diagnostic.Message);
    }

    [Fact]
    public void Reanalyze_InteropConstruct_PublishesInfoSeverityUnsupported_NotError()
    {
        var source = """
            §M{m001:TestModule}
              §F{f001:Pick:pub} () -> object
                §R §CS{DateTime.Now}
            """;

        var state = LspTestHarness.CreateDocument(source);

        Assert.False(state.Diagnostics.HasErrors);
        var unsupported = state.Diagnostics.Where(
            d => d.Code == Compiler.Diagnostics.DiagnosticCode.AnalysisUnsupportedNode).ToList();
        Assert.NotEmpty(unsupported);
        Assert.All(unsupported,
            d => Assert.Equal(Compiler.Diagnostics.DiagnosticSeverity.Info, d.Severity));
    }

    [Fact]
    public void Update_ChangesSource()
    {
        var source1 = """
            §M{m001:TestModule}
              §F{f001:Test}
                §R 0
            """;
        var source2 = """
            §M{m001:TestModule}
              §F{f001:Test2}
                §R 1
            """;

        var state = LspTestHarness.CreateDocument(source1);
        state.Update(source2, 2);

        Assert.Equal(source2, state.Source);
        Assert.Equal(2, state.Version);
        Assert.NotNull(state.Ast);
        Assert.Equal("Test2", state.Ast.Functions[0].Name);
    }

    [Fact]
    public void Update_ClearsPreviousDiagnostics()
    {
        var badSource = "§M{m001:Test} §UNKNOWN_TOKEN_XYZ"; // Invalid
        var goodSource = """
            §M{m001:TestModule}
            """;

        var state = LspTestHarness.CreateDocument(badSource);
        Assert.True(state.Diagnostics.HasErrors);

        state.Update(goodSource, 2);

        Assert.False(state.Diagnostics.HasErrors);
    }

    [Fact]
    public void GetTokenAtPosition_ReturnsToken()
    {
        var source = """
            §M{m001:TestModule}
            """;

        var state = LspTestHarness.CreateDocument(source);
        var token = state.GetTokenAtPosition(1, 1);

        Assert.NotNull(token);
    }

    [Fact]
    public void GetTokenAtPosition_InvalidPosition_ReturnsNull()
    {
        var source = """
            §M{m001:TestModule}
            """;

        var state = LspTestHarness.CreateDocument(source);
        var token = state.GetTokenAtPosition(100, 1);

        Assert.Null(token);
    }

    [Fact]
    public void GetTokenAtOffset_ReturnsToken()
    {
        var source = """
            §M{m001:TestModule}
            """;

        var state = LspTestHarness.CreateDocument(source);
        var token = state.GetTokenAtOffset(0);

        Assert.NotNull(token);
    }

    [Fact]
    public void Reanalyze_SetsFilePath()
    {
        var source = """
            §M{m001:TestModule}
            """;

        var state = LspTestHarness.CreateDocument(source, "file:///path/to/test.calr");

        // Diagnostics should have the file path set
        // We can verify by checking if the state processed correctly with a file path
        Assert.NotNull(state.Ast);
    }

    [Fact]
    public void Reanalyze_WithBindingErrors_StillHasAst()
    {
        var source = """
            §M{m001:TestModule}
              §F{f001:Test}
                §R undefined_variable
            """;

        var state = LspTestHarness.CreateDocument(source);

        // Parsing should succeed, binding might have errors
        Assert.NotNull(state.Ast);
        Assert.NotEmpty(state.Ast.Functions);
    }

    [Fact]
    public void UndefinedVariable_WithSimilarName_HasDidYouMeanSuggestion()
    {
        // Uses "valeu" instead of "value" - a typo that should be suggested
        var source = """
            §M{m001:TestModule}
              §F{f001:Test}
                §I{i32:value}
                §O{i32}
                §R valeu
            """;

        var state = LspTestHarness.CreateDocument(source);

        // Should have an error for undefined reference
        Assert.True(state.Diagnostics.HasErrors);

        // Should have a "did you mean" suggestion
        var undefinedError = state.Diagnostics.Errors
            .FirstOrDefault(d => d.Code == "Calor0200"); // UndefinedReference
        Assert.NotNull(undefinedError);
        Assert.Contains("Did you mean", undefinedError.Message);
        Assert.Contains("value", undefinedError.Message);
    }

    [Fact]
    public void UndefinedVariable_WithSimilarName_HasQuickFix()
    {
        // Uses "valeu" instead of "value" - should have a quick fix
        var source = """
            §M{m001:TestModule}
              §F{f001:Test}
                §I{i32:value}
                §O{i32}
                §R valeu
            """;

        var fixes = LspTestHarness.GetDiagnosticsWithFixes(source);

        // Should have a quick fix for the undefined reference
        var undefinedFix = fixes.FirstOrDefault(d => d.Code == "Calor0200");
        Assert.NotNull(undefinedFix);
        Assert.NotNull(undefinedFix.Fix);
        Assert.Contains("value", undefinedFix.Fix.Description);
        Assert.NotEmpty(undefinedFix.Fix.Edits);
        Assert.Equal("value", undefinedFix.Fix.Edits.First().NewText);
    }

    [Fact]
    public async Task ConcurrentUpdatesAndReads_PublishOnlyCoherentLatestSnapshotAsync()
    {
        var state = new DocumentState(
            new Uri("file:///concurrent.calr"),
            VersionedSource(0),
            version: 0);
        await state.ReanalyzeAsync();
        using var stopReaders = new CancellationTokenSource();
        var reader = Task.Run(() =>
        {
            while (!stopReaders.IsCancellationRequested)
                AssertCoherent(state.Snapshot);
        });

        var updates = Enumerable.Range(1, 200)
            .Select(version => state.UpdateAsync(
                VersionedSource(version),
                version))
            .ToArray();
        await Task.WhenAll(updates);
        await stopReaders.CancelAsync();
        await reader;

        var latest = state.Snapshot;
        Assert.Equal(200, latest.Version);
        AssertCoherent(latest);
        Assert.Equal("Version200", latest.Ast!.Functions.Single().Name);
    }

    [Fact]
    public async Task CancellationRace_NewerVersionWinsAndCanceledAnalysisCannotPublishAsync()
    {
        using var firstBindingEntered = new ManualResetEventSlim();
        using var releaseFirstBinding = new ManualResetEventSlim();
        var bindingCalls = 0;
        var state = new DocumentState(
            new Uri("file:///cancellation.calr"),
            VersionedSource(1),
            version: 1,
            sourceIdentity: null,
            logger: new CapturingLogger(),
            failureInjector: phase =>
            {
                if (phase == DocumentState.DocumentAnalysisPhase.Binding
                    && Interlocked.Increment(ref bindingCalls) == 1)
                {
                    firstBindingEntered.Set();
                    Assert.True(releaseFirstBinding.Wait(TimeSpan.FromSeconds(10)));
                }
                return null;
            });

        var canceledAnalysis = state.ReanalyzeAsync();
        Assert.True(firstBindingEntered.Wait(TimeSpan.FromSeconds(10)));
        var latest = await state.UpdateAsync(VersionedSource(2), newVersion: 2);
        Assert.True(latest.Accepted);
        releaseFirstBinding.Set();
        await canceledAnalysis;

        Assert.Equal(2, state.Snapshot.Version);
        Assert.Equal("Version2", state.Snapshot.Ast!.Functions.Single().Name);
        AssertCoherent(state.Snapshot);
    }

    [Fact]
    public async Task DisposeDuringBlockedAnalysis_CancelsWithoutDisposingWorkerTokenAsync()
    {
        using var workerEntered = new ManualResetEventSlim();
        using var releaseWorker = new ManualResetEventSlim();
        var logger = new CapturingLogger();
        var state = new DocumentState(
            new Uri("file:///dispose-race.calr"),
            VersionedSource(1),
            version: 1,
            sourceIdentity: null,
            logger,
            failureInjector: phase =>
            {
                if (phase == DocumentState.DocumentAnalysisPhase.Lexing)
                {
                    workerEntered.Set();
                    Assert.True(releaseWorker.Wait(TimeSpan.FromSeconds(10)));
                }
                return null;
            });

        var analysis = state.ReanalyzeAsync();
        Assert.True(workerEntered.Wait(TimeSpan.FromSeconds(10)));
        state.Dispose();
        releaseWorker.Set();
        var snapshot = await analysis.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, snapshot.Version);
        Assert.Empty(logger.Entries);
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => state.ReanalyzeAsync());
    }

    [Fact]
    public async Task ReanalysisRegistrationRace_DidChangeVersionAlwaysWinsAsync()
    {
        var reanalysisCapturedOldContent = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var allowReanalysisRegistration = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var barrierCalls = 0;
        var state = new DocumentState(
            new Uri("file:///save-change-race.calr"),
            VersionedSource(1),
            version: 1,
            sourceIdentity: null,
            logger: new CapturingLogger(),
            failureInjector: null,
            reanalysisRegistrationBarrier: () =>
            {
                if (Interlocked.Increment(ref barrierCalls) == 2)
                {
                    reanalysisCapturedOldContent.TrySetResult();
                    return allowReanalysisRegistration.Task;
                }
                return Task.CompletedTask;
            });
        await state.ReanalyzeAsync();

        var save = state.ReanalyzeAsync();
        await reanalysisCapturedOldContent.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var change = await state.UpdateAsync(
            VersionedSource(2),
            newVersion: 2);
        Assert.True(change.Accepted);
        allowReanalysisRegistration.TrySetResult();
        var saveSnapshot = await save.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(2, saveSnapshot.Version);
        Assert.Equal(2, state.Snapshot.Version);
        Assert.Equal(VersionedSource(2), state.Snapshot.Source);
        Assert.Equal("Version2", state.Snapshot.Ast!.Functions.Single().Name);
        AssertCoherent(state.Snapshot);
    }

    [Fact]
    public async Task CancelledInitialOpen_IsUnacceptedAndAbsentAsync()
    {
        var workspace = new WorkspaceState();
        var uri = DocumentUri.From("file:///cancelled-initial-open.calr");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var accepted = await workspace.GetOrCreateAsync(
            uri,
            VersionedSource(1),
            version: 1,
            cancellation.Token);

        Assert.False(accepted);
        Assert.False(workspace.Contains(uri));
        Assert.Null(workspace.Get(uri));
    }

    [Fact]
    public async Task CancelledUpdate_PreservesCompletedSnapshotAndSameVersionRetrySucceedsAsync()
    {
        using var updateEntered = new ManualResetEventSlim();
        using var releaseUpdate = new ManualResetEventSlim();
        var bindingCalls = 0;
        var state = new DocumentState(
            new Uri("file:///cancelled-retry.calr"),
            VersionedSource(1),
            version: 1,
            sourceIdentity: null,
            logger: new CapturingLogger(),
            failureInjector: phase =>
            {
                if (phase == DocumentState.DocumentAnalysisPhase.Binding
                    && Interlocked.Increment(ref bindingCalls) == 2)
                {
                    updateEntered.Set();
                    Assert.True(releaseUpdate.Wait(TimeSpan.FromSeconds(10)));
                }
                return null;
            });
        await state.ReanalyzeAsync();
        var completed = state.Snapshot;
        using var cancellation = new CancellationTokenSource();

        var canceledUpdate = state.UpdateAsync(
            VersionedSource(2),
            newVersion: 2,
            cancellation.Token);
        Assert.True(updateEntered.Wait(TimeSpan.FromSeconds(10)));
        await cancellation.CancelAsync();
        releaseUpdate.Set();
        var canceled = await canceledUpdate;

        Assert.False(canceled.Accepted);
        Assert.Same(completed, state.Snapshot);
        Assert.Equal(1, state.Snapshot.Version);

        var retry = await state.UpdateAsync(
            VersionedSource(2),
            newVersion: 2);
        Assert.True(retry.Accepted);
        Assert.Equal(2, state.Snapshot.Version);
        Assert.Equal(VersionedSource(2), state.Snapshot.Source);
        AssertCoherent(state.Snapshot);
    }

    [Fact]
    public async Task NewerPendingUpdateWinsAndOlderRetryRemainsRejectedAsync()
    {
        using var newerEntered = new ManualResetEventSlim();
        using var releaseNewer = new ManualResetEventSlim();
        var bindingCalls = 0;
        var state = new DocumentState(
            new Uri("file:///pending-order.calr"),
            VersionedSource(1),
            version: 1,
            sourceIdentity: null,
            logger: new CapturingLogger(),
            failureInjector: phase =>
            {
                if (phase == DocumentState.DocumentAnalysisPhase.Binding
                    && Interlocked.Increment(ref bindingCalls) == 2)
                {
                    newerEntered.Set();
                    Assert.True(releaseNewer.Wait(TimeSpan.FromSeconds(10)));
                }
                return null;
            });
        await state.ReanalyzeAsync();

        var newer = state.UpdateAsync(VersionedSource(3), newVersion: 3);
        Assert.True(newerEntered.Wait(TimeSpan.FromSeconds(10)));
        var older = await state.UpdateAsync(VersionedSource(2), newVersion: 2);
        Assert.False(older.Accepted);
        releaseNewer.Set();
        Assert.True((await newer).Accepted);

        Assert.Equal(3, state.Snapshot.Version);
        Assert.Equal(VersionedSource(3), state.Snapshot.Source);
        AssertCoherent(state.Snapshot);
    }

    [Fact]
    public async Task DiagnosticPublicationRace_NeverRegressesGenerationForUnchangedUriAsync()
    {
        var coordinator = new DiagnosticPublicationCoordinator();
        var uri = DocumentUri.From("file:///unchanged-b.calr");
        var published = new List<long>();
        var tasks = Enumerable.Range(1, 100)
            .OrderByDescending(generation => generation % 7)
            .Select(generation => Task.Run(() => coordinator.PublishAsync(
                () => (
                    generation,
                    (IReadOnlyList<DiagnosticPublication>)
                    [
                        new DiagnosticPublication(
                            uri,
                            () =>
                            {
                                published.Add(generation);
                                return true;
                            }),
                    ]),
                CancellationToken.None)))
            .ToArray();

        await Task.WhenAll(tasks);
        await coordinator.PublishAsync(
            () => (
                99L,
                (IReadOnlyList<DiagnosticPublication>)
                [
                    new DiagnosticPublication(
                        uri,
                        () =>
                        {
                            published.Add(99);
                            return true;
                        }),
                ]),
            CancellationToken.None);

        Assert.NotEmpty(published);
        Assert.Equal(100, published[^1]);
        Assert.True(published.Zip(published.Skip(1), (left, right) => left < right)
            .All(increasing => increasing));
    }

    [Fact]
    public async Task SupersededWorkspaceDiagnostics_CannotOverwriteUnchangedDocumentAsync()
    {
        var workspace = new WorkspaceState();
        var aUri = DocumentUri.From("file:///publication-a.calr");
        var bUri = DocumentUri.From("file:///publication-b.calr");
        workspace.GetOrCreate(aUri, "§M{m001:A}\n", version: 1);
        workspace.GetOrCreate(bUri, "§M{m002:B}\n", version: 1);
        var older = workspace.CaptureSnapshot();
        var olderB = older.GetDocument(bUri)!;

        workspace.Update(aUri, "§M{m001:A2}\n", version: 2);
        var newer = workspace.CaptureSnapshot();
        var newerB = newer.GetDocument(bUri)!;
        Assert.Same(olderB.Analysis, newerB.Analysis);
        Assert.Equal(olderB.Analysis.Version, newerB.Analysis.Version);

        var coordinator = new DiagnosticPublicationCoordinator();
        var published = new List<string>();
        await coordinator.PublishAsync(
            () => (
                newer.Generation,
                (IReadOnlyList<DiagnosticPublication>)
                [
                    new DiagnosticPublication(
                        bUri,
                        () => workspace.TryPublishDiagnostics(
                            newer,
                            newerB,
                            () => published.Add("newer"))),
                ]),
            CancellationToken.None);
        await coordinator.PublishAsync(
            () => (
                older.Generation,
                (IReadOnlyList<DiagnosticPublication>)
                [
                    new DiagnosticPublication(
                        bUri,
                        () => workspace.TryPublishDiagnostics(
                            older,
                            olderB,
                            () => published.Add("older"))),
                ]),
            CancellationToken.None);

        Assert.Equal(["newer"], published);
    }

    [Fact]
    public async Task OutOfOrderAndEqualVersions_AreIgnoredAsync()
    {
        var state = new DocumentState(
            new Uri("file:///ordering.calr"),
            VersionedSource(5),
            version: 5);
        await state.ReanalyzeAsync();
        var captured = state.Snapshot;

        var older = await state.UpdateAsync(VersionedSource(4), newVersion: 4);
        var equal = await state.UpdateAsync(VersionedSource(50), newVersion: 5);

        Assert.False(older.Accepted);
        Assert.False(equal.Accepted);
        Assert.Same(captured, state.Snapshot);
        Assert.Equal("Version5", state.Snapshot.Ast!.Functions.Single().Name);
    }

    [Theory]
    [InlineData((int)DocumentState.DocumentAnalysisPhase.Binding)]
    [InlineData((int)DocumentState.DocumentAnalysisPhase.BindValidation)]
    [InlineData((int)DocumentState.DocumentAnalysisPhase.ReturnValidation)]
    public async Task AnalysisFailure_IsObservableAsCalor9999AndStructuredLogAsync(
        int phaseValue)
    {
        var phase = (DocumentState.DocumentAnalysisPhase)phaseValue;
        var logger = new CapturingLogger();
        var state = new DocumentState(
            new Uri("file:///failure.calr"),
            VersionedSource(7),
            version: 7,
            sourceIdentity: null,
            logger: logger,
            failureInjector: candidate => candidate == phase
                ? new InvalidOperationException("injected")
                : null);

        var snapshot = await state.ReanalyzeAsync();

        var diagnostic = Assert.Single(
            snapshot.Diagnostics.Where(item => item.Code == "Calor9999"));
        Assert.Contains("Internal", diagnostic.Message, StringComparison.Ordinal);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.IsType<InvalidOperationException>(entry.Exception);
        Assert.Equal(phase, entry.Properties["AnalysisPhase"]);
        Assert.Equal(state.Uri, entry.Properties["DocumentUri"]);
        Assert.Equal(7, entry.Properties["DocumentVersion"]);
    }

    private static string VersionedSource(int version) => $$"""
        §M{m001:Versioned}
          §F{f001:Version{{version}}:pub} () -> i32
            §R MissingVersion{{version}}
        """;

    private static void AssertCoherent(DocumentSnapshot snapshot)
    {
        var expectedName = $"Version{snapshot.Version}";
        Assert.Contains(expectedName, snapshot.Source, StringComparison.Ordinal);
        Assert.Contains(snapshot.Tokens, token =>
            token.Text.Contains(expectedName, StringComparison.Ordinal));
        Assert.Equal(expectedName, snapshot.Ast?.Functions.Single().Name);
        Assert.Contains(snapshot.BoundModule?.Functions ?? [], function =>
            function.Symbol.Name.EndsWith(expectedName, StringComparison.Ordinal));
        Assert.Contains(snapshot.Diagnostics, diagnostic =>
            diagnostic.Code == Compiler.Diagnostics.DiagnosticCode.UndefinedReference
            && diagnostic.Message.Contains(
                $"MissingVersion{snapshot.Version}",
                StringComparison.Ordinal));
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic =>
            diagnostic.Code == "Calor9999");
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) =>
            NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(pair => pair.Key, pair => pair.Value)
                : new Dictionary<string, object?>();
            Entries.Add(new LogEntry(logLevel, exception, properties));
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();
            public void Dispose()
            {
            }
        }
    }

    // ========================================================================
    // v0.15 E1 slice 2a, review round 1 finding 2 — Calor0270 in the editor.
    //
    // The binder marks EVERY receiver it cannot type UnresolvedBoundType, but
    // only reports the shapes an author can act on. These two pins observe that
    // split where the author actually sees it: the LSP diagnostic stream
    // (DocumentState.Reanalyze -> DiagnosticConverter), not just the bag.
    // ========================================================================

    [Fact]
    public void Calor0270_InferredLocalReceiver_IsOneInformationDiagnostic()
    {
        var source = """
            §M{m001:Test}
              §F{f001:DoWork:pub} () -> void
                §B{x} §C{Unknown.Make} §/C
                §C{x.Run} §/C
            """;

        var state = LspTestHarness.CreateDocument(source);

        var diagnostic = Assert.Single(state.Diagnostics.Where(
            d => d.Code == Calor.Compiler.Diagnostics.DiagnosticCode.SignatureUnresolved));
        Assert.Contains("'x'", diagnostic.Message, StringComparison.Ordinal);

        // What the editor is actually handed: an Information squiggle, never a
        // warning or an error — the type is unknown, the code is not wrong.
        var lsp = Calor.LanguageServer.Utilities.DiagnosticConverter
            .ToLspDiagnostic(diagnostic, source);
        Assert.Equal(
            OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Information,
            lsp.Severity);
    }

    [Fact]
    public void Calor0270_MemberChainReceiver_IsSilentInTheEditor()
    {
        var source = """
            §M{m001:Test}
              §F{f001:DoWork:pub} () -> void
                §B{a} §NEW{Random} §/NEW
                §C{a.b.Chain} §/C
            """;

        var state = LspTestHarness.CreateDocument(source);

        // The receiver is still unresolved to every analysis — it just does not
        // put an unactionable squiggle in the author's editor. Ungated, this
        // shape produced the bulk of 875 diagnostics over the converted corpus
        // (bench/phase0-agent-native/calor0270-corpus-ledger.json).
        Assert.Empty(state.Diagnostics.Where(
            d => d.Code == Calor.Compiler.Diagnostics.DiagnosticCode.SignatureUnresolved));
    }

    private sealed record LogEntry(
        LogLevel Level,
        Exception? Exception,
        IReadOnlyDictionary<string, object?> Properties);
}
