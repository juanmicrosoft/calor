using System.Diagnostics;
using System.Security;
using Calor.Compiler.Effects;

namespace Calor.Compiler.Commands;

/// <summary>
/// Shared workspace materialization for the <c>calor run</c> and <c>calor test</c>
/// commands. Compiles .calr sources in-process (effects enforcement on by default,
/// same defaults as <see cref="Program.Compile(string, string, CompilationOptions?)"/>),
/// writes the generated C# into a temporary project referencing the Calor.Runtime
/// assembly that ships alongside the compiler, and shells out to the dotnet CLI
/// to build/run/test it. No hand-wired MSBuild required.
/// </summary>
internal static class ExecutionWorkspace
{
    internal const string DotnetMissingMessage =
        "Error: the 'dotnet' CLI was not found on PATH. " +
        "The .NET SDK (10.0 or later) is required to execute Calor programs. " +
        "Install it from https://dotnet.microsoft.com/download and ensure 'dotnet' is on PATH.";

    internal sealed record CompiledUnit(string FileName, string GeneratedCode);

    /// <summary>
    /// Resolves a path argument (a .calr file or a directory containing .calr files)
    /// to the list of source files. Returns null and sets <paramref name="error"/>
    /// when the path is invalid or contains no Calor sources.
    /// </summary>
    internal static List<FileInfo>? ResolveSources(string path, out string error)
    {
        error = string.Empty;

        if (File.Exists(path))
        {
            if (!path.EndsWith(".calr", StringComparison.OrdinalIgnoreCase))
            {
                error = $"Not a Calor source file (expected .calr extension): {path}";
                return null;
            }

            return [new FileInfo(Path.GetFullPath(path))];
        }

        if (Directory.Exists(path))
        {
            var sources = Directory.EnumerateFiles(path, "*.calr", SearchOption.AllDirectories)
                .Where(f => !IsInExcludedDirectory(path, f))
                .OrderBy(f => f, StringComparer.Ordinal)
                .Select(f => new FileInfo(Path.GetFullPath(f)))
                .ToList();

            if (sources.Count == 0)
            {
                error = $"No .calr files found in directory: {path}";
                return null;
            }

            return sources;
        }

        error = $"File or directory not found: {path}";
        return null;
    }

