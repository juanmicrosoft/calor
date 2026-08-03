namespace Calor.RoundTrip.Harness.TaskGen;

/// <summary>
/// The task-generation orchestrator (D-W4.1, Slice C). ENUMERATES native regions first, then sites
/// mutations within them (mutate-then-convert), collects clause-(a)/(b) + D-W4.3 attribution
/// evidence via the round-trip pipeline primitives, decides each candidate with the pure
/// <see cref="EligibilityPredicate"/>, and writes eligible task bundles + a full exclusion-accounting
/// report. It does NOT run agents — it produces the substrate the Slice-E dry-run consumes.
/// </summary>
public sealed class TaskGenerator
{
    private readonly RoundTripPipeline _pipeline = new();

    /// <summary>Generate bundles for one project. Returns native fraction, bundles, and exclusion accounting.</summary>
    public async Task<TaskGenProjectResult> GenerateForProjectAsync(RoundTripConfig project, TaskGenOptions options)
    {
        var workRoot = options.WorkRoot ?? Path.Combine(Path.GetTempPath(), "calor-taskgen", project.ProjectName);
        Directory.CreateDirectory(workRoot);
        var accounting = new ExclusionAccounting();
        var bundles = new List<TaskBundle>();

        Console.WriteLine($"[{project.ProjectName}] Step 1/4: baseline suite + native-region enumeration (mutate-then-convert prefilter)");

        // --- Baseline suite on the pristine (unmutated) source: both arms share this baseline. ---
        var baseDir = _pipeline.PrepareWorkingCopy(Clone(project, Path.Combine(workRoot, "baseline")));
        var baseBuild = await _pipeline.BuildProjectAsync(baseDir, project);
        var baseline = await _pipeline.RunTestsAsync(baseDir, project, noBuild: baseBuild.Succeeded);
        Console.WriteLine($"[{project.ProjectName}]   baseline: {baseline.Passed}/{baseline.TotalTests} passed");

        // --- Native-region enumeration: convert the pristine library, build+recover, measure fidelity. ---
        var nativeDir = _pipeline.PrepareWorkingCopy(Clone(project, Path.Combine(workRoot, "native-probe")));
        var probeReport = new RoundTripReport { ProjectName = project.ProjectName };
        var nativeResults = await _pipeline.ConvertAndReplaceAsync(nativeDir, project, probeReport);
        var nativeBuild = await _pipeline.BuildProjectAsync(nativeDir, project);
        if (!nativeBuild.Succeeded)
            await _pipeline.RecoverBuildAsync(nativeDir, project, nativeResults);
        var coverage = ConversionCoverage.Compute(nativeResults, probeReport.ExcludedFileCount);
        var nativeFiles = nativeResults.Where(f => f.ConvertedNative).Select(f => f.FilePath).ToList();
        Console.WriteLine($"[{project.ProjectName}]   NativeFraction {coverage.NativeFraction:P1} " +
            $"({coverage.ConvertedNative} native of {coverage.TotalConvertibleFiles}); siting mutations in {nativeFiles.Count} native file(s)");

        // --- Site injected mutations in native regions ONLY (governing fact). ---
        var enumerated = new List<MutationCandidate>();
        foreach (var relPath in nativeFiles)
        {
            var absOriginal = Path.Combine(project.OriginalProjectPath, relPath);
            if (!File.Exists(absOriginal)) continue;
            var source = await File.ReadAllTextAsync(absOriginal);
            // NegateCondition always compiles, but inverts a whole branch (all-or-nothing coverage),
            // so it is dropped from the DEFAULT bounded set to keep the demo focused on point mutations;
            // the operator itself remains available in InjectedMutationOperators and unit-tested.
            enumerated.AddRange(InjectedMutationOperators.Enumerate(source, relPath)
                .Where(c => c.Operator != MutationOperatorKind.NegateCondition));
        }
        var totalEnumerated = enumerated.Count; // honest denominator (minor: before Take/early-stop truncation)
        var candidates = enumerated
            .OrderBy(c => c.FileRelPath, StringComparer.Ordinal)
            .ThenBy(c => c.Line).ThenBy(c => c.Column)
            .Take(options.MaxCandidatesPerProject)
            .ToList();
        Console.WriteLine($"[{project.ProjectName}] Step 2/4: {totalEnumerated} candidate(s) enumerated; evaluating up to {candidates.Count}");

        int idx = 0;
        foreach (var candidate in candidates)
        {
            idx++;
            if (options.TargetEligiblePerProject > 0 && bundles.Count >= options.TargetEligiblePerProject)
            {
                Console.WriteLine($"[{project.ProjectName}]   reached target of {options.TargetEligiblePerProject} eligible; {candidates.Count - idx + 1} candidate(s) not evaluated");
                break;
            }
            Console.WriteLine($"[{project.ProjectName}]   candidate {idx}/{candidates.Count}: {candidate.FileRelPath} [{candidate.OperatorDescription}] L{candidate.Line}");
            var (verdict, bundle) = await EvaluateCandidateAsync(project, options, candidate, baseline, coverage.NativeFraction, workRoot, idx);
            accounting.Record(new CandidateDisposition
            {
                ProjectName = project.ProjectName,
                CandidateId = candidate.Id,
                FileRelPath = candidate.FileRelPath,
                Operator = candidate.Operator,
                Source = candidate.Source,
                Eligible = verdict.Eligible,
                Reason = verdict.Reason,
                Explanation = verdict.Explanation,
            });
            if (bundle != null) bundles.Add(bundle);
            Console.WriteLine($"[{project.ProjectName}]     -> {(verdict.Eligible ? "ELIGIBLE" : "excluded: " + verdict.Reason)}");
        }

        return new TaskGenProjectResult
        {
            ProjectName = project.ProjectName,
            NativeFraction = coverage.NativeFraction,
            Inconclusive = false,
            NativeSourceFiles = nativeFiles.Count,
            TotalConvertibleFiles = coverage.TotalConvertibleFiles,
            TotalEnumeratedCandidates = totalEnumerated,
            BaselineTestsPassed = baseline.Passed,
            Bundles = bundles,
            Accounting = accounting,
        };
    }

