using System.Reflection;
using Calor.Compiler.Analysis;
using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class NativePostfixTests
{
    [Theory]
    [InlineData("\"hello\".Contains(\"HELLO\", System.StringComparison.OrdinalIgnoreCase)", "True", "Contains")]
    [InlineData("\"hello\".StartsWith(\"he\", System.StringComparison.Ordinal)", "True", "StartsWith")]
    [InlineData("\"hello\".IndexOf(\"l\", System.StringComparison.Ordinal)", "2", "IndexOf")]
    [InlineData("System.String.Equals(\"I\", \"i\", System.StringComparison.OrdinalIgnoreCase)", "True", "Equals")]
    public void MetadataEnumConstant_RetainsTheSelectedMethodAndArgumentIdentity(
        string expression, string expected, string methodName)
    {
        var source = Source(expression, type: methodName == "IndexOf" ? "int" : "bool");
        var (_, bound, diagnostics) = Bind(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        var method = Assert.IsAssignableFrom<IMethodSymbol>(call.ResolvedMetadataMethod);
        Assert.Equal(methodName, method.Name);
        var argument = Assert.IsType<BoundFieldAccessExpression>(call.Arguments.Last());
        var field = Assert.IsAssignableFrom<IFieldSymbol>(argument.ResolvedMetadataMember);
        Assert.True(field.IsConst);
        var type = Assert.IsType<Calor.Compiler.Binding.BoundTypes.NominalBoundType>(argument.Type);
        Assert.Equal(TypeKind.Enum, type.RoslynSymbol!.TypeKind);
        Assert.True(SymbolEqualityComparer.Default.Equals(field.Type, type.RoslynSymbol));
        Assert.Equal("System.StringComparison", type.RoslynSymbol.ToDisplayString());
        Assert.Equal(expected, Invoke(Compile(source), "Probe")!.ToString());
    }

    [Theory]
    [InlineData("System.StringComparison", "Ordinal")]
    [InlineData("System.DayOfWeek", "Monday")]
    public void MetadataEnumConstant_AliasesRetainTheActualEnumIdentity(string enumType, string member)
    {
        var source = $$"""
            module Native Native
              using Mode = {{enumType}}
              func Probe () → object
                !
                return Mode.{{member}}
            """;
        var (_, bound, diagnostics) = Bind(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var read = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundFieldAccessExpression>());
        var field = Assert.IsAssignableFrom<IFieldSymbol>(read.ResolvedMetadataMember);
        Assert.Equal(enumType, field.ContainingType.ToDisplayString());
        Assert.Equal(member, field.Name);
        Assert.False(read.HasUnknownInferredType);
        Assert.Equal(member, Invoke(Compile(source), "Probe")!.ToString());
    }

    [Theory]
    [InlineData("System.DayOfWeek", "Monday")]
    [InlineData("System.StringComparison", "Missing")]
    [InlineData("Missing.Enum", "Ordinal")]
    public void MetadataEnumConstant_InvalidArgumentsDoNotAcquireAComparisonContract(string enumType, string member)
    {
        var source = $$"""
            module Native Native
              using Mode = {{enumType}}
              func Probe () → bool
                !
                return "hello".Contains("he", Mode.{{member}})
            """;
        var (_, bound, diagnostics) = Bind(source);
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.True(call.ResolvedMetadataMethod is null,
            $"{call.ResolvedMetadataMethod}; argument {call.Arguments.Last().Type}: "
            + string.Join("; ", diagnostics));
        var compiled = Program.Compile(source);
        Assert.True(compiled.HasErrors);
    }

    [Theory]
    [InlineData("", "", false)]
    [InlineData("cw", "", true)]
    [InlineData("cw", "cw", false)]
    public void InterfaceReceiver_ChargesTheSelectedDeclarationContract(
        string declared, string caller, bool forbidden)
    {
        var source = $$"""
            module Native Native
              interface IReader
                func Read () → int
                  ! {{declared}}
              class Reader
                implements IReader
                func Read () → int
                  !
                  return 9
              func Probe (IReader:value) → int
                ! {{caller}}
                return value.Read()
            """;
        var (ast, bound, diagnostics) = Bind(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        var symbol = Assert.IsType<FunctionSymbol>(call.ResolvedSymbol);
        Assert.Equal("Native.IReader.Read", symbol.Name);
        Assert.Equal(Assert.Single(ast.Interfaces[0].Methods).Span, symbol.DefinitionSpan);
        Assert.Equal("Read", source.Substring(symbol.DeclarationSpan.Start, symbol.DeclarationSpan.Length));
        Assert.Equal("Read", source.Substring(call.CalleeSpan.Start, call.CalleeSpan.Length));
        Assert.Null(call.ResolvedMetadataMethod);
        var result = Program.Compile(source);
        Assert.Equal(forbidden, result.HasErrors);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.AssumedEffects);
        if (forbidden)
            Assert.All(result.Diagnostics.Errors, diagnostic => Assert.Equal(DiagnosticCode.ForbiddenEffect, diagnostic.Code));
        else
        {
            var type = Compile(source);
            Assert.Equal(9, Invoke(type, "Probe", Activator.CreateInstance(type.Assembly.GetType("Native.Reader")!)));
        }
    }

    [Theory]
    [InlineData("\"text\"", "", false, "ITextReader")]
    [InlineData("1", "", true, "IIntReader")]
    [InlineData("1", "cw", false, "IIntReader")]
    public void InterfaceReceiver_InheritedOverloadsKeepTheirOwnRows(
        string argument, string caller, bool forbidden, string selectedOwner)
    {
        var source = $$"""
            module Native Native
              interface IIntReader
                func Read (int:x) → int
                  ! cw
              interface ITextReader
                func Read (string:x) → int
                  !
              interface IReader
                extends IIntReader
                extends ITextReader
              func Probe (IReader:value) → int
                ! {{caller}}
                return value.Read({{argument}})
            """;
        var (_, bound, diagnostics) = Bind(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.Equal("Native." + selectedOwner + ".Read", Assert.IsType<FunctionSymbol>(call.ResolvedSymbol).Name);
        var result = Program.Compile(source);
        Assert.Equal(forbidden, result.HasErrors);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
        if (forbidden)
            Assert.All(result.Diagnostics.Errors, diagnostic => Assert.Equal(DiagnosticCode.ForbiddenEffect, diagnostic.Code));
    }

    [Theory]
    [InlineData("true", DiagnosticCode.NoMatchingOverload)]
    [InlineData("missing", DiagnosticCode.UndefinedReference)]
    public void InterfaceReceiver_InvalidArgumentsDoNotAcquireAContract(string argument, string code)
    {
        var source = $$"""
            module Native Native
              interface IReader
                func Read (int:x) → int
                  !
              func Probe (IReader:value) → int
                !
                return value.Read({{argument}})
            """;
        var result = Program.Compile(source);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == code);
    }

    [Fact]
    public void InterfaceReceiver_ConflictingInheritedSignaturesRemainAmbiguous()
    {
        const string source = """
            module Native Native
              interface IQuiet
                func Read () → int
                  !
              interface ILoud
                func Read () → int
                  ! cw
              interface IReader
                extends IQuiet
                extends ILoud
              func Probe (IReader:value) → int
                !
                return value.Read()
            """;
        var result = Program.Compile(source);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.AmbiguousOverload);
        var compilation = CSharpCompilation.Create("InterfaceAmbiguity",
            [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble
                + new CSharpEmitter().Emit(Parse(source)))],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Contains(compilation.GetDiagnostics(), diagnostic => diagnostic.Id == "CS0121");
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("cw", false)]
    public void InterfaceReceiver_NestedSameNamedDeclarationsKeepTheirOwnIdentity(string caller, bool forbidden)
    {
        var source = $$"""
            module Native Native
              class Left
                interface IReader
                  func Read () → int
                    ! cw
              class Right
                interface IReader
                  func Read () → int
                    !
              func Probe (Left.IReader:left, Right.IReader:right) → int
                ! {{caller}}
                return left.Read() + right.Read()
            """;
        var (_, bound, diagnostics) = Bind(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var calls = BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>().ToArray();
        Assert.Equal(["Native.Left.IReader.Read", "Native.Right.IReader.Read"],
            calls.Select(call => Assert.IsType<FunctionSymbol>(call.ResolvedSymbol).Name));
        Assert.NotEqual(calls[0].ResolvedSymbol!.Id, calls[1].ResolvedSymbol!.Id);
        var result = Program.Compile(source);
        Assert.Equal(forbidden, result.HasErrors);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.AssumedEffects);
        if (forbidden)
            Assert.All(result.Diagnostics.Errors, diagnostic => Assert.Equal(DiagnosticCode.ForbiddenEffect, diagnostic.Code));
        else
            Assert.True(GeneratedCSharpCompiler.Validate(result.GeneratedCode).CompilationSuccess);
    }

    [Theory]
    [InlineData("Read")]
    [InlineData("@Read")]
    public void InterfaceReceiver_ConvertedSignaturesKeepTheOriginalIdentifierSpan(string identifier)
    {
        var source = $"public interface IReader {{ int {identifier}(); }}";
        var result = new CSharpToCalorConverter(new ConversionOptions
            { UserManifestRoot = global::Calor.Tests.Shared.HermeticCalorHome.EmptyManifestRoot }).Convert(source);
        Assert.True(result.Success, string.Join("\n", result.Issues));
        var signature = Assert.Single(Assert.Single(result.Ast!.Interfaces).Methods);
        Assert.Equal(identifier, signature.Name);
        Assert.Equal(identifier, source.Substring(signature.IdentifierSpan.Start, signature.IdentifierSpan.Length));
    }

    [Fact]
    public void InterfaceReceiver_ContractSimplificationKeepsTheOriginalIdentifierSpan()
    {
        const string source = """
            module Native Native
              interface IReader
                func Read () → int
                  !
                  requires 2 > 1
            """;
        var ast = Parse(source);
        var signature = Assert.Single(Assert.Single(ast.Interfaces).Methods);
        var diagnostics = new DiagnosticBag();
        var simplified = new ContractSimplificationPass(diagnostics).Simplify(ast);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var rewritten = Assert.Single(Assert.Single(simplified.Interfaces).Methods);
        Assert.NotSame(signature, rewritten);
        Assert.Equal(signature.IdentifierSpan, rewritten.IdentifierSpan);
        Assert.Equal("Read", source.Substring(rewritten.IdentifierSpan.Start, rewritten.IdentifierSpan.Length));
    }

    [Fact]
    public void InterfaceReceiver_ConditionalPreservationDoesNotAcquireANativeContract()
    {
        const string original = """
            public interface IReader
            {
                int Read();
            #if EXTRA
                int Read(int value);
            #endif
            }
            """;
        var converted = new CSharpToCalorConverter(new ConversionOptions
            { UserManifestRoot = global::Calor.Tests.Shared.HermeticCalorHome.EmptyManifestRoot }).Convert(original);
        Assert.True(converted.Success, string.Join("\n", converted.Issues));
        var declaration = Assert.Single(converted.Ast!.Interfaces);
        Assert.Single(declaration.Methods);
        Assert.Single(declaration.PreprocessorBlocks);
        var source = converted.CalorSource + """

              func Probe (IReader:value) → int
                !
                return value.Read()
            """;
        var (_, bound, diagnostics) = Bind(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.Null(call.ResolvedSymbol);
        var result = Program.Compile(source);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
    }

    [Fact]
    public void InterfaceReceiver_ImplementationCannotWidenTheContract()
    {
        const string source = """
            module Native Native
              interface IReader
                func Read () → int
                  !
              class Reader
                implements IReader
                func Read () → int
                  ! cw
                  System.Console.Write("called")
                  return 9
              func Probe (IReader:value) → int
                !
                return value.Read()
            """;
        var result = Program.Compile(source);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.InterfaceEffectVariance);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
    }

    [Fact]
    public void InterfaceReceiver_NestedImplementationCannotWidenTheContract()
    {
        const string source = """
            module Native Native
              class Outer
                interface IReader
                  func Read () → int
                    !
              class Reader
                implements Outer.IReader
                func Read () → int
                  ! cw
                  System.Console.Write("called")
                  return 9
              func Probe (Outer.IReader:value) → int
                !
                return value.Read()
            """;
        var result = Program.Compile(source);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.InterfaceEffectVariance);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
    }

    [Fact]
    public void InterfaceReceiver_MethodTypeArgumentsAndNamedArgumentsRetainClrBehavior()
    {
        const string source = """
            module Native Native
              interface IReader
                func Echo<T> (T:value) → T
                  !
                func Read (int:first, int:second = 4) → int
                  !
              class Reader
                implements IReader
                func Echo<T> (T:value) → T
                  !
                  return value
                func Read (int:first, int:second = 4) → int
                  !
                  return first * 10 + second
              func Probe (IReader:value) → int
                !
                return value.Echo<int>(9) + value.Read(second: 2, first: 3) + value.Read(1)
            """;
        var type = Compile(source);
        Assert.Equal(55, Invoke(type, "Probe", Activator.CreateInstance(type.Assembly.GetType("Native.Reader")!)));
    }

    [Fact]
    public void InterfaceReceiver_DiamondInheritanceRetainsOneDeclarationIdentity()
    {
        const string source = """
            module Native Native
              interface IReader
                func Read () → int
                  !
              interface ILeft
                extends IReader
              interface IRight
                extends IReader
              interface IBoth
                extends ILeft
                extends IRight
              func Probe (IBoth:value) → int
                !
                return value.Read()
            """;
        var result = Program.Compile(source);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        var (_, bound, diagnostics) = Bind(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.Equal("Native.IReader.Read", Assert.Single(call.ResolvedSymbols).Name);
        Assert.True(GeneratedCSharpCompiler.Validate(result.GeneratedCode).CompilationSuccess);
    }

    [Fact]
    public void InterfaceReceiver_ExternalNamesDoNotBorrowSourceContracts()
    {
        const string source = """
            module Native Native
              interface IDisposable
                func Dispose () → void
                  !
              func Probe (System.IDisposable:value) → void
                !
                value.Dispose()
            """;
        var (_, bound, diagnostics) = Bind(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.Null(call.ResolvedSymbol);
        Assert.Equal("System.IDisposable", call.ResolvedMetadataMethod!.ContainingType.ToDisplayString());
    }

    [Theory]
    [InlineData("", "", false)]
    [InlineData("cw", "", true)]
    [InlineData("cw", "cw", false)]
    public void AbstractReceiver_ChargesTheSelectedDeclarationContract(
        string declared, string caller, bool forbidden)
    {
        var source = $$"""
            module Native Native
              class Base abstract
                func Read abstract () → int
                  ! {{declared}}
              class Derived
                extends Base
                func Read override () → int
                  !
                  return 9
              func Probe (Base:value) → int
                ! {{caller}}
                return value.Read()
            """;
        var (ast, bound, diagnostics) = Bind(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        var signature = Assert.Single(ast.Classes[0].Methods);
        Assert.Equal(signature.Span, Assert.IsType<FunctionSymbol>(call.ResolvedSymbol).DefinitionSpan);
        Assert.Equal("Native.Base.Read", call.ResolvedSymbol.Name);
        var result = Program.Compile(source);
        Assert.Equal(forbidden, result.HasErrors);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.AssumedEffects);
        if (forbidden)
            Assert.All(result.Diagnostics.Errors, diagnostic => Assert.Equal(DiagnosticCode.ForbiddenEffect, diagnostic.Code));
        else
        {
            var type = Compile(source);
            Assert.Equal(9, Invoke(type, "Probe", Activator.CreateInstance(type.Assembly.GetType("Native.Derived")!)));
        }
    }

    [Theory]
    [InlineData("\"text\"", "", false, "string")]
    [InlineData("1", "", true, "int")]
    [InlineData("1", "cw", false, "int")]
    public void AbstractReceiver_OverloadsKeepTheirOwnDeclaredRows(
        string argument, string caller, bool forbidden, string selectedType)
    {
        var source = $$"""
            module Native Native
              class Base abstract
                func Read abstract (int:x) → int
                  ! cw
                func Read abstract (string:x) → int
                  !
              func Probe (Base:value) → int
                ! {{caller}}
                return value.Read({{argument}})
            """;
        var (_, bound, diagnostics) = Bind(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.Equal(selectedType, Assert.Single(Assert.IsType<FunctionSymbol>(call.ResolvedSymbol).Parameters).TypeName);
        var result = Program.Compile(source);
        Assert.Equal(forbidden, result.HasErrors);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
        if (forbidden)
            Assert.All(result.Diagnostics.Errors, diagnostic => Assert.Equal(DiagnosticCode.ForbiddenEffect, diagnostic.Code));
    }

    [Theory]
    [InlineData("true", DiagnosticCode.NoMatchingOverload)]
    [InlineData("missing", DiagnosticCode.UndefinedReference)]
    public void AbstractReceiver_InvalidArgumentsDoNotAcquireAContract(string argument, string code)
    {
        var source = $$"""
            module Native Native
              class Base abstract
                func Read abstract (int:x) → int
                  !
              func Probe (Base:value) → int
                !
                return value.Read({{argument}})
            """;
        var result = Program.Compile(source);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == code);
    }

    [Fact]
    public void AbstractReceiver_OverrideCannotWidenTheContract()
    {
        const string source = """
            module Native Native
              class Base abstract
                func Read abstract () → int
                  !
              class Derived
                extends Base
                func Read override () → int
                  ! cw
                  System.Console.Write("called")
                  return 9
              func Probe (Base:value) → int
                !
                return value.Read()
            """;
        var result = Program.Compile(source);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.OverrideEffectVariance);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
    }

    [Theory]
    [InlineData(4, 5)]
    [InlineData(1, 2)]
    public void LegacyLoopHeader_NestedSiblingOperandsRetainCheckedRuntimeSemantics(int a, int b)
    {
        const string source = """
            module Native Native
              func Probe (int:a, int:b) → int
                !
                var int:count = 0
                for i from 0 to (a - 1) + (b - 2) step 1
                  set count count + 1
                return count
            """;
        var result = Program.Compile(source);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        Assert.Contains("checked(checked(a - 1) + (checked(b - 2)))", result.GeneratedCode);
        Assert.Equal(a + b - 2, Invoke(Compile(source), "Probe", a, b));
        var overflow = Assert.Throws<TargetInvocationException>(() =>
            Invoke(Compile(source), "Probe", int.MinValue, 2));
        Assert.IsType<OverflowException>(overflow.InnerException);
    }

    [Theory]
    [InlineData("dict.GetOrAdd(key, () => new V { P = 1 })")]
    [InlineData("dict.GetOrAdd(key, () => new V { P = 1 }),8")]
    public void CSharpLambdaInitializerHole_PreservesTheOriginalRawSource(string hole)
    {
        var source = Source($"\"${{{hole}}}\"", type: "string");
        foreach (var candidate in new[] { source, new CalorEmitter().Emit(Parse(source)) })
        {
            var part = InterpolationPart(Parse(candidate));
            Assert.Equal(hole.EndsWith(",8", StringComparison.Ordinal) ? "8" : null, part.AlignmentClause);
            var call = Assert.IsType<ExpressionCallNode>(part.Expression);
            var member = Assert.IsType<FieldAccessNode>(call.TargetExpression);
            Assert.Equal("GetOrAdd", member.FieldName);
            Assert.Equal("dict", Assert.IsType<ReferenceNode>(member.Target).Name);
            Assert.Equal(2, call.Arguments.Count);
            Assert.Equal("key", Assert.IsType<ReferenceNode>(call.Arguments[0]).Name);
            var lambda = Assert.IsType<LambdaExpressionNode>(call.Arguments[1]);
            Assert.Empty(lambda.Parameters);
            var creation = Assert.IsType<NewExpressionNode>(lambda.ExpressionBody);
            Assert.Equal("V", creation.TypeName);
            Assert.Empty(creation.Arguments);
            var initializer = Assert.Single(creation.Initializers);
            Assert.Equal("P", initializer.PropertyName);
            Assert.Equal(1, Assert.IsType<IntLiteralNode>(initializer.Value).Value);
        }
    }

    [Theory]
    [InlineData("new Lazy<System.Text.StringBuilder>(() => new System.Text.StringBuilder { Capacity = 8 }).Value.Capacity", "8")]
    [InlineData("((Func<int>)(() => 9))()", "9")]
    [InlineData("Array.IndexOf(new int[] { 1, 2 }, 2)", "1")]
    public void UnsupportedCSharpHoleChildren_KeepTheirRawRuntimeSemantics(string hole, string expected)
    {
        var source = Source($"\"${{{hole}}}\"", type: "string");
        foreach (var candidate in new[] { source, new CalorEmitter().Emit(Parse(source)) })
        {
            // Preserve the original runtime lane; effect enforcement is checked separately below.
            Assert.Equal(expected, Invoke(Compile(candidate, enforceEffects: false), "Probe"));
            var (ast, bound, diagnostics) = Bind(candidate);
            Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
            var expression = InterpolationPart(ast).Expression;
            var result = Program.Compile(candidate);
            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "Calor0419");
            if (hole.StartsWith("new Lazy", StringComparison.Ordinal))
            {
                var capacity = Assert.IsType<FieldAccessNode>(expression);
                Assert.Equal("Capacity", capacity.FieldName);
                var value = Assert.IsType<FieldAccessNode>(capacity.Target);
                Assert.Equal("Value", value.FieldName);
                var lazy = Assert.IsType<NewExpressionNode>(value.Target);
                Assert.Equal("Lazy<System.Text.StringBuilder>", lazy.TypeName);
                Assert.Empty(lazy.TypeArguments);
                var factory = Assert.IsType<LambdaExpressionNode>(Assert.Single(lazy.Arguments));
                Assert.Empty(factory.Parameters);
                var builder = Assert.IsType<NewExpressionNode>(factory.ExpressionBody);
                Assert.Equal("System.Text.StringBuilder", builder.TypeName);
                Assert.Empty(builder.Arguments);
                var initializer = Assert.Single(builder.Initializers);
                Assert.Equal("Capacity", initializer.PropertyName);
                Assert.Equal(8, Assert.IsType<IntLiteralNode>(initializer.Value).Value);
                var boundFactory = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundLambdaExpression>());
                Assert.Equal(factory.Id, boundFactory.Id);
                Assert.Empty(boundFactory.Parameters);
                var boundBuilder = Assert.IsType<BoundNewExpression>(boundFactory.ExpressionBody);
                Assert.Empty(boundBuilder.Arguments);
                var boundInitializer = Assert.Single(boundBuilder.Initializers);
                Assert.Equal("Capacity", boundInitializer.MemberName);
                Assert.Equal(8, Assert.IsType<BoundIntLiteral>(boundInitializer.Value).Value);
                var unknown = result.Diagnostics.Where(diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall).ToArray();
                Assert.Equal(3, unknown.Length);
                Assert.Contains(unknown, diagnostic => diagnostic.Message.Contains("System.Lazy`1..ctor(Func)", StringComparison.Ordinal));
                Assert.Contains(unknown, diagnostic => diagnostic.Message.Contains("Lazy`1<StringBuilder>.get_Value", StringComparison.Ordinal));
                Assert.Contains(unknown, diagnostic => diagnostic.Message.Contains("?.get_Capacity", StringComparison.Ordinal));
                Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                    diagnostic.Code == DiagnosticCode.ForbiddenEffect && diagnostic.Message.Contains("'unknown'", StringComparison.Ordinal));
            }
            else if (hole.StartsWith("Array.IndexOf", StringComparison.Ordinal))
            {
                var call = Assert.IsType<ExpressionCallNode>(expression);
                var member = Assert.IsType<FieldAccessNode>(call.TargetExpression);
                Assert.Equal("IndexOf", member.FieldName);
                Assert.Equal("Array", Assert.IsType<ReferenceNode>(member.Target).Name);
                Assert.Equal(2, call.Arguments.Count);
                var array = Assert.IsType<ArrayCreationNode>(call.Arguments[0]);
                Assert.Equal([1L, 2L], array.Initializer.Select(element => Assert.IsType<IntLiteralNode>(element).Value));
                Assert.Equal(2, Assert.IsType<IntLiteralNode>(call.Arguments[1]).Value);
                var selected = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
                Assert.Equal("System.Array", selected.ResolvedMetadataMethod!.ContainingType.ToDisplayString());
                Assert.Equal("IndexOf", selected.ResolvedMetadataMethod.Name);
                Assert.Equal(SpecialType.System_Int32, Assert.Single(selected.ResolvedMetadataMethod.TypeArguments).SpecialType);
                Assert.Equal(2, selected.Arguments.Count);
                var boundArray = Assert.IsType<BoundArrayCreation>(selected.Arguments[0]);
                Assert.Equal(array.Id, boundArray.Id);
                Assert.Equal([1L, 2L], boundArray.Initializer.Select(element => Assert.IsType<BoundIntLiteral>(element).Value));
                Assert.Equal(2, Assert.IsType<BoundIntLiteral>(selected.Arguments[1]).Value);
                Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
                Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                    diagnostic.Code == DiagnosticCode.ForbiddenEffect && diagnostic.Message.Contains("'alloc'", StringComparison.Ordinal));
            }
            else
            {
                var call = Assert.IsType<ExpressionCallNode>(expression);
                var cast = Assert.IsType<TypeOperationNode>(call.TargetExpression);
                Assert.Equal(TypeOp.Cast, cast.Operation);
                Assert.Equal("Func<int>", cast.TargetType);
                var lambda = Assert.IsType<LambdaExpressionNode>(cast.Operand);
                Assert.Empty(lambda.Parameters);
                Assert.Equal(9, Assert.IsType<IntLiteralNode>(lambda.ExpressionBody).Value);
                var boundCall = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundExpressionCall>());
                Assert.Empty(boundCall.Arguments);
                var boundCast = Assert.IsType<BoundTypeOperationExpression>(boundCall.Target);
                Assert.Equal(cast.Operation, boundCast.Operation);
                Assert.Equal(cast.TargetType, boundCast.TargetType);
                var boundLambda = Assert.IsType<BoundLambdaExpression>(boundCast.Operand);
                Assert.Equal(lambda.Id, boundLambda.Id);
                Assert.Empty(boundLambda.Parameters);
                Assert.Equal(9, Assert.IsType<BoundIntLiteral>(boundLambda.ExpressionBody).Value);
                var signature = Assert.IsType<Calor.Compiler.Binding.BoundTypes.FunctionBoundType>(boundCast.Type);
                Assert.Empty(signature.ParameterTypes);
                Assert.Equal(Calor.Compiler.Binding.BoundTypes.EffectRow.Unknown, signature.Row);
                Assert.Equal(signature.ReturnType, boundCall.Type);
                Assert.False(boundCall.HasUnknownInferredType);
                Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
                Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code.StartsWith("Calor04", StringComparison.Ordinal));
            }
        }
    }

    [Fact]
    public void LegacyTypeArrayWitness_PreservesBothElementsThroughRuntimeAndSourceRoundTrip()
    {
        const string source = """
            module Native Native
              func Probe () → System.Type[]
                ! alloc
                var _hoist000 = new System.Type[] { typeof(Model1Validator), typeof(Model2Validator) }
                return _hoist000
              class Model1Validator
              class Model2Validator
            """;
        var array = Assert.IsType<ArrayCreationNode>(Assert.IsType<BindStatementNode>(
            Assert.Single(Parse(source).Functions).Body[0]).Initializer);
        Assert.Equal(2, array.Initializer.Count);
        foreach (var candidate in new[] { source, Parse(source).Accept(new CalorEmitter()) })
        {
            var types = Assert.IsType<Type[]>(Invoke(Compile(candidate), "Probe"));
            Assert.Equal(["Native.Model1Validator", "Native.Model2Validator"],
                types.Select(type => type.FullName));
        }
    }

    [Theory]
    [InlineData("new int[] { (int)1, (int)2 }")]
    [InlineData("new int[] { ((int)1), (int)2 }")]
    [InlineData("new int[] { ((int)1), ((int)2) }")]
    [InlineData("new List<int> { (int)1, (int)2 }")]
    [InlineData("new HashSet<int> { (int)1, (int)2 }")]
    [InlineData("new Dictionary<int,int> { { (int)1, (int)10 }, { (int)2, (int)20 } }")]
    [InlineData("new int[,] { { (int)1, (int)2 } }")]
    [InlineData("new int[] { (Echo (1)), (Echo (2)) }")]
    [InlineData("new int[] { Echo (1), (Echo (2)) }")]
    [InlineData("new int[] { Echo(Echo (1)), (Echo (2)) }")]
    [InlineData("new List<int> { Echo(1), Echo(2) }")]
    [InlineData("new HashSet<int> { (Echo (1)), (Echo (2)) }")]
    [InlineData("new Dictionary<int,int> { { (Echo (1)), (Echo (10)) }, { (Echo (2)), (Echo (20)) } }")]
    [InlineData("new int[,] { { (Echo (1)), (Echo (2)) } }")]
    public void LegacyCollectionElements_KeepTheirBoundariesAndExplicitNativeCallDelimiters(string expression)
    {
        var source = $$"""
            module Native Native
              func Echo private (int:value) → int
                return value
              func Probe () → object
                ! alloc
                let values = {{expression}}
                return values
            """;
        foreach (var candidate in new[] { source, Parse(source).Accept(new CalorEmitter()) })
        {
            var value = Invoke(Compile(candidate), "Probe");
            if (value is IDictionary<int, int> dictionary)
            {
                Assert.Equal(2, dictionary.Count);
                Assert.Equal(10, dictionary[1]);
                Assert.Equal(20, dictionary[2]);
            }
            else if (value is int[,] matrix)
            {
                Assert.Equal(1, matrix.GetLength(0));
                Assert.Equal(2, matrix.GetLength(1));
                Assert.Equal(1, matrix[0, 0]);
                Assert.Equal(2, matrix[0, 1]);
            }
            else if (value is HashSet<int> set)
                Assert.True(set.SetEquals([1, 2]));
            else
                Assert.Equal([1, 2], Assert.IsAssignableFrom<IEnumerable<int>>(value));
        }
    }

    [Fact]
    public void Chaining_UsesExistingStructuralNodesAndCompleteSpans()
    {
        const string expression = "Factory().Items[Next(), 1].Pick<int>(second: in value, first: ref other)";
        var call = Assert.IsType<ExpressionCallNode>(Expression(expression));
        Assert.Equal(expression.Length, call.Span.Length);
        Assert.Equal(["int"], call.TypeArguments);
        Assert.Equal(["second", "first"], call.ArgumentNames);
        Assert.Equal(["in", "ref"], call.ArgumentModifiers);
        var method = Assert.IsType<FieldAccessNode>(call.TargetExpression);
        Assert.Equal("Pick", Slice(expression, call.Span.Start, method.FieldNameSpan));
        Assert.Equal("value", Slice(expression, call.Span.Start, call.Arguments[0].Span));
        var element = Assert.IsType<MultiDimArrayAccessNode>(method.Target);
        Assert.Equal(2, element.Indices.Count);
        Assert.IsType<CallExpressionNode>(element.Indices[0]);
        var items = Assert.IsType<FieldAccessNode>(element.Array);
        Assert.IsType<CallExpressionNode>(items.Target);
    }

    [Fact]
    public void CallsGroupsTuplesAndComparisons_RemainDistinct()
    {
        Assert.Empty(Assert.IsType<CallExpressionNode>(Expression("f()")).Arguments);
        Assert.IsType<ReferenceNode>(Expression("(f)"));
        Assert.IsType<TupleLiteralNode>(Expression("(f, x)"));
        Assert.IsType<CallExpressionNode>(Expression("(f())"));
        Assert.IsType<ArrayAccessNode>(Expression("(items[0])"));
        Assert.IsType<CallExpressionNode>(Expression("(f) (x)"));
        Assert.IsType<ExpressionCallNode>(Expression("factory()(x)"));
        Assert.IsType<ExpressionCallNode>(Expression("(gate ? first : second)(x)"));
        var generic = Assert.IsType<CallExpressionNode>(Expression("M < Dictionary<string, List<int>> > (x)"));
        Assert.Equal("M", generic.Target);
        Assert.Equal(["Dictionary<string,List<int>>"], generic.TypeArguments!.Select(type => type.Replace(" ", "")));
        generic = Assert.IsType<CallExpressionNode>(Expression("M<(int First, string Second)>(x)"));
        Assert.Equal(["(int First,string Second)"], generic.TypeArguments!.Select(type => type.Replace(", ", ",")));
        Assert.Equal(["@class"], Assert.IsType<CallExpressionNode>(Expression("M<@class>(x)")).TypeArguments);
        Assert.IsType<TupleLiteralNode>(Expression("(M<T, U>(x), y < z)"));
        var relational = Assert.IsType<BinaryOperationNode>(Expression("a < b > c"));
        Assert.Equal(BinaryOperator.GreaterThan, relational.Operator);
        Assert.Equal(BinaryOperator.LessThan, Assert.IsType<BinaryOperationNode>(relational.Left).Operator);
        Assert.IsType<BinaryOperationNode>(Expression("(a) < b > (c)"));
        Assert.IsType<UnaryOperationNode>(Expression("-f () * 2") is BinaryOperationNode product
            ? product.Left : throw new InvalidOperationException());
    }

    [Fact]
    public void GenericCallLookahead_DoesNotJoinTheFollowingPhysicalLine()
    {
        const string source = """
            module Native Native
              func Probe () → void
                let selected = M<int>
                (f)
            """;
        var diagnostics = new DiagnosticBag();
        var body = Assert.Single(Parse(source, diagnostics).Functions).Body;
        var error = Assert.Single(diagnostics.Errors);
        Assert.Equal(DiagnosticCode.UnexpectedToken, error.Code);
        Assert.Equal("A native expression statement must be an invocation or object creation.", error.Message);
        Assert.Equal(4, error.Span.Line);
        Assert.Equal(5, error.Span.Column);
        Assert.Equal("(f)", source.Substring(error.Span.Start, error.Span.Length));
        Assert.Equal(2, body.Count);
        Assert.Equal("M<int>", Assert.IsType<ReferenceNode>(
            Assert.IsType<BindStatementNode>(body[0]).Initializer).Name);
        Assert.IsType<ReferenceNode>(Assert.IsType<ExpressionStatementNode>(body[1]).Expression);
    }

    [Theory]
    [InlineData("(obj.Child.Pick)(value)", "Pick")]
    [InlineData("obj.@class(value)", "@class")]
    [InlineData("obj . @class(value)", "@class")]
    [InlineData("Factory().@class(value)", "@class")]
    public void MemberIdentifierSpans_ExcludeGroupingAndRetainEscapes(string expression, string identifier)
    {
        var call = Assert.IsType<ExpressionCallNode>(Expression(expression));
        var member = Assert.IsType<FieldAccessNode>(call.TargetExpression);
        Assert.Equal(expression.Length, call.Span.Length);
        Assert.Equal(identifier, Slice(expression, call.Span.Start, member.FieldNameSpan));
    }

    [Theory]
    [InlineData("@this.Pick(value)", "this")]
    [InlineData("@base.Pick(value)", "base")]
    public void EscapedReceiverNames_AreValuesNotInstanceKeywords(string expression, string name)
    {
        var call = Assert.IsType<ExpressionCallNode>(Expression(expression));
        var member = Assert.IsType<FieldAccessNode>(call.TargetExpression);
        Assert.Equal(name, Assert.IsType<ReferenceNode>(member.Target).Name);
        Assert.Equal(expression.Length, call.Span.Length);
        Assert.Equal(expression, call.Accept(new CSharpEmitter()));
    }

    [Theory]
    [InlineData("set x (1 + 2)")]
    [InlineData("set items[0] (1 + 2)")]
    [InlineData("set map[\"x\"] (1 + 2)")]
    public void LegacyPositionalOperands_KeepTheirGroupedValue(string statement)
    {
        var body = Assert.Single(Parse(Source(statement, statement: true)).Functions).Body;
        var value = Assert.Single(body) switch
        {
            AssignmentStatementNode assignment => assignment.Value,
            CollectionSetIndexNode set => set.Value,
            DictionaryPutNode put => put.Value,
            _ => throw new InvalidOperationException()
        };
        Assert.IsType<BinaryOperationNode>(value);
        Assert.IsType<BinaryOperationNode>(Expression("x + (1 + 2)"));
        Assert.IsType<CallExpressionNode>(Assert.IsType<BinaryOperationNode>(
            Expression("f() + g()")).Left);
    }

    [Fact]
    public void LegacyNestedOperands_KeepTheirOuterOperandBoundary()
    {
        var assignment = Assert.IsType<AssignmentStatementNode>(Assert.Single(
            Assert.Single(Parse(Source("set items[0] (items[0] + 4)", statement: true)).Functions).Body));
        Assert.IsType<IntLiteralNode>(Assert.IsType<ArrayAccessNode>(assignment.Target).Index);
        Assert.IsType<BinaryOperationNode>(assignment.Value);
        Assert.IsType<ArrayAccessNode>(Assert.IsType<BinaryOperationNode>(
            Expression("items[0] + (1 + 2)")).Left);
        var lengthSum = Assert.IsType<BinaryOperationNode>(Expression("(items).Length + (1 + 2)"));
        var length = Assert.IsType<FieldAccessNode>(lengthSum.Left);
        Assert.Equal("Length", length.FieldName);
        Assert.Equal("items", Assert.IsType<ReferenceNode>(length.Target).Name);
        var sibling = Assert.IsType<BinaryOperationNode>(lengthSum.Right);
        Assert.Equal(BinaryOperator.Add, sibling.Operator);
        Assert.Equal(1, Assert.IsType<IntLiteralNode>(sibling.Left).Value);
        Assert.Equal(2, Assert.IsType<IntLiteralNode>(sibling.Right).Value);
        var type = Compile("""
            module Native Native
              func Probe (int[]:items) → int
                !
                return (items).Length + (1 + 2)
            """);
        Assert.Equal(5, Invoke(type, "Probe", new[] { 10, 20 }));
        Assert.Equal(3, Invoke(type, "Probe", Array.Empty<int>()));
        Assert.IsType<CallExpressionNode>(Assert.IsType<BinaryOperationNode>(
            Expression("f(g (1), items[h (2)]) + 3")).Left);
    }

    [Theory]
    [InlineData("x?.M(1)")]
    [InlineData("factory()?.M(1)")]
    [InlineData("x?.Factory().M()")]
    public void NativeConditionalInvocation_PreservesThePostfixChain(string expression)
    {
        var source = $"module Native Native\n  func Probe () → object\n    !\n    return {expression}\n";
        var diagnostics = new DiagnosticBag();
        var ast = Parse(source, diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.IsType<ExpressionCallNode>(
            Assert.IsType<ReturnStatementNode>(Assert.Single(Assert.Single(ast.Functions).Body)).Expression);
        Assert.Contains("?.", new CalorEmitter().Emit(ast));
        Assert.DoesNotContain("§", new CalorEmitter().Emit(ast));
        Assert.Equal(expression, call.Accept(new CSharpEmitter()));
    }

    [Theory]
    [InlineData("x?.Items[0]")]
    [InlineData("x?[0]")]
    [InlineData("x?.M<int>(1)")]
    [InlineData("x?.M(value: 1)")]
    [InlineData("x?.M(ref value)")]
    [InlineData("(x?.Length).ToString()")]
    [InlineData("((x?.M())).N()")]
    [InlineData("(x?.Factory())()")]
    public void NativeUnsupportedConditionalForms_RemainGated(string expression)
    {
        var diagnostics = new DiagnosticBag();
        Parse($"module Native Native\n  func Probe () → object\n    !\n    return {expression}\n", diagnostics);
        Assert.True(diagnostics.HasErrors, expression);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains("not yet supported"));
    }

    [Fact]
    public void NativeConditionalInvocation_BindsItsChildrenAndDoesNotLaunderEffects()
    {
        const string source = """
            module Native Native
              func Step () → int
                ! cw
                System.Console.WriteLine(1)
                return 0
              func Probe (string?:x) → string?
                !
                return x?.Substring(Step())
            """;
        var (ast, bound, diagnostics) = Bind(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var conditional = Assert.IsType<BoundConditionalAccessExpression>(Assert.IsType<BoundReturnStatement>(
            Assert.Single(bound.Functions.Single(function => function.Symbol.Name == "Probe").Body)).Expression);
        var call = Assert.IsType<BoundCallExpression>(conditional.WhenNotNull);
        Assert.Equal("Substring", call.ResolvedMetadataMethod!.Name);
        Assert.Equal("x", Assert.IsType<BoundVariableExpression>(conditional.Receiver).Variable.Name);
        Assert.Equal([call], conditional.DeferredChildren);
        Assert.IsType<BoundCallExpression>(Assert.Single(call.Arguments));
        Assert.False(call.HasUnknownInferredType);
        var result = Program.Compile(new CalorEmitter().Emit(ast));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "Calor0419");
    }

    [Theory]
    [InlineData("Produce()?.ToString()")]
    [InlineData("x?.ToString() ?? Produce()")]
    public void NativeConditionalInvocation_ChargesReceiverAndCoalescingRhsEffects(string expression)
    {
        var source = $$"""
            module Native Native
              func Produce () → string?
                ! cw
                System.Console.WriteLine(1)
                return null
              func Probe (string?:x) → string?
                !
                return {{expression}}
            """;
        var result = Program.Compile(source);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "Calor0419");
    }

    [Theory]
    [InlineData("f(1,)")]
    [InlineData("f(ref)")]
    [InlineData("f(first:)")]
    [InlineData("f(, 1)")]
    [InlineData("f<>(1)")]
    [InlineData("f<int,>(1)")]
    [InlineData("items[]")]
    [InlineData("items[0,]")]
    [InlineData("items[0")]
    [InlineData("factory().")]
    [InlineData("x?.M<int>(1)")]
    [InlineData("x?.M(value: 1)")]
    [InlineData("x?.M(ref value)")]
    [InlineData("x?.Items[0]")]
    [InlineData("x?[0]")]
    public void MalformedAndUnsupportedConditionalForms_ReportErrors(string expression)
    {
        var diagnostics = new DiagnosticBag();
        Parse(Source(expression), diagnostics);
        Assert.True(diagnostics.HasErrors, expression);
    }

    [Fact]
    public void MissingArgument_LeavesTheNextStatementForRecovery()
    {
        var diagnostics = new DiagnosticBag();
        var module = Parse("""
            module Native Native
              func Probe () → int
                f(
                return 7
            """, diagnostics);
        Assert.True(diagnostics.HasErrors);
        Assert.Equal(7, Assert.IsType<IntLiteralNode>(Assert.IsType<ReturnStatementNode>(
            Assert.Single(module.Functions).Body.Last()).Expression).Value);
    }

    [Fact]
    public void NativeMemberBinding_RetainsExactDeclarationsAndMetadata()
    {
        var (ast, bound, diagnostics) = Bind("""
            module Native Native
              func Use (Holder:h) → int
                !
                return Get(h).Pick(value: 3)
              func Get (Holder:h) → Holder
                !
                return h
              class Holder
                func Pick (int:value) → int
                  !
                  return value
                func Pick (string:value) → string
                  !
                  return value
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var call = Assert.IsType<BoundCallExpression>(Assert.IsType<BoundReturnStatement>(
            Assert.Single(use.Body)).Expression);
        Assert.Same(call.EvaluatedReceiver, call.Children[0]);
        Assert.Same(call.Arguments[0], call.Children[1]);
        Assert.Same(bound.Functions.Single(function => function.Symbol.Name == "Native.Holder.Pick"
            && function.Symbol.ReturnType == "int").Symbol, call.ResolvedSymbol);
        Assert.Contains($"Holder.{ast.Classes[0].Methods[0].Id}",
            CallGraphAnalysis.Build(ast).ResolveCallSites(ast.Functions[0].Id, "<expression-call>", call.Span));

        (_, bound, diagnostics) = Bind("""
            module Native Native
              func Array (System.Globalization.CultureInfo:culture) → string[]
                !
                return culture.DateTimeFormat.GetAllDateTimePatterns()
              func Generic (System.Collections.ArrayList:items) → IEnumerable<int>
                !
                return items.Cast<int>()
              func Static (int:x) → int
                !
                return Math.Abs(x)
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var calls = BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>().ToArray();
        var array = calls.Single(call => call.ResolvedMetadataMethod?.Name == "GetAllDateTimePatterns");
        Assert.Same(array.ResolvedMetadataMethod!.ReturnType, array.MetadataArrayResultType);
        Assert.Equal(SpecialType.System_String, array.MetadataArrayResultType!.ElementType.SpecialType);
        var generic = calls.Single(call => call.ResolvedMetadataMethod?.Name == "Cast");
        Assert.Equal(SpecialType.System_Int32, Assert.Single(generic.ResolvedMetadataMethod!.TypeArguments).SpecialType);
        Assert.Equal("System.Math", calls.Single(call => call.ResolvedMetadataMethod?.Name == "Abs")
            .ResolvedMetadataMethod!.ContainingType.ToDisplayString());
    }

    [Theory]
    [InlineData("h.Pick(true)")]
    [InlineData("h.Pick(value: 1, value: 2)")]
    public void NativeOverloadErrors_AreNotHidden(string expression)
    {
        var (_, _, diagnostics) = Bind($$"""
            module Native Native
              func Use (Holder:h) → int
                !
                return {{expression}}
              class Holder
                func Pick (int:value) → int
                  !
                  return value
            """);
        Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.NoMatchingOverload);
    }

    [Fact]
    public void NativeGenericAndStaticCalls_KeepNativeIdentityRatherThanBclAliases()
    {
        var source = """
            module Native Native
              func Probe () → int
                !
                return Random.Echo<int>(value: 7)
              class Random
                func Echo<T> static (T:value) → T
                  !
                  return value
            """;
        var (_, bound, diagnostics) = Bind(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.Same(bound.Functions.Single(function => function.Symbol.Name == "Native.Random.Echo").Symbol, call.ResolvedSymbol);
        Assert.Equal(["int"], call.TypeArguments);
        Assert.Null(call.ResolvedMetadataMethod);
        Assert.IsType<BoundTypeReferenceExpression>(call.EvaluatedReceiver);
        Assert.Equal(7, Invoke(Compile(source), "Probe"));
        Assert.Equal(3, Invoke(Compile(Source("System.Math.Abs(-3)")), "Probe"));
    }

    [Theory]
    [InlineData("  func Use<T> () → int\n    !\n    return T.Pick()")]
    [InlineData("  class Container<T>\n    func Use static () → int\n      !\n      return T.Pick()")]
    [InlineData("  class Container\n    func Use<T> static () → int\n      !\n      return T.Pick()")]
    public void TypeParameterReceivers_DoNotBindAnUnrelatedSourceClass(string declaration)
    {
        var source = $$"""
            module Native Native
            {{declaration}}
              class T
                func Pick static () → int
                  !
                  return 1
            """;
        var (_, bound, diagnostics) = Bind(source);
        Assert.True(diagnostics.HasErrors);
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.Null(call.ResolvedSymbol);
        Assert.IsType<BoundVariableExpression>(call.EvaluatedReceiver);
        var result = Program.Compile(source, "native-postfix.calr", new CompilationOptions
            { EnableTypeChecking = true, EnforceEffects = true, StatusWriter = TextWriter.Null });
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LexicalValueReceiver_ShadowingAClassRetainsItsRealMethodIdentity(bool inherited)
    {
        var ownerMember = inherited ? "extends Parent" : "field Holder:T public static";
        var (_, bound, diagnostics) = Bind($$"""
            module Native Native
              class Owner
                {{ownerMember}}
                func Use static () → int
                  !
                  return T.Pick()
              class Parent
                field Holder:T public static
              class T
                func Pick static () → int
                  !
                  return 1
              class Holder
                func Pick () → int
                  !
                  return 2
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.Same(bound.Functions.Single(function => function.Symbol.Name == "Native.Holder.Pick").Symbol,
            call.ResolvedSymbol);
        Assert.IsType<BoundVariableExpression>(call.EvaluatedReceiver);
    }

    [Fact]
    public void TypeParameterReceiver_DoesNotBindUnrelatedBclMetadata()
    {
        var source = """
            module Native Native
              func Use<Math> () → int
                !
                return Math.Abs(-3)
            """;
        var (_, bound, diagnostics) = Bind(source);
        Assert.True(diagnostics.HasErrors);
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.Null(call.ResolvedSymbol);
        Assert.Null(call.ResolvedMetadataMethod);
        Assert.IsType<BoundVariableExpression>(call.EvaluatedReceiver);
        var result = Program.Compile(source, "native-postfix.calr", new CompilationOptions
            { EnableTypeChecking = true, EnforceEffects = true, StatusWriter = TextWriter.Null });
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
    }

    [Fact]
    public void NestedTypeReceiver_RetainsItsLexicalDeclarationIdentity()
    {
        var (_, bound, diagnostics) = Bind("""
            module Native Native
              class Owner
                func Use static () → int
                  !
                  return T.Pick()
                class T
                  func Pick static () → int
                    !
                    return 2
              class T
                func Pick static () → int
                  !
                  return 1
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.Same(bound.Functions.Single(function => function.Symbol.Name == "Native.Owner.T.Pick").Symbol,
            call.ResolvedSymbol);
        Assert.Equal(call.ResolvedSymbol!.ContainingTypeName,
            Assert.IsType<BoundTypeReferenceExpression>(call.EvaluatedReceiver).ResolvedType!.QualifiedName);
    }

    [Theory]
    [InlineData("missing.Pick(1)", "missing")]
    [InlineData("System.Math.Abs(missing)", "missing")]
    [InlineData("System.Math.Abs(GetMissing())", "GetMissing")]
    public void DeferredReceiverClassification_DoesNotHideUndefinedInputs(string expression, string name)
    {
        var result = Program.Compile(Source(expression), "native-postfix.calr", new CompilationOptions
            { EnableTypeChecking = true, EnforceEffects = false, StatusWriter = TextWriter.Null });
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Message.Contains(name, StringComparison.Ordinal));
    }

    [Fact]
    public void NativeUnknownCalls_StillFailClosed()
    {
        var result = Program.Compile("""
            module Native Native
              func Use (System.Random:random) → void
                !
                random.DoesNotExist()
            """, "native-postfix.calr", new CompilationOptions
            { EnableTypeChecking = true, EnforceEffects = true, StatusWriter = TextWriter.Null });
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
    }

    [Fact]
    public void NativeConditionalDelegate_StillInvalidatesCapturedFacts()
    {
        var (_, _, diagnostics) = Bind("""
            module Native Native
              func Use (string?:input, bool:gate) → int
                !
                var string?:value = input
                let Action:later = () =>
                  set value null
                if value != null
                  (gate ? later : later)()
                  let string:observed = value
                  return 1
                return 0
            """);
        Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == "Calor0272");
    }

    [Fact]
    public void NativeCalls_PreserveOrderedIndexReceiverAndArgumentsAtRuntime()
    {
        var type = Compile(RuntimeSource("""
            GetLists(trace, items)[Index(trace)].Add(Number(trace, 3))
            return items[0].IndexOf(3)
            """));
        var trace = new List<int>();
        var items = new[] { new List<int>() };
        Assert.Equal(0, Invoke(type, "Probe", trace, items));
        Assert.Equal([1, 2, 3], trace);
        Assert.Equal([3], items[0]);
    }

    [Theory]
    [InlineData("false && GetLists(trace, items)[Index(trace)].Contains(Number(trace, 3))", false, false)]
    [InlineData("true || GetLists(trace, items)[Index(trace)].Contains(Number(trace, 3))", true, false)]
    [InlineData("true && GetLists(trace, items)[Index(trace)].Contains(Number(trace, 3))", true, true)]
    [InlineData("false ? GetLists(trace, items)[Index(trace)].Contains(Number(trace, 3)) : true", true, false)]
    [InlineData("true ? GetLists(trace, items)[Index(trace)].Contains(Number(trace, 3)) : false", true, true)]
    public void NativeCalls_StayInsideLazyRegions(string expression, bool expected, bool evaluated)
    {
        var type = Compile(RuntimeSource($"return {expression}", "bool"));
        var trace = new List<int>();
        Assert.Equal(expected, Invoke(type, "Probe", trace, new[] { new List<int> { 3 } }));
        Assert.Equal(evaluated ? [1, 2, 3] : Array.Empty<int>(), trace);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1 / zero")]
    public void NativeExceptions_StopBeforeLaterOperands(string index)
    {
        var type = Compile(RuntimeSource(
            $"GetLists(trace, items)[{index}].Add(Number(trace, 3))\nreturn 0", zero: true));
        var trace = new List<int>();
        var error = Assert.Throws<TargetInvocationException>(() =>
            Invoke(type, "Probe", trace, Array.Empty<List<int>>(), 0));
        Assert.True(error.InnerException is IndexOutOfRangeException or DivideByZeroException);
        Assert.Equal([1], trace);
    }

    [Fact]
    public void NativeArgumentExceptions_StopBeforeLaterArguments()
    {
        var type = Compile(RuntimeSource(
            "GetLists(trace, items)[0].Insert(Number(trace, 2) / zero, Number(trace, 3))\nreturn 0", zero: true));
        var trace = new List<int>();
        var items = new[] { new List<int>() };
        var error = Assert.Throws<TargetInvocationException>(() => Invoke(type, "Probe", trace, items, 0));
        Assert.IsType<DivideByZeroException>(error.InnerException);
        Assert.Equal([1, 2], trace);
        Assert.Empty(items[0]);
    }

    [Theory]
    [InlineData("9", 9, false)]
    [InlineData("null", 0, true)]
    public void NativeCalls_StayInsideCoalescingFallbacks(string value, int expected, bool evaluated)
    {
        var type = Compile(RuntimeSource(
            $"let int?:value = {value}\nreturn value ?? GetLists(trace, items)[Index(trace)].IndexOf(Number(trace, 3))"));
        var trace = new List<int>();
        Assert.Equal(expected, Invoke(type, "Probe", trace, new[] { new List<int> { 3 } }));
        Assert.Equal(evaluated ? [1, 2, 3] : Array.Empty<int>(), trace);
    }

    [Theory]
    [InlineData(true, 8)]
    [InlineData(false, 9)]
    public void NativeConditionalDelegate_ExecutesOnlyTheSelectedTarget(bool gate, int expected)
    {
        var type = Compile("""
            module Native Native
              func Probe (bool:gate, Func<int,int>:first !, Func<int,int>:second !) → int
                !
                return (gate ? first : second)(7)
            """);
        var calls = new List<int>();
        Func<int, int> first = value => { calls.Add(1); return value + 1; };
        Func<int, int> second = value => { calls.Add(2); return value + 2; };
        Assert.Equal(expected, Invoke(type, "Probe", gate, first, second));
        Assert.Equal(gate ? [1] : new[] { 2 }, calls);
    }

    [Theory]
    [InlineData("ref", 7)]
    [InlineData("out", 7)]
    [InlineData("in", 2)]
    public void NativeNamesAndModifiers_RoundTripAndExecute(string modifier, int expected)
    {
        var source = $$"""
            module Native Native
              func Probe (Holder:h) → int
                ! mut
                var int:value = 2
                h.Update(item: {{modifier}} value)
                return value
              class Holder
                func Update ({{modifier}} int:item) → void
                  ! mut
                  {{(modifier == "in" ? "let int:observed = item" : "set item 7")}}
            """;
        var ast = Parse(source);
        var roundTrip = ast.Accept(new CalorEmitter());
        var type = Compile(roundTrip);
        var holder = Activator.CreateInstance(type.Assembly.GetType("Native.Holder")!);
        Assert.Equal(expected, Invoke(type, "Probe", holder));
        Assert.Contains($"item: {modifier} value", roundTrip);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeFactoryReturnRows_RemainEnforced(bool permitsWrite)
    {
        var result = Program.Compile($$"""
            module Native Native
              func Use (Holder:h) → int
                ! {{(permitsWrite ? "cw" : "")}}
                return h.Make()()
              class Holder
                func Make () → Func<int> ! cw
                  !
                  return () => 0
            """, "native-postfix.calr", new CompilationOptions
            { EnableTypeChecking = true, EnforceEffects = true, StatusWriter = TextWriter.Null });
        Assert.Equal(!permitsWrite, result.HasErrors);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.EffectRowUnknown);
        if (!permitsWrite)
            Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == "Calor0410");
    }

    [Fact]
    public void ComputedGenericSerialization_ReparsesAndKeepsSelectedBclMethod()
    {
        var expression = new ExpressionCallNode(default,
            new FieldAccessNode(default, new NewExpressionNode(default, "System.Collections.ArrayList", [], []), "Cast"),
            [], typeArguments: ["i32"]);
        var text = expression.Accept(new CalorEmitter());
        Assert.EndsWith(".Cast<int>()", text);
        var (_, bound, diagnostics) = Bind(Source(text, type: "IEnumerable<int>"));
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.Equal("Cast", call.ResolvedMetadataMethod?.Name);
        Assert.Equal(SpecialType.System_Int32, Assert.Single(call.ResolvedMetadataMethod!.TypeArguments).SpecialType);
    }

    [Fact]
    public void CastDelegateInvocation_IsStructuralRatherThanARawStringTarget()
    {
        var result = new CSharpToCalorConverter(new ConversionOptions
            { UserManifestRoot = global::Calor.Tests.Shared.HermeticCalorHome.EmptyManifestRoot }).Convert("""
            public static class Probe {
                public static object Run(System.Func<object> value) => ((System.Func<object>)value)();
            }
            """);
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.DoesNotContain(result.Losses, loss => loss.Feature == "raw-call-target");
        var method = Assert.Single(Assert.Single(result.Ast!.Classes).Methods);
        var call = Assert.IsType<ExpressionCallNode>(
            Assert.IsType<ReturnStatementNode>(Assert.Single(method.Body)).Expression);
        Assert.IsType<TypeOperationNode>(call.TargetExpression);
        Assert.IsType<ExpressionCallNode>(Expression(call.Accept(new CalorEmitter())));
    }

    [Theory]
    [InlineData("nameof(n)", "n")]
    [InlineData("(nameof(n))", "n")]
    [InlineData("nameof(n).Length", "1")]
    [InlineData("nameof(n),3", "  n")]
    public void NameofInterpolation_KeepsItsExistingIntrinsicRoute(string hole, string expected)
    {
        var source = $$"""
            module Native Native
              func Probe (int:n) → string
                !
                return "${{{hole}}}"
            """;
        foreach (var candidate in new[] { source, new CalorEmitter().Emit(Parse(source)) })
        {
            Assert.Equal(expected, Invoke(Compile(candidate), "Probe", 12));
            var (ast, bound, diagnostics) = Bind(candidate);
            Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
            var part = InterpolationPart(ast);
            Assert.Equal(hole.EndsWith(",3", StringComparison.Ordinal) ? "3" : null, part.AlignmentClause);
            var operand = part.Expression;
            if (hole == "nameof(n).Length")
            {
                var member = Assert.IsType<FieldAccessNode>(operand);
                Assert.Equal("Length", member.FieldName);
                operand = member.Target;
                var read = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundFieldAccessExpression>());
                Assert.Equal("Length", read.FieldName);
                Assert.Equal("n", Assert.IsType<BoundStringLiteral>(read.Target).Value);
            }
            Assert.Equal("n", Assert.IsType<NameOfExpressionNode>(operand).Name);
            Assert.Equal("n", Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundStringLiteral>()).Value);
            var result = Program.Compile(candidate);
            Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code.StartsWith("Calor04", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("String.Concat(\"\\x41\")", "String.Concat(\"\\u0041\")", "A")]
    [InlineData("String.Concat(\"${x}\")", "String.Concat(\"\\${x}\")", "${x}")]
    [InlineData("String.Concat(\"\\x41\"),4", "String.Concat(\"\\u0041\"),4", "   A")]
    [InlineData("String.Concat(\"${x}\"),8", "String.Concat(\"\\${x}\"),8", "    ${x}")]
    [InlineData("String.Concat(String.Concat(\"\\x41\"))", "String.Concat(String.Concat(\"\\u0041\"))", "A")]
    [InlineData("String.Concat(\"${x}\").Length", "String.Concat(\"\\${x}\").Length", "4")]
    public void NativeCallInterpolation_UnsafeStringOperandsKeepTheirCSharpRuntimeValue(
        string originalHole, string hole, string expected)
    {
        var original = $$"""
            namespace Native
            {
                public static class NativeModule
                {
                    public static string Probe(int x) => $"{{{originalHole}}}";
                }
            }
            """;
        Assert.Equal(expected, Invoke(CompileCSharp(original), "Probe", 7));
        var source = $$"""
            module Native Native
              func Probe (int:x) → string
                !
                return "${{{hole}}}"
            """;
        foreach (var candidate in new[] { source, new CalorEmitter().Emit(Parse(source)) })
        {
            Assert.Equal(expected, Invoke(Compile(candidate, enforceEffects: false), "Probe", 7));
            var (ast, bound, diagnostics) = Bind(candidate);
            Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
            var part = InterpolationPart(ast);
            Assert.Equal(hole.EndsWith(",4", StringComparison.Ordinal) ? "4"
                : hole.EndsWith(",8", StringComparison.Ordinal) ? "8" : null, part.AlignmentClause);
            ExpressionNode operand = part.Expression;
            if (hole.EndsWith(".Length", StringComparison.Ordinal))
            {
                var member = Assert.IsType<FieldAccessNode>(operand);
                Assert.Equal("Length", member.FieldName);
                operand = member.Target;
            }
            var call = Assert.IsType<ExpressionCallNode>(operand);
            var concat = Assert.IsType<FieldAccessNode>(call.TargetExpression);
            Assert.Equal("Concat", concat.FieldName);
            Assert.Equal("String", Assert.IsType<ReferenceNode>(concat.Target).Name);
            var argument = Assert.Single(call.Arguments);
            if (hole.StartsWith("String.Concat(String.Concat", StringComparison.Ordinal))
            {
                var inner = Assert.IsType<ExpressionCallNode>(argument);
                var innerConcat = Assert.IsType<FieldAccessNode>(inner.TargetExpression);
                Assert.Equal("Concat", innerConcat.FieldName);
                Assert.Equal("String", Assert.IsType<ReferenceNode>(innerConcat.Target).Name);
                argument = Assert.Single(inner.Arguments);
            }
            var value = hole.Contains("\\u0041", StringComparison.Ordinal) ? "A" : "${x}";
            Assert.Equal(value, Assert.IsType<StringLiteralNode>(argument).Value);
            var children = BoundNodeHelpers.DescendantsAndSelf(bound).ToArray();
            Assert.Equal(value, Assert.Single(children.OfType<BoundStringLiteral>()).Value);
            Assert.Empty(children.OfType<BoundVariableExpression>());
            var calls = children.OfType<BoundCallExpression>().ToArray();
            Assert.Equal(hole.StartsWith("String.Concat(String.Concat", StringComparison.Ordinal) ? 2 : 1, calls.Length);
            Assert.All(calls, selected =>
            {
                Assert.Equal(SpecialType.System_String, selected.ResolvedMetadataMethod!.ContainingType.SpecialType);
                Assert.Equal("Concat", selected.ResolvedMetadataMethod.Name);
            });
            var result = Program.Compile(candidate);
            Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code.StartsWith("Calor04", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void NativeNamedCallInterpolation_HasExactMetadataAndRealOutput()
    {
        const string source = """
            module Native Native
              func Probe (int:n) → string
                !
                return "v=${Math.Max(val1: n, val2: 3)}"
            """;
        var (_, bound, diagnostics) = Bind(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.Equal("System.Math", call.ResolvedMetadataMethod?.ContainingType.ToDisplayString());
        Assert.Equal("Max", call.ResolvedMetadataMethod?.Name);
        Assert.All(call.ResolvedMetadataMethod!.Parameters, parameter =>
            Assert.Equal(SpecialType.System_Int32, parameter.Type.SpecialType));
        var type = Compile(source);
        Assert.Equal("v=3", Invoke(type, "Probe", 1));
        Assert.Equal("v=5", Invoke(type, "Probe", 5));
    }

    private static string RuntimeSource(string body, string type = "int", bool zero = false) => $$"""
        module Native Native
          func Probe (List<int>:trace, List<int>[]:items{{(zero ? ", int:zero" : "")}}) → {{type}}
            ! mut
        {{string.Join("\n", body.Split('\n').Select(line => "    " + line))}}
          func GetLists private (List<int>:trace, List<int>[]:items) → List<int>[]
            trace.Add(1)
            return items
          func Index private (List<int>:trace) → int
            trace.Add(2)
            return 0
          func Number private (List<int>:trace, int:value) → int
            trace.Add(value)
            return value
        """;

    private static string Source(string expression, bool statement = false, string type = "int") => $$"""
        module Native Native
          func Probe () → {{type}}
            !
            {{(statement ? "" : "return ")}}{{expression}}
        """;

    private static string Slice(string expression, int start, TextSpan span) =>
        expression.Substring(span.Start - start, span.Length);

    private static ExpressionNode Expression(string expression) =>
        Assert.IsType<ReturnStatementNode>(Assert.Single(Assert.Single(Parse(Source(expression)).Functions).Body)).Expression!;

    private static ModuleNode Parse(string source, DiagnosticBag? diagnostics = null)
    {
        var requireSuccess = diagnostics is null;
        diagnostics ??= new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        if (requireSuccess)
            Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        return module;
    }

    private static (ModuleNode Ast, BoundModule Bound, DiagnosticBag Diagnostics) Bind(string source)
    {
        var diagnostics = new DiagnosticBag();
        var ast = Parse(source, diagnostics);
        return (ast, new Calor.Compiler.Binding.Binder(diagnostics).Bind(ast), diagnostics);
    }

    private static object? Invoke(Type type, string name, params object?[] arguments) =>
        type.GetMethod(name)!.Invoke(null, arguments);

    private static InterpolatedStringExpressionNode InterpolationPart(ModuleNode ast)
    {
        var interpolation = Assert.IsType<InterpolatedStringNode>(Assert.IsType<ReturnStatementNode>(
            Assert.Single(Assert.Single(ast.Functions).Body)).Expression);
        return Assert.IsType<InterpolatedStringExpressionNode>(Assert.Single(interpolation.Parts));
    }

    private static Type Compile(string source, bool enforceEffects = true)
    {
        var result = Program.Compile(source, "native-postfix.calr", new CompilationOptions
            { EnableTypeChecking = true, EnforceEffects = enforceEffects, StatusWriter = TextWriter.Null });
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        return CompileCSharp(result.GeneratedCode);
    }

    private static Type CompileCSharp(string code)
    {
        var compilation = CSharpCompilation.Create("Postfix_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble + code)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emission = compilation.Emit(stream);
        Assert.True(emission.Success, string.Join("\n", emission.Diagnostics) + "\n" + code);
        return Assembly.Load(stream.ToArray()).GetType("Native.NativeModule", throwOnError: true)!;
    }
}
