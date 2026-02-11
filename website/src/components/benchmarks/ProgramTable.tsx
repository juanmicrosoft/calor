'use client';

import { useState, useMemo } from 'react';
import { cn } from '@/lib/utils';
import { ChevronUp, ChevronDown, Check, X } from 'lucide-react';
import { trackProgramTableSort, trackProgramTableFilter } from '@/lib/analytics';

interface ProgramData {
  id: string;
  name: string;
  level: number;
  features: string[];
  calorSuccess: boolean;
  cSharpSuccess: boolean;
  advantage: number;
  metrics: Record<string, number>;
}

interface ProgramTableProps {
  programs: ProgramData[];
  metricNames: string[];
}

type SortField = 'name' | 'level' | 'advantage' | string;
type SortDirection = 'asc' | 'desc';

// Human-readable metric names
const metricDisplayNames: Record<string, string> = {
  TokenEconomics: 'Tokens',
  GenerationAccuracy: 'Gen Acc',
  Comprehension: 'Comp',
  EditPrecision: 'Edit',
  ErrorDetection: 'Err Det',
  InformationDensity: 'Info Den',
  TaskCompletion: 'Task',
  RefactoringStability: 'Refactor',
  ContractVerification: 'Contracts',
  EffectSoundness: 'Effects',
  InteropEffectCoverage: 'Interop',
};

function formatValue(value: number): string {
  return value.toFixed(2);
}

function getValueColor(value: number): string {
  if (value >= 1.0) return 'text-calor-pink font-medium';
  if (value >= 0.8) return 'text-muted-foreground';
  return 'text-calor-cerulean';
}

