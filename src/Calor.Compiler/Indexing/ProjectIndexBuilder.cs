using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Binding.BoundTypes;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Incremental;
using Calor.Compiler.Parsing;
using Calor.Compiler.Refactoring;

namespace Calor.Compiler.Indexing;

/// <summary>
/// Builds a <see cref="ProjectIndex"/> from a set of Calor sources.
///
/// The in-memory model is <see cref="ProjectSymbolIndex"/> — the same one the
/// rename harness addresses (§2.5 gate 4). This builder persists that model
/// rather than growing a second one, so identity, cross-file resolution, and the
/// exact-identifier rule cannot drift between the two consumers.
///
/// v1 rebuilds wholesale (scoping doc §3): there is no incremental path, and the
/// header's inputs are what make a stale index detectable rather than silent.
/// </summary>
public static class ProjectIndexBuilder
{
    public sealed record Options(
        string ProjectDirectory,
        string OptionsToken,
        IReadOnlyList<string> Files);

    /// <summary>
    /// Collects the .calr sources under a directory, in a deterministic order.
    /// </summary>
    public static IReadOnlyList<string> DiscoverSources(string projectDirectory)
    {
        ArgumentNullException.ThrowIfNull(projectDirectory);
        if (!Directory.Exists(projectDirectory))
            return [];

        return Directory
            .GetFiles(projectDirectory, "*.calr", SearchOption.AllDirectories)
            .Where(path => !IsExcluded(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsExcluded(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Contains("/obj/", StringComparison.Ordinal)
            || normalized.Contains("/bin/", StringComparison.Ordinal);
    }

    /// <summary>
    /// The invalidation inputs as they stand right now, in the same shape the
    /// index header records them. Callers compare these against a loaded index
    /// to decide whether it may answer.
    /// </summary>
    public static (string CompilerHash, string OptionsHash, string ManifestHash,
        Dictionary<string, string> Files) CurrentInputs(Options options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var manifestDirectories = options.Files
            .Select(file => Path.GetDirectoryName(Path.GetFullPath(file))!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (manifestDirectories.Count == 0)
            manifestDirectories.Add(Path.GetFullPath(options.ProjectDirectory));

        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in options.Files)
        {
            files[Relative(options.ProjectDirectory, file)] =
                BuildStateCache.ComputeFileHash(file);
        }

        return (
            BuildStateCache.ComputeCliCompilerHash(),
            BuildStateCache.ComputeOptionsHash(options.OptionsToken),
            BuildStateCache.ComputeManifestHash(manifestDirectories),
            files);
    }

    public static ProjectIndex Build(Options options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var inputs = CurrentInputs(options);
        var index = new ProjectIndex
        {
            CompilerHash = inputs.CompilerHash,
            OptionsHash = inputs.OptionsHash,
            ManifestHash = inputs.ManifestHash,
            Files = inputs.Files,
        };

        var symbols = ProjectSymbolIndex.Build(options.Files, out var skipped);
        foreach (var unreadable in skipped)
            index.Residual.UnreadableFiles.Add(Relative(options.ProjectDirectory, unreadable));

        var declarationIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var document in symbols.Documents)
        {
            var relative = Relative(options.ProjectDirectory, document.FilePath);

            foreach (var symbol in document.BoundModule.SymbolsById.Values)
            {
                if (symbol.Id.IsNone || !declarationIds.Add(symbol.Id.Value))
                    continue;

                var (line, column) = LineColumn(document.Source, symbol.DeclarationSpan.Start);
                index.Declarations.Add(new IndexedDeclaration
                {
                    SymbolId = symbol.Id.Value,
                    Name = symbol.Name,
                    Kind = KindOf(symbol),
                    File = relative,
                    Line = line,
                    Column = column,
                    SemanticHash = HashDefinition(document.Source, symbol),
                });
            }
        }

        foreach (var document in symbols.Documents)
        {
            var relative = Relative(options.ProjectDirectory, document.FilePath);
            foreach (var occurrence in symbols.OccurrencesIn(document.FilePath))
            {
                var (line, column) = LineColumn(document.Source, occurrence.Span.Start);
                index.Occurrences.Add(new IndexedOccurrence
                {
                    SymbolId = occurrence.SymbolId.Value,
                    File = relative,
                    Line = line,
                    Column = column,
                    Kind = occurrence.Kind.ToString(),
                });
            }
        }

        foreach (var document in symbols.Documents)
        {
            var relative = Relative(options.ProjectDirectory, document.FilePath);
            var moduleId = document.BoundModule.SymbolsById.Values
                .OfType<FunctionSymbol>()
                .Select(symbol => symbol.Id.Value)
                .FirstOrDefault() ?? "";

            // Module-scoped assumptions apply to everything in the file, so they
            // are recorded against the file rather than any one declaration.
            foreach (var assumption in document.Ast.Assumptions)
            {
                var (line, _) = LineColumn(document.Source, assumption.Span.Start);
                index.Assumptions.Add(new IndexedAssumption
                {
                    SymbolId = "",
                    Scope = "module",
                    Category = assumption.Category?.ToString() ?? "",
                    Description = assumption.Description,
                    File = relative,
                    Line = line,
                });
            }

            foreach (var function in document.Ast.Functions)
            {
                var symbol = document.BoundModule.Functions
                    .FirstOrDefault(bound => bound.Symbol.Name == function.Name)?.Symbol;
                if (symbol == null || symbol.Id.IsNone)
                    continue;

                AddContracts(index, document, relative, symbol.Id.Value,
                    "precondition", function.Preconditions.Select(node => node.Span));
                AddContracts(index, document, relative, symbol.Id.Value,
                    "postcondition", function.Postconditions.Select(node => node.Span));

                foreach (var assumption in function.Assumptions)
                {
                    var (line, _) = LineColumn(document.Source, assumption.Span.Start);
                    index.Assumptions.Add(new IndexedAssumption
                    {
                        SymbolId = symbol.Id.Value,
                        Scope = "declaration",
                        Category = assumption.Category?.ToString() ?? "",
                        Description = assumption.Description,
                        File = relative,
                        Line = line,
                    });
                }
            }
        }

        var sourcesByPath = symbols.Documents.ToDictionary(
            document => document.FilePath,
            document => document.Source,
            StringComparer.Ordinal);
        foreach (var edge in symbols.CallEdges)
        {
            var (line, column) = LineColumn(sourcesByPath[edge.FilePath], edge.Span.Start);
            index.CallEdges.Add(new IndexedCallEdge
            {
                CallerSymbolId = edge.CallerSymbolId.Value,
                CalleeSymbolId = edge.CalleeSymbolId.Value,
                File = Relative(options.ProjectDirectory, edge.FilePath),
                Line = line,
                Column = column,
            });
        }

        foreach (var unresolved in symbols.Residual.UnresolvedCalls)
        {
            index.Residual.UnresolvedCalls.Add(new IndexedUnresolvedCall
            {
                CallerSymbolId = unresolved.CallerSymbolId.Value,
                Target = unresolved.Target,
                File = Relative(options.ProjectDirectory, unresolved.FilePath),
            });
        }
        foreach (var ambiguous in symbols.Residual.AmbiguousCallees)
            index.Residual.AmbiguousCallees.Add(ambiguous);

        RecordEffectRows(index, options, symbols);

        index.Canonicalize();
        return index;
    }

    // --- v0.15 E5: the effects facet (design-doc §8.5/§8.6) -------------------

    /// <summary>
    /// Records one <see cref="IndexedEffectRow"/> per declaration and per
    /// function-typed parameter/return position, from the SAME two producers
    /// <c>calor build</c> runs — the per-module <see cref="EffectEnforcementPass"/>
    /// (its <see cref="EffectEnforcementPass.DeclarationFacts"/>) and the
    /// cross-module pass's resolution over the symbol-keyed
    /// <see cref="EffectSummary"/> projection. Nothing here infers an effect: the
    /// index is a consumer of the compilation's answer (§8.5), and
    /// <c>EffectSummaryIsIndexIndependent</c> pins that the dependency does not
    /// run the other way.
    ///
    /// <para>A file the binder reported errors for gets no rows and a residual
    /// entry, because the CLI skips the effect pass there and rows the CLI never
    /// computed would be an answer with no producer.</para>
    /// </summary>
    private static void RecordEffectRows(
        ProjectIndex index,
        Options options,
        ProjectSymbolIndex symbols)
    {
        var projectDirectory = Path.GetFullPath(options.ProjectDirectory);
        var resolver = new EffectResolver();
        resolver.Initialize(projectDirectory);

        // The driver's own cross-module qualification map and registry, over
        // every module the index holds — so a call to another file's public
        // function is charged the way `calor build` charges it, not as unknown.
        var modules = symbols.Documents
            .Select(document => (document.Ast, document.FilePath))
            .ToList();
        var crossModuleNames = CompilationDriver
            .BuildCrossModuleFunctionMap(modules.Select(module => module.Ast).ToList())
            .Keys
            .ToArray();
        var registry = CrossModuleEffectRegistry.Build(modules);
        var crossPass = new CrossModuleEffectEnforcementPass(UnknownCallPolicy.Strict);

        foreach (var document in symbols.Documents)
        {
            var relative = Relative(options.ProjectDirectory, document.FilePath);
            if (document.BindHadErrors)
            {
                index.Residual.EffectRowsUnavailable.Add(
                    $"{relative}: the binder reported errors, so the effect pass did not run");
                continue;
            }

            IReadOnlyDictionary<string, EffectEnforcementPass.DeclarationEffectFact> facts;
            IReadOnlyDictionary<string, EffectSet> crossCharges;
            try
            {
                var pass = new EffectEnforcementPass(
                    new DiagnosticBag(),
                    UnknownCallPolicy.Strict,
                    resolver: resolver,
                    projectDirectory: projectDirectory,
                    crossModuleFunctionNames: crossModuleNames);
                pass.Enforce(document.Ast);
                facts = pass.DeclarationFacts;
                crossCharges = crossPass.ResolveCrossModuleEffects(
                    EffectSummaryBuilder.Build(document.Ast), document.FilePath, registry);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                index.Residual.EffectRowsUnavailable.Add(
                    $"{relative}: the effect pass failed ({exception.GetType().Name})");
                continue;
            }

            var functionSymbols = document.BoundModule.SymbolsById.Values
                .OfType<FunctionSymbol>()
                .Where(symbol => !symbol.Id.IsNone)
                .ToLookup(symbol => symbol.DefinitionSpan.Start);

            foreach (var fact in facts.Values)
            {
                var symbol = MatchSymbol(functionSymbols[fact.Span.Start], fact);
                if (symbol == null)
                    continue;

                var (line, _) = LineColumn(document.Source, symbol.DeclarationSpan.Start);
                index.EffectRows.Add(ToDeclarationRow(
                    fact, symbol, crossCharges.GetValueOrDefault(fact.FunctionId), relative, line));
            }

            foreach (var function in document.Ast.Functions)
            {
                var symbol = functionSymbols[function.Span.Start].FirstOrDefault();
                if (symbol != null)
                    RecordPositionRows(index, document, relative, symbol, function.Parameters, function.Output);
            }
            foreach (var cls in CallGraphAnalysis.EnumerateClasses(document.Ast))
            {
                foreach (var method in CallGraphAnalysis.EnumerateMethods(cls))
                {
                    var symbol = functionSymbols[method.Span.Start].FirstOrDefault();
                    if (symbol != null)
                        RecordPositionRows(index, document, relative, symbol, method.Parameters, method.Output);
                }
            }
        }
    }

    /// <summary>
    /// The bound symbol a pass fact belongs to: the one whose definition starts
    /// where the fact's declaration does. Event accessors share their event's
    /// span, so a span collision is settled by the accessor's name.
    /// </summary>
    private static FunctionSymbol? MatchSymbol(
        IEnumerable<FunctionSymbol> candidates,
        EffectEnforcementPass.DeclarationEffectFact fact)
    {
        FunctionSymbol? first = null;
        var count = 0;
        foreach (var candidate in candidates)
        {
            first ??= candidate;
            count++;
            if (string.Equals(candidate.Name, fact.Name, StringComparison.Ordinal)
                || candidate.Name.EndsWith("." + fact.Name, StringComparison.Ordinal))
                return candidate;
        }

        return count == 1 ? first : null;
    }

    private static IndexedEffectRow ToDeclarationRow(
        EffectEnforcementPass.DeclarationEffectFact fact,
        FunctionSymbol symbol,
        EffectSet? crossCharges,
        string relative,
        int line)
    {
        var inferred = fact.InferredRow;
        var verdict = fact.Verdict;
        var code = fact.DiagnosticCode;
        var forbidden = new List<string>(fact.Forbidden);

        // Cross-module callees: the per-module pass charges nothing for a call
        // to another file's function (the cross-module pass checks it against
        // the caller's DECLARED row, on the same `EffectRow.Fits` relation used
        // here — P16). Fold its charge into the inferred row and its verdict
        // into ours, so `calor query effects` says what `calor build` says.
        if (crossCharges != null && !crossCharges.IsEmpty)
        {
            var crossRow = crossCharges.ToRow();
            inferred = EffectRow.Join(inferred, crossRow);
            if (verdict != EffectFit.DoesNotFit)
            {
                var crossVerdict = EffectRow.Fits(crossRow, fact.DeclaredRow);
                if (crossVerdict == EffectFit.DoesNotFit)
                {
                    verdict = EffectFit.DoesNotFit;
                    code = DiagnosticCode.ForbiddenEffect;
                    foreach (var (kind, value) in crossCharges.Except(fact.DeclaredRow.ToEffectSet()))
                        forbidden.Add(EffectCodes.ToCompact(kind, value));
                }
                else if (crossVerdict == EffectFit.CannotTell && verdict == EffectFit.Fits)
                {
                    verdict = EffectFit.CannotTell;
                    code ??= DiagnosticCode.EffectRowUnknown;
                }
            }
        }

        return new IndexedEffectRow
        {
            SymbolId = symbol.Id.Value,
            OwnerSymbolId = "",
            Name = symbol.Name,
            Kind = fact.Kind,
            Declared = fact.HasDeclaration,
            DeclaredRow = ToIndexedRow(fact.DeclaredRow, fact.DeclaredVariables),
            InferredRow = ToIndexedRow(inferred, fact.InferredVariables),
            Verdict = VerdictText(verdict),
            DiagnosticCode = code,
            Forbidden = forbidden.Distinct(StringComparer.Ordinal).ToList(),
            BoundRow = null,
            File = relative,
            Line = line,
        };
    }

    /// <summary>
    /// Position rows (design-doc §3.3 positions 4/5/6): a parameter or return
    /// that WRITES a row. The declared row is read off the <c>§E</c> node
    /// exactly as the pass reads it (<see cref="EffectEnforcementPass.GetDeclaredEffects"/>
    /// plus the node's binders); <see cref="IndexedEffectRow.BoundRow"/> is what
    /// the binder's <c>FunctionBoundType.Row</c> says for the same position —
    /// its first production reader — and the two are pinned to agree wherever
    /// the row mentions no <c>eff</c> variable (the binder collapses those to
    /// Unknown; E2b's decision, still registered on roadmap §4.2 E5).
    /// Fields and <c>§B</c> bindings are deferred: the index models them as
    /// declarations, but their bound function types are not on a symbol the
    /// builder can reach without a second bind.
    /// </summary>
    private static void RecordPositionRows(
        ProjectIndex index,
        IndexedDocument document,
        string relative,
        FunctionSymbol symbol,
        IReadOnlyList<ParameterNode> parameters,
        OutputNode? output)
    {
        if (symbol.Parameters.Count == parameters.Count)
        {
            for (var ordinal = 0; ordinal < parameters.Count; ordinal++)
            {
                var parameter = parameters[ordinal];
                if (parameter.Row == null)
                    continue;

                var parameterSymbol = symbol.Parameters[ordinal];
                var (line, _) = LineColumn(document.Source, parameter.Span.Start);
                index.EffectRows.Add(new IndexedEffectRow
                {
                    SymbolId = parameterSymbol.Id.IsNone ? symbol.Id.Value : parameterSymbol.Id.Value,
                    OwnerSymbolId = symbol.Id.Value,
                    Name = parameter.Name,
                    Kind = "parameter",
                    Declared = true,
                    DeclaredRow = ToIndexedRow(
                        EffectEnforcementPass.GetDeclaredEffects(parameter.Row).ToRow(),
                        Binders(parameter.Row)),
                    InferredRow = null,
                    Verdict = "declared-only",
                    DiagnosticCode = null,
                    BoundRow = parameterSymbol.FunctionType?.Row.ToCompactDisplayString(),
                    File = relative,
                    Line = line,
                });
            }
        }

        if (output?.Row != null)
        {
            var (line, _) = LineColumn(document.Source, output.Span.Start);
            index.EffectRows.Add(new IndexedEffectRow
            {
                SymbolId = symbol.Id.Value,
                OwnerSymbolId = symbol.Id.Value,
                Name = symbol.Name,
                Kind = "return",
                Declared = true,
                DeclaredRow = ToIndexedRow(
                    EffectEnforcementPass.GetDeclaredEffects(output.Row).ToRow(),
                    Binders(output.Row)),
                InferredRow = null,
                Verdict = "declared-only",
                DiagnosticCode = null,
                BoundRow = symbol.ReturnFunctionType?.Row.ToCompactDisplayString(),
                File = relative,
                Line = line,
            });
        }
    }

    private static IReadOnlyList<KeyValuePair<int, string>> Binders(EffectsNode row)
    {
        var binders = new List<KeyValuePair<int, string>>();
        for (var index = 0; index < row.EffectVariables.Count; index++)
        {
            var ordinal = index < row.EffectVariableOrdinals.Count ? row.EffectVariableOrdinals[index] : -1;
            binders.Add(new KeyValuePair<int, string>(ordinal, row.EffectVariables[index]));
        }
        return binders;
    }

    private static IndexedRow ToIndexedRow(
        EffectRow row,
        IReadOnlyList<KeyValuePair<int, string>> variables)
    {
        var indexed = new IndexedRow
        {
            State = row.IsUnknown ? "unknown" : row.IsAssumed ? "assumed" : "concrete",
            Effects = row.IsUnknown
                ? []
                : row.ToEffectSet().Effects
                    .Select(effect => EffectCodes.ToCompact(effect.Kind, effect.Value))
                    .OrderBy(code => code, StringComparer.Ordinal)
                    .ToList(),
            Variables = variables
                .OrderBy(variable => variable.Key)
                .Select(variable => new IndexedEffectVariable { Ordinal = variable.Key, Name = variable.Value })
                .ToList(),
            Reasons = [.. row.Reasons],
        };

        // EffectRowDisplay's spelling, with the row's `eff` binders appended —
        // a polymorphic row is written `e` or `cw, e`, never `[pure]`, and an
        // assumed one `[assumed: cw, e]`. Unknown absorbs everything (§4.2).
        var display = row.ToCompactDisplayString();
        if (indexed.Variables.Count > 0 && !row.IsUnknown)
        {
            var names = string.Join(", ", indexed.Variables.Select(variable => variable.Name));
            display = row.IsAssumed
                ? display.EndsWith(": pure]", StringComparison.Ordinal)
                    ? $"[assumed: {names}]"
                    : display[..^1] + $", {names}]"
                : display == "[pure]" ? names : $"{display}, {names}";
        }
        indexed.Display = display;
        return indexed;
    }

    internal static string VerdictText(EffectFit verdict) => verdict switch
    {
        EffectFit.Fits => "fits",
        EffectFit.DoesNotFit => "does-not-fit",
        _ => "cannot-tell",
    };

    private static void AddContracts(
        ProjectIndex index,
        IndexedDocument document,
        string relative,
        string symbolId,
        string kind,
        IEnumerable<Calor.Compiler.Parsing.TextSpan> spans)
    {
        var position = 0;
        foreach (var span in spans)
        {
            var (line, _) = LineColumn(document.Source, span.Start);
            var text = span.Start >= 0 && span.Length > 0 && span.End <= document.Source.Length
                ? document.Source.Substring(span.Start, span.Length).Trim()
                : "";
            index.Contracts.Add(new IndexedContract
            {
                SymbolId = symbolId,
                Kind = kind,
                Index = position++,
                Text = text,
                File = relative,
                Line = line,
            });
        }
    }

    /// <summary>
    /// Hashes a declaration's own definition text. Per declaration, not per
    /// file: file granularity was measured and rejected (see
    /// IndexedDeclaration.SemanticHash).
    /// </summary>
    private static string HashDefinition(string source, Symbol symbol)
    {
        var span = symbol.DefinitionSpan;
        if (span.Start < 0 || span.Length <= 0 || span.End > source.Length)
            span = symbol.DeclarationSpan;
        if (span.Start < 0 || span.Length <= 0 || span.End > source.Length)
            return "";

        var text = source.Substring(span.Start, span.Length);
        return Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(text)))[..16];
    }

    private static string KindOf(Symbol symbol) => symbol switch
    {
        FunctionSymbol => "function",
        TypeSymbol => "type",
        VariableSymbol { IsParameter: true } => "parameter",
        VariableSymbol { IsField: true } => "field",
        VariableSymbol { IsProperty: true } => "property",
        VariableSymbol => "local",
        _ => "symbol",
    };

    private static string Relative(string projectDirectory, string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetFullPath(projectDirectory);
        return Path.GetRelativePath(root, full).Replace('\\', '/');
    }

    private static (int Line, int Column) LineColumn(string source, int offset)
    {
        var line = 1;
        var column = 1;
        for (var index = 0; index < offset && index < source.Length; index++)
        {
            if (source[index] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return (line, column);
    }
}
