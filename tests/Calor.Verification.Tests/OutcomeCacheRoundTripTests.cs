using System.Text.Json;
using Calor.Compiler;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Verification;
using Calor.Compiler.Verification.Z3;
using Calor.Compiler.Verification.Z3.Cache;
using Xunit;

namespace Calor.Verification.Tests;

/// <summary>
/// Cache round-trip of the choke-point outcome (review of #754 item 2): the
/// claim "cache hits keep five-status granularity" needs a test that actually
/// serializes a non-null Outcome — including the positional-record
/// counterexample bindings — and reads it back. Covers both the raw entry
/// serialization (with the cache's own JSON options) and a full compile→
/// cache-hit→compile pipeline pass.
/// </summary>
public class OutcomeCacheRoundTripTests
{
    // Mirrors VerificationCache.JsonOptions (camelCase, indented)
    private static readonly JsonSerializerOptions CacheJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void CacheEntry_SerializesAndRehydrates_RefutedOutcomeWithBindings()
    {
        var outcome = ProofOutcome.Rehydrate(
            "refuted",
            [new CounterexampleBinding("x", "1"), new CounterexampleBinding("result", "10")],
            null);
        var result = Calor.Compiler.Verification.Z3.ContractVerificationResult.FromOutcome(outcome, Duration: TimeSpan.FromMilliseconds(12));

        var entry = VerificationCacheEntry.FromResult(result, contractHash: "hash", z3Version: "4.15.7");
        var json = JsonSerializer.Serialize(entry, CacheJsonOptions);

        // The new fields must actually be present on the wire
        Assert.Contains("\"proofStatus\": \"refuted\"", json);
        Assert.Contains("counterexampleBindings", json);
        Assert.Contains("\"name\": \"x\"", json);

        var restored = JsonSerializer.Deserialize<VerificationCacheEntry>(json, CacheJsonOptions);
        Assert.NotNull(restored);
        var restoredResult = restored.ToResult();

        Assert.NotNull(restoredResult.Outcome);
        Assert.Equal(ProofStatus.Refuted, restoredResult.Outcome.Status);
        Assert.NotNull(restoredResult.Outcome.Counterexample);
        Assert.Equal(2, restoredResult.Outcome.Counterexample.Bindings.Count);
        Assert.Equal("Counterexample: x=1, result=10", restoredResult.Outcome.Counterexample.Render());
        Assert.Equal(ContractVerificationStatus.Disproven, restoredResult.Status);
    }

    [Theory]
    [InlineData("timeout", "smt timeout")]
    [InlineData("unknown", "incomplete theory")]
    [InlineData("unsupported", "Type 'f64' is not supported")]
    public void CacheEntry_SerializesAndRehydrates_NonRefutedStatuses(string status, string reason)
    {
        var result = Calor.Compiler.Verification.Z3.ContractVerificationResult.FromOutcome(ProofOutcome.Rehydrate(status, null, reason));

        var entry = VerificationCacheEntry.FromResult(result, "hash", null);
        var restored = JsonSerializer.Deserialize<VerificationCacheEntry>(
            JsonSerializer.Serialize(entry, CacheJsonOptions), CacheJsonOptions);

        Assert.NotNull(restored);
        Assert.Equal(status, restored.ToResult().Outcome!.StatusName);
        Assert.Equal(reason, restored.ToResult().Outcome!.Reason);
    }

    [SkippableFact]
    public void CacheHit_KeepsFiveStatusGranularity_EndToEnd()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        var tempDir = Path.Combine(Path.GetTempPath(), "calor-cache-rt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var source = "§M{m1:Demo}\n  §F{f1:Dec:pub} (i32:x) -> i32\n    §Q (> x 0)\n    §S (> result 10)\n    §R (- x 1)\n";
            var path = Path.Combine(tempDir, "demo.calr");
            File.WriteAllText(path, source);

            CompilationOptions Options() => new()
            {
                VerifyContracts = true,
                StatusWriter = TextWriter.Null,
                VerificationCacheOptions = new VerificationCacheOptions
                {
                    Enabled = true,
                    ProjectDirectory = tempDir
                }
            };

            static Diagnostic RefutedDiagnostic(CompilationResult result) =>
                result.Diagnostics.Single(d => d.Code == DiagnosticCode.PostconditionMayBeViolated);

            // Cold run populates the cache
            var cold = RefutedDiagnostic(Program.Compile(source, path, Options()));
            Assert.Equal(ProofStatus.Refuted, cold.Verification!.Status);
            Assert.NotNull(cold.Verification.Counterexample);

            // The on-disk entry must carry the outcome fields
            var cacheDir = Directory.GetDirectories(tempDir, ".calor", SearchOption.AllDirectories)
                .DefaultIfEmpty(Path.Combine(tempDir, ".calor")).First();
            var entryFiles = Directory.Exists(cacheDir)
                ? Directory.GetFiles(cacheDir, "*.json", SearchOption.AllDirectories)
                : [];
            Assert.True(entryFiles.Length > 0, "expected verification cache entries on disk");
            Assert.Contains(entryFiles, f => File.ReadAllText(f).Contains("proofStatus"));

            // Warm run (cache hit) must keep the refuted status AND the model
            var warm = RefutedDiagnostic(Program.Compile(source, path, Options()));
            Assert.Equal(ProofStatus.Refuted, warm.Verification!.Status);
            Assert.NotNull(warm.Verification.Counterexample);
            Assert.NotEmpty(warm.Verification.Counterexample.Bindings);
            Assert.Equal(cold.Verification.Counterexample!.Render(), warm.Verification.Counterexample.Render());
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch (IOException) { }
        }
    }
}
