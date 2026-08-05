namespace Calor.Compiler.TypeChecking;

/// <summary>
/// Base class for all Calor types.
/// </summary>
public abstract class CalorType : IEquatable<CalorType>
{
    /// <summary>
    /// The internal type name (e.g. <c>INT</c>, <c>STRING</c>). Used as an identity key
    /// for equality/hashing and by <see cref="PrimitiveType.FromName"/> — NOT for
    /// user-facing text. Diagnostics must use <see cref="SurfaceName"/> instead (#741).
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// The compact surface spelling agents actually write (<c>i32</c>, <c>str</c>,
    /// <c>bool</c>, <c>Option&lt;str&gt;</c>) — the form every diagnostic that echoes a
    /// type must use, so the message never teaches an un-writable internal spelling.
    /// Defaults to <see cref="Name"/> (correct for user/record/union/param types whose
    /// name is already what the user wrote); primitives and composites override to
    /// surface-spell themselves and recurse (#741).
    /// </summary>
    public virtual string SurfaceName => Name;

    public abstract bool Equals(CalorType? other);
    public override bool Equals(object? obj) => obj is CalorType other && Equals(other);
    public abstract override int GetHashCode();
    public override string ToString() => Name;

    public static bool operator ==(CalorType? left, CalorType? right)
        => ReferenceEquals(left, right) || (left?.Equals(right) ?? false);
    public static bool operator !=(CalorType? left, CalorType? right) => !(left == right);
}

/// <summary>
/// Represents primitive types (INT, FLOAT, BOOL, STRING, VOID).
/// </summary>
public sealed class PrimitiveType : CalorType
{
    public static readonly PrimitiveType Void = new("VOID");
    public static readonly PrimitiveType Int = new("INT");
    public static readonly PrimitiveType Float = new("FLOAT");
    public static readonly PrimitiveType Bool = new("BOOL");
    public static readonly PrimitiveType String = new("STRING");
    public static readonly PrimitiveType Unit = new("UNIT");

    /// <summary>
    /// <c>char</c>. Distinct from <see cref="Int"/> deliberately: collapsing it would make
    /// `char + i32` type-check, and C# does promote there, but it would also make a `char`
    /// silently satisfy an `i32` parameter, which is the error worth catching.
    /// </summary>
    public static readonly PrimitiveType Char = new("CHAR");

    /// <summary>
    /// <c>decimal</c>. Kept apart from <see cref="Float"/> because C# has no implicit
    /// decimal↔double conversion in either direction — treating them as one type would accept
    /// programs the emitted C# rejects.
    /// </summary>
    public static readonly PrimitiveType Decimal = new("DECIMAL");

    /// <summary>
    /// <c>object</c> — the top type. Everything is assignable TO it and nothing is assignable
    /// FROM it without a cast, which is what <see cref="TypeChecker.IsAssignable"/> implements.
    /// </summary>
    public static readonly PrimitiveType Object = new("OBJECT");

    public override string Name { get; }

    /// <summary>
    /// The surface spelling for a diagnostic. Note the type system collapses every integer
    /// width into a single <see cref="Int"/> primitive and every float width into
    /// <see cref="Float"/> (see <see cref="FromName"/>), so a collapsed primitive carries no
    /// width — it renders as the canonical default (<c>i32</c>/<c>f64</c>). This is exact for
    /// the only case that reaches here today: an <em>inferred</em> value (an integer literal
    /// is i32, a float literal is f64). A sized annotation like <c>i64</c> does NOT collapse
    /// to <c>i32</c> here — the opt-in checker does not resolve sized types at all, so they
    /// surface from their carried width string via <c>ToSurfaceSpelling</c>, not this map.
    /// </summary>
    /// <summary>
    /// The spelling the user actually wrote, when it differs from the canonical one for this
    /// type's family. Every integer width collapses to <see cref="Int"/> for CHECKING — that is
    /// the type system's existing design, and width analysis lives in the verifier — but a
    /// diagnostic must still echo `i64` for an `i64` binding. Without this, resolving the sized
    /// spellings (which previously failed outright with a spurious "Unknown type 'i64'") would
    /// have traded a false error for a message that quietly reports the wrong width.
    /// </summary>
    private readonly string? _surfaceOverride;

    public override string SurfaceName => _surfaceOverride ?? Name switch
    {
        "INT" => "i32",
        "FLOAT" => "f64",
        "BOOL" => "bool",
        "STRING" => "str",
        "VOID" => "void",
        "UNIT" => "unit",
        "CHAR" => "char",
        "DECIMAL" => "decimal",
        "OBJECT" => "object",
        _ => Name,
    };

