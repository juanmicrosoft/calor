using System.Reflection;
using Calor.Compiler;
using Calor.Compiler.IR;
using Calor.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using CalorCompilationOptions = Calor.Compiler.CompilationOptions;
using RoslynDiagnosticSeverity = Microsoft.CodeAnalysis.DiagnosticSeverity;

namespace Calor.Semantics.Tests;

/// <summary>
/// Test harness for semantics tests, extending the base TestHarness with
/// CNF-specific functionality and side-effect tracking.
/// </summary>
public static class SemanticsTestHarness
{
    /// <summary>
    /// Compiles Calor source and returns the CNF intermediate representation.
    /// </summary>
    public static CnfModule CompileToCnf(string source)
    {
        var result = Program.Compile(source, "test.calr", new CalorCompilationOptions
        {
            EnforceEffects = false
        });

        if (result.HasErrors)
        {
            throw new InvalidOperationException(
                $"Compilation failed: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}");
        }

        // Get the parsed module and lower to CNF
        var module = result.Ast ?? throw new InvalidOperationException("No module in compilation result");
        var lowering = new CnfLowering();
        return lowering.LowerModule(module);
    }

    /// <summary>
    /// Executes Calor code and captures side effects in order.
    /// Side effects are tracked via a special SideEffectTracker that is
    /// injected into the generated code.
    /// </summary>
    public static (object? Result, List<string> SideEffects) ExecuteWithSideEffects(
        string source,
        string methodName,
        object?[]? args = null)
    {
        var result = Program.Compile(source, "test.calr", new CalorCompilationOptions
        {
            EnforceEffects = false,
            ContractMode = ContractMode.Debug
        });

        if (result.HasErrors)
        {
            throw new InvalidOperationException(
                $"Compilation failed: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}");
        }

        // Generate wrapper code that tracks side effects
        var csharpCode = WrapWithSideEffectTracking(result.GeneratedCode);

        return ExecuteGeneratedCode(csharpCode, methodName, args);
    }

    /// <summary>
    /// Compiles and executes Calor code, returning the result.
    /// </summary>
    public static RuntimeResult Execute(
        string source,
        string methodName,
        object?[]? args = null)
    {
        var result = Program.Compile(source, "test.calr", new CalorCompilationOptions
        {
            EnforceEffects = false,
            ContractMode = ContractMode.Debug
        });

        if (result.HasErrors)
        {
            return new RuntimeResult
            {
                Exception = new InvalidOperationException(
                    $"Compilation failed: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}")
            };
        }

        return ExecuteGeneratedCodeBasic(result.GeneratedCode, methodName, args);
    }

    /// <summary>
    /// Compiles and executes Calor code with checked arithmetic for overflow testing.
    /// </summary>
    public static RuntimeResult ExecuteChecked(
        string source,
        string methodName,
        object?[]? args = null)
    {
        var result = Program.Compile(source, "test.calr", new CalorCompilationOptions
        {
            EnforceEffects = false,
            ContractMode = ContractMode.Debug
        });

        if (result.HasErrors)
        {
            return new RuntimeResult
            {
                Exception = new InvalidOperationException(
                    $"Compilation failed: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}")
            };
        }

        // Wrap arithmetic in checked context
        var checkedCode = WrapInCheckedContext(result.GeneratedCode);
        return ExecuteGeneratedCodeBasic(checkedCode, methodName, args);
    }

    private static string WrapWithSideEffectTracking(string csharpCode)
    {
        // Add SideEffectTracker class to the code
        var tracker = @"
public static class SideEffectTracker
{
    public static readonly System.Collections.Generic.List<string> Effects = new();
    public static void Record(string effect) => Effects.Add(effect);
    public static void Clear() => Effects.Clear();
}
";
        return tracker + "\n" + csharpCode;
    }

    private static string WrapInCheckedContext(string csharpCode)
    {
        // This is a simplified approach - in a full implementation,
        // we would modify the AST or use Roslyn to wrap arithmetic expressions
        // For now, we'll use assembly-level CheckForOverflowUnderflow
        return csharpCode;
    }

