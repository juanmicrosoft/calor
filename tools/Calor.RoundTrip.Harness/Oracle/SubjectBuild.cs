using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Calor.RoundTrip.Harness.Oracle;

/// <summary>
/// #1587: the subject library, compiled in-process from the exact <c>csc</c> arguments MSBuild
/// computes for it. The original is emitted once; each candidate swaps one syntax tree and emits
/// again, so "build the project twice" costs one Roslyn emit per file instead of a dotnet build.
/// Source generators from the project's analyzer references run on every emit.
/// </summary>
public sealed class SubjectBuild
{
    private readonly CSharpCompilation _compilation;
    private readonly ImmutableArray<ISourceGenerator> _generators;

    public string ProjectFile { get; }
    public string TargetFramework { get; }
    public CSharpParseOptions ParseOptions { get; }
    public IReadOnlyDictionary<string, string> DependencyPaths { get; }

    /// <summary>Everything a child process needs to rebuild the same compilation without MSBuild.</summary>
    public sealed record Spec(string ProjectFile, string TargetFramework, string[] CompilerArguments, Dictionary<string, string> DependencyPaths);

    public Spec BuildSpec { get; }

    /// <summary>Analyzer references whose generators could not be loaded (reported when the original fails to compile).</summary>
    public IReadOnlyList<string> GeneratorLoadFailures { get; private init; } = [];

    public int GeneratorCount => _generators.Length;

    private SubjectBuild(Spec spec, CSharpCompilation compilation, ImmutableArray<ISourceGenerator> generators)
    {
        BuildSpec = spec;
        ProjectFile = spec.ProjectFile;
        TargetFramework = spec.TargetFramework;
        _compilation = compilation;
        _generators = generators;
        ParseOptions = (CSharpParseOptions)compilation.SyntaxTrees.First().Options;
        DependencyPaths = spec.DependencyPaths;
    }

    /// <summary>The source trees of the build, by full path.</summary>
    public IReadOnlyDictionary<string, SyntaxTree> Trees =>
        _compilation.SyntaxTrees.Where(t => t.FilePath.Length > 0).GroupBy(t => Path.GetFullPath(t.FilePath), PathComparer)
            .ToDictionary(g => g.Key, g => g.First(), PathComparer);

    public static StringComparer PathComparer => OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    /// <summary>Builds the project once with dotnet (restore, project references, generated files), then
    /// captures its compiler command line and dependency paths.</summary>
    public static async Task<SubjectBuild> CreateAsync(string projectFile, RoundTripConfig config, CancellationToken cancellationToken)
    {
        var projectDir = Path.GetDirectoryName(projectFile)!;
        var env = new Dictionary<string, string> { ["MSBUILDDISABLENODEREUSE"] = "1" };
        var tfm = await SelectTargetFrameworkAsync(projectFile, config, env, cancellationToken);
        var common = $"-p:TargetFramework={tfm} -p:Configuration={config.Configuration} {config.ExtraBuildProperties}";

        var (buildExit, buildOut, buildErr) = await ProcessRunner.RunAsync(config.DotnetPath,
            $"build \"{projectFile}\" {common} -nodeReuse:false --verbosity quiet", projectDir, config.BuildTimeout, env, cancellationToken);
        if (buildExit != 0)
            throw new InvalidOperationException($"dotnet build of the original subject failed ({buildExit}):\n{Tail(buildOut + buildErr)}");

        var neverFile = Path.Combine(projectDir, "obj", ".calor-oracle-1587.never");
        var (exit, stdout, stderr) = await ProcessRunner.RunAsync(config.DotnetPath,
            $"msbuild \"{projectFile}\" -target:ResolveReferences;CoreCompile {common} -p:BuildProjectReferences=false "
            + $"-p:SkipCompilerExecution=true -p:ProvideCommandLineArgs=true \"-p:NonExistentFile={neverFile}\" "
            + "-getItem:CscCommandLineArgs,ReferencePath,RuntimeCopyLocalItems -nodeReuse:false -verbosity:quiet",
            projectDir, config.BuildTimeout, env, cancellationToken);
        if (exit != 0)
            throw new InvalidOperationException($"Could not capture the compiler command line ({exit}):\n{Tail(stdout + stderr)}");

        using var json = JsonDocument.Parse(stdout);
        var items = json.RootElement.GetProperty("Items");
        var args = Identities(items, "CscCommandLineArgs").ToArray();
        if (args.Length == 0)
            throw new InvalidOperationException("MSBuild reported no compiler arguments.");
        return FromSpec(new Spec(projectFile, tfm, args, DependencyMap(items, projectDir)), cancellationToken);
    }

