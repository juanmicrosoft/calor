using System.Text.Json;
using Calor.Compiler.Commands;
using Calor.Compiler.Mcp.Sessions;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// v0.16 E7 — the MCP query surface over the project index. Answers
/// <c>callers | callees | impact [effects, row] | effects</c> for a declaration
/// by reading the same on-disk index <c>calor query</c> reads, through the same
/// reader (<see cref="ProjectIndexQueryReader"/>) and the same formatters
/// (<see cref="QueryCommand"/>), so an agent's answer is byte-identical to the
/// CLI's: text mode returns what the CLI prints, JSON mode returns the CLI's
/// <c>--json</c> envelope document.
///
/// Not an extension of <see cref="NavigateTool"/>: that tool works on source
/// text and has no notion of a project directory or an index.
/// </summary>
public sealed class QueryTool : McpToolBase
{
    /// <summary>
    /// The write-confinement root, canonicalized. Resolving a stale or missing
    /// index REBUILDS it — <c>ProjectIndex.Save</c> creates directories and
    /// writes a file — so this tool is a writer and is confined exactly as
    /// <see cref="FileWriteTool"/> is: <c>mcp --root</c> pins the root, and
    /// with no root the server process's working directory is it.
    /// </summary>
    private readonly string _root;

    /// <summary>Every argument the schema declares — the additionalProperties:false denominator.</summary>
    private static readonly string[] KnownArguments =
    [
        "projectDirectory", "facet", "symbol", "inFile", "effects", "row", "noBuild", "indexPath", "format",
    ];

    internal QueryTool(string? rootDirectory = null)
    {
        _root = CanonicalPath.Resolve(rootDirectory ?? Environment.CurrentDirectory);
    }

    public override string Name => "calor_query";

    public override string Description =>
        "Ask the project index about a declaration: who calls it (callers), what it calls (callees), "
        + "what a change to it could affect (impact; with effects=true, which callers' declared effect "
        + "rows a new row would stop fitting), or its declared and inferred effect rows (effects). "
        + "Reads the index `calor index build` writes under <projectDirectory>/obj/calor and answers "
        + "byte-identically to `calor query`; a stale or missing index is rebuilt first unless noBuild=true. "
        + "Every answer says when it may be PARTIAL and why.";

