'use client';

import Link from 'next/link';
import Image from 'next/image';
import { Button } from '@/components/ui/button';
import { Github, ArrowRight } from 'lucide-react';
import { getBasePath } from '@/lib/utils';
import { trackCtaClick, trackOutboundLink } from '@/lib/analytics';

const basePath = getBasePath();

export function Hero() {
  return (
    <section className="relative overflow-hidden py-24 sm:py-32">
      {/* Video background */}
      <video
        autoPlay
        loop
        muted
        playsInline
        className="absolute inset-0 w-full h-full object-cover -z-20"
      >
        <source src={`${basePath}/calor-back-long.mp4`} type="video/mp4" />
      </video>
      {/* Dark overlay for text contrast */}
      <div className="absolute inset-0 bg-black/50 -z-10" />

      <div className="mx-auto max-w-7xl px-6 lg:px-8">
        <div className="mx-auto max-w-3xl text-center">
          <div className="flex justify-center mb-6">
            <Image
              src={`${basePath}/calor-logo.png`}
              alt="Calor logo"
              width={120}
              height={120}
              className="h-24 w-24 sm:h-32 sm:w-32 drop-shadow-lg"
              priority
            />
          </div>
          <h1 className="text-4xl font-bold tracking-tight text-white sm:text-6xl">
            Calor
          </h1>
          <p className="mt-4 text-xl font-medium text-white sm:text-2xl">
            When AI writes your code, the language should catch the bugs.
          </p>
          <p className="mt-6 text-lg leading-8 text-white/80">
            Contracts verified by Z3, effects enforced by the compiler, semantic bugs caught before runtime.
          </p>

          <div className="mt-10 flex items-center justify-center gap-x-4">
            <Button asChild size="lg" className="bg-calor-pink hover:bg-calor-pink/90 text-white">
              <Link href="/docs/getting-started/" onClick={() => trackCtaClick('get_started')}>
                Get Started
                <ArrowRight className="ml-2 h-4 w-4" />
              </Link>
            </Button>
            <Button variant="outline" size="lg" asChild className="bg-white border-white text-black hover:bg-white/90">
              <a
                href="https://github.com/juanmicrosoft/calor"
                target="_blank"
                rel="noopener noreferrer"
                onClick={() => { trackCtaClick('github'); trackOutboundLink('https://github.com/juanmicrosoft/calor'); }}
              >
                <Github className="mr-2 h-4 w-4" />
                GitHub
              </a>
            </Button>
          </div>
        </div>
      </div>

    </section>
  );
}
