using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json.Nodes;
using Calor.Compiler.Tests.SoundnessRegistration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Calor.Soundness.Sweep;

internal static class Roslyn
{
    // Compiles one C# source (C# 14, nullable on) to an in-memory library; returns null and the errors on failure.
    public static (MemoryStream? Image, string Errors) Emit(string source, IEnumerable<MetadataReference> references, bool checkOverflow)
    {
        var compilation = CSharpCompilation.Create("R1_" + Guid.NewGuid().ToString("N"), [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp14))],
            references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, checkOverflow: checkOverflow, nullableContextOptions: NullableContextOptions.Enable));
        var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        stream.Position = 0;
        return emit.Success ? (stream, "") : (null, string.Join("; ", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Take(5)));
    }

    public static bool Checked(string oracleSource) => !oracleSource.Contains("public const bool Checked = false;", StringComparison.Ordinal);
}

// Executes a case's registered oracle program on specific inputs, for two registered uses that IndependentOracle.Evaluate (the O1 verdict, used unchanged) does
// not expose: recovering the input tuples behind its rendered witness and replay sample (R1-O2), and replaying a solver counterexample model under O1 semantics
// (spurious-refutation). Compilation mirrors IndependentOracle (BCL only).
internal sealed class OracleProgram : IDisposable
{
    private readonly AssemblyLoadContext _context = new("R1OracleRuntime", isCollectible: true);
    private readonly Type _type;

    public OracleProgram(string oracleSource)
    {
        var (image, errors) = Roslyn.Emit(oracleSource, IndependentOracle.BclReferences, Roslyn.Checked(oracleSource));
        _type = _context.LoadFromStream(image ?? throw new InvalidOperationException("oracle does not compile: " + errors)).GetType("R1Oracle")!;
    }

    // Oracle domain parameters (names and CLR types), from the registered Hyp signature.
    public IReadOnlyList<ParameterInfo> Parameters => _type.GetMethod("Hyp")!.GetParameters();

    // O1 semantics on one input: excluded (Hyp false/throws or Body throws), holds, or violated.
    public string PointVerdict(object?[] input) => WithCulture(() =>
    {
        if (!TryInvoke("HypO", [input], out var hyp)) return "excluded-hyp-threw";
        if (!(bool)hyp!) return "excluded-hyp-false";
        object? result = null;
        if ((bool)_type.GetProperty("HasBody")!.GetValue(null)! && !TryInvoke("BodyO", [input], out result)) return "excluded-body-threw";
        return TryInvoke("PropO", [input, result], out var prop) && (bool)prop! ? "holds" : "violated";
    });

    // Fresh input tuples (from the registered Inputs()) whose rendering is in rendered.
    public Dictionary<string, object?[]> Recover(IReadOnlyCollection<string> rendered)
    {
        var found = new Dictionary<string, object?[]>(StringComparer.Ordinal);
        if (rendered.Count == 0) return found;
        foreach (var input in (IEnumerable<object?[]>)_type.GetMethod("Inputs")!.Invoke(null, null)!)
        {
            var text = Render(input);
            if (rendered.Contains(text)) found.TryAdd(text, input);
            if (found.Count == rendered.Count) break;
        }
        return found;
    }

    // An oracle input tuple from a solver model (integers, bool, string), or null when the model is not replayable.
    public object?[]? FromModel(IReadOnlyDictionary<string, string> bindings)
    {
        var values = new List<object?>();
        foreach (var p in Parameters)
        {
            if (!bindings.TryGetValue(p.Name!, out var raw) || !TryConvert(raw.Trim(), p.ParameterType, out var value)) return null;
            values.Add(value);
        }
        return [.. values];
    }

    private static readonly Dictionary<Type, (int Bits, bool Signed)> Widths = new()
    {
        [typeof(sbyte)] = (8, true), [typeof(short)] = (16, true), [typeof(int)] = (32, true), [typeof(long)] = (64, true),
        [typeof(byte)] = (8, false), [typeof(ushort)] = (16, false), [typeof(uint)] = (32, false), [typeof(ulong)] = (64, false),
    };

