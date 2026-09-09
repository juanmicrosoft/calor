using System.Diagnostics;
using Calor.Compiler.Commands;
using Calor.Compiler.Verification;
using Calor.Compiler.Verification.Z3;
using Calor.Compiler.Verification.Z3.Cache;
using Calor.Enforcement.Tests;
using Xunit;

namespace Calor.Compiler.Tests;

public class ProductionOverflowRuntimeTests
{
    [Theory]
    [InlineData("(== (- (+ value 1) 1) value)", int.MaxValue)]
    [InlineData("(== (- (- value 1) -1) value)", int.MinValue)]
    [InlineData("(== (* value 2) (* 2 value))", int.MaxValue)]
    [InlineData("(== (- (- value)) value)", int.MinValue)]
    public void Verification_CannotElideOverflowingIdentityGuard(string predicate, int boundary)
    {
        var source = $$"""
            §M{m1:Overflow}
              §F{f1:Probe:pub} (i32:value) -> i32
                §E{}
                §S {{predicate}}
                §R value
            """;
        var options = VerifiedOptions();
        var compiled = Program.Compile(source, "overflow.calr", options);
        Assert.False(compiled.HasErrors, string.Join("; ", compiled.Diagnostics.Errors));
        Assert.Contains(compiled.Diagnostics, diagnostic =>
            diagnostic.Verification is { Status: ProofStatus.Assumed } outcome
            && outcome.Assumptions.Contains(Z3Verifier.CheckedArithmeticAssumption));
        Assert.IsType<OverflowException>(TestHarness.Execute(source, "Probe", [boundary], options).Exception);
        var safe = TestHarness.Execute(source, "Probe", [0], options);
        Assert.Null(safe.Exception);
        Assert.Equal(0, safe.ReturnValue);
    }

    [Fact]
    public void VerifiedBody_NormalReturnProofDoesNotSuppressBodyOverflow()
    {
        const string source = """
            §M{m1:Overflow}
              §F{f1:Probe:pub} (i32:value) -> i32
                §E{}
                §Q (>= value 0)
                §S (>= result 0)
                §R (* value value)
            """;
        var options = VerifiedOptions();
        var compiled = Program.Compile(source, "overflow.calr", options);
        Assert.Contains(compiled.Diagnostics, diagnostic =>
            diagnostic.Verification is { Status: ProofStatus.Proven, IsVacuous: false });
        Assert.IsType<OverflowException>(TestHarness.Execute(source, "Probe", [int.MaxValue], options).Exception);
        var safe = TestHarness.Execute(source, "Probe", [3], options);
        Assert.Null(safe.Exception);
        Assert.Equal(9, safe.ReturnValue);
    }

    [Theory]
    [InlineData("(forall ((i i32)) (-> (&& (>= i 0) (< i 1)) (== (- (+ value 1) 1) value)))")]
    [InlineData("(exists ((i i32)) (&& (&& (>= i 0) (< i 1)) (== (- (+ value 1) 1) value)))")]
    public void QuantifiedIdentity_KeepsOverflowingRuntimePredicate(string predicate)
    {
        var source = $$"""
            §M{m1:Overflow}
              §F{f1:Probe:pub} (i32:value) -> i32
                §E{}
                §S {{predicate}}
                §R value
            """;
        var options = VerifiedOptions();
        Assert.IsType<OverflowException>(TestHarness.Execute(source, "Probe", [int.MaxValue], options).Exception);
        var safe = TestHarness.Execute(source, "Probe", [0], options);
        Assert.Null(safe.Exception);
        Assert.Equal(0, safe.ReturnValue);
    }

    [Theory]
    [InlineData("(-> (< value 2147483647) (== (- (+ value 1) 1) value))")]
    [InlineData("(|| (== value 2147483647) (== (- (+ value 1) 1) value))")]
    [InlineData("(== (? (< value 2147483647) (== (- (+ value 1) 1) value) true) true)")]
    public void GuardedArithmetic_ProvesWithoutEvaluatingUnselectedOverflow(string predicate)
    {
        var source = $$"""
            §M{m1:Overflow}
              §F{f1:Probe:pub} (i32:value) -> i32
                §E{}
                §S {{predicate}}
                §R value
            """;
        var options = VerifiedOptions();
        var compiled = Program.Compile(source, "overflow.calr", options);
        Assert.False(compiled.HasErrors, string.Join("; ", compiled.Diagnostics.Errors));
        Assert.Contains(compiled.Diagnostics, diagnostic =>
            diagnostic.Verification is { Status: ProofStatus.Proven, IsVacuous: false });
        var execution = TestHarness.Execute(source, "Probe", [int.MaxValue], options);
        Assert.Null(execution.Exception);
        Assert.Equal(int.MaxValue, execution.ReturnValue);
    }

    [Fact]
    public void ObligationSolver_CannotElideOverflowingIdentity()
    {
        const string source = """
            §M{m1:Overflow}
              §F{f1:Probe:pub} (i32:value) -> i32
                §E{}
                §PROOF{p1:checked} (== (- (+ value 1) 1) value)
                §R value
            """;
        var options = VerifiedOptions();
        Assert.IsType<OverflowException>(TestHarness.Execute(source, "Probe", [int.MaxValue], options).Exception);
        var safe = TestHarness.Execute(source, "Probe", [0], options);
        Assert.Null(safe.Exception);
        Assert.Equal(0, safe.ReturnValue);
    }

