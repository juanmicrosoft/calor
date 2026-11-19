using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

// Flat test namespace: see BinderDispatchCompletenessTests.
namespace Calor.Compiler.Tests;

/// <summary>
/// 0.30 (option A): source overload resolution with C#'s implicit nullable conversions,
/// constant conversions of `const` fields and literals, and the better-function-member rule
/// extended to nullable types. Each row declares an overload set and one call, in Calor and in
/// the equivalent C#. Roslyn's selection on that C# (and on the C# Calor generates) is the
/// reference. Calor must select the same overload, or decline: leave the call opaque (its
/// effects stay unknown) or reject it. It must never select a different overload.
///
/// Every row runs twice: with the source projection (Roslyn compiles the module and the binder
/// cross-checks its own choice), and without it, so the binder's own rules are held to the
/// same standard. <c>Expected</c> pins the binder's precision without the projection.
/// </summary>
public class SourceOverloadDifferentialTests
{
    public enum CalorOutcome { Selects, Declines }

    // Parameters of Pick, available to every call.
    private const string PickParameters =
        "int:i, long:l, byte:b, short:s, uint:ui, char:c, float:f, double:d, decimal:m, " +
        "int?:ni, long?:nl, bool:z, string:str, object:o";
    private const string PickParametersCSharp =
        "int i, long l, byte b, short s, uint ui, char c, float f, double d, decimal m, " +
        "int? ni, long? nl, bool z, string str, object o";

