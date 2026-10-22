using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace Calor.RoundTrip.Harness.Oracle;

/// <summary>
/// #1587: canonical, culture-invariant rendering of a value and its public state to a fixed depth.
/// Two values from different load contexts render equal when their observable public state is equal.
///
/// <para>Depth counts member hops from the rendered value. At depth 2 an object shows its public
/// instance fields and properties, and their public fields and properties in turn. Below the last
/// level a complex value renders as its type name only. Scalars (numbers, strings, enums and other
/// <see cref="IFormattable"/> values) always render in full. Collections show their elements (up to
/// <see cref="MaxItems"/>) at one level deeper, plus their own public properties such as Count and
/// Capacity.</para>
/// </summary>
public static class StateRenderer
{
    public const int DefaultDepth = 2;
    public const int MaxItems = 32;
    public const int MaxLength = 16_384;

    public static string Render(object? value, int depth)
    {
        var sb = new StringBuilder();
        Append(sb, value, depth);
        return sb.Length <= MaxLength ? sb.ToString()
            : sb.ToString(0, MaxLength) + "…#" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..16];
    }

    private static void Append(StringBuilder sb, object? value, int depth)
    {
        if (sb.Length > MaxLength)
            return;
        switch (value)
        {
            case null:
                sb.Append("null");
                return;
            case string s:
                sb.Append('"');
                foreach (var c in s)
                    sb.Append(c is '"' or '\\' ? "\\" + c : c < 0x20 || c > 0x7e ? $"\\u{(int)c:x4}" : c.ToString());
                sb.Append('"');
                return;
            case char c:
                sb.Append($"'\\u{(int)c:x4}'");
                return;
            case bool b:
                sb.Append(b ? "true" : "false");
                return;
            case double d:
                sb.Append(d.ToString("R", CultureInfo.InvariantCulture)).Append('d');
                return;
            case float f:
                sb.Append(f.ToString("R", CultureInfo.InvariantCulture)).Append('f');
                return;
            case decimal m:
                sb.Append(m.ToString(CultureInfo.InvariantCulture)).Append('m');
                return;
            case DateTime dt:
                sb.Append(dt.ToString("O", CultureInfo.InvariantCulture)).Append('/').Append(dt.Kind);
                return;
            case DateTimeOffset dto:
                sb.Append(dto.ToString("O", CultureInfo.InvariantCulture));
                return;
            case Enum e:
                sb.Append(TypeName(e.GetType())).Append('.').Append(e.ToString()).Append("(=")
                  .Append(Convert.ToInt64(Convert.ChangeType(e, Enum.GetUnderlyingType(e.GetType()), CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)).Append(')');
                return;
            case Type t:
                sb.Append("typeof(").Append(TypeName(t)).Append(')');
                return;
            case Delegate del:
                // Lambda method names are compiler-generated and may differ between equivalent builds.
                sb.Append("<delegate ").Append(TypeName(del.GetType())).Append('>');
                return;
            case Task task:
                AppendTask(sb, task, depth);
                return;
            case IFormattable formattable when value.GetType().IsPrimitive || value.GetType().Namespace == "System":
                sb.Append(formattable.ToString(null, CultureInfo.InvariantCulture));
                return;
            case MemberInfo member:
                sb.Append('<').Append(member.MemberType).Append(' ').Append(member.Name).Append('>');
                return;
        }

        var type = value.GetType();
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
        {
            sb.Append('{');
            Append(sb, type.GetProperty("Key")!.GetValue(value), depth);
            sb.Append(": ");
            Append(sb, type.GetProperty("Value")!.GetValue(value), depth);
            sb.Append('}');
            return;
        }
        if (type.IsPointer || type.IsByRefLike)
        {
            sb.Append("<byref-like ").Append(TypeName(type)).Append('>');
            return;
        }

        sb.Append(TypeName(type));
        if (depth <= 0)
            return;
        if (value is IEnumerable items && type != typeof(string))
        {
            sb.Append('[');
            try
            {
                var n = 0;
                foreach (var item in items)
                {
                    if (n > 0)
                        sb.Append(", ");
                    if (n++ == MaxItems)
                    {
                        sb.Append('…');
                        break;
                    }
                    Append(sb, item, depth - 1);
                }
            }
            catch (Exception ex)
            {
                sb.Append('!').Append(Unwrap(ex).GetType().FullName);
            }
            sb.Append(']');
        }
        AppendMembers(sb, value, type, depth);
    }

    /// <summary>Public instance fields and readable non-indexer properties, ordered by name.</summary>
    private static void AppendMembers(StringBuilder sb, object value, Type type, int depth)
    {
        sb.Append('{');
        var first = true;
        foreach (var member in PublicState(type))
        {
            if (!first)
                sb.Append(", ");
            first = false;
            sb.Append(member.Name).Append('=');
            object? memberValue;
            try
            {
                memberValue = member is FieldInfo f ? f.GetValue(value) : ((PropertyInfo)member).GetValue(value);
            }
            catch (Exception ex)
            {
                sb.Append('!').Append(Unwrap(ex).GetType().FullName);
                continue;
            }
            Append(sb, memberValue, depth - 1);
        }
        sb.Append('}');
    }

    /// <summary>The public instance state members of a type, in ordinal name order (static ones when <paramref name="statics"/>).</summary>
    public static IReadOnlyList<MemberInfo> PublicState(Type type, bool statics = false)
    {
        var flags = BindingFlags.Public | (statics ? BindingFlags.Static | BindingFlags.DeclaredOnly : BindingFlags.Instance);
        return type.GetFields(flags).Where(f => !f.FieldType.IsByRefLike && !f.FieldType.IsPointer).Cast<MemberInfo>()
            .Concat(type.GetProperties(flags).Where(p => p.GetIndexParameters().Length == 0 && p.GetMethod is { IsPublic: true }
                && !p.PropertyType.IsByRefLike && !p.PropertyType.IsPointer))
            // A property hidden with `new` appears once per declaring type; keep the most derived one.
            .GroupBy(m => m.Name, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(m => Depth(m.DeclaringType)).First())
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static int Depth(Type? t)
    {
        var d = 0;
        for (; t is not null; t = t.BaseType)
            d++;
        return d;
    }

    /// <summary>Static state of a type: its own public static fields and properties.</summary>
    public static string RenderStatics(Type type, int depth)
    {
        var sb = new StringBuilder("{");
        var first = true;
        foreach (var member in PublicState(type, statics: true))
        {
            if (!first)
                sb.Append(", ");
            first = false;
            sb.Append(member.Name).Append('=');
            try
            {
                var v = member is FieldInfo f ? f.GetValue(null) : ((PropertyInfo)member).GetValue(null);
                sb.Append(Render(v, depth - 1));
            }
            catch (Exception ex)
            {
                sb.Append('!').Append(Unwrap(ex).GetType().FullName);
            }
        }
        return sb.Append('}').ToString();
    }

    /// <summary>A task's outcome. Task ids and scheduler details are process-specific and never rendered.</summary>
    private static void AppendTask(StringBuilder sb, Task task, int depth)
    {
        try
        {
            task.Wait(TimeSpan.FromSeconds(1));
        }
        catch (Exception)
        {
            // Observed below through the task's status and exception.
        }
        sb.Append("Task(").Append(task.Status);
        if (task.IsFaulted)
            sb.Append(", !").Append(Unwrap(task.Exception!).GetType().FullName);
        else if (task.Status == TaskStatus.RanToCompletion && task.GetType().IsGenericType
            && task.GetType().GetProperty("Result") is { } result && result.PropertyType.FullName != "System.Threading.Tasks.VoidTaskResult")
        {
            sb.Append(", ");
            Append(sb, result.GetValue(task), depth);
        }
        sb.Append(')');
    }

    public static Exception Unwrap(Exception ex)
    {
        while (true)
        {
            switch (ex)
            {
                case TargetInvocationException { InnerException: { } inner }:
                    ex = inner;
                    continue;
                case AggregateException { InnerExceptions.Count: 1 } agg:
                    ex = agg.InnerExceptions[0];
                    continue;
                default:
                    return ex;
            }
        }
    }

    /// <summary>A load-context-independent type name: namespace, name and generic arguments, no assembly qualifiers.
    /// Compiler-generated types render as one placeholder, because their names are not part of a type's behavior.</summary>
    public static string TypeName(Type t)
    {
        if (t.IsByRef)
            return "ref " + TypeName(t.GetElementType()!);
        if (t.IsArray)
            return TypeName(t.GetElementType()!) + "[" + new string(',', t.GetArrayRank() - 1) + "]";
        if (t.IsGenericParameter)
            return t.Name;
        if (t.Name.Contains('<'))
            return "<compiler-generated>";
        var simple = t.Name.Split('`')[0];
        var name = t.IsNested ? TypeName(t.DeclaringType!) + "+" + simple : (t.Namespace is { Length: > 0 } ns ? ns + "." : "") + simple;
        if (!t.IsGenericType)
            return name;
        var args = t.GetGenericArguments();
        // Nested types repeat their parents' generic arguments; show only the ones this type adds.
        var inherited = t.IsNested ? t.DeclaringType!.GetGenericArguments().Length : 0;
        var own = args.Skip(inherited).ToArray();
        return own.Length == 0 ? name : name + "<" + string.Join(",", own.Select(TypeName)) + ">";
    }
}
