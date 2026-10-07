using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Calor.Compiler.Tests.EvidenceContract;

namespace Calor.Compiler.Tests.InteropScope;

/// <summary>
/// #1427 (0.25 D1): validates the ref/out contract registry (<c>contract.json</c>) and fails closed on
/// registry gaps (codes D001-D010, documented in docs/plans/v0.25-refout-contract.md §12). It checks the
/// registry's shape and its links to R0 and the consumed revisions; the executable checks of the cases
/// themselves are in <see cref="RefOutContractCaseTests"/>.
/// </summary>
internal static class RefOutContractValidator
{
    public const string ContractPath = "docs/plans/evidence/v0.25-d1-1427/contract.json";
    public const string R0ScopePath = InteropScopeValidator.PacketDir + "/scope.json";

    /// <summary>Consumed revisions frozen by this contract (#1379's scoping doc, R-OBL and its residuals).</summary>
    public const string NullabilityRevision = "d0dc0f8bf3ea400b1d7d16780a98d9ff3341abc7";
    public const string NullabilityBlob = "cc5fcf955d394bfd20b1321a89cba8a9ba6137ab";
    public const string RoblMerge = "ba2e31cb6373bcc10aeaa04e3f94e9067d0b8c06";
    public const string RoblResidualsMerge = "9f5dfd763f90aebe16e4e4783f4c1703e464c8d3";

    /// <summary>Every area the issue requires a decision for. Each needs a rule and positive and negative cases.</summary>
    public static readonly string[] Areas =
    [
        "syntax", "call-shape", "lvalue", "copy", "aliasing", "evaluation-order", "definite-assignment",
        "exception", "declaration", "representation", "nullability", "effects", "analysis", "unsupported", "migration",
    ];

    /// <summary>Areas that need a case but not one of each polarity (unsupported shapes are refusals by definition).</summary>
    private static readonly HashSet<string> SinglePolarityAreas = ["definite-assignment", "exception", "representation", "migration", "unsupported"];

    /// <summary>Operand and call shapes that must each have at least one registered case.</summary>
    public static readonly string[] RequiredShapes =
    [
        "alias-narrowing", "aliased-parameter", "array-element", "bare-field", "computed-expression", "constructor-argument",
        "delegate-by-ref", "discard", "discard-collision", "expression-call", "flow-attribute", "generic-type-payload", "immutable-binding",
        "conditional-ref", "heap-alias-narrowing", "in-argument", "in-array-element", "in-extension", "in-omitted", "in-parameter", "indexer-argument", "literal", "local", "loop-condition", "loop-variable",
        "method-generic", "missing-modifier", "modifier-text", "named-argument", "narrowing", "nullable-annotation",
        "out-var", "out-var-loop-header", "overload-by-modifier", "parameter", "preserved-csharp", "property", "readonly-field", "readonly-field-ctor",
        "receiver-field", "ref-extension", "ref-local", "same-storage-twice", "short-circuit", "span-indexer",
        "static-field", "struct-element", "struct-element-field", "struct-local-field", "wrong-modifier",
    ];

    /// <summary>R0 F1 rows (#1426 scope 1.0.0) this contract must map.</summary>
    public static readonly string[] R0FixtureRows =
        ["F1-REFOUT-01", "F1-REFOUT-02", "F1-REFOUT-03", "F1-REFOUT-04", "F1-REFOUT-05", "F1-REFOUT-06", "F1-REFOUT-07", "F1-REFOUT-08"];
    public static readonly string[] R0AnalysisRows = ["F1-REFOUT-A1", "F1-REFOUT-A2"];

    /// <summary>Areas whose by-reference behavior is observable at run time; their native C# cases need a mutant.</summary>
    private static readonly HashSet<string> WitnessAreas = ["copy", "aliasing", "evaluation-order", "exception", "call-shape", "declaration", "nullability", "migration"];

    private static readonly string[] Forbidden = ["independently reviewed", "independently verified"];
    private static readonly Regex RuleId = new(@"^RO-[A-Z]+-\d+$", RegexOptions.Compiled);
    private static readonly Regex CaseId = new(@"^D1-[A-Z0-9]+-\d{2}$", RegexOptions.Compiled);
    private static readonly Regex CalorCode = new(@"^Calor\d{4}$", RegexOptions.Compiled);
    private static readonly Regex SemVer = new(@"^\d+\.\d+\.\d+$", RegexOptions.Compiled);

