'use client';

import { useState } from 'react';
import Link from 'next/link';
import { cn } from '@/lib/utils';
import { trackCodeComparisonTab } from '@/lib/analytics';

const calorCode = `§M{m001:Math}
  §F{f001:Square:pub} (i32:x) -> i32
    §E{}
    §Q (>= x 0)
    §S (>= result 0)
    §R (* x x)`;

const csharpCode = `public static int Square(int x)
{
    if (!(x >= 0))
        throw new ArgumentException("Precondition failed");
    var result = checked(x * x);
    if (!(result >= 0))
        throw new InvalidOperationException("Postcondition failed");
    return result;
}`;

const annotations = {
  calor: [
    '§Q and §S declare the input and normal-return obligations.',
    'The optional f001 ID gives editing tools a named target when preserved across edits.',
    '§E{} declares no effects; enforcement depends on resolved calls and documented coverage.',
  ],
  csharp: [
    'The visible x >= 0 condition checks the same input requirement.',
    'The result >= 0 guard checks the normal-return requirement in ordinary control flow.',
    'checked(x * x) matches native Calor’s default overflow trap; ordinary unchecked C# differs.',
  ],
};

export function CodeComparison() {
  const [activeTab, setActiveTab] = useState<'calor' | 'csharp'>('calor');
  return (
    <section className="py-16" aria-labelledby="contract-comparison-heading">
      <div className="mx-auto max-w-5xl px-6 lg:px-8">
        <h2 id="contract-comparison-heading" className="text-3xl font-bold tracking-tight">
          Compare runtime contracts
        </h2>
        <p className="mt-4 text-muted-foreground font-body">
          Both examples reject negative inputs and check a nonnegative result.
          These are function fragments with no caller, not complete runnable programs.
          Exception types and diagnostic details differ.
        </p>
        <div className="mt-6 flex flex-wrap gap-2">
          {(['calor', 'csharp'] as const).map(tab => (
            <button key={tab} type="button" aria-pressed={activeTab === tab}
              onClick={() => { setActiveTab(tab); trackCodeComparisonTab(tab); }}
              className={cn('rounded-md border px-4 py-2 text-sm font-medium',
                activeTab === tab ? 'bg-primary text-primary-foreground' : 'hover:bg-muted')}>
              {tab === 'calor' ? 'Calor contract syntax' : 'C# guard clauses'}
            </button>
          ))}
        </div>
        <div className="mt-6 grid gap-6 lg:grid-cols-5">
          <div className="min-w-0 rounded-lg border bg-calor-navy lg:col-span-3">
            <div className="border-b border-white/10 px-4 py-2 text-sm text-calor-cyan font-mono">
              {activeTab === 'calor' ? 'program.calr' : 'Program.cs'} — fragment
            </div>
            <pre className="overflow-x-auto p-5 text-sm leading-7">
              <code className="font-mono text-calor-cyan">{activeTab === 'calor' ? calorCode : csharpCode}</code>
            </pre>
          </div>
          <div className="lg:col-span-2">
            <h3 className="font-semibold">What the code declares or checks</h3>
            <ul className="mt-3 list-disc space-y-3 pl-5 text-sm text-muted-foreground">
              {annotations[activeTab].map(text => <li key={text}>{text}</li>)}
            </ul>
          </div>
        </div>
        <p className="mt-6 text-sm text-muted-foreground">
          Runtime checks depend on contract mode and supported body shapes.
          Optional --verify asks Z3 to prove supported obligations and can remove
          eligible postcondition guards; --keep-proven-guards prevents that removal.
          These checks do not establish whole-program correctness. Read the{' '}
          <Link href="/docs/guides/verification-guarantees/" className="text-primary underline">
            verification guarantees and limits
          </Link>.
        </p>
      </div>
    </section>
  );
}
