using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Binding.BoundTypes;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public class LocalReferenceAnnotationTests
{
    public static IEnumerable<object[]> ReferenceChains()
    {
        foreach (var producer in new[] { "new", "native", "native-nullable", "bcl", "bcl-nullable" })
        foreach (var depth in new[] { 0, 1, 2 })
        foreach (var boundary in new[] { "binding", "return", "argument" })
            yield return [producer, depth, boundary];
    }

    [Theory]
    [MemberData(nameof(ReferenceChains))]
    public void EstablishedReferenceAnnotation_SurvivesInferredLocals(
        string producer, int depth, string boundary)
    {
        var isBcl = producer.StartsWith("bcl", StringComparison.Ordinal);
        var nullable = producer.EndsWith("-nullable", StringComparison.Ordinal);
        var receivingType = isBcl ? "System.IO.DirectoryInfo" : "Foo";
        var annotation = nullable ? NullableAnnotation.Annotated : NullableAnnotation.NotAnnotated;
        var expression = producer switch
        {
            "new" => "§NEW{Foo}",
            "native" or "native-nullable" => "§C{Foo.Get} §A input §/C",
            "bcl" => "§C{System.IO.Directory.CreateDirectory} §A STR:\"/\" §/C",
            "bcl-nullable" => "§C{System.IO.Directory.GetParent} §A STR:\"/\" §/C",
            _ => throw new ArgumentOutOfRangeException(nameof(producer))
        };
        var statements = new List<string>();
        for (var i = 0; i < depth; i++)
        {
            statements.Add($"§B{{local{i}}} {expression}");
            expression = $"local{i}";
        }
        statements.Add(boundary switch
        {
            "binding" => $"§B{{result:{receivingType}}} {expression}",
            "return" => $"§R {expression}",
            "argument" => $"§R §C{{{(isBcl ? "System.IO.FileSystemAclExtensions.GetAccessControl" : "Foo.Consume")}}} §A {expression} §/C",
            _ => throw new ArgumentOutOfRangeException(nameof(boundary))
        });
        var body = string.Join("\n      ", statements);
        var output = boundary == "return" ? receivingType
            : boundary == "argument" ? isBcl ? "System.Security.AccessControl.DirectorySecurity" : "i32"
            : "void";
        var source = $$"""
            §M{m1:LocalReferences}
              §CL{c1:Foo:pub}
                §MT{get:Get:pub:static} ({{(nullable ? "?Foo" : "Foo")}}:input) -> {{(nullable ? "?Foo" : "Foo")}}
                  §R input
                §MT{consume:Consume:pub:static} ({{receivingType}}:value) -> i32
                  §R 0
                §MT{probe:Probe:pub:static} ({{(nullable ? "?Foo" : "Foo")}}:input) -> {{output}}
                  {{body}}
            """;
        var (module, diagnostics) = Bind(source);
        var probe = Probe(module);
        foreach (var local in probe.Body.Take(depth).Cast<BoundBindStatement>())
            Assert.Equal(annotation, local.Variable.NullableAnnotation);
        var consumed = ConsumerValue(probe.Body.Last(), boundary);
        Assert.Equal(annotation, Assert.IsType<NominalBoundType>(consumed.Type).NullableAnnotation);
        if (consumed is BoundNewExpression constructed)
            Assert.NotNull(constructed.ResolvedType);
        else
        {
            var nominal = Assert.IsType<NominalBoundType>(consumed.Type);
            Assert.True(nominal.IsKnownReferenceType);
            if (isBcl)
                Assert.Equal("DirectoryInfo", nominal.RoslynSymbol?.Name);
            else
                Assert.Equal("Foo", nominal.Declaration?.QualifiedName);
        }

        var expectedCode = boundary switch
        {
            "binding" => DiagnosticCode.NullableToNonNullableBinding,
            "return" => DiagnosticCode.NullableReturnFromNonNullable,
            _ => DiagnosticCode.NullableArgumentToNonNullableParameter
        };
        var findings = diagnostics.Where(d => d.Code == expectedCode).ToArray();
        Assert.Equal(nullable ? 1 : 0, findings.Length);
        Assert.DoesNotContain(diagnostics, d => d.Code is
            DiagnosticCode.NoMatchingOverload or DiagnosticCode.UndefinedReference);
        foreach (var finding in findings)
        {
            Assert.Equal(consumed.Span, finding.Span);
            Assert.False(BindingDiagnosticPolicy.IsCompilationError(finding));
        }
    }

    [Theory]
    [InlineData("field", "Foo", false)]
    [InlineData("field", "?Foo", true)]
    [InlineData("property", "Foo", false)]
    [InlineData("property", "?Foo", true)]
    [InlineData("field", "string", false)]
    [InlineData("field", "?string", true)]
    [InlineData("property", "string", false)]
    [InlineData("property", "?string", true)]
    public void NativeMember_BareThisAndBaseKeepTheSameResolvedAnnotation(
        string kind, string typeName, bool nullable)
    {
        var member = kind == "field"
            ? $"§FLD{{{typeName}:Value:pub}}"
            : $"§PROP{{p1:Value:{typeName}:pub:get,set}}";
        var receivingType = typeName.TrimStart('?');
        var source = $$"""
            §M{m1:NativeMemberAnnotations}
              §CL{c1:Foo:pub}
              §CL{c2:Parent:pub}
                {{member}}
              §CL{c3:Child:pub}
                §EXT{Parent}
                §MT{probe:Probe:pub} () -> void
                  §B{bare:{{receivingType}}} Value
                  §B{viaThis:{{receivingType}}} §THIS.Value
                  §B{viaBase:{{receivingType}}} §BASE.Value
            """;
        var (module, diagnostics) = Bind(source);
        var statements = Probe(module).Body.Cast<BoundBindStatement>().ToArray();
        var bare = Assert.IsType<BoundVariableExpression>(statements[0].Initializer);
        var viaThis = Assert.IsType<BoundFieldAccessExpression>(statements[1].Initializer);
        var viaBase = Assert.IsType<BoundFieldAccessExpression>(statements[2].Initializer);
        Assert.Equal(bare.Variable.Id, viaThis.ResolvedSymbolId);
        Assert.Equal(bare.Variable.Id, viaBase.ResolvedSymbolId);
        var annotation = nullable ? NullableAnnotation.Annotated : NullableAnnotation.NotAnnotated;
        foreach (var statement in statements)
        {
            var value = Assert.IsType<NominalBoundType>(statement.Initializer!.Type);
            Assert.Equal(annotation, value.NullableAnnotation);
            if (receivingType == "Foo")
                Assert.Equal("Foo", value.Declaration?.QualifiedName);
        }
        Assert.Equal(nullable ? 3 : 0,
            diagnostics.Count(d => d.Code == DiagnosticCode.NullableToNonNullableBinding));
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.UndefinedReference);
    }

    [Theory]
    [InlineData("A.Foo", true, true)]
    [InlineData("B.Foo", true, false)]
    [InlineData("A.Foo", false, false)]
    [InlineData("B.Foo", false, false)]
    public void InferredIdentity_StaysInTheProducerNamespaceAcrossTwoLocals(
        string receivingType, bool nullable, bool expectedFinding)
    {
        var sourceA = $$"""
            §M{m1:Namespaces}
              §CL{c1:Foo:pub}
                §MT{get:Get:pub:static} () -> {{(nullable ? "?Foo" : "Foo")}}
                  §R §NEW{Foo}
            """;
        var sourceB = $$"""
            §M{m1:Namespaces}
              §CL{c2:Foo:pub}
                §MT{probe:Probe:pub:static} () -> void
                  §B{first} §C{A.Foo.Get} §/C
                  §B{second} first
                  §B{result:{{receivingType}}} second
            """;
        var parsedA = Parse(sourceA);
        var parsedB = Parse(sourceB);
        var classA = Assert.Single(parsedA.Classes);
        var classB = Assert.Single(parsedB.Classes);
        classA.NamespaceIdentity = "A";
        classB.NamespaceIdentity = "B";
        var parsed = new ModuleNode(parsedA.Span, parsedA.Id, parsedA.Name,
            parsedA.Usings, parsedA.Interfaces, [classA, classB], parsedA.Functions, parsedA.Attributes);
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(parsed);
        var probe = Probe(bound);
        foreach (var local in probe.Body.Take(2).Cast<BoundBindStatement>())
            Assert.Equal("A.Foo", local.Variable.InferredReferenceType?.Declaration?.QualifiedName);
        var consumed = Assert.IsType<BoundVariableExpression>(
            Assert.IsType<BoundBindStatement>(probe.Body.Last()).Initializer);
        var type = Assert.IsType<NominalBoundType>(consumed.Type);
        Assert.Equal("Foo", type.DisplayString);
        Assert.Equal("A.Foo", type.Declaration?.QualifiedName);
        Assert.Equal(nullable ? NullableAnnotation.Annotated : NullableAnnotation.NotAnnotated, type.NullableAnnotation);
        Assert.Equal(expectedFinding,
            diagnostics.Any(d => d.Code == DiagnosticCode.NullableToNonNullableBinding));
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.NoMatchingOverload);
    }

    [Theory]
    [InlineData("Foo", NullableAnnotation.NotAnnotated)]
    [InlineData("?Foo", NullableAnnotation.Annotated)]
    public void ExplicitTargetAnnotation_IsNotReplacedByInitializerAnnotation(
        string declaredType, NullableAnnotation expected)
    {
        var source = $$"""
            §M{m1:DeclaredReferences}
              §CL{c1:Foo:pub}
                §MT{probe:Probe:pub:static} () -> void
                  §B{first:{{declaredType}}} §NEW{Foo}
                  §B{second} first
                  §B{third} second
            """;
        var (module, _) = Bind(source);
        var bindings = Probe(module).Body.Cast<BoundBindStatement>().ToArray();
        Assert.Null(bindings[0].Variable.InferredReferenceType);
        foreach (var binding in bindings)
            Assert.Equal(expected, binding.Variable.NullableAnnotation);
        Assert.Equal(expected, Assert.IsType<NominalBoundType>(bindings[2].Initializer!.Type).NullableAnnotation);
    }

    [Theory]
    [InlineData("Foo")]
    [InlineData("?Foo")]
    public void UnmodeledNominalConditional_IsNotUpgradedToNonNull(string rightType)
    {
        var source = $$"""
            §M{m1:ConditionalLimit}
              §CL{c1:Foo:pub}
                §MT{probe:Probe:pub:static} (bool:choose, Foo:left, {{rightType}}:right) -> void
                  §B{first} (? choose left right)
                  §B{second} first
            """;
        var (module, _) = Bind(source);
        var bindings = Probe(module).Body.Cast<BoundBindStatement>().ToArray();
        Assert.IsType<BoundConditionalExpression>(bindings[0].Initializer);
        foreach (var binding in bindings)
        {
            Assert.Equal(NullableAnnotation.Oblivious, binding.Variable.NullableAnnotation);
            Assert.Null(binding.Variable.InferredReferenceType);
        }
        Assert.Equal(NullableAnnotation.Oblivious,
            Assert.IsType<NominalBoundType>(bindings[1].Initializer!.Type).NullableAnnotation);
    }

    [Theory]
    [InlineData("INT:1")]
    [InlineData("§C{System.Guid.NewGuid} §/C")]
    [InlineData("§C{Unknown.Get} §/C")]
    public void ValueAndUnresolvedSources_DoNotAcquireReferenceIdentity(string expression)
    {
        var source = $$"""
            §M{m1:ExcludedSources}
              §CL{c1:Holder:pub}
                §MT{probe:Probe:pub:static} () -> void
                  §B{first} {{expression}}
                  §B{second} first
            """;
        var (module, _) = Bind(source);
        foreach (var binding in Probe(module).Body.Cast<BoundBindStatement>())
        {
            Assert.Null(binding.Variable.InferredReferenceType);
            Assert.Equal(NullableAnnotation.Oblivious, binding.Variable.NullableAnnotation);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ResolvedBclString_LocalChainKeepsItsExactAnnotation(bool nullable)
    {
        var call = nullable
            ? "§C{System.Environment.GetEnvironmentVariable} §A STR:\"CALOR_N0_UNSET\" §/C"
            : "§C{System.IO.Directory.GetCurrentDirectory} §/C";
        var source = $$"""
            §M{m1:StringLocalRegression}
              §CL{c1:Holder:pub}
                §MT{probe:Probe:pub:static} () -> string
                  §B{first} {{call}}
                  §B{second} first
                  §R second
            """;
        var (module, diagnostics) = Bind(source);
        var probe = Probe(module);
        var expected = nullable ? NullableAnnotation.Annotated : NullableAnnotation.NotAnnotated;
        var producer = Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundBindStatement>(probe.Body[0]).Initializer);
        Assert.NotNull(Assert.IsType<NominalBoundType>(producer.Type).RoslynSymbol);
        foreach (var binding in probe.Body.Take(2).Cast<BoundBindStatement>())
            Assert.Equal(expected, binding.Variable.NullableAnnotation);
        Assert.Equal(nullable,
            diagnostics.Any(d => d.Code == DiagnosticCode.NullableReturnFromNonNullable));
    }

    [Theory]
    [InlineData("_top", true)]
    [InlineData("_top", false)]
    [InlineData("_current", true)]
    [InlineData("_current", false)]
    public void ReducedEnricherStackFieldPattern_PreservesResolvedInterfaceThroughLocals(
        string memberName, bool nullable)
    {
        // Reduced from Serilog Context/EnricherStack.cs's classified _top/_current
        // nullable-interface field reads. The local interface makes resolution
        // explicit; the original per-file corpus still has an unresolved interface.
        var source = $$"""
            §M{m1:EnricherFieldReduction}
              §IFACE{i1:ILogEventEnricher:pub}
              §CL{c1:Holder:pub}
                §FLD{ {{(nullable ? "?ILogEventEnricher" : "ILogEventEnricher")}}:{{memberName}}:priv}
                §MT{probe:Probe:pub} () -> ILogEventEnricher
                  §B{first} §THIS.{{memberName}}
                  §B{second} first
                  §R second
            """;
        var (module, diagnostics) = Bind(source);
        var probe = Probe(module);
        var annotation = nullable ? NullableAnnotation.Annotated : NullableAnnotation.NotAnnotated;
        foreach (var binding in probe.Body.Take(2).Cast<BoundBindStatement>())
        {
            Assert.Equal(annotation, binding.Variable.NullableAnnotation);
            Assert.Equal("ILogEventEnricher", binding.Variable.InferredReferenceType?.Declaration?.QualifiedName);
        }
        Assert.Equal(nullable,
            diagnostics.Any(d => d.Code == DiagnosticCode.NullableReturnFromNonNullable));
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.UndefinedReference);
    }

    [Fact]
    public void NullableBclReturnToNativeInput_RetainsExistingApplicabilityRejection()
    {
        const string source = """
            §M{m1:RetainedNativeInputLimit}
              §CL{c1:Holder:pub}
                §MT{consume:Consume:pub:static} (System.IO.DirectoryInfo:value) -> i32
                  §R 0
                §MT{probe:Probe:pub:static} () -> i32
                  §R §C{Holder.Consume} §A §C{System.IO.Directory.GetParent} §A STR:"/" §/C §/C
            """;
        var (module, diagnostics) = Bind(source);
        var call = Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundReturnStatement>(Assert.Single(Probe(module).Body)).Expression);
        Assert.Null(call.ResolvedSymbol);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.NoMatchingOverload);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter);
    }

    private static BoundExpression ConsumerValue(BoundStatement statement, string boundary)
    {
        var expression = statement switch
        {
            BoundBindStatement binding => binding.Initializer,
            BoundReturnStatement returned => returned.Expression,
            _ => throw new InvalidOperationException("Expected a binding or return.")
        };
        Assert.NotNull(expression);
        if (boundary != "argument")
            return expression;
        var consumer = Assert.IsType<BoundCallExpression>(expression);
        if (consumer.Target == "System.IO.FileSystemAclExtensions.GetAccessControl")
            Assert.Equal("DirectorySecurity", Assert.IsType<NominalBoundType>(consumer.Type).RoslynSymbol?.Name);
        else
            Assert.NotNull(consumer.ResolvedSymbol);
        return Assert.Single(consumer.Arguments);
    }

    private static BoundFunction Probe(BoundModule module) =>
        Assert.Single(module.Functions.Where(f => f.Symbol.Name.EndsWith(".Probe", StringComparison.Ordinal)));

    private static (BoundModule Module, DiagnosticBag Diagnostics) Bind(string source)
    {
        var diagnostics = new DiagnosticBag();
        return (new Binder(diagnostics).Bind(Parse(source)), diagnostics);
    }

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var parsed = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics));
        return parsed;
    }
}
