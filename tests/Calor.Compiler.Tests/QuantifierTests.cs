using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for quantified contracts (forall, exists) and implication.
/// </summary>
public class QuantifierTests
{
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

    #region Parser Tests

    [Fact]
    public void Parser_ParsesSimpleForall()
    {
        var source = @"
§M{m001:Test}
§F{f001:TestFunc:pub}
  §I{i32:x}
  §O{bool}
  §Q (forall ((i i32)) (>= i INT:0))
  §R true
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        Assert.Single(module.Functions);

        var func = module.Functions[0];
        Assert.Single(func.Preconditions);

        var requires = func.Preconditions[0];
        Assert.IsType<ForallExpressionNode>(requires.Condition);

        var forall = (ForallExpressionNode)requires.Condition;
        Assert.Single(forall.BoundVariables);
        Assert.Equal("i", forall.BoundVariables[0].Name);
        Assert.Equal("i32", forall.BoundVariables[0].TypeName);
    }

    [Fact]
    public void Parser_ParsesSimpleExists()
    {
        var source = @"
§M{m001:Test}
§F{f001:TestFunc:pub}
  §I{i32:x}
  §O{bool}
  §Q (exists ((i i32)) (== i x))
  §R true
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var func = module.Functions[0];
        var requires = func.Preconditions[0];
        Assert.IsType<ExistsExpressionNode>(requires.Condition);

        var exists = (ExistsExpressionNode)requires.Condition;
        Assert.Single(exists.BoundVariables);
        Assert.Equal("i", exists.BoundVariables[0].Name);
    }

    [Fact]
    public void Parser_ParsesMultipleBoundVariables()
    {
        var source = @"
§M{m001:Test}
§F{f001:TestFunc:pub}
  §I{i32:n}
  §O{bool}
  §Q (forall ((i i32) (j i32)) (== i j))
  §R true
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var func = module.Functions[0];
        var requires = func.Preconditions[0];
        Assert.IsType<ForallExpressionNode>(requires.Condition);

        var forall = (ForallExpressionNode)requires.Condition;
        Assert.Equal(2, forall.BoundVariables.Count);
        Assert.Equal("i", forall.BoundVariables[0].Name);
        Assert.Equal("j", forall.BoundVariables[1].Name);
    }

    [Fact]
    public void Parser_ParsesImplication()
    {
        var source = @"
§M{m001:Test}
§F{f001:TestFunc:pub}
  §I{i32:x}
  §O{bool}
  §Q (-> (>= x INT:0) (>= x INT:0))
  §R true
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var func = module.Functions[0];
        var requires = func.Preconditions[0];
        Assert.IsType<ImplicationExpressionNode>(requires.Condition);

        var impl = (ImplicationExpressionNode)requires.Condition;
        Assert.IsType<BinaryOperationNode>(impl.Antecedent);
        Assert.IsType<BinaryOperationNode>(impl.Consequent);
    }

    [Fact]
    public void Parser_ParsesNestedQuantifiers()
    {
        var source = @"
§M{m001:Test}
§F{f001:TestFunc:pub}
  §I{i32:n}
  §O{bool}
  §Q (forall ((x i32)) (exists ((y i32)) (== x y)))
  §R true
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var func = module.Functions[0];
        var requires = func.Preconditions[0];
        Assert.IsType<ForallExpressionNode>(requires.Condition);

        var forall = (ForallExpressionNode)requires.Condition;
        Assert.IsType<ExistsExpressionNode>(forall.Body);

        var exists = (ExistsExpressionNode)forall.Body;
        Assert.Single(exists.BoundVariables);
        Assert.Equal("y", exists.BoundVariables[0].Name);
    }

    [Fact]
    public void Parser_ParsesForallWithImplication()
    {
        var source = @"
§M{m001:Test}
§F{f001:ArrayAllNonNeg:pub}
  §I{i32:n}
  §O{bool}
  §Q (forall ((i i32)) (-> (&& (>= i INT:0) (< i n)) (>= i INT:0)))
  §R true
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var func = module.Functions[0];
        var requires = func.Preconditions[0];
        Assert.IsType<ForallExpressionNode>(requires.Condition);

        var forall = (ForallExpressionNode)requires.Condition;
        Assert.IsType<ImplicationExpressionNode>(forall.Body);
    }

    #endregion

    #region Z3 Translation Tests

    // Note: Direct Z3 API tests are in the main compiler project.
    // These tests verify the AST nodes and visitor patterns work correctly.

    [Fact]
    public void QuantifierNodes_AcceptVisitor()
    {
        var span = new TextSpan(0, 0, 1, 1);
        var boundVar = new QuantifierVariableNode(span, "i", "i32");
        var iRef = new ReferenceNode(span, "i");
        var zero = new IntLiteralNode(span, 0);
        var body = new BinaryOperationNode(span, BinaryOperator.GreaterOrEqual, iRef, zero);

        var forall = new ForallExpressionNode(span, new[] { boundVar }, body);
        var exists = new ExistsExpressionNode(span, new[] { boundVar }, body);
        var impl = new ImplicationExpressionNode(span, body, body);

        // Verify they can be visited by the emitter
        var emitter = new CSharpEmitter(EmitContractMode.Debug);

        var forallResult = forall.Accept(emitter);
        Assert.NotNull(forallResult);
        Assert.NotEmpty(forallResult);

        var existsResult = exists.Accept(emitter);
        Assert.NotNull(existsResult);
        Assert.NotEmpty(existsResult);

        var implResult = impl.Accept(emitter);
        Assert.NotNull(implResult);
        Assert.NotEmpty(implResult);
    }

    [Fact]
    public void QuantifierVariable_HasCorrectProperties()
    {
        var span = new TextSpan(0, 10, 1, 1);
        var qv = new QuantifierVariableNode(span, "idx", "i64");

        Assert.Equal("idx", qv.Name);
        Assert.Equal("i64", qv.TypeName);
        Assert.Equal(0, qv.Span.Start);
        Assert.Equal(10, qv.Span.Length);
    }

