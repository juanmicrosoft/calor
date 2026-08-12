using Calor.Compiler.Ast;
using Microsoft.Z3;

namespace Calor.Compiler.Verification.Z3;

/// <summary>
/// Translates Calor AST expressions to Z3 expressions using bit-vector arithmetic.
/// </summary>
/// <remarks>
/// <para>
/// <b>Thread Safety:</b> This class is NOT thread-safe. Each instance maintains internal state
/// (declared variables, expression metadata, warnings) that is modified during translation.
/// Create a new instance for each verification operation, or synchronize access externally.
/// The Z3 <see cref="Microsoft.Z3.Context"/> passed to the constructor should also be used
/// from a single thread unless Z3 was configured for multi-threaded use.
/// </para>
/// <para>
/// This translator uses Z3 bit-vectors instead of unbounded integers to correctly model
/// fixed-width arithmetic with wrap-around overflow semantics (two's complement).
/// </para>
/// <para>
/// <b>Supported types:</b> i8, i16, i32, i64, u8, u16, u32, u64, bool, string, arrays
/// </para>
/// <para>
/// <b>String support:</b> Uses Z3's native string theory for verification. Supported operations:
/// Length, Contains, StartsWith, EndsWith, Equals, IsNullOrEmpty, IndexOf, Substring, Concat.
/// (Replace is NOT modeled: Z3 replaces the first occurrence, .NET replaces all — W1 Slice 1.)
/// </para>
/// <para>
/// <b>Array support:</b> Arrays are modeled with 64-bit indices and typed elements. Each array
/// has an associated <c>$length</c> variable (e.g., <c>arr$length</c>) representing its length
/// as an unsigned 32-bit value.
/// </para>
/// <para>
/// <b>Limitation - Null strings:</b> Z3 strings cannot be null - they are always valid sequences.
/// The <c>IsNullOrEmpty</c> operation only checks if the string length equals zero. Code that
/// relies on null string semantics may not verify correctly.
/// </para>
/// <para>
/// <b>Limitation - String comparison modes:</b> The <see cref="StringComparisonMode"/> parameter
/// on string operations is ignored. Z3's string theory uses ordinal comparison only; case-insensitive
/// and culture-aware comparisons are not supported.
/// </para>
/// <para>
/// <b>Limitation - Unsupported string operations:</b> ToUpper, ToLower, Trim, TrimStart, TrimEnd,
/// PadLeft, PadRight, Split, Join, Format, ToString, and all Regex operations return null
/// (marked as Unsupported) because Z3 lacks native support for these operations.
/// </para>
/// </remarks>
public sealed class ContractTranslator
{
    /// <summary>
    /// Version of the executable-semantics model used to translate contracts.
    /// This value participates in verification cache validity.
    /// </summary>
    public const string SemanticsVersion = "z3-executable-semantics-v2";

    private readonly Context _ctx;
    private readonly Dictionary<string, (Expr Expr, string Type)> _variables = new();
    private readonly Stack<Dictionary<string, (Expr Expr, string Type)>> _scopeStack = new();

    /// <summary>
    /// Tracks metadata for bit-vector expressions (width and signedness).
    /// </summary>
    private readonly Dictionary<Expr, BitVecInfo> _exprInfo =
        new(ReferenceEqualityComparer.Instance);

    private record struct BitVecInfo(uint Width, bool IsSigned);

    /// <summary>
    /// Tracks metadata for string expressions (nullable flag for future null handling).
    /// </summary>
    private record struct StringInfo(bool IsNullable);
    private readonly Dictionary<Expr, StringInfo> _stringInfo = new();

    /// <summary>
    /// Tracks metadata for array expressions (element type and length expression).
    /// </summary>
    private record struct ArrayInfo(string ElementType, Expr? LengthExpr);
    private readonly Dictionary<string, ArrayInfo> _arrayInfo = new();

    /// <summary>
    /// Z3 uninterpreted sorts for user-defined types (classes). Cached per type name so that
    /// multiple variables of the same class share a sort, allowing Z3 to reason about field
    /// accessors as functions on that sort.
    /// </summary>
    private readonly Dictionary<string, Sort> _userTypeSorts = new();

    /// <summary>
    /// Z3 uninterpreted function declarations for field accessors on user-defined types,
    /// keyed by (type-name, field-name). Created on demand when a FieldAccessNode is
    /// translated. The result type is the field's declared Calor type from the module registry.
    /// </summary>
    private readonly Dictionary<(string TypeName, string FieldName), FuncDecl> _fieldAccessors = new();

    /// <summary>
    /// Optional registry of user-defined types and their fields, supplied by the verification
    /// pass when iterating a module. Lets the translator pick the correct Z3 result sort for
    /// each field accessor instead of falling back to bit-vector.
    /// </summary>
    private IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? _userTypeRegistry;

    /// <summary>
    /// Collects warnings about features that were silently ignored during translation.
    /// These don't cause translation failure but may result in unexpected verification behavior.
    /// </summary>
    private readonly List<string> _warnings = new();

    /// <summary>
    /// Stack of self-variable names for refinement predicate contexts.
    /// When translating a refinement predicate, # resolves to the variable at the top of this stack.
    /// </summary>
    private readonly Stack<string> _selfVariableStack = new();

    /// <summary>
    /// Gets warnings that were generated during translation.
    /// Warnings indicate features that were silently handled in a potentially unexpected way.
    /// </summary>
    /// <remarks>
    /// <b>Permanently empty since D4 (v0.12).</b> The only producer was the warn-and-approximate
    /// path for non-ordinal string comparison, and D4 is the demonstration that a warning is not an
    /// adequate response to a modeling divergence: a warning does not stop <c>Proven</c>, and
    /// <c>Proven &amp;&amp; !IsVacuous</c> deletes the runtime check — so the warning arrives in a
    /// build where the check it was warning about is already gone.
    /// <para>Retained deliberately as a hook rather than deleted: <c>Z3Verifier</c> plumbs it into
    /// <c>ContractVerificationResult.Warnings</c>, which is public API. Anything added here must
    /// first show it is <b>not</b> soundness-relevant — a soundness-relevant finding belongs in a
    /// refusal. Pinned by <c>TranslatorTests.ContractTranslator_HasNoWarningProducersInSource</c>.</para>
    /// </remarks>
    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>
    /// Clears accumulated warnings.
    /// </summary>
    public void ClearWarnings() => _warnings.Clear();

    public ContractTranslator(Context ctx)
    {
        _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
    }

    /// <summary>
    /// Registers a map of class type-name → (field-name → field-type) so that
    /// FieldAccess translation can produce correctly-typed Z3 functions. Missing entries
    /// are refused rather than modeled at a guessed width.
    /// </summary>
    public void SetUserTypeRegistry(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? registry)
    {
        _userTypeRegistry = registry;
    }

    /// <summary>
    /// Builds the field-type registry used by field-access translation from the module's
    /// class declarations. Partial declarations are merged, nested classes are qualified,
    /// and derived types include inherited non-private instance fields.
    /// </summary>
    internal static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>
        BuildUserTypeRegistry(ModuleNode module)
    {
        ArgumentNullException.ThrowIfNull(module);

        var declarations = EnumerateClasses(module.Classes)
            .GroupBy(entry => entry.QualifiedName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(entry => entry.Class).ToList(),
                StringComparer.Ordinal);
        var declaredTypeNames = declarations.Keys.ToHashSet(StringComparer.Ordinal);
        var directFields =
            new Dictionary<string, Dictionary<string, RegisteredField>>(StringComparer.Ordinal);
        var baseTypes = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var (typeName, partials) in declarations)
        {
            var fields = new Dictionary<string, RegisteredField>(StringComparer.Ordinal);
            foreach (var fieldGroup in partials
                         .SelectMany(cls => cls.Fields)
                         .GroupBy(field => field.Name, StringComparer.Ordinal))
            {
                var candidates = fieldGroup
                    .Select(field => new RegisteredField(
                        NormalizeTypeName(field.TypeName),
                        field.Visibility,
                        field.IsStatic))
                    .Distinct()
                    .ToList();
                if (candidates.Count == 1 && !string.IsNullOrEmpty(candidates[0].TypeName))
                    fields[fieldGroup.Key] = candidates[0];
            }

            directFields[typeName] = fields;

            var resolvedBases = partials
                .Select(cls => cls.BaseClass)
                .Where(baseClass => !string.IsNullOrWhiteSpace(baseClass))
                .Select(baseClass => ResolveDeclaredTypeName(
                    baseClass!,
                    typeName,
                    declaredTypeNames))
                .Where(baseClass => baseClass is not null)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            baseTypes[typeName] = resolvedBases.Count == 1 ? resolvedBases[0] : null;
        }

        var resolvedFields =
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        var resolving = new HashSet<string>(StringComparer.Ordinal);

        IReadOnlyDictionary<string, string> ResolveFields(string typeName)
        {
            if (resolvedFields.TryGetValue(typeName, out var cached))
                return cached;

            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!resolving.Add(typeName))
                return fields;

            if (baseTypes.TryGetValue(typeName, out var baseType) && baseType is not null)
            {
                foreach (var (fieldName, fieldType) in ResolveFields(baseType))
                {
                    var baseField = FindRegisteredField(baseType, fieldName, directFields, baseTypes);
                    if (baseField is not null && baseField.Visibility != Visibility.Private)
                        fields[fieldName] = fieldType;
                }
            }

            foreach (var (fieldName, field) in directFields[typeName])
            {
                if (field.IsStatic)
                    fields.Remove(fieldName);
                else
                    fields[fieldName] = field.TypeName;
            }

