using System.Reflection;
using Calor.Compiler.Analysis.BugPatterns;
using Calor.Compiler.Binding;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests.Analysis;

public class BugPatternRuntimeOracleTests
{
    public static TheoryData<OracleCase> Corpus => new()
    {
        OracleCase.Throws(
            "DivisionUnsafe",
            """
            module DivisionUnsafe DivisionUnsafe block
              func Run () → int
                !
                var int:divisor = 1
                set divisor 0
                return (10 / divisor)
            """,
            DiagnosticCode.DivisionByZero,
            typeof(DivideByZeroException)),
        OracleCase.ThrowsWithArguments(
            "NonNegativePreconditionAllowsZero",
            """
            module NonNegativePreconditionAllowsZero NonNegativePreconditionAllowsZero block
              func Run (int:divisor) → int
                !
                requires (divisor >= 0)
                return (10 / divisor)
            """,
            DiagnosticCode.DivisionByZero,
            typeof(DivideByZeroException),
            [0]),
        OracleCase.Returns(
            "DivisionSafe",
            """
            module DivisionSafe DivisionSafe block
              func Run () → int
                !
                let int:divisor = 2
                return (10 / divisor)
            """,
            DiagnosticCode.DivisionByZero,
            5),
        OracleCase.Returns(
            "DivisionInConstantFalseBranch",
            """
            module DivisionInConstantFalseBranch DivisionInConstantFalseBranch block
              func Run () → int
                !
                let int:divisor = 0
                if (1 < 0)
                  return (10 / divisor)
                return 7
            """,
            DiagnosticCode.DivisionByZero,
            7),
        OracleCase.Returns(
            "ConditionalDeadArm",
            """
            module ConditionalDeadArm ConditionalDeadArm block
              func Run () → int
                !
                let int:divisor = 0
                return (true ? 7 : (10 / divisor))
            """,
            DiagnosticCode.DivisionByZero,
            7),
        OracleCase.ThrowsWithArguments(
            "ConditionalVariableBugArm",
            """
            module ConditionalVariableBugArm ConditionalVariableBugArm block
              func Run (bool:condition) → int
                !
                let int:divisor = 0
                return (condition ? 7 : (10 / divisor))
            """,
            DiagnosticCode.DivisionByZero,
            typeof(DivideByZeroException),
            [false]),
        OracleCase.Returns(
            "ShortCircuitFalseAnd",
            """
            module ShortCircuitFalseAnd ShortCircuitFalseAnd block
              func Run () → bool
                !
                let int:divisor = 0
                return (false && ((10 / divisor) == 0))
            """,
            DiagnosticCode.DivisionByZero,
            false),
        OracleCase.Returns(
            "ShortCircuitTrueOr",
            """
            module ShortCircuitTrueOr ShortCircuitTrueOr block
              func Run () → bool
                !
                let int:divisor = 0
                return (true || ((10 / divisor) == 0))
            """,
            DiagnosticCode.DivisionByZero,
            true),
        OracleCase.ThrowsWithArguments(
            "ShortCircuitVariableAnd",
            """
            module ShortCircuitVariableAnd ShortCircuitVariableAnd block
              func Run (bool:condition) → bool
                !
                let int:divisor = 0
                return (condition && ((10 / divisor) == 0))
            """,
            DiagnosticCode.DivisionByZero,
            typeof(DivideByZeroException),
            [true]),
        OracleCase.Returns(
            "DecimalDivisionSafe",
            """
            module DecimalDivisionSafe DecimalDivisionSafe block
              func Run () → decimal
                !
                let decimal:divisor = (0.5m - 0.2m)
                return (1m / divisor)
            """,
            DiagnosticCode.DivisionByZero,
            1m / 0.3m),
        OracleCase.Throws(
            "DecimalDivisionUnsafe",
            """
            module DecimalDivisionUnsafe DecimalDivisionUnsafe block
              func Run () → decimal
                !
                let decimal:divisor = (0.5m - 0.5m)
                return (1m / divisor)
            """,
            DiagnosticCode.DivisionByZero,
            typeof(DivideByZeroException)),
        OracleCase.Throws(
            "BoundsUnsafe",
            """
            module BoundsUnsafe BoundsUnsafe block
              func Run () → int
                !
                let int[]:items = new int[2]
                return items[2]
            """,
            DiagnosticCode.IndexOutOfBounds,
            typeof(IndexOutOfRangeException)),
        OracleCase.Returns(
            "BoundsSafe",
            """
            module BoundsSafe BoundsSafe block
              func Run () → int
                !
                let int[]:items = new int[] { 4, 7 }
                return items[1]
            """,
            DiagnosticCode.IndexOutOfBounds,
            7),
        OracleCase.Throws(
            "OverflowUnsafe",
            """
            module OverflowUnsafe OverflowUnsafe block
              func Run () → int
                !
                var int:value = 2147483647
                set value (value + 1)
                return value
            """,
            DiagnosticCode.IntegerOverflow,
            typeof(OverflowException)),
        OracleCase.Returns(
            "OverflowSafe",
            """
            module OverflowSafe OverflowSafe block
              func Run () → int
                !
                let int:value = (40 + 2)
                return value
            """,
            DiagnosticCode.IntegerOverflow,
            42),
        OracleCase.Throws(
            "MinValueRemainderOverflow",
            """
            module MinValueRemainderOverflow MinValueRemainderOverflow block
              func Run () → int
                !
                let int:value = -2147483648
                let int:divisor = -1
                return (value % divisor)
            """,
            DiagnosticCode.IntegerOverflow,
            typeof(OverflowException)),
        OracleCase.Throws(
            "DecimalToIntCastOverflow",
            """
            module DecimalToIntCastOverflow DecimalToIntCastOverflow block
              func Run () → int
                !
                let decimal:value = 79228162514264337593543950335m
                return ((int)value)
            """,
            DiagnosticCode.IntegerOverflow,
            typeof(OverflowException)),
        OracleCase.Throws(
            "NarrowingIntegralCastTraps",
            """
            module NarrowingIntegralCastTraps NarrowingIntegralCastTraps block
              func Run () → int
                !
                let long:value = 2147483648L
                return ((int)value)
            """,
            DiagnosticCode.IntegerOverflow,
            typeof(OverflowException)),
        OracleCase.ReturnsWithArguments(
            "SameSymbolSubtraction",
            """
            module SameSymbolSubtraction SameSymbolSubtraction block
              func Run (int:value) → int
                !
                return (value - value)
            """,
            DiagnosticCode.IntegerOverflow,
            0,
            [123]),
        OracleCase.ThrowsWithArguments(
            "SameSymbolZeroDivisor",
            """
            module SameSymbolZeroDivisor SameSymbolZeroDivisor block
              func Run (int:value) → int
                !
                return (1 / (value - value))
            """,
            DiagnosticCode.DivisionByZero,
            typeof(DivideByZeroException),
            [123]),
        OracleCase.Returns(
            "ShiftCountMaskedToZero",
            """
            module ShiftCountMaskedToZero ShiftCountMaskedToZero block
              func Run () → int
                !
                return (2147483647 << 32)
            """,
            DiagnosticCode.IntegerOverflow,
            int.MaxValue),
        OracleCase.Wraps(
            "ShiftCountMaskedToOne",
            """
            module ShiftCountMaskedToOne ShiftCountMaskedToOne block
              func Run () → int
                !
                return (2147483647 << 33)
            """,
            DiagnosticCode.IntegerOverflow,
            -2),
        OracleCase.Wraps(
            "NegativeShiftCountUsesLowBits",
            """
            module NegativeShiftCountUsesLowBits NegativeShiftCountUsesLowBits block
              func Run () → int
                !
                return (2147483647 << -1)
            """,
            DiagnosticCode.IntegerOverflow,
            int.MinValue),
        OracleCase.Throws(
            "NullableArrayUnsafe",
            """
            module NullableArrayUnsafe NullableArrayUnsafe block
              func Run () → int
                !
                let int[]:items = null
                return items.Length
            """,
            DiagnosticCode.NullDereference,
            typeof(NullReferenceException)),
        OracleCase.Returns(
            "NullableArraySafe",
            """
            module NullableArraySafe NullableArraySafe block
              func Run () → int
                !
                let int[]:items = new int[] { 1 }
                return items.Length
            """,
            DiagnosticCode.NullDereference,
            1),
        OracleCase.Returns(
            "NullFirstArrayGuardSafe",
            """
            module NullFirstArrayGuardSafe NullFirstArrayGuardSafe block
              func Run () → int
                !
                let int[]:items = new int[] { 1 }
                if (null == items)
                  return 0
                return items.Length
            """,
            DiagnosticCode.NullDereference,
            1),
        OracleCase.ThrowsWithArguments(
            "UnknownNullableArrayConditionalArm",
            """
            module UnknownNullableArrayConditionalArm UnknownNullableArrayConditionalArm block
              func Make private () → int[]
                return null

              func Run (bool:condition) → int
                !
                let int[]:known = new int[] { 1 }
                let int[]:items = (condition ? known : Make())
                return items.Length
            """,
            DiagnosticCode.NullDereference,
            typeof(NullReferenceException),
            [false]),
        OracleCase.ThrowsWithArguments(
            "ExcludedZeroDivisionOverflow",
            """
            module ExcludedZeroDivisionOverflow ExcludedZeroDivisionOverflow block
              func Run (int:divisor) → int
                !
                if (divisor == 0)
                  return 0
                return ((-2147483648 / divisor) + 1)
            """,
            DiagnosticCode.IntegerOverflow,
            typeof(OverflowException),
            [-1]),
        OracleCase.Throws(
            "OffByOneUnsafe",
            """
            module OffByOneUnsafe OffByOneUnsafe block
              func Run () → int
                !
                let int[]:items = new int[] { 1, 2 }
                let int:length = items.Length
                var int:sum = 0
                for i from 0 to length step 1
                  set sum (sum + items[i])
                return sum
            """,
            DiagnosticCode.OffByOne,
            typeof(IndexOutOfRangeException)),
        OracleCase.Returns(
            "OffByOneSafe",
            """
            module OffByOneSafe OffByOneSafe block
              func Run () → int
                !
                let int[]:items = new int[] { 1, 2 }
                let int:length = items.Length
                let int:upper = (length - 1)
                var int:sum = 0
                for i from 0 to upper step 1
                  set sum (sum + items[i])
                return sum
            """,
            DiagnosticCode.OffByOne,
            3),
        OracleCase.Throws(
            "ComputedNegativeStepUnsafe",
            """
            module ComputedNegativeStepUnsafe ComputedNegativeStepUnsafe block
              func Run () → int
                !
                let int[]:items = new int[] { 1, 2, 3 }
                let int:length = items.Length
                var int:sum = 0
                for i from length to 0 step (0 - 1)
                  set sum (sum + items[i])
                return sum
            """,
            DiagnosticCode.OffByOne,
            typeof(IndexOutOfRangeException)),
        OracleCase.Returns(
            "ComputedNegativeStepSafe",
            """
            module ComputedNegativeStepSafe ComputedNegativeStepSafe block
              func Run () → int
                !
                let int[]:items = new int[] { 1, 2, 3 }
                let int:length = items.Length
                let int:upper = (length - 1)
                var int:sum = 0
                for i from upper to 0 step (0 - 1)
                  set sum (sum + items[i])
                return sum
            """,
            DiagnosticCode.OffByOne,
            6),
        OracleCase.Returns(
            "StepTwoSkipsEndpoint",
            """
            module StepTwoSkipsEndpoint StepTwoSkipsEndpoint block
              func Run () → int
                !
                let int[]:items = new int[] { 1, 2, 3 }
                let int:length = items.Length
                var int:sum = 0
                for i from 0 to length step 2
                  set sum (sum + items[i])
                return sum
            """,
            DiagnosticCode.OffByOne,
            4),
        OracleCase.Throws(
            "StepTwoReachesEndpoint",
            """
            module StepTwoReachesEndpoint StepTwoReachesEndpoint block
              func Run () → int
                !
                let int[]:items = new int[] { 1, 2, 3, 4 }
                let int:length = items.Length
                var int:sum = 0
                for i from 0 to length step 2
                  set sum (sum + items[i])
                return sum
            """,
            DiagnosticCode.OffByOne,
            typeof(IndexOutOfRangeException)),
        OracleCase.ReturnsWithArguments(
            "CanonicalEmptyArrayLoop",
            """
            module CanonicalEmptyArrayLoop CanonicalEmptyArrayLoop block
              func Run (int[]:items) → int
                !
                let int:length = items.Length
                let int:upper = (length - 1)
                var int:sum = 0
                for i from 0 to upper step 1
                  set sum (sum + items[i])
                return sum
            """,
            DiagnosticCode.IndexOutOfBounds,
            0,
            [Array.Empty<int>()]),
        OracleCase.ThrowsWithArguments(
            "CanonicalUpperAdjacentUnsafe",
            """
            module CanonicalUpperAdjacentUnsafe CanonicalUpperAdjacentUnsafe block
              func Run (int[]:items) → int
                !
                let int:length = items.Length
                var int:sum = 0
                for i from 0 to length step 1
                  set sum (sum + items[i])
                return sum
            """,
            DiagnosticCode.IndexOutOfBounds,
            typeof(IndexOutOfRangeException),
            [new[] { 1, 2 }]),
        OracleCase.ThrowsWithArguments(
            "CanonicalLowerAdjacentUnsafe",
            """
            module CanonicalLowerAdjacentUnsafe CanonicalLowerAdjacentUnsafe block
              func Run (int[]:items) → int
                !
                let int:length = items.Length
                let int:upper = (length - 1)
                var int:sum = 0
                for i from -1 to upper step 1
                  set sum (sum + items[i])
                return sum
            """,
            DiagnosticCode.IndexOutOfBounds,
            typeof(IndexOutOfRangeException),
            [new[] { 1, 2 }]),
    };

