using Calor.Compiler;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification;
using Calor.Compiler.Verification.Obligations;
using Calor.Compiler.Verification.Z3;
using Calor.Compiler.Verification.Z3.Cache;
using Xunit;

namespace Calor.Verification.Tests;

/// <summary>
/// #1413 (0.24 S2) regression witnesses for S1 row QNT-NESTED (F-B1/N1-006..008), registered
/// unsupported-refused: a nested bounded forall has no runtime lowering (the emitter rejects
/// it with Calor0326), yet the verifier reported the postcondition Proven. The verifier now
/// claims nothing about a quantifier inside another, on every channel, and the cache never
/// serves such a key. The tests need Z3 and fail, not skip, without it.
/// </summary>
public sealed class S2NestedQuantifierTests : IDisposable
{
    private readonly string _cacheDir = Path.Combine(
        Path.GetTempPath(), "calor-s2-nested", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_cacheDir))
                Directory.Delete(_cacheDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static string Nested(string comparison) => $$"""
        §M{m1:R1Case}
          §F{f1:Probe:pub} (i8:n) -> i32
            §E{}
            §Q (&& (> n INT:0) (< n INT:12))
            §S (forall ((i i32)) (-> (&& (>= i INT:0) (< i n)) (forall ((j i32)) (-> (&& (>= j INT:0) (< j n)) {{comparison}}))))
            §R INT:0
        """;

    private (ProofOutcome Outcome, CompilationResult Result) Verify(string source, bool cache = false)
    {
        var options = new CompilationOptions
        {
            VerifyContracts = true,
            VerificationCacheOptions = new VerificationCacheOptions { Enabled = cache, CacheDirectory = _cacheDir },
        };
        var result = Program.Compile(source, "case.calr", options);
        var function = Assert.Single(options.VerificationResults!.Functions);
        return (Assert.Single(function.PostconditionResults).EffectiveOutcome, result);
    }

    [Theory]
    [InlineData("(< (- i j) INT:29)")]    // R1-QNT-NESTED-001
    [InlineData("(!= (- i j) INT:101)")]  // R1-QNT-NESTED-003
    [InlineData("(<= (- i j) INT:94)")]   // R1-QNT-NESTED-004
    public void NestedForall_IsRefusedNotProven(string comparison)
    {
        var (outcome, result) = Verify(Nested(comparison));
        Assert.Equal(ProofStatus.Unsupported, outcome.Status);
        Assert.Contains("nested quantifiers are refused", outcome.Reason);
        // The front end still rejects the runtime lowering; the verifier no longer claims a proof.
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.QuantifierRuntimeLoweringUnsupported);
    }

    [Fact]
    public void Control_SingleBoundedForall_StillProven()
    {
        const string source = """
            §M{m1:M}
              §F{f1:Probe:pub} (i8:n) -> i32
                §E{}
                §Q (&& (> n INT:0) (< n INT:12))
                §S (forall ((i i32)) (-> (&& (>= i INT:0) (< i n)) (< i INT:29)))
                §R INT:0
            """;
        Assert.Equal(ProofStatus.Proven, Verify(source).Outcome.Status);
    }

    [Fact]
    public void NestedForall_IsNeverCached()
    {
        // A cache written before the refusal may hold Proven for a nested quantifier: the key
        // is neither stored nor looked up, so such an entry can never be served.
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(Nested("(< (- i j) INT:29)"), diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        var function = Assert.Single(module.Functions);
        var post = Assert.Single(function.Postconditions);

        var hasher = new ContractHasher();
        hasher.GetCanonicalExpression(post.Condition);
        Assert.True(hasher.SawUnhashedKind);

        List<(string Name, string TypeName)> parameters = [("n", "i8")];
        using var cache = new VerificationCache(new VerificationCacheOptions { Enabled = true, CacheDirectory = _cacheDir });
        cache.CachePostconditionResult(parameters, "i32", function.Preconditions, post, function.Body,
            new Calor.Compiler.Verification.Z3.ContractVerificationResult(ContractVerificationStatus.Proven));
        Assert.False(cache.TryGetPostconditionResult(parameters, "i32", function.Preconditions, post, function.Body, out _));
    }

    [Fact]
    public void NestedQuantifierProofObligation_IsUnsupported()
    {
        const string source = """
            §M{m1:M}
              §F{f1:Probe:priv} (i32:n) -> i32
                §E{}
                §PROOF{p1:claim} (forall ((i i32)) (-> (&& (>= i INT:0) (< i INT:3)) (forall ((j i32)) (-> (&& (>= j INT:0) (< j INT:3)) (< (- i j) INT:5)))))
                §R INT:0
            """;
        var options = new CompilationOptions
        {
            VerifyRefinements = true,
            VerificationCacheOptions = new VerificationCacheOptions { Enabled = false },
        };
        Program.Compile(source, "case.calr", options);
        var proof = Assert.Single(options.ObligationResults!.Obligations, o => o.Kind == ObligationKind.ProofObligation);
        Assert.Equal(ObligationStatus.Unsupported, proof.Status);
    }

    [Fact]
    public void NestedQuantifierImplication_IsUnsupported()
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(Nested("(< (- i j) INT:29)"), diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        var function = Assert.Single(module.Functions);
        using var ctx = Z3ContextFactory.Create();
        using var prover = new Z3ImplicationProver(ctx);
        var result = prover.ProveImplication(
            [("n", "i8")], function.Preconditions[0].Condition, function.Postconditions[0].Condition);
        Assert.Equal(ImplicationStatus.Unsupported, result.Status);
    }
}
