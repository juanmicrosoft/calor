using System.Text.Json.Nodes;

namespace Calor.Compiler.Tests.SoundnessRegistration;

/// <summary>
/// #1419 (0.24 R1) — structural validator for the soundness-sweep registration. It checks that the
/// registration is complete and consistent with the frozen #1407 contract before #1311 may run.
/// Every rule fails closed: a missing field is a violation, never a default.
/// </summary>
internal static class SoundnessRegistrationValidator
{
    internal sealed record Violation(string Code, string Message);

    internal static readonly string[] Classifications = ["modeled", "assumed", "unsupported-refused", "not-investigated"];

    internal static readonly string?[] ObligationKinds =
        ["ProofObligation", "RefinementEntry", "RefinementReturn", "Subtype", "IndexBounds"];

    internal static readonly string[] RequiredSamplingDimensions =
        ["aliases", "mutation", "early-returns", "caches", "numeric-widths", "arrays", "strings", "quantifiers", "guard-elision"];

    internal static IReadOnlyList<Violation> Validate(
        JsonNode registration, JsonNode templates, JsonNode contract, IReadOnlyDictionary<string, JsonNode> manifests,
        IReadOnlyList<SweepCaseGenerator.Case> cases)
    {
        var v = new List<Violation>();
        void Add(string code, string message) => v.Add(new Violation(code, message));

        // R015 lifecycle: frozen at merge, no decision-bearing inspection before the freeze.
        if (Str(registration, "status") != "FROZEN-AT-MERGE")
            Add("R015", "status must be FROZEN-AT-MERGE");
        if (registration["freeze"]?["decisionBearingInspectionBeforeFreeze"]?.GetValue<bool>() != false)
            Add("R015", "freeze.decisionBearingInspectionBeforeFreeze must be explicitly false");
        if (Str(registration["contract"], "contractVersion") != Str(contract, "contractVersion"))
            Add("R006", "registration names a different contract version than contract.json");

        // R006 baselines: B1 and N1 commits equal the contract's; both present; distinct.
        var contractBaselines = contract["baselines"]!.AsArray().ToDictionary(b => Str(b, "id")!, b => Str(b, "commit"));
        var baselines = registration["baselines"]?.AsArray() ?? new JsonArray();
        foreach (var id in new[] { "B1", "N1" })
        {
            var b = baselines.FirstOrDefault(x => Str(x, "id") == id);
            if (b == null || Str(b, "commit") != contractBaselines.GetValueOrDefault(id))
                Add("R006", $"baseline {id} missing or its commit differs from contract.json");
            if (b != null && b["binary"]?["procedure"] == null)
                Add("R006", $"baseline {id} has no binary acquisition procedure");
        }
        if (baselines.FirstOrDefault(x => Str(x, "id") == "N1")?["binary"]?["nupkgSha256"] is not JsonValue sha
            || sha.GetValue<string>().Length != 64)
            Add("R006", "N1 binary must pin the nupkg SHA-256");

        // R007 no pooling: distinct result paths per baseline, distinct manifest files.
        var paths = registration["noPooling"]?["resultPaths"]?.AsObject();
        if (paths == null || paths.Select(p => p.Value?.GetValue<string>()).Distinct().Count() != paths.Count
            || !paths.ContainsKey("B1") || !paths.ContainsKey("N1"))
            Add("R007", "B1 and N1 need distinct result paths");

        // R008 disposition vocabulary equals the contract's.
        var vocab = registration["dispositionHandoff"]?["vocabulary"]?.AsArray().Select(x => x!.GetValue<string>()).ToList();
        var contractVocab = contract["findingDispositions"]!.AsArray().Select(x => x!.GetValue<string>()).ToList();
        if (vocab == null || !vocab.SequenceEqual(contractVocab))
            Add("R008", "disposition vocabulary differs from contract.json findingDispositions");

        // Rows.
        var rows = registration["denominator"]?["rows"]?.AsArray() ?? new JsonArray();
        var rowIds = new HashSet<string>(StringComparer.Ordinal);
        var catalog = registration["denominator"]?["entryPointCatalog"]?.AsObject();
        foreach (var row in rows)
        {
            var id = Str(row, "id") ?? "";
            if (!rowIds.Add(id))
                Add("R001", $"duplicate row {id}");
            var cls = Str(row, "classification");
            var caseCount = row!["casesPerBaseline"]?.GetValue<int>() ?? -1;
            var critical = row["releaseCritical"]?.GetValue<bool>();
            if (cls == null || !Classifications.Contains(cls))
                Add("R001", $"row {id} has unknown classification '{cls}'");
            if (critical == null)
                Add("R001", $"row {id} has no releaseCritical mark");
            if (cls == "not-investigated" && (caseCount != 0 || critical == true))
                Add("R002", $"not-investigated row {id} must have zero cases and not be release-critical");
            if (cls != "not-investigated" && caseCount <= 0)
                Add("R003", $"investigated row {id} has no cases");
            foreach (var ep in row["entryPoints"]?.AsArray() ?? new JsonArray())
                if (catalog == null || !catalog.ContainsKey(ep!.GetValue<string>()))
                    Add("R003", $"row {id} names unknown entry point {ep}");
        }

        // R004 templates agree with rows; R014 expected outcomes only on controls; R013 oracle independence.
        var perRow = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var t in templates["templates"]!.AsArray())
        {
            var row = Str(t, "row")!;
            var instances = t!["instances"]!.GetValue<int>();
            perRow[row] = perRow.GetValueOrDefault(row) + instances;
            if (!rowIds.Contains(row))
                Add("R004", $"template {Str(t, "id")} names unregistered row {row}");
            if (Str(t, "claimSite") == "obligation" && !ObligationKinds.Contains(Str(t, "obligationKind")))
                Add("R004", $"obligation template {Str(t, "id")} must name the claimed obligationKind");
            var isControl = row.StartsWith("CTRL-", StringComparison.Ordinal);
            if (isControl != (t["expectedOutcome"] != null))
                Add("R014", $"template {Str(t, "id")}: expectedOutcome must be present exactly on control templates");
            if (t["oracle"]!.ToJsonString().Contains("Calor.", StringComparison.Ordinal))
                Add("R013", $"template {Str(t, "id")} oracle text references a Calor namespace");
        }
        foreach (var row in rows)
            if (perRow.GetValueOrDefault(Str(row, "id")!) != row!["casesPerBaseline"]!.GetValue<int>())
                Add("R004", $"row {Str(row, "id")} casesPerBaseline differs from its templates' instances");

