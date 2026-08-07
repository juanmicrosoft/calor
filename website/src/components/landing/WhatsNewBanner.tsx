'use client';

import Link from 'next/link';
import { useState } from 'react';
import { X, Sparkles } from 'lucide-react';

export function WhatsNewBanner() {
  const [dismissed, setDismissed] = useState(false);

  if (dismissed) return null;

  return (
    <div className="relative bg-gradient-to-r from-calor-cerulean/10 via-calor-cyan/10 to-calor-cerulean/10 border-b">
      <div className="mx-auto max-w-7xl px-4 py-2.5 sm:px-6 lg:px-8">
        <div className="flex items-center justify-center gap-3 text-sm">
          <Sparkles className="h-4 w-4 text-calor-cerulean flex-shrink-0" />
          <p className="text-center">
            <span className="font-semibold text-calor-cerulean">v0.12.1</span>
            <span className="text-muted-foreground mx-1.5">&mdash;</span>
            <span className="text-foreground">The soundness release of v0.12.0 is now actually installable. Both publish channels were broken — the extension had failed to publish for three consecutive releases — so <code>calor</code> is back on NuGet and the VS Code extension is back on the marketplace. Along the way: the shipped Z3 wrapper is now upstream&apos;s Release build rather than a Debug artifact, it loads on ARM64 again, and CI exercises both release-only paths so neither failure can reach release day unseen.</span>
            <Link
              href="/docs/changelog/"
              className="ml-2 font-medium text-calor-cerulean hover:text-calor-cerulean/80 underline underline-offset-4"
            >
              See what&apos;s new
            </Link>
          </p>
          <button
            onClick={() => setDismissed(true)}
            className="ml-2 flex-shrink-0 rounded-full p-1 hover:bg-muted transition-colors"
            aria-label="Dismiss banner"
          >
            <X className="h-3.5 w-3.5 text-muted-foreground" />
          </button>
        </div>
      </div>
    </div>
  );
}
