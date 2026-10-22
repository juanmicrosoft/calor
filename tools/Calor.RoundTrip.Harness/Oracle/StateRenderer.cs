using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Calor.RoundTrip.Harness.Oracle;

/// <summary>
/// #1587: canonical, culture-invariant rendering of a value and its public state to a fixed depth.
/// Two values from different load contexts render equal when their observable public state is equal.
///
/// <para>Depth counts member hops from the rendered value. At depth 2 an object shows its public
/// instance fields and properties (hidden base members included, labelled with their declaring type),
/// and their public fields and properties in turn. A type that overrides <see cref="object.ToString"/>
/// also shows that string, which is how text-holding types such as <see cref="StringBuilder"/> expose
/// their content. Below the last level a complex value renders as its type name. Scalars always render
/// in full, tagged with their runtime type where two types share a spelling (<c>1</c> is an int,
/// <c>1L</c> a long). Collections show their elements (up to <see cref="MaxItems"/>, then an explicit
/// marker) at one level deeper, plus their own public properties such as Count and Capacity.</para>
///
/// <para>Nothing is silently cut: a rendering longer than <see cref="DisplayLength"/> is stored as its
/// first <see cref="DisplayLength"/> characters plus a hash of the whole rendering, and a rendering that
/// reaches <see cref="HardCap"/> ends with <see cref="Incomplete"/> so the observation says so.</para>
/// </summary>
public static class StateRenderer
{
    public const int DefaultDepth = 2;
    public const int MaxItems = 4096;
    public const int DisplayLength = 16_384;
    public const int HardCap = 4 * 1024 * 1024;
    public const string Incomplete = "…#INCOMPLETE";

    public static string Render(object? value, int depth)
    {
        var sb = new StringBuilder();
        Append(sb, value, depth);
        return Compact(sb);
    }

    /// <summary>Long renderings keep a readable prefix plus a hash of the complete text, so equality still covers every character.</summary>
    public static string Compact(StringBuilder sb)
    {
        if (sb.Length >= HardCap)
            sb.Append(Incomplete);
        if (sb.Length <= DisplayLength)
            return sb.ToString();
        var full = sb.ToString();
        return full[..DisplayLength] + "…#sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(full)))
            + (full.EndsWith(Incomplete, StringComparison.Ordinal) ? Incomplete : "");
    }

