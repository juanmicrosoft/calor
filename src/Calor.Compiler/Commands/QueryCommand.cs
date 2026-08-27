using System.CommandLine;
using System.CommandLine.Invocation;
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
///
/// v0.16 E7: the answers come from <see cref="ProjectIndexQueryReader"/>, which
/// the MCP tool <c>calor_query</c> reads through as well; this command is the
/// CLI's argument parsing plus the text and <c>--json</c> formatters, and the
/// MCP tool renders through the same formatters so the two surfaces answer
/// byte-identically.
/// </summary>
public static class QueryCommand
{
    public static Command Create()
    {
        var command = new Command("query", "Ask the project index about your code")
        {
            CreateFacetCommand("symbol", "Where a name is declared"),
            CreateFacetCommand("callers", "What calls a declaration", withJson: true),
            CreateFacetCommand("callees", "What a declaration calls", withJson: true),
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
        var jsonOption = new Option<bool>(
            aliases: ["--json"],
            description: "Emit the answer as an envelope document (schema v1.1) instead of text");

        var command = new Command("impact", "What a change could affect")
        {
            subjectArgument, pathOption, inFileOption, fileOption, noBuildOption,
            effectsOption, rowOption, jsonOption,
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
                context.ParseResult.GetValueForOption(rowOption),
                context.ParseResult.GetValueForOption(jsonOption));
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
        string? row = null,
        bool json = false)
    {
        var index = ProjectIndexQueryReader.Resolve(projectDirectory, noBuild, out var error);
        if (index == null)
        {
            Console.Error.WriteLine(error);
            return 1;
        }

        if (wholeFile)
        {
            var fileAnswer = ProjectIndexQueryReader.ImpactOfFile(index, subject, out error);
            if (fileAnswer == null)
            {
                Console.Error.WriteLine(error);
                return 1;
            }

            if (effects)
            {
                Console.Error.WriteLine("Error: --effects asks about one declaration's row; it cannot be combined with --file.");
                return 1;
            }

            if (json)
                Console.WriteLine(ToJson(fileAnswer));
            else
                WriteImpact(Console.Out, fileAnswer);
            return 0;
        }

        var lookup = ProjectIndexQueryReader.ResolveSubject(index, subject, inFile);
        if (lookup.NotFound)
        {
            Console.Error.WriteLine(ImpactNotFoundText(subject));
            return 1;
        }

        if (lookup.Subject == null)
        {
            foreach (var line in ProjectIndexQueryReader.AmbiguityLines(subject, lookup.Candidates))
                Console.Error.WriteLine(line);
            return 1;
        }

        if (effects)
        {
            var effectAnswer = ProjectIndexQueryReader.EffectImpact(index, lookup.Subject, row, out error);
            if (effectAnswer == null)
            {
                Console.Error.WriteLine(error);
                return 1;
            }

            if (json)
                Console.WriteLine(ToJson(effectAnswer));
            else
                WriteEffectImpact(Console.Out, effectAnswer);
            return 0;
        }

        var answer = ProjectIndexQueryReader.Impact(index, lookup.Subject);
        if (json)
            Console.WriteLine(ToJson(answer));
        else
            WriteImpact(Console.Out, answer);
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
        var index = ProjectIndexQueryReader.Resolve(projectDirectory, noBuild, out var error);
        if (index == null)
        {
            Console.Error.WriteLine(error);
            return 1;
        }

        var lookup = ProjectIndexQueryReader.ResolveSubject(index, name, inFile);
        if (lookup.NotFound)
        {
            foreach (var line in NotFoundLines(index, name))
                Console.WriteLine(line);
            return 1;
        }

        if (facet == "symbol")
        {
            foreach (var declaration in lookup.Candidates)
                Console.WriteLine($"  {ProjectIndexQueryReader.Describe(declaration)}");
            Console.WriteLine(
                $"query: {lookup.Candidates.Count} declaration(s) named '{name}'");
            if (index.DeclarationLookupIsPartial())
                WriteResidual(Console.Out, index.Residual);
            return 0;
        }

        if (lookup.Subject == null)
        {
            foreach (var line in ProjectIndexQueryReader.AmbiguityLines(name, lookup.Candidates))
                Console.Error.WriteLine(line);
            return 1;
        }

        var subject = lookup.Subject;
        if (facet == "contracts")
        {
            var contracts = index.FindContracts(subject.SymbolId);
            foreach (var contract in contracts)
                Console.WriteLine($"  {contract.File}:{contract.Line} {contract.Kind}: {contract.Text}");
            Console.WriteLine(
                contracts.Count == 0
                    ? $"query: no contracts declared on {ProjectIndexQueryReader.Describe(subject)}"
                    : $"query: {contracts.Count} contract(s) on {ProjectIndexQueryReader.Describe(subject)}");
            // The index records what is DECLARED. A proof status would have to
            // come from the verifier, and a stale "Proven" is worse than none.
            Console.WriteLine(
                "query: declared contracts only — run `calor verify` for proof outcomes");
            return 0;
        }

        if (facet == "effects")
        {
            var effects = ProjectIndexQueryReader.Effects(index, subject);
            if (json)
            {
                Console.WriteLine(ToJson(effects));
                return 0;
            }
            return WriteEffects(Console.Out, effects);
        }

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
                    ? $"query: nothing is assumed for {ProjectIndexQueryReader.Describe(subject)}"
                    : $"query: {assumptions.Count} assumption(s) in force for {ProjectIndexQueryReader.Describe(subject)}");
            return 0;
        }

