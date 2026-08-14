using System.Reflection;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public class Issue766DeclarationModuleSemanticsTests
{
    private static readonly TextSpan Span = TextSpan.Empty;

    [Fact]
    public void UsingDirectives_PreserveCompleteObjectsAndDedupeBySemanticTuple()
    {
        var module = Parse(
            """
            §M{m001:UsingCases}
              §U{System.Text}
              §U{System.Text}
              §U{Alias:System.Collections.Generic}
              §U{static:System.Math}
              §U{global:System.Threading}
              §U{global:Tasks:System.Threading.Tasks}
              §U{global:static:System.Math}
              §F{f001:Value:pub} () -> i32
                §R INT:1
            """);

        Assert.Collection(
            module.Usings,
            u => AssertUsing(u, "System.Text"),
            u => AssertUsing(u, "System.Text"),
            u => AssertUsing(u, "System.Collections.Generic", alias: "Alias"),
            u => AssertUsing(u, "System.Math", isStatic: true),
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
                isGlobal: true));

        var generated = new CSharpEmitter().Emit(module);

        Assert.Equal(1, CountOccurrences(generated, "using System.Text;"));
        Assert.Contains("using Alias = System.Collections.Generic;", generated);
        Assert.Contains("using static System.Math;", generated);
        Assert.Contains("global using System.Threading;", generated);
        Assert.Contains("global using Tasks = System.Threading.Tasks;", generated);
        Assert.Contains("global using static System.Math;", generated);
        Assert.True(
            generated.IndexOf("global using", StringComparison.Ordinal)
            < generated.IndexOf("using System;", StringComparison.Ordinal));
        Assert.True(GeneratedCSharpCompiler.Validate(generated).CompilationSuccess);
        Assert.Equal(
            "using Pair = (int Left, int Right);",
            new UsingDirectiveNode(
                Span,
                "(i32 Left, i32 Right)",
                alias: "Pair").Accept(new CSharpEmitter()));

        var conditional = new CSharpEmitter().Emit(Parse(
            """
            §M{m002:ConditionalUsing}
              §PP{FEATURE}
                §U{global:System.Text}
                §U{Buffers:System.Buffers}
            """));
        Assert.True(
            conditional.IndexOf("global using System.Text;", StringComparison.Ordinal)
            < conditional.IndexOf("namespace ConditionalUsing", StringComparison.Ordinal));
        Assert.True(
            conditional.IndexOf("using Buffers = System.Buffers;", StringComparison.Ordinal)
            < conditional.IndexOf("namespace ConditionalUsing", StringComparison.Ordinal));
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
        Assert.Contains("§U{global:System}", result.CalorSource);
        Assert.Contains("§U{global:Text:System.Text}", result.CalorSource);
        Assert.Contains("§U{global:static:System.Math}", result.CalorSource);
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
            §M{m001:NestedElseTypes}
              §PP{OUTER}
                §CL{c001:OuterSelected:pub}
                  §MT{m001:Value:pub:stat} () -> i32
                    §R INT:1
              §PPE
                §CL{c002:ElseDirect:pub}
                  §FLD{i32:Value:pub}
                §PP{INNER}
                  §CL{c003:InnerSelected:pub}
                    §MT{m003:Value:pub:stat} () -> i32
                      §R INT:2
                §PPE
                  §CL{c004:InnerFallback:pub}
                    §MT{m004:Value:pub:stat} () -> i32
                      §R INT:3
                §/PP{INNER}
                §IFACE{i005:IElseTail}
              §/PP{OUTER}
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
    public void ConditionalUnsupportedRecord_RemainsGuardedAndRunsPerSymbol()
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
        Assert.Equal(2, typeBlock.InteropBlocks.Count);
        var recordInterop = Assert.Single(
            typeBlock.InteropBlocks.Where(interop =>
                interop.CSharpCode.Contains("record Selected", StringComparison.Ordinal)));
        Assert.Contains("record Selected", recordInterop.CSharpCode);
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
        Assert.Collection(
            block.Items,
            item => Assert.Equal("Before", Assert.IsType<ClassFieldNode>(item).Name),
            item => Assert.IsType<CSharpInteropBlockNode>(item),
            item => Assert.Equal("After", Assert.IsType<ClassFieldNode>(item).Name));
        Assert.Contains(
            "record Inner",
            Assert.Single(block.InteropBlocks).CSharpCode);
        Assert.Collection(
            block.ElseBranch!.Items,
            item => Assert.Equal("BeforeElse", Assert.IsType<ClassFieldNode>(item).Name),
            item => Assert.IsType<CSharpInteropBlockNode>(item),
            item => Assert.Equal("AfterElse", Assert.IsType<ClassFieldNode>(item).Name));
        Assert.Contains(
            "struct Inner",
            Assert.Single(block.ElseBranch!.InteropBlocks).CSharpCode);

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
            §M{m001:ElifNestedTypes}
              §PP{FIRST}
                §CL{c001:FirstSelected:pub}
                  §FLD{i32:Value:pub}
              §PPE
                §PP{SECOND}
                  §IFACE{i002:ISecondBefore}
                    §PROP{p002:Value:i32:pub:get}
                  §PP{DEEP}
                    §CL{c003:DeepSelected:pub}
                      §FLD{i32:Value:pub}
                  §/PP{DEEP}
                  §CL{c004:SecondAfter:pub}
                    §FLD{i32:Value:pub}
                §PPE
                  §CL{c005:FinalFallback:pub}
                    §FLD{i32:Value:pub}
                §/PP{SECOND}
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

    [Fact]
    public void ReorderedSameTypeRecordFields_PreserveRuntimeValues()
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
            "§D{Pair} §FL{Right} \"r\" §FL{Left} \"l\"",
            creation.Accept(new CalorEmitter()));
    }

    [Fact]
    public void InterfaceVarianceAndConstraints_ArePreservedMappedAndCompilable()
    {
        var module = Parse(
            """
            §M{m001:Variance}
              §IFACE{i001:IVariant}<out TOut, in TIn, TUnmanaged, TNotNull, TDerived>
                §WHERE TOut : class?
                §WHERE TIn : class, new()
                §WHERE TUnmanaged : unmanaged
                §WHERE TNotNull : notnull, System.IComparable<TNotNull>
                §WHERE TDerived : ExternalBase, IMarker, new()
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
            §M{m001:InvalidVariance}
              §CL{c001:Box:pub}<out T>
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
            §M{m001:ConstraintParsing}
              §IFACE{i001:IConstraintHost}
                §MT{m001:Apply}<T>
                  §WHERE T : allows ref struct
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
            §M{m001:BadFunction}
              §F{f001:Bad:pub}<T>
                §WHERE T : default
            """,
            """
            §M{m001:BadClass}
              §CL{c001:Bad:pub}<T>
                §WHERE T : default
            """,
            """
            §M{m001:BadInterface}
              §IFACE{i001:IBad}<T>
                §WHERE T : default
            """,
            """
            §M{m001:BadMethod}
              §CL{c001:Host:pub}
                §MT{m001:Bad:pub}<T>
                  §WHERE T : default
            """,
            """
            §M{m001:BadInterfaceMethod}
              §IFACE{i001:IHost}
                §MT{m001:Bad}<T>
                  §WHERE T : default
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
        var explicitMethod = Assert.Single(
            Assert.Single(
                explicitResult.Ast!.Classes.Where(type => type.Name == "Explicit"))
            .Methods);
        Assert.Equal("IExplicit.Echo", explicitMethod.Name);
        Assert.Equal(
            TypeConstraintKind.Default,
            Assert.Single(explicitMethod.TypeParameters[0].Constraints).Kind);

        var generated = CompileConvertedCalor(explicitResult);
        Assert.Contains("IExplicit.Echo<T>", generated);
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
            AutoGenerateIds = true,
            StripPreprocessor = false
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
        var sender = Assert.Single(
            conversion.Ast!.Interfaces.Where(type => type.Name == "ISender"));
        Assert.Collection(
            sender.Methods,
            method => Assert.Equal(
                "IRequest",
                Assert.Single(method.TypeParameters[0].Constraints).TypeName),
            method => Assert.Equal(
                "IRequest<TResponse>",
                Assert.Single(method.TypeParameters[0].Constraints).TypeName));
        var publisher = Assert.Single(
            conversion.Ast.Interfaces.Where(type => type.Name == "IPublisher"));
        Assert.Equal(
            "INotification",
            Assert.Single(
                Assert.Single(publisher.Methods)
                    .TypeParameters[0]
                    .Constraints).TypeName);

        Assert.Contains("§WHERE TRequest : IRequest", conversion.CalorSource);
        Assert.Contains(
            "§WHERE TRequest : IRequest<TResponse>",
            conversion.CalorSource);
        Assert.Contains(
            "§WHERE TNotification : INotification",
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
            §M{m001:AliasBases}
              §U{Alias:External}
              §CL{c001:Derived:pub}
                §EXT{Alias.Base<i32>}
                §IMPL{Alias.IFace<str>}
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
    public void QualifiedNames_SanitizeKeywordsAcrossNamespacesCallsArgumentsAndTypes()
    {
        var result = Program.Compile(
            """
            §M{m001:namespace.123bad.class}
              §F{f001:event:pub} (i32:class) -> i32
                §R class
            """);

        Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics));
        Assert.Contains("namespace @namespace._123bad.@class", result.GeneratedCode);
        Assert.Contains("int @event(int @class)", result.GeneratedCode);
        Assert.Contains("return @class;", result.GeneratedCode);

        var call = new CallExpressionNode(
            Span,
            "namespace.class",
            [new IntLiteralNode(Span, 1)],
            ["event"]);
        Assert.Equal(
            "@namespace.@class(@event: 1)",
            call.Accept(new CSharpEmitter()));
        Assert.Equal(
            "Factory<int>.@class()",
            new CallExpressionNode(
                Span,
                "Factory<int>.class",
                []).Accept(new CSharpEmitter()));

        var type = new GenericTypeNode(
            Span,
            "namespace.class",
            ["str"]);
        Assert.Equal(
            "@namespace.@class<string>",
            type.Accept(new CSharpEmitter()));
        Assert.Equal(
            "@var",
            new GenericTypeNode(Span, "var", []).Accept(new CSharpEmitter()));
    }

    [Fact]
    public void ReusingEmitterAcrossModules_IsByteIdenticalToFreshEmitter()
    {
        var indexedModule = Parse(
            """
            §M{m001:First}
              §ITYPE{it001:Leaky:i32[]:n}
            """);
        var unrelatedModule = Parse(
            """
            §M{m002:Second}
              §F{f002:Echo:pub} (Leaky:value) -> Leaky
                §R value
            """);

        var reused = new CSharpEmitter();
        _ = reused.Emit(indexedModule);
        var reusedOutput = reused.Emit(unrelatedModule);
        var freshOutput = new CSharpEmitter().Emit(unrelatedModule);

        Assert.Equal(freshOutput, reusedOutput);
        Assert.Contains("Leaky value", reusedOutput);
        Assert.DoesNotContain("int[] value", reusedOutput);
    }

    [Fact]
    public void GeneratedCSharp_CompilesWithImplicitUsingsDisabled()
    {
        var result = Program.Compile(
            """
            §M{m001:Standalone}
              §F{f001:Echo:pub}<T> (List<T>:items) -> List<T>
                §R items
              §AF{f002:EchoAsync:pub} (i32:value) -> i32
                §R value
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
    public void BuiltInShortTypeMatrix_CompilesStandaloneWithoutImplicitUsings()
    {
        string[] types =
        [
            "i8", "i16", "i32", "i64",
            "u8", "u16", "u32", "u64",
            "f32", "f64", "dec", "bool", "str", "char", "any",
            "List<i32>", "Dict<str, i32>", "Set<i32>",
            "Seq<i32>", "IList<i32>", "IDict<str, i32>",
            "ICollection<i32>", "ISet<i32>",
            "ReadList<i32>", "ReadCollection<i32>",
            "ReadDict<str, i32>", "ReadSet<i32>",
            "Task", "Task<i32>", "ValueTask", "ValueTask<i32>",
            "datetime", "datetimeoffset", "timespan", "date", "time", "guid",
            "Span<i32>", "ReadOnlySpan<i32>", "Memory<i32>", "ReadOnlyMemory<i32>",
            "StringBuilder", "void"
        ];

        foreach (var type in types)
        {
            var signature = type == "void"
                ? "() -> void"
                : $"({type}:value) -> {type}";
            var body = type == "void" ? "" : "    §R value";
            var result = Program.Compile(
                $$"""
                  §M{m001:BuiltInMatrix}
                    §F{f001:Echo:pub} {{signature}}
                  {{body}}
                  """,
                null,
                new CompilationOptions
                {
                    DeferGeneratedOutputValidation = true
                });
            Assert.False(
                result.HasErrors,
                $"{type}: {string.Join("; ", result.Diagnostics.Select(d => d.Message))}");

            var validation = GeneratedCSharpCompiler.Validate(
                [new GeneratedCSharpSource(result.GeneratedCode, $"{type}.g.cs")],
                new GeneratedCSharpCompilationContext
                {
                    IncludeImplicitGlobalUsings = false
                });
            Assert.True(
                validation.CompilationSuccess,
                $"{type}:{Environment.NewLine}"
                + string.Join(
                    Environment.NewLine,
                    validation.FormattedCompilationErrors));

            if (type == "StringBuilder")
                Assert.Contains("using System.Text;", result.GeneratedCode);
        }
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
            StripPreprocessor = false,
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
        var result = Program.Compile(
            conversion.CalorSource!,
            null,
            new CompilationOptions
            {
                DeferGeneratedOutputValidation = true,
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
