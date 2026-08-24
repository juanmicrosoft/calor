using System.Collections.Immutable;
using Calor.Compiler.Binding.BoundTypes;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Calor.Compiler.Binding.Metadata;

/// <summary>
/// v0.14 §S4 — the spike proper. Consumes <see cref="MetadataContext"/> to
/// resolve a Calor call site against .NET metadata and produce either a
/// resolved <see cref="IMethodSymbol"/> wrapped in a
/// <see cref="NominalBoundType"/>, or an <see cref="UnresolvedBoundType"/>
/// with a Calor0270 reason. Runs against F-1's 15 shape fixtures via the
/// measurement test at
/// <c>tests/Calor.Compiler.Tests/Binding/Metadata/MetadataBinderShapeMeasurementTests.cs</c>
/// — the S4 exit-criterion "Metadata-binding shape resolution: X / N" number.
///
/// <para>Improvements over <see cref="MetadataContext.TryResolveOverload"/>:
/// <list type="bullet">
///   <item>Synthetic tree carries <c>using System.Linq;</c> so
///     extension-on-interface resolves (S3 shapes 3/4/9/15).</item>
///   <item>Mixed static+instance overload groups where arity only matches
///     the static side switch syntactic form (S3 shape 13).</item>
///   <item>Dedicated paths for constructor / indexer / property resolution
///     (S3 shapes 1/10/11) — the shapes that <c>MetadataContext</c>'s method
///     surface cannot reach.</item>
/// </list>
/// </para>
///
/// <para>Output produced by <see cref="ResolveCall"/> is either
/// <see cref="MetadataBinderResult.Resolved"/> (carrying the symbol and its
/// return type wrapped as <see cref="NominalBoundType"/>) or
/// <see cref="MetadataBinderResult.Unresolved"/> (carrying the reason for
/// Calor0270). Callers do not need to inspect Roslyn types directly.</para>
/// </summary>
internal sealed class MetadataBinder
{
    private readonly MetadataContext _context;

    public MetadataBinder(MetadataContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// Resolves a Calor call site. Returns a resolved
    /// <see cref="MetadataBinderResult"/> on success or an unresolved one
    /// carrying the Calor0270 reason string.
    /// </summary>
    public MetadataBinderResult ResolveCall(
        ITypeSymbol receiverType,
        string methodName,
        IReadOnlyList<MetadataArgument> arguments)
    {
        if (receiverType is null) throw new ArgumentNullException(nameof(receiverType));
        if (methodName is null) throw new ArgumentNullException(nameof(methodName));

        // Primary path: TryResolveOverload from the S1 mechanism, extended
        // with System.Linq usings and a smarter static-vs-instance heuristic.
        var resolved = TryResolveWithLinq(receiverType, methodName, arguments, out var reason);
        if (resolved is not null)
        {
            return MetadataBinderResult.CreateResolved(resolved);
        }

        // Fallback: static-only retry when the primary path chose instance
        // syntax on a mixed group but no instance overload matched the arity.
        var staticFallback = TryResolveStaticOnly(receiverType, methodName, arguments);
        if (staticFallback is not null)
        {
            return MetadataBinderResult.CreateResolved(staticFallback);
        }

        return MetadataBinderResult.CreateUnresolved(reason ?? "Method resolution failed for an unknown reason.");
    }

    /// <summary>
    /// Constructor resolution — S3 shape 10. Roslyn does not expose
    /// constructors via the method-name synthetic-tree path
    /// (<c>ctor</c> is not a valid method identifier). Query directly via
    /// <see cref="INamedTypeSymbol.InstanceConstructors"/> and pick by
    /// arity + parameter-type match.
    /// </summary>
    public MetadataBinderResult ResolveConstructor(
        INamedTypeSymbol typeToConstruct,
        IReadOnlyList<MetadataArgument> arguments)
    {
        if (typeToConstruct is null) throw new ArgumentNullException(nameof(typeToConstruct));

        var candidates = typeToConstruct.InstanceConstructors;
        if (candidates.IsEmpty)
        {
            return MetadataBinderResult.CreateUnresolved(
                $"Type '{typeToConstruct.ToDisplayString()}' has no accessible constructors.");
        }

        var matched = candidates
            .Where(c => c.Parameters.Length == arguments.Count)
            .Where(c => TypesMatch(c.Parameters, arguments))
            .ToArray();

        if (matched.Length == 1)
        {
            return MetadataBinderResult.CreateResolved(matched[0]);
        }
        if (matched.Length > 1)
        {
            return MetadataBinderResult.CreateUnresolved(
                $"Ambiguous constructor for '{typeToConstruct.ToDisplayString()}' — {matched.Length} candidates.");
        }
        return MetadataBinderResult.CreateUnresolved(
            $"No constructor for '{typeToConstruct.ToDisplayString()}' matches the supplied argument types.");
    }

    /// <summary>
    /// Indexer resolution — S3 shape 11. Indexers surface as
    /// <see cref="IPropertySymbol"/>s under the synthesized name
    /// <c>this[]</c>. Return the property symbol so downstream can bind to
    /// the getter/setter as needed.
    /// </summary>
    public IPropertySymbol? ResolveIndexer(
        ITypeSymbol receiverType,
        IReadOnlyList<MetadataArgument> arguments)
    {
        if (receiverType is null) throw new ArgumentNullException(nameof(receiverType));
        return receiverType.GetMembers("this[]")
            .OfType<IPropertySymbol>()
            .FirstOrDefault(p =>
                p.Parameters.Length == arguments.Count
                && TypesMatch(p.Parameters, arguments));
    }

    /// <summary>
    /// Property resolution — S3 shape 1. Returns the property symbol if
    /// a same-named property exists on the receiver (no method-group
    /// disambiguation applies).
    /// </summary>
    public IPropertySymbol? ResolveProperty(ITypeSymbol receiverType, string propertyName)
    {
        if (receiverType is null) throw new ArgumentNullException(nameof(receiverType));
        return receiverType.GetMembers(propertyName)
            .OfType<IPropertySymbol>()
            .FirstOrDefault();
    }

    // -------- helpers --------

    /// <summary>
    /// D4 mechanism with the S4 improvement: <c>using System.Linq;</c> is
    /// added so extension-on-interface resolves. Behavior otherwise mirrors
    /// <see cref="MetadataContext.TryResolveOverload"/>.
    /// </summary>
    private IMethodSymbol? TryResolveWithLinq(
        ITypeSymbol receiverType,
        string methodName,
        IReadOnlyList<MetadataArgument> arguments,
        out string? unresolvedReason)
    {
        var receiverFq = receiverType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var argExprs = BuildArgumentExpressions(arguments);

        // Static-vs-instance heuristic identical to MetadataContext's, but
        // exposed here because we need to know which branch was taken for
        // the static-fallback path.
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
            receiverExpr = $"(({receiverFq})default!)";
        }
        else
        {
            unresolvedReason = $"Receiver '{receiverFq}' has no member named '{methodName}'.";
            return null;
        }

        var source = BuildSyntheticSource(receiverExpr, methodName, argExprs);
        return QueryInvocation(source, out unresolvedReason);
    }

