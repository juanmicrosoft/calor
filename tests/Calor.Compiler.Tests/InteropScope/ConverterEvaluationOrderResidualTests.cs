using Calor.Compiler.Migration;
using Xunit;
using Surface = Calor.Compiler.Tests.InteropScope.RefOutImplementationTests.Surface;

namespace Calor.Compiler.Tests.InteropScope;

/// <summary>
/// #1677: the evaluation-order residuals left after #1673 (shapes A–F), block lambdas that dropped
/// the hoisted statements of their enclosing statement, and compound assignments to static and
/// instance members, properties and indexers inside expression lambdas. Every row is converted on
/// the library, CLI and MCP surfaces, compiled, run, and compared with the original C#; a row also
/// states whether it must convert natively or be kept as C#.
/// </summary>
public class ConverterEvaluationOrderResidualTests
{
    private static IEnumerable<(string Id, string Source, string Allowed)> Rows()
    {
        yield return ("AUDIT-sibling-argument-read", "public class C { public int N() => 2; } public static class Probe { static int x = 1; static C F() { x = 10; return new C(); } static string Use(int a, int b) => a + \",\" + b; public static string Run() { return Use(x, F().N()); } }", "native-or-preserved");
        yield return ("AUDIT-sibling-binary-read", "public class C { public int N() => 2; } public static class Probe { static int x = 1; static C F() { x = 10; return new C(); } public static string Run() { return (x + F().N()).ToString(); } }", "native-or-preserved");
        yield return ("AUDIT-receiver-before-argument", "public class C { public int N() => 2; public int Use(int x) => x; } public static class Probe { static string log = \"\"; static C Receiver() { log += \"R\"; return new C(); } static C F() { log += \"F\"; return new C(); } public static string Run() { int n = 1 + Receiver().Use(F().N()); return log + n; } }", "native-or-preserved");
        yield return ("AUDIT-static-string-equals-null", "public static class Probe { public static string Run() { string a = null; return string.Equals(a, \"x\").ToString(); } }", "native-or-preserved");
        yield return ("AUDIT-named-substring", "public static class Probe { public static string Run() { return \"abcd\".Substring(length: 2, startIndex: 1); } }", "native-or-preserved");
        yield return ("AUDIT-static-string-equals-mode-null", "using System; public static class Probe { public static string Run() { string a = null; return string.Equals(a, \"x\", StringComparison.OrdinalIgnoreCase).ToString(); } }", "native-or-preserved");
        yield return ("AUDIT-constructor-arguments", "public class C { public int N() => 2; } public class Pair { public int A; public Pair(int a, int b) { A = a; } } public static class Probe { static int x = 1; static C F() { x = 10; return new C(); } public static string Run() { return new Pair(x, F().N()).A.ToString(); } }", "native-or-preserved");
        yield return ("AUDIT-named-constructor", "public class Pair { public int A; public Pair(int a, int b) { A = a; } } public static class Probe { public static string Run() { return new Pair(b: 2, a: 1).A.ToString(); } }", "native-or-preserved");
        yield return ("AUDIT-tuple-elements", "public class C { public int N() => 2; } public static class Probe { static int x = 1; static C F() { x = 10; return new C(); } public static string Run() { var p = (x, F().N()); return p.Item1.ToString(); } }", "native-or-preserved");
        yield return ("AUDIT-anonymous-elements", "public class C { public int N() => 2; } public static class Probe { static int x = 1; static C F() { x = 10; return new C(); } public static string Run() { var p = new { A = x, B = F().N() }; return p.A.ToString(); } }", "native-or-preserved");
        yield return ("AUDIT-array-elements", "public class C { public int N() => 2; } public static class Probe { static int x = 1; static C F() { x = 10; return new C(); } public static string Run() { var p = new[] { x, F().N() }; return p[0].ToString(); } }", "native-or-preserved");
        yield return ("AUDIT-shadowed-regex", "public static class Regex { public static bool IsMatch(string a, string b) => false; } public static class Probe { public static string Run() { return Regex.IsMatch(\"a\", \"a\").ToString(); } }", "native-or-preserved");
        yield return ("AUDIT-shadowed-char", "public static class Char { public static char ToUpper(char c) => 'x'; } public static class Probe { public static string Run() { return \"\" + Char.ToUpper('a'); } }", "native-or-preserved");
        yield return ("AUDIT-comparison-property", "public static class Fake { public static int Calls; public static System.StringComparison Ordinal { get { Calls++; return System.StringComparison.OrdinalIgnoreCase; } } } public static class Probe { public static string Run() { var same = \"a\".Equals(\"A\", Fake.Ordinal); return same + \":\" + Fake.Calls; } }", "native-or-preserved");
        yield return ("AUDIT-shadowed-stringbuilder", "public class StringBuilder { public override string ToString() => \"custom\"; } public static class Probe { public static string Run() { var b = new StringBuilder(); return b.ToString() ?? \"fallback\"; } }", "native-or-preserved");
        foreach (var type in new[] { "long", "short", "byte" })
            yield return ($"AUDIT-default-{type}", $"public static class Probe {{ static string Pick({type} n) => \"typed\"; static string Pick(int n) => \"int\"; public static string Run() => Pick(default({type})); }}", "native-or-preserved");
        yield return ("AUDIT-loop-chain", "public class C { public bool Value() => Probe.checks < 3; } public static class Probe { public static int checks; static C Next() { checks++; return new C(); } public static string Run() { int n = 0; while (Next().Value() && n < 3) n++; return checks + \":\" + n; } }", "native-or-preserved");
        foreach (var type in new[] { "short", "byte" })
            yield return ($"AUDIT-limit-{type}", $"public static class Probe {{ static string Pick({type} n) => \"typed\"; static string Pick(int n) => \"int\"; public static string Run() => Pick({type}.MaxValue); }}", "native-or-preserved");
        yield return ("AUDIT-shadowed-string-empty", "public static class String { public static string Empty => \"custom\"; } public static class Probe { public static string Run() => String.Empty ?? \"fallback\"; }", "native-or-preserved");
        yield return ("AUDIT-chain-named", "public class C { int a; public C Use(int a, int b) { this.a = a; return this; } public int N() => a; } public static class Probe { public static string Run() => new C().Use(b: 2, a: 1).N().ToString(); }", "native-or-preserved");
        yield return ("AUDIT-chain-siblings", "public class C { public C Use(int a, int b) { Probe.seen = a; return this; } public int N() => 2; } public static class Probe { static int x = 1; public static int seen; static C F() { x = 10; return new C(); } public static string Run() { int n = new C().Use(x, F().N()).N(); return n + \":\" + seen; } }", "native-or-preserved");
        yield return ("AUDIT-chain-base-receiver", "public class C { public C Use(int x) => this; public int N() => 2; } public static class Probe { static string log = \"\"; public static C R { get { log += \"R\"; return new C(); } } static C F() { log += \"F\"; return new C(); } public static string Run() { int n = R.Use(F().N()).N(); return log + n; } }", "native-or-preserved");
        yield return ("AUDIT-pattern-subject", "public static class Probe { static int calls; static int Next() { calls++; return 1; } public static string Run() { bool yes = Next() is > 0 and < 3; return yes + \":\" + calls; } }", "native-or-preserved");
        yield return ("AUDIT-null-pattern-operator", "public class C { public static bool operator ==(C a, C b) => true; public static bool operator !=(C a, C b) => false; } public static class Probe { public static string Run() { var c = new C(); return (c is null).ToString(); } }", "native-or-preserved");
        yield return ("AUDIT-nan-pattern", "public static class Probe { public static string Run() { double n = 0.0 / 0.0; return (n is System.Double.NaN).ToString(); } }", "native-or-preserved");
        yield return ("AUDIT-collection-interface", "using System.Collections.Generic; public static class Probe { public static string Run() { IEnumerable<int> xs = [1, 2]; return xs.GetType().Name; } }", "native-or-preserved");
        yield return ("AUDIT-collection-siblings", "public class C { public int N() => 2; } public static class Probe { static int x = 1; static C F() { x = 10; return new C(); } public static string Run() { int[] xs = [1, x, F().N()]; return xs[1].ToString(); } }", "native-or-preserved");
        yield return ("AUDIT-mixed-spread", "using System.Collections.Generic; public static class Probe { static string log = \"\"; static IEnumerable<int> Items() { log += \"S\"; yield return 1; } static int Bump() { log += \"B\"; return 2; } public static string Run() { int[] xs = [0, ..Items(), 0, Bump()]; return log + xs.Length; } }", "native-or-preserved");
        yield return ("AUDIT-stackalloc-siblings", "using System; public class C { public int N() => 2; } public static class Probe { static int x = 1; static C F() { x = 10; return new C(); } public static string Run() { Span<int> xs = stackalloc int[] { x, F().N() }; return \"\" + xs[0]; } }", "native-or-preserved");
        yield return ("AUDIT-query-join-order", "using System.Linq; public class C { public int[] Items() => new[] { 1 }; } public static class Probe { static string log = \"\"; static int[] Source() { log += \"S\"; return new[] { 1 }; } static C Factory() { log += \"F\"; return new C(); } public static string Run() { var q = from x in Source() join y in Factory().Items() on x equals y select x; return log + \":\" + string.Join(\",\", q); } }", "native-or-preserved");
        yield return ("AUDIT-index-receiver", "public class C { public int N() => 0; } public static class Probe { static string log = \"\"; static int[] A() { log += \"A\"; return new[] { 1 }; } static C F() { log += \"F\"; return new C(); } public static string Run() { int n = A()[F().N()]; return log + n; } }", "native-or-preserved");
        yield return ("AUDIT-with-siblings", "public record C(int A, int B); public class Helper { public int N() => 2; } public static class Probe { static int x = 1; static Helper F() { x = 10; return new Helper(); } public static string Run() { var c = new C(0, 0); var d = c with { A = x, B = F().N() }; return \"\" + d.A; } }", "native-or-preserved");
        yield return ("AUDIT-positional-pattern", "public class C { public void Deconstruct(out int a, out int b) { a = 3; b = 4; } } public static class Probe { public static string Run() { var c = new C(); return (c is C(1, 2)).ToString(); } }", "native-or-preserved");
        yield return ("AUDIT-declaration-pattern", "public static class Probe { public static string Run() { object o = 1; bool yes = o is string s; return \"\" + yes; } }", "native-or-preserved");
        yield return ("AUDIT-declaration-pattern-match", "public static class Probe { static int calls; static object Next() { calls++; return \"abc\"; } public static string Run() { if (Next() is string s) return calls + \":\" + s.Length; return \"no\"; } }", "native-or-preserved");
        yield return ("AUDIT-var-pattern-type", "public static class Probe { static string Pick(int n) => \"int\"; static string Pick(object n) => \"object\"; public static string Run() { int n = 1; if (n is var v) return Pick(v); return \"no\"; } }", "native-or-preserved");
        yield return ("AUDIT-property-pattern", "public class C { public int hits; public int P { get { hits++; return hits; } } } public static class Probe { public static string Run() { var c = new C(); bool yes = c is { P: > 0 and < 2 }; return yes + \":\" + c.hits; } }", "native-or-preserved");
        // ---- A: a staged call's own user-defined conversion runs before the later staged call. ----
        yield return ("A-staged-call-user-conversion", "public class W { public static implicit operator int(W w) { Probe.log += \"I\"; return 1; } } public static class Probe { public static string log = \"\"; static W A() { log += \"A\"; return new W(); } static int F() { log += \"F\"; return 5; } public static string Run() { string r; if (A() < F()) r = \"T\"; else r = \"E\"; return r + log; } }", "native-or-preserved");
        yield return ("A-staged-call-user-conversion-right", "public class W { public static implicit operator int(W w) { Probe.log += \"I\"; return 1; } } public static class Probe { public static string log = \"\"; static int A() { log += \"A\"; return 0; } static W F() { log += \"F\"; return new W(); } public static string Run() { string r; if (A() < F()) r = \"T\"; else r = \"E\"; return r + log; } }", "native-or-preserved");
        yield return ("A-staged-call-control", "public static class Probe { public static string log = \"\"; static int A() { log += \"A\"; return 1; } static int F() { log += \"F\"; return 5; } public static string Run() { string r; if (A() < F()) r = \"T\"; else r = \"E\"; return r + log; } }", "native");

        // ---- B: code in a nested type has not initialized its enclosing type. ----
        yield return ("B-nested-type-static-ctor", "public class B { public static int V; static B() { Probe.x = 10; } public static class N { public static string Go() { int y = B.V + Probe.x++; return Probe.x + \":\" + y; } } } public static class Probe { public static int x = 1; public static string Run() => B.N.Go(); }", "native-or-preserved");
        yield return ("B-nested-type-local-postfix", "public class B { public static int V; static B() { Probe.hits++; } public static class N { public static string Go() { int x = 1; try { int y = B.V + x++; } catch (System.Exception) { } return Probe.hits + \":\" + x; } } } public static class Probe { public static int hits; public static string Run() => B.N.Go(); }", "native-or-preserved");
        yield return ("B-own-type-control", "public class B { public static int V; static B() { Probe.x = 10; } public static string Go() { int y = B.V + Probe.x++; return Probe.x + \":\" + y; } } public static class Probe { public static int x = 1; public static string Run() => B.Go(); }", "native-or-preserved");

        // ---- C: a user-defined conversion nested inside index arithmetic runs once. ----
        yield return ("C-index-arithmetic-conversion", "public struct Key { public static implicit operator int(Key k) { Probe.n++; return 0; } } public static class Probe { public static int n; public static string Run() { var a = new int[] { 1 }; Key k = new Key(); a[k + 0] += 5; a[k + 0]++; a[0 + k] -= 1; return n + \":\" + a[0]; } }", "native-or-preserved");
        yield return ("C-index-arithmetic-cast", "public struct Key { public static explicit operator int(Key k) { Probe.n++; return 0; } } public static class Probe { public static int n; public static string Run() { var a = new int[] { 1 }; Key k = new Key(); a[(int)k * 1] += 5; return n + \":\" + a[0]; } }", "native-or-preserved");
        yield return ("C-index-arithmetic-control", "public static class Probe { public static string Run() { var a = new int[] { 1, 2 }; int k = 0; a[k + 1] += 5; a[k + 1]++; return a[0] + \":\" + a[1]; } }", "native");

        // ---- D: a postfix inside the target's own index. ----
        yield return ("D-postfix-in-own-index", "public static class Probe { public static string Run() { int x = 10; var a = new int[30]; a[x + x++] += 5; return a[20] + \":\" + a[21] + \":\" + x; } }", "native-or-preserved");
        yield return ("D-postfix-in-own-index-simple", "public static class Probe { public static string Run() { int x = 10; var a = new int[30]; a[x + x++] = 5; return a[20] + \":\" + a[21] + \":\" + x; } }", "native-or-preserved");
        yield return ("D-postfix-in-own-index-read", "public static class Probe { public static string Run() { int x = 3; var a = new int[] { 0, 10, 20, 30, 40 }; a[x++] += a[x]; return a[3] + \":\" + x; } }", "native-or-preserved");
        yield return ("D-postfix-in-own-index-control", "public static class Probe { public static string Run() { int x = 1; var a = new int[] { 10, 20, 30 }; a[x++] += 5; a[x++]++; return a[1] + \":\" + a[2] + \":\" + x; } }", "native");

        // ---- E: the key's conversion runs before a collection-initializer value. ----
        yield return ("E-put-collection-initializer", "using System.Collections.Generic; public struct Key { public static implicit operator int(Key k) { Probe.log += \"I\"; return 0; } } public static class Probe { public static string log = \"\"; static int F() { log += \"F\"; return 1; } public static string Run() { var a = new List<int>[1]; var k = new Key(); a[k] = new List<int> { F() }; return log + a[0][0]; } }", "native-or-preserved");
        yield return ("E-put-collection-initializer-key-changes", "using System.Collections.Generic; public static class Probe { static int k; static int F() { k = 1; return 7; } public static string Run() { var a = new List<int>[2]; a[k] = new List<int> { F() }; return (a[0] == null ? \"-\" : \"\" + a[0][0]) + \":\" + (a[1] == null ? \"-\" : \"\" + a[1][0]); } }", "native-or-preserved");
        yield return ("E-put-dictionary-initializer", "using System.Collections.Generic; public struct Key { public static implicit operator int(Key k) { Probe.log += \"I\"; return 0; } } public static class Probe { public static string log = \"\"; static int F() { log += \"F\"; return 1; } public static string Run() { var a = new Dictionary<int, int>[1]; var k = new Key(); a[k] = new Dictionary<int, int> { { 1, F() } }; return log + a[0][1]; } }", "native-or-preserved");
        yield return ("E-put-dictionary-index-initializer", "using System.Collections.Generic; public struct Key { public static implicit operator int(Key k) { Probe.log += \"I\"; return 0; } } public static class Probe { public static string log = \"\"; static int F() { log += \"F\"; return 1; } public static string Run() { var a = new Dictionary<int, int>[1]; var k = new Key(); a[k] = new Dictionary<int, int> { [1] = F() }; return log + a[0][1]; } }", "native-or-preserved");
        yield return ("E-put-set-initializer", "using System.Collections.Generic; public struct Key { public static implicit operator int(Key k) { Probe.log += \"I\"; return 0; } } public static class Probe { public static string log = \"\"; static int F() { log += \"F\"; return 1; } public static string Run() { var a = new HashSet<int>[1]; var k = new Key(); a[k] = new HashSet<int> { F() }; return log + a[0].Count; } }", "native-or-preserved");
        yield return ("E-put-dictionary-receiver-changes", "using System.Collections.Generic; public static class Probe { static Dictionary<int, int>[] a = new Dictionary<int, int>[1], other = new Dictionary<int, int>[1]; static int F() { a = other; return 7; } public static string Run() { var first = a; a[0] = new Dictionary<int, int> { { 1, F() } }; return (first[0] == null ? \"-\" : \"f\") + (other[0] == null ? \"-\" : \"o\"); } }", "native-or-preserved");
        yield return ("E-put-list-capacity", "using System.Collections.Generic; public struct Key { public static implicit operator int(Key k) { Probe.log += \"I\"; return 0; } } public static class Probe { public static string log = \"\"; static int F() { log += \"F\"; return 4; } public static string Run() { var a = new List<int>[1]; var k = new Key(); a[k] = new List<int>(F()); return log + a[0].Capacity; } }", "native-or-preserved");
        yield return ("E-put-dictionary-capacity", "using System.Collections.Generic; public struct Key { public static implicit operator int(Key k) { Probe.log += \"I\"; return 0; } } public static class Probe { public static string log = \"\"; static int F() { log += \"F\"; return 4; } public static string Run() { var a = new Dictionary<int, int>[1]; var k = new Key(); a[k] = new Dictionary<int, int>(F()); return log + a[0].Count; } }", "native-or-preserved");
        yield return ("E-put-collection-initializer-control", "using System.Collections.Generic; public static class Probe { public static string log = \"\"; static int F() { log += \"F\"; return 1; } public static string Run() { var d = new Dictionary<string, List<int>>(); d[\"k\"] = new List<int> { F() }; return log + d[\"k\"][0]; } }", "native");

        // ---- Emitter hoists of §NEW arguments (found while checking E). ----
        yield return ("N-new-arg-member-store", "public class C { public int V; public C(int v) { V = v; } } public class B { public C F; } public static class Probe { static B b = new B(), other = new B(); static int G() { b = other; return 1; } public static string Run() { var first = b; b.F = new C(G()); return (first.F == null ? \"-\" : \"f\") + (other.F == null ? \"-\" : \"o\"); } }", "native-or-preserved");
        yield return ("N-new-arg-compound", "public class C { public int V; public C(int v) { V = v; } } public static class Probe { static int x = 1; static int G() { x = 10; return 2; } public static string Run() { x += new C(G()).V; return x.ToString(); } }", "native-or-preserved");
        yield return ("N-new-arg-call-argument", "public class C { public int V; public C(int v) { V = v; } } public static class Probe { static int x = 1; static int G() { x = 10; return 2; } static string Use(int a, C c) => a + \",\" + c.V; public static string Run() { return Use(x, new C(G())); } }", "native-or-preserved");
        yield return ("N-new-arg-call-statement", "public class C { public int V; public C(int v) { V = v; } } public static class Probe { static int x = 1; static string log = \"\"; static int G() { x = 10; return 2; } static void Use(int a, C c) { log += a + \",\" + c.V; } public static string Run() { Use(x, new C(G())); return log; } }", "native-or-preserved");
        yield return ("N-new-arg-local", "public class C { public int V; public C(int v) { V = v; } } public static class Probe { static int x = 1; static int G() { x = 10; return 2; } public static string Run() { int y = x + new C(G()).V; return y.ToString(); } }", "native-or-preserved");

        // ---- F: a struct field path under a reference receiver throws before the right-hand side. ----
        yield return ("F-struct-path-null-receiver", "public struct S { public int X; } public class H { public S s; } public class C { public int N() => 2; } public static class Probe { static int calls; static H h; static C F() { calls++; return new C(); } public static string Run() { try { h.s.X = F().N(); } catch (System.NullReferenceException) { } return calls.ToString(); } }", "native-or-preserved");
        yield return ("F-struct-array-element-bounds", "public struct S { public int X; } public class C { public int N() => 2; } public static class Probe { static int calls; static S[] a = new S[0]; static int i; static C F() { calls++; return new C(); } public static string Run() { try { a[i].X = F().N(); } catch (System.IndexOutOfRangeException) { } return calls.ToString(); } }", "native-or-preserved");
        yield return ("F-struct-path-receiver-reassigned", "public struct S { public int X; } public class H { public S s; } public class C { public int N() => 2; } public static class Probe { static H h = new H(), other = new H(); static C F() { h = other; return new C(); } public static string Run() { var first = h; h.s.X = F().N(); return first.s.X + \":\" + other.s.X; } }", "native-or-preserved");
        yield return ("F-class-field-null-control", "public class H { public int X; } public class C { public int N() => 2; } public static class Probe { static int calls; static H h; static C F() { calls++; return new C(); } public static string Run() { try { h.X = F().N(); } catch (System.NullReferenceException) { } return calls.ToString(); } }", "native-or-preserved");

        // ---- Block lambdas keep the hoisted statements of their enclosing statement. ----
        yield return ("L-block-lambda-chain-receiver", "using System; public class C { public C Next() { Probe.log += \"n\"; return this; } public int Apply(Func<int, int> f) => f(1); } public static class Probe { public static string log = \"\"; public static string Run() { var c = new C(); int r = c.Next().Apply(v => { log += \"b\"; return v + 1; }); return log + r; } }", "native-or-preserved");
        yield return ("L-block-lambda-chain-statement", "using System; public class C { public C Next() { Probe.log += \"n\"; return this; } public void Apply(Action<int> f) { f(1); } } public static class Probe { public static string log = \"\"; public static string Run() { var c = new C(); c.Next().Apply(v => { log += \"b\" + v; }); c.Next().Apply(delegate (int v) { log += \"d\" + v; }); return log; } }", "native-or-preserved");
        yield return ("L-block-lambda-postfix", "using System; public static class Probe { static string Use(int a, Func<int> f) => a + \",\" + f(); public static string Run() { int y = 1; string r = Use(y++, () => { return 10; }); return r + \":\" + y; } }", "native-or-preserved");
        yield return ("L-block-lambda-compound-rhs", "using System; public class C { public int N() => 3; public int Apply(Func<int, int> f) => f(2); } public static class Probe { static int x = 1; static C F() { x = 10; return new C(); } public static string Run() { x += F().Apply(v => { return v * 2; }); return x.ToString(); } }", "native-or-preserved");
        yield return ("L-block-lambda-inner-hoist", "using System; public class C { public C Next() { Probe.log += \"n\"; return this; } public int Val() => 4; } public static class Probe { public static string log = \"\"; public static string Run() { var c = new C(); Func<int> f = () => { return c.Next().Val(); }; log += \"x\"; return log + f() + log; } }", "native");
        yield return ("L-block-lambda-local-declaration", "using System; using System.Linq; public class C { public C Next() { Probe.log += \"n\"; return this; } public int[] Items() => new[] { 1, 2, 3 }; } public static class Probe { public static string log = \"\"; public static string Run() { var c = new C(); var total = c.Next().Items().Select(v => { return v * 2; }).Sum(); return log + total; } }", "native-or-preserved");

        yield return ("L-block-lambda-argument-after-chain", "using System; public class C { public C When(Func<int, bool> p, Action a) { Probe.log += \"w\"; if (p(1)) a(); return this; } public void Otherwise(Action a) { Probe.log += \"o\"; a(); } } public static class Probe { public static string log = \"\"; public static string Run() { var c = new C(); c.When(x => x > 0, () => { log += \"a\"; }).Otherwise(() => { log += \"b\"; }); return log; } }", "native");
        yield return ("L-chain-receiver-runs-once", "using System; public class C { public C Next() { Probe.log += \"n\"; return this; } public C Use(Func<int, int> f) { Probe.log += \"u\" + f(1); return this; } public void Done(int v) { Probe.log += \"d\" + v; } } public static class Probe { public static string log = \"\"; static int F() { log += \"F\"; return 3; } public static string Run() { var c = new C(); c.Next().Use(v => v + 1).Done(F()); c.Next().Use(v => { return v * 5; }).Done(4); return log; } }", "native-or-preserved");

        yield return ("L-chain-in-expression-runs-once", "using System; public class C { public C Next() { Probe.log += \"n\"; return this; } public int Val() { Probe.log += \"v\"; return 2; } } public static class Probe { public static string log = \"\"; public static string Run() { var c = new C(); int r = c.Next().Next().Val(); Func<int> f = () => c.Next().Next().Val(); r += f(); return log + r; } }", "native-or-preserved");

        // ---- Codex review round 1. ----
        yield return ("R1-tuple-element-hoist", "public static class Probe { static int x = 1; static int Bump() { x = 10; return 2; } static int Use((int, int) t) => t.Item1; public static string Run() { x += Use((Bump() + 0, 0)); return x.ToString(); } }", "native-or-preserved");
        yield return ("R1-range-bound-hoist", "public static class Probe { static int x = 1; static int Bump() { x = 10; return 1; } static int Use(System.Range r) => r.Start.Value; public static string Run() { x += Use(Bump()..3); return x.ToString(); } }", "native-or-preserved");
        yield return ("R1-condition-conversion-hoist", "public class W { public static implicit operator int(W w) { Probe.log += \"I\"; return 1; } } public class C { public int N() { Probe.log += \"N\"; return 5; } } public static class Probe { public static string log = \"\"; static W A() { log += \"A\"; return new W(); } static C F() { log += \"F\"; return new C(); } public static string Run() { if (A() < F().N()) log += \"T\"; return log; } }", "native-or-preserved");
        yield return ("R1-indexer-then-call-lambda", "using System; public class C { public C Next() { Probe.log += \"N\"; return this; } public int Index() => 0; public int Val() => 4; } public static class Probe { public static string log = \"\"; public static string Run() { var c = new C(); var a = new C[] { c }; Func<int> f = () => a[c.Next().Index()].Val(); int r = f(); return log + r; } }", "native-or-preserved");
        yield return ("R1-static-struct-field-store", "public struct S { public int X; } public class H { public static S s; static H() { Probe.log += \"H\"; } } public class C { public int N() => 2; } public static class Probe { public static string log = \"\"; static C F() { log += \"F\"; return new C(); } public static string Run() { H.s.X = F().N(); return log; } }", "native-or-preserved");
        yield return ("R1-static-field-in-index", "public class K { public static int V; static K() { Probe.a = new int[] { 10 }; } } public static class Probe { public static int[] a = new int[] { 1 }; public static string Run() { var first = a; a[K.V + 0] += 5; return first[0] + \":\" + a[0]; } }", "native-or-preserved");
        yield return ("R1-string-concat-in-index", "using System.Collections.Generic; public class Key { public override string ToString() { Probe.calls++; return \"k\"; } } public static class Probe { public static int calls; public static string Run() { var d = new Dictionary<string, int>(); d[\"k\"] = 1; var k = new Key(); d[\"\" + k] += 5; return calls + \":\" + d[\"k\"]; } }", "native-or-preserved");
        yield return ("R1-receiver-before-argument", "public class C { public C Next() { Probe.log += \"N\"; return this; } public int Val() { Probe.log += \"V\"; return 1; } public int Use(int n) { Probe.log += \"U\"; return n; } } public static class Probe { public static string log = \"\"; static C F() { log += \"F\"; return new C(); } public static string Run() { var c = new C(); int x = c.Next().Use(F().Val()); c.Next().Use(F().Val()); return log + x; } }", "native-or-preserved");

        yield return ("L-using-resource-hoist", "using System; public class D : IDisposable { public void Dispose() { Probe.log += \"d\"; } } public static class Probe { public static string log = \"\"; static D Open(Action<int> a) { a(1); return new D(); } public static string Run() { using (Open(v => log += \"o\" + v)) { log += \"b\"; } using (Open(v => { log += \"p\" + v; })) log += \"c\"; return log; } }", "native");

        // ---- Expression lambdas: compound assignments to members, properties and indexers. ----
        yield return ("X-lambda-static-field", "using System; public class Point { public static string Log = \"\"; } public static class Probe { public static string Run() { Action<int> a = n => Point.Log += \"x\"; a(1); a(2); return Point.Log; } }", "native");
        yield return ("X-lambda-static-field-int", "using System; public class Point { public static int Count = 1; } public static class Probe { public static string Run() { Action<int> a = n => Point.Count += n; Action<int> m = n => Point.Count *= n; a(4); m(3); a(1); return Point.Count.ToString(); } }", "native");
        yield return ("X-lambda-static-property", "using System; public class Point { static int p = 1; public static int P { get { Probe.log += \"g\"; return p; } set { Probe.log += \"s\"; p = value; } } } public static class Probe { public static string log = \"\"; public static string Run() { Action<int> a = n => Point.P += n; a(4); a(1); return log + Point.P; } }", "native");
        yield return ("X-lambda-instance-field", "using System; public class Point { public int X = 1; } public static class Probe { public static string Run() { var p = new Point(); Action<int> a = n => p.X += n; Action<int> s = n => p.X -= n; a(5); s(2); a(10); return p.X.ToString(); } }", "native");
        yield return ("X-lambda-instance-property", "using System; public class Point { int x = 1; public int X { get { Probe.log += \"g\"; return x; } set { Probe.log += \"s\"; x = value; } } } public static class Probe { public static string log = \"\"; public static string Run() { var p = new Point(); Action<int> a = n => p.X += n; a(5); a(1); return log + p.X; } }", "native");
        yield return ("X-lambda-this-member", "using System; public class Probe { int total = 1; string text = \"\"; void Go() { Action<int> a = n => total += n; Action<int> t = n => this.text += n; a(5); t(3); t(4); a(2); } public static string Run() { var p = new Probe(); p.Go(); return p.total + \":\" + p.text; } }", "native");
        yield return ("X-lambda-array-indexer", "using System; public static class Probe { public static string Run() { var arr = new int[] { 1, 2 }; Action<int> a = n => arr[1] += n; Action<int> b = n => arr[n] *= 3; a(5); b(0); b(1); return arr[0] + \":\" + arr[1]; } }", "native");
        yield return ("X-lambda-list-indexer", "using System; using System.Collections.Generic; public static class Probe { public static string Run() { var l = new List<int> { 1, 2 }; Action<int> a = n => l[n] += 10; a(0); a(1); a(1); return l[0] + \":\" + l[1]; } }", "native");
        yield return ("X-lambda-dictionary-indexer", "using System; using System.Collections.Generic; public static class Probe { public static string Run() { var d = new Dictionary<string, int> { [\"k\"] = 1 }; Action<string> a = k => d[k] += 5; a(\"k\"); a(\"k\"); return \"\" + d[\"k\"]; } }", "native-or-preserved");
        yield return ("X-lambda-increment-members", "using System; public class Point { public static int S; public int X; } public static class Probe { public static string Run() { var p = new Point(); Action a = () => Point.S++; Action b = () => p.X--; Action c = () => ++Point.S; a(); a(); b(); c(); return Point.S + \":\" + p.X; } }", "native");
        yield return ("X-lambda-func-value", "using System; public class Point { public static int S = 1; } public static class Probe { public static string Run() { Func<int, int> f = n => Point.S += n; int r = f(4); return r + \":\" + Point.S; } }", "native-or-preserved");
        yield return ("X-lambda-hoisted-rhs", "using System; public class C { public int N() => 3; } public class Point { public static int S = 1; } public static class Probe { static C F() { Point.S = 100; return new C(); } public static string Run() { Action<int> a = n => Point.S += F().N(); a(0); return Point.S.ToString(); } }", "native");
        yield return ("AUDIT-query-identity-runtime-type", "using System.Linq; public static class Probe { public static string Run() { int[] xs = { 1, 2 }; var q = from x in xs select x; return (q is int[]) + \":\" + q.Sum(); } }", "native-or-preserved");
        yield return ("AUDIT-query-identity-custom-select", "using System; public class Input { public int Value; public Input(int value) { Value = value; } public Input Select(Func<int, int> selector) { Probe.log += \"S\"; return new Input(selector(Value) + 1); } } public static class Probe { public static string log = \"\"; public static string Run() { var q = from x in new Input(2) select x; return log + q.Value; } }", "native-or-preserved");
        yield return ("AUDIT-using-multiple-dispose", "using System; public class D : IDisposable { string name; public D(string n) { name = n; Probe.log += n; } public void Dispose() { Probe.log += name; } } public static class Probe { public static string log = \"\"; public static string Run() { using (D a = new D(\"a\"), b = new D(\"b\")) { log += \"B\"; } return log; } }", "native");
        yield return ("AUDIT-using-multiple-constructor-throws", "using System; public class D : IDisposable { public void Dispose() { Probe.log += \"d\"; } } public static class Probe { public static string log = \"\"; static D Fail() { log += \"X\"; throw new InvalidOperationException(); } public static string Run() { try { using (D a = new D(), b = Fail()) { log += \"B\"; } } catch (InvalidOperationException) { log += \"C\"; } return log; } }", "native");
        yield return ("AUDIT-using-multiple-hoisted-resource", "using System; public class D : IDisposable { string name; public D(string n) { name = n; Probe.log += n; } public D Next() { Probe.log += \"n\"; return this; } public void Dispose() { Probe.log += name; } } public static class Probe { public static string log = \"\"; static D Make() { log += \"m\"; return new D(\"b\"); } public static string Run() { using (D a = new D(\"a\"), b = Make().Next()) { log += \"B\"; } return log; } }", "native");
        yield return ("AUDIT-query-group-identity-runtime-type", "using System.Linq; public static class Probe { public static string Run() { int[] xs = { 1, 1 }; var q = from x in xs group x by x; return q.GetType().Name; } }", "native-or-preserved");
        yield return ("AUDIT-query-group-custom-overload", "using System; public class Input { public int Value; public Input(int value) { Value = value; } public Input GroupBy(Func<int, int> key) { Probe.log += \"K\"; return this; } public Input GroupBy(Func<int, int> key, Func<int, int> element) { Probe.log += \"E\"; return this; } } public static class Probe { public static string log = \"\"; public static string Run() { var q = from x in new Input(2) group x by x; return log + q.Value; } }", "native-or-preserved");
        yield return ("AUDIT-query-parenthesized-identity", "using System.Linq; public static class Probe { public static string Run() { int[] xs = { 1, 2 }; var q = from x in xs where x > 0 select (x); return q.GetType().Name; } }", "native-or-preserved");
        yield return ("AUDIT-query-group-parenthesized", "using System.Linq; public static class Probe { public static string Run() { int[] xs = { 1, 1 }; var q = from x in xs group (x) by x; return q.GetType().Name; } }", "native-or-preserved");
        yield return ("AUDIT-query-escaped-identity", "using System; public class Input { public int Value; public Input(int value) { Value = value; } public Input Where(Func<int, bool> predicate) { Probe.log += \"W\"; return this; } public Input Select(Func<int, int> selector) { Probe.log += \"S\"; return this; } } public static class Probe { public static string log = \"\"; public static string Run() { var q = from @event in new Input(2) where @event > 0 select @event; return log + q.Value; } }", "native-or-preserved");
    }

