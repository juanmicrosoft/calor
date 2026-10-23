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
/// instance fields and properties, and their public fields and properties in turn. Every declaration in
/// the hierarchy is shown: a member hidden by a more derived one (<c>new</c>) is kept and labelled with
/// its declaring type; only true overrides collapse. A type that overrides <see cref="object.ToString"/>
/// also shows that string, which is how text-holding types such as <see cref="StringBuilder"/> expose
/// their content. A delegate shows its target's state and, when the generator can supply its parameters,
/// what it returns on generated inputs. Below the last level a complex value renders as its type name.
/// Scalars always render in full, tagged with their runtime type where two types share a spelling
/// (<c>1</c> is an int, <c>1L</c> a long). Collections show every element at one level deeper, plus their
/// own public properties such as Count and Capacity.</para>
///
/// <para>Nothing is cut: every character is hashed, and only the display prefix is stored. A rendering
/// that cannot be completed (more than <see cref="MaxItems"/> elements, more than <see cref="HardCap"/>
/// characters, or a delegate it cannot call) ends with <see cref="Incomplete"/>, and the oracle never
/// counts such an observation as agreement.</para>
/// </summary>
public static partial class StateRenderer
{
    public const int DefaultDepth = 2;
    public const int MaxItems = 1_000_000;
    public const int DisplayLength = 16_384;
    public const long HardCap = 256L * 1024 * 1024;
    public const string Incomplete = "…#INCOMPLETE";

    /// <summary>
    /// The rendering target: keeps the first <see cref="DisplayLength"/> characters and hashes every character.
    /// </summary>
    public sealed class Sink
    {
        private readonly StringBuilder _prefix = new();
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private readonly char[] _one = new char[1];

        public long Length { get; private set; }
        public bool IsIncomplete { get; private set; }
        public bool Full => Length >= HardCap;

        public Sink Append(string? s)
        {
            if (string.IsNullOrEmpty(s) || Full)
                return this;
            if (_prefix.Length < DisplayLength)
                _prefix.Append(s, 0, Math.Min(s.Length, DisplayLength - _prefix.Length));
            _hash.AppendData(MemoryMarshalBytes(s));
            Length += s.Length;
            if (Full)
                IsIncomplete = true;
            return this;
        }

        public Sink Append(char c)
        {
            _one[0] = c;
            return Append(new string(_one));
        }

        public void MarkIncomplete() => IsIncomplete = true;

        private static byte[] MemoryMarshalBytes(string s) => Encoding.UTF8.GetBytes(s);

        /// <summary>The stored observation: the text when short, else the prefix plus the hash of everything.</summary>
        public string Result()
        {
            var suffix = IsIncomplete ? Incomplete : "";
            return Length <= DisplayLength
                ? _prefix + suffix
                : _prefix + "…#sha256:" + Convert.ToHexStringLower(_hash.GetCurrentHash()) + suffix;
        }
    }

    public static string Render(object? value, int depth)
    {
        var sink = new Sink();
        Append(sink, value, depth);
        return sink.Result();
    }

    /// <summary>Whether a stored observation is incomplete (so it can never count as agreement).</summary>
    public static bool IsIncompleteObservation(string observation) => observation.Contains(Incomplete, StringComparison.Ordinal);