    public static TheoryData<string, string[], string, CalorOutcome> Rows() => new()
    {
        // Constant conversions of a const field and of literals.
        { "const-byte-vs-long", ["byte", "long"], "C", CalorOutcome.Selects },
        { "qualified-const-byte-vs-long", ["byte", "long"], "L.C", CalorOutcome.Selects },
        { "literal-byte-vs-long", ["byte", "long"], "1", CalorOutcome.Selects },
        { "literal-too-big-for-byte", ["byte", "long"], "300", CalorOutcome.Selects },
        { "negative-const-not-byte", ["byte", "short"], "Neg", CalorOutcome.Selects },
        { "const-only-byte", ["byte"], "C", CalorOutcome.Selects },
        { "variable-not-byte", ["byte"], "i", CalorOutcome.Declines },
        { "literal-sbyte-vs-byte", ["sbyte", "byte"], "1", CalorOutcome.Selects },
        { "literal-short-vs-ushort", ["short", "ushort"], "1", CalorOutcome.Selects },
        { "literal-uint-vs-long", ["uint", "long"], "1", CalorOutcome.Selects },
        { "literal-long-vs-ulong", ["long", "ulong"], "1", CalorOutcome.Selects },
        { "literal-int-vs-uint", ["uint", "int"], "1", CalorOutcome.Selects },
        { "long-const-ulong-vs-long", ["ulong", "long"], "CL", CalorOutcome.Selects },
        { "long-const-only-ulong", ["ulong"], "CL", CalorOutcome.Selects },
        // Constants this binder does not evaluate: decline when a constant conversion could apply.
        { "folded-const-byte-vs-long", ["byte", "long"], "(C + 1)", CalorOutcome.Declines },
        { "const-of-const-byte-vs-long", ["byte", "long"], "Alias", CalorOutcome.Declines },
        // A Calor literal above int.MaxValue is long (emitted `3000000000L`); `U` makes it uint.
        { "long-literal", ["uint", "long"], "3000000000", CalorOutcome.Selects },
        { "uint-literal", ["uint", "long"], "3000000000U", CalorOutcome.Selects },
        // Implicit nullable conversions.
        { "int-to-int?", ["int?"], "i", CalorOutcome.Selects },
        { "int-to-long?", ["long?"], "i", CalorOutcome.Selects },
        { "int?-vs-long?", ["int?", "long?"], "i", CalorOutcome.Selects },
        { "byte-vs-int?-literal", ["byte", "int?"], "1", CalorOutcome.Selects },
        { "byte?-vs-long?-const", ["byte?", "long?"], "C", CalorOutcome.Selects },
        { "byte?-vs-int?-literal", ["byte?", "int?"], "1", CalorOutcome.Selects },
        { "byte?-vs-long?-negative", ["byte?", "long?"], "Neg", CalorOutcome.Selects },
        { "int?-vs-uint?-const", ["int?", "uint?"], "C", CalorOutcome.Selects },
        { "long?-vs-ulong?-long-const", ["long?", "ulong?"], "CL", CalorOutcome.Selects },
        { "lifted-int?-to-long?", ["long?", "double?"], "ni", CalorOutcome.Selects },
        { "lifted-int?-only-long?", ["long?"], "ni", CalorOutcome.Selects },
        { "nullable-not-to-value", ["long"], "ni", CalorOutcome.Declines },
        { "int?-vs-object", ["object", "int?"], "i", CalorOutcome.Selects },
        { "int?-vs-string", ["string", "int?"], "i", CalorOutcome.Selects },
        { "bool?-vs-object", ["object", "bool?"], "z", CalorOutcome.Selects },
        { "long?-vs-long", ["long?", "long"], "i", CalorOutcome.Selects },
        // The null literal matches no type exactly; the better target decides.
        { "null-string-vs-object", ["string", "object"], "null", CalorOutcome.Selects },
        { "null-int?-vs-long?", ["int?", "long?"], "null", CalorOutcome.Selects },
        { "null-string-vs-int?-ambiguous", ["string", "int?"], "null", CalorOutcome.Declines },
        // C# ambiguity: Calor must not select.
        { "int?-vs-long-ambiguous", ["int?", "long"], "i", CalorOutcome.Declines },
        { "int?-vs-double-ambiguous", ["int?", "double"], "i", CalorOutcome.Declines },
        { "decimal-vs-double-ambiguous", ["decimal", "double"], "i", CalorOutcome.Declines },
        // Ordinary numeric betterness and char.
        { "long-vs-double", ["long", "double"], "i", CalorOutcome.Selects },
        { "int-vs-long-from-byte", ["int", "long"], "b", CalorOutcome.Selects },
        { "float-vs-double", ["float", "double"], "i", CalorOutcome.Selects },
        { "char-int-vs-object", ["int", "object"], "c", CalorOutcome.Selects },
        { "char-ushort-vs-int", ["ushort", "int"], "c", CalorOutcome.Selects },
        { "short-vs-int-exact", ["int", "short"], "s", CalorOutcome.Selects },
        { "uint-vs-long-variable", ["long", "ulong"], "ui", CalorOutcome.Selects },
        // Conversions this binder does not model: boxing to an interface, a user-defined
        // conversion, a user enum or struct under `?`. The rules decline; with the projection
        // the call stands only where Roslyn selected the same declaration.
        { "interface-boxing", ["System.IComparable", "object"], "i", CalorOutcome.Declines },
        { "interface-boxing-vs-long", ["System.IComparable", "long"], "i", CalorOutcome.Declines },
        { "user-defined-implicit", ["Meters", "object"], "i", CalorOutcome.Declines },
        { "user-defined-implicit-vs-long", ["Meters", "long"], "i", CalorOutcome.Declines },
        { "enum-nullable-target", ["Color?", "object"], "Color.Red", CalorOutcome.Declines },
        // Decided without modeling Pt: everything converts to object and object to nothing
        // else, and an exact match beats any other conversion.
        { "struct-nullable-target", ["Pt?", "object"], "new Pt()", CalorOutcome.Selects },
        { "struct-exact-vs-nullable", ["Pt", "Pt?"], "new Pt()", CalorOutcome.Selects },
        { "struct-nullable-vs-interface", ["Pt?", "System.IComparable"], "new Pt()", CalorOutcome.Declines },
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void SourceOverload_SelectsWhatRoslynSelects_OrDeclines(
        string row, string[] overloads, string argument, CalorOutcome expected)
    {
        // The reference is Roslyn on the C# Calor generates (what runs). When Calor rejects the
        // program there is none, and the hand-written equivalent stands in.
        var compiled = Program.Compile(CalorSource(overloads, argument), "differential.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null, EnforceEffects = false, DeferGeneratedOutputValidation = true });
        var reference = compiled.HasErrors
            ? RoslynSelection(CSharpSource(overloads, argument))
            : RoslynSelection(compiled.GeneratedCode);
        foreach (var projection in new[] { false, true })
        {
            var (selected, declined) = CalorSelection(CalorSource(overloads, argument), overloads, projection);
            if (selected is { } calor)
            {
                Assert.True(reference == calor,
                    $"{row} (projection {projection}): Calor selected F({overloads[calor]}), Roslyn "
                    + (reference is { } roslyn ? $"F({overloads[roslyn]})" : "reports an error"));
            }
            else
            {
                Assert.True(declined, $"{row} (projection {projection}): no selection and no decline");
            }
            if (!projection)
                Assert.True(expected == (selected is null ? CalorOutcome.Declines : CalorOutcome.Selects),
                    $"{row}: expected Calor to {expected}; selected {(selected is { } s ? overloads[s] : "nothing")}");
        }

        // The generated C# makes the same choice as the hand-written C#, so the rows also
        // describe C# programs (a Calor long literal is written with C#'s `L`).
        if (!compiled.HasErrors && row != "long-literal")
            Assert.Equal(RoslynSelection(CSharpSource(overloads, argument)), reference);
    }

    // Opaque calls keep effects unknown: a pure caller of one is a Calor0410 error.
    [Fact]
    public void DeclinedCall_IsChargedUnknown()
    {
        var source = CalorSource(["byte", "long"], "(C + 1)");
        var compiled = Program.Compile(source, "declined.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null, DeferGeneratedOutputValidation = true });
        Assert.Contains(compiled.Diagnostics.Errors, diagnostic =>
            diagnostic.Code == "Calor0410" && diagnostic.Message.Contains("'Pick'"));
        var selected = Program.Compile(CalorSource(["byte", "long"], "C"), "selected.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null, DeferGeneratedOutputValidation = true });
        Assert.False(selected.HasErrors, string.Join("\n", selected.Diagnostics.Errors));
    }

    private static string CalorSource(string[] overloads, string argument)
    {
        var functions = string.Join("", overloads.Select((type, index) => $"""

                func F ({type}:x) → int
                  !
                  return {index}

            """));
        return $"""
            module Differential
              enum Color
                Red
                Green

              class Pt struct
                field int:X

              class Meters
                op implicit (int:value) → Meters
                  ! alloc
                  return new Meters()

              module L
                field int:C const = 1
                field int:Neg const = -1
                field long:CL const = 5
                field int:Alias const = C
            {functions}
                func Pick ({PickParameters}) → int
                  !
                  return F({argument})

            """;
    }

    private static string CSharpSource(string[] overloads, string argument)
    {
        var functions = string.Join("\n", overloads.Select((type, index) =>
            $"    public static int F({type} x) => {index};"));
        return $$"""
            public enum Color { Red, Green }
            public struct Pt { public int X; }
            public class Meters { public static implicit operator Meters(int value) => new Meters(); }
            public static class L
            {
                public const int C = 1;
                public const int Neg = -1;
                public const long CL = 5;
                public const int Alias = C;
            {{functions}}
                public static int Pick({{PickParametersCSharp}}) => F({{argument}});
            }
            """;
    }

    /// <summary>The index of the F overload Roslyn binds the call in Pick to; null on an error.</summary>
    private static int? RoslynSelection(string csharp)
    {
        var tree = CSharpSyntaxTree.ParseText(csharp);
        var compilation = CSharpCompilation.Create("Differential_" + Guid.NewGuid().ToString("N"), [tree],
            TrustedPlatformReferences.All, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot();
        var pick = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == "Pick");
        var call = pick.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        if (compilation.GetDiagnostics().Any(diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error
                && call.Span.IntersectsWith(diagnostic.Location.SourceSpan)))
            return null;
        if (model.GetSymbolInfo(call).Symbol is not IMethodSymbol method)
            return null;
        var declarations = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(declaration => declaration.Identifier.ValueText == "F").ToList();
        var declared = method.DeclaringSyntaxReferences.Single().GetSyntax();
        return declarations.IndexOf((MethodDeclarationSyntax)declared);
    }

    /// <summary>Calor's selection (overload index), or whether it declined (opaque or an error).</summary>
    private static (int? Selected, bool Declined) CalorSelection(string source, string[] overloads, bool projection)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors));
        var bound = new Binder(diagnostics) { ResolveSourceCallConversions = projection }.Bind(module);
        var pick = bound.Functions.SingleOrDefault(function => function.Symbol.Name == "L.Pick");
        Assert.True(pick is not null, "functions: " + string.Join(", ", bound.Functions.Select(function => function.Symbol.Name)));
        var call = Descendants(pick).OfType<BoundCallExpression>().SingleOrDefault(candidate =>
            candidate.Target is "F" or "L.F" || candidate.ResolvedSymbol?.Name is "F" or "L.F");
        Assert.True(call is not null, "no call to F in Pick: " + string.Join(", ", Descendants(pick)
            .Select(node => node is BoundCallExpression c ? $"call:{c.Target}" : node.GetType().Name))
            + "\n" + string.Join("\n", diagnostics));
        if (call.ResolvedSymbol is { Name: "F" or "L.F" } function)
        {
            var parameter = TypeIdentity.Canonicalize(Assert.Single(function.Parameters).TypeName);
            var index = Array.FindIndex(overloads, type => TypeIdentity.Canonicalize(type) == parameter);
            Assert.True(index >= 0, $"unknown overload {parameter}");
            Assert.False(call.HasOpaqueOverloads);
            return (index, false);
        }
        var rejected = diagnostics.Errors.Any(diagnostic =>
            diagnostic.Code is DiagnosticCode.NoMatchingOverload or DiagnosticCode.AmbiguousOverload);
        return (null, call.HasOpaqueOverloads || rejected);
    }

    private static IEnumerable<BoundNode> Descendants(BoundNode node)
    {
        yield return node;
        foreach (var child in node.ChildNodes)
            foreach (var descendant in Descendants(child))
                yield return descendant;
    }
}
