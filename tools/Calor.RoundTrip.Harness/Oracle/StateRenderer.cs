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
            case Exception ex: AppendException(sb, ex, depth); return;
            case ValueTask valueTask:
                // Awaited like a Task, so the other parts observe the state after it completes.
                sb.Append("ValueTask:");
                AppendTask(sb, AsTask(() => valueTask.AsTask()), depth);
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
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            sb.Append("ValueTask:");
            AppendTask(sb, AsTask(() => (Task)type.GetMethod(nameof(ValueTask<int>.AsTask))!.Invoke(value, null)!), depth);
            return;
        }

        sb.Append(TypeName(type));
        if (depth <= 0)
            return;
        if (value is IEnumerable items)
            AppendItems(sb, items, depth);
        else if (AsyncItemType(type) is { } asyncItem)
            AppendAsyncItems(sb, value, asyncItem, depth);
        else if (value is IEnumerator enumerator && type.Name.Contains('<'))
            AppendEnumerator(sb, enumerator, depth);
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
                AppendThrown(sb, ex, 0, "!");
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
            AppendThrown(sb, ex, depth - 1, "!");
        }
        sb.Append(']');
    }

    /// <summary>
    /// The private working directory of the member being driven. Every run gets a fresh one with a random name, so a
    /// string that embeds it (an exception message naming a file, say) is rendered with the root replaced: two builds
    /// that name the same file in their own sandbox agree, and the rest of the string is still compared.
    /// </summary>
    internal static volatile string? SandboxRoot;

    /// <summary>
    /// Stands for the sandbox root in a rendered string. It is written after escaping, and escaping turns every
    /// non-ASCII character into <c>\uXXXX</c>, so a literal <c>§</c> (or the text <c>&lt;sandbox&gt;</c>) in a
    /// subject string can never render as this token: the real directory and a lookalike literal never alias.
    /// </summary>
    internal const string SandboxToken = "§sandbox§";

    private static void Escape(StringBuilder into, char c) =>
        into.Append(c is '"' or '\\' ? "\\" + c : c < 0x20 || c > 0x7e ? $"\\u{(int)c:x4}" : c.ToString());

    private static string Escaped(string s)
    {
        var sb = new StringBuilder(s.Length + 16);
        foreach (var c in s)
            Escape(sb, c);
        return sb.ToString();
    }

    private static void AppendString(Sink sb, string s)
    {
        sb.Append('"');
        if (SandboxRoot is { Length: > 0 } root && s.Contains(Path.GetFileName(root), StringComparison.Ordinal))
        {
            // macOS resolves /var to /private/var, so a path can come back in either spelling.
            var escaped = Escaped(s);
            if (root.StartsWith("/var/", StringComparison.Ordinal))
                escaped = escaped.Replace(Escaped("/private" + root), SandboxToken, StringComparison.Ordinal);
            sb.Append(escaped.Replace(Escaped(root), SandboxToken, StringComparison.Ordinal));
            sb.Append('"');
            return;
        }
        var chunk = new StringBuilder(Math.Min(s.Length, 8192) + 16);
        foreach (var c in s)
        {
            Escape(chunk, c);
            if (chunk.Length >= 8192)
            {
                sb.Append(chunk.ToString());
                chunk.Clear();
            }
        }
        sb.Append(chunk.ToString());
        sb.Append('"');
    }

    /// <summary>A string as the renderer writes it (quoted and escaped), for text the oracle itself puts in a part.</summary>
    internal static string Quoted(string s)
    {
        var sink = new Sink();
        AppendString(sink, s);
        return sink.Result();
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
            // The task's AggregateException is the task's own wrapper; every exception it holds is the subject's, and is
            // rendered whole (a subject AggregateException inside it keeps its message and data).
            sb.Append(", !");
            var inner = task.Exception!.InnerExceptions;
            for (var i = 0; i < inner.Count; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                Append(sb, inner[i], depth);
            }
        }
        else if (task.Status == TaskStatus.RanToCompletion && task.GetType().IsGenericType
            && task.GetType().GetProperty("Result") is { } result && result.PropertyType.FullName != "System.Threading.Tasks.VoidTaskResult")
        {
            sb.Append(", ");
            Append(sb, result.GetValue(task), depth);
        }
        sb.Append(')');
    }

    private static Task AsTask(Func<Task> convert)
    {
        try
        {
            return convert();
        }
        catch (Exception ex)
        {
            return Task.FromException(StripInvocation(ex));
        }
    }

    /// <summary>
    /// An exception: its type, message, <see cref="Exception.Data"/> entries, the public state its own type declares
    /// below <see cref="Exception"/> (<c>ParamName</c>, <c>ActualValue</c>, a subject exception's properties), and its
    /// inner exception. Stack traces, sources, target sites and HResults are left out (they are not what the code
    /// chose to report). Two exceptions of one type with different messages or data therefore render differently.
    /// </summary>
    internal static void AppendException(Sink sb, Exception ex, int depth)
    {
        var type = ex.GetType();
        sb.Append(TypeName(type) + "(");
        AppendString(sb, ex.Message);
        if (depth <= 0)
        {
            sb.Append(')');
            return;
        }
        try
        {
            if (ex.Data is { Count: > 0 } data)
            {
                sb.Append(", data={");
                var first = true;
                foreach (DictionaryEntry entry in data)
                {
                    if (!first)
                        sb.Append(", ");
                    first = false;
                    Append(sb, entry.Key, depth - 1);
                    sb.Append(": ");
                    Append(sb, entry.Value, depth - 1);
                }
                sb.Append('}');
            }
        }
        catch (Exception dataError)
        {
            sb.Append(", data=!" + Unwrap(dataError).GetType().FullName);
        }
        foreach (var (label, member) in PublicState(type))
        {
            if (member.DeclaringType is not { } declaring || declaring == typeof(Exception) || !typeof(Exception).IsAssignableFrom(declaring)
                || label is nameof(Exception.Message) or nameof(Exception.InnerException) or nameof(Exception.StackTrace))
                continue;
            sb.Append(", " + label + "=");
            try
            {
                Append(sb, member is FieldInfo f ? f.GetValue(ex) : ((PropertyInfo)member).GetValue(ex), depth - 1);
            }
            catch (Exception getterError)
            {
                AppendThrown(sb, getterError, 0, "!");
            }
        }
        if (ex.InnerException is { } inner)
        {
            sb.Append(", inner=");
            Append(sb, inner, depth - 1);
        }
        sb.Append(')');
    }

    /// <summary>
    /// A thrown exception, as <c>throw(...)</c>: the full exception rendering, so its message and data count. Only the
    /// one <see cref="TargetInvocationException"/> that the oracle's own reflection call adds is removed; whatever the
    /// subject threw, a <see cref="TargetInvocationException"/> or a single-inner <see cref="AggregateException"/>
    /// included, is rendered whole.
    /// </summary>
    internal static void AppendThrown(Sink sb, Exception thrown, int depth, string prefix = "throw")
    {
        sb.Append(prefix + "(");
        AppendException(sb, StripInvocation(thrown), depth);
        sb.Append(')');
    }

    /// <summary>Removes the single reflection wrapper (<c>MethodBase.Invoke</c>, <c>DynamicInvoke</c>, <c>PropertyInfo.GetValue</c>).</summary>
    internal static Exception StripInvocation(Exception ex) => ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;

    /// <summary>The element type of an <c>IAsyncEnumerable&lt;T&gt;</c> the value implements.</summary>
    private static Type? AsyncItemType(Type type) =>
        type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>))?.GetGenericArguments()[0];

    /// <summary>An async stream is drained like a collection (each <c>MoveNextAsync</c> awaited), so what it yields is compared.</summary>
    private static void AppendAsyncItems(Sink sb, object value, Type itemType, int depth)
    {
        sb.Append("async[");
        try
        {
            var enumerable = typeof(IAsyncEnumerable<>).MakeGenericType(itemType);
            var enumeratorType = typeof(IAsyncEnumerator<>).MakeGenericType(itemType);
            var enumerator = enumerable.GetMethod("GetAsyncEnumerator")!.Invoke(value, [CancellationToken.None])!;
            var moveNext = enumeratorType.GetMethod("MoveNextAsync")!;
            var current = enumeratorType.GetProperty("Current")!;
            try
            {
                for (var n = 0; ((ValueTask<bool>)moveNext.Invoke(enumerator, null)!).AsTask().GetAwaiter().GetResult(); n++)
                {
                    if (n > 0)
                        sb.Append(", ");
                    if (n == MaxItems || sb.Full)
                    {
                        sb.MarkIncomplete();
                        break;
                    }
                    Append(sb, current.GetValue(enumerator), depth - 1);
                }
            }
            finally
            {
                ((IAsyncDisposable)enumerator).DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        catch (Exception ex)
        {
            AppendThrown(sb, ex, depth - 1, "!");
        }
        sb.Append(']');
    }

    /// <summary>A compiler-generated iterator returned as an <see cref="IEnumerator"/> is drained, so what it yields is compared.</summary>
    private static void AppendEnumerator(Sink sb, IEnumerator enumerator, int depth)
    {
        sb.Append("enumerator[");
        try
        {
            for (var n = 0; enumerator.MoveNext(); n++)
            {
                if (n > 0)
                    sb.Append(", ");
                if (n == MaxItems || sb.Full)
                {
                    sb.MarkIncomplete();
                    break;
                }
                Append(sb, enumerator.Current, depth - 1);
            }
        }
        catch (Exception ex)
        {
            AppendThrown(sb, ex, depth - 1, "!");
        }
        sb.Append(']');
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
