using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Calor.Compiler.TypeChecking;
using Microsoft.CodeAnalysis;
using Xunit;
using Binder = Calor.Compiler.Binding.Binder;

namespace Calor.Compiler.Tests;

[Collection("ProductionPipelineConsole")]
public sealed class NativeArrayDimensionTests
{
    private const string MatrixSource = """
        module MatrixSymmetry
          func CheckSymmetric (int[,]:matrix, int:n) → bool
            !
            requires n >= 0
            requires matrix.GetLength(0) == n && matrix.GetLength(1) == n
            requires forall int:i in 0..n, int:j in 0..n : matrix[i,j] == matrix[j,i]
            return true
        """;

    [Fact]
    public void MatrixDimensions_RetainActualArrayReceiverAndSelectedMetadataMethod()
    {
        foreach (var source in new[] { MatrixSource, new CalorEmitter().Emit(Parse(MatrixSource)) })
        {
            var diagnostics = new DiagnosticBag();
            var bound = new Binder(diagnostics).Bind(Parse(source));
            Assert.Empty(diagnostics);
            var calls = BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>().ToArray();
            Assert.Equal(2, calls.Length);
            foreach (var call in calls)
            {
                var receiver = Assert.IsType<BoundVariableExpression>(call.Receiver);
                var array = Assert.IsType<Binding.BoundTypes.ArrayBoundType>(receiver.MethodInputArrayType);
                Assert.Equal(2, array.Rank);
                Assert.True(call.ResolvedMetadataMethod is not null,
                    $"Receiver: {receiver.Type.DisplayString}, unknown={receiver.HasUnknownInferredType}, "
                    + $"element={array.ElementType.DisplayString} ({array.ElementType.GetType().Name}), "
                    + $"unresolvedArgs={call.HasUnresolvedArguments}, opaque={call.HasOpaqueOverloads}");
                Assert.Equal("GetLength", call.ResolvedMetadataMethod.Name);
                Assert.Equal(SpecialType.System_Array, call.ResolvedMetadataMethod.ContainingType.SpecialType);
                Assert.Equal(SpecialType.System_Int32, call.ResolvedMetadataMethod.ReturnType.SpecialType);
                Assert.Equal(SpecialType.System_Int32, Assert.Single(call.ResolvedMetadataMethod.Parameters).Type.SpecialType);
                Assert.False(call.HasUnknownInferredType);
                Assert.Equal("INT", TypeIdentity.Canonicalize(call.Type.DisplayString));
                Assert.Equal(new[] { 0 }, call.ArgumentParameterIndices);
                Assert.Equal("matrix", source.Substring(receiver.Span.Start, receiver.Span.Length));
            }
        }
    }

