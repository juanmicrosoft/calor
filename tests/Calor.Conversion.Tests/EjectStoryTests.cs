using System.Reflection;
using System.Runtime.Loader;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Conversion.Tests;

/// <summary>
/// D-W3.4 (wedge plan v0.11): the eject story as a tested feature. For
/// representative Calor modules (contracts, effects, options/results), the
/// Calor → C# conversion must produce C# that (a) compiles via the shared
/// Slice-3 <see cref="GeneratedCSharpCompiler"/>, (b) preserves behavior at
/// runtime, and (c) degrades contracts to the documented runtime checks
/// (retained <c>ContractViolationException</c> guards in debug/release mode;
/// stripped entirely under the contract-mode-off waiver). The degradation
/// semantics are documented in docs/guides/adoption-playbook.md — this suite
/// is what makes that documentation a tested claim instead of a promise.
/// </summary>
public class EjectStoryTests
{
    // ------------------------------------------------------------------
    // Fixtures — representative wedge-shaped modules
    // ------------------------------------------------------------------

    // Single-return body: postcondition lowering refuses early/nested-return
    // bodies (the #764 T2 stopgap emits Calor1001 and no §S check there) —
    // that refusal is itself pinned in a test below.
    private const string ContractsModule = @"§M{m001:Pricing}
  §F{f001:ClampToCap:pub} (i32:amount, i32:cap) -> i32
    §Q (>= cap 0)
    §S (<= result cap)
    §R §C{Math.Min} §A amount §A cap §/C
";

    private const string EarlyReturnContractsModule = @"§M{m001:Pricing}
  §F{f001:ClampToCap:pub} (i32:amount, i32:cap) -> i32
    §Q (>= cap 0)
    §S (<= result cap)
    §IF{if1} (> amount cap)
      §R cap
    §R amount
";

    private const string EffectsModule = @"§M{m001:Greeter}
  §F{f001:Greet:pub} (str:name) -> void
    §E{cw}
    §P name
";

    private const string OptionsModule = @"§M{m001:Ejected}
  §F{f001:PickOrDefault:pub} (i32:x, bool:take) -> i32
    §IF{if1} take
      §B{opt} §SM x
      §R §C{opt.UnwrapOr} §A 0 §/C
    §B{none} §NN{i32}
    §R §C{none.UnwrapOr} §A -1 §/C
";

    // ------------------------------------------------------------------
    // Helpers: emit with a chosen contract mode, compile, execute
    // ------------------------------------------------------------------

