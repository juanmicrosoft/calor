using Calor.Compiler;
using Calor.Compiler.Diagnostics;
using ExperimentalFlags = Calor.Compiler.ExperimentalFlags;

namespace Calor.Experimental.Tests.Framework;

/// <summary>
/// Runs a single <c>.calr</c> program through the compiler with the feature flag
/// enabled and returns whether the expected diagnostic was emitted.
///
/// Used by per-hypothesis test classes (e.g., <c>Tier1AMicroValidationTests</c>) to
/// implement the positive-case / negative-case assertions uniformly.
/// </summary>
public static class MicroValidationRunner
{
    /// <summary>
    /// Compile the program at <paramref name="programPath"/> with the manifest's
    /// experimental flag enabled. Returns true iff <paramref name="manifest"/>'s
    /// <c>ExpectedDiagnosticCode</c> appears in the diagnostics (any severity).
    /// </summary>
    public static Outcome Run(MicroValidationManifest manifest, string programPath)
    {
        if (manifest is null) throw new ArgumentNullException(nameof(manifest));
        if (string.IsNullOrWhiteSpace(programPath)) throw new ArgumentException("programPath required.", nameof(programPath));
        if (!File.Exists(programPath)) throw new FileNotFoundException(programPath);

        var source = File.ReadAllText(programPath);

        var flagOnOptions = new CompilationOptions
        {
            EnforceEffects = false,
            ExperimentalFlags = new ExperimentalFlags(new[] { manifest.ExperimentalFlag })
        };
        var flagOffOptions = new CompilationOptions
        {
            EnforceEffects = false,
            ExperimentalFlags = ExperimentalFlags.None
        };

        var onResult = Program.Compile(source, programPath, flagOnOptions);
        var offResult = Program.Compile(source, programPath, flagOffOptions);

        var hasWithFlag = onResult.Diagnostics.Any(d => d.Code == manifest.ExpectedDiagnosticCode);
        var hasWithoutFlag = offResult.Diagnostics.Any(d => d.Code == manifest.ExpectedDiagnosticCode);

        return new Outcome(
            ProgramPath: programPath,
            DiagnosticEmittedWithFlag: hasWithFlag,
            DiagnosticEmittedWithoutFlag: hasWithoutFlag,
            CompileErrorsWithFlag: onResult.HasErrors,
            CompileErrorsWithoutFlag: offResult.HasErrors);
    }

    /// <summary>
    /// Outcome of running one program twice (flag on, flag off).
    /// </summary>
    /// <param name="ProgramPath">Path to the program that was run.</param>
    /// <param name="DiagnosticEmittedWithFlag">
    /// True if the expected diagnostic appeared when the flag was on — what a positive case expects.
    /// </param>
    /// <param name="DiagnosticEmittedWithoutFlag">
    /// True if the expected diagnostic appeared even when the flag was off — always a red flag;
    /// experimental features must not emit diagnostics when their flag is disabled.
    /// </param>
    /// <param name="CompileErrorsWithFlag">True if the compilation failed with the flag on.</param>
    /// <param name="CompileErrorsWithoutFlag">True if the compilation failed with the flag off.</param>
    public sealed record Outcome(
        string ProgramPath,
        bool DiagnosticEmittedWithFlag,
        bool DiagnosticEmittedWithoutFlag,
        bool CompileErrorsWithFlag,
        bool CompileErrorsWithoutFlag);
}