    public static TheoryData<string, Surface> ConversionRows()
    {
        var data = new TheoryData<string, Surface>();
        foreach (var (id, _, _) in Rows())
            foreach (var surface in Enum.GetValues<Surface>())
                data.Add(id, surface);
        return data;
    }

    [Theory]
    [MemberData(nameof(ConversionRows))]
    public async Task Conversion_MatchesTheOriginal(string id, Surface surface)
    {
        var (_, source, allowed) = Rows().Single(row => row.Id == id);
        var original = RefOutImplementationTests.Run(source);
        var (calor, losses) = await RefOutImplementationTests.ConvertAsync(source, surface);
        var interop = calor.Contains("§CSHARP{", StringComparison.Ordinal) || calor.Contains("§CS{", StringComparison.Ordinal)
            || calor.Contains("§RAW", StringComparison.Ordinal) || losses.Count > 0;
        var strict = Program.Compile(calor, id + ".calr", new CompilationOptions { StatusWriter = TextWriter.Null });
        // As in ConverterCompoundAssignmentTests: an effects-only refusal is compiled without
        // enforcement, since the comparison is about values.
        var relaxed = strict.HasErrors && strict.Diagnostics.Errors.All(e => e.Code.StartsWith("Calor04", StringComparison.Ordinal))
            ? Program.Compile(calor, id + ".calr", new CompilationOptions { StatusWriter = TextWriter.Null, EnforceEffects = false })
            : strict;
        Assert.False(relaxed.HasErrors, $"{id} on {surface}: {string.Join("; ", relaxed.Diagnostics.Errors)}\n{calor}");
        var converted = Task.Run(() => RefOutImplementationTests.Run(relaxed.GeneratedCode));
        var finished = await Task.WhenAny(converted, Task.Delay(TimeSpan.FromSeconds(30)));
        Assert.True(finished == converted, $"{id} on {surface}: the converted program did not finish\n{calor}");
        Assert.True(original == await converted, $"{id} on {surface}: C# gives {original}, the conversion gives {await converted}\n{calor}");
        Assert.True(allowed.Split("-or-").Contains(interop ? "preserved" : "native"), $"{id} on {surface}: expected {allowed}, losses [{string.Join(", ", losses)}]\n{calor}");
    }

