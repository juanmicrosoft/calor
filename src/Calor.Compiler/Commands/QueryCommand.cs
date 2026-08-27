using System.CommandLine;
using System.CommandLine.Invocation;
using Calor.Compiler.Binding.BoundTypes;
using Calor.Compiler.Effects;
using Calor.Compiler.Indexing;

namespace Calor.Compiler.Commands;

/// <summary>
/// Asks the project index a question (§2.2; scoping doc §7 S2).
///
/// Two rules govern every answer here:
///
/// 1. A stale index never answers. The index is rebuilt on demand; with
///    <c>--no-build</c> the command refuses instead, which is what a timing run
///    needs so it measures a query rather than a build.
/// 2. The residual is printed WITH the answer. Calor binds one file at a time,
///    so a cross-file call resolves only when exactly one declaration bears the
///    name — these are "the callers we can name", and saying so is the
///    difference between a limit and a lie.
/// </summary>
public static class QueryCommand
{
    private const string OptionsToken = "index-v1";

    public static Command Create()
    {
        var command = new Command("query", "Ask the project index about your code")
        {
            CreateFacetCommand("symbol", "Where a name is declared"),
            CreateFacetCommand("callers", "What calls a declaration"),
            CreateFacetCommand("callees", "What a declaration calls"),
            CreateImpactCommand(),
            CreateFacetCommand("contracts", "Contracts declared on a declaration"),
            CreateFacetCommand("assumptions", "Assumptions in force for a declaration"),
            CreateFacetCommand(
                "effects",
                "Declared and inferred effect rows of a declaration, and the verdict between them",
                withJson: true),
        };
        return command;
    }

    /// <summary>
    /// v0.15 E5 — the payload <c>calor query effects --json</c> carries under
    /// the envelope's <c>data</c>. The rows are the index's own records, so
    /// the JSON and the text answer cannot disagree.
    /// </summary>
    private sealed record EffectsAnswer(
        string Subject,
        string SymbolId,
        IReadOnlyList<IndexedEffectRow> Rows,
        bool Partial);

    private static Command CreateImpactCommand()
    {
        var subjectArgument = new Argument<string>(
            name: "subject",
            description: "Declaration name to treat as changed (or a file with --file)");
        var pathOption = new Option<string>(
            aliases: ["--project"],
            description: "Project directory",
            getDefaultValue: () => ".");
        var inFileOption = new Option<string?>(
            aliases: ["--in-file"],
            description: "Disambiguate when several files declare the name");
        var fileOption = new Option<bool>(
            aliases: ["--file"],
            description: "Treat the subject as a whole changed FILE rather than one declaration");
        var noBuildOption = new Option<bool>(
            aliases: ["--no-build"],
            description: "Refuse a missing or stale index instead of rebuilding it");
        var effectsOption = new Option<bool>(
            aliases: ["--effects"],
            description: "Effect-row blast radius: which affected callers' declared rows the "
                + "subject's row would stop fitting");
        var rowOption = new Option<string?>(
            aliases: ["--row"],
            description: "With --effects: the row the subject would carry after the change, as "
                + "comma-separated effect codes (e.g. \"cw,fs:w\"; \"\" for pure). "
                + "Default: the subject's current declared row");

        var command = new Command("impact", "What a change could affect")
        {
            subjectArgument, pathOption, inFileOption, fileOption, noBuildOption,
            effectsOption, rowOption,
        };

        command.SetHandler((InvocationContext context) =>
        {
            context.ExitCode = ExecuteImpact(
                context.ParseResult.GetValueForArgument(subjectArgument),
                context.ParseResult.GetValueForOption(pathOption)!,
                context.ParseResult.GetValueForOption(inFileOption),
                context.ParseResult.GetValueForOption(fileOption),
                context.ParseResult.GetValueForOption(noBuildOption),
                context.ParseResult.GetValueForOption(effectsOption),
                context.ParseResult.GetValueForOption(rowOption));
        });

        return command;
    }