    /// <summary>The compiler's own constant for a diagnostic symbol (DiagnosticCode.&lt;symbol&gt;), or null.</summary>
    public static string? CompilerConstant(string symbol)
        => typeof(Calor.Compiler.Diagnostics.DiagnosticCode)
            .GetField(symbol, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null) as string;

    public static IReadOnlyList<ContractViolation> Validate(JsonNode contract, JsonNode r0Scope, Func<string, byte[]?> readRepoFile,
        Func<string, string?>? compilerConstant = null)
    {
        compilerConstant ??= CompilerConstant;
        var v = new List<ContractViolation>();
        void Fail(string code, string subject, string message) => v.Add(new ContractViolation(code, subject, message));
        var rules = Arr(contract["rules"]).ToList();
        var cases = Arr(contract["cases"]).ToList();
        var vocab = contract["vocabularies"];
        bool In(string field, string? value) => value != null && Arr(vocab?[field]).Any(x => Str(x) == value);
        var docPath = Str(contract["document"]);
        var doc = docPath is { Length: > 0 } && readRepoFile(docPath) is { } bytes ? Encoding.UTF8.GetString(bytes) : null;

        // D001: header and consumed revisions.
        if (Str(contract["schema"]) != "calor.refout-contract/1") Fail("D001", "schema", "unknown schema");
        if (Str(contract["contractVersion"]) is not { } version || !SemVer.IsMatch(version)) Fail("D001", "contractVersion", "not a semantic version");
        if (Str(contract["status"]) is not ("PROPOSED" or "FROZEN")) Fail("D001", "status", "status must be PROPOSED or FROZEN");
        if (Int(contract["issue"]) != 1427 || Int(contract["epic"]) != 1425 || Int(contract["consumer"]) != 943)
            Fail("D001", "issue", "contract must be #1427 under epic #1425, consumed by #943");
        if (doc == null) Fail("D001", "document", "contract document missing");
        var consumes = Arr(contract["consumes"]).ToDictionary(c => Str(c?["id"]) ?? "", c => c!);
        if (!consumes.TryGetValue("nullability-0.22", out var nullability)
            || Str(nullability["revision"]) != NullabilityRevision || Str(nullability["blob"]) != NullabilityBlob
            || Str(nullability["path"]) is not { } nullPath || readRepoFile(nullPath) is null)
            Fail("D001", "nullability-0.22", $"must consume the 0.22 scoping doc at {NullabilityRevision}");
        if (!consumes.TryGetValue("r-obl", out var robl) || Str(robl["mergeCommit"]) != RoblMerge || Str(robl["residualsMergeCommit"]) != RoblResidualsMerge)
            Fail("D001", "r-obl", "must consume R-OBL (#1496) and its residuals (#1503) at their merge commits");
        if (!consumes.TryGetValue("r0-scope", out var r0) || Str(r0["scopeVersion"]) != Str(r0Scope["scopeVersion"]))
            Fail("D001", "r0-scope", "consumed R0 scope version differs from scope.json");

        // D002: rules.
        var ruleIds = new HashSet<string>();
        foreach (var rule in rules)
        {
            var id = Str(rule?["id"]) ?? "";
            if (!RuleId.IsMatch(id) || !ruleIds.Add(id)) Fail("D002", id, "rule id malformed or duplicated");
            if (!Areas.Contains(Str(rule?["area"]))) Fail("D002", id, "rule area outside the frozen list");
            if (string.IsNullOrWhiteSpace(Str(rule?["text"]))) Fail("D002", id, "rule has no text");
            if (doc != null && !doc.Contains(id, StringComparison.Ordinal)) Fail("D002", id, "rule not stated in the document");
        }
        if (!Arr(vocab?["area"]).Select(Str).SequenceEqual(Areas)) Fail("D002", "vocabularies.area", "area vocabulary differs from the frozen list");
        foreach (var area in Areas.Where(a => rules.All(r => Str(r?["area"]) != a)))
            Fail("D002", area, "required area has no rule");
        var cited = cases.SelectMany(c => Arr(c?["rules"]).Select(Str)).ToHashSet();
        foreach (var id in ruleIds.Where(id => !cited.Contains(id)))
            Fail("D002", id, "rule has no registered case");

        // D003: cases.
        var caseIds = new HashSet<string>();
        var preludes = contract["preludes"]?.AsObject();
        foreach (var c in cases)
        {
            var id = Str(c?["id"]) ?? "";
            if (!CaseId.IsMatch(id) || !caseIds.Add(id)) Fail("D003", id, "case id malformed or duplicated");
            if (!In("area", Str(c?["area"]))) Fail("D003", id, "area outside the vocabulary");
            if (!In("polarity", Str(c?["polarity"]))) Fail("D003", id, "polarity outside the vocabulary");
            if (!RequiredShapes.Contains(Str(c?["shape"]))) Fail("D003", id, "shape outside the frozen list");
            var caseRules = Arr(c?["rules"]).Select(Str).ToList();
            if (caseRules.Count == 0 || caseRules.Any(r => r == null || !ruleIds.Contains(r))) Fail("D003", id, "case cites no rule or an unknown rule");
            if (doc != null && !doc.Contains(id, StringComparison.Ordinal)) Fail("D003", id, "case not listed in the document");
            switch (Str(c?["kind"]))
            {
                case "calor":
                    if (Str(c?["prelude"]) is not { } p || preludes?[p] is null) Fail("D003", id, "unknown prelude");
                    if (Arr(c?["source"]).Count() == 0) Fail("D003", id, "no source");
                    ValidateCalorExpectation(c!, id, In, Fail, SymbolCodes(contract));
                    break;
                case "csharp":
                case "r0-fixture":
                    if (!In("conversion", Str(c?["conversion"]))) Fail("D003", id, "conversion outcome outside the vocabulary");
                    if (Str(c?["expectedOutput"]) is null) Fail("D003", id, "no expected Probe.Run() output");
                    var source = SourceText(c!, readRepoFile);
                    if (source == null) Fail("D003", id, "source or fixture missing");
                    ValidateMutants(c!, id, source, In, Fail);
                    break;
                case "spec":
                    if (string.IsNullOrWhiteSpace(Str(c?["witness"]))) Fail("D003", id, "spec case without a witness description");
                    break;
                default:
                    Fail("D003", id, "kind outside the vocabulary");
                    break;
            }
        }

        // D004: coverage of shapes and polarity per area.
        if (!Arr(contract["requiredShapes"]).Select(Str).OrderBy(s => s, StringComparer.Ordinal)
                .SequenceEqual(RequiredShapes.OrderBy(s => s, StringComparer.Ordinal)))
            Fail("D004", "requiredShapes", "required shape list differs from the frozen list");
        foreach (var shape in RequiredShapes.Where(s => cases.All(c => Str(c?["shape"]) != s)))
            Fail("D004", shape, "required shape has no case");
        foreach (var area in Areas)
        {
            // A C# witness's mutants are its negative half: each must change the output.
            var inArea = cases.Where(c => Str(c?["area"]) == area).ToList();
            var polarities = inArea.Select(c => Str(c?["polarity"])).ToHashSet();
            if (inArea.Any(c => Arr(c?["mutants"]).Any())) polarities.Add("negative");
            if (polarities.Count == 0) Fail("D004", area, "area has no case");
            else if (!SinglePolarityAreas.Contains(area) && polarities.Count < 2) Fail("D004", area, "area needs a positive and a negative case");
        }

        // D005: R0 linkage.
        var r0Cases = Arr(r0Scope["cases"]).ToDictionary(c => Str(c?["id"]) ?? "", c => c!);
        foreach (var row in R0FixtureRows)
        {
            var mapped = cases.Where(c => Str(c?["kind"]) == "r0-fixture" && Str(c?["r0Case"]) == row).ToList();
            if (mapped.Count != 1 || !r0Cases.TryGetValue(row, out var r0Case)) { Fail("D005", row, "R0 row must map to exactly one r0-fixture case"); continue; }
            var m = mapped[0]!;
            if (Str(m["fixture"]) != Str(r0Case["fixture"]) || Str(m["fixtureSha256"]) != Str(r0Case["fixtureSha256"]))
                Fail("D005", row, "fixture path or hash differs from R0");
            if (Str(m["conversion"]) != Str(r0Case["expected"])) Fail("D005", row, "expected conversion differs from R0's registered expectation");
            if (Str(m["fixture"]) is { } f && readRepoFile(f) is { } fb && Sha(fb) != Str(m["fixtureSha256"]))
                Fail("D005", row, "fixture bytes do not match the hash");
        }
        foreach (var row in R0AnalysisRows.Where(row => cases.All(c => !Arr(c?["r0Rows"]).Any(x => Str(x) == row))))
            Fail("D005", row, "R0 analysis row has no case");
        foreach (var c in cases)
        {
            foreach (var row in Arr(c?["r0Rows"]).Select(Str).Where(r => r == null || !R0AnalysisRows.Contains(r)))
                Fail("D005", Str(c?["id"]) ?? "", $"r0Rows names an unknown analysis row '{row}'");
        }

        // D006: native-admitting by-reference witnesses must be able to fail.
        foreach (var c in cases.Where(c => Str(c?["kind"]) is "csharp" or "r0-fixture"))
        {
            var id = Str(c?["id"]) ?? "";
            var admitsNative = Str(c?["conversion"])?.Contains("native", StringComparison.Ordinal) == true;
            var semantic = WitnessAreas.Contains(Str(c?["area"]) ?? "") && Str(c?["polarity"]) == "positive";
            // F1-REFOUT-02 is a declaration decision; D1-CS-14 carries its lowering witness.
            var exempt = Str(c?["kind"]) == "r0-fixture" && Str(c?["r0Case"]) == "F1-REFOUT-02";
            if (admitsNative && semantic && !exempt && !Arr(c?["mutants"]).Any())
                Fail("D006", id, "a case that admits native conversion needs a mutant (copy, dropped modifier, reorder or hoist) that changes the output");
        }

        // D007: refusals name explicit diagnostics.
        var symbols = Arr(contract["diagnostics"]).Select(d => Str(d?["symbol"])).ToHashSet();
        foreach (var d in Arr(contract["diagnostics"]))
        {
            if (Str(d?["range"]) is not { } range || !range.StartsWith("Calor02", StringComparison.Ordinal) || Int(d?["allocatedBy"]) != 943)
                Fail("D007", Str(d?["symbol"]) ?? "", "diagnostic symbol needs a Calor02xx range and #943 as allocator");
            if (d?["code"] is not null && !(Str(d["code"]) is { } allocated && Regex.IsMatch(allocated, "^Calor02\\d\\d$")
                    && Str(d["symbol"]) is { } sym && compilerConstant(sym) == allocated))
                Fail("D007", Str(d?["symbol"]) ?? "", "an allocated code must be Calor02xx and equal the compiler's DiagnosticCode.<symbol>");
        }
        foreach (var c in cases.Where(c => Str(c?["kind"]) == "calor" && Str(c?["expected"]?["outcome"]) == "rejected"))
        {
            var diag = Str(c?["expected"]?["diagnostic"]);
            if (diag == null || diag == "Calor1002" || !(CalorCode.IsMatch(diag) || symbols.Contains(diag)))
                Fail("D007", Str(c?["id"]) ?? "", "a refusal must name a Calor code or registered symbol; generated-C# failure (Calor1002) is not a refusal");
        }

        // D009: amendments.
        var amendments = Arr(contract["amendments"]).ToList();
        var versions = new List<Version> { new(1, 0, 0) };
        foreach (var a in amendments)
        {
            var av = Str(a?["version"]);
            if (av == null || !SemVer.IsMatch(av) || new Version(av) <= versions[^1]
                || !DateTime.TryParse(Str(a?["dateUtc"]), out _) || Int(a?["pr"]) <= 0 || string.IsNullOrWhiteSpace(Str(a?["justification"])))
                Fail("D009", av ?? "", "amendment needs an increasing version, UTC date, PR and justification");
            else versions.Add(new Version(av));
        }
        if (Str(contract["contractVersion"]) is { } cv && SemVer.IsMatch(cv) && new Version(cv) != versions[^1])
            Fail("D009", "contractVersion", "contractVersion must equal the latest amendment version (1.0.0 without amendments)");

        // D010: reduced independence; no independence claims.
        var text = (doc ?? "") + contract.ToJsonString();
        foreach (var phrase in Forbidden.Where(p => text.Contains(p, StringComparison.OrdinalIgnoreCase)))
            Fail("D010", phrase, "the reduced-independence deviation forbids this claim");
        return v;
    }

