using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// One trigger per error-capable binder site the 0.30 review admitted to
/// <c>BinderErrorEmissionCatalog.golden.json</c> (pin 30 → 44). Each case names its
/// catalog site, the code the site reports, and whether the policy routes that code
/// to a compilation error (asserted through <see cref="Program.Compile"/>) or keeps
/// it analysis-only (asserted on the binder's own bag, plus the downstream failure
/// that keeps the program from compiling silently). The per-site review notes sit
/// beside the pinned count in <c>BinderErrorEmissionCatalogTests</c>.
/// </summary>
public sealed class BinderErrorEmissionNewSiteTests
{
    // --- compilation-error sites -------------------------------------------------

    [Theory]
    // Binder.BindNewExpression#03 (Calor0208): a collection initializer whose entry
    // has no applicable instance Add.
    [InlineData("BindNewExpression#03", DiagnosticCode.NoMatchingOverload, "cannot resolve an instance Add overload", """
        module P
          using System.Collections.Generic
          module M
            func Run () → List<int>
              ! alloc, mut
              return new List<int>() { "x" }
        """)]
    // Binder.CheckMetadataArrayToCollection#01 (Calor0254).
    [InlineData("CheckMetadataArrayToCollection#01", DiagnosticCode.BindArrayToConcreteCollection, "returns an array", """
        module P
          using System.Collections.Generic
          module M
            func Run (string:s) → int
              ! alloc
              let List<string>:xs = s.Split(',')
              return xs.Count
        """)]
    // Binder.Declare#01 (Calor0201): a dictionary deconstruction that names one local twice.
    [InlineData("Declare#01", DiagnosticCode.DuplicateDefinition, "Variable 'k' is already defined", """
        module P
          using System.Collections.Generic
          module M
            func Run (Dictionary<string, int>:d) → int
              !
              var int:t = 0
              each k, k in d
                set t (t + 1)
              return t
        """)]
    // Binder.RegisterClassTree#05 (Calor0201): two events with one name.
    [InlineData("RegisterClassTree#05", DiagnosticCode.DuplicateDefinition, "Event 'Clicked' is already defined in 'Button'", """
        module P
          using System
          class Button
            event Action<int>:Clicked
            event Action<int>:Clicked
        """)]
    // Binder.ResolveCall#01 (Calor0208): an interface receiver that has no such method.
    [InlineData("ResolveCall#01", DiagnosticCode.NoMatchingOverload, "Interface receiver has no method 'Perimeter'", """
        module P
          interface IShape
            func Area () → int
              !
          module M
            func Run (IShape:s) → int
              !
              return s.Perimeter()
        """)]
    // Binder.ResolveSourceEnumExtension#01 (Calor0208) and #02 (Calor0207).
    [InlineData("ResolveSourceEnumExtension#01", DiagnosticCode.NoMatchingOverload, "No source extension overload 'Code' matches", """
        module P
          enum Color
            Red
          module ColorExt
            func Code (this Color:c) → int
              !
              return ((int)c)
          module M
            func Run (Color:c) → int
              !
              return c.Code("x")
        """)]
    [InlineData("ResolveSourceEnumExtension#02", DiagnosticCode.AmbiguousOverload, "Source extension call 'Code' is ambiguous", """
        module P
          enum Color
            Red
          module ColorExt
            func Code (this Color:c) → int
              !
              return ((int)c)
          module ColorExt2
            func Code (this Color:c) → int
              !
              return 1
          module M
            func Run (Color:c) → int
              !
              return c.Code()
        """)]
    // Binder.ValidateCallableEffectRows#01 (Calor0407, decision D2).
    [InlineData("ValidateCallableEffectRows#01", DiagnosticCode.MissingCallableEffectRow, "requires an explicit effect row", """
        module P
          module M
            func Apply (System.Func<int, int>:f, int:x) → int
              ! unknown
              return f(x)
        """)]
    public void CompilationErrorSite_RejectsTheProgram(string site, string code, string message, string source)
    {
        var result = Program.Compile(source, "site.calr");
        Assert.True(result.Diagnostics.Errors.Any(d => d.Code == code
                && d.Message.Contains(message, StringComparison.Ordinal)),
            $"{site}: expected error {code} '{message}', got:\n{string.Join("\n", result.Diagnostics)}");
        Assert.True(BindingDiagnosticPolicy.Catalog[code].Disposition == BindingDiagnosticDisposition.CompilationError);
    }

    // --- analysis-only sites ------------------------------------------------------

    [Theory]
    // Binder.BindFieldAccess#01 (Calor0200): a missing source enum member.
    [InlineData("BindFieldAccess#01", DiagnosticCode.UndefinedReference, "Enum member 'Color.Purple' is missing", """
        module P
          enum Color
            Red
          module M
            func Run () → Color
              !
              return Color.Purple
        """)]
    // Binder.BindNewExpression#02 (Calor0202): a collection initializer on a source
    // class that does not implement IEnumerable.
    [InlineData("BindNewExpression#02", DiagnosticCode.TypeMismatch, "requires a resolved System.Collections.IEnumerable", """
        module P
          class Bag
            func Add (int:x)
              !
          module M
            func Run () → Bag
              ! unknown
              return new Bag() { 1, 2 }
        """)]
    // Binder.BindNewExpression#04 (Calor0202): the same on a metadata type.
    [InlineData("BindNewExpression#04", DiagnosticCode.TypeMismatch, "must implement System.Collections.IEnumerable", """
        module P
          module M
            func Run () → System.Text.StringBuilder
              ! alloc, mut
              return new System.Text.StringBuilder() { "a" }
        """)]
    public void AnalysisOnlySite_ReportsInTheBinderAndTheProgramStillFails(
        string site, string code, string message, string source)
    {
        var diagnostics = new DiagnosticBag();
        var parse = new DiagnosticBag();
        var module = new Parser(new Lexer(source, parse).TokenizeAllForParser(), parse).Parse();
        Assert.False(parse.HasErrors, string.Join("; ", parse));
        new Binder(diagnostics, "site.calr").Bind(module);
        Assert.True(diagnostics.Errors.Any(d => d.Code == code
                && d.Message.Contains(message, StringComparison.Ordinal)),
            $"{site}: expected binder error {code} '{message}', got:\n{string.Join("\n", diagnostics)}");
        Assert.True(BindingDiagnosticPolicy.Catalog[code].Disposition == BindingDiagnosticDisposition.AnalysisOnly);

        // Analysis-only is not silent: the program still does not compile.
        Assert.True(Program.Compile(source, "site.calr").HasErrors, $"{site}: compiled silently");
    }

