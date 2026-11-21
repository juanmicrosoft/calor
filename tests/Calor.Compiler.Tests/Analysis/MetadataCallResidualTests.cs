using Calor.Compiler.Analysis;
using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Indexing;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests.Analysis;

/// <summary>
/// 0.30 (query <c>effects:Log</c> answered partially): a native member call that the
/// binder resolves to a metadata (BCL) member, such as <c>Console.WriteLine(s)</c>, is
/// attributed to metadata. It is no edge to a project declaration, so it must not land
/// in the unresolved residual and mark its caller's callees, effects and impact answers
/// partial. Every call whose target could still be a project declaration stays there:
/// a delegate or computed-value invocation, an unresolved receiver, and an overridable
/// member reached through a receiver a project type could derive from or implement.
/// </summary>
public sealed class MetadataCallResidualTests : IDisposable
{
    private readonly List<string> _dirs = [];

    public void Dispose()
    {
        foreach (var dir in _dirs)
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    private const string Parameters =
        "string:s, object:o, int:i, System.Collections.Generic.List<int>:xs, "
        + "System.Text.StringBuilder:sb, System.Collections.Generic.IComparer<int>:cmp, Func<int, int>:f";

    private static string Source(string call) => $$"""
        module Test Test
          func Pick public () → Func<int, int>
            !
            return (int:x) => x
          func Use public ({{Parameters}}) → void
            ! cw, mut
            var r = {{call}}
        """;

    [Theory]
    [InlineData("Console.WriteLine(s)")]           // static
    [InlineData("System.Math.Max(1, 2)")]          // static, written qualified
    [InlineData("s.ToUpper()")]                    // non-virtual instance
    [InlineData("xs.Add(1)")]                      // non-virtual on a non-sealed type
    [InlineData("o.GetType()")]                     // non-virtual, receiver object
    [InlineData("sb.ToString()")]                  // override, receiver type sealed
    [InlineData("i.ToString()")]                   // override, receiver a value type
    [InlineData("System.Linq.Enumerable.Count(xs)")]
    public void MetadataMemberCall_IsNotAnUnresolvedCall(string call)
    {
        var (bound, ast, site) = BindUse(call);

        var resolved = CallGraphAnalysis.BuildResolved(bound);
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        Assert.True(CallGraphAnalysis.IsProvablyMetadataCall(site));
        Assert.Empty(resolved.ForwardGraph[use.SymbolId]);
        Assert.DoesNotContain(resolved.UnresolvedCalls, unresolved => unresolved.Caller == use.SymbolId);

        var astUse = ast.Functions.Single(function => function.Name == "Use");
        Assert.DoesNotContain(CallGraphAnalysis.Build(ast).UnresolvedCalls,
            unresolved => unresolved.CallerId == astUse.Id);
    }

    [Theory]
    [InlineData("o.ToString()", "<expression-call>")]        // virtual: a project type may override it
    [InlineData("o.GetHashCode()", "<expression-call>")]
    [InlineData("cmp.Compare(1, 2)", "<expression-call>")]   // interface: a project type may implement it
    [InlineData("f.Invoke(1)", "<expression-call>")]         // delegate member: calls whatever f holds
    [InlineData("f(1)", "f")]                                // delegate value invocation
    public void CallThatMayReachProjectCode_StaysUnresolved(string call, string target)
    {
        var (bound, ast, site) = BindUse(call);

        var resolved = CallGraphAnalysis.BuildResolved(bound);
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        Assert.False(CallGraphAnalysis.IsProvablyMetadataCall(site));
        var unresolved = Assert.Single(resolved.UnresolvedCalls, entry => entry.Caller == use.SymbolId);
        Assert.Equal(target, unresolved.Target);
        Assert.Equal(site.Span, unresolved.Span);

        var astUse = ast.Functions.Single(function => function.Name == "Use");
        Assert.Contains(CallGraphAnalysis.Build(ast).UnresolvedCalls,
            entry => entry.CallerId == astUse.Id && entry.Target == target && entry.Span == site.Span);
    }

    [Fact]
    public void ComputedValueInvocation_StaysUnresolved()
    {
        var diagnostics = new DiagnosticBag();
        var ast = Parse(Source("Pick()(1)"), diagnostics);
        var bound = new Binder(diagnostics).Bind(ast);
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var valueCall = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundExpressionCall>());

        var resolved = CallGraphAnalysis.BuildResolved(bound);
        var unresolved = Assert.Single(resolved.UnresolvedCalls, entry => entry.Caller == use.SymbolId);
        Assert.Equal("<expression-call>", unresolved.Target);
        Assert.Equal(valueCall.Span, unresolved.Span);
        // Pick() itself is a resolved edge.
        Assert.Single(resolved.ForwardGraph[use.SymbolId]);
    }