        // R005 budget: allocation sums match rows and stay within the contract ceiling.
        var ceiling = contract["authorityCapacity"]!["capacity"]!["ceilings"]!.AsArray()
            .Single(c => Str(c, "id") == "s1-generated-cases")!["value"]!.GetValue<int>();
        var alloc = registration["budget"]?["allocation"];
        var sweep = rows.Where(r => Str(r, "family") != "control").Sum(r => r!["casesPerBaseline"]!.GetValue<int>());
        var controls = rows.Where(r => Str(r, "family") == "control").Sum(r => r!["casesPerBaseline"]!.GetValue<int>());
        if (alloc == null
            || alloc["B1"]?["sweep"]?.GetValue<int>() != sweep || alloc["N1"]?["sweep"]?.GetValue<int>() != sweep
            || alloc["B1"]?["controls"]?.GetValue<int>() != controls || alloc["N1"]?["controls"]?.GetValue<int>() != controls
            || alloc["total"]?.GetValue<int>() is not int total || total > ceiling
            || total != 2 * (sweep + controls) + (alloc["P845"]?["controls"]?.GetValue<int>() ?? 0)
                + (alloc["minimizationReserve"]?.GetValue<int>() ?? 0))
            Add("R005", $"case allocation is inconsistent or exceeds the {ceiling}-case ceiling");

        // R009 sampling dimensions required by #1419 each reach at least one row with cases.
        foreach (var dim in RequiredSamplingDimensions)
            if (!rows.Any(r => r!["casesPerBaseline"]!.GetValue<int>() > 0
                    && r["samplingDimensions"]!.AsArray().Any(d => d!.GetValue<string>() == dim)))
                Add("R009", $"sampling dimension '{dim}' reaches no row with cases");

