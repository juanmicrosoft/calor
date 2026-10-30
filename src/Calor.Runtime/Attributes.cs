namespace Calor.Runtime;

/// <summary>
/// Represents the absence of a meaningful value usable as a type parameter.
/// </summary>
public readonly struct Unit : IEquatable<Unit>
{
    public static readonly Unit Value = default;

    public bool Equals(Unit other) => true;
    public override bool Equals(object? obj) => obj is Unit;
    public override int GetHashCode() => 0;
    public override string ToString() => "()";

    public static bool operator ==(Unit left, Unit right) => true;
    public static bool operator !=(Unit left, Unit right) => false;
}

/// <summary>
/// Marks a method as an Calor function with its original metadata.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class CalorFunctionAttribute : Attribute
{
    public string Id { get; }
    public string? OriginalName { get; set; }

    public CalorFunctionAttribute(string id)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
    }
}

/// <summary>
/// Marks a method with semantic information about its behavior.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class CalorSemanticAttribute : Attribute
{
    public string Key { get; }
    public string Value { get; }

    public CalorSemanticAttribute(string key, string value)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }
}

/// <summary>
/// Marks a class or module as generated from Calor.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
public sealed class CalorModuleAttribute : Attribute
{
    public string Id { get; }
    public string? OriginalName { get; set; }

    public CalorModuleAttribute(string id)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
    }
}

/// <summary>
/// Declares an effect that a method may perform.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class CalorEffectAttribute : Attribute
{
    public string EffectType { get; }
    public string? Description { get; set; }

    public CalorEffectAttribute(string effectType)
    {
        EffectType = effectType ?? throw new ArgumentNullException(nameof(effectType));
    }
}

/// <summary>
/// Declares a precondition for a method.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class CalorRequiresAttribute : Attribute
{
    public string Condition { get; }

    public CalorRequiresAttribute(string condition)
    {
        Condition = condition ?? throw new ArgumentNullException(nameof(condition));
    }
}

/// <summary>
/// Declares a postcondition for a method.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class CalorEnsuresAttribute : Attribute
{
    public string Condition { get; }

    public CalorEnsuresAttribute(string condition)
    {
        Condition = condition ?? throw new ArgumentNullException(nameof(condition));
    }
}