        var answer = facet == "callers"
            ? ProjectIndexQueryReader.Callers(index, subject)
            : ProjectIndexQueryReader.Callees(index, subject);
        if (json)
            Console.WriteLine(ToJson(answer));
        else
            WriteDeclarations(Console.Out, answer);
        return 0;
    }

    // --- formatters (shared with the MCP tool) -----------------------------

    /// <summary>
    /// The envelope document <c>--json</c> prints (schema v1.1 envelope, the
    /// answer record under <c>data</c>). The MCP tool returns this same text.
    /// </summary>
    internal static string ToJson(object answer) => EnvelopeWriter.Serialize("query", answer);

    /// <summary>What a name-keyed facet prints when the name is unknown (stdout, exit 1).</summary>
    internal static IReadOnlyList<string> NotFoundLines(ProjectIndex index, string name)
    {
        var lines = new List<string> { $"query: no declaration named '{name}'" };
        if (index.DeclarationLookupIsPartial())
            lines.AddRange(ProjectIndexQueryReader.ResidualLines(index.Residual));
        return lines;
    }

    /// <summary>What <c>impact</c> prints when the name is unknown (stderr, exit 1).</summary>
    internal static string ImpactNotFoundText(string name) =>
        $"Error: no declaration named '{name}'. Use --file to ask about a file.";

    internal static void WriteDeclarations(TextWriter writer, ProjectIndexQueryReader.DeclarationsAnswer answer)
    {
        foreach (var declaration in answer.Declarations)
            writer.WriteLine($"  {ProjectIndexQueryReader.Describe(declaration)}");

        var noun = answer.Facet == "callers" ? "caller" : "callee";
        writer.WriteLine(
            answer.Declarations.Count == 0
                ? $"query: no {noun}s found for {answer.Subject}"
                : $"query: {answer.Declarations.Count} {noun}(s) of {answer.Subject}");
        WriteResidual(writer, answer.Residual);
    }

    internal static void WriteImpact(TextWriter writer, ProjectIndexQueryReader.ImpactAnswer answer)
    {
        foreach (var declaration in answer.Affected)
            writer.WriteLine($"  {ProjectIndexQueryReader.Describe(declaration)}");

        writer.WriteLine(
            answer.Affected.Count == 0
                ? $"impact: nothing calls into {answer.Subject}"
                : $"impact: {answer.Affected.Count} declaration(s) in {answer.AffectedFiles} file(s) "
                    + $"affected by a change to {answer.Subject}");

        if (answer.File != null)
        {
            writer.WriteLine(
                "impact: file-grained — a change to ANY declaration in this file "
                    + "implicates all of these. Ask about a declaration for a precise answer.");
        }

        WriteResidual(writer, answer.Residual);
    }

    internal static void WriteEffectImpact(TextWriter writer, ProjectIndexQueryReader.EffectImpactAnswer answer)
    {
        foreach (var impact in answer.Impacts)
        {
            var declared = impact.DeclaredRow ?? "(no row recorded)";
            writer.WriteLine(
                $"  {ProjectIndexQueryReader.Describe(impact.Declaration)} — declares {declared}: {impact.Verdict}");
        }

        var rowDescribed = answer.RowIsCurrentDeclared
            ? answer.Row + " (its current declared row)"
            : answer.Row;

        // "Would stop fitting" is DoesNotFit only; a caller whose row is Unknown
        // or unrecorded is counted as what it is — undecided — never as broken.
        writer.WriteLine(
            answer.Impacts.Count == 0
                ? $"impact: nothing calls into {answer.Subject}, so no declared row is affected by a row of {rowDescribed}"
                : $"impact: {answer.StopFitting} of {answer.Impacts.Count} affected declaration(s) would stop fitting "
                    + $"a row of {rowDescribed} on {answer.Subject}");
        if (answer.CannotTell > 0)
        {
            writer.WriteLine(
                $"impact: {answer.CannotTell} of {answer.Impacts.Count} cannot tell — no declared row the index could compare against");
        }
        WriteResidual(writer, answer.Residual);
    }

    /// <summary>
    /// The text answer for <c>effects</c>. Returns the exit code: 1 when the
    /// index holds no row for the subject, which the text says and the JSON
    /// form reports as an empty <c>rows</c>.
    /// </summary>
    internal static int WriteEffects(TextWriter writer, ProjectIndexQueryReader.EffectsAnswer answer)
    {
        if (answer.Rows.Count == 0)
        {
            writer.WriteLine(
                answer.Unavailable != null
                    ? $"query: no effect row for {answer.Subject} — {answer.Unavailable}"
                    : $"query: no effect row is recorded for {answer.Subject} (only functions, methods, "
                        + "constructors, accessors and rowed parameters/returns carry one)");
            WriteResidual(writer, answer.Residual);
            return 1;
        }

        IndexedEffectRow? own = null;
        foreach (var row in answer.Rows)
        {
            if (ProjectIndexQueryReader.IsOwnRow(row))
            {
                own = row;
                writer.WriteLine($"  {answer.Subject}");
                writer.WriteLine($"    declared: {row.DeclaredRow.Display}"
                    + (row.Declared ? "" : "  (no §E written — a declaration without one is pure)"));
                writer.WriteLine($"    inferred: {row.InferredRow?.Display ?? "(not inferred)"}");
                writer.WriteLine($"    verdict:  {ProjectIndexQueryReader.DescribeVerdict(row)}");
                foreach (var reason in row.InferredRow?.Reasons ?? [])
                    writer.WriteLine($"    assumed because: {reason}");
                continue;
            }

            var bound = row.BoundRow == null ? "" : $"; bound type carries {row.BoundRow}";
            var position = row.Kind == "return" ? "return" : $"parameter {row.Name}";
            writer.WriteLine($"  {row.File}:{row.Line} {position} declares {row.DeclaredRow.Display}{bound}");
        }

        writer.WriteLine(
            own == null
                ? $"query: {answer.Rows.Count} position row(s) on {answer.Subject}"
                : $"query: effect row of {answer.Subject} — declared {own.DeclaredRow.Display}, "
                    + $"inferred {own.InferredRow?.Display ?? "(not inferred)"}, {ProjectIndexQueryReader.DescribeVerdict(own)}");
        WriteResidual(writer, answer.Residual);
        return 0;
    }

    /// <summary>
    /// Printed with every answer, never on request only. "3 callers" over a
    /// silently dropped fourth is the failure this project keeps paying for.
    /// </summary>
    internal static void WriteResidual(TextWriter writer, IndexResidual? residual)
    {
        if (residual == null)
            return;
        foreach (var line in ProjectIndexQueryReader.ResidualLines(residual))
            writer.WriteLine(line);
    }
}