    private static int ExecuteImpact(
        string subject,
        string projectDirectory,
        string? inFile,
        bool wholeFile,
        bool noBuild,
        bool effects = false,
        string? row = null)
    {
        if (!Directory.Exists(projectDirectory))
        {
            Console.Error.WriteLine($"Error: directory not found: {projectDirectory}");
            return 1;
        }

        var index = Resolve(projectDirectory, noBuild, out var error);
        if (index == null)
        {
            Console.Error.WriteLine(error);
            return 1;
        }

        IReadOnlyList<IndexedDeclaration> affected;
        string described;
        if (wholeFile)
        {
            var normalized = subject.Replace('\\', '/');
            if (!index.Files.ContainsKey(normalized))
            {
                Console.Error.WriteLine($"Error: '{subject}' is not an indexed source file.");
                return 1;
            }
            affected = index.FindImpactOfFile(normalized);
            described = $"the whole file {normalized}";
        }
        else
        {
            var declarations = index.FindDeclarations(subject);
            if (declarations.Count == 0)
            {
                Console.Error.WriteLine(
                    $"Error: no declaration named '{subject}'. Use --file to ask about a file.");
                return 1;
            }

            IndexedDeclaration target;
            if (declarations.Count == 1)
            {
                target = declarations[0];
            }
            else
            {
                var matches = inFile == null
                    ? declarations
                    : declarations.Where(declaration => string.Equals(
                        declaration.File, inFile, StringComparison.Ordinal)).ToArray();
                if (matches.Count != 1)
                {
                    Console.Error.WriteLine(
                        $"Error: '{subject}' is declared in {declarations.Count} places; "
                            + "narrow it with --in-file:");
                    foreach (var declaration in declarations)
                        Console.Error.WriteLine($"  {Describe(declaration)}");
                    return 1;
                }
                target = matches[0];
            }

            if (effects)
                return ExecuteEffectImpact(index, target, row);

            affected = index.FindImpactOfDeclarations([target.SymbolId]);
            described = Describe(target);
        }

        if (effects)
        {
            Console.Error.WriteLine("Error: --effects asks about one declaration's row; it cannot be combined with --file.");
            return 1;
        }

        foreach (var declaration in affected.OrderBy(
                     declaration => $"{declaration.File}:{declaration.Line}",
                     StringComparer.Ordinal))
        {
            Console.WriteLine($"  {Describe(declaration)}");
        }

        var affectedFiles = affected
            .Select(declaration => declaration.File)
            .Distinct(StringComparer.Ordinal)
            .Count();
        Console.WriteLine(
            affected.Count == 0
                ? $"impact: nothing calls into {described}"
                : $"impact: {affected.Count} declaration(s) in {affectedFiles} file(s) "
                    + $"affected by a change to {described}");

        if (wholeFile)
        {
            Console.WriteLine(
                "impact: file-grained — a change to ANY declaration in this file "
                    + "implicates all of these. Ask about a declaration for a precise answer.");
        }

        ReportResidual(index, index.ImpactAnswerIsPartial());
        return 0;
    }

    /// <summary>
    /// v0.15 E5 (design-doc §8.6) — effect-change blast radius. The closure is
    /// <see cref="ProjectIndex.FindImpactOfDeclarations"/>'s, unchanged; the
    /// effects dimension is the verdict of fitting the row the subject WOULD
    /// carry into each affected caller's DECLARED row. A caller that stops
    /// fitting is where a Calor0410 would land after the change.
    /// </summary>
    private static int ExecuteEffectImpact(ProjectIndex index, IndexedDeclaration target, string? row)
    {
        var own = index.FindEffectRow(target.SymbolId);
        EffectRow hypothetical;
        string rowDescribed;
        if (row != null)
        {
            var codes = row.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            try
            {
                hypothetical = EffectSet.From(codes).ToRow();
            }
            catch (ArgumentException exception)
            {
                Console.Error.WriteLine($"Error: --row '{row}' is not a row of effect codes: {exception.Message}");
                return 1;
            }
            rowDescribed = hypothetical.ToCompactDisplayString();
        }
        else if (own != null)
        {
            hypothetical = own.DeclaredRow.Row;
            rowDescribed = own.DeclaredRow.Display + " (its current declared row)";
        }
        else
        {
            Console.Error.WriteLine(
                $"Error: no effect row is recorded for {Describe(target)}; pass --row to ask about a hypothetical one.");
            return 1;
        }

        var impacts = index.FindEffectImpact(target.SymbolId, hypothetical);
        var stopFitting = 0;
        foreach (var impact in impacts.OrderBy(
                     impact => $"{impact.Declaration.File}:{impact.Declaration.Line}",
                     StringComparer.Ordinal))
        {
            var declared = impact.Row?.DeclaredRow.Display ?? "(no row recorded)";
            var verdict = ProjectIndexBuilder.VerdictText(impact.Verdict);
            if (impact.Verdict != EffectFit.Fits)
                stopFitting++;
            Console.WriteLine(
                $"  {Describe(impact.Declaration)} — declares {declared}: {verdict}");
        }

        Console.WriteLine(
            impacts.Count == 0
                ? $"impact: nothing calls into {Describe(target)}, so no declared row is affected by a row of {rowDescribed}"
                : $"impact: {stopFitting} of {impacts.Count} affected declaration(s) would stop fitting "
                    + $"a row of {rowDescribed} on {Describe(target)}");
        ReportResidual(index, index.ImpactAnswerIsPartial());
        return 0;
    }

