# 0.25: closing the release-gate gaps 0.24 left open (#1545–#1548)

**Maintainer decision (2026-10-18):** 0.25 re-proves 0.24's checks on its own candidate, and the
0.24 gaps (#1545–#1547) close before 0.25's acceptance. The goal is that 0.25 releases through the
#1410 gate (`scripts/verify_release_adjudication.py`), not through an override.

Delivery: two PRs.

- PR A covers #1545 and part of #1548. Reviews: `reviews/pr-a-*`. It runs under exception
  exceptions EXC-RELEASE-GAPS-A-CHECKER and EXC-RELEASE-GAPS-A-YAML (below).
- PR B, #1563 (merged), covers #1546 and #1547. Reviews: `reviews/pr-b-*`.
- The Trusted Publishing PR (2026-10-19) replaces #1545's stored key with nuget.org Trusted
  Publishing (OIDC). Reviews: `reviews/trusted-publishing-*`.

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
`main` controls.

### Design since 2026-10-19: nuget.org Trusted Publishing, no stored key

**Maintainer decision (2026-10-19):** "I'm not creating a key, I'm using trusted publishing which
is the preferred method." No NuGet API key is stored in GitHub at all. The interim design below
(PR A: a key in an environment secret) is superseded.

**nuget.org policy (configured by the maintainer, active):**

| Field | Value |
|---|---|
| Policy name | `calor-release` |
| Package owner | `juanmicrosoft` (user account; the only owner of `calor` and `Calor.Sdk`) |
| Scope | push new packages and package versions |
| Packages | `calor`, `Calor.Sdk` |
| Publisher | GitHub Actions |
| Repository owner | `juanmicrosoft` (ID 85199405) |
| Repository | `calor` (ID 1144664061) |
| Workflow file | `publish-nuget.yml` |
| Environment | `nuget-release` |

**Workflow (`publish-nuget.yml`, job `publish`).**

- It keeps `environment: nuget-release` and its permissions (`contents: write`, `id-token: write`,
  `attestations: write`, `actions: write`). `id-token: write` was already there for the
  attestations; the OIDC exchange needs it too.
- Step 1 is `NuGet/login@8d196754b4036150537f80ac539e15c2f1028841 # v1.2.0` (the official action;
  `v1.2.0` and `v1` both point at that commit on 2026-10-19), with `id: nuget-login` and
  `user: juanmicrosoft`. The user is the nuget.org account that owns the packages and the policy.
  It is a constant in the workflow, not a secret or a variable. The action exchanges the job's
  GitHub OIDC token for an API key that lives one hour, and masks it.
- Step 2 is the fail-closed guard. It reads the action's output through `env` and stops the job
  if it is empty. Both steps run before checkout, so a missing or mismatched policy, or a run
  outside `nuget-release`, stops the job before anything is packed, attested, or pushed.
- The push reads the same output through `env` (`NUGET_OIDC_KEY`), never interpolated into the
  script. In the last successful publish run (38087720778), the push started 1.5 minutes after
  the job began. That is far inside the key's one-hour life.
- No workflow names a NuGet secret. The only remaining mention of the name `NUGET_API_KEY` is the
  action's output, `${{ steps.nuget-login.outputs.NUGET_API_KEY }}`. That name is fixed by
  `NuGet/login` and is not a secret.

**Why an older ref cannot get a key.** Source: NuGetGallery
`src/NuGetGallery.Services/Authentication/Federated/GitHubTokenPolicyValidator.cs`
(`EvaluatePolicyAsync`, last changed 2026-10-01, commit 63b66c98). nuget.org issues a key only
when the GitHub OIDC token matches all of these:

1. `repository_owner` and `repository` equal the policy's values. After the policy's first use,
   `repository_owner_id` and `repository_id` must also match, which stops a deleted-and-recreated
   repository.
2. The workflow **file name**, taken from the `job_workflow_ref` claim between
   `.github/workflows/` and `@`, equals `publish-nuget.yml`. **The ref after `@` is not checked.**
3. When the policy names an environment, the `environment` claim must equal it, ignoring case.
   A token with no environment claim counts as the empty string and does not match.

So:

- **An older tag's `publish-nuget.yml`** has the right file name but declares no environment. Its
  token has no `environment` claim, so nuget.org refuses it. A rerun of an old run replays the
  same file and is refused the same way.
- **A branch or tag other than `main`** whose workflow declares `environment: nuget-release` never
  starts the job. GitHub enforces the environment's deployment rule (branch `main` only, admin
  bypass off) before the job runs, so no token with that claim is ever issued.
- **Any other workflow file**, on any ref, has the wrong file name. A reusable workflow called
  from another repository has a `job_workflow_ref` under that repository, and the prefix check
  refuses it.
- nuget.org does not check the ref. **The environment and its `main`-only rule are therefore
  load-bearing.** If either changes, older tags could get a key again.
  `check_release_credentials.py --live` checks both.

