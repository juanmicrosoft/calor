import Link from 'next/link';

export function BenchmarkChart() {
  return (
    <section className="py-16" aria-labelledby="evidence-heading">
      <div className="mx-auto max-w-3xl px-6 lg:px-8">
        <h2 id="evidence-heading" className="text-3xl font-bold tracking-tight">
          An early project, with honest limits
        </h2>
        <p className="mt-4 text-muted-foreground font-body">
          Calor is a pre-1.0 research project. Its checks are still developing, and they cover only documented cases.
        </p>
        <p className="mt-3 text-muted-foreground font-body">
          Published comparisons do not show that Calor makes AI agents more productive, makes software safer, or finds every bug. Start small and keep your normal reviews and tests.
        </p>
        <div className="mt-6 flex flex-wrap gap-6">
          <Link className="text-primary underline underline-offset-4" href="/docs/guides/adoption-playbook/">
            How to try Calor gradually
          </Link>
          <Link className="text-primary underline underline-offset-4" href="/docs/benchmarking/">
            Read about the research and its limits
          </Link>
        </div>
      </div>
    </section>
  );
}
