# B2 (#1422) PR 2 verification pass (Codex, read-only, reasoning effort high)

Command: `codex exec -s read-only --ephemeral -c model_reasoning_effort="high" "<prompt>" < <git diff origin/main...HEAD>`
on the final diff at `22c803ae`, run 2026-10-08 after the Codex usage limit reset. The first
attempt on 2026-10-07 at 19:02 stopped at the usage limit with no output. The prompt asked the
reviewer to confirm that every round 1 to 3 fix is still in place and that no later commit
regressed anything. It also asked for a check that the claims in the README PR 2 section,
`local-e2e-pr2.md`, `mutation-check-pr2.txt`, and CHANGELOG `[Unreleased]` match the code.
Verdict: **REQUEST-CHANGES** (1 MAJOR, no BLOCKING).

| # | Severity | Finding (summarized) | Response |
|---|---|---|---|
| 1 | MAJOR | Main could add a new file with another part of `EvidenceContractValidator` without editing any listed input. For example, an overload `ValidateBenchmarkResults(Dictionary<string,string> ...)` would make the unchanged call at `BenchmarkResultsTests.cs:160` bind to it instead of the `IReadOnlyDictionary` overload. B2-08 would report fresh inputs while main's validator behaved differently. The README limit claimed that callers would have to change, which is false. | Fixed in `bca13caf`. `partial_validator_files` lists every `.cs` file under `tests/Calor.Compiler.Tests` that declares a part of `EvidenceContractValidator`, `EvidenceContractTests`, `BenchmarkResultsTests`, or `BenchmarkRegistrationTests`, at HEAD or on main (`git grep -E`). Each such file is a freshness input, so a part added only on main is absent at HEAD and refuses. New test: `test_a_new_validator_partial_class_file_on_main_is_refused` (a part in `EvidenceContract/` and one elsewhere in the project). Mutation P22 is killed. The README limit is corrected: the match is textual, unusual formatting is a stated gap, and non-partial types cannot affect these static calls. |

Under the coordinator's instruction, no further review round was run after this fix. After the
fix: 64 tests OK, 22 of 22 mutations killed, and the local end-to-end rerun on `bca13caf` gave all
scenarios as expected (`local-e2e-pr2.md`; new hashes, with the same determinism pattern).
