'use client';

import { FileCode, Shield, Fingerprint, Layers, ChevronRight } from 'lucide-react';
import Link from 'next/link';
import { trackFeatureLearnMore } from '@/lib/analytics';
import { useScrollReveal } from '@/hooks/useScrollReveal';

const features = [
  {
    name: 'Rules That Enforce Themselves',
    description:
      'Define what your function should do—like "input must be positive." The compiler proves it, not your tests.',
    icon: Shield,
    code: '§Q (>= x 0)\n§S (>= result 0)',
    href: '/docs/philosophy/effects-contracts-enforcement/',
  },
  {
    name: 'No Hidden Side Effects',
    description:
      'Database calls? Network requests? The compiler tells you exactly where they happen—even buried 5 layers deep.',
    icon: FileCode,
    code: '§E{db:rw,net:rw}',
    href: '/docs/philosophy/effects-contracts-enforcement/',
  },
  {
    name: 'Rename Without Breaking',
    description:
      'Every function has a permanent ID. Rename files, move code around—AI agents still find exactly what they need.',
    icon: Fingerprint,
    code: '§F{f_01J5X7K9M2:Process:pub}',
    href: '/docs/philosophy/stable-identifiers/',
  },
  {
    name: 'No More Missing Braces',
    description:
      'Explicit start/end tags mean AI can\'t generate malformed code. No more "unexpected token" from bad indentation.',
    icon: Layers,
    code: '§M{m_01J5X7K9M2:App}\n  ...\n§/M{m_01J5X7K9M2}',
    href: '/docs/syntax-reference/',
  },
];

export function FeatureGrid() {
  const sectionRef = useScrollReveal<HTMLDivElement>();
  const gridRef = useScrollReveal<HTMLDivElement>({ staggerChildren: true, staggerDelay: 120 });

  return (
    <section className="relative py-24 overflow-hidden">
      {/* Gradient mesh */}
      <div className="gradient-mesh gradient-mesh-pink absolute top-10 right-0 w-[500px] h-[500px] -z-10" />
      <div className="gradient-mesh gradient-mesh-salmon absolute bottom-10 left-10 w-[350px] h-[350px] -z-10" />

      <div className="mx-auto max-w-7xl px-6 lg:px-8">
        <div className="mx-auto max-w-2xl text-center" ref={sectionRef}>
          <h2 className="text-3xl font-bold tracking-tight sm:text-4xl">
            Built for How AI Actually Writes Code
          </h2>
          <p className="mt-4 text-lg text-muted-foreground font-body">
            Four features that make AI-generated code reliable
          </p>
        </div>

        <div className="mt-16 grid gap-6 sm:grid-cols-2 lg:grid-cols-4" ref={gridRef}>
          {features.map((feature) => {
            const Icon = feature.icon;
            return (
              <div
                key={feature.name}
                className="gradient-border relative rounded-xl border bg-background p-6 hover:shadow-xl transition-all duration-300"
              >
                <div className="flex h-11 w-11 items-center justify-center rounded-lg bg-gradient-to-br from-calor-pink/10 to-calor-cyan/10 ring-1 ring-calor-pink/10">
                  <Icon className="h-5 w-5 text-calor-pink" />
                </div>
                <h3 className="mt-4 font-semibold font-display text-lg">{feature.name}</h3>
                <p className="mt-2 text-sm text-muted-foreground font-body leading-relaxed">
                  {feature.description}
                </p>
                <div className="mt-4 rounded-lg bg-calor-navy p-3 shadow-inner">
                  <code className="text-xs text-calor-cyan whitespace-pre font-mono">
                    {feature.code}
                  </code>
                </div>
                {'href' in feature && feature.href && (
                  <Link
                    href={feature.href}
                    className="mt-4 inline-flex items-center text-sm text-calor-pink hover:text-calor-salmon transition-colors font-body"
                    onClick={() => trackFeatureLearnMore(feature.name)}
                  >
                    Learn more
                    <ChevronRight className="ml-1 h-4 w-4" />
                  </Link>
                )}
              </div>
            );
          })}
        </div>
      </div>
    </section>
  );
}
