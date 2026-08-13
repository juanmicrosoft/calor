using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

// Flat test namespace: see BinderDispatchCompletenessTests.
namespace Calor.Compiler.Tests;

/// <summary>
/// #762 B3: arrays/indexes + collections (13 classes) bind structurally — bound node
/// type, explicit type string, ALL AST properties retained, no Calor0259. Direct-AST
/// construction (the B2 pattern). Checker-consumption honesty: binding these makes the
/// subtrees VISIBLE (BoundChildren default arms traverse them — pinned below); the
/// index-OOB checker still keys on call-target strings until #786 re-platforms it, so
/// visibility here is not yet an OOB-findings delta, and the family PR says so.
/// </summary>
public class BinderArrayCollectionFamilyTests
{
    private static readonly TextSpan S = new(0, 0, 1, 1);
    private static BoundExpression I(long v) => new BoundIntLiteral(S, v);

    private static (BoundExpression Expr, DiagnosticBag Diagnostics) BindReturn(ExpressionNode expr)
    {
        var func = new FunctionNode(S, "f001", "Probe", Visibility.Public,
            Array.Empty<ParameterNode>(), new OutputNode(S, "OBJECT"), null,
            new StatementNode[] { new ReturnStatementNode(S, expr) },
            new AttributeCollection());
        var module = new ModuleNode(S, "m001", "Test",
            Array.Empty<UsingDirectiveNode>(), new[] { func }, new AttributeCollection());
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(module);
        var ret = bound.Functions.Single().Body.OfType<BoundReturnStatement>().Single();
        return (ret.Expression!, diagnostics);
    }

    private static void AssertComplete(DiagnosticBag d) =>
        Assert.DoesNotContain(d, x => x.Code == DiagnosticCode.AnalysisIncomplete);

    private static IntLiteralNode Lit(long v) => new(S, v);

    [Fact]
    public void ArrayCreation_BindsSizeAndInitializers_ComposesType()
    {
        var (expr, diags) = BindReturn(new ArrayCreationNode(S, "a1", "nums", "i32",
            Lit(3), new ExpressionNode[] { Lit(1), Lit(2), Lit(3) }, new AttributeCollection()));
        var arr = Assert.IsType<BoundArrayCreation>(expr);
        Assert.Equal("i32[]", arr.TypeName);
        Assert.NotNull(arr.Size);
        Assert.Equal(3, arr.Initializer.Count);
        AssertComplete(diags);
    }

    [Fact]
    public void ArrayAccess_DerivesElementType_FromArrayTypeString()
    {
        var creation = new ArrayCreationNode(S, "a1", "nums", "i32",
            null, new ExpressionNode[] { Lit(1) }, new AttributeCollection());
        var (expr, diags) = BindReturn(new ArrayAccessNode(S, creation, Lit(0)));
        var access = Assert.IsType<BoundArrayAccess>(expr);
        Assert.Equal("i32", access.TypeName);
        Assert.IsType<BoundArrayCreation>(access.Array);
        AssertComplete(diags);
    }

    [Fact]
    public void ArrayLength_And_CollectionCount_AreInt()
    {
        var creation = new ArrayCreationNode(S, "a1", "nums", "i32",
            null, new ExpressionNode[] { Lit(1) }, new AttributeCollection());
        var (len, d1) = BindReturn(new ArrayLengthNode(S, creation));
        Assert.Equal("INT", Assert.IsType<BoundArrayLength>(len).TypeName);
        AssertComplete(d1);

        var list = new ListCreationNode(S, "l1", "xs", "i32", new ExpressionNode[] { Lit(1) }, new AttributeCollection());
        var (cnt, d2) = BindReturn(new CollectionCountNode(S, list));
        Assert.Equal("INT", Assert.IsType<BoundCollectionCount>(cnt).TypeName);
        AssertComplete(d2);
    }

