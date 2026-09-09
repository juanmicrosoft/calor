using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Calor.Enforcement.Tests;
using FsCheck;
using FsCheck.Xunit;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Calor.Compiler.Tests.PropertyTests;

[CollectionDefinition("ProductionPipelineConsole", DisableParallelization = true)]
public sealed class ProductionPipelineConsoleCollection;

[Collection("ProductionPipelineConsole")]
public sealed class GeneratedProductionPipelinePropertyTests(ITestOutputHelper output)
{
    public sealed record Term(string Kind, int Value = 0, int Tag = 0,
        Term? Left = null, Term? Right = null, Term? Third = null);

    public sealed record Sample(string Mode, Term Expression, Term Alternative, int Count, int Printed);

    private sealed record Observation(int? Value, Type? ExceptionType, string Output, int[] Trace);

    public static Arbitrary<Sample> Programs()
    {
        var literal = Gen.OneOf(Gen.Choose(-6, 6), Gen.Elements(int.MinValue, int.MaxValue, 0, 1, -1));
        Gen<Term> Grow(int depth)
        {
            var leaf = literal.Select(value => new Term("literal", value));
            if (depth == 0)
                return leaf;
            var child = Grow(depth - 1);
            return Gen.OneOf(leaf,
                from kind in Gen.Elements("+", "-", "*", "/", "and", "or")
                from left in child from right in child
                select new Term(kind, Left: left, Right: right),
                from condition in child from yes in child from no in child
                select new Term("conditional", Left: condition, Right: yes, Third: no));
        }

        return Arb.From(
            from mode in Gen.Elements("expression", "loop", "match")
            from expression in Grow(2)
            from alternative in Grow(2)
            from count in Gen.Choose(0, 3)
            from printed in Gen.Choose(-1000, 1000)
            select NumberLeaves(new Sample(mode, expression, alternative, count, printed)));
    }

    private static Sample NumberLeaves(Sample sample)
    {
        var tag = 0;
        Term Number(Term term) => term.Kind == "literal"
            ? term with { Tag = ++tag }
            : term with
            {
                Left = term.Left == null ? null : Number(term.Left),
                Right = term.Right == null ? null : Number(term.Right),
                Third = term.Third == null ? null : Number(term.Third)
            };
        return sample with { Expression = Number(sample.Expression), Alternative = Number(sample.Alternative) };
    }

    [Property(MaxTest = 60)]
    public Property GeneratedPrograms_PreserveStructureAndObservableSemantics() =>
        Prop.ForAll(Programs(), sample =>
        {
            CheckSample(sample);
            return true;
        });

    [Fact]
    public void ValidGenerator_ReportsEveryAcceptedAndRejectedSample()
    {
        var samples = Gen.Sample(10, 60, Programs().Generator).ToArray();
        var accepted = 0;
        var rejected = 0;
        foreach (var sample in samples)
        {
            var (_, diagnostics) = Parse(Render(sample));
            if (diagnostics.HasErrors)
                rejected++;
            else
                accepted++;
        }
        output.WriteLine($"Well-typed generator: accepted={accepted}, rejected={rejected}, total={samples.Length}");
        Assert.Equal(60, accepted);
        Assert.Equal(0, rejected);
    }

    [Theory]
    [InlineData("+", int.MaxValue, 1, typeof(OverflowException))]
    [InlineData("-", int.MinValue, 1, typeof(OverflowException))]
    [InlineData("*", int.MaxValue, 2, typeof(OverflowException))]
    [InlineData("/", 1, 0, typeof(DivideByZeroException))]
    [InlineData("/", int.MinValue, -1, typeof(OverflowException))]
    [InlineData("+", 7, -3, null)]
    public void ArithmeticBoundaryControls_ExerciseProductionExceptions(
        string operation, int left, int right, Type? expectedError)
    {
        var sample = NumberLeaves(new Sample("expression",
            new Term(operation, Left: new Term("literal", left), Right: new Term("literal", right)),
            new Term("literal"), 0, 42));
        Assert.Equal(expectedError, Evaluate(sample).ExceptionType);
        CheckSample(sample);
    }

