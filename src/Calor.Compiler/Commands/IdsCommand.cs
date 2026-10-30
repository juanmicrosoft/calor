using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Ids;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Commands;

/// <summary>CLI commands for tooling-owned declaration identities.</summary>
public static class IdsCommand
{
    public static Command Create()
    {
        var command = new Command("ids", "Manage tooling-owned declaration IDs; use ids sync PROJECT --register FILE")
        {
            CreateCheckCommand(),
            CreateIndexCommand(),
            CreateSyncCommand()
        };
        command.AddValidator(result =>
        {
            if (result.Tokens.Count > 0 && result.Tokens[0].Value == "assign")
                result.ErrorMessage = "ids assign is retired. Run calor ids sync PROJECT --register FILE; IDs are never written to source.";
        });
        return command;
    }

    private static Command CreateSyncCommand()
    {
        var root = new Argument<string>("project", "Project root containing .calor/ids.json");
        var files = new Option<string[]>("--register", "Explicit active .calr files to register (never discovers historical fixtures)")
        {
            AllowMultipleArgumentsPerToken = true
        };
        var command = new Command("sync", "Synchronize declaration identities without rewriting source") { root, files };
        command.SetHandler((InvocationContext context) =>
        {
            try
            {
                var store = new DeclarationIdentityStore(context.ParseResult.GetValueForArgument(root));
                var state = store.Sync(context.ParseResult.GetValueForOption(files));
                Console.WriteLine($"Synchronized {state.Declarations.Count} declarations in {state.Files.Count} registered files.");
            }
            catch (Exception ex) when (IsIdentityFailure(ex))
            {
                Console.Error.WriteLine($"Identity sync failed: {ex.Message}");
                context.ExitCode = 1;
            }
        });
        return command;
    }

    private static Command CreateCheckCommand()
    {
        var paths = new Argument<string[]>("paths", "Active .calr files or directories of registered files")
        {
            Arity = ArgumentArity.OneOrMore
        };
        var allowTestIds = new Option<bool>("--allow-test-ids", "Allow test identities in the sidecar");
        var verbose = new Option<bool>(["--verbose", "-v"], "Show detailed output");
        var format = new Option<string>(["--format", "-f"], () => "text", "Output format: text or json (envelope v1.1)");
        var command = new Command("check", "Synchronize and validate sidecar declaration identities")
        {
            paths, allowTestIds, verbose, format
        };
        command.SetHandler(async (InvocationContext context) =>
        {
            context.ExitCode = await CheckAsync(
                context.ParseResult.GetValueForArgument(paths),
                context.ParseResult.GetValueForOption(allowTestIds),
                context.ParseResult.GetValueForOption(verbose),
                context.ParseResult.GetValueForOption(format) ?? "text");
        });
        return command;
    }

