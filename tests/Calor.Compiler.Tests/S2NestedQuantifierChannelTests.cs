using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification.Obligations;
using Calor.Compiler.Verification.Z3;
using Calor.Compiler.Verification.Z3.Cache;
using Calor.Compiler.Verification.Z3.KInduction;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// #1413 (0.24 S2), S1 row QNT-NESTED, review round 1: the nested-quantifier refusal must hold
/// on the remaining channels too — obligation assumptions and k-induction invariants. These
/// need Z3 and fail, not skip, without it.
/// </summary>
public sealed class S2NestedQuantifierChannelTests
{
    [Fact]
    public void NestedQuantifierPrecondition_DoesNotDischargeAnObligation()
    {
        // The nested precondition entails n > 0 over non-empty bounds; it must not be used.
        Assert.True(Z3ContextFactory.IsAvailable, "these witnesses need Z3");
        const string source = """
            module M
              func Probe private (int:n | (forall int:i in 0..1 : (forall int:j in 0..1 : # > 0))) → int
                proof{"claim"} n > 0
                return n
            """;
        var options = new CompilationOptions
        {
            VerifyRefinements = true,
            VerificationCacheOptions = new VerificationCacheOptions { Enabled = false },
        };
        Program.Compile(source, "case.calr", options);
        var proof = Assert.Single(options.ObligationResults!.Obligations, o => o.Kind == ObligationKind.ProofObligation);
        Assert.NotEqual(ObligationStatus.Discharged, proof.Status);
    }

    [Fact]
    public void NestedFactOutOfScope_DoesNotBlockAnUnrelatedObligation()
    {
        // Review round 2 witness: the nested if-condition is a fact only inside its then-body,
        // so it must not make the earlier obligation Unsupported.
        Assert.True(Z3ContextFactory.IsAvailable, "these witnesses need Z3");
        const string source = """
            module M
              func Probe private (int:n) → int
                proof{"claim"} n * 0 >= 0
                if forall int:i in 0..1 : (bool)(forall int:j in 0..1 : j < 2)
                  return 1
                return 0
            """;
        var options = new CompilationOptions
        {
            VerifyRefinements = true,
            VerificationCacheOptions = new VerificationCacheOptions { Enabled = false },
        };
        Program.Compile(source, "case.calr", options);
        var proof = Assert.Single(options.ObligationResults!.Obligations, o => o.Kind == ObligationKind.ProofObligation);
        Assert.NotEqual(ObligationStatus.Unsupported, proof.Status);
    }

