using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;

namespace Calor.Compiler.Analysis.BugPatterns.Patterns;

/// <summary>
/// Flow-sensitive Option/Result unwrap checker. Tracks per-variable Checked/Unchecked
/// state through a forward walk of the bound function body. Catches three classes of
/// issues beyond the existing <see cref="NullDereferenceChecker"/>:
///
///   1. Reassignment after check — a variable verified Some/Ok becomes Unchecked
///      when reassigned. <c>if (opt.is_some()) { opt = other; opt.unwrap() }</c>
///      should fire.
///   2. Guard-return fallthrough — <c>if (opt.is_none()) return; opt.unwrap()</c>
///      is safe; the existing checker false-positives because it doesn't recognize
///      the fallthrough implication.
///   3. Outside-check paths — <c>if (opt.is_some()) return; opt.unwrap()</c> fires
///      because after the if, the is_some check no longer holds.
///
/// This checker is reconstructed from docs/design/tier1a-postmortem.md §1 as part of
/// §6 (shape-Calor corpus test). Ships behind the experimental
/// <c>flow-option-tracking</c> flag; off by default.
/// </summary>
public sealed class OptionResultFlowChecker : IBugPatternChecker
{
    public string Name => "OPTION_RESULT_FLOW";

    public void Check(BoundFunction function, DiagnosticBag diagnostics)
    {
        var state = new FlowState();
        // Parameters start Unchecked (consistent with treating them as untrusted)
        foreach (var stmt in function.Body)
        {
            CheckStatement(stmt, diagnostics, state);
        }
    }

    // ────────────────────────────────────────────────────────────────────
    // Flow state
    // ────────────────────────────────────────────────────────────────────

    private enum VarState
    {
        Unchecked,
        Checked
    }

    private sealed class FlowState
    {
        private readonly Dictionary<string, VarState> _variables;

        public FlowState() => _variables = new Dictionary<string, VarState>();
        private FlowState(Dictionary<string, VarState> inner) => _variables = inner;

        public FlowState Clone()
        {
            return new FlowState(new Dictionary<string, VarState>(_variables));
        }

        public void MarkChecked(string name) => _variables[name] = VarState.Checked;

        public void Invalidate(string name) => _variables[name] = VarState.Unchecked;

        public bool IsChecked(string name) =>
            _variables.TryGetValue(name, out var s) && s == VarState.Checked;

        /// <summary>
        /// Intersect: var is Checked in the merged state iff it's Checked on both incoming paths.
        /// </summary>
        public void MergeFrom(FlowState other)
        {
            // Walk this state's Checked entries; demote any that aren't Checked in other.
            var keys = new List<string>(_variables.Keys);
            foreach (var key in keys)
            {
                if (_variables[key] == VarState.Checked && !other.IsChecked(key))
                    _variables[key] = VarState.Unchecked;
            }
            // A var only Checked in `other` does not transfer — probing it in merged state
            // correctly returns Unchecked because it's absent from `this`.
        }

        /// <summary>
        /// Replace this state's contents with a copy of source's contents.
        /// </summary>
        public void ReplaceWith(FlowState source)
        {
            _variables.Clear();
            foreach (var kv in source._variables)
                _variables[kv.Key] = kv.Value;
        }
    }

    // ────────────────────────────────────────────────────────────────────
    // Statement walk
    // ────────────────────────────────────────────────────────────────────

