using Calor.Compiler.Ast;
using Calor.Compiler.Analysis;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification;
using Calor.Enforcement.Tests;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public class BclLoweringPrerequisiteTests
{
    [Theory]
    [InlineData("return receiver.GetGenericArguments().Length;")]
    [InlineData("return receiver.GetMethods().Length > 0 ? 2 : 0;")]
    [InlineData("var arguments = receiver.GenericTypeArguments; return arguments.Length;")]
    [InlineData("var arguments = receiver.GenericTypeArguments; var concreteArguments = concrete.GenericTypeArguments; return arguments.Length == concreteArguments.Length ? 2 : 0;")]
    [InlineData("var types = new Type[] { receiver }; return types.Count(type => type.GetGenericArguments().Length == 2) + 1;")]
    public void MetadataArrayResults_PreserveOrdinaryLengthAndRuntime(string body)
    {
        var converted = new CSharpToCalorConverter().Convert($$"""
            using System;
            using System.Linq;
            using System.Collections.Generic;
            public static class Sample
            {
                public static int Run()
                {
                    Type receiver = typeof(Dictionary<int, string>);
                    Type concrete = typeof(Tuple<int, string>);
                    {{body}}
                }
            }
            """);
        Assert.True(converted.Success, string.Join("\n", converted.Issues.Select(issue => issue.Message)));
        Assert.Equal(0, converted.InteropPreservationCount);
        Assert.Contains(".Length", converted.CalorSource);
        Assert.DoesNotContain("(len ", converted.CalorSource);
        var compiled = Program.Compile(converted.CalorSource!, "metadata-array.calr",
            new CompilationOptions { UnknownCallPolicy = UnknownCallPolicy.Strict, StatusWriter = TextWriter.Null });
        var legacySource = System.Text.RegularExpressions.Regex.Replace(
            converted.CalorSource!, @"§C\{([^}\r\n]+)\}\s*§/C\.Length", "(len §C{$1} §/C)");
        legacySource = System.Text.RegularExpressions.Regex.Replace(
            legacySource, @"\b([A-Za-z_][A-Za-z0-9_]*)\.Length\b", "(len $1)");
        var legacy = Program.Compile(legacySource,
            "legacy-array.calr",
            new CompilationOptions { UnknownCallPolicy = UnknownCallPolicy.Strict, StatusWriter = TextWriter.Null });
        Assert.Equal(
            legacy.Diagnostics.Where(diagnostic => diagnostic.Code is DiagnosticCode.UnknownExternalCall
                or DiagnosticCode.ForbiddenEffect).Select(diagnostic => diagnostic.Message).Order(),
            compiled.Diagnostics.Where(diagnostic => diagnostic.Code is DiagnosticCode.UnknownExternalCall
                or DiagnosticCode.ForbiddenEffect).Select(diagnostic => diagnostic.Message).Order());
        Assert.DoesNotContain(compiled.Diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.UnknownExternalCall && diagnostic.Message.Contains("get_Length"));
        var runtime = TestHarness.Execute(converted.CalorSource!, "Run");
        Assert.Null(runtime.Exception);
        Assert.Equal(2, runtime.ReturnValue);
    }

    [Fact]
    public void MetadataArrayResults_AreIndexedByCallerAndExpressionSpan()
    {
        var source = """
            §M{MetadataArrays}
              §U{ReflectionType:System.Type}
              §F{Known:pub} (ReflectionType:receiver) -> i32
                §B{arguments} §C{receiver.GetGenericArguments}
                §R arguments.Length
              §F{Unknown:pub} (Missing:receiver) -> i32
                §B{arguments} §C{receiver.GetGenericArguments}
                §R arguments.Length
            """;
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors);
        var graph = CallGraphAnalysis.Build(module);
        var known = Assert.IsType<BindStatementNode>(module.Functions[0].Body[0]).Initializer!;
        var unknown = Assert.IsType<BindStatementNode>(module.Functions[1].Body[0]).Initializer!;
        var array = Assert.IsType<Calor.Compiler.Binding.BoundTypes.ArrayBoundType>(
            graph.MetadataArrayReceiverType(module.Functions[0].Id, known.Span));
        Assert.Equal("System.Type", array.ElementType.DisplayString);
        Assert.Null(graph.MetadataArrayReceiverType(module.Functions[1].Id, known.Span));
        Assert.Null(graph.MetadataArrayReceiverType(module.Functions[1].Id, unknown.Span));
        var compiled = Program.Compile(source, "scoped-arrays.calr",
            new CompilationOptions { UnknownCallPolicy = UnknownCallPolicy.Strict, StatusWriter = TextWriter.Null });
        Assert.Single(compiled.Diagnostics.Where(diagnostic =>
            diagnostic.Code == DiagnosticCode.UnknownExternalCall && diagnostic.Message.Contains("get_Length")));
    }

    [Fact]
    public void MetadataArrayResult_IsInvalidatedByMutableRebinding()
    {
        var source = """
            §M{MetadataArrays}
              §F{Run:pub} (System.Type:receiver) -> i32
                §B{~values} §C{receiver.GetGenericArguments}
                §B{~values} 3
                §R values.Length
            """;
        var compiled = Program.Compile(source, "rebound-array.calr",
            new CompilationOptions { UnknownCallPolicy = UnknownCallPolicy.Strict, StatusWriter = TextWriter.Null });
        Assert.Contains(compiled.Diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.UnknownExternalCall && diagnostic.Message.Contains("get_Length"));
    }

    [Fact]
    public void MutableCapturedArrayResult_IsNotADeferredTypeGuarantee()
    {
        var source = """
            §M{MetadataArrays}
              §F{Run:pub} (System.Type:receiver) -> i32
                §B{~values} §C{receiver.GetGenericArguments}
                §B{read} §LAM{} values.Length §/LAM
                §B{~values} 3
                §R §C{read}
            """;
        var compiled = Program.Compile(source, "captured-array.calr",
            new CompilationOptions { UnknownCallPolicy = UnknownCallPolicy.Strict, StatusWriter = TextWriter.Null });
        Assert.DoesNotContain(compiled.Diagnostics, diagnostic =>
            diagnostic.Code.StartsWith("Calor01", StringComparison.Ordinal));
        Assert.Contains(compiled.Diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.UnknownExternalCall && diagnostic.Message.Contains("get_Length"));
    }

    [Fact]
    public void InstanceArrayCallThroughType_DoesNotEstablishAProducerType()
    {
        var source = """
            §M{MetadataArrays}
              §F{Run:pub} () -> i32
                §B{values} §C{System.Type.GetGenericArguments}
                §R values.Length
            """;
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors);
        var initializer = Assert.IsType<BindStatementNode>(module.Functions[0].Body[0]).Initializer!;
        var graph = CallGraphAnalysis.Build(module);
        Assert.Null(graph.MetadataArrayReceiverType(module.Functions[0].Id, initializer.Span));
        var compiled = Program.Compile(source, "type-array-call.calr",
            new CompilationOptions { UnknownCallPolicy = UnknownCallPolicy.Strict, StatusWriter = TextWriter.Null });
        Assert.Contains(compiled.Diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.UnknownExternalCall && diagnostic.Message.Contains("get_Length"));
    }

    [Fact]
    public void MetadataArrayCallWithArguments_RemainsUnknown()
    {
        var converted = new CSharpToCalorConverter().Convert("""
            using System;
            public static class Sample
            {
                public static int Run(Type receiver) => receiver.GetCustomAttributes(false).Length;
            }
            """);
        Assert.NotNull(converted.Ast);
        Assert.DoesNotContain("(len ", converted.CalorSource);
        var compiled = Program.Compile(converted.CalorSource!, "unsupported-array-call.calr",
            new CompilationOptions { UnknownCallPolicy = UnknownCallPolicy.Strict, StatusWriter = TextWriter.Null });
        Assert.Contains(compiled.Diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.UnknownExternalCall && diagnostic.Message.Contains("get_Length"));
    }

    [Fact]
    public void DeclaredCustomLength_IsNotMetadataArrayPurity()
    {
        var converted = new CSharpToCalorConverter().Convert("""
            using System;
            public sealed class Type
            {
                public Pretender GetGenericArguments()
                {
                    Sample.Trace = Sample.Trace * 10 + 1;
                    return new Pretender();
                }
            }
            public sealed class Pretender
            {
                public int Length
                {
                    get
                    {
                        Sample.Trace = Sample.Trace * 10 + 2;
                        return 4;
                    }
                }
            }
            public static class Sample
            {
                public static int Trace;
                public static int Run()
                {
                    Type receiver = new Type();
                    var width = receiver.GetGenericArguments().Length;
                    return Trace * 10 + width;
                }
            }
            """);
        Assert.True(converted.Success, string.Join("\n", converted.Issues.Select(issue => issue.Message)));
        Assert.Equal(0, converted.InteropPreservationCount);
        Assert.DoesNotContain("(len ", converted.CalorSource);
        var method = converted.Ast!.Classes.Single(type => type.Name == "Sample")
            .Methods.Single(candidate => candidate.Name == "Run");
        var width = Assert.IsType<FieldAccessNode>(
            method.Body.OfType<BindStatementNode>().Single(binding => binding.Name == "width").Initializer);
        var graph = CallGraphAnalysis.Build(converted.Ast);
        var callerId = $"Sample.{method.Id}";
        Assert.NotEmpty(graph.BoundValueTypes(callerId));
        Assert.Null(graph.MetadataArrayReceiverType(callerId, width.Target.Span));
        var runtime = TestHarness.Execute(converted.CalorSource!, "Run");
        Assert.Null(runtime.Exception);
        Assert.Equal(124, runtime.ReturnValue);
    }

    [Fact]
    public void MissingCrossFileLambdaType_RetainsLegacyBehaviorWithoutMetadataProof()
    {
        var missingInput = new CSharpToCalorConverter().Convert("""
            using System.Linq;
            public static class Sample
            {
                public static int Run() =>
                    External.Types.Count(type => type.GetGenericArguments().Length == 2);
            }
            """);
        Assert.NotNull(missingInput.Ast);
        Assert.Equal(0, missingInput.InteropPreservationCount);
        Assert.Contains("(len ", missingInput.CalorSource);
        var converted = new CSharpToCalorConverter().Convert("""
            using System.Linq;
            public static class Sample
            {
                public static int Run() =>
                    External.Types.Count((System.Type type) => External.Arguments(type).Length == 2);
            }
            """);
        Assert.NotNull(converted.Ast);
        Assert.Equal(0, converted.InteropPreservationCount);
        Assert.Contains("(len ", converted.CalorSource);
        var generated = new CSharpEmitter().Emit(converted.Ast!);
        var context = """
            public static class External
            {
                public static System.Type[] Arguments(System.Type type) => type.GetGenericArguments();
                public static System.Type[] Types => new[]
                {
                    typeof(System.Collections.Generic.Dictionary<int, string>),
                    typeof(System.Collections.Generic.List<int>)
                };
            }
            """;
        var compilation = CSharpCompilation.Create(
            $"UnresolvedLength_{Guid.NewGuid():N}",
            [CSharpSyntaxTree.ParseText(generated), CSharpSyntaxTree.ParseText(context)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var assemblyStream = new MemoryStream();
        var emitted = compilation.Emit(assemblyStream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        var assembly = System.Reflection.Assembly.Load(assemblyStream.ToArray());
        Assert.Equal(1, assembly.GetType("Sample")!.GetMethod("Run")!.Invoke(null, null));
    }

    [Fact]
    public void JaggedArrayElement_RetainsItsArrayLengthType()
    {
        var converted = new CSharpToCalorConverter().Convert("""
            public static class Sample
            {
                public static int Run()
                {
                    string[][] rows = new string[][] { new string[] { "a", "b" } };
                    var row = rows[0];
                    return row.Length;
                }
            }
            """);
        Assert.True(converted.Success, string.Join("\n", converted.Issues.Select(issue => issue.Message)));
        var compiled = Program.Compile(converted.CalorSource!, "jagged-length.calr",
            new CompilationOptions { UnknownCallPolicy = UnknownCallPolicy.Strict, StatusWriter = TextWriter.Null });
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics.Errors));
        Assert.DoesNotContain(compiled.Diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.UnknownExternalCall && diagnostic.Message.Contains("get_Length"));
        var runtime = TestHarness.Execute(converted.CalorSource!, "Run");
        Assert.Null(runtime.Exception);
        Assert.Equal(2, runtime.ReturnValue);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public void ArrayLength_DoesNotHoistOutOfLazyOperand(bool take, int expected)
    {
        var converted = new CSharpToCalorConverter().Convert("""
            public static class Sample
            {
                public static int Calls;
                public static int[] Read()
                {
                    Calls++;
                    return new int[] { 1 };
                }
                public static int Run(bool take)
                {
                    bool found = take && Read().Length > 0;
                    return Calls;
                }
            }
            """);
        Assert.True(converted.Success, string.Join("\n", converted.Issues.Select(issue => issue.Message)));
        Assert.Equal(0, converted.InteropPreservationCount);
        var runtime = TestHarness.Execute(converted.CalorSource!, "Run", [take]);
        Assert.Null(runtime.Exception);
        Assert.Equal(expected, runtime.ReturnValue);
    }

    [Theory]
    [InlineData("string", "sbText", "\"hello\"", 5)]
    [InlineData("int[]", "sbArray", "new int[3]", 3)]
    [InlineData("StringBuilder", "builder", "new StringBuilder(\"abcd\")", 4)]
    public void Length_UsesReceiverTypesWithoutNameHeuristics(
        string type, string name, string initializer, int expected)
    {
        var converted = new CSharpToCalorConverter().Convert($$"""
            using System.Text;
            public static class Sample
            {
                public static int Run()
                {
                    {{type}} {{name}} = {{initializer}};
                    return {{name}}.Length;
                }
            }
            """);
        Assert.True(converted.Success, string.Join("\n", converted.Issues.Select(issue => issue.Message)));
        Assert.NotNull(converted.Ast);
        Assert.Equal(0, converted.InteropPreservationCount);
        Assert.DoesNotContain("sb-length", converted.CalorSource);
        var method = Assert.Single(Assert.Single(converted.Ast.Classes).Methods);
        var returned = Assert.IsType<ReturnStatementNode>(method.Body[^1]).Expression;
        if (type == "string")
            Assert.Equal(StringOp.Length, Assert.IsType<StringOperationNode>(returned).Operation);
        else
        {
            Assert.DoesNotContain("(len ", converted.CalorSource);
            Assert.IsType<FieldAccessNode>(returned);
        }
        var emitted = new CSharpEmitter().Emit(converted.Ast);
        Assert.True(GeneratedCSharpCompiler.Validate(emitted).CompilationSuccess);
        var compiled = Program.Compile(converted.CalorSource!, "bcl-length.calr");
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var runtime = TestHarness.Execute(converted.CalorSource!, "Run");
        Assert.Null(runtime.Exception);
        Assert.Equal(expected, runtime.ReturnValue);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BclContracts_CompileAndExecuteWithAndWithoutVerification(bool verify)
    {
        var source = """
            §M{BclContracts}
              §F{Run:pub} (str:s) -> bool
                §E{}
                §Q §C{s.Contains} §A "b" §/C
                §S (== result §C{s.StartsWith} §A "a" §A System.StringComparison.Ordinal §/C)
                §R §C{s.StartsWith} §A "a" §A System.StringComparison.Ordinal §/C
            """;
        var options = new CompilationOptions
        {
            VerifyContracts = verify,
            StatusWriter = TextWriter.Null,
            ContractMode = ContractMode.Debug
        };
        var compiled = Program.Compile(source, "bcl-contract.calr", options);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics.Select(diagnostic => diagnostic.Message)));
        if (verify)
        {
            Assert.NotNull(options.VerificationResults);
            var function = Assert.Single(options.VerificationResults.Functions);
            Assert.Equal(ProofStatus.Proven, Assert.Single(function.PreconditionResults).EffectiveOutcome.Status);
            Assert.Equal(ProofStatus.Assumed, Assert.Single(function.PostconditionResults).EffectiveOutcome.Status);
        }
        var runtime = TestHarness.Execute(source, "Run", ["abc"], options);
        Assert.Null(runtime.Exception);
        Assert.Equal(true, runtime.ReturnValue);
    }

    [Fact]
    public void SbNamedUserReceiver_IsNotAStringBuilderOperation()
    {
        var converted = new CSharpToCalorConverter().Convert("""
            public sealed class Pretender
            {
                public int Append(int value) => value + 1;
            }
            public static class Sample
            {
                public static int Run()
                {
                    var sb = new Pretender();
                    return sb.Append(5);
                }
            }
            """);
        Assert.True(converted.Success, string.Join("\n", converted.Issues.Select(issue => issue.Message)));
        Assert.Equal(0, converted.InteropPreservationCount);
        Assert.DoesNotContain("sb-append", converted.CalorSource);
        Assert.Contains("§C{sb.Append}", converted.CalorSource);
        var runtime = TestHarness.Execute(converted.CalorSource!, "Run",
            options: new CompilationOptions { EnforceEffects = false });
        Assert.Null(runtime.Exception);
        Assert.Equal(6, runtime.ReturnValue);
    }
}
