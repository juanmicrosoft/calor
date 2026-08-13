using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;

namespace Calor.Compiler.Analysis.BugPatterns.Patterns;

public sealed class OverflowChecker : IBugPatternChecker
{
    private readonly BugPatternOptions _options;

    public OverflowChecker(BugPatternOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string Name => "OVERFLOW";

    public void Check(BoundFunction function, DiagnosticBag diagnostics) =>
        TypedBugPatternAnalysis.Check(
            function,
            diagnostics,
            _options,
            TypedBugPatternKind.IntegerOverflow);
}
