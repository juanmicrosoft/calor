using System.Collections.Frozen;
using Calor.Compiler.Binding.BoundTypes;
using Calor.Compiler.Diagnostics;

namespace Calor.Compiler.Binding;

public enum BindingDiagnosticDisposition
{
    CompilationError,
    AnalysisOnly
}

public enum BindingReceivingBoundary
{
    None,
    Initializer,
    NativeReturn,
    MethodArgument
}

public enum BindingReceivingShape
{
    None,
    ScalarString,
    Array,
    Generic,
    Nominal,
    Unsupported
}

/// <summary>
/// Binder provenance and structural receiving context, not a null-state proof.
/// Array/generic/nominal shapes do not imply supported or resolved references.
/// </summary>
public sealed record BindingDiagnosticContext(
    BindingReceivingBoundary Boundary,
    BindingReceivingShape Shape)
{
    public static BindingDiagnosticContext General { get; } =
        new(BindingReceivingBoundary.None, BindingReceivingShape.None);

    public static BindingDiagnosticContext For(BindingReceivingBoundary boundary, BoundType target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return new(boundary, target switch
        {
            NominalBoundType nominal when TypeIdentity.Canonicalize(nominal.QualifiedName) == "STRING"
                => BindingReceivingShape.ScalarString,
            NominalBoundType => BindingReceivingShape.Nominal,
            ArrayBoundType => BindingReceivingShape.Array,
            GenericInstantiationBoundType => BindingReceivingShape.Generic,
            _ => BindingReceivingShape.Unsupported
        });
    }
}

public sealed record BindingDiagnosticRule(
    string Code,
    BindingDiagnosticDisposition Disposition,
    string Justification,
    int OwningIssue);

public sealed record BindingReceivingRule(
    string Code,
    BindingDiagnosticContext Context,
    BindingDiagnosticRule Policy);

/// <summary>
/// Current production/analysis routing. New binder Error-capable emissions must
/// be explicitly cataloged and pass the source-site ratchet before shipping.
/// </summary>
public static class BindingDiagnosticPolicy
{
    public static IReadOnlyDictionary<string, BindingDiagnosticRule> Catalog { get; } =
        new BindingDiagnosticRule[]
        {
            Active(DiagnosticCode.DuplicateDefinition, "Duplicate declarations are already compilation errors."),
            Active(DiagnosticCode.DuplicateFunctionSignature, "Duplicate callable signatures are already compilation errors."),
            Active(DiagnosticCode.AmbiguousOverload, "Ambiguous internal overload selection is already a compilation error."),
            Active(DiagnosticCode.NoMatchingOverload, "Failed internal overload selection is already a compilation error."),
            Active(DiagnosticCode.BindRequiresTypeOrInitializer, "A missing binding type/value is already a compilation error."),
            Active(DiagnosticCode.InstanceMemberInStaticContext, "Illegal static-context member access is already a compilation error."),
            Active(DiagnosticCode.EffectRowMisplaced, "Effect-row declaration placement is already enforced."),
            Active(DiagnosticCode.EffectVariableScope, "Unbound effect-row variables are already enforced."),
            Analysis(DiagnosticCode.ExpectedTypeName, "Parser owns the user-facing missing-type error; binder recovery is not a new activation.", 1396),
            Analysis(DiagnosticCode.UndefinedReference, "Incomplete binding/resolution can report false positives; preserve the current analysis-only exception.", 1396),
            Analysis(DiagnosticCode.TypeMismatch, "Binder variable-use recovery is not full production type checking; cross-pass ownership must remain explicit.", 1397),
            Analysis(DiagnosticCode.BindReassignsImmutable, "General mutable/rebind validation is outside the nullability receiving-boundary activation.", 1396),
            Analysis(DiagnosticCode.BindRebindTypeMismatch, "General rebind type checks retain their current analysis-only disposition.", 1396),
            Analysis(DiagnosticCode.AnalysisICE, "A failed best-effort member analysis is not a successful safety result or a new production gate.", 1396),
            Analysis(DiagnosticCode.SignatureUnresolved, "MetadataBinderResult.ToDiagnostics accepts caller-selected severity; it is internal measurement/resolution evidence, not production enforcement.", 1396),
            Analysis(DiagnosticCode.NullableToNonNullableBinding, "Nullability activation requires explicit receiving shape and its staged prerequisites.", 1385),
            Analysis(DiagnosticCode.NullableReturnFromNonNullable, "Nullability activation requires explicit receiving shape and its staged prerequisites.", 1385),
            Analysis(DiagnosticCode.NullableArgumentToNonNullableParameter, "Nullability activation requires explicit receiving shape and its staged prerequisites.", 1385)
        }.ToFrozenDictionary(rule => rule.Code, StringComparer.Ordinal);

