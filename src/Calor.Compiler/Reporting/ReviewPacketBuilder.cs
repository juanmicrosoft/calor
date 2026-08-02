using System.Text;
using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Verification;
using Calor.Compiler.Verification.Z3;

namespace Calor.Compiler.Reporting;

/// <summary>
/// Assembles the review packet (D-W3.3) from per-build analyses: contract
/// verification outcomes (seven-status vocabulary), assumption lists,
/// vacuity flags, per-module interop/permissive fractions, and caller impact
/// from the in-memory <see cref="CallGraphAnalysis"/>. No persisted index is
/// consulted — that scope delta vs the strategy's full packet is recorded in
/// the wedge plan register (§9).
/// </summary>
public sealed class ReviewPacketBuilder
{
    /// <summary>The #782 honesty line (W1 kickoff S honesty item).</summary>
    public const string RefinementHonestyNote =
        "Refinement types are NOT runtime-enforced (#782): refinement obligations are "
        + "compile-time analysis only — no runtime guard is emitted for them.";

    /// <summary>Builder options.</summary>
    public sealed record Options(
        bool PermissiveEffects = false,
        bool ContractChecksOff = false,
        uint VerificationTimeoutMs = 5000,
        IReadOnlyList<string>? ChangedDeclarations = null,
        IReadOnlyDictionary<string, IReadOnlyList<(int StartLine, int EndLine)>>? ChangedLineRanges = null,
        string? BaselineRef = null,
        string? ProjectDirectory = null);

    /// <summary>One input file: path plus source text.</summary>
    public sealed record InputFile(string Path, string Source);

    /// <summary>Build result: the packet, plus compile diagnostics per file.</summary>
    public sealed record Result(
        ReviewPacket Packet,
        IReadOnlyList<Diagnostic> CompileDiagnostics,
        bool HasCompileErrors,
        bool AnyRefinementTypes,
        IReadOnlyList<string> UnmatchedChangedDeclarations);

    private ReviewPacketBuilder() { }

    public static Result Build(IReadOnlyList<InputFile> files, Options options)
    {
        var packet = new ReviewPacket
        {
            GeneratedAt = DateTime.UtcNow.ToString("O"),
            BaselineRef = options.BaselineRef
        };

        if (options.PermissiveEffects)
        {
            packet.Waivers.Add(
                "WAIVER: compiled with --permissive-effects — unknown external calls are assumed pure; "
                + "the effect guarantee is VOID for these modules (strategy §5.2).");
        }
        if (options.ContractChecksOff)
        {
            packet.Waivers.Add(
                "WAIVER: compiled with contract checks OFF — no runtime contract checks are emitted; "
                + "the contract guarantee is VOID for these modules (strategy §5.2).");
        }
        packet.HonestyNotes.Add(RefinementHonestyNote);

        var compileDiagnostics = new List<Diagnostic>();
        var hasCompileErrors = false;
        var anyRefinements = false;
        var compiledModules = new List<(InputFile File, ModuleNode Module)>();

        // Multi-file invocations mirror the CLI driver's cross-module map
        // (G3/#809): without it, a cross-file internal call is an unknown
        // call to the per-file effect pass and — under the W2 default-on
        // strictness — fails the compile with Calor0410, dropping the file
        // from the packet entirely.
        var crossModuleMap = files.Count > 1 ? BuildCrossModuleFunctionMap(files) : null;

        foreach (var file in files)
        {
            var compileOptions = new CompilationOptions
            {
                VerifyContracts = true,
                VerificationTimeoutMs = options.VerificationTimeoutMs,
                EnforceEffects = true,
                UnknownCallPolicy = options.PermissiveEffects
                    ? Effects.UnknownCallPolicy.Permissive
                    : Effects.UnknownCallPolicy.Strict,
                ContractMode = options.ContractChecksOff ? ContractMode.Off : ContractMode.Debug,
                ProjectDirectory = options.ProjectDirectory
                    ?? System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(file.Path))
            };
            compileOptions.CrossModuleFunctionModules = crossModuleMap;

            var result = Program.Compile(file.Source, file.Path, compileOptions);

            foreach (var diagnostic in result.Diagnostics)
                compileDiagnostics.Add(diagnostic);

            if (result.HasErrors || result.Ast is null)
            {
                hasCompileErrors = true;
                continue;
            }

            var module = result.Ast;
            anyRefinements |= module.RefinementTypes.Count > 0;
            compiledModules.Add((file, module));

            CollectContracts(packet, file, module, compileOptions.VerificationResults);
            CollectModuleDisclosure(packet, file, module, options);
        }

