# Trusted Publishing PR (#1545): Codex verification pass

This pass reviewed commit 980ca769d, which holds the round-3 fixes, against base a6d5c2f0d. The
review budget was three rounds plus this pass; this is the last review. The command was
`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`. Raw final message:
`trusted-publishing-verification-pass-codex.raw.txt`.

**Verdict: APPROVE.** There are no BLOCKING or MAJOR findings. Under the stated policy and
settings, Codex found no credential path outside the gated publish job.

**Codex verified:**

- **Round-3 fixes.** Each of these fails closed:
  - a name listed twice, including duplicates that differ only in case;
  - an invalid `total_count`;
  - a `total_count` that changes between pages;
  - more than 100 pages.

  The HTTP status comes from gh's status line, so message text cannot admit the attestation.
- **Earlier fixes still hold.**
  - These still fail closed: an omitted environment, an unnamed entry, a repeated JSON member,
    and an invalid attestation date.
  - An unattested 403 is `V001`, and a valid attestation is labelled `A001`.
  - An API answer always takes precedence over an attestation.
- **OIDC reasoning.** NuGetGallery checks three things: the repository identity, the workflow
  file name, and the required environment. It ignores the ref. GitHub's deployment rule supplies
  the branch restriction, and the corrected reusable-workflow text is consistent with this.
- **Workflow.** These are correct:
  - the action pin, the `user` input, and the output name;
  - masking of the key;
  - the unconditional guard;
  - the push, which reads the key through `env`.

  A key that expires before the push fails closed.
- **Regressions.** The release-gate changes are confined to credential handling. Retiring the S006
  test matches the retired stored-key design. The C# test methods are intact, and the docs and
  checklist state the required controls and the limits of the check.

**Codex's validation limits.** 103 in-memory checks passed. The sandbox could not create a `/tmp`
venv and had no PyYAML, so Codex reviewed the YAML cases from source. The author ran the full suite
locally with the hash-pinned PyYAML: 45 tests OK. CI runs it too.
