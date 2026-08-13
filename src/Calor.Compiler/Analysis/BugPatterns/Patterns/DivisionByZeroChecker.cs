using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;

namespace Calor.Compiler.Analysis.BugPatterns.Patterns;

public sealed class DivisionByZeroChecker : IBugPatternChecker
{
    private readonly BugPatternOptions _options;

    public DivisionByZeroChecker(BugPatternOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string Name => "DIV_ZERO";

    public void Check(BoundFunction function, DiagnosticBag diagnostics) =>
        TypedBugPatternAnalysis.Check(
            function,
            diagnostics,
            _options,
            TypedBugPatternKind.DivisionByZero);
}