            resolving.Remove(typeName);
            resolvedFields[typeName] = fields;
            return fields;
        }

        foreach (var typeName in declarations.Keys.OrderBy(name => name, StringComparer.Ordinal))
            ResolveFields(typeName);

        // Nested types are canonically registered by qualified name. Preserve exact bare-name
        // lookup for methods inside an enclosing type only when that spelling is globally unique;
        // ambiguous nested names are omitted so translation refuses rather than picking a type.
        foreach (var simpleNameGroup in declarations.Keys
                     .GroupBy(GetSimpleTypeName, StringComparer.Ordinal)
                     .Where(group => group.Count() == 1))
        {
            var qualifiedName = simpleNameGroup.Single();
            if (!resolvedFields.ContainsKey(simpleNameGroup.Key))
                resolvedFields[simpleNameGroup.Key] = resolvedFields[qualifiedName];
        }

        return resolvedFields;
    }

    internal static string BuildUserTypeRegistryCacheScope(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> registry)
    {
        return string.Join(
            "|",
            registry.OrderBy(type => type.Key, StringComparer.Ordinal).Select(type =>
                $"{type.Key.Length}#{type.Key}:" +
                string.Join(
                    ",",
                    type.Value.OrderBy(field => field.Key, StringComparer.Ordinal)
                        .Select(field =>
                            $"{field.Key.Length}#{field.Key}={field.Value.Length}#{field.Value}"))));
    }

    private static RegisteredField? FindRegisteredField(
        string typeName,
        string fieldName,
        IReadOnlyDictionary<string, Dictionary<string, RegisteredField>> directFields,
        IReadOnlyDictionary<string, string?> baseTypes)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var currentType = typeName;
        while (visited.Add(currentType))
        {
            if (directFields.TryGetValue(currentType, out var fields)
                && fields.TryGetValue(fieldName, out var field))
            {
                return field;
            }

            if (!baseTypes.TryGetValue(currentType, out var baseType) || baseType is null)
                break;
            currentType = baseType;
        }

        return null;
    }

    private static string? ResolveDeclaredTypeName(
        string referencedType,
        string declaringType,
        IReadOnlySet<string> declaredTypes)
    {
        var normalizedReference = NormalizeTypeName(referencedType);
        if (declaredTypes.Contains(normalizedReference))
            return normalizedReference;

        var separator = declaringType.LastIndexOf('.');
        while (separator >= 0)
        {
            var candidate = $"{declaringType[..separator]}.{normalizedReference}";
            if (declaredTypes.Contains(candidate))
                return candidate;
            separator = declaringType.LastIndexOf('.', separator - 1);
        }

        return null;
    }

    private static string GetSimpleTypeName(string typeName)
    {
        var separator = typeName.LastIndexOf('.');
        return separator < 0 ? typeName : typeName[(separator + 1)..];
    }

    private static IEnumerable<(ClassDefinitionNode Class, string QualifiedName)> EnumerateClasses(
        IEnumerable<ClassDefinitionNode> classes,
        string? enclosingType = null)
    {
        foreach (var cls in classes)
        {
            var simpleName = NormalizeTypeName(cls.Name);
            var qualifiedName = enclosingType is null
                ? simpleName
                : $"{enclosingType}.{simpleName}";
            yield return (cls, qualifiedName);
            foreach (var nested in EnumerateClasses(cls.NestedClasses, qualifiedName))
                yield return nested;
        }
    }

    private sealed record RegisteredField(
        string TypeName,
        Visibility Visibility,
        bool IsStatic);

    /// <summary>
    /// Pushes a self-variable name for refinement predicate translation.
    /// When SelfRefNode (#) is encountered, it resolves to the variable with this name.
    /// </summary>
    public void PushSelfVariable(string variableName)
    {
        _selfVariableStack.Push(variableName);
    }

    /// <summary>
    /// Pops the current self-variable name.
    /// </summary>
    public void PopSelfVariable()
    {
        if (_selfVariableStack.Count > 0)
            _selfVariableStack.Pop();
    }

    /// <summary>
    /// Declares a variable with the given name and type.
    /// </summary>
    /// <param name="name">Variable name.</param>
    /// <param name="typeName">Calor type name (i32, bool, etc.).</param>
    /// <returns>True if the type is supported and variable was declared.</returns>
    public bool DeclareVariable(string name, string typeName)
    {
        var expr = CreateVariableForType(name, typeName);
        if (expr == null)
            return false;

        _variables[name] = (expr, typeName);
        return true;
    }

    /// <summary>
    /// Gets all declared variables.
    /// </summary>
    public IReadOnlyDictionary<string, (Expr Expr, string Type)> Variables => _variables;

    /// <summary>
    /// Translates a Calor expression to a Z3 boolean expression.
    /// Returns null if the expression contains unsupported constructs.
    /// </summary>
    public BoolExpr? TranslateBoolExpr(ExpressionNode node)
    {
        LastRefusalReason = null;
        var expr = Translate(node);
        return expr as BoolExpr;
    }

    /// <summary>
    /// Translates a Calor expression to a Z3 arithmetic expression.
    /// Returns null if the expression contains unsupported constructs.
    /// </summary>
    [Obsolete("Use TranslateBitVecExpr instead. ArithExpr uses unbounded integers which don't model overflow correctly.")]
    public ArithExpr? TranslateArithExpr(ExpressionNode node)
    {
        var expr = Translate(node);
        return expr as ArithExpr;
    }

    /// <summary>
    /// Translates a Calor expression to a Z3 bit-vector expression.
    /// Returns null if the expression contains unsupported constructs.
    /// </summary>
    public BitVecExpr? TranslateBitVecExpr(ExpressionNode node)
    {
        var expr = Translate(node);
        return expr as BitVecExpr;
    }

    /// <summary>
    /// Translates a Calor expression to a Z3 expression.
    /// Returns null if the expression contains unsupported constructs.
    /// </summary>
    public Expr? Translate(ExpressionNode node)
    {
        return node switch
        {
            IntLiteralNode intLit => TranslateIntLiteral(intLit),
            BoolLiteralNode boolLit => _ctx.MkBool(boolLit.Value),
            ReferenceNode refNode => TranslateReference(refNode),
            BinaryOperationNode binOp => TranslateBinaryOp(binOp),
            UnaryOperationNode unaryOp => TranslateUnaryOp(unaryOp),
            ConditionalExpressionNode condExpr => TranslateConditional(condExpr),
            ForallExpressionNode forall => TranslateForall(forall),
            ExistsExpressionNode exists => TranslateExists(exists),
            ImplicationExpressionNode impl => TranslateImplication(impl),
            ArrayAccessNode arrayAccess => TranslateArrayAccess(arrayAccess),
            ArrayLengthNode arrayLen => TranslateArrayLength(arrayLen),
            FieldAccessNode fieldAccess => TranslateFieldAccess(fieldAccess),

            // String support using Z3's native string theory
            StringLiteralNode strLit => TrackString(_ctx.MkString(strLit.Value)),
            StringOperationNode strOp => TranslateStringOperation(strOp),

            // Dependent Types: Self-reference in refinement predicates
            SelfRefNode => TranslateSelfRef(),

            // Unsupported constructs - return null
            FloatLiteralNode => null,
            CallExpressionNode => null,
            _ => null
        };
    }

    private Expr? TranslateReference(ReferenceNode node)
    {
        if (_variables.TryGetValue(node.Name, out var variable))
            return variable.Expr;

        // Dot-path references like "item.Quantity" come through the parser as a single
        // ReferenceNode with the dot baked into the name. Resolve them as field access:
        // item.Quantity → field accessor function applied to item's Z3 const.
        if (node.Name.Contains('.'))
        {
            var resolved = ResolveDotPath(node.Name);
            if (resolved is not null) return resolved;
        }

        return null;
    }

    private BitVecExpr TranslateIntLiteral(IntLiteralNode literal)
    {
        if (literal.IsUnsigned)
        {
            var width = literal.IsLong || literal.UnsignedValue > uint.MaxValue ? 64u : 32u;
            return TrackBitVec(_ctx.MkBV(literal.UnsignedValue, width), width, isSigned: false);
        }

        var signedWidth = literal.IsLong || literal.Value is > int.MaxValue or < int.MinValue
            ? 64u
            : 32u;
        return TrackBitVec(_ctx.MkBV(literal.Value, signedWidth), signedWidth, isSigned: true);
    }

    /// <summary>
    /// Resolves a dot-separated reference name (e.g. "item.Quantity" or "a.b.c") by
    /// walking the path: the first segment is looked up in the variable scope, and each
    /// subsequent segment becomes a field-access on the previous result. Each step uses
    /// (or creates) an uninterpreted Z3 function for the field. Receiver and intermediate
    /// field types must be present in the user-type registry; missing entries are refused.
    /// </summary>
    private Expr? ResolveDotPath(string dottedName)
    {
        var parts = dottedName.Split('.');
        if (parts.Length < 2) return null;

        if (!_variables.TryGetValue(parts[0], out var rootVar))
            return null;

        Expr current = rootVar.Expr;
        string currentType = rootVar.Type;

        for (int i = 1; i < parts.Length; i++)
        {
            var fieldName = parts[i];
            var coreType = NormalizeTypeName(currentType);
            if (coreType.EndsWith("?")) coreType = coreType[..^1];
            if (coreType.EndsWith("[]"))
                return null; // array element access via dot-path is not supported here

            var fieldType = ResolveRegisteredFieldType(coreType, fieldName);
            if (fieldType is null)
                return null;

            var key = (coreType, fieldName);
            if (!_fieldAccessors.TryGetValue(key, out var accessor))
            {
                if (!_userTypeSorts.TryGetValue(coreType, out var receiverSort))
                {
                    receiverSort = MarkUninterpretedSort(coreType);
                    _userTypeSorts[coreType] = receiverSort;
                }
                var resultSort = ResultSortForType(fieldType);
                if (resultSort is null)
                {
                    return Refuse(
                        $"field '{coreType}.{fieldName}' has unsupported type '{fieldType}': " +
                        "modeling it as a guessed sort could produce an unsound proof");
                }
                accessor = _ctx.MkFuncDecl($"{coreType}_{fieldName}", new[] { receiverSort }, resultSort);
                _fieldAccessors[key] = accessor;
            }

            current = accessor.Apply(current);
            currentType = fieldType;

            // Track bit-vector width if the field is integer-typed so subsequent
            // numerical operations behave correctly.
            if (current is BitVecExpr bv)
            {
                var (w, signed) = GetTypeWidthAndSignedness(fieldType);
                if (w > 0) TrackBitVec(bv, w, signed);
            }
        }
        return current;
    }

    private string? ResolveRegisteredFieldType(string receiverType, string fieldName)
    {
        if (_userTypeRegistry is null
            || !_userTypeRegistry.TryGetValue(receiverType, out var fieldMap)
            || !fieldMap.TryGetValue(fieldName, out var fieldType)
            || string.IsNullOrWhiteSpace(fieldType))
        {
            _ = Refuse(
                $"field '{receiverType}.{fieldName}' has no registered type: modeling it at a " +
                "guessed width could reason at the wrong wrap boundary (modeled-forms divergence D7)");
            return null;
        }

        return NormalizeTypeName(fieldType);
    }

    /// <summary>
    /// Translates # (self-reference) by looking up the current self-variable from the stack.
    /// </summary>
    private Expr? TranslateSelfRef()
    {
        if (_selfVariableStack.Count == 0)
            return null;

        var selfName = _selfVariableStack.Peek();
        if (_variables.TryGetValue(selfName, out var variable))
            return variable.Expr;

        return null;
    }

    private Expr? TranslateBinaryOp(BinaryOperationNode node)
    {
        var left = Translate(node.Left);
        var right = Translate(node.Right);

        if (left == null || right == null)
            return null;

        // Refuse operand typings that have no executable C# semantics.
        if (left is BitVecExpr lRefuse && right is BitVecExpr rRefuse)
        {
            var refusal = DiagnoseUnmodeledBitVecTyping(node.Operator, lRefuse, rRefuse);
            if (refusal != null)
                return Refuse(refusal);
        }

        return node.Operator switch
        {
            // Arithmetic operations (require BitVecExpr for fixed-width semantics)
            // Add, Sub, Mul are the same for signed/unsigned (two's complement)
            BinaryOperator.Add when left is BitVecExpr la && right is BitVecExpr ra
                => ApplyBitVecBinaryOp(la, ra, _ctx.MkBVAdd),
            BinaryOperator.Subtract when left is BitVecExpr ls && right is BitVecExpr rs
                => ApplyBitVecBinaryOp(ls, rs, _ctx.MkBVSub),
            BinaryOperator.Multiply when left is BitVecExpr lm && right is BitVecExpr rm
                => ApplyBitVecBinaryOp(lm, rm, _ctx.MkBVMul),

            // Division and modulo need signed/unsigned variants
            BinaryOperator.Divide when left is BitVecExpr ld && right is BitVecExpr rd
                => ApplyDivModOp(ld, rd, _ctx.MkBVSDiv, _ctx.MkBVUDiv),
            // C#'s % is REMAINDER (result takes the dividend's sign) = Z3 bvsrem —
            // NOT bvsmod (divisor's sign). Using bvsmod let `a % -3` "prove"
            // result <= 0 while runtime returns +1 (G1 re-verification M-new).
            BinaryOperator.Modulo when left is BitVecExpr lmod && right is BitVecExpr rmod
                => ApplyDivModOp(lmod, rmod, _ctx.MkBVSRem, _ctx.MkBVURem),

            // Comparison operations (return BoolExpr) - need signed/unsigned variants
            BinaryOperator.Equal => MkEqNormalized(left, right),
            BinaryOperator.NotEqual => _ctx.MkNot(MkEqNormalized(left, right)),
            BinaryOperator.LessThan when left is BitVecExpr llt && right is BitVecExpr rlt
                => ApplySignedComparison(llt, rlt, _ctx.MkBVSLT, _ctx.MkBVULT),
            BinaryOperator.LessOrEqual when left is BitVecExpr lle && right is BitVecExpr rle
                => ApplySignedComparison(lle, rle, _ctx.MkBVSLE, _ctx.MkBVULE),
            BinaryOperator.GreaterThan when left is BitVecExpr lgt && right is BitVecExpr rgt
                => ApplySignedComparison(lgt, rgt, _ctx.MkBVSGT, _ctx.MkBVUGT),
            BinaryOperator.GreaterOrEqual when left is BitVecExpr lge && right is BitVecExpr rge
                => ApplySignedComparison(lge, rge, _ctx.MkBVSGE, _ctx.MkBVUGE),

            // Logical operations (require BoolExpr)
            BinaryOperator.And when left is BoolExpr land && right is BoolExpr rand
                => _ctx.MkAnd(land, rand),
            BinaryOperator.Or when left is BoolExpr lor && right is BoolExpr ror
                => _ctx.MkOr(lor, ror),

            // Bitwise operations (require BitVecExpr)
            BinaryOperator.BitwiseAnd when left is BitVecExpr bl && right is BitVecExpr br
                => ApplyBitVecBinaryOp(bl, br, _ctx.MkBVAND),
            BinaryOperator.BitwiseOr when left is BitVecExpr bol && right is BitVecExpr bor
                => ApplyBitVecBinaryOp(bol, bor, _ctx.MkBVOR),
            BinaryOperator.BitwiseXor when left is BitVecExpr bxl && right is BitVecExpr bxr
                => ApplyBitVecBinaryOp(bxl, bxr, _ctx.MkBVXOR),
            // Shifts: dedicated C# semantics — count masked, no binary numeric
            // promotion (review #833 C3)
            BinaryOperator.LeftShift when left is BitVecExpr shl && right is BitVecExpr shr
                => ApplyShiftOp(shl, shr, leftShift: true),
            BinaryOperator.RightShift when left is BitVecExpr ashl && right is BitVecExpr ashr
                => ApplyShiftOp(ashl, ashr, leftShift: false),

            _ => null
        };
    }

    /// <summary>
    /// W1 Slice 1 (T1): names the bit-vector operand typings whose solver
    /// semantics diverge from C# runtime semantics; null = modeled.
    /// Mixed signed/unsigned comparison, equality, and division where the
    /// signed operand is not a provably non-negative literal: C# compares via
    /// promotion to a wider signed type, while a same-width solver comparison
    /// reads the bit pattern (`-1 == 4294967295u` would hold).
    /// </summary>
    private string? DiagnoseUnmodeledBitVecTyping(BinaryOperator op, BitVecExpr left, BitVecExpr right)
    {
        // Shift counts wider than 32 bits have no C# typing (the count operand
        // must convert to int); refuse rather than guess (review #833 C3).
        if (op is BinaryOperator.LeftShift or BinaryOperator.RightShift
            && (right.SortSize > 32 || right.SortSize == 32 && !IsSigned(right)))
        {
            return "the shift count is not implicitly convertible to int";
        }

        // Genuinely-mixed signedness (no literal rescue) below 64 bits is MODELED
        // by promotion to a 64-bit signed comparison — exactly C#'s int-vs-uint →
        // long semantics (PromoteMixedTo64). At 64 bits there is no common C# type
        // (long vs ulong comparison is a compile error): refuse rather than guess.
        var isSignednessSensitive = op is BinaryOperator.Add or BinaryOperator.Subtract
            or BinaryOperator.Multiply or BinaryOperator.BitwiseAnd
            or BinaryOperator.BitwiseOr or BinaryOperator.BitwiseXor
            or BinaryOperator.Equal or BinaryOperator.NotEqual
            or BinaryOperator.LessThan or BinaryOperator.LessOrEqual
            or BinaryOperator.GreaterThan or BinaryOperator.GreaterOrEqual
            or BinaryOperator.Divide or BinaryOperator.Modulo;
        if (isSignednessSensitive
            && EffectiveIsSigned(left, right) != EffectiveIsSigned(right, left))
        {
            // Only a 64-bit UNSIGNED side is unmodelable (long vs ulong has no
            // common C# type). A signed 64-bit side with a sub-64 unsigned side
            // promotes to long and is modeled (verification round N2 — the
            // earlier max-width condition wrongly refused uint-vs-long, which
            // compiles in C#).
            var unsignedWidth = EffectiveIsSigned(left, right) ? right.SortSize : left.SortSize;
            if (unsignedWidth >= 64)
            {
                return "mixed signed/unsigned comparison with a 64-bit unsigned operand has " +
                       "no common C# type (long vs ulong does not compile); the raw bit-pattern " +
                       "comparison the solver would use diverges from any runtime semantics";
            }
        }

        return null;
    }

    /// <summary>
    /// Applies C# binary numeric promotion, including constant-expression
    /// conversion to uint and the pre-promotion treatment of byte/ushort when
    /// paired with uint or ulong.
    /// </summary>
    private (BitVecExpr Left, BitVecExpr Right, bool IsSigned) ApplyBinaryNumericPromotions(
        BitVecExpr left,
        BitVecExpr right)
    {
        var leftSigned = IsSigned(left);
        var rightSigned = IsSigned(right);
        var leftWidth = left.SortSize;
        var rightWidth = right.SortSize;

        uint targetWidth;
        bool targetSigned;

        if ((!leftSigned && leftWidth == 64) || (!rightSigned && rightWidth == 64))
        {
            targetWidth = 64;
            targetSigned = false;
        }
        else if ((leftSigned && leftWidth == 64) || (rightSigned && rightWidth == 64))
        {
            targetWidth = 64;
            targetSigned = true;
        }
        else if ((!leftSigned && leftWidth == 32) || (!rightSigned && rightWidth == 32))
        {
            var signedOperand = leftSigned ? left : rightSigned ? right : null;
            var constantFitsUnsigned = signedOperand is not null
                && IsNonNegativeLiteralWithin(signedOperand, uint.MaxValue);
            targetWidth = signedOperand is not null && !constantFitsUnsigned ? 64u : 32u;
            targetSigned = targetWidth == 64;
        }
        else
        {
            targetWidth = 32;
            targetSigned = true;
        }

        return (
            ConvertIntegral(left, targetWidth, targetSigned),
            ConvertIntegral(right, targetWidth, targetSigned),
            targetSigned);
    }

    private BitVecExpr ConvertIntegral(BitVecExpr operand, uint targetWidth, bool targetSigned)
    {
        if (operand.SortSize == targetWidth)
            return operand;

        var converted = IsSigned(operand)
            ? _ctx.MkSignExt(targetWidth - operand.SortSize, operand)
            : _ctx.MkZeroExt(targetWidth - operand.SortSize, operand);
        return TrackBitVec(converted, targetWidth, targetSigned);
    }

    private Expr? TranslateUnaryOp(UnaryOperationNode node)
    {
        var operand = Translate(node.Operand);
        if (operand == null)
            return null;

        return node.Operator switch
        {
            UnaryOperator.Not when operand is BoolExpr boolOp => _ctx.MkNot(boolOp),
            UnaryOperator.Negate when operand is BitVecExpr bvOp =>
                TranslateUnaryNegation(bvOp),
            _ => null
        };
    }

    private Expr? TranslateUnaryNegation(BitVecExpr operand)
    {
        if (!IsSigned(operand) && operand.SortSize == 64)
        {
            return Refuse(
                "unary negation of a 64-bit unsigned operand has no executable C# semantics");
        }

        BitVecExpr promoted;
        if (!IsSigned(operand) && operand.SortSize == 32)
        {
            promoted = TrackBitVec(_ctx.MkZeroExt(32, operand), 64, isSigned: true);
        }
        else
        {
            promoted = PromoteNarrowIntegral(operand);
        }

        return TrackBitVec(_ctx.MkBVNeg(promoted), promoted.SortSize, isSigned: true);
    }

    private Expr? TranslateConditional(ConditionalExpressionNode node)
    {
        var condition = Translate(node.Condition) as BoolExpr;
        var whenTrue = Translate(node.WhenTrue);
        var whenFalse = Translate(node.WhenFalse);

        if (condition == null || whenTrue == null || whenFalse == null)
            return null;

        return _ctx.MkITE(condition, whenTrue, whenFalse);
    }

    /// <summary>
    /// Pushes the current variable scope onto the stack.
    /// </summary>
    private void PushScope()
    {
        _scopeStack.Push(new Dictionary<string, (Expr, string)>(_variables));
    }

    /// <summary>
    /// Pops and restores the previous variable scope.
    /// </summary>
    private void PopScope()
    {
        var prev = _scopeStack.Pop();
        _variables.Clear();
        foreach (var kvp in prev)
            _variables[kvp.Key] = kvp.Value;
    }

    /// <summary>
    /// Translates a universal quantifier (forall) expression.
    /// </summary>
    private BoolExpr? TranslateForall(ForallExpressionNode node)
    {
        PushScope();
        try
        {
            var boundVars = new List<Expr>();
            foreach (var bv in node.BoundVariables)
            {
                var z3Var = CreateVariableForType(bv.Name, bv.TypeName);
                if (z3Var == null)
                    return null;
                _variables[bv.Name] = (z3Var, bv.TypeName);
                boundVars.Add(z3Var);
            }

            var body = TranslateBoolExpr(node.Body);
            if (body == null)
                return null;

            return _ctx.MkForall(boundVars.ToArray(), body);
        }
        finally
        {
            PopScope();
        }
    }

    /// <summary>
    /// Translates an existential quantifier (exists) expression.
    /// </summary>
    private BoolExpr? TranslateExists(ExistsExpressionNode node)
    {
        PushScope();
        try
        {
            var boundVars = new List<Expr>();
            foreach (var bv in node.BoundVariables)
            {
                var z3Var = CreateVariableForType(bv.Name, bv.TypeName);
                if (z3Var == null)
                    return null;
                _variables[bv.Name] = (z3Var, bv.TypeName);
                boundVars.Add(z3Var);
            }

            var body = TranslateBoolExpr(node.Body);
            if (body == null)
                return null;

            return _ctx.MkExists(boundVars.ToArray(), body);
        }
        finally
        {
            PopScope();
        }
    }

    /// <summary>
    /// Translates a logical implication expression.
    /// p -> q is equivalent to !p || q
    /// </summary>
    private BoolExpr? TranslateImplication(ImplicationExpressionNode node)
    {
        var ante = TranslateBoolExpr(node.Antecedent);
        var cons = TranslateBoolExpr(node.Consequent);

        if (ante == null || cons == null)
            return null;

        return _ctx.MkImplies(ante, cons);
    }

    /// <summary>
    /// Translates an array access expression.
    /// For Z3, we model arrays as uninterpreted functions with 64-bit indices.
    /// </summary>
    private Expr? TranslateArrayAccess(ArrayAccessNode node)
    {
        // For array access like arr{i}, we need to model it as an array select
        // First, get or create an array variable for the base array
        if (node.Array is ReferenceNode arrayRef)
        {
            var index = Translate(node.Index);
            if (index == null || index is not BitVecExpr indexBv)
                return null;

            // Check if we already have an array variable
            var arrayName = arrayRef.Name;
            if (!_variables.TryGetValue(arrayName, out var arrayVar))
            {
                // D6, adjudicated at W1 Slice 1 (recorded why-not): the on-demand
                // i32-element default is UNREACHABLE from the §Q/§S proof path —
                // contract parameters and quantifier bound variables are declared
                // with their true types before translation, and a contract that
                // references an undeclared name is rejected upstream by
                // ContractVerifier's reference validation. This path serves the
                // obligation/refinement surface (never elides), which has always
                // used these semantics. Also creates the associated $length.
                var arrayExpr = CreateArrayVariable(arrayName, "i32");
                if (arrayExpr == null)
                    return null;
                _variables[arrayName] = (arrayExpr, "array<i32>");
                arrayVar = (arrayExpr, "array<i32>");
            }
            if (arrayVar.Expr is ArrayExpr arrExpr)
            {
                // Extend index to 64-bit for array access (sign or zero extend based on signedness)
                BitVecExpr normalizedIndex;
                if (indexBv.SortSize == 64)
                {
                    normalizedIndex = indexBv;
                }
                else if (IsSigned(indexBv))
                {
                    normalizedIndex = _ctx.MkSignExt(64 - indexBv.SortSize, indexBv);
                }
                else
                {
                    normalizedIndex = _ctx.MkZeroExt(64 - indexBv.SortSize, indexBv);
                }
                var selected = (BitVecExpr)_ctx.MkSelect(arrExpr, normalizedIndex);
                if (_arrayInfo.TryGetValue(arrayName, out var info))
                {
                    var (width, isSigned) = GetTypeWidthAndSignedness(info.ElementType);
                    if (width > 0)
                        TrackBitVec(selected, width, isSigned);
                }
                return selected;
            }
        }

        return null;
    }

    private Expr? CreateVariableForType(string name, string typeName)
    {
        // Normalize type names
        var normalizedType = NormalizeTypeName(typeName);

        // Check for array types (e.g., "i32[]", "int[]", "u8[]")
        if (normalizedType.EndsWith("[]"))
        {
            var elementType = normalizedType[..^2]; // Remove "[]" suffix
            return CreateArrayVariable(name, elementType);
        }

        return normalizedType switch
        {
            // Signed integer types
            "i8" or "sbyte" => TrackBitVec(_ctx.MkBVConst(name, 8), 8, isSigned: true),
            "i16" or "short" => TrackBitVec(_ctx.MkBVConst(name, 16), 16, isSigned: true),
            "i32" or "int" => TrackBitVec(_ctx.MkBVConst(name, 32), 32, isSigned: true),
            "i64" or "long" => TrackBitVec(_ctx.MkBVConst(name, 64), 64, isSigned: true),

            // Unsigned integer types
            "u8" or "byte" => TrackBitVec(_ctx.MkBVConst(name, 8), 8, isSigned: false),
            "u16" or "ushort" => TrackBitVec(_ctx.MkBVConst(name, 16), 16, isSigned: false),
            "u32" or "uint" => TrackBitVec(_ctx.MkBVConst(name, 32), 32, isSigned: false),
            "u64" or "ulong" => TrackBitVec(_ctx.MkBVConst(name, 64), 64, isSigned: false),

            "bool" => _ctx.MkBoolConst(name),
            // String type - uses Z3's native string theory
            "string" or "str" => TrackString((SeqExpr)_ctx.MkConst(name, _ctx.StringSort)),
            // Unsupported types
            "f32" or "f64" or "float" or "double" => null,
            _ => CreateUserDefinedTypeVariable(name, normalizedType, typeName)
        };
    }

    /// <summary>
    /// Creates a Z3 constant of an uninterpreted sort for a user-defined type (class).
    /// One sort per type name, shared across variables of that type. Strips a trailing
    /// '?' nullable marker so `Order` and `Order?` map to the same sort. Field accesses
    /// are then translated as uninterpreted functions on that sort — see
    /// <see cref="TranslateFieldAccess"/>.
    /// </summary>
    private Expr CreateUserDefinedTypeVariable(string name, string normalizedType, string originalType)
    {
        // Strip nullable marker for sort lookup; nullability tracked separately if needed.
        var coreType = normalizedType.EndsWith("?") ? normalizedType[..^1] : normalizedType;
        if (string.IsNullOrEmpty(coreType))
            return null!;

        if (!_userTypeSorts.TryGetValue(coreType, out var sort))
        {
            sort = MarkUninterpretedSort(coreType);
            _userTypeSorts[coreType] = sort;
        }
        return _ctx.MkConst(name, sort);
    }

    /// <summary>
    /// Creates an array variable expression. Called internally from CreateVariableForType.
    /// </summary>
    private Expr? CreateArrayVariable(string name, string elementType)
    {
        var (elementWidth, _) = GetTypeWidthAndSignedness(elementType);
        if (elementWidth == 0)
            return null;

        // Create array sort: BitVec64 (index) -> BitVec[elementWidth] (element)
        var bv64Sort = _ctx.MkBitVecSort(64);
        var elementSort = _ctx.MkBitVecSort(elementWidth);
        TouchedNullableReferenceSort = true;
        var arrayExpr = _ctx.MkArrayConst(name, bv64Sort, elementSort);

        // Create associated length variable (unsigned 32-bit)
        var lengthVarName = $"{name}$length";
        var lengthExpr = MarkArrayLength(lengthVarName);
        _variables[lengthVarName] = (lengthExpr, "u32");

        _arrayInfo[name] = new ArrayInfo(elementType, lengthExpr);
        return arrayExpr;
    }

    private static string NormalizeTypeName(string typeName)
    {
        return typeName.ToLowerInvariant() switch
        {
            // Signed types
            "int8" or "system.sbyte" => "i8",
            "int16" or "system.int16" => "i16",
            "int32" or "system.int32" => "i32",
            "int64" or "system.int64" => "i64",

            // Unsigned types
            "uint8" or "system.byte" => "u8",
            "uint16" or "system.uint16" => "u16",
            "uint32" or "system.uint32" => "u32",
            "uint64" or "system.uint64" => "u64",

            "boolean" or "system.boolean" => "bool",
            "single" or "system.single" => "f32",
            "double" or "system.double" => "f64",
            var t => t
        };
    }

    /// <summary>
    /// Tracks the bit-width and signedness of a bit-vector expression.
    /// </summary>
    private BitVecExpr TrackBitVec(BitVecExpr expr, uint width, bool isSigned)
    {
        _exprInfo[expr] = new BitVecInfo(width, isSigned);
        return expr;
    }

    /// <summary>
    /// Tracks a string expression with metadata.
    /// </summary>
    /// <summary>
    /// True once ANY term of Z3's string sort has been minted by this translator — D3/D12's
    /// demotion trigger (see <see cref="Z3Verifier.StringModelAssumption"/>).
    ///
    /// <para>This lives here, rather than being re-derived by walking the contract AST, because
    /// the AST is the wrong thing to look at. The same translator instance also encodes the
    /// function BODY into the solver (<c>FunctionBodyEncoder.TryEncodeResult</c> asserts
    /// <c>result == encode(body)</c>), so a proof can be carried entirely by string terms that
    /// appear nowhere in the contract: <c>§S (== result INT:2)</c> over <c>§R (len STR:"é")</c>
    /// has no string parameter, a non-string return, and no string node in the contract — and it
    /// was <c>Proven</c> and elided. Setting the flag where the sort is CREATED cannot miss a
    /// form, because every string term in the query passes through here or through
    /// <see cref="TranslateStringOperation"/>.</para>
    /// </summary>
    public bool TouchedStringTheory { get; private set; }

    /// <summary>
    /// True once a term has been minted whose Z3 sort is TOTAL where the corresponding .NET value
    /// is a nullable REFERENCE — arrays and user-type (uninterpreted) sorts.
    ///
    /// <para>This is D3's defect one sort over, and it was found the fifth time this bar was
    /// audited. `DeclareArrayVariable` mints `&lt;name&gt;$length` as an unconstrained u32, so
    /// <c>a.Length &gt;= 0</c> is a solver tautology — while at runtime a null array makes that
    /// same expression throw. Reproduced end-to-end in pure Calor: `§S (&gt;= §LEN a INT:0)` over a
    /// never-assigned `[i32]` field was `Proven`, and `calor run` crashed with a
    /// NullReferenceException while `calor run --verify` printed and exited 0.</para>
    ///
    /// <para>Kept separate from <see cref="TouchedStringTheory"/> only so the assumption text can
    /// name the right sort; both drive the same demotion. The lesson recorded with it: the class
    /// is not "strings", it is "a sort Z3 models as total where .NET's value can be null", and
    /// enumerating its members by hand is what has failed repeatedly.</para>
    /// </summary>
    public bool TouchedNullableReferenceSort { get; private set; }

    /// <summary>
    /// Uninterpreted sorts stand in for user types, which are nullable reference types in C# —
    /// same total-vs-nullable mismatch as arrays, so the same demotion applies.
    /// </summary>
    private Sort MarkUninterpretedSort(string name)
    {
        TouchedNullableReferenceSort = true;
        return _ctx.MkUninterpretedSort(name);
    }

    /// <summary>Records the string-sort touch for sites that hand back a bare <c>Sort</c>.</summary>
    private Sort MarkStringSort()
    {
        TouchedStringTheory = true;
        return _ctx.StringSort;
    }

    /// <summary>
    /// Mints an array's synthetic <c>$length</c> companion, flagging the reference-model
    /// assumption (D14) at the point the tautology is actually created.
    ///
    /// <para>An earlier revision put the flag next to <c>MkArrayConst</c> instead. That covered
    /// two of the three <c>$length</c> sites <b>incidentally</b> — because they happen to sit
    /// beside an array construction — and missed the third, where <c>TranslateArrayLength</c>
    /// mints the length ON DEMAND with no array const in sight. `§PROOF (>= §LEN a INT:0)` over a
    /// local `§ARR` therefore still discharged. The whole argument of this fix is that
    /// enumerating members of a class by hand is what keeps failing; the first attempt then
    /// enumerated by hand and missed one. Routing every mint through here is the invariant:
    /// <b>no <c>$length</c> exists that did not set the flag.</b></para>
    /// </summary>
    private BitVecExpr MarkArrayLength(string lengthVarName)
    {
        TouchedNullableReferenceSort = true;
        return TrackBitVec(_ctx.MkBVConst(lengthVarName, 32), 32, isSigned: false);
    }

    private SeqExpr TrackString(SeqExpr expr, bool isNullable = false)
    {
        TouchedStringTheory = true;
        _stringInfo[expr] = new StringInfo(isNullable);
        return expr;
    }

    /// <summary>
    /// Gets the info for a bit-vector expression.
    /// Defaults to signed 32-bit if not tracked (e.g., for integer literals).
    /// </summary>
    private BitVecInfo GetBitVecInfo(Expr expr) => expr switch
    {
        BitVecExpr bv when _exprInfo.TryGetValue(bv, out var info) => info,
        BitVecExpr bv => new BitVecInfo(bv.SortSize, IsSigned: true), // Default to signed
        _ => new BitVecInfo(32u, IsSigned: true)
    };

    /// <summary>
    /// Determines if an expression is signed.
    /// </summary>
    private bool IsSigned(Expr expr) => GetBitVecInfo(expr).IsSigned;

    /// <summary>
    /// Checks if an expression is a non-negative literal value.
    /// </summary>
    private bool IsNonNegativeLiteral(Expr expr)
    {
        if (expr is BitVecNum num)
        {
            // For signed interpretation, check if the high bit is 0
            // A non-negative signed value has its MSB = 0
            var width = num.SortSize;
            var value = num.BigInteger;
            var maxPositive = System.Numerics.BigInteger.Pow(2, (int)width - 1) - 1;
            return value >= 0 && value <= maxPositive;
        }
        return false;
    }

    private bool IsNonNegativeLiteralWithin(Expr expr, ulong maximum) =>
        IsNonNegativeLiteral(expr)
        && expr is BitVecNum number
        && number.BigInteger <= maximum;

    /// <summary>
    /// Normalizes two bit-vector expressions to the same width.
    /// Uses sign extension for signed types, zero extension for unsigned.
    /// </summary>
    private (BitVecExpr Left, BitVecExpr Right) NormalizeBitVecWidths(BitVecExpr left, BitVecExpr right)
    {
        var leftWidth = left.SortSize;
        var rightWidth = right.SortSize;

        if (leftWidth == rightWidth)
            return (left, right);

        var leftSigned = IsSigned(left);
        var rightSigned = IsSigned(right);

        if (leftWidth < rightWidth)
        {
            var extended = leftSigned
                ? _ctx.MkSignExt(rightWidth - leftWidth, left)
                : _ctx.MkZeroExt(rightWidth - leftWidth, left);
            return (extended, right);
        }

        else
        {
            var extended = rightSigned
                ? _ctx.MkSignExt(leftWidth - rightWidth, right)
                : _ctx.MkZeroExt(leftWidth - rightWidth, right);
            return (left, extended);
        }
    }

    private BitVecExpr PromoteNarrowIntegral(BitVecExpr operand)
    {
        if (operand.SortSize >= 32)
            return operand;

        var promoted = IsSigned(operand)
            ? _ctx.MkSignExt(32 - operand.SortSize, operand)
            : _ctx.MkZeroExt(32 - operand.SortSize, operand);
        return TrackBitVec(promoted, 32, isSigned: true);
    }

    /// <summary>
    /// Applies a binary bit-vector operation with width normalization.
    /// </summary>
    /// <summary>
    /// C# shift semantics (review #833 C3): shifts do NOT follow binary numeric
    /// promotion — the LEFT operand promotes individually (narrow → int, uint
    /// stays uint, i64/u64 stay), the count is an int, and the runtime MASKS the
    /// count by (width − 1): `1 &lt;&lt; 32` is 1 at runtime, while an unmasked
    /// solver shift yields 0 — a false-Proven vector. The mask is modeled
    /// explicitly. Right shifts pick arithmetic vs logical by the promoted left
    /// operand's signedness (narrow operands promote to int = signed).
    /// </summary>
    private BitVecExpr ApplyShiftOp(BitVecExpr left, BitVecExpr right, bool leftShift)
    {
        // Promote the left operand individually; the count is converted to int
        // and then extended to the promoted left width for the Z3 shift.
        var promotedLeft = PromoteNarrowIntegral(left);
        var promotedCount = PromoteNarrowIntegral(right);
        var (normalizedLeft, normalizedCount) = NormalizeBitVecWidths(promotedLeft, promotedCount);
        var w = normalizedLeft.SortSize;
        var maskedCount = _ctx.MkBVAND(normalizedCount, _ctx.MkBV(w - 1, w));

        // Promoted-left signedness: sub-32-bit operands promote to int (signed).
        var resultSigned = left.SortSize < 32 || IsSigned(left);

        var result = leftShift
            ? _ctx.MkBVSHL(normalizedLeft, maskedCount)
            : resultSigned
                ? _ctx.MkBVASHR(normalizedLeft, maskedCount)
                : _ctx.MkBVLSHR(normalizedLeft, maskedCount);
        return TrackBitVec(result, w, resultSigned);
    }

    private BitVecExpr ApplyBitVecBinaryOp(BitVecExpr left, BitVecExpr right, Func<BitVecExpr, BitVecExpr, BitVecExpr> op)
    {
        var promoted = ApplyBinaryNumericPromotions(left, right);
        return TrackBitVec(
            op(promoted.Left, promoted.Right),
            promoted.Left.SortSize,
            promoted.IsSigned);
    }

    /// <summary>
    /// The C#-typing signedness of <paramref name="operand"/> in a binary
    /// operation with <paramref name="other"/>: a signed non-negative literal
    /// paired with a **32-bit-or-wider** unsigned operand converts implicitly to
    /// the unsigned type (`u32 x - 1` is uint). Review #833 C2: the rescue must
    /// NOT apply to narrow unsigned operands — C# has no byte/ushort arithmetic,
    /// `byte - const` is plain int (signed), so `u8 x - 5` can be negative.
    /// </summary>
    private bool EffectiveIsSigned(Expr operand, Expr other)
        => IsSigned(operand)
           && !(IsNonNegativeLiteral(operand)
                && !IsSigned(other)
                && other is BitVecExpr otherBv
                && otherBv.SortSize >= 32);

    /// <summary>
    /// Applies a signed or unsigned comparison operation with width normalization.
    /// </summary>
    private BoolExpr ApplySignedComparison(BitVecExpr left, BitVecExpr right,
        Func<BitVecExpr, BitVecExpr, BoolExpr> signedOp,
        Func<BitVecExpr, BitVecExpr, BoolExpr> unsignedOp)
    {
        var promoted = ApplyBinaryNumericPromotions(left, right);
        var op = promoted.IsSigned ? signedOp : unsignedOp;
        return op(promoted.Left, promoted.Right);
    }

    /// <summary>
    /// Applies a division or modulo operation, choosing signed or unsigned variant.
    /// </summary>
    /// <summary>
    /// Translates a division/modulo's operands to the width and signedness the
    /// operation itself will use (mirrors <see cref="ApplyDivModOp"/> — mixed
    /// operands promote, same-signedness operands normalize). Used by the
    /// divisor-side-condition collector (review #833 C4) so the
    /// MinValue-÷-(−1) overflow condition is expressed at the operation's own
    /// width. Null when either operand is outside the bit-vector surface.
    /// </summary>
    public (BitVecExpr Left, BitVecExpr Right, bool Signed)? GetDivModOperands(ExpressionNode leftNode, ExpressionNode rightNode)
    {
        if (Translate(leftNode) is not BitVecExpr l || Translate(rightNode) is not BitVecExpr r)
            return null;

        var promoted = ApplyBinaryNumericPromotions(l, r);
        return (promoted.Left, promoted.Right, promoted.IsSigned);
    }

    private BitVecExpr ApplyDivModOp(BitVecExpr left, BitVecExpr right,
        Func<BitVecExpr, BitVecExpr, BitVecExpr> signedOp,
        Func<BitVecExpr, BitVecExpr, BitVecExpr> unsignedOp)
    {
        var promoted = ApplyBinaryNumericPromotions(left, right);
        var op = promoted.IsSigned ? signedOp : unsignedOp;
        return TrackBitVec(
            op(promoted.Left, promoted.Right),
            promoted.Left.SortSize,
            promoted.IsSigned);
    }

    /// <summary>
    /// Creates an equality expression, normalizing bit-vector widths if needed.
    /// </summary>
    private BoolExpr MkEqNormalized(Expr left, Expr right)
    {
        if (left is BitVecExpr bvLeft && right is BitVecExpr bvRight)
        {
            var promoted = ApplyBinaryNumericPromotions(bvLeft, bvRight);
            return _ctx.MkEq(promoted.Left, promoted.Right);
        }
        return _ctx.MkEq(left, right);
    }

    // ===========================================
    // String Theory Support
    // ===========================================

    /// <summary>
    /// Translates a string operation to a Z3 expression.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Supported operations: Length, Contains, StartsWith, EndsWith, Equals, IsNullOrEmpty,
    /// IndexOf, Substring, SubstringFrom, Concat. (Replace refused — first-occurrence vs
    /// all-occurrences divergence, W1 Slice 1.)
    /// </para>
    /// <para>
    /// <b>Note:</b> The <see cref="StringOperationNode.ComparisonMode"/> property is ignored.
    /// Z3's string theory only supports ordinal comparison; case-insensitive comparisons
    /// (e.g., <c>:ignore-case</c>) cannot be modeled and will verify as if ordinal comparison
    /// was specified.
    /// </para>
    /// </remarks>
    private Expr? TranslateStringOperation(StringOperationNode node)
    {
        // Operations whose RESULT is not a string (Length, IndexOf, Contains…) never reach
        // TrackString, but they are still carried by the string theory's axioms.
        TouchedStringTheory = true;

        // D4, closed by refusal (mirrors D9/string.Replace). A non-ordinal comparison mode used to
        // add a warning and then translate as ORDINAL anyway — the solver proved under one semantics
        // while the emitter emitted the runtime call under another. Because the mode-bearing form was
        // whitelisted in ModeledForms and Z3Verifier never demoted on warnings, that produced a
        // genuine false `Proven`, and `Proven && !IsVacuous` ELIDES the runtime check. Reproduced
        // end-to-end: `§S (! (Equals result STR:"ABC" :ignore-case))` over `§R s` with `s == "abc"`
        // threw ContractViolationException without --verify and printed `abc` with it.
        // A warning cannot carry this: the whole point of the elision is that nobody reads it.
        if (node.ComparisonMode.HasValue && node.ComparisonMode.Value != StringComparisonMode.Ordinal)
        {
            return Refuse(
                $"string comparison mode '{node.ComparisonMode.Value}' is not modeled: the solver has " +
                "only ordinal string semantics, so proving through a case-insensitive or culture-aware " +
                "comparison could elide a runtime check the program needs.");
        }

        // D4, second half. The three operations below are culture-sensitive in .NET when no mode is
        // given: `String.StartsWith(String)`, `EndsWith(String)` and `IndexOf(String)` use
        // CurrentCulture, while `Contains(String)` and `Equals(String)` are ordinal. The solver
        // models all of them with ordinal Z3 primitives, so OMITTING the mode is not the safe
        // default it looks like — it is the same false-`Proven`-elides vector, on the far more
        // common spelling. Verified on .NET 10/ICU: "abc".StartsWith("\u200dabc") is TRUE under the
        // culture overload and FALSE ordinally; likewise EndsWith, and IndexOf returns 0 vs -1.
        // Reproduced end-to-end before this fix: `§S (! (starts result STR:"\u200dabc"))` over
        // `§R s` with `s == "abc"` threw ContractViolationException under `calor run` and printed
        // `abc` under `calor run --verify`.
        //
        // Refused rather than re-emitted as ordinal: changing which .NET overload the emitter picks
        // would silently change the runtime behaviour of existing programs, which is a semantics
        // decision needing its own adjudication. Refusal only stops proving.
        if (node.Operation is StringOp.StartsWith or StringOp.EndsWith or StringOp.IndexOf
            && node.ComparisonMode != StringComparisonMode.Ordinal)
        {
            return Refuse(
                $"'{node.Operation}' without an explicit ':ordinal' is not modeled: .NET resolves it " +
                "to the CURRENT-CULTURE overload while the solver models it ordinally, so a proof " +
                "could elide a runtime check the program needs. State ':ordinal' to make it provable.");
        }

        return node.Operation switch
        {
            StringOp.Length => TranslateStringLength(node),
            StringOp.Contains => TranslateStringContains(node),
            StringOp.StartsWith => TranslateStringStartsWith(node),
            StringOp.EndsWith => TranslateStringEndsWith(node),
            StringOp.Equals => TranslateStringEquals(node),
            StringOp.IsNullOrEmpty => TranslateStringIsNullOrEmpty(node),
            StringOp.IndexOf => TranslateStringIndexOf(node),
            StringOp.Substring => TranslateStringSubstring(node),
            StringOp.SubstringFrom => TranslateStringSubstringFrom(node),
            StringOp.Concat => TranslateStringConcat(node),
            // Replace refused: Z3's MkReplace models FIRST-occurrence replacement while
            // .NET's string.Replace substitutes ALL occurrences — proving through that
            // divergence could elide a runtime guard the program needs (W1 Slice 1, T1).
            StringOp.Replace => Refuse(
                "string.Replace is not modeled: the solver's replace substitutes the first " +
                "occurrence while .NET substitutes all occurrences"),
            // Unsupported operations - ToUpper, ToLower, Trim, Regex, etc.
            _ => null
        };
    }

    /// <summary>
    /// Translates string length operation: (len s) -> BitVecExpr (32-bit unsigned)
    /// </summary>
    private Expr? TranslateStringLength(StringOperationNode node)
    {
        if (node.Arguments.Count < 1)
            return null;

        var str = Translate(node.Arguments[0]);
        if (str is not SeqExpr seqExpr)
            return null;

        // MkLength returns IntExpr, convert to 32-bit unsigned bit-vector
        var lengthInt = _ctx.MkLength(seqExpr);
        return TrackBitVec(_ctx.MkInt2BV(32, lengthInt), 32, isSigned: false);
    }

    /// <summary>
    /// Translates string contains operation: (contains s "hello") -> BoolExpr
    /// </summary>
    private Expr? TranslateStringContains(StringOperationNode node)
    {
        if (node.Arguments.Count < 2)
            return null;

        var str = Translate(node.Arguments[0]);
        var substr = Translate(node.Arguments[1]);

        if (str is not SeqExpr strExpr || substr is not SeqExpr substrExpr)
            return null;

        return _ctx.MkContains(strExpr, substrExpr);
    }

    /// <summary>
    /// Translates string starts-with operation: (starts s "prefix") -> BoolExpr
    /// Note: Z3's MkPrefixOf takes (prefix, str) - prefix first!
    /// </summary>
    private Expr? TranslateStringStartsWith(StringOperationNode node)
    {
        if (node.Arguments.Count < 2)
            return null;

        var str = Translate(node.Arguments[0]);
        var prefix = Translate(node.Arguments[1]);

        if (str is not SeqExpr strExpr || prefix is not SeqExpr prefixExpr)
            return null;

        // MkPrefixOf takes prefix first, then string
        return _ctx.MkPrefixOf(prefixExpr, strExpr);
    }

    /// <summary>
    /// Translates string ends-with operation: (ends s "suffix") -> BoolExpr
    /// Note: Z3's MkSuffixOf takes (suffix, str) - suffix first!
    /// </summary>
    private Expr? TranslateStringEndsWith(StringOperationNode node)
    {
        if (node.Arguments.Count < 2)
            return null;

        var str = Translate(node.Arguments[0]);
        var suffix = Translate(node.Arguments[1]);

        if (str is not SeqExpr strExpr || suffix is not SeqExpr suffixExpr)
            return null;

        // MkSuffixOf takes suffix first, then string
        return _ctx.MkSuffixOf(suffixExpr, strExpr);
    }

    /// <summary>
    /// Translates string equals operation: (equals s1 s2) -> BoolExpr
    /// </summary>
    private Expr? TranslateStringEquals(StringOperationNode node)
    {
        if (node.Arguments.Count < 2)
            return null;

        var str1 = Translate(node.Arguments[0]);
        var str2 = Translate(node.Arguments[1]);

        if (str1 is not SeqExpr str1Expr || str2 is not SeqExpr str2Expr)
            return null;

        return _ctx.MkEq(str1Expr, str2Expr);
    }

    /// <summary>
    /// Translates string is-null-or-empty operation: (isempty s) -> BoolExpr.
    /// </summary>
    /// <remarks>
    /// <b>Important:</b> Z3 strings cannot be null - they are always valid sequences.
    /// This method only checks if the string length equals zero. Code that passes null
    /// strings to <c>string.IsNullOrEmpty()</c> in C# will behave differently than this
    /// Z3 translation, which cannot distinguish null from empty.
    /// </remarks>
    private Expr? TranslateStringIsNullOrEmpty(StringOperationNode node)
    {
        if (node.Arguments.Count < 1)
            return null;

        var str = Translate(node.Arguments[0]);
        if (str is not SeqExpr seqExpr)
            return null;

        // Check if length equals 0
        var length = _ctx.MkLength(seqExpr);
        return _ctx.MkEq(length, _ctx.MkInt(0));
    }

    /// <summary>
    /// Translates string index-of operation: (indexof s "search") or (indexof s "search" start) -> BitVecExpr (32-bit signed).
    /// Returns -1 if not found.
    /// </summary>
    /// <remarks>
    /// Supports both 2-argument form (searches from index 0) and 3-argument form (searches from specified start index).
    /// </remarks>
    private Expr? TranslateStringIndexOf(StringOperationNode node)
    {
        if (node.Arguments.Count < 2)
            return null;

        var str = Translate(node.Arguments[0]);
        var search = Translate(node.Arguments[1]);

        if (str is not SeqExpr strExpr || search is not SeqExpr searchExpr)
            return null;

        // Determine start index: use 3rd argument if provided, otherwise 0
        IntExpr startIndex;
        if (node.Arguments.Count >= 3)
        {
            var startArg = Translate(node.Arguments[2]);
            var startInt = ConvertToIntExpr(startArg);
            if (startInt == null)
                return null;
            startIndex = startInt;
        }
        else
        {
            startIndex = _ctx.MkInt(0);
        }

        // MkIndexOf takes (str, search, startIndex)
        var indexInt = _ctx.MkIndexOf(strExpr, searchExpr, startIndex);
        return TrackBitVec(_ctx.MkInt2BV(32, indexInt), 32, isSigned: true);
    }

    /// <summary>
    /// Translates string substring operation: (substr s start len) -> SeqExpr
    /// </summary>
    private Expr? TranslateStringSubstring(StringOperationNode node)
    {
        if (node.Arguments.Count < 3)
            return null;

        var str = Translate(node.Arguments[0]);
        var start = Translate(node.Arguments[1]);
        var len = Translate(node.Arguments[2]);

        if (str is not SeqExpr strExpr)
            return null;

        // Convert BitVec indices to Int
        var startInt = ConvertToIntExpr(start);
        var lenInt = ConvertToIntExpr(len);

        if (startInt == null || lenInt == null)
            return null;

        return TrackString(_ctx.MkExtract(strExpr, startInt, lenInt));
    }

    /// <summary>
    /// Translates string substring-from operation: (substr s start) -> SeqExpr
    /// Gets substring from start to end of string
    /// </summary>
    private Expr? TranslateStringSubstringFrom(StringOperationNode node)
    {
        if (node.Arguments.Count < 2)
            return null;

        var str = Translate(node.Arguments[0]);
        var start = Translate(node.Arguments[1]);

        if (str is not SeqExpr strExpr)
            return null;

        var startInt = ConvertToIntExpr(start);
        if (startInt == null)
            return null;

        // Length from start to end = total length - start
        var totalLen = _ctx.MkLength(strExpr);
        var remainingLen = _ctx.MkSub(totalLen, startInt) as IntExpr;
        if (remainingLen == null)
            return null;

        return TrackString(_ctx.MkExtract(strExpr, startInt, remainingLen));
    }

    /// <summary>
    /// Translates string concat operation: (concat s1 s2 ...) -> SeqExpr
    /// </summary>
    private Expr? TranslateStringConcat(StringOperationNode node)
    {
        if (node.Arguments.Count < 2)
            return null;

        var strings = new List<SeqExpr>();
        foreach (var arg in node.Arguments)
        {
            var translated = Translate(arg);
            if (translated is not SeqExpr seqExpr)
                return null;
            strings.Add(seqExpr);
        }

        return TrackString(_ctx.MkConcat(strings.ToArray()));
    }

    /// <summary>
    /// Sets <see cref="LastRefusalReason"/> and returns null — the translator's
    /// deliberate-refusal channel, distinct from "no case matched". The verifier
    /// surfaces the reason in the Unsupported evidence (W1 Slice 1).
    /// </summary>
    private Expr? Refuse(string reason)
    {
        LastRefusalReason = reason;
        return null;
    }

    /// <summary>
    /// The reason for the most recent deliberate translation refusal (semantic
    /// divergences the syntactic whitelist cannot express: narrow-int arithmetic,
    /// mixed-signedness comparison, unknown field/array widths, string.Replace).
    /// Null when the last failure was not a deliberate refusal. Reset at each
    /// <see cref="TranslateBoolExpr"/> entry.
    /// </summary>
    public string? LastRefusalReason { get; private set; }

    /// <summary>
    /// Converts a bit-vector expression to an IntExpr for Z3 string operations.
    /// </summary>
    private IntExpr? ConvertToIntExpr(Expr? expr)
    {
        if (expr is IntExpr intExpr)
            return intExpr;

        if (expr is BitVecExpr bvExpr)
        {
            // Use signed conversion for signed types, unsigned for unsigned
            return _ctx.MkBV2Int(bvExpr, IsSigned(bvExpr));
        }

        return null;
    }

    // ===========================================
    // Array Theory Enhancement
    // ===========================================

    /// <summary>
    /// Translates a field-access expression on a user-defined-type value: obj.Field.
    /// Models the field as an uninterpreted Z3 function on the object's sort. The
    /// function's result sort comes from the required user-type registry. One function
    /// per (type, field) pair is shared across all expressions referencing the same field.
    /// </summary>
    private Expr? TranslateFieldAccess(FieldAccessNode node)
    {
        // Translate the receiver to a Z3 expression.
        var target = Translate(node.Target);
        if (target is null)
            return null;

        // Determine the receiver's Calor type so we can index our field-accessor table.
        // For now we look this up via the variable registry when the target is a plain reference.
        string? receiverType = node.Target switch
        {
            ReferenceNode r when _variables.TryGetValue(r.Name, out var v) => v.Type,
            _ => null,
        };
        if (receiverType is null)
            return null;

        var coreType = NormalizeTypeName(receiverType);
        if (coreType.EndsWith("?")) coreType = coreType[..^1];
        if (coreType.EndsWith("[]")) return null; // arrays handled elsewhere

        // W1 Slice 1 (D7): the field's type must come from the user-type registry.
        // The old i32 default guessed a width/signedness; a wrong guess reasons at
        // the wrong wrap boundary and can mint a false Proven that elides a guard.
        var fieldType = ResolveRegisteredFieldType(coreType, node.FieldName);
        if (fieldType is null)
            return null;

        // Cache the field accessor function decl per (type, field).
        var key = (coreType, node.FieldName);
        if (!_fieldAccessors.TryGetValue(key, out var accessor))
        {
            // Receiver's sort: if we declared the variable through CreateVariableForType
            // it'll be the uninterpreted sort we cached. Otherwise create one now.
            if (!_userTypeSorts.TryGetValue(coreType, out var receiverSort))
            {
                receiverSort = MarkUninterpretedSort(coreType);
                _userTypeSorts[coreType] = receiverSort;
            }

            var resultSort = ResultSortForType(fieldType);
            if (resultSort is null)
            {
                return Refuse(
                    $"field '{coreType}.{node.FieldName}' has unsupported type '{fieldType}': " +
                    "modeling it as a guessed sort could produce an unsound proof");
            }
            accessor = _ctx.MkFuncDecl($"{coreType}_{node.FieldName}", new[] { receiverSort }, resultSort);
            _fieldAccessors[key] = accessor;
        }

        var applied = accessor.Apply(target);

        // If the field is an integer, track its width so subsequent operations work.
        if (applied is BitVecExpr bv && fieldType is "i32" or "int" or "i64" or "long" or "u32" or "uint" or "u64" or "ulong" or "i8" or "i16" or "u8" or "u16" or "byte" or "short" or "ushort")
        {
            var (w, signed) = GetTypeWidthAndSignedness(fieldType);
            if (w > 0) TrackBitVec(bv, w, signed);
        }
        return applied;
    }

    /// <summary>
    /// Maps a Calor field type to the corresponding Z3 sort, used as the result sort of
    /// uninterpreted field accessors. Unsupported or empty types are refused by the caller.
    /// </summary>
    private Sort? ResultSortForType(string typeName)
    {
        var t = NormalizeTypeName(typeName);
        if (t.EndsWith("?")) t = t[..^1];
        return t switch
        {
            "i8" or "u8" => _ctx.MkBitVecSort(8),
            "i16" or "u16" => _ctx.MkBitVecSort(16),
            "i32" or "u32" or "int" or "uint" => _ctx.MkBitVecSort(32),
            "i64" or "u64" or "long" or "ulong" => _ctx.MkBitVecSort(64),
            "bool" => _ctx.BoolSort,
            // Like every other string-sort site (D3/D12), this arm mints a field accessor's
            // result sort and accessor.Apply(target) does not pass through TrackString.
            "string" or "str" => MarkStringSort(),
            "f32" or "f64" or "float" or "double" => null,
            _ when !string.IsNullOrEmpty(t) => _userTypeSorts.TryGetValue(t, out var s)
                                               ? s
                                               : (_userTypeSorts[t] = MarkUninterpretedSort(t)),
            _ => null,
        };
    }

    /// <summary>
    /// Translates array length access: arr.Length -> BitVecExpr (32-bit unsigned)
    /// </summary>
    private Expr? TranslateArrayLength(ArrayLengthNode node)
    {
        if (node.Array is ReferenceNode arrayRef)
        {
            var lengthVarName = $"{arrayRef.Name}$length";

            // Check if we already have a length variable for this array
            if (_variables.TryGetValue(lengthVarName, out var lengthVar))
                return lengthVar.Expr;

            // D6 adjudication (W1 Slice 1): on-demand $length is u32 always — no
            // width is guessed — and this path is unreachable from the §Q/§S proof
            // path (see TranslateArrayAccess). Create unsigned 32-bit length.
            var lengthExpr = MarkArrayLength(lengthVarName);
            _variables[lengthVarName] = (lengthExpr, "u32");

            return lengthExpr;
        }
        return null;
    }

    /// <summary>
    /// Declares an array variable with the given name and element type.
    /// Also creates an associated length variable.
    /// </summary>
    /// <param name="name">Array variable name.</param>
    /// <param name="elementType">The type of array elements (e.g., "i32", "u8").</param>
    /// <returns>True if the array was declared successfully.</returns>
    public bool DeclareArrayVariable(string name, string elementType)
    {
        var (elementWidth, elementSigned) = GetTypeWidthAndSignedness(elementType);
        if (elementWidth == 0)
            return false;

        // Create array sort: BitVec64 (index) -> BitVec[elementWidth] (element)
        var bv64Sort = _ctx.MkBitVecSort(64);
        var elementSort = _ctx.MkBitVecSort(elementWidth);
        TouchedNullableReferenceSort = true;
        var arrayExpr = _ctx.MkArrayConst(name, bv64Sort, elementSort);

        _variables[name] = (arrayExpr, $"array<{elementType}>");

        // Create associated length variable (unsigned 32-bit)
        var lengthVarName = $"{name}$length";
        var lengthExpr = MarkArrayLength(lengthVarName);
        _variables[lengthVarName] = (lengthExpr, "u32");

        _arrayInfo[name] = new ArrayInfo(elementType, lengthExpr);
        return true;
    }

    /// <summary>
    /// Gets the bit width and signedness for a type name.
    /// </summary>
    private (uint Width, bool IsSigned) GetTypeWidthAndSignedness(string typeName)
    {
        var normalizedType = NormalizeTypeName(typeName);
        return normalizedType switch
        {
            "i8" or "sbyte" => (8, true),
            "i16" or "short" => (16, true),
            "i32" or "int" => (32, true),
            "i64" or "long" => (64, true),
            "u8" or "byte" => (8, false),
            "u16" or "ushort" => (16, false),
            "u32" or "uint" => (32, false),
            "u64" or "ulong" => (64, false),
            _ => (0, false) // Unsupported type
        };
    }

    // ===========================================
    // Diagnostic Support
    // ===========================================

    /// <summary>
    /// Diagnoses why translation failed for an expression.
    /// Returns a human-readable description of the first unsupported construct found.
    /// </summary>
    /// <param name="node">The expression that failed to translate.</param>
    /// <returns>A diagnostic message, or null if no specific issue was identified.</returns>
    public string? DiagnoseTranslationFailure(ExpressionNode node)
    {
        return DiagnoseNode(node);
    }

    private string? DiagnoseNode(ExpressionNode node)
    {
        return node switch
        {
            FloatLiteralNode f => $"Floating-point literal '{f.Value}' is not supported (Z3 bit-vector theory does not model floats)",
            IntLiteralNode i when (i.IsUnsigned && i.UnsignedValue > int.MaxValue) || i.Value > int.MaxValue || i.Value < int.MinValue
                => $"Integer literal '{(i.IsUnsigned ? i.UnsignedValue.ToString() : i.Value.ToString())}' is outside the signed 32-bit translation domain",
            CallExpressionNode c => $"Function call '{c.Target}' is not supported (only built-in operations are verifiable)",
            ReferenceNode r when !_variables.ContainsKey(r.Name) => $"Unknown variable '{r.Name}'",
            StringOperationNode s => DiagnoseStringOperation(s),
            BinaryOperationNode b => DiagnoseBinaryOp(b),
            UnaryOperationNode u => DiagnoseUnaryOp(u),
            ConditionalExpressionNode c => DiagnoseConditional(c),
            ForallExpressionNode f => DiagnoseForall(f),
            ExistsExpressionNode e => DiagnoseExists(e),
            ImplicationExpressionNode i => DiagnoseImplication(i),
            ArrayAccessNode a => DiagnoseArrayAccess(a),
            ArrayLengthNode l => DiagnoseArrayLength(l),
            _ => $"Unsupported expression type: {node.GetType().Name}"
        };
    }

    private string? DiagnoseStringOperation(StringOperationNode node)
    {
        // Check if this is an unsupported string operation
        var unsupportedOps = new[] {
            StringOp.ToUpper, StringOp.ToLower, StringOp.Trim, StringOp.TrimStart, StringOp.TrimEnd,
            StringOp.PadLeft, StringOp.PadRight, StringOp.Split, StringOp.Join, StringOp.Format,
            StringOp.RegexTest, StringOp.IsNullOrWhiteSpace
        };

        if (unsupportedOps.Contains(node.Operation))
        {
            return $"String operation '{node.Operation}' is not supported (Z3 string theory lacks this operation)";
        }

        // D4: modes other than Ordinal are refused, not approximated (see TranslateStringOperation).
        if (node.ComparisonMode.HasValue && node.ComparisonMode.Value != StringComparisonMode.Ordinal)
        {
            return $"String comparison mode '{node.ComparisonMode.Value}' is not supported " +
                   "(Z3 string theory models ordinal comparison only)";
        }

        // D4 second half: these three resolve to the CURRENT-CULTURE .NET overload when no mode is
        // given, so the mode must be stated explicitly for the ordinal model to be honest.
        if (node.Operation is StringOp.StartsWith or StringOp.EndsWith or StringOp.IndexOf
            && node.ComparisonMode != StringComparisonMode.Ordinal)
        {
            return $"String operation '{node.Operation}' without an explicit ':ordinal' comparison " +
                   "mode is not supported (.NET uses CurrentCulture; the solver models ordinal)";
        }

        // Check arguments recursively
        foreach (var arg in node.Arguments)
        {
            var argResult = Translate(arg);
            if (argResult == null)
            {
                var argDiag = DiagnoseNode(arg);
                if (argDiag != null)
                    return argDiag;
            }
        }

        // Check if arguments have wrong types
        if (node.Arguments.Count > 0)
        {
            var firstArg = Translate(node.Arguments[0]);
            if (firstArg != null && firstArg is not SeqExpr)
            {
                return $"String operation '{node.Operation}' requires a string argument, but got {firstArg.GetType().Name}";
            }
        }

        // Check for operations that require integer arguments (IndexOf start, Substring indices)
        if (node.Operation == StringOp.IndexOf && node.Arguments.Count >= 3)
        {
            var startArg = Translate(node.Arguments[2]);
            if (startArg != null && startArg is not BitVecExpr && startArg is not IntExpr)
            {
                return $"IndexOf start index must be an integer, but got {startArg.GetType().Name}";
            }
        }

        if (node.Operation == StringOp.Substring && node.Arguments.Count >= 3)
        {
            var startArg = Translate(node.Arguments[1]);
            var lenArg = Translate(node.Arguments[2]);
            if (startArg != null && startArg is not BitVecExpr && startArg is not IntExpr)
            {
                return $"Substring start index must be an integer, but got {startArg.GetType().Name}";
            }
            if (lenArg != null && lenArg is not BitVecExpr && lenArg is not IntExpr)
            {
                return $"Substring length must be an integer, but got {lenArg.GetType().Name}";
            }
        }

        if (node.Operation == StringOp.SubstringFrom && node.Arguments.Count >= 2)
        {
            var startArg = Translate(node.Arguments[1]);
            if (startArg != null && startArg is not BitVecExpr && startArg is not IntExpr)
            {
                return $"SubstringFrom start index must be an integer, but got {startArg.GetType().Name}";
            }
        }

        return null;
    }

    private string? DiagnoseBinaryOp(BinaryOperationNode node)
    {
        var left = Translate(node.Left);
        var right = Translate(node.Right);

        if (left == null)
            return DiagnoseNode(node.Left);
        if (right == null)
            return DiagnoseNode(node.Right);

        // Check for type mismatches
        return node.Operator switch
        {
            BinaryOperator.Add or BinaryOperator.Subtract or BinaryOperator.Multiply or
            BinaryOperator.Divide or BinaryOperator.Modulo when left is not BitVecExpr || right is not BitVecExpr
                => $"Arithmetic operator '{node.Operator}' requires integer operands, but got {left.GetType().Name} and {right.GetType().Name}",

            BinaryOperator.And or BinaryOperator.Or when left is not BoolExpr || right is not BoolExpr
                => $"Logical operator '{node.Operator}' requires boolean operands, but got {left.GetType().Name} and {right.GetType().Name}",

            BinaryOperator.BitwiseAnd or BinaryOperator.BitwiseOr or BinaryOperator.BitwiseXor or
            BinaryOperator.LeftShift or BinaryOperator.RightShift when left is not BitVecExpr || right is not BitVecExpr
                => $"Bitwise operator '{node.Operator}' requires integer operands, but got {left.GetType().Name} and {right.GetType().Name}",

            BinaryOperator.LessThan or BinaryOperator.LessOrEqual or
            BinaryOperator.GreaterThan or BinaryOperator.GreaterOrEqual when left is not BitVecExpr || right is not BitVecExpr
                => $"Comparison operator '{node.Operator}' requires integer operands, but got {left.GetType().Name} and {right.GetType().Name} (string ordering is not modeled)",

            _ => null
        };
    }

    private string? DiagnoseUnaryOp(UnaryOperationNode node)
    {
        var operand = Translate(node.Operand);
        if (operand == null)
            return DiagnoseNode(node.Operand);

        return node.Operator switch
        {
            UnaryOperator.Not when operand is not BoolExpr
                => $"Logical NOT requires a boolean operand, but got {operand.GetType().Name}",
            UnaryOperator.Negate when operand is not BitVecExpr
                => $"Negation requires an integer operand, but got {operand.GetType().Name}",
            _ => null
        };
    }

    private string? DiagnoseConditional(ConditionalExpressionNode node)
    {
        var cond = Translate(node.Condition);
        if (cond == null)
            return DiagnoseNode(node.Condition);
        if (cond is not BoolExpr)
            return $"Conditional expression requires boolean condition, but got {cond.GetType().Name}";

        var whenTrue = Translate(node.WhenTrue);
        if (whenTrue == null)
            return DiagnoseNode(node.WhenTrue);

        var whenFalse = Translate(node.WhenFalse);
        if (whenFalse == null)
            return DiagnoseNode(node.WhenFalse);

        // Check for type mismatches between branches
        if (whenTrue.GetType() != whenFalse.GetType())
        {
            // Allow BitVecExpr with different widths (they can be normalized)
            if (whenTrue is BitVecExpr && whenFalse is BitVecExpr)
                return null;

            return $"Conditional branches have incompatible types: '{whenTrue.GetType().Name}' and '{whenFalse.GetType().Name}'";
        }

        return null;
    }

    private string? DiagnoseForall(ForallExpressionNode node)
    {
        // Register bound variables in a scope before diagnosing the body —
        // otherwise the quantifier's own variable is misreported as unknown
        // (#822 review M2).
        PushScope();
        try
        {
            foreach (var bv in node.BoundVariables)
            {
                var z3Var = CreateVariableForType(bv.Name, bv.TypeName);
                if (z3Var == null)
                    return $"Unsupported type '{bv.TypeName}' for bound variable '{bv.Name}' in forall expression";
                _variables[bv.Name] = (z3Var, bv.TypeName);
            }

            return DiagnoseNode(node.Body);
        }
        finally
        {
            PopScope();
        }
    }

    private string? DiagnoseExists(ExistsExpressionNode node)
    {
        PushScope();
        try
        {
            foreach (var bv in node.BoundVariables)
            {
                var z3Var = CreateVariableForType(bv.Name, bv.TypeName);
                if (z3Var == null)
                    return $"Unsupported type '{bv.TypeName}' for bound variable '{bv.Name}' in exists expression";
                _variables[bv.Name] = (z3Var, bv.TypeName);
            }

            return DiagnoseNode(node.Body);
        }
        finally
        {
            PopScope();
        }
    }

    private string? DiagnoseImplication(ImplicationExpressionNode node)
    {
        var ante = Translate(node.Antecedent);
        if (ante == null)
            return DiagnoseNode(node.Antecedent);
        if (ante is not BoolExpr)
            return $"Implication antecedent must be boolean, but got {ante.GetType().Name}";

        var cons = Translate(node.Consequent);
        if (cons == null)
            return DiagnoseNode(node.Consequent);
        if (cons is not BoolExpr)
            return $"Implication consequent must be boolean, but got {cons.GetType().Name}";

        return null;
    }

    private string? DiagnoseArrayAccess(ArrayAccessNode node)
    {
        if (node.Array is ReferenceNode baseRef
            && _variables.TryGetValue(baseRef.Name, out var baseVar)
            && !baseVar.Type.Contains('[')
            && !baseVar.Type.StartsWith("array<", StringComparison.Ordinal))
        {
            // Auto-declared arrays are typed "array<T>" (no bracket) — exempt them
            // so an index-typing diagnosis can fire instead (#822 re-verification
            // m-new-1).
            return $"Array access on '{baseRef.Name}', which has non-array type '{baseVar.Type}'";
        }

        if (node.Array is not ReferenceNode)
        {
            var arrayType = node.Array.GetType().Name;
            return $"Array access requires a simple variable reference, but got '{arrayType}' " +
                   "(computed array expressions like method returns or nested accesses are not supported)";
        }

        var index = Translate(node.Index);
        if (index == null)
            return DiagnoseNode(node.Index);
        if (index is not BitVecExpr)
            return $"Array index must be an integer, but got {index.GetType().Name}";

        return null;
    }

    private string? DiagnoseArrayLength(ArrayLengthNode node)
    {
        if (node.Array is not ReferenceNode)
            return "Array length requires a simple variable reference";

        return null;
    }

    /// <summary>
    /// Diagnoses why a boolean expression translation failed.
    /// This is useful when Translate succeeds but TranslateBoolExpr returns null.
    /// </summary>
    /// <param name="node">The expression that failed to translate to boolean.</param>
    /// <returns>A diagnostic message.</returns>
    public string? DiagnoseBoolExprFailure(ExpressionNode node)
    {
        var expr = Translate(node);
        if (expr == null)
            return DiagnoseTranslationFailure(node);

        if (expr is not BoolExpr)
        {
            return $"Expression must be boolean for verification, but got {expr.GetType().Name}. " +
                   "Boolean expressions include comparisons (==, !=, <, >, <=, >=), " +
                   "logical operations (&&, ||, !), and boolean variables.";
        }

        return null;
    }

    /// <summary>
    /// Gets a description of why a type is not supported.
    /// </summary>
    /// <param name="typeName">The type name that was not supported.</param>
    /// <returns>A diagnostic message.</returns>
    public static string DiagnoseUnsupportedType(string typeName)
    {
        var normalized = typeName.ToLowerInvariant();
        return normalized switch
        {
            "f32" or "f64" or "float" or "double" or "single" or "decimal"
                => $"Type '{typeName}' is not supported (floating-point types cannot be verified with bit-vector theory)",
            "object" or "dynamic"
                => $"Type '{typeName}' is not supported (reference/dynamic types cannot be statically verified)",
            var t when t.Contains("func") || t.Contains("action") || t.Contains("delegate")
                => $"Type '{typeName}' is not supported (function/delegate types cannot be verified)",
            _ => $"Type '{typeName}' is not supported for verification"
        };
    }
}

