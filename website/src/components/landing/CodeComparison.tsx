'use client';

import { useState } from 'react';
import { cn } from '@/lib/utils';

const calorCode = `§F{f_01J5X7K9M2:Square:pub}
  §I{i32:x}
  §O{i32}
  §Q (>= x 0)
  §S (>= result 0)
  §R (* x x)
§/F{f_01J5X7K9M2}`;

const csharpCode = `public static int Square(int x)
{
    if (!(x >= 0))
        throw new ArgumentException("Precondition failed");
    var result = x * x;
    if (!(result >= 0))
        throw new InvalidOperationException("Postcondition failed");
    return result;
}`;

const calorAnnotations = [
  { line: 0, text: 'Stable ID f_01J5X7K9M2 - survives renames and refactors' },
  { line: 3, text: 'Precondition (§Q): x >= 0' },
  { line: 4, text: 'Postcondition (§S): result >= 0' },
  { line: 5, text: 'No side effects (no §E declaration)' },
];

const csharpAnnotations = [
  { line: 2, text: 'Must parse exception patterns to find contracts' },
  { line: 5, text: 'Assertions are implementation detail, not syntax' },
  { line: 0, text: 'Hope line numbers don\'t change across edits' },
];

export function CodeComparison() {
  const [activeTab, setActiveTab] = useState<'calor' | 'csharp'>('calor');

  return (
    <section className="py-24 bg-muted/30">
      <div className="mx-auto max-w-7xl px-6 lg:px-8">
        <div className="mx-auto max-w-2xl text-center">
          <h2 className="text-3xl font-bold tracking-tight sm:text-4xl">
            What Agents See
          </h2>
          <p className="mt-4 text-lg text-muted-foreground">
            Calor makes semantics explicit. C# requires inference.
          </p>
        </div>

        <div className="mt-16 mx-auto max-w-5xl">
          {/* Tab buttons */}
          <div className="flex justify-center mb-6">
            <div className="inline-flex rounded-lg border p-1 bg-background">
              <button
                onClick={() => setActiveTab('calor')}
                className={cn(
                  'px-4 py-2 rounded-md text-sm font-medium transition-colors',
                  activeTab === 'calor'
                    ? 'bg-primary text-primary-foreground'
                    : 'hover:bg-muted'
                )}
              >
                Calor - Everything Explicit
              </button>
              <button
                onClick={() => setActiveTab('csharp')}
                className={cn(
                  'px-4 py-2 rounded-md text-sm font-medium transition-colors',
                  activeTab === 'csharp'
                    ? 'bg-primary text-primary-foreground'
                    : 'hover:bg-muted'
                )}
              >
                C# - Requires Inference
              </button>
            </div>
          </div>

          {/* Code display */}
          <div className="grid lg:grid-cols-2 gap-6">
            {/* Code block */}
            <div className="rounded-lg border bg-zinc-950 overflow-hidden">
              <div className="flex items-center justify-between border-b border-zinc-800 px-4 py-2">
                <span className="text-sm text-zinc-400">
                  {activeTab === 'calor' ? 'program.calr' : 'Program.cs'}
                </span>
              </div>
              <pre className="p-4 text-sm leading-6 overflow-x-auto">
                <code className="text-zinc-100">
                  {activeTab === 'calor' ? calorCode : csharpCode}
                </code>
              </pre>
            </div>

            {/* Annotations */}
            <div className="space-y-4">
              <h3 className="font-semibold text-lg">
                {activeTab === 'calor'
                  ? 'What Calor tells the agent directly:'
                  : 'What C# requires the agent to infer:'}
              </h3>
              <ul className="space-y-3">
                {(activeTab === 'calor' ? calorAnnotations : csharpAnnotations).map(
                  (annotation, i) => (
                    <li
                      key={i}
                      className={cn(
                        'flex items-start gap-3 p-3 rounded-lg',
                        activeTab === 'calor'
                          ? 'bg-green-500/10 border border-green-500/20'
                          : 'bg-yellow-500/10 border border-yellow-500/20'
                      )}
                    >
                      <span
                        className={cn(
                          'shrink-0 w-6 h-6 rounded-full flex items-center justify-center text-xs font-medium',
                          activeTab === 'calor'
                            ? 'bg-green-500/20 text-green-600'
                            : 'bg-yellow-500/20 text-yellow-600'
                        )}
                      >
                        {i + 1}
                      </span>
                      <span className="text-sm">{annotation.text}</span>
                    </li>
                  )
                )}
              </ul>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
