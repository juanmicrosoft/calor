namespace Opal.Runtime;

/// <summary>
/// Marks a method as an OPAL function with its original metadata.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class OpalFunctionAttribute : Attribute
{
    public string Id { get; }
    public string? OriginalName { get; set; }

    public OpalFunctionAttribute(string id)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
    }
}

/// <summary>
/// Marks a method with semantic information about its behavior.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class OpalSemanticAttribute : Attribute
{
    public string Key { get; }
    public string Value { get; }

    public OpalSemanticAttribute(string key, string value)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }
}

/// <summary>
/// Marks a class or module as generated from OPAL.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
public sealed class OpalModuleAttribute : Attribute
{
    public string Id { get; }
    public string? OriginalName { get; set; }

    public OpalModuleAttribute(string id)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
    }
}

/// <summary>
/// Declares an effect that a method may perform.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class OpalEffectAttribute : Attribute
{
    public string EffectType { get; }
    public string? Description { get; set; }

    public OpalEffectAttribute(string effectType)
    {
        EffectType = effectType ?? throw new ArgumentNullException(nameof(effectType));
    }
}

/// <summary>
/// Declares a precondition for a method.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class OpalRequiresAttribute : Attribute
{
    public string Condition { get; }

    public OpalRequiresAttribute(string condition)
    {
        Condition = condition ?? throw new ArgumentNullException(nameof(condition));
    }
}

/// <summary>
/// Declares a postcondition for a method.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class OpalEnsuresAttribute : Attribute
{
    public string Condition { get; }

    public OpalEnsuresAttribute(string condition)
    {
        Condition = condition ?? throw new ArgumentNullException(nameof(condition));
    }
}