    private static (object? Result, List<string> SideEffects) ExecuteGeneratedCode(
        string csharpCode,
        string methodName,
        object?[]? args)
    {
        var sideEffects = new List<string>();

        try
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(csharpCode);

            var references = GetReferences();

            var compilation = CSharpCompilation.Create(
                "TestAssembly",
                new[] { syntaxTree },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                    .WithOverflowChecks(true)); // Enable checked arithmetic

            using var ms = new MemoryStream();
            var emitResult = compilation.Emit(ms);

            if (!emitResult.Success)
            {
                var errors = string.Join("\n", emitResult.Diagnostics
                    .Where(d => d.Severity == RoslynDiagnosticSeverity.Error)
                    .Select(d => d.ToString()));
                throw new InvalidOperationException($"C# compilation failed:\n{errors}");
            }

            ms.Seek(0, SeekOrigin.Begin);
            var assembly = Assembly.Load(ms.ToArray());

            // Clear tracker
            var trackerType = assembly.GetType("SideEffectTracker");
            trackerType?.GetMethod("Clear")?.Invoke(null, null);

            // Find and invoke the method
            MethodInfo? method = null;
            foreach (var type in assembly.GetTypes())
            {
                method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
                if (method != null) break;
            }

            if (method == null)
            {
                throw new InvalidOperationException($"Method '{methodName}' not found");
            }

            object? returnValue;
            try
            {
                returnValue = method.Invoke(null, args);
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                throw tie.InnerException;
            }

            // Get recorded effects
            if (trackerType != null)
            {
                var effectsField = trackerType.GetField("Effects", BindingFlags.Public | BindingFlags.Static);
                if (effectsField?.GetValue(null) is List<string> effects)
                {
                    sideEffects.AddRange(effects);
                }
            }

            return (returnValue, sideEffects);
        }
        catch (Exception ex)
        {
            return (null, new List<string> { $"ERROR: {ex.Message}" });
        }
    }

    private static RuntimeResult ExecuteGeneratedCodeBasic(string csharpCode, string methodName, object?[]? args)
    {
        try
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(csharpCode);

            var references = GetReferences();

            var compilation = CSharpCompilation.Create(
                "TestAssembly",
                new[] { syntaxTree },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                    .WithOverflowChecks(true)); // Enable checked arithmetic

            using var ms = new MemoryStream();
            var emitResult = compilation.Emit(ms);

            if (!emitResult.Success)
            {
                var errors = string.Join("\n", emitResult.Diagnostics
                    .Where(d => d.Severity == RoslynDiagnosticSeverity.Error)
                    .Select(d => d.ToString()));
                return new RuntimeResult
                {
                    Exception = new InvalidOperationException($"C# compilation failed:\n{errors}\n\nGenerated code:\n{csharpCode}")
                };
            }

            ms.Seek(0, SeekOrigin.Begin);
            var assembly = Assembly.Load(ms.ToArray());

            MethodInfo? method = null;
            foreach (var type in assembly.GetTypes())
            {
                method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
                if (method != null) break;
            }

            if (method == null)
            {
                return new RuntimeResult
                {
                    Exception = new InvalidOperationException($"Method '{methodName}' not found in generated assembly")
                };
            }

            try
            {
                var returnValue = method.Invoke(null, args);
                return new RuntimeResult { ReturnValue = returnValue };
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                return new RuntimeResult { Exception = tie.InnerException };
            }
        }
        catch (Exception ex)
        {
            return new RuntimeResult { Exception = ex };
        }
    }

    private static List<MetadataReference> GetReferences()
    {
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ContractViolationException).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
        };

        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Collections.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Linq.dll")));

        return references;
    }
}

/// <summary>
/// Result of executing Calor code at runtime.
/// </summary>
public class RuntimeResult
{
    public object? ReturnValue { get; init; }
    public Exception? Exception { get; init; }
    public bool Succeeded => Exception == null;
}
