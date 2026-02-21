using Calor.Compiler.Parsing;

namespace Calor.Compiler.Diagnostics;

/// <summary>
/// Diagnostic severity levels.
/// </summary>
public enum DiagnosticSeverity
{
    Error,
    Warning,
    Info
}

/// <summary>
/// Standard diagnostic codes for the Calor compiler.
/// </summary>
public static class DiagnosticCode
{
    // Lexer errors (Calor0001-0099)
    public const string UnexpectedCharacter = "Calor0001";
    public const string UnterminatedString = "Calor0002";
    public const string InvalidTypedLiteral = "Calor0003";
    public const string InvalidEscapeSequence = "Calor0004";
    public const string UnterminatedRawBlock = "Calor0005";
    public const string UnknownSectionMarker = "Calor0006";
    public const string InvalidSectionOperator = "Calor0007";

    // Parser errors (Calor0100-0199)
    public const string UnexpectedToken = "Calor0100";
    public const string MismatchedId = "Calor0101";
    public const string MissingRequiredAttribute = "Calor0102";
    public const string ExpectedKeyword = "Calor0103";
    public const string ExpectedExpression = "Calor0104";
    public const string ExpectedClosingTag = "Calor0105";
    public const string InvalidOperator = "Calor0106";
    public const string InvalidModifier = "Calor0107";

    // Parser validation errors (Calor0110-0119)
    public const string OperatorArgumentCount = "Calor0110";
    public const string InvalidComparisonMode = "Calor0111";
    public const string InvalidCharLiteral = "Calor0112";
    public const string ExpectedTypeName = "Calor0113";
    public const string InvalidLispExpression = "Calor0114";
    public const string TypeParameterNotFound = "Calor0115";

    // Semantic errors (Calor0200-0299)
    public const string UndefinedReference = "Calor0200";
    public const string DuplicateDefinition = "Calor0201";
    public const string TypeMismatch = "Calor0202";
    public const string InvalidReference = "Calor0203";

    /// <summary>
    /// Error: Extension method must have a parameter of the extended type.
    /// </summary>
    public const string MissingExtensionSelf = "Calor0204";

    // Contract errors (Calor0300-0399)
    public const string InvalidPrecondition = "Calor0300";
    public const string InvalidPostcondition = "Calor0301";
    public const string ContractViolation = "Calor0302";

    // Quantifier diagnostics (Calor0320-0329)
    /// <summary>
    /// Error: Quantifier has no bound variables.
    /// </summary>
    public const string QuantifierNoBoundVars = "Calor0320";

    /// <summary>
    /// Warning: Quantifier over infinite range cannot be checked at runtime.
    /// </summary>
    public const string QuantifierInfiniteRange = "Calor0321";

    /// <summary>
    /// Error: Bound variable shadows an outer variable.
    /// </summary>
    public const string QuantifierVariableShadowing = "Calor0322";

    /// <summary>
    /// Info: Quantifier is static-only (Z3 verification, no runtime check).
    /// </summary>
    public const string QuantifierStaticOnly = "Calor0323";

    /// <summary>
    /// Warning: Quantifier variable has non-integer type, which may not support finite range iteration.
    /// </summary>
    public const string QuantifierNonIntegerType = "Calor0324";

    /// <summary>
    /// Info: Nested or multi-variable quantifier may result in O(n^k) runtime complexity.
    /// </summary>
    public const string QuantifierNestedComplexity = "Calor0325";

    // Effect errors (Calor0400-0499)
    public const string UndeclaredEffect = "Calor0400";
    public const string UnusedEffectDeclaration = "Calor0401";
    public const string EffectMismatch = "Calor0402";

    // Effect enforcement (Calor0410-0419)
    public const string ForbiddenEffect = "Calor0410";
    public const string UnknownExternalCall = "Calor0411";
    public const string MissingSpecificEffect = "Calor0412";
    public const string AmbiguousStub = "Calor0413";

    // Pattern matching errors (Calor0500-0599)
    public const string NonExhaustiveMatch = "Calor0500";
    public const string UnreachablePattern = "Calor0501";
    public const string DuplicatePattern = "Calor0502";
    public const string InvalidPatternForType = "Calor0503";

    // API strictness errors (Calor0600-0699)
    public const string BreakingChangeWithoutMarker = "Calor0600";
    public const string MissingDocComment = "Calor0601";
    public const string PublicApiChanged = "Calor0602";

    // Semantics version (Calor0700-0799)
    /// <summary>
    /// Warning: Module declares a newer semantics version than the compiler supports.
    /// The code may use features not available in this compiler version.
    /// </summary>
    public const string SemanticsVersionMismatch = "Calor0700";

    /// <summary>
    /// Error: Module declares an incompatible semantics version (major version mismatch).
    /// The code cannot be compiled with this compiler version.
    /// </summary>
    public const string SemanticsVersionIncompatible = "Calor0701";

    // ID errors (Calor0800-0899)
    /// <summary>
    /// Error: Declaration is missing a required ID.
    /// </summary>
    public const string Calor0800 = "Calor0800";

