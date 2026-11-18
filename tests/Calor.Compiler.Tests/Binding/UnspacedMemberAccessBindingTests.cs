using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;
using Binder = Calor.Compiler.Binding.Binder;

namespace Calor.Compiler.Tests;

/// <summary>
/// The lexer reads <c>x.Size</c> as one dotted identifier. When its first segment is a
/// variable in scope it is a member read on that variable, exactly like the spaced
/// <c>x . Size</c>, for nullable and non-nullable user types alike. Before, a user-class
/// receiver fell through to "Undefined variable 'x.Size'".
/// </summary>
public sealed class UnspacedMemberAccessBindingTests
{
    private const string Types = """

          class Node public
            field Node?:Inner public
            field string?:Value public

          class Text public
            field int:Size public
        """;

    [Theory]
    [InlineData("Text?:t", "t.Size", "t . Size")]
    [InlineData("Text:t", "t.Size", "t . Size")]
    [InlineData("Node?:t", "t.Inner.Value", "t . Inner . Value")]
    [InlineData("Node:t", "t.Inner.Value", "t . Inner . Value")]
    public void VariableReceiver_BindsLikeTheSpacedForm(string parameter, string unspaced, string spaced)
    {
        var actual = Shape(parameter, unspaced);
        Assert.Equal(Shape(parameter, spaced), actual);
        Assert.EndsWith("<-t", actual, StringComparison.Ordinal);
    }

    [Fact]
    public void NonNullUserReceiver_ResolvesTheDeclaredField()
    {
        // A possibly-null receiver keeps the spaced form's unidentified read (its
        // dereference may throw); an established one resolves the field identity.
        var (bound, diagnostics) = Bind("""
            module M
              func Read public (Text:t) → int
                !
                return t.Size
            """ + Types);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var read = Assert.Single(Reads(bound));
        Assert.Equal("Size", read.ResolvedField?.Name);
        Assert.Equal("int", read.Type.DisplayString);
    }

    private static string Shape(string parameter, string expression)
    {
        var (bound, diagnostics) = Bind($"""
            module M
              func Read public ({parameter}) → object?
                !
                return {expression}
            """ + Types);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var read = Reads(bound).Single(candidate => !Reads(bound).Any(outer => outer.Target == candidate));
        var parts = new List<string>();
        BoundExpression current = read;
        while (current is BoundFieldAccessExpression member)
        {
            parts.Add($"{member.FieldName}:{member.ResolvedField?.Name}:{member.Type.DisplayString}");
            current = member.Target;
        }
        parts.Add("<-" + Assert.IsType<BoundVariableExpression>(current).Variable.Name);
        return string.Join(" ", parts);
    }

    [Theory]
    [InlineData("Text:t", "t~Nope", "int")]
    [InlineData("Node:obj", "obj~Nope~Value", "string?")]
    public void MissingMember_IsReportedExactlyLikeTheSpacedForm(string parameter, string read, string output)
    {
        // The unspaced read is the spaced form's member access, so a missing member binds
        // and fails exactly as there: the generated C# names it (CS1061), or a chain past it
        // has no known receiver and is charged unknown.
        string Source(string separator) => $"""
            module M
              func Read public ({parameter}) → {output}
                !
                return {read.Replace("~", separator, StringComparison.Ordinal)}
            """ + Types;

        var (_, unspacedBinding) = Bind(Source("."));
        var (_, spacedBinding) = Bind(Source(" . "));
        Assert.Equal(Messages(spacedBinding), Messages(unspacedBinding));
        var unspaced = Program.Compile(Source("."));
        var spaced = Program.Compile(Source(" . "));
        Assert.True(unspaced.HasErrors);
        Assert.Equal(Codes(spaced), Codes(unspaced));
        Assert.Contains(unspaced.Diagnostics.Errors, error => error.Code is DiagnosticCode.CodeGenCompilationError
            or DiagnosticCode.ForbiddenEffect);

        static string[] Messages(DiagnosticBag diagnostics) => diagnostics.Select(d => $"{d.Code} {d.Message}").ToArray();
        static string[] Codes(CompilationResult result) => result.Diagnostics.Select(d => d.Code).ToArray();
    }

    [Fact]
    public void QualifiedNamesWithoutAVariableRoot_KeepTheirBinding()
    {
        var (bound, diagnostics) = Bind("""
            module M
              func Read public (int:value) → int
                !
                return System.Math.Max(value, System.Int32.MaxValue)
            """);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        Assert.Contains(BoundNodeHelpers.DescendantsAndSelf(bound.Functions.Single()),
            node => node is BoundTypeReferenceExpression { Name: "System.Math" or "System.Int32" }
                || node is BoundFieldAccessExpression { FieldName: "MaxValue" });
    }

    private static IEnumerable<BoundFieldAccessExpression> Reads(BoundModule bound) =>
        BoundNodeHelpers.DescendantsAndSelf(bound.Functions.Single()).OfType<BoundFieldAccessExpression>();

    private static (BoundModule Bound, DiagnosticBag Diagnostics) Bind(string source)
    {
        var parse = new DiagnosticBag();
        var module = new Parser(new Lexer(source, parse).TokenizeAllForParser(), parse).Parse();
        Assert.False(parse.HasErrors, string.Join("\n", parse));
        var diagnostics = new DiagnosticBag();
        return (new Binder(diagnostics).Bind(module), diagnostics);
    }
}
