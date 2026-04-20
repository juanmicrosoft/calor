'use client';

import { useScrollReveal } from '@/hooks/useScrollReveal';

export function CatchBugs() {
  const sectionRef = useScrollReveal<HTMLDivElement>();
  const codeRef = useScrollReveal<HTMLDivElement>({ direction: 'left' });
  const errorRef = useScrollReveal<HTMLDivElement>({ direction: 'right' });

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
    <section className="relative py-24 overflow-hidden">
      {/* Gradient mesh */}
      <div className="gradient-mesh gradient-mesh-salmon absolute top-20 left-0 w-[400px] h-[400px] -z-10" />

      <div className="mx-auto max-w-7xl px-6 lg:px-8">
        <div className="mx-auto max-w-2xl text-center" ref={sectionRef}>
          <h2 className="text-3xl font-bold tracking-tight sm:text-4xl">
            Your AI Forgot a Network Call. The Compiler Didn&apos;t.
          </h2>
          <p className="mt-4 text-lg text-muted-foreground font-body">
            See exactly what your code does—even when side effects hide in helper functions.
          </p>
        </div>

        <div className="mt-16 mx-auto max-w-5xl">
          <div className="grid lg:grid-cols-2 gap-6">
            {/* Code block */}
            <div ref={codeRef} className="rounded-lg border border-calor-navy/20 bg-calor-navy overflow-hidden shadow-lg">
              <div className="border-b border-white/10 px-4 py-2">
                <span className="text-sm text-calor-cyan/70 font-mono">order-service.calr</span>
              </div>
              <pre className="p-5 text-sm leading-7 overflow-x-auto">
                <code className="text-calor-cyan font-mono">{calorCode}</code>
              </pre>
            </div>

            {/* Error output — dramatic treatment */}
            <div ref={errorRef} className="rounded-lg overflow-hidden animate-pulse-glow"
              style={{ animationDelay: '1s' }}
            >
              <div className="rounded-lg border-2 border-calor-pink/40 bg-calor-navy overflow-hidden">
                <div className="border-b border-calor-pink/30 px-4 py-2 bg-calor-pink/10">
                  <span className="text-sm text-calor-pink font-mono font-bold">
                    Compiler Output
                  </span>
                </div>
                <pre className="p-5 text-sm leading-6 overflow-x-auto">
                  <code className="text-calor-salmon font-mono">{errorOutput}</code>
                </pre>
              </div>
            </div>
          </div>

          {/* Explanation — overlapping card */}
          <div className="mt-6 lg:-mt-4 relative z-10 mx-auto max-w-3xl">
            <div className="p-6 rounded-lg border bg-background shadow-lg">
              <p className="text-muted-foreground font-body">
                <strong className="text-foreground">What happened:</strong> Your AI wrote code that calls <code className="text-sm bg-calor-navy/5 text-calor-cerulean px-1.5 py-0.5 rounded font-mono">NotifyCustomer</code>, which
                calls <code className="text-sm bg-calor-navy/5 text-calor-cerulean px-1.5 py-0.5 rounded font-mono">SendEmail</code>, which makes a network request. The compiler caught that
                you didn&apos;t declare the network access—before you ran anything. In most languages, this bug ships to production.
              </p>
            </div>
          </div>

          {/* Static Analysis section */}
          <div className="mt-20 mx-auto max-w-2xl text-center">
            <h3 className="text-2xl font-bold tracking-tight sm:text-3xl">
              Static Analysis Finds What C# Misses
            </h3>
            <p className="mt-4 text-lg text-muted-foreground font-body">
              Run <code className="text-sm bg-calor-navy/5 text-calor-cerulean px-1.5 py-0.5 rounded font-mono">calor --analyze</code> to
              detect null dereferences, injection vulnerabilities, and arithmetic bugs across your entire codebase.
            </p>
          </div>

          <div className="mt-10 mx-auto max-w-3xl">
            <div className="rounded-lg border border-calor-navy/20 bg-calor-navy overflow-hidden shadow-lg">
              <div className="border-b border-white/10 px-4 py-2">
                <span className="text-sm text-calor-cyan/70 font-mono">$ calor --analyze --input NullPropagationTransform.calr</span>
              </div>
              <pre className="p-5 text-sm leading-7 overflow-x-auto">
                <code className="font-mono">
                  <span className="text-calor-salmon">{'warning Calor0922: Potential unsafe unwrap without prior\n'}</span>
                  <span className="text-calor-salmon">{'                   Option/Result check\n'}</span>
                  <span className="text-white/50">{'\n'}</span>
                  <span className="text-calor-cyan/70">{'  NullPropagationTransform.calr(75,9)\n'}</span>
                  <span className="text-calor-cyan/70">{'  NullPropagationTransform.calr(81,9)\n'}</span>
                  <span className="text-calor-cyan/70">{'  NullPropagationTransform.calr(86,9)\n'}</span>
                  <span className="text-white/50">{'\n'}</span>
                  <span className="text-calor-salmon">{'warning Calor0983: Potential path traversal: tainted data\n'}</span>
                  <span className="text-calor-salmon">{'                   from FileRead flows to file path\n'}</span>
                  <span className="text-white/50">{'\n'}</span>
                  <span className="text-calor-cyan/70">{'  FtpClient.calr(1201,11)\n'}</span>
                  <span className="text-white/50">{'\n'}</span>
                  <span className="text-green-400">{'Verified findings only. Use --all-findings for more.\n'}</span>
                </code>
              </pre>
            </div>

            <div className="mt-6 grid grid-cols-2 sm:grid-cols-4 gap-4">
              <div className="text-center p-4 rounded-lg border bg-background">
                <div className="text-2xl font-bold text-calor-cerulean">51</div>
                <div className="text-xs text-muted-foreground mt-1">Projects scanned</div>
              </div>
              <div className="text-center p-4 rounded-lg border bg-background">
                <div className="text-2xl font-bold text-calor-cerulean">262K</div>
                <div className="text-xs text-muted-foreground mt-1">Files analyzed</div>
              </div>
              <div className="text-center p-4 rounded-lg border bg-background">
                <div className="text-2xl font-bold text-calor-salmon">23</div>
                <div className="text-xs text-muted-foreground mt-1">Verified findings</div>
              </div>
              <div className="text-center p-4 rounded-lg border bg-background">
                <div className="text-2xl font-bold text-green-500">~90%</div>
                <div className="text-xs text-muted-foreground mt-1">True positive rate</div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