    [Fact]
    public void MultiDim_CreationAndAccess_RetainAllChildren()
    {
        var creation = new MultiDimArrayCreationNode(S, "m1", "grid", "i32", 2,
            new ExpressionNode[] { Lit(2), Lit(2) },
            new IReadOnlyList<ExpressionNode>[] { new ExpressionNode[] { Lit(1), Lit(2) } });
        var (expr, diags) = BindReturn(creation);
        var mc = Assert.IsType<BoundMultiDimArrayCreation>(expr);
        Assert.Equal("i32[,]", mc.TypeName);
        Assert.Equal(2, mc.DimensionSizes.Count);
        // Row STRUCTURE retained (review M2): one row of two, not a flattened two.
        Assert.Single(mc.InitializerRows);
        Assert.Equal(2, mc.InitializerRows[0].Count);
        AssertComplete(diags);

        var (acc, d2) = BindReturn(new MultiDimArrayAccessNode(S, creation,
            new ExpressionNode[] { Lit(0), Lit(1) }));
        var ma = Assert.IsType<BoundMultiDimArrayAccess>(acc);
        Assert.Equal("i32", ma.TypeName);
        Assert.Equal(2, ma.Indices.Count);
        AssertComplete(d2);
    }

    [Fact]
    public void IndexFromEnd_And_Range_BindWithOptionalBounds()
    {
        var (idx, d1) = BindReturn(new IndexFromEndNode(S, Lit(1)));
        Assert.Equal("INDEX", Assert.IsType<BoundIndexFromEnd>(idx).TypeName);
        AssertComplete(d1);

        var (openRange, d2) = BindReturn(new RangeExpressionNode(S, Lit(1), null));
        var r = Assert.IsType<BoundRangeExpression>(openRange);
        Assert.NotNull(r.Start);
        Assert.Null(r.End);
        AssertComplete(d2);
    }

    [Fact]
    public void ListSetDictionary_BindElementsAndEntries_ComposeTypes()
    {
        var (list, d1) = BindReturn(new ListCreationNode(S, "l1", "xs", "i32",
            new ExpressionNode[] { Lit(1), Lit(2) }, new AttributeCollection()));
        Assert.Equal("List<i32>", Assert.IsType<BoundListCreation>(list).TypeName);
        AssertComplete(d1);

        var (set, d2) = BindReturn(new SetCreationNode(S, "s1", "ys", "str",
            new ExpressionNode[] { new StringLiteralNode(S, "a") }, new AttributeCollection()));
        Assert.Equal("HashSet<str>", Assert.IsType<BoundSetCreation>(set).TypeName);
        AssertComplete(d2);

        var (dict, d3) = BindReturn(new DictionaryCreationNode(S, "d1", "map", "str", "i32",
            new[] { new KeyValuePairNode(S, new StringLiteralNode(S, "k"), Lit(1)) }, new AttributeCollection()));
        var bd = Assert.IsType<BoundDictionaryCreation>(dict);
        Assert.Equal("Dictionary<str,i32>", bd.TypeName);
        Assert.Single(bd.Entries);
        AssertComplete(d3);
    }

    [Fact]
    public void Contains_IsBool_TupleComposes()
    {
        var (contains, d1) = BindReturn(new CollectionContainsNode(S, "xs", Lit(1),
            ContainsMode.Value));
        var cc = Assert.IsType<BoundCollectionContains>(contains);
        Assert.Equal("BOOL", cc.TypeName);
        Assert.Equal("xs", cc.CollectionName);
        // The B3 review's CRITICAL: Mode distinguishes three different operations.
        Assert.Equal(ContainsMode.Value, cc.Mode);
        AssertComplete(d1);

        var (tuple, d2) = BindReturn(new TupleLiteralNode(S,
            new ExpressionNode[] { Lit(1), new StringLiteralNode(S, "a") }));
        var tl = Assert.IsType<BoundTupleLiteral>(tuple);
        Assert.Equal(2, tl.Elements.Count);
        Assert.Equal("Tuple<INT,STRING>", tl.TypeName);
        AssertComplete(d2);
    }