    // Binder.BindFieldAccess#02, Binder.BindReferenceExpression#03 and
    // Binder.ResolveCallReceiver#01 (all Calor0200, analysis-only): a derived class
    // reading or invoking its base class's field-like event as delegate storage (C#
    // CS0070). Each site is identified by the span it reports.
    [Fact]
    public void EventStorageReadFromDerivedClass_ReportsAtEachSiteAndStillFails()
    {
        const string source = """
            module P
              using System
              class Button
                event Action<int>:Clicked
              class Fancy
                extends Button
                func Member () → Action<int> ! unknown
                  ! unknown
                  return this.Clicked
                func Bare () → Action<int> ! unknown
                  ! unknown
                  return Clicked
                func Fire (int:x)
                  ! unknown
                  Clicked.Invoke(x)
            """;
        var diagnostics = new DiagnosticBag();
        var parse = new DiagnosticBag();
        var module = new Parser(new Lexer(source, parse).TokenizeAllForParser(), parse).Parse();
        Assert.False(parse.HasErrors, string.Join("; ", parse));
        new Binder(diagnostics, "site.calr").Bind(module);

        var reported = diagnostics.Errors
            .Where(d => d.Code == DiagnosticCode.UndefinedReference
                && d.Message == "Event 'Clicked' is not readable as delegate storage in this context")
            .Select(d => (d.Span.Line, d.Span.Column))
            .OrderBy(position => position)
            .ToArray();
        Assert.Equal(new[]
        {
            (9, 19),  // BindFieldAccess#02: the member name in `this.Clicked`
            (12, 14), // BindReferenceExpression#03: the bare reference
            (15, 7),  // BindReferenceExpression#03: the receiver of `.Invoke`
        }, reported);
        Assert.True(Program.Compile(source, "site.calr").HasErrors, "compiled silently");

        // ResolveCallReceiver#01 is the call-target fallback the binder takes only when the
        // C# projection that selects event storage is not built. The one production binder
        // built that way is CrossModuleTypeRegistry's declaration binder, whose bag is
        // discarded. There the fallback cannot tell the declaring type from any other, so
        // it also reports a valid in-class invocation; with the projection (every
        // user-facing compile) the same program binds cleanly.
        const string invoked = """
            module P
              using System
              class Button
                event Action<int>:Clicked
                func Fire (int:x)
                  ! unknown
                  Clicked(x)
            """;
        DiagnosticBag BindInvoked(bool projection)
        {
            var bag = new DiagnosticBag();
            var invokedParse = new DiagnosticBag();
            var invokedModule = new Parser(new Lexer(invoked, invokedParse).TokenizeAllForParser(), invokedParse).Parse();
            Assert.False(invokedParse.HasErrors, string.Join("; ", invokedParse));
            new Binder(bag, "site.calr") { ResolveSourceCallConversions = projection }.Bind(invokedModule);
            return bag;
        }
        Assert.Contains(BindInvoked(projection: false).Errors, d => d.Code == DiagnosticCode.UndefinedReference
            && d.Message == "Event 'Clicked' is not readable as delegate storage in this context"
            && d.Span.Line == 7 && d.Span.Column == 7);
        Assert.Empty(BindInvoked(projection: true).Errors);
        Assert.False(Program.Compile(invoked, "site.calr").HasErrors);
    }

    // A review finding on Binder.ResolveCall#01: System.Object's members are callable on
    // every interface-typed value, as in C#. They bind as opaque (charged Unknown), never
    // as Calor0208 "Interface receiver has no method".
    [Fact]
    public void InterfaceReceiver_ObjectMembers_BindAsOpaqueNotNoMatch()
    {
        var result = Program.Compile("""
            module P
              interface IShape
                func Area () → int
                  !
              module M
                func Text (IShape:s) → string
                  ! unknown
                  return s.ToString()
                func Same (IShape:s, IShape:t) → bool
                  ! unknown
                  return s.Equals(t)
                func Hash (IShape:s) → int
                  ! unknown
                  return s.GetHashCode()
            """, "site.calr");
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));

        var pure = Program.Compile("""
            module P
              interface IShape
                func Area () → int
                  !
              module M
                func Text (IShape:s) → string
                  !
                  return s.ToString()
            """, "site.calr");
        Assert.DoesNotContain(pure.Diagnostics, d => d.Code == DiagnosticCode.NoMatchingOverload);
        Assert.Contains(pure.Diagnostics.Errors, d => d.Code == DiagnosticCode.ForbiddenEffect
            && d.Message.Contains("'unknown'", StringComparison.Ordinal));
    }
}
