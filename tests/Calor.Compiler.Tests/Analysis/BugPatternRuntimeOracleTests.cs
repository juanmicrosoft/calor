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
            §M{DivisionUnsafe}
              §F{Run:pub} () -> i32
                §B{~divisor:i32} INT:1
                §ASSIGN divisor INT:0
                §R (/ INT:10 divisor)
            """,
            DiagnosticCode.DivisionByZero,
            typeof(DivideByZeroException)),
        OracleCase.ThrowsWithArguments(
            "NonNegativePreconditionAllowsZero",
            """
            §M{NonNegativePreconditionAllowsZero}
              §F{Run:pub} (i32:divisor) -> i32
                §Q (>= divisor INT:0)
                §R (/ INT:10 divisor)
            """,
            DiagnosticCode.DivisionByZero,
            typeof(DivideByZeroException),
            [0]),
        OracleCase.Returns(
            "DivisionSafe",
            """
            §M{DivisionSafe}
              §F{Run:pub} () -> i32
                §B{divisor:i32} INT:2
                §R (/ INT:10 divisor)
            """,
            DiagnosticCode.DivisionByZero,
            5),
        OracleCase.Returns(
            "DivisionInConstantFalseBranch",
            """
            §M{DivisionInConstantFalseBranch}
              §F{Run:pub} () -> i32
                §B{divisor:i32} INT:0
                §IF (< INT:1 INT:0)
                  §R (/ INT:10 divisor)
                §R INT:7
            """,
            DiagnosticCode.DivisionByZero,
            7),
        OracleCase.Returns(
            "ConditionalDeadArm",
            """
            §M{ConditionalDeadArm}
              §F{Run:pub} () -> i32
                §B{divisor:i32} INT:0
                §R (? BOOL:true INT:7 (/ INT:10 divisor))
            """,
            DiagnosticCode.DivisionByZero,
            7),
        OracleCase.ThrowsWithArguments(
            "ConditionalVariableBugArm",
            """
            §M{ConditionalVariableBugArm}
              §F{Run:pub} (bool:condition) -> i32
                §B{divisor:i32} INT:0
                §R (? condition INT:7 (/ INT:10 divisor))
            """,
            DiagnosticCode.DivisionByZero,
            typeof(DivideByZeroException),
            [false]),
        OracleCase.Returns(
            "ShortCircuitFalseAnd",
            """
            §M{ShortCircuitFalseAnd}
              §F{Run:pub} () -> bool
                §B{divisor:i32} INT:0
                §R (&& BOOL:false (== (/ INT:10 divisor) INT:0))
            """,
            DiagnosticCode.DivisionByZero,
            false),
        OracleCase.Returns(
            "ShortCircuitTrueOr",
            """
            §M{ShortCircuitTrueOr}
              §F{Run:pub} () -> bool
                §B{divisor:i32} INT:0
                §R (|| BOOL:true (== (/ INT:10 divisor) INT:0))
            """,
            DiagnosticCode.DivisionByZero,
            true),
        OracleCase.ThrowsWithArguments(
            "ShortCircuitVariableAnd",
            """
            §M{ShortCircuitVariableAnd}
              §F{Run:pub} (bool:condition) -> bool
                §B{divisor:i32} INT:0
                §R (&& condition (== (/ INT:10 divisor) INT:0))
            """,
            DiagnosticCode.DivisionByZero,
            typeof(DivideByZeroException),
            [true]),
        OracleCase.Returns(
            "DecimalDivisionSafe",
            """
            §M{DecimalDivisionSafe}
              §F{Run:pub} () -> dec
                §B{divisor:dec} (- DEC:0.5 DEC:0.2)
                §R (/ DEC:1 divisor)
            """,
            DiagnosticCode.DivisionByZero,
            1m / 0.3m),
        OracleCase.Throws(
            "DecimalDivisionUnsafe",
            """
            §M{DecimalDivisionUnsafe}
              §F{Run:pub} () -> dec
                §B{divisor:dec} (- DEC:0.5 DEC:0.5)
                §R (/ DEC:1 divisor)
            """,
            DiagnosticCode.DivisionByZero,
            typeof(DivideByZeroException)),
        OracleCase.Throws(
            "BoundsUnsafe",
            """
            §M{BoundsUnsafe}
              §F{Run:pub} () -> i32
                §B{[i32]:items} §ARR{i32:2}
                §R §IDX items INT:2
            """,
            DiagnosticCode.IndexOutOfBounds,
            typeof(IndexOutOfRangeException)),
        OracleCase.Returns(
            "BoundsSafe",
            """
            §M{BoundsSafe}
              §F{Run:pub} () -> i32
                §B{[i32]:items} §ARR{i32} §A INT:4 §A INT:7 §/ARR
                §R §IDX items INT:1
            """,
            DiagnosticCode.IndexOutOfBounds,
            7),
        OracleCase.Throws(
            "OverflowUnsafe",
            """
            §M{OverflowUnsafe}
              §F{Run:pub} () -> i32
                §B{~value:i32} INT:2147483647
                §ASSIGN value (+ value INT:1)
                §R value
            """,
            DiagnosticCode.IntegerOverflow,
            typeof(OverflowException)),
        OracleCase.Returns(
            "OverflowSafe",
            """
            §M{OverflowSafe}
              §F{Run:pub} () -> i32
                §B{value:i32} (+ INT:40 INT:2)
                §R value
            """,
            DiagnosticCode.IntegerOverflow,
            42),
        OracleCase.Throws(
            "MinValueRemainderOverflow",
            """
            §M{MinValueRemainderOverflow}
              §F{Run:pub} () -> i32
                §B{value:i32} INT:-2147483648
                §B{divisor:i32} INT:-1
                §R (% value divisor)
            """,
            DiagnosticCode.IntegerOverflow,
            typeof(OverflowException)),
        OracleCase.Throws(
            "DecimalToIntCastOverflow",
            """
            §M{DecimalToIntCastOverflow}
              §F{Run:pub} () -> i32
                §B{value:dec} DEC:79228162514264337593543950335
                §R (cast i32 value)
            """,
            DiagnosticCode.IntegerOverflow,
            typeof(OverflowException)),
        OracleCase.Throws(
            "NarrowingIntegralCastTraps",
            """
            §M{NarrowingIntegralCastTraps}
              §F{Run:pub} () -> i32
                §B{value:i64} INT:2147483648
                §R (cast i32 value)
            """,
            DiagnosticCode.IntegerOverflow,
            typeof(OverflowException)),
        OracleCase.ReturnsWithArguments(
            "SameSymbolSubtraction",
            """
            §M{SameSymbolSubtraction}
              §F{Run:pub} (i32:value) -> i32
                §R (- value value)
            """,
            DiagnosticCode.IntegerOverflow,
            0,
            [123]),
        OracleCase.ThrowsWithArguments(
            "SameSymbolZeroDivisor",
            """
            §M{SameSymbolZeroDivisor}
              §F{Run:pub} (i32:value) -> i32
                §R (/ INT:1 (- value value))
            """,
            DiagnosticCode.DivisionByZero,
            typeof(DivideByZeroException),
            [123]),
        OracleCase.Returns(
            "ShiftCountMaskedToZero",
            """
            §M{ShiftCountMaskedToZero}
              §F{Run:pub} () -> i32
                §R (<< INT:2147483647 INT:32)
            """,
            DiagnosticCode.IntegerOverflow,
            int.MaxValue),
        OracleCase.Wraps(
            "ShiftCountMaskedToOne",
            """
            §M{ShiftCountMaskedToOne}
              §F{Run:pub} () -> i32
                §R (<< INT:2147483647 INT:33)
            """,
            DiagnosticCode.IntegerOverflow,
            -2),
        OracleCase.Wraps(
            "NegativeShiftCountUsesLowBits",
            """
            §M{NegativeShiftCountUsesLowBits}
              §F{Run:pub} () -> i32
                §R (<< INT:2147483647 INT:-1)
            """,
            DiagnosticCode.IntegerOverflow,
            int.MinValue),
        OracleCase.Throws(
            "NullableArrayUnsafe",
            """
            §M{NullableArrayUnsafe}
              §F{Run:pub} () -> i32
                §B{items:i32[]} null
                §R §LEN items
            """,
            DiagnosticCode.NullDereference,
            typeof(NullReferenceException)),
        OracleCase.Returns(
            "NullableArraySafe",
            """
            §M{NullableArraySafe}
              §F{Run:pub} () -> i32
                §B{items:i32[]} §ARR{i32} §A INT:1 §/ARR
                §R §LEN items
            """,
            DiagnosticCode.NullDereference,
            1),
        OracleCase.Returns(
            "NullFirstArrayGuardSafe",
            """
            §M{NullFirstArrayGuardSafe}
              §F{Run:pub} () -> i32
                §B{items:i32[]} §ARR{i32} §A INT:1 §/ARR
                §IF (== null items)
                  §R INT:0
                §R §LEN items
            """,
            DiagnosticCode.NullDereference,
            1),
        OracleCase.ThrowsWithArguments(
            "UnknownNullableArrayConditionalArm",
            """
            §M{UnknownNullableArrayConditionalArm}
              §F{Make:priv} () -> i32[]
                §R null
              §F{Run:pub} (bool:condition) -> i32
                §B{known:i32[]} §ARR{i32} §A INT:1 §/ARR
                §B{items:i32[]} (? condition known §C{Make} §/C)
                §R §LEN items
            """,
            DiagnosticCode.NullDereference,
            typeof(NullReferenceException),
            [false]),
        OracleCase.ThrowsWithArguments(
            "ExcludedZeroDivisionOverflow",
            """
            §M{ExcludedZeroDivisionOverflow}
              §F{Run:pub} (i32:divisor) -> i32
                §IF (== divisor INT:0)
                  §R INT:0
                §R (+ (/ INT:-2147483648 divisor) INT:1)
            """,
            DiagnosticCode.IntegerOverflow,
            typeof(OverflowException),
            [-1]),
        OracleCase.Throws(
            "OffByOneUnsafe",
            """
            §M{OffByOneUnsafe}
              §F{Run:pub} () -> i32
                §B{[i32]:items} §ARR{i32} §A INT:1 §A INT:2 §/ARR
                §B{length:i32} §LEN items
                §B{~sum:i32} INT:0
                §L{i:0:length:1}
                  §ASSIGN sum (+ sum §IDX items i)
                §R sum
            """,
            DiagnosticCode.OffByOne,
            typeof(IndexOutOfRangeException)),
        OracleCase.Returns(
            "OffByOneSafe",
            """
            §M{OffByOneSafe}
              §F{Run:pub} () -> i32
                §B{[i32]:items} §ARR{i32} §A INT:1 §A INT:2 §/ARR
                §B{length:i32} §LEN items
                §B{upper:i32} (- length INT:1)
                §B{~sum:i32} INT:0
                §L{i:0:upper:1}
                  §ASSIGN sum (+ sum §IDX items i)
                §R sum
            """,
            DiagnosticCode.OffByOne,
            3),
        OracleCase.Throws(
            "ComputedNegativeStepUnsafe",
            """
            §M{ComputedNegativeStepUnsafe}
              §F{Run:pub} () -> i32
                §B{[i32]:items} §ARR{i32} §A INT:1 §A INT:2 §A INT:3 §/ARR
                §B{length:i32} §LEN items
                §B{~sum:i32} INT:0
                §L{i:length:0:(- 0 1)}
                  §ASSIGN sum (+ sum §IDX items i)
                §R sum
            """,
            DiagnosticCode.OffByOne,
            typeof(IndexOutOfRangeException)),
        OracleCase.Returns(
            "ComputedNegativeStepSafe",
            """
            §M{ComputedNegativeStepSafe}
              §F{Run:pub} () -> i32
                §B{[i32]:items} §ARR{i32} §A INT:1 §A INT:2 §A INT:3 §/ARR
                §B{length:i32} §LEN items
                §B{upper:i32} (- length INT:1)
                §B{~sum:i32} INT:0
                §L{i:upper:0:(- 0 1)}
                  §ASSIGN sum (+ sum §IDX items i)
                §R sum
            """,
            DiagnosticCode.OffByOne,
            6),
        OracleCase.Returns(
            "StepTwoSkipsEndpoint",
            """
            §M{StepTwoSkipsEndpoint}
              §F{Run:pub} () -> i32
                §B{[i32]:items} §ARR{i32} §A INT:1 §A INT:2 §A INT:3 §/ARR
                §B{length:i32} §LEN items
                §B{~sum:i32} INT:0
                §L{i:0:length:2}
                  §ASSIGN sum (+ sum §IDX items i)
                §R sum
            """,
            DiagnosticCode.OffByOne,
            4),
        OracleCase.Throws(
            "StepTwoReachesEndpoint",
            """
            §M{StepTwoReachesEndpoint}
              §F{Run:pub} () -> i32
                §B{[i32]:items} §ARR{i32} §A INT:1 §A INT:2 §A INT:3 §A INT:4 §/ARR
                §B{length:i32} §LEN items
                §B{~sum:i32} INT:0
                §L{i:0:length:2}
                  §ASSIGN sum (+ sum §IDX items i)
                §R sum
            """,
            DiagnosticCode.OffByOne,
            typeof(IndexOutOfRangeException)),
        OracleCase.ReturnsWithArguments(
            "CanonicalEmptyArrayLoop",
            """
            §M{CanonicalEmptyArrayLoop}
              §F{Run:pub} (i32[]:items) -> i32
                §B{length:i32} §LEN items
                §B{upper:i32} (- length INT:1)
                §B{~sum:i32} INT:0
                §L{i:0:upper:1}
                  §ASSIGN sum (+ sum §IDX items i)
                §R sum
            """,
            DiagnosticCode.IndexOutOfBounds,
            0,
            [Array.Empty<int>()]),
        OracleCase.ThrowsWithArguments(
            "CanonicalUpperAdjacentUnsafe",
            """
            §M{CanonicalUpperAdjacentUnsafe}
              §F{Run:pub} (i32[]:items) -> i32
                §B{length:i32} §LEN items
                §B{~sum:i32} INT:0
                §L{i:0:length:1}
                  §ASSIGN sum (+ sum §IDX items i)
                §R sum
            """,
            DiagnosticCode.IndexOutOfBounds,
            typeof(IndexOutOfRangeException),
            [new[] { 1, 2 }]),
        OracleCase.ThrowsWithArguments(
            "CanonicalLowerAdjacentUnsafe",
            """
            §M{CanonicalLowerAdjacentUnsafe}
              §F{Run:pub} (i32[]:items) -> i32
                §B{length:i32} §LEN items
                §B{upper:i32} (- length INT:1)
                §B{~sum:i32} INT:0
                §L{i:-1:upper:1}
                  §ASSIGN sum (+ sum §IDX items i)
                §R sum
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
