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
            module Topology Example block
              enum Mode
                None = 0
                First = 1
                Second = 2
                All = First | Second

              class Survivor
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
        // 0.30: without the compiler assembly as a reference, the checker's base
        // `IBugPatternChecker` does not resolve (class or interface?), so the class is kept as
        // C# (class-unresolved-base-kind); it must still stay in the enum's namespace scope.
        var followingClass = Assert.Single(
            reparsed.InteropBlocks.Where(item =>
                item.CSharpCode.Contains(
                    "class TypedBugPatternChecker",
                    StringComparison.Ordinal)));
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
        AssertWholeFileTopology(result, source);

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
        AssertWholeFileTopology(result, source);
        var compilation = CreateCompilation(CompileConverted(result));
        AssertNoCompilationErrors(compilation);
        Assert.Equal(SpecialType.System_Double, compilation.GetTypeByMetadataName("Alpha.Calculator")!
            .GetMembers("Value").OfType<IMethodSymbol>().Single().ReturnType.SpecialType);
        Assert.Equal(SpecialType.System_Single, compilation.GetTypeByMetadataName("Beta.Calculator")!
            .GetMembers("Value").OfType<IMethodSymbol>().Single().ReturnType.SpecialType);
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
        AssertWholeFileTopology(result, source);

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
        AssertWholeFileTopology(nested);
        var nestedGenerated = CompileConverted(nested);
        Assert.Contains("namespace Outer", nestedGenerated);
        Assert.Contains("namespace Inner", nestedGenerated);
        var nestedCompilation = CreateCompilation(nestedGenerated);
        AssertNoCompilationErrors(nestedCompilation);
        Assert.NotNull(nestedCompilation.GetTypeByMetadataName("Outer.Inner.NestedType"));

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

        AssertWholeFileTopology(result);
        var generated = CompileConverted(result);
        var compilation = CreateCompilation(generated);
        AssertNoCompilationErrors(compilation);
        Assert.NotNull(compilation.GetTypeByMetadataName("Beta.Kept"));
        Assert.Null(compilation.GetTypeByMetadataName("Empty.Beta.Kept"));
    }

    [Fact]
    public void LossyConversion_EmptyClassBeforeNestedNamespace_KeepsBothScopes()
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

        AssertWholeFileTopology(result);
        var compilation = CreateCompilation(CompileConverted(result));
        AssertNoCompilationErrors(compilation);
        Assert.NotNull(compilation.GetTypeByMetadataName("Outer.Marker"));
        Assert.NotNull(compilation.GetTypeByMetadataName("Outer.Inner.Real"));
    }

    [Fact]
    public void LossyConversion_EmptyInterfaceBeforeNestedNamespace_KeepsBothScopes()
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

        AssertWholeFileTopology(result);
        var compilation = CreateCompilation(CompileConverted(result));
        AssertNoCompilationErrors(compilation);
        Assert.NotNull(compilation.GetTypeByMetadataName("Outer.IMarker"));
        Assert.NotNull(compilation.GetTypeByMetadataName("Outer.Inner.Real"));
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

        // 0.30: the two-namespace donor is whole-file C#, so the merger has no native Alpha.Shared
        // to move out of it and its second namespace stays exactly where the source put it.
        AssertWholeFileTopology(donor);
        var merged = new PartialClassMerger().Merge([target.Ast!, donor.Ast!]);
        var donorModule = merged[1];
        Assert.Empty(donorModule.Classes);
        Assert.Single(Assert.Single(merged[0].Classes).Methods);

        var calor = new CalorEmitter(new ConversionContext()).Emit(donorModule);
        var reparsed = ParseCalor(calor);
        var generated = new CSharpEmitter().Emit(reparsed);
        var compilation = CreateCompilation(generated);
        AssertNoCompilationErrors(compilation);
        Assert.NotNull(compilation.GetTypeByMetadataName("Beta.Survivor"));
        Assert.Null(compilation.GetTypeByMetadataName("Alpha.Beta.Survivor"));
    }

    // 0.30: parts of a namespace-less partial class sit in the module's global scope, whose
    // usings the parser keeps at module level (no scope). The merger tagged the donor's usings
    // with the global scope id, which duplicated the target's own using and made native module
    // emission throw ("cannot flatten distinct namespace or using scopes"), failing the whole
    // `migrate --merge-partials` run. They now merge at module level, once each.
    [Fact]
    public void PartialMerge_GlobalScopeUsings_MergeAtModuleLevel()
    {
        var target = ParseCalor("""
            module First
              using System.Collections.Generic

              class Bag partial
                func A () → int
                  !
                  return 1

            """);
        var donor = ParseCalor("""
            module Second
              using System.Collections.Generic
              using System.Text

              class Bag partial
                func B () → int
                  !
                  return 2

            """);
        var targetClass = Assert.Single(target.Classes);
        Assert.Contains(target.NamespaceScopes, scope => scope.IsGlobal && scope.Id == targetClass.NamespaceScopeId);

        var merged = new PartialClassMerger().Merge([target, donor]);
        Assert.Equal(2, Assert.Single(merged[0].Classes).Methods.Count);
        Assert.Empty(merged[1].Classes);
        Assert.All(merged[0].Usings, directive => Assert.Null(directive.NamespaceScopeId));
        Assert.Equal(new[] { "System.Collections.Generic", "System.Text" },
            merged[0].Usings.Select(directive => directive.Namespace).ToArray());

        var calor = new CalorEmitter(new ConversionContext()).Emit(merged[0]);
        var generated = new CSharpEmitter().Emit(ParseCalor(calor));
        Assert.Single(generated.Split('\n'), line => line.Trim() == "using System.Collections.Generic;");
        AssertNoCompilationErrors(CreateCompilation(generated));
    }

    [Fact]
    public void SimplifiableContracts_PreserveOneNamespaceAndDeclarationMetadata()
    {
        var module = ParseCalor(
            """
            module Test Alpha block
              func Compute () → int
                !
                requires 2 > 1
                return 42
              class Worker
                func Run () → int
                  !
                  requires 2 > 1
                  return 42
              interface IWorker
                func Run () → int
                  !
                  requires 2 > 1
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
                module StorageFile Company.Storage block
                  func Save () → int
                    !
                    return 41
                """);
            File.WriteAllText(
                globalPath,
                """
                module GlobalFile
                  func Ping () → int
                    !
                    return 1
                """);
            File.WriteAllText(
                callerPath,
                """
                module Consumer Consumer block
                  func ReadNamespaced () → int
                    !
                    return Save()

                  func ReadGlobal () → int
                    !
                    return Ping()

                  func ReadExplicitNamespace () → int
                    !
                    return Company.Storage.Save()
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
    public void PrivateSameScopeModuleFunction_RemainsBareAndRuns()
    {
        const string source =
            """
            module LocalModule Alpha block
              func Pick private () → int
                return 7

              func Run () → int
                !
                return Pick()
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
    public void GenericClassesWithSameBaseName_KeepAritySeparated()
    {
        const string source =
            """
            module GenericArity
              class Box<T>
                ctor (int:value)
                  let int:copy1 = value

                func Pick () → int
                  !
                  return 1

              class Box<TFirst,TSecond>
                ctor (string:value)
                  let string?:copy2 = value

                func Pick () → int
                  !
                  return 2

              func UseOne (Box<int>:box) → int
                !
                return box.Pick()

              func UseTwo (Box<int, string>:box) → int
                !
                return box.Pick()

              func MakeOne () → Box<int>
                !
                return new Box<int>(1)

              func MakeTwo () → Box<int, string>
                !
                return new Box<int, string>("two")
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
        Assert.Contains("module _global _global block", result.CalorSource);

        var generated = CompileConverted(result);
        var compilation = CreateCompilation(generated);
        AssertNoCompilationErrors(compilation);
        Assert.NotNull(compilation.GetTypeByMetadataName("_global.Thing"));
        Assert.Null(compilation.GetTypeByMetadataName("Thing"));
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

    /// <summary>
    /// 0.30: a native module header carries one namespace, so a file with several (or nested)
    /// namespace scopes, or namespace-local usings, is kept as one whole-file C# interop block.
    /// The tests then check the semantics through the compiled output.
    /// </summary>
    private static void AssertWholeFileTopology(ConversionResult result, string? source = null)
    {
        Assert.Contains(result.Losses, loss => loss.Feature == "namespace-topology"
            && loss.Kind == ConversionLossKind.InteropPreserved);
        Assert.Empty(result.Ast!.Classes);
        var interop = Assert.Single(result.Ast.InteropBlocks);
        if (source != null)
            Assert.Equal(source, interop.CSharpCode);
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