    private static bool TryConvert(string raw, Type type, out object? value)
    {
        value = null;
        if (type == typeof(string)) return TryParseZ3String(raw, out value);
        if (type == typeof(bool)) { value = raw == "true"; return raw is "true" or "false"; }
        if (!Widths.TryGetValue(type, out var w)) return false;
        BigInteger n;
        if (raw.StartsWith("#x", StringComparison.Ordinal)) n = BigInteger.Parse("0" + raw[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        else if (raw.StartsWith("#b", StringComparison.Ordinal)) n = raw[2..].Aggregate(BigInteger.Zero, (acc, c) => acc * 2 + (c - '0'));
        else if (!BigInteger.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out n)) return false;
        var modulus = BigInteger.One << w.Bits;
        if (n < 0) n += modulus;
        if (n < 0 || n >= modulus) return false;
        if (w.Signed && n >= modulus / 2) n -= modulus; // bit-vector models print unsigned
        value = Convert.ChangeType(n.ToString(CultureInfo.InvariantCulture), type, CultureInfo.InvariantCulture);
        return true;
    }

    // An SMT-LIB 2.6 string literal as Z3 prints it ("" for a quote, \u{hex} for a code point).
    private static bool TryParseZ3String(string raw, out object? value)
    {
        value = null;
        if (raw.Length < 2 || raw[0] != '"' || raw[^1] != '"') return false;
        var (body, sb) = (raw[1..^1], new StringBuilder());
        for (var i = 0; i < body.Length; i++)
        {
            if (body[i] == '"' && i + 1 < body.Length && body[i + 1] == '"') { sb.Append('"'); i++; continue; }
            if (body[i] == '\\' && i + 2 < body.Length && body[i + 1] == 'u' && body[i + 2] == '{')
            {
                var close = body.IndexOf('}', i);
                if (close < 0 || !int.TryParse(body.AsSpan(i + 3, close - i - 3), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var cp)) return false;
                sb.Append(cp is >= 0xD800 and <= 0xDFFF ? ((char)cp).ToString() : char.ConvertFromUtf32(cp));
                i = close;
                continue;
            }
            sb.Append(body[i]);
        }
        value = sb.ToString();
        return true;
    }

    // Byte-for-byte the rendering IndependentOracle uses for witnesses and replay samples.
    public static string Render(object?[] values) => "(" + string.Join(", ", values.Select(v => v switch
    {
        null => "null",
        string s => "\"" + string.Concat(s.Select(c => c < 32 || c > 126 ? $"\\u{(int)c:x4}" : c.ToString())) + "\"",
        Array a => "[" + string.Join(", ", a.Cast<object?>().Select(e => Convert.ToString(e, CultureInfo.InvariantCulture))) + "]",
        _ => Convert.ToString(v, CultureInfo.InvariantCulture),
    })) + ")";

    private bool TryInvoke(string method, object?[] args, out object? value)
    {
        try { value = _type.GetMethod(method)!.Invoke(null, args); return true; }
        catch (TargetInvocationException) { value = null; return false; }
    }

    internal static T WithCulture<T>(Func<T> action)
    {
        var previous = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(IndependentOracle.RuntimeCulture); return action(); }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    public void Dispose() => _context.Unload();
}

// R1-O2: the baseline's forced-guard emission, compiled against the baseline's own Calor.Runtime.dll and run on replay inputs. A replay driver is added next to
// the emitted code (inside the emitted module class when it exists, so private Probe functions stay callable); the emitted code is not edited.
internal static class EmittedReplay
{
    private static readonly IReadOnlyList<MetadataReference> Framework = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
        .Where(p => !Path.GetFileName(p).StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal)).Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList();

