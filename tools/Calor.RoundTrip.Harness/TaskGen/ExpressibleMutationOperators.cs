using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Calor.RoundTrip.Harness.TaskGen;

/// <summary>
/// The EXPRESSIBLE-stratum mutation operators (W4 dry-run disposition). Unlike the logic-stratum
/// operators (<see cref="InjectedMutationOperators"/>) — arithmetic / off-by-one / boundary, for
/// which Calor has NO mechanical signal — every operator here injects a REAL behavioral defect that
/// is ALSO <em>verification-addressable</em>: on the mutated-CONVERTED Calor code, one of Calor's
/// mechanical checks fires (an undeclared-effect build error, or a div-by-zero / index-OOB /
/// null-deref bug-pattern diagnostic) while the C# compiler has no equivalent. Each candidate
/// carries the diagnostic code it is PREDICTED to make fire (<see cref="MutationCandidate.ExpectedCheck"/>);
/// the task generator then MECHANICALLY confirms the prediction with a differential addressability
/// probe (<see cref="VerificationAddressability"/>) and drops any candidate whose check does not
/// actually fire (<see cref="ExclusionReason.NotVerificationAddressable"/>) — a logic bug must never
/// masquerade as expressible.
///
/// Like the logic operators, each is a single-point, C#-compiling change, applied in C#
/// (mutate-then-convert), and is pure over source text so it is unit-testable without the corpus.
/// </summary>
public static class ExpressibleMutationOperators
{
    // Calor diagnostic codes the operators target (see Diagnostics/Diagnostic.cs).
    internal const string CalorForbiddenEffect = "Calor0410"; // effect used but not declared
    internal const string CalorDivisionByZero = "Calor0920";
    internal const string CalorIndexOutOfBounds = "Calor0921";
    internal const string CalorNullDereference = "Calor0922";

    // The DETERMINISTIC, guaranteed-nonzero effectful corruptor. `Directory.Exists(GetCurrentDirectory())`
    // is always true (a process's current directory always exists) on every platform, so the taint value
    // is a fixed 1 — the return is corrupted deterministically, IDENTICALLY across runs and across both arms
    // (no Random() nondeterminism → no test-isolation flakiness → clause (b) sees an identical failure).
    // Both Directory.* calls are filesystem effects charged by enforcement; nested in a `using` body — the
    // converter's §E-inference walker's blind spot — they are left out of the converted §E, so the effect
    // is INTRODUCED into the converted arm as an undeclared effect. The corruption is INTRINSIC to the
    // effect (the taint local is written only by the effectful call), so removing the effect removes the
    // bug — the "remove the effect → correct" fix path is genuine.
    private const string TaintLocal = "__calorTaint";
    private const string TaintSink = "__calorSink";

    /// <summary>Enumerate all expressible-stratum candidates for a C# file, in document order.</summary>
    public static IReadOnlyList<MutationCandidate> Enumerate(string source, string fileRelPath)
    {
        var results = new List<MutationCandidate>();
        SyntaxNode root;
        try
        {
            root = CSharpSyntaxTree.ParseText(source).GetRoot();
        }
        catch
        {
            return results;
        }

        EnumerateEffectViolations(fileRelPath, root, results);

        // Guard-removal operators: unwrap a protective `if` so a value can flow into an unchecked
        // divide / index / dereference. Each is a real runtime fault; the addressability probe
        // decides whether Calor's checker actually catches it on the converted arm.
        foreach (var node in root.DescendantNodes())
        {
            if (node is IfStatementSyntax ifs)
            {
                TryDivByZeroGuardRemoval(fileRelPath, root, ifs, results);
                TryIndexOutOfBoundsGuardRemoval(fileRelPath, root, ifs, results);
                TryNullDerefGuardRemoval(fileRelPath, root, ifs, results);
            }
        }

        return results;
    }

    // ===================================================================================
    // EffectViolation → Calor0410
    // ===================================================================================