        // R010 every frozen whitelist member is mapped to rows whose GENERATED cases carry its token
        // (template options alone are not coverage: a random hole may never render an option).
        var calorText = cases.GroupBy(c => c.RowId)
            .ToDictionary(g => g.Key, g => string.Join("\n", g.Select(c => c.CalorSource + c.CalorPrimeSource)), StringComparer.Ordinal);
        var map = registration["denominator"]?["whitelistCoverageMap"]?.AsArray() ?? new JsonArray();
        foreach (var line in registration["denominator"]?["frozenWhitelist"]?.AsArray() ?? new JsonArray())
        {
            var text = line!.GetValue<string>();
            var name = text[..text.IndexOf(':')];
            if (name == "string-comparison-modes" || name == "quantifier-bound-variable-types")
            {
                if (!map.Any(m => Str(m, "line") == name))
                    Add("R010", $"whitelist line '{name}' has no coverage entry");
                continue;
            }
            foreach (var member in text[(name.Length + 1)..].Split(',').Select(s => s.Trim().Split(' ')[0]))
                if (!map.Any(m => Str(m, "line") == name && Str(m, "member") == member))
                    Add("R010", $"whitelist member {name}/{member} has no coverage entry");
        }
        foreach (var entry in map)
        {
            var token = Str(entry, "token")!;
            if (!entry!["rows"]!.AsArray().Any(r => calorText.TryGetValue(r!.GetValue<string>(), out var text)
                    && text.Contains(token, StringComparison.Ordinal)))
                Add("R010", $"coverage entry {Str(entry, "line")}/{Str(entry, "member")} token '{token}' appears in none of its rows");
        }

        // R011 hypothesis H1 registered and not executed; R012 #845 retrospective control registered.
        var h1 = registration["hypotheses"]?.AsArray().FirstOrDefault(h => Str(h, "id") == "H1");
        if (h1 == null || Str(h1, "status") != "registered, not executed" || !rowIds.Contains(Str(h1, "row") ?? ""))
            Add("R011", "hypothesis H1 must be registered, unexecuted, and bound to a row");
        var retro = registration["controls"]?["retrospective845"];
        if (retro == null || Str(retro, "preFixParent") != "17d253dff964e37538a5f503d3744b576bd2fb52"
            || !rowIds.Contains("CTRL-RETRO-845"))
            Add("R012", "#845 retrospective discriminating control must be registered with its pre-fix parent");
        foreach (var t in templates["templates"]!.AsArray().Where(t => Str(t, "row")!.StartsWith("CTRL-RETRO-", StringComparison.Ordinal)))
            if (t!["guards"] is not JsonArray guards || guards.Count == 0 || guards.Any(g => !rowIds.Contains(g!.GetValue<string>())))
                Add("R012", $"retrospective control {Str(t, "id")} must name the registered rows it guards");

        // R016 manifests: one per investigated baseline, same rows, baseline-prefixed ids, own paths.
        foreach (var id in new[] { "B1", "N1" })
        {
            if (!manifests.TryGetValue(id, out var m))
            {
                Add("R016", $"manifest for {id} missing");
                continue;
            }
            var mRows = m["rows"]!.AsArray();
            if (Str(m, "baseline") != id || Str(m, "commit") != contractBaselines.GetValueOrDefault(id)
                || mRows.Count != rows.Count
                || mRows.Any(r => !Str(r, "id")!.StartsWith(id + ":", StringComparison.Ordinal))
                || mRows.Any(r => !Str(r, "resultPath")!.StartsWith(paths?[id]?.GetValue<string>() ?? "\0", StringComparison.Ordinal))
                || mRows.Zip(rows).Any(p => Str(p.First, "row") != Str(p.Second, "id")
                    || p.First!["caseIds"]!.AsArray().Count != p.Second!["casesPerBaseline"]!.GetValue<int>()))
                Add("R016", $"manifest for {id} does not match the registration rows, commit, or result path");
        }
        return v;
    }

    private static string? Str(JsonNode? node, string key) => node?[key] is JsonValue value ? value.GetValue<string>() : null;
}
