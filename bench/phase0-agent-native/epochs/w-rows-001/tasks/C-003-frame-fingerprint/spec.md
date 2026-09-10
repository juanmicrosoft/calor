# Ordered frame fingerprint

Implement `FrameCodec.Fingerprint(int first, int second)`. Normalize both
keys using the supplied codec library, then return `101 * left + right`.
Order matters: do not sort the keys or combine them before normalization.
Keep the normalization implementation and public surface unchanged.

Normalization clamps an integer key to the inclusive range zero through
one hundred. Do not duplicate that implementation in the new method.

## Dependency quick start

The codec's normal key adapter is `this.normalize(key)`. It accepts one
key per call. The dependency is supplied in `dependency.calr.inc`; edit
the separate method fragment.
