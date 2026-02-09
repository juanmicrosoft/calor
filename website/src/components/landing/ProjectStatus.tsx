import { Check } from 'lucide-react';

const completedMilestones = [
  'Core compiler',
  'Control flow',
  'Type system',
  'Contracts',
  'Effects',
  'MSBuild SDK',
  'Benchmarks',
  'VS Code Extension',
];

const currentFocus = 'Direct IL emission';

export function ProjectStatus() {
  return (
    <section className="py-16 bg-muted/30">
      <div className="mx-auto max-w-7xl px-6 lg:px-8">
        <div className="mx-auto max-w-3xl">
          <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 p-6 rounded-lg border bg-background">
            <div className="flex items-center gap-3">
              <div className="flex h-10 w-10 items-center justify-center rounded-full bg-calor-cyan">
                <Check className="h-5 w-5 text-calor-navy" />
              </div>
              <div>
                <h3 className="font-semibold">Project Status</h3>
                <p className="text-sm text-muted-foreground">
                  {completedMilestones.length} milestones completed
                </p>
              </div>
            </div>
            <div className="flex flex-wrap gap-2">
              {completedMilestones.map((milestone) => (
                <span
                  key={milestone}
                  className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs bg-calor-cyan/10 text-calor-cerulean"
                >
                  {milestone}
                </span>
              ))}
            </div>
          </div>
          <p className="mt-4 text-center text-sm text-muted-foreground">
            Currently working on: <span className="font-medium text-foreground">{currentFocus}</span>
          </p>
        </div>
      </div>
    </section>
  );
}
