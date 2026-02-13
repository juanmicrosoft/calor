'use client';

import { useState } from 'react';
import { Check, Copy, Terminal } from 'lucide-react';
import { cn } from '@/lib/utils';
import { trackInstallCommandCopy } from '@/lib/analytics';

const commands = [
  {
    label: 'Install Calor',
    command: 'dotnet tool install -g calor',
    description: 'One command. Works on Windows, Mac, and Linux. Requires .NET 8+.',
  },
  {
    label: 'Set up your AI agent',
    command: 'calor init --ai claude',
    description: 'Teaches Claude Code the Calor syntax so it can start writing code.',
  },
  {
    label: 'Build and check',
    command: 'dotnet build',
    description: 'Compiles your code and catches bugs—before you run anything.',
  },
];

export function QuickStart() {
  const [copiedIndex, setCopiedIndex] = useState<number | null>(null);

  const copyToClipboard = async (text: string, index: number) => {
    await navigator.clipboard.writeText(text.replace(/\\\n/g, ''));
    trackInstallCommandCopy(commands[index].label);
    setCopiedIndex(index);
    setTimeout(() => setCopiedIndex(null), 2000);
  };

  return (
    <section className="py-24">
      <div className="mx-auto max-w-7xl px-6 lg:px-8">
        <div className="mx-auto max-w-2xl text-center">
          <h2 className="text-3xl font-bold tracking-tight sm:text-4xl">
            Try It Now
          </h2>
          <p className="mt-4 text-lg text-muted-foreground">
            Three commands to start writing safer code with your AI agent
          </p>
        </div>

        <div className="mt-12 mx-auto max-w-3xl">
          <div className="rounded-lg border bg-zinc-950 overflow-hidden">
            {/* Terminal header */}
            <div className="flex items-center gap-2 border-b border-zinc-800 px-4 py-3">
              <div className="flex gap-1.5">
                <div className="w-3 h-3 rounded-full bg-red-500" />
                <div className="w-3 h-3 rounded-full bg-yellow-500" />
                <div className="w-3 h-3 rounded-full bg-green-500" />
              </div>
              <div className="flex items-center gap-2 ml-4 text-zinc-400 text-sm">
                <Terminal className="h-4 w-4" />
                <span>Terminal</span>
              </div>
            </div>

            {/* Commands */}
            <div className="divide-y divide-zinc-800">
              {commands.map((cmd, index) => (
                <div key={index} className="relative group">
                  <div className="flex items-start justify-between p-4">
                    <div className="space-y-2">
                      <span className="text-xs text-zinc-500 uppercase tracking-wider">
                        {cmd.label}
                      </span>
                      <pre className="text-sm text-zinc-100 font-mono whitespace-pre-wrap">
                        <span className="text-green-400">$</span> {cmd.command}
                      </pre>
                      <p className="text-xs text-zinc-500">{cmd.description}</p>
                    </div>
                    <button
                      onClick={() => copyToClipboard(cmd.command, index)}
                      className={cn(
                        'shrink-0 flex items-center gap-1 rounded px-2 py-1 text-xs transition-colors',
                        copiedIndex === index
                          ? 'text-green-400'
                          : 'text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800'
                      )}
                    >
                      {copiedIndex === index ? (
                        <>
                          <Check className="h-3.5 w-3.5" />
                          Copied
                        </>
                      ) : (
                        <>
                          <Copy className="h-3.5 w-3.5" />
                          Copy
                        </>
                      )}
                    </button>
                  </div>
                </div>
              ))}
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
