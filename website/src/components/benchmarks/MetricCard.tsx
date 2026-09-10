'use client';

import { staticMetricLabels } from '@/lib/benchmark-labels';

interface MetricCardProps {
  name: string;
  ratio: number;
  winner: 'calor' | 'csharp' | 'tie';
  description?: string;
  isCalorOnly?: boolean;
}

export function MetricCard({ name, ratio, description, isCalorOnly }: MetricCardProps) {
  const label = staticMetricLabels[name];
  return (
    <div className="space-y-2 border-b border-border pb-4" data-static-metric={name}>
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h4 className="font-medium">{label?.name || name} static score</h4>
        <span className="font-mono">
          {isCalorOnly ? 'No comparison' : `${ratio.toFixed(2)}x direction-normalized`}
        </span>
      </div>
      <p className="text-sm text-muted-foreground">{description || label?.description}</p>
    </div>
  );
}
