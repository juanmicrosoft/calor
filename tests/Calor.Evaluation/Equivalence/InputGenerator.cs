using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Calor.Evaluation.Equivalence;

/// <summary>
/// The registered input generator (<c>b1-1276-signature-inputs-v1</c>). Inputs are a pure function
/// of (pair id, member key, parameter types): boundary values, a capped cartesian product, and
/// seeded random tuples. The member key is shared by both arms, so both arms receive the same
/// inputs in the same order. No value depends on either arm's code or output.
/// </summary>
public static class InputGenerator
{
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
    };

    private static readonly Type[] ListShapes =
        [typeof(List<>), typeof(IList<>), typeof(IReadOnlyList<>), typeof(IEnumerable<>), typeof(ICollection<>), typeof(IReadOnlyCollection<>)];

    public static bool Supports(MethodInfo method) =>
        !method.IsGenericMethodDefinition
        && method.GetParameters().All(p => !p.ParameterType.IsByRef && IsSupportedValue(p.ParameterType))
        && (method.ReturnType == typeof(void) || IsSupportedValue(method.ReturnType));

    private static bool IsSupportedValue(Type t) =>
        Pools.ContainsKey(t) || (Nullable.GetUnderlyingType(t) is { } u && Pools.ContainsKey(u)) || ElementType(t) is { } e && Pools.ContainsKey(e);

    private static Type? ElementType(Type t) =>
        t.IsArray && t.GetArrayRank() == 1 ? t.GetElementType()
        : t.IsGenericType && ListShapes.Contains(t.GetGenericTypeDefinition()) ? t.GetGenericArguments()[0]
        : null;

    /// <summary>All registered input tuples for one member, in registered order.</summary>
    public static IEnumerable<object?[]> Generate(string pairId, string memberKey, MethodInfo method)
    {
        var types = method.GetParameters().Select(p => p.ParameterType).ToArray();
        if (types.Length == 0 || memberKey.StartsWith(PairDifferentialOracle.EntryKeyPrefix, StringComparison.Ordinal))
        {
            yield return [];
            yield break;
        }

        var pools = types.Select(BoundaryPool).ToArray();
        var product = pools.Aggregate(1L, (acc, p) => Math.Min(acc * p.Length, long.MaxValue / 64));
        if (product <= PairDifferentialOracle.CartesianCap)
        {
            foreach (var tuple in Cartesian(pools, 0))
                yield return tuple;
        }
        else
        {
            var longest = pools.Max(p => p.Length);
            for (var i = 0; i < longest; i++)
                yield return pools.Select((p, j) => p[(i + j) % p.Length]).ToArray();
        }

        var rng = new SplitMix64(Seed(pairId, memberKey));
        for (var n = 0; n < PairDifferentialOracle.RandomTuplesPerMember; n++)
            yield return types.Select(t => RandomValue(t, rng)).ToArray();
    }

    public static ulong Seed(string pairId, string memberKey)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{PairDifferentialOracle.InputGeneratorVersion}|{pairId}|{memberKey}"));
        return System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(digest);
    }

    private static IEnumerable<object?[]> Cartesian(object?[][] pools, int index)
    {
        if (index == pools.Length)
        {
            yield return new object?[pools.Length];
            yield break;
        }
        foreach (var head in pools[index])
        {
            foreach (var tail in Cartesian(pools, index + 1))
            {
                tail[index] = head;
                yield return tail;
            }
        }
    }

    private static object?[] BoundaryPool(Type t)
    {
        if (Pools.TryGetValue(t, out var pool))
            return pool;
        if (Nullable.GetUnderlyingType(t) is { } underlying)
            return [null, .. Pools[underlying]];
        var e = ElementType(t)!;
        var p = Pools[e].Where(v => v is not null).ToArray();
        object? At(int i) => p[i % p.Length];
        return [null, Make(t, []), Make(t, [At(0)]), Make(t, [At(1), At(2), At(3)]), Make(t, [At(3), At(2), At(1), At(1)]), Make(t, p)];
    }

    private static object? RandomValue(Type t, SplitMix64 rng)
    {
        if (Nullable.GetUnderlyingType(t) is { } underlying)
            return rng.Next(8) == 0 ? null : RandomValue(underlying, rng);
        if (ElementType(t) is { } e)
        {
            if (rng.Next(16) == 0)
                return null;
            var items = new object?[rng.Next(9)];
            for (var i = 0; i < items.Length; i++)
                items[i] = RandomValue(e, rng);
            return Make(t, items);
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

    private static object Make(Type collectionType, object?[] items)
    {
        var e = ElementType(collectionType)!;
        var array = Array.CreateInstance(e, items.Length);
        for (var i = 0; i < items.Length; i++)
            array.SetValue(items[i], i);
        return collectionType.IsArray ? array : Activator.CreateInstance(typeof(List<>).MakeGenericType(e), array)!;
    }

    /// <summary>Fresh copies of mutable inputs, so no invocation sees another's mutation.</summary>
    public static object?[] Clone(object?[] args) => args.Select(a => a switch
    {
        Array array => array.Clone(),
        IList list when a.GetType().IsGenericType => Activator.CreateInstance(a.GetType(), list),
        _ => a,
    }).ToArray();

    /// <summary>Canonical, culture-invariant rendering used for comparison and evidence.</summary>
    public static string Render(object? value) => value switch
    {
        null => "null",
        string s => "\"" + string.Concat(s.Select(c => c is '"' or '\\' ? "\\" + c : c < 0x20 || c > 0x7e ? $"\\u{(int)c:x4}" : c.ToString())) + "\"",
        char c => $"'\\u{(int)c:x4}'",
        bool b => b ? "true" : "false",
        double d => d.ToString("R", CultureInfo.InvariantCulture) + "d",
        float f => f.ToString("R", CultureInfo.InvariantCulture) + "f",
        decimal m => m.ToString(CultureInfo.InvariantCulture) + "m",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        object?[] tuple when value.GetType() == typeof(object[]) => "(" + string.Join(", ", tuple.Select(Render)) + ")",
        IEnumerable items => "[" + string.Join(",", items.Cast<object?>().Select(Render)) + "]",
        _ => "<" + value.GetType().FullName + ">",
    };

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
