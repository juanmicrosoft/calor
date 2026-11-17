using System.Reflection;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using Binder = Calor.Compiler.Binding.Binder;

namespace Calor.Compiler.Tests;

public class Issue766DeclarationModuleSemanticsTests
{
    private static readonly TextSpan Span = TextSpan.Empty;

    [Fact]
    public void UsingDirectives_PreserveCompleteObjectsAndSourceDuplicates()
    {
        var module = Parse(
            """
            module UsingCases UsingCases block
              global using System.Threading
              global using Tasks = System.Threading.Tasks
              global using static System.Math
              using System.Text
              using System.Text
              using Alias = System.Collections.Generic
              using static System.Math
              func Value () → int
                !
                return 1
            """);

        Assert.Collection(
            module.Usings,
            u => AssertUsing(u, "System.Threading", isGlobal: true),
            u => AssertUsing(
                u,
                "System.Threading.Tasks",
                alias: "Tasks",
                isGlobal: true),
            u => AssertUsing(
                u,
                "System.Math",
                isStatic: true,
                isGlobal: true),
            u => AssertUsing(u, "System.Text"),
            u => AssertUsing(u, "System.Text"),
            u => AssertUsing(u, "System.Collections.Generic", alias: "Alias"),
            u => AssertUsing(u, "System.Math", isStatic: true));

        var generated = new CSharpEmitter().Emit(module);

        Assert.Equal(2, CountOccurrences(generated, "using System.Text;"));
        Assert.Contains("using Alias = System.Collections.Generic;", generated);
        Assert.Contains("using static System.Math;", generated);
        Assert.Contains("global using System.Threading;", generated);
        Assert.Contains("global using Tasks = System.Threading.Tasks;", generated);
        Assert.Contains("global using static System.Math;", generated);
        Assert.True(
            generated.IndexOf("global using", StringComparison.Ordinal)
            < generated.IndexOf("using System;", StringComparison.Ordinal));
        Assert.True(GeneratedCSharpCompiler.Validate(generated).CompilationSuccess);
        var duplicateValidation = GeneratedCSharpCompiler.Validate(
            [new GeneratedCSharpSource(generated, "duplicate-usings.g.cs")],
            new GeneratedCSharpCompilationContext
            {
                TreatWarningsAsErrors = true
            });
        Assert.False(duplicateValidation.CompilationSuccess);
        Assert.Contains(
            duplicateValidation.CompilationErrors,
            diagnostic => diagnostic.Id == "CS0105");
        Assert.Equal(
            "using Pair = (int Left, int Right);",
            new UsingDirectiveNode(
                Span,
                "(i32 Left, i32 Right)",
                alias: "Pair").Accept(new CSharpEmitter()));

        var conditional = new CSharpEmitter().Emit(Parse(
            """
            module ConditionalUsing
              pp FEATURE
                global using System.Text
                using Buffers = System.Buffers
            """));
        Assert.Contains("global using System.Text;", conditional);
        Assert.Contains("using Buffers = System.Buffers;", conditional);
        Assert.DoesNotContain("namespace ConditionalUsing", conditional);
        var conditionalValidation = GeneratedCSharpCompiler.Validate(
            [new GeneratedCSharpSource(conditional, "conditional.g.cs")],
            new GeneratedCSharpCompilationContext
            {
                IncludeImplicitGlobalUsings = false,
                PreprocessorSymbols = ["FEATURE"]
            });
        Assert.True(
            conditionalValidation.CompilationSuccess,
            string.Join(
                Environment.NewLine,
                conditionalValidation.FormattedCompilationErrors));
    }

    [Fact]
    public void CSharpConversion_PreservesNormalAliasStaticAndGlobalUsings()
    {
        const string csharp =
            """
            global using System;
            global using Text = System.Text;
            global using static System.Math;
            using Collections = System.Collections.Generic;

            public class Demo { }
            """;

        var result = new CSharpToCalorConverter(new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossy,
            ModuleName = "UsingConversion",
            AutoGenerateIds = true
        }).Convert(csharp);

