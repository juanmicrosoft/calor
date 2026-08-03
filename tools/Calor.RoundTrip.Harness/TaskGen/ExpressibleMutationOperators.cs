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

    // The corrupting value injected is `new System.Random().Next()` (an int), so the target field
    // must accept an int without a cast — int and long. (Restricting the set keeps the injected
    // statement guaranteed-compiling; the addressability probe + clause (b) filter the rest.)
    private static readonly HashSet<string> IntLikeFieldTypes = new(StringComparer.Ordinal) { "int", "long" };

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
    /// Inject a nondeterministic, effect-bearing write to a writable int/long INSTANCE field, wrapped
    /// in a <c>lock (this) {{ … }}</c> block, as the first statement of an instance method that READS
    /// that field. Two things happen at once:
    ///   • <b>Real behavioral defect:</b> the field is overwritten with <c>new Random().Next()</c>, so
    ///     the method's result (which depends on the field) becomes nondeterministic — a held-out test
    ///     asserting a specific value fails on BOTH arms.
    ///   • <b>Verification-addressable:</b> the effectful call sits inside a <c>lock</c> body, which the
    ///     C#→Calor converter's §E inference walker does NOT descend into (it has no lock/using case),
    ///     so the converted §E stays pure (empty) — while the compile-time effect-enforcement pass DOES
    ///     charge the <c>rand</c> effect. computed ⊄ declared → <c>Calor0410</c> (ForbiddenEffect), a
    ///     build-time signal the C# arm never sees. (Empirically validated: the lock/using body is the
    ///     converter's inference gap; a bare effectful call is self-declared and leaves no gap.)
    ///
    /// The papering-over residual is deliberately preserved: the agent can clear the Calor build by
    /// REMOVING the injected write (correct → held-out passes → caught) OR by DECLARING the effect in
    /// §E (papers over → the nondeterminism still ships → held-out fails → escaped). Which path the
    /// agent takes IS the measurement, not a bug to fix — so both remain possible.
    /// </summary>
    private static void EnumerateEffectViolations(string rel, SyntaxNode root, List<MutationCandidate> acc)
    {
        foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            if (type is not (ClassDeclarationSyntax or StructDeclarationSyntax or RecordDeclarationSyntax))
                continue;
            // A readonly struct cannot host a mutating write; a struct has no reference `this` to lock.
            if (type is StructDeclarationSyntax || type is RecordDeclarationSyntax { ClassOrStructKeyword.RawKind: (int)SyntaxKind.StructKeyword })
                continue;
            if (type.Modifiers.Any(SyntaxKind.ReadOnlyKeyword))
                continue;

            var writableFields = CollectWritableIntLikeInstanceFields(type);
            if (writableFields.Count == 0)
                continue;

            foreach (var method in type.Members.OfType<MethodDeclarationSyntax>())
            {
                if (method.Body is not { } body) continue;                // need a block to inject into
                if (method.Modifiers.Any(SyntaxKind.StaticKeyword)) continue; // `this` needs an instance

                foreach (var field in writableFields)
                {
                    if (!MethodReadsIdentifier(body, field)) continue;

                    // Inject `lock (this) { <field> = new System.Random().Next(); }` as the first
                    // statement. The `rand` effect is charged by enforcement but — because it is nested
                    // in a lock body the converter's §E walker skips — left out of the converted §E.
                    // The field name is written UNQUALIFIED so it resolves to either an instance or a
                    // static field of the containing type (widening the operator's reach on real code).
                    var inject = SyntaxFactory.ParseStatement(
                        $"lock (this) {{ {field} = new System.Random().Next(); }}\n");
                    var newBody = body.WithStatements(body.Statements.Insert(0, inject));
                    var mutatedRoot = root.ReplaceNode(body, newBody);

                    var pos = method.Identifier.GetLocation().GetLineSpan().StartLinePosition;
                    acc.Add(new MutationCandidate
                    {
                        FileRelPath = rel,
                        Source = MutationSource.InjectedMutation,
                        Operator = MutationOperatorKind.EffectViolation,
                        Stratum = DefectStratum.Expressible,
                        ExpectedCheck = CalorForbiddenEffect,
                        OperatorDescription = $"inject undeclared `rand` effect (lock-wrapped `{field} = new Random().Next()`) into {method.Identifier.Text}",
                        Line = pos.Line + 1,
                        Column = pos.Character + 1,
                        OriginalSnippet = $"{method.Identifier.Text}(...) {{ ... }}",
                        MutatedSnippet = $"{method.Identifier.Text}(...) {{ lock (this) {{ {field} = new Random().Next(); }} ... }}",
                        MutatedSource = mutatedRoot.ToFullString(),
                    });
                }
            }
        }
    }

    private static List<string> CollectWritableIntLikeInstanceFields(TypeDeclarationSyntax type)
    {
        var fields = new List<string>();
        foreach (var member in type.Members.OfType<FieldDeclarationSyntax>())
        {
            var mods = member.Modifiers;
            // const/readonly are not writable; static is allowed (written unqualified from an instance
            // method, still inside `lock (this)`). This widens reach to counter/cache-style fields.
            if (mods.Any(SyntaxKind.ConstKeyword) || mods.Any(SyntaxKind.ReadOnlyKeyword))
                continue;
            var typeName = member.Declaration.Type is PredefinedTypeSyntax p ? p.Keyword.Text
                : member.Declaration.Type.ToString();
            if (!IntLikeFieldTypes.Contains(typeName)) continue;
            foreach (var v in member.Declaration.Variables)
                fields.Add(v.Identifier.Text);
        }
        return fields;
    }

    private static bool MethodReadsIdentifier(SyntaxNode body, string name) =>
        body.DescendantNodes().OfType<IdentifierNameSyntax>().Any(id => id.Identifier.Text == name);

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
