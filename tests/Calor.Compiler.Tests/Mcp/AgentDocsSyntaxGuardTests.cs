using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Calor.Compiler;
using Calor.Compiler.Analysis;
using Calor.Compiler.Effects;
using Xunit;

namespace Calor.Compiler.Tests.Mcp;

/// <summary>
/// Guard for EVERY agent-readable documentation surface (v0.6.7 plan, Item 0).
/// Calor is indent-only: structural closing tags (§/F, §/M, §/L, …) hard-error at
/// compile time (Calor0830), and the old 4-field §F/§AF header and §B{name}=value
/// binding form are obsolete. Agent docs that teach any of these make an agent that
/// follows Calor's own documentation write non-compiling Calor.
///
/// This test scans the positive-teaching text of each agent-doc surface for the
/// forbidden forms and compiles every complete §M module it can find. The forbidden
/// structural-closer set is the compiler's own <see cref="LegacyCloserFormLint"/>
/// scanner (which mirrors the parser's StructuralLegacyClosers hard-error set), so the
/// guard can never drift from the language.
///
/// Negative examples (JSON <c>wrongExample</c> fields, the MCP primer's "Common
/// mistakes" section) intentionally contain closer-form and are excluded: JSON surfaces
/// scan only designated correct-syntax fields, and the primer is guarded separately by
/// <see cref="PrimerCompilesTests"/> / PrimerMistakesRejectedTests.
/// </summary>
public class AgentDocsSyntaxGuardTests
{
    private static readonly Regex FourFieldHeader =
        new(@"§A?F\{[^}]*:[^}]*:[^}]*:[^}]*\}", RegexOptions.Compiled);

    private static readonly Regex BindEquals =
        new(@"§B\{[^}]*\}\s*=", RegexOptions.Compiled);

    // A real module header is the marker alone on its line. Cheat-sheet / reference
    // blocks write "§M{id:Name}   <prose describing the tag>", which must NOT be
    // treated as compilable code.
    private static readonly Regex ModuleHeaderLine =
        new(@"^§M\{[^}]*\}\s*$", RegexOptions.Compiled);

    // JSON property names whose string values are CORRECT Calor examples that agents are
    // taught to imitate. Everything else (csharp, wrongExample, description, prose, …) is
    // skipped so illustrative "don't do this" snippets are not flagged.
    private static readonly HashSet<string> CorrectCalorJsonFields = new(StringComparer.Ordinal)
    {
        "calorSyntax", "calor", "syntax", "correctExample",
    };

    // ----------------------------------------------------------------------------------
    // Surface discovery
    // ----------------------------------------------------------------------------------

    private sealed record Surface(string Name, string PositiveText);

    private static readonly Lazy<IReadOnlyList<Surface>> Surfaces = new(LoadSurfaces);

    private static IReadOnlyList<Surface> LoadSurfaces()
    {
        var root = FindRepoRoot()
            ?? throw new InvalidOperationException("Could not locate repository root from test assembly location.");

        var list = new List<Surface>();

        // Whole-file positive-teaching surfaces (markdown / templates).
        foreach (var rel in new[]
        {
            @"src\Calor.Compiler\Resources\Templates\copilot-instructions.md.template",
            @"src\Calor.Compiler\Resources\Templates\AGENTS.md.template",
            @"src\Calor.Compiler\Resources\Templates\CLAUDE.md.template",
            @"src\Calor.Compiler\Resources\Templates\GEMINI.md.template",
            @"src\Calor.Compiler\README.nuget.md",
            @"tests\Calor.Evaluation\Skills\calor-language-skills.md",
        })
        {
            var path = Path.Combine(root, ToPlatformRelative(rel));
            list.Add(new Surface(rel, File.ReadAllText(path)));
        }

        // JSON surfaces: only the correct-Calor fields are positive teaching.
        foreach (var rel in new[]
        {
            @"src\Calor.Compiler\Resources\calor-syntax-documentation.json",
            @"src\Calor.Compiler\Resources\error-suggestions.json",
        })
        {
            var path = Path.Combine(root, ToPlatformRelative(rel));
            var correct = CollectCorrectCalorStrings(File.ReadAllText(path));
            list.Add(new Surface(rel, string.Join("\n\n", correct)));
        }

        return list;
    }

    // The surface paths above are written with Windows-style '\' separators for
    // readability; normalize them to the running platform's separator so the
    // guard reads the files on Linux/macOS CI as well as on Windows.
    private static string ToPlatformRelative(string rel) =>
        rel.Replace('\\', Path.DirectorySeparatorChar)
           .Replace('/', Path.DirectorySeparatorChar);

