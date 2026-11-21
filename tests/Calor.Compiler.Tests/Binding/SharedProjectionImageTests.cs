using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Incremental;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Pins the shared source image (<see cref="CallGraphAnalysis.SharedProjectionImage"/>).
/// A project pass used to compile one C# image per module projection (the module plus
/// every companion), and the proof gate's error check compiled every companion body
/// each time, so index builds were quadratic in project size. One image per pass now
/// serves every module whose projection text it holds. These tests check that the
/// shared image gives exactly the bindings the per-module compilation gives, that a
/// changed source never reuses it, and that every module of a project shares one
/// compilation.
/// </summary>
public sealed class SharedProjectionImageTests
{
    private static readonly string[] Subjects =
    [
        "tests/TestData/Benchmarks/DesignPatterns",
        "tests/TestData/Benchmarks/OOPGenerics",
        "tests/TestData/Benchmarks/DomainProblems",
        "tests/TestData/Benchmarks/ContractVerification",
        "bench/phase0-agent-native/latency/fixture-10k/src",
    ];

    public static IEnumerable<object[]> SubjectModes() =>
        from subject in Subjects
        from mode in new[] { ContractMode.Debug, ContractMode.Release }
        select new object[] { subject, mode };

    /// <summary>
    /// Every module of a real multi-file project binds against the shared image (no
    /// module falls back), all of them in one compilation, and each module's bindings
    /// are exactly the ones its own per-module compilation produces.
    /// </summary>
    [Theory]
    [MemberData(nameof(SubjectModes))]
    public void SharedImage_GivesEveryModuleItsOwnCompilationsBindings(string subject, ContractMode mode)
    {
        var project = Load(subject);
        var unshared = new UnsharedContext(project.Registry);
        var map = CompilationDriver.BuildCrossModuleFunctionMap(project.Modules.Select(module => module.Ast).ToList());
        var compilations = new HashSet<CSharpCompilation>(ReferenceEqualityComparer.Instance);
        var checkedModules = 0;
        foreach (var (ast, path) in project.Modules)
        {
            var image = project.Registry.SharedImage(path, mode);
            Assert.NotNull(image);
            var source = CallGraphAnalysis.EmitAnnotatedProjection(ast, mode, map, image!.Markers).Source;
            var selection = image.Select(path, source, [new CSharpParseOptions(LanguageVersion.Preview)]);
            Assert.True(selection is not null, $"{Path.GetFileName(path)} did not share the image");
            compilations.Add(Assert.Single(selection!.Compilations).Compilation);

            var sharedDiagnostics = new DiagnosticBag();
            var shared = CallGraphAnalysis.ResolveOperatorCalls(ast, sharedDiagnostics, mode,
                singleModuleContext: false, crossModuleTypes: project.Registry, sourcePath: path,
                crossModuleFunctions: map);
            var ownDiagnostics = new DiagnosticBag();
            var own = CallGraphAnalysis.ResolveOperatorCalls(ast, ownDiagnostics, mode,
                singleModuleContext: false, crossModuleTypes: unshared, sourcePath: path,
                crossModuleFunctions: map);
            Assert.Equal(Dump(own), Dump(shared));
            Assert.Equal(ownDiagnostics.Select(Describe), sharedDiagnostics.Select(Describe));
            checkedModules++;
        }
        Assert.Equal(project.Modules.Count, checkedModules);
        Assert.Single(compilations);
    }

