# SdkConsumer — the M-A1 package-consumption fixture

This is the template consumer project behind the **M-A1 SDK consumability**
metric (wedge plan v0.11, D-W1.2): a fresh project that restores, builds, and
tests green against the **published `Calor.Sdk` package artifact** — never a
source-tree `ProjectReference`.

It is exercised by `.github/scripts/test-sdk-package.sh`, which:

1. packs `Calor.Sdk` into a temporary local folder feed,
2. copies this template to a temp directory and substitutes the explicit
   `Sdk="Calor.Sdk/<version>"` and local-feed placeholders,
3. restores with a **fresh** `NUGET_PACKAGES` directory and lock files,
4. repeats restore with the package feed unavailable to prove offline use,
5. performs clean Release and Debug builds, an incremental rebuild, and a
   design-time build,
6. builds with `CalorVerify=true` and asserts:
   - no `Calor0710` (Z3-unavailable-in-task-context) warning, and
   - a `Calor0712` refutation warning from the deliberate canary contract in
     `CalorLib/quotes.calr` — positive proof that the packaged Z3 actually ran,
7. runs a dependency-free console assertion harness that calls into the
   `.calr`-compiled code.

The placeholders make this template unbuildable in place — that is intentional.
Only the script's substituted copy builds, so the fixture can never silently
degrade into a source-tree consumer.