    private void CheckStatement(BoundStatement stmt, DiagnosticBag diagnostics, FlowState state)
    {
        switch (stmt)
        {
            case BoundBindStatement bind:
                if (bind.Initializer != null)
                    CheckExpression(bind.Initializer, diagnostics, state);
                // New binding starts Unchecked (we don't track declaration-time checks yet)
                state.Invalidate(bind.Variable.Name);
                break;

            case BoundAssignmentStatement assign:
                CheckExpression(assign.Value, diagnostics, state);
                var assignedName = ExtractAssignedName(assign.Target);
                if (assignedName != null)
                    state.Invalidate(assignedName);
                break;

            case BoundCompoundAssignment compound:
                CheckExpression(compound.Target, diagnostics, state);
                CheckExpression(compound.Value, diagnostics, state);
                var compoundName = ExtractAssignedName(compound.Target);
                if (compoundName != null)
                    state.Invalidate(compoundName);
                break;

            case BoundIfStatement ifStmt:
                CheckExpression(ifStmt.Condition, diagnostics, state);
                WalkIf(ifStmt, diagnostics, state);
                break;

            case BoundReturnStatement ret:
                if (ret.Expression != null)
                    CheckExpression(ret.Expression, diagnostics, state);
                break;

            case BoundThrowStatement throwStmt:
                if (throwStmt.Expression != null)
                    CheckExpression(throwStmt.Expression, diagnostics, state);
                break;

            case BoundCallStatement call:
                CheckCallUnwrapSite(call.Target, call.Span, diagnostics, state);
                foreach (var arg in call.Arguments)
                    CheckExpression(arg, diagnostics, state);
                break;

            case BoundExpressionStatement exprStmt:
                CheckExpression(exprStmt.Expression, diagnostics, state);
                break;

            case BoundForStatement forStmt:
                CheckExpression(forStmt.From, diagnostics, state);
                CheckExpression(forStmt.To, diagnostics, state);
                if (forStmt.Step != null)
                    CheckExpression(forStmt.Step, diagnostics, state);
                WalkLoopBody(forStmt.Body, diagnostics, state);
                break;

            case BoundWhileStatement whileStmt:
                CheckExpression(whileStmt.Condition, diagnostics, state);
                WalkLoopBody(whileStmt.Body, diagnostics, state);
                break;

            case BoundDoWhileStatement doWhile:
                WalkLoopBody(doWhile.Body, diagnostics, state);
                CheckExpression(doWhile.Condition, diagnostics, state);
                break;

            case BoundForeachStatement forEach:
                CheckExpression(forEach.Collection, diagnostics, state);
                WalkLoopBody(forEach.Body, diagnostics, state);
                break;

            case BoundUsingStatement usingStmt:
                CheckExpression(usingStmt.ResourceExpression, diagnostics, state);
                foreach (var s in usingStmt.Body)
                    CheckStatement(s, diagnostics, state);
                break;
        }
    }

    // ────────────────────────────────────────────────────────────────────
    // If statement handling — condition checks, guard-return detection
    // ────────────────────────────────────────────────────────────────────

    private void WalkIf(BoundIfStatement ifStmt, DiagnosticBag diagnostics, FlowState state)
    {
        // Condition implies certain vars are Checked in then-branch (if is_some/is_ok/has_value)
        // or in else-branch (if is_none/is_err).
        var (thenChecks, elseChecks) = ExtractConditionImplications(ifStmt.Condition);

        var thenState = state.Clone();
        foreach (var name in thenChecks) thenState.MarkChecked(name);
        foreach (var s in ifStmt.ThenBody) CheckStatement(s, diagnostics, thenState);
        var thenExits = BranchAlwaysExits(ifStmt.ThenBody);

        var elseState = state.Clone();
        foreach (var name in elseChecks) elseState.MarkChecked(name);

        // Walk else-if chain
        var elseExitsList = new List<bool>();
        var elseBranchStates = new List<FlowState>();
        foreach (var elseIf in ifStmt.ElseIfClauses)
        {
            CheckExpression(elseIf.Condition, diagnostics, elseState);
            var (eiThen, _) = ExtractConditionImplications(elseIf.Condition);
            var eiState = elseState.Clone();
            foreach (var name in eiThen) eiState.MarkChecked(name);
            foreach (var s in elseIf.Body) CheckStatement(s, diagnostics, eiState);
            elseBranchStates.Add(eiState);
            elseExitsList.Add(BranchAlwaysExits(elseIf.Body));
        }

        var elseBodyState = elseState.Clone();
        if (ifStmt.ElseBody != null)
        {
            foreach (var s in ifStmt.ElseBody) CheckStatement(s, diagnostics, elseBodyState);
        }
        var elseExits = ifStmt.ElseBody != null && BranchAlwaysExits(ifStmt.ElseBody);

        // Guard-return fallthrough: if then-branch always exits and condition implied
        // "else-branch vars are Checked" (i.e., condition was is_none/is_err), then on
        // fallthrough those vars are Checked.
        if (thenExits && ifStmt.ElseIfClauses.Count == 0 && ifStmt.ElseBody == null)
        {
            foreach (var name in elseChecks) state.MarkChecked(name);
            return;
        }

        // General merge: for each branch that DOES fall through, incorporate its state.
        // Branches that always exit don't contribute to post-if state.
        FlowState? merged = null;
        if (!thenExits) merged = merged == null ? thenState : Merged(merged, thenState);
        for (int i = 0; i < elseBranchStates.Count; i++)
        {
            if (!elseExitsList[i])
                merged = merged == null ? elseBranchStates[i] : Merged(merged, elseBranchStates[i]);
        }
        if (ifStmt.ElseBody == null)
        {
            // Implicit empty else branch — uses original state with elseChecks applied
            var implicitElse = state.Clone();
            foreach (var name in elseChecks) implicitElse.MarkChecked(name);
            merged = merged == null ? implicitElse : Merged(merged, implicitElse);
        }
        else if (!elseExits)
        {
            merged = merged == null ? elseBodyState : Merged(merged, elseBodyState);
        }

        if (merged != null)
        {
            // Replace state's contents with merged
            CopyFrom(state, merged);
        }
    }