/// <summary>
/// The positive modeled-forms whitelist (guarantees plan D-G2.3): the single
/// in-code enumeration of what the contract prover models. `TryValidate` is the
/// gate — <see cref="Z3Verifier"/> runs it before translation, so anything
/// outside the whitelist is `unsupported` BY CONSTRUCTION rather than by
/// whichever translator branch happens to return null ("a blacklist by
/// accident", strategy §1.2). `RenderWhitelist` is the canonical enumeration a
/// conformance test compares against the generated appendix in
/// docs/verification-modeled-forms.md — the document no longer carries the only
/// enumeration. Keep this class and the translator in lockstep: a
/// whitelist-accepted form that fails to translate is surfaced as whitelist
/// DRIFT in the outcome reason and pinned by ModeledFormsTests.
/// </summary>
public static class ModeledForms
{
    /// <summary>Scalar types modeled as solver variables (canonical spellings; aliases normalize).</summary>
    public static readonly IReadOnlyList<string> ScalarTypes =
        ["i8", "i16", "i32", "i64", "u8", "u16", "u32", "u64", "bool", "str"];

    /// <summary>Binary operators the translator models (bit-vector/bool/string semantics per the doc).</summary>
    public static readonly IReadOnlyList<BinaryOperator> Operators =
    [
        BinaryOperator.Add, BinaryOperator.Subtract, BinaryOperator.Multiply,
        BinaryOperator.Divide, BinaryOperator.Modulo,
        BinaryOperator.Equal, BinaryOperator.NotEqual,
        BinaryOperator.LessThan, BinaryOperator.LessOrEqual,
        BinaryOperator.GreaterThan, BinaryOperator.GreaterOrEqual,
        BinaryOperator.And, BinaryOperator.Or,
        BinaryOperator.BitwiseAnd, BinaryOperator.BitwiseOr, BinaryOperator.BitwiseXor,
        BinaryOperator.LeftShift, BinaryOperator.RightShift
    ];

