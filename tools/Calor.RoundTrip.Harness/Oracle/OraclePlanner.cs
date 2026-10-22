using System.Globalization;
using System.Reflection;

namespace Calor.RoundTrip.Harness.Oracle;

/// <summary>One member the oracle drives (or reports as skipped), keyed by rendered type name and member key.</summary>
internal sealed record PlannedMember(string Type, string Member, string Kind, IReadOnlyList<object?[]> Inputs, string? SkipReason)
{
    /// <summary>For instance members: the constructor (plan key) and arguments that build each fresh receiver;
    /// null means the parameterless constructor (or a struct's default value).</summary>
    public string? ReceiverCtor { get; init; }
    public object?[] ReceiverArgs { get; init; } = [];

    /// <summary>For the <c>&lt;hierarchy&gt;</c> member: the type's kind, visibility, base chain and interfaces.</summary>
    public string? Hierarchy { get; init; }

    public string Key => Type + "::" + Member;
}

/// <summary>
/// #1587: what the oracle drives in a build. Every type in scope counts, whatever its accessibility (an
/// internal type's public members are reachable through the public types that use it), except
/// compiler-generated types and delegate types. Per type:
/// <list type="bullet">
/// <item><c>&lt;hierarchy&gt;</c>: kind, visibility, base chain and interfaces, compared as surface;</item>
/// <item><c>&lt;statics&gt;</c>: public static state after type initialization; <c>&lt;enum-values&gt;</c> for enums;</item>
/// <item>every public constructor (construction state), and a struct's default value;</item>
/// <item>every public method, property setter and indexer, declared on the type or inherited from a type in the
/// same assembly (instance members only), driven on a fresh receiver per input. Static methods of interfaces are
/// included; parameterless getters are covered by state, event accessors take delegates.</item>
/// </list>
/// Generic types and methods are closed over the first of <see cref="ClosingArguments"/> that meets every constraint.
/// </summary>
internal static class OraclePlanner
{
    public static readonly Type[] ClosingArguments = [typeof(object), typeof(string), typeof(int)];