    internal static void Append(Sink sb, object? value, int depth)
    {
        if (sb.Full)
            return;
        switch (value)
        {
            case null: sb.Append("null"); return;
            case string s: AppendString(sb, s); return;
            case char c: sb.Append($"'\\u{(int)c:x4}'"); return;
            case bool b: sb.Append(b ? "true" : "false"); return;
            case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); return;
            case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture) + "L"); return;
            case short sh: sb.Append(sh.ToString(CultureInfo.InvariantCulture) + "s"); return;
            case byte by: sb.Append(by.ToString(CultureInfo.InvariantCulture) + "y"); return;
            case sbyte sby: sb.Append(sby.ToString(CultureInfo.InvariantCulture) + "sy"); return;
            case uint ui: sb.Append(ui.ToString(CultureInfo.InvariantCulture) + "u"); return;
            case ulong ul: sb.Append(ul.ToString(CultureInfo.InvariantCulture) + "uL"); return;
            case ushort us: sb.Append(us.ToString(CultureInfo.InvariantCulture) + "us"); return;
            case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture) + "d"); return;
            case float f: sb.Append(f.ToString("R", CultureInfo.InvariantCulture) + "f"); return;
            case decimal m: sb.Append(m.ToString(CultureInfo.InvariantCulture) + "m"); return;
            case DateTime dt: sb.Append(dt.ToString("O", CultureInfo.InvariantCulture) + "/" + dt.Kind); return;
            case DateTimeOffset dto: sb.Append(dto.ToString("O", CultureInfo.InvariantCulture)); return;
            case Enum e:
                // The underlying value is rendered in its own type, so unsigned 64-bit values never narrow.
                sb.Append(TypeName(e.GetType()) + "." + e + "(=" + Convert.ToString(
                    Convert.ChangeType(e, Enum.GetUnderlyingType(e.GetType()), CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) + ")");
                return;
            case Type t: sb.Append("typeof(" + TypeName(t) + ")"); return;
            case Delegate del: AppendDelegate(sb, del, depth); return;
            case Task task: AppendTask(sb, task, depth); return;
            case MemberInfo member:
                sb.Append("<" + member.MemberType + " " + (member.DeclaringType is { } declaring ? TypeName(declaring) + "::" : "") + member + ">");
                return;
            case Exception ex:
                // Exception.ToString carries stack traces and line numbers, which differ between equivalent builds.
                sb.Append(TypeName(ex.GetType()) + "(");
                AppendString(sb, ex.Message);
                if (depth > 0 && ex.InnerException is { } inner)
                {
                    sb.Append(", inner=");
                    Append(sb, inner, depth - 1);
                }
                sb.Append(')');
                return;
            case IFormattable formattable when value.GetType().IsPrimitive || value.GetType().Namespace == "System":
                sb.Append(TypeName(value.GetType()) + "(" + formattable.ToString(null, CultureInfo.InvariantCulture) + ")");
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
            sb.Append("<byref-like " + TypeName(type) + ">");
            return;
        }

        sb.Append(TypeName(type));
        if (depth <= 0)
            return;
        if (value is IEnumerable items)
            AppendItems(sb, items, depth);
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
                sb.Append("!" + Unwrap(ex).GetType().FullName);
            }
        }
    }

    private static void AppendItems(Sink sb, IEnumerable items, int depth)
    {
        sb.Append('[');
        try
        {
            var n = 0;
            foreach (var item in items)
            {
                if (n > 0)
                    sb.Append(", ");
                if (n++ == MaxItems || sb.Full)
                {
                    // An enumeration this long (or unbounded) cannot be compared in full.
                    sb.MarkIncomplete();
                    break;
                }
                Append(sb, item, depth - 1);
            }
        }
        catch (Exception ex)
        {
            sb.Append("!" + Unwrap(ex).GetType().FullName);
        }
        sb.Append(']');
    }

    private static void AppendString(Sink sb, string s)
    {
        sb.Append('"');
        var chunk = new StringBuilder(Math.Min(s.Length, 8192) + 16);
        foreach (var c in s)
        {
            chunk.Append(c is '"' or '\\' ? "\\" + c : c < 0x20 || c > 0x7e ? $"\\u{(int)c:x4}" : c.ToString());
            if (chunk.Length >= 8192)
            {
                sb.Append(chunk.ToString());
                chunk.Clear();
            }
        }
        sb.Append(chunk.ToString());
        sb.Append('"');
    }

    private static bool OverridesToString(Type type)
    {
        var declaring = type.GetMethod(nameof(ToString), BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes)?.DeclaringType;
        return declaring is not null && declaring != typeof(object) && declaring != typeof(ValueType) && declaring != typeof(Enum);
    }

    /// <summary>
    /// A task's outcome after it completes. The wait is unbounded here: the member's time budget bounds it, so a
    /// task that never completes makes the member time out instead of rendering an unfinished status. Task ids
    /// and scheduler details are process-specific and never rendered.
    /// </summary>
    private static void AppendTask(Sink sb, Task task, int depth)
    {
        try
        {
            task.Wait();
        }
        catch (Exception)
        {
            // Observed below through the task's status and exception.
        }
        sb.Append("Task(" + task.Status);
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
}
