import Link from 'next/link';

export function CodeComparison() {
  return (
    <section className="py-16" aria-labelledby="contract-comparison-heading">
      <div className="mx-auto max-w-5xl px-6 lg:px-8">
        <h2 id="contract-comparison-heading" className="text-3xl font-bold tracking-tight">
          What changes for your team?
        </h2>
        <p className="mt-4 text-muted-foreground font-body">
          The aim is to make important information easier to review—not to ask you to read unfamiliar symbols all day.
        </p>
        <div className="mt-6 grid gap-4 sm:grid-cols-3">
          <div className="rounded-lg border p-5">
            <h3 className="font-semibold">Review the intent</h3>
            <p className="mt-2 text-sm text-muted-foreground">See the important rules alongside the change, instead of guessing them from the code.</p>
          </div>
          <div className="rounded-lg border p-5">
            <h3 className="font-semibold">Spot some mistakes sooner</h3>
            <p className="mt-2 text-sm text-muted-foreground">Calor can flag some behavior that does not match a written rule before the program runs.</p>
          </div>
          <div className="rounded-lg border p-5">
            <h3 className="font-semibold">Keep control of the result</h3>
            <p className="mt-2 text-sm text-muted-foreground">Review the generated C#, run your usual tests, and keep using standard .NET tools.</p>
          </div>
        </div>
        <p className="mt-6 text-sm text-muted-foreground">
          Calor does not guarantee that software is bug-free. See what its checks can and cannot establish in the{' '}
          <Link href="/docs/guides/verification-guarantees/" className="text-primary underline">
            guide to checks and their limits
          </Link>.
        </p>
      </div>
    </section>
  );
}
