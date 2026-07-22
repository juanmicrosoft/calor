using System.Text.RegularExpressions;
using Calor.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Diagnostic = Calor.Compiler.Diagnostics.Diagnostic;
using DiagnosticSeverity = Calor.Compiler.Diagnostics.DiagnosticSeverity;
using RoslynSeverity = Microsoft.CodeAnalysis.DiagnosticSeverity;

namespace Calor.Compiler.SelfCheck;

/// <summary>
/// Deep, exemplar-specific drift checks for
/// <c>src/Calor.Compiler/Resources/agent-syntax-exemplar.md</c> — the single-sourced
/// idiom sheet served to agents as <c>calor://primer</c>.
///
/// <para>The exemplar promises "every complete §M program compiles (the compiler's
/// own tests prove it)". <see cref="CheckCompletePrograms"/> makes that literally
/// true: each complete program is compiled Calor→C# and the <em>generated C#</em>
/// is then compiled with Roslyn's full semantic model. That last step is the point
/// — Calor's own emitter happily produces <c>List&lt;string&gt; x = File.ReadAllLines(p);</c>
/// (the E1a trap: an array assigned to a list, CS0029) and only the C# compiler
/// rejects it. Parse-checking, and even Calor→C# emission, both pass it.</para>
///
/// <para><see cref="CheckArrayBindingTraps"/> guards that same array-vs-collection
/// trap on the copyable <em>fragment</em> reference lines (the "Calls" block, etc.),
/// which intermix prose and free identifiers and so cannot be compiled standalone.
/// Together they make "reintroducing the List&lt;str&gt; ReadAllLines bug fails
/// self-check" true wherever the idiom appears. Regression guard for #712.</para>
/// </summary>
public static class ExemplarCompileChecker
{
    // A binding whose initializer is a call to an array-returning BCL API, e.g.
    // "§B{lines:[str]} §C{File.ReadAllLines} §A path §/C". Captures the declared
    // binding type and the API so the type can be required to be the array form.
    // The recognized APIs come from the shared Analysis.ArrayReturningBcl table
    // (same source the language-level Calor0254 check uses), so the docs-level lint
    // and the compiler diagnostic cannot drift apart. This only guards the fragment
    // reference lines that cannot be compiled; the complete programs get the real
    // Roslyn compile, which catches array/collection mismatches for any API.
    private static readonly Regex ArrayReturningBinding = new(
        @"§B\{\s*~?\s*[A-Za-z_][A-Za-z0-9_]*\s*:\s*(?<type>[^}]+?)\s*\}\s*" +
        @"§C\{\s*(?<api>" +
        string.Join("|", Analysis.ArrayReturningBcl.Methods.Keys.Select(Regex.Escape)) +
        @")\s*\}",
        RegexOptions.Compiled);

    /// <summary>The exemplar's repository-relative path.</summary>
    public static readonly string RelativePath =
        System.IO.Path.Combine("src", "Calor.Compiler", "Resources", "agent-syntax-exemplar.md");

    /// <summary>A complete §M program lifted from a fenced block, with the document
    /// line of its first content line (for diagnostic anchoring) and whether the
    /// block was exempted by a preceding <c>drift:ignore</c> marker.</summary>
    public sealed record ExemplarProgram(string Source, int FirstContentLine, bool Suppressed);

    /// <summary>
    /// Runs every exemplar-specific check and returns findings (empty = clean).
    /// </summary>
    public static List<Diagnostic> Check(DocFile exemplar)
    {
        var diagnostics = new List<Diagnostic>();
        CheckCompletePrograms(exemplar, diagnostics);
        CheckArrayBindingTraps(exemplar, diagnostics);
        return diagnostics;
    }

