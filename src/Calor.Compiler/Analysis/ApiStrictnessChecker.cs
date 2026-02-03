using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;

namespace Calor.Compiler.Analysis;

/// <summary>
/// Compilation options for API strictness checking.
/// </summary>
public sealed class ApiStrictnessOptions
{
    /// <summary>
    /// When true, requires §BREAKING markers for any public API changes.
    /// Default is false for backward compatibility.
    /// </summary>
    public bool StrictApi { get; init; }

    /// <summary>
    /// When true, requires doc comments on all public functions and types.
    /// </summary>
    public bool RequireDocs { get; init; }

    /// <summary>
    /// When true, experimental APIs must be marked with §EXPERIMENTAL.
    /// </summary>
    public bool RequireStabilityMarkers { get; init; }

    public static ApiStrictnessOptions Default => new() { StrictApi = false, RequireDocs = false, RequireStabilityMarkers = false };
    public static ApiStrictnessOptions Strict => new() { StrictApi = true, RequireDocs = true, RequireStabilityMarkers = true };
}

/// <summary>
/// Checks for API strictness rules to prevent accidental breaking changes.
/// This enables agent confidence by making public API contracts explicit.
/// </summary>
public sealed class ApiStrictnessChecker
{
    private readonly DiagnosticBag _diagnostics;
    private readonly ApiStrictnessOptions _options;

    public ApiStrictnessChecker(DiagnosticBag diagnostics, ApiStrictnessOptions? options = null)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _options = options ?? ApiStrictnessOptions.Default;
    }

    /// <summary>
    /// Check a module for API strictness violations.
    /// </summary>
    public void Check(ModuleNode module)
    {
        // Check module-level documentation
        if (_options.RequireDocs && !HasModuleLevelDocs(module))
        {
            _diagnostics.ReportWarning(
                module.Span,
                DiagnosticCode.MissingDocComment,
                $"Module '{module.Name}' is missing documentation. Add §CONTEXT or decision records.");
        }

        // Check each public function
        foreach (var func in module.Functions)
        {
            CheckFunction(func);
        }

        // Check each public interface
        foreach (var iface in module.Interfaces)
        {
            CheckInterface(iface);
        }

        // Check each public class
        foreach (var cls in module.Classes)
        {
            CheckClass(cls);
        }
    }

    private void CheckFunction(FunctionNode func)
    {
        // Only check public functions
        if (func.Visibility != Visibility.Public) return;

        // Check for documentation
        if (_options.RequireDocs && !HasFunctionDocs(func))
        {
            _diagnostics.ReportWarning(
                func.Span,
                DiagnosticCode.MissingDocComment,
                $"Public function '{func.Name}' is missing documentation. " +
                "Add §ASSUME, §USES, or examples to document behavior.");
        }

        // Check for stability markers on public APIs without §SINCE
        if (_options.RequireStabilityMarkers && func.Since == null && func.Deprecated == null)
        {
            _diagnostics.ReportInfo(
                func.Span,
                DiagnosticCode.PublicApiChanged,
                $"Public function '{func.Name}' has no version marker. " +
                "Consider adding §SINCE[version] or §EXPERIMENTAL to indicate stability.");
        }

        // In strict mode, warn about public functions without contracts
        if (_options.StrictApi && !func.HasContracts && !HasFunctionDocs(func))
        {
            _diagnostics.ReportWarning(
                func.Span,
                DiagnosticCode.MissingDocComment,
                $"Public function '{func.Name}' has no contracts or documentation. " +
                "Add §Q (precondition), §S (postcondition), or documentation.");
        }
    }

    private void CheckInterface(InterfaceDefinitionNode iface)
    {
        // InterfaceDefinitionNode doesn't have Visibility - treat all as public
        if (_options.RequireDocs)
        {
            // Interfaces should have documentation
            _diagnostics.ReportWarning(
                iface.Span,
                DiagnosticCode.MissingDocComment,
                $"Interface '{iface.Name}' should have documentation comments.");
        }
    }

    private void CheckClass(ClassDefinitionNode cls)
    {
        // ClassDefinitionNode doesn't have Visibility - treat all as public
        if (_options.RequireDocs)
        {
            _diagnostics.ReportWarning(
                cls.Span,
                DiagnosticCode.MissingDocComment,
                $"Class '{cls.Name}' should have documentation comments.");
        }

        // Check methods
        foreach (var method in cls.Methods)
        {
            if (method.Visibility == Visibility.Public)
            {
                CheckMethod(method);
            }
        }

        // Check public properties
        foreach (var prop in cls.Properties)
        {
            if (prop.Visibility == Visibility.Public && _options.RequireDocs)
            {
                _diagnostics.ReportInfo(
                    prop.Span,
                    DiagnosticCode.MissingDocComment,
                    $"Public property '{prop.Name}' should have documentation.");
            }
        }
    }

    private void CheckMethod(MethodNode method)
    {
        if (method.Visibility != Visibility.Public) return;

        // Check for documentation
        if (_options.RequireDocs)
        {
            _diagnostics.ReportWarning(
                method.Span,
                DiagnosticCode.MissingDocComment,
                $"Public method '{method.Name}' is missing documentation.");
        }
    }

    private static bool HasModuleLevelDocs(ModuleNode module)
    {
        return module.Context != null ||
               module.Decisions.Count > 0 ||
               module.Assumptions.Count > 0 ||
               module.Invariants.Count > 0;
    }

    private static bool HasFunctionDocs(FunctionNode func)
    {
        return func.HasExtendedMetadata ||
               func.HasContracts ||
               func.Examples.Count > 0 ||
               func.Assumptions.Count > 0 ||
               func.Uses != null ||
               func.UsedBy != null;
    }
}

