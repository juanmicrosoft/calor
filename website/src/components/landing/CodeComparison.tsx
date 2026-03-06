'use client';

import { useState } from 'react';
import { cn } from '@/lib/utils';
import { trackCodeComparisonTab } from '@/lib/analytics';
import { useScrollReveal } from '@/hooks/useScrollReveal';

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
  { line: 0, text: 'Permanent ID means AI can find this function even after you rename it' },
  { line: 3, text: 'Rule: input must be >= 0. Compiler enforces this automatically.' },
  { line: 4, text: 'Rule: output must be >= 0. No way to return invalid results.' },
  { line: 5, text: 'No database or network calls—guaranteed by the compiler.' },
];

const csharpAnnotations = [
  { line: 2, text: 'AI has to read the exception message to understand the rule' },
  { line: 5, text: 'Rules are buried in code—easy for AI to miss or misunderstand' },
  { line: 0, text: 'If you rename this function, AI references break' },
];

export function CodeComparison() {
  const [activeTab, setActiveTab] = useState<'calor' | 'csharp'>('calor');
  const sectionRef = useScrollReveal<HTMLDivElement>();
  const contentRef = useScrollReveal<HTMLDivElement>({ staggerChildren: false });

  return (
    <section className="relative py-24 overflow-hidden">
      {/* Gradient mesh background */}
      <div className="gradient-mesh gradient-mesh-pink absolute top-0 right-0 w-[500px] h-[500px] -z-10" />
      <div className="gradient-mesh gradient-mesh-cyan absolute bottom-0 left-0 w-[400px] h-[400px] -z-10" />

      <div className="mx-auto max-w-7xl px-6 lg:px-8">
        <div className="mx-auto max-w-2xl text-center" ref={sectionRef}>
          <h2 className="text-3xl font-bold tracking-tight sm:text-4xl">
            Why AI Makes Fewer Mistakes in Calor
          </h2>
          <p className="mt-4 text-lg text-muted-foreground font-body">
            When the rules are visible in the code, AI doesn&apos;t have to guess them.
          </p>
        </div>

        <div className="mt-16 mx-auto max-w-5xl" ref={contentRef}>
          {/* Tab buttons */}
          <div className="flex justify-center mb-6">
            <div className="inline-flex rounded-lg border p-1 bg-background">
              <button
                onClick={() => { setActiveTab('calor'); trackCodeComparisonTab('calor'); }}
                className={cn(
                  'px-4 py-2 rounded-md text-sm font-medium transition-colors font-body',
                  activeTab === 'calor'
                    ? 'bg-calor-navy text-white'
                    : 'hover:bg-muted'
                )}
              >
                Calor - Rules Are Visible
              </button>
              <button
                onClick={() => { setActiveTab('csharp'); trackCodeComparisonTab('csharp'); }}
                className={cn(
                  'px-4 py-2 rounded-md text-sm font-medium transition-colors font-body',
                  activeTab === 'csharp'
                    ? 'bg-calor-navy text-white'
                    : 'hover:bg-muted'
                )}
              >
                C# - Rules Are Hidden
              </button>
            </div>
          </div>

          {/* Code display */}
          <div className="grid lg:grid-cols-5 gap-6">
            {/* Code block — wider */}
            <div className="lg:col-span-3 rounded-lg border border-calor-navy/20 bg-calor-navy overflow-hidden shadow-xl shadow-calor-navy/20">
              <div className="flex items-center justify-between border-b border-white/10 px-4 py-2">
                <span className="text-sm text-calor-cyan/70 font-mono">
                  {activeTab === 'calor' ? 'program.calr' : 'Program.cs'}
                </span>
                <div className="flex gap-1.5">
                  <div className="w-2.5 h-2.5 rounded-full bg-calor-pink/60" />
                  <div className="w-2.5 h-2.5 rounded-full bg-calor-salmon/60" />
                  <div className="w-2.5 h-2.5 rounded-full bg-calor-cyan/60" />
                </div>
              </div>
              <pre className="p-5 text-sm leading-7 overflow-x-auto">
                <code className={cn(
                  'font-mono',
                  activeTab === 'calor' ? 'text-calor-cyan' : 'text-white/90'
                )}>
                  {activeTab === 'calor' ? calorCode : csharpCode}
                </code>
              </pre>
            </div>

            {/* Annotations — narrower, floating style */}
            <div className="lg:col-span-2 space-y-3">
              <h3 className="font-semibold text-base font-display mb-4">
                {activeTab === 'calor'
                  ? 'What your AI sees immediately:'
                  : 'What your AI has to figure out:'}
              </h3>
              <ul className="space-y-2.5">
                {(activeTab === 'calor' ? calorAnnotations : csharpAnnotations).map(
                  (annotation, i) => (
                    <li
                      key={i}
                      className={cn(
                        'flex items-start gap-3 p-3 rounded-lg transition-all',
                        activeTab === 'calor'
                          ? 'bg-calor-cyan/5 border border-calor-cyan/15 hover:border-calor-cyan/30'
                          : 'bg-calor-salmon/5 border border-calor-salmon/15 hover:border-calor-salmon/30'
                      )}
                    >
                      <span
                        className={cn(
                          'shrink-0 w-6 h-6 rounded-full flex items-center justify-center text-xs font-bold font-mono',
                          activeTab === 'calor'
                            ? 'bg-calor-cyan/15 text-calor-cerulean'
                            : 'bg-calor-salmon/15 text-calor-salmon'
                        )}
                      >
                        {i + 1}
                      </span>
                      <span className="text-sm font-body">{annotation.text}</span>
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
