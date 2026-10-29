using Calor.Compiler.IR;
using FsCheck;
using FsCheck.Xunit;
using Xunit;
using Xunit.Sdk;

namespace Calor.Compiler.Tests.PropertyTests;

/// <summary>
/// CNF here means Calor Normal Form, not conjunctive normal form. Its auxiliaries
/// are assigned temporaries, not existential SAT variables. Execute the actual
/// branches and assignments, comparing values and short-circuit reference counts.
/// Pure parameter reads can be deferred until an IR operation uses its operands.
/// </summary>
public sealed class CnfLoweringEquivalencePropertyTests
{
    public sealed record Term(string Operation, Term? Left = null, Term? Right = null, Term? Third = null);

    private sealed record Observation(bool Value, string[] Reads);

    public static Arbitrary<Term> BooleanExpressions()
    {
        Gen<Term> Grow(int depth)
        {
            var leaf = Gen.Elements("a", "b", "c", "true", "false").Select(op => new Term(op));
            if (depth == 0)
                return leaf;
            var child = Grow(depth - 1);
            return Gen.OneOf(leaf,
                child.Select(term => new Term("not", term)),
                from op in Gen.Elements("and", "or", "==", "!=")
                from left in child
                from right in child
                select new Term(op, left, right),
                from condition in child
                from yes in child
                from no in child
                select new Term("if", condition, yes, no));
        }

        return Arb.From(Grow(3), Shrink);
    }

    private static IEnumerable<Term> Shrink(Term term) =>
        new[] { term.Left, term.Right, term.Third }.OfType<Term>();

    [Property(MaxTest = 100)]
    public Property Lowering_PreservesTruthAndShortCircuitReadCountsForEveryAssignment() =>
        Prop.ForAll(BooleanExpressions(), term =>
        {
            Check(term);
            Check(new Term("not", term));
            return true;
        });

