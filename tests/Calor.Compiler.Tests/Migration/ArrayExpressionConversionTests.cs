using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.RegularExpressions;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Commands;
using Calor.Compiler.Mcp.Tools;
using Calor.Compiler.Migration;
using Calor.Compiler.Migration.Project;
using Calor.Compiler.Tests.EvidenceContract;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// 0.25 F5 (#1132): array expressions convert natively and keep C# evaluation
/// semantics. Each registered R0 row (F5-ARRAY-01..05) and each extra witness is
/// converted on every surface, compiled with default options (effects enforced), run,
/// and compared with the original C#. The witnesses log every side effect, so a
/// reordered, duplicated, skipped or eagerly evaluated subexpression changes the
/// result. Nothing may be preserved as C# (no rescue, no §CS) on a native row.
/// </summary>
public class ArrayExpressionConversionTests
{
    private const string R0Fixtures = "docs/plans/evidence/v0.25-r0-1426/fixtures";

    public enum Surface
    {
        LibraryDefault,
        CliDefault,
        CliPassthrough,
        McpDefault,
        McpPassthroughOnError,
        McpPassthroughOnErrorModuleName,
        ProjectMigrate,
        ProjectMigratePassthrough
    }

    // Element order around a call that mutates what the other elements read, in
    // expression and statement position; object initializers as 2-D elements; a ?: that
    // must not evaluate the unselected branch; && / || laziness over array operands;
    // index order; an array argument after another argument; rank 3, sized and bare
    // initializers; a nested array; an element that throws part-way through a row.
    private const string Ordering = """
        using System;

        public class Cell
        {
            public int V;
        }

        public static class L
        {
            public static string log = "";
            public static int counter = 0;

            public static int S(int v)
            {
                log = log + v;
                return v;
            }

            public static int Next()
            {
                counter = counter + 1;
                log = log + "n" + counter;
                return counter;
            }

            public static bool T(bool b, int v)
            {
                log = log + "t" + v;
                return b;
            }

            public static int Boom()
            {
                throw new InvalidOperationException("boom");
            }

            public static int Sum(int a, int[] xs)
            {
                int s = a;
                foreach (var x in xs)
                {
                    s = s + x;
                }
                return s;
            }
        }

        public static class A1
        {
            public static string Go()
            {
                var r = new int[] { L.counter, L.Next(), L.counter };
                int[] a = { L.counter, L.Next(), L.counter };
                return r[0] + "," + r[1] + "," + r[2] + "," + a[0] + "," + a[1] + "," + a[2];
            }
        }

        public static class A2
        {
            public static string Go()
            {
                Cell[,] g = { { new Cell { V = L.Next() }, new Cell { V = L.counter } } };
                return g[0, 0].V + "," + g[0, 1].V;
            }
        }

        public static class A3
        {
            public static Cell[,] Pick(bool flag)
            {
                return flag ? new Cell[,] { { new Cell { V = L.S(1) } } } : new Cell[,] { { new Cell { V = L.S(2) }, new Cell { V = L.S(3) } } };
            }

            public static Cell[] Pick1(bool flag)
            {
                return flag ? new Cell[] { new Cell { V = L.S(4) } } : new Cell[] { new Cell { V = L.S(5) } };
            }

            public static string Go()
            {
                return Pick(false).GetLength(1) + "," + Pick1(true)[0].V;
            }
        }

        public static class A4
        {
            public static string Go()
            {
                bool lazyAnd = L.T(false, 1) && new int[] { L.S(9) }.Length > 0;
                bool lazyOr = L.T(true, 2) || new int[,] { { L.S(8) } }.Length > 0;
                int len = new int[,] { { L.S(3), L.S(4) } }.Length;
                return "" + lazyAnd + lazyOr + len;
            }
        }

        public static class A5
        {
            public static string Go()
            {
                int[] a = { 5, 6 };
                Cell[,] p = new Cell[1, 2];
                p[0, 1] = new Cell { V = 7 };
                int idx = a[L.S(0)] + p[L.S(0), L.S(1)].V;
                int sum = L.Sum(L.S(3), new int[] { L.S(4), L.S(5) });
                return idx + "," + sum;
            }
        }

        public static class A7
        {
            public static int[,,] Cube()
            {
                return new int[,,] { { { L.S(4), L.S(5) } }, { { L.S(6), L.S(7) } } };
            }

            public static string Go()
            {
                var c = Cube();
                var sized = new int[2, 2] { { 1, 2 }, { 3, 4 } };
                int[,,] local = { { { 1 }, { 2 } } };
                return c[1, 0, 1] + "," + c.Rank + "," + sized[1, 0] + "," + local[0, 1, 0] + local.GetLength(1);
            }
        }

        public static class A8
        {
            public static string Go()
            {
                var nested = new object[] { new int[] { L.S(6) }, L.S(7) };
                string thrown = "none";
                try
                {
                    var bad = new int[,] { { L.S(1), L.Boom(), L.S(2) } };
                    thrown = "len" + bad.Length;
                }
                catch (InvalidOperationException e)
                {
                    thrown = e.Message;
                }
                return ((int[])nested[0])[0] + "," + thrown;
            }
        }

        public static class Probe
        {
            public static string Run()
            {
                return A1.Go() + "|" + A2.Go() + "|" + A3.Go() + "|" + A4.Go() + "|" + A5.Go() + "|" + A7.Go() + "|" + A8.Go() + "|" + L.log;
            }
        }
        """;

