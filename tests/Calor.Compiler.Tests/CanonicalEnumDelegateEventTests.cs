using System.Reflection;
using System.Runtime.Loader;
using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class CanonicalEnumDelegateEventTests
{
    private const string Source = """
        module Retained
          enum Mode byte
            None = 0
            @event = 7
          delegate Handler (int:value) → void
          class Source
            field int:Count public = 0
            event Handler:Changed
              add
                set Count (Count + 1)
              remove
                set Count (Count + 10)
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeDeclarationsPreserveClrSignaturesAndAccessorExecution(bool roundtrip)
    {
        var source = roundtrip ? new CalorEmitter().Emit(Parse(Source)) : Source;
        Assert.DoesNotContain('§', source);
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        Execute(compiled.GeneratedCode, assembly =>
        {
            var mode = assembly.GetType("Mode", throwOnError: true)!;
            Assert.Equal(typeof(byte), Enum.GetUnderlyingType(mode));
            Assert.Equal((byte)7, Convert.ChangeType(Enum.Parse(mode, "event"), typeof(byte)));
            var handlerType = assembly.GetType("Handler", throwOnError: true)!;
            Assert.Equal(typeof(void), handlerType.GetMethod("Invoke")!.ReturnType);
            Assert.Equal(typeof(int), handlerType.GetMethod("Invoke")!.GetParameters().Single().ParameterType);
            var type = assembly.GetType("Source", throwOnError: true)!;
            var instance = Activator.CreateInstance(type)!;
            var handler = Delegate.CreateDelegate(handlerType, typeof(CanonicalEnumDelegateEventTests)
                .GetMethod(nameof(Observe), BindingFlags.Static | BindingFlags.NonPublic)!);
            var changed = type.GetEvent("Changed")!;
            changed.AddEventHandler(instance, handler);
            changed.RemoveEventHandler(instance, handler);
            Assert.Equal(11, type.GetField("Count")!.GetValue(instance));
        });
    }

    [Theory]
    [InlineData("public enum Mode : byte { None = 0, @event = 7 }")]
    [InlineData("public delegate int Transform(int value);")]
    [InlineData("public class Source { private int count; public event System.Action Changed { add { count += 1; } remove { count += 10; } } public int Count => count; }")]
    [InlineData("public class Source { public event System.Action Changed; }")]
    [InlineData("public class Source { protected internal event System.Action Changed; }")]
    [InlineData("public class Source { private protected event System.Action Changed; }")]
    public void RealConverterEmitsNativeRepresentableDeclarations(string original)
    {
        var converted = new CSharpToCalorConverter().Convert(original);
        Assert.True(converted.Success, string.Join("\n", converted.Context.Issues));
        Assert.DoesNotContain("raw ", converted.CalorSource!);
        Assert.DoesNotContain('§', converted.CalorSource!);
        var compiled = Program.Compile(converted.CalorSource!);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        foreach (var source in new[] { original, compiled.GeneratedCode })
            Execute(source, assembly =>
            {
                if (!original.Contains("count += 1", StringComparison.Ordinal)) return;
                var type = assembly.GetType("Source", throwOnError: true)!;
                var instance = Activator.CreateInstance(type)!;
                Action handler = () => { };
                type.GetEvent("Changed")!.AddEventHandler(instance, handler);
                type.GetEvent("Changed")!.RemoveEventHandler(instance, handler);
                Assert.Equal(11, type.GetProperty("Count")!.GetValue(instance));
            });
    }

    [Theory]
    [InlineData("delegate D private () → void")]
    [InlineData("delegate D static () → void")]
    [InlineData("class Source\n    event System.Action:Changed abstract")]
    [InlineData("class Source\n    event System.Action:Changed\n      add\n        return")]
    [InlineData("class Source\n    event System.Action:Changed\n      add\n        return\n      add\n        return\n      remove\n        return")]
    public void UnrepresentedOrIncompleteNativeDeclarationsDiagnose(string declaration)
    {
        var diagnostics = new DiagnosticBag();
        _ = new Parser(new Lexer("module Invalid\n  " + declaration, diagnostics)
            .TokenizeAllForParser(), diagnostics).Parse();
        Assert.True(diagnostics.HasErrors);
    }

    private static void Observe(int value) { }

    [Theory]
    [InlineData("")]
    [InlineData("cw")]
    [InlineData("mut,throw")]
    public void IndentedDelegateEffectRowsRoundTripAndApplyToCalls(string effects)
    {
        var source = $"module DelegateEffects\n  delegate Handler (int:value) → void\n    ! {effects}\n"
            + $"  func Invoke (Handler:handler ! {effects}) → void\n    ! {effects}\n    handler(7)";
        var parsed = Parse(source);
        var declared = parsed.Delegates.Single().Effects!;
        var emitted = new CalorEmitter().Emit(parsed);
        Assert.DoesNotContain('§', emitted);
        Assert.Equal(declared.Effects, Parse(emitted).Delegates.Single().Effects!.Effects);
        foreach (var input in new[] { source, emitted })
        {
            var compiled = Program.Compile(input, filePath: null, options: new CompilationOptions { EnforceEffects = true });
            Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
            Execute(compiled.GeneratedCode, assembly =>
            {
                var receiver = new Receiver();
                var handler = Delegate.CreateDelegate(assembly.GetType("Handler", throwOnError: true)!,
                    receiver, typeof(Receiver).GetMethod(nameof(Receiver.Handle))!);
                assembly.GetType("GlobalModule", throwOnError: true)!.GetMethod("Invoke")!.Invoke(null, [handler]);
                Assert.Equal(7, receiver.Value);
            });
        }
    }

    [Fact]
    public void EffectfulDelegateInvocationCannotBeLaunderedThroughAPureCaller()
    {
        const string source = """
            module DelegateEffects
              delegate Handler (int:value) → void
                ! cw
              func Invoke (Handler:handler ! cw) → void
                !
                handler(7)
            """;
        var compiled = Program.Compile(source, filePath: null, options: new CompilationOptions { EnforceEffects = true });
        Assert.Contains(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
        Assert.DoesNotContain(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.EffectRowUnknown);
    }

    private sealed class Receiver
    {
        public int Value { get; private set; }
        public void Handle(int value) => Value = value;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnsafeCallableModifierDoesNotTerminateTheNativeHeader(bool roundtrip)
    {
        const string input = """
            module UnsafeHeaders
              class Native
                func Change private unsafe (in int:value) → int
                  return value + 1
            """;
        var source = roundtrip ? new CalorEmitter().Emit(Parse(input)) : input;
        var method = Parse(source).Classes.Single().Methods.Single();
        Assert.True(method.IsUnsafe);
        Assert.Equal(Visibility.Private, method.Visibility);
        Assert.Equal(Calor.Compiler.Ast.ParameterModifier.In, method.Parameters.Single().Modifier);
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        Execute(compiled.GeneratedCode, assembly =>
        {
            var type = assembly.GetType("Native", throwOnError: true)!;
            var change = type.GetMethod("Change", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.True(change.GetParameters().Single().IsIn);
            Assert.Equal(42, change.Invoke(Activator.CreateInstance(type), [41]));
        }, allowUnsafe: true);
    }

    [Fact]
    public void StaticEventConversionStillPreservesUnsupportedCSharpVerbatim()
    {
        const string original = "public class Source { public static event System.Action Changed; }";
        var converted = new CSharpToCalorConverter().Convert(original);
        Assert.True(converted.Success, string.Join("\n", converted.Context.Issues));
        Assert.Contains("raw ", converted.CalorSource!);
        Assert.DoesNotContain("post-validation-fallback", converted.CalorSource!);
        var compiled = Program.Compile(converted.CalorSource!);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        Execute(compiled.GeneratedCode, assembly =>
            Assert.True(assembly.GetType("Source", throwOnError: true)!.GetEvent("Changed")!.AddMethod!.IsStatic));
    }

    [Theory]
    [InlineData("module Source", "")]
    [InlineData("class Source", " static")]
    public void NamedModulesAndExplicitStaticEventsKeepStaticClrAccessors(string owner, string modifier)
    {
        var original = $"""
            module StaticEvents
              {owner}
                field int:Count public static = 0
                event System.Action:Changed{modifier}
                  add
                    set Count (Count + 1)
                  remove
                    set Count (Count + 10)
            """;
        foreach (var source in new[] { original, new CalorEmitter().Emit(Parse(original)) })
        {
            Assert.True(Parse(source).Classes.Single().Events.Single().IsStatic);
            var compiled = Program.Compile(source);
            Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
            Execute(compiled.GeneratedCode, assembly =>
            {
                var type = assembly.GetType("Source", throwOnError: true)!;
                var changed = type.GetEvent("Changed")!;
                Assert.True(changed.AddMethod!.IsStatic);
                Assert.True(changed.RemoveMethod!.IsStatic);
                Action handler = () => { };
                changed.AddEventHandler(null, handler);
                changed.RemoveEventHandler(null, handler);
                Assert.Equal(11, type.GetField("Count")!.GetValue(null));
            });
        }
    }

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        return module;
    }

    private static void Execute(string source, Action<Assembly> observe, bool allowUnsafe = false)
    {
        var compilation = CSharpCompilation.Create("NativeEnumDelegateEvents",
            [CSharpSyntaxTree.ParseText(source)],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: allowUnsafe));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        stream.Position = 0;
        var context = new AssemblyLoadContext("NativeEnumDelegateEvents", isCollectible: true);
        try { observe(context.LoadFromStream(stream)); }
        finally { context.Unload(); }
    }
}
