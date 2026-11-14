using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Calor.Compiler.Analysis;
using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Mcp.Tools;
using Calor.Compiler.Migration;
using Calor.Compiler.Verification.Z3.Cache;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// 0.27 (#1595, #1541, #1524): collection and object initializers convert faithfully or are kept
/// as C# with a reported loss, and a class-level collection block is a hard error instead of
/// silently ending the class. The cross-surface convert, compile, run and compare checks are the
/// T1-INIT-* controls in <c>tests/TestData/InteropScope/T1/controls.json</c>
/// (<see cref="InteropScope.SharedInteropControlsTests"/>); this class holds the parser rule, the
/// per-shape conversion decisions, and the negative controls that prove the run-and-compare
/// check catches the old miscompilations. Historical method names remain stable even when
/// their current representation is counted member, type or whole-unit interop.
/// </summary>
public class InitializerConversionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HashSetInitializers_StayNative_AndPreserveDuplicateAndExceptionOrder(bool throws)
    {
        var source = $$"""
            using System;
            using System.Collections.Generic;
            public class Holder
            {
                public HashSet<int> Field = new HashSet<int> { Probe.Next(1), Probe.Next(1) };
                public HashSet<int> Property { get; } = new HashSet<int> { Probe.Next(2) };
                public List<HashSet<int>> Nested = new List<HashSet<int>> { new HashSet<int> { 3, 3 } };
            }
            public static class Probe
            {
                public static string Log = "";
                public static int Next(int value)
                {
                    Log += value;
                    if (value == 4 && {{throws.ToString().ToLowerInvariant()}})
                        throw new InvalidOperationException();
                    return value;
                }
                public static string Run()
                {
                    var holder = new Holder();
                    try
                    {
                        var local = new HashSet<int> { Next(4), Next(4), Next(5) };
                        return Log + ":" + holder.Field.Count + holder.Property.Count
                            + holder.Nested[0].Count + ":" + local.Count;
                    }
                    catch (InvalidOperationException)
                    {
                        return Log + ":throw";
                    }
                }
            }
            """;
        var result = Convert(source);
        Assert.True(result.Success, result.CalorSource);
        Assert.Empty(result.Context.Losses);
        var parsed = AssertShape(result, 112, 7, 0, 6);
        AssertFieldAccesses(parsed, "ref(Probe).Next", "ref(Probe).Next", "ref(Probe).Next",
            "ref(holder).Field", "ref(holder).Field.Count", "ref(holder).Property",
            "ref(holder).Property.Count", "ref(holder).Nested", "index(ref(holder).Nested,0).Count",
            "ref(local).Count");
        var generated = Compile(result.CalorSource!, enforceEffects: false);
        Assert.False(generated.HasErrors, string.Join("\n", generated.Diagnostics.Errors));
        var expected = throws ? "1124:throw" : "112445:111:2";
        Assert.Equal(expected, Run(source));
        Assert.Equal(expected, Run(generated.GeneratedCode));
    }

    [Fact]
    public void UninitializedSourceReferenceLocal_StaysNative_WithoutCreatingAnInstanceEarly()
    {
        const string source = """
            public class Value
            {
                public int N;
                public Value() { Probe.Log += "ctor;"; }
            }
            public static class Probe
            {
                public static string Log = "";
                public static string Run()
                {
                    Value assigned;
                    Log += "before;";
                    assigned = new Value { N = 7 };
                    return Log + assigned.N;
                }
            }
            """;
        var result = Convert(source);
        Assert.True(result.Success, result.CalorSource);
        Assert.Empty(result.Context.Losses);
        var parsed = AssertShape(result, 34, 1, 0, 4);
        AssertFieldAccesses(parsed, "ref(Probe).Log", "ref(Probe).Log", "ref(assigned).N");
        var generated = Compile(result.CalorSource!, enforceEffects: true);
        Assert.False(generated.HasErrors, string.Join("\n", generated.Diagnostics.Errors));
        Assert.Equal("before;ctor;7", Run(source));
        Assert.Equal(Run(source), Run(generated.GeneratedCode));
    }

    [Theory]
    [InlineData("public struct Value { public int N; public Value() { N = 9; Probe.Log += \"ctor;\"; } }")]
    [InlineData("public class Value { public int N; } public class Base { protected int @default = 3; }")]
    public void UnsupportedOrCollidingLocalDefault_RemainsPreserved(string declaration)
    {
        var baseType = declaration.Contains("class Base", StringComparison.Ordinal) ? " : Base" : "";
        var source = $$"""
            {{declaration}}
            public class Probe{{baseType}}
            {
                public static string Log = "";
                public static string Run()
                {
                    Value assigned;
                    Log += "before;";
                    assigned = new Value { N = 7 };
                    return Log + assigned.N;
                }
            }
            """;
        var result = Convert(source);
        Assert.True(result.Success, result.CalorSource);
        if (baseType.Length == 0)
        {
            var parsed = AssertShape(result, 18, 0, 0, 3, "default-expression");
            AssertFieldAccesses(parsed, "ref(Probe).Log", "ref(Probe).Log");
            AssertOriginalPayloads(parsed, source);
        }
        else
        {
            var parsed = ParseCalor(result.CalorSource!);
            AssertFieldAccesses(parsed, "ref(assigned).N");
            var nodes = AllNodes(parsed).ToArray();
            Assert.Equal(28, nodes.Length);
            Assert.Equal(4, nodes.Count(node => node is MethodNode or ClassFieldNode));
            Assert.Single(nodes.OfType<NewExpressionNode>());
            Assert.Empty(nodes.OfType<LambdaExpressionNode>());
            Assert.Empty(nodes.OfType<CSharpInteropBlockNode>());
            Assert.Empty(nodes.OfType<RawCSharpExpressionNode>());
            var original = Assert.Single(CSharpSyntaxTree.ParseText(source).GetRoot()
                .DescendantNodes().OfType<LocalDeclarationStatementSyntax>());
            Assert.Equal(Encoding.UTF8.GetBytes(original.ToFullString()),
                Encoding.UTF8.GetBytes(Assert.Single(nodes.OfType<RawCSharpNode>()).CSharpCode));
            var loss = Assert.Single(result.Context.Losses);
            Assert.Equal(ConversionLossKind.InteropPreserved, loss.Kind);
            Assert.Equal("default-member-collision", loss.Feature);
            Assert.Equal("Statement preserved as raw C#: Value assigned;", loss.Description);
            Assert.Equal("interop", loss.Path);
            Assert.Null(loss.Trigger);
        }
        var generated = Compile(result.CalorSource!, enforceEffects: false);
        Assert.False(generated.HasErrors, string.Join("\n", generated.Diagnostics.Errors));
        Assert.Equal(Run(source), Run(generated.GeneratedCode));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceFieldInitializer_InNoHeaderOperator_HasOnlyIntrinsicEffects(bool inherited)
    {
        var source = $$"""
            module InitializerEffects
              class Storage
                field int:Value
              class Box
                {{(inherited ? "extends Storage" : "field int:Value")}}
              class Source
                op implicit (Source:value) → Box
                  return new Box() { Value = 7 }
            """;
        var generated = Compile(source, enforceEffects: true);
        Assert.False(generated.HasErrors, string.Join("\n", generated.Diagnostics.Errors));
        Assert.DoesNotContain(generated.Diagnostics, diagnostic => diagnostic.Code == "Calor0411"
            && diagnostic.Message.Contains("set_Value", StringComparison.Ordinal));
        Assert.Equal("7", Run(generated.GeneratedCode + """

            public static class Probe
            {
                public static string Run() =>
                    ((Box)new Source()).Value.ToString();
            }
            """));
    }

    [Theory]
    [InlineData("field int:Other")]
    [InlineData("property int:Value\n      get\n      set\n        System.Console.WriteLine(value)")]
    [InlineData("property int:Value\n      get\n      set\n        Missing.External(value)")]
    public void SourcePropertyInitializer_InNoHeaderOperator_StillRejectsEffectfulOrUnknownSetter(string member)
    {
        var source = $$"""
            module InitializerEffects
              class Box
                {{member}}
              class Source
                op implicit (Source:value) → Box
                  return new Box() { Value = 7 }
            """;
        var generated = Compile(source, enforceEffects: true);
        Assert.Contains(generated.Diagnostics.Errors, diagnostic => diagnostic.Code == "Calor0423");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceFieldInitializer_StillChargesItsValueAndConstructor(bool constructorEffect)
    {
        var source = $$"""
            module InitializerEffects
              class Box
                field int:Value
                ctor ()
                  {{(constructorEffect ? "System.Console.WriteLine(1)" : "let value = 0")}}
              module Effects
                func Next () → int
                  ! cw
                  System.Console.WriteLine(2)
                  return 7
              class Source
                op implicit (Source:value) → Box
                  return new Box() { Value = {{(constructorEffect ? "7" : "Effects.Next()")}} }
            """;
        var generated = Compile(source, enforceEffects: true);
        Assert.Contains(generated.Diagnostics.Errors, diagnostic => diagnostic.Code == "Calor0423");
    }

    // ---- #1595: parser -----------------------------------------------------------------------

    [Theory]
    [InlineData("let List<int>:xs = new List<int>() { 1 }", "let")]
    [InlineData("let Dictionary<string,int>:d = new Dictionary<string,int>() { { \"a\", 1 } }", "let")]
    [InlineData("let HashSet<int>:s = new HashSet<int>() { 1 }", "let")]
    [InlineData("var List<int>:xs = new List<int>() { 1 }", "var")]
    [InlineData("var Dictionary<string,int>:d = new Dictionary<string,int>() { { \"a\", 1 } }", "var")]
    [InlineData("var HashSet<int>:s = new HashSet<int>() { 1 }", "var")]
    public void ClassLevelCollectionBlock_IsAHardError_AndLaterMembersSurvive(string block, string tag)
    {
        var source = $$"""
            module Repro
              class Holder
                field int:Before = 1
                {{block}}
                field int:After = 2
                func Get () → int
                  !
                  return After
              class Other
                field int:Y = 3
            """;
        var diagnostics = new Calor.Compiler.Diagnostics.DiagnosticBag();
        var tokens = new Calor.Compiler.Parsing.Lexer(source, diagnostics).TokenizeAllForParser();
        var module = new Calor.Compiler.Parsing.Parser(tokens, diagnostics).Parse();
        var error = Assert.Single(diagnostics.Errors);
        Assert.Equal("Calor0100", error.Code);
        Assert.Equal($"'{tag}' is a local binding and cannot appear in a class body; " +
            "declare a field or property with an inline initializer instead.", error.Message);
        Assert.Equal(4, error.Span.Line);
        // The parser keeps reading the class after the block: every later member and type is in the AST.
        var classes = module.Classes;
        var holder = Assert.Single(classes, c => c.Name == "Holder");
        Assert.Equal(new[] { "Holder", "Other" }, classes.Select(c => c.Name));
        Assert.Equal(new[] { "Before", "After" }, holder.Fields.Select(f => f.Name));
        var method = Assert.Single(holder.Methods);
        Assert.Equal("Get", method.Name);
        Assert.Equal("After", Assert.IsType<ReferenceNode>(
            Assert.IsType<ReturnStatementNode>(Assert.Single(method.Body)).Expression).Name);
        Assert.Equal("Y", Assert.Single(classes[1].Fields).Name);
        Assert.Empty(AllNodes(module).OfType<BindStatementNode>());
        var compiled = Compile(source, enforceEffects: false, expectParseSuccess: false);
        Assert.True(compiled.HasErrors);
        Assert.Equal("Calor0100", Assert.Single(compiled.Diagnostics.Errors).Code);
    }

    // ---- Converter decisions -----------------------------------------------------------------

    [Fact]
    public void IssueRepro_ConvertsNatively_WithoutLoss()
    {
        var result = Convert("""
            using System.Collections.Generic;
            using System.Text;
            public class Holder
            {
                public int Before = 1;
                public List<List<int>> X = new List<List<int>> { new List<int> { 1 } };
                public List<int> Xs = new List<int> { 5 };
                public StringBuilder Sb { get; set; } = new StringBuilder { Capacity = 64 };
                public int After = 2;
                public int Get() => After;
            }
            public class Other { public int Y = 3; }
            """);
        Assert.True(result.Success, string.Join("\n", result.Issues.Where(i => i.Severity == ConversionIssueSeverity.Error).Select(i => i.ToString())) + "\n" + result.CalorSource);
        Assert.Empty(result.Context.Losses);
        var calor = result.CalorSource!;
        var parsed = AssertShape(result, 38, 4, 0, 7);
        var holder = Assert.Single(parsed.Classes, type => type.Name == "Holder");
        var nested = Assert.IsType<NewExpressionNode>(
            Assert.Single(holder.Fields, field => field.Name == "X").DefaultValue);
        Assert.Equal(1, Assert.IsType<IntLiteralNode>(Assert.Single(
            Assert.IsType<NewExpressionNode>(Assert.Single(nested.CollectionInitializers).Arguments[0])
                .CollectionInitializers).Arguments[0]).Value);
        var sb = Assert.IsType<NewExpressionNode>(Assert.Single(holder.Properties).DefaultValue);
        var capacity = Assert.Single(sb.Initializers);
        Assert.Equal("Capacity", capacity.PropertyName);
        Assert.Equal(64, Assert.IsType<IntLiteralNode>(capacity.Value).Value);
        var csharp = Compile(calor, enforceEffects: false).GeneratedCode;
        Assert.Contains("new System.Collections.Generic.List<int>() { 5 }", csharp, StringComparison.Ordinal);
        Assert.Contains("Capacity = 64", csharp, StringComparison.Ordinal);
        Assert.Contains("class Other", csharp, StringComparison.Ordinal);
        Assert.Contains("After", csharp, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public Outer O = new Outer { Items = { 1, 2 } };", "nested-member-initializer")]
    [InlineData("public Outer O = new Outer { Inner = { A = 1 } };", "nested-member-initializer")]
    [InlineData("public Dictionary<string, int> D = new Dictionary<string, int> { [\"a\"] = 1 };", "dictionary-initializer")]
    [InlineData("public Bag B = new Bag { 1, 2 };", "collection-initializer")]
    [InlineData("public List<int> L = new() { 4, 5 };", "collection-initializer")]
    [InlineData("public List<int> L = new List<int>(8) { 4 };", "collection-initializer")]
    [InlineData("public System.Collections.Generic.List<int> L = new System.Collections.Generic.List<int> { 4 };", "collection-initializer")]
    [InlineData("public List<int> L = new List<int> { Next() + 1 };", "collection-initializer")]
    [InlineData("public int M() { var o = new Outer { V = Next() + 1 }; return o.V; }", "object-initializer-value")]
    [InlineData("public Outer O = new Outer { V = Take(1, new Outer()) + 1 };", "object-initializer-value")]
    [InlineData("public Outer O = new Outer { Value = new List<int>(Next()) };", "object-initializer-value")]
    [InlineData("public Outer O = new Outer { Value = default(int) };", "object-initializer-value")]
    [InlineData("public Outer O = new Outer { Value = (System.Func<int>)(() => new List<int>(Next()).Count) };", "object-initializer-value")]
    [InlineData("public List<object> X = new List<object> { new System.\n        Object() };", "multi-line-name")]
    [InlineData("public int M() { return V0 + Take(Next(), new Outer { V = Next() }); }", "conditional-expression-hoisting")]
    [InlineData("public int M() { var h = this; return h.Take2(new Outer { V = Next() }).V; }", "initializer-position")]
    [InlineData("public int M() { var t = (V0, new List<int> { Next() }); return t.Item1; }", "tuple")]
    [InlineData("public int M() { var a = new { A = V0, B = new List<int> { Next() } }; return a.A; }", "anonymous-object")]
    [InlineData("public Outer O = new Outer { Value = (int?)V0 };", "nullable-cast")]
    [InlineData("public Outer O = new Outer { Value = (System.Nullable<int>)V0 };", "nullable-cast")]
    [InlineData("public int M() { var a = new List<int>[2]; var i = 0; a[i] = new List<int> { Next() }; return 0; }", "initializer-position")]
    [InlineData("public int M() { var o = new Outer(); System.Func<int> f = () => { o = null; return 1; }; o.Items.Add(Take(1, new Outer { V = f() })); return 0; }", "conditional-expression-hoisting")]
    [InlineData("public int M() { var o = new Outer { Value = (System.Func<object>)(() => { return default(int); }) }; return 0; }", "object-initializer-value")]
    public void ShapeWithoutAFaithfulNativeForm_IsPreservedWithALoss(string member, string reason)
    {
        var result = Convert(ShapeSource(member));
        Assert.True(result.Success, string.Join("\n", result.Issues.Where(i => i.Severity == ConversionIssueSeverity.Error).Select(i => i.ToString())) + "\n" + result.CalorSource);
        var feature = reason is "dictionary-initializer" or "conditional-expression-hoisting"
            ? reason : "initializer-interop";
        var parsed = AssertShape(result, 81, 3, 0, 14, "explicit-interface", feature);
        AssertFieldAccesses(parsed, "ref(_items).Add", "ref(_items).GetEnumerator", "ref(o).V");
        AssertOriginalPayloads(parsed, ShapeSource(member));
        var holder = Assert.Single(parsed.Classes, type => type.Name == "Holder");
        var preserved = Assert.Single(holder.InteropBlocks);
        Assert.Equal(member, preserved.CSharpCode.Trim());
        Assert.Equal(member.StartsWith("public int M()", StringComparison.Ordinal)
            ? InteropMemberKind.Method : InteropMemberKind.Field, preserved.MemberKind);
        Assert.Equal(feature, preserved.FeatureName);
        Assert.Equal(new[] { "Next", "Take", "Take2" }, holder.Methods.Select(method => method.Name));
    }

    [Theory]
    [InlineData("public Outer O = new Outer { V = 3, W = -V0 };")]
    [InlineData("public HashSet<string> H = new HashSet<string> { \"a\", \"a\" };")]
    [InlineData("public static List<List<int>> L = new List<List<int>> { new List<int> { 1 }, new List<int>() };")]
    [InlineData("public int M() { var o = new Outer { V = Next(), W = Next() }; return o.V; }")]
    [InlineData("public int M() { var l = new List<int> { Next(), Next() }; return l[1]; }")]
    [InlineData("public Outer O = new Outer { V = Next() };")]
    [InlineData("public int M() { return Take(Next(), new Outer { V = Next() }); }")]
    [InlineData("public List<int> L = new List<int> { Next(), Take(Next(), null) };")]
    public void FaithfulShape_ConvertsNatively(string member)
    {
        var result = Convert(ShapeSource(member));
        Assert.True(result.Success, string.Join("\n", result.Issues.Where(i => i.Severity == ConversionIssueSeverity.Error).Select(i => i.ToString())) + "\n" + result.CalorSource);
        var shape = member switch
        {
            "public Outer O = new Outer { V = 3, W = -V0 };" => (86, 4, 15),
            "public HashSet<string> H = new HashSet<string> { \"a\", \"a\" };" => (88, 4, 15),
            "public static List<List<int>> L = new List<List<int>> { new List<int> { 1 }, new List<int>() };" => (95, 6, 15),
            "public int M() { var o = new Outer { V = Next(), W = Next() }; return o.V; }" => (91, 4, 15),
            "public int M() { var l = new List<int> { Next(), Next() }; return l[1]; }" => (95, 4, 15),
            "public Outer O = new Outer { V = Next() };" => (84, 4, 15),
            "public int M() { return Take(Next(), new Outer { V = Next() }); }" => (93, 4, 15),
            "public List<int> L = new List<int> { Next(), Take(Next(), null) };" => (90, 4, 15),
            _ => throw new InvalidOperationException(member)
        };
        // The shared Bag scaffold alone retains its explicit-interface member.
        var parsed = AssertShape(result, shape.Item1, shape.Item2, 0, shape.Item3, "explicit-interface");
        AssertFieldAccesses(parsed,
            member == "public int M() { var o = new Outer { V = Next(), W = Next() }; return o.V; }"
                ? ["ref(_items).Add", "ref(_items).GetEnumerator", "ref(o).V", "ref(o).V"]
                : ["ref(_items).Add", "ref(_items).GetEnumerator", "ref(o).V"]);
        AssertOriginalPayloads(parsed, ShapeSource(member));
        Assert.Empty(Assert.Single(parsed.Classes, type => type.Name == "Holder").InteropBlocks);
    }

    private static string ShapeSource(string member) => $$"""
        using System.Collections;
        using System.Collections.Generic;
        public class Outer
        {
            public List<int> Items { get; } = new List<int>();
            public Inner Inner { get; } = new Inner();
            public int V { get; set; }
            public int W { get; set; }
            public object Value { get; set; }
        }
        public class Inner { public int A { get; set; } }
        public class Bag : IEnumerable<int>
        {
            private readonly List<int> _items = new List<int>();
            public void Add(int item) { _items.Add(item); }
            public IEnumerator<int> GetEnumerator() => _items.GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
        public class Holder
        {
            public const int V0 = 4;
            private static int _n;
            public static int Next() => ++_n;
            public static int Take(int a, Outer o) => a * 10 + o.V;
            public Outer Take2(Outer o) => o;
            {{member}}
        }
        """;

    // ---- Verification-pass regressions (convert, compile, run, compare) ------------------------

    public static TheoryData<string, string, bool> VerificationPassShapes() => new()
    {
        // A `ref` parameter as the assignment receiver: Swap() replaces the aliased storage.
        { "ref-receiver", """
            using System.Collections.Generic;
            public class Slot { public List<int> Items; }
            public static class Probe
            {
                public static Slot Current, Other;
                static int Swap() { Current = Other; return 7; }
                static void Set(ref Slot s) { s.Items = new List<int> { Swap() }; }
                public static string Run()
                {
                    Current = new Slot(); Other = new Slot();
                    var old = Current;
                    Set(ref Current);
                    return (old.Items == null) + ":" + (Other.Items == null);
                }
            }
            """, true },
        // A constant index whose conversion to the key type runs user code.
        { "index-conversion", """
            using System.Collections.Generic;
            public class Key { public static implicit operator Key(int x) { Probe.N += 1; return new Key(); } }
            public static class Probe
            {
                public static int N;
                public static string Run()
                {
                    var d = new Dictionary<Key, List<int>>();
                    d[0] = new List<int> { N };
                    var read = -1;
                    foreach (var pair in d) read = pair.Value[0];
                    return read + ":" + N;
                }
            }
            """, true },
        // A struct `this` argument before an initializer that mutates the struct.
        { "struct-this", """
            public class X { public int V; }
            public struct Counter
            {
                public int N;
                static int Take(Counter c, X x) => c.N * 100 + x.V;
                int Change() { N = 10; return 7; }
                public int Go() { return Take(this, new X { V = Change() }); }
            }
            public static class Probe
            {
                public static string Run() => new Counter().Go().ToString();
            }
            """, true },
        // The copied fixture now preserves Publisher after the retired subscription fails parsing.
        { "scorecard-092-delegate-this-argument", """
            using System;
            public class CustomEventArgs : EventArgs { public string Message { get; set; } }
            public class Publisher
            {
                public event EventHandler<CustomEventArgs> MessageSent;
                public string Log = "";
                public Publisher() { MessageSent += OnSent; }
                void OnSent(object sender, CustomEventArgs e) { Log = Log + e.Message + ";"; }
                public void Send(string message)
                {
                    if (MessageSent != null)
                    {
                        MessageSent(this, new CustomEventArgs { Message = message });
                    }
                }
            }
            public static class Probe
            {
                public static string Run()
                {
                    var publisher = new Publisher();
                    publisher.Send("hi");
                    publisher.Send("there");
                    return publisher.Log;
                }
            }
            """, true },
        // `(this)` is `this` after its parentheses are stripped: the struct would be copied late.
        { "parenthesized-this", """
            public class X { public int V; }
            public struct Counter
            {
                public int N;
                static int Take(Counter c, X x) => c.N * 100 + x.V;
                int Change() { N = 10; return 7; }
                public int Go() { return Take((this), new X { V = Change() }); }
            }
            public static class Probe
            {
                public static string Run() => new Counter().Go().ToString();
            }
            """, true },
        // A struct local read by the initializer and mutated through a method call in the same
        // statement: a hoisted creation would copy it before the call.
        { "struct-local-mutated-by-call", """
            public struct Counter
            {
                public int N;
                public int Change() { N = 10; return 0; }
            }
            public class Notice { public Counter V; }
            public static class Probe
            {
                static int Read(Notice x) => x.V.N;
                static int Take(int a, Notice x) => a + x.V.N;
                public static string Run()
                {
                    var c = new Counter();
                    var first = Take(c.Change(), new Notice { V = c });
                    return first.ToString();
                }
            }
            """, true },
        // The same position with a constructor that replaces the delegate stays C#.
        { "constructor-replaces-receiver", """
            public class Notice
            {
                public string Text;
                public Notice() { Probe.Sink = n => Probe.Log += "second;"; }
            }
            public static class Probe
            {
                public static string Log = "";
                public static System.Action<Notice> Sink = n => Log += "first:" + n.Text + ";";
                static void Send(string text) { Sink(new Notice { Text = text }); }
                public static string Run() { Send("hi"); return Log; }
            }
            """, true },
        // An initialized event followed only by an initialized property: member order matters.
        { "event-then-property", """
            using System;
            public class X { public int V; }
            public class Holder
            {
                public event Action E = Probe.Make();
                public X P { get; } = new X { V = Probe.Next() };
            }
            public static class Probe
            {
                public static int N;
                public static int Next() { N += 1; return N; }
                static void Empty() { }
                public static Action Make() { N += 10; return Empty; }
                public static string Run() { N = 0; return new Holder().P.V.ToString(); }
            }
            """, true },
    };

    [Theory]
    [MemberData(nameof(VerificationPassShapes))]
    public void VerificationPassShape_RunsLikeTheOriginal_AndReportsItsInterop(string name, string source, bool expectsLoss)
    {
        var result = Convert(source);
        Assert.True(result.Success, name + ": " + string.Join("\n", result.Issues
            .Where(i => i.Severity == ConversionIssueSeverity.Error).Select(i => i.ToString())) + "\n" + result.CalorSource);
        // The event member is preserved through member escalation, which records its own feature.
        Assert.Equal(expectsLoss, result.Context.Losses.Any(loss => loss.Kind == ConversionLossKind.InteropPreserved));
        var shape = name switch
        {
            "ref-receiver" => (44, 2, 0, 5),
            "index-conversion" => (20, 1, 0, 2),
            "struct-this" or "parenthesized-this" => (37, 1, 0, 5),
            "scorecard-092-delegate-this-argument" => (27, 1, 0, 2),
            "struct-local-mutated-by-call" => (32, 0, 0, 5),
            "constructor-replaces-receiver" => (40, 0, 2, 5),
            "event-then-property" => (50, 2, 0, 7),
            _ => throw new InvalidOperationException(name)
        };
        var parsed = AssertShape(result, shape.Item1, shape.Item2, shape.Item3, shape.Item4,
            name == "scorecard-092-delegate-this-argument" ? "post-validation-fallback" : "initializer-interop");
        AssertFieldAccesses(parsed, name switch
        {
            "ref-receiver" => ["ref(old).Items", "ref(Other).Items"],
            "index-conversion" => ["ref(Probe).N", "ref(Probe).N"],
            "struct-this" or "parenthesized-this" =>
                ["ref(c).N", "ref(x).V", "new(Counter).Go", "call(new(Counter).Go).ToString"],
            "scorecard-092-delegate-this-argument" =>
                ["ref(publisher).Send", "ref(publisher).Send", "ref(publisher).Log"],
            "struct-local-mutated-by-call" => ["ref(x).V", "ref(x).V.N", "ref(x).V", "ref(x).V.N"],
            "constructor-replaces-receiver" => ["ref(Probe).Sink", "ref(Probe).Log", "ref(Probe).Log", "ref(n).Text"],
            "event-then-property" =>
                ["ref(Probe).Next", "new(Holder).P", "new(Holder).P.V", "new(Holder).P.V.ToString"],
            _ => throw new InvalidOperationException(name)
        });
        AssertOriginalPayloads(parsed, source);
        if (name == "scorecard-092-delegate-this-argument")
        {
            var block = Assert.Single(parsed.InteropBlocks);
            Assert.Equal(InteropMemberKind.Other, block.MemberKind);
            Assert.Equal("Converted Calor for 'global::Publisher' did not parse or did not survive the C# round trip; original C# preserved (#717).", block.Reason);
            Assert.DoesNotContain(parsed.Classes, type => type.Name == "Publisher");
            Assert.Equal("parse-failure", Assert.Single(result.Context.Losses).Trigger);
        }
        var generated = Compile(result.CalorSource!, enforceEffects: false);
        Assert.False(generated.HasErrors, name + ": " + string.Join("\n", generated.Diagnostics.Errors.Select(e => e.Message)));
        Assert.Equal(Run(source), Run(generated.GeneratedCode));
    }

    [Fact]
    public void NativeDelegateSnapshot_WithThisAndInitializer_PreservesTheCapturedReceiver()
    {
        const string source = """
            using System;
            public class Notice
            {
                public string Text;
                public Notice() { Probe.Sink = (sender, notice) => Probe.Log += "second:" + notice.Text + ";"; }
            }
            public class Publisher
            {
                public void Send(string message)
                {
                    var receiver = Probe.Sink;
                    var notice = new Notice { Text = message };
                    receiver(this, notice);
                }
            }
            public static class Probe
            {
                public static string Log = "";
                public static Publisher Publisher = new Publisher();
                public static Action<object, Notice> Sink = (sender, notice) =>
                    Log += (object.ReferenceEquals(sender, Publisher) ? "same:" : "wrong:") + notice.Text + ";";
                public static string Run()
                {
                    Publisher.Send("hi");
                    Publisher.Send("there");
                    return Log;
                }
            }
            """;
        var result = Convert(source);
        Assert.True(result.Success, result.CalorSource);
        var parsed = AssertShape(result, 80, 2, 2, 7);
        AssertFieldAccesses(parsed, "ref(Probe).Sink", "ref(Probe).Sink", "ref(Probe).Log", "ref(Probe).Log",
            "ref(notice).Text", "ref(notice).Text", "ref(object).ReferenceEquals",
            "ref(Publisher).Send", "ref(Publisher).Send");
        var send = Assert.Single(Assert.Single(parsed.Classes, type => type.Name == "Publisher").Methods);
        Assert.Equal(3, send.Body.Count);
        var receiver = Assert.IsType<BindStatementNode>(send.Body[0]);
        Assert.Equal("receiver", receiver.Name);
        var sink = Assert.IsType<FieldAccessNode>(receiver.Initializer);
        Assert.Equal("Sink", sink.FieldName);
        var owner = Assert.IsType<ReferenceNode>(sink.Target);
        Assert.Equal("Probe", owner.Name);
        Assert.False(owner.IsVerbatim);
        var bound = new Calor.Compiler.Binding.Binder(new Calor.Compiler.Diagnostics.DiagnosticBag()).Bind(parsed);
        var sinks = BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundFieldAccessExpression>()
            .Where(field => field.FieldName == "Sink").ToArray();
        Assert.Equal(2, sinks.Length);
        var selected = Assert.Single(sinks, field => field.Span == sink.Span).ResolvedField;
        Assert.NotNull(selected);
        Assert.Equal("Probe", selected.DeclaringTypeName);
        Assert.Equal("Sink", selected.Name);
        Assert.True(selected.IsStatic);
        Assert.Same(bound.SymbolsById[selected.Id], selected);
        Assert.All(sinks, field =>
        {
            Assert.Same(selected, field.ResolvedField);
            Assert.Same(Assert.Single(parsed.Classes, type => type.Name == "Probe"), field.SourceStaticFieldOwner);
            var target = Assert.IsType<BoundTypeReferenceExpression>(field.Target);
            Assert.Equal("Probe", target.Name);
            Assert.NotNull(target.ResolvedType);
        });
        var notice = Assert.IsType<BindStatementNode>(send.Body[1]);
        Assert.Equal("notice", notice.Name);
        Assert.Equal("Text", Assert.Single(Assert.IsType<NewExpressionNode>(notice.Initializer).Initializers).PropertyName);
        var call = Assert.IsType<CallExpressionNode>(Assert.IsType<ExpressionStatementNode>(send.Body[2]).Expression);
        Assert.Equal("receiver", call.Target);
        Assert.IsType<ThisExpressionNode>(call.Arguments[0]);
        Assert.Equal("notice", Assert.IsType<ReferenceNode>(call.Arguments[1]).Name);
        var strict = Compile(result.CalorSource!, enforceEffects: true);
        Assert.Equal(new[] { "Calor0410", "Calor0410", "Calor0422" },
            strict.Diagnostics.Errors.Select(error => error.Code).OrderBy(code => code, StringComparer.Ordinal));
        Assert.All(strict.Diagnostics.Errors, error =>
            Assert.Contains("'unknown'", error.Message, StringComparison.Ordinal));
        var generated = Compile(result.CalorSource!, enforceEffects: false);
        Assert.False(generated.HasErrors, string.Join("\n", generated.Diagnostics.Errors));
        Assert.Equal("same:hi;second:there;", Run(source));
        Assert.Equal(Run(source), Run(generated.GeneratedCode));
    }

    [Fact]
    public void NativeInitializer_VerbatimReceiverAndMember_RetainTheirIdentities()
    {
        const string source = """
            public class Box { public int @true; }
            public static class Probe
            {
                public static string Run()
                {
                    var @this = new Box { @true = 7 };
                    var other = new Box { @true = 11 };
                    return @this.@true + ":" + other.@true;
                }
            }
            """;
        var result = Convert(source);
        Assert.True(result.Success, result.CalorSource);
        var parsed = AssertShape(result, 23, 2, 0, 2);
        Assert.Equal("7:11", Run(source));
        foreach (var calor in new[] { result.CalorSource!, new CalorEmitter().Emit(parsed) })
        {
            var module = ParseCalor(calor);
            AssertFieldAccesses(module, "verbatim(this).true", "ref(other).true");
            var accesses = AllNodes(module).OfType<FieldAccessNode>().ToArray();
            Assert.All(accesses, field =>
                Assert.Equal("@true", calor.Substring(field.FieldNameSpan.Start, field.FieldNameSpan.Length)));
            Assert.Empty(AllNodes(module).OfType<ThisExpressionNode>());
            var diagnostics = new Calor.Compiler.Diagnostics.DiagnosticBag();
            var bound = new Calor.Compiler.Binding.Binder(diagnostics).Bind(module);
            Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
            var nodes = BoundNodeHelpers.DescendantsAndSelf(bound).ToArray();
            var locals = nodes.OfType<BoundBindStatement>().ToArray();
            Assert.Equal(new[] { "other", "this" },
                locals.Select(local => local.Variable.Name).OrderBy(name => name, StringComparer.Ordinal));
            var fields = nodes.OfType<BoundFieldAccessExpression>().ToArray();
            Assert.Equal(2, fields.Length);
            Assert.All(fields, field =>
            {
                Assert.Equal("true", field.FieldName);
                var receiver = Assert.IsType<BoundVariableExpression>(field.Target);
                Assert.Equal("Box", receiver.Type.DisplayString);
                Assert.Same(Assert.Single(locals, local => local.Variable.Name == receiver.Variable.Name).Variable,
                    receiver.Variable);
            });
            var generated = Compile(calor, enforceEffects: false);
            Assert.False(generated.HasErrors, string.Join("\n", generated.Diagnostics.Errors));
            Assert.Equal(Run(source), Run(generated.GeneratedCode));
        }
    }

    [Theory]
    [InlineData(false, false, "receiver;constructor;argument;take;1:True")]
    [InlineData(true, false, "receiver;constructor;argument;argument-error;0:True")]
    [InlineData(false, true, "receiver;receiver-error;0:False")]
    public void CapturedReceiver_PrecedesPreservedInitializerAndItsExceptions(
        bool argumentThrows, bool receiverThrows, string expected)
    {
        var source = $$"""
            using System;
            using System.Collections.Generic;
            public class Outer
            {
                public int V;
                public readonly List<int> Values = new List<int>();
                public List<int> Items
                {
                    get
                    {
                        Probe.Log += "receiver;";
                        if ({{receiverThrows.ToString().ToLowerInvariant()}})
                            throw new InvalidOperationException("receiver-error");
                        return Values;
                    }
                }
                public Outer() { Probe.Log += "constructor;"; }
            }
            public static class Probe
            {
                public static string Log = "";
                public static int Take(int a, Outer value)
                {
                    Log += "take;";
                    return value.V;
                }
                public static string Run()
                {
                    var o = new Outer();
                    var saved = o;
                    Log = "";
                    Func<int> f = () =>
                    {
                        Log += "argument;";
                        o = null;
                        if ({{argumentThrows.ToString().ToLowerInvariant()}})
                            throw new InvalidOperationException("argument-error");
                        return 1;
                    };
                    try
                    {
                        o.Items.Add(Take(1, new Outer { V = f() }));
                    }
                    catch (InvalidOperationException ex)
                    {
                        Log += ex.Message + ";";
                    }
                    return Log + saved.Values.Count + ":" + (o == null);
                }
            }
            """;
        var result = Convert(source);
        Assert.True(result.Success, result.CalorSource);
        var loss = Assert.Single(result.Context.Losses,
            loss => loss.Feature == "conditional-expression-hoisting");
        Assert.Equal(ConversionLossKind.InteropPreserved, loss.Kind);
        var parsed = AssertShape(result, 51, 2, 0, 6, "conditional-expression-hoisting");
        AssertFieldAccesses(parsed, "ref(Probe).Log", "ref(Probe).Log", "ref(Probe).Log", "ref(Probe).Log", "ref(value).V");
        AssertOriginalPayloads(parsed, source);
        var probe = Assert.Single(parsed.Classes, type => type.Name == "Probe");
        Assert.Equal("Take", Assert.Single(probe.Methods).Name);
        Assert.Equal(InteropMemberKind.Method, Assert.Single(probe.InteropBlocks).MemberKind);
        var generated = Compile(result.CalorSource!, enforceEffects: false);
        Assert.False(generated.HasErrors,
            string.Join("\n", generated.Diagnostics.Errors.Select(error => error.Message)));
        Assert.Equal(expected, Run(source));
        Assert.Equal(expected, Run(generated.GeneratedCode));
    }

    public static TheoryData<string, string, string> InertEventMutations() => new()
    {
        { "literal-user-conversion", """
            public class Value
            {
                public static implicit operator Value(int value) { Probe.N += 1; return new Value(); }
            }
            public class Notice { public Value Value { get; set; } }
            """, "1" },
        { "instance-event-initializer", """
            public class Notice
            {
                public event System.Action Changed = Probe.Make();
                public int Value { get; set; }
            }
            """, "1" },
        { "partial-property-definition", """
            public partial class Notice { public partial int Value { get; set; } }
            public partial class Notice
            {
                private int _value;
                public partial int Value { get => _value; set { Probe.N += 1; _value = value; } }
            }
            """, "1" },
        { "finalizer", """
            public class Notice { public int Value { get; set; } ~Notice() { Probe.N += 1; } }
            """, "1" },
        { "eventargs-symbol-spoof", """
            public class EventArgs { public EventArgs() { Probe.N += 1; } }
            public class Notice : System.EventArgs { public int Value { get; set; } }
            """, "1" },
        { "static-constructor", """
            public class Notice { static Notice() { Probe.N += 1; } public int Value { get; set; } }
            """, "1" },
        { "static-field-initializer", """
            public class Notice { private static int _value = ++Probe.N; public int Value { get; set; } }
            """, "1" },
        { "recursive-initializer", """
            public class Notice { private Notice _child = new Notice(); public int Value { get; set; } }
            """, "1" },
        { "expanding-generic-initializer", """
            public class Chain<T> { private Chain<Chain<T>> _child = new Chain<Chain<T>>(); }
            public class Notice : Chain<int> { public int Value { get; set; } }
            """, "1" },
    };

    [Theory]
    [MemberData(nameof(InertEventMutations))]
    public void EventArgument_RunningUserCode_IsPreserved(string name, string declarations, string value)
    {
        var source = $$"""
            namespace System
            {
            {{declarations}}
            public class Publisher
            {
                public event System.Action<object, Notice> Sent;
                public void Send() { Sent(this, new Notice { Value = {{value}} }); }
            }
            public static class Probe
            {
                public static int N;
                public static System.Action Make() { N += 1; return Empty; }
                private static void Empty() { }
            }
            }
            """;
        var result = Convert(source);
        Assert.True(result.Success, name + ": " + result.CalorSource);
        var shape = name switch
        {
            "literal-user-conversion" => (37, 1, 6, new[] { "initializer-interop" }),
            "instance-event-initializer" => (23, 0, 5, new[] { "initializer-interop", "initializer-interop" }),
            "partial-property-definition" => (23, 0, 5, new[] { "partial-property", "partial-property", "initializer-interop" }),
            "finalizer" => (23, 0, 5, new[] { "destructor", "initializer-interop" }),
            "eventargs-symbol-spoof" => (31, 0, 6, new[] { "initializer-interop" }),
            "static-constructor" => (30, 0, 6, new[] { "initializer-interop" }),
            "static-field-initializer" => (23, 0, 5, new[] { "field", "initializer-interop" }),
            "recursive-initializer" => (25, 1, 6, new[] { "initializer-interop" }),
            "expanding-generic-initializer" => (29, 1, 6, new[] { "initializer-interop" }),
            _ => throw new InvalidOperationException(name)
        };
        var parsed = AssertShape(result, shape.Item1, shape.Item2, 0, shape.Item3, shape.Item4);
        AssertFieldAccesses(parsed, name is "literal-user-conversion" or "eventargs-symbol-spoof" or "static-constructor"
            ? ["ref(Probe).N", "ref(Probe).N"] : []);
        AssertOriginalPayloads(parsed, source);
        var publisher = Assert.Single(parsed.Classes, type => type.Name == "Publisher");
        Assert.Empty(publisher.Methods);
        var send = Assert.Single(publisher.InteropBlocks);
        Assert.Equal(InteropMemberKind.Method, send.MemberKind);
        Assert.Equal("initializer-interop", send.FeatureName);
        Assert.Equal($"Unsupported Method: public void Send() {{ Sent(this, new Notice {{ Value = {value} }}); }}", send.Reason);
    }

    [Fact]
    public void Scorecard092_ConvertsNatively_WithReceiverSnapshot()
    {
        var source = TestDataLoader.LoadTestFile("Level5_Advanced/092_Events.cs");
        var result = Convert(source);
        Assert.True(result.Success, result.CalorSource);
        // The frozen namespaced scorecard is a whole-unit preservation, not the copied fixture above.
        var parsed = AssertShape(result, 2, 0, 0, 0, "namespace-topology");
        AssertOriginalPayloads(parsed, source);
        var block = Assert.Single(parsed.InteropBlocks);
        Assert.True(block.IsCompilationUnitPassthrough);
        Assert.Equal(InteropMemberKind.Other, block.MemberKind);
        Assert.Equal("Multiple lexical namespace scopes are preserved as one whole-file interop boundary.", block.Reason);
        Assert.Equal("Whole C# file preserved because lossless conversion does not split multi-namespace topology.",
            Assert.Single(result.Context.Losses).Description);
        var generated = Compile(result.CalorSource!, enforceEffects: false);
        Assert.False(generated.HasErrors, string.Join("\n", generated.Diagnostics.Errors.Select(error => error.Message)));
        const string caller = """
            public static class Probe
            {
                public static string Run()
                {
                    var publisher = new Events.Publisher();
                    var log = "";
                    publisher.MessageSent += (sender, notice) =>
                        log += object.ReferenceEquals(sender, publisher) + ":" + notice.Message + ";";
                    publisher.Send("hi");
                    publisher.Send("there");
                    return log;
                }
            }
            """;
        Assert.Equal("True:hi;True:there;", Run(source + caller));
        Assert.Equal(Run(source + caller), Run(generated.GeneratedCode + caller));
    }

    // ---- Negative controls -------------------------------------------------------------------

    private const string ReproProbe = """
        using System.Collections.Generic;
        using System.Text;
        public class Holder
        {
            public List<int> Xs = new List<int> { 5 };
            public StringBuilder Sb { get; set; } = new StringBuilder { Capacity = 64 };
            public int After = 2;
        }
        public static class Probe
        {
            public static string Run()
            {
                var h = new Holder();
                return h.Xs.Count + ":" + string.Join(",", h.Xs) + "|" + h.Sb.Capacity + "|" + h.After;
            }
        }
        """;

    /// <summary>
    /// An active-syntax reproduction of the pre-fix semantics (#1541, #1524). It compiles,
    /// so only running it shows the change: the run-and-compare check must report a difference.
    /// </summary>
    [Fact]
    public void NegativeControl_PreFixConversion_RunsDifferently()
    {
        const string preFix = """
            module Repro overflow unchecked
              using System.Collections.Generic
              using System.Text
              class Holder
                field List<int>:Xs = new List<int>(5)
                property StringBuilder:Sb = new StringBuilder()
                  get public
                  set public
                field int:After = 2
              module Probe
                func Run () → string
                  !
                  let h = new Holder()
                  return h.Xs.Count + ":" + string.Join(",", h.Xs) + "|" + h.Sb.Capacity + "|" + h.After
            """;
        var original = Run(ReproProbe);
        Assert.Equal("1:5|64|2", original);
        var generated = Compile(preFix, enforceEffects: false);
        Assert.False(generated.HasErrors, string.Join("\n", generated.Diagnostics.Errors.Select(e => e.Message)));
        Assert.Equal("0:|16|2", Run(generated.GeneratedCode));
        Assert.NotEqual(original, Run(generated.GeneratedCode));
    }

    /// <summary>The same probe through today's converter runs like the original.</summary>
    [Fact]
    public void PositiveControl_CurrentConversion_RunsLikeTheOriginal()
    {
        var result = Convert(ReproProbe);
        Assert.True(result.Success, string.Join("\n", result.Issues.Where(i => i.Severity == ConversionIssueSeverity.Error).Select(i => i.ToString())) + "\n" + result.CalorSource);
        var parsed = AssertShape(result, 49, 3, 0, 4);
        AssertFieldAccesses(parsed, "ref(h).Xs", "ref(h).Xs", "ref(h).Xs.Count",
            "ref(string).Join", "ref(h).Sb", "ref(h).Sb.Capacity", "ref(h).After");
        var generated = Compile(result.CalorSource!, enforceEffects: false);
        Assert.False(generated.HasErrors, string.Join("\n", generated.Diagnostics.Errors.Select(e => e.Message)));
        Assert.Equal(Run(ReproProbe), Run(generated.GeneratedCode));
    }

    /// <summary>The pre-fix #1595 shape (a local collection hoisted into the class body) cannot compile silently.</summary>
    [Fact]
    public void NegativeControl_PreFixClassLevelHoist_FailsLoudly()
    {
        const string preFix = """
            module g overflow unchecked
              using System.Collections.Generic
              class Holder
                field int:Before = 1
                let List<int>:list003 = new List<int>() { 1 }
                field List<List<int>>:X = new List<List<int>>()
                field int:After = 2
              class Other
                field int:Y = 3
            """;
        var result = Compile(preFix, enforceEffects: false, expectParseSuccess: false);
        Assert.True(result.HasErrors);
        var error = Assert.Single(result.Diagnostics.Errors);
        Assert.Equal("Calor0100", error.Code);
        Assert.Contains("'let' is a local binding", error.Message, StringComparison.Ordinal);
    }

    // ---- Helpers -----------------------------------------------------------------------------

    private static ConversionResult Convert(string source) =>
        new CSharpToCalorConverter(new ConversionOptions
        {
            UserManifestRoot = global::Calor.Tests.Shared.HermeticCalorHome.EmptyManifestRoot,
        }).Convert(source);

    private static CompilationResult Compile(string calor, bool enforceEffects, bool expectParseSuccess = true)
    {
        if (expectParseSuccess)
            ParseCalor(calor);
        var resolver = new Effects.EffectResolver(new Effects.Manifests.ManifestLoader(loadUserLevelManifests: false));
        resolver.Initialize(projectDirectory: null, solutionDirectory: null);
        using var context = new CompilationContext { SharedEffectResolver = resolver };
        return Program.Compile(calor, "Initializers.calr", new CompilationOptions
        {
            StatusWriter = TextWriter.Null,
            EnforceEffects = enforceEffects,
            VerificationCacheOptions = new VerificationCacheOptions { Enabled = false },
            Context = context,
        });
    }

    private static ModuleNode ParseCalor(string calor)
    {
        var parsed = CalorSourceHelper.Parse(calor, "initializer-shape.calr");
        Assert.True(parsed.IsSuccess, string.Join("\n", parsed.Errors) + "\n" + calor);
        return parsed.Ast!;
    }

    private static ModuleNode AssertShape(ConversionResult result, int nodes, int creations, int lambdas,
        int members, params string[] features)
    {
        var module = ParseCalor(result.CalorSource!);
        var all = AllNodes(module).ToArray();
        Assert.Equal(nodes, all.Length);
        Assert.Equal(creations, all.OfType<NewExpressionNode>().Count());
        Assert.Equal(lambdas, all.OfType<LambdaExpressionNode>().Count());
        Assert.Equal(members, all.Count(node => node is MethodNode or ClassFieldNode or PropertyNode
            or ConstructorNode or EventDefinitionNode or OperatorOverloadNode));
        Assert.Equal(features.Length, all.OfType<CSharpInteropBlockNode>().Count());
        Assert.Equal(features, result.Context.Losses.Select(loss => loss.Feature));
        Assert.All(result.Context.Losses, loss =>
        {
            Assert.Equal(ConversionLossKind.InteropPreserved, loss.Kind);
            Assert.Equal(loss.Feature == "post-validation-fallback" ? "rescue" : "interop", loss.Path);
            Assert.Equal(loss.Feature == "post-validation-fallback" ? "parse-failure" : null, loss.Trigger);
        });
        Assert.DoesNotContain(all, node => node is RawCSharpNode or RawCSharpExpressionNode or FallbackExpressionNode);
        return module;
    }

    private static void AssertOriginalPayloads(ModuleNode module, string source)
    {
        var originalMembers = CSharpSyntaxTree.ParseText(source).GetRoot()
            .DescendantNodes().OfType<MemberDeclarationSyntax>().ToArray();
        foreach (var block in AllNodes(module).OfType<CSharpInteropBlockNode>())
        {
            var expected = source;
            if (!block.IsCompilationUnitPassthrough)
            {
                var original = Assert.Single(originalMembers, member =>
                    (block.FeatureName == "post-validation-fallback" ? member.ToString() : member.ToFullString())
                        == block.CSharpCode);
                expected = block.FeatureName == "post-validation-fallback" ? original.ToString() : original.ToFullString();
                if (block.FeatureName != "post-validation-fallback")
                {
                    var text = original.ToString();
                    Assert.Equal($"Unsupported {block.MemberKind}: {(text.Length > 80 ? text[..77] + "..." : text)}",
                        block.Reason);
                }
            }
            Assert.Equal(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(block.CSharpCode));
        }
    }

    private static void AssertFieldAccesses(ModuleNode module, params string[] expected)
    {
        static string Identity(ExpressionNode expression) => expression switch
        {
            ReferenceNode reference => $"{(reference.IsVerbatim ? "verbatim" : "ref")}({reference.Name})",
            FieldAccessNode field => $"{Identity(field.Target)}.{field.FieldName}",
            ArrayAccessNode array => $"index({Identity(array.Array)},{Identity(array.Index)})",
            IntLiteralNode literal => literal.Value.ToString(CultureInfo.InvariantCulture),
            NewExpressionNode creation => $"new({creation.TypeName})",
            ExpressionCallNode call => $"call({Identity(call.TargetExpression)})",
            _ => throw new InvalidOperationException($"Unexpected receiver: {expression.GetType().Name}")
        };

        Assert.Equal(expected.OrderBy(identity => identity, StringComparer.Ordinal),
            AllNodes(module).OfType<FieldAccessNode>().Select(Identity)
                .OrderBy(identity => identity, StringComparer.Ordinal));
    }

    private static IEnumerable<AstNode> AllNodes(AstNode root)
    {
        var seen = new HashSet<AstNode>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<AstNode>();
        pending.Push(root);
        while (pending.TryPop(out var node))
        {
            if (!seen.Add(node))
                continue;
            yield return node;
            foreach (var child in RecursiveAstWalker.GetAllChildren(node))
                pending.Push(child);
        }
    }

    private static string Run(string csharp)
    {
        var compilation = CSharpCompilation.Create("Init_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(csharp, new CSharpParseOptions(LanguageVersion.Preview))],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        using var stream = new MemoryStream();
        var emission = compilation.Emit(stream);
        Assert.True(emission.Success, string.Join("\n", emission.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)) + "\n" + csharp);
        stream.Position = 0;
        var context = new AssemblyLoadContext(compilation.AssemblyName!, isCollectible: true);
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var run = context.LoadFromStream(stream).GetTypes().Single(t => t.Name == "Probe")
                .GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;
            return run.Invoke(null, null)?.ToString() ?? "<null>";
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            context.Unload();
        }
    }
}
