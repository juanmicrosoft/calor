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
            module AsyncTests AsyncTests block
              func FetchDataAsync async (string:url) → string
                !
                return "data"
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
            module SyncTests SyncTests block
              func GetData (string:url) → string
                !
                return "data"
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
            module AsyncMethodTests AsyncMethodTests block
              class Service
                func ProcessAsync async (int:id) → string
                  !
                  return "done"
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
            module SyncMethodTests SyncMethodTests block
              class Service
                func Process (int:id) → string
                  !
                  return "done"
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
            module AsyncEmitTests AsyncEmitTests block
              func GetDataAsync async () → string
                !
                return "data"
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
            module AsyncMethodEmitTests AsyncMethodEmitTests block
              class Service
                func ProcessAsync async () → int
                  !
                  return 42
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
            module AsyncVoidTests AsyncVoidTests block
              func DoWorkAsync async ()
                !
                return
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
            module SyncEmitTests SyncEmitTests block
              func GetData () → string
                !
                return "data"
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

        Assert.Contains("func GetDataAsync async", calor);
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

        Assert.Contains("func GetData (", calor);
        Assert.DoesNotContain("§/MT{", calor);
        Assert.DoesNotContain("func GetData async", calor);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Emit_AsyncMethodWithTaskOutput_DoesNotDoubleWrap()
    {
        // If someone explicitly declares Task<T> as output, we shouldn't wrap it again
        var calorSource = """
            module NoDoubleWrap NoDoubleWrap block
              class Service
                func GetTaskAsync async () → Task<int>
                  !
                  return 42
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
            module MixedMethods MixedMethods block
              class Service
                func GetSync () → string
                  !
                  return "sync"

                func GetAsync async () → string
                  !
                  return "async"
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
            module ModifierTests ModifierTests block
              class Service
                func ProcessAsync virtual async () → string
                  !
                  return "done"
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

    #endregion

    #region Edge Cases - ConfigureAwait

    [Fact]
    public void Emit_AwaitWithConfigureAwaitFalse_EmitsConfigureAwaitCall()
    {
        var calorSource = """
            module ConfigureAwaitEmit ConfigureAwaitEmit block
              func FetchAsync async () → string
                !
                let string:result = await (client.GetStringAsync("url")).ConfigureAwait(false)
                return result
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
            module NoConfigureAwait NoConfigureAwait block
              func FetchAsync async () → string
                !
                let string:result = await client.GetStringAsync("url")
                return result
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
            module NestedGenericTests NestedGenericTests block
              class Service
                func GetListAsync async () → List<int>
                  ! alloc
                  return new List<int>()
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
            module ContractTests ContractTests block
              func GetUserAsync async (int:userId | (# > 0)) → string
                !
                return "user"
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAllForParser(), diagnostics);
        var ast = parser.Parse();

        Assert.Empty(diagnostics.Errors);
        var func = Assert.Single(ast.Functions);
        Assert.True(func.IsAsync);
        Assert.Single(Calor.Compiler.Verification.ContractVerifier.EffectivePreconditions(func.Parameters, func.Preconditions));
    }

    [Fact]
    public void Parse_AsyncFunctionWithPostcondition_PreservesBoth()
    {
        var calorSource = """
            module ContractTests ContractTests block
              func ComputeAsync async (int:x) → int
                !
                => result >= 0
                return 42
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
            module ContractEmitTests ContractEmitTests block
              func ValidateAsync async (int:value | (# >= 0)) → bool
                !
                return true
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
            module MethodContractTests MethodContractTests block
              class Service
                func ProcessAsync async (string:input | (# != null)) → string
                  !
                  return "processed"
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var parser = new Parser(lexer.TokenizeAllForParser(), diagnostics);
        var ast = parser.Parse();

        Assert.Empty(diagnostics.Errors);
        var classNode = Assert.Single(ast.Classes);
        var method = Assert.Single(classNode.Methods);
        Assert.True(method.IsAsync);
        Assert.Single(Calor.Compiler.Verification.ContractVerifier.EffectivePreconditions(method.Parameters, method.Preconditions));
    }

    #endregion

    #region Edge Cases - Type Parameters

    [Fact]
    public void Parse_AsyncGenericFunction_PreservesTypeParameters()
    {
        // Correct syntax: type params come after the brace
        var calorSource = """
            module GenericAsyncTests GenericAsyncTests block
              func GetAsync<T> async (T:item) → T
                !
                return item
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
            module GenericAsyncEmitTests GenericAsyncEmitTests block
              func GetAsync<T> async (T:item) → T
                !
                return item
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
                public async Task<TKey> GetByIdAsync<TKey>(TKey id)
                {
                    return id;
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
            module StaticAsyncTests StaticAsyncTests block
              class Helper
                func ComputeAsync static async (int:x) → int
                  !
                  return 42
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
            module StaticAsyncEmitTests StaticAsyncEmitTests block
              class Helper
                func ComputeAsync static async (int:x) → int
                  !
                  return 42
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
            module MultipleAwaitsTests MultipleAwaitsTests block
              func ChainAsync async () → string
                !
                let string:first = await GetFirstAsync()
                let string:second = await GetSecondAsync()
                return second
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
            module MultipleAwaitsEmit MultipleAwaitsEmit block
              func ChainAsync async () → string
                !
                let string:first = await GetFirstAsync()
                let string:second = await (GetSecondAsync()).ConfigureAwait(false)
                return second
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
    [InlineData("int", "Task<int>", true)]
    [InlineData("int", "Task<int>", false)]
    [InlineData("Task<int>", "Task<int>", true)]
    [InlineData("ValueTask<int>", "ValueTask<int>", true)]
    [InlineData("System.Threading.Tasks.Task<int>", "Task<int>", true)]
    [InlineData("System.Threading.Tasks.ValueTask<int>", "ValueTask<int>", false)]
    public async Task Compile_AsyncCallableAndAwait_UseDifferentResultTypes(
        string output, string bindingType, bool typed)
    {
        var binding = typed ? $"{bindingType}:pending" : "pending";
        var assembly = CompileAndLoad($$"""
            module AsyncNative AsyncNative block
              func Produce async () → {{output}}
                !
                return 42

              func Consume async () → int
                !
                let {{binding}} = Produce()
                let int:value = await pending
                return value
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
            module AsyncVoidNative AsyncVoidNative block
              func Produce async () → {{output}}
                !
                return

              func Consume async () → void
                !
                await Produce()
            """);
        var module = assembly.GetType("AsyncVoidNative.AsyncVoidNativeModule", throwOnError: true)!;
        Assert.Equal(output == "ValueTask" ? typeof(ValueTask) : typeof(Task),
            module.GetMethod("Produce")!.ReturnType);
        await (Task)module.GetMethod("Consume")!.Invoke(null, null)!;
    }

    [Theory]
    [InlineData("let Task<int>:pending = 42")]
    [InlineData("let string:value = await Produce()")]
    [InlineData("let int:value = await 42")]
    [InlineData("let int:value = Produce()")]
    [InlineData("return \"wrong\"")]
    public void Compile_AsyncWrongResult_IsRejected(string statement)
    {
        var result = Program.Compile($$"""
            module AsyncRejected AsyncRejected block
              func Produce async () → int
                !
                return 42

              func Consume async () → int
                !
                {{statement}}
                return 42
            """, "async-rejected.calr", new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public async Task Compile_GenericAsyncMethodGroup_PreservesTaskResult()
    {
        var assembly = CompileAndLoad("""
            module AsyncGeneric AsyncGeneric block
              func Identity<T> async (T:value) → T
                !
                return value

              func Consume async () → int
                !
                let Func<int, Task<int>>:factory = Identity
                let Task<int>:pending = factory(42)
                return await pending
            """);
        var module = assembly.GetType("AsyncGeneric.AsyncGenericModule", throwOnError: true)!;
        Assert.Equal(typeof(Task<int>), module.GetMethod("Identity")!.MakeGenericMethod(typeof(int)).ReturnType);
        Assert.Equal(42, await (Task<int>)module.GetMethod("Consume")!.Invoke(null, null)!);
    }

    [Fact]
    public async Task Compile_ClassAndInheritedAsyncCalls_PreserveTaskResult()
    {
        var assembly = CompileAndLoad("""
            module AsyncClasses AsyncClasses block
              class Base
                func Produce async () → int
                  !
                  return 42

              class Derived
                extends Base

                func Consume async () → int
                  !
                  let Task<int>:pending = Produce()
                  return await pending
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
            module AsyncCustom AsyncCustom block
              class {{name}}<T>

              func Produce async ({{name}}<int>:value) → {{name}}<int>
                !
                => (result == value)
                return value

              func Consume async ({{name}}<int>:value) → {{name}}<int>
                !
                let global::System.Threading.Tasks.Task<{{name}}<int>>:pending = Produce(value)
                return await pending
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
            module AsyncSyncCarrier AsyncSyncCarrier block
              func Pass (Task<int>:pending) → Task<int>
                !
                return pending

              func Consume async (Task<int>:pending) → int
                !
                let Task<int>:copy = Pass(pending)
                return await copy
            """);
        var module = assembly.GetType("AsyncSyncCarrier.AsyncSyncCarrierModule", throwOnError: true)!;
        var value = Task.FromResult(42);
        Assert.Same(value, module.GetMethod("Pass")!.Invoke(null, [value]));
        Assert.Equal(42, await (Task<int>)module.GetMethod("Consume")!.Invoke(null, [value])!);
    }

    [Theory]
    [InlineData("using Pending = System.Threading.Tasks.Task<int>", "Pending")]
    [InlineData("using Tasks = System.Threading.Tasks", "Tasks.Task<int>")]
    public async Task Compile_AsyncAlias_PreservesCarrier(string import, string output)
    {
        var assembly = CompileAndLoad($$"""
            module AsyncAlias AsyncAlias block
              {{import}}
              func Produce async () → {{output}}
                !
                return 42

              func Consume async () → int
                !
                let Task<int>:pending = Produce()
                return await pending
            """);
        var module = assembly.GetType("AsyncAlias.AsyncAliasModule", throwOnError: true)!;
        Assert.Equal(typeof(Task<int>), module.GetMethod("Produce")!.ReturnType);
        Assert.Equal(42, await (Task<int>)module.GetMethod("Consume")!.Invoke(null, null)!);
    }

    [Theory]
    [InlineData("Task<int>")]
    [InlineData("ValueTask<int>")]
    public async Task Compile_AsyncLambda_UsesDelegateBodyResult(string carrier)
    {
        var assembly = CompileAndLoad($$"""
            module AsyncLambda AsyncLambda block
              func Consume async () → int
                !
                let Func<{{carrier}}>:factory = async () => 42
                return await factory()
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
            module AsyncVoidRejected AsyncVoidRejected block
              func Consume async ({{carrier}}:pending) → int
                !
                let int:value = await pending
                return value
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
            module AsyncCustomRejected AsyncCustomRejected block
              class {{name}}<T>

              func Consume async ({{name}}<int>:pending) → int
                !
                return await pending
            """, "async-custom-rejected.calr", new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Theory]
    [InlineData("int")]
    [InlineData("Task<int>")]
    [InlineData("ValueTask<int>")]
    public async Task Compile_AsyncPostcondition_DescribesBodyResult(string output)
    {
        var assembly = CompileAndLoad($$"""
            module AsyncContracts AsyncContracts block
              func Produce async () → {{output}}
                !
                => (result > 0)
                return 42

              func Consume async () → int
                !
                return await Produce()
            """);
        var module = assembly.GetType("AsyncContracts.AsyncContractsModule", throwOnError: true)!;
        Assert.Equal(42, await (Task<int>)module.GetMethod("Consume")!.Invoke(null, null)!);
    }

    [Fact]
    public async Task Compile_TypeParameterNamedTask_IsNotBclTask()
    {
        var assembly = CompileAndLoad("""
            module AsyncParameter AsyncParameter block
              func Identity<Task> async (Task:value) → Task
                !
                return value
            """);
        var method = assembly.GetType("AsyncParameter.AsyncParameterModule", throwOnError: true)!
            .GetMethod("Identity")!.MakeGenericMethod(typeof(int));
        Assert.Equal(typeof(Task<int>), method.ReturnType);
        Assert.Equal(42, await (Task<int>)method.Invoke(null, [42])!);
    }

    [Theory]
    [InlineData("Func<int>")]
    [InlineData("Func<Task<string>>")]
    public void Compile_AsyncLambdaWrongTargetOrBody_IsRejected(string target)
    {
        var result = Program.Compile($$"""
            module AsyncLambdaRejected AsyncLambdaRejected block
              func Probe () → void
                !
                let {{target}}:factory = async () => 42
            """, "async-lambda-rejected.calr", new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void Compile_BoundNativeAsyncResult_AgreesWithCallableAndBodyTypes()
    {
        var result = Program.Compile("""
            module AsyncBound AsyncBound block
              func Produce async () → int
                !
                return 42

              func Consume async () → int
                !
                let pending = Produce()
                return await pending
            """, "async-bound.calr", new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        var diagnostics = new DiagnosticBag();
        var bound = new Binding.Binder(diagnostics).Bind(result.Ast!);
        Assert.Empty(diagnostics.Errors);
        var producer = bound.Functions.Single(function => function.Symbol.Name == "Produce");
        Assert.Equal("global::System.Threading.Tasks.Task<int>", producer.Symbol.ReturnType);
        Assert.Equal("int", producer.Symbol.BodyReturnType);
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
            module AsyncBaseCarrier AsyncBaseCarrier block
              func Produce async () → int
                !
                return 42

              func Consume async () → void
                !
                let Task:pending = Produce()
                await pending
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
            module AsyncLambdaBound AsyncLambdaBound block
              func Probe () → void
                !
                let Func<{{carrier}}<int>>:factory = async () => 42
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
    [InlineData("Task")]
    [InlineData("ValueTask")]
    public async Task Compile_ClassCarrierTypeParameter_IsAValueReturn(string parameter)
    {
        var assembly = CompileAndLoad($$"""
            module AsyncClassParameter AsyncClassParameter block
              class Box<{{parameter}}>
                func Produce async ({{parameter}}:value) → {{parameter}}
                  !
                  => true
                  return value
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
            module AsyncNestedClass AsyncNestedClass block
              class Outer<{{parameter}}>
                class Inner
                  func Produce async ({{parameter}}:value) → {{parameter}}
                    !
                    => true
                    return value
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
            module AsyncContractContext AsyncContractContext block
              class Outer<{{parameter}}>
                class Inner<T>
                  func Produce<U> async ({{parameter}}:value) → {{parameter}}
                    !
                    => true
                    return value
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
    [InlineData("int")]
    [InlineData("global::System.Threading.Tasks.Task<int>")]
    [InlineData("global::System.Threading.Tasks.ValueTask<int>")]
    public void Compile_AsyncMethodContract_VerifiesTheBodyResult(string output)
    {
        var options = new CompilationOptions
        {
            StatusWriter = TextWriter.Null,
            VerifyContracts = true,
            VerificationCacheOptions = Verification.Z3.Cache.VerificationCacheOptions.Disabled
        };
        var compilation = Program.Compile($$"""
            module AsyncMethodProof AsyncMethodProof block
              class Box<Task>
                func Produce async () → {{output}}
                  !
                  => (result == 42)
                  return 42
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
