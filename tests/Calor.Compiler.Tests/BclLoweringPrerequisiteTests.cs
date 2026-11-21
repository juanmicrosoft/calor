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

    [Fact]
    public void MetadataArrayResults_AreIndexedByCallerAndExpressionSpan()
    {
        var source = """
            module MetadataArrays MetadataArrays block
              using ReflectionType = System.Type
              func Known (ReflectionType:receiver) → int
                !
                let arguments = receiver.GetGenericArguments()
                return arguments.Length

              func Unknown (Missing:receiver) → int
                !
                let arguments = receiver.GetGenericArguments()
                return arguments.Length
            """;
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors);
        var graph = CallGraphAnalysis.Build(module);
        var known = Assert.IsType<BindStatementNode>(module.Functions[0].Body[0]).Initializer!;
        var unknown = Assert.IsType<BindStatementNode>(module.Functions[1].Body[0]).Initializer!;
        var array = Assert.IsType<Calor.Compiler.Binding.BoundTypes.ArrayBoundType>(
            graph.BoundArrayReceiverType(module.Functions[0].Id, known.Span));
        Assert.Equal("System.Type", array.ElementType.DisplayString);
        Assert.Null(graph.BoundArrayReceiverType(module.Functions[1].Id, known.Span));
        Assert.Null(graph.BoundArrayReceiverType(module.Functions[1].Id, unknown.Span));
        var compiled = Program.Compile(source, "scoped-arrays.calr",
            new CompilationOptions { UnknownCallPolicy = UnknownCallPolicy.Strict, StatusWriter = TextWriter.Null });
        Assert.Single(compiled.Diagnostics.Where(diagnostic =>
            diagnostic.Code == DiagnosticCode.UnknownExternalCall && diagnostic.Message.Contains("get_Length")));
    }

    [Fact]
    public void MetadataArrayResult_IsInvalidatedByMutableRebinding()
    {
        // 0.30 fixes a local's storage type at its declaration, so the 0.29 program that rebound
        // `values` from Type[] to int is now rejected outright (CS0029 at the rebind): no stale
        // array fact can reach an accepted program through an inferred local.
        var rejected = Program.Compile("""
            module MetadataArrays MetadataArrays block
              func Run (System.Type:receiver) → int
                ! unknown
                var values = receiver.GetGenericArguments()
                set values 3
                return values.Length
            """, "retyped-array.calr",
            new CompilationOptions { UnknownCallPolicy = UnknownCallPolicy.Strict, StatusWriter = TextWriter.Null });
        Assert.Contains(rejected.Diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.CodeGenCompilationError && diagnostic.Message.Contains("CS0029"));

        // `dynamic` storage still admits a value of another type, so the invariant is exercised
        // there: the producer's Type[] fact makes `values.Length` an array length only until the
        // variable is rebound. Without the rebind, Length is the array's (only the producing
        // call is unknown); after it, Length is a dynamic member read with no contract.
        const string template = """
            module MetadataArrays MetadataArrays block
              func Run (System.Type:receiver) → int
                !
                var dynamic:values = receiver.GetGenericArguments()
            REBIND
                return values.Length
            """;
        var unchanged = Program.Compile(template.Replace("REBIND\n", ""), "kept-array.calr",
            new CompilationOptions { UnknownCallPolicy = UnknownCallPolicy.Strict, StatusWriter = TextWriter.Null });
        Assert.Contains(unchanged.Diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.UnknownExternalCall && diagnostic.Message.Contains("GetGenericArguments"));
        Assert.DoesNotContain(unchanged.Diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.UnknownExternalCall && diagnostic.Message.Contains("get_Length"));

        var compiled = Program.Compile(template.Replace("REBIND", "    set values 3"), "rebound-array.calr",
            new CompilationOptions { UnknownCallPolicy = UnknownCallPolicy.Strict, StatusWriter = TextWriter.Null });
        Assert.Contains(compiled.Diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.UnknownExternalCall && diagnostic.Message.Contains("get_Length"));
    }

    [Fact]
    public void MutableCapturedArrayResult_IsNotADeferredTypeGuarantee()
    {
        // A lambda runs later, after any rebind of a mutable capture, so the capture does not
        // carry the producer's array fact into it. `dynamic` storage is the 0.30 spelling that
        // can still be rebound to another type (an inferred local's type is fixed).
        const string template = """
            module MetadataArrays MetadataArrays block
              func Run (System.Type:receiver) → int
                !
                BINDING dynamic:values = receiver.GetGenericArguments()
                let read = () => values.Length
            REBIND
                return read()
            """;
        var compiled = Program.Compile(
            template.Replace("BINDING", "var").Replace("REBIND", "    set values 3"), "captured-array.calr",
            new CompilationOptions { UnknownCallPolicy = UnknownCallPolicy.Strict, StatusWriter = TextWriter.Null });
        Assert.DoesNotContain(compiled.Diagnostics, diagnostic =>
            diagnostic.Code.StartsWith("Calor01", StringComparison.Ordinal));
        Assert.Contains(compiled.Diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.UnknownExternalCall && diagnostic.Message.Contains("get_Length"));

        // Control: an immutable capture cannot be rebound, so it keeps the array fact.
        var immutable = Program.Compile(
            template.Replace("BINDING", "let").Replace("REBIND\n", ""), "captured-immutable-array.calr",
            new CompilationOptions { UnknownCallPolicy = UnknownCallPolicy.Strict, StatusWriter = TextWriter.Null });
        Assert.DoesNotContain(immutable.Diagnostics, diagnostic =>
            diagnostic.Code.StartsWith("Calor01", StringComparison.Ordinal));
        Assert.DoesNotContain(immutable.Diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.UnknownExternalCall && diagnostic.Message.Contains("get_Length"));
    }

    [Fact]
    public void InstanceArrayCallThroughType_DoesNotEstablishAProducerType()
    {
        var source = """
            module MetadataArrays MetadataArrays block
              func Run () → int
                !
                let values = System.Type.GetGenericArguments()
                return values.Length
            """;
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors);
        var initializer = Assert.IsType<BindStatementNode>(module.Functions[0].Body[0]).Initializer!;
        var graph = CallGraphAnalysis.Build(module);
        Assert.Null(graph.BoundArrayReceiverType(module.Functions[0].Id, initializer.Span));
        var compiled = Program.Compile(source, "type-array-call.calr",
            new CompilationOptions { UnknownCallPolicy = UnknownCallPolicy.Strict, StatusWriter = TextWriter.Null });
        Assert.Contains(compiled.Diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.UnknownExternalCall && diagnostic.Message.Contains("get_Length"));
    }

    [Fact]
    public void MetadataArrayCallWithArguments_RemainsUnknownWithoutExactOverload()
    {
        // 0.29 established array results only for zero-argument calls, so any call with
        // arguments left `.Length` unknown. 0.30 binds `receiver.M(args)` by Roslyn overload
        // resolution: an exactly selected overload's array return type is a fact, and a call
        // whose overload cannot be selected exactly still establishes nothing.
        var converted = new CSharpToCalorConverter().Convert("""
            using System;
            public static class Sample
            {
                public static int Run(Type receiver) => receiver.GetCustomAttributes(false).Length;
            }
            """);
        Assert.NotNull(converted.Ast);
        var method = Assert.Single(Assert.Single(converted.Ast.Classes).Methods);
        var length = Assert.IsType<FieldAccessNode>(Assert.IsType<ReturnStatementNode>(method.Body[^1]).Expression);
        Assert.Equal("Length", length.FieldName);
        Assert.Contains("! unknown", converted.CalorSource);

        // With the row tightened to pure, only the producing call is unknown: the selected
        // GetCustomAttributes(bool) returns object[], whose Length is the array's.
        var tightened = Program.Compile(
            System.Text.RegularExpressions.Regex.Replace(converted.CalorSource!, @"(?m)^(\s*)! unknown$", "$1!"),
            "exact-array-call.calr",
            new CompilationOptions { UnknownCallPolicy = UnknownCallPolicy.Strict, StatusWriter = TextWriter.Null });
        Assert.Contains(tightened.Diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.UnknownExternalCall && diagnostic.Message.Contains("GetCustomAttributes"));
        Assert.DoesNotContain(tightened.Diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.UnknownExternalCall && diagnostic.Message.Contains("get_Length"));

        // An argument of unknown type selects no overload, so no array type is established
        // and `.Length` stays an unknown member read.
        var compiled = Program.Compile("""
            module MetadataArrays MetadataArrays block
              func Run (System.Type:receiver, Missing:inherit) → int
                !
                return receiver.GetCustomAttributes(inherit).Length
            """, "unsupported-array-call.calr",
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
        Assert.Null(graph.BoundArrayReceiverType(callerId, width.Target.Span));
        var runtime = TestHarness.Execute(converted.CalorSource!, "Run");
        Assert.Null(runtime.Exception);
        Assert.Equal(124, runtime.ReturnValue);
    }

    [Fact]
    public void MissingCrossFileLambdaType_RetainsLegacyBehaviorWithoutMetadataProof()
    {
        const string missingSource = """
            using System.Linq;
            public static class Sample
            {
                public static int Run() =>
                    External.Types.Count(type => type.GetGenericArguments().Length == 2);
            }
            """;
        AssertDefaultPreservation(missingSource);
        var missingInput = new CSharpToCalorConverter(
            new() { ValidateRoundTripCSharp = false }).Convert(missingSource);
        Assert.NotNull(missingInput.Ast);
        Assert.Equal(0, missingInput.InteropPreservationCount);
        AssertLegacyLengthWithoutMetadataProof(missingInput);
        const string typedSource = """
            using System.Linq;
            public static class Sample
            {
                public static int Run() =>
                    External.Types.Count((System.Type type) => External.Arguments(type).Length == 2);
            }
            """;
        AssertDefaultPreservation(typedSource);
        var converted = new CSharpToCalorConverter(
            new() { ValidateRoundTripCSharp = false }).Convert(typedSource);
        Assert.NotNull(converted.Ast);
        Assert.Equal(0, converted.InteropPreservationCount);
        AssertLegacyLengthWithoutMetadataProof(converted);
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

        // The receiver's type is a nameless error type, so the converter keeps the legacy
        // string-length node in its AST. 0.30 prints that node as native `.Length` (the old
        // `(len x)` spelling is gone), so the claim is checked on the AST and by compiling:
        // with the row tightened to pure, the Length read is still an unknown member read.
        static void AssertLegacyLengthWithoutMetadataProof(ConversionResult conversion)
        {
            Assert.Contains(EnumerateNodes(conversion.Ast!), node =>
                node is StringOperationNode { Operation: StringOp.Length });
            Assert.Contains(").Length == 2", conversion.CalorSource);
            Assert.Contains("! unknown", conversion.CalorSource);
            var tightened = Program.Compile(
                System.Text.RegularExpressions.Regex.Replace(conversion.CalorSource!, @"(?m)^(\s*)! unknown$", "$1!"),
                "missing-lambda-type.calr",
                new CompilationOptions { UnknownCallPolicy = UnknownCallPolicy.Strict, StatusWriter = TextWriter.Null });
            Assert.Contains(tightened.Diagnostics, diagnostic =>
                diagnostic.Code == DiagnosticCode.UnknownExternalCall && diagnostic.Message.Contains("get_Length"));
        }

        static IEnumerable<AstNode> EnumerateNodes(AstNode node) =>
            new[] { node }.Concat(Calor.Compiler.Analysis.RecursiveAstWalker.GetAllChildren(node).SelectMany(EnumerateNodes));

        static void AssertDefaultPreservation(string original)
        {
            var migration = new CSharpToCalorConverter().Convert(original);
            Assert.True(migration.Success, string.Join("\n", migration.Issues.Select(issue => issue.Message)));
            Assert.Equal(1, migration.InteropPreservationCount);
            Assert.Contains(migration.Losses, loss => loss.Feature == "whole-file-preservation");
            Assert.DoesNotContain(migration.Losses, loss => loss.IsSemanticLoss);
            Assert.Equal(original, new CSharpEmitter().Emit(migration.Ast!));
        }
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
            module BclContracts BclContracts block
              func Run (string:s) → bool
                !
                requires s.Contains("b")
                => (result == s.StartsWith("a", System.StringComparison.Ordinal))
                return s.StartsWith("a", System.StringComparison.Ordinal)
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
        Assert.Contains("sb.Append(5)", converted.CalorSource);
        var runtime = TestHarness.Execute(converted.CalorSource!, "Run",
            options: new CompilationOptions { EnforceEffects = false });
        Assert.Null(runtime.Exception);
        Assert.Equal(6, runtime.ReturnValue);
    }
}
