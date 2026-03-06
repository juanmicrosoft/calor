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
    <footer className="relative bg-calor-navy text-white">
      {/* Heat gradient edge at top */}
      <div className="absolute top-0 left-0 right-0 h-px bg-gradient-to-r from-transparent via-calor-pink to-transparent" />
      <div className="absolute top-0 left-0 right-0 h-8 bg-gradient-to-b from-calor-pink/10 to-transparent pointer-events-none" />

      <div className="mx-auto max-w-7xl px-6 py-14 lg:px-8">
        <div className="grid grid-cols-2 gap-8 md:grid-cols-4">
          <div className="col-span-2 md:col-span-1">
            <Link href="/" className="text-xl font-bold font-display">
              Calor
            </Link>
            <p className="mt-4 text-sm text-white/50 font-body leading-relaxed">
              Coding Agent Language for Optimized Reasoning. A language designed for AI coding agents.
            </p>
          </div>

          <div>
            <h3 className="text-sm font-semibold text-calor-cyan font-body">Documentation</h3>
            <ul className="mt-4 space-y-2">
              {footerLinks.documentation.map((link) => (
                <li key={link.name}>
                  <Link
                    href={link.href}
                    className="text-sm text-white/50 hover:text-white transition-colors font-body"
                  >
                    {link.name}
                  </Link>
                </li>
              ))}
            </ul>
          </div>

          <div>
            <h3 className="text-sm font-semibold text-calor-cyan font-body">Resources</h3>
            <ul className="mt-4 space-y-2">
              {footerLinks.resources.map((link) => (
                <li key={link.name}>
                  <Link
                    href={link.href}
                    className="text-sm text-white/50 hover:text-white transition-colors font-body"
                  >
                    {link.name}
                  </Link>
                </li>
              ))}
            </ul>
          </div>

          <div>
            <h3 className="text-sm font-semibold text-calor-cyan font-body">Community</h3>
            <ul className="mt-4 space-y-2">
              {footerLinks.community.map((link) => (
                <li key={link.name}>
                  <a
                    href={link.href}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="text-sm text-white/50 hover:text-white transition-colors font-body"
                    onClick={link.name === 'Ask Calor' ? () => trackAskCalorClick('footer') : undefined}
                  >
                    {link.name}
                  </a>
                </li>
              ))}
            </ul>
          </div>
        </div>

        <div className="mt-12 border-t border-white/10 pt-8 flex flex-col sm:flex-row justify-between items-center gap-2">
          <p className="text-sm text-white/40 font-body">
            Calor is open source. Licensed under Apache 2.0.
          </p>
          <p className="text-sm text-white/40 font-mono">
            v{packageJson.version}
          </p>
        </div>
      </div>
    </footer>
  );
}
