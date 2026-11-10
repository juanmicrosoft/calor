using System.Runtime.Loader;
using Calor.Compiler.Analysis;
using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Binding.Metadata;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.Compiler.TypeChecking;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class InlineLambdaArgumentBindingTests
{
    [Theory]
    [InlineData("Action", "ref", "string[]?", "string[]")]
    [InlineData("Action", "out", "string[]?", "string[]")]
    [InlineData("Action", "ref", "string?", "string")]
    [InlineData("Action", "out", "string?", "string")]
    [InlineData("System.Action", "ref", "string[]?", "string[]")]
    [InlineData("System.Action", "out", "string[]?", "string[]")]
    [InlineData("System.Action", "ref", "string?", "string")]
    [InlineData("System.Action", "out", "string?", "string")]
    [InlineData("DeferredCallback", "ref", "string[]?", "string[]")]
    [InlineData("DeferredCallback", "out", "string[]?", "string[]")]
    [InlineData("DeferredCallback", "ref", "string?", "string")]
    [InlineData("DeferredCallback", "out", "string?", "string")]
    public void VoidBlockLambda_BindsExactSourceDeclarationAndPreservesDeferredFlow(
        string callbackType, string modifier, string nullableType, string nonNullableType)
    {
        var source = $$"""
            module DeferredConjunction
              delegate DeferredCallback () → void
                ! mut
              func Mutate public ({{modifier}} {{nullableType}}:value) → bool
                ! mut
                set value null
                return true
              func Ignore public ({{callbackType}}:action ! mut) → bool
                !
                return true
              func Take public ({{nonNullableType}}:value) → void
                !
              func Probe public ({{nullableType}}:value) → void
                ! mut
                if value != null && Ignore(() ! mut =>
                  Mutate({{modifier}} value)
                )
                  Take(value)
            """;
        var ast = Parse(source);
        var typeDiagnostics = new DiagnosticBag();
        new TypeChecker(typeDiagnostics).Check(ast);
        Assert.False(typeDiagnostics.HasErrors, string.Join("\n", typeDiagnostics));

        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(ast);
        Assert.DoesNotContain(diagnostics, BindingDiagnosticPolicy.IsCompilationError);
        var probe = Assert.Single(bound.Functions.Where(function => function.Symbol.Name == "Probe"));
        var condition = Assert.IsType<BoundBinaryExpression>(
            Assert.IsType<BoundIfStatement>(Assert.Single(probe.Body)).Condition);
        var call = Assert.IsType<BoundCallExpression>(condition.Right);
        Assert.Same(bound.Functions.Single(function => function.Symbol.Name == "Ignore").Symbol,
            call.ResolvedSymbol);
        var mapping = Assert.Single(Assert.Single(call.SelectedOverloadMatches).Arguments);
        Assert.Equal(0, mapping.ArgumentIndex);
        Assert.Equal(0, mapping.ParameterIndex);
        Assert.False(mapping.IsExpandedParams);
        var lambda = Assert.IsType<BoundLambdaExpression>(Assert.Single(call.Arguments));
        Assert.Equal("VOID", lambda.ReturnTypeName);
        var mutation = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(lambda).OfType<BoundCallExpression>());
        Assert.Equal(new[] { modifier }, mutation.ArgumentModifiers);
        Assert.Same(bound.Functions.Single(function => function.Symbol.Name == "Mutate").Symbol,
            mutation.ResolvedSymbol);
        Assert.Empty(condition.InvalidatedLeftNarrowingNames);

        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        using var context = MetadataContext.Create();
        var generated = context.HostCompilationForBinder.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText(compiled.GeneratedCode));
        Assert.DoesNotContain(generated.GetDiagnostics(),
            diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("Func<int>", "Func<string>")]
    [InlineData("System.Func<int>", "System.Func<string>")]
    [InlineData("Numeric", "Textual")]
    [InlineData("Number", "Text")]
    public void NamedLambdaArgument_UsesExactWinningOverloadAndArgumentMap(
        string numericType, string textType)
    {
        var source = $$"""
            module NamedCallbacks
              using Numeric = System.Func<int>
              using Textual = System.Func<string>
              delegate Number () → int
                !
              delegate Text () → string
                !
              func Choose public ({{numericType}}:callback !, int:bias) → int
                !
                return callback() + bias
              func Choose public ({{textType}}:callback !, int:bias) → string
                !
                return callback()
              func Run public () → int
                !
                return Choose(bias: 2, callback: () ! => 40)
            """;
        var bound = Bind(source);
        var call = SourceCall(bound, "Run", "Choose");
        Assert.Same(bound.Functions.First(function => function.Symbol.Name == "Choose").Symbol,
            call.ResolvedSymbol);
        Assert.Equal(new[] { (0, 1), (1, 0) },
            Assert.Single(call.SelectedOverloadMatches).Arguments
                .Select(argument => (argument.ArgumentIndex, argument.ParameterIndex)).ToArray());
        Assert.Equal(42, CompileAndRun(source));
    }

    [Theory]
    [InlineData("System.Func<int,int>", "")]
    [InlineData("Callback", "using Callback = System.Func<int,int>")]
    [InlineData("Action", "delegate Action (int:value) → int\n    !")]
    public void ImplicitLambdaParameter_UsesActualDelegateSignature(string callbackType, string declaration)
    {
        var source = $$"""
            module ContextualCallbacks
              {{declaration}}
              func Apply public ({{callbackType}}:callback !) → int
                !
                return callback(41)
              func Run public () → int
                !
                return Apply((value) ! => value + 1)
            """;
        var bound = Bind(source);
        var call = SourceCall(bound, "Run", "Apply");
        Assert.Same(bound.Functions.Single(function => function.Symbol.Name == "Apply").Symbol,
            call.ResolvedSymbol);
        var lambda = Assert.IsType<BoundLambdaExpression>(Assert.Single(call.Arguments));
        Assert.Equal("INT", TypeIdentity.Canonicalize(Assert.Single(lambda.Parameters).TypeName));
        Assert.Equal(42, CompileAndRun(source));
    }

    [Theory]
    [InlineData("class Action", "Action", "() ! => 1")]
    [InlineData("class Func<T>", "Func<int>", "() ! => 1")]
    [InlineData("class action<T>", "action<int>", "() ! => 1")]
    [InlineData("using Action = System.String", "Action", "() ! => 1")]
    [InlineData("using Callback = System.Action<int>", "Callback", "() ! => 1")]
    [InlineData("", "System.Action<int>", "() ! => 1")]
    [InlineData("", "Func<int,int>", "() ! => 1")]
    [InlineData("", "System.Func<int>", "() ! => true")]
    [InlineData("", "Action", "() ! => true")]
    [InlineData("delegate Callback (ref int:value) → int\n    !", "Callback", "(int:value) ! => value")]
    [InlineData("delegate Callback () → int\n    !", "Callback", "() ! =>\n      return\n    ")]
    public void InvalidLambdaReceivingConversion_CannotAcquireASourceIdentity(
        string declaration, string callbackType, string lambda)
    {
        var source = $$"""
            module InvalidCallbacks
              {{declaration}}
              func Keep public ({{callbackType}}:callback !) → int
                !
                return 1
              func Run public () → int
                !
                return Keep({{lambda}})
            """;
        var ast = Parse(source);
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(ast);
        var call = SourceCall(bound, "Run", "Keep");
        Assert.Null(call.ResolvedSymbol);
        Assert.Empty(call.SelectedOverloadMatches);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.NoMatchingOverload);
        var typeDiagnostics = new DiagnosticBag();
        new TypeChecker(typeDiagnostics).Check(ast);
        Assert.True(typeDiagnostics.HasErrors, string.Join("\n", typeDiagnostics));
        using var metadata = MetadataContext.Create();
        var generated = metadata.HostCompilationForBinder.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText(new CSharpEmitter().Emit(ast)));
        Assert.Contains(generated.GetDiagnostics(),
            diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.True(Program.Compile(source).HasErrors);
    }

    [Fact]
    public void TypeParameterShadowing_DoesNotBecomeSystemAction()
    {
        const string source = """
            module ShadowedCallback
              func Keep<Action> public (Action:callback) → int
                !
                return 1
              func Run public () → int
                !
                return Keep<int>(() ! => 1)
            """;
        var bound = Bind(source, allowErrors: true);
        Assert.Null(SourceCall(bound, "Run", "Keep").ResolvedSymbol);
        Assert.True(Program.Compile(source).HasErrors);
    }

    [Theory]
    [InlineData("System.Action", "System.Action")]
    [InlineData("First", "Second")]
    public void AmbiguousNominalDelegates_DoNotPickAnArbitraryDeclaration(string first, string second)
    {
        var declarations = first == second ? "" : """
            delegate First () → void
                !
              delegate Second () → void
                !
            """;
        var source = $$"""
            module AmbiguousCallbacks
              {{declarations}}
              func Choose public ({{first}}:callback !, int:first, long:second) → int
                !
                return 1
              func Choose public ({{second}}:callback !, long:first, int:second) → int
                !
                return 2
              func Run public () → int
                !
                return Choose(() ! =>
                  return
                , 1, 1)
            """;
        var ast = Parse(source);
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(ast);
        var call = SourceCall(bound, "Run", "Choose");
        Assert.Null(call.ResolvedSymbol);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.AmbiguousOverload);
        using var metadata = MetadataContext.Create();
        var generated = metadata.HostCompilationForBinder.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText(new CSharpEmitter().Emit(ast)));
        Assert.Contains(generated.GetDiagnostics(), diagnostic => diagnostic.Id == "CS0121");
        Assert.True(Program.Compile(source).HasErrors);
    }

    [Fact]
    public void InvalidNamedArgument_DoesNotUseOtherwiseValidLambdaConversion()
    {
        const string source = """
            module InvalidNamedCallback
              func Keep public (System.Action:callback !) → int
                !
                return 1
              func Run public () → int
                !
                return Keep(wrong: () ! =>
                  return
                )
            """;
        var bound = Bind(source, allowErrors: true);
        Assert.Null(SourceCall(bound, "Run", "Keep").ResolvedSymbol);
        Assert.True(Program.Compile(source).HasErrors);
    }

    [Theory]
    [InlineData(false, "Action")]
    [InlineData(false, "System.Action")]
    [InlineData(false, "Callback")]
    [InlineData(true, "Action")]
    [InlineData(true, "System.Action")]
    [InlineData(true, "Callback")]
    public void NamedOverloadedCallback_ChecksTheSelectedPureRow(bool memberCall, string callbackType)
    {
        var source = $$"""
            module NamedCallbackRows
              using Callback = System.Action
              class Receiver
                func Ignore public ({{callbackType}}:callback !, int:value) → void
                  !
                func Ignore public (System.Func<int>:callback ! cw, string:value) → void
                  !
              func Ignore public ({{callbackType}}:callback !, int:value) → void
                !
              func Ignore public (System.Func<int>:callback ! cw, string:value) → void
                !
              func Run public () → void
                ! cw,alloc
                {{(memberCall ? "new Receiver()." : "")}}Ignore(value: 1, callback: () ! cw =>
                  System.Console.WriteLine("not executed")
                )
            """;
        var ast = Parse(source);
        var bindingDiagnostics = new DiagnosticBag();
        var bound = new Binder(bindingDiagnostics).Bind(ast);
        Assert.DoesNotContain(bindingDiagnostics, BindingDiagnosticPolicy.IsCompilationError);
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(
            bound.Functions.Single(function => function.Symbol.Name == "Run"))
            .OfType<BoundCallExpression>().Where(call => call.ResolvedSymbol?.Name.EndsWith("Ignore") == true));
        var declaration = memberCall
            ? ast.Classes.Single().Methods[0].IdentifierSpan : ast.Functions[0].IdentifierSpan;
        Assert.Same(bound.SymbolsById.Values.OfType<FunctionSymbol>()
                .Single(function => function.DeclarationSpan == declaration),
            call.ResolvedSymbol);
        Assert.Equal(new[] { (0, 1), (1, 0) }, Assert.Single(call.SelectedOverloadMatches).Arguments
            .Select(argument => (argument.ArgumentIndex, argument.ParameterIndex)).ToArray());
        var compiled = Program.Compile(source);
        Assert.Contains(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.EffectRowMismatch);
    }

    [Theory]
    [InlineData(" ! fs:rw", "fs:r", false)]
    [InlineData(" ! fs:r", "fs:rw", true)]
    public void CallbackReceivingRows_KeepDirectionalSubtyping(
        string parameterRow, string lambdaRow, bool invalid)
    {
        var source = $$"""
            module CallbackSubtyping
              func Ignore public (System.Action:callback{{parameterRow}}) → void
                !
              func Run public () → void
                ! fs:rw
                Ignore(() ! {{lambdaRow}} =>
                  return
                )
            """;
        var bound = Bind(source);
        Assert.NotNull(SourceCall(bound, "Run", "Ignore").ResolvedSymbol);
        var compiled = Program.Compile(source);
        Assert.Equal(invalid, compiled.HasErrors);
        Assert.Equal(invalid, compiled.Diagnostics.Any(diagnostic =>
            diagnostic.Code == DiagnosticCode.EffectRowMismatch));
    }

    [Fact]
    public void GenericCallback_InferenceKeepsTheOriginalDeclarationAndConstructedReturn()
    {
        const string source = """
            module GenericCallback
              func Apply<T> public (System.Func<T>:callback !) → T
                !
                return callback()
              func Run public () → int
                !
                return Apply(() ! => 42)
            """;
        var bound = Bind(source);
        var call = SourceCall(bound, "Run", "Apply");
        Assert.Same(bound.Functions.Single(function => function.Symbol.Name == "Apply").Symbol,
            call.ResolvedSymbol);
        Assert.Equal("INT", TypeIdentity.Canonicalize(call.Type.DisplayString));
        Assert.Equal(42, CompileAndRun(source));
    }

    [Fact]
    public void NamedDelegateValues_DoNotConvertStructurallyToAnotherDelegate()
    {
        const string source = """
            module NominalCallbacks
              delegate First () → int
                !
              delegate Second () → int
                !
              func Keep public (Second:callback !) → int
                !
                return 1
              func Run public () → int
                !
                let First:callback ! = () ! => 42
                return Keep(callback)
            """;
        var bound = Bind(source, allowErrors: true);
        Assert.Null(SourceCall(bound, "Run", "Keep").ResolvedSymbol);
        Assert.True(Program.Compile(source).HasErrors);
    }

    [Theory]
    [InlineData("Func<int,int>")]
    [InlineData("System.Func<int,int>")]
    [InlineData("Callback")]
    public void MetadataCallbackParameter_CanStillBeSavedAndReturned(string callbackType)
    {
        var source = $$"""
            module SavedCallbacks
              using Callback = System.Func<int,int>
              func Keep public ({{callbackType}}:callback !) → Func<int,int> !
                !
                let Func<int,int>:saved ! = callback
                return saved
              func Run public () → int
                !
                let Func<int,int>:callback ! = Keep((value) ! => value + 1)
                return callback(41)
            """;
        var bound = Bind(source);
        Assert.Same(bound.Functions.Single(function => function.Symbol.Name == "Keep").Symbol,
            SourceCall(bound, "Run", "Keep").ResolvedSymbol);
        Assert.Equal(42, CompileAndRun(source));
    }

    [Theory]
    [InlineData(" !", "", false)]
    [InlineData(" ! cw", "cw", false)]
    [InlineData("", "", true)]
    [InlineData(" ! unknown", "", true)]
    public void ExactClrConversion_DoesNotGrantMissingCallbackEffectAuthority(
        string callbackRow, string callerRow, bool invalid)
    {
        var source = $$"""
            module CallbackEffects
              func Invoke public (System.Action:callback{{callbackRow}}) → void
                ! {{callerRow}}
                callback()
              func Run public () → void
                ! {{callerRow}}
                Invoke(() ! =>
                  return
                )
            """;
        var bound = Bind(source);
        Assert.Same(bound.Functions.Single(function => function.Symbol.Name == "Invoke").Symbol,
            SourceCall(bound, "Run", "Invoke").ResolvedSymbol);
        var compiled = Program.Compile(source);
        Assert.Equal(invalid, compiled.HasErrors);
        Assert.Equal(invalid, compiled.Diagnostics.Any(diagnostic =>
            diagnostic.Code == DiagnosticCode.EffectRowUnknown));
        if (invalid)
            Assert.Contains(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Theory]
    [InlineData("Action")]
    [InlineData("System.Action")]
    [InlineData("Callback")]
    public void ExactClrConversion_DoesNotNarrowAnEffectfulLambdaToPure(string callbackType)
    {
        var source = $$"""
            module CallbackNarrowing
              delegate Callback () → void
                !
              func Ignore public ({{callbackType}}:callback !) → void
                !
              func Run public () → void
                !
                Ignore(() ! cw =>
                  System.Console.WriteLine("not executed")
                )
            """;
        var bound = Bind(source);
        Assert.Same(bound.Functions.Single(function => function.Symbol.Name == "Ignore").Symbol,
            SourceCall(bound, "Run", "Ignore").ResolvedSymbol);
        var compiled = Program.Compile(source);
        Assert.True(compiled.HasErrors);
        Assert.Contains(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.EffectRowMismatch);
    }

    public static IEnumerable<object[]> StandaloneCallbackCases()
    {
        foreach (var type in new[] { "Action", "System.Action", "Callback", "NativeCallback" })
        foreach (var row in new[] { " !", "", " ! unknown" })
        foreach (var unrelatedLambda in new[] { false, true })
            yield return [type, row, unrelatedLambda];
    }

    [Theory]
    [MemberData(nameof(StandaloneCallbackCases))]
    public void StandaloneCallbackInvocation_DoesNotDependOnUnrelatedLambda(
        string callbackType, string callbackRow, bool unrelatedLambda)
    {
        var source = $$"""
            module StandaloneCallbacks
              using Callback = System.Action
              delegate NativeCallback () → void
                !
              func Invoke public ({{callbackType}}:callback{{callbackRow}}) → void
                !
                callback()
              {{UnrelatedLambda(unrelatedLambda)}}
            """;
        var invalid = callbackRow != " !";
        var compiled = Program.Compile(source);
        Assert.True(invalid == compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        Assert.Equal(invalid, compiled.Diagnostics.Any(diagnostic =>
            diagnostic.Code == DiagnosticCode.EffectRowUnknown));
        if (invalid)
            Assert.Contains(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);

        var ast = Parse(source);
        var typeDiagnostics = new DiagnosticBag();
        new TypeChecker(typeDiagnostics).Check(ast);
        Assert.False(typeDiagnostics.HasErrors, string.Join("\n", typeDiagnostics));
        var bound = Bind(source);
        var parameter = Assert.Single(bound.Functions.Single(function =>
            function.Symbol.Name == "Invoke").Symbol.Parameters);
        var functionType = Assert.IsType<Calor.Compiler.Binding.BoundTypes.FunctionBoundType>(parameter.FunctionType);
        Assert.Equal(invalid, functionType.Row.IsUnknown);
        Assert.Equal("VOID", TypeIdentity.Canonicalize(functionType.ReturnType.DisplayString));
        Assert.Empty(functionType.ParameterTypes);

        if (invalid)
            return;
        using var metadata = MetadataContext.Create();
        var generated = metadata.HostCompilationForBinder.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText(compiled.GeneratedCode));
        using var stream = new MemoryStream();
        var emitted = generated.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        stream.Position = 0;
        var context = new AssemblyLoadContext("StandaloneCallbacks", isCollectible: true);
        try
        {
            var method = Assert.Single(context.LoadFromStream(stream).GetTypes(),
                type => type.GetMethod("Invoke") is { IsStatic: true }).GetMethod("Invoke")!;
            var calls = 0;
            Action callback = () => calls++;
            method.Invoke(null, [Delegate.CreateDelegate(
                method.GetParameters().Single().ParameterType, callback.Target, callback.Method)]);
            Assert.Equal(1, calls);
        }
        finally
        {
            context.Unload();
        }
    }

    public static IEnumerable<object[]> StandaloneReceivingCases()
    {
        foreach (var type in new[] { "Action", "System.Action", "Callback", "NativeCallback" })
        foreach (var row in new[] { " !", " ! cw" })
        foreach (var unrelatedLambda in new[] { false, true })
        foreach (var memberCall in new[] { false, true })
            yield return [type, row, unrelatedLambda, memberCall];
    }

    [Theory]
    [MemberData(nameof(StandaloneReceivingCases))]
    public void StandaloneNamedOverloadedReceivingRow_DoesNotDependOnUnrelatedLambda(
        string callbackType, string callbackRow, bool unrelatedLambda, bool memberCall)
    {
        var source = $$"""
            module StandaloneReceiving
              using Callback = System.Action
              delegate NativeCallback () → void
                !
              class Receiver
                func Ignore public ({{callbackType}}:callback !, int:value) → void
                  !
                func Ignore public (System.Func<int>:callback ! cw, string:value) → void
                  !
              func Ignore public ({{callbackType}}:callback !, int:value) → void
                !
              func Ignore public (System.Func<int>:callback ! cw, string:value) → void
                !
              func Run public ({{callbackType}}:callback{{callbackRow}}) → void
                ! cw,alloc
                {{(memberCall ? "new Receiver()." : "")}}Ignore(value: 1, callback: callback)
              {{UnrelatedLambda(unrelatedLambda)}}
            """;
        var ast = Parse(source);
        var bound = Bind(source);
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(
            bound.Functions.Single(function => function.Symbol.Name == "Run"))
            .OfType<BoundCallExpression>().Where(call => call.ResolvedSymbol?.Name.EndsWith("Ignore") == true));
        var declaration = memberCall
            ? ast.Classes.Single().Methods[0].IdentifierSpan : ast.Functions[0].IdentifierSpan;
        Assert.Same(bound.SymbolsById.Values.OfType<FunctionSymbol>()
            .Single(function => function.DeclarationSpan == declaration), call.ResolvedSymbol);
        Assert.Equal(new[] { (0, 1), (1, 0) }, Assert.Single(call.SelectedOverloadMatches).Arguments
            .Select(argument => (argument.ArgumentIndex, argument.ParameterIndex)).ToArray());
        var compiled = Program.Compile(source);
        var invalid = callbackRow != " !";
        Assert.True(invalid == compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        Assert.Equal(invalid, compiled.Diagnostics.Any(diagnostic =>
            diagnostic.Code == DiagnosticCode.EffectRowMismatch));
    }

    [Theory]
    [InlineData("Action", false, false)]
    [InlineData("Action", false, true)]
    [InlineData("Action", true, false)]
    [InlineData("Action", true, true)]
    [InlineData("System.Action", false, false)]
    [InlineData("System.Action", false, true)]
    [InlineData("System.Action", true, false)]
    [InlineData("System.Action", true, true)]
    [InlineData("Callback", false, false)]
    [InlineData("Callback", false, true)]
    [InlineData("Callback", true, false)]
    [InlineData("Callback", true, true)]
    public void UnsupportedBodyLowering_DoesNotThrowDuringSignatureProjection(
        string callbackType, bool contract, bool unrelatedLambda)
    {
        const string predicate = "forall int:i in 0..n, int:j in 0..n : matrix[i,j] == matrix[j,i]";
        var source = $$"""
            module QuantifiedCallbacks
              using Callback = System.Action
              func Check<T> ({{callbackType}}:callback !, T[,]:matrix, int:n) → bool
                !
                {{(contract ? "requires " + predicate : "")}}
                callback()
                return {{(contract ? "true" : predicate)}}
              {{UnrelatedLambda(unrelatedLambda)}}
            """;
        var ast = Parse(source);
        var compiled = Program.Compile(source);
        Assert.True(compiled.HasErrors);
        Assert.Contains(compiled.Diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.QuantifierRuntimeLoweringUnsupported);
        if (contract)
        {
            var disabled = Program.Compile(source, "quantified-callback.calr", new CompilationOptions
            {
                ContractMode = ContractMode.Off,
                EnableTypeChecking = true
            });
            Assert.False(disabled.HasErrors, string.Join("\n", disabled.Diagnostics));
            var graph = CallGraphAnalysis.Build(ast, null, projectionContractMode: ContractMode.Off);
            Assert.Empty(graph.OperatorBindingFailures);
            var check = Assert.Single(ast.Functions, function => function.Name == "Check");
            Assert.True(graph.DeclaredFunctionTypes(check.Id).TryGetValue("callback", out var callback));
            Assert.False(callback.Row.IsUnknown);
            Assert.Empty(callback.ParameterTypes);
            using var metadata = MetadataContext.Create();
            var generated = metadata.HostCompilationForBinder.AddSyntaxTrees(
                CSharpSyntaxTree.ParseText(disabled.GeneratedCode));
            using var stream = new MemoryStream();
            var emitted = generated.Emit(stream);
            Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        }

        var typeDiagnostics = new DiagnosticBag();
        new TypeChecker(typeDiagnostics).Check(ast);
        Assert.Contains(typeDiagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.QuantifierRuntimeLoweringUnsupported);

        Assert.Contains(CallGraphAnalysis.Build(ast).OperatorBindingFailures,
            failure => failure.Contains(DiagnosticCode.QuantifierRuntimeLoweringUnsupported, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false, DiagnosticCode.EffectRowUnknown)]
    [InlineData(true, DiagnosticCode.EffectRowMismatch)]
    public void DisabledRuntimeContracts_RetainStrictCallbackRows(bool receiving, string expectedCode)
    {
        var source = receiving ? """
            module CallbackNarrowing
              func Ignore (System.Action:callback !) → void
                !
              func Run () → void
                ! cw
                let System.Action:callback ! cw = () ! cw =>
                  System.Console.WriteLine("effect")
                Ignore(callback)
            """ : """
            module UnknownCallback
              func Run (System.Action:callback) → void
                !
                callback()
            """;
        var compiled = Program.Compile(source, "contract-off-callback.calr", new CompilationOptions
        {
            ContractMode = ContractMode.Off,
            EnableTypeChecking = true
        });
        Assert.True(compiled.HasErrors);
        Assert.Contains(compiled.Diagnostics, diagnostic => diagnostic.Code == expectedCode);
    }

    private static string UnrelatedLambda(bool include) => include ? """
        func Unrelated private () → void
            !
            let Func<int>:unused ! = () ! => 1
        """ : "";

    private static BoundModule Bind(string source, bool allowErrors = false)
    {
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(Parse(source));
        if (!allowErrors)
            Assert.DoesNotContain(diagnostics, BindingDiagnosticPolicy.IsCompilationError);
        return bound;
    }

    private static BoundCallExpression SourceCall(BoundModule module, string caller, string target) =>
        Assert.Single(BoundNodeHelpers.DescendantsAndSelf(module.Functions.Single(function =>
            function.Symbol.Name == caller)).OfType<BoundCallExpression>().Where(call => call.Target == target));

    private static int CompileAndRun(string source)
    {
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        using var metadata = MetadataContext.Create();
        var generated = metadata.HostCompilationForBinder.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText(compiled.GeneratedCode));
        using var stream = new MemoryStream();
        var emitted = generated.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        stream.Position = 0;
        var context = new AssemblyLoadContext("InlineCallbacks", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(stream);
            return (int)Assert.Single(assembly.GetTypes(), type => type.GetMethod("Run") != null)
                .GetMethod("Run")!.Invoke(null, null)!;
        }
        finally
        {
            context.Unload();
        }
    }

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var ast = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        return ast;
    }
}