**Residual (unchanged in kind).** Every step of the `publish` job can request an OIDC token
(`id-token: write` applies to the whole job), including the steps that build the adjudicated
candidate. Before this change, the same steps could already mint attestation tokens. The
candidate is the commit #1408 adjudicated, so this trust is the one the gate already places in
it. Splitting the push into its own job would narrow it, but that changes the release gate, so
it is out of scope here.

**Checker (`scripts/check_release_credentials.py`, read-only).** Workflow codes (parsed YAML):

- `W002`: a workflow names `NUGET_API_KEY` or `NUGET_RELEASE_API_KEY` anywhere except the exact
  login-output expression inside the publish job.
- `W003`: another job uses `NuGet/login` or names its output.
- `W005`: no push step reads exactly the login output through `env`.
- `W007`: step 2 is not exactly the guard.
- `W013` (new): the job does not grant `id-token: write`.
- `W014` (new): step 1 is not exactly the pinned login: the SHA, `user: juanmicrosoft`,
  `id: nuget-login`, and no `if` or `continue-on-error`.
- `W015` (new): any other part of the job uses the login or its output.
- `W001`, `W004`, `W006`, `W008`, `W009`, `W011`, and `W012` keep their meanings.

Settings codes (`--live`):

- `S001`: a repository secret whose name contains `NUGET` exists.
- `S002`–`S005`: the environment rules, as before.
- `S007` (new): any environment holds a secret whose name contains `NUGET`.
- `S008`/`S009` (new): the default `GITHUB_TOKEN` is not read-only, or Actions may create or
  approve pull requests.
- `S006` (the environment lacks `NUGET_RELEASE_API_KEY`) is retired.
- An unreadable answer is `UNKNOWN` (`U001`–`U005`).

Reading the Actions workflow permissions needs the *Actions policies* permission. The `gh` login
used for the check (`juanatjcx`) gets HTTP 403 there. A 403 on that endpoint is `V001`, reported
as **`UNVERIFIABLE`** (exit 1), never `CLOSED`. With `--attest-workflow-permissions
docs/plans/evidence/release-gaps-0.25/actions-workflow-permissions-attestation.json`, a valid
maintainer attestation replaces the missing read. The output then says **ATTESTED, not read by
API** (`A001`), with who confirmed it and when. An attestation is used only after a 403. It never
overrides an answer the API gave, and an invalid one is `U006` (`UNKNOWN`). Tests:
`scripts/test_check_release_credentials.py` (40 tests, every earlier case kept).

**Interim design (PR A, 2026-10-18; superseded by the section above).**

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

### State on 2026-10-19 (after the maintainer's setup)

**Checked by API** (read-only `GET`, `gh` login `juanatjcx`):

- Repository secrets: only `ANTHROPIC_API_KEY` and `VSCE_PAT`. `NUGET_API_KEY` is gone.
- Environment `nuget-release` exists, with `can_admins_bypass: false`. Its deployment policy is
  custom branch policies with a single rule, branch `main`.
- No environment (`nuget-release`, `github-pages`) holds a secret whose name contains `NUGET`.
  `nuget-release` holds no secrets at all.

**Confirmed by @juanmicrosoft on 2026-10-19, not checkable by API with this login:**

- Actions workflow permissions are read-only, and *Allow GitHub Actions to create and approve pull
  requests* is off. `GET .../actions/permissions/workflow` answers HTTP 403 to this login.
  Recorded in `actions-workflow-permissions-attestation.json`.
- The old NuGet API key is revoked on nuget.org.
- The nuget.org Trusted Publishing policy `calor-release` is active with the values in the table
  above.

**`scripts/check_release_credentials.py --live`, 2026-10-19:**

- Without an attestation: `RELEASE CREDENTIALS: UNVERIFIABLE`, exit 1. The only finding is `V001`,
  the HTTP 403 on the workflow permissions. Every other check passed.
- With `--attest-workflow-permissions
  docs/plans/evidence/release-gaps-0.25/actions-workflow-permissions-attestation.json`:
  `RELEASE CREDENTIALS: CLOSED (workflows and repository settings; Actions workflow permissions
  ATTESTED by the maintainer, not read by API (A001 above))`, exit 0. Line `A001` names
  @juanmicrosoft and 2026-10-19.

(Earlier state, 2026-10-18: `OPEN`, with `S001`, the repository `NUGET_API_KEY` exists, and
`S002`, no `nuget-release` environment.)

**Behavior if a setting regresses.**

- If the environment loses its `main`-only rule, or is deleted (GitHub recreates a referenced
  environment on first use, with no rules), any branch can push a copy of `publish-nuget.yml`
  that enters `nuget-release` without the #1410 gate. nuget.org would issue that copy a key,
  because the file name and environment match. Older tags stay refused, because their files name
  no environment. This is why `S002`–`S005` matter.
- If the policy is deleted or changed on nuget.org, the exchange fails at step 1 and nothing is
  published.

### Maintainer checklist (an agent must not do these)

Done on 2026-10-19:

