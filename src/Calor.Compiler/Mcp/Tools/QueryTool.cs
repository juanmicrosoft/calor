using System.Text.Json;
using Calor.Compiler.Commands;

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
                    "description": "Project directory whose .calr files the index covers (the CLI's --project)"
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
                    "description": "Directory holding .calor-index.json, when the index was built with `calor index build --output`. Default: <projectDirectory>/obj/calor"
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

    private static McpToolResult Execute(JsonElement? arguments)
    {
        var projectDirectory = GetString(arguments, "projectDirectory");
        if (string.IsNullOrWhiteSpace(projectDirectory))
            return McpToolResult.Error("Missing required parameter: projectDirectory");

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

        var effects = GetBool(arguments, "effects");
        var row = GetString(arguments, "row");
        if (facet != "impact" && (effects || row != null))
            return McpToolResult.Error("Parameters 'effects' and 'row' apply to facet \"impact\" only");
        if (row != null && !effects)
            return McpToolResult.Error("Parameter 'row' requires effects=true");

        var inFile = GetString(arguments, "inFile");
        var noBuild = GetBool(arguments, "noBuild");
        var indexPath = GetString(arguments, "indexPath");
        if (indexPath != null && File.Exists(indexPath))
            indexPath = Path.GetDirectoryName(Path.GetFullPath(indexPath));

        var index = ProjectIndexQueryReader.Resolve(projectDirectory, noBuild, out var error, indexPath);
        if (index == null)
            return McpToolResult.Error(error!);

        var lookup = ProjectIndexQueryReader.ResolveSubject(index, symbol, inFile);
        if (lookup.NotFound)
        {
            return McpToolResult.Error(facet == "impact"
                ? QueryCommand.ImpactNotFoundText(symbol)
                : string.Join(Environment.NewLine, QueryCommand.NotFoundLines(index, symbol)));
        }

        if (lookup.Subject == null)
        {
            return McpToolResult.Error(string.Join(
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
                    return McpToolResult.Error(error!);
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
