using System.Text.Json;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// <c>--format json|sarif</c> combined with <c>--verbose</c>: stdout must be
/// exactly one JSON document, and the verbose progress text must still be
/// printed — on stderr. Covers every CLI command that has both options.
///
/// <para>Regression: <c>calor convert x.cs --format json --verbose</c> printed
/// the converter's <c>Converted N nodes</c> block to stdout before the envelope;
/// <c>calor verify --format json --verbose</c> printed the compiler phase lines
/// (<c>Lexer produced N tokens</c>, …); and <c>calor benchmark &lt;dir&gt;
/// --format json --verbose</c> printed <c>Benchmarked: name</c>. Found by
/// 0.25 W1 (PR #1566).</para>
/// </summary>
public class JsonVerboseStdoutTests : IDisposable
{
    private readonly string _tempDir;

    private const string CSharpSource =
        "namespace Demo;\n" +
        "public class Counter\n" +
        "{\n" +
        "    private int _count;\n" +
        "    public int Count => _count;\n" +
        "    public void Increment() { _count++; }\n" +
        "    public int Add(int a, int b) { return a + b; }\n" +
        "}\n";

    private const string CalorSource =
        "§M{m1:Demo}\n" +
        "  §F{f1:Add:pub} (i32:a, i32:b) -> i32\n" +
        "    §Q (>= a 0)\n" +
        "    §E{}\n" +
        "    §R (+ a b)\n" +
        "  §F{f2:Main:pub} () -> void\n" +
        "    §E{cw}\n" +
        "    §P \"hi\"\n";

    public JsonVerboseStdoutTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "calor-json-verbose-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "Counter.cs"), CSharpSource);
        File.WriteAllText(Path.Combine(_tempDir, "hello.calr"), CalorSource);

        // A project directory with one paired .calr/.cs (for benchmark) and a
        // C# file (for assess / analyze-convertibility).
        var proj = Path.Combine(_tempDir, "proj");
        Directory.CreateDirectory(proj);
        File.WriteAllText(Path.Combine(proj, "hello.calr"), CalorSource);
        File.WriteAllText(Path.Combine(proj, "hello.cs"), CSharpSource);
        File.WriteAllText(Path.Combine(proj, "Counter.cs"), CSharpSource);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Each row: the CLI arguments ('|'-separated) and a line of verbose
    /// progress text that must appear on stderr (the negative control: the
    /// text was moved, not dropped). An empty marker means the command prints
    /// no verbose text for this input; the row still pins the stdout contract.
    /// </summary>
    [Theory]
    [InlineData("-i|hello.calr|-o|out.g.cs|--format|json|--verbose", "Lexer produced")]
    [InlineData("-i|hello.calr|-o|out.g.cs|--format|sarif|--verbose", "Lexer produced")]
    [InlineData("convert|Counter.cs|-o|Counter.calr|--format|json|--verbose", "Converted 7 nodes")]
    [InlineData("convert|hello.calr|-o|hello.g.cs|--format|json|--verbose", "Lexer produced")]
    [InlineData("format|hello.calr|--format|json|--verbose", "Processed 1 file(s)")]
    [InlineData("lint|hello.calr|--format|json|--verbose", "")]
    [InlineData("lint|hello.calr|--format|sarif|--verbose", "")]
    [InlineData("verify|hello.calr|--format|json|--verbose", "Lexer produced")]
    [InlineData("ids|check|hello.calr|--format|json|--verbose", "")]
    [InlineData("assess|proj|--format|json|--verbose", "Analyzing (1/2)")]
    [InlineData("assess|proj|--format|sarif|--verbose", "Analyzing (1/2)")]
    [InlineData("analyze-convertibility|proj|--format|json|--verbose", "Analyzing proj")]
    [InlineData("benchmark|--calor|hello.calr|--csharp|Counter.cs|--format|json|--verbose", "Running full benchmark")]
    [InlineData("benchmark|proj|--format|json|--verbose", "Benchmarked: hello")]
    [InlineData("benchmark|proj|--quick|--format|json|--verbose", "Scanning project (quick mode)")]
    public void StructuredFormat_WithVerbose_StdoutIsOneJsonDocument(string args, string stderrMarker)
    {
        var (exitCode, stdOut, stdErr) = CliTestHarness.RunCli(_tempDir, args.Split('|'));

        JsonDocument? doc = null;
        try
        {
            doc = JsonDocument.Parse(stdOut);
        }
        catch (JsonException ex)
        {
            Assert.Fail($"stdout is not one JSON document ({ex.Message}). exit {exitCode}.\n" +
                        $"stdout:\n{stdOut}\nstderr:\n{stdErr}");
        }

        using (doc)
        {
            Assert.Equal(JsonValueKind.Object, doc!.RootElement.ValueKind);
        }

        if (stderrMarker.Length > 0)
        {
            Assert.Contains(stderrMarker, stdErr);
            Assert.DoesNotContain(stderrMarker, stdOut);
        }
    }