    /// <summary>
    /// Error: ID has an invalid format (not a valid ULID or test ID).
    /// </summary>
    public const string Calor0801 = "Calor0801";

    /// <summary>
    /// Error: ID prefix doesn't match the declaration kind.
    /// </summary>
    public const string Calor0802 = "Calor0802";

    /// <summary>
    /// Error: Duplicate ID detected across declarations.
    /// </summary>
    public const string Calor0803 = "Calor0803";

    /// <summary>
    /// Error: Test ID (e.g., f001) used in production code.
    /// </summary>
    public const string Calor0804 = "Calor0804";

    /// <summary>
    /// Error: ID churn detected (existing ID was modified).
    /// </summary>
    public const string Calor0805 = "Calor0805";

    // Contract inheritance (Calor0810-0814)

    /// <summary>
    /// Error: LSP violation - implementer has stronger precondition than interface.
    /// </summary>
    public const string StrongerPrecondition = "Calor0810";

    /// <summary>
    /// Error: LSP violation - implementer has weaker postcondition than interface.
    /// </summary>
    public const string WeakerPostcondition = "Calor0811";

    /// <summary>
    /// Info: Contracts inherited from interface.
    /// </summary>
    public const string InheritedContracts = "Calor0812";

    /// <summary>
    /// Warning: Interface method not implemented.
    /// </summary>
    public const string InterfaceMethodNotFound = "Calor0813";

    /// <summary>
    /// Info: Contract inheritance is valid.
    /// </summary>
    public const string ContractInheritanceValid = "Calor0814";

    // Contract inheritance Z3 proving (Calor0815-0817)

    /// <summary>
    /// Info: Contract implication proven by Z3 SMT solver.
    /// </summary>
    public const string ImplicationProvenByZ3 = "Calor0815";

    /// <summary>
    /// Warning: Contract implication could not be determined (Z3 timeout or complexity).
    /// </summary>
    public const string ImplicationUnknown = "Calor0816";

    /// <summary>
    /// Info: Z3 SMT solver is unavailable, using heuristic checking only.
    /// </summary>
    public const string Z3UnavailableForInheritance = "Calor0817";

    // Contract simplification (Calor0330-0339)

    /// <summary>
    /// Info: Contract expression is a tautology (always true).
    /// </summary>
    public const string ContractTautology = "Calor0330";

    /// <summary>
    /// Warning: Contract expression is a contradiction (always false).
    /// </summary>
    public const string ContractContradiction = "Calor0331";

    /// <summary>
    /// Info: Contract expression was simplified.
    /// </summary>
    public const string ContractSimplified = "Calor0332";

    // Dataflow analysis (Calor0900-0919)

    /// <summary>
    /// Error/Warning: Variable is used before initialization.
    /// </summary>
    public const string UninitializedVariable = "Calor0900";

    /// <summary>
    /// Warning: Dead code detected (unreachable statement).
    /// </summary>
    public const string DeadCode = "Calor0901";

    /// <summary>
    /// Warning: Assignment to variable that is never read (dead store).
    /// </summary>
    public const string DeadStore = "Calor0902";

    /// <summary>
    /// Info: Variable is redefined without being used.
    /// </summary>
    public const string RedefinedWithoutUse = "Calor0903";

    // Bug pattern detection (Calor0920-0949)

    /// <summary>
    /// Error: Potential division by zero.
    /// </summary>
    public const string DivisionByZero = "Calor0920";

    /// <summary>
    /// Error: Potential array index out of bounds.
    /// </summary>
    public const string IndexOutOfBounds = "Calor0921";

    /// <summary>
    /// Error: Potential null/None dereference.
    /// </summary>
    public const string NullDereference = "Calor0922";

    /// <summary>
    /// Warning: Potential integer overflow.
    /// </summary>
    public const string IntegerOverflow = "Calor0923";

    /// <summary>
    /// Warning: Result of operation discarded (potential logic error).
    /// </summary>
    public const string DiscardedResult = "Calor0924";

    /// <summary>
    /// Error: Unwrap on Option/Result without prior check.
    /// </summary>
    public const string UnsafeUnwrap = "Calor0925";

    // K-induction / loop analysis (Calor0950-0979)

    /// <summary>
    /// Info: Loop invariant successfully synthesized.
    /// </summary>
    public const string LoopInvariantSynthesized = "Calor0950";

    /// <summary>
    /// Warning: Loop invariant could not be synthesized.
    /// </summary>
    public const string LoopInvariantUnknown = "Calor0951";

    /// <summary>
    /// Error: Loop may not terminate (potential infinite loop).
    /// </summary>
    public const string PotentialInfiniteLoop = "Calor0952";

    /// <summary>
    /// Info: Loop bound proven by k-induction.
    /// </summary>
    public const string LoopBoundProven = "Calor0953";

    // Taint tracking / security (Calor0980-0999)

    /// <summary>
    /// Error: Tainted data flows to security-sensitive sink (e.g., SQL injection).
    /// </summary>
    public const string TaintedSink = "Calor0980";

