using System.Text.Json;
using Calor.Compiler;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Indexing;
using Xunit;

namespace Calor.Tests.Shared;

internal static class EditScriptCorpus
{
    internal sealed record ScriptStep(string SourceDirectory, string Options);
    internal sealed record EditScript(
        string Id, IReadOnlyList<ScriptStep> Steps, bool ExpectsReuse, string ReuseNote);

    internal static string RepoRoot
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
                 directory != null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Calor.sln"))
                    && Directory.Exists(Path.Combine(directory.FullName, "src", "Calor.Compiler")))
                    return directory.FullName;
            }
            throw new DirectoryNotFoundException("Calor repository root not found.");
        }
    }

    private static string CorpusRoot => Path.Combine(RepoRoot, "tests", "TestData", "EditScripts");

    internal static string[] EnumerateScriptDirectories() =>
        Directory.GetDirectories(CorpusRoot).Order(StringComparer.Ordinal).ToArray();

    internal static EditScript LoadScript(string scriptName)
    {
        var directory = Path.Combine(CorpusRoot, scriptName);
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "script.json")));
        var root = json.RootElement;
        var steps = root.GetProperty("steps").EnumerateArray()
            .Select(step => new ScriptStep(
                Path.Combine(directory, step.GetProperty("dir").GetString()!),
                step.GetProperty("options").GetString()!)).ToArray();
        Assert.NotEmpty(steps);
        foreach (var step in steps)
        {
            Assert.True(Directory.Exists(step.SourceDirectory));
            Assert.Contains(step.Options, new[] { "effects-on", "effects-off", "docs-required" });
        }
        return new EditScript(root.GetProperty("id").GetString()!, steps,
            root.GetProperty("expectsReuse").GetBoolean(), root.GetProperty("reuseNote").GetString()!);
    }

    internal static CompilationOptions OptionsFor(string profile) => new()
    {
        EnforceEffects = profile != "effects-off",
        RequireDocs = profile == "docs-required",
    };

    internal static void SyncWorkspace(string workspace, string stepDirectory)
    {
        var wanted = Directory.GetFiles(stepDirectory, "*.calr").Select(Path.GetFileName)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var existing in Directory.GetFiles(workspace, "*.calr"))
        {
            if (wanted.Contains(Path.GetFileName(existing)))
                continue;
            File.Delete(existing);
            File.Delete(Path.ChangeExtension(existing, ".g.cs"));
        }
        foreach (var source in Directory.GetFiles(stepDirectory, "*.calr"))
            File.Copy(source, Path.Combine(workspace, Path.GetFileName(source)), overwrite: true);
    }

    internal static IReadOnlyList<string> Canonicalize(DiagnosticBag diagnostics, string workspace) =>
        diagnostics.Select(d => DiagnosticLine(workspace, d.FilePath, d.Code,
            d.Severity.ToString(), d.Span.Line, d.Span.Column, d.Message))
            .Order(StringComparer.Ordinal).ToArray();

    internal static string DiagnosticLine(
        string workspace, string? file, string code, string severity, int line, int column, string message) =>
        string.Join("|", string.IsNullOrEmpty(file) ? "" :
            Path.GetRelativePath(workspace, file).Replace('\\', '/'),
            code, severity.ToLowerInvariant(), line, column, message);

    internal static string SerializeIndex(string workspace, ScriptStep step)
    {
        var sources = Directory.GetFiles(workspace, "*.calr").Order(StringComparer.Ordinal).ToArray();
        return CanonicalizeIndex(ProjectIndexBuilder.Build(
            new ProjectIndexBuilder.Options(workspace, step.Options, sources)));
    }

    // The existing gate's projection deliberately omits machine/path-dependent symbol IDs.
    internal static string CanonicalizeIndex(ProjectIndex index)
    {
        var lines = new List<string>();
        foreach (var d in index.Declarations)
            lines.Add($"decl|{d.File}|{d.Line}|{d.Column}|{d.Kind}|{d.Name}");
        foreach (var edge in index.CallEdges)
            lines.Add($"edge|{edge.File}|{edge.Line}|{edge.Column}");
        foreach (var o in index.Occurrences)
            lines.Add($"occ|{o.File}|{o.Line}|{o.Column}|{o.Kind}");
        foreach (var row in index.EffectRows)
            lines.Add($"effrow|{row.File}|{row.Line}|{row.Kind}|{row.Name}"
                + $"|declared={row.Declared}|{DescribeRow(row.DeclaredRow)}|{DescribeRow(row.InferredRow)}"
                + $"|{row.Verdict}|{row.DiagnosticCode}"
                + $"|forbidden={string.Join(",", row.Forbidden)}|bound={row.BoundRow}");
        foreach (var value in index.Residual.EffectRowsUnavailable)
            lines.Add($"residual-effect-rows-unavailable|{value}");
        foreach (var value in index.Residual.UnreadableFiles)
            lines.Add($"residual-unreadable|{value}");
        foreach (var value in index.Residual.UnresolvedCalls)
            lines.Add($"residual-unresolved|{value}");
        foreach (var value in index.Residual.AmbiguousCallees)
            lines.Add($"residual-ambiguous|{value}");
        return string.Join("\n", lines);
    }

    private static string DescribeRow(IndexedRow? row) => row == null ? "-" :
        $"{row.State}{{{string.Join(",", row.Effects)}}}"
        + $"<{string.Join(",", row.Variables.Select(v => $"{v.Ordinal}:{v.Name}"))}>"
        + $"[{string.Join(",", row.Reasons)}]";
}