    /// <summary>Unary operators the translator models.</summary>
    public static readonly IReadOnlyList<UnaryOperator> UnaryOperators =
        [UnaryOperator.Not, UnaryOperator.Negate];

    /// <summary>
    /// String operations modeled via Z3's string theory, ORDINAL only (divergence D4). Two
    /// refusals, both enforced by the translator and <see cref="TryValidate"/> alike:
    /// a non-ordinal <c>ComparisonMode</c> on any of these, and <c>StartsWith</c>/<c>EndsWith</c>/
    /// <c>IndexOf</c> with NO mode — .NET resolves those single-argument overloads to
    /// <c>CurrentCulture</c>, unlike <c>Contains</c>/<c>Equals</c>, which are ordinal.
    /// Replace is deliberately absent: Z3's MkReplace substitutes the FIRST occurrence while .NET's
    /// string.Replace substitutes ALL occurrences — a whitelisted divergence that
    /// could mint a false Proven and elide a runtime guard (W1 Slice 1, T1).
    /// </summary>
    public static readonly IReadOnlyList<StringOp> StringOperations =
    [
        StringOp.Length, StringOp.Contains, StringOp.StartsWith, StringOp.EndsWith,
        StringOp.Equals, StringOp.IsNullOrEmpty, StringOp.IndexOf, StringOp.Substring,
        StringOp.SubstringFrom, StringOp.Concat
    ];