    [Theory]
    [InlineData("a")]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("not")]
    [InlineData("and")]
    [InlineData("or")]
    [InlineData("==")]
    [InlineData("!=")]
    [InlineData("if")]
    [InlineData("nested")]
    [InlineData("deferred-read")]
    public void Controls_ExerciseBothBranchPolaritiesAndNestedTemporaries(string operation)
    {
        var a = new Term("a");
        var b = new Term("b");
        var term = operation switch
        {
            "a" or "true" or "false" => new Term(operation),
            "not" => new Term("not", a),
            "if" => new Term("if", a, b, new Term("c")),
            "nested" => new Term("or",
                new Term("and", new Term("not", a), b),
                new Term("if", new Term("c"), new Term("not", b), a)),
            "deferred-read" => new Term("==", new Term("c"),
                new Term("if", new Term("true"), a, new Term("false"))),
            _ => new Term(operation, a, b)
        };
        Check(term);
        Check(new Term("not", term));
    }

    [Theory]
    [InlineData("branch")]
    [InlineData("default")]
    [InlineData("negation")]
    public void Oracle_RejectsWrongPolarityAndTemporaryInitialization(string mutation)
    {
        var term = mutation == "negation"
            ? new Term("not", new Term("a"))
            : new Term("and", new Term("a"), new Term("b"));
        var original = Lower(term);
        var changed = false;
        var mutant = original.Select(statement =>
        {
            if (changed)
                return statement;
            CnfStatement replacement = (mutation, statement) switch
            {
                ("branch", CnfBranch branch) =>
                    new CnfBranch(branch.Condition, branch.FalseLabel, branch.TrueLabel),
                ("default", CnfAssign { Value: CnfLiteral { Value: bool value } } assign) =>
                    new CnfAssign(assign.Target, new CnfLiteral(!value, CnfType.Bool), assign.TargetType),
                ("negation", CnfAssign { Value: CnfUnaryOp unary } assign) =>
                    new CnfAssign(assign.Target, unary.Operand, assign.TargetType),
                _ => statement
            };
            changed = !ReferenceEquals(statement, replacement);
            return replacement;
        }).ToArray();
        Assert.True(changed);
        Assert.ThrowsAny<XunitException>(() => Check(term, mutant));
    }

    [Fact]
    public void ReadOracle_RejectsEagerEvaluationEvenWhenTruthValueMatches()
    {
        var term = new Term("and", new Term("a"), new Term("b"));
        var original = Lower(term);
        var mutant = new CnfStatement[]
        {
            new CnfAssign("eager", new CnfVariableRef("b", CnfType.Bool), CnfType.Bool)
        }.Concat(original).ToArray();
        var expected = Evaluate(term, 0);
        var actual = Execute(mutant, 0);
        Assert.Equal(expected.Value, actual.Value);
        Assert.NotEqual(expected.Reads, actual.Reads);
        Assert.ThrowsAny<XunitException>(() => Check(term, mutant));
    }

    private static void Check(Term term, IReadOnlyList<CnfStatement>? statements = null)
    {
        statements ??= Lower(term);
        for (var assignment = 0; assignment < 8; assignment++)
        {
            var expected = Evaluate(term, assignment);
            var actual = Execute(statements, assignment);
            Assert.True(expected.Value == actual.Value,
                $"{Render(term)}, assignment={assignment}: expected={expected.Value}, actual={actual.Value}");
            Assert.Equal(expected.Reads.Order(StringComparer.Ordinal), actual.Reads.Order(StringComparer.Ordinal));
        }
    }

    private static IReadOnlyList<CnfStatement> Lower(Term term)
    {
        var source = "§M{m001:BooleanProperties}\n"
            + "  §F{f001:Evaluate:pub} (bool:a, bool:b, bool:c) -> bool\n"
            + $"    §R {Render(term)}\n";
        var (module, diagnostics) = GeneratedProductionPipelinePropertyTests.Parse(source);
        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Errors));
        var function = Assert.Single(new CnfLowering().LowerModule(module).Functions);
        Assert.All(function.Parameters, parameter => Assert.Equal(CnfType.Bool, parameter.Type));
        Assert.Equal(CnfType.Bool, function.ReturnType);
        return function.Body.Statements;
    }

    private static string Render(Term term) => term.Operation switch
    {
        "a" or "b" or "c" => term.Operation,
        "true" or "false" => $"BOOL:{term.Operation}",
        "not" => $"(! {Render(term.Left!)})",
        "if" => $"(? {Render(term.Left!)} {Render(term.Right!)} {Render(term.Third!)})",
        "and" or "or" or "==" or "!=" =>
            $"({term.Operation} {Render(term.Left!)} {Render(term.Right!)})",
        _ => throw new NotSupportedException(term.Operation)
    };

    private static Observation Evaluate(Term term, int assignment)
    {
        var reads = new List<string>();
        bool Visit(Term current)
        {
            if (current.Operation is "a" or "b" or "c")
            {
                reads.Add(current.Operation);
                return (assignment & (1 << (current.Operation[0] - 'a'))) != 0;
            }
            return current.Operation switch
            {
                "true" => true,
                "false" => false,
                "not" => !Visit(current.Left!),
                "and" => Visit(current.Left!) && Visit(current.Right!),
                "or" => Visit(current.Left!) || Visit(current.Right!),
                "==" => Visit(current.Left!) == Visit(current.Right!),
                "!=" => Visit(current.Left!) != Visit(current.Right!),
                "if" => Visit(current.Left!) ? Visit(current.Right!) : Visit(current.Third!),
                _ => throw new NotSupportedException(current.Operation)
            };
        }
        return new Observation(Visit(term), reads.ToArray());
    }

    private static Observation Execute(IReadOnlyList<CnfStatement> statements, int assignment)
    {
        var values = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["a"] = (assignment & 1) != 0,
            ["b"] = (assignment & 2) != 0,
            ["c"] = (assignment & 4) != 0
        };
        var reads = new List<string>();
        var labels = statements.Select((statement, index) => (statement, index))
            .Where(item => item.statement is CnfLabel)
            .ToDictionary(item => ((CnfLabel)item.statement).Name, item => item.index, StringComparer.Ordinal);

        bool Value(CnfExpression expression) => expression switch
        {
            CnfLiteral { Value: bool value } => value,
            CnfVariableRef reference => Read(reference.Name),
            CnfUnaryOp { Operator: CnfUnaryOperator.Not } unary => !Value(unary.Operand),
            CnfBinaryOp { Operator: CnfBinaryOperator.Equal } binary => Value(binary.Left) == Value(binary.Right),
            CnfBinaryOp { Operator: CnfBinaryOperator.NotEqual } binary => Value(binary.Left) != Value(binary.Right),
            _ => throw new NotSupportedException($"Unexpected boolean IR: {expression.GetType().Name}")
        };

        bool Read(string name)
        {
            if (name is "a" or "b" or "c")
                reads.Add(name);
            Assert.True(values.TryGetValue(name, out var value), $"Read of unassigned CNF temporary {name}");
            return value;
        }

        var pc = 0;
        var steps = 0;
        while (pc < statements.Count)
        {
            Assert.True(++steps <= statements.Count * 2, "Boolean lowering must terminate without loops");
            switch (statements[pc++])
            {
                case CnfAssign assign:
                    values[assign.Target] = Value(assign.Value);
                    break;
                case CnfBranch branch:
                    pc = labels[Value(branch.Condition) ? branch.TrueLabel : branch.FalseLabel];
                    break;
                case CnfGoto jump:
                    pc = labels[jump.Target];
                    break;
                case CnfLabel:
                    break;
                case CnfReturn { Value: not null } ret:
                    return new Observation(Value(ret.Value), reads.ToArray());
                default:
                    throw new NotSupportedException($"Unexpected boolean statement: {statements[pc - 1].GetType().Name}");
            }
        }
        throw new InvalidOperationException("Lowered boolean function did not return");
    }
}