    /// <summary>Registered diagnostic symbols mapped to their allocated code, or to null before #943 allocates one.</summary>
    public static Dictionary<string, string?> SymbolCodes(JsonNode contract)
        => Arr(contract["diagnostics"]).Where(d => Str(d?["symbol"]) != null)
            .ToDictionary(d => Str(d!["symbol"])!, d => Str(d!["code"]));

    private static void ValidateCalorExpectation(JsonNode c, string id, Func<string, string?, bool> In, Action<string, string, string> Fail,
        IReadOnlyDictionary<string, string?> symbolCodes)
    {
        // D008: the recorded status must agree with the observation.
        var expected = c["expected"];
        var observed = c["observed"];
        var outcome = Str(expected?["outcome"]);
        if (!In("outcome", outcome)) { Fail("D003", id, "expected outcome outside the vocabulary"); return; }
        if (!In("status", Str(c["status"]))) { Fail("D003", id, "status outside the vocabulary"); return; }
        if (expected?["proof"] is { } p && !In("proof", Str(p))) Fail("D003", id, "proof expectation outside the vocabulary");
        if (expected?["proof"] != null && c["verify"]?.GetValue<bool>() != true) Fail("D003", id, "a proof expectation needs verify");
        if (expected?["proof"] != null && Str(c["observed"]?["proof"]) is not ("Pending" or "Discharged" or "Failed" or "Timeout" or "Boundary" or "Unsupported"))
            Fail("D003", id, "a proof expectation needs a recorded ObligationStatus observation");
        var errors = Arr(observed?["errors"]).Select(Str).ToList();
        bool meets = outcome == "accepted"
            ? errors.Count == 0 && ProofMeets(Str(expected?["proof"]), Str(observed?["proof"]))
            : Str(expected?["diagnostic"]) is { } diag
              && errors.Contains(symbolCodes.TryGetValue(diag, out var code) ? code ?? diag : diag);
        if (meets != (Str(c["status"]) == "holds"))
            Fail("D008", id, $"status '{Str(c["status"])}' disagrees with the observation");
    }