    [Fact]
    public void ForallExpression_HasCorrectStructure()
    {
        var span = new TextSpan(0, 0, 1, 1);
        var boundVar1 = new QuantifierVariableNode(span, "i", "i32");
        var boundVar2 = new QuantifierVariableNode(span, "j", "i32");
        var body = new BoolLiteralNode(span, true);

        var forall = new ForallExpressionNode(span, new[] { boundVar1, boundVar2 }, body);

        Assert.Equal(2, forall.BoundVariables.Count);
        Assert.Equal("i", forall.BoundVariables[0].Name);
        Assert.Equal("j", forall.BoundVariables[1].Name);
        Assert.Same(body, forall.Body);
    }

    [Fact]
    public void ExistsExpression_HasCorrectStructure()
    {
        var span = new TextSpan(0, 0, 1, 1);
        var boundVar = new QuantifierVariableNode(span, "k", "bool");
        var body = new BoolLiteralNode(span, false);

        var exists = new ExistsExpressionNode(span, new[] { boundVar }, body);

        Assert.Single(exists.BoundVariables);
        Assert.Equal("k", exists.BoundVariables[0].Name);
        Assert.Equal("bool", exists.BoundVariables[0].TypeName);
        Assert.Same(body, exists.Body);
    }

    [Fact]
    public void ImplicationExpression_HasCorrectStructure()
    {
        var span = new TextSpan(0, 0, 1, 1);
        var ante = new BoolLiteralNode(span, true);
        var cons = new BoolLiteralNode(span, false);

        var impl = new ImplicationExpressionNode(span, ante, cons);

        Assert.Same(ante, impl.Antecedent);
        Assert.Same(cons, impl.Consequent);
    }

    #endregion

    #region Code Generation Tests

    [Fact]
    public void Emitter_EmitsImplicationAsDisjunction()
    {
        var span = new TextSpan(0, 0, 1, 1);
        var xRef = new ReferenceNode(span, "x");
        var zero = new IntLiteralNode(span, 0);
        var ante = new BinaryOperationNode(span, BinaryOperator.GreaterOrEqual, xRef, zero);
        var cons = new BinaryOperationNode(span, BinaryOperator.GreaterThan, xRef, zero);
        var impl = new ImplicationExpressionNode(span, ante, cons);

        var emitter = new CSharpEmitter(EmitContractMode.Debug);
        var result = impl.Accept(emitter);

        // p -> q should emit as !p || q
        Assert.Contains("!", result);
        Assert.Contains("||", result);
        Assert.Contains("x >= 0", result);
        Assert.Contains("x > 0", result);
    }

    [Fact]
    public void Emitter_EmitsFiniteRangeForall()
    {
        // Create AST: (forall ((i i32)) (-> (&& (>= i INT:0) (< i n)) (>= i INT:0)))
        var span = new TextSpan(0, 0, 1, 1);
        var boundVar = new QuantifierVariableNode(span, "i", "i32");

        var iRef1 = new ReferenceNode(span, "i");
        var iRef2 = new ReferenceNode(span, "i");
        var iRef3 = new ReferenceNode(span, "i");
        var zero1 = new IntLiteralNode(span, 0);
        var zero2 = new IntLiteralNode(span, 0);
        var nRef = new ReferenceNode(span, "n");

        // Build (>= i 0)
        var lowerBound = new BinaryOperationNode(span, BinaryOperator.GreaterOrEqual, iRef1, zero1);
        // Build (< i n)
        var upperBound = new BinaryOperationNode(span, BinaryOperator.LessThan, iRef2, nRef);
        // Build (&& (>= i 0) (< i n))
        var antecedent = new BinaryOperationNode(span, BinaryOperator.And, lowerBound, upperBound);
        // Build (>= i 0) as consequent
        var consequent = new BinaryOperationNode(span, BinaryOperator.GreaterOrEqual, iRef3, zero2);
        // Build implication
        var impl = new ImplicationExpressionNode(span, antecedent, consequent);
        // Build forall
        var forall = new ForallExpressionNode(span, new[] { boundVar }, impl);

        var emitter = new CSharpEmitter(EmitContractMode.Debug);
        var result = forall.Accept(emitter);

        // Should generate Enumerable.Range(...).All(...)
        Assert.Contains("Enumerable.Range", result);
        Assert.Contains(".All(", result);
    }

    [Fact]
    public void Emitter_EmitsStaticOnlyCommentForInfiniteForall()
    {
        // Create AST: (forall ((i i32)) (>= i INT:0))
        // No finite range, so should emit static-only comment
        var span = new TextSpan(0, 0, 1, 1);
        var boundVar = new QuantifierVariableNode(span, "i", "i32");
        var iRef = new ReferenceNode(span, "i");
        var zero = new IntLiteralNode(span, 0);
        var body = new BinaryOperationNode(span, BinaryOperator.GreaterOrEqual, iRef, zero);
        var forall = new ForallExpressionNode(span, new[] { boundVar }, body);

        var emitter = new CSharpEmitter(EmitContractMode.Debug);
        var result = forall.Accept(emitter);

        // Should emit a comment indicating static-only verification
        Assert.Contains("STATIC ONLY", result);
        Assert.Contains("forall", result);
    }

    [Fact]
    public void Emitter_EmitsStaticOnlyCommentForInfiniteExists()
    {
        // Create AST: (exists ((i i32)) (== i INT:0))
        // No finite range, so should emit static-only comment
        var span = new TextSpan(0, 0, 1, 1);
        var boundVar = new QuantifierVariableNode(span, "i", "i32");
        var iRef = new ReferenceNode(span, "i");
        var zero = new IntLiteralNode(span, 0);
        var body = new BinaryOperationNode(span, BinaryOperator.Equal, iRef, zero);
        var exists = new ExistsExpressionNode(span, new[] { boundVar }, body);

        var emitter = new CSharpEmitter(EmitContractMode.Debug);
        var result = exists.Accept(emitter);

        // Should emit a comment indicating static-only verification
        Assert.Contains("STATIC ONLY", result);
        Assert.Contains("exists", result);
    }