    /// <summary>
    /// Compiles every complete §M program in the exemplar Calor→C→Roslyn and
    /// reports any error that the full C# compile surfaces (including semantic
    /// type errors the Calor pipeline itself does not reject).
    /// </summary>
    public static void CheckCompletePrograms(DocFile exemplar, List<Diagnostic> diagnostics)
    {
        foreach (var program in ExtractCompletePrograms(exemplar.Content))
        {
            // A drift:ignore marker on the line before the fence exempts the block
            // (same escape hatch the rest of self-check honors).
            if (program.Suppressed)
            {
                continue;
            }

            // Stage 1: Calor → C#. A failure here is a genuine Calor-level error.
            CompilationResult result;
            try
            {
                result = Program.Compile(program.Source, exemplar.Path);
            }
            catch (Exception ex)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCode.DocDriftExampleCompileError, DiagnosticSeverity.Error,
                    $"Exemplar program crashed the Calor compiler: {ex.Message}",
                    exemplar.Path, program.FirstContentLine, 1));
                continue;
            }

            if (result.Diagnostics.HasErrors)
            {
                foreach (var error in result.Diagnostics.Errors)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCode.DocDriftExampleCompileError, DiagnosticSeverity.Error,
                        $"Exemplar program does not compile (Calor): {error.Code}: {error.Message}",
                        exemplar.Path,
                        program.FirstContentLine + Math.Max(error.Span.Line - 1, 0),
                        Math.Max(error.Span.Column, 1)));
                }

                continue;
            }

            // Stage 2: the generated C# through Roslyn's semantic model. This is
            // the only layer that catches type errors like the List<str>/[str] trap.
            foreach (var message in RoslynErrors(result.GeneratedCode))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCode.DocDriftExampleCompileError, DiagnosticSeverity.Error,
                    $"Exemplar program emits C# that does not compile: {message}",
                    exemplar.Path, program.FirstContentLine, 1));
            }
        }
    }

    /// <summary>
    /// Lints the exemplar for the array-vs-collection trap: a binding fed by an
    /// array-returning BCL call must declare the array form (<c>[T]</c>), never a
    /// generic collection like <c>List&lt;T&gt;</c>. Catches the E1a bug even on
    /// the copyable fragment reference lines, which cannot be compiled standalone.
    /// </summary>
    public static void CheckArrayBindingTraps(DocFile exemplar, List<Diagnostic> diagnostics)
    {
        var lines = exemplar.Content.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var match = ArrayReturningBinding.Match(lines[i]);
            if (!match.Success)
            {
                continue;
            }

            // Honor the repo-wide drift:ignore escape (e.g. a deliberate "do NOT
            // write this" negative example in the Common mistakes section).
            if (i > 0 && lines[i - 1].Contains(DocDriftChecker.SuppressionMarker, StringComparison.Ordinal))
            {
                continue;
            }

            var type = match.Groups["type"].Value.Trim();
            if (type.StartsWith("[", StringComparison.Ordinal))
            {
                continue;
            }

            var api = match.Groups["api"].Value;
            diagnostics.Add(new Diagnostic(
                DiagnosticCode.DocDriftArrayBindingTrap, DiagnosticSeverity.Error,
                $"Exemplar binds {api} (returns an array) to type '{type}'; use the array form " +
                $"'[...]', not a generic collection — copying this into C# is a CS0029 type error " +
                $"(the E1a trap).",
                exemplar.Path, i + 1, match.Index + 1));
        }
    }

    /// <summary>
    /// Lifts every complete §M program out of the markdown: a <c>```calor</c>-tagged
    /// fenced block whose first non-blank content line starts with §M. The tag is
    /// what distinguishes a compilable program from the prose-annotated reference
    /// blocks (which use bare <c>```</c> fences and may open with a shape line like
    /// <c>§M{id:Name}   Module</c>) and the "common mistakes" block (deliberately
    /// non-compiling).
    /// </summary>
    public static IReadOnlyList<ExemplarProgram> ExtractCompletePrograms(string markdown)
    {
        var programs = new List<ExemplarProgram>();
        var lines = markdown.Split('\n');
        var insideFence = false;      // inside any fenced block
        List<string>? current = null; // non-null only while capturing a ```calor block
        var fenceContentStart = 0;
        var suppressed = false;       // drift:ignore on the line before the opening fence

        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimEnd('\r').TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                if (insideFence)
                {
                    // Closing fence. Finalize if we were capturing a calor block.
                    if (current != null)
                    {
                        var first = current.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
                        if (first != null && first.TrimStart().StartsWith("§M", StringComparison.Ordinal))
                        {
                            programs.Add(new ExemplarProgram(
                                string.Join("\n", current) + "\n", fenceContentStart, suppressed));
                        }

                        current = null;
                    }

                    insideFence = false;
                }
                else
                {
                    // Opening fence. Only ```calor blocks are captured.
                    insideFence = true;
                    if (trimmed[3..].Trim() == "calor")
                    {
                        current = [];
                        fenceContentStart = i + 2; // 1-based line of the first content line
                        suppressed = i > 0 &&
                            lines[i - 1].Contains(DocDriftChecker.SuppressionMarker, StringComparison.Ordinal);
                    }
                }

                continue;
            }

            current?.Add(lines[i].TrimEnd('\r'));
        }

        return programs;
    }

    // Built once — enumerating the trusted-platform-assembly set is not free and the
    // reference set is constant for the process lifetime. Lazy<T> makes the one-time
    // build thread-safe under xUnit's parallel test classes.
    private static readonly Lazy<IReadOnlyList<MetadataReference>> _references =
        new(BuildReferences);

    private static IReadOnlyList<MetadataReference> References() => _references.Value;

    private static IReadOnlyList<MetadataReference> BuildReferences()
    {
        var tpa = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "";
        var references = tpa
            .Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();

        // Belt-and-suspenders: the generated C# for contract-bearing programs
        // references Calor.Runtime; ensure it is present even if TPA omitted it.
        var runtime = typeof(Calor.Runtime.ContractKind).Assembly.Location;
        if (!string.IsNullOrEmpty(runtime) &&
            !references.Any(r => (r as PortableExecutableReference)?.FilePath == runtime))
        {
            references.Add(MetadataReference.CreateFromFile(runtime));
        }

        return references;
    }

    // The implicit global usings the Calor SDK provides to generated code — this
    // mirrors Microsoft.NET.Sdk's ImplicitUsings set as of .NET 10 (see
    // src/Calor.Sdk/obj/**/Calor.Sdk.GlobalUsings.g.cs, the ground truth). The
    // emitter relies on these — it emits `File.ReadAllLines(...)` with no
    // `using System.IO;` — so the generated C# only compiles standalone when they
    // are supplied here too. If a future SDK grows the set and a program starts
    // using a newly-implicit namespace, that program's Roslyn compile will fail
    // here (CS0246/CS0103) until this list is updated to match.
    private const string GlobalUsingsPreamble =
        "global using System;\n" +
        "global using System.Collections.Generic;\n" +
        "global using System.IO;\n" +
        "global using System.Linq;\n" +
        "global using System.Net.Http;\n" +
        "global using System.Threading;\n" +
        "global using System.Threading.Tasks;\n";

    /// <summary>
    /// Compiles generated C# with Roslyn's semantic model and returns error
    /// messages (empty = compiles cleanly). Warnings are ignored.
    /// </summary>
    public static IReadOnlyList<string> RoslynErrors(string generatedCSharp)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var trees = new[]
        {
            CSharpSyntaxTree.ParseText(GlobalUsingsPreamble, parseOptions),
            CSharpSyntaxTree.ParseText(generatedCSharp, parseOptions),
        };

        var compilation = CSharpCompilation.Create(
            "ExemplarCompileCheck",
            trees,
            References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return compilation.GetDiagnostics()
            .Where(d => d.Severity == RoslynSeverity.Error)
            .Select(d => $"{d.Id}: {d.GetMessage()}")
            .ToList();
    }
}