    // Arrays as field, property and bare initializers, and size expressions with calls
    // in argument and return position (kept in place, not hoisted ahead of the call's
    // earlier argument).
    private const string Placement = """
        using System;

        public class Holder
        {
            public static int[,] Grid = new int[,] { { 1, 2 }, { 3, 4 } };
            public int[,,] Cube { get; } = new int[,,] { { { 5 } } };
            public static int[,] Bare = { { 6, 7 } };
        }

        public static class L
        {
            public static string log = "";

            public static int S(int v)
            {
                log = log + v;
                return v;
            }

            public static int Use(int a, int[] xs)
            {
                return a + xs.Length;
            }

            public static int Use2(int a, int[,] xs)
            {
                return a + xs.Length;
            }

            public static int[,] Make()
            {
                return new int[L.S(5) + 1, L.S(6)];
            }
        }

        public static class Probe
        {
            public static string Run()
            {
                int a = L.Use(L.S(1), new int[L.S(2)]);
                int b = L.Use2(L.S(3), new int[L.S(4), 2]);
                return a + "," + b + "," + L.Make().Length + "," + Holder.Grid[1, 0] + new Holder().Cube[0, 0, 0] + Holder.Bare[0, 1] + "|" + L.log;
            }
        }
        """;

    // Codex round 1 reproductions that must stay native: an inline array argument before
    // a hoisted constructor argument; postfix in an element; a computed size after a
    // plain one; constructor arguments inside an element; written sizes behind an empty
    // level; an implicitly typed array of calls; jagged and rectangular-of-jagged
    // creation; fresh (not shared) empty arrays; a nested sized array.
    private const string Shapes = """
        public class Cell
        {
            public int N;

            public Cell(int n)
            {
                N = n;
            }

            public Cell(int a, int b)
            {
                N = a * 10 + b;
            }
        }

        public static class L
        {
            public static string log = "";
            public static int x = 0;
            public static int n = 1;

            public static int S(int v)
            {
                log = log + v;
                return v;
            }

            public static int Bump()
            {
                x = x + 1;
                return x;
            }

            public static int Next()
            {
                n = n + 1;
                return n;
            }

            public static int Use(int[] a, Cell c)
            {
                return a.Length + c.N;
            }
        }

        public static class Probe
        {
            static int[][] Make(bool f)
            {
                return f ? new int[][] { new int[L.n + 1], new int[L.S(6)] } : new int[][] { new int[L.S(7)] };
            }

            public static string Run()
            {
                int u = L.Use(new int[] { L.S(1) }, new Cell(L.S(2)));
                int y = 0;
                int[] b = { y, y++, y };
                var d = new int[L.n, L.Next()];
                Cell[] cs = { new Cell(L.x, L.Bump()) };
                var e1 = new int[0, 3] { };
                var e2 = new int[2, 0, 3] { { }, { } };
                var imp = new[] { L.S(3), L.S(4) };
                var jag = new int[2][];
                var mj = new int[,][] { { new int[] { 7 } } };
                bool fresh = new int[0] == new int[0];
                int[] empty = { };
                var nested = new int[][] { new int[0], new int[2] };
                var made = Make(true);
                return u + "|" + b[0] + b[1] + b[2] + "|" + d.GetLength(0) + d.GetLength(1) + made[0].Length + made[1].Length
                    + "|" + cs[0].N + "|" + e1.GetLength(1) + e2.GetLength(2) + "|" + (imp is int[]) + "|" + jag.Length + mj[0, 0][0]
                    + "|" + fresh + empty.Length + nested[1].Length + "|" + L.log;
            }
        }
        """;

