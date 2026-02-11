'use client';

import { useEffect } from 'react';
import { trackDocsPageView } from '@/lib/analytics';

export function DocsPageTracker({ slug }: { slug: string }) {
  useEffect(() => {
    trackDocsPageView(slug);
  }, [slug]);

  return null;
}
