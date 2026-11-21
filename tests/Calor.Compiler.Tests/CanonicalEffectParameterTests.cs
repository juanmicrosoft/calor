using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Calor.Compiler.Effects;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Reflection;
using System.Runtime.Loader;
using Xunit;

namespace Calor.Compiler.Tests;

[Collection("ProductionPipelineConsole")]
public sealed class CanonicalEffectParameterTests
{
    [Theory]
    [InlineData("082_Lambda.cs")]
    [InlineData("093_Delegates.cs")]
    public void ConvertedOriginalCallbacksKeepStrictContractsMetadataAndRuntime(string filename)
    {
        var source = TestDataLoader.LoadTestFile("Level5_Advanced/" + filename);
        var project = new CSharpToCalorConverter().ConvertProject(source, filename);
        Assert.True(project.Success);
        Assert.False(project.IsSplit);
        var conversion = Assert.Single(project.Outputs).Conversion;
        Assert.Empty(conversion.Losses);
        Assert.Equal(0, conversion.InteropPreservationCount);
        var result = Program.Compile(conversion.CalorSource!);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        var before = CompileCSharp(source);
        var after = CompileCSharp(result.GeneratedCode);
        Assert.Equal(Metadata(before), Metadata(after));
        Assert.Equal(SelectedCalls(before), SelectedCalls(after));
        var module = Parse(conversion.CalorSource!);
        var methods = module.Classes.Single().Methods;
        var apply = methods.Single(method => method.Name == "Apply");
        Assert.Single(apply.EffectParameters);
        Assert.Single(apply.Parameters.Single(parameter => parameter.Row != null).Row!.EffectVariables);
        if (filename.StartsWith("082", StringComparison.Ordinal))
        {
            foreach (var name in new[] { "GetDoubler", "GetAdder", "GetIsPositive" })
            {
                var row = methods.Single(method => method.Name == name).Output!.Row;
                Assert.NotNull(row);
                Assert.True(EffectEnforcementPass.GetDeclaredEffects(row).IsEmpty);
            }
            Assert.Equal("cw", EffectEnforcementPass.GetDeclaredEffects(
                methods.Single(method => method.Name == "GetPrinter").Output!.Row).ToDisplayString());
        }
        else
            // D2: a callback row cannot be omitted (Calor0407); a delegate whose invocations the
            // converter cannot bound declares the sound `! unknown` row, never a binder.
            Assert.All(module.Delegates, declaration =>
            {
                Assert.NotNull(declaration.Effects);
                Assert.True(EffectEnforcementPass.GetDeclaredEffects(declaration.Effects).IsUnknown);
                Assert.Empty(declaration.Effects!.EffectVariables);
            });
        Assert.Equal(Observe(before, filename), Observe(after, filename));
    }

    [Fact]
    public void ConversionUsesIndependentBindersWithoutChangingClrGenericArity()
    {
        var conversion = ConvertNative("""
            using System;
            public static class Callbacks {
                public static T Both<T>(Func<T> first, Func<T> second) {
                    first();
                    return second();
                }
                public static int Both(int value) => value;
            }
            """);
        var method = Parse(conversion).Classes.Single().Methods.Single(method => method.Parameters.Count == 2);
        Assert.Single(method.TypeParameters);
        Assert.Equal(new[] { 1, 2 }, method.EffectParameters.Select(parameter => parameter.Ordinal));
        Assert.Equal(new[] { 0, 1 }, method.Parameters.Select(parameter => parameter.Row!.EffectVariableOrdinals.Single()));
        Assert.Equal(new[] { 0, 1 }, method.Effects!.EffectVariableOrdinals);
        var compiled = Program.Compile(conversion);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        var type = CompileCSharp(compiled.GeneratedCode).GetTypeByMetadataName("Callbacks")!;
        Assert.Equal(new[] { 0, 1 }, type.GetMembers("Both").OfType<IMethodSymbol>().Select(method => method.Arity).Order());
    }

