import Link from 'next/link';

const helloWorldSource = `§M{m001:HelloApp}
  §F{f001:Main:pub} () -> void
    §E{cw}
    §P "Hello from Calor!"`;

export function QuickStart() {
  return (
    <section className="py-16" aria-labelledby="quick-start-title">
      <div className="mx-auto max-w-7xl px-6 lg:px-8">
        <div className="mx-auto max-w-3xl">
          <h2 id="quick-start-title" className="text-3xl font-bold tracking-tight sm:text-4xl">
            Choose how you want to explore Calor
          </h2>
          <p className="mt-3 text-muted-foreground font-body">
            You can learn what Calor does before installing anything. To run it
            on your computer, you need Microsoft's .NET 10 developer tools (the
            SDK).
          </p>
          <div className="mt-6 rounded-lg border p-5" role="region" aria-labelledby="try-calor-title">
            <h3 id="try-calor-title" className="text-xl font-semibold">Get started: run your first Calor program</h3>
            <p className="mt-3 text-sm text-muted-foreground">
              No AI account is needed. After installing the .NET 10 SDK, open a terminal and install Calor:
            </p>
            <pre className="mt-3 overflow-x-auto rounded-lg bg-calor-navy p-4 text-sm text-white"><code>dotnet tool install -g calor</code></pre>
            <p className="mt-4 text-sm text-muted-foreground">
              In a new, empty folder, save the following as <code>Program.calr</code>. This small program prints a greeting.
            </p>
            <pre className="mt-3 overflow-x-auto rounded-lg bg-calor-navy p-4 text-sm text-white"><code>{helloWorldSource}</code></pre>
            <p className="mt-4 text-sm text-muted-foreground">Run it from that folder:</p>
            <pre className="mt-3 overflow-x-auto rounded-lg bg-calor-navy p-4 text-sm text-white"><code>calor run Program.calr</code></pre>
            <p className="mt-4 text-sm text-muted-foreground">You should see:</p>
            <pre className="mt-3 overflow-x-auto rounded-lg bg-calor-navy p-4 text-sm text-white"><code>Hello from Calor!</code></pre>
            <Link className="mt-4 inline-block text-primary underline" href="/docs/getting-started/hello-world/">
              Follow the complete setup and example
            </Link>
          </div>
        </div>

        <div className="mt-6 mx-auto grid max-w-3xl gap-4 sm:grid-cols-2">
          <div className="rounded-lg border p-5">
            <h3 className="font-semibold">Just getting to know Calor?</h3>
            <p className="mt-2 text-sm text-muted-foreground">
              See what happens when you run a small example. No AI account or
              existing project is needed.
            </p>
            <Link className="mt-4 inline-block text-primary underline" href="/docs/getting-started/hello-world/">
              Follow the guided example
            </Link>
          </div>
          <div className="rounded-lg border p-5">
            <h3 className="font-semibold">Already have a C# project?</h3>
            <p className="mt-2 text-sm text-muted-foreground">
              Keep your project running while you convert to Calor in phases.
              Start with one file; there is no need to convert everything at once.
            </p>
            <Link className="mt-4 inline-block text-primary underline" href="/docs/guides/adding-calor-to-existing-projects/">
              Follow the phased conversion guide
            </Link>
          </div>
        </div>
      </div>
    </section>
  );
}