    private static bool ProofMeets(string? expected, string? observed) => expected switch
    {
        null => true,
        "discharged" => observed == "Discharged",
        "unsupported" => observed == "Unsupported",
        _ => false,
    };

    private static void ValidateMutants(JsonNode c, string id, string? source, Func<string, string?, bool> In, Action<string, string, string> Fail)
    {
        foreach (var m in Arr(c["mutants"]))
        {
            var find = Str(m?["find"]);
            if (!In("mutant", Str(m?["kind"])) || string.IsNullOrEmpty(find) || Str(m?["replace"]) is null || find == Str(m?["replace"]))
                Fail("D006", id, "mutant malformed");
            else if (source != null && Occurrences(source, find) != 1)
                Fail("D006", id, $"mutant text must occur exactly once: {find}");
        }
    }

    /// <summary>The C# source of a csharp or r0-fixture case, or null if missing.</summary>
    public static string? SourceText(JsonNode c, Func<string, byte[]?> readRepoFile)
    {
        if (Str(c["kind"]) == "csharp")
        {
            var lines = Arr(c["source"]).Select(Str).ToList();
            return lines.Count == 0 || lines.Any(l => l == null) ? null : string.Join("\n", lines);
        }
        var path = Str(c["fixture"]);
        return path != null && !path.Contains("..", StringComparison.Ordinal) && readRepoFile(path) is { } b ? Encoding.UTF8.GetString(b) : null;
    }

