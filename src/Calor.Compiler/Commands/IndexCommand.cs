using System.CommandLine;
using System.CommandLine.Invocation;
using Calor.Compiler.Indexing;

namespace Calor.Compiler.Commands;

/// <summary>
/// Builds and inspects the persistent project index (§2.2; scoping doc §7 S1).
///
/// <c>calor index build</c> writes it; <c>calor index status</c> says whether it
/// may still be trusted. Status exists because the interesting failure is not a
/// missing index but a stale one that answers anyway.
/// </summary>
public static class IndexCommand
{
    private const string DefaultOptionsToken = "index-v1";

    public static Command Create()
    {
        var command = new Command("index", "Build and inspect the project index")
        {
            CreateBuildCommand(),
            CreateStatusCommand(),
        };
        return command;
    }

    private static Command CreateBuildCommand()
    {
        var pathArgument = new Argument<string>(
            name: "path",
            description: "Project directory to index",
            getDefaultValue: () => ".");
        var outputOption = new Option<string?>(
            aliases: ["--output"],
            description: "Where to write the index (default: <path>/obj/calor)");

        var command = new Command("build", "Build the project index")
        {
            pathArgument, outputOption,
        };

        command.SetHandler((InvocationContext context) =>
        {
            var path = context.ParseResult.GetValueForArgument(pathArgument);
            var output = context.ParseResult.GetValueForOption(outputOption);
            context.ExitCode = Build(path, output);
        });

        return command;
    }

    private static Command CreateStatusCommand()
    {
        var pathArgument = new Argument<string>(
            name: "path",
            description: "Project directory",
            getDefaultValue: () => ".");
        var outputOption = new Option<string?>(
            aliases: ["--output"],
            description: "Where the index lives (default: <path>/obj/calor)");

        var command = new Command("status", "Report whether the index is current")
        {
            pathArgument, outputOption,
        };

        command.SetHandler((InvocationContext context) =>
        {
            var path = context.ParseResult.GetValueForArgument(pathArgument);
            var output = context.ParseResult.GetValueForOption(outputOption);
            context.ExitCode = Status(path, output);
        });

        return command;
    }

    internal static string DefaultOutputDirectory(string projectDirectory) =>
        Path.Combine(Path.GetFullPath(projectDirectory), "obj", "calor");

    private static int Build(string projectDirectory, string? outputDirectory)
    {
        if (!Directory.Exists(projectDirectory))
        {
            Console.Error.WriteLine($"Error: directory not found: {projectDirectory}");
            return 1;
        }

        var sources = ProjectIndexBuilder.DiscoverSources(projectDirectory);
        if (sources.Count == 0)
        {
            Console.Error.WriteLine($"Error: no .calr files under {projectDirectory}");
            return 1;
        }

        var options = new ProjectIndexBuilder.Options(
            projectDirectory, DefaultOptionsToken, sources);
        var index = ProjectIndexBuilder.Build(options);
        var target = outputDirectory ?? DefaultOutputDirectory(projectDirectory);
        index.Save(target);

        Console.WriteLine(
            $"index: {index.Declarations.Count} declarations, "
                + $"{index.Occurrences.Count} occurrences, "
                + $"{index.CallEdges.Count} call edges "
                + $"from {sources.Count} file(s)");
        ReportResidual(index);
        Console.WriteLine($"index: written to {ProjectIndex.PathFor(target)}");
        return 0;
    }

    private static int Status(string projectDirectory, string? outputDirectory)
    {
        var target = outputDirectory ?? DefaultOutputDirectory(projectDirectory);
        var (index, status) = ProjectIndex.Load(target);
        if (index == null)
        {
            Console.WriteLine($"index: not usable — {ProjectIndex.Explain(status)}");
            return 1;
        }

        var sources = ProjectIndexBuilder.DiscoverSources(projectDirectory);
        var inputs = ProjectIndexBuilder.CurrentInputs(
            new ProjectIndexBuilder.Options(projectDirectory, DefaultOptionsToken, sources));
        var freshness = index.CheckFreshness(
            inputs.CompilerHash, inputs.OptionsHash, inputs.ManifestHash, inputs.Files);

        if (freshness != ProjectIndex.Freshness.Fresh)
        {
            Console.WriteLine(
                $"index: STALE — {ProjectIndex.Explain(freshness)}. Run `calor index build`.");
            return 1;
        }

        Console.WriteLine(
            $"index: current — {index.Declarations.Count} declarations, "
                + $"{index.CallEdges.Count} call edges from {index.Files.Count} file(s)");
        ReportResidual(index);
        return 0;
    }

    /// <summary>
    /// The residual is printed with the answer, never on request only. An index
    /// that reports 400 call edges and stays silent about the 40 it could not
    /// resolve is the shape this project keeps paying for.
    /// </summary>
    private static void ReportResidual(ProjectIndex index)
    {
        if (index.Residual.IsEmpty)
        {
            Console.WriteLine("index: no residual — every call site resolved, every file read");
            return;
        }

        Console.WriteLine($"index: residual — {index.Residual.Total} item(s) not accounted for:");
        foreach (var file in index.Residual.UnreadableFiles)
            Console.WriteLine($"  unreadable: {file}");
        foreach (var call in index.Residual.UnresolvedCalls)
            Console.WriteLine($"  unresolved call: {call.File}: {call.Target}");
        foreach (var name in index.Residual.AmbiguousCallees)
            Console.WriteLine($"  ambiguous callee: {name} (several declarations share the name)");
    }
}
