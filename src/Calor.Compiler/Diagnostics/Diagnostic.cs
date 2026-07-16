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

    /// <summary>
    /// Warning: leading whitespace uses tab characters. Calor indentation is
    /// canonically 2 spaces per level; tabs are tolerated (each counts as one
    /// column) but easily produce dedent mismatches when later lines use
    /// spaces. Reported once per file with a machine-applicable fix that
    /// rewrites every tab-indented line (tab → 2 spaces).
    /// </summary>
    public const string TabIndentation = "Calor0008";

    /// <summary>
    /// Warning: a new indentation level is not exactly 2 spaces deeper than
    /// its parent (e.g. 3- or 4-space steps). The file may still parse —
    /// indentation is stack-relative — but non-standard widths are the top
    /// source of agent authoring thrash. Carries a machine-applicable fix
    /// that re-indents the line to the canonical 2-spaces-per-level column.
    /// </summary>
    public const string NonStandardIndentWidth = "Calor0009";

    // Phase 3 lexer errors (RFC §4.1)
    public const string MixedIndentation = "Calor0099";

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

    /// <summary>
    /// Error: A <c>§F</c>/<c>§AF</c> function header carries four positional
    /// fields (<c>{id:name:type:vis}</c>) where the third is a return type and
    /// the fourth a visibility. Function headers take at most
    /// <c>{id:name:visibility}</c>; the return type belongs in the signature
    /// (<c>(...) -&gt; type</c>). Left unflagged, the extra field is silently
    /// dropped — the parser reads the type as the visibility and discards the
    /// real visibility — emitting a void method (e.g. <c>void Add() { return 0; }</c>,
    /// CS0127). Only <c>§F</c>/<c>§AF</c> are affected; <c>§MT</c>/<c>§AMT</c>
    /// legitimately take a fourth modifier field.
    /// </summary>
    public const string MalformedFunctionHeader = "Calor0116";

    /// <summary>
    /// Error: a <c>§EI</c>/<c>§EL</c> clause appears where a statement was
    /// expected — its indentation does not align with any open <c>§IF</c>.
    /// The most common agent mistake is dedenting the clause too far (past
    /// the <c>§IF</c> column) so the if-chain closes before the clause is
    /// seen. Carries a machine-applicable fix that re-indents the clause to
    /// the column of the nearest preceding <c>§IF</c>.
    /// </summary>
    public const string MisalignedElseClause = "Calor0117";

    // Call-elision diagnostics (Calor0150-0159) — RFC v0.6 call-closer-elision

    /// <summary>
    /// Error: A closer-less <c>§C{target}</c> call expression consumed one
    /// inline primary expression as its argument and then encountered a
    /// second expression-start token on the same call. The form
    /// <c>§C{f} a b</c> is ambiguous: <c>b</c> might be a second positional
    /// argument (needs <c>§A</c>) or an unrelated token. Use the explicit
    /// form <c>§C{f} §A a §A b §/C</c>. Per RFC v0.6 call-closer-elision §3.2
    /// case B.
    /// </summary>
    public const string AmbiguousCallContinuation = "Calor0150";

    // Semantic errors (Calor0200-0299)
    public const string UndefinedReference = "Calor0200";
    public const string DuplicateDefinition = "Calor0201";
    public const string TypeMismatch = "Calor0202";
    public const string InvalidReference = "Calor0203";

    /// <summary>
    /// Error: Extension method must have a parameter of the extended type.
    /// </summary>
    public const string MissingExtensionSelf = "Calor0204";

    /// <summary>
    /// Error: a value-returning <c>§R expr</c> appears in the body of an owner
    /// that returns no value — a <c>void</c>/<c>Task</c> function or method, an
    /// iterator (its body yields), a constructor, a property/indexer setter or
    /// init accessor, or an event add/remove accessor. The generated C# would
    /// fail to compile (CS0127 "since it returns void" / CS1622 for iterators).
    /// The fix is either to declare a return type (<c>§O{type}</c> / a 3-field
    /// header return type) or to drop the value and use a bare <c>§R</c>.
    /// </summary>
    public const string ReturnValueInVoidOwner = "Calor0205";

    // Bind inference diagnostics (Calor0250-0259) — RFC v0.6 bind-inference-formalization

    /// <summary>
    /// Error: <c>§B{name}</c> requires either a <c>:type</c> annotation or
    /// an initializer expression. Previously the binder silently defaulted
    /// to <c>INT</c> in this case; that was a latent bug because misuse
    /// produced wrong-typed code with no diagnostic.
    /// </summary>
    public const string BindRequiresTypeOrInitializer = "Calor0250";

    /// <summary>
    /// Error: <c>§B{name} none</c> / <c>§B{name} null</c> cannot infer a
    /// type — the right-hand side carries no concrete element type. The
    /// fix is to give the binding an explicit option type, e.g.
    /// <c>§B{name:Option&lt;T&gt;} none</c>.
    /// Default-on since v0.6.3; disable with <c>--no-strict-bind-inference</c>.
    /// See RFC v0.6 bind-inference-formalization §3.2 / §6.
    /// </summary>
    public const string BindCannotInferNullLiteral = "Calor0251";

    /// <summary>
    /// Error: <c>§B{name} §C{Foo.bar}</c> cannot infer a type when
    /// <c>Foo.bar</c> returns a generic with an unresolved type
    /// parameter (e.g. <c>Vec&lt;T&gt;.empty</c>). The fix is to give
    /// the binding an explicit type argument.
    /// Default-on since v0.6.3; disable with <c>--no-strict-bind-inference</c>.
    /// See RFC v0.6 bind-inference-formalization §3.2 / §6.
    /// </summary>
    public const string BindCannotInferGenericReturn = "Calor0252";

    /// <summary>
    /// Error: <c>§B{name} (+ INT:0 FLOAT:0.0)</c> — the initializer mixes
    /// numeric types and widening could pick more than one bound type.
    /// The fix is to give the binding an explicit numeric annotation
    /// (<c>:i32</c>, <c>:f64</c>, …).
    /// Default-on since v0.6.3; disable with <c>--no-strict-bind-inference</c>.
    /// See RFC v0.6 bind-inference-formalization §3.2 / §6.
    /// </summary>
    public const string BindAmbiguousNumeric = "Calor0253";

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
    public const string ILResolvedEffect = "Calor0414";
    public const string ILAnalysisFallback = "Calor0415";
    public const string ILAnalysisBudgetExhausted = "Calor0416";

    /// <summary>
    /// Warning: Public Calor function has no §E effect declaration. Cross-module callers cannot verify effect safety.
    /// </summary>
    public const string UndeclaredPublicFunction = "Calor0417";

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

    // Contract verification results (Calor0702-0705) — emitted by
    // Verification/ContractVerificationPass. Note: the verification pass also
    // reuses Calor0700 (Z3 unavailable, info) and Calor0701 (precondition may
    // be violated, warning) with meanings that differ from the semantics-version
    // constants above; that pre-existing collision is preserved for
    // compatibility and is scheduled for renumbering.

    /// <summary>
    /// Warning: Z3 disproved a postcondition; a counterexample is reported.
    /// </summary>
    public const string PostconditionMayBeViolated = "Calor0702";

    /// <summary>
    /// Info: a postcondition was statically proven; its runtime check is elided.
    /// </summary>
    public const string PostconditionProven = "Calor0703";

    /// <summary>
    /// Info: per-module contract verification summary (proven / unproven /
    /// potentially violated / unsupported counts).
    /// </summary>
    public const string VerificationSummary = "Calor0704";

    /// <summary>
    /// Info (verbose): verification cache statistics.
    /// </summary>
    public const string VerificationCacheStats = "Calor0705";

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

    // Legacy structural-ID lint (Calor0820-0822) — Phase 1/2 v6 plan
    // (drop structural IDs, then introduce compact 12-char IDs).

    /// <summary>
    /// Info (opt-in lint): a structural opener still carries a legacy
    /// <c>{id:…}</c> block that the Phase 1 migrator (<c>calor fix
    /// --drop-structural-ids</c>) can safely remove.
    ///
    /// Per RFC §5.7 the diagnostic is informational and must include a
    /// machine-applicable suggested fix (the byte range to delete).
    /// </summary>
    public const string LegacyStructuralId = "Calor0820";

    /// <summary>
    /// Info (opt-in lint, Phase 2): a Calor declaration uses a 26-char
    /// ULID payload that the compact migrator (<c>calor fix
    /// --compact-ids</c>) can rewrite to a 12-char Crockford-lowercase
    /// compact ID.
    /// </summary>
    public const string LegacyUlidPayload = "Calor0821";

    /// <summary>
    /// Warning (Phase 2): two declarations in the compile unit produced
    /// the same compact ID. Indicates a generator collision and is
    /// surfaced as a hard error in the registry path.
    /// </summary>
    public const string CompactIdCollision = "Calor0822";

    /// <summary>
    /// Error (Phase 4d): a legacy structural closing tag (e.g.
    /// <c>§/F</c>, <c>§/CL</c>, <c>§/L</c>, <c>§/M</c>) is present in
    /// source. Closer form was removed in Phase 4d — indent form alone
    /// now terminates every structural block. Closers that still carry
    /// payload (<c>§/DO</c> condition, <c>§/PP</c> condition, <c>§/K</c>
    /// case delimiter) and inline expression closers (<c>§/C</c>,
    /// <c>§/T</c>, <c>§/NEW</c>, etc.) are NOT flagged.
    ///
    /// The fix is to delete the entire closer line; the body's indentation
    /// already marks where the block ends. The diagnostic carries a
    /// <see cref="SuggestedFix"/> that performs this deletion, so the LSP
    /// quick-fix and the <c>calor_check</c> MCP tool (<c>apply: true</c>)
    /// can heal the source automatically. (Note: <c>calor format</c> and
    /// <c>calor lint --fix</c> cannot — they parse first and abort on this
    /// very error.)
    /// </summary>
    public const string LegacyCloserForm = "Calor0830";

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

    /// <summary>
    /// Warning: Division by parameter without precondition.
    /// </summary>
    public const string MissingPrecondition = "Calor0926";

    /// <summary>
    /// Warning: Potential off-by-one error in loop bounds with array access.
    /// </summary>
    public const string OffByOne = "Calor0927";

    /// <summary>
    /// Info: Contract inferred from function body analysis.
    /// </summary>
    public const string InferredContract = "Calor0928";

    // Class member analysis (Calor0930-0949)

    /// <summary>
    /// Warning: Analysis of a class member was skipped due to a known limitation.
    /// </summary>
    public const string AnalysisSkipped = "Calor0930";

    /// <summary>
    /// Info: A statement type is not fully supported in analysis and is treated as opaque.
    /// Deduplicated per NodeTypeName per file.
    /// </summary>
    public const string AnalysisUnsupportedNode = "Calor0931";

    /// <summary>
    /// Error: Internal compiler error during class member analysis.
    /// </summary>
    public const string AnalysisICE = "Calor0932";

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

    // C# Interop diagnostics (Calor1010-1019)

    /// <summary>
    /// Error: Unterminated §CSHARP interop block (missing }§/CSHARP).
    /// </summary>
    public const string UnterminatedCSharpInteropBlock = "Calor1010";

    /// <summary>
    /// Info: Raw C# code preserved in interop block for unsupported feature.
    /// </summary>
    public const string CSharpInteropBlockPreserved = "Calor1011";

    // Refinement type diagnostics (Calor1100-1109)

    /// <summary>
    /// Error: Refinement type predicate does not evaluate to a boolean.
    /// </summary>
    public const string RefinementPredicateNotBoolean = "Calor1100";

    /// <summary>
    /// Error: Self-reference placeholder (#) used outside a refinement predicate.
    /// </summary>
    public const string SelfRefOutsidePredicate = "Calor1101";

    /// <summary>
    /// Error: Refinement type references an undefined base type.
    /// </summary>
    public const string RefinementUndefinedBaseType = "Calor1102";

    /// <summary>
    /// Error: Duplicate refinement type name.
    /// </summary>
    public const string RefinementDuplicateName = "Calor1103";

    // Obligation verification diagnostics (Calor1120-1141)

    /// <summary>
    /// Info: Obligation successfully discharged (proven).
    /// </summary>
    public const string ObligationDischarged = "Calor1120";

    /// <summary>
    /// Error: Obligation failed — counterexample found.
    /// </summary>
    public const string ObligationFailed = "Calor1121";

    /// <summary>
    /// Warning: Obligation solver timed out.
    /// </summary>
    public const string ObligationTimeout = "Calor1122";

    /// <summary>
    /// Info: Boundary obligation — requires runtime check.
    /// </summary>
    public const string ObligationBoundary = "Calor1123";

    /// <summary>
    /// Warning: Obligation contains unsupported constructs.
    /// </summary>
    public const string ObligationUnsupported = "Calor1124";

    /// <summary>
    /// Error: Proof obligation failed.
    /// </summary>
    public const string ProofObligationFailed = "Calor1140";

    /// <summary>
    /// Info: Proof obligation discharged (proven).
    /// </summary>
    public const string ProofObligationDischarged = "Calor1141";

    // Experimental feature pilot diagnostics (Calor1200-1299) —
    // reserved for flag-plumbing verification and other short-lived signals
    // from the research-program (docs/plans/calor-native-type-system-v2.md)
    // Phase 0. Not intended to appear in shipped features.

    /// <summary>
    /// Info: An experimental feature flag was enabled for this compilation.
    /// Used by the Phase 0a pilot flag to verify end-to-end plumbing from CLI
    /// and MSBuild property through CompilationOptions. Emits once per
    /// compilation when the <c>pilot-hello-world</c> flag is enabled.
    /// </summary>
    public const string ExperimentalFlagPilot = "Calor1200";

    // CLI diagnostics (Calor1300-1399) — issues surfaced by CLI commands
    // themselves (file resolution, usage errors, lint style findings) rather
    // than by the compilation pipeline. Carrying stable codes lets them flow
    // through the structured output formats (--format json|sarif).

    // `calor lint` style findings (Calor1300-1309)

    /// <summary>
    /// Warning (lint): a line has trailing whitespace.
    /// </summary>
    public const string LintTrailingWhitespace = "Calor1300";

    /// <summary>
    /// Warning (lint): a construct ID is not in abbreviated form
    /// (e.g. <c>f001</c> instead of <c>f1</c>, or <c>for1</c> instead of <c>l1</c>).
    /// </summary>
    public const string LintNonAbbreviatedId = "Calor1301";

    /// <summary>
    /// Error (lint): an input file passed to <c>calor lint</c> does not exist.
    /// </summary>
    public const string LintFileNotFound = "Calor1302";

    /// <summary>
    /// Error (lint): an input file passed to <c>calor lint</c> is not a
    /// <c>.calr</c> file and cannot be linted.
    /// </summary>
    public const string LintUnsupportedFileType = "Calor1303";

    /// <summary>
    /// Error (lint): an unexpected exception occurred while linting a file.
    /// </summary>
    public const string LintProcessingError = "Calor1304";

    // Root compile command (Calor1310-1319)

    /// <summary>
    /// Error (CLI): an input file passed via <c>--input</c> does not exist.
    /// </summary>
    public const string CliInputNotFound = "Calor1310";

    /// <summary>
    /// Error (CLI): invalid combination of command-line arguments
    /// (e.g. <c>--output</c> with multiple <c>--input</c> files).
    /// </summary>
    public const string CliUsageError = "Calor1311";

    /// <summary>
    /// Error (CLI): an unhandled exception occurred during compilation.
    /// Emitted so structured output modes (<c>--format json|sarif</c>) still
    /// produce a parseable document on crash paths.
    /// </summary>
    public const string CliInternalError = "Calor1312";

    // `calor self-check docs` drift findings (Calor1320-1329) — agent-facing
    // documentation contradicts the implementation. See SelfCheck/DocDriftChecker.

    /// <summary>
    /// Error (docs drift): a §-keyword cited in agent-facing docs does not
    /// exist in the lexer's keyword table (the "§FOREACH vs §EACH" class).
    /// </summary>
    public const string DocDriftUnknownKeyword = "Calor1320";

    /// <summary>
    /// Error (docs drift): a diagnostic code cited in agent-facing docs is not
    /// defined in <see cref="DiagnosticCode"/> (the "Calor0820 vs Calor0830" class).
    /// </summary>
    public const string DocDriftUnknownDiagnosticCode = "Calor1321";

    /// <summary>
    /// Error (docs drift): a documented diagnostic-code range (band) contains
    /// no implemented diagnostic codes.
    /// </summary>
    public const string DocDriftEmptyDiagnosticRange = "Calor1322";

    /// <summary>
    /// Error (docs drift): an effect code listed in the docs is unknown to the
    /// compiler's effect-code registry.
    /// </summary>
    public const string DocDriftUnknownEffectCode = "Calor1323";

    /// <summary>
    /// Error (docs drift): an implemented (non-legacy) effect code is missing
    /// from the effect-code documentation (the undocumented-<c>mut</c> class).
    /// </summary>
    public const string DocDriftUndocumentedEffectCode = "Calor1324";

    /// <summary>
    /// Error (docs drift): a doc file hardcodes the current compiler version
    /// string; versions belong in Directory.Build.props only (the stale-version class).
    /// </summary>
    public const string DocDriftHardcodedVersion = "Calor1325";

    /// <summary>
    /// Error (docs drift): a file or doc section the self-check needs is
    /// missing or unreadable.
    /// </summary>
    public const string DocDriftMissingInput = "Calor1326";

    /// <summary>
    /// Error (docs drift): a CLI diagnostic code (Calor1300-1399) is not listed
    /// in docs/cli/structured-output.md's code table.
    /// </summary>
    public const string DocDriftUndocumentedCliCode = "Calor1327";

    /// <summary>
    /// Error (docs drift): a fenced ```calor example that declares a complete
    /// program (first non-blank line starts with §M) no longer lexes/parses
    /// with the current compiler — the example has rotted.
    /// </summary>
    public const string DocDriftExampleParseError = "Calor1328";
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