    /// <summary>Expression node kinds the whitelist accepts (children validated recursively).</summary>
    public static readonly IReadOnlyList<string> ExpressionKinds =
    [
        nameof(IntLiteralNode), nameof(BoolLiteralNode), nameof(StringLiteralNode),
        nameof(ReferenceNode), nameof(BinaryOperationNode), nameof(UnaryOperationNode),
        nameof(ConditionalExpressionNode), nameof(ForallExpressionNode), nameof(ExistsExpressionNode),
        nameof(ImplicationExpressionNode), nameof(ArrayAccessNode), nameof(ArrayLengthNode),
        nameof(FieldAccessNode), nameof(StringOperationNode), nameof(SelfRefNode)
    ];

    private static readonly HashSet<string> s_floatTypeSpellings = new(StringComparer.OrdinalIgnoreCase)
    {
        // The ONLY bound-variable types the translator refuses are floating-point
        // (CreateVariableForType returns null for them); every other type is
        // declarable — modeled scalars directly, unknown types as uninterpreted
        // sorts. The whitelist matches the translator EXACTLY (a hand-narrowed
        // set regressed alias spellings and user types — #822 review M1); any
        // deliberate tightening must be its own recorded change.
        "f32", "f64", "float", "double", "single", "system.single", "system.double"
    };

