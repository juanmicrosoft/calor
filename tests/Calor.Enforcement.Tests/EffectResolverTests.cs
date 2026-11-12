using System.Runtime.Loader;
using Calor.Compiler.Binding.BoundTypes;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Effects.Manifests;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Calor.Enforcement.Tests;

/// <summary>
/// Tests for the effect resolver (layered manifest resolution).
/// </summary>
public class EffectResolverTests
{
    [Fact]
    public void StringCopyContracts_ExactMetadataKeysCannotAuthorizeOtherOverloadsOrMemberKinds()
    {
        var type = StringProbeCompilation("", []).GetSpecialType(SpecialType.System_String);
        var format = new SymbolDisplayFormat(typeQualificationStyle:
            SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);
        var candidates = type.GetMembers().OfType<IMethodSymbol>()
            .Where(method => method.DeclaredAccessibility == Accessibility.Public
                && (method.MethodKind == MethodKind.Constructor
                    || method.Name is "ToCharArray" or "Split" or "Contains" or "Compare" or "Format"))
            .ToArray();
        foreach (var safeFirst in new[] { false, true })
        {
            var loader = new ManifestLoader(loadUserLevelManifests: false);
            var resolver = new EffectResolver(loader);
            resolver.Initialize();
            var mapping = Assert.Single(loader.LoadedManifests.SelectMany(entry => entry.Manifest.Mappings)
                .Where(mapping => mapping.Type == "System.String"));
            Assert.Null(mapping.DefaultEffects);
            Assert.DoesNotContain("*", mapping.Methods!.Keys);
            Assert.All(mapping.Methods.Keys, signature => Assert.Contains("(", signature));
            Assert.Equal("(System.Char[])", Assert.Single(mapping.Constructors!).Key);
            var safeKeys = new[]
            {
                EffectResolverKey.FromStrings("System.String", ".ctor", ["System.Char[]"], EffectMemberKind.Constructor)
            };
            if (safeFirst)
                foreach (var key in safeKeys)
                    Assert.Equal(EffectResolutionStatus.PureExplicit, resolver.Resolve(key).Status);
            foreach (var method in candidates.Concat(candidates.Reverse()))
            {
                Assert.Equal(typeof(string).Assembly.FullName, method.ContainingAssembly.Identity.ToString());
                Assert.DoesNotContain(method.Locations, location => location.IsInSource);
                var constructor = method.MethodKind == MethodKind.Constructor;
                var parameters = method.Parameters.Select(parameter => parameter.Type.ToDisplayString(format)).ToArray();
                var key = EffectResolverKey.FromStrings("System.String", method.Name, parameters,
                    constructor ? EffectMemberKind.Constructor : EffectMemberKind.Method);
                var safe = constructor
                    ? parameters.SequenceEqual(new[] { "System.Char[]" })
                    : method.Name == "Contains" && parameters.SequenceEqual(new[] { "System.Char" });
                var result = resolver.Resolve(key);
                Assert.Equal(safe ? EffectResolutionStatus.PureExplicit : EffectResolutionStatus.Unknown, result.Status);
                Assert.Equal(!safe, result.Effects.IsUnknown);
                Assert.Same(result, resolver.Resolve(key));
                Assert.Equal(EffectResolutionStatus.Unknown, resolver.Resolve(key.WithoutParameterList()).Status);
            }
            foreach (var key in safeKeys)
            {
                Assert.Equal(EffectResolutionStatus.PureExplicit, resolver.Resolve(key).Status);
                foreach (var kind in new[] { EffectMemberKind.Method, EffectMemberKind.Constructor,
                    EffectMemberKind.Getter, EffectMemberKind.Setter })
                {
                    if (kind != key.Kind)
                        Assert.Equal(EffectResolutionStatus.Unknown, resolver.Resolve(
                            EffectResolverKey.FromStrings("System.String", key.MemberName, key.ParameterTypes, kind)).Status);
                }
                Assert.Equal(EffectResolutionStatus.Unknown, resolver.Resolve(
                    EffectResolverKey.FromStrings("System.String", key.MemberName, ["?"], key.Kind)).Status);
                Assert.Equal(EffectResolutionStatus.Unknown, resolver.Resolve(
                    EffectResolverKey.FromStrings("Vendor.String", key.MemberName, key.ParameterTypes, key.Kind)).Status);
            }
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("\0\u00e9\ud800\udc00\udfff\uffff")]
    public void StringCopyContracts_CharArrayConstructorIsStrictlyAdmittedBeforeExecution(string? value)
    {
        var source = $$"""
            #nullable enable
            public static class Target
            {
                public static string Apply(char[]? value) => new string(value);
            }
            """;
        var selected = SelectedStringCopyMember(source, constructor: true);
        Assert.False(selected.IsStatic);
        Assert.Equal(MethodKind.Constructor, selected.MethodKind);
        Assert.Equal(".ctor", selected.Name);
        var parameter = Assert.Single(selected.Parameters);
        var array = Assert.IsAssignableFrom<IArrayTypeSymbol>(parameter.Type);
        Assert.Equal(SpecialType.System_Char, array.ElementType.SpecialType);
        Assert.Equal(1, array.Rank);
        Assert.Equal(Microsoft.CodeAnalysis.NullableAnnotation.Annotated, parameter.NullableAnnotation);
        var resolution = new EffectResolver(new ManifestLoader(loadUserLevelManifests: false))
            .Resolve(StringCopyKey(selected));
        Assert.Equal(EffectResolutionStatus.PureExplicit, resolution.Status);
        Assert.True(resolution.Effects.IsEmpty);
        Assert.Contains("embedded:Calor.Compiler.Manifests.bcl-system", resolution.Source);
        var conversion = ConvertStringSource(source);
        Assert.True(conversion.Success);
        Assert.Equal(0, conversion.InteropPreservationCount);
        foreach (var native in StringEmissions(conversion.CalorSource!))
        foreach (var typing in new[] { false, true })
        {
            var result = Calor.Compiler.Program.Compile(native, "safe-string-copy.calr",
                new Calor.Compiler.CompilationOptions
                {
                    EnableTypeChecking = typing, VerifyContracts = false, StatusWriter = TextWriter.Null
                });
            Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
            Assert.Equal(selected.ToDisplayString(),
                SelectedStringCopyMember(result.GeneratedCode, constructor: true).ToDisplayString());
            foreach (var symbols in new string[][] { [], ["NET6_0_OR_GREATER"] })
            foreach (var throws in new[] { false, true })
            {
                var witness = StringCopyWitness(value, throws);
                Assert.Equal("True:0", RunStringProbe(source + witness, symbols));
                Assert.Equal("True:0", RunStringProbe(result.GeneratedCode + witness, symbols));
            }
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void StringCopyContracts_SourceAliasesAndLookalikesAreNotNativeContractEvidence(bool constructor, bool alias)
    {
        var name = alias ? "VendorString" : "String";
        var source = $$"""
            {{(alias ? "using String = VendorString;" : "")}}
            public sealed class {{name}}
            {
                public static int Calls;
                public {{name}}() { }
                {{(constructor ? $"public {name}(char[] value) {{ Calls++; }}" : "")}}
                public char[] ToCharArray() { Calls++; return null; }
            }
            public static class Target
            {
                public static {{(constructor ? "String" : "char[]")}} Apply({{(constructor ? "char[]" : "String")}} value) =>
                    {{(constructor ? "new String(value)" : "value.ToCharArray()")}};
            }
            """;
        var original = StringProbeCompilation(source, []);
        var tree = original.SyntaxTrees.Single();
        var call = tree.GetRoot().DescendantNodes().OfType<ArrowExpressionClauseSyntax>().Single().Expression;
        var selected = Assert.IsAssignableFrom<IMethodSymbol>(original.GetSemanticModel(tree).GetSymbolInfo(call).Symbol);
        Assert.Contains(selected.Locations, location => location.IsInSource);
        Assert.Equal(SpecialType.None, selected.ContainingType.SpecialType);
        var conversion = ConvertStringSource(source);
        Assert.True(conversion.Success);
        Assert.Equal(1, conversion.InteropPreservationCount);
        var witness = $$"""

            public static class Probe
            {
                public static bool Run()
                {
                    {{name}}.Calls = 0;
                    Target.Apply({{(constructor ? "new char[0]" : $"new {name}()")}});
                    return {{name}}.Calls == 1;
                }
            }
            """;
        Assert.Equal(true, RunStringProbe(source + witness, []));
    }

    [Fact]
    public void StringCopyContracts_ToCharArrayNullCheckCanInvokeUICultureBeforeMethodEntry()
    {
        const string source = """
            public static class Target
            {
                public static char[] Apply(string value) => value.ToCharArray();
            }
            """;
        const string driver = """

            public static class NullWitness
            {
                public static bool Run()
                {
                    try { Target.Apply(null); return false; }
                    catch (System.NullReferenceException) { return true; }
                }
            }
            """;
        foreach (var symbols in new string[][] { [], ["NET6_0_OR_GREATER"] })
        {
            var original = Assert.IsType<string>(RunStringProbe(source + driver
                + NumericWitness("NullWitness.Run()", throws: false, providerIsCulture: true), symbols));
            Assert.StartsWith("True:", original);
            Assert.True(int.Parse(original["True:".Length..], System.Globalization.CultureInfo.InvariantCulture) > 0);
        }
        AssertStringRejected(source, """
            module StringContracts overflow unchecked nullable oblivious
              module Target
                func Apply (string:value) → char[]
                  return value.ToCharArray()
            """);
    }

    [Theory]
    [InlineData(false, "value.ToCharArray()", "string", "char[]")]
    [InlineData(false, "((string)null).ToCharArray()", "string", "char[]")]
    [InlineData(false, "value.ToCharArray(0, 1)", "string", "char[]")]
    [InlineData(true, "new string(value, 0, 1)", "char[]", "string")]
    [InlineData(true, "new string('x', 1)", "char[]", "string")]
    [InlineData(false, "value.Contains('x', (System.StringComparison)123)", "string", "bool")]
    public void StringCopyContracts_UnprovedNeighborsRemainStrictlyRejected(
        bool constructor, string expression, string parameter, string returnType)
    {
        var source = $$"""
            public static class Target
            {
                public static {{returnType}} Apply({{parameter}} value) => {{expression}};
            }
            """;
        var selected = SelectedStringCopyMember(source, constructor);
        var resolver = new EffectResolver(new ManifestLoader(loadUserLevelManifests: false));
        Assert.Equal(EffectResolutionStatus.PureExplicit, resolver.Resolve(EffectResolverKey.FromStrings(
            "System.String", ".ctor", ["System.Char[]"], EffectMemberKind.Constructor)).Status);
        Assert.Equal(EffectResolutionStatus.Unknown, resolver.Resolve(StringCopyKey(selected)).Status);
        var conversion = ConvertStringSource(source);
        Assert.True(conversion.Success);
        var native = $$"""
            module StringContracts overflow unchecked nullable oblivious
              module Target
                func Apply ({{parameter}}:value) → {{returnType}}
                  return {{expression}}
            """;
        var emissions = StringEmissions(native);
        if (conversion.InteropPreservationCount == 0)
            emissions = emissions.Concat(StringEmissions(conversion.CalorSource!)).ToArray();
        foreach (var emission in emissions)
        foreach (var typing in new[] { false, true })
        {
            var result = Calor.Compiler.Program.Compile(emission, "unknown-string-copy.calr",
                new Calor.Compiler.CompilationOptions
                {
                    EnableTypeChecking = typing, VerifyContracts = false, StatusWriter = TextWriter.Null
                });
            Assert.True(result.HasErrors);
            Assert.Empty(result.GeneratedCode);
            Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("'unknown'", StringComparison.Ordinal));
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall
                && diagnostic.Message.Contains("." + selected.Name, StringComparison.Ordinal));
        }
    }

    private static IMethodSymbol SelectedStringCopyMember(string source, bool constructor)
    {
        var compilation = StringProbeCompilation(source, []);
        Assert.DoesNotContain(compilation.GetDiagnostics(),
            diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        var tree = compilation.SyntaxTrees.Single();
        var expression = Assert.Single(tree.GetRoot().DescendantNodes()
            .Where(node => constructor ? node is ObjectCreationExpressionSyntax : node is InvocationExpressionSyntax));
        var selected = Assert.IsAssignableFrom<IMethodSymbol>(compilation.GetSemanticModel(tree).GetSymbolInfo(expression).Symbol);
        Assert.Equal(SpecialType.System_String, selected.ContainingType.SpecialType);
        Assert.True(selected.ContainingType.IsSealed);
        Assert.Equal(typeof(string).Assembly.FullName, selected.ContainingAssembly.Identity.ToString());
        Assert.DoesNotContain(selected.Locations, location => location.IsInSource);
        return selected;
    }

    private static EffectResolverKey StringCopyKey(IMethodSymbol method)
    {
        var format = new SymbolDisplayFormat(typeQualificationStyle:
            SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);
        return EffectResolverKey.FromStrings("System.String", method.Name,
            method.Parameters.Select(parameter => parameter.Type.ToDisplayString(format)).ToArray(),
            method.MethodKind == MethodKind.Constructor ? EffectMemberKind.Constructor : EffectMemberKind.Method);
    }

    private static string StringCopyWitness(string? value, bool throws)
    {
        var literal = value == null ? "null" : SymbolDisplay.FormatLiteral(value, quote: true);
        var chars = value == null ? "null" : "new char[] { "
            + string.Join(", ", value.Select(character => SymbolDisplay.FormatLiteral(character, quote: true))) + " }";
        var check = $$"""
            char[] input = {{chars}};
            var result = Target.Apply(input);
            if (result != {{(value == null ? "\"\"" : literal)}}) return false;
            if (input != null && input.Length > 0) input[0] = '\ufffe';
            return result == {{(value == null ? "\"\"" : literal)}};
            """;
        var culture = NumericWitness("CopyWitness.Run()", throws, providerIsCulture: true)
            .Replace("public override string Name", """
                public override System.Globalization.TextInfo TextInfo
                {
                    get
                    {
                        Reads++;
                        if (Throw) throw new System.InvalidOperationException("numeric-culture-callback");
                        return base.TextInfo;
                    }
                }
                public override System.Globalization.CompareInfo CompareInfo
                {
                    get
                    {
                        Reads++;
                        if (Throw) throw new System.InvalidOperationException("numeric-culture-callback");
                        return base.CompareInfo;
                    }
                }
                public override string Name
                """, StringComparison.Ordinal);
        return culture + $$"""

            public static class CopyWitness
            {
                public static bool Run()
                {
                    {{check}}
                }
            }
            """;
    }

    [Theory]
    [InlineData("ToUpper", new string[0])]
    [InlineData("ToLower", new string[0])]
    [InlineData("ToUpper", new[] { "System.Globalization.CultureInfo" })]
    [InlineData("ToLower", new[] { "System.Globalization.CultureInfo" })]
    [InlineData("Compare", new[] { "System.String", "System.String" })]
    [InlineData("StartsWith", new[] { "System.String" })]
    [InlineData("IndexOf", new[] { "System.String" })]
    [InlineData("Equals", new[] { "System.String", "System.StringComparison" })]
    [InlineData("Contains", new[] { "System.String", "System.StringComparison" })]
    [InlineData("Concat", new[] { "System.Object" })]
    [InlineData("Concat", new[] { "System.Collections.Generic.IEnumerable`1<System.String>" })]
    [InlineData("Join", new[] { "System.String", "System.Collections.Generic.IEnumerable`1<System.String>" })]
    [InlineData("Format", new[] { "System.IFormatProvider", "System.String", "System.Object[]" })]
    [InlineData("ToString", new[] { "System.IFormatProvider" })]
    [InlineData("Uncovered", new string[0])]
    public void StringContracts_LeaveUnprovenOverloadsUnknown(string member, string[] parameters)
    {
        var resolver = new EffectResolver(new ManifestLoader(loadUserLevelManifests: false));
        var result = resolver.Resolve(EffectResolverKey.FromStrings("System.String", member, parameters));
        Assert.Equal(EffectResolutionStatus.Unknown, result.Status);
        Assert.True(result.Effects.IsUnknown);
    }

    [Fact]
    public void StringContracts_PartialAndCachedLookupsCannotBorrowAnotherOverloadsPermission()
    {
        var safe = EffectResolverKey.FromStrings("System.String", "Contains", ["System.Char"]);
        var unresolved = new[]
        {
            safe.WithoutParameterList(),
            EffectResolverKey.FromStrings("System.String", "Contains", ["?"]),
            EffectResolverKey.FromStrings("System.String", "Contains", ["System.String"]),
            EffectResolverKey.FromStrings("Vendor.String", "Contains", ["System.Char"]),
            EffectResolverKey.FromStrings("System.String", "Length", kind: EffectMemberKind.Setter),
            EffectResolverKey.FromStrings("System.String", "Length"),
            EffectResolverKey.FromStrings("System.String", ".ctor", ["System.Char", "System.Int32"],
                EffectMemberKind.Constructor)
        };
        foreach (var safeFirst in new[] { false, true })
        {
            var resolver = new EffectResolver(new ManifestLoader(loadUserLevelManifests: false));
            if (safeFirst)
                Assert.Equal(EffectResolutionStatus.PureExplicit, resolver.Resolve(safe).Status);
            foreach (var key in unresolved.Concat(unresolved.Reverse()))
                Assert.Equal(EffectResolutionStatus.Unknown, resolver.Resolve(key).Status);
            var resolution = resolver.Resolve(safe);
            Assert.Equal(EffectResolutionStatus.PureExplicit, resolution.Status);
            Assert.Same(resolution, resolver.Resolve(safe));
        }
    }

    [Theory]
    [InlineData("string.IsNullOrEmpty(\"\")", "bool", true)]
    [InlineData("\"a\".ToUpperInvariant()", "string", "A")]
    [InlineData("\"\\u00e9\".ToUpperInvariant()", "string", "\u00c9")]
    [InlineData("\"A\".ToLowerInvariant()", "string", "a")]
    [InlineData("\"\\u00c9\".ToLowerInvariant()", "string", "\u00e9")]
    [InlineData("\"a\".ToString()", "string", "a")]
    [InlineData("\"a\".Equals(\"a\")", "bool", true)]
    [InlineData("string.Equals(\"a\", \"a\")", "bool", true)]
    [InlineData("string.CompareOrdinal(\"a\", \"b\") < 0", "bool", true)]
    [InlineData("\"abc\".Contains('b')", "bool", true)]
    [InlineData("\"\".Contains('b')", "bool", false)]
    [InlineData("\"abc\".IndexOf('b')", "int", 1)]
    [InlineData("\"\".IndexOf('b')", "int", -1)]
    [InlineData("\"abc\".Length", "int", 3)]
    public void StringContracts_SafeSelectedMembersHaveExactAuthorityAndExecute(
        string expression, string returnType, object expected)
    {
        var csharp = $"public static class Probe {{ public static {returnType} Run() => {expression}; }}";
        var compilation = StringProbeCompilation(csharp, []);
        var model = compilation.GetSemanticModel(compilation.SyntaxTrees.Single());
        var selected = compilation.SyntaxTrees.Single().GetRoot().DescendantNodes()
            .Where(node => node is InvocationExpressionSyntax or MemberAccessExpressionSyntax)
            .Select(node => model.GetSymbolInfo(node).Symbol)
            .First(symbol => symbol is IMethodSymbol or IPropertySymbol);
        Assert.NotNull(selected);
        Assert.Equal(SpecialType.System_String, selected.ContainingType.SpecialType);
        Assert.Equal(typeof(string).Assembly.FullName, selected.ContainingAssembly.Identity.ToString());
        Assert.DoesNotContain(selected.Locations, location => location.IsInSource);
        var format = new SymbolDisplayFormat(typeQualificationStyle:
            SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);
        var method = selected as IMethodSymbol;
        var key = EffectResolverKey.FromStrings("System.String", selected.Name,
            method?.Parameters.Select(parameter => parameter.Type.ToDisplayString(format)).ToArray(),
            method == null ? EffectMemberKind.Getter : EffectMemberKind.Method);
        var resolution = new EffectResolver(new ManifestLoader(loadUserLevelManifests: false)).Resolve(key);
        Assert.Equal(EffectResolutionStatus.PureExplicit, resolution.Status);
        Assert.True(resolution.Effects.IsEmpty);
        Assert.Contains("embedded:Calor.Compiler.Manifests.bcl-system", resolution.Source);

        var conversion = ConvertStringSource(csharp);
        Assert.True(conversion.Success);
        Assert.Equal(0, conversion.InteropPreservationCount);
        var hostileCulture = CultureProbe.Replace("public static class Probe", "public static class CultureWitness",
                StringComparison.Ordinal)
            .Replace("public TrackingCulture() : base(\"\") { }", """
                public TrackingCulture() : base("") { }
                public override object GetFormat(System.Type type)
                {
                    Reads++;
                    throw new System.InvalidOperationException("culture-callback");
                }
                public override System.Globalization.CompareInfo CompareInfo
                {
                    get { Reads++; throw new System.InvalidOperationException("culture-callback"); }
                }
                """, StringComparison.Ordinal)
            .Replace("return result + \":\" + reads;",
                "return System.Convert.ToString(result, System.Globalization.CultureInfo.InvariantCulture)"
                + " + \":\" + reads.ToString(System.Globalization.CultureInfo.InvariantCulture);", StringComparison.Ordinal)
            .Replace("$CALL$", "Probe.Run()", StringComparison.Ordinal)
            .Replace("$THROW$", "true", StringComparison.Ordinal);
        foreach (var symbols in new string[][] { [], ["NET6_0_OR_GREATER"] })
        {
            Assert.Equal(expected, RunStringProbe(csharp, symbols));
            Assert.Equal(expected + ":0", RunStringProbe(csharp + hostileCulture, symbols, "CultureWitness"));
            foreach (var native in StringEmissions(conversion.CalorSource!))
            foreach (var typeChecking in new[] { false, true })
            {
                var result = Calor.Compiler.Program.Compile(native, "safe-string.calr",
                    new Calor.Compiler.CompilationOptions
                    {
                        EnableTypeChecking = typeChecking, VerifyContracts = false, StatusWriter = TextWriter.Null
                    });
                Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
                Assert.Equal(expected, RunStringProbe(result.GeneratedCode, symbols));
                Assert.Equal(expected + ":0",
                    RunStringProbe(result.GeneratedCode + hostileCulture, symbols, "CultureWitness"));
            }
        }
    }

    [Theory]
    [InlineData("value.ToUpper()", "a", "A")]
    [InlineData("value.ToLower()", "A", "a")]
    [InlineData("value.ToUpper(culture)", "a", "A")]
    [InlineData("value.ToLower(culture)", "A", "a")]
    public void StringContracts_CultureDispatchMutatesOrThrowsInOriginalAndIsStrictlyRejected(
        string expression, string input, string output)
    {
        var csharp = $$"""
            public static class Target
            {
                public static string Apply(string value, System.Globalization.CultureInfo culture) => {{expression}};
            }
            """;
        foreach (var throws in new[] { false, true })
        foreach (var symbols in new string[][] { [], ["NET6_0_OR_GREATER"] })
        {
            var driver = CultureProbe.Replace("$CALL$", $"Target.Apply(\"{input}\", culture)", StringComparison.Ordinal)
                .Replace("$THROW$", throws ? "true" : "false", StringComparison.Ordinal);
            Assert.Equal(throws ? "throw:1" : output + ":1", RunStringProbe(csharp + driver, symbols));
        }
        AssertStringRejected(csharp, $$"""
            module StringContracts overflow unchecked nullable oblivious
              module Target
                func Apply (string:value, System.Globalization.CultureInfo:culture) → string
                  return {{expression}}
            """);
    }

    [Theory]
    [InlineData("value.CompareTo(other)", "-1")]
    [InlineData("string.Compare(value, other, false, culture)", "-1")]
    [InlineData("value.IndexOf(other)", "1")]
    public void StringContracts_ComparisonDispatchAlsoStaysUnknown(string expression, string expected)
    {
        var csharp = $$"""
            #nullable enable
            public static class Target
            {
                public static int Apply(string value, string other, System.Globalization.CultureInfo culture) => {{expression}};
            }
            """;
        foreach (var throws in new[] { false, true })
        foreach (var symbols in new string[][] { [], ["NET6_0_OR_GREATER"] })
        {
            var driver = CultureProbe.Replace("System.Globalization.TextInfo TextInfo",
                    "System.Globalization.CompareInfo CompareInfo", StringComparison.Ordinal)
                .Replace("base.TextInfo", "base.CompareInfo", StringComparison.Ordinal)
                .Replace("$CALL$", "Target.Apply(\"abc\", \"b\", culture)", StringComparison.Ordinal)
                .Replace("$THROW$", throws ? "true" : "false", StringComparison.Ordinal);
            Assert.Equal(throws ? "throw:1" : expected + ":1", RunStringProbe(csharp + driver, symbols));
        }
        AssertStringRejected(csharp, $$"""
            module StringContracts overflow unchecked
              module Target
                func Apply (string:value, string:other, System.Globalization.CultureInfo:culture) → int
                  return {{expression}}
            """, expectedInterop: 0);
    }

    [Fact]
    public void StringContracts_OriginalLinqChainDoesNotAcquirePureCallbackPermission()
    {
        foreach (var throws in new[] { false, true })
        foreach (var symbols in new string[][] { [], ["NET6_0_OR_GREATER"] })
        {
            var driver = CultureProbe.Replace("$CALL$",
                    "string.Join(\",\", new Transform().GetNames(new System.Collections.Generic.List<string> { \"a\", \"b\" }))",
                    StringComparison.Ordinal)
                .Replace("$THROW$", throws ? "true" : "false", StringComparison.Ordinal);
            Assert.Equal(throws ? "throw:1" : "A,B:2", RunStringProbe(OriginalStringLinq + driver, symbols));
        }
        AssertStringRejected(OriginalStringLinq, """
            module Test_05_03 overflow unchecked
              using System.Linq
              using System.Collections.Generic

              class Transform
                func GetNames (List<string>:items) → List<string>
                  return (items.Where((string:s) => (s.Length > 0))).Select((string:s) => (s).ToUpper()).OrderBy((string:s) => s).ToList()
            """);
    }

    [Fact]
    public void StringContracts_SourceAliasCannotStealBuiltinSignatureAuthority()
    {
        var conversion = ConvertStringSource("""
            using String = VendorString;
            public class VendorString
            {
                public static int Calls;
                public static bool IsNullOrEmpty(string value) { Calls++; return false; }
            }
            public class Probe
            {
                public static bool Run() => String.IsNullOrEmpty("");
            }
            """);
        Assert.True(conversion.Success);
        var run = conversion.Ast!.Classes.Single(type => type.Name == "Probe").Methods.Single();
        Assert.Contains(EffectEnforcementPass.GetDeclaredEffects(run.Effects).Effects,
            effect => effect.Kind == EffectKind.Mutation);
        var result = Calor.Compiler.Program.Compile(conversion.CalorSource!, "shadow-string.calr",
            new Calor.Compiler.CompilationOptions { VerifyContracts = false, StatusWriter = TextWriter.Null });
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        Assert.Equal(false, RunStringProbe(result.GeneratedCode, []));
    }

    private static void AssertStringRejected(string csharp, string nativeSource, int expectedInterop = 1)
    {
        var original = StringProbeCompilation(csharp, []);
        var tree = original.SyntaxTrees.Single();
        var model = original.GetSemanticModel(tree);
        var selected = Assert.Single(tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Select(node => model.GetSymbolInfo(node).Symbol).OfType<IMethodSymbol>()
            .Where(method => method.ContainingType.SpecialType == SpecialType.System_String));
        Assert.Equal(typeof(string).Assembly.FullName, selected.ContainingAssembly.Identity.ToString());
        Assert.DoesNotContain(selected.Locations, location => location.IsInSource);
        var conversion = ConvertStringSource(csharp);
        Assert.True(conversion.Success);
        Assert.Equal(expectedInterop, conversion.InteropPreservationCount);
        var emissions = StringEmissions(nativeSource);
        if (expectedInterop == 0)
            emissions = emissions.Concat(StringEmissions(conversion.CalorSource!)).ToArray();
        foreach (var native in emissions)
        foreach (var typeChecking in new[] { false, true })
        {
            var result = Calor.Compiler.Program.Compile(native, "unknown-string.calr",
                new Calor.Compiler.CompilationOptions
                {
                    EnableTypeChecking = typeChecking, VerifyContracts = false, StatusWriter = TextWriter.Null
                });
            Assert.True(result.HasErrors);
            Assert.Empty(result.GeneratedCode);
            Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("'unknown'", StringComparison.Ordinal));
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall
                && diagnostic.Message.Contains("." + selected.Name, StringComparison.Ordinal));
        }
    }