    private static bool IsInExcludedDirectory(string root, string file)
    {
        var relative = Path.GetRelativePath(root, file);
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(s =>
            s.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("tests", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Compiles all sources in-process. Effects enforcement is ON by default;
    /// <paramref name="permissive"/> switches unknown-call handling to the
    /// permissive policy (unknown calls assumed pure, forbidden effects demoted
    /// to warnings). Prints diagnostics to stderr and returns null on errors.
    /// </summary>
    internal static List<CompiledUnit>? CompileSources(IReadOnlyList<FileInfo> sources, bool permissive, bool verbose)
    {
        var units = new List<CompiledUnit>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var modules = new List<(Ast.ModuleNode Ast, string FilePath)>();
        var anyErrors = false;

        foreach (var file in sources)
        {
            if (verbose)
            {
                Console.WriteLine($"Compiling: {file.FullName}");
            }

            var source = File.ReadAllText(file.FullName);
            var options = new CompilationOptions
            {
                Verbose = verbose,
                EnforceEffects = true,
                UnknownCallPolicy = permissive ? UnknownCallPolicy.Permissive : UnknownCallPolicy.Strict,
                ProjectDirectory = Path.GetDirectoryName(file.FullName)
            };

            var result = Program.Compile(source, file.FullName, options);
            if (result.HasErrors)
            {
                foreach (var diagnostic in result.Diagnostics)
                {
                    Console.Error.WriteLine(diagnostic);
                }

                anyErrors = true;
                continue;
            }

            var baseName = Path.GetFileNameWithoutExtension(file.Name);
            var fileName = $"{baseName}.g.cs";
            for (var i = 2; !usedNames.Add(fileName); i++)
            {
                fileName = $"{baseName}_{i}.g.cs";
            }

            units.Add(new CompiledUnit(fileName, result.GeneratedCode));

            if (result.Ast != null)
            {
                modules.Add((result.Ast, file.FullName));
            }
        }

        // Cross-module effect enforcement, same as the top-level compile command.
        if (!anyErrors && modules.Count > 1)
        {
            var registry = CrossModuleEffectRegistry.Build(modules);
            foreach (var diagnostic in registry.BuildDiagnostics)
            {
                Console.Error.WriteLine(diagnostic);
            }

            var crossDiagnostics = new CrossModuleEffectEnforcementPass().Enforce(modules, registry);
            foreach (var diagnostic in crossDiagnostics)
            {
                Console.Error.WriteLine(diagnostic);
                if (diagnostic.IsError)
                {
                    anyErrors = true;
                }
            }
        }

        return anyErrors ? null : units;
    }

    /// <summary>
    /// Creates an isolated temp workspace. Empty Directory.Build.props/targets
    /// insulate the materialized projects from any repository-level MSBuild
    /// customization above the temp directory.
    /// </summary>
    internal static string CreateWorkspace(string prefix)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Directory.Build.props"), "<Project />");
        File.WriteAllText(Path.Combine(dir, "Directory.Build.targets"), "<Project />");
        return dir;
    }

    /// <summary>
    /// Writes the generated C# files and a .csproj into <paramref name="projectDir"/>.
    /// The project references the Calor.Runtime assembly shipped next to the running
    /// compiler (works both for repo dev builds, where the ProjectReference copies it
    /// to the compiler output, and for the installed global tool).
    /// </summary>
    internal static string WriteProject(string projectDir, string projectName, bool executable, IReadOnlyList<CompiledUnit> units)
    {
        Directory.CreateDirectory(projectDir);
        foreach (var unit in units)
        {
            File.WriteAllText(Path.Combine(projectDir, unit.FileName), unit.GeneratedCode);
        }

        var runtimePath = Path.Combine(AppContext.BaseDirectory, "Calor.Runtime.dll");
        var runtimeReference = File.Exists(runtimePath)
            ? $"""
                 <ItemGroup>
                   <Reference Include="Calor.Runtime">
                     <HintPath>{SecurityElement.Escape(runtimePath)}</HintPath>
                     <Private>true</Private>
                   </Reference>
                 </ItemGroup>
               """
            : "  <!-- Calor.Runtime.dll not found next to the compiler; runtime types unavailable -->";

        var csproj = $"""
            <Project Sdk="Microsoft.NET.Sdk">

              <PropertyGroup>
                <OutputType>{(executable ? "Exe" : "Library")}</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <AssemblyName>{projectName}</AssemblyName>
              </PropertyGroup>

            {runtimeReference}

            </Project>
            """;

        var csprojPath = Path.Combine(projectDir, $"{projectName}.csproj");
        File.WriteAllText(csprojPath, csproj);
        return csprojPath;
    }

    /// <summary>
    /// Locates the dotnet CLI on PATH (or DOTNET_ROOT). Returns null when the
    /// .NET SDK is not installed.
    /// </summary>
    internal static string? FindDotnet()
    {
        var exeName = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";

        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), exeName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // Malformed PATH entry — skip it.
            }
        }

        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(dotnetRoot))
        {
            var candidate = Path.Combine(dotnetRoot, exeName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Runs a process with inherited stdio (output streams directly to the console)
    /// and returns its exit code.
    /// </summary>
    internal static int RunProcess(string fileName, IReadOnlyList<string> arguments, string workingDirectory)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false
        };

        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi);
        if (process == null)
        {
            Console.Error.WriteLine($"Error: failed to start process: {fileName}");
            return 2;
        }

        process.WaitForExit();
        return process.ExitCode;
    }

    /// <summary>
    /// Runs a process capturing its combined output. Used for build steps so
    /// MSBuild noise stays hidden unless the build fails (or --verbose is set),
    /// in which case the full output is replayed.
    /// </summary>
    internal static int RunProcessCaptured(string fileName, IReadOnlyList<string> arguments, string workingDirectory, bool echoAlways)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi);
        if (process == null)
        {
            Console.Error.WriteLine($"Error: failed to start process: {fileName}");
            return 2;
        }

        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        var stdOut = stdOutTask.GetAwaiter().GetResult();
        var stdErr = stdErrTask.GetAwaiter().GetResult();

        if (echoAlways || process.ExitCode != 0)
        {
            if (stdOut.Length > 0)
            {
                Console.Out.Write(stdOut);
            }

            if (stdErr.Length > 0)
            {
                Console.Error.Write(stdErr);
            }
        }

        return process.ExitCode;
    }

    /// <summary>
    /// Deletes the workspace unless --keep-temp was requested, in which case its
    /// path is printed so the materialized project can be inspected.
    /// </summary>
    internal static void FinishWorkspace(string workspace, bool keepTemp)
    {
        if (keepTemp)
        {
            Console.WriteLine($"Workspace kept at: {workspace}");
            return;
        }

        try
        {
            Directory.Delete(workspace, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup; temp directories are reaped by the OS eventually.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
