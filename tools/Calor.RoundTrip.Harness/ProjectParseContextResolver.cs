using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Calor.RoundTrip.Harness;

internal sealed record ProjectFileParseContext(
    CSharpParseOptions ParseOptions,
    string Configuration,
    string Platform,
    string? TargetFramework,
    string ProjectFile,
    IReadOnlyDictionary<string, string> GlobalProperties,
    IReadOnlyList<ProjectReferenceIdentity> References,
    IReadOnlyList<string> AnalyzerPaths,
    IReadOnlyList<string> AnalyzerConfigHashes,
    IReadOnlyDictionary<string, string> CompilationProperties,
    IReadOnlyList<string> CompileInputHashes,
    IReadOnlyList<string> AdditionalInputHashes,
    IReadOnlyList<ProjectBuildState> BuildStates,
    IReadOnlyList<ProjectContextProvenance> Provenance);

internal sealed record ProjectReferenceIdentity(
    string Path,
    IReadOnlyList<string> Aliases,
    IReadOnlyDictionary<string, string> Properties,
    string ContentHash,
    string EvaluationKey);

internal sealed record ProjectContextProvenance(
    IReadOnlyList<string> ProjectGraphPath,
    IReadOnlyDictionary<string, string> GlobalProperties);

internal sealed record ProjectBuildState(
    string ProjectFile,
    string Configuration,
    string Platform,
    string? TargetFramework,
    IReadOnlyDictionary<string, string> GlobalProperties,
    IReadOnlyList<string> ProjectGraphPath);

internal abstract record ProjectFileParseResolution
{
    private ProjectFileParseContext ResolvedContext =>
        this is ResolvedProjectFileParseContext resolved
            ? resolved.Context
            : throw new InvalidOperationException(
                "This file does not have one resolved parse context.");

    public CSharpParseOptions ParseOptions =>
        ResolvedContext.ParseOptions;
    public string Configuration =>
        ResolvedContext.Configuration;
    public string? TargetFramework =>
        ResolvedContext.TargetFramework;
    public IReadOnlyList<ProjectContextProvenance> Provenance =>
        ResolvedContext.Provenance;
}

internal sealed record ResolvedProjectFileParseContext(
    ProjectFileParseContext Context)
    : ProjectFileParseResolution;

internal sealed record AmbiguousProjectFileParseContext(
    IReadOnlyList<ProjectFileParseContext> Contexts,
    string Diagnostic)
    : ProjectFileParseResolution;

internal sealed record MissingProjectFileParseContext(
    string Diagnostic)
    : ProjectFileParseResolution;

internal static class ProjectParseContextResolver
{
    private sealed record EvaluatedProject(
        ProjectFileParseContext Context,
        IReadOnlyList<string> SourcePaths,
        IReadOnlyList<ProjectReferenceSelection> References);

    private sealed record ResolvedCompilerInputs(
        IReadOnlyList<ProjectReferenceIdentity> References,
        IReadOnlyList<string> AnalyzerPaths,
        IReadOnlyList<string> AnalyzerConfigHashes,
        IReadOnlyList<string> CompileInputHashes,
        IReadOnlyList<string> AdditionalInputHashes,
        IReadOnlyList<string> SourcePaths,
        IReadOnlyDictionary<string, string> Properties,
        IReadOnlyList<string> CscArguments);

    private sealed record ProjectReferenceSelection(
        string ProjectFile,
        string SelectedTargetFramework,
        IReadOnlyDictionary<string, string> GlobalProperties,
        IReadOnlyList<string> ProjectGraphPath,
        IReadOnlyList<ProjectEvaluationKey> ActiveEvaluationKeys,
        bool IsInSelectedScope);

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

