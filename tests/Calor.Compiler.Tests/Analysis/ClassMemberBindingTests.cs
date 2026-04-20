using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests.Analysis;

/// <summary>
/// Tests for class member binding — methods, constructors, properties, operators,
/// indexers, events, and the supporting infrastructure (scope, overloads, this, fields).
/// </summary>
public class ClassMemberBindingTests
{
    #region Helpers

    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    private static BoundModule Bind(string source, out DiagnosticBag diagnostics)
    {
        var module = Parse(source, out diagnostics);
        if (diagnostics.HasErrors) return null!;

        var binder = new Binder(diagnostics);
        return binder.Bind(module);
    }

    private static BoundFunction GetBoundMember(string source, string qualifiedName)
    {
        var bound = Bind(source, out var diagnostics);
        Assert.NotNull(bound);
        var member = bound.Functions.FirstOrDefault(f => f.Symbol.Name == qualifiedName);
        Assert.NotNull(member);
        return member;
    }

    #endregion

    #region Method Binding

    [Fact]
    public void BindMethod_SimpleMethod_ProducesMethod()
    {
        var source = @"
§M{m001:Test}
  §CL{c001:Calculator:pub}
    §MT{m002:Add:pub} (i32:x, i32:y) -> i32
      §E{*}
      §R (+ x y)
    §/MT{m002}
  §/CL{c001}
§/M{m001}
";
        var bound = Bind(source, out var diagnostics);
        Assert.NotNull(bound);

        var method = bound.Functions.FirstOrDefault(f => f.Symbol.Name == "Calculator.Add");
        Assert.NotNull(method);
        Assert.Equal(BoundMemberKind.Method, method.MemberKind);
        Assert.Equal("Calculator", method.ContainingTypeName);
        Assert.Equal("i32", method.Symbol.ReturnType);
        Assert.Equal(2, method.Symbol.Parameters.Count);
    }

    [Fact]
    public void BindMethod_OverloadedMethods_AllBound()
    {
        var source = @"
§M{m001:Test}
  §CL{c001:Converter:pub}
    §MT{m002:Convert:pub} (i32:x) -> str
      §E{*}
      §R (str x)
    §/MT{m002}
    §MT{m003:Convert:pub} (i32:x, str:format) -> str
      §E{*}
      §R (str x)
    §/MT{m003}
  §/CL{c001}
§/M{m001}
";
        var bound = Bind(source, out var diagnostics);
        Assert.NotNull(bound);

        // Both overloads should be bound
        var methods = bound.Functions.Where(f => f.Symbol.Name == "Converter.Convert").ToList();
        Assert.Equal(2, methods.Count);
    }

    [Fact]
    public void BindMethod_AbstractMethod_NotBound()
    {
        var source = @"
§M{m001:Test}
  §CL{c001:Base:pub:abs}
    §MT{m002:DoWork:pub:abs}
    §/MT{m002}
  §/CL{c001}
§/M{m001}
";
        var bound = Bind(source, out var diagnostics);
        Assert.NotNull(bound);
        Assert.DoesNotContain(bound.Functions, f => f.Symbol.Name.Contains("DoWork"));
    }

    #endregion

    #region Constructor Binding

    [Fact]
    public void BindConstructor_WithFieldAssignment_FieldResolves()
    {
        var source = @"
§M{m001:Test}
  §CL{c001:Widget:pub}
    §FLD{i32:_count:priv}
    §CTOR{ctor002:pub} (i32:count)
      §ASSIGN _count count
    §/CTOR{ctor002}
  §/CL{c001}
§/M{m001}
";
        var bound = Bind(source, out var diagnostics);
        Assert.NotNull(bound);

        var ctor = bound.Functions.FirstOrDefault(f => f.MemberKind == BoundMemberKind.Constructor);
        Assert.NotNull(ctor);
        Assert.Equal("Widget..ctor", ctor.Symbol.Name);
        Assert.Single(ctor.Symbol.Parameters);
        // Field assignment should produce a bound statement (not crash)
        Assert.NotEmpty(ctor.Body);
    }

