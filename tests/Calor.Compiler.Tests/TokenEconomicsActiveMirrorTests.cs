using System.Globalization;
using System.Reflection;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Calor.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Calor.Compiler.Tests;

[Collection("ProductionPipelineConsole")]
public sealed class TokenEconomicsActiveMirrorTests
{
    private const string Prefix = "tests/TestData/Benchmarks/TokenEconomics/";
    private static readonly HashSet<string> InvalidRuntimeInputs =
    [
        "AreaOfCircle", "Average", "CelsiusToKelvin", "CompoundInterest",
        "TemperatureConverter", "TemperatureRange", "SumRange", "Power"
    ];

    public static IEnumerable<object[]> Inputs() =>
        ActiveSyntaxInputs.OriginalPaths.Where(path => path.StartsWith(Prefix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).Select(path => new object[] { path });

    public static IEnumerable<object[]> RuntimeInputs() =>
        Inputs().Where(row => !InvalidRuntimeInputs.Contains(Path.GetFileNameWithoutExtension((string)row[0])));

    [Fact]
    public void Mirrors_RetainAll52OriginalManifestSubjectsAndExplicitRuntimeBoundaries()
    {
        var originals = Inputs().Select(row => (string)row[0]).ToArray();
        Assert.Equal(52, originals.Length);
        var manifest = BulkBenchmarkCompilationTests.AllBenchmarks()
            .Where(row => (string)row[2] == "TokenEconomics")
            .Select(row => "tests/TestData/Benchmarks/" + (string)row[3]).Order(StringComparer.Ordinal);
        Assert.Equal(manifest, originals);
        Assert.Equal(44, RuntimeInputs().Count());
        Assert.All(InvalidRuntimeInputs, name => Assert.Contains(Prefix + name + ".calr", originals));
        var power = FrozenSource(Prefix + "Power.calr");
        Assert.Contains("int base, int exp", power);
        Assert.Contains("result = result * base;", power);
        Assert.Contains(CSharpSyntaxTree.ParseText(power).GetDiagnostics(),
            diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
    }

    [Theory]
    [MemberData(nameof(Inputs))]
    public void NativeMirror_PreservesFrozenProgramStructureUnderCurrentCompilerPolicies(string original)
    {
        var module = Parse(ActiveSource(original));
        var frozen = FrozenSource(original);
        if (original == Prefix + "Power.calr")
        {
            // This frozen golden predates reserved-identifier escaping; do not rewrite it on disk.
            frozen = frozen.Replace("int base, int exp", "int @base, int exp", StringComparison.Ordinal)
                .Replace("result = result * base;", "result = result * @base;", StringComparison.Ordinal);
        }
        var baseline = Tree(frozen);
        var actual = Tree(new CSharpEmitter(ContractMode.Off).Emit(module));
        Assert.Equal(Topology(baseline), Topology(actual));
        var expectedMethods = Methods(baseline);
        var actualMethods = Methods(actual);
        Assert.Equal(expectedMethods.Length, actualMethods.Length);
        var normalizer = new ProgramBodyNormalizer();
        foreach (var (expected, observed) in expectedMethods.Zip(actualMethods))
        {
            var difference = FirstDifference(normalizer.Visit(expected)!, normalizer.Visit(observed)!);
            Assert.True(difference.Length == 0,
                $"{original}/{expected.Identifier.ValueText}\nFrozen:\n{normalizer.Visit(expected)}"
                + $"\nNative:\n{normalizer.Visit(observed)}\n"
                + difference);
        }

        var current = Tree(new CSharpEmitter(ContractMode.Debug).Emit(module));
        AssertContractsEquivalent(baseline, current);
        var owners = module.Functions.SelectMany(function =>
                ParsedContracts(function.Id, function.Preconditions, function.Postconditions))
            .Concat(module.Classes.SelectMany(type => type.Methods).SelectMany(method =>
                ParsedContracts(method.Id, method.Preconditions, method.Postconditions))).ToArray();
        var emitted = Contracts(current);
        Assert.Equal(owners.Length, emitted.Length);
        foreach (var (owner, creation) in owners.Zip(emitted))
        {
            Assert.Equal(owner.Id, LiteralText(creation.ArgumentList!.Arguments[1].Expression));
            Assert.Equal("Calor.Runtime.ContractKind." + owner.Kind,
                creation.ArgumentList.Arguments[2].Expression.ToString());
            foreach (var (field, value) in new[]
            {
                ("startOffset", owner.Span.Start), ("length", owner.Span.Length),
                ("line", owner.Span.Line), ("column", owner.Span.Column)
            })
                Assert.Equal(value.ToString(CultureInfo.InvariantCulture),
                    Assert.Single(creation.ArgumentList.Arguments,
                        argument => argument.NameColon?.Name.Identifier.ValueText == field).Expression.ToString());
        }
    }

    private static void AssertContractsEquivalent(SyntaxNode baseline, SyntaxNode actual)
    {
        var expectedContracts = Contracts(baseline);
        var currentContracts = Contracts(actual);
        Assert.Equal(expectedContracts.Length, currentContracts.Length);
        foreach (var (expected, observed) in expectedContracts.Zip(currentContracts))
        {
            var oldArguments = expected.ArgumentList!.Arguments;
            var newArguments = observed.ArgumentList!.Arguments;
            Assert.True(SyntaxFactory.AreEquivalent(oldArguments[2], newArguments[2]));
            var oldResult = ResultStorage(expected.Ancestors().OfType<MethodDeclarationSyntax>().First());
            var newResult = ResultStorage(observed.Ancestors().OfType<MethodDeclarationSyntax>().First());
            AssertConditionEquivalent(ArgumentText(expected, "condition"), ArgumentText(observed, "condition"),
                oldResult, newResult);
            var oldMessage = LiteralText(oldArguments[0].Expression);
            var newMessage = LiteralText(newArguments[0].Expression);
            var prefix = oldMessage.StartsWith("Precondition failed: ", StringComparison.Ordinal)
                ? "Precondition failed: "
                : oldMessage.StartsWith("Postcondition failed: ", StringComparison.Ordinal)
                    ? "Postcondition failed: " : "";
            if (prefix.Length == 0)
                Assert.Equal(oldMessage, newMessage);
            else
            {
                Assert.StartsWith(prefix, newMessage);
                AssertConditionEquivalent(oldMessage[prefix.Length..], newMessage[prefix.Length..], oldResult, newResult);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Inputs))]
    public void NativeMirror_RoundTripsOrRetainsTheExplicitCustomMessageBoundary(string original)
    {
        var module = Parse(ActiveSource(original));
        var emitter = new CalorEmitter();
        if (original == Prefix + "ContractedDivide.calr")
        {
            Assert.Contains("requires{\"divisor must not be zero\"}", ActiveSource(original));
            Assert.Equal("Canonical precondition message syntax has not been selected.",
                Assert.Throws<InvalidOperationException>(() => emitter.Emit(module)).Message);
            return;
        }

        var reemitted = Parse(emitter.Emit(module));
        var normalizer = new ProgramBodyNormalizer();
        Assert.True(SyntaxFactory.AreEquivalent(
            normalizer.Visit(Tree(new CSharpEmitter(ContractMode.Off).Emit(module))),
            normalizer.Visit(Tree(new CSharpEmitter(ContractMode.Off).Emit(reemitted)))),
            $"Native emission changed {original}\nBefore:\n{new CSharpEmitter(ContractMode.Off).Emit(module)}"
            + $"\nAfter:\n{new CSharpEmitter(ContractMode.Off).Emit(reemitted)}");
        AssertContractsEquivalent(Tree(new CSharpEmitter().Emit(module)), Tree(new CSharpEmitter().Emit(reemitted)));
    }

    [Theory]
    [MemberData(nameof(RuntimeInputs))]
    public void NativeMirror_MatchesExecutableFrozenGoldenOnNonOverflowSamples(string original)
    {
        var native = Program.Compile(ActiveSource(original), original,
            new CompilationOptions { ContractMode = ContractMode.Debug, EnforceEffects = false });
        Assert.False(native.HasErrors, string.Join("\n", native.Diagnostics.Errors));
        var frozen = FrozenSource(original);
        var oldMethods = Methods(Tree(frozen)).ToDictionary(method => method.Identifier.ValueText);
        var newMethods = Methods(Tree(native.GeneratedCode)).ToDictionary(method => method.Identifier.ValueText);
        var expected = Compile(frozen);
        var actual = Compile(native.GeneratedCode);
        foreach (var type in expected.GetTypes())
        {
            var currentType = actual.GetType(type.FullName!, throwOnError: true)!;
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                var current = currentType.GetMethod(method.Name, method.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
                Assert.NotNull(current);
                foreach (var arguments in Arguments(method.GetParameters()))
                {
                    var baseline = Observe(method, arguments, ResultStorage(oldMethods[method.Name]));
                    var observed = Observe(current, arguments, ResultStorage(newMethods[method.Name]));
                    Assert.True(baseline == observed,
                        $"{original}/{method.Name}({string.Join(", ", arguments.Select(value => value ?? "null"))})\n"
                        + $"Frozen: {baseline}\nNative: {observed}");
                }
            }
        }
    }

    [Fact]
    public void BodyProjection_RetainsOperatorGroupingOperandsAndControlFlow()
    {
        var normalizer = new ProgramBodyNormalizer();
        SyntaxNode? Normalize(string expression) => normalizer.Visit(SyntaxFactory.ParseExpression(expression));
        Assert.True(SyntaxFactory.AreEquivalent(Normalize("(a + b) * c"), Normalize("((a + b)) * (c)")));
        Assert.False(SyntaxFactory.AreEquivalent(Normalize("(a + b) * c"), Normalize("a + b * c")));
        Assert.False(SyntaxFactory.AreEquivalent(Normalize("a - b"), Normalize("b - a")));
        Assert.False(SyntaxFactory.AreEquivalent(
            normalizer.Visit(SyntaxFactory.ParseStatement("if (a) return b;")),
            normalizer.Visit(SyntaxFactory.ParseStatement("if (b) return a;"))));
        Assert.Empty(FirstDifference(Normalize("(a + b) * c")!, Normalize("((a + b)) * (c)")!));
        Assert.NotEmpty(FirstDifference(Normalize("(a + b) * c")!, Normalize("a + b * c")!));
        Assert.NotEmpty(FirstDifference(Normalize("a - b")!, Normalize("b - a")!));
    }

    [Fact]
    public void CurrentCheckedPolicy_RemainsExplicitRatherThanChangingTheMirrorToMatchHistoricalOverflow()
    {
        const string original = Prefix + "ExpressionBodied.calr";
        var oldMethod = Compile(FrozenSource(original)).GetTypes().SelectMany(type => type.GetMethods())
            .Single(method => method.Name == "Double");
        var compiled = Program.Compile(ActiveSource(original), original,
            new CompilationOptions { EnforceEffects = false });
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics.Errors));
        var currentMethod = Compile(compiled.GeneratedCode).GetTypes().SelectMany(type => type.GetMethods())
            .Single(method => method.Name == "Double");
        Assert.Equal(-2, oldMethod.Invoke(null, [int.MaxValue]));
        var failure = Assert.Throws<TargetInvocationException>(() => currentMethod.Invoke(null, [int.MaxValue]));
        Assert.IsType<OverflowException>(failure.InnerException);
    }

