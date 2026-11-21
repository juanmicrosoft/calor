using Calor.Compiler.Analysis.Security;
using Calor.Compiler.Binding;
using Calor.Compiler.Binding.BoundTypes;
using Calor.Compiler.Binding.Metadata;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Microsoft.CodeAnalysis;
using Xunit;
using NullableAnnotation = Calor.Compiler.Binding.BoundTypes.NullableAnnotation;

namespace Calor.Compiler.Tests;

public partial class BclMemberAnnotationTests
{
    public static IEnumerable<object[]> JointLocalCases()
    {
        foreach (var depth in new[] { 1, 2 })
        foreach (var boundary in new[] { "binding", "return" })
        {
            yield return ["System.Environment.ProcessPath", "", "str", true, false, depth, boundary];
            yield return ["System.Environment.CurrentDirectory", "", "str", false, false, depth, boundary];
            yield return ["directory.Parent", "System.IO.DirectoryInfo:directory", "System.IO.DirectoryInfo", true, false, depth, boundary];
            yield return ["directory.Root", "System.IO.DirectoryInfo:directory", "System.IO.DirectoryInfo", false, false, depth, boundary];
            yield return ["value.NullableField", "N5Fixture.Derived:value", "str", true, true, depth, boundary];
            yield return ["value.NonNullField", "N5Fixture.Derived:value", "str", false, true, depth, boundary];
            yield return ["N5Fixture.Derived.StaticNullableDirectoryField", "", "System.IO.DirectoryInfo", true, true, depth, boundary];
            yield return ["N5Fixture.Derived.StaticNonNullDirectoryField", "", "System.IO.DirectoryInfo", false, true, depth, boundary];
        }
    }

