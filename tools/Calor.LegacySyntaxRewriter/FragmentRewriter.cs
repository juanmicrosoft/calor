namespace Calor.Compiler.LegacySyntax;

/// <summary>Rewrites legacy text that is not a complete module.</summary>
internal sealed class FragmentRewriter
{
    private readonly Dictionary<string, RewriteResult> _cache;

    public FragmentRewriter(Dictionary<string, RewriteResult> cache) => _cache = cache;

    public RewriteResult Rewrite(string fragment) => RewriteResult.Fail("fragment");
}
