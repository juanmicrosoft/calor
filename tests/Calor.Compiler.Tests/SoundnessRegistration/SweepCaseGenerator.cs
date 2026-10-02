using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Calor.Compiler.Tests.SoundnessRegistration;

/// <summary>
/// #1419 (0.24 R1) — deterministic expansion of the frozen case templates into the #1311 case set.
/// Every case is a pair rendered from ONE template: the Calor program the released verifier sees,
/// and an independent C# reference program (the behavioral oracle) written by the registrant. The
/// generator never calls the compiler, its simplifier, its translator, or its emitter; the oracle
/// text comes only from the template and the registered value tables.
/// </summary>
internal static class SweepCaseGenerator
{
    /// <summary>Hard cap on the input tuples one case enumerates when the domain is sampled.</summary>
    internal const int MaxSampledTuples = 4096;

    /// <summary>Seeded draws added to each numeric parameter's boundary set when sampling.</summary>
    internal const int SeededValuesPerParameter = 6;

    internal sealed record NumericType(string Calor, string CSharp, BigInteger Min, BigInteger Max)
    {
        public bool Narrow => Max - Min < 65536;
        public bool Unsigned => Min.IsZero;
    }

    internal static readonly IReadOnlyDictionary<string, NumericType> Numeric =
        new Dictionary<string, NumericType>(StringComparer.Ordinal)
        {
            ["i8"] = new("i8", "sbyte", sbyte.MinValue, sbyte.MaxValue),
            ["i16"] = new("i16", "short", short.MinValue, short.MaxValue),
            ["i32"] = new("i32", "int", int.MinValue, int.MaxValue),
            ["i64"] = new("i64", "long", long.MinValue, long.MaxValue),
            ["u8"] = new("u8", "byte", byte.MinValue, byte.MaxValue),
            ["u16"] = new("u16", "ushort", ushort.MinValue, ushort.MaxValue),
            ["u32"] = new("u32", "uint", uint.MinValue, uint.MaxValue),
            ["u64"] = new("u64", "ulong", ulong.MinValue, ulong.MaxValue),
        };

