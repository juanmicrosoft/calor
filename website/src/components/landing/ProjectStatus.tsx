import { Check, Circle } from 'lucide-react';
import { cn } from '@/lib/utils';

interface StatusItem {
  label: string;
  completed: boolean;
}

const statusItems: StatusItem[] = [
  { label: 'Core compiler (lexer, parser, C# code generation)', completed: true },
  { label: 'Control flow (for, if/else, while)', completed: true },
  { label: 'Type system (Option, Result)', completed: true },
  { label: 'Contracts (requires, ensures)', completed: true },
  { label: 'Effects declarations', completed: true },
  { label: 'MSBuild SDK integration', completed: true },
  { label: 'Evaluation framework (7 metrics, 20 benchmarks)', completed: true },
  { label: 'Direct IL emission', completed: false },
  { label: 'IDE language server', completed: false },
];

export function ProjectStatus() {
  const completedCount = statusItems.filter((item) => item.completed).length;

  return (
    <section className="py-24 bg-muted/30">
      <div className="mx-auto max-w-7xl px-6 lg:px-8">
        <div className="mx-auto max-w-2xl text-center">
          <h2 className="text-3xl font-bold tracking-tight sm:text-4xl">
            Project Status
          </h2>
          <p className="mt-4 text-lg text-muted-foreground">
            {completedCount} of {statusItems.length} milestones completed
          </p>
        </div>

        <div className="mt-12 mx-auto max-w-2xl">
          <div className="space-y-3">
            {statusItems.map((item, index) => (
              <div
                key={index}
                className={cn(
                  'flex items-center gap-3 rounded-lg border p-4 transition-colors',
                  item.completed
                    ? 'bg-green-500/5 border-green-500/20'
                    : 'bg-background'
                )}
              >
                {item.completed ? (
                  <div className="flex h-6 w-6 items-center justify-center rounded-full bg-green-500">
                    <Check className="h-4 w-4 text-white" />
                  </div>
                ) : (
                  <div className="flex h-6 w-6 items-center justify-center rounded-full border-2 border-muted-foreground/30">
                    <Circle className="h-3 w-3 text-muted-foreground/30" />
                  </div>
                )}
                <span
                  className={cn(
                    'text-sm',
                    item.completed
                      ? 'text-foreground'
                      : 'text-muted-foreground'
                  )}
                >
                  {item.label}
                </span>
              </div>
            ))}
          </div>

          {/* Progress bar */}
          <div className="mt-8">
            <div className="flex items-center justify-between text-sm text-muted-foreground mb-2">
              <span>Progress</span>
              <span>{Math.round((completedCount / statusItems.length) * 100)}%</span>
            </div>
            <div className="h-2 bg-muted rounded-full overflow-hidden">
              <div
                className="h-full bg-green-500 rounded-full transition-all duration-500"
                style={{ width: `${(completedCount / statusItems.length) * 100}%` }}
              />
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