    /// <summary>Every type of the assembly the oracle can use, by rendered name (generic definitions closed).</summary>
    public static Dictionary<string, Type> UsableTypes(Assembly assembly)
    {
        var types = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var t in AllTypes(assembly))
            if (!IsCompilerGenerated(t) && (t.IsGenericTypeDefinition ? CloseType(t) : t) is { } usable)
                types.TryAdd(StateRenderer.TypeName(usable), usable);
        return types;
    }

    private static IEnumerable<Type> AllTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            throw new InvalidOperationException("types failed to load: " + string.Join("; ",
                ex.LoaderExceptions.Where(e => e is not null).Select(e => e!.Message).Distinct().Take(3)), ex);
        }
    }

    private static bool IsCompilerGenerated(Type t)
    {
        for (var c = t; c is not null; c = c.DeclaringType)
            if (c.Name.Contains('<'))
                return true;
        return false;
    }

    public static List<PlannedMember> Plan(Assembly assembly, IReadOnlyCollection<string> typeScope, OracleOptions options, bool chooseReceivers)
    {
        var plan = new List<PlannedMember>();
        foreach (var name in typeScope.Order(StringComparer.Ordinal))
        {
            var type = assembly.GetType(name, throwOnError: false);
            if (type is null || IsCompilerGenerated(type) || typeof(Delegate).IsAssignableFrom(type))
                continue;
            if (type.IsGenericTypeDefinition)
            {
                if (CloseType(type) is not { } closed)
                {
                    plan.Add(new PlannedMember(StateRenderer.TypeName(type), "<type>", "type", [], "open generic type (no closing argument meets its constraints)"));
                    continue;
                }
                type = closed;
            }
            var typeName = StateRenderer.TypeName(type);
            plan.Add(new PlannedMember(typeName, "<hierarchy>", "hierarchy", [], null) { Hierarchy = Hierarchy(type) });
            if (type.IsEnum)
            {
                plan.Add(new PlannedMember(typeName, "<enum-values>", "enum-values", [[]], null));
                continue;
            }
            plan.Add(new PlannedMember(typeName, "<statics>", "statics", [[]], null));

            if (!type.IsInterface && (!type.IsAbstract || type.IsValueType))
            {
                foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).OrderBy(Signature, StringComparer.Ordinal))
                    plan.Add(Planned(typeName, "ctor" + Signature(ctor), "construct", ctor, ctor.GetParameters(), options));
                if (type.IsValueType && type.GetConstructor(Type.EmptyTypes) is null)
                    plan.Add(new PlannedMember(typeName, "ctor()", "construct", [[]], null));
            }

            // Receivers are chosen once, in the original build's plan; the converted build uses the same choice.
            var (receiverKey, receiverArgs, receiverProblem) = type.IsValueType ? (null, [], null)
                : type.IsInterface || type.IsAbstract ? (null, Array.Empty<object?>(), type.IsInterface ? "instance member of an interface" : "instance member of an abstract type")
                : chooseReceivers ? ChooseReceiver(type, typeName, options)
                : (null, [], null);

            foreach (var declared in CallableMethods(type))
            {
                var method = declared.IsGenericMethodDefinition ? CloseMethod(declared) : declared;
                var kind = declared.IsStatic ? "static" : "instance";
                if (method is null)
                {
                    plan.Add(new PlannedMember(typeName, MethodKey(type, declared), kind, [], "generic method (no closing argument meets its constraints)"));
                    continue;
                }
                var key = MethodKey(type, method);
                if (!method.IsStatic && receiverProblem is not null)
                    plan.Add(new PlannedMember(typeName, key, kind, [], receiverProblem));
                else
                    plan.Add(Planned(typeName, key, kind, method, method.GetParameters(), options) with
                    {
                        ReceiverCtor = method.IsStatic ? null : receiverKey,
                        ReceiverArgs = method.IsStatic ? [] : receiverArgs,
                    });
            }
        }
        return plan;
    }

    /// <summary>Public methods the oracle calls: declared on the type, or instance methods inherited from a type in
    /// the same assembly. Interfaces contribute their non-abstract static methods only.</summary>
    public static IEnumerable<MethodInfo> CallableMethods(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance;
        return type.GetMethods(flags)
            .Where(m => !m.Name.Contains('<') && !m.IsAbstract
                && (m.DeclaringType == type || (!m.IsStatic && m.DeclaringType?.Assembly == type.Assembly))
                && (!type.IsInterface || m.IsStatic)
                && !(m.IsSpecialName && ((m.Name.StartsWith("get_", StringComparison.Ordinal) && m.GetParameters().Length == 0)
                    || m.Name.StartsWith("add_", StringComparison.Ordinal) || m.Name.StartsWith("remove_", StringComparison.Ordinal))))
            .OrderBy(m => MethodKey(type, m), StringComparer.Ordinal);
    }

    /// <summary>
    /// The receiver constructor: the parameterless one when there is one, else each public constructor the generator
    /// can supply, fewest parameters first, tried with up to 16 generated tuples (non-null ones first) until one
    /// constructs. Each attempt has a 2 s budget.
    /// </summary>
    private static (string? Key, object?[] Args, string? Problem) ChooseReceiver(Type type, string typeName, OracleOptions options)
    {
        var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderBy(c => c.GetParameters().Length).ThenBy(Signature, StringComparer.Ordinal).ToList();
        if (ctors.Count == 0)
            return (null, [], "no public constructor for a receiver");
        var supported = ctors.Where(c => MemberOracleInputs.Unsupported(c.GetParameters()) is null).ToList();
        if (supported.Count == 0)
            return (null, [], "no public constructor the generator can supply, for a receiver");
        foreach (var ctor in supported)
        {
            var key = "ctor" + Signature(ctor);
            var parameters = ctor.GetParameters();
            var candidates = parameters.Length == 0 ? [[]] : MemberOracleInputs.ReceiverCandidates(options.SeedKey, typeName + "::receiver:" + key, parameters);
            foreach (var args in candidates)
            {
                Exception? failure = null;
                var thread = new Thread(() =>
                {
                    try
                    {
                        ctor.Invoke(MemberOracleInputs.Materialize(args, parameters));
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                    }
                }, 16 * 1024 * 1024) { IsBackground = true, CurrentCulture = CultureInfo.InvariantCulture, CurrentUICulture = CultureInfo.InvariantCulture };
                thread.Start();
                if (!thread.Join(TimeSpan.FromSeconds(2)))
                    return (null, [], "receiver construction timed out");
                if (failure is null)
                    return (parameters.Length == 0 ? null : key, args, null);
            }
        }
        return (null, [], "no generated input constructs a receiver");
    }

    private static PlannedMember Planned(string typeName, string key, string kind, MethodBase method, ParameterInfo[] parameters, OracleOptions options)
    {
        if (MemberOracleInputs.Unsupported(parameters) is { } reason)
            return new PlannedMember(typeName, key, kind, [], reason);
        if (method is MethodInfo { ReturnType: { IsByRef: true } or { IsByRefLike: true } or { IsPointer: true } })
            return new PlannedMember(typeName, key, kind, [], "by-ref or ref-struct return");
        return new PlannedMember(typeName, key, kind, MemberOracleInputs.Generate(options.SeedKey, typeName + "::" + key, parameters), null);
    }

    /// <summary>Kind, visibility and inheritance: a converted type that changes any of them is a surface difference.</summary>
    private static string Hierarchy(Type type)
    {
        const TypeAttributes shape = TypeAttributes.VisibilityMask | TypeAttributes.ClassSemanticsMask | TypeAttributes.Abstract | TypeAttributes.Sealed;
        var chain = new List<string>();
        for (var b = type.BaseType; b is not null; b = b.BaseType)
            chain.Add(StateRenderer.TypeName(b));
        var interfaces = type.GetInterfaces().Select(StateRenderer.TypeName).Order(StringComparer.Ordinal);
        return $"{type.Attributes & shape} valueType={type.IsValueType} base=[{string.Join(" < ", chain)}] interfaces=[{string.Join(", ", interfaces)}]";
    }

    public static Type? CloseType(Type open)
    {
        foreach (var argument in ClosingArguments)
        {
            try
            {
                return open.MakeGenericType(Enumerable.Repeat(argument, open.GetGenericArguments().Length).ToArray());
            }
            catch (ArgumentException)
            {
                // A constraint rejects this argument; try the next.
            }
        }
        return null;
    }

    public static MethodInfo? CloseMethod(MethodInfo open)
    {
        foreach (var argument in ClosingArguments)
        {
            try
            {
                return open.MakeGenericMethod(Enumerable.Repeat(argument, open.GetGenericArguments().Length).ToArray());
            }
            catch (ArgumentException)
            {
                // A constraint rejects this argument; try the next.
            }
        }
        return null;
    }

    /// <summary>A method's plan key. An inherited method is prefixed with its declaring type.</summary>
    public static string MethodKey(Type owner, MethodInfo m) =>
        (m.DeclaringType is { } d && d != owner ? StateRenderer.TypeName(d) + "." : "")
        + (m.IsStatic ? "static " : "") + m.Name
        + (m.IsGenericMethod ? "<" + string.Join(",", m.GetGenericArguments().Select(StateRenderer.TypeName)) + ">" : "")
        + Signature(m) + "->" + StateRenderer.TypeName(m.ReturnType);

    public static string Signature(MethodBase m) => "(" + string.Join(",", m.GetParameters().Select(p =>
        (p.IsOut ? "out " : "") + StateRenderer.TypeName(p.ParameterType))) + ")";

    /// <summary>Finds the same member in another load context by its plan key.</summary>
    public static MemberInfo? Rebind(Type type, PlannedMember member) => member.Kind switch
    {
        "statics" or "enum-values" => type,
        "construct" => type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(c => "ctor" + Signature(c) == member.Member),
        _ => CallableMethods(type).Select(m => m.IsGenericMethodDefinition ? CloseMethod(m) : m)
            .FirstOrDefault(m => m is not null && MethodKey(type, m) == member.Member),
    };
}
