# Telemetry privacy/security review checklist

Complete this checklist for every telemetry schema or transport change.

- [ ] State the purpose and necessity of every new event and field.
- [ ] Keep event names, property names, metric names, and global context fields
      in the explicit versioned allowlist.
- [ ] Use fixed low-cardinality values or numeric aggregates; do not accept
      free-form caller values.
- [ ] Confirm source, generated code, identifiers, literals, paths, project
      names, arguments, environment values, diagnostic/exception text, stacks,
      hashes, hostnames, IPs, and stable user/machine IDs cannot enter payloads.
- [ ] Add canary regression coverage at each new source-derived boundary.
- [ ] Assert serialized Application Insights payloads, not only in-memory
      objects.
- [ ] Verify default-off, explicit opt-in, opt-out override, preview-only, and
      missing/invalid endpoint behavior.
- [ ] Verify schema/redaction/serialization/channel failures fail closed
      without changing command behavior.
- [ ] Update `docs/telemetry.md` and the complete payload inventory.
- [ ] Intentionally update `docs/telemetry-schema-v1.json`; the snapshot test
      must fail if runtime fields change without this update.
- [ ] For a breaking removal/rename or changed meaning, create a new schema
      version and compatibility test instead of silently changing version 1.
- [ ] Confirm the operator, destination, and retention statement remain true.

Review approval should explicitly mention privacy/security review completion in
the pull request description or review record.
