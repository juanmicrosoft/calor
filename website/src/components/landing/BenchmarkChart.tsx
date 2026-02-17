'use client';

import Link from 'next/link';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import { Clock } from 'lucide-react';
import { trackBenchmarkDetailClick } from '@/lib/analytics';

// Build-time import of benchmark data
import benchmarkData from '../../../public/data/benchmark-results.json';

interface BenchmarkResult {
  category: string;
  ratio: number;
  winner: 'calor' | 'csharp' | 'tie';
  interpretation: string;
  isCalorOnly?: boolean;
}

interface BenchmarkData {
  timestamp: string;
  summary: {
    programCount: number;
  };
  metrics: Record<string, { ratio: number; winner: 'calor' | 'csharp' | 'tie'; isCalorOnly?: boolean }>;
}

// Human-readable metric names and interpretations
const metricDisplayInfo: Record<
  string,
  { name: string; calorInterpretation: string; csharpInterpretation: string; tieInterpretation: string }
> = {
  TokenEconomics: {
    name: 'Code Size',
    calorInterpretation: 'Calor code is more compact',
    csharpInterpretation: 'Calor\'s explicit rules add some overhead',
    tieInterpretation: 'Both languages produce similar code size',
  },
  GenerationAccuracy: {
    name: 'First-Try Success',
    calorInterpretation: 'AI generates correct code more often',
    csharpInterpretation: 'AI knows C# better (for now)',
    tieInterpretation: 'AI generates correct code at similar rates',
  },
  Comprehension: {
    name: 'Understanding Code',
    calorInterpretation: 'AI understands Calor code 1.5x better',
    csharpInterpretation: 'C# familiarity helps AI follow along',
    tieInterpretation: 'AI understands both languages equally well',
  },
  EditPrecision: {
    name: 'Accurate Edits',
    calorInterpretation: 'AI makes precise changes without breaking things',
    csharpInterpretation: 'C# editing patterns are well-established',
    tieInterpretation: 'AI makes equally precise edits in both languages',
  },
  ErrorDetection: {
    name: 'Finding Bugs',
    calorInterpretation: 'AI spots 22% more bugs in Calor code',
    csharpInterpretation: 'C# has mature debugging tools',
    tieInterpretation: 'AI spots bugs equally well in both languages',
  },
  InformationDensity: {
    name: 'Meaning Per Line',
    calorInterpretation: 'Each line carries more information',
    csharpInterpretation: 'Calor trades brevity for clarity',
    tieInterpretation: 'Both languages convey similar meaning per line',
  },
  TaskCompletion: {
    name: 'Finishing Tasks',
    calorInterpretation: 'AI completes more tasks successfully',
    csharpInterpretation: 'More libraries and examples help AI',
    tieInterpretation: 'AI completes tasks at similar rates',
  },
  RefactoringStability: {
    name: 'Safe Refactoring',
    calorInterpretation: 'Code stays correct after restructuring',
    csharpInterpretation: 'C# has better refactoring tools',
    tieInterpretation: 'Both languages maintain stability during refactoring',
  },
  Safety: {
    name: 'Bug Catching',
    calorInterpretation: 'Contracts catch more bugs with better error messages',
    csharpInterpretation: 'Guard clauses require manual implementation',
    tieInterpretation: 'Both approaches catch bugs equally well',
  },
  EffectDiscipline: {
    name: 'Side Effect Control',
    calorInterpretation: 'Effect system prevents hidden side effect bugs',
    csharpInterpretation: 'Relies on conventions, no enforcement',
    tieInterpretation: 'Both approaches manage side effects equally',
  },
  Correctness: {
    name: 'Edge Case Handling',
    calorInterpretation: 'Contracts help prevent edge case bugs',
    csharpInterpretation: 'Guard clauses require explicit implementation',
    tieInterpretation: 'Both languages handle edge cases equally well',
  },
};

function transformMetrics(data: BenchmarkData): BenchmarkResult[] {
  return Object.entries(data.metrics)
    .map(([key, metric]) => {
      const info = metricDisplayInfo[key] || { name: key, calorInterpretation: '', csharpInterpretation: '', tieInterpretation: '' };
      let interpretation: string;
      if (metric.winner === 'calor') {
        interpretation = info.calorInterpretation;
      } else if (metric.winner === 'tie') {
        interpretation = info.tieInterpretation;
      } else {
        interpretation = info.csharpInterpretation;
      }
      return {
        category: info.name,
        ratio: metric.ratio,
        winner: metric.winner,
        interpretation,
        isCalorOnly: metric.isCalorOnly,
      };
    })
    .sort((a, b) => {
      // Sort Calor-only metrics to the end, then by ratio descending
      if (a.isCalorOnly && !b.isCalorOnly) return 1;
      if (!a.isCalorOnly && b.isCalorOnly) return -1;
      return b.ratio - a.ratio;
    });
}

// Pre-computed at build time
const results = transformMetrics(benchmarkData as BenchmarkData);
const programCount = benchmarkData.summary.programCount;
const lastUpdated = benchmarkData.timestamp;

