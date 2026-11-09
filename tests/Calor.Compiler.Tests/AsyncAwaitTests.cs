using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for async/await support including async functions, async methods, and await expressions.
/// </summary>
public class AsyncAwaitTests
{
    private readonly CSharpToCalorConverter _converter = new(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });

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
    public void Convert_AsyncValueTaskMethod_PreservesCarrier()
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
        Assert.Equal("ValueTask<i32>", method.Output.TypeName);
    }

    #endregion

    #region Calor Parsing - Async Function

    [Fact]
    public void Parse_AsyncFunction_SetsIsAsyncFlag()
    {
        var calorSource = """
            §M{AsyncTests}
                §AF{FetchDataAsync:pub}
                    §I{str:url}
                    §O{str}
                    §R "data"
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAllForParser(), diagnostics);
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
            §M{SyncTests}
                §F{GetData:pub}
                    §I{str:url}
                    §O{str}
                    §R "data"
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAllForParser(), diagnostics);
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
            §M{AsyncMethodTests}
                §CL{Service:pub}
                    §AMT{ProcessAsync:pub}
                      §I{i32:id}
                      §O{str}
                      §R "done"
                    §/AMT
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAllForParser(), diagnostics);
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
            §M{SyncMethodTests}
                §CL{Service:pub}
                    §MT{Process:pub}
                        §I{i32:id}
                        §O{str}
                        §R "done"
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAllForParser(), diagnostics);
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
            §M{AsyncEmitTests}
                §AF{GetDataAsync:pub}
                    §O{str}
                    §R "data"
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAllForParser(), diagnostics);
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
            §M{AsyncMethodEmitTests}
                §CL{Service:pub}
                    §AMT{ProcessAsync:pub}
                      §O{i32}
                      §R 42
                    §/AMT
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAllForParser(), diagnostics);
        var ast = parser.Parse();

        var emitter = new CSharpEmitter();
        var csharp = emitter.Emit(ast);

        Assert.Contains("public async Task<int> ProcessAsync", csharp);
    }

    [Fact]
    public void Emit_AsyncVoidFunction_EmitsTaskReturnType()
    {
        var calorSource = """
            §M{AsyncVoidTests}
                §AF{DoWorkAsync:pub}
                    §R
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAllForParser(), diagnostics);
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
            §M{SyncEmitTests}
                §F{GetData:pub}
                    §O{str}
                    §R "data"
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAllForParser(), diagnostics);
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
        Assert.DoesNotContain("§/AMT{", calor);
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
        Assert.DoesNotContain("§/MT{", calor);
        Assert.DoesNotContain("§AMT{", calor);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Emit_AsyncMethodWithTaskOutput_DoesNotDoubleWrap()
    {
        // If someone explicitly declares Task<T> as output, we shouldn't wrap it again
        var calorSource = """
            §M{NoDoubleWrap}
                §CL{Service:pub}
                    §AMT{GetTaskAsync:pub}
                      §O{Task<i32>}
                      §R 42
                    §/AMT
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAllForParser(), diagnostics);
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
            §M{MixedMethods}
                §CL{Service:pub}
                    §MT{GetSync:pub}
                        §O{str}
                        §R "sync"
                    §AMT{GetAsync:pub}
                      §O{str}
                      §R "async"
                    §/AMT
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAllForParser(), diagnostics);
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
            §M{ModifierTests}
                §CL{Service:pub}
                    §AMT{ProcessAsync:pub:virt}
                      §O{str}
                      §R "done"
                    §/AMT
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAllForParser(), diagnostics);
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
        var source = "§AF{Test:pub}";
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();

        Assert.Contains(tokens, t => t.Kind == TokenKind.AsyncFunc);
    }

    [Fact]
    public void Lexer_EndAsyncFuncToken_Recognized()
    {
        var source = "§/AF";
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();

        Assert.Contains(tokens, t => t.Kind == TokenKind.EndAsyncFunc);
    }

    [Fact]
    public void Lexer_AsyncMethodToken_Recognized()
    {
        var source = "§AMT{Test:pub}";
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();

        Assert.Contains(tokens, t => t.Kind == TokenKind.AsyncMethod);
    }

    [Fact]
    public void Lexer_EndAsyncMethodToken_Recognized()
    {
        var source = "§/AMT";
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();

        Assert.Contains(tokens, t => t.Kind == TokenKind.EndAsyncMethod);
    }

    #endregion

    #region Edge Cases - ConfigureAwait

    [Fact]
    public void Parse_AwaitWithConfigureAwaitFalse_SetsConfigureAwaitProperty()
    {
        // Test using §B (Bind) statement with await expression
        var calorSource = """
            §M{ConfigureAwaitTests}
                §AF{FetchAsync:pub}
                    §O{str}
                    §B{str:result} §AWAIT{false} §C{client.GetStringAsync} §A "url" §/C
                    §R result
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAllForParser(), diagnostics);
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
            §M{ConfigureAwaitTests}
                §AF{FetchAsync:pub}
                    §O{str}
                    §B{str:result} §AWAIT{true} §C{client.GetStringAsync} §A "url" §/C
                    §R result
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAllForParser(), diagnostics);
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
            §M{ConfigureAwaitEmit}
                §AF{FetchAsync:pub}
                    §O{str}
                    §B{str:result} §AWAIT{false} §C{client.GetStringAsync} §A "url" §/C
                    §R result
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAllForParser(), diagnostics);
        var ast = parser.Parse();

        var emitter = new CSharpEmitter();
        var csharp = emitter.Emit(ast);

        Assert.Contains(".ConfigureAwait(false)", csharp);
    }

    [Fact]
    public void Parse_AwaitWithoutConfigureAwait_HasNullConfigureAwait()
    {
        var calorSource = """
            §M{NoConfigureAwait}
                §AF{FetchAsync:pub}
                    §O{str}
                    §B{str:result} §AWAIT §C{client.GetStringAsync} §A "url" §/C
                    §R result
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAllForParser(), diagnostics);
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
            §M{NestedGenericTests}
                §CL{Service:pub}
                    §AMT{GetListAsync:pub}
                      §O{List<i32>}
                      §R §C{new List<int>}§/C
                    §/AMT
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAllForParser(), diagnostics);
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
            §M{ContractTests}
                §AF{GetUserAsync:pub}
                    §I{i32:userId}
                    §O{str}
                    §Q{"userId must be positive"} (> userId 0)
                    §R "user"
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAllForParser(), diagnostics);
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
            §M{ContractTests}
                §AF{ComputeAsync:pub}
                    §I{i32:x}
                    §O{i32}
                    §S{"result is non-negative"} (>= result 0)
                    §R 42
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAllForParser(), diagnostics);
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
            §M{ContractEmitTests}
                §AF{ValidateAsync:pub}
                    §I{i32:value}
                    §O{bool}
                    §Q{"value must be non-negative"} (>= value 0)
                    §R true
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAllForParser(), diagnostics);
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
            §M{MethodContractTests}
                §CL{Service:pub}
                    §AMT{ProcessAsync:pub}
                      §I{str:input}
                      §O{str}
                      §Q{"input must not be null"} (!= input null)
                      §R "processed"
                    §/AMT
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAllForParser(), diagnostics);
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
            §M{GenericAsyncTests}
                §AF{GetAsync:pub}<T>
                    §I{T:item}
                    §O{T}
                    §R item
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAllForParser(), diagnostics);
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
            §M{GenericAsyncEmitTests}
                §AF{GetAsync:pub}<T>
                    §I{T:item}
                    §O{T}
                    §R item
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAllForParser(), diagnostics);
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
            §M{StaticAsyncTests}
                §CL{Helper:pub}
                    §AMT{ComputeAsync:pub:stat}
                      §I{i32:x}
                      §O{i32}
                      §R 42
                    §/AMT
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAllForParser(), diagnostics);
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
            §M{StaticAsyncEmitTests}
                §CL{Helper:pub}
                    §AMT{ComputeAsync:pub:stat}
                      §I{i32:x}
                      §O{i32}
                      §R 42
                    §/AMT
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAllForParser(), diagnostics);
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
            §M{MultipleAwaitsTests}
                §AF{ChainAsync:pub}
                    §O{str}
                    §B{str:first} §AWAIT §C{GetFirstAsync}§/C
                    §B{str:second} §AWAIT §C{GetSecondAsync}§/C
                    §R second
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAllForParser(), diagnostics);
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
            §M{MultipleAwaitsEmit}
                §AF{ChainAsync:pub}
                    §O{str}
                    §B{str:first} §AWAIT §C{GetFirstAsync}§/C
                    §B{str:second} §AWAIT{false} §C{GetSecondAsync}§/C
                    §R second
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAllForParser(), diagnostics);
        var ast = parser.Parse();

        var emitter = new CSharpEmitter();
        var csharp = emitter.Emit(ast);

        // Count await occurrences
        var awaitCount = System.Text.RegularExpressions.Regex.Matches(csharp, @"\bawait\b").Count;
        Assert.Equal(2, awaitCount);
        Assert.Contains("ConfigureAwait(false)", csharp);
    }

    #endregion

    [Theory]
    [InlineData("i32", "Task<i32>", true)]
    [InlineData("i32", "Task<i32>", false)]
    [InlineData("Task<i32>", "Task<i32>", true)]
    [InlineData("ValueTask<i32>", "ValueTask<i32>", true)]
    [InlineData("System.Threading.Tasks.Task<i32>", "Task<i32>", true)]
    [InlineData("System.Threading.Tasks.ValueTask<i32>", "ValueTask<i32>", false)]
    public async Task Compile_AsyncCallableAndAwait_UseDifferentResultTypes(
        string output, string bindingType, bool typed)
    {
        var binding = typed ? $"pending:{bindingType}" : "pending";
        var assembly = CompileAndLoad($$"""
            §M{AsyncNative}
              §AF{Produce:pub} () -> {{output}}
                §R 42
              §AF{Consume:pub} () -> i32
                §B{ {{binding}} } §C{Produce} §/C
                §B{value:i32} §AWAIT pending
                §R value
            """);
        var module = assembly.GetType("AsyncNative.AsyncNativeModule", throwOnError: true)!;
        Assert.Equal(bindingType.StartsWith("ValueTask", StringComparison.Ordinal)
            ? typeof(ValueTask<int>) : typeof(Task<int>), module.GetMethod("Produce")!.ReturnType);
        Assert.Equal(42, await (Task<int>)module.GetMethod("Consume")!.Invoke(null, null)!);
    }

    [Theory]
    [InlineData("Task")]
    [InlineData("ValueTask")]
    [InlineData("void")]
    public async Task Compile_AsyncVoidBody_UsesVoidAwaitResult(string output)
    {
        var assembly = CompileAndLoad($$"""
            §M{AsyncVoidNative}
              §AF{Produce:pub} () -> {{output}}
                §R
              §AF{Consume:pub} () -> void
                §AWAIT §C{Produce} §/C
            """);
        var module = assembly.GetType("AsyncVoidNative.AsyncVoidNativeModule", throwOnError: true)!;
        Assert.Equal(output == "ValueTask" ? typeof(ValueTask) : typeof(Task),
            module.GetMethod("Produce")!.ReturnType);
        await (Task)module.GetMethod("Consume")!.Invoke(null, null)!;
    }

    [Theory]
    [InlineData("§B{pending:Task<i32>} 42")]
    [InlineData("§B{value:str} §AWAIT §C{Produce} §/C")]
    [InlineData("§B{value:i32} §AWAIT 42")]
    [InlineData("§B{value:i32} §C{Produce} §/C")]
    [InlineData("§R \"wrong\"")]
    public void Compile_AsyncWrongResult_IsRejected(string statement)
    {
        var result = Program.Compile($$"""
            §M{AsyncRejected}
              §AF{Produce:pub} () -> i32
                §R 42
              §AF{Consume:pub} () -> i32
                {{statement}}
                §R 42
            """, "async-rejected.calr", new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public async Task Compile_GenericAsyncMethodGroup_PreservesTaskResult()
    {
        var assembly = CompileAndLoad("""
            §M{AsyncGeneric}
              §AF{Identity:pub}<T> (T:value) -> T
                §R value
              §AF{Consume:pub} () -> i32
                §B{factory:Func<i32,Task<i32>>} Identity
                §B{pending:Task<i32>} §C{factory} §A 42 §/C
                §R §AWAIT pending
            """);
        var module = assembly.GetType("AsyncGeneric.AsyncGenericModule", throwOnError: true)!;
        Assert.Equal(typeof(Task<int>), module.GetMethod("Identity")!.MakeGenericMethod(typeof(int)).ReturnType);
        Assert.Equal(42, await (Task<int>)module.GetMethod("Consume")!.Invoke(null, null)!);
    }

    [Fact]
    public async Task Compile_ClassAndInheritedAsyncCalls_PreserveTaskResult()
    {
        var assembly = CompileAndLoad("""
            §M{AsyncClasses}
              §CL{Base:pub}
                §AMT{Produce:pub} () -> i32
                  §R 42
              §CL{Derived:pub}
                §EXT{Base}
                §AMT{Consume:pub} () -> i32
                  §B{pending:Task<i32>} §C{Produce} §/C
                  §R §AWAIT pending
            """);
        var derived = assembly.GetType("AsyncClasses.Derived", throwOnError: true)!;
        Assert.Equal(42, await (Task<int>)derived.GetMethod("Consume")!.Invoke(Activator.CreateInstance(derived), null)!);
    }

    [Theory]
    [InlineData("Task<int>")]
    [InlineData("ValueTask<int>")]
    [InlineData("Task")]
    [InlineData("ValueTask")]
    public async Task Convert_AsyncPublicSignature_IsPreserved(string returnType)
    {
        var returnsValue = returnType.EndsWith('>');
        var source = $$"""
            using System.Threading.Tasks;
            public class AsyncMigration
            {
                public async {{returnType}} Produce()
                {
                    await Task.Yield();
                    {{(returnsValue ? "return 42;" : "")}}
                }
            }
            """;
        var conversion = _converter.Convert(source);
        Assert.True(conversion.Success, GetErrorMessage(conversion));
        var assembly = CompileAndLoad(conversion.CalorSource!);
        var type = Assert.Single(assembly.GetTypes(), candidate => candidate.Name == "AsyncMigration");
        var expected = returnType switch
        {
            "Task<int>" => typeof(Task<int>),
            "ValueTask<int>" => typeof(ValueTask<int>),
            "Task" => typeof(Task),
            _ => typeof(ValueTask)
        };
        Assert.Equal(expected, type.GetMethod("Produce")!.ReturnType);
        var pending = type.GetMethod("Produce")!.Invoke(Activator.CreateInstance(type), null);
        switch (pending)
        {
            case Task<int> task:
                Assert.Equal(42, await task);
                break;
            case ValueTask<int> valueTask:
                Assert.Equal(42, await valueTask);
                break;
            case Task task:
                await task;
                break;
            case ValueTask valueTask:
                await valueTask;
                break;
            default:
                Assert.Fail("Converted async method did not return its declared carrier.");
                break;
        }
    }

    private static Assembly CompileAndLoad(string source)
    {
        var result = Program.Compile(source, "async-native.calr", new CompilationOptions
        {
            EnableTypeChecking = true,
            ContractMode = ContractMode.Debug,
            StatusWriter = TextWriter.Null
        });
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        var compilation = CSharpCompilation.Create("Async_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble + result.GeneratedCode)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }

    [Theory]
    [InlineData("MyTask")]
    [InlineData("Task")]
    public async Task Compile_CustomTaskNames_AreBodyValuesNotBclCarriers(string name)
    {
        var assembly = CompileAndLoad($$"""
            §M{AsyncCustom}
              §CL{ {{name}}:pub}<T>
              §AF{Produce:pub} ({{name}}<i32>:value) -> {{name}}<i32>
                §S (== result value)
                §R value
              §AF{Consume:pub} ({{name}}<i32>:value) -> {{name}}<i32>
                §B{pending:global::System.Threading.Tasks.Task<{{name}}<i32>>} §C{Produce} §A value §/C
                §R §AWAIT pending
            """);
        var module = assembly.GetType("AsyncCustom.AsyncCustomModule", throwOnError: true)!;
        var custom = assembly.GetType($"AsyncCustom.{name}`1", throwOnError: true)!.MakeGenericType(typeof(int));
        var value = Activator.CreateInstance(custom);
        Assert.Equal(typeof(Task<>).MakeGenericType(custom), module.GetMethod("Produce")!.ReturnType);
        var pending = (Task)module.GetMethod("Consume")!.Invoke(null, [value])!;
        await pending;
        Assert.Same(value, pending.GetType().GetProperty("Result")!.GetValue(pending));
    }

    [Fact]
    public async Task Compile_SynchronousTaskReturn_IsNotUnwrapped()
    {
        var assembly = CompileAndLoad("""
            §M{AsyncSyncCarrier}
              §F{Pass:pub} (Task<i32>:pending) -> Task<i32>
                §R pending
              §AF{Consume:pub} (Task<i32>:pending) -> i32
                §B{copy:Task<i32>} §C{Pass} §A pending §/C
                §R §AWAIT copy
            """);
        var module = assembly.GetType("AsyncSyncCarrier.AsyncSyncCarrierModule", throwOnError: true)!;
        var value = Task.FromResult(42);
        Assert.Same(value, module.GetMethod("Pass")!.Invoke(null, [value]));
        Assert.Equal(42, await (Task<int>)module.GetMethod("Consume")!.Invoke(null, [value])!);
    }

    [Theory]
    [InlineData("§U{Pending:System.Threading.Tasks.Task<i32>}", "Pending")]
    [InlineData("§U{Tasks:System.Threading.Tasks}", "Tasks.Task<i32>")]
    public async Task Compile_AsyncAlias_PreservesCarrier(string import, string output)
    {
        var assembly = CompileAndLoad($$"""
            §M{AsyncAlias}
              {{import}}
              §AF{Produce:pub} () -> {{output}}
                §R 42
              §AF{Consume:pub} () -> i32
                §B{pending:Task<i32>} §C{Produce} §/C
                §R §AWAIT pending
            """);
        var module = assembly.GetType("AsyncAlias.AsyncAliasModule", throwOnError: true)!;
        Assert.Equal(typeof(Task<int>), module.GetMethod("Produce")!.ReturnType);
        Assert.Equal(42, await (Task<int>)module.GetMethod("Consume")!.Invoke(null, null)!);
    }

    [Theory]
    [InlineData("Task<i32>")]
    [InlineData("ValueTask<i32>")]
    public async Task Compile_AsyncLambda_UsesDelegateBodyResult(string carrier)
    {
        var assembly = CompileAndLoad($$"""
            §M{AsyncLambda}
              §AF{Consume:pub} () -> i32
                §B{factory:Func<{{carrier}}>} §LAM{async} 42 §/LAM
                §R §AWAIT §C{factory} §/C
            """);
        var module = assembly.GetType("AsyncLambda.AsyncLambdaModule", throwOnError: true)!;
        Assert.Equal(42, await (Task<int>)module.GetMethod("Consume")!.Invoke(null, null)!);
    }

    [Theory]
    [InlineData("Task")]
    [InlineData("ValueTask")]
    public void Compile_VoidAwaitCannotInitializeValue(string carrier)
    {
        var result = Program.Compile($$"""
            §M{AsyncVoidRejected}
              §AF{Consume:pub} ({{carrier}}:pending) -> i32
                §B{value:i32} §AWAIT pending
                §R value
            """, "async-void-rejected.calr", new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Theory]
    [InlineData("Task")]
    [InlineData("MyTask")]
    public void Compile_CustomTaskCannotBeAwaitedAsBclCarrier(string name)
    {
        var result = Program.Compile($$"""
            §M{AsyncCustomRejected}
              §CL{ {{name}}:pub}<T>
              §AF{Consume:pub} ({{name}}<i32>:pending) -> i32
                §R §AWAIT pending
            """, "async-custom-rejected.calr", new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Theory]
    [InlineData("i32")]
    [InlineData("Task<i32>")]
    [InlineData("ValueTask<i32>")]
    public async Task Compile_AsyncPostcondition_DescribesBodyResult(string output)
    {
        var assembly = CompileAndLoad($$"""
            §M{AsyncContracts}
              §AF{Produce:pub} () -> {{output}}
                §S (> result 0)
                §R 42
              §AF{Consume:pub} () -> i32
                §R §AWAIT §C{Produce} §/C
            """);
        var module = assembly.GetType("AsyncContracts.AsyncContractsModule", throwOnError: true)!;
        Assert.Equal(42, await (Task<int>)module.GetMethod("Consume")!.Invoke(null, null)!);
    }

    [Fact]
    public async Task Compile_TypeParameterNamedTask_IsNotBclTask()
    {
        var assembly = CompileAndLoad("""
            §M{AsyncParameter}
              §AF{Identity:pub}<Task> (Task:value) -> Task
                §R value
            """);
        var method = assembly.GetType("AsyncParameter.AsyncParameterModule", throwOnError: true)!
            .GetMethod("Identity")!.MakeGenericMethod(typeof(int));
        Assert.Equal(typeof(Task<int>), method.ReturnType);
        Assert.Equal(42, await (Task<int>)method.Invoke(null, [42])!);
    }

    [Theory]
    [InlineData("Func<i32>")]
    [InlineData("Func<Task<str>>")]
    public void Compile_AsyncLambdaWrongTargetOrBody_IsRejected(string target)
    {
        var result = Program.Compile($$"""
            §M{AsyncLambdaRejected}
              §F{Probe:pub} () -> void
                §B{factory:{{target}}} §LAM{async} 42 §/LAM
            """, "async-lambda-rejected.calr", new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void Compile_BoundNativeAsyncResult_AgreesWithCallableAndBodyTypes()
    {
        var result = Program.Compile("""
            §M{AsyncBound}
              §AF{Produce:pub} () -> i32
                §R 42
              §AF{Consume:pub} () -> i32
                §B{pending} §C{Produce} §/C
                §R §AWAIT pending
            """, "async-bound.calr", new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        var diagnostics = new DiagnosticBag();
        var bound = new Binding.Binder(diagnostics).Bind(result.Ast!);
        Assert.Empty(diagnostics.Errors);
        var producer = bound.Functions.Single(function => function.Symbol.Name == "Produce");
        Assert.Equal("global::System.Threading.Tasks.Task<i32>", producer.Symbol.ReturnType);
        Assert.Equal("i32", producer.Symbol.BodyReturnType);
        var consumer = bound.Functions.Single(function => function.Symbol.Name == "Consume");
        var pending = Assert.IsType<Binding.BoundBindStatement>(consumer.Body[0]);
        Assert.Contains("Task<", pending.Initializer!.Type.DisplayString);
        var returned = Assert.IsType<Binding.BoundReturnStatement>(consumer.Body[1]);
        Assert.Equal("INT", Binding.TypeIdentity.Canonicalize(returned.Expression!.Type.DisplayString));
        Assert.False(returned.Expression.HasUnknownInferredType);
    }

    [Fact]
    public async Task Compile_TaskOfValueCanInitializeNonGenericTask()
    {
        var assembly = CompileAndLoad("""
            §M{AsyncBaseCarrier}
              §AF{Produce:pub} () -> i32
                §R 42
              §AF{Consume:pub} () -> void
                §B{pending:Task} §C{Produce} §/C
                §AWAIT pending
            """);
        var module = assembly.GetType("AsyncBaseCarrier.AsyncBaseCarrierModule", throwOnError: true)!;
        await (Task)module.GetMethod("Consume")!.Invoke(null, null)!;
    }

    [Fact]
    public void Convert_AsyncVoid_IsPreservedRatherThanChangedToTask()
    {
        var conversion = new CSharpToCalorConverter().Convert("""
            using System.Threading.Tasks;
            public class AsyncVoidMigration
            {
                public async void Produce() { await Task.Yield(); }
            }
            """);
        Assert.True(conversion.Success, GetErrorMessage(conversion));
        var assembly = CompileAndLoad(conversion.CalorSource!);
        var type = Assert.Single(assembly.GetTypes(), candidate => candidate.Name == "AsyncVoidMigration");
        Assert.Equal(typeof(void), type.GetMethod("Produce")!.ReturnType);
        Assert.Contains(conversion.Issues, issue => issue.Message.Contains("async-return-signature", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Task")]
    [InlineData("ValueTask")]
    public void Compile_BoundAsyncLambda_PreservesContextualCarrier(string carrier)
    {
        var result = Program.Compile($$"""
            §M{AsyncLambdaBound}
              §F{Probe:pub} () -> void
                §B{factory:Func<{{carrier}}<i32>>} §LAM{async} 42 §/LAM
            """, "async-lambda-bound.calr", new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        var diagnostics = new DiagnosticBag();
        var bound = new Binding.Binder(diagnostics).Bind(result.Ast!);
        Assert.Empty(diagnostics.Errors);
        var initializer = Assert.IsType<Binding.BoundLambdaExpression>(
            Assert.IsType<Binding.BoundBindStatement>(Assert.Single(bound.Functions).Body[0]).Initializer);
        Assert.Equal(Binding.TypeIdentity.Canonicalize($"{carrier}<i32>"),
            Binding.TypeIdentity.Canonicalize(initializer.ReturnTypeName));
    }

    [Theory]
    [InlineData("Task", false)]
    [InlineData("Task", true)]
    [InlineData("ValueTask", false)]
    [InlineData("ValueTask", true)]
    public async Task Compile_ClassCarrierTypeParameter_IsAValueReturn(string parameter, bool hasRefinement)
    {
        var assembly = CompileAndLoad($$"""
            §M{AsyncClassParameter}
              {{(hasRefinement ? "§RTYPE{Positive:i32} (> # 0)" : "")}}
              §CL{Box:pub}<{{parameter}}>
                §AMT{Produce:pub} ({{parameter}}:value) -> {{parameter}}
                  §S true
                  §R value
            """);
        var type = assembly.GetType("AsyncClassParameter.Box`1", throwOnError: true)!.MakeGenericType(typeof(int));
        var method = type.GetMethod("Produce")!;
        Assert.Equal(typeof(Task<int>), method.ReturnType);
        Assert.Equal(42, await (Task<int>)method.Invoke(Activator.CreateInstance(type), [42])!);
    }

    [Theory]
    [InlineData("Task")]
    [InlineData("ValueTask")]
    public async Task Compile_OuterClassCarrierTypeParameter_IsAValueReturn(string parameter)
    {
        var assembly = CompileAndLoad($$"""
            §M{AsyncNestedClass}
              §CL{Outer:pub}<{{parameter}}>
                §CL{Inner:pub}
                  §AMT{Produce:pub} ({{parameter}}:value) -> {{parameter}}
                    §S true
                    §R value
            """);
        var type = assembly.GetType("AsyncNestedClass.Outer`1+Inner", throwOnError: true)!.MakeGenericType(typeof(int));
        var method = type.GetMethod("Produce")!;
        Assert.Equal(typeof(Task<int>), method.ReturnType);
        Assert.Equal(42, await (Task<int>)method.Invoke(Activator.CreateInstance(type), [42])!);
    }

    [Theory]
    [InlineData("Task")]
    [InlineData("ValueTask")]
    public async Task Convert_LocalAsyncFunction_PreservesClassCarrierTypeParameter(string parameter)
    {
        var conversion = new CSharpToCalorConverter().Convert($$"""
            public class Box<{{parameter}}>
            {
                public System.Threading.Tasks.Task<{{parameter}}> Produce({{parameter}} value)
                {
                    async System.Threading.Tasks.Task<{{parameter}}> Local({{parameter}} item)
                    {
                        await System.Threading.Tasks.Task.Yield();
                        return item;
                    }
                    return Local(value);
                }
            }
            """);
        Assert.True(conversion.Success, GetErrorMessage(conversion));
        var assembly = CompileAndLoad(conversion.CalorSource!);
        var type = Assert.Single(assembly.GetTypes(), candidate => candidate.Name == "Box`1").MakeGenericType(typeof(int));
        var method = type.GetMethod("Produce")!;
        Assert.Equal(typeof(Task<int>), method.ReturnType);
        Assert.Equal(42, await (Task<int>)method.Invoke(Activator.CreateInstance(type), [42])!);
    }

    [Theory]
    [InlineData("Task")]
    [InlineData("ValueTask")]
    public void Compile_ContractAdapter_PreservesAsyncAndEnclosingClassParameters(string parameter)
    {
        var compilation = Program.Compile($$"""
            §M{AsyncContractContext}
              §CL{Outer:pub}<{{parameter}}>
                §CL{Inner:pub}<T>
                  §AMT{Produce:pub}<U> ({{parameter}}:value) -> {{parameter}}
                    §S true
                    §R value
            """, "async-contract-context.calr", new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.NotNull(compilation.Ast);
        var enumerate = typeof(Verification.Z3.ContractVerificationPass).GetMethod("EnumerateContractBearers",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var adapted = Assert.Single(Assert.IsAssignableFrom<IEnumerable<FunctionNode>>(
            enumerate.Invoke(null, [compilation.Ast])));
        Assert.True(adapted.IsAsync);
        Assert.Equal(["U", "T", parameter], adapted.TypeParameters.Select(typeParameter => typeParameter.Name));
        Assert.Equal(parameter, Compiler.Analysis.ReturnShape.GetAsyncReturnTypes(
            adapted.Output!.TypeName, adapted.IsAsync, compilation.Ast,
            adapted.TypeParameters.Select(typeParameter => typeParameter.Name)).BodyType);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics.Errors));
        var diagnostics = new DiagnosticBag();
        var bound = new Binding.Binder(diagnostics).Bind(compilation.Ast);
        Assert.Empty(diagnostics.Errors);
        var method = Assert.Single(bound.Functions);
        Assert.Equal(parameter, method.Symbol.BodyReturnType);
        Assert.Equal($"global::System.Threading.Tasks.Task<{parameter}>", method.Symbol.ReturnType);
    }

    [Theory]
    [InlineData("i32")]
    [InlineData("global::System.Threading.Tasks.Task<i32>")]
    [InlineData("global::System.Threading.Tasks.ValueTask<i32>")]
    public void Compile_AsyncMethodContract_VerifiesTheBodyResult(string output)
    {
        var options = new CompilationOptions
        {
            StatusWriter = TextWriter.Null,
            VerifyContracts = true,
            VerificationCacheOptions = Verification.Z3.Cache.VerificationCacheOptions.Disabled
        };
        var compilation = Program.Compile($$"""
            §M{AsyncMethodProof}
              §CL{Box:pub}<Task>
                §AMT{Produce:pub} () -> {{output}}
                  §S (== result 42)
                  §R 42
            """, "async-method-proof.calr", options);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics.Errors));
        Assert.NotNull(options.VerificationResults);
        var verification = Assert.Single(Assert.Single(options.VerificationResults.Functions).PostconditionResults);
        Assert.Equal(Verification.Z3.ContractVerificationStatus.Proven, verification.Status);
    }

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
