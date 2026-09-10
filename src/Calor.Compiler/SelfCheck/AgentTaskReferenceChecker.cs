using System.Text.RegularExpressions;
using Calor.Compiler.Diagnostics;

namespace Calor.Compiler.SelfCheck;

/// <summary>Compiles the exact syntax reference copied into agent-task workspaces.</summary>
public static class AgentTaskReferenceChecker
{
    public static readonly string RelativePath =
        Path.Combine("tests", "E2E", "agent-tasks", "lib", "helpers.sh");

    private const string StartMarker = "<< 'CALOR_REFERENCE'\n";
    private const string EndMarker = "\nCALOR_REFERENCE";
    private static readonly Regex Fence = new(
        @"^```(?<language>[^\r\n]*)\r?\n(?<body>.*?)^```[ \t]*\r?$",
        RegexOptions.Multiline | RegexOptions.Singleline);
    private static readonly Regex Declaration = new(@"^§(?:M|F|AF|CL|IFACE|DEL)\{");

    public static IReadOnlyList<ExemplarCompileChecker.ExemplarProgram> ExtractPrograms(string helperSource)
    {
        var source = helperSource.Replace("\r\n", "\n", StringComparison.Ordinal);
        var start = source.IndexOf(StartMarker, StringComparison.Ordinal);
        if (start < 0)
            throw new InvalidDataException("Agent reference heredoc opening marker is missing.");
        start += StartMarker.Length;
        var end = source.IndexOf(EndMarker, start, StringComparison.Ordinal);
        if (end < 0)
            throw new InvalidDataException("Agent reference heredoc closing marker is missing.");

        var programs = new List<ExemplarCompileChecker.ExemplarProgram>();
        foreach (Match fence in Fence.Matches(source[start..end]))
        {
            var language = fence.Groups["language"].Value.Trim();
            if (language is not ("" or "calor"))
                continue;
            var body = fence.Groups["body"].Value;
            var first = body.Split('\n').Select(line => line.Trim())
                .FirstOrDefault(line => line.Length > 0 && !line.StartsWith("//", StringComparison.Ordinal));
            if (first == null || !Declaration.IsMatch(first))
                continue;
            var lineNumber = source[..(start + fence.Groups["body"].Index)].Count(c => c == '\n') + 1;
            if (!first.StartsWith("§M{", StringComparison.Ordinal))
                body = $"§M{{m_ref_{programs.Count}:AgentReference{programs.Count}}}\n"
                    + string.Join("\n", body.Split('\n').Select(line => "  " + line));
            programs.Add(new(body, lineNumber, Suppressed: false));
        }
        return programs;
    }

    public static List<Diagnostic> Check(DocFile helper)
    {
        var diagnostics = new List<Diagnostic>();
        IReadOnlyList<ExemplarCompileChecker.ExemplarProgram> programs;
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
                    helper.Path, program.FirstContentLine + Math.Max(0, finding.Span.Line - 3), finding.Span.Column));
        }
        return diagnostics;
    }
}
