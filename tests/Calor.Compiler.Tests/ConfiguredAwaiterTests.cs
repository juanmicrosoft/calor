using System.Reflection;
using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Binding.BoundTypes;
using Calor.Compiler.Binding.Metadata;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

[Collection("ProductionPipelineConsole")]
public sealed class ConfiguredAwaiterTests
{
    private const string Original = """
        module ConfigureAwaitLimit
          using System.Threading.Tasks
          func Read async () → int
            !
            return await Task.FromResult(42).ConfigureAwait(false)
        """;

    [Fact]
    public async Task OriginalNegative_NowBindsEnforcesAndExecutes()
    {
        var result = Program.Compile(Original);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code.StartsWith("Calor04", StringComparison.Ordinal));
        var method = Compile(result.GeneratedCode).GetTypes().SelectMany(type => type.GetMethods())
            .Single(method => method.Name == "Read");
        Assert.Equal(42, await Assert.IsType<Task<int>>(method.Invoke(null, null)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectedMetadata_RetainsConstructedCarrierAwaiterResultAndSourceSpans(bool configure)
    {
        var source = configure ? Original.Replace("(false)", "(true)", StringComparison.Ordinal) : Original;
        var (bound, diagnostics) = Bind(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var nodes = BoundNodeHelpers.DescendantsAndSelf(bound).ToArray();
        var calls = nodes.OfType<BoundCallExpression>().ToArray();
        var factory = Assert.Single(calls, call => call.ResolvedMetadataMethod?.Name == "FromResult");
        var selected = Assert.IsAssignableFrom<IMethodSymbol>(factory.ResolvedMetadataMethod);
        Assert.Equal("System.Threading.Tasks.Task", selected.ContainingType.ToDisplayString());
        Assert.Equal(SpecialType.System_Int32, Assert.Single(selected.TypeArguments).SpecialType);
        var task = Assert.IsType<GenericInstantiationBoundType>(factory.Type);
        Assert.Equal("System.Threading.Tasks.Task", task.Definition.QualifiedName);
        Assert.Equal("System.Threading.Tasks.Task<int>", task.DisplayString);
        Assert.True(SymbolEqualityComparer.Default.Equals(selected.ReturnType, task.RoslynSymbol));
        var configureCall = Assert.Single(calls, call => call.ResolvedMetadataMethod?.Name == "ConfigureAwait");
        Assert.Same(factory, configureCall.Receiver);
        var configureMethod = Assert.IsAssignableFrom<IMethodSymbol>(configureCall.ResolvedMetadataMethod);
        Assert.True(SymbolEqualityComparer.Default.Equals(task.RoslynSymbol, configureMethod.ContainingType));
        Assert.Equal(SpecialType.System_Boolean, Assert.Single(configureMethod.Parameters).Type.SpecialType);
        Assert.Equal(configure, Assert.IsType<BoundBoolLiteral>(Assert.Single(configureCall.Arguments)).Value);
        Assert.Equal("ConfigureAwait", Slice(source, configureCall.CalleeSpan));
        Assert.Equal("Task.FromResult(42).ConfigureAwait(" + configure.ToString().ToLowerInvariant() + ")",
            Slice(source, configureCall.Span));
        var configured = Assert.IsType<GenericInstantiationBoundType>(configureCall.Type);
        Assert.Equal("System.Runtime.CompilerServices.ConfiguredTaskAwaitable", configured.Definition.QualifiedName);
        Assert.Equal("System.Runtime.CompilerServices.ConfiguredTaskAwaitable<int>", configured.DisplayString);
        Assert.True(SymbolEqualityComparer.Default.Equals(configureMethod.ReturnType, configured.RoslynSymbol));
        var awaited = Assert.Single(nodes.OfType<BoundStructuralExpression>(),
            node => node.NodeTypeName == nameof(AwaitExpressionNode));
        Assert.Same(configureCall, Assert.Single(awaited.Children));
        var getAwaiter = Assert.IsAssignableFrom<IMethodSymbol>(awaited.Metadata["GetAwaiter"]);
        var getResult = Assert.IsAssignableFrom<IMethodSymbol>(awaited.Metadata["GetResult"]);
        var completed = Assert.IsAssignableFrom<IPropertySymbol>(awaited.Metadata["IsCompleted"]);
        Assert.True(SymbolEqualityComparer.Default.Equals(configured.RoslynSymbol, getAwaiter.ContainingType));
        Assert.Equal("System.Runtime.CompilerServices.ConfiguredTaskAwaitable<int>.ConfiguredTaskAwaiter",
            getAwaiter.ReturnType.ToDisplayString());
        Assert.True(SymbolEqualityComparer.Default.Equals(getAwaiter.ReturnType, getResult.ContainingType));
        Assert.True(SymbolEqualityComparer.Default.Equals(getResult.ContainingType, completed.ContainingType));
        Assert.Equal(SpecialType.System_Boolean, completed.Type.SpecialType);
        Assert.Equal(SpecialType.System_Int32, getResult.ReturnType.SpecialType);
        Assert.Equal("System.Runtime.CompilerServices.ConfiguredTaskAwaitable`1+ConfiguredTaskAwaiter",
            EffectResolverKey.FromBoundReceiver(MetadataBinderResult.ToBoundType(getAwaiter.ReturnType), "GetResult").DeclaringType);
        Assert.Equal("await " + Slice(source, configureCall.Span), Slice(source, awaited.Span));
        Assert.False(awaited.HasUnknownInferredType);
        Assert.Equal("int", awaited.Type.DisplayString);
        var key = EffectResolverKey.FromBoundReceiver(task, configureMethod.Name, ["bool"]);
        Assert.Equal("System.Threading.Tasks.Task`1", key.DeclaringType);
        Assert.False(key.FromStringFallback);
        var effect = new EffectResolver().Resolve(key);
        Assert.NotEqual(EffectResolutionStatus.Unknown, effect.Status);
        Assert.True(effect.Effects.IsEmpty);
        Assert.Contains("bcl-runtime.calor-effects.json", effect.Source);
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        var result = Compile(compiled.GeneratedCode).GetTypes().SelectMany(type => type.GetMethods())
            .Single(method => method.Name == "Read");
        Assert.Equal(42, await Assert.IsType<Task<int>>(result.Invoke(null, null)));
    }

    public static IEnumerable<object?[]> Payloads()
    {
        foreach (var configure in new[] { false, true })
        {
            yield return ["int", Task.FromResult(42), 42, configure];
            yield return ["string", Task.FromResult("42"), "42", configure];
            yield return ["string?", Task.FromResult<string?>(null), null, configure];
            yield return ["bool", Task.FromResult(true), true, configure];
            var array = new[] { 42 };
            yield return ["int[]", Task.FromResult(array), array, configure];
            var list = new List<int> { 42 };
            yield return ["List<int>", Task.FromResult(list), list, configure];
            yield return ["int?", Task.FromResult<int?>(42), 42, configure];
            yield return ["int?", Task.FromResult<int?>(null), null, configure];
            var nested = Task.FromResult(42);
            yield return ["Task<int>", Task.FromResult(nested), nested, configure];
        }
    }

    [Theory]
    [MemberData(nameof(Payloads))]
    public async Task GenericPayloads_AwaitExactlyOneConstructedCarrier(
        string payload, Task pending, object? expected, bool configure)
    {
        var source = $$"""
            module ConfigureAwaitPayload
              using System.Threading.Tasks
              using System.Collections.Generic
              func Read async (Task<{{payload}}>:pending) → Task<{{payload}}>
                !
                return await pending.ConfigureAwait({{configure.ToString().ToLowerInvariant()}})
            """;
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        var method = Compile(compiled.GeneratedCode).GetTypes().SelectMany(type => type.GetMethods())
            .Single(method => method.Name == "Read");
        Assert.Equal(pending.GetType(), method.ReturnType);
        var observation = Assert.IsAssignableFrom<Task>(method.Invoke(null, [pending]));
        await observation;
        Assert.Equal(expected, method.ReturnType.GetProperty("Result")!.GetValue(observation));
        var (bound, diagnostics) = Bind(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var awaited = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundStructuralExpression>(),
            node => node.NodeTypeName == nameof(AwaitExpressionNode));
        var getResult = Assert.IsAssignableFrom<IMethodSymbol>(awaited.Metadata["GetResult"]);
        Assert.False(awaited.HasUnknownInferredType);
        if (payload == "int[]")
            Assert.IsType<ArrayBoundType>(awaited.Type);
        if (payload is "List<int>" or "Task<int>")
            Assert.IsType<GenericInstantiationBoundType>(awaited.Type);
        Assert.Equal(awaited.Type.DisplayString, getResult.ReturnType.ToDisplayString());
        if (payload == "string?")
            Assert.Equal(Microsoft.CodeAnalysis.NullableAnnotation.Annotated, getResult.ReturnType.NullableAnnotation);
    }

    [Theory]
    [InlineData("Task.FromResult(42).ConfigureAwait(42)")]
    [InlineData("Task.FromResult(42).ConfigureAwait(\"false\")")]
    [InlineData("Task.FromResult(42).ConfigureAwait()")]
    [InlineData("Task.FromResult(42).ConfigureAwait(false, true)")]
    [InlineData("42.ConfigureAwait(false)")]
    [InlineData("missing.ConfigureAwait(false)")]
    public void InvalidReceiversAndOverloads_DoNotAcquireTheNativeContract(string expression)
    {
        var source = Original.Replace("Task.FromResult(42).ConfigureAwait(false)", expression, StringComparison.Ordinal);
        var (bound, _) = Bind(source);
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>(),
            node => Slice(source, node.CalleeSpan) == "ConfigureAwait");
        Assert.Null(call.ResolvedMetadataMethod);
        var compiled = Program.Compile(source);
        Assert.True(compiled.HasErrors);
        Assert.Contains(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
        Assert.Contains(compiled.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Theory]
    [InlineData("", true, false)]
    [InlineData("cw", false, false)]
    [InlineData("", true, true)]
    [InlineData("cw", false, true)]
    public async Task ReceiverAndArgumentEffects_AreChargedAndEvaluatedOnceInOrder(
        string row, bool forbidden, bool effectOnArgument)
    {
        var source = $$"""
            module ConfigureAwaitOrder
              using System.Threading.Tasks
              using System.Collections.Generic
              func Receiver private (List<int>:trace) → Task<int>
                ! mut{{(effectOnArgument ? "" : ", cw")}}
                {{(effectOnArgument ? "" : "System.Console.Write(\"\")")}}
                trace.Add(1)
                return Task.FromResult(42)
              func Argument private (List<int>:trace) → bool
                ! mut{{(effectOnArgument ? ", cw" : "")}}
                {{(effectOnArgument ? "System.Console.Write(\"\")" : "")}}
                trace.Add(2)
                return false
              func Read async (List<int>:trace) → int
                ! mut{{(row.Length == 0 ? "" : ", " + row)}}
                return await Receiver(trace).ConfigureAwait(Argument(trace))
            """;
        var compiled = Program.Compile(source);
        Assert.Equal(forbidden, compiled.HasErrors);
        Assert.DoesNotContain(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
        if (forbidden)
        {
            Assert.Contains(compiled.Diagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.ForbiddenEffect && diagnostic.Message.Contains("'cw'", StringComparison.Ordinal));
            return;
        }
        var method = Compile(compiled.GeneratedCode).GetTypes().SelectMany(type => type.GetMethods())
            .Single(method => method.Name == "Read");
        var trace = new List<int>();
        Assert.Equal(42, await Assert.IsType<Task<int>>(method.Invoke(null, [trace])));
        Assert.Equal([1, 2], trace);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NonGenericTask_UsesTheVoidAwaiterContract(bool configure)
    {
        var source = $$"""
            module ConfigureAwaitVoid
              using System.Threading.Tasks
              func Read async (Task:pending) → void
                !
                await pending.ConfigureAwait({{configure.ToString().ToLowerInvariant()}})
            """;
        var (bound, diagnostics) = Bind(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var awaited = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundStructuralExpression>(),
            node => node.NodeTypeName == nameof(AwaitExpressionNode));
        Assert.Equal(SpecialType.System_Void,
            Assert.IsAssignableFrom<IMethodSymbol>(awaited.Metadata["GetResult"]).ReturnType.SpecialType);
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        var method = Compile(compiled.GeneratedCode).GetTypes().SelectMany(type => type.GetMethods())
            .Single(method => method.Name == "Read");
        Assert.Equal(typeof(Task), method.ReturnType);
        await Assert.IsAssignableFrom<Task>(method.Invoke(null, [Task.CompletedTask]));
    }

    [Theory]
    [InlineData("None")]
    [InlineData("ContinueOnCapturedContext")]
    public async Task ActualOptionsOverload_KeepsItsEnumParameter(string option)
    {
        var source = Original.Replace("false", $"ConfigureAwaitOptions.{option}", StringComparison.Ordinal);
        var (bound, diagnostics) = Bind(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var method = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>(),
            call => call.ResolvedMetadataMethod?.Name == "ConfigureAwait").ResolvedMetadataMethod!;
        Assert.Equal("System.Threading.Tasks.ConfigureAwaitOptions", Assert.Single(method.Parameters).Type.ToDisplayString());
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        var read = Compile(compiled.GeneratedCode).GetTypes().SelectMany(type => type.GetMethods())
            .Single(candidate => candidate.Name == "Read");
        Assert.Equal(42, await Assert.IsType<Task<int>>(read.Invoke(null, null)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InferredLocals_RetainConstructedMetadata(bool configure)
    {
        var source = $$"""
            module ConfigureAwaitLocal
              using System.Threading.Tasks
              func Read async () → int
                !
                let pending = Task.FromResult(42)
                let configured = pending.ConfigureAwait({{configure.ToString().ToLowerInvariant()}})
                return await configured
            """;
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        var read = Compile(compiled.GeneratedCode).GetTypes().SelectMany(type => type.GetMethods())
            .Single(candidate => candidate.Name == "Read");
        Assert.Equal(42, await Assert.IsType<Task<int>>(read.Invoke(null, null)));
    }

    [Theory]
    [InlineData("ValueTask<int>", "int", false)]
    [InlineData("ValueTask<int>", "int", true)]
    [InlineData("ValueTask", "void", false)]
    [InlineData("ValueTask", "void", true)]
    public async Task ValueTaskConfiguration_UsesTheExistingNamespaceAuthority(string carrier, string result, bool configure)
    {
        var source = $$"""
            module ConfigureAwaitValueTask
              using System.Threading.Tasks
              func Read async ({{carrier}}:pending) → {{result}}
                !
                {{(result == "void" ? "" : "return ")}}await pending.ConfigureAwait({{configure.ToString().ToLowerInvariant()}})
            """;
        var (bound, diagnostics) = Bind(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var nodes = BoundNodeHelpers.DescendantsAndSelf(bound).ToArray();
        var call = Assert.Single(nodes.OfType<BoundCallExpression>());
        Assert.Equal("ConfigureAwait", call.ResolvedMetadataMethod?.Name);
        var awaited = Assert.Single(nodes.OfType<BoundStructuralExpression>());
        Assert.Equal(result, Assert.IsAssignableFrom<IMethodSymbol>(awaited.Metadata["GetResult"]).ReturnType.ToDisplayString());
        var authority = new EffectResolver().Resolve(EffectResolverKey.FromBoundReceiver(
            MetadataBinderResult.ToBoundType(call.ResolvedMetadataMethod!.ContainingType), "ConfigureAwait", ["bool"]));
        Assert.True(authority.Effects.IsEmpty);
        Assert.Contains("bcl-runtime.calor-effects.json", authority.Source);
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        var method = Compile(compiled.GeneratedCode).GetTypes().SelectMany(type => type.GetMethods())
            .Single(candidate => candidate.Name == "Read");
        object input = result == "void" ? ValueTask.CompletedTask : ValueTask.FromResult(42);
        var observed = Assert.IsAssignableFrom<Task>(method.Invoke(null, [input]));
        await observed;
        if (result != "void")
            Assert.Equal(42, await Assert.IsType<Task<int>>(observed));
    }

    [Theory]
    [InlineData("")]
    [InlineData("cw")]
    public void SourceCompetitor_CannotInheritTheFrameworkPureContract(string row)
    {
        var source = $$"""
            module ConfigureAwaitCompetitor
              using System.Threading.Tasks
              class Rival
                func ConfigureAwait (bool:configure) → Task<int>
                  ! {{row}}
                  {{(row.Length == 0 ? "" : "System.Console.Write(\"\")")}}
                  return Task.FromResult(42)
              func Read async (Rival:pending) → int
                !
                return await pending.ConfigureAwait(false)
            """;
        var (bound, diagnostics) = Bind(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>(),
            node => Slice(source, node.CalleeSpan) == "ConfigureAwait");
        Assert.NotNull(call.ResolvedSymbol);
        Assert.Null(call.ResolvedMetadataMethod);
        var compiled = Program.Compile(source);
        Assert.Equal(row.Length > 0, compiled.HasErrors);
        if (row.Length > 0)
            Assert.Contains(compiled.Diagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.ForbiddenEffect && diagnostic.Message.Contains("'cw'", StringComparison.Ordinal));
    }

    [Fact]
    public void CustomAwaitable_AndForeignContext_DoNotFabricateNativeAwaiterIdentity()
    {
        const string source = """
            module CustomAwaitable
              class Rival<T>
                func GetAwaiter () → int
                  ! cw
                  System.Console.Write("")
                  return 42
              func Read async (Rival<int>:pending) → int
                !
                return await pending
            """;
        var (bound, _) = Bind(source);
        var awaited = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundStructuralExpression>());
        Assert.Null(awaited.Metadata["GetAwaiter"]);
        Assert.Null(awaited.Metadata["GetResult"]);
        Assert.True(awaited.HasUnknownInferredType);
        Assert.True(Program.Compile(source).HasErrors);

        using var context = MetadataContext.Create();
        var foreign = CSharpCompilation.Create("NotInMetadataContext",
            [CSharpSyntaxTree.ParseText("""
                public class Rival<T>
                {
                    public System.Runtime.CompilerServices.TaskAwaiter<T> GetAwaiter()
                        => System.Threading.Tasks.Task.FromResult(default(T)!).GetAwaiter();
                }
                """)], GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var rival = foreign.GetTypeByMetadataName("Rival`1")!
            .Construct(foreign.GetSpecialType(SpecialType.System_Int32));
        Assert.Null(new MetadataBinder(context).ResolveAwait(rival));
        Assert.Null(new MetadataBinder(context).ResolveAwait(context.TryResolveType("System.Int32")!));
        Assert.Null(new MetadataBinder(context).ResolveAwait(
            context.TryResolveType("System.Runtime.CompilerServices.ConfiguredTaskAwaitable`1")!));
    }

    [Theory]
    [InlineData("Task<int>")]
    [InlineData("ValueTask<int>")]
    public async Task PlainCarriers_ContinueToWorkUnderOrdinaryEnforcement(string carrier)
    {
        var source = $$"""
            module PlainAwait
              using System.Threading.Tasks
              func Read async ({{carrier}}:pending) → int
                !
                return await pending
            """;
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        var method = Compile(compiled.GeneratedCode).GetTypes().SelectMany(type => type.GetMethods())
            .Single(candidate => candidate.Name == "Read");
        object input = carrier.StartsWith("ValueTask", StringComparison.Ordinal)
            ? ValueTask.FromResult(42) : Task.FromResult(42);
        Assert.Equal(42, await Assert.IsType<Task<int>>(method.Invoke(null, [input])));
    }

    [Fact]
    public void NullablePayload_CannotBecomeANonNullableReturnThroughConfiguration()
    {
        const string source = """
            module NullableConfiguredAwait
              using System.Threading.Tasks
              func Read async (Task<string?>:pending) → string
                !
                return await pending.ConfigureAwait(false)
            """;
        var compiled = Program.Compile(source);
        Assert.True(compiled.HasErrors);
        Assert.Contains(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.NullableReturnFromNonNullable);
    }

    private static string Slice(string source, TextSpan span) => source.Substring(span.Start, span.Length);

    private static (BoundModule Bound, DiagnosticBag Diagnostics) Bind(string source)
    {
        var diagnostics = new DiagnosticBag();
        var ast = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        return (new Calor.Compiler.Binding.Binder(diagnostics).Bind(ast), diagnostics);
    }

    private static Assembly Compile(string code)
    {
        var compilation = CSharpCompilation.Create("Configured_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble + code)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics) + "\n" + code);
        return Assembly.Load(stream.ToArray());
    }
}