    public static JsonObject Run(string emitted, string runtimeDll, IReadOnlyList<ParameterInfo> parameters, string? replay,
        IReadOnlyList<(string Rendered, object?[] Input, string O1)> inputs, string claimKind, string? obligationKind)
    {
        // Parameters typed object or declared by the oracle (e.g. its own Box) cannot be passed to the emitted Probe
        // directly: they are copied by member name into the emitted type and bound dynamically; an input the emitted
        // signature cannot accept is recorded, never coerced.
        static bool Bcl(Type t) => t.IsArray ? Bcl(t.GetElementType()!) : t.IsPrimitive || t == typeof(string);
        var signature = string.Join(", ", parameters.Select(p => $"{(Bcl(p.ParameterType) ? CSharpName(p.ParameterType) : "object?")} {p.Name}"));
        var call = replay ?? $"Probe({string.Join(", ", parameters.Select(p => Bcl(p.ParameterType) ? p.Name : $"(dynamic?)__R1Copy({p.Name})"))})";
        const string Copy = "static object? __R1Copy(object? v) { if (v == null || v.GetType().Assembly == typeof(object).Assembly) return v; "
            + "var t = System.Linq.Enumerable.First(System.Reflection.Assembly.GetExecutingAssembly().GetTypes(), x => x.Name == v.GetType().Name); var o = System.Activator.CreateInstance(t)!; "
            + "foreach (var f in v.GetType().GetFields()) { var m = t.GetField(f.Name); if (m != null) m.SetValue(o, f.GetValue(v)); else t.GetProperty(f.Name)!.SetValue(o, f.GetValue(v)); } return o; }";
        var driver = $"public static object? __R1Run({signature}) {{ return (object?)({call}); }} {Copy}";
        var at = emitted.IndexOf("public static class R1CaseModule", StringComparison.Ordinal);
        var source = at >= 0 ? emitted.Insert(emitted.IndexOf('{', at) + 1, "\n" + driver + "\n")
            : emitted + "\nnamespace R1Case { public static class R1CaseReplay { " + driver + " } }\n";
        var record = new JsonObject { ["driver"] = driver };
        var (image, errors) = Roslyn.Emit(source, [.. Framework, MetadataReference.CreateFromFile(runtimeDll)], checkOverflow: false);
        if (image == null) { record["status"] = "not-run-compile-error"; record["errors"] = errors; return record; }
        var context = new ReplayContext(runtimeDll);
        try
        {
            var method = context.LoadFromStream(image).GetTypes().Select(t => t.GetMethod("__R1Run", BindingFlags.Public | BindingFlags.Static)).First(m => m != null)!;
            var runs = new JsonArray();
            foreach (var (rendered, input, o1) in inputs)
            {
                string o2;
                string? exception = null;
                try { OracleProgram.WithCulture(() => method.Invoke(null, input.Select(v => v is Array a ? a.Clone() : v).ToArray())); o2 = "returned"; }
                catch (TargetInvocationException ex)
                {
                    var inner = ex.InnerException!;
                    exception = inner.GetType().FullName + ": " + inner.Message;
                    o2 = inner.GetType().FullName == "Microsoft.CSharp.RuntimeBinder.RuntimeBinderException" ? "not-run-input-not-representable"
                        : GuardFired(inner, claimKind, obligationKind) ? "guard-threw" : "o2-other-exception";
                }
                runs.Add(new JsonObject { ["input"] = rendered, ["o1"] = o1, ["o2"] = o2, ["exception"] = exception, ["divergence"] = (o1 == "violated" && o2 == "returned") || (o1 == "holds" && o2 == "guard-threw") });
            }
            (record["status"], record["runs"]) = ("run", runs);
        }
        finally { context.Unload(); }
        return record;
    }

    // The registered O2 exception per claim site.
    private static bool GuardFired(Exception e, string claimKind, string? obligationKind) => claimKind is "postcondition" or "guard-emission"
        ? e.GetType().FullName == "Calor.Runtime.ContractViolationException" && e.GetType().GetProperty("Kind")?.GetValue(e)?.ToString() == "Ensures"
        : obligationKind switch
        {
            "ProofObligation" => e is InvalidOperationException && e.Message.Contains("Proof obligation", StringComparison.Ordinal),
            "IndexBounds" => e is IndexOutOfRangeException,
            "RefinementEntry" or "RefinementReturn" or "Subtype" => e is ArgumentOutOfRangeException,
            _ => false,
        };

    private static string CSharpName(Type t) => t.IsArray ? CSharpName(t.GetElementType()!) + "[]" : Type.GetTypeCode(t) switch
    {
        TypeCode.Boolean => "bool", TypeCode.SByte => "sbyte", TypeCode.Byte => "byte", TypeCode.Int16 => "short", TypeCode.UInt16 => "ushort", TypeCode.Int32 => "int",
        TypeCode.UInt32 => "uint", TypeCode.Int64 => "long", TypeCode.UInt64 => "ulong", TypeCode.String => "string", _ => t.FullName!,
    };

    private sealed class ReplayContext(string runtimeDll) : AssemblyLoadContext("R1O2", isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName name) => name.Name == "Calor.Runtime" ? LoadFromAssemblyPath(runtimeDll) : null;
    }
}

internal static class Hashing
{
    public static string Sha256File(string path) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));

    public static string Sha256Text(string text) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(new UTF8Encoding(false).GetBytes(text)));
}