    /// <summary>
    /// Warning: Potential SQL injection vulnerability.
    /// </summary>
    public const string SqlInjection = "Calor0981";

    /// <summary>
    /// Warning: Potential command injection vulnerability.
    /// </summary>
    public const string CommandInjection = "Calor0982";

    /// <summary>
    /// Warning: Potential path traversal vulnerability.
    /// </summary>
    public const string PathTraversal = "Calor0983";

    /// <summary>
    /// Warning: Potential cross-site scripting (XSS) vulnerability.
    /// </summary>
    public const string CrossSiteScripting = "Calor0984";

    /// <summary>
    /// Info: Taint source identified.
    /// </summary>
    public const string TaintSource = "Calor0985";

    /// <summary>
    /// Info: Sanitizer applied to tainted data.
    /// </summary>
    public const string TaintSanitized = "Calor0986";

    // Code generation validation (Calor1000-1009)

    /// <summary>
    /// Warning: Generated C# code contains syntax errors.
    /// </summary>
    public const string CodeGenSyntaxError = "Calor1000";
}

/// <summary>
/// Represents a compiler diagnostic (error, warning, or info).
/// </summary>
public sealed class Diagnostic
{
    public string Code { get; }
    public string Message { get; }
    public TextSpan Span { get; }
    public DiagnosticSeverity Severity { get; }
    public string? FilePath { get; }

    public Diagnostic(
        string code,
        string message,
        TextSpan span,
        DiagnosticSeverity severity = DiagnosticSeverity.Error,
        string? filePath = null)
    {
        Code = code;
        Message = message;
        Span = span;
        Severity = severity;
        FilePath = filePath;
    }

    public Diagnostic(
        string code,
        DiagnosticSeverity severity,
        string message,
        string? filePath,
        int line,
        int column)
    {
        Code = code;
        Message = message;
        Span = new TextSpan(0, 0, line, column);
        Severity = severity;
        FilePath = filePath;
    }

    public bool IsError => Severity == DiagnosticSeverity.Error;
    public bool IsWarning => Severity == DiagnosticSeverity.Warning;

    public override string ToString()
    {
        var location = FilePath != null
            ? $"{FilePath}({Span.Line},{Span.Column})"
            : $"({Span.Line},{Span.Column})";

        var severityText = Severity switch
        {
            DiagnosticSeverity.Error => "error",
            DiagnosticSeverity.Warning => "warning",
            DiagnosticSeverity.Info => "info",
            _ => "unknown"
        };

        return $"{location}: {severityText} {Code}: {Message}";
    }
}

/// <summary>
/// A diagnostic with an associated suggested fix.
/// </summary>
public sealed class DiagnosticWithFix
{
    public string Code { get; }
    public string Message { get; }
    public TextSpan Span { get; }
    public DiagnosticSeverity Severity { get; }
    public string? FilePath { get; }
    public SuggestedFix Fix { get; }

    public DiagnosticWithFix(
        string code,
        string message,
        TextSpan span,
        SuggestedFix fix,
        DiagnosticSeverity severity = DiagnosticSeverity.Error,
        string? filePath = null)
    {
        Code = code;
        Message = message;
        Span = span;
        Severity = severity;
        FilePath = filePath;
        Fix = fix ?? throw new ArgumentNullException(nameof(fix));
    }

    public bool IsError => Severity == DiagnosticSeverity.Error;
    public bool IsWarning => Severity == DiagnosticSeverity.Warning;
}

/// <summary>
/// A suggested fix for a diagnostic.
/// </summary>
public sealed class SuggestedFix
{
    /// <summary>
    /// Description of what the fix does.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// The edits to apply to fix the issue.
    /// </summary>
    public IReadOnlyList<TextEdit> Edits { get; }

    public SuggestedFix(string description, IReadOnlyList<TextEdit> edits)
    {
        Description = description ?? throw new ArgumentNullException(nameof(description));
        Edits = edits ?? throw new ArgumentNullException(nameof(edits));
    }

    public SuggestedFix(string description, TextEdit edit)
        : this(description, new[] { edit })
    {
    }
}

/// <summary>
/// A text edit to apply as part of a fix.
/// </summary>
public sealed class TextEdit
{
    public string FilePath { get; }
    public int StartLine { get; }
    public int StartColumn { get; }
    public int EndLine { get; }
    public int EndColumn { get; }
    public string NewText { get; }

    public TextEdit(string filePath, int startLine, int startColumn, int endLine, int endColumn, string newText)
    {
        FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        StartLine = startLine;
        StartColumn = startColumn;
        EndLine = endLine;
        EndColumn = endColumn;
        NewText = newText ?? throw new ArgumentNullException(nameof(newText));
    }

    /// <summary>
    /// Create an insertion edit.
    /// </summary>
    public static TextEdit Insert(string filePath, int line, int column, string text)
        => new(filePath, line, column, line, column, text);

    /// <summary>
    /// Create a replacement edit.
    /// </summary>
    public static TextEdit Replace(string filePath, int startLine, int startColumn, int endLine, int endColumn, string text)
        => new(filePath, startLine, startColumn, endLine, endColumn, text);
}
