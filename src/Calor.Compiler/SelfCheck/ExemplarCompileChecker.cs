using System.Text.RegularExpressions;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
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
/// <para><see cref="CheckAgentTaskReference"/> applies the same compilation check
/// to the generated reference copied into agent-task workspaces.</para>
/// </summary>
public static class ExemplarCompileChecker
{
    public const string TeachingSourcePath = "src/Calor.Compiler/Resources/Teaching/reference.json";
    public const string TeachingReferencePath = "src/Calor.Compiler/Resources/Teaching/Generated/current-language-reference.md";

    /// <summary>Renders checked-in teaching mirrors; runtime consumers read only packaged output.</summary>
    public static IReadOnlyDictionary<string, string> GenerateTeachingResources(string source)
    {
        var root = JsonNode.Parse(source)?.AsObject()
            ?? throw new InvalidDataException("Teaching source must be an object.");
        if (root["schemaVersion"]?.GetValue<int>() != 1)
            throw new InvalidDataException("Unsupported teaching schema version.");
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        string Text(JsonNode node, string key) => node[key]?.GetValue<string>()
            ?? throw new InvalidDataException($"Teaching field '{key}' is missing.");
        string Json(JsonNode node) => node.ToJsonString(options) + "\n";
        var constructs = root["constructs"]!.AsArray();
        var programs = root["programs"]!.AsArray().Select(p => p!).ToDictionary(p => Text(p, "id"), StringComparer.Ordinal);
        if (constructs.Count == 0 || programs.Count == 0)
            throw new InvalidDataException("Teaching source must contain constructs and complete examples.");
        var byId = constructs.Select(c => c!).ToDictionary(c => Text(c, "id"), StringComparer.Ordinal);
        var effectCodes = root["catalogs"]!["effects.json"]!["effects"]!.AsArray()
            .Select(effect => Text(effect!, "code")).ToArray();
        if (effectCodes.Distinct(StringComparer.Ordinal).Count() != effectCodes.Length
            || !effectCodes.ToHashSet(StringComparer.Ordinal).SetEquals(Effects.EffectCodes.DocumentedCompactCodes))
            throw new InvalidDataException("Teaching effects must match the current non-legacy effect registry.");
        foreach (var construct in byId.Values)
        {
            foreach (var key in new[] { "csharpConstruct", "calorSyntax", "description", "csharpEquivalent" })
                _ = Text(construct, key);
            if (construct["keywords"]!.AsArray().Count == 0)
                throw new InvalidDataException($"Construct '{Text(construct, "id")}' has no keywords.");
            if (construct["lookupOnly"]?.GetValue<bool>() != true && construct["programs"]!.AsArray().Count == 0)
                throw new InvalidDataException($"Construct '{Text(construct, "id")}' has no complete example.");
            foreach (var id in construct["programs"]!.AsArray())
                if (!programs.ContainsKey(id!.GetValue<string>()))
                    throw new InvalidDataException($"Unknown teaching program '{id}'.");
        }
        var lookupConstructs = constructs.DeepClone().AsArray();
        foreach (var construct in lookupConstructs)
        {
            construct!["examples"] = new JsonArray(construct["programs"]!.AsArray()
                .Select(id => (JsonNode)new JsonObject
                {
                    ["csharp"] = Text(construct, "csharpEquivalent"),
                    ["calor"] = Text(programs[id!.GetValue<string>()], "calor")
                }).ToArray());
        }

        var tags = root["tags"]!.DeepClone().AsObject();
        foreach (var (_, tag) in tags)
        {
            if (tag!["construct"] is not { } target)
                continue;
            var construct = byId[target.GetValue<string>()];
            tag["name"] ??= Text(construct, "csharpConstruct");
            tag["syntax"] = Text(construct, "calorSyntax");
            tag["description"] = Text(construct, "description");
            tag["csharpEquivalent"] = Text(construct, "csharpEquivalent");
            tag.AsObject().Remove("construct");
        }

        const string banner = "<!-- Generated from src/Calor.Compiler/Resources/Teaching/reference.json by calor self-check docs --fix. Do not edit. -->\n\n";
        var reference = new StringBuilder(banner);
        reference.Append(Text(root, "introduction")).Append('\n').Append(Text(root, "overview")).Append('\n');
        foreach (var construct in byId.Values)
        {
            reference.Append("## ").Append(Text(construct, "csharpConstruct")).Append("\n\n")
                .Append(Text(construct, "description")).Append("\n\n")
                .Append("Syntax: `").Append(Text(construct, "calorSyntax")).Append("`\n\n");
            if (Text(construct, "csharpEquivalent").Length > 0)
                reference.Append("C# equivalents (fragments):\n\n```csharp\n")
                    .Append(Text(construct, "csharpEquivalent")).Append("\n```\n\n");
            foreach (var id in construct["programs"]!.AsArray())
                reference.Append("Complete example: [").Append(Text(programs[id!.GetValue<string>()], "title"))
                    .Append("](#example-").Append(id.GetValue<string>()).Append(").\n\n");
        }
        foreach (var (id, program) in programs)
        {
            reference.Append("## Example ").Append(id).Append("\n\n")
                .Append(Text(program, "title")).Append("\n\n```calor\n")
                .Append(Text(program, "calor").TrimEnd()).Append("\n```\n\n");
        }

        var referenceText = reference.ToString().TrimEnd() + "\n";
        var primerSource = root["primer"]!;
        var primer = new StringBuilder(banner).Append(Text(primerSource, "introduction"))
            .Append("## Complete programs (the compiler's tests prove every one compiles)\n\n");
        foreach (var id in primerSource["programs"]!.AsArray())
            primer.Append("```calor\n").Append(Text(programs[id!.GetValue<string>()], "calor").TrimEnd()).Append("\n```\n\n");
        primer.Append(Text(primerSource, "mistakes"));
        var instructions = Text(root, "agentInstructions");
        var outputs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TeachingReferencePath] = referenceText,
            [RelativePath.Replace('\\', '/')] = primer.ToString(),
            ["src/Calor.Compiler/Resources/calor-syntax-documentation.json"] = Json(new JsonObject
            {
                ["version"] = root["version"]!.DeepClone(),
                ["constructs"] = lookupConstructs,
                ["tags"] = tags
            }),
            ["src/Calor.Compiler/Resources/Teaching/Generated/server-instructions.md"] = instructions,
            ["src/Calor.Compiler/Resources/Teaching/Generated/overview.md"] = Text(root, "overview"),
            ["docs/syntax-reference/current-language.md"] =
                "---\nlayout: default\ntitle: Current language reference\nparent: Syntax Reference\n"
                + "nav_order: 0\npermalink: /syntax-reference/current-language/\n---\n\n" + referenceText,
        };
        foreach (var template in new[] { "CLAUDE", "AGENTS", "GEMINI", "copilot-instructions" })
            outputs[$"src/Calor.Compiler/Resources/Templates/{template}.md.template"] =
                "<!-- BEGIN CalorC SECTION - DO NOT EDIT -->\n<!-- calor-template-version: 1 -->\n"
                + banner + "## Calor-First Project\n\n" + instructions
                + "\n*Generated by calor v{{VERSION}}*\n<!-- END CalorC SECTION -->\n";
        foreach (var (name, value) in root["catalogs"]!.AsObject())
            outputs["src/Calor.Compiler/Resources/Teaching/Generated/" + name] =
                name.EndsWith(".json", StringComparison.Ordinal) ? Json(value!) : value!.GetValue<string>();
        outputs["src/Calor.Compiler/Resources/Teaching/Generated/tags.json"] = Json(new JsonObject
        {
            ["tags"] = JsonSerializer.SerializeToNode(tags.Select(pair => new
            {
                open = pair.Value!["syntax"]!.GetValue<string>(),
                close = pair.Value["close"]?.GetValue<string>() ?? "(none)",
                tag = pair.Key,
                description = pair.Value["description"]!.GetValue<string>(),
                csharpEquivalent = pair.Value["csharpEquivalent"]!.GetValue<string>()
            }).ToArray(), options),
            ["closingTagRules"] = "Structural blocks end at dedent. Calls, block lambdas, do-while conditions and value literals use their documented delimiters."
        });
        // MDX prose needs escaping; code fences remain byte-identical to the agent reference.
        var mdx = new StringBuilder("---\ntitle: Current language reference\ndescription: Generated current Calor constructs and complete examples\n---\n\n");
        var inFence = false;
        foreach (var line in referenceText.Split('\n'))
        {
            if (line.StartsWith("```", StringComparison.Ordinal))
                inFence = !inFence;
            if (line.StartsWith("<!--", StringComparison.Ordinal))
                mdx.Append("{/* Generated from Teaching/reference.json by calor self-check docs --fix. */}");
            else
                mdx.Append(inFence || line.StartsWith("```", StringComparison.Ordinal) ? line :
                    line.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("{", "&#123;").Replace("}", "&#125;"));
            mdx.Append('\n');
        }
        outputs["website/content/syntax-reference/current-language.mdx"] = mdx.ToString().TrimEnd() + "\n";
        return outputs;
    }

    // A binding whose initializer is a call to an array-returning BCL API, e.g.
    // "§B{lines:string[]} File.ReadAllLines(path)". Captures the declared
    // binding type and the API so the type can be required to be the array form.
    // The recognized APIs come from the shared Analysis.ArrayReturningBcl table
    // (same source the language-level Calor0254 check uses), so the docs-level lint
    // and the compiler diagnostic cannot drift apart. This only guards the fragment
    // reference lines that cannot be compiled; the complete programs get the real
    // Roslyn compile, which catches array/collection mismatches for any API.
    private static readonly Regex ArrayReturningBinding = new(
        @"§B\{\s*~?\s*[A-Za-z_][A-Za-z0-9_]*\s*:\s*(?<type>[^}]+?)\s*\}\s*" +
        @"(?:§C\{\s*)?(?<api>" +
        string.Join("|", Analysis.ArrayReturningBcl.Methods.Keys.Select(Regex.Escape)) +
        @")\s*[}(]",
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
            if (type.StartsWith("[", StringComparison.Ordinal) || type.EndsWith("[]", StringComparison.Ordinal))
            {
                continue;
            }

            var api = match.Groups["api"].Value;
            diagnostics.Add(new Diagnostic(
                DiagnosticCode.DocDriftArrayBindingTrap, DiagnosticSeverity.Error,
                $"Exemplar binds {api} (returns an array) to type '{type}'; use the array form " +
                $"'T[]', not a generic collection — copying this into C# is a CS0029 type error " +
                $"(the E1a trap).",
                exemplar.Path, i + 1, match.Index + 1));
        }
    }

    /// <summary>
    /// Lifts every complete §M program out of the markdown: a <c>```calor</c>-tagged
    /// fenced block whose first non-blank content line starts with §M. The tag is
    /// what distinguishes a compilable program from the prose-annotated reference
    /// blocks (which use bare <c>```</c> fences and may open with a shape line like
    /// <c>§M{Name}   Module</c>) and the "common mistakes" block (deliberately
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

    public static readonly string AgentTaskReferencePath =
        TeachingReferencePath;

    private static readonly Regex ReferenceDeclaration = new(@"^§(?:M|F|AF|CL|IFACE|DEL)\{");

    public sealed record ReferenceProgram(string Source, int FirstContentLine, bool Wrapped);

    public static IReadOnlyList<ReferenceProgram> ExtractAgentTaskPrograms(string helperSource)
    {
        var lines = helperSource.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var programs = new List<ReferenceProgram>();
        string? language = null;
        var contentStart = 0;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (!line.StartsWith("```", StringComparison.Ordinal))
                continue;
            if (language == null)
            {
                language = line[3..].Trim();
                contentStart = index + 1;
                continue;
            }
            if (line != "```")
                throw new InvalidDataException($"Agent reference has an unclosed fence before line {index + 1}.");

            var first = contentStart;
            while (first < index && (string.IsNullOrWhiteSpace(lines[first])
                || lines[first].TrimStart().StartsWith("//", StringComparison.Ordinal)))
                first++;
            if (language == "calor-fragment" || first == index || !ReferenceDeclaration.IsMatch(lines[first].TrimStart()))
            {
                language = null;
                continue;
            }
            if (language is not ("" or "calor"))
                throw new InvalidDataException($"Complete Calor example at line {first + 1} has unexpected fence language '{language}'.");
            var body = string.Join("\n", lines[first..index]);
            var wrapped = !lines[first].TrimStart().StartsWith("§M{", StringComparison.Ordinal);
            if (wrapped)
                body = $"§M{{AgentReference{programs.Count}}}\n"
                    + string.Join("\n", body.Split('\n').Select(line => "  " + line));
            programs.Add(new(body, first + 1, wrapped));
            language = null;
        }
        if (language != null)
            throw new InvalidDataException($"Agent reference has an unterminated fence at line {contentStart}.");
        return programs;
    }

    public static List<Diagnostic> CheckAgentTaskReference(DocFile helper)
    {
        var diagnostics = new List<Diagnostic>();
        IReadOnlyList<ReferenceProgram> programs;
        try
        {
            programs = ExtractAgentTaskPrograms(helper.Content);
            if (programs.Count == 0)
                throw new InvalidDataException("Agent reference contains no complete examples.");
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(new(DiagnosticCode.DocDriftExampleCompileError, DiagnosticSeverity.Error,
                exception.Message, helper.Path, 1, 1));
            return diagnostics;
        }

        foreach (var program in programs)
        {
            var example = new DocFile(helper.Path, "```calor\n" + program.Source + "\n```\n");
            var findings = new List<Diagnostic>();
            CheckCompletePrograms(example, findings);
            foreach (var finding in findings)
                diagnostics.Add(new(finding.Code, finding.Severity, finding.Message,
                    helper.Path,
                    program.FirstContentLine + Math.Max(0, finding.Span.Line - (program.Wrapped ? 3 : 2)),
                    Math.Max(1, finding.Span.Column - (program.Wrapped ? 2 : 0))));
        }
        return diagnostics;
    }

    /// <summary>
    /// Compiles generated C# with Roslyn's semantic model and returns error
    /// messages (empty = compiles cleanly). Warnings are ignored. Delegates to
    /// the shared <see cref="CodeGen.GeneratedCSharpCompiler"/> infrastructure
    /// (TPA reference set + Calor.Runtime + the SDK's implicit global usings),
    /// so the self-check, migration, and test compiles cannot drift apart (#761).
    /// </summary>
    public static IReadOnlyList<string> RoslynErrors(string generatedCSharp)
    {
        return CodeGen.GeneratedCSharpCompiler.Validate(generatedCSharp)
            .CompilationErrors
            .Select(d => $"{d.Id}: {d.GetMessage()}")
            .ToList();
    }
}