    public static async Task<IReadOnlyDictionary<string, ProjectFileParseResolution>>
        ResolveAsync(
            string workDir,
            RoundTripConfig config,
            IReadOnlyCollection<string> candidateFiles,
            CancellationToken cancellationToken)
    {
        var candidates = candidateFiles
            .Select(Canonicalize)
            .ToHashSet(PathComparer);
        var contexts = new Dictionary<
            string,
            List<ProjectFileParseContext>>(PathComparer);
        var configuredProject = Path.IsPathRooted(config.SolutionOrProjectFile)
            ? Path.GetFullPath(config.SolutionOrProjectFile)
            : Path.GetFullPath(
                Path.Combine(workDir, config.SolutionOrProjectFile));
        var selectedContextProject =
            string.IsNullOrWhiteSpace(config.ParseContextProjectFile)
                ? null
                : Canonicalize(Path.IsPathRooted(
                        config.ParseContextProjectFile)
                    ? config.ParseContextProjectFile
                    : Path.Combine(
                        workDir,
                        config.ParseContextProjectFile));
        if (!File.Exists(configuredProject))
        {
            if (config.LooseDirectoryMode)
            {
                return candidates.ToDictionary(
                    candidate => candidate,
                    candidate =>
                        (ProjectFileParseResolution)
                        new MissingProjectFileParseContext(
                            "Loose-directory conversion has no evaluated "
                            + "project parse context."),
                    PathComparer);
            }
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
        if (selectedContextProject != null
            && !File.Exists(selectedContextProject))
        {
            throw new InvalidOperationException(
                $"Configured parse-context project "
                + $"'{selectedContextProject}' does not exist.");
        }

        var rootTargetFramework = await ResolveRootTargetFrameworkAsync(
            workDir,
            configuredProject,
            config,
            cancellationToken);
        var rootProperties = CreateRootGlobalProperties(
            config,
            rootTargetFramework);
        await RestoreParseContextGraphAsync(
            workDir,
            configuredProject,
            rootProperties,
            config,
            cancellationToken);
        var pending = new Queue<ProjectReferenceSelection>();
        var rootSelection = new ProjectReferenceSelection(
            configuredProject,
            rootTargetFramework,
            rootProperties,
            [Canonicalize(configuredProject)],
            [],
            selectedContextProject == null
                || PathComparer.Equals(
                    Canonicalize(configuredProject),
                    selectedContextProject));
        pending.Enqueue(rootSelection);
        var evaluatedProjects = new Dictionary<
            ProjectEvaluationKey,
            EvaluatedProject>(
            new ProjectEvaluationKeyComparer());
        var evaluationMemo = new Dictionary<
            ProjectEvaluationKey,
            Task<EvaluatedProject>>(
            new ProjectEvaluationKeyComparer());
        var semanticHashMemo = new Dictionary<
            string,
            Task<string>>(StringComparer.Ordinal);
        var selectedScopeFound =
            selectedContextProject == null
            || rootSelection.IsInSelectedScope;
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var selection = pending.Dequeue();
            var evaluationKey = CreateEvaluationKey(selection);
            var activeEvaluationKeys =
                selection.ActiveEvaluationKeys
                    .Append(evaluationKey)
                    .ToList();
            if (evaluatedProjects.TryGetValue(
                    evaluationKey,
                    out var priorEvaluation))
            {
                if (selection.IsInSelectedScope)
                {
                    AddContexts(
                        priorEvaluation with
                        {
                            Context = AddProvenance(
                                priorEvaluation.Context,
                                selection)
                        });
                }
                foreach (var reference in priorEvaluation.References)
                {
                    var canonicalReference = Canonicalize(
                        reference.ProjectFile);
                    if (ContainsEvaluationKey(
                            activeEvaluationKeys,
                            CreateEvaluationKey(reference)))
                    {
                        continue;
                    }
                    pending.Enqueue(reference with
                    {
                        ProjectGraphPath =
                        [
                            .. selection.ProjectGraphPath,
                            canonicalReference
                        ],
                        ActiveEvaluationKeys =
                            activeEvaluationKeys,
                        IsInSelectedScope =
                            IsInSelectedScope(
                                selection,
                                reference)
                    });
                }
                continue;
            }

            var evaluated = await EvaluateProjectCachedAsync(
                workDir,
                selection,
                config,
                cancellationToken,
                evaluationMemo);
            evaluated = evaluated with
            {
                Context = await AttachReferencedProjectStateHashesAsync(
                    workDir,
                    evaluated.Context,
                    evaluated.References,
                    config,
                        cancellationToken,
                        [evaluationKey],
                        semanticHashMemo,
                        evaluationMemo)
            };
            evaluatedProjects[evaluationKey] = evaluated;
            if (selection.IsInSelectedScope)
                AddContexts(evaluated);

            foreach (var reference in evaluated.References)
            {
                if (ContainsEvaluationKey(
                        activeEvaluationKeys,
                        CreateEvaluationKey(reference)))
                {
                    continue;
                }

                pending.Enqueue(reference with
                {
                    ActiveEvaluationKeys =
                        activeEvaluationKeys,
                    IsInSelectedScope =
                        IsInSelectedScope(
                            selection,
                            reference)
                });
            }
        }
        if (!selectedScopeFound)
        {
            throw new InvalidOperationException(
                $"Parse-context project '{selectedContextProject}' was not "
                + "found in the evaluated project graph for the selected "
                + $"target framework '{config.TargetFramework}'.");
        }
        return candidates.ToDictionary(
            candidate => candidate,
            candidate =>
            {
                if (!contexts.TryGetValue(
                        candidate,
                        out var candidateContexts)
                    || candidateContexts.Count == 0)
                {
                    return (ProjectFileParseResolution)
                        new MissingProjectFileParseContext(
                            $"No evaluated parse context includes "
                            + $"'{candidate}'.");
                }
                if (candidateContexts.Count == 1)
                {
                    return new ResolvedProjectFileParseContext(
                        candidateContexts[0]);
                }
                var ordered = candidateContexts
                    .OrderBy(
                        CreateContextIdentity,
                        StringComparer.Ordinal)
                    .ToList();
                return new AmbiguousProjectFileParseContext(
                    ordered,
                    CreateAmbiguityDiagnostic(
                        candidate,
                        ordered));
            },
            PathComparer);

        bool IsInSelectedScope(
            ProjectReferenceSelection parent,
            ProjectReferenceSelection reference)
        {
            if (selectedContextProject != null
                && PathComparer.Equals(
                    Canonicalize(reference.ProjectFile),
                    selectedContextProject))
            {
                selectedScopeFound = true;
                return true;
            }
            return parent.IsInSelectedScope;
        }

        void AddContexts(EvaluatedProject evaluated)
        {
            foreach (var sourcePath in evaluated.SourcePaths.Where(path =>
                         candidates.Contains(Canonicalize(path))))
            {
                var canonicalSource = Canonicalize(sourcePath);
                if (!contexts.TryGetValue(
                        canonicalSource,
                        out var existingContexts))
                {
                    contexts[canonicalSource] =
                        [evaluated.Context];
                    continue;
                }
                var evaluatedIdentity =
                    CreateContextIdentity(evaluated.Context);
                var existingIndex = existingContexts.FindIndex(
                    context => string.Equals(
                        CreateContextIdentity(context),
                        evaluatedIdentity,
                        StringComparison.Ordinal));
                if (existingIndex < 0)
                {
                    existingContexts.Add(evaluated.Context);
                    continue;
                }
                var existing = existingContexts[existingIndex];
                var existingOwnership = GetOwnershipScore(
                    existing,
                    canonicalSource);
                var candidateOwnership = GetOwnershipScore(
                    evaluated.Context,
                    canonicalSource);
                var candidateWins =
                    candidateOwnership < existingOwnership
                    || candidateOwnership == existingOwnership
                    && string.Compare(
                        CreateRepresentativeKey(evaluated.Context),
                        CreateRepresentativeKey(existing),
                        StringComparison.Ordinal) < 0;
                existingContexts[existingIndex] = candidateWins
                    ? MergeProvenance(evaluated.Context, existing)
                    : MergeProvenance(existing, evaluated.Context);
            }
        }

    }

