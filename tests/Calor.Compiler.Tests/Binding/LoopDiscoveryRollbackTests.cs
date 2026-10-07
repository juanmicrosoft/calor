using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Loop callable discovery binds each loop body once without committing it,
/// then rolls back the symbols and declaration-id counters it created. The
/// rollback used to copy both module-wide maps at every loop, which made
/// binding quadratic in module size (Binding_MediumModule_Under500ms). It now
/// undoes only what discovery added. These tests pin the observable result:
/// the same symbol ids, in the same order, with no "duplicate:" suffixes.
/// </summary>
public sealed class LoopDiscoveryRollbackTests
{
    private const string Prefix = "calor://source/%3Cmemory%3E/module/m1/";

    private static BoundModule Bind(string source)
    {
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        var module = new Parser(tokens, diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        var bound = new Binder(diagnostics).Bind(module);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        return bound;
    }

    [Fact]
    public void SymbolsById_KeepsDeclarationOrderAcrossLoopDiscovery()
    {
        var bound = Bind("""
            §M{m1:Order}
              §F{f1:Run:pub} (i32:n) -> i32
                §B{~total:i32} 0
                §L{l1:i:0:n:1}
                  §B{step:i32} (+ i 1)
                  §B{dup:i32} step
                  §ASSIGN total (+ total step)
                §WH{w1} (< total 10)
                  §B{inner:i32} 1
                  §ASSIGN total (+ total inner)
                §B{after:i32} total
                §R after
              §F{f2:Other:pub} () -> i32
                §B{x:i32} 1
                §R x
            """);

        // Recorded from the binder before the rollback change (full-copy restore).
        string[] expected =
        [
            "function/ast%3Af1/parameter/name%3An",
            "function/ast%3Af1",
            "function/ast%3Af2",
            "function/ast%3Af1/local/name%3Atotal",
            "function/ast%3Af1/for/name%3Ai",
            "function/ast%3Af1/local/name%3Astep",
            "function/ast%3Af1/local/name%3Adup",
            "function/ast%3Af1/local/name%3Ainner",
            "function/ast%3Af1/local/name%3Aafter",
            "function/ast%3Af2/local/name%3Ax",
        ];
        Assert.Equal(
            expected.Select(suffix => Prefix + suffix),
            bound.SymbolsById.Keys.Select(id => id.Value));
    }

    [Fact]
    public void NestedLoops_DoNotLeakDiscoveryDeclarationIds()
    {
        var bound = Bind("""
            §M{m1:Nested}
              §F{f1:Run:pub} (i32:n) -> i32
                §B{~total:i32} 0
                §L{l1:i:0:n:1}
                  §B{a:i32} i
                  §L{l2:j:0:n:1}
                    §B{a2:i32} (+ a j)
                    §ASSIGN total (+ total a2)
                §L{l3:k:0:n:1}
                  §B{a:i32} k
                  §ASSIGN total (+ total a)
                §R total
            """);

        // Recorded from the binder before the rollback change. The second block
        // local `a` legitimately takes duplicate ordinal 1; discovery of the
        // three loop bodies must not consume any further ordinals.
        string[] expected =
        [
            "function/ast%3Af1/parameter/name%3An",
            "function/ast%3Af1",
            "function/ast%3Af1/local/name%3Atotal",
            "function/ast%3Af1/for/name%3Ai",
            "function/ast%3Af1/local/name%3Aa",
            "function/ast%3Af1/for/name%3Aj",
            "function/ast%3Af1/local/name%3Aa2",
            "function/ast%3Af1/for/name%3Ak",
            "function/ast%3Af1/local/name%3Aa/duplicate%3A1",
        ];
        Assert.Equal(
            expected.Select(suffix => Prefix + suffix),
            bound.SymbolsById.Keys.Select(id => id.Value));
    }
}
