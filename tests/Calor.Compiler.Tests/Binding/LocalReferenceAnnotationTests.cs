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
            "new" => "new Foo()",
            "native" or "native-nullable" => "Foo.Get(input)",
            "bcl" => "System.IO.Directory.CreateDirectory(\"/\")",
            "bcl-nullable" => "System.IO.Directory.GetParent(\"/\")",
            _ => throw new ArgumentOutOfRangeException(nameof(producer))
        };
        var statements = new List<string>();
        for (var i = 0; i < depth; i++)
        {
            statements.Add($"let local{i} = {expression}");
            expression = $"local{i}";
        }
        statements.Add(boundary switch
        {
            "binding" => $"let {receivingType}:result = {expression}",
            "return" => $"return {expression}",
            "argument" => $"return {(isBcl ? "System.IO.FileSystemAclExtensions.GetAccessControl" : "Foo.Consume")}({expression})",
            _ => throw new ArgumentOutOfRangeException(nameof(boundary))
        });
        var body = string.Join("\n      ", statements);
        var output = boundary == "return" ? receivingType
            : boundary == "argument" ? isBcl ? "System.Security.AccessControl.DirectorySecurity" : "int"
            : "void";
        var source = $$"""
            module LocalReferences
              class Foo public
                func Get public static ({{(nullable ? "Foo?" : "Foo")}}:input) → {{(nullable ? "Foo?" : "Foo")}}
                  !
                  return input
                func Consume public static ({{receivingType}}:value) → int
                  !
                  return 0
                func Probe public static ({{(nullable ? "Foo?" : "Foo")}}:input) → {{output}}
                  !
                  {{body}}
            """;
        var (module, diagnostics) = Bind(source);
        var probe = Probe(module);
        foreach (var local in probe.Body.Take(depth).Cast<BoundBindStatement>())
            Assert.Equal(annotation, local.Variable.NullableAnnotation);
        var consumed = ConsumerValue(probe.Body.Last(), boundary, isBcl);
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
            Assert.True(BindingDiagnosticPolicy.IsCompilationError(finding));
        }
    }

    [Theory]
    [InlineData("field", "Foo", false)]
    [InlineData("field", "Foo?", true)]
    [InlineData("property", "Foo", false)]
    [InlineData("property", "Foo?", true)]
    [InlineData("field", "string", false)]
    [InlineData("field", "string?", true)]
    [InlineData("property", "string", false)]
    [InlineData("property", "string?", true)]
    public void NativeMember_BareThisAndBaseKeepTheSameResolvedAnnotation(
        string kind, string typeName, bool nullable)
    {
        var member = kind == "field"
            ? $"field {typeName}:Value public"
            : $"property {typeName}:Value public\n      get\n      set";
        var receivingType = typeName.TrimEnd('?');
        var source = $$"""
            module NativeMemberAnnotations
              class Foo public
              class Parent public
                {{member}}
              class Child public
                extends Parent
                func Probe public () → void
                  !
                  let {{receivingType}}:bare = Value
                  let {{receivingType}}:viaThis = this.Value
                  let {{receivingType}}:viaBase = base.Value
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
            module Namespaces A
              class Foo public
                func Get public static () → {{(nullable ? "Foo?" : "Foo")}}
                  !
                  return new Foo()
            """;
        var sourceB = $$"""
            module Namespaces B
              class Foo public
                func Probe public static () → void
                  !
                  let first = A.Foo.Get()
                  let second = first
                  let {{receivingType}}:result = second
            """;
        var parsedA = Parse(sourceA);
        var parsedB = Parse(sourceB);
        var classA = Assert.Single(parsedA.Classes);
        var classB = Assert.Single(parsedB.Classes);
        Assert.Equal("A", classA.NamespaceIdentity);
        Assert.Equal("B", classB.NamespaceIdentity);
        // This composed AST retains namespace identities, not the separate files' lexical scope tables.
        classA.NamespaceScopeId = null;
        classB.NamespaceScopeId = null;
        classA.Methods.Single().NamespaceScopeId = null;
        classB.Methods.Single().NamespaceScopeId = null;
        var parsed = new ModuleNode(parsedA.Span, parsedA.Id, parsedA.Name,
            parsedA.Usings, parsedA.Interfaces, [classA, classB], parsedA.Functions, parsedA.Attributes);
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(parsed);
        var probe = Probe(bound);
        var producerCall = Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundBindStatement>(probe.Body[0]).Initializer);
        Assert.NotNull(producerCall.ResolvedSymbol);
        Assert.Equal("A.Foo",
            Assert.IsType<NominalBoundType>(producerCall.Type).Declaration?.QualifiedName);
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
    [InlineData("Foo?", NullableAnnotation.Annotated)]
    public void ExplicitTargetAnnotation_IsNotReplacedByInitializerAnnotation(
        string declaredType, NullableAnnotation expected)
    {
        var source = $$"""
            module DeclaredReferences
              class Foo public
                func Probe public static () → void
                  !
                  let {{declaredType}}:first = new Foo()
                  let second = first
                  let third = second
            """;
        var (module, _) = Bind(source);
        var bindings = Probe(module).Body.Cast<BoundBindStatement>().ToArray();
        Assert.Null(bindings[0].Variable.InferredReferenceType);
        foreach (var binding in bindings)
            Assert.Equal(expected, binding.Variable.NullableAnnotation);
        Assert.Equal(expected, Assert.IsType<NominalBoundType>(bindings[2].Initializer!.Type).NullableAnnotation);
    }

    [Theory]
    [InlineData("Foo", NullableAnnotation.NotAnnotated)]
    [InlineData("Foo?", NullableAnnotation.Annotated)]
    public void ModeledNominalConditional_RetainsBranchAnnotationAndIdentity(
        string rightType, NullableAnnotation expected)
    {
        var source = $$"""
            module ConditionalLimit
              class Foo public
                func Probe public static (bool:choose, Foo:left, {{rightType}}:right) → void
                  !
                  let first = choose ? left : right
                  let second = first
            """;
        var (module, _) = Bind(source);
        var bindings = Probe(module).Body.Cast<BoundBindStatement>().ToArray();
        Assert.IsType<BoundConditionalExpression>(bindings[0].Initializer);
        foreach (var binding in bindings)
        {
            Assert.Equal(expected, binding.Variable.NullableAnnotation);
            Assert.True(Assert.IsType<NominalBoundType>(binding.Variable.InferredReferenceType).IsKnownReferenceType);
        }
        Assert.Equal(expected,
            Assert.IsType<NominalBoundType>(bindings[1].Initializer!.Type).NullableAnnotation);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("System.Guid.NewGuid()")]
    [InlineData("Unknown.Get()")]
    public void ValueAndUnresolvedSources_DoNotAcquireReferenceIdentity(string expression)
    {
        var source = $$"""
            module ExcludedSources
              class Holder public
                func Probe public static () → void
                  !
                  let first = {{expression}}
                  let second = first
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
            ? "System.Environment.GetEnvironmentVariable(\"CALOR_N0_UNSET\")"
            : "System.IO.Directory.GetCurrentDirectory()";
        var source = $$"""
            module StringLocalRegression
              class Holder public
                func Probe public static () → string
                  !
                  let first = {{call}}
                  let second = first
                  return second
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
            module EnricherFieldReduction
              interface ILogEventEnricher public
              class Holder public
                field {{(nullable ? "ILogEventEnricher?" : "ILogEventEnricher")}}:{{memberName}} private
                func Probe public () → ILogEventEnricher
                  !
                  let first = this.{{memberName}}
                  let second = first
                  return second
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

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    public void BclToNativeInputs_RetainApplicabilityAndStageBActivation(int depth, bool nullable)
    {
        var value = $"System.IO.Directory.{(nullable ? "GetParent" : "CreateDirectory")}(\"/\")";
        var statements = new List<string>();
        for (var i = 0; i < depth; i++)
        {
            statements.Add($"let local{i} = {value}");
            value = $"local{i}";
        }
        statements.Add($"return Holder.Consume({value})");
        var source = $$"""
            module RetainedNativeInputLimit
              class Holder public
                func Consume public static (System.IO.DirectoryInfo:value) → int
                  !
                  return 0
                func Probe public static () → int
                  ! fs:{{(nullable ? "r" : "w")}}
                  {{string.Join("\n      ", statements)}}
            """;
        var (module, diagnostics) = Bind(source);
        var call = Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundReturnStatement>(Probe(module).Body.Last()).Expression);
        var argument = Assert.IsType<NominalBoundType>(Assert.Single(call.Arguments).Type);
        Assert.Equal("DirectoryInfo", argument.RoslynSymbol?.Name);
        Assert.Equal(nullable ? NullableAnnotation.Annotated : NullableAnnotation.NotAnnotated,
            argument.NullableAnnotation);
        var rejected = nullable && depth == 0;
        Assert.Equal(!rejected, call.ResolvedSymbol is not null);
        Assert.Equal(rejected, diagnostics.Any(d => d.Code == DiagnosticCode.NoMatchingOverload));
        Assert.Equal(nullable && !rejected,
            diagnostics.Any(d => d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter));

        // Pre-existing production behavior, not Stage B enforcement: adding a
        // local already resolved this call at the base, without any annotation.
        var result = Program.Compile(source, "native-bcl-local.calr");
        Assert.Equal(nullable, result.HasErrors);
        Assert.Equal(!nullable, !string.IsNullOrEmpty(result.GeneratedCode));
        Assert.Equal(nullable && !rejected, result.Diagnostics.Any(
            d => d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter));
    }

    public static IEnumerable<object[]> StringMemberAliases()
    {
        foreach (var kind in new[] { "field", "property" })
        foreach (var typeName in new[] { "string", "System.String", "global::System.String?", "Text?", "string?", "System.String?" })
            yield return [kind, typeName];
    }

    [Theory]
    [MemberData(nameof(StringMemberAliases))]
    public void NativeStringMember_InferredChainsPreserveAnnotationForBareThisAndBase(
        string kind, string typeName)
    {
        var nullable = typeName.Contains('?');
        var member = kind == "field"
            ? $"field {typeName}:Value public"
            : $"property {typeName}:Value public\n      get\n      set";
        var source = $$"""
            module StringMemberInference
              using Text = System.String
              class Parent public
                {{member}}
              class Child public
                extends Parent
                func Probe public () → void
                  !
                  let bareFirst = Value
                  let bareSecond = bareFirst
                  let string:bareResult = bareSecond
                  let thisFirst = this.Value
                  let thisSecond = thisFirst
                  let string:thisResult = thisSecond
                  let baseFirst = base.Value
                  let baseSecond = baseFirst
                  let string:baseResult = baseSecond
            """;
        var (module, diagnostics) = Bind(source);
        var bindings = Probe(module).Body.Cast<BoundBindStatement>().ToArray();
        var annotation = nullable ? NullableAnnotation.Annotated : NullableAnnotation.NotAnnotated;
        for (var i = 0; i < bindings.Length; i++)
        {
            Assert.Equal(annotation, Assert.IsType<NominalBoundType>(bindings[i].Initializer!.Type).NullableAnnotation);
            Assert.Equal(i % 3 == 2 ? NullableAnnotation.NotAnnotated : annotation,
                bindings[i].Variable.NullableAnnotation);
        }
        Assert.Equal(nullable ? 3 : 0,
            diagnostics.Count(d => d.Code == DiagnosticCode.NullableToNonNullableBinding));
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.UndefinedReference);
    }

    public static IEnumerable<object[]> StringSourceAliases()
    {
        foreach (var returned in new[] { false, true })
        foreach (var typeName in new[] { "string", "System.String", "global::System.String?", "Text?", "string?", "System.String?" })
            yield return [returned, typeName];
    }

    [Theory]
    [MemberData(nameof(StringSourceAliases))]
    public void NativeStringParameterAndReturn_InferredChainsKeepEstablishedAnnotation(
        bool returned, string typeName)
    {
        var source = $$"""
            module StringSourceInference
              using Text = System.String
              class Holder public
                func Get public static ({{typeName}}:input) → {{typeName}}
                  !
                  return input
                func Probe public static ({{typeName}}:input) → void
                  !
                  let first = {{(returned ? "Holder.Get(input)" : "input")}}
                  let second = first
                  let string:result = second
            """;
        var (module, diagnostics) = Bind(source);
        var nullable = typeName.Contains('?');
        var annotation = nullable ? NullableAnnotation.Annotated : NullableAnnotation.NotAnnotated;
        var bindings = Probe(module).Body.Cast<BoundBindStatement>().ToArray();
        foreach (var binding in bindings.Take(2))
        {
            Assert.Equal(annotation, Assert.IsType<NominalBoundType>(binding.Initializer!.Type).NullableAnnotation);
            Assert.Equal(annotation, binding.Variable.NullableAnnotation);
        }
        Assert.Equal(annotation, Assert.IsType<NominalBoundType>(bindings[2].Initializer!.Type).NullableAnnotation);
        Assert.Equal(nullable,
            diagnostics.Any(d => d.Code == DiagnosticCode.NullableToNonNullableBinding));
        Assert.DoesNotContain(diagnostics, d => d.Code is
            DiagnosticCode.NoMatchingOverload or DiagnosticCode.UndefinedReference);
    }

    [Fact]
    public void NullableReference_InferredChainRetainsNullableAnnotation()
    {
        const string source = """
            module OptionInference
              class Holder public
                func Probe public static () → void
                  !
                  let string?:first = null
                  let second = first
                  let string:result = second
            """;
        var (module, diagnostics) = Bind(source);
        foreach (var binding in Probe(module).Body.Take(2).Cast<BoundBindStatement>())
        {
            Assert.Equal(NullableAnnotation.Annotated, binding.Variable.NullableAnnotation);
        }
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.NullableToNonNullableBinding);
    }

    private static BoundExpression ConsumerValue(BoundStatement statement, string boundary, bool isBcl)
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
        if (isBcl)
        {
            Assert.Equal("System.IO.FileSystemAclExtensions", consumer.ResolvedTypeName);
            Assert.Equal("GetAccessControl", consumer.ResolvedMethodName);
            Assert.Equal("DirectorySecurity", Assert.IsType<NominalBoundType>(consumer.Type).RoslynSymbol?.Name);
        }
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