    [Theory]
    [InlineData("and", 0)]
    [InlineData("or", 1)]
    public void ShortCircuitControls_DoNotEvaluateThrowingRightOperand(string operation, int left)
    {
        var sample = NumberLeaves(new Sample("expression",
            new Term(operation, Left: new Term("literal", left),
                Right: new Term("/", Left: new Term("literal", 1), Right: new Term("literal", 0))),
            new Term("literal"), 0, 42));
        var expected = Evaluate(sample);
        Assert.Null(expected.ExceptionType);
        Assert.Single(expected.Trace);
        CheckSample(sample);
    }

    [Theory]
    [InlineData("print")]
    [InlineData("operator")]
    [InlineData("operand-order")]
    public void BehavioralAndStructuralOracles_RejectSameShapeMutants(string mutation)
    {
        var sample = NumberLeaves(new Sample("expression",
            new Term("-", Left: new Term("literal", 8), Right: new Term("literal", 3)),
            new Term("literal"), 0, 42));
        var source = Render(sample);
        var mutant = mutation switch
        {
            "print" => source.Replace("§P 42", "§P 0", StringComparison.Ordinal),
            "operator" => source.Replace("§R (- ", "§R (+ ", StringComparison.Ordinal),
            "operand-order" => source.Replace(RenderTerm(sample.Expression),
                RenderTerm(sample.Expression with
                {
                    Left = sample.Expression.Right,
                    Right = sample.Expression.Left
                }), StringComparison.Ordinal),
            _ => throw new NotSupportedException(mutation)
        };
        Assert.NotEqual(source, mutant);
        var (originalAst, _) = Parse(source);
        var (mutatedAst, _) = Parse(mutant);
        Assert.Equal(originalAst.Name, mutatedAst.Name);
        Assert.Equal(originalAst.Functions.Count, mutatedAst.Functions.Count);
        Assert.Equal(originalAst.Functions.Select(function => function.Body.Count),
            mutatedAst.Functions.Select(function => function.Body.Count));
        Assert.NotEqual(SemanticTree(originalAst), SemanticTree(mutatedAst));
        Assert.False(Program.Compile(mutant, "mutant.calr", Options()).HasErrors);
        Assert.ThrowsAny<XunitException>(() => CheckSample(sample, mutant));
    }

    [Theory]
    [InlineData("loop")]
    [InlineData("match")]
    [InlineData("conditional")]
    public void ControlFlowControls_RejectChangedIterationOrBranchSemantics(string construct)
    {
        var expression = construct == "conditional"
            ? new Term("conditional", Left: new Term("literal", 1),
                Right: new Term("literal", 8), Third: new Term("literal", 3))
            : new Term("literal", 8);
        var sample = NumberLeaves(new Sample(construct == "conditional" ? "expression" : construct,
            expression, new Term("literal", 3), construct == "loop" ? 3 : 0, 42));
        CheckSample(sample);
        var source = Render(sample);
        var mutant = construct switch
        {
            "loop" => source.Replace("§L{loop:i:1:3:1}", "§L{loop:i:0:3:1}", StringComparison.Ordinal),
            "match" => source.Replace("§W{match} 0", "§W{match} 1", StringComparison.Ordinal),
            "conditional" => source.Replace(RenderTerm(sample.Expression),
                RenderTerm(sample.Expression with
                {
                    Right = sample.Expression.Third,
                    Third = sample.Expression.Right
                }), StringComparison.Ordinal),
            _ => throw new NotSupportedException(construct)
        };
        Assert.NotEqual(source, mutant);
        Assert.False(Program.Compile(mutant, "mutant.calr", Options()).HasErrors);
        Assert.ThrowsAny<XunitException>(() => CheckSample(sample, mutant));
    }

