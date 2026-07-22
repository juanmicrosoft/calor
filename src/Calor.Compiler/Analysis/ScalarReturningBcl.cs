namespace Calor.Compiler.Analysis;

/// <summary>
/// Curated table of well-known BCL methods with an unambiguous scalar (non-array)
/// return type, keyed by the call target as it appears in Calor (<c>§C{Type.Method}</c>)
/// and mapped to the Calor type. The binder has no general BCL return-type model, so
/// this small table is what lets the mutable-rebind type check (<c>Calor0256</c>, #740)
/// infer the type of an <em>unannotated, non-literal</em> rebind value — e.g. flagging
/// <c>§B{~x:i32} 0</c> then <c>§B{~x} §C{File.ReadAllText}</c> (a <c>str</c> into an
/// <c>i32</c>, CS0029).
///
/// <para>Kept deliberately small and unambiguous: only methods whose return type is a
/// primitive whose <em>category</em> (string / numeric / bool) is unmistakable are
/// listed, because <c>Calor0256</c> is a hard error and a wrong entry would be a false
/// positive. Anything not listed is a conservative false negative (accepted), never a
/// false positive. The sibling <see cref="ArrayReturningBcl"/> covers array returns.</para>
/// </summary>
public static class ScalarReturningBcl
{
    /// <summary>Method target → Calor scalar return type.</summary>
    public static readonly IReadOnlyDictionary<string, string> Methods =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // string-returning
            ["File.ReadAllText"] = "str",
            ["Console.ReadLine"] = "str",
            ["Path.GetFileName"] = "str",
            ["Path.GetFileNameWithoutExtension"] = "str",
            ["Path.GetExtension"] = "str",
            ["Path.GetDirectoryName"] = "str",
            ["Path.GetFullPath"] = "str",
            ["Path.Combine"] = "str",
            ["Guid.NewGuid.ToString"] = "str",
            ["Environment.GetEnvironmentVariable"] = "str",
        };
}