    [Fact]
    public void MatrixDimensions_OrdinaryCliChecksTheOriginalDimensionAndSymmetryContracts()
    {
        var directory = Path.Combine(Path.GetTempPath(), "calor-array-dimensions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var input = Path.Combine(directory, "MatrixSymmetry.calr");
            var output = Path.Combine(directory, "MatrixSymmetry.g.cs");
            File.WriteAllText(input, MatrixSource);
            var cli = CliTestHarness.RunCli(directory, "--no-telemetry", "-i", input, "-o", output);
            Assert.True(cli.ExitCode == 0, cli.StdOut + cli.StdErr);
            Assert.DoesNotContain("Calor0200", cli.StdOut + cli.StdErr);
            Assert.DoesNotContain("Calor0411", cli.StdOut + cli.StdErr);
            Assert.DoesNotContain("Calor0410", cli.StdOut + cli.StdErr);
            var generated = File.ReadAllText(output);
            Assert.Contains("matrix.GetLength(0)", generated);
            Assert.Contains("matrix.GetLength(1)", generated);
            Assert.Contains("matrix[i, j]", generated);
            Assert.Equal("True:True", Run(generated,
                "GlobalModule.CheckSymmetric(new int[,] { { 1, 2 }, { 2, 3 } }, 2)"
                + " + \":\" + GlobalModule.CheckSymmetric(new int[0, 0], 0)"));
            foreach (var expression in new[]
            {
                "GlobalModule.CheckSymmetric(new int[,] { { 1, 2 }, { 3, 4 } }, 2)",
                "GlobalModule.CheckSymmetric(new int[2, 3], 2)",
                "GlobalModule.CheckSymmetric(new int[3, 2], 2)",
                "GlobalModule.CheckSymmetric(new int[0, 0], -1)"
            })
                Assert.Equal("!ContractViolationException", Run(generated, expression));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(ContractMode.Debug)]
    [InlineData(ContractMode.Release)]
    [InlineData(ContractMode.Off)]
    public void MatrixContracts_RetainEnabledAndDisabledRuntimeBehavior(ContractMode mode)
    {
        var compiled = Compile(MatrixSource, mode);
        Assert.All(compiled.Diagnostics, diagnostic => Assert.Equal(DiagnosticCode.QuantifierNestedComplexity, diagnostic.Code));
        Assert.Equal("True", Run(compiled.GeneratedCode,
            "GlobalModule.CheckSymmetric(new int[,] { { 1, 2 }, { 2, 3 } }, 2)"));
        Assert.Equal(mode == ContractMode.Off ? "True" : "!ContractViolationException",
            Run(compiled.GeneratedCode, "GlobalModule.CheckSymmetric(new int[,] { { 1, 2 }, { 3, 4 } }, 2)"));
        Assert.Equal(mode == ContractMode.Off ? "True" : "!ContractViolationException",
            Run(compiled.GeneratedCode, "GlobalModule.CheckSymmetric(new int[2, 3], 2)"));
    }

    [Theory]
    [InlineData("int[]", "new int[3]", 1, 3)]
    [InlineData("int[,]", "new int[2, 3]", 2, 3)]
    [InlineData("int[,,]", "new int[2, 3, 4]", 3, 4)]
    [InlineData("string[,]", "new string[2, 3]", 2, 3)]
    [InlineData("byte[,]", "new byte[2, 3]", 2, 3)]
    [InlineData("int[,][]", "new int[2, 3][]", 2, 3)]
    [InlineData("int[][,]", "new int[3][,]", 1, 3)]
    public void DeclaredRanks_UseRealArrayMetadataAndKeepTheirShape(string type, string value, int rank, int expected)
    {
        var source = $$"""
            module ArrayDimensions
              func Length ({{type}}:matrix) → int
                !
                return matrix.GetLength({{rank - 1}})
            """;
        var compiled = Compile(source);
        Assert.Empty(compiled.Diagnostics);
        Assert.Equal(expected.ToString(), Run(compiled.GeneratedCode, $"GlobalModule.Length({value})"));
        var bound = new Binder(new DiagnosticBag()).Bind(Parse(source));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.Equal(rank, call.Receiver!.MethodInputArrayType!.Rank);
        Assert.Equal("System.Array", call.ResolvedMetadataMethod!.ContainingType.ToDisplayString());
    }

    [Theory]
    [InlineData("matrix.GetLength(1)", 3)]
    [InlineData("matrix.GetLength(dimension: 0)", 2)]
    [InlineData("matrix.GetUpperBound(1)", 2)]
    [InlineData("matrix.GetLowerBound(1)", 0)]
    [InlineData("matrix.GetLongLength(1)", 3)]
    [InlineData("Identity(matrix).GetLength(1)", 3)]
    [InlineData("(gate ? matrix : other).GetLength(1)", 3)]
    [InlineData("(matrix ?? other).GetLength(1)", 3)]
    public void ArrayDimensionReads_PreserveSelectedScalarResults(string expression, int expected)
    {
        var resultType = expression.Contains("GetLongLength", StringComparison.Ordinal) ? "long" : "int";
        var source = $$"""
            module ArrayReads
              func Identity (int[,]:matrix) → int[,]
                !
                return matrix
              func Read (int[,]:matrix, int[,]:other, bool:gate) → {{resultType}}
                !
                return {{expression}}
            """;
        var compiled = Compile(source);
        Assert.Equal(expected.ToString(), Run(compiled.GeneratedCode,
            "GlobalModule.Read(new int[2, 3], new int[4, 5], true)"));
    }

    [Theory]
    [InlineData("matrix.GetLength(\"zero\")")]
    [InlineData("matrix.GetLength(missing: 0)")]
    [InlineData("matrix.GetLength<int>(0)")]
    [InlineData("matrix.MissingDimension(0)")]
    [InlineData("matrix.SetValue(1, 0, 0)")]
    [InlineData("matrix.Initialize()")]
    public void UnresolvedOrNonScalarCalls_DoNotBorrowTheArrayPureRow(string expression)
    {
        var source = $$"""
            module ArrayBoundary
              func Read (int[,]:matrix) → void
                !
                {{expression}}
            """;
        var bound = new Binder(new DiagnosticBag()).Bind(Parse(source));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.Null(call.ResolvedMetadataMethod);
        var result = Program.Compile(source);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Theory]
    [InlineData("string:dimension", "matrix.GetLength(dimension)")]
    [InlineData("int:dimension", "matrix[0, 0, dimension]")]
    [InlineData("bool:dimension", "matrix[0, dimension]")]
    public void InvalidDimensionsAndRanks_AreRejected(string parameter, string expression)
    {
        var result = Program.Compile($$"""
            module InvalidDimensions
              func Read (int[,]:matrix, {{parameter}}) → int
                !
                return {{expression}}
            """);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code is DiagnosticCode.TypeMismatch or DiagnosticCode.ForbiddenEffect);
    }