    [Fact]
    public void TraceOracle_RejectsOrderOnlyMutationWithIdenticalOtherObservations()
    {
        var sample = NumberLeaves(new Sample("expression",
            new Term("+", Left: new Term("literal", 8), Right: new Term("literal", 3)),
            new Term("literal"), 0, 42));
        CheckSample(sample);
        var mutant = sample with
        {
            Expression = sample.Expression with
            {
                Left = sample.Expression.Right,
                Right = sample.Expression.Left
            }
        };
        var expected = Evaluate(sample);
        var actual = Execute(Render(mutant));
        Assert.Equal(expected.Value, actual.Value);
        Assert.Equal(expected.ExceptionType, actual.ExceptionType);
        Assert.Equal(expected.Output, actual.Output);
        Assert.Equal(new[] { 1, 2 }, expected.Trace);
        Assert.Equal(new[] { 2, 1 }, actual.Trace);
        Assert.Throws<EqualException>(() => AssertObservationsEqual(expected, actual));
    }

    [Fact]
    public void NestedOperandControl_PreservesLeftConditionalBeforeRightArithmetic()
    {
        var sample = NumberLeaves(new Sample("match",
            new Term("-",
                Left: new Term("or", Left: new Term("literal", 4), Right: new Term("literal", -1)),
                Right: new Term("or", Left: new Term("literal", -1), Right: new Term("literal", 6))),
            new Term("/",
                Left: new Term("conditional", Left: new Term("literal", 2),
                    Right: new Term("literal", 1), Third: new Term("literal", 6)),
                Right: new Term("+", Left: new Term("literal", 0), Right: new Term("literal", 4))),
            2, 601));
        var expected = Evaluate(sample);
        Assert.Equal(0, expected.Value);
        Assert.Null(expected.ExceptionType);
        Assert.Equal(new[] { 5, 6, 8, 9 }, expected.Trace);
        CheckSample(sample);
    }

    [Fact]
    public void LeftExceptionControl_SuppressesRightOperandEffects()
    {
        var sample = NumberLeaves(new Sample("loop",
            new Term("-",
                Left: new Term("/", Left: new Term("literal", int.MinValue),
                    Right: new Term("literal", -1)),
                Right: new Term("*", Left: new Term("literal", 0),
                    Right: new Term("literal", int.MinValue))),
            new Term("literal"), 3, 830));
        var expected = Evaluate(sample);
        Assert.Equal(typeof(OverflowException), expected.ExceptionType);
        Assert.Equal(new[] { 1, 2 }, expected.Trace);
        CheckSample(sample);
    }

    [Fact]
    public void EffectsControl_RejectsUndeclaredObservableMutation()
    {
        var source = Render(NumberLeaves(new Sample("expression", new Term("literal", 1),
            new Term("literal"), 0, 42)));
        var result = Program.Compile(source.Replace("§E{mut}", "§E{}", StringComparison.Ordinal),
            "missing-effect.calr", Options());
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Fact]
    public async Task MalformedGenerator_ReportsRejectionsWithBoundedTermination()
    {
        var malformed = Gen.Elements(
            "§M{m:Bad}\n  §NO_SUCH_KEYWORD\n",
            "§M{m:Bad}\n  §F{f:Probe:pub} () -> i32\n    §R (+ 1)\n",
            "§M{m:Bad}\n  §F{f:Probe:pub} () -> str\n    §R \"unterminated\n",
            "§M{m:Bad}\n  §/M{m}\n");
        var rejected = 0;
        foreach (var source in Gen.Sample(4, 32, malformed))
        {
            Assert.True(source.Length < 200);
            var (_, diagnostics) = await Task.Run(() => Parse(source)).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(diagnostics.HasErrors, source);
            rejected++;
        }
        output.WriteLine($"Malformed generator: accepted=0, rejected={rejected}, total=32");
        Assert.Equal(32, rejected);
    }

    [Fact]
    public void UnsupportedGeneratorConstructs_AreExplicitlyRejected()
    {
        foreach (var mode in new[] { "generic", "async", "unbounded-loop", "raw-csharp" })
        {
            var sample = new Sample(mode, new Term("literal"), new Term("literal"), 0, 0);
            Assert.Throws<NotSupportedException>(() => Render(sample));
            Assert.Throws<NotSupportedException>(() => Evaluate(sample));
        }
    }