        Assert.True(result.Success, string.Join("; ", result.Issues.Select(i => i.Message)));
        Assert.NotNull(result.Ast);
        Assert.Contains(
            result.Ast.Usings,
            u => u.Namespace == "System" && u.IsGlobal && !u.IsStatic && u.Alias == null);
        Assert.Contains(
            result.Ast.Usings,
            u => u.Namespace == "System.Text" && u.IsGlobal && u.Alias == "Text");
        Assert.Contains(
            result.Ast.Usings,
            u => u.Namespace == "System.Math" && u.IsGlobal && u.IsStatic);
        Assert.Contains(
            result.Ast.Usings,
            u => u.Namespace == "System.Collections.Generic"
                && !u.IsGlobal
                && u.Alias == "Collections");
        Assert.Contains("global using System\n", result.CalorSource);
        Assert.Contains("global using Text = System.Text", result.CalorSource);
        Assert.Contains("global using static System.Math", result.CalorSource);
    }

    [Fact]
    public void ConditionalUsingMigration_PreservesAllBranchesWithoutRootDuplicates()
    {
        var result = ConvertWithoutPreprocessorStripping(
            """
            #if FEATURE
            using Selected = FeatureNs.Marker;
            #elif ALT
            using Selected = AltNs.Marker;
            #else
            using Selected = FallbackNs.Marker;
            #endif

            public class Consumer
            {
                public object Create() => new Selected();
            }
            """);

        Assert.Empty(result.Ast!.Usings);
        var block = Assert.Single(result.Ast.TypePreprocessorBlocks);
        Assert.Equal("FEATURE", block.Condition);
        Assert.Equal("FeatureNs.Marker", Assert.Single(block.Usings).Namespace);
        Assert.Equal("Selected", block.Usings[0].Alias);
        Assert.NotNull(block.ElseBranch);
        Assert.Equal("ALT", block.ElseBranch!.Condition);
        Assert.Equal("AltNs.Marker", Assert.Single(block.ElseBranch.Usings).Namespace);
        Assert.NotNull(block.ElseBranch.ElseBranch);
        Assert.Equal("", block.ElseBranch.ElseBranch!.Condition);
        Assert.Equal(
            "FallbackNs.Marker",
            Assert.Single(block.ElseBranch.ElseBranch.Usings).Namespace);

        var generated = CompileConvertedCalor(result);
        const string definitions =
            """
            namespace FeatureNs { public sealed class Marker { } }
            namespace AltNs { public sealed class Marker { } }
            namespace FallbackNs { public sealed class Marker { } }
            """;

        Assert.Equal(
            "FeatureNs.Marker",
            InvokeCreatedType(CompileAssembly(generated + definitions, ["FEATURE"])));
        Assert.Equal(
            "AltNs.Marker",
            InvokeCreatedType(CompileAssembly(generated + definitions, ["ALT"])));
        Assert.Equal(
            "FallbackNs.Marker",
            InvokeCreatedType(CompileAssembly(generated + definitions)));
    }

    [Fact]
    public void ConditionalUsingMigration_PreservesNestedGroupsAndAliasScopes()
    {
        var result = ConvertWithoutPreprocessorStripping(
            """
            #if OUTER
            using Root = OuterNs.Root;
            #if INNER
            using Leaf = InnerNs.Leaf;
            #else
            using Leaf = FallbackNs.Leaf;
            #endif
            using Tail = TailNs.Marker;
            #else
            using Root = OtherNs.Root;
            #endif
            """);

        Assert.Empty(result.Ast!.Usings);
        var outer = Assert.Single(result.Ast.TypePreprocessorBlocks);
        Assert.Equal("OUTER", outer.Condition);
        Assert.Equal(
            ["OuterNs.Root", "TailNs.Marker"],
            outer.Usings.Select(usingDirective => usingDirective.Namespace));
        var inner = Assert.Single(outer.NestedBlocks);
        Assert.Collection(
            outer.Items,
            item => Assert.IsType<UsingDirectiveNode>(item),
            item => Assert.Same(inner, item),
            item => Assert.IsType<UsingDirectiveNode>(item));
        Assert.Equal("INNER", inner.Condition);
        Assert.Equal("InnerNs.Leaf", Assert.Single(inner.Usings).Namespace);
        Assert.Equal(
            "FallbackNs.Leaf",
            Assert.Single(inner.ElseBranch!.Usings).Namespace);
        Assert.Equal(
            "OtherNs.Root",
            Assert.Single(outer.ElseBranch!.Usings).Namespace);
        var conditionalUsingCode = CompileConvertedCalor(result);
        Assert.True(
            conditionalUsingCode.IndexOf("using Root = OuterNs.Root;", StringComparison.Ordinal)
            < conditionalUsingCode.IndexOf("#if INNER", StringComparison.Ordinal));
        Assert.True(
            conditionalUsingCode.IndexOf("#endif", conditionalUsingCode.IndexOf("#if INNER", StringComparison.Ordinal), StringComparison.Ordinal)
            < conditionalUsingCode.IndexOf("using Tail = TailNs.Marker;", StringComparison.Ordinal));

        var generated = conditionalUsingCode
            + """

              public static class ConditionalAliasHarness
              {
                  public static object CreateRoot() => new Root();
              #if OUTER
                  public static object CreateLeaf() => new Leaf();
              #endif
              }
              namespace OuterNs { public sealed class Root { } }
              namespace OtherNs { public sealed class Root { } }
              namespace InnerNs { public sealed class Leaf { } }
              namespace FallbackNs { public sealed class Leaf { } }
              namespace TailNs { public sealed class Marker { } }
              """;

        var innerAssembly = CompileAssembly(generated, ["OUTER", "INNER"]);
        Assert.Equal(
            "OuterNs.Root",
            InvokeStaticCreatedType(innerAssembly, "CreateRoot"));
        Assert.Equal(
            "InnerNs.Leaf",
            InvokeStaticCreatedType(innerAssembly, "CreateLeaf"));

        var fallbackAssembly = CompileAssembly(generated, ["OUTER"]);
        Assert.Equal(
            "OuterNs.Root",
            InvokeStaticCreatedType(fallbackAssembly, "CreateRoot"));
        Assert.Equal(
            "FallbackNs.Leaf",
            InvokeStaticCreatedType(fallbackAssembly, "CreateLeaf"));

        Assert.Equal(
            "OtherNs.Root",
            InvokeStaticCreatedType(
                CompileAssembly(generated),
                "CreateRoot"));
    }

    [Fact]
    public void TypePreprocessorElse_EmitsNestedIfDeclarationsUnderEverySymbolSet()
    {
        var result = Program.Compile(
            """
            module NestedElseTypes NestedElseTypes block
              pp OUTER
                class OuterSelected
                  func Value static () → int
                    !
                    return 1

              pp else
                class ElseDirect
                  field int:Value

                pp INNER
                  class InnerSelected
                    func Value static () → int
                      !
                      return 2

                pp else
                  class InnerFallback
                    func Value static () → int
                      !
                      return 3

                interface IElseTail
            """,
            null,
            new CompilationOptions { DeferGeneratedOutputValidation = true });

        Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics));
        var generated = result.GeneratedCode;
        Assert.Contains("#else", generated);
        Assert.Contains("#if INNER", generated);
        var elseDirect = generated.IndexOf("class ElseDirect", StringComparison.Ordinal);
        var nestedTypeIf = generated.IndexOf("#if INNER", elseDirect, StringComparison.Ordinal);
        Assert.True(
            elseDirect < nestedTypeIf,
            generated);
        Assert.True(
            generated.IndexOf("#endif", nestedTypeIf, StringComparison.Ordinal)
            < generated.IndexOf("interface IElseTail", StringComparison.Ordinal),
            generated);

        var outerAssembly = CompileAssembly(generated, ["OUTER"]);
        AssertTypePresence(
            outerAssembly,
            present: ["OuterSelected"],
            absent: ["ElseDirect", "InnerSelected", "InnerFallback", "IElseTail"]);
        Assert.Equal(1, InvokeStaticInt(outerAssembly, "OuterSelected", "Value"));

        var innerAssembly = CompileAssembly(generated, ["INNER"]);
        AssertTypePresence(
            innerAssembly,
            present: ["ElseDirect", "InnerSelected", "IElseTail"],
            absent: ["OuterSelected", "InnerFallback"]);
        Assert.Equal(2, InvokeStaticInt(innerAssembly, "InnerSelected", "Value"));

        var fallbackAssembly = CompileAssembly(generated);
        AssertTypePresence(
            fallbackAssembly,
            present: ["ElseDirect", "InnerFallback", "IElseTail"],
            absent: ["OuterSelected", "InnerSelected"]);
        Assert.Equal(3, InvokeStaticInt(fallbackAssembly, "InnerFallback", "Value"));
    }

    [Fact]
    public void RoslynMigration_ElseNestedIfDeclarationsCompileAndRunForBothSymbols()
    {
        var conversion = ConvertWithoutPreprocessorStripping(
            """
            #if OUTER
            using SelectedMarker = OuterNs.Marker;
            public static class OuterSelected
            {
                public static int Value() => new SelectedMarker().Value;
            }
            #else
            using DirectMarker = ElseNs.Marker;
            #if INNER
            using SelectedMarker = InnerNs.Marker;
            #else
            using SelectedMarker = InnerFallbackNs.Marker;
            #endif
            public sealed class ElseDirect
            {
                public int Value => new DirectMarker().Value;
            }
            #if INNER
            public static class InnerSelected
            {
                public static int Value() => new SelectedMarker().Value;
            }
            #else
            public static class InnerFallback
            {
                public static int Value() => new SelectedMarker().Value;
            }
            #endif
            public interface IElseTail
            {
                int Value { get; }
            }
            #endif
            """);

        var generated = CompileConvertedCalor(conversion)
            + """

              namespace OuterNs { public sealed class Marker { public int Value => 1; } }
              namespace ElseNs { public sealed class Marker { public int Value => 10; } }
              namespace InnerNs { public sealed class Marker { public int Value => 2; } }
              namespace InnerFallbackNs { public sealed class Marker { public int Value => 3; } }
              """;
        Assert.Contains("#else", generated);
        Assert.Contains("#if INNER", generated);
        var elseDirect = generated.IndexOf("class ElseDirect", StringComparison.Ordinal);
        var nestedTypeIf = generated.IndexOf("#if INNER", elseDirect, StringComparison.Ordinal);
        Assert.True(
            elseDirect < nestedTypeIf,
            generated);
        Assert.True(
            generated.IndexOf("#endif", nestedTypeIf, StringComparison.Ordinal)
            < generated.IndexOf("interface IElseTail", StringComparison.Ordinal),
            generated);

        var innerAssembly = CompileAssembly(generated, ["INNER"]);
        AssertTypePresence(
            innerAssembly,
            present: ["ElseDirect", "InnerSelected", "IElseTail"],
            absent: ["OuterSelected", "InnerFallback"]);
        Assert.Equal(2, InvokeStaticInt(innerAssembly, "InnerSelected", "Value"));

        var fallbackAssembly = CompileAssembly(generated);
        AssertTypePresence(
            fallbackAssembly,
            present: ["ElseDirect", "InnerFallback", "IElseTail"],
            absent: ["OuterSelected", "InnerSelected"]);
        Assert.Equal(3, InvokeStaticInt(fallbackAssembly, "InnerFallback", "Value"));
    }

    [Fact]
    public void ConditionalNativeRecord_RemainsGuardedAndRunsPerSymbol()
    {
        var conversion = ConvertWithoutPreprocessorStripping(
            """
            #if FEATURE
            public record Selected(int Value);
            public delegate T Factory<T>();
            #else
            public sealed class Fallback
            {
                public int Value => 2;
            }
            #endif

            public static class RecordHarness
            {
            #if FEATURE
                public static int Run() => new Selected(7).Value;
            #else
                public static int Run() => new Fallback().Value;
            #endif
            }
            """);

        Assert.DoesNotContain(
            conversion.Ast!.InteropBlocks,
            interop => interop.CSharpCode.Contains("record Selected", StringComparison.Ordinal));
        var typeBlock = Assert.Single(conversion.Ast.TypePreprocessorBlocks);
        Assert.Single(typeBlock.InteropBlocks);
        Assert.True(Assert.Single(typeBlock.Classes).IsRecord);
        Assert.DoesNotContain(typeBlock.InteropBlocks, interop =>
            interop.CSharpCode.Contains("record Selected", StringComparison.Ordinal));
        Assert.Contains(
            typeBlock.InteropBlocks,
            interop => interop.CSharpCode.Contains(
                "delegate T Factory<T>()",
                StringComparison.Ordinal));
        Assert.Empty(typeBlock.ElseBranch!.InteropBlocks);

        var generated = CompileConvertedCalor(conversion);
        var featureAssembly = CompileAssembly(generated, ["FEATURE"]);
        AssertTypePresence(
            featureAssembly,
            present: ["Selected", "Factory`1", "RecordHarness"],
            absent: ["Fallback"]);
        Assert.Equal(7, InvokeStaticInt(featureAssembly, "RecordHarness", "Run"));
        Assert.NotNull(featureAssembly.GetTypes().Single(type => type.Name == "Selected").GetMethod("<Clone>$"));

        var fallbackAssembly = CompileAssembly(generated);
        AssertTypePresence(
            fallbackAssembly,
            present: ["Fallback", "RecordHarness"],
            absent: ["Selected", "Factory`1"]);
        Assert.Equal(2, InvokeStaticInt(fallbackAssembly, "RecordHarness", "Run"));
    }

    [Fact]
    public void ConditionalUnsupportedNestedTypes_RemainInsideMemberBranches()
    {
        var conversion = ConvertWithoutPreprocessorStripping(
            """
            public class Container
            {
            #if FEATURE
                public int Before;
                public record Inner(int Value);
                public int After;
            #else
                public int BeforeElse;
                public struct Inner
                {
                    public int Value;
                }
                public int AfterElse;
            #endif
            }
            """);

        var container = Assert.Single(
            conversion.Ast!.Classes.Where(type => type.Name == "Container"));
        Assert.DoesNotContain(
            conversion.Ast.Classes,
            type => type.Name == "Inner");
        Assert.Empty(container.InteropBlocks);
        var block = Assert.Single(container.PreprocessorBlocks);
        var featureRaw = Assert.Single(block.InteropBlocks).CSharpCode;
        Assert.Contains("Before", featureRaw);
        Assert.Contains("record Inner", featureRaw);
        Assert.Contains("After", featureRaw);
        Assert.Collection(
            block.ElseBranch!.Items,
            item => Assert.Equal(
                "BeforeElse",
                Assert.IsType<ClassFieldNode>(item).Name),
            item => Assert.Contains(
                "struct Inner",
                Assert.IsType<CSharpInteropBlockNode>(item).CSharpCode),
            item => Assert.Equal(
                "AfterElse",
                Assert.IsType<ClassFieldNode>(item).Name));

        var featureInner = CompileAssembly(
                CompileConvertedCalor(conversion),
                ["FEATURE"])
            .GetTypes()
            .Single(type =>
                type.Name == "Inner"
                && type.DeclaringType?.Name == "Container");
        Assert.False(featureInner.IsValueType);
        Assert.NotNull(featureInner.GetProperty("Value"));
        Assert.NotNull(featureInner.DeclaringType!.GetField("Before"));
        Assert.NotNull(featureInner.DeclaringType.GetField("After"));

        var fallbackInner = CompileAssembly(CompileConvertedCalor(conversion))
            .GetTypes()
            .Single(type =>
                type.Name == "Inner"
                && type.DeclaringType?.Name == "Container");
        Assert.True(fallbackInner.IsValueType);
        Assert.NotNull(fallbackInner.GetField("Value"));
        Assert.NotNull(fallbackInner.DeclaringType!.GetField("BeforeElse"));
        Assert.NotNull(fallbackInner.DeclaringType.GetField("AfterElse"));
    }

    [Fact]
    public void InterfaceDefaultPrivateAndStaticMethods_PreserveBodiesAndRuntime()
    {
        var conversion = new CSharpToCalorConverter().Convert(
            """
            public interface IDefaults
            {
                int Value() => 42;
                private int Hidden() => 1;
                static int StaticValue() => 3;
            }
            public sealed class Defaults : IDefaults { }
            public static class InterfaceHarness
            {
                public static int Run() => ((IDefaults)new Defaults()).Value();
            }
            """,
            "InterfaceDefaults.cs");
        Assert.True(
            conversion.Success,
            string.Join("; ", conversion.Issues.Select(issue => issue.Message)));
        Assert.Empty(conversion.Ast!.Interfaces);
        var loss = Assert.Single(conversion.Losses.Where(item =>
            item.Feature == "interface-method-semantics"));
        Assert.Equal(ConversionLossKind.InteropPreserved, loss.Kind);
        Assert.DoesNotContain(conversion.Losses, item =>
            item.Kind == ConversionLossKind.Dropped);
        Assert.DoesNotContain("§IFACE{", conversion.CalorSource);
        var generated = CompileConvertedCalor(conversion);
        var assembly = CompileAssembly(generated);
        Assert.Equal(
            42,
            InvokeStaticInt(assembly, "InterfaceHarness", "Run"));
        Assert.Contains("private int Hidden() => 1;", generated);
        Assert.Contains("static int StaticValue() => 3;", generated);
    }

    [Fact]
    public void DelegateVisibilityAttributesAndModifiers_PreserveExactApi()
    {
        var conversion = new CSharpToCalorConverter().Convert(
            """
            [System.Obsolete("legacy")]
            internal delegate int HiddenHandler(int value);
            public static class DelegateHarness
            {
                public static int Run()
                {
                    HiddenHandler handler = value => value + 1;
                    return handler(41);
                }
            }
            """,
            "DelegateSemantics.cs");
        Assert.True(
            conversion.Success,
            string.Join("; ", conversion.Issues.Select(issue => issue.Message)));
        Assert.Empty(conversion.Ast!.Delegates);
        Assert.Contains(
            conversion.Ast.InteropBlocks,
            interop => interop.CSharpCode.Contains(
                "internal delegate int HiddenHandler",
                StringComparison.Ordinal));
        var assembly = CompileAssembly(CompileConvertedCalor(conversion));
        var delegateType = assembly.GetType("HiddenHandler")
            ?? throw new InvalidOperationException("HiddenHandler missing");
        Assert.True(delegateType.IsNotPublic);
        Assert.NotNull(delegateType.GetCustomAttribute<ObsoleteAttribute>());
        Assert.Equal(
            42,
            InvokeStaticInt(assembly, "DelegateHarness", "Run"));
    }

    [Fact]
    public void ModuleConditionalScanner_ExcludesNestedGroupsForBothNamespaceForms()
    {
        string[] sources =
        [
            """
            namespace BlockScoped
            {
            #if TOP
                public record Same(int Value);
            #else
                public sealed class Same
                {
                    public int Value => 2;
                }
            #endif

                public class Container
                {
                #if NESTED
                    public record Same(int Value);
                #else
                    public sealed class Same
                    {
                        public int Value => 4;
                    }
                #endif
                }
            }
            """,
            """
            namespace FileScoped;

            #if TOP
            public record Same(int Value);
            #else
            public sealed class Same
            {
                public int Value => 2;
            }
            #endif

            public class Container
            {
            #if NESTED
                public record Same(int Value);
            #else
                public sealed class Same
                {
                    public int Value => 4;
                }
            #endif
            }
            """
        ];

        foreach (var source in sources)
        {
            var conversion = ConvertWithoutPreprocessorStripping(source);
            Assert.Empty(conversion.Ast!.InteropBlocks);
            var moduleBlock = Assert.Single(conversion.Ast.TypePreprocessorBlocks);
            Assert.Equal("TOP", moduleBlock.Condition);

            var container = Assert.Single(
                conversion.Ast.Classes.Where(type => type.Name == "Container"));
            var memberBlock = Assert.Single(container.PreprocessorBlocks);
            Assert.Equal("NESTED", memberBlock.Condition);
            Assert.DoesNotContain(
                conversion.Ast.TypePreprocessorBlocks,
                block => block.Condition == "NESTED");

            var generated = CompileConvertedCalor(conversion);
            AssertConditionalSameTypes(CompileAssembly(generated), false, false);
            AssertConditionalSameTypes(
                CompileAssembly(generated, ["TOP"]),
                true,
                false);
            AssertConditionalSameTypes(
                CompileAssembly(generated, ["NESTED"]),
                false,
                true);
            AssertConditionalSameTypes(
                CompileAssembly(generated, ["TOP", "NESTED"]),
                true,
                true);
        }
    }

    [Fact]
    public void TypePreprocessorElif_EmitsNestedDeclarationBranchesInSourceOrder()
    {
        var result = Program.Compile(
            """
            module ElifNestedTypes ElifNestedTypes block
              pp FIRST
                class FirstSelected
                  field int:Value
              pp elif SECOND
                interface ISecondBefore
                  property int:Value
                    get
                pp DEEP
                  class DeepSelected
                    field int:Value
                class SecondAfter
                  field int:Value
              pp else
                class FinalFallback
                  field int:Value
            """,
            null,
            new CompilationOptions { DeferGeneratedOutputValidation = true });

        Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics));
        var generated = result.GeneratedCode;
        Assert.Contains("#elif SECOND", generated);
        var second = generated.IndexOf("#elif SECOND", StringComparison.Ordinal);
        var before = generated.IndexOf("interface ISecondBefore", second, StringComparison.Ordinal);
        var nested = generated.IndexOf("#if DEEP", second, StringComparison.Ordinal);
        var after = generated.IndexOf("class SecondAfter", second, StringComparison.Ordinal);
        Assert.True(second < before && before < nested && nested < after);

        AssertTypePresence(
            CompileAssembly(generated, ["FIRST"]),
            present: ["FirstSelected"],
            absent: ["ISecondBefore", "DeepSelected", "SecondAfter", "FinalFallback"]);
        AssertTypePresence(
            CompileAssembly(generated, ["SECOND", "DEEP"]),
            present: ["ISecondBefore", "DeepSelected", "SecondAfter"],
            absent: ["FirstSelected", "FinalFallback"]);
        AssertTypePresence(
            CompileAssembly(generated),
            present: ["FinalFallback"],
            absent: ["FirstSelected", "ISecondBefore", "DeepSelected", "SecondAfter"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReorderedSameTypeRecordFields_PreserveRuntimeValues(bool throughCanonicalSyntax)
    {
        var creation = new RecordCreationNode(
            Span,
            "Pair",
            [
                new FieldAssignmentNode(
                    Span,
                    "Right",
                    new StringLiteralNode(Span, "right")),
                new FieldAssignmentNode(
                    Span,
                    "Left",
                    new StringLiteralNode(Span, "left"))
            ]);

        var expression = creation.Accept(new CSharpEmitter());
        if (throughCanonicalSyntax)
        {
            var canonical = creation.Accept(new CalorEmitter());
            var module = Parse(
                $"module RecordMigration RecordMigration block\n  func Create () → Pair\n    !\n    return {canonical}");
            var replay = Assert.IsType<ReturnStatementNode>(module.Functions[0].Body[0]).Expression!;
            expression = replay.Accept(new CSharpEmitter());
        }
        Assert.Equal(
            "new Pair(Right: \"right\", Left: \"left\")",
            expression);

        var assembly = CompileAssembly(
            $$"""
              public record Pair(string Left, string Right);
              public static class RecordHarness
              {
                  public static Pair Create() => {{expression}};
              }
              """);
        var pair = assembly
            .GetType("RecordHarness")!
            .GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, null)!;

        Assert.Equal("left", pair.GetType().GetProperty("Left")!.GetValue(pair));
        Assert.Equal("right", pair.GetType().GetProperty("Right")!.GetValue(pair));
    }

    [Fact]
    public void CalorEmitter_PreservesRecordFieldNames()
    {
        var creation = new RecordCreationNode(
            Span,
            "Pair",
            [
                new FieldAssignmentNode(Span, "Right", new StringLiteralNode(Span, "r")),
                new FieldAssignmentNode(Span, "Left", new StringLiteralNode(Span, "l"))
            ]);

        Assert.Equal(
            "new Pair(Right: \"r\", Left: \"l\")",
            creation.Accept(new CalorEmitter()));
    }

    [Theory]
    [InlineData("(Right: \"right\", Left: \"left\")", "Right", "Left")]
    [InlineData("(\"left\", Right: \"right\")", null, "Right")]
    public void NamedConstructorArguments_SurviveParserBindingAndBothEmitters(
        string arguments, string? firstName, string secondName)
    {
        var module = Parse(
            $$"""
            module NamedConstruction NamedConstruction block
              func Create () → Pair
                !
                return new Pair{{arguments}}
            """);
        var creation = Assert.IsType<NewExpressionNode>(
            Assert.IsType<ReturnStatementNode>(Assert.Single(module.Functions).Body[0]).Expression);
        Assert.Equal([firstName, secondName], creation.ArgumentNames);

        var bound = new Binder(new DiagnosticBag()).Bind(module);
        var boundCreation = Assert.IsType<BoundNewExpression>(
            Assert.IsType<BoundReturnStatement>(Assert.Single(bound.Functions).Body[0]).Expression);
        Assert.Equal(creation.ArgumentNames, boundCreation.ArgumentNames);

        var emitted = new CalorEmitter().Emit(module);
        var reparsed = Parse(emitted);
        var replay = Assert.IsType<NewExpressionNode>(
            Assert.IsType<ReturnStatementNode>(Assert.Single(reparsed.Functions).Body[0]).Expression);
        Assert.Equal(creation.ArgumentNames, replay.ArgumentNames);
        var expression = replay.Accept(new CSharpEmitter());
        var assembly = CompileAssembly(
            $$"""
            public record Pair(string Left, string Right);
            public static class RecordHarness
            {
                public static Pair Create() => {{expression}};
            }
            """);
        var pair = assembly.GetType("RecordHarness")!.GetMethod("Create")!.Invoke(null, null)!;
        Assert.Equal("left", pair.GetType().GetProperty("Left")!.GetValue(pair));
        Assert.Equal("right", pair.GetType().GetProperty("Right")!.GetValue(pair));
    }

    [Fact]
    public void NamedConstructorArguments_ResolveTheCorrectInternalOverload()
    {
        var module = Parse(
            """
            module ConstructorResolution ConstructorResolution block
              class Pair
                ctor (int:Left, string:Right)

                ctor (string:Left, int:Right)

              func Create () → Pair
                !
                return new Pair(Right: "right", Left: 1)
            """);
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(module);
        Assert.DoesNotContain(diagnostics, diagnostic =>
            diagnostic.Code is DiagnosticCode.AmbiguousOverload or DiagnosticCode.NoMatchingOverload);
        var creation = Assert.IsType<BoundNewExpression>(
            Assert.IsType<BoundReturnStatement>(
                bound.Functions.Single(function => function.Symbol.Name == "Create").Body[0]).Expression);
        Assert.NotNull(creation.ResolvedConstructor);
        Assert.Equal("Left", creation.ResolvedConstructor.Parameters[0].Name);
        Assert.Equal("int", creation.ResolvedConstructor.Parameters[0].TypeName);
        Assert.Equal("string", creation.ResolvedConstructor.Parameters[1].TypeName);
        Assert.Equal(["Right", "Left"], creation.ArgumentNames);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NamedConstructorConversion_PreservesReorderedValuesAndEvaluationOrder(bool targetTyped)
    {
        var creation = targetTyped
            ? "new(Right: Next(\"right\"), Left: Next(\"left\"))"
            : "new Pair(Right: Next(\"right\"), Left: Next(\"left\"))";
        var conversion = ConvertWithoutPreprocessorStripping(
            $$"""
            public sealed class Pair
            {
                public string Left { get; }
                public string Right { get; }
                public Pair(string Left, string Right)
                {
                    this.Left = Left;
                    this.Right = Right;
                }
            }
            public static class ConstructorHarness
            {
                public static string Log = "";
                private static string Next(string value)
                {
                    Log += value;
                    return value;
                }
                public static Pair Create()
                {
                    return {{creation}};
                }
            }
            """);
        Assert.Contains("Right: Next(", conversion.CalorSource);
        Assert.Contains("Left: Next(", conversion.CalorSource);
        Assert.DoesNotContain("§CS", conversion.CalorSource);
        var assembly = CompileAssembly(CompileConvertedCalor(conversion));
        var harness = assembly.GetTypes().Single(type => type.Name == "ConstructorHarness");
        var pair = harness.GetMethod("Create")!.Invoke(null, null)!;
        Assert.Equal("left", pair.GetType().GetProperty("Left")!.GetValue(pair));
        Assert.Equal("right", pair.GetType().GetProperty("Right")!.GetValue(pair));
        Assert.Equal("rightleft", harness.GetField("Log")!.GetValue(null));
    }

    [Fact]
    public void SimplifyingConstructorArguments_PreservesTheirNames()
    {
        var module = Parse(
            """
            module ConstructorSimplification ConstructorSimplification block
              func Create () → Pair
                !
                return new Pair(Right: (1 + 2), Left: 4)
            """);
        var creation = Assert.IsType<NewExpressionNode>(
            Assert.IsType<ReturnStatementNode>(module.Functions[0].Body[0]).Expression);
        var simplified = Assert.IsType<NewExpressionNode>(
            creation.Accept(new Calor.Compiler.Verification.ExpressionSimplifier()));
        Assert.Equal(creation.ArgumentNames, simplified.ArgumentNames);
        Assert.Equal("new Pair(Right: 3, Left: 4)", simplified.Accept(new CSharpEmitter()));
    }

    [Fact]
    public void NamedStringBuilderConstructor_DoesNotLoseNamesInNativeLowering()
    {
        var conversion = ConvertWithoutPreprocessorStripping(
            """
            using System.Text;
            public static class ConstructorHarness
            {
                public static int Run()
                {
                    var builder = new StringBuilder(maxCapacity: 100, capacity: 16);
                    return builder.Capacity * 1000 + builder.MaxCapacity;
                }
            }
            """);
        Assert.Contains("new StringBuilder(", conversion.CalorSource);
        Assert.Contains("maxCapacity: 100", conversion.CalorSource);
        Assert.Contains("capacity: 16", conversion.CalorSource);
        Assert.DoesNotContain("§CS", conversion.CalorSource);
        var assembly = CompileAssembly(CompileConvertedCalor(conversion));
        var harness = assembly.GetTypes().Single(type => type.Name == "ConstructorHarness");
        Assert.Equal(16100, harness.GetMethod("Run")!.Invoke(null, null));
    }

    [Fact]
    public void InterfaceVarianceAndConstraints_ArePreservedMappedAndCompilable()
    {
        var module = Parse(
            """
            module Variance Variance block
              interface IVariant<out TOut, in TIn, TUnmanaged, TNotNull, TDerived>
                where TOut : class?
                where TIn : class, new()
                where TUnmanaged : unmanaged
                where TNotNull : notnull, System.IComparable<TNotNull>
                where TDerived : ExternalBase, IMarker, new()
            """);

        var typeParameters = Assert.Single(module.Interfaces).TypeParameters;
        Assert.Equal(Calor.Compiler.Ast.VarianceKind.Out, typeParameters[0].Variance);
        Assert.Equal(Calor.Compiler.Ast.VarianceKind.In, typeParameters[1].Variance);
        Assert.Equal(TypeConstraintKind.ClassNullable, Assert.Single(typeParameters[0].Constraints).Kind);
        Assert.Equal(
            [TypeConstraintKind.NotNull, TypeConstraintKind.TypeName],
            typeParameters[3].Constraints.Select(c => c.Kind));

        var generated = new CSharpEmitter().Emit(module);
        Assert.Contains("IVariant<out TOut, in TIn, TUnmanaged, TNotNull, TDerived>", generated);
        Assert.Contains("where TOut : class?", generated);
        Assert.Contains("where TIn : class, new()", generated);
        Assert.Contains("where TUnmanaged : unmanaged", generated);
        Assert.Contains("where TNotNull : notnull, System.IComparable<TNotNull>", generated);
        Assert.Contains("where TDerived : ExternalBase, IMarker, new()", generated);

        var validation = GeneratedCSharpCompiler.Validate(
            generated,
            "public class ExternalBase { } public interface IMarker { }");
        Assert.True(
            validation.CompilationSuccess,
            string.Join(Environment.NewLine, validation.FormattedCompilationErrors));
    }

    [Fact]
    public void Variance_OnIllegalOwner_IsRejectedWithoutDiscardingTheAstField()
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(
            """
            module InvalidVariance InvalidVariance block
              class Box<out T>
            """,
            diagnostics);
        var parser = new Parser(lexer.TokenizeAllForParser(), diagnostics);
        var module = parser.Parse();

        Assert.Equal(
            Calor.Compiler.Ast.VarianceKind.Out,
            Assert.Single(module.Classes).TypeParameters[0].Variance);
        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.InvalidTypeParameterVariance);
    }

    [Fact]
    public void EverySupportedConstraintKind_HasAnExplicitEmission()
    {
        var emitter = new CSharpEmitter();
        var cases = new Dictionary<TypeConstraintKind, string>
        {
            [TypeConstraintKind.Class] = "class",
            [TypeConstraintKind.ClassNullable] = "class?",
            [TypeConstraintKind.Struct] = "struct",
            [TypeConstraintKind.Unmanaged] = "unmanaged",
            [TypeConstraintKind.New] = "new()",
            [TypeConstraintKind.Interface] = "IFace<int>",
            [TypeConstraintKind.BaseClass] = "Base<string>",
            [TypeConstraintKind.TypeName] = "Alias.@class<int>",
            [TypeConstraintKind.NotNull] = "notnull",
            [TypeConstraintKind.Default] = "default",
            [TypeConstraintKind.AllowsRefStruct] = "allows ref struct"
        };

        foreach (var (kind, expected) in cases)
        {
            var typeName = kind switch
            {
                TypeConstraintKind.Interface => "IFace<i32>",
                TypeConstraintKind.BaseClass => "Base<str>",
                TypeConstraintKind.TypeName => "Alias.class<i32>",
                _ => null
            };
            var node = new TypeConstraintNode(Span, kind, typeName);
            Assert.Equal(expected, node.Accept(emitter));
        }

        var parsed = Parse(
            """
            module ConstraintParsing ConstraintParsing block
              interface IConstraintHost
                func Apply<T> ()
                  where T : allows ref struct
                  !
            """);
        Assert.Equal(
            TypeConstraintKind.AllowsRefStruct,
            Assert.Single(
                Assert.Single(Assert.Single(parsed.Interfaces).Methods)
                .TypeParameters[0]
                .Constraints).Kind);

        var allowsConversion = new CSharpToCalorConverter(new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossy,
            ModuleName = "AllowsConstraint",
            AutoGenerateIds = true
        }).Convert(
            """
            public interface IRefConsumer
            {
                void Apply<T>(T value) where T : allows ref struct;
            }
            """);
        Assert.True(allowsConversion.Success);
        Assert.Equal(
            TypeConstraintKind.AllowsRefStruct,
            Assert.Single(
                Assert.Single(
                    Assert.Single(allowsConversion.Ast!.Interfaces).Methods)
                .TypeParameters[0]
                .Constraints).Kind);

        var defaultConversion = new CSharpToCalorConverter(new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossy,
            ModuleName = "DefaultConstraint",
            AutoGenerateIds = true
        }).Convert(
            """
            #nullable enable
            public abstract class Base
            {
                public abstract T? Echo<T>(T? value);
            }
            public sealed class Derived : Base
            {
                public override T? Echo<T>(T? value) where T : default => value;
            }
            """);
        Assert.True(defaultConversion.Success);
        var derived = Assert.Single(
            defaultConversion.Ast!.Classes.Where(type => type.Name == "Derived"));
        Assert.Equal(
            TypeConstraintKind.Default,
            Assert.Single(
                Assert.Single(derived.Methods).TypeParameters[0].Constraints).Kind);
    }

    [Fact]
    public void DefaultConstraint_IsRejectedOnEveryIllegalOwner()
    {
        string[] invalidSources =
        [
            """
            module BadFunction BadFunction block
              func Bad<T> ()
                where T : default
                !
            """,
            """
            module BadClass BadClass block
              class Bad<T>
                where T : default
            """,
            """
            module BadInterface BadInterface block
              interface IBad<T>
                where T : default
            """,
            """
            module BadMethod BadMethod block
              class Host
                func Bad<T> ()
                  where T : default
                  !
            """,
            """
            module BadInterfaceMethod BadInterfaceMethod block
              interface IHost
                func Bad<T> ()
                  where T : default
                  !
            """
        ];

        foreach (var source in invalidSources)
        {
            var diagnostics = new DiagnosticBag();
            var lexer = new Lexer(source, diagnostics);
            var parser = new Parser(lexer.TokenizeAllForParser(), diagnostics);
            _ = parser.Parse();
            Assert.Contains(
                diagnostics,
                diagnostic =>
                    diagnostic.Code == DiagnosticCode.InvalidDefaultConstraintOwner);
        }
    }

    [Fact]
    public void DefaultConstraint_MigrationPreservesOverrideAndExplicitInterfaceOwners()
    {
        var overrideResult = new CSharpToCalorConverter(new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossy,
            ModuleName = "DefaultOverride",
            AutoGenerateIds = true
        }).Convert(
            """
            #nullable enable
            public abstract class Base
            {
                public abstract T? Echo<T>(T? value);
            }
            public sealed class Derived : Base
            {
                public override T? Echo<T>(T? value) where T : default => value;
            }
            """);
        Assert.True(
            overrideResult.Success,
            string.Join("; ", overrideResult.Issues.Select(issue => issue.Message)));
        Assert.Contains(
            "where T : default",
            CompileConvertedCalor(overrideResult));

        var explicitResult = new CSharpToCalorConverter(new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossy,
            ModuleName = "DefaultExplicit",
            AutoGenerateIds = true
        }).Convert(
            """
            #nullable enable
            public interface IExplicit
            {
                T? Echo<T>(T? value);
            }
            public sealed class Explicit : IExplicit
            {
                T? IExplicit.Echo<T>(T? value) where T : default => value;
            }
            """);
        Assert.True(
            explicitResult.Success,
            string.Join("; ", explicitResult.Issues.Select(issue => issue.Message)));
        // 0.30 native syntax has no explicit-interface method form (015ee950f), so the
        // member is preserved as its original C#, constraint included.
        var explicitType = Assert.Single(
            explicitResult.Ast!.Classes.Where(type => type.Name == "Explicit"));
        Assert.Empty(explicitType.Methods);
        Assert.Contains("T? IExplicit.Echo<T>(T? value) where T : default", explicitResult.CalorSource);

        var generated = CompileConvertedCalor(explicitResult);
        Assert.Contains("IExplicit.Echo<T>", generated);
        Assert.Contains("where T : default", generated);
        Assert.DoesNotContain("private T? IExplicit.Echo<T>", generated);
        var validation = GeneratedCSharpCompiler.Validate(generated);
        Assert.True(
            validation.CompilationSuccess,
            string.Join(
                Environment.NewLine,
                validation.FormattedCompilationErrors));
    }

    [Fact]
    public void InterfaceGenericMethodConstraints_RoundTripWithImplementation()
    {
        var conversion = new CSharpToCalorConverter(new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossy,
            ModuleName = "ConstraintRelationship",
            AutoGenerateIds = true
        }).Convert(
            """
            using System.Threading;
            using System.Threading.Tasks;

            public interface IRequest
            {
                int Marker { get; }
            }

            public interface IRequest<TResponse>
            {
                TResponse Response { get; }
            }

            public interface INotification
            {
                int Marker { get; }
            }

            public interface ISender
            {
                Task Send<TRequest>(
                    TRequest request,
                    CancellationToken cancellationToken = default)
                    where TRequest : IRequest;

                Task<TResponse> Send<TRequest, TResponse>(
                    TRequest request,
                    CancellationToken cancellationToken = default)
                    where TRequest : IRequest<TResponse>;
            }

            public interface IPublisher
            {
                Task Publish<TNotification>(
                    TNotification notification,
                    CancellationToken cancellationToken = default)
                    where TNotification : INotification;
            }

            public abstract class Mediator : ISender, IPublisher
            {
                public abstract Task Send<TRequest>(
                    TRequest request,
                    CancellationToken cancellationToken = default)
                    where TRequest : IRequest;

                public abstract Task<TResponse> Send<TRequest, TResponse>(
                    TRequest request,
                    CancellationToken cancellationToken = default)
                    where TRequest : IRequest<TResponse>;

                public abstract Task Publish<TNotification>(
                    TNotification notification,
                    CancellationToken cancellationToken = default)
                    where TNotification : INotification;
            }
            """);

        Assert.True(
            conversion.Success,
            string.Join("; ", conversion.Issues.Select(issue => issue.Message)));
        // 0.30: `CancellationToken cancellationToken = default` has no native spelling
        // (a non-primitive `default`, af5d914ab), so each of these members is preserved as
        // its original C#. The constraints must still survive the round trip verbatim.
        Assert.NotNull(conversion.Ast);

        Assert.Contains("where TRequest : IRequest", conversion.CalorSource);
        Assert.Contains(
            "where TRequest : IRequest<TResponse>",
            conversion.CalorSource);
        Assert.Contains(
            "where TNotification : INotification",
            conversion.CalorSource);

        var compiled = Program.Compile(
            conversion.CalorSource!,
            null,
            new CompilationOptions
            {
                DeferGeneratedOutputValidation = true
            });
        Assert.False(
            compiled.HasErrors,
            string.Join("; ", compiled.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Contains(
            "where TRequest : IRequest",
            compiled.GeneratedCode);
        Assert.Contains(
            "where TRequest : IRequest<TResponse>",
            compiled.GeneratedCode);
        Assert.Contains(
            "where TNotification : INotification",
            compiled.GeneratedCode);
        var validation = GeneratedCSharpCompiler.Validate(compiled.GeneratedCode);
        Assert.True(
            validation.CompilationSuccess,
            string.Join(
                Environment.NewLine,
                validation.FormattedCompilationErrors));
    }

    [Fact]
    public void AliasesInBaseAndInterfaceGenericPositions_UseCentralTypeMapping()
    {
        var result = Program.Compile(
            """
            module AliasBases AliasBases block
              using Alias = External
              class Derived
                extends Alias.Base<int>
                implements Alias.IFace<string>
            """,
            null,
            new CompilationOptions { DeferGeneratedOutputValidation = true });

        Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics));
        Assert.Contains("using Alias = External;", result.GeneratedCode);
        Assert.Contains(
            "class Derived : Alias.Base<int>, Alias.IFace<string>",
            result.GeneratedCode);

        var validation = GeneratedCSharpCompiler.Validate(
            result.GeneratedCode,
            """
            namespace External
            {
                public class Base<T> { }
                public interface IFace<T> { }
            }
            """);
        Assert.True(
            validation.CompilationSuccess,
            string.Join(Environment.NewLine, validation.FormattedCompilationErrors));
    }

    [Fact]
    public void GeneratedCSharp_CompilesWithImplicitUsingsDisabled()
    {
        var result = Program.Compile(
            """
            module Standalone Standalone block
              func Echo<T> (List<T>:items) → List<T>
                !
                return items

              func EchoAsync async (int:value) → int
                !
                return value
            """,
            null,
            new CompilationOptions { DeferGeneratedOutputValidation = true });

        Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics));
        Assert.Contains("using System.Collections.Generic;", result.GeneratedCode);
        Assert.Contains("using System.Threading.Tasks;", result.GeneratedCode);

        var validation = GeneratedCSharpCompiler.Validate(
            [new GeneratedCSharpSource(result.GeneratedCode, "standalone.g.cs")],
            new GeneratedCSharpCompilationContext
            {
                IncludeImplicitGlobalUsings = false
            });
        Assert.True(
            validation.CompilationSuccess,
            string.Join(Environment.NewLine, validation.FormattedCompilationErrors));
    }

    [Fact]
    public void DeclarationNodes_ExposeOnlyEmitterAccountedFields()
    {
        var expected = new Dictionary<Type, string[]>
        {
            [typeof(UsingDirectiveNode)] =
                ["Alias", "IsGlobal", "IsStatic", "Namespace"],
            [typeof(RecordCreationNode)] =
                ["Fields", "TypeName", "TypeNameSpan"],
            [typeof(FieldAssignmentNode)] =
                ["FieldName", "Value"],
            [typeof(TypeParameterNode)] =
                ["Constraints", "Name", "Variance"],
            [typeof(TypeConstraintNode)] =
                ["Kind", "TypeName"],
            [typeof(TypePreprocessorBlockNode)] =
                ["Classes", "Condition", "Delegates", "ElseBranch", "Enums",
                    "Interfaces", "InteropBlocks", "Items", "NestedBlocks", "Usings"],
            [typeof(MemberPreprocessorBlockNode)] =
                ["Condition", "Constructors", "ElseBranch", "Events", "Fields",
                    "Indexers", "InteropBlocks", "Items", "Methods",
                    "OperatorOverloads", "Properties"]
        };

        foreach (var (type, properties) in expected)
        {
            var actual = type
                .GetProperties(
                    BindingFlags.Public
                    | BindingFlags.Instance
                    | BindingFlags.DeclaredOnly)
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(properties, actual);
        }
    }

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var parser = new Parser(lexer.TokenizeAllForParser(), diagnostics);
        var module = parser.Parse();
        Assert.False(
            diagnostics.HasErrors,
            string.Join(Environment.NewLine, diagnostics.Select(d => d.ToString())));
        return module;
    }

    private static ConversionResult ConvertWithoutPreprocessorStripping(string source)
    {
        var result = new CSharpToCalorConverter(new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossy,
            ModuleName = "Issue766Conditional",
            AutoGenerateIds = true,
            ValidateRoundTripCSharp = false
        }).Convert(source);
        Assert.True(
            result.Success,
            string.Join("; ", result.Issues.Select(issue => issue.Message))
            + Environment.NewLine
            + result.CalorSource);
        Assert.NotNull(result.Ast);
        Assert.NotNull(result.CalorSource);
        return result;
    }

    private static string CompileConvertedCalor(ConversionResult conversion)
    {
        // #1173 (fixed) and #1176 (fixed): the converter derives every §E row from the
        // compiler's own inference over the text it emits, and a property accessor now
        // has an intrinsic effect contract like every other surface-less declaration. So
        // converted code reports NO Calor0410 at all — this helper enforces effects and
        // tolerates nothing.
        //
        // It used to compile twice: once enforcing, to hold the #1173 guard, and once
        // not, because the one residual #1173 left — an allocating getter, undeclarable
        // until #1176 — is an ERROR and errors stop code generation, and these tests need
        // the generated C#. That residual is gone, so the second compile is gone with it.
        var result = Program.Compile(
            conversion.CalorSource!,
            null,
            new CompilationOptions
            {
                DeferGeneratedOutputValidation = true,
                EnforceEffects = true,
                UnknownCallPolicy = Calor.Compiler.Effects.UnknownCallPolicy.Permissive
            });

        Assert.False(
            result.HasErrors,
            string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        return result.GeneratedCode;
    }

    private static string InvokeCreatedType(Assembly assembly)
    {
        var consumer = Assert.Single(
            assembly.GetTypes().Where(type => type.Name == "Consumer"));
        return consumer.GetMethod("Create")!
            .Invoke(Activator.CreateInstance(consumer), null)!
            .GetType()
            .FullName!;
    }

    private static string InvokeStaticCreatedType(
        Assembly assembly,
        string methodName)
        => assembly.GetType("ConditionalAliasHarness")!
            .GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, null)!
            .GetType()
            .FullName!;

    private static void AssertTypePresence(
        Assembly assembly,
        IReadOnlyList<string> present,
        IReadOnlyList<string> absent)
    {
        var names = assembly.GetTypes()
            .Select(type => type.Name)
            .ToHashSet(StringComparer.Ordinal);
        Assert.All(present, name => Assert.Contains(name, names));
        Assert.All(absent, name => Assert.DoesNotContain(name, names));
    }

    private static int InvokeStaticInt(
        Assembly assembly,
        string typeName,
        string methodName)
        => Assert.IsType<int>(
            assembly.GetTypes()
                .Single(type => type.Name == typeName)
                .GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, null));

    private static void AssertConditionalSameTypes(
        Assembly assembly,
        bool topIsRecord,
        bool nestedIsRecord)
    {
        var top = assembly.GetTypes().Single(type =>
            type.Name == "Same" && type.DeclaringType == null);
        var container = assembly.GetTypes().Single(type =>
            type.Name == "Container");
        var nested = assembly.GetTypes().Single(type =>
            type.Name == "Same" && type.DeclaringType == container);

        Assert.Equal(
            topIsRecord,
            top.GetConstructor([typeof(int)]) != null);
        Assert.Equal(
            nestedIsRecord,
            nested.GetConstructor([typeof(int)]) != null);

        var topInstance = topIsRecord
            ? Activator.CreateInstance(top, [11])!
            : Activator.CreateInstance(top)!;
        var nestedInstance = nestedIsRecord
            ? Activator.CreateInstance(nested, [13])!
            : Activator.CreateInstance(nested)!;
        Assert.Equal(
            topIsRecord ? 11 : 2,
            top.GetProperty("Value")!.GetValue(topInstance));
        Assert.Equal(
            nestedIsRecord ? 13 : 4,
            nested.GetProperty("Value")!.GetValue(nestedInstance));
    }

    private static void AssertUsing(
        UsingDirectiveNode node,
        string target,
        string? alias = null,
        bool isStatic = false,
        bool isGlobal = false)
    {
        Assert.Equal(target, node.Namespace);
        Assert.Equal(alias, node.Alias);
        Assert.Equal(isStatic, node.IsStatic);
        Assert.Equal(isGlobal, node.IsGlobal);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = text.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }
        return count;
    }

    private static Assembly CompileAssembly(
        string source,
        IReadOnlyList<string>? preprocessorSymbols = null)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(
                LanguageVersion.Latest,
                preprocessorSymbols: preprocessorSymbols ?? []));
        var compilation = CSharpCompilation.Create(
            $"Issue766_{Guid.NewGuid():N}",
            [syntaxTree],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(
            emit.Success,
            string.Join(
                Environment.NewLine,
                emit.Diagnostics
                    .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                    .Select(d => d.ToString())));
        return Assembly.Load(stream.ToArray());
    }
}