    private static string[] StringEmissions(string source)
    {
        var diagnostics = new DiagnosticBag();
        var ast = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        return [source, new CalorEmitter().Emit(ast)];
    }

    private static ConversionResult ConvertStringSource(string source) =>
        new CSharpToCalorConverter(new ConversionOptions { ModuleName = "StringContracts" }).Convert(source);

    private static CSharpCompilation StringProbeCompilation(string source, string[] symbols) =>
        CSharpCompilation.Create("StringProbe_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(preprocessorSymbols: symbols))],
            TestHarness.CompilationReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static object? RunStringProbe(string source, string[] symbols, string typeName = "Probe")
    {
        using var stream = new MemoryStream();
        var result = StringProbeCompilation(source, symbols).Emit(stream);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        stream.Position = 0;
        var context = new AssemblyLoadContext("string-probe", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(stream);
            return assembly.GetType(typeName)!.GetMethod("Run")!.Invoke(null, null);
        }
        finally
        {
            context.Unload();
        }
    }

    private const string CultureProbe = """

        public sealed class TrackingCulture : System.Globalization.CultureInfo
        {
            public static int Reads;
            public static bool ThrowOnRead;
            public TrackingCulture() : base("") { }
            public override System.Globalization.TextInfo TextInfo
            {
                get
                {
                    Reads++;
                    if (ThrowOnRead) throw new System.InvalidOperationException("culture-callback");
                    return base.TextInfo;
                }
            }
        }
        public static class Probe
        {
            public static string Run()
            {
                var previous = System.Globalization.CultureInfo.CurrentCulture;
                var culture = new TrackingCulture();
                System.Globalization.CultureInfo.CurrentCulture = culture;
                TrackingCulture.Reads = 0;
                TrackingCulture.ThrowOnRead = $THROW$;
                try
                {
                    try
                    {
                        var result = $CALL$;
                        var reads = TrackingCulture.Reads;
                        return result + ":" + reads;
                    }
                    catch (System.InvalidOperationException error) when (error.Message == "culture-callback")
                    {
                        return "throw:" + TrackingCulture.Reads;
                    }
                }
                finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
            }
        }
        """;

    private const string OriginalStringLinq = """
        using System.Linq;
        using System.Collections.Generic;

        public class Transform
        {
            public List<string> GetNames(List<string> items)
            {
                return items.Where(s => s.Length > 0)
                            .Select(s => s.ToUpper())
                            .OrderBy(s => s)
                            .ToList();
            }
        }
        """;

    [Theory]
    [InlineData("System.Int32")]
    [InlineData("System.Int64")]
    [InlineData("System.Double")]
    [InlineData("System.Boolean")]
    public void NumericContracts_ExactAuthorityCannotEscapeThroughDefaultsOverloadsOrCaches(string typeName)
    {
        var compilation = StringProbeCompilation("", []);
        var type = compilation.GetTypeByMetadataName(typeName)!;
        Assert.Equal(typeof(int).Assembly.FullName, type.ContainingAssembly.Identity.ToString());
        var methods = type.GetMembers().OfType<IMethodSymbol>()
            .Where(method => method.DeclaredAccessibility == Accessibility.Public
                && method.Name is "Parse" or "TryParse" or "ToString" or "Equals" or "CompareTo" or "GetHashCode")
            .ToArray();
        foreach (var safeFirst in new[] { false, true })
        {
            var loader = new ManifestLoader(loadUserLevelManifests: false);
            var resolver = new EffectResolver(loader);
            resolver.Initialize();
            var mapping = Assert.Single(loader.LoadedManifests.SelectMany(entry => entry.Manifest.Mappings)
                .Where(mapping => mapping.Type == typeName));
            Assert.Null(mapping.DefaultEffects);
            Assert.All(mapping.Methods!.Keys, signature => Assert.Contains("(", signature));
            var safe = NumericKey(methods.Single(method => method.Name == "GetHashCode"));
            if (safeFirst)
                Assert.Equal(EffectResolutionStatus.PureExplicit, resolver.Resolve(safe).Status);
            foreach (var method in methods.Concat(methods.Reverse()))
            {
                Assert.DoesNotContain(method.Locations, location => location.IsInSource);
                var key = NumericKey(method);
                var safeValueOperation = method.Name == "GetHashCode"
                    || (method.Name is "Equals" or "CompareTo" && method.Parameters.Length == 1
                        && SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, type))
                    || (typeName == "System.Boolean" && method.Name == "ToString");
                var resolution = resolver.Resolve(key);
                Assert.Equal(safeValueOperation ? EffectResolutionStatus.PureExplicit : EffectResolutionStatus.Unknown,
                    resolution.Status);
                Assert.Equal(!safeValueOperation, resolution.Effects.IsUnknown);
                Assert.Same(resolution, resolver.Resolve(key));
                Assert.Equal(EffectResolutionStatus.Unknown, resolver.Resolve(key.WithoutParameterList()).Status);
                Assert.Equal(EffectResolutionStatus.Unknown, resolver.Resolve(
                    EffectResolverKey.FromStrings(typeName, method.Name, ["?"])).Status);
            }
            foreach (var key in new[]
            {
                EffectResolverKey.FromStrings(typeName, "Uncovered", []),
                EffectResolverKey.FromStrings(typeName, "ToString", ["System.String"]),
                EffectResolverKey.FromStrings(typeName, "GetHashCode", kind: EffectMemberKind.Getter),
                EffectResolverKey.FromStrings(typeName, "GetHashCode", kind: EffectMemberKind.Setter),
                EffectResolverKey.FromStrings(typeName, ".ctor", [], EffectMemberKind.Constructor),
                EffectResolverKey.FromStrings("Vendor." + type.Name, "GetHashCode", [])
            })
                Assert.Equal(EffectResolutionStatus.Unknown, resolver.Resolve(key).Status);
        }
    }