    // Codex round 2 reproductions that must stay native: a sized 2-D array as a 2-D
    // element (it took the parent's next row), `n++` in a size, an implicitly typed
    // array received as object[], and `null` / `default` before a hoisted argument.
    private const string Round2 = """
        #nullable enable
        public class Cell
        {
            public int N;

            public Cell(int n)
            {
                N = n;
            }
        }

        public static class L
        {
            public static int x = 0;

            public static int Bump()
            {
                x = x + 1;
                return x;
            }

            public static string S()
            {
                return "a";
            }

            public static int Use(string? s, int[] a, Cell c)
            {
                return (s == null ? 1000 : 0) + a.Length + c.N;
            }

            public static int Use2(int[] a, int d, Cell c)
            {
                return a.Length + d + c.N;
            }
        }

        public static class Probe
        {
            public static string Run()
            {
                object[,] grid = new object[,] { { new int[1, 1] }, { 42 } };
                int n = 1;
                var shape = new int[n, n++];
                object[] oa = new[] { L.S() };
                int u = L.Use(null, new int[] { 1 }, new Cell(L.Bump()));
                int v = L.Use2(new int[] { 1 }, default, new Cell(L.Bump()));
                return grid.GetLength(0) + "," + ((int[,])grid[0, 0])[0, 0] + "," + grid[1, 0] + "|" + shape.GetLength(0) + shape.GetLength(1) + n
                    + "|" + (oa is string[]) + "|" + u + "," + v;
            }
        }
        """;

    // Codex round 3 reproductions that must stay native: an implicit conversion inside
    // each element stays with its element (no hoisting), `g[n, n++]` reads its indices in
    // order, a 1-D array of rectangular arrays, written sizes behind an empty level that
    // are constant expressions, an implicitly typed rectangular array; and (verification
    // pass) a constructor argument and a multi-line anonymous object in statement elements,
    // and a declared int[][,] with a bare initializer.
    private const string Round3 = """
        using System;

        public struct X
        {
            public int V;

            public X(int v)
            {
                V = v;
            }

            public static implicit operator int(X x)
            {
                L.log = L.log + "c";
                return x.V;
            }
        }

        public static class L
        {
            public static string log = "";

            public static int T(int v)
            {
                log = log + "t";
                return v;
            }

            public static X S(int v)
            {
                log = log + "s";
                return new X(v);
            }

        }

        public static class Probe
        {
            public static string Run()
            {
                int[] a = { L.S(1), L.S(2) };
                L.log = L.log + "|";
                int[,] g = { { 10, 20 }, { 30, 40 } };
                int n = 0;
                int picked = g[n, n++];
                var mr = new int[][,] { new int[,] { { 1 } } };
                var cs = new int[1, 1 + 1, 0] { { { }, { } } };
                var imp = new[,] { { 1, 2 }, { 3, 4 } };
                int[] cv = { L.S(5), new X(L.T(6)) };
                object[] an = { new { A = 7 } };
                int[][,] dj = { new int[,] { { 8 } } };
                return a[0] + a[1] + "," + picked + "," + mr[0][0, 0] + cs.GetLength(1) + imp[1, 0] + "," + (cv[0] + cv[1]) + an.Length + dj[0][0, 0] + "|" + L.log;
            }
        }
        """;

