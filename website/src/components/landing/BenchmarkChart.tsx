'use client';

import { useState, useEffect } from 'react';
import Link from 'next/link';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import { Clock } from 'lucide-react';

interface BenchmarkResult {
  category: string;
  ratio: number;
  winner: 'calor' | 'csharp';
  interpretation: string;
}

interface BenchmarkData {
  timestamp: string;
  summary: {
    programCount: number;
  };
  metrics: Record<string, { ratio: number; winner: 'calor' | 'csharp' }>;
}

// Fallback hardcoded data for when fetch fails
const fallbackResults: BenchmarkResult[] = [
  {
    category: 'Error Detection',
    ratio: 1.08,
    winner: 'calor',
    interpretation: 'Contracts surface invariant violations',
  },
  {
    category: 'Generation Accuracy',
    ratio: 0.94,
    winner: 'csharp',
    interpretation: 'Mature tooling, familiar patterns',
  },
  {
    category: 'Task Completion',
    ratio: 0.75,
    winner: 'csharp',
    interpretation: 'Ecosystem maturity advantage',
  },
  {
    category: 'Edit Precision',
    ratio: 0.73,
    winner: 'csharp',
    interpretation: 'Established editing patterns',
  },
  {
    category: 'Token Economics',
    ratio: 0.63,
    winner: 'csharp',
    interpretation: "Calor's explicit syntax uses more tokens",
  },
  {
    category: 'Comprehension',
    ratio: 0.25,
    winner: 'csharp',
    interpretation: 'Familiar syntax easier to follow',
  },
];

// Human-readable metric names and interpretations
const metricDisplayInfo: Record<
  string,
  { name: string; calorInterpretation: string; csharpInterpretation: string }
> = {
  TokenEconomics: {
    name: 'Token Economics',
    calorInterpretation: 'More compact representation',
    csharpInterpretation: "Calor's explicit syntax uses more tokens",
  },
  GenerationAccuracy: {
    name: 'Generation Accuracy',
    calorInterpretation: 'Better code generation from prompts',
    csharpInterpretation: 'Mature tooling, familiar patterns',
  },
  Comprehension: {
    name: 'Comprehension',
    calorInterpretation: 'Explicit structure aids understanding',
    csharpInterpretation: 'Familiar syntax easier to follow',
  },
  EditPrecision: {
    name: 'Edit Precision',
    calorInterpretation: 'Unique IDs enable targeted changes',
    csharpInterpretation: 'Established editing patterns',
  },
  ErrorDetection: {
    name: 'Error Detection',
    calorInterpretation: 'Contracts surface invariant violations',
    csharpInterpretation: 'Mature error handling ecosystem',
  },
  InformationDensity: {
    name: 'Information Density',
    calorInterpretation: 'More semantic content per token',
    csharpInterpretation: 'Calor trades density for explicitness',
  },
  TaskCompletion: {
    name: 'Task Completion',
    calorInterpretation: 'Better task completion rate',
    csharpInterpretation: 'Ecosystem maturity advantage',
  },
  RefactoringStability: {
    name: 'Refactoring Stability',
    calorInterpretation: 'More stable during refactoring',
    csharpInterpretation: 'Better refactoring support',
  },
};

function transformMetrics(data: BenchmarkData): BenchmarkResult[] {
  return Object.entries(data.metrics)
    .map(([key, metric]) => {
      const info = metricDisplayInfo[key] || { name: key, calorInterpretation: '', csharpInterpretation: '' };
      return {
        category: info.name,
        ratio: metric.ratio,
        winner: metric.winner,
        interpretation:
          metric.winner === 'calor' ? info.calorInterpretation : info.csharpInterpretation,
      };
    })
    .sort((a, b) => b.ratio - a.ratio); // Sort by ratio descending
}

function formatRatio(ratio: number): string {
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
  const [results, setResults] = useState<BenchmarkResult[]>(fallbackResults);
  const [programCount, setProgramCount] = useState<number>(28);
  const [lastUpdated, setLastUpdated] = useState<string | null>(null);
  const [isLive, setIsLive] = useState(false);

  useEffect(() => {
    async function fetchData() {
      try {
        const response = await fetch('/data/benchmark-results.json');
        if (!response.ok) {
          throw new Error(`HTTP ${response.status}`);
        }
        const data: BenchmarkData = await response.json();
        setResults(transformMetrics(data));
        setProgramCount(data.summary.programCount);
        setLastUpdated(data.timestamp);
        setIsLive(true);
      } catch (err) {
        console.warn('Failed to fetch benchmark data, using fallback:', err);
        // Keep using fallback data
      }
    }

    fetchData();
  }, []);

  return (
    <section className="py-24">
      <div className="mx-auto max-w-7xl px-6 lg:px-8">
        <div className="mx-auto max-w-2xl text-center">
          <h2 className="text-3xl font-bold tracking-tight sm:text-4xl">
            Benchmark Results
          </h2>
          <p className="mt-4 text-lg text-muted-foreground">
            Evaluated across {programCount} paired Calor/C# programs
          </p>
          {lastUpdated && (
            <div className="mt-2 flex items-center justify-center gap-2 text-sm text-muted-foreground">
              <Clock className="h-4 w-4" />
              <span>Last updated: {formatDate(lastUpdated)}</span>
              {isLive && (
                <span className="inline-flex items-center px-2 py-0.5 rounded text-xs bg-green-500/10 text-green-600">
                  Live
                </span>
              )}
            </div>
          )}
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
                        result.winner === 'calor'
                          ? 'bg-calor-cyan/20 text-calor-cerulean'
                          : 'bg-calor-salmon/20 text-calor-salmon'
                      )}
                    >
                      {result.winner === 'calor' ? 'Calor' : 'C#'} wins
                    </span>
                  </div>
                  <span className="font-mono font-bold">
                    {formatRatio(result.ratio)}
                  </span>
                </div>

                <div className="relative h-8 bg-muted rounded-full overflow-hidden">
                  <div
                    className={cn(
                      'absolute inset-y-0 left-0 rounded-full transition-all duration-500',
                      result.winner === 'calor'
                        ? 'bg-gradient-to-r from-calor-cyan to-calor-cyan/80'
                        : 'bg-gradient-to-r from-calor-salmon to-calor-salmon/80'
                    )}
                    style={{ width: `${getBarWidth(result.ratio)}%` }}
                  />
                  {/* Center line at 1.0 */}
                  <div className="absolute inset-y-0 left-1/2 w-px bg-border" />
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
              <div className="w-3 h-3 rounded-full bg-calor-cyan" />
              <span>Calor better</span>
            </div>
            <div className="flex items-center gap-2">
              <div className="w-3 h-3 rounded-full bg-calor-salmon" />
              <span>C# better</span>
            </div>
            <span className="text-xs">|</span>
            <span className="text-xs">Center line = 1.0x (equal)</span>
          </div>

          {/* Key finding */}
          <div className="mt-12 p-6 rounded-lg border bg-muted/50">
            <h3 className="font-semibold mb-2">Key Finding</h3>
            <p className="text-muted-foreground">
              Calor excels in error detection through explicit contracts. C# currently leads
              in most other metrics, reflecting ecosystem maturity and LLM training data bias.
              The tradeoff: explicit semantics require more tokens but enable better invariant checking.
            </p>
          </div>

          <div className="mt-8 text-center">
            <Button variant="outline" asChild>
              <Link href="/docs/benchmarking/results/">
                View detailed benchmarks
              </Link>
            </Button>
          </div>
        </div>
      </div>
    </section>
  );
}
