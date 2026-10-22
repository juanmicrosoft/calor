using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Calor.RoundTrip.Harness.Oracle;

/// <summary>
/// #1587: the member oracle's input generator (<see cref="GeneratorId"/>). It is a fork of the B1
/// registered generator (<c>tests/Calor.Evaluation/Equivalence/InputGenerator.cs</c>, hash-pinned by
/// the #1276 registration, so it is not edited). The pools are the same; this fork adds enums,
/// <see cref="CancellationToken"/>, <see cref="object"/>, by-ref and out parameters, and subject types
/// with a public parameterless constructor.
///
/// <para>Inputs are arm-neutral: a value that depends on a type the subject defines (an enum value, a
/// default-constructed subject object) is a token that <see cref="Materialize"/> turns into a value of
/// the parameter type in each load context. Both arms therefore get equal inputs, and no input depends
/// on either arm's code. The set is a pure function of (seed key, member key, parameter shapes).</para>
/// </summary>
public static class MemberOracleInputs
{
    public const string GeneratorId = "1587-member-inputs-v1";

    /// <summary>Full Cartesian product of the boundary pools up to this many tuples; a diagonal walk above it.</summary>
    public const int CartesianCap = 64;

    /// <summary>Seeded random tuples added after the boundary tuples.</summary>
    public const int RandomTuplesPerMember = 16;

