'use client';

import { MetricCard } from './MetricCard';
import { ProgramTable } from './ProgramTable';
import { cn } from '@/lib/utils';
import { BarChart3, FileCode, Trophy, Clock } from 'lucide-react';

// Build-time import of benchmark data
import benchmarkData from '../../../public/data/benchmark-results.json';

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
  metrics: Record<string, { ratio: number; winner: 'calor' | 'csharp' }>;
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
const data = benchmarkData as BenchmarkData;

function formatDate(isoString: string): string {
  try {
    const date = new Date(isoString);
    return date.toLocaleDateString('en-US', {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  } catch {
    return 'Unknown';
  }
}

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
  // Sort metrics: Calor wins first, then by ratio descending
  const sortedMetrics = Object.entries(data.metrics).sort(([, a], [, b]) => {
    if (a.winner === 'calor' && b.winner !== 'calor') return -1;
    if (a.winner !== 'calor' && b.winner === 'calor') return 1;
    return b.ratio - a.ratio;
  });

  const metricNames = Object.keys(data.metrics);

  return (
    <div className="space-y-8">
      {/* Header with timestamp */}
      <div className="flex items-center justify-between flex-wrap gap-4">
        <div>
          <h2 className="text-2xl font-bold">Live Benchmark Results</h2>
          <p className="text-muted-foreground">
            Evaluated across {data.summary.programCount} programs with {data.summary.metricCount} metrics
          </p>
        </div>
        <div className="flex items-center gap-2 text-sm text-muted-foreground">
          <Clock className="h-4 w-4" />
          <span>Updated: {formatDate(data.timestamp)}</span>
          {data.commit && (
            <span className="font-mono text-xs bg-muted px-2 py-0.5 rounded">
              {data.commit.slice(0, 7)}
            </span>
          )}
        </div>
      </div>

      {/* Summary cards */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
        <SummaryCard
          icon={<BarChart3 className="h-4 w-4" />}
          label="Overall Advantage"
          value={`${(data.summary.overallAdvantage * 100).toFixed(0)}%`}
          subtext="Calor vs C# (100% = equal)"
          highlight={data.summary.overallAdvantage >= 1 ? 'calor' : 'csharp'}
        />
        <SummaryCard
          icon={<FileCode className="h-4 w-4" />}
          label="Programs Tested"
          value={data.summary.programCount}
          subtext={`${data.programs.filter((p) => p.calorSuccess).length} Calor successes`}
        />
        <SummaryCard
          icon={<Trophy className="h-4 w-4" />}
          label="Calor Wins"
          value={data.summary.calorWins}
          subtext={`of ${data.summary.metricCount} metrics`}
          highlight="calor"
        />
        <SummaryCard
          icon={<Trophy className="h-4 w-4" />}
          label="C# Wins"
          value={data.summary.cSharpWins}
          subtext={`of ${data.summary.metricCount} metrics`}
          highlight="csharp"
        />
      </div>

      {/* Metric breakdown */}
      <div>
        <h3 className="text-xl font-semibold mb-4">Metric Breakdown</h3>
        <div className="space-y-6">
          {sortedMetrics.map(([name, metric]) => (
            <MetricCard
              key={name}
              name={name}
              ratio={metric.ratio}
              winner={metric.winner}
            />
          ))}
        </div>

        {/* Legend */}
        <div className="mt-6 flex items-center justify-center gap-8 text-sm text-muted-foreground">
          <div className="flex items-center gap-2">
            <div className="w-3 h-3 rounded-full bg-calor-pink" />
            <span>Calor better</span>
          </div>
          <div className="flex items-center gap-2">
            <div className="w-3 h-3 rounded-full bg-calor-cerulean" />
            <span>C# better</span>
          </div>
          <span className="text-xs">|</span>
          <span className="text-xs">Center line = 1.0x (equal)</span>
        </div>
      </div>

      {/* Key finding */}
      <div className="p-6 rounded-lg border bg-muted/50">
        <h3 className="font-semibold mb-2">Current Status</h3>
        <p className="text-muted-foreground">
          {data.summary.calorWins > data.summary.cSharpWins ? (
            <>
              Calor leads in {data.summary.calorWins} of {data.summary.metricCount} metrics,
              demonstrating advantages in areas where explicitness matters.
            </>
          ) : data.summary.calorWins === data.summary.cSharpWins ? (
            <>
              Results are evenly split between Calor and C#, each winning {data.summary.calorWins} metrics.
            </>
          ) : (
            <>
              C# currently leads in {data.summary.cSharpWins} of {data.summary.metricCount} metrics.
              Calor shows strength in Error Detection ({data.metrics.ErrorDetection?.ratio.toFixed(2)}x),
              validating the value of explicit contracts. The token cost of explicitness remains
              a tradeoff, but improvements in the V2 syntax continue to close the gap.
            </>
          )}
        </p>
      </div>

      {/* Per-program table */}
      {data.programs.length > 0 && (
        <div>
          <h3 className="text-xl font-semibold mb-4">Per-Program Results</h3>
          <ProgramTable programs={data.programs} metricNames={metricNames} />
        </div>
      )}
    </div>
  );
}
