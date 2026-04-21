using Calor.Compiler.Ast;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Effects;

/// <summary>
/// Builds a serializable <see cref="EffectSummary"/> from a module AST. The summary
/// contains everything the cross-module effect pass needs — declared effects of public
/// functions, internal function names (for "is this an internal call?" decisions),
/// and per-caller call-target listings.
/// </summary>
public static class EffectSummaryBuilder
{
    public static EffectSummary Build(ModuleNode module)
    {
        var summary = new EffectSummary
        {
            ModuleName = module.Name
        };

        foreach (var function in module.Functions)
        {
            summary.InternalFunctionNames.Add(function.Name);

            if (function.Visibility == Visibility.Public || function.Visibility == Visibility.Internal)
            {
                summary.PublicFunctions.Add(BuildFunctionLikeSummary(
                    name: function.Name,
                    className: null,
                    effectsNode: function.Effects,
                    span: function.Span));
            }
        }

        foreach (var cls in module.Classes)
        {
            foreach (var method in cls.Methods)
            {
                summary.InternalMethodNames.Add(method.Name);

                if (method.Visibility == Visibility.Public || method.Visibility == Visibility.Internal)
                {
                    summary.PublicMethods.Add(BuildFunctionLikeSummary(
                        name: method.Name,
                        className: cls.Name,
                        effectsNode: method.Effects,
                        span: method.Span));
                }
            }
        }

        // Per-caller listings — group raw calls by caller name, then attach them with
        // the caller's declared effects + a diagnostic span for any cross-module errors.
        var rawCalls = ExternalCallCollector.CollectPerFunctionWithBareNames(module);
        var callsByCaller = new Dictionary<string, List<RawCall>>(StringComparer.Ordinal);
        foreach (var call in rawCalls)
        {
            if (!callsByCaller.TryGetValue(call.CallerName, out var list))
            {
                list = new List<RawCall>();
                callsByCaller[call.CallerName] = list;
            }
            list.Add(call);
        }

        foreach (var function in module.Functions)
        {
            AppendCallerSummary(summary, function.Effects, function.Span, function.Name, callsByCaller);
        }

        foreach (var cls in module.Classes)
        {
            foreach (var method in cls.Methods)
            {
                var callerName = $"{cls.Name}.{method.Name}";
                AppendCallerSummary(summary, method.Effects, method.Span, callerName, callsByCaller);
            }
        }

        return summary;
    }

    /// <summary>
    /// Parse an <see cref="EffectsNode"/> into serializable <see cref="EffectEntry"/> entries.
    /// Returns an empty list if the node is null or has no effects.
    /// </summary>
    private static List<EffectEntry> ParseEffectsToEntries(EffectsNode? effectsNode)
    {
        var entries = new List<EffectEntry>();
        if (effectsNode == null)
            return entries;

        foreach (var kv in effectsNode.Effects)
        {
            var kind = EffectEnforcementPass.ParseEffectCategory(kv.Key);
            foreach (var value in kv.Value.Split(','))
            {
                var trimmed = value.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    entries.Add(new EffectEntry { Kind = kind.ToString(), Value = trimmed });
                }
            }
        }
        return entries;
    }

    /// <summary>
    /// Build a summary for a function or class method. Both AST node types carry the
    /// same effect-relevant shape (name, effects, span); this unifies the two paths.
    ///
    /// Note: <c>HasEffectDeclaration</c> is true when the §E block exists at all —
    /// even if empty. An explicit <c>§E{}</c> is a deliberate "no effects" contract
    /// (callers can verify against it as EffectSet.Empty); only the absence of §E
    /// is treated as missing a declaration.
    /// </summary>
    private static EffectFunctionSummary BuildFunctionLikeSummary(
        string name,
        string? className,
        EffectsNode? effectsNode,
        TextSpan span)
    {
        return new EffectFunctionSummary
        {
            Name = name,
            ClassName = className,
            HasEffectDeclaration = effectsNode != null,
            DeclaredEffects = ParseEffectsToEntries(effectsNode),
            DeclarationLine = span.Line,
            DeclarationColumn = span.Column
        };
    }

    private static void AppendCallerSummary(
        EffectSummary summary,
        EffectsNode? effectsNode,
        TextSpan functionSpan,
        string callerName,
        Dictionary<string, List<RawCall>> callsByCaller)
    {
        if (!callsByCaller.TryGetValue(callerName, out var calls) || calls.Count == 0)
            return;

        var diagnosticSpan = effectsNode?.Span ?? functionSpan;
        var callerSummary = new EffectCallerSummary
        {
            CallerName = callerName,
            DiagnosticLine = diagnosticSpan.Line,
            DiagnosticColumn = diagnosticSpan.Column,
            DeclaredEffects = ParseEffectsToEntries(effectsNode)
        };

        foreach (var call in calls)
        {
            callerSummary.Calls.Add(new EffectCallSummary
            {
                Target = call.Target,
                IsConstructor = call.IsConstructor
            });
        }

        summary.Callers.Add(callerSummary);
    }

    /// <summary>
    /// Parse an EffectEntry list back into an EffectSet. Kind is stored as enum name.
    /// Null or empty input yields <see cref="EffectSet.Empty"/>. Entries with an
    /// unknown Kind string are silently skipped — safe because the options-hash
    /// includes the EffectKind enum shape, so any change to the enum invalidates
    /// the cache before this code runs against stale values.
    /// </summary>
    internal static EffectSet ToEffectSet(IReadOnlyList<EffectEntry>? entries)
    {
        if (entries == null || entries.Count == 0)
            return EffectSet.Empty;

        var effects = new List<(EffectKind Kind, string Value)>();
        foreach (var entry in entries)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Kind))
                continue;
            if (Enum.TryParse<EffectKind>(entry.Kind, out var kind))
            {
                effects.Add((kind, entry.Value ?? ""));
            }
        }
        return EffectSet.FromInternal(effects);
    }
}
