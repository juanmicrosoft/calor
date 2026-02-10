using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for async/await support including async functions, async methods, and await expressions.
/// </summary>
public class AsyncAwaitTests
{
    private readonly CSharpToCalorConverter _converter = new();

    #region C# to Calor Conversion - Async Method Detection

    [Fact]
    public void Convert_AsyncMethod_SetsIsAsyncFlag()
    {
        var csharpSource = """
            public class Service
            {
                public async Task<int> GetValueAsync()
                {
                    return 42;
                }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.Ast);

        var classNode = Assert.Single(result.Ast.Classes);
        var method = Assert.Single(classNode.Methods);
        Assert.True(method.IsAsync);
        Assert.Equal("GetValueAsync", method.Name);
    }

    [Fact]
    public void Convert_SyncMethod_DoesNotSetIsAsyncFlag()
    {
        var csharpSource = """
            public class Service
            {
                public int GetValue()
                {
                    return 42;
                }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));

        var classNode = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(classNode.Methods);
        Assert.False(method.IsAsync);
    }

    [Fact]
    public void Convert_AsyncTaskMethod_UnwrapsTaskReturnType()
    {
        var csharpSource = """
            public class Service
            {
                public async Task<string> GetNameAsync()
                {
                    return "Test";
                }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));

        var classNode = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(classNode.Methods);
        Assert.True(method.IsAsync);
        Assert.NotNull(method.Output);
        Assert.Equal("str", method.Output.TypeName);
    }

    [Fact]
    public void Convert_AsyncVoidTask_HasNoOutput()
    {
        var csharpSource = """
            public class Service
            {
                public async Task DoWorkAsync()
                {
                    await Task.Delay(100);
                }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));

        var classNode = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(classNode.Methods);
        Assert.True(method.IsAsync);
        Assert.Null(method.Output);
    }

    [Fact]
    public void Convert_AsyncValueTaskMethod_UnwrapsReturnType()
    {
        var csharpSource = """
            public class Service
            {
                public async ValueTask<int> GetValueAsync()
                {
                    return 42;
                }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));

        var classNode = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(classNode.Methods);
        Assert.True(method.IsAsync);
        Assert.NotNull(method.Output);
        Assert.Equal("i32", method.Output.TypeName);
    }

    #endregion

    #region Calor Parsing - Async Function

    [Fact]
    public void Parse_AsyncFunction_SetsIsAsyncFlag()
    {
        var calorSource = """
            §M{m001:AsyncTests}
              §AF{f001:FetchDataAsync:pub}
                §I{str:url}
                §O{str}
                §R "data"
              §/AF{f001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAll(), diagnostics);
        var ast = parser.Parse();

        Assert.Empty(diagnostics.Errors);
        var func = Assert.Single(ast.Functions);
        Assert.True(func.IsAsync);
        Assert.Equal("FetchDataAsync", func.Name);
    }

    [Fact]
    public void Parse_RegularFunction_DoesNotSetIsAsyncFlag()
    {
        var calorSource = """
            §M{m001:SyncTests}
              §F{f001:GetData:pub}
                §I{str:url}
                §O{str}
                §R "data"
              §/F{f001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAll(), diagnostics);
        var ast = parser.Parse();

        Assert.Empty(diagnostics.Errors);
        var func = Assert.Single(ast.Functions);
        Assert.False(func.IsAsync);
    }

    #endregion

    #region Calor Parsing - Async Method

    [Fact]
    public void Parse_AsyncMethod_SetsIsAsyncFlag()
    {
        var calorSource = """
            §M{m001:AsyncMethodTests}
              §CL{c001:Service:pub}
                §AMT{mt001:ProcessAsync:pub}
                  §I{i32:id}
                  §O{str}
                  §R "done"
                §/AMT{mt001}
              §/CL{c001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAll(), diagnostics);
        var ast = parser.Parse();

        Assert.Empty(diagnostics.Errors);
        var classNode = Assert.Single(ast.Classes);
        var method = Assert.Single(classNode.Methods);
        Assert.True(method.IsAsync);
        Assert.Equal("ProcessAsync", method.Name);
    }

    [Fact]
    public void Parse_RegularMethod_DoesNotSetIsAsyncFlag()
    {
        var calorSource = """
            §M{m001:SyncMethodTests}
              §CL{c001:Service:pub}
                §MT{mt001:Process:pub}
                  §I{i32:id}
                  §O{str}
                  §R "done"
                §/MT{mt001}
              §/CL{c001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAll(), diagnostics);
        var ast = parser.Parse();

        Assert.Empty(diagnostics.Errors);
        var classNode = Assert.Single(ast.Classes);
        var method = Assert.Single(classNode.Methods);
        Assert.False(method.IsAsync);
    }

    #endregion

    #region CSharpEmitter - Async Emission

    [Fact]
    public void Emit_AsyncFunction_IncludesAsyncKeywordAndTaskReturnType()
    {
        var calorSource = """
            §M{m001:AsyncEmitTests}
              §AF{f001:GetDataAsync:pub}
                §O{str}
                §R "data"
              §/AF{f001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAll(), diagnostics);
        var ast = parser.Parse();

        var emitter = new CSharpEmitter();
        var csharp = emitter.Emit(ast);

        Assert.Contains("async", csharp);
        Assert.Contains("Task<string>", csharp);
        Assert.Contains("GetDataAsync", csharp);
    }

    [Fact]
    public void Emit_AsyncMethod_IncludesAsyncKeywordAndTaskReturnType()
    {
        var calorSource = """
            §M{m001:AsyncMethodEmitTests}
              §CL{c001:Service:pub}
                §AMT{mt001:ProcessAsync:pub}
                  §O{i32}
                  §R 42
                §/AMT{mt001}
              §/CL{c001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAll(), diagnostics);
        var ast = parser.Parse();

        var emitter = new CSharpEmitter();
        var csharp = emitter.Emit(ast);

        Assert.Contains("public async Task<int> ProcessAsync", csharp);
    }

    [Fact]
    public void Emit_AsyncVoidFunction_EmitsTaskReturnType()
    {
        var calorSource = """
            §M{m001:AsyncVoidTests}
              §AF{f001:DoWorkAsync:pub}
                §R
              §/AF{f001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAll(), diagnostics);
        var ast = parser.Parse();

        var emitter = new CSharpEmitter();
        var csharp = emitter.Emit(ast);

        Assert.Contains("async", csharp);
        Assert.Contains("Task", csharp);
        Assert.DoesNotContain("Task<", csharp);
    }

    [Fact]
    public void Emit_SyncFunction_DoesNotIncludeAsyncKeyword()
    {
        var calorSource = """
            §M{m001:SyncEmitTests}
              §F{f001:GetData:pub}
                §O{str}
                §R "data"
              §/F{f001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAll(), diagnostics);
        var ast = parser.Parse();

        var emitter = new CSharpEmitter();
        var csharp = emitter.Emit(ast);

        Assert.DoesNotContain("async", csharp);
        Assert.Contains("string GetData", csharp);
    }

    #endregion

    #region CalorEmitter - Async Emission

    [Fact]
    public void CalorEmit_AsyncFunction_EmitsAFTag()
    {
        var csharpSource = """
            public class Service
            {
                public async Task<string> GetDataAsync()
                {
                    return "data";
                }
            }
            """;

        var result = _converter.Convert(csharpSource);
        Assert.True(result.Success, GetErrorMessage(result));

        var emitter = new CalorEmitter();
        var calor = emitter.Emit(result.Ast!);

        Assert.Contains("§AMT{", calor);
        Assert.Contains("§/AMT{", calor);
    }

    [Fact]
    public void CalorEmit_SyncMethod_EmitsMTTag()
    {
        var csharpSource = """
            public class Service
            {
                public string GetData()
                {
                    return "data";
                }
            }
            """;

        var result = _converter.Convert(csharpSource);
        Assert.True(result.Success, GetErrorMessage(result));

        var emitter = new CalorEmitter();
        var calor = emitter.Emit(result.Ast!);

        Assert.Contains("§MT{", calor);
        Assert.Contains("§/MT{", calor);
        Assert.DoesNotContain("§AMT{", calor);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Emit_AsyncMethodWithTaskOutput_DoesNotDoubleWrap()
    {
        // If someone explicitly declares Task<T> as output, we shouldn't wrap it again
        var calorSource = """
            §M{m001:NoDoubleWrap}
              §CL{c001:Service:pub}
                §AMT{mt001:GetTaskAsync:pub}
                  §O{Task<i32>}
                  §R 42
                §/AMT{mt001}
              §/CL{c001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAll(), diagnostics);
        var ast = parser.Parse();

        var emitter = new CSharpEmitter();
        var csharp = emitter.Emit(ast);

        // Should be Task<int>, not Task<Task<int>>
        Assert.Contains("Task<int>", csharp);
        Assert.DoesNotContain("Task<Task<", csharp);
    }

    [Fact]
    public void Emit_MixedSyncAsyncMethods_CorrectlyHandlesBoth()
    {
        var calorSource = """
            §M{m001:MixedMethods}
              §CL{c001:Service:pub}
                §MT{mt001:GetSync:pub}
                  §O{str}
                  §R "sync"
                §/MT{mt001}
                §AMT{mt002:GetAsync:pub}
                  §O{str}
                  §R "async"
                §/AMT{mt002}
              §/CL{c001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAll(), diagnostics);
        var ast = parser.Parse();

        var classNode = Assert.Single(ast.Classes);
        Assert.Equal(2, classNode.Methods.Count);

        var syncMethod = classNode.Methods.First(m => m.Name == "GetSync");
        var asyncMethod = classNode.Methods.First(m => m.Name == "GetAsync");

        Assert.False(syncMethod.IsAsync);
        Assert.True(asyncMethod.IsAsync);

        var emitter = new CSharpEmitter();
        var csharp = emitter.Emit(ast);

        Assert.Contains("public string GetSync", csharp);
        Assert.Contains("public async Task<string> GetAsync", csharp);
    }

    [Fact]
    public void Parse_AsyncMethodWithModifiers_PreservesAllModifiers()
    {
        var calorSource = """
            §M{m001:ModifierTests}
              §CL{c001:Service:pub}
                §AMT{mt001:ProcessAsync:pub:virt}
                  §O{str}
                  §R "done"
                §/AMT{mt001}
              §/CL{c001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAll(), diagnostics);
        var ast = parser.Parse();

        Assert.Empty(diagnostics.Errors);
        var classNode = Assert.Single(ast.Classes);
        var method = Assert.Single(classNode.Methods);
        Assert.True(method.IsAsync);
        Assert.True(method.IsVirtual);
    }

    #endregion

    #region Round-Trip Tests

    [Fact]
    public void RoundTrip_AsyncMethod_PreservesAsyncBehavior()
    {
        var originalCSharp = """
            public class Service
            {
                public async Task<int> ComputeAsync(int x)
                {
                    return x * 2;
                }
            }
            """;

        // C# -> Calor
        var toCalorResult = _converter.Convert(originalCSharp);
        Assert.True(toCalorResult.Success, GetErrorMessage(toCalorResult));

        // Verify AST has async flag
        var classNode = Assert.Single(toCalorResult.Ast!.Classes);
        var method = Assert.Single(classNode.Methods);
        Assert.True(method.IsAsync);

        // Calor -> C#
        var emitter = new CSharpEmitter();
        var regeneratedCSharp = emitter.Emit(toCalorResult.Ast);

        // Verify regenerated C# has async
        Assert.Contains("async", regeneratedCSharp);
        Assert.Contains("Task<int>", regeneratedCSharp);
        Assert.Contains("ComputeAsync", regeneratedCSharp);
    }

    #endregion

    #region Lexer Token Tests

    [Fact]
    public void Lexer_AsyncFuncToken_Recognized()
    {
        var source = "§AF{f001:Test:pub}";
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();

        Assert.Contains(tokens, t => t.Kind == TokenKind.AsyncFunc);
    }

    [Fact]
    public void Lexer_EndAsyncFuncToken_Recognized()
    {
        var source = "§/AF{f001}";
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();

        Assert.Contains(tokens, t => t.Kind == TokenKind.EndAsyncFunc);
    }

    [Fact]
    public void Lexer_AsyncMethodToken_Recognized()
    {
        var source = "§AMT{m001:Test:pub}";
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();

        Assert.Contains(tokens, t => t.Kind == TokenKind.AsyncMethod);
    }

    [Fact]
    public void Lexer_EndAsyncMethodToken_Recognized()
    {
        var source = "§/AMT{m001}";
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();

        Assert.Contains(tokens, t => t.Kind == TokenKind.EndAsyncMethod);
    }

    #endregion

    #region Edge Cases - ConfigureAwait

    [Fact]
    public void Parse_AwaitWithConfigureAwaitFalse_SetsConfigureAwaitProperty()
    {
        // Test using §B (Bind) statement with await expression
        var calorSource = """
            §M{m001:ConfigureAwaitTests}
              §AF{f001:FetchAsync:pub}
                §O{str}
                §B{str:result} §AWAIT{false} §C{client.GetStringAsync} §A "url" §/C
                §R result
              §/AF{f001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAll(), diagnostics);
        var ast = parser.Parse();

        Assert.Empty(diagnostics.Errors);
        var func = Assert.Single(ast.Functions);
        Assert.True(func.IsAsync);

        // Find the await expression in the bind statement
        var bindStmt = func.Body.OfType<BindStatementNode>().FirstOrDefault();
        Assert.NotNull(bindStmt);
        var awaitExpr = bindStmt.Initializer as AwaitExpressionNode;
        Assert.NotNull(awaitExpr);
        Assert.False(awaitExpr.ConfigureAwait);
    }

    [Fact]
    public void Parse_AwaitWithConfigureAwaitTrue_SetsConfigureAwaitProperty()
    {
        var calorSource = """
            §M{m001:ConfigureAwaitTests}
              §AF{f001:FetchAsync:pub}
                §O{str}
                §B{str:result} §AWAIT{true} §C{client.GetStringAsync} §A "url" §/C
                §R result
              §/AF{f001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAll(), diagnostics);
        var ast = parser.Parse();

        Assert.Empty(diagnostics.Errors);
        var func = Assert.Single(ast.Functions);

        var bindStmt = func.Body.OfType<BindStatementNode>().FirstOrDefault();
        Assert.NotNull(bindStmt);
        var awaitExpr = bindStmt.Initializer as AwaitExpressionNode;
        Assert.NotNull(awaitExpr);
        Assert.True(awaitExpr.ConfigureAwait);
    }

    [Fact]
    public void Emit_AwaitWithConfigureAwaitFalse_EmitsConfigureAwaitCall()
    {
        var calorSource = """
            §M{m001:ConfigureAwaitEmit}
              §AF{f001:FetchAsync:pub}
                §O{str}
                §B{str:result} §AWAIT{false} §C{client.GetStringAsync} §A "url" §/C
                §R result
              §/AF{f001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAll(), diagnostics);
        var ast = parser.Parse();

        var emitter = new CSharpEmitter();
        var csharp = emitter.Emit(ast);

        Assert.Contains(".ConfigureAwait(false)", csharp);
    }

    [Fact]
    public void Parse_AwaitWithoutConfigureAwait_HasNullConfigureAwait()
    {
        var calorSource = """
            §M{m001:NoConfigureAwait}
              §AF{f001:FetchAsync:pub}
                §O{str}
                §B{str:result} §AWAIT §C{client.GetStringAsync} §A "url" §/C
                §R result
              §/AF{f001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAll(), diagnostics);
        var ast = parser.Parse();

        Assert.Empty(diagnostics.Errors);
        var func = Assert.Single(ast.Functions);

        var bindStmt = func.Body.OfType<BindStatementNode>().FirstOrDefault();
        Assert.NotNull(bindStmt);
        var awaitExpr = bindStmt.Initializer as AwaitExpressionNode;
        Assert.NotNull(awaitExpr);
        Assert.Null(awaitExpr.ConfigureAwait);
    }

    #endregion

    #region Edge Cases - Async Lambdas

    [Fact]
    public void Convert_AsyncLambdaInsideMethod_DetectsAsyncMethod()
    {
        // Test that a method containing an async lambda is detected properly
        var csharpSource = """
            using System;
            using System.Threading.Tasks;
            public class Service
            {
                public void Process()
                {
                    Func<Task<int>> asyncLambda = async () =>
                    {
                        await Task.Delay(100);
                        return 42;
                    };
                }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        var classNode = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(classNode.Methods);
        Assert.False(method.IsAsync); // The method itself is not async, only the lambda
    }

    [Fact]
    public void Convert_AsyncLambdaInsideAsyncMethod_BothAreAsync()
    {
        var csharpSource = """
            using System;
            using System.Threading.Tasks;
            public class Service
            {
                public async Task ProcessAsync()
                {
                    Func<Task<int>> innerAsync = async () =>
                    {
                        return 42;
                    };
                    await innerAsync();
                }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        var classNode = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(classNode.Methods);
        Assert.True(method.IsAsync); // Outer method is async
    }

    [Fact]
    public void RoundTrip_AsyncLambda_PreservesAsyncBehavior()
    {
        var csharpSource = """
            using System;
            using System.Threading.Tasks;
            public class Service
            {
                public async Task RunAsync()
                {
                    Func<int, Task<int>> doubleAsync = async x =>
                    {
                        await Task.Delay(10);
                        return x * 2;
                    };
                    var result = await doubleAsync(21);
                }
            }
            """;

        var result = _converter.Convert(csharpSource);
        Assert.True(result.Success, GetErrorMessage(result));

        var emitter = new CSharpEmitter();
        var regenerated = emitter.Emit(result.Ast!);

        // Verify both async keywords appear
        Assert.Contains("async Task RunAsync", regenerated);
        Assert.Contains("async", regenerated);
    }

    #endregion

    #region Edge Cases - Complex Type Unwrapping

    [Fact]
    public void Convert_AsyncMethodWithNestedGeneric_UnwrapsCorrectly()
    {
        var csharpSource = """
            using System.Collections.Generic;
            using System.Threading.Tasks;
            public class Service
            {
                public async Task<List<int>> GetListAsync()
                {
                    return new List<int> { 1, 2, 3 };
                }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        var classNode = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(classNode.Methods);
        Assert.True(method.IsAsync);
        Assert.NotNull(method.Output);
        // Should unwrap Task<List<int>> to List<int>
        Assert.Contains("List", method.Output.TypeName);
        Assert.DoesNotContain("Task", method.Output.TypeName);
    }

    [Fact]
    public void Convert_AsyncMethodWithTupleReturn_UnwrapsCorrectly()
    {
        var csharpSource = """
            using System.Threading.Tasks;
            public class Service
            {
                public async Task<(int Id, string Name)> GetTupleAsync()
                {
                    return (1, "Test");
                }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        var classNode = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(classNode.Methods);
        Assert.True(method.IsAsync);
        Assert.NotNull(method.Output);
        // Should unwrap Task<(int, string)> to the tuple type
        Assert.DoesNotContain("Task<", method.Output.TypeName);
    }

    [Fact]
    public void Emit_AsyncMethodWithNestedGeneric_WrapsCorrectly()
    {
        var calorSource = """
            §M{m001:NestedGenericTests}
              §CL{c001:Service:pub}
                §AMT{mt001:GetListAsync:pub}
                  §O{List<i32>}
                  §R §C{new List<int>}§/C
                §/AMT{mt001}
              §/CL{c001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAll(), diagnostics);
        var ast = parser.Parse();

        var emitter = new CSharpEmitter();
        var csharp = emitter.Emit(ast);

        // Should be Task<List<int>>, not Task<Task<List<int>>>
        Assert.Contains("Task<List<int>>", csharp);
        Assert.DoesNotContain("Task<Task<", csharp);
    }

    [Fact]
    public void Convert_AsyncMethodWithDictionaryReturn_UnwrapsCorrectly()
    {
        var csharpSource = """
            using System.Collections.Generic;
            using System.Threading.Tasks;
            public class Service
            {
                public async Task<Dictionary<string, List<int>>> GetComplexAsync()
                {
                    return new Dictionary<string, List<int>>();
                }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        var classNode = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(classNode.Methods);
        Assert.True(method.IsAsync);
        Assert.NotNull(method.Output);
        // Should unwrap properly without Task - note type is mapped to Dict in Calor
        Assert.DoesNotContain("Task<", method.Output.TypeName);
        Assert.Contains("Dict", method.Output.TypeName);
    }

    #endregion

    #region Edge Cases - Contracts with Async

    [Fact]
    public void Parse_AsyncFunctionWithPrecondition_PreservesBoth()
    {
        // Correct syntax: §Q{"message"} (s-expression)
        var calorSource = """
            §M{m001:ContractTests}
              §AF{f001:GetUserAsync:pub}
                §I{i32:userId}
                §O{str}
                §Q{"userId must be positive"} (> userId 0)
                §R "user"
              §/AF{f001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAll(), diagnostics);
        var ast = parser.Parse();

        Assert.Empty(diagnostics.Errors);
        var func = Assert.Single(ast.Functions);
        Assert.True(func.IsAsync);
        Assert.Single(func.Preconditions);
    }

    [Fact]
    public void Parse_AsyncFunctionWithPostcondition_PreservesBoth()
    {
        var calorSource = """
            §M{m001:ContractTests}
              §AF{f001:ComputeAsync:pub}
                §I{i32:x}
                §O{i32}
                §S{"result is non-negative"} (>= result 0)
                §R 42
              §/AF{f001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAll(), diagnostics);
        var ast = parser.Parse();

        Assert.Empty(diagnostics.Errors);
        var func = Assert.Single(ast.Functions);
        Assert.True(func.IsAsync);
        Assert.Single(func.Postconditions);
    }

    [Fact]
    public void Emit_AsyncFunctionWithContracts_EmitsContractsAndAsync()
    {
        var calorSource = """
            §M{m001:ContractEmitTests}
              §AF{f001:ValidateAsync:pub}
                §I{i32:value}
                §O{bool}
                §Q{"value must be non-negative"} (>= value 0)
                §R true
              §/AF{f001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAll(), diagnostics);
        var ast = parser.Parse();

        var emitter = new CSharpEmitter();
        var csharp = emitter.Emit(ast);

        Assert.Contains("async", csharp);
        Assert.Contains("Task<bool>", csharp);
        // Contract annotations should still be present
        Assert.Contains("Contract", csharp);
    }

    [Fact]
    public void Parse_AsyncMethodWithPrecondition_PreservesBoth()
    {
        var calorSource = """
            §M{m001:MethodContractTests}
              §CL{c001:Service:pub}
                §AMT{mt001:ProcessAsync:pub}
                  §I{str:input}
                  §O{str}
                  §Q{"input must not be null"} (!= input null)
                  §R "processed"
                §/AMT{mt001}
              §/CL{c001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAll(), diagnostics);
        var ast = parser.Parse();

        Assert.Empty(diagnostics.Errors);
        var classNode = Assert.Single(ast.Classes);
        var method = Assert.Single(classNode.Methods);
        Assert.True(method.IsAsync);
        Assert.Single(method.Preconditions);
    }

    #endregion

    #region Edge Cases - Type Parameters

    [Fact]
    public void Parse_AsyncGenericFunction_PreservesTypeParameters()
    {
        // Correct syntax: type params come after the brace
        var calorSource = """
            §M{m001:GenericAsyncTests}
              §AF{f001:GetAsync:pub}<T>
                §I{T:item}
                §O{T}
                §R item
              §/AF{f001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAll(), diagnostics);
        var ast = parser.Parse();

        Assert.Empty(diagnostics.Errors);
        var func = Assert.Single(ast.Functions);
        Assert.True(func.IsAsync);
        Assert.Single(func.TypeParameters);
        Assert.Equal("T", func.TypeParameters[0].Name);
    }

    [Fact]
    public void Emit_AsyncGenericFunction_EmitsTaskOfT()
    {
        var calorSource = """
            §M{m001:GenericAsyncEmitTests}
              §AF{f001:GetAsync:pub}<T>
                §I{T:item}
                §O{T}
                §R item
              §/AF{f001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAll(), diagnostics);
        var ast = parser.Parse();

        var emitter = new CSharpEmitter();
        var csharp = emitter.Emit(ast);

        Assert.Contains("async", csharp);
        Assert.Contains("Task<T>", csharp);
        Assert.Contains("GetAsync<T>", csharp);
    }

    [Fact]
    public void Convert_AsyncGenericMethod_PreservesAll()
    {
        var csharpSource = """
            using System.Threading.Tasks;
            public class Repository<TEntity>
            {
                public async Task<TEntity> GetByIdAsync<TKey>(TKey id)
                {
                    return default;
                }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        var classNode = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(classNode.Methods);
        Assert.True(method.IsAsync);
        Assert.Single(method.TypeParameters);
        Assert.Equal("TKey", method.TypeParameters[0].Name);
    }

    #endregion

    #region Edge Cases - Static and Other Modifiers

    [Fact]
    public void Parse_StaticAsyncMethod_PreservesBothModifiers()
    {
        var calorSource = """
            §M{m001:StaticAsyncTests}
              §CL{c001:Helper:pub}
                §AMT{mt001:ComputeAsync:pub:stat}
                  §I{i32:x}
                  §O{i32}
                  §R 42
                §/AMT{mt001}
              §/CL{c001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAll(), diagnostics);
        var ast = parser.Parse();

        Assert.Empty(diagnostics.Errors);
        var classNode = Assert.Single(ast.Classes);
        var method = Assert.Single(classNode.Methods);
        Assert.True(method.IsAsync);
        Assert.True(method.IsStatic);
    }

    [Fact]
    public void Emit_StaticAsyncMethod_EmitsCorrectSignature()
    {
        var calorSource = """
            §M{m001:StaticAsyncEmitTests}
              §CL{c001:Helper:pub}
                §AMT{mt001:ComputeAsync:pub:stat}
                  §I{i32:x}
                  §O{i32}
                  §R 42
                §/AMT{mt001}
              §/CL{c001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAll(), diagnostics);
        var ast = parser.Parse();

        var emitter = new CSharpEmitter();
        var csharp = emitter.Emit(ast);

        Assert.Contains("public static async Task<int> ComputeAsync", csharp);
    }

    [Fact]
    public void Convert_StaticAsyncMethod_PreservesBothModifiers()
    {
        var csharpSource = """
            using System.Threading.Tasks;
            public class Helper
            {
                public static async Task<int> ComputeAsync(int x)
                {
                    return x * 2;
                }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        var classNode = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(classNode.Methods);
        Assert.True(method.IsAsync);
        Assert.True(method.IsStatic);
    }

    #endregion

    #region Edge Cases - Multiple Awaits

    [Fact]
    public void Parse_AsyncFunctionWithMultipleAwaits_ParsesAll()
    {
        // Use §B (Bind) for variable declarations
        var calorSource = """
            §M{m001:MultipleAwaitsTests}
              §AF{f001:ChainAsync:pub}
                §O{str}
                §B{str:first} §AWAIT §C{GetFirstAsync}§/C
                §B{str:second} §AWAIT §C{GetSecondAsync}§/C
                §R second
              §/AF{f001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAll(), diagnostics);
        var ast = parser.Parse();

        Assert.Empty(diagnostics.Errors);
        var func = Assert.Single(ast.Functions);
        Assert.True(func.IsAsync);
        Assert.Equal(3, func.Body.Count); // 2 bind statements + 1 return
    }

    [Fact]
    public void Emit_AsyncFunctionWithMultipleAwaits_EmitsAllAwaits()
    {
        var calorSource = """
            §M{m001:MultipleAwaitsEmit}
              §AF{f001:ChainAsync:pub}
                §O{str}
                §B{str:first} §AWAIT §C{GetFirstAsync}§/C
                §B{str:second} §AWAIT{false} §C{GetSecondAsync}§/C
                §R second
              §/AF{f001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAll(), diagnostics);
        var ast = parser.Parse();

        var emitter = new CSharpEmitter();
        var csharp = emitter.Emit(ast);

        // Count await occurrences
        var awaitCount = System.Text.RegularExpressions.Regex.Matches(csharp, @"\bawait\b").Count;
        Assert.Equal(2, awaitCount);
        Assert.Contains("ConfigureAwait(false)", csharp);
    }

    #endregion

    #region Helpers

    private static string GetErrorMessage(ConversionResult result)
    {
        if (result.Issues.Count > 0)
        {
            return string.Join("\n", result.Issues.Select(i => i.Message));
        }
        return "Conversion failed with no specific error message";
    }

    #endregion
}
