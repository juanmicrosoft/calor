using Calor.Compiler.Migration;
using Calor.Compiler.Verification.Z3;

namespace Calor.RoundTrip.Harness.TaskGen;

/// <summary>
/// The verification-addressability probe (W4 expressible stratum, deliverable #2). Given an
/// expressible candidate, it MECHANICALLY confirms that Calor's expected mechanical check actually
/// fires on the mutated-CONVERTED code. This is what partitions the expressible stratum from the
/// logic stratum: a defect is "expressible" only if a Calor build error / bug-pattern diagnostic
/// fires on the converted Calor that the C# compiler has no equivalent of.
///
/// The probe is <b>differential</b> (mirroring the D-W4.3 attribution philosophy): it converts and
/// analyses BOTH the mutated file and the clean original, and reports the defect addressable only
/// when the expected diagnostic is present on the MUTATED conversion and ABSENT on the clean one —
/// i.e. the mutation INTRODUCED the signal. This neutralises converter-baseline noise (many corpus
/// functions already trip a checker on their clean conversion; those pre-existing diagnostics must
/// not be credited to the mutation).
///
/// Effect-family checks (Calor04xx) are probed with effect enforcement on; bug-pattern checks
/// (Calor092x) with the verification analyses on. The two are kept separate so an effect error can
/// never short-circuit the compile before the bug-pattern analyses run, and vice-versa.
/// </summary>
public sealed class VerificationAddressability
{
    /// <summary>Outcome of a differential addressability probe for one candidate.</summary>
    public sealed class Result
    {
        /// <summary>Whether both conversions produced compilable Calor so the differential is meaningful.</summary>
        public required bool Determinable { get; init; }

        /// <summary>The expected check fired on the mutated conversion and NOT on the clean one.</summary>
        public required bool Addressable { get; init; }

        /// <summary>The expected Calor diagnostic code (e.g. <c>Calor0410</c>).</summary>
        public required string ExpectedCheck { get; init; }

        /// <summary>Relevant Calor diagnostic codes observed on the mutated conversion.</summary>
        public IReadOnlyCollection<string> FiredOnMutated { get; init; } = Array.Empty<string>();

        /// <summary>Relevant Calor diagnostic codes observed on the clean conversion.</summary>
        public IReadOnlyCollection<string> FiredOnClean { get; init; } = Array.Empty<string>();

        /// <summary>Human-readable disclosure for provenance / the exclusion report.</summary>
        public required string Note { get; init; }
    }

    // Effect-family codes: undeclared/forbidden effect + the WS-W2 variance/assumption codes.
    private static readonly HashSet<string> EffectCheckCodes = new(StringComparer.Ordinal)
    {
        "Calor0400", "Calor0410", "Calor0418", "Calor0419", "Calor0420", "Calor0421",
    };

    // Bug-pattern codes we recognise as expressible signals.
    private static readonly HashSet<string> BugPatternCheckCodes = new(StringComparer.Ordinal)
    {
        "Calor0920", "Calor0921", "Calor0922", "Calor0925",
    };

    private readonly RoundTripPipeline _pipeline;

    public VerificationAddressability(RoundTripPipeline? pipeline = null) => _pipeline = pipeline ?? new RoundTripPipeline();

