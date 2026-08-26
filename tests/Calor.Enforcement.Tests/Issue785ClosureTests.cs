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
            §M{m1:Test}
              §F{f1:CreateInt:pub}
                §O{void}
                §E{alloc,cw}
                §B{w:Example.Widget} §NEW{Example.Widget} §A INT:1 §/NEW
              §F{f2:CreateString:pub}
                §O{void}
                §E{alloc,fs:r}
                §B{w:Example.Widget} §NEW{Example.Widget} §A STR:"x" §/NEW
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
            §M{m1:Test}
              §F{f1:Create:pub}
                §O{void}
                §E{alloc}
                §B{x:Missing.Widget} §NEW{Missing.Widget} §/NEW
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
            §M{m1:Test}
              §CL{c1:Person:pub}
                §FLD{str:_name:pri}
                §CTOR{ctor1:pub}
                  §I{str:name}
                  §ASSIGN §THIS._name name
                §/CTOR{ctor1}
              §F{f1:Create:pub}
                §O{void}
                §E{alloc,mut}
                §B{p:Person} §NEW{Person} §A STR:"Ada" §/NEW
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
            §M{m1:Test}
              §CL{c1:Reporter:pub}
                §CTOR{ctor1:pub}
                  §P "created"
                §/CTOR{ctor1}
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
            §M{m1:Test}
              §F{f1:Create:pub}
                §O{void}
                §E{alloc,mut}
                §B{p:Example.Person} §NEW{Example.Person}
                  §INIT{Name} STR:"Ada"
                §/NEW
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
    public void AutoPropertyObjectInitializer_ChargesMutation()
    {
        var diagnostics = Enforce(
            """
            §M{m1:Test}
              §CL{c1:Person:pub}
                §PROP{p1:Name:str:pub:get,set}
              §F{f1:Create:pub}
                §O{void}
                §E{alloc}
                §B{p:Person} §NEW{Person}
                  §INIT{Name} STR:"Ada"
                §/NEW
            """);

        Assert.Contains(diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("mut"));
        Assert.DoesNotContain(diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
    }

    [Fact]
    public void CustomInitializerSetterBody_IsInferredAndFailsClosed()
    {
        var diagnostics = Enforce(
            """
            §M{m1:Test}
              §CL{c1:Person:pub}
                §PROP{p1:Name:str:pub}
                  §SET
                    §P "setting"
                  §/SET
                §/PROP{p1}
              §F{f1:Create:pub}
                §O{void}
                §E{alloc,mut}
                §B{p:Person} §NEW{Person}
                  §INIT{Name} STR:"Ada"
                §/NEW
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
            §M{m1:Test}
              §F{f1:Use:pub}
                §O{void}
                §E{alloc}
                §USE{u1:r:Example.Resource} §NEW{Example.Resource} §/NEW
                §/USE{u1}
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
            §M{m1:Test}
              §F{f1:Use:pub}
                §I{Missing.Resource:r}
                §O{void}
                §E{}
                §USE{u1:x:Missing.Resource} r
                §/USE{u1}
            """);

        Assert.Contains(diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall
                && diagnostic.Message.Contains("Dispose"));
        Assert.Contains(diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("unknown"));
    }

    [Fact]
    public void CustomEventAccessorEffects_AreEnforcedAndChargedAtSubscription()
    {
        var diagnostics = Enforce(
            """
            §M{m1:Test}
              §CL{c1:Publisher:pub}
                §EVT{e1:Changed:pub:EventHandler}
                  §EADD
                    §P "adding"
                  §/EADD
                  §EREM
                  §/EREM
                §/EVT{e1}
              §F{f1:Subscribe:pub}
                §I{Publisher:p}
                §I{EventHandler:h}
                §O{void}
                §E{mut}
                §SUB p.Changed h
            """);

        Assert.Contains(diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.AccessorEffectContractUnavailable
                && diagnostic.Message.Contains("add accessor"));
        Assert.Contains(diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("cw"));
    }

    [Fact]
    public void FieldStyleEventSubscription_ChargesMutationWithoutUnknown()
    {
        var diagnostics = Enforce(
            """
            §M{m1:Test}
              §CL{c1:Publisher:pub}
                §EVT{e1:Changed:pub:EventHandler}
              §F{f1:Subscribe:pub}
                §I{Publisher:p}
                §I{EventHandler:h}
                §O{void}
                §E{mut}
                §SUB p.Changed h
            """);

        Assert.DoesNotContain(diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
        Assert.DoesNotContain(diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
    }

    [Fact]
    public void CustomRemoveAccessorEffects_AreChargedAtUnsubscription()
    {
        var diagnostics = Enforce(
            """
            §M{m1:Test}
              §CL{c1:Publisher:pub}
                §EVT{e1:Changed:pub:EventHandler}
                  §EADD
                  §/EADD
                  §EREM
                    §P "removing"
                  §/EREM
                §/EVT{e1}
              §F{f1:Unsubscribe:pub}
                §I{Publisher:p}
                §I{EventHandler:h}
                §O{void}
                §E{mut}
                §UNSUB p.Changed h
            """);

        Assert.Contains(diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.AccessorEffectContractUnavailable
                && diagnostic.Message.Contains("remove accessor"));
        Assert.Contains(diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("cw"));
    }

    [Fact]
    public void SourceEffectTypo_ProducesDedicatedDiagnostic()
    {
        var diagnostics = Parse(
            """
            §M{m1:Test}
              §F{f1:Run:pub}
                §O{void}
                §E{fs:x}
            """,
            out _);

        Assert.Contains(diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.UnknownEffectCode
                && diagnostic.Message.Contains("fs:x"));
    }

    [Fact]
    public void FullTaxonomy_RoundTripsAcrossSourceSetManifestEmitterAndDocs()
    {
        var docs = File.ReadAllText(Path.Combine(FindRepositoryRoot(),
            "docs", "syntax-reference", "effects.md"));
        foreach (var code in EffectCodes.KnownCompactCodes)
        {
            Assert.True(EffectCodes.TryParseCompact(
                code,
                out var kind,
                out var category,
                out var value));

            var effectSet = EffectSet.From(code);
            Assert.True(effectSet.Contains(kind, value));
            Assert.Contains(code, effectSet.ToDisplayString());

            var sourceDiagnostics = Parse(
                $"§M{{m1:Test}}\n  §F{{f1:Run:pub}}\n    §O{{void}}\n    §E{{{code}}}\n",
                out var module);
            Assert.DoesNotContain(sourceDiagnostics.Errors,
                diagnostic => diagnostic.Code == DiagnosticCode.UnknownEffectCode);

            var emitted = new CalorEmitter().Visit(module.Functions[0].Effects!);
            Assert.Equal($"§E{{{code}}}", emitted);

            var loader = new ManifestLoader();
            loader.LoadFromJson(
                $$"""
                {
                  "version": "1.0",
                  "mappings": [{
                    "type": "Example.Type",
                    "methods": { "Run": ["{{code}}"] }
                  }]
                }
                """,
                $"taxonomy-{code}");
            Assert.Empty(loader.ValidateManifests());
        }

        foreach (var code in EffectCodes.DocumentedCompactCodes)
            Assert.Contains($"`{code}`", docs);
    }

    [Fact]
    public void StaticExternalCall_CannotAliasSameNamedInternalFunction()
    {
        var diagnostics = Enforce(
            """
            §M{m1:Test}
              §F{f1:WriteLine:pub}
                §O{void}
                §E{}
              §F{f2:Run:pub}
                §O{void}
                §E{}
                §C{Console.WriteLine} §A STR:"external" §/C
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
            §M{m1:Test}
              §CL{c1:Base:pub}
                §CTOR{ctor1:pub}
                  §P "base"
                §/CTOR{ctor1}
              §CL{c2:Derived:Base:pub}
                §CTOR{ctor2:pub}
                §/CTOR{ctor2}
              §F{f1:Create:pub}
                §O{void}
                §E{alloc}
                §B{x:Derived} §NEW{Derived} §/NEW
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
        var diagnostics = Enforce(
            """
            §M{m1:Test}
              §F{f1:Run:pub}
                §O{void}
                §E{alloc}
                §C{Example.Api.Transform}
                  §A §NEW{Example.Box<i32>} §/NEW
                §/C
            """,
            """
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
            """);

        Assert.Contains(diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("cw"));
    }

    [Fact]
    public void InheritedDisposeEventAndInitializerMembers_AreResolved()
    {
        var diagnostics = Enforce(
            """
            §M{m1:Test}
              §F{f1:Use:pub}
                §I{Derived:x}
                §I{EventHandler:h}
                §O{void}
                §E{mut,cw}
                §SUB x.Changed h
                §USE{u1:r:Derived} x
                §/USE{u1}
              §F{f2:Create:pub}
                §O{void}
                §E{alloc,mut}
                §B{x:Derived} §NEW{Derived}
                  §INIT{Name} STR:"ok"
                §/NEW
              §CL{c1:Base:pub}
                §PROP{p1:Name:str:pub:get,set}
                §EVT{e1:Changed:pub:EventHandler}
                §MT{m1:Dispose:pub}
                  §O{void}
                  §E{cw}
                  §P "dispose"
              §CL{c2:Derived:Base:pub}
            """);

        Assert.DoesNotContain(diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
    }

    [Fact]
    public void InheritedCustomGetterEffects_AreCharged()
    {
        var diagnostics = Enforce(
            """
            §M{m1:Test}
              §F{f1:Read:pub}
                §I{Derived:x}
                §O{void}
                §E{}
                §B{value:str} x.Name
              §CL{c1:Base:pub}
                §PROP{p1:Name:str:pub}
                  §GET
                    §P "get"
                    §R STR:"value"
                  §/GET
                §/PROP{p1}
              §CL{c2:Derived:Base:pub}
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
            §M{m1:Test}
              §CL{c1:Base:pub}
                §CTOR{ctor1:pub}
                  §P "instance"
                §/CTOR{ctor1}
              §CL{c2:Derived:Base:pub}
                §CTOR{ctor2:stat}
                §/CTOR{ctor2}
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
            §M{m1:Test}
              §F{f1:Use:pub}
                §I{Vendor.Widget:w}
                §O{void}
                §E{mut}
                §B{name:str} w.Name
                §ASSIGN w.Name STR:"updated"
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
            §M{m1:Test}
              §F{f1:Create:pub}
                §O{void}
                §E{}
                §B{[i32]:items} §ARR{i32:items:1}
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