    // Not read-only: resolving a stale or missing index rebuilds it on disk
    // (obj/calor/.calor-index.json), exactly as `calor query` does. noBuild=true
    // never writes.
    public override McpToolAnnotations? Annotations => new() { IdempotentHint = true };

    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "projectDirectory": {
                    "type": "string",
                    "description": "Project directory whose .calr files the index covers (the CLI's --project). Must be inside the server root (`calor mcp --root`); the path is canonicalized (symlinks and `..` resolved) before it is checked and used, and refusals name the canonical path"
                },
                "facet": {
                    "type": "string",
                    "enum": ["callers", "callees", "impact", "effects"],
                    "description": "callers=what calls the declaration, callees=what it calls, impact=what a change to it could affect, effects=its declared and inferred effect rows and the verdict between them"
                },
                "symbol": {
                    "type": "string",
                    "description": "Declaration name to ask about"
                },
                "inFile": {
                    "type": "string",
                    "description": "Disambiguate when several files declare the name (project-relative path, the CLI's --in-file)"
                },
                "effects": {
                    "type": "boolean",
                    "default": false,
                    "description": "facet=impact only: effect-row blast radius — which affected callers' declared rows the subject's row would stop fitting (the CLI's --effects)"
                },
                "row": {
                    "type": "string",
                    "description": "With effects=true: the row the subject would carry after the change, as comma-separated effect codes (e.g. \"cw,fs:w\"; \"\" for pure). Default: its current declared row (the CLI's --row)"
                },
                "noBuild": {
                    "type": "boolean",
                    "default": false,
                    "description": "Refuse a missing or stale index instead of rebuilding it (the CLI's --no-build)"
                },
                "indexPath": {
                    "type": "string",
                    "description": "READ-ONLY override: a directory (or the .calor-index.json file) holding an index built with `calor index build --output`, inside the server root. The index is never rebuilt or written through this argument — a stale or missing one is refused. Default: <projectDirectory>/obj/calor, which IS rebuilt when stale unless noBuild is true"
                },
                "format": {
                    "type": "string",
                    "enum": ["json", "text"],
                    "default": "json",
                    "description": "json=the envelope document `calor query … --json` prints; text=the lines `calor query` prints"
                }
            },
            "required": ["projectDirectory", "facet", "symbol"],
            "additionalProperties": false
        }
        """;

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken = default)
        => Task.FromResult(Execute(arguments));

    /// <summary>
    /// A refusal the CLI would print. It carries the CLI's own text, and — like
    /// every line the CLI writes — it ends with a newline, so an answer and a
    /// refusal are the same shape to a client that concatenates them.
    /// Argument-level validation (a missing or malformed parameter, which the
    /// CLI's parser rejects before any of this) stays a bare message, as every
    /// other MCP tool's does.
    /// </summary>
    private static McpToolResult Refusal(string text) =>
        McpToolResult.Error(text.EndsWith(Environment.NewLine, StringComparison.Ordinal)
            ? text
            : text + Environment.NewLine);

    /// <summary>
    /// A boolean argument that must be a JSON boolean when present.
    /// <see cref="McpToolBase.GetBool"/> silently returns the default for any
    /// other kind, which would turn <c>"noBuild": "true"</c> (a string) into
    /// "rebuild the index" — the opposite of what the caller asked, on the one
    /// flag that decides whether this tool writes.
    /// </summary>
    private static bool ReadBool(JsonElement? arguments, string name, out string? error)
    {
        error = null;
        if (arguments is not { ValueKind: JsonValueKind.Object } element
            || !element.TryGetProperty(name, out var property))
        {
            return false;
        }

        switch (property.ValueKind)
        {
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
            case JsonValueKind.Null:
                return false;
            default:
                error = $"Parameter '{name}' must be a boolean (true or false), not {property.ValueKind.ToString().ToLowerInvariant()}";
                return false;
        }
    }

    private McpToolResult Execute(JsonElement? arguments)
    {
        var projectDirectory = GetString(arguments, "projectDirectory");
        if (string.IsNullOrWhiteSpace(projectDirectory))
            return McpToolResult.Error("Missing required parameter: projectDirectory");

        // The schema says additionalProperties:false; enforce it rather than
        // trusting every client to validate. A misspelled argument that is
        // silently ignored is how "noBuild" becomes "the tool wrote anyway".
        if (arguments is { ValueKind: JsonValueKind.Object } given)
        {
            var unknown = given.EnumerateObject()
                .Select(property => property.Name)
                .Where(name => !KnownArguments.Contains(name, StringComparer.Ordinal))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            if (unknown.Length > 0)
            {
                return McpToolResult.Error(
                    $"Unknown parameter(s): {string.Join(", ", unknown)}. Accepted: {string.Join(", ", KnownArguments)}");
            }
        }

        var facet = GetString(arguments, "facet");
        if (facet == null || !ProjectIndexQueryReader.Facets.Contains(facet, StringComparer.Ordinal))
        {
            return McpToolResult.Error(
                $"Parameter 'facet' must be one of: {string.Join(", ", ProjectIndexQueryReader.Facets)}");
        }

        var symbol = GetString(arguments, "symbol");
        if (string.IsNullOrWhiteSpace(symbol))
            return McpToolResult.Error("Missing required parameter: symbol");

        var format = GetString(arguments, "format") ?? "json";
        if (format is not ("json" or "text"))
            return McpToolResult.Error("Parameter 'format' must be \"json\" or \"text\"");

        var effects = ReadBool(arguments, "effects", out var argumentError);
        if (argumentError != null)
            return McpToolResult.Error(argumentError);
        var row = GetString(arguments, "row");
        if (facet != "impact" && (effects || row != null))
            return McpToolResult.Error("Parameters 'effects' and 'row' apply to facet \"impact\" only");
        if (row != null && !effects)
            return McpToolResult.Error("Parameter 'row' requires effects=true");

        var inFile = GetString(arguments, "inFile");
        var noBuild = ReadBool(arguments, "noBuild", out argumentError);
        if (argumentError != null)
            return McpToolResult.Error(argumentError);

        // Write confinement (the same rule calor_file_write applies): this tool
        // rebuilds — and therefore writes — the index, so both the project it
        // reads and the directory it would write to must be inside the pinned
        // root. Canonicalized first, so `..` and symlinks cannot step outside —
        // and the CANONICAL path is what the rest of this method uses, so the
        // value that was checked is the value that is acted on (and the value
        // any refusal names).
        string canonicalProject;
        try
        {
            canonicalProject = CanonicalPath.Resolve(projectDirectory);
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or NotSupportedException)
        {
            return McpToolResult.Error($"Parameter 'projectDirectory' is not a usable path: {exception.Message}");
        }

        if (!CanonicalPath.IsUnder(canonicalProject, _root))
        {
            return McpToolResult.Error(
                $"Parameter 'projectDirectory' is outside the server's root '{_root}' — "
                    + "start the server with `calor mcp --root <project>` over the directory you want to query");
        }

        // An empty string is not "no index path": Path.GetFullPath("") throws,
        // and an exception here would leave the protocol with an internal error
        // instead of the refusal this tool promises.
        var indexPath = GetString(arguments, "indexPath");
        if (string.IsNullOrWhiteSpace(indexPath))
        {
            indexPath = null;
        }
        else
        {
            if (File.Exists(indexPath))
                indexPath = Path.GetDirectoryName(Path.GetFullPath(indexPath));

            string canonicalIndex;
            try
            {
                canonicalIndex = CanonicalPath.Resolve(indexPath!);
            }
            catch (Exception exception) when (exception is IOException or ArgumentException or NotSupportedException)
            {
                return McpToolResult.Error($"Parameter 'indexPath' is not a usable path: {exception.Message}");
            }

            // Inside the root, like every other path this tool touches. It need
            // not be inside the project: `calor index build --output` may put an
            // index anywhere, and reading one from a sibling directory is
            // harmless because nothing is ever written through this argument
            // (see the read-only rule below) and a foreign index is refused by
            // its own header.
            if (!CanonicalPath.IsUnder(canonicalIndex, _root))
            {
                return McpToolResult.Error(
                    $"Parameter 'indexPath' is outside the server's root '{_root}'");
            }

            indexPath = canonicalIndex;
        }

        // An explicit index path is READ-ONLY. Rebuilding through it would
        // create a tree wherever it points, or overwrite another project's
        // index in place with this project's contents — and the argument has no
        // CLI counterpart, so "byte-identical to `calor query`" gives it no
        // cover. A stale or missing index at an explicit path is therefore
        // refused; the refusal says how to rebuild it, since "drop --no-build"
        // would be advice the caller cannot take.
        var index = ProjectIndexQueryReader.Resolve(
            canonicalProject,
            noBuild || indexPath != null,
            out var error,
            indexPath,
            indexPath == null
                ? null
                : $"`indexPath` is read-only; rebuild it with `calor index build --output {indexPath}`.");
        if (index == null)
            return Refusal(error!);

        var lookup = ProjectIndexQueryReader.ResolveSubject(index, symbol, inFile);
        if (lookup.NotFound)
        {
            return Refusal(facet == "impact"
                // The CLI's own text ends "Use --file to ask about a file",
                // which this tool has no counterpart for: whole-file impact is
                // CLI-only (`calor query impact <file> --file`). Say that
                // instead of pointing at a flag that does not exist here.
                ? $"Error: no declaration named '{symbol}'. Whole-file impact is CLI-only: "
                    + "`calor query impact <file> --file`."
                : string.Join(Environment.NewLine, QueryCommand.NotFoundLines(index, symbol)));
        }

        if (lookup.Subject == null)
        {
            return Refusal(string.Join(
                Environment.NewLine, ProjectIndexQueryReader.AmbiguityLines(symbol, lookup.Candidates)));
        }

        var json = format == "json";
        var writer = new StringWriter();
        switch (facet)
        {
            case "callers":
            case "callees":
            {
                var answer = facet == "callers"
                    ? ProjectIndexQueryReader.Callers(index, lookup.Subject)
                    : ProjectIndexQueryReader.Callees(index, lookup.Subject);
                if (json)
                    writer.WriteLine(QueryCommand.ToJson(answer));
                else
                    QueryCommand.WriteDeclarations(writer, answer);
                return McpToolResult.Text(writer.ToString());
            }

            case "impact" when effects:
            {
                var answer = ProjectIndexQueryReader.EffectImpact(index, lookup.Subject, row, out error);
                if (answer == null)
                    return Refusal(error!);
                if (json)
                    writer.WriteLine(QueryCommand.ToJson(answer));
                else
                    QueryCommand.WriteEffectImpact(writer, answer);
                return McpToolResult.Text(writer.ToString());
            }

            case "impact":
            {
                var answer = ProjectIndexQueryReader.Impact(index, lookup.Subject);
                if (json)
                    writer.WriteLine(QueryCommand.ToJson(answer));
                else
                    QueryCommand.WriteImpact(writer, answer);
                return McpToolResult.Text(writer.ToString());
            }

            default:
            {
                var answer = ProjectIndexQueryReader.Effects(index, lookup.Subject);
                if (json)
                {
                    writer.WriteLine(QueryCommand.ToJson(answer));
                    return McpToolResult.Text(writer.ToString());
                }

                // The CLI exits 1 when no row is recorded; the tool says so the same way.
                var exitCode = QueryCommand.WriteEffects(writer, answer);
                return McpToolResult.Text(writer.ToString(), isError: exitCode != 0);
            }
        }
    }
}
