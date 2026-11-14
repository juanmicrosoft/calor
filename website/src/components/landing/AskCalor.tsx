'use client';

import Link from 'next/link';
import { trackAskCalorClick } from '@/lib/analytics';

export function AskCalor() {
  return (
    <section className="border-t py-12" aria-labelledby="resources-heading">
      <div className="mx-auto max-w-5xl px-6 lg:px-8">
        <h2 id="resources-heading" className="text-2xl font-bold">Keep exploring</h2>
        <ul className="mt-4 space-y-3 text-sm text-muted-foreground">
          <li>
            <Link href="/docs/" className="text-primary underline">Browse all guides and resources</Link>
            {' '}— start with a plain-language overview, then choose a topic.
          </li>
          <li>
            <Link href="/docs/syntax-reference/" className="text-primary underline">Language reference for developers and coding agents</Link>
            {' '}— detailed notation and commands, when you need them.
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
