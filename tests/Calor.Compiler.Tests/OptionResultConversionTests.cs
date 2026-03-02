using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for converting Option&lt;T&gt; and Result&lt;T,E&gt; structs from C# to Calor,
/// verifying per-member fallback, target-typed new inference, and round-trip fidelity.
/// </summary>
public class OptionResultConversionTests
{
    private readonly CSharpToCalorConverter _converter = new();

    private const string OptionCSharp = """
        using System;
        using System.Collections.Generic;

        public readonly struct Option<T>
        {
            private readonly T _value;
            private readonly bool _hasValue;

            private Option(T value)
            {
                _value = value;
                _hasValue = true;
            }

            public static Option<T> Some(T value) => new(value);

            public static Option<T> None => default;

            public bool HasValue => _hasValue;

            public T Value => _hasValue ? _value : throw new InvalidOperationException("Option has no value");

            public T GetValueOrDefault(T defaultValue) => _hasValue ? _value : defaultValue;

            public Option<TResult> Map<TResult>(Func<T, TResult> mapper) =>
                _hasValue ? Option<TResult>.Some(mapper(_value)) : Option<TResult>.None;

            public static implicit operator Option<T>(T value) => Some(value);

            public static bool operator ==(Option<T> left, Option<T> right) =>
                left._hasValue == right._hasValue &&
                (!left._hasValue || EqualityComparer<T>.Default.Equals(left._value, right._value));

            public static bool operator !=(Option<T> left, Option<T> right) => !(left == right);

            public override bool Equals(object? obj) => obj is Option<T> other && this == other;

            public override int GetHashCode() => _hasValue ? _value?.GetHashCode() ?? 0 : 0;

            public override string ToString() => _hasValue ? $"Some({_value})" : "None";
        }
        """;

    private const string ResultCSharp = """
        using System;

        public readonly struct Result<T, E> where T : notnull where E : Exception
        {
            private readonly T _value;
            private readonly E? _error;
            private readonly bool _isOk;

            private Result(T value)
            {
                _value = value;
                _error = default;
                _isOk = true;
            }

            private Result(E error)
            {
                _value = default!;
                _error = error;
                _isOk = false;
            }

            public static Result<T, E> Ok(T value) => new(value);

            public static Result<T, E> Err(E error) => new(error);

            public bool IsOk => _isOk;

            public T Value => _isOk ? _value : throw new InvalidOperationException("Result is not Ok");

            public E Error => !_isOk ? _error! : throw new InvalidOperationException("Result is not Err");

            public T GetValueOrDefault(T defaultValue) => _isOk ? _value : defaultValue;

            public Result<TNew, E> Map<TNew>(Func<T, TNew> mapper) where TNew : notnull =>
                _isOk ? Result<TNew, E>.Ok(mapper(_value)) : Result<TNew, E>.Err(_error!);

            public override string ToString() => _isOk ? $"Ok({_value})" : $"Err({_error})";
        }
        """;

    #region Test 1: Conversion Succeeds