1. [x] **nuget.org Trusted Publishing policy.** *Account → Trusted Publishing → Add*:
   - name `calor-release`;
   - owner `juanmicrosoft`;
   - repository owner `juanmicrosoft`;
   - repository `calor`;
   - workflow file `publish-nuget.yml` (file name only);
   - environment `nuget-release`.

   The environment field is required for this repository. nuget.org ignores the ref, so without
   it every older tag's `publish-nuget.yml` would match. No API key is created.
2. [x] **Environment `nuget-release`.** *Settings → Environments*:
   - *Deployment branches and tags*: **Selected branches and tags**, with one rule, *Branch* `main`,
     and no tag rule;
   - *Allow administrators to bypass configured protection rules*: **off**;
   - no environment secrets.

   *Required reviewers* (`@juanmicrosoft`, *Prevent self-review* off) is recommended but not
   checked.
3. [x] **Delete the repository secret `NUGET_API_KEY`.** *Settings → Secrets and variables →
   Actions*.
4. [x] **Revoke the old key** on nuget.org (*API Keys → Delete*). A run queued before step 3 could
   still hold the old value; revocation ends that. Not checkable by this script.
5. [x] **Actions permissions** (also #1547's residual). *Settings → Actions → General → Workflow
   permissions*: **Read repository contents and packages permissions**, and *Allow GitHub Actions
   to create and approve pull requests* **off**.
   - The gated benchmark publishing step on `main` still pushes its verified branch and then fails
     at `gh pr create`. Open that PR by hand.
   - An older workflow that declares `contents: write` can still push a branch. Only the optional
     ruleset in the #1547 section stops that.

Remaining:

6. [ ] **Merge this PR.** Until it merges, `main`'s `publish` job still reads
   `secrets.NUGET_RELEASE_API_KEY`, which no longer exists, so a publish fails at its first step.
   That is fail-closed.
7. [ ] **Check from `main` after the merge**, with the pinned parser in a throwaway venv and your
   own `gh` login:

   ```bash
   python3 -m venv /tmp/cred-venv
   /tmp/cred-venv/bin/pip install --require-hashes -r eng/release-credentials-requirements.txt
   /tmp/cred-venv/bin/python scripts/check_release_credentials.py --live \
     --attest-workflow-permissions docs/plans/evidence/release-gaps-0.25/actions-workflow-permissions-attestation.json
   ```

   It must print `RELEASE CREDENTIALS: CLOSED`, and the `A001` line must name your confirmation.
   Without the flag it prints `UNVERIFIABLE` (`V001`), because your login cannot read the
   workflow permissions. If a token with the *Actions policies* permission is available, run
   without the flag: then `S008`/`S009` are read from the API. The codes:
   - `S001`: a repository-level secret whose name contains `NUGET` exists (step 3).
   - `S002`–`S005`: the environment is missing, admin bypass is on, or the branch rule is not
     exactly `main` (step 2).
   - `S007`: some environment holds a NuGet secret; delete it (step 2).
   - `S008`/`S009`: the Actions permissions (step 5).
   - `V001`: workflow permissions refused (HTTP 403) and no attestation passed.
   - `U00x`: unreadable; rerun with an admin login. `U006`: the attestation file is invalid.
   - `W0xx`: a workflow on this checkout breaks the rules. `W012` on every file means PyYAML is
     missing.
8. [ ] **First publish.** The first successful exchange records the repository IDs in the policy,
   which nuget.org shows as permanently active. The IDs are already shown, so this is expected to
   be a no-op. If the policy ever shows "temporarily active", it expires 7 days after creation
   unless a publish uses it.
9. [ ] **Re-attest when the settings change.** The attestation is dated. If the Actions
   permissions are changed and changed back, update `confirmed_on`.

The same GitHub settings through the CLI, for reference. The maintainer runs these, never an agent:

```bash
gh api -X PUT repos/juanmicrosoft/calor/environments/nuget-release --input - <<'EOF'
{"deployment_branch_policy": {"protected_branches": false, "custom_branch_policies": true},
 "can_admins_bypass": false}
EOF
gh api -X POST repos/juanmicrosoft/calor/environments/nuget-release/deployment-branch-policies \
  -f name=main -f type=branch
gh secret delete NUGET_API_KEY --repo juanmicrosoft/calor
gh api -X PUT repos/juanmicrosoft/calor/actions/permissions/workflow \
  -f default_workflow_permissions=read -F can_approve_pull_request_reviews=false
```

`can_approve_pull_request_reviews=false` is the API name of the *create and approve pull
requests* checkbox.

**What this does not cover.**

- Someone with write access can push straight to `main` unless branch protection stops them
  (`release-gate.md` §5 item 2). A workflow pushed to `main` can enter `nuget-release`.
- An administrator can change any of these settings, or the nuget.org policy.
- Older tags can still create GitHub releases with `GITHUB_TOKEN`. `release-audit.yml` flags
  them, and a `v*` tag ruleset (§5 item 4) narrows that path.
- The check cannot see nuget.org: neither the policy's values nor the old key's revocation.
- Every step of the `publish` job can request an OIDC token, including the candidate's build
  steps (see "Residual" above).

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
  requests* (maintainer checklist step 5 in the #1545 section; done 2026-10-19). Older workflows that declare
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
