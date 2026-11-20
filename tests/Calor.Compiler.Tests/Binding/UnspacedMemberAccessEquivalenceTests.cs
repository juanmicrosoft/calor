using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// 0.30: an unspaced member read <c>x.Field</c> means exactly what the spaced
/// <c>x . Field</c> means. The lexer reads <c>x.Field</c> as one dotted name; when its
/// first segment is a value in scope (or a source class of the module with that member)
/// the parser lowers it into the spaced form's member-access chain, so every later pass
/// (binding, typing, effects, nullability, contracts, verification, codegen) sees the same
/// tree. Each program below is compiled in both spellings and must produce the same
/// diagnostics, effect summary and generated C#; a test also checks that the program
/// exercises what it is named for, so a vacuous pass is impossible.
/// </summary>
public sealed class UnspacedMemberAccessEquivalenceTests
{
    // `~` marks a member access: `.` in the unspaced spelling, ` . ` in the spaced one.
    public static TheoryData<string, string> Programs => new()
    {
        { "nullable-receiver", """
            module Nullable
              class Text public
                field int:Size public
                field Text?:Next public

              func Read public (Text?:t) → int
                !
                return t~Size

              func Chain public (Text:t) → int
                !
                return t~Next~Size

              func Contracted public (Text:t) → int
                !
                requires t~Size > 0
                => result == t~Size
                return t~Size
            """ },
        { "static-constructor", """
            module StaticCtor
              class Holder
                field int:Value public static = 7

                ctor static ()
                  ! cw
                  Console.WriteLine("init")

              func Read public () → int
                ! cw
                return Holder~Value

              func ReadPure public () → int
                !
                return Holder~Value
            """ },
        { "property-row", """
            module Rows
              class Account
                field int:balance private
                property int:Balance
                  get
                    ! throw
                    if balance < 0
                      throw new InvalidOperationException("corrupt")
                    return balance

              func Read public (Account:account) → int
                ! throw
                return account~Balance

              func ReadPure public (Account:account) → int
                !
                return account~Balance
            """ },
        { "generic-receiver", """
            module SelectedMembers
              func Read<T> public (List<T>:items) → int
                !
                return items~Count

              func Flag<T> public (ICollection<T>:items) → bool
                !
                return items~IsReadOnly
            """ },
        { "shadowing-local", """
            module Shadow
              class Box
                field double:PI public

              func Parameter public (Box:Math) → double
                !
                return Math~PI

              func Local public () → double
                ! alloc
                let Math = new Box()
                return Math~PI
            """ },
        { "scoped-values", """
            module Scoped
              class Box
                field int:Size public

              class Node
                field Box:Inner public = new Box()
                func Twice public () → int
                  !
                  return Inner~Size + Inner~Size

              class Leaf
                extends Node
                func Own public () → int
                  !
                  return Inner~Size

              func Sum public (List<Node>:nodes) → int
                !
                var total = 0
                each Node:node in nodes
                  set total total + node~Inner~Size
                return total

              func Pick public (object:value) → int
                !
                if value is Node found
                  return found~Inner~Size
                return 0

              func Mapped public (Node:start) → int
                !
                let Func<Node, int>:size = (Node:n) ! => n~Inner~Size
                return size(start)
            """ },
    };

    [Theory]
    [MemberData(nameof(Programs))]
    public void BothSpellings_CompileIdentically(string name, string template)
    {
        var unspaced = template.Replace("~", ".", StringComparison.Ordinal);
        var spaced = template.Replace("~", " . ", StringComparison.Ordinal);

        Assert.Equal(Shape(spaced), Shape(unspaced));
        Assert.DoesNotContain(Nodes(Parse(unspaced)), node => node is ReferenceNode reference
            && reference.Name.Contains('.', StringComparison.Ordinal));
        foreach (var options in Options())
        {
            var expected = Observe(spaced, options());
            Assert.Equal(expected, Observe(unspaced, options()));
            Assert.Contains("<generated>", expected, StringComparison.Ordinal);
        }
        AssertExercised(name, Observe(unspaced, Options()[0]()), Observe(unspaced, Options()[1]()));
    }

