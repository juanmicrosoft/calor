using System.Reflection;
using System.Runtime.Loader;
using Calor.Compiler.Analysis;
using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Analysis.Security;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Formatting;
using Calor.Compiler.Migration;
using Calor.Compiler.Migration.Project;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using CalorBinder = Calor.Compiler.Binding.Binder;

namespace Calor.Compiler.Tests;

public class Issue769NamespaceTopologyTests
{
    [Fact]
    public void CompilationReferences_ReuseTheExactPlatformSetAndLoadedMetadata()
    {
        var first = CreateCompilation("public class First {}");
        var second = CreateCompilation("public class Second {}");
        var firstReferences = first.References.Cast<PortableExecutableReference>().ToArray();
        var secondReferences = second.References.Cast<PortableExecutableReference>().ToArray();

        Assert.NotSame(first, second);
        Assert.Equal(
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator),
            firstReferences.Select(reference => reference.FilePath));
        Assert.Equal(firstReferences.Length, secondReferences.Length);
        for (var index = 0; index < firstReferences.Length; index++)
        {
            Assert.Same(TrustedPlatformReferences.All[index], firstReferences[index]);
            Assert.Same(firstReferences[index], secondReferences[index]);
            Assert.Equal(
                firstReferences[index].GetMetadataId(),
                secondReferences[index].GetMetadataId());
        }
    }

    [Fact]
    public void NamespaceScope_MultilineEnumValue_DoesNotCaptureFollowingClass()
    {
        const string source =
            """
            §M{Topology}
              §NS{Example}
                §EN{Mode:pub}
                  None = 0
                  First = 1
                  Second = 2
                  All = First
                    | Second
                §CL{Survivor:pub}
            """;

        var module = ParseCalor(source);

        var scope = Assert.Single(module.NamespaceScopes);
        Assert.Equal("Example", scope.FullName);
        Assert.Equal(scope.Id, Assert.Single(module.Enums).NamespaceScopeId);
        var survivor = Assert.Single(module.Classes);
        Assert.Equal("Example", survivor.NamespaceIdentity);
        Assert.Equal(scope.Id, survivor.NamespaceScopeId);

        var compilation = Program.Compile(
            source,
            null,
            new CompilationOptions
            {
                EnforceEffects = false,
                UnknownCallPolicy =
                    Calor.Compiler.Effects.UnknownCallPolicy.Permissive
            });
        Assert.False(
            compilation.HasErrors,
            string.Join(
                Environment.NewLine,
                compilation.Diagnostics.Errors.Select(error => error.Message)));
        var generatedCompilation = CreateCompilation(compilation.GeneratedCode);
        AssertNoCompilationErrors(generatedCompilation);
        Assert.NotNull(
            generatedCompilation.GetTypeByMetadataName("Example.Survivor"));
    }

    [Fact]
    public void TypedBugPatternAnalysis_ConversionPreservesEnumAndFollowingClassScope()
    {
        var sourcePath = Path.Combine(
            RepositoryRoot(),
            "src",
            "Calor.Compiler",
            "Analysis",
            "BugPatterns",
            "TypedBugPatternAnalysis.cs");
        var result = ConvertLossy(File.ReadAllText(sourcePath));
        var reparsed = ParseCalor(result.CalorSource!);

        var enumDefinition = Assert.Single(
            reparsed.Enums.Where(item => item.Name == "TypedBugPatternKind"));
        var followingClass = Assert.Single(
            reparsed.Classes.Where(item => item.Name == "TypedBugPatternChecker"));
        var analysisInterop = Assert.Single(
            reparsed.InteropBlocks.Where(item =>
                item.CSharpCode.Contains(
                    "class TypedBugPatternAnalysis",
                    StringComparison.Ordinal)));
        Assert.Equal(
            "Calor.Compiler.Analysis.BugPatterns",
            enumDefinition.NamespaceIdentity);
        Assert.Equal(
            enumDefinition.NamespaceScopeId,
            followingClass.NamespaceScopeId);
        Assert.Equal(
            enumDefinition.NamespaceIdentity,
            followingClass.NamespaceIdentity);
        Assert.Equal(
            enumDefinition.NamespaceScopeId,
            analysisInterop.NamespaceScopeId);

        var generated = new CSharpEmitter().Emit(reparsed);
        Assert.Contains(
            "namespace Calor.Compiler.Analysis.BugPatterns",
            generated);
        Assert.Contains("class TypedBugPatternChecker", generated);
        Assert.Contains("class TypedBugPatternAnalysis", generated);
        Assert.Empty(
            CSharpSyntaxTree.ParseText(generated)
                .GetDiagnostics()
                .Where(diagnostic =>
                    diagnostic.Severity
                    == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
    }

    [Fact]
    public void GlobalDeclarations_StayGlobalThroughExplicitAndSyntheticScopes()
    {
        var result = ConvertLossy(
            "public class GlobalType { public int Value() => 42; }");
        var type = Assert.Single(result.Ast!.Classes);
        Assert.Equal("", type.NamespaceIdentity);
        Assert.Equal("global::GlobalType", type.FullyQualifiedSymbolIdentity);

        var explicitScopeGenerated = CompileConverted(result);
        Assert.DoesNotContain("namespace _global", explicitScopeGenerated);
        Assert.NotNull(CreateCompilation(explicitScopeGenerated)
            .GetTypeByMetadataName("GlobalType"));

        type.NamespaceScopeId = "orphan-global";
        var syntheticScopeGenerated = new CSharpEmitter().Emit(result.Ast);
        Assert.DoesNotContain("namespace _global", syntheticScopeGenerated);
        Assert.NotNull(CreateCompilation(syntheticScopeGenerated)
            .GetTypeByMetadataName("GlobalType"));
    }

    [Theory]
    [InlineData("protected", "prot", Accessibility.Protected)]
    [InlineData(
        "protected internal",
        "prot-int",
        Accessibility.ProtectedOrInternal)]
    [InlineData(
        "private protected",
        "priv-prot",
        Accessibility.ProtectedAndInternal)]
    public void NamespacedOverride_PreservesProtectedAccessibilityDuringSingleFileReplacement(
        string accessibility,
        string calorVisibility,
        Accessibility expectedAccessibility)
    {
        var baseSource =
            $$"""
            namespace FluentValidation.Validators;

            public interface IValidatorSelector { }
            public class ValidationContext<T> { }

            public class ChildValidatorAdaptor<T, TProperty>
            {
                {{accessibility}} virtual IValidatorSelector GetSelector(
                    ValidationContext<T> context,
                    TProperty value) => null;
            }
            """;
        var derivedSource =
            $$"""
            namespace FluentValidation.Validators;

            public class PolymorphicValidator<T, TProperty>
                : ChildValidatorAdaptor<T, TProperty>
            {
                {{accessibility}} override IValidatorSelector GetSelector(
                    ValidationContext<T> context,
                    TProperty value) => null;
            }
            """;

        var result = ConvertLossy(derivedSource);
        Assert.Contains(
            $"§MT{{GetSelector:{calorVisibility}:over}}",
            result.CalorSource);

        var generated = new CSharpEmitter().Emit(ParseCalor(result.CalorSource!));
        Assert.Contains($"{accessibility} override", generated);

        var compilation = CreateCompilation(baseSource, generated);
        AssertNoCompilationErrors(compilation);
        var derived = compilation.GetTypeByMetadataName(
            "FluentValidation.Validators.PolymorphicValidator`2");
        Assert.NotNull(derived);
        var method = Assert.Single(
            derived.GetMembers("GetSelector").OfType<IMethodSymbol>());
        Assert.Equal(expectedAccessibility, method.DeclaredAccessibility);
        Assert.Equal(
            expectedAccessibility,
            method.OverriddenMethod!.DeclaredAccessibility);
    }

    [Fact]
    public void LossyConversion_PreservesSameNamedTypesAcrossNamespaces()
    {
        const string source =
            """
            namespace Alpha
            {
                public class Widget
                {
                    public string Identity() => "alpha";
                }
            }

            namespace Beta
            {
                public class Widget
                {
                    public string Identity() => "beta";
                }
            }
            """;

        var result = ConvertLossy(source);

        Assert.Collection(
            result.Ast!.Classes.OrderBy(type => type.NamespaceIdentity),
            type =>
            {
                Assert.Equal("Alpha", type.NamespaceIdentity);
                Assert.Equal("global::Alpha.Widget", type.FullyQualifiedSymbolIdentity);
            },
            type =>
            {
                Assert.Equal("Beta", type.NamespaceIdentity);
                Assert.Equal("global::Beta.Widget", type.FullyQualifiedSymbolIdentity);
            });
        Assert.Equal(2, result.Ast.NamespaceScopes.Count);
        Assert.Contains("§NS{", result.CalorSource);

        var generated = CompileConverted(result);
        Assert.Contains("namespace Alpha", generated);
        Assert.Contains("namespace Beta", generated);
        Assert.True(GeneratedCSharpCompiler.Validate(generated).CompilationSuccess);

        using var loaded = CompileAndLoad(generated);
        Assert.Equal("alpha", InvokeIdentity(loaded.Assembly, "Alpha.Widget"));
        Assert.Equal("beta", InvokeIdentity(loaded.Assembly, "Beta.Widget"));
    }

    [Fact]
    public void NamespaceLocalAliases_DoNotLeakAcrossScopes()
    {
        const string source =
            """
            namespace Alpha
            {
                using Item = System.String;
                public class Holder { public Item Value = ""; }
            }

            namespace Beta
            {
                using Item = System.Int32;
                public class Holder { public Item Value = 42; }
            }
            """;

        var result = ConvertLossy(source);
        var aliases = result.Ast!.Usings.Where(item => item.Alias == "Item").ToList();
        Assert.Equal(2, aliases.Count);
        Assert.Equal(2, aliases.Select(item => item.NamespaceScopeId).Distinct().Count());
        Assert.Contains(aliases, item => item.NamespaceIdentity == "Alpha");
        Assert.Contains(aliases, item => item.NamespaceIdentity == "Beta");

        var generated = CompileConverted(result);
        var compilation = CreateCompilation(generated);
        AssertNoCompilationErrors(compilation);
        Assert.Equal(
            SpecialType.System_String,
            GetFieldType(compilation, "Alpha.Holder", "Value").SpecialType);
        Assert.Equal(
            SpecialType.System_Int32,
            GetFieldType(compilation, "Beta.Holder", "Value").SpecialType);
    }

    [Fact]
    public void NamespaceLocalStaticUsings_StayInTheirLexicalScopes()
    {
        const string source =
            """
            namespace Alpha
            {
                using static System.Math;
                public class Calculator { public double Value() => Abs(-1.0); }
            }

            namespace Beta
            {
                using static System.MathF;
                public class Calculator { public float Value() => Abs(-1.0f); }
            }
            """;

        var result = ConvertLossy(source);
        var staticUsings = result.Ast!.Usings.Where(item => item.IsStatic).ToList();
        Assert.Equal(2, staticUsings.Count);
        Assert.Contains(
            staticUsings,
            item => item.Namespace == "System.Math"
                    && item.NamespaceIdentity == "Alpha");
        Assert.Contains(
            staticUsings,
            item => item.Namespace == "System.MathF"
                    && item.NamespaceIdentity == "Beta");
        AssertNoCompilationErrors(CreateCompilation(CompileConverted(result)));
    }

    [Fact]
    public void GlobalAndNamespaceLocalUsings_KeepTheirVisibility()
    {
        const string source =
            """
            global using System.Collections.Generic;

            namespace Alpha
            {
                using Text = System.String;
                public class UsesBoth
                {
                    public List<Text> Values = new();
                }
            }

            namespace Beta
            {
                public class UsesGlobal
                {
                    public List<int> Values = new();
                }
            }
            """;

        var result = ConvertLossy(source);
        Assert.Contains(
            result.Ast!.Usings,
            item => item.IsGlobal
                    && item.Namespace == "System.Collections.Generic"
                    && item.NamespaceScopeId == null);
        Assert.Contains(
            result.Ast.Usings,
            item => item.Alias == "Text"
                    && item.NamespaceIdentity == "Alpha"
                    && item.NamespaceScopeId != null);

        var generated = CompileConverted(result);
        Assert.True(
            generated.IndexOf(
                "global using System.Collections.Generic;",
                StringComparison.Ordinal)
            < generated.IndexOf("namespace Alpha", StringComparison.Ordinal));
        var betaStart = generated.IndexOf("namespace Beta", StringComparison.Ordinal);
        Assert.DoesNotContain(
            "using Text =",
            generated[betaStart..]);
        AssertNoCompilationErrors(CreateCompilation(generated));
    }

    [Fact]
    public void NestedAndFileScopedNamespaces_PreserveHierarchyAndStyle()
    {
        var nested = ConvertLossy(
            """
            namespace Outer
            {
                namespace Inner
                {
                    public class NestedType { }
                }
            }
            """);
        var outer = Assert.Single(
            nested.Ast!.NamespaceScopes.Where(scope => scope.ParentScopeId == null));
        var inner = Assert.Single(
            nested.Ast.NamespaceScopes.Where(scope => scope.ParentScopeId == outer.Id));
        Assert.Equal("Outer.Inner", inner.FullName);
        Assert.Equal(
            "global::Outer.Inner.NestedType",
            Assert.Single(nested.Ast.Classes).FullyQualifiedSymbolIdentity);
        var nestedGenerated = CompileConverted(nested);
        Assert.Contains("namespace Outer", nestedGenerated);
        Assert.Contains("namespace Inner", nestedGenerated);
        AssertNoCompilationErrors(CreateCompilation(nestedGenerated));

        var fileScoped = new CSharpToCalorConverter().Convert(
            "namespace Files.Scoped;\npublic class FileType { }");
        Assert.True(fileScoped.Success, FormatIssues(fileScoped));
        Assert.True(Assert.Single(fileScoped.Ast!.NamespaceScopes).IsFileScoped);
        var fileGenerated = CompileConverted(fileScoped);
        Assert.Contains("namespace Files.Scoped;", fileGenerated);
        AssertNoCompilationErrors(CreateCompilation(fileGenerated));
    }

    [Fact]
    public void AllTopLevelTypeKinds_CarryFullyQualifiedIdentity()
    {
        var result = ConvertLossy(
            """
            namespace Contracts
            {
                public interface IService { void Execute(); }
                public enum Mode { One }
                public delegate void Handler();
            }
            """);

        Assert.Equal(
            "global::Contracts.IService",
            Assert.Single(result.Ast!.Interfaces).FullyQualifiedSymbolIdentity);
        Assert.Equal(
            "global::Contracts.Mode",
            Assert.Single(result.Ast.Enums).FullyQualifiedSymbolIdentity);
        Assert.Equal(
            "global::Contracts.Handler",
            Assert.Single(result.Ast.Delegates).FullyQualifiedSymbolIdentity);
        AssertNoCompilationErrors(CreateCompilation(CompileConverted(result)));
    }

    [Fact]
    public void FullyQualifiedCrossNamespaceTypeReferences_RoundTrip()
    {
        var result = ConvertLossy(
            """
            namespace Alpha
            {
                public class Factory
                {
                    public Beta.Widget Create() => new Beta.Widget();
                }
            }

            namespace Beta
            {
                public class Widget { }
            }
            """);

        var generated = CompileConverted(result);
        var compilation = CreateCompilation(generated);
        AssertNoCompilationErrors(compilation);
        var returnType = compilation.GetTypeByMetadataName("Alpha.Factory")!
            .GetMembers("Create")
            .OfType<IMethodSymbol>()
            .Single()
            .ReturnType;
        Assert.Equal("Beta.Widget", returnType.ToDisplayString());
    }

    [Fact]
    public void LosslessMultiNamespaceFile_UsesWholeFileInterop()
    {
        const string source =
            """
            namespace Alpha { public class One { } }
            namespace Beta { public class Two { } }
            """;

        var result = new CSharpToCalorConverter().Convert(source);

        Assert.True(result.Success, FormatIssues(result));
        Assert.Empty(result.Ast!.Classes);
        var interop = Assert.Single(result.Ast.InteropBlocks);
        Assert.Equal(source, interop.CSharpCode);
        Assert.Contains(
            result.Losses,
            loss => loss.Feature == "namespace-topology"
                    && loss.Kind == ConversionLossKind.InteropPreserved);
        var generated = CompileConverted(result);
        Assert.DoesNotContain("namespace _global", generated);
        Assert.Contains("namespace Alpha", generated);
        Assert.Contains("namespace Beta", generated);
        var compilation = CreateCompilation(generated);
        AssertNoCompilationErrors(compilation);
        Assert.NotNull(compilation.GetTypeByMetadataName("Alpha.One"));
        Assert.NotNull(compilation.GetTypeByMetadataName("Beta.Two"));
        Assert.Null(compilation.GetTypeByMetadataName("_global.Alpha.One"));
    }

    [Fact]
    public void EmptyNamespace_DoesNotReparentFollowingSibling()
    {
        var result = ConvertLossy(
            """
            namespace Empty { }
            namespace Beta { public class Kept { } }
            """);

        Assert.Equal(2, result.Ast!.NamespaceScopes.Count);
        var generated = CompileConverted(result);
        var compilation = CreateCompilation(generated);
        AssertNoCompilationErrors(compilation);
        Assert.NotNull(compilation.GetTypeByMetadataName("Beta.Kept"));
        Assert.Null(compilation.GetTypeByMetadataName("Empty.Beta.Kept"));
    }

    [Fact]
    public void LossyConversion_EmptyClassBeforeNestedNamespace_RemainsNative()
    {
        var result = ConvertLossy(
            """
            namespace Outer
            {
                public class Marker { }

                namespace Inner
                {
                    public class Real { }
                }
            }
            """);

        Assert.DoesNotContain(
            result.Losses,
            loss => loss.Kind == ConversionLossKind.InteropPreserved);
        var reparsed = ParseCalor(result.CalorSource!);
        var outer = Assert.Single(
            reparsed.NamespaceScopes.Where(scope => scope.ParentScopeId == null));
        var inner = Assert.Single(
            reparsed.NamespaceScopes.Where(scope => scope.ParentScopeId == outer.Id));
        Assert.Equal("Outer.Inner", inner.FullName);
        Assert.Equal(
            "Outer",
            Assert.Single(reparsed.Classes.Where(type => type.Name == "Marker"))
                .NamespaceIdentity);
        Assert.Equal(
            "Outer.Inner",
            Assert.Single(reparsed.Classes.Where(type => type.Name == "Real"))
                .NamespaceIdentity);

        var compilation = CreateCompilation(new CSharpEmitter().Emit(reparsed));
        AssertNoCompilationErrors(compilation);
        Assert.NotNull(compilation.GetTypeByMetadataName("Outer.Marker"));
        Assert.NotNull(compilation.GetTypeByMetadataName("Outer.Inner.Real"));
    }

    [Fact]
    public void LossyConversion_EmptyInterfaceBeforeNestedNamespace_RemainsNative()
    {
        var result = ConvertLossy(
            """
            namespace Outer
            {
                public interface IMarker { }

                namespace Inner
                {
                    public class Real { }
                }
            }
            """);

        Assert.DoesNotContain(
            result.Losses,
            loss => loss.Kind == ConversionLossKind.InteropPreserved);
        var reparsed = ParseCalor(result.CalorSource!);
        var outer = Assert.Single(
            reparsed.NamespaceScopes.Where(scope => scope.ParentScopeId == null));
        var inner = Assert.Single(
            reparsed.NamespaceScopes.Where(scope => scope.ParentScopeId == outer.Id));
        Assert.Equal("Outer.Inner", inner.FullName);
        Assert.Equal(
            "Outer",
            Assert.Single(reparsed.Interfaces).NamespaceIdentity);
        Assert.Equal(
            "Outer.Inner",
            Assert.Single(reparsed.Classes).NamespaceIdentity);

        var compilation = CreateCompilation(new CSharpEmitter().Emit(reparsed));
        AssertNoCompilationErrors(compilation);
        Assert.NotNull(compilation.GetTypeByMetadataName("Outer.IMarker"));
        Assert.NotNull(compilation.GetTypeByMetadataName("Outer.Inner.Real"));
    }

    [Fact]
    public void PostValidationRecovery_UsesFullyQualifiedIdentity()
    {
        const string source =
            """
            namespace Alpha { public class Broken { public int A() => 1; } }
            namespace Beta { public class Broken { public int B() => 2; } }
            """;
        var converter = new CSharpToCalorConverter(new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossy,
            PassthroughOnError = true
        })
        {
            ParseValidatorOverride = calor =>
                !OutsideInterop(calor).Contains("Broken", StringComparison.Ordinal)
        };

        var result = converter.Convert(source);

        Assert.True(result.Success, FormatIssues(result));
        Assert.Empty(result.Ast!.Classes);
        Assert.Equal(2, result.Ast.InteropBlocks.Count);
        Assert.Contains(
            result.Ast.InteropBlocks,
            block => block.FullyQualifiedSymbolIdentity == "global::Alpha.Broken");
        Assert.Contains(
            result.Ast.InteropBlocks,
            block => block.FullyQualifiedSymbolIdentity == "global::Beta.Broken");
        var generated = CompileConverted(result);
        AssertNoCompilationErrors(CreateCompilation(generated));
    }

    [Fact]
    public void PartialMerge_KeysByFullyQualifiedIdentity()
    {
        var alphaOne = ConvertLossy(
            "namespace Alpha { public partial class Shared { public void A() { } } }");
        var alphaTwo = ConvertLossy(
            "namespace Alpha { public partial class Shared { public void B() { } } }");
        var beta = ConvertLossy(
            "namespace Beta { public partial class Shared { public void C() { } } }");

        var merged = new PartialClassMerger().Merge(
            [alphaOne.Ast!, alphaTwo.Ast!, beta.Ast!]);
        var classes = merged.SelectMany(module => module.Classes).ToList();

        var alpha = Assert.Single(
            classes.Where(type =>
                type.FullyQualifiedSymbolIdentity == "global::Alpha.Shared"));
        Assert.Equal(2, alpha.Methods.Count);
        var betaClass = Assert.Single(
            classes.Where(type =>
                type.FullyQualifiedSymbolIdentity == "global::Beta.Shared"));
        Assert.Single(betaClass.Methods);
    }

    [Fact]
    public void PartialMerge_EmptyDonorNamespace_DoesNotCaptureSecondNamespace()
    {
        var target = ConvertLossy(
            "namespace Alpha { public partial class Shared { public void A() { } } }");
        var donor = ConvertLossy(
            """
            namespace Alpha
            {
                public partial class Shared { public void B() { } }
            }
            namespace Beta
            {
                public class Survivor { }
            }
            """);

        var merged = new PartialClassMerger().Merge([target.Ast!, donor.Ast!]);
        var donorModule = merged[1];
        Assert.DoesNotContain(
            donorModule.Classes,
            type => type.FullyQualifiedSymbolIdentity == "global::Alpha.Shared");
        Assert.Contains(
            donorModule.Classes,
            type => type.FullyQualifiedSymbolIdentity == "global::Beta.Survivor");

        var calor = new CalorEmitter(new ConversionContext()).Emit(donorModule);
        var reparsed = ParseCalor(calor);
        Assert.Equal(
            "Beta",
            Assert.Single(reparsed.Classes).NamespaceIdentity);
        var generated = new CSharpEmitter().Emit(reparsed);
        var compilation = CreateCompilation(generated);
        AssertNoCompilationErrors(compilation);
        Assert.NotNull(compilation.GetTypeByMetadataName("Beta.Survivor"));
        Assert.Null(compilation.GetTypeByMetadataName("Alpha.Beta.Survivor"));
    }

    [Fact]
    public void SimplifiableContracts_PreserveOneNamespaceAndDeclarationMetadata()
    {
        var module = ParseCalor(
            """
            §M{Test}
              §NS{Alpha}
                §F{Compute:pub} () -> i32
                  §Q (> INT:2 INT:1)
                  §R INT:42
                §CL{Worker:pub}
                  §MT{Run:pub} () -> i32
                    §Q (> INT:2 INT:1)
                    §R INT:42
                §IFACE{IWorker}
                  §MT{Run}
                    §O{i32}
                    §Q (> INT:2 INT:1)
            """);
        module.NamespaceIdentity = "module-metadata";
        module.NamespaceScopeId = "module-scope";
        module.FullyQualifiedSymbolIdentity = "global::<module>";
        module.DocComment = "module docs";
        var method = Assert.Single(Assert.Single(module.Classes).Methods);
        method.NamespaceIdentity = "Alpha";
        method.NamespaceScopeId = "ns1";
        method.FullyQualifiedSymbolIdentity = "global::Alpha.Worker.Run";
        var signature = Assert.Single(Assert.Single(module.Interfaces).Methods);
        signature.NamespaceIdentity = "Alpha";
        signature.NamespaceScopeId = "ns1";
        signature.FullyQualifiedSymbolIdentity = "global::Alpha.IWorker.Run";

        var simplified =
            new ContractSimplificationPass(new DiagnosticBag()).Simplify(module);

        Assert.NotSame(module, simplified);
        Assert.Same(module.NamespaceScopes, simplified.NamespaceScopes);
        AssertMetadataEqual(module, simplified);
        AssertMetadataEqual(
            Assert.Single(module.Functions),
            Assert.Single(simplified.Functions));
        AssertMetadataEqual(
            Assert.Single(module.Classes),
            Assert.Single(simplified.Classes));
        AssertMetadataEqual(
            Assert.Single(module.Interfaces),
            Assert.Single(simplified.Interfaces));
        AssertMetadataEqual(
            method,
            Assert.Single(Assert.Single(simplified.Classes).Methods));
        AssertMetadataEqual(
            signature,
            Assert.Single(Assert.Single(simplified.Interfaces).Methods));

        var generated = new CSharpEmitter().Emit(simplified);
        var compilation = CreateCompilation(generated);
        AssertNoCompilationErrors(compilation);
        Assert.NotNull(compilation.GetTypeByMetadataName("Alpha.Worker"));
        Assert.NotNull(compilation.GetTypeByMetadataName("Alpha.IWorker"));
    }

    [Fact]
    public void SimplifiableContracts_PreserveMultipleNamespaceScopes()
    {
        var module = ParseCalor(
            """
            §M{Test}
              §NS{Alpha}
                §F{Value:pub} () -> i32
                  §Q (> INT:2 INT:1)
                  §R INT:1
              §NS{Beta}
                §F{Value:pub} () -> i32
                  §Q (> INT:3 INT:2)
                  §R INT:2
            """);

        var simplified =
            new ContractSimplificationPass(new DiagnosticBag()).Simplify(module);

        Assert.Equal(2, simplified.NamespaceScopes.Count);
        Assert.Collection(
            simplified.Functions.OrderBy(function => function.NamespaceIdentity),
            function => Assert.Equal("Alpha", function.NamespaceIdentity),
            function => Assert.Equal("Beta", function.NamespaceIdentity));
        var generated = new CSharpEmitter().Emit(simplified);
        var compilation = CreateCompilation(generated);
        AssertNoCompilationErrors(compilation);
        Assert.NotNull(compilation.GetTypeByMetadataName("Alpha.AlphaModule"));
        Assert.NotNull(compilation.GetTypeByMetadataName("Beta.BetaModule"));
    }

    [Fact]
    public void CalorAndFormatterReemission_PreserveNullScopeDeclarationsByIdentity()
    {
        var module = ParseCalor(
            """
            §M{Test}
              §NS{Alpha}
                §CL{One:pub}
              §NS{Beta}
                §CL{Two:pub}
            """);
        foreach (var type in module.Classes)
            type.NamespaceScopeId = null;

        var context = new ConversionContext();
        var emitted = new CalorEmitter(context).Emit(module);
        Assert.DoesNotContain(
            context.Issues,
            issue => issue.Severity == ConversionIssueSeverity.Error);
        var reparsed = ParseCalor(emitted);
        Assert.Contains(
            reparsed.Classes,
            type => type.Name == "One" && type.NamespaceIdentity == "Alpha");
        Assert.Contains(
            reparsed.Classes,
            type => type.Name == "Two" && type.NamespaceIdentity == "Beta");

        var formatted = new CalorFormatter().FormatSource(emitted);
        Assert.True(
            formatted.Success,
            string.Join(
                Environment.NewLine,
                formatted.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var formattedModule = ParseCalor(formatted.Formatted);
        Assert.Equal(
            new string?[] { "Alpha", "Beta" },
            formattedModule.Classes
                .Select(type => type.NamespaceIdentity)
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public void CalorEmitter_MalformedNullScope_ReportsAndPreservesDeclaration()
    {
        var module = ParseCalor(
            """
            §M{Test}
              §NS{Alpha}
                §CL{Malformed:pub}
            """);
        var type = Assert.Single(module.Classes);
        type.NamespaceScopeId = null;
        type.NamespaceIdentity = null;

        var context = new ConversionContext();
        var emitted = new CalorEmitter(context).Emit(module);

        Assert.Contains(
            context.Issues,
            issue => issue.Severity == ConversionIssueSeverity.Error
                     && issue.Message.Contains(
                         "neither a namespace scope",
                         StringComparison.Ordinal));
        var reparsed = ParseCalor(emitted);
        var preserved = Assert.Single(reparsed.Classes);
        Assert.Equal("", preserved.NamespaceIdentity);
        Assert.Contains(reparsed.NamespaceScopes, scope => scope.IsGlobal);
    }

    [Fact]
    public void ExplicitTopology_UnscopedDeclarationWithoutIdentity_ReportsDiagnostic()
    {
        var result = ConvertLossy(
            "namespace Alpha { public class MissingMetadata { } }");
        var type = Assert.Single(result.Ast!.Classes);
        type.NamespaceScopeId = null;

        var fallbackGenerated = new CSharpEmitter().Emit(result.Ast);
        Assert.NotNull(CreateCompilation(fallbackGenerated)
            .GetTypeByMetadataName("Alpha.MissingMetadata"));

        type.NamespaceIdentity = null;
        var emitter = new CSharpEmitter();
        var malformedGenerated = emitter.Emit(result.Ast);
        Assert.Contains(
            emitter.EmissionDiagnostics.Errors,
            diagnostic =>
                diagnostic.Code == DiagnosticCode.MalformedNamespaceTopology);
        Assert.NotNull(
            CreateCompilation(malformedGenerated)
                .GetTypeByMetadataName("MissingMetadata"));
    }

    [Fact]
    public void ProgramCompile_MixedNamespaceAndDirectModuleDeclaration_IsValid()
    {
        const string source =
            """
            §M{Mixed}
              §NS{Alpha}
                §CL{Namespaced:pub}
              §CL{DirectGlobal:pub}
            """;

        var parsed = ParseCalor(source);
        var direct = Assert.Single(
            parsed.Classes.Where(type => type.Name == "DirectGlobal"));
        Assert.Equal("", direct.NamespaceIdentity);
        Assert.Equal("", direct.NamespaceScopeId);

        var result = Program.Compile(
            source,
            null,
            new CompilationOptions
            {
                EnforceEffects = false,
                UnknownCallPolicy =
                    Calor.Compiler.Effects.UnknownCallPolicy.Permissive
            });

        Assert.False(
            result.HasErrors,
            string.Join(
                Environment.NewLine,
                result.Diagnostics.Errors.Select(error => error.Message)));
        var compilation = CreateCompilation(result.GeneratedCode);
        AssertNoCompilationErrors(compilation);
        Assert.NotNull(compilation.GetTypeByMetadataName("Alpha.Namespaced"));
        Assert.NotNull(compilation.GetTypeByMetadataName("DirectGlobal"));
    }

    [Fact]
    public void CrossModuleCalls_TargetEmittedNamespaceAndGlobalModuleClass()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"calor-issue769-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var namespacedPath = Path.Combine(directory, "namespaced.calr");
            var globalPath = Path.Combine(directory, "global.calr");
            var callerPath = Path.Combine(directory, "caller.calr");
            File.WriteAllText(
                namespacedPath,
                """
                §M{StorageFile}
                  §NS{Company.Storage}
                    §F{Save:pub} () -> i32
                      §R INT:41
                """);
            File.WriteAllText(
                globalPath,
                """
                §M{GlobalFile}
                  §NS{_global:global}
                    §F{Ping:pub} () -> i32
                      §R INT:1
                """);
            File.WriteAllText(
                callerPath,
                """
                §M{Consumer}
                  §F{ReadNamespaced:pub} () -> i32
                    §R §C{Save}
                  §F{ReadGlobal:pub} () -> i32
                    §R §C{Ping}
                  §F{ReadExplicitNamespace:pub} () -> i32
                    §R §C{Company.Storage.Save}
                """);

            var files = new[]
            {
                new FileInfo(namespacedPath),
                new FileInfo(globalPath),
                new FileInfo(callerPath)
            };
            var map = CompilationDriver.BuildCrossModuleFunctionMap(files);
            Assert.Equal("Company.Storage", map["Save"].NamespaceIdentity);
            Assert.Equal("StorageModule", map["Save"].ModuleClassName);
            Assert.Equal(
                map["Save"],
                map["Company.Storage.Save"]);
            Assert.Equal("", map["Ping"].NamespaceIdentity);
            Assert.Equal("GlobalModule", map["Ping"].ModuleClassName);

            var generated = new Dictionary<string, string>(
                StringComparer.Ordinal);
            var diagnostics = new DiagnosticBag();
            var driverResult = CompilationDriver.CompileAll(
                files,
                _ => new CompilationOptions
                {
                    EnforceEffects = false,
                    UnknownCallPolicy =
                        Calor.Compiler.Effects.UnknownCallPolicy.Permissive
                },
                crossModuleEnforcement: false,
                crossModulePolicy:
                    Calor.Compiler.Effects.UnknownCallPolicy.Permissive,
                onCompiled: (file, result) =>
                    generated[file.Name] = result.GeneratedCode,
                diagnosticSink: diagnostics);

            Assert.False(
                driverResult.AnyErrors,
                string.Join(
                    Environment.NewLine,
                    diagnostics.Errors.Select(error => error.Message)));
            Assert.Contains(
                "global::Company.Storage.StorageModule.Save()",
                generated["caller.calr"]);
            Assert.Contains(
                "global::GlobalModule.Ping()",
                generated["caller.calr"]);

            var compilation = CreateCompilation(generated.Values.ToArray());
            AssertNoCompilationErrors(compilation);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PrivateSiblingModuleFunction_IsQualifiedAndRuns()
    {
        const string source =
            """
            §M{LocalModule}
              §NS{Alpha}
                §F{Pick:priv} (i32:value) -> i32
                  §R INT:7
                §F{Pick:priv} (str:value) -> i32
                  §R INT:8
              §NS{Beta}
                §F{Run:pub} () -> i32
                  §R §C{Pick} §A INT:1 §/C
            """;

        var result = Program.Compile(
            source,
            "private-sibling.calr",
            new CompilationOptions
            {
                EnforceEffects = false,
                UnknownCallPolicy =
                    Calor.Compiler.Effects.UnknownCallPolicy.Permissive
            });

        Assert.False(
            result.HasErrors,
            string.Join(
                Environment.NewLine,
                result.Diagnostics.Errors.Select(error => error.Message)));
        Assert.Contains(
            "global::Alpha.AlphaModule.Pick(1)",
            result.GeneratedCode);
        Assert.Equal(
            2,
            result.GeneratedCode.Split("internal static int Pick(").Length - 1);
        using var loaded = CompileAndLoad(result.GeneratedCode);
        Assert.Equal(
            7,
            InvokeStatic<int>(
                loaded.Assembly,
                "Beta.BetaModule",
                "Run"));
    }

    [Fact]
    public void PrivateSameScopeModuleFunction_RemainsBareAndRuns()
    {
        const string source =
            """
            §M{LocalModule}
              §NS{Alpha}
                §F{Pick:priv} () -> i32
                  §R INT:7
                §F{Run:pub} () -> i32
                  §R §C{Pick}
            """;

        var result = Program.Compile(
            source,
            "private-same-scope.calr",
            new CompilationOptions
            {
                EnforceEffects = false,
                UnknownCallPolicy =
                    Calor.Compiler.Effects.UnknownCallPolicy.Permissive
            });

        Assert.False(
            result.HasErrors,
            string.Join(
                Environment.NewLine,
                result.Diagnostics.Errors.Select(error => error.Message)));
        Assert.Contains("private static int Pick()", result.GeneratedCode);
        Assert.Contains("return Pick();", result.GeneratedCode);
        Assert.DoesNotContain(
            "global::Alpha.AlphaModule.Pick()",
            result.GeneratedCode);
        using var loaded = CompileAndLoad(result.GeneratedCode);
        Assert.Equal(
            7,
            InvokeStatic<int>(
                loaded.Assembly,
                "Alpha.AlphaModule",
                "Run"));
    }

    [Fact]
    public void CrossFilePublicSameName_CannotRebindPrivateSiblingCall()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"calor-private-rebind-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var localPath = Path.Combine(directory, "local.calr");
            var externalPath = Path.Combine(directory, "external.calr");
            File.WriteAllText(
                localPath,
                """
                §M{LocalModule}
                  §NS{Alpha}
                    §F{Pick:priv} (i32:value) -> i32
                      §R INT:7
                  §NS{Beta}
                    §F{Run:pub} () -> i32
                      §R §C{Pick} §A INT:1 §/C
                """);
            File.WriteAllText(
                externalPath,
                """
                §M{ExternalModule}
                  §NS{Gamma}
                    §F{Pick:pub} (i32:value) -> i32
                      §R INT:99
                """);

            var files = new[]
            {
                new FileInfo(localPath),
                new FileInfo(externalPath)
            };
            var crossFileMap =
                CompilationDriver.BuildCrossModuleFunctionMap(files);
            Assert.Equal("Gamma", crossFileMap["Pick"].NamespaceIdentity);

            var generated = new Dictionary<string, string>(
                StringComparer.Ordinal);
            var diagnostics = new DiagnosticBag();
            var driverResult = CompilationDriver.CompileAll(
                files,
                _ => new CompilationOptions
                {
                    EnforceEffects = false,
                    UnknownCallPolicy =
                        Calor.Compiler.Effects.UnknownCallPolicy.Permissive
                },
                crossModuleEnforcement: false,
                crossModulePolicy:
                    Calor.Compiler.Effects.UnknownCallPolicy.Permissive,
                onCompiled: (file, result) =>
                    generated[file.Name] = result.GeneratedCode,
                diagnosticSink: diagnostics);

            Assert.False(
                driverResult.AnyErrors,
                string.Join(
                    Environment.NewLine,
                    diagnostics.Errors.Select(error => error.Message)));
            Assert.Contains(
                "global::Alpha.AlphaModule.Pick(1)",
                generated["local.calr"]);
            Assert.DoesNotContain(
                "global::Gamma.GammaModule.Pick(1)",
                generated["local.calr"]);

            using var loaded = CompileAndLoad(generated.Values.ToArray());
            Assert.Equal(
                7,
                InvokeStatic<int>(
                    loaded.Assembly,
                    "Beta.BetaModule",
                    "Run"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MismatchedPrivateSiblingSignature_DoesNotFallBackToExternalFunction()
    {
        const string localSource =
            """
            §M{LocalModule}
              §NS{Alpha}
                §F{Pick:priv} (str:value) -> i32
                  §R INT:7
              §NS{Beta}
                §F{Run:pub} () -> i32
                  §R §C{Pick} §A INT:1 §/C
            """;
        const string externalSource =
            """
            §M{ExternalModule}
              §NS{Gamma}
                §F{Pick:pub} (i32:value) -> i32
                  §R INT:99
            """;

        var localModule = ParseCalor(localSource);
        var externalModule = ParseCalor(externalSource);
        var bindingDiagnostics = new DiagnosticBag();
        _ = new CalorBinder(bindingDiagnostics).Bind(localModule);
        Assert.Contains(
            bindingDiagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.NoMatchingOverload);

        var externalMap = CompilationDriver.BuildCrossModuleFunctionMap(
            [localModule, externalModule]);
        Assert.Equal("Gamma", externalMap["Pick"].NamespaceIdentity);
        var emitter = new CSharpEmitter
        {
            CrossModuleFunctionModules = externalMap
        };
        var generated = emitter.Emit(localModule);

        Assert.Contains(
            "global::Alpha.AlphaModule.Pick(1)",
            generated);
        Assert.DoesNotContain(
            "global::Gamma.GammaModule.Pick(1)",
            generated);
        Assert.Contains(
            CreateCompilation(generated).GetDiagnostics(),
            diagnostic =>
                diagnostic.Severity
                    == Microsoft.CodeAnalysis.DiagnosticSeverity.Error
                && diagnostic.Id == "CS1503");
    }

    [Fact]
    public void IntraModuleFunctionMap_IncludesPrivateOverloadsAndKeepsAmbiguity()
    {
        const string source =
            """
            §M{LocalModule}
              §NS{Alpha}
                §F{Pick:priv} (i32:value) -> i32
                  §R value
                §F{Pick:priv} (str:value) -> i32
                  §R INT:1
                §F{Clash:priv} () -> i32
                  §R INT:2
              §NS{Beta}
                §F{Clash:priv} () -> i32
                  §R INT:3
            """;

        var map = CompilationDriver.BuildIntraModuleFunctionMap(
            ParseCalor(source));

        Assert.Equal("Alpha", map["Pick"].NamespaceIdentity);
        Assert.Equal(map["Pick"], map["Alpha.Pick"]);
        Assert.False(map.ContainsKey("Clash"));
        Assert.Equal("Alpha", map["Alpha.Clash"].NamespaceIdentity);
        Assert.Equal("Beta", map["Beta.Clash"].NamespaceIdentity);
    }

    [Fact]
    public void AmbiguousLocalName_IsNeverReboundToExternalFunction()
    {
        const string localSource =
            """
            §M{LocalModule}
              §NS{Alpha}
                §F{Pick:priv} () -> i32
                  §R INT:7
              §NS{Beta}
                §F{Pick:priv} () -> i32
                  §R INT:8
              §NS{Consumer}
                §F{Run:pub} () -> i32
                  §R §C{Pick}
            """;
        const string externalSource =
            """
            §M{ExternalModule}
              §NS{Gamma}
                §F{Pick:pub} () -> i32
                  §R INT:99
            """;

        var localModule = ParseCalor(localSource);
        var externalMap = CompilationDriver.BuildCrossModuleFunctionMap(
            [localModule, ParseCalor(externalSource)]);
        Assert.Equal("Gamma", externalMap["Pick"].NamespaceIdentity);

        var generated = new CSharpEmitter
        {
            CrossModuleFunctionModules = externalMap
        }.Emit(localModule);

        Assert.Contains("return Pick();", generated);
        Assert.DoesNotContain("global::Gamma.GammaModule.Pick()", generated);
        Assert.Contains(
            CreateCompilation(generated).GetDiagnostics(),
            diagnostic =>
                diagnostic.Severity
                    == Microsoft.CodeAnalysis.DiagnosticSeverity.Error
                && diagnostic.Id == "CS0103");
    }

    [Fact]
    public void NamespacedTypeQualifiedCallsAndConstructors_RetainBoundSymbols()
    {
        const string source =
            """
            §M{Binding}
              §NS{Alpha}
                §CL{Widget:pub}
                  §CTOR{pub} (i32:value)
                    §P STR:"alpha"
                  §MT{Create:pub:stat} (i32:value) -> i32
                    §R value
                §CL{Receiver:pub}
                  §MT{Read:pub} (i32:value) -> i32
                    §R value
                §F{UseStatic:pub} () -> i32
                  §R §C{Widget.Create} §A INT:41 §/C
                §F{UseReceiver:pub} (Receiver:receiver) -> i32
                  §R §C{receiver.Read} §A INT:42 §/C
                §F{Make:pub} () -> Widget
                  §R §NEW{Widget} §A INT:43 §/NEW
                §F{WrongStatic:pub} () -> i32
                  §R §C{Widget.Create} §A BOOL:true §/C
                §F{WrongConstructor:pub} () -> Widget
                  §R §NEW{Widget} §A BOOL:true §/NEW
              §NS{Beta}
                §CL{Widget:pub}
                  §CTOR{pub} (str:value)
                    §P STR:"beta"
                  §MT{Create:pub:stat} (str:value) -> str
                    §R value
                §F{UseFullyQualified:pub} () -> i32
                  §R §C{Alpha.Widget.Create} §A INT:44 §/C
            """;

        var module = ParseCalor(source);
        var diagnostics = new DiagnosticBag();
        var bound = new CalorBinder(diagnostics).Bind(module);
        var alphaCreate = bound.Functions.Single(function =>
            function.Symbol.Name == "Alpha.Widget.Create");
        var alphaConstructor = bound.Functions.Single(function =>
            function.Symbol.Name == "Alpha.Widget..ctor");

        var staticCall = GetReturnExpression<BoundCallExpression>(
            bound,
            "UseStatic");
        var receiverCall = GetReturnExpression<BoundCallExpression>(
            bound,
            "UseReceiver");
        var creation = GetReturnExpression<BoundNewExpression>(
            bound,
            "Make");
        var fullyQualifiedCall = GetReturnExpression<BoundCallExpression>(
            bound,
            "UseFullyQualified");
        var graph = CallGraphAnalysis.BuildResolved(bound);
        var useStatic = bound.Functions.Single(function =>
            function.Symbol.Name == "UseStatic");

        Assert.Same(alphaCreate.Symbol, staticCall.ResolvedSymbol);
        Assert.Equal(
            [alphaCreate.Symbol],
            staticCall.ResolvedSymbols);
        Assert.Equal("Alpha.Widget", staticCall.ResolvedTypeName);
        Assert.NotNull(staticCall.ReceiverTypeSymbolId);
        Assert.NotNull(receiverCall.ReceiverSymbolId);
        Assert.Same(alphaConstructor.Symbol, creation.ResolvedConstructor);
        Assert.Equal(
            [alphaConstructor.Symbol],
            creation.ResolvedConstructors);
        Assert.Equal("Alpha.Widget", creation.ResolvedType!.QualifiedName);
        Assert.Same(alphaCreate.Symbol, fullyQualifiedCall.ResolvedSymbol);
        Assert.Equal(
            alphaCreate.SymbolId,
            Assert.Single(graph.ForwardGraph[useStatic.SymbolId]).Callee);
        Assert.Equal(
            2,
            diagnostics.Count(diagnostic =>
                diagnostic.Code == DiagnosticCode.NoMatchingOverload));
        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.AmbiguousOverload);
    }

    [Fact]
    public void GenericClassInstanceStaticAndConstruction_ResolveExactArity()
    {
        const string source =
            """
            §M{GenericBinding}
              §NS{Alpha}
                §CL{Box:pub}<T>
                  §CTOR{pub} (i32:value)
                    §B{copy:i32} value
                  §MT{Instance:pub} (i32:value) -> i32
                    §R value
                  §MT{Static:pub:stat} (i32:value) -> i32
                    §R value
              §NS{Beta}
                §F{UseInstance:pub} (Alpha.Box<i32>:box) -> i32
                  §R §C{box.Instance} §A INT:41 §/C
                §F{UseStatic:pub} () -> i32
                  §R §C{Alpha.Box<i32>.Static} §A INT:42 §/C
                §F{Make:pub} () -> Alpha.Box<i32>
                  §R §NEW{Alpha.Box<i32>} §A INT:43 §/NEW
                §F{WrongInstance:pub} (Alpha.Box<i32>:box) -> i32
                  §R §C{box.Instance} §A BOOL:true §/C
                §F{WrongStatic:pub} () -> i32
                  §R §C{Alpha.Box<i32>.Static} §A BOOL:true §/C
                §F{WrongConstructor:pub} () -> Alpha.Box<i32>
                  §R §NEW{Alpha.Box<i32>} §A BOOL:true §/NEW
            """;

        var module = ParseCalor(source);
        var diagnostics = new DiagnosticBag();
        var bound = new CalorBinder(diagnostics).Bind(module);
        var boxType = bound.SymbolsById.Values
            .OfType<TypeSymbol>()
            .Single(symbol => symbol.QualifiedName == "Alpha.Box`1");
        var instanceMethod = bound.Functions.Single(function =>
            function.Symbol.Name == "Alpha.Box`1.Instance");
        var staticMethod = bound.Functions.Single(function =>
            function.Symbol.Name == "Alpha.Box`1.Static");
        var constructor = bound.Functions.Single(function =>
            function.Symbol.Name == "Alpha.Box`1..ctor");
        var instanceCall = GetReturnExpression<BoundCallExpression>(
            bound,
            "UseInstance");
        var staticCall = GetReturnExpression<BoundCallExpression>(
            bound,
            "UseStatic");
        var creation = GetReturnExpression<BoundNewExpression>(
            bound,
            "Make");
        var graph = CallGraphAnalysis.BuildResolved(bound);

        Assert.Same(instanceMethod.Symbol, instanceCall.ResolvedSymbol);
        Assert.Equal("Alpha.Box`1", instanceCall.ResolvedTypeName);
        Assert.Same(staticMethod.Symbol, staticCall.ResolvedSymbol);
        Assert.Equal(boxType.Id, staticCall.ReceiverTypeSymbolId);
        Assert.Equal("Alpha.Box`1", staticCall.ResolvedTypeName);
        Assert.Same(constructor.Symbol, creation.ResolvedConstructor);
        Assert.Equal(boxType.Id, creation.ResolvedTypeSymbolId);
        Assert.Equal(boxType.Id, creation.TypeReference.ResolvedTypeSymbolId);
        Assert.Equal(
            instanceMethod.SymbolId,
            Assert.Single(graph.ForwardGraph[
                bound.Functions.Single(function =>
                    function.Symbol.Name == "UseInstance").SymbolId]).Callee);
        Assert.Equal(
            3,
            diagnostics.Count(diagnostic =>
                diagnostic.Code == DiagnosticCode.NoMatchingOverload));
        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.AmbiguousOverload);
    }

    [Fact]
    public void GenericClassesWithSameBaseName_KeepAritySeparated()
    {
        const string source =
            """
            §M{GenericArity}
              §CL{Box:pub}<T>
                §CTOR{pub} (i32:value)
                  §B{copy1:i32} value
                §MT{Pick:pub} () -> i32
                  §R INT:1
              §CL{Box:pub}<TFirst,TSecond>
                §CTOR{pub} (str:value)
                  §B{copy2:?str} value
                §MT{Pick:pub} () -> i32
                  §R INT:2
              §F{UseOne:pub} (Box<i32>:box) -> i32
                §R §C{box.Pick}
              §F{UseTwo:pub} (Box<i32,str>:box) -> i32
                §R §C{box.Pick}
              §F{MakeOne:pub} () -> Box<i32>
                §R §NEW{Box<i32>} §A INT:1 §/NEW
              §F{MakeTwo:pub} () -> Box<i32,str>
                §R §NEW{Box<i32,str>} §A STR:"two" §/NEW
            """;

        var module = ParseCalor(source);
        var diagnostics = new DiagnosticBag();
        var bound = new CalorBinder(diagnostics).Bind(module);
        var types = bound.SymbolsById.Values
            .OfType<TypeSymbol>()
            .Where(symbol => symbol.Name == "Box")
            .OrderBy(symbol => symbol.QualifiedName, StringComparer.Ordinal)
            .ToArray();
        var oneCall = GetReturnExpression<BoundCallExpression>(
            bound,
            "UseOne");
        var twoCall = GetReturnExpression<BoundCallExpression>(
            bound,
            "UseTwo");
        var oneCreation = GetReturnExpression<BoundNewExpression>(
            bound,
            "MakeOne");
        var twoCreation = GetReturnExpression<BoundNewExpression>(
            bound,
            "MakeTwo");

        Assert.Equal(["Box`1", "Box`2"], types.Select(type => type.QualifiedName));
        Assert.Equal("Box`1.Pick", oneCall.ResolvedSymbol!.Name);
        Assert.Equal("Box`2.Pick", twoCall.ResolvedSymbol!.Name);
        Assert.Equal("Box`1..ctor", oneCreation.ResolvedConstructor!.Name);
        Assert.Equal("Box`2..ctor", twoCreation.ResolvedConstructor!.Name);
        Assert.Equal(types[0].Id, oneCreation.ResolvedTypeSymbolId);
        Assert.Equal(types[1].Id, twoCreation.ResolvedTypeSymbolId);
        Assert.False(diagnostics.HasErrors);
    }

    [Fact]
    public void GenericReceiverResolution_PreservesEffectsAndTaintIdentity()
    {
        const string effectSource =
            """
            §M{GenericEffects}
              §CL{Box:pub}<T>
                §MT{Write:pub} (str:value) -> void
                  §E{cw}
                  §P value
              §F{Use:pub} (Box<i32>:box, str:value) -> void
                §E{}
                §C{box.Write} §A value §/C
            """;
        var effectResult = Program.Compile(effectSource);

        Assert.Contains(
            effectResult.Diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
        Assert.DoesNotContain(
            effectResult.Diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);

        const string taintSource =
            """
            §M{GenericTaint}
              §NS{Alpha}
                §CL{Box:pub}<T>
                  §MT{Danger:pub} (str:value) -> void
                    §B{copy:?str} value
              §NS{Beta}
                §F{Use:pub} (Alpha.Box<i32>:box, str:user_input) -> void
                  §C{box.Danger} §A user_input §/C
            """;
        var module = ParseCalor(taintSource);
        var diagnostics = new DiagnosticBag();
        var bound = new CalorBinder(diagnostics).Bind(module);
        var use = bound.Functions.Single(function =>
            function.Symbol.Name == "Use");
        var call = Assert.IsType<BoundCallStatement>(Assert.Single(use.Body));
        var analysis = new TaintAnalysis(
            use,
            new TaintAnalysisOptions
            {
                AdditionalSinks =
                [
                    new TaintSinkRule(
                        new TaintCallIdentity(
                            TypeName: "Alpha.Box`1",
                            MethodName: "Danger"),
                        TaintSink.SqlQuery,
                        0),
                ],
            });

        Assert.False(diagnostics.HasErrors);
        Assert.Equal("Alpha.Box`1", call.ResolvedTypeName);
        Assert.NotNull(call.ResolvedSymbol);
        Assert.Equal(TaintSink.SqlQuery, Assert.Single(analysis.Vulnerabilities).Sink);
    }

    [Fact]
    public void IntraModuleCrossNamespaceCalls_BindEmitCompileAndRun()
    {
        const string source =
            """
            §M{Topology}
              §NS{Alpha}
                §F{Foo:pub} (i32:value) -> i32
                  §R (+ value INT:1)
                §F{Over:pub} (i32:value) -> i32
                  §R (+ value INT:2)
                §F{Over:pub} (str:value) -> i32
                  §R INT:7
              §NS{Beta}
                §F{BetaLocal:pub} () -> i32
                  §R INT:5
                §F{FromBare:pub} () -> i32
                  §R §C{Foo} §A INT:40 §/C
                §F{FromQualified:pub} () -> i32
                  §R §C{Alpha.Foo} §A INT:41 §/C
                §F{FromOverload:pub} () -> i32
                  §R §C{Alpha.Over} §A INT:40 §/C
                §F{FromSameNamespace:pub} () -> i32
                  §R §C{BetaLocal}
                §F{FromGlobal:pub} () -> i32
                  §R §C{GlobalValue}
              §NS{_global:global}
                §F{GlobalValue:pub} () -> i32
                  §R INT:3
                §F{FromAlpha:pub} () -> i32
                  §R §C{Alpha.Foo} §A INT:9 §/C
            """;

        var module = ParseCalor(source);
        var diagnostics = new DiagnosticBag();
        var bound = new CalorBinder(diagnostics).Bind(module);
        Assert.False(
            diagnostics.HasErrors,
            string.Join(
                Environment.NewLine,
                diagnostics.Errors.Select(error => error.Message)));

        var alphaFoo = bound.Functions.Single(function =>
            function.Symbol.Name == "Foo");
        var bareCall = GetReturnExpression<BoundCallExpression>(
            bound,
            "FromBare");
        var qualifiedCall = GetReturnExpression<BoundCallExpression>(
            bound,
            "FromQualified");
        var overloadCall = GetReturnExpression<BoundCallExpression>(
            bound,
            "FromOverload");
        Assert.Same(alphaFoo.Symbol, bareCall.ResolvedSymbol);
        Assert.Same(alphaFoo.Symbol, qualifiedCall.ResolvedSymbol);
        Assert.Equal(
            "i32",
            Assert.Single(overloadCall.ResolvedSymbol!.Parameters).TypeName);

        var generated = new CSharpEmitter().Emit(module);
        Assert.Contains(
            "global::Alpha.AlphaModule.Foo(40)",
            generated);
        Assert.Contains(
            "global::Alpha.AlphaModule.Foo(41)",
            generated);
        Assert.Contains(
            "global::Alpha.AlphaModule.Over(40)",
            generated);
        Assert.Contains(
            "global::GlobalModule.GlobalValue()",
            generated);
        Assert.Contains(
            "return BetaLocal();",
            generated);
        Assert.DoesNotContain(
            "global::Beta.BetaModule.BetaLocal()",
            generated);

        using var loaded = CompileAndLoad(generated);
        Assert.Equal(
            41,
            InvokeStatic<int>(loaded.Assembly, "Beta.BetaModule", "FromBare"));
        Assert.Equal(
            42,
            InvokeStatic<int>(
                loaded.Assembly,
                "Beta.BetaModule",
                "FromQualified"));
        Assert.Equal(
            42,
            InvokeStatic<int>(
                loaded.Assembly,
                "Beta.BetaModule",
                "FromOverload"));
        Assert.Equal(
            5,
            InvokeStatic<int>(
                loaded.Assembly,
                "Beta.BetaModule",
                "FromSameNamespace"));
        Assert.Equal(
            3,
            InvokeStatic<int>(
                loaded.Assembly,
                "Beta.BetaModule",
                "FromGlobal"));
        Assert.Equal(
            10,
            InvokeStatic<int>(loaded.Assembly, "GlobalModule", "FromAlpha"));
    }

    [Fact]
    public void IntraModuleAmbiguousBareCall_IsNotSilentlyQualified()
    {
        const string source =
            """
            §M{Topology}
              §NS{Alpha}
                §F{Shared:pub} () -> i32
                  §R INT:1
              §NS{Beta}
                §F{Shared:pub} () -> i32
                  §R INT:2
              §NS{Gamma}
                §F{FromBare:pub} () -> i32
                  §R §C{Shared}
                §F{FromQualified:pub} () -> i32
                  §R §C{Alpha.Shared}
            """;

        var module = ParseCalor(source);
        var diagnostics = new DiagnosticBag();
        var bound = new CalorBinder(diagnostics).Bind(module);
        var bareCall = GetReturnExpression<BoundCallExpression>(
            bound,
            "FromBare");
        var qualifiedCall = GetReturnExpression<BoundCallExpression>(
            bound,
            "FromQualified");

        Assert.Null(bareCall.ResolvedSymbol);
        Assert.Empty(bareCall.ResolvedSymbols);
        Assert.NotNull(qualifiedCall.ResolvedSymbol);

        var generated = new CSharpEmitter().Emit(module);
        Assert.Contains("return Shared();", generated);
        Assert.Contains(
            "return global::Alpha.AlphaModule.Shared();",
            generated);
        Assert.DoesNotContain(
            "return global::Beta.BetaModule.Shared();",
            generated);
    }

    [Fact]
    public void RealGlobalNamedNamespace_RoundTripsWithoutSentinelCollision()
    {
        var result = ConvertLossy(
            "namespace _global { public class Thing { } }");
        var scope = Assert.Single(result.Ast!.NamespaceScopes);
        Assert.False(scope.IsGlobal);
        Assert.Equal(NamespaceScopeKind.Named, scope.Kind);
        var type = Assert.Single(result.Ast.Classes);
        Assert.Equal("_global", type.NamespaceIdentity);
        Assert.Equal("global::_global.Thing", type.FullyQualifiedSymbolIdentity);
        Assert.Contains("§NS{_global:named}", result.CalorSource);

        var generated = CompileConverted(result);
        var compilation = CreateCompilation(generated);
        AssertNoCompilationErrors(compilation);
        Assert.NotNull(compilation.GetTypeByMetadataName("_global.Thing"));
        Assert.Null(compilation.GetTypeByMetadataName("Thing"));
    }

    [Fact]
    public void LegacyGlobalNamespaceSentinel_ParsesAndReemitsExplicitFlag()
    {
        var module = ParseCalor(
            """
            §M{Legacy}
              §NS{_global}
                §CL{Thing:pub}
            """);

        Assert.True(Assert.Single(module.NamespaceScopes).IsGlobal);
        var emitted = new CalorEmitter().Emit(module);
        Assert.Contains("§NS{_global:global}", emitted);
        var reparsed = ParseCalor(emitted);
        Assert.True(Assert.Single(reparsed.NamespaceScopes).IsGlobal);
    }

    [Fact]
    public async Task NamespaceTopology_PlanningAndAccounting_AreNotFullyNative()
    {
        Assert.Equal(
            SupportLevel.Partial,
            FeatureSupport.GetSupportLevel("namespace"));
        Assert.Equal(
            SupportLevel.Full,
            FeatureSupport.GetSupportLevel("namespace-single-scope"));
        Assert.Equal(
            SupportLevel.Partial,
            FeatureSupport.GetSupportLevel("namespace-topology"));

        var fixtureDirectory = Path.Combine(
            RepositoryRoot(),
            "bench",
            "phase0-agent-native",
            "fixtures",
            "d-s1.5",
            "namespace-topology");
        var plan = await new ProjectDiscovery().DiscoverCSharpFilesAsync(
            fixtureDirectory,
            MigrationDirection.CSharpToCalor);
        var entry = Assert.Single(plan.Entries);
        Assert.Equal(FileConvertibility.Partial, entry.Convertibility);
        Assert.Contains("namespace-topology", entry.DetectedFeatures);
        Assert.Equal(0, plan.ConvertibleFiles);
        Assert.Equal(1, plan.PartialFiles);

        var result = new CSharpToCalorConverter().Convert(
            File.ReadAllText(Path.Combine(fixtureDirectory, "input.cs")));
        Assert.Contains(
            result.Losses,
            loss => loss.Feature == "namespace-topology"
                    && loss.Kind == ConversionLossKind.InteropPreserved);
        Assert.Contains(
            "namespace-topology",
            result.Context.GetExplanation().PartialFeatures);
        Assert.Equal(
            FileMigrationStatus.Partial,
            ProjectMigrator.GetMigrationStatus(result));
    }

    [Fact]
    public void ProjectOutputCollision_IsDeterministicallyDisambiguated()
    {
        var entries = new[]
        {
            Entry(
                "/repo/Alpha/Widget.cs",
                "/repo/output/Widget.calr",
                "global::Alpha.Widget"),
            Entry(
                "/repo/Beta/Widget.cs",
                "/repo/output/Widget.calr",
                "global::Beta.Widget")
        };

        ProjectDiscovery.ResolveOutputPathCollisions(entries);

        Assert.NotEqual(entries[0].OutputPath, entries[1].OutputPath);
        Assert.Contains("Beta_Widget", entries[1].OutputPath);
    }

    private static ConversionResult ConvertLossy(string source)
    {
        var result = new CSharpToCalorConverter(new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossy,
            PassthroughOnError = true
        }).Convert(source);
        Assert.True(result.Success, FormatIssues(result));
        return result;
    }

    private static ModuleNode ParseCalor(string source)
    {
        var diagnostics = new DiagnosticBag();
        var parser = new Parser(
            new Lexer(source, diagnostics).TokenizeAllForParser(),
            diagnostics);
        var module = parser.Parse();
        Assert.False(
            diagnostics.HasErrors,
            string.Join(
                Environment.NewLine,
                diagnostics.Errors.Select(error => error.Message)));
        return module;
    }

    private static void AssertMetadataEqual(AstNode expected, AstNode actual)
    {
        Assert.Equal(expected.NamespaceIdentity, actual.NamespaceIdentity);
        Assert.Equal(expected.NamespaceScopeId, actual.NamespaceScopeId);
        Assert.Equal(
            expected.FullyQualifiedSymbolIdentity,
            actual.FullyQualifiedSymbolIdentity);
        Assert.Equal(expected.DocComment, actual.DocComment);
    }

    private static string RepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(directory, "Directory.Build.props")))
            directory = Directory.GetParent(directory)!.FullName;
        return directory;
    }

    private static string CompileConverted(ConversionResult result)
    {
        var compilation = Program.Compile(
            result.CalorSource!,
            null,
            new CompilationOptions
            {
                EnforceEffects = false,
                UnknownCallPolicy =
                    Calor.Compiler.Effects.UnknownCallPolicy.Permissive
            });
        Assert.False(
            compilation.HasErrors,
            string.Join(
                Environment.NewLine,
                compilation.Diagnostics.Errors.Select(error => error.Message)));
        return compilation.GeneratedCode!;
    }

    private static CSharpCompilation CreateCompilation(params string[] sources)
    {
        return CSharpCompilation.Create(
            $"Issue769_{Guid.NewGuid():N}",
            sources.Select(source =>
                CSharpSyntaxTree.ParseText(
                    source,
                    new CSharpParseOptions(LanguageVersion.Preview))),
            TrustedPlatformReferences.All,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static void AssertNoCompilationErrors(CSharpCompilation compilation)
    {
        var errors = compilation.GetDiagnostics()
            .Where(diagnostic =>
                diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .ToList();
        Assert.Empty(errors);
    }

    private static ITypeSymbol GetFieldType(
        CSharpCompilation compilation,
        string metadataName,
        string fieldName)
        => Assert.IsAssignableFrom<IFieldSymbol>(
            compilation.GetTypeByMetadataName(metadataName)!
                .GetMembers(fieldName)
                .Single()).Type;

    private static LoadedAssembly CompileAndLoad(params string[] sources)
    {
        var compilation = CreateCompilation(sources);
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(
            emit.Success,
            string.Join(
                Environment.NewLine,
                emit.Diagnostics.Where(diagnostic =>
                    diagnostic.Severity
                    == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)));
        stream.Position = 0;
        var loadContext = new AssemblyLoadContext(
            $"Issue769_{Guid.NewGuid():N}",
            isCollectible: true);
        return new LoadedAssembly(
            loadContext,
            loadContext.LoadFromStream(stream));
    }

    private static string InvokeIdentity(Assembly assembly, string typeName)
    {
        var type = assembly.GetType(typeName, throwOnError: true)!;
        var instance = Activator.CreateInstance(type);
        return (string)type.GetMethod("Identity")!.Invoke(instance, null)!;
    }

    private static T GetReturnExpression<T>(
        BoundModule module,
        string functionName)
        where T : BoundExpression
        => Assert.IsType<T>(
            Assert.IsType<BoundReturnStatement>(
                Assert.Single(module.Functions.Single(function =>
                    function.Symbol.Name == functionName).Body)).Expression);

    private static T InvokeStatic<T>(
        Assembly assembly,
        string typeName,
        string methodName)
        => (T)assembly.GetType(typeName, throwOnError: true)!
            .GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, null)!;

    private static string OutsideInterop(string calor)
    {
        var result = Parsing.RawCSharpFrame.MaskForAnalysis(calor);
        while (true)
        {
            var start = result.IndexOf("§CSHARP{", StringComparison.Ordinal);
            if (start < 0)
                return result;
            var end = result.IndexOf("}§/CSHARP", start, StringComparison.Ordinal);
            if (end < 0)
                return result;
            result = result.Remove(start, end + "}§/CSHARP".Length - start);
        }
    }

    private static MigrationPlanEntry Entry(
        string sourcePath,
        string outputPath,
        string identity)
        => new()
        {
            SourcePath = sourcePath,
            OutputPath = outputPath,
            Convertibility = FileConvertibility.Full,
            FileSizeBytes = 1,
            PrimarySymbolIdentity = identity
        };

    private static string FormatIssues(ConversionResult result)
        => string.Join("; ", result.Issues.Select(issue => issue.ToString()));

    private sealed class LoadedAssembly(
        AssemblyLoadContext loadContext,
        Assembly assembly) : IDisposable
    {
        public Assembly Assembly { get; } = assembly;

        public void Dispose()
        {
            loadContext.Unload();
        }
    }
}