    /// <summary>
    /// Static-only fallback: forces static syntax
    /// (<c>Receiver.Method(...)</c>) regardless of the method group. Used
    /// when the mixed-group path fails — a call whose arity matches only
    /// static overloads should still resolve.
    /// </summary>
    private IMethodSymbol? TryResolveStaticOnly(
        ITypeSymbol receiverType,
        string methodName,
        IReadOnlyList<MetadataArgument> arguments)
    {
        var receiverFq = receiverType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var argExprs = BuildArgumentExpressions(arguments);
        var source = BuildSyntheticSource(receiverFq, methodName, argExprs);
        return QueryInvocation(source, out _);
    }

    private static string[] BuildArgumentExpressions(IReadOnlyList<MetadataArgument> arguments)
    {
        var exprs = new string[arguments.Count];
        for (int i = 0; i < arguments.Count; i++)
        {
            var argFq = arguments[i].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            exprs[i] = arguments[i].RefKind switch
            {
                RefKind.Out => $"out {argFq} _o{i}",
                RefKind.Ref => $"ref var _r{i}",
                RefKind.In => $"in (({argFq})default!)",
                _ => $"(({argFq})default!)",
            };
        }
        return exprs;
    }

    private static string BuildSyntheticSource(string receiverExpr, string methodName, string[] argExprs) =>
        "#nullable enable\n" +
        "using System.Linq;\n" +
        "using System.Collections.Generic;\n" +
        "class __CalorS4Synth { static void __Probe() {\n" +
        $"    _ = {receiverExpr}.{methodName}({string.Join(", ", argExprs)});\n" +
        "} }\n";

    private IMethodSymbol? QueryInvocation(string source, out string? unresolvedReason)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var extended = _context.HostCompilationForBinder.AddSyntaxTrees(tree);
        var model = extended.GetSemanticModel(tree);
        var invocation = tree.GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault();

