import Link from 'next/link';

export function CatchBugs() {
  const calorCode = `§F{f001:ProcessOrder:pub} (Order:order) -> void
  §E{db:rw}
  §C{SaveOrder} §A order §/C
  §C{NotifyCustomer} §A order §/C`;

  const errorOutput = `Calor0410: ProcessOrder uses a network effect
that its declaration does not permit.

Review the call and either remove the effect
or declare it if the operation is intended.`;

  return (
    <section className="py-16" aria-labelledby="effect-example-heading">
      <div className="mx-auto max-w-5xl px-6 lg:px-8">
        <h2 id="effect-example-heading" className="text-3xl font-bold tracking-tight">
          Find an undeclared network effect
        </h2>
        <p className="mt-4 text-muted-foreground font-body">
          Illustrative fragment: assume SaveOrder declares database access and
          NotifyCustomer declares network access. Their definitions, the Order type,
          and a caller are omitted. This is not a runnable example.
        </p>
        <div className="mt-6 grid gap-6 lg:grid-cols-2">
          <div className="min-w-0 rounded-lg border bg-calor-navy">
            <div className="border-b border-white/10 px-4 py-2 text-sm text-calor-cyan font-mono">
              order-service.calr — fragment
            </div>
            <pre className="p-4 sm:p-5 text-[13px] leading-6 whitespace-pre-wrap break-words overflow-x-auto">
              <code className="font-mono text-calor-cyan">{calorCode}</code>
            </pre>
          </div>
          <div className="min-w-0 rounded-lg border bg-calor-navy">
            <div className="border-b border-white/10 px-4 py-2 text-sm text-calor-salmon font-mono">
              Illustrative diagnostic (abridged, not captured output)
            </div>
            <pre className="p-4 sm:p-5 text-[13px] leading-6 whitespace-pre-wrap break-words overflow-x-auto">
              <code className="font-mono text-calor-salmon">{errorOutput}</code>
            </pre>
          </div>
        </div>
        <p className="mt-6 text-muted-foreground font-body">
          Effect checking follows resolved calls across the supplied Calor inputs.
          Unknown external calls and raw C# need separate diagnostics, manifests,
          or review. A missing declaration is not permission to broaden an
          interface automatically. See the{' '}
          <Link href="/docs/syntax-reference/effects/" className="text-primary underline">effect reference</Link>.
        </p>
        <p className="mt-4 text-sm text-muted-foreground">
          Type and effect checks are on by default. Static bug-pattern analysis
          requires --analyze; contract verification requires --verify. Supported
          checks, runtime modes and opt-outs are documented in{' '}
          <Link href="/docs/cli/compile/" className="text-primary underline">compile options</Link>.
          None guarantees that every defect is found.
        </p>
      </div>
    </section>
  );
}
