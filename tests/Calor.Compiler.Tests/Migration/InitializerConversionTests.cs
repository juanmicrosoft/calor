using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Migration;
using Calor.Compiler.Verification.Z3.Cache;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// 0.27 (#1595, #1541, #1524): collection and object initializers convert faithfully or are kept
/// as C# with a reported loss, and a class-level collection block is a hard error instead of
/// silently ending the class. The cross-surface convert, compile, run and compare checks are the
/// T1-INIT-* controls in <c>tests/TestData/InteropScope/T1/controls.json</c>
/// (<see cref="InteropScope.SharedInteropControlsTests"/>); this class holds the parser rule, the
/// per-shape conversion decisions, and the negative controls that prove the run-and-compare
/// check catches the old miscompilations.
/// </summary>
public class InitializerConversionTests
{
    // ---- #1595: parser -----------------------------------------------------------------------

    [Theory]
    [InlineData("§LIST{xs:i32}\n        1", "§LIST")]
    [InlineData("§DICT{d:str:i32}\n        §KV \"a\" 1", "§DICT")]
    [InlineData("§HSET{s:i32}\n        1", "§HSET")]
    public void ClassLevelCollectionBlock_IsAHardError_AndLaterMembersSurvive(string block, string tag)
    {
        var source = $$"""
            §M{m001:Repro}
              §CL{c001:Holder:pub}
                §FLD{i32:Before:pub} = 1
                {{block.Replace("\n        ", "\n      ", StringComparison.Ordinal)}}
                §FLD{i32:After:pub} = 2
                §MT{m002:Get:pub} () -> i32
                  §R After
              §CL{c003:Other:pub}
                §FLD{i32:Y:pub} = 3
            """;
        var diagnostics = new Calor.Compiler.Diagnostics.DiagnosticBag();
        var tokens = new Calor.Compiler.Parsing.Lexer(source, diagnostics).TokenizeAllForParser();
        var module = new Calor.Compiler.Parsing.Parser(tokens, diagnostics).Parse();
        var error = Assert.Single(diagnostics.Errors);
        Assert.Equal("Calor0100", error.Code);
        Assert.Contains(tag + " is a statement", error.Message, StringComparison.Ordinal);
        // The parser keeps reading the class after the block: every later member and type is in the AST.
        var classes = module.Classes;
        var holder = Assert.Single(classes, c => c.Name == "Holder");
        Assert.Contains(holder.Fields, f => f.Name == "After");
        Assert.Contains(holder.Methods, m => m.Name == "Get");
        Assert.Contains(classes, c => c.Name == "Other");
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
        Assert.DoesNotContain("§CS{", calor, StringComparison.Ordinal);
        Assert.Contains("§LIST{list003:i32} 1 §/LIST{list003}", calor, StringComparison.Ordinal);
        Assert.Contains("= §NEW{StringBuilder} Capacity = 64 §/NEW", calor, StringComparison.Ordinal);
        Assert.DoesNotContain("§A 5", calor, StringComparison.Ordinal);
        var csharp = Compile(calor, enforceEffects: false).GeneratedCode;
        Assert.Contains("new List<int>() { 5 }", csharp, StringComparison.Ordinal);
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
    [InlineData("public int M() { var o = new Outer(); System.Func<int> f = () => { o = null; return 1; }; o.Items.Add(Take(1, new Outer { V = f() })); return 0; }", "initializer-position")]
    [InlineData("public int M() { var o = new Outer { Value = (System.Func<object>)(() => { return default(int); }) }; return 0; }", "object-initializer-value")]
    public void ShapeWithoutAFaithfulNativeForm_IsPreservedWithALoss(string member, string reason)
    {
        var result = Convert(ShapeSource(member));
        Assert.True(result.Success, string.Join("\n", result.Issues.Where(i => i.Severity == ConversionIssueSeverity.Error).Select(i => i.ToString())) + "\n" + result.CalorSource);
        Assert.Contains(result.Context.Losses, loss => loss.Kind == ConversionLossKind.InteropPreserved
            && (loss.Feature == "initializer-interop" && loss.Description.Contains(reason, StringComparison.Ordinal)
                || loss.Feature == reason));
        Assert.Contains("§CS{", result.CalorSource!, StringComparison.Ordinal);
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
        Assert.DoesNotContain(result.Context.Losses, loss => loss.Feature is "initializer-interop" or "dictionary-initializer");
        Assert.DoesNotContain("§CS{", result.CalorSource!, StringComparison.Ordinal);
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
        // Conversion scorecard sample 092's shape: an initializer passed with `this` to a delegate
        // field. The delegate is read before the argument in C#, so the creation stays C#.
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
        var generated = Compile(result.CalorSource!, enforceEffects: false);
        Assert.False(generated.HasErrors, name + ": " + string.Join("\n", generated.Diagnostics.Errors.Select(e => e.Message)));
        Assert.Equal(Run(source), Run(generated.GeneratedCode));
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
    /// The Calor the converter produced for the repro before the fix (#1541, #1524). It compiles,
    /// so only running it shows the change: the run-and-compare check must report a difference.
    /// </summary>
    [Fact]
    public void NegativeControl_PreFixConversion_RunsDifferently()
    {
        const string preFix = """
            §M{m001:Repro:overflow=unchecked}
              §U{System.Collections.Generic}
              §U{System.Text}
              §NS{ns_global:_global:global}
                §CL{c001:Holder:pub}
                  §FLD{List<i32>:Xs:pub} = §NEW{List<i32>} §A 5 §/NEW
                  §PROP{p002:Sb:StringBuilder:pub:get,set} = (sb-new)
                  §FLD{i32:After:pub} = 2
                §CL{c003:Probe:pub:stat}
                  §MT{m004:Run:pub:stat} () -> str
                    §B{h} §NEW{Holder} §/NEW
                    §R (+ (+ (+ (+ (+ (+ h.Xs.Count ":") (join "," h.Xs)) "|") h.Sb.Capacity) "|") h.After)
            """;
        var original = Run(ReproProbe);
        Assert.Equal("1:5|64|2", original);
        var generated = Compile(preFix, enforceEffects: false);
        Assert.False(generated.HasErrors, string.Join("\n", generated.Diagnostics.Errors.Select(e => e.Message)));
        Assert.NotEqual(original, Run(generated.GeneratedCode));
    }

    /// <summary>The same probe through today's converter runs like the original.</summary>
    [Fact]
    public void PositiveControl_CurrentConversion_RunsLikeTheOriginal()
    {
        var result = Convert(ReproProbe);
        Assert.True(result.Success, string.Join("\n", result.Issues.Where(i => i.Severity == ConversionIssueSeverity.Error).Select(i => i.ToString())) + "\n" + result.CalorSource);
        var generated = Compile(result.CalorSource!, enforceEffects: false);
        Assert.False(generated.HasErrors, string.Join("\n", generated.Diagnostics.Errors.Select(e => e.Message)));
        Assert.Equal(Run(ReproProbe), Run(generated.GeneratedCode));
    }

    /// <summary>The pre-fix #1595 shape (a §LIST hoisted into the class body) no longer compiles silently.</summary>
    [Fact]
    public void NegativeControl_PreFixClassLevelHoist_FailsLoudly()
    {
        const string preFix = """
            §M{m001:g:overflow=unchecked}
              §U{System.Collections.Generic}
              §NS{ns_global:_global:global}
                §CL{c001:Holder:pub}
                  §FLD{i32:Before:pub} = 1
                  §LIST{list003:i32}
                    1
                  §FLD{List<List<i32>>:X:pub} = §NEW{List<List<i32>>} §A  §/NEW
                  §FLD{i32:After:pub} = 2
                §CL{c005:Other:pub}
                  §FLD{i32:Y:pub} = 3
            """;
        var result = Compile(preFix, enforceEffects: false);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Errors, e => e.Code == "Calor0100" && e.Message.Contains("§LIST", StringComparison.Ordinal));
    }

    // ---- Helpers -----------------------------------------------------------------------------

    private static ConversionResult Convert(string source) =>
        new CSharpToCalorConverter(new ConversionOptions
        {
            UserManifestRoot = global::Calor.Tests.Shared.HermeticCalorHome.EmptyManifestRoot,
        }).Convert(source);

    private static CompilationResult Compile(string calor, bool enforceEffects)
    {
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
