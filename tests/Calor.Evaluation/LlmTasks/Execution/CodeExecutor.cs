using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Calor.Compiler;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.Evaluation.LlmTasks;
using Calor.Runtime;
using RoslynDiagnosticSeverity = Microsoft.CodeAnalysis.DiagnosticSeverity;

namespace Calor.Evaluation.LlmTasks.Execution;

/// <summary>
/// Executes generated code for both Calor and C# languages.
/// Handles compilation, assembly loading, and execution with timeouts.
/// </summary>
public sealed class CodeExecutor : IDisposable
{
    private readonly int _timeoutMs;
    private readonly List<AssemblyLoadContext> _loadContexts = new();
    private readonly EmitContractMode _contractMode;

    /// <summary>
    /// Creates a new code executor.
    /// </summary>
    /// <param name="timeoutMs">Execution timeout in milliseconds.</param>
    /// <param name="contractMode">Contract enforcement mode for Calor compilation.</param>
    public CodeExecutor(int timeoutMs = 5000, EmitContractMode contractMode = EmitContractMode.Off)
    {
        _timeoutMs = timeoutMs;
        _contractMode = contractMode;
    }

    /// <summary>
    /// Compiles Calor source code.
    /// </summary>
    /// <param name="calorSource">The Calor source code.</param>
    /// <returns>Compilation result with generated C# and any errors.</returns>
    public CalorCompileResult CompileCalor(string calorSource)
    {
        try
        {
            var diagnostics = new DiagnosticBag();

            // Lexical analysis
            var lexer = new Lexer(calorSource, diagnostics);
            var tokens = lexer.TokenizeAll();

            if (diagnostics.HasErrors)
            {
                return new CalorCompileResult
                {
                    Success = false,
                    Errors = diagnostics.Errors.Select(d => d.Message).ToList()
                };
            }

            // Parsing
            var parser = new Parser(tokens, diagnostics);
            var ast = parser.Parse();

            if (diagnostics.HasErrors)
            {
                return new CalorCompileResult
                {
                    Success = false,
                    Errors = diagnostics.Errors.Select(d => d.Message).ToList()
                };
            }

            // Code generation - use configured contract mode
            // Off mode: simple execution testing without contract overhead
            // Debug mode: full contract enforcement with detailed diagnostics (safety benchmark)
            var emitter = new CSharpEmitter(_contractMode);
            var generatedCSharp = emitter.Emit(ast);

            return new CalorCompileResult
            {
                Success = true,
                GeneratedCSharp = generatedCSharp,
                Warnings = diagnostics.Warnings.Select(d => d.Message).ToList()
            };
        }
        catch (Exception ex)
        {
            return new CalorCompileResult
            {
                Success = false,
                Errors = new List<string> { $"Compilation exception: {ex.Message}" }
            };
        }
    }

