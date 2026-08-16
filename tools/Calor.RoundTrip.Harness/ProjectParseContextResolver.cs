using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Calor.RoundTrip.Harness;

internal sealed record ProjectFileParseContext(
    CSharpParseOptions ParseOptions,
    string Configuration,
    string? TargetFramework,
    string ProjectFile);

internal static class ProjectParseContextResolver
{
    private sealed record EvaluatedProject(
        ProjectFileParseContext Context,
        IReadOnlyList<string> SourcePaths,
        IReadOnlyList<ProjectReferenceSelection> References);

    private sealed record ProjectReferenceSelection(
        string ProjectFile,
        string SelectedTargetFramework,
        IReadOnlyDictionary<string, string> GlobalProperties);

    private readonly record struct ProjectEvaluationKey(
        string ProjectFile,
        string PropertyState);

    private sealed class ProjectEvaluationKeyComparer
        : IEqualityComparer<ProjectEvaluationKey>
    {
        public bool Equals(
            ProjectEvaluationKey left,
            ProjectEvaluationKey right)
            => PathComparer.Equals(
                    left.ProjectFile,
                    right.ProjectFile)
                && string.Equals(
                    left.PropertyState,
                    right.PropertyState,
                    StringComparison.Ordinal);

        public int GetHashCode(ProjectEvaluationKey value)
            => HashCode.Combine(
                PathComparer.GetHashCode(value.ProjectFile),
                StringComparer.Ordinal.GetHashCode(
                    value.PropertyState));
    }

