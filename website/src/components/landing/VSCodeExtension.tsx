'use client';

import { useState } from 'react';
import { Check, Copy, FileCode, Palette, Settings, ExternalLink } from 'lucide-react';
import { cn } from '@/lib/utils';
import { trackVSCodeExtensionClick, trackOutboundLink } from '@/lib/analytics';

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
    trackVSCodeExtensionClick('copy_command');
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
            Editor support, with release availability stated plainly
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

            <div className="mt-4 rounded-lg border border-amber-500/30 bg-amber-500/5 p-4 text-sm text-muted-foreground">
              <strong className="text-foreground">Release status:</strong> the Marketplace currently carries
              v0.3.8, which has a known activation defect. v0.12.1 packages build successfully, but publishing
              is still blocked by an expired Marketplace token. Use the repository build instructions when you
              need the current extension.
            </div>

            {/* Marketplace Button */}
            <a
              href={MARKETPLACE_URL}
              target="_blank"
              rel="noopener noreferrer"
              onClick={() => { trackVSCodeExtensionClick('marketplace'); trackOutboundLink(MARKETPLACE_URL); }}
              className="mt-6 flex items-center justify-center gap-2 rounded-lg bg-calor-navy px-6 py-3 font-medium text-white hover:bg-calor-navy/90 transition-colors"
            >
              <ExternalLink className="h-4 w-4" />
              View legacy Marketplace listing
            </a>

            {/* Quick Install Command */}
            <div className="mt-6">
              <span className="text-xs text-muted-foreground uppercase tracking-wider">
                Legacy Marketplace install (v0.3.8)
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

            <a
              href="https://github.com/juanmicrosoft/calor/tree/main/editors/vscode"
              target="_blank"
              rel="noopener noreferrer"
              onClick={() => trackOutboundLink('https://github.com/juanmicrosoft/calor/tree/main/editors/vscode')}
              className="mt-4 flex items-center justify-center gap-2 rounded-lg border px-6 py-3 font-medium hover:bg-muted transition-colors"
            >
              <ExternalLink className="h-4 w-4" />
              Build the current extension
            </a>
          </div>
        </div>
      </div>
    </section>
  );
}
