namespace Calor.RoundTrip.Harness.TaskGen;

/// <summary>Outcome of the D-W4.3 converter-attribution check.</summary>
public enum AttributionOutcome
{
    /// <summary>Attribution was not run (e.g. an earlier clause already excluded the candidate).</summary>
    NotChecked,

    /// <summary>The held-out failure is attributed to the mutated file — reverting its conversion clears the failure.</summary>
    AttributedToMutation,

    /// <summary>The held-out failure persists after the mutated file's conversion is reverted — a different converted file diverges.</summary>
    DivergentOtherFile,
}

/// <summary>
/// The evidence the D-W4.1 predicate decides over. Collected by the live orchestrator via real
/// build/test runs, but the predicate itself is a pure function of this record so both clauses
/// and the exclusion accounting are unit-testable without the corpus.
/// </summary>
public sealed class EligibilityEvidence
{
    /// <summary>Clause (a): the mutated file's post-conversion result on the Calor arm.</summary>
    public required FileConversionResult MutatedFileResult { get; init; }

    /// <summary>Whether a covering test could be identified for the mutation.</summary>
    public required bool HasCoveringTest { get; init; }

    /// <summary>Clause (b): the held-out test's outcome on the C# arm (idiomatic, mutated, no conversion).</summary>
    public required string CSharpArmHeldOutOutcome { get; init; }

    /// <summary>Clause (b): the held-out test's outcome on the converted Calor arm (mutate-then-convert).</summary>
    public required string CalorArmHeldOutOutcome { get; init; }

    /// <summary>Normalized failure signature on the C# arm (exception type / assert kind), when it failed.</summary>
    public string? CSharpArmFailureSignature { get; init; }

    /// <summary>Normalized failure signature on the Calor arm, when it failed.</summary>
    public string? CalorArmFailureSignature { get; init; }

    /// <summary>D-W4.3 attribution outcome.</summary>
    public AttributionOutcome Attribution { get; init; } = AttributionOutcome.NotChecked;

    /// <summary>
    /// When true (default), clause (b) additionally requires the two arms' normalized failure
    /// signatures to match ("fails IDENTICALLY"). When false, both-Failed is sufficient and the
    /// signatures are recorded for disclosure only.
    /// </summary>
    public bool RequireIdenticalSignature { get; init; } = true;

    // ---- Expressible-stratum addressability (the ADDITIONAL clause; null/false for logic tasks) ----

    /// <summary>
    /// The Calor diagnostic code the mutation is PREDICTED to make fire on the converted arm. Non-null
    /// marks the candidate as expressible-stratum; the addressability clause then applies. Null =
    /// logic-stratum, and the addressability clause is skipped (existing behaviour unchanged).
    /// </summary>
    public string? ExpectedCheck { get; init; }

    /// <summary>Whether the differential addressability probe could be run (both conversions compiled).</summary>
    public bool AddressabilityDeterminable { get; init; }

    /// <summary>Whether the expected check was INTRODUCED by the mutation (fires on mutated, absent on clean).</summary>
    public bool VerificationAddressable { get; init; }
}

/// <summary>The predicate's verdict for one candidate.</summary>
public sealed class EligibilityVerdict
{
    public required bool Eligible { get; init; }
    public required ExclusionReason Reason { get; init; }
    public required string Explanation { get; init; }

    /// <summary>Whether the reason is a clause-(a) exclusion (native-region failure).</summary>
    public bool IsClauseA => Reason is ExclusionReason.NotNativeRegion or ExclusionReason.MutatedFileReverted;

    /// <summary>Whether the reason is a clause-(b) exclusion (survival / identical-failure failure).</summary>
    public bool IsClauseB => Reason is ExclusionReason.NoObservableDefect
        or ExclusionReason.MutationDidNotSurviveConversion
        or ExclusionReason.ArmsDiverge;
}

