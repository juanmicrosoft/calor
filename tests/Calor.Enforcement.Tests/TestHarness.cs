using System.Reflection;
using System.Text.Json;
using Calor.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// Avoid ambiguity with Microsoft.CodeAnalysis types
using CompilationOptions = Calor.Compiler.CompilationOptions;
using Diagnostic = Calor.Compiler.Diagnostics.Diagnostic;
using ContractMode = Calor.Compiler.ContractMode;
using DiagnosticSeverity = Microsoft.CodeAnalysis.DiagnosticSeverity;

namespace Calor.Enforcement.Tests;

/// <summary>
/// Test harness for compiling and executing Calor code in tests.
/// </summary>
public static class TestHarness
{
    /// <summary>
    /// Compiles Calor source and returns diagnostics and generated code.
    /// </summary>
    public static Calor.Compiler.CompilationResult Compile(string source, CompilationOptions? options = null)
    {
        options ??= new CompilationOptions();  // EnforceEffects = true by default
        return Calor.Compiler.Program.Compile(source, "test.calr", options);
    }

    /// <summary>
    /// Compiles Calor source with specific effect enforcement settings.
    /// </summary>
    public static Calor.Compiler.CompilationResult CompileWithEffects(
        string source,
        bool enforceEffects = true,
        Calor.Compiler.Effects.UnknownCallPolicy policy = Calor.Compiler.Effects.UnknownCallPolicy.Strict)
    {
        var options = new CompilationOptions
        {
            EnforceEffects = enforceEffects,
            UnknownCallPolicy = policy
        };
        return Calor.Compiler.Program.Compile(source, "test.calr", options);
    }

    /// <summary>
    /// Compiles Calor source with specific contract mode.
    /// </summary>
    public static Calor.Compiler.CompilationResult CompileWithContractMode(string source, ContractMode mode)
    {
        var options = new CompilationOptions
        {
            EnforceEffects = false,  // Don't fail on effect errors in contract tests
            ContractMode = mode
        };
        return Calor.Compiler.Program.Compile(source, "test.calr", options);
    }

    /// <summary>
    /// Compiles Calor → C# → Assembly → Execute method and returns the result.
    /// </summary>
    public static RuntimeResult Execute(
        string source,
        string methodName,
        object?[]? args = null,
        CompilationOptions? options = null)
    {
        options ??= new CompilationOptions
        {
            EnforceEffects = false,  // Allow execution even with effect warnings
            ContractMode = ContractMode.Debug
        };

        var result = Calor.Compiler.Program.Compile(source, "test.calr", options);

        if (result.HasErrors)
        {
            return new RuntimeResult
            {
                Exception = new InvalidOperationException(
                    $"Compilation failed: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}")
            };
        }

        return ExecuteGeneratedCode(result.GeneratedCode, methodName, args);
    }

    /// <summary>
    /// Compiles C# code to an assembly and executes a method.
    /// </summary>
    private static RuntimeResult ExecuteGeneratedCode(string csharpCode, string methodName, object?[]? args)
    {
        try
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(csharpCode);

            var references = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(ContractViolationException).Assembly.Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
            };

            // Add additional runtime references
            var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Collections.dll")));
            references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Text.RegularExpressions.dll")));

            var compilation = CSharpCompilation.Create(
                "TestAssembly",
                new[] { syntaxTree },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using var ms = new MemoryStream();
            var emitResult = compilation.Emit(ms);

            if (!emitResult.Success)
            {
                var errors = string.Join("\n", emitResult.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.ToString()));
                return new RuntimeResult
                {
                    Exception = new InvalidOperationException($"C# compilation failed:\n{errors}\n\nGenerated code:\n{csharpCode}")
                };
            }

            ms.Seek(0, SeekOrigin.Begin);
            var assembly = Assembly.Load(ms.ToArray());

            // Find the method (look in all types)
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