    /// <summary>
    /// Compiles C# source code to an assembly.
    /// </summary>
    /// <param name="csharpSource">The C# source code.</param>
    /// <param name="assemblyName">Name for the assembly.</param>
    /// <returns>Compilation result with assembly bytes or errors.</returns>
    public CSharpCompileResult CompileCSharp(string csharpSource, string? assemblyName = null)
    {
        assemblyName ??= $"LlmTask_{Guid.NewGuid():N}";

        // Auto-inject common using directives if not present
        // This fixes LLM-generated C# code that often omits these
        csharpSource = EnsureCommonUsings(csharpSource);

        try
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(csharpSource);

            // Check for parse errors
            var parseErrors = syntaxTree.GetDiagnostics()
                .Where(d => d.Severity == RoslynDiagnosticSeverity.Error)
                .ToList();

            if (parseErrors.Any())
            {
                return new CSharpCompileResult
                {
                    Success = false,
                    Errors = parseErrors.Select(d => d.GetMessage()).ToList()
                };
            }

            // Get references
            var references = GetRequiredReferences();

            var compilation = CSharpCompilation.Create(
                assemblyName,
                new[] { syntaxTree },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                    .WithOptimizationLevel(OptimizationLevel.Release)
                    .WithPlatform(Platform.AnyCpu));

            using var ms = new MemoryStream();
            var emitResult = compilation.Emit(ms);

            if (!emitResult.Success)
            {
                var errors = emitResult.Diagnostics
                    .Where(d => d.Severity == RoslynDiagnosticSeverity.Error)
                    .Select(d => d.GetMessage())
                    .ToList();

                return new CSharpCompileResult
                {
                    Success = false,
                    Errors = errors
                };
            }

            ms.Seek(0, SeekOrigin.Begin);
            return new CSharpCompileResult
            {
                Success = true,
                AssemblyBytes = ms.ToArray(),
                Warnings = emitResult.Diagnostics
                    .Where(d => d.Severity == RoslynDiagnosticSeverity.Warning)
                    .Select(d => d.GetMessage())
                    .ToList()
            };
        }
        catch (Exception ex)
        {
            return new CSharpCompileResult
            {
                Success = false,
                Errors = new List<string> { $"Compilation exception: {ex.Message}" }
            };
        }
    }

    /// <summary>
    /// Executes a method from compiled assembly bytes.
    /// </summary>
    /// <param name="assemblyBytes">The compiled assembly.</param>
    /// <param name="methodName">Name of the method to execute.</param>
    /// <param name="arguments">Arguments to pass to the method.</param>
    /// <param name="typeName">Optional type name containing the method.</param>
    /// <returns>Execution result.</returns>
    public ExecutionResult Execute(
        byte[] assemblyBytes,
        string methodName,
        object?[]? arguments = null,
        string? typeName = null)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Create isolated load context
            var loadContext = new AssemblyLoadContext($"LlmTask_{Guid.NewGuid():N}", isCollectible: true);
            _loadContexts.Add(loadContext);

            using var stream = new MemoryStream(assemblyBytes);
            var assembly = loadContext.LoadFromStream(stream);

            // Find the method
            var (type, method) = FindMethod(assembly, methodName, typeName);
            if (method == null)
            {
                var availableMethods = GetAvailableMethods(assembly);
                return new ExecutionResult
                {
                    Success = false,
                    Error = $"Method '{methodName}' not found in assembly. Available methods: {availableMethods}"
                };
            }

            // Execute with timeout
            object? result = null;
            Exception? exception = null;

            // Convert arguments to match parameter types
            var convertedArgs = ConvertArguments(method, arguments);

            using var cts = new CancellationTokenSource(_timeoutMs);
            var task = Task.Run(() =>
            {
                try
                {
                    var instance = method.IsStatic ? null : Activator.CreateInstance(type!);
                    result = method.Invoke(instance, convertedArgs);
                }
                catch (TargetInvocationException tie)
                {
                    exception = tie.InnerException ?? tie;
                }
                catch (StackOverflowException)
                {
                    exception = new InvalidOperationException("Stack overflow detected - likely infinite recursion");
                }
                catch (OutOfMemoryException)
                {
                    exception = new InvalidOperationException("Out of memory - likely excessive allocation");
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
            }, cts.Token);

            try
            {
                if (!task.Wait(_timeoutMs))
                {
                    stopwatch.Stop();
                    return new ExecutionResult
                    {
                        Success = false,
                        Error = "Execution timed out",
                        TimedOut = true,
                        DurationMs = stopwatch.Elapsed.TotalMilliseconds
                    };
                }
            }
            catch (AggregateException ae)
            {
                stopwatch.Stop();
                var innerEx = ae.InnerExceptions.FirstOrDefault() ?? ae;
                return new ExecutionResult
                {
                    Success = false,
                    Error = $"Execution failed: {innerEx.Message}",
                    ExceptionType = innerEx.GetType().Name,
                    DurationMs = stopwatch.Elapsed.TotalMilliseconds
                };
            }

            stopwatch.Stop();

            if (exception != null)
            {
                // Check for contract violation - use actual type check and message patterns
                var isContractViolation =
                    exception is ContractViolationException ||
                    exception.GetType().Name.Contains("ContractViolation") ||
                    exception.Message.Contains("Precondition failed") ||
                    exception.Message.Contains("Postcondition failed") ||
                    exception.Message.Contains("Contract") ||
                    exception.Message.Contains("Precondition") ||
                    exception.Message.Contains("Postcondition");

                // Extract meaningful error details
                var errorMessage = exception.Message;
                if (exception.InnerException != null && exception.Message != exception.InnerException.Message)
                {
                    errorMessage += $" -> {exception.InnerException.Message}";
                }

                // For safety benchmark: Analyze the exception quality
                // Determine language based on exception type (ContractViolationException = calor)
                var language = exception is ContractViolationException ? "calor" : "csharp";
                var safetyAnalysis = SafetyScorer.AnalyzeException(exception, language);

                return new ExecutionResult
                {
                    Success = false,
                    Error = errorMessage,
                    ExceptionType = exception.GetType().Name,
                    ContractViolation = isContractViolation,
                    DurationMs = stopwatch.Elapsed.TotalMilliseconds,
                    Exception = exception,
                    SafetyAnalysis = safetyAnalysis
                };
            }

            return new ExecutionResult
            {
                Success = true,
                ReturnValue = result,
                DurationMs = stopwatch.Elapsed.TotalMilliseconds
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new ExecutionResult
            {
                Success = false,
                Error = $"Execution failed: {ex.Message}",
                ExceptionType = ex.GetType().Name,
                DurationMs = stopwatch.Elapsed.TotalMilliseconds
            };
        }
    }

    /// <summary>
    /// Compiles and executes Calor code.
    /// </summary>
    public ExecutionResult CompileAndExecuteCalor(
        string calorSource,
        string methodName,
        object?[]? arguments = null)
    {
        var calorResult = CompileCalor(calorSource);
        if (!calorResult.Success)
        {
            return new ExecutionResult
            {
                Success = false,
                Error = $"Calor compilation failed: {string.Join("; ", calorResult.Errors)}",
                CompilationFailed = true
            };
        }

        var csharpResult = CompileCSharp(calorResult.GeneratedCSharp!);
        if (!csharpResult.Success)
        {
            return new ExecutionResult
            {
                Success = false,
                Error = $"C# compilation failed: {string.Join("; ", csharpResult.Errors)}",
                CompilationFailed = true
            };
        }

        return Execute(csharpResult.AssemblyBytes!, methodName, arguments);
    }

    /// <summary>
    /// Compiles and executes C# code.
    /// </summary>
    public ExecutionResult CompileAndExecuteCSharp(
        string csharpSource,
        string methodName,
        object?[]? arguments = null)
    {
        var result = CompileCSharp(csharpSource);
        if (!result.Success)
        {
            return new ExecutionResult
            {
                Success = false,
                Error = $"Compilation failed: {string.Join("; ", result.Errors)}",
                CompilationFailed = true
            };
        }

        return Execute(result.AssemblyBytes!, methodName, arguments);
    }

    private static object?[]? ConvertArguments(MethodInfo method, object?[]? arguments)
    {
        if (arguments == null || arguments.Length == 0)
            return arguments;

        var parameters = method.GetParameters();
        if (parameters.Length != arguments.Length)
            return arguments; // Let it fail naturally if counts don't match

        var converted = new object?[arguments.Length];
        for (int i = 0; i < arguments.Length; i++)
        {
            var arg = arguments[i];
            var paramType = parameters[i].ParameterType;

            if (arg == null)
            {
                converted[i] = null;
            }
            else if (paramType.IsAssignableFrom(arg.GetType()))
            {
                converted[i] = arg;
            }
            else
            {
                // Try to convert numeric types
                try
                {
                    converted[i] = Convert.ChangeType(arg, paramType);
                }
                catch
                {
                    converted[i] = arg; // Let it fail naturally
                }
            }
        }
        return converted;
    }

    private static (Type?, MethodInfo?) FindMethod(Assembly assembly, string methodName, string? typeName)
    {
        var allTypes = GetAllTypes(assembly);

        // If typeName is specified, filter to that type
        var types = typeName != null
            ? allTypes.Where(t => t.FullName == typeName || t.Name == typeName)
            : allTypes;

        // Prioritize common module/class names that LLM-generated code uses
        var prioritizedTypes = types
            .OrderByDescending(t => t.Name.Equals("Math", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(t => t.Name.Equals("Module", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(t => t.Name.Equals("MathModule", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(t => t.Name.Equals("Program", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(t => t.Name.EndsWith("Module", StringComparison.OrdinalIgnoreCase))
            .ThenBy(t => t.Name);

        foreach (var type in prioritizedTypes)
        {
            if (type == null) continue;

            var bindingFlags = BindingFlags.Public | BindingFlags.NonPublic |
                               BindingFlags.Static | BindingFlags.Instance;

            // Try exact match first
            var method = type.GetMethod(methodName, bindingFlags);
            if (method != null)
                return (type, method);

            // Try case-insensitive match
            method = type.GetMethods(bindingFlags)
                .FirstOrDefault(m => m.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase));
            if (method != null)
                return (type, method);

            // Try matching with common prefixes (Get, Is, Calculate, Compute)
            var prefixes = new[] { "Get", "Is", "Calculate", "Compute", "Do" };
            foreach (var prefix in prefixes)
            {
                method = type.GetMethods(bindingFlags)
                    .FirstOrDefault(m => m.Name.Equals($"{prefix}{methodName}", StringComparison.OrdinalIgnoreCase));
                if (method != null)
                    return (type, method);
            }
        }

        return (null, null);
    }

    private static IEnumerable<Type> GetAllTypes(Assembly assembly)
    {
        try
        {
            // Get all types including nested types
            var types = assembly.GetTypes();
            var allTypes = new List<Type>();

            foreach (var type in types)
            {
                allTypes.Add(type);
                allTypes.AddRange(GetNestedTypes(type));
            }

            return allTypes;
        }
        catch (ReflectionTypeLoadException ex)
        {
            // Return whatever types we could load
            return ex.Types.Where(t => t != null).Cast<Type>();
        }
    }

    private static IEnumerable<Type> GetNestedTypes(Type type)
    {
        var nested = new List<Type>();
        try
        {
            foreach (var nestedType in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                nested.Add(nestedType);
                nested.AddRange(GetNestedTypes(nestedType));
            }
        }
        catch { /* Ignore reflection errors */ }
        return nested;
    }

    private static string GetAvailableMethods(Assembly assembly)
    {
        var methods = new List<string>();
        foreach (var type in GetAllTypes(assembly))
        {
            var typeMethods = type.GetMethods(
                BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName)
                .Select(m => $"{type.Name}.{m.Name}");
            methods.AddRange(typeMethods);
        }
        return methods.Any() ? string.Join(", ", methods.Take(10)) : "none found";
    }

    /// <summary>
    /// Ensures common using directives are present in C# source code.
    /// LLM-generated code often omits these, causing compilation failures.
    /// </summary>
    private static string EnsureCommonUsings(string csharpSource)
    {
        var usingsToAdd = new List<string>();

        // Check for common usings and add if missing
        if (!csharpSource.Contains("using System;") && !csharpSource.Contains("using System\n"))
        {
            usingsToAdd.Add("using System;");
        }

        if (!csharpSource.Contains("using System.Collections.Generic;"))
        {
            usingsToAdd.Add("using System.Collections.Generic;");
        }

        if (!csharpSource.Contains("using System.Linq;"))
        {
            usingsToAdd.Add("using System.Linq;");
        }

        if (usingsToAdd.Count == 0)
        {
            return csharpSource;
        }

        // Prepend the missing usings
        return string.Join("\n", usingsToAdd) + "\n\n" + csharpSource;
    }

    private static IEnumerable<MetadataReference> GetRequiredReferences()
    {
        var assemblyPath = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.Collections.dll")),
            MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.Linq.dll")),
            MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "netstandard.dll"))
        };

        // Add Calor.Runtime reference for contract enforcement
        var calorRuntimeAssembly = typeof(Calor.Runtime.ContractViolationException).Assembly;
        references.Add(MetadataReference.CreateFromFile(calorRuntimeAssembly.Location));

        return references;
    }

    public void Dispose()
    {
        foreach (var context in _loadContexts)
        {
            try { context.Unload(); } catch { }
        }
        _loadContexts.Clear();
    }
}

/// <summary>
/// Result of compiling Calor source code.
/// </summary>
public record CalorCompileResult
{
    public bool Success { get; init; }
    public string? GeneratedCSharp { get; init; }
    public List<string> Errors { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
}

/// <summary>
/// Result of compiling C# source code.
/// </summary>
public record CSharpCompileResult
{
    public bool Success { get; init; }
    public byte[]? AssemblyBytes { get; init; }
    public List<string> Errors { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
}

/// <summary>
/// Result of executing compiled code.
/// </summary>
public record ExecutionResult
{
    public bool Success { get; init; }
    public object? ReturnValue { get; init; }
    public string? Error { get; init; }
    public string? ExceptionType { get; init; }
    public bool TimedOut { get; init; }
    public bool CompilationFailed { get; init; }
    public bool ContractViolation { get; init; }
    public double DurationMs { get; init; }

    /// <summary>
    /// The actual exception object (for detailed analysis in safety benchmarks).
    /// </summary>
    public Exception? Exception { get; init; }

    /// <summary>
    /// Safety exception analysis (populated for safety benchmarks).
    /// </summary>
    public SafetyExceptionAnalysis? SafetyAnalysis { get; init; }

    /// <summary>
    /// Gets the return value as a specific type.
    /// </summary>
    public T? GetReturnValue<T>() =>
        ReturnValue is T value ? value : default;

    /// <summary>
    /// Gets the return value as JSON for comparison.
    /// </summary>
    public string ToJson() =>
        ReturnValue == null ? "null" : JsonSerializer.Serialize(ReturnValue);
}
