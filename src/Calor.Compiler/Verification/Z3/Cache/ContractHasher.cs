using System.Security.Cryptography;
using System.Text;
using Calor.Compiler.Ast;

namespace Calor.Compiler.Verification.Z3.Cache;

/// <summary>
/// Generates deterministic hash keys from contract expressions for caching.
/// </summary>
public sealed class ContractHasher
{
    /// <summary>
    /// Computes a hash key for a precondition.
    /// Format: PRE:{params}::{expression_hash}
    /// </summary>
    public string HashPrecondition(
        IReadOnlyList<(string Name, string TypeName)> parameters,
        RequiresNode precondition)
    {
        ResetUnhashedKindFlag();
        var sb = new StringBuilder();
        sb.Append("PRE:");
        AppendParameters(sb, parameters);
        sb.Append("::");
        AppendExpression(sb, precondition.Condition);

        return ComputeSha256Hash(sb.ToString());
    }

    /// <summary>
    /// Computes a hash key for a postcondition.
    /// Format: POST:{params}:{output}:PRECS:{prec_hashes}::POST:{post_hash}[::BODY:{body}]
    /// The body component is included exactly when the postcondition references
    /// <c>result</c> — the verifier binds <c>result</c> to the body in that case
    /// (guarantees plan D-G1.1), so a body edit must invalidate the cached proof.
    /// </summary>
    public string HashPostcondition(
        IReadOnlyList<(string Name, string TypeName)> parameters,
        string? outputType,
        IReadOnlyList<RequiresNode> preconditions,
        EnsuresNode postcondition,
        IReadOnlyList<StatementNode>? body = null)
    {
        ResetUnhashedKindFlag();
        var sb = new StringBuilder();
        sb.Append("POST:");
        AppendParameters(sb, parameters);
        sb.Append(':');
        sb.Append(outputType ?? "void");
        sb.Append(":PRECS:");

        // Include all preconditions in the hash since they affect postcondition verification
        foreach (var pre in preconditions)
        {
            AppendExpression(sb, pre.Condition);
            sb.Append(';');
        }

        sb.Append("::POST:");
        AppendExpression(sb, postcondition.Condition);

        if (body != null && FunctionBodyEncoder.ReferencesResult(postcondition.Condition))
        {
            sb.Append("::BODY:");
            AppendStatements(sb, body);
        }

        return ComputeSha256Hash(sb.ToString());
    }

    /// <summary>
    /// Serializes a statement list for hashing. Return and if/elseif/else statements —
    /// the encodable surface — serialize structurally with their expressions; any other
    /// statement kind serializes as an opaque marker with its node type, which is
    /// collision-safe because such bodies always verify as Unsupported regardless of
    /// their content.
    /// </summary>
    private void AppendStatements(StringBuilder sb, IReadOnlyList<StatementNode> statements)
    {
        foreach (var stmt in statements)
        {
            switch (stmt)
            {
                case ReturnStatementNode ret:
                    sb.Append("R(");
                    if (ret.Expression != null)
                        AppendExpression(sb, ret.Expression);
                    sb.Append(')');
                    break;

                case BindStatementNode bind:
                    // Immutable bindings are encodable (D-G3.1) and must hash their
                    // FULL content — two bodies differing only in an initializer must
                    // never share a cached result-bound verdict. Mutable bindings are
                    // unencodable (never cached) but must still hash DISTINCTLY from
                    // the immutable spelling.
                    sb.Append(bind.IsMutable ? "B~(" : "B(");
                    AppendRaw(sb, bind.Name);
                    sb.Append(':');
                    AppendRaw(sb, bind.TypeName ?? "?");
                    sb.Append('=');
                    if (bind.Initializer != null)
                        AppendExpression(sb, bind.Initializer);
                    sb.Append(')');
                    break;

                case IfStatementNode ifStmt:
                    sb.Append("IF(");
                    AppendExpression(sb, ifStmt.Condition);
                    sb.Append("){");
                    AppendStatements(sb, ifStmt.ThenBody);
                    sb.Append('}');
                    foreach (var clause in ifStmt.ElseIfClauses)
                    {
                        sb.Append("EI(");
                        AppendExpression(sb, clause.Condition);
                        sb.Append("){");
                        AppendStatements(sb, clause.Body);
                        sb.Append('}');
                    }
                    if (ifStmt.ElseBody != null)
                    {
                        sb.Append("EL{");
                        AppendStatements(sb, ifStmt.ElseBody);
                        sb.Append('}');
                    }
                    break;

                default:
                    sb.Append("OPAQUE:");
                    sb.Append(stmt.GetType().Name);
                    break;
            }
            sb.Append(';');
        }
    }

