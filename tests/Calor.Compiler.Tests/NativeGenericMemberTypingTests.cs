using System.Reflection;
using Calor.Compiler.Analysis;
using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class NativeGenericMemberTypingTests
{
    private const string ListCount = """
        module GenericMembers
          func GetCount<T> public (List<T>:items) → int
            return items . Count
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ListCount_DefaultPipelineCompilesAndExecutesNativeAndReemittedInputs(bool sample)
    {
        var root = CliTestHarness.FindRepoRoot();
        var authored = sample
            ? File.ReadAllText(ActiveSyntaxInputs.ForCurrentCompiler(root,
                Path.Combine(root, "samples", "Generics", "generics.calr")))
            : ListCount;
        foreach (var source in NativeInputs(authored))
        {
            var compiled = Program.Compile(source);
            Assert.False(compiled.HasErrors, string.Join(Environment.NewLine, compiled.Diagnostics));
            var method = CompileAssembly(compiled.GeneratedCode).GetTypes()
                .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                .Single(method => method.Name == "GetCount");
            Assert.True(method.IsGenericMethodDefinition);
            Assert.Equal(0, method.MakeGenericMethod(typeof(int)).Invoke(null, [new List<int>()]));
            Assert.Equal(3, method.MakeGenericMethod(typeof(int)).Invoke(null, [new List<int> { 2, 4, 6 }]));
            Assert.Equal(0, method.MakeGenericMethod(typeof(string)).Invoke(null, [new List<string>()]));
            Assert.Equal(2, method.MakeGenericMethod(typeof(string)).Invoke(null, [new List<string> { "a", "b" }]));
        }
    }

    [Theory]
    [InlineData("List<T>", "Count", "int", SpecialType.System_Int32, SymbolKind.Property)]
    [InlineData("List<T>", "Capacity", "int", SpecialType.System_Int32, SymbolKind.Property)]
    [InlineData("IReadOnlyCollection<T>", "Count", "int", SpecialType.System_Int32, SymbolKind.Property)]
    [InlineData("ICollection<T>", "IsReadOnly", "bool", SpecialType.System_Boolean, SymbolKind.Property)]
    [InlineData("System.Tuple<int,T>", "Item1", "int", SpecialType.System_Int32, SymbolKind.Property)]
    [InlineData("System.Tuple<string,T>", "Item1", "string", SpecialType.System_String, SymbolKind.Property)]
    [InlineData("System.Collections.Generic.KeyValuePair<int,T>", "Key", "int", SpecialType.System_Int32, SymbolKind.Property)]
    [InlineData("System.ArraySegment<T>", "Offset", "int", SpecialType.System_Int32, SymbolKind.Property)]
    [InlineData("System.ValueTuple<int,T>", "Item1", "int", SpecialType.System_Int32, SymbolKind.Field)]
    public void ScalarMetadataSelection_RetainsExactTypeAndSeparateGetterIdentity(
        string receiver, string name, string output, SpecialType resultType, SymbolKind kind)
    {
        var authored = $"module SelectedMembers\n  func Read<T> ({receiver}:value) → {output}\n    return (value).{name}";
        var oracleTree = CSharpSyntaxTree.ParseText(
            $"using System.Collections.Generic; class Oracle {{ {output} Read<T>({receiver} value) => value.{name}; }}");
        var oracle = CSharpCompilation.Create("MemberOracle", [oracleTree], GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(oracle.GetDiagnostics().Where(diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var oracleSyntax = Assert.Single(oracleTree.GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax>());
        var oracleMember = oracle.GetSemanticModel(oracleTree).GetSymbolInfo(oracleSyntax).Symbol!;
        foreach (var source in NativeInputs(authored))
        {
            var module = Parse(source);
            var bindings = CallGraphAnalysis.ResolveOperatorCalls(module);
            var (origin, selected) = Assert.Single(bindings.MetadataMemberTypes);
            Assert.IsType<ReferenceNode>(origin.Target);
            Assert.Equal(name, origin.FieldName);
            Assert.Equal(name, source.Substring(origin.FieldNameSpan.Start, origin.FieldNameSpan.Length));
            Assert.Equal(kind, selected.Member.Kind);
            Assert.Equal(oracleMember.ToDisplayString(), selected.Member.ToDisplayString());
            Assert.Equal(oracleMember.ContainingAssembly.Identity, selected.Member.ContainingAssembly.Identity);
            Assert.Equal(resultType, selected.ResultType.SpecialType);
            Assert.True(selected.ReceiverType.IsGenericType);
            Assert.Empty(selected.Member.DeclaringSyntaxReferences);
            Assert.Empty(bindings.SourceCalls);
            Assert.Empty(bindings.ExternalCalls);
            Assert.Empty(bindings.ResultTypes);
            if (kind == SymbolKind.Property)
            {
                var getter = Assert.Single(bindings.MetadataGetters).Value;
                Assert.Same(selected.Member, getter.Property);
                Assert.Same(selected.ReceiverType, getter.ReceiverType);
                Assert.Equal(oracleMember.OriginalDefinition.ToDisplayString(), getter.Property.OriginalDefinition.ToDisplayString());
            }
            else
                Assert.Empty(bindings.MetadataGetters);
            var bound = new Calor.Compiler.Binding.Binder(new DiagnosticBag()).Bind(module);
            var member = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundFieldAccessExpression>());
            Assert.Null(member.ResolvedMetadataMember);
            Assert.Equal("OBJECT", member.Type.DisplayString);
            Assert.True(member.HasUnknownInferredType);
            var diagnostics = new DiagnosticBag();
            new TypeChecking.TypeChecker(diagnostics).Check(module);
            Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics));
            CompileAssembly(new CSharpEmitter().Emit(module));
        }
    }

    [Theory]
    [InlineData("List<T>", "Missing")]
    [InlineData("List<T>", "count")]
    [InlineData("List<T>", "_size")]
    [InlineData("List<Missing>", "Count")]
    [InlineData("List<int,int>", "Count")]
    [InlineData("System.Tuple<T,int>", "Item1")]
    [InlineData("List<T>", "Comparer")]
    [InlineData("List<T>", "AsReadOnly")]
    [InlineData("Dictionary<T,int>", "Keys")]
    public void MissingInaccessibleInvalidOrNonscalarMembers_DoNotAcquireMetadataTyping(
        string receiver, string name)
    {
        var authored = $"module RejectedMembers\n  func Read<T> ({receiver}:value) → int\n    return (value).{name}";
        foreach (var source in NativeInputs(authored))
        {
            var module = Parse(source);
            Assert.Empty(CallGraphAnalysis.ResolveOperatorCalls(module).MetadataMemberTypes);
            Assert.Empty(CallGraphAnalysis.ResolveOperatorCalls(module).MetadataGetters);
            Assert.True(Program.Compile(source).HasErrors);
            var diagnostics = new DiagnosticBag();
            new TypeChecking.TypeChecker(diagnostics).Check(module);
            Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
        }
    }

    [Theory]
    [InlineData("return (items).Count", "bool")]
    [InlineData("return (items).Count + true", "int")]
    [InlineData("if (items).Count\n      return 1\n    return 0", "int")]
    public void SelectedIntegerMember_DoesNotBecomeAnUnknownOrBoolean(string body, string output)
    {
        var authored = $"module WrongMemberContext\n  func Read<T> (List<T>:items) → {output}\n    {body}";
        foreach (var source in NativeInputs(authored))
        {
            var diagnostics = new DiagnosticBag();
            new TypeChecking.TypeChecker(diagnostics).Check(Parse(source));
            Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
            Assert.True(Program.Compile(source).HasErrors);
        }
    }

    [Theory]
    [InlineData("true ? items : missing")]
    [InlineData("missing ?? items")]
    public void ReceiverDiagnostics_AreNotSkippedByMemberProjection(string receiver)
    {
        var authored = $"module InvalidReceiver\n  func Read<T> (List<T>:items) → int\n    return ({receiver}).Count";
        foreach (var source in NativeInputs(authored))
        {
            var module = Parse(source);
            Assert.Empty(CallGraphAnalysis.ResolveOperatorCalls(module).MetadataMemberTypes);
            var diagnostics = new DiagnosticBag();
            new TypeChecking.TypeChecker(diagnostics).Check(module);
            Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.UndefinedReference
                && diagnostic.Message.Contains("'missing'", StringComparison.Ordinal));
            Assert.True(Program.Compile(source).HasErrors);
        }
    }

    [Theory]
    [InlineData("field bool:Count public", "bool", false)]
    [InlineData("field bool:Count public", "int", true)]
    [InlineData("", "int", true)]
    public void SourceLookalikes_RetainSourceMemberRulesWithoutMetadataEvidence(
        string declaration, string output, bool invalid)
    {
        var authored = $$"""
            module SourceList
              class List<T>
                {{declaration}}
              func Read<T> (List<T>:items) → {{output}}
                let {{output}}:result = (items).Count
                return result
            """;
        foreach (var source in NativeInputs(authored))
        {
            var module = Parse(source);
            Assert.Empty(CallGraphAnalysis.ResolveOperatorCalls(module).MetadataMemberTypes);
            var diagnostics = new DiagnosticBag();
            new TypeChecking.TypeChecker(diagnostics).Check(module);
            Assert.True(invalid == diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics));
            if (invalid)
                Assert.True(Program.Compile(source).HasErrors);
            else
                CompileAssembly(new CSharpEmitter().Emit(module));
        }
    }

    [Fact]
    public void ListReceiver_ExistingCatalogContractDoesNotEstablishBoundMemberAuthority()
    {
        var module = Parse(ListCount);
        var bound = new Calor.Compiler.Binding.Binder(new DiagnosticBag()).Bind(module);
        var member = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundFieldAccessExpression>());
        var receiver = Assert.IsType<Calor.Compiler.Binding.BoundTypes.NominalBoundType>(member.Target.Type);
        Assert.Null(receiver.RoslynSymbol);
        Assert.Null(member.ResolvedMetadataMember);
        Assert.True(member.HasUnknownInferredType);
        Assert.Equal("OBJECT", member.Type.DisplayString);
        var key = Effects.EffectResolverKey.FromBoundReceiver(receiver, "Count", null, Effects.EffectMemberKind.Getter);
        Assert.Equal("System.Collections.Generic.List`1", key.DeclaringType);
        var resolver = new Effects.EffectResolver();
        Assert.NotEqual(Effects.EffectResolutionStatus.Unknown, resolver.Resolve(key).Status);
        var graph = CallGraphAnalysis.Build(module);
        var caller = Assert.Single(graph.Functions).Key;
        Assert.Null(graph.MetadataMemberReceiverType(caller, member.Span));
        var getter = Assert.Single(CallGraphAnalysis.ResolveOperatorCalls(module).MetadataGetters).Value;
        Assert.Equal("System.Collections.Generic.List<T>", getter.ReceiverType.ToDisplayString());
        Assert.Equal("Count", getter.Property.Name);
        Assert.Empty(getter.Property.DeclaringSyntaxReferences);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ListCount_DefaultAndExplicitPureRowsReuseTheExistingCatalogUnderStrictEnforcement(bool sample, bool explicitPure)
    {
        var root = CliTestHarness.FindRepoRoot();
        var authored = sample
            ? File.ReadAllText(ActiveSyntaxInputs.ForCurrentCompiler(root,
                Path.Combine(root, "samples", "Generics", "generics.calr")))
            : ListCount;
        if (explicitPure)
            authored = authored.Replace("    return items . Count", "    !\n    return items . Count", StringComparison.Ordinal);
        foreach (var source in NativeInputs(authored))
        {
            foreach (var strict in new[] { false, true })
            {
                var compiled = Program.Compile(source, null, new CompilationOptions { StrictEffects = strict });
                Assert.False(compiled.HasErrors, string.Join(Environment.NewLine, compiled.Diagnostics));
                Assert.DoesNotContain(compiled.Diagnostics, diagnostic => diagnostic.Code is
                    DiagnosticCode.UnknownExternalCall or DiagnosticCode.ForbiddenEffect);
            }
        }
    }

    [Fact]
    public void OpaqueReceiver_DoesNotBorrowMetadataTyping()
    {
        const string opaque = "public static System.Collections.Generic.List<int> Items() { return new(); }";
        var authored = $$"""
            module OpaqueGenericReceiver
              raw member {{System.Text.Encoding.UTF8.GetByteCount(opaque)}}
            {{opaque}}
              func Read () → int
                !
                return (Items()).Count
            """;
        foreach (var source in NativeInputs(authored))
        {
            var bindings = CallGraphAnalysis.ResolveOperatorCalls(Parse(source));
            Assert.Empty(bindings.MetadataMemberTypes);
            Assert.Empty(bindings.MetadataGetters);
            Assert.True(Program.Compile(source).HasErrors);
        }
    }

    [Fact]
    public void ConditionalNativeMembers_KeepEachActiveOriginWithoutGrantingCallAuthority()
    {
        const string authored = """
            module ConditionalMembers
              class Host
                pp FEATURE
                  func Read<T> public static (List<T>:items) → int
                    return (items).Count
                pp else
                  func Read<T> public static (IReadOnlyCollection<T>:items) → int
                    return (items).Count
            """;
        foreach (var source in NativeInputs(authored))
        {
            var selections = CallGraphAnalysis.ResolveOperatorCalls(Parse(source));
            Assert.Equal(2, selections.MetadataMemberTypes.Count);
            Assert.Equal(2, selections.MetadataGetters.Count);
            Assert.Equal(new[] { "IReadOnlyCollection", "List" },
                selections.MetadataMemberTypes.Values.Select(selection => selection.ReceiverType.Name).Order(StringComparer.Ordinal));
            Assert.All(selections.MetadataMemberTypes.Values, selection =>
                Assert.Equal(SpecialType.System_Int32, selection.ResultType.SpecialType));
            Assert.Empty(selections.SourceCalls);
            Assert.Empty(selections.ExternalCalls);
        }
    }

    [Theory]
    [InlineData("IReadOnlyCollection<T>")]
    [InlineData("int")]
    public void ConfigurationDependentOrInvalidReceiver_DoesNotLeavePartialMemberEvidence(string other)
    {
        var authored = $$"""
            module ConflictingMember
              class Host<T>
                pp FEATURE
                  field List<T>:items public
                pp else
                  field {{other}}:items public
                func Read public () → int
                  return (items).Count
            """;
        foreach (var source in NativeInputs(authored))
        {
            var bindings = CallGraphAnalysis.ResolveOperatorCalls(Parse(source));
            Assert.Empty(bindings.MetadataMemberTypes);
            Assert.Empty(bindings.MetadataGetters);
        }
    }

    [Fact]
    public void AmbiguousAliases_DoNotLeaveSelectedMemberEvidence()
    {
        const string authored = """
            module AmbiguousAlias
              using Items = System.Collections.Generic.List<int>
              using Items = System.Collections.Generic.IReadOnlyCollection<int>
              func Read (Items:items) → int
                return (items).Count
            """;
        foreach (var source in NativeInputs(authored))
        {
            var bindings = CallGraphAnalysis.ResolveOperatorCalls(Parse(source));
            Assert.Empty(bindings.MetadataMemberTypes);
            Assert.Empty(bindings.MetadataGetters);
            Assert.True(Program.Compile(source).HasErrors);
        }
    }

    [Theory]
    [InlineData("IReadOnlyCollection<T>", "Count", "int")]
    [InlineData("ICollection<T>", "IsReadOnly", "bool")]
    [InlineData("System.Tuple<int,T>", "Item1", "int")]
    public void SelectedGetterWithoutCatalogAuthority_RemainsUnknownUnderStrictEnforcement(
        string receiver, string member, string output)
    {
        var authored = $"module UncontractedGetter\n  func Read<T> ({receiver}:items) → {output}\n    !\n    return (items).{member}";
        foreach (var source in NativeInputs(authored))
        {
            var bindings = CallGraphAnalysis.ResolveOperatorCalls(Parse(source));
            Assert.Single(bindings.MetadataMemberTypes);
            Assert.Single(bindings.MetadataGetters);
            var compiled = Program.Compile(source, null, new CompilationOptions { StrictEffects = true });
            Assert.Contains(compiled.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
            Assert.Contains(compiled.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("'unknown'", StringComparison.Ordinal));
            Assert.DoesNotContain(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.TypeMismatch);
        }
    }

    [Fact]
    public void SourceGetterNamedCount_DoesNotInheritTheListCatalog()
    {
        const string authored = """
            module EffectfulSourceList
              class List<T>
                property int:Count
                  get
                    System.Console.WriteLine("source getter")
                    return 1
              func Read<T> (List<T>:items) → int
                !
                return (items).Count
            """;
        foreach (var source in NativeInputs(authored))
        {
            var bindings = CallGraphAnalysis.ResolveOperatorCalls(Parse(source));
            Assert.Empty(bindings.MetadataMemberTypes);
            Assert.Empty(bindings.MetadataGetters);
            CompileAssembly(new CSharpEmitter().Emit(Parse(source)));
            var compiled = Program.Compile(source, null, new CompilationOptions { StrictEffects = true });
            Assert.Contains(compiled.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("Read", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void OpaqueSourceGetterReceiver_CannotTurnTypeEvidenceIntoGetterAuthority()
    {
        const string opaque = "public class Provider { public static System.Collections.Generic.List<int> Items { get { System.Console.WriteLine(\"opaque\"); return new(); } } }";
        var authored = $$"""
            module OpaqueGetterReceiver
              raw member {{System.Text.Encoding.UTF8.GetByteCount(opaque)}}
            {{opaque}}
              func Read () → int
                !
                return (Provider.Items).Count
            """;
        foreach (var source in NativeInputs(authored))
        {
            var bindings = CallGraphAnalysis.ResolveOperatorCalls(Parse(source));
            Assert.Single(bindings.MetadataMemberTypes);
            Assert.Empty(bindings.MetadataGetters);
            Assert.True(Program.Compile(source, null, new CompilationOptions { StrictEffects = true }).HasErrors);
        }
    }

    [Fact]
    public void SelectedGetter_DoesNotHideReceiverCallEffects()
    {
        const string authored = """
            module GenericReceiverEffects
              func Receiver (List<int>:items) → List<int>
                ! cw
                System.Console.WriteLine("receiver")
                return items
              func Read (List<int>:items) → int
                !
                return (Receiver(items)).Count
            """;
        foreach (var source in NativeInputs(authored))
        {
            Assert.Single(CallGraphAnalysis.ResolveOperatorCalls(Parse(source)).MetadataGetters);
            var compiled = Program.Compile(source);
            Assert.Contains(compiled.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("'cw'", StringComparison.Ordinal));
            var accepted = Program.Compile(source.Replace("func Read (List<int>:items) → int\n    !",
                "func Read (List<int>:items) → int\n    ! cw", StringComparison.Ordinal));
            Assert.False(accepted.HasErrors, string.Join(Environment.NewLine, accepted.Diagnostics));
        }
    }

    private static IEnumerable<string> NativeInputs(string source)
    {
        yield return source;
        yield return new CalorEmitter().Emit(Parse(source));
    }

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics));
        return module;
    }

    private static Assembly CompileAssembly(string source)
    {
        var compilation = CSharpCompilation.Create("GenericMembers_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)], GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, source + Environment.NewLine + string.Join(Environment.NewLine, result.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }
}
