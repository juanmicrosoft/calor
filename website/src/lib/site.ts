import { getBasePath } from './utils';

const origin = new URL(process.env.NEXT_PUBLIC_SITE_ORIGIN || 'https://calor.dev');
if (!['https:', 'http:'].includes(origin.protocol) || origin.pathname !== '/' || origin.search || origin.hash) {
  throw new Error('NEXT_PUBLIC_SITE_ORIGIN must be an HTTP(S) origin without a path, query or fragment');
}
export const siteOrigin = origin.origin;
export function publicUrl(path: string): string {
  return `${siteOrigin}${getBasePath()}/${path.replace(/^\/+/, '')}`;
}
export function canonicalUrl(path: string): string {
  return publicUrl(`${path.replace(/^\/+|\/+$/g, '')}/`);
}