    // Each shape's outcome: kept as C# with the named loss, or native (null) with no loss.
    [Theory]
    [InlineData("A-staged-call-user-conversion", null)]
    [InlineData("A-staged-call-control", null)]
    [InlineData("B-nested-type-static-ctor", null)]
    [InlineData("B-nested-type-local-postfix", null)]
    [InlineData("C-index-arithmetic-conversion", null)]
    [InlineData("C-index-arithmetic-cast", null)]
    [InlineData("D-postfix-in-own-index", null)]
    [InlineData("D-postfix-in-own-index-simple", null)]
    [InlineData("D-postfix-in-own-index-control", null)]
    [InlineData("E-put-collection-initializer", "initializer-interop")]
    [InlineData("E-put-list-capacity", "compound-assignment-order")]
    [InlineData("E-put-dictionary-capacity", "compound-assignment-order")]
    [InlineData("E-put-dictionary-initializer", "dictionary-initializer")]
    [InlineData("E-put-collection-initializer-control", null)]
    [InlineData("F-struct-path-null-receiver", "compound-assignment-order")]
    [InlineData("F-struct-array-element-bounds", "compound-assignment-order")]
    [InlineData("F-class-field-null-control", null)]
    [InlineData("L-block-lambda-postfix", null)]
    [InlineData("L-block-lambda-chain-statement", null)]
    [InlineData("L-block-lambda-argument-after-chain", null)]
    [InlineData("L-using-resource-hoist", null)]
    [InlineData("X-lambda-func-value", "embedded-assignment")]
    [InlineData("X-lambda-increment-members", null)]
    [InlineData("X-lambda-static-field", null)]
    public void Shape_HasTheExpectedOutcome(string id, string? loss)
    {
        var result = new CSharpToCalorConverter(new ConversionOptions()).Convert(Rows().Single(r => r.Id == id).Source);
        var features = result.Losses.Select(l => l.Feature).Distinct().ToList();
        if (loss == null)
            Assert.Empty(features);
        else
            Assert.Equal([loss], features);
    }

    // #1677 (C): the index is bound once, so its conversion runs once.
    [Fact]
    public void IndexWithANestedConversion_IsBoundOnce()
    {
        var calor = Convert(Rows().Single(r => r.Id == "C-index-arithmetic-conversion").Source);
        Assert.Matches(@"§B\{(_asg\d+)\} \(\+ k 0\)\s+§ASSIGN §IDX\{[^}]+\} \1 \(\+ §IDX\{[^}]+\} \1 5\)", calor);
    }

    // #1677: `Point.Log += "x"` in an expression lambda keeps its operator and its target.
    [Fact]
    public void ExpressionLambda_StaticMemberCompound_KeepsTheOperator()
    {
        var calor = Convert(Rows().Single(r => r.Id == "X-lambda-static-field").Source);
        Assert.Contains("§ASSIGN Point.Log (+ Point.Log \"x\")", calor);
    }

    private static string Convert(string source)
        => new CSharpToCalorConverter(new ConversionOptions()).Convert(source).CalorSource!.Replace("\r\n", "\n");
}
