'use client';

import { cn } from '@/lib/utils';
import { Bot, CheckCircle2, XCircle } from 'lucide-react';

// Build-time import of agent refactoring data
import agentData from '../../../public/data/agent-refactoring-results.json';

interface AgentRefactoringData {
  version: string;
  timestamp: string;
  commit: string;
  benchmark: string;
  description: string;
  results: {
    calor: {
      passRate: number;
      tasksPassed: number;
      tasksTotal: number;
    };
    csharp: {
      passRate: number;
      tasksPassed: number;
      tasksTotal: number;
    };
  };
  categories: Array<{
    name: string;
    calor: { passed: number; total: number };
    csharp: { passed: number; total: number };
  }>;
}

const data = agentData as AgentRefactoringData;

function PassRateBar({ passRate, winner }: { passRate: number; winner: boolean }) {
  return (
    <div className="flex items-center gap-2">
      <div className="flex-1 h-3 bg-muted rounded-full overflow-hidden">
        <div
          className={cn(
            'h-full rounded-full transition-all',
            winner ? 'bg-calor-pink' : 'bg-calor-cerulean'
          )}
          style={{ width: `${passRate}%` }}
        />
      </div>
      <span className={cn('text-sm font-medium w-12 text-right', winner && 'text-calor-pink')}>
        {passRate}%
      </span>
    </div>
  );
}

export function AgentRefactoringCard() {
  const calorWins = data.results.calor.passRate >= data.results.csharp.passRate;
  const csharpWins = data.results.csharp.passRate > data.results.calor.passRate;

  return (
    <div className="p-6 rounded-lg border bg-card">
      <div className="flex items-center gap-2 mb-4">
        <Bot className="h-5 w-5 text-calor-pink" />
        <h3 className="text-lg font-semibold">Agent Refactoring Benchmark</h3>
      </div>

      <p className="text-sm text-muted-foreground mb-4">
        Measures Claude Code agent success rates on real refactoring tasks (rename, extract, inline,
        move, add contracts, change signature).
      </p>

      {/* Overall Results */}
      <div className="space-y-3 mb-6">
        <div>
          <div className="flex justify-between text-sm mb-1">
            <span className="font-medium">Calor</span>
            <span className="text-muted-foreground">
              {data.results.calor.tasksPassed}/{data.results.calor.tasksTotal} tasks
            </span>
          </div>
          <PassRateBar passRate={data.results.calor.passRate} winner={calorWins} />
        </div>

        <div>
          <div className="flex justify-between text-sm mb-1">
            <span className="font-medium">C#</span>
            <span className="text-muted-foreground">
              {data.results.csharp.tasksPassed}/{data.results.csharp.tasksTotal} tasks
            </span>
          </div>
          <PassRateBar passRate={data.results.csharp.passRate} winner={csharpWins} />
        </div>
      </div>

      {/* Category Breakdown */}
      <details className="group">
        <summary className="cursor-pointer text-sm text-muted-foreground hover:text-foreground transition-colors">
          View category breakdown
        </summary>
        <div className="mt-3 space-y-2">
          {data.categories.map((cat) => {
            const calorRate = Math.round((cat.calor.passed / cat.calor.total) * 100);
            const csharpRate = Math.round((cat.csharp.passed / cat.csharp.total) * 100);
            const catWinner = calorRate >= csharpRate ? 'calor' : 'csharp';

            return (
              <div key={cat.name} className="flex items-center text-sm">
                <span className="w-32 truncate">{cat.name}</span>
                <div className="flex items-center gap-4 flex-1">
                  <div className="flex items-center gap-1">
                    {cat.calor.passed === cat.calor.total ? (
                      <CheckCircle2 className="h-3 w-3 text-green-500" />
                    ) : (
                      <XCircle className="h-3 w-3 text-red-500" />
                    )}
                    <span
                      className={cn('w-8', catWinner === 'calor' && 'font-medium text-calor-pink')}
                    >
                      {cat.calor.passed}/{cat.calor.total}
                    </span>
                  </div>
                  <div className="flex items-center gap-1">
                    {cat.csharp.passed === cat.csharp.total ? (
                      <CheckCircle2 className="h-3 w-3 text-green-500" />
                    ) : (
                      <XCircle className="h-3 w-3 text-red-500" />
                    )}
                    <span
                      className={cn(
                        'w-8',
                        catWinner === 'csharp' && 'font-medium text-calor-cerulean'
                      )}
                    >
                      {cat.csharp.passed}/{cat.csharp.total}
                    </span>
                  </div>
                </div>
              </div>
            );
          })}
        </div>
      </details>

      {/* Methodology note */}
      <div className="mt-4 pt-4 border-t text-xs text-muted-foreground">
        <span>Majority voting (2/3 runs) with compilation + Z3 verification</span>
        {data.commit && (
          <span className="ml-2 font-mono bg-muted px-1.5 py-0.5 rounded">
            {data.commit.slice(0, 7)}
          </span>
        )}
      </div>
    </div>
  );
}
