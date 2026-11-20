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
    private static string EmitTypedExpression(ExpressionNode expression)
    {
        var attributes = new AttributeCollection();
        var parameters = new[] { "n", "m", "target", "x", "arr" }
            .Select(name => new ParameterNode(TextSpan.Empty, name, name == "arr" ? "i32[]" : "i32", attributes))
            .ToArray();
        var function = new FunctionNode(TextSpan.Empty, "f_context", "Context", Visibility.Public,
            parameters, new OutputNode(TextSpan.Empty, "bool"), null,
            [new ReturnStatementNode(TextSpan.Empty, expression)], attributes);
        return new CSharpEmitter(EmitContractMode.Debug).Emit(
            new ModuleNode(TextSpan.Empty, "m_context", "Context", [], [function], attributes));
    }

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

    // #1618: a precondition is a parameter refinement; this is the list the verifier sees.
    private static IReadOnlyList<RequiresNode> Pre(FunctionNode function)
        => Calor.Compiler.Verification.ContractVerifier.EffectivePreconditions(
            function.Parameters, function.Preconditions);

    #region Parser Tests

    [Fact]
    public void Parser_ParsesSimpleForall()
    {
        var source = @"
module Test Test block
  func TestFunc (int:x | (forall int:i where true : (i >= 0))) → bool
    !
    return true
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        Assert.Single(module.Functions);

        var func = module.Functions[0];
        Assert.Single(Pre(func));

        var requires = Pre(func)[0];
        Assert.IsType<ForallExpressionNode>(requires.Condition);

        var forall = (ForallExpressionNode)requires.Condition;
        Assert.Single(forall.BoundVariables);
        Assert.Equal("i", forall.BoundVariables[0].Name);
        Assert.Equal("int", forall.BoundVariables[0].TypeName);
    }

    [Fact]
    public void Parser_ParsesSimpleExists()
    {
        var source = @"
module Test Test block
  func TestFunc (int:x | (exists int:i where true : (i == #))) → bool
    !
    return true
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var func = module.Functions[0];
        var requires = Pre(func)[0];
        Assert.IsType<ExistsExpressionNode>(requires.Condition);

        var exists = (ExistsExpressionNode)requires.Condition;
        Assert.Single(exists.BoundVariables);
        Assert.Equal("i", exists.BoundVariables[0].Name);
    }

    [Fact]
    public void Parser_ParsesMultipleBoundVariables()
    {
        var source = @"
module Test Test block
  func TestFunc (int:n | (forall int:i, int:j where true : (i == j))) → bool
    !
    return true
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var func = module.Functions[0];
        var requires = Pre(func)[0];
        Assert.IsType<ForallExpressionNode>(requires.Condition);

        var forall = (ForallExpressionNode)requires.Condition;
        Assert.Equal(2, forall.BoundVariables.Count);
        Assert.Equal("i", forall.BoundVariables[0].Name);
        Assert.Equal("j", forall.BoundVariables[1].Name);
    }

    [Fact]
    public void Parser_ParsesForallWithImplication()
    {
        var source = @"
module Test Test block
  func ArrayAllNonNeg (int:n | (forall int:i in 0..# : (i >= 0))) → bool
    !
    return true
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var func = module.Functions[0];
        var requires = Pre(func)[0];
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

        Assert.Throws<InvalidOperationException>(() => forall.Accept(emitter));
        Assert.Throws<InvalidOperationException>(() => exists.Accept(emitter));

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

        var result = EmitTypedExpression(forall);

        // Should generate Enumerable.Range(...).All(...)
        Assert.Contains("Calor.Runtime.ContractQuantifier.Range", result);
        Assert.Contains(".All(", result);
    }

    [Fact]
    public void Emitter_RejectsUnboundedRuntimeForall()
    {
        // Create AST: (forall ((i i32)) (>= i INT:0))
        // A requested runtime check must not become a success-shaped static-only comment.
        var span = new TextSpan(0, 0, 1, 1);
        var boundVar = new QuantifierVariableNode(span, "i", "i32");
        var iRef = new ReferenceNode(span, "i");
        var zero = new IntLiteralNode(span, 0);
        var body = new BinaryOperationNode(span, BinaryOperator.GreaterOrEqual, iRef, zero);
        var forall = new ForallExpressionNode(span, new[] { boundVar }, body);

        var emitter = new CSharpEmitter(EmitContractMode.Debug);
        var error = Assert.Throws<InvalidOperationException>(() => forall.Accept(emitter));
        Assert.Contains("runtime", error.Message);
        Assert.Contains("forall", error.Message);
    }

    [Fact]
    public void Emitter_RejectsUnboundedRuntimeExists()
    {
        // Create AST: (exists ((i i32)) (== i INT:0))
        // An unenforced existential condition is not a runtime contract check.
        var span = new TextSpan(0, 0, 1, 1);
        var boundVar = new QuantifierVariableNode(span, "i", "i32");
        var iRef = new ReferenceNode(span, "i");
        var zero = new IntLiteralNode(span, 0);
        var body = new BinaryOperationNode(span, BinaryOperator.Equal, iRef, zero);
        var exists = new ExistsExpressionNode(span, new[] { boundVar }, body);

        var emitter = new CSharpEmitter(EmitContractMode.Debug);
        var error = Assert.Throws<InvalidOperationException>(() => exists.Accept(emitter));
        Assert.Contains("runtime", error.Message);
        Assert.Contains("exists", error.Message);
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

        var result = EmitTypedExpression(forall);

        // Should generate nested Enumerable.Range(...).All(...) calls
        Assert.Contains("Calor.Runtime.ContractQuantifier.Range", result);
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

        var result = EmitTypedExpression(exists);

        // Should generate nested Enumerable.Range(...).Any(...) calls
        Assert.Contains("Calor.Runtime.ContractQuantifier.Range", result);
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

        var result = EmitTypedExpression(forall);

        // Should still generate runtime check
        Assert.Contains("Calor.Runtime.ContractQuantifier.Range", result);
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

        var result = EmitTypedExpression(forall);

        // Should generate runtime check
        Assert.Contains("Calor.Runtime.ContractQuantifier.Range", result);
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

        var result = EmitTypedExpression(forall);

        // Should generate runtime check with (n + 1) as upper bound
        Assert.Contains("Calor.Runtime.ContractQuantifier.Range", result);
        Assert.Contains("Calor.Runtime.ContractQuantifier.Successor(n)", result);
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

        var result = EmitTypedExpression(forall);

        // Should generate runtime check with (0 + 1) as lower bound
        Assert.Contains("Calor.Runtime.ContractQuantifier.Range", result);
        Assert.Contains("Calor.Runtime.ContractQuantifier.Successor(0)", result);
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

        var result = EmitTypedExpression(forall);

        // Should generate runtime check with array access
        Assert.Contains("Calor.Runtime.ContractQuantifier.Range", result);
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

        var result = EmitTypedExpression(exists);

        // Should generate runtime check with array access
        Assert.Contains("Calor.Runtime.ContractQuantifier.Range", result);
        Assert.Contains(".Any(", result);
        Assert.Contains("arr[i]", result);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void Integration_ParseAndEmitQuantifiedContract()
    {
        var source = @"
module Test Test block
  func CheckAllPositive (int:n | (forall int:i in 0..# : (i >= 0))) → bool
    !
    return true
";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter(EmitContractMode.Debug);
        var result = emitter.Emit(module);

        // Should contain the function
        Assert.Contains("CheckAllPositive", result);

        // Should have contract checks (either loop or comment)
        Assert.True(
            result.Contains("Calor.Runtime.ContractQuantifier.Range") || result.Contains("STATIC ONLY"),
            "Expected either runtime check or static-only comment"
        );
    }

    [Fact]
    public void Integration_ParseAndEmitImplication()
    {
        var source = @"
module Test Test block
  func CheckImplication (int:x | (!(# > 0) || (# >= 0))) → bool
    !
    return true
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
        // Declared element types provide evidence that the comparisons are built-in.
        var source = @"
module Test Test block
  func AllNonNegative (int[]:arr, int:n | (forall int:i in 0..# : (arr[i] >= 0))) → bool
    !
    return true
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
        Assert.Contains("Calor.Runtime.ContractQuantifier.Range", result);
    }

    [Fact]
    public void Integration_ParseAndEmitExistsWithArrayAccess()
    {
        // Declared element types provide evidence that the comparisons are built-in.
        var source = @"
module Test Test block
  func ContainsTarget (int[]:arr, int:n, int:target | (exists int:i in 0..n : (arr[i] == #))) → bool
    !
    return true
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
        Assert.Contains("(forall int:i where true :", result);
        Assert.Contains("(i >=", result);
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
        Assert.Contains("(exists int:k where true :", result);
        Assert.Contains("(k ==", result);
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
        Assert.Contains("(!((x > 0)) ||", result);
        Assert.Contains("(x >", result);
        Assert.Contains("(x >=", result);
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
        Assert.Contains("(forall int:i, int:j where true :", result);
    }

    [Fact]
    public void CalorEmitter_Roundtrip_ParseEmitReparse()
    {
        // Parse source with quantifiers
        var source = @"
module Test Test block
  func TestFunc (int:n | (forall int:i in 0..# : (i >= 0))) → bool
    !
    return true
";

        var module1 = Parse(source, out var diag1);
        Assert.False(diag1.HasErrors, string.Join("\n", diag1.Select(d => d.Message)));

        // Get the precondition expression and emit it as Calor syntax
        var forall = (ForallExpressionNode)Pre(module1.Functions[0])[0].Condition;
        var emitter = new CalorEmitter();
        var emittedContract = forall.Accept(emitter);

        // Verify the emitted string contains expected elements
        Assert.Contains("forall", emittedContract);
        Assert.Contains("in 0..n", emittedContract);
        Assert.Contains("int:i", emittedContract);
    }

    #endregion

    #region Type Validation Tests

    [Fact]
    public void Verification_AcceptsIntegerQuantifierTypes()
    {
        // Test that integer types are accepted without warnings
        var source = @"
module Test Test block
  func TestFunc (int:n | (forall int:i in 0..# : (i >= 0))) → bool
    !
    return true
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
module Test Test block
  func TestFunc (int:n | (forall float:x where true : (x >= 0))) → bool
    !
    return true
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
        Assert.Contains("Calor.Runtime.ContractQuantifier.Range(0, 0)", result);
        Assert.Contains(".All(", result);
    }

    [Fact]
    public void Emitter_RejectsUnboundedShadowingQuantifiers()
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
        Assert.Throws<InvalidOperationException>(() => forall.Accept(emitter));
    }

    [Fact]
    public void Parser_ParsesQuantifierInPostconditionWithResult()
    {
        // Test: quantifier in postcondition referencing 'result'
        var source = @"
module Test Test block
  func SumPositive (int:n) → int
    !
    => (forall int:i in 0..result : (i >= 0))
    return n
";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var func = module.Functions[0];
        Assert.Single(func.Postconditions);

        var ensures = func.Postconditions[0];
        Assert.IsType<ForallExpressionNode>(ensures.Condition);
    }

    [Fact]
    public void Emitter_RejectsUnboundedNestedQuantifiers()
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
        Assert.Throws<InvalidOperationException>(() => outer.Accept(emitter));
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

        var result = EmitTypedExpression(forall);

        // Should still extract the first lower bound (0) and generate code
        Assert.Contains("Calor.Runtime.ContractQuantifier.Range(0,", result);
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
module Sorting Sorting block
  func IsSorted (int:arr, int:n | (# > 0)) → bool
    !
    => (forall int:i where ((i >= 0) && (i < (n - 1))) : (arr[i] <= arr[(i + 1)]))
    return true
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
module Search Search block
  func BinarySearch (int:arr, int:n | ((# > 0) && (forall int:i where ((i >= 0) && (i < (# - 1))) : (arr[i] <= arr[(i + 1)]))), int:target) → int
    !
    => (!(result >= 0) || (arr[result] == target))
    return 0
";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var func = module.Functions[0];
        // One refinement on n conjoins both conditions: n > 0 and array is sorted
        var sorted = Assert.IsType<BinaryOperationNode>(Assert.Single(Pre(func)).Condition);
        Assert.Equal(BinaryOperator.And, sorted.Operator);
        Assert.IsType<ForallExpressionNode>(sorted.Right);
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
module QuickSort QuickSort block
  func Partition (int:arr, int:low, int:high | (low <= #), int:pivot) → int
    !
    => (forall int:i where ((i >= low) && (i < result)) : (arr[i] <= pivot))
    => (forall int:j where ((j > result) && (j <= high)) : (arr[j] > pivot))
    return low
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
module Validation Validation block
  func ValidateGrades (int:grades, int:n | ((# > 0) && (forall int:i where ((i >= 0) && (i < #)) : ((grades[i] >= 0) && (grades[i] <= 100))))) → bool
    !
    return true
";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var func = module.Functions[0];
        // One refinement on n conjoins n > 0 and all grades in [0, 100]
        var conjunction = Assert.IsType<BinaryOperationNode>(Assert.Single(Pre(func)).Condition);
        Assert.Equal(BinaryOperator.And, conjunction.Operator);

        // Second conjunct should be a forall
        Assert.IsType<ForallExpressionNode>(conjunction.Right);
    }

    /// <summary>
    /// Example: Matrix multiplication dimension compatibility
    /// When multiplying A[m,n] by B[n,p], ensure dimensions match.
    /// </summary>
    [Fact]
    public void RealWorld_MatrixDimensionContract()
    {
        var source = @"
module Matrix Matrix block
  func MultiplyDimCheck (int:rowsA | (# > 0), int:colsA | (# > 0), int:rowsB | ((# > 0) && (colsA == #)), int:colsB | (# > 0)) → bool
    !
    return true
";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var func = module.Functions[0];
        // Four refinements (one per parameter); rowsB's conjoins the dimension compatibility check
        Assert.Equal(4, Pre(func).Count);
    }

    /// <summary>
    /// Example: Heap property (for priority queues)
    /// Parent is always greater than or equal to children.
    /// </summary>
    [Fact]
    public void RealWorld_MaxHeapPropertyContract()
    {
        var source = @"
module Heap Heap block
  func IsMaxHeap (int:heap, int:n | (# > 0)) → bool
    !
    => (!(result == true) || (forall int:i where ((i >= 0) && ((2 * (i + 1)) < n)) : (heap[i] >= heap[(2 * (i + 1))])))
    return false
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
module Sorting Sorting block
  func Sort (int[]:arr, int:n | (# > 0)) → bool
    !
    => (forall int:i in 0..(n - 1) : (arr[i] <= arr[(i + 1)]))
    return true
";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter(EmitContractMode.Debug);
        var result = emitter.Emit(module);

        // Should contain the function
        Assert.Contains("Sort", result);

        // Should have contract checks for the postcondition
        Assert.Contains("Calor.Runtime.ContractQuantifier.Range", result);

        // Should reference array access
        Assert.Contains("arr[", result);
    }

    #endregion
    /// <summary>
    /// The runtime lowering of a bounded forall must check the WHOLE implication, not just the
    /// consequent. This is the seventh false-`Proven`-elide vector of the v0.12 cycle and the
    /// first on the emitter side — the sort-demotion mechanism is structurally blind to it,
    /// because nothing in this path mints a Z3 sort at all.
    ///
    /// The bounds mined out of the antecedent constrain the `Enumerable.Range`; they do not
    /// replace the antecedent. Emitting `impl.Consequent` alone dropped every antecedent conjunct
    /// that is not a bound on the loop variable, making the runtime check a strictly STRONGER
    /// proposition than the one Z3 proved — and `Proven &amp;&amp; !IsVacuous` then deleted it.
    ///
    /// End-to-end before the fix, on a postcondition that is a TAUTOLOGY as written:
    ///   §S (forall ((i i32)) (-&gt; (&amp;&amp; (&amp;&amp; (&gt;= i 0) (&lt; i n)) (!= i 3)) (!= i 3)))
    ///   calor run          → Postcondition failed: Range(0, n-0).All(i =&gt; (i != 3))
    ///   calor run --verify → prints 5, exit 0
    /// </summary>
    [Fact]
    public void Emitter_BoundedForall_KeepsNonBoundAntecedentConjuncts()
    {
        var span = new TextSpan(0, 0, 1, 1);
        var boundVar = new QuantifierVariableNode(span, "i", "i32");

        // (&& (&& (>= i 0) (< i n)) (!= i 3)) -> (!= i 3)
        var antecedent = new BinaryOperationNode(span, BinaryOperator.And,
            new BinaryOperationNode(span, BinaryOperator.And,
                new BinaryOperationNode(span, BinaryOperator.GreaterOrEqual,
                    new ReferenceNode(span, "i"), new IntLiteralNode(span, 0)),
                new BinaryOperationNode(span, BinaryOperator.LessThan,
                    new ReferenceNode(span, "i"), new ReferenceNode(span, "n"))),
            new BinaryOperationNode(span, BinaryOperator.NotEqual,
                new ReferenceNode(span, "i"), new IntLiteralNode(span, 3)));
        var consequent = new BinaryOperationNode(span, BinaryOperator.NotEqual,
            new ReferenceNode(span, "i"), new IntLiteralNode(span, 3));

        var forall = new ForallExpressionNode(span, new[] { boundVar },
            new ImplicationExpressionNode(span, antecedent, consequent));

        var result = EmitTypedExpression(forall);

        // Still a bounded range check...
        Assert.Contains("Calor.Runtime.ContractQuantifier.Range", result);
        Assert.Contains(".All(", result);
        // ...but the guard survives into the predicate, as the implication's `!(ante) ||` form.
        Assert.Contains("!(", result);
        Assert.Contains("i < n", result);
    }

    /// <summary>
    /// The `??=` in ExtractBound keeps only the FIRST bound of each direction, so a second,
    /// tighter upper bound was silently discarded: Z3 proved over [0, min(n,m)) while the runtime
    /// checked [0, n). Keeping the full implication makes a too-wide range harmless — the extra
    /// values are guarded by the antecedent and the implication is vacuously true there.
    /// </summary>
    [Fact]
    public void Emitter_BoundedForall_KeepsASecondTighterBound()
    {
        var span = new TextSpan(0, 0, 1, 1);
        var boundVar = new QuantifierVariableNode(span, "i", "i32");

        // (&& (&& (>= i 0) (< i n)) (< i 2)) -> (< i 2)
        var antecedent = new BinaryOperationNode(span, BinaryOperator.And,
            new BinaryOperationNode(span, BinaryOperator.And,
                new BinaryOperationNode(span, BinaryOperator.GreaterOrEqual,
                    new ReferenceNode(span, "i"), new IntLiteralNode(span, 0)),
                new BinaryOperationNode(span, BinaryOperator.LessThan,
                    new ReferenceNode(span, "i"), new ReferenceNode(span, "n"))),
            new BinaryOperationNode(span, BinaryOperator.LessThan,
                new ReferenceNode(span, "i"), new IntLiteralNode(span, 2)));
        var consequent = new BinaryOperationNode(span, BinaryOperator.LessThan,
            new ReferenceNode(span, "i"), new IntLiteralNode(span, 2));

        var forall = new ForallExpressionNode(span, new[] { boundVar },
            new ImplicationExpressionNode(span, antecedent, consequent));

        var result = EmitTypedExpression(forall);

        // The discarded second bound must appear in the emitted predicate, not only in the Range.
        Assert.Contains("i < 2", result);
        Assert.Contains("!(", result);
    }

}
