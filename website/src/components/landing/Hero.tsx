'use client';

import Link from 'next/link';
import Image from 'next/image';
import { useEffect, useRef, useState } from 'react';
import { Button } from '@/components/ui/button';
import { Github, ArrowRight, Pause, Play } from 'lucide-react';
import { getBasePath } from '@/lib/utils';
import { trackCtaClick, trackOutboundLink } from '@/lib/analytics';

const basePath = getBasePath();

export function Hero() {
  const heroRef = useRef<HTMLDivElement>(null);
  const videoRef = useRef<HTMLVideoElement>(null);
  const [allowVideo, setAllowVideo] = useState(false);
  const [playing, setPlaying] = useState(false);
  // Records a DELIBERATE pause, so that a later media-query change — resizing back
  // across the md breakpoint, a connection event — re-enables the control without
  // restarting a video the visitor chose to stop.
  const userChosePlayback = useRef(false);

  useEffect(() => {
    const video = videoRef.current;
    if (!video) return;
    // The `autoPlay` attribute is not a guarantee, so playback is also started
    // explicitly. Measured in Chrome: with the tab rendered but NOT focused
    // (document.hasFocus() === false), the attribute left the video at
    // `paused: true, currentTime: 0` even at readyState 4 — fully buffered and
    // simply never started — while an explicit play() on the same element
    // resolved without rejection. That is the cmd-click / open-in-background-tab
    // / session-restore path, and it is the same failure shape as the hero
    // stagger documented in globals.css: the decoration silently never runs for
    // whoever did not arrive by a focused top-level navigation.
    //
    // Retrying on `canplay` and `visibilitychange` covers the element mounting
    // before it has data, and the background tab that is only looked at later.
    // The element exists only while `playing` is true, so this can never fight a
    // deliberate pause — pausing unmounts it.
    const start = () => { void video.play().catch(() => {}); };
    start();
    video.addEventListener('canplay', start);
    document.addEventListener('visibilitychange', start);
    return () => {
      video.removeEventListener('canplay', start);
      document.removeEventListener('visibilitychange', start);
      video.pause();
      video.removeAttribute('src');
      video.querySelectorAll('source').forEach(source => source.removeAttribute('src'));
      video.load();
    };
  }, [allowVideo, playing]);

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
      // Play by default wherever playback is allowed at all. `allowed` is already the
      // conservative gate — desktop only, no prefers-reduced-motion, no Save-Data and
      // not on a 2g/3g connection — so the click that used to be required on top of it
      // meant the animation effectively never ran. Every constrained visitor still
      // fetches nothing; see the tests in tests/assets.spec.ts.
      if (!allowed) setPlaying(false);
      else if (!userChosePlayback.current) setPlaying(true);
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

  // The entrance stagger lives in CSS (`[data-hero-animate]` in globals.css) and
  // animates transform only, never opacity. It used to live here, setting opacity to
  // 0 and restoring it in a requestAnimationFrame; rAF does not fire in a hidden tab,
  // and the restore did not survive the re-render when allowVideo resolves, so the
  // hero could render as an empty card. See the keyframes comment for why the CSS
  // version must not fade opacity either. Per-element delays are set below.

  return (
    <section className="relative overflow-hidden py-8 sm:py-12">
      {/* The poster paints first and sits behind the video, so the hero is never blank
          while the video loads — and it stays the only asset fetched wherever the gate
          above says no. */}
        <div
          className="absolute inset-0 w-full h-full bg-cover bg-center -z-20"
          style={{ backgroundImage: `url(${basePath}/calor-lava-poster.jpg)` }}
          aria-hidden="true"
        />
      {allowVideo && playing && (
        <video
          ref={videoRef}
          autoPlay
          loop
          muted
          playsInline
          preload="auto"
          className="absolute inset-0 w-full h-full object-cover -z-20"
          poster={`${basePath}/calor-lava-poster.jpg`}
          aria-hidden="true"
        >
          <source src={`${basePath}/calor-lava.mp4`} type="video/mp4" />
        </video>
      )}

      {/* Both edges of the video are softened into the page background, so the hero
          neither starts nor ends on a hard horizontal line where the footage is cut
          off. The ramp is 30px on each edge.

          The colour is `background` at both ends and that is correct in both themes:
          below the hero is the page itself, and above it is the header, which is
          `bg-background/95` (Header.tsx) sitting at the top of that same page.

          Sizes differ for one reason. The top element is exactly the 30px ramp. The
          bottom is 54px — the same 30px ramp, then a hard stop that stays opaque for
          another 24px. That tail is not decoration: the divider below is a WAVY path,
          not a rectangle, so it covers only part of its own 24px band and roughly 10px
          of video showed through above the curve. Going opaque before that band and
          staying opaque through it covers the gap at every x position. Shrinking the
          bottom element to a bare 30px brings that strip of video straight back. */}
      <div
        className="pointer-events-none absolute inset-x-0 top-0 z-0 h-[30px]"
        style={{ background: 'linear-gradient(to top, transparent, hsl(var(--background)) 30px)' }}
        aria-hidden="true"
      />
      <div
        className="pointer-events-none absolute inset-x-0 bottom-0 z-0 h-[54px]"
        style={{ background: 'linear-gradient(to bottom, transparent, hsl(var(--background)) 30px, hsl(var(--background)))' }}
        aria-hidden="true"
      />

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
          <div className="rounded-2xl bg-white/5 backdrop-blur-xl border border-white/10 px-4 py-6 sm:px-10 sm:py-8 shadow-2xl">
            <div className="flex justify-center mb-4" data-hero-animate style={{ animationDelay: '200ms' }}>
              <Image
                src={`${basePath}/calor-logo-256.webp`}
                alt="Calor logo"
                width={120}
                height={120}
                className="h-16 w-16 sm:h-24 sm:w-24 drop-shadow-[0_0_30px_rgba(250,61,111,0.4)]"
                priority
              />
            </div>
            <h1 className="text-4xl font-bold tracking-tight text-white sm:text-6xl font-display" data-hero-animate style={{ animationDelay: '350ms' }}>
              Calor
            </h1>
            <p className="mt-3 text-base font-medium text-white/90 sm:text-xl font-body" data-hero-animate style={{ animationDelay: '500ms' }}>
              A language for coding agents, compiled to C# and .NET.
            </p>
            <p className="mt-3 text-sm leading-6 text-white/80 font-body" data-hero-animate style={{ animationDelay: '650ms' }}>
              Inspect explicit contracts, declared effects, and stable IDs.
            </p>

            <div className="mt-6 flex flex-col sm:flex-row items-stretch sm:items-center justify-center gap-3" data-hero-animate style={{ animationDelay: '800ms' }}>
              <Button asChild size="lg" className="bg-gradient-to-r from-calor-pink to-calor-salmon hover:from-calor-pink/90 hover:to-calor-salmon/90 text-white border-0 shadow-lg shadow-calor-pink/25">
                <Link href="/docs/getting-started/hello-world/" onClick={() => trackCtaClick('get_started')}>
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
        <button
          type="button"
          onClick={() => { userChosePlayback.current = true; setPlaying(!playing); }}
          // The accessible name stays the full sentence even though the control is now
          // an icon. It is what a screen reader announces, and what the browser tests
          // select on (tests/assets.spec.ts) — an icon-only button with no name would
          // read as "button" and break both at once.
          aria-label={playing ? 'Pause background animation' : 'Play background animation'}
          title={playing ? 'Pause background animation' : 'Play background animation'}
          className="absolute bottom-8 right-6 z-20 grid h-8 w-8 place-items-center rounded-full border border-white/30 bg-calor-navy/70 text-white backdrop-blur-sm transition-colors hover:bg-calor-navy focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-white"
        >
          {playing
            ? <Pause className="h-3.5 w-3.5" aria-hidden="true" />
            : <Play className="h-3.5 w-3.5" aria-hidden="true" />}
        </button>
      )}

      {/* Shaped bottom divider */}
      <div className="hero-divider pointer-events-none" style={{ height: 24 }}>
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
