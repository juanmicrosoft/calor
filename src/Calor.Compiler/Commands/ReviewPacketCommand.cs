using System.CommandLine;
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
        var filesArgument = new Argument<FileInfo[]>("files")
        {
            Description = "The Calor source file(s) the packet covers",
            Arity = ArgumentArity.OneOrMore
        };

        var changedOption = new Option<string[]>("--changed")
        {
            Description = "Declaration id or name treated as changed (repeatable); drives the caller-impact section",
            Arity = ArgumentArity.ZeroOrMore,
            AllowMultipleArgumentsPerToken = true
        };

        var baselineRefOption = new Option<string?>("--baseline-ref")
        {
            Description = "Git ref to diff against; changed declarations are derived from the diff's line ranges. "
                + "Approximation: a declaration's extent runs to the next declaration's start, so an edit between "
                + "declarations attributes to the preceding one (conservative over-inclusion, never omission)"
        };

        var permissiveOption = new Option<bool>("--permissive-effects")
        {
            Description = "The module is built with permissive effects — disclosed as a waiver on the packet's first line"
        };

        var contractModeOption = new Option<string>("--contract-mode")
        {
            Description = "Contract mode the module is built with: debug, release, or off (off = waiver, disclosed first)",
            DefaultValueFactory = _ => "debug"
        };

        var timeoutOption = new Option<int>("--timeout", "-t")
        {
            Description = "Z3 solver timeout per contract in milliseconds",
            DefaultValueFactory = _ => 5000
        };

        var outputOption = new Option<FileInfo?>("--output", "-o")
        {
            Description = "Write the markdown packet to a file (default: stdout)"
        };

        var jsonOption = new Option<bool>("--json")
        {
            Description = "Emit the envelope document (packet under data) to stdout"
        };

        var referenceOption = new Option<FileInfo[]>("--reference", "-r")
        {
            Description = "Assembly reference: calls on its types bind to their metadata, and generated C# is validated against it (repeatable)",
            AllowMultipleArgumentsPerToken = true
        };

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
            jsonOption,
            referenceOption
        };

        // Exit code returned from the action: a code parked only on
        // Environment.ExitCode is overwritten by Main's InvokeAsync return.
        command.SetAction(parseResult =>
        {
            return Execute(
                parseResult.GetValue(filesArgument)!,
                parseResult.GetValue(changedOption) ?? [],
                parseResult.GetValue(baselineRefOption),
                parseResult.GetValue(permissiveOption),
                parseResult.GetValue(contractModeOption) ?? "debug",
                (uint)Math.Max(1, parseResult.GetValue(timeoutOption)),
                parseResult.GetValue(outputOption),
                parseResult.GetValue(jsonOption),
                parseResult.GetValue(referenceOption) ?? []);
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
        bool json,
        FileInfo[] references)
    {
        try
        {
            var inputs = new List<ReviewPacketBuilder.InputFile>();
            foreach (var reference in references)
            {
                if (reference.Exists)
                    continue;

                var message = $"Reference assembly not found: {reference.FullName}";
                if (json)
                {
                    Console.WriteLine(EnvelopeWriter.Serialize("review-packet", null,
                        [new Diagnostic(DiagnosticCode.ReviewPacketCommandError,
                            DiagnosticSeverity.Error, message, reference.FullName,
                            line: 1, column: 1)]));
                }
                Console.Error.WriteLine($"Error: {message}");
                return 1;
            }
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
                BaselineRef: baselineRef,
                ReferencedAssemblyPaths: references.Select(reference => reference.FullName).ToList()));

            var diagnostics = new List<Diagnostic>(result.CompileDiagnostics);
            foreach (var selector in result.UnmatchedChangedDeclarations)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCode.ReviewPacketUnknownChangedDeclaration, DiagnosticSeverity.Warning,
                    $"--changed '{selector}' matched no declaration id or name in the given files — "
                    + "its caller impact is absent from this packet. Check for a typo.",
                    filePath: null, line: 1, column: 1));
            }
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

            foreach (var selector in result.UnmatchedChangedDeclarations)
            {
                Console.Error.WriteLine(
                    $"Warning: --changed '{selector}' matched no declaration id or name in the given files "
                    + "— its caller impact is absent from this packet. Check for a typo.");
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
