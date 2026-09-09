using System.Diagnostics;
using System.Reflection;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Commands;
using Calor.Compiler.Migration;
using Calor.Compiler.Verification;
using Calor.Compiler.Verification.Z3;
using Calor.Compiler.Verification.Z3.Cache;
using Calor.Enforcement.Tests;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public class ProductionOverflowRuntimeTests
{
    [Theory]
    [InlineData("wrap")]
    [InlineData("true")]
    [InlineData("unchecked-typo")]
    public void UnknownOverflowPolicy_IsRejectedRatherThanSilentlyChanged(string policy)
    {
        var result = Program.Compile($$"""
            §M{m1:Overflow:overflow={{policy}}}
              §F{f1:Probe:pub} (i32:value) -> i32
                §R (+ value 1)
            """, "overflow.calr", Options());
        Assert.True(result.HasErrors);
        Assert.Empty(result.GeneratedCode);
        Assert.Contains(result.Diagnostics.Errors, error => error.Message.Contains("overflow policy"));
    }

    [Theory]
    [InlineData("int", "return value + 1;", int.MaxValue)]
    [InlineData("int", "return value - 1;", int.MinValue)]
    [InlineData("int", "return value * 2;", int.MaxValue)]
    [InlineData("long", "return (int)value;", 2147483648L)]
    [InlineData("int", "value++; return value;", int.MaxValue)]
    [InlineData("int", "value += 1; return value;", int.MaxValue)]
    [InlineData("int", "return checked(value + 1);", int.MaxValue)]
    [InlineData("int", "return unchecked(value + 1);", int.MaxValue)]
    [InlineData("int", "checked { return value + 1; }", int.MaxValue)]
    [InlineData("int", "unchecked { return value + 1; }", int.MaxValue)]
    [InlineData("int", "return checked(unchecked(value + 1) - 1);", int.MaxValue)]
    public void LosslessMigration_PreservesCSharpOverflowContext(string type, string body, object input)
    {
        var csharp = $$"""
            public static class Migrated
            {
                public static int Probe({{type}} value) { {{body}} }
            }
            """;
        var conversion = new CSharpToCalorConverter().Convert(csharp);
        Assert.True(conversion.Success, string.Join("; ", conversion.Issues.Select(issue => issue.Message)));
        Assert.DoesNotContain(conversion.Losses, loss => loss.Kind == ConversionLossKind.Dropped);
        Assert.Contains("overflow=unchecked", conversion.CalorSource);
        var compiled = Program.Compile(conversion.CalorSource!, "migration.calr", Options());
        Assert.False(compiled.HasErrors, string.Join("; ", compiled.Diagnostics.Errors));
        var actual = InvokeCSharp(compiled.GeneratedCode, input);
        var expected = InvokeCSharp(csharp, input);
        Assert.True(expected.Error?.GetType() == actual.Error?.GetType(),
            $"Expected {expected.Error?.GetType().Name ?? "no exception"}; actual: {actual.Error}\n{conversion.CalorSource}");
        Assert.Equal(expected.Value, actual.Value);
    }

    [Fact]
    public void OverflowPolicy_RoundTripsAndSeparatesWarmProofCaches()
    {
        const string source = """
            §M{m1:Overflow:overflow=unchecked}
              §F{f1:Probe:pub} (i32:value) -> i32
                §E{}
                §S (== (- (+ value 1) 1) value)
                §R value
            """;
        var directory = Path.Combine(AppContext.BaseDirectory, "overflow-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            CompilationOptions OptionsWithCache() => new()
            {
                VerifyContracts = true,
                ElideProvenGuards = true,
                Verbose = true,
                StatusWriter = TextWriter.Null,
                VerificationCacheOptions = new VerificationCacheOptions { Enabled = true, ProjectDirectory = directory }
            };
            var uncheckedCompile = Program.Compile(source, "overflow.calr", OptionsWithCache());
            Assert.False(uncheckedCompile.HasErrors, string.Join("; ", uncheckedCompile.Diagnostics.Errors));
            Assert.Contains(uncheckedCompile.Diagnostics, diagnostic =>
                diagnostic.Verification is { Status: ProofStatus.Proven, IsVacuous: false });
            Assert.Contains("overflow=unchecked", new CalorEmitter().Emit(uncheckedCompile.Ast!));
            Assert.Null(TestHarness.Execute(source, "Probe", [int.MaxValue], OptionsWithCache()).Exception);
            var checkedSource = source.Replace(":overflow=unchecked", "");
            Assert.IsType<OverflowException>(
                TestHarness.Execute(checkedSource, "Probe", [int.MaxValue], OptionsWithCache()).Exception);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static (object? Value, Exception? Error) InvokeCSharp(string source, object input)
    {
        var compilation = CSharpCompilation.Create(
            "OverflowOracle_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var image = new MemoryStream();
        var emit = compilation.Emit(image);
        Assert.True(emit.Success, string.Join("; ", emit.Diagnostics));
        var assembly = Assembly.Load(image.ToArray());
        try
        {
            return (assembly.GetTypes().Single(type => type.Name == "Migrated")
                .GetMethod("Probe")!.Invoke(null, [input]), null);
        }
        catch (TargetInvocationException exception)
        {
            return (null, exception.InnerException);
        }
    }

    [Fact]
    public void UnsignedConditional_BodySafetyCannotExcludeValidNormalReturns()
    {
        const string source = """
            §M{m1:Overflow}
              §F{f1:Probe:pub} (u32:value) -> u32
                §E{}
                §S (&& (== result result) (!= value 2147483647))
                §R (+ (? true value value) 1)
            """;
        foreach (var elide in new[] { false, true })
        {
            var options = VerifiedOptions(elide);
            var compiled = Program.Compile(source, "overflow.calr", options);
            Assert.DoesNotContain(compiled.Diagnostics, diagnostic =>
                diagnostic.Verification is { Status: ProofStatus.Proven, IsVacuous: false });
            var execution = TestHarness.Execute(source, "Probe", [2147483647u], options);
            Assert.IsType<Calor.Runtime.ContractViolationException>(execution.Exception);
        }
    }

    [Fact]
    public void UnsignedConditional_IdentityCannotElideOverflowingCheck()
    {
        const string source = """
            §M{m1:Overflow}
              §F{f1:Probe:pub} (u32:value) -> u32
                §E{}
                §S (== (+ (? true value value) 1) (+ value 1))
                §R value
            """;
        foreach (var elide in new[] { false, true })
            Assert.IsType<OverflowException>(
                TestHarness.Execute(source, "Probe", [uint.MaxValue], VerifiedOptions(elide)).Exception);
    }

    [Fact]
    public void IntLocalPromotions_CannotExcludeUnsignedNormalReturnStates()
    {
        foreach (var binding in new[]
        {
            "§B{one:i32} 1\n    §B{unused} (+ value one)",
            "§B{one} 1\n    §B{unused} (+ value one)",
            "§B{one:i32} 1\n    §B{alias:i32} one\n    §B{unused} (+ value alias)",
            "§B{one:i32} 1\n    §IF{i1} (> value 0)\n      §B{unused} (+ value one)"
        })
        {
            var source = $$"""
                §M{m1:Overflow}
                  §F{f1:Probe:pub} (u32:value) -> i32
                    §E{}
                    §S (&& (== result 0) (!= value 4294967295))
                    {{binding}}
                    §R 0
                """;
            foreach (var elide in new[] { false, true })
            {
                var options = VerifiedOptions(elide);
                var compiled = Program.Compile(source, "overflow.calr", options);
                Assert.DoesNotContain(compiled.Diagnostics, diagnostic =>
                    diagnostic.Verification is { Status: ProofStatus.Proven, IsVacuous: false });
                Assert.IsType<Calor.Runtime.ContractViolationException>(
                    TestHarness.Execute(source, "Probe", [uint.MaxValue], options).Exception);
            }
        }
    }

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
    public async Task ShippedExecutionProject_TrapsNativeOverflowAndPreservesCSharpInterop()
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
                            var value = long.Parse("2147483648");
                            if ((int)value != int.MinValue) return 2;
                            Console.WriteLine("checked-native-and-compatible-csharp");
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
            Assert.Contains("checked-native-and-compatible-csharp", await stdout);
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

    private static CompilationOptions VerifiedOptions(bool elide = true) => new()
    {
        EnableTypeChecking = true,
        EnforceEffects = true,
        VerifyContracts = true,
        VerifyRefinements = true,
        ElideProvenGuards = elide,
        Verbose = true,
        ContractMode = ContractMode.Debug,
        StatusWriter = TextWriter.Null,
        VerificationCacheOptions = new VerificationCacheOptions { Enabled = false }
    };
}