/// <summary>
/// Checks for breaking changes between two versions of an API.
/// This is used for semantic diff to detect contract drift.
/// </summary>
public sealed class BreakingChangeDetector
{
    private readonly DiagnosticBag _diagnostics;

    public BreakingChangeDetector(DiagnosticBag diagnostics)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    /// <summary>
    /// Compare two module versions and report breaking changes.
    /// </summary>
    public BreakingChangeReport Compare(ModuleNode oldModule, ModuleNode newModule)
    {
        var report = new BreakingChangeReport();

        // Index old functions by ID
        var oldFunctions = oldModule.Functions
            .Where(f => f.Visibility == Visibility.Public)
            .ToDictionary(f => f.Id, f => f);

        // Check each public function in new module
        foreach (var newFunc in newModule.Functions.Where(f => f.Visibility == Visibility.Public))
        {
            if (!oldFunctions.TryGetValue(newFunc.Id, out var oldFunc))
            {
                // New function - not a breaking change
                report.AddedFunctions.Add(newFunc.Name);
                continue;
            }

            // Compare signatures
            var changes = CompareFunctions(oldFunc, newFunc);
            if (changes.Count > 0)
            {
                // Check if breaking change is documented
                var hasBreakingMarker = newFunc.BreakingChanges.Count > 0;

                foreach (var change in changes)
                {
                    if (!hasBreakingMarker)
                    {
                        _diagnostics.ReportError(
                            newFunc.Span,
                            DiagnosticCode.BreakingChangeWithoutMarker,
                            $"Breaking change in '{newFunc.Name}': {change}. Add §BREAKING marker to document this change.");
                    }
                    report.BreakingChanges.Add($"{newFunc.Name}: {change}");
                }
            }

            oldFunctions.Remove(newFunc.Id);
        }

        // Remaining old functions were removed - breaking change
        foreach (var (id, oldFunc) in oldFunctions)
        {
            _diagnostics.ReportError(
                newModule.Span,
                DiagnosticCode.BreakingChangeWithoutMarker,
                $"Public function '{oldFunc.Name}' was removed. This is a breaking change.");
            report.RemovedFunctions.Add(oldFunc.Name);
        }

        return report;
    }

    private List<string> CompareFunctions(FunctionNode oldFunc, FunctionNode newFunc)
    {
        var changes = new List<string>();

        // Check parameter count
        if (oldFunc.Parameters.Count != newFunc.Parameters.Count)
        {
            changes.Add($"Parameter count changed from {oldFunc.Parameters.Count} to {newFunc.Parameters.Count}");
        }
        else
        {
            // Check parameter types
            for (var i = 0; i < oldFunc.Parameters.Count; i++)
            {
                var oldParam = oldFunc.Parameters[i];
                var newParam = newFunc.Parameters[i];

                if (oldParam.TypeName != newParam.TypeName)
                {
                    changes.Add($"Parameter '{oldParam.Name}' type changed from {oldParam.TypeName} to {newParam.TypeName}");
                }

                if (oldParam.Name != newParam.Name)
                {
                    changes.Add($"Parameter name changed from '{oldParam.Name}' to '{newParam.Name}'");
                }
            }
        }

        // Check return type
        var oldReturn = oldFunc.Output?.TypeName ?? "void";
        var newReturn = newFunc.Output?.TypeName ?? "void";
        if (oldReturn != newReturn)
        {
            changes.Add($"Return type changed from {oldReturn} to {newReturn}");
        }

        // Check visibility change (only breaking if going from public to private)
        if (oldFunc.Visibility == Visibility.Public && newFunc.Visibility != Visibility.Public)
        {
            changes.Add("Visibility changed from public to non-public");
        }

        // Check precondition changes (added preconditions are breaking)
        if (newFunc.Preconditions.Count > oldFunc.Preconditions.Count)
        {
            changes.Add($"New preconditions added ({newFunc.Preconditions.Count - oldFunc.Preconditions.Count} new)");
        }

        return changes;
    }
}

/// <summary>
/// Report of breaking changes between API versions.
/// </summary>
public sealed class BreakingChangeReport
{
    public List<string> BreakingChanges { get; } = new();
    public List<string> AddedFunctions { get; } = new();
    public List<string> RemovedFunctions { get; } = new();

    public bool HasBreakingChanges => BreakingChanges.Count > 0 || RemovedFunctions.Count > 0;
}
