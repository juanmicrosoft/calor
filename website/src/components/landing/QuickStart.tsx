'use client';

import { useState } from 'react';
import { Check, Copy } from 'lucide-react';
import { cn } from '@/lib/utils';
import { trackInstallCommandCopy } from '@/lib/analytics';
import Link from 'next/link';

const commands = [
  {
    label: 'Install Calor',
    command: 'dotnet tool install -g calor',
    description: 'One command. Works on Windows, Mac, and Linux. Requires .NET 10+.',
  },
  {
    label: 'Enable Calor in your project',
    command: 'calor init',
    description: 'Adds MSBuild integration. Connecting an AI agent is optional.',
  },
  {
    label: 'Build and check',
    command: 'dotnet build',
    description: 'Compiles the Calor sources in this project to C# and builds the application.',
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
    <section className="py-16">
      <div className="mx-auto max-w-7xl px-6 lg:px-8">
        <div className="mx-auto max-w-3xl">
          <h2 className="text-3xl font-bold tracking-tight sm:text-4xl">
            Use an existing .NET project
          </h2>
          <p className="mt-3 text-muted-foreground font-body">
            Requires the .NET 10 SDK. Run these commands in a directory containing
            your <code>.csproj</code> and Calor source files; keep your existing entry point.
            {' '}Starting from scratch?{' '}
            <Link className="text-primary underline" href="/docs/getting-started/hello-world/">
              Run the complete Hello World example
            </Link>{' '}without an AI subscription.
          </p>
        </div>

        <div className="mt-6 mx-auto max-w-3xl">
          <div className="rounded-lg overflow-hidden border">
            <div className="bg-calor-navy" role="region" aria-label="Existing-project commands">
              {/* Terminal header */}
              <div className="flex items-center gap-2 border-b border-white/10 px-4 py-3">
                <div className="flex gap-1.5">
                  <div className="w-3 h-3 rounded-full bg-calor-pink" />
                  <div className="w-3 h-3 rounded-full bg-calor-salmon" />
                  <div className="w-3 h-3 rounded-full bg-calor-cyan" />
                </div>
                <div className="flex items-center gap-2 ml-4 text-calor-cyan text-sm font-terminal">
                  <span>calor-terminal</span>
                </div>
              </div>

              {/* Commands */}
              <div className="relative divide-y divide-white/5">
                {commands.map((cmd, index) => (
                  <div key={index} className="relative group">
                    <div className="flex items-start justify-between p-4 sm:p-5">
                      <div className="space-y-2">
                        <span className="text-xs text-calor-cyan uppercase tracking-widest font-terminal">
                          {cmd.label}
                        </span>
                        <pre className="text-sm sm:text-base text-white font-terminal whitespace-pre-wrap leading-relaxed">
                          <span className="text-calor-cyan">$</span> {cmd.command}
                        </pre>
                        <p className="text-xs text-white/80 font-body">{cmd.description}</p>
                      </div>
                      <button
                        onClick={() => copyToClipboard(cmd.command, index)}
                        className={cn(
                          'shrink-0 flex items-center gap-1 rounded px-2 py-1 text-xs transition-colors font-body',
                          copiedIndex === index
                            ? 'text-calor-cyan'
                            : 'text-white/80 hover:text-white hover:bg-white/5'
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
      </div>
    </section>
  );
}
