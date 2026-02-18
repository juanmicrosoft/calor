using System.CommandLine;
using System.Diagnostics;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Init;
using Calor.Compiler.Telemetry;
using Microsoft.CodeAnalysis.CSharp;

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

        var validateCodegenOption = new Option<bool>(
            aliases: ["--validate-codegen"],
            description: "Validate generated C# code for syntax errors");

        var command = new Command("diagnose", "Output machine-readable diagnostics for Calor files")
        {
            inputArgument,
            formatOption,
            outputOption,
            strictApiOption,
            requireDocsOption,
            validateCodegenOption
        };

        command.SetHandler(ExecuteAsync, inputArgument, formatOption, outputOption, strictApiOption, requireDocsOption, validateCodegenOption);

        return command;
    }

    private static async Task ExecuteAsync(
        FileInfo[] files,
        string format,
        FileInfo? output,
        bool strictApi,
        bool requireDocs,
        bool validateCodegen)
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

                // Validate generated C# if requested
                if (validateCodegen && !string.IsNullOrEmpty(result.GeneratedCode))
                {
                    var tree = CSharpSyntaxTree.ParseText(result.GeneratedCode);
                    foreach (var roslynDiag in tree.GetDiagnostics())
                    {
                        if (roslynDiag.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                        {
                            var lineSpan = roslynDiag.Location.GetLineSpan();
                            var span = new Parsing.TextSpan(
                                0,
                                roslynDiag.Location.SourceSpan.Length,
                                lineSpan.StartLinePosition.Line + 1,
                                lineSpan.StartLinePosition.Character + 1);
                            allDiagnostics.Report(
                                span,
                                DiagnosticCode.CodeGenSyntaxError,
                                $"Generated C# syntax error: {roslynDiag.GetMessage()}",
                                DiagnosticSeverity.Warning);
                        }
                    }
                }
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
