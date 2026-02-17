'use client';

import { cn } from '@/lib/utils';

interface MetricCardProps {
  name: string;
  ratio: number;
  winner: 'calor' | 'csharp';
  description?: string;
  isCalorOnly?: boolean;
}

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

// Human-readable metric names
const metricDisplayNames: Record<string, string> = {
  TokenEconomics: 'Token Economics',
  GenerationAccuracy: 'Generation Accuracy',
  Comprehension: 'Comprehension',
  EditPrecision: 'Edit Precision',
  ErrorDetection: 'Error Detection',
  InformationDensity: 'Information Density',
  TaskCompletion: 'Task Completion',
  RefactoringStability: 'Refactoring Stability',
  Safety: 'Safety',
  EffectDiscipline: 'Effect Discipline',
  InteropEffectCoverage: 'Interop Effect Coverage',
};

// Brief interpretations for each metric
const metricInterpretations: Record<string, { calor: string; csharp: string }> = {
  TokenEconomics: {
    calor: 'More compact representation',
    csharp: "Calor's explicit syntax uses more tokens",
  },
  GenerationAccuracy: {
    calor: 'Better code generation from prompts',
    csharp: 'Mature tooling, familiar patterns',
  },
  Comprehension: {
    calor: 'Explicit structure aids understanding',
    csharp: 'Familiar syntax easier to follow',
  },
  EditPrecision: {
    calor: 'Unique IDs enable targeted changes',
    csharp: 'Established editing patterns',
  },
  ErrorDetection: {
    calor: 'Contracts surface invariant violations',
    csharp: 'Mature error handling ecosystem',
  },
  InformationDensity: {
    calor: 'More semantic content per token',
    csharp: 'Calor trades density for explicitness',
  },
  TaskCompletion: {
    calor: 'Better task completion rate',
    csharp: 'Ecosystem maturity advantage',
  },
  RefactoringStability: {
    calor: 'More stable during refactoring',
    csharp: 'Better refactoring support',
  },
  Safety: {
    calor: 'Contracts catch more bugs with better error messages',
    csharp: 'Guard clauses require manual implementation',
  },
  EffectDiscipline: {
    calor: 'Effect system prevents hidden side effect bugs',
    csharp: 'Side effect discipline relies on conventions',
  },
  InteropEffectCoverage: {
    calor: 'Calor has comprehensive BCL effect coverage',
    csharp: 'N/A',
  },
};

export function MetricCard({ name, ratio, winner, description, isCalorOnly }: MetricCardProps) {
  const displayName = metricDisplayNames[name] || name;
  const defaultInterpretation =
    winner === 'calor'
      ? metricInterpretations[name]?.calor
      : metricInterpretations[name]?.csharp;
  const interpretation = description || defaultInterpretation || '';

  return (
    <div className="space-y-2">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-3">
          <span className="font-medium">{displayName}</span>
          <span
            className={cn(
              'text-xs px-2 py-0.5 rounded-full',
              isCalorOnly
                ? 'bg-calor-pink/20 text-calor-pink'
                : winner === 'calor'
                  ? 'bg-calor-pink/20 text-calor-pink'
                  : 'bg-calor-cerulean/20 text-calor-cerulean'
            )}
          >
            {isCalorOnly ? 'Calor only' : winner === 'calor' ? 'Calor wins' : 'C# wins'}
          </span>
        </div>
        <span className="font-mono font-bold">{formatRatio(ratio, isCalorOnly)}</span>
      </div>

      <div className="relative h-8 bg-muted rounded-full overflow-hidden">
        <div
          className={cn(
            'absolute inset-y-0 left-0 rounded-full transition-all duration-500',
            isCalorOnly
              ? 'bg-gradient-to-r from-calor-pink to-calor-pink/60'
              : winner === 'calor'
                ? 'bg-gradient-to-r from-calor-pink to-calor-pink/80'
                : 'bg-gradient-to-r from-calor-cerulean to-calor-cerulean/80'
          )}
          style={{ width: isCalorOnly ? '100%' : `${getBarWidth(ratio)}%` }}
        />
        {/* Center line at 1.0 - hide for Calor-only metrics */}
        {!isCalorOnly && (
          <div className="absolute inset-y-0 left-1/2 w-px bg-border" />
        )}
      </div>

      {interpretation && (
        <p className="text-sm text-muted-foreground">{interpretation}</p>
      )}
    </div>
  );
}