    private async Task<(EligibilityVerdict, TaskBundle?)> EvaluateCandidateAsync(
        RoundTripConfig project, TaskGenOptions options, MutationCandidate candidate,
        TestRunResult baseline, double nativeFraction, string workRoot, int idx)
    {
        var stem = $"cand{idx}-{candidate.Operator}";

        // ===== C# arm: idiomatic original + mutation, no conversion. =====
        var csDir = _pipeline.PrepareWorkingCopy(Clone(project, Path.Combine(workRoot, $"{stem}-csharp")));
        await File.WriteAllTextAsync(Path.Combine(csDir, candidate.FileRelPath), candidate.MutatedSource);
        var csTests = await _pipeline.RunTestsAsync(csDir, project); // builds + runs full suite

        // A mutation that does not COMPILE (no structured results) is an invalid candidate — a
        // distinct bucket from "compiles but has no covering test" (minor).
        if (csTests.Results.Count == 0)
            return (Excluded(ExclusionReason.DidNotCompile,
                "the mutation did not compile / produced no test results on the C# arm — invalid candidate."), null);

        var covering = HeldOutExtraction.IdentifyCoveringTests(baseline, csTests);
        if (covering.Count == 0)
            return (Excluded(ExclusionReason.NoObservableDefect,
                "clause (b): the mutation compiles but breaks no previously-passing test — no observable defect."), null);

        var heldOut = HeldOutExtraction.ToHeldOut(covering);
        var csSignature = EligibilityPredicate.NormalizeFailureSignature(covering[0].ErrorMessage);
        var heldOutFilter = HeldOutExtraction.BuildHeldOutFilter(heldOut);

        // ===== Calor arm: mutate-then-convert the whole library, build+recover. =====
        var calorDir = _pipeline.PrepareWorkingCopy(Clone(project, Path.Combine(workRoot, $"{stem}-calor")));
        await File.WriteAllTextAsync(Path.Combine(calorDir, candidate.FileRelPath), candidate.MutatedSource);
        var report = new RoundTripReport { ProjectName = project.ProjectName };
        var calorResults = await _pipeline.ConvertAndReplaceAsync(calorDir, project, report);
        var calorBuild = await _pipeline.BuildProjectAsync(calorDir, project);
        if (!calorBuild.Succeeded)
        {
            await _pipeline.RecoverBuildAsync(calorDir, project, calorResults);
            calorBuild = await _pipeline.BuildProjectAsync(calorDir, project);
        }

        var mutatedResult = calorResults.FirstOrDefault(f => f.FilePath == candidate.FileRelPath)
            ?? new FileConversionResult { FilePath = candidate.FileRelPath, Status = FileStatus.ConversionFailed };

        string calorOutcome;
        string? calorSignature = null;
        if (!calorBuild.Succeeded)
        {
            calorOutcome = "BuildFailed";
        }
        else
        {
            var calorHeldOut = await _pipeline.RunTestsAsync(calorDir, Clone(project, null, heldOutFilter), noBuild: true);
            (calorOutcome, calorSignature) = OutcomeOf(calorHeldOut, heldOut);
        }

        // ===== D-W4.3 attribution (bisect pattern, review [M]#2): swap in the UNMUTATED-CONVERTED file. =====
        // Replacing the mutated-converted file with the converter's output for the CLEAN original source
        // removes the mutation while KEEPING this file's conversion, so a same-file converter divergence is
        // NOT masked. Held-out passes ⇒ the mutation caused it (mutation-attributed, eligible). Held-out
        // still fails ⇒ the converter diverges on this file even without the mutation (converter-attributed).
        var attribution = AttributionOutcome.NotChecked;
        if (calorOutcome == "Failed")
        {
            var originalUnmutated = await File.ReadAllTextAsync(Path.Combine(project.OriginalProjectPath, candidate.FileRelPath));
            var unmutatedConverted = _pipeline.ConvertSourceToRoundTripCSharp(
                originalUnmutated, Path.Combine(calorDir, candidate.FileRelPath));
            if (unmutatedConverted == null)
            {
                // The clean file does not convert-and-recompile here (it should, since the prefilter said
                // native) — cannot isolate soundly, so exclude conservatively as converter-attributed.
                attribution = AttributionOutcome.DivergentOtherFile;
            }
            else
            {
                await File.WriteAllTextAsync(Path.Combine(calorDir, candidate.FileRelPath), unmutatedConverted);
                var attrBuild = await _pipeline.BuildProjectAsync(calorDir, project);
                if (attrBuild.Succeeded)
                {
                    var attrRun = await _pipeline.RunTestsAsync(calorDir, Clone(project, null, heldOutFilter), noBuild: true);
                    var (attrOutcome, _) = OutcomeOf(attrRun, heldOut);
                    attribution = attrOutcome == "Failed" ? AttributionOutcome.DivergentOtherFile : AttributionOutcome.AttributedToMutation;
                }
                else
                {
                    // The unmutated-converted file breaks the build in-context ⇒ this file's conversion
                    // diverges independent of the mutation ⇒ converter-attributed exclusion.
                    attribution = AttributionOutcome.DivergentOtherFile;
                }
            }
        }

        var evidence = new EligibilityEvidence
        {
            MutatedFileResult = mutatedResult,
            HasCoveringTest = true,
            CSharpArmHeldOutOutcome = "Failed",
            CalorArmHeldOutOutcome = calorOutcome,
            CSharpArmFailureSignature = csSignature,
            CalorArmFailureSignature = calorSignature,
            Attribution = attribution,
            RequireIdenticalSignature = options.RequireIdenticalSignature,
        };

        var verdict = EligibilityPredicate.Evaluate(evidence);
        if (!verdict.Eligible)
            return (verdict, null);

        var proof = new EligibilityProof
        {
            MutatedFileRelPath = candidate.FileRelPath,
            MutatedFileConvertedNative = mutatedResult.ConvertedNative,
            MutatedFileLossCount = mutatedResult.LossCount,
            MutatedFileStatus = mutatedResult.Status.ToString(),
            CSharpArmHeldOutOutcome = "Failed",
            CalorArmHeldOutOutcome = calorOutcome,
            CSharpArmFailureSignature = csSignature,
            CalorArmFailureSignature = calorSignature,
            AttributionOutcome = attribution.ToString(),
            ProjectNativeFraction = nativeFraction,
        };

        // Build a FRESH canonical Calor arm for the bundle (the eval copy was perturbed by attribution).
        var bundle = await WriteBundleAsync(project, options, candidate, heldOut, covering[0], proof, csDir, workRoot, stem);
        return (verdict, bundle);
    }

