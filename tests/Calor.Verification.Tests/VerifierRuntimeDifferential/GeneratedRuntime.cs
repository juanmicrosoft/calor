using System.Reflection;
using System.Runtime.Loader;
using Calor.Compiler.CodeGen;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Calor.Verification.Tests.VerifierRuntimeDifferential;

internal sealed class GeneratedRuntime
{
    private readonly Assembly _assembly;
    private readonly Type _moduleType;

    private GeneratedRuntime(Assembly assembly, Type moduleType)
    {
        _assembly = assembly;
        _moduleType = moduleType;
    }

    public static GeneratedRuntime Compile(string assemblyName, string generatedCode, string moduleName)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var trees = new[]
        {
            CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble, parseOptions),
            CSharpSyntaxTree.ParseText(generatedCode, parseOptions)
        };
        var compilation = CSharpCompilation.Create(
            assemblyName,
            trees,
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        if (!emit.Success)
        {
            var errors = string.Join(
                Environment.NewLine,
                emit.Diagnostics
                    .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                    .Select(diagnostic => diagnostic.ToString()));
            throw new InvalidOperationException(
                $"Generated differential C# did not compile:{Environment.NewLine}{errors}");
        }

        stream.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(stream);
        var moduleType = assembly.GetType($"{moduleName}.{moduleName}Module")
            ?? throw new InvalidOperationException(
                $"Generated module type '{moduleName}.{moduleName}Module' was not found.");
        return new GeneratedRuntime(assembly, moduleType);
    }

    public RuntimeVerdict Invoke(string methodName, IReadOnlyList<string> parameterTypes, out string? detail)
    {
        var method = _moduleType.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Generated method '{methodName}' was not found.");
        var arguments = parameterTypes.Select(CreateArgument).ToArray();

        try
        {
            method.Invoke(null, arguments);
            detail = null;
            return RuntimeVerdict.Completed;
        }
        catch (TargetInvocationException invocation) when (invocation.InnerException != null)
        {
            var inner = invocation.InnerException;
            detail = $"{inner.GetType().FullName}: {inner.Message}";
            if (inner is Calor.Runtime.ContractViolationException)
                return RuntimeVerdict.GuardFailed;
            if (inner is InvalidOperationException
                && inner.Message.StartsWith("Proof obligation", StringComparison.Ordinal))
            {
                return RuntimeVerdict.GuardFailed;
            }
            return RuntimeVerdict.RuntimeError;
        }
    }

    private object? CreateArgument(string typeName)
    {
        return typeName switch
        {
            "i8" => (sbyte)7,
            "i16" => (short)7,
            "i32" => 7,
            "i64" => 7L,
            "u8" => (byte)7,
            "u16" => (ushort)7,
            "u32" => 7U,
            "u64" => 7UL,
            "bool" => true,
            "str" => "ascii",
            "Probe" => CreateProbe(),
            _ when typeName.EndsWith("[]", StringComparison.Ordinal) =>
                CreateArray(typeName[..^2]),
            _ => throw new InvalidOperationException(
                $"No deterministic runtime witness is registered for parameter type '{typeName}'.")
        };
    }

    private object CreateProbe()
    {
        var type = _assembly.GetType("VerifierDifferential.Probe")
            ?? throw new InvalidOperationException("Generated Probe type was not found.");
        var value = Activator.CreateInstance(type)
            ?? throw new InvalidOperationException("Generated Probe instance could not be created.");
        type.GetField("Value", BindingFlags.Public | BindingFlags.Instance)?.SetValue(value, 7);
        return value;
    }

    private static Array CreateArray(string elementTypeName)
    {
        var elementType = elementTypeName switch
        {
            "i8" => typeof(sbyte),
            "i16" => typeof(short),
            "i32" => typeof(int),
            "i64" => typeof(long),
            "u8" => typeof(byte),
            "u16" => typeof(ushort),
            "u32" => typeof(uint),
            "u64" => typeof(ulong),
            _ => throw new InvalidOperationException(
                $"No deterministic array witness is registered for '{elementTypeName}[]'.")
        };
        var array = Array.CreateInstance(elementType, 1);
        array.SetValue(Convert.ChangeType(7, elementType, System.Globalization.CultureInfo.InvariantCulture), 0);
        return array;
    }
}

internal static class GeneratedMethodInspector
{
    public static IReadOnlyDictionary<string, string> ExtractMethods(string generatedCode)
    {
        var root = CSharpSyntaxTree.ParseText(
                generatedCode,
                new CSharpParseOptions(LanguageVersion.Preview))
            .GetRoot();
        return root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .ToDictionary(
                method => method.Identifier.ValueText,
                method => method.ToFullString(),
                StringComparer.Ordinal);
    }

    public static bool HasGuard(string methodText, ContractPosition position)
    {
        return position switch
        {
            ContractPosition.Precondition =>
                methodText.Contains("ContractKind.Requires", StringComparison.Ordinal),
            ContractPosition.Postcondition =>
                methodText.Contains("ContractKind.Ensures", StringComparison.Ordinal),
            ContractPosition.Obligation =>
                methodText.Contains("throw new InvalidOperationException", StringComparison.Ordinal)
                && methodText.Contains("Proof obligation", StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(position))
        };
    }
}
