using System.Reflection;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using CalorBinder = Calor.Compiler.Binding.Binder;
using ParameterModifier = Calor.Compiler.Ast.ParameterModifier;

namespace Calor.Compiler.Tests;

public class StaticModuleDeclarationTests
{
    public static IEnumerable<object[]> ActiveStaticModuleInputs() =>
        ActiveSyntaxInputs.OriginalPaths
            .Where(path => path.StartsWith("bench/phase0-agent-native/fixtures/d-s1.5/", StringComparison.Ordinal)
                || path is "tests/TestData/Benchmarks/DesignPatterns/TemplateMethod.calr"
                    or "tests/TestData/Benchmarks/DesignPatterns/Visitor.calr")
            .Order(StringComparer.Ordinal)
            .Select(path => new object[] { path });

    [Theory]
    [MemberData(nameof(ActiveStaticModuleInputs))]
    public void RegisteredActiveMirror_PreservesExactReleaseCSharp(string original)
    {
        var root = CliTestHarness.FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, original));
        var originalModule = ParseRejectedHistoricalStatic(source);
        var activeModule = Parse(File.ReadAllText(ActiveSyntaxInputs.ForCurrentCompiler(root, original)));
        Assert.Equal(new CSharpEmitter(EmitContractMode.Release).Emit(originalModule),
            new CSharpEmitter(EmitContractMode.Release).Emit(activeModule));
    }

    [Theory]
    [InlineData("tests/TestData/InteropScope/R0/fixtures/F4-ITER-03.calr.txt")]
    [InlineData("tests/TestData/InteropScope/R0/generated/F5-ARRAY-03.mcp-default.calr.txt")]
    public void CalorTextMirror_PreservesExactReleaseCSharp(string original)
    {
        var root = CliTestHarness.FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, original))
            .Replace("(len one)", "one.Length", StringComparison.Ordinal)
            .Replace("(len two)", "two.Length", StringComparison.Ordinal);
        var historical = ParseRejectedHistoricalStatic(source);
        var active = Parse(File.ReadAllText(ActiveSyntaxInputs.ForCurrentTypedInput(
            root, original, ActiveSyntaxInputs.InputKind.CalorText)));
        Assert.Equal(new CSharpEmitter(EmitContractMode.Release).Emit(historical),
            new CSharpEmitter(EmitContractMode.Release).Emit(active));
    }

    [Fact]
    public void StaticClasses_BecomeNamedModules_WithCallerNamesAndStateIntact()
    {
        const string source = """
            namespace Library;
            [System.Obsolete("kept")]
            public static partial class Utilities
            {
                public const int Seed = 4;
                public static int Count = Seed;
                public static int Value { get; set; } = 3;
                static Utilities() { Count = Count + 2; }
                [System.Obsolete("method")]
                public static int Add(int x = 1) { return x + Count + Value; }
                public static class Nested
                {
                    public static int Twice(int x) { return x * 2; }
                }
                public class Item { public int Get() { return Count; } }
            }
            """;

        var (result, module, generated) = RoundTrip(source);
        var declaration = Assert.Single(module.Classes);
        Assert.True(declaration.IsModule);
        Assert.True(declaration.IsPartial);
        Assert.Equal("Library", declaration.NamespaceIdentity);
        Assert.True(declaration.NestedClasses.Single(type => type.Name == "Nested").IsModule);
        Assert.False(declaration.NestedClasses.Single(type => type.Name == "Item").IsModule);
        Assert.Contains("§F{", result.CalorSource);
        Assert.DoesNotContain(":stat}", result.CalorSource!.Split('\n')
            .First(line => line.Contains(":Utilities", StringComparison.Ordinal)));
        Assert.DoesNotContain("§CL", result.CalorSource.Split('\n')
            .First(line => line.Contains(":Nested", StringComparison.Ordinal)));
        Assert.Contains("public static partial class Utilities", generated);
        Assert.Contains("Obsolete(\"kept\")]", generated);
        Assert.Contains("Obsolete(\"method\")]", generated);

        var assembly = Compile(generated);
        var utilities = assembly.GetType("Library.Utilities")!;
        Assert.Equal(1, utilities.GetMethod("Add")!.GetParameters()[0].DefaultValue);
        Assert.Equal(4, utilities.GetField("Seed")!.GetRawConstantValue());
        Assert.Equal(12, utilities.GetMethod("Add")!.Invoke(null, [3]));
        var nested = assembly.GetType("Library.Utilities+Nested")!;
        Assert.Equal(10, nested.GetMethod("Twice")!.Invoke(null, [5]));
        var item = assembly.GetType("Library.Utilities+Item")!;
        Assert.Equal(6, item.GetMethod("Get")!.Invoke(Activator.CreateInstance(item), null));
    }

    [Fact]
    public void Extensions_PreserveAttributesGenericConstraintsRefAndDefaultParameters()
    {
        const string source = """
            namespace Extensions;
            public static class TextExtensions
            {
                [System.Obsolete("extension")]
                public static T Identity<T>(this T value) where T : class { return value; }
                public static int Bump(this ref int value, int amount = 2)
                {
                    value = value + amount;
                    return value;
                }
            }
            public class Caller
            {
                public static int Run()
                {
                    int value = 3;
                    return value.Bump();
                }
            }
            """;

        var (_, module, generated) = RoundTrip(source);
        var extensions = module.Classes.Single(type => type.Name == "TextExtensions");
        Assert.True(extensions.IsModule);
        var identity = extensions.Methods.Single(method => method.Name == "Identity");
        Assert.True(identity.Parameters[0].Modifier.HasFlag(ParameterModifier.This));
        Assert.NotEmpty(identity.TypeParameters[0].Constraints);
        Assert.Contains(extensions.InteropBlocks, block => block.CSharpCode.Contains("this ref int"));
        Assert.Contains("this ref int", generated);
        Assert.Contains("Obsolete(\"extension\")]", generated);
        var extensionType = Compile(generated).GetType("Extensions.TextExtensions")!;
        Assert.Equal(2, extensionType.GetMethod("Bump")!.GetParameters()[1].DefaultValue);
        Assert.True(extensionType.GetMethod("Bump")!.GetParameters()[0].ParameterType.IsByRef);
        Assert.Equal(5, Compile(generated).GetType("Extensions.Caller")!
            .GetMethod("Run")!.Invoke(null, null));
        AssertSignaturesEqual(source, generated, "Extensions.TextExtensions");
    }

    [Fact]
    public void NestedGenericModules_KeepContainingTypesAndSignatures()
    {
        const string source = """
            namespace Library;
            public class Outer<T> where T : class
            {
                public static class Helpers<U> where U : struct
                {
                    public static T Pick(T value, U ignored) { return value; }
                }
            }
            """;
        var (_, module, generated) = RoundTrip(source);
        var outer = Assert.Single(module.Classes);
        Assert.False(outer.IsModule);
        Assert.True(Assert.Single(outer.NestedClasses).IsModule);
        AssertSignaturesEqual(source, generated, "Library.Outer`1+Helpers`1");
        var nested = Compile(generated).GetType("Library.Outer`1+Helpers`1")!
            .MakeGenericType(typeof(string), typeof(int));
        Assert.Equal("kept", nested.GetMethod("Pick")!.Invoke(null, ["kept", 1]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("static ")]
    public void NestedTypes_CanReadWriteAndCallEnclosingPrivateStaticMembers(string staticModifier)
    {
        var source = $$"""
            namespace Access;
            public {{staticModifier}}class Outer
            {
                private const int Seed = 3;
                private static int Value = 2;
                private static int Stored { get; set; } = 1;
                private static int Twice(int value) { return value * 2; }
                public class Inner
                {
                    public int Run()
                    {
                        Value = Value + Seed;
                        Stored = Stored + 1;
                        return Twice(Value) + Stored;
                    }
                    public class Deep
                    {
                        public static int Read() { return Twice(Seed); }
                    }
                }
            }
            """;
        var original = Compile(source);
        var (conversion, module, generated) = RoundTrip(source);
        var outer = Assert.Single(module.Classes);
        Assert.Equal("Outer", outer.Name);
        Assert.Equal(staticModifier.Length > 0, outer.IsModule);
        Assert.Equal(new[] { "Seed", "Value" }, outer.Fields.Select(field => field.Name));
        Assert.Equal("Stored", Assert.Single(outer.Properties).Name);
        Assert.Equal("Twice", Assert.Single(outer.Methods).Name);
        var innerDeclaration = Assert.Single(outer.NestedClasses);
        Assert.Equal("Inner", innerDeclaration.Name);
        Assert.Equal("Run", Assert.Single(innerDeclaration.Methods).Name);
        var deepDeclaration = Assert.Single(innerDeclaration.NestedClasses);
        Assert.Equal("Deep", deepDeclaration.Name);
        Assert.Equal("Read", Assert.Single(deepDeclaration.Methods).Name);
        Assert.Empty(outer.InteropBlocks);
        Assert.Empty(innerDeclaration.InteropBlocks);
        Assert.Empty(deepDeclaration.InteropBlocks);
        Assert.DoesNotContain("§CSHARP", conversion.CalorSource);
        var converted = Compile(generated);
        foreach (var assembly in new[] { original, converted })
        {
            var inner = assembly.GetType("Access.Outer+Inner")!;
            Assert.Equal(12, inner.GetMethod("Run")!.Invoke(Activator.CreateInstance(inner), null));
            Assert.Equal(6, assembly.GetType("Access.Outer+Inner+Deep")!.GetMethod("Read")!.Invoke(null, null));
        }
    }

    [Theory]
    [InlineData("unrelated", "CS0122")]
    [InlineData("outer-to-inner", "CS0122")]
    [InlineData("sibling", "CS0122")]
    [InlineData("wrong-receiver", "CS0117")]
    public void PrivateAccess_DoesNotLeakToUnrelatedOrWrongReceiverTypes(string shape, string expectedCSharpCode)
    {
        var declarations = shape switch
        {
            "unrelated" => """
                  §M{Outer:pub}
                    §FLD{i32:Value:priv:stat} = 3
                  §M{Caller:pub}
                    §F{Read:pub} () -> i32
                      §R Outer.Value
                """,
            "outer-to-inner" => """
                  §M{Outer:pub}
                    §CL{Inner:pub}
                      §FLD{i32:Value:priv:stat} = 3
                    §F{Read:pub} () -> i32
                      §R Inner.Value
                """,
            "sibling" => """
                  §M{Outer:pub}
                    §CL{Sibling:pub}
                      §MT{Secret:priv:stat} () -> i32
                        §R 3
                    §CL{Caller:pub}
                      §MT{Read:pub:stat} () -> i32
                        §R §C{Sibling.Secret} §/C
                """,
            _ => """
                  §M{Outer:pub}
                    §FLD{i32:Value:priv:stat} = 3
                    §CL{Inner:pub}
                      §MT{Read:pub:stat} () -> i32
                        §R Inner.Value
                """
        };
        var source = "§M{Access}\n" + declarations;
        var module = Parse(source);
        var generated = new CSharpEmitter().Emit(module);
        Assert.Contains(Compilation(generated).GetDiagnostics(), diagnostic => diagnostic.Id == expectedCSharpCode);
        Assert.True(Program.Compile(source, "private-access.calr").HasErrors);
    }

    [Theory]
    [InlineData("private int Secret() { return 1; }", "", "Secret()", "CS0120")]
    [InlineData("private static int Secret(int value) { return value; }",
        "private static int Secret(string value) { return 0; }", "Secret(1)", "CS1503")]
    public void EnclosingLookup_RejectsMissingInstanceAndShadowedOverloads(
        string outerMethod, string innerMethod, string call, string expectedCode)
    {
        var source = $$"""
            public class Outer
            {
                {{outerMethod}}
                public class Inner
                {
                    {{innerMethod}}
                    public static int Run() { return {{call}}; }
                }
            }
            """;
        Assert.Contains(Compilation(source).GetDiagnostics(), diagnostic => diagnostic.Id == expectedCode);
        var conversion = new CSharpToCalorConverter(new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossy,
            ValidateRoundTripCSharp = false
        }).Convert(source);
        Assert.NotNull(conversion.CalorSource);
        Assert.True(Program.Compile(conversion.CalorSource, "enclosing-call.calr").HasErrors);
    }

    [Fact]
    public void NestedType_CanUsePrivateInstanceMembersThroughAnOuterReceiver()
    {
        const string source = """
            public class Outer
            {
                private int Value = 7;
                private int Read() { return Value; }
                public class Inner
                {
                    public static int Run(Outer owner) { return owner.Read() + owner.Read(); }
                }
            }
            """;
        var original = Compile(source);
        var (_, _, generated) = RoundTrip(source);
        var converted = Compile(generated);
        foreach (var assembly in new[] { original, converted })
            Assert.Equal(14, assembly.GetType("Outer+Inner")!.GetMethod("Run")!
                .Invoke(null, [Activator.CreateInstance(assembly.GetType("Outer")!)]));
    }

    [Fact]
    public async Task ModuleFunctions_AsyncAndLocalFunctionsRoundTrip()
    {
        const string source = """
            namespace Library;
            public static class AsyncHelpers
            {
                public static async System.Threading.Tasks.Task<int> Run(int value)
                {
                    int Double(int x) { return x * 2; }
                    return await System.Threading.Tasks.Task.FromResult(Double(value));
                }
            }
            """;
        var (result, module, generated) = RoundTrip(source);
        Assert.Contains("§AF{", result.CalorSource);
        Assert.True(Assert.Single(Assert.Single(module.Classes).Methods).IsAsync);
        var task = (System.Threading.Tasks.Task<int>)Compile(generated)
            .GetType("Library.AsyncHelpers")!.GetMethod("Run")!.Invoke(null, [3])!;
        Assert.Equal(6, await task);
    }

    [Fact]
    public void ModuleMembers_AreImplicitlyStatic_AndEmptySiblingsRemainSiblings()
    {
        var module = Parse("""
            §M{Library}
              §M{Empty:pub}
              §M{Helpers:pub}
                §FLD{i32:Count:pub} = 3
                §PROP{Value:i32:pub} = 4
                §F{Get:pub} () -> i32
                  §R (+ Count Value)
              §CL{Sibling:pub}
            """);
        Assert.Equal(3, module.Classes.Count);
        var helpers = module.Classes[1];
        Assert.True(helpers.IsModule);
        Assert.True(Assert.Single(helpers.Fields).IsStatic);
        Assert.True(Assert.Single(helpers.Properties).IsStatic);
        Assert.True(Assert.Single(helpers.Methods).IsStatic);
        var assembly = Compile(new CSharpEmitter().Emit(module));
        Assert.Equal(7, assembly.GetType("Library.Helpers")!.GetMethod("Get")!.Invoke(null, null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ModuleClassCollision_IsDiagnosedInEitherOrder(bool moduleFirst)
    {
        var declarations = moduleFirst
            ? "  §M{Helpers:pub}\n  §CL{Helpers:pub}"
            : "  §CL{Helpers:pub}\n  §M{Helpers:pub}";
        var module = Parse("§M{Library}\n" + declarations);
        Assert.Contains(Bind(module), diagnostic => diagnostic.Code == DiagnosticCode.DuplicateDefinition);
    }

    [Fact]
    public void CollisionCheck_UsesLexicalNamespaceContainingTypeAndGenericArity()
    {
        var module = Parse("""
            §M{File}
              §NS{First}
                §M{Helpers:pub}
                §CL{Helpers<T>:pub}
                §CL{Outer:pub}
                  §M{Helpers:pub}
              §NS{Second}
                §CL{Helpers:pub}
            """);
        Assert.DoesNotContain(Bind(module), diagnostic => diagnostic.Code == DiagnosticCode.DuplicateDefinition);
        Compile(new CSharpEmitter().Emit(module));
    }

    [Fact]
    public void NestedModuleClassCollision_IsDiagnosed()
    {
        var module = Parse("""
            §M{Library}
              §CL{Outer:pub}
                §M{Helpers:pub}
                §CL{Helpers:pub}
            """);
        Assert.Contains(Bind(module), diagnostic => diagnostic.Code == DiagnosticCode.DuplicateDefinition);
    }

    [Fact]
    public void LegacyFunctionContainerCollision_IsDiagnosed()
    {
        var module = Parse("""
            §M{Library}
              §F{Get:pub} () -> i32
                §R 1
              §CL{LibraryModule:pub}
            """);
        Assert.Contains(Bind(module), diagnostic => diagnostic.Code == DiagnosticCode.DuplicateDefinition);
    }

    [Theory]
    [InlineData("  §M{Helpers<T>:pub}\n    §F{Run:pub} (T:value:this) -> T\n      §R value")]
    [InlineData("  §CL{Outer:pub}\n    §M{Helpers:pub}\n      §F{Run:pub} (i32:value:this) -> i32\n        §R value")]
    [InlineData("  §M{Helpers:pub}\n    §F{Run:pub} (i32:first, i32:second:this) -> i32\n      §R first")]
    public void InvalidExtensionContainersOrReceiverPositions_AreDiagnosed(string declarations)
    {
        var module = Parse("§M{Library}\n" + declarations);
        Assert.Contains(Bind(module), diagnostic =>
            diagnostic.Code == DiagnosticCode.InvalidReference && diagnostic.Message.Contains("Extension"));
        Assert.True(Program.Compile("§M{Library}\n" + declarations, "invalid-extension.calr").HasErrors);
    }

    [Theory]
    [InlineData("  §M{Helpers:pub:abs}")]
    [InlineData("  §M{Helpers:pub:seal}")]
    [InlineData("  §M{Helpers:pub}\n    §CTOR{pub} ()")]
    public void InvalidModuleInstanceShapes_StopCompilation(string declaration)
    {
        var source = "§M{Library}\n" + declaration;
        Parse(source);
        var compilation = Program.Compile(source, "invalid-module.calr");
        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.InvalidReference);
    }

    [Fact]
    public void ConditionalModulesAndFunctions_RetainAllBranches()
    {
        const string source = """
            namespace Library;
            #if FEATURE
            public static class Helpers
            {
                #if INNER
                public static int Value() { return 1; }
                #else
                public static int Value() { return 2; }
                #endif
            }
            #else
            public static class Helpers
            {
                public static int Value() { return 3; }
            }
            #endif
            """;
        var result = Convert(source, preservePreprocessor: true);
        var parsed = Parse(result.CalorSource!);
        var generated = new CSharpEmitter().Emit(parsed);
        Assert.DoesNotContain("§CL{", result.CalorSource);
        foreach (var (symbols, expected) in new[]
                 {
                     (Array.Empty<string>(), 3),
                     (new[] { "FEATURE" }, 2),
                     (new[] { "FEATURE", "INNER" }, 1)
                 })
        {
            var assembly = Compile(generated, symbols);
            var helpers = assembly.GetType("Library.Helpers");
            Assert.True(helpers != null, generated + "\n" + result.CalorSource);
            Assert.Equal(expected, helpers.GetMethod("Value")!.Invoke(null, null));
            Assert.Equal(expected, Compile(source, symbols).GetType("Library.Helpers")!
                .GetMethod("Value")!.Invoke(null, null));
            AssertSignaturesEqual(source, generated, "Library.Helpers", symbols);
        }
    }

    [Theory]
    [InlineData("st")]
    [InlineData("stat")]
    [InlineData("static")]
    [InlineData("stat,partial")]
    [InlineData("partial,static")]
    [InlineData("pub,st")]
    public void LegacyStaticClasses_AreRejectedWithNamedModuleReplacement(string modifiers)
    {
        var source = $$"""
            §M{Library}
              §CL{c1:Helpers:{{modifiers}}}
                §MT{Get:pub:stat} () -> i32
                  §R 1
            """;
        var result = Program.Compile(source, "legacy-static.calr");
        Assert.Contains(result.Diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.InvalidModifier
            && diagnostic.Message.Contains("§M{c1:Helpers:", StringComparison.Ordinal));
        Assert.True(result.HasErrors);
    }

    [Fact]
    public void UnsafeModuleDeclaration_IsExplicitInterop_NotAnAlteredSignature()
    {
        const string source = """
            namespace Library;
            public unsafe static class Pointers
            {
                public static int* Identity(int* value) { return value; }
            }
            """;
        var (result, module, generated) = RoundTrip(source);
        Assert.Empty(module.Classes);
        Assert.Contains(result.Losses, loss => loss.Feature == "unsafe");
        Assert.Contains(module.InteropBlocks, block => block.CSharpCode.Contains("unsafe static class Pointers"));
        AssertSignaturesEqual(source, generated, "Library.Pointers");
    }

    [Theory]
    [InlineData("")]
    [InlineData("static ")]
    public void NestedUnsafeType_KeepsItsUnsafeContextInsideTheModule(string staticModifier)
    {
        var source = $$"""
            namespace Library;
            public static class Helpers
            {
                public unsafe {{staticModifier}}class Pointers
                {
                    public static int* Identity(int* value) { return value; }
                }
                public static int Value() { return 42; }
            }
            """;
        var (result, module, generated) = RoundTrip(source);
        var helpers = Assert.Single(module.Classes);
        Assert.True(helpers.IsModule);
        Assert.Empty(helpers.NestedClasses);
        Assert.Contains(result.Losses, loss =>
            loss.Kind == ConversionLossKind.InteropPreserved && loss.Feature == "class");
        Assert.Contains(helpers.InteropBlocks, block => block.CSharpCode.Contains($"unsafe {staticModifier}class Pointers"));
        AssertSignaturesEqual(source, generated, "Library.Helpers+Pointers");
        Assert.Equal(42, Compile(generated).GetType("Library.Helpers")!.GetMethod("Value")!.Invoke(null, null));
    }

    [Theory]
    [InlineData(";")]
    [InlineData("{ add { } remove { } }")]
    public void StaticEvents_AreExplicitInteropWithoutLosingAttributesOrStaticness(string body)
    {
        var source = $$"""
            namespace Library;
            public static class Helpers
            {
                [System.Obsolete("kept")]
                public static event System.Action Changed{{body}}
                public static int Value() { return 42; }
            }
            """;
        var (result, module, generated) = RoundTrip(source);
        var helpers = Assert.Single(module.Classes);
        Assert.True(helpers.IsModule);
        Assert.Empty(helpers.Events);
        Assert.Contains(result.Losses, loss => loss.Kind == ConversionLossKind.InteropPreserved
            && loss.Feature == (body == ";" ? "eventfield" : "event"));
        Assert.Contains(helpers.InteropBlocks, block => block.CSharpCode.Contains("static event"));
        var @event = Assert.IsAssignableFrom<IEventSymbol>(
            Assert.Single(Compilation(generated).GetTypeByMetadataName("Library.Helpers")!.GetMembers("Changed")));
        Assert.True(@event.IsStatic);
        Assert.Equal(Accessibility.Public, @event.DeclaredAccessibility);
        Assert.Contains(@event.GetAttributes(), attribute => attribute.AttributeClass?.Name == "ObsoleteAttribute");
        Assert.Equal(42, Compile(generated).GetType("Library.Helpers")!.GetMethod("Value")!.Invoke(null, null));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TypeParameterAttributes_PreserveTheOwningDeclaration(bool onType)
    {
        const string attribute = "[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods)]";
        var source = onType
            ? $"namespace Library; public static class Helpers<{attribute} T> {{ public static T Identity(T value) {{ return value; }} }}"
            : $"namespace Library; public static class Helpers {{ public static T Identity<{attribute} T>(T value) {{ return value; }} }}";
        var (result, module, generated) = RoundTrip(source);
        Assert.Contains(result.Losses, loss =>
            loss.Kind == ConversionLossKind.InteropPreserved && loss.Feature == (onType ? "attributes" : "method"));
        Assert.Contains(attribute, generated);
        if (onType)
            Assert.Empty(module.Classes);
        else
            Assert.NotEmpty(Assert.Single(module.Classes).InteropBlocks);
        AssertSignaturesEqual(source, generated, onType ? "Library.Helpers`1" : "Library.Helpers");
    }

    [Fact]
    public void PartialMergeAndContractSimplification_PreserveModuleOwnership()
    {
        var first = Parse("""
            §M{Library}
              §M{Helpers:pub:partial}
                §F{First:pub} (i32:value) -> i32
                  §Q (> 2 1)
                  §R value
            """);
        var second = Parse("""
            §M{Library}
              §M{Helpers:pub:partial}
                §F{Second:pub} () -> i32
                  §R 2
            """);
        var merged = new PartialClassMerger().Merge([first, second]);
        var declaration = Assert.Single(merged.SelectMany(module => module.Classes));
        Assert.True(declaration.IsModule);
        Assert.Equal(2, declaration.Methods.Count);
        var simplified = new ContractSimplificationPass(new DiagnosticBag()).Simplify(merged[0]);
        Assert.True(Assert.Single(simplified.Classes).IsModule);
        Assert.NotSame(declaration, simplified.Classes[0]);
        Assert.DoesNotContain("§CL{", new CalorEmitter().Emit(simplified));
        Compile(new CSharpEmitter().Emit(simplified));
    }

    private static (ConversionResult Result, ModuleNode Module, string Generated) RoundTrip(string source)
    {
        var result = Convert(source);
        var module = Parse(result.CalorSource!);
        Assert.False(Bind(module).HasErrors, string.Join("\n", Bind(module).Select(d => d.Message)));
        var generated = new CSharpEmitter().Emit(module);
        Compile(generated);
        return (result, module, generated);
    }

    private static ConversionResult Convert(string source, bool preservePreprocessor = false)
    {
        var result = new CSharpToCalorConverter(new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossy,
            ValidateRoundTripCSharp = !preservePreprocessor
        }).Convert(source);
        Assert.True(result.Success, string.Join("\n", result.Issues.Select(issue => issue.Message)) + "\n" + result.CalorSource);
        return result;
    }

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        var module = new Parser(tokens, diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)) + "\n" + source);
        return module;
    }

    private static ModuleNode ParseRejectedHistoricalStatic(string source)
    {
        // Recovery AST is only a negative old-form/codegen control, never accepted compilation.
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.NotEmpty(diagnostics.Errors);
        Assert.All(diagnostics.Errors, diagnostic =>
        {
            Assert.Equal(DiagnosticCode.InvalidModifier, diagnostic.Code);
            Assert.Contains("Static class declarations are removed", diagnostic.Message);
        });
        return module;
    }

    private static DiagnosticBag Bind(ModuleNode module)
    {
        var diagnostics = new DiagnosticBag();
        new CalorBinder(diagnostics).Bind(module);
        return diagnostics;
    }

    private static CSharpCompilation Compilation(string source, string[]? symbols = null) =>
        CSharpCompilation.Create("Modules_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(preprocessorSymbols: symbols))],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

    private static Assembly Compile(string source, string[]? symbols = null)
    {
        using var stream = new MemoryStream();
        var result = Compilation(source, symbols).Emit(stream);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics) + "\n" + source);
        return Assembly.Load(stream.ToArray());
    }

    private static void AssertSignaturesEqual(string original, string generated, string metadataName, string[]? symbols = null)
    {
        static string[] Signatures(CSharpCompilation compilation, string name) =>
            compilation.GetTypeByMetadataName(name)!.GetMembers().OfType<IMethodSymbol>()
                .Where(method => method.MethodKind == MethodKind.Ordinary)
                .Select(method => method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)
                    + " -> " + method.ReturnType.ToDisplayString()
                    + " : " + string.Join(",", method.TypeParameters.SelectMany(parameter => parameter.ConstraintTypes)))
                .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(Signatures(Compilation(original, symbols), metadataName), Signatures(Compilation(generated, symbols), metadataName));
    }
}
