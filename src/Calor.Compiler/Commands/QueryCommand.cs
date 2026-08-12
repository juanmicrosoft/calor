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
        };
        return command;
    }

    private static Command CreateFacetCommand(string facet, string description)
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

        var command = new Command(facet, description)
        {
            nameArgument, pathOption, inFileOption, noBuildOption,
        };

        command.SetHandler((InvocationContext context) =>
        {
            context.ExitCode = Execute(
                facet,
                context.ParseResult.GetValueForArgument(nameArgument),
                context.ParseResult.GetValueForOption(pathOption)!,
                context.ParseResult.GetValueForOption(inFileOption),
                context.ParseResult.GetValueForOption(noBuildOption));
        });

        return command;
    }

    private static int Execute(
        string facet,
        string name,
        string projectDirectory,
        string? inFile,
        bool noBuild)
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
