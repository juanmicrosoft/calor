'use client';

import { useState } from 'react';
import { Check, Copy, FileCode, Palette, Settings, ExternalLink } from 'lucide-react';
import { cn } from '@/lib/utils';

const features = [
  {
    name: 'Syntax Highlighting',
    description: 'Full syntax highlighting for .calr files with support for contracts, effects, and identifiers.',
    icon: Palette,
  },
  {
    name: 'Custom File Icons',
    description: 'Distinctive file icons in the explorer to easily identify Calor source files.',
    icon: FileCode,
  },
  {
    name: 'Language Configuration',
    description: 'Auto-closing brackets, comment toggling, and other editor conveniences.',
    icon: Settings,
  },
];

const MARKETPLACE_URL = 'https://marketplace.visualstudio.com/items?itemName=calor-dev.calor';
const INSTALL_COMMAND = 'ext install calor-dev.calor';

export function VSCodeExtension() {
  const [copied, setCopied] = useState(false);

  const copyToClipboard = async () => {
    await navigator.clipboard.writeText(INSTALL_COMMAND);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  return (
    <section className="py-24">
      <div className="mx-auto max-w-7xl px-6 lg:px-8">
        <div className="mx-auto max-w-2xl text-center">
          <h2 className="text-3xl font-bold tracking-tight sm:text-4xl">
            VS Code Extension
          </h2>
          <p className="mt-4 text-lg text-muted-foreground">
            First-class editor support for Calor development
          </p>
        </div>

        <div className="mt-16 grid gap-8 lg:grid-cols-2">
          {/* Features */}
          <div className="space-y-6">
            {features.map((feature) => {
              const Icon = feature.icon;
              return (
                <div
                  key={feature.name}
                  className="flex gap-4 rounded-lg border bg-background p-4 hover:border-calor-cyan transition-colors"
                >
                  <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-gradient-to-br from-calor-navy/10 to-calor-cyan/10">
                    <Icon className="h-5 w-5 text-calor-navy" />
                  </div>
                  <div>
                    <h3 className="font-semibold">{feature.name}</h3>
                    <p className="mt-1 text-sm text-muted-foreground">
                      {feature.description}
                    </p>
                  </div>
                </div>
              );
            })}
          </div>

          {/* Install Card */}
          <div className="rounded-lg border bg-background p-6">
            <h3 className="text-xl font-semibold">Get Started</h3>
            <p className="mt-2 text-muted-foreground">
              Install the Calor Language extension to enable syntax highlighting and editor features for <code className="text-calor-cyan">.calr</code> files.
            </p>

            {/* Marketplace Button */}
            <a
              href={MARKETPLACE_URL}
              target="_blank"
              rel="noopener noreferrer"
              className="mt-6 flex items-center justify-center gap-2 rounded-lg bg-calor-navy px-6 py-3 font-medium text-white hover:bg-calor-navy/90 transition-colors"
            >
              <ExternalLink className="h-4 w-4" />
              View on VS Code Marketplace
            </a>

            {/* Quick Install Command */}
            <div className="mt-6">
              <span className="text-xs text-muted-foreground uppercase tracking-wider">
                Or install via command palette
              </span>
              <div className="mt-2 flex items-center justify-between rounded-lg border bg-zinc-950 px-4 py-3">
                <code className="text-sm text-zinc-100 font-mono">
                  <span className="text-calor-pink">&gt;</span> {INSTALL_COMMAND}
                </code>
                <button
                  onClick={copyToClipboard}
                  className={cn(
                    'shrink-0 flex items-center gap-1 rounded px-2 py-1 text-xs transition-colors',
                    copied
                      ? 'text-green-400'
                      : 'text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800'
                  )}
                >
                  {copied ? (
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
          </div>
        </div>
      </div>
    </section>
  );
}