    [Theory]
    [InlineData("int[]")]
    [InlineData("int[,,]")]
    [InlineData("int[][]")]
    public void SourceCalls_DoNotUnifyDifferentArrayRanks(string suppliedType)
    {
        var result = Program.Compile($$"""
            module RankMismatch
              func Need (int[,]:matrix) → int
                !
                return matrix.GetLength(0)
              func Read ({{suppliedType}}:matrix) → int
                !
                return Need(matrix)
            """);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code is DiagnosticCode.TypeMismatch or DiagnosticCode.NoMatchingOverload);
    }

    [Theory]
    [InlineData("class @String", "@String[,]", "")]
    [InlineData("class String", "@String[,]", "")]
    [InlineData("class String\n  using Text = @String", "Text[,]", "")]
    [InlineData("class @Int32", "@Int32[,]", "")]
    [InlineData("", "T[,]", "<T>")]
    [InlineData("", "Missing[,]", "")]
    public void UnsupportedArrayElements_DoNotBorrowMetadataIdentity(
        string declaration, string arrayType, string typeParameters)
    {
        var source = $$"""
            module ArrayIdentityBoundary
              {{declaration}}
              func Read{{typeParameters}} ({{arrayType}}:matrix) → int
                !
                return matrix.GetLength(0)
            """;
        var bound = new Binder(new DiagnosticBag()).Bind(Parse(source));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.Null(call.ResolvedMetadataMethod);
        var result = Program.Compile(source);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Theory]
    [InlineData("@String", "class String", false)]
    [InlineData("@String", "class @String", false)]
    [InlineData("int", "", true)]
    public void FieldArrayElements_UseCompletedDeclarationRegistration(
        string element, string declaration, bool resolved)
    {
        var source = $$"""
            module FieldArrayIdentity
              class Holder
                field {{element}}[,]:matrix private
                func Read public () → int
                  !
                  return matrix.GetLength(0)
              {{declaration}}
            """;
        var bound = new Binder(new DiagnosticBag()).Bind(Parse(source));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.Equal(resolved, call.ResolvedMetadataMethod is not null);
        var compiled = Program.Compile(source);
        Assert.Equal(!resolved, compiled.HasErrors);
        if (!resolved)
            Assert.Contains(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Fact]
    public void SameNamedSourceMethod_KeepsItsOwnIdentityAndEffects()
    {
        const string source = """
            module SourceDimensions
              class Matrix
                func GetLength public (int:dimension) → int
                  ! cw
                  System.Console.Write("source")
                  return dimension
              func Read (Matrix:matrix) → int
                !
                return matrix.GetLength(0)
            """;
        var bound = new Binder(new DiagnosticBag()).Bind(Parse(source));
        var call = BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>()
            .Single(call => call.ResolvedMethodName == "GetLength");
        Assert.Null(call.ResolvedMetadataMethod);
        Assert.NotNull(call.ResolvedSymbol);
        Assert.Equal("Matrix", call.ResolvedSymbol.ContainingTypeName);
        var result = Program.Compile(source);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Fact]
    public void NullableArrayReceiver_IsNotPromotedToANonNullProof()
    {
        var compiled = Compile("""
            module NullableDimensions
              func Read (int[,]?:matrix) → int
                !
                return matrix.GetLength(1)
            """);
        Assert.Equal("3", Run(compiled.GeneratedCode, "GlobalModule.Read(new int[2, 3])"));
        Assert.Equal("!NullReferenceException", Run(compiled.GeneratedCode, "GlobalModule.Read(null)"));
    }

    [Fact]
    public void OpenGenericMatrixPredicate_RemainsUnsupportedRatherThanAssumingPrimitiveEquality()
    {
        var result = Program.Compile("""
            module GenericMatrixPredicate
              func Check<T> (T[,]:matrix, int:n) → bool
                !
                requires forall int:i in 0..n, int:j in 0..n : matrix[i,j] == matrix[j,i]
                return true
            """);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.QuantifierRuntimeLoweringUnsupported);
        Assert.True(result.HasErrors);
    }

    [Fact]
    public void ArrayTypeIdentity_IncludesEveryRankAfterGenericSubstitution()
    {
        var matrix = new ArrayType(PrimitiveType.Int, 2);
        Assert.Equal(PrimitiveType.Int.SurfaceName + "[,]", matrix.SurfaceName);
        Assert.Equal(matrix, new ArrayType(PrimitiveType.Int, 2));
        Assert.NotEqual(matrix, new ArrayType(PrimitiveType.Int));
        Assert.NotEqual(matrix, new ArrayType(PrimitiveType.Int, 3));
        Assert.NotEqual(matrix, new ArrayType(new ArrayType(PrimitiveType.Int)));
        Assert.Equal(new ArrayType(new ArrayType(PrimitiveType.Int), 2),
            new ArrayType(new ArrayType(PrimitiveType.Int), 2));
        const string source = """
            module GenericArrayRanks
              func Preserve<T> (T[,]:matrix) → T[,]
                !
                return matrix
              func Read (int[,]:matrix) → int[,]
                !
                return Preserve(matrix)
            """;
        var compiled = Compile(source);
        Assert.Equal("3", Run(compiled.GeneratedCode, "GlobalModule.Read(new int[2, 3]).GetLength(1)"));
    }

    [Fact]
    public void ReceiverAndDimensionEffects_AreChargedAndExecuteInOrderOnce()
    {
        const string source = """
            module OrderedDimensions
              func Receiver (int[,]:matrix) → int[,]
                ! cw
                System.Console.Write("r")
                return matrix
              func Dimension () → int
                ! cw
                System.Console.Write("d")
                return 1
              func Read (int[,]:matrix) → int
                ! cw
                return Receiver(matrix).GetLength(Dimension())
            """;
        var compiled = Compile(source);
        Assert.Equal("rd:3", InteropScope.RefOutImplementationTests.Run(compiled.GeneratedCode + """

            public static class Probe
            {
                public static object? Run()
                {
                    var original = System.Console.Out;
                    using var output = new System.IO.StringWriter();
                    try
                    {
                        System.Console.SetOut(output);
                        var result = GlobalModule.Read(new int[2, 3]);
                        return output.ToString() + ":" + result;
                    }
                    finally { System.Console.SetOut(original); }
                }
            }
            """));
        var pure = Program.Compile(source.Replace(
            "func Read (int[,]:matrix) → int\n    ! cw",
            "func Read (int[,]:matrix) → int\n    !", StringComparison.Ordinal));
        Assert.Contains(pure.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
    }

    private static CompilationResult Compile(string source, ContractMode mode = ContractMode.Debug)
    {
        var result = Program.Compile(source, null, new CompilationOptions { ContractMode = mode });
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
        return result;
    }

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.Empty(diagnostics);
        return module;
    }

    private static string Run(string generated, string expression) =>
        InteropScope.RefOutImplementationTests.Run(generated + $$"""

            public static class Probe
            {
                public static object? Run() => {{expression}};
            }
            """);
}
