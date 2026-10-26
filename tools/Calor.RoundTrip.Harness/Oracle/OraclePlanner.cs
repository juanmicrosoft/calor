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

    /// <summary>#1667: for a suffix segment, the member it continues (what <see cref="OraclePlanner.Rebind"/> looks up).</summary>
    public string? Origin { get; init; }

    /// <summary>#1667: the index of <see cref="Inputs"/>[0] in the origin member's input list (0 except for a suffix segment).</summary>
    public int Offset { get; init; }

    /// <summary>#1667: 0 for the member itself, n for its n-th suffix segment.</summary>
    public int Segment { get; init; }

    public string Key => Type + "::" + Member;

    /// <summary>The member as the subject declares it (a suffix segment's origin).</summary>
    public string Callable => Origin ?? Member;
}

/// <summary>
/// #1587: what the oracle drives in a build. Every type in scope counts, whatever its accessibility, except
/// compiler-generated and delegate types. The scope <see cref="AllTypes"/> means every type of the emitted
/// assembly, generated ones included. Per type:
/// <list type="bullet">
/// <item><c>&lt;hierarchy&gt;</c>: kind, visibility, base chain and interfaces, compared as surface;</item>
/// <item><c>&lt;statics&gt;</c>: public static state after type initialization; <c>&lt;enum-values&gt;</c> for enums;</item>
/// <item>every public constructor (construction state), and a struct's default value;</item>
/// <item>every public method, property setter and indexer the type declares, plus public instance methods of its base
/// types in the same assembly (hidden ones included, overrides once), driven on a fresh receiver per input;</item>
/// <item>every interface method (from any assembly) the type implements explicitly, and every default implementation
/// of a subject interface, called through the interface on the receiver;</item>
/// <item>for interfaces: every member, as surface (driven through implementing types), and static methods.</item>
/// </list>
/// Generic types and methods are closed over the first of <see cref="ClosingArguments"/> that meets every constraint.
/// </summary>
internal static class OraclePlanner
{
    /// <summary>Scope marker: every type of the emitted assembly.</summary>
    public const string AllTypes = "*";

    public static readonly Type[] ClosingArguments = [typeof(object), typeof(string), typeof(int)];

