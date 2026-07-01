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
            <span className="font-semibold text-calor-cerulean">v0.6.7</span>
            <span className="text-muted-foreground mx-1.5">&mdash;</span>
            <span className="text-foreground">Two new compile-time diagnostics catch non-compiling Calor earlier: <code>Calor0116</code> rejects malformed four-field function headers, and <code>Calor0205</code> flags a value returned from a <code>void</code>, iterator, constructor, setter, or event accessor &mdash; together closing the &ldquo;value returned from void&rdquo; gap. Every agent-readable doc surface was also swept to indent-only syntax, guarded by a compile-time test.</span>
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
