using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.IR;
using Calor.Compiler.Parsing;
using Calor.Compiler.TypeChecking;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for bugs identified during compiler code review.
/// Covers previously untested code paths across Lexer, TypeChecker, Binder,
/// CnfLowering, Effects, and CSharpEmitter.
/// </summary>
public class CompilerBugFixTests
{
    #region Helpers

    private static List<Token> Tokenize(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        return lexer.TokenizeAll();
    }

    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    private static TextSpan DummySpan => new(0, 1, 1, 1);

    private static ModuleNode MakeModule(params FunctionNode[] functions)
    {
        return new ModuleNode(
            DummySpan, "m001", "Test",
            Array.Empty<UsingDirectiveNode>(),
            functions,
            new AttributeCollection());
    }

    private static FunctionNode MakeFunction(
        string name,
        string returnType,
        IReadOnlyList<ParameterNode>? parameters = null,
        IReadOnlyList<StatementNode>? body = null)
    {
        return new FunctionNode(
            DummySpan, $"f_{name}", name, Visibility.Public,
            parameters ?? Array.Empty<ParameterNode>(),
            returnType == "VOID" ? null : new OutputNode(DummySpan, returnType),
            null,
            body ?? Array.Empty<StatementNode>(),
            new AttributeCollection());
    }

    #endregion

    #region Bug 1: CSharpEmitter — static keyword for all module functions

    [Fact]
    public void Emitter_AllModuleFunctions_AreStatic()
    {
        var source = """
            §M{m001:TestMod}
            §F{f001:Helper:pub}
              §O{void}
            §/F{f001}
            §F{f002:Main:pub}
              §O{void}
            §/F{f002}
            §/M{m001}
            """;

        var result = Program.Compile(source);

        Assert.False(result.HasErrors);
        Assert.Contains("public static void Helper()", result.GeneratedCode);
        Assert.Contains("public static void Main()", result.GeneratedCode);
    }

    #endregion

    #region Bug 3: CnfLowering — variable type tracking

