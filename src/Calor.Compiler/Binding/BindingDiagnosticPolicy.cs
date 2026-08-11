using Calor.Compiler.Diagnostics;

namespace Calor.Compiler.Binding;

/// <summary>
/// Defines which binder diagnostics are part of compilation semantics rather
/// than optional analysis instrumentation.
/// </summary>
public static class BindingDiagnosticPolicy
{
    public static bool IsCompilationError(Diagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        if (!diagnostic.IsError)
            return false;

        // Only promote diagnostics whose binder decision is complete without
        // external .NET member/type knowledge. The binder emits overload
        // diagnostics only after finding a known internal candidate set;
        // unresolved external/interop calls remain NotFound and diagnostic-free.
        return diagnostic.Code is
            DiagnosticCode.DuplicateDefinition
            or DiagnosticCode.DuplicateFunctionSignature
            or DiagnosticCode.AmbiguousOverload
            or DiagnosticCode.NoMatchingOverload
            or DiagnosticCode.BindRequiresTypeOrInitializer
            or DiagnosticCode.InstanceMemberInStaticContext;
    }

    public static void PropagateCompilationErrors(
        IEnumerable<Diagnostic> source,
        DiagnosticBag destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        foreach (var diagnostic in source.Where(IsCompilationError))
        {
            if (destination.Any(existing =>
                    existing.Code == diagnostic.Code
                    && existing.Span == diagnostic.Span
                    && existing.Message == diagnostic.Message
                    && existing.Severity == diagnostic.Severity))
            {
                continue;
            }

            destination.Add(diagnostic);
        }
    }
}