    private static Command CreateFacetCommand(string facet, string description, bool withJson = false)
    {
        var nameArgument = new Argument<string>(
            name: "name",
            description: "Declaration name to ask about");
        var pathOption = new Option<string>(
            aliases: ["--project"],
            description: "Project directory",
            getDefaultValue: () => ".");
        var inFileOption = new Option<string?>(
            aliases: ["--in-file"],
            description: "Disambiguate when several files declare the name");
        var noBuildOption = new Option<bool>(
            aliases: ["--no-build"],
            description: "Refuse a missing or stale index instead of rebuilding it");
        var jsonOption = new Option<bool>(
            aliases: ["--json"],
            description: "Emit the answer as an envelope document (schema v1.1) instead of text");

        var command = new Command(facet, description)
        {
            nameArgument, pathOption, inFileOption, noBuildOption,
        };
        if (withJson)
            command.AddOption(jsonOption);

        command.SetHandler((InvocationContext context) =>
        {
            context.ExitCode = Execute(
                facet,
                context.ParseResult.GetValueForArgument(nameArgument),
                context.ParseResult.GetValueForOption(pathOption)!,
                context.ParseResult.GetValueForOption(inFileOption),
                context.ParseResult.GetValueForOption(noBuildOption),
                withJson && context.ParseResult.GetValueForOption(jsonOption));
        });

        return command;
    }