    // Codex round 3: an `in` argument whose address calls a method cannot run after a
    // hoisted later argument, so the member is preserved; a method-group element keeps
    // its target type.
    private const string Round3Preserved = """
        using System;

        public struct X
        {
            public int V;

            public static implicit operator int(X x)
            {
                L.log = L.log + "c";
                return x.V;
            }
        }

        public class Cell
        {
        }

        public static class L
        {
            public static string log = "";

            public static X S(int v)
            {
                log = log + "s";
                return new X { V = v };
            }

            public static int I()
            {
                log = log + "1";
                return 0;
            }

            public static int T(int v)
            {
                log = log + "2";
                return v;
            }

            public static int Id(int x) => x;

            public static string Id(string x) => x;

            public static Func<int, int> Make() => Id;

            public static int Use(in int a, int[] b, Cell c) => a + b.Length;
        }

        public static class Probe
        {
            public static string Run()
            {
                int[] a = { L.S(1), L.S(2) };
                L.log = L.log + "|";
                Func<int, int>[] fs = { L.Id, L.Make() };
                int[,] g = { { 10, 20 }, { 30, 40 } };
                int n = 0;
                int picked = g[n, n++];
                int[] xs = { 5 };
                int used = L.Use(in xs[L.I()], new int[] { L.T(3) }, new Cell());
                var mr = new int[][,] { new int[,] { { 1 } } };
                var cs = new int[1, 1 + 1, 0] { { { }, { } } };
                var imp = new[,] { { 1, 2 }, { 3, 4 } };
                return a[0] + a[1] + "," + fs[0](4) + fs[1](5) + "," + picked + "," + used + "," + mr[0][0, 0] + cs.GetLength(1) + imp[1, 0] + "|" + L.log;
            }
        }
        """;

