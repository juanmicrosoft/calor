import Link from 'next/link';

export function FirstProgram() {
  return (
    <section aria-labelledby="first-program-title" className="mx-auto w-full max-w-5xl px-6 py-8 lg:px-8">
      <h2 id="first-program-title" className="text-2xl font-bold">From Calor to a .NET program</h2>
      <div className="mt-4 grid gap-4 sm:grid-cols-2">
        <div className="min-w-0 rounded-lg bg-calor-navy p-4 text-white">
          <p className="mb-2 text-sm text-calor-cyan">Program.calr — Calor</p>
          <pre className="overflow-x-auto text-xs leading-6"><code>{`§M{m001:HelloApp}
  §F{f001:Main:pub} () -> void
    §E{cw}
    §P "Hello from Calor!"`}</code></pre>
        </div>
        <div className="min-w-0 rounded-lg border p-4">
          <p className="mb-2 text-sm font-medium">Run with the .NET 10 SDK and Calor installed</p>
          <pre className="overflow-x-auto text-xs leading-6"><code>calor run Program.calr</code></pre>
          <p className="mt-3 text-sm">Program output:</p>
          <pre className="overflow-x-auto text-sm"><code>Hello from Calor!</code></pre>
        </div>
      </div>
      <p className="mt-4 text-sm text-muted-foreground">
        Calor emits C# and uses the .NET SDK to build and run it.{' '}
        <Link className="text-primary underline" href="/docs/getting-started/hello-world/">
          Follow the complete setup and first-run steps
        </Link>. No AI subscription required.
      </p>
    </section>
  );
}
