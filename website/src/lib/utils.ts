import { type ClassValue, clsx } from 'clsx';
import { twMerge } from 'tailwind-merge';

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}

export function getBasePath() {
  return process.env.NEXT_PUBLIC_BASE_PATH || '';
}

export function normalizePathname(pathname: string | null, basePath = getBasePath()): string {
  const path = pathname || '/';
  const withoutBase = basePath && (path === basePath || path.startsWith(`${basePath}/`))
    ? path.slice(basePath.length) : path;
  return withoutBase.replace(/\/+$/, '') || '/';
}

export function currentDocSection(pathname: string | null): string | undefined {
  const normalized = normalizePathname(pathname);
  return normalized.startsWith('/docs/') ? normalized.slice('/docs/'.length).split('/')[0] : undefined;
}