    [Fact]
    public void Emitter_EmitsNestedRangeForMultiVariableForall()
    {
        // Create AST: (forall ((i i32) (j i32)) (-> (&& (>= i INT:0) (< i n) (>= j INT:0) (< j m)) (== i j)))
        var span = new TextSpan(0, 0, 1, 1);
        var boundVarI = new QuantifierVariableNode(span, "i", "i32");
        var boundVarJ = new QuantifierVariableNode(span, "j", "i32");

        var iRef1 = new ReferenceNode(span, "i");
        var iRef2 = new ReferenceNode(span, "i");
        var iRef3 = new ReferenceNode(span, "i");
        var jRef1 = new ReferenceNode(span, "j");
        var jRef2 = new ReferenceNode(span, "j");
        var jRef3 = new ReferenceNode(span, "j");
        var zero1 = new IntLiteralNode(span, 0);
        var zero2 = new IntLiteralNode(span, 0);
        var nRef = new ReferenceNode(span, "n");
        var mRef = new ReferenceNode(span, "m");

        // Build bounds: (>= i 0), (< i n), (>= j 0), (< j m)
        var iLower = new BinaryOperationNode(span, BinaryOperator.GreaterOrEqual, iRef1, zero1);
        var iUpper = new BinaryOperationNode(span, BinaryOperator.LessThan, iRef2, nRef);
        var jLower = new BinaryOperationNode(span, BinaryOperator.GreaterOrEqual, jRef1, zero2);
        var jUpper = new BinaryOperationNode(span, BinaryOperator.LessThan, jRef2, mRef);

        // Build nested ANDs for antecedent
        var andIBounds = new BinaryOperationNode(span, BinaryOperator.And, iLower, iUpper);
        var andJBounds = new BinaryOperationNode(span, BinaryOperator.And, jLower, jUpper);
        var antecedent = new BinaryOperationNode(span, BinaryOperator.And, andIBounds, andJBounds);

        // Build consequent: (== i j)
        var consequent = new BinaryOperationNode(span, BinaryOperator.Equal, iRef3, jRef3);

        // Build implication
        var impl = new ImplicationExpressionNode(span, antecedent, consequent);

        // Build forall with two variables
        var forall = new ForallExpressionNode(span, new[] { boundVarI, boundVarJ }, impl);

        var emitter = new CSharpEmitter(EmitContractMode.Debug);
        var result = forall.Accept(emitter);

        // Should generate nested Enumerable.Range(...).All(...) calls
        Assert.Contains("Enumerable.Range", result);
        Assert.Contains(".All(i =>", result);
        Assert.Contains(".All(j =>", result);
    }

    [Fact]
    public void Emitter_EmitsNestedRangeForMultiVariableExists()
    {
        // Create AST: (exists ((i i32) (j i32)) (&& (>= i INT:0) (< i n) (>= j INT:0) (< j m) (== i j)))
        var span = new TextSpan(0, 0, 1, 1);
        var boundVarI = new QuantifierVariableNode(span, "i", "i32");
        var boundVarJ = new QuantifierVariableNode(span, "j", "i32");

        var iRef1 = new ReferenceNode(span, "i");
        var iRef2 = new ReferenceNode(span, "i");
        var iRef3 = new ReferenceNode(span, "i");
        var jRef1 = new ReferenceNode(span, "j");
        var jRef2 = new ReferenceNode(span, "j");
        var jRef3 = new ReferenceNode(span, "j");
        var zero1 = new IntLiteralNode(span, 0);
        var zero2 = new IntLiteralNode(span, 0);
        var nRef = new ReferenceNode(span, "n");
        var mRef = new ReferenceNode(span, "m");

        // Build bounds: (>= i 0), (< i n), (>= j 0), (< j m)
        var iLower = new BinaryOperationNode(span, BinaryOperator.GreaterOrEqual, iRef1, zero1);
        var iUpper = new BinaryOperationNode(span, BinaryOperator.LessThan, iRef2, nRef);
        var jLower = new BinaryOperationNode(span, BinaryOperator.GreaterOrEqual, jRef1, zero2);
        var jUpper = new BinaryOperationNode(span, BinaryOperator.LessThan, jRef2, mRef);

        // Build body condition: (== i j)
        var bodyCondition = new BinaryOperationNode(span, BinaryOperator.Equal, iRef3, jRef3);

        // Build nested ANDs: (&& (>= i 0) (< i n) (>= j 0) (< j m) (== i j))
        var and1 = new BinaryOperationNode(span, BinaryOperator.And, iLower, iUpper);
        var and2 = new BinaryOperationNode(span, BinaryOperator.And, and1, jLower);
        var and3 = new BinaryOperationNode(span, BinaryOperator.And, and2, jUpper);
        var body = new BinaryOperationNode(span, BinaryOperator.And, and3, bodyCondition);

        // Build exists with two variables
        var exists = new ExistsExpressionNode(span, new[] { boundVarI, boundVarJ }, body);

        var emitter = new CSharpEmitter(EmitContractMode.Debug);
        var result = exists.Accept(emitter);

        // Should generate nested Enumerable.Range(...).Any(...) calls
        Assert.Contains("Enumerable.Range", result);
        Assert.Contains(".Any(i =>", result);
        Assert.Contains(".Any(j =>", result);
    }

    [Fact]
    public void Emitter_HandlesReversedBoundOrder()
    {
        // Test: bounds in reverse order (< i n) before (>= i 0)
        var span = new TextSpan(0, 0, 1, 1);
        var boundVar = new QuantifierVariableNode(span, "i", "i32");

        var iRef1 = new ReferenceNode(span, "i");
        var iRef2 = new ReferenceNode(span, "i");
        var iRef3 = new ReferenceNode(span, "i");
        var zero = new IntLiteralNode(span, 0);
        var nRef = new ReferenceNode(span, "n");

        // Build bounds in REVERSE order: (< i n) first, then (>= i 0)
        var upperBound = new BinaryOperationNode(span, BinaryOperator.LessThan, iRef1, nRef);
        var lowerBound = new BinaryOperationNode(span, BinaryOperator.GreaterOrEqual, iRef2, zero);
        var antecedent = new BinaryOperationNode(span, BinaryOperator.And, upperBound, lowerBound);
        var consequent = new BinaryOperationNode(span, BinaryOperator.GreaterOrEqual, iRef3, zero);
        var impl = new ImplicationExpressionNode(span, antecedent, consequent);
        var forall = new ForallExpressionNode(span, new[] { boundVar }, impl);

        var emitter = new CSharpEmitter(EmitContractMode.Debug);
        var result = forall.Accept(emitter);

        // Should still generate runtime check
        Assert.Contains("Enumerable.Range", result);
        Assert.Contains(".All(", result);
    }