    /// <summary>
    /// Validates that the expression tree lies entirely inside the modeled
    /// surface. On failure, <paramref name="offending"/> names the first
    /// out-of-whitelist construct in human terms. Purely syntactic: name
    /// resolution, typing, and declarability are the translator's concern.
    /// </summary>
    public static bool TryValidate(ExpressionNode expr, out string? offending)
    {
        switch (expr)
        {
            case IntLiteralNode or BoolLiteralNode or StringLiteralNode or ReferenceNode or SelfRefNode:
                offending = null;
                return true;

            case BinaryOperationNode b:
                if (!Operators.Contains(b.Operator))
                {
                    offending = $"binary operator '{b.Operator}'";
                    return false;
                }
                return TryValidate(b.Left, out offending) && TryValidate(b.Right, out offending);

            case UnaryOperationNode u:
                if (!UnaryOperators.Contains(u.Operator))
                {
                    offending = $"unary operator '{u.Operator}'";
                    return false;
                }
                return TryValidate(u.Operand, out offending);

            case ConditionalExpressionNode c:
                return TryValidate(c.Condition, out offending)
                    && TryValidate(c.WhenTrue, out offending)
                    && TryValidate(c.WhenFalse, out offending);

            case ForallExpressionNode f:
                foreach (var bv in f.BoundVariables)
                {
                    if (s_floatTypeSpellings.Contains(bv.TypeName))
                    {
                        offending = $"quantifier bound variable of floating-point type '{bv.TypeName}'";
                        return false;
                    }
                }
                return TryValidate(f.Body, out offending);

            case ExistsExpressionNode e:
                foreach (var bv in e.BoundVariables)
                {
                    if (s_floatTypeSpellings.Contains(bv.TypeName))
                    {
                        offending = $"quantifier bound variable of floating-point type '{bv.TypeName}'";
                        return false;
                    }
                }
                return TryValidate(e.Body, out offending);

            case ImplicationExpressionNode i:
                return TryValidate(i.Antecedent, out offending) && TryValidate(i.Consequent, out offending);

            case ArrayAccessNode a:
                return TryValidate(a.Array, out offending) && TryValidate(a.Index, out offending);

            case ArrayLengthNode al:
                return TryValidate(al.Array, out offending);

            case FieldAccessNode fa:
                return TryValidate(fa.Target, out offending);

            case StringOperationNode sop:
                if (!StringOperations.Contains(sop.Operation))
                {
                    offending = $"string operation '{sop.Operation}'";
                    return false;
                }
                // D4: a non-ordinal comparison mode is OUTSIDE the modeled surface, for the same
                // reason Replace is absent above — the solver has ordinal semantics only.
                //
                // To be precise about what this line does and does not do: the translator refusal
                // is what makes D4 sound. Z3Verifier.AcceptedButUntranslatable already turns a
                // whitelist-accepted-but-refused form into Unsupported, so a translator-only fix
                // would have closed the vector. Pre-fix the whitelist and the translator AGREED —
                // on a model that did not match .NET, which is the actual defect. Keeping the two
                // in step buys a precise message and keeps the GateReject drift detector quiet.
                if (sop.ComparisonMode.HasValue && sop.ComparisonMode.Value != StringComparisonMode.Ordinal)
                {
                    offending = $"string comparison mode '{sop.ComparisonMode.Value}'";
                    return false;
                }
                // D4 second half: for these three, ABSENCE of a mode means CurrentCulture in .NET,
                // so the modeled surface requires the mode to be stated explicitly.
                if (sop.Operation is StringOp.StartsWith or StringOp.EndsWith or StringOp.IndexOf
                    && sop.ComparisonMode != StringComparisonMode.Ordinal)
                {
                    offending = $"'{sop.Operation}' without an explicit ':ordinal' comparison mode";
                    return false;
                }
                foreach (var arg in sop.Arguments)
                {
                    if (!TryValidate(arg, out offending))
                        return false;
                }
                offending = null;
                return true;

            default:
                offending = expr switch
                {
                    FloatLiteralNode => "floating-point literal",
                    CallExpressionNode => "function call",
                    _ => $"expression kind '{expr.GetType().Name}'"
                };
                return false;
        }
    }