export function ProgramTable({ programs, metricNames }: ProgramTableProps) {
  const [sortField, setSortField] = useState<SortField>('name');
  const [sortDirection, setSortDirection] = useState<SortDirection>('asc');
  const [levelFilter, setLevelFilter] = useState<number | null>(null);

  const handleSort = (field: SortField) => {
    trackProgramTableSort(field);
    if (sortField === field) {
      setSortDirection(sortDirection === 'asc' ? 'desc' : 'asc');
    } else {
      setSortField(field);
      setSortDirection('asc');
    }
  };

  const sortedPrograms = useMemo(() => {
    let filtered = programs;
    if (levelFilter !== null) {
      filtered = programs.filter((p) => p.level === levelFilter);
    }

    return [...filtered].sort((a, b) => {
      let aValue: string | number;
      let bValue: string | number;

      if (sortField === 'name') {
        aValue = a.name;
        bValue = b.name;
      } else if (sortField === 'level') {
        aValue = a.level;
        bValue = b.level;
      } else if (sortField === 'advantage') {
        aValue = a.advantage;
        bValue = b.advantage;
      } else {
        // Metric field
        aValue = a.metrics[sortField] ?? 0;
        bValue = b.metrics[sortField] ?? 0;
      }

      if (typeof aValue === 'string') {
        const comparison = aValue.localeCompare(bValue as string);
        return sortDirection === 'asc' ? comparison : -comparison;
      }

      const comparison = aValue - (bValue as number);
      return sortDirection === 'asc' ? comparison : -comparison;
    });
  }, [programs, sortField, sortDirection, levelFilter]);

  const levels = useMemo(() => {
    const uniqueLevels = [...new Set(programs.map((p) => p.level))];
    return uniqueLevels.sort((a, b) => a - b);
  }, [programs]);

  const SortHeader = ({
    field,
    children,
    className,
  }: {
    field: SortField;
    children: React.ReactNode;
    className?: string;
  }) => (
    <th
      className={cn(
        'px-3 py-2 text-left text-xs font-medium text-muted-foreground cursor-pointer hover:text-foreground transition-colors',
        className
      )}
      onClick={() => handleSort(field)}
    >
      <div className="flex items-center gap-1">
        {children}
        {sortField === field && (
          sortDirection === 'asc' ? (
            <ChevronUp className="h-3 w-3" />
          ) : (
            <ChevronDown className="h-3 w-3" />
          )
        )}
      </div>
    </th>
  );

  return (
    <div className="space-y-4">
      {/* Level filter */}
      <div className="flex items-center gap-2 text-sm">
        <span className="text-muted-foreground">Filter by level:</span>
        <button
          className={cn(
            'px-2 py-1 rounded text-xs',
            levelFilter === null
              ? 'bg-primary text-primary-foreground'
              : 'bg-muted hover:bg-muted/80'
          )}
          onClick={() => { trackProgramTableFilter('all'); setLevelFilter(null); }}
        >
          All
        </button>
        {levels.map((level) => (
          <button
            key={level}
            className={cn(
              'px-2 py-1 rounded text-xs',
              levelFilter === level
                ? 'bg-primary text-primary-foreground'
                : 'bg-muted hover:bg-muted/80'
            )}
            onClick={() => { trackProgramTableFilter(`L${level}`); setLevelFilter(level); }}
          >
            L{level}
          </button>
        ))}
      </div>

      {/* Table */}
      <div className="overflow-x-auto rounded-lg border">
        <table className="w-full text-sm">
          <thead className="bg-muted/50">
            <tr>
              <SortHeader field="name" className="sticky left-0 bg-muted/50">
                Program
              </SortHeader>
              <SortHeader field="level">Lvl</SortHeader>
              <th className="px-3 py-2 text-left text-xs font-medium text-muted-foreground">
                Status
              </th>
              <SortHeader field="advantage">Adv</SortHeader>
              {metricNames.map((metric) => (
                <SortHeader key={metric} field={metric}>
                  {metricDisplayNames[metric] || metric}
                </SortHeader>
              ))}
            </tr>
          </thead>
          <tbody className="divide-y">
            {sortedPrograms.map((program) => (
              <tr key={program.id} className="hover:bg-muted/30 transition-colors">
                <td className="px-3 py-2 font-medium sticky left-0 bg-background">
                  {program.name}
                </td>
                <td className="px-3 py-2 text-muted-foreground">{program.level}</td>
                <td className="px-3 py-2">
                  <div className="flex items-center gap-2">
                    <span
                      title="Calor"
                      className={cn(
                        'flex items-center justify-center w-5 h-5 rounded-full text-xs',
                        program.calorSuccess
                          ? 'bg-calor-pink/20 text-calor-pink'
                          : 'bg-calor-cerulean/20 text-calor-cerulean'
                      )}
                    >
                      {program.calorSuccess ? (
                        <Check className="h-3 w-3" />
                      ) : (
                        <X className="h-3 w-3" />
                      )}
                    </span>
                    <span
                      title="C#"
                      className={cn(
                        'flex items-center justify-center w-5 h-5 rounded-full text-xs',
                        program.cSharpSuccess
                          ? 'bg-green-500/20 text-green-600'
                          : 'bg-red-500/20 text-red-600'
                      )}
                    >
                      {program.cSharpSuccess ? (
                        <Check className="h-3 w-3" />
                      ) : (
                        <X className="h-3 w-3" />
                      )}
                    </span>
                  </div>
                </td>
                <td className={cn('px-3 py-2 font-mono', getValueColor(program.advantage))}>
                  {formatValue(program.advantage)}
                </td>
                {metricNames.map((metric) => (
                  <td
                    key={metric}
                    className={cn(
                      'px-3 py-2 font-mono',
                      getValueColor(program.metrics[metric] ?? 0)
                    )}
                  >
                    {formatValue(program.metrics[metric] ?? 0)}
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <p className="text-xs text-muted-foreground">
        Showing {sortedPrograms.length} of {programs.length} programs.
        Values above 1.0 favor Calor (highlighted in pink), values below 1.0 favor C# (highlighted in teal).
      </p>
    </div>
  );
}
