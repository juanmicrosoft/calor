using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Pins the two 0.30 binding-cost fixes. The source projection (the annotated C# image
/// <see cref="CallGraphAnalysis.ResolveOperatorCalls"/> binds with Roslyn) looked up the
/// Calor origin of every walked operation with a linear scan over all annotated ranges,
/// and the binder built that image for every module that declared any parameter.
/// </summary>
public sealed class SourceProjectionScalingTests
{
    /// <summary>
    /// The indexed origin lookup must return exactly what the length-ordered scan
    /// returned, for nested ranges (the shape the emitter's markers produce) and for
    /// any other family (which falls back to the scan).
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void OriginIndex_MatchesTheLengthOrderedScan(int seed)
    {
        var random = new Random(seed);
        for (var family = 0; family < 40; family++)
        {
            var ranges = new List<(int Start, int End, AstNode Node)>();
            var nested = family % 4 != 3;
            if (nested)
                AddNested(random, ranges, 0, 2_000, depth: 0);
            else
                for (var count = 0; count < random.Next(1, 60); count++)
                {
                    var start = random.Next(0, 2_000);
                    ranges.Add((start, start + random.Next(0, 300), Node(ranges.Count)));
                }
            // The projection sorts by length with List.Sort; the index must agree with
            // whatever order that sort left equal-length ranges in.
            ranges.Sort((left, right) => (left.End - left.Start).CompareTo(right.End - right.Start));
            var index = new CallGraphAnalysis.ExpressionOriginIndex(ranges);
            for (var query = 0; query < 400; query++)
            {
                var start = random.Next(-5, 2_310);
                var end = random.Next(0, 4) == 0 ? start : start + random.Next(0, 200);
                var expected = ranges.FirstOrDefault(range => range.Start <= start && range.End >= end).Node;
                Assert.Same(expected, index.Find(start, end));
            }
            foreach (var range in ranges)
                Assert.Same(ranges.First(candidate => candidate.Start <= range.Start && candidate.End >= range.End).Node,
                    index.Find(range.Start, range.End));
        }

        static void AddNested(Random random, List<(int Start, int End, AstNode Node)> ranges,
            int start, int end, int depth)
        {
            // Siblings are separated by at least one character, as marker comments
            // separate emitted expressions; children sit strictly inside their parent.
            var cursor = start;
            while (cursor < end && ranges.Count < 300)
            {
                var childStart = cursor + random.Next(1, 20);
                var childEnd = childStart + random.Next(0, Math.Max(1, (end - childStart) / 2));
                if (childEnd >= end)
                    break;
                ranges.Add((childStart, childEnd, Node(ranges.Count)));
                if (depth < 8 && childEnd - childStart > 4 && random.Next(0, 3) != 0)
                    AddNested(random, ranges, childStart, childEnd, depth + 1);
                cursor = childEnd + 1;
            }
        }
    }

    [Fact]
    public void OriginIndex_TouchingAndDuplicateRangesUseTheScan()
    {
        var a = Node(0);
        var b = Node(1);
        var c = Node(2);
        // [0,5] and [5,9] share position 5 without nesting; [10,12] appears twice.
        var ranges = new List<(int Start, int End, AstNode Node)> { (10, 12, b), (10, 12, c), (5, 9, a), (0, 5, Node(3)) };
        ranges.Sort((left, right) => (left.End - left.Start).CompareTo(right.End - right.Start));
        var index = new CallGraphAnalysis.ExpressionOriginIndex(ranges);
        foreach (var (start, end) in new[] { (5, 5), (10, 12), (11, 11), (0, 9), (6, 7) })
            Assert.Same(ranges.FirstOrDefault(range => range.Start <= start && range.End >= end).Node,
                index.Find(start, end));
    }

    public static IEnumerable<object?[]> DeclaredPositionCases() =>
    [
        // Spellings whose emitted C# can never be a delegate: no function type.
        ["int", null],
        ["double", null],
        ["(int, string)", null],
        ["string", null],
        ["string?", null],
        ["bool", null],
        ["int[]", null],
        // Callback spellings still get the projection's signature.
        ["Action", "VOID"],
        ["System.Action", "VOID"],
        ["Callback", "VOID"],
        ["NativeCallback", "VOID"],
        ["Func<int>", "INT"],
    ];

    /// <summary>
    /// The binder skips the source projection for a parameter or return whose emitted
    /// type cannot be a delegate. Those positions bind exactly as before (no function
    /// type) and callback positions still carry the projection's signature.
    /// </summary>
    [Theory]
    [MemberData(nameof(DeclaredPositionCases))]
    public void ParameterAndReturnFunctionTypes_DependOnlyOnTheEmittedType(string type, string? returnType)
    {
        var row = returnType is null ? "" : " !";
        var source = $$"""
            module DeclaredPositions
              using Callback = System.Action
              delegate NativeCallback () → void
                !
              func Pass public ({{type}}:value{{row}}) → {{type}}{{row}}
                !
                return value
            """;
        var diagnostics = new DiagnosticBag();
        var ast = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var bound = new Binder(diagnostics).Bind(ast);
        Assert.DoesNotContain(diagnostics, BindingDiagnosticPolicy.IsCompilationError);
        var symbol = bound.Functions.Single(function => function.Symbol.Name == "Pass").Symbol;
        var parameter = Assert.Single(symbol.Parameters);
        if (returnType is null)
        {
            Assert.Null(parameter.FunctionType);
            Assert.Null(symbol.ReturnFunctionType);
            return;
        }
        foreach (var functionType in new[] { parameter.FunctionType, symbol.ReturnFunctionType })
        {
            Assert.NotNull(functionType);
            Assert.False(functionType!.Row.IsUnknown);
            Assert.Empty(functionType.ParameterTypes);
            Assert.Equal(returnType, TypeIdentity.Canonicalize(functionType.ReturnType.DisplayString));
        }
    }

    /// <summary>
    /// For-loop temporaries share one module-wide name set. Reserving them now resumes
    /// at the last suffix instead of probing from the bare name for every loop (which
    /// made emission quadratic in loop count); the names must be exactly the first free
    /// ones, skipping any the source already uses.
    /// </summary>
    [Fact]
    public void ForLoopTemporaries_TakeTheFirstFreeSuffixAcrossTheModule()
    {
        var source = """
            module LoopTemps
              func First public (int:n) → int
                !
                let int:__calorForFrom_1 = n
                for i from 0 to n step 1
                  let int:a = i * 2
                for j from 0 to n step 2
                  let int:b = j * 2
                return __calorForFrom_1
              func Second public (int:n) → int
                !
                for k from 1 to n step 1
                  let int:c = k * 2
                return n
            """;
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        var declared = System.Text.RegularExpressions.Regex.Matches(compiled.GeneratedCode, @"var (__calorFor(From|First)(_\d+)?) =")
            .Select(match => match.Groups[1].Value).ToArray();
        Assert.Equal(new[]
        {
            "__calorForFrom", "__calorForFirst",
            "__calorForFrom_2", "__calorForFirst_1",
            "__calorForFrom_3", "__calorForFirst_2",
        }, declared);
    }

    private static AstNode Node(int index) => new ReferenceNode(new TextSpan(index, 1, 1, 1), $"n{index}");
}