    [Theory]
    [InlineData(2, 0, 1)]
    [InlineData(2, 5, 32)]
    [InlineData(-2, 3, -8)]
    [InlineData(3, 4, 81)]
    [InlineData(0, 2, 0)]
    public void Power_RetainsTheOriginalIterativeProgramDespiteItsInvalidFrozenIdentifier(
        int value, int exponent, int expected)
    {
        var result = Program.Compile(ActiveSource(Prefix + "Power.calr"), Prefix + "Power.calr",
            new CompilationOptions { EnforceEffects = false });
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Errors));
        var method = Compile(result.GeneratedCode).GetType("MathPower.MathPowerModule")!.GetMethod("Power")!;
        Assert.Equal(expected, method.Invoke(null, [value, exponent]));
    }

    private static string ActiveSource(string original) => File.ReadAllText(
        ActiveSyntaxInputs.ForCurrentCompiler(CliTestHarness.FindRepoRoot(), original));

    private static string FrozenSource(string original) => File.ReadAllText(
        Path.Combine(CliTestHarness.FindRepoRoot(), Path.ChangeExtension(original, ".g.cs")));

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors));
        Assert.Empty(module.InteropBlocks);
        Assert.All(module.Classes, type => Assert.Empty(type.InteropBlocks));
        return module;
    }

    private static SyntaxNode Tree(string source) => CSharpSyntaxTree.ParseText(source).GetRoot();

    private static string FirstDifference(SyntaxNodeOrToken left, SyntaxNodeOrToken right)
    {
        if (left.RawKind != right.RawKind || left.IsMissing != right.IsMissing
            || left.IsToken && (left.AsToken().Text != right.AsToken().Text
                || left.AsToken().ValueText != right.AsToken().ValueText
                || !Equals(left.AsToken().Value, right.AsToken().Value)))
            return $"{left.Kind()}({left}) != {right.Kind()}({right})";
        var a = left.ChildNodesAndTokens();
        var b = right.ChildNodesAndTokens();
        if (a.Count != b.Count)
            return $"{left.Kind()}: {a.Count} children != {b.Count}";
        for (var index = 0; index < a.Count; index++)
        {
            var difference = FirstDifference(a[index], b[index]);
            if (difference.Length > 0)
                return left.Kind() + "/" + difference;
        }
        return "";
    }

    private static string[] Topology(SyntaxNode root) => root.DescendantNodes()
        .OfType<ClassDeclarationSyntax>().Select(type =>
            $"{Assert.IsAssignableFrom<BaseNamespaceDeclarationSyntax>(type.Parent).Name}.{type.Identifier.ValueText}"
            + ":" + string.Join(" ", type.Modifiers.Select(token => token.ValueText)))
        .ToArray();

    private static MethodDeclarationSyntax[] Methods(SyntaxNode root) =>
        root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();

    private static ObjectCreationExpressionSyntax[] Contracts(SyntaxNode root) =>
        root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
            .Where(node => node.Type.ToString() == "Calor.Runtime.ContractViolationException").ToArray();

    private static IEnumerable<(string Id, string Kind, TextSpan Span)> ParsedContracts(
        string id, IReadOnlyList<RequiresNode> preconditions, IReadOnlyList<EnsuresNode> postconditions) =>
        preconditions.Select(contract => (id, "Requires", contract.Span))
            .Concat(postconditions.Select(contract => (id, "Ensures", contract.Span)));

    private static string LiteralText(ExpressionSyntax expression) =>
        Assert.IsType<LiteralExpressionSyntax>(expression).Token.ValueText;

    private static string ArgumentText(ObjectCreationExpressionSyntax creation, string name) =>
        LiteralText(Assert.Single(creation.ArgumentList!.Arguments,
            argument => argument.NameColon?.Name.Identifier.ValueText == name).Expression);

    private static void AssertConditionEquivalent(string expected, string actual,
        string? expectedResult = null, string? actualResult = null)
    {
        Assert.True(SyntaxFactory.AreEquivalent(
            new ExpressionNormalizer(expectedResult).Visit(SyntaxFactory.ParseExpression(expected)),
            new ExpressionNormalizer(actualResult).Visit(SyntaxFactory.ParseExpression(actual))),
            $"Contract expression changed: {expected} -> {actual}");
    }

    private static string? ResultStorage(MethodDeclarationSyntax method)
    {
        if (method.Body?.Statements.LastOrDefault() is not ReturnStatementSyntax
            { Expression: IdentifierNameSyntax result }
            || result.Identifier.ValueText != "__result__"
                && !result.Identifier.ValueText.StartsWith("__calorPostconditionResult", StringComparison.Ordinal))
            return null;
        var name = result.Identifier.ValueText;
        Assert.Single(method.Body.Statements.OfType<LocalDeclarationStatementSyntax>()
            .SelectMany(statement => statement.Declaration.Variables),
            variable => variable.Identifier.ValueText == name);
        return name;
    }

    private static Assembly Compile(string source)
    {
        var compilation = CSharpCompilation.Create("TokenMirror_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)], GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }

    private static IEnumerable<object?[]> Arguments(ParameterInfo[] parameters)
    {
        IEnumerable<object?[]> rows = [Array.Empty<object?>()];
        foreach (var parameter in parameters)
        {
            object?[] values = parameter.ParameterType == typeof(int) ? [-1, 0, 1, 2, 10]
                : parameter.ParameterType == typeof(float) ? [-1f, 0f, 1f, 2f, 10f]
                : parameter.ParameterType == typeof(bool) ? [false, true]
                : parameter.ParameterType == typeof(string) ? [null, "", "a", "A", "e", "i", "o", "u", "abc"]
                : throw new InvalidOperationException($"Unregistered runtime parameter type: {parameter.ParameterType}");
            rows = rows.SelectMany(row => values.Select(value => row.Append(value).ToArray()));
        }
        return rows;
    }

    private sealed record Observation(object? Result, string Output, string? ExceptionType, string? Message,
        ContractKind? Kind, string? Condition);

    private static Observation Observe(MethodInfo method, object?[] arguments, string? resultStorage)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        var previous = Console.Out;
        try
        {
            Console.SetOut(output);
            try
            {
                return new Observation(method.Invoke(null, arguments), output.ToString(), null, null, null, null);
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                var failure = exception.InnerException;
                var contract = failure as ContractViolationException;
                var condition = contract?.Condition;
                var message = failure.Message;
                if (contract?.Kind == ContractKind.Ensures && condition is not null && resultStorage is not null)
                {
                    var normalized = new ExpressionNormalizer(resultStorage)
                        .Visit(SyntaxFactory.ParseExpression(condition))!.NormalizeWhitespace().ToFullString();
                    if (message == "Postcondition failed: " + condition)
                        message = "Postcondition failed: " + normalized;
                    condition = normalized;
                }
                return new Observation(null, output.ToString(), failure.GetType().FullName, message,
                    contract?.Kind, condition);
            }
        }
        finally
        {
            Console.SetOut(previous);
        }
    }

    private class ExpressionNormalizer(string? resultStorage = null) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitParenthesizedExpression(ParenthesizedExpressionSyntax node) => Visit(node.Expression);

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node) =>
            node.Identifier.ValueText == resultStorage ? SyntaxFactory.IdentifierName("result") : base.VisitIdentifierName(node);
    }

    private sealed class ProgramBodyNormalizer : ExpressionNormalizer
    {
        // Historical goldens predate today's checked arithmetic and counted-loop lowering.
        public override SyntaxNode? VisitCheckedExpression(CheckedExpressionSyntax node) => Visit(node.Expression);

        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            if (node.Expression is IdentifierNameSyntax { Identifier.ValueText: "System" }
                && node.Name.Identifier.ValueText == "Console")
                return SyntaxFactory.IdentifierName("Console");
            return base.VisitMemberAccessExpression(node);
        }

        public override SyntaxNode? VisitForStatement(ForStatementSyntax node)
        {
            var variable = Assert.Single(node.Declaration!.Variables);
            var condition = Assert.IsType<BinaryExpressionSyntax>(node.Condition);
            Assert.True(condition.IsKind(SyntaxKind.LessThanOrEqualExpression));
            Assert.Equal(variable.Identifier.ValueText, Assert.IsType<IdentifierNameSyntax>(condition.Left).Identifier.ValueText);
            var update = Assert.Single(node.Incrementors);
            var step = update switch
            {
                PostfixUnaryExpressionSyntax increment when increment.IsKind(SyntaxKind.PostIncrementExpression)
                    && increment.Operand.ToString() == variable.Identifier.ValueText => "1",
                AssignmentExpressionSyntax assignment when assignment.IsKind(SyntaxKind.AddAssignmentExpression)
                    && assignment.Left.ToString() == variable.Identifier.ValueText => assignment.Right.ToString(),
                _ => throw new InvalidOperationException($"Unexpected frozen counted-loop update: {update}")
            };
            var source = $$"""
                module CountedLoopProjection
                  func Probe () → void
                    for {{variable.Identifier.ValueText}} from {{variable.Initializer!.Value}} to {{condition.Right}} step {{step}}
                      OriginalLoopBody()
                """;
            var template = Methods(Tree(new CSharpEmitter(ContractMode.Off).Emit(Parse(source)))).Single();
            var scaffold = Assert.IsType<BlockSyntax>(Assert.Single(template.Body!.Statements));
            var loop = Assert.IsType<WhileStatementSyntax>(scaffold.Statements.Last());
            var body = Assert.IsType<BlockSyntax>(loop.Statement);
            Assert.Equal("OriginalLoopBody();", body.Statements.Last().ToString());
            var originalBody = Assert.IsType<BlockSyntax>(node.Statement);
            var projected = loop.WithStatement(body.WithStatements(body.Statements.RemoveAt(body.Statements.Count - 1)
                .AddRange(originalBody.Statements)));
            return Visit(scaffold.WithStatements(scaffold.Statements.Replace(loop, projected)));
        }

        public override SyntaxNode? VisitIfStatement(IfStatementSyntax node) =>
            node.Statement is ThrowStatementSyntax { Expression: ObjectCreationExpressionSyntax creation }
                && creation.Type.ToString() == "Calor.Runtime.ContractViolationException"
                ? null : base.VisitIfStatement(node);

        public override SyntaxNode? VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node) =>
            node.Declaration.Variables is [{ Identifier.ValueText: "__result__" }]
                ? null : base.VisitLocalDeclarationStatement(node);

        public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax node) =>
            node.Expression is AssignmentExpressionSyntax { Left: IdentifierNameSyntax { Identifier.ValueText: "__result__" } } assignment
                ? SyntaxFactory.ReturnStatement((ExpressionSyntax)Visit(assignment.Right)!)
                : base.VisitExpressionStatement(node);

        public override SyntaxNode? VisitReturnStatement(ReturnStatementSyntax node) =>
            node.Expression is IdentifierNameSyntax { Identifier.ValueText: "__result__" }
                ? null : base.VisitReturnStatement(node);

        public override SyntaxNode? VisitElseClause(ElseClauseSyntax node)
        {
            var visited = (ElseClauseSyntax)base.VisitElseClause(node)!;
            return visited.Statement is BlockSyntax { Statements: [IfStatementSyntax nested] }
                ? visited.WithStatement(nested) : visited;
        }
    }
}
