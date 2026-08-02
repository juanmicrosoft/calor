# SdkConsumer — the M-A1 package-consumption fixture

This is the template consumer project behind the **M-A1 SDK consumability**
metric (wedge plan v0.11, D-W1.2): a fresh project that restores, builds, and
tests green against the **published `Calor.Sdk` package artifact** — never a
source-tree `ProjectReference`.

It is exercised by `.github/scripts/test-sdk-package.sh`, which:

1. packs `Calor.Sdk` into a temporary local folder feed,
2. copies this template to a temp directory, substituting
   `CALOR_SDK_VERSION_PLACEHOLDER` and `CALOR_LOCAL_FEED_PLACEHOLDER`,
3. restores with a **fresh** `NUGET_PACKAGES` directory (no cache reuse),
4. builds with `CalorVerify=true` and asserts:
   - no `Calor0710` (Z3-unavailable-in-task-context) warning, and
   - a `Calor0712` refutation warning from the deliberate canary contract in
     `CalorLib/quotes.calr` — positive proof that the packaged Z3 actually ran,
5. runs the xunit test that calls into the `.calr`-compiled code.

The placeholders make this template unbuildable in place — that is intentional.
Only the script's substituted copy builds, so the fixture can never silently
degrade into a source-tree consumer.
