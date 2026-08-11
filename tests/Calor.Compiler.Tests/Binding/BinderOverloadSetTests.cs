using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

// Flat test namespace: see BinderDispatchCompletenessTests.
namespace Calor.Compiler.Tests;

/// <summary>
/// #762 items 5–6 (B8, scoping doc D5): top-level functions register in overload sets
/// (previously a same-name second declaration's TryDeclare silently failed and every
/// call resolved to the first declaration); duplicate signatures, true applicability
/// ties, and no-match calls are explicit diagnostics; exact types discriminate same-
/// arity overloads, and resolution is order-independent.
/// </summary>
public class BinderOverloadSetTests
{
    private static readonly TextSpan S = new(0, 0, 1, 1);

    private static FunctionNode Func(string id, string name, string returnType,
        (string Type, string Name)[] parameters, params StatementNode[] body)
        => new(S, id, name, Visibility.Public,
            parameters.Select(p => new ParameterNode(S, p.Name, p.Type, new AttributeCollection())).ToArray(),
            new OutputNode(S, returnType), null, body, new AttributeCollection());

    private static (BoundModule Bound, DiagnosticBag Diagnostics) Bind(params FunctionNode[] funcs)
    {
        var module = new ModuleNode(S, "m001", "Test",
            Array.Empty<UsingDirectiveNode>(), funcs, new AttributeCollection());
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(module);
        return (bound, diagnostics);
    }

    private static FunctionNode Caller(string id, int argCount)
        => Func(id, "Caller", "OBJECT", Array.Empty<(string, string)>(),
            new ReturnStatementNode(S, new CallExpressionNode(S, "Pick",
                Enumerable.Range(0, argCount).Select(i => (ExpressionNode)new IntLiteralNode(S, i)).ToArray())));

    private static string CallerResolvedType(BoundModule bound)
        => bound.Functions.Single(f => f.Symbol.Name == "Caller")
            .Body.OfType<BoundReturnStatement>().Single().Expression!.TypeName;

    [Fact]
    public void Overloads_ResolveByArity_RegardlessOfDeclarationOrder()
    {
        var oneArg = Func("f001", "Pick", "i32", new[] { ("i32", "x") });
        var twoArg = Func("f002", "Pick", "str", new[] { ("i32", "x"), ("i32", "y") });

        // Caller FIRST (pass-2 resolution must see overloads declared after it),
        // then the same module with the overload order reversed.
        var (boundA, diagsA) = Bind(Caller("f003", 2), oneArg, twoArg);
        var (boundB, diagsB) = Bind(twoArg, oneArg, Caller("f003", 2));

        Assert.Equal("str", CallerResolvedType(boundA));
        Assert.Equal("str", CallerResolvedType(boundB));
        foreach (var d in new[] { diagsA, diagsB })
        {
            Assert.DoesNotContain(d, x => x.Code == DiagnosticCode.DuplicateDefinition);
            Assert.DoesNotContain(d, x => x.Code == DiagnosticCode.DuplicateFunctionSignature);
            Assert.DoesNotContain(d, x => x.Code == DiagnosticCode.AmbiguousOverload);
            Assert.DoesNotContain(d, x => x.Code == DiagnosticCode.NoMatchingOverload);
        }
    }

    [Fact]
    public void DuplicateSignature_IsAnError()
    {
        var (_, diags) = Bind(
            Func("f001", "Pick", "i32", new[] { ("i32", "x") }),
            Func("f002", "Pick", "str", new[] { ("i32", "y") })); // same ordered types
        Assert.Contains(diags, d => d.Code == DiagnosticCode.DuplicateFunctionSignature
            && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void SameArityOverloads_ResolveByExactArgumentTypes()
    {
        // Structural binding now carries exact argument types, so arity alone does not
        // make otherwise distinct overloads ambiguous.
        var (bound, diags) = Bind(
            Func("f001", "Pick", "i32", new[] { ("i32", "x") }),
            Func("f002", "Pick", "str", new[] { ("str", "s") }),
            Caller("f003", 1));
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCode.AmbiguousOverload);
        Assert.Equal("i32", CallerResolvedType(bound));
    }

    [Fact]
    public void NoArityMatch_IsANonSilentError()
    {
        var (_, diags) = Bind(
            Func("f001", "Pick", "i32", new[] { ("i32", "x") }),
            Caller("f002", 3));
        Assert.Contains(diags, d => d.Code == DiagnosticCode.NoMatchingOverload
            && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void SecondOverload_NoLongerSilentlyDropped()
    {
        // Pre-B8 pin of the item-5 defect: the second declaration's TryDeclare failed
        // silently and a 2-arg call resolved to the FIRST declaration's return type.
        var (bound, diags) = Bind(
            Func("f001", "Pick", "i32", new[] { ("i32", "x") }),
            Func("f002", "Pick", "str", new[] { ("i32", "x"), ("i32", "y") }),
            Caller("f003", 2));
        Assert.Equal("str", CallerResolvedType(bound));
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCode.DuplicateFunctionSignature);
    }
}
