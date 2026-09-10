'use client';

import { AgentRefactoringCard } from './AgentRefactoringCard';
import { MetricCard } from './MetricCard';
import { ProgramTable } from './ProgramTable';
import { cn } from '@/lib/utils';
import { BarChart3, FileCode, Clock } from 'lucide-react';
import { trackBenchmarkResultsView } from '@/lib/analytics';
import { useEffect } from 'react';
import { identifyPrograms } from '@/lib/benchmark-identity';
import { formatTimestamp } from '@/lib/timestamps';
import { staticMetricOrder } from '@/lib/benchmark-labels';

// Build-time import of benchmark data
import benchmarkData from '../../../public/data/benchmark-results.json';
import provenance from '../../../public/data/benchmark-provenance.json';

interface BenchmarkData {
  version: string;
  timestamp: string;
  commit: string;
  frameworkVersion: string;
  summary: {
    overallAdvantage: number;
    programCount: number;
    metricCount: number;
    calorWins: number;
    cSharpWins: number;
    statisticalRunCount: number;
  };
  metrics: Record<string, { ratio: number; winner: 'calor' | 'csharp' | 'tie'; isCalorOnly?: boolean }>;
  programs: Array<{
    id: string;
    name: string;
    level: number;
    features: string[];
    calorSuccess: boolean;
    cSharpSuccess: boolean;
    advantage: number;
    metrics: Record<string, number>;
  }>;
}

// Pre-loaded at build time
const data = {
  ...benchmarkData as BenchmarkData,
  programs: identifyPrograms(benchmarkData.programs),
};


interface SummaryCardProps {
  icon: React.ReactNode;
  label: string;
  value: string | number;
  subtext?: string;
  highlight?: 'calor' | 'csharp' | 'neutral';
}

function SummaryCard({ icon, label, value, subtext, highlight = 'neutral' }: SummaryCardProps) {
  return (
    <div
      className={cn(
        'p-4 rounded-lg border',
        highlight === 'calor' && 'border-calor-pink/50 bg-calor-pink/5',
        highlight === 'csharp' && 'border-calor-cerulean/50 bg-calor-cerulean/5',
        highlight === 'neutral' && 'border-border bg-muted/30'
      )}
    >
      <div className="flex items-center gap-2 text-muted-foreground mb-2">
        {icon}
        <span className="text-sm">{label}</span>
      </div>
      <div className="text-2xl font-bold">{value}</div>
      {subtext && <div className="text-xs text-muted-foreground mt-1">{subtext}</div>}
    </div>
  );
}

export function BenchmarkDashboard() {
  useEffect(() => {
    trackBenchmarkResultsView();
  }, []);

  const metricNames = staticMetricOrder.filter(name => data.metrics[name]);

  return (
    <div className="space-y-8">
      {/* Header with timestamp */}
      <div className="flex items-center justify-between flex-wrap gap-4">
        <div>
          <h2 className="text-2xl font-bold">Static Benchmark Snapshot</h2>
          <p className="text-muted-foreground">
            Evaluated across {data.summary.programCount} programs with {data.summary.metricCount} metrics
          </p>
        </div>
        <div className="flex items-center gap-2 text-sm text-muted-foreground">
          <Clock className="h-4 w-4" />
          <span>Updated: <time dateTime={data.timestamp}>{formatTimestamp(data.timestamp)}</time></span>
          {data.commit && (
            <span className="font-mono text-xs bg-muted px-2 py-0.5 rounded">
              {data.commit.slice(0, 7)}
            </span>
          )}
        </div>
      </div>

      <div className="rounded-lg border p-4 text-sm text-muted-foreground" role="note" aria-label="Benchmark provenance">
        <p>Recorded source revision{' '}
          <a className="text-primary underline" href={`https://github.com/juanmicrosoft/calor/blob/${provenance.sourceCommit}/Directory.Build.props`}>
            {provenance.sourceCommit}
          </a>{' '}declares compiler v{provenance.sourceDeclaredVersion}; this is a pinned static-calculator run, not an agent-productivity measurement.
        </p>
        <p>Corpus: <code>{provenance.corpus}</code> — {data.programs.length} programs.</p>
        <p>Method: {provenance.method}, {data.summary.statisticalRunCount} repetitions, {Object.keys(data.metrics).length} metrics.</p>
        <p>{provenance.limitation}</p>
      </div>

      {/* Summary cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
        <SummaryCard
          icon={<BarChart3 className="h-4 w-4" />}
          label="Legacy Direction-Normalized Composite"
          value={`${data.summary.overallAdvantage.toFixed(2)}x`}
          subtext="Above 1 favors Calor"
        />
        <SummaryCard
          icon={<FileCode className="h-4 w-4" />}
          label="Source Pairs"
          value={data.summary.programCount}
          subtext={`${data.summary.metricCount} deterministic calculators`}
        />
      </div>

      <div className="rounded-lg border border-amber-500/30 bg-amber-500/5 p-5 text-sm text-muted-foreground">
        <strong className="text-foreground">How to read this run:</strong>{' '}
        source pairs are not all behaviorally equivalent, so these ratios cannot establish a language
        advantage. The exporter reports singleton category intervals and pooled repeated-program
        intervals; neither establishes sampling uncertainty. These deterministic calculators are not
        coding-agent trials and are separate from the v0.12 release gates.{' '}
        <a className="text-primary underline" href="https://github.com/juanmicrosoft/calor/issues/1276">
          Benchmark integrity follow-up
        </a>.
      </div>

      {/* Agent Refactoring Benchmark */}
      <AgentRefactoringCard />

      {/* Metric breakdown */}
      <div>
        <h3 className="text-xl font-semibold mb-4">Static Scores by Metric</h3>
        <p className="mb-4 text-sm text-muted-foreground">
          Alphabetical metric order. Ratios are direction-normalized: above 1
          favors Calor and below 1 favors C#. Lower-is-better metrics invert their
          raw score ratio. Neither means a language is better.
        </p>
        <div className="space-y-6">
          {metricNames.map(name => (
            <MetricCard
              key={name}
              name={name}
              ratio={data.metrics[name].ratio}
              winner={data.metrics[name].winner}
              isCalorOnly={data.metrics[name].isCalorOnly}
            />
          ))}
        </div>

      </div>

      {/* Per-program table */}
      {data.programs.length > 0 && (
        <div>
          <h3 className="text-xl font-semibold mb-4">Per-Program Static Scores</h3>
          <ProgramTable programs={data.programs} metricNames={metricNames} />
        </div>
      )}
    </div>
  );
}
