import { getDocSlugs, getDocBySlug } from '@/lib/docs';

export const dynamic = 'force-static';

export function GET() {
  const documents = getDocSlugs().map(slug => {
    const doc = getDocBySlug(slug)!;
    return { slug, title: doc.title, text: doc.content.replace(/\s+/g, ' ').trim() };
  });
  return Response.json(documents);
}
