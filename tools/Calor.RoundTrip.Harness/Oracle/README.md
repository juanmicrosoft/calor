# Member-level differential oracle

`calor-roundtrip oracle` (#1587) builds each corpus subject twice per converted file: the original,
and the same project with only that file swapped for its round-tripped C#. It loads both builds
into fresh collectible load contexts, drives every member it can plan, and compares what it
observes. The CI job is `member-oracle` in `.github/workflows/test.yml`.

| File | Role |
|---|---|
| `OraclePlanner.cs` | What is driven: types, closings, members, receivers, suffix segments |
| `MemberOracleInputs.cs` | The input generator (`1587-member-inputs-v1`) |
| `MemberDifferentialOracle.Run.cs` | Running a member on one build; the observation and its parts |
| `MemberDifferentialOracle.cs` | Comparing parts, judging a member, classifying timeout replays |
| `StateRenderer*.cs` | Rendering values, state, exceptions, tasks, streams and delegates |
| `OracleRunner*.cs` | Per-file child processes, restarts, replays, the gate and the report |
| `OracleModel.cs`, `OracleBaseline.cs` | Verdicts, options, skip categories, the baseline file |

## What one input observes

Each input of a member produces one observation made of named parts. Every part is compared on
its own:

| Part | Content |
|---|---|
| `result` | The return value (tasks and `ValueTask`s awaited, async streams and iterators drained, delegates called) or the thrown exception: type, message, `Data`, the exception type's own public state, inner exception |
| `args` | Every argument after the call, each at the full state depth (`ref`/`out` included) |
| `this` | The receiver after the call |
| `statics` | The member's type's public statics |
| `project` | Every project type's public statics, generic types closed over each closing |
| `private-statics` | Every project type's non-public static fields (delegates rendered without calls) |
| `stdout`, `stderr` | Console output, through process-wide routers retargeted per input |
| `files` | Files (by content hash) and directories created in the member's sandbox |
| `setup` | Present only when the receiver, the arguments or the observation could not be set up |

Strings have the per-run sandbox root replaced by `<sandbox>`, so a message naming a file in the
sandbox is stable.

## Outcome paths

Every way a member comparison can end. "Hides?" says whether the path can hide a real behavior
change; each such path has a mutation fixture in
`tests/Calor.RoundTrip.Harness.Tests/MemberOracleAuditTests.cs` (#1667) that asserts the change is
reported, or that the member is excluded with a reason that keeps it out of coverage. Coverage is
`OracleRunner.Compared`: AGREE, DIFFERENT, and TIMEOUT or NONDETERMINISTIC members with at least
one compared input. Every other verdict carries a `SkipCategory`.

### Planning (`OraclePlanner`, `MemberDifferentialOracle.SharedPlan`)

| # | Outcome | When | Hides? | Fixture |
|---|---|---|---|---|
| P1 | DIFFERENT `SURFACE` | A member exists in one build only, or a type's kind, visibility, base chain or interfaces differ | no | `SurfaceDifference_IsDifferent`, `InheritedMemberAndHierarchyChanges_AreDifferent` |
| P2 | SKIPPED `open-generic` | No closing argument (`object`, `string`, `int`) meets a generic type's or method's constraints | yes, excluded | `GenericNoClosingFits_IsExcludedFromCoverage` |
| P3 | AGREE on one closing only | A generic is closed over a reference type; a change that shows only for a value type | **was a false negative**; now also closed over `int`, nested types named with their closed parent | `GenericMethod_DifferenceOnlyForAValueType_IsDifferent`, `GenericType_DifferenceOnlyForAValueType_IsDifferent`, `NestedTypeOfAGenericType_IsDrivenForEachClosing`, `StaticOfAGenericTypeClosedOverInt_IsObserved` |
| P4 | SKIPPED `interface-member` | An interface's instance member (driven through implementing types) | yes, excluded; the implementer is driven | `InterfaceMember_IsDrivenThroughItsImplementer`, `InterfaceImplementedByABaseClass_IsDrivenOnTheDerivedType`, `ExplicitGenericInterfaceImplementation_IsDriven`, `DefaultImplementationReplacedByAnExplicitOne_IsDifferent` |
| P5 | SKIPPED `abstract-receiver` | An abstract type's instance member (driven through concrete subclasses as an inherited member) | yes, excluded; the subclass is driven | `AbstractBaseMember_IsDrivenThroughItsSubclass` |
| P6 | SKIPPED `no-receiver` | No public constructor, none the generator can supply, no generated tuple constructs one, or receiver construction timed out | yes, excluded | `NoReceiver_IsExcludedFromCoverage` |
| P7 | SKIPPED `unsupported-parameter` | A parameter type the generator cannot supply | yes, excluded | `UnsupportedParameter_IsExcludedFromCoverage` |
| P8 | SKIPPED `byref-return` | A by-ref, ref-struct or pointer return | yes, excluded | `ByRefReturn_IsExcludedFromCoverage` |

### Judging a member from its four runs (`MemberDifferentialOracle.Judge`)

The runs are original, converted, original, converted, each in a fresh load context. Per input
(`CompareParts`), a part is *stable* when each build reproduces it on its re-run. A stable part
that differs between the builds is a difference. An unstable part is set aside alone. A part
identical on both builds but rendered incompletely is incomplete. The input counts as compared
when its primary part (`result`, or `statics` for a type's statics) is stable and complete. A
setup failure identical on both builds is not compared. The member's verdict is the first that
applies:

| # | Outcome | When | Hides? | Fixture |
|---|---|---|---|---|
| J1 | DIFFERENT `OBSERVATION` | Some input has a stable part that differs, whatever other parts or inputs do | no | `StableInputDifference_IsDifferentEvenWhenAnotherInputIsUnstable`, `GuidStatic_DoesNotHideAStableReturnDifference`, `NondeterministicReceiver_DoesNotHideAStableResultDifference`, `IncompleteSecondaryPart_DoesNotHideAStableResultDifference`, `IncompleteOnOneBuildOnly_IsDifferent` |
| J2 | DIFFERENT `ONE-SIDED-NONDETERMINISM` | A part one build reproduces on every input, the other build does not reproduce on some input | **was a false negative** (NONDETERMINISTIC, skipped) | `OneSidedNondeterminism_ConvertedUnstable_IsDifferent`, `OneSidedNondeterminism_OriginalUnstable_IsDifferent`, `OneSidedNondeterminism_FingerprintIsStable` |
| J3 | SKIPPED `incomplete-observation` (INCOMPLETE) | A part identical on both builds could not be rendered in full (more than 1,000,000 items, more than 256 MiB, a delegate the generator cannot call) | yes, excluded | `IncompleteResult_IsExcludedFromCoverage`, `IncompleteSecondaryPart_IsNeverAgreement`, `PrivateStaticDelegate_DoesNotMakeObservationsIncomplete` |
| J4 | NONDETERMINISTIC | No input compared, and some part was unstable on both builds | yes, excluded | `NondeterministicOnBothBuilds_IsExcludedFromCoverage` |
| J5 | TIMEOUT (prefix) | The member was limited at input k; the inputs before k are compared; then classified by replays (R1 to R8) | the failing input only; later inputs run as a segment (S1) | `SharedTimeout_StillComparesTheFinishedPrefix`, `TimeoutWithNoComparedInput_IsExcludedFromCoverage` |
| J6 | SKIPPED `no-input-compared` | No input compared: setup failed on both builds (for example the receiver throws on every call) | yes, excluded | `ReceiverFailingOnEveryCall_IsExcludedFromCoverage` |
| J7 | AGREE | Every compared input agrees; the reason notes inputs not compared and parts set aside | only on inputs not compared (setup failed identically) | `Agree_InputsWithSetupFailures_DoNotHideADifferenceOnTheOthers`, `Agree_IdenticalBuilds_CountAsCoverage`, and every observation fixture below |

AGREE (J7) is only as strong as what the observation renders. These fixtures inject a change in
each channel; the ones marked fixed were AGREE (or TIMEOUT, NONDETERMINISTIC) before #1667:

| Channel | Fixtures |
|---|---|
| Exceptions of one type with different messages or data (**fixed**: only the type was compared) | `ThrownException_SameTypeDifferentMessage_IsDifferent`, `ThrownException_SameMessageDifferentData_IsDifferent`, `ThrownException_DifferentInnerException_IsDifferent`, `ThrownException_SubjectExceptionProperty_IsDifferent`, `ThrowingConstructor_DifferentMessage_IsDifferent`, `ThrowingGetter_DifferentMessage_IsDifferent`, `FaultedTask_DifferentMessage_IsDifferent`, `ExceptionNamingTheSandbox_IsStable` |
| Static state (**fixed**: private statics, and `Cache<int>`-style closed generics) | `PrivateStatic_WrittenAfterItsOnlyReader_IsDifferent`, `StaticOfAGenericTypeClosedOverInt_IsObserved`, `PublicStaticOfAnotherType_IsDifferent`, `MutationOfAnotherTypesStatics_IsCompared` |
| Console and files (**fixed**: stdout and stderr were one buffer; a writer captured from `Console.Out` lost later output; directories were not listed) | `Console_StdoutVersusStderr_IsDifferent`, `Console_WriterCapturedInATypeInitializer_IsObservedOnLaterInputs`, `FileContent_IsDifferent`, `CreatedDirectory_IsDifferent` |
| Async results (**fixed**: `ValueTask` was not awaited; async streams were opaque) | `ValueTask_StateAfterCompletion_IsDifferent`, `ValueTaskResult_IsDifferent`, `Task_ReceiverStateAfterCompletion_IsDifferent`, `AsyncStream_ItemsAreCompared`, `AsyncResult_IsAwaitedBeforeComparison` |
| Returned delegates and iterators (**fixed**: a delegate's exception was compared by type; a returned `IEnumerator` was opaque) | `ReturnedDelegate_ThrowingDifferentMessages_IsDifferent`, `ReturnedDelegate_CapturedStateDifference_IsDifferent`, `ReturnedIterator_ItemsAreCompared`, `ReturnedDelegate_IsCalled`, `DelegateSideEffects_AreCompared` |
| Depth-2 state (**fixed**: the argument list consumed a level, so arguments were seen one hop deep) | `ArgumentStateAtDepth2_IsDifferent`, `ReceiverStateAtDepth2_IsDifferent`, `ReturnStateAtDepth2_IsDifferent`, `StateBeyondTheDepth_IsTheDocumentedBound` |
| Fingerprint aliasing (**fixed**: labels lacked the input index, and every culture rendered alike, so a moved difference kept its fingerprint) | `Fingerprint_DistinguishesInputsThatRenderAlike`, `Fingerprint_NamesCultureInputs`, `Fingerprint_CoversEveryDifferingInput`, `FailureFingerprint_UsesTheNormalizedMessage` |

### Timeouts, crashes and replays (`ClassifyTimeout`, `OracleRunner.Child.cs`)

A member input that times out (or, in a child process, crashes) restarts the comparison with the
member limited at that input k. The input is then replayed from clean state, twice per build:
the preceding members, then the member's inputs up to k, in one sandbox.

| # | Outcome | When | Hides? | Fixture |
|---|---|---|---|---|
| S1 | Suffix segment `Member [inputs k+1..]` | Every limited member gets a segment for the inputs after k, judged like a member (up to 3 segments) | **was a false negative**: inputs after k were never compared | `SharedTimeout_InputsAfterTheFailingOneAreCompared`, `Segments_AreBoundedAndDeterministic` |
| R1 | INCONCLUSIVE | A replay never reached the input (infrastructure); the gate fails it | no (fails) | `Replay_NeverReachingTheInput_FailsTheGate`, `TimeoutClassification_InfrastructureFailureStaysInconclusive` |
| R2 | DIFFERENT `OBSERVATION` | All four replays finished and a stable part differs | no | `Replay_StableDifferenceInASecondaryPart_IsDifferent`, `ReplayedInput_StablePartDifferenceFailsDespiteANondeterministicReturn` |
| R3 | NONDETERMINISTIC | A build's two replays disagree in termination or in the primary part | yes; prefix still counted | `TimeoutClassification_FollowsTheCleanStateReplays` |
| R4 | DIFFERENT `HANG` / `CRASH` | One build fails both replays, the other finishes both within a quarter of the budget | no | `ConvertedHang_IsDifferent`, `SequenceDependentHang_IsReplayedWithItsPrefix`, `FilesystemDependentHang_IsReplayedInOneSandbox` |
| R5 | DIFFERENT `FAILURE-MODE` | Both builds fail, one by hanging and one by crashing | **was a false negative** (shared TIMEOUT) | `Replay_HangVersusCrash_IsDifferent` |
| R6 | TIMEOUT, both fail | Both builds fail input k the same way | the failing input only | `TimeoutClassification_FollowsTheCleanStateReplays` |
| R7 | TIMEOUT, did not reproduce | The replays finish and agree, or one build finishes slowly while the other fails | the failing input only | `TimeoutClassification_FollowsTheCleanStateReplays` |
| R8 | DIFFERENT prefix, replay folded in | The prefix already differs; the replay outcome joins its fingerprint | no | `TimeoutClassification_ChangesTheFingerprintOfADifferingPrefix` |
| C1 | NONDETERMINISTIC, unreproduced crash | The file's process crashed in the member once and the traced re-run finished: AGREE becomes NONDETERMINISTIC | no: a DIFFERENT verdict is kept | `UnreproducedCrash_NeverHidesADifference` |
| C2 | File failure | The child crashed outside a member, kept failing, or the swap does not build; the gate fails the file | no (fails) | `Gate_FileFailure_Fails`, `Gate_SwapBuildFailure_FailsEvenForANewFile` |

### Gate tolerances (`OracleRunner.ApplyGate`)

A DIFFERENT member passes the gate only when its file's conversion records a lossy or dropped
construct, or when `eng/member-oracle-baseline.json` lists it with the same fingerprint. The
fingerprint hashes every differing input (its index, its label and both observations), so a
difference that changes or moves fails. See `Gate_*` in `MemberOracleTests.cs`.

## Coverage and the skip distribution

Two checks keep coverage loss from passing as a smaller AGREE count. Both read
`eng/member-oracle-expected.json`, which `--write-expected` rewrites from an unsharded run:

- **Per file**: an expected file must be exercised with at least 95% of the member comparisons it
  recorded (`projects`).
- **Per project** (#1667): the distinct members not compared, per skip category (`skips`), may rise
  by at most 2 members or 5%, whichever is more. This catches what the per-file floor cannot: a
  rise within 5% in every file, members that move from one skip reason to another, and new members
  that cannot be driven. It runs unsharded only.

The report (`<Project>-member-oracle.json`, `skipDistribution`) and the job summary show the
distribution next to the recorded one.

## Known limits

These can still hide a change. They are bounds of the design, not paths that report AGREE
silently on evidence they hold:

- **Depth.** State is rendered two member hops deep (`--depth`). A change three hops inside a
  value is not seen (`StateBeyondTheDepth_IsTheDocumentedBound`).
- **Private instance state.** An object's non-public instance fields are not rendered. A change to
  them shows only through public state or later behavior of the same object.
- **Inputs.** AGREE means no disagreement on the generated inputs, not a proof. A delegate is called
  on at most 8 generated inputs.
- **Files outside the sandbox.** Only the member's working directory is listed.
- **A failing input's own effects.** Output written before an input hangs is not compared; the
  input is classified by its replays.
- **Slow versus hung.** A build that finishes in more than a quarter of the budget while the other
  hangs is TIMEOUT, not HANG, because the timing cannot separate them reliably.
- **Replays.** One-sided nondeterminism (J2) applies to the main runs. In replays, a build whose
  replays disagree makes the member NONDETERMINISTIC (R3).
- **Segments.** After the third failing input of a member, its remaining inputs are dropped.
- **Closings.** Generics are closed over `object` (or `string`) and `int` only.