    [Fact]
    public void UnresolvedReceiver_StaysUnresolved()
    {
        var diagnostics = new DiagnosticBag();
        var ast = Parse(Source("zz.Foo()"), diagnostics);
        var bound = new Binder(diagnostics).Bind(ast);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UndefinedReference);
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundCallExpression>());

        Assert.False(CallGraphAnalysis.IsProvablyMetadataCall(call));
        var unresolved = Assert.Single(CallGraphAnalysis.BuildResolved(bound).UnresolvedCalls,
            entry => entry.Caller == use.SymbolId);
        Assert.Equal("<expression-call>", unresolved.Target);
    }

    /// <summary>
    /// The CI failure itself, end to end through the project index: Log's only call
    /// is <c>Console.WriteLine(s)</c>, so its callees and effects answers are complete.
    /// </summary>
    [Fact]
    public void ProjectIndex_MetadataCallDoesNotMarkCallerPartial()
    {
        var dir = NewProject(("app.calr", """
            module App App block
              func Log (string:s) → void
                ! cw
                Console.WriteLine(s)

              func Upper (string:s) → string
                !
                return s.ToUpper()
            """));
        var index = ProjectIndexBuilder.Build(OptionsFor(dir));

        foreach (var name in new[] { "Log", "Upper" })
        {
            var declaration = Assert.Single(index.Declarations, item => item.Name == name);
            Assert.DoesNotContain(index.Residual.UnresolvedCalls,
                entry => entry.CallerSymbolId == declaration.SymbolId);
            Assert.False(index.CalleesAnswerIsPartial(declaration.SymbolId), name);
            Assert.False(index.EffectsAnswerIsPartial(declaration.SymbolId, declaration.File), name);
        }
        Assert.True(index.Residual.IsEmpty);
        Assert.False(index.ImpactAnswerIsPartial());
    }

    /// <summary>
    /// The other side: a call whose target the index cannot attribute keeps its
    /// caller's answers partial — a virtual member a project type could override, a
    /// computed-value invocation (newly recorded by the index), and a receiver that
    /// did not bind.
    /// </summary>
    [Fact]
    public void ProjectIndex_UnresolvableExpressionCallStillMarksCallerPartial()
    {
        var dir = NewProject(
            ("app.calr", """
                module App App block
                  func Pick () → Func<int, int>
                    !
                    return (int:x) => x

                  func Describe (object:o) → string
                    !
                    return o.ToString()

                  func Apply () → int
                    !
                    return Pick()(1)
                """),
            ("broken.calr", """
                module Broken Broken block
                  func Unknown () → int
                    !
                    return zz.Foo()
                """));
        var index = ProjectIndexBuilder.Build(OptionsFor(dir));

        foreach (var name in new[] { "Describe", "Apply", "Unknown" })
        {
            var declaration = Assert.Single(index.Declarations, item => item.Name == name);
            var entry = Assert.Single(index.Residual.UnresolvedCalls,
                call => call.CallerSymbolId == declaration.SymbolId);
            Assert.Equal("<expression-call>", entry.Target);
            Assert.True(index.CalleesAnswerIsPartial(declaration.SymbolId), name);
            Assert.True(index.EffectsAnswerIsPartial(declaration.SymbolId, declaration.File), name);
        }
        Assert.True(index.ImpactAnswerIsPartial());
    }

    private static (BoundModule Bound, ModuleNode Ast, BoundCallExpression Site) BindUse(string call)
    {
        var diagnostics = new DiagnosticBag();
        var ast = Parse(Source(call), diagnostics);
        var bound = new Binder(diagnostics).Bind(ast);
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var site = BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundCallExpression>()
            .OrderByDescending(candidate => candidate.Span.Length)
            .First();
        return (bound, ast, site);
    }

    private static ModuleNode Parse(string source, DiagnosticBag diagnostics)
    {
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        var module = new Parser(tokens, diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        return module;
    }

    private string NewProject(params (string Name, string Source)[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "calor-metadata-call-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(dir);
        _dirs.Add(dir);
        foreach (var (name, source) in files)
            File.WriteAllText(Path.Combine(dir, name), source);
        return dir;
    }

    private static ProjectIndexBuilder.Options OptionsFor(string dir) =>
        new(dir, "t", ProjectIndexBuilder.DiscoverSources(dir));
}
