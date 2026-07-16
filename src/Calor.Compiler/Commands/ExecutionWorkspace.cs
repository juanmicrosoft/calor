using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using System.Security;
using System.Text;
using Calor.Compiler.Effects;

namespace Calor.Compiler.Commands;

/// <summary>
/// Shared workspace materialization for the <c>calor run</c> and <c>calor test</c>
/// commands. Compiles .calr sources in-process via <see cref="CompilationDriver"/>
/// (the same orchestration as the top-level compile command), writes the generated
/// C# into a temporary project referencing the Calor.Runtime assembly that ships
/// alongside the compiler, and shells out to the dotnet CLI to build/run/test it.
/// No hand-wired MSBuild required.
/// </summary>
internal static class ExecutionWorkspace
{
    internal const string DotnetMissingMessage =
        "Error: the 'dotnet' CLI was not found on PATH. " +
        "The .NET SDK (10.0 or later) is required to execute Calor programs. " +
        "Install it from https://dotnet.microsoft.com/download and ensure 'dotnet' is on PATH.";

    /// <summary>Default per-child-process timeout in seconds (overridable via --timeout).</summary>
    internal const int DefaultTimeoutSeconds = 600;

    /// <summary>Exit code used when a child process exceeds the timeout.</summary>
    internal const int TimeoutExitCode = 2;

    internal sealed record CompiledUnit(string FileName, string GeneratedCode);