    [Theory]
    [InlineData("int", "value.GetHashCode()", "int", "-1234", "0", "-1234")]
    [InlineData("int", "value.CompareTo(other)", "int", "-2147483648", "2147483647", "-1")]
    [InlineData("int", "value.Equals(other)", "bool", "1", "1", "True")]
    [InlineData("long", "value.GetHashCode()", "int", "-1234L", "0L", "1233")]
    [InlineData("long", "value.CompareTo(other)", "int", "-1234L", "0L", "-1")]
    [InlineData("long", "value.Equals(other)", "bool", "1L", "1L", "True")]
    [InlineData("double", "value.GetHashCode()", "int", "-0.0", "0.0", "0")]
    [InlineData("double", "value.CompareTo(other)", "int", "double.NaN", "0.0", "-1")]
    [InlineData("double", "value.CompareTo(other)", "int", "0.0", "double.NaN", "1")]
    [InlineData("double", "value.Equals(other)", "bool", "double.NaN", "double.NaN", "True")]
    [InlineData("bool", "value.GetHashCode()", "int", "true", "false", "1")]
    [InlineData("bool", "value.CompareTo(other)", "int", "false", "true", "-1")]
    [InlineData("bool", "value.Equals(other)", "bool", "true", "false", "False")]
    [InlineData("bool", "value.ToString()", "string", "true", "false", "True")]
    [InlineData("bool", "value.ToString(provider)", "string", "false", "true", "False")]
    public void NumericContracts_SafeSelectedMembersExecuteWithoutCultureOrProviderCallbacks(
        string type, string expression, string returnType, string value, string other, string expected)
    {
        var source = $$"""
            public static class Target
            {
                public static {{returnType}} Apply({{type}} value, {{type}} other, System.IFormatProvider provider) => {{expression}};
            }
            """;
        var selected = SelectedNumericMethod(source);
        var resolution = new EffectResolver(new ManifestLoader(loadUserLevelManifests: false))
            .Resolve(NumericKey(selected));
        Assert.Equal(EffectResolutionStatus.PureExplicit, resolution.Status);
        Assert.Contains("embedded:Calor.Compiler.Manifests.bcl-system", resolution.Source);
        var conversion = new CSharpToCalorConverter(new ConversionOptions { ModuleName = "NumericContracts" })
            .Convert(source);
        Assert.True(conversion.Success);
        Assert.Equal(0, conversion.InteropPreservationCount);
        foreach (var native in StringEmissions(conversion.CalorSource!))
        foreach (var typing in new[] { false, true })
        {
            var result = Calor.Compiler.Program.Compile(native, "safe-numeric.calr",
                new Calor.Compiler.CompilationOptions
                {
                    EnableTypeChecking = typing, VerifyContracts = false, StatusWriter = TextWriter.Null
                });
            Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
            foreach (var symbols in new string[][] { [], ["NET6_0_OR_GREATER"] })
            foreach (var providerIsCulture in new[] { false, true })
            {
                var witness = NumericWitness($"Target.Apply({value}, {other}, provider)", throws: true, providerIsCulture);
                Assert.Equal(expected + ":0", RunStringProbe(source + witness, symbols));
                Assert.Equal(expected + ":0", RunStringProbe(result.GeneratedCode + witness, symbols));
            }
        }
    }

    [Theory]
    [InlineData("int")]
    [InlineData("long")]
    [InlineData("double")]
    public void NumericContracts_ParsingAndFormattingDispatchInOriginalAndAreStrictlyRejected(string type)
    {
        foreach (var (expression, returnType, expected) in new[]
        {
            ($"{type}.Parse(\"1234\")", type, "1234"),
            ($"{type}.Parse(\"1234\", provider)", type, "1234"),
            ($"{type}.Parse(\"1234\", System.Globalization.NumberStyles.Integer, provider)", type, "1234"),
            ($"(({type})(-1234)).ToString()", "string", "-1234"),
            ($"(({type})(-1234)).ToString(provider)", "string", "-1234"),
            ($"(({type})(-1234)).ToString(\"D\", provider)", "string", "-1234")
        }.Where(entry => type != "double" || !entry.Item1.Contains("\"D\"", StringComparison.Ordinal)))
        {
            var source = $$"""
                public static class Target
                {
                    public static {{returnType}} Apply(System.IFormatProvider provider) => {{expression}};
                }
                """;
            foreach (var symbols in new string[][] { [], ["NET6_0_OR_GREATER"] })
            foreach (var throws in new[] { false, true })
            foreach (var providerIsCulture in new[] { false, true })
                Assert.Equal(throws ? "throw:1" : expected + ":1", RunStringProbe(source
                    + NumericWitness("Target.Apply(provider)", throws, providerIsCulture), symbols));
            AssertNumericRejected(source, $$"""
                module NumericContracts overflow unchecked nullable oblivious
                  module Target
                    func Apply (System.IFormatProvider:provider) → {{returnType}}
                      return {{expression}}
                """);
        }

        var invariantSource = $$"""
            public static class Target
            {
                public static {{type}} Apply(System.IFormatProvider provider) =>
                    {{type}}.Parse("1234", System.Globalization.CultureInfo.InvariantCulture);
            }
            """;
        foreach (var symbols in new string[][] { [], ["NET6_0_OR_GREATER"] })
            Assert.Equal("1234:0", RunStringProbe(invariantSource
                + NumericWitness("Target.Apply(provider)", throws: true, providerIsCulture: true), symbols));
        AssertNumericRejected(invariantSource, $$"""
            module NumericContracts overflow unchecked
              module Target
                func Apply (System.IFormatProvider:provider) → {{type}}
                  return {{type}}.Parse("1234", System.Globalization.CultureInfo.InvariantCulture)
            """);

        foreach (var explicitProvider in new[] { false, true })
        {
            var arguments = explicitProvider
                ? "\"1234\", System.Globalization.NumberStyles.Integer, provider, out var value"
                : "\"1234\", out var value";
            var source = $$"""
                public static class Target
                {
                    public static bool Apply(System.IFormatProvider provider) => {{type}}.TryParse({{arguments}});
                }
                """;
            Assert.Equal(EffectResolutionStatus.Unknown,
                new EffectResolver(new ManifestLoader(loadUserLevelManifests: false))
                    .Resolve(NumericKey(SelectedNumericMethod(source))).Status);
            foreach (var symbols in new string[][] { [], ["NET6_0_OR_GREATER"] })
            foreach (var throws in new[] { false, true })
            foreach (var providerIsCulture in new[] { false, true })
                Assert.Equal(throws ? "throw:1" : "True:1", RunStringProbe(source
                    + NumericWitness("Target.Apply(provider)", throws, providerIsCulture), symbols));
        }
    }

    [Fact]
    public void NumericContracts_BooleanParseSuccessDoesNotProveItsExceptionPathPure()
    {
        const string source = """
            public static class Target
            {
                public static bool Apply(System.IFormatProvider provider) => bool.Parse("invalid");
            }
            """;
        foreach (var symbols in new string[][] { [], ["NET6_0_OR_GREATER"] })
        {
            var mutating = Assert.IsType<string>(RunStringProbe(source
                + NumericWitness("Target.Apply(provider)", throws: false, providerIsCulture: true), symbols));
            Assert.StartsWith("format:", mutating);
            Assert.True(int.Parse(mutating["format:".Length..], System.Globalization.CultureInfo.InvariantCulture) > 0);
            Assert.Equal("throw:1", RunStringProbe(source
                + NumericWitness("Target.Apply(provider)", throws: true, providerIsCulture: true), symbols));
            Assert.Equal("True:0", RunStringProbe(source.Replace("\"invalid\"", "\"true\"", StringComparison.Ordinal)
                + NumericWitness("Target.Apply(provider)", throws: true, providerIsCulture: true), symbols));
        }
        AssertNumericRejected(source, """
            module NumericContracts overflow unchecked
              module Target
                func Apply (System.IFormatProvider:provider) → bool
                  return bool.Parse("invalid")
            """);
    }

    [Theory]
    [InlineData("sbyte", "-128, -1, 0, 42, 127", "0080|00FF|0000|002A|007F")]
    [InlineData("byte", "0, 42, 255", "0000|002A|00FF")]
    [InlineData("short", "-32768, -1, 0, 42, 32767", "8000|FFFF|0000|002A|7FFF")]
    [InlineData("ushort", "0, 42, 65535", "0000|002A|FFFF")]
    [InlineData("int", "-2147483648, -42, -1, 0, 42, 2147483647",
        "80000000|FFFFFFD6|FFFFFFFF|0000|002A|7FFFFFFF")]
    [InlineData("uint", "0U, 42U, 4294967295U", "0000|002A|FFFFFFFF")]
    [InlineData("long", "-9223372036854775808L, -42L, -1L, 0L, 42L, 9223372036854775807L",
        "8000000000000000|FFFFFFFFFFFFFFD6|FFFFFFFFFFFFFFFF|0000|002A|7FFFFFFFFFFFFFFF")]
    [InlineData("ulong", "0UL, 42UL, 18446744073709551615UL", "0000|002A|FFFFFFFFFFFFFFFF")]
    public void HexInterpolation_ClosedHandlerPathHasNoCultureCallbacksForEitherSign(
        string type, string values, string expected)
    {
        const string interpolation = "$\"{{literal}} {value,8:X4}\"";
        const string lower = "$\"{value,-20:x16}\"";
        const string wide = "$\"{value:X99}\"";
        var source = $$"""
            public static class Target
            {
                public static string Apply({{type}} value) => {{interpolation}};
                public static string Lower({{type}} value) => {{lower}};
                public static string Wide({{type}} value) => {{wide}};
            }
            """;
        var metadata = StringProbeCompilation(source, []);
        var selectedType = metadata.GetTypeByMetadataName("Target")!.GetMembers("Apply")
            .OfType<IMethodSymbol>().Single().Parameters[0].Type;
        Assert.Equal(typeof(int).Assembly.FullName, selectedType.ContainingAssembly.Identity.ToString());
        Assert.DoesNotContain(selectedType.Locations, location => location.IsInSource);
        Assert.Equal(EffectResolutionStatus.Unknown, new EffectResolver(new ManifestLoader(loadUserLevelManifests: false))
            .Resolve(EffectResolverKey.FromStrings(selectedType.ToDisplayString(), "ToString",
                ["System.String", "System.IFormatProvider"])).Status);
        var conversion = ConvertStringSource(source);
        Assert.True(conversion.Success, string.Join("\n", conversion.Issues));
        Assert.Equal(0, conversion.InteropPreservationCount);
        Assert.Empty(conversion.Losses);
        var direct = $$"""
            module HexContracts
              module Target
                func Apply ({{type}}:value) → string
                  !
                  return "{literal} ${value,8:X4}"
                func Lower ({{type}}:value) → string
                  !
                  return "${value,-20:x16}"
                func Wide ({{type}}:value) → string
                  !
                  return "${value:X99}"
            """;
        var hex = expected.Split('|');
        var answers = hex.Select(value => "{literal} " + value.PadLeft(8)).ToArray();
        var lowerAnswers = hex.Select(value => value.ToLowerInvariant().PadLeft(16, '0').PadRight(20)).ToArray();
        var wideAnswers = hex.Select(value => value.PadLeft(99, '0')).ToArray();
        var witness = NumericWitness("HexWitness.Run()", throws: true, providerIsCulture: true)
            .Replace("public override object GetFormat", """
                public override System.Globalization.NumberFormatInfo NumberFormat
                {
                    get
                    {
                        Reads++;
                        throw new System.InvalidOperationException("numeric-culture-callback");
                    }
                    set { throw new System.InvalidOperationException("numeric-culture-callback"); }
                }
                public override object GetFormat
                """, StringComparison.Ordinal)
            + $$"""

                public static class HexWitness
                {
                    public static bool Run()
                    {
                        var values = new {{type}}[] { {{values}} };
                        var answers = new string[] { {{string.Join(", ", answers.Select(value => SymbolDisplay.FormatLiteral(value, true)))}} };
                        var lower = new string[] { {{string.Join(", ", lowerAnswers.Select(value => SymbolDisplay.FormatLiteral(value, true)))}} };
                        var wide = new string[] { {{string.Join(", ", wideAnswers.Select(value => SymbolDisplay.FormatLiteral(value, true)))}} };
                        for (var index = 0; index < values.Length; index++)
                            if (Target.Apply(values[index]) != answers[index]
                                || Target.Lower(values[index]) != lower[index]
                                || Target.Wide(values[index]) != wide[index]) return false;
                        return true;
                    }
                }
                """;
        Assert.Equal("True:0", RunStringProbe(source + witness, []));
        foreach (var native in StringEmissions(direct).Concat(StringEmissions(conversion.CalorSource!)))
        foreach (var typing in new[] { false, true })
        {
            var result = Calor.Compiler.Program.Compile(native, "safe-hex.calr",
                new Calor.Compiler.CompilationOptions
                {
                    EnableTypeChecking = typing, EnforceEffects = true, StrictEffects = true,
                    StrictApi = true, UnknownCallPolicy = UnknownCallPolicy.Strict,
                    VerifyContracts = false, StatusWriter = TextWriter.Null
                });
            Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
            Assert.Equal("True:0", RunStringProbe(result.GeneratedCode + witness, []));
        }
    }

