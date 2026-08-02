using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Reporting;

namespace Calor.Compiler.Commands;

/// <summary>
/// <c>calor review-packet</c> — the verification centerpiece's adoption
/// artifact (wedge plan v0.11 D-W3.3): a per-change report that LEADS with
/// the unproven remainder over the seven-status vocabulary, with assumption
/// lists, vacuity flags, per-module interop/permissive fractions, waiver
/// disclosures on the first line (strategy §5.2), caller impact from the
/// in-memory call graph, and the #782 refinement honesty note.
/// </summary>
public static class ReviewPacketCommand
{
    public static Command Create()
    {
        var filesArgument = new Argument<FileInfo[]>(
            name: "files",
            description: "The Calor source file(s) the packet covers")
        {
            Arity = ArgumentArity.OneOrMore
        };

        var changedOption = new Option<string[]>(
            aliases: ["--changed"],
            description: "Declaration id or name treated as changed (repeatable); drives the caller-impact section")
        { Arity = ArgumentArity.ZeroOrMore, AllowMultipleArgumentsPerToken = true };

        var baselineRefOption = new Option<string?>(
            aliases: ["--baseline-ref"],
            description: "Git ref to diff against; changed declarations are derived from the diff's line ranges");

        var permissiveOption = new Option<bool>(
            aliases: ["--permissive-effects"],
            description: "The module is built with permissive effects — disclosed as a waiver on the packet's first line");

        var contractModeOption = new Option<string>(
            aliases: ["--contract-mode"],
            getDefaultValue: () => "debug",
            description: "Contract mode the module is built with: debug, release, or off (off = waiver, disclosed first)");

        var timeoutOption = new Option<int>(
            aliases: ["--timeout", "-t"],
            getDefaultValue: () => 5000,
            description: "Z3 solver timeout per contract in milliseconds");

        var outputOption = new Option<FileInfo?>(
            aliases: ["--output", "-o"],
            description: "Write the markdown packet to a file (default: stdout)");

        var jsonOption = new Option<bool>(
            aliases: ["--json"],
            description: "Emit the envelope document (packet under data) to stdout");

        var command = new Command("review-packet",
            "Assemble a per-change review packet that leads with the unproven remainder")
        {
            filesArgument,
            changedOption,
            baselineRefOption,
            permissiveOption,
            contractModeOption,
            timeoutOption,
            outputOption,
            jsonOption
        };

        // Exit code returned through ctx.ExitCode: a code parked only on
        // Environment.ExitCode is overwritten by Main's InvokeAsync return.
        command.SetHandler((InvocationContext ctx) =>
        {
            ctx.ExitCode = Execute(
                ctx.ParseResult.GetValueForArgument(filesArgument),
                ctx.ParseResult.GetValueForOption(changedOption) ?? [],
                ctx.ParseResult.GetValueForOption(baselineRefOption),
                ctx.ParseResult.GetValueForOption(permissiveOption),
                ctx.ParseResult.GetValueForOption(contractModeOption) ?? "debug",
                (uint)Math.Max(1, ctx.ParseResult.GetValueForOption(timeoutOption)),
                ctx.ParseResult.GetValueForOption(outputOption),
                ctx.ParseResult.GetValueForOption(jsonOption));
        });

        return command;
    }

