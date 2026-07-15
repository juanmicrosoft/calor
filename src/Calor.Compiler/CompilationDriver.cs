using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;

namespace Calor.Compiler;

/// <summary>
/// Shared multi-file compile orchestration used by the top-level compile command
/// (<see cref="Program"/>) and by <c>calor run</c> / <c>calor test</c>
/// (<c>ExecutionWorkspace</c>). One place owns the loop semantics:
/// <list type="bullet">
///   <item>Each file is compiled with options from <c>optionsFactory</c>.</item>
///   <item>Warnings are always printed to stderr — including warnings produced by
///     demotion under the permissive policy — not only when a file has errors.</item>
///   <item>Cross-module effect enforcement runs over the successfully compiled
///     modules regardless of whether other files failed, and honors the
///     <see cref="UnknownCallPolicy"/> (permissive demotes violations to warnings).</item>
/// </list>
/// </summary>
internal static class CompilationDriver
{
    internal sealed record FileResult(FileInfo File, CompilationResult Result);

    internal sealed record DriverResult(List<FileResult> Compiled, bool AnyErrors);

    /// <summary>
    /// Compiles all <paramref name="sources"/> in order. <paramref name="onCompiled"/>
    /// is invoked for each successfully compiled file (e.g. to write its output),
    /// before cross-module enforcement runs.
    /// </summary>
    /// <param name="crossModuleEnforcement">
    /// Whether to run cross-module effect enforcement when more than one module
    /// compiled successfully. The top-level compile command always passes true
    /// (its historical behavior); run/test pass their effective effects-enforcement
    /// setting.
    /// </param>
    internal static DriverResult CompileAll(
        IReadOnlyList<FileInfo> sources,
        Func<FileInfo, CompilationOptions> optionsFactory,
        bool crossModuleEnforcement,
        UnknownCallPolicy crossModulePolicy,
        Action<FileInfo, CompilationResult>? onCompiled = null)
    {
        var compiled = new List<FileResult>();
        var modules = new List<(ModuleNode Ast, string FilePath)>();
        var anyErrors = false;

        foreach (var file in sources)
        {
            var options = optionsFactory(file);
            if (options.Verbose)
            {
                Console.WriteLine($"Compiling: {file.FullName}");
            }

            var source = File.ReadAllText(file.FullName);
            var result = Program.Compile(source, file.FullName, options);

            PrintDiagnostics(result.Diagnostics, includeAll: result.HasErrors);

            if (result.HasErrors)
            {
                anyErrors = true;
                continue;
            }

            compiled.Add(new FileResult(file, result));
            onCompiled?.Invoke(file, result);

            if (result.Ast != null)
            {
                modules.Add((result.Ast, file.FullName));
            }
        }

        // Cross-module effect enforcement over successfully compiled modules —
        // runs even when other files failed, so all reportable violations surface
        // in one pass (top-level compile semantics).
        if (crossModuleEnforcement && modules.Count > 1)
        {
            var registry = CrossModuleEffectRegistry.Build(modules);
            foreach (var diagnostic in registry.BuildDiagnostics)
            {
                Console.Error.WriteLine(diagnostic);
            }

            var crossPass = new CrossModuleEffectEnforcementPass(crossModulePolicy);
            var crossDiagnostics = crossPass.Enforce(modules, registry);

            foreach (var diagnostic in crossDiagnostics)
            {
                Console.Error.WriteLine(diagnostic);
                if (diagnostic.IsError)
                {
                    anyErrors = true;
                }
            }
        }

        return new DriverResult(compiled, anyErrors);
    }

    /// <summary>
    /// Prints diagnostics to stderr. Errors and warnings are always printed;
    /// informational diagnostics are included only when the compilation failed
    /// (preserving the historical quiet-on-success output).
    /// </summary>
    private static void PrintDiagnostics(DiagnosticBag diagnostics, bool includeAll)
    {
        foreach (var diagnostic in diagnostics)
        {
            if (includeAll || diagnostic.IsError || diagnostic.IsWarning)
            {
                Console.Error.WriteLine(diagnostic);
            }
        }
    }

    /// <summary>
    /// Parses a --contract-mode CLI value ("off", "debug", "release"; case-insensitive).
    /// Unrecognized values fall back to <see cref="ContractMode.Debug"/>.
    /// </summary>
    internal static ContractMode ParseContractMode(string? contractMode) =>
        contractMode?.ToLowerInvariant() switch
        {
            "off" => ContractMode.Off,
            "release" => ContractMode.Release,
            _ => ContractMode.Debug
        };
}
