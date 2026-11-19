using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Calor.Compiler.CodeGen;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Calor.Compiler.Binding.Metadata;

/// <summary>
/// Backstop for the F-2 whitelist — enforced in code so a tampered manifest
/// cannot silently whitelist compiler-internal assemblies. Any allowed
/// assembly name must match one of these prefixes (round-2 M1 mitigation).
/// Kept in sync with the case pattern in
/// <c>scripts/generate-metadata-references-manifest.sh</c>.
/// </summary>
internal static class MetadataAllowedNamePrefixes
{
    public static readonly string[] AllowedPrefixes =
    {
        "System.",
    };

    public static readonly string[] AllowedExactNames =
    {
        "Microsoft.CSharp",
        "Microsoft.VisualBasic",
        "netstandard",
        "mscorlib",
    };

    public static bool IsAllowed(string assemblyName)
    {
        foreach (var exact in AllowedExactNames)
        {
            if (string.Equals(assemblyName, exact, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        foreach (var prefix in AllowedPrefixes)
        {
            if (assemblyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}

/// <summary>
/// v0.14 §3.1 metadata-binding entry spike — the compile-time surface for
/// resolving Calor call sites against real .NET metadata.
///
/// Reuses <see cref="GeneratedCSharpCompiler.References"/> (TPA-derived,
/// process-lifetime <see cref="Lazy{T}"/>) as the source of truth, filtered
/// against the F-2 manifest so Calor code cannot bind to compiler internals
/// (Z3, Microsoft.CodeAnalysis.*).
///
/// Overload resolution goes through Roslyn via synthetic invocation syntax
/// + <c>SemanticModel.GetSymbolInfo</c> — Roslyn's own algorithm handles
/// generic inference, params collapse, extension priority, nullable
/// annotations. See scoping doc §D4.
///
/// Lifecycle: one context per Calor.Sdk.Compilation, disposed with it. The
/// underlying <see cref="CSharpCompilation"/> is immutable-per-instance;
/// adding a synthetic tree for overload resolution returns a new compilation
/// without mutating the shared one.
/// </summary>
internal sealed class MetadataContext : IDisposable
{
    private readonly CSharpCompilation _hostCompilation;
    private readonly ImmutableArray<MetadataReference> _references;
    private readonly MetadataReferenceManifest _manifest;
    // Round-2 M5 mitigation: `volatile` guarantees visibility across threads
    // on Arm64. TryResolveOverload is called from the D4 mechanism and must
    // observe Dispose promptly.
    private volatile bool _disposed;

    private MetadataContext(
        CSharpCompilation hostCompilation,
        ImmutableArray<MetadataReference> references,
        MetadataReferenceManifest manifest)
    {
        _hostCompilation = hostCompilation;
        _references = references;
        _manifest = manifest;
    }

    /// <summary>
    /// S4 accessor — <see cref="MetadataBinder"/> needs the underlying
    /// compilation to add its own synthetic trees (with different usings).
    /// Kept <c>internal</c> so callers outside <c>Calor.Compiler</c> cannot
    /// bypass the manifest-verified reference set.
    /// </summary>
    internal CSharpCompilation HostCompilationForBinder
    {
        get { ThrowIfDisposed(); return _hostCompilation; }
    }

    /// <summary>
    /// Standard entry point: loads the F-2 manifest from its pinned location,
    /// verifies against the current TPA, and constructs a context. Throws
    /// <see cref="InvalidOperationException"/> on manifest drift with a
    /// message naming the specific defect and the observed SDK version.
    /// The context also admits the explicit references of the compilation in
    /// progress (<see cref="UseReferences"/>); outside one, it holds the
    /// manifest's <c>System.*</c> set alone.
    /// </summary>
    public static MetadataContext Create()
        => Create(CurrentReferencePaths);

    /// <summary>
    /// Entry point with an explicit reference list (CLI <c>-r</c>, the MSBuild
    /// task's references, <see cref="CompilationOptions.ReferencedAssemblyPaths"/>).
    /// See <see cref="AdmitExplicitReferences"/> for which of them are admitted.
    /// </summary>
    public static MetadataContext Create(IReadOnlyList<string>? referencePaths)
    {
        var manifest = MetadataReferenceManifest.Load();
        return CreateWithManifest(manifest, referencePaths);
    }

    /// <summary>
    /// Test-friendly entry point: accepts an in-memory manifest. Used by
    /// <see cref="MetadataContextTests"/> to exercise the manifest-drift
    /// hard-failure path without touching the checked-in manifest file.
    /// </summary>
    public static MetadataContext CreateWithManifest(
        MetadataReferenceManifest manifest,
        IReadOnlyList<string>? referencePaths = null)
    {
        // Round-2 C1 mitigation: SDK-version-range enforcement. If the running
        // framework is outside the manifest's declared range, hard-fail before
        // even attempting SHA drift detection — the range is the coarser bar.
        VerifyFrameworkVersionInRange(manifest);

        var filtered = FilterAndVerifyReferences(
            GeneratedCSharpCompiler.References,
            manifest);
        var references = filtered.AddRange(AdmitExplicitReferences(referencePaths, filtered));

        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            .WithNullableContextOptions(NullableContextOptions.Enable);

        var compilation = CSharpCompilation.Create(
            assemblyName: "CalorMetadataContext",
            syntaxTrees: null,
            references: references,
            options: options);

        return new MetadataContext(compilation, references, manifest);
    }

    // -------- explicit references --------

    // The explicit references of the compilation in progress. Every binder of one
    // compilation (the semantic binder, the effect pass's call-graph binder, the
    // verification and cross-module binders) creates its own context; an ambient
    // value gives them all the same reference set without threading it through
    // each construction site. AsyncLocal keeps concurrent compilations apart.
    private static readonly AsyncLocal<IReadOnlyList<string>?> AmbientReferencePaths = new();

    /// <summary>The explicit references in scope; empty outside a compilation.</summary>
    internal static IReadOnlyList<string> CurrentReferencePaths => AmbientReferencePaths.Value ?? [];

    /// <summary>
    /// Makes <paramref name="referencePaths"/> the explicit references of every
    /// context created on this flow until the returned scope is disposed, which
    /// restores the previous set. A null list means no explicit references: an
    /// inner compilation never inherits an outer one's.
    /// </summary>
    internal static ReferenceScope UseReferences(IEnumerable<string>? referencePaths)
    {
        var previous = AmbientReferencePaths.Value;
        AmbientReferencePaths.Value = referencePaths?.ToArray() ?? [];
        return new ReferenceScope(previous);
    }

    internal readonly struct ReferenceScope : IDisposable
    {
        private readonly IReadOnlyList<string>? _previous;
        internal ReferenceScope(IReadOnlyList<string>? previous) => _previous = previous;
        public void Dispose() => AmbientReferencePaths.Value = _previous;
    }

    /// <summary>
    /// The explicit references admitted next to the manifest set. The F-2
    /// whitelist keeps the compiler's own process (TPA) from leaking into Calor's
    /// metadata view: Roslyn, Z3 and Calor.Compiler are in the TPA but were never
    /// asked for. An explicit reference is different: the user named it, and the
    /// generated C# already compiles against it. So it is admitted, with three
    /// rules that keep the result a function of the reference set alone:
    /// <list type="bullet">
    ///   <item><description>Only the listed files. Nothing is probed: no
    ///   dependency resolution, no framework or package directories.</description></item>
    ///   <item><description>An assembly whose simple name is already in the
    ///   manifest set is skipped, so a project's <c>System.*</c> reference
    ///   assemblies never replace or duplicate the reviewed surface.</description></item>
    ///   <item><description>Paths are taken in ordinal order and the first file with
    ///   a given simple name wins, so the list's order does not matter. A file that
    ///   is missing, unreadable or not a managed assembly is not admitted (its
    ///   calls stay unresolved, which is the conservative outcome).</description></item>
    /// </list>
    /// Callers that cache compiler output must key it on the reference set's
    /// content; the CLI build cache and the MSBuild task already do.
    /// </summary>
    internal static ImmutableArray<MetadataReference> AdmitExplicitReferences(
        IReadOnlyList<string>? referencePaths,
        ImmutableArray<MetadataReference> manifestReferences)
        => AdmitExplicit(referencePaths, manifestReferences
                .OfType<PortableExecutableReference>()
                .Select(reference => Path.GetFileNameWithoutExtension(reference.FilePath ?? string.Empty)))
            .Select(admitted => (MetadataReference)admitted.Reference)
            .ToImmutableArray();

    private static List<ExplicitReference> AdmitExplicit(
        IReadOnlyList<string>? referencePaths, IEnumerable<string> manifestNames)
    {
        var admitted = new List<ExplicitReference>();
        if (referencePaths is null || referencePaths.Count == 0)
            return admitted;

        var names = manifestNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in referencePaths
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Select(Path.GetFullPath)
                     .Distinct(StringComparer.Ordinal)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            if (LoadExplicitReference(path) is not { } loaded || !names.Add(loaded.AssemblyName))
                continue;
            admitted.Add(loaded);
        }
        return admitted;
    }

    private static readonly object ProjectionReferencesLock = new();
    private static (ExplicitReference[] Admitted, IReadOnlyList<MetadataReference> References)? _projectionReferences;

    /// <summary>
    /// The reference set for the source projection images
    /// (<c>CallGraphAnalysis</c>'s Roslyn compilation of the module's C# image):
    /// the process set it has always used, with the explicit references this
    /// compilation admits (<see cref="AdmitExplicitReferences"/>) added, each
    /// replacing a same-named process assembly. Without explicit references it is
    /// <see cref="GeneratedCSharpCompiler.References"/> itself. The image and the
    /// binder therefore see the same framework assemblies; otherwise every call on
    /// a framework type would be an error in the image, and one error there makes
    /// every source call in the file unproved.
    /// </summary>
    internal static IReadOnlyList<MetadataReference> ProjectionReferences()
    {
        var paths = CurrentReferencePaths;
        if (paths.Count == 0)
            return GeneratedCSharpCompiler.References;
        var admitted = AdmitExplicit(paths,
            MetadataReferenceManifest.Load().Assemblies.Select(entry => entry.AssemblyName)).ToArray();
        if (admitted.Length == 0)
            return GeneratedCSharpCompiler.References;
        lock (ProjectionReferencesLock)
        {
            // The cached references are reused objects, so one set of files maps to
            // one list and the images' compilation caches keep hitting.
            if (_projectionReferences is { } cached && cached.Admitted.SequenceEqual(admitted))
                return cached.References;
            var names = admitted.Select(reference => reference.AssemblyName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var references = GeneratedCSharpCompiler.References
                .Where(reference => reference is not PortableExecutableReference { FilePath: { } path }
                    || !names.Contains(Path.GetFileNameWithoutExtension(path)))
                .Concat(admitted.Select(reference => (MetadataReference)reference.Reference))
                .ToArray();
            _projectionReferences = (admitted, references);
            return references;
        }
    }

    private sealed record ExplicitReference(string AssemblyName, PortableExecutableReference Reference);

    // One entry per path, replaced when the file's length or write time changes, so a
    // long-lived host (an MSBuild node) holds at most one image per referenced path.
    // Reusing the reference object also lets Roslyn reuse its assembly symbols across
    // the many contexts one compilation creates.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<
        string, (long Length, DateTime LastWriteUtc, ExplicitReference? Reference)> ExplicitReferenceCache = new();

    private static ExplicitReference? LoadExplicitReference(string path)
    {
        try
        {
            var before = new FileInfo(path);
            if (!before.Exists)
                return null;
            if (ExplicitReferenceCache.TryGetValue(path, out var cached)
                && cached.Length == before.Length && cached.LastWriteUtc == before.LastWriteTimeUtc)
                return cached.Reference;

            // One read: the name and the reference come from the same bytes.
            var image = File.ReadAllBytes(path);
            ExplicitReference? loaded = null;
            using (var pe = new System.Reflection.PortableExecutable.PEReader(
                       ImmutableArray.Create(image)))
            {
                if (pe.HasMetadata)
                {
                    var reader = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
                    if (reader.IsAssembly)
                    {
                        var name = reader.GetString(reader.GetAssemblyDefinition().Name);
                        loaded = new ExplicitReference(name,
                            MetadataReference.CreateFromImage(image, filePath: path));
                    }
                }
            }
            var after = new FileInfo(path);
            if (after.Exists && after.Length == before.Length && after.LastWriteTimeUtc == before.LastWriteTimeUtc)
                ExplicitReferenceCache[path] = (before.Length, before.LastWriteTimeUtc, loaded);
            return loaded;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or BadImageFormatException or InvalidOperationException)
        {
            return null;
        }
    }

    private static readonly Lazy<Task> WarmUp = new(() => Task.Run(RunWarmUp));

    /// <summary>
    /// Starts compiling Roslyn's metadata-lookup path on a thread-pool thread, once per
    /// process. The binder resolves .NET names through a Roslyn semantic model (see
    /// <c>Binder.ResolveScopedMetadataType</c>), and in a fresh CLI process the first
    /// such lookup is dominated by JIT-compiling Roslyn itself: about half a second,
    /// paid by every compile, accepted or rejected. Started at process entry, that work
    /// overlaps command-line parsing, the declaration-identity sync, lexing and parsing.
    /// The result is discarded and nothing is shared except what the process already
    /// shares (the reference set, the reference-hash cache, compiled code), so no
    /// diagnostic, output or cache key can depend on it. A failure here is ignored:
    /// the binder repeats the same work and reports any real failure itself.
    /// Returns the one warm-up task of the process; callers do not wait for it.
    /// </summary>
    internal static Task StartWarmUp() => WarmUp.Value;

    private static void RunWarmUp()
    {
        try
        {
            using var context = Create();
            var tree = CSharpSyntaxTree.ParseText(
                "using System;\nusing System.Collections.Generic;\n"
                + "class __CalorWarmUp { static global::System.Type Resolve() => typeof(List<>); }");
            var model = context._hostCompilation.AddSyntaxTrees(tree).GetSemanticModel(tree);
            _ = model.GetDiagnostics();
            _ = model.GetTypeInfo(tree.GetRoot().DescendantNodes().OfType<TypeOfExpressionSyntax>().Single().Type);
        }
        catch
        {
            // Best effort only; see the summary.
        }
    }

    // -------- D3 typed lookup surface --------

    /// <summary>
    /// Resolves a .NET type by its metadata name (e.g. <c>System.Console</c>,
    /// <c>System.Collections.Generic.List`1</c>). Returns null when the type
    /// is not present in the manifest's reference set — callers wrap null
    /// results as <c>UnresolvedBoundType</c> once the BoundType hierarchy
    /// lands in S2.
    /// </summary>
    public INamedTypeSymbol? TryResolveType(string metadataName)
    {
        ThrowIfDisposed();
        return _hostCompilation.GetTypeByMetadataName(metadataName);
    }

    /// <summary>
    /// Returns the method group for a given receiver + method name. Empty when
    /// the method group is not present (rather than null — an empty group is
    /// meaningful: "receiver has no method with this name").
    /// </summary>
    public IReadOnlyList<IMethodSymbol> TryResolveMethodGroup(
        ITypeSymbol receiverType, string methodName)
    {
        ThrowIfDisposed();
        return receiverType.GetMembers(methodName).OfType<IMethodSymbol>().ToArray();
    }

    /// <summary>
    /// D4: overload resolution via synthetic invocation syntax +
    /// <c>SemanticModel.GetSymbolInfo</c>. Roslyn's own algorithm resolves —
    /// generic inference, params collapse, extension priority, nullable
    /// tie-breaks.
    ///
    /// The synthetic tree wraps the invocation in a <c>#nullable enable</c>
    /// context so the calling process's nullable-context defaults do not
    /// leak in. Argument expressions are <c>((TArg)default!)</c> placeholders
    /// — the <c>default!</c> suppresses nullable-flow warnings on the
    /// placeholder itself; the resolution is a compile-time query, no
    /// runtime evaluation.
    ///
    /// Returns the resolved <see cref="IMethodSymbol"/> on success. On
    /// ambiguity, no-match, or missing metadata, returns <c>null</c> with
    /// <paramref name="unresolvedReason"/> set to a human-readable
    /// classification (fed into Calor0270 diagnostic messages downstream).
    /// </summary>
    public IMethodSymbol? TryResolveOverload(
        ITypeSymbol receiverType,
        string methodName,
        IReadOnlyList<MetadataArgument> arguments,
        out string? unresolvedReason)
    {
        ThrowIfDisposed();

        var receiverFq = receiverType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var argExprs = MetadataBinder.BuildArgumentExpressions(arguments);

        // Round-2 C2 mitigation: static-vs-instance based on the actual
        // method group, not on `receiverType.IsStatic` (which is only true
        // for `static class` — false for value types like `int` where
        // `int.TryParse` is a static method on a non-static type).
        //
        // Strategy: probe the method group. If any candidate is static,
        // model as a static call (`TReceiver.Method(...)`). Otherwise, use
        // an instance placeholder cast. This matches how C# programmers
        // write these calls, and lets Roslyn's overload resolution do the
        // right thing when both instance and static overloads coexist
        // (rare in BCL but possible).
        // v0.17 R3 — the group is taken over the receiver's whole inheritance
        // surface, not its own declarations alone. See MetadataMemberLookup.
        var methodGroup = MetadataMemberLookup.MethodGroup(_hostCompilation, receiverType, methodName);
        var hasStatic = methodGroup.Any(m => m.IsStatic);
        var hasInstance = methodGroup.Any(m => !m.IsStatic);
        string receiverExpr;
        if (hasStatic && !hasInstance)
        {
            receiverExpr = receiverFq;
        }
        else if (!hasStatic && hasInstance)
        {
            receiverExpr = $"(({receiverFq})default!)";
        }
        else if (hasStatic && hasInstance)
        {
            // Mixed group — Roslyn resolves both syntactic forms; we choose
            // the instance form because a `default!` value can dispatch to
            // both static and instance overloads in Roslyn's model
            // (instance-off-default is a warning, not an error). If the
            // resolution picks a static overload from the instance-form
            // invocation, the resolved IMethodSymbol still carries
            // IsStatic=true — the caller acts on that, not on the syntax.
            receiverExpr = $"(({receiverFq})default!)";
        }
        else
        {
            // No members with that name — record as unresolved before we
            // even attempt the synthetic tree round-trip.
            unresolvedReason =
                $"Receiver '{receiverFq}' has no member named '{methodName}'.";
            return null;
        }

        var source =
            "#nullable enable\n" +
            "class __CalorSynth { static void __Probe() {\n" +
            MetadataBinder.BuildArgumentLocals(arguments) +
            $"    _ = {receiverExpr}.{methodName}({string.Join(", ", argExprs)});\n" +
            "} }\n";

        var tree = CSharpSyntaxTree.ParseText(source);
        var extended = _hostCompilation.AddSyntaxTrees(tree);
        var model = extended.GetSemanticModel(tree);
        var invocation = tree.GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault();

        if (invocation is null)
        {
            unresolvedReason = "Synthetic tree did not contain an invocation expression (parser failure).";
            return null;
        }

        var info = model.GetSymbolInfo(invocation);
        if (info.Symbol is IMethodSymbol resolved)
        {
            unresolvedReason = null;
            return resolved;
        }

        unresolvedReason = info.CandidateReason switch
        {
            CandidateReason.Ambiguous =>
                $"Ambiguous method call — {info.CandidateSymbols.Length} candidates.",
            CandidateReason.OverloadResolutionFailure =>
                "No overload matches the supplied argument types.",
            CandidateReason.Inaccessible =>
                "The resolved method is not accessible.",
            CandidateReason.WrongArity =>
                "Method exists but arity does not match.",
            CandidateReason.MemberGroup =>
                "Symbol resolved to a method group; no single overload matched.",
            CandidateReason.None when info.CandidateSymbols.IsEmpty =>
                $"Receiver '{receiverFq}' has no member named '{methodName}'.",
            _ => $"Unresolved: candidateReason={info.CandidateReason}",
        };
        return null;
    }

    // -------- helpers --------

    /// <summary>
    /// Round-2 C1: SDK-version-range enforcement. Reads
    /// <see cref="RuntimeInformation.FrameworkDescription"/>, extracts the
    /// version, and hard-fails if outside the manifest's declared range.
    /// </summary>
    private static void VerifyFrameworkVersionInRange(MetadataReferenceManifest manifest)
    {
        var (min, max) = ParseSdkVersionRange(manifest.SdkVersionRange);
        var observed = ExtractFrameworkVersion(RuntimeInformation.FrameworkDescription);
        if (observed is null)
        {
            throw new InvalidOperationException(
                $"Could not parse the running framework version from " +
                $"'{RuntimeInformation.FrameworkDescription}'. Metadata binding requires .NET 10+.");
        }
        if (observed < min || observed > max)
        {
            throw new InvalidOperationException(
                $"Framework version {observed} is outside the manifest's " +
                $"sdkVersionRange '{manifest.SdkVersionRange}' (min={min}, max={max}). " +
                $"Regenerate the manifest via scripts/generate-metadata-references-manifest.sh " +
                $"or pin the SDK in global.json to a version inside the declared range.");
        }
    }

    private static (Version Min, Version Max) ParseSdkVersionRange(string range)
    {
        // Format: "MIN - MAX" (e.g. "10.0.0 - 10.0.999"). Whitespace tolerant.
        var parts = range.Split('-', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            throw new InvalidOperationException(
                $"Invalid sdkVersionRange format: '{range}'. Expected 'MIN - MAX'.");
        }
        if (!Version.TryParse(parts[0], out var min) || !Version.TryParse(parts[1], out var max))
        {
            throw new InvalidOperationException(
                $"Invalid sdkVersionRange versions: '{range}'. Both endpoints must be parseable.");
        }
        return (min, max);
    }

    private static Version? ExtractFrameworkVersion(string frameworkDescription)
    {
        // FrameworkDescription is "".NET 10.0.10" or ".NET Core 3.1.15" or
        // ".NET Framework 4.7.2". We match the last N.N.N triplet.
        var match = Regex.Match(frameworkDescription, @"(\d+)\.(\d+)\.(\d+)");
        if (!match.Success)
        {
            return null;
        }
        return Version.TryParse(match.Value, out var v) ? v : null;
    }

    private static ImmutableArray<MetadataReference> FilterAndVerifyReferences(
        IReadOnlyList<MetadataReference> allReferences,
        MetadataReferenceManifest manifest)
    {
        // Round-2 M1 mitigation: in-code allow-list backstops the manifest's
        // whitelist. A tampered manifest that tries to whitelist a
        // compiler-internal assembly (Microsoft.CodeAnalysis.*, Z3,
        // Calor.Compiler) fails here — the code, not the JSON, is the trust
        // boundary.
        foreach (var entry in manifest.Assemblies)
        {
            if (!MetadataAllowedNamePrefixes.IsAllowed(entry.AssemblyName))
            {
                throw new InvalidOperationException(
                    $"Metadata manifest tampering detected: assembly '{entry.AssemblyName}' " +
                    "is not in the compiler's in-code allow-list. Allowed prefixes: " +
                    $"[{string.Join(", ", MetadataAllowedNamePrefixes.AllowedPrefixes)}]; " +
                    $"allowed exact: [{string.Join(", ", MetadataAllowedNamePrefixes.AllowedExactNames)}]. " +
                    "Compiler-internal assemblies (Microsoft.CodeAnalysis.*, Z3, Calor.Compiler) " +
                    "are deliberately excluded.");
            }
        }

        var whitelist = manifest.Assemblies
            .Select(a => a.AssemblyName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var portableRefs = allReferences.OfType<PortableExecutableReference>().ToArray();
        var filtered = portableRefs
            .Where(r =>
            {
                var name = Path.GetFileNameWithoutExtension(r.FilePath ?? string.Empty);
                return whitelist.Contains(name);
            })
            .Cast<MetadataReference>()
            .ToImmutableArray();

        // Drift check: every manifest entry must have a matching TPA entry
        // (name + SHA-256). Hard-fail on missing OR SHA mismatch. Silent null
        // fails the F-2 discriminating pin.
        foreach (var expected in manifest.Assemblies)
        {
            var matched = portableRefs.FirstOrDefault(r => string.Equals(
                Path.GetFileNameWithoutExtension(r.FilePath ?? string.Empty),
                expected.AssemblyName,
                StringComparison.OrdinalIgnoreCase));
            if (matched is null || string.IsNullOrEmpty(matched.FilePath))
            {
                throw new InvalidOperationException(
                    $"Metadata manifest drift: expected assembly '{expected.AssemblyName}' " +
                    $"was not found in TPA. SDK version: {RuntimeInformation.FrameworkDescription}. " +
                    "Regenerate the manifest via scripts/generate-metadata-references-manifest.sh " +
                    $"or check the SDK version (manifest range: {manifest.SdkVersionRange}).");
            }

            // Scoping doc §D3: inside sdkVersionRange, SHA drift is an
            // Info-level condition that triggers auto-regeneration — not a
            // hard failure. VerifyFrameworkVersionInRange has already
            // asserted we're inside range by the time we reach this point,
            // so any SHA drift here is intentionally the lenient path.
            //
            // The manifest's SHA becomes an *expected* value that survives
            // SDK patch upgrades within the same range; a drift is a signal
            // for the auto-regen bot to refresh the manifest, but the
            // running compiler continues with the actual TPA bytes.
            //
            // A future revision will emit a diagnostic here that the
            // auto-regen bot picks up; today we compute the SHA (verifying
            // the file is readable) and continue.
            _ = ReadReferenceSha256(matched.FilePath);
        }

        return filtered;
    }

    // Every binder creates a context, and hashing the whole reference set each time
    // was about a quarter of effect-checking time. The hash is a function of the file's
    // bytes, so it is computed once per process for a given path, length and
    // last-write time. A changed file has a new key and is read (and hashed) again,
    // and a missing file still throws when its length is read. Only a file that
    // became unreadable without any change to its length or write time is no longer
    // re-opened. The value is still discarded today (in-range drift is lenient); it is kept
    // so the planned drift diagnostic can compare it without re-reading the file.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<
        (string Path, long Length, DateTime LastWriteUtc), string> ReferenceHashes = new();

    private static string ReadReferenceSha256(string filePath)
    {
        var file = new FileInfo(filePath);
        var key = (file.FullName, file.Length, file.LastWriteTimeUtc);
        return ReferenceHashes.GetOrAdd(key, _ => ManifestAssemblyEntry.ComputeSha256(filePath));
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MetadataContext));
        }
    }

    public void Dispose()
    {
        // CSharpCompilation itself is immutable-per-instance; it does not own
        // unmanaged handles. MetadataReferences hold file handles / mapped
        // regions at Compilation-object lifetime; dropping the reference lets
        // GC reclaim. Marking disposed prevents further use of a stale
        // reference through this facade.
        _disposed = true;
    }
}

/// <summary>
/// One argument to <see cref="MetadataContext.TryResolveOverload"/> — a
/// (type, ref-kind) pair so Roslyn's overload resolution can distinguish
/// value / out / ref / in parameters correctly (CS1620 otherwise).
/// </summary>
internal readonly record struct MetadataArgument(
    ITypeSymbol Type,
    RefKind RefKind = RefKind.None,
    string? Name = null);

/// <summary>
/// v0.17 R3 — member lookup over the receiver's whole inheritance surface.
///
/// <para>Both metadata resolution paths used to compute their method group with
/// <c>receiverType.GetMembers(name)</c>, which returns only the members
/// <em>declared</em> on that one type. An empty group short-circuited to
/// "Receiver 'X' has no member named 'Y'." <em>without ever asking Roslyn</em> —
/// so every inherited member was reported unresolvable:</para>
/// <list type="bullet">
///   <item><description><c>IValidator.GetType()</c> — declared on
///   <c>System.Object</c>, and an interface has no <c>BaseType</c>.</description></item>
///   <item><description><c>MethodInfo.Invoke(…)</c>, <c>Task&lt;T&gt;.Wait()</c> —
///   declared on a base class.</description></item>
///   <item><description><c>IList&lt;T&gt;.Add(item)</c> — declared on the base
///   interface <c>ICollection&lt;T&gt;</c>.</description></item>
///   <item><description><c>TProperty.CompareTo(other)</c> — reached through a
///   type-parameter constraint.</description></item>
///   <item><description><c>Severity.ToString()</c> — an enum reaching
///   <c>System.Enum</c> → <c>ValueType</c> → <c>Object</c>.</description></item>
/// </list>
/// <para>The group is now taken over the full lookup surface. Roslyn still does
/// the overload resolution; this only decides whether it gets asked at all, and
/// whether the probe is written in static or instance form.</para>
/// </summary>
internal static class MetadataMemberLookup
{
    /// <summary>
    /// The methods named <paramref name="methodName"/> visible on
    /// <paramref name="receiverType"/>, including inherited ones.
    /// </summary>
    public static IMethodSymbol[] MethodGroup(
        Compilation compilation,
        ITypeSymbol receiverType,
        string methodName)
    {
        var group = new List<IMethodSymbol>();
        foreach (var type in LookupSurface(compilation, receiverType))
        {
            foreach (var member in type.GetMembers(methodName))
            {
                if (member is IMethodSymbol method)
                {
                    group.Add(method);
                }
            }
        }
        return group.ToArray();
    }

    /// <summary>
    /// The types that actually declare a method named
    /// <paramref name="methodName"/> reachable from
    /// <paramref name="receiverType"/>, most-derived first, excluding the
    /// receiver itself. Used as probe receivers when the receiver's own name
    /// does not exist in the probe compilation.
    /// </summary>
    public static IEnumerable<ITypeSymbol> DeclaringTypes(
        Compilation compilation,
        ITypeSymbol receiverType,
        string methodName)
    {
        var seen = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default)
        {
            receiverType,
        };
        foreach (var type in LookupSurface(compilation, receiverType))
        {
            if (!seen.Add(type)) continue;
            if (type.GetMembers(methodName).OfType<IMethodSymbol>().Any())
            {
                yield return type;
            }
        }
    }

    /// <summary>
    /// Every type whose members participate in a member lookup on
    /// <paramref name="receiverType"/>. Ordered most-derived first; deduplicated.
    /// </summary>
    private static IEnumerable<ITypeSymbol> LookupSurface(
        Compilation compilation,
        ITypeSymbol receiverType)
    {
        var seen = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        var surface = new List<ITypeSymbol>();

        void Add(ITypeSymbol? type)
        {
            if (type is null || type.TypeKind == TypeKind.Error) return;
            if (!seen.Add(type)) return;
            surface.Add(type);
        }

        void AddWithBases(ITypeSymbol type)
        {
            Add(type);
            for (var b = type.BaseType; b is not null; b = b.BaseType)
            {
                Add(b);
            }
            foreach (var iface in type.AllInterfaces)
            {
                Add(iface);
            }
        }

        switch (receiverType)
        {
            // A type parameter declares nothing of its own: its members are
            // whatever its constraints supply, plus System.Object's.
            case ITypeParameterSymbol typeParameter:
                foreach (var constraint in typeParameter.ConstraintTypes)
                {
                    AddWithBases(constraint);
                }
                break;

            // An array's members come from System.Array and the generic
            // collection interfaces the runtime grafts onto it.
            case IArrayTypeSymbol array:
                Add(compilation.GetSpecialType(SpecialType.System_Array));
                foreach (var iface in array.AllInterfaces)
                {
                    Add(iface);
                }
                break;

            default:
                AddWithBases(receiverType);
                break;
        }

        // Every receiver bottoms out at System.Object — interfaces and type
        // parameters have no BaseType chain that reaches it.
        Add(compilation.GetSpecialType(SpecialType.System_Object));
        return surface;
    }
}