    /// <summary>
    /// A width-carrying view of a collapsed primitive: equal to the canonical instance (equality
    /// and hashing are on <see cref="Name"/>, so <c>Sized("INT","i64") == Int</c>) but rendering
    /// the spelling the user wrote.
    /// </summary>
    private static PrimitiveType Sized(PrimitiveType canonical, string surface)
        => new(canonical.Name, surface);

    private PrimitiveType(string name, string? surfaceOverride)
    {
        Name = name;
        _surfaceOverride = surfaceOverride;
    }

    private PrimitiveType(string name)
    {
        Name = name;
    }

    public static PrimitiveType? FromName(string name)
    {
        return name.ToUpperInvariant() switch
        {
            "VOID" => Void,
            // Every integer width collapses to one Int. That is the type system's existing
            // choice (I32/I64 already did) and the sized-width checking lives in the verifier,
            // not here — but the SPELLINGS have to resolve, or a documented type reads as
            // "Unknown type 'u32'". docs/syntax-reference/types.md is the authority for this list.
            "INT" or "INT32" or "I32" => Int,
            "I64" or "INT64" => Sized(Int, "i64"),
            "I8" => Sized(Int, "i8"),
            "SBYTE" => Sized(Int, "i8"),
            "I16" or "INT16" or "SHORT" => Sized(Int, "i16"),
            "U8" or "BYTE" => Sized(Int, "u8"),
            "U16" or "UINT16" or "USHORT" => Sized(Int, "u16"),
            "U32" or "UINT" or "UINT32" => Sized(Int, "u32"),
            "U64" or "UINT64" or "ULONG" => Sized(Int, "u64"),
            "LONG" => Sized(Int, "i64"),
            "FLOAT" or "FLOAT64" or "DOUBLE" or "F64" => Float,
            "F32" or "SINGLE" => Sized(Float, "f32"),
            "DECIMAL" => Decimal,
            "BOOL" or "BOOLEAN" => Bool,
            "STRING" or "STR" => String,
            "CHAR" => Char,
            "OBJECT" => Object,
            "UNIT" => Unit,
            _ => null
        };
    }

    public override bool Equals(CalorType? other)
    {
        if (other is PrimitiveType pt) return pt.Name == Name;
        if (other is TypeVariable tv && tv.ResolvedType != null) return tv.ResolvedType.Equals(this);
        return false;
    }

    public override int GetHashCode() => Name.GetHashCode();
}

/// <summary>
/// A type the checker does not model — overwhelmingly a .NET type reached through interop
/// (<c>Type</c>, <c>StringComparison</c>, a class from a <c>using</c>).
///
/// <para>This exists because the alternative is a false positive by construction. The checker
/// has no BCL surface, so it cannot distinguish "external type" from "typo", and reporting
/// <c>Unknown type 'Type'</c> rejected programs that compile and run. The generated C# is
/// compiled regardless, and a genuine typo surfaces there as CS0246 with a better message than
/// this checker could produce — so the signal is deferred, not lost.</para>
///
/// <para>Assignability is permissive in both directions: nothing is known about the type, and
/// guessing in either direction would be inventing a verdict.</para>
/// </summary>
public sealed class ExternalType : CalorType
{
    public override string Name { get; }

    public ExternalType(string name) => Name = name;

    public override bool Equals(CalorType? other) => other is ExternalType et && et.Name == Name;
    public override int GetHashCode() => Name.GetHashCode();
}

/// <summary>
/// An array, <c>T[]</c>. A distinct kind rather than a <see cref="GenericInstanceType"/> named
/// "Array" so diagnostics echo <c>i32[]</c> — a spelling the user can actually write — instead of
/// <c>Array&lt;i32&gt;</c>, which is the #741 rule this file already applies to primitives.
/// </summary>
public sealed class ArrayType : CalorType
{
    public CalorType ElementType { get; }
    public override string Name => $"{ElementType.Name}[]";
    public override string SurfaceName => $"{ElementType.SurfaceName}[]";

    public ArrayType(CalorType elementType)
        => ElementType = elementType ?? throw new ArgumentNullException(nameof(elementType));

    public override bool Equals(CalorType? other)
        => other is ArrayType at && at.ElementType.Equals(ElementType);

    public override int GetHashCode() => HashCode.Combine("[]", ElementType);
}

/// <summary>
/// Represents an Option[T] type.
/// </summary>
public sealed class OptionType : CalorType
{
    public CalorType InnerType { get; }
    public override string Name => $"Option<{InnerType.Name}>";
    public override string SurfaceName => $"Option<{InnerType.SurfaceName}>";

    public OptionType(CalorType innerType)
    {
        InnerType = innerType ?? throw new ArgumentNullException(nameof(innerType));
    }

