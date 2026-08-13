using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;

namespace Calor.Compiler.Analysis.BugPatterns.Patterns;

public sealed class NullDereferenceChecker : IBugPatternChecker
{
    private readonly BugPatternOptions _options;

    public NullDereferenceChecker(BugPatternOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string Name => "NULL_DEREF";

    public void Check(BoundFunction function, DiagnosticBag diagnostics) =>
        TypedBugPatternAnalysis.Check(
            function,
            diagnostics,
            _options,
            TypedBugPatternKind.NullDereference);
}
