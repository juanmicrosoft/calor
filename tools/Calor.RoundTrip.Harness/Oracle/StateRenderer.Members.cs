using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;

namespace Calor.RoundTrip.Harness.Oracle;

public static partial class StateRenderer
{
    private static readonly ConcurrentDictionary<(Type, bool), IReadOnlyList<(string Label, MemberInfo Member)>> StateCache = new();

    /// <summary>Public instance fields and readable non-indexer properties, ordered by label.</summary>
    private static void AppendMembers(Sink sb, object value, Type type, int depth)
    {
        sb.Append('{');
        var first = true;
        foreach (var (label, member) in PublicState(type))
        {
            if (sb.Full)
                break;
            if (!first)
                sb.Append(", ");
            first = false;
            sb.Append(label + "=");
            object? memberValue;
            try
            {
                memberValue = member is FieldInfo f ? f.GetValue(value) : ((PropertyInfo)member).GetValue(value);
            }
            catch (Exception ex)
            {
                sb.Append("!" + Unwrap(ex).GetType().FullName);
                continue;
            }
            Append(sb, memberValue, depth - 1);
        }
        sb.Append('}');
    }

    /// <summary>
    /// The public state members of a type (static ones when <paramref name="statics"/>), each with a label. The
    /// hierarchy is walked declaration by declaration, so a member hidden by a more derived one with the same name
    /// and signature (<c>new</c>) is kept, labelled <c>DeclaringType.Name</c>; an override collapses into the most
    /// derived declaration. Static state is the type's own.
    /// </summary>
    public static IReadOnlyList<(string Label, MemberInfo Member)> PublicState(Type type, bool statics = false) =>
        StateCache.GetOrAdd((type, statics), key => ComputeState(key.Item1, key.Item2));

    private static IReadOnlyList<(string Label, MemberInfo Member)> ComputeState(Type type, bool statics)
    {
        var flags = BindingFlags.Public | BindingFlags.DeclaredOnly | (statics ? BindingFlags.Static : BindingFlags.Instance);
        var result = new List<(string, MemberInfo)>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        var overridden = new HashSet<MethodInfo>();
        for (var t = type; t is not null; t = statics ? null : t.BaseType)
        {
            var level = new List<MemberInfo>();
            level.AddRange(t.GetFields(flags).Where(f => !f.FieldType.IsByRefLike && !f.FieldType.IsPointer));
            foreach (var p in t.GetProperties(flags))
            {
                if (p.GetIndexParameters().Length != 0 || p.GetMethod is not { IsPublic: true } getter || p.PropertyType.IsByRefLike || p.PropertyType.IsPointer)
                    continue;
                // An override of a property already taken from a more derived type is the same state.
                if (overridden.Contains(getter.GetBaseDefinition()))
                    continue;
                overridden.Add(getter.GetBaseDefinition());
                level.Add(p);
            }
            foreach (var m in level.OrderBy(m => m.Name, StringComparer.Ordinal))
            {
                var label = seenNames.Add(m.Name) ? m.Name : TypeName(t) + "." + m.Name;
                result.Add((label, m));
            }
        }
        return result.OrderBy(x => x.Item1, StringComparer.Ordinal).ToList();
    }

    /// <summary>Static state of a type: its own public static fields and properties.</summary>
    public static string RenderStatics(Type type, int depth)
    {
        var sink = new Sink();
        AppendStatics(sink, type, depth);
        return sink.Result();
    }

    /// <summary>The public static state of every given type, in name order: what a call may have changed anywhere in
    /// the project. Appended straight into the observation, so incompleteness is never lost.</summary>
    internal static void AppendProjectStatics(Sink sink, IEnumerable<KeyValuePair<string, Type>> types, int depth)
    {
        foreach (var (name, type) in types.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (type.IsGenericTypeDefinition || PublicState(type, statics: true).Count == 0)
                continue;
            sink.Append(name + "=");
            AppendStatics(sink, type, depth);
            sink.Append(';');
        }
    }

    internal static void AppendStatics(Sink sink, Type type, int depth)
    {
        sink.Append('{');
        var first = true;
        foreach (var (label, member) in PublicState(type, statics: true))
        {
            if (!first)
                sink.Append(", ");
            first = false;
            sink.Append(label + "=");
            try
            {
                var v = member is FieldInfo f ? f.GetValue(null) : ((PropertyInfo)member).GetValue(null);
                Append(sink, v, depth - 1);
            }
            catch (Exception ex)
            {
                sink.Append("!" + Unwrap(ex).GetType().FullName);
            }
        }
        sink.Append('}');
    }

    /// <summary>
    /// A delegate: for each target in its invocation list, the declaring type, the method (compiler-generated lambda
    /// names masked, because they differ between equivalent builds), the target object's state (captured variables of
    /// a closure are its public fields), and the results of calling it on up to 8 generated inputs. A delegate the
    /// generator cannot call makes the rendering incomplete.
    /// </summary>
    private static void AppendDelegate(Sink sb, Delegate del, int depth)
    {
        sb.Append("<delegate " + TypeName(del.GetType()));
        if (depth <= 0)
        {
            sb.Append('>');
            return;
        }
        foreach (var d in del.GetInvocationList())
        {
            var method = d.Method;
            sb.Append(" -> " + (method.DeclaringType is { } t ? TypeName(t) : "?") + "." + (method.Name.Contains('<') ? "<lambda>" : method.Name));
            if (d.Target is { } target)
            {
                sb.Append(" target=");
                Append(sb, target, depth - 1);
            }
        }
        var invoke = del.GetType().GetMethod("Invoke")!;
        var parameters = invoke.GetParameters();
        if (MemberOracleInputs.Unsupported(parameters) is not null || invoke.ReturnType.IsByRef || invoke.ReturnType.IsByRefLike)
        {
            sb.Append(" calls=<not callable with generated inputs>>");
            sb.MarkIncomplete();
            return;
        }
        sb.Append(" calls=[");
        foreach (var tuple in MemberOracleInputs.Generate("delegate", TypeName(del.GetType()), parameters).Take(8))
        {
            object?[] args;
            try
            {
                args = MemberOracleInputs.Materialize(tuple, parameters);
            }
            catch (Exception)
            {
                sb.MarkIncomplete();
                continue;
            }
            sb.Append(MemberOracleInputs.Render(tuple) + "=>");
            try
            {
                Append(sb, del.DynamicInvoke(args), depth - 1);
            }
            catch (Exception ex)
            {
                sb.Append("throw(" + Unwrap(ex).GetType().FullName + ")");
            }
            // What the call changed: its arguments (arrays, lists, ref/out) and its targets' state.
            // The argument array is a container, not a hop: its elements render one level below the delegate.
            sb.Append(" args=");
            Append(sb, args, depth);
            foreach (var d in del.GetInvocationList().Where(d => d.Target is not null))
            {
                sb.Append(" target=");
                Append(sb, d.Target, depth - 1);
            }
            sb.Append("; ");
        }
        sb.Append("]>");
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

    internal static string Invariant(double seconds) => seconds.ToString("0.00", CultureInfo.InvariantCulture);
}