    /// <summary>
    /// <c>calor watch --format json --verbose</c> streams NDJSON: every stdout
    /// line must be one JSON document, and the verbose <c>Compiled:</c> line
    /// goes to stderr. Watch runs until interrupted, so the child is killed once
    /// stderr reports the initial compile (which is written after that
    /// rebuild's NDJSON line).
    /// </summary>
    [Fact]
    public void Watch_JsonVerbose_EveryStdoutLineIsOneJsonDocument()
    {
        var watchDir = Path.Combine(_tempDir, "watch");
        Directory.CreateDirectory(watchDir);
        File.WriteAllText(Path.Combine(watchDir, "hello.calr"), CalorSource);

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = _tempDir
        };
        foreach (var arg in new[] { CliTestHarness.FindCalorDll(), "--no-telemetry", "watch", watchDir, "--format", "json", "--verbose" })
        {
            psi.ArgumentList.Add(arg);
        }

        var stdOut = new System.Text.StringBuilder();
        var stdErr = new System.Text.StringBuilder();
        var initialDone = new ManualResetEventSlim(false);
        using var proc = new System.Diagnostics.Process { StartInfo = psi };
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) lock (stdOut) stdOut.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            lock (stdErr) stdErr.AppendLine(e.Data);
            if (e.Data.StartsWith("Initial compile:", StringComparison.Ordinal)) initialDone.Set();
        };
        Assert.True(proc.Start());
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        try
        {
            Assert.True(initialDone.Wait(TimeSpan.FromMinutes(2)),
                $"watch did not report its initial compile.\nstderr:\n{stdErr}");
        }
        finally
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* best-effort */ }
            // The parameterless overload also waits for the async stdout/stderr
            // readers to reach end-of-stream, so no buffered line is lost.
            if (proc.WaitForExit(30_000)) proc.WaitForExit();
        }

        string outText, errText;
        lock (stdOut) outText = stdOut.ToString();
        lock (stdErr) errText = stdErr.ToString();

        var lines = outText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.NotEmpty(lines);
        foreach (var line in lines)
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
            }
            catch (JsonException ex)
            {
                Assert.Fail($"stdout line is not a JSON document ({ex.Message}): {line}\nstderr:\n{errText}");
            }
        }

        Assert.Contains("Compiled:", errText);
        Assert.DoesNotContain("Compiled:", outText);
    }

    /// <summary>
    /// Text mode is unchanged: the same verbose progress lines stay on stdout.
    /// </summary>
    [Theory]
    [InlineData("convert|Counter.cs|-o|Counter.calr|--verbose", "Converted 7 nodes")]
    [InlineData("verify|hello.calr|--verbose", "Lexer produced")]
    [InlineData("benchmark|proj|--verbose", "Benchmarked: hello")]
    public void TextFormat_WithVerbose_KeepsProgressOnStdout(string args, string stdoutMarker)
    {
        var (_, stdOut, stdErr) = CliTestHarness.RunCli(_tempDir, args.Split('|'));

        Assert.True(stdOut.Contains(stdoutMarker, StringComparison.Ordinal),
            $"expected '{stdoutMarker}' on stdout.\nstdout:\n{stdOut}\nstderr:\n{stdErr}");
    }
}
