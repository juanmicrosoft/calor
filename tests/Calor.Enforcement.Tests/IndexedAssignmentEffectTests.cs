using Calor.Compiler;
using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Enforcement.Tests;

public sealed class IndexedAssignmentEffectTests
{
    [Theory]
    [InlineData("§ASSIGN §IDX items INT:0 INT:42")]
    [InlineData("§SETIDX{items} INT:0 INT:42")]
    [InlineData("§ASSIGN §IDX items INT:0 (+ §IDX items INT:0 INT:41)")]
    public void IndexedWrites_RequireMutationAndExecuteWhenDeclared(string statement)
    {
        var rejected = Program.Compile(CurrentSource(statement, ""), "indexed-write.calr", Options());
        AssertMissing(rejected.Diagnostics, "mut");

        var items = new[] { 1 };
        var execution = TestHarness.Execute(CurrentSource(statement, "mut"), "Write", [items], Options());
        Assert.Null(execution.Exception);
        Assert.Equal(42, items[0]);
    }

    [Theory]
    [InlineData("§ASSIGN §IDX items §C{Index} §/C INT:42")]
    [InlineData("§SETIDX{items} (cast i32 §C{Index} §/C) INT:42")]
    [InlineData("§ASSIGN §IDX §C{Receiver} §A items §/C §C{Index} §/C INT:42")]
    public void TargetCalls_ContributeEffectsIndependentlyOfMutation(string statement)
    {
        var missingConsole = Program.Compile(CurrentSource(statement, "mut"), "target-effects.calr", Options());
        AssertMissing(missingConsole.Diagnostics, "cw");
        var missingMutation = Program.Compile(CurrentSource(statement, "cw"), "target-mutation.calr", Options());
        AssertMissing(missingMutation.Diagnostics, "mut");

        var items = new[] { 1 };
        var execution = TestHarness.Execute(CurrentSource(statement, "mut,cw"), "Write", [items], Options());
        Assert.Null(execution.Exception);
        Assert.Equal(42, items[0]);
    }

    [Fact]
    public void MultidimensionalTargets_ChargeMutationAndAllIndices()
    {
        var source = Source("§ASSIGN §IDX2D{2} items INT:0 §C{Index} §/C INT:42", "mut")
            .Replace("(i32[]:items) -> void", "(i32[,]:items) -> void");
        var current = CurrentSource("§ASSIGN §IDX2D{2} items INT:0 §C{Index} §/C INT:42", "mut", matrix: true);
        AssertMissing(Program.Compile(current, "matrix-write.calr", Options()).Diagnostics, "cw");
        var retiredWithoutMutation = source.Replace("§E{ mut }", "§E{cw}");
        AssertMissing(Program.Compile(CurrentSource(
                "§ASSIGN §IDX2D{2} items INT:0 §C{Index} §/C INT:42", "cw", matrix: true),
            "matrix-mutation.calr", Options()).Diagnostics, "mut");
        var items = new int[1, 1];
        var retiredWithBothEffects = source.Replace("§E{ mut }", "§E{mut,cw}");
        var execution = TestHarness.Execute(CurrentSource(
                "§ASSIGN §IDX2D{2} items INT:0 §C{Index} §/C INT:42", "mut,cw", matrix: true),
            "Write", [items], Options());
        Assert.Null(execution.Exception);
        Assert.Equal(42, items[0, 0]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompoundAssignmentAst_UsesTheSameTargetEffectRules(bool effectfulTarget)
    {
        var target = effectfulTarget
            ? "§IDX §C{Receiver} §A items §/C §C{Index} §/C"
            : "§IDX items INT:0";
        foreach (var row in new[] { "", "mut", "mut,cw" })
        {
            var diagnostics = new DiagnosticBag();
            var source = Source($"§ASSIGN {target} INT:41", row);
            var current = CurrentSource($"§ASSIGN {target} INT:41", row);
            var module = new Parser(new Lexer(current, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
            Assert.False(diagnostics.HasErrors);
            var write = Assert.Single(module.Functions, function => function.Name == "Write");
            var assignment = Assert.IsType<AssignmentStatementNode>(Assert.Single(write.Body));
            var compound = new CompoundAssignmentStatementNode(assignment.Span,
                assignment.Target, CompoundAssignmentOperator.Add, assignment.Value);
            var replacement = write.CopyMetadataTo(new FunctionNode(write.Span, write.Id, write.Name, write.Visibility,
                write.Parameters, write.Output, write.Effects, [compound], write.Attributes));
            module = module.With(update => update.Functions = module.Functions
                .Select(function => function == write ? replacement : function).ToArray());

            new EffectEnforcementPass(diagnostics, UnknownCallPolicy.Strict).Enforce(module);
            if (row.Length == 0)
                AssertMissing(diagnostics, "mut");
            else if (effectfulTarget && row == "mut")
                AssertMissing(diagnostics, "cw");
            else
                Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
        }
    }

    private static void AssertMissing(DiagnosticBag diagnostics, string effect) =>
        Assert.Contains(diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.ForbiddenEffect
            && diagnostic.Message.Contains(effect, StringComparison.Ordinal));

    private static CompilationOptions Options() => new()
    {
        EnableTypeChecking = true,
        EnforceEffects = true,
        VerifyContracts = false,
        ContractMode = ContractMode.Debug,
        ElideProvenGuards = true,
        StatusWriter = TextWriter.Null
    };

    // Historical inputs remain intact; only their explicit native counterparts enter the current compiler.
    private static string CurrentSource(string statement, string effects, bool matrix = false)
    {
        var currentStatement = statement switch
        {
            "§ASSIGN §IDX items INT:0 INT:42" => "set items[0] 42",
            "§SETIDX{items} INT:0 INT:42" => "set items[0] 42",
            "§ASSIGN §IDX items INT:0 (+ §IDX items INT:0 INT:41)" => "set items[0] items[0] + 41",
            "§ASSIGN §IDX items §C{Index} §/C INT:42" => "set items[Index()] 42",
            "§SETIDX{items} (cast i32 §C{Index} §/C) INT:42" => "set items[((int)Index())] 42",
            "§ASSIGN §IDX §C{Receiver} §A items §/C §C{Index} §/C INT:42" => "set Receiver(items)[Index()] 42",
            "§ASSIGN §IDX2D{2} items INT:0 §C{Index} §/C INT:42" => "set items[0, Index()] 42",
            "§ASSIGN §IDX items INT:0 INT:41" => "set items[0] 41",
            "§ASSIGN §IDX §C{Receiver} §A items §/C §C{Index} §/C INT:41" => "set Receiver(items)[Index()] 41",
            _ => throw new ArgumentOutOfRangeException(nameof(statement), statement, "Unregistered indexed-write fixture.")
        };
        var source = $$"""
            module IndexedEffects IndexedEffects block
              func Index public () → int
                ! cw
                System.Console.WriteLine("index")
                return 0
              func Receiver public (int[]:items) → int[]
                ! cw
                System.Console.WriteLine("receiver")
                return items
              func Write public ({{(matrix ? "int[,]" : "int[]")}}:items) → void
                ! {{effects}}
                {{currentStatement}}
            """;
        var diagnostics = new DiagnosticBag();
        _ = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
        return source;
    }

    private static string Source(string statement, string effects) => $$"""
        §M{IndexedEffects}
          §F{Index:pub} () -> i32
            §E{cw}
            §P "index"
            §R INT:0
          §F{Receiver:pub} (i32[]:items) -> i32[]
            §E{cw}
            §P "receiver"
            §R items
          §F{Write:pub} (i32[]:items) -> void
            §E{ {{effects}} }
            {{statement}}
        """;
}