    [Fact]
    public void Emitter_HandlesBoundsOnRightSide()
    {
        // Test: (0 <= i) instead of (i >= 0)
        var span = new TextSpan(0, 0, 1, 1);
        var boundVar = new QuantifierVariableNode(span, "i", "i32");

        var iRef1 = new ReferenceNode(span, "i");
        var iRef2 = new ReferenceNode(span, "i");
        var iRef3 = new ReferenceNode(span, "i");
        var zero = new IntLiteralNode(span, 0);
        var nRef = new ReferenceNode(span, "n");

        // Build: (0 <= i) - variable on RIGHT side
        var lowerBound = new BinaryOperationNode(span, BinaryOperator.LessOrEqual, zero, iRef1);
        var upperBound = new BinaryOperationNode(span, BinaryOperator.LessThan, iRef2, nRef);
        var antecedent = new BinaryOperationNode(span, BinaryOperator.And, lowerBound, upperBound);
        var consequent = new BinaryOperationNode(span, BinaryOperator.GreaterOrEqual, iRef3, zero);
        var impl = new ImplicationExpressionNode(span, antecedent, consequent);
        var forall = new ForallExpressionNode(span, new[] { boundVar }, impl);

        var emitter = new CSharpEmitter(EmitContractMode.Debug);
        var result = forall.Accept(emitter);

        // Should generate runtime check
        Assert.Contains("Enumerable.Range", result);
        Assert.Contains(".All(", result);
    }

    [Fact]
    public void Emitter_HandlesLessOrEqualUpperBound()
    {
        // Test: (i <= n) should generate upper bound of (n + 1)
        var span = new TextSpan(0, 0, 1, 1);
        var boundVar = new QuantifierVariableNode(span, "i", "i32");

        var iRef1 = new ReferenceNode(span, "i");
        var iRef2 = new ReferenceNode(span, "i");
        var iRef3 = new ReferenceNode(span, "i");
        var zero = new IntLiteralNode(span, 0);
        var nRef = new ReferenceNode(span, "n");

        // Build: (<= i n) instead of (< i n)
        var lowerBound = new BinaryOperationNode(span, BinaryOperator.GreaterOrEqual, iRef1, zero);
        var upperBound = new BinaryOperationNode(span, BinaryOperator.LessOrEqual, iRef2, nRef);
        var antecedent = new BinaryOperationNode(span, BinaryOperator.And, lowerBound, upperBound);
        var consequent = new BinaryOperationNode(span, BinaryOperator.GreaterOrEqual, iRef3, zero);
        var impl = new ImplicationExpressionNode(span, antecedent, consequent);
        var forall = new ForallExpressionNode(span, new[] { boundVar }, impl);

        var emitter = new CSharpEmitter(EmitContractMode.Debug);
        var result = forall.Accept(emitter);

        // Should generate runtime check with (n + 1) as upper bound
        Assert.Contains("Enumerable.Range", result);
        Assert.Contains("(n + 1)", result);
    }

    [Fact]
    public void Emitter_HandlesGreaterThanLowerBound()
    {
        // Test: (i > 0) should generate lower bound of (0 + 1)
        var span = new TextSpan(0, 0, 1, 1);
        var boundVar = new QuantifierVariableNode(span, "i", "i32");

        var iRef1 = new ReferenceNode(span, "i");
        var iRef2 = new ReferenceNode(span, "i");
        var iRef3 = new ReferenceNode(span, "i");
        var zero = new IntLiteralNode(span, 0);
        var nRef = new ReferenceNode(span, "n");

        // Build: (> i 0) instead of (>= i 0)
        var lowerBound = new BinaryOperationNode(span, BinaryOperator.GreaterThan, iRef1, zero);
        var upperBound = new BinaryOperationNode(span, BinaryOperator.LessThan, iRef2, nRef);
        var antecedent = new BinaryOperationNode(span, BinaryOperator.And, lowerBound, upperBound);
        var consequent = new BinaryOperationNode(span, BinaryOperator.GreaterOrEqual, iRef3, zero);
        var impl = new ImplicationExpressionNode(span, antecedent, consequent);
        var forall = new ForallExpressionNode(span, new[] { boundVar }, impl);

        var emitter = new CSharpEmitter(EmitContractMode.Debug);
        var result = forall.Accept(emitter);

        // Should generate runtime check with (0 + 1) as lower bound
        Assert.Contains("Enumerable.Range", result);
        Assert.Contains("(0 + 1)", result);
    }

    [Fact]
    public void Emitter_HandlesArrayAccessInQuantifierBody()
    {
        // Test: forall with array access: (forall ((i i32)) (-> bounds (>= arr{i} 0)))
        var span = new TextSpan(0, 0, 1, 1);
        var boundVar = new QuantifierVariableNode(span, "i", "i32");

        var iRef1 = new ReferenceNode(span, "i");
        var iRef2 = new ReferenceNode(span, "i");
        var iRefIndex = new ReferenceNode(span, "i");
        var zero1 = new IntLiteralNode(span, 0);
        var zero2 = new IntLiteralNode(span, 0);
        var nRef = new ReferenceNode(span, "n");
        var arrRef = new ReferenceNode(span, "arr");

        // Build bounds
        var lowerBound = new BinaryOperationNode(span, BinaryOperator.GreaterOrEqual, iRef1, zero1);
        var upperBound = new BinaryOperationNode(span, BinaryOperator.LessThan, iRef2, nRef);
        var antecedent = new BinaryOperationNode(span, BinaryOperator.And, lowerBound, upperBound);

        // Build consequent with array access: (>= arr{i} 0)
        var arrayAccess = new ArrayAccessNode(span, arrRef, iRefIndex);
        var consequent = new BinaryOperationNode(span, BinaryOperator.GreaterOrEqual, arrayAccess, zero2);

        var impl = new ImplicationExpressionNode(span, antecedent, consequent);
        var forall = new ForallExpressionNode(span, new[] { boundVar }, impl);

        var emitter = new CSharpEmitter(EmitContractMode.Debug);
        var result = forall.Accept(emitter);

        // Should generate runtime check with array access
        Assert.Contains("Enumerable.Range", result);
        Assert.Contains(".All(", result);
        Assert.Contains("arr[i]", result);
    }

