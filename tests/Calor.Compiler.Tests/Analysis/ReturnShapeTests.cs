using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Unit table for <see cref="ReturnShape"/> — the shared classifier that
/// <see cref="ReturnValidationPass"/> (Calor0205) and
/// <see cref="Calor.Compiler.Verification.ContractVerifier"/> (<c>result</c>
/// referenceability) both defer to. Verifies each owner construct maps to the
/// expected <see cref="ReturnShape.Kind"/>, the no-value predicate, and — the
/// case the two consumers must NOT confuse — the divergence between the runtime
/// classification (<see cref="ReturnShape.Classify"/>) and the header predicate
/// (<see cref="ReturnShape.DeclaresValueOutput"/>) for an iterator.
/// </summary>
public class ReturnShapeTests
{
    // ------------------------------------------------------------- Classify()

    [Fact]
    public void ValueFunction_ClassifiesAsValue()
        => Assert.Equal(ReturnShape.Kind.Value, ClassifyFirst<FunctionNode>(@"
§M{m1:T}
  §F{f1:Get:pub}
    §O{i32}
    §R INT:0
"));

    [Fact]
    public void VoidFunction_ClassifiesAsVoid()
        => Assert.Equal(ReturnShape.Kind.Void, ClassifyFirst<FunctionNode>(@"
§M{m1:T}
  §F{f1:Do:pub}
    §P ""x""
"));

    [Fact]
    public void AsyncVoidFunction_ClassifiesAsAsyncVoid()
        => Assert.Equal(ReturnShape.Kind.AsyncVoid, ClassifyFirst<FunctionNode>(@"
§M{m1:T}
  §AF{f1:DoAsync:pub}
    §P ""x""
"));

    [Fact]
    public void IteratorFunction_ClassifiesAsIterator()
        => Assert.Equal(ReturnShape.Kind.Iterator, ClassifyFirst<FunctionNode>(@"
§M{m1:T}
  §F{f1:Nums:pub}
    §O{i32}
    §YIELD 42
"));

    [Fact]
    public void ValueMethod_ClassifiesAsValue()
        => Assert.Equal(ReturnShape.Kind.Value, ClassifyFirst<MethodNode>(@"
§M{m1:T}
  §CL{c1:C:pub}
    §MT{mt1:Get:pub} () -> i32
      §R INT:0
"));

    [Fact]
    public void VoidMethod_ClassifiesAsVoid()
        => Assert.Equal(ReturnShape.Kind.Void, ClassifyFirst<MethodNode>(@"
§M{m1:T}
  §CL{c1:C:pub}
    §MT{mt1:Do:pub} () -> void
      §P ""x""
"));

    [Fact]
    public void ValueOperator_ClassifiesAsValue()
        => Assert.Equal(ReturnShape.Kind.Value, ClassifyFirst<OperatorOverloadNode>(@"
§M{m1:T}
  §CL{c1:Vector:pub}
    §OP{op1:+:pub} (Vector:a, Vector:b) -> Vector
      §R a
"));

    [Fact]
    public void Constructor_ClassifiesAsConstructor()
        => Assert.Equal(ReturnShape.Kind.Constructor, ClassifyFirst<ConstructorNode>(@"
§M{m1:T}
  §CL{c1:C:pub}
    §CTOR{ctor1:pub} ()
      §P ""x""
"));

    [Fact]
    public void Event_ClassifiesAsEventAccessor()
        => Assert.Equal(ReturnShape.Kind.EventAccessor, ClassifyFirst<EventDefinitionNode>(@"
§M{m1:T}
  §CL{c1:C:pub}
    §EVT{e1:Click:pub:EventHandler}
    §EADD
    §P ""x""
    §/EADD
    §/EVT{e1}
"));

    [Fact]
    public void PropertyGetter_ClassifiesAsValue_AndSetterAsSetter()
    {
        const string src = @"
§M{m1:T}
  §CL{c1:C:pub}
    §PROP{p1:Name:str:pub}
      §GET
        §R ""x""
      §/GET
      §SET
        §P ""x""
      §/SET
    §/PROP{p1}
";
        var accessors = FindAll<PropertyAccessorNode>(Parse(src));
        var getter = accessors.Single(a => a.Kind == PropertyAccessorNode.AccessorKind.Get);
        var setter = accessors.Single(a => a.Kind != PropertyAccessorNode.AccessorKind.Get);

        Assert.Equal(ReturnShape.Kind.Value, ReturnShape.Classify(getter));
        Assert.Equal(ReturnShape.Kind.Setter, ReturnShape.Classify(setter));
    }

    [Fact]
    public void NonOwnerNode_ClassifiesAsNone()
        => Assert.Equal(ReturnShape.Kind.None, ReturnShape.Classify(Parse(@"
§M{m1:T}
  §F{f1:Do:pub}
    §P ""x""
")));

    // ------------------------------------------- Iterator divergence (the trap)

    /// <summary>
    /// An iterator DECLARES a value output (e.g. <c>IEnumerable&lt;T&gt;</c>), so the
    /// header predicate <see cref="ReturnShape.DeclaresValueOutput"/> is true and the
    /// contract verifier still allows <c>result</c> — but the runtime
    /// <see cref="ReturnShape.Classify"/> is <see cref="ReturnShape.Kind.Iterator"/>,
    /// not <see cref="ReturnShape.Kind.Value"/>, so the return-value pass treats a
    /// value <c>§R</c> as illegal. The two answers legitimately diverge; this test
    /// pins that so the two consumers can't be accidentally collapsed.
    /// </summary>
    [Fact]
    public void Iterator_DeclaresValueOutput_ButClassifiesAsIterator()
    {
        var fn = FindAll<FunctionNode>(Parse(@"
§M{m1:T}
  §F{f1:Nums:pub}
    §O{i32}
    §YIELD 42
")).Single();

        Assert.True(ReturnShape.DeclaresValueOutput(fn.Output));
        Assert.Equal(ReturnShape.Kind.Iterator, ReturnShape.Classify(fn));
    }

    // ------------------------------------------------ DeclaresValueOutput / void

    [Fact]
    public void DeclaresValueOutput_NullOutput_IsFalse()
        => Assert.False(ReturnShape.DeclaresValueOutput(null));

    [Theory]
    [InlineData("void", false)]
    [InlineData("VOID", false)]
    [InlineData("Void", false)]
    [InlineData(" void ", false)]
    [InlineData("i32", true)]
    [InlineData("string", true)]
    public void DeclaresValueOutput_MatchesVoidNameCaseInsensitively(string typeName, bool expected)
        => Assert.Equal(expected, ReturnShape.DeclaresValueOutput(new OutputNode(default, typeName)));

    [Theory]
    [InlineData("void", true)]
    [InlineData("VOID", true)]
    [InlineData(" void ", true)]
    [InlineData("i32", false)]
    public void IsVoidType_MatchesVoidNameCaseInsensitively(string typeName, bool expected)
        => Assert.Equal(expected, ReturnShape.IsVoidType(typeName));

    // ---------------------------------------------------------- IsNoValueOwner

    [Theory]
    [InlineData(ReturnShape.Kind.Void)]
    [InlineData(ReturnShape.Kind.AsyncVoid)]
    [InlineData(ReturnShape.Kind.Iterator)]
    [InlineData(ReturnShape.Kind.Setter)]
    [InlineData(ReturnShape.Kind.Constructor)]
    [InlineData(ReturnShape.Kind.EventAccessor)]
    public void IsNoValueOwner_TrueForEveryNoValueShape(ReturnShape.Kind kind)
        => Assert.True(ReturnShape.IsNoValueOwner(kind));

    [Theory]
    [InlineData(ReturnShape.Kind.None)]
    [InlineData(ReturnShape.Kind.Value)]
    public void IsNoValueOwner_FalseForValueBearingShapes(ReturnShape.Kind kind)
        => Assert.False(ReturnShape.IsNoValueOwner(kind));

    // ---------------------------------------------------------------- helpers

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        var module = new Parser(tokens, diagnostics).Parse();
        Assert.False(
            diagnostics.HasErrors,
            "Test source failed to parse:\n  " +
            string.Join("\n  ", diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return module;
    }

    private static ReturnShape.Kind ClassifyFirst<T>(string source) where T : AstNode
        => ReturnShape.Classify(FindAll<T>(Parse(source)).First());

    private static List<T> FindAll<T>(AstNode root) where T : AstNode
    {
        var found = new List<T>();
        Collect(root, found);
        return found;

        static void Collect(AstNode node, List<T> acc)
        {
            if (node is T t)
            {
                acc.Add(t);
            }
            foreach (var child in RecursiveAstWalker.GetChildren(node))
            {
                Collect(child, acc);
            }
        }
    }
}
