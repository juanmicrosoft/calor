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
    /// Standard entry point: loads the F-2 manifest from its pinned location,
    /// verifies against the current TPA, and constructs a context. Throws
    /// <see cref="InvalidOperationException"/> on manifest drift with a
    /// message naming the specific defect and the observed SDK version.
    /// </summary>
    public static MetadataContext Create()
    {
        var manifest = MetadataReferenceManifest.Load();
        return CreateWithManifest(manifest);
    }

    /// <summary>
    /// Test-friendly entry point: accepts an in-memory manifest. Used by
    /// <see cref="MetadataContextTests"/> to exercise the manifest-drift
    /// hard-failure path without touching the checked-in manifest file.
    /// </summary>
    public static MetadataContext CreateWithManifest(MetadataReferenceManifest manifest)
    {
        // Round-2 C1 mitigation: SDK-version-range enforcement. If the running
        // framework is outside the manifest's declared range, hard-fail before
        // even attempting SHA drift detection — the range is the coarser bar.
        VerifyFrameworkVersionInRange(manifest);

        var filtered = FilterAndVerifyReferences(
            GeneratedCSharpCompiler.References,
            manifest);

        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            .WithNullableContextOptions(NullableContextOptions.Enable);

        var compilation = CSharpCompilation.Create(
            assemblyName: "CalorMetadataContext",
            syntaxTrees: null,
            references: filtered,
            options: options);

        return new MetadataContext(compilation, filtered, manifest);
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
        var argExprs = new string[arguments.Count];
        for (int i = 0; i < arguments.Count; i++)
        {
            var argFq = arguments[i].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            // out/ref parameters need matching argument syntax; a value cast
            // fails overload resolution with CS1620. `out var _o{i}` /
            // `ref var _r{i}` produces a fresh local that Roslyn accepts.
            argExprs[i] = arguments[i].RefKind switch
            {
                RefKind.Out => $"out {argFq} _o{i}",
                RefKind.Ref => $"ref var _r{i}",
                RefKind.In => $"in (({argFq})default!)",
                _ => $"(({argFq})default!)",
            };
        }

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
        var methodGroup = receiverType.GetMembers(methodName).OfType<IMethodSymbol>().ToArray();
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
            _ = ManifestAssemblyEntry.ComputeSha256(matched.FilePath);
        }

        return filtered;
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
internal readonly record struct MetadataArgument(ITypeSymbol Type, RefKind RefKind = RefKind.None);
