using System.Reflection;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class CanonicalNamespaceTests
{
    [Theory]
    [InlineData("module Grid Contoso.Tools", "Contoso.Tools", true)]
    [InlineData("module Grid Contoso.Tools block", "Contoso.Tools", false)]
    [InlineData("module Grid Contoso.@event", "Contoso.event", true)]
    [InlineData("module Grid @overflow block overflow unchecked", "overflow", false)]
    [InlineData("module Grid _global", "_global", true)]
    [InlineData("module Grid", "", true)]
    public void PositionalNamespaceIsSeparateFromWrapperAndStaticContainer(
        string header, string expectedNamespace, bool fileScoped)
    {
        var module = Parse($"{header}\n  module Area\n    func Square (int:side) → int\n      return side * side");
        Assert.Equal("Grid", module.Name);
        var scope = Assert.Single(module.NamespaceScopes);
        Assert.Equal(expectedNamespace, scope.FullName);
        Assert.Equal(fileScoped, scope.IsFileScoped);
        Assert.Equal(expectedNamespace.Length == 0, scope.IsGlobal);
        var container = Assert.Single(module.Classes);
        Assert.True(container.IsModule);
        Assert.Equal(expectedNamespace, container.NamespaceIdentity);
        Assert.Equal(scope.Id, container.NamespaceScopeId);
        var emitted = new CalorEmitter().Emit(module);
        Assert.DoesNotContain('§', emitted);
        var reparse = Parse(emitted);
        Assert.Equal(expectedNamespace, reparse.Classes.Single().NamespaceIdentity);
        Assert.Equal(fileScoped, reparse.NamespaceScopes.Single().IsFileScoped);
        var compilation = Compile(new CSharpEmitter().Emit(reparse));
        var typeName = expectedNamespace.Length == 0 ? "Area" : $"{expectedNamespace}.Area";
        Assert.NotNull(compilation.GetTypeByMetadataName(typeName));
        Assert.Null(compilation.GetTypeByMetadataName($"{expectedNamespace}.Grid"));
    }

    [Fact]
    public void NativeUsingFormsKeepCompilationUnitScopeAndTargets()
    {
        var module = Parse("""
            module Grid Contoso.Tools
              global using System
              using System.Text
              using static System.Math
              using Json = System.Text.Json.JsonDocument
              class Area
            """);
        Assert.Equal(4, module.Usings.Count);
        Assert.All(module.Usings, directive => Assert.Null(directive.NamespaceScopeId));
        Assert.True(module.Usings[2].IsStatic);
        Assert.Equal("Json", module.Usings[3].Alias);
        Assert.True(module.Usings[0].IsGlobal);
        var emitted = new CalorEmitter().Emit(module);
        Assert.DoesNotContain('§', emitted);
        var reparse = Parse(emitted);
        Assert.Equal(module.Usings.Select(directive => directive.Namespace),
            reparse.Usings.Select(directive => directive.Namespace));
        Compile(new CSharpEmitter().Emit(reparse));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" block")]
    public void NamespaceUsingReemissionKeepsEveryImportKindAndPlacement(string origin)
    {
        var source = $$"""
            module Imports Witness{{origin}}
              global using System
              using Text = System.Text
              namespace using System.Collections.Generic
              namespace using static System.Math
              namespace using Text = System.Int32
              module Probe
                func Read () → int
                  !
                  return Abs(-42)
            """;
        foreach (var native in new[] { source, new CalorEmitter().Emit(Parse(source)) })
        {
            var module = Parse(native);
            var scope = Assert.Single(module.NamespaceScopes);
            Assert.Equal(origin.Length == 0, scope.IsFileScoped);
            Assert.Equal(5, module.Usings.Count);
            Assert.All(module.Usings.Take(2), directive => Assert.Null(directive.NamespaceScopeId));
            Assert.All(module.Usings.Skip(2), directive =>
            {
                Assert.Equal(scope.Id, directive.NamespaceScopeId);
                Assert.Equal("Witness", directive.NamespaceIdentity);
                Assert.False(directive.IsGlobal);
            });
            Assert.True(module.Usings[0].IsGlobal);
            Assert.True(module.Usings[3].IsStatic);
            Assert.Equal(["Text", "Text"], module.Usings.Where(item => item.Alias != null).Select(item => item.Alias));
            var strict = Program.Compile(native);
            Assert.Contains(strict.Diagnostics.Errors, error => error.Code == DiagnosticCode.ForbiddenEffect
                && error.Message.Contains("unknown"));
            var generated = Compile(new CSharpEmitter().Emit(module));
            var tree = generated.SyntaxTrees.Single();
            var root = (CompilationUnitSyntax)tree.GetRoot();
            Assert.Equal(["global using System;", "using Text = System.Text;"],
                root.Usings.Where(item => item.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword) || item.Alias != null)
                    .Select(item => item.ToString()));
            var ns = Assert.IsAssignableFrom<BaseNamespaceDeclarationSyntax>(Assert.Single(root.Members));
            Assert.Equal(["using System.Collections.Generic;", "using static System.Math;", "using Text = int;"],
                ns.Usings.Select(item => item.ToString()));
            var call = Assert.Single(root.DescendantNodes().OfType<InvocationExpressionSyntax>());
            var selected = Assert.IsAssignableFrom<IMethodSymbol>(generated.GetSemanticModel(tree).GetSymbolInfo(call).Symbol);
            Assert.Equal("System.Math.Abs(int)", selected.ToDisplayString());
            Assert.Equal(SpecialType.System_Int32, selected.ReturnType.SpecialType);
            Assert.Equal(42, Invoke(Load(generated).GetType("Witness.Probe")!, "Read"));
        }
    }

    [Theory]
    [InlineData(false, "using Picks;", "Item", "Witness.Picks.Item", "Picks.Item")]
    [InlineData(true, "using Picks;", "Item", "Witness.Picks.Item", "Picks.Item")]
    [InlineData(false, "using Choice = Picks.Item;", "Choice", "Witness.Picks.Item", "Picks.Item")]
    [InlineData(true, "using Choice = Picks.Item;", "Choice", "Witness.Picks.Item", "Picks.Item")]
    [InlineData(false, "using static Helpers;", "Item", "Witness.Helpers.Item", "Helpers.Item")]
    [InlineData(true, "using static Helpers;", "Item", "Witness.Helpers.Item", "Helpers.Item")]
    public void ProjectScopedImportsKeepResolvedSiblingLookupInsteadOfHoisting(
        bool fileScoped, string import, string typeName, string localType, string outerType)
    {
        const string companions = """
            namespace Picks { public class Item {} }
            namespace Witness.Picks { public class Item {} }
            public static class Helpers { public class Item {} }
            namespace Witness { public static class Helpers { public class Item {} } }
            """;
        string Source(string placement) => $$"""
            {{(placement == "namespace" ? "" : (placement == "global" ? "global " : "") + import)}}
            namespace Witness{{(fileScoped ? ";" : " {")}}
            {{(placement == "namespace" ? import : "")}}
            public static class Probe { public static {{typeName}} Echo({{typeName}} value) { return value; } }
            {{(fileScoped ? "" : "}")}}
            """;
        var converter = new CSharpToCalorConverter(new()
        {
            NullableContextOptions = NullableContextOptions.Enable,
            AdditionalSemanticSyntaxTrees = [CSharpSyntaxTree.ParseText(companions, new CSharpParseOptions(LanguageVersion.Preview))]
        });
        var project = converter.ConvertProject(Source("namespace"), "Imports.cs");
        Assert.True(project.Success, string.Join("\n", project.Outputs.SelectMany(output => output.Conversion.Issues)));
        Assert.False(project.IsSplit);
        var output = Assert.Single(project.Outputs);
        Assert.Equal("Imports.calr", output.FileName);
        Assert.True(output.Conversion.Losses.Count == 0,
            string.Join("\n", output.Conversion.Issues.Concat<object>(output.Conversion.Losses)));
        Assert.Equal("native", output.Conversion.Paths.Outcome);
        Assert.Equal("verified", output.Conversion.Paths.RoundTrip);
        foreach (var native in new[] { output.Conversion.CalorSource!,
                     new CalorEmitter().Emit(Parse(output.Conversion.CalorSource!)) })
        {
            var module = Parse(native);
            var directive = Assert.Single(module.Usings);
            Assert.Equal(module.NamespaceScopes.Single().Id, directive.NamespaceScopeId);
            Assert.False(directive.IsGlobal);
            var compiled = Program.Compile(native, null, new CompilationOptions { DeferGeneratedOutputValidation = true });
            Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
            AssertEchoTypeAndRuntime(CompileWithCompanions(compiled.GeneratedCode, companions), localType);
        }
        foreach (var placement in new[] { "namespace", "file", "global" })
            AssertEchoTypeAndRuntime(CompileWithCompanions(Source(placement), companions),
                placement == "namespace" ? localType : outerType);

        static void AssertEchoTypeAndRuntime(CSharpCompilation compilation, string expectedType)
        {
            var echo = Assert.Single(compilation.GetTypeByMetadataName("Witness.Probe")!.GetMembers("Echo").OfType<IMethodSymbol>());
            Assert.Equal(expectedType, echo.ReturnType.ToDisplayString());
            Assert.Equal(expectedType, echo.Parameters.Single().Type.ToDisplayString());
            var tree = compilation.SyntaxTrees.First();
            var parameter = tree.GetRoot().DescendantNodes().OfType<ParameterSyntax>().Single();
            Assert.Equal(expectedType, compilation.GetSemanticModel(tree).GetTypeInfo(parameter.Type!).Type!.ToDisplayString());
            var method = Load(compilation).GetType("Witness.Probe")!.GetMethod("Echo")!;
            var value = Activator.CreateInstance(method.GetParameters().Single().ParameterType);
            Assert.Same(value, method.Invoke(null, [value]));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProjectLocalAliasShadowsOuterAliasWithoutLosingGlobalVisibility(bool global)
    {
        var original = $$"""
            {{(global ? "global " : "")}}using Choice = System.Int16;
            namespace Witness
            {
                using Choice = System.Int32;
                public static class Probe
                {
                    public static Choice Echo(Choice value) { return value; }
                }
            }
            """;
        var consumer = global ? "public class Consumer { public Choice Value; }" : "public class Consumer { }";
        var project = new CSharpToCalorConverter(new()
        {
            NullableContextOptions = NullableContextOptions.Enable,
            AdditionalSemanticSyntaxTrees = [CSharpSyntaxTree.ParseText(consumer, new CSharpParseOptions(LanguageVersion.Preview))]
        }).ConvertProject(original);
        Assert.True(project.Success);
        Assert.False(project.IsSplit);
        var conversion = Assert.Single(project.Outputs).Conversion;
        Assert.Empty(conversion.Losses);
        var module = Parse(conversion.CalorSource!);
        Assert.Equal(2, module.Usings.Count);
        Assert.Null(module.Usings[0].NamespaceScopeId);
        Assert.Equal(global, module.Usings[0].IsGlobal);
        Assert.Equal(module.NamespaceScopes.Single().Id, module.Usings[1].NamespaceScopeId);
        foreach (var source in new[] { original, new CSharpEmitter().Emit(module),
                     new CSharpEmitter().Emit(Parse(new CalorEmitter().Emit(module))) })
        {
            var compilation = CompileWithCompanions(source, consumer);
            var echo = Assert.Single(compilation.GetTypeByMetadataName("Witness.Probe")!.GetMembers("Echo").OfType<IMethodSymbol>());
            Assert.Equal(SpecialType.System_Int32, echo.ReturnType.SpecialType);
            Assert.Equal(SpecialType.System_Int32, echo.Parameters.Single().Type.SpecialType);
            if (global)
                Assert.Equal(SpecialType.System_Int16,
                    Assert.Single(compilation.GetTypeByMetadataName("Consumer")!.GetMembers("Value").OfType<IFieldSymbol>()).Type.SpecialType);
            Assert.Equal(42, Load(compilation).GetType("Witness.Probe")!.GetMethod("Echo")!.Invoke(null, [42]));
        }
    }

    [Theory]
    [InlineData("namespace Witness { using System;\n#if true\nclass C {}\n#endif\n}")]
    [InlineData("namespace Witness { using Pair = (int X, int Y); class C {} }")]
    [InlineData("namespace Witness { using System; class C {} } namespace Other { class D {} }")]
    public void ProjectScopedImportUnsupportedTopologyKeepsTheWholeFile(string source)
    {
        var project = new CSharpToCalorConverter().ConvertProject(source);
        Assert.True(project.Success);
        Assert.False(project.IsSplit);
        var conversion = Assert.Single(project.Outputs).Conversion;
        Assert.Equal("namespace-topology", Assert.Single(conversion.Losses).Feature);
        Assert.Equal(source, new CSharpEmitter().Emit(Parse(conversion.CalorSource!)));
    }

    [Theory]
    [InlineData(NullableContextOptions.Disable, false)]
    [InlineData(NullableContextOptions.Enable, true)]
    public void ProjectScopedImportsRequireExactDeclaredNullability(
        NullableContextOptions nullableContext, bool native)
    {
        const string source = """
            namespace Witness
            {
                using System.Text;
                public class Probe { public string Echo(string value) { return value; } }
            }
            """;
        var result = new CSharpToCalorConverter(new() { NullableContextOptions = nullableContext })
            .ConvertProject(source);
        Assert.True(result.Success);
        Assert.False(result.IsSplit);
        var output = Assert.Single(result.Outputs).Conversion;
        if (!native)
        {
            Assert.Equal("namespace-topology", Assert.Single(output.Losses).Feature);
            Assert.Equal(source, new CSharpEmitter().Emit(Parse(output.CalorSource!)));
            Assert.Contains(output.Issues, issue => issue.Feature == "namespace-split-refused"
                && issue.Message.Contains("declared type/nullability metadata")
                && issue.Message.Contains("[None]") && issue.Message.Contains("[Annotated]"));
        }
        else
        {
            Assert.Empty(output.Losses);
            var original = Compile(source).WithOptions(new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: nullableContext));
            var generated = Compile(new CSharpEmitter().Emit(Parse(output.CalorSource!)));
            foreach (var compilation in new[] { original, generated })
            {
                var echo = Assert.Single(compilation.GetTypeByMetadataName("Witness.Probe")!.GetMembers("Echo").OfType<IMethodSymbol>());
                Assert.Equal(NullableAnnotation.NotAnnotated, echo.ReturnNullableAnnotation);
                Assert.Equal(NullableAnnotation.NotAnnotated, echo.Parameters.Single().NullableAnnotation);
            }
        }
    }

    [Fact]
    public void ProjectScopedImportsRequireOriginalContextAndKeepStrictEffects()
    {
        const string source = """
            namespace Witness
            {
                using System.Threading.Tasks;
                public static class Probe { public static void Read() { Console.WriteLine("not pure"); } }
            }
            """;
        var standalone = new CSharpToCalorConverter().ConvertProject(source);
        Assert.False(standalone.Success);
        Assert.False(standalone.IsSplit);
        Assert.Contains(Assert.Single(standalone.Outputs).Conversion.Issues,
            issue => issue.Feature == "namespace-split-source-validation" && issue.Message.Contains("CS0103"));
        var evaluated = new CSharpToCalorConverter(new() { IncludeImplicitSystemUsing = true }).ConvertProject(source);
        Assert.True(evaluated.Success);
        var conversion = Assert.Single(evaluated.Outputs).Conversion;
        Assert.Empty(conversion.Losses);
        Assert.True(conversion.Metadata.IncludeImplicitSystemUsing);
        Assert.Equal("verified", conversion.Paths.RoundTrip);
        var native = conversion.CalorSource!;
        Assert.Contains("! cw", native);
        var rejected = Program.Compile(native.Replace("! cw", "!", StringComparison.Ordinal));
        Assert.Contains(rejected.Diagnostics.Errors, error => error.Code == DiagnosticCode.ForbiddenEffect);
        var unknown = Program.Compile("module Imports Witness\n  namespace using System\n  func Read () → void\n    !\n    Missing.Run()");
        Assert.Contains(unknown.Diagnostics.Errors, error => error.Code == DiagnosticCode.ForbiddenEffect
            && error.Message.Contains("unknown"));
    }

    [Theory]
    [InlineData(false, "using Picks;", "Item", NullableContextOptions.Disable)]
    [InlineData(true, "using Picks;", "Item", NullableContextOptions.Enable)]
    [InlineData(false, "using Choice = Picks.Item;", "Choice", NullableContextOptions.Enable)]
    [InlineData(true, "using Choice = Picks.Item;", "Choice", NullableContextOptions.Disable)]
    [InlineData(false, "using static Helpers;", "Item", NullableContextOptions.Disable)]
    [InlineData(true, "using static Helpers;", "Item", NullableContextOptions.Enable)]
    public void SelectedBranchLossyImportsKeepLexicalBindingAndDeclaredAnnotations(
        bool fileScoped, string import, string typeName, NullableContextOptions nullableContext)
    {
        const string companions = """
            namespace Picks { public class Item {} }
            namespace Witness.Picks { public class Item {} }
            public static class Helpers { public class Item {} }
            namespace Witness { public static class Helpers { public class Item {} } }
            """;
        var source = $$"""
            #region outer
            using Choice = System.Int16;
            namespace Witness{{(fileScoped ? ";" : " {")}}
            #region imports
            {{import}}
            #endregion
            public class Probe
            {
                public {{typeName}} Echo({{typeName}} value) { return value; }
                public string Text(string value) { return value; }
                public string? Maybe(string? value) { return value; }
            }
            {{(fileScoped ? "" : "}")}}
            #endregion
            """;
        var options = new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossy,
            PreprocessorMode = PreprocessorConversionMode.SelectActiveBranchLossy,
            NullableContextOptions = nullableContext,
            AdditionalSemanticSyntaxTrees = [CSharpSyntaxTree.ParseText(companions, new CSharpParseOptions(LanguageVersion.Preview))]
        };
        var result = new CSharpToCalorConverter(options).Convert(source, "Imports.cs");
        Assert.True(result.Success, string.Join("\n", result.Issues.Concat<object>(result.Losses)));
        Assert.Empty(result.Losses);
        Assert.Equal("verified", result.Paths.RoundTrip);
        var original = CompileWithCompanions(source, companions).WithOptions(
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: nullableContext));
        var originalTree = original.SyntaxTrees.First();
        foreach (var native in new[] { result.CalorSource!, new CalorEmitter().Emit(Parse(result.CalorSource!)) })
        {
            var module = Parse(native);
            Assert.Empty(module.InteropBlocks);
            Assert.Equal(2, module.Usings.Count);
            Assert.Null(module.Usings[0].NamespaceScopeId);
            Assert.Equal(module.NamespaceScopes.Single().Id, module.Usings[1].NamespaceScopeId);
            Assert.Equal(fileScoped, module.NamespaceScopes.Single().IsFileScoped);
            var compiled = Program.Compile(native, null, new CompilationOptions { DeferGeneratedOutputValidation = true });
            Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
            var generated = CompileWithCompanions(compiled.GeneratedCode, companions);
            var generatedTree = generated.SyntaxTrees.First();
            Assert.Equal(CSharpToCalorConverter.DeclaredTypeMetadata(original.GetSemanticModel(originalTree),
                    originalTree.GetCompilationUnitRoot()),
                CSharpToCalorConverter.DeclaredTypeMetadata(generated.GetSemanticModel(generatedTree),
                    generatedTree.GetCompilationUnitRoot()));
            var echo = Assert.Single(generated.GetTypeByMetadataName("Witness.Probe")!.GetMembers("Echo").OfType<IMethodSymbol>());
            Assert.Equal(import.Contains("Helpers") ? "Witness.Helpers.Item" : "Witness.Picks.Item",
                echo.ReturnType.ToDisplayString());
        }
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public void SelectedBranchLossyImportsUseRoslynSelectionAndRecordEveryRemovedDirective(bool defined, int expected)
    {
        const string source = """
            namespace Witness
            {
                using System;
            #if ALT
                public class Probe { public int Read() { return 2; } }
            #else
            #nullable enable
                public class Probe { public int Read() { return 1; } }
            #endif
            }
            """;
        // The active nullable directive remains closed; only an inactive one may
        // be removed by the already-explicit selected-branch policy.
        var result = new CSharpToCalorConverter(new()
        {
            Fidelity = ConversionFidelity.Lossy,
            PreprocessorMode = PreprocessorConversionMode.SelectActiveBranchLossy,
            DefinedSymbols = defined ? ["ALT"] : []
        }).Convert(source);
        if (!defined)
        {
            Assert.False(result.Success);
            Assert.Null(result.CalorSource);
            Assert.Contains(result.Issues, issue => issue.Feature == "namespace-topology");
            return;
        }
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.Equal(3, result.Losses.Count(loss => loss.Kind == ConversionLossKind.PreprocessorStripped));
        Assert.Contains(result.Losses, loss => loss.Kind == ConversionLossKind.DirectiveRemoved
            && loss.Feature == "nullable-directive");
        Assert.Equal(["ALT"], result.Metadata.DefinedSymbols);
        Assert.Contains($"return {expected}", result.CalorSource);
        Assert.Equal("verified", result.Paths.RoundTrip);
    }

    [Theory]
    [InlineData("namespace Witness { using System; public class Probe { public Missing Echo(Missing value) { return value; } } }")]
    [InlineData("namespace Witness { using System.Threading; using System.Timers; public class Probe { public Timer Echo(Timer value) { return value; } } }")]
    [InlineData("namespace Witness { using System; public class Probe { public int Read() { return \"wrong\"; } } }")]
    public void SelectedBranchLossyImportsRefuseInvalidAmbiguousOrIncompleteContext(string source)
    {
        var result = new CSharpToCalorConverter(new()
        {
            Fidelity = ConversionFidelity.Lossy,
            PreprocessorMode = PreprocessorConversionMode.SelectActiveBranchLossy
        }).Convert(source);
        Assert.False(result.Success);
        Assert.Null(result.CalorSource);
        Assert.Contains(result.Issues, issue => issue.Feature == "scoped-namespace-source-validation");
    }

    [Fact]
    public void SelectedBranchLossyImportsRequireSuppliedReferencesAndRealValidation()
    {
        const string source = "namespace Witness { using System; public class Probe {} }";
        var missing = new CSharpToCalorConverter(new()
        {
            Fidelity = ConversionFidelity.Lossy,
            PreprocessorMode = PreprocessorConversionMode.SelectActiveBranchLossy,
            References = [new ConversionReference(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dll"), [])]
        }).Convert(source);
        Assert.False(missing.Success);
        Assert.Null(missing.CalorSource);
        Assert.Contains(missing.Issues, issue => issue.Feature == "scoped-namespace-source-validation");
        var deferred = new CSharpToCalorConverter(new()
        {
            Fidelity = ConversionFidelity.Lossy,
            PreprocessorMode = PreprocessorConversionMode.SelectActiveBranchLossy,
            ValidateRoundTripCSharp = false
        }).Convert(source);
        Assert.False(deferred.Success);
        Assert.Null(deferred.CalorSource);
        Assert.Contains(deferred.Issues, issue => issue.Feature == "namespace-topology");
    }

    [Fact]
    public void SelectedBranchLossyImportsDoNotCountPreservedBodiesAsNative()
    {
        const string source = """
            namespace Witness
            {
                using System;
                public static class Probe { public static object Read() { return global::System.EventArgs.Empty; } }
            }
            """;
        var result = new CSharpToCalorConverter(new()
        {
            Fidelity = ConversionFidelity.Lossy,
            PreprocessorMode = PreprocessorConversionMode.SelectActiveBranchLossy
        }).Convert(source);
        Assert.False(result.Success);
        Assert.Null(result.CalorSource);
        Assert.Contains(result.Losses, loss => loss.Kind == ConversionLossKind.InteropPreserved);
        Assert.Contains(result.Issues, issue => issue.Feature == "scoped-namespace-fidelity");
    }

    [Fact]
    public void SelectedBranchLossyImportsKeepStrictEffectChecks()
    {
        const string source = """
            namespace Witness
            {
                using System;
                public static class Probe { public static void Read() { Console.WriteLine("not pure"); } }
            }
            """;
        var result = new CSharpToCalorConverter(new()
        {
            Fidelity = ConversionFidelity.Lossy,
            PreprocessorMode = PreprocessorConversionMode.SelectActiveBranchLossy
        }).Convert(source);
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.Empty(result.Losses);
        Assert.Contains("! cw", result.CalorSource);
        var rejected = Program.Compile(result.CalorSource!.Replace("! cw", "!", StringComparison.Ordinal));
        Assert.Contains(rejected.Diagnostics.Errors, error => error.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Fact]
    public void SelectedBranchLossyImportsRefuseWarningOnlyUnknownCallbackTransport()
    {
        const string source = """
            namespace Witness
            {
                using System;
                public static class Probe
                {
                    public static Func<int> Keep(Func<int> callback) { return callback; }
                }
            }
            """;
        const string native = """
            module Issue1104DepthSweep Witness block overflow unchecked nullable oblivious
              namespace using System

              module Probe
                func Keep (Func<int>:callback) → Func<int>
                  return callback
            """;
        Compile(source);
        var ordinary = Program.Compile(native);
        Assert.False(ordinary.HasErrors, string.Join("\n", ordinary.Diagnostics));
        Assert.Equal(DiagnosticCode.EffectRowUnknown, Assert.Single(ordinary.Diagnostics.Warnings).Code);
        var strict = Program.Compile(native, null, new CompilationOptions { StrictEffects = true });
        Assert.Equal(DiagnosticCode.EffectRowUnknown, Assert.Single(strict.Diagnostics.Errors).Code);

        var result = new CSharpToCalorConverter(new()
        {
            Fidelity = ConversionFidelity.Lossy,
            PreprocessorMode = PreprocessorConversionMode.SelectActiveBranchLossy,
            ModuleName = "Issue1104DepthSweep"
        }).Convert(source, "CallbackTransport.cs");
        Assert.False(result.Success);
        Assert.Null(result.CalorSource);
        Assert.Empty(result.Losses);
        Assert.Equal("failed", result.Paths.RoundTrip);
        Assert.Contains(result.Issues, issue => issue.Feature == "scoped-namespace-fidelity"
            && issue.Message.Contains("The return of 'Keep' is function-typed with no effect row"));
    }

    [Fact]
    public void SelectedBranchLossyImportsRetainOrdinaryStrictSuccess()
    {
        const string source = """
            namespace Witness
            {
                using System;
                public static class Probe { public static int Echo(int value) { return value; } }
            }
            """;
        var result = new CSharpToCalorConverter(new()
        {
            Fidelity = ConversionFidelity.Lossy,
            PreprocessorMode = PreprocessorConversionMode.SelectActiveBranchLossy
        }).Convert(source);
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.Empty(result.Losses);
        Assert.Equal("verified", result.Paths.RoundTrip);
        var strict = Program.Compile(result.CalorSource!, null, new CompilationOptions { StrictEffects = true });
        Assert.False(strict.HasErrors, string.Join("\n", strict.Diagnostics));
        Assert.Empty(strict.Diagnostics);
    }

    [Theory]
    [InlineData("#nullable enable")]
    [InlineData("#pragma warning disable CS0168")]
    [InlineData("#warning keep warning")]
    [InlineData("#error keep error")]
    [InlineData("#line 20 \"other.cs\"")]
    [InlineData("#region unclosed")]
    [InlineData("#endregion")]
    public void SelectedBranchLossyImportsDoNotAdmitOtherDirectivesOrUnbalancedRegions(string directive)
    {
        var result = new CSharpToCalorConverter(new()
        {
            Fidelity = ConversionFidelity.Lossy,
            PreprocessorMode = PreprocessorConversionMode.SelectActiveBranchLossy
        }).Convert($"namespace Witness {{\nusing System;\n{directive}\npublic class Probe {{}}\n}}");
        Assert.False(result.Success);
        Assert.Null(result.CalorSource);
        Assert.NotEmpty(result.Issues);
    }

    [Theory]
    [InlineData("namespace Witness { using System; class Probe {} } namespace Other { class Item {} }")]
    [InlineData("namespace Witness { using System; namespace Nested { class Probe {} } }")]
    [InlineData("namespace Witness { using Pair = (int X, int Y); class Probe {} }")]
    [InlineData("namespace Witness { using unsafe Pointer = int*; class Probe {} }")]
    [InlineData("extern alias Library; namespace Witness { using System; class Probe {} }")]
    [InlineData("[assembly: System.Reflection.AssemblyTitle(\"test\")] namespace Witness { using System; class Probe {} }")]
    public void SelectedBranchLossyImportsKeepUnsupportedTopologiesClosed(string source)
    {
        var result = new CSharpToCalorConverter(new()
        {
            Fidelity = ConversionFidelity.Lossy,
            PreprocessorMode = PreprocessorConversionMode.SelectActiveBranchLossy
        }).Convert(source);
        Assert.False(result.Success);
        Assert.Null(result.CalorSource);
        Assert.Contains(result.Issues, issue => issue.Feature == "namespace-topology");
    }

    [Theory]
    [InlineData("module Imports\n  namespace using System")]
    [InlineData("module Imports Witness\n  namespace global using System")]
    [InlineData("module Imports Witness\n  namespace\n  using System")]
    [InlineData("module Imports Witness\n  namespace using\n  class Probe")]
    [InlineData("module Imports Witness\n  class Probe\n    namespace using System")]
    public void NamespaceUsingRejectsMissingNamedScopeAndMalformedPlacement(string source)
    {
        var diagnostics = new DiagnosticBag();
        _ = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.True(diagnostics.HasErrors);
    }

    [Theory]
    [InlineData("internal interface ISource { int Get(); }", "", "ISource", Accessibility.Internal)]
    [InlineData("namespace Contoso.Tools; internal interface ISource { int Get(); }",
        "Contoso.Tools", "ISource", Accessibility.Internal)]
    [InlineData("namespace Contoso.@event { public class Owner { private interface ISource { int Get(); } } }",
        "Contoso.event", "Owner+ISource", Accessibility.Private)]
    [InlineData("namespace _global { public class Owner { protected interface ISource { int Get(); } } }",
        "_global", "Owner+ISource", Accessibility.Protected)]
    public void RealConverterNativeSourceAndClrPreserveOriginalInterfaceExports(
        string source, string ns, string type, Accessibility expected)
    {
        var conversion = new CSharpToCalorConverter().Convert(source);
        Assert.True(conversion.Success, string.Join("\n", conversion.Context.Issues));
        Assert.NotNull(conversion.CalorSource);
        Assert.DoesNotContain("raw ", conversion.CalorSource);
        Assert.DoesNotContain('§', conversion.CalorSource);
        var module = Parse(conversion.CalorSource);
        Assert.Equal(ns, module.NamespaceScopes.Single().FullName);
        var compiled = Compile(new CSharpEmitter().Emit(module));
        var original = Compile(source);
        var metadataName = ns.Length == 0 ? type : $"{ns}.{type}";
        Assert.Equal(expected, original.GetTypeByMetadataName(metadataName)!.DeclaredAccessibility);
        Assert.Equal(expected, compiled.GetTypeByMetadataName(metadataName)!.DeclaredAccessibility);
    }

    [Theory]
    [InlineData("UsualFile.cs", "UsualFile")]
    [InlineData("a4-created.cs", "a4created")]
    [InlineData("123-array-input.cs", "_123arrayinput")]
    [InlineData("space label.cs", "spacelabel")]
    [InlineData("snapshot#1.verified.cs", "snapshot_1.verified")]
    [InlineData("global::Source.cs", "global__Source")]
    [InlineData("---.cs", "_")]
    [InlineData("class.cs", "class")]
    [InlineData("module.cs", "module")]
    [InlineData("café Δ.cs", "caféΔ")]
    public void FileDerivedLabelsAreNativeIdentifiersWithoutChangingClrIdentity(
        string sourceFile, string expectedLabel)
    {
        const string source = "public static class Functions { public static int Read() => 42; }";
        var conversion = new CSharpToCalorConverter().Convert(source, sourceFile);
        Assert.True(conversion.Success, string.Join("\n", conversion.Issues));
        Assert.Equal(0, conversion.InteropPreservationCount);
        Assert.Equal(0, conversion.LossySubstitutionCount);
        Assert.Equal(0, conversion.DropCount);
        var module = Parse(conversion.CalorSource!);
        Assert.Equal(expectedLabel, module.Name);
        Assert.True(Assert.Single(module.NamespaceScopes).IsGlobal);
        var original = Compile(source);
        var generated = Compile(new CSharpEmitter().Emit(module));
        var originalType = Assert.IsAssignableFrom<INamedTypeSymbol>(
            original.GetTypeByMetadataName("Functions"));
        var generatedType = Assert.IsAssignableFrom<INamedTypeSymbol>(
            generated.GetTypeByMetadataName("Functions"));
        Assert.Equal(originalType.ToDisplayString(), generatedType.ToDisplayString());
        Assert.Equal(originalType.DeclaredAccessibility, generatedType.DeclaredAccessibility);
        Assert.Equal(SpecialType.System_Int32,
            Assert.IsAssignableFrom<IMethodSymbol>(Assert.Single(generatedType.GetMembers("Read")))
                .ReturnType.SpecialType);
        Assert.Equal(["Functions"], generated.Assembly.GlobalNamespace.GetTypeMembers().Select(type => type.Name));
    }

    [Theory]
    [InlineData("", "", "")]
    [InlineData("namespace Source.Owners;", "", "Source.Owners")]
    [InlineData("namespace Source.Owners {", "}", "Source.Owners")]
    public void ExplicitModuleLabelsPreserveSourceDeclarationOwners(
        string opening, string closing, string expectedNamespace)
    {
        var source = $$"""
            {{opening}}
            public class Node
            {
                public int Id;
                public class Nested { }
            }
            public class Demo
            {
                public static Node Echo(Node value) => value;
            }
            {{closing}}
            """;
        var original = Compile(source);
        var originalTree = original.SyntaxTrees.Single();
        var expectedMetadata = CSharpToCalorConverter.DeclaredTypeMetadata(
            original.GetSemanticModel(originalTree), originalTree.GetCompilationUnitRoot());
        var conversion = new CSharpToCalorConverter(new ConversionOptions { ModuleName = "ReviewFile" })
            .Convert(source, "Input.cs");
        Assert.True(conversion.Success, string.Join("\n", conversion.Issues));
        Assert.Empty(conversion.Losses);
        Assert.Equal(0, conversion.InteropPreservationCount);

        foreach (var native in new[] { conversion.CalorSource!, new CalorEmitter().Emit(Parse(conversion.CalorSource!)) })
        {
            var module = Parse(native);
            Assert.Equal("ReviewFile", module.Name);
            Assert.Equal(expectedNamespace, Assert.Single(module.NamespaceScopes).FullName);
            Assert.All(module.Classes, type => Assert.Equal(expectedNamespace, type.NamespaceIdentity));
            var node = module.Classes.Single(type => type.Name == "Node");
            var prefix = expectedNamespace.Length == 0 ? "" : expectedNamespace + ".";
            Assert.Equal($"global::{prefix}Node+Nested",
                Assert.Single(node.NestedClasses).FullyQualifiedSymbolIdentity);

            var compiled = Program.Compile(native, "ReviewFile.calr");
            Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics.Errors));
            var generated = Compile(compiled.GeneratedCode);
            var generatedTree = generated.SyntaxTrees.Single();
            Assert.Equal(expectedMetadata, CSharpToCalorConverter.DeclaredTypeMetadata(
                generated.GetSemanticModel(generatedTree), generatedTree.GetCompilationUnitRoot()));
        }
    }

    [Theory]
    [InlineData("namespace A { class C {} } namespace A { class D {} }")]
    [InlineData("namespace A { namespace B { class C {} } }")]
    [InlineData("namespace A { using System; class C {} }")]
    public void OtherLexicalTopologiesPreserveTheOriginalWholeFile(string source)
    {
        var conversion = new CSharpToCalorConverter().Convert(source);
        Assert.True(conversion.Success, string.Join("\n", conversion.Context.Issues));
        var parsed = Parse(conversion.CalorSource!);
        var preserved = Assert.Single(parsed.InteropBlocks);
        Assert.True(preserved.IsCompilationUnitPassthrough);
        Assert.Equal(source, preserved.CSharpCode);
        Compile(new CSharpEmitter().Emit(parsed));
    }

    [Theory]
    [InlineData("module Grid overflow=checked")]
    [InlineData("module Grid Contoso.Tools Extra")]
    [InlineData("module Grid Contoso.event")]
    [InlineData("module Grid Contoso.Tools overflow impossible")]
    public void HeaderAliasesAndMalformedNamespaceFieldsAreDiagnosed(string header)
    {
        var diagnostics = new DiagnosticBag();
        _ = new Parser(new Lexer($"{header}\n  class Area", diagnostics)
            .TokenizeAllForParser(), diagnostics).Parse();
        Assert.True(diagnostics.HasErrors);
    }

    [Theory]
    [InlineData("global::System.EventArgs.Empty", true, ConversionFidelity.Lossless)]
    [InlineData("global::System.EventArgs.Empty", false, ConversionFidelity.Lossless)]
    [InlineData("Root::EventArgs.Empty", true, ConversionFidelity.Lossless)]
    [InlineData("Root::EventArgs.Empty", false, ConversionFidelity.Lossless)]
    [InlineData("global::System.EventArgs.Empty", true, ConversionFidelity.Lossy)]
    [InlineData("global::System.EventArgs.Empty", false, ConversionFidelity.Lossy)]
    [InlineData("Root::EventArgs.Empty", true, ConversionFidelity.Lossy)]
    [InlineData("Root::EventArgs.Empty", false, ConversionFidelity.Lossy)]
    public void AliasQualifiedValuesPreserveTheOriginalMemberSymbolAndRuntime(
        string expression, bool shadow, ConversionFidelity fidelity)
    {
        var source = QualifiedValueWitness(expression, shadow,
            "public static object Read() { return /* keep qualifier */ VALUE; }");
        var original = Compile(source);
        AssertEmptyFieldTargetAndRuntime(original, "System.EventArgs");
        var originalMethod = original.SyntaxTrees.Single().GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>().Single();
        var result = new CSharpToCalorConverter(new ConversionOptions { Fidelity = fidelity }).Convert(source);
        Assert.True(result.Success, string.Join("\n", result.Issues));
        var loss = Assert.Single(result.Losses);
        Assert.Equal(ConversionLossKind.InteropPreserved, loss.Kind);
        Assert.Equal("alias-qualified-value", loss.Feature);
        Assert.Equal(ConversionPath.Interop, loss.Path);
        Assert.Equal("preserved", result.Paths.Outcome);
        Assert.Equal(1, result.InteropPreservationCount);
        Assert.Equal(0, result.LossySubstitutionCount);
        Assert.Equal(0, result.DropCount);
        Assert.NotNull(FeatureSupport.GetFeatureInfo(loss.Feature));
        Assert.Equal(SupportLevel.NotSupported, FeatureSupport.GetSupportLevel(loss.Feature));

        foreach (var native in new[] { result.CalorSource!, new CalorEmitter().Emit(Parse(result.CalorSource!)) })
        {
            var module = Parse(native);
            Assert.Empty(module.InteropBlocks);
            var scope = Assert.Single(module.NamespaceScopes);
            Assert.Equal("AliasFidelity", scope.FullName);
            var alias = Assert.Single(module.Usings);
            Assert.Equal("Root", alias.Alias);
            Assert.Equal("global::System", alias.Namespace);
            Assert.Null(alias.NamespaceScopeId);
            Assert.False(alias.IsGlobal);
            var probe = module.Classes.Single(type => type.Name == "Probe");
            Assert.True(probe.IsModule);
            Assert.Empty(probe.Methods);
            var preserved = Assert.Single(probe.InteropBlocks);
            Assert.Equal("alias-qualified-value", preserved.FeatureName);
            Assert.Equal(InteropMemberKind.Method, preserved.MemberKind);
            Assert.Equal("AliasFidelity", probe.NamespaceIdentity);
            Assert.Equal(scope.Id, probe.NamespaceScopeId);
            Assert.Equal(originalMethod.ToFullString(), preserved.CSharpCode);
            AssertEmptyFieldTargetAndRuntime(Compile(new CSharpEmitter().Emit(module)), "System.EventArgs");
        }

        var originalBlock = Assert.Single(result.Ast!.Classes.Single(type => type.Name == "Probe").InteropBlocks);
        Assert.Equal("AliasFidelity", originalBlock.NamespaceIdentity);
        Assert.Equal(result.Ast.NamespaceScopes.Single().Id, originalBlock.NamespaceScopeId);
        Assert.Equal(originalMethod.SpanStart, originalBlock.Span.Start);
        Assert.Equal(originalMethod.Span.Length, originalBlock.Span.Length);
        var position = originalMethod.GetLocation().GetLineSpan().StartLinePosition;
        Assert.Equal(position.Line + 1, originalBlock.Span.Line);
        Assert.Equal(position.Character + 1, originalBlock.Span.Column);
    }

    [Theory]
    [InlineData("public static object Value = VALUE; public static object Read() { return Value; }",
        InteropMemberKind.Field)]
    [InlineData("public static object Value { get { return VALUE; } } public static object Read() { return Value; }",
        InteropMemberKind.Property)]
    [InlineData("private static object Value; static Probe() { Value = VALUE; } public static object Read() { return Value; }",
        InteropMemberKind.Constructor)]
    public void AliasQualifiedInitializersAndAccessorsPreserveTheirRequiredMemberBoundary(
        string member, InteropMemberKind expectedKind)
    {
        var source = QualifiedValueWitness("global::System.EventArgs.Empty", true, member);
        AssertEmptyFieldTargetAndRuntime(Compile(source), "System.EventArgs");
        var result = new CSharpToCalorConverter().Convert(source);
        Assert.True(result.Success, string.Join("\n", result.Issues));
        var loss = Assert.Single(result.Losses);
        Assert.Equal(ConversionLossKind.InteropPreserved, loss.Kind);
        Assert.Equal("alias-qualified-value", loss.Feature);
        foreach (var native in new[] { result.CalorSource!, new CalorEmitter().Emit(Parse(result.CalorSource!)) })
        {
            var module = Parse(native);
            var preserved = Assert.Single(module.Classes.Single(type => type.Name == "Probe").InteropBlocks);
            Assert.Equal(expectedKind, preserved.MemberKind);
            Assert.Equal("alias-qualified-value", preserved.FeatureName);
            Assert.Contains("global::System.EventArgs.Empty", preserved.CSharpCode);
            AssertEmptyFieldTargetAndRuntime(Compile(new CSharpEmitter().Emit(module)), "System.EventArgs");
        }
    }

    [Theory]
    [InlineData(false, "System.EventArgs")]
    [InlineData(true, "AliasFidelity.System.EventArgs")]
    public void OrdinaryValuesRetainNativeShadowBinding(bool shadow, string declaringType)
    {
        var source = QualifiedValueWitness("System.EventArgs.Empty", shadow,
            "public static object Read() { return VALUE; }");
        AssertEmptyFieldTargetAndRuntime(Compile(source), declaringType);
        var result = new CSharpToCalorConverter().Convert(source);
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.Empty(result.Losses);
        Assert.Equal("native", result.Paths.Outcome);
        foreach (var native in new[] { result.CalorSource!, new CalorEmitter().Emit(Parse(result.CalorSource!)) })
            AssertEmptyFieldTargetAndRuntime(Compile(new CSharpEmitter().Emit(Parse(native))), declaringType);
    }

    [Fact]
    public void AliasQualifiedTypesUsingsConstraintsAndNameofRemainNative()
    {
        const string source = """
            using Root = global::System;
            namespace AliasFidelity;
            public class Probe<T> where T : global::System.EventArgs
            {
                public global::System.EventArgs Echo(Root::EventArgs value) { return value; }
                public string Name() { return nameof(global::System.EventArgs.Empty); }
            }
            """;
        var result = new CSharpToCalorConverter().Convert(source);
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.Empty(result.Losses);
        Assert.Equal("native", result.Paths.Outcome);
        foreach (var native in new[] { result.CalorSource!, new CalorEmitter().Emit(Parse(result.CalorSource!)) })
        {
            var compiled = Compile(new CSharpEmitter().Emit(Parse(native)));
            var probe = compiled.GetTypeByMetadataName("AliasFidelity.Probe`1")!;
            var constraint = Assert.Single(probe.TypeParameters.Single().ConstraintTypes);
            Assert.Equal("System.EventArgs", constraint.ToDisplayString());
            var echo = Assert.Single(probe.GetMembers("Echo").OfType<IMethodSymbol>());
            Assert.Equal("System.EventArgs", echo.ReturnType.ToDisplayString());
            Assert.True(SymbolEqualityComparer.Default.Equals(echo.ReturnType, echo.Parameters.Single().Type));
            Assert.Equal("Empty", Invoke(Load(compiled).GetType("AliasFidelity.Probe`1")!
                .MakeGenericType(typeof(EventArgs)), "Name"));
        }
    }

    [Theory]
    [InlineData("global::System.EventArgs.Empty", 18)]
    [InlineData("Root::EventArgs.Empty", 16)]
    public void AuthoredAliasQualifiedValuesRemainExplicitCapabilityErrors(string expression, int column)
    {
        var source = $"module Values\n  func Read () → object\n    return {expression}";
        var diagnostics = new DiagnosticBag();
        _ = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.UnexpectedToken
            && diagnostic.Span.Line == 3 && diagnostic.Span.Column == column);
        Assert.Equal("::", source.Substring(source.LastIndexOf('\n') + column, 2));
    }

    [Fact]
    public void NamespaceLocalAliasContextStillPreservesTheExactWholeFile()
    {
        const string source = """
            namespace AliasFidelity
            {
                // This alias belongs only to this namespace.
            #if true
                using Root = global::System;
            #endif
                public static class Probe
                {
                    public static object Read() { return Root::EventArgs.Empty; }
                }
            }
            """;
        var result = new CSharpToCalorConverter().Convert(source);
        Assert.True(result.Success, string.Join("\n", result.Issues));
        var loss = Assert.Single(result.Losses);
        Assert.Equal(ConversionLossKind.InteropPreserved, loss.Kind);
        Assert.Equal("namespace-topology", loss.Feature);
        foreach (var native in new[] { result.CalorSource!, new CalorEmitter().Emit(Parse(result.CalorSource!)) })
        {
            var module = Parse(native);
            var preserved = Assert.Single(module.InteropBlocks);
            Assert.True(preserved.IsCompilationUnitPassthrough);
            Assert.Equal(source, preserved.CSharpCode);
            AssertEmptyFieldTargetAndRuntime(Compile(new CSharpEmitter().Emit(module)), "System.EventArgs");
        }
    }

    [Theory]
    [InlineData("public static int Read(string text) => text.Length;", "Calor0274")]
    [InlineData("public static bool Equal(object left, object right) => object.Equals(left, right);", "Calor0410")]
    public void ScopedImportFallbackRetainsTheActualNativeCandidateRefusal(string member, string code)
    {
        var source = $$"""
            namespace Witness
            {
                using System;
                public static class Probe { {{member}} }
            }
            """;
        var project = new CSharpToCalorConverter().ConvertProject(source, "Probe.cs");
        Assert.True(project.Success);
        Assert.False(project.IsSplit);
        var conversion = Assert.Single(project.Outputs).Conversion;
        Assert.True(conversion.InteropPreservationCount > 0);
        Assert.Contains(conversion.Issues, issue =>
            issue.Severity == ConversionIssueSeverity.Warning
            && issue.Feature == "roundtrip-calor-validation"
            && issue.Line is > 0 && issue.Column is > 0
            && issue.Message.Contains("Rejected native namespace candidate", StringComparison.Ordinal)
            && issue.Message.Contains(code, StringComparison.Ordinal));
        Assert.Contains(conversion.Ast!.InteropBlocks, block => block.CSharpCode == source);
        var compiled = Program.Compile(conversion.CalorSource!);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
    }

    private static string QualifiedValueWitness(string expression, bool shadow, string member) => $$"""
        using Root = global::System;
        namespace AliasFidelity;
        {{(shadow ? "public static class System { public static class EventArgs { public static int Empty = 7; } }" : "")}}
        public static class Probe
        {
            {{member.Replace("VALUE", expression, StringComparison.Ordinal)}}
        }
        """;

    private static void AssertEmptyFieldTargetAndRuntime(CSharpCompilation compilation, string declaringType)
    {
        var tree = compilation.SyntaxTrees.Single();
        var access = tree.GetRoot().DescendantNodes().OfType<MemberAccessExpressionSyntax>()
            .Single(node => node.Name.Identifier.ValueText == "Empty");
        var symbol = Assert.IsAssignableFrom<IFieldSymbol>(
            compilation.GetSemanticModel(tree).GetSymbolInfo(access).Symbol);
        Assert.Equal(declaringType, symbol.ContainingType.ToDisplayString());
        Assert.True(symbol.IsStatic);
        var value = Invoke(Load(compilation).GetType("AliasFidelity.Probe")!, "Read");
        if (declaringType == "System.EventArgs")
        {
            Assert.Equal(typeof(EventArgs).Assembly.GetName().Name, symbol.ContainingAssembly.Name);
            Assert.Same(EventArgs.Empty, value);
        }
        else
        {
            Assert.True(SymbolEqualityComparer.Default.Equals(compilation.Assembly, symbol.ContainingAssembly));
            Assert.Equal(7, Assert.IsType<int>(value));
        }
    }

    private static Assembly Load(CSharpCompilation compilation)
    {
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }

    private static object? Invoke(Type type, string method)
    {
        var target = type.GetMethod(method)!;
        return target.Invoke(target.IsStatic ? null : Activator.CreateInstance(type), null);
    }

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        return module;
    }

    private static CSharpCompilation Compile(string source)
    {
        var compilation = CSharpCompilation.Create("NamespaceExports",
            [CSharpSyntaxTree.ParseText(source)],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        return compilation;
    }

    private static CSharpCompilation CompileWithCompanions(string source, string companions)
    {
        var compilation = Compile("").RemoveAllSyntaxTrees()
            .WithOptions(new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable))
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(source), CSharpSyntaxTree.ParseText(companions));
        Assert.Empty(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        return compilation;
    }
}
