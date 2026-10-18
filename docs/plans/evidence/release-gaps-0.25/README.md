# 0.25: closing the release-gate gaps 0.24 left open (#1545–#1548)

**Maintainer decision (2026-10-18):** 0.25 re-proves 0.24's checks on its own candidate, and the
0.24 gaps (#1545–#1547) close before 0.25's acceptance. The goal is that 0.25 releases through the
#1410 gate (`scripts/verify_release_adjudication.py`), not through an override.

Delivery: two PRs.

- PR A covers #1545 and part of #1548. Reviews: `reviews/pr-a-*`. It runs under exception
  exceptions EXC-RELEASE-GAPS-A-CHECKER and EXC-RELEASE-GAPS-A-YAML (below).
- PR B, #1563 (merged), covers #1546 and #1547. Reviews: `reviews/pr-b-*`.

## #1545: older tags can read the NuGet key

**Gap.** Every older tag holds a `publish-nuget.yml` without the adjudication gate. That file reads
the repository secret `NUGET_API_KEY`, and `workflow_dispatch --ref v0.9.0` or `gh run rerun`
runs it. A workflow file in an existing tag cannot be edited.

**Why there is no code-only fix.** A tag's workflow file is fixed git content. Nothing committed
to `main` can change what an older file reads. The key leaves those files only when the key itself
changes: the repository secret is deleted and the key lives only in an environment that older
files never enter, and the old key is revoked on nuget.org. These are repository and nuget.org
actions. Deleting tags is not enough: a rerun replays the original commit and ref, and any branch
copy of an old workflow can still ask for the repository secret. Code can do only the half that
`main` controls, and PR A does that.

**Design (code half, PR A).**

- The `publish` job in `publish-nuget.yml` declares `environment: nuget-release`.
- It reads the key as `secrets.NUGET_RELEASE_API_KEY`, a name no older workflow uses. Its first
  step fails the job when the key is empty, so the job stops before it packs, attests, or pushes.
- The key reaches `dotnet nuget push` through `env:`. It is no longer interpolated into the
  script.
- `scripts/check_release_credentials.py` is a read-only check, with tests in
  `scripts/test_check_release_credentials.py` and
  `ReleaseWorkflowGateTests.NugetKeyIsReachableOnlyThroughTheMainOnlyEnvironment` and
  `CiRunsTheCredentialCheckWithThePinnedYamlParser`.
  - With no options, it checks only the workflows. No workflow may name `NUGET_API_KEY`. Only the
    `publish` job may name the new key or enter `nuget-release`.
  - With `--live`, it also reads the settings through `gh api --method GET`. No NuGet secret may
    exist at repository level. The environment must exist, with admin bypass off. Its deployment
    policy must be "selected branches and tags" with exactly one rule, branch `main`. It must hold
    `NUGET_RELEASE_API_KEY`.
  - It exits 0 only when the result is `CLOSED`. A setting it cannot read, or a list whose
    entries do not add up to its `total_count`, is `UNKNOWN`, never `CLOSED`. It reads every page.
  - The workflow check decides on the **parsed YAML**, never on text. It uses PyYAML 6.0.2,
    hash-pinned in `eng/release-credentials-requirements.txt` and loaded with `yaml.SafeLoader`.
    It fails closed (`W012`) when PyYAML is missing, when a workflow does not parse, or when a
    mapping has a duplicate key, including keys that differ only in case. On the parsed workflow:
    - The publish job's `environment` must be exactly `nuget-release` (`W006`).
    - Its first step must equal the committed guard step (`W007`): its `name`, its `env`, and its
      `run` script. So an added `if`, an uncalled shell function, or `exit 0` fails.
    - Its `dotnet nuget push` step must read the key through `env` (`W005`).
    - Every other job's `environment` must equal an allowlisted value exactly, today only the
      `github-pages` deploy (`W004`). Any variant, including a case change such as
      `GITHUB-PAGES`, fails closed. Mentions of `nuget-release` and of the key are matched
      without case.
    - The key may not appear in any other job (`W003`) or outside `jobs` (`W008`).
    - `NUGET_API_KEY` may not appear anywhere (`W002`).
    - Every job-level `uses` must be a local `./.github/workflows/<file>.yml` that exists, with no
      `@ref` (`W011`). This is decided on the parsed value, so folded, quoted, flow-style, and
      multiline flow-mapping forms are all judged by what they mean. A local call's own jobs are
      checked like any other workflow, `secrets: inherit` included.
    - Anchors and aliases are reported (`W009`). Merge keys (`<<`) fail closed at construction
      (`W012`).

    `W010` (a key split across lines) is retired, because the parser decodes those. `W001` means
    `publish-nuget.yml` is missing.
  - CI runs the check and its tests (`scripts/test_check_release_credentials.py`) in
    `test.yml`'s `calor-first-guard` job. It uses a venv holding the pinned parser, installed with
    `--require-hashes`. The C# lane has no PyYAML, so `ReleaseWorkflowGateTests` checks only the
    committed facts and that CI wiring. The mutation cases that used to run in C# now run in the
    Python tests.

### Exception EXC-RELEASE-GAPS-A-YAML (@juanmicrosoft, 2026-10-18)

The EXC-RELEASE-GAPS-A-CHECKER pass (record: `reviews/pr-a-exception-verification-codex.md`) left
one MAJOR finding: a multiline flow-mapping job body got past the line scan. The maintainer chose
to rewrite the check's workflow analysis with a real YAML parser, and granted one more fix plus
exactly one Codex verification pass.

- **Parser:** PyYAML is hash-pinned, and duplicate keys are rejected.
- **Decisions on the parsed structure:** a job-level `uses` must be a local file with no `@ref`.
  The codes keep their meanings. `W005` is now emitted. `W010` is retired, and `W012` is new
  (cannot parse).
- **Tests:** every earlier case is kept as a regression test. Cases where the parser now gives a
  different but still fail-closed answer have updated expected codes. A quoted local call is now
  accepted, because it means the same thing as an unquoted one.
- **New cases:** a multiline flow mapping, duplicate keys, a parse failure, a missing parser, and
  a push step that does not read the key.
- **The pass:** if it reports anything BLOCKING or MAJOR, PR A stops. Record:
  `reviews/pr-a-yaml-verification-codex.md`.

### Exception EXC-RELEASE-GAPS-A-CHECKER (@juanmicrosoft, 2026-10-18)

PR A spent its review budget: 3 rounds plus 1 verification pass. The verification pass left one
MAJOR finding: a folded `uses: >-` call or a quoted `"uses"` key got past `W011`. The maintainer
granted one fix plus one verification-only Codex pass.

- **The fix:** any job-level `uses` key, in any quoting, case, or escape, fails unless its value
  on the same line is a local `./.github/workflows/` path.
- **Tests:** folded, quoted, and flow-style calls fail. A one-line local call is accepted.
- **The pass:** if it reports anything BLOCKING or MAJOR, PR A stops. Record:
  `reviews/pr-a-exception-verification-codex.md`.

**Behavior until the maintainer acts.** GitHub creates an environment named in a workflow on its
first use, with no rules. A publish dispatched before the setup below therefore creates a bare
`nuget-release` and runs the job. The job finds no `NUGET_RELEASE_API_KEY` and fails at its first
step. The repository `NUGET_API_KEY` stays readable by older tags until step 4 below. The check
then reports `S003`/`S004` for the auto-created environment.

**State on 2026-10-18** (`scripts/check_release_credentials.py --live`, read-only):
`OPEN`. It reported `S001` (the repository-level `NUGET_API_KEY` exists) and `S002` (no
`nuget-release` environment).

### Maintainer steps (repository settings; an agent must not do these)

Do them in this order. Publishing fails closed until step 3 is done. Older tags lose the key at
step 4.

1. **Create a new NuGet key** on nuget.org: *API Keys → Create*.
   - Key name: `calor-release` (any name works).
   - Expiration: 365 days or less.
   - Scopes: *Push → Push new packages and package versions*.
   - Packages: select `calor` and `Calor.Sdk`, or use the glob `calor*`.
   - Copy the key. A new key is used because the old one was readable by every ref.
2. **Create the environment.** In GitHub, open *Settings → Environments → New environment* and
   name it `nuget-release`.
   - *Deployment branches and tags*: choose **Selected branches and tags**. Add one rule:
     *Ref type* **Branch**, *Name pattern* `main`. Add no tag rule.
   - *Allow administrators to bypass configured protection rules*: **off**.
   - *Required reviewers* (recommended): add `@juanmicrosoft` and leave *Prevent self-review*
     off. Each publish then waits for one approval click after the test jobs pass.
3. **Store the key in the environment.** On the `nuget-release` page, open *Environment secrets →
   Add environment secret*. Name it `NUGET_RELEASE_API_KEY` and paste the value from step 1. Do
   not add it as a repository secret.
4. **Delete the repository secret.** Open *Settings → Secrets and variables → Actions →
   Repository secrets* and delete `NUGET_API_KEY`.
5. **Revoke the old key** on nuget.org: *API Keys*, the key that was in `NUGET_API_KEY` →
   *Delete*. Actions reads a secret when a run is queued, so a run queued before step 4 can still
   hold the old value. Revocation is what ends that. The check in step 6 cannot see nuget.org, so
   it cannot confirm this step.
6. **Check**, from a checkout of `main`, with the pinned parser in a throwaway venv:

   ```bash
   python3 -m venv /tmp/cred-venv
   /tmp/cred-venv/bin/pip install --require-hashes -r eng/release-credentials-requirements.txt
   /tmp/cred-venv/bin/python scripts/check_release_credentials.py --live
   ```

   Run it with your own `gh` login, because listing secrets needs admin rights. It must print
   `RELEASE CREDENTIALS: CLOSED`. Each code it can print means one thing to fix:
   - `S001`: a repository-level secret whose name contains `NUGET` still exists (step 4).
   - `S002`: the environment is missing (step 2).
   - `S003`: admin bypass is on (step 2).
   - `S004`: the deployment policy is not *Selected branches and tags* (step 2).
   - `S005`: the policy's rules are not exactly branch `main` (step 2).
   - `S006`: the environment has no `NUGET_RELEASE_API_KEY` (step 3).
   - `U00x`: a setting could not be read; rerun with an admin login.
   - `W0xx`: a workflow on this checkout breaks the rules above. `W012` alone, on every file,
     means PyYAML is missing: `pip install --require-hashes -r
     eng/release-credentials-requirements.txt`, in a venv.
7. **Actions permissions, for #1547's residual.** Older refs' benchmark workflows can still push
   a branch and open a PR with `GITHUB_TOKEN`. Open *Settings → Actions → General → Workflow
   permissions*:
   - Choose **Read repository contents and packages permissions**. Workflows without a
     `permissions:` block then get a read-only token.
   - Turn **off** *Allow GitHub Actions to create and approve pull requests*. No workflow, old or
     current, can then open a PR with `GITHUB_TOKEN`.

   Effect on the current code: `benchmark.yml`'s gated publishing step (a dispatch with
   `publish_results=true` and an approving record) still pushes its verified
   `benchmark-results-v<version>` branch. It then fails at `gh pr create`, which marks the job
   red. Open that PR by hand from the pushed branch. No script checks this setting:
   `check_release_credentials.py` does not read it. An older workflow that declares
   `contents: write` can still push a branch. Only the optional ruleset in the #1547 section
   below stops that.