    private static Command CreateIndexCommand()
    {
        var paths = new Argument<string[]>("paths", "Active .calr files or directories of registered files")
        {
            Arity = ArgumentArity.OneOrMore
        };
        var output = new Option<FileInfo?>(["--output", "-o"], "Output file path (default: calor.ids.json)");
        var command = new Command("index", "Synchronize and export sidecar declaration identities") { paths, output };
        command.SetHandler(async (InvocationContext context) =>
        {
            var diagnostics = new DiagnosticBag();
            var entries = await ReadEntriesAsync(context.ParseResult.GetValueForArgument(paths), diagnostics);
            if (diagnostics.HasErrors)
            {
                foreach (var diagnostic in diagnostics) Console.Error.WriteLine(diagnostic);
                context.ExitCode = 1;
                return;
            }

            var outputPath = context.ParseResult.GetValueForOption(output)?.FullName ?? "calor.ids.json";
            try
            {
                // An export is not a second identity store and must never overwrite
                // source or the authoritative sidecar (including through aliases).
                var destination = CanonicalPath.Resolve(outputPath);
                if (Path.GetExtension(destination).Equals(".calr", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Path.GetFileName(Path.GetDirectoryName(destination)), ".calor", StringComparison.OrdinalIgnoreCase)
                    || entries.Any(entry => CanonicalPath.Resolve(entry.FilePath) == destination))
                    throw new IOException("Index output must not overwrite source or .calor/ids.json.");

                var json = JsonSerializer.Serialize(entries.Select(entry => new
                {
                    id = entry.Id,
                    kind = entry.Kind.ToString(),
                    name = entry.Name,
                    file = Path.GetRelativePath(Directory.GetCurrentDirectory(), entry.FilePath),
                    line = entry.Span.Line
                }), new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(outputPath, json);
                Console.WriteLine($"Indexed {entries.Count} ID(s) to {outputPath}");
            }
            catch (Exception ex) when (IsIdentityFailure(ex))
            {
                Console.Error.WriteLine($"Identity index failed: {ex.Message}");
                context.ExitCode = 1;
            }
        });
        return command;
    }

    private static async Task<int> CheckAsync(string[] paths, bool allowTestIds, bool verbose, string format)
    {
        var diagnostics = new DiagnosticBag();
        var resolver = new DeclarationIdResolver();
        var entries = await ReadEntriesAsync(paths, diagnostics, resolver);
        var result = IdChecker.Check(entries, allowTestIds);
        foreach (var diagnostic in IdChecker.GenerateDiagnostics(result)) diagnostics.Add(diagnostic);

        if (format.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(EnvelopeWriter.Serialize("ids", new
            {
                totalIds = entries.Count,
                missing = result.MissingIds.Count,
                invalidFormat = result.InvalidFormatIds.Count,
                wrongPrefix = result.WrongPrefixIds.Count,
                testIds = result.TestIdsInProduction.Count,
                duplicateGroups = result.DuplicateGroups.Count
            }, diagnostics, resolver));
        }
        else
        {
            foreach (var diagnostic in diagnostics.OrderBy(d => d.FilePath).ThenBy(d => d.Span.Line))
                Console.Error.WriteLine(diagnostic);
            if (!diagnostics.HasErrors) Console.WriteLine($"All {entries.Count} IDs are valid.");
            else if (verbose) Console.Error.WriteLine($"Identity check failed with {diagnostics.Errors.Count} error(s).");
        }
        return diagnostics.HasErrors ? 1 : 0;
    }

    private static bool IsIdentityFailure(Exception ex) =>
        ex is IOException or InvalidOperationException or JsonException or UnauthorizedAccessException or ArgumentException;

    private static async Task<List<IdEntry>> ReadEntriesAsync(
        string[] paths, DiagnosticBag diagnostics, DeclarationIdResolver? resolver = null)
    {
        var entries = new List<IdEntry>();
        try
        {
            var files = CollectFiles(paths);
            if (files.Count == 0)
            {
                diagnostics.Add(new Diagnostic(DiagnosticCode.CliInputNotFound, DiagnosticSeverity.Error,
                    "No active .calr files found. Pass active files explicitly or run calor ids sync PROJECT --register FILE.",
                    null, 1, 1));
                return entries;
            }
            DeclarationIdentityStore.SyncActiveInputs(files);
            foreach (var file in files)
            {
                var source = await File.ReadAllTextAsync(file);
                var parseDiagnostics = new DiagnosticBag();
                parseDiagnostics.SetFilePath(file);
                var module = new Parser(new Lexer(source, parseDiagnostics).TokenizeAllForParser(), parseDiagnostics).Parse();
                foreach (var diagnostic in parseDiagnostics) diagnostics.Add(diagnostic);
                if (parseDiagnostics.HasErrors) continue;

                var store = DeclarationIdentityStore.Find(file);
                if (store == null || !store.IsRegistered(file))
                    throw new InvalidOperationException($"No persisted identity for '{file}'. Run calor ids sync PROJECT --register FILE in a dedicated project directory.");
                store.Attach(file, source, module);
                resolver?.AddFile(file, source, module);
                entries.AddRange(new IdScanner().Scan(module, file));
            }
        }
        catch (DeclarationIdentityParseException ex)
        {
            foreach (var diagnostic in ex.Diagnostics) diagnostics.Add(diagnostic);
        }
        catch (FileNotFoundException ex)
        {
            diagnostics.Add(new Diagnostic(DiagnosticCode.CliInputNotFound, DiagnosticSeverity.Error,
                ex.Message, ex.FileName, 1, 1));
        }
        catch (Exception ex) when (IsIdentityFailure(ex))
        {
            diagnostics.Add(new Diagnostic(DiagnosticCode.DeclarationIdentityUnavailable, DiagnosticSeverity.Error,
                $"Declaration identity unavailable: {ex.Message}", null, 1, 1));
        }
        return entries;
    }

    private static List<string> CollectFiles(string[] paths)
    {
        var files = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var full = Path.GetFullPath(path);
            if (File.Exists(full) && full.EndsWith(".calr", StringComparison.OrdinalIgnoreCase))
            {
                files.Add(full);
                continue;
            }
            if (!Directory.Exists(full)) throw new FileNotFoundException($"Input not found: {path}");

            // Directory arguments select registered inputs only. Never recursively
            // enroll fixtures, archives, build output or symlinked directories.
            if (DeclarationIdentityStore.IsAmbientRoot(full)) continue;
            var store = DeclarationIdentityStore.Find(Path.Combine(full, "__ids_input__.calr"));
            if (store == null || !File.Exists(store.SidecarPath)) continue;
            // Read only the registration snapshot to select actual inputs. Finding
            // an ancestor store alone must not trigger recovery of its journal.
            var snapshot = JsonSerializer.Deserialize<DeclarationIdentityStore.State>(
                File.ReadAllText(store.SidecarPath))
                ?? throw new JsonException("Invalid declaration identity registration snapshot.");
            if (snapshot.Files == null)
                throw new JsonException("Missing declaration identity file registrations.");
            var selected = new List<string>();
            foreach (var relative in snapshot.Files)
            {
                if (string.IsNullOrEmpty(relative) || Path.IsPathRooted(relative)
                    || relative.Replace('\\', '/').Split('/').Contains("..", StringComparer.Ordinal))
                    throw new JsonException("Invalid declaration identity file registration.");
                var file = Path.GetFullPath(Path.Combine(store.Root, relative));
                if (File.Exists(file) && CanonicalPath.IsUnder(CanonicalPath.Resolve(file), CanonicalPath.Resolve(full)))
                    selected.Add(file);
            }
            if (selected.Count == 0) continue;
            store.Sync(selected);
            foreach (var file in selected) files.Add(file);
        }
        return files.Order(StringComparer.Ordinal).ToList();
    }
}