    private static int Execute(
        string facet,
        string name,
        string projectDirectory,
        string? inFile,
        bool noBuild,
        bool json = false)
    {
        if (!Directory.Exists(projectDirectory))
        {
            Console.Error.WriteLine($"Error: directory not found: {projectDirectory}");
            return 1;
        }

        var index = Resolve(projectDirectory, noBuild, out var error);
        if (index == null)
        {
            Console.Error.WriteLine(error);
            return 1;
        }

        var declarations = index.FindDeclarations(name);
        if (declarations.Count == 0)
        {
            Console.WriteLine($"query: no declaration named '{name}'");
            ReportResidual(index, partial: index.DeclarationLookupIsPartial());
            return 1;
        }

        if (facet == "symbol")
        {
            foreach (var declaration in declarations)
                Console.WriteLine($"  {Describe(declaration)}");
            Console.WriteLine(
                $"query: {declarations.Count} declaration(s) named '{name}'");
            ReportResidual(index, index.DeclarationLookupIsPartial());
            return 0;
        }

        // Several declarations share the name: the caller must say which, rather
        // than the tool picking one and presenting the result as if unambiguous.
        IndexedDeclaration subject;
        if (declarations.Count == 1)
        {
            subject = declarations[0];
        }
        else
        {
            var matches = inFile == null
                ? declarations
                : declarations
                    .Where(declaration => string.Equals(
                        declaration.File, inFile, StringComparison.Ordinal))
                    .ToArray();
            if (matches.Count != 1)
            {
                Console.Error.WriteLine(
                    $"Error: '{name}' is declared in {declarations.Count} places; "
                        + "narrow it with --in-file:");
                foreach (var declaration in declarations)
                    Console.Error.WriteLine($"  {Describe(declaration)}");
                return 1;
            }
            subject = matches[0];
        }

        if (facet == "contracts")
        {
            var contracts = index.FindContracts(subject.SymbolId);
            foreach (var contract in contracts)
                Console.WriteLine($"  {contract.File}:{contract.Line} {contract.Kind}: {contract.Text}");
            Console.WriteLine(
                contracts.Count == 0
                    ? $"query: no contracts declared on {Describe(subject)}"
                    : $"query: {contracts.Count} contract(s) on {Describe(subject)}");
            // The index records what is DECLARED. A proof status would have to
            // come from the verifier, and a stale "Proven" is worse than none.
            Console.WriteLine(
                "query: declared contracts only — run `calor verify` for proof outcomes");
            return 0;
        }

        if (facet == "effects")
            return ExecuteEffects(index, subject, json);

        if (facet == "assumptions")
        {
            var assumptions = index.FindAssumptions(subject.SymbolId, subject.File);
            foreach (var assumption in assumptions)
            {
                var scope = assumption.Scope == "module" ? "module-wide" : "on this declaration";
                var category = string.IsNullOrEmpty(assumption.Category)
                    ? "" : $"[{assumption.Category}] ";
                Console.WriteLine(
                    $"  {assumption.File}:{assumption.Line} ({scope}) {category}{assumption.Description}");
            }
            Console.WriteLine(
                assumptions.Count == 0
                    ? $"query: nothing is assumed for {Describe(subject)}"
                    : $"query: {assumptions.Count} assumption(s) in force for {Describe(subject)}");
            return 0;
        }

        var (answer, partial) = facet switch
        {
            "callers" => (index.FindCallers(subject.SymbolId),
                index.CallersAnswerIsPartial(subject.Name)),
            "callees" => (index.FindCallees(subject.SymbolId),
                index.CalleesAnswerIsPartial(subject.SymbolId)),
            _ => (Array.Empty<IndexedDeclaration>(), false),
        };

        foreach (var declaration in answer.OrderBy(
                     declaration => $"{declaration.File}:{declaration.Line}",
                     StringComparer.Ordinal))
        {
            Console.WriteLine($"  {Describe(declaration)}");
        }

        var noun = facet == "callers" ? "caller" : "callee";
        Console.WriteLine(
            answer.Count == 0
                ? $"query: no {noun}s found for {Describe(subject)}"
                : $"query: {answer.Count} {noun}(s) of {Describe(subject)}");
        ReportResidual(index, partial);
        return 0;
    }

    /// <summary>
    /// v0.15 E5 (design-doc §8.6) — <c>calor query effects</c>: the declared
    /// row, the inferred row, the verdict between them and the diagnostic code
    /// that fires, plus the assumption reasons when the inferred row is only
    /// assumed; then the rows of the function-typed positions the declaration
    /// owns. Read off the index, which read it off the enforcement pass: no
    /// inference runs here.
    /// </summary>
    private static int ExecuteEffects(ProjectIndex index, IndexedDeclaration subject, bool json)
    {
        var rows = index.FindEffectRows(subject.SymbolId);
        var partial = index.EffectsAnswerIsPartial(subject.SymbolId, subject.File);

        if (json)
        {
            Console.WriteLine(EnvelopeWriter.Serialize(
                "query",
                new EffectsAnswer(Describe(subject), subject.SymbolId, rows, partial)));
            return 0;
        }

        if (rows.Count == 0)
        {
            var unavailable = index.Residual.EffectRowsUnavailable
                .FirstOrDefault(entry => entry.StartsWith(subject.File + ":", StringComparison.Ordinal));
            Console.WriteLine(
                unavailable != null
                    ? $"query: no effect row for {Describe(subject)} — {unavailable[(subject.File.Length + 2)..]}"
                    : $"query: no effect row is recorded for {Describe(subject)} (only functions, methods, "
                        + "constructors, accessors and rowed parameters/returns carry one)");
            ReportResidual(index, partial);
            return 1;
        }

        IndexedEffectRow? own = null;
        foreach (var row in rows)
        {
            if (row.OwnerSymbolId.Length == 0 && row.Kind is not ("parameter" or "return"))
            {
                own = row;
                Console.WriteLine($"  {Describe(subject)}");
                Console.WriteLine($"    declared: {row.DeclaredRow.Display}"
                    + (row.Declared ? "" : "  (no §E written — a declaration without one is pure)"));
                Console.WriteLine($"    inferred: {row.InferredRow?.Display ?? "(not inferred)"}");
                Console.WriteLine($"    verdict:  {DescribeVerdict(row)}");
                foreach (var reason in row.InferredRow?.Reasons ?? [])
                    Console.WriteLine($"    assumed because: {reason}");
                continue;
            }

            var bound = row.BoundRow == null ? "" : $"; bound type carries {row.BoundRow}";
            var position = row.Kind == "return" ? "return" : $"parameter {row.Name}";
            Console.WriteLine($"  {row.File}:{row.Line} {position} declares {row.DeclaredRow.Display}{bound}");
        }

        Console.WriteLine(
            own == null
                ? $"query: {rows.Count} position row(s) on {Describe(subject)}"
                : $"query: effect row of {Describe(subject)} — declared {own.DeclaredRow.Display}, "
                    + $"inferred {own.InferredRow?.Display ?? "(not inferred)"}, {DescribeVerdict(own)}");
        ReportResidual(index, partial);
        return 0;
    }