    /// <summary>
    /// Asserts that a specific diagnostic was emitted.
    /// </summary>
    public static void AssertDiagnostic(
        IEnumerable<Diagnostic> diagnostics,
        string code,
        string? functionIdContains = null,
        int? expectedLine = null)
    {
        var diag = diagnostics.FirstOrDefault(d => d.Code == code);

        if (diag == null)
        {
            var allCodes = string.Join(", ", diagnostics.Select(d => d.Code));
            throw new Xunit.Sdk.XunitException(
                $"Expected diagnostic {code} but found: [{allCodes}]");
        }

        if (functionIdContains != null && !diag.Message.Contains(functionIdContains))
        {
            throw new Xunit.Sdk.XunitException(
                $"Expected diagnostic message to contain '{functionIdContains}' but was: {diag.Message}");
        }

        if (expectedLine.HasValue && diag.Span.Line != expectedLine.Value)
        {
            throw new Xunit.Sdk.XunitException(
                $"Expected diagnostic at line {expectedLine.Value} but was at line {diag.Span.Line}");
        }
    }

    /// <summary>
    /// Asserts that a diagnostic message contains the expected call chain.
    /// </summary>
    public static void AssertCallChain(Diagnostic diagnostic, params string[] expectedChain)
    {
        var expectedStr = string.Join(" → ", expectedChain);
        if (!diagnostic.Message.Contains(expectedStr))
        {
            throw new Xunit.Sdk.XunitException(
                $"Expected call chain '{expectedStr}' in message: {diagnostic.Message}");
        }
    }

    /// <summary>
    /// Asserts no diagnostics with the specified code were emitted.
    /// </summary>
    public static void AssertNoDiagnostic(IEnumerable<Diagnostic> diagnostics, string code)
    {
        var diag = diagnostics.FirstOrDefault(d => d.Code == code);
        if (diag != null)
        {
            throw new Xunit.Sdk.XunitException(
                $"Expected no diagnostic {code} but found: {diag.Message}");
        }
    }

    /// <summary>
    /// Loads a test scenario file from the Scenarios directory.
    /// </summary>
    public static string LoadScenario(string relativePath)
    {
        var baseDir = AppContext.BaseDirectory;
        var fullPath = Path.Combine(baseDir, "Scenarios", relativePath);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Scenario file not found: {fullPath}");
        }

        return File.ReadAllText(fullPath);
    }

    /// <summary>
    /// Loads expected diagnostics from a JSON file.
    /// </summary>
    public static ExpectedDiagnostics LoadExpected(string relativePath)
    {
        var json = LoadScenario(relativePath);
        return JsonSerializer.Deserialize<ExpectedDiagnostics>(json)
            ?? throw new InvalidOperationException($"Failed to parse expected diagnostics from {relativePath}");
    }

    /// <summary>
    /// Asserts that actual diagnostics match expected diagnostics.
    /// </summary>
    public static void AssertDiagnosticsMatch(
        IEnumerable<Diagnostic> actual,
        ExpectedDiagnostics expected)
    {
        var actualList = actual.ToList();

        foreach (var exp in expected.Diagnostics)
        {
            var matching = actualList.FirstOrDefault(d =>
                d.Code == exp.Code &&
                (exp.FunctionId == null || d.Message.Contains(exp.FunctionId)) &&
                (exp.Line == null || d.Span.Line == exp.Line) &&
                (exp.MessageContains == null || d.Message.Contains(exp.MessageContains)));

            if (matching == null)
            {
                var actualCodes = string.Join(", ", actualList.Select(d => $"{d.Code}@{d.Span.Line}"));
                throw new Xunit.Sdk.XunitException(
                    $"Expected diagnostic {exp.Code} (function={exp.FunctionId}, line={exp.Line}) not found. Actual: [{actualCodes}]");
            }
        }
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

/// <summary>
/// Expected diagnostics loaded from JSON.
/// </summary>
public class ExpectedDiagnostics
{
    public List<ExpectedDiagnostic> Diagnostics { get; set; } = new();
}

/// <summary>
/// A single expected diagnostic.
/// </summary>
public class ExpectedDiagnostic
{
    public string Code { get; set; } = "";
    public string? FunctionId { get; set; }
    public int? Line { get; set; }
    public string? MessageContains { get; set; }
}
