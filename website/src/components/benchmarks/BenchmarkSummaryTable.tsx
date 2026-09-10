'use client';

import benchmarkData from '../../../public/data/benchmark-results.json';
import { staticMetricLabels, staticMetricOrder } from '@/lib/benchmark-labels';

export function BenchmarkSummaryTable() {
  const metrics: Record<string, { ratio: number }> = benchmarkData.metrics;
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-sm">
        <thead>
          <tr className="border-b">
            <th className="text-left py-2 px-3 font-semibold">Static metric</th>
            <th className="text-left py-2 px-3 font-semibold">Direction-normalized ratio</th>
          </tr>
        </thead>
        <tbody>
          {staticMetricOrder.filter(name => metrics[name]).map(name => (
            <tr key={name} className="border-b border-border/50">
              <td className="py-2 px-3">{staticMetricLabels[name].name}</td>
              <td className="py-2 px-3 font-mono">{metrics[name].ratio.toFixed(2)}x</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
