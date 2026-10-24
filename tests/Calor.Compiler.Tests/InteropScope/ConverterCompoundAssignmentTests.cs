using Calor.Compiler.Migration;
using Xunit;
using Surface = Calor.Compiler.Tests.InteropScope.RefOutImplementationTests.Surface;

namespace Calor.Compiler.Tests.InteropScope;

/// <summary>
/// #1633, #1632, #1634: compound assignments evaluate their target before the right-hand side,
/// hoisted statements land inside the body they belong to, and compound assignments on small
/// integral types keep C#'s implicit narrowing cast. Every row is converted on the library, CLI
/// and MCP surfaces, compiled, run, and compared with the original C#; a row also states whether
/// it must convert natively or be kept as C#.
/// </summary>
public class ConverterCompoundAssignmentTests
{
    // Logging probes: `log` records every call in the order C# runs it.
    private const string Log = "public static string log = \"\"; static int G() { log += \"G\"; return 5; } static int I() { log += \"I\"; return 1; } ";

    private static readonly (string Name, string Op)[] Operators =
    [
        ("add", "+="), ("sub", "-="), ("mul", "*="), ("div", "/="), ("mod", "%="),
        ("and", "&="), ("or", "|="), ("xor", "^="), ("shl", "<<="), ("shr", ">>="),
    ];

    // (C# type, initial value, a right operand of that type, a value that overflows on +=).
    private static readonly (string Type, string Initial, string Operand)[] SmallTypes =
    [
        ("byte", "250", "(byte)3"), ("sbyte", "120", "(sbyte)3"), ("short", "32760", "(short)3"),
        ("ushort", "65530", "(ushort)3"), ("char", "'\\uFFFA'", "'\\u0003'"),
    ];