    /// <summary>
    /// Gets the canonical string representation of an expression (for testing).
    /// </summary>
    public string GetCanonicalExpression(ExpressionNode expression)
    {
        ResetUnhashedKindFlag(); // #914 review F5: no stale flag across calls
        var sb = new StringBuilder();
        AppendExpression(sb, expression);
        return sb.ToString();
    }

    private void AppendParameters(StringBuilder sb, IReadOnlyList<(string Name, string TypeName)> parameters)
    {
        for (int i = 0; i < parameters.Count; i++)
        {
            if (i > 0)
                sb.Append(',');
            AppendRaw(sb, parameters[i].Name);
            sb.Append(':');
            AppendRaw(sb, parameters[i].TypeName);
        }
    }

    /// <summary>#914 review F4: raw caller-supplied text (names, type spellings) is
    /// length-prefixed so delimiter characters inside it cannot forge serialization
    /// structure. Parser-produced identifiers cannot contain delimiters (lexer
    /// restricts them), but the cache is public SDK surface — collision resistance
    /// must hold by construction, not by lexer accident. Confirmed collision pre-fix:
    /// parameters [("a","T"),("b","U")] vs [("a","T,b:U")] hashed identically.</summary>
    private static void AppendRaw(StringBuilder sb, string text)
    {
        sb.Append(text.Length);
        sb.Append('#');
        sb.Append(text);
    }

    private void AppendExpression(StringBuilder sb, ExpressionNode expr)
    {
        switch (expr)
        {
            case IntLiteralNode intLit:
                sb.Append("INT:");
                sb.Append(intLit.Value);
                break;

            case BoolLiteralNode boolLit:
                sb.Append("BOOL:");
                sb.Append(boolLit.Value ? "true" : "false");
                break;

            case FloatLiteralNode floatLit:
                sb.Append("FLOAT:");
                sb.Append(floatLit.Value.ToString("G17"));
                break;

            case StringLiteralNode strLit:
                sb.Append("STR:\"");
                sb.Append(strLit.Value.Replace("\\", "\\\\").Replace("\"", "\\\""));
                sb.Append('"');
                break;

            case ReferenceNode refNode:
                sb.Append("REF:");
                AppendRaw(sb, refNode.Name);
                break;

            case BinaryOperationNode binOp:
                sb.Append('(');
                sb.Append(GetOperatorSymbol(binOp.Operator));
                sb.Append(' ');
                AppendExpression(sb, binOp.Left);
                sb.Append(' ');
                AppendExpression(sb, binOp.Right);
                sb.Append(')');
                break;

            case UnaryOperationNode unaryOp:
                sb.Append('(');
                sb.Append(GetUnaryOperatorSymbol(unaryOp.Operator));
                sb.Append(' ');
                AppendExpression(sb, unaryOp.Operand);
                sb.Append(')');
                break;

            case ForallExpressionNode forall:
                sb.Append("(FORALL (");
                foreach (var bv in forall.BoundVariables)
                {
                    sb.Append('(');
                    AppendRaw(sb, bv.Name);
                    sb.Append(' ');
                    AppendRaw(sb, bv.TypeName);
                    sb.Append(')');
                }
                sb.Append(") ");
                AppendExpression(sb, forall.Body);
                sb.Append(')');
                break;

            case ExistsExpressionNode exists:
                sb.Append("(EXISTS (");
                foreach (var bv in exists.BoundVariables)
                {
                    sb.Append('(');
                    AppendRaw(sb, bv.Name);
                    sb.Append(' ');
                    AppendRaw(sb, bv.TypeName);
                    sb.Append(')');
                }
                sb.Append(") ");
                AppendExpression(sb, exists.Body);
                sb.Append(')');
                break;

            case ImplicationExpressionNode impl:
                sb.Append("(-> ");
                AppendExpression(sb, impl.Antecedent);
                sb.Append(' ');
                AppendExpression(sb, impl.Consequent);
                sb.Append(')');
                break;

            case ConditionalExpressionNode cond:
                sb.Append("(ITE ");
                AppendExpression(sb, cond.Condition);
                sb.Append(' ');
                AppendExpression(sb, cond.WhenTrue);
                sb.Append(' ');
                AppendExpression(sb, cond.WhenFalse);
                sb.Append(')');
                break;

            case ArrayAccessNode arrAccess:
                sb.Append("(IDX ");
                AppendExpression(sb, arrAccess.Array);
                sb.Append(' ');
                AppendExpression(sb, arrAccess.Index);
                sb.Append(')');
                break;

            case ArrayLengthNode arrLen:
                sb.Append("(LEN ");
                AppendExpression(sb, arrLen.Array);
                sb.Append(')');
                break;

            // #824 review C2: these kinds are ENCODABLE (walker + translator model
            // them), so their cacheable verdicts must hash on CONTENT — the
            // content-free UNSUPPORTED marker let `(len s)` and `(len u)` share a
            // key and serve a stale false Proven from the default-on cache. Hash
            // changes here strand only previously-COLLIDING (broken) keys; no
            // format bump needed (recorded per the semantics-ledger rule).
            case StringOperationNode strOp:
                sb.Append("(SOP:");
                sb.Append(strOp.Operation);
                if (strOp.ComparisonMode != null)
                {
                    sb.Append(':');
                    sb.Append(strOp.ComparisonMode);
                }
                foreach (var arg in strOp.Arguments)
                {
                    sb.Append(' ');
                    AppendExpression(sb, arg);
                }
                sb.Append(')');
                break;

            case FieldAccessNode fieldAccess:
                sb.Append("(FLD ");
                AppendExpression(sb, fieldAccess.Target);
                sb.Append(' ');
                AppendRaw(sb, fieldAccess.FieldName);
                sb.Append(')');
                break;

            // #778: SelfRefNode is on the ModeledForms whitelist (refinement `#`), so
            // it can appear in CACHED contracts — it must not fall to the default arm.
            // It is contentless, so a fixed token is exact.
            case SelfRefNode:
                sb.Append("SELF");
                break;

            default:
                // #778: an expression kind with no serializer here CANNOT be given a
                // collision-safe key (two distinct instances of the same kind would
                // share a hash). The flag makes the cache refuse to read or write
                // under such a key — defense in depth behind the ModeledForms
                // whitelist, which should have refused the contract before any
                // cacheable verdict existed. The marker stays in the hash text for
                // debuggability, but no cache round-trip consumes it.
                SawUnhashedKind = true;
                sb.Append("UNSUPPORTED:");
                sb.Append(expr.GetType().Name);
                break;
        }
    }

