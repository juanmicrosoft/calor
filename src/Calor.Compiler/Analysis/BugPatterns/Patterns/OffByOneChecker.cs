using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;

namespace Calor.Compiler.Analysis.BugPatterns.Patterns;

public sealed class OffByOneChecker : IBugPatternChecker
{
    private readonly BugPatternOptions _options;

    public OffByOneChecker(BugPatternOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string Name => "OFF_BY_ONE";

    public void Check(BoundFunction function, DiagnosticBag diagnostics) =>
        TypedBugPatternAnalysis.Check(
            function,
            diagnostics,
            _options,
            TypedBugPatternKind.OffByOne);
}