    private static List<string> CollectCorrectCalorStrings(string json)
    {
        var result = new List<string>();
        using var doc = JsonDocument.Parse(json);
        Walk(doc.RootElement, propertyName: null);
        return result;

        void Walk(JsonElement el, string? propertyName)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var prop in el.EnumerateObject())
                    {
                        Walk(prop.Value, prop.Name);
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (var item in el.EnumerateArray())
                    {
                        Walk(item, propertyName);
                    }
                    break;
                case JsonValueKind.String:
                    if (propertyName != null && CorrectCalorJsonFields.Contains(propertyName))
                    {
                        result.Add(el.GetString()!);
                    }
                    break;
            }
        }
    }

    // ----------------------------------------------------------------------------------
    // Structural / obsolete-form scans
    // ----------------------------------------------------------------------------------

    public static IEnumerable<object[]> AllSurfaces()
        => Surfaces.Value.Select(s => new object[] { s.Name, s.PositiveText });

    [Theory]
    [MemberData(nameof(AllSurfaces))]
    public void Surface_TeachesNoStructuralClosers(string name, string positiveText)
    {
        var findings = LegacyCloserFormLint.Scan(positiveText, name);
        Assert.True(
            findings.Count == 0,
            $"Agent-doc surface '{name}' teaches {findings.Count} legacy structural closer(s) that " +
            $"hard-error (Calor0830). Calor is indent-only; delete the closer(s):\n" +
            string.Join("\n", findings.Select(f => $"  §/{f.Keyword} at L{f.Line}:{f.Column}")));
    }

    [Theory]
    [MemberData(nameof(AllSurfaces))]
    public void Surface_TeachesNoFourFieldFunctionHeaders(string name, string positiveText)
    {
        var matches = FourFieldHeader.Matches(positiveText);
        Assert.True(
            matches.Count == 0,
            $"Agent-doc surface '{name}' teaches {matches.Count} obsolete 4-field §F/§AF header(s) " +
            $"(§F{{id:name:returnType:vis}}). Use the 3-field header + signature: " +
            $"§F{{id:name:vis}} (type:name) -> retType. Offenders:\n" +
            string.Join("\n", matches.Select(m => "  " + m.Value)));
    }

    [Theory]
    [MemberData(nameof(AllSurfaces))]
    public void Surface_TeachesNoBindEqualsForm(string name, string positiveText)
    {
        var matches = BindEquals.Matches(positiveText);
        Assert.True(
            matches.Count == 0,
            $"Agent-doc surface '{name}' teaches {matches.Count} obsolete §B{{name}}=value binding(s). " +
            $"Use §B{{name}} expr (no '='). Offenders:\n" +
            string.Join("\n", matches.Select(m => "  " + m.Value)));
    }

    [Fact]
    public void Guard_CoversEveryKnownSurface()
    {
        // Drift guard: if a surface is removed/renamed this fails loudly rather than
        // silently scanning fewer files. 6 markdown/template + 2 JSON surfaces.
        Assert.Equal(8, Surfaces.Value.Count);
    }

    // ----------------------------------------------------------------------------------
    // Complete-module compile check
    // ----------------------------------------------------------------------------------

    public static IEnumerable<object[]> CompleteModules()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var surface in Surfaces.Value)
        {
            foreach (var module in ExtractCompleteModules(surface.PositiveText))
            {
                if (seen.Add(module))
                {
                    var header = module.Split('\n')[0].Trim();
                    yield return new object[] { surface.Name, header, module };
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(CompleteModules))]
    public void CompleteModule_Compiles(string surface, string header, string source)
    {
        // Illustrative doc snippets may call external methods and omit effect
        // declarations for brevity; disable unknown-call and effect enforcement so this
        // check fails only on real parse/bind/emit breakage (closer-form is a parse
        // error regardless of effect policy) — the syntax correctness the sweep is about.
        var options = new CompilationOptions
        {
            ContractMode = ContractMode.Debug,
            UnknownCallPolicy = UnknownCallPolicy.Warn,
            EnforceEffects = false,
            StrictEffects = false,
            VerifyContracts = false,
        };

        var result = Program.Compile(source, "agent-doc-example.calr", options);
        var errors = result.Diagnostics.Where(d => d.IsError).ToList();

        Assert.True(
            errors.Count == 0,
            $"Agent-doc module '{header}' from '{surface}' must compile but produced {errors.Count} error(s):\n" +
            string.Join("\n", errors.Select(e => $"  [{e.Code}] L{e.Span.Line}:{e.Span.Column} {e.Message}")) +
            $"\n--- source ---\n{source}");
    }

    /// <summary>
    /// Extracts every complete module from arbitrary text: a module begins at a
    /// column-0 <c>§M{</c> line and includes every following blank or indented line,
    /// ending at the next column-0 non-blank line. Fenced markdown code blocks keep the
    /// module at column 0, so this works for both markdown and raw JSON-field snippets.
    /// </summary>
    private static IEnumerable<string> ExtractCompleteModules(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var i = 0;
        while (i < lines.Length)
        {
            if (!ModuleHeaderLine.IsMatch(lines[i]))
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

            yield return string.Join("\n", lines[start..end]) + "\n";
        }
    }

    // ----------------------------------------------------------------------------------
    // Repo-root discovery (mirrors BindCorpusCleanTests)
    // ----------------------------------------------------------------------------------

    private static string? FindRepoRoot()
    {
        var assemblyDir = Path.GetDirectoryName(typeof(AgentDocsSyntaxGuardTests).Assembly.Location);
        var dir = assemblyDir != null ? new DirectoryInfo(assemblyDir) : null;
        while (dir != null)
        {
            var sln = Path.Combine(dir.FullName, "Calor.sln");
            var templates = Path.Combine(dir.FullName, "src", "Calor.Compiler", "Resources", "Templates");
            if (File.Exists(sln) && Directory.Exists(templates))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        return null;
    }
}