    public override bool Equals(CalorType? other)
        => other is OptionType ot && InnerType.Equals(ot.InnerType);

    public override int GetHashCode() => HashCode.Combine("Option", InnerType);
}

/// <summary>
/// Represents a Result[T, E] type.
/// </summary>
public sealed class ResultType : CalorType
{
    public CalorType OkType { get; }
    public CalorType ErrType { get; }
    public override string Name => $"Result<{OkType.Name}, {ErrType.Name}>";
    public override string SurfaceName => $"Result<{OkType.SurfaceName}, {ErrType.SurfaceName}>";

    public ResultType(CalorType okType, CalorType errType)
    {
        OkType = okType ?? throw new ArgumentNullException(nameof(okType));
        ErrType = errType ?? throw new ArgumentNullException(nameof(errType));
    }

    public override bool Equals(CalorType? other)
        => other is ResultType rt && OkType.Equals(rt.OkType) && ErrType.Equals(rt.ErrType);

    public override int GetHashCode() => HashCode.Combine("Result", OkType, ErrType);
}

/// <summary>
/// Represents a record type with named fields.
/// </summary>
public sealed class RecordType : CalorType
{
    public override string Name { get; }
    public IReadOnlyList<RecordField> Fields { get; }

    public RecordType(string name, IReadOnlyList<RecordField> fields)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Fields = fields ?? throw new ArgumentNullException(nameof(fields));
    }

    public RecordField? GetField(string name)
        => Fields.FirstOrDefault(f => f.Name == name);

    public override bool Equals(CalorType? other)
        => other is RecordType rt && rt.Name == Name;

    public override int GetHashCode() => Name.GetHashCode();
}

/// <summary>
/// Represents a field in a record type.
/// </summary>
public sealed class RecordField
{
    public string Name { get; }
    public CalorType Type { get; }
    public bool HasDefault { get; }

    public RecordField(string name, CalorType type, bool hasDefault = false)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Type = type ?? throw new ArgumentNullException(nameof(type));
        HasDefault = hasDefault;
    }
}

/// <summary>
/// Represents a discriminated union type.
/// </summary>
public sealed class UnionType : CalorType
{
    public override string Name { get; }
    public IReadOnlyList<UnionVariant> Variants { get; }

    public UnionType(string name, IReadOnlyList<UnionVariant> variants)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Variants = variants ?? throw new ArgumentNullException(nameof(variants));
    }

    public UnionVariant? GetVariant(string name)
        => Variants.FirstOrDefault(v => v.Name == name);

    public override bool Equals(CalorType? other)
        => other is UnionType ut && ut.Name == Name;

    public override int GetHashCode() => Name.GetHashCode();
}

/// <summary>
/// Represents a variant in a discriminated union.
/// </summary>
public sealed class UnionVariant
{
    public string Name { get; }
    public IReadOnlyList<RecordField> Fields { get; }

    public UnionVariant(string name, IReadOnlyList<RecordField> fields)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Fields = fields ?? throw new ArgumentNullException(nameof(fields));
    }
}

/// <summary>
/// Represents a function type.
/// </summary>
public sealed class FunctionType : CalorType
{
    public IReadOnlyList<CalorType> ParameterTypes { get; }
    public CalorType ReturnType { get; }
    public override string Name
    {
        get
        {
            var paramStr = string.Join(", ", ParameterTypes.Select(p => p.Name));
            return $"({paramStr}) -> {ReturnType.Name}";
        }
    }
    public override string SurfaceName
    {
        get
        {
            var paramStr = string.Join(", ", ParameterTypes.Select(p => p.SurfaceName));
            return $"({paramStr}) -> {ReturnType.SurfaceName}";
        }
    }

    public FunctionType(IReadOnlyList<CalorType> parameterTypes, CalorType returnType)
    {
        ParameterTypes = parameterTypes ?? throw new ArgumentNullException(nameof(parameterTypes));
        ReturnType = returnType ?? throw new ArgumentNullException(nameof(returnType));
    }

    public override bool Equals(CalorType? other)
    {
        if (other is not FunctionType ft) return false;
        if (!ReturnType.Equals(ft.ReturnType)) return false;
        if (ParameterTypes.Count != ft.ParameterTypes.Count) return false;
        return ParameterTypes.Zip(ft.ParameterTypes).All(pair => pair.First.Equals(pair.Second));
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add("Function");
        hash.Add(ReturnType);
        foreach (var p in ParameterTypes)
            hash.Add(p);
        return hash.ToHashCode();
    }
}