    public static async Task<IReadOnlyDictionary<string, ProjectFileParseContext>>
        ResolveAsync(
            string workDir,
            RoundTripConfig config,
            IReadOnlyCollection<string> candidateFiles,
            CancellationToken cancellationToken)
    {
        var candidates = candidateFiles
            .Select(Canonicalize)
            .ToHashSet(PathComparer);
        var contexts = new Dictionary<string, ProjectFileParseContext>(PathComparer);
        var configuredProject = Path.IsPathRooted(config.SolutionOrProjectFile)
            ? Path.GetFullPath(config.SolutionOrProjectFile)
            : Path.GetFullPath(
                Path.Combine(workDir, config.SolutionOrProjectFile));
        if (!File.Exists(configuredProject))
        {
            if (config.LooseDirectoryMode)
                return contexts;
            throw new InvalidOperationException(
                $"Configured project '{configuredProject}' does not exist.");
        }
        if (!string.Equals(
                Path.GetExtension(configuredProject),
                ".csproj",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Evaluated harness parse contexts require "
                + $"a concrete .csproj, not '{config.SolutionOrProjectFile}'.");
        }

        var rootTargetFramework = await ResolveRootTargetFrameworkAsync(
            workDir,
            configuredProject,
            config,
            cancellationToken);
        var pending = new Queue<ProjectReferenceSelection>();
        pending.Enqueue(new ProjectReferenceSelection(
            configuredProject,
            rootTargetFramework,
            CreateRootGlobalProperties(
                config,
                rootTargetFramework)));
        var evaluatedProjects = new HashSet<ProjectEvaluationKey>(
            new ProjectEvaluationKeyComparer());
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var selection = pending.Dequeue();
            var evaluationKey = CreateEvaluationKey(selection);
            if (!evaluatedProjects.Add(evaluationKey))
                continue;

            var evaluated = await EvaluateProjectAsync(
                workDir,
                selection,
                config,
                cancellationToken);
            foreach (var sourcePath in evaluated.SourcePaths.Where(path =>
                         candidates.Contains(Canonicalize(path))))
            {
                var canonicalSource = Canonicalize(sourcePath);
                if (contexts.TryGetValue(canonicalSource, out var existing)
                    && !Equivalent(existing, evaluated.Context))
                {
                    throw new InvalidOperationException(
                        $"'{sourcePath}' has ambiguous evaluated parse contexts from "
                        + $"'{existing.ProjectFile}' and "
                        + $"'{selection.ProjectFile}'.");
                }
                contexts[canonicalSource] = evaluated.Context;
            }

            foreach (var reference in evaluated.References)
                pending.Enqueue(reference);
        }
        return contexts;
    }

    private static async Task<EvaluatedProject> EvaluateProjectAsync(
        string workDir,
        ProjectReferenceSelection selection,
        RoundTripConfig config,
        CancellationToken cancellationToken)
    {
        var project = selection.ProjectFile;
        var selectedTargetFramework =
            selection.SelectedTargetFramework;
        var relativeProject = Path.GetRelativePath(workDir, project);
        var arguments =
            $"msbuild \"{relativeProject}\" -target:PrepareForBuild "
            + "-getItem:Compile,ProjectReference "
            + "-getProperty:TargetFramework,TargetFrameworks,DefineConstants,LangVersion,Configuration,Platform "
            + BuildMsBuildPropertyArguments(selection.GlobalProperties)
            + "-nodeReuse:false -verbosity:quiet";
        var (exitCode, stdout, stderr) = await ProcessRunner.RunAsync(
            config.DotnetPath,
            arguments,
            workDir,
            config.BuildTimeout,
            environmentVariables: CreateMsBuildEnvironment(),
            cancellationToken: cancellationToken);
        if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
        {
            throw new InvalidOperationException(
                $"Could not evaluate '{project}' for target framework "
                + $"'{selectedTargetFramework}': "
                + $"{string.Join(Environment.NewLine, stdout, stderr).Trim()}");
        }

        using var document = JsonDocument.Parse(stdout);
        if (!document.RootElement.TryGetProperty("Items", out var items)
            || !items.TryGetProperty("Compile", out var compileItems)
            || !document.RootElement.TryGetProperty("Properties", out var properties))
        {
            throw new InvalidOperationException(
                $"MSBuild returned incomplete evaluated data for '{project}'.");
        }
        var projectDirectory = Path.GetDirectoryName(project)!;
        var sourcePaths = compileItems.EnumerateArray()
            .Select(item => item.TryGetProperty("FullPath", out var fullPath)
                ? fullPath.GetString()
                : item.GetProperty("Identity").GetString())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.IsPathRooted(path!)
                ? Path.GetFullPath(path!)
                : Path.GetFullPath(Path.Combine(projectDirectory, path!)))
            .Where(File.Exists)
            .Distinct(PathComparer)
            .ToArray();
        var symbols = GetProperty(properties, "DefineConstants")
            .Split(
                [';', ','],
                StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries);
        var languageText = GetProperty(properties, "LangVersion");
        var languageVersion = LanguageVersionFacts.TryParse(
            languageText,
            out var parsedLanguage)
            ? parsedLanguage
            : LanguageVersion.Default;
        var targetFramework = GetProperty(properties, "TargetFramework");
        if (!string.IsNullOrWhiteSpace(targetFramework)
            && !string.Equals(
                targetFramework,
                selectedTargetFramework,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"MSBuild evaluated '{project}' as '{targetFramework}' after "
                + $"'{selectedTargetFramework}' was selected.");
        }
        var parseOptions = new CSharpParseOptions(
            languageVersion,
            DocumentationMode.Parse,
            SourceCodeKind.Regular,
            symbols);
        var effectiveConfiguration = GetProperty(
            properties,
            "Configuration");
        if (string.IsNullOrWhiteSpace(effectiveConfiguration)
            && selection.GlobalProperties.TryGetValue(
                "Configuration",
                out var selectedConfiguration))
        {
            effectiveConfiguration = selectedConfiguration;
        }
        var context = new ProjectFileParseContext(
                parseOptions,
                effectiveConfiguration,
                string.IsNullOrWhiteSpace(targetFramework)
                    ? selectedTargetFramework
                    : targetFramework,
                Path.GetFullPath(project));
        var hasProjectReferences = items.TryGetProperty(
                "ProjectReference",
                out var projectReferences)
            && projectReferences.GetArrayLength() > 0;
        var references = hasProjectReferences
            ? await ResolveProjectReferencesAsync(
                workDir,
                selection,
                config,
                cancellationToken)
            : [];
        return new EvaluatedProject(context, sourcePaths, references);
    }

    private static async Task<string> ResolveRootTargetFrameworkAsync(
        string workDir,
        string project,
        RoundTripConfig config,
        CancellationToken cancellationToken)
    {
        var relativeProject = Path.GetRelativePath(workDir, project);
        var arguments =
            $"msbuild \"{relativeProject}\" "
            + "-getProperty:TargetFramework,TargetFrameworks "
            + $"-property:Configuration={config.Configuration} "
            + "-nodeReuse:false -verbosity:quiet";
        if (!string.IsNullOrWhiteSpace(config.ExtraBuildProperties))
            arguments += $" {config.ExtraBuildProperties}";
        var (exitCode, stdout, stderr) = await ProcessRunner.RunAsync(
            config.DotnetPath,
            arguments,
            workDir,
            config.BuildTimeout,
            environmentVariables: CreateMsBuildEnvironment(),
            cancellationToken: cancellationToken);
        if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
        {
            throw new InvalidOperationException(
                $"Could not inspect target frameworks for '{project}': "
                + stderr.Trim());
        }
        using var document = JsonDocument.Parse(stdout);
        if (!document.RootElement.TryGetProperty(
                "Properties",
                out var properties))
        {
            throw new InvalidOperationException(
                $"MSBuild did not report target frameworks for '{project}'.");
        }
        var frameworks = GetProperty(properties, "TargetFrameworks")
            .Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries);
        var single = GetProperty(properties, "TargetFramework");
        if (frameworks.Length == 0)
        {
            if (string.IsNullOrWhiteSpace(single))
            {
                throw new InvalidOperationException(
                    $"'{project}' does not declare a target framework.");
            }
            frameworks = [single];
        }

        if (string.IsNullOrWhiteSpace(config.TargetFramework))
        {
            if (frameworks.Length == 1)
                return frameworks[0];
            throw new InvalidOperationException(
                $"'{project}' targets multiple frameworks "
                + $"({string.Join(", ", frameworks)}); configure one explicitly.");
        }
        var exact = frameworks.FirstOrDefault(framework => string.Equals(
            framework,
            config.TargetFramework,
            StringComparison.OrdinalIgnoreCase));
        if (exact == null)
        {
            throw new InvalidOperationException(
                $"Configured target framework '{config.TargetFramework}' is not "
                + $"declared by root project '{project}' "
                + $"({string.Join(", ", frameworks)}).");
        }
        return exact;
    }

    private static async Task<IReadOnlyList<ProjectReferenceSelection>>
        ResolveProjectReferencesAsync(
            string workDir,
            ProjectReferenceSelection parentSelection,
            RoundTripConfig config,
            CancellationToken cancellationToken)
    {
        var project = parentSelection.ProjectFile;
        var selectedTargetFramework =
            parentSelection.SelectedTargetFramework;
        var relativeProject = Path.GetRelativePath(workDir, project);
        var arguments =
            $"msbuild \"{relativeProject}\" -target:PrepareProjectReferences "
            + "-getItem:_MSBuildProjectReferenceExistent "
            + BuildMsBuildPropertyArguments(
                parentSelection.GlobalProperties)
            + "-nodeReuse:false -verbosity:quiet";
        var (exitCode, stdout, stderr) = await ProcessRunner.RunAsync(
            config.DotnetPath,
            arguments,
            workDir,
            config.BuildTimeout,
            environmentVariables: CreateMsBuildEnvironment(),
            cancellationToken: cancellationToken);
        if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
        {
            throw new InvalidOperationException(
                $"Could not resolve the evaluated project graph for '{project}' "
                + $"at '{selectedTargetFramework}': "
                + $"{string.Join(Environment.NewLine, stdout, stderr).Trim()}");
        }

        using var document = JsonDocument.Parse(stdout);
        if (!document.RootElement.TryGetProperty("Items", out var items)
            || !items.TryGetProperty(
                "_MSBuildProjectReferenceExistent",
                out var references))
        {
            return [];
        }

        var resolved = new List<ProjectReferenceSelection>();
        foreach (var reference in references.EnumerateArray())
        {
            var path = GetItemPath(reference, Path.GetDirectoryName(project)!);
            if (path == null
                || !File.Exists(path)
                || !string.Equals(
                    Path.GetExtension(path),
                    ".csproj",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var nearest = GetProperty(reference, "NearestTargetFramework");
            if (string.IsNullOrWhiteSpace(nearest))
            {
                nearest = ParseSetTargetFramework(
                    GetProperty(reference, "SetTargetFramework"));
            }
            if (string.IsNullOrWhiteSpace(nearest)
                && string.Equals(
                    GetProperty(reference, "HasSingleTargetFramework"),
                    "true",
                    StringComparison.OrdinalIgnoreCase))
            {
                var declared = GetProperty(reference, "TargetFrameworks")
                    .Split(
                        ';',
                        StringSplitOptions.RemoveEmptyEntries
                        | StringSplitOptions.TrimEntries);
                if (declared.Length == 1)
                    nearest = declared[0];
            }
            if (string.IsNullOrWhiteSpace(nearest))
            {
                throw new InvalidOperationException(
                    $"MSBuild did not select a target framework for referenced "
                    + $"project '{path}' from '{project}'.");
            }
            var childProperties = CreateChildGlobalProperties(
                parentSelection.GlobalProperties,
                reference);
            resolved.Add(new ProjectReferenceSelection(
                Canonicalize(path),
                nearest,
                childProperties));
        }
        return resolved;
    }

    internal static string SelectCompatibleTargetFramework(
        IReadOnlyList<string> frameworks,
        string? requestedFramework)
    {
        if (frameworks.Count == 0)
            throw new InvalidOperationException("No target frameworks were declared.");
        if (!string.IsNullOrWhiteSpace(requestedFramework)
            && frameworks.Contains(
                requestedFramework,
                StringComparer.OrdinalIgnoreCase))
            return requestedFramework;
        if (TryParseFramework(requestedFramework, out var requested))
        {
            var compatible = frameworks
                .Select(framework => (
                    Framework: framework,
                    Parsed: TryParseFramework(framework, out var parsed),
                    Value: parsed))
                .Where(candidate =>
                    candidate.Parsed
                    && IsCompatible(requested, candidate.Value))
                .OrderByDescending(candidate =>
                    candidate.Value.Family == requested.Family)
                .ThenByDescending(candidate => candidate.Value.Version)
                .FirstOrDefault();
            if (compatible.Parsed)
                return compatible.Framework;
        }
        if (string.IsNullOrWhiteSpace(requestedFramework)
            && frameworks.Count == 1)
        {
            return frameworks[0];
        }
        throw new InvalidOperationException(
            $"No declared target framework ({string.Join(", ", frameworks)}) "
            + $"is compatible with '{requestedFramework ?? "<unspecified>"}'.");
    }

    private static string? GetItemPath(
        JsonElement item,
        string projectDirectory)
    {
        var path = item.TryGetProperty("FullPath", out var fullPath)
            ? fullPath.GetString()
            : item.TryGetProperty("Identity", out var identity)
                ? identity.GetString()
                : null;
        if (string.IsNullOrWhiteSpace(path))
            return null;
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(projectDirectory, path));
    }

    private static string? ParseSetTargetFramework(string value)
    {
        foreach (var assignment in value.Split(
                     ';',
                     StringSplitOptions.RemoveEmptyEntries
                     | StringSplitOptions.TrimEntries))
        {
            const string prefix = "TargetFramework=";
            if (assignment.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                return assignment[prefix.Length..].Trim();
            }
        }
        return null;
    }

    private static IReadOnlyDictionary<string, string>
        CreateRootGlobalProperties(
            RoundTripConfig config,
            string targetFramework)
    {
        var properties = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["Configuration"] = config.Configuration,
            ["TargetFramework"] = targetFramework
        };
        foreach (var token in TokenizeArguments(
                     config.ExtraBuildProperties ?? string.Empty))
        {
            var propertyText = GetPropertySwitchValue(token);
            if (propertyText != null)
                ApplyProperties(properties, propertyText);
        }
        return properties;
    }

    private static IReadOnlyDictionary<string, string>
        CreateChildGlobalProperties(
            IReadOnlyDictionary<string, string> parentProperties,
            JsonElement reference)
    {
        var properties = new Dictionary<string, string>(
            parentProperties,
            StringComparer.OrdinalIgnoreCase);
        RemoveProperties(
            properties,
            GetProperty(reference, "GlobalPropertiesToRemove"));
        RemoveProperties(
            properties,
            GetProperty(reference, "UndefineProperties"));
        ApplyProperties(
            properties,
            GetProperty(reference, "SetConfiguration"));
        ApplyProperties(
            properties,
            GetProperty(reference, "SetPlatform"));
        ApplyProperties(
            properties,
            GetProperty(reference, "SetTargetFramework"));
        ApplyProperties(
            properties,
            GetProperty(reference, "AdditionalProperties"));
        return properties;
    }

    private static void RemoveProperties(
        IDictionary<string, string> properties,
        string names)
    {
        foreach (var name in names.Split(
                     ';',
                     StringSplitOptions.RemoveEmptyEntries
                     | StringSplitOptions.TrimEntries))
        {
            properties.Remove(Uri.UnescapeDataString(name));
        }
    }

    private static void ApplyProperties(
        IDictionary<string, string> properties,
        string assignments)
    {
        string? currentName = null;
        var currentValue = new System.Text.StringBuilder();
        foreach (var assignment in assignments.Split(
                     ';',
                     StringSplitOptions.RemoveEmptyEntries
                     | StringSplitOptions.TrimEntries))
        {
            var separator = assignment.IndexOf('=');
            if (separator <= 0)
            {
                if (currentName != null)
                {
                    if (currentValue.Length > 0)
                        currentValue.Append(';');
                    currentValue.Append(
                        Uri.UnescapeDataString(assignment));
                }
                continue;
            }
            Commit();
            currentName = Uri.UnescapeDataString(
                assignment[..separator].Trim());
            currentValue.Append(Uri.UnescapeDataString(
                assignment[(separator + 1)..].Trim()));
        }
        Commit();

        void Commit()
        {
            if (!string.IsNullOrWhiteSpace(currentName))
                properties[currentName] = currentValue.ToString();
            currentName = null;
            currentValue.Clear();
        }
    }

    private static string BuildMsBuildPropertyArguments(
        IReadOnlyDictionary<string, string> properties)
        => string.Concat(properties
            .OrderBy(
                property => property.Key,
                StringComparer.OrdinalIgnoreCase)
            .Select(property =>
                $"-property:{property.Key}="
                + $"\"{EscapeMsBuildPropertyValue(property.Value)}\" "));

    private static ProjectEvaluationKey CreateEvaluationKey(
        ProjectReferenceSelection selection)
        => new(
            Canonicalize(selection.ProjectFile),
            $"{selection.SelectedTargetFramework.Length}:"
            + selection.SelectedTargetFramework
            + string.Concat(selection.GlobalProperties
                .OrderBy(
                    property => property.Key,
                    StringComparer.OrdinalIgnoreCase)
                .Select(property =>
                    $"{property.Key.Length}:"
                    + property.Key.ToUpperInvariant()
                    + $"{property.Value.Length}:{property.Value}")));

    private static IEnumerable<string> TokenizeArguments(string arguments)
    {
        var current = new System.Text.StringBuilder();
        var quoted = false;
        for (var index = 0; index < arguments.Length; index++)
        {
            var character = arguments[index];
            if (character == '"')
            {
                quoted = !quoted;
                continue;
            }
            if (char.IsWhiteSpace(character) && !quoted)
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }
                continue;
            }
            current.Append(character);
        }
        if (current.Length > 0)
            yield return current.ToString();
    }

    private static string? GetPropertySwitchValue(string token)
    {
        string[] prefixes =
        [
            "-property:",
            "/property:",
            "-p:",
            "/p:"
        ];
        var prefix = prefixes.FirstOrDefault(candidate =>
            token.StartsWith(
                candidate,
                StringComparison.OrdinalIgnoreCase));
        return prefix == null ? null : token[prefix.Length..];
    }

    private static string EscapeMsBuildPropertyValue(string value)
        => value
            .Replace("%", "%25", StringComparison.Ordinal)
            .Replace(";", "%3B", StringComparison.Ordinal)
            .Replace("\"", "%22", StringComparison.Ordinal)
            .Replace("$", "%24", StringComparison.Ordinal)
            .Replace("@", "%40", StringComparison.Ordinal)
            .Replace("'", "%27", StringComparison.Ordinal)
            .Replace("?", "%3F", StringComparison.Ordinal)
            .Replace("*", "%2A", StringComparison.Ordinal);

    private static Dictionary<string, string> CreateMsBuildEnvironment()
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["MSBUILDDISABLENODEREUSE"] = "1"
        };

    private static bool TryParseFramework(
        string? targetFramework,
        out ParsedFramework parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(targetFramework))
            return false;
        var normalized = targetFramework
            .Split('-', 2)[0];
        if (normalized.StartsWith(
                "netstandard",
                StringComparison.OrdinalIgnoreCase))
            return Parse(
                normalized["netstandard".Length..],
                FrameworkFamily.NetStandard,
                out parsed);
        if (normalized.StartsWith(
                "netcoreapp",
                StringComparison.OrdinalIgnoreCase))
            return Parse(
                normalized["netcoreapp".Length..],
                FrameworkFamily.NetCoreApp,
                out parsed);
        if (!normalized.StartsWith(
                "net",
                StringComparison.OrdinalIgnoreCase))
            return false;
        var value = normalized[3..];
        if (value.Contains('.'))
        {
            if (!Version.TryParse(value, out var dotted))
                return false;
            parsed = new ParsedFramework(
                dotted.Major >= 5
                    ? FrameworkFamily.ModernNet
                    : FrameworkFamily.NetFramework,
                dotted);
            return true;
        }
        if (!value.Contains('.')
            && value.Length is 2 or 3
            && value.All(char.IsDigit))
        {
            var version = value.Length == 2
                ? new Version(value[0] - '0', value[1] - '0')
                : new Version(
                    value[0] - '0',
                    value[1] - '0',
                    value[2] - '0');
            parsed = new ParsedFramework(
                version.Major >= 5
                    ? FrameworkFamily.ModernNet
                    : FrameworkFamily.NetFramework,
                version);
            return true;
        }
        if (!int.TryParse(value, out var major))
            return false;
        parsed = new ParsedFramework(
            major >= 5
                ? FrameworkFamily.ModernNet
                : FrameworkFamily.NetFramework,
            new Version(major, 0));
        return true;

        static bool Parse(
            string value,
            FrameworkFamily family,
            out ParsedFramework result)
        {
            result = default;
            if (!TryParseVersion(value, out var version))
                return false;
            result = new ParsedFramework(family, version);
            return true;
        }

        static bool TryParseVersion(
            string value,
            out Version version)
        {
            if (value.Contains('.'))
                return Version.TryParse(value, out version!);
            if (value.Length is 2 or 3
                && value.All(char.IsDigit))
            {
                version = value.Length == 2
                    ? new Version(value[0] - '0', value[1] - '0')
                    : new Version(
                        value[0] - '0',
                        value[1] - '0',
                        value[2] - '0');
                return true;
            }
            version = new Version();
            return false;
        }
    }

    private static bool IsCompatible(
        ParsedFramework requested,
        ParsedFramework candidate)
    {
        if (requested.Family == candidate.Family)
            return candidate.Version <= requested.Version;
        return candidate.Family == FrameworkFamily.NetStandard
            && requested.Family is FrameworkFamily.ModernNet
                or FrameworkFamily.NetCoreApp
                or FrameworkFamily.NetFramework
            && candidate.Version <= new Version(
                requested.Family == FrameworkFamily.NetFramework ? 2 : 2,
                requested.Family == FrameworkFamily.NetFramework ? 0 : 1);
    }

    private enum FrameworkFamily
    {
        NetFramework,
        NetStandard,
        NetCoreApp,
        ModernNet
    }

    private readonly record struct ParsedFramework(
        FrameworkFamily Family,
        Version Version);

    private static bool Equivalent(
        ProjectFileParseContext left,
        ProjectFileParseContext right)
        => left.ParseOptions.LanguageVersion == right.ParseOptions.LanguageVersion
            && left.ParseOptions.DocumentationMode == right.ParseOptions.DocumentationMode
            && left.ParseOptions.Kind == right.ParseOptions.Kind
            && left.ParseOptions.PreprocessorSymbolNames
                .OrderBy(symbol => symbol, StringComparer.Ordinal)
                .SequenceEqual(
                    right.ParseOptions.PreprocessorSymbolNames
                        .OrderBy(symbol => symbol, StringComparer.Ordinal),
                    StringComparer.Ordinal)
            && string.Equals(
                left.Configuration,
                right.Configuration,
                StringComparison.Ordinal)
            && string.Equals(
                left.TargetFramework,
                right.TargetFramework,
                StringComparison.OrdinalIgnoreCase);

    private static string GetProperty(JsonElement properties, string name)
        => properties.TryGetProperty(name, out var property)
            ? property.GetString() ?? string.Empty
            : string.Empty;

    internal static string Canonicalize(string path)
        => Path.GetFullPath(path)
            .Replace("/private/var/", "/var/");

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
}
