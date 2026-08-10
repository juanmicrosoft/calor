'use client';

import Link from 'next/link';
import { useEffect, useRef, useState } from 'react';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import { Clock } from 'lucide-react';
import { trackBenchmarkDetailClick } from '@/lib/analytics';
import { useScrollReveal } from '@/hooks/useScrollReveal';

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
    name: 'Token Economics',
    calorInterpretation: 'The token/character/line composite favors Calor; raw tokens do not',
    csharpInterpretation: 'The token/character/line composite favors C#',
    tieInterpretation: 'The composite is similar for both languages',
  },
  GenerationAccuracy: {
    name: 'Generation Structure',
    calorInterpretation: 'Compilation and structural completeness narrowly favor Calor',
    csharpInterpretation: 'Compilation and structural completeness favor C#',
    tieInterpretation: 'Compilation and structural completeness are similar',
  },
  Comprehension: {
    name: 'Understanding Code',
    calorInterpretation: 'The calculator finds more explicit comprehension signals',
    csharpInterpretation: 'C# familiarity helps AI follow along',
    tieInterpretation: 'AI understands both languages equally well',
  },
  EditPrecision: {
    name: 'Accurate Edits',
    calorInterpretation: 'Stable IDs improve the measured targeting signals',
    csharpInterpretation: 'C# editing patterns are well-established',
    tieInterpretation: 'AI makes equally precise edits in both languages',
  },
  ErrorDetection: {
    name: 'Finding Bugs',
    calorInterpretation: 'Contracts and effects add explicit detection signals',
    csharpInterpretation: 'C# has mature debugging tools',
    tieInterpretation: 'AI spots bugs equally well in both languages',
  },
  InformationDensity: {
    name: 'Information Density',
    calorInterpretation: 'Each line carries more information',
    csharpInterpretation: 'C# has slightly more counted semantic elements per token',
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
    calorInterpretation: 'ID-based references improve the measured stability score',
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
    calorInterpretation: 'Structural correctness signals favor Calor in estimation mode',
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
      if (a.isCalorOnly && !b.isCalorOnly) return 1;
      if (!a.isCalorOnly && b.isCalorOnly) return -1;
      return b.ratio - a.ratio;
    });
}

const results = transformMetrics(benchmarkData as BenchmarkData);
const lastUpdated = benchmarkData.timestamp;

function formatRatio(ratio: number, isCalorOnly?: boolean): string {
  if (isCalorOnly) return 'Calor only';
  return `${ratio.toFixed(2)}x`;
}

function getBarWidth(ratio: number): number {
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

function AnimatedBar({ result }: { result: BenchmarkResult }) {
  const barRef = useRef<HTMLDivElement>(null);
  const [visible, setVisible] = useState(false);

  useEffect(() => {
    const el = barRef.current;
    if (!el) return;
    const observer = new IntersectionObserver(
      ([entry]) => {
        if (entry.isIntersecting) {
          setVisible(true);
          observer.unobserve(el);
        }
      },
      { threshold: 0.3 }
    );
    observer.observe(el);
    return () => observer.disconnect();
  }, []);

  const targetWidth = result.isCalorOnly ? 100 : getBarWidth(result.ratio);

  return (
    <div ref={barRef} className="relative h-8 bg-calor-navy/5 rounded-full overflow-hidden">
      <div
        className={cn(
          'absolute inset-y-0 left-0 rounded-full transition-all duration-1000 ease-out',
          result.isCalorOnly
            ? 'bg-gradient-to-r from-calor-pink to-calor-salmon'
            : result.winner === 'calor'
              ? 'bg-gradient-to-r from-calor-pink to-calor-pink/70'
              : result.winner === 'tie'
                ? 'bg-gradient-to-r from-calor-salmon to-calor-salmon/70'
                : 'bg-gradient-to-r from-calor-cerulean to-calor-cerulean/70'
        )}
        style={{ width: visible ? `${targetWidth}%` : '0%' }}
      />
      {!result.isCalorOnly && (
        <div className="absolute inset-y-0 left-1/2 w-px bg-border" />
      )}
    </div>
  );
}

export function BenchmarkChart() {
  const sectionRef = useScrollReveal<HTMLDivElement>();

  return (
    <section className="relative py-24 overflow-hidden">
      <div className="gradient-mesh gradient-mesh-cyan absolute top-0 left-1/4 w-[500px] h-[500px] -z-10" />

      <div className="mx-auto max-w-7xl px-6 lg:px-8">
        <div className="mx-auto max-w-2xl text-center" ref={sectionRef}>
          <h2 className="text-3xl font-bold tracking-tight sm:text-4xl">
            Measured Across 217 Paired Programs
          </h2>
          <p className="mt-4 text-lg text-muted-foreground font-body">
            Eight deterministic calculators compare the same programs in Calor and C#.
          </p>
          <div className="mt-2 flex items-center justify-center gap-2 text-sm text-muted-foreground">
            <Clock className="h-4 w-4" />
            <span className="font-body">Last updated: {formatDate(lastUpdated)}</span>
          </div>
        </div>

        <div className="mt-16 mx-auto max-w-4xl">
          <div className="space-y-6">
            {results.map((result) => (
              <div key={result.category} className="space-y-2">
                <div className="flex items-center justify-between">
                  <div className="flex items-center gap-3">
                    <span className="font-medium font-display">{result.category}</span>
                    <span
                      className={cn(
                        'text-xs px-2 py-0.5 rounded-full font-body font-medium',
                        result.isCalorOnly
                          ? 'bg-calor-pink/15 text-calor-pink'
                          : result.winner === 'calor'
                            ? 'bg-calor-pink/15 text-calor-pink'
                            : result.winner === 'tie'
                              ? 'bg-calor-salmon/15 text-calor-salmon'
                              : 'bg-calor-cerulean/15 text-calor-cerulean'
                      )}
                    >
                      {result.isCalorOnly ? 'Calor only' : result.winner === 'calor' ? 'Calor wins' : result.winner === 'tie' ? 'Tie' : 'C# wins'}
                    </span>
                  </div>
                  <span className="font-mono font-bold text-sm">
                    {formatRatio(result.ratio, result.isCalorOnly)}
                  </span>
                </div>

                <AnimatedBar result={result} />

                <p className="text-sm text-muted-foreground font-body">
                  {result.interpretation}
                </p>
              </div>
            ))}
          </div>

          {/* Legend */}
          <div className="mt-8 flex items-center justify-center gap-8 text-sm text-muted-foreground font-body">
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
            <span className="text-xs opacity-50">|</span>
            <span className="text-xs">Center line = 1.0x (equal)</span>
          </div>

          {/* Key finding */}
          <div className="mt-12 p-6 rounded-xl border bg-gradient-to-br from-calor-pink/5 to-calor-salmon/5">
            <h3 className="font-semibold mb-2 font-display">The Bottom Line</h3>
            <p className="text-muted-foreground font-body leading-relaxed">
              <strong className="text-foreground">The v0.12 static run favors Calor in seven of eight defined metrics.</strong>{' '}
              Its strongest scores come from explicit comprehension and error-detection signals; C# narrowly leads information density.
              These calculators do not directly measure model productivity or production defect rates.
            </p>
          </div>

          <div className="mt-8 text-center">
            <Button variant="outline" asChild className="font-body">
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
