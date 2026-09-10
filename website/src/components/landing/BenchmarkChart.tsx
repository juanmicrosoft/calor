'use client';

import Link from 'next/link';
import { formatTimestamp } from '@/lib/timestamps';
import { trackBenchmarkDetailClick } from '@/lib/analytics';
import benchmarkData from '../../../public/data/benchmark-results.json';
import provenance from '../../../public/data/benchmark-provenance.json';

export function BenchmarkChart() {
  return (
    <section className="py-16" aria-labelledby="evidence-heading">
      <div className="mx-auto max-w-3xl px-6 lg:px-8">
        <h2 id="evidence-heading" className="text-3xl font-bold tracking-tight">
          Evidence and its limits
        </h2>
        <p className="mt-4 text-muted-foreground font-body">
          The historical static snapshot covers {benchmarkData.programs.length} source pairs
          and {Object.keys(benchmarkData.metrics).length} deterministic calculators.
          Source revision {provenance.sourceCommit} declares compiler v{provenance.sourceDeclaredVersion};
          the executed binary was not independently recorded.
        </p>
        <p className="mt-3 text-muted-foreground font-body">
          The pairs are not all behaviorally equivalent. Repeating deterministic scores
          does not establish sampling uncertainty, and these scores do not measure
          agent productivity, safety, or a language advantage. Separate agent studies
          have their own dates, instruments, and limitations.
        </p>
        <p className="mt-3 text-sm text-muted-foreground">
          Snapshot: <time dateTime={benchmarkData.timestamp}>{formatTimestamp(benchmarkData.timestamp)}</time>.
          Corpus: <code>{provenance.corpus}</code>.
        </p>
        <div className="mt-6 flex flex-wrap gap-6">
          <Link className="text-primary underline underline-offset-4" href="/docs/benchmarking/results/"
            onClick={trackBenchmarkDetailClick}>Read results and provenance</Link>
          <Link className="text-primary underline underline-offset-4" href="/docs/benchmarking/evidence-status/">
            Research evidence status
          </Link>
        </div>
      </div>
    </section>
  );
}
