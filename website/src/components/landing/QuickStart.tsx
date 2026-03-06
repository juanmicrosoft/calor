'use client';

import { useState } from 'react';
import { Check, Copy } from 'lucide-react';
import { cn } from '@/lib/utils';
import { trackInstallCommandCopy } from '@/lib/analytics';
import { useScrollReveal } from '@/hooks/useScrollReveal';

const commands = [
  {
    label: 'Install Calor',
    command: 'dotnet tool install -g calor',
    description: 'One command. Works on Windows, Mac, and Linux. Requires .NET 10+.',
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
  const sectionRef = useScrollReveal<HTMLDivElement>();
  const terminalRef = useScrollReveal<HTMLDivElement>();

  const copyToClipboard = async (text: string, index: number) => {
    await navigator.clipboard.writeText(text.replace(/\\\n/g, ''));
    trackInstallCommandCopy(commands[index].label);
    setCopiedIndex(index);
    setTimeout(() => setCopiedIndex(null), 2000);
  };

  return (
    <section className="relative py-24 overflow-hidden">
      <div className="gradient-mesh gradient-mesh-pink absolute bottom-0 right-0 w-[400px] h-[400px] -z-10" />

      <div className="mx-auto max-w-7xl px-6 lg:px-8">
        <div className="mx-auto max-w-2xl text-center" ref={sectionRef}>
          <h2 className="text-3xl font-bold tracking-tight sm:text-4xl">
            Try It Now
          </h2>
          <p className="mt-4 text-lg text-muted-foreground font-body">
            Three commands to start writing safer code with your AI agent
          </p>
        </div>

        <div className="mt-12 mx-auto max-w-3xl" ref={terminalRef}>
          <div className="relative rounded-xl overflow-hidden shadow-2xl shadow-calor-navy/30">
            {/* CRT container */}
            <div className="bg-calor-navy crt-curve">
              {/* Terminal header */}
              <div className="flex items-center gap-2 border-b border-white/10 px-4 py-3">
                <div className="flex gap-1.5">
                  <div className="w-3 h-3 rounded-full bg-calor-pink" />
                  <div className="w-3 h-3 rounded-full bg-calor-salmon" />
                  <div className="w-3 h-3 rounded-full bg-calor-cyan" />
                </div>
                <div className="flex items-center gap-2 ml-4 text-calor-cyan/50 text-sm font-terminal">
                  <span>calor-terminal</span>
                </div>
              </div>

              {/* Commands */}
              <div className="relative divide-y divide-white/5">
                {commands.map((cmd, index) => (
                  <div key={index} className="relative group">
                    <div className="flex items-start justify-between p-4 sm:p-5">
                      <div className="space-y-2">
                        <span className="text-xs text-calor-cyan/40 uppercase tracking-widest font-terminal">
                          {cmd.label}
                        </span>
                        <pre className="text-sm sm:text-base text-white font-terminal whitespace-pre-wrap leading-relaxed">
                          <span className="terminal-glow text-calor-cyan">$</span> {cmd.command}
                        </pre>
                        <p className="text-xs text-white/30 font-body">{cmd.description}</p>
                      </div>
                      <button
                        onClick={() => copyToClipboard(cmd.command, index)}
                        className={cn(
                          'shrink-0 flex items-center gap-1 rounded px-2 py-1 text-xs transition-colors font-body',
                          copiedIndex === index
                            ? 'text-calor-cyan'
                            : 'text-white/30 hover:text-white/70 hover:bg-white/5'
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

                {/* CRT scanline overlay */}
                <div className="crt-scanlines absolute inset-0 rounded-b-xl" />
              </div>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
