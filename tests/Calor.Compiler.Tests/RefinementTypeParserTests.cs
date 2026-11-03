using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Calor.Compiler.TypeChecking;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for refinement type parsing, emission, and type checking (Milestone 0).
/// </summary>
public sealed class RefinementTypeParserTests
{
    private static List<Token> Tokenize(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        return lexer.TokenizeAllForParser();
    }

    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    private static string ParseAndEmitCSharp(string calorSource)
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors,
            $"Parse errors:\n{string.Join("\n", diagnostics.Select(d => d.Message))}");

        AttachToolingIdentity(module, calorSource);
        var emitter = new CSharpEmitter();
        return emitter.Emit(module);
    }

    private static void AttachToolingIdentity(ModuleNode module, string source)
    {
        var root = Path.Combine(Path.GetTempPath(), "calor-refinement-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "active.calr");
            File.WriteAllText(file, source);
            var store = new Ids.DeclarationIdentityStore(root);
            store.Sync([file]);
            store.Attach(file, source, module);
            Assert.Equal(source, File.ReadAllText(file));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string ParseAndEmitCalor(string calorSource)
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors,
            $"Parse errors:\n{string.Join("\n", diagnostics.Select(d => d.Message))}");

        var emitter = new CalorEmitter();
        return emitter.Emit(module);
    }

    // ───── Tokenization ─────

    [Fact]
    public void Tokenize_RefinedType_ReturnsCorrectTokenKind()
    {
        var tokens = Tokenize("§RTYPE", out var diagnostics);
        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.RefinedType, tokens[0].Kind);
    }

    [Fact]
    public void Tokenize_EndRefinedType_ReturnsCorrectTokenKind()
    {
        var tokens = Tokenize("§/RTYPE", out var diagnostics);
        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.EndRefinedType, tokens[0].Kind);
    }

    [Fact]
    public void Tokenize_Proof_ReturnsCorrectTokenKind()
    {
        var tokens = Tokenize("§PROOF", out var diagnostics);
        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.Proof, tokens[0].Kind);
    }

    [Fact]
    public void Tokenize_RefinedType_IsKeyword()
    {
        var tokens = Tokenize("§RTYPE", out _);
        Assert.True(tokens[0].IsKeyword);
    }

    [Fact]
    public void Tokenize_Proof_IsKeyword()
    {
        var tokens = Tokenize("§PROOF", out _);
        Assert.True(tokens[0].IsKeyword);
    }

    // ───── Parsing: Refinement Type Definitions ─────

    [Fact]
    public void Parse_RefinementTypeDefinition_ParsesCorrectly()
    {
        var source = """
            §M{Test}
              §RTYPE{NatInt:i32} (>= # INT:0)
              §F{Main:pub}
                  §O{void}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors,
            $"Errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");
        Assert.Single(module.RefinementTypes);

        var rtype = module.RefinementTypes[0];
        Assert.Equal(rtype.LocalId, rtype.Id);
        Assert.Null(rtype.ToolingId);
        Assert.Equal("NatInt", rtype.Name);
        Assert.Equal("i32", rtype.BaseTypeName);
        Assert.NotNull(rtype.Predicate);
    }

    [Fact]
    public void Parse_RefinementTypeWithAndPredicate_ParsesCorrectly()
    {
        var source = """
            §M{Test}
              §RTYPE{Port:i32} (&& (>= # INT:1) (<= # INT:65535))
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors,
            $"Errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");
        Assert.Single(module.RefinementTypes);

        var rtype = module.RefinementTypes[0];
        Assert.Equal("Port", rtype.Name);
        Assert.Equal("i32", rtype.BaseTypeName);
        Assert.IsType<BinaryOperationNode>(rtype.Predicate);
    }

    [Fact]
    public void Parse_RefinementTypeWithLengthPredicate_ParsesCorrectly()
    {
        var source = """
            §M{Test}
              §RTYPE{NonEmpty:str} (> (len #) INT:0)
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors,
            $"Errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");
        Assert.Single(module.RefinementTypes);

        var rtype = module.RefinementTypes[0];
        Assert.Equal("NonEmpty", rtype.Name);
        Assert.Equal("str", rtype.BaseTypeName);
    }

    [Fact]
    public void Parse_MultipleRefinementTypes_ParsesAll()
    {
        var source = """
            §M{Test}
              §RTYPE{NatInt:i32} (>= # INT:0)
              §RTYPE{Port:i32} (&& (>= # INT:1) (<= # INT:65535))
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors,
            $"Errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");
        Assert.Equal(2, module.RefinementTypes.Count);
        Assert.Equal("NatInt", module.RefinementTypes[0].Name);
        Assert.Equal("Port", module.RefinementTypes[1].Name);
    }

    // ───── Parsing: SelfRefNode (#) ─────

    [Fact]
    public void Parse_SelfRefInsidePredicate_CreatesSelfRefNode()
    {
        var source = """
            §M{Test}
              §RTYPE{NatInt:i32} (>= # INT:0)
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        var rtype = module.RefinementTypes[0];

        // The predicate is (>= # INT:0), a BinaryOperationNode
        var binOp = Assert.IsType<BinaryOperationNode>(rtype.Predicate);
        Assert.IsType<SelfRefNode>(binOp.Left);
    }

    [Fact]
    public void Parse_SelfRefOutsidePredicate_ReportsError()
    {
        var source = """
            §M{Test}
              §F{Main:pub}
                  §O{void}
                  §R #
            """;

        var module = Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.SelfRefOutsidePredicate);
    }

    // ───── Parsing: Proof Obligations ─────

    [Fact]
    public void Parse_ProofObligation_ParsesCorrectly()
    {
        var source = """
            §M{Test}
              §F{Main:pub}
                  §I{i32:x}
                  §O{void}
                  §PROOF{positive} (>= x INT:0)
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors,
            $"Errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");

        var func = Assert.Single(module.Functions);
        Assert.Single(func.Body);

        var proof = Assert.IsType<ProofObligationNode>(func.Body[0]);
        Assert.Equal(proof.LocalId, proof.Id);
        Assert.Null(proof.ToolingId);
        Assert.Equal("positive", proof.Description);
        Assert.NotNull(proof.Condition);
    }

    [Fact]
    public void Parse_ProofObligationWithoutDescription_ParsesCorrectly()
    {
        var source = """
            §M{Test}
              §F{Main:pub}
                  §I{i32:x}
                  §O{void}
                  §PROOF{} (>= x INT:0)
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors,
            $"Errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");

        var func = Assert.Single(module.Functions);
        var proof = Assert.IsType<ProofObligationNode>(func.Body[0]);
        Assert.Equal(proof.LocalId, proof.Id);
        Assert.Null(proof.ToolingId);
        Assert.Null(proof.Description);
    }

    // ───── Parsing: Inline Refinement on Parameters ─────

    [Fact]
    public void Parse_InlineRefinementOnParameter_ParsesCorrectly()
    {
        var source = """
            §M{Test}
              §F{Main:pub}
                  §I{i32:age} | (>= # INT:0)
                  §O{void}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors,
            $"Errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");

        var func = Assert.Single(module.Functions);
        var param = Assert.Single(func.Parameters);
        Assert.Equal("age", param.Name);
        Assert.NotNull(param.InlineRefinement);
        // The base type name comes from the parameter's TypeName (as interpreted by AttributeHelper)
        Assert.Equal(param.TypeName, param.InlineRefinement.BaseTypeName);
        Assert.IsType<BinaryOperationNode>(param.InlineRefinement.Predicate);
    }

    [Fact]
    public void Parse_ParameterWithoutRefinement_HasNullInlineRefinement()
    {
        var source = """
            §M{Test}
              §F{Main:pub}
                  §I{i32:x}
                  §O{void}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        var func = Assert.Single(module.Functions);
        var param = Assert.Single(func.Parameters);
        Assert.Null(param.InlineRefinement);
    }

    // ───── C# Emission: Erasure ─────

    [Fact]
    public void CSharpEmit_RefinementTypeDefinition_ErasesToNothing()
    {
        var source = """
            §M{Test}
              §RTYPE{NatInt:i32} (>= # INT:0)
              §F{Main:pub}
                  §O{void}
            """;

        var csharp = ParseAndEmitCSharp(source);

        // Refinement type definition should be erased — no "NatInt" in C#
        Assert.DoesNotContain("RTYPE", csharp);
        Assert.DoesNotContain("§", csharp);
        // Function should still be emitted
        Assert.Contains("Main", csharp);
    }

    [Fact]
    public void CSharpEmit_ProofObligation_EmitsRuntimeGuard()
    {
        var source = """
            §M{Test}
              §F{Main:pub}
                  §I{i32:x}
                  §O{void}
                  §PROOF{positive} (>= x INT:0)
            """;

        var csharp = ParseAndEmitCSharp(source);

        Assert.Contains("if (!(x >= 0))", csharp);
        Assert.Matches(@"Proof obligation \[po_[0-9a-z]{12}: positive\] violated", csharp);
    }

    [Fact]
    public void CSharpEmit_ProofObligationWithoutDescription_EmitsRuntimeGuard()
    {
        var source = """
            §M{Test}
              §F{Main:pub}
                  §I{i32:x}
                  §O{void}
                  §PROOF{} (>= x INT:0)
            """;

        var csharp = ParseAndEmitCSharp(source);

        Assert.Contains("if (!(x >= 0))", csharp);
        Assert.Matches(@"Proof obligation \[po_[0-9a-z]{12}\] violated", csharp);
    }

    // ───── CalorEmitter: Round-trip ─────

    [Fact]
    public void CalorEmit_RefinementType_RoundTrips()
    {
        var source = """
            §M{Test}
              §RTYPE{NatInt:i32} (>= # INT:0)
            """;

        var calor = ParseAndEmitCalor(source);

        Assert.Contains("§RTYPE{NatInt:int}", calor);
        Assert.Contains("#", calor);
    }

    [Fact]
    public void CalorEmit_ProofObligation_RoundTrips()
    {
        var source = """
            §M{Test}
              §F{Main:pub}
                  §I{i32:x}
                  §O{void}
                  §PROOF{positive} (>= x INT:0)
            """;

        var calor = ParseAndEmitCalor(source);

        Assert.Contains("§PROOF{\"positive\"}", calor);
    }

    // ───── Type System ─────

    [Fact]
    public void TypeCheck_RefinementType_RegistersInEnvironment()
    {
        var source = """
            §M{Test}
              §RTYPE{NatInt:i32} (>= # INT:0)
              §F{Main:pub}
                  §O{void}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var checker = new TypeChecker(diagnostics);
        checker.Check(module);

        // No errors from type checking
        Assert.False(diagnostics.HasErrors,
            $"TypeCheck errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");
    }

    [Fact]
    public void RefinedType_IsSubtypeOfBaseType()
    {
        var baseType = PrimitiveType.Int;
        var dummyPredicate = new IntLiteralNode(new TextSpan(0, 0, 1, 1), 0);
        var refinedType = new RefinedType(baseType, ">= 0", dummyPredicate);

        // Refined type should NOT equal base type (different type)
        Assert.NotEqual<CalorType>(baseType, refinedType);

        // But RefinedType should have BaseType accessible
        Assert.Equal(baseType, refinedType.BaseType);
    }

    [Fact]
    public void RefinedType_EqualsWithSamePredicateText()
    {
        var baseType = PrimitiveType.Int;
        var dummyPredicate = new IntLiteralNode(new TextSpan(0, 0, 1, 1), 0);
        var type1 = new RefinedType(baseType, ">= 0", dummyPredicate);
        var type2 = new RefinedType(baseType, ">= 0", dummyPredicate);

        Assert.Equal(type1, type2);
    }

    [Fact]
    public void RefinedType_NotEqualsWithDifferentPredicate()
    {
        var baseType = PrimitiveType.Int;
        var dummyPredicate = new IntLiteralNode(new TextSpan(0, 0, 1, 1), 0);
        var type1 = new RefinedType(baseType, ">= 0", dummyPredicate);
        var type2 = new RefinedType(baseType, "> 0", dummyPredicate);

        Assert.NotEqual(type1, type2);
    }

    [Fact]
    public void RefinedType_NameIncludesBaseAndPredicate()
    {
        var baseType = PrimitiveType.Int;
        var dummyPredicate = new IntLiteralNode(new TextSpan(0, 0, 1, 1), 0);
        var refinedType = new RefinedType(baseType, ">= 0", dummyPredicate);

        Assert.Contains("INT", refinedType.Name);
        Assert.Contains(">= 0", refinedType.Name);
    }

    // ───── Feature Support ─────

    [Fact]
    public void FeatureSupport_RefinementType_IsFullySupported()
    {
        Assert.True(FeatureSupport.IsFullySupported("refinement-type"));
    }

    [Fact]
    public void FeatureSupport_ProofObligation_IsFullySupported()
    {
        Assert.True(FeatureSupport.IsFullySupported("proof-obligation"));
    }

    // ───── IdScanner ─────

    [Fact]
    public void IdScanner_RefinementType_CollectsId()
    {
        var source = """
            §M{Test}
              §RTYPE{NatInt:i32} (>= # INT:0)
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        AttachToolingIdentity(module, source);
        var scanner = new Ids.IdScanner();
        var entries = scanner.Scan(module, "test.calr");

        Assert.Contains(entries, e => e.Id == module.RefinementTypes.Single().Id
            && e.Kind == Ids.IdKind.RefinementType);
        Assert.All(entries, e => Assert.True(Ids.IdValidator.IsCanonicalId(e.Id)));
    }

    [Fact]
    public void IdScanner_ProofObligation_CollectsId()
    {
        var source = """
            §M{Test}
              §F{Main:pub}
                  §I{i32:x}
                  §O{void}
                  §PROOF{test} (>= x INT:0)
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        AttachToolingIdentity(module, source);
        var scanner = new Ids.IdScanner();
        var entries = scanner.Scan(module, "test.calr");

        Assert.Contains(entries, e => e.Id == module.Functions.Single().Body.OfType<ProofObligationNode>().Single().Id
            && e.Kind == Ids.IdKind.ProofObligation);
        Assert.All(entries, e => Assert.True(Ids.IdValidator.IsCanonicalId(e.Id)));
    }

    // ───── Negative Tests: Error Paths ─────

    [Fact]
    public void TypeCheck_DuplicateRefinementTypeName_ReportsError()
    {
        var source = """
            §M{Test}
              §RTYPE{NatInt:i32} (>= # INT:0)
              §RTYPE{NatInt:i32} (> # INT:0)
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var checker = new TypeChecker(diagnostics);
        checker.Check(module);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.RefinementDuplicateName);
    }

    [Fact]
    public void TypeCheck_UndefinedBaseType_ReportsError()
    {
        var source = """
            §M{Test}
              §RTYPE{MyType:nonexistent_type_xyz} (>= # INT:0)
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var checker = new TypeChecker(diagnostics);
        checker.Check(module);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.RefinementUndefinedBaseType);
    }

    [Fact]
    public void Parse_HashAtModuleLevel_ReportsError()
    {
        // # at module level is not inside a refinement predicate context
        // This tests that the error is reported even if it somehow appears
        // in a context where it can be parsed (e.g., inside a function return)
        var source = """
            §M{Test}
              §F{Main:pub}
                  §O{i32}
                  §R #
            """;

        var module = Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.SelfRefOutsidePredicate);
    }

    [Fact]
    public void Parse_HashInCallArgument_ReportsError()
    {
        var source = """
            §M{Test}
              §F{Main:pub}
                  §O{void}
                  §C{Console.WriteLine}
                    §A #
                  §/C
            """;

        var module = Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.SelfRefOutsidePredicate);
    }

    [Fact]
    public void Parse_HashInsideRefinementPredicate_NoError()
    {
        // Verify that # inside a refinement predicate does NOT report an error
        var source = """
            §M{Test}
              §RTYPE{NatInt:i32} (>= # INT:0)
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors,
            $"Unexpected errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.SelfRefOutsidePredicate);
    }

    // ───── CalorEmitter: Inline Refinement Round-trip ─────

    [Fact]
    public void CalorEmit_InlineRefinement_RoundTrips()
    {
        var source = """
            §M{Test}
              §F{Main:pub}
                  §I{i32:age} | (>= # INT:0)
                  §O{void}
            """;

        var calor = ParseAndEmitCalor(source);

        // The inline refinement should appear in the emitted Calor
        Assert.Contains("|", calor);
        Assert.Contains("#", calor);
        Assert.Contains(">=", calor);
    }

    [Fact]
    public void CalorEmit_ParameterWithoutRefinement_NoBar()
    {
        var source = """
            §M{Test}
              §F{Main:pub}
                  §I{i32:x}
                  §O{void}
            """;

        var calor = ParseAndEmitCalor(source);

        // No refinement separator should appear for a plain parameter
        Assert.DoesNotContain("|", calor);
    }

    // ───── Inline Refinement | Ambiguity ─────

    [Fact]
    public void Parse_InlineRefinementWithDefaultValue_ParsesBothCorrectly()
    {
        var source = """
            §M{Test}
              §F{Main:pub}
                  §I{i32:age} | (>= # INT:0) = INT:18
                  §O{void}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors,
            $"Errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");

        var func = Assert.Single(module.Functions);
        var param = Assert.Single(func.Parameters);

        // Both inline refinement and default value should be present
        Assert.NotNull(param.InlineRefinement);
        Assert.NotNull(param.DefaultValue);
        Assert.IsType<IntLiteralNode>(param.DefaultValue);
        Assert.Equal(18, ((IntLiteralNode)param.DefaultValue).Value);
    }

    [Fact]
    public void Parse_PipeInLispExpression_IsBitwiseOr()
    {
        // | inside a Lisp expression should still be bitwise OR, not refinement
        var source = """
            §M{Test}
              §F{Main:pub}
                  §I{i32:x}
                  §I{i32:y}
                  §O{i32}
                  §R (| x y)
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors,
            $"Errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");

        var func = Assert.Single(module.Functions);
        var ret = Assert.IsType<ReturnStatementNode>(func.Body[0]);
        Assert.NotNull(ret.Expression);
        // Should parse as bitwise OR operation
        Assert.IsType<BinaryOperationNode>(ret.Expression);
    }

    // ───── Roslyn Compilation Integration Test ─────

    [Fact]
    public void RoslynCompile_RefinementTypeErased_CSharpCompiles()
    {
        var source = """
            §M{Test}
              §RTYPE{NatInt:i32} (>= # INT:0)
              §F{Add:pub}
                  §I{i32:a}
                  §I{i32:b}
                  §O{i32}
                  §PROOF{result} (>= (+ a b) INT:0)
                  §R (+ a b)
            """;

        var csharp = ParseAndEmitCSharp(source);

        // Should not contain any Calor syntax
        Assert.DoesNotContain("§", csharp);
        Assert.DoesNotContain("RTYPE", csharp);

        // Unverified proof obligations remain executable runtime guards.
        Assert.Matches(@"Proof obligation \[po_[0-9a-z]{12}: result\] violated", csharp);

        // Verify it compiles with Roslyn
        var errors = RoslynCompile(csharp);
        Assert.Empty(errors);
    }

    [Fact]
    public void RoslynCompile_InlineRefinementErased_CSharpCompiles()
    {
        var source = """
            §M{Test}
              §F{Clamp:pub}
                  §I{i32:value} | (>= # INT:0)
                  §O{i32}
                  §R value
            """;

        var csharp = ParseAndEmitCSharp(source);

        // Inline refinement should be erased — just a normal int parameter
        // (| should not appear as refinement separator in emitted C#)
        Assert.DoesNotContain("__self__", csharp);
        Assert.DoesNotContain("| (>=", csharp);

        var errors = RoslynCompile(csharp);
        Assert.Empty(errors);
    }

    private static IReadOnlyList<Microsoft.CodeAnalysis.Diagnostic> RoslynCompile(string csharpSource)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(csharpSource);

        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Runtime.AssemblyTargetedPatchBandAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!,
                "System.Runtime.dll")),
        };

        var compilation = CSharpCompilation.Create(
            "RefinementRoundTripTest",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return compilation.GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Where(d => !d.GetMessage().Contains("'Calor'"))
            .ToArray();
    }
}