    /// <summary>Registered non-numeric value tables (C# expressions), frozen with the registration.</summary>
    internal static readonly IReadOnlyDictionary<string, string[]> FixedTables =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["bool"] = ["false", "true"],
            ["str"] = ["null", "\"\"", "\"a\"", "\"abc\"", "\"ABC\"", "\"b\"", "\"\\u00e9\"", "\"\\ud83d\\ude00ab\"",
                "\"\\u200dabc\"", "\"aaaa\"", "\"zzz\"", "\"a\\u0000b\""],
            ["i32[]"] = ["null", "new int[0]", "new[] { 0 }", "new[] { -1 }", "new[] { int.MaxValue }",
                "new[] { 1, 2, 3 }", "new[] { 5, 4, 3, 2, 1 }", "new[] { int.MinValue, 0, int.MaxValue }"],
            ["i8[]"] = ["null", "new sbyte[0]", "new sbyte[] { -128 }", "new sbyte[] { 0, 127 }"],
            ["i16[]"] = ["null", "new short[0]", "new short[] { -32768 }", "new short[] { 0, 32767 }"],
            ["i64[]"] = ["null", "new long[0]", "new[] { long.MinValue }", "new[] { 0L, 1L, long.MaxValue }"],
            ["u8[]"] = ["null", "new byte[0]", "new byte[] { 0 }", "new byte[] { 255 }", "new byte[] { 1, 2, 3 }"],
            ["u16[]"] = ["null", "new ushort[0]", "new ushort[] { 0 }", "new ushort[] { 65535 }"],
            ["u32[]"] = ["null", "new uint[0]", "new uint[] { 0 }", "new uint[] { 4294967295 }"],
            ["u64[]"] = ["null", "new ulong[0]", "new ulong[] { 0 }", "new ulong[] { 18446744073709551615 }"],
        };

    internal sealed record Case(
        string Id,
        string RowId,
        string TemplateId,
        int Instance,
        string Claim,
        bool Exhaustive,
        string CalorSource,
        string? CalorPrimeSource,
        string OracleSource)
    {
        public string CalorSha256 => Sha256(CalorSource);
        public string OracleSha256 => Sha256(OracleSource);
    }

    /// <summary>SplitMix64 — fixed algorithm, so the case set never depends on System.Random.</summary>
    internal sealed class SplitMix64(ulong seed)
    {
        private ulong _state = seed;

        public ulong Next()
        {
            var z = _state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>Uniform-enough draw in [0, bound) (modulo reduction; the bias is part of the frozen method).</summary>
        public int Below(int bound) => (int)(Next() % (ulong)bound);

        public BigInteger InRange(BigInteger min, BigInteger max)
        {
            var span = max - min + 1;
            var draw = new BigInteger(Next()) * ulong.MaxValue + new BigInteger(Next());
            return min + BigInteger.Remainder(draw, span);
        }
    }

    /// <summary>Per-case seed: the registered master seed mixed with a stable hash of the case id.</summary>
    internal static ulong CaseSeed(ulong masterSeed, string caseId)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(caseId));
        return masterSeed ^ BitConverter.ToUInt64(digest, 0);
    }

    internal static IReadOnlyList<BigInteger> BoundaryValues(NumericType type)
    {
        var candidates = new BigInteger[]
        {
            type.Min, type.Min + 1, -2, -1, 0, 1, 2, type.Max - 1, type.Max,
            int.MinValue, int.MaxValue, (BigInteger)int.MaxValue + 1, uint.MaxValue, (BigInteger)uint.MaxValue + 1,
            long.MaxValue, (BigInteger)long.MaxValue + 1,
        };
        return candidates.Where(v => v >= type.Min && v <= type.Max).Distinct().Order().ToList();
    }

    /// <summary>Expands every registered template in file order into its frozen cases.</summary>
    internal static IReadOnlyList<Case> Generate(JsonNode registration, JsonNode templates)
    {
        var masterSeed = ulong.Parse(
            registration["generation"]!["masterSeed"]!.GetValue<string>(), CultureInfo.InvariantCulture);
        var perRow = new Dictionary<string, int>(StringComparer.Ordinal);
        var cases = new List<Case>();
        foreach (var template in templates["templates"]!.AsArray())
        {
            var rowId = template!["row"]!.GetValue<string>();
            var instances = template["instances"]!.GetValue<int>();
            for (var instance = 0; instance < instances; instance++)
            {
                perRow[rowId] = perRow.GetValueOrDefault(rowId) + 1;
                var caseId = $"R1-{rowId}-{perRow[rowId]:D3}";
                cases.Add(Expand(template, caseId, instance, new SplitMix64(CaseSeed(masterSeed, caseId))));
            }
        }
        return cases;
    }

    internal static Case Expand(JsonNode template, string caseId, int instance, SplitMix64 rng)
    {
        var holes = new Dictionary<string, (string Calor, string CSharp)>(StringComparer.Ordinal);
        var holeTypes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, spec) in template["holes"]?.AsObject() ?? new JsonObject())
            holes[name] = DrawHole(spec!, holeTypes, rng, name);

        var oracle = template["oracle"]!;
        var claim = oracle["claim"]?.GetValue<string>() ?? "forall";
        var overflow = template["overflow"]?.GetValue<string>() ?? "checked";
        var header = overflow == "unchecked" ? "§M{m1:R1Case:overflow=unchecked}" : "§M{m1:R1Case}";
        var calor = header + "\n" + Fill(Text(template["calor"]!), holes, csharp: false).TrimEnd() + "\n";
        var prime = template["calorPrime"] is { } primeNode
            ? header + "\n" + Fill(Text(primeNode), holes, csharp: false).TrimEnd() + "\n"
            : null;

        var domain = (oracle["domain"]?.AsArray() ?? new JsonArray())
            .Select(d => (Name: d!["name"]!.GetValue<string>(),
                Type: Fill(d["type"]!.GetValue<string>(), holes, csharp: false),
                Values: d["values"]?.AsArray().Select(v => Fill(v!.GetValue<string>(), holes, csharp: true)).ToArray()))
            .ToList();
        var exhaustiveProp = oracle["exhaustiveProp"]?.GetValue<bool>() ?? true;
        var (inputs, exhaustive) = RenderInputs(domain, rng);
        exhaustive &= exhaustiveProp;

        var parameters = string.Join(", ", domain.Select(d => $"{CSharpType(d.Type)} {d.Name}"));
        var unpack = string.Join(", ", domain.Select((d, i) => $"({CSharpType(d.Type)})a[{i}]{(IsReference(d.Type) ? "" : "!")}"));
        var resultType = oracle["result"] is { } r ? CSharpType(Fill(r.GetValue<string>(), holes, csharp: false)) : null;
        var body = oracle["body"] is { } b ? Fill(Text(b), holes, csharp: true) : null;
        var sb = new StringBuilder();
        sb.Append("// R1 #1419 independent oracle for ").Append(caseId).Append('\n');
        sb.Append("using System;\nusing System.Collections.Generic;\nusing System.Linq;\nusing System.Numerics;\n");
        sb.Append("public static class R1Oracle\n{\n");
        sb.Append("    public const string Claim = \"").Append(claim).Append("\";\n");
        sb.Append("    public const bool Exhaustive = ").Append(exhaustive ? "true" : "false").Append(";\n");
        sb.Append("    public const bool Checked = ").Append(overflow == "unchecked" ? "false" : "true").Append(";\n");
        if (oracle["prelude"] is { } prelude)
            sb.Append(Fill(Text(prelude), holes, csharp: true)).Append('\n');
        sb.Append("    public static bool Hyp(").Append(parameters).Append(") => ")
          .Append(Fill(oracle["hyp"]?.GetValue<string>() ?? "true", holes, csharp: true)).Append(";\n");
        if (body != null)
        {
            sb.Append("    public static ").Append(resultType).Append(" Body(").Append(parameters).Append(")\n    {\n        ")
              .Append(body).Append("\n    }\n");
        }
        var propParams = body != null ? (parameters.Length > 0 ? parameters + ", " : "") + resultType + " result" : parameters;
        sb.Append("    public static bool Prop(").Append(propParams).Append(") => ")
          .Append(Fill(oracle["prop"]!.GetValue<string>(), holes, csharp: true)).Append(";\n");
        sb.Append("    public static bool HypO(object?[] a) => Hyp(").Append(unpack).Append(");\n");
        sb.Append("    public static bool HasBody => ").Append(body != null ? "true" : "false").Append(";\n");
        sb.Append("    public static object? BodyO(object?[] a) => ").Append(body != null ? $"Body({unpack})" : "null").Append(";\n");
        var resultUnpack = resultType == null ? "" : $"({resultType})r{(IsReferenceCSharp(resultType) ? "" : "!")}";
        var propArgs = body != null ? string.Join(", ", new[] { unpack, resultUnpack }.Where(s => s.Length > 0)) : unpack;
        sb.Append("    public static bool PropO(object?[] a, object? r) => Prop(").Append(propArgs).Append(");\n");
        sb.Append(inputs);
        sb.Append("}\n");

        return new Case(caseId, template["row"]!.GetValue<string>(), template["id"]!.GetValue<string>(),
            instance, claim, exhaustive, calor, prime, sb.ToString());
    }

    private static (string Calor, string CSharp) DrawHole(
        JsonNode spec, Dictionary<string, string> holeTypes, SplitMix64 rng, string name)
    {
        if (spec["oneOf"] is JsonArray options)
        {
            var chosen = options[rng.Below(options.Count)]!.GetValue<string>();
            if (Numeric.ContainsKey(chosen) || FixedTables.ContainsKey(chosen))
            {
                holeTypes[name] = chosen;
                return (chosen, CSharpType(chosen));
            }
            return (chosen, chosen);
        }
        if (spec["pairs"] is JsonArray pairs)
        {
            // A Calor spelling and the registrant's C# rendering of the same expression.
            var pair = pairs[rng.Below(pairs.Count)]!.AsArray();
            return (pair[0]!.GetValue<string>(), pair[1]!.GetValue<string>());
        }
        if (spec["literalOf"] is not null || spec["typedLiteralOf"] is not null)
        {
            var typeRef = (spec["literalOf"] ?? spec["typedLiteralOf"])!.GetValue<string>();
            var typeName = holeTypes.TryGetValue(typeRef, out var bound) ? bound : typeRef;
            var type = Numeric[typeName];
            var pool = BoundaryValues(type).ToList();
            for (var i = 0; i < SeededValuesPerParameter; i++)
                pool.Add(rng.InRange(type.Min, type.Max));
            var value = pool[rng.Below(pool.Count)];
            return RenderLiteral(type, value, typed: spec["typedLiteralOf"] != null);
        }
        if (spec["intIn"] is JsonArray range)
        {
            var value = rng.InRange(range[0]!.GetValue<long>(), range[1]!.GetValue<long>());
            return RenderLiteral(Numeric["i32"], value, typed: false);
        }
        throw new InvalidOperationException($"Unknown hole spec for '{name}': {spec.ToJsonString()}");
    }

    /// <summary>
    /// Calor typed literal and the C# literal of the same value and C# type. INT is used whenever
    /// the value fits int (C#'s own literal typing); LONG/UINT/ULONG carry the wide values, or every
    /// value when <paramref name="typed"/> forces the width-carrying spelling (#845 territory).
    /// </summary>
    internal static (string Calor, string CSharp) RenderLiteral(NumericType type, BigInteger value, bool typed)
    {
        var text = value.ToString(CultureInfo.InvariantCulture);
        var fitsInt = value >= int.MinValue && value <= int.MaxValue;
        string Paren(string s) => value.Sign < 0 ? $"({s})" : s;
        return type.Calor switch
        {
            "i64" when typed || !fitsInt => ($"LONG:{text}", Paren(text + "L")),
            "u32" when typed || !fitsInt => ($"UINT:{text}", text + "U"),
            "u64" when typed || !fitsInt => ($"ULONG:{text}", text + "UL"),
            _ => ($"INT:{text}", Paren(text)),
        };
    }

    private static (string Inputs, bool Exhaustive) RenderInputs(
        IReadOnlyList<(string Name, string Type, string[]? Values)> domain, SplitMix64 rng)
    {
        var sb = new StringBuilder("    public static IEnumerable<object?[]> Inputs()\n    {\n");
        // Exhaustive only when every parameter is bool or a narrow integer with no override list and
        // the full product is at most 65,536 tuples (e.g. two 8-bit parameters, or one 16-bit one).
        static BigInteger Size(string type) =>
            type == "bool" ? 2 : Numeric.TryGetValue(type, out var t) && t.Narrow ? t.Max - t.Min + 1 : -1;
        var enumerable = domain.Count > 0 && domain.All(d => d.Values == null && Size(d.Type) > 0);
        var product = domain.Aggregate(BigInteger.One, (acc, d) => acc * BigInteger.Abs(Size(d.Type)));
        if (enumerable && product <= 65536)
        {
            var indent = "        ";
            foreach (var d in domain)
            {
                if (d.Type == "bool")
                    sb.Append(indent).Append($"foreach (var {d.Name} in new[] {{ false, true }})\n");
                else
                {
                    var t = Numeric[d.Type];
                    sb.Append(indent).Append($"for (long {d.Name} = {t.Min}; {d.Name} <= {t.Max}; {d.Name}++)\n");
                }
                indent += "    ";
            }
            var args = string.Join(", ", domain.Select(d => d.Type == "bool" ? d.Name : $"({CSharpType(d.Type)}){d.Name}"));
            sb.Append(indent).Append($"yield return new object?[] {{ {args} }};\n    }}\n");
            return (sb.ToString(), true);
        }

        var columns = domain.Select(d => d.Values ?? (FixedTables.TryGetValue(d.Type, out var fixedValues)
            ? fixedValues
            : NumericColumn(Numeric[d.Type], rng))).ToList();
        var tuples = columns.Aggregate(
            (IEnumerable<string[]>)new[] { Array.Empty<string>() },
            (acc, column) => acc.SelectMany(prefix => column.Select(v => prefix.Append(v).ToArray()))).ToList();
        if (tuples.Count > MaxSampledTuples)
        {
            var keep = new SortedSet<int>();
            while (keep.Count < MaxSampledTuples)
                keep.Add(rng.Below(tuples.Count));
            tuples = keep.Select(i => tuples[i]).ToList();
        }
        foreach (var tuple in tuples)
        {
            var args = string.Join(", ", tuple.Select((v, i) => $"({CSharpType(domain[i].Type)})({v})"));
            sb.Append($"        yield return new object?[] {{ {args} }};\n");
        }
        if (domain.Count == 0)
            sb.Append("        yield return Array.Empty<object?>();\n");
        sb.Append("    }\n");
        // A domain with only fixed tables (no numeric sampling) and no tuple cap is complete for
        // those tables but not for the type, so it is never exhaustive.
        return (sb.ToString(), false);
    }

    private static string[] NumericColumn(NumericType type, SplitMix64 rng)
    {
        var values = BoundaryValues(type).ToList();
        for (var i = 0; i < SeededValuesPerParameter; i++)
            values.Add(rng.InRange(type.Min, type.Max));
        return values.Distinct().Order()
            .Select(v => RenderLiteral(type, v, typed: false).CSharp)
            .ToArray();
    }

    internal static string CSharpType(string calorType) => calorType switch
    {
        "bool" => "bool",
        "str" => "string?",
        "f64" => "double",
        "f32" => "float",
        "int" or "long" or "uint" or "ulong" or "short" or "ushort" or "byte" or "sbyte" or "double" or "float" => calorType,
        "string" or "object" => calorType + "?",
        _ when calorType.EndsWith("[]", StringComparison.Ordinal) => CSharpType(calorType[..^2]).TrimEnd('?') + "[]?",
        _ when Numeric.TryGetValue(calorType, out var t) => t.CSharp,
        _ => calorType + "?",
    };

    private static bool IsReference(string calorType) => CSharpType(calorType).EndsWith('?');

    private static bool IsReferenceCSharp(string csharpType) => csharpType.EndsWith('?');

    /// <summary>A template text field is a string or an array of lines.</summary>
    private static string Text(JsonNode node) => node is JsonArray lines
        ? string.Join("\n", lines.Select(line => line!.GetValue<string>()))
        : node.GetValue<string>();

    private static string Fill(string text, IReadOnlyDictionary<string, (string Calor, string CSharp)> holes, bool csharp)
    {
        foreach (var (name, value) in holes)
            text = text.Replace("{" + name + "}", csharp ? value.CSharp : value.Calor, StringComparison.Ordinal);
        return text;
    }

    internal static string Sha256(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(new UTF8Encoding(false).GetBytes(text.Replace("\r\n", "\n"))));
}