The same settings through the CLI, as an alternative to steps 2–4 and 7. The maintainer runs
these, never an agent:

```bash
gh api -X PUT repos/juanmicrosoft/calor/environments/nuget-release --input - <<'EOF'
{"deployment_branch_policy": {"protected_branches": false, "custom_branch_policies": true},
 "can_admins_bypass": false}
EOF
gh api -X POST repos/juanmicrosoft/calor/environments/nuget-release/deployment-branch-policies \
  -f name=main -f type=branch
gh secret set NUGET_RELEASE_API_KEY --env nuget-release --repo juanmicrosoft/calor   # prompts for the value
gh secret delete NUGET_API_KEY --repo juanmicrosoft/calor
gh api -X PUT repos/juanmicrosoft/calor/actions/permissions/workflow \
  -f default_workflow_permissions=read -F can_approve_pull_request_reviews=false
```

`can_approve_pull_request_reviews=false` is the API name of the *create and approve pull
requests* checkbox.

**What this does not cover.**

- Someone with write access can push straight to `main` unless branch protection stops them
  (`release-gate.md` §5 item 2).
- An administrator can change any of these settings.
- Older tags can still create GitHub releases with `GITHUB_TOKEN`. `release-audit.yml` flags
  them, and a `v*` tag ruleset (§5 item 4) narrows that path.
