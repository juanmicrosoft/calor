using System.Reflection;
using System.Text.Json;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Binding.BoundTypes;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using Binder = Calor.Compiler.Binding.Binder;

namespace Calor.Compiler.Tests;

public sealed class NativeQualifiedModuleCallTests
{
    private const string Callee = """
        module OrderService OrderService

          func SaveOrder () → void
            ! db:w
            // A real implementation writes the order to a database here.
        """ + "\n";

    private const string Caller = """
        module Handler
          func HandleRequest () → void
            ! db:w
            OrderService.SaveOrder()
        """ + "\n";

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RealCli_OriginalQualifiedCallPreservesDeclaredRowAndOwner(bool reverse, bool declared)
    {
        var root = Path.Combine(Path.GetTempPath(), "calor-native-qualified-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "OrderService.calr"), Callee);
            var caller = declared ? Caller : Caller.Replace("    ! db:w\n", "", StringComparison.Ordinal);
            File.WriteAllText(Path.Combine(root, "Handler.calr"), caller);
            var inputs = reverse ? new[] { "Handler.calr", "OrderService.calr" } : ["OrderService.calr", "Handler.calr"];
            string[]? previousOutput = null;
            for (var run = 0; run < 2; run++)
            {
                var result = CliTestHarness.RunCli(root,
                    "--input", inputs[0], "--input", inputs[1], "--format", "json");
                using var json = JsonDocument.Parse(result.StdOut);
                var diagnostics = json.RootElement.GetProperty("diagnostics").EnumerateArray().ToArray();
                Assert.DoesNotContain(diagnostics, item => item.GetProperty("code").GetString() == "Calor0411");
                Assert.DoesNotContain("INT.SaveOrder", result.StdOut + result.StdErr);
                if (!declared)
                {
                    Assert.Equal(1, result.ExitCode);
                    Assert.Equal(2, diagnostics.Length);
                    var error = Assert.Single(diagnostics.Where(item => item.GetProperty("code").GetString() == "Calor0410"));
                    Assert.Equal("error", error.GetProperty("severity").GetString());
                    Assert.Contains("db:w", error.GetProperty("message").GetString());
                    var warning = Assert.Single(diagnostics.Where(item => item.GetProperty("code").GetString() == "Calor0417"));
                    Assert.Equal("warning", warning.GetProperty("severity").GetString());
                    continue;
                }

                Assert.True(result.ExitCode == 0, result.StdOut + result.StdErr);
                Assert.Empty(diagnostics);
                var output = new[]
                {
                    File.ReadAllText(Path.Combine(root, "OrderService.g.cs")),
                    File.ReadAllText(Path.Combine(root, "Handler.g.cs"))
                };
                Assert.Contains("global::OrderService.OrderServiceModule.SaveOrder()", output[1]);
                if (previousOutput is not null)
                    Assert.Equal(previousOutput, output);
                previousOutput = output;
                var assembly = Compile(output);
                Assert.NotNull(assembly.GetType("OrderService.OrderServiceModule")!.GetMethod("SaveOrder"));
                Assert.Null(assembly.GetType("GlobalModule")!.GetMethod("HandleRequest")!.Invoke(null, null));
            }
            Assert.Equal(Callee, File.ReadAllText(Path.Combine(root, "OrderService.calr")));
            Assert.Equal(caller, File.ReadAllText(Path.Combine(root, "Handler.calr")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("OrderService", "OrderService")]
    [InlineData("Actual.Store", "OrderService")]
    [InlineData("Actual.Store", "Actual.Store")]
    public void BoundQualifiedCallUsesActualOwnerAndAuthoredSpans(string actualNamespace, string qualifier)
    {
        var callee = Parse(Callee.Replace("module OrderService OrderService",
            $"module OrderService {actualNamespace}", StringComparison.Ordinal));
        var source = Caller.Replace("OrderService.SaveOrder()", $"{qualifier}.SaveOrder()", StringComparison.Ordinal);
        var caller = Parse(source);
        var targets = CompilationDriver.BuildCrossModuleFunctionMap([callee, caller]);
        var diagnostics = new DiagnosticBag();
        var binder = new Binder(diagnostics) { CrossModuleFunctionModules = targets };
        var bound = binder.Bind(caller);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.Single(Descendants(bound).OfType<BoundCallExpression>());
        var owner = $"{actualNamespace}.{actualNamespace.Split('.')[^1]}Module";
        Assert.Equal($"{qualifier}.SaveOrder", call.Target);
        Assert.Equal(owner, call.ResolvedTypeName);
        var receiver = Assert.IsType<BoundTypeReferenceExpression>(call.Receiver);
        Assert.Equal(owner, receiver.Type.FullyQualifiedName);
        Assert.Equal(qualifier, source[receiver.Span.Start..receiver.Span.End]);
        Assert.Equal("SaveOrder", source[call.CalleeSpan.Start..call.CalleeSpan.End]);
        Assert.Equal(source.IndexOf("SaveOrder()", StringComparison.Ordinal), call.CalleeSpan.Start);
        Assert.Equal($"{qualifier}.SaveOrder", Assert.Single(binder.CrossModuleCallTargets).Value);
        var summary = EffectSummaryBuilder.Build(caller, targets);
        Assert.Equal("OrderService.SaveOrder", Assert.Single(Assert.Single(summary.Callers).Calls).Target);
        var registry = CrossModuleEffectRegistry.Build([(callee, "callee.calr")]);
        Assert.NotNull(registry.TryResolve("OrderService.SaveOrder"));
        var pass = new EffectEnforcementPass(diagnostics, crossModuleFunctionNames: targets.Keys)
        {
            CrossModuleFunctionModules = targets
        };
        pass.Enforce(caller);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var generated = new CSharpEmitter { CrossModuleFunctionModules = targets }.Emit(caller);
        Assert.Contains($"global::{owner}.SaveOrder()", generated);
        var assembly = Compile([new CSharpEmitter().Emit(callee), generated]);
        Assert.Null(assembly.GetType("GlobalModule")!.GetMethod("HandleRequest")!.Invoke(null, null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceTypeAndValueReceiversShadowModuleLabel(bool valueReceiver)
    {
        var callee = Parse(Callee.Replace("module OrderService OrderService",
            "module OrderService Actual.Store", StringComparison.Ordinal));
        var source = valueReceiver ? """
            module Handler
              class LocalStore
                func SaveOrder () → int
                  !
                  return 17
              func Read (LocalStore:OrderService) → int
                !
                return OrderService.SaveOrder()
            """ : """
            module Handler
              module OrderService
                func SaveOrder () → int
                  !
                  return 17
              func Read () → int
                !
                return OrderService.SaveOrder()
            """;
        var caller = Parse(source);
        var targets = CompilationDriver.BuildCrossModuleFunctionMap([callee, caller]);
        var diagnostics = new DiagnosticBag();
        var binder = new Binder(diagnostics) { CrossModuleFunctionModules = targets };
        binder.Bind(caller);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        Assert.Empty(binder.CrossModuleCallTargets);
        var pass = new EffectEnforcementPass(diagnostics, crossModuleFunctionNames: targets.Keys)
        {
            CrossModuleFunctionModules = targets
        };
        pass.Enforce(caller);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var generated = new CSharpEmitter { CrossModuleFunctionModules = targets }.Emit(caller);
        Assert.DoesNotContain("StoreModule.SaveOrder", generated);
        var assembly = Compile([new CSharpEmitter().Emit(callee), generated]);
        object?[]? args = valueReceiver ? [Activator.CreateInstance(assembly.GetType("LocalStore")!)] : null;
        Assert.Equal(17, assembly.GetType("GlobalModule")!.GetMethod("Read")!.Invoke(null, args));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrAmbiguousModuleReceiverIsUnresolvedNotInt(bool ambiguous)
    {
        var caller = Parse(Caller);
        var callees = ambiguous
            ? new[] { Parse(Callee), Parse(Callee.Replace("OrderService OrderService", "OrderService Other", StringComparison.Ordinal)) }
            : Array.Empty<ModuleNode>();
        var targets = CompilationDriver.BuildCrossModuleFunctionMap(callees.Append(caller).ToArray());
        var diagnostics = new DiagnosticBag();
        var binder = new Binder(diagnostics) { CrossModuleFunctionModules = targets };
        var bound = binder.Bind(caller);
        Assert.Contains(diagnostics, item => item.Code == DiagnosticCode.UndefinedReference);
        Assert.Empty(binder.CrossModuleCallTargets);
        Assert.IsType<UnresolvedBoundType>(Assert.Single(Descendants(bound).OfType<BoundCallExpression>()).Receiver!.Type);
        new EffectEnforcementPass(diagnostics, crossModuleFunctionNames: targets.Keys)
        {
            CrossModuleFunctionModules = targets
        }.Enforce(caller);
        Assert.Contains(diagnostics, item => item.Code == DiagnosticCode.UnknownExternalCall);
        Assert.DoesNotContain(diagnostics, item => item.Message.Contains("INT.SaveOrder", StringComparison.Ordinal));
        Assert.Contains(diagnostics, item => item.Code == DiagnosticCode.UnknownExternalCall
            && item.Message.Contains("OrderService.SaveOrder", StringComparison.Ordinal));
    }

    [Fact]
    public void MetadataTypeReceiverWinsOverEqualModuleLabel()
    {
        var callee = Parse("""
            module Math Actual.Store
              func Abs (int:value) → int
                ! db:w
                return 0
            """);
        var caller = Parse("""
            module Handler
              func Read () → int
                !
                return Math.Abs(-3)
            """);
        var targets = CompilationDriver.BuildCrossModuleFunctionMap([callee, caller]);
        var diagnostics = new DiagnosticBag();
        var binder = new Binder(diagnostics) { CrossModuleFunctionModules = targets };
        binder.Bind(caller);
        Assert.Empty(binder.CrossModuleCallTargets);
        new EffectEnforcementPass(diagnostics, crossModuleFunctionNames: targets.Keys)
        {
            CrossModuleFunctionModules = targets
        }.Enforce(caller);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var generated = new CSharpEmitter { CrossModuleFunctionModules = targets }.Emit(caller);
        Assert.DoesNotContain("StoreModule.Abs", generated);
        var assembly = Compile([new CSharpEmitter().Emit(callee), generated]);
        Assert.Equal(3, assembly.GetType("GlobalModule")!.GetMethod("Read")!.Invoke(null, null));
    }

    [Fact]
    public void QualifiedModuleCallsRetainArgumentEvaluationEffects()
    {
        var callee = Parse(Callee.Replace("SaveOrder ()", "SaveOrder (int:value)", StringComparison.Ordinal));
        var caller = Parse("""
            module Handler
              func Observe () → int
                ! cw
                Console.WriteLine(1)
                return 1
              func HandleRequest () → void
                ! db:w
                OrderService.SaveOrder(Observe())
            """);
        var targets = CompilationDriver.BuildCrossModuleFunctionMap([callee, caller]);
        var diagnostics = new DiagnosticBag();
        new EffectEnforcementPass(diagnostics, crossModuleFunctionNames: targets.Keys)
        {
            CrossModuleFunctionModules = targets
        }.Enforce(caller);
        Assert.Contains(diagnostics, item => item.Code == DiagnosticCode.ForbiddenEffect
            && item.Message.Contains("HandleRequest", StringComparison.Ordinal)
            && item.Message.Contains("cw", StringComparison.Ordinal));
    }

    [Fact]
    public void ReusedEmitterAndBinderDoNotRetainQualifiedDecisionsAcrossModules()
    {
        var callee = Parse(Callee);
        var caller = Parse(Caller);
        var shadow = Parse(Caller.Replace("func HandleRequest ()", "func HandleRequest (int:OrderService)", StringComparison.Ordinal));
        var targets = CompilationDriver.BuildCrossModuleFunctionMap([callee, caller]);
        var binder = new Binder(new DiagnosticBag()) { CrossModuleFunctionModules = targets };
        binder.Bind(caller);
        Assert.Single(binder.CrossModuleCallTargets);
        binder.Bind(shadow);
        Assert.Empty(binder.CrossModuleCallTargets);
        var emitter = new CSharpEmitter { CrossModuleFunctionModules = targets };
        Assert.Contains("global::OrderService.OrderServiceModule.SaveOrder", emitter.Emit(caller));
        Assert.DoesNotContain("OrderServiceModule.SaveOrder", emitter.Emit(shadow));
    }

    [Theory]
    [InlineData("file", false)]
    [InlineData("file", true)]
    [InlineData("namespace", false)]
    [InlineData("namespace", true)]
    [InlineData("global", false)]
    [InlineData("global", true)]
    public void RealCli_NamespaceAliasNeverBorrowsUnrelatedFileLabel(string scope, bool reverse)
    {
        var root = Path.Combine(Path.GetTempPath(), "calor-qualified-alias-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "callee.calr"),
                "module M Actual.Store\n  func SaveOrder () → void\n    ! db:w\n");
            File.WriteAllText(Path.Combine(root, "other.calr"),
                "module Other Actual.Other\n"
                + (scope == "global" ? "  global using M = Actual.Other\n" : "")
                + "  func Noop () → void\n    !\n");
            var source = scope == "namespace" ? """
                module Caller Client block
                  namespace using M = Actual.Other
                  func Use () → void
                    ! db:w
                    M.SaveOrder()
                """ + "\n" :
                "module Caller\n"
                + (scope == "file" ? "  using M = Actual.Other\n" : "")
                + "  func Use () → void\n    ! db:w\n    M.SaveOrder()\n";
            File.WriteAllText(Path.Combine(root, "caller.calr"), source);
            var inputs = new[] { "callee.calr", "other.calr", "caller.calr" };
            if (reverse)
                Array.Reverse(inputs);
            var result = CliTestHarness.RunCli(root,
                "--input", inputs[0], "--input", inputs[1], "--input", inputs[2], "--format", "json");
            Assert.True(result.ExitCode == 1, result.StdOut + result.StdErr);
            using var json = JsonDocument.Parse(result.StdOut);
            var diagnostics = json.RootElement.GetProperty("diagnostics").EnumerateArray().ToArray();
            Assert.Contains(diagnostics, item => item.GetProperty("severity").GetString() == "error");
            Assert.DoesNotContain(diagnostics, item =>
                item.GetProperty("code").GetString()!.StartsWith("Calor00", StringComparison.Ordinal)
                || item.GetProperty("code").GetString()!.StartsWith("Calor01", StringComparison.Ordinal));
            var generated = Path.Combine(root, "caller.g.cs");
            if (File.Exists(generated))
                Assert.DoesNotContain("global::Actual.Store.StoreModule.SaveOrder", File.ReadAllText(generated));
            Assert.Equal(source, File.ReadAllText(Path.Combine(root, "caller.calr")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void NamespaceAliasIsLexicalAndEmitterDoesNotRetargetIt()
    {
        var callee = Parse("module M Actual.Store\n  func SaveOrder () → void\n    ! db:w\n");
        var source = """
            module Caller Client.Aliased block
              namespace using M = Actual.Other
              func Rejected () → void
                ! db:w
                M.SaveOrder()
            """ + "\n";
        var caller = Parse(source);
        var plain = Parse("""
            module Plain Client.Plain block
              func Accepted () → void
                ! db:w
                M.SaveOrder()
            """);
        var targets = CompilationDriver.BuildCrossModuleFunctionMap([callee, caller, plain]);
        var diagnostics = new DiagnosticBag();
        var binder = new Binder(diagnostics) { CrossModuleFunctionModules = targets };
        binder.Bind(caller);
        Assert.Empty(binder.CrossModuleCallTargets);
        binder.Bind(plain);
        Assert.Equal("M.SaveOrder", Assert.Single(binder.CrossModuleCallTargets).Value);
        var emitter = new CSharpEmitter { CrossModuleFunctionModules = targets };
        var generated = emitter.Emit(caller);
        Assert.Contains("using M = Actual.Other;", generated);
        Assert.DoesNotContain("global::Actual.Store.StoreModule.SaveOrder()", generated);
        Assert.Contains("M.SaveOrder();", generated);
        Assert.Contains("global::Actual.Store.StoreModule.SaveOrder()", emitter.Emit(plain));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActualTypeAliasRetainsItsSelectedOwnerInsteadOfFileLabel(bool sourceType)
    {
        var callee = Parse("""
            module M Actual.Store
              func Abs (int:value) → int
                ! db:w
                return 0
            """);
        var caller = Parse(sourceType ? """
            module Handler Local
              using M = Local.Math
              module Math
                func Abs (int:value) → int
                  !
                  return 17
              func Read () → int
                !
                return M.Abs(-3)
            """ : """
            module Handler
              using M = System.Math
              func Read () → int
                !
                return M.Abs(-3)
            """);
        var targets = CompilationDriver.BuildCrossModuleFunctionMap([callee, caller]);
        var diagnostics = new DiagnosticBag();
        var binder = new Binder(diagnostics) { CrossModuleFunctionModules = targets };
        binder.Bind(caller);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        Assert.Empty(binder.CrossModuleCallTargets);
        var generated = new CSharpEmitter { CrossModuleFunctionModules = targets }.Emit(caller);
        Assert.DoesNotContain("StoreModule.Abs", generated);
        var assembly = Compile([new CSharpEmitter().Emit(callee), generated]);
        var owner = sourceType ? "Local.LocalModule" : "GlobalModule";
        Assert.Equal(sourceType ? 17 : 3, assembly.GetType(owner)!.GetMethod("Read")!.Invoke(null, null));
    }

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        return module;
    }

    private static IEnumerable<BoundNode> Descendants(BoundNode node)
    {
        yield return node;
        foreach (var child in node.ChildNodes)
            foreach (var descendant in Descendants(child))
                yield return descendant;
    }

    private static Assembly Compile(string[] sources)
    {
        var paths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("Trusted platform assemblies unavailable.")).Split(Path.PathSeparator);
        var compilation = CSharpCompilation.Create("QualifiedModules_" + Guid.NewGuid().ToString("N"),
            sources.Select(source => CSharpSyntaxTree.ParseText(source)),
            paths.Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }
}
