namespace Calor.Compiler.LegacySyntax;

/// <summary>Tab-separated record of every input considered: status, location, reason, detail.</summary>
internal sealed class Log : IDisposable
{
    private readonly TextWriter? _writer;
    private readonly Exclusions _exclusions;
    public Dictionary<string, int> SkipReasons { get; } = new(StringComparer.Ordinal);
    public int RewrittenCount { get; private set; }
    public int Skipped { get; private set; }

    public Log(string? path, Exclusions exclusions)
    {
        _exclusions = exclusions;
        if (path != null)
            _writer = new StreamWriter(path) { NewLine = "\n" };
        _writer?.WriteLine("status\tpath\tline\treason\tdetail");
    }

    public void Rewritten(string path, int line, string kind)
    {
        RewrittenCount++;
        _writer?.WriteLine($"rewritten\t{_exclusions.Relative(path)}\t{line}\t{kind}\t");
    }

    public void Skip(string path, int line, string reason, string? detail)
    {
        Skipped++;
        SkipReasons[reason] = SkipReasons.GetValueOrDefault(reason) + 1;
        _writer?.WriteLine($"skipped\t{_exclusions.Relative(path)}\t{line}\t{reason}\t{Clean(detail)}");
    }

    private static string Clean(string? s) => (s ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');

    public void Summary(TextWriter output)
    {
        output.WriteLine($"rewritten: {RewrittenCount}");
        output.WriteLine($"skipped:   {Skipped}");
        foreach (var (reason, count) in SkipReasons.OrderByDescending(kv => kv.Value))
            output.WriteLine($"  {count,6}  {reason}");
    }

    public void Dispose() => _writer?.Dispose();
}