    private static string Emit(string calorSource, EmitContractMode mode)
    {
        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath("eject.calr");
        var lexer = new Lexer(calorSource, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors.Select(d => d.Message)));
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors.Select(d => d.Message)));
        return new CSharpEmitter(mode).Emit(module);
    }

    private static Assembly CompileToAssembly(string csharpSource)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var preambleTree = CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble, parseOptions);
        var sourceTree = CSharpSyntaxTree.ParseText(csharpSource, parseOptions);
        var compilation = CSharpCompilation.Create(
            "EjectStory_" + Guid.NewGuid().ToString("N"),
            [preambleTree, sourceTree],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);
        Assert.True(emitResult.Success, string.Join("\n",
            emitResult.Diagnostics
                .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                .Select(d => d.ToString())));

        stream.Position = 0;
        var context = new AssemblyLoadContext("eject-story", isCollectible: false);
        context.Resolving += (_, name) =>
        {
            // Resolve Calor.Runtime (and friends) from the reference set the
            // generated code was compiled against.
            var path = GeneratedCSharpCompiler.References
                .OfType<PortableExecutableReference>()
                .Select(r => r.FilePath)
                .FirstOrDefault(p => p is not null
                    && string.Equals(Path.GetFileNameWithoutExtension(p), name.Name, StringComparison.OrdinalIgnoreCase));
            return path is null ? null : context.LoadFromAssemblyPath(path);
        };
        return context.LoadFromStream(stream);
    }

    private static object? Invoke(Assembly assembly, string moduleName, string method, params object?[] args)
    {
        var type = assembly.GetType($"{moduleName}.{moduleName}Module");
        Assert.NotNull(type);
        var methodInfo = type.GetMethod(method, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(methodInfo);
        return methodInfo.Invoke(null, args);
    }

    // ------------------------------------------------------------------
    // (a) Ejected C# compiles via the shared Slice-3 compiler
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(ContractsModule)]
    [InlineData(EffectsModule)]
    [InlineData(OptionsModule)]
    public void EjectedModules_CompileCleanly(string calorSource)
    {
        var csharp = Emit(calorSource, EmitContractMode.Debug);
        var validation = GeneratedCSharpCompiler.Validate(csharp);
        Assert.True(validation.SyntaxSuccess, string.Join("\n", validation.SyntaxErrors.Select(e => e.ToString())));
        Assert.True(validation.CompilationSuccess, string.Join("\n", validation.FormattedCompilationErrors));
    }

    // ------------------------------------------------------------------
    // (b) Behavior is preserved at runtime
    // ------------------------------------------------------------------

    [Fact]
    public void Contracts_BehaviorPreserved_OnValidInputs()
    {
        var assembly = CompileToAssembly(Emit(ContractsModule, EmitContractMode.Debug));
        Assert.Equal(5, Invoke(assembly, "Pricing", "ClampToCap", 5, 10));
        Assert.Equal(10, Invoke(assembly, "Pricing", "ClampToCap", 25, 10));
    }

    [Fact]
    public void Options_BehaviorPreserved()
    {
        var assembly = CompileToAssembly(Emit(OptionsModule, EmitContractMode.Debug));
        Assert.Equal(42, Invoke(assembly, "Ejected", "PickOrDefault", 42, true));
        Assert.Equal(-1, Invoke(assembly, "Ejected", "PickOrDefault", 42, false));
    }

    [Fact]
    public void Effects_BehaviorPreserved_AndNoRuntimeFootprint()
    {
        var csharp = Emit(EffectsModule, EmitContractMode.Debug);

        // §E is compile-time discipline only: ejecting drops the enforcement,
        // not the behavior — no effect machinery appears in the output.
        Assert.DoesNotContain("EffectSet", csharp);
        Assert.DoesNotContain("§E", csharp);

        var assembly = CompileToAssembly(csharp);
        var original = Console.Out;
        var writer = new StringWriter();
        try
        {
            Console.SetOut(writer);
            Invoke(assembly, "Greeter", "Greet", "hello-from-ejected-code");
        }
        finally
        {
            Console.SetOut(original);
        }
        Assert.Contains("hello-from-ejected-code", writer.ToString());
    }

    // ------------------------------------------------------------------
    // (c) Documented contract degradation
    // ------------------------------------------------------------------

    [Fact]
    public void Contracts_DegradeToRuntimeChecks_InDebugMode()
    {
        var csharp = Emit(ContractsModule, EmitContractMode.Debug);

        // Both §Q and §S survive as runtime guards — `calor convert` runs no
        // verification, so nothing is elided on the eject path.
        Assert.Contains("ContractViolationException", csharp);
        Assert.Contains("ContractKind.Requires", csharp);
        Assert.Contains("ContractKind.Ensures", csharp);
    }

    [Fact]
    public void Contracts_EarlyReturnBody_PostconditionRefusedLoudly_NotSilentlySkipped()
    {
        // The #764 stopgap: a §S on an early-return body gets NO runtime
        // check — the lowering refuses (Calor1001) instead of emitting a
        // check that could be silently skipped. The §Q guard still emits.
        var csharp = Emit(EarlyReturnContractsModule, EmitContractMode.Debug);
        Assert.Contains("ContractKind.Requires", csharp);
        Assert.DoesNotContain("ContractKind.Ensures", csharp);
    }

    [Fact]
    public void Contracts_ViolationThrows_AtRuntime()
    {
        var assembly = CompileToAssembly(Emit(ContractsModule, EmitContractMode.Debug));

        // cap < 0 violates §Q (>= cap 0) — the retained guard must throw.
        var ex = Assert.Throws<TargetInvocationException>(
            () => Invoke(assembly, "Pricing", "ClampToCap", 5, -1));
        Assert.NotNull(ex.InnerException);
        Assert.Equal("ContractViolationException", ex.InnerException!.GetType().Name);
        Assert.Contains("cap", ex.InnerException.Message);
    }

    [Fact]
    public void Contracts_ReleaseMode_KeepsLeanChecks()
    {
        var csharp = Emit(ContractsModule, EmitContractMode.Release);
        Assert.Contains("ContractViolationException", csharp);

        var assembly = CompileToAssembly(csharp);
        var ex = Assert.Throws<TargetInvocationException>(
            () => Invoke(assembly, "Pricing", "ClampToCap", 5, -1));
        Assert.Equal("ContractViolationException", ex.InnerException!.GetType().Name);
    }

    [Fact]
    public void Contracts_OffMode_StripsAllChecks_TheDocumentedWaiver()
    {
        var csharp = Emit(ContractsModule, EmitContractMode.Off);
        Assert.DoesNotContain("ContractViolationException", csharp);

        // The waiver strips guards; behavior otherwise unchanged — the same
        // violating call now runs through and returns the (nonsensical) cap.
        var assembly = CompileToAssembly(csharp);
        Assert.Equal(-1, Invoke(assembly, "Pricing", "ClampToCap", 5, -1));
    }
}