    /// <summary>
    /// Settings shared by <c>calor run</c> and <c>calor test</c>, bound from the
    /// common CLI options declared in <see cref="AddCommonOptions"/>.
    /// </summary>
    internal sealed record ExecutionSettings(
        bool Permissive,
        bool KeepTemp,
        bool Verbose,
        bool EnforceEffects,
        bool Verify,
        string ContractMode,
        int TimeoutSeconds)
    {
        public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);
    }

    /// <summary>
    /// Declares the CLI options shared by run/test, adds them to
    /// <paramref name="command"/>, and returns a binder that extracts an
    /// <see cref="ExecutionSettings"/> from an invocation.
    /// </summary>
    internal static Func<InvocationContext, ExecutionSettings> AddCommonOptions(Command command)
    {
        var permissiveOption = new Option<bool>(
            aliases: ["--permissive"],
            description: "Relax effect enforcement: unknown calls assumed pure, forbidden effects (including cross-module violations) demoted to warnings");

        var keepTempOption = new Option<bool>(
            aliases: ["--keep-temp"],
            description: "Preserve the materialized temp project and print its path");

        var verboseOption = new Option<bool>(
            aliases: ["--verbose", "-v"],
            description: "Show compilation details and stream build output live");

        var enforceEffectsOption = new Option<bool>(
            aliases: ["--enforce-effects"],
            description: "Enforce effect declarations (default: true; pass 'false' to opt out)",
            getDefaultValue: () => true);

        var verifyOption = new Option<bool>(
            aliases: ["--verify"],
            description: "Enable static contract verification with Z3 SMT solver");

        var contractModeOption = new Option<string>(
            aliases: ["--contract-mode"],
            description: "Contract enforcement mode: off, debug, or release (default: debug)",
            getDefaultValue: () => "debug");

        var timeoutOption = new Option<int>(
            aliases: ["--timeout"],
            description: $"Timeout in seconds for each child process (build/run/test; default: {DefaultTimeoutSeconds})",
            getDefaultValue: () => DefaultTimeoutSeconds);
        timeoutOption.AddValidator(result =>
        {
            if (result.GetValueOrDefault<int>() <= 0)
            {
                result.ErrorMessage = "Timeout must be a positive number of seconds";
            }
        });

        command.AddOption(permissiveOption);
        command.AddOption(keepTempOption);
        command.AddOption(verboseOption);
        command.AddOption(enforceEffectsOption);
        command.AddOption(verifyOption);
        command.AddOption(contractModeOption);
        command.AddOption(timeoutOption);

        return ctx => new ExecutionSettings(
            Permissive: ctx.ParseResult.GetValueForOption(permissiveOption),
            KeepTemp: ctx.ParseResult.GetValueForOption(keepTempOption),
            Verbose: ctx.ParseResult.GetValueForOption(verboseOption),
            EnforceEffects: ctx.ParseResult.GetValueForOption(enforceEffectsOption),
            Verify: ctx.ParseResult.GetValueForOption(verifyOption),
            ContractMode: ctx.ParseResult.GetValueForOption(contractModeOption) ?? "debug",
            TimeoutSeconds: ctx.ParseResult.GetValueForOption(timeoutOption));
    }

    internal sealed record PreparedExecution(string Dotnet, List<CompiledUnit> Units);

    /// <summary>
    /// Shared run/test prologue: resolve sources, locate the dotnet CLI, and
    /// compile everything. Returns null with <paramref name="exitCode"/> set
    /// when any step fails.
    /// </summary>
    internal static PreparedExecution? Prepare(string path, ExecutionSettings settings, out int exitCode)
    {
        exitCode = 0;

        var sources = ResolveSources(path, out var error);
        if (sources == null)
        {
            Console.Error.WriteLine($"Error: {error}");
            exitCode = 2;
            return null;
        }

        var dotnet = FindDotnet();
        if (dotnet == null)
        {
            Console.Error.WriteLine(DotnetMissingMessage);
            exitCode = 2;
            return null;
        }

        var units = CompileSources(sources, settings);
        if (units == null)
        {
            exitCode = 1;
            return null;
        }

        return new PreparedExecution(dotnet, units);
    }

    /// <summary>
    /// Resolves a path argument (a .calr file or a directory containing .calr files)
    /// to the list of source files. Files under bin/, obj/, or reference/ directory
    /// segments are skipped (with a stderr warning listing them) — reference/ holds
    /// benchmark-pair reference solutions that duplicate the primary modules.
    /// Returns null and sets <paramref name="error"/> when the path is invalid or
    /// contains no Calor sources.
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
            var sources = new List<FileInfo>();
            var skipped = new List<string>();
            foreach (var file in Directory.EnumerateFiles(path, "*.calr", SearchOption.AllDirectories)
                         .OrderBy(f => f, StringComparer.Ordinal))
            {
                if (IsInExcludedDirectory(path, file))
                {
                    skipped.Add(Path.GetRelativePath(path, file));
                }
                else
                {
                    sources.Add(new FileInfo(Path.GetFullPath(file)));
                }
            }

            if (skipped.Count > 0)
            {
                Console.Error.WriteLine(
                    $"Warning: skipping {skipped.Count} .calr file(s) under bin/, obj/, or reference/ directories: " +
                    string.Join(", ", skipped));
            }

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

    internal static bool IsInExcludedDirectory(string root, string file)
    {
        var relative = Path.GetRelativePath(root, file);
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        // Only directory segments count — the file name itself is never an exclusion key.
        return segments.Take(segments.Length - 1).Any(s =>
            s.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("reference", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Compiles all sources in-process through <see cref="CompilationDriver"/> —
    /// the same orchestration as the top-level compile command, so warnings
    /// (including permissive demotions) are always printed and cross-module
    /// effect enforcement honors the permissive policy. Returns null on errors.
    /// </summary>
    internal static List<CompiledUnit>? CompileSources(IReadOnlyList<FileInfo> sources, ExecutionSettings settings)
    {
        var units = new List<CompiledUnit>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var policy = settings.Permissive ? UnknownCallPolicy.Permissive : UnknownCallPolicy.Strict;

        var result = CompilationDriver.CompileAll(
            sources,
            file => new CompilationOptions
            {
                Verbose = settings.Verbose,
                EnforceEffects = settings.EnforceEffects,
                UnknownCallPolicy = policy,
                VerifyContracts = settings.Verify,
                ContractMode = CompilationDriver.ParseContractMode(settings.ContractMode),
                ProjectDirectory = Path.GetDirectoryName(file.FullName)
            },
            crossModuleEnforcement: settings.EnforceEffects,
            crossModulePolicy: policy,
            onCompiled: (file, compileResult) =>
            {
                var baseName = Path.GetFileNameWithoutExtension(file.Name);
                var fileName = $"{baseName}.g.cs";
                for (var i = 2; !usedNames.Add(fileName); i++)
                {
                    fileName = $"{baseName}_{i}.g.cs";
                }

                units.Add(new CompiledUnit(fileName, compileResult.GeneratedCode));
            });

        return result.AnyErrors ? null : units;
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
                     <HintPath>{EscapeForMsBuildValue(runtimePath)}</HintPath>
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
    /// Escapes a path for use as an MSBuild element value: first MSBuild special
    /// characters (%, $, @, ', ;, ?, *) so the evaluator takes them literally,
    /// then XML entities so the project file stays well-formed.
    /// </summary>
    internal static string EscapeForMsBuildValue(string value)
    {
        var msbuildEscaped = value
            .Replace("%", "%25") // must be first — the escapes below introduce '%'
            .Replace("$", "%24")
            .Replace("@", "%40")
            .Replace("'", "%27")
            .Replace(";", "%3B")
            .Replace("?", "%3F")
            .Replace("*", "%2A");
        return SecurityElement.Escape(msbuildEscaped) ?? msbuildEscaped;
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
                // Windows PATH entries may be quoted ("C:\Program Files\dotnet").
                var candidate = Path.Combine(dir.Trim().Trim('"'), exeName);
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
    /// and returns its exit code. If the process exceeds <paramref name="timeout"/>,
    /// its entire process tree is killed and <see cref="TimeoutExitCode"/> is returned.
    /// </summary>
    internal static int RunProcess(string fileName, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeout)
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

        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            KillProcessTree(process);
            Console.Error.WriteLine($"Error: process timed out after {timeout.TotalSeconds:0}s: {fileName} {string.Join(' ', arguments)}");
            return TimeoutExitCode;
        }

        return process.ExitCode;
    }

    /// <summary>
    /// Runs a process capturing its combined output. Used for build steps so
    /// MSBuild noise stays hidden unless the build fails, in which case the
    /// buffered output is replayed. With <paramref name="stream"/> (--verbose)
    /// output is streamed to the console live instead of buffered. Applies the
    /// same timeout/kill-tree semantics as <see cref="RunProcess"/>.
    /// </summary>
    internal static int RunProcessCaptured(string fileName, IReadOnlyList<string> arguments, string workingDirectory, bool stream, TimeSpan timeout)
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

        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        using var process = new Process();
        process.StartInfo = psi;
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            if (stream)
            {
                Console.Out.WriteLine(e.Data);
            }
            else
            {
                lock (stdOut) stdOut.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            if (stream)
            {
                Console.Error.WriteLine(e.Data);
            }
            else
            {
                lock (stdErr) stdErr.AppendLine(e.Data);
            }
        };

        try
        {
            if (!process.Start())
            {
                Console.Error.WriteLine($"Error: failed to start process: {fileName}");
                return 2;
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Console.Error.WriteLine($"Error: failed to start process: {fileName} ({ex.Message})");
            return 2;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            KillProcessTree(process);
            Console.Error.WriteLine($"Error: process timed out after {timeout.TotalSeconds:0}s: {fileName} {string.Join(' ', arguments)}");
            return TimeoutExitCode;
        }

        // A second, untimed WaitForExit drains the async output handlers
        // (WaitForExit(int) returning true does not wait for stream EOF).
        process.WaitForExit();

        if (!stream && process.ExitCode != 0)
        {
            lock (stdOut)
            {
                if (stdOut.Length > 0) Console.Out.Write(stdOut.ToString());
            }

            lock (stdErr)
            {
                if (stdErr.Length > 0) Console.Error.Write(stdErr.ToString());
            }
        }

        return process.ExitCode;
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best-effort: the process may have exited between the timeout and the kill.
        }
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
