using System.Reflection;
using Calor.Compiler.Ast;

namespace Calor.Compiler.LegacySyntax;

/// <summary>
/// The effect half of the oracle. Effect rows are Calor semantics that the C# output does
/// not carry, so every effect row in the tree (callable own rows and type rows) is compared
/// in traversal order. The only tolerated difference: a public function/method with no
/// legacy row versus native `!` — today's enforcement reads both as Pure
/// (EffectEnforcementPass.GetDeclaredEffects(null) == Empty), and native syntax requires
/// the explicit row on a boundary.
/// </summary>
internal static class EffectEquivalence
{
    public static string? Difference(ModuleNode legacy, ModuleNode native)
    {
        var a = Rows(legacy).ToList();
        var b = Rows(native).ToList();
        if (a.Count != b.Count)
            return $"{a.Count} effect rows vs {b.Count}";
        for (var i = 0; i < a.Count; i++)
        {
            if (a[i].Owner != b[i].Owner)
                return $"row {i}: owner {a[i].Owner} vs {b[i].Owner}";
            if (a[i].Value == b[i].Value)
                continue;
            if (a[i].Value == "null" && b[i].Value == "pure" && a[i].PublicBoundary)
                continue;
            return $"row {i} ({a[i].Owner}): {a[i].Value} vs {b[i].Value}";
        }
        return null;
    }

    private static IEnumerable<(string Owner, string Value, bool PublicBoundary)> Rows(AstNode root)
    {
        var stack = new Stack<AstNode>();
        stack.Push(root);
        var seen = new HashSet<AstNode>(ReferenceEqualityComparer.Instance);
        while (stack.TryPop(out var node))
        {
            if (!seen.Add(node))
                continue;
            foreach (var property in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.PropertyType != typeof(EffectsNode) || property.GetIndexParameters().Length > 0)
                    continue;
                var row = (EffectsNode?)property.GetValue(node);
                var boundary = property.Name == "Effects" && node switch
                {
                    FunctionNode f => f.Visibility is not (Visibility.Private or Visibility.Internal),
                    MethodNode m => m.Visibility is not (Visibility.Private or Visibility.Internal),
                    _ => false
                };
                yield return ($"{node.GetType().Name}.{property.Name}", Canonical(row), boundary);
            }
            foreach (var child in Analysis.RecursiveAstWalker.GetAllChildren(node).Reverse())
                stack.Push(child);
        }
    }

    private static string Canonical(EffectsNode? row)
    {
        if (row == null)
            return "null";
        if (row.IsUnknown)
            return "unknown" + (row.EffectVariables.Count > 0 ? "|" + string.Join(",", row.EffectVariables.Order()) : "");
        var codes = row.Effects
            .SelectMany(kv => kv.Value.Split(',').Select(v => kv.Key + ":" + v.Trim()))
            .Distinct().Order(StringComparer.Ordinal);
        var text = string.Join(",", codes) + "|" + string.Join(",", row.EffectVariables.Order(StringComparer.Ordinal));
        return text == "|" ? "pure" : text;
    }
}
