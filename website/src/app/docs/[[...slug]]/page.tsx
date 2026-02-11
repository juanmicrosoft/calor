import { notFound } from 'next/navigation';
import { MDXRemote } from 'next-mdx-remote/rsc';
import remarkGfm from 'remark-gfm';
import {
  getDocBySlug,
  getDocSlugs,
  getDocSections,
  getAdjacentDocs,
  extractHeadings,
} from '@/lib/docs';
import { Sidebar } from '@/components/docs/Sidebar';
import { TableOfContents } from '@/components/docs/TableOfContents';
import { Pagination } from '@/components/docs/Pagination';
import { MobileSidebar } from '@/components/docs/MobileSidebar';
import { DocsPageTracker } from '@/components/docs/DocsPageTracker';
import { mdxComponents } from '@/components/mdx';

interface DocPageProps {
  params: Promise<{ slug?: string[] }>;
}

export async function generateStaticParams() {
  const slugs = getDocSlugs();

  return [
    { slug: [] }, // /docs/
    ...slugs.map((slug) => ({
      slug: slug.split('/'),
    })),
  ];
}

export async function generateMetadata({ params }: DocPageProps) {
  const { slug } = await params;
  const slugPath = slug?.join('/') || '';
  const doc = getDocBySlug(slugPath);

  if (!doc) {
    return {
      title: 'Not Found',
    };
  }

  return {
    title: doc.title,
    description: doc.description,
  };
}

export default async function DocPage({ params }: DocPageProps) {
  const { slug } = await params;
  const slugPath = slug?.join('/') || '';
  const doc = getDocBySlug(slugPath);

  if (!doc) {
    notFound();
  }

  const sections = getDocSections();
  const { prev, next } = getAdjacentDocs(slugPath);
  const headings = extractHeadings(doc.content);

  return (
    <div className="mx-auto max-w-7xl px-4 py-8 lg:px-8">
      <DocsPageTracker slug={slugPath} />
      <div className="flex gap-8">
        {/* Desktop Sidebar */}
        <div className="hidden lg:block">
          <Sidebar sections={sections} />
        </div>

        {/* Main Content */}
        <div className="min-w-0 flex-1">
          {/* Mobile Sidebar */}
          <MobileSidebar sections={sections} />

          <article className="prose dark:prose-invert max-w-none">
            <h1>{doc.title}</h1>
            {doc.description && (
              <p className="lead text-xl text-muted-foreground">{doc.description}</p>
            )}

            <MDXRemote
              source={doc.content}
              components={mdxComponents}
              options={{
                mdxOptions: {
                  remarkPlugins: [remarkGfm],
                },
              }}
            />
          </article>

          <Pagination prev={prev} next={next} />
        </div>

        {/* Table of Contents */}
        <TableOfContents headings={headings} />
      </div>
    </div>
  );
}