    /// <summary>Rebuilds the compilation from a captured compiler command line (no MSBuild).</summary>
    public static SubjectBuild FromSpec(Spec spec, CancellationToken cancellationToken = default)
    {
        var projectDir = Path.GetDirectoryName(spec.ProjectFile)!;
        var parsed = CSharpCommandLineParser.Default.Parse(spec.CompilerArguments, projectDir, sdkDirectory: null);
        var fatal = parsed.Errors.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (fatal.Count > 0)
            throw new InvalidOperationException("Could not parse the compiler command line: " + string.Join("; ", fatal));

        var trees = parsed.SourceFiles.Select(f => CSharpSyntaxTree.ParseText(
            Microsoft.CodeAnalysis.Text.SourceText.From(File.ReadAllText(f.Path), Encoding.UTF8),
            parsed.ParseOptions, Path.GetFullPath(f.Path), cancellationToken)).ToList();
        var references = parsed.MetadataReferences.Select(r => (MetadataReference)MetadataReference.CreateFromFile(
            Path.GetFullPath(r.Reference, projectDir), r.Properties)).ToList();

        // Warnings stay warnings: the oracle compares behavior, and the round-trip legs own warning policy.
        var options = parsed.CompilationOptions
            .WithGeneralDiagnosticOption(ReportDiagnostic.Default)
            .WithSpecificDiagnosticOptions(parsed.CompilationOptions.SpecificDiagnosticOptions
                .Select(kv => KeyValuePair.Create(kv.Key, kv.Value == ReportDiagnostic.Error ? ReportDiagnostic.Warn : kv.Value)))
            .WithCryptoKeyFile(null).WithCryptoKeyContainer(null).WithCryptoPublicKey(default).WithDelaySign(null)
            .WithDeterministic(true)
            .WithOutputKind(OutputKind.DynamicallyLinkedLibrary);
        var compilation = CSharpCompilation.Create(parsed.CompilationName ?? Path.GetFileNameWithoutExtension(spec.ProjectFile), trees, references, options);

        var loader = new GeneratorAssemblyLoader();
        var failures = new List<string>();
        var generators = parsed.AnalyzerReferences
            .Select(a =>
            {
                var reference = new AnalyzerFileReference(Path.GetFullPath(a.FilePath, projectDir), loader);
                reference.AnalyzerLoadFailed += (_, e) => failures.Add($"{Path.GetFileName(reference.FullPath)}: {e.ErrorCode} {e.Message}");
                return reference;
            })
            .SelectMany(r => r.GetGenerators(LanguageNames.CSharp))
            .ToImmutableArray();
        return new SubjectBuild(spec, compilation, generators) { GeneratorLoadFailures = failures };
    }

    /// <summary>The configured target framework when the project declares it, else the newest .NET (Core) one, else the first.</summary>
    private static async Task<string> SelectTargetFrameworkAsync(string projectFile, RoundTripConfig config, Dictionary<string, string> env, CancellationToken cancellationToken)
    {
        var (exit, stdout, stderr) = await ProcessRunner.RunAsync(config.DotnetPath,
            $"msbuild \"{projectFile}\" -getProperty:TargetFramework,TargetFrameworks -nodeReuse:false",
            Path.GetDirectoryName(projectFile)!, config.BuildTimeout, env, cancellationToken);
        if (exit != 0)
            throw new InvalidOperationException($"Could not evaluate target frameworks:\n{Tail(stdout + stderr)}");
        using var json = JsonDocument.Parse(stdout);
        var props = json.RootElement.GetProperty("Properties");
        var declared = (props.GetProperty("TargetFrameworks").GetString() is { Length: > 0 } many ? many : props.GetProperty("TargetFramework").GetString() ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (config.TargetFramework is { } wanted && declared.Contains(wanted, StringComparer.OrdinalIgnoreCase))
            return wanted;
        var modern = declared.Where(t => t.StartsWith("net", StringComparison.OrdinalIgnoreCase) && t.Contains('.') && !t.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase)
                && !t.StartsWith("netcoreapp", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(t => Version.TryParse(t[3..].Split('-')[0], out var v) ? v : new Version(0, 0)).ToList();
        return modern.FirstOrDefault() ?? declared.FirstOrDefault()
            ?? throw new InvalidOperationException($"{projectFile} declares no target framework.");
    }

    /// <summary>
    /// Runtime assemblies for the subject's own dependencies, by simple name: package runtime assets
    /// first, then project and file references. Framework reference assemblies (targeting packs) and
    /// anything the host already provides through its trusted platform assemblies are left to the
    /// default context.
    /// </summary>
    private static Dictionary<string, string> DependencyMap(JsonElement items, string projectDir)
    {
        var host = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.Contains($"{Path.DirectorySeparatorChar}shared{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Identities(items, "RuntimeCopyLocalItems").Concat(Identities(items, "ReferencePath"))
                     .Select(p => Path.GetFullPath(p, projectDir)))
        {
            if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)
                || path.Contains($"{Path.DirectorySeparatorChar}packs{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                continue;
            var name = Path.GetFileNameWithoutExtension(path);
            if (host.Contains(name))
                continue;
            map.TryAdd(name, path);
        }
        return map;
    }

    private static IEnumerable<string> Identities(JsonElement items, string name) =>
        items.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Select(i => i.GetProperty("Identity").GetString() ?? "").Where(s => s.Length > 0)
            : [];

    private static string Tail(string text) => text.Length <= 4000 ? text : text[^4000..];

    /// <summary>Emits the build with the given files' text replaced (none for the original).</summary>
    public ArmImage? Emit(string name, IReadOnlyDictionary<string, string> replacements, out List<string> errors, CancellationToken cancellationToken = default)
    {
        var compilation = _compilation;
        foreach (var (path, text) in replacements)
        {
            var old = compilation.SyntaxTrees.Single(t => PathComparer.Equals(Path.GetFullPath(t.FilePath), path));
            compilation = compilation.ReplaceSyntaxTree(old, CSharpSyntaxTree.ParseText(text, ParseOptions, old.FilePath, Encoding.UTF8, cancellationToken));
        }
        if (_generators.Length > 0)
        {
            var driver = CSharpGeneratorDriver.Create(_generators, parseOptions: ParseOptions, optionsProvider: null);
            driver.RunGeneratorsAndUpdateCompilation(compilation, out var generated, out _, cancellationToken);
            compilation = (CSharpCompilation)generated;
        }
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream, cancellationToken: cancellationToken);
        errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString()).Take(10).ToList();
        return result.Success ? new ArmImage(name, stream.ToArray(), DependencyPaths) : null;
    }

