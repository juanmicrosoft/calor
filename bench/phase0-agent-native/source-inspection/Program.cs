using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;

// This program only parses. It never binds, compiles, or executes task code.
using var request = JsonDocument.Parse(Console.In.ReadToEnd());
var results = new SortedDictionary<string, object?>(StringComparer.Ordinal);
foreach (var source in request.RootElement.GetProperty("sources").EnumerateArray())
{
    var text = source.GetProperty("text").GetString()!;
    var ranges = source.GetProperty("editableRanges").EnumerateArray()
        .Select(range => (Start: range[0].GetInt32(), End: range[1].GetInt32())).ToArray();
    var diagnostics = new DiagnosticBag();
    try
    {
        var tokens = new Lexer(text, diagnostics).TokenizeAllForParser();
        var module = new Parser(tokens, diagnostics).Parse();
        var calls = new List<string>();
        var interpolations = tokens.Where(token => token.Kind == TokenKind.StrLiteral)
            .Select(token => token.Span).ToHashSet();
        Inspect.Calls(module, ranges, null, interpolations, calls,
            new HashSet<object>(ReferenceEqualityComparer.Instance));
        results[source.GetProperty("name").GetString()!] = new
        {
            parseOk = !diagnostics.HasErrors,
            publicApi = diagnostics.HasErrors ? null : Inspect.Surface(module),
            calls = diagnostics.HasErrors ? [] : calls.Distinct().Order(StringComparer.Ordinal).ToArray()
        };
    }
    catch (Exception exception) when (exception is not OutOfMemoryException)
    {
        results[source.GetProperty("name").GetString()!] = new
        {
            parseOk = false, error = exception.GetType().Name, calls = Array.Empty<string>()
        };
    }
}
Console.WriteLine(JsonSerializer.Serialize(new
{
    schemaVersion = 1,
    compilerSha256 = Convert.ToHexStringLower(SHA256.HashData(
        File.ReadAllBytes(typeof(Parser).Assembly.Location))),
    sources = results
}));

internal static class Inspect
{
    private static readonly HashSet<string> NonSurface = new(StringComparer.Ordinal)
    {
        "Body", "Initializer", "Initializers", "Id", "DocComment", "NamespaceScopeId",
        "FullyQualifiedSymbolIdentity", "MemberOrder", "Items"
    };
    private static readonly HashSet<string> Unordered = new(StringComparer.Ordinal)
    {
        "Functions", "Classes", "Interfaces", "Methods", "Fields", "Properties", "Indexers",
        "Constructors", "Events", "OperatorOverloads", "NestedClasses", "NestedInterfaces",
        "Enums", "Delegates", "Usings"
    };

    private static IEnumerable<PropertyInfo> Properties(object value) =>
        value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0);

    public static object? Surface(object? value, int depth = 0)
    {
        if (depth > 128)
            throw new InvalidOperationException("Public declaration nesting exceeds the inspection limit.");
        if (value is null || value is string || value.GetType().IsPrimitive || value is decimal)
            return value;
        if (value is Enum)
            return value.ToString();
        if (value is System.Numerics.BigInteger integer)
            return integer.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (value is AttributeCollection attributes)
            return attributes.All().Where(item => item.Key is not ("id" or "_pos0"))
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        if (value is IDictionary dictionary)
        {
            var entries = new SortedDictionary<string, object?>(StringComparer.Ordinal);
            foreach (DictionaryEntry entry in dictionary)
                entries[entry.Key.ToString()!] = Surface(entry.Value, depth + 1);
            return entries;
        }
        if (value is IEnumerable collection)
            return collection.Cast<object?>().Select(item => Surface(item, depth + 1))
                .Where(item => item is not null).ToArray();
        if (value is AstNode && Properties(value).FirstOrDefault(property => property.Name == "Visibility")
                ?.GetValue(value)?.ToString() is "Private" or "Internal")
            return null;
        var result = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["nodeKind"] = value.GetType().Name
        };
        foreach (var property in Properties(value))
        {
            if (NonSurface.Contains(property.Name) || property.Name.EndsWith("Span", StringComparison.Ordinal))
                continue;
            var child = Surface(property.GetValue(value), depth + 1);
            if (Unordered.Contains(property.Name) && child is object?[] items)
                child = items.OrderBy(item => JsonSerializer.Serialize(item), StringComparer.Ordinal).ToArray();
            result[property.Name] = child;
        }
        return result;
    }

    public static void Calls(object? value, (int Start, int End)[] ranges, bool? interpolationEditable,
        HashSet<TextSpan> interpolations, List<string> calls, HashSet<object> visited)
    {
        if (value is null || value is string || value.GetType().IsValueType || !visited.Add(value))
            return;
        if (value is IEnumerable collection)
        {
            foreach (var child in collection)
                Calls(child, ranges, interpolationEditable, interpolations, calls, visited);
            return;
        }
        if (value is not AstNode node)
            return;
        if (node is InterpolatedStringNode && interpolations.Contains(node.Span))
        {
            var contained = ranges.Any(range => range.Start <= node.Span.Start && node.Span.End <= range.End);
            var overlaps = ranges.Any(range => range.Start < node.Span.End && node.Span.Start < range.End);
            if (overlaps && !contained)
                throw new InvalidOperationException("Interpolation crosses the editable source boundary.");
            // Inline interpolation expressions have local parser spans. Only their
            // enclosing physical string token may determine source ownership.
            interpolationEditable = contained;
        }
        var call = node switch
        {
            CallStatementNode statement => (statement.Target, statement.CalleeSpan),
            CallExpressionNode expression => (expression.Target, expression.CalleeSpan),
            _ => ((string?)null, TextSpan.Empty)
        };
        var editable = interpolationEditable ?? ranges.Any(range =>
            range.Start <= call.Item2.Start && call.Item2.End <= range.End && call.Item2.Length > 0);
        if (editable && call.Item1 is not null)
            calls.Add("§C{" + call.Item1.Trim() + "}");
        foreach (var property in Properties(node))
            Calls(property.GetValue(node), ranges, interpolationEditable, interpolations, calls, visited);
    }
}
