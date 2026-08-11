'use client';

import { FileCode, Palette, Settings, ExternalLink } from 'lucide-react';
import { trackVSCodeExtensionClick, trackOutboundLink } from '@/lib/analytics';

const features = [
  {
    name: 'Language Server',
    description: 'Diagnostics, navigation, references, and rename support through the bundled Calor LSP.',
    icon: FileCode,
  },
  {
    name: 'Syntax Highlighting',
    description: 'Full syntax highlighting for .calr files with support for contracts, effects, and identifiers.',
    icon: Palette,
  },
  {
    name: 'Language Configuration',
    description: 'Auto-closing brackets, comment toggling, and other editor conveniences.',
    icon: Settings,
  },
];

const RELEASES_URL = 'https://github.com/juanmicrosoft/calor/releases';
const MARKETPLACE_URL = 'https://marketplace.visualstudio.com/items?itemName=calor-dev.calor';

export function VSCodeExtension() {
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
              v0.3.8, which has a known activation defect. This is deliberate: Marketplace publishing is
              opportunistic and happens only if a token is minted. Current, supported platform VSIX packages
              are attached to GitHub releases.
            </div>

            <a
              href={RELEASES_URL}
              target="_blank"
              rel="noopener noreferrer"
              onClick={() => { trackVSCodeExtensionClick('release'); trackOutboundLink(RELEASES_URL); }}
              className="mt-6 flex items-center justify-center gap-2 rounded-lg bg-calor-navy px-6 py-3 font-medium text-white hover:bg-calor-navy/90 transition-colors"
            >
              <ExternalLink className="h-4 w-4" />
              Download the release VSIX
            </a>

            <a
              href={MARKETPLACE_URL}
              target="_blank"
              rel="noopener noreferrer"
              onClick={() => { trackVSCodeExtensionClick('marketplace'); trackOutboundLink(MARKETPLACE_URL); }}
              className="mt-4 flex items-center justify-center gap-2 rounded-lg border px-6 py-3 font-medium hover:bg-muted transition-colors"
            >
              <ExternalLink className="h-4 w-4" />
              View legacy Marketplace listing
            </a>
          </div>
        </div>
      </div>
    </section>
  );
}