    /// <summary>
    /// The types a swapped file can affect: every type declared in the file (any accessibility, so an
    /// internal helper reaches the public types that use it), plus the types declared in any other file
    /// that mentions one of those type names. Names are CLR metadata names.
    /// </summary>
    public IReadOnlyList<string> AffectedTypes(string path, string? replacementText, bool includeDependents)
    {
        var tree = Trees[path];
        var declared = DeclaredTypes(_compilation, tree).ToHashSet(StringComparer.Ordinal);
        if (replacementText is not null)
        {
            var swapped = _compilation.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(replacementText, ParseOptions, tree.FilePath, Encoding.UTF8));
            declared.UnionWith(DeclaredTypes(swapped, swapped.SyntaxTrees.Single(t => t.FilePath == tree.FilePath)));
        }
        if (!includeDependents)
            return declared.Order(StringComparer.Ordinal).ToList();
        var simpleNames = declared.Select(n => n.Split('.', '+')[^1].Split('`')[0]).ToHashSet(StringComparer.Ordinal);
        var scope = new HashSet<string>(declared, StringComparer.Ordinal);
        foreach (var other in _compilation.SyntaxTrees.Where(t => t != tree))
        {
            var mentions = other.GetRoot().DescendantTokens()
                .Any(t => t.IsKind(SyntaxKind.IdentifierToken) && simpleNames.Contains(t.ValueText) && t.Parent is SimpleNameSyntax);
            if (mentions)
                scope.UnionWith(DeclaredTypes(_compilation, other));
        }
        return scope.Order(StringComparer.Ordinal).ToList();
    }

    private static IEnumerable<string> DeclaredTypes(CSharpCompilation compilation, SyntaxTree tree)
    {
        var model = compilation.GetSemanticModel(tree);
        foreach (var node in tree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            if (model.GetDeclaredSymbol(node) is INamedTypeSymbol symbol)
                yield return MetadataName(symbol);
    }

    public static string MetadataName(INamedTypeSymbol symbol)
    {
        var parts = new Stack<string>();
        for (var t = symbol; t is not null; t = t.ContainingType)
            parts.Push(t.MetadataName);
        var ns = symbol.ContainingNamespace is { IsGlobalNamespace: false } n ? n.ToDisplayString() + "." : "";
        return ns + string.Join("+", parts);
    }

    /// <summary>
    /// Loads one build's generators into their own load context. Two subjects can ship different versions of
    /// the same generator (the net8 and net10 regex generators, for example); in the default context the first
    /// one loaded would win for every later build. Roslyn itself is shared from the default context.
    /// </summary>
    private sealed class GeneratorAssemblyLoader : IAnalyzerAssemblyLoader
    {
        private readonly GeneratorLoadContext _context = new();

        public void AddDependencyLocation(string fullPath) => _context.Add(fullPath);

        public Assembly LoadFromPath(string fullPath)
        {
            _context.Add(fullPath);
            return _context.LoadFromAssemblyPath(fullPath);
        }
    }

    private sealed class GeneratorLoadContext() : System.Runtime.Loader.AssemblyLoadContext("calor-oracle-generators")
    {
        private readonly Dictionary<string, string> _paths = new(StringComparer.OrdinalIgnoreCase);

        public void Add(string path)
        {
            lock (_paths)
                _paths.TryAdd(Path.GetFileNameWithoutExtension(path), path);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name is not { } name || name.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal))
                return null;
            lock (_paths)
                return _paths.TryGetValue(name, out var path) ? LoadFromAssemblyPath(path) : null;
        }
    }
}