- A stronger option for later is nuget.org *Trusted Publishing*, which uses GitHub OIDC and
  stores no key. Its policy must name the `nuget-release` environment, because older tags use the
  same workflow file name.

## #1548 (part): the maintainer override is pinned to 0.24.0

0.24.0 shipped through a recorded maintainer override (#1544). The override's check,
`scripts/verify_maintainer_override.py`, used to accept whatever version the committed override
record named. A record edited or added for another version, at a commit on `main`, would have
reopened the override.

- The script now declares `OVERRIDE_VERSION = "0.24.0"`. Any other well-formed version fails with
  `O014`, both in mode selection (`--select-mode`, which every override workflow runs first) and
  in the check itself. This holds whatever the committed records say. Reopening the override now
  takes a reviewed change to the script on `main`.
- The mode stays, because the 0.24.0 release (its body trailer, `release-audit.yml`, and
  `verify-release.yml`) refers to it. 0.24.0 is already on nuget.org, and the existing
  registry-comparison step means an override run can only republish byte-identical packages.
- Tests (`MaintainerOverrideTests`):
  - `ModeSelectionRefusesTheOverrideForAnyOtherVersion` (3 cases);
  - `OverrideForAnyOtherVersionFailsEvenWithAMatchingCommittedRecord` (2), where every committed
    fact agrees with the other version;
  - `TheOverrideVersionIsPinnedInTheScriptNotOnlyInTheRecord`.
- `CommittedOverrideRecordMatchesTheRepository` is now pinned to 0.24.0 instead of reading the
  version from `Directory.Build.props`. Without that, the 0.25 version bump would have failed it.

Out of scope here, with recommendations in the PR report: SDK pinning, Google Fonts, and the
review-round ceilings.

## #1546: the release PR could not consume an identity

**Gap.** The release PR bumps the version and writes the notes before the candidate is frozen, so
it can never carry the adjudication identity. 0.24 inventoried it as a release surface, and
condition 7 failed on it (AD-1).

**Options considered.**

1. *Freeze the candidate first and generate the release PR from it.* This is unsound. The
   candidate must already declare the version (`G009`) and hold the notes the record hashes, so a
   PR generated afterwards changes the candidate.
2. *Make the release PR a metadata step bound to the identity.* It still precedes the identity, so
   the binding could only be checked afterwards. That is option 3 with extra machinery.
3. *Drop the release PR as a publication surface (chosen).* It publishes nothing: no package, tag,
   release, or deploy. Its only products are candidate bytes, and the identity names the candidate.

**Design (PR B).**

- `verify_release_adjudication.py` checks the release PR's products on **every** run, for every
  surface, whether or not `--release-notes` is passed.
  - The version is checked by `G009`, as before.
  - New: the gate renders the candidate's `CHANGELOG.md` `## [<version>]` section with publish-nuget's
    rule. The section must hash to `publication.release-notes.sha256` and pass the wording scan.
    A missing or different section is `G014`; forbidden wording is `G012`.
  - A release PR whose notes differ from what #1408 adjudicated therefore blocks every surface.
- `release-gate.md` §3 (the release-PR row) and §6 record the decision.
- The `create-release` skill now calls this PR the candidate-preparation PR. Its body template
  restates no notes and no benchmark numbers.
- The 0.24 contract is frozen and is not amended. The 0.25 contract must adopt the same
  classification; that is the maintainer's call.
- Tests: `CandidateChangelogThatIsNotTheAdjudicatedNotesFails` (2),
  `CandidateWithoutTheVersionsChangelogSectionFails` (2), and
  `IndependentClaimInTheCandidateChangelogFailsWithoutAnySurfaceOption`. All of them pass no surface
  option.

## #1547: benchmark results were published on the release path

**Gap.** `publish-nuget.yml` dispatched `benchmark.yml` after every gated release. That workflow
pushed the B2 headline to a public branch and opened a PR. So "no benchmark results are published"
was false for 0.24 (AD-3).

**Design (PR B).** Benchmark publication is now a separate, explicitly approved action, with two
independent locks.

1. **Dispatch.**
   - `publish-nuget.yml` no longer dispatches `benchmark.yml` in either mode; it dispatches only
     the website.
   - `benchmark.yml` gains `publish_results` (boolean, default `false`). The publishing step runs
     only on a dispatch with `publish_results == 'true'`. Every other dispatch, and every push run,
     measures and uploads run artifacts only.
2. **Record.** With `--benchmark-worktree`, the gate also requires
   `publication.benchmark-results.approved` to be the JSON literal `true` (`G015`). Adjudicating
   the hashes is not, by itself, approval to publish them. A record without approval still passes
   every other surface.

Tests:

- `BenchmarkPublicationWithoutTheRecordsApprovalFails` (3 cases: missing, `false`, `"true"`);
- `BenchmarkPublicationIsASeparateExplicitDispatch`;
- `ReleasePathKeepsTheTestGatesAndNeverDispatchesBenchmarkPublication`, which also checks that no
  workflow runs `gh workflow run benchmark…`;
- `scripts/test_benchmark_publication_gate.py`, which now allows only the narrowing
  `publish_results` input and checks the step condition.

**Scope: workflows on `main` only.** Workflow files on older refs keep their own publication
logic. For example, `v0.22.0`'s `benchmark.yml` is dispatchable, declares `contents: write`, and
opens a PR with `peter-evans/create-pull-request`. Both run with the write-capable
`GITHUB_TOKEN`, and #1545's NuGet credential isolation does not restrict them. Closing that path
takes repository settings, which the maintainer owns:

- *Settings → Actions → General → Workflow permissions*: set **Read repository contents and
  packages permissions**, and turn **off** *Allow GitHub Actions to create and approve pull
  requests* (maintainer step 7 in the #1545 section). Older workflows that declare
  `contents: write` still get it and can push a branch, but none can open a PR. The gated
  publishing step on `main` still pushes its verified branch, then fails at `gh pr create`. The
  maintainer opens that PR by hand, which suits a separately approved action.
- Optionally, a branch ruleset on `benchmark-results*` that restricts creation. It stops older
  workflows from pushing those branches. It also stops the gated step unless the ruleset's bypass
  list allows it, and in that case publication becomes a fully manual branch push.

Until then, the guarantee is: **no workflow on `main` publishes benchmark results without both
locks.**

The renderer also changed in round 1. publish-nuget used to render the notes with `awk`, which
matched the version as a regular expression. It now uses `scripts/render_release_notes.py`,
which is the gate's own literal `changelog_section`. A version header that appears twice fails
(`G014`, and the renderer exits 1).

**Wording that stays true.** "The release does not publish benchmark results." Run artifacts are
the exception: every `benchmark.yml` run uploads its measurements as workflow artifacts. Anyone
signed in to GitHub can download artifacts on a public repository. Those artifacts are diagnostic
and not a publication surface, but release notes must not claim that no benchmark numbers are
visible anywhere.