    /// <summary>Probe whether <paramref name="expectedCheck"/> is INTRODUCED by the mutation (differential).</summary>
    public Result Probe(string expectedCheck, string mutatedSource, string cleanSource, string filePath)
    {
        var isEffect = EffectCheckCodes.Contains(expectedCheck);
        var isBugPattern = BugPatternCheckCodes.Contains(expectedCheck);
        if (!isEffect && !isBugPattern)
        {
            return new Result
            {
                Determinable = false,
                Addressable = false,
                ExpectedCheck = expectedCheck,
                Note = $"unknown expected check '{expectedCheck}'; not a recognised expressible signal.",
            };
        }

        // The bug-pattern checks (div-by-zero, index-OOB) reach their precise verdict through Z3.
        // Without the native solver they degrade SILENTLY to "no diagnostic" — which the differential
        // below would read as "Calor has no signal for this defect" and record as
        // ExclusionReason.NotVerificationAddressable. That is a false statement about Calor's
        // capability: the honest answer is "we did not ask". Fail loud instead, as indeterminable.
        if (isBugPattern && !Z3ContextFactory.IsAvailable)
        {
            return new Result
            {
                Determinable = false,
                Addressable = false,
                ExpectedCheck = expectedCheck,
                Note = $"addressability indeterminable: '{expectedCheck}' is a Z3-backed check and the native "
                     + "Z3 library is unavailable, so a non-firing check cannot be distinguished from an "
                     + "unasked one. This is NOT evidence that Calor lacks a signal for this defect.",
            };
        }

        var relevant = isEffect ? EffectCheckCodes : BugPatternCheckCodes;

        var mutated = CodesFor(mutatedSource, filePath, isEffect, relevant);
        var clean = CodesFor(cleanSource, filePath, isEffect, relevant);

        if (mutated == null || clean == null)
        {
            var which = mutated == null ? "mutated" : "clean";
            return new Result
            {
                Determinable = false,
                Addressable = false,
                ExpectedCheck = expectedCheck,
                FiredOnMutated = mutated ?? Array.Empty<string>(),
                FiredOnClean = clean ?? Array.Empty<string>(),
                Note = $"addressability indeterminable: the {which} source did not convert+compile to Calor for the probe.",
            };
        }

        var firedOnMutated = mutated.Contains(expectedCheck);
        var firedOnClean = clean.Contains(expectedCheck);
        var addressable = firedOnMutated && !firedOnClean;

        var note = addressable
            ? $"{expectedCheck} is INTRODUCED by the mutation (fires on the mutated conversion, absent on the clean one) — verification-addressable."
            : firedOnMutated
                ? $"{expectedCheck} fires on BOTH conversions — pre-existing converter-baseline diagnostic, not attributable to the mutation; not addressable."
                : $"{expectedCheck} does NOT fire on the mutated conversion — Calor's check has no signal for this defect; not addressable.";

        return new Result
        {
            Determinable = true,
            Addressable = addressable,
            ExpectedCheck = expectedCheck,
            FiredOnMutated = mutated,
            FiredOnClean = clean,
            Note = note,
        };
    }

    /// <summary>
    /// Convert one C# source to Calor and compile it with the check family enabled, returning the
    /// set of relevant Calor diagnostic codes present (any severity). Returns null if the source
    /// cannot be converted+compiled far enough for the probe to be meaningful.
    /// </summary>
    private IReadOnlyCollection<string>? CodesFor(string source, string filePath, bool effectFamily, HashSet<string> relevant)
    {
        var converter = new CSharpToCalorConverter(new ConversionOptions
        {
            GracefulFallback = true,
            PreserveComments = true,
            AutoGenerateIds = true,
        });
        Compiler.ContractMode contractOff = Compiler.ContractMode.Off;

        Compiler.CompilationResult compileResult;
        try
        {
            var conversion = converter.Convert(source, filePath);
            if (!conversion.Success || string.IsNullOrWhiteSpace(conversion.CalorSource))
                return null;

            var options = effectFamily
                ? new Compiler.CompilationOptions
                {
                    // Effect family: enforce effects; permissive so unknown external calls surface as
                    // warnings (not hard errors that would flood/abort) — we key on the code, not severity.
                    EnforceEffects = true,
                    UnknownCallPolicy = Compiler.Effects.UnknownCallPolicy.Permissive,
                    ContractMode = contractOff,
                }
                : new Compiler.CompilationOptions
                {
                    // Bug-pattern family: effects off (so an effect error can't short-circuit the
                    // compile before the analyses run); verification analyses on, Z3 on, only
                    // definitively-proven findings (no inconclusive Info noise / precondition suggestions).
                    EnforceEffects = false,
                    ContractMode = contractOff,
                    EnableVerificationAnalyses = true,
                    VerificationAnalysisOptions = new Compiler.Analysis.VerificationAnalysisOptions
                    {
                        EnableDataflow = false,
                        EnableBugPatterns = true,
                        EnableTaintAnalysis = false,
                        UseZ3Verification = true,
                        BugPatternOptions = new Compiler.Analysis.BugPatterns.BugPatternOptions
                        {
                            ReportOnlyVerified = true,
                            UseZ3Verification = true,
                            CheckDivisionByZero = true,
                            CheckIndexOutOfBounds = true,
                            CheckNullDereference = true,
                            CheckOverflow = false,
                            CheckOffByOne = false,
                            CheckMissingPreconditions = false,
                        },
                    },
                };

            compileResult = Compiler.Program.Compile(conversion.CalorSource, filePath, options);
        }
        catch
        {
            return null;
        }

        return compileResult.Diagnostics
            .Select(d => d.Code)
            .Where(relevant.Contains)
            .ToHashSet(StringComparer.Ordinal);
    }
}
