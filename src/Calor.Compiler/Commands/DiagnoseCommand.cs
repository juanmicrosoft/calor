using System.CommandLine;
using System.Diagnostics;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Init;
using Calor.Compiler.Telemetry;

namespace Calor.Compiler.Commands;

/// <summary>
/// CLI command for outputting machine-readable diagnostics.
/// This enables automated fix application by AI agents and tools.
/// </summary>
public static class DiagnoseCommand
{
    public static Command Create()
    {
        var inputArgument = new Argument<FileInfo[]>(
            name: "files",
            description: "The Calor source file(s) to diagnose")
        {
            Arity = ArgumentArity.OneOrMore
        };

        var formatOption = new Option<string>(
            aliases: ["--format", "-f"],
            getDefaultValue: () => "text",
            description: "Output format: text, json, or sarif");

        var outputOption = new Option<FileInfo?>(
            aliases: ["--output", "-o"],
            description: "Output file (stdout if not specified)");

        var strictApiOption = new Option<bool>(
            aliases: ["--strict-api"],
            description: "Enable strict API checking");

        var requireDocsOption = new Option<bool>(
            aliases: ["--require-docs"],
            description: "Require documentation on public functions");

        var command = new Command("diagnose", "Output machine-readable diagnostics for Calor files")
        {
            inputArgument,
            formatOption,
            outputOption,
            strictApiOption,
            requireDocsOption
        };

        command.SetHandler(ExecuteAsync, inputArgument, formatOption, outputOption, strictApiOption, requireDocsOption);

        return command;
    }

    private static async Task ExecuteAsync(
        FileInfo[] files,
        string format,
        FileInfo? output,
        bool strictApi,
        bool requireDocs)
    {
        var telemetry = CalorTelemetry.IsInitialized ? CalorTelemetry.Instance : null;
        telemetry?.SetCommand("diagnose");
        if (telemetry != null && files.Length > 0)
        {
            var discovered = CalorConfigManager.Discover(files[0].FullName);
            telemetry.SetAgents(CalorConfigManager.GetAgentString(discovered?.Config));
        }
        var sw = Stopwatch.StartNew();

        var allDiagnostics = new DiagnosticBag();

        foreach (var file in files)
        {
            if (!file.Exists)
            {
                Console.Error.WriteLine($"Error: File not found: {file.FullName}");
                Environment.ExitCode = 1;
                continue;
            }

            if (!file.Extension.Equals(".calr", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"Warning: Skipping non-Calor file: {file.Name}");
                continue;
            }

            try
            {
                var source = await File.ReadAllTextAsync(file.FullName);
                var options = new CompilationOptions
                {
                    StrictApi = strictApi,
                    RequireDocs = requireDocs
                };
                var result = Program.Compile(source, file.FullName, options);
                allDiagnostics.AddRange(result.Diagnostics);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error processing {file.Name}: {ex.Message}");
                Environment.ExitCode = 2;
            }
        }

        // Format output (use DiagnosticBag overload to include fix information)
        var formatter = DiagnosticFormatterFactory.Create(format);
        var formatted = formatter.Format(allDiagnostics);

        // Write output
        if (output != null)
        {
            await File.WriteAllTextAsync(output.FullName, formatted);
            Console.Error.WriteLine($"Diagnostics written to: {output.FullName}");
        }
        else
        {
            Console.WriteLine(formatted);
        }

        // Set exit code based on errors
        if (allDiagnostics.Any(d => d.IsError))
        {
            Environment.ExitCode = 1;
        }

        sw.Stop();
        telemetry?.TrackCommand("diagnose", Environment.ExitCode, new Dictionary<string, string>
        {
            ["durationMs"] = sw.ElapsedMilliseconds.ToString(),
            ["diagnosticCount"] = allDiagnostics.Count.ToString()
        });
        if (Environment.ExitCode != 0)
        {
            IssueReporter.PromptForIssue(telemetry?.OperationId ?? "unknown", "diagnose", "Diagnostics found errors");
        }
    }
}