    private static int Occurrences(string text, string find)
    {
        var count = 0;
        for (var i = text.IndexOf(find, StringComparison.Ordinal); i >= 0; i = text.IndexOf(find, i + find.Length, StringComparison.Ordinal)) count++;
        return count;
    }

    /// <summary>
    /// SHA-256 over everything the contract freezes: rules, preludes, required shapes, diagnostics (without
    /// the allocated code) and every case field except `observed` and `status`, which #943 updates as
    /// behavior changes. Sources, mutants, expectations and notes are all sealed. The tests pin it.
    /// </summary>
    public static string Seal(JsonNode contract)
    {
        var frozen = new JsonObject
        {
            ["rules"] = contract["rules"]?.DeepClone(),
            ["preludes"] = contract["preludes"]?.DeepClone(),
            ["requiredShapes"] = contract["requiredShapes"]?.DeepClone(),
            ["diagnostics"] = contract["diagnostics"]?.DeepClone(),
            ["cases"] = contract["cases"]?.DeepClone(),
        };
        foreach (var d in Arr(frozen["diagnostics"]).OfType<JsonObject>()) d.Remove("code");
        foreach (var c in Arr(frozen["cases"]).OfType<JsonObject>())
        {
            c.Remove("observed");
            c.Remove("status");
        }
        return Sha(Encoding.UTF8.GetBytes(frozen.ToJsonString()));
    }

    public static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static IEnumerable<JsonNode?> Arr(JsonNode? node) => node is JsonArray a ? a : [];
    private static string? Str(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
    private static int Int(JsonNode? node) => node is JsonValue v && v.TryGetValue<int>(out var i) ? i : -1;
}