    /// <summary>
    /// Inject an undeclared filesystem effect that DETERMINISTICALLY corrupts the return of any method
    /// (instance OR static) whose return type the corruption can be expressed for
    /// (<see cref="IsCorruptibleReturn"/>). Into the first return the method owns, it injects a
    /// <c>using</c>-nested effectful read whose fixed value (1) drives the corruption:
    /// <code>
    ///   int __calorTaint = 0;
    ///   using (var __calorSink = new System.IO.StringWriter())
    ///   { __calorTaint = (System.IO.Directory.Exists(System.IO.Directory.GetCurrentDirectory()) ? 1 : 0); }
    ///   return (ORIGINAL) + __calorTaint;              // int/long
    ///   return (ORIGINAL) ^ (__calorTaint == 1);       // bool
    ///   return __calorTaint == 1 ? default(T)! : (ORIGINAL);   // everything else
    /// </code>
    /// Two things happen at once:
    ///   • <b>Real, DETERMINISTIC defect:</b> the taint is a fixed 1 (the current directory always
    ///     exists), so the method returns a corrupted value — a value-asserting held-out test fails
    ///     IDENTICALLY on both arms and across every run (no <c>Random()</c> nondeterminism).
    ///   • <b>Verification-addressable:</b> the <c>Directory.*</c> filesystem effect sits inside a
    ///     <c>using</c> body — the converter's §E-inference walker's blind spot (it has no lock/using
    ///     case) — so the converted §E stays pure while the enforcement pass charges the <c>fs</c>
    ///     effect. computed ⊄ declared → <c>Calor0410</c>, a build signal the C# arm never sees.
    ///
    /// <para><b>Site requirement, widened twice.</b> First (dry-run supply finding) from "a writable
    /// field" to "any method returning a computed int/long". Then (D-S0.5.2) beyond int/long entirely,
    /// by generalizing the CORRUPTION — see <see cref="IsCorruptibleReturn"/>. The int/long restriction
    /// was never about addressability; it existed only because the corruption was arithmetic. Measured
    /// effect of the second widening on the pinned corpus: native supply 9 → 204.</para>
    ///
    /// <para><b>The corruption is INTRINSIC to the effect</b> — the taint local is written only by the
    /// effectful call — so the papering-over residual is genuine: REMOVING the injected block removes
    /// both the effect and the corruption (correct → held-out passes → caught); DECLARING <c>fs</c> in
    /// §E silences Calor0410 but the corruption still ships (papers over → held-out fails → escaped).
    /// Both remain possible; which path the agent takes IS the measurement.</para>
    ///
    /// <para><b>The three corruption forms are NOT equivalent, and the difference matters downstream.</b>
    /// <c>AddOne</c> and <c>FlipBool</c> both still evaluate the original expression and perturb its
    /// value. <c>DefaultWhenTainted</c> does not: the taint is always 1 by construction, so the
    /// <c>ORIGINAL</c> branch of the conditional is dead in every execution and the method returns
    /// <c>default(T)</c> without computing anything. On the pinned corpus a majority of that form's
    /// sites elide a call. That is a real defect and a deterministic one, but it is a different defect
    /// CLASS from an off-by-one — it propagates as a null/zero at a distance rather than a wrong value
    /// at the mutation site, which is a plausible source of <c>ArmsDiverge</c> and of converter-attributed
    /// failures. Any comparison of supply across the widening is a comparison of operator reach, not of
    /// one experiment's subject count.</para>
    /// </summary>
    private static void EnumerateEffectViolations(string rel, SyntaxNode root, List<MutationCandidate> acc)
    {
        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (method.Body is not { } body) continue;                       // need a block to inject into
            if (!IsCorruptibleReturn(method, out var corruptionKind)) continue;

            // The first `return <expr>;` the method itself owns (not one inside a nested lambda / local
            // function, whose return type differs). Corrupting a method-owned return corrupts the result.
            var ownedReturn = method.Body.DescendantNodes().OfType<ReturnStatementSyntax>()
                .FirstOrDefault(r => r.Expression != null && OwningFunction(r) == method);
            if (ownedReturn?.Expression is not { } origExpr) continue;

            // `return null;` corrupted to `taint == 1 ? default(T)! : null` injects NO defect —
            // default(T) IS null. Counting it as supply overstates the number by candidates that can
            // never fail a held-out test. (Downstream NoObservableDefect would drop it, but a
            // pre-pass that only enumerates never gets there.)
            if (corruptionKind == CorruptionKind.DefaultWhenTainted && IsDefaultValued(origExpr)) continue;

            var corruptedReturn = ownedReturn.WithExpression(
                SyntaxFactory.ParseExpression(CorruptionExpression(corruptionKind, origExpr, method.ReturnType)));

            // The injected effect MUST be spelled `System.IO.Directory.…`. Qualifying it as
            // `global::System.IO.…` compiles more widely but the converter's §E-inference no longer
            // recognises the call, so Calor0410 stops firing and the candidate stops being
            // addressable — i.e. it fixes a compile error by silently destroying the mechanism under
            // measurement. Verified: EffectViolation_IsAddressable_Calor0410_IntroducedByTheMutation
            // fails under `global::`. So instead of qualifying, SKIP the sites where the spelling
            // cannot bind: a type that declares its own member named `System` shadows the namespace
            // (Serilog's TimeProvider has `public static TimeProvider System { get; }` → CS1061).
            if (ShadowsSystemNamespace(method)) continue;

            var taintStmts = SyntaxFactory.ParseStatement(
                    $"{{ int {TaintLocal} = 0; using (var {TaintSink} = new System.IO.StringWriter()) "
                    + $"{{ {TaintLocal} = (System.IO.Directory.Exists(System.IO.Directory.GetCurrentDirectory()) ? 1 : 0); }} }}")
                is BlockSyntax b ? b.Statements : default;
            if (taintStmts.Count == 0) continue;

            // An elastic newline after the injection: without it the FOLLOWING statement's leading
            // trivia — a `#pragma`/`#if` directive — lands mid-line and the file no longer parses (CS1040).
            taintStmts = SyntaxFactory.List(taintStmts.Select((st, i) =>
                i == taintStmts.Count - 1
                    ? st.WithTrailingTrivia(st.GetTrailingTrivia().Add(SyntaxFactory.ElasticCarriageReturnLineFeed))
                    : st));

            var bodyWithReturn = body.ReplaceNode(ownedReturn, corruptedReturn);
            var newBody = bodyWithReturn.WithStatements(bodyWithReturn.Statements.InsertRange(0, taintStmts));
            var mutatedRoot = root.ReplaceNode(body, newBody);

            // Fail closed: a candidate whose mutated source does not PARSE is not a candidate. Without
            // this the pre-pass counts sites that can never become tasks, and the supply number — which
            // feeds a venue-retirement decision — is true only by luck.
            if (CSharpSyntaxTree.ParseText(mutatedRoot.ToFullString()).GetDiagnostics()
                    .Any(d => d.Severity == DiagnosticSeverity.Error))
                continue;

            var pos = method.Identifier.GetLocation().GetLineSpan().StartLinePosition;
            acc.Add(new MutationCandidate
            {
                FileRelPath = rel,
                Source = MutationSource.InjectedMutation,
                Operator = MutationOperatorKind.EffectViolation,
                Stratum = DefectStratum.Expressible,
                ExpectedCheck = CalorForbiddenEffect,
                OperatorDescription = $"inject undeclared `fs` effect (using-nested Directory.Exists) that corrupts "
                                      + $"{method.Identifier.Text}'s return ({DescribeCorruption(corruptionKind)})",
                Line = pos.Line + 1,
                Column = pos.Character + 1,
                OriginalSnippet = Truncate($"return {origExpr};"),
                MutatedSnippet = Truncate($"using (…) {{ {TaintLocal} = Directory.Exists(cwd)?1:0; }} "
                                          + $"return {CorruptionExpression(corruptionKind, origExpr, method.ReturnType)};"),
                MutatedSource = mutatedRoot.ToFullString(),
            });
        }
    }

    /// <summary>How a method's return value is corrupted. See <see cref="IsCorruptibleReturn"/>.</summary>
    private enum CorruptionKind
    {
        /// <summary><c>(ORIGINAL) + __calorTaint</c> — the original int/long form, preserved verbatim.</summary>
        AddOne,

        /// <summary><c>(ORIGINAL) ^ (__calorTaint == 1)</c> — flips a bool.</summary>
        FlipBool,

        /// <summary><c>__calorTaint == 1 ? default(T)! : (ORIGINAL)</c> — the general form.</summary>
        DefaultWhenTainted,
    }

    /// <summary>
    /// D-S0.5.2 operator widening. The site predicate previously required a <b>predefined int/long</b>
    /// return, which capped `EffectViolation` at roughly 1% of block-bodied methods. That restriction
    /// was never about addressability — the `using`-nested <c>Directory.*</c> effect that makes
    /// <c>Calor0410</c> fire is independent of the return type — it existed only because the corruption
    /// was arithmetic (<c>+ __calorTaint</c>) and arithmetic needs a number.
    ///
    /// <para>Widening the <i>corruption</i> therefore widens the site universe without touching the
    /// mechanism under measurement. This is deliberately NOT a relaxation of what counts as a defect:
    /// the injected effect is the same undeclared <c>fs</c> effect, the corruption is still
    /// deterministic, still single-point, and still intrinsic to the effect (the taint local is written
    /// only by the effectful call), so the papering-over residual that makes the measurement meaningful
    /// is unchanged. Per the plan's M-S4 population, an operator change that raised supply by making the
    /// injected defect less real would count against the honesty invariant; this one does not.</para>
    ///
    /// <para><b>Exclusions, each because the corruption would not compile or would not be a single
    /// deterministic point:</b> <c>void</c> (nothing to corrupt); <c>ref</c> returns (<c>default</c> is
    /// not a ref); pointer types (unsafe); and <b>async</b> methods, where the declared return type is
    /// <c>Task&lt;T&gt;</c> but <c>return</c> yields <c>T</c>, so <c>default(Task&lt;T&gt;)</c> is a type
    /// error. Iterators never match because <c>yield return</c> is not a <c>ReturnStatementSyntax</c>.</para>
    /// </summary>
    private static bool IsCorruptibleReturn(MethodDeclarationSyntax method, out CorruptionKind kind)
    {
        kind = CorruptionKind.DefaultWhenTainted;

        // async: declared Task<T>, returned T — default(Task<T>) would not compile in that position.
        if (method.Modifiers.Any(m => m.IsKind(SyntaxKind.AsyncKeyword))) return false;

        var type = method.ReturnType;
        if (type is RefTypeSyntax or PointerTypeSyntax) return false;

        if (type is PredefinedTypeSyntax p)
        {
            switch (p.Keyword.Kind())
            {
                case SyntaxKind.VoidKeyword:
                    return false;
                case SyntaxKind.IntKeyword:
                case SyntaxKind.LongKeyword:
                    kind = CorruptionKind.AddOne;       // unchanged from the pre-widening operator
                    return true;
                case SyntaxKind.BoolKeyword:
                    kind = CorruptionKind.FlipBool;     // a flipped bool is the most observable corruption there is
                    return true;
                default:
                    kind = CorruptionKind.DefaultWhenTainted;
                    return true;                        // string, char, other numerics, object, …
            }
        }

        // Named types, generics, arrays, nullable, type parameters: `default(T)` is well-formed for all
        // of them. `dynamic` parses as IdentifierNameSyntax and default(dynamic) is legal.
        return type is IdentifierNameSyntax or QualifiedNameSyntax or GenericNameSyntax
                    or ArrayTypeSyntax or NullableTypeSyntax or AliasQualifiedNameSyntax
                    or TupleTypeSyntax;
    }

    private static string CorruptionExpression(CorruptionKind kind, ExpressionSyntax orig, TypeSyntax returnType) => kind switch
    {
        CorruptionKind.AddOne => $"({orig}) + {TaintLocal}",
        CorruptionKind.FlipBool => $"({orig}) ^ ({TaintLocal} == 1)",
        _ => $"({TaintLocal} == 1 ? default({returnType})! : ({orig}))",
    };

    /// <summary>
    /// True when the method's enclosing type declares a member named <c>System</c>, which shadows the
    /// namespace for simple-name lookup and makes the injected <c>System.IO.Directory</c> unresolvable.
    /// Rare (one site in the pinned corpus) and skipped fail-closed, because the alternative —
    /// <c>global::</c> qualification — costs addressability for every candidate.
    /// </summary>
    private static bool ShadowsSystemNamespace(MethodDeclarationSyntax method)
    {
        var type = method.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        if (type == null) return false;
        return type.Members.Any(m => m switch
        {
            PropertyDeclarationSyntax p => p.Identifier.Text == "System",
            FieldDeclarationSyntax f => f.Declaration.Variables.Any(v => v.Identifier.Text == "System"),
            MethodDeclarationSyntax me => me.Identifier.Text == "System",
            _ => false,
        });
    }

    /// <summary>`null`, `default`, `default(T)`, or a zero literal — values `default(T)` cannot differ from.</summary>
    private static bool IsDefaultValued(ExpressionSyntax expr) => expr switch
    {
        LiteralExpressionSyntax l =>
            l.IsKind(SyntaxKind.NullLiteralExpression)
            || l.IsKind(SyntaxKind.DefaultLiteralExpression)
            || (l.IsKind(SyntaxKind.NumericLiteralExpression) && l.Token.ValueText is "0" or "0.0" or "0f" or "0d" or "0m"),
        DefaultExpressionSyntax => true,
        _ => false,
    };

    private static string DescribeCorruption(CorruptionKind kind) => kind switch
    {
        CorruptionKind.AddOne => "+1",
        CorruptionKind.FlipBool => "boolean flip",
        _ => "returns default instead of the computed value",
    };

    /// <summary>The function (method / local function / lambda) a statement belongs to — for return ownership.</summary>
    private static SyntaxNode? OwningFunction(SyntaxNode node) =>
        node.Ancestors().FirstOrDefault(a =>
            a is MethodDeclarationSyntax or LocalFunctionStatementSyntax
              or ParenthesizedLambdaExpressionSyntax or SimpleLambdaExpressionSyntax
              or AnonymousMethodExpressionSyntax or AccessorDeclarationSyntax);

    // ===================================================================================
    // Guard-removal operators (div-by-zero / index-OOB / null-deref)
    // ===================================================================================

    /// <summary>
    /// Remove a WRAPPING zero-guard `if (d != 0) { ... a / d ... }` so the division always runs.
    /// When <c>d == 0</c> at runtime this throws <see cref="DivideByZeroException"/> (a real defect);
    /// on the converted arm Calor's div-by-zero checker no longer sees the guard as a path condition,
    /// so it proves the divisor can be zero and raises <c>Calor0920</c>. The differential probe
    /// confirms the diagnostic is INTRODUCED by the removal (absent while the guard stood).
    ///
    /// Only the WRAPPING-if form is differential. Empirically, the checker does NOT model early-return
    /// / throw guards (`if (d == 0) return …; … a / d`) as path conditions — it already fires Calor0920
    /// on the guarded division — so removing such a guard is NOT addressable (fires on both conversions)
    /// and the probe correctly rejects it. Wrapping-if divisor guards are rare in real code, so this
    /// operator's live yield is expected to be low; the base rate discloses it.
    /// </summary>
    private static void TryDivByZeroGuardRemoval(string rel, SyntaxNode root, IfStatementSyntax ifs, List<MutationCandidate> acc)
    {
        if (ifs.Else != null) return;
        var guarded = MatchNonZeroGuard(ifs.Condition);
        if (guarded == null) return;
        if (!BodyUsesAsDivisor(ifs.Statement, guarded)) return;
        AddGuardRemoval(rel, root, ifs, MutationOperatorKind.DivByZero, CalorDivisionByZero,
            $"remove zero-guard `if ({guarded} != 0)` protecting a division", acc);
    }

    /// <summary>
    /// Remove a wrapping bounds-guard `if (i &lt; xs.Length) { ... xs[i] ... }` so the index access
    /// always runs (a real out-of-range access). NOTE: Calor's index-OOB checker models array access
    /// as specific call shapes (<c>.get</c>/<c>.at</c>/<c>[]</c>); ordinary converted indexing may
    /// not lower to those, so this operator's addressability yield is expected to be low — the
    /// differential probe reports the truth and the base rate discloses it.
    /// </summary>
    private static void TryIndexOutOfBoundsGuardRemoval(string rel, SyntaxNode root, IfStatementSyntax ifs, List<MutationCandidate> acc)
    {
        if (ifs.Else != null) return;
        var indexVar = MatchLengthGuard(ifs.Condition);
        if (indexVar == null) return;
        if (!BodyUsesAsIndex(ifs.Statement, indexVar)) return;
        AddGuardRemoval(rel, root, ifs, MutationOperatorKind.IndexOutOfBounds, CalorIndexOutOfBounds,
            $"remove bounds-guard `if ({indexVar} < …Length)` protecting an index access", acc);
    }

    /// <summary>
    /// Remove a wrapping null-guard `if (x != null) { ... x.M() ... }` so a null reference can flow
    /// into a dereference (a real NullReferenceException). NOTE: Calor's null bug-pattern models
    /// Option/Result <c>.unwrap</c>/<c>.expect</c> shapes, NOT plain reference null-deref, so
    /// converted corpus code is not expected to trigger it — this operator is included so the base
    /// rate can disclose the gap honestly rather than hide it.
    /// </summary>
    private static void TryNullDerefGuardRemoval(string rel, SyntaxNode root, IfStatementSyntax ifs, List<MutationCandidate> acc)
    {
        if (ifs.Else != null) return;
        var guardedVar = MatchNonNullGuard(ifs.Condition);
        if (guardedVar == null) return;
        if (!BodyDereferences(ifs.Statement, guardedVar)) return;
        AddGuardRemoval(rel, root, ifs, MutationOperatorKind.NullDeref, CalorNullDereference,
            $"remove null-guard `if ({guardedVar} != null)` protecting a dereference", acc);
    }

    /// <summary>Replace the whole `if (guard) {{ body }}` with its unwrapped body statements.</summary>
    private static void AddGuardRemoval(
        string rel, SyntaxNode root, IfStatementSyntax ifs, MutationOperatorKind kind,
        string expectedCheck, string desc, List<MutationCandidate> acc)
    {
        var innerStatements = ifs.Statement is BlockSyntax block
            ? block.Statements.ToArray()
            : new[] { ifs.Statement };
        if (innerStatements.Length == 0) return;

        // Carry the `if`'s leading trivia onto the first hoisted statement so the file still parses.
        var rewritten = innerStatements
            .Select((s, i) => i == 0 ? s.WithLeadingTrivia(ifs.GetLeadingTrivia()) : s)
            .Cast<SyntaxNode>()
            .ToList();
        var mutatedRoot = root.ReplaceNode(ifs, rewritten);

        var pos = ifs.IfKeyword.GetLocation().GetLineSpan().StartLinePosition;
        acc.Add(new MutationCandidate
        {
            FileRelPath = rel,
            Source = MutationSource.InjectedMutation,
            Operator = kind,
            Stratum = DefectStratum.Expressible,
            ExpectedCheck = expectedCheck,
            OperatorDescription = desc,
            Line = pos.Line + 1,
            Column = pos.Character + 1,
            OriginalSnippet = Truncate(ifs.ToString()),
            MutatedSnippet = Truncate(string.Join(" ", innerStatements.Select(s => s.ToString()))),
            MutatedSource = mutatedRoot.ToFullString(),
        });
    }

    // ---- guard-condition matchers (return the guarded identifier name, or null) ----

    /// <summary>`d != 0` / `0 != d` → "d".</summary>
    private static string? MatchNonZeroGuard(ExpressionSyntax cond)
    {
        if (cond is not BinaryExpressionSyntax bin || !bin.IsKind(SyntaxKind.NotEqualsExpression)) return null;
        if (bin.Left is IdentifierNameSyntax lid && IsZeroLiteral(bin.Right)) return lid.Identifier.Text;
        if (bin.Right is IdentifierNameSyntax rid && IsZeroLiteral(bin.Left)) return rid.Identifier.Text;
        return null;
    }

    /// <summary>`x != null` / `null != x` → "x".</summary>
    private static string? MatchNonNullGuard(ExpressionSyntax cond)
    {
        if (cond is not BinaryExpressionSyntax bin || !bin.IsKind(SyntaxKind.NotEqualsExpression)) return null;
        if (bin.Left is IdentifierNameSyntax lid && bin.Right.IsKind(SyntaxKind.NullLiteralExpression))
            return lid.Identifier.Text;
        if (bin.Right is IdentifierNameSyntax rid && bin.Left.IsKind(SyntaxKind.NullLiteralExpression))
            return rid.Identifier.Text;
        return null;
    }

    /// <summary>`i &lt; xs.Length` / `i &lt; xs.Count` → "i".</summary>
    private static string? MatchLengthGuard(ExpressionSyntax cond)
    {
        if (cond is not BinaryExpressionSyntax bin || !bin.IsKind(SyntaxKind.LessThanExpression)) return null;
        if (bin.Left is not IdentifierNameSyntax id) return null;
        if (bin.Right is MemberAccessExpressionSyntax mae
            && mae.Name.Identifier.Text is "Length" or "Count")
            return id.Identifier.Text;
        return null;
    }

    private static bool IsZeroLiteral(ExpressionSyntax e) =>
        e is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.NumericLiteralExpression)
        && lit.Token.ValueText == "0";

    // ---- body-usage detectors ----

    private static bool BodyUsesAsDivisor(SyntaxNode body, string name) =>
        body.DescendantNodes().OfType<BinaryExpressionSyntax>().Any(b =>
            (b.IsKind(SyntaxKind.DivideExpression) || b.IsKind(SyntaxKind.ModuloExpression))
            && b.Right is IdentifierNameSyntax id && id.Identifier.Text == name);

    private static bool BodyUsesAsIndex(SyntaxNode body, string name) =>
        body.DescendantNodes().OfType<ElementAccessExpressionSyntax>().Any(ea =>
            ea.ArgumentList.Arguments.Count == 1
            && ea.ArgumentList.Arguments[0].Expression is IdentifierNameSyntax id
            && id.Identifier.Text == name);

    private static bool BodyDereferences(SyntaxNode body, string name) =>
        body.DescendantNodes().OfType<MemberAccessExpressionSyntax>().Any(m =>
            m.Expression is IdentifierNameSyntax id && id.Identifier.Text == name);

    private static string Truncate(string s)
    {
        s = s.Replace("\r", " ").Replace("\n", " ").Trim();
        while (s.Contains("  ")) s = s.Replace("  ", " ");
        return s.Length > 120 ? s[..117] + "..." : s;
    }
}