    public static TheoryData<string, string, Surface> Rows()
    {
        var data = new TheoryData<string, string, Surface>();
        foreach (var surface in Enum.GetValues<Surface>())
        {
            data.Add("F5-ARRAY-01", "2x2|2|3", surface);
            data.Add("F5-ARRAY-02", "13|2|5|67125", surface);
            data.Add("F5-ARRAY-03", "3|3|3|4", surface);
            data.Add("F5-ARRAY-04", "1|2|50|boom|123", surface);
            data.Add("F5-ARRAY-05", "3|4|3|9|b|6", surface);
            data.Add("ordering", "0,1,1,1,2,2|3,3|2,4|FalseTrue2|12,12|7,3,3,22|6,boom|n1n2n3234t1t2340013454567671", surface);
            data.Add("placement", "3,11,36,357|123456", surface);
            data.Add("shapes", "3|001|1236|1|33|True|27|False02|12346", surface);
            data.Add("round2", "2,0,42|112|True|1002,3", surface);
            data.Add("round3", "3,10,123,1118|scsc|sctc", surface);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task ArrayExpressions_ConvertNatively_AndRunLikeTheOriginal(string row, string expected, Surface surface)
    {
        var source = Source(row);
        Assert.Equal(expected, Run(source));

        var (calor, losses) = await ConvertAsync(source, surface);

        // Native: nothing preserved as C# and nothing rescued after the fact.
        Assert.Empty(losses);
        Assert.DoesNotContain("§CSHARP", calor);
        Assert.DoesNotContain("§CS{", calor);

        var compilation = Program.Compile(calor, row + ".calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(compilation.HasErrors,
            string.Join(Environment.NewLine, compilation.Diagnostics.Errors) + "\n" + calor);
        Assert.Equal(expected, Run(compilation.GeneratedCode));
    }

    [Fact]
    public void HistoricalRepro_ReturnsTheArrayInPlace()
    {
        // F5-ARRAY-01: the expression-position §ARR2D used to be a block bound to a
        // name the C# never declared (CS0103 arr2d005) plus a bare `new …;` (CS0201).
        var result = new CSharpToCalorConverter().Convert(R0Fixture("F5-ARRAY-01"));
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.Matches(@"§R §ARR2D\{(\w+):\1:Cell\} §ROW §NEW\{Cell\} V = 1 §/NEW", result.CalorSource!);
        Assert.DoesNotContain("§B{~_hoist", result.CalorSource);
        Assert.Contains("(§IDX2D cells 0 1).V", result.CalorSource);
    }

    [Fact]
    public void GeneratedNames_NeverReuseAUserName()
    {
        // F5-ARRAY-04: the user's `_hoist000` local and the parameters named like the
        // ids the converter used to generate (arr2d005/arr2d006) stay the only owners
        // of those names.
        var result = new CSharpToCalorConverter().Convert(R0Fixture("F5-ARRAY-04"));
        Assert.True(result.Success, string.Join("\n", result.Issues));
        var calor = result.CalorSource!;
        Assert.Single(Regex.Matches(calor, @"§B\{[^}]*\b_hoist000\}"));
        Assert.DoesNotMatch(@"§ARR2D\{arr2d00[56]\b", calor);

        var context = new ConversionContext { OriginalSource = "int arr001 = 0; var _hoist000 = arr002;" };
        Assert.Equal("arr003", context.GenerateId("arr"));
        Assert.True(context.IsReservedName("_hoist000"));

        // Codex round 1: a name spelled only with an escape inside an interpolation hole,
        // or only in another file of the project (a base-class field), is reserved too.
        const string probe = """
            public class Box { public Box(int a, int b) { } }

            public class Probe2 : Base
            {
                static int S() { return 1; }

                public string Go()
                {
                    int[] a = { S(), 2 };
                    // A §NEW argument with a call is hoisted: the temp needs a fresh name.
                    var box = new Box(a[0], S());
                    int[] b = { a[0], S() };
                    return $"{\u005fhoist001}" + a[0] + b.Length + _hoist002;
                }
            }
            """;
        var baseTree = CSharpSyntaxTree.ParseText("public class Base { public int _hoist000 = 99; public int _hoist002 = 97; }");
        // _hoist001 is spelled here only through the escape; the base tree supplies 000 and 002.
        var escaped = new CSharpToCalorConverter(new ConversionOptions { AdditionalSemanticSyntaxTrees = [baseTree], ValidateRoundTripCSharp = false })
            .Convert(probe);
        Assert.True(escaped.Success, string.Join("\n", escaped.Issues));
        Assert.DoesNotMatch(@"§B\{~_hoist00[012]\}", escaped.CalorSource!);
        Assert.Matches(@"§B\{~_hoist003\}", escaped.CalorSource!);

        // Codex round 2: a name inside an #if branch that is active only under the
        // conversion's own symbols is reserved too (it was parsed without them).
        const string conditional = """
            public static class Probe
            {
            #if FOO
                static int _ho\u0069st000 = 99;
                static int S() { return 1; }
                public static int Run() { int[] a = { S() }; return _ho\u0069st000 + a[0]; }
            #endif
            }
            """;
        var withSymbols = new CSharpToCalorConverter(new ConversionOptions { DefinedSymbols = ["FOO"] }).Convert(conditional);
        Assert.True(withSymbols.Success, string.Join("\n", withSymbols.Issues));
        Assert.DoesNotMatch(@"§B\{~_hoist000\}", withSymbols.CalorSource!);
    }

    [Fact]
    public async Task StatementPositionControl_IsUnchanged()
    {
        // F5-ARRAY-03: statement-position arrays keep exactly the R0 baseline output.
        var baseline = File.ReadAllText(Path.Combine(EvidenceContractTests.RepoRoot(),
            "docs/plans/evidence/v0.25-r0-1426/generated/F5-ARRAY-03.mcp-default.calr.txt"));
        var (calor, _) = await ConvertAsync(R0Fixture("F5-ARRAY-03"), Surface.McpDefault);
        Assert.Equal(baseline.ReplaceLineEndings().TrimEnd(), calor.ReplaceLineEndings().TrimEnd());
    }

    [Fact]
    public void UnsupportedShapes_ArePreservedWithAReport_AndRunLikeTheOriginal()
    {
        // Negative controls: a size with a call in a ?: branch, and an assignment used as
        // an array element or an index, cannot be written in place natively; each is
        // preserved as C# with a named loss, never hoisted or evaluated twice.
        const string source = """
            public static class L
            {
                public static string log = "";

                public static int S(int v)
                {
                    log = log + v;
                    return v;
                }

                public static int Bump()
                {
                    x = x + 1;
                    return x;
                }

                public static int x = 0;
            }

            public static class Probe
            {
                public static string Run()
                {
                    bool flag = false;
                    int[] xs = flag ? new int[L.S(7)] : new int[1];
                    int x = 0;
                    int[] ys = flag ? new int[] { x = 5 } : new int[] { 9 };
                    // Codex round 1: an assignment used as an element or index was queued
                    // ahead of the statement AND kept as the value (evaluated twice).
                    int[] a = { L.x, L.x = L.Bump(), L.x };
                    var t = new[] { 10, 20 };
                    int pick = t[L.x = 0];
                    int[] sized = new int[L.x = L.Bump()];
                    return xs.Length + "," + ys[0] + "," + x + "," + a[0] + a[1] + a[2] + "," + pick + "," + sized.Length + L.x + "|" + L.log;
                }
            }
            """;
        var result = new CSharpToCalorConverter().Convert(source);
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.Equal(5, result.Losses.Count(loss => loss.Feature == "conditional-expression-hoisting"
            && loss.Kind == ConversionLossKind.InteropPreserved));
        var compilation = Program.Compile(result.CalorSource!, "preserved.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null, EnforceEffects = false });
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Errors));
        Assert.Equal("1,9,0,011,10,11|", Run(source));
        Assert.Equal(Run(source), Run(compilation.GeneratedCode));
    }

    [Fact]
    public void ArrayExtensionCalls_AndInitialize_AreNotCertifiedPure()
    {
        // Codex round 3 and verification: only System.Array's own members, with their own
        // argument shapes, answer for an array receiver. An extension (even one named
        // GetLength) and Initialize (element constructors) stay unknown calls.
        const string calor = """
            §M{m1:T}
              §CSHARP{public static class Extensions { public static int Touch(this int[] xs) { System.Console.WriteLine("t"); return xs.Length; } public static int GetLength(this int[] xs, string s) { System.Console.WriteLine(s); return xs.Length; } }}§/CSHARP
              §CL{c1:G:pub:stat}
                §MT{m2:Go:pub:stat} (i32[]:xs) -> i32
                  §E{}
                  §R (+ §C{xs.Touch} §/C §C{xs.GetLength} §A "a" §/C)
                §MT{m3:Ok:pub:stat} (i32[,]:g, i32:k) -> i32
                  §E{}
                  §R (+ §C{g.GetLength} §A k §/C §C{g.GetLength} §A 0 §/C)
                §MT{m4:Init:pub:stat} (System.Array:a) -> void
                  §E{}
                  §C{a.Initialize} §/C
            """;
        var compilation = Program.Compile(calor, "extension.calr", new CompilationOptions { StatusWriter = TextWriter.Null });
        var undeclared = compilation.Diagnostics.Errors.Where(d => d.Code == "Calor0410").Select(d => d.Message).ToList();
        Assert.Equal(2, undeclared.Count);
        Assert.Contains(undeclared, m => m.Contains("'Go'"));
        Assert.Contains(undeclared, m => m.Contains("'Init'"));
    }

    [Fact]
    public void InAddressBeforeAHoistedArgument_IsPreserved_AndRunsLikeTheOriginal()
    {
        var result = new CSharpToCalorConverter().Convert(Round3Preserved);
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.Single(result.Losses, loss => loss.Feature == "conditional-expression-hoisting");
        var compilation = Program.Compile(result.CalorSource!, "round3.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null, EnforceEffects = false });
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Errors));
        Assert.Equal("3,45,10,6,123|scsc|12", Run(Round3Preserved));
        Assert.Equal(Run(Round3Preserved), Run(compilation.GeneratedCode));
    }

