using Calor.Compiler.CodeGen;
using Calor.Compiler.Migration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// 0.30: the converter and CalorEmitter must never print retired `§` syntax. Before this, a few
/// shapes (compound assignment the converter could not lower, event subscription, stackalloc and
/// pointers, inline raw C# expressions, deep ternaries, hoisted temps) printed legacy text; the
/// converter's parse check then fell back to preserving the whole type or file as C#. Each shape
/// now converts natively or keeps only its own statement or member as C#, with a named reason.
/// The parser half covers three native forms that did not parse.
/// </summary>
public class NativeEmissionRegressionTests
{
    /// <summary>The emitter's text before any parse-check fallback: visitor plus CalorEmitter.</summary>
    private static string EmitBeforeFallback(string csharp)
    {
        var tree = CSharpSyntaxTree.ParseText(csharp, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create("Probe", [tree], GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        var visitor = new RoslynSyntaxVisitor(new ConversionContext(), compilation.GetSemanticModel(tree));
        var module = visitor.Convert((CompilationUnitSyntax)tree.GetRoot(), "Probe");
        return new CalorEmitter().Emit(module);
    }

    private static string Convert(string csharp)
        => new CSharpToCalorConverter().Convert(csharp, "probe.cs").CalorSource!.Replace("\r\n", "\n");

    private static CompilationResult Compile(string calor)
        => Program.Compile(calor, "probe.calr", new CompilationOptions { StatusWriter = TextWriter.Null });

    private static string Errors(CompilationResult result)
        => string.Join("\n", result.Diagnostics.Errors.Select(d => $"{d.Code}: {d.Message}"));

    public static TheoryData<string, string> LegacyShapes => new()
    {
        { "event-subscription", "using System; public class B { public event EventHandler? Click; public void Wire(EventHandler h) { Click += h; Click -= h; } }" },
        { "delegate-field-compound", "using System; public class B { private EventHandler? _click; public event EventHandler Click { add { _click += value; } remove { _click -= value; } } }" },
        { "property-compound", "public class A { public double Balance { get; private set; } public void Add(double x) { Balance += x; } }" },
        { "decimal-compound", "public class A { private decimal _total; public void Add(decimal x) { _total += x; } }" },
        { "stackalloc-pointers", "public static class U { public static unsafe int F() { int x = 1; int* p = &x; *p = 2; System.Span<int> s = stackalloc int[4]; return *p + s.Length; } }" },
        { "interpolation-hole", "public static class F { public static string G(int[] a) { return $\"v={a[0]}\"; } }" },
        { "deep-ternary", "public static class T { public static int G(int s) { return s >= 90 ? 4 : s >= 80 ? 3 : s >= 70 ? 2 : 1; } }" },
        { "linq-query", "using System.Linq; using System.Collections.Generic; public static class L { public static List<int> F(List<int> xs) { var q = from x in xs where x > 2 select x * 2; return q.ToList(); } }" },
    };

    [Theory]
    [MemberData(nameof(LegacyShapes))]
    public void Emitter_NeverPrintsRetiredSectionMarkers(string shape, string csharp)
    {
        var calor = EmitBeforeFallback(csharp);
        Assert.False(calor.Contains('§'), $"{shape}:\n{calor}");
    }

    [Fact]
    public void EventSubscription_KeepsItsOwnCodeAsCSharpWithANamedReason()
    {
        var result = new CSharpToCalorConverter().Convert(
            "using System; namespace N { public class B { public event EventHandler? Click; public void Wire(EventHandler h) { Click += h; } public int Two() { return 2; } } }", "probe.cs");
        var calor = result.CalorSource!.Replace("\r\n", "\n");
        Assert.Contains(result.Issues, issue => issue.Feature == "event-subscription");
        Assert.Contains("func Two () → int", calor);
        Assert.DoesNotContain("raw unit:", calor);
    }

    [Fact]
    public void PropertyCompoundAssignment_LowersToANativeSet()
    {
        var calor = EmitBeforeFallback("public class A { public double Balance { get; private set; } public void Add(double x) { Balance += x; } }");
        Assert.Contains("set Balance (Balance + x)", calor);
    }

    [Fact]
    public void DeepTernary_StaysOneNativeExpression()
    {
        var calor = Convert("public static class T { public static int G(int s) { return s >= 90 ? 4 : s >= 80 ? 3 : s >= 70 ? 2 : 1; } }");
        Assert.DoesNotContain("raw ", calor);
        Assert.DoesNotContain("_tern", calor);
        Assert.Contains("? 2 : 1", calor);
    }

    [Fact]
    public void NonPrimitiveDefault_KeepsItsMemberAsCSharpWithANamedReason()
    {
        // Native source has no bare `default` for a non-primitive target (types.mdx). Before,
        // the converter printed `@default`, which does not parse, and the whole file fell back
        // to C#; now only the member that needs it stays C#.
        var result = new CSharpToCalorConverter().Convert(
            "using System.Threading; namespace N { public static class S { public static int Send(int request, CancellationToken token = default) { return request; } public static T Pick<T>() => default; public static int Two() { return 2; } } }", "probe.cs");
        var calor = result.CalorSource!.Replace("\r\n", "\n");
        Assert.Contains(result.Issues, issue => issue.Feature == "default-expression");
        Assert.DoesNotContain("@default", calor);
        Assert.DoesNotContain("raw unit:", calor);
        Assert.Contains("func Two () → int", calor);
    }

    [Fact]
    public void PrimitiveAndReferenceDefaults_StayNative()
    {
        var calor = Convert("public static class S { public static int N(int x = default) => x; public static string? R(string? s = default) => s; }");
        Assert.DoesNotContain("raw ", calor);
        Assert.DoesNotContain("default", calor);
    }

    [Fact]
    public void StackallocAndPointers_KeepTheMemberAsCSharpWithANamedReason()
    {
        var result = new CSharpToCalorConverter().Convert(
            "public static class U { public static unsafe int F() { System.Span<int> s = stackalloc int[4]; return s.Length; } public static int Two() { return 2; } }", "probe.cs");
        Assert.Contains(result.Issues, issue => issue.Feature == "stackalloc");
        Assert.Contains("func Two () → int", result.CalorSource!.Replace("\r\n", "\n"));
    }

    [Fact]
    public void Invariant_EmitsTheNativeKeyword()
    {
        var module = new Calor.Compiler.Parsing.Parser(
            new Calor.Compiler.Parsing.Lexer("module M M block\n  invariant (1 > 0)\n  func F () → int\n    !\n    return 1\n",
                new Calor.Compiler.Diagnostics.DiagnosticBag()).TokenizeAllForParser(),
            new Calor.Compiler.Diagnostics.DiagnosticBag()).Parse();
        var calor = new CalorEmitter().Emit(module);
        Assert.Contains("invariant (1 > 0)", calor);
        Assert.False(calor.Contains('§'), calor);
    }

    [Fact]
    public void TypedUsing_WithAGenericType_Parses()
    {
        var result = Compile("""
            module M M block
              class T
                func Run () → bool
                  ! unknown
                  using IEnumerator<int>:e = (new List<int>()).GetEnumerator()
                    return e.MoveNext()
                  return false
            """);
        Assert.False(result.HasErrors, Errors(result));
        Assert.Contains("using (IEnumerator<int> e =", result.GeneratedCode);
    }

    [Fact]
    public void TypedEach_WithAGenericType_Parses()
    {
        var result = Compile("""
            module M M block
              class T
                func Run (List<List<int>>:xs) → int
                  !
                  var int:n = 0
                  each List<int>:row in xs
                    set n n + row.Count
                  return n
            """);
        Assert.False(result.HasErrors, Errors(result));
        Assert.Contains("foreach (List<int> row in xs)", result.GeneratedCode);
    }

    [Fact]
    public void GlobalUsing_InsideAPreprocessorBlock_Parses()
    {
        var result = Compile("""
            module ConditionalUsing ConditionalUsing block
              pp FEATURE
                global using System.Text
                using Buffers = System.Buffers
              class T
                func Run () → int
                  !
                  return 0
            """);
        Assert.False(result.HasErrors, Errors(result));
        Assert.Contains("global using System.Text;", result.GeneratedCode);
        Assert.Contains("using Buffers = System.Buffers;", result.GeneratedCode);
    }

    [Fact]
    public void EmptyInterface_DoesNotSwallowTheNextDeclaration()
    {
        var result = Compile("""
            module Reach Reach block
              interface INote
              func Accept (INote:n) → int
                !
                return 1
            """);
        Assert.False(result.HasErrors, Errors(result));
        Assert.Contains("public static int Accept(INote n)", result.GeneratedCode);
    }
}
