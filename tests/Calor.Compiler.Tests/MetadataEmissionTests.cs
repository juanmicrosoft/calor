using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public class MetadataEmissionTests
{

    [Fact]
    public void ProgrammaticProseMetadata_EmitsDocumentationNotRetiredMarkers()
    {
        var span = TextSpan.Empty;
        var attributes = new AttributeCollection();
        var dependency = new DependencyNode(span, "Dependency", "1.0", true, attributes);
        var issue = new IssueNode(span, IssueKind.Fixme, "issue1", "correctness", IssuePriority.High,
            "Keep this note.\n§F{NotADeclaration:pub}", attributes);
        var function = new FunctionNode(
            span, "f1", "Value", Visibility.Public, [], [], new OutputNode(span, "i32"), null,
            [], [], [new ReturnStatementNode(span, new IntLiteralNode(span, 1))], attributes,
            examples: [], issues: [issue],
            uses: new UsesNode(span, [dependency], attributes),
            usedBy: new UsedByNode(span, [dependency], true, attributes),
            assumptions: [],
            complexity: new ComplexityNode(span, ComplexityClass.ON, ComplexityClass.O1, true, null, attributes),
            since: null, deprecated: null, breakingChanges: [], properties: [],
            lockNode: new LockNode(span, "agent", null, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), attributes),
            author: new AuthorNode(span, "author", new DateOnly(2026, 1, 1), "task1", attributes),
            taskRef: new TaskRefNode(span, "task1", "Finish the work.", attributes));
        var decision = new DecisionNode(span, "decision1", "Representation", "Named construction",
            ["Preserves constructor semantics."],
            [new RejectedOptionNode(span, "Property initializer", ["No parameterless constructor."], attributes)],
            "Record creation", new DateOnly(2026, 1, 1), "author", attributes);
        var context = new ContextNode(span, true,
            [new FileRefNode(span, "visible.calr", "Visible file.", attributes)],
            [new FileRefNode(span, "hidden.calr", "Hidden file.", attributes)], "Value", attributes);
        var module = new ModuleNode(span, "m1", "Prose", [], [], [], [function], attributes,
            [new IssueNode(span, IssueKind.Todo, null, null, IssuePriority.Medium, "Module note.", attributes)],
            [], [], [decision], context);

        var emitted = new CalorEmitter().Emit(module);
        var reparsed = Parse(emitted);
        Assert.Empty(reparsed.Issues);
        Assert.Empty(reparsed.Decisions);
        Assert.Null(reparsed.Context);
        var replay = Assert.Single(reparsed.Functions);
        Assert.Contains("Module note.", reparsed.DocComment);
        Assert.Contains("Decision decision1: Representation", reparsed.DocComment);
        Assert.Contains("Named construction", reparsed.DocComment);
        Assert.Contains("Property initializer", reparsed.DocComment);
        Assert.Contains("No parameterless constructor.", reparsed.DocComment);
        Assert.Contains("Visible: visible.calr (Visible file.)", reparsed.DocComment);
        Assert.Contains("Hidden: hidden.calr (Hidden file.)", reparsed.DocComment);
        Assert.Contains("Focus: Value", reparsed.DocComment);
        Assert.Contains("Keep this note.", replay.DocComment);
        Assert.Contains("§F{NotADeclaration:pub}", replay.DocComment);
        Assert.Contains("Uses: Dependency@1.0?", replay.DocComment);
        Assert.Contains("Used by: Dependency@1.0?, [external]", replay.DocComment);
        Assert.Contains("Complexity: worst:time:O(n),space:O(1)", replay.DocComment);
        Assert.Contains("Lock: agent=agent", replay.DocComment);
        Assert.Contains("Author: agent=author:date=2026-01-01:task=task1", replay.DocComment);
        Assert.Contains("Task task1: Finish the work.", replay.DocComment);
        Assert.Equal(emitted, new CalorEmitter().Emit(reparsed));
    }

    [Theory]
    [InlineData("reason", null)]
    [InlineData(null, "4.0.0")]
    public void UnrepresentableDeprecationFields_FailExplicitlyInsteadOfDisappearing(string? reason, string? removed)
    {
        var metadata = new DeprecatedNode(TextSpan.Empty, "2.0.0", null, reason, removed, new AttributeCollection());
        var exception = Assert.Throws<InvalidOperationException>(() => metadata.Accept(new CalorEmitter()));
        Assert.Contains("cannot preserve", exception.Message);
        Assert.Contains("structured-documentation replacement", exception.Message);
    }

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors));
        return module;
    }
}