    private async Task<TaskBundle> WriteBundleAsync(
        RoundTripConfig project, TaskGenOptions options, MutationCandidate candidate,
        List<HeldOutTest> heldOut, TestResult coveringSample, EligibilityProof proof,
        string csDir, string workRoot, string stem)
    {
        // Fresh canonical Calor arm = clean copy + mutation + one conversion pass + build/recover.
        var armDir = _pipeline.PrepareWorkingCopy(Clone(project, Path.Combine(workRoot, $"{stem}-calorarm")));
        await File.WriteAllTextAsync(Path.Combine(armDir, candidate.FileRelPath), candidate.MutatedSource);
        var armResults = await _pipeline.ConvertAndReplaceAsync(armDir, project, new RoundTripReport { ProjectName = project.ProjectName });
        var armBuild = await _pipeline.BuildProjectAsync(armDir, project);
        if (!armBuild.Succeeded)
            await _pipeline.RecoverBuildAsync(armDir, project, armResults);

        var taskId = $"{project.ProjectName.ToLowerInvariant()}-{candidate.Source.ToString().ToLowerInvariant()}-{stem}".Replace(' ', '-');
        var bundleDir = Path.Combine(options.OutputDir, "bundles", taskId);
        var csArm = Path.Combine(bundleDir, "csharp-arm");
        var calorArm = Path.Combine(bundleDir, "calor-arm");
        CopySourceTree(csDir, csArm);
        CopySourceTree(armDir, calorArm);

        var provenance = new TaskProvenance
        {
            Source = candidate.Source,
            Operator = candidate.Operator,
            OperatorDescription = candidate.OperatorDescription,
            MutatedFileRelPath = candidate.FileRelPath,
            Line = candidate.Line,
            Column = candidate.Column,
            OriginalSnippet = candidate.OriginalSnippet,
            MutatedSnippet = candidate.MutatedSnippet,
            RevertedCommit = candidate.RevertedCommit,
            RevertedCommitSubject = candidate.RevertedCommitSubject,
            NativeEligibility = proof,
        };

        var bundle = new TaskBundle
        {
            TaskId = taskId,
            ProjectName = project.ProjectName,
            Provenance = provenance,
            CSharpArmDir = csArm,
            CalorArmDir = calorArm,
            HeldOut = heldOut,
            VisibleTestFilter = HeldOutExtraction.BuildVisibleFilter(heldOut),
            RegressionNetProject = project.SolutionOrProjectFile,
            FailingBehavior = HeldOutExtraction.SynthesizeFailingBehavior(coveringSample),
            EligibilityProof = proof,
        };

        await File.WriteAllTextAsync(Path.Combine(bundleDir, "provenance.json"), TaskGenReportWriter.Serialize(bundle));
        await File.WriteAllTextAsync(Path.Combine(bundleDir, "README.md"), TaskGenReportWriter.BundleReadme(bundle));
        return bundle;
    }