    [Fact]
    public void OptionT_ConversionSucceeds()
    {
        var result = _converter.Convert(OptionCSharp);
        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.Ast);
        Assert.NotNull(result.CalorSource);
    }

    [Fact]
    public void ResultTE_ConversionSucceeds()
    {
        var result = _converter.Convert(ResultCSharp);
        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.Ast);
        Assert.NotNull(result.CalorSource);
    }

    #endregion

    #region Test 2: No Interop Blocks

    [Fact]
    public void OptionT_NoInteropBlocks()
    {
        var result = _converter.Convert(OptionCSharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var calor = result.CalorSource!;
        Assert.DoesNotContain("§CSHARP", calor);
    }

    [Fact]
    public void ResultTE_NoInteropBlocks()
    {
        var result = _converter.Convert(ResultCSharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var calor = result.CalorSource!;
        Assert.DoesNotContain("§CSHARP", calor);
    }

    #endregion

    #region Test 3: Key Features Preserved

    [Fact]
    public void OptionT_PreservesKeyFeatures()
    {
        var result = _converter.Convert(OptionCSharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var cls = Assert.Single(result.Ast!.Classes);

        // Readonly struct
        Assert.True(cls.IsStruct, "Should be struct");
        Assert.True(cls.IsReadOnly, "Should be readonly");

        // Private constructor
        Assert.Contains(cls.Constructors, c => c.Visibility == Visibility.Private);

        // Operators (==, !=, implicit)
        Assert.Contains(cls.OperatorOverloads, o => o.Kind == OperatorOverloadKind.Equality);
        Assert.Contains(cls.OperatorOverloads, o => o.Kind == OperatorOverloadKind.Inequality);
        Assert.Contains(cls.OperatorOverloads, o => o.Kind == OperatorOverloadKind.Implicit);

        // Generic methods (Map<TResult>)
        Assert.Contains(cls.Methods, m => m.Name == "Map" && m.TypeParameters.Count > 0);

        // Properties
        Assert.Contains(cls.Properties, p => p.Name == "HasValue");
        Assert.Contains(cls.Properties, p => p.Name == "Value");
    }

    [Fact]
    public void ResultTE_PreservesKeyFeatures()
    {
        var result = _converter.Convert(ResultCSharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var cls = Assert.Single(result.Ast!.Classes);

        // Readonly struct
        Assert.True(cls.IsStruct, "Should be struct");
        Assert.True(cls.IsReadOnly, "Should be readonly");

        // Private constructors (two of them)
        Assert.True(cls.Constructors.Count(c => c.Visibility == Visibility.Private) >= 2,
            "Should have at least 2 private constructors");

        // Generic method (Map<TNew>)
        Assert.Contains(cls.Methods, m => m.Name == "Map" && m.TypeParameters.Count > 0);

        // Properties
        Assert.Contains(cls.Properties, p => p.Name == "IsOk");
        Assert.Contains(cls.Properties, p => p.Name == "Value");
        Assert.Contains(cls.Properties, p => p.Name == "Error");
    }

    #endregion

    #region Test 4: Round-Trip

    [Fact]
    public void OptionT_Roundtrip_CalorContainsKeyPatterns()
    {
        var result = _converter.Convert(OptionCSharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var calor = result.CalorSource!;

        // Struct and readonly modifiers preserved in emitted Calor
        Assert.Contains("struct", calor);
        Assert.Contains("readonly", calor);

        // Operators emitted
        Assert.Contains("§OP", calor); // operator declarations present
    }

    [Fact]
    public void OptionT_Roundtrip_SimpleStruct_FullRoundtrip()
    {
        // Test full roundtrip with a simpler Option struct (no complex expressions)
        var simpleCsharp = """
            public readonly struct SimpleOption<T>
            {
                private readonly T _value;
                private readonly bool _hasValue;

                private SimpleOption(T value)
                {
                    _value = value;
                    _hasValue = true;
                }

                public bool HasValue => _hasValue;
            }
            """;

        var result = _converter.Convert(simpleCsharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var compilationResult = Program.Compile(result.CalorSource!);
        Assert.False(compilationResult.HasErrors,
            "Roundtrip parse failed:\n" +
            string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var output = emitter.Emit(compilationResult.Ast!);

        Assert.Contains("readonly struct", output);
    }

    [Fact]
    public void ResultTE_Roundtrip_CalorContainsKeyPatterns()
    {
        var result = _converter.Convert(ResultCSharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var calor = result.CalorSource!;

        // Struct and readonly modifiers preserved in emitted Calor
        Assert.Contains("struct", calor);
        Assert.Contains("readonly", calor);
    }

    #endregion

    #region Test 5: Target-Typed New Inference

    [Fact]
    public void TargetTypedNew_InExpressionBodiedMethod_InfersReturnType()
    {
        var csharp = """
            public readonly struct Wrapper<T>
            {
                private readonly T _value;

                private Wrapper(T value)
                {
                    _value = value;
                }

                public static Wrapper<T> Create(T value) => new(value);
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var calor = result.CalorSource!;
        // Should NOT contain "new object" — that would indicate failed inference
        Assert.DoesNotContain("new object", calor);
    }

    [Fact]
    public void TargetTypedNew_InReturnStatement_InfersReturnType()
    {
        var csharp = """
            public readonly struct Box<T>
            {
                private readonly T _value;

                private Box(T value)
                {
                    _value = value;
                }

                public static Box<T> Create(T value)
                {
                    return new(value);
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var calor = result.CalorSource!;
        Assert.DoesNotContain("new object", calor);
    }

    [Fact]
    public void TargetTypedNew_InImplicitOperator_InfersReturnType()
    {
        var csharp = """
            public readonly struct Tag
            {
                private readonly string _value;

                private Tag(string value)
                {
                    _value = value;
                }

                public static implicit operator Tag(string value) => new(value);
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var calor = result.CalorSource!;
        Assert.DoesNotContain("new object", calor);
    }

    #endregion

    #region Test 6: Per-Member Fallback

    [Fact]
    public void PerMemberFallback_FailingMemberDoesNotWrapEntireStruct()
    {
        // Use interop mode to trigger per-member fallback
        var converter = new CSharpToCalorConverter(new ConversionOptions
        {
            Mode = ConversionMode.Interop
        });

        // A struct where most members convert fine, including an indexer (now supported)
        var csharp = """
            public struct MixedStruct
            {
                public int Value { get; set; }

                public string Name { get; set; }

                public void NormalMethod()
                {
                    var x = 1 + 2;
                }

                // Indexer — now fully supported
                public int this[int index]
                {
                    get => index;
                }
            }
            """;

        var result = converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var cls = Assert.Single(result.Ast!.Classes);

        // The normal members should still be converted
        Assert.Contains(cls.Properties, p => p.Name == "Value");
        Assert.Contains(cls.Properties, p => p.Name == "Name");
        Assert.Contains(cls.Methods, m => m.Name == "NormalMethod");

        // The struct itself should NOT be entirely wrapped — it should exist as a class node
        Assert.True(cls.IsStruct, "Should still be a struct, not entirely wrapped in interop");

        // The indexer should be converted as a first-class IndexerNode
        Assert.True(cls.Indexers.Count > 0,
            "The indexer should be converted as an IndexerNode");
    }

    #endregion

    #region Test 7: Explicit Conversion Operator InferTargetType

    [Fact]
    public void TargetTypedNew_InExplicitOperator_InfersReturnType()
    {
        var csharp = """
            public readonly struct Celsius
            {
                private readonly double _value;

                private Celsius(double value)
                {
                    _value = value;
                }

                public static explicit operator Celsius(double value) => new(value);
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var calor = result.CalorSource!;
        Assert.DoesNotContain("new object", calor);
    }

    #endregion

    #region Test 8: Nested Lambda Does Not Inherit Outer Return Type

    [Fact]
    public void TargetTypedNew_InsideLambda_DoesNotInferOuterMethodReturnType()
    {
        var csharp = """
            using System;

            public class Container
            {
                public int OuterMethod()
                {
                    Func<string> f = () => new("hello");
                    return 42;
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var calor = result.CalorSource!;
        // The new("hello") is inside a lambda — should NOT infer 'int' (outer method return type).
        // Without semantic model it falls back to 'object', which is acceptable.
        // The critical thing is it must NOT contain "new i32" which would be the incorrect outer type.
        Assert.DoesNotContain("new i32", calor);
    }

    [Fact]
    public void TargetTypedNew_InsideLocalFunction_DoesNotInferOuterMethodReturnType()
    {
        var csharp = """
            using System;

            public class Container
            {
                public int OuterMethod()
                {
                    string Local() => new("hello");
                    return 42;
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var calor = result.CalorSource!;
        // The new("hello") is inside a local function — should NOT infer 'int'.
        // It should either infer 'string' (local function return type) or fall back to 'object'.
        Assert.DoesNotContain("new i32", calor);
    }

    #endregion

    #region Helpers

    private static string GetErrorMessage(ConversionResult result)
    {
        if (result.Success) return string.Empty;
        return string.Join("\n", result.Issues.Select(i => i.ToString()));
    }

    #endregion
}
