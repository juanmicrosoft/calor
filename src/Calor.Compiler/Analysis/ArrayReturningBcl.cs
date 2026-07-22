namespace Calor.Compiler.Analysis;

/// <summary>
/// Single source of truth for the well-known array-returning BCL methods, keyed
/// by the call target as it appears in Calor (<c>§C{Type.Method}</c>) and mapped
/// to the Calor element type. The binder has no general BCL return-type model, so
/// this small curated table is what lets the array-vs-collection guards recognize
/// these calls as arrays.
///
/// <para>Shared by two guards that must never disagree — <c>BindValidationPass</c>
/// (the language-level <c>Calor0254</c> error) and <c>SelfCheck.ExemplarCompileChecker</c>
/// (the docs-level <c>Calor1331</c> lint). Keeping one table means there is no
/// hand-synced pair to drift (the very rot self-check exists to kill). To extend:
/// add another array-returning method and its element type here; both guards pick
/// it up automatically.</para>
/// </summary>
public static class ArrayReturningBcl
{
    /// <summary>Method target → Calor array element type.</summary>
    public static readonly IReadOnlyDictionary<string, string> Methods =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["File.ReadAllLines"] = "str",
            ["File.ReadAllBytes"] = "u8",
            ["Directory.GetFiles"] = "str",
            ["Directory.GetDirectories"] = "str",
            ["Directory.GetFileSystemEntries"] = "str",
        };
}
