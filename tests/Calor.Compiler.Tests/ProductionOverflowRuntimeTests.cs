using System.Diagnostics;
using System.Reflection;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Commands;
using Calor.Compiler.Diagnostics;
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
    [Fact]
    public void GloballyCheckedOpaqueCompilation_IsRejectedWithExplicitLoss()
    {
        const string source = """
            [assembly: System.Reflection.AssemblyTitle("Overflow")]
            public static class Migrated
            {
                public static int Probe(int value) => value + 1;
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create("CheckedSource", [tree], GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, checkOverflow: true));
        var context = new ConversionContext { SourceFile = "checked.cs" };
        new RoslynSyntaxVisitor(context, compilation.GetSemanticModel(tree))
            .Convert(tree.GetCompilationUnitRoot(), "Migrated");

        Assert.IsType<OverflowException>(InvokeCSharp(source, int.MaxValue, checkOverflow: true).Error);
        Assert.True(context.HasErrors);
        Assert.Contains(context.Losses, loss =>
            loss.Kind == ConversionLossKind.Dropped && loss.Feature == "checked-compilation-interop");
    }

    [Theory]
    [InlineData("return value + 1;")]
    [InlineData("return checked(value + 1);")]
    [InlineData("return unchecked(value + 1);")]
    [InlineData("checked { return value + 1; }")]
    [InlineData("unchecked { return value + 1; }")]
    public void GloballyCheckedNativeAndExplicitContexts_PreserveRuntimeSemantics(string body)
    {
        var source = $$"""
            public static class Migrated
            {
                public static int Probe(int value) { {{body}} }
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create("CheckedSource", [tree], GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, checkOverflow: true));
        var context = new ConversionContext { SourceFile = "checked.cs" };
        var module = new RoslynSyntaxVisitor(context, compilation.GetSemanticModel(tree))
            .Convert(tree.GetCompilationUnitRoot(), "Migrated");
        Assert.False(context.HasErrors, string.Join("; ", context.Issues.Select(issue => issue.Message)));
        var compiled = Program.Compile(new CalorEmitter().Emit(module), "checked.calr", Options());
        Assert.False(compiled.HasErrors, string.Join("; ", compiled.Diagnostics.Errors));
        var actual = InvokeCSharp(compiled.GeneratedCode, int.MaxValue);
        var expected = InvokeCSharp(source, int.MaxValue, checkOverflow: true);
        Assert.Equal(expected.Error?.GetType(), actual.Error?.GetType());
        Assert.Equal(expected.Value, actual.Value);
    }

    [Theory]
    [InlineData("wrap")]
    [InlineData("true")]
    [InlineData("unchecked_typo")]
    public void UnknownOverflowPolicy_IsRejectedRatherThanSilentlyChanged(string policy)
    {
        var result = Program.Compile($$"""
            module Overflow overflow {{policy}}
              func Probe (int:value) → int
                !
                return (value + 1)
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
        Assert.Contains("overflow unchecked", conversion.CalorSource);
        var compiled = Program.Compile(conversion.CalorSource!, "migration.calr", Options());
        Assert.False(compiled.HasErrors, string.Join("; ", compiled.Diagnostics.Errors));
        var actual = InvokeCSharp(compiled.GeneratedCode, input);
        var expected = InvokeCSharp(csharp, input);
        Assert.True(expected.Error?.GetType() == actual.Error?.GetType(),
            $"Expected {expected.Error?.GetType().Name ?? "no exception"}; actual: {actual.Error}\n{conversion.CalorSource}");
        Assert.Equal(expected.Value, actual.Value);
    }

    [Theory]
    [InlineData("int", "checked(value + 1)", int.MaxValue)]
    [InlineData("int", "unchecked(value + 1)", int.MaxValue)]
    [InlineData("int", "checked(unchecked(value + 1) - 1)", int.MaxValue)]
    [InlineData("int", "unchecked(checked(value + 1) - 1)", int.MaxValue)]
    [InlineData("int", "unchecked(value + 1) - 1", int.MaxValue)]
    [InlineData("long", "checked((int)value)", 2147483648L)]
    [InlineData("long", "unchecked((int)value)", 2147483648L)]
    [InlineData("int", "checked((byte)unchecked(value + 1))", 255)]
    [InlineData("int", "unchecked((byte)checked(value + 1))", 255)]
    public void NativeOverflowExpressions_PreserveScopeAcrossMigrationAndReemission(
        string type, string expression, object input)
    {
        var csharp = $$"""
            public static class Migrated
            {
                public static int Probe({{type}} value) => {{expression}};
            }
            """;
        foreach (var checkOverflow in new[] { false, true })
        {
            var native = $$"""
                module OverflowExpressions{{(checkOverflow ? "" : " overflow unchecked")}}
                  module Migrated
                    func Probe ({{type}}:value) → int
                      !
                      return {{expression}}
                """;
            var migrated = ConvertNativeOverflowSource(csharp, checkOverflow);
            foreach (var source in new[] { native, migrated })
            {
                var expected = InvokeCSharp(csharp, input, checkOverflow);
                foreach (var generated in CompileAndReemitOverflow(source))
                {
                    foreach (var generatedCheckOverflow in new[] { false, true })
                    {
                        var actual = InvokeCSharp(generated, input, generatedCheckOverflow);
                        Assert.Equal(expected.Error?.GetType(), actual.Error?.GetType());
                        Assert.Equal(expected.Value, actual.Value);
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData("checked(Record(trace, 2147483647) + Record(trace, 1)) + Record(trace, 3)")]
    [InlineData("unchecked(Record(trace, 2147483647) + Record(trace, 1)) + Record(trace, 3)")]
    [InlineData("checked(unchecked(Record(trace, 2147483647) + Record(trace, 1)) - Record(trace, 1))")]
    [InlineData("unchecked(checked(Record(trace, 2147483647) + Record(trace, 1)) - Record(trace, 1))")]
    public void NativeOverflowExpressions_EvaluateOperandsOnceInOrder(string expression)
    {
        var csharp = $$"""
            using System.Collections.Generic;
            public static class Migrated
            {
                private static int Record(List<int> trace, int value)
                {
                    trace.Add(value);
                    return value;
                }
                public static int Probe(List<int> trace) => {{expression}};
            }
            """;
        foreach (var checkOverflow in new[] { false, true })
        {
            var native = $$"""
                module OverflowEvaluation{{(checkOverflow ? "" : " overflow unchecked")}}
                  using System.Collections.Generic
                  module Migrated
                    func Record private (List<int>:trace, int:value) → int
                      trace.Add(value)
                      return value
                    func Probe (List<int>:trace) → int
                      ! mut
                      return {{expression}}
                """;
            var sources = new List<string> { native };
            if (!checkOverflow)
            {
                var conversion = new CSharpToCalorConverter().Convert(csharp);
                Assert.True(conversion.Success, string.Join("; ", conversion.Issues.Select(issue => issue.Message)));
                Assert.Empty(conversion.Losses);
                sources.Add(conversion.CalorSource!);
            }
            foreach (var source in sources)
            {
                foreach (var generated in CompileAndReemitOverflow(source))
                {
                    var expectedTrace = new List<int>();
                    var actualTrace = new List<int>();
                    var expected = InvokeCSharp(csharp, expectedTrace, checkOverflow);
                    var actual = InvokeCSharp(generated, actualTrace);
                    Assert.Equal(expected.Error?.GetType(), actual.Error?.GetType());
                    Assert.Equal(expected.Value, actual.Value);
                    Assert.Equal(expectedTrace, actualTrace);
                    Assert.Equal([int.MaxValue, 1], actualTrace.Take(2));
                }
            }
        }
    }

    [Fact]
    public void EscapedOverflowKeywords_RemainOrdinaryCallsAndBindings()
    {
        const string csharp = """
            public static class Migrated
            {
                private static int @unchecked(int value) => value;
                private static int @checked(int value) => @unchecked(value);
                public static int Probe(int value)
                {
                    int @unchecked = 1;
                    return @checked(value) + @unchecked;
                }
            }
            """;
        const string native = """
            module OverflowIdentifiers overflow unchecked
              module Migrated
                func @unchecked private (int:value) → int
                  return value
                func @checked private (int:value) → int
                  return @unchecked(value)
                func Probe (int:value) → int
                  !
                  let int:@unchecked = 1
                  return @checked(value) + @unchecked
            """;
        foreach (var source in new[] { native, ConvertNativeOverflowSource(csharp, checkOverflow: false) })
        {
            foreach (var generated in CompileAndReemitOverflow(source))
                Assert.Equal(int.MinValue, InvokeCSharp(generated, int.MaxValue).Value);
        }
    }

    [Theory]
    [InlineData("checked")]
    [InlineData("unchecked")]
    public void NativeOverflowExpressions_DoNotHideBindingTypeOrEffectErrors(string keyword)
    {
        string Source(string expression) => $$"""
            module OverflowAnalysis
              func Effectful private () → int
                Console.WriteLine("effect")
                return 1
              func Probe (int:value) → int
                !
                return {{keyword}}({{expression}})
            """;
        var unbound = Program.Compile(Source("missing + value"), "overflow.calr", Options());
        Assert.Contains(unbound.Diagnostics.Errors, error => error.Code == DiagnosticCode.UndefinedReference);
        var wrongType = Program.Compile(Source("\"not an integer\""), "overflow.calr", Options());
        Assert.Contains(wrongType.Diagnostics.Errors, error => error.Code == DiagnosticCode.TypeMismatch);
        var missingEffect = Program.Compile(Source("Effectful()"), "overflow.calr", Options());
        Assert.Contains(missingEffect.Diagnostics.Errors, error => error.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Fact]
    public void ExplicitOverflowScope_CannotElideUnmodeledRuntimeGuard()
    {
        const string source = """
            module OverflowProof
              module Migrated
                func Probe (int:value) → int
                  !
                  => unchecked(value + 1) > value
                  return value
            """;
        foreach (var elide in new[] { false, true })
        {
            var compiled = Program.Compile(source, "overflow.calr", VerifiedOptions(elide));
            Assert.False(compiled.HasErrors, string.Join("; ", compiled.Diagnostics.Errors));
            Assert.DoesNotContain(compiled.Diagnostics, diagnostic =>
                diagnostic.Verification is { Status: ProofStatus.Proven, IsVacuous: false });
            Assert.IsType<Calor.Runtime.ContractViolationException>(
                InvokeCSharp(compiled.GeneratedCode, int.MaxValue).Error);
            Assert.Null(InvokeCSharp(compiled.GeneratedCode, 0).Error);
        }
    }

    // These theory rows are registered cases of the #1421 determinism protocol
    // (tests/TestData/DeterminismProtocol/cases.json), whose names embed the row data, so the
    // rows keep their original (legacy) spelling and the native source text is looked up here.
    private static readonly Dictionary<string, string> NativeRowTexts = new(StringComparer.Ordinal)
    {
        ["(== (- (+ value 1) 1) value)"] = "(((value + 1) - 1) == value)",
        ["(== (- (- value 1) -1) value)"] = "(((value - 1) - -1) == value)",
        ["(== (* value 2) (* 2 value))"] = "((value * 2) == (2 * value))",
        ["(== (- (- value)) value)"] = "((-(-value)) == value)",
        ["(forall ((i i32)) (-> (&& (>= i 0) (< i 1)) (== (- (+ value 1) 1) value)))"] =
            "(forall int:i in 0..1 : (((value + 1) - 1) == value))",
        ["(exists ((i i32)) (&& (&& (>= i 0) (< i 1)) (== (- (+ value 1) 1) value)))"] =
            "(exists int:i in 0..1 : (((value + 1) - 1) == value))",
        ["(-> (< value 2147483647) (== (- (+ value 1) 1) value))"] =
            "(!(value < 2147483647) || (((value + 1) - 1) == value))",
        ["(|| (== value 2147483647) (== (- (+ value 1) 1) value))"] =
            "((value == 2147483647) || (((value + 1) - 1) == value))",
        ["(== (? (< value 2147483647) (== (- (+ value 1) 1) value) true) true)"] =
            "(((value < 2147483647) ? (((value + 1) - 1) == value) : true) == true)",
        ["i64"] = "long",
        ["i32"] = "int",
        ["u8"] = "byte",
        ["f64"] = "double",
    };

    private static string NativeRowText(string row) => NativeRowTexts[row];

    private static string ConvertNativeOverflowSource(string source, bool checkOverflow)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create("OverflowSource", [tree], GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, checkOverflow: checkOverflow));
        var context = new ConversionContext { SourceFile = "overflow.cs" };
        var module = new RoslynSyntaxVisitor(context, compilation.GetSemanticModel(tree))
            .Convert(tree.GetCompilationUnitRoot(), "Migrated");
        Assert.False(context.HasErrors, string.Join("; ", context.Issues.Select(issue => issue.Message)));
        Assert.Empty(context.Losses);
        return new CalorEmitter().Emit(module);
    }

    private static IEnumerable<string> CompileAndReemitOverflow(string source)
    {
        var defaultCompile = Program.Compile(source, "overflow.calr");
        Assert.False(defaultCompile.HasErrors, string.Join("; ", defaultCompile.Diagnostics.Errors));
        yield return defaultCompile.GeneratedCode;
        var typedCompile = Program.Compile(source, "overflow.calr", Options());
        Assert.False(typedCompile.HasErrors, string.Join("; ", typedCompile.Diagnostics.Errors));
        var reemitted = new CalorEmitter().Emit(typedCompile.Ast!);
        Assert.DoesNotContain("§CS", reemitted);
        var recompiled = Program.Compile(reemitted, "overflow.calr", Options());
        Assert.False(recompiled.HasErrors, string.Join("; ", recompiled.Diagnostics.Errors));
        yield return recompiled.GeneratedCode;
    }

    [Fact]
    public void OverflowPolicy_RoundTripsAndSeparatesWarmProofCaches()
    {
        const string source = """
            module Overflow Overflow block overflow unchecked
              func Probe (int:value) → int
                !
                => (((value + 1) - 1) == value)
                return value
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
            Assert.Contains("overflow unchecked", new CalorEmitter().Emit(uncheckedCompile.Ast!));
            Assert.Null(TestHarness.Execute(source, "Probe", [int.MaxValue], OptionsWithCache()).Exception);
            var checkedSource = source.Replace(" overflow unchecked", "");
            Assert.IsType<OverflowException>(
                TestHarness.Execute(checkedSource, "Probe", [int.MaxValue], OptionsWithCache()).Exception);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static (object? Value, Exception? Error) InvokeCSharp(string source, object input, bool checkOverflow = false)
    {
        var compilation = CSharpCompilation.Create(
            "OverflowOracle_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, checkOverflow: checkOverflow));
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
            module Overflow Overflow block
              func Probe (uint:value) → uint
                !
                => ((result == result) && (value != 2147483647))
                return ((true ? value : value) + 1)
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
            module Overflow Overflow block
              func Probe (uint:value) → uint
                !
                => (((true ? value : value) + 1) == (value + 1))
                return value
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
            "let int:one = 1\n    let unused = (value + one)",
            "let one = 1\n    let unused = (value + one)",
            "let int:one = 1\n    let int:alias = one\n    let unused = (value + alias)",
            "let int:one = 1\n    if (value > 0)\n      let unused = (value + one)"
        })
        {
            var source = $$"""
                module Overflow Overflow block
                  func Probe (uint:value) → int
                    !
                    => ((result == 0) && (value != 4294967295L))
                    {{binding}}
                    return 0
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
            module Overflow Overflow block
              func Probe (int:value) → int
                !
                => {{NativeRowText(predicate)}}
                return value
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
            module Overflow Overflow block
              func Probe (int:value) → int
                !
                requires (value >= 0)
                => (result >= 0)
                return (value * value)
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
            module Overflow Overflow block
              func Probe (int:value) → int
                !
                => {{NativeRowText(predicate)}}
                return value
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
    // The name is a registered case identity of the #1421 determinism protocol (cases.json,
    // compiler-verifier) and of contract amendment 1.3.2, so it keeps "Proves" although the test
    // now asserts Assumed: the protocol never removes a registered case, and a rename would.
    // TODO(after 0.24): rename to GuardedArithmetic_IsAssumedWithoutEvaluatingUnselectedOverflow
    // under a new determinism registration (maintainer decision, 2026-10-08).
    public void GuardedArithmetic_ProvesWithoutEvaluatingUnselectedOverflow(string predicate)
    {
        var source = $$"""
            module Overflow Overflow block
              func Probe (int:value) → int
                !
                => {{NativeRowText(predicate)}}
                return value
            """;
        var options = VerifiedOptions();
        var compiled = Program.Compile(source, "overflow.calr", options);
        Assert.False(compiled.HasErrors, string.Join("; ", compiled.Diagnostics.Errors));
        // #1413 (amendment 1.3.2): `value + 1` can overflow for some i32 and overflow sensitivity is
        // decided by rule, without the guard, so the postcondition is Assumed [checked-arithmetic]
        // (the runtime check is kept); the unselected branch is still never evaluated.
        Assert.Contains(compiled.Diagnostics, diagnostic =>
            diagnostic.Verification is { Status: ProofStatus.Assumed, IsVacuous: false } verification
            && verification.Assumptions.SequenceEqual([Z3Verifier.CheckedArithmeticAssumption]));
        Assert.DoesNotContain(compiled.Diagnostics, diagnostic =>
            diagnostic.Verification is { Status: ProofStatus.Proven });
        var execution = TestHarness.Execute(source, "Probe", [int.MaxValue], options);
        Assert.Null(execution.Exception);
        Assert.Equal(int.MaxValue, execution.ReturnValue);
    }

    [Fact]
    public void ObligationSolver_CannotElideOverflowingIdentity()
    {
        const string source = """
            module Overflow Overflow block
              func Probe (int:value) → int
                !
                proof{"checked"} (((value + 1) - 1) == value)
                return value
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
            module Overflow Overflow block
              func Probe (int:left, int:right) → int
                !
                return (left {{operation}} right)
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
            module Overflow Overflow block
              func Probe ({{NativeRowText(inputType)}}:value) → {{NativeRowText(outputType)}}
                !
                return (({{NativeRowText(outputType)}})value)
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
            module Overflow Overflow block
              func Probe (int:value) → int
                !
                return (-value)
            """, [int.MinValue]);
        AssertOverflow("""
            module Overflow Overflow block
              func Probe (int:value) → char
                !
                return ((char)value)
            """, [65536]);
    }

    [Theory]
    [InlineData("+", int.MaxValue)]
    [InlineData("-", int.MinValue)]
    public void ExplicitIncrementAndDecrement_Trap(string operation, int input)
    {
        AssertOverflow($$"""
            module Overflow Overflow block
              func Probe (int:input) → int
                ! mut
                var int:value = input
                set value (value {{operation}} 1)
                return value
            """, [input]);
    }

    [Fact]
    public async Task ShippedExecutionProject_TrapsNativeOverflowAndPreservesCSharpInterop()
    {
        const string source = """
            module Overflow Overflow block
              func Probe (int:value) → int
                !
                return (value + 1)
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
