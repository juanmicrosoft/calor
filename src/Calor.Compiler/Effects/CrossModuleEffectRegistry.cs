using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Effects;

/// <summary>
/// Resolution result for a cross-module function call: which module it came from,
/// the function's name, and the effects it declared via §E.
/// </summary>
public sealed record CrossModuleResolution(
    string ModulePath,
    string ModuleName,
    string FunctionName,
    EffectSet DeclaredEffects);

/// <summary>
/// Tracks declared effects of public/internal Calor functions across multiple modules,
/// so that cross-module callers can verify their §E declarations cover the effects
/// of functions they call.
///
/// Registry keys:
///   - Bare function name: "SaveOrder" → unique match only (skipped if ambiguous across modules)
///   - Module-qualified: "OrderService.SaveOrder"
///   - Class-qualified for class methods: "ClassName.MethodName"
///
/// The registry is built from <see cref="EffectSummary"/> — a serializable projection of
/// each module's public surface. This lets warm builds mix fresh summaries (from files
/// that just recompiled) with cached summaries (from files that were incrementally skipped)
/// without needing ASTs for the skipped files.
/// </summary>
public sealed class CrossModuleEffectRegistry
{
    private readonly Dictionary<string, CrossModuleResolution> _qualified =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<CrossModuleResolution>> _bareName =
        new(StringComparer.Ordinal);

    private CrossModuleEffectRegistry() { }

    /// <summary>
    /// Diagnostics produced while building the registry (e.g., Calor0417 for undeclared
    /// public functions). These must be reported by the caller.
    /// </summary>
    public List<Diagnostic> BuildDiagnostics { get; } = new();

    /// <summary>
    /// Build the registry from a set of module summaries. This is the primary entry point;
    /// warm builds pass in a mix of freshly-computed and cache-restored summaries.
    /// Null list fields on a summary (e.g., from a hand-edited or partial cache) are
    /// tolerated — treated as empty.
    /// </summary>
    public static CrossModuleEffectRegistry Build(
        IReadOnlyList<(EffectSummary Summary, string FilePath)> summaries)
    {
        var registry = new CrossModuleEffectRegistry();

        foreach (var (summary, filePath) in summaries)
        {
            if (summary.PublicFunctions != null)
            {
                foreach (var func in summary.PublicFunctions)
                {
                    if (func != null)
                        registry.RegisterFunction(summary.ModuleName ?? "", filePath, func);
                }
            }
            if (summary.PublicMethods != null)
            {
                foreach (var method in summary.PublicMethods)
                {
                    if (method != null)
                        registry.RegisterMethod(summary.ModuleName ?? "", filePath, method);
                }
            }
        }

        return registry;
    }

    /// <summary>
    /// Convenience overload: builds summaries from ASTs on the fly, then builds the registry.
    /// Used by the CLI path which has no build cache.
    /// </summary>
    public static CrossModuleEffectRegistry Build(
        IReadOnlyList<(ModuleNode Ast, string FilePath)> modules)
    {
        var summaries = new List<(EffectSummary, string)>(modules.Count);
        foreach (var (ast, path) in modules)
        {
            summaries.Add((EffectSummaryBuilder.Build(ast), path));
        }
        return Build(summaries);
    }

    private void RegisterFunction(string moduleName, string filePath, EffectFunctionSummary func)
    {
        if (!func.HasEffectDeclaration)
        {
            BuildDiagnostics.Add(new Diagnostic(
                DiagnosticCode.UndeclaredPublicFunction,
                $"Public function '{func.Name}' in module '{moduleName}' has no effect declaration. " +
                $"Cross-module callers cannot verify effect safety. Add §E{{...}} to declare effects.",
                new TextSpan(0, 0, func.DeclarationLine, func.DeclarationColumn),
                DiagnosticSeverity.Warning,
                filePath));
            return;
        }

        var declaredEffects = EffectSummaryBuilder.ToEffectSet(func.DeclaredEffects);
        var resolution = new CrossModuleResolution(
            filePath, moduleName, func.Name, declaredEffects);

        AddQualified($"{moduleName}.{func.Name}", resolution);
        AddBareName(func.Name, resolution);
    }

    private void RegisterMethod(string moduleName, string filePath, EffectFunctionSummary method)
    {
        var className = method.ClassName ?? "";
        var qualifiedName = string.IsNullOrEmpty(className) ? method.Name : $"{className}.{method.Name}";

        if (!method.HasEffectDeclaration)
        {
            BuildDiagnostics.Add(new Diagnostic(
                DiagnosticCode.UndeclaredPublicFunction,
                $"Public method '{qualifiedName}' in module '{moduleName}' has no effect declaration. " +
                $"Cross-module callers cannot verify effect safety. Add §E{{...}} to declare effects.",
                new TextSpan(0, 0, method.DeclarationLine, method.DeclarationColumn),
                DiagnosticSeverity.Warning,
                filePath));
            return;
        }

        var declaredEffects = EffectSummaryBuilder.ToEffectSet(method.DeclaredEffects);
        var resolution = new CrossModuleResolution(
            filePath, moduleName, qualifiedName, declaredEffects);

        AddQualified($"{moduleName}.{qualifiedName}", resolution);
        AddQualified(qualifiedName, resolution);
    }

    private void AddQualified(string key, CrossModuleResolution resolution)
    {
        // First definition wins; conflicting duplicates at qualified names indicate user
        // error and are handled elsewhere (duplicate name diagnostics).
        _qualified.TryAdd(key, resolution);
    }

    private void AddBareName(string name, CrossModuleResolution resolution)
    {
        if (!_bareName.TryGetValue(name, out var list))
        {
            list = new List<CrossModuleResolution>();
            _bareName[name] = list;
        }
        list.Add(resolution);
    }

    /// <summary>
    /// Try to resolve a call target to a registered cross-module function.
    /// Resolution priority:
    ///   1. Exact qualified match (e.g., "OrderService.SaveOrder" or "ClassName.MethodName").
    ///   2. Bare name match — only if unambiguous (exactly one registration).
    /// Returns null if the target is not registered or the bare name is ambiguous.
    /// </summary>
    public CrossModuleResolution? TryResolve(string callTarget)
    {
        if (string.IsNullOrEmpty(callTarget))
            return null;

        if (_qualified.TryGetValue(callTarget, out var qualified))
            return qualified;

        // Bare name: only resolve if unambiguous
        if (!callTarget.Contains('.') &&
            _bareName.TryGetValue(callTarget, out var matches) &&
            matches.Count == 1)
        {
            return matches[0];
        }

        return null;
    }
}