        // Caller impact runs over the UNION of all files in the invocation —
        // a caller in one file of a declaration changed in another must be
        // found (review M2). Cross-invocation impact stays deferred with the
        // persisted index (wedge plan §9).
        var unmatched = CollectCallerImpact(packet, compiledModules, options);

        FinalizeSummary(packet);

        return new Result(packet, compileDiagnostics, hasCompileErrors, anyRefinements, unmatched);
    }

    /// <summary>
    /// Builds the bare-name → defining-module map from the in-memory sources,
    /// matching <c>CompilationDriver.BuildCrossModuleFunctionMap</c>'s rule
    /// exactly (public AND internal functions; ambiguous names dropped). The
    /// driver's helper reads from disk — the packet builder's inputs may not
    /// exist on disk (tests, editor buffers), so the map is built from the
    /// sources it already holds.
    /// </summary>
    private static IReadOnlyDictionary<string, string> BuildCrossModuleFunctionMap(
        IReadOnlyList<InputFile> files)
    {
        var byName = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var diagnostics = new DiagnosticBag();
            ModuleNode? module;
            try
            {
                var lexer = new Parsing.Lexer(file.Source, diagnostics);
                var parser = new Parsing.Parser(lexer.TokenizeAllForParser(), diagnostics);
                module = parser.Parse();
            }
            catch (Exception)
            {
                // Pre-parse is best-effort; the real compile reports errors.
                continue;
            }

            if (module is null || string.IsNullOrEmpty(module.Name) || module.Name == "_global")
                continue;

            foreach (var fn in module.Functions)
            {
                if (fn.Visibility is not (Visibility.Public or Visibility.Internal))
                    continue;
                if (!byName.TryGetValue(fn.Name, out var modules))
                    byName[fn.Name] = modules = [];
                if (!modules.Contains(module.Name))
                    modules.Add(module.Name);
            }
        }

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, modules) in byName)
        {
            if (modules.Count == 1)
                map[name] = modules[0];
        }
        return map;
    }

    // ------------------------------------------------------------------
    // Contracts — the seven-status vocabulary, unproven remainder first
    // ------------------------------------------------------------------

    private static void CollectContracts(
        ReviewPacket packet,
        InputFile file,
        ModuleNode module,
        ModuleVerificationResult? verification)
    {
        if (verification is null)
            return;

        // Function/method lookup so contract text can be sliced from source.
        var functionsById = new Dictionary<string, FunctionNode>(StringComparer.Ordinal);
        foreach (var fn in module.Functions)
            functionsById.TryAdd(fn.Id, fn);

        foreach (var fnResult in verification.Functions)
        {
            functionsById.TryGetValue(fnResult.FunctionId, out var fnNode);

            AddContracts(packet, file, module, fnResult.FunctionId, fnResult.FunctionName,
                "precondition", fnResult.PreconditionResults,
                i => SliceText(file.Source, fnNode?.Preconditions, i));
            AddContracts(packet, file, module, fnResult.FunctionId, fnResult.FunctionName,
                "postcondition", fnResult.PostconditionResults,
                i => SliceText(file.Source, fnNode?.Postconditions, i));
        }
    }

    private static void AddContracts(
        ReviewPacket packet,
        InputFile file,
        ModuleNode module,
        string functionId,
        string functionName,
        string kind,
        IReadOnlyList<Verification.Z3.ContractVerificationResult> results,
        Func<int, string?> textFor)
    {
        for (var i = 0; i < results.Count; i++)
        {
            var outcome = results[i].EffectiveOutcome;
            var record = new ContractRecord(
                File: System.IO.Path.GetFileName(file.Path),
                Module: module.Name,
                FunctionId: functionId,
                FunctionName: functionName,
                ContractKind: kind,
                Index: i,
                Text: textFor(i),
                Status: outcome.StatusName,
                Reason: outcome.Reason,
                Vacuous: outcome.IsVacuous,
                Assumptions: outcome.Assumptions,
                Counterexample: outcome.Counterexample?.Render());

            var cleanProven = outcome.Status == ProofStatus.Proven && !outcome.IsVacuous;
            if (cleanProven)
                packet.Proven.Add(record);
            else
                packet.UnprovenRemainder.Add(record);
        }
    }

    private static string? SliceText<TContract>(string source, IReadOnlyList<TContract>? contracts, int index)
        where TContract : AstNode
    {
        if (contracts is null || index < 0 || index >= contracts.Count)
            return null;
        var span = contracts[index].Span;
        if (span.Start < 0 || span.Length <= 0 || span.End > source.Length)
            return null;
        return source.Substring(span.Start, span.Length).Trim();
    }

    // ------------------------------------------------------------------
    // Interop / permissive fraction per module
    // ------------------------------------------------------------------

    private static void CollectModuleDisclosure(
        ReviewPacket packet,
        InputFile file,
        ModuleNode module,
        Options options)
    {
        var interop = module.InteropBlocks.Count;
        var members =
            module.Functions.Count
            + module.Interfaces.Count
            + module.Enums.Count
            + module.EnumExtensions.Count
            + module.Delegates.Count;

        foreach (var cls in module.Classes)
        {
            interop += cls.InteropBlocks.Count;
            members +=
                cls.Fields.Count
                + cls.Properties.Count
                + cls.Indexers.Count
                + cls.Constructors.Count
                + cls.Methods.Count
                + cls.Events.Count
                + cls.OperatorOverloads.Count;
        }

        var total = members + interop;
        packet.Modules.Add(new ModuleDisclosure(
            Module: module.Name,
            File: System.IO.Path.GetFileName(file.Path),
            InteropBlocks: interop,
            TotalMembers: total,
            InteropFraction: total == 0 ? 0.0 : Math.Round((double)interop / total, 4),
            PermissiveEffects: options.PermissiveEffects,
            ContractChecksOff: options.ContractChecksOff,
            UsesRefinementTypes: module.RefinementTypes.Count > 0));
    }

    // ------------------------------------------------------------------
    // Caller impact — in-memory call graph
    // ------------------------------------------------------------------

    /// <summary>
    /// Computes caller impact over the union of every module in the
    /// invocation: per-module call graphs are built, then call edges are
    /// re-resolved against the combined declaration set so a caller in one
    /// file of a declaration defined in another is found (review M2).
    /// Returns the requested changed-declaration selectors that matched no
    /// declaration in any module (review m1 — a typo must not silently yield
    /// an impact-free packet).
    /// </summary>
    private static IReadOnlyList<string> CollectCallerImpact(
        ReviewPacket packet,
        IReadOnlyList<(InputFile File, ModuleNode Module)> compiledModules,
        Options options)
    {
        var explicitIds = options.ChangedDeclarations ?? [];
        var anyRanges = options.ChangedLineRanges is { Count: > 0 };
        if (explicitIds.Count == 0 && !anyRanges)
            return [];

        // Combined declaration set + per-module graphs.
        var declarations = new Dictionary<string, (FunctionNode Node, string Module)>(StringComparer.Ordinal);
        var nameToIds = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var graphs = new List<(InputFile File, ModuleNode Module, CallGraphAnalysis Graph)>();

        foreach (var (file, module) in compiledModules)
        {
            var graph = CallGraphAnalysis.Build(module);
            graphs.Add((file, module, graph));
            foreach (var (id, node) in graph.Functions)
            {
                declarations.TryAdd(id, (node, module.Name));
                if (!nameToIds.TryGetValue(node.Name, out var ids))
                {
                    ids = [];
                    nameToIds[node.Name] = ids;
                }
                if (!ids.Contains(id))
                    ids.Add(id);
            }
        }

        // Cross-module reverse edges: re-resolve every call-site name against
        // the combined declaration set.
        var reverse = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (_, _, graph) in graphs)
        {
            foreach (var (callerId, callees) in graph.ForwardGraph)
            {
                foreach (var (calleeName, _) in callees)
                {
                    var bareName = calleeName;
                    var lastDot = bareName.LastIndexOf('.');
                    if (lastDot >= 0)
                        bareName = bareName[(lastDot + 1)..];

                    foreach (var candidate in ResolveNames(calleeName, bareName, declarations, nameToIds))
                    {
                        if (!reverse.TryGetValue(candidate, out var callers))
                        {
                            callers = new HashSet<string>(StringComparer.Ordinal);
                            reverse[candidate] = callers;
                        }
                        callers.Add(callerId);
                    }
                }
            }
        }

        // Resolve the changed set.
        var changed = new List<string>();
        var unmatched = new List<string>();
        foreach (var selector in explicitIds)
        {
            if (declarations.ContainsKey(selector))
                changed.Add(selector);
            else if (nameToIds.TryGetValue(selector, out var ids))
                changed.AddRange(ids);
            else
                unmatched.Add(selector);
        }

        if (options.ChangedLineRanges is { Count: > 0 } rangesByFile)
        {
            foreach (var (file, _, graph) in graphs)
            {
                if (rangesByFile.TryGetValue(System.IO.Path.GetFullPath(file.Path), out var ranges)
                    && ranges is { Count: > 0 })
                {
                    changed.AddRange(DeclarationsInRanges(graph, ranges));
                }
            }
        }

        foreach (var id in changed.Distinct().OrderBy(c => c, StringComparer.Ordinal))
        {
            if (!packet.ChangedDeclarations.Contains(id))
                packet.ChangedDeclarations.Add(id);

            var (node, moduleName) = declarations[id];
            var callers = (reverse.TryGetValue(id, out var callerIds)
                    ? (IEnumerable<string>)callerIds
                    : Array.Empty<string>())
                .Where(callerId => callerId != id)
                .OrderBy(c => c, StringComparer.Ordinal)
                .Select(callerId => new CallerRef(
                    callerId,
                    declarations.TryGetValue(callerId, out var caller) ? caller.Node.Name : callerId))
                .ToList();

            packet.CallerImpact.Add(new CallerImpactRecord(id, node.Name, moduleName, callers));
        }

        return unmatched;
    }

    private static IEnumerable<string> ResolveNames(
        string calleeName,
        string bareName,
        Dictionary<string, (FunctionNode Node, string Module)> declarations,
        Dictionary<string, List<string>> nameToIds)
    {
        if (declarations.ContainsKey(calleeName))
            yield return calleeName;
        if (nameToIds.TryGetValue(calleeName, out var direct))
        {
            foreach (var id in direct)
                yield return id;
        }
        if (bareName != calleeName && nameToIds.TryGetValue(bareName, out var bare))
        {
            foreach (var id in bare)
                yield return id;
        }
    }

    /// <summary>
    /// Maps changed line ranges to declarations. A declaration's extent is
    /// approximated as [its start line, next declaration's start line - 1]
    /// within the file — sufficient for overlap tests without an end-line on
    /// the AST node. Consequence (documented): an edit BETWEEN declarations
    /// attributes to the preceding declaration — conservative over-inclusion,
    /// never omission.
    /// </summary>
    private static IEnumerable<string> DeclarationsInRanges(
        CallGraphAnalysis callGraph,
        IReadOnlyList<(int StartLine, int EndLine)> ranges)
    {
        var ordered = callGraph.Functions
            .OrderBy(kvp => kvp.Value.Span.Line)
            .ToList();

        for (var i = 0; i < ordered.Count; i++)
        {
            var startLine = ordered[i].Value.Span.Line;
            var endLine = i + 1 < ordered.Count ? ordered[i + 1].Value.Span.Line - 1 : int.MaxValue;
            foreach (var (rangeStart, rangeEnd) in ranges)
            {
                if (rangeStart <= endLine && rangeEnd >= startLine)
                {
                    yield return ordered[i].Key;
                    break;
                }
            }
        }
    }

    // ------------------------------------------------------------------
    // Summary + markdown rendering
    // ------------------------------------------------------------------

    private static void FinalizeSummary(ReviewPacket packet)
    {
        var all = packet.UnprovenRemainder.Concat(packet.Proven).ToList();
        var summary = packet.Summary;
        summary.TotalContracts = all.Count;
        // 'proven' counts CLEAN proofs only — a vacuous proof is unproven
        // remainder and must not let automated consumers read
        // proven == totalContracts as clean (review m2).
        summary.Proven = all.Count(c => c.Status == "proven" && !c.Vacuous);
        summary.ProvenVacuous = all.Count(c => c.Status == "proven" && c.Vacuous);
        summary.Refuted = all.Count(c => c.Status == "refuted");
        summary.Assumed = all.Count(c => c.Status == "assumed");
        summary.Unknown = all.Count(c => c.Status == "unknown");
        summary.Timeout = all.Count(c => c.Status == "timeout");
        summary.Unsupported = all.Count(c => c.Status == "unsupported");
        summary.Unavailable = all.Count(c => c.Status == "unavailable");
        summary.Vacuous = all.Count(c => c.Vacuous);
        summary.UnprovenRemainder = packet.UnprovenRemainder.Count;

        // Refuted first, then assumed/unknown/timeout/unsupported/unavailable,
        // vacuous-proven last inside the remainder.
        packet.UnprovenRemainder.Sort((a, b) =>
        {
            var rank = StatusRank(a).CompareTo(StatusRank(b));
            if (rank != 0) return rank;
            var byFile = string.Compare(a.File, b.File, StringComparison.Ordinal);
            if (byFile != 0) return byFile;
            return string.Compare(a.FunctionId, b.FunctionId, StringComparison.Ordinal);
        });

        static int StatusRank(ContractRecord record) => record.Status switch
        {
            "refuted" => 0,
            "unsupported" => 1,
            "unknown" => 2,
            "timeout" => 3,
            "assumed" => 4,
            "unavailable" => 5,
            _ => 6 // vacuous proven
        };
    }

    /// <summary>Renders the packet as markdown. Waivers are the first line.</summary>
    public static string RenderMarkdown(ReviewPacket packet)
    {
        var sb = new StringBuilder();

        // §5.2: waiver disclosure on the first line, before anything else.
        foreach (var waiver in packet.Waivers)
            sb.AppendLine($"**{waiver}**");
        if (packet.Waivers.Count > 0)
            sb.AppendLine();

        sb.AppendLine("# Review packet");
        sb.AppendLine();
        sb.AppendLine($"Generated: {packet.GeneratedAt}"
            + (packet.BaselineRef is null ? "" : $" · baseline: `{packet.BaselineRef}`"));
        sb.AppendLine();

        var s = packet.Summary;
        sb.AppendLine("## Unproven remainder");
        sb.AppendLine();
        sb.AppendLine($"**{s.UnprovenRemainder} of {s.TotalContracts} contract(s) are not cleanly proven** "
            + $"(refuted {s.Refuted}, assumed {s.Assumed}, unknown {s.Unknown}, timeout {s.Timeout}, "
            + $"unsupported {s.Unsupported}, unavailable {s.Unavailable}, vacuous {s.Vacuous}).");
        sb.AppendLine();

        if (packet.UnprovenRemainder.Count > 0)
        {
            sb.AppendLine("| Status | Declaration | Contract | Detail |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var record in packet.UnprovenRemainder)
            {
                var status = record.Vacuous ? $"{record.Status} (VACUOUS)" : record.Status;
                var detail = record.Counterexample is not null
                    ? $"counterexample: {record.Counterexample}"
                    : record.Assumptions.Count > 0
                        ? $"assumes: {string.Join("; ", record.Assumptions)}"
                        : record.Reason ?? "";
                var text = record.Text is null ? $"{record.ContractKind}[{record.Index}]" : $"`{record.Text}`";
                sb.AppendLine($"| {status} | {record.Module}.{record.FunctionName} ({record.FunctionId}) | {text} | {Escape(detail)} |");
            }
            sb.AppendLine();
        }

        sb.AppendLine("## Honesty notes");
        sb.AppendLine();
        foreach (var note in packet.HonestyNotes)
            sb.AppendLine($"- {note}");
        sb.AppendLine();

        sb.AppendLine("## Module disclosure (interop / waiver fractions)");
        sb.AppendLine();
        sb.AppendLine("| Module | File | Interop blocks | Total members | Interop fraction | Permissive effects | Contract checks off | Refinement types |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var module in packet.Modules)
        {
            sb.AppendLine($"| {module.Module} | {module.File} | {module.InteropBlocks} | {module.TotalMembers} "
                + $"| {module.InteropFraction:P1} | {(module.PermissiveEffects ? "YES (waiver)" : "no")} "
                + $"| {(module.ContractChecksOff ? "YES (waiver)" : "no")} "
                + $"| {(module.UsesRefinementTypes ? "yes (NOT runtime-enforced, #782)" : "no")} |");
        }
        sb.AppendLine();

        if (packet.CallerImpact.Count > 0)
        {
            sb.AppendLine("## Caller impact (in-memory call graph)");
            sb.AppendLine();
            foreach (var impact in packet.CallerImpact)
            {
                var callers = impact.Callers.Count == 0
                    ? "no internal callers"
                    : string.Join(", ", impact.Callers.Select(c => $"{c.Name} ({c.Id})"));
                sb.AppendLine($"- **{impact.Module}.{impact.ChangedName}** ({impact.ChangedId}) — callers: {callers}");
            }
            sb.AppendLine();
        }

        if (packet.Proven.Count > 0)
        {
            sb.AppendLine("## Proven (non-vacuous)");
            sb.AppendLine();
            foreach (var record in packet.Proven)
            {
                var text = record.Text is null ? $"{record.ContractKind}[{record.Index}]" : $"`{record.Text}`";
                sb.AppendLine($"- {record.Module}.{record.FunctionName} ({record.FunctionId}): {text}");
            }
        }

        return sb.ToString();
    }

    private static string Escape(string value)
        => value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
}