/// <summary>
/// Represents a refinement type: a base type constrained by a predicate.
/// E.g., NatInt is i32 where # >= 0.
/// At runtime, refinement types erase to their base type.
/// </summary>
public sealed class RefinedType : CalorType
{
    public CalorType BaseType { get; }
    public string PredicateText { get; }
    public Ast.ExpressionNode Predicate { get; }
    public override string Name => $"{BaseType.Name}{{{PredicateText}}}";
    public override string SurfaceName => $"{BaseType.SurfaceName}{{{PredicateText}}}";

    public RefinedType(CalorType baseType, string predicateText, Ast.ExpressionNode predicate)
    {
        BaseType = baseType ?? throw new ArgumentNullException(nameof(baseType));
        PredicateText = predicateText ?? throw new ArgumentNullException(nameof(predicateText));
        Predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
    }

    public override bool Equals(CalorType? other)
    {
        if (other is RefinedType rt)
            return BaseType.Equals(rt.BaseType) && PredicateText == rt.PredicateText;
        return false;
    }

    public override int GetHashCode() => HashCode.Combine("Refined", BaseType, PredicateText);
}

/// <summary>
/// Represents an unknown/error type used during type checking.
/// </summary>
public sealed class ErrorType : CalorType
{
    public static readonly ErrorType Instance = new();

    public override string Name => "<error>";

    private ErrorType() { }

    public override bool Equals(CalorType? other) => other is ErrorType;
    public override int GetHashCode() => "<error>".GetHashCode();
}

/// <summary>
/// Represents a type variable for inference.
/// </summary>
public sealed class TypeVariable : CalorType
{
    private static int _counter;

    public int Id { get; }
    public CalorType? ResolvedType { get; private set; }
    public override string Name => ResolvedType?.Name ?? $"T{Id}";
    public override string SurfaceName => ResolvedType?.SurfaceName ?? $"T{Id}";

    public TypeVariable()
    {
        Id = Interlocked.Increment(ref _counter);
    }

    public void Resolve(CalorType type)
    {
        if (ResolvedType != null)
            throw new InvalidOperationException("Type variable already resolved");
        ResolvedType = type;
    }

    public override bool Equals(CalorType? other)
    {
        if (other is TypeVariable tv)
            return Id == tv.Id || (ResolvedType != null && ResolvedType.Equals(tv.ResolvedType ?? (CalorType)tv));
        if (ResolvedType != null)
            return ResolvedType.Equals(other);
        return false;
    }

    public override int GetHashCode() => Id.GetHashCode();
}

/// <summary>
/// Represents a type parameter (e.g., T in List&lt;T&gt;).
/// Used during type checking to track type parameters declared in generic functions/classes.
/// </summary>
public sealed class TypeParameterType : CalorType
{
    public override string Name { get; }
    public IReadOnlyList<Ast.TypeConstraintNode> Constraints { get; }

    public TypeParameterType(string name, IReadOnlyList<Ast.TypeConstraintNode> constraints)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Constraints = constraints ?? Array.Empty<Ast.TypeConstraintNode>();
    }

    public TypeParameterType(string name)
        : this(name, Array.Empty<Ast.TypeConstraintNode>())
    {
    }

    public override bool Equals(CalorType? other)
        => other is TypeParameterType tpt && tpt.Name == Name;

    public override int GetHashCode() => Name.GetHashCode();
}

/// <summary>
/// Represents an instantiated generic type (e.g., List&lt;int&gt;, Dictionary&lt;string, T&gt;).
/// Used during type checking to track generic type instantiations.
/// </summary>
public sealed class GenericInstanceType : CalorType
{
    public string BaseName { get; }
    public IReadOnlyList<CalorType> TypeArguments { get; }
    public override string Name
    {
        get
        {
            var argsStr = string.Join(", ", TypeArguments.Select(a => a.Name));
            return $"{BaseName}<{argsStr}>";
        }
    }
    public override string SurfaceName
    {
        get
        {
            var argsStr = string.Join(", ", TypeArguments.Select(a => a.SurfaceName));
            return $"{BaseName}<{argsStr}>";
        }
    }

    public GenericInstanceType(string baseName, IReadOnlyList<CalorType> typeArguments)
    {
        BaseName = baseName ?? throw new ArgumentNullException(nameof(baseName));
        TypeArguments = typeArguments ?? throw new ArgumentNullException(nameof(typeArguments));
    }

    public override bool Equals(CalorType? other)
    {
        if (other is not GenericInstanceType git) return false;
        if (BaseName != git.BaseName) return false;
        if (TypeArguments.Count != git.TypeArguments.Count) return false;
        return TypeArguments.Zip(git.TypeArguments).All(pair => pair.First.Equals(pair.Second));
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(BaseName);
        foreach (var arg in TypeArguments)
            hash.Add(arg);
        return hash.ToHashCode();
    }
}
