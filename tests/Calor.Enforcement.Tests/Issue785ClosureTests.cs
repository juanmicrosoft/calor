using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Effects.Manifests;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Enforcement.Tests;

public sealed class Issue785ClosureTests
{
    [Fact]
    public void NewExpression_ChargesAllocationAndSignatureResolvedConstructorEffects()
    {
        var diagnostics = Enforce(
            """
            module Test Test block
              func CreateInt () → void
                ! alloc, cw
                let Example.Widget:w = new Example.Widget(1)

              func CreateString () → void
                ! alloc, fs:r
                let Example.Widget:w = new Example.Widget("x")
            """,
            """
            {
              "version": "1.0",
              "mappings": [{
                "type": "Example.Widget",
                "constructors": {
                  "(Int32)": ["cw"],
                  "(String)": ["fs:r"]
                }
              }]
            }
            """);

        Assert.DoesNotContain(diagnostics.Errors,
            diagnostic => diagnostic.Code is DiagnosticCode.ForbiddenEffect
                or DiagnosticCode.UnknownExternalCall);
    }

    [Fact]
    public void NewExpression_UnresolvedConstructorFailsClosed()
    {
        var diagnostics = Enforce(
            """
            module Test Test block
              func Create () → void
                ! alloc
                let Missing.Widget:x = new Missing.Widget()
            """);

        Assert.Contains(diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall
                && diagnostic.Message.Contains("Missing.Widget..ctor"));
        Assert.Contains(diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("unknown"));
    }

    [Fact]
    public void InModuleConstructor_PropagatesMutationAndAllowsIntrinsicInitialization()
    {
        var diagnostics = Enforce(
            """
            module Test Test block
              class Person
                field string:_name private
                ctor (string:name)
                  set this . _name name

              func Create () → void
                ! alloc, mut
                let Person:p = new Person("Ada")
            """);

        Assert.DoesNotContain(diagnostics.Errors,
            diagnostic => diagnostic.Code is DiagnosticCode.ForbiddenEffect
                or DiagnosticCode.ConstructorEffectContractUnavailable);
    }

    [Fact]
    public void EffectfulConstructorBody_FailsClosedWithoutDeclarationSurface()
    {
        var diagnostics = Enforce(
            """
            module Test Test block
              class Reporter
                ctor ()
                  Console.WriteLine("created")
            """);

        Assert.Contains(diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.ConstructorEffectContractUnavailable
                && diagnostic.Message.Contains("cw"));
    }