    private static IEnumerable<(string Id, string Source, string Allowed)> Rows()
    {
        // ---- #1633: the target is read before the right-hand side runs, for every operator. ----
        foreach (var (name, op) in Operators)
        {
            yield return ($"O-{name}-hoisted-rhs", "public class C { public int N() => 3; } public static class Probe { static int x = 7; static C F() { x = 100; return new C(); } public static string Run() { x " + op + " F().N(); return x.ToString(); } }", "native");
            yield return ($"O-{name}-property", "public class C { public int N() => 3; } public static class Probe { public static string log = \"\"; static int p = 7; public static int P { get { log += \"g\"; return p; } set { log += \"s\" + value; p = value; } } static C F() { log += \"F\"; p = 100; return new C(); } public static string Run() { P " + op + " F().N(); return log; } }", "native");
            yield return ($"O-{name}-indexer-args", "public static class Probe { " + Log + "static int[] a = { 40, 40, 40 }; public static string Run() { a[I()] " + op + " G(); return log + a[0] + \",\" + a[1]; } }", "native");
        }
        yield return ("O-issue-repro", "public class C { public int N() => 2; } public static class Probe { static int x = 1; static C F() { x = 10; return new C(); } public static string Run() { x += F().N(); return x.ToString(); } }", "native");
        yield return ("O-local-postfix-rhs", "public static class Probe { public static string Run() { int x = 1; x += x++; int y = 1; y -= y--; return x + \":\" + y; } }", "native");
        yield return ("O-local-prefix-rhs", "public static class Probe { public static string Run() { int x = 1; x += ++x; return x.ToString(); } }", "native");
        yield return ("O-member-receiver-reassigned", "public class B { public int N; } public static class Probe { static B b = new B(), second = new B(); static int G() { b = second; return 5; } public static string Run() { var first = b; b.N += G(); return first.N + \":\" + second.N; } }", "native");
        yield return ("O-member-receiver-call", "public class B { public int N = 1; } public static class Probe { " + Log + "static B b = new B(); static B Get() { log += \"get\"; return b; } public static string Run() { Get().N += G(); Get().N++; Get().N *= 2; return log + b.N; } }", "native");
        yield return ("O-member-receiver-hoisted-rhs", "public class C { public int N() => 3; } public class B { public int V = 1; } public static class Probe { public static string log = \"\"; static B b = new B(); static B Get() { log += \"get\"; return b; } static C F() { log += \"F\"; b = new B { V = 50 }; return new C(); } public static string Run() { var first = b; Get().V += F().N(); return log + first.V + \":\" + b.V; } }", "native");
        yield return ("O-indexer-receiver-reassigned", "public static class Probe { static int[] a = { 1 }, other = { 100 }; static int G() { a = other; return 5; } public static string Run() { var first = a; a[0] += G(); return first[0] + \":\" + other[0]; } }", "native");
        yield return ("O-indexer-arg-reassigned", "public static class Probe { static int k = 0; static int[] a = { 1, 1 }; static int G() { k = 1; return 5; } public static string Run() { a[k] += G(); return a[0] + \":\" + a[1]; } }", "native");
        yield return ("O-list-indexer", "using System.Collections.Generic; public static class Probe { " + Log + "public static string Run() { var l = new List<int> { 1, 2 }; l[I()] += G(); l[I()] -= 1; return log + l[1]; } }", "native");
        yield return ("O-dictionary-indexer", "using System.Collections.Generic; public static class Probe { " + Log + "static string K() { log += \"K\"; return \"k\"; } public static string Run() { var d = new Dictionary<string, int> { [\"k\"] = 1 }; d[K()] += G(); return log + d[\"k\"]; } }", "native-or-preserved");
        yield return ("O-struct-local", "public struct S { public int X; } public static class Probe { " + Log + "public static string Run() { var s = new S(); s.X += G(); s.X++; return log + s.X; } }", "native");
        yield return ("O-struct-field-of-class", "public struct S { public int X; } public class H { public S s; } public static class Probe { static H h = new H(), other = new H(); static int G() { h = other; return 5; } public static string Run() { var first = h; h.s.X += G(); return first.s.X + \":\" + other.s.X; } }", "native-or-preserved");
        yield return ("O-struct-array-element", "public struct S { public int X; } public static class Probe { " + Log + "static S[] a = new S[2]; public static string Run() { a[I()].X += G(); return log + a[1].X; } }", "native-or-preserved");
        yield return ("O-string-concat", "public class C { public string N() => \"n\"; } public static class Probe { static string s = \"a\"; static C F() { s = \"z\"; return new C(); } public static string Run() { s += F().N(); return s; } }", "native");
        yield return ("O-increment-indexer-statements", "public static class Probe { " + Log + "static int[] a = { 0, 10 }; public static string Run() { a[I()]++; a[I()]--; ++a[I()]; --a[I()]; a[I()]++; return log + a[1]; } }", "native");
        yield return ("O-coalesce-hoisted-rhs", "#nullable enable\npublic class C { public string N() => \"n\"; } public static class Probe { public static string log = \"\"; static C F() { log += \"F\"; return new C(); } public static string Run() { string? s = \"x\"; s ??= F().N(); string? t = null; t ??= F().N(); return log + s + t; } }", "native-or-preserved");
        yield return ("O-coalesce-receiver-call", "#nullable enable\npublic class B { public string? S; } public static class Probe { public static string log = \"\"; static B b = new B(); static B Get() { log += \"get\"; return b; } public static string Run() { Get().S ??= \"v\"; Get().S ??= \"w\"; return log + b.S; } }", "native-or-preserved");
        yield return ("O-compound-as-value-hoisted-rhs", "public class C { public int N() => 3; } public static class Probe { static int x = 1; static C F() { x = 10; return new C(); } public static string Run() { int y = x += F().N(); return x + \":\" + y; } }", "preserved");
        yield return ("O-expression-bodied-method", "public class C { public int N() => 3; } public class Probe { int x = 1; C F() { x = 10; return new C(); } void Bump() => x += F().N(); public static string Run() { var p = new Probe(); p.Bump(); return p.x.ToString(); } }", "native-or-preserved");
        yield return ("O-expression-bodied-setter", "public class C { public int N() => 3; } public class Probe { int x = 1; C F() { x = 10; return new C(); } public int P { set => x += F().N() + value; } public static string Run() { var p = new Probe(); p.P = 0; return p.x.ToString(); } }", "native-or-preserved");
        yield return ("O-for-incrementor", "public class C { public int N() => 3; } public static class Probe { static int i; static C F() { i += 100; return new C(); } public static string Run() { string r = \"\"; for (i = 0; i < 400; i += F().N()) { r += i + \",\"; } return r; } }", "native-or-preserved");
        yield return ("O-event-control", "public class Probe { event System.Action? E; static string log = \"\"; static void H() { log += \"h\"; } public static string Run() { var p = new Probe(); p.E += H; p.E += H; p.E -= H; p.E?.Invoke(); return log; } }", "native-or-preserved");
        // Codex review round 1 reproducers.
        yield return ("R1-coalesce-getter-reassigns-receiver", "#nullable enable\npublic class B { public string? s; public string? S { get { Probe.b = Probe.other; return s; } set { s = value; } } } public static class Probe { public static B b = new B(), other = new B(); public static string Run() { var first = b; b.S ??= \"v\"; return (first.s ?? \"-\") + \":\" + (other.s ?? \"-\"); } }", "native-or-preserved");
        yield return ("R1-index-hoist-reassigns-array", "public class C { public int G() => 1; } public static class Probe { static int[] a = { 1, 1 }, other = { 100, 100 }; static C F() { a = other; return new C(); } public static string Run() { var first = a; a[F().G()] += 5; a[F().G()]++; return first[1] + \":\" + other[1]; } }", "native-or-preserved");
        yield return ("R1-expression-lambda-compound", "public static class Probe { public static string Run() { int total = 1; System.Action<int> add = x => total += x; System.Action<int> mul = x => total *= x; System.Action<int> shl = x => total <<= x; add(5); mul(3); shl(1); add(2); return total.ToString(); } }", "native");
        // Codex review round 2 reproducers.
        yield return ("R2-index-user-conversion", "public struct Key { public static implicit operator int(Key k) { Probe.n++; return 0; } } public static class Probe { public static int n; public static string Run() { var a = new byte[] { 1 }; Key k = new Key(); a[k] += 5; a[k]++; return n + \":\" + a[0]; } }", "native-or-preserved");
        yield return ("R2-read-user-conversion", "public class W { public int V; public static implicit operator int(W w) { Probe.log += \"I\"; return Probe.n; } public static implicit operator W(int v) { Probe.log += \"W\"; return new W { V = v }; } } public class C { public int N() => 2; } public static class Probe { public static int n = 1; public static string log = \"\"; static W x = new W(); static C F() { log += \"F\"; n = 10; return new C(); } public static string Run() { x += F().N(); return log + \":\" + x.V; } }", "native-or-preserved");
        yield return ("R2-staging-user-operator", "public class C { public static int operator +(C a, C b) { Probe.log += \"O\"; return 1; } } public static class Probe { public static string log = \"\"; static int F() { log += \"F\"; return 2; } public static string Run() { var a = new C(); var b = new C(); if (a + b < F()) log += \"T\"; return log; } }", "native-or-preserved");
        yield return ("R2-staging-division", "public static class Probe { public static string log = \"\"; static int F() { log += \"F\"; return 2; } public static string Run() { int z = 0; try { if (10 / z < F()) log += \"T\"; } catch (System.DivideByZeroException) { log += \"D\"; } return log; } }", "native-or-preserved");
        yield return ("R2-postfix-past-throw", "public static class Probe { public static string Run() { int x = 1; var a = new int[0]; try { int y = a[0] + x++; } catch (System.IndexOutOfRangeException) { } return x.ToString(); } }", "native-or-preserved");
        yield return ("R2-user-increment", "public class W { public int V; public static W operator +(W w, int n) => new W { V = w.V + n }; public static W operator ++(W w) => new W { V = w.V + 10 }; } public static class Probe { public static string Run() { var w = new W(); w++; var u = new W(); var before = u++; return w.V + \":\" + before.V + \":\" + u.V; } }", "native-or-preserved");
        yield return ("R2-instance-compound-operator", "public class W { public int V; public static W operator +(W w, int n) => new W { V = w.V + n }; public void operator +=(int n) { V += n * 10; } } public static class Probe { public static string Run() { var w = new W(); w += 2; return w.V.ToString(); } }", "native-or-preserved");
        yield return ("R2-simple-index-hoist", "public class C { public int G() => 0; } public static class Probe { static int[] a = { 1 }, other = { 100 }; static C F() { a = other; return new C(); } public static string Run() { var first = a; a[F().G()] = 5; return first[0] + \":\" + other[0]; } }", "native-or-preserved");
        yield return ("R2-ref-indexer", "public class B { int v = 1; public int calls; public ref int this[int i] { get { calls++; return ref v; } } } public static class Probe { public static string Run() { var b = new B(); b[0] += 5; b[0]++; return b.calls.ToString(); } }", "native-or-preserved");
        // Codex review round 3 reproducers.
        yield return ("R3-embedded-instance-compound-operator", "public class W { public int V; public static W operator +(W w, int n) => new W { V = w.V + n }; public void operator +=(int n) { V += n * 10; } } public static class Probe { public static string Run() { var w = new W(); W y = new W(); if (true) y = w += 2; return w.V + \":\" + y.V; } }", "native-or-preserved");
        yield return ("R3-staging-explicit-conversion", "public class W { public static explicit operator int(W w) => Probe.n; } public static class Probe { public static int n = 1; static int F() { n = 10; return 5; } public static string Run() { var w = new W(); string r; if ((int)w < F()) r = \"T\"; else r = \"F\"; return r + n; } }", "native-or-preserved");
        yield return ("R3-staging-implicit-conversion", "public class W { public static implicit operator int(W w) => Probe.n; } public static class Probe { public static int n = 1; static int F() { n = 10; return 5; } public static string Run() { var w = new W(); string r; if (w < F()) r = \"T\"; else r = \"F\"; return r + n; } }", "native-or-preserved");
        yield return ("R3-postfix-past-conversion", "public class W { public static explicit operator int(W w) => Probe.n; public static implicit operator long(W w) => Probe.n; } public static class Probe { public static int n = 1; public static string Run() { var w = new W(); int y = (int)w + n++; long z = w + n++; return y + \":\" + z + \":\" + n; } }", "native-or-preserved");
        yield return ("R3-postfix-past-unboxing", "public static class Probe { public static string Run() { object o = \"s\"; int x = 1; try { int y = (int)o + x++; } catch (System.InvalidCastException) { } return x.ToString(); } }", "native-or-preserved");
        yield return ("R3-dynamic-index", "public struct Key { public static implicit operator int(Key k) { Probe.n++; return 0; } } public static class Probe { public static int n; public static string Run() { var a = new int[] { 1 }; dynamic k = new Key(); a[k] += 5; return n + \":\" + a[0]; } }", "native-or-preserved");
        yield return ("R3-static-initializer", "public class B { public static int V; static B() { throw new System.Exception(); } } public static class Probe { public static string Run() { int x = 1; try { int y = B.V + x++; } catch (System.TypeInitializationException) { } return x.ToString(); } }", "native-or-preserved");
        yield return ("R3-ref-property-simple-assignment", "public class C { public int N() => 2; } public static class Probe { public static string log = \"\"; static int x; public static ref int P { get { log += \"P\"; return ref x; } } static C F() { log += \"F\"; return new C(); } public static string Run() { P = F().N(); return log + \":\" + x; } }", "native-or-preserved");
        yield return ("R3-key-conversion-simple-assignment", "public struct Key { public static implicit operator int(Key k) { Probe.log += \"I\"; return 0; } } public class C { public int N() => 2; } public static class Probe { public static string log = \"\"; static C F() { log += \"F\"; return new C(); } public static string Run() { var a = new int[1]; var k = new Key(); a[k] = F().N(); return log + \":\" + a[0]; } }", "native-or-preserved");
        yield return ("R3-postfix-property-receiver", "public class B { public int p; public int P { get { Probe.b = Probe.other; return p; } set { p = value; } } } public static class Probe { public static B b = new B { p = 1 }, other = new B { p = 100 }; public static string Run() { var first = b; int v = b.P++; return first.p + \":\" + other.p + \":\" + v; } }", "native-or-preserved");
        yield return ("R3-dynamic-narrowing", "public static class Probe { public static string Run() { byte b = 1; dynamic v = 2; b += v; return \"\" + b; } }", "native-or-preserved");
        yield return ("O-control-plain", "public static class Probe { " + Log + "public static string Run() { int x = 1, y = 2; x += 1; x += y; x += G(); x <<= 1; return log + x; } }", "native");

        // ---- #1632: hoisted statements stay inside an unbraced body or a switch section. ----
        const string use = "static int Use(int v) => v + 1; ";
        yield return ("P-for", "public static class Probe { " + use + "public static string Run() { int y = 0; for (int i = 0; i < 3; i++) y = Use(y++); return y.ToString(); } }", "native");
        yield return ("P-if-else", "public static class Probe { " + use + "public static string Run() { int y = 0, z = 0; if (y == 0) y = Use(y++); else y = Use(y--); if (y == 0) z = Use(z++); else z = Use(z--); return y + \":\" + z; } }", "native");
        yield return ("P-else-if", "public static class Probe { " + use + "public static string Run() { int y = 3; if (y == 0) y = 1; else if (y == 3) y = Use(y++); else y = Use(y--); return y.ToString(); } }", "native");
        yield return ("P-while", "public static class Probe { " + use + "public static string Run() { int y = 0; while (y < 5) y = Use(y++); return y.ToString(); } }", "native");
        yield return ("P-do", "public static class Probe { " + use + "public static string Run() { int y = 0; do y = Use(y++); while (y < 5); return y.ToString(); } }", "native");
        yield return ("P-foreach", "public static class Probe { " + use + "public static string Run() { int y = 0; foreach (var k in new[] { 1, 2, 3 }) y = Use(y++) + k; return y.ToString(); } }", "native");
        yield return ("P-switch-section", "public static class Probe { " + use + "public static string Run() { int y = 0; for (int k = 0; k < 3; k++) { switch (k) { case 0: y = Use(y++); break; case 1: y = Use(y--) * 10; break; default: y += 7; break; } } return y.ToString(); } }", "native");
        yield return ("P-lock", "public static class Probe { " + use + "static readonly object o = new object(); public static string Run() { int y = 0; lock (o) y = Use(y++); return y.ToString(); } }", "native-or-preserved");
        yield return ("P-using", "public class D : System.IDisposable { public void Dispose() { Probe.log += \"d\"; } } public static class Probe { public static string log = \"\"; " + use + "public static string Run() { int y = 0; using (new D()) y = Use(y++); return log + y; } }", "native-or-preserved");
        yield return ("P-nested-unbraced", "public static class Probe { " + use + "public static string Run() { int y = 0; for (int i = 0; i < 4; i++) if (i % 2 == 0) y = Use(y++); else y += Use(y--); return y.ToString(); } }", "native");
        yield return ("P-chain-call-unbraced", "public class C { public C Next() { Probe.log += \"n\"; return this; } public void Touch() { Probe.log += \"t\"; } } public static class Probe { public static string log = \"\"; public static string Run() { var c = new C(); for (int i = 0; i < 2; i++) c.Next().Touch(); if (log.Length > 100) c.Next().Touch(); return log; } }", "native");
        yield return ("P-compound-unbraced", "public class C { public int N() => 3; } public static class Probe { static int x = 1; static C F() { x = 10; return new C(); } public static string Run() { for (int i = 0; i < 2; i++) x += F().N(); return x.ToString(); } }", "native");
        yield return ("P-chained-assignment-unbraced", "public static class Probe { " + Log + "public static string Run() { int x = 0, y = 0; for (int k = 0; k < 2; k++) x = y = G(); if (x > 0) x = y = I(); return log + x + y; } }", "native");
        yield return ("P-labeled-statement", "public static class Probe { " + use + "public static string Run() { int y = 0; goto L; L: y += 5; return y.ToString(); } }", "native-or-preserved");
        yield return ("P-conditional-unbraced", "public static class Probe { " + Log + "public static string Run() { int y = 0; for (int i = 0; i < 2; i++) y += i == 0 ? G() : I(); return log + y; } }", "native-or-preserved");
        yield return ("P-out-var-unbraced", "public static class Probe { public static string Run() { int y = 0; for (int i = 0; i < 2; i++) y += int.TryParse(\"4\", out var n) ? n : 0; return y.ToString(); } }", "native-or-preserved");

        // ---- Audit: other hoisting sites, ordered against the rest of their statement. ----
        yield return ("A-postfix-argument-order", "public static class Probe { static string Use2(int a, int b) => a + \",\" + b; public static string Run() { int x = 1; var r = Use2(x, x++); return r + \":\" + x; } }", "native-or-preserved");
        yield return ("A-postfix-after-call", "public static class Probe { static int x = 1; static int Read() => x * 10; static string Use2(int a, int b) => a + \",\" + b; public static string Run() { var r = Use2(Read(), x++); return r + \":\" + x; } }", "native-or-preserved");
        yield return ("A-postfix-while-condition", "public static class Probe { public static string Run() { int x = 0, n = 0; while (x++ < 3) { n++; } return x + \":\" + n; } }", "native-or-preserved");
        yield return ("A-postfix-short-circuit", "public static class Probe { public static string Run() { int x = 0; bool b = false && x++ > 0; return x + \":\" + b; } }", "native-or-preserved");
        yield return ("A-postfix-indexer-value", "public static class Probe { " + Log + "static int[] a = { 0, 10 }; public static string Run() { int v = a[I()]++; return log + v + \":\" + a[1]; } }", "native-or-preserved");
        yield return ("A-postfix-property-value", "public static class Probe { public static string log = \"\"; static int p = 3; public static int P { get { log += \"g\"; return p; } set { log += \"s\"; p = value; } } public static string Run() { int v = P++; return log + v + p; } }", "native-or-preserved");
        yield return ("A-null-conditional-unbraced", "public class C { public void M(int v) { Probe.log += v; } } public static class Probe { public static string log = \"\"; public static string Run() { C? c = new C(); int y = 0; for (int i = 0; i < 2; i++) c?.M(y++); return log + y; } }", "native-or-preserved");
        yield return ("A-pattern-variable-unbraced", "public static class Probe { public static string Run() { object o = 4; int y = 0; for (int i = 0; i < 2; i++) if (o is int n) y += n; return y.ToString(); } }", "native-or-preserved");
        yield return ("A-if-call-staging-order", "public static class Probe { static int x = 1; static int Bump() { x = 10; return 5; } public static string Run() { string r; if (x < Bump()) r = \"lt\"; else r = \"ge\"; return r + x; } }", "native-or-preserved");
        yield return ("A-if-call-staging-control", "public static class Probe { static int Five() => 5; public static string Run() { int x = 1; string r; if (x < Five()) r = \"lt\"; else r = \"ge\"; return r; } }", "native");
        yield return ("A-else-if-postfix", "public static class Probe { public static string Run() { int x = 5, r = 0; if (x > 3) r = 1; else if (x++ > 0) r = 2; int y = 0; if (y > 3) r += 10; else if (y++ >= 0) r += 20; return r + \":\" + x + \":\" + y; } }", "native");
        yield return ("A-else-if-pattern", "public static class Probe { public static string Run() { object o = \"s\"; int r = 0; if (o is string) r = 1; else if (o is int n) r = n; object p = 7; if (p is string) r += 1; else if (p is int m) r += m; return r.ToString(); } }", "native-or-preserved");
        yield return ("A-labeled-hoist", "public static class Probe { static int Use(int v) => v + 1; public static string Run() { int y = 0, k = 0; L: y = Use(y++); k++; if (k < 3) goto L; return y.ToString(); } }", "native-or-preserved");
        yield return ("O-coalesce-property", "#nullable enable\npublic static class Probe { public static string log = \"\"; static string? p; public static string? P { get { log += \"g\"; return p; } set { log += \"s\"; p = value; } } public static string Run() { P ??= \"v\"; P ??= \"w\"; return log + P; } }", "native-or-preserved");
        yield return ("A-postfix-in-condition", "public static class Probe { public static string Run() { int x = 0, n = 0; for (int i = 0; i < 3; i++) { if (x++ < 2) n++; } return x + \":\" + n; } }", "native-or-preserved");

        // ---- #1634: compound assignments on small integral types keep the narrowing cast. ----
        foreach (var (type, initial, operand) in SmallTypes)
        {
            foreach (var (name, op) in Operators)
            {
                var right = name is "shl" or "shr" ? "3" : operand;
                yield return ($"N-{type}-{name}", "public static class Probe { public static string Run() { " + type + " v = " + initial + "; v " + op + " " + right + "; " + type + " w = " + initial + "; w " + op + " " + right + "; w " + op + " " + right + "; return (int)v + \":\" + (int)w; } }", "native");
            }
            yield return ($"N-{type}-increment", "public static class Probe { public static string Run() { " + type + " v = " + initial + "; v++; ++v; v++; v++; v++; v++; " + type + " w = default; w--; --w; " + type + " k = " + initial + "; int before = k++; int after = ++k; int down = k--; return (int)v + \":\" + (int)w + \":\" + before + \":\" + after + \":\" + down + \":\" + (int)k; } }", "native");
            yield return ($"N-{type}-checked", "public static class Probe { public static string Run() { " + type + " v = " + initial + "; try { checked { v += " + operand + "; v += " + operand + "; v += " + operand + "; v += " + operand + "; v += " + operand + "; v += " + operand + "; } } catch (System.OverflowException) { return \"overflow:\" + (int)v; } return (int)v + \"\"; } }", "native-or-preserved");
            yield return ($"N-{type}-unchecked", "public static class Probe { public static string Run() { " + type + " v = " + initial + "; unchecked { v += " + operand + "; v += " + operand + "; v += " + operand + "; v += " + operand + "; v += " + operand + "; v += " + operand + "; } return (int)v + \"\"; } }", "native-or-preserved");
            yield return ($"N-{type}-checked-increment", "public static class Probe { public static string Run() { " + type + " v = " + initial + "; try { checked { for (int i = 0; i < 10; i++) v++; } } catch (System.OverflowException) { return \"overflow:\" + (int)v; } return (int)v + \"\"; } }", "native-or-preserved");
        }
        yield return ("N-nullable-byte", "public static class Probe { public static string Run() { byte? b = 250; b += 10; byte? n = null; n += 10; b++; return b + \":\" + (n == null); } }", "native-or-preserved");
        yield return ("N-widening-control", "public static class Probe { public static string Run() { long l = long.MaxValue; l += 1; float f = 1.5f; f += 2; double d = 1; d *= f; return l + \":\" + d.ToString(System.Globalization.CultureInfo.InvariantCulture); } }", "native");
        yield return ("N-enum", "public enum E { A = 1, B = 2, C = 4 } public static class Probe { public static string Run() { E e = E.A; e += 1; e |= E.C; e++; return ((int)e).ToString(); } }", "native-or-preserved");
        yield return ("N-byte-property", "public static class Probe { public static byte P { get; set; } = 250; public static string Run() { P += 10; P++; return \"\" + P; } }", "native");
        yield return ("N-byte-array-element", "public static class Probe { " + Log + "public static string Run() { var a = new byte[] { 0, 250 }; a[I()] += 10; a[I()]++; return log + a[1]; } }", "native");
        yield return ("N-byte-field-receiver", "public class B { public byte V = 250; } public static class Probe { public static string Run() { var b = new B(); b.V += 10; b.V <<= 1; return \"\" + b.V; } }", "native");
        yield return ("N-byte-hoisted-rhs", "public class C { public byte N() => 3; } public static class Probe { static byte x = 250; static C F() { x = 1; return new C(); } public static string Run() { x += F().N(); return \"\" + x; } }", "native");
        yield return ("N-control-int", "public static class Probe { public static string Run() { int i = 1; i += 2; i <<= 2; long l = 1; l *= 3; return i + \":\" + l; } }", "native");
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
        // Struct and cross-class members are unknown calls for effects (a pre-existing gap); the
        // comparison is about values, so an effects-only refusal is compiled without enforcement.
        var relaxed = strict.HasErrors && strict.Diagnostics.Errors.All(e => e.Code.StartsWith("Calor04", StringComparison.Ordinal))
            ? Program.Compile(calor, id + ".calr", new CompilationOptions { StatusWriter = TextWriter.Null, EnforceEffects = false })
            : strict;
        Assert.False(relaxed.HasErrors, $"{id} on {surface}: {string.Join("; ", relaxed.Diagnostics.Errors)}\n{calor}");
        // A hoist placed outside its loop can make the converted program loop forever.
        var converted = Task.Run(() => RefOutImplementationTests.Run(relaxed.GeneratedCode));
        var finished = await Task.WhenAny(converted, Task.Delay(TimeSpan.FromSeconds(30)));
        Assert.True(finished == converted, $"{id} on {surface}: the converted program did not finish\n{calor}");
        Assert.Equal(original, await converted); // never a reorder or a second evaluation
        Assert.True(allowed.Split("-or-").Contains(interop ? "preserved" : "native"), $"{id} on {surface}: expected {allowed}, losses [{string.Join(", ", losses)}]\n{calor}");
    }