    /// <summary>Every type of the assembly the oracle can use, by rendered name (generic definitions closed).</summary>
    public static Dictionary<string, Type> UsableTypes(Assembly assembly)
    {
        var types = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var t in LoadTypes(assembly))
            if (!IsCompilerGenerated(t))
                foreach (var usable in t.IsGenericTypeDefinition ? CloseTypes(t) : [t])
                    types.TryAdd(StateRenderer.TypeName(usable), usable);
        return types;
    }

    private static Type[] LoadTypes(Assembly assembly)
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
        var types = typeScope.Contains(AllTypes)
            ? LoadTypes(assembly).Concat(typeScope.Where(n => n != AllTypes).Select(n => assembly.GetType(n, throwOnError: false)))
            : typeScope.Select(n => assembly.GetType(n, throwOnError: false));
        // A type is planned once per rendered name: two plan entries under one name would run the same type twice and
        // leave the other unexercised, so a collision is reported instead of silently merged.
        var planned = new HashSet<string>(StringComparer.Ordinal);
        void PlanOnce(Type t)
        {
            var name = StateRenderer.TypeName(t);
            if (planned.Add(name))
                PlanType(plan, t, options, chooseReceivers);
            else
                plan.Add(new PlannedMember(name, "<type> " + t.FullName, "type", [], "type name shared with another type (not driven twice)"));
        }
        foreach (var candidate in types.Where(t => t is not null).Distinct().OrderBy(t => t!.FullName, StringComparer.Ordinal))
        {
            var type = candidate!;
            if (IsCompilerGenerated(type))
                continue;
            if (typeof(Delegate).IsAssignableFrom(type))
            {
                // #1667: a delegate type has no behavior of its own, but its signature is surface: a changed Invoke
                // signature is a difference, not a silently dropped type.
                if (planned.Add(StateRenderer.TypeName(type)))
                {
                    plan.Add(new PlannedMember(StateRenderer.TypeName(type), "<hierarchy>", "hierarchy", [], null) { Hierarchy = Hierarchy(type) });
                    if (type.GetMethod("Invoke") is { } invoke)
                        plan.Add(new PlannedMember(StateRenderer.TypeName(type), MethodKey(type, invoke), "delegate-signature", [], "delegate type (surface only)"));
                }
                continue;
            }
            if (type.IsGenericTypeDefinition)
            {
                var closings = CloseTypes(type);
                if (closings.Count == 0)
                    plan.Add(new PlannedMember(StateRenderer.TypeName(type), "<type>", "type", [], "open generic type (no closing argument meets its constraints)"));
                foreach (var closed in closings)
                    PlanOnce(closed);
                continue;
            }
            PlanOnce(type);
        }
        return plan;
    }

    private static void PlanType(List<PlannedMember> plan, Type type, OracleOptions options, bool chooseReceivers)
    {
        var typeName = StateRenderer.TypeName(type);
        plan.Add(new PlannedMember(typeName, "<hierarchy>", "hierarchy", [], null) { Hierarchy = Hierarchy(type) });
        if (type.IsEnum)
        {
            plan.Add(new PlannedMember(typeName, "<enum-values>", "enum-values", [[]], null));
            return;
        }
        plan.Add(new PlannedMember(typeName, "<statics>", "statics", [[]], null));
        if (type.IsInterface)
        {
            // Every interface member is surface; instance members are driven through the types that implement them.
            foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                         .OrderBy(m => MethodKey(type, m), StringComparer.Ordinal))
                plan.Add(new PlannedMember(typeName, MethodKey(type, m), "interface-member", [], "interface member (surface; driven through implementing types)"));
        }

        if (type is { IsInterface: false, IsAbstract: true, IsSealed: false })
        {
            // #1667: abstract declarations have no body to drive, but their signatures are surface: changing one is a
            // difference, and the implementations are driven through the concrete types.
            foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => m.IsAbstract)
                         .OrderBy(m => MethodKey(type, m), StringComparer.Ordinal))
                plan.Add(new PlannedMember(typeName, "abstract " + MethodKey(type, m), "abstract-member", [], "abstract member (surface; driven through implementing types)"));
        }

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
            : options.ReceiverBans.Contains(typeName) ? (null, [], "receiver construction timed out on an earlier attempt")
            : chooseReceivers ? ChooseReceiver(type, typeName, options)
            : (null, [], null);

        foreach (var (key, method, original) in Callables(type))
        {
            var kind = method?.IsStatic ?? original.IsStatic ? "static" : "instance";
            if (method is null)
                plan.Add(new PlannedMember(typeName, key, kind, [], "generic method (no closing argument meets its constraints)"));
            else if (!method.IsStatic && receiverProblem is not null)
                plan.Add(new PlannedMember(typeName, key, kind, [], receiverProblem));
            else
                plan.Add(Planned(typeName, key, kind, method, method.GetParameters(), options) with
                {
                    ReceiverCtor = method.IsStatic ? null : receiverKey,
                    ReceiverArgs = method.IsStatic ? [] : receiverArgs,
                });
        }
    }

    /// <summary>
    /// The methods the oracle calls on a type, each with its plan key: declared public methods; public instance methods
    /// of base types in the same assembly, hidden ones included and overrides once (prefixed with the declaring type);
    /// and interface methods (same assembly) the type implements explicitly or by default implementation, called
    /// through the interface (prefixed <c>via</c>). Generic methods are closed; a method that cannot be closed comes
    /// back with a null <c>Method</c>.
    /// </summary>
    public static IEnumerable<(string Key, MethodInfo? Method, MethodInfo Original)> Callables(Type type)
    {
        var result = new List<(string, MethodInfo?, MethodInfo)>();
        var seen = new HashSet<MethodInfo>();
        for (var t = type; t is not null && (t == type || t.Assembly == type.Assembly); t = t.BaseType)
        {
            var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly | (t == type ? BindingFlags.Static : 0);
            foreach (var m in t.GetMethods(flags))
            {
                if (m.Name.Contains('<') || m.IsAbstract || (type.IsInterface && !m.IsStatic) || IsStateAccessor(m))
                    continue;
                // An override of a method already taken from a more derived type is the same behavior.
                if (!m.IsStatic && !seen.Add(m.GetBaseDefinition()))
                    continue;
                AddClosings(result, MethodKey(type, m), m);
            }
        }
        if (!type.IsInterface && !type.IsAbstract)
        {
            // Interfaces from any assembly: an explicit implementation of an external interface (IDisposable.Dispose) is
            // subject behavior too. Default implementations come from subject interfaces only.
            foreach (var iface in type.GetInterfaces().Where(i => !i.ContainsGenericParameters))
            {
                InterfaceMapping map;
                try
                {
                    map = type.GetInterfaceMap(iface);
                }
                catch (ArgumentException)
                {
                    continue;
                }
                for (var i = 0; i < map.InterfaceMethods.Length; i++)
                {
                    var (im, target) = (map.InterfaceMethods[i], map.TargetMethods[i]);
                    // A public implementation on the type is already driven directly; only subject code is driven.
                    if (im.IsStatic || im.Name.Contains('<') || (target.IsPublic && target.DeclaringType != iface)
                        || target.DeclaringType?.Assembly != type.Assembly)
                        continue;
                    AddClosings(result, "via " + StateRenderer.TypeName(iface) + "." + MethodKey(iface, im), im);
                }
            }
        }
        return result.OrderBy(x => x.Item1, StringComparer.Ordinal);
    }

    /// <summary>A method under its key; a generic method once per closing (the value-type closing keyed <c>key over System.Int32</c>),
    /// or once with a null method when no closing fits.</summary>
    private static void AddClosings(List<(string, MethodInfo?, MethodInfo)> result, string key, MethodInfo m)
    {
        if (!m.IsGenericMethodDefinition)
        {
            result.Add((key, m, m));
            return;
        }
        var closings = CloseMethods(m);
        if (closings.Count == 0)
            result.Add((key, null, m));
        for (var i = 0; i < closings.Count; i++)
            result.Add((i == 0 ? key : key + " over " + StateRenderer.TypeName(closings[i].GetGenericArguments()[0]), closings[i], m));
    }

    /// <summary>Parameterless getters are observed as state; event accessors take delegates.</summary>
    private static bool IsStateAccessor(MethodInfo m) =>
        m.IsSpecialName && ((m.Name.StartsWith("get_", StringComparison.Ordinal) && m.GetParameters().Length == 0)
            || m.Name.StartsWith("add_", StringComparison.Ordinal) || m.Name.StartsWith("remove_", StringComparison.Ordinal));

    /// <summary>
    /// The receiver constructor: the parameterless one when there is one, else each public constructor the generator
    /// can supply, fewest parameters first, tried with up to 16 generated tuples (non-null ones first) until one
    /// constructs. Each attempt runs sandboxed (private working directory, captured console) with a 2 s budget; a
    /// timed-out attempt calls <see cref="OracleOptions.OnPlanTimeout"/>, which in child mode ends the process.
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
                var outcome = Sandbox.Run(TimeSpan.FromSeconds(2), _ => ctor.Invoke(MemberOracleInputs.Materialize(args, parameters)));
                if (outcome.TimedOut)
                {
                    options.OnPlanTimeout?.Invoke(typeName);
                    return (null, [], "receiver construction timed out");
                }
                if (outcome.Error is null)
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

    public static Type? CloseType(Type open) => CloseTypes(open).FirstOrDefault();

    public static MethodInfo? CloseMethod(MethodInfo open) => CloseMethods(open).FirstOrDefault();

    /// <summary>The value type a generic is also closed over (#1667), when its constraints allow it.</summary>
    public static readonly Type ValueClosing = typeof(int);

    /// <summary>
    /// #1667: the closings the oracle drives. The first of <see cref="ClosingArguments"/> that meets every constraint, as
    /// before, and also <see cref="ValueClosing"/> when that first one is a reference type and <c>int</c> fits too. Code that
    /// depends on <c>T</c> (<c>default(T)</c>, a null check, boxing, <c>typeof(T)</c>) can behave the same for
    /// <c>object</c> and differently for a value type, so one reference closing alone could hide the change.
    /// </summary>
    private static List<T> Closings<T>(int arity, Func<Type[], T> make) where T : class
    {
        var closings = new List<T>();
        foreach (var argument in ClosingArguments)
        {
            if (TryMake(argument) is { } first)
            {
                closings.Add(first);
                if (!argument.IsValueType && TryMake(ValueClosing) is { } value)
                    closings.Add(value);
                break;
            }
        }
        return closings;

        T? TryMake(Type argument)
        {
            try
            {
                return make(Enumerable.Repeat(argument, arity).ToArray());
            }
            catch (ArgumentException)
            {
                // A constraint rejects this argument.
                return null;
            }
        }
    }

    public static List<Type> CloseTypes(Type open) => Closings(open.GetGenericArguments().Length, open.MakeGenericType);

    public static List<MethodInfo> CloseMethods(MethodInfo open) => Closings(open.GetGenericArguments().Length, open.MakeGenericMethod);

    /// <summary>A method's plan key. A method declared on another type is prefixed with its declaring type.</summary>
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
        "construct" => type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(c => "ctor" + Signature(c) == member.Callable),
        _ => Callables(type).FirstOrDefault(c => c.Key == member.Callable).Method,
    };

    /// <summary>Suffix segments a member may get (#1667): after this many failing inputs, the rest of its inputs are dropped.</summary>
    public const int MaxSegments = 3;

    /// <summary>
    /// #1667: the plan with a suffix segment after every limited member. A member limited at input k (it timed out or
    /// crashed there) used to stop at k, so inputs after k were never compared. Each limit now adds a segment that
    /// drives inputs k+1 onward, right after the member, keyed <c>Member [inputs k+1..]</c>. A segment can be limited in
    /// turn, up to <see cref="MaxSegments"/> segments per member. The expansion is a pure function of the plan and the
    /// limits, so the main runs, the restarts and every replay derive the same plan.
    /// </summary>
    public static List<PlannedMember> Expand(IEnumerable<PlannedMember> plan, IReadOnlyDictionary<string, int> limits)
    {
        var expanded = new List<PlannedMember>();
        foreach (var member in plan)
        {
            expanded.Add(member);
            var current = member;
            while (current.Segment + 1 < MaxSegments && limits.TryGetValue(current.Key, out var k) && k + 1 < current.Inputs.Count)
            {
                var from = current.Offset + k + 1;
                current = current with
                {
                    Member = member.Member + $" [inputs {from}..]",
                    Origin = member.Member,
                    Inputs = member.Inputs.Skip(from).ToList(),
                    Offset = from,
                    Segment = current.Segment + 1,
                };
                expanded.Add(current);
            }
        }
        return expanded;
    }
}