    private static void Append(StringBuilder sb, object? value, int depth)
    {
        if (sb.Length >= HardCap)
            return;
        switch (value)
        {
            case null:
                sb.Append("null");
                return;
            case string s:
                AppendString(sb, s);
                return;
            case char c:
                sb.Append($"'\\u{(int)c:x4}'");
                return;
            case bool b:
                sb.Append(b ? "true" : "false");
                return;
            case int i:
                sb.Append(i.ToString(CultureInfo.InvariantCulture));
                return;
            case long l:
                sb.Append(l.ToString(CultureInfo.InvariantCulture)).Append('L');
                return;
            case short sh:
                sb.Append(sh.ToString(CultureInfo.InvariantCulture)).Append('s');
                return;
            case byte by:
                sb.Append(by.ToString(CultureInfo.InvariantCulture)).Append('y');
                return;
            case sbyte sby:
                sb.Append(sby.ToString(CultureInfo.InvariantCulture)).Append("sy");
                return;
            case uint ui:
                sb.Append(ui.ToString(CultureInfo.InvariantCulture)).Append('u');
                return;
            case ulong ul:
                sb.Append(ul.ToString(CultureInfo.InvariantCulture)).Append("uL");
                return;
            case ushort us:
                sb.Append(us.ToString(CultureInfo.InvariantCulture)).Append("us");
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
                // The underlying value is rendered in its own type, so unsigned 64-bit values never narrow.
                sb.Append(TypeName(e.GetType())).Append('.').Append(e.ToString()).Append("(=")
                  .Append(Convert.ToString(Convert.ChangeType(e, Enum.GetUnderlyingType(e.GetType()), CultureInfo.InvariantCulture), CultureInfo.InvariantCulture))
                  .Append(')');
                return;
            case Type t:
                sb.Append("typeof(").Append(TypeName(t)).Append(')');
                return;
            case Delegate del:
                AppendDelegate(sb, del);
                return;
            case Task task:
                AppendTask(sb, task, depth);
                return;
            case MemberInfo member:
                sb.Append('<').Append(member.MemberType).Append(' ')
                  .Append(member.DeclaringType is { } declaring ? TypeName(declaring) + "::" : "").Append(member).Append('>');
                return;
            case Exception ex:
                // Exception.ToString carries stack traces and line numbers, which differ between equivalent builds.
                sb.Append(TypeName(ex.GetType())).Append('(');
                AppendString(sb, ex.Message);
                if (depth > 0 && ex.InnerException is { } inner)
                {
                    sb.Append(", inner=");
                    Append(sb, inner, depth - 1);
                }
                sb.Append(')');
                return;
            case IFormattable formattable when value.GetType().IsPrimitive || value.GetType().Namespace == "System":
                sb.Append(TypeName(value.GetType())).Append('(').Append(formattable.ToString(null, CultureInfo.InvariantCulture)).Append(')');
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
        if (value is IEnumerable items)
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
                        sb.Append("…more");
                        break;
                    }
                    Append(sb, item, depth - 1);
                    if (sb.Length >= HardCap)
                        break;
                }
            }
            catch (Exception ex)
            {
                sb.Append('!').Append(Unwrap(ex).GetType().FullName);
            }
            sb.Append(']');
        }
        AppendMembers(sb, value, type, depth);
        if (OverridesToString(type))
        {
            sb.Append(".ToString()=");
            try
            {
                AppendString(sb, value.ToString() ?? "null");
            }
            catch (Exception ex)
            {
                sb.Append('!').Append(Unwrap(ex).GetType().FullName);
            }
        }
    }

    private static void AppendString(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (var c in s)
        {
            if (sb.Length >= HardCap)
                break;
            sb.Append(c is '"' or '\\' ? "\\" + c : c < 0x20 || c > 0x7e ? $"\\u{(int)c:x4}" : c.ToString());
        }
        sb.Append('"');
    }

    private static bool OverridesToString(Type type)
    {
        var declaring = type.GetMethod(nameof(ToString), BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes)?.DeclaringType;
        return declaring is not null && declaring != typeof(object) && declaring != typeof(ValueType) && declaring != typeof(Enum);
    }

    /// <summary>A delegate's type and target methods. Compiler-generated lambda names are masked, because they are
    /// not part of behavior and differ between equivalent builds; the declaring type is kept.</summary>
    private static void AppendDelegate(StringBuilder sb, Delegate del)
    {
        sb.Append("<delegate ").Append(TypeName(del.GetType()));
        foreach (var d in del.GetInvocationList())
        {
            var method = d.Method;
            sb.Append(" -> ").Append(method.DeclaringType is { } t ? TypeName(t) : "?").Append('.')
              .Append(method.Name.Contains('<') ? "<lambda>" : method.Name);
        }
        sb.Append('>');
    }

    /// <summary>Public instance fields and readable non-indexer properties, ordered by label.</summary>
    private static void AppendMembers(StringBuilder sb, object value, Type type, int depth)
    {
        sb.Append('{');
        var first = true;
        foreach (var (label, member) in PublicState(type))
        {
            if (sb.Length >= HardCap)
                break;
            if (!first)
                sb.Append(", ");
            first = false;
            sb.Append(label).Append('=');
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

    /// <summary>The public state members of a type (static ones when <paramref name="statics"/>), each with a label: the
    /// member name, or <c>DeclaringType.Name</c> for a member that a more derived member hides.</summary>
    public static IReadOnlyList<(string Label, MemberInfo Member)> PublicState(Type type, bool statics = false)
    {
        var flags = BindingFlags.Public | (statics ? BindingFlags.Static | BindingFlags.DeclaredOnly : BindingFlags.Instance);
        var members = type.GetFields(flags).Where(f => !f.FieldType.IsByRefLike && !f.FieldType.IsPointer).Cast<MemberInfo>()
            .Concat(type.GetProperties(flags).Where(p => p.GetIndexParameters().Length == 0 && p.GetMethod is { IsPublic: true }
                && !p.PropertyType.IsByRefLike && !p.PropertyType.IsPointer))
            .ToList();
        // Reflection reports an overridden property once (most derived) but a hidden (`new`) one once per declaring type.
        return members.GroupBy(m => m.Name, StringComparer.Ordinal)
            .SelectMany(g => g.OrderByDescending(m => Depth(m.DeclaringType))
                .Select((m, i) => (Label: i == 0 ? m.Name : TypeName(m.DeclaringType!) + "." + m.Name, Member: m)))
            .OrderBy(x => x.Label, StringComparer.Ordinal)
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
        foreach (var (label, member) in PublicState(type, statics: true))
        {
            if (!first)
                sb.Append(", ");
            first = false;
            sb.Append(label).Append('=');
            try
            {
                var v = member is FieldInfo f ? f.GetValue(null) : ((PropertyInfo)member).GetValue(null);
                Append(sb, v, depth - 1);
            }
            catch (Exception ex)
            {
                sb.Append('!').Append(Unwrap(ex).GetType().FullName);
            }
        }
        return Compact(sb.Append('}'));
    }

    /// <summary>
    /// A task's outcome after it completes. The wait is unbounded here: the member's time budget bounds it, so a
    /// task that never completes makes the member time out instead of rendering an unfinished status. Task ids
    /// and scheduler details are process-specific and never rendered.
    /// </summary>
    private static void AppendTask(StringBuilder sb, Task task, int depth)
    {
        try
        {
            task.Wait();
        }
        catch (Exception)
        {
            // Observed below through the task's status and exception.
        }
        sb.Append("Task(").Append(task.Status);
        if (task.IsFaulted)
        {
            sb.Append(", !");
            Append(sb, Unwrap(task.Exception!), depth);
        }
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
