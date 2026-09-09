'use client';

import { useEffect, useRef, useState } from 'react';
import { THEME_KEY } from '@/lib/theme';

export function useTheme() {
  const [isDark, setIsDark] = useState(false);
  const override = useRef<string | null>(null);
  const apply = (dark: boolean) => {
    document.documentElement.classList.toggle('dark', dark);
    document.documentElement.style.colorScheme = dark ? 'dark' : 'light';
    setIsDark(dark);
  };

  useEffect(() => {
    try { override.current = localStorage.getItem(THEME_KEY); } catch { /* System fallback when storage is unavailable. */ }
    const system = window.matchMedia('(prefers-color-scheme: dark)');
    const sync = () => apply(override.current === 'dark' || (override.current !== 'light' && system.matches));
    const stored = (event: StorageEvent) => {
      if (event.key !== THEME_KEY && event.key !== null) return;
      override.current = event.newValue;
      sync();
    };
    sync();
    system.addEventListener('change', sync);
    window.addEventListener('storage', stored);
    return () => {
      system.removeEventListener('change', sync);
      window.removeEventListener('storage', stored);
    };
  }, []);

  const toggleTheme = () => {
    const dark = !document.documentElement.classList.contains('dark');
    override.current = dark ? 'dark' : 'light';
    try { localStorage.setItem(THEME_KEY, override.current); } catch { /* Keep the choice for this visit. */ }
    apply(dark);
    return dark ? 'dark' : 'light';
  };
  return { isDark, toggleTheme };
}