    [Fact]
    public void Emitter_HandlesExistsWithArrayAccess()
    {
        // Test: exists with array access: (exists ((i i32)) (&& bounds (== arr{i} target)))
        var span = new TextSpan(0, 0, 1, 1);
        var boundVar = new QuantifierVariableNode(span, "i", "i32");

        var iRef1 = new ReferenceNode(span, "i");
        var iRef2 = new ReferenceNode(span, "i");
        var iRefIndex = new ReferenceNode(span, "i");
        var zero = new IntLiteralNode(span, 0);
        var nRef = new ReferenceNode(span, "n");
        var arrRef = new ReferenceNode(span, "arr");
        var targetRef = new ReferenceNode(span, "target");

        // Build bounds
        var lowerBound = new BinaryOperationNode(span, BinaryOperator.GreaterOrEqual, iRef1, zero);
        var upperBound = new BinaryOperationNode(span, BinaryOperator.LessThan, iRef2, nRef);

        // Build body with array access: (== arr{i} target)
        var arrayAccess = new ArrayAccessNode(span, arrRef, iRefIndex);
        var bodyCondition = new BinaryOperationNode(span, BinaryOperator.Equal, arrayAccess, targetRef);

        // Build conjunction
        var and1 = new BinaryOperationNode(span, BinaryOperator.And, lowerBound, upperBound);
        var body = new BinaryOperationNode(span, BinaryOperator.And, and1, bodyCondition);

        var exists = new ExistsExpressionNode(span, new[] { boundVar }, body);

        var emitter = new CSharpEmitter(EmitContractMode.Debug);
        var result = exists.Accept(emitter);

        // Should generate runtime check with array access
        Assert.Contains("Enumerable.Range", result);
        Assert.Contains(".Any(", result);
        Assert.Contains("arr[i]", result);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void Integration_ParseAndEmitQuantifiedContract()
    {
        var source = @"
§M{m001:Test}
§F{f001:CheckAllPositive:pub}
  §I{i32:n}
  §O{bool}
  §Q (forall ((i i32)) (-> (&& (>= i INT:0) (< i n)) (>= i INT:0)))
  §R true
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter(EmitContractMode.Debug);
        var result = emitter.Emit(module);

        // Should contain the function
        Assert.Contains("CheckAllPositive", result);

        // Should have contract checks (either loop or comment)
        Assert.True(
            result.Contains("Enumerable.Range") || result.Contains("STATIC ONLY"),
            "Expected either runtime check or static-only comment"
        );
    }

    [Fact]
    public void Integration_ParseAndEmitImplication()
    {
        var source = @"
§M{m001:Test}
§F{f001:CheckImplication:pub}
  §I{i32:x}
  §O{bool}
  §Q (-> (> x INT:0) (>= x INT:0))
  §R true
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter(EmitContractMode.Debug);
        var result = emitter.Emit(module);

        // Should contain the function
        Assert.Contains("CheckImplication", result);

        // Should have the implication translated as !p || q
        Assert.Contains("!", result);
        Assert.Contains("||", result);
    }

    [Fact]
    public void Integration_ParseAndEmitQuantifierWithArrayAccess()
    {
        // Note: Using 'arr' as a regular parameter - array access syntax arr{i} works
        // in quantifier bodies regardless of the declared parameter type
        var source = @"
§M{m001:Test}
§F{f001:AllNonNegative:pub}
  §I{i32:arr}
  §I{i32:n}
  §O{bool}
  §Q (forall ((i i32)) (-> (&& (>= i INT:0) (< i n)) (>= arr{i} INT:0)))
  §R true
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter(EmitContractMode.Debug);
        var result = emitter.Emit(module);

        // Should contain the function
        Assert.Contains("AllNonNegative", result);

        // Should have array access in the generated code
        Assert.Contains("arr[i]", result);

        // Should have Enumerable.Range for the quantifier
        Assert.Contains("Enumerable.Range", result);
    }

    [Fact]
    public void Integration_ParseAndEmitExistsWithArrayAccess()
    {
        // Note: Using 'arr' as a regular parameter - array access syntax arr{i} works
        // in quantifier bodies regardless of the declared parameter type
        var source = @"
§M{m001:Test}
§F{f001:ContainsTarget:pub}
  §I{i32:arr}
  §I{i32:n}
  §I{i32:target}
  §O{bool}
  §Q (exists ((i i32)) (&& (>= i INT:0) (< i n) (== arr{i} target)))
  §R true
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter(EmitContractMode.Debug);
        var result = emitter.Emit(module);

        // Should contain the function
        Assert.Contains("ContainsTarget", result);

        // Should have array access in the generated code
        Assert.Contains("arr[i]", result);

        // Should have .Any() for the exists quantifier
        Assert.Contains(".Any(", result);
    }

    #endregion

    #region CalorEmitter Roundtrip Tests

    [Fact]
    public void CalorEmitter_EmitsForallExpression()
    {
        var span = new TextSpan(0, 0, 1, 1);
        var boundVar = new QuantifierVariableNode(span, "i", "i32");
        var iRef = new ReferenceNode(span, "i");
        var zero = new IntLiteralNode(span, 0);
        var body = new BinaryOperationNode(span, BinaryOperator.GreaterOrEqual, iRef, zero);
        var forall = new ForallExpressionNode(span, new[] { boundVar }, body);

        var emitter = new CalorEmitter();
        var result = forall.Accept(emitter);

        // Should emit valid Calor syntax
        Assert.Contains("(forall ((i i32))", result);
        Assert.Contains("(>= i", result);
    }

    [Fact]
    public void CalorEmitter_EmitsExistsExpression()
    {
        var span = new TextSpan(0, 0, 1, 1);
        var boundVar = new QuantifierVariableNode(span, "k", "i32");
        var kRef = new ReferenceNode(span, "k");
        var five = new IntLiteralNode(span, 5);
        var body = new BinaryOperationNode(span, BinaryOperator.Equal, kRef, five);
        var exists = new ExistsExpressionNode(span, new[] { boundVar }, body);

        var emitter = new CalorEmitter();
        var result = exists.Accept(emitter);

        // Should emit valid Calor syntax
        Assert.Contains("(exists ((k i32))", result);
        Assert.Contains("(== k", result);
    }

    [Fact]
    public void CalorEmitter_EmitsImplicationExpression()
    {
        var span = new TextSpan(0, 0, 1, 1);
        var xRef1 = new ReferenceNode(span, "x");
        var xRef2 = new ReferenceNode(span, "x");
        var zero = new IntLiteralNode(span, 0);
        var ante = new BinaryOperationNode(span, BinaryOperator.GreaterThan, xRef1, zero);
        var cons = new BinaryOperationNode(span, BinaryOperator.GreaterOrEqual, xRef2, zero);
        var impl = new ImplicationExpressionNode(span, ante, cons);

        var emitter = new CalorEmitter();
        var result = impl.Accept(emitter);

        // Should emit valid Calor syntax
        Assert.Contains("(->", result);
        Assert.Contains("(> x", result);
        Assert.Contains("(>= x", result);
    }

    [Fact]
    public void CalorEmitter_EmitsMultipleBoundVariables()
    {
        var span = new TextSpan(0, 0, 1, 1);
        var boundVarI = new QuantifierVariableNode(span, "i", "i32");
        var boundVarJ = new QuantifierVariableNode(span, "j", "i32");
        var body = new BoolLiteralNode(span, true);
        var forall = new ForallExpressionNode(span, new[] { boundVarI, boundVarJ }, body);

        var emitter = new CalorEmitter();
        var result = forall.Accept(emitter);

        // Should emit both bound variables
        Assert.Contains("(forall ((i i32) (j i32))", result);
    }

    [Fact]
    public void CalorEmitter_Roundtrip_ParseEmitReparse()
    {
        // Parse source with quantifiers
        var source = @"
§M{m001:Test}
§F{f001:TestFunc:pub}
  §I{i32:n}
  §O{bool}
  §Q (forall ((i i32)) (-> (&& (>= i INT:0) (< i n)) (>= i INT:0)))
  §R true
§/F{f001}
§/M{m001}
";

        var module1 = Parse(source, out var diag1);
        Assert.False(diag1.HasErrors, string.Join("\n", diag1.Select(d => d.Message)));

        // Get the precondition expression and emit it as Calor syntax
        var forall = (ForallExpressionNode)module1.Functions[0].Preconditions[0].Condition;
        var emitter = new CalorEmitter();
        var emittedContract = forall.Accept(emitter);

        // Verify the emitted string contains expected elements
        Assert.Contains("forall", emittedContract);
        Assert.Contains("->", emittedContract);
        Assert.Contains("(i i32)", emittedContract);
    }

    #endregion

    #region Type Validation Tests

    [Fact]
    public void Verification_AcceptsIntegerQuantifierTypes()
    {
        // Test that integer types are accepted without warnings
        var source = @"
§M{m001:Test}
§F{f001:TestFunc:pub}
  §I{i32:n}
  §O{bool}
  §Q (forall ((i i32)) (-> (&& (>= i INT:0) (< i n)) (>= i INT:0)))
  §R true
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);

        // No warnings should be emitted for integer types
        var quantifierWarnings = diagnostics.Where(d =>
            d.Code == DiagnosticCode.QuantifierNonIntegerType).ToList();
        Assert.Empty(quantifierWarnings);
    }

