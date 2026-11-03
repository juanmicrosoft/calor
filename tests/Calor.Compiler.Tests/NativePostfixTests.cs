using System.Reflection;
using Calor.Compiler.Analysis;
using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class NativePostfixTests
{
    [Theory]
    [InlineData(4, 5)]
    [InlineData(1, 2)]
    public void LegacyLoopHeader_NestedSiblingOperandsRetainCheckedRuntimeSemantics(int a, int b)
    {
        const string source = """
            §M{Native}
              §F{Probe:pub} (int:a, int:b) -> int
                §E{}
                §B{~count:int} 0
                §L{i:0:(+ (- a 1) (- b 2)):1}
                  §ASSIGN count (+ count 1)
                §R count
            """;
        var result = Program.Compile(source);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        Assert.Contains("checked(checked(a - 1) + (checked(b - 2)))", result.GeneratedCode);
        Assert.Equal(a + b - 2, Invoke(Compile(source), "Probe", a, b));
        var overflow = Assert.Throws<TargetInvocationException>(() =>
            Invoke(Compile(source), "Probe", int.MinValue, 2));
        Assert.IsType<OverflowException>(overflow.InnerException);
    }

    [Theory]
    [InlineData("dict.GetOrAdd(key, () => new V { P = 1 })")]
    [InlineData("dict.GetOrAdd(key, () => new V { P = 1 }),8")]
    public void CSharpLambdaInitializerHole_PreservesTheOriginalRawSource(string hole)
    {
        var ast = Parse(Source($"\"${{{hole}}}\"", type: "string"));
        var interpolation = Assert.IsType<InterpolatedStringNode>(Assert.IsType<ReturnStatementNode>(
            Assert.Single(Assert.Single(ast.Functions).Body)).Expression);
        var part = Assert.IsType<InterpolatedStringExpressionNode>(Assert.Single(interpolation.Parts));
        Assert.IsType<RawCSharpExpressionNode>(part.Expression);
        Assert.Contains("$\"{" + hole + "}\"", ast.Accept(new CSharpEmitter()));
    }

    [Theory]
    [InlineData("new Lazy<System.Text.StringBuilder>(() => new System.Text.StringBuilder { Capacity = 8 }).Value.Capacity", "8")]
    [InlineData("((Func<int>)(() => 9))()", "9")]
    [InlineData("Array.IndexOf(new int[] { 1, 2 }, 2)", "1")]
    public void UnsupportedCSharpHoleChildren_KeepTheirRawRuntimeSemantics(string hole, string expected)
    {
        var source = Source($"\"${{{hole}}}\"", type: "string");
        var result = Program.Compile(source);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Calor0419");
        Assert.Equal(expected, Invoke(Compile(source, enforceEffects: false), "Probe"));
    }

    [Fact]
    public void LegacyTypeArrayWitness_PreservesBothElementsThroughRuntimeAndSourceRoundTrip()
    {
        const string source = """
            §M{Native}
              §F{Probe:pub} () -> System.Type[]
                §E{alloc}
                §B{~_hoist000} §ARR{System.Type} (typeof Model1Validator) (typeof Model2Validator) §/ARR
                §R _hoist000
              §CL{Model1Validator:pub}
              §CL{Model2Validator:pub}
            """;
        var array = Assert.IsType<ArrayCreationNode>(Assert.IsType<BindStatementNode>(
            Assert.Single(Parse(source).Functions).Body[0]).Initializer);
        Assert.Equal(2, array.Initializer.Count);
        foreach (var candidate in new[] { source, Parse(source).Accept(new CalorEmitter()) })
        {
            var types = Assert.IsType<Type[]>(Invoke(Compile(candidate), "Probe"));
            Assert.Equal(["Native.Model1Validator", "Native.Model2Validator"],
                types.Select(type => type.FullName));
        }
    }

    [Theory]
    [InlineData("§ARR{int} (cast int 1) (cast int 2) §/ARR")]
    [InlineData("§ARR{int} §A (cast int 1) (cast int 2) §/ARR")]
    [InlineData("§ARR{int} §A (cast int 1) §A (cast int 2) §/ARR")]
    [InlineData("§LIST{int} (cast int 1) (cast int 2) §/LIST")]
    [InlineData("§HSET{int} (cast int 1) (cast int 2) §/HSET")]
    [InlineData("§DICT{int:int} §KV (cast int 1) (cast int 10) §KV (cast int 2) (cast int 20) §/DICT")]
    [InlineData("§ARR2D{int} §ROW (cast int 1) (cast int 2) §/ARR2D")]
    [InlineData("§ARR{int} (Echo (1)) (Echo (2)) §/ARR")]
    [InlineData("§ARR{int} §A (Echo (1)) (Echo (2)) §/ARR")]
    [InlineData("§ARR{int} Echo(Echo (1)) (Echo (2)) §/ARR")]
    [InlineData("§LIST{int} Echo(1) Echo(2) §/LIST")]
    [InlineData("§HSET{int} (Echo (1)) (Echo (2)) §/HSET")]
    [InlineData("§DICT{int:int} §KV (Echo (1)) (Echo (10)) §KV (Echo (2)) (Echo (20)) §/DICT")]
    [InlineData("§ARR2D{int} §ROW (Echo (1)) (Echo (2)) §/ARR2D")]
    public void LegacyCollectionElements_KeepTheirBoundariesAndExplicitNativeCallDelimiters(string expression)
    {
        var source = $$"""
            §M{Native}
              §F{Echo:priv} (int:value) -> int
                §E{}
                §R value
              §F{Probe:pub} () -> object
                §E{alloc}
                §B{values} {{expression}}
                §R values
            """;
        foreach (var candidate in new[] { source, Parse(source).Accept(new CalorEmitter()) })
        {
            var value = Invoke(Compile(candidate), "Probe");
            if (value is IDictionary<int, int> dictionary)
            {
                Assert.Equal(2, dictionary.Count);
                Assert.Equal(10, dictionary[1]);
                Assert.Equal(20, dictionary[2]);
            }
            else if (value is int[,] matrix)
            {
                Assert.Equal(1, matrix.GetLength(0));
                Assert.Equal(2, matrix.GetLength(1));
                Assert.Equal(1, matrix[0, 0]);
                Assert.Equal(2, matrix[0, 1]);
            }
            else if (value is HashSet<int> set)
                Assert.True(set.SetEquals([1, 2]));
            else
                Assert.Equal([1, 2], Assert.IsAssignableFrom<IEnumerable<int>>(value));
        }
    }

    [Fact]
    public void Chaining_UsesExistingStructuralNodesAndCompleteSpans()
    {
        const string expression = "Factory().Items[Next(), 1].Pick<int>(second: in value, first: ref other)";
        var call = Assert.IsType<ExpressionCallNode>(Expression(expression));
        Assert.Equal(expression.Length, call.Span.Length);
        Assert.Equal(["int"], call.TypeArguments);
        Assert.Equal(["second", "first"], call.ArgumentNames);
        Assert.Equal(["in", "ref"], call.ArgumentModifiers);
        var method = Assert.IsType<FieldAccessNode>(call.TargetExpression);
        Assert.Equal("Pick", Slice(expression, call.Span.Start, method.FieldNameSpan));
        Assert.Equal("value", Slice(expression, call.Span.Start, call.Arguments[0].Span));
        var element = Assert.IsType<MultiDimArrayAccessNode>(method.Target);
        Assert.Equal(2, element.Indices.Count);
        Assert.IsType<CallExpressionNode>(element.Indices[0]);
        var items = Assert.IsType<FieldAccessNode>(element.Array);
        Assert.IsType<CallExpressionNode>(items.Target);
    }

    [Fact]
    public void CallsGroupsTuplesAndComparisons_RemainDistinct()
    {
        Assert.Empty(Assert.IsType<CallExpressionNode>(Expression("f()")).Arguments);
        Assert.IsType<ReferenceNode>(Expression("(f)"));
        Assert.IsType<TupleLiteralNode>(Expression("(f, x)"));
        Assert.IsType<CallExpressionNode>(Expression("(f())"));
        Assert.IsType<ArrayAccessNode>(Expression("(items[0])"));
        Assert.IsType<CallExpressionNode>(Expression("(f) (x)"));
        Assert.IsType<ExpressionCallNode>(Expression("factory()(x)"));
        Assert.IsType<ExpressionCallNode>(Expression("(gate ? first : second)(x)"));
        var generic = Assert.IsType<CallExpressionNode>(Expression("M < Dictionary<string, List<int>> > (x)"));
        Assert.Equal("M", generic.Target);
        Assert.Equal(["Dictionary<string,List<int>>"], generic.TypeArguments!.Select(type => type.Replace(" ", "")));
        generic = Assert.IsType<CallExpressionNode>(Expression("M<(int First, string Second)>(x)"));
        Assert.Equal(["(int First,string Second)"], generic.TypeArguments!.Select(type => type.Replace(", ", ",")));
        Assert.Equal(["@class"], Assert.IsType<CallExpressionNode>(Expression("M<@class>(x)")).TypeArguments);
        Assert.IsType<TupleLiteralNode>(Expression("(M<T, U>(x), y < z)"));
        var relational = Assert.IsType<BinaryOperationNode>(Expression("a < b > c"));
        Assert.Equal(BinaryOperator.GreaterThan, relational.Operator);
        Assert.Equal(BinaryOperator.LessThan, Assert.IsType<BinaryOperationNode>(relational.Left).Operator);
        Assert.IsType<BinaryOperationNode>(Expression("(a) < b > (c)"));
        Assert.IsType<UnaryOperationNode>(Expression("-f () * 2") is BinaryOperationNode product
            ? product.Left : throw new InvalidOperationException());
    }

    [Fact]
    public void GenericCallLookahead_DoesNotJoinTheFollowingPhysicalLine()
    {
        var body = Assert.Single(Parse("""
            §M{Native}
              §F{Probe:pub} () -> void
                §B{selected} M<int>
                (f)
            """).Functions).Body;
        Assert.Equal("M<int>", Assert.IsType<ReferenceNode>(
            Assert.IsType<BindStatementNode>(body[0]).Initializer).Name);
        Assert.IsType<ReferenceNode>(Assert.IsType<ExpressionStatementNode>(body[1]).Expression);
    }

    [Theory]
    [InlineData("(obj.Child.Pick)(value)", "Pick")]
    [InlineData("obj.@class(value)", "@class")]
    [InlineData("obj . @class(value)", "@class")]
    [InlineData("Factory().@class(value)", "@class")]
    public void MemberIdentifierSpans_ExcludeGroupingAndRetainEscapes(string expression, string identifier)
    {
        var call = Assert.IsType<ExpressionCallNode>(Expression(expression));
        var member = Assert.IsType<FieldAccessNode>(call.TargetExpression);
        Assert.Equal(expression.Length, call.Span.Length);
        Assert.Equal(identifier, Slice(expression, call.Span.Start, member.FieldNameSpan));
    }

    [Theory]
    [InlineData("@this.Pick(value)", "this")]
    [InlineData("@base.Pick(value)", "base")]
    public void EscapedReceiverNames_AreValuesNotInstanceKeywords(string expression, string name)
    {
        var call = Assert.IsType<ExpressionCallNode>(Expression(expression));
        var member = Assert.IsType<FieldAccessNode>(call.TargetExpression);
        Assert.Equal(name, Assert.IsType<ReferenceNode>(member.Target).Name);
        Assert.Equal(expression.Length, call.Span.Length);
        Assert.Equal(expression, call.Accept(new CSharpEmitter()));
    }

    [Theory]
    [InlineData("§ASSIGN x (+ 1 2)")]
    [InlineData("§SETIDX{items} 0 (+ 1 2)")]
    [InlineData("§PUT{map} \"x\" (+ 1 2)")]
    public void LegacyPositionalOperands_KeepTheirGroupedValue(string statement)
    {
        var body = Assert.Single(Parse(Source(statement, statement: true)).Functions).Body;
        var value = Assert.Single(body) switch
        {
            AssignmentStatementNode assignment => assignment.Value,
            CollectionSetIndexNode set => set.Value,
            DictionaryPutNode put => put.Value,
            _ => throw new InvalidOperationException()
        };
        Assert.IsType<BinaryOperationNode>(value);
        Assert.IsType<BinaryOperationNode>(Expression("(+ x (+ 1 2))"));
        Assert.IsType<CallExpressionNode>(Assert.IsType<BinaryOperationNode>(
            Expression("(+ f() g())")).Left);
    }

    [Fact]
    public void LegacyNestedOperands_KeepTheirOuterOperandBoundary()
    {
        var assignment = Assert.IsType<AssignmentStatementNode>(Assert.Single(
            Assert.Single(Parse(Source("§ASSIGN §IDX items 0 (+ §IDX items 0 4)", statement: true)).Functions).Body));
        Assert.IsType<IntLiteralNode>(Assert.IsType<ArrayAccessNode>(assignment.Target).Index);
        Assert.IsType<BinaryOperationNode>(assignment.Value);
        Assert.IsType<ArrayAccessNode>(Assert.IsType<BinaryOperationNode>(
            Expression("(+ §IDX items 0 (+ 1 2))")).Left);
        Assert.IsType<ArrayLengthNode>(Assert.IsType<BinaryOperationNode>(
            Expression("(+ §LEN items (+ 1 2))")).Left);
        Assert.IsType<CallExpressionNode>(Assert.IsType<BinaryOperationNode>(
            Expression("(+ f(g (1), items[h (2)]) 3)")).Left);
    }

    [Theory]
    [InlineData("f(1,)")]
    [InlineData("f(ref)")]
    [InlineData("f(first:)")]
    [InlineData("f(, 1)")]
    [InlineData("f<>(1)")]
    [InlineData("f<int,>(1)")]
    [InlineData("items[]")]
    [InlineData("items[0,]")]
    [InlineData("items[0")]
    [InlineData("factory().")]
    [InlineData("x?.M(1)")]
    [InlineData("x?.Items[0]")]
    [InlineData("x?[0]")]
    [InlineData("factory()?.M(1)")]
    [InlineData("x?.Factory().M()")]
    public void MalformedAndUnsupportedConditionalForms_ReportErrors(string expression)
    {
        var diagnostics = new DiagnosticBag();
        Parse(Source(expression), diagnostics);
        Assert.True(diagnostics.HasErrors, expression);
    }

    [Fact]
    public void MissingArgument_LeavesTheNextStatementForRecovery()
    {
        var diagnostics = new DiagnosticBag();
        var module = Parse("""
            §M{Native}
              §F{Probe:pub} () -> int
                f(
                §R 7
            """, diagnostics);
        Assert.True(diagnostics.HasErrors);
        Assert.Equal(7, Assert.IsType<IntLiteralNode>(Assert.IsType<ReturnStatementNode>(
            Assert.Single(module.Functions).Body.Last()).Expression).Value);
    }

    [Fact]
    public void NativeMemberBinding_RetainsExactDeclarationsAndMetadata()
    {
        var (ast, bound, diagnostics) = Bind("""
            §M{Native}
              §F{Use:pub} (Holder:h) -> int
                §R Get(h).Pick(value: 3)
              §F{Get:pub} (Holder:h) -> Holder
                §R h
              §CL{Holder:pub}
                §MT{Pick:pub} (int:value) -> int
                  §R value
                §MT{Pick:pub} (string:value) -> string
                  §R value
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var call = Assert.IsType<BoundCallExpression>(Assert.IsType<BoundReturnStatement>(
            Assert.Single(use.Body)).Expression);
        Assert.Same(call.EvaluatedReceiver, call.Children[0]);
        Assert.Same(call.Arguments[0], call.Children[1]);
        Assert.Same(bound.Functions.Single(function => function.Symbol.Name == "Holder.Pick"
            && function.Symbol.ReturnType == "int").Symbol, call.ResolvedSymbol);
        Assert.Contains($"Holder.{ast.Classes[0].Methods[0].Id}",
            CallGraphAnalysis.Build(ast).ResolveCallSites(ast.Functions[0].Id, "<expression-call>", call.Span));

        (_, bound, diagnostics) = Bind("""
            §M{Native}
              §F{Array:pub} (System.Globalization.CultureInfo:culture) -> string[]
                §R culture.DateTimeFormat.GetAllDateTimePatterns()
              §F{Generic:pub} (System.Collections.ArrayList:items) -> IEnumerable<int>
                §R items.Cast<int>()
              §F{Static:pub} (int:x) -> int
                §R Math.Abs(x)
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var calls = BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>().ToArray();
        var array = calls.Single(call => call.ResolvedMetadataMethod?.Name == "GetAllDateTimePatterns");
        Assert.Same(array.ResolvedMetadataMethod!.ReturnType, array.MetadataArrayResultType);
        Assert.Equal(SpecialType.System_String, array.MetadataArrayResultType!.ElementType.SpecialType);
        var generic = calls.Single(call => call.ResolvedMetadataMethod?.Name == "Cast");
        Assert.Equal(SpecialType.System_Int32, Assert.Single(generic.ResolvedMetadataMethod!.TypeArguments).SpecialType);
        Assert.Equal("System.Math", calls.Single(call => call.ResolvedMetadataMethod?.Name == "Abs")
            .ResolvedMetadataMethod!.ContainingType.ToDisplayString());
    }

    [Theory]
    [InlineData("h.Pick(true)")]
    [InlineData("h.Pick(value: 1, value: 2)")]
    public void NativeOverloadErrors_AreNotHidden(string expression)
    {
        var (_, _, diagnostics) = Bind($$"""
            §M{Native}
              §F{Use:pub} (Holder:h) -> int
                §R {{expression}}
              §CL{Holder:pub}
                §MT{Pick:pub} (int:value) -> int
                  §R value
            """);
        Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.NoMatchingOverload);
    }

    [Fact]
    public void NativeGenericAndStaticCalls_KeepNativeIdentityRatherThanBclAliases()
    {
        var source = """
            §M{Native}
              §F{Probe:pub} () -> int
                §R Random.Echo<int>(value: 7)
              §CL{Random:pub}
                §MT{Echo<T>:pub:stat} (T:value) -> T
                  §R value
            """;
        var (_, bound, diagnostics) = Bind(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.Same(bound.Functions.Single(function => function.Symbol.Name == "Random.Echo").Symbol, call.ResolvedSymbol);
        Assert.Equal(["int"], call.TypeArguments);
        Assert.Null(call.ResolvedMetadataMethod);
        Assert.IsType<BoundTypeReferenceExpression>(call.EvaluatedReceiver);
        Assert.Equal(7, Invoke(Compile(source), "Probe"));
        Assert.Equal(3, Invoke(Compile(Source("System.Math.Abs(-3)")), "Probe"));
    }

    [Theory]
    [InlineData("  §F{Use:pub}<T> () -> int\n    §R T.Pick()")]
    [InlineData("  §CL{Container:pub}<T>\n    §MT{Use:pub:stat} () -> int\n      §R T.Pick()")]
    [InlineData("  §CL{Container:pub}\n    §MT{Use:pub:stat}<T> () -> int\n      §R T.Pick()")]
    public void TypeParameterReceivers_DoNotBindAnUnrelatedSourceClass(string declaration)
    {
        var source = $$"""
            §M{Native}
            {{declaration}}
              §CL{T:pub}
                §MT{Pick:pub:stat} () -> int
                  §R 1
            """;
        var (_, bound, diagnostics) = Bind(source);
        Assert.True(diagnostics.HasErrors);
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.Null(call.ResolvedSymbol);
        Assert.IsType<BoundVariableExpression>(call.EvaluatedReceiver);
        var result = Program.Compile(source, "native-postfix.calr", new CompilationOptions
            { EnableTypeChecking = true, EnforceEffects = true, StatusWriter = TextWriter.Null });
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LexicalValueReceiver_ShadowingAClassRetainsItsRealMethodIdentity(bool inherited)
    {
        var ownerMember = inherited ? "§EXT{Parent}" : "§FLD{Holder:T:pub:stat}";
        var (_, bound, diagnostics) = Bind($$"""
            §M{Native}
              §CL{Owner:pub}
                {{ownerMember}}
                §MT{Use:pub:stat} () -> int
                  §R T.Pick()
              §CL{Parent:pub}
                §FLD{Holder:T:pub:stat}
              §CL{T:pub}
                §MT{Pick:pub:stat} () -> int
                  §R 1
              §CL{Holder:pub}
                §MT{Pick:pub} () -> int
                  §R 2
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.Same(bound.Functions.Single(function => function.Symbol.Name == "Holder.Pick").Symbol,
            call.ResolvedSymbol);
        Assert.IsType<BoundVariableExpression>(call.EvaluatedReceiver);
    }

    [Fact]
    public void TypeParameterReceiver_DoesNotBindUnrelatedBclMetadata()
    {
        var source = """
            §M{Native}
              §F{Use:pub}<Math> () -> int
                §R Math.Abs(-3)
            """;
        var (_, bound, diagnostics) = Bind(source);
        Assert.True(diagnostics.HasErrors);
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.Null(call.ResolvedSymbol);
        Assert.Null(call.ResolvedMetadataMethod);
        Assert.IsType<BoundVariableExpression>(call.EvaluatedReceiver);
        var result = Program.Compile(source, "native-postfix.calr", new CompilationOptions
            { EnableTypeChecking = true, EnforceEffects = true, StatusWriter = TextWriter.Null });
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
    }

    [Fact]
    public void NestedTypeReceiver_RetainsItsLexicalDeclarationIdentity()
    {
        var (_, bound, diagnostics) = Bind("""
            §M{Native}
              §CL{Owner:pub}
                §MT{Use:pub:stat} () -> int
                  §R T.Pick()
                §CL{T:pub}
                  §MT{Pick:pub:stat} () -> int
                    §R 2
              §CL{T:pub}
                §MT{Pick:pub:stat} () -> int
                  §R 1
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.Same(bound.Functions.Single(function => function.Symbol.Name == "Owner.T.Pick").Symbol,
            call.ResolvedSymbol);
        Assert.Equal(call.ResolvedSymbol!.ContainingTypeName,
            Assert.IsType<BoundTypeReferenceExpression>(call.EvaluatedReceiver).ResolvedType!.QualifiedName);
    }

    [Theory]
    [InlineData("missing.Pick(1)", "missing")]
    [InlineData("System.Math.Abs(missing)", "missing")]
    [InlineData("System.Math.Abs(GetMissing())", "GetMissing")]
    public void DeferredReceiverClassification_DoesNotHideUndefinedInputs(string expression, string name)
    {
        var result = Program.Compile(Source(expression), "native-postfix.calr", new CompilationOptions
            { EnableTypeChecking = true, EnforceEffects = false, StatusWriter = TextWriter.Null });
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Message.Contains(name, StringComparison.Ordinal));
    }

    [Fact]
    public void NativeUnknownCalls_StillFailClosed()
    {
        var result = Program.Compile("""
            §M{Native}
              §F{Use:pub} (System.Random:random) -> void
                §E{}
                random.DoesNotExist()
            """, "native-postfix.calr", new CompilationOptions
            { EnableTypeChecking = true, EnforceEffects = true, StatusWriter = TextWriter.Null });
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
    }

    [Fact]
    public void NativeConditionalDelegate_StillInvalidatesCapturedFacts()
    {
        var (_, _, diagnostics) = Bind("""
            §M{Native}
              §F{Use:pub} (?string:input, bool:gate) -> int
                §B{~value:?string} input
                §B{later:Action} §LAM{}
                  §ASSIGN value null
                §/LAM
                §IF value != null
                  (gate ? later : later)()
                  §B{observed:string} value
                  §R 1
                §R 0
            """);
        Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == "Calor0272");
    }

    [Fact]
    public void NativeCalls_PreserveOrderedIndexReceiverAndArgumentsAtRuntime()
    {
        var type = Compile(RuntimeSource("""
            GetLists(trace, items)[Index(trace)].Add(Number(trace, 3))
            §R items[0].IndexOf(3)
            """));
        var trace = new List<int>();
        var items = new[] { new List<int>() };
        Assert.Equal(0, Invoke(type, "Probe", trace, items));
        Assert.Equal([1, 2, 3], trace);
        Assert.Equal([3], items[0]);
    }

    [Theory]
    [InlineData("false && GetLists(trace, items)[Index(trace)].Contains(Number(trace, 3))", false, false)]
    [InlineData("true || GetLists(trace, items)[Index(trace)].Contains(Number(trace, 3))", true, false)]
    [InlineData("true && GetLists(trace, items)[Index(trace)].Contains(Number(trace, 3))", true, true)]
    [InlineData("false ? GetLists(trace, items)[Index(trace)].Contains(Number(trace, 3)) : true", true, false)]
    [InlineData("true ? GetLists(trace, items)[Index(trace)].Contains(Number(trace, 3)) : false", true, true)]
    public void NativeCalls_StayInsideLazyRegions(string expression, bool expected, bool evaluated)
    {
        var type = Compile(RuntimeSource($"§R {expression}", "bool"));
        var trace = new List<int>();
        Assert.Equal(expected, Invoke(type, "Probe", trace, new[] { new List<int> { 3 } }));
        Assert.Equal(evaluated ? [1, 2, 3] : Array.Empty<int>(), trace);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1 / zero")]
    public void NativeExceptions_StopBeforeLaterOperands(string index)
    {
        var type = Compile(RuntimeSource(
            $"GetLists(trace, items)[{index}].Add(Number(trace, 3))\n§R 0", zero: true));
        var trace = new List<int>();
        var error = Assert.Throws<TargetInvocationException>(() =>
            Invoke(type, "Probe", trace, Array.Empty<List<int>>(), 0));
        Assert.True(error.InnerException is IndexOutOfRangeException or DivideByZeroException);
        Assert.Equal([1], trace);
    }

    [Fact]
    public void NativeArgumentExceptions_StopBeforeLaterArguments()
    {
        var type = Compile(RuntimeSource(
            "GetLists(trace, items)[0].Insert(Number(trace, 2) / zero, Number(trace, 3))\n§R 0", zero: true));
        var trace = new List<int>();
        var items = new[] { new List<int>() };
        var error = Assert.Throws<TargetInvocationException>(() => Invoke(type, "Probe", trace, items, 0));
        Assert.IsType<DivideByZeroException>(error.InnerException);
        Assert.Equal([1, 2], trace);
        Assert.Empty(items[0]);
    }

    [Theory]
    [InlineData("9", 9, false)]
    [InlineData("null", 0, true)]
    public void NativeCalls_StayInsideCoalescingFallbacks(string value, int expected, bool evaluated)
    {
        var type = Compile(RuntimeSource(
            $"§B{{value:?int}} {value}\n§R value ?? GetLists(trace, items)[Index(trace)].IndexOf(Number(trace, 3))"));
        var trace = new List<int>();
        Assert.Equal(expected, Invoke(type, "Probe", trace, new[] { new List<int> { 3 } }));
        Assert.Equal(evaluated ? [1, 2, 3] : Array.Empty<int>(), trace);
    }

    [Theory]
    [InlineData(true, 8)]
    [InlineData(false, 9)]
    public void NativeConditionalDelegate_ExecutesOnlyTheSelectedTarget(bool gate, int expected)
    {
        var type = Compile("""
            §M{Native}
              §F{Probe:pub} (bool:gate, Func<int,int>:first §E{}, Func<int,int>:second §E{}) -> int
                §E{}
                §R (gate ? first : second)(7)
            """);
        var calls = new List<int>();
        Func<int, int> first = value => { calls.Add(1); return value + 1; };
        Func<int, int> second = value => { calls.Add(2); return value + 2; };
        Assert.Equal(expected, Invoke(type, "Probe", gate, first, second));
        Assert.Equal(gate ? [1] : new[] { 2 }, calls);
    }

    [Theory]
    [InlineData("ref", 7)]
    [InlineData("out", 7)]
    [InlineData("in", 2)]
    public void NativeNamesAndModifiers_RoundTripAndExecute(string modifier, int expected)
    {
        var source = $$"""
            §M{Native}
              §F{Probe:pub} (Holder:h) -> int
                §E{mut}
                §B{~value:int} 2
                h.Update(item: {{modifier}} value)
                §R value
              §CL{Holder:pub}
                §MT{Update:pub} (int:item:{{modifier}}) -> void
                  §E{mut}
                  {{(modifier == "in" ? "§B{observed:int} item" : "§ASSIGN item 7")}}
            """;
        var ast = Parse(source);
        var roundTrip = ast.Accept(new CalorEmitter());
        var type = Compile(roundTrip);
        var holder = Activator.CreateInstance(type.Assembly.GetType("Native.Holder")!);
        Assert.Equal(expected, Invoke(type, "Probe", holder));
        Assert.Contains($"item: {modifier} value", roundTrip);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeFactoryReturnRows_RemainEnforced(bool permitsWrite)
    {
        var result = Program.Compile($$"""
            §M{Native}
              §F{Use:pub} (Holder:h) -> int
                §E{ {{(permitsWrite ? "cw" : "")}} }
                §R h.Make()()
              §CL{Holder:pub}
                §MT{Make:pub} () -> Func<int> §E{cw}
                  §E{}
                  §R §LAM{} 0 §/LAM
            """, "native-postfix.calr", new CompilationOptions
            { EnableTypeChecking = true, EnforceEffects = true, StatusWriter = TextWriter.Null });
        Assert.Equal(!permitsWrite, result.HasErrors);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.EffectRowUnknown);
        if (!permitsWrite)
            Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == "Calor0410");
    }

    [Fact]
    public void ComputedGenericSerialization_ReparsesAndKeepsSelectedBclMethod()
    {
        var expression = new ExpressionCallNode(default,
            new FieldAccessNode(default, new NewExpressionNode(default, "System.Collections.ArrayList", [], []), "Cast"),
            [], typeArguments: ["i32"]);
        var text = expression.Accept(new CalorEmitter());
        Assert.EndsWith(".Cast<int>()", text);
        var (_, bound, diagnostics) = Bind(Source(text, type: "IEnumerable<int>"));
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.Equal("Cast", call.ResolvedMetadataMethod?.Name);
        Assert.Equal(SpecialType.System_Int32, Assert.Single(call.ResolvedMetadataMethod!.TypeArguments).SpecialType);
    }

    [Fact]
    public void CastDelegateInvocation_IsStructuralRatherThanARawStringTarget()
    {
        var result = new CSharpToCalorConverter(new ConversionOptions
            { UserManifestRoot = global::Calor.Tests.Shared.HermeticCalorHome.EmptyManifestRoot }).Convert("""
            public static class Probe {
                public static object Run(System.Func<object> value) => ((System.Func<object>)value)();
            }
            """);
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.DoesNotContain(result.Losses, loss => loss.Feature == "raw-call-target");
        var method = Assert.Single(Assert.Single(result.Ast!.Classes).Methods);
        var call = Assert.IsType<ExpressionCallNode>(
            Assert.IsType<ReturnStatementNode>(Assert.Single(method.Body)).Expression);
        Assert.IsType<TypeOperationNode>(call.TargetExpression);
        Assert.IsType<ExpressionCallNode>(Expression(call.Accept(new CalorEmitter())));
    }

    [Theory]
    [InlineData("nameof(n)", "n")]
    [InlineData("(nameof(n))", "n")]
    [InlineData("nameof(n).Length", "1")]
    [InlineData("nameof(n),3", "  n")]
    public void NameofInterpolation_KeepsItsExistingIntrinsicRoute(string hole, string expected)
    {
        var source = $$"""
            §M{Native}
              §F{Probe:pub} (int:n) -> string
                §R "${{{hole}}}"
            """;
        var interpolation = Assert.IsType<InterpolatedStringNode>(Assert.IsType<ReturnStatementNode>(
            Assert.Single(Assert.Single(Parse(source).Functions).Body)).Expression);
        Assert.IsType<RawCSharpExpressionNode>(Assert.IsType<InterpolatedStringExpressionNode>(
            Assert.Single(interpolation.Parts)).Expression);
        Assert.Equal(expected, Invoke(Compile(source), "Probe", 12));
    }

    [Theory]
    [InlineData("String.Concat(\"\\x41\")", "A")]
    [InlineData("String.Concat(\"${x}\")", "${x}")]
    [InlineData("String.Concat(\"\\x41\"),4", "   A")]
    [InlineData("String.Concat(\"${x}\"),8", "    ${x}")]
    [InlineData("String.Concat(String.Concat(\"\\x41\"))", "A")]
    [InlineData("String.Concat(\"${x}\").Length", "4")]
    public void NativeCallInterpolation_UnsafeStringOperandsKeepTheirCSharpRuntimeValue(string hole, string expected)
    {
        var source = $$"""
            §M{Native}
              §F{Probe:pub} (int:x) -> string
                §R "${{{hole}}}"
            """;
        var interpolation = Assert.IsType<InterpolatedStringNode>(Assert.IsType<ReturnStatementNode>(
            Assert.Single(Assert.Single(Parse(source).Functions).Body)).Expression);
        Assert.IsType<RawCSharpExpressionNode>(Assert.IsType<InterpolatedStringExpressionNode>(
            Assert.Single(interpolation.Parts)).Expression);
        Assert.Equal(expected, Invoke(Compile(source, enforceEffects: false), "Probe", 7));
    }

    [Fact]
    public void NativeNamedCallInterpolation_HasExactMetadataAndRealOutput()
    {
        const string source = """
            §M{Native}
              §F{Probe:pub} (int:n) -> string
                §R "v=${Math.Max(val1: n, val2: 3)}"
            """;
        var (_, bound, diagnostics) = Bind(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.Equal("System.Math", call.ResolvedMetadataMethod?.ContainingType.ToDisplayString());
        Assert.Equal("Max", call.ResolvedMetadataMethod?.Name);
        Assert.All(call.ResolvedMetadataMethod!.Parameters, parameter =>
            Assert.Equal(SpecialType.System_Int32, parameter.Type.SpecialType));
        var type = Compile(source);
        Assert.Equal("v=3", Invoke(type, "Probe", 1));
        Assert.Equal("v=5", Invoke(type, "Probe", 5));
    }

    private static string RuntimeSource(string body, string type = "int", bool zero = false) => $$"""
        §M{Native}
          §F{Probe:pub} (List<int>:trace, List<int>[]:items{{(zero ? ", int:zero" : "")}}) -> {{type}}
            §E{mut}
        {{string.Join("\n", body.Split('\n').Select(line => "    " + line))}}
          §F{GetLists:priv} (List<int>:trace, List<int>[]:items) -> List<int>[]
            §E{mut}
            trace.Add(1)
            §R items
          §F{Index:priv} (List<int>:trace) -> int
            §E{mut}
            trace.Add(2)
            §R 0
          §F{Number:priv} (List<int>:trace, int:value) -> int
            §E{mut}
            trace.Add(value)
            §R value
        """;

    private static string Source(string expression, bool statement = false, string type = "int") => $$"""
        §M{Native}
          §F{Probe:pub} () -> {{type}}
            {{(statement ? "" : "§R ")}}{{expression}}
        """;

    private static string Slice(string expression, int start, TextSpan span) =>
        expression.Substring(span.Start - start, span.Length);

    private static ExpressionNode Expression(string expression) =>
        Assert.IsType<ReturnStatementNode>(Assert.Single(Assert.Single(Parse(Source(expression)).Functions).Body)).Expression!;

    private static ModuleNode Parse(string source, DiagnosticBag? diagnostics = null)
    {
        var requireSuccess = diagnostics is null;
        diagnostics ??= new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        if (requireSuccess)
            Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        return module;
    }

    private static (ModuleNode Ast, BoundModule Bound, DiagnosticBag Diagnostics) Bind(string source)
    {
        var diagnostics = new DiagnosticBag();
        var ast = Parse(source, diagnostics);
        return (ast, new Calor.Compiler.Binding.Binder(diagnostics).Bind(ast), diagnostics);
    }

    private static object? Invoke(Type type, string name, params object?[] arguments) =>
        type.GetMethod(name)!.Invoke(null, arguments);

    private static Type Compile(string source, bool enforceEffects = true)
    {
        var result = Program.Compile(source, "native-postfix.calr", new CompilationOptions
            { EnableTypeChecking = true, EnforceEffects = enforceEffects, StatusWriter = TextWriter.Null });
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        var compilation = CSharpCompilation.Create("Postfix_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble + result.GeneratedCode)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emission = compilation.Emit(stream);
        Assert.True(emission.Success, string.Join("\n", emission.Diagnostics) + "\n" + result.GeneratedCode);
        return Assembly.Load(stream.ToArray()).GetType("Native.NativeModule", throwOnError: true)!;
    }
}
