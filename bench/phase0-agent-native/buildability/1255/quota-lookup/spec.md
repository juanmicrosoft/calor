# Quota lookup

Implement `QuotaService.Preview(int requested)` using the supplied quota
library. The current quota schedule assigns five units per requested slot,
with requests clamped to the inclusive range zero through ten. Preserve
the library and public API; do not duplicate the schedule in the new method.

## Dependency quick start

The service's configured quota lookup is `this.lookup(requested)`.
Use that callback for the standard schedule. The dependency is already
implemented in `dependency.calr.inc`; the edit belongs in the supplied
`Preview` method.