    [Theory]
    [MemberData(nameof(Corpus))]
    public void GeneratedCSharpMatchesTypedFinding(OracleCase item)
    {
        var diagnostics = Analyze(item.Source);
        var found = diagnostics.Any(diagnostic =>
            diagnostic.Code == item.DiagnosticCode);
        Assert.Equal(item.ExpectFinding, found);

        var assembly = Compile(item.Source);
        var method = Assert.Single(
            assembly.GetTypes(),
            type => type.Name == $"{item.ModuleName}Module")
            .GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;

        if (item.ExceptionType != null)
        {
            var exception = Assert.Throws<TargetInvocationException>(
                () => method.Invoke(null, item.Arguments));
            Assert.IsType(item.ExceptionType, exception.InnerException);
        }
        else
        {
            var result = method.Invoke(null, item.Arguments);
            Assert.Equal(item.ExpectedResult, result);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConvertedIndexOperandsMatchCSharpEvaluationOrder(bool multidimensional)
    {
        var csharp = multidimensional
            ? """
              public static class Probe
              {
                  private static bool SetIndex(ref int index)
                  {
                      index = 2;
                      return true;
                  }

                  public static int Run()
                  {
                      int index = 0;
                      int[,] values = new int[1, 1];
                      return values[SetIndex(ref index) ? 0 : 0, index];
                  }
              }
              """
            : """
              public static class Probe
              {
                  private static bool SetIndex(ref int index)
                  {
                      index = 2;
                      return true;
                  }

                  public static int Run()
                  {
                      int index = 0;
                      int[] values = new int[1];
                      return (SetIndex(ref index) ? values : values)[index];
                  }
              }
              """;
        var conversion = new CSharpToCalorConverter().Convert(csharp);
        var ast = Assert.IsType<Calor.Compiler.Ast.ModuleNode>(conversion.Ast);

        var bindingDiagnostics = new DiagnosticBag();
        var bound = new Calor.Compiler.Binding.Binder(bindingDiagnostics).Bind(ast);
        Assert.False(
            bindingDiagnostics.HasErrors,
            string.Join(Environment.NewLine, bindingDiagnostics.Errors));
        var findings = new DiagnosticBag();
        new BugPatternRunner(
            findings,
            new BugPatternOptions
            {
                CheckDivisionByZero = false,
                CheckNullDereference = false,
                CheckOverflow = false,
                CheckOffByOne = false,
                CheckMissingPreconditions = false,
                ReportOnlyVerified = true,
                UseZ3Verification = false,
            }).Check(bound);

        var finding = Assert.Single(findings.Where(diagnostic =>
            diagnostic.Code == DiagnosticCode.IndexOutOfBounds));
        if (multidimensional)
            Assert.Contains("dimension 1", finding.Message, StringComparison.Ordinal);

        var generated = new CSharpEmitter().Emit(ast);
        var assembly = CompileGenerated(generated);
        var method = Assert.Single(
            assembly.GetTypes(),
            type => type.Name == "Probe")
            .GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;
        var exception = Assert.Throws<TargetInvocationException>(
            () => method.Invoke(null, null));
        Assert.IsType<IndexOutOfRangeException>(exception.InnerException);
    }

    private static DiagnosticBag Analyze(string source)
    {
        var diagnostics = new DiagnosticBag();
        var parser = new Parser(
            new Lexer(source, diagnostics).TokenizeAllForParser(),
            diagnostics);
        var module = parser.Parse();
        Assert.False(
            diagnostics.HasErrors,
            string.Join(Environment.NewLine, diagnostics.Errors));
        var bound = new Calor.Compiler.Binding.Binder(diagnostics).Bind(module);
        Assert.False(
            diagnostics.HasErrors,
            string.Join(Environment.NewLine, diagnostics.Errors));

        var findings = new DiagnosticBag();
        new BugPatternRunner(
            findings,
            new BugPatternOptions
            {
                CheckMissingPreconditions = false,
                ReportOnlyVerified = true,
                UseZ3Verification = false,
            }).Check(bound);
        return findings;
    }

    private static Assembly Compile(string source)
    {
        var result = Program.Compile(
            source,
            "bug-pattern-oracle.calr",
            new CompilationOptions
            {
                EnforceEffects = false,
                EnableTypeChecking = false,
            });
        Assert.False(
            result.HasErrors,
            string.Join(Environment.NewLine, result.Diagnostics.Errors));

        return CompileGenerated(result.GeneratedCode);
    }

    private static Assembly CompileGenerated(string generatedCode)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            GeneratedCSharpCompiler.GlobalUsingsPreamble + generatedCode);
        var compilation = CSharpCompilation.Create(
            $"BugPatternOracle_{Guid.NewGuid():N}",
            [syntaxTree],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(
            emit.Success,
            string.Join(Environment.NewLine, emit.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }

    public sealed record OracleCase(
        string ModuleName,
        string Source,
        string DiagnosticCode,
        bool ExpectFinding,
        Type? ExceptionType,
        object? ExpectedResult,
        object?[]? Arguments = null)
    {
        public static OracleCase Throws(
            string moduleName,
            string source,
            string diagnosticCode,
            Type exceptionType) =>
            new(
                moduleName,
                source,
                diagnosticCode,
                ExpectFinding: true,
                exceptionType,
                ExpectedResult: null);

        public static OracleCase ThrowsWithArguments(
            string moduleName,
            string source,
            string diagnosticCode,
            Type exceptionType,
            object?[] arguments) =>
            new(
                moduleName,
                source,
                diagnosticCode,
                ExpectFinding: true,
                exceptionType,
                ExpectedResult: null,
                arguments);

        public static OracleCase Returns(
            string moduleName,
            string source,
            string diagnosticCode,
            object expectedResult) =>
            new(
                moduleName,
                source,
                diagnosticCode,
                ExpectFinding: false,
                ExceptionType: null,
                expectedResult);

        public static OracleCase ReturnsWithArguments(
            string moduleName,
            string source,
            string diagnosticCode,
            object expectedResult,
            object?[] arguments) =>
            new(
                moduleName,
                source,
                diagnosticCode,
                ExpectFinding: false,
                ExceptionType: null,
                expectedResult,
                arguments);

        public static OracleCase Wraps(
            string moduleName,
            string source,
            string diagnosticCode,
            object wrappedResult) =>
            new(
                moduleName,
                source,
                diagnosticCode,
                ExpectFinding: true,
                ExceptionType: null,
                wrappedResult);

        public override string ToString() => ModuleName;
    }
}
