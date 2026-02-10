namespace Calor.Compiler.TypeChecking;

/// <summary>
/// Base class for all Calor types.
/// </summary>
public abstract class CalorType : IEquatable<CalorType>
{
    public abstract string Name { get; }

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

    public override string Name { get; }

    private PrimitiveType(string name)
    {
        Name = name;
    }

    public static PrimitiveType? FromName(string name)
    {
        return name.ToUpperInvariant() switch
        {
            "VOID" => Void,
            "INT" or "INT32" => Int,
            "FLOAT" or "FLOAT64" or "DOUBLE" => Float,
            "BOOL" or "BOOLEAN" => Bool,
            "STRING" or "STR" => String,
            "UNIT" => Unit,
            _ => null
        };
    }

    public override bool Equals(CalorType? other)
        => other is PrimitiveType pt && pt.Name == Name;

    public override int GetHashCode() => Name.GetHashCode();
}

/// <summary>
/// Represents an Option[T] type.
/// </summary>
public sealed class OptionType : CalorType
{
    public CalorType InnerType { get; }
    public override string Name => $"Option<{InnerType.Name}>";

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
            return Id == tv.Id;
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
