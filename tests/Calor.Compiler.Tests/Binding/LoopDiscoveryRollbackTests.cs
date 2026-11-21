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
/// the same snapshot-local symbol ids, in the same order, without leaked discovery symbols.
/// </summary>
public sealed class LoopDiscoveryRollbackTests
{
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

    private static string Snapshot(BoundModule module)
    {
        var id = Assert.Single(module.SymbolsById, entry => entry.Value.Name == "total").Key.Value;
        var snapshot = id.Split("/snapshot/", StringSplitOptions.None)[1].Split('/')[0];
        Assert.Matches("^[A-F0-9]{64}$", snapshot);
        return snapshot;
    }

    private static string Local(string owner, string snapshot, string kind, string name, int start) =>
        owner + "/snapshot/" + snapshot + "/" + kind + "/" + Uri.EscapeDataString("name:" +
            System.Text.Json.JsonSerializer.Serialize(new { name, typeName = "int", Start = start }));

    [Fact]
    public void NestedLoops_DoNotLeakDiscoveryDeclarationIds()
    {
        const string source = """
            module Nested Nested block
              func Run (int:n) → int
                !
                var int:total = 0
                for i from 0 to n step 1
                  let int:a = i
                  for j from 0 to n step 1
                    let int:a2 = (a + j)
                    set total (total + a2)
                for k from 0 to n step 1
                  let int:a = k
                  set total (total + a)
                return total
            """;
        var bound = Bind(source);

        // Distinct source offsets distinguish the two locals named `a`.
        // Discovery must not register extra instances or duplicate suffixes.
        var run = Assert.Single(bound.Functions).Symbol.Id.Value;
        var snapshot = Snapshot(bound);
        string[] expected =
        [
            run + "/parameter/name%3An",
            run,
            // 0.30 binds the postcondition `result` pseudo-variable for every function.
            run + "/snapshot/" + snapshot + "/contract-result/" + Uri.EscapeDataString("name:" +
                System.Text.Json.JsonSerializer.Serialize(new { name = "result", typeName = "int", Start = 0 })),
            Local(run, snapshot, "local", "total", source.IndexOf("total", StringComparison.Ordinal)),
            Local(run, snapshot, "for", "i", source.IndexOf("i from", StringComparison.Ordinal)),
            Local(run, snapshot, "local", "a", source.IndexOf("a = i", StringComparison.Ordinal)),
            Local(run, snapshot, "for", "j", source.IndexOf("j from", StringComparison.Ordinal)),
            Local(run, snapshot, "local", "a2", source.IndexOf("a2", StringComparison.Ordinal)),
            Local(run, snapshot, "for", "k", source.IndexOf("k from", StringComparison.Ordinal)),
            Local(run, snapshot, "local", "a", source.IndexOf("a = k", StringComparison.Ordinal)),
        ];
        Assert.True(expected.SequenceEqual(bound.SymbolsById.Keys.Select(id => id.Value)),
            string.Join("\n", bound.SymbolsById.Keys.Select(id => id.Value)));
    }
}