    private static FlowState Merged(FlowState a, FlowState b)
    {
        var clone = a.Clone();
        clone.MergeFrom(b);
        return clone;
    }

    private static void CopyFrom(FlowState target, FlowState source) => target.ReplaceWith(source);

    // ────────────────────────────────────────────────────────────────────
    // Loop body walk — conservative: invalidate any variable assigned inside
    // ────────────────────────────────────────────────────────────────────

    private void WalkLoopBody(IReadOnlyList<BoundStatement> body, DiagnosticBag diagnostics, FlowState state)
    {
        var assigned = new HashSet<string>();
        CollectAssignedNames(body, assigned);
        foreach (var name in assigned) state.Invalidate(name);

        foreach (var s in body) CheckStatement(s, diagnostics, state);

        // After the loop, conservatively invalidate again.
        foreach (var name in assigned) state.Invalidate(name);
    }

    private static void CollectAssignedNames(IReadOnlyList<BoundStatement> body, HashSet<string> assigned)
    {
        foreach (var stmt in body)
        {
            switch (stmt)
            {
                case BoundAssignmentStatement assign:
                    var n = ExtractAssignedName(assign.Target);
                    if (n != null) assigned.Add(n);
                    break;
                case BoundCompoundAssignment compound:
                    var cn = ExtractAssignedName(compound.Target);
                    if (cn != null) assigned.Add(cn);
                    break;
                case BoundIfStatement ifS:
                    CollectAssignedNames(ifS.ThenBody, assigned);
                    foreach (var ei in ifS.ElseIfClauses) CollectAssignedNames(ei.Body, assigned);
                    if (ifS.ElseBody != null) CollectAssignedNames(ifS.ElseBody, assigned);
                    break;
                case BoundForStatement fs:
                    CollectAssignedNames(fs.Body, assigned);
                    break;
                case BoundWhileStatement ws:
                    CollectAssignedNames(ws.Body, assigned);
                    break;
                case BoundDoWhileStatement dw:
                    CollectAssignedNames(dw.Body, assigned);
                    break;
                case BoundForeachStatement fe:
                    CollectAssignedNames(fe.Body, assigned);
                    break;
                case BoundUsingStatement us:
                    CollectAssignedNames(us.Body, assigned);
                    break;
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────
    // Expression walk
    // ────────────────────────────────────────────────────────────────────

    private void CheckExpression(BoundExpression expr, DiagnosticBag diagnostics, FlowState state)
    {
        switch (expr)
        {
            case BoundCallExpression callExpr:
                CheckCallUnwrapSite(callExpr.Target, callExpr.Span, diagnostics, state);
                foreach (var arg in callExpr.Arguments)
                    CheckExpression(arg, diagnostics, state);
                break;
            case BoundBinaryExpression bin:
                CheckExpression(bin.Left, diagnostics, state);
                CheckExpression(bin.Right, diagnostics, state);
                break;
            case BoundUnaryExpression un:
                CheckExpression(un.Operand, diagnostics, state);
                break;
            case BoundConditionalExpression cond:
                CheckExpression(cond.Condition, diagnostics, state);
                CheckExpression(cond.WhenTrue, diagnostics, state);
                CheckExpression(cond.WhenFalse, diagnostics, state);
                break;
        }
    }

    // ────────────────────────────────────────────────────────────────────
    // Unwrap-site detection
    // ────────────────────────────────────────────────────────────────────

    private static void CheckCallUnwrapSite(string target, Parsing.TextSpan span, DiagnosticBag diagnostics, FlowState state)
    {
        if (!IsUnsafeUnwrapCall(target)) return;
        var receiver = ExtractReceiver(target);
        if (receiver == null) return; // anonymous — out of scope; existing checker handles
        if (state.IsChecked(receiver)) return;

        diagnostics.ReportWarning(
            span,
            DiagnosticCode.UnsafeUnwrapFlow,
            $"Unsafe unwrap on '{receiver}' on a path where it isn't verified Some/Ok. Check with is_some()/is_ok() or use a guard like 'if {receiver}.is_none() return;' before unwrapping.");
    }

    private static bool IsUnsafeUnwrapCall(string target)
    {
        if (target.EndsWith(".unwrap")) return true;
        if (target.EndsWith(".expect")) return true;
        if (target.EndsWith(".unwrap_unchecked")) return true;
        if (target.EndsWith(".get_unchecked")) return true;
        return false;
    }

    private static string? ExtractReceiver(string target)
    {
        var dotIndex = target.LastIndexOf('.');
        if (dotIndex <= 0) return null;
        var prefix = target[..dotIndex];
        // Reject anonymous expressions stringified as AST types
        if (prefix.Contains("Calor.Compiler.")) return null;
        // Identifier validation: letters, digits, underscore, dot for qualified names
        foreach (var c in prefix)
        {
            if (!char.IsLetterOrDigit(c) && c != '_' && c != '.')
                return null;
        }
        return prefix;
    }

    private static string? ExtractAssignedName(BoundExpression target)
    {
        return target switch
        {
            BoundVariableExpression v => v.Variable.Name,
            _ => null
        };
    }

    // ────────────────────────────────────────────────────────────────────
    // Condition analysis — extract Option/Result checks and their branch implications
    // ────────────────────────────────────────────────────────────────────

    private static (List<string> ThenChecks, List<string> ElseChecks) ExtractConditionImplications(BoundExpression cond)
    {
        var thenChecks = new List<string>();
        var elseChecks = new List<string>();
        Walk(cond, positive: true);
        return (thenChecks, elseChecks);

        void Walk(BoundExpression e, bool positive)
        {
            switch (e)
            {
                case BoundUnaryExpression un when un.Operator == UnaryOperator.Not:
                    Walk(un.Operand, !positive);
                    break;
                case BoundBinaryExpression bin when bin.Operator == BinaryOperator.And && positive:
                    Walk(bin.Left, true);
                    Walk(bin.Right, true);
                    break;
                case BoundBinaryExpression bin when bin.Operator == BinaryOperator.Or && !positive:
                    // De Morgan: !(a || b) = !a && !b
                    Walk(bin.Left, false);
                    Walk(bin.Right, false);
                    break;
                case BoundCallExpression call:
                    var target = call.Target;
                    var receiver = ExtractReceiver(target);
                    if (receiver == null) break;
                    if (target.EndsWith(".is_some") || target.EndsWith(".is_ok") || target.EndsWith(".has_value"))
                    {
                        if (positive) thenChecks.Add(receiver);
                        else elseChecks.Add(receiver);
                    }
                    else if (target.EndsWith(".is_none") || target.EndsWith(".is_err"))
                    {
                        if (positive) elseChecks.Add(receiver);
                        else thenChecks.Add(receiver);
                    }
                    break;
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────
    // Branch exit detection (for guard-return pattern)
    // ────────────────────────────────────────────────────────────────────

    private static bool BranchAlwaysExits(IReadOnlyList<BoundStatement> body)
    {
        if (body.Count == 0) return false;
        // A branch always exits if its last statement is return/throw (straightforwardly)
        // or if it's a block that always exits.
        var last = body[body.Count - 1];
        return IsExitingStatement(last);
    }

    private static bool IsExitingStatement(BoundStatement s) => s switch
    {
        BoundReturnStatement => true,
        BoundThrowStatement => true,
        BoundIfStatement ifS when ifS.ElseBody != null &&
            BranchAlwaysExits(ifS.ThenBody) &&
            BranchAlwaysExits(ifS.ElseBody) &&
            ifS.ElseIfClauses.All(ei => BranchAlwaysExits(ei.Body)) => true,
        _ => false
    };
}