    /// <summary>
    /// The image is keyed by the projection's text. A module whose source changed
    /// after the registry was built (an editor buffer, an incremental rebuild) projects
    /// differently, never selects the stale image, and binds exactly as its own
    /// compilation binds it.
    /// </summary>
    [Fact]
    public void ChangedSource_NeverReusesTheImage()
    {
        var project = Load(Subjects[0]);
        var (original, path) = project.Modules[0];
        var text = File.ReadAllText(path) + """

              func AddedAfterRegistry public (int:x) → int
                !
                return (x + 1)
            """;
        var diagnostics = new DiagnosticBag();
        var changed = new Parser(new Lexer(text, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var map = CompilationDriver.BuildCrossModuleFunctionMap(project.Modules.Select(module => module.Ast).ToList());

        var image = project.Registry.SharedImage(path, ContractMode.Debug)!;
        var options = new[] { new CSharpParseOptions(LanguageVersion.Preview) };
        Assert.NotNull(image.Select(path,
            CallGraphAnalysis.EmitAnnotatedProjection(original, ContractMode.Debug, map, image.Markers).Source, options));
        Assert.Null(image.Select(path,
            CallGraphAnalysis.EmitAnnotatedProjection(changed, ContractMode.Debug, map, image.Markers).Source, options));

        var shared = CallGraphAnalysis.ResolveOperatorCalls(changed, new DiagnosticBag(), ContractMode.Debug,
            singleModuleContext: false, crossModuleTypes: project.Registry, sourcePath: path, crossModuleFunctions: map);
        var own = CallGraphAnalysis.ResolveOperatorCalls(changed, new DiagnosticBag(), ContractMode.Debug,
            singleModuleContext: false, crossModuleTypes: new UnsharedContext(project.Registry), sourcePath: path,
            crossModuleFunctions: map);
        Assert.Equal(Dump(own), Dump(shared));
    }

    /// <summary>
    /// #1738: a project pass asks for one module's operator bindings several times (the
    /// symbol index's binder, the effect pass's binder, the effect pass's call graph).
    /// A repeated request returns the first answer, and every answer the pass returns
    /// is exactly the one a fresh per-module compilation computes for that request.
    /// </summary>
    [Theory]
    [MemberData(nameof(DebugSubjects))]
    public void RepeatedRequest_ReturnsTheAnswerAFreshCompilationComputes(string subject)
    {
        const ContractMode mode = ContractMode.Debug;
        var project = Load(subject);
        var fresh = Load(subject);
        var unshared = new UnsharedContext(fresh.Registry);
        var map = CompilationDriver.BuildCrossModuleFunctionMap(project.Modules.Select(module => module.Ast).ToList());
        var freshMap = CompilationDriver.BuildCrossModuleFunctionMap(fresh.Modules.Select(module => module.Ast).ToList());
        for (var index = 0; index < project.Modules.Count; index++)
        {
            var (ast, path) = project.Modules[index];
            var (freshAst, freshPath) = fresh.Modules[index];

            // The binders' request (no map, single-module context), twice.
            var firstDiagnostics = new DiagnosticBag();
            var first = CallGraphAnalysis.ResolveOperatorCalls(ast, firstDiagnostics, mode,
                crossModuleTypes: project.Registry, sourcePath: path);
            var secondDiagnostics = new DiagnosticBag();
            var second = CallGraphAnalysis.ResolveOperatorCalls(ast, secondDiagnostics, mode,
                crossModuleTypes: project.Registry, sourcePath: path);
            Assert.Same(first, second);
            Assert.Equal(firstDiagnostics.Select(Describe), secondDiagnostics.Select(Describe));
            var expectedDiagnostics = new DiagnosticBag();
            Assert.Equal(Dump(CallGraphAnalysis.ResolveOperatorCalls(freshAst, expectedDiagnostics, mode,
                crossModuleTypes: unshared, sourcePath: freshPath)), Dump(first));
            Assert.Equal(expectedDiagnostics.Select(Describe), firstDiagnostics.Select(Describe));

            // The call graph's request (the map, project context): answered from the
            // same entry only when its text is the same and the context is not read.
            var pass = CallGraphAnalysis.ResolveOperatorCalls(ast, new DiagnosticBag(), mode,
                singleModuleContext: false, crossModuleTypes: project.Registry, sourcePath: path,
                crossModuleFunctions: map);
            Assert.Equal(Dump(CallGraphAnalysis.ResolveOperatorCalls(freshAst, new DiagnosticBag(), mode,
                singleModuleContext: false, crossModuleTypes: unshared, sourcePath: freshPath,
                crossModuleFunctions: freshMap)), Dump(pass));
        }
    }

    public static IEnumerable<object[]> DebugSubjects() => Subjects.Select(subject => new object[] { subject });

    /// <summary>
    /// The cross-module map is not part of a request's key: it reaches the answer only
    /// through the projection text, which the memo compares. A request whose map
    /// qualifies calls differently projects different text and is resolved afresh,
    /// exactly as its own compilation resolves it.
    /// </summary>
    [Fact]
    public void RequestWhoseProjectionDiffers_IsResolvedAfresh()
    {
        const ContractMode mode = ContractMode.Debug;
        var project = Load(Subjects[^1]);
        var fresh = Load(Subjects[^1]);
        var map = CompilationDriver.BuildCrossModuleFunctionMap(project.Modules.Select(module => module.Ast).ToList());
        static Dictionary<string, CodeGen.CrossModuleFunctionTarget> Redirect(
            IReadOnlyDictionary<string, CodeGen.CrossModuleFunctionTarget> targets) =>
            targets.ToDictionary(entry => entry.Key,
                entry => entry.Value with { ModuleClassName = "Elsewhere" + entry.Value.ModuleClassName },
                StringComparer.Ordinal);
        var redirected = Redirect(map);
        var freshRedirected = Redirect(
            CompilationDriver.BuildCrossModuleFunctionMap(fresh.Modules.Select(module => module.Ast).ToList()));
        var checkedModules = 0;
        for (var index = 0; index < project.Modules.Count && checkedModules < 4; index++)
        {
            var (ast, path) = project.Modules[index];
            var markers = project.Registry.SharedImage(path, mode)!.Markers;
            if (CallGraphAnalysis.EmitAnnotatedProjection(ast, mode, map, markers).Source
                == CallGraphAnalysis.EmitAnnotatedProjection(ast, mode, redirected, markers).Source)
                continue;
            var plain = CallGraphAnalysis.ResolveOperatorCalls(ast, new DiagnosticBag(), mode,
                singleModuleContext: false, crossModuleTypes: project.Registry, sourcePath: path,
                crossModuleFunctions: map);
            var moved = CallGraphAnalysis.ResolveOperatorCalls(ast, new DiagnosticBag(), mode,
                singleModuleContext: false, crossModuleTypes: project.Registry, sourcePath: path,
                crossModuleFunctions: redirected);
            Assert.NotSame(plain, moved);
            var (freshAst, freshPath) = fresh.Modules[index];
            Assert.Equal(Dump(CallGraphAnalysis.ResolveOperatorCalls(freshAst, new DiagnosticBag(), mode,
                singleModuleContext: false, crossModuleTypes: new UnsharedContext(fresh.Registry),
                sourcePath: freshPath, crossModuleFunctions: freshRedirected)), Dump(moved));
            checkedModules++;
        }
        Assert.True(checkedModules > 0, "no module's projection depends on the cross-module map");
    }

    /// <summary>
    /// A file the registry does not hold gets no image and no companions, as before.
    /// </summary>
    [Fact]
    public void UnregisteredPath_HasNoImage()
    {
        var project = Load(Subjects[0]);
        var outside = Path.Combine(Path.GetTempPath(), $"calor-not-registered-{Guid.NewGuid():N}.calr");
        Assert.Null(project.Registry.SharedImage(outside, ContractMode.Debug));
        Assert.Null(project.Registry.SharedImage(null, ContractMode.Debug));
        Assert.Empty(project.Registry.CompanionSources(outside, ContractMode.Debug));
    }

    private sealed record Project(List<(ModuleNode Ast, string Path)> Modules, CrossModuleTypeRegistry Registry);

    private static Project Load(string subject)
    {
        var directory = Path.Combine(ActiveSyntaxInputs.RepositoryRoot, ActiveSyntaxInputs.RelativeRoot, subject);
        var modules = new List<(ModuleNode Ast, string Path)>();
        var hashes = new Dictionary<string, string>(BuildStateCache.GetPathComparer());
        foreach (var path in Directory.GetFiles(directory, "*.calr").Order(StringComparer.Ordinal))
        {
            var bytes = File.ReadAllBytes(path);
            var diagnostics = new DiagnosticBag();
            var ast = new Parser(new Lexer(CompilationDriver.DecodeSource(bytes), diagnostics)
                .TokenizeAllForParser(), diagnostics).Parse();
            if (ast is null || diagnostics.HasErrors)
                continue;
            modules.Add((ast, Path.GetFullPath(path)));
            hashes[Path.GetFullPath(path)] = BuildStateCache.ComputeContentHash(bytes);
        }
        Assert.True(modules.Count > 1, $"{subject} must be a multi-file project");
        return new(modules, CrossModuleTypeRegistry.Build(modules, hashes));
    }

    // The registry's companions without its shared image: exactly the per-module
    // compilation every projection built before the image existed.
    private sealed class UnsharedContext(ISourceBindingContext inner) : ISourceBindingContext
    {
        public IReadOnlyList<SourceProjection> CompanionSources(string? sourcePath, ContractMode mode) =>
            inner.CompanionSources(sourcePath, mode);

        public SourceCallable? FindCallable(AstNode declaration) => inner.FindCallable(declaration);
    }

    // Every binding the projection records, rendered without object identity: Calor
    // nodes by kind and span, Roslyn symbols by display string, operations by their
    // C# text. Marker GUIDs differ between images, so they are blanked.
    private static List<string> Dump(CallGraphAnalysis.OperatorBindings bindings)
    {
        var lines = new List<string>();
        foreach (var property in typeof(CallGraphAnalysis.OperatorBindings)
                     .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                     .OrderBy(property => property.Name, StringComparer.Ordinal))
        {
            var value = property.GetValue(bindings);
            IEnumerable<string> entries = value switch
            {
                IDictionary dictionary => Entries(dictionary)
                    .Select(entry => Describe(entry.Key) + " => " + Describe(entry.Value))
                    .Order(StringComparer.Ordinal),
                IList list => list.Cast<object?>().Select(Describe),
                IEnumerable set => set.Cast<object?>().Select(Describe).Order(StringComparer.Ordinal),
                _ => [Describe(value)]
            };
            lines.AddRange(entries.Select(entry => property.Name + ": " + entry));
        }
        return lines;
    }

    private static IEnumerable<DictionaryEntry> Entries(IDictionary dictionary)
    {
        var entries = dictionary.GetEnumerator();
        while (entries.MoveNext())
            yield return entries.Entry;
    }

    private static string Describe(object? value) => Guids.Replace(DescribeRaw(value, 0), "G");

    private static readonly Regex Guids = new("[0-9a-f]{32}", RegexOptions.CultureInvariant);

    private static string DescribeRaw(object? value, int depth) => value switch
    {
        null => "null",
        string text => text,
        AstNode node => $"{node.GetType().Name}@{node.Span.Start}..{node.Span.End}",
        ISymbol symbol => $"{symbol.Kind}:{symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}",
        IOperation operation => $"{operation.Kind}:{operation.Syntax}",
        Calor.Compiler.Diagnostics.Diagnostic diagnostic => $"{diagnostic.Code}@{diagnostic.Span.Start}..{diagnostic.Span.End}:{diagnostic.Message}",
        TextSpan or bool or int or Enum => value.ToString()!,
        IDictionary dictionary => "{" + string.Join(", ", Entries(dictionary)
            .Select(entry => DescribeRaw(entry.Key, depth + 1) + "=" + DescribeRaw(entry.Value, depth + 1))
            .Order(StringComparer.Ordinal)) + "}",
        IEnumerable items => "[" + string.Join(", ", items.Cast<object?>()
            .Select(item => DescribeRaw(item, depth + 1))) + "]",
        _ when depth > 4 => value.ToString()!,
        _ => value.GetType().Name + "(" + string.Join(", ", value.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(property => property.GetIndexParameters().Length == 0 && property.Name != "EqualityContract")
            .Select(property => property.Name + "=" + DescribeRaw(property.GetValue(value), depth + 1))
            .Concat(value.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Select(field => field.Name + "=" + DescribeRaw(field.GetValue(value), depth + 1)))) + ")"
    };
}

/// <summary>
/// The effect summary's call collector used to bind every module a second time with
/// exactly the inputs the call graph had just bound it with. It now reads the call
/// graph's bound module; these tests pin that the collected calls are the ones its own
/// binding collects, and that the reuse happens only for identical inputs.
/// </summary>
public sealed class EffectSummaryBoundModuleReuseTests
{
    public static IEnumerable<object[]> Subjects() =>
    [
        ["tests/TestData/Benchmarks/DesignPatterns"],
        ["tests/TestData/Benchmarks/OOPGenerics"],
        ["tests/TestData/Benchmarks/DataStructures"],
        ["bench/phase0-agent-native/latency/fixture-10k/src"],
    ];

    [Theory]
    [MemberData(nameof(Subjects))]
    public void CollectedCalls_AreTheOnesASecondBindingCollects(string subject)
    {
        var directory = Path.Combine(ActiveSyntaxInputs.RepositoryRoot, ActiveSyntaxInputs.RelativeRoot, subject);
        var modules = Directory.GetFiles(directory, "*.calr").Order(StringComparer.Ordinal)
            .Select(path =>
            {
                var diagnostics = new DiagnosticBag();
                return new Parser(new Lexer(File.ReadAllText(path), diagnostics).TokenizeAllForParser(),
                    diagnostics).Parse();
            })
            .Where(module => module is not null)
            .ToList();
        var map = CompilationDriver.BuildCrossModuleFunctionMap(modules);
        // Equal content, another instance: the call graph's binding is then not the
        // collector's (the reuse is keyed by identity), so the collector binds itself.
        var equalMap = CompilationDriver.BuildCrossModuleFunctionMap(modules);
        var reused = 0;
        foreach (var module in modules)
        {
            var graph = CallGraphAnalysis.Build(module, map);
            if (graph.PlainBoundModule(module, map, graph.ContractInheritance) is not null)
                reused++;
            Assert.Null(graph.PlainBoundModule(module, equalMap, graph.ContractInheritance));
            var withReuse = ExternalCallCollector.CollectPerFunctionWithBareNames(module, map, graph);
            var withoutReuse = ExternalCallCollector.CollectPerFunctionWithBareNames(module, equalMap,
                CallGraphAnalysis.Build(module, map));
            Assert.Equal(withoutReuse, withReuse);
        }
        Assert.Equal(modules.Count, reused);
    }

    [Fact]
    public void ProjectContextBinding_IsNeverReused()
    {
        var source = """
            module Reuse
              func Twice public (int:x) → int
                !
                return (x + x)
            """;
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        var path = Path.Combine(Path.GetTempPath(), "reuse.calr");
        var registry = CrossModuleTypeRegistry.Build([module]);
        var contextual = CallGraphAnalysis.Build(module, null, crossModuleTypes: registry);
        Assert.Null(contextual.PlainBoundModule(module, null, contextual.ContractInheritance));
        var withPath = CallGraphAnalysis.Build(module, null, sourcePath: path);
        Assert.Null(withPath.PlainBoundModule(module, null, withPath.ContractInheritance));
        var release = CallGraphAnalysis.Build(module, null, projectionContractMode: ContractMode.Release);
        Assert.Null(release.PlainBoundModule(module, null, release.ContractInheritance));
        var plain = CallGraphAnalysis.Build(module, null);
        Assert.NotNull(plain.PlainBoundModule(module, null, plain.ContractInheritance));
        Assert.Null(plain.PlainBoundModule(module, null, contextual.ContractInheritance));
    }
}
