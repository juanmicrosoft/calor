using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Xunit;

// Flat test namespace: a Calor.Compiler.Tests.Binding namespace would shadow other test
// files' relative `Binding.X` qualifiers against Calor.Compiler.Binding.
namespace Calor.Compiler.Tests;

/// <summary>
/// #762 B1 (scoping doc D1): the dispatch table is the one authoritative expression
/// binding surface, and it must cover the concrete ExpressionNode subclass set EXACTLY —
/// in both directions. A new node class with no dispatch entry, or a stale entry for a
/// removed class, fails here by construction (the code-side half of F-1's bidirectional
/// completeness rule). The current language has 57 accepted expression classes;
/// KeywordArgNode is a parser helper rather than a value expression.
/// </summary>
public class BinderDispatchCompletenessTests
{
    [Theory]
    [InlineData("let Func<Task<int>>:factory = async () => 42", 2)]
    [InlineData("let Func<ValueTask<int>>:factory = async () => 42", 2)]
    [InlineData("let factory = async () => 42", 2)]
    [InlineData("let Func<int>:factory = () => 42", 2)]
    [InlineData("let Func<Task<int>>:factory = async () => 40 + 2", 4)]
    public void ContextualLambdaBinding_CountsEachNativeExpressionOnce(string statement, int expectedCount)
    {
        AssertExpressionCount($$"""
            module LambdaAccounting
              func Probe () → void
                !
                {{statement}}
            """, expectedCount);
    }

    [Theory]
    [InlineData("Task", 2)]
    [InlineData("ValueTask", 2)]
    public void ContextualLambdaReturn_CountsEachNativeExpressionOnce(string carrier, int expectedCount)
    {
        AssertExpressionCount($$"""
            module LambdaReturnAccounting
              func Probe () → Func<{{carrier}}<int>> ! unknown
                !
                return async () => 42
            """, expectedCount);
    }

    [Theory]
    [InlineData("checked")]
    [InlineData("unchecked")]
    public void OverflowScope_CountsItsWrapperAndEveryOperandOnce(string keyword)
    {
        AssertExpressionCount($$"""
            module OverflowAccounting
              func Probe () → void
                !
                let int:value = {{keyword}}(1 + 2)
            """, 4);
    }

    [Fact]
    public void AsyncOverloadCandidates_DoNotDuplicateLambdaAccounting()
    {
        AssertExpressionCount("""
            module CandidateAccounting
              func Choose async (int:value) → Task<int>
                !
                return value
              func Choose async (string:value) → Task<int>
                !
                return 0
              func Consume async () → Task<int>
                !
                let Func<Task<int>>:factory = async () => 42
                return await Choose(42)
            """, 7);
    }

    [Fact]
    public void GenericAsyncMethodGroup_CountsDispatchWithoutRebindingLambda()
    {
        AssertExpressionCount("""
            module MethodGroupAccounting
              func Identity<T> async (T:value) → Task<T>
                !
                return value
              func Consume async () → Task<int>
                !
                let Func<ValueTask<int>>:local = async () => 42
                let Func<int, Task<int>>:factory = Identity
                let Task<int>:pending = factory(42)
                return await pending
            """, 8, "'Identity' is a function, not a variable. Did you mean to call it?");
    }

    private static void AssertExpressionCount(string source, int expectedCount, string? expectedBinderError = null)
    {
        var compilation = Program.Compile(source, "lambda-accounting.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics.Errors));
        Assert.NotNull(compilation.Ast);
        var diagnostics = new Compiler.Diagnostics.DiagnosticBag();
        var binder = new Binder(diagnostics);
        binder.Bind(compilation.Ast);
        if (expectedBinderError == null)
            Assert.Empty(diagnostics.Errors);
        else
        {
            var diagnostic = Assert.Single(diagnostics.Errors);
            Assert.Equal(Compiler.Diagnostics.DiagnosticCode.TypeMismatch, diagnostic.Code);
            Assert.Equal(expectedBinderError, diagnostic.Message);
        }
        Assert.Equal(expectedCount, binder.ExpressionsBound);
    }

    private static IReadOnlyList<Type> ConcreteExpressionTypes() =>
        typeof(ExpressionNode).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(ExpressionNode).IsAssignableFrom(t))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void DispatchTable_CoversEveryConcreteExpressionNode_Exactly()
    {
        var concrete = ConcreteExpressionTypes();
        var dispatched = Binder.ExpressionDispatch.Keys.ToHashSet();

        var missing = concrete.Where(t => !dispatched.Contains(t)).Select(t => t.Name).ToList();
        var stale = dispatched.Except(concrete).Select(t => t.Name).ToList();

        Assert.True(missing.Count == 0,
            $"Concrete ExpressionNode classes with no dispatch entry: {string.Join(", ", missing)}");
        Assert.True(stale.Count == 0,
            $"Dispatch entries for types that are not concrete ExpressionNodes: {string.Join(", ", stale)}");
    }

    [Fact]
    public void ConcreteExpressionNodeCount_MatchesTheF1Freeze()
    {
        // F-1 (v0.13-freeze-registrations.md) froze the denominator at 61 classes,
        // exhaustively tiered. A count change here without an F-1 amendment is exactly
        // the silent-denominator-drift the registration's bidirectional rule forbids.
        // B8 amendment (#762 item 8, subtractive-with-supersession recorded in F-1):
        // KeywordArgNode reclassified out of the AST entirely — it is a parser-internal
        // value object that can no longer TYPE as an expression, so it leaves the
        // denominator by becoming unrepresentable, not by being silently skipped.
        // #1617 removes Some/None/Ok/Err expression classes from the current language.
        // Native checked/unchecked expressions add one explicit lexical-scope class in 0.30.
        // The historical registration stays unchanged; exact surviving dispatch is checked above.
        Assert.Equal(57, ConcreteExpressionTypes().Count);
    }
}
