export function CatchBugs() {
  return (
    <section className="py-16" aria-labelledby="effect-example-heading">
      <div className="mx-auto max-w-5xl px-6 lg:px-8">
        <h2 id="effect-example-heading" className="text-3xl font-bold tracking-tight">
          Catch mismatches before you rely on a change
        </h2>
        <p className="mt-4 text-muted-foreground font-body">
          Calor can flag when code does not match rules your project has written down—for example, if a change tries to do something it was not expected to do.
        </p>
        <div className="mt-6 rounded-lg border bg-muted/30 p-5">
          <h3 className="font-semibold">A useful extra check, not a replacement for testing</h3>
          <p className="mt-2 text-sm text-muted-foreground">
            Some checks cover only specific kinds of code, and external services may need extra setup. Keep reviewing changes and running your tests. Calor explains which checks passed and where it could not be sure.
          </p>
        </div>
      </div>
    </section>
  );
}
