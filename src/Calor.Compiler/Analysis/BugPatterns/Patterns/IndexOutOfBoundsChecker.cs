using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;

namespace Calor.Compiler.Analysis.BugPatterns.Patterns;

public sealed class IndexOutOfBoundsChecker : IBugPatternChecker
{
    private readonly BugPatternOptions _options;

    public IndexOutOfBoundsChecker(BugPatternOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string Name => "INDEX_OOB";

    public void Check(BoundFunction function, DiagnosticBag diagnostics) =>
        TypedBugPatternAnalysis.Check(
            function,
            diagnostics,
            _options,
            TypedBugPatternKind.IndexOutOfBounds);
}
