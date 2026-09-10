using System.Text.RegularExpressions;
using Calor.Compiler.Diagnostics;

namespace Calor.Compiler.SelfCheck;

/// <summary>Compiles the exact syntax reference copied into agent-task workspaces.</summary>
public static class AgentTaskReferenceChecker
{
    public static readonly string RelativePath =
        Path.Combine("tests", "E2E", "agent-tasks", "lib", "helpers.sh");

    private const string StartMarker = "<< 'CALOR_REFERENCE'";
    private static readonly Regex Declaration = new(@"^§(?:M|F|AF|CL|IFACE|DEL)\{");

    public sealed record ReferenceProgram(string Source, int FirstContentLine, bool Wrapped);

    public static IReadOnlyList<ReferenceProgram> ExtractPrograms(string helperSource)
    {
        var lines = helperSource.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var start = Array.FindIndex(lines, line => line.TrimEnd().EndsWith(StartMarker, StringComparison.Ordinal));
        if (start < 0)
            throw new InvalidDataException("Agent reference heredoc opening marker is missing.");
        var end = Array.FindIndex(lines, start + 1, line => line == "CALOR_REFERENCE");
        if (end < 0)
            throw new InvalidDataException("Agent reference heredoc closing marker is missing.");

        var programs = new List<ReferenceProgram>();
        string? language = null;
        var contentStart = 0;
        for (var index = start + 1; index < end; index++)
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
            if (language == "calor-fragment" || first == index || !Declaration.IsMatch(lines[first].TrimStart()))
            {
                language = null;
                continue;
            }
            if (language is not ("" or "calor"))
                throw new InvalidDataException($"Complete Calor example at line {first + 1} has unexpected fence language '{language}'.");
            var body = string.Join("\n", lines[first..index]);
            var wrapped = !lines[first].TrimStart().StartsWith("§M{", StringComparison.Ordinal);
            if (wrapped)
                body = $"§M{{m_ref_{programs.Count}:AgentReference{programs.Count}}}\n"
                    + string.Join("\n", body.Split('\n').Select(line => "  " + line));
            programs.Add(new(body, first + 1, wrapped));
            language = null;
        }
        if (language != null)
            throw new InvalidDataException($"Agent reference has an unterminated fence at line {contentStart}.");
        return programs;
    }

    public static List<Diagnostic> Check(DocFile helper)
    {
        var diagnostics = new List<Diagnostic>();
        IReadOnlyList<ReferenceProgram> programs;
        try
        {
            programs = ExtractPrograms(helper.Content);
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
            ExemplarCompileChecker.CheckCompletePrograms(example, findings);
            foreach (var finding in findings)
                diagnostics.Add(new(finding.Code, finding.Severity, finding.Message,
                    helper.Path,
                    program.FirstContentLine + Math.Max(0, finding.Span.Line - (program.Wrapped ? 3 : 2)),
                    Math.Max(1, finding.Span.Column - (program.Wrapped ? 2 : 0))));
        }
        return diagnostics;
    }
}
