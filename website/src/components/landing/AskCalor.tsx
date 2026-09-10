'use client';

import Link from 'next/link';
import { trackAskCalorClick } from '@/lib/analytics';

export function AskCalor() {
  return (
    <section className="border-t py-12" aria-labelledby="resources-heading">
      <div className="mx-auto max-w-5xl px-6 lg:px-8">
        <h2 id="resources-heading" className="text-2xl font-bold">Reference and help</h2>
        <ul className="mt-4 space-y-3 text-sm text-muted-foreground">
          <li>
            <Link href="/docs/" className="text-primary underline">Search the documentation</Link>
            {' '}— local, account-free syntax, commands, and diagnostic reference.
          </li>
          <li>
            <Link href="/docs/philosophy/stable-identifiers/" className="text-primary underline">Stable IDs</Link>
            {' '}and{' '}
            <Link href="/docs/guides/project-intelligence-for-agents/" className="text-primary underline">project queries</Link>
            {' '}— references for targeted edits and inspecting the compiler model.
          </li>
          <li>
            <a href="https://chatgpt.com/g/g-6994cc69517c8191a0dc7be0bfc00186-ask-calor"
              target="_blank" rel="noopener noreferrer" className="text-primary underline"
              onClick={() => trackAskCalorClick('homepage')}>
              Ask Calor on ChatGPT (external)
            </a>
            {' '}— opens an external service and may require a ChatGPT account or plan.
            It is not documentation search.
          </li>
        </ul>
      </div>
    </section>
  );
}
