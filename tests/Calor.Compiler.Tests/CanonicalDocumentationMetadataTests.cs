using System.Runtime.Loader;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class CanonicalDocumentationMetadataTests
{
    [Theory]
    [InlineData("/// deprecated 1.4.0 NewApi", "NewApi")]
    [InlineData("/// deprecated 1.4.0", null)]
    public void CountedDeprecationFormsPreserveVersionAndOptionalReplacement(string comment, string? replacement)
    {
        var source = $"module Api\n  {comment}\n  func Old () → void\n    !\n    return";
        var module = Parse(source);
        var deprecated = module.Functions.Single().Deprecated!;
        Assert.Equal("1.4.0", deprecated.SinceVersion);
        Assert.Equal(replacement, deprecated.Replacement);
        var emitted = new CalorEmitter().Emit(module);
        Assert.DoesNotContain('§', emitted);
        Assert.Equal(1, emitted.Split("/// deprecated").Length - 1);
        Assert.True(emitted.IndexOf("/// deprecated", StringComparison.Ordinal)
            < emitted.IndexOf("func Old", StringComparison.Ordinal));
        var reparse = Parse(emitted).Functions.Single().Deprecated!;
        Assert.Equal(deprecated.SinceVersion, reparse.SinceVersion);
        Assert.Equal(replacement, reparse.Replacement);
        Assert.Contains("[System.Obsolete(\"Deprecated since 1.4.0", new CSharpEmitter().Emit(module));
    }

    [Fact]
    public void CountedBreakingFormsPreserveLiteralDescriptionsAndSourceOrder()
    {
        var source = """
            module Api
              /// Explains the API.
              /// since 1.2.0
              /// breaking 2.0.0 "Requires x > 0 & y < 5"
              /// breaking 3.0.0 "Requires options"
              func Old () → void
                !
                return
            """;
        var module = Parse(source);
        var function = module.Functions.Single();
        Assert.Equal("1.2.0", function.Since!.Version);
        Assert.Equal(new[] { "2.0.0", "3.0.0" }, function.BreakingChanges.Select(change => change.Version));
        Assert.Equal(new[] { "Requires x > 0 & y < 5", "Requires options" },
            function.BreakingChanges.Select(change => change.Description));
        var emitted = new CalorEmitter().Emit(module);
        Assert.Contains("/// Explains the API.", emitted);
        Assert.Equal(1, emitted.Split("/// since").Length - 1);
        Assert.Equal(2, emitted.Split("/// breaking").Length - 1);
        Assert.DoesNotContain('§', emitted);
        var reparsed = Parse(emitted).Functions.Single();
        Assert.Equal(function.BreakingChanges.Select(change => change.Description),
            reparsed.BreakingChanges.Select(change => change.Description));
        Assert.Equal(emitted, new CalorEmitter().Emit(Parse(emitted)));
    }

    [Fact]
    public void BreakingDescriptionsDecodeAndReencodeCSharpEscapes()
    {
        var module = Parse("module Api\n  /// breaking 2.0.0 \"Use \\\"options\\\"\\nNext line\"\n  func Old ()\n    !\n    return");
        Assert.Equal("Use \"options\"\nNext line", module.Functions.Single().BreakingChanges.Single().Description);
        var emitted = new CalorEmitter().Emit(module);
        Assert.Equal(module.Functions.Single().BreakingChanges.Single().Description,
            Parse(emitted).Functions.Single().BreakingChanges.Single().Description);
    }

    [Theory]
    [InlineData("since")]
    [InlineData("since not-a-version")]
    [InlineData("deprecated")]
    [InlineData("deprecated 1.4.0 NewApi extra")]
    [InlineData("deprecated 1.4.0\n  /// deprecated 1.5.0")]
    [InlineData("breaking 2.0.0 unquoted")]
    [InlineData("breaking 2.0.0 \"text\" + \"more\"")]
    [InlineData("breaking 2.0.0 \"text\" trailing")]
    public void MalformedOrDuplicateMetadataIsDiagnosed(string comment)
    {
        var diagnostics = new DiagnosticBag();
        _ = new Parser(new Lexer($"module Bad\n  /// {comment}\n  func Old ()\n    !\n    return", diagnostics)
            .TokenizeAllForParser(), diagnostics).Parse();
        Assert.True(diagnostics.HasErrors);
    }

    [Fact]
    public void UnselectedReasonAndRemovalFieldsAreNotSilentlyDropped()
    {
        var span = default(TextSpan);
        var emitter = new CalorEmitter();
        Assert.Throws<InvalidOperationException>(() =>
            emitter.Visit(new DeprecatedNode(span, "1.4.0", null, "Reason", "2.0.0", new AttributeCollection())));
    }

    [Theory]
    [InlineData("module", false)]
    [InlineData("module", true)]
    [InlineData("class", false)]
    public void MemberMetadataRetainsSelectedSpellingAndActualClrDeprecation(string ownerKind, bool isAsync)
    {
        var source = $$"""
            module Api
              {{ownerKind}} Owner
                /// Explains the API.
                /// since 1.2.0
                /// deprecated 1.4.0 Rewrite
                /// breaking 2.0.0 "Requires x > 0 & y < 5"
                /// breaking 3.0.0 "Use \"options\"\nNext line ${value}"
                func Old{{(isAsync ? " async" : "")}} () → int
                  !
                  return 7
            """;
        var original = Parse(source);
        var method = original.Classes.Single().Methods.Single();
        Assert.Equal("1.2.0", method.Since!.Version);
        Assert.Equal("Rewrite", method.Deprecated!.Replacement);
        Assert.Equal(2, method.BreakingChanges.Count);
        var emitted = new CalorEmitter().Emit(original);
        Assert.DoesNotContain('§', emitted);
        Assert.Equal(1, emitted.Split("/// since").Length - 1);
        Assert.Equal(1, emitted.Split("/// deprecated").Length - 1);
        Assert.Equal(2, emitted.Split("/// breaking").Length - 1);
        var reparsed = Parse(emitted);
        var roundTrip = reparsed.Classes.Single().Methods.Single();
        Assert.Equal(method.Since.Version, roundTrip.Since!.Version);
        Assert.Equal(method.Deprecated.SinceVersion, roundTrip.Deprecated!.SinceVersion);
        Assert.Equal(method.BreakingChanges.Select(change => change.Description),
            roundTrip.BreakingChanges.Select(change => change.Description));
        Assert.Equal(emitted, new CalorEmitter().Emit(reparsed));
        var compilation = CSharpCompilation.Create("MemberMetadata",
            [CSharpSyntaxTree.ParseText(new CSharpEmitter().Emit(reparsed),
                new CSharpParseOptions(documentationMode: DocumentationMode.Diagnose))],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var member = compilation.GetTypeByMetadataName("Owner")!.GetMembers("Old").Single();
        var attribute = Assert.Single(member.GetAttributes());
        Assert.Equal("System.ObsoleteAttribute", attribute.AttributeClass!.ToDisplayString());
        Assert.Equal("Deprecated since 1.4.0. Use Rewrite instead", attribute.ConstructorArguments.Single().Value);
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        stream.Position = 0;
        var context = new AssemblyLoadContext(Guid.NewGuid().ToString("N"), isCollectible: true);
        try
        {
            var loaded = context.LoadFromStream(stream);
            var runtimeMethod = loaded.GetType("Owner")!.GetMethod("Old")!;
            var obsolete = Assert.IsType<ObsoleteAttribute>(
                Assert.Single(runtimeMethod.GetCustomAttributes(typeof(ObsoleteAttribute), inherit: false)));
            Assert.Equal("Deprecated since 1.4.0. Use Rewrite instead", obsolete.Message);
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void ContractSimplificationRetainsMethodMetadataAndEffectBinders()
    {
        var original = Parse("""
            module Api
              module Owner
                /// since 1.2.0
                /// deprecated 1.4.0 Rewrite
                /// breaking 2.0.0 "New behavior"
                func Old <! e> () → int
                  ! e
                  => true && true
                  return 7
            """);
        var before = original.Classes.Single().Methods.Single();
        var simplified = new ContractSimplificationPass(new DiagnosticBag()).Simplify(original);
        var after = simplified.Classes.Single().Methods.Single();
        Assert.NotSame(before, after);
        Assert.Same(before.Since, after.Since);
        Assert.Same(before.Deprecated, after.Deprecated);
        Assert.Same(before.BreakingChanges, after.BreakingChanges);
        Assert.Same(before.EffectParameters, after.EffectParameters);
    }

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        return module;
    }
}
