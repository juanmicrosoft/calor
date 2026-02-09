'use client';

// Build-time import of benchmark data
import benchmarkData from '../../../public/data/benchmark-results.json';

interface MetricData {
  ratio: number;
  winner: 'calor' | 'csharp';
}

interface BenchmarkData {
  metrics: Record<string, MetricData>;
}

// Human-readable metric names
const METRIC_NAMES: Record<string, string> = {
  Comprehension: 'Comprehension',
  ErrorDetection: 'Error Detection',
  EditPrecision: 'Edit Precision',
  RefactoringStability: 'Refactoring Stability',
  GenerationAccuracy: 'Generation Accuracy',
  TaskCompletion: 'Task Completion',
  TokenEconomics: 'Token Economics',
  InformationDensity: 'Information Density',
};

// Preferred display order (Calor wins first, then C# wins)
const METRIC_ORDER = [
  'Comprehension',
  'ErrorDetection',
  'EditPrecision',
  'RefactoringStability',
  'GenerationAccuracy',
  'TaskCompletion',
  'TokenEconomics',
  'InformationDensity',
];

const data = benchmarkData as BenchmarkData;

function formatRatio(ratio: number, winner: string): string {
  const formatted = ratio.toFixed(2) + 'x';
  return winner === 'calor' ? `**${formatted}**` : formatted;
}

export function BenchmarkSummaryTable() {
  const sortedMetrics = METRIC_ORDER
    .filter(key => data.metrics[key])
    .map(key => ({
      key,
      name: METRIC_NAMES[key] || key,
      ...data.metrics[key],
    }));

  return (
    <div className="overflow-x-auto">
      <table className="w-full text-sm">
        <thead>
          <tr className="border-b">
            <th className="text-left py-2 px-3 font-semibold">Category</th>
            <th className="text-left py-2 px-3 font-semibold">Calor vs C#</th>
            <th className="text-left py-2 px-3 font-semibold">Winner</th>
          </tr>
        </thead>
        <tbody>
          {sortedMetrics.map((metric) => (
            <tr key={metric.key} className="border-b border-border/50">
              <td className="py-2 px-3">{metric.name}</td>
              <td className="py-2 px-3 font-mono">
                {metric.winner === 'calor' ? (
                  <strong>{metric.ratio.toFixed(2)}x</strong>
                ) : (
                  <span>{metric.ratio.toFixed(2)}x</span>
                )}
              </td>
              <td className="py-2 px-3">
                <span
                  className={
                    metric.winner === 'calor'
                      ? 'text-calor-cerulean font-medium'
                      : 'text-calor-salmon'
                  }
                >
                  {metric.winner === 'calor' ? 'Calor' : 'C#'}
                </span>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