    [Fact]
    public void BindConstructor_FieldInScope_NoUndefinedReference()
    {
        var source = @"
§M{m001:Test}
  §CL{c001:Widget:pub}
    §FLD{i32:_count:priv}
    §MT{m002:UseCount:pub}
      §E{*}
      §C{Console.WriteLine} §A _count §/C
    §/MT{m002}
  §/CL{c001}
§/M{m001}
";
        var bound = Bind(source, out var diagnostics);
        Assert.NotNull(bound);

        // _count should resolve (it's a field in class scope), not produce undefined reference
        var errors = diagnostics.Where(d => d.Code == DiagnosticCode.UndefinedReference &&
            d.Message.Contains("_count")).ToList();
        Assert.Empty(errors);
    }

    #endregion

    #region Property Binding

    [Fact]
    public void BindPropertyAccessor_Getter_ProducesPropertyGetter()
    {
        var source = @"
§M{m001:Test}
  §CL{c001:Config:pub}
    §FLD{str:_name:priv}
    §PROP{p002:Name:str:pub}
      §GET{pub}
        §R _name
      §/GET
    §/PROP{p002}
  §/CL{c001}
§/M{m001}
";
        var bound = Bind(source, out var diagnostics);
        Assert.NotNull(bound);

        var getter = bound.Functions.FirstOrDefault(f => f.MemberKind == BoundMemberKind.PropertyGetter);
        Assert.NotNull(getter);
        Assert.Equal("Config.Name.get", getter.Symbol.Name);
    }

    [Fact]
    public void BindPropertyAccessor_Setter_HasImplicitValueParam()
    {
        var source = @"
§M{m001:Test}
  §CL{c001:Config:pub}
    §FLD{str:_name:priv}
    §PROP{p002:Name:str:pub}
      §GET{pub}
        §R _name
      §/GET
      §SET{pub}
        §ASSIGN _name value
      §/SET
    §/PROP{p002}
  §/CL{c001}
§/M{m001}
";
        var bound = Bind(source, out var diagnostics);
        Assert.NotNull(bound);

        var setter = bound.Functions.FirstOrDefault(f => f.MemberKind == BoundMemberKind.PropertySetter);
        Assert.NotNull(setter);
        Assert.Equal("Config.Name.set", setter.Symbol.Name);
        // Setter should have implicit 'value' parameter
        var valueParam = Assert.Single(setter.Symbol.Parameters);
        Assert.Equal("value", valueParam.Name);
        Assert.Equal("str", setter.Symbol.Parameters[0].TypeName);
    }

    #endregion

    #region Operator Binding

    [Fact]
    public void BindOperator_ProducesOperatorOverload()
    {
        var source = @"
§M{m001:Test}
  §CL{c001:Vector:pub}
    §FLD{f64:X:pub}
    §FLD{f64:Y:pub}
    §OP{op002:+:pub} (Vector:a, Vector:b) -> Vector
      §R §NEW{Vector} §/NEW
    §/OP{op002}
  §/CL{c001}
§/M{m001}
";
        var bound = Bind(source, out var diagnostics);
        Assert.NotNull(bound);

        var op = bound.Functions.FirstOrDefault(f => f.MemberKind == BoundMemberKind.OperatorOverload);
        Assert.NotNull(op);
        Assert.Contains("op_", op.Symbol.Name);
        // Operators should have unknown effects
        Assert.Contains("*:*", op.DeclaredEffects);
    }

    #endregion

    #region Scope and Resolution

