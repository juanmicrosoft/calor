using System.Collections.Generic;
using System.Linq;
using Calor.Compiler;
using Calor.Compiler.Effects;
using Calor.Compiler.Mcp;
using Xunit;

namespace Calor.Compiler.Tests.Mcp;

/// <summary>
/// Semantic guard for the <c>calor://primer</c> MCP resource (v0.7 plan, item D1/D2):
/// every complete <c>§M{...}</c> module taught in the primer must actually compile under
/// the same options the <c>calor_compile</c> MCP tool uses by default. This is the test
/// that would have caught the closer-form / §RESULT "lies" no string assertion could.
/// </summary>
public class PrimerCompilesTests
{
    /// <summary>
    /// Extracts each complete module from the primer. A module begins at a column-0
    /// <c>§M{</c> line and includes every following blank or indented line; it ends at
    /// the next column-0 non-blank line (a section header or the next module). The
    /// "Common mistakes" snippets are intentionally indented under a column-0 header
    /// (not a <c>§M{</c>), so they are never collected.
    /// </summary>
    public static IEnumerable<object[]> PrimerModules()
    {
        var lines = McpResourceValidator.GetPrimer().Replace("\r\n", "\n").Split('\n');
        var i = 0;
        while (i < lines.Length)
        {
            if (!lines[i].StartsWith("§M{", System.StringComparison.Ordinal))
            {
                i++;
                continue;
            }

            var start = i;
            i++;
            while (i < lines.Length && (lines[i].Length == 0 || char.IsWhiteSpace(lines[i][0])))
            {
                i++;
            }

            var end = i;
            while (end > start + 1 && lines[end - 1].Trim().Length == 0)
            {
                end--;
            }

            var module = string.Join("\n", lines[start..end]) + "\n";
            var name = lines[start].Trim();
            yield return new object[] { name, module };
        }
    }

    [Theory]
    [MemberData(nameof(PrimerModules))]
    public void PrimerModule_Compiles(string name, string source)
    {
        // Mirror the calor_compile MCP tool's default options (CompileTool.cs):
        // EnforceEffects defaults to true; effectMode "default" => Strict policy, non-strict.
        var options = new CompilationOptions
        {
            ContractMode = ContractMode.Debug,
            UnknownCallPolicy = UnknownCallPolicy.Strict,
            StrictEffects = false,
            VerifyContracts = false,
        };

        var result = Program.Compile(source, "primer-example.calr", options);

        var errors = result.Diagnostics.Where(d => d.IsError).ToList();
        Assert.True(
            errors.Count == 0,
            $"Primer module '{name}' must compile but produced {errors.Count} error(s):\n" +
            string.Join("\n", errors.Select(e => $"  [{e.Code}] L{e.Span.Line}:{e.Span.Column} {e.Message}")) +
            $"\n--- source ---\n{source}");
    }

    [Fact]
    public void Primer_ExposesEveryTaughtModule()
    {
        // Guards against a silently-empty extractor: the primer teaches one complete
        // module per construct (Basics, Effects, Contracts, Branching, Loops, Bindings, Classes).
        Assert.Equal(7, PrimerModules().Count());
    }
}