    [Fact]
    public void ObjectInitializer_ChargesMutationAndResolvedSetterEffects()
    {
        var diagnostics = Enforce(
            """
            module Test Test block
              func Create () → void
                ! alloc, mut
                let Example.Person:p = new Example.Person() { Name = "Ada" }
            """,
            """
            {
              "version": "1.0",
              "mappings": [{
                "type": "Example.Person",
                "constructors": { "()": [] },
                "setters": { "Name": ["cw"] }
              }]
            }
            """);

        Assert.Contains(diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("cw"));
        Assert.DoesNotContain(diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
    }

    [Fact]
    public void CustomInitializerSetterBody_IsInferredAndFailsClosed()
    {
        var diagnostics = Enforce(
            """
            module Test Test block
              class Person
                property string:Name
                  set
                    Console.WriteLine("setting")

              func Create () → void
                ! alloc, mut
                let Person:p = new Person() { Name = "Ada" }
            """);

        Assert.Contains(diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.AccessorEffectContractUnavailable
                && diagnostic.Message.Contains("set accessor"));
        Assert.Contains(diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("cw"));
    }

    [Fact]
    public void UsingStatement_ChargesResolvedDisposeEffects()
    {
        var diagnostics = Enforce(
            """
            module Test Test block
              func Use () → void
                ! alloc
                using Example.Resource:r = new Example.Resource()
            """,
            """
            {
              "version": "1.0",
              "mappings": [{
                "type": "Example.Resource",
                "constructors": { "()": [] },
                "methods": { "Dispose": ["fs:w"] }
              }]
            }
            """);

        Assert.Contains(diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("fs:w"));
        Assert.DoesNotContain(diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
    }

    [Fact]
    public void UsingStatement_UnresolvedDisposeFailsClosed()
    {
        var diagnostics = Enforce(
            """
            module Test Test block
              func Use (Missing.Resource:r) → void
                !
                using Missing.Resource:x = r
            """);

        Assert.Contains(diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall
                && diagnostic.Message.Contains("Dispose"));
        Assert.Contains(diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("unknown"));
    }

    [Fact]
    public void StaticExternalCall_CannotAliasSameNamedInternalFunction()
    {
        var diagnostics = Enforce(
            """
            module Test Test block
              func WriteLine () → void
                !
                return

              func Run () → void
                !
                Console.WriteLine("external")
            """);

        Assert.Contains(diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("cw"));
    }

    [Fact]
    public void ImplicitBaseConstructorEffects_AreCharged()
    {
        var diagnostics = Enforce(
            """
            module Test Test block
              class Base
                ctor ()
                  Console.WriteLine("base")

              class Derived internal
                extends Base

                ctor ()

              func Create () → void
                ! alloc
                let Derived:x = new Derived()
            """);

        Assert.Contains(diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("cw"));
    }

    [Fact]
    public void GenericSignatureNormalization_CanonicalizesCalorAndClrSpellings()
    {
        Assert.Equal(
            EffectResolver.NormalizeParameterType("List<i32>"),
            EffectResolver.NormalizeParameterType(
                "System.Collections.Generic.List`1<System.Int32>"));
        Assert.Equal("List`1<Int32>", EffectResolver.NormalizeParameterType("List<i32>"));
    }

    [Fact]
    public void GenericManifestSignature_ResolvesAcrossClrAndCalorSpellings()
    {
        var loader = new ManifestLoader();
        loader.LoadFromJson(
            """
            {
              "version": "1.0",
              "mappings": [{
                "type": "Example.GenericApi",
                "methods": {
                  "Transform(System.Collections.Generic.List`1<System.Int32>)": ["cw"]
                },
                "constructors": {
                  "(System.Collections.Generic.List`1<System.Int32>)": ["fs:r"]
                }
              }]
            }
            """,
            "generic-signature");
        var resolver = new EffectResolver(loader);

        Assert.True(resolver.Resolve(EffectResolverKey.FromStrings("Example.GenericApi", "Transform", ["List<i32>"])).Effects.Contains(EffectKind.IO, "console_write"));
        Assert.True(resolver.Resolve(EffectResolverKey.FromStrings("Example.GenericApi", ".ctor", ["List<i32>"], EffectMemberKind.Constructor)).Effects.Contains(EffectKind.IO, "filesystem_read"));
    }

    [Fact]
    public void ConstructedGenericArgument_UsesOverloadSpecificEffects()
    {
        // The overload-specific entry for a constructed generic argument wins over
        // the name-only wildcard, across CLR and Calor spellings of the argument.
        const string manifest = """
            {
              "version": "1.0",
              "mappings": [
                {
                  "type": "Example.Box",
                  "constructors": { "()": [] }
                },
                {
                  "type": "Example.Api",
                  "methods": {
                    "Transform": [],
                    "Transform(Example.Box`1<System.Int32>)": ["cw"]
                  }
                }
              ]
            }
            """;
        var loader = new ManifestLoader();
        loader.LoadFromJson(manifest, "issue-785-test");
        var resolver = new EffectResolver(loader);
        foreach (var spelling in new[] { "Example.Box`1<System.Int32>", "Example.Box<int>", "Example.Box<i32>" })
        {
            var specific = resolver.Resolve(EffectResolverKey.FromStrings("Example.Api", "Transform", [spelling]));
            Assert.Equal(EffectResolutionStatus.Resolved, specific.Status);
            Assert.True(specific.Effects.Contains(EffectKind.IO, "console_write"));
        }
        var other = resolver.Resolve(EffectResolverKey.FromStrings("Example.Api", "Transform", ["Example.Box<string>"]));
        Assert.Equal(EffectResolutionStatus.PureExplicit, other.Status);
        Assert.True(other.Effects.IsEmpty);

        // End to end, 0.30 charges a native member call only through the
        // metadata method the binder selected. Example.Api is absent from the
        // binder's metadata, and decision D3 rules out a name-only manifest
        // fallback, so the call fails closed as Unknown instead of reading either
        // entry by spelling.
        var diagnostics = Enforce(
            """
            module Test Test block
              func Run () → void
                ! alloc
                Example.Api.Transform(new Example.Box<int>())
            """,
            manifest);

        Assert.Contains(diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall
                && diagnostic.Message.Contains(".Transform'", StringComparison.Ordinal));
        Assert.Contains(diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("'unknown'", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("'cw'", StringComparison.Ordinal));
    }

    [Fact]
    public void InheritedCustomGetterEffects_AreCharged()
    {
        var diagnostics = Enforce(
            """
            module Test Test block
              func Read (Derived:x) → void
                !
                let string:value = x.Name

              class Base
                property string:Name
                  get
                    Console.WriteLine("get")
                    return "value"

              class Derived internal
                extends Base
            """);

        Assert.Contains(diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("cw"));
    }

    [Fact]
    public void StaticConstructor_DoesNotChargeInstanceBaseConstructor()
    {
        var diagnostics = Enforce(
            """
            module Test Test block
              class Base
                ctor ()
                  Console.WriteLine("instance")

              class Derived internal
                extends Base

                ctor static ()
            """);

        Assert.DoesNotContain(diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.ConstructorEffectContractUnavailable
                && diagnostic.Message.Contains("Derived")
                && diagnostic.Message.Contains("cw"));
    }

    [Fact]
    public void GenericOverloads_PreserveCustomTypeNamespaces()
    {
        var loader = new ManifestLoader();
        loader.LoadFromJson(
            """
            {
              "version": "1.0",
              "mappings": [{
                "type": "Example.Api",
                "methods": {
                  "Transform(A.Box`1<System.Int32>)": ["cw"],
                  "Transform(B.Box`1<System.Int32>)": ["fs:r"]
                }
              }]
            }
            """,
            "namespaced-generics");
        var resolver = new EffectResolver(loader);

        Assert.True(resolver.Resolve(EffectResolverKey.FromStrings("Example.Api", "Transform", ["A.Box<i32>"])).Effects.Contains(EffectKind.IO, "console_write"));
        Assert.True(resolver.Resolve(EffectResolverKey.FromStrings("Example.Api", "Transform", ["B.Box<i32>"])).Effects.Contains(EffectKind.IO, "filesystem_read"));
    }

    [Fact]
    public void UnrelatedReceiver_DoesNotUseBareLinqExtensionFallback()
    {
        var loader = new ManifestLoader();
        loader.LoadFromJson(
            """
            {
              "version": "1.0",
              "mappings": [{
                "type": "System.Linq.Enumerable",
                "extensionProvider": true,
                "methods": { "Select": [] }
              }]
            }
            """,
            "linq-extension");
        var resolver = new EffectResolver(loader);

        Assert.Equal(
            EffectResolutionStatus.Unknown,
            resolver.Resolve(EffectResolverKey.FromStrings("Vendor.Writer", "Select", kind: EffectMemberKind.Extension)).Status);
    }

    [Fact]
    public void ExternalGetterAndSetterEffects_AreCharged()
    {
        var diagnostics = Enforce(
            """
            module Test Test block
              func Use (Vendor.Widget:w) → void
                ! mut
                let string:name = w.Name
                set w.Name "updated"
            """,
            """
            {
              "version": "1.0",
              "mappings": [{
                "type": "Vendor.Widget",
                "getters": { "Name": ["fs:r"] },
                "setters": { "Name": ["cw"] }
              }]
            }
            """);

        Assert.Contains(diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("fs:r"));
        Assert.Contains(diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("cw"));
    }

    [Fact]
    public void ArrayCreation_ChargesAllocation()
    {
        var diagnostics = Enforce(
            """
            module Test Test block
              func Create () → void
                !
                let int[]:items = new items[1]
            """);

        Assert.Contains(diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("alloc"));
    }

    private static DiagnosticBag Enforce(string source, string? manifestJson = null)
    {
        var diagnostics = Parse(source, out var module);
        Assert.False(diagnostics.HasErrors,
            $"Parse failed: {string.Join("; ", diagnostics.Errors.Select(d => d.Message))}");

        EffectResolver? resolver = null;
        if (manifestJson != null)
        {
            var loader = new ManifestLoader();
            loader.LoadFromJson(manifestJson, "issue-785-test");
            resolver = new EffectResolver(loader);
        }

        var pass = new EffectEnforcementPass(
            diagnostics,
            UnknownCallPolicy.Strict,
            resolver);
        pass.Enforce(module);
        return diagnostics;
    }

    private static DiagnosticBag Parse(string source, out ModuleNode module)
    {
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        module = new Parser(tokens, diagnostics).Parse();
        return diagnostics;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