    private static int GetOwnershipScore(
        ProjectFileParseContext context,
        string sourcePath)
    {
        var projectDirectory = Path.GetDirectoryName(
            Canonicalize(context.ProjectFile))!;
        var relative = Path.GetRelativePath(
            projectDirectory,
            Canonicalize(sourcePath));
        return relative == ".."
            || relative.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal)
            || Path.IsPathRooted(relative)
            ? 1
            : 0;
    }

    private static bool ContainsEvaluationKey(
        IEnumerable<ProjectEvaluationKey> keys,
        ProjectEvaluationKey candidate)
    {
        var comparer = new ProjectEvaluationKeyComparer();
        return keys.Any(key => comparer.Equals(key, candidate));
    }

    private static async Task<ProjectFileParseContext>
        AttachReferencedProjectStateHashesAsync(
            string workDir,
            ProjectFileParseContext context,
            IReadOnlyList<ProjectReferenceSelection> references,
            RoundTripConfig config,
            CancellationToken cancellationToken,
            IReadOnlyList<ProjectEvaluationKey> activeKeys,
            IDictionary<string, Task<string>> memo,
            IDictionary<ProjectEvaluationKey, Task<EvaluatedProject>>
                evaluationMemo)
    {
        if (references.Count == 0)
            return context;
        var stateHashes = new Dictionary<string, string>(
            StringComparer.Ordinal);
        foreach (var reference in references)
        {
            var referenceKey = CreateEvaluationKey(reference);
            stateHashes[SerializeEvaluationKey(referenceKey)] =
                await GetSemanticHashAsync(
                    reference,
                    referenceKey,
                    activeKeys);
        }
        return context with
        {
            References = context.References
                .Select(reference =>
                    stateHashes.TryGetValue(
                        reference.EvaluationKey,
                        out var stateHash)
                        ? reference with
                        {
                            ContentHash = stateHash
                        }
                        : reference)
                .ToList()
        };

        async Task<string> GetSemanticHashAsync(
            ProjectReferenceSelection reference,
            ProjectEvaluationKey referenceKey,
            IReadOnlyList<ProjectEvaluationKey> active)
        {
            if (ContainsEvaluationKey(active, referenceKey))
            {
                var comparer = new ProjectEvaluationKeyComparer();
                var cycleStart = active
                    .Select((key, index) => (key, index))
                    .First(entry => comparer.Equals(
                        entry.key,
                        referenceKey))
                    .index;
                var canonicalCycle = active
                    .Skip(cycleStart)
                    .Append(referenceKey)
                    .Select(SerializeEvaluationKey)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal);
                return "CYCLE:"
                    + CreateStableDigest(string.Join(
                        "|",
                        canonicalCycle));
            }
            var memoKey = SerializeEvaluationKey(referenceKey)
                + "|active:"
                + CreateStableDigest(string.Join(
                    "|",
                    active.Select(SerializeEvaluationKey)
                        .OrderBy(value => value, StringComparer.Ordinal)));
            if (memo.TryGetValue(memoKey, out var cached))
                return await cached;
            var computation = ComputeSemanticHashAsync();
            memo[memoKey] = computation;
            return await computation;

            async Task<string> ComputeSemanticHashAsync()
            {
                var evaluated = await EvaluateProjectCachedAsync(
                    workDir,
                    reference,
                    config,
                    cancellationToken,
                    evaluationMemo);
                var childContext =
                    await AttachReferencedProjectStateHashesAsync(
                        workDir,
                        evaluated.Context,
                        evaluated.References,
                        config,
                        cancellationToken,
                        [.. active, referenceKey],
                        memo,
                        evaluationMemo);
                return CreateStableDigest(
                    CreateContextIdentity(childContext));
            }
        }
    }

    private static async Task<EvaluatedProject> EvaluateProjectCachedAsync(
        string workDir,
        ProjectReferenceSelection selection,
        RoundTripConfig config,
        CancellationToken cancellationToken,
        IDictionary<ProjectEvaluationKey, Task<EvaluatedProject>> cache)
    {
        var key = CreateEvaluationKey(selection);
        if (!cache.TryGetValue(key, out var evaluation))
        {
            evaluation = EvaluateProjectAsync(
                workDir,
                selection,
                config,
                cancellationToken);
            cache[key] = evaluation;
        }
        return await evaluation;
    }

    private static string CreateStableDigest(string value)
        => Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(value)));

    private static async Task RestoreParseContextGraphAsync(
        string workDir,
        string project,
        IReadOnlyDictionary<string, string> globalProperties,
        RoundTripConfig config,
        CancellationToken cancellationToken)
    {
        var relativeProject = Path.GetRelativePath(
            workDir,
            project);
        var restoreProperties = new Dictionary<string, string>(
            globalProperties,
            StringComparer.OrdinalIgnoreCase);
        restoreProperties.Remove("TargetFramework");
        var arguments =
            $"restore \"{relativeProject}\" "
            + BuildMsBuildPropertyArguments(restoreProperties)
            + "--verbosity quiet";
        var (exitCode, stdout, stderr) =
            await ProcessRunner.RunAsync(
                config.DotnetPath,
                arguments,
                workDir,
                config.BuildTimeout,
                environmentVariables: CreateMsBuildEnvironment(),
                cancellationToken: cancellationToken);
        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"Could not restore parse-context graph '{project}': "
                + $"{string.Join(
                    Environment.NewLine,
                    stdout,
                    stderr).Trim()}");
        }
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
            + "-getProperty:TargetFramework,TargetFrameworks,DefineConstants,LangVersion,Configuration,Platform,Features,GenerateDocumentationFile,DocumentationFile,Nullable,AllowUnsafeBlocks,CheckForOverflowUnderflow,OutputType,TreatWarningsAsErrors,WarningsAsErrors,WarningsNotAsErrors,NoWarn,WarningLevel,ImplicitUsings "
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
            || !document.RootElement.TryGetProperty("Properties", out _))
        {
            throw new InvalidOperationException(
                $"MSBuild returned incomplete evaluated data for '{project}'.");
        }
        var projectDirectory = Path.GetDirectoryName(project)!;
        var compilerInputs = await ResolveCompilerInputsAsync(
            workDir,
            selection,
            config,
            cancellationToken);
        var effectiveProperties = compilerInputs.Properties;
        var sourcePaths = compilerInputs.SourcePaths;
        var parseOptions = CreateEffectiveParseOptions(
            effectiveProperties,
            compilerInputs.CscArguments);
        var targetFramework = GetProperty(
            effectiveProperties,
            "TargetFramework");
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
        var effectiveConfiguration = GetProperty(
            effectiveProperties,
            "Configuration");
        if (string.IsNullOrWhiteSpace(effectiveConfiguration)
            && selection.GlobalProperties.TryGetValue(
                "Configuration",
                out var selectedConfiguration))
        {
            effectiveConfiguration = selectedConfiguration;
        }
        var platform = GetProperty(effectiveProperties, "Platform");
        var directProjectReferences = items.TryGetProperty(
                "ProjectReference",
                out var projectReferences)
            ? projectReferences.EnumerateArray()
                .Select(reference => GetItemPath(
                    reference,
                    projectDirectory))
                .Where(path => path != null)
                .Select(path => Canonicalize(path!))
                .ToHashSet(PathComparer)
            : [];
        var references = directProjectReferences.Count > 0
            ? await ResolveProjectReferencesAsync(
                workDir,
                selection,
                directProjectReferences,
                config,
                cancellationToken)
            : [];
        var effectiveReferences = compilerInputs.References
            .Concat(references.Select(CreateProjectReferenceStateIdentity))
            .OrderBy(
                reference => CanonicalIdentityReference(reference.Path),
                StringComparer.Ordinal)
            .ThenBy(
                reference => SerializeProperties(reference.Properties)
                    + reference.ContentHash,
                StringComparer.Ordinal)
            .ToList();
        var compilationProperties =
            CreateCompilationProperties(effectiveProperties);
        var context = new ProjectFileParseContext(
                parseOptions,
                effectiveConfiguration,
                platform,
                string.IsNullOrWhiteSpace(targetFramework)
                    ? selectedTargetFramework
                    : targetFramework,
                Canonicalize(project),
                CopyProperties(selection.GlobalProperties),
                effectiveReferences,
                compilerInputs.AnalyzerPaths,
                compilerInputs.AnalyzerConfigHashes,
                compilationProperties,
                compilerInputs.CompileInputHashes,
                compilerInputs.AdditionalInputHashes,
                [
                    new ProjectBuildState(
                        Canonicalize(project),
                        effectiveConfiguration,
                        platform,
                        string.IsNullOrWhiteSpace(targetFramework)
                            ? selectedTargetFramework
                            : targetFramework,
                        CopyProperties(selection.GlobalProperties),
                        selection.ProjectGraphPath.ToList())
                ],
                [
                    new ProjectContextProvenance(
                        selection.ProjectGraphPath.ToList(),
                        CopyProperties(selection.GlobalProperties))
                ]);
        return new EvaluatedProject(context, sourcePaths, references);
    }

    private static async Task<ResolvedCompilerInputs>
        ResolveCompilerInputsAsync(
            string workDir,
            ProjectReferenceSelection selection,
            RoundTripConfig config,
            CancellationToken cancellationToken)
    {
        var project = selection.ProjectFile;
        var projectDirectory = Path.GetDirectoryName(project)!;
        var relativeProject = Path.GetRelativePath(
            workDir,
            project);
        var properties = new Dictionary<string, string>(
            selection.GlobalProperties,
            StringComparer.OrdinalIgnoreCase)
        {
            ["BuildProjectReferences"] = "false",
            ["SkipCompilerExecution"] = "true",
            ["ProvideCommandLineArgs"] = "true",
            ["NonExistentFile"] = Path.Combine(
                projectDirectory,
                "obj",
                ".calor-command-line-args.never")
        };
        var arguments =
            $"msbuild \"{relativeProject}\" "
            + "-target:GenerateMSBuildEditorConfigFile;ResolveReferences;CoreCompile "
            + "-getItem:ReferencePathWithRefAssemblies,ReferencePath,Analyzer,AnalyzerConfigFiles,EditorConfigFiles,Compile,AdditionalFiles,AddModules,CscCommandLineArgs "
            + "-getProperty:TargetFramework,TargetFrameworks,DefineConstants,LangVersion,Configuration,Platform,Features,GenerateDocumentationFile,DocumentationFile,Nullable,AllowUnsafeBlocks,CheckForOverflowUnderflow,OutputType,TreatWarningsAsErrors,WarningsAsErrors,WarningsNotAsErrors,NoWarn,WarningLevel,ImplicitUsings,CompilerResponseFile "
            + BuildMsBuildPropertyArguments(properties)
            + "-nodeReuse:false -verbosity:quiet";
        var (exitCode, stdout, stderr) =
            await ProcessRunner.RunAsync(
                config.DotnetPath,
                arguments,
                workDir,
                config.BuildTimeout,
                environmentVariables: CreateMsBuildEnvironment(),
                cancellationToken: cancellationToken);
        if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
        {
            throw new InvalidOperationException(
                $"Could not resolve compiler inputs for '{project}': "
                + $"{string.Join(
                    Environment.NewLine,
                    stdout,
                    stderr).Trim()}");
        }

        using var document = JsonDocument.Parse(stdout);
        if (!document.RootElement.TryGetProperty(
                "Items",
                out var items)
            || !document.RootElement.TryGetProperty(
                "Properties",
                out var resolvedProperties))
        {
            throw new InvalidOperationException(
                $"MSBuild did not report compiler inputs for '{project}'.");
        }
        var hasReferenceAssemblies = items.TryGetProperty(
                "ReferencePathWithRefAssemblies",
                out var referenceItems)
            && referenceItems.GetArrayLength() > 0;
        if (!hasReferenceAssemblies)
        {
            items.TryGetProperty(
                "ReferencePath",
                out referenceItems);
        }
        var references = referenceItems.ValueKind
                == JsonValueKind.Array
            ? referenceItems.EnumerateArray()
                .Select(reference => CreateReferenceIdentity(
                    reference,
                    projectDirectory,
                    isProjectReference: false))
                .GroupBy(
                    reference => CreateReferenceIdentityKey(reference),
                    StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(
                    reference => CanonicalIdentityReference(
                        reference.Path),
                    StringComparer.Ordinal)
                .ThenBy(
                    reference => string.Join(",", reference.Aliases),
                    StringComparer.Ordinal)
                .ToList()
            : [];
        var analyzerPaths = GetItemPaths(
            items,
            "Analyzer",
            projectDirectory);
        var analyzerConfigPaths = GetItemPaths(
                items,
                "AnalyzerConfigFiles",
                projectDirectory)
            .Concat(GetItemPaths(
                items,
                "EditorConfigFiles",
                projectDirectory))
            .Distinct(PathComparer)
            .OrderBy(
                path => CanonicalIdentityPath(path),
                StringComparer.Ordinal)
            .ToList();
        var analyzerConfigHashes = analyzerConfigPaths
            .Select(CreateAnalyzerConfigHash)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(hash => hash, StringComparer.Ordinal)
            .ToList();
        var sourcePaths = items.TryGetProperty(
                "Compile",
                out var compileItems)
            ? compileItems.EnumerateArray()
                    .Select(item => GetItemPath(
                        item,
                        projectDirectory))
                    .Where(path => path != null)
                    .Select(path => Canonicalize(path!))
                    .Where(File.Exists)
                    .Distinct(PathComparer)
                    .ToList()
            : [];
        var compileInputHashes = CreateCompileInputHashes(
            sourcePaths,
            projectDirectory);
        var cscArguments = items.TryGetProperty(
                "CscCommandLineArgs",
                out var commandLineItems)
            ? commandLineItems.EnumerateArray()
                .Select(item => GetProperty(item, "Identity"))
                .Where(argument => !string.IsNullOrWhiteSpace(argument))
                .ToList()
            : [];
        var responseFile = GetProperty(
            resolvedProperties,
            "CompilerResponseFile");
        if (!string.IsNullOrWhiteSpace(responseFile))
            cscArguments.Insert(0, $"@{responseFile}");
        var expandedArguments = ExpandResponseFileArguments(
            cscArguments,
            projectDirectory);
        cscArguments = expandedArguments.Arguments.ToList();
        var additionalInputHashes = GetItemPaths(
                items,
                "AdditionalFiles",
                projectDirectory)
            .Concat(GetItemPaths(
                items,
                "AddModules",
                projectDirectory))
            .Select(path => CreateFileInputHash(
                path,
                projectDirectory))
            .Concat(expandedArguments.ResponseFiles.Select(path =>
                CreateFileInputHash(path, projectDirectory)))
            .Concat(GetCompilerArgumentFileOperands(
                    cscArguments,
                    projectDirectory)
                .Select(path => CreateFileInputHash(
                    path,
                    projectDirectory)))
            .ToList();
        var orderedArguments = string.Concat(
            cscArguments.Select(argument =>
                $"{argument.Length}:{argument}"));
        additionalInputHashes.Add(
            "ARGS:" + CreateStableDigest(orderedArguments));
        additionalInputHashes = additionalInputHashes
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();
        var effectiveProperties = resolvedProperties
            .EnumerateObject()
            .ToDictionary(
                property => property.Name,
                property => property.Value.GetString()
                    ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);
        return new ResolvedCompilerInputs(
            references,
            analyzerPaths,
            analyzerConfigHashes,
            compileInputHashes,
            additionalInputHashes,
            sourcePaths,
            effectiveProperties,
            cscArguments);
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
            IReadOnlySet<string> directProjectReferences,
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
        var projectDirectory = Path.GetDirectoryName(project)!;

        var resolved = new List<ProjectReferenceSelection>();
        foreach (var reference in references.EnumerateArray())
        {
            var path = GetItemPath(reference, projectDirectory);
            if (path == null
                || string.Equals(
                    GetProperty(reference, "BuildReference"),
                    "false",
                    StringComparison.OrdinalIgnoreCase)
                || !File.Exists(path)
                || !string.Equals(
                    Path.GetExtension(path),
                    ".csproj",
                    StringComparison.OrdinalIgnoreCase)
                || !directProjectReferences.Contains(
                    Canonicalize(path)))
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
                childProperties,
                [
                    .. parentSelection.ProjectGraphPath,
                    Canonicalize(path)
                ],
                [],
                false));
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

    private static IReadOnlyList<string> GetItemPaths(
        JsonElement items,
        string itemName,
        string projectDirectory)
        => items.TryGetProperty(itemName, out var itemValues)
            ? itemValues.EnumerateArray()
                .Select(item => GetItemPath(
                    item,
                    projectDirectory))
                .Where(path => path != null)
                .Select(path => Canonicalize(path!))
                .Distinct(PathComparer)
                .OrderBy(
                    path => CanonicalIdentityPath(path),
                    StringComparer.Ordinal)
                .ToList()
            : [];

    private static IReadOnlyList<string> CreateCompileInputHashes(
        IEnumerable<string> sourcePaths,
        string projectDirectory)
        => sourcePaths.Select(path =>
                CreateFileInputHash(path, projectDirectory))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();

    private static string CreateFileInputHash(
        string path,
        string projectDirectory)
    {
        var canonical = Canonicalize(path);
        var relative = Path.GetRelativePath(
                projectDirectory,
                canonical)
            .Replace('\\', '/');
        var bytes = File.Exists(canonical)
            ? File.ReadAllBytes(canonical)
            : [];
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(bytes));
        return $"{relative.Length}:{relative}"
            + $"{hash.Length}:{hash}";
    }

    private static IReadOnlyDictionary<string, string>
        CreateCompilationProperties(
            IReadOnlyDictionary<string, string> properties)
    {
        return new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["Nullable"] = NormalizeNullable(
                GetProperty(properties, "Nullable")),
            ["AllowUnsafeBlocks"] = NormalizeBoolean(
                GetProperty(properties, "AllowUnsafeBlocks")),
            ["CheckForOverflowUnderflow"] = NormalizeBoolean(
                GetProperty(properties, "CheckForOverflowUnderflow")),
            ["OutputType"] = NormalizeOutputType(
                GetProperty(properties, "OutputType")),
            ["TreatWarningsAsErrors"] = NormalizeBoolean(
                GetProperty(properties, "TreatWarningsAsErrors")),
            ["WarningsAsErrors"] = NormalizeDiagnosticList(
                GetProperty(properties, "WarningsAsErrors")),
            ["WarningsNotAsErrors"] = NormalizeDiagnosticList(
                GetProperty(properties, "WarningsNotAsErrors")),
            ["NoWarn"] = NormalizeDiagnosticList(
                GetProperty(properties, "NoWarn")),
            ["WarningLevel"] = NormalizeWarningLevel(
                GetProperty(properties, "WarningLevel")),
            ["ImplicitUsings"] = NormalizeImplicitUsings(
                GetProperty(properties, "ImplicitUsings"))
        };
    }

    private static string NormalizeBoolean(string value)
        => string.Equals(
                value,
                "true",
                StringComparison.OrdinalIgnoreCase)
            ? "true"
            : "false";

    private static string NormalizeNullable(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "enable" => "enable",
            "warnings" => "warnings",
            "annotations" => "annotations",
            _ => "disable"
        };

    private static string NormalizeOutputType(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "exe" => "exe",
            "winexe" => "winexe",
            "module" => "module",
            "winmdobj" => "winmdobj",
            _ => "library"
        };

    private static string NormalizeDiagnosticList(string value)
        => string.Join(
            ";",
            value.Split(
                    [';', ','],
                    StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries)
                .Select(code => code.ToUpperInvariant())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(code => code, StringComparer.Ordinal));

    private static string NormalizeWarningLevel(string value)
        => int.TryParse(
                value,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var level)
            ? level.ToString(
                System.Globalization.CultureInfo.InvariantCulture)
            : "4";

    private static string NormalizeImplicitUsings(string value)
        => value.Trim().ToLowerInvariant() is "enable" or "enabled" or "true"
            ? "enable"
            : "disable";

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

    internal static string BuildMsBuildPropertyArguments(
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

    private static string SerializeEvaluationKey(
        ProjectEvaluationKey key)
        => $"{key.ProjectFile.Length}:{key.ProjectFile}"
            + $"{key.PropertyState.Length}:{key.PropertyState}";

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

    private sealed record ExpandedResponseArguments(
        IReadOnlyList<string> Arguments,
        IReadOnlyList<string> ResponseFiles);

    private static ExpandedResponseArguments
        ExpandResponseFileArguments(
            IReadOnlyList<string> arguments,
            string baseDirectory)
    {
        var expanded = new List<string>();
        var responseFiles = new List<string>();
        var active = new HashSet<string>(PathComparer);
        foreach (var argument in arguments)
            Expand(argument, baseDirectory);
        return new ExpandedResponseArguments(
            expanded,
            responseFiles
                .Distinct(PathComparer)
                .ToList());

        void Expand(string argument, string currentDirectory)
        {
            var trimmed = argument.Trim();
            if (!trimmed.StartsWith(
                    "@",
                    StringComparison.Ordinal))
            {
                expanded.Add(argument);
                return;
            }
            var responsePath = trimmed[1..].Trim().Trim('"');
            if (!Path.IsPathRooted(responsePath))
                responsePath = Path.Combine(
                    currentDirectory,
                    responsePath);
            responsePath = Canonicalize(responsePath);
            if (!File.Exists(responsePath))
            {
                expanded.Add(argument);
                return;
            }
            if (!active.Add(responsePath))
            {
                expanded.Add(
                    $"#response-cycle:{responsePath}");
                return;
            }
            responseFiles.Add(responsePath);
            var responseDirectory = Path.GetDirectoryName(
                responsePath)!;
            var content = string.Join(
                Environment.NewLine,
                File.ReadLines(responsePath)
                    .Where(line => !line.TrimStart()
                        .StartsWith(
                            "#",
                            StringComparison.Ordinal)));
            foreach (var nested in TokenizeArguments(content))
                Expand(nested, responseDirectory);
            active.Remove(responsePath);
        }
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

    private static IReadOnlyList<KeyValuePair<string, string>>
        ParseFeatures(string value)
        => value.Split(
                [',', ';'],
                StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries)
            .Select(feature =>
            {
                var separator = feature.IndexOf('=');
                return separator > 0
                    ? new KeyValuePair<string, string>(
                        feature[..separator].Trim(),
                        feature[(separator + 1)..].Trim())
                    : new KeyValuePair<string, string>(
                        feature,
                        "true");
            })
            .GroupBy(
                feature => feature.Key,
                StringComparer.Ordinal)
            .Select(group => group.Last())
            .OrderBy(
                feature => feature.Key,
                StringComparer.Ordinal)
            .ToList();

    private static CSharpParseOptions CreateEffectiveParseOptions(
        IReadOnlyDictionary<string, string> properties,
        IReadOnlyList<string> arguments)
    {
        var symbols = GetProperty(properties, "DefineConstants")
            .Split(
                [';', ','],
                StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
        var languageText = GetProperty(properties, "LangVersion");
        var documentationMode =
            string.Equals(
                GetProperty(properties, "GenerateDocumentationFile"),
                "true",
                StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrWhiteSpace(
                GetProperty(properties, "DocumentationFile"))
                ? DocumentationMode.Diagnose
                : DocumentationMode.Parse;
        var features = ParseFeatures(
                GetProperty(properties, "Features"))
            .ToDictionary(
                feature => feature.Key,
                feature => feature.Value,
                StringComparer.Ordinal);
        foreach (var argument in arguments)
        {
            var normalized = argument.Trim().Trim('"');
            if (TryGetSwitchValue(
                    normalized,
                    ["define", "d"],
                    out var defines))
            {
                foreach (var symbol in defines.Split(
                             [';', ','],
                             StringSplitOptions.RemoveEmptyEntries
                             | StringSplitOptions.TrimEntries))
                    symbols.Add(symbol);
            }
            else if (TryGetSwitchValue(
                         normalized,
                         ["langversion"],
                         out var language))
            {
                languageText = language;
            }
            else if (TryGetSwitchValue(
                         normalized,
                         ["features"],
                         out var featureText))
            {
                foreach (var feature in ParseFeatures(featureText))
                    features[feature.Key] = feature.Value;
            }
            else if (TryGetSwitchValue(
                         normalized,
                         ["doc"],
                         out _))
            {
                documentationMode = DocumentationMode.Diagnose;
            }
        }
        var languageVersion = LanguageVersionFacts.TryParse(
            languageText,
            out var parsedLanguage)
            ? parsedLanguage
            : LanguageVersion.Default;
        return new CSharpParseOptions(
                languageVersion,
                documentationMode,
                SourceCodeKind.Regular,
                symbols.OrderBy(
                    symbol => symbol,
                    StringComparer.Ordinal))
            .WithFeatures(features);
    }

    private static bool TryGetSwitchValue(
        string argument,
        IReadOnlyList<string> names,
        out string value)
    {
        foreach (var name in names)
        {
            foreach (var prefix in new[]
                     {
                         $"/{name}:",
                         $"-{name}:"
                     })
            {
                if (!argument.StartsWith(
                        prefix,
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                value = argument[prefix.Length..].Trim('"');
                return true;
            }
        }
        value = string.Empty;
        return false;
    }

    private static IEnumerable<string>
        GetCompilerArgumentFileOperands(
            IReadOnlyList<string> arguments,
            string projectDirectory)
    {
        string[] fileSwitches =
        [
            "additionalfile",
            "addmodule",
            "analyzer",
            "ruleset",
            "keyfile",
            "win32res",
            "win32manifest",
            "appconfig",
            "sourcelink"
        ];
        foreach (var argument in arguments)
        {
            if (!TryGetSwitchValue(
                    argument.Trim().Trim('"'),
                    fileSwitches,
                    out var value))
            {
                continue;
            }
            foreach (var operand in value.Split(
                         ',',
                         StringSplitOptions.RemoveEmptyEntries
                         | StringSplitOptions.TrimEntries))
            {
                var path = operand.Trim('"');
                if (!Path.IsPathRooted(path))
                    path = Path.Combine(projectDirectory, path);
                yield return Canonicalize(path);
            }
        }
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

    private static ProjectReferenceIdentity CreateReferenceIdentity(
        JsonElement reference,
        string projectDirectory,
        bool isProjectReference)
    {
        var path = isProjectReference
            ? GetItemPath(reference, projectDirectory)
                ?? GetProperty(reference, "Identity")
            : ResolveAssemblyReferencePath(
                reference,
                projectDirectory);
        var aliases = GetProperty(reference, "Aliases")
            .Split(
                [',', ';'],
                StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(alias => alias, StringComparer.Ordinal)
            .ToList();
        if (aliases.Count == 0)
            aliases.Add("global");
        var properties = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["EmbedInteropTypes"] = NormalizeBoolean(
                GetProperty(reference, "EmbedInteropTypes"))
        };
        AddProperty("ReferenceSourceTarget");
        AddProperty("NearestTargetFramework");
        AddProperty("TargetFramework");
        AddProperty("SetConfiguration");
        AddProperty("SetPlatform");
        AddProperty("SetTargetFramework");
        AddProperty("AdditionalProperties");
        AddProperty("GlobalPropertiesToRemove");
        AddProperty("UndefineProperties");
        var sourceProject = GetProperty(
            reference,
            "MSBuildSourceProjectFile");
        if (!string.IsNullOrWhiteSpace(sourceProject))
        {
            properties["MSBuildSourceProjectFile"] =
                Path.IsPathRooted(sourceProject)
                    ? Canonicalize(sourceProject)
                    : sourceProject;
        }
        return new ProjectReferenceIdentity(
            isProjectReference && Path.IsPathRooted(path)
                ? Canonicalize(path)
                : path,
            aliases,
            properties,
            string.Empty,
            string.Empty);

        void AddProperty(string name)
        {
            var value = GetProperty(reference, name);
            if (!string.IsNullOrWhiteSpace(value))
                properties[name] = value;
        }
    }

    private static string ResolveAssemblyReferencePath(
        JsonElement reference,
        string projectDirectory)
    {
        if (reference.TryGetProperty(
                "FullPath",
                out var fullPath)
            && !string.IsNullOrWhiteSpace(fullPath.GetString())
            && File.Exists(fullPath.GetString()!))
        {
            return Canonicalize(fullPath.GetString()!);
        }
        var hintPath = GetProperty(reference, "HintPath");
        if (!string.IsNullOrWhiteSpace(hintPath))
        {
            var resolvedHint = Path.IsPathRooted(hintPath)
                ? hintPath
                : Path.Combine(projectDirectory, hintPath);
            return Canonicalize(resolvedHint);
        }
        return $"assembly:{GetProperty(reference, "Identity")}";
    }

    private static string CreateReferenceIdentityKey(
        ProjectReferenceIdentity reference)
        => CanonicalIdentityReference(reference.Path)
            + "|"
            + string.Join(",", reference.Aliases)
            + "|"
            + SerializeProperties(reference.Properties)
            + "|"
            + reference.ContentHash;

    private static ProjectReferenceIdentity
        CreateProjectReferenceStateIdentity(
            ProjectReferenceSelection reference)
        => new(
            Canonicalize(reference.ProjectFile),
            ["global"],
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase),
            string.Empty,
            SerializeEvaluationKey(
                CreateEvaluationKey(reference)));

    private static ProjectFileParseContext AddProvenance(
        ProjectFileParseContext context,
        ProjectReferenceSelection selection)
        => MergeProvenance(
            context,
            context with
            {
                Provenance =
                [
                    new ProjectContextProvenance(
                        selection.ProjectGraphPath.ToList(),
                        CopyProperties(selection.GlobalProperties))
                ]
            });

    private static ProjectFileParseContext MergeProvenance(
        ProjectFileParseContext left,
        ProjectFileParseContext right)
        => left with
        {
            BuildStates = left.BuildStates
                .Concat(right.BuildStates)
                .GroupBy(
                    CreateBuildStateIdentity,
                    StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(
                    CreateBuildStateIdentity,
                    StringComparer.Ordinal)
                .ToList(),
            Provenance = left.Provenance
                .Concat(right.Provenance)
                .GroupBy(
                    CreateProvenanceIdentity,
                    StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(
                    provenance => string.Join(
                        " -> ",
                        provenance.ProjectGraphPath),
                    StringComparer.Ordinal)
                .ThenBy(
                    provenance => SerializeProperties(
                        provenance.GlobalProperties),
                    StringComparer.Ordinal)
                .ToList()
        };

    private static string CreateBuildStateIdentity(
        ProjectBuildState state)
        => CanonicalIdentityPath(state.ProjectFile)
            + "|"
            + SerializeProperties(state.GlobalProperties)
            + "|"
            + state.Configuration
            + "|"
            + state.Platform
            + "|"
            + (state.TargetFramework ?? string.Empty);

    private static string CreateRepresentativeKey(
        ProjectFileParseContext context)
        => CanonicalIdentityPath(context.ProjectFile)
            + "|"
            + context.Configuration
            + "|"
            + context.Platform
            + "|"
            + (context.TargetFramework ?? string.Empty)
            + "|"
            + SerializeProperties(context.GlobalProperties)
            + "|"
            + CreateContextIdentity(context);

    private static string CreateProvenanceIdentity(
        ProjectContextProvenance provenance)
        => string.Join(
                "->",
                provenance.ProjectGraphPath.Select(
                    CanonicalIdentityPath))
            + "|"
            + SerializeProperties(provenance.GlobalProperties);

    private static string FormatProvenance(
        ProjectFileParseContext context)
        => string.Join(
            "; ",
            context.Provenance.Select(provenance =>
                $"[{string.Join(" -> ", provenance.ProjectGraphPath)}]"
                + $" globals={SerializeProperties(provenance.GlobalProperties)}"));

    private static string CreateAmbiguityDiagnostic(
        string sourcePath,
        IReadOnlyList<ProjectFileParseContext> contexts)
    {
        var lines = new List<string>
        {
            $"'{sourcePath}' has materially different evaluated "
            + "parse/reference contexts."
        };
        for (var index = 0; index < contexts.Count; index++)
        {
            lines.Add(
                $"Context {index + 1}: "
                + FormatProvenance(contexts[index]));
            lines.Add(
                $"Identity {index + 1}: "
                + CreateContextIdentity(contexts[index]));
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static string CreateContextIdentity(
        ProjectFileParseContext context)
    {
        var builder = new System.Text.StringBuilder();
        Add(
            "language",
            ((int)context.ParseOptions.LanguageVersion).ToString(
                System.Globalization.CultureInfo.InvariantCulture));
        Add(
            "documentation",
            ((int)context.ParseOptions.DocumentationMode).ToString(
                System.Globalization.CultureInfo.InvariantCulture));
        Add(
            "kind",
            ((int)context.ParseOptions.Kind).ToString(
                System.Globalization.CultureInfo.InvariantCulture));
        foreach (var feature in context.ParseOptions.Features
                     .OrderBy(
                         feature => feature.Key,
                         StringComparer.Ordinal))
        {
            Add($"feature:{feature.Key}", feature.Value);
        }
        foreach (var symbol in context.ParseOptions.PreprocessorSymbolNames
                     .Distinct(StringComparer.Ordinal)
                     .OrderBy(symbol => symbol, StringComparer.Ordinal))
        {
            Add("symbol", symbol);
        }
        foreach (var reference in context.References)
        {
            Add(
                "reference",
                CanonicalIdentityReference(reference.Path)
                + "|"
                + string.Join(",", reference.Aliases)
                + "|"
                + SerializeProperties(reference.Properties)
                + "|"
                + reference.ContentHash);
        }
        foreach (var analyzer in context.AnalyzerPaths)
            Add("analyzer", CanonicalIdentityPath(analyzer));
        foreach (var analyzerConfig in context.AnalyzerConfigHashes)
            Add(
                "analyzerConfig",
                analyzerConfig);
        foreach (var compileInput in context.CompileInputHashes)
            Add("compileInput", compileInput);
        foreach (var additionalInput in context.AdditionalInputHashes)
            Add("additionalInput", additionalInput);
        foreach (var property in context.CompilationProperties
                     .OrderBy(
                         property => property.Key,
                         StringComparer.OrdinalIgnoreCase))
        {
            Add(
                $"compilation:{property.Key.ToUpperInvariant()}",
                property.Value);
        }
        return builder.ToString();

        void Add(string name, string value)
        {
            builder.Append(name.Length)
                .Append(':')
                .Append(name)
                .Append(value.Length)
                .Append(':')
                .Append(value);
        }
    }

    private static IReadOnlyDictionary<string, string> CopyProperties(
        IReadOnlyDictionary<string, string> properties)
        => new Dictionary<string, string>(
            properties,
            StringComparer.OrdinalIgnoreCase);

    private static string SerializeProperties(
        IReadOnlyDictionary<string, string> properties)
        => string.Concat(properties
            .OrderBy(
                property => property.Key,
                StringComparer.OrdinalIgnoreCase)
            .Select(property =>
                $"{property.Key.Length}:"
                + property.Key.ToUpperInvariant()
                + $"{property.Value.Length}:{property.Value}"));

    private static string CanonicalIdentityPath(string path)
    {
        var canonical = Canonicalize(path);
        return OperatingSystem.IsWindows()
            ? canonical.ToUpperInvariant()
            : canonical;
    }

    private static string CanonicalIdentityReference(string reference)
        => reference.StartsWith(
                "assembly:",
                StringComparison.Ordinal)
            ? reference
            : CanonicalIdentityPath(reference);

    private static string CreateAnalyzerConfigHash(
        string path)
    {
        var bytes = File.Exists(path)
            ? File.ReadAllBytes(path)
            : System.Text.Encoding.UTF8.GetBytes("<missing>");
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(bytes));
        var canonicalPath = CanonicalIdentityPath(path);
        return $"{canonicalPath.Length}:{canonicalPath}"
            + $"{hash.Length}:{hash}";
    }

    private static string GetProperty(JsonElement properties, string name)
        => properties.TryGetProperty(name, out var property)
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static string GetProperty(
        IReadOnlyDictionary<string, string> properties,
        string name)
        => properties.TryGetValue(name, out var value)
            ? value
            : string.Empty;

    internal static string Canonicalize(string path)
        => Path.GetFullPath(path)
            .Replace("/private/var/", "/var/");

    internal static StringComparer PathComparer =>
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
}
