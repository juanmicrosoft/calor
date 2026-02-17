'use client';

import Link from 'next/link';
import packageJson from '../../package.json';
import { trackAskCalorClick } from '@/lib/analytics';

const footerLinks = {
  documentation: [
    { name: 'Getting Started', href: '/docs/getting-started/' },
    { name: 'Syntax Reference', href: '/docs/syntax-reference/' },
    { name: 'CLI Reference', href: '/docs/cli/' },
  ],
  resources: [
    { name: 'Benchmarking', href: '/docs/benchmarking/' },
    { name: 'Philosophy', href: '/docs/philosophy/' },
    { name: 'Contributing', href: '/docs/contributing/' },
  ],
  community: [
    { name: 'GitHub', href: 'https://github.com/juanmicrosoft/calor', external: true },
    { name: 'Issues', href: 'https://github.com/juanmicrosoft/calor/issues', external: true },
    { name: 'Ask Calor', href: 'https://chatgpt.com/g/g-6994cc69517c8191a0dc7be0bfc00186-ask-calor', external: true },
  ],
};

export function Footer() {
  return (
    <footer className="border-t bg-muted/50">
      <div className="mx-auto max-w-7xl px-6 py-12 lg:px-8">
        <div className="grid grid-cols-2 gap-8 md:grid-cols-4">
          <div className="col-span-2 md:col-span-1">
            <Link href="/" className="text-xl font-bold">
              Calor
            </Link>
            <p className="mt-4 text-sm text-muted-foreground">
              Coding Agent Language for Optimized Reasoning. A language designed for AI coding agents.
            </p>
          </div>

          <div>
            <h3 className="text-sm font-semibold">Documentation</h3>
            <ul className="mt-4 space-y-2">
              {footerLinks.documentation.map((link) => (
                <li key={link.name}>
                  <Link
                    href={link.href}
                    className="text-sm text-muted-foreground hover:text-primary"
                  >
                    {link.name}
                  </Link>
                </li>
              ))}
            </ul>
          </div>

          <div>
            <h3 className="text-sm font-semibold">Resources</h3>
            <ul className="mt-4 space-y-2">
              {footerLinks.resources.map((link) => (
                <li key={link.name}>
                  <Link
                    href={link.href}
                    className="text-sm text-muted-foreground hover:text-primary"
                  >
                    {link.name}
                  </Link>
                </li>
              ))}
            </ul>
          </div>

          <div>
            <h3 className="text-sm font-semibold">Community</h3>
            <ul className="mt-4 space-y-2">
              {footerLinks.community.map((link) => (
                <li key={link.name}>
                  <a
                    href={link.href}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="text-sm text-muted-foreground hover:text-primary"
                    onClick={link.name === 'Ask Calor' ? () => trackAskCalorClick('footer') : undefined}
                  >
                    {link.name}
                  </a>
                </li>
              ))}
            </ul>
          </div>
        </div>

        <div className="mt-12 border-t pt-8 flex flex-col sm:flex-row justify-between items-center gap-2">
          <p className="text-sm text-muted-foreground">
            Calor is open source. Licensed under MIT.
          </p>
          <p className="text-sm text-muted-foreground">
            v{packageJson.version}
          </p>
        </div>
      </div>
    </footer>
  );
}