    private static string DescribeVerdict(IndexedEffectRow row)
    {
        var text = row.Verdict switch
        {
            "fits" => "fits",
            "does-not-fit" => "does not fit",
            "cannot-tell" => "cannot tell",
            _ => row.Verdict,
        };
        if (row.DiagnosticCode != null)
        {
            text += $" — {row.DiagnosticCode} fires";
            if (row.Forbidden.Count > 0)
                text += $" (undeclared: {string.Join(", ", row.Forbidden)})";
        }
        return text;
    }

    private static ProjectIndex? Resolve(
        string projectDirectory,
        bool noBuild,
        out string? error)
    {
        error = null;
        var output = IndexCommand.DefaultOutputDirectory(projectDirectory);
        var sources = ProjectIndexBuilder.DiscoverSources(projectDirectory);
        if (sources.Count == 0)
        {
            error = $"Error: no .calr files under {projectDirectory}";
            return null;
        }

        var options = new ProjectIndexBuilder.Options(
            projectDirectory, OptionsToken, sources);
        var inputs = ProjectIndexBuilder.CurrentInputs(options);

        var (loaded, status) = ProjectIndex.Load(output);
        var freshness = loaded == null
            ? status
            : loaded.CheckFreshness(
                inputs.CompilerHash, inputs.OptionsHash, inputs.ManifestHash, inputs.Files);

        if (freshness == ProjectIndex.Freshness.Fresh && loaded != null)
            return loaded;

        // A stale index is never answered from — that is the whole discipline.
        if (noBuild)
        {
            error = $"Error: index unusable — {ProjectIndex.Explain(freshness)}. "
                + "Run `calor index build` (or drop --no-build).";
            return null;
        }

        var rebuilt = ProjectIndexBuilder.Build(options);
        rebuilt.Save(output);
        return rebuilt;
    }

    private static string Describe(IndexedDeclaration declaration) =>
        $"{declaration.File}:{declaration.Line}:{declaration.Column} "
            + $"{declaration.Kind} {declaration.Name}";

    /// <summary>
    /// Printed with every answer, never on request only. "3 callers" over a
    /// silently dropped fourth is the failure this project keeps paying for.
    /// </summary>
    private static void ReportResidual(ProjectIndex index, bool partial)
    {
        if (!partial)
            return;

        Console.WriteLine(
            "query: PARTIAL — this answer may be incomplete. Calor binds one file "
                + "at a time, so a call resolves only when exactly one declaration "
                + "bears the name:");
        foreach (var file in index.Residual.UnreadableFiles)
            Console.WriteLine($"  unreadable file: {file} (nothing in it is indexed)");
        foreach (var call in index.Residual.UnresolvedCalls)
            Console.WriteLine($"  unresolved call: {call.File}: {call.Target}");
        foreach (var ambiguous in index.Residual.AmbiguousCallees)
            Console.WriteLine($"  ambiguous name: {ambiguous} (several declarations share it)");
    }
}