    private static readonly Dictionary<Type, object?[]> Pools = new()
    {
        [typeof(bool)] = [false, true],
        [typeof(int)] = [0, 1, -1, 2, -2, 3, 5, 7, 10, 15, 100, -100, 1000, int.MaxValue, int.MinValue],
        [typeof(long)] = [0L, 1L, -1L, 2L, 10L, 100L, -100L, 1_000_000L, 2_147_483_648L, long.MaxValue, long.MinValue],
        [typeof(short)] = [(short)0, (short)1, (short)-1, (short)2, (short)10, (short)100, short.MaxValue, short.MinValue],
        [typeof(byte)] = [(byte)0, (byte)1, (byte)2, (byte)10, (byte)127, byte.MaxValue],
        [typeof(uint)] = [0u, 1u, 2u, 10u, 100u, uint.MaxValue],
        [typeof(ulong)] = [0UL, 1UL, 2UL, 10UL, 100UL, ulong.MaxValue],
        [typeof(double)] = [0.0, -0.0, 1.0, -1.0, 0.5, -2.5, 3.14159, 100.0, 1e9, -1e9, double.NaN, double.PositiveInfinity, double.NegativeInfinity],
        [typeof(float)] = [0f, -0f, 1f, -1f, 0.5f, -2.5f, 3.14159f, 100f, 1e9f, -1e9f, float.NaN, float.PositiveInfinity, float.NegativeInfinity],
        [typeof(decimal)] = [0m, 1m, -1m, 0.5m, -2.5m, 100.25m, decimal.MaxValue, decimal.MinValue],
        [typeof(char)] = ['a', 'Z', '0', ' ', ',', '"', '\n', '\0'],
        [typeof(string)] = [null, "", "a", "abc", "Hello, World", "a,b,c", "a,b\nc,d", "  padded  ", "12345", "racecar", "AbC", "ünï©ødé"],
        [typeof(object)] = [null, 1, "a"],
        [typeof(CancellationToken)] = [CancellationToken.None],
        [typeof(Type)] = [null, typeof(int), typeof(string), typeof(object), typeof(List<int>)],
        [typeof(CultureInfo)] = [null, CultureInfo.InvariantCulture, CultureInfo.GetCultureInfo("en-US"), CultureInfo.GetCultureInfo("de-DE"), CultureInfo.GetCultureInfo("ja-JP")],
        [typeof(IFormatProvider)] = [null, CultureInfo.InvariantCulture, CultureInfo.GetCultureInfo("en-US"), CultureInfo.GetCultureInfo("de-DE")],
        [typeof(Exception)] = [null, new FactoryToken("new Exception(\"boom\")", () => new Exception("boom")),
            new FactoryToken("new InvalidOperationException(\"bad\", inner)", () => new InvalidOperationException("bad", new ArgumentException("inner")))],
        [typeof(DateTime)] = [DateTime.MinValue, DateTime.MaxValue, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 2, 29, 13, 45, 30, 123, DateTimeKind.Unspecified), new DateTime(1999, 12, 31, 23, 59, 59, DateTimeKind.Local)],
        [typeof(DateTimeOffset)] = [DateTimeOffset.MinValue, DateTimeOffset.MaxValue, new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 2, 29, 13, 45, 30, 123, TimeSpan.FromHours(5.5))],
        [typeof(TimeSpan)] = [TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(-1), TimeSpan.FromDays(1.5), TimeSpan.MaxValue, TimeSpan.MinValue],
        [typeof(Guid)] = [Guid.Empty, new Guid("0f8fad5b-d9cb-469f-a165-70867728950e")],
    };

    /// <summary>Numeric, boolean, character and string types: random inputs are drawn around zero, not from the pool.</summary>
    private static readonly HashSet<Type> Scalars =
        [typeof(bool), typeof(int), typeof(long), typeof(short), typeof(byte), typeof(uint), typeof(ulong), typeof(double), typeof(float), typeof(decimal), typeof(char), typeof(string)];

    /// <summary>A mutable input (an exception, for example), built fresh for every invocation.</summary>
    public sealed record FactoryToken(string Label, Func<object> Make);

    private static readonly Type[] ListShapes =
        [typeof(List<>), typeof(IList<>), typeof(IReadOnlyList<>), typeof(IEnumerable<>), typeof(ICollection<>), typeof(IReadOnlyCollection<>)];

    /// <summary>An enum value, by its underlying value (the enum type may be subject-defined).</summary>
    public sealed record EnumToken(Int128 Value);

    /// <summary>A fresh instance of the parameter type from its public parameterless constructor.</summary>
    public sealed record DefaultInstanceToken
    {
        public static readonly DefaultInstanceToken Instance = new();
        private DefaultInstanceToken() { }
    }

    /// <summary>Why a parameter list cannot be generated, or null when it can.</summary>
    public static string? Unsupported(IReadOnlyList<ParameterInfo> parameters)
    {
        foreach (var p in parameters)
        {
            var t = p.ParameterType.IsByRef ? p.ParameterType.GetElementType()! : p.ParameterType;
            if (!IsSupportedValue(t))
                return "unsupported parameter type " + StateRenderer.TypeName(p.ParameterType);
        }
        return null;
    }

    private static bool IsSupportedValue(Type t) =>
        Pools.ContainsKey(t)
        || t.IsEnum
        || (Nullable.GetUnderlyingType(t) is { } u && (Pools.ContainsKey(u) || u.IsEnum))
        || (ElementType(t) is { } e && Pools.ContainsKey(e))
        || IsDefaultConstructible(t);

    /// <summary>A concrete subject class with a public parameterless constructor: inputs are null and a fresh instance.</summary>
    private static bool IsDefaultConstructible(Type t) =>
        t is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false }
        && !typeof(Delegate).IsAssignableFrom(t)
        && t.Assembly != typeof(object).Assembly
        && t.GetConstructor(Type.EmptyTypes) is { IsPublic: true };

    private static Type? ElementType(Type t) =>
        t.IsArray && t.GetArrayRank() == 1 ? t.GetElementType()
        : t.IsGenericType && ListShapes.Contains(t.GetGenericTypeDefinition()) ? t.GetGenericArguments()[0]
        : null;

    /// <summary>All input tuples for one member, in a fixed order. Requires <see cref="Unsupported"/> to be null.</summary>
    public static IReadOnlyList<object?[]> Generate(string seedKey, string memberKey, IReadOnlyList<ParameterInfo> parameters)
    {
        var types = parameters.Select(p => p.ParameterType.IsByRef ? p.ParameterType.GetElementType()! : p.ParameterType).ToArray();
        var outs = parameters.Select(p => p.IsOut).ToArray();
        if (types.Length == 0)
            return [[]];

        var result = new List<object?[]>();
        var pools = types.Select((t, i) => outs[i] ? [DefaultOf(t)] : BoundaryPool(t)).ToArray();
        var product = pools.Aggregate(1L, (acc, p) => Math.Min(acc * p.Length, long.MaxValue / 64));
        if (product <= CartesianCap)
        {
            result.AddRange(Cartesian(pools));
        }
        else
        {
            var longest = pools.Max(p => p.Length);
            for (var i = 0; i < longest; i++)
                result.Add(pools.Select((p, j) => p[(i + j) % p.Length]).ToArray());
        }

        var rng = new SplitMix64(Seed(seedKey, memberKey));
        for (var n = 0; n < RandomTuplesPerMember; n++)
            result.Add(types.Select((t, i) => outs[i] ? DefaultOf(t) : RandomValue(t, rng)).ToArray());
        return result;
    }

    /// <summary>Candidate arguments for building a receiver, in the order they are tried: tuples with no null value
    /// first, then the rest, at most <paramref name="max"/>.</summary>
    public static IReadOnlyList<object?[]> ReceiverCandidates(string seedKey, string memberKey, IReadOnlyList<ParameterInfo> parameters, int max = 16)
    {
        var tuples = Generate(seedKey, memberKey, parameters);
        return tuples.Where(t => t.All(v => v is not null)).Concat(tuples.Where(t => t.Any(v => v is null))).Take(max).ToList();
    }

    public static ulong Seed(string seedKey, string memberKey)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{GeneratorId}|{seedKey}|{memberKey}"));
        return System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(digest);
    }

    private static object? DefaultOf(Type t) =>
        t.IsEnum ? new EnumToken(0) : t.IsValueType && Nullable.GetUnderlyingType(t) is null ? Activator.CreateInstance(t) : null;

    /// <summary>First parameter outermost, last parameter innermost.</summary>
    private static IEnumerable<object?[]> Cartesian(object?[][] pools) =>
        pools.Aggregate((IEnumerable<object?[]>)[[]], (acc, pool) => acc.SelectMany(prefix => pool.Select(v => (object?[])[.. prefix, v])));

    private static object?[] BoundaryPool(Type t)
    {
        if (Pools.TryGetValue(t, out var pool))
            return pool;
        if (t.IsEnum)
            return EnumPool(t);
        if (Nullable.GetUnderlyingType(t) is { } underlying)
            return [null, .. underlying.IsEnum ? EnumPool(underlying) : Pools[underlying]];
        if (ElementType(t) is { } e)
        {
            var p = Pools[e].Where(v => v is not null).ToArray();
            object? At(int i) => p[i % p.Length];
            return [null, new ListSpec([]), new ListSpec([At(0)]), new ListSpec([At(1), At(2), At(3)]), new ListSpec([At(3), At(2), At(1), At(1)]), new ListSpec(p)];
        }
        return [null, DefaultInstanceToken.Instance];
    }

    /// <summary>Every declared value (ordered by underlying value) plus one undeclared value inside the underlying type's range.</summary>
    private static object?[] EnumPool(Type enumType)
    {
        var values = Enum.GetValuesAsUnderlyingType(enumType).Cast<object>().Select(ToInt128).Distinct().Order().ToList();
        var (min, max) = Range(Enum.GetUnderlyingType(enumType));
        Int128? undeclared = values.Count == 0 ? 0 : values[^1] < max ? values[^1] + 1 : values[0] > min ? values[0] - 1 : null;
        return [.. values.Select(v => (object?)new EnumToken(v)), .. undeclared is { } u ? new object?[] { new EnumToken(u) } : []];
    }

    private static Int128 ToInt128(object value) => value switch
    {
        ulong u => u,
        uint u => u,
        ushort u => u,
        byte u => u,
        char c => c,
        _ => Convert.ToInt64(value, CultureInfo.InvariantCulture),
    };

    private static (Int128 Min, Int128 Max) Range(Type underlying) => Type.GetTypeCode(underlying) switch
    {
        TypeCode.SByte => (sbyte.MinValue, sbyte.MaxValue),
        TypeCode.Byte => (byte.MinValue, byte.MaxValue),
        TypeCode.Int16 => (short.MinValue, short.MaxValue),
        TypeCode.UInt16 => (ushort.MinValue, ushort.MaxValue),
        TypeCode.Int32 => (int.MinValue, int.MaxValue),
        TypeCode.UInt32 => (uint.MinValue, uint.MaxValue),
        TypeCode.UInt64 => (ulong.MinValue, ulong.MaxValue),
        _ => (long.MinValue, long.MaxValue),
    };

    /// <summary>An enum value from its underlying value, in the enum's own underlying type (never truncated).</summary>
    private static object EnumValue(Type enumType, Int128 value) => Type.GetTypeCode(Enum.GetUnderlyingType(enumType)) switch
    {
        TypeCode.UInt64 or TypeCode.UInt32 or TypeCode.UInt16 or TypeCode.Byte => Enum.ToObject(enumType, (ulong)value),
        _ => Enum.ToObject(enumType, (long)value),
    };

    private static object? RandomValue(Type t, SplitMix64 rng)
    {
        if (t.IsEnum)
        {
            var pool = EnumPool(t);
            return pool[rng.Next(pool.Length)];
        }
        if (Nullable.GetUnderlyingType(t) is { } underlying)
            return rng.Next(8) == 0 ? null : RandomValue(underlying, rng);
        if (ElementType(t) is { } e)
        {
            if (rng.Next(16) == 0)
                return null;
            var items = new object?[rng.Next(9)];
            for (var i = 0; i < items.Length; i++)
                items[i] = RandomValue(e, rng);
            return new ListSpec(items);
        }
        if (!Scalars.Contains(t))
        {
            var pool = BoundaryPool(t);
            return pool[rng.Next(pool.Length)];
        }
        if (rng.Next(4) == 0)
        {
            var pool = Pools[t];
            return pool[rng.Next(pool.Length)];
        }
        const string alphabet = "abcXYZ019 ,;\n\"";
        var centered = (long)rng.Next(2001) - 1000;
        return Type.GetTypeCode(t) switch
        {
            TypeCode.Boolean => rng.Next(2) == 1,
            TypeCode.Int32 => (int)centered,
            TypeCode.Int64 => centered * 1000,
            TypeCode.Int16 => (short)centered,
            TypeCode.Byte => (byte)rng.Next(256),
            TypeCode.UInt32 => (uint)rng.Next(2001),
            TypeCode.UInt64 => (ulong)rng.Next(2001),
            TypeCode.Double => centered / 8.0,
            TypeCode.Single => (float)(centered / 8.0),
            TypeCode.Decimal => centered / 8m,
            TypeCode.Char => alphabet[rng.Next(alphabet.Length)],
            _ => new string(Enumerable.Range(0, rng.Next(13)).Select(_ => alphabet[rng.Next(alphabet.Length)]).ToArray()),
        };
    }

    /// <summary>A collection input, materialized as an array or a <see cref="List{T}"/> of the parameter's element type.</summary>
    public sealed record ListSpec(object?[] Items);

    /// <summary>Fresh, arm-local argument values for one invocation: tokens resolved, collections and instances newly built.</summary>
    public static object?[] Materialize(object?[] tuple, IReadOnlyList<ParameterInfo> parameters) =>
        tuple.Select((v, i) => Materialize(v, parameters[i].ParameterType.IsByRef ? parameters[i].ParameterType.GetElementType()! : parameters[i].ParameterType)).ToArray();

    private static object? Materialize(object? value, Type target) => value switch
    {
        EnumToken token => EnumValue(Nullable.GetUnderlyingType(target) ?? target, token.Value),
        DefaultInstanceToken => Activator.CreateInstance(target),
        FactoryToken factory => factory.Make(),
        ListSpec list => MakeCollection(target, list.Items),
        _ => value,
    };

    private static object MakeCollection(Type collectionType, object?[] items)
    {
        var e = ElementType(collectionType)!;
        var array = Array.CreateInstance(e, items.Length);
        Array.Copy(items, array, items.Length);
        return collectionType.IsArray ? array : Activator.CreateInstance(typeof(List<>).MakeGenericType(e), array)!;
    }

    /// <summary>Arm-neutral rendering of an input tuple for witnesses.</summary>
    public static string Render(object?[] tuple) => "(" + string.Join(", ", tuple.Select(v => v switch
    {
        EnumToken t => "enum:" + t.Value.ToString(CultureInfo.InvariantCulture),
        DefaultInstanceToken => "new()",
        FactoryToken f => f.Label,
        ListSpec l => "[" + string.Join(",", l.Items.Select(i => StateRenderer.Render(i, 0))) + "]",
        _ => StateRenderer.Render(v, 0),
    })) + ")";

    /// <summary>SplitMix64: a fixed, portable PRNG, so the input set never depends on the runtime's Random.</summary>
    internal sealed class SplitMix64(ulong state)
    {
        private ulong _state = state;

        public ulong NextUInt64()
        {
            var z = _state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public int Next(int exclusiveMax) => (int)(NextUInt64() % (ulong)exclusiveMax);
    }
}
