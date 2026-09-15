# Independent final-candidate review records

The original 2026-09-14 reviews assessed merged scalar activation
`a4898048695587e6e0b1f8710db9f9af4ccef617`, tree
`ff8b3b369f82c819f4fcda53e132ee948891664e`, and correctly certified a
technical **BLOCK**. They found the two Serilog compatibility failures that
repair PR #1459 subsequently resolved. Those reviews are historical and do not
count as reviews of the repaired candidate.

The candidate adjudicated by this packet is merged commit
`e3a84766dcc9f34c104445d1150552499c3d3ce3`, tree
`cc8caed0d4a897ca8bda3c7979b28e1fb8baa23b`. PR #1459 head
`562259c528494ace7d5130dd6382f46b3b3f4a29` and CI merge
`915bfb37181f0b18aa990ef5cc2c75b166c5b2ae` have that same tree.

## Repaired-candidate review cycle

Two separately initiated reviews examined the repaired candidate, the complete
packet, issue #1386, and retained base/final CI round-trip artifacts.

### Compiler-integration review

- Reviewer: synchronous `rubber-duck` specialist; runtime context ID and exact
  model build were not exposed to the coordinator.
- Lens: compiler integration, tree bridges, routing, mode/cache/API/LSP/MSBuild
  evidence, test accounting, and technical PASS.
- Findings:
  1. Classify three additional `Calor0208` identities inside two
     already-rejected FluentValidation files.
  2. Replay the manual surface matrix against the repaired Release binary.
  3. Replace the stale pre-repair review record.
  4. Regenerate the packet hash manifest.
- Disposition: the diagnostic-only deltas are now retained in
  `roundtrip-comparison.json`; the 99 root-CLI and 14 entrypoint rows were
  replayed against compiler SHA-256
  `2f2553aa9afad80e873f7967802208914f4e45480c3c7db08b68c332cbef1f49`;
  105 Tasks/MSBuild tests passed; this record was replaced; the manifest is
  regenerated after certification.
- Limitation: the reviewer inspected retained artifacts and source but did not
  rerun the suites.

### Adversarial compatibility review

- Reviewer: separately initiated general-purpose context using requested model
  `claude-opus-4.8`; runtime context ID and exact model build were not exposed.
- Lens: corpus denominators, per-file deltas, repaired controls, flake policy,
  limitations, integrity, and governance.
- Findings:
  1. The quantitative PASS core recomputed exactly: no hidden denominator
     change, exactly two status deltas, both repaired blockers at base parity,
     and one declared clean attempt per CI leg.
  2. The review record still described the pre-repair BLOCK.
  3. The SHA-256 manifest was stale.
  4. A historical `warm-cache.json` reference was dangling.
  5. Governance omitted repair PR #1459.
- Disposition: the stale record was replaced, the cache measurement is
  referenced only through retained `surface-matrix.json`, governance names
  #1459, and the hash manifest is regenerated after final certification.
- Limitation: the reviewer recomputed retained artifacts and git metadata but
  did not rerun compiler or corpus suites.

## Final certification after dispositions

Two fresh, separately initiated contexts reviewed the corrected packet after
all findings above were addressed.

- **Compiler-integration certification:** synchronous `rubber-duck`
  specialist; runtime context ID/model build not exposed. It independently
  confirmed the corpus counts, paired tests, equal-input provenance, tree
  bridges, diagnostic-only classifications, final repaired compiler hash,
  99/14 CLI replays, 105 Tasks/MSBuild replay, review/governance corrections,
  and technical PASS. Its only remaining finding was the intentionally
  last-step stale `sha256.json`; this manifest is regenerated after this
  certification record. Limitation: retained-artifact and source inspection,
  not a fresh suite rerun.
- **Adversarial compatibility certification:** separately initiated
  general-purpose context using requested model `claude-opus-4.8`; runtime
  context ID/exact build not exposed. It recomputed all five-subject
  denominators, exactly two status improvements, repaired-blocker parity,
  one-attempt flake policy, tree bridges, surface provenance, governance, and
  test accounting. Verdict: **PASS, no blockers**. It noted same-count
  diagnostic rendering changes in five already-rejected files; these are now
  retained in `roundtrip-comparison.json`. Limitation: no fresh compiler or
  corpus rerun within the review context.

## Evidence ownership and maintainer disposition

- Evidence owner: GitHub Copilot CLI session
  `183563e9-b822-44f0-843f-4ffee2c11e19` on branch
  `evidence/1386-scalar-activation-adjudication`; exact runtime model build is
  not exposed.
- Implementation owners: PRs #1458 and #1459 were authored and merged by
  `@juanmicrosoft`; final repaired merge
  `e3a84766dcc9f34c104445d1150552499c3d3ce3`.
- Maintainer adjudication: not fabricated. The packet records technical PASS;
  issue closure remains a maintainer action after the evidence PR is green.