    [Fact]
    public void ElementAccessMember_ParsesAsAGroup()
    {
        // `(§IDX2D g 0 1).V` is g[0, 1].V; written bare, `.V` belonged to the last index.
        const string calor = """
            §M{m1:T}
              §CL{c1:Cell:pub}
                §FLD{i32:V:pub}
              §CL{c2:G:pub:stat}
                §MT{m2:Get:pub:stat} (Cell[,]:g, [Cell]:xs) -> i32
                  §R (+ (§IDX2D g 0 1).V (§IDX{xs} 0).V)
            """;
        var compilation = Program.Compile(calor, "group.calr", new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Errors));
        Assert.Contains("g[0, 1].V", compilation.GeneratedCode);
        Assert.Contains("xs[0].V", compilation.GeneratedCode);
    }

    [Theory]
    [InlineData("§ARR2D{a:a:i32:2:1:2} §ROW 1 2 §ROW 3 4 §/ARR2D{a}", "i32[,,]", "new int[2, 1, 2] { { { 1, 2 } }, { { 3, 4 } } }", true)]
    [InlineData("§ARR2D{a:a:i32:2:2} §ROW 1 2 §ROW 3 4 §/ARR2D{a}", "i32[,]", "new int[2, 2] { { 1, 2 }, { 3, 4 } }", true)]
    [InlineData("§ARR2D{a:a:i32:2:2} §ROW 1 2 §/ARR2D{a}", "i32[,]", "new int[2, 2] { { 1, 2 } }", false)]
    public void SizedRows_KeepTheirShape_AndAMismatchIsAnError(string array, string type, string csharp, bool compiles)
    {
        // Rank 3+ and sized initializers: the rows are innermost vectors, regrouped by
        // the sizes. A row count that disagrees with the sizes is rejected (CS0847),
        // never padded or truncated.
        var calor = $"§M{{m1:T}}\n  §CL{{c1:G:pub:stat}}\n    §MT{{m2:Make:pub:stat}} () -> {type}\n      §E{{alloc}}\n      §R {array}\n";
        var compilation = Program.Compile(calor, "sized.calr", new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.Contains(csharp, compilation.GeneratedCode ?? "");
        Assert.Equal(!compiles, compilation.HasErrors);
    }

    [Theory]
    [InlineData("§B{i32:n} §C{g.GetLength} §A 0 §/C", "", false)]
    [InlineData("§B{i32:n} g.Rank", "", false)]
    [InlineData("§C{g.SetValue} §A 1 §A 0 §A 0 §/C", "alloc", true)]
    [InlineData("§B{x} §C{g.Clone} §/C", "", true)]
    [InlineData("§B{x} §C{g.Clone} §/C", "alloc", false)]
    [InlineData("§B{x} §ARR2D{a:a:str} §ROW §C{Console.ReadLine} §/C §/ARR2D{a}", "alloc", true)]
    [InlineData("§B{x} §C{g.GetValue} §A 0 §A 0 §/C", "", true)]
    [InlineData("§C{g.Initialize} §/C", "mut", true)]
    [InlineData("§C{g.SetValue} §A 1 §A 0 §A 0 §/C", "mut", true)]
    public void ArrayMembersAndRowElements_AreCharged(string statement, string declared, bool undeclaredEffect)
    {
        // Array members resolve on System.Array (they were unknown, Calor0410), a
        // mutating one is charged, and the elements of §ARR2D rows are charged (they
        // were not inferred at all, so a row could launder an effect). Clone allocates:
        // System.Array's pure default must not certify it (Codex round 1). GetValue boxes;
        // Initialize runs element constructors, so it stays unknown (Codex round 2).
        var calor = $"§M{{m1:T}}\n  §CL{{c1:G:pub:stat}}\n    §MT{{m2:Go:pub:stat}} (i32[,]:g) -> void\n      §E{{{declared}}}\n      {statement}\n";
        var compilation = Program.Compile(calor, "effects.calr", new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.Equal(undeclaredEffect, compilation.Diagnostics.Errors.Any(d => d.Code == "Calor0410"));
        // Only Initialize is an unknown call by design.
        Assert.Equal(statement.Contains("Initialize"), compilation.Diagnostics.Any(d => d.Code == "Calor0411"));
    }

    private static string Source(string row) => row switch
    {
        "ordering" => Ordering,
        "placement" => Placement,
        "shapes" => Shapes,
        "round2" => Round2,
        "round3" => Round3,
        _ => R0Fixture(row)
    };

    private static string R0Fixture(string id) => File.ReadAllText(Path.Combine(
        EvidenceContractTests.RepoRoot(), R0Fixtures, id + ".cs.txt"));

    private static async Task<(string Calor, List<string> Losses)> ConvertAsync(string source, Surface surface)
    {
        switch (surface)
        {
            case Surface.LibraryDefault:
                return Library(new ConversionOptions());
            case Surface.CliDefault or Surface.CliPassthrough:
                return Library(ConvertCommand.BuildCSharpToCalorOptions(
                    benchmark: false, verbose: false, explain: false, noFallback: false,
                    passthrough: surface == Surface.CliPassthrough, explicitCallClosers: false));
            case Surface.McpDefault or Surface.McpPassthroughOnError or Surface.McpPassthroughOnErrorModuleName:
            {
                var args = new Dictionary<string, object> { ["source"] = source };
                if (surface != Surface.McpDefault)
                    args["passthroughOnError"] = true;
                if (surface == Surface.McpPassthroughOnErrorModuleName)
                    args["moduleName"] = "Custom";
                var result = await new ConvertTool().ExecuteAsync(
                    JsonDocument.Parse(JsonSerializer.Serialize(args)).RootElement);
                Assert.False(result.IsError, result.Content[0].Text);
                using var payload = JsonDocument.Parse(result.Content[0].Text!);
                var root = payload.RootElement;
                var losses = root.GetProperty("lossSummary").GetProperty("locations").EnumerateArray()
                    .Select(location => location.GetProperty("feature").GetString()!).ToList();
                return (root.GetProperty("calorSource").GetString()!, losses);
            }
            default:
            {
                var dir = Path.Combine(Path.GetTempPath(), "calor-f5-1132-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                try
                {
                    File.WriteAllText(Path.Combine(dir, "Fixture.cs"), source);
                    var migrator = new ProjectMigrator(new MigrationPlanOptions
                    {
                        Parallel = false,
                        SkipAnalyze = true,
                        SkipVerify = true,
                        PassthroughOnError = surface == Surface.ProjectMigratePassthrough
                    });
                    var plan = await migrator.CreatePlanAsync(dir, MigrationDirection.CSharpToCalor);
                    var report = await migrator.ExecuteAsync(plan);
                    var file = Assert.Single(report.FileResults);
                    Assert.NotEqual(FileMigrationStatus.Failed, file.Status);
                    return (File.ReadAllText(file.OutputPath!), file.Losses.Select(loss => loss.Feature).ToList());
                }
                finally
                {
                    try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
                }
            }
        }

        (string, List<string>) Library(ConversionOptions options)
        {
            var result = new CSharpToCalorConverter(options).Convert(source);
            Assert.True(result.Success, string.Join(Environment.NewLine, result.Issues));
            return (result.CalorSource!, result.Losses.Select(loss => loss.Feature).ToList());
        }
    }

    private static string Run(string csharp)
    {
        var compilation = CSharpCompilation.Create("F5_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(csharp)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emission = compilation.Emit(stream);
        Assert.True(emission.Success, string.Join(Environment.NewLine, emission.Diagnostics) + "\n" + csharp);
        stream.Position = 0;
        var context = new AssemblyLoadContext(compilation.AssemblyName!, isCollectible: true);
        try
        {
            var probe = context.LoadFromStream(stream).GetTypes().Single(type => type.Name == "Probe");
            var run = probe.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;
            return Assert.IsType<string>(run.Invoke(null, null));
        }
        finally
        {
            context.Unload();
        }
    }
}
