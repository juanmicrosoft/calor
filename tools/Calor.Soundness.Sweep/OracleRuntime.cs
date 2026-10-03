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

/// <summary>
/// Executes a case's registered oracle program on specific inputs, for two registered uses that
/// IndependentOracle.Evaluate (the O1 verdict, used unchanged) does not expose: recovering the input
/// tuples behind its rendered witness and replay sample (R1-O2), and replaying a solver counterexample
/// model under O1 semantics (spurious-refutation). Compilation mirrors IndependentOracle exactly
/// (BCL references only, the case's overflow mode, nullable enabled).
/// </summary>
internal sealed class OracleProgram : IDisposable
{
    private readonly AssemblyLoadContext _context = new("R1OracleRuntime", isCollectible: true);
    private readonly Type _type;

    public OracleProgram(string oracleSource)
    {
        var tree = CSharpSyntaxTree.ParseText(oracleSource, new CSharpParseOptions(LanguageVersion.CSharp14));
        var checkedMode = !oracleSource.Contains("public const bool Checked = false;", StringComparison.Ordinal);
        var compilation = CSharpCompilation.Create("R1OracleRuntime_" + Guid.NewGuid().ToString("N"), [tree],
            IndependentOracle.BclReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, checkOverflow: checkedMode,
                nullableContextOptions: NullableContextOptions.Enable));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        if (!emit.Success)
            throw new InvalidOperationException("oracle does not compile: " + string.Join("; ", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        stream.Position = 0;
        _type = _context.LoadFromStream(stream).GetType("R1Oracle")!;
    }

    /// <summary>Oracle domain parameters (names and CLR types), from the registered Hyp signature.</summary>
    public IReadOnlyList<ParameterInfo> Parameters => _type.GetMethod("Hyp")!.GetParameters();

    public IEnumerable<object?[]> Inputs() => (IEnumerable<object?[]>)_type.GetMethod("Inputs")!.Invoke(null, null)!;

    /// <summary>O1 semantics on one input: excluded (Hyp false/throws or Body throws), holds, or violated.</summary>
    public string PointVerdict(object?[] input)
    {
        return WithCulture(() =>
        {
            if (!TryInvoke("HypO", [input], out var hyp))
                return "excluded-hyp-threw";
            if (!(bool)hyp!)
                return "excluded-hyp-false";
            object? result = null;
            if ((bool)_type.GetProperty("HasBody")!.GetValue(null)! && !TryInvoke("BodyO", [input], out result))
                return "excluded-body-threw";
            var ok = TryInvoke("PropO", [input, result], out var prop);
            return ok && (bool)prop! ? "holds" : "violated";
        });
    }

    /// <summary>Recovers the input tuples whose rendering (IndependentOracle's format) is in <paramref name="rendered"/>.</summary>
    public Dictionary<string, object?[]> Recover(IReadOnlyCollection<string> rendered)
    {
        var found = new Dictionary<string, object?[]>(StringComparer.Ordinal);
        if (rendered.Count == 0)
            return found;
        foreach (var input in Inputs())
        {
            var text = Render(input);
            if (rendered.Contains(text) && !found.ContainsKey(text))
                found[text] = input;
            if (found.Count == rendered.Count)
                break;
        }
        return found;
    }

    /// <summary>Builds an oracle input tuple from a solver model, or null when the model is not replayable.</summary>
    public object?[]? FromModel(IReadOnlyDictionary<string, string> bindings)
    {
        var values = new List<object?>();
        foreach (var p in Parameters)
        {
            if (!bindings.TryGetValue(p.Name!, out var raw) || !TryConvert(raw, p.ParameterType, out var value))
                return null;
            values.Add(value);
        }
        return [.. values];
    }

    private static bool TryConvert(string raw, Type type, out object? value)
    {
        value = null;
        raw = raw.Trim();
        if (type == typeof(string))
            return TryParseZ3String(raw, out value);
        if (type == typeof(bool))
        {
            if (raw is "true" or "false") { value = raw == "true"; return true; }
            return false;
        }
        var widths = new Dictionary<Type, (int Bits, bool Signed)>
        {
            [typeof(sbyte)] = (8, true), [typeof(short)] = (16, true), [typeof(int)] = (32, true), [typeof(long)] = (64, true),
            [typeof(byte)] = (8, false), [typeof(ushort)] = (16, false), [typeof(uint)] = (32, false), [typeof(ulong)] = (64, false),
        };
        if (!widths.TryGetValue(type, out var w))
            return false;
        BigInteger n;
        if (raw.StartsWith("#x", StringComparison.Ordinal))
            n = BigInteger.Parse("0" + raw[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        else if (raw.StartsWith("#b", StringComparison.Ordinal))
            n = raw[2..].Aggregate(BigInteger.Zero, (acc, c) => acc * 2 + (c - '0'));
        else if (!BigInteger.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out n))
            return false;
        var modulus = BigInteger.One << w.Bits;
        if (n < 0) n += modulus;
        if (n < 0 || n >= modulus) return false;
        if (w.Signed && n >= modulus / 2) n -= modulus;
        value = Convert.ChangeType(n.ToString(CultureInfo.InvariantCulture), type, CultureInfo.InvariantCulture);
        return true;
    }

    /// <summary>An SMT-LIB 2.6 string literal as Z3 prints it ("" for a quote, \u{hex} for a code point).</summary>
    private static bool TryParseZ3String(string raw, out object? value)
    {
        value = null;
        if (raw.Length < 2 || raw[0] != '"' || raw[^1] != '"')
            return false;
        var body = raw[1..^1];
        var sb = new StringBuilder();
        for (var i = 0; i < body.Length; i++)
        {
            if (body[i] == '"' && i + 1 < body.Length && body[i + 1] == '"') { sb.Append('"'); i++; continue; }
            if (body[i] == '\\' && i + 2 < body.Length && body[i + 1] == 'u' && body[i + 2] == '{')
            {
                var close = body.IndexOf('}', i);
                if (close < 0 || !int.TryParse(body.AsSpan(i + 3, close - i - 3), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var cp))
                    return false;
                sb.Append(cp is >= 0xD800 and <= 0xDFFF ? ((char)cp).ToString() : char.ConvertFromUtf32(cp));
                i = close;
                continue;
            }
            sb.Append(body[i]);
        }
        value = sb.ToString();
        return true;
    }

    /// <summary>Byte-for-byte the rendering IndependentOracle uses for witnesses and replay samples.</summary>
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

/// <summary>
/// R1-O2: the baseline's forced-guard emission, compiled against the baseline's own Calor.Runtime.dll
/// and run on replay inputs. A replay driver is added next to the emitted code (inside the emitted
/// module class when it exists, so private Probe functions stay callable); the emitted code itself
/// is not edited.
/// </summary>
internal static class EmittedReplay
{
    private static readonly IReadOnlyList<MetadataReference> Framework =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(p => !Path.GetFileName(p).StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList();

    public static JsonObject Run(string emitted, string runtimeDll, IReadOnlyList<ParameterInfo> parameters, string? replay,
        IReadOnlyList<(string Rendered, object?[] Input, string O1)> inputs, string claimKind, string? obligationKind)
    {
        var record = new JsonObject();
        var signature = string.Join(", ", parameters.Select(p => $"{CSharpName(p.ParameterType)} {p.Name}"));
        var call = replay ?? $"Probe({string.Join(", ", parameters.Select(p => p.Name))})";
        var driver = $"public static object? __R1Run({signature}) {{ return (object?)({call}); }}";
        string source;
        const string ModuleClass = "public static class R1CaseModule";
        var at = emitted.IndexOf(ModuleClass, StringComparison.Ordinal);
        if (at >= 0)
        {
            var brace = emitted.IndexOf('{', at);
            source = emitted[..(brace + 1)] + "\n" + driver + "\n" + emitted[(brace + 1)..];
        }
        else
            source = emitted + "\nnamespace R1Case { public static class R1CaseReplay { " + driver + " } }\n";
        record["driver"] = driver;

        var compilation = CSharpCompilation.Create("R1O2_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp14))],
            [.. Framework, MetadataReference.CreateFromFile(runtimeDll)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        if (!emit.Success)
        {
            record["status"] = "not-run-compile-error";
            record["errors"] = string.Join("; ", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Take(5));
            return record;
        }
        stream.Position = 0;
        var context = new ReplayContext(runtimeDll);
        try
        {
            var assembly = context.LoadFromStream(stream);
            var method = assembly.GetTypes().Select(t => t.GetMethod("__R1Run", BindingFlags.Public | BindingFlags.Static)).First(m => m != null)!;
            var runs = new JsonArray();
            foreach (var (rendered, input, o1) in inputs)
            {
                var copy = input.Select(v => v is Array a ? a.Clone() : v).ToArray();
                string o2;
                string? exception = null;
                try
                {
                    OracleProgram.WithCulture(() => method.Invoke(null, copy));
                    o2 = "returned";
                }
                catch (TargetInvocationException ex)
                {
                    var inner = ex.InnerException!;
                    exception = inner.GetType().FullName + ": " + inner.Message;
                    o2 = GuardFired(inner, claimKind, obligationKind) ? "guard-threw" : "o2-other-exception";
                }
                var divergence = (o1 == "violated" && o2 == "returned") || (o1 == "holds" && o2 == "guard-threw");
                runs.Add(new JsonObject { ["input"] = rendered, ["o1"] = o1, ["o2"] = o2, ["exception"] = exception, ["divergence"] = divergence });
            }
            record["status"] = "run";
            record["runs"] = runs;
        }
        finally
        {
            context.Unload();
        }
        return record;
    }

    private static bool GuardFired(Exception e, string claimKind, string? obligationKind)
    {
        if (claimKind is "postcondition" or "guard-emission")
            return e.GetType().FullName == "Calor.Runtime.ContractViolationException"
                && e.GetType().GetProperty("Kind")?.GetValue(e)?.ToString() == "Ensures";
        return obligationKind switch
        {
            "ProofObligation" => e is InvalidOperationException && e.Message.Contains("Proof obligation", StringComparison.Ordinal),
            "IndexBounds" => e is IndexOutOfRangeException,
            "RefinementEntry" or "RefinementReturn" or "Subtype" => e is ArgumentOutOfRangeException,
            _ => false,
        };
    }

    internal static string CSharpName(Type t)
    {
        if (t.IsArray) return CSharpName(t.GetElementType()!) + "[]";
        return Type.GetTypeCode(t) switch
        {
            TypeCode.Boolean => "bool", TypeCode.SByte => "sbyte", TypeCode.Byte => "byte", TypeCode.Int16 => "short",
            TypeCode.UInt16 => "ushort", TypeCode.Int32 => "int", TypeCode.UInt32 => "uint", TypeCode.Int64 => "long",
            TypeCode.UInt64 => "ulong", TypeCode.String => "string", TypeCode.Double => "double", TypeCode.Single => "float",
            _ => t.FullName!,
        };
    }

    private sealed class ReplayContext(string runtimeDll) : AssemblyLoadContext("R1O2", isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName name) =>
            name.Name == "Calor.Runtime" ? LoadFromAssemblyPath(runtimeDll) : null;
    }
}

internal static class Hashing
{
    public static string Sha256File(string path) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));

    public static string Sha256Text(string text) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(new UTF8Encoding(false).GetBytes(text)));
}