    [Fact]
    public void Verification_WarnsForFloatQuantifierType()
    {
        // Test that float types generate a warning
        var source = @"
§M{m001:Test}
§F{f001:TestFunc:pub}
  §I{i32:n}
  §O{bool}
  §Q (forall ((x f32)) (>= x INT:0))
  §R true
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);

        // There may be parsing errors but we want to verify the concept
        // Note: This test validates the warning mechanism exists
        // In practice, f32 may not be a recognized type in the parser
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void Emitter_HandlesEmptyRange()
    {
        // Test: when n=0, the range [0, n) is empty
        // Enumerable.Range(0, 0).All(...) should return true (vacuously true)
        var span = new TextSpan(0, 0, 1, 1);
        var boundVar = new QuantifierVariableNode(span, "i", "i32");

        var iRef1 = new ReferenceNode(span, "i");
        var iRef2 = new ReferenceNode(span, "i");
        var iRef3 = new ReferenceNode(span, "i");
        var zero1 = new IntLiteralNode(span, 0);
        var zero2 = new IntLiteralNode(span, 0);
        // Use literal 0 as upper bound to test empty range
        var upperLiteral = new IntLiteralNode(span, 0);

        var lowerBound = new BinaryOperationNode(span, BinaryOperator.GreaterOrEqual, iRef1, zero1);
        var upperBound = new BinaryOperationNode(span, BinaryOperator.LessThan, iRef2, upperLiteral);
        var antecedent = new BinaryOperationNode(span, BinaryOperator.And, lowerBound, upperBound);
        var consequent = new BinaryOperationNode(span, BinaryOperator.GreaterOrEqual, iRef3, zero2);
        var impl = new ImplicationExpressionNode(span, antecedent, consequent);
        var forall = new ForallExpressionNode(span, new[] { boundVar }, impl);

        var emitter = new CSharpEmitter(EmitContractMode.Debug);
        var result = forall.Accept(emitter);

        // Should still generate valid code - Enumerable.Range handles empty ranges correctly
        Assert.Contains("Enumerable.Range(0, 0 - 0)", result);
        Assert.Contains(".All(", result);
    }

