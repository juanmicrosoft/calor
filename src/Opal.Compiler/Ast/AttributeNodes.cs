using Opal.Compiler.Parsing;

namespace Opal.Compiler.Ast;

/// <summary>
/// Represents a C#-style attribute in OPAL (e.g., [@HttpPost], [@Route("api/[controller]")]).
/// </summary>
public sealed class OpalAttributeNode : AstNode
{
    /// <summary>
    /// The attribute name (e.g., "HttpPost", "Route", "ApiController").
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The attribute arguments (positional and named).
    /// </summary>
    public IReadOnlyList<OpalAttributeArgument> Arguments { get; }

    public OpalAttributeNode(TextSpan span, string name, IReadOnlyList<OpalAttributeArgument>? arguments = null)
        : base(span)
    {
        Name = name;
        Arguments = arguments ?? Array.Empty<OpalAttributeArgument>();
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);

    /// <summary>
    /// Returns true if this attribute has no arguments.
    /// </summary>
    public bool HasNoArguments => Arguments.Count == 0;

    /// <summary>
    /// Returns true if this attribute has only positional arguments (no named arguments).
    /// </summary>
    public bool HasOnlyPositionalArguments => Arguments.All(a => a.Name == null);
}

/// <summary>
/// Represents an argument to a C#-style attribute.
/// </summary>
public sealed class OpalAttributeArgument
{
    /// <summary>
    /// The argument name for named arguments (e.g., "PropertyName" in [JsonProperty(PropertyName="foo")]).
    /// Null for positional arguments.
    /// </summary>
    public string? Name { get; }

    /// <summary>
    /// The argument value. Can be string, int, bool, double, or a type reference.
    /// </summary>
    public object Value { get; }

    /// <summary>
    /// Creates a positional argument.
    /// </summary>
    public OpalAttributeArgument(object value)
    {
        Name = null;
        Value = value;
    }

    /// <summary>
    /// Creates a named argument.
    /// </summary>
    public OpalAttributeArgument(string? name, object value)
    {
        Name = name;
        Value = value;
    }

    /// <summary>
    /// Returns true if this is a positional argument.
    /// </summary>
    public bool IsPositional => Name == null;

    /// <summary>
    /// Returns true if this is a named argument.
    /// </summary>
    public bool IsNamed => Name != null;

    /// <summary>
    /// Gets the value formatted as a string for OPAL/C# emission.
    /// </summary>
    public string GetFormattedValue()
    {
        return Value switch
        {
            string s => $"\"{EscapeString(s)}\"",
            bool b => b ? "true" : "false",
            int i => i.ToString(),
            long l => l.ToString(),
            double d => d.ToString(),
            float f => f.ToString(),
            // Type reference (typeof)
            Type t => $"typeof({t.Name})",
            // For type name strings that represent typeof expressions
            TypeOfReference tr => $"typeof({tr.TypeName})",
            // Default: treat as identifier/enum value
            _ => Value?.ToString() ?? "null"
        };
    }

    private static string EscapeString(string s)
    {
        return s
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }
}

/// <summary>
/// Represents a typeof() reference in an attribute argument.
/// </summary>
public sealed class TypeOfReference
{
    public string TypeName { get; }

    public TypeOfReference(string typeName)
    {
        TypeName = typeName;
    }

    public override string ToString() => $"typeof({TypeName})";
}
