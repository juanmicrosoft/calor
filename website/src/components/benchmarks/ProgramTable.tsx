'use client';

import { useState, useMemo } from 'react';
import { cn } from '@/lib/utils';
import { ChevronUp, ChevronDown, Check, X } from 'lucide-react';
import { trackProgramTableSort, trackProgramTableFilter } from '@/lib/analytics';
import { staticMetricLabels } from '@/lib/benchmark-labels';

interface ProgramData {
  identity: string;
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

function formatValue(value: number): string {
  return value.toFixed(2);
}

function SortHeader({ field, children, className, sortField, sortDirection, onSort }: {
  field: SortField;
  children: React.ReactNode;
  className?: string;
  sortField: SortField;
  sortDirection: SortDirection;
  onSort: (field: SortField) => void;
}) {
  return (
    <th
      scope="col"
      aria-sort={sortField === field ? (sortDirection === 'asc' ? 'ascending' : 'descending') : undefined}
      className={cn('px-3 py-2 text-left text-xs font-medium text-muted-foreground', className)}
    >
      <button type="button" onClick={() => onSort(field)}
        className="flex items-center gap-1 hover:text-foreground focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary">
        {children}
        {sortField === field && (sortDirection === 'asc'
          ? <ChevronUp className="h-3 w-3" aria-hidden="true" />
          : <ChevronDown className="h-3 w-3" aria-hidden="true" />)}
      </button>
    </th>
  );
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

  const sortProps = { sortField, sortDirection, onSort: handleSort };

  return (
    <div className="space-y-4">
      <p className="text-sm text-muted-foreground">
        Values are Calor/C# static-score ratios: above 1 means a higher Calor
        calculator score, below 1 a higher C# score, and 1 equal scores.
        The legacy composite combines metric scores; Token Economics combines
        token, character, and line ratios, not raw-token savings.
      </p>
      {/* Level filter */}
      <div className="flex items-center gap-2 text-sm">
        <span className="text-muted-foreground">Filter by level:</span>
        <button
          aria-pressed={levelFilter === null}
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
            aria-pressed={levelFilter === level}
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
              <SortHeader {...sortProps} field="name" className="sticky left-0 bg-muted/50">
                Program
              </SortHeader>
              <SortHeader {...sortProps} field="level">Lvl</SortHeader>
              <th className="px-3 py-2 text-left text-xs font-medium text-muted-foreground">
                Status
              </th>
              <SortHeader {...sortProps} field="advantage">Static-score composite</SortHeader>
              {metricNames.map((metric) => (
                <SortHeader {...sortProps} key={metric} field={metric}>
                  {staticMetricLabels[metric]?.name || metric}
                </SortHeader>
              ))}
            </tr>
          </thead>
          <tbody className="divide-y">
            {sortedPrograms.map((program) => (
              <tr key={program.identity} className="hover:bg-muted/30 transition-colors">
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
                <td className="px-3 py-2 font-mono">
                  {formatValue(program.advantage)}
                </td>
                {metricNames.map((metric) => (
                  <td
                    key={metric}
                    className="px-3 py-2 font-mono"
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
      </p>
    </div>
  );
}
