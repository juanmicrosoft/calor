using System.Reflection;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.Compiler.TypeChecking;
using Calor.Compiler.Verification;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class NamedRefinementHygieneTests
{
    [Theory]
    [InlineData("(>= # 0)")]
    [InlineData("true")]
    [InlineData("(&& (>= # 0) (< # 10))")]
    [InlineData("(>= (? (< # 0) (- 0 #) #) 0)")]
    [InlineData("(>= (** # 2) 0)")]
    [InlineData("(>= (cast i64 #) 0)")]
    public void ClosedPredicates_CompileThroughValidatedPipeline(string predicate)
    {
        var result = Compile(predicate);
        Assert.False(result.HasErrors, Errors(result));
        Assert.NotEmpty(result.GeneratedCode);
    }

    [Theory]
    [InlineData("n")]
    [InlineData("result")]
    [InlineData("this")]
    [InlineData("Limits.Min")]
    [InlineData("this.Minimum")]
    [InlineData("System.Environment.TickCount")]
    [InlineData("predicate")]
    [InlineData("default")]
    public void NamedPredicate_RejectsFreeUseSiteNamesAtTheReference(string reference)
    {
        var result = Compile($"(> # {reference})");
        var diagnostic = Assert.Single(result.Diagnostics.Errors.Where(
            error => error.Code == DiagnosticCode.NamedRefinementNotClosed));
        Assert.Contains($"cannot capture '{reference}'", diagnostic.Message);
        Assert.Equal(reference, Source($"(> # {reference})")
            .Substring(diagnostic.Span.Start, diagnostic.Span.Length));
        Assert.Empty(result.GeneratedCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DisablingTypeChecking_DoesNotPermitUseSiteCapture(bool typeCheck)
    {
        var result = Compile("(> # n)", typeCheck);
        Assert.Contains(result.Diagnostics.Errors,
            error => error.Code == DiagnosticCode.NamedRefinementNotClosed);
        Assert.Empty(result.GeneratedCode);
    }

    [Fact]
    public void UnusedNamedRefinement_IsStillValidated()
    {
        var result = Program.Compile("""
            §M{Hygiene}
              §RTYPE{Unused:i32} (> # n)
              §F{Take:pub} (i32:n) -> i32
                §R n
            """, "hygiene.calr");
        Assert.Contains(result.Diagnostics.Errors,
            error => error.Code == DiagnosticCode.NamedRefinementNotClosed);
    }

    [Theory]
    [InlineData("(> # Limits.Min)")]
    [InlineData("(> # this.Minimum)")]
    [InlineData("§C{this.Check} §A # §/C")]
    public void ResolvableGlobalAndReceiverState_DoesNotBecomeADeclarationInput(string predicate)
    {
        var result = Program.Compile($$"""
            §M{Hygiene}
              §RTYPE{Above:i32} {{predicate}}
              §CL{Limits:pub}
                §FLD{i32:Min:pub:stat} 0
              §CL{Consumer:pub}
                §FLD{i32:Minimum:pub} 0
                §MT{Check:pub} (i32:value) -> bool
                  §R (> value 0)
                §MT{Take:pub} (Above:x) -> i32
                  §R x
            """, "hygiene.calr");
        Assert.Contains(result.Diagnostics.Errors,
            error => error.Code == DiagnosticCode.NamedRefinementNotClosed);
        Assert.Empty(result.GeneratedCode);
    }

    [Theory]
    [InlineData("§C{predicate} §A # §/C", "predicate")]
    [InlineData("§C{this.Check} §A # §/C", "this.Check")]
    [InlineData("§C{Limits.Check} §A # §/C", "Limits.Check")]
    [InlineData("§C{Math.Abs} §A # §/C", "Math.Abs")]
    public void Calls_AreRefusedAsUnmodeledBinding_NotMisidentifiedAsValueReferences(
        string call, string target)
    {
        var module = Parse(Source($"(> {call} 0)"));
        var diagnostics = new DiagnosticBag();
        new ContractVerifier(diagnostics).Verify(module);
        var diagnostic = Assert.Single(diagnostics.Errors);
        Assert.Equal(DiagnosticCode.NamedRefinementNotClosed, diagnostic.Code);
        Assert.Contains($"cannot use call '{target}'", diagnostic.Message);
        Assert.DoesNotContain("cannot capture", diagnostic.Message);
        Assert.Equal(target.Split('.').Last(), Source($"(> {call} 0)")
            .Substring(diagnostic.Span.Start, diagnostic.Span.Length));
    }

    [Theory]
    [InlineData("(forall ((n i32)) (-> (&& (>= n 0) (< n 2)) (>= # n)))")]
    [InlineData("(exists ((n i32)) (&& (>= n 0) (&& (< n 2) (>= # n))))")]
    public void QuantifierLocals_AreNotFreeCaptures_ButStillRequireARuntimeCertificate(string predicate)
    {
        var module = Parse(Source(predicate));
        var diagnostics = new DiagnosticBag();
        new TypeChecker(diagnostics).Check(module);
        new ContractVerifier(diagnostics).Verify(module);
        Assert.DoesNotContain(diagnostics.Errors,
            error => error.Code == DiagnosticCode.NamedRefinementNotClosed);
        var result = Compile(predicate);
        Assert.Contains(result.Diagnostics.Errors,
            error => error.Code == DiagnosticCode.QuantifierRuntimeLoweringUnsupported);
        Assert.DoesNotContain(result.Diagnostics.Errors,
            error => error.Code == DiagnosticCode.NamedRefinementNotClosed);
    }

    [Fact]
    public void QuantifierScope_DoesNotLeakIntoSiblingExpression()
    {
        var result = Compile("(&& (forall ((n i32)) (-> (&& (>= n 0) (< n 2)) (>= # n))) (> # n))");
        var diagnostic = Assert.Single(result.Diagnostics.Errors.Where(
            error => error.Code == DiagnosticCode.NamedRefinementNotClosed));
        Assert.Contains("cannot capture 'n'", diagnostic.Message);
    }

    [Theory]
    [InlineData("(> (len #) 0)")]
    [InlineData("(!= # null)")]
    [InlineData("(contains # \"a\")")]
    [InlineData("(starts # \"a\" :ordinal)")]
    [InlineData("(== (char-code (char-at # 0)) 97)")]
    public void NativeStringAndCharacterOperations_AreNotFreeCallNames(string predicate)
    {
        var result = Program.Compile($$"""
            §M{Hygiene}
              §RTYPE{Text:str} {{predicate}}
              §F{Take:pub} (Text:x) -> str
                §R x
            """, "hygiene.calr");
        Assert.False(result.HasErrors, Errors(result));
        Assert.NotEmpty(result.GeneratedCode);
    }

    [Theory]
    [InlineData("(== §CS{n} 1)")]
    [InlineData("(== (upper \"a\") \"A\")")]
    [InlineData("(starts \"abc\" \"a\")")]
    public void MutationRawCodeAndAmbientCulture_AreRefused(string predicate)
    {
        var result = Compile(predicate);
        Assert.Contains(result.Diagnostics.Errors,
            error => error.Code == DiagnosticCode.NamedRefinementNotClosed);
    }

    [Fact]
    public void RemovedUpdateSyntaxIsRejectedBeforeNamedRefinementHygiene()
    {
        const string predicate = "(== (inc #) 1)";
        var result = Compile(predicate);
        var diagnostic = Assert.Single(result.Diagnostics.Errors.Where(
            error => error.Code == DiagnosticCode.UnexpectedToken));
        Assert.Equal("Update operator 'inc' is removed. Use §ASSIGN with an explicit value instead.", diagnostic.Message);
        Assert.Equal("(inc #)", Source(predicate).Substring(diagnostic.Span.Start, diagnostic.Span.Length));
        Assert.DoesNotContain(result.Diagnostics.Errors,
            error => error.Code == DiagnosticCode.NamedRefinementNotClosed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProgrammaticAst_CannotBypassHygiene(bool typeChecker)
    {
        var span = new TextSpan(17, 4, 2, 8);
        ExpressionNode[] expressions =
        [
            new ReferenceNode(span, "n"),
            new ThisExpressionNode(span),
            new FieldAccessNode(span, new ThisExpressionNode(span), "Minimum"),
            new FieldAccessNode(span, new SelfRefNode(span), "Minimum"),
            new CallExpressionNode(span, "predicate", [new SelfRefNode(span)]),
            new RawCSharpExpressionNode(span, "n > 0"),
            new UnaryOperationNode(span, UnaryOperator.PreIncrement, new SelfRefNode(span)),
            new ForallExpressionNode(span, [new QuantifierVariableNode(span, "Limits.Min", "i32")],
                new ReferenceNode(span, "Limits.Min")),
            new ForallExpressionNode(span, [new QuantifierVariableNode(span, "n", "ReceiverType")],
                new ReferenceNode(span, "n")),
            new LambdaExpressionNode(span, "l1",
                [new LambdaParameterNode(span, "n", "i32")], null, false,
                new ReferenceNode(span, "n"), null, new AttributeCollection())
        ];
        foreach (var expression in expressions)
        {
            var module = Parse("§M{Hygiene}\n").With(update =>
                update.RefinementTypes =
                [new RefinementTypeNode(span, "r1", "Closed", "i32", expression, new AttributeCollection())]);
            var diagnostics = new DiagnosticBag();
            if (typeChecker)
                new TypeChecker(diagnostics).Check(module);
            else
                new ContractVerifier(diagnostics).Verify(module);
            var diagnostic = Assert.Single(diagnostics.Errors);
            Assert.Equal(DiagnosticCode.NamedRefinementNotClosed, diagnostic.Code);
            Assert.Equal(span, diagnostic.Span);
        }
    }

    [Fact]
    public void ComposedNamedRefinements_KeepTheirRuntimeGuards()
    {
        var result = Program.Compile("""
            §M{Hygiene}
              §RTYPE{Nat:i32} (>= # 0)
              §RTYPE{Small:Nat} (< # 10)
              §F{Take:pub} (Small:x) -> i32
                §R x
            """, "hygiene.calr");
        Assert.False(result.HasErrors, Errors(result));
        var method = Load(result.GeneratedCode).GetType("Hygiene.HygieneModule")!.GetMethod("Take")!;
        Assert.Equal(5, method.Invoke(null, [5]));
        foreach (var value in new[] { -1, 10 })
            Assert.IsType<ArgumentOutOfRangeException>(
                Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [value])).InnerException);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InlineCrossParameterPredicate_StillCompilesAndExecutes(bool typeCheck)
    {
        var result = Program.Compile("""
            §M{Hygiene}
              §F{Take:pub} (i32:n, i32:x | (> # n)) -> i32
                §R x
            """, "hygiene.calr", Options(typeCheck));
        Assert.False(result.HasErrors, Errors(result));
        Assert.NotEmpty(result.GeneratedCode);
        var method = Load(result.GeneratedCode).GetType("Hygiene.HygieneModule")!.GetMethod("Take")!;
        Assert.Equal(2, method.Invoke(null, [1, 2]));
        Assert.IsType<ArgumentOutOfRangeException>(
            Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [2, 1])).InnerException);
    }

    private static string Source(string predicate) => $$"""
        §M{Hygiene}
          §RTYPE{Above:i32} {{predicate}}
          §F{Take:pub} (i32:n, Above:x) -> i32
            §E{}
            §R x
        """;

    private static CompilationResult Compile(string predicate, bool typeCheck = true)
        => Program.Compile(Source(predicate), "hygiene.calr", Options(typeCheck));

    private static CompilationOptions Options(bool typeCheck) => new()
    {
        EnableTypeChecking = typeCheck,
        StatusWriter = TextWriter.Null
    };

    private static string Errors(CompilationResult result) => string.Join("\n", result.Diagnostics.Errors);

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors));
        return module;
    }

    private static Assembly Load(string code)
    {
        var compilation = CSharpCompilation.Create("Hygiene_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble + code)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }
}