    [Fact]
    public void KInduction_UnparsableConjunct_IsNotDropped()
    {
        // Proving only "i >= 0" must not report the whole invariant, nested part included.
        Assert.True(Z3ContextFactory.IsAvailable, "these witnesses need Z3");
        const string source = """
            module Test
              func Loop public () → int
                ! cw
                for i from 0 to 3 step 1
                  System.Console.WriteLine(i)
                return 0
            """;
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        var function = new Binder(diagnostics).Bind(module).Functions.First();
        var loop = function.Body.OfType<BoundForStatement>().Single();

        using var prover = new KInductionProver(new KInductionOptions());
        var result = prover.ProveInvariant(loop, "i >= 0 && (forall ((j i32)) (forall ((k i32)) false))", function);
        Assert.NotEqual(KInductionStatus.Proven, result.Status);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public void RuntimeQuantifierRefusal_RetainsRequestedStaticRefusalWithoutCode(
        bool proofObligation, bool enableTypeChecking)
    {
        Assert.True(Z3ContextFactory.IsAvailable, "these witnesses need Z3");
        using var status = new StringWriter();
        var source = $$"""
            module NativeRefusal
              func Probe public (sbyte:n | (# > 0 && # < 12)) → int
                !
                {{(proofObligation ? "proof{\"claim\"}" : "=>")}} forall int:i in 0..n : (forall int:j in 0..n : i - j < 29)
                return 0
            """;
        var options = new CompilationOptions
        {
            VerifyContracts = !proofObligation,
            VerifyRefinements = proofObligation,
            EnableTypeChecking = enableTypeChecking,
            Verbose = true,
            StatusWriter = status,
            VerificationCacheOptions = new VerificationCacheOptions { Enabled = false }
        };

        var result = Program.Compile(source, "case.calr", options);

        Assert.True(result.HasErrors);
        Assert.Empty(result.GeneratedCode);
        Assert.Single(result.Diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.QuantifierRuntimeLoweringUnsupported);
        Assert.True(proofObligation ? options.ObligationResults is not null : options.VerificationResults is not null,
            "Requested verification was not reached:\n" + status);
        if (proofObligation)
        {
            Assert.NotNull(options.ObligationResults);
            var proof = Assert.Single(options.ObligationResults.Obligations,
                obligation => obligation.Kind == ObligationKind.ProofObligation);
            Assert.Equal(ObligationStatus.Unsupported, proof.Status);
        }
        else
        {
            Assert.NotNull(options.VerificationResults);
            var function = Assert.Single(options.VerificationResults.Functions);
            var postcondition = Assert.Single(function.PostconditionResults);
            Assert.Equal(Verification.ProofStatus.Unsupported, postcondition.EffectiveOutcome.Status);
            Assert.Contains("nested quantifiers are not verified", postcondition.EffectiveOutcome.Reason);
        }
    }

    [Theory]
    [InlineData("return missing")]
    [InlineData("System.Console.WriteLine(n)\n    return 0")]
    public void OrdinaryTypingAndEffectErrors_StillStopBeforeStaticVerification(string body)
    {
        var source = $$"""
            module InvalidNative
              func Probe public (int:n) → int
                !
                => result == n
                {{body}}
            """;
        var options = new CompilationOptions
        {
            VerifyContracts = true,
            VerificationCacheOptions = new VerificationCacheOptions { Enabled = false }
        };

        var result = Program.Compile(source, "case.calr", options);

        Assert.True(result.HasErrors);
        Assert.Empty(result.GeneratedCode);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.IsError
            && diagnostic.Code != DiagnosticCode.QuantifierRuntimeLoweringUnsupported);
        Assert.Null(options.VerificationResults);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RuntimeQuantifierRefusal_WithoutVerification_StillStopsWithoutCode(bool enableTypeChecking)
    {
        const string source = """
            module NativeRefusal
              func Probe public (sbyte:n | (# > 0 && # < 12)) → int
                !
                => forall int:i in 0..n : (forall int:j in 0..n : i - j < 29)
                return 0
            """;
        var options = new CompilationOptions { EnableTypeChecking = enableTypeChecking };

        var result = Program.Compile(source, "case.calr", options);

        Assert.True(result.HasErrors);
        Assert.Empty(result.GeneratedCode);
        Assert.Single(result.Diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.QuantifierRuntimeLoweringUnsupported);
        Assert.Null(options.VerificationResults);
        Assert.Null(options.ObligationResults);
    }

    [Fact]
    public void RuntimeQuantifierRefusal_DoesNotCachePartialVerification()
    {
        const string source = """
            module NativeRefusal
              func Probe public (sbyte:n | (# > 0 && # < 12)) → int
                !
                => forall int:i in 0..n : (forall int:j in 0..n : i - j < 29)
                return 0
            """;
        var cacheDirectory = Path.Combine(Path.GetTempPath(), "calor-refused-quantifier", Guid.NewGuid().ToString("N"));
        try
        {
            var options = new CompilationOptions
            {
                VerifyContracts = true,
                ElideProvenGuards = true,
                VerificationCacheOptions = new VerificationCacheOptions
                {
                    Enabled = true,
                    CacheDirectory = cacheDirectory
                }
            };

            var result = Program.Compile(source, "case.calr", options);

            Assert.True(result.HasErrors);
            Assert.Empty(result.GeneratedCode);
            Assert.NotNull(options.VerificationResults);
            Assert.Empty(Directory.Exists(cacheDirectory)
                ? Directory.GetFiles(cacheDirectory, "*", SearchOption.AllDirectories)
                : []);
        }
        finally
        {
            if (Directory.Exists(cacheDirectory))
                Directory.Delete(cacheDirectory, recursive: true);
        }
    }

    [Fact]
    public void RuntimeQuantifierRefusal_WithTypingError_StillStopsBeforeVerification()
    {
        const string source = """
            module InvalidNative
              func Probe public (sbyte:n | (# > 0 && # < 12)) → int
                !
                => forall int:i in 0..n : (forall int:j in 0..n : i - j < 29)
                return missing
            """;
        var options = new CompilationOptions { VerifyContracts = true };

        var result = Program.Compile(source, "case.calr", options);

        Assert.True(result.HasErrors);
        Assert.Empty(result.GeneratedCode);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UndefinedReference);
        Assert.Null(options.VerificationResults);
    }
}