    private static int Execute(
        FileInfo[] files,
        string[] changed,
        string? baselineRef,
        bool permissiveEffects,
        string contractMode,
        uint timeoutMs,
        FileInfo? output,
        bool json)
    {
        try
        {
            var inputs = new List<ReviewPacketBuilder.InputFile>();
            foreach (var file in files)
            {
                if (!file.Exists)
                {
                    var message = $"File not found: {file.FullName}";
                    if (json)
                    {
                        Console.WriteLine(EnvelopeWriter.Serialize("review-packet", null,
                            [new Diagnostic(DiagnosticCode.ReviewPacketCommandError, DiagnosticSeverity.Error,
                                message, file.FullName, line: 1, column: 1)]));
                    }
                    Console.Error.WriteLine($"Error: {message}");
                    return 1;
                }
                inputs.Add(new ReviewPacketBuilder.InputFile(file.FullName, File.ReadAllText(file.FullName)));
            }

            IReadOnlyDictionary<string, IReadOnlyList<(int, int)>>? changedRanges = null;
            if (baselineRef is not null)
            {
                changedRanges = ComputeChangedLineRanges(files, baselineRef, out var gitError);
                if (gitError is not null)
                {
                    if (json)
                    {
                        Console.WriteLine(EnvelopeWriter.Serialize("review-packet", null,
                            [new Diagnostic(DiagnosticCode.ReviewPacketCommandError, DiagnosticSeverity.Error,
                                gitError, filePath: null, line: 1, column: 1)]));
                    }
                    Console.Error.WriteLine($"Error: {gitError}");
                    return 1;
                }
            }

            var contractChecksOff = contractMode.Equals("off", StringComparison.OrdinalIgnoreCase);
            var result = ReviewPacketBuilder.Build(inputs, new ReviewPacketBuilder.Options(
                PermissiveEffects: permissiveEffects,
                ContractChecksOff: contractChecksOff,
                VerificationTimeoutMs: timeoutMs,
                ChangedDeclarations: changed.Length > 0 ? changed : null,
                ChangedLineRanges: changedRanges,
                BaselineRef: baselineRef));

            var diagnostics = new List<Diagnostic>(result.CompileDiagnostics);
            if (result.Packet.Waivers.Count > 0)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCode.ReviewPacketWaiverDisclosure, DiagnosticSeverity.Warning,
                    string.Join(" ", result.Packet.Waivers),
                    filePath: null, line: 1, column: 1));
            }
            if (result.AnyRefinementTypes)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCode.ReviewPacketRefinementHonesty, DiagnosticSeverity.Info,
                    ReviewPacketBuilder.RefinementHonestyNote,
                    filePath: null, line: 1, column: 1));
            }

            if (json)
            {
                Console.WriteLine(EnvelopeWriter.Serialize("review-packet", result.Packet, diagnostics));
                return result.HasCompileErrors ? 1 : 0;
            }

            var markdown = ReviewPacketBuilder.RenderMarkdown(result.Packet);
            if (output is not null)
            {
                File.WriteAllText(output.FullName, markdown);
                Console.WriteLine($"Review packet written to {output.FullName}");
            }
            else
            {
                Console.WriteLine(markdown);
            }

            if (result.HasCompileErrors)
            {
                Console.Error.WriteLine("Warning: one or more files failed to compile; the packet covers only the files that compiled.");
                foreach (var diagnostic in result.CompileDiagnostics.Where(d => d.IsError).Take(20))
                    Console.Error.WriteLine($"  {diagnostic}");
                return 1;
            }

            return 0;
        }
        catch (Exception ex)
        {
            if (json)
            {
                Console.WriteLine(EnvelopeWriter.Serialize("review-packet", null,
                    [new Diagnostic(DiagnosticCode.ReviewPacketCommandError, DiagnosticSeverity.Error,
                        $"Review packet failed: {ex.Message}", filePath: null, line: 1, column: 1)]));
            }
            Console.Error.WriteLine($"Error: Review packet failed: {ex.Message}");
            return 1;
        }
    }

    // ------------------------------------------------------------------
    // Git diff → changed line ranges
    // ------------------------------------------------------------------

    private static readonly Regex HunkHeader = new(
        @"^@@ -\d+(?:,\d+)? \+(?<start>\d+)(?:,(?<count>\d+))? @@",
        RegexOptions.Compiled);

    private static IReadOnlyDictionary<string, IReadOnlyList<(int, int)>>? ComputeChangedLineRanges(
        FileInfo[] files,
        string baselineRef,
        out string? error)
    {
        error = null;
        var result = new Dictionary<string, IReadOnlyList<(int, int)>>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = file.DirectoryName ?? Directory.GetCurrentDirectory(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("diff");
            startInfo.ArgumentList.Add("-U0");
            startInfo.ArgumentList.Add(baselineRef);
            startInfo.ArgumentList.Add("--");
            startInfo.ArgumentList.Add(file.FullName);

            try
            {
                using var process = Process.Start(startInfo);
                if (process is null)
                {
                    error = "Failed to start git for --baseline-ref diff.";
                    return null;
                }
                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode is not (0 or 1))
                {
                    error = $"git diff against '{baselineRef}' failed: {stderr.Trim()}";
                    return null;
                }

                var ranges = new List<(int, int)>();
                foreach (var line in stdout.Split('\n'))
                {
                    var match = HunkHeader.Match(line);
                    if (!match.Success)
                        continue;
                    var start = int.Parse(match.Groups["start"].Value);
                    var count = match.Groups["count"].Success ? int.Parse(match.Groups["count"].Value) : 1;
                    if (count == 0)
                    {
                        // Pure deletion at this position: treat the surrounding
                        // line as changed so the enclosing declaration is flagged.
                        ranges.Add((Math.Max(1, start), Math.Max(1, start)));
                    }
                    else
                    {
                        ranges.Add((start, start + count - 1));
                    }
                }

                if (ranges.Count > 0)
                    result[file.FullName] = ranges;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
            {
                error = $"Failed to run git for --baseline-ref: {ex.Message}";
                return null;
            }
        }

        return result;
    }
}