    // #1633: the target is read into a temp before the call the right-hand side hoists.
    [Fact]
    public void HoistedRightHandSide_ReadsTheTargetFirst()
    {
        var calor = Convert(Rows().Single(r => r.Id == "O-issue-repro").Source);
        Assert.Matches(@"§B\{(_asg\d+)\} x\s+§B\{(_chain\w*)\} §C\{F\}\s+§ASSIGN x \(\+ \1 §C\{\2\.N\}", calor);
    }

    // #1633: an index with an effect runs once, bound before the statement.
    [Fact]
    public void IndexWithAnEffect_IsBoundOnce()
    {
        var calor = Convert(Rows().Single(r => r.Id == "O-add-indexer-args").Source);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(calor, @"§C\{I\}"));
        Assert.Matches(@"§B\{(_asg\d+)\} §C\{I\}\s+§ASSIGN §IDX\{[^}]+\} \1 \(\+ §IDX\{[^}]+\} \1 §C\{G\}", calor);
    }

    // #1634: every compound operator and ++/-- on a small integral type casts back to the target type.
    [Theory]
    [InlineData("byte", "u8")]
    [InlineData("sbyte", "i8")]
    [InlineData("short", "i16")]
    [InlineData("ushort", "u16")]
    [InlineData("char", "char")]
    public void SmallIntegralTarget_CastsBack(string type, string calorType)
    {
        foreach (var (name, _) in Operators)
            Assert.Contains($"(cast {calorType} (", Convert(Rows().Single(r => r.Id == $"N-{type}-{name}").Source));
        // Six statement-level ++/-- on v and w, and the updates of k++ and k-- used as values.
        var increments = Convert(Rows().Single(r => r.Id == $"N-{type}-increment").Source);
        Assert.Equal(10, System.Text.RegularExpressions.Regex.Matches(increments, $@"\(cast {calorType} \(").Count);
    }

    // Controls: an int target, and a right-hand side that hoists nothing, convert exactly as before.
    [Fact]
    public void Controls_KeepTheirShape()
    {
        var calor = Convert(Rows().Single(r => r.Id == "O-control-plain").Source);
        Assert.Contains("§ASSIGN x (+ x §C{G})", calor);
        Assert.DoesNotContain("_asg", calor);
        Assert.DoesNotContain("cast", Convert(Rows().Single(r => r.Id == "N-control-int").Source));
    }

    // #1632: the hoisted statements of an unbraced loop body are inside the loop.
    [Fact]
    public void UnbracedBody_KeepsItsHoistedStatements()
    {
        var calor = Convert(Rows().Single(r => r.Id == "P-for").Source);
        Assert.Matches(@"§L\{[^}]+\}\n\s+§B\{(_pre\d+)\} y\n\s+§ASSIGN y \(\+ \1 1\)\n\s+§ASSIGN y §C\{Use\} \1", calor);
    }

    [Theory]
    [InlineData("A-postfix-argument-order", "compound-assignment-order")]
    [InlineData("O-compound-as-value-hoisted-rhs", "embedded-assignment")]
    public void KeptShapes_AreReportedAsLosses(string id, string feature)
    {
        var result = new CSharpToCalorConverter(new ConversionOptions()).Convert(Rows().Single(r => r.Id == id).Source);
        Assert.Contains(result.Losses, loss => loss.Feature == feature);
        Assert.Equal(SupportLevel.NotSupported, FeatureSupport.GetFeatureInfo(feature)!.Support);
    }

    [Fact]
    public void NewLossFeatures_AreRegistered()
    {
        Assert.Equal(SupportLevel.NotSupported, FeatureSupport.GetFeatureInfo("compound-assignment-order")!.Support);
        Assert.Equal(SupportLevel.NotSupported, FeatureSupport.GetFeatureInfo("compound-assignment-narrowing")!.Support);
    }

    private static string Convert(string source)
        => new CSharpToCalorConverter(new ConversionOptions()).Convert(source).CalorSource!.Replace("\r\n", "\n");
}
