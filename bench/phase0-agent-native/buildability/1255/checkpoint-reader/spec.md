# Checkpoint preview

Implement `Checkpoint.PreviewOther(Checkpoint other, int version)`.
Return the next version for the other checkpoint's standard configuration.
The next version is `version + 1`. Use `AfterVersion`, which returns zero
when the reader does not advance; preserve the dependency and public surface.

## Dependency quick start

A checkpoint owns the callback `readVersion`. For another checkpoint,
pass `other.readVersion` to `AfterVersion`. Checkpoints use the standard
configuration and do not change during a preview.