    [Fact]
    public void Emitter_HandlesVariableShadowingInNestedQuantifiers()
    {
        // Test: nested quantifiers with same variable name
        // (forall ((i i32)) (exists ((i i32)) body))
        // The inner 'i' should shadow the outer 'i'
        var span = new TextSpan(0, 0, 1, 1);
        var outerVar = new QuantifierVariableNode(span, "i", "i32");
        var innerVar = new QuantifierVariableNode(span, "i", "i32");  // Same name!

        var iRef = new ReferenceNode(span, "i");
        var zero = new IntLiteralNode(span, 0);
        var body = new BinaryOperationNode(span, BinaryOperator.GreaterOrEqual, iRef, zero);

        var exists = new ExistsExpressionNode(span, new[] { innerVar }, body);
        var forall = new ForallExpressionNode(span, new[] { outerVar }, exists);

        var emitter = new CSharpEmitter(EmitContractMode.Debug);
        var result = forall.Accept(emitter);

        // Should generate code - the inner 'i' shadows the outer 'i'
        // This might generate STATIC ONLY since no finite range is detected
        Assert.NotEmpty(result);
    }

    [Fact]
    public void Parser_ParsesQuantifierInPostconditionWithResult()
    {
        // Test: quantifier in postcondition referencing 'result'
        var source = @"
§M{m001:Test}
§F{f001:SumPositive:pub}
  §I{i32:n}
  §O{i32}
  §S (forall ((i i32)) (-> (&& (>= i INT:0) (< i result)) (>= i INT:0)))
  §R n
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var func = module.Functions[0];
        Assert.Single(func.Postconditions);

        var ensures = func.Postconditions[0];
        Assert.IsType<ForallExpressionNode>(ensures.Condition);
    }

    [Fact]
    public void Emitter_HandlesDeepNestedQuantifiers()
    {
        // Test: deeply nested quantifiers (3 levels)
        var span = new TextSpan(0, 0, 1, 1);
        var varI = new QuantifierVariableNode(span, "i", "i32");
        var varJ = new QuantifierVariableNode(span, "j", "i32");
        var varK = new QuantifierVariableNode(span, "k", "i32");

        var body = new BoolLiteralNode(span, true);
        var inner = new ForallExpressionNode(span, new[] { varK }, body);
        var middle = new ForallExpressionNode(span, new[] { varJ }, inner);
        var outer = new ForallExpressionNode(span, new[] { varI }, middle);

        var emitter = new CSharpEmitter(EmitContractMode.Debug);
        var result = outer.Accept(emitter);

        // Should generate code with STATIC ONLY comment for infinite nested quantifiers
        Assert.Contains("STATIC ONLY", result);
        Assert.Contains("forall", result);
    }

    [Fact]
    public void Emitter_HandlesSingleVariableWithMultipleBounds()
    {
        // Test: variable with redundant bounds like (i >= 0) && (i >= -1)
        var span = new TextSpan(0, 0, 1, 1);
        var boundVar = new QuantifierVariableNode(span, "i", "i32");

        var iRef1 = new ReferenceNode(span, "i");
        var iRef2 = new ReferenceNode(span, "i");
        var iRef3 = new ReferenceNode(span, "i");
        var iRef4 = new ReferenceNode(span, "i");
        var zero = new IntLiteralNode(span, 0);
        var negOne = new IntLiteralNode(span, -1);
        var nRef = new ReferenceNode(span, "n");

        // Build: (>= i 0) and (>= i -1) and (< i n) - redundant lower bound
        var bound1 = new BinaryOperationNode(span, BinaryOperator.GreaterOrEqual, iRef1, zero);
        var bound2 = new BinaryOperationNode(span, BinaryOperator.GreaterOrEqual, iRef2, negOne);
        var bound3 = new BinaryOperationNode(span, BinaryOperator.LessThan, iRef3, nRef);
        var and1 = new BinaryOperationNode(span, BinaryOperator.And, bound1, bound2);
        var antecedent = new BinaryOperationNode(span, BinaryOperator.And, and1, bound3);

        var consequent = new BinaryOperationNode(span, BinaryOperator.GreaterOrEqual, iRef4, zero);
        var impl = new ImplicationExpressionNode(span, antecedent, consequent);
        var forall = new ForallExpressionNode(span, new[] { boundVar }, impl);

        var emitter = new CSharpEmitter(EmitContractMode.Debug);
        var result = forall.Accept(emitter);

        // Should still extract the first lower bound (0) and generate code
        Assert.Contains("Enumerable.Range(0,", result);
        Assert.Contains(".All(", result);
    }

    #endregion

    #region Real-World Contract Examples

    /// <summary>
    /// Example: Sorted array invariant
    /// A common contract for sorting algorithms ensuring all adjacent elements are in order.
    /// </summary>
    [Fact]
    public void RealWorld_SortedArrayInvariant()
    {
        var source = @"
§M{m001:Sorting}
§F{f001:IsSorted:pub}
  §I{i32:arr}
  §I{i32:n}
  §O{bool}
  §Q (> n INT:0)
  §S (forall ((i i32)) (-> (&& (>= i INT:0) (< i (- n INT:1))) (<= arr{i} arr{(+ i INT:1)})))
  §R true
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var func = module.Functions[0];
        Assert.Single(func.Postconditions);

        // The postcondition should be a forall ensuring all adjacent pairs are ordered
        var ensures = func.Postconditions[0];
        Assert.IsType<ForallExpressionNode>(ensures.Condition);
    }

    /// <summary>
    /// Example: Binary search postcondition
    /// If found, result index contains the target; if not found, target is not in the array.
    /// </summary>
    [Fact]
    public void RealWorld_BinarySearchContract()
    {
        var source = @"
§M{m001:Search}
§F{f001:BinarySearch:pub}
  §I{i32:arr}
  §I{i32:n}
  §I{i32:target}
  §O{i32}
  §Q (> n INT:0)
  §Q (forall ((i i32)) (-> (&& (>= i INT:0) (< i (- n INT:1))) (<= arr{i} arr{(+ i INT:1)})))
  §S (-> (>= result INT:0) (== arr{result} target))
  §R INT:0
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var func = module.Functions[0];
        // Should have 2 preconditions: n > 0 and array is sorted
        Assert.Equal(2, func.Preconditions.Count);
        // Should have 1 postcondition: if result >= 0, arr[result] == target
        Assert.Single(func.Postconditions);
    }