    private static IMethodSymbol SelectedNumericMethod(string source)
    {
        var compilation = StringProbeCompilation(source, []);
        Assert.DoesNotContain(compilation.GetDiagnostics(),
            diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var selected = Assert.IsAssignableFrom<IMethodSymbol>(model.GetSymbolInfo(
            Assert.Single(tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())).Symbol);
        Assert.Contains(selected.ContainingType.SpecialType, new[]
        {
            SpecialType.System_Int32, SpecialType.System_Int64, SpecialType.System_Double, SpecialType.System_Boolean
        });
        Assert.Equal(typeof(int).Assembly.FullName, selected.ContainingAssembly.Identity.ToString());
        Assert.DoesNotContain(selected.Locations, location => location.IsInSource);
        return selected;
    }

    private static EffectResolverKey NumericKey(IMethodSymbol method)
    {
        var format = new SymbolDisplayFormat(typeQualificationStyle:
            SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);
        return EffectResolverKey.FromStrings(method.ContainingType.ToDisplayString(format), method.Name,
            method.Parameters.Select(parameter => parameter.Type.ToDisplayString(format)).ToArray());
    }

    private static void AssertNumericRejected(string source, string native)
    {
        var selected = SelectedNumericMethod(source);
        Assert.Equal(EffectResolutionStatus.Unknown,
            new EffectResolver(new ManifestLoader(loadUserLevelManifests: false)).Resolve(NumericKey(selected)).Status);
        var conversion = new CSharpToCalorConverter(new ConversionOptions { ModuleName = "NumericContracts" })
            .Convert(source);
        Assert.True(conversion.Success);
        Assert.Equal(1, conversion.InteropPreservationCount);
        foreach (var emission in StringEmissions(native))
        foreach (var typing in new[] { false, true })
        {
            var result = Calor.Compiler.Program.Compile(emission, "unknown-numeric.calr",
                new Calor.Compiler.CompilationOptions
                {
                    EnableTypeChecking = typing, VerifyContracts = false, StatusWriter = TextWriter.Null
                });
            Assert.True(result.HasErrors);
            Assert.Empty(result.GeneratedCode);
            Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("'unknown'", StringComparison.Ordinal));
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall
                && diagnostic.Message.Contains("." + selected.Name, StringComparison.Ordinal));
        }
    }

    private static string NumericWitness(string call, bool throws, bool providerIsCulture) => $$"""

        public sealed class TrackingNumericCulture : System.Globalization.CultureInfo
        {
            public static int Reads;
            public static bool Throw;
            public TrackingNumericCulture() : base("") { }
            public override object GetFormat(System.Type type)
            {
                Reads++;
                if (Throw) throw new System.InvalidOperationException("numeric-culture-callback");
                return base.GetFormat(type);
            }
            public override string Name
            {
                get
                {
                    Reads++;
                    if (Throw) throw new System.InvalidOperationException("numeric-culture-callback");
                    return base.Name;
                }
            }
        }
        public sealed class TrackingNumericProvider : System.IFormatProvider
        {
            public object GetFormat(System.Type type)
            {
                TrackingNumericCulture.Reads++;
                if (TrackingNumericCulture.Throw)
                    throw new System.InvalidOperationException("numeric-culture-callback");
                return System.Globalization.NumberFormatInfo.InvariantInfo;
            }
        }
        public static class Probe
        {
            public static string Run()
            {
                var previous = System.Globalization.CultureInfo.CurrentCulture;
                var previousUI = System.Globalization.CultureInfo.CurrentUICulture;
                try
                {
                    var culture = new TrackingNumericCulture();
                    System.Globalization.CultureInfo.CurrentCulture = culture;
                    System.Globalization.CultureInfo.CurrentUICulture = culture;
                    System.IFormatProvider provider = {{(providerIsCulture ? "culture" : "new TrackingNumericProvider()")}};
                    TrackingNumericCulture.Reads = 0;
                    TrackingNumericCulture.Throw = {{(throws ? "true" : "false")}};
                    try
                    {
                        var value = {{call}};
                        var reads = TrackingNumericCulture.Reads;
                        return System.Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
                            + ":" + reads.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }
                    catch (System.InvalidOperationException error) when (error.Message == "numeric-culture-callback")
                    {
                        var reads = TrackingNumericCulture.Reads;
                        return "throw:" + reads.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }
                    catch (System.FormatException)
                    {
                        var reads = TrackingNumericCulture.Reads;
                        return "format:" + reads.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }
                }
                finally
                {
                    System.Globalization.CultureInfo.CurrentCulture = previous;
                    System.Globalization.CultureInfo.CurrentUICulture = previousUI;
                }
            }
        }
        """;

    [Fact]
    public void Resolve_SelectsSignatureSpecificMethodAndConstructorOverloads()
    {
        var json = """
            {
              "version": "1.0",
              "mappings": [{
                "type": "Example.Widget",
                "methods": {
                  "Transform(Int32)": ["cw"],
                  "Transform(String)": ["fs:r"]
                },
                "constructors": {
                  "(Int32)": ["net:w"],
                  "(String)": ["db:r"]
                }
              }]
            }
            """;
        var loader = new ManifestLoader();
        loader.LoadFromJson(json, "signature-test");
        var resolver = new EffectResolver(loader);
        resolver.Initialize();

        Assert.True(resolver.Resolve(EffectResolverKey.FromStrings("Example.Widget", "Transform", ["i32"]))
            .Effects.Contains(EffectKind.IO, "console_write"));
        Assert.True(resolver.Resolve(EffectResolverKey.FromStrings("Example.Widget", "Transform", ["str"]))
            .Effects.Contains(EffectKind.IO, "filesystem_read"));
        Assert.Equal(EffectResolutionStatus.Unknown,
            resolver.Resolve(EffectResolverKey.FromStrings("Example.Widget", "Transform", ["bool"])).Status);

        Assert.True(resolver.Resolve(EffectResolverKey.FromStrings("Example.Widget", ".ctor", ["i32"], EffectMemberKind.Constructor))
            .Effects.Contains(EffectKind.IO, "network_write"));
        Assert.True(resolver.Resolve(EffectResolverKey.FromStrings("Example.Widget", ".ctor", ["str"], EffectMemberKind.Constructor))
            .Effects.Contains(EffectKind.IO, "database_read"));
        Assert.Equal(EffectResolutionStatus.Unknown,
            resolver.Resolve(EffectResolverKey.FromStrings("Example.Widget", ".ctor", ["bool"], EffectMemberKind.Constructor)).Status);

        Assert.Equal(
            new[] { "System.String", "System.Collections.Generic.List`1<System.Int32>" },
            EffectResolver.ParseParameterSignature(
                "(System.String,System.Collections.Generic.List`1<System.Int32>)"));
    }

    [Fact]
    public void Resolve_ReturnsManifestEffects_ForKnownBclMethods()
    {
        var resolver = new EffectResolver();
        resolver.Initialize();

        var resolution = resolver.Resolve(EffectResolverKey.FromStrings("System.Console", "WriteLine"));

        Assert.Equal(EffectResolutionStatus.Resolved, resolution.Status);
        Assert.Contains(resolution.Effects.Effects, e => e.Value == "console_write");
        Assert.Contains("embedded:", resolution.Source);
    }

    [Fact]
    public void Resolve_ReturnsPureExplicit_ForMathMethods()
    {
        var resolver = new EffectResolver();
        resolver.Initialize();

        var resolution = resolver.Resolve(EffectResolverKey.FromStrings("System.Math", "Max"));

        // Math.Max should be pure (no effects)
        Assert.True(resolution.Effects.IsEmpty || resolution.Status == EffectResolutionStatus.PureExplicit);
    }

    [Theory]
    [InlineData("Dict<str, i32>", "Count", false)]
    [InlineData("Dict<str, i32>", "Keys", true)]
    [InlineData("Dict<str, i32>", "Values", true)]
    [InlineData("SortedDictionary<str, i32>", "Count", false)]
    [InlineData("SortedDictionary<str, i32>", "Keys", true)]
    [InlineData("SortedDictionary<str, i32>", "Values", true)]
    public void ResolveDictionaryGetter_UsesActualReceiverAndChargesViewAllocation(
        string type, string member, bool allocates)
    {
        var resolver = new EffectResolver();
        resolver.Initialize();
        var resolution = resolver.Resolve(EffectResolverKey.FromStrings(
            EffectEnforcementPass.MapShortTypeNameToFullName(type), member, kind: EffectMemberKind.Getter));
        Assert.NotEqual(EffectResolutionStatus.Unknown, resolution.Status);
        Assert.Equal(allocates, !resolution.Effects.IsEmpty);
        Assert.True(resolution.Effects.IsSubsetOf(EffectSet.From("alloc")));
    }

    [Fact]
    public void SortedDictionaryGetterCoverage_DoesNotMakeUncoveredMembersPure()
    {
        var resolver = new EffectResolver();
        resolver.Initialize();
        Assert.Equal(EffectResolutionStatus.Unknown, resolver.Resolve(
            EffectResolverKey.FromStrings("System.Collections.Generic.SortedDictionary`2", "Uncovered")).Status);
    }

    [Fact]
    public void InvariantCultureGetter_HasAnExplicitPureContract()
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        Assert.True(culture.IsReadOnly);
        Assert.Equal("", culture.Name);
        Assert.Same(culture, System.Globalization.CultureInfo.InvariantCulture);

        var resolver = new EffectResolver();
        resolver.Initialize();
        var resolution = resolver.Resolve(EffectResolverKey.FromStrings(
            "System.Globalization.CultureInfo", "InvariantCulture", kind: EffectMemberKind.Getter));

        Assert.Equal(EffectResolutionStatus.PureExplicit, resolution.Status);
        Assert.True(resolution.Effects.IsEmpty);
        Assert.Contains("embedded:", resolution.Source);
        Assert.Contains("bcl-system", resolution.Source);
    }

    [Theory]
    [InlineData("System.Globalization.CultureInfo", "CurrentCulture", EffectMemberKind.Getter)]
    [InlineData("System.Globalization.CultureInfo", "CurrentUICulture", EffectMemberKind.Getter)]
    [InlineData("System.Globalization.CultureInfo", "InvariantCulture", EffectMemberKind.Setter)]
    [InlineData("System.Globalization.CultureInfo", "InvariantCulture", EffectMemberKind.Method)]
    [InlineData("System.Globalization.CultureInfo", "ClearCachedData", EffectMemberKind.Method)]
    [InlineData("System.Globalization.CultureInfo", ".ctor", EffectMemberKind.Constructor)]
    [InlineData("System.Globalization.OtherCultureInfo", "InvariantCulture", EffectMemberKind.Getter)]
    [InlineData("Vendor.CultureInfo", "InvariantCulture", EffectMemberKind.Getter)]
    public void InvariantCultureGetter_DoesNotAuthorizeOtherMembers(
        string type, string member, EffectMemberKind kind)
    {
        var resolver = new EffectResolver();
        resolver.Initialize();
        var resolution = resolver.Resolve(EffectResolverKey.FromStrings(type, member, kind: kind));

        Assert.Equal(EffectResolutionStatus.Unknown, resolution.Status);
        Assert.True(resolution.Effects.IsUnknown);
    }

    [Fact]
    public void Resolve_ReturnsUnknown_ForUnknownMethod()
    {
        var resolver = new EffectResolver();
        resolver.Initialize();

        var resolution = resolver.Resolve(EffectResolverKey.FromStrings("UnknownNamespace.UnknownType", "UnknownMethod"));

        Assert.Equal(EffectResolutionStatus.Unknown, resolution.Status);
        Assert.True(resolution.Effects.IsUnknown);
    }

    [Fact]
    public void Resolve_UsesManifestForSpecificMethod()
    {
        var json = @"{
            ""version"": ""1.0"",
            ""mappings"": [
                {
                    ""type"": ""MyApp.CustomService"",
                    ""methods"": {
                        ""DoWork"": [""net:w"", ""db:w""]
                    }
                }
            ]
        }";

        var loader = new ManifestLoader();
        loader.LoadFromJson(json, "test");

        var resolver = new EffectResolver(loader);
        resolver.Initialize();

        var resolution = resolver.Resolve(EffectResolverKey.FromStrings("MyApp.CustomService", "DoWork"));

        Assert.Equal(EffectResolutionStatus.Resolved, resolution.Status);
        Assert.Contains(resolution.Effects.Effects, e => e.Value == "network_write");
        Assert.Contains(resolution.Effects.Effects, e => e.Value == "database_write");
    }

    [Fact]
    public void Resolve_UsesWildcard_WhenNoSpecificMethod()
    {
        var json = @"{
            ""version"": ""1.0"",
            ""mappings"": [
                {
                    ""type"": ""MyApp.PureService"",
                    ""methods"": {
                        ""*"": []
                    }
                }
            ]
        }";

        var loader = new ManifestLoader();
        loader.LoadFromJson(json, "test");

        var resolver = new EffectResolver(loader);
        resolver.Initialize();

        var resolution = resolver.Resolve(EffectResolverKey.FromStrings("MyApp.PureService", "AnyMethod"));

        Assert.Equal(EffectResolutionStatus.PureExplicit, resolution.Status);
        Assert.True(resolution.Effects.IsEmpty);
    }

    [Fact]
    public void Resolve_UsesDefaultEffects_WhenNoMethodMatch()
    {
        var json = @"{
            ""version"": ""1.0"",
            ""mappings"": [
                {
                    ""type"": ""MyApp.IoService"",
                    ""defaultEffects"": [""fs:rw""]
                }
            ]
        }";

        var loader = new ManifestLoader();
        loader.LoadFromJson(json, "test");

        var resolver = new EffectResolver(loader);
        resolver.Initialize();

        var resolution = resolver.Resolve(EffectResolverKey.FromStrings("MyApp.IoService", "SomeMethod"));

        Assert.Equal(EffectResolutionStatus.Resolved, resolution.Status);
        Assert.Contains(resolution.Effects.Effects, e => e.Value == "filesystem_readwrite");
    }

    [Fact]
    public void Resolve_UsesNamespaceDefaults_WhenNoTypeMatch()
    {
        var json = @"{
            ""version"": ""1.0"",
            ""mappings"": [],
            ""namespaceDefaults"": {
                ""MyApp.Data"": [""db:rw""]
            }
        }";

        var loader = new ManifestLoader();
        loader.LoadFromJson(json, "test");

        var resolver = new EffectResolver(loader);
        resolver.Initialize();

        var resolution = resolver.Resolve(EffectResolverKey.FromStrings("MyApp.Data.Repository", "GetAll"));

        Assert.Equal(EffectResolutionStatus.Resolved, resolution.Status);
        Assert.Contains(resolution.Effects.Effects, e => e.Value == "database_readwrite");
    }

    [Fact]
    public void Resolve_SpecificMethodOverridesWildcard()
    {
        var json = @"{
            ""version"": ""1.0"",
            ""mappings"": [
                {
                    ""type"": ""MyApp.Service"",
                    ""methods"": {
                        ""SpecificMethod"": [""cw""],
                        ""*"": [""fs:rw""]
                    }
                }
            ]
        }";

        var loader = new ManifestLoader();
        loader.LoadFromJson(json, "test");

        var resolver = new EffectResolver(loader);
        resolver.Initialize();

        var specificResolution = resolver.Resolve(EffectResolverKey.FromStrings("MyApp.Service", "SpecificMethod"));
        var otherResolution = resolver.Resolve(EffectResolverKey.FromStrings("MyApp.Service", "OtherMethod"));

        Assert.Contains(specificResolution.Effects.Effects, e => e.Value == "console_write");
        Assert.DoesNotContain(specificResolution.Effects.Effects, e => e.Value == "filesystem_readwrite");

        Assert.Contains(otherResolution.Effects.Effects, e => e.Value == "filesystem_readwrite");
    }

    [Fact]
    public void ResolveGetter_ReturnsPropertyEffects()
    {
        var json = @"{
            ""version"": ""1.0"",
            ""mappings"": [
                {
                    ""type"": ""MyApp.Config"",
                    ""getters"": {
                        ""Value"": [""env:r""]
                    }
                }
            ]
        }";

        var loader = new ManifestLoader();
        loader.LoadFromJson(json, "test");

        var resolver = new EffectResolver(loader);
        resolver.Initialize();

        var resolution = resolver.Resolve(EffectResolverKey.FromStrings("MyApp.Config", "Value", kind: EffectMemberKind.Getter));

        Assert.Equal(EffectResolutionStatus.Resolved, resolution.Status);
        Assert.Contains(resolution.Effects.Effects, e => e.Value == "environment_read");
    }

    [Fact]
    public void ResolveSetter_ReturnsPropertyEffects()
    {
        var json = @"{
            ""version"": ""1.0"",
            ""mappings"": [
                {
                    ""type"": ""MyApp.Config"",
                    ""setters"": {
                        ""Value"": [""env:w""]
                    }
                }
            ]
        }";

        var loader = new ManifestLoader();
        loader.LoadFromJson(json, "test");

        var resolver = new EffectResolver(loader);
        resolver.Initialize();

        var resolution = resolver.Resolve(EffectResolverKey.FromStrings("MyApp.Config", "Value", kind: EffectMemberKind.Setter));

        Assert.Equal(EffectResolutionStatus.Resolved, resolution.Status);
        Assert.Contains(resolution.Effects.Effects, e => e.Value == "environment_write");
    }

    [Fact]
    public void ResolveConstructor_ReturnsConstructorEffects()
    {
        var json = @"{
            ""version"": ""1.0"",
            ""mappings"": [
                {
                    ""type"": ""MyApp.FileService"",
                    ""constructors"": {
                        ""(String)"": [""fs:r""]
                    }
                }
            ]
        }";

        var loader = new ManifestLoader();
        loader.LoadFromJson(json, "test");

        var resolver = new EffectResolver(loader);
        resolver.Initialize();

        var resolution = resolver.Resolve(EffectResolverKey.FromStrings("MyApp.FileService", ".ctor", ["String"], EffectMemberKind.Constructor));

        Assert.Equal(EffectResolutionStatus.Resolved, resolution.Status);
        Assert.Contains(resolution.Effects.Effects, e => e.Value == "filesystem_read");
    }

    [Fact]
    public void Resolve_CachesResults()
    {
        var resolver = new EffectResolver();
        resolver.Initialize();

        var resolution1 = resolver.Resolve(EffectResolverKey.FromStrings("System.Console", "WriteLine"));
        var resolution2 = resolver.Resolve(EffectResolverKey.FromStrings("System.Console", "WriteLine"));

        // Should return same reference due to caching
        Assert.Same(resolution1, resolution2);
    }

    [Fact]
    public void LoadErrors_ReportsManifestProblems()
    {
        var loader = new ManifestLoader();
        loader.LoadFromJson("{ invalid }", "bad-manifest");

        var resolver = new EffectResolver(loader);
        resolver.Initialize();

        Assert.NotEmpty(resolver.LoadErrors);
    }

    // ========================================================================
    // Tier B: Framework interface manifest tests
    // ========================================================================

    [Theory]
    [InlineData("Microsoft.Extensions.Logging.ILogger", "Log", "console_write")]
    [InlineData("Microsoft.Extensions.Logging.LoggerExtensions", "LogInformation", "console_write")]
    [InlineData("Microsoft.Extensions.Logging.LoggerExtensions", "LogError", "console_write")]
    [InlineData("Microsoft.Extensions.Logging.LoggerExtensions", "LogWarning", "console_write")]
    [InlineData("Microsoft.Extensions.Logging.LoggerExtensions", "LogDebug", "console_write")]
    [InlineData("Microsoft.Extensions.Logging.LoggerExtensions", "LogCritical", "console_write")]
    public void TierB_Logger_ResolvesToConsoleWrite(string type, string method, string expectedValue)
    {
        var resolver = new EffectResolver();
        resolver.Initialize();

        var resolution = resolver.Resolve(EffectResolverKey.FromStrings(type, method));

        Assert.NotEqual(EffectResolutionStatus.Unknown, resolution.Status);
        Assert.Contains(resolution.Effects.Effects, e => e.Value == expectedValue);
    }

    [Fact]
    public void TierB_ILogger_IsEnabled_IsPure()
    {
        var resolver = new EffectResolver();
        resolver.Initialize();

        var resolution = resolver.Resolve(EffectResolverKey.FromStrings("Microsoft.Extensions.Logging.ILogger", "IsEnabled"));

        Assert.Equal(EffectResolutionStatus.PureExplicit, resolution.Status);
    }

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore.DbContext", "SaveChanges", "database_write")]
    [InlineData("Microsoft.EntityFrameworkCore.DbContext", "SaveChangesAsync", "database_write")]
    [InlineData("Microsoft.EntityFrameworkCore.DbContext", "Find", "database_read")]
    [InlineData("Microsoft.EntityFrameworkCore.DbContext", "FindAsync", "database_read")]
    public void TierB_DbContext_ResolvesToDatabaseEffects(string type, string method, string expectedValue)
    {
        var resolver = new EffectResolver();
        resolver.Initialize();

        var resolution = resolver.Resolve(EffectResolverKey.FromStrings(type, method));

        Assert.NotEqual(EffectResolutionStatus.Unknown, resolution.Status);
        Assert.Contains(resolution.Effects.Effects, e => e.Value == expectedValue);
    }

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore.DbContext", "Add", "heap_write")]
    [InlineData("Microsoft.EntityFrameworkCore.DbContext", "Update", "heap_write")]
    [InlineData("Microsoft.EntityFrameworkCore.DbContext", "Remove", "heap_write")]
    public void TierB_DbContext_MutationMethods_ResolveToMut(string type, string method, string expectedValue)
    {
        var resolver = new EffectResolver();
        resolver.Initialize();

        var resolution = resolver.Resolve(EffectResolverKey.FromStrings(type, method));

        Assert.NotEqual(EffectResolutionStatus.Unknown, resolution.Status);
        Assert.Contains(resolution.Effects.Effects, e => e.Value == expectedValue);
    }

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions", "ToListAsync", "database_read")]
    [InlineData("Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions", "FirstOrDefaultAsync", "database_read")]
    [InlineData("Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions", "CountAsync", "database_read")]
    [InlineData("Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions", "AnyAsync", "database_read")]
    public void TierB_EfCoreQueryExtensions_ResolveToDbRead(string type, string method, string expectedValue)
    {
        var resolver = new EffectResolver();
        resolver.Initialize();

        var resolution = resolver.Resolve(EffectResolverKey.FromStrings(type, method));

        Assert.NotEqual(EffectResolutionStatus.Unknown, resolution.Status);
        Assert.Contains(resolution.Effects.Effects, e => e.Value == expectedValue);
    }

    [Theory]
    [InlineData("Microsoft.Extensions.Configuration.IConfiguration", "GetSection", "environment_read")]
    [InlineData("Microsoft.Extensions.Configuration.IConfigurationSection", "GetSection", "environment_read")]
    [InlineData("Microsoft.Extensions.Configuration.ConfigurationExtensions", "GetConnectionString", "environment_read")]
    public void TierB_Configuration_ResolvesToEnvRead(string type, string method, string expectedValue)
    {
        var resolver = new EffectResolver();
        resolver.Initialize();

        var resolution = resolver.Resolve(EffectResolverKey.FromStrings(type, method));

        Assert.NotEqual(EffectResolutionStatus.Unknown, resolution.Status);
        Assert.Contains(resolution.Effects.Effects, e => e.Value == expectedValue);
    }

    [Fact]
    public void TierB_IServiceProvider_GetService_IsPure()
    {
        var resolver = new EffectResolver();
        resolver.Initialize();

        var resolution = resolver.Resolve(EffectResolverKey.FromStrings("Microsoft.Extensions.DependencyInjection.IServiceProvider", "GetService"));

        Assert.Equal(EffectResolutionStatus.PureExplicit, resolution.Status);
    }

    [Theory]
    [InlineData("Microsoft.Extensions.Hosting.IHost", "RunAsync", "network_readwrite")]
    [InlineData("Microsoft.Extensions.Hosting.IHost", "StartAsync", "network_readwrite")]
    [InlineData("Microsoft.Extensions.Hosting.HostingAbstractionsHostExtensions", "Run", "network_readwrite")]
    public void TierB_Hosting_ResolvesToNetRw(string type, string method, string expectedValue)
    {
        var resolver = new EffectResolver();
        resolver.Initialize();

        var resolution = resolver.Resolve(EffectResolverKey.FromStrings(type, method));

        Assert.NotEqual(EffectResolutionStatus.Unknown, resolution.Status);
        Assert.Contains(resolution.Effects.Effects, e => e.Value == expectedValue);
    }

    [Fact]
    public void TierB_IOptions_Value_IsPure()
    {
        var resolver = new EffectResolver();
        resolver.Initialize();

        var resolution = resolver.Resolve(EffectResolverKey.FromStrings("Microsoft.Extensions.Options.IOptions`1", "Value", kind: EffectMemberKind.Getter));

        Assert.Equal(EffectResolutionStatus.PureExplicit, resolution.Status);
    }

    [Theory]
    [InlineData("Microsoft.AspNetCore.Mvc.ControllerBase", "Ok")]
    [InlineData("Microsoft.AspNetCore.Mvc.ControllerBase", "NotFound")]
    [InlineData("Microsoft.AspNetCore.Mvc.ControllerBase", "BadRequest")]
    public void TierB_MvcController_ResultMethods_ArePure(string type, string method)
    {
        var resolver = new EffectResolver();
        resolver.Initialize();

        var resolution = resolver.Resolve(EffectResolverKey.FromStrings(type, method));

        Assert.Equal(EffectResolutionStatus.PureExplicit, resolution.Status);
    }

    [Fact]
    public void TierB_HttpResponse_WriteAsync_IsNetWrite()
    {
        var resolver = new EffectResolver();
        resolver.Initialize();

        var resolution = resolver.Resolve(EffectResolverKey.FromStrings("Microsoft.AspNetCore.Http.HttpResponse", "WriteAsync"));

        Assert.Equal(EffectResolutionStatus.Resolved, resolution.Status);
        Assert.Contains(resolution.Effects.Effects, e => e.Value == "network_write");
    }

    [Fact]
    public void TierB_DatabaseFacade_Migrate_IsDbWrite()
    {
        var resolver = new EffectResolver();
        resolver.Initialize();

        var resolution = resolver.Resolve(EffectResolverKey.FromStrings("Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade", "Migrate"));

        Assert.Equal(EffectResolutionStatus.Resolved, resolution.Status);
        Assert.Contains(resolution.Effects.Effects, e => e.Value == "database_write");
    }

    // ========================================================================
    // BCL manifest tests: System.Text.Json
    // ========================================================================

    [Theory]
    [InlineData("System.Text.Json.JsonSerializer", "Serialize")]
    [InlineData("System.Text.Json.JsonSerializer", "Deserialize")]
    [InlineData("System.Text.Json.JsonSerializer", "SerializeToUtf8Bytes")]
    public void BCL_JsonSerializer_PureMethods_ArePure(string type, string method)
    {
        var resolver = new EffectResolver();
        resolver.Initialize();
        var result = resolver.Resolve(EffectResolverKey.FromStrings(type, method));
        Assert.Equal(EffectResolutionStatus.PureExplicit, result.Status);
    }

    [Fact]
    public void BCL_JsonSerializer_SerializeAsync_HasFsWrite()
    {
        var resolver = new EffectResolver();
        resolver.Initialize();
        var result = resolver.Resolve(EffectResolverKey.FromStrings("System.Text.Json.JsonSerializer", "SerializeAsync"));
        Assert.Equal(EffectResolutionStatus.Resolved, result.Status);
        Assert.Contains(result.Effects.Effects, e => e.Value == "filesystem_write");
    }

    [Fact]
    public void BCL_JsonSerializer_DeserializeAsync_HasFsRead()
    {
        var resolver = new EffectResolver();
        resolver.Initialize();
        var result = resolver.Resolve(EffectResolverKey.FromStrings("System.Text.Json.JsonSerializer", "DeserializeAsync"));
        Assert.Equal(EffectResolutionStatus.Resolved, result.Status);
        Assert.Contains(result.Effects.Effects, e => e.Value == "filesystem_read");
    }

    [Fact]
    public void BCL_JsonElement_IsPure()
    {
        var resolver = new EffectResolver();
        resolver.Initialize();
        var result = resolver.Resolve(EffectResolverKey.FromStrings("System.Text.Json.JsonElement", "GetString"));
        Assert.Equal(EffectResolutionStatus.PureExplicit, result.Status);
    }

    // ========================================================================
    // BCL manifest tests: Regex
    // ========================================================================

    [Theory]
    [InlineData("System.Text.RegularExpressions.Regex", "IsMatch")]
    [InlineData("System.Text.RegularExpressions.Regex", "Match")]
    [InlineData("System.Text.RegularExpressions.Regex", "Replace")]
    [InlineData("System.Text.RegularExpressions.Regex", "Split")]
    public void BCL_Regex_AllMethods_ArePure(string type, string method)
    {
        var resolver = new EffectResolver();
        resolver.Initialize();
        var result = resolver.Resolve(EffectResolverKey.FromStrings(type, method));
        Assert.Equal(EffectResolutionStatus.PureExplicit, result.Status);
    }

    // ========================================================================
    // BCL manifest tests: Concurrent collections
    // ========================================================================

    [Theory]
    [InlineData("System.Collections.Concurrent.ConcurrentDictionary`2", "TryAdd", "heap_write")]
    [InlineData("System.Collections.Concurrent.ConcurrentDictionary`2", "TryRemove", "heap_write")]
    [InlineData("System.Collections.Concurrent.ConcurrentQueue`1", "Enqueue", "heap_write")]
    [InlineData("System.Collections.Concurrent.ConcurrentStack`1", "Push", "heap_write")]
    [InlineData("System.Collections.Concurrent.ConcurrentBag`1", "Add", "heap_write")]
    public void BCL_Concurrent_MutationMethods_HaveMut(string type, string method, string expectedValue)
    {
        var resolver = new EffectResolver();
        resolver.Initialize();
        var result = resolver.Resolve(EffectResolverKey.FromStrings(type, method));
        Assert.Equal(EffectResolutionStatus.Resolved, result.Status);
        Assert.Contains(result.Effects.Effects, e => e.Value == expectedValue);
    }

    [Theory]
    [InlineData("System.Collections.Concurrent.ConcurrentDictionary`2", "TryGetValue")]
    [InlineData("System.Collections.Concurrent.ConcurrentDictionary`2", "ContainsKey")]
    [InlineData("System.Collections.Concurrent.ConcurrentQueue`1", "TryPeek")]
    public void BCL_Concurrent_ReadMethods_ArePure(string type, string method)
    {
        var resolver = new EffectResolver();
        resolver.Initialize();
        var result = resolver.Resolve(EffectResolverKey.FromStrings(type, method));
        Assert.Equal(EffectResolutionStatus.PureExplicit, result.Status);
    }

    // ========================================================================
    // BCL manifest tests: Crypto
    // ========================================================================

    [Theory]
    [InlineData("System.Security.Cryptography.SHA256", "HashData")]
    [InlineData("System.Security.Cryptography.SHA256", "ComputeHash")]
    [InlineData("System.Security.Cryptography.Aes", "Create")]
    [InlineData("System.Security.Cryptography.RSA", "Encrypt")]
    public void BCL_Crypto_DeterministicMethods_ArePure(string type, string method)
    {
        var resolver = new EffectResolver();
        resolver.Initialize();
        var result = resolver.Resolve(EffectResolverKey.FromStrings(type, method));
        Assert.Equal(EffectResolutionStatus.PureExplicit, result.Status);
    }

    [Theory]
    [InlineData("System.Security.Cryptography.RandomNumberGenerator", "GetBytes", "random")]
    [InlineData("System.Security.Cryptography.RandomNumberGenerator", "GetInt32", "random")]
    public void BCL_Crypto_RandomMethods_HaveRand(string type, string method, string expectedValue)
    {
        var resolver = new EffectResolver();
        resolver.Initialize();
        var result = resolver.Resolve(EffectResolverKey.FromStrings(type, method));
        Assert.Equal(EffectResolutionStatus.Resolved, result.Status);
        Assert.Contains(result.Effects.Effects, e => e.Value == expectedValue);
    }

    // ========================================================================
    // Ecosystem manifest tests: Serilog
    // ========================================================================

    [Theory]
    [InlineData("Serilog.Log", "Information", "console_write")]
    [InlineData("Serilog.Log", "Warning", "console_write")]
    [InlineData("Serilog.Log", "Error", "console_write")]
    [InlineData("Serilog.Log", "Fatal", "console_write")]
    [InlineData("Serilog.Log", "Debug", "console_write")]
    [InlineData("Serilog.ILogger", "Information", "console_write")]
    [InlineData("Serilog.ILogger", "Write", "console_write")]
    public void Ecosystem_Serilog_LogMethods_ResolveToCw(string type, string method, string expectedValue)
    {
        var resolver = new EffectResolver();
        resolver.Initialize();
        var result = resolver.Resolve(EffectResolverKey.FromStrings(type, method));
        Assert.NotEqual(EffectResolutionStatus.Unknown, result.Status);
        Assert.Contains(result.Effects.Effects, e => e.Value == expectedValue);
    }

    [Fact]
    public void Ecosystem_Serilog_IsEnabled_IsPure()
    {
        var resolver = new EffectResolver();
        resolver.Initialize();
        var result = resolver.Resolve(EffectResolverKey.FromStrings("Serilog.ILogger", "IsEnabled"));
        Assert.Equal(EffectResolutionStatus.PureExplicit, result.Status);
    }

    // ========================================================================
    // Ecosystem manifest tests: Newtonsoft.Json
    // ========================================================================

    [Theory]
    [InlineData("Newtonsoft.Json.JsonConvert", "SerializeObject")]
    [InlineData("Newtonsoft.Json.JsonConvert", "DeserializeObject")]
    [InlineData("Newtonsoft.Json.Linq.JObject", "Parse")]
    [InlineData("Newtonsoft.Json.Linq.JArray", "Parse")]
    [InlineData("Newtonsoft.Json.Linq.JToken", "Parse")]
    public void Ecosystem_NewtonsoftJson_PureMethods(string type, string method)
    {
        var resolver = new EffectResolver();
        resolver.Initialize();
        var result = resolver.Resolve(EffectResolverKey.FromStrings(type, method));
        Assert.Equal(EffectResolutionStatus.PureExplicit, result.Status);
    }

    [Theory]
    [InlineData("Newtonsoft.Json.Linq.JObject", "Add", "heap_write")]
    [InlineData("Newtonsoft.Json.Linq.JObject", "Remove", "heap_write")]
    [InlineData("Newtonsoft.Json.Linq.JArray", "Add", "heap_write")]
    [InlineData("Newtonsoft.Json.JsonConvert", "PopulateObject", "heap_write")]
    public void Ecosystem_NewtonsoftJson_MutationMethods(string type, string method, string expectedValue)
    {
        var resolver = new EffectResolver();
        resolver.Initialize();
        var result = resolver.Resolve(EffectResolverKey.FromStrings(type, method));
        Assert.Equal(EffectResolutionStatus.Resolved, result.Status);
        Assert.Contains(result.Effects.Effects, e => e.Value == expectedValue);
    }

    // ========================================================================
    // Ecosystem manifest tests: Dapper
    // ========================================================================

    [Theory]
    [InlineData("Dapper.SqlMapper", "Query", "database_read")]
    [InlineData("Dapper.SqlMapper", "QueryAsync", "database_read")]
    [InlineData("Dapper.SqlMapper", "QueryFirst", "database_read")]
    [InlineData("Dapper.SqlMapper", "QueryFirstOrDefault", "database_read")]
    [InlineData("Dapper.SqlMapper", "QuerySingle", "database_read")]
    [InlineData("Dapper.SqlMapper", "ExecuteScalar", "database_read")]
    public void Ecosystem_Dapper_ReadMethods(string type, string method, string expectedValue)
    {
        var resolver = new EffectResolver();
        resolver.Initialize();
        var result = resolver.Resolve(EffectResolverKey.FromStrings(type, method));
        Assert.Equal(EffectResolutionStatus.Resolved, result.Status);
        Assert.Contains(result.Effects.Effects, e => e.Value == expectedValue);
    }

    [Theory]
    [InlineData("Dapper.SqlMapper", "Execute", "database_write")]
    [InlineData("Dapper.SqlMapper", "ExecuteAsync", "database_write")]
    public void Ecosystem_Dapper_WriteMethods(string type, string method, string expectedValue)
    {
        var resolver = new EffectResolver();
        resolver.Initialize();
        var result = resolver.Resolve(EffectResolverKey.FromStrings(type, method));
        Assert.Equal(EffectResolutionStatus.Resolved, result.Status);
        Assert.Contains(result.Effects.Effects, e => e.Value == expectedValue);
    }

    // ========================================================================
    // Ecosystem manifest tests: MediatR, AutoMapper, FluentValidation, Polly
    // ========================================================================

    [Theory]
    [InlineData("MediatR.IMediator", "Send")]
    [InlineData("MediatR.IMediator", "Publish")]
    [InlineData("AutoMapper.IMapper", "Map")]
    [InlineData("AutoMapper.IMapper", "ProjectTo")]
    [InlineData("FluentValidation.IValidator", "Validate")]
    [InlineData("FluentValidation.IValidator", "ValidateAsync")]
    [InlineData("Polly.Policy", "Execute")]
    [InlineData("Polly.Policy", "ExecuteAsync")]
    public void Ecosystem_MediatR_AutoMapper_FluentValidation_Polly_ArePure(string type, string method)
    {
        var resolver = new EffectResolver();
        resolver.Initialize();
        var result = resolver.Resolve(EffectResolverKey.FromStrings(type, method));
        Assert.Equal(EffectResolutionStatus.PureExplicit, result.Status);
    }

    [Fact]
    public void Ecosystem_FluentValidation_ValidateAndThrow_HasThrowEffect()
    {
        var resolver = new EffectResolver();
        resolver.Initialize();
        var result = resolver.Resolve(EffectResolverKey.FromStrings("FluentValidation.AbstractValidator`1", "ValidateAndThrow"));
        Assert.Equal(EffectResolutionStatus.Resolved, result.Status);
        Assert.Contains(result.Effects.Effects, e => e.Value == "intentional");
    }

    [Theory]
    [InlineData("Calor.Runtime.Option`1", "Map")]
    [InlineData("Calor.Runtime.Option`1", "AndThen")]
    [InlineData("Calor.Runtime.Option`1", "Match")]
    [InlineData("Calor.Runtime.Option`1", "Filter")]
    [InlineData("Calor.Runtime.Option`1", "Unwrap")]
    [InlineData("Calor.Runtime.Option`1", "UnwrapOrElse")]
    [InlineData("Calor.Runtime.Result`2", "Map")]
    [InlineData("Calor.Runtime.Result`2", "MapErr")]
    [InlineData("Calor.Runtime.Result`2", "AndThen")]
    [InlineData("Calor.Runtime.Result`2", "OrElse")]
    [InlineData("Calor.Runtime.Result`2", "Match")]
    [InlineData("Calor.Runtime.Result", "Try")]
    [InlineData("Calor.Runtime.Option", "Some")]
    [InlineData("Calor.Runtime.Option", "FromNullable")]
    public void RemovedRuntimeCombinators_AreNotTreatedAsKnownPure(string type, string method)
    {
        var resolver = new EffectResolver();
        resolver.Initialize();
        var result = resolver.Resolve(EffectResolverKey.FromStrings(type, method));
        Assert.Equal(EffectResolutionStatus.Unknown, result.Status);
    }

    [Theory]
    [InlineData("?i32", "System.Nullable`1")]
    [InlineData("?str", "System.String")]
    [InlineData("Option<i32>", "Option<i32>")]
    [InlineData("Result<i32,str>", "Result<i32,str>")]
    [InlineData("Box<i32>", "Box<i32>")]
    [InlineData("Option", "Option")]
    [InlineData("Result", "Result")]
    [InlineData("Console", "System.Console")]
    public void MapShortTypeName_LeavesThirdPartyOptionResultTypesNominal(string surface, string expected)
    {
        Assert.Equal(expected, EffectEnforcementPass.MapShortTypeNameToFullName(surface));
    }

    /// <summary>
    /// v0.15 E1 slice 2c, review round 1 (MINOR 7) — a manifest mapping written
    /// with a <c>global::</c> prefix stays reachable.
    ///
    /// <para>Keys normalize their declaring type (trim, strip <c>global::</c>),
    /// so once lookups became key-based the type cache had to normalize on
    /// INSERT too. It did not, briefly: the cache was keyed on the raw manifest
    /// string while every lookup arrived normalized, and a user manifest
    /// declaring <c>"type": "global::Vendor.Widget"</c> — which resolved before
    /// the slice — silently stopped resolving. Both spellings now answer, which
    /// is strictly more than the pre-slice behaviour (where only the exact raw
    /// spelling matched).</para>
    /// </summary>
    [Theory]
    [InlineData("global::Vendor.Widget")]
    [InlineData("Vendor.Widget")]
    public void ManifestTypeWrittenWithGlobalPrefix_IsReachableUnderEitherSpelling(string lookupType)
    {
        var loader = new ManifestLoader();
        loader.LoadFromJson(
            """
            {
              "version": "1.0",
              "mappings": [{
                "type": "global::Vendor.Widget",
                "methods": { "Save": ["db:w"] }
              }]
            }
            """,
            "global-prefixed");
        var resolver = new EffectResolver(loader);

        var resolution = resolver.Resolve(EffectResolverKey.FromStrings(lookupType, "Save"));

        Assert.Equal(EffectResolutionStatus.Resolved, resolution.Status);
        Assert.True(resolution.Effects.Contains(EffectKind.IO, "database_write"));
    }

    /// <summary>
    /// The extension-provider manifest both tests below resolve against: one
    /// name-only <c>Select</c> entry, reachable ONLY through
    /// <c>IsCompatibleExtensionReceiver</c>. There is no signature entry, so the
    /// signature probe cannot answer and the receiver-compatibility decision is
    /// the whole result.
    /// </summary>
    private const string LinqExtensionProviderManifest =
        """
        {
          "version": "1.0",
          "mappings": [{
            "type": "System.Linq.Enumerable",
            "extensionProvider": true,
            "methods": { "Select": ["cw"] }
          }]
        }
        """;

    private static EffectResolver LinqExtensionResolver()
    {
        var loader = new ManifestLoader();
        loader.LoadFromJson(LinqExtensionProviderManifest, "linq-extension");
        return new EffectResolver(loader);
    }

    /// <summary>
    /// v0.15 E1 slice 2c, review round 1 (CRITICAL 1) — the DISCRIMINATING pin
    /// for <c>EffectResolverKey.ReceiverInterfaces</c>.
    ///
    /// <para>The slice claims extension-receiver compatibility is asked of the
    /// BINDER first ("does the receiver implement <c>IEnumerable</c>?") and only
    /// then of <c>IsCompatibleExtensionReceiver</c>'s name-shape list. Round 1
    /// found that claim unobservable: replacing <c>InterfacesOf</c> with
    /// <c>Array.Empty</c> left every suite green, because every shape that
    /// answered through the interface branch ALSO satisfied the name-shape list,
    /// so the two routes never disagreed on anything a test looked at.</para>
    ///
    /// <para><c>IReadOnlyList&lt;T&gt;</c> is the shape where they disagree, and
    /// that is the point of choosing it. Its key's declaring type is
    /// <c>IReadOnlyList`1</c> — the generic definition plus arity, which is how
    /// manifests spell generics. The name-shape list tests for <c>[]</c>,
    /// <c>System.Collections.</c>, and the ANGLE-BRACKET spellings
    /// (<c>IEnumerable&lt;</c>, <c>IList&lt;</c>, <c>List&lt;</c>, …); the
    /// backtick form matches none of them, and <c>IReadOnlyList</c> is not on
    /// the list under any spelling. So the name-shape route says NO and the
    /// interface route says YES, and only one of them can be producing the
    /// result below.</para>
    ///
    /// <para>Neutralise <c>InterfacesOf</c> and this test fails while its
    /// control keeps passing. That is the experiment round 1 asked for.</para>
    /// </summary>
    [Fact]
    public void BoundReceiverInterfaces_DecideExtensionCompatibility_WhereTheNameShapeListSaysNo()
    {
        var resolver = LinqExtensionResolver();

        var boundReceiver = new Calor.Compiler.Binding.BoundTypes.GenericInstantiationBoundType(
            new Calor.Compiler.Binding.BoundTypes.NominalBoundType("IReadOnlyList"),
            [new Calor.Compiler.Binding.BoundTypes.NominalBoundType("STRING")]);

        var boundKey = EffectResolverKey.FromBoundReceiver(
            boundReceiver, "Select", kind: EffectMemberKind.Extension);

        // The premise: the declaring type is the backtick form, which is exactly
        // what the name-shape list cannot match. If this ever becomes
        // "IReadOnlyList<STRING>" the test stops discriminating and must be
        // rewritten rather than deleted.
        Assert.Equal("IReadOnlyList`1", boundKey.DeclaringType);
        Assert.Contains("System.Collections.Generic.IEnumerable`1", boundKey.ReceiverInterfaces);

        var throughInterfaces = resolver.Resolve(boundKey);
        Assert.Equal(EffectResolutionStatus.Resolved, throughInterfaces.Status);
        Assert.True(throughInterfaces.Effects.Contains(EffectKind.IO, "console_write"));
    }

    /// <summary>
    /// The control for
    /// <see cref="BoundReceiverInterfaces_DecideExtensionCompatibility_WhereTheNameShapeListSaysNo"/>:
    /// the IDENTICAL declaring type and member, keyed from TEXT instead of from
    /// a bound receiver. No interfaces, so the name-shape list decides — and it
    /// says no. One key carries the binder's answer and the other does not, and
    /// that is the only difference between resolving and not.
    /// </summary>
    [Fact]
    public void SameTypeKeyedFromText_HasNoInterfaces_AndTheNameShapeListRefusesIt()
    {
        var resolver = LinqExtensionResolver();

        var stringKey = EffectResolverKey.FromStrings(
            "IReadOnlyList`1", "Select", kind: EffectMemberKind.Extension);

        Assert.Empty(stringKey.ReceiverInterfaces);
        Assert.Equal(EffectResolutionStatus.Unknown, resolver.Resolve(stringKey).Status);
    }

    /// <summary>
    /// The language-guaranteed half: an array implements <c>IEnumerable&lt;T&gt;</c>,
    /// and a bound array receiver says so structurally.
    ///
    /// <para>Stated plainly because it matters for reading the pin above: for
    /// arrays the two routes AGREE — <c>STRING[]</c> also satisfies the
    /// name-shape list's <c>[]</c> test — so this case is coverage, not a
    /// discriminator. <c>IReadOnlyList`1</c> is the discriminator.</para>
    /// </summary>
    [Fact]
    public void BoundArrayReceiver_CarriesTheEnumerableInterface()
    {
        var resolver = LinqExtensionResolver();

        var arrayKey = EffectResolverKey.FromBoundReceiver(
            new Calor.Compiler.Binding.BoundTypes.ArrayBoundType(
                new Calor.Compiler.Binding.BoundTypes.NominalBoundType("STRING")),
            "Select",
            kind: EffectMemberKind.Extension);

        Assert.Contains("System.Collections.Generic.IEnumerable`1", arrayKey.ReceiverInterfaces);
        Assert.Equal(EffectResolutionStatus.Resolved, resolver.Resolve(arrayKey).Status);
    }

    // ================================================================ P21 ====
    // v0.15 E3 slice a — design-doc §8.4's mapping, at the level the RESOLVER
    // owns it. Slice b pinned the EffectSet → EffectRow bridge in
    // EffectRowLatticeTests; this pins the three EffectResolutionStatus values
    // that feed it, so the two halves cannot drift apart.

    [Fact]
    public void ManifestResolutionMapsToRow()
    {
        // §8.4: Resolved → Concrete(S), PureExplicit → Concrete(∅), Unknown →
        // EffectRow.Unknown. Discriminating revert: map Unknown to Concrete(∅)
        // and P17's fixture goes silent, because a provably-pure row fits every
        // destination.
        var resolved = new EffectResolution(
            EffectResolutionStatus.Resolved, EffectSet.From("cw"), "manifest");
        var pure = new EffectResolution(
            EffectResolutionStatus.PureExplicit, EffectSet.Empty, "manifest");
        var unknown = new EffectResolution(
            EffectResolutionStatus.Unknown, EffectSet.Unknown, "unknown");

        Assert.Equal(EffectRow.Concrete(new[] { "io:console_write" }), resolved.Effects.ToRow());

        Assert.Equal(EffectRow.Pure, pure.Effects.ToRow());
        Assert.True(pure.Effects.ToRow().IsConcrete);

        Assert.True(unknown.Effects.ToRow().IsUnknown);
        Assert.NotEqual(EffectRow.Pure, unknown.Effects.ToRow());

        // …and an Unknown row fits NOTHING and is fitted BY nothing, which is
        // what makes the mapping load-bearing rather than cosmetic.
        Assert.Equal(EffectFit.CannotTell,
            EffectRow.Fits(unknown.Effects.ToRow(), EffectRow.Pure));
        Assert.Equal(EffectFit.CannotTell,
            EffectRow.Fits(EffectRow.Pure, unknown.Effects.ToRow()));
    }

    [Fact]
    public void EffectRowDiagnosticCodesAreTheOnesSection61Froze()
    {
        // §6.1's allocation, asserted as literals so a renumbering has to be
        // deliberate. Both were verified free at the design doc's merge and at
        // PP-E1's registration; they are now taken.
        Assert.Equal("Calor0424", DiagnosticCode.EffectRowMismatch);
        Assert.Equal("Calor0425", DiagnosticCode.EffectRowUnknown);

        // The neighbours they must not collide with.
        Assert.Equal("Calor0404", DiagnosticCode.EffectVariableScope);
        Assert.Equal("Calor0405", DiagnosticCode.EffectRowMisplaced);
        Assert.Equal("Calor0420", DiagnosticCode.OverrideEffectVariance);
        Assert.Equal("Calor0421", DiagnosticCode.InterfaceEffectVariance);
        Assert.Equal("Calor0423", DiagnosticCode.AccessorEffectContractUnavailable);
    }

    [Fact]
    public void EnumerableContracts_RuntimeOverloadsAndIncompleteKeysCannotBorrowPurity()
    {
        var compilation = StringProbeCompilation("", []);
        var type = compilation.GetTypeByMetadataName("System.Linq.Enumerable")!;
        Assert.Equal(typeof(Enumerable).Assembly.FullName, type.ContainingAssembly.Identity.ToString());
        var methods = type.GetMembers().OfType<IMethodSymbol>()
            .Where(method => method.DeclaredAccessibility == Accessibility.Public && method.IsStatic)
            .ToArray();
        Assert.NotEmpty(methods);
        foreach (var warm in new[] { false, true })
        {
            var loader = new ManifestLoader(loadUserLevelManifests: false);
            var resolver = new EffectResolver(loader);
            resolver.Initialize();
            Assert.True(Assert.Single(loader.LoadedManifests.SelectMany(entry => entry.Manifest.Mappings)
                .Where(mapping => mapping.Type == "System.Linq.Enumerable")).ExtensionProvider);
            if (warm)
                Assert.Equal(EffectResolutionStatus.PureExplicit, resolver.Resolve(
                    EffectResolverKey.FromStrings("System.String", "Contains", ["System.Char"])).Status);
            foreach (var definition in methods.Concat(methods.Reverse()))
            {
                Assert.DoesNotContain(definition.Locations, location => location.IsInSource);
                var method = definition.IsGenericMethod
                    ? definition.Construct(Enumerable.Repeat<ITypeSymbol>(
                        compilation.GetSpecialType(SpecialType.System_Int32), definition.Arity).ToArray())
                    : definition;
                var key = LinqKey(method);
                foreach (var probe in new[]
                {
                    key, key.WithoutParameterList(),
                    EffectResolverKey.FromStrings(key.DeclaringType, key.MemberName, ["?"]),
                    EffectResolverKey.FromStrings("Vendor.Enumerable", key.MemberName, key.ParameterTypes?.ToArray()),
                    EffectResolverKey.FromStrings("int[]", key.MemberName, ["?"],
                        kind: EffectMemberKind.Extension),
                    EffectResolverKey.FromBoundReceiver(new ArrayBoundType(new NominalBoundType("INT")),
                        key.MemberName, kind: EffectMemberKind.Extension)
                })
                {
                    var resolution = resolver.Resolve(probe);
                    Assert.Equal(EffectResolutionStatus.Unknown, resolution.Status);
                    Assert.True(resolution.Effects.IsUnknown);
                    Assert.True(resolution.Effects.ToRow().IsUnknown);
                }
                Assert.Same(resolver.Resolve(key), resolver.Resolve(key));
            }
            foreach (var kind in new[] { EffectMemberKind.Method, EffectMemberKind.Getter,
                EffectMemberKind.Setter, EffectMemberKind.Constructor })
                Assert.Equal(EffectResolutionStatus.Unknown, resolver.Resolve(
                    EffectResolverKey.FromStrings("System.Linq.Enumerable", "Uncovered", [], kind)).Status);
        }
    }

    [Fact]
    public void EnumerableContracts_NullSelectAndWhereDispatchAmbientCultureInOriginalAndAreRejected()
    {
        const string source = """
            using System.Linq;
            public static class Target
            {
                public static System.Collections.Generic.IEnumerable<int> Apply(System.Collections.Generic.IEnumerable<int> source)
                {
            #if LINQ_ALT
                    return source.Where(value => true);
            #else
                    return source.Select(value => value);
            #endif
                }
            }
            """;
        foreach (var symbols in new string[][] { [], ["LINQ_ALT"] })
        foreach (var throws in new[] { false, true })
        {
            var witness = LinqNullWitness.Replace("$THROW$", throws ? "true" : "false", StringComparison.Ordinal)
                .Replace("$METHOD$", symbols.Length == 0 ? "Select" : "Where", StringComparison.Ordinal);
            var observed = Assert.IsType<int[]>(RunStringProbe(source + witness, symbols));
            Assert.All(observed.Take(2), hits => Assert.True(hits > 0));
            if (throws)
                Assert.Equal(new[] { 1, 1 }, observed.Take(2));
            Assert.Equal(new[] { 1, 1 }, observed.Skip(2));
        }
        AssertLinqRejected(source, """
            module LinqContracts overflow unchecked nullable oblivious
              using System.Linq
              module Target
                func Apply public (System.Collections.Generic.IEnumerable<int>:source) → System.Collections.Generic.IEnumerable<int>
                  !
                  pp LINQ_ALT
                    return source.Where((int:value) => true)
                  pp else
                    return source.Select((int:value) => value)
            """);
    }

    [Theory]
    [InlineData("GetEnumerator")]
    [InlineData("MoveNext")]
    [InlineData("Current")]
    [InlineData("Dispose")]
    [InlineData("ToList")]
    [InlineData("Selector")]
    [InlineData("Comparer")]
    public void EnumerableContracts_UserEnumerationCallbacksAndComparersExecuteOnlyInOriginal(string stage)
    {
        var expression = stage switch
        {
            "Current" => "source.ToArray().Length",
            "ToList" => "source.ToList().Count",
            "Selector" => "source.Select(selector).ToArray().Length",
            "Comparer" => "source.OrderBy(value => value, comparer).ToArray().Length",
            _ => "source.Count()"
        };
        var source = $$"""
            using System.Linq;
            public static class Target
            {
                public static int Apply(System.Collections.Generic.IEnumerable<int> source,
                    System.Func<int, int> selector, System.Collections.Generic.IComparer<int> comparer)
                    => {{expression}};
            }
            """;
        var witness = LinqUserCodeWitness.Replace("$STAGE$", stage == "ToList" ? "GetEnumerator" : stage,
            StringComparison.Ordinal);
        foreach (var symbols in new string[][] { [], ["LINQ_ALT"] })
            Assert.Equal(1, RunStringProbe(source + witness, symbols));
        AssertLinqRejected(source, $$"""
            module LinqContracts overflow unchecked nullable oblivious
              using System.Linq
              module Target
                func Apply public (System.Collections.Generic.IEnumerable<int>:source, System.Func<int,int>:selector, System.Collections.Generic.IComparer<int>:comparer) → int
                  !
                  return {{expression.Replace("value => value", "(int:value) => value", StringComparison.Ordinal)}}
            """);
    }

    [Fact]
    public void EnumerableContracts_SourceAliasRemainsSourceCodeRatherThanBuiltinAuthority()
    {
        const string source = """
            using Enumerable = VendorEnumerable;
            public static class VendorEnumerable
            {
                public static int Calls;
                public static int Select(int value) { Calls++; return value; }
            }
            public static class Probe
            {
                public static int Run() => Enumerable.Select(7);
            }
            """;
        var compilation = StringProbeCompilation(source, []);
        var tree = compilation.SyntaxTrees.Single();
        var selected = Assert.IsAssignableFrom<IMethodSymbol>(compilation.GetSemanticModel(tree).GetSymbolInfo(
            Assert.Single(tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())).Symbol);
        Assert.Contains(selected.Locations, location => location.IsInSource);
        Assert.Equal("VendorEnumerable", selected.ContainingType.Name);
        Assert.Equal(EffectResolutionStatus.Unknown,
            new EffectResolver(new ManifestLoader(loadUserLevelManifests: false)).Resolve(LinqKey(selected)).Status);
        Assert.Equal(7, RunStringProbe(source, []));
        var conversion = new CSharpToCalorConverter(new ConversionOptions { ModuleName = "LinqContracts" }).Convert(source);
        Assert.True(conversion.Success);
        var run = conversion.Ast!.Classes.Single(type => type.Name == "Probe").Methods.Single();
        Assert.Contains(EffectEnforcementPass.GetDeclaredEffects(run.Effects).Effects,
            effect => effect.Kind == EffectKind.Mutation);
    }

    private static void AssertLinqRejected(string source, string native)
    {
        foreach (var symbols in new string[][] { [], ["LINQ_ALT"] })
        {
            var compilation = StringProbeCompilation(source, symbols);
            Assert.DoesNotContain(compilation.GetDiagnostics(),
                diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
            var tree = compilation.SyntaxTrees.Single();
            var model = compilation.GetSemanticModel(tree);
            var selected = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Select(node => model.GetSymbolInfo(node).Symbol).OfType<IMethodSymbol>().ToArray();
            Assert.NotEmpty(selected);
            foreach (var method in selected)
            {
                Assert.Equal(typeof(Enumerable).Assembly.FullName, method.ContainingAssembly.Identity.ToString());
                Assert.Equal("System.Linq.Enumerable", method.ContainingType.ToDisplayString());
                Assert.DoesNotContain(method.Locations, location => location.IsInSource);
                var provider = method.ReducedFrom?.Construct(method.TypeArguments.ToArray()) ?? method;
                Assert.Equal(EffectResolutionStatus.Unknown,
                    new EffectResolver(new ManifestLoader(loadUserLevelManifests: false)).Resolve(LinqKey(provider)).Status);
            }
        }
        var conversion = new CSharpToCalorConverter(new ConversionOptions { ModuleName = "LinqContracts" }).Convert(source);
        Assert.True(conversion.Success);
        Assert.True(conversion.InteropPreservationCount > 0);
        foreach (var emission in StringEmissions(native))
        foreach (var typing in new[] { false, true })
        {
            var result = Calor.Compiler.Program.Compile(emission, "unknown-linq.calr",
                new Calor.Compiler.CompilationOptions
                {
                    EnableTypeChecking = typing, VerifyContracts = false, StatusWriter = TextWriter.Null
                });
            Assert.True(result.HasErrors);
            Assert.Empty(result.GeneratedCode);
            Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("'unknown'", StringComparison.Ordinal));
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall
                && (diagnostic.Message.Contains(".Count", StringComparison.Ordinal)
                    || diagnostic.Message.Contains(".Select", StringComparison.Ordinal)
                    || diagnostic.Message.Contains(".Where", StringComparison.Ordinal)
                    || diagnostic.Message.Contains(".ToList", StringComparison.Ordinal)
                    || diagnostic.Message.Contains(".ToArray", StringComparison.Ordinal)
                    || diagnostic.Message.Contains(".OrderBy", StringComparison.Ordinal)));
        }
    }

    private static EffectResolverKey LinqKey(IMethodSymbol method)
    {
        var format = new SymbolDisplayFormat(
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);
        return EffectResolverKey.FromStrings(method.ContainingType.ToDisplayString(format), method.Name,
            method.Parameters.Select(parameter => parameter.Type.ToDisplayString(format)).ToArray());
    }

    private const string LinqNullWitness = """

        public sealed class LinqBoundaryException : System.Exception { }
        public sealed class LinqBoundaryCulture : System.Globalization.CultureInfo
        {
            public static int Hits;
            public static bool Active;
            public LinqBoundaryCulture() : base("") { }
            public override string Name
            {
                get
                {
                    if (Active)
                    {
                        Hits++;
                        if ($THROW$) throw new LinqBoundaryException();
                    }
                    return base.Name;
                }
            }
        }
        public static class Probe
        {
            public static int[] Run()
            {
                var saved = System.Globalization.CultureInfo.CurrentUICulture;
                var culture = new LinqBoundaryCulture();
                var hits = new int[2];
                var errors = new System.Exception[2];
                System.Globalization.CultureInfo.CurrentUICulture = culture;
                LinqBoundaryCulture.Active = true;
                try
                {
                    for (int pass = 0; pass < 2; pass++)
                    {
                        LinqBoundaryCulture.Hits = 0;
                        try { Target.Apply(null); }
                        catch (LinqBoundaryException error) { errors[pass] = error; }
                        catch (System.ArgumentNullException error) { errors[pass] = error; }
                        hits[pass] = LinqBoundaryCulture.Hits;
                    }
                }
                finally
                {
                    LinqBoundaryCulture.Active = false;
                    System.Globalization.CultureInfo.CurrentUICulture = saved;
                }
                return new[] { hits[0], hits[1],
                    errors[0].StackTrace.Contains("Enumerable.$METHOD$") ? 1 : 0,
                    errors[1].StackTrace.Contains("Enumerable.$METHOD$") ? 1 : 0 };
            }
        }
        """;

    private const string LinqUserCodeWitness = """

        public sealed class LinqDispatchException : System.Exception { }
        public sealed class LinqSource : System.Collections.Generic.IEnumerable<int>, System.Collections.Generic.IEnumerator<int>
        {
            public int Current { get { Probe.Touch("Current"); return 1; } }
            object System.Collections.IEnumerator.Current => Current;
            public System.Collections.Generic.IEnumerator<int> GetEnumerator() { Probe.Touch("GetEnumerator"); return this; }
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
            private int index;
            public bool MoveNext() { Probe.Touch("MoveNext"); return index++ == 0; }
            public void Reset() { index = 0; }
            public void Dispose() { Probe.Touch("Dispose"); }
        }
        public sealed class LinqComparer : System.Collections.Generic.IComparer<int>
        {
            public int Compare(int left, int right) { Probe.Touch("Comparer"); return left.CompareTo(right); }
        }
        public static class Probe
        {
            private static int hits;
            public static void Touch(string stage)
            {
                if (stage == "$STAGE$") { hits++; throw new LinqDispatchException(); }
            }
            public static int Run()
            {
                System.Collections.Generic.IEnumerable<int> source =
                    "$STAGE$" == "Selector" || "$STAGE$" == "Comparer" ? new[] { 2, 1 } : new LinqSource();
                try { Target.Apply(source, value => { Touch("Selector"); return value; }, new LinqComparer()); }
                catch (LinqDispatchException) { return hits; }
                catch (System.InvalidOperationException error) when (error.InnerException is LinqDispatchException) { return hits; }
                return -1;
            }
        }
        """;

    [Fact]
    public void ConvertContracts_OnlyExactProvedOverloadsSurviveAllManifestAndCacheLayers()
    {
        var type = StringProbeCompilation("", []).GetTypeByMetadataName("System.Convert")!;
        var methods = type.GetMembers().OfType<IMethodSymbol>()
            .Where(method => method.DeclaredAccessibility == Accessibility.Public).ToArray();
        foreach (var safeFirst in new[] { false, true })
        {
            var loader = new ManifestLoader(loadUserLevelManifests: false);
            var resolver = new EffectResolver(loader);
            resolver.Initialize();
            var mapping = Assert.Single(loader.LoadedManifests.SelectMany(entry => entry.Manifest.Mappings)
                .Where(mapping => mapping.Type == "System.Convert"));
            Assert.Null(mapping.DefaultEffects);
            Assert.Null(mapping.Constructors);
            Assert.Null(mapping.Getters);
            Assert.Null(mapping.Setters);
            Assert.Equal(6, mapping.Methods!.Count);
            Assert.All(mapping.Methods.Keys, signature => Assert.StartsWith("ToString(", signature));
            var safeKeys = methods.Where(IsProvedConvertOverload).Select(NumericKey).ToArray();
            Assert.Equal(6, safeKeys.Length);
            if (safeFirst)
                foreach (var key in safeKeys)
                    Assert.Equal(EffectResolutionStatus.PureExplicit, resolver.Resolve(key).Status);
            foreach (var method in methods.Concat(methods.Reverse()))
            {
                Assert.Equal(typeof(Convert).Assembly.FullName, method.ContainingAssembly.Identity.ToString());
                Assert.DoesNotContain(method.Locations, location => location.IsInSource);
                var key = NumericKey(method);
                var safe = IsProvedConvertOverload(method);
                var result = resolver.Resolve(key);
                Assert.Equal(safe ? EffectResolutionStatus.PureExplicit : EffectResolutionStatus.Unknown, result.Status);
                Assert.Equal(!safe, result.Effects.IsUnknown);
                Assert.Same(result, resolver.Resolve(key));
                Assert.Equal(EffectResolutionStatus.Unknown, resolver.Resolve(key.WithoutParameterList()).Status);
            }
            foreach (var key in safeKeys)
            {
                Assert.Equal(EffectResolutionStatus.PureExplicit, resolver.Resolve(key).Status);
                foreach (var kind in new[] { EffectMemberKind.Getter, EffectMemberKind.Setter, EffectMemberKind.Constructor })
                    Assert.Equal(EffectResolutionStatus.Unknown, resolver.Resolve(
                        EffectResolverKey.FromStrings("System.Convert", key.MemberName, key.ParameterTypes, kind)).Status);
                Assert.Equal(EffectResolutionStatus.Unknown, resolver.Resolve(
                    EffectResolverKey.FromStrings("System.Convert", key.MemberName, ["?"])).Status);
                Assert.Equal(EffectResolutionStatus.Unknown, resolver.Resolve(
                    EffectResolverKey.FromStrings("Vendor.Convert", key.MemberName, key.ParameterTypes)).Status);
            }
            foreach (var key in new[]
            {
                EffectResolverKey.FromStrings("System.Convert", "Uncovered", []),
                EffectResolverKey.FromStrings("System.Convert", ".ctor", [], EffectMemberKind.Constructor),
                EffectResolverKey.FromStrings("System.Convert", "ToString", ["System.String", "?"]),
                EffectResolverKey.FromStrings("System.Convert", "ToString", ["?", "System.IFormatProvider"])
            })
                Assert.Equal(EffectResolutionStatus.Unknown, resolver.Resolve(key).Status);
        }
    }

    [Theory]
    [InlineData("string?", "null", null)]
    [InlineData("string?", "\"value\"", "value")]
    [InlineData("bool", "true", "True")]
    [InlineData("bool", "false", "False")]
    [InlineData("char", "'\\0'", "True")]
    [InlineData("char", "'\\ud800'", "True")]
    [InlineData("char", "'\\uffff'", "True")]
    public void ConvertContracts_ProvedControlsAreStrictlyAdmittedBeforeExecution(
        string type, string argument, string? expected)
    {
        foreach (var provider in new[] { false, true })
        {
            var expression = $"System.Convert.ToString(value{(provider ? ", provider" : "")})";
            var source = $$"""
                #nullable enable
                public static class Target
                {
                    public static string? Apply({{type}} value, System.IFormatProvider provider) => {{expression}};
                }
                """;
            var selected = SelectedConvertMethod(source);
            Assert.True(IsProvedConvertOverload(selected));
            var resolution = new EffectResolver(new ManifestLoader(loadUserLevelManifests: false))
                .Resolve(NumericKey(selected));
            Assert.Equal(EffectResolutionStatus.PureExplicit, resolution.Status);
            Assert.Contains("embedded:Calor.Compiler.Manifests.bcl-system", resolution.Source);
            var conversion = ConvertStringSource(source);
            Assert.True(conversion.Success);
            Assert.Equal(0, conversion.InteropPreservationCount);
            var direct = $$"""
                module ConvertContracts
                  module Target
                    func Apply ({{type}}:value, System.IFormatProvider:provider) → string?
                      !
                      return {{expression}}
                """;
            foreach (var native in StringEmissions(direct).Concat(StringEmissions(conversion.CalorSource!)))
            foreach (var typing in new[] { false, true })
            {
                var result = Calor.Compiler.Program.Compile(native, "safe-convert.calr",
                    new Calor.Compiler.CompilationOptions
                    {
                        EnableTypeChecking = typing, VerifyContracts = false, StatusWriter = TextWriter.Null
                    });
                Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
                Assert.Equal(selected.ToDisplayString(), SelectedConvertMethod(result.GeneratedCode).ToDisplayString());
                foreach (var providerIsCulture in new[] { false, true })
                {
                    var call = type switch
                    {
                        "string?" => $"object.ReferenceEquals(Target.Apply({argument}, provider), {argument})",
                        "char" => $"Target.Apply({argument}, provider) == new string({argument}, 1)",
                        _ => $"Target.Apply({argument}, provider)"
                    };
                    var witness = NumericWitness(call, throws: true, providerIsCulture);
                    var output = (type == "string?" ? "True" : expected) + ":0";
                    Assert.Equal(output, RunStringProbe(source + witness, []));
                    Assert.Equal(output, RunStringProbe(result.GeneratedCode + witness, []));
                }
            }
        }
    }

    [Theory]
    [InlineData("object?", "new Loud()", "value")]
    [InlineData("int", "-1234", "-1234")]
    [InlineData("long", "-1234L", "-1234")]
    [InlineData("double", "-1234.5", "-1234.5")]
    public void ConvertContracts_CallbackOverloadsAreUnknownAndNeverExecuteAfterRefusal(
        string type, string argument, string expected)
    {
        foreach (var provider in new[] { false, true })
        {
            var expression = $"System.Convert.ToString(value{(provider ? ", provider" : "")})";
            var source = $$"""
                #nullable enable
                public sealed class Loud
                {
                    public override string ToString()
                    {
                        System.Console.WriteLine("convert-callback");
                        return "value";
                    }
                }
                public static class Target
                {
                    public static string? Apply({{type}} value, System.IFormatProvider provider) => {{expression}};
                }
                """;
            var selected = SelectedConvertMethod(source);
            Assert.False(IsProvedConvertOverload(selected));
            Assert.Equal(EffectResolutionStatus.Unknown,
                new EffectResolver(new ManifestLoader(loadUserLevelManifests: false)).Resolve(NumericKey(selected)).Status);
            if (type == "object?")
            {
                var witness = """

                    public static class Probe
                    {
                        public static string Run()
                        {
                            var previous = System.Console.Out;
                            using var output = new System.IO.StringWriter();
                            try
                            {
                                System.Console.SetOut(output);
                                var result = Target.Apply(new Loud(), null);
                                return result + ":" + output.ToString();
                            }
                            finally { System.Console.SetOut(previous); }
                        }
                    }
                    """;
                Assert.Equal("value:convert-callback" + Environment.NewLine, RunStringProbe(source + witness, []));
            }
            else
                foreach (var throws in new[] { false, true })
                foreach (var providerIsCulture in new[] { false, true })
                    Assert.Equal(throws ? "throw:1" : expected + ":1", RunStringProbe(source
                        + NumericWitness($"Target.Apply({argument}, provider)", throws, providerIsCulture), []));
            var conversion = ConvertStringSource(source);
            Assert.True(conversion.Success);
            Assert.Equal(0, conversion.InteropPreservationCount);
            var direct = $$"""
                module ConvertContracts
                  module Target
                    func Apply ({{type}}:value, System.IFormatProvider:provider) → string?
                      !
                      return {{expression}}
                """;
            foreach (var native in StringEmissions(direct).Concat(StringEmissions(conversion.CalorSource!)))
            foreach (var typing in new[] { false, true })
            {
                var result = Calor.Compiler.Program.Compile(native, "unknown-convert.calr",
                    new Calor.Compiler.CompilationOptions
                    {
                        EnableTypeChecking = typing, VerifyContracts = false, StatusWriter = TextWriter.Null
                    });
                Assert.True(result.HasErrors);
                Assert.Empty(result.GeneratedCode);
                Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                    && diagnostic.Message.Contains("'unknown'", StringComparison.Ordinal));
                Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall
                    && diagnostic.Message.Contains("System.Convert.ToString", StringComparison.Ordinal));
            }
        }
    }

    [Theory]
    [InlineData("\"${System.Convert.ToString(value)}\"")]
    [InlineData("System.Convert.ToString(value) + \":suffix\"")]
    public void ConvertContracts_StringResultsCannotLaunderNumericFormatting(string expression)
    {
        var source = $$"""
            module ConvertFormatting
              func Render (int:value) → string
                !
                return {{expression}}
            """;
        foreach (var native in StringEmissions(source))
        foreach (var typing in new[] { false, true })
        {
            var result = Calor.Compiler.Program.Compile(native, "convert-formatting.calr",
                new Calor.Compiler.CompilationOptions
                {
                    EnableTypeChecking = typing, VerifyContracts = false, StrictApi = true,
                    StrictEffects = true, StatusWriter = TextWriter.Null
                });
            Assert.True(result.HasErrors);
            Assert.Empty(result.GeneratedCode);
            Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("'unknown'", StringComparison.Ordinal));
            Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall
                && diagnostic.Message.Contains("System.Convert.ToString", StringComparison.Ordinal));
        }
    }

    private static bool IsProvedConvertOverload(IMethodSymbol method) =>
        method.Name == "ToString" && method.Parameters.Length is 1 or 2
        && method.Parameters[0].Type.SpecialType is SpecialType.System_String or SpecialType.System_Boolean or SpecialType.System_Char
        && (method.Parameters.Length == 1 || method.Parameters[1].Type.ToDisplayString(
            SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.IFormatProvider");

    private static IMethodSymbol SelectedConvertMethod(string source)
    {
        var compilation = StringProbeCompilation(source, []);
        Assert.DoesNotContain(compilation.GetDiagnostics(),
            diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var method = Assert.Single(tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Select(node => model.GetSymbolInfo(node).Symbol).OfType<IMethodSymbol>()
            .Where(symbol => symbol.ContainingType.ToDisplayString() == "System.Convert"));
        Assert.True(method.IsStatic);
        Assert.Equal(typeof(Convert).Assembly.FullName, method.ContainingAssembly.Identity.ToString());
        Assert.DoesNotContain(method.Locations, location => location.IsInSource);
        return method;
    }
}