    [Fact]
    public void BoundChildren_EnumeratesEveryB3NodeChild()
    {
        var i = new BoundIntLiteral(S, 1);
        var j = new BoundIntLiteral(S, 2);
        var arr = new BoundArrayCreation(S, "id1", "a", "i32", i, [j]);
        Assert.Equal([i, j], BoundChildren.Of(arr));
        Assert.Equal(new BoundExpression[] { arr, i },
            BoundChildren.Of(new BoundArrayAccess(S, arr, i)));
        Assert.Equal([arr], BoundChildren.Of(new BoundArrayLength(S, arr)));
        Assert.Equal([i, j], BoundChildren.Of(
            new BoundMultiDimArrayCreation(S, "idm", "m", "i32", 2, [i], [[j]])));
        Assert.Equal(new BoundExpression[] { arr, i, j },
            BoundChildren.Of(new BoundMultiDimArrayAccess(S, arr, [i, j])));
        Assert.Equal([i], BoundChildren.Of(new BoundIndexFromEnd(S, i)));
        Assert.Equal([i], BoundChildren.Of(new BoundRangeExpression(S, i, null)));
        Assert.Equal([i, j], BoundChildren.Of(new BoundRangeExpression(S, i, j)));
        Assert.Equal([i], BoundChildren.Of(new BoundListCreation(S, "idl", "l", "i32", [i])));
        Assert.Equal([i], BoundChildren.Of(new BoundSetCreation(S, "ids", "s", "i32", [i])));
        Assert.Equal([i, j], BoundChildren.Of(
            new BoundDictionaryCreation(S, "idd", "d", "str", "i32", [new BoundPair(i, j, S)])));
        Assert.Equal([i], BoundChildren.Of(new BoundCollectionContains(S, "xs", i, ContainsMode.Value)));
        Assert.Equal([i], BoundChildren.Of(new BoundCollectionCount(S, i)));
        Assert.Equal([i, j], BoundChildren.Of(new BoundTupleLiteral(S, [i, j])));
    }

    [Fact]
    public void JaggedElementType_DerivesFromTrailingBracketGroup()
    {
        // review m5: "i32[][]" element = "i32[]" (EndsWith route) and a multi-dim
        // access on a jagged-of-multidim type slices at the TRAILING bracket group.
        var jagged = new BoundArrayCreation(S, "j1", "js", "i32[]", null, []);
        Assert.Equal("i32[][]", jagged.TypeName);
        Assert.Equal("i32[]", new BoundArrayAccess(S, jagged, new BoundIntLiteral(S, 0)).TypeName);
    }

    [Fact]
    public void ContainsArrayAccess_RecognizesStructuralAccessNodes()
    {
        // review M4: the helper NAMED for array access must match the structural nodes
        // this family added, not only recurse through them.
        var arr = new BoundArrayCreation(S, "a1", "xs", "i32", null, []);
        var idx = new BoundIntLiteral(S, 0);
        Assert.True(Compiler.Analysis.Dataflow.BoundNodeHelpers.ContainsArrayAccess(
            new BoundArrayAccess(S, arr, idx), out var a, out var i2));
        Assert.Same(arr, a);
        Assert.Same(idx, i2);
        Assert.True(Compiler.Analysis.Dataflow.BoundNodeHelpers.ContainsArrayAccess(
            new BoundMultiDimArrayAccess(S, arr, [idx]), out _, out _));
    }

    [Fact]
    public void NestedDivision_InsideArrayIndex_ProducesRealFinding_EndToEnd()
    {
        // The B2-review standard applied to B3: a /0 nested in a bound B3 node must
        // produce the actual Calor0920 through the full pipeline, not merely a subtree.
        const string source = @"
§M{m001:Test}
  §F{f001:Trap:pub} () -> void
    §E{cw,alloc}
    §LIST{xs:i32}
      (/ 10 0)
    §/LIST{xs}
    §P §CNT{xs}";

        var result = Compiler.Program.Compile(source, "test.calr", new CompilationOptions
        {
            EnableVerificationAnalyses = true,
            VerificationAnalysisOptions = new Compiler.Analysis.VerificationAnalysisOptions
            {
                BugPatternOptions = new Compiler.Analysis.BugPatterns.BugPatternOptions
                {
                    ReportOnlyVerified = false
                }
            }
        });

        // Span-anchored (review m7): the finding must point INSIDE the initializer
        // (line 6 of the source), not merely exist somewhere in the file.
        Assert.Contains(result.Diagnostics,
            d => d.Code == DiagnosticCode.DivisionByZero && d.Span.Line == 6);
    }
}
