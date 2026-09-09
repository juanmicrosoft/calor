'use client';

import Link from 'next/link';
import Image from 'next/image';
import { useEffect, useRef, useState } from 'react';
import { Button } from '@/components/ui/button';
import { Github, ArrowRight } from 'lucide-react';
import { getBasePath } from '@/lib/utils';
import { trackCtaClick, trackOutboundLink } from '@/lib/analytics';

const basePath = getBasePath();

export function Hero() {
  const heroRef = useRef<HTMLDivElement>(null);
  const [allowVideo, setAllowVideo] = useState(false);
  const [playing, setPlaying] = useState(false);

  useEffect(() => {
    const motion = window.matchMedia('(prefers-reduced-motion: reduce)');
    const desktop = window.matchMedia('(min-width: 768px)');
    const connection = (navigator as Navigator & {
      connection?: EventTarget & { saveData?: boolean; effectiveType?: string };
    }).connection;
    const update = () => {
      const allowed = !motion.matches && desktop.matches && !connection?.saveData
        && !['slow-2g', '2g', '3g'].includes(connection?.effectiveType || '');
      setAllowVideo(allowed);
      if (!allowed) setPlaying(false);
    };
    update();
    motion.addEventListener('change', update);
    desktop.addEventListener('change', update);
    connection?.addEventListener('change', update);
    return () => {
      motion.removeEventListener('change', update);
      desktop.removeEventListener('change', update);
      connection?.removeEventListener('change', update);
    };
  }, []);

  useEffect(() => {
    const el = heroRef.current;
    if (!el) return;
    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;

    // Stagger hero elements on load
    const children = el.querySelectorAll('[data-hero-animate]');
    children.forEach((child, i) => {
      const htmlChild = child as HTMLElement;
      htmlChild.style.opacity = '0';
      htmlChild.style.transform = 'translateY(24px)';
      htmlChild.style.transition = 'opacity 0.7s ease-out, transform 0.7s ease-out';
      htmlChild.style.transitionDelay = `${200 + i * 150}ms`;
      requestAnimationFrame(() => {
        htmlChild.style.opacity = '1';
        htmlChild.style.transform = 'translateY(0)';
      });
    });
  }, []);

  return (
    <section className="relative overflow-hidden py-28 sm:py-36 lg:py-44">
      {/* Poster is the default: no decorative video request before explicit opt-in. */}
        <div
          className="absolute inset-0 w-full h-full bg-cover bg-center -z-20"
          style={{ backgroundImage: `url(${basePath}/calor-lava-poster.jpg)` }}
          aria-hidden="true"
        />
      {allowVideo && playing && (
        <video
          autoPlay
          loop
          muted
          playsInline
          preload="none"
          className="absolute inset-0 w-full h-full object-cover -z-20"
          poster={`${basePath}/calor-lava-poster.jpg`}
          aria-hidden="true"
        >
          <source src={`${basePath}/calor-lava.mp4`} type="video/mp4" />
        </video>
      )}

      {/* Gradient overlay — navy at top/bottom, transparent center */}
      <div className="absolute inset-0 -z-10 bg-gradient-to-b from-calor-navy/80 via-calor-navy/20 to-calor-navy/90" />

      {/* Radial glow behind logo */}
      <div className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 -z-10 w-[600px] h-[600px] rounded-full"
        style={{
          background: 'radial-gradient(circle, rgba(250, 61, 111, 0.25) 0%, rgba(255, 142, 119, 0.1) 40%, transparent 70%)',
        }}
      />

      <div className="mx-auto max-w-7xl px-6 lg:px-8" ref={heroRef}>
        <div className="mx-auto max-w-3xl text-center">
          {/* Frosted glass card */}
          <div className="rounded-2xl bg-white/5 backdrop-blur-xl border border-white/10 px-8 py-12 sm:px-12 sm:py-16 shadow-2xl">
            <div className="flex justify-center mb-8" data-hero-animate>
              <Image
                src={`${basePath}/calor-logo-256.webp`}
                alt="Calor logo"
                width={120}
                height={120}
                className="h-24 w-24 sm:h-32 sm:w-32 drop-shadow-[0_0_30px_rgba(250,61,111,0.4)]"
                priority
              />
            </div>
            <h1 className="text-5xl font-bold tracking-tight text-white sm:text-7xl font-display" data-hero-animate>
              Calor
            </h1>
            <p className="mt-4 text-xl font-medium text-white/90 sm:text-2xl font-body" data-hero-animate>
              A programming language for coding agents
            </p>
            <p className="mt-6 text-lg leading-8 text-white/60 font-body" data-hero-animate>
              Fewer errors. Better refactors. Cleaner merges.
            </p>

            <div className="mt-10 flex items-center justify-center gap-x-4" data-hero-animate>
              <Button asChild size="lg" className="bg-gradient-to-r from-calor-pink to-calor-salmon hover:from-calor-pink/90 hover:to-calor-salmon/90 text-white border-0 shadow-lg shadow-calor-pink/25">
                <Link href="/docs/getting-started/" onClick={() => trackCtaClick('get_started')}>
                  Get Started
                  <ArrowRight className="ml-2 h-4 w-4" />
                </Link>
              </Button>
              <Button variant="outline" size="lg" asChild className="bg-white/10 border-white/20 text-white hover:bg-white/20 backdrop-blur-sm">
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
      </div>

      {allowVideo && (
        <button type="button" onClick={() => setPlaying(!playing)}
          className="absolute bottom-20 right-6 z-20 rounded border border-white/30 bg-calor-navy/90 px-3 py-2 text-xs text-white">
          {playing ? 'Pause background animation' : 'Play background animation'}
        </button>
      )}

      {/* Shaped bottom divider */}
      <div className="hero-divider">
        <svg viewBox="0 0 1440 80" preserveAspectRatio="none" className="w-full h-full">
          <path
            d="M0,40 C360,80 720,0 1080,40 C1260,60 1380,50 1440,40 L1440,80 L0,80 Z"
            className="fill-background"
          />
        </svg>
      </div>
    </section>
  );
}