/// <summary>
/// The D-W4.1 eligibility predicate — the gate that makes a task valid. Evaluated mechanically:
/// clause (a) the mutated region converts NATIVELY; clause (b) the mutation SURVIVES conversion
/// (removed held-out test fails identically on both arms); D-W4.3 attribution guards against a
/// converter divergence in a different file being mistaken for the mutation's effect.
/// </summary>
public static class EligibilityPredicate
{
    public static EligibilityVerdict Evaluate(EligibilityEvidence e)
    {
        // ---- Clause (a): mutated region converts NATIVELY ----
        if (e.MutatedFileResult.Status == FileStatus.Reverted)
            return Excluded(ExclusionReason.MutatedFileReverted,
                "clause (a): build recovery reverted the mutated file — the defect would sit in raw C# inside the Calor arm.");

        if (!e.MutatedFileResult.ConvertedNative)
            return Excluded(ExclusionReason.NotNativeRegion,
                $"clause (a): mutated file is not ConvertedNative (Status={e.MutatedFileResult.Status}, LossCount={e.MutatedFileResult.LossCount}).");

        // ---- Covering test must exist to have anything to hold out ----
        if (!e.HasCoveringTest)
            return Excluded(ExclusionReason.NoCoveringTest,
                "no covering test could be identified for the mutation.");

        // ---- Clause (b): mutation SURVIVES conversion, identical failure on both arms ----
        var csFailed = e.CSharpArmHeldOutOutcome == "Failed";
        var calorFailed = e.CalorArmHeldOutOutcome == "Failed";

        if (!csFailed)
            return Excluded(ExclusionReason.NoObservableDefect,
                $"clause (b): held-out test does not fail on the C# arm (outcome={e.CSharpArmHeldOutOutcome}); the mutation produced no observable defect.");

        if (!calorFailed)
            return Excluded(ExclusionReason.MutationDidNotSurviveConversion,
                $"clause (b): held-out test fails on the C# arm but passes on the converted Calor arm (outcome={e.CalorArmHeldOutOutcome}) — the mutation did not survive conversion.");

        if (e.RequireIdenticalSignature)
        {
            // A null/absent signature must NOT count as identical (review [M]#3): both signatures
            // must be present AND equal, so a converter-reason failure with no parseable signature
            // can never slip through as an identical defect-failure.
            var a = e.CSharpArmFailureSignature;
            var b = e.CalorArmFailureSignature;
            if (a is null || b is null || !string.Equals(a, b, StringComparison.Ordinal))
                return Excluded(ExclusionReason.ArmsDiverge,
                    $"clause (b): held-out test fails on both arms but not identically (C#='{a ?? "<none>"}', Calor='{b ?? "<none>"}').");
        }

        // ---- D-W4.3 attribution: the held-out failure must be caused by the mutation ----
        if (e.Attribution == AttributionOutcome.DivergentOtherFile)
            return Excluded(ExclusionReason.ConverterAttributed,
                "D-W4.3: the held-out failure persists after reverting the mutated file's conversion — a different converted file diverges, so the defect is converter-attributable, not the mutation.");

        // ---- Expressible-stratum ADDITIONAL clause: the defect must be verification-addressable ----
        // For an expressible candidate, all four base clauses above (native / covering / survives-
        // identically / attribution) still had to hold; on TOP of them, Calor's expected mechanical
        // check must actually fire on the mutated-converted code (differential probe). A candidate
        // whose check does not fire is a logic bug in disguise — excluded, counted, disclosed — so a
        // logic bug can never masquerade as expressible.
        if (e.ExpectedCheck != null && !e.VerificationAddressable)
            return Excluded(ExclusionReason.NotVerificationAddressable,
                e.AddressabilityDeterminable
                    ? $"expressible clause: the predicted check {e.ExpectedCheck} was not INTRODUCED by the mutation on the converted arm (absent, or already present on the clean conversion) — the defect is not verification-addressable."
                    : $"expressible clause: the differential addressability probe for {e.ExpectedCheck} was indeterminable (a conversion did not compile) — excluded conservatively rather than credit an unconfirmed signal.");

        return new EligibilityVerdict
        {
            Eligible = true,
            Reason = ExclusionReason.None,
            Explanation = "clause (a) native + clause (b) survives identically on both arms; failure attributed to the mutation.",
        };
    }

    /// <summary>
    /// Normalize a raw test error message into a comparable failure signature: the exception type
    /// or the xUnit assertion kind (the first token before "Failure"), ignoring the concrete values
    /// so trivial formatting differences do not read as divergence.
    /// </summary>
    public static string? NormalizeFailureSignature(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage)) return null;
        var first = errorMessage.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";

        // xUnit assertions: "Assert.Equal() Failure: ..." → "Assert.Equal()"
        var idx = first.IndexOf(" Failure", StringComparison.OrdinalIgnoreCase);
        if (idx > 0) return first[..idx].Trim();

        // Exception form: "System.DivideByZeroException : Cannot divide by zero" → the type.
        var colon = first.IndexOf(':');
        if (colon > 0)
        {
            var head = first[..colon].Trim();
            if (head.Contains('.') || head.EndsWith("Exception", StringComparison.Ordinal))
                return head;
        }
        return first;
    }

    private static EligibilityVerdict Excluded(ExclusionReason reason, string explanation) =>
        new() { Eligible = false, Reason = reason, Explanation = explanation };
}