    private static void AssertExercised(string name, string verified, string strict)
    {
        switch (name)
        {
            case "nullable-receiver":
                Assert.Contains("Calor0922", verified, StringComparison.Ordinal);
                Assert.Contains("Precondition failed: t.Size > 0", verified, StringComparison.Ordinal);
                break;
            case "static-constructor":
                // Reading the static field may run the static constructor.
                Assert.Contains("Function 'ReadPure' uses effect 'cw'", strict, StringComparison.Ordinal);
                Assert.DoesNotContain("Function 'Read' uses", strict, StringComparison.Ordinal);
                break;
            case "property-row":
                Assert.Contains("Function 'ReadPure' uses effect 'throw'", strict, StringComparison.Ordinal);
                break;
            case "generic-receiver":
                Assert.Contains("get_IsReadOnly", strict, StringComparison.Ordinal);
                Assert.DoesNotContain("Function 'Read' uses", strict, StringComparison.Ordinal);
                break;
            case "shadowing-local":
                Assert.DoesNotContain(" Error ", strict, StringComparison.Ordinal);
                Assert.DoesNotContain("System.Math", verified, StringComparison.Ordinal);
                break;
            case "scoped-values":
                Assert.DoesNotContain(" Error ", strict, StringComparison.Ordinal);
                break;
        }
    }

    /// <summary>
    /// 0.30 (i8): a chain rooted at a type whose qualifier is itself a member read stays one
    /// dotted name when written unspaced (the parser cannot tell where the type name ends),
    /// so the trees differ; the binder and the effect pass must still read it exactly as the
    /// spaced member access, and produce the same diagnostics, effects and C#.
    /// </summary>
    public static TheoryData<string, string> TypeRootedChains => new()
    {
        { "metadata-struct-result", """
            module Clock
              func Year public () → int
                ! time
                return DateTime.Now~Year

              func YearPure public () → int
                !
                return DateTime.Now~Year

              func Qualified public () → int
                ! time
                return System.DateTime.Now~Year
            """ },
        { "metadata-reference-result", """
            module Writer
              func Width public () → int
                ! unknown
                return Console.Out~NewLine~Length

              func WidthPure public () → int
                !
                return Console.Out~NewLine~Length
            """ },
        { "nested-source-static", """
            module Nested
              class Outer
                class Inner public
                  field Inner:Instance public static = new Inner()
                  field int:Size public

              func Read public () → int
                ! alloc
                return Outer.Inner~Instance~Size

              func ReadPure public () → int
                !
                return Outer.Inner~Instance~Size
            """ },
    };

    [Theory]
    [MemberData(nameof(TypeRootedChains))]
    public void TypeRootedChains_BothSpellings_CompileIdentically(string name, string template)
    {
        var unspaced = template.Replace("~", ".", StringComparison.Ordinal);
        var spaced = template.Replace("~", " . ", StringComparison.Ordinal);
        // The unspaced spelling keeps the dotted name: this is the case the parser cannot lower.
        Assert.Contains(Nodes(Parse(unspaced)), node => node is ReferenceNode reference
            && reference.Name.Count(c => c == '.') >= 2);
        // The type checker warns that it cannot type the spaced `Outer.Inner . Instance`
        // (Calor0200, a nested type); that warning is not about effects and is set aside.
        static string WithoutTypeWarnings(string observed) => string.Join("\n", observed.Split('\n')
            .Where(line => !line.StartsWith("Calor0200 Warning", StringComparison.Ordinal)));
        foreach (var options in Options())
            Assert.Equal(WithoutTypeWarnings(Observe(spaced, options())),
                WithoutTypeWarnings(Observe(unspaced, options())));
        var strict = Observe(unspaced, Options()[1]());
        switch (name)
        {
            case "metadata-struct-result":
                // DateTime.Now is `time` in the manifest and DateTime.Year is pure.
                Assert.Contains("Function 'YearPure' uses effect 'time'", strict, StringComparison.Ordinal);
                Assert.DoesNotContain("Function 'Year' uses", strict, StringComparison.Ordinal);
                Assert.DoesNotContain("Function 'Qualified' uses", strict, StringComparison.Ordinal);
                break;
            case "metadata-reference-result":
                Assert.Contains("TextWriter.get_NewLine", strict, StringComparison.Ordinal);
                Assert.Contains("Function 'WidthPure' uses effect 'unknown'", strict, StringComparison.Ordinal);
                Assert.DoesNotContain("Function 'Width' uses", strict, StringComparison.Ordinal);
                break;
            case "nested-source-static":
                Assert.Contains("Function 'ReadPure' uses effect 'alloc'", strict, StringComparison.Ordinal);
                Assert.DoesNotContain("Function 'Read' uses", strict, StringComparison.Ordinal);
                break;
        }
    }