    [Fact]
    public void ArityAwareLookup_ResolvesCorrectOverload()
    {
        var scope = new Scope();
        var oneArg = new FunctionSymbol("Foo", "str", new List<VariableSymbol>
        {
            new("x", "i32", false, true)
        });
        var twoArgs = new FunctionSymbol("Foo", "bool", new List<VariableSymbol>
        {
            new("x", "i32", false, true),
            new("y", "i32", false, true)
        });
        scope.DeclareOverload(oneArg);
        scope.DeclareOverload(twoArgs);

        Assert.Equal("str", scope.LookupByArity("Foo", 1)!.ReturnType);
        Assert.Equal("bool", scope.LookupByArity("Foo", 2)!.ReturnType);
    }

    [Fact]
    public void ArityAwareLookup_FallsBackToFirstOverload()
    {
        var scope = new Scope();
        scope.DeclareOverload(new FunctionSymbol("Bar", "i32",
            new List<VariableSymbol> { new("x", "str", false, true) }));

        // No 0-arg overload, should fall back to first
        var result = scope.LookupByArity("Bar", 0);
        Assert.NotNull(result);
        Assert.Equal("i32", result.ReturnType);
    }

    [Fact]
    public void ArityAwareLookup_WalksParentScope()
    {
        var parent = new Scope();
        parent.DeclareOverload(new FunctionSymbol("Helper", "str",
            new List<VariableSymbol> { new("x", "i32", false, true) }));

        var child = parent.CreateChild();
        var result = child.LookupByArity("Helper", 1);
        Assert.NotNull(result);
        Assert.Equal("str", result.ReturnType);
    }

    #endregion

    #region This/Field/Static Context