    /// <summary>
    /// Canonical, deterministic rendering of the whitelist — the source of the
    /// generated appendix in docs/verification-modeled-forms.md (conformance-
    /// checked by ModeledFormsTests; regenerate the doc block from this output
    /// when the whitelist changes).
    /// </summary>
    public static string RenderWhitelist()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("scalar-types: ").Append(string.Join(", ", ScalarTypes)).Append('\n');
        sb.Append("array-element-types: i8, i16, i32, i64, u8, u16, u32, u64 (with synthetic $length)\n");
        sb.Append("expression-kinds: ").Append(string.Join(", ", ExpressionKinds)).Append('\n');
        sb.Append("binary-operators: ").Append(string.Join(", ", Operators)).Append('\n');
        sb.Append("unary-operators: ").Append(string.Join(", ", UnaryOperators)).Append('\n');
        sb.Append("string-operations: ").Append(string.Join(", ", StringOperations)).Append('\n');
        // D4. Rendered because the appendix is the canonical statement of the modeled surface: D9
        // recorded its narrowing by REMOVING Replace from the list above, but D4 narrows within an
        // operation, so without this line a reader cannot tell that a mode-bearing Contains, or a
        // bare StartsWith, is out of scope.
        sb.Append("string-comparison-modes: Ordinal only — a non-ordinal mode is refused, and ")
          .Append("StartsWith/EndsWith/IndexOf require ':ordinal' EXPLICITLY (.NET resolves their ")
          .Append("no-mode overloads to CurrentCulture; Contains/Equals are ordinal)\n");
        sb.Append("quantifier-bound-variable-types: any declarable type except floating-point (unmodeled types become uninterpreted sorts)\n");
        return sb.ToString();
    }
}