    [Fact]
    public void NamespaceTypeAndEnumHeads_KeepTheirQualifiedNames()
    {
        var module = Parse("""
            module Qualified
              enum Color
                Red
              func Read public () → double
                !
                let Color:c = Color.Red
                return System.Math.PI + Math.E
            """);
        var names = Nodes(module).OfType<ReferenceNode>().Select(reference => reference.Name).ToArray();
        Assert.Contains("Color.Red", names);
        Assert.Contains("System.Math.PI", names);
        Assert.Contains("Math.E", names);
        Assert.DoesNotContain(Nodes(module), node => node is FieldAccessNode);
    }

    [Fact]
    public void ValueWhoseTypeHasItsOwnName_KeepsTheQualifiedName()
    {
        // C#'s "Color Color" rule lets `Color.Red` mean the type's static member even when
        // a value named Color of type Color is in scope; the dotted name keeps that reading.
        var module = Parse("""
            module ColorColor
              enum Color
                Red
              func Read public (Color:Color) → Color
                !
                return Color.Red
            """);
        Assert.Contains(Nodes(module), node => node is ReferenceNode { Name: "Color.Red" });
    }

    [Fact]
    public void Emitter_PrintsMemberAccessUnspaced_AndRoundTripsTheTree()
    {
        var source = """
            module Emitted
              class Holder
                field Holder:Instance public static = new Holder()
                field int:Size public
              func Read public (Holder:h) → int
                ! alloc
                return h . Instance . Size + Holder . Instance . Size + (Console.Out) . NewLine . Length
            """;
        var emitted = new CalorEmitter().Emit(Parse(source));
        Assert.Contains("(h.Instance.Size + Holder.Instance.Size) + ((Console.Out).NewLine).Length", emitted,
            StringComparison.Ordinal);
        Assert.DoesNotContain(" . ", emitted, StringComparison.Ordinal);
        Assert.Equal(Shape(source), Shape(emitted));
    }

    private static Func<CompilationOptions>[] Options() =>
    [
        () => new CompilationOptions { VerifyContracts = true, EnableVerificationAnalyses = true },
        () => new CompilationOptions { StrictEffects = true },
    ];

    private static string Observe(string source, CompilationOptions options)
    {
        var result = Program.Compile(source, "equivalence.calr", options);
        var text = new StringBuilder();
        foreach (var diagnostic in result.Diagnostics)
            text.AppendLine($"{diagnostic.Code} {diagnostic.Severity} L{diagnostic.Span.Line} {diagnostic.Message}");
        // Contract guards quote their source offsets, which the two spellings shift.
        text.AppendLine("<generated>");
        text.AppendLine(Regex.Replace(result.GeneratedCode, @"startOffset: \d+, length: \d+", "startOffset, length"));
        if (result.Ast is { } module)
            text.AppendLine(Regex.Replace(JsonSerializer.Serialize(EffectSummaryBuilder.Build(module)),
                "\"[A-Za-z]*Column\":\\d+,?", ""));
        return text.ToString();
    }

    private static string Shape(string source)
    {
        var text = new StringBuilder();
        void Write(AstNode node, int depth)
        {
            text.Append(' ', depth).Append(node.GetType().Name);
            switch (node)
            {
                case ReferenceNode reference: text.Append(' ').Append(reference.Name); break;
                case FieldAccessNode member: text.Append(" .").Append(member.FieldName); break;
            }
            text.AppendLine();
            foreach (var child in RecursiveAstWalker.GetAllChildren(node))
                Write(child, depth + 1);
        }
        Write(Parse(source), 0);
        return text.ToString();
    }

    private static IEnumerable<AstNode> Nodes(AstNode node) =>
        new[] { node }.Concat(RecursiveAstWalker.GetAllChildren(node).SelectMany(Nodes));

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        return module;
    }
}