    private static (string outcome, string? signature) OutcomeOf(TestRunResult run, List<HeldOutTest> heldOut)
    {
        // Match by fully-qualified METHOD name (theory-safe): all rows of a held-out theory count.
        var filterNames = heldOut
            .Select(h => string.IsNullOrEmpty(h.FilterName) ? HeldOutExtraction.MethodFqn(h.TestName, h.ClassName) : h.FilterName)
            .ToHashSet(StringComparer.Ordinal);
        var considered = run.Results
            .Where(t => filterNames.Contains(HeldOutExtraction.MethodFqn(t.TestName, t.ClassName)))
            .ToList();

        // No result for the held-out method(s) ⇒ NotRun. NEVER attribute an unrelated failure in the
        // filtered run to the held-out test (review [M]#3) — that null-signature bypass is removed.
        if (considered.Count == 0)
            return ("NotRun", null);

        var failed = considered.FirstOrDefault(t => t.Outcome == "Failed");
        return failed != null
            ? ("Failed", EligibilityPredicate.NormalizeFailureSignature(failed.ErrorMessage))
            : (considered[0].Outcome, null);
    }

    private static EligibilityVerdict Excluded(ExclusionReason reason, string explanation) =>
        new() { Eligible = false, Reason = reason, Explanation = explanation };

    /// <summary>Clone a config with an overridden working directory and/or test filter.</summary>
    private static RoundTripConfig Clone(RoundTripConfig c, string? workDir, string? testFilter = null) => new()
    {
        ProjectName = c.ProjectName,
        OriginalProjectPath = c.OriginalProjectPath,
        LibrarySourceRelativePath = c.LibrarySourceRelativePath,
        SolutionOrProjectFile = c.SolutionOrProjectFile,
        WorkingDirectory = workDir,
        ExcludePatterns = c.ExcludePatterns,
        DotnetPath = c.DotnetPath,
        TargetFramework = c.TargetFramework,
        ExtraBuildProperties = c.ExtraBuildProperties,
        TestTimeout = c.TestTimeout,
        BuildTimeout = c.BuildTimeout,
        TestFilter = testFilter,
    };

    private static void CopySourceTree(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source))
        {
            var name = Path.GetFileName(file);
            if (string.Equals(name, ".git", StringComparison.Ordinal)) continue;
            File.Copy(file, Path.Combine(dest, name), overwrite: true);
        }
        foreach (var dir in Directory.GetDirectories(source))
        {
            var name = Path.GetFileName(dir);
            if (name is "bin" or "obj" or ".git" or ".vs" or ".idea" or "TestResults") continue;
            CopySourceTree(dir, Path.Combine(dest, name));
        }
    }
}
