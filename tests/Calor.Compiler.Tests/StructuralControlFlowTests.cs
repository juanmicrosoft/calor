using System.Reflection;
using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public class StructuralControlFlowTests
{
    private static readonly TextSpan Span = TextSpan.Empty;

    [Fact]
    public void BlockBodiedLambda_ContainsEveryStructuralBlockStatement()
    {
        foreach (var sample in BlockStatementSamples())
        {
            var lambda = new LambdaExpressionNode(
                Span,
                $"lambda_{sample.Statement.GetType().Name}",
                Array.Empty<LambdaParameterNode>(),
                effects: null,
                isAsync: false,
                expressionBody: null,
                statementBody: [sample.Statement],
                new AttributeCollection());

            var emitted = lambda.Accept(new CSharpEmitter());

            Assert.StartsWith("() => {", emitted, StringComparison.Ordinal);
            Assert.Contains(sample.Marker, emitted, StringComparison.Ordinal);
            Assert.EndsWith("\n}", emitted, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void BlockBodiedLambdaSamples_CoverEveryStructuralBlockStatementType()
    {
        var sampled = BlockStatementSamples()
            .Select(sample => sample.Statement.GetType())
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();
        var discovered = typeof(StatementNode).Assembly.GetTypes()
            .Where(type =>
                !type.IsAbstract
                && typeof(StatementNode).IsAssignableFrom(type)
                && HasStatementBody(type, new HashSet<Type>()))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(discovered, sampled);
    }

    [Fact]
    public void CSharpEmitter_UsesExplicitEmissionContextInsteadOfGlobalWriter()
    {
        var emitterType = typeof(CSharpEmitter);
        Assert.DoesNotContain(
            emitterType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic),
            field => field.FieldType == typeof(System.Text.StringBuilder));

        var contextType = emitterType.GetNestedType(
            "EmissionContext",
            BindingFlags.NonPublic);
        Assert.NotNull(contextType);
        Assert.Contains(
            emitterType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic),
            method =>
                method.Name == "EmitStatement"
                && method.GetParameters().Any(parameter =>
                    parameter.ParameterType == contextType));
    }

    [Fact]
    public void GuardedMatchStatement_UsesGuardForRuntimeSelection()
    {
        var assembly = Compile(
            """
            §M{m001:GuardedMatch}
              §F{f001:Classify:pub} (i32 value) -> str
                §W{m001} value
                  §K §VAR{n} §WHEN (> n INT:10)
                    §R STR:"large"
                  §K §VAR{n} §WHEN (> n INT:0)
                    §R STR:"positive"
                  §K _
                    §R STR:"other"
            """,
            out var generated);

        Assert.Contains("case var n when", generated, StringComparison.Ordinal);
        Assert.Contains("n > 10", generated, StringComparison.Ordinal);
        Assert.Equal(
            "large",
            InvokeStatic(assembly, "GuardedMatchModule", "Classify", 20));
        Assert.Equal(
            "positive",
            InvokeStatic(assembly, "GuardedMatchModule", "Classify", 5));
        Assert.Equal(
            "other",
            InvokeStatic(assembly, "GuardedMatchModule", "Classify", -1));
    }

    [Fact]
    public void ForLoops_HandleDynamicDirectionsExpressionsZeroAndExactOnceBounds()
    {
        var assembly = Compile(
            """
            §M{m001:StructuralLoops}
              §F{f001:Encode:pub} (i32 from, i32 to, i32 step) -> i32
                §B{~value:i32} INT:0
                §L{l001:i:from:to:step}
                  §B{~value:i32} (+ (* value INT:10) i)
                §R value

              §F{f002:ExpressionStep:pub} () -> i32
                §B{one:i32} INT:1
                §B{~value:i32} INT:0
                §L{l002:i:1:5:(+ one one)}
                  §B{~value:i32} (+ (* value INT:10) i)
                §R value

              §F{f003:ExactOnce:pub} () -> i32
                §B{~fromCalls:i32} INT:0
                §B{~toCalls:i32} INT:0
                §B{~stepCalls:i32} INT:0
                §B{~value:i32} INT:0
                §L{l003:i:(pre-inc fromCalls):(+ (pre-inc toCalls) INT:2):(pre-inc stepCalls)}
                  §B{~value:i32} (+ (* value INT:10) i)
                §R (+ (* value INT:1000) (+ (* fromCalls INT:100) (+ (* toCalls INT:10) stepCalls)))

              §F{f004:Collision:pub} () -> i32
                §B{__calorForFrom:i32} INT:7
                §B{~value:i32} INT:0
                §L{l004:i:1:1:1}
                  §B{~value:i32} (+ value i)
                §R (+ value __calorForFrom)
            """,
            out var generated);

        Assert.Equal(
            123,
            InvokeStatic(assembly, "StructuralLoopsModule", "Encode", 1, 3, 1));
        Assert.Equal(
            321,
            InvokeStatic(assembly, "StructuralLoopsModule", "Encode", 3, 1, -1));
        Assert.Equal(
            135,
            InvokeStatic(assembly, "StructuralLoopsModule", "ExpressionStep"));
        Assert.Equal(
            123111,
            InvokeStatic(assembly, "StructuralLoopsModule", "ExactOnce"));
        Assert.Equal(
            8,
            InvokeStatic(assembly, "StructuralLoopsModule", "Collision"));
        Assert.Contains(
            "int __calorForFrom = 7;",
            generated,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "var __calorForFrom =",
            generated,
            StringComparison.Ordinal);

        var exception = Assert.Throws<TargetInvocationException>(() =>
            InvokeStatic(
                assembly,
                "StructuralLoopsModule",
                "Encode",
                1,
                3,
                0));
        Assert.IsType<ArgumentOutOfRangeException>(exception.InnerException);
    }

    [Fact]
    public void IntegralLoops_TerminateAtExtremaForEveryIntegralType()
    {
        var functions = new[]
        {
            "i8", "u8", "i16", "u16", "i32", "u32", "i64", "u64"
        }.Select(CreateCountingLoopFunction).ToArray();
        var module = new ModuleNode(
            Span,
            "integral-extrema",
            "IntegralExtrema",
            Array.Empty<UsingDirectiveNode>(),
            functions,
            new AttributeCollection());
        var assembly = CompileGenerated(new CSharpEmitter().Emit(module));

        AssertSignedExtrema<sbyte>(
            assembly,
            "Count_i8",
            sbyte.MinValue,
            sbyte.MaxValue);
        AssertUnsignedExtrema<byte>(
            assembly,
            "Count_u8",
            byte.MaxValue);
        AssertSignedExtrema<short>(
            assembly,
            "Count_i16",
            short.MinValue,
            short.MaxValue);
        AssertUnsignedExtrema<ushort>(
            assembly,
            "Count_u16",
            ushort.MaxValue);
        AssertSignedExtrema<int>(
            assembly,
            "Count_i32",
            int.MinValue,
            int.MaxValue);
        AssertUnsignedExtrema<uint>(
            assembly,
            "Count_u32",
            uint.MaxValue);
        AssertSignedExtrema<long>(
            assembly,
            "Count_i64",
            long.MinValue,
            long.MaxValue);
        AssertUnsignedExtrema<ulong>(
            assembly,
            "Count_u64",
            ulong.MaxValue);
    }

    [Fact]
    public void OmittedSteps_PreserveIntegralTypeAndExactDynamicSequence()
    {
        var functions = new[]
        {
            "i8", "u8", "i16", "u16", "i32", "u32", "i64", "u64"
        }.Select(CreateOmittedStepSequenceFunction).ToArray();
        var module = new ModuleNode(
            Span,
            "omitted-step",
            "OmittedStep",
            Array.Empty<UsingDirectiveNode>(),
            functions,
            new AttributeCollection());
        var generated = new CSharpEmitter().Emit(module);
        var assembly = CompileGenerated(generated);

        AssertOmittedSequence<sbyte>(
            assembly,
            "Sequence_i8",
            (sbyte)125,
            sbyte.MaxValue);
        AssertOmittedSequence<byte>(
            assembly,
            "Sequence_u8",
            (byte)253,
            byte.MaxValue);
        AssertOmittedSequence<short>(
            assembly,
            "Sequence_i16",
            (short)(short.MaxValue - 2),
            short.MaxValue);
        AssertOmittedSequence<ushort>(
            assembly,
            "Sequence_u16",
            (ushort)(ushort.MaxValue - 2),
            ushort.MaxValue);
        AssertOmittedSequence<int>(
            assembly,
            "Sequence_i32",
            int.MaxValue - 2,
            int.MaxValue);
        AssertOmittedSequence<uint>(
            assembly,
            "Sequence_u32",
            uint.MaxValue - 2,
            uint.MaxValue);
        AssertOmittedSequence<long>(
            assembly,
            "Sequence_i64",
            long.MaxValue - 2,
            long.MaxValue);
        AssertOmittedSequence<ulong>(
            assembly,
            "Sequence_u64",
            ulong.MaxValue - 2,
            ulong.MaxValue);

        Assert.DoesNotContain("__calorForStep", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("+=", generated, StringComparison.Ordinal);
        Assert.Contains("value++;", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void RawCSharpIdentifiers_CannotCollideWithGeneratedLoopState()
    {
        var assembly = Compile(
            """
            §M{m001:RawLoopCollision}
              §F{f001:Run:pub} () -> i32
                §B{~result:i32} INT:0
                §RAW
                int __calorForFrom = 10;
                int __calorForTo = 20;
                int __calorForStep = 30;
                int __calorForAscending = 40;
                int __calorForFirst = 50;
                goto __calorForAdvance;
                __calorForAdvance:
                result += __calorForFrom + __calorForTo + __calorForStep
                    + __calorForAscending + __calorForFirst;
                §/RAW
                §L{l001:i:1:2:1}
                  §RAW
                  if (i == 1) continue;
                  §/RAW
                  §B{~result:i32} (+ result i)
                §R result
            """,
            out var generated);

        Assert.Contains("var __calorForFrom_1 = 1;", generated);
        Assert.Contains("var __calorForFirst_1 = true;", generated);
        Assert.Contains("while (true)", generated);
        Assert.Equal(
            152,
            InvokeStatic(assembly, "RawLoopCollisionModule", "Run"));
    }

    [Fact]
    public void RawCSharpIdentifiers_AreReservedAcrossEveryPreprocessorBranch()
    {
        var defaultAssembly = Compile(
            """
            §M{m001:RawConditionalCollision}
              §F{f001:Run:pub} () -> i32
                §B{~result:i32} INT:0
                §RAW
                #if BROKEN
                int __calorForFrom_1 = 1;
                /* deliberately unterminated disabled fragment
                #elif FOO
                int __calorForFrom = 10;
                int __calorForTo = 20;
                __calorForFirst:
                result += __calorForFrom + __calorForTo;
                #elif BAR
                int __calorForStep = 20;
                result += __calorForStep;
                #else
                int __calorForAscending = 30;
                #if NESTED
                int __calorForFirst_1 = 40;
                result += __calorForAscending + __calorForFirst_1;
                #else
                int __calorForFirst_1 = 50;
                result += __calorForAscending + __calorForFirst_1;
                #endif
                #endif
                // __calorForFrom_2 and keywords class while are lexical noise.
                string lexicalNoise = "__calorForTo_1 __calorForStep_1";
                _ = lexicalNoise;
                §/RAW
                §L{l001:i:1:2:1}
                  §B{~result:i32} (+ result i)
                §R result
            """,
            out var generated);

        Assert.Contains("var __calorForFrom_2 = 1;", generated);
        Assert.Contains("var __calorForTo_1 = 2;", generated);
        Assert.Contains("var __calorForStep_1 = 1;", generated);
        Assert.Contains("var __calorForAscending_1 =", generated);
        Assert.Contains("var __calorForFirst_2 = true;", generated);
        Assert.Equal(
            83,
            InvokeStatic(
                defaultAssembly,
                "RawConditionalCollisionModule",
                "Run"));

        var fooAssembly = CompileGenerated(generated, "FOO");
        Assert.Equal(
            33,
            InvokeStatic(
                fooAssembly,
                "RawConditionalCollisionModule",
                "Run"));

        var barAssembly = CompileGenerated(generated, "BAR");
        Assert.Equal(
            23,
            InvokeStatic(
                barAssembly,
                "RawConditionalCollisionModule",
                "Run"));
    }

    [Fact]
    public void YieldDetection_IsStructuralAcrossLegalContainers()
    {
        foreach (var statement in new StatementNode[]
                 {
                     MatchWith(YieldValue()),
                     new UsingStatementNode(
                         Span,
                         "resource",
                         "IDisposable",
                         new ReferenceNode(Span, "source"),
                         [YieldValue()]),
                     new SyncBlockNode(
                         Span,
                         "sync",
                         new ReferenceNode(Span, "gate"),
                         [YieldValue()]),
                 })
        {
            var (function, diagnostics) = ValidateFunctionBody(statement);

            Assert.Equal(ReturnShape.Kind.Iterator, ReturnShape.Classify(function));
            Assert.DoesNotContain(
                diagnostics,
                diagnostic => diagnostic.Code == DiagnosticCode.IllegalYield);
        }
    }

    [Fact]
    public void YieldAcrossMatchUsingAndSync_CompilesAndRuns()
    {
        var function = new FunctionNode(
            Span,
            "values",
            "Values",
            Visibility.Public,
            [
                new ParameterNode(
                    Span,
                    "source",
                    "IDisposable",
                    new AttributeCollection()),
                new ParameterNode(
                    Span,
                    "gate",
                    "object",
                    new AttributeCollection())
            ],
            new OutputNode(Span, "i32"),
            effects: null,
            body:
            [
                new UsingStatementNode(
                    Span,
                    "resource",
                    "IDisposable",
                    new ReferenceNode(Span, "source"),
                    [new YieldReturnStatementNode(Span, Int(1))]),
                new SyncBlockNode(
                    Span,
                    "sync",
                    new ReferenceNode(Span, "gate"),
                    [new YieldReturnStatementNode(Span, Int(2))]),
                MatchWith(new YieldReturnStatementNode(Span, Int(3)))
            ],
            new AttributeCollection());
        var module = new ModuleNode(
            Span,
            "module",
            "YieldContainers",
            Array.Empty<UsingDirectiveNode>(),
            [function],
            new AttributeCollection());
        var validation = new DiagnosticBag();
        new ReturnValidationPass(validation).Check(module);
        Assert.False(
            validation.HasErrors,
            string.Join(Environment.NewLine, validation.Errors));

        var generated = new CSharpEmitter().Emit(module);
        var assembly = CompileGenerated(generated);
        using var source = new MemoryStream();
        var values = Assert.IsAssignableFrom<IEnumerable<int>>(
                InvokeStatic(
                    assembly,
                    "YieldContainersModule",
                    "Values",
                    source,
                    new object()))
            .ToArray();

        Assert.Equal(new[] { 1, 2, 3 }, values);
    }

    [Fact]
    public void IllegalYieldLocations_AreDiagnosedExhaustively()
    {
        foreach (var statement in new StatementNode[]
                 {
                     new UnsafeBlockNode(Span, "unsafe", [YieldValue()]),
                     new FixedStatementNode(
                         Span,
                         "fixed",
                         "pointer",
                         "i32*",
                         new ReferenceNode(Span, "source"),
                         [YieldValue()]),
                     new TryStatementNode(
                         Span,
                         "catch",
                         Array.Empty<StatementNode>(),
                         [
                             new CatchClauseNode(
                                 Span,
                                 exceptionType: null,
                                 variableName: null,
                                 filter: null,
                                 body: [YieldValue()],
                                 new AttributeCollection())
                         ],
                         finallyBody: null,
                         new AttributeCollection()),
                     new TryStatementNode(
                         Span,
                         "finally",
                         Array.Empty<StatementNode>(),
                         Array.Empty<CatchClauseNode>(),
                         finallyBody: [YieldValue()],
                         new AttributeCollection()),
                     new TryStatementNode(
                         Span,
                         "try-catch",
                         [YieldValue()],
                         [
                             new CatchClauseNode(
                                 Span,
                                 exceptionType: null,
                                 variableName: null,
                                 filter: null,
                                 body: Array.Empty<StatementNode>(),
                                 new AttributeCollection())
                         ],
                         finallyBody: null,
                         new AttributeCollection()),
                 })
        {
            var (_, diagnostics) = ValidateFunctionBody(statement);
            Assert.Contains(
                diagnostics,
                diagnostic => diagnostic.Code == DiagnosticCode.IllegalYield);
        }
    }

    [Fact]
    public void PropertyAndIndexerYields_FailClosedForScalarAndEnumerableTypes()
    {
        var properties = new[]
        {
            PropertyWithYield("Scalar", "i32"),
            PropertyWithYield("Enumerable", "IEnumerable<i32>"),
        };
        var indexers = new[]
        {
            IndexerWithYield("i32"),
            IndexerWithYield("IEnumerable<i32>"),
        };
        var type = new ClassDefinitionNode(
            Span,
            "accessors",
            "AccessorYields",
            isAbstract: false,
            isSealed: false,
            isPartial: false,
            isStatic: false,
            baseClass: null,
            implementedInterfaces: Array.Empty<string>(),
            typeParameters: Array.Empty<TypeParameterNode>(),
            fields: Array.Empty<ClassFieldNode>(),
            properties,
            constructors: Array.Empty<ConstructorNode>(),
            methods: Array.Empty<MethodNode>(),
            events: Array.Empty<EventDefinitionNode>(),
            operatorOverloads: Array.Empty<OperatorOverloadNode>(),
            new AttributeCollection(),
            csharpAttributes: Array.Empty<CalorAttributeNode>(),
            indexers: indexers);
        var module = new ModuleNode(
            Span,
            "module",
            "AccessorYields",
            Array.Empty<UsingDirectiveNode>(),
            Array.Empty<InterfaceDefinitionNode>(),
            [type],
            Array.Empty<FunctionNode>(),
            new AttributeCollection());
        var diagnostics = new DiagnosticBag();

        new ReturnValidationPass(diagnostics).Check(module);

        var yieldErrors = diagnostics
            .Where(diagnostic => diagnostic.Code == DiagnosticCode.IllegalYield)
            .ToArray();
        Assert.Equal(4, yieldErrors.Length);
        Assert.All(
            yieldErrors,
            diagnostic => Assert.Contains(
                "property/indexer accessor",
                diagnostic.Message,
                StringComparison.Ordinal));
        Assert.True(diagnostics.HasErrors);
    }

    [Fact]
    public void NestedLambdaYield_IsIllegalAndDoesNotClassifyOuterFunctionAsIterator()
    {
        var lambda = new LambdaExpressionNode(
            Span,
            "nested",
            Array.Empty<LambdaParameterNode>(),
            effects: null,
            isAsync: false,
            expressionBody: null,
            statementBody: [YieldValue()],
            new AttributeCollection());
        var bind = new BindStatementNode(
            Span,
            "nested",
            typeName: null,
            isMutable: false,
            lambda,
            new AttributeCollection());
        var (function, diagnostics) = ValidateFunctionBody(
            bind,
            [
                new ParameterNode(
                    Span,
                    "value",
                    "i32",
                    Calor.Compiler.Ast.ParameterModifier.Ref,
                    new AttributeCollection(),
                    Array.Empty<CalorAttributeNode>())
            ]);

        Assert.Equal(ReturnShape.Kind.Void, ReturnShape.Classify(function));
        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.IllegalYield);
        Assert.DoesNotContain(
            diagnostics,
            diagnostic =>
                diagnostic.Code == DiagnosticCode.IllegalYield
                && diagnostic.Message.Contains(
                    "Iterator function 'Values' cannot declare parameter",
                    StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("ref")]
    [InlineData("in")]
    [InlineData("out")]
    public void CalorIterator_RejectsByReferenceParameters(string modifier)
    {
        var result = Program.Compile(
            $$"""
            §M{m001:IteratorParameters}
              §F{f001:Values:pub}
                §I{i32:value:{{modifier}}}
                §O{i32}
                §YIELD value
            """);

        Assert.Contains(
            result.Diagnostics,
            diagnostic =>
                diagnostic.Code == DiagnosticCode.IllegalYield
                && diagnostic.Message.Contains(
                    $"modifier '{modifier}'",
                    StringComparison.Ordinal));
        Assert.Empty(result.GeneratedCode);
    }

    [Fact]
    public void CalorIterator_AcceptsOrdinaryValueParameter()
    {
        var result = Program.Compile(
            """
            §M{m001:IteratorParameters}
              §F{f001:Values:pub}
                §I{i32:value}
                §O{i32}
                §YIELD value
            """);

        Assert.False(
            result.HasErrors,
            string.Join(Environment.NewLine, result.Diagnostics.Errors));
        Assert.Contains("IEnumerable<int>", result.GeneratedCode);
    }

    [Fact]
    public void AsyncAndStaticGenericIterators_StillRejectRefParameters()
    {
        foreach (var source in new[]
                 {
                     """
                     §M{m001:AsyncIteratorParameters}
                       §AF{f001:Values:pub}
                         §I{i32:value:ref}
                         §O{i32}
                         §YIELD value
                     """,
                     """
                     §M{m001:GenericIteratorParameters}
                       §CL{c001:Container:pub}
                         §MT{m001:Values:pub:stat}<T>
                           §I{T:value:ref}
                           §O{T}
                           §YIELD value
                     """
                 })
        {
            var result = Program.Compile(source);
            Assert.Contains(
                result.Diagnostics,
                diagnostic =>
                    diagnostic.Code == DiagnosticCode.IllegalYield
                    && diagnostic.Message.Contains(
                        "modifier 'ref'",
                        StringComparison.Ordinal));
            Assert.Empty(result.GeneratedCode);
        }
    }

    [Theory]
    [InlineData("ref")]
    [InlineData("in")]
    [InlineData("out")]
    public void MigratedCSharpIterator_RejectsByReferenceParameters(
        string modifier)
    {
        var assignment = modifier == "out" ? "value = 1;" : "";
        var conversion = new CSharpToCalorConverter().Convert(
            $$"""
            using System.Collections.Generic;
            public class IteratorParameters
            {
                public IEnumerable<int> Values({{modifier}} int value)
                {
                    {{assignment}}
                    yield return value;
                }
            }
            """);
        Assert.False(conversion.Success);
        Assert.Contains(
            conversion.Issues,
            issue => issue.Message.Contains(
                $"modifier '{modifier}'",
                StringComparison.Ordinal));
        Assert.NotNull(conversion.Ast);
        var diagnostics = new DiagnosticBag();

        new ReturnValidationPass(diagnostics).Check(conversion.Ast!);

        Assert.Contains(
            diagnostics,
            diagnostic =>
                diagnostic.Code == DiagnosticCode.IllegalYield
                && diagnostic.Message.Contains(
                    $"modifier '{modifier}'",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void MigratedStaticGenericIterator_AcceptsValueParameter()
    {
        var conversion = new CSharpToCalorConverter().Convert(
            """
            using System.Collections.Generic;
            public static class IteratorParameters
            {
                public static IEnumerable<T> Values<T>(T value)
                {
                    yield return value;
                }
            }
            """);
        Assert.True(
            conversion.Success,
            string.Join(Environment.NewLine, conversion.Issues));
        var diagnostics = new DiagnosticBag();

        new ReturnValidationPass(diagnostics).Check(conversion.Ast!);

        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.IllegalYield);
    }

    [Fact]
    public void MigratedLocalIterator_DoesNotPoisonOuterRefParameter()
    {
        var conversion = new CSharpToCalorConverter().Convert(
            """
            using System.Collections.Generic;
            public class IteratorParameters
            {
                public void Outer(ref int value)
                {
                    int copy = value;
                    IEnumerable<int> Local()
                    {
                        yield return copy;
                    }
                }
            }
            """);
        Assert.True(
            conversion.Success,
            string.Join(Environment.NewLine, conversion.Issues));
        var diagnostics = new DiagnosticBag();

        new ReturnValidationPass(diagnostics).Check(conversion.Ast!);

        Assert.DoesNotContain(
            diagnostics,
            diagnostic =>
                diagnostic.Code == DiagnosticCode.IllegalYield
                && diagnostic.Message.Contains(
                    "Outer",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void MigratedLocalIterator_RejectsItsOwnRefParameter()
    {
        var conversion = new CSharpToCalorConverter().Convert(
            """
            using System.Collections.Generic;
            public class IteratorParameters
            {
                public void Outer()
                {
                    IEnumerable<int> Local(ref int value)
                    {
                        yield return value;
                    }
                }
            }
            """);
        Assert.False(conversion.Success);
        Assert.Contains(
            conversion.Issues,
            issue => issue.Message.Contains(
                "local function 'Local'",
                StringComparison.Ordinal)
                && issue.Message.Contains(
                    "modifier 'ref'",
                    StringComparison.Ordinal));
        Assert.NotNull(conversion.Ast);
        var interop = Assert.Single(
            conversion.Ast.Classes.SelectMany(type => type.InteropBlocks));
        Assert.Contains("Local(ref int value)", interop.CSharpCode);
        var diagnostics = new DiagnosticBag();

        new ReturnValidationPass(diagnostics).Check(conversion.Ast!);

        Assert.Contains(
            diagnostics,
            diagnostic =>
                diagnostic.Code == DiagnosticCode.IllegalYield
                && diagnostic.Message.Contains(
                    "local function 'Local'",
                    StringComparison.Ordinal)
                && diagnostic.Message.Contains(
                    "modifier 'ref'",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void ValuelessYield_IsRejectedAndNeverEmittedAsYieldReturn()
    {
        var result = Program.Compile(
            """
            §M{m001:ValuelessYield}
              §F{f001:Values:pub} () -> i32
                §YIELD
            """);

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.YieldRequiresValue);
        Assert.DoesNotContain("yield return;", result.GeneratedCode, StringComparison.Ordinal);
    }

    private static IEnumerable<(StatementNode Statement, string Marker)>
        BlockStatementSamples()
    {
        var leaf = Leaf();
        yield return (
            new ForStatementNode(
                Span,
                "for",
                "i",
                Int(0),
                Int(0),
                Int(1),
                [leaf],
                new AttributeCollection()),
            "while (true)");
        yield return (
            new WhileStatementNode(
                Span,
                "while",
                new BoolLiteralNode(Span, false),
                [leaf],
                new AttributeCollection()),
            "while (");
        yield return (
            new DoWhileStatementNode(
                Span,
                "do",
                [leaf],
                new BoolLiteralNode(Span, false),
                new AttributeCollection()),
            "do\n");
        yield return (
            new IfStatementNode(
                Span,
                "if",
                new BoolLiteralNode(Span, true),
                [leaf],
                [
                    new ElseIfClauseNode(
                        Span,
                        new BoolLiteralNode(Span, false),
                        [leaf])
                ],
                [leaf],
                new AttributeCollection()),
            "if (");
        yield return (MatchWith(leaf), "switch (");
        yield return (
            new ForeachStatementNode(
                Span,
                "each",
                "item",
                "i32",
                new ReferenceNode(Span, "items"),
                [leaf],
                new AttributeCollection()),
            "foreach (");
        yield return (
            new DictionaryForeachNode(
                Span,
                "eachkv",
                "key",
                "value",
                new ReferenceNode(Span, "items"),
                [leaf],
                new AttributeCollection()),
            "foreach (");
        yield return (
            new UsingStatementNode(
                Span,
                "resource",
                "IDisposable",
                new ReferenceNode(Span, "source"),
                [leaf]),
            "using (");
        yield return (
            new TryStatementNode(
                Span,
                "try",
                [leaf],
                [
                    new CatchClauseNode(
                        Span,
                        exceptionType: null,
                        variableName: null,
                        filter: null,
                        body: [leaf],
                        new AttributeCollection())
                ],
                finallyBody: [leaf],
                new AttributeCollection()),
            "try");
        yield return (
            new PreprocessorDirectiveNode(Span, "TEST", [leaf], [leaf]),
            "#if TEST");
        yield return (
            new UnsafeBlockNode(Span, "unsafe", [leaf]),
            "unsafe");
        yield return (
            new SyncBlockNode(
                Span,
                "sync",
                new ReferenceNode(Span, "gate"),
                [leaf]),
            "lock (");
        yield return (
            new FixedStatementNode(
                Span,
                "fixed",
                "pointer",
                "i32*",
                new ReferenceNode(Span, "source"),
                [leaf]),
            "fixed (");
    }

    private static bool HasStatementBody(Type type, HashSet<Type> visited)
    {
        if (!visited.Add(type))
            return false;

        foreach (var property in type.GetProperties(
                     BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length != 0)
                continue;

            var childType = GetEnumerableElementType(property.PropertyType)
                ?? property.PropertyType;
            if (typeof(StatementNode).IsAssignableFrom(childType))
                return true;
            if (typeof(AstNode).IsAssignableFrom(childType)
                && !typeof(ExpressionNode).IsAssignableFrom(childType)
                && HasStatementBody(childType, visited))
            {
                return true;
            }
        }

        return false;
    }

    private static Type? GetEnumerableElementType(Type type)
    {
        if (type == typeof(string))
            return null;

        foreach (var candidate in new[] { type }.Concat(type.GetInterfaces()))
        {
            if (candidate.IsGenericType
                && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                return candidate.GetGenericArguments()[0];
            }
        }

        return null;
    }

    private static (FunctionNode Function, DiagnosticBag Diagnostics)
        ValidateFunctionBody(
            StatementNode statement,
            IReadOnlyList<ParameterNode>? parameters = null)
    {
        var function = new FunctionNode(
            Span,
            "function",
            "Values",
            Visibility.Public,
            parameters ?? Array.Empty<ParameterNode>(),
            output: null,
            effects: null,
            body: [statement],
            new AttributeCollection());
        var module = new ModuleNode(
            Span,
            "module",
            "YieldValidation",
            Array.Empty<UsingDirectiveNode>(),
            [function],
            new AttributeCollection());
        var diagnostics = new DiagnosticBag();
        new ReturnValidationPass(diagnostics).Check(module);
        return (function, diagnostics);
    }

    private static MatchStatementNode MatchWith(StatementNode statement) =>
        new(
            Span,
            "match",
            Int(0),
            [
                new MatchCaseNode(
                    Span,
                    new WildcardPatternNode(Span),
                    guard: null,
                    body: [statement])
            ],
            new AttributeCollection());

    private static YieldReturnStatementNode YieldValue() =>
        new(Span, Int(1));

    private static PrintStatementNode Leaf() =>
        new(Span, Int(1));

    private static IntLiteralNode Int(int value) =>
        new(Span, value);

    private static FunctionNode CreateCountingLoopFunction(string typeName)
    {
        var count = new BindStatementNode(
            Span,
            "count",
            "i32",
            isMutable: true,
            Int(0),
            new AttributeCollection());
        var increment = new BindStatementNode(
            Span,
            "count",
            "i32",
            isMutable: true,
            new BinaryOperationNode(
                Span,
                BinaryOperator.Add,
                new ReferenceNode(Span, "count"),
                Int(1)),
            new AttributeCollection());
        var loop = new ForStatementNode(
            Span,
            "loop",
            "value",
            new ReferenceNode(Span, "from"),
            new ReferenceNode(Span, "to"),
            new ReferenceNode(Span, "step"),
            [increment],
            new AttributeCollection());
        return new FunctionNode(
            Span,
            $"count-{typeName}",
            $"Count_{typeName}",
            Visibility.Public,
            [
                new ParameterNode(
                    Span,
                    "from",
                    typeName,
                    new AttributeCollection()),
                new ParameterNode(
                    Span,
                    "to",
                    typeName,
                    new AttributeCollection()),
                new ParameterNode(
                    Span,
                    "step",
                    typeName,
                    new AttributeCollection()),
            ],
            new OutputNode(Span, "i32"),
            effects: null,
            body:
            [
                count,
                loop,
                new ReturnStatementNode(
                    Span,
                    new ReferenceNode(Span, "count"))
            ],
            new AttributeCollection());
    }

    private static FunctionNode CreateOmittedStepSequenceFunction(string typeName)
    {
        var loop = new ForStatementNode(
            Span,
            "loop",
            "value",
            new ReferenceNode(Span, "from"),
            new ReferenceNode(Span, "to"),
            step: null,
            body:
            [
                new YieldReturnStatementNode(
                    Span,
                    new ReferenceNode(Span, "value"))
            ],
            new AttributeCollection());
        return new FunctionNode(
            Span,
            $"sequence-{typeName}",
            $"Sequence_{typeName}",
            Visibility.Public,
            [
                new ParameterNode(
                    Span,
                    "from",
                    typeName,
                    new AttributeCollection()),
                new ParameterNode(
                    Span,
                    "to",
                    typeName,
                    new AttributeCollection()),
            ],
            new OutputNode(Span, typeName),
            effects: null,
            body: [loop],
            new AttributeCollection());
    }

    private static void AssertSignedExtrema<T>(
        Assembly assembly,
        string methodName,
        T min,
        T max)
        where T : struct, System.Numerics.INumber<T>, System.Numerics.IMinMaxValue<T>
    {
        Assert.Equal(
            2,
            InvokeStatic(
                assembly,
                "IntegralExtremaModule",
                methodName,
                max - T.One,
                max,
                T.One));
        Assert.Equal(
            2,
            InvokeStatic(
                assembly,
                "IntegralExtremaModule",
                methodName,
                min + T.One,
                min,
                -T.One));
        Assert.Equal(
            2,
            InvokeStatic(
                assembly,
                "IntegralExtremaModule",
                methodName,
                T.Zero,
                max,
                max));
        Assert.Equal(
            2,
            InvokeStatic(
                assembly,
                "IntegralExtremaModule",
                methodName,
                T.Zero,
                min,
                min));
    }

    private static void AssertUnsignedExtrema<T>(
        Assembly assembly,
        string methodName,
        T max)
        where T : struct, System.Numerics.INumber<T>
    {
        Assert.Equal(
            2,
            InvokeStatic(
                assembly,
                "IntegralExtremaModule",
                methodName,
                max - T.One,
                max,
                T.One));
        Assert.Equal(
            2,
            InvokeStatic(
                assembly,
                "IntegralExtremaModule",
                methodName,
                T.Zero,
                max,
                max));
    }

    private static void AssertOmittedSequence<T>(
        Assembly assembly,
        string methodName,
        T from,
        T to)
        where T : struct, System.Numerics.INumber<T>
    {
        var actual = Assert.IsAssignableFrom<IEnumerable<T>>(
                InvokeStatic(
                    assembly,
                    "OmittedStepModule",
                    methodName,
                    from,
                    to))
            .ToArray();
        Assert.Equal(
            new[] { from, from + T.One, to },
            actual);
    }

    private static PropertyNode PropertyWithYield(
        string name,
        string typeName) =>
        new(
            Span,
            $"property-{name}",
            name,
            typeName,
            Visibility.Public,
            new PropertyAccessorNode(
                Span,
                PropertyAccessorNode.AccessorKind.Get,
                visibility: null,
                Array.Empty<RequiresNode>(),
                [YieldValue()],
                new AttributeCollection()),
            setter: null,
            initer: null,
            defaultValue: null,
            new AttributeCollection());

    private static IndexerNode IndexerWithYield(string typeName) =>
        new(
            Span,
            $"indexer-{typeName}",
            typeName,
            Visibility.Public,
            MethodModifiers.None,
            [
                new ParameterNode(
                    Span,
                    "index",
                    "i32",
                    new AttributeCollection())
            ],
            new PropertyAccessorNode(
                Span,
                PropertyAccessorNode.AccessorKind.Get,
                visibility: null,
                Array.Empty<RequiresNode>(),
                [YieldValue()],
                new AttributeCollection()),
            setter: null,
            initer: null,
            new AttributeCollection(),
            Array.Empty<CalorAttributeNode>());

    private static Assembly Compile(string source, out string generatedCode)
    {
        var result = Program.Compile(
            source,
            "structural-control-flow.calr",
            new CompilationOptions
            {
                EnableTypeChecking = false,
                EnforceEffects = false,
            });
        Assert.False(
            result.HasErrors,
            string.Join(Environment.NewLine, result.Diagnostics.Errors));
        generatedCode = result.GeneratedCode;

        return CompileGenerated(generatedCode);
    }

    private static Assembly CompileGenerated(
        string generatedCode,
        params string[] preprocessorSymbols)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            GeneratedCSharpCompiler.GlobalUsingsPreamble + generatedCode,
            CSharpParseOptions.Default.WithPreprocessorSymbols(
                preprocessorSymbols));
        var compilation = CSharpCompilation.Create(
            $"StructuralControlFlow_{Guid.NewGuid():N}",
            [syntaxTree],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(
            emit.Success,
            string.Join(Environment.NewLine, emit.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }

    private static object? InvokeStatic(
        Assembly assembly,
        string typeName,
        string methodName,
        params object?[] arguments) =>
        Assert.Single(assembly.GetTypes(), type => type.Name == typeName)
            .GetMethod(methodName)!
            .Invoke(null, arguments);
}
