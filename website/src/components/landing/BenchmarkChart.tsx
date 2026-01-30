import Link from 'next/link';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';

interface BenchmarkResult {
  category: string;
  ratio: number;
  winner: 'opal' | 'csharp';
  interpretation: string;
}

const results: BenchmarkResult[] = [
  {
    category: 'Comprehension',
    ratio: 1.33,
    winner: 'opal',
    interpretation: 'Explicit structure aids understanding',
  },
  {
    category: 'Error Detection',
    ratio: 1.19,
    winner: 'opal',
    interpretation: 'Contracts surface invariant violations',
  },
  {
    category: 'Edit Precision',
    ratio: 1.15,
    winner: 'opal',
    interpretation: 'Unique IDs enable targeted changes',
  },
  {
    category: 'Generation Accuracy',
    ratio: 0.94,
    winner: 'csharp',
    interpretation: 'Mature tooling, familiar patterns',
  },
  {
    category: 'Task Completion',
    ratio: 0.93,
    winner: 'csharp',
    interpretation: 'Ecosystem maturity advantage',
  },
  {
    category: 'Token Economics',
    ratio: 0.67,
    winner: 'csharp',
    interpretation: "OPAL's explicit syntax uses more tokens",
  },
];

function formatRatio(ratio: number): string {
  if (ratio >= 1) {
    return `${ratio.toFixed(2)}x`;
  }
  return `${ratio.toFixed(2)}x`;
}

function getBarWidth(ratio: number): number {
  // Normalize to 0-100 scale, where 1.0 = 50%
  if (ratio >= 1) {
    return Math.min(50 + (ratio - 1) * 50, 100);
  }
  return Math.max(ratio * 50, 10);
}

export function BenchmarkChart() {
  return (
    <section className="py-24">
      <div className="mx-auto max-w-7xl px-6 lg:px-8">
        <div className="mx-auto max-w-2xl text-center">
          <h2 className="text-3xl font-bold tracking-tight sm:text-4xl">
            Benchmark Results
          </h2>
          <p className="mt-4 text-lg text-muted-foreground">
            Evaluated across 20 paired OPAL/C# programs using V2 compact syntax
          </p>
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
                        result.winner === 'opal'
                          ? 'bg-green-500/10 text-green-600'
                          : 'bg-blue-500/10 text-blue-600'
                      )}
                    >
                      {result.winner === 'opal' ? 'OPAL' : 'C#'} wins
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
                      result.winner === 'opal'
                        ? 'bg-gradient-to-r from-green-500 to-green-400'
                        : 'bg-gradient-to-r from-blue-500 to-blue-400'
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
              <div className="w-3 h-3 rounded-full bg-green-500" />
              <span>OPAL better</span>
            </div>
            <div className="flex items-center gap-2">
              <div className="w-3 h-3 rounded-full bg-blue-500" />
              <span>C# better</span>
            </div>
            <span className="text-xs">|</span>
            <span className="text-xs">Center line = 1.0x (equal)</span>
          </div>

          {/* Key finding */}
          <div className="mt-12 p-6 rounded-lg border bg-muted/50">
            <h3 className="font-semibold mb-2">Key Finding</h3>
            <p className="text-muted-foreground">
              OPAL excels where explicitness matters - comprehension, error detection,
              and edit precision. C# wins on token efficiency, reflecting a fundamental
              tradeoff: explicit semantics require more tokens but enable better agent reasoning.
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