    /// <summary>#778: true when the most recent Hash* call encountered an expression
    /// kind AppendExpression cannot serialize with content. Reset at the start of each
    /// Hash* call; the cache must skip both lookup and store when set. NOT thread-safe —
    /// callers serialize access (VerificationCache holds its hasher lock across the
    /// hash + flag read).</summary>
    public bool SawUnhashedKind { get; private set; }

    internal void ResetUnhashedKindFlag() => SawUnhashedKind = false;

    private static string GetOperatorSymbol(BinaryOperator op)
    {
        return op switch
        {
            BinaryOperator.Add => "+",
            BinaryOperator.Subtract => "-",
            BinaryOperator.Multiply => "*",
            BinaryOperator.Divide => "/",
            BinaryOperator.Modulo => "%",
            BinaryOperator.Power => "**",
            BinaryOperator.Equal => "==",
            BinaryOperator.NotEqual => "!=",
            BinaryOperator.LessThan => "<",
            BinaryOperator.LessOrEqual => "<=",
            BinaryOperator.GreaterThan => ">",
            BinaryOperator.GreaterOrEqual => ">=",
            BinaryOperator.And => "&&",
            BinaryOperator.Or => "||",
            BinaryOperator.BitwiseAnd => "&",
            BinaryOperator.BitwiseOr => "|",
            BinaryOperator.BitwiseXor => "^",
            BinaryOperator.LeftShift => "<<",
            BinaryOperator.RightShift => ">>",
            _ => op.ToString()
        };
    }

    private static string GetUnaryOperatorSymbol(UnaryOperator op)
    {
        return op switch
        {
            UnaryOperator.Negate => "-",
            UnaryOperator.Not => "!",
            UnaryOperator.BitwiseNot => "~",
            _ => op.ToString()
        };
    }

    private static string ComputeSha256Hash(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