    public static IReadOnlyList<BindingReceivingRule> ReceivingRules { get; } = CreateReceivingRules();

    public static BindingDiagnosticRule GetRule(string code, BindingDiagnosticContext? context = null)
    {
        if (!Catalog.TryGetValue(code, out var rule))
            throw new InvalidOperationException($"Unclassified binder diagnostic '{code}'. Add an explicit routing rule and source-site coverage.");

        if (context is not null)
        {
            var receiving = ReceivingRules.FirstOrDefault(candidate =>
                candidate.Code == code && candidate.Context == context);
            if (receiving is not null)
                return receiving.Policy;
        }
        return rule;
    }

    public static bool IsCompilationError(Diagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        if (!diagnostic.IsError)
            return false;
        // This helper also receives non-binder diagnostics in existing callers.
        if (diagnostic.BindingContext is null && !Catalog.ContainsKey(diagnostic.Code))
            return false;
        return GetRule(diagnostic.Code, diagnostic.BindingContext).Disposition
            == BindingDiagnosticDisposition.CompilationError;
    }

    public static bool IsAnalysisOnly(Diagnostic diagnostic) =>
        diagnostic.IsError && diagnostic.BindingContext is not null
        && GetRule(diagnostic.Code, diagnostic.BindingContext).Disposition == BindingDiagnosticDisposition.AnalysisOnly;

    public static void PropagateCompilationErrors(IEnumerable<Diagnostic> source, DiagnosticBag destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        foreach (var diagnostic in source.Where(IsCompilationError))
        {
            if (destination.Any(existing =>
                    existing.Code == diagnostic.Code && existing.Span == diagnostic.Span
                    && existing.Message == diagnostic.Message && existing.Severity == diagnostic.Severity))
                continue;
            destination.Add(diagnostic);
        }
    }

    private static BindingDiagnosticRule Active(string code, string reason) =>
        new(code, BindingDiagnosticDisposition.CompilationError, reason, 1396);

    private static BindingDiagnosticRule Analysis(string code, string reason, int issue) =>
        new(code, BindingDiagnosticDisposition.AnalysisOnly, reason, issue);

    private static IReadOnlyList<BindingReceivingRule> CreateReceivingRules()
    {
        var boundaries = new[]
        {
            (DiagnosticCode.NullableToNonNullableBinding, BindingReceivingBoundary.Initializer),
            (DiagnosticCode.NullableReturnFromNonNullable, BindingReceivingBoundary.NativeReturn),
            (DiagnosticCode.NullableArgumentToNonNullableParameter, BindingReceivingBoundary.MethodArgument)
        };
        return Array.AsReadOnly(boundaries.SelectMany(boundary =>
            Enum.GetValues<BindingReceivingShape>().Select(shape => new BindingReceivingRule(
                boundary.Item1, new(boundary.Item2, shape),
                Analysis(boundary.Item1,
                    shape == BindingReceivingShape.ScalarString
                        ? "Scalar STRING is planned Stage A, not activated by the routing catalog."
                        : "Non-scalar or unsupported receiving context is not Stage A; Stage B requires a separate measured decision.",
                    shape == BindingReceivingShape.ScalarString ? 1385 : 1402))))
            .ToArray());
    }
}