    [Theory]
    [InlineData("+", int.MaxValue, 1, 3, 4, 7)]
    [InlineData("-", int.MinValue, 1, 7, 4, 3)]
    [InlineData("*", int.MaxValue, 2, 7, 4, 28)]
    public void DynamicArithmetic_TrapsWithOrdinaryRoslynSettings(
        string operation, int overflowLeft, int overflowRight, int safeLeft, int safeRight, int expected)
    {
        var source = $$"""
            §M{m1:Overflow}
              §F{f1:Probe:pub} (i32:left, i32:right) -> i32
                §E{}
                §R ({{operation}} left right)
            """;
        AssertOverflow(source, [overflowLeft, overflowRight]);
        var safe = TestHarness.Execute(source, "Probe", [safeLeft, safeRight], Options());
        Assert.Null(safe.Exception);
        Assert.Equal(expected, safe.ReturnValue);
    }

    [Theory]
    [InlineData("i64", "i32", 2147483648L, 2147483647L, int.MaxValue)]
    [InlineData("i32", "u8", 256, 255, (byte)255)]
    [InlineData("i32", "u8", -1, 0, (byte)0)]
    [InlineData("f64", "i32", 2147483648.0, 2147483647.0, int.MaxValue)]
    public void NarrowingConversions_TrapAtBoundaries(
        string inputType, string outputType, object overflow, object safeInput, object expected)
    {
        var source = $$"""
            §M{m1:Overflow}
              §F{f1:Probe:pub} ({{inputType}}:value) -> {{outputType}}
                §E{}
                §R (cast {{outputType}} value)
            """;
        AssertOverflow(source, [overflow]);
        var safe = TestHarness.Execute(source, "Probe", [safeInput], Options());
        Assert.Null(safe.Exception);
        Assert.Equal(expected, safe.ReturnValue);
    }

    [Fact]
    public void NegationAndCharacterNarrowing_Trap()
    {
        AssertOverflow("""
            §M{m1:Overflow}
              §F{f1:Probe:pub} (i32:value) -> i32
                §E{}
                §R (- value)
            """, [int.MinValue]);
        AssertOverflow("""
            §M{m1:Overflow}
              §F{f1:Probe:pub} (i32:value) -> char
                §E{}
                §R (char-from-code value)
            """, [65536]);
    }

    [Theory]
    [InlineData("inc", int.MaxValue)]
    [InlineData("dec", int.MinValue)]
    public void IncrementAndDecrement_Trap(string operation, int input)
    {
        AssertOverflow($$"""
            §M{m1:Overflow}
              §F{f1:Probe:pub} (i32:input) -> i32
                §E{mut}
                §B{~value:i32} input
                §R ({{operation}} value)
            """, [input]);
    }

    [Fact]
    public async Task ShippedExecutionProject_EnforcesNativeAndBackendOverflow()
    {
        const string source = """
            §M{m1:Overflow}
              §F{f1:Probe:pub} (i32:value) -> i32
                §E{}
                §R (+ value INT:1)
            """;
        var result = Program.Compile(source, "overflow.calr", Options());
        Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics.Errors));
        var directory = Path.Combine(AppContext.BaseDirectory, "overflow-workspace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "Directory.Build.props"), "<Project />");
            File.WriteAllText(Path.Combine(directory, "Directory.Build.targets"), "<Project />");
            File.WriteAllText(Path.Combine(directory, "overflow.calr"), source);
            var project = ExecutionWorkspace.WriteProject(directory, "OverflowWorkspace", true,
            [
                new("Overflow.g.cs", result.GeneratedCode),
                new("EntryPoint.cs", """
                    using System;
                    public static class EntryPoint
                    {
                        public static int Main()
                        {
                            try { Overflow.OverflowModule.Probe(int.MaxValue); return 1; }
                            catch (OverflowException) { }
                            try { var value = long.Parse("2147483648"); _ = (int)value; return 2; }
                            catch (OverflowException) { }
                            Console.WriteLine("checked-native-and-backend");
                            return 0;
                        }
                    }
                    """)
            ]);
            var start = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = directory
            };
            foreach (var argument in new[] { "run", "--project", project, "--verbosity", "quiet" })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            finally
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            Assert.True(process.ExitCode == 0, await stdout + Environment.NewLine + await stderr);
            Assert.Contains("checked-native-and-backend", await stdout);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void AssertOverflow(string source, object?[] arguments)
    {
        var result = TestHarness.Execute(source, "Probe", arguments, Options());
        Assert.IsType<OverflowException>(result.Exception);
    }

    private static CompilationOptions Options() => new()
    {
        EnableTypeChecking = true,
        EnforceEffects = true,
        StatusWriter = TextWriter.Null
    };

    private static CompilationOptions VerifiedOptions() => new()
    {
        EnableTypeChecking = true,
        EnforceEffects = true,
        VerifyContracts = true,
        VerifyRefinements = true,
        ElideProvenGuards = true,
        Verbose = true,
        ContractMode = ContractMode.Debug,
        StatusWriter = TextWriter.Null,
        VerificationCacheOptions = new VerificationCacheOptions { Enabled = false }
    };
}