        if (invocation is null)
        {
            unresolvedReason = "Synthetic tree did not contain an invocation expression.";
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
                "No method group.",
            _ => $"Unresolved: candidateReason={info.CandidateReason}",
        };
        return null;
    }

    private static bool TypesMatch(
        ImmutableArray<IParameterSymbol> parameters,
        IReadOnlyList<MetadataArgument> arguments)
    {
        for (int i = 0; i < parameters.Length; i++)
        {
            if (!SymbolEqualityComparer.Default.Equals(parameters[i].Type, arguments[i].Type))
            {
                return false;
            }
        }
        return true;
    }
}

/// <summary>Result of a call to <see cref="MetadataBinder.ResolveCall"/> or
/// <see cref="MetadataBinder.ResolveConstructor"/>.</summary>
internal readonly struct MetadataBinderResult
{
    public IMethodSymbol? Symbol { get; }
    public string? UnresolvedReason { get; }
    public bool IsResolved => Symbol is not null;

    private MetadataBinderResult(IMethodSymbol? symbol, string? unresolvedReason)
    {
        Symbol = symbol;
        UnresolvedReason = unresolvedReason;
    }

    public static MetadataBinderResult CreateResolved(IMethodSymbol symbol) => new(symbol, null);

    public static MetadataBinderResult CreateUnresolved(string reason) => new(null, reason);

    /// <summary>
    /// F-4: emit the diagnostic(s) this result implies at the given call-site
    /// span. Unresolved → Calor0270 (SignatureUnresolved, Info severity in S4).
    /// Resolved with Oblivious annotation → Calor0271 (Info severity always).
    /// Callers control severity per §3.5's promotion path (Info → Warning →
    /// Error over the release cycle).
    /// </summary>
    public IEnumerable<Calor.Compiler.Diagnostics.Diagnostic> ToDiagnostics(TextSpan span, Calor.Compiler.Diagnostics.DiagnosticSeverity severity = Calor.Compiler.Diagnostics.DiagnosticSeverity.Info)
    {
        if (!IsResolved)
        {
            yield return new Calor.Compiler.Diagnostics.Diagnostic(
                DiagnosticCode.SignatureUnresolved,
                $"Metadata resolution failed: {UnresolvedReason}",
                span,
                severity);
            yield break;
        }

        // Resolved: check for Oblivious surface (return type or any parameter).
        var oblivious = Symbol!.ReturnType.NullableAnnotation == Microsoft.CodeAnalysis.NullableAnnotation.None ||
                        Symbol.Parameters.Any(p => p.Type.NullableAnnotation == Microsoft.CodeAnalysis.NullableAnnotation.None);
        if (oblivious)
        {
            yield return new Calor.Compiler.Diagnostics.Diagnostic(
                DiagnosticCode.SignatureResolvedNullableOblivious,
                $"Resolved '{Symbol.ToDisplayString()}' but its nullable annotation is Oblivious; " +
                "callers needing a non-null claim must adapt at the interop boundary.",
                span,
                Calor.Compiler.Diagnostics.DiagnosticSeverity.Info);
        }
    }

    /// <summary>
    /// v0.14 §S2 (nullability workstream, task #9) — build a NominalBoundType
    /// for the resolved return type, carrying the Roslyn NullableAnnotation
    /// through to the bound-tree side. Callers that hold a resolved result
    /// consume this instead of re-inspecting the raw Roslyn symbol.
    ///
    /// Returns null when the result is not resolved. Array-typed returns
    /// (task #7 §S6) are surfaced through <see cref="GetReturnBoundTypeEx"/>
    /// for callers that need the element-annotation-aware
    /// <see cref="ArrayBoundType"/> shape; this method keeps the flat
    /// NominalBoundType shim in place for backward compatibility (drops an
    /// IArrayTypeSymbol return into <c>NominalBoundType("string[]")</c>).
    /// </summary>
    public NominalBoundType? GetReturnBoundType()
    {
        if (!IsResolved) return null;
        return ToNominalBoundType(Symbol!.ReturnType);
    }

    /// <summary>
    /// v0.14 §S6 (task #7 Phase-C) — element-annotation-preserving
    /// counterpart to <see cref="GetReturnBoundType"/>. When the Roslyn
    /// return type is an array symbol, this returns an
    /// <see cref="ArrayBoundType"/> wrapping the element's own
    /// NullableAnnotation (so an <c>IEnumerable&lt;string?&gt;.ToArray()</c>
    /// or an <c>string?[]</c> return surface as
    /// <c>ArrayBoundType(NominalBoundType(STRING, Annotated))</c>).
    /// Non-array types fall through to the same NominalBoundType shape as
    /// <see cref="GetReturnBoundType"/>. Returns null when unresolved.
    /// </summary>
    public BoundType? GetReturnBoundTypeEx()
    {
        if (!IsResolved) return null;
        return ToBoundType(Symbol!.ReturnType);
    }

    /// <summary>
    /// Parameter BoundTypes for the resolved call, in declaration order,
    /// each carrying its own NullableAnnotation. Empty when not resolved.
    /// </summary>
    public IReadOnlyList<NominalBoundType> GetParameterBoundTypes()
    {
        if (!IsResolved) return Array.Empty<NominalBoundType>();
        return Symbol!.Parameters
            .Select(p => ToNominalBoundType(p.Type))
            .ToList();
    }

    /// <summary>
    /// Maps a Roslyn ITypeSymbol to a NominalBoundType, translating the
    /// Roslyn NullableAnnotation to the BoundTypes NullableAnnotation enum.
    /// The Roslyn back-reference is preserved on the resulting BoundType
    /// when the type symbol is an INamedTypeSymbol.
    /// </summary>
    internal static NominalBoundType ToNominalBoundType(Microsoft.CodeAnalysis.ITypeSymbol typeSymbol)
    {
        return new NominalBoundType(
            qualifiedName: typeSymbol.ToDisplayString(),
            nullableAnnotation: MapAnnotation(typeSymbol.NullableAnnotation),
            declaration: null,
            roslynSymbol: typeSymbol as Microsoft.CodeAnalysis.INamedTypeSymbol);
    }

    /// <summary>
    /// v0.14 §S6 helper — element-annotation-preserving BoundType
    /// construction. Widens <see cref="ToNominalBoundType"/> to recognize
    /// <see cref="Microsoft.CodeAnalysis.IArrayTypeSymbol"/> and preserve
    /// the element's NullableAnnotation on an <see cref="ArrayBoundType"/>
    /// wrapper. Non-array types fall through to the flat NominalBoundType
    /// shape unchanged.
    /// </summary>
    internal static BoundType ToBoundType(Microsoft.CodeAnalysis.ITypeSymbol typeSymbol)
    {
        if (typeSymbol is Microsoft.CodeAnalysis.IArrayTypeSymbol arraySymbol)
        {
            var elementBound = ToBoundType(arraySymbol.ElementType);
            return new ArrayBoundType(
                elementType: elementBound,
                rank: arraySymbol.Rank,
                nullableAnnotation: MapAnnotation(arraySymbol.NullableAnnotation));
        }

        // v0.14 §S7 — preserve per-argument NullableAnnotation on generic
        // instantiations so a BCL return of e.g. List<string?> flows to the
        // nullability checker as GenericInstantiationBoundType(Definition=List,
        // TypeArguments=[NominalBoundType(STRING, Annotated)]). Mirrors what
        // the IArrayTypeSymbol branch does for arrays. Non-generic named
        // types fall through to the flat NominalBoundType shape unchanged.
        if (typeSymbol is Microsoft.CodeAnalysis.INamedTypeSymbol named
            && named.IsGenericType
            && !named.TypeArguments.IsDefaultOrEmpty)
        {
            var definition = new NominalBoundType(
                qualifiedName: named.ConstructedFrom.ToDisplayString(),
                nullableAnnotation: Calor.Compiler.Binding.BoundTypes.NullableAnnotation.Oblivious,
                declaration: null,
                roslynSymbol: named.ConstructedFrom);
            var args = named.TypeArguments
                .Select(ToBoundType)
                .ToImmutableArray();
            return new GenericInstantiationBoundType(
                definition,
                args,
                MapAnnotation(named.NullableAnnotation));
        }

        return ToNominalBoundType(typeSymbol);
    }

    /// <summary>Maps Roslyn's NullableAnnotation (None/NotAnnotated/Annotated)
    /// to Calor's NullableAnnotation (Oblivious/NotAnnotated/Annotated).
    /// Per D3 of docs/plans/v0.14-nullability-enforcement-scoping.md, Roslyn's
    /// <c>None</c> maps to <c>Oblivious</c> and is treated conservatively
    /// (possibly-null) at check time.</summary>
    internal static Calor.Compiler.Binding.BoundTypes.NullableAnnotation MapAnnotation(Microsoft.CodeAnalysis.NullableAnnotation roslyn) =>
        roslyn switch
        {
            Microsoft.CodeAnalysis.NullableAnnotation.Annotated => Calor.Compiler.Binding.BoundTypes.NullableAnnotation.Annotated,
            Microsoft.CodeAnalysis.NullableAnnotation.NotAnnotated => Calor.Compiler.Binding.BoundTypes.NullableAnnotation.NotAnnotated,
            _ => Calor.Compiler.Binding.BoundTypes.NullableAnnotation.Oblivious,
        };
}