    [Fact]
    public void SupportLedger_ReferencesExistingPositiveAndNegativeTests()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "eng", "production-pipeline-support.json")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        using var ledger = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(directory.FullName, "eng", "production-pipeline-support.json")));
        var constructs = ledger.RootElement.GetProperty("constructs").EnumerateArray().ToArray();
        Assert.Equal(8, constructs.Length);
        foreach (var construct in constructs)
        {
            foreach (var stage in construct.GetProperty("stages").EnumerateArray())
            {
                foreach (var role in new[] { "positive", "negative" })
                {
                    var name = stage.GetProperty(role).GetString();
                    var method = typeof(GeneratedProductionPipelinePropertyTests).GetMethod(name!);
                    Assert.NotNull(method);
                    Assert.Contains(method.GetCustomAttributes(), attribute => attribute is FactAttribute);
                }
            }
            Assert.False(string.IsNullOrWhiteSpace(construct.GetProperty("fallback").GetString()));
        }
    }

    private static void CheckSample(Sample sample, string? sourceOverride = null)
    {
        var source = sourceOverride ?? Render(sample);
        var (first, firstDiagnostics) = Parse(source);
        Assert.False(firstDiagnostics.HasErrors, string.Join("; ", firstDiagnostics.Errors));
        var pretty = new CalorEmitter().Emit(first);
        var (second, secondDiagnostics) = Parse(pretty);
        Assert.False(secondDiagnostics.HasErrors, string.Join("; ", secondDiagnostics.Errors));
        Assert.Equal(SemanticTree(first), SemanticTree(second));
        var expected = Evaluate(sample);
        foreach (var executable in new[] { source, pretty })
            AssertObservationsEqual(expected, Execute(executable));
    }

    private static void AssertObservationsEqual(Observation expected, Observation actual)
    {
        Assert.Equal(expected.Value, actual.Value);
        Assert.Equal(expected.ExceptionType, actual.ExceptionType);
        Assert.Equal(expected.Output, actual.Output);
        Assert.Equal(expected.Trace, actual.Trace);
    }

    internal static (ModuleNode Module, DiagnosticBag Diagnostics) Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        return (new Parser(tokens, diagnostics).Parse(), diagnostics);
    }

    internal static string SemanticTree(AstNode node)
    {
        var temporaries = new Dictionary<string, ExpressionNode>(StringComparer.Ordinal);
        void Collect(AstNode current)
        {
            if (current is BindStatementNode { Initializer: not null } binding
                && binding.Name.StartsWith("_hoist", StringComparison.Ordinal))
                Assert.True(temporaries.TryAdd(binding.Name, binding.Initializer), "Generated temporary names must be unique");
            foreach (var child in RecursiveAstWalker.GetAllChildren(current))
                Collect(child);
        }
        Collect(node);
        return Describe(node, new HashSet<string>(StringComparer.Ordinal));

        string Describe(AstNode current, HashSet<string> expanding)
        {
            // Pretty printing introduces single-definition temporaries. Project them
            // back to expression children; the separate runtime oracle checks placement/order.
            if (current is ReferenceNode reference && temporaries.TryGetValue(reference.Name, out var initializer))
            {
                Assert.True(expanding.Add(reference.Name), "Cyclic generated temporary");
                var expanded = Describe(initializer, expanding);
                expanding.Remove(reference.Name);
                return expanded;
            }
            var scalars = current.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.GetIndexParameters().Length == 0
                && !property.Name.EndsWith("Id", StringComparison.Ordinal)
                && (property.PropertyType.IsPrimitive || property.PropertyType.IsEnum || property.PropertyType == typeof(string)))
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => property.Name + "=" + JsonSerializer.Serialize(property.GetValue(current)));
            var effects = current is EffectsNode row
            ? string.Join(",", row.Effects.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value}"))
            : "";
            var children = RecursiveAstWalker.GetAllChildEdges(current)
                .Where(edge => edge.Node is not BindStatementNode binding || !temporaries.ContainsKey(binding.Name))
                .Select(edge => edge.Property.Name + ":" + Describe(edge.Node, expanding));
            return $"{current.GetType().Name}[{string.Join(";", scalars)};effects={effects}]({string.Join(",", children)})";
        }
    }

    private static CompilationOptions Options() => new()
    {
        EnableTypeChecking = true,
        EnforceEffects = true,
        VerifyContracts = false,
        StatusWriter = TextWriter.Null
    };

    private static Observation Execute(string source)
    {
        var trace = new List<int>();
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        var previous = Console.Out;
        try
        {
            Console.SetOut(writer);
            var result = TestHarness.Execute(source, "Probe", [trace], Options());
            Assert.True(result.Exception is null or OverflowException or DivideByZeroException,
                $"Unexpected production failure: {result.Exception}\n{source}");
            return new Observation((int?)result.ReturnValue, result.Exception?.GetType(), writer.ToString(),
                trace.ToArray());
        }
        finally
        {
            Console.SetOut(previous);
        }
    }

    private static Observation Evaluate(Sample sample)
    {
        if (sample.Mode is not ("expression" or "loop" or "match"))
            throw new NotSupportedException(sample.Mode);
        var trace = new List<int>();
        int Visit(Term term)
        {
            if (term.Kind == "literal")
            {
                trace.Add(term.Tag);
                return term.Value;
            }
            var left = Visit(term.Left!);
            if (term.Kind == "conditional")
                return Visit(left != 0 ? term.Right! : term.Third!);
            if (term.Kind == "and")
                return left != 0 && Visit(term.Right!) != 0 ? 1 : 0;
            if (term.Kind == "or")
                return left != 0 || Visit(term.Right!) != 0 ? 1 : 0;
            var right = Visit(term.Right!);
            return term.Kind switch
            {
                "+" => checked(left + right),
                "-" => checked(left - right),
                "*" => checked(left * right),
                "/" => checked(left / right),
                _ => throw new NotSupportedException(term.Kind)
            };
        }

        int? value = null;
        Type? error = null;
        try
        {
            if (sample.Mode == "loop")
            {
                var total = 0;
                for (var i = 0; i < sample.Count; i++)
                    total = checked(total + Visit(sample.Expression));
                value = total;
            }
            else
                value = Visit(sample.Mode == "match" && sample.Count != 0 ? sample.Alternative : sample.Expression);
        }
        catch (Exception exception) when (exception is OverflowException or DivideByZeroException)
        {
            error = exception.GetType();
        }
        return new Observation(value, error,
            sample.Printed.ToString(CultureInfo.InvariantCulture) + Environment.NewLine, trace.ToArray());
    }

    private static string RenderTerm(Term term) => term.Kind switch
    {
        "literal" => $"§C{{Tap}} §A trace §A {term.Tag} §A INT:{term.Value.ToString(CultureInfo.InvariantCulture)} §/C",
        "+" or "-" or "*" or "/" => $"({term.Kind} {RenderTerm(term.Left!)} {RenderTerm(term.Right!)})",
        "conditional" => $"(? (!= {RenderTerm(term.Left!)} 0) {RenderTerm(term.Right!)} {RenderTerm(term.Third!)})",
        "and" or "or" => $"(? ({(term.Kind == "and" ? "&&" : "||")} (!= {RenderTerm(term.Left!)} 0) (!= {RenderTerm(term.Right!)} 0)) 1 0)",
        _ => throw new NotSupportedException(term.Kind)
    };

    private static string Render(Sample sample)
    {
        var expression = RenderTerm(sample.Expression);
        var body = sample.Mode switch
        {
            "expression" => $"    §R {expression}\n",
            "loop" => $"    §B{{~total:i32}} 0\n    §L{{loop:i:1:{sample.Count}:1}}\n      §B{{~total}} (+ total {expression})\n    §R total\n",
            "match" => $"    §W{{match}} {sample.Count}\n      §K 0\n        §R {expression}\n      §K _\n        §R {RenderTerm(sample.Alternative)}\n",
            _ => throw new NotSupportedException(sample.Mode)
        };
        return """
            §M{module:GeneratedPipeline}
              §F{tap:Tap:pub} (List<i32>:trace, i32:tag, i32:value) -> i32
                §E{mut}
                §C{trace.Add} §A tag §/C
                §R value
              §F{probe:Probe:pub} (List<i32>:trace) -> i32
                §E{mut,cw}
            """ + $"\n    §P {sample.Printed.ToString(CultureInfo.InvariantCulture)}\n" + body;
    }
}