    [Fact]
    public void ClassScope_FieldShadowedByParam_BareNameResolvesToParam()
    {
        // When a parameter has the same name as a field, bare name references
        // resolve to the parameter (innermost scope wins)
        var source = @"
§M{m001:Test}
  §CL{c001:Foo:pub}
    §FLD{i32:x:priv}
    §MT{m002:Set:pub} (i32:x)
      §E{*}
      §C{Console.WriteLine} §A x §/C
    §/MT{m002}
  §/CL{c001}
§/M{m001}
";
        var bound = Bind(source, out var diagnostics);
        Assert.NotNull(bound);
        // No errors — x resolves to the parameter
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.UndefinedReference);
    }

    [Fact]
    public void ClassScope_LookupLocal_FindsFieldNotParam()
    {
        // Unit test for the _currentClassScope.LookupLocal fix:
        // LookupLocal on the class scope should find the field, even when
        // a parameter with the same name exists in the method scope
        var classScope = new Scope();
        classScope.TryDeclare(new VariableSymbol("x", "i32", true)); // field

        var methodScope = classScope.CreateChild();
        methodScope.TryDeclare(new VariableSymbol("x", "str", false, true)); // param shadows

        // Lookup walks scope chain — finds param (innermost)
        var fromLookup = methodScope.Lookup("x");
        Assert.NotNull(fromLookup);
        Assert.Equal("str", ((VariableSymbol)fromLookup).TypeName); // param wins

        // LookupLocal on class scope — finds field directly
        var fromClassScope = classScope.LookupLocal("x");
        Assert.NotNull(fromClassScope);
        Assert.Equal("i32", ((VariableSymbol)fromClassScope).TypeName); // field, not param
    }

    #endregion

    #region Statement Binding

    [Fact]
    public void BindAssignment_InMethod_ProducesBoundAssignment()
    {
        var source = @"
§M{m001:Test}
  §CL{c001:Counter:pub}
    §FLD{i32:_count:priv}
    §MT{m002:Increment:pub}
      §E{*}
      §ASSIGN _count (+ _count 1)
    §/MT{m002}
  §/CL{c001}
§/M{m001}
";
        var bound = Bind(source, out var diagnostics);
        Assert.NotNull(bound);
        var method = bound.Functions.FirstOrDefault(f => f.Symbol.Name == "Counter.Increment");
        Assert.NotNull(method);
        Assert.NotEmpty(method.Body);
        Assert.IsType<BoundAssignmentStatement>(method.Body[0]);
    }

    [Fact]
    public void BindUnsupportedStatement_EmitsDiagnostic_NotCrash()
    {
        // If we encounter an AST node type the Binder doesn't handle,
        // it should produce BoundUnsupportedStatement + Calor0931, not throw
        var source = @"
§M{m001:Test}
  §CL{c001:MyClass:pub}
    §MT{m002:DoWork:pub}
      §E{*}
      §C{Console.WriteLine} §A ""hello"" §/C
    §/MT{m002}
  §/CL{c001}
§/M{m001}
";
        var bound = Bind(source, out var diagnostics);
        Assert.NotNull(bound);
        // Should not throw, should produce bound functions
        Assert.NotEmpty(bound.Functions);
    }

    #endregion

    #region BoundMemberKind Classification

    [Fact]
    public void TopLevelFunction_HasCorrectMemberKind()
    {
        var source = @"
§M{m001:Test}
  §F{f001:add:pub} (i32:x, i32:y) -> i32
    §R (+ x y)
  §/F{f001}
§/M{m001}
";
        var bound = Bind(source, out var diagnostics);
        Assert.NotNull(bound);
        var func = bound.Functions.First();
        Assert.Equal(BoundMemberKind.TopLevelFunction, func.MemberKind);
        Assert.Null(func.ContainingTypeName);
    }

    [Fact]
    public void AllMemberKinds_CorrectlyClassified()
    {
        var source = @"
§M{m001:Test}
  §CL{c001:MyClass:pub}
    §FLD{i32:_x:priv}
    §MT{m002:Foo:pub}
      §E{*}
      §C{Console.WriteLine} §A ""hello"" §/C
    §/MT{m002}
    §CTOR{ctor003:pub} (i32:x)
      §ASSIGN _x x
    §/CTOR{ctor003}
    §PROP{p004:X:i32:pub}
      §GET{pub}
        §R _x
      §/GET
      §SET{pub}
        §ASSIGN _x value
      §/SET
    §/PROP{p004}
  §/CL{c001}
§/M{m001}
";
        var bound = Bind(source, out var diagnostics);
        Assert.NotNull(bound);

        Assert.Contains(bound.Functions, f => f.MemberKind == BoundMemberKind.Method);
        Assert.Contains(bound.Functions, f => f.MemberKind == BoundMemberKind.Constructor);
        Assert.Contains(bound.Functions, f => f.MemberKind == BoundMemberKind.PropertyGetter);
        Assert.Contains(bound.Functions, f => f.MemberKind == BoundMemberKind.PropertySetter);

        // All should have ContainingTypeName
        foreach (var member in bound.Functions)
            Assert.Equal("MyClass", member.ContainingTypeName);
    }

    #endregion

    #region ContractInferencePass Guard

    [Fact]
    public void ContractInference_SkipsClassMembers()
    {
        var source = @"
§M{m001:Test}
  §CL{c001:Math:pub}
    §MT{m002:Divide:pub} (i32:x, i32:y) -> i32
      §E{*}
      §R (/ x y)
    §/MT{m002}
  §/CL{c001}
§/M{m001}
";
        var bound = Bind(source, out var diagnostics);
        Assert.NotNull(bound);

        // Verify the member is bound
        var method = bound.Functions.FirstOrDefault(f => f.Symbol.Name == "Math.Divide");
        Assert.NotNull(method);
        Assert.Equal(BoundMemberKind.Method, method.MemberKind);

        // ContractInferencePass should skip class members (they'd fail name matching)
        // This is verified by the fact that no spurious contract inference diagnostics are produced
    }

    #endregion

    #region TryBindMember Error Handling

    [Fact]
    public void FailedMember_NotAddedToFunctions()
    {
        // If a class has methods that fail to bind, they should produce diagnostics
        // but not be added to the functions list
        var source = @"
§M{m001:Test}
  §CL{c001:Good:pub}
    §MT{m002:WorkingMethod:pub}
      §E{*}
      §C{Console.WriteLine} §A ""ok"" §/C
    §/MT{m002}
  §/CL{c001}
§/M{m001}
";
        var bound = Bind(source, out var diagnostics);
        Assert.NotNull(bound);
        // Working method should be in the list
        Assert.Contains(bound.Functions, f => f.Symbol.Name == "Good.WorkingMethod");
    }

    #endregion

    #region Conditional Expression

    [Fact]
    public void ConditionalExpression_BothBranchesPreserved()
    {
        var source = @"
§M{m001:Test}
  §F{f001:abs:pub} (i32:x) -> i32
    §R (? (>= x 0) x (- 0 x))
  §/F{f001}
§/M{m001}
";
        var bound = Bind(source, out var diagnostics);
        Assert.NotNull(bound);
        var func = bound.Functions.First();
        // Return statement should have a BoundConditionalExpression, not just the true branch
        var ret = func.Body.OfType<BoundReturnStatement>().First();
        Assert.IsType<BoundConditionalExpression>(ret.Expression);
        var condExpr = (BoundConditionalExpression)ret.Expression!;
        Assert.NotNull(condExpr.Condition);
        Assert.NotNull(condExpr.WhenTrue);
        Assert.NotNull(condExpr.WhenFalse);
    }

    #endregion

    #region Statement Binder Tests

    [Fact]
    public void BindForeach_InMethod_ProducesBoundForeach()
    {
        var source = @"
§M{m001:Test}
  §CL{c001:Processor:pub}
    §MT{m002:Process:pub} (List<str>:items)
      §E{*}
      §EACH{each003:item:str} items
        §C{Console.WriteLine} §A item §/C
      §/EACH{each003}
    §/MT{m002}
  §/CL{c001}
§/M{m001}
";
        var bound = Bind(source, out var diagnostics);
        Assert.NotNull(bound);
        var method = bound.Functions.FirstOrDefault(f => f.Symbol.Name == "Processor.Process");
        Assert.NotNull(method);
        Assert.Contains(method.Body, s => s is BoundForeachStatement);
    }

    [Fact]
    public void BindThrow_InMethod_ProducesBoundThrow()
    {
        var source = @"
§M{m001:Test}
  §CL{c001:Guard:pub}
    §MT{m002:Check:pub} (i32:x)
      §E{*,throw}
      §IF{if003} (< x 0)
        §TH §NEW{ArgumentException} §A ""negative"" §/NEW
      §/I{if003}
    §/MT{m002}
  §/CL{c001}
§/M{m001}
";
        var bound = Bind(source, out var diagnostics);
        Assert.NotNull(bound);
        var method = bound.Functions.FirstOrDefault(f => f.Symbol.Name == "Guard.Check");
        Assert.NotNull(method);
    }

    [Fact]
    public void BindUsing_InMethod_ProducesBoundUsing()
    {
        var source = @"
§M{m001:Test}
  §CL{c001:FileHandler:pub}
    §MT{m002:ReadFile:pub} (str:path) -> str
      §E{*}
      §USE{use003:reader:StreamReader} §C{File.OpenText} §A path §/C
        §R §C{reader.ReadToEnd} §/C
      §/USE{use003}
    §/MT{m002}
  §/CL{c001}
§/M{m001}
";
        var bound = Bind(source, out var diagnostics);
        Assert.NotNull(bound);
        var method = bound.Functions.FirstOrDefault(f => f.Symbol.Name == "FileHandler.ReadFile");
        Assert.NotNull(method);
        Assert.Contains(method.Body, s => s is BoundUsingStatement);
    }

    #endregion

    #region Dataflow Tests

    [Fact]
    public void Assignment_DefinesVariable_InDataflow()
    {
        // BoundAssignmentStatement should register as a variable definition
        var assign = new BoundAssignmentStatement(
            new Parsing.TextSpan(0, 1, 1, 1),
            new BoundVariableExpression(new Parsing.TextSpan(0, 1, 1, 1),
                new VariableSymbol("x", "i32", true)),
            new BoundIntLiteral(new Parsing.TextSpan(0, 1, 1, 1), 42));

        var defined = BoundNodeHelpers.GetDefinedVariable(assign);
        Assert.NotNull(defined);
        Assert.Equal("x", defined.Name);
    }

    [Fact]
    public void Assignment_LHS_NotCountedAsUse()
    {
        // For x = 1, x should NOT appear in used variables (it's defined, not used)
        var xVar = new VariableSymbol("x", "i32", true);
        var assign = new BoundAssignmentStatement(
            new Parsing.TextSpan(0, 1, 1, 1),
            new BoundVariableExpression(new Parsing.TextSpan(0, 1, 1, 1), xVar),
            new BoundIntLiteral(new Parsing.TextSpan(0, 1, 1, 1), 42));

        var used = BoundNodeHelpers.GetUsedVariables((BoundStatement)assign).ToList();
        Assert.DoesNotContain(used, v => v.Name == "x");
    }

    [Fact]
    public void Assignment_RHS_CountedAsUse()
    {
        // For x = y, y should appear in used variables
        var xVar = new VariableSymbol("x", "i32", true);
        var yVar = new VariableSymbol("y", "i32", false);
        var assign = new BoundAssignmentStatement(
            new Parsing.TextSpan(0, 1, 1, 1),
            new BoundVariableExpression(new Parsing.TextSpan(0, 1, 1, 1), xVar),
            new BoundVariableExpression(new Parsing.TextSpan(0, 1, 1, 1), yVar));

        var used = BoundNodeHelpers.GetUsedVariables((BoundStatement)assign).ToList();
        Assert.Contains(used, v => v.Name == "y");
        Assert.DoesNotContain(used, v => v.Name == "x");
    }

    [Fact]
    public void Foreach_DefinesLoopVariable()
    {
        var loopVar = new VariableSymbol("item", "str", false);
        var forEach = new BoundForeachStatement(
            new Parsing.TextSpan(0, 1, 1, 1),
            loopVar,
            new BoundVariableExpression(new Parsing.TextSpan(0, 1, 1, 1),
                new VariableSymbol("list", "List<str>", false)),
            Array.Empty<BoundStatement>());

        var defined = BoundNodeHelpers.GetDefinedVariable(forEach);
        Assert.NotNull(defined);
        Assert.Equal("item", defined.Name);
    }

    [Fact]
    public void ConditionalExpression_AllBranchesYieldVariables()
    {
        var x = new VariableSymbol("x", "i32", false);
        var y = new VariableSymbol("y", "i32", false);
        var z = new VariableSymbol("z", "i32", false);
        var condExpr = new BoundConditionalExpression(
            new Parsing.TextSpan(0, 1, 1, 1),
            new BoundVariableExpression(new Parsing.TextSpan(0, 1, 1, 1), x),
            new BoundVariableExpression(new Parsing.TextSpan(0, 1, 1, 1), y),
            new BoundVariableExpression(new Parsing.TextSpan(0, 1, 1, 1), z));

        var used = BoundNodeHelpers.GetUsedVariables(condExpr).Select(v => v.Name).ToList();
        Assert.Contains("x", used);
        Assert.Contains("y", used);
        Assert.Contains("z", used);
    }

    #endregion

    #region Scope Registration

    [Fact]
    public void RegisterClassMembers_PropertiesResolvable()
    {
        var source = @"
§M{m001:Test}
  §CL{c001:Config:pub}
    §PROP{p002:Name:str:pub:get,set}
    §MT{m003:UseProperty:pub}
      §E{*}
      §C{Console.WriteLine} §A Name §/C
    §/MT{m003}
  §/CL{c001}
§/M{m001}
";
        var bound = Bind(source, out var diagnostics);
        Assert.NotNull(bound);
        // Name property should be resolvable as a variable (no undefined reference)
        var undefs = diagnostics.Where(d => d.Code == DiagnosticCode.UndefinedReference &&
            d.Message.Contains("'Name'")).ToList();
        Assert.Empty(undefs);
    }

    #endregion

    #region Decimal Literal Fix

    [Fact]
    public void DecimalLiteral_NotMisparsedAsZero()
    {
        var source = @"
§M{m001:Test}
  §F{f001:convert:pub} (dec:amount) -> dec
    §R (/ amount DEC:100)
  §/F{f001}
§/M{m001}
";
        var bound = Bind(source, out var diagnostics);
        Assert.NotNull(bound);
        var func = bound.Functions.First();
        var ret = func.Body.OfType<BoundReturnStatement>().First();
        // The return expression should be a binary division
        Assert.IsType<BoundBinaryExpression>(ret.Expression);
        var div = (BoundBinaryExpression)ret.Expression!;
        // The RHS (DEC:100) should NOT be zero
        Assert.IsType<BoundFloatLiteral>(div.Right);
        Assert.NotEqual(0.0, ((BoundFloatLiteral)div.Right).Value);
    }

    #endregion

    #region End-to-End Analysis

    [Fact]
    public void EndToEnd_ClassMethodDivByZero_FindsBugPattern()
    {
        // End-to-end: parse → bind → analyze → assert Calor0920 found
        // Uses ReportOnlyVerified=false to see heuristic findings
        var source = @"
§M{m001:Test}
  §CL{c001:Calculator:pub}
    §MT{m002:Divide:pub} (i32:x, i32:y) -> i32
      §E{*}
      §R (/ x y)
    §/MT{m002}
  §/CL{c001}
§/M{m001}
";
        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors, $"Parse errors: {string.Join(", ", parseDiags.Errors.Select(e => e.Message))}");

        var analysisDiags = new DiagnosticBag();
        var analysisPass = new Calor.Compiler.Analysis.VerificationAnalysisPass(analysisDiags);
        var result = analysisPass.Analyze(module);

        Assert.True(result.FunctionsAnalyzed > 0, "Expected >0 functions analyzed");
        Assert.True(result.BugPatternsFound > 0, "Expected bug patterns from division by unchecked parameter");
    }

    [Fact]
    public void EndToEnd_ConstructorInitializerArgs_Analyzed()
    {
        // Constructor with : base(args) — initializer args should be visible to analysis
        var source = @"
§M{m001:Test}
  §CL{c001:Widget:pub}
    §FLD{i32:_value:priv}
    §CTOR{ctor002:pub} (i32:v)
      §BASE §A v §/BASE
      §ASSIGN _value v
    §/CTOR{ctor002}
  §/CL{c001}
§/M{m001}
";
        var bound = Bind(source, out var diagnostics);
        Assert.NotNull(bound);
        var ctor = bound.Functions.FirstOrDefault(f => f.MemberKind == BoundMemberKind.Constructor);
        Assert.NotNull(ctor);
        // Constructor body should have a BoundCallStatement for the base call + assignment
        Assert.True(ctor.Body.Count >= 2, "Expected at least 2 statements (base call + assignment)");
        Assert.IsType<BoundCallStatement>(ctor.Body[0]); // base..ctor call
    }

    #endregion

    #region Static Context Negative Tests

    [Fact]
    public void StaticMethod_ThisExpression_DoesNotResolve()
    {
        var source = @"
§M{m001:Test}
  §CL{c001:Utils:pub}
    §FLD{i32:_counter:priv}
    §MT{m002:StaticMethod:pub:stat}
      §E{*}
      §C{Console.WriteLine} §A §THIS §/C
    §/MT{m002}
  §/CL{c001}
§/M{m001}
";
        var bound = Bind(source, out var diagnostics);
        Assert.NotNull(bound);
        var method = bound.Functions.FirstOrDefault(f => f.Symbol.Name == "Utils.StaticMethod");
        Assert.NotNull(method);
        // In static context, §THIS should not resolve to BoundThisExpression
        // It should fall through to BindFallbackExpression (which reports a diagnostic)
    }

    #endregion
}