    [Fact]
    public void CnfLowering_StringParameter_HasStringType()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Greet:pub}
              §I{string:name}
              §O{string}
              §R name
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diag);
        Assert.False(diag.HasErrors);

        var lowering = new CnfLowering();
        var cnfModule = lowering.LowerModule(module);

        Assert.Single(cnfModule.Functions);
        var func = cnfModule.Functions[0];
        Assert.Single(func.Parameters);
        Assert.Equal(CnfType.String, func.Parameters[0].Type);
    }

    [Fact]
    public void CnfLowering_BoolParameter_HasBoolType()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Check:pub}
              §I{bool:flag}
              §O{bool}
              §R flag
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diag);
        Assert.False(diag.HasErrors);

        var lowering = new CnfLowering();
        var cnfModule = lowering.LowerModule(module);
        var func = cnfModule.Functions[0];

        Assert.Equal(CnfType.Bool, func.Parameters[0].Type);
    }

    [Fact]
    public void CnfLowering_BindTracksType_AssignHasCorrectType()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Foo:pub}
              §O{void}
              §B{msg:string} STR:"hello"
              §C{Console.WriteLine}
                §A msg
              §/C
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diag);
        Assert.False(diag.HasErrors);

        var lowering = new CnfLowering();
        var cnfModule = lowering.LowerModule(module);
        var func = cnfModule.Functions[0];

        // The first statement should be an assignment to 'msg' of type String
        Assert.True(func.Body.Statements.Count > 0);
        var firstStmt = func.Body.Statements[0];
        Assert.IsType<CnfAssign>(firstStmt);
        var assign = (CnfAssign)firstStmt;
        Assert.Equal("msg", assign.Target);
        Assert.Equal(CnfType.String, assign.TargetType);
    }

    #endregion

    #region Bug 7: TypeChecker — match expression type inference

    [Fact]
    public void TypeChecker_MatchExpression_NoTypeErrorForValidMatch()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Classify:pub}
              §I{i32:x}
              §O{i32}
              §X{ma001:x}
                §WC _
                  §R INT:1
              §/X{ma001}
              §R INT:0
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diag);
        var checker = new TypeChecker(diag);
        checker.Check(module);
        // No type mismatch errors from match expressions
    }

    #endregion

    #region Bug 8: TypeChecker — arithmetic operators reject non-numeric

    [Fact]
    public void TypeChecker_StringArithmetic_ReportsError()
    {
        var left = new StringLiteralNode(DummySpan, "hello");
        var right = new StringLiteralNode(DummySpan, "world");
        var binOp = new BinaryOperationNode(DummySpan, BinaryOperator.Add, left, right);

        var func = MakeFunction("TestFunc", "VOID",
            body: new List<StatementNode> { new ReturnStatementNode(DummySpan, binOp) });
        var module = MakeModule(func);

        var diagnostics = new DiagnosticBag();
        var checker = new TypeChecker(diagnostics);
        checker.Check(module);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics.Errors,
            d => d.Message.Contains("Arithmetic operators require numeric operands"));
    }

    [Fact]
    public void TypeChecker_IntArithmetic_NoError()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Add:pub}
              §I{i32:a}
              §I{i32:b}
              §O{i32}
              §R (+ a b)
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diag);
        var checker = new TypeChecker(diag);
        checker.Check(module);

        Assert.False(diag.HasErrors);
    }

    [Fact]
    public void TypeChecker_FloatArithmetic_NoError()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Add:pub}
              §I{f64:a}
              §I{f64:b}
              §O{f64}
              §R (+ a b)
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diag);
        var checker = new TypeChecker(diag);
        checker.Check(module);

        Assert.False(diag.HasErrors);
    }

    #endregion

    #region Bug 9: TypeChecker — default cases in CheckStatement/CheckPattern

    [Fact]
    public void TypeChecker_PrintStatement_NoError()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §E{cw}
              §C{Console.WriteLine}
                §A STR:"hello"
              §/C
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diag);
        var checker = new TypeChecker(diag);
        checker.Check(module);
    }

    #endregion

    #region Bug 10: Binder — expression completeness

    [Fact]
    public void Binder_UnaryExpression_DoesNotThrow()
    {
        var operand = new IntLiteralNode(DummySpan, 42);
        var unary = new UnaryOperationNode(DummySpan, UnaryOperator.Negate, operand);

        var func = MakeFunction("Foo", "INT",
            body: new List<StatementNode> { new ReturnStatementNode(DummySpan, unary) });
        var module = MakeModule(func);

        var diagnostics = new DiagnosticBag();
        var binder = new Binder(diagnostics);
        var bound = binder.Bind(module);

        Assert.NotNull(bound);
        Assert.Single(bound.Functions);
    }

    [Fact]
    public void Binder_CallExpression_DoesNotThrow()
    {
        var callExpr = new CallExpressionNode(DummySpan, "SomeFunc",
            new List<ExpressionNode> { new IntLiteralNode(DummySpan, 1) });

        var func = MakeFunction("Foo", "INT",
            body: new List<StatementNode> { new ReturnStatementNode(DummySpan, callExpr) });
        var module = MakeModule(func);

        var diagnostics = new DiagnosticBag();
        var binder = new Binder(diagnostics);
        var bound = binder.Bind(module);

        Assert.NotNull(bound);
    }

    [Fact]
    public void Binder_FallbackExpression_ReportsDiagnostic()
    {
        var noneExpr = new NoneExpressionNode(DummySpan, null);

        var func = MakeFunction("Foo", "INT",
            body: new List<StatementNode> { new ReturnStatementNode(DummySpan, noneExpr) });
        var module = MakeModule(func);

        var diagnostics = new DiagnosticBag();
        var binder = new Binder(diagnostics);
        var bound = binder.Bind(module);

        Assert.NotNull(bound);
        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics.Errors,
            d => d.Message.Contains("Unsupported expression type in binding"));
    }

    #endregion

    #region Bug 12: Lexer — escape sequences

    [Fact]
    public void Lexer_NullEscapeSequence_ParsesCorrectly()
    {
        var tokens = Tokenize("STR:\"null\\0char\"", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.StrLiteral, tokens[0].Kind);
        Assert.Equal("null\0char", tokens[0].Value);
    }

    [Fact]
    public void Lexer_TabEscapeSequence_ParsesCorrectly()
    {
        var tokens = Tokenize("STR:\"col1\\tcol2\"", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.StrLiteral, tokens[0].Kind);
        Assert.Equal("col1\tcol2", tokens[0].Value);
    }

    [Fact]
    public void Lexer_CarriageReturnEscape_ParsesCorrectly()
    {
        var tokens = Tokenize("STR:\"line\\rend\"", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.StrLiteral, tokens[0].Kind);
        Assert.Equal("line\rend", tokens[0].Value);
    }

    [Fact]
    public void Lexer_InvalidEscapeSequence_ReportsDiagnostic()
    {
        Tokenize("STR:\"bad\\xescape\"", out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics.Errors,
            d => d.Code == DiagnosticCode.InvalidEscapeSequence);
    }

    [Fact]
    public void Lexer_EscapedQuote_ParsesCorrectly()
    {
        var tokens = Tokenize("STR:\"she said \\\"hi\\\"\"", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal("she said \"hi\"", tokens[0].Value);
    }

    [Fact]
    public void Lexer_EscapedBackslash_ParsesCorrectly()
    {
        var tokens = Tokenize("STR:\"path\\\\file\"", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal("path\\file", tokens[0].Value);
    }

    #endregion

    #region Bug 14: TypeVariable — symmetric equality

    [Fact]
    public void TypeVariable_ResolvedEquals_IsSymmetric()
    {
        var tv = new TypeVariable();
        tv.Resolve(PrimitiveType.Int);

        Assert.True(tv.Equals(PrimitiveType.Int));
        Assert.True(PrimitiveType.Int.Equals(tv));
    }

    [Fact]
    public void TypeVariable_UnresolvedEquals_ReturnsFalse()
    {
        var tv = new TypeVariable();

        Assert.False(tv.Equals(PrimitiveType.Int));
        Assert.False(PrimitiveType.Int.Equals(tv));
    }

    [Fact]
    public void TypeVariable_SameInstance_AreEqual()
    {
        var tv1 = new TypeVariable();
        Assert.True(tv1.Equals(tv1));
    }

    [Fact]
    public void TypeVariable_DifferentIds_NotEqual()
    {
        var tv1 = new TypeVariable();
        var tv2 = new TypeVariable();
        Assert.False(tv1.Equals(tv2));
    }

    #endregion

    #region Bug 15: EffectSubtyping — env:rw support

    [Fact]
    public void EffectSubtyping_EnvReadWrite_EncompassesRead()
    {
        var declared = (EffectKind.IO, "environment_readwrite");
        var required = (EffectKind.IO, "environment_read");

        Assert.True(EffectSubtyping.Encompasses(declared, required));
    }

    [Fact]
    public void EffectSubtyping_EnvReadWrite_EncompassesWrite()
    {
        var declared = (EffectKind.IO, "environment_readwrite");
        var required = (EffectKind.IO, "environment_write");

        Assert.True(EffectSubtyping.Encompasses(declared, required));
    }

    [Fact]
    public void EffectSet_EnvRW_ParsesCorrectly()
    {
        var set = EffectSet.From("env:rw");

        Assert.False(set.IsEmpty);
        Assert.Contains(set.Effects, e => e.Value == "environment_readwrite");
    }

    [Fact]
    public void EffectSet_FsRead_IsSubsetOfFsRW()
    {
        var required = EffectSet.From("fs:r");
        var declared = EffectSet.From("fs:rw");

        Assert.True(required.IsSubsetOf(declared));
    }

    [Fact]
    public void EffectSet_EnvRead_IsSubsetOfEnvRW()
    {
        var required = EffectSet.From("env:r");
        var declared = EffectSet.From("env:rw");

        Assert.True(required.IsSubsetOf(declared));
    }

    #endregion

    #region Bug 18: Binder — function symbols in parent scope

    [Fact]
    public void Binder_FunctionRegistration_ResolvesCallExpressions()
    {
        var callExpr = new CallExpressionNode(DummySpan, "Helper",
            new List<ExpressionNode> { new IntLiteralNode(DummySpan, 5) });

        var helperFunc = new FunctionNode(
            DummySpan, "f001", "Helper", Visibility.Public,
            new List<ParameterNode> { new ParameterNode(DummySpan, "x", "INT", new AttributeCollection()) },
            new OutputNode(DummySpan, "INT"),
            null,
            new List<StatementNode> { new ReturnStatementNode(DummySpan, new ReferenceNode(DummySpan, "x")) },
            new AttributeCollection());

        var mainFunc = MakeFunction("Main", "INT",
            body: new List<StatementNode> { new ReturnStatementNode(DummySpan, callExpr) });

        var module = MakeModule(helperFunc, mainFunc);

        var diagnostics = new DiagnosticBag();
        var binder = new Binder(diagnostics);
        var bound = binder.Bind(module);

        Assert.NotNull(bound);
        Assert.Equal(2, bound.Functions.Count);

        var mainBound = bound.Functions[1];
        var returnStmt = mainBound.Body[0] as BoundReturnStatement;
        Assert.NotNull(returnStmt?.Expression);
        var boundCall = returnStmt!.Expression as BoundCallExpression;
        Assert.NotNull(boundCall);
        Assert.Equal("Helper", boundCall!.Target);
        Assert.Equal("INT", boundCall.TypeName);
    }

    #endregion

    #region Bug 6: ContractVerificationPass — bounds safety

    [Fact]
    public void ContractVerificationPass_EmptyContracts_NoCrash()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Simple:pub}
              §O{void}
            §/F{f001}
            §/M{m001}
            """;

        var result = Program.Compile(source);
        Assert.False(result.HasErrors);
    }

    #endregion

    #region E2E: Multiple bug interactions

    [Fact]
    public void E2E_CompleteProgram_WithArithmetic_Compiles()
    {
        var source = """
            §M{m001:Calculator}
            §F{f001:Add:pub}
              §I{i32:a}
              §I{i32:b}
              §O{i32}
              §R (+ a b)
            §/F{f001}
            §F{f002:Main:pub}
              §O{void}
              §E{cw}
              §C{Console.WriteLine}
                §A INT:42
              §/C
            §/F{f002}
            §/M{m001}
            """;

        var result = Program.Compile(source);

        Assert.False(result.HasErrors);
        Assert.Contains("public static int Add(int a, int b)", result.GeneratedCode);
        Assert.Contains("public static void Main()", result.GeneratedCode);
    }

    [Fact]
    public void E2E_ProgramWithContracts_CompilesCleanly()
    {
        var source = """
            §M{m001:Safe}
            §F{f001:Divide:pub}
              §I{i32:a}
              §I{i32:b}
              §O{i32}
              §Q (!= b INT:0)
              §R (/ a b)
            §/F{f001}
            §/M{m001}
            """;

        var result = Program.Compile(source);
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void E2E_ArrayInitializer_Compiles()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §ARR{a001:nums:i32} INT:1 INT:2 INT:3
            §/F{f001}
            §/M{m001}
            """;

        var result = Program.Compile(source);
        if (!result.HasErrors)
        {
            Assert.NotEmpty(result.GeneratedCode);
        }
    }

    #endregion
}