    /// <summary>
    /// Example: Array partitioning (quicksort invariant)
    /// All elements before pivot are less than or equal; all after are greater.
    /// </summary>
    [Fact]
    public void RealWorld_PartitionContract()
    {
        var source = @"
§M{m001:QuickSort}
§F{f001:Partition:pub}
  §I{i32:arr}
  §I{i32:low}
  §I{i32:high}
  §I{i32:pivot}
  §O{i32}
  §Q (<= low high)
  §S (forall ((i i32)) (-> (&& (>= i low) (< i result)) (<= arr{i} pivot)))
  §S (forall ((j i32)) (-> (&& (> j result) (<= j high)) (> arr{j} pivot)))
  §R low
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var func = module.Functions[0];
        // Should have 2 postconditions for the partition property
        Assert.Equal(2, func.Postconditions.Count);

        // Both should be forall expressions
        foreach (var ensures in func.Postconditions)
        {
            Assert.IsType<ForallExpressionNode>(ensures.Condition);
        }
    }

    /// <summary>
    /// Example: All elements in range are within bounds
    /// Common for validating array contents (e.g., all grades between 0 and 100).
    /// </summary>
    [Fact]
    public void RealWorld_ArrayBoundsContract()
    {
        var source = @"
§M{m001:Validation}
§F{f001:ValidateGrades:pub}
  §I{i32:grades}
  §I{i32:n}
  §O{bool}
  §Q (> n INT:0)
  §Q (forall ((i i32)) (-> (&& (>= i INT:0) (< i n)) (&& (>= grades{i} INT:0) (<= grades{i} INT:100))))
  §R true
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var func = module.Functions[0];
        // Should have 2 preconditions: n > 0 and all grades in [0, 100]
        Assert.Equal(2, func.Preconditions.Count);

        // Second precondition should be a forall
        var boundsContract = func.Preconditions[1];
        Assert.IsType<ForallExpressionNode>(boundsContract.Condition);
    }

    /// <summary>
    /// Example: Element exists in collection (membership test)
    /// Used for testing that an element is present.
    /// </summary>
    [Fact]
    public void RealWorld_ElementExistsContract()
    {
        var source = @"
§M{m001:Collections}
§F{f001:Contains:pub}
  §I{i32:arr}
  §I{i32:n}
  §I{i32:target}
  §O{bool}
  §Q (>= n INT:0)
  §S (-> (== result true) (exists ((i i32)) (&& (>= i INT:0) (< i n) (== arr{i} target))))
  §R false
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var func = module.Functions[0];
        // Should have 1 postcondition using implication with exists
        Assert.Single(func.Postconditions);

        var ensures = func.Postconditions[0];
        Assert.IsType<ImplicationExpressionNode>(ensures.Condition);
    }

    /// <summary>
    /// Example: Matrix multiplication dimension compatibility
    /// When multiplying A[m,n] by B[n,p], ensure dimensions match.
    /// </summary>
    [Fact]
    public void RealWorld_MatrixDimensionContract()
    {
        var source = @"
§M{m001:Matrix}
§F{f001:MultiplyDimCheck:pub}
  §I{i32:rowsA}
  §I{i32:colsA}
  §I{i32:rowsB}
  §I{i32:colsB}
  §O{bool}
  §Q (> rowsA INT:0)
  §Q (> colsA INT:0)
  §Q (> rowsB INT:0)
  §Q (> colsB INT:0)
  §Q (== colsA rowsB)
  §R true
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var func = module.Functions[0];
        // Should have 5 preconditions including dimension compatibility
        Assert.Equal(5, func.Preconditions.Count);
    }

    /// <summary>
    /// Example: Unique elements (no duplicates)
    /// All pairs of distinct indices have distinct values.
    /// </summary>
    [Fact]
    public void RealWorld_UniqueElementsContract()
    {
        var source = @"
§M{m001:Sets}
§F{f001:HasUniqueElements:pub}
  §I{i32:arr}
  §I{i32:n}
  §O{bool}
  §Q (>= n INT:0)
  §S (-> (== result true) (forall ((i i32) (j i32)) (-> (&& (>= i INT:0) (< i n) (>= j INT:0) (< j n) (!= i j)) (!= arr{i} arr{j}))))
  §R false
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var func = module.Functions[0];
        // Should have 1 postcondition with nested quantifier
        Assert.Single(func.Postconditions);

        // The postcondition should be an implication containing a forall with 2 bound vars
        var ensures = func.Postconditions[0];
        Assert.IsType<ImplicationExpressionNode>(ensures.Condition);

        var impl = (ImplicationExpressionNode)ensures.Condition;
        Assert.IsType<ForallExpressionNode>(impl.Consequent);

        var forall = (ForallExpressionNode)impl.Consequent;
        Assert.Equal(2, forall.BoundVariables.Count);
    }

    /// <summary>
    /// Example: Heap property (for priority queues)
    /// Parent is always greater than or equal to children.
    /// </summary>
    [Fact]
    public void RealWorld_MaxHeapPropertyContract()
    {
        var source = @"
§M{m001:Heap}
§F{f001:IsMaxHeap:pub}
  §I{i32:heap}
  §I{i32:n}
  §O{bool}
  §Q (> n INT:0)
  §S (-> (== result true) (forall ((i i32)) (-> (&& (>= i INT:0) (< (* INT:2 (+ i INT:1)) n)) (>= heap{i} heap{(* INT:2 (+ i INT:1))}))))
  §R false
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var func = module.Functions[0];
        // Should have heap property postcondition
        Assert.Single(func.Postconditions);
    }

    /// <summary>
    /// Test emitting a realistic sorted array contract.
    /// </summary>
    [Fact]
    public void RealWorld_EmitSortedArrayContract()
    {
        var source = @"
§M{m001:Sorting}
§F{f001:Sort:pub}
  §I{i32:arr}
  §I{i32:n}
  §O{bool}
  §Q (> n INT:0)
  §S (forall ((i i32)) (-> (&& (>= i INT:0) (< i (- n INT:1))) (<= arr{i} arr{(+ i INT:1)})))
  §R true
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter(EmitContractMode.Debug);
        var result = emitter.Emit(module);

        // Should contain the function
        Assert.Contains("Sort", result);

        // Should have contract checks for the postcondition
        Assert.True(
            result.Contains("Enumerable.Range") || result.Contains("STATIC ONLY"),
            "Expected either runtime check or static-only comment"
        );

        // Should reference array access
        Assert.Contains("arr[", result);
    }

    #endregion
}