function formatRatio(ratio: number, isCalorOnly?: boolean): string {
  if (isCalorOnly) return 'Calor only';
  return `${ratio.toFixed(2)}x`;
}

function getBarWidth(ratio: number): number {
  // Normalize to 0-100 scale, where 1.0 = 50%
  if (ratio >= 1) {
    return Math.min(50 + (ratio - 1) * 50, 100);
  }
  return Math.max(ratio * 50, 10);
}

function formatDate(isoString: string): string {
  try {
    const date = new Date(isoString);
    return date.toLocaleDateString('en-US', {
      month: 'short',
      day: 'numeric',
      year: 'numeric',
    });
  } catch {
    return '';
  }
}

export function BenchmarkChart() {
  return (
    <section className="py-24">
      <div className="mx-auto max-w-7xl px-6 lg:px-8">
        <div className="mx-auto max-w-2xl text-center">
          <h2 className="text-3xl font-bold tracking-tight sm:text-4xl">
            Measured Against Real AI Tasks
          </h2>
          <p className="mt-4 text-lg text-muted-foreground">
            We tested how well AI agents work with Calor vs C#. Here's what we found.
          </p>
          <div className="mt-2 flex items-center justify-center gap-2 text-sm text-muted-foreground">
            <Clock className="h-4 w-4" />
            <span>Last updated: {formatDate(lastUpdated)}</span>
          </div>
        </div>

        <div className="mt-16 mx-auto max-w-4xl">
          <div className="space-y-6">
            {results.map((result) => (
              <div key={result.category} className="space-y-2">
                <div className="flex items-center justify-between">
                  <div className="flex items-center gap-3">
                    <span className="font-medium">{result.category}</span>
                    <span
                      className={cn(
                        'text-xs px-2 py-0.5 rounded-full',
                        result.isCalorOnly
                          ? 'bg-calor-pink/20 text-calor-pink'
                          : result.winner === 'calor'
                            ? 'bg-calor-pink/20 text-calor-pink'
                            : result.winner === 'tie'
                              ? 'bg-calor-salmon/20 text-calor-salmon'
                              : 'bg-calor-cerulean/20 text-calor-cerulean'
                      )}
                    >
                      {result.isCalorOnly ? 'Calor only' : result.winner === 'calor' ? 'Calor wins' : result.winner === 'tie' ? 'Tie' : 'C# wins'}
                    </span>
                  </div>
                  <span className="font-mono font-bold">
                    {formatRatio(result.ratio, result.isCalorOnly)}
                  </span>
                </div>

                <div className="relative h-8 bg-muted rounded-full overflow-hidden">
                  <div
                    className={cn(
                      'absolute inset-y-0 left-0 rounded-full transition-all duration-500',
                      result.isCalorOnly
                        ? 'bg-gradient-to-r from-calor-pink to-calor-pink/60'
                        : result.winner === 'calor'
                          ? 'bg-gradient-to-r from-calor-pink to-calor-pink/80'
                          : result.winner === 'tie'
                            ? 'bg-gradient-to-r from-calor-salmon to-calor-salmon/80'
                            : 'bg-gradient-to-r from-calor-cerulean to-calor-cerulean/80'
                    )}
                    style={{ width: result.isCalorOnly ? '100%' : `${getBarWidth(result.ratio)}%` }}
                  />
                  {/* Center line at 1.0 - hide for Calor-only metrics */}
                  {!result.isCalorOnly && (
                    <div className="absolute inset-y-0 left-1/2 w-px bg-border" />
                  )}
                </div>

                <p className="text-sm text-muted-foreground">
                  {result.interpretation}
                </p>
              </div>
            ))}
          </div>

          {/* Legend */}
          <div className="mt-8 flex items-center justify-center gap-8 text-sm text-muted-foreground">
            <div className="flex items-center gap-2">
              <div className="w-3 h-3 rounded-full bg-calor-pink" />
              <span>Calor better</span>
            </div>
            <div className="flex items-center gap-2">
              <div className="w-3 h-3 rounded-full bg-calor-salmon" />
              <span>Tie</span>
            </div>
            <div className="flex items-center gap-2">
              <div className="w-3 h-3 rounded-full bg-calor-cerulean" />
              <span>C# better</span>
            </div>
            <span className="text-xs">|</span>
            <span className="text-xs">Center line = 1.0x (equal)</span>
          </div>

          {/* Key finding */}
          <div className="mt-12 p-6 rounded-lg border bg-muted/50">
            <h3 className="font-semibold mb-2">The Bottom Line</h3>
            <p className="text-muted-foreground">
              <strong>Calor wins where bugs hurt most:</strong> AI understands code better, catches more errors, and makes safer changes.
              <strong> C# wins on familiarity:</strong> AI has seen more C# code, so it generates it faster. But as AI learns Calor,
              the familiarity gap shrinks—the safety advantage doesn't.
            </p>
          </div>

          <div className="mt-8 text-center">
            <Button variant="outline" asChild>
              <Link href="/docs/benchmarking/results/" onClick={() => trackBenchmarkDetailClick()}>
                View detailed benchmarks
              </Link>
            </Button>
          </div>
        </div>
      </div>
    </section>
  );
}