    [Theory]
    [InlineData("Action", "!", true)]
    [InlineData("Action", "! cw", false)]
    [InlineData("Callback", "!", true)]
    [InlineData("Callback", "! cw", false)]
    public void ConvertedRelationChargesTheSelectedCallbackToItsCaller(
        string callbackType, string ownRow, bool rejected)
    {
        var converted = ConvertNative($$"""
            using System;
            public delegate void Callback();
            public static class Callbacks {
                public static void Run({{callbackType}} callback) { callback(); }
            }
            """);
        var native = AppendMethods(converted, $"""
            func Write ()
              ! cw
              Console.WriteLine("called")
            func Caller ()
              {ownRow}
              Run(Write)
            """);
        var result = Program.Compile(native);
        Assert.True(rejected == result.HasErrors, string.Join("\n", result.Diagnostics) + "\n" + native);
        if (rejected)
            Assert.Contains(result.Diagnostics.Errors, error => error.Code == DiagnosticCode.ForbiddenEffect
                && error.Message.Contains("cw", StringComparison.Ordinal));
        else
        {
            var pass = new EffectEnforcementPass(new DiagnosticBag(), UnknownCallPolicy.Strict);
            pass.Enforce(Parse(native));
            Assert.Equal("cw", pass.DeclarationFacts.Values.Single(fact => fact.Name == "Caller")
                .InferredRow.ToCompactDisplayString());
        }
        var unknown = Program.Compile(AppendMethods(converted, $"""
            func Unknown ({callbackType}:callback ! unknown)
              !
              Run(callback)
            """));
        Assert.True(unknown.HasErrors);
        Assert.Contains(unknown.Diagnostics.Errors, error => error.Code == DiagnosticCode.ForbiddenEffect
            && error.Message.Contains("unknown", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Func<int>", "var alias = callback; return alias();")]
    [InlineData("Func<int>", "callback(); return (() => callback())();")]
    [InlineData("Func<int>", "callback(); Consume(callback); return 1;")]
    [InlineData("Receiver", "return callback.Invoke();")]
    public void UnsupportedIdentityOrEscapingRelationsDoNotAcquireBinders(string type, string body)
    {
        var converted = new CSharpToCalorConverter(new() { Fidelity = ConversionFidelity.Lossy,
            ValidateRoundTripCSharp = false }).Convert($$"""
            using System;
            public class Receiver { public int Invoke() => 1; }
            public static class Callbacks {
                public static void Consume(Func<int> callback) {}
                public static int Run({{type}} callback) { {{body}} }
            }
            """);
        Assert.True(converted.Success, string.Join("\n", converted.Issues));
        Assert.NotNull(converted.Ast);
        var method = converted.Ast.Classes.Single(owner => owner.Name == "Callbacks").Methods.Single(method => method.Name == "Run");
        Assert.Empty(method.EffectParameters);
        // D2: a callback parameter still declares a row (Calor0407), but an unsupported relation
        // gets the sound `! unknown` row, never an effect variable.
        Assert.All(method.Parameters.Where(parameter => parameter.Row != null), parameter =>
        {
            Assert.StartsWith("Func<", parameter.TypeName, StringComparison.Ordinal);
            Assert.True(EffectEnforcementPass.GetDeclaredEffects(parameter.Row).IsUnknown);
            Assert.Empty(parameter.Row!.EffectVariables);
        });
    }

    [Theory]
    [InlineData("Func<int>", "return () => 1;", "pure")]
    [InlineData("Action", "return () => Console.WriteLine(\"later\");", "cw")]
    [InlineData("Action", "if (choice) return () => Console.WriteLine(\"later\"); return () => Ignore();", "cw")]
    [InlineData("Func<int>", "return () => Other();", null)]
    [InlineData("Func<int>", "if (choice) return () => 1; return () => Other();", null)]
    [InlineData("Action", "return existing;", null)]
    public void ReturnedRowsUseAllNativeLambdaFactsAndOtherwiseDeclareUnknown(
        string returnType, string body, string? expected)
    {
        var converted = new CSharpToCalorConverter(new() { Fidelity = ConversionFidelity.Lossy,
            ValidateRoundTripCSharp = false }).Convert($$"""
            using System;
            public static class Callbacks {
                private static void Ignore() {}
                private static int Other() => System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(1);
                public static {{returnType}} Make(bool choice, {{returnType}} existing) { {{body}} }
            }
            """);
        Assert.True(converted.Success, string.Join("\n", converted.Issues));
        var output = converted.Ast!.Classes.Single().Methods.Single(method => method.Name == "Make").Output!;
        // D2: a returned callback row cannot be omitted; when the native lambda facts do not
        // bound it (an uncontracted callee, a returned parameter) it is the sound `! unknown`.
        if (expected == null)
        {
            Assert.True(output.Row != null, converted.CalorSource);
            Assert.True(EffectEnforcementPass.GetDeclaredEffects(output.Row).IsUnknown, converted.CalorSource);
        }
        else
        {
            Assert.True(output.Row != null, converted.CalorSource);
            var row = EffectEnforcementPass.GetDeclaredEffects(output.Row);
            Assert.Equal(expected == "pure" ? "[pure]" : expected, row.ToDisplayString());
        }
    }

    [Fact]
    public void ARelationDoesNotAuthorizeUnknownBodyCalleesOrArgumentEvaluation()
    {
        var converted = new CSharpToCalorConverter(new() { Fidelity = ConversionFidelity.Lossy,
            ValidateRoundTripCSharp = false }).Convert("""
            using System;
            public static class Callbacks {
                public static int Run(Func<int,int> callback) {
                    return callback(System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(1));
                }
            }
            """);
        Assert.True(converted.Success);
        // 0.30: the uncontracted ReverseEndianness call makes Run's strict effects Unknown, so
        // the converter declares ! unknown on Run (not the callback relation alone) and the
        // module compiles. The relation still does not authorize the callee: a caller that
        // declares a known row is charged unknown and rejected.
        var run = Parse(converted.CalorSource!).Classes.Single().Methods.Single(method => method.Name == "Run");
        Assert.True(EffectEnforcementPass.GetDeclaredEffects(run.Effects).IsUnknown, converted.CalorSource);
        Assert.Single(run.EffectParameters);
        var compiled = Program.Compile(converted.CalorSource!);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics) + "\n" + converted.CalorSource);
        foreach (var (row, rejected) in new[] { ("!", true), ("! unknown", false) })
        {
            var native = AppendMethods(converted.CalorSource!, $"""
                func Caller () → int
                  {row}
                  return Run((int:value) => value)
                """);
            var caller = Program.Compile(native);
            Assert.True(rejected == caller.HasErrors, string.Join("\n", caller.Diagnostics) + "\n" + native);
            if (rejected)
                Assert.Contains(caller.Diagnostics.Errors, error => error.Code == DiagnosticCode.ForbiddenEffect
                    && error.Message.Contains("'Caller'", StringComparison.Ordinal)
                    && error.Message.Contains("unknown", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActiveConfigurationsKeepTheirOwnBoundCallbackAndInvocationMultiplicity(bool left)
    {
        const string original = """
            using System;
            public static class CallbackConfigurations {
                public static int Run(Func<int,int> Work, int value) {
            #if LEFT
                    return Work(value);
            #else
                    return Work(Work(value));
            #endif
                }
                public static int Work(int value) => value;
            }
            """;
        var symbols = left ? new[] { "LEFT" } : Array.Empty<string>();
        var project = new CSharpToCalorConverter(new()
        {
            ParseOptions = new CSharpParseOptions(LanguageVersion.Preview, preprocessorSymbols: symbols),
            NullableContextOptions = NullableContextOptions.Enable
        }).ConvertProject(original);
        Assert.True(project.Success);
        var conversion = Assert.Single(project.Outputs).Conversion;
        Assert.Equal("preprocessor-disabled", Assert.Single(conversion.Losses).Feature);
        var result = Program.Compile(conversion.CalorSource!);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        var before = CompileCSharp(original, symbols, NullableContextOptions.Enable);
        var after = CompileCSharp(result.GeneratedCode, symbols, NullableContextOptions.Enable);
        Assert.Equal(Metadata(before), Metadata(after));
        Assert.Equal(SelectedCalls(before), SelectedCalls(after));
        Assert.Equal(left ? 1 : 2, SelectedCalls(after).Length);
        Assert.Equal(Observe(before, "conditional"), Observe(after, "conditional"));
    }

    [Fact]
    public void ArgumentEffectsNamedArgumentOrderAndCallbackIdentitiesSurviveSynthesis()
    {
        const string original = """
            using System;
            public static class CallbackOrder {
                private static int Next(int value) { Console.WriteLine(value); return value; }
                private static int Sum(int a, int b) => a + b;
                public static int Run(Func<int,int> first, Func<int,int> second) {
                    return first(Sum(b: Next(2), a: Next(1))) + second(Next(4));
                }
            }
            """;
        var native = ConvertNative(original);
        var method = Parse(native).Classes.Single().Methods.Single(method => method.Name == "Run");
        Assert.Equal("cw", EffectEnforcementPass.GetDeclaredEffects(method.Effects).ToDisplayString());
        Assert.Equal(2, method.EffectParameters.Count);
        var result = Program.Compile(native);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        var before = CompileCSharp(original);
        var after = CompileCSharp(result.GeneratedCode);
        Assert.Equal(Metadata(before), Metadata(after));
        Assert.Equal(SelectedCalls(before), SelectedCalls(after));
        Assert.Equal(Observe(before, "order"), Observe(after, "order"));
    }

    [Fact]
    public void AssumedNativeLambdaFactsRemainAssumedAndDoNotProduceReturnContracts()
    {
        var span = new TextSpan(0, 1, 1, 1);
        var attributes = new AttributeCollection();
        var lambda = new LambdaExpressionNode(span, "lambda", [], null, false,
            new RawCSharpExpressionNode(span, "1"), null, attributes);
        var function = new FunctionNode(span, "factory", "Factory", Visibility.Public, [],
            new OutputNode(span, "Func<int>"), null, [new ReturnStatementNode(span, lambda)], attributes);
        var module = new ModuleNode(span, "module", "Assumptions", [], [function], attributes);
        Assert.True(EffectEnforcementPass.SynthesizeConvertedRows(module, new EffectResolver()));
        Assert.Null(function.Output!.Row);
        var diagnostics = new DiagnosticBag();
        var pass = new EffectEnforcementPass(diagnostics, UnknownCallPolicy.Strict);
        pass.Enforce(module);
        Assert.True(pass.DeclarationFacts[function.Id].InferredRow.IsAssumed);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.AssumedEffects);
    }

    [Fact]
    public void DefaultCompilationErasesOnlyTheBinderAndExecutesBothCallbackCalls()
    {
        var source = """
            module Polymorphic
              func RunTwice <! e> (Func<int>:g ! e) → int
                ! e
                return g() + g()
            """;
        var result = Program.Compile(source);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        var compilation = CSharpCompilation.Create("NativePolymorphic",
            [CSharpSyntaxTree.ParseText(result.GeneratedCode)],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        stream.Position = 0;
        var context = new AssemblyLoadContext("NativePolymorphic", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(stream);
            var method = assembly.GetTypes().SelectMany(type => type.GetMethods())
                .Single(method => method.Name == "RunTwice");
            var calls = 0;
            Func<int> next = () => ++calls;
            Assert.Equal(3, method.Invoke(null, [next]));
            Assert.Equal(2, calls);
            Assert.False(method.IsGenericMethod);
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void CountedEffectBindersAreNotTypeArgumentsAndKeepTheirOrdinals()
    {
        var module = Parse("""
            module Polymorphic
              func RunTwice <! e> (Func<int>:g ! e) → int
                ! e
                return g() + g()
              func Handle <! e> (int:request, Func<int>:next ! e) → int
                ! e
                return RunTwice(next)
            """);
        var function = module.Functions[0];
        Assert.Empty(function.TypeParameters);
        Assert.Equal("e", Assert.Single(function.EffectParameters).Name);
        Assert.Equal(0, function.EffectParameters.Single().Ordinal);
        Assert.Equal(0, Assert.Single(function.Parameters.Single().Row!.EffectVariableOrdinals));
        Assert.Equal(0, Assert.Single(function.Effects!.EffectVariableOrdinals));
        var emitted = new CalorEmitter().Emit(module);
        Assert.Contains("func RunTwice <! e>", emitted);
        Assert.DoesNotContain("eff e", emitted);
        var reparsed = Parse(emitted).Functions[0];
        Assert.Empty(reparsed.TypeParameters);
        Assert.Equal(function.EffectParameters.Single().Ordinal, reparsed.EffectParameters.Single().Ordinal);
        Assert.DoesNotContain("<! e>", new CSharpEmitter().Emit(module));
    }

    [Fact]
    public void ParameterRowsDoNotSwallowTheFollowingTypedParameter()
    {
        var module = Parse("""
            module Polymorphic
              func Copy<T,! e> (Func<T>:first ! e, T:value, Func<T>:last ! e) → T
                ! e
                return value
            """);
        var function = module.Functions.Single();
        Assert.Single(function.TypeParameters);
        Assert.Equal(1, Assert.Single(function.EffectParameters).Ordinal);
        Assert.Equal(new[] { "first", "value", "last" }, function.Parameters.Select(parameter => parameter.Name));
        Assert.Null(function.Parameters[1].Row);
        Assert.Equal(0, function.Parameters[2].Row!.EffectVariableOrdinals.Single());
        var reparse = Parse(new CalorEmitter().Emit(module)).Functions.Single();
        Assert.Equal(3, reparse.Parameters.Count);
        Assert.Equal(1, reparse.EffectParameters.Single().Ordinal);
    }

    [Theory]
    [InlineData("Func<int>:first ! e, @fs:r", 2)]
    [InlineData("Func<int>:first ! e, fs:r, int:value", 2)]
    [InlineData("Func<int>:first ! mut,throw, Func<int>:last ! e", 2)]
    public void ConcreteRowsAndEscapedTypeNamesKeepParameterBoundaries(string parameters, int count)
    {
        var module = Parse($"module Rows\n  func F <! e> ({parameters}) → void\n    ! e,mut,throw,fs:r");
        var function = module.Functions.Single();
        Assert.Equal(count, function.Parameters.Count);
        var emitted = new CalorEmitter().Emit(module);
        var reparsed = Parse(emitted).Functions.Single();
        Assert.Equal(function.Parameters.Select(parameter => parameter.TypeName),
            reparsed.Parameters.Select(parameter => parameter.TypeName));
        Assert.Equal(function.Parameters.Select(parameter => parameter.Name),
            reparsed.Parameters.Select(parameter => parameter.Name));
        Assert.Equal(function.Parameters[0].Row!.Effects, reparsed.Parameters[0].Row!.Effects);
    }

    [Theory]
    [InlineData("func Bad <eff e> () → void")]
    [InlineData("func Bad <! cw> () → void")]
    [InlineData("class Bad <! e>")]
    public void RetiredBindersAndTaxonomyCollisionsAreDiagnosed(string header)
    {
        var diagnostics = new DiagnosticBag();
        _ = new Parser(new Lexer($"module Bad\n  {header}", diagnostics)
            .TokenizeAllForParser(), diagnostics).Parse();
        Assert.True(diagnostics.HasErrors);
    }

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        return module;
    }

    private static string ConvertNative(string source)
    {
        var project = new CSharpToCalorConverter().ConvertProject(source);
        Assert.True(project.Success, string.Join("\n", project.Outputs.SelectMany(output => output.Conversion.Issues)));
        var converted = Assert.Single(project.Outputs).Conversion;
        Assert.Empty(converted.Losses);
        return converted.CalorSource!;
    }

    private static string AppendMethods(string module, string methods)
        => module.TrimEnd() + "\n" + string.Join("\n", methods.Split('\n').Select(line => "    " + line));

    private static CSharpCompilation CompileCSharp(
        string source, string[]? symbols = null, NullableContextOptions nullableContext = NullableContextOptions.Disable)
    {
        var compilation = CSharpCompilation.Create("CallbackWitness",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview, preprocessorSymbols: symbols))],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithNullableContextOptions(nullableContext));
        Assert.DoesNotContain(compilation.GetDiagnostics(),
            diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        return compilation;
    }

    private static string[] Metadata(CSharpCompilation compilation)
    {
        var tree = compilation.SyntaxTrees.Single();
        return CSharpToCalorConverter.DeclaredTypeMetadata(
            compilation.GetSemanticModel(tree), tree.GetCompilationUnitRoot());
    }

    private static string[] SelectedCalls(CSharpCompilation compilation)
    {
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        return tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Select(call => Assert.IsAssignableFrom<IMethodSymbol>(model.GetSymbolInfo(call).Symbol)
                .ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat))
            .Order(StringComparer.Ordinal).ToArray();
    }

    private static string[] Observe(CSharpCompilation compilation, string filename)
    {
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        stream.Position = 0;
        var context = new AssemblyLoadContext("CallbackWitness", isCollectible: true);
        var previous = Console.Out;
        using var output = new StringWriter();
        var observations = new List<string>();
        try
        {
            var assembly = context.LoadFromStream(stream);
            Console.SetOut(output);
            if (filename == "conditional")
            {
                var calls = 0;
                Func<int, int> callback = value => { observations.Add($"callback:{++calls}:{value}"); return value + calls; };
                observations.Add(assembly.GetType("CallbackConfigurations")!.GetMethod("Run")!
                    .Invoke(null, [callback, 3])!.ToString()!);
            }
            else if (filename == "order")
            {
                Func<int, int> first = value => { Console.WriteLine($"first:{value}"); return value + 10; };
                Func<int, int> second = value => { Console.WriteLine($"second:{value}"); return value + 20; };
                var value = assembly.GetType("CallbackOrder")!.GetMethod("Run")!.Invoke(null, [first, second]);
                Assert.Equal(37, value);
                Assert.Equal(string.Join(Environment.NewLine, "2", "1", "first:3", "4", "second:4", ""), output.ToString());
                observations.Add($"{value}:{output}");
            }
            else if (filename.StartsWith("082", StringComparison.Ordinal))
            {
                var type = assembly.GetType("Lambda.Lambdas")!;
                Assert.All(type.GetMethods(BindingFlags.Public | BindingFlags.Static),
                    method => Assert.False(method.IsGenericMethod));
                var doubler = (Func<int, int>)type.GetMethod("GetDoubler")!.Invoke(null, null)!;
                var adder = (Func<int, int, int>)type.GetMethod("GetAdder")!.Invoke(null, null)!;
                var positive = (Func<int, bool>)type.GetMethod("GetIsPositive")!.Invoke(null, null)!;
                var printer = (Action<string>)type.GetMethod("GetPrinter")!.Invoke(null, null)!;
                Assert.Equal("", output.ToString());
                observations.Add($"{doubler(7)}:{adder(2, 5)}:{positive(-1)}:{positive(1)}");
                printer("later");
                observations.Add(output.ToString());
                var calls = 0;
                Func<int, int> callback = value => { observations.Add($"apply:{++calls}:{value}"); return value + calls; };
                var apply = type.GetMethod("Apply")!;
                observations.Add(apply.Invoke(null, [callback, 4])!.ToString()!);
                Assert.Equal(1, calls);
                Capture(() => apply.Invoke(null, [null, 4]));
                Func<int, int> throwing = _ => throw new InvalidOperationException("callback failure");
                Capture(() => apply.Invoke(null, [throwing, 4]));
            }
            else
            {
                var type = assembly.GetType("Delegates.Operations")!;
                Assert.All(type.GetMethods(BindingFlags.Public | BindingFlags.Static),
                    method => Assert.False(method.IsGenericMethod));
                var apply = type.GetMethod("Apply")!;
                var operationType = apply.GetParameters()[0].ParameterType;
                var count = 0;
                Func<int, int, int> operation = (left, right) => { observations.Add($"op:{++count}:{left}:{right}"); return left - right; };
                observations.Add(apply.Invoke(null, [Adapt(operationType, operation), 9, 2])!.ToString()!);
                Assert.Equal(1, count);
                Capture(() => apply.Invoke(null, [null, 9, 2]));
                var filter = type.GetMethod("Filter")!;
                var predicateType = filter.GetParameters()[1].ParameterType;
                var numbers = new[] { 1, -2, 3 };
                Func<int, bool> predicate = value => { observations.Add($"pred:{value}"); return value > 0; };
                var filtered = (int[])filter.Invoke(null, [numbers, Adapt(predicateType, predicate)])!;
                Assert.Equal(new[] { 1, 3 }, filtered);
                Assert.Equal(6, observations.Count(item => item.StartsWith("pred:", StringComparison.Ordinal)));
                observations.Add(string.Join(",", filtered));
                Assert.Empty((int[])filter.Invoke(null, [Array.Empty<int>(), null])!);
                Capture(() => filter.Invoke(null, [numbers, null]));
                Capture(() => filter.Invoke(null, [null, Adapt(predicateType, predicate)]));
                var calls = 0;
                Func<int, bool> inconsistent = value => { observations.Add($"changing:{++calls}:{value}"); return calls > 3; };
                Capture(() => filter.Invoke(null, [numbers, Adapt(predicateType, inconsistent)]));
                Assert.Equal(4, calls);
                Func<int, bool> throwing = value => { observations.Add($"throw:{value}"); throw new InvalidOperationException("predicate failure"); };
                Capture(() => filter.Invoke(null, [numbers, Adapt(predicateType, throwing)]));
            }
            return observations.ToArray();
        }
        finally
        {
            Console.SetOut(previous);
            context.Unload();
        }

        static Delegate Adapt(Type type, Delegate callback)
            => Delegate.CreateDelegate(type, callback.Target, callback.Method);
        void Capture(Action action)
        {
            var exception = Assert.Throws<TargetInvocationException>(action).InnerException!;
            observations.Add($"{exception.GetType().FullName}:{exception.Message}");
        }
    }
}
