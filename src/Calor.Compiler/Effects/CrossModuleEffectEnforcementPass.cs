using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Effects;

/// <summary>
/// Verifies that each function's declared effects (via §E) cover the declared effects
/// of any cross-module Calor functions it calls. Runs over per-module effect summaries,
/// which lets it work correctly on warm builds where some modules are incrementally cached.
///
/// Unlike the per-module <see cref="EffectEnforcementPass"/>, this pass:
///   - Works across multiple modules using their declared (contract) effects
///   - Sees bare-name cross-module calls (e.g., §C{SaveOrder})
///   - Reports cross-module violations as errors unconditionally — Permissive mode in
///     the per-file pass only demotes unknown-call warnings, not known-callee mismatches
/// </summary>
public sealed class CrossModuleEffectEnforcementPass
{
    /// <summary>
    /// Enforce cross-module effect propagation over a collection of module summaries.
    /// Null or missing list fields on a summary are tolerated as empty — this keeps
    /// the pass robust against hand-edited caches or partial-format summaries from
    /// future schema migrations.
    /// </summary>
    public List<Diagnostic> Enforce(
        IReadOnlyList<(EffectSummary Summary, string FilePath)> summaries,
        CrossModuleEffectRegistry registry)
    {
        var diagnostics = new List<Diagnostic>();

        foreach (var (summary, filePath) in summaries)
        {
            var internalNames = BuildInternalNameSet(summary);
            if (summary.Callers == null)
                continue;

            foreach (var caller in summary.Callers)
            {
                if (caller == null)
                    continue;
                CheckCaller(caller, filePath, internalNames, registry, diagnostics);
            }
        }

        return diagnostics;
    }

    /// <summary>
    /// Convenience overload: builds summaries from ASTs on the fly, then enforces.
    /// Used by the CLI path which has no build cache.
    /// </summary>
    public List<Diagnostic> Enforce(
        IReadOnlyList<(ModuleNode Ast, string FilePath)> modules,
        CrossModuleEffectRegistry registry)
    {
        var summaries = new List<(EffectSummary, string)>(modules.Count);
        foreach (var (ast, path) in modules)
        {
            summaries.Add((EffectSummaryBuilder.Build(ast), path));
        }
        return Enforce(summaries, registry);
    }

    private static void CheckCaller(
        EffectCallerSummary caller,
        string filePath,
        HashSet<string> internalNames,
        CrossModuleEffectRegistry registry,
        List<Diagnostic> diagnostics)
    {
        var declaredEffects = EffectSummaryBuilder.ToEffectSet(caller.DeclaredEffects);
        if (caller.Calls == null)
            return;

        foreach (var call in caller.Calls)
        {
            if (call == null || string.IsNullOrEmpty(call.Target))
                continue;
            if (call.IsConstructor)
                continue;

            // Resolution order:
            //   1. If the registry has a match, use it — with one exception: for bare-name
            //      targets (no dot), internal functions with the same name shadow the
            //      cross-module export (standard scoping: local wins over "imported").
            //      For DOTTED targets, the user explicitly qualified the call, so the
            //      registry's qualified match is authoritative even when the bare method
            //      name happens to collide with an internal name.
            //   2. If no registry match, the target is either an internal call (handled by
            //      the per-module pass) or an unresolved call (left alone here).
            var resolution = registry.TryResolve(call.Target);
            if (resolution == null)
                continue;

            var isDottedTarget = call.Target.Contains('.');
            if (!isDottedTarget && IsInternalCall(call.Target, internalNames))
                continue;

            // The registry may have resolved to our own module if a function with the
            // same name is re-exported or duplicate-registered; skip self-module matches.
            if (string.Equals(resolution.ModulePath, filePath, StringComparison.OrdinalIgnoreCase))
                continue;

            VerifyEffects(caller, filePath, resolution, declaredEffects, diagnostics);
        }
    }

    private static HashSet<string> BuildInternalNameSet(EffectSummary summary)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (summary.InternalFunctionNames != null)
        {
            foreach (var name in summary.InternalFunctionNames)
            {
                if (!string.IsNullOrEmpty(name))
                    set.Add(name);
            }
        }
        if (summary.InternalMethodNames != null)
        {
            foreach (var name in summary.InternalMethodNames)
            {
                if (!string.IsNullOrEmpty(name))
                    set.Add(name);
            }
        }
        return set;
    }

    private static bool IsInternalCall(string target, HashSet<string> internalNames)
    {
        if (internalNames.Contains(target))
            return true;

        var lastDot = target.LastIndexOf('.');
        if (lastDot > 0)
        {
            var bareMethodName = target[(lastDot + 1)..];
            if (internalNames.Contains(bareMethodName))
                return true;
        }

        return false;
    }

    private static void VerifyEffects(
        EffectCallerSummary caller,
        string callerFilePath,
        CrossModuleResolution resolution,
        EffectSet declaredEffects,
        List<Diagnostic> diagnostics)
    {
        if (resolution.DeclaredEffects.IsSubsetOf(declaredEffects))
            return;

        var forbidden = resolution.DeclaredEffects.Except(declaredEffects).ToList();
        var diagnosticSpan = new TextSpan(0, 0, caller.DiagnosticLine, caller.DiagnosticColumn);
        // Caller names for class methods are formatted "ClassName.MethodName".
        var callerKind = caller.CallerName.Contains('.') ? "Method" : "Function";

        foreach (var (kind, value) in forbidden)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCode.ForbiddenEffect,
                $"{callerKind} '{caller.CallerName}' uses effect '{EffectSetExtensions.ToSurfaceCode(kind, value)}' " +
                $"via cross-module call to '{resolution.FunctionName}' (in module '{resolution.ModuleName}') " +
                $"but does not declare it.",
                diagnosticSpan,
                DiagnosticSeverity.Error,
                callerFilePath));
        }
    }
}
