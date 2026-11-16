using System.Text.RegularExpressions;

namespace Calor.Evaluation.Metrics;

/// <summary>
/// Source patterns for native (0.30) Calor constructs, shared by the metric calculators.
/// Each pattern is the native spelling of the construct the calculators used to find by its
/// retired section marker (<c>requires</c> for a precondition, a line-head <c>=&gt;</c> for a
/// postcondition, a line-head <c>!</c> for an effect row, and so on). The metrics, weights and
/// scoring are unchanged; only the spelling they look for is.
/// </summary>
public static class CalorSyntax
{
    private const RegexOptions Line = RegexOptions.Compiled | RegexOptions.Multiline;

    public static readonly Regex Module = new(@"^[ \t]*module[ \t]+\S", Line);
    public static readonly Regex Function = new(@"^[ \t]*func[ \t]+\S", Line);
    /// <summary>A function header that declares a return type (<c>→ T</c>).</summary>
    public static readonly Regex TypedOutput = new(@"^[ \t]*func[ \t][^\n]*→", Line);
    public static readonly Regex Return = new(@"^[ \t]*return\b", Line);
    public static readonly Regex Requires = new(@"^[ \t]*requires\b", Line);
    public static readonly Regex Ensures = new(@"^[ \t]*=>[ \t]", Line);
    public static readonly Regex Invariant = new(@"^[ \t]*invariant\b", Line);
    /// <summary>An effect row; a bare <c>!</c> is the pure row.</summary>
    public static readonly Regex EffectRow = new(@"^[ \t]*!(?=[ \t]|\r?$)", Line);
    /// <summary>The effects listed on a non-empty row, e.g. <c>cw,db:rw</c> in <c>! cw,db:rw</c>.</summary>
    public static readonly Regex EffectRowBody = new(@"^[ \t]*![ \t]+([^\s/]+)", Line);
    public static readonly Regex Binding = new(@"^[ \t]*(?:let|var)[ \t]", Line);
    public static readonly Regex MutableBinding = new(@"^[ \t]*var[ \t]", Line);
    public static readonly Regex If = new(@"^[ \t]*if\b", Line);
    public static readonly Regex ElseIf = new(@"^[ \t]*else[ \t]+if\b", Line);
    public static readonly Regex Else = new(@"^[ \t]*else[ \t]*\r?$", Line);
    public static readonly Regex Loop = new(@"^[ \t]*(?:for|each|while|do)\b", Line);
    public static readonly Regex Match = new(@"^[ \t]*match\b", Line);
    /// <summary>A call: a (possibly dotted) name followed by <c>(</c>, excluding keywords.</summary>
    public static readonly Regex Call = new(
        @"(?<![\w.])(?!(?:if|while|for|each|do|match|requires|return|func|module|invariant|let|var|set)\b)[A-Za-z_][\w.]*\(",
        RegexOptions.Compiled);

    private static readonly Regex FunctionParameters = new(@"^[ \t]*func[ \t][^\n(]*\(([^\n]*)\)", Line);
    private static readonly Regex TypedParameter = new(@"(?<=[\w>\]?)]):~?[A-Za-z_]\w*");

    /// <summary>Typed parameters (<c>T:name</c>) across every function header.</summary>
    public static int CountTypedParameters(string source) =>
        FunctionParameters.Matches(source).Sum(header => TypedParameter.Matches(header.Groups[1].Value).Count);

    public static bool HasTypedParameters(string source) => CountTypedParameters(source) > 0;
}