    [Theory]
    [MemberData(nameof(JointLocalCases))]
    public void JointMemberLocals_PreserveActualIdentityAtReceivingBoundary(
        string expression, string parameters, string receivingType, bool nullable, bool fixture, int depth, string boundary)
    {
        var value = depth == 1 ? "first" : "second";
        var source = $$"""
            module JointLocals
              func Probe public ({{parameters}}) → {{(boundary == "return" ? TypeMapper.ToSurfaceType(receivingType) : "void")}}
                !
                let first = {{expression}}
                {{(depth == 2 ? "let second = first" : "")}}
                {{(boundary == "return" ? "return" : $"let {TypeMapper.ToSurfaceType(receivingType)}:result =")}} {{value}}
            """;
        var (module, diagnostics, binder) = Bind(source, fixture ? FixtureContext.Value : null);
        var body = Assert.Single(module.Functions).Body;
        var original = Assert.IsType<BoundFieldAccessExpression>(Assert.IsType<BoundBindStatement>(body[0]).Initializer);
        var type = AssertResolvedMember(original, Metadata(binder).Context, nullable);
        Assert.Equal(fixture, original.ResolvedMetadataMember!.ContainingAssembly.Name == "CalorN5AnnotatedFixture");
        foreach (var local in body.Take(depth).Cast<BoundBindStatement>())
        {
            Assert.True(local.Variable.IsTypeInferred);
            Assert.True(SymbolEqualityComparer.IncludeNullability.Equals(
                type.RoslynSymbol, local.Variable.InferredReferenceType?.RoslynSymbol));
            Assert.Equal(type.NullableAnnotation, local.Variable.NullableAnnotation);
        }
        var received = boundary == "return"
            ? Assert.IsType<BoundReturnStatement>(body[^1]).Expression!
            : Assert.IsType<BoundBindStatement>(body[^1]).Initializer!;
        AssertJointIdentity(received, type);
        AssertJointFinding(diagnostics, Code(boundary), received, nullable);
        if (!fixture)
        {
            // These joint nominal/API controls disable effects; original controls retain default gates.
            var compiled = Program.Compile(source, "n5-joint-locals.calr",
                new CompilationOptions { EnforceEffects = false, StatusWriter = TextWriter.Null });
            Assert.Equal(nullable, compiled.HasErrors);
            if (!nullable)
            {
                var validation = GeneratedCSharpCompiler.Validate(compiled.GeneratedCode);
                Assert.True(validation.CompilationSuccess, string.Join("\n", validation.CompilationErrors));
            }
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void JointNativeNamedInput_UsesRetainedMemberIdentity(bool expression, bool nullable)
    {
        var source = $$"""
            module JointNativeMap
              func Take public (System.IO.DirectoryInfo:required, System.IO.DirectoryInfo:safe) → int
                !
                return 1
              func Probe public (System.IO.DirectoryInfo:directory) → void
                !
                let first = directory.{{(nullable ? "Parent" : "Root")}}
                let second = first
                {{(expression ? "let result = " : "")}}Take(safe: directory, required: second)
            """;
        var (module, diagnostics, binder) = Bind(source);
        var body = module.Functions.Single(f => f.Symbol.Name == "Probe").Body;
        var member = Assert.IsType<BoundFieldAccessExpression>(Assert.IsType<BoundBindStatement>(body[0]).Initializer);
        var type = AssertResolvedMember(member, Metadata(binder).Context, nullable);
        var call = JointCall(body[^1], expression);
        var match = Assert.Single(call.NativeMatches);
        Assert.Equal(module.Functions.Single(f => f.Symbol.Name == "Take").SymbolId, match.Function.Id);
        Assert.Equal(new[] { 1, 0 }, match.Arguments.Select(a => a.ParameterIndex));
        AssertJointIdentity(call.Arguments[1], type);
        AssertJointFinding(diagnostics, Code("argument"), call.Arguments[1], nullable, "'required'");
        var compiled = Program.Compile(source, "n5-joint-native.calr",
            new CompilationOptions { EnforceEffects = false, StatusWriter = TextWriter.Null });
        Assert.Equal(nullable, compiled.HasErrors);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void JointBclNamedOrExpandedInput_PreservesScalarMemberAnnotation(
        bool expression, bool expanded, bool nullable)
    {
        var arguments = expanded
            ? "\"a\", \"b\", \"c\", \"d\", second"
            : "path2: \"safe\", path1: second";
        var source = $$"""
            module JointBclMap
              func Probe public () → void
                !
                let first = {{(nullable ? "System.Environment.ProcessPath" : "System.String.Empty")}}
                let second = first
                {{(expression ? "let result = " : "")}}System.IO.Path.Combine({{arguments}})
            """;
        var (module, diagnostics, binder) = Bind(source);
        var body = Assert.Single(module.Functions).Body;
        var member = Assert.IsType<BoundFieldAccessExpression>(Assert.IsType<BoundBindStatement>(body[0]).Initializer);
        var type = AssertResolvedMember(member, Metadata(binder).Context, nullable);
        Assert.Equal(nullable ? SymbolKind.Property : SymbolKind.Field, member.ResolvedMetadataMember!.Kind);
        var call = JointCall(body[^1], expression);
        Assert.Equal(expanded ? new[] { 0, 0, 0, 0, 0 } : new[] { 1, 0 }, call.MetadataIndices);
        if (expanded)
        {
            var context = Metadata(binder).Context;
            var selected = Metadata(binder).ResolveCall(context.TryResolveType("System.IO.Path")!, "Combine",
                Enumerable.Repeat(new MetadataArgument(type.RoslynSymbol!), 5).ToArray());
            Assert.True(selected.IsResolved);
            Assert.True(Assert.Single(selected.Symbol!.Parameters).IsParams);
            Assert.Contains("System.String", Assert.Single(call.ParameterTypes!));
            Assert.All(selected.Arguments, mapping =>
            {
                Assert.True(mapping.IsExpandedParams);
                Assert.Equal(SpecialType.System_String, mapping.TargetType.SpecialType);
                Assert.Equal(Microsoft.CodeAnalysis.NullableAnnotation.NotAnnotated, mapping.TargetType.NullableAnnotation);
            });
        }
        else
            Assert.Equal(new[] { "System.String", "System.String" }, call.ParameterTypes);
        var argument = call.Arguments[^1];
        AssertJointIdentity(argument, type);
        AssertJointFinding(diagnostics, Code("argument"), argument, nullable, expanded ? "'paths'" : "'path1'");
        if (nullable)
            Assert.Equal(BindingReceivingShape.ScalarString,
                Assert.Single(diagnostics.Where(d => d.Code == Code("argument"))).BindingContext!.Shape);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void JointControlledReferenceContext_IsSharedByMemberAndSelectedCall(bool expression, bool nullable)
    {
        var source = $$"""
            module JointContext
              func Probe public (N5Fixture.Derived:value) → void
                !
                let first = value.{{(nullable ? "Nullable" : "NonNull")}}DirectoryField
                let second = first
                {{(expression ? "let result = " : "")}}System.N5MemberSink.Take(optional: first, required: second)
            """;
        var context = FixtureContext.Value;
        var (module, diagnostics, binder) = Bind(source, context);
        Assert.Same(context, Metadata(binder).Context);
        var body = Assert.Single(module.Functions).Body;
        var member = Assert.IsType<BoundFieldAccessExpression>(Assert.IsType<BoundBindStatement>(body[0]).Initializer);
        var type = AssertResolvedMember(member, context, nullable);
        var call = JointCall(body[^1], expression);
        Assert.Equal(new[] { 1, 0 }, call.MetadataIndices);
        Assert.Equal(new[] { "System.IO.DirectoryInfo", "System.IO.DirectoryInfo", "System.Int32" }, call.ParameterTypes);
        AssertJointIdentity(call.Arguments[1], type);
        var selected = Metadata(binder).ResolveCall(context.TryResolveType("System.N5MemberSink")!, "Take",
            [new MetadataArgument(type.RoslynSymbol!, Name: "optional"), new MetadataArgument(type.RoslynSymbol!, Name: "required")]);
        Assert.True(selected.IsResolved);
        Assert.Equal("CalorN5AnnotatedFixture", selected.Symbol!.ContainingAssembly.Name);
        Assert.True(selected.Symbol.Parameters[2].IsOptional);
        Assert.Equal(new[] { 1, 0 }, selected.Arguments.Select(a => a.Parameter.Ordinal));
        Assert.True(SymbolEqualityComparer.Default.Equals(type.RoslynSymbol, selected.Arguments[1].Parameter.Type));
        AssertJointFinding(diagnostics, Code("argument"), call.Arguments[1], nullable, "'required'");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void JointActualBclMember_PreservesSelectedTaintRoles(bool expression, bool taintedPath)
    {
        var source = $$"""
            module JointTaint
              func Probe public (string:user_input) → void
                !
                let first = System.Text.Encoding.UTF8
                let second = first
                {{(expression ? "let result = " : "")}}System.IO.File.ReadAllText(encoding: second, path: {{(taintedPath ? "user_input" : "\"safe.txt\"")}})
            """;
        var (module, diagnostics, binder) = Bind(source);
        var body = Assert.Single(module.Functions).Body;
        var member = Assert.IsType<BoundFieldAccessExpression>(Assert.IsType<BoundBindStatement>(body[0]).Initializer);
        var type = AssertResolvedMember(member, Metadata(binder).Context, false);
        var call = JointCall(body[^1], expression);
        AssertJointIdentity(call.Arguments[0], type);
        Assert.Equal(new[] { 1, 0 }, call.MetadataIndices);
        Assert.Equal(new[] { "System.String", "System.Text.Encoding" }, call.ParameterTypes);
        Assert.DoesNotContain(diagnostics, BindingDiagnosticPolicy.IsCompilationError);
        var taint = new DiagnosticBag();
        new TaintAnalysisRunner(taint).Analyze(module);
        Assert.Equal(taintedPath, taint.Any(d => d.Code == DiagnosticCode.PathTraversal));
    }

    private static void AssertJointIdentity(BoundExpression expression, NominalBoundType original)
    {
        var type = Assert.IsType<NominalBoundType>(expression.Type);
        Assert.Equal(original.NullableAnnotation, type.NullableAnnotation);
        Assert.True(SymbolEqualityComparer.IncludeNullability.Equals(original.RoslynSymbol, type.RoslynSymbol));
    }

    private static void AssertJointFinding(
        DiagnosticBag diagnostics, string code, BoundExpression source, bool nullable, string? parameter = null)
    {
        var findings = diagnostics.Where(d => d.Code == code).ToArray();
        if (!nullable)
        {
            Assert.Empty(findings);
            Assert.DoesNotContain(diagnostics, BindingDiagnosticPolicy.IsCompilationError);
        }
        else
        {
            var finding = Assert.Single(findings);
            Assert.Equal(source.Span, finding.Span);
            Assert.Contains("'Annotated'", finding.Message);
            if (parameter is not null)
                Assert.Contains(parameter, finding.Message);
            Assert.True(BindingDiagnosticPolicy.IsCompilationError(finding));
        }
    }

    private static (IReadOnlyList<BoundExpression> Arguments, IReadOnlyList<int>? MetadataIndices,
        IReadOnlyList<string>? ParameterTypes, IReadOnlyList<ResolvedOverloadMatch> NativeMatches)
        JointCall(BoundStatement statement, bool expression)
    {
        if (expression)
        {
            var call = Assert.IsType<BoundCallExpression>(Assert.IsType<BoundBindStatement>(statement).Initializer);
            return (call.Arguments, call.ArgumentParameterIndices, call.ResolvedParameterTypes, call.SelectedOverloadMatches);
        }
        var statementCall = Assert.IsType<BoundCallExpression>(Assert.IsType<BoundExpressionStatement>(statement).Expression);
        return (statementCall.Arguments, statementCall.ArgumentParameterIndices, statementCall.ResolvedParameterTypes, statementCall.SelectedOverloadMatches);
    }
}
