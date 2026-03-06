'use client';

import { useEffect, useRef } from 'react';

interface ScrollRevealOptions {
  threshold?: number;
  rootMargin?: string;
  direction?: 'up' | 'left' | 'right';
  staggerChildren?: boolean;
  staggerDelay?: number;
}

export function useScrollReveal<T extends HTMLElement>({
  threshold = 0.15,
  rootMargin = '0px 0px -50px 0px',
  direction = 'up',
  staggerChildren = false,
  staggerDelay = 100,
}: ScrollRevealOptions = {}) {
  const ref = useRef<T>(null);

  useEffect(() => {
    const el = ref.current;
    if (!el) return;

    // Check for reduced motion preference
    const prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    if (prefersReducedMotion) return;

    // Set initial hidden state
    const hiddenClass = direction === 'up' ? 'scroll-hidden' : direction === 'left' ? 'scroll-hidden-left' : 'scroll-hidden-right';
    const visibleClass = direction === 'up' ? 'scroll-visible' : 'scroll-visible-x';

    el.classList.add(hiddenClass);

    if (staggerChildren) {
      const children = el.children;
      for (let i = 0; i < children.length; i++) {
        const child = children[i] as HTMLElement;
        child.classList.add(hiddenClass);
        child.style.transitionDelay = `${i * staggerDelay}ms`;
      }
    }

    const observer = new IntersectionObserver(
      (entries) => {
        entries.forEach((entry) => {
          if (entry.isIntersecting) {
            el.classList.remove(hiddenClass);
            el.classList.add(visibleClass);

            if (staggerChildren) {
              const children = el.children;
              for (let i = 0; i < children.length; i++) {
                const child = children[i] as HTMLElement;
                child.classList.remove(hiddenClass);
                child.classList.add(visibleClass);
              }
            }

            observer.unobserve(el);
          }
        });
      },
      { threshold, rootMargin }
    );

    observer.observe(el);

    return () => observer.disconnect();
  }, [threshold, rootMargin, direction, staggerChildren, staggerDelay]);

  return ref;
}
