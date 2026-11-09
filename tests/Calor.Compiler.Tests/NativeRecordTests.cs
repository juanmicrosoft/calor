using System.Reflection;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class NativeRecordTests
{
    [Theory]
    [InlineData("record P (int:X)", TokenKind.Record)]
    [InlineData("record()", TokenKind.Identifier)]
    [InlineData("record ()", TokenKind.Identifier)]
    [InlineData("record.X()", TokenKind.Identifier)]
    public void RecordKeyword_IsContextualToDeclarationHeaders(string source, TokenKind expected)
    {
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        Assert.Equal(expected, tokens[0].Kind);
        Assert.False(diagnostics.HasErrors);
    }

    [Fact]
    public void CallableNamedRecord_RemainsAnOrdinaryCallableAtLineStart()
    {
        var result = Compile("""
            §M{Records}
              §F{record:pub} () -> int
                §E{}
                §R INT:7
              §F{Invoke:pub} () -> void
                §E{}
                record()
            """);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        Emit(result.GeneratedCode);
    }

    private static ConversionResult Convert(string source)
    {
        var result = new CSharpToCalorConverter().Convert(source, "records.cs");
        Assert.True(result.Success, string.Join("\n", result.Issues.Select(issue => issue.Message)));
        return result;
    }

    private static CompilationResult Compile(string source) => Program.Compile(source, "records.calr.txt",
        new CompilationOptions { EnableTypeChecking = true, EnforceEffects = true, StatusWriter = TextWriter.Null });

    private static Assembly Emit(string source)
    {
        var compilation = CSharpCompilation.Create("Records_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble + source)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }

    [Fact]
    public void BareRecord_IsPublicNativeAstWithActualPositionalDeclarations()
    {
        var result = Compile("""
            §M{Records}
              record P:pub (int:X, int:Y = INT:2)
            """);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        Assert.Contains("public record P(int X, int Y = 2)", result.GeneratedCode);
        var bag = new DiagnosticBag();
        var module = new Parser(new Lexer("""
            §M{Records}
              record P:pub (int:X, int:Y = INT:2)
            """, bag).TokenizeAllForParser(), bag).Parse();
        Assert.False(bag.HasErrors, string.Join("\n", bag));
        var record = Assert.Single(module.Classes);
        Assert.True(record.IsRecord);
        Assert.False(record.IsStruct);
        Assert.False(record.IsSealed);
        Assert.Equal(2, record.RecordParameters!.Count);
        Assert.Contains(record.RecordPrimaryConstructor, record.Constructors);
        Assert.Contains(record.RecordCopyConstructor, record.Constructors);
        Assert.All(record.RecordPositionalProperties, property =>
        {
            Assert.Equal(Visibility.Public, property.Visibility);
            Assert.NotNull(property.Getter);
            Assert.NotNull(property.Initer);
            Assert.Null(property.Setter);
        });
        Assert.Equal(2, record.RecordPositionalProperties.Count);
        var bound = new Calor.Compiler.Binding.Binder(bag).Bind(module);
        Assert.False(bag.HasErrors, string.Join("\n", bag));
        var symbol = Assert.Single(bound.SymbolsById.Values.OfType<Calor.Compiler.Binding.TypeSymbol>()
            .Where(type => type.Name == "P"));
        Assert.True(symbol.IsRecord);
        Assert.True(symbol.IsReferenceType);
    }

    [Theory]
    [InlineData("Level3_Types/044_Record.cs", 1)]
    [InlineData("Level5_Advanced/100_RecordWithMethods.cs", 2)]
    public void OriginalScorecardInputs_AreNativeAndReparseWithEffects(string path, int records)
    {
        var root = FindRoot();
        var source = File.ReadAllText(Path.Combine(root, "tests/TestData/CSharpImport", path));
        var conversion = Convert(source);
        Assert.Empty(conversion.Losses);
        RawFrameAssertions.AssertFramed(conversion.CalorSource, present: false);
        Assert.DoesNotContain("§CS{", conversion.CalorSource);
        Assert.Equal(records, conversion.Ast!.Classes.Count(node => node.IsRecord));
        var result = Compile(conversion.CalorSource!);
        Assert.False(result.HasErrors, conversion.CalorSource + "\n" + string.Join("\n", result.Diagnostics));
        Emit(source);
        Emit(result.GeneratedCode);
    }

    [Fact]
    public void PositionalRecord_ClrIdentityEqualityCloneDeconstructionAndDefaultsMatchIndependentOracle()
    {
        const string declarations = "namespace Records { public record P(int X, int Y = 2); }";
        const string probe = """
            namespace Records {
              public static class Probe {
                public static string Run() {
                  var a = new P(1);
                  var b = a with { X = 3 };
                  var (x, y) = b;
                  return $"{a == new P(1, 2)}|{a.GetHashCode() == new P(1, 2).GetHashCode()}|{a}|{b}|{x}|{y}|{a.X}";
                }
              }
            }
            """;
        var conversion = Convert(declarations);
        Assert.Empty(conversion.Losses);
        var result = Compile(conversion.CalorSource!);
        Assert.False(result.HasErrors, conversion.CalorSource + "\n" + string.Join("\n", result.Diagnostics));
        var original = Emit(declarations + probe);
        var generated = Emit(result.GeneratedCode + probe);
        Assert.Equal(original.GetType("Records.Probe")!.GetMethod("Run")!.Invoke(null, null),
            generated.GetType("Records.Probe")!.GetMethod("Run")!.Invoke(null, null));
        var record = generated.GetType("Records.P")!;
        Assert.False(record.IsValueType);
        Assert.False(record.IsSealed);
        Assert.NotNull(record.GetMethod("<Clone>$"));
        Assert.NotNull(record.GetProperty("EqualityContract", BindingFlags.Instance | BindingFlags.NonPublic));
        Assert.Contains(typeof(System.Runtime.CompilerServices.IsExternalInit),
            record.GetProperty("X")!.SetMethod!.ReturnParameter.GetRequiredCustomModifiers());
        Assert.Equal(2, record.GetMethod("Deconstruct")!.GetParameters().Length);
        Assert.Equal(2, record.GetConstructors().Single().GetParameters()[1].DefaultValue);
        var a = Activator.CreateInstance(record, 1, 2)!;
        var b = Activator.CreateInstance(record, 1, 2)!;
        var wrong = Activator.CreateInstance(record, 1, 3)!;
        Assert.True(a.Equals(b));
        Assert.False(a.Equals(wrong));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotSame(a, record.GetMethod("<Clone>$")!.Invoke(a, null));
    }

    [Fact]
    public void DerivedRecords_RetainBaseStateEqualityContractAndPolymorphicClone()
    {
        const string declarations = """
            namespace Records {
              public record IBase(int X);
              public sealed record Derived(int X, string Label) : IBase(X);
            }
            """;
        const string probe = """
            namespace Records {
              public static class Probe {
                public static string Run() {
                  IBase a = new Derived(4, "a");
                  var copy = a with { X = 9 };
                  return $"{a == new IBase(4)}|{a == new Derived(4, "a")}|{a == new Derived(4, "b")}|{copy.GetType().Name}|{copy.X}|{((Derived)copy).Label}|{a.X}";
                }
              }
            }
            """;
        var conversion = Convert(declarations);
        Assert.Empty(conversion.Losses);
        var result = Compile(conversion.CalorSource!);
        Assert.False(result.HasErrors, conversion.CalorSource + "\n" + string.Join("\n", result.Diagnostics));
        Assert.Equal(Emit(declarations + probe).GetType("Records.Probe")!.GetMethod("Run")!.Invoke(null, null),
            Emit(result.GeneratedCode + probe).GetType("Records.Probe")!.GetMethod("Run")!.Invoke(null, null));
    }

    [Fact]
    public void DerivedPositionalRecord_CannotHideForbiddenInheritedInitEffect()
    {
        const string source = """
            §M{DerivedInit}
              record Base:pub (int:X)
                §PROP{X:int:pub}
                  §GET
                    §R 0
                  §INIT
                    §P "base init"
              record Derived:pub (int:X) : Base(X)
              §F{Copy:pub} (Derived:value) -> Derived
                §E{alloc,mut}
                §R §WITH value §SET{X} 1
            """;
        var refused = Compile(source);
        Assert.Contains(refused.Diagnostics.Errors, error => error.Code == "Calor0423"
            && error.Message.Contains("Base.X", StringComparison.Ordinal));
        var declared = Compile(source.Replace("§E{alloc,mut}", "§E{alloc,mut,cw}", StringComparison.Ordinal));
        Assert.Contains(declared.Diagnostics.Errors, error => error.Code == "Calor0423"
            && error.Message.Contains("Base.X", StringComparison.Ordinal));
    }

    [Fact]
    public void RecordOperatorsAndConversions_ExecuteWithoutChangingValueSemantics()
    {
        const string declarations = """
            namespace Records {
              public record Money(decimal Amount, string Currency) {
                public static Money operator +(Money a, Money b) =>
                  new Money(a.Amount + b.Amount, a.Currency);
              }
              public record Celsius(double Value) {
                public static implicit operator double(Celsius c) => c.Value;
              }
            }
            """;
        const string probe = """
            namespace Records {
              public static class Probe {
                public static bool Run() {
                  var a = new Money(2m, "USD");
                  var b = new Money(3m, "USD");
                  return a + b == new Money(5m, "USD")
                    && (double)new Celsius(3) == 3
                    && new Celsius(3) == new Celsius(3);
                }
              }
            }
            """;
        var conversion = Convert(declarations);
        Assert.Empty(conversion.Losses);
        Assert.All(conversion.Ast!.Classes, record =>
        {
            Assert.True(record.IsRecord);
            Assert.Single(record.OperatorOverloads);
        });
        var result = Compile(conversion.CalorSource!);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        Assert.Equal(true, Emit(declarations + probe).GetType("Records.Probe")!.GetMethod("Run")!.Invoke(null, null));
        Assert.Equal(true, Emit(result.GeneratedCode + probe).GetType("Records.Probe")!.GetMethod("Run")!.Invoke(null, null));
    }

    [Theory]
    [InlineData("public readonly record struct P(int X);", "record-struct")]
    [InlineData("public record P([property: System.Obsolete] int X);", "record-positional-parameter-metadata")]
    [InlineData("public record P { public event System.Action? Changed; }", "record-member-EventFieldDeclaration")]
    [InlineData("public partial record P(int X);", "record-partial")]
    public void UnsupportedVariants_PreserveExactCSharpWithExplicitReason(string source, string reason)
    {
        var conversion = Convert(source);
        var interop = Assert.Single(conversion.Ast!.InteropBlocks);
        Assert.Equal(source, interop.CSharpCode.Trim());
        Assert.Equal(reason, Assert.Single(conversion.Losses).Feature);
        Assert.Empty(conversion.Ast.Classes);
        var result = Compile(conversion.CalorSource!);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        Emit(result.GeneratedCode);
    }

    [Fact]
    public void WrongConstructorSignatureAndWrongRecordKind_AreNotSilentlyAccepted()
    {
        var wrongSignature = Compile("""
            §M{Records}
              record P:pub (int:X)
              §F{Make:pub} () -> P
                §E{alloc}
                §R §NEW{P} §A "wrong" §/NEW
            """);
        Assert.True(wrongSignature.HasErrors);
        Assert.NotEmpty(wrongSignature.Diagnostics.Errors);
        var wrongKind = Compile("""
            §M{Records}
              record P:pub:struct (int:X)
            """);
        Assert.True(wrongKind.HasErrors);
    }

    [Theory]
    [InlineData("pub")]
    [InlineData("priv")]
    public void ConstructorEffects_ChargeActualConstructorAndCaller(string visibility)
    {
        var result = Compile($$"""
            §M{Records}
              record P:pub
                §CTOR{pub} (int:x)
                  §P "ctor"
              §F{Make:{{visibility}}} () -> P
                §E{alloc}
                §R §NEW{P} §A INT:1 §/NEW
            """);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Calor0422");
    }

    [Theory]
    [InlineData("pub")]
    [InlineData("priv")]
    public void PublicAndPrivateRecordConstructors_UseTheSameRealEffectContract(string visibility)
    {
        var result = Compile($$"""
            §M{Records}
              record P:pub
                §CTOR{ {{visibility}} } (int:x)
                  §P "ctor"
                §MT{Make:pub:stat} () -> P
                  §E{alloc}
                  §R §NEW{P} §A INT:1 §/NEW
            """);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Calor0422");
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
    }

    [Fact]
    public void CopyAndInitBodies_AreNotLaunderedByWith()
    {
        var result = Compile("""
            §M{Records}
              record P:pub (int:X)
                §CTOR{prot} (P:original)
                  §P "copy"
                §PROP{Value:int:pub}
                  §GET
                    §R INT:0
                  §INIT
                    §P "init"
                §MT{Copy:pub} () -> P
                  §E{alloc,mut}
                  §R §WITH §THIS §SET{Value} INT:2 §/WITH
            """);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Calor0422");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Calor0423");
    }

    [Fact]
    public void UnknownConstructor_StillReportsCalor0411()
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer("""
            §M{Records}
              record P:pub (int:X)
              §F{Make:pub} () -> P
                §E{alloc}
                §R §NEW{P} §A "wrong" §/NEW
            """, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        new EffectEnforcementPass(diagnostics, policy: UnknownCallPolicy.Strict).Enforce(module);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
    }

    [Fact]
    public void OriginalRecordMethodsAndState_MatchIndependentCSharpOracle()
    {
        var source = File.ReadAllText(Path.Combine(FindRoot(),
            "tests/TestData/CSharpImport/Level5_Advanced/100_RecordWithMethods.cs"));
        var conversion = Convert(source);
        Assert.Empty(conversion.Losses);
        var result = Compile(conversion.CalorSource!);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        static object[] Observe(Assembly assembly)
        {
            var person = assembly.GetType("RecordWithMethods.Person")!;
            var original = Activator.CreateInstance(person, "Ada", "Lovelace", 17)!;
            var aged = person.GetMethod("WithAge")!.Invoke(original, [18])!;
            var point = assembly.GetType("RecordWithMethods.Point3D")!;
            var a = Activator.CreateInstance(point, 3d, 4d, 0d)!;
            var scaled = point.GetMethod("Scale")!.Invoke(a, [2d])!;
            var sum = point.GetMethod("Add")!.Invoke(a, [scaled])!;
            return [
                person.GetProperty("FullName")!.GetValue(original)!,
                person.GetProperty("IsAdult")!.GetValue(original)!,
                person.GetProperty("IsAdult")!.GetValue(aged)!,
                person.GetProperty("Age")!.GetValue(original)!,
                person.GetMethod("Greet")!.Invoke(aged, null)!,
                point.GetProperty("Magnitude")!.GetValue(a)!,
                point.GetProperty("X")!.GetValue(sum)!,
                point.GetProperty("Y")!.GetValue(sum)!,
                point.GetProperty("Z")!.GetValue(point.GetProperty("Origin")!.GetValue(null))!
            ];
        }
        Assert.Equal(Observe(Emit(source)), Observe(Emit(result.GeneratedCode)));
    }

    [Theory]
    [InlineData("public record P<T>(T Value);")]
    [InlineData("public record P { public int Value { get; init; } = 7; }")]
    public void AdditionalRecordShapes_PreserveNativeAstAndClrKind(string declaration)
    {
        var conversion = Convert(declaration);
        Assert.Empty(conversion.Losses);
        Assert.All(conversion.Ast!.Classes, record => Assert.True(record.IsRecord));
        var result = Compile(conversion.CalorSource!);
        Assert.False(result.HasErrors, conversion.CalorSource + "\n" + string.Join("\n", result.Diagnostics));
        Assert.Contains(Emit(result.GeneratedCode).GetTypes(), type => type.GetMethod("<Clone>$") != null);
    }

    [Fact]
    public void CustomCopyConstructor_PreservesStateAndImplicitDefaultConstructor()
    {
        const string source = """
            namespace Records {
              public record P {
                public int Value { get; init; } = 7;
                protected P(P other) { Value = other.Value + 1; }
                public static P Make() => new P();
                public P Copy() => this with { };
              }
            }
            """;
        var conversion = Convert(source);
        Assert.Empty(conversion.Losses);
        var result = Compile(conversion.CalorSource!);
        Assert.False(result.HasErrors, conversion.CalorSource + "\n" + string.Join("\n", result.Diagnostics));
        static (int Original, int Copy) Observe(Assembly assembly)
        {
            var type = assembly.GetType("Records.P")!;
            var original = type.GetMethod("Make")!.Invoke(null, null)!;
            var copy = type.GetMethod("Copy")!.Invoke(original, null)!;
            Assert.NotSame(original, copy);
            return ((int)type.GetProperty("Value")!.GetValue(original)!,
                (int)type.GetProperty("Value")!.GetValue(copy)!);
        }
        Assert.Equal((7, 8), Observe(Emit(source)));
        Assert.Equal(Observe(Emit(source)), Observe(Emit(result.GeneratedCode)));
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
                return directory.FullName;
        throw new InvalidOperationException("Repository root not found.");
    }
}
