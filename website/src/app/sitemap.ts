import type { MetadataRoute } from 'next';
import { getDocSlugs } from '@/lib/docs';
import { canonicalUrl } from '@/lib/site';

export const dynamic = 'force-static';

export default function sitemap(): MetadataRoute.Sitemap {
  return [
    { url: canonicalUrl('/') },
    ...getDocSlugs().map(slug => ({ url: canonicalUrl(`/docs/${slug}`) })),
  ];
}
