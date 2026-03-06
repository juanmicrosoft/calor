using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests verifying that explicit generic type arguments on method calls
/// round-trip correctly through the C# → Calor AST → Calor text → C# pipeline.
/// </summary>
public class GenericCallTests
{
    private readonly CSharpToCalorConverter _converter = new();

    #region C# → Calor Conversion (AST level)

    [Fact]
    public void Convert_InstanceGenericCall_ExtractsTypeArguments()
    {
        var csharp = """
            using System.Collections.Generic;
            using System.Linq;
            public class Svc
            {
                public IEnumerable<int> GetInts(IEnumerable<object> items)
                {
                    return items.Cast<int>();
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        // Find a CallExpressionNode with TypeArguments
        var callNode = FindCallNode(result.Ast!, "Cast");
        Assert.NotNull(callNode);
        Assert.NotNull(callNode!.TypeArguments);
        Assert.Single(callNode.TypeArguments);
        Assert.Equal("i32", callNode.TypeArguments[0]); // int → i32 in Calor
        Assert.DoesNotContain("<", callNode.Target); // Target should be clean
    }

    [Fact]
    public void Convert_StaticGenericCall_ExtractsTypeArguments()
    {
        var csharp = """
            using System;
            public class Svc
            {
                public string[] GetEmpty()
                {
                    return Array.Empty<string>();
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var callNode = FindCallNode(result.Ast!, "Empty");
        Assert.NotNull(callNode);
        Assert.NotNull(callNode!.TypeArguments);
        Assert.Single(callNode.TypeArguments);
        Assert.Equal("str", callNode.TypeArguments[0]); // string → str in Calor
        Assert.DoesNotContain("<", callNode.Target);
    }

    [Fact]
    public void Convert_MethodWithoutTypeArgs_HasNullTypeArguments()
    {
        var csharp = """
            using System.Collections.Generic;
            using System.Linq;
            public class Svc
            {
                public int Sum(IEnumerable<int> items)
                {
                    return items.Count();
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var callNode = FindCallNode(result.Ast!, "Count");
        Assert.NotNull(callNode);
        Assert.Null(callNode!.TypeArguments);
    }

    [Fact]
    public void Convert_MultipleTypeArgs_ExtractsAll()
    {
        var csharp = """
            using System.Collections.Generic;
            using System.Linq;
            public class Svc
            {
                public Dictionary<string, int> Convert(IEnumerable<KeyValuePair<string, int>> items)
                {
                    return items.ToDictionary<KeyValuePair<string, int>, string, int>(
                        kvp => kvp.Key, kvp => kvp.Value);
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var callNode = FindCallNode(result.Ast!, "ToDictionary");
        Assert.NotNull(callNode);
        Assert.NotNull(callNode!.TypeArguments);
        Assert.Equal(3, callNode.TypeArguments.Count);
        Assert.DoesNotContain("<", callNode.Target);
    }

    #endregion

    #region CSharpEmitter (AST → C#)

    [Fact]
    public void CSharpEmitter_EmitsTypeArguments()
    {
        var node = new CallExpressionNode(
            default,
            "items.Cast",
            Array.Empty<ExpressionNode>(),
            null,
            null,
            new[] { "i32" });

        var emitter = new CSharpEmitter();
        var result = node.Accept(emitter);

        Assert.Equal("items.Cast<int>()", result);
    }

    [Fact]
    public void CSharpEmitter_NoTypeArguments_EmitsCleanCall()
    {
        var node = new CallExpressionNode(
            default,
            "items.Count",
            Array.Empty<ExpressionNode>());

        var emitter = new CSharpEmitter();
        var result = node.Accept(emitter);

        Assert.Equal("items.Count()", result);
    }

    [Fact]
    public void CSharpEmitter_MultipleTypeArguments()
    {
        var node = new CallExpressionNode(
            default,
            "items.ToDictionary",
            Array.Empty<ExpressionNode>(),
            null,
            null,
            new[] { "str", "i32" });

        var emitter = new CSharpEmitter();
        var result = node.Accept(emitter);

        Assert.Equal("items.ToDictionary<string, int>()", result);
    }

    #endregion

    #region CalorEmitter (AST → Calor text)

    [Fact]
    public void CalorEmitter_EmitsTypeArgumentsInTarget()
    {
        var node = new CallExpressionNode(
            default,
            "items.Cast",
            Array.Empty<ExpressionNode>(),
            null,
            null,
            new[] { "i32" });

        var emitter = new CalorEmitter();
        var result = node.Accept(emitter);

        Assert.Contains("items.Cast<i32>", result);
        Assert.Contains("§C{", result);
    }

    #endregion

    #region Full Round-Trip: C# → Calor AST → Calor text → Parse → C#

    [Fact]
    public void RoundTrip_InstanceGenericCall_PreservesTypeArgs()
    {
        var csharp = """
            using System.Collections.Generic;
            using System.Linq;
            public class Svc
            {
                public IEnumerable<int> GetInts(IEnumerable<object> items)
                {
                    return items.Cast<int>();
                }
            }
            """;

        var generatedCSharp = RoundTrip(csharp);
        Assert.Contains("Cast<int>", generatedCSharp);
    }

    [Fact]
    public void RoundTrip_StaticGenericCall_PreservesTypeArgs()
    {
        var csharp = """
            using System;
            public class Svc
            {
                public string[] GetEmpty()
                {
                    return Array.Empty<string>();
                }
            }
            """;

        var generatedCSharp = RoundTrip(csharp);
        Assert.Contains("Empty<string>", generatedCSharp);
    }

    [Fact]
    public void RoundTrip_NonGenericCall_NoTypeArgs()
    {
        var csharp = """
            using System.Collections.Generic;
            using System.Linq;
            public class Svc
            {
                public int GetCount(IEnumerable<int> items)
                {
                    return items.Count();
                }
            }
            """;

        var generatedCSharp = RoundTrip(csharp);
        // Should NOT have spurious type args
        Assert.DoesNotContain("Count<", generatedCSharp);
        Assert.Contains("Count()", generatedCSharp);
    }

    [Fact]
    public void RoundTrip_MultipleTypeArgs_PreservesAll()
    {
        var csharp = """
            using System.Collections.Generic;
            using System.Linq;
            public class Svc
            {
                public Dictionary<string, int> Convert(IEnumerable<KeyValuePair<string, int>> items)
                {
                    return items.ToDictionary<KeyValuePair<string, int>, string, int>(
                        kvp => kvp.Key, kvp => kvp.Value);
                }
            }
            """;

        var generatedCSharp = RoundTrip(csharp);
        Assert.Contains("ToDictionary<", generatedCSharp);
        Assert.Contains("string, int>", generatedCSharp);
    }

    #endregion

    #region Helpers

    private string RoundTrip(string csharp)
    {
        // C# → Calor AST
        var convertResult = _converter.Convert(csharp);
        Assert.True(convertResult.Success, GetErrorMessage(convertResult));

        // Calor AST → Calor text
        var calrEmitter = new CalorEmitter();
        var calrText = calrEmitter.Emit(convertResult.Ast!);

        // Calor text → Parse → Compile → C#
        var compileResult = Program.Compile(calrText, "round-trip.calr", new CompilationOptions
        {
            EnforceEffects = false
        });

        Assert.False(compileResult.HasErrors, FormatDiagnostics(compileResult));
        return compileResult.GeneratedCode;
    }

    private static CallExpressionNode? FindCallNode(ModuleNode module, string methodNameContains)
    {
        var visitor = new CallNodeFinder(methodNameContains);
        foreach (var cls in module.Classes)
        {
            foreach (var method in cls.Methods)
            {
                foreach (var stmt in method.Body)
                {
                    var found = FindCallInStatement(stmt, methodNameContains);
                    if (found != null) return found;
                }
            }
        }
        return null;
    }

    private static CallExpressionNode? FindCallInStatement(StatementNode stmt, string methodName)
    {
        if (stmt is ReturnStatementNode ret && ret.Expression != null)
            return FindCallInExpression(ret.Expression, methodName);
        if (stmt is BindStatementNode bind && bind.Initializer != null)
            return FindCallInExpression(bind.Initializer, methodName);
        if (stmt is ExpressionStatementNode exprStmt)
            return FindCallInExpression(exprStmt.Expression, methodName);
        return null;
    }

    private static CallExpressionNode? FindCallInExpression(ExpressionNode expr, string methodName)
    {
        if (expr is CallExpressionNode call)
        {
            if (call.Target.Contains(methodName))
                return call;
            // Search in arguments
            foreach (var arg in call.Arguments)
            {
                var found = FindCallInExpression(arg, methodName);
                if (found != null) return found;
            }
        }
        return null;
    }

    private static string GetErrorMessage(ConversionResult result)
    {
        if (result.Issues.Count > 0)
            return string.Join("\n", result.Issues.Select(i => i.Message));
        return "Conversion failed with no specific error message";
    }

    private static string FormatDiagnostics(CompilationResult result)
    {
        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"[{d.Code}] {d.Message}");
        return string.Join("\n", errors);
    }

    /// <summary>Simple visitor to find CallExpressionNode by target name.</summary>
    private class CallNodeFinder
    {
        private readonly string _methodName;
        public CallNodeFinder(string methodName) => _methodName = methodName;
    }

    #endregion
}
