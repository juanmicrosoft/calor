'use client';

export function CatchBugs() {
  const calorCode = `§F[f_01A8X:ProcessOrder:pub]
  §I[Order:order]
  §O[bool]
  §E[db]

  §C[SaveOrder] order
  §C[NotifyCustomer] order
§/F[f_01A8X]`;

  const errorOutput = `error CALOR0410: Function 'ProcessOrder' uses effect 'net'
                   but does not declare it

  Call chain: ProcessOrder → NotifyCustomer → SendEmail
              → HttpClient.PostAsync

  Declared effects: §E[db]
  Required effects: §E[db,net]

  Fix: Add 'net' to the effect declaration:
       §E[db,net]`;

  return (
    <section className="py-24">
      <div className="mx-auto max-w-7xl px-6 lg:px-8">
        <div className="mx-auto max-w-2xl text-center">
          <h2 className="text-3xl font-bold tracking-tight sm:text-4xl">
            Your AI Forgot a Network Call. The Compiler Didn't.
          </h2>
          <p className="mt-4 text-lg text-muted-foreground">
            See exactly what your code does—even when side effects hide in helper functions.
          </p>
        </div>

        <div className="mt-16 mx-auto max-w-5xl">
          <div className="grid lg:grid-cols-2 gap-6">
            {/* Code block */}
            <div className="rounded-lg border bg-zinc-950 overflow-hidden">
              <div className="border-b border-zinc-800 px-4 py-2">
                <span className="text-sm text-zinc-400">order-service.calr</span>
              </div>
              <pre className="p-4 text-sm leading-6 overflow-x-auto">
                <code className="text-zinc-100">{calorCode}</code>
              </pre>
            </div>

            {/* Error output */}
            <div className="rounded-lg border border-destructive/50 bg-destructive/5 overflow-hidden">
              <div className="border-b border-destructive/30 px-4 py-2">
                <span className="text-sm text-destructive">Compiler Output</span>
              </div>
              <pre className="p-4 text-sm leading-6 overflow-x-auto">
                <code className="text-destructive/90 font-mono">{errorOutput}</code>
              </pre>
            </div>
          </div>

          {/* Explanation */}
          <div className="mt-8 p-6 rounded-lg border bg-muted/50">
            <p className="text-muted-foreground">
              <strong>What happened:</strong> Your AI wrote code that calls <code className="text-sm bg-muted px-1 rounded">NotifyCustomer</code>, which
              calls <code className="text-sm bg-muted px-1 rounded">SendEmail</code>, which makes a network request. The compiler caught that
              you didn't declare the network access—before you ran anything. In most languages, this bug ships to production.
            </p>
          </div>
        </div>
      </div>
    </section>
  );
}