/// <summary>
/// Runs subject code on a worker thread with a private working directory and captured console, within a budget.
/// A worker that does not finish is abandoned (it cannot be stopped); callers in child processes then end the process.
/// </summary>
internal static class Sandbox
{
    public sealed record Outcome(bool TimedOut, Exception? Error, string Console, string Directory);

    public static Outcome Run(TimeSpan budget, Action<string> body)
    {
        var sandbox = System.IO.Directory.CreateTempSubdirectory("calor-oracle-1587-");
        var stdout = new StringWriter(CultureInfo.InvariantCulture);
        var (originalOut, originalErr, originalIn, originalDir) = (Console.Out, Console.Error, Console.In, Environment.CurrentDirectory);
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                body(sandbox.FullName);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        }, 16 * 1024 * 1024) { IsBackground = true, CurrentCulture = CultureInfo.InvariantCulture, CurrentUICulture = CultureInfo.InvariantCulture };
        var timedOut = false;
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stdout);
            Console.SetIn(new StringReader(""));
            Environment.CurrentDirectory = sandbox.FullName;
            thread.Start();
            timedOut = !thread.Join(budget);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
            Console.SetIn(originalIn);
            Environment.CurrentDirectory = originalDir;
            if (!timedOut)
            {
                try { sandbox.Delete(recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        return new Outcome(timedOut, error, stdout.ToString(), sandbox.FullName);
    }
}
