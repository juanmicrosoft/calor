import { expect, test } from '@playwright/test';
import { readFile } from 'node:fs/promises';
import { compile } from '@mdx-js/mdx';
import matter from 'gray-matter';
import remarkGfm from 'remark-gfm';
import type { Root, Heading as MarkdownHeading } from 'mdast';
import { remarkHeadings, type Heading } from '../src/lib/headings';

// The shared vectors pin the ids headings.ts assigns, so the release gate's port
// (scripts/verify_release_adjudication.py heading_ids) cannot drift from the site.
test('heading ids match the shared vectors and the W1 pages', async () => {
  const { vectors, pages } = JSON.parse(await readFile('tests/fixtures/heading-ids.json', 'utf8')) as {
    vectors: { markdown: string; ids: string[] }[];
    pages: { path: string; ids: string[] }[];
  };
  const fromPages = await Promise.all(pages.map(async page => ({ markdown: await readFile(page.path, 'utf8'), ids: page.ids })));
  for (const vector of [...vectors, ...fromPages]) {
    const ids: string[] = [];
    const collect = () => (tree: Root) => {
      const visit = (node: Root | Root['children'][number]) => {
        if (node.type === 'heading') {
          const id = (node as MarkdownHeading).data?.hProperties?.id;
          ids.push(String(id));
        }
        if ('children' in node) for (const child of node.children) visit(child as Root['children'][number]);
      };
      visit(tree);
    };
    const headings: Heading[] = [];
    await compile(matter(vector.markdown).content, { remarkPlugins: [remarkGfm, remarkHeadings(headings), collect] });
    expect(ids, JSON.stringify(vector.markdown)).toEqual(vector.ids);
  }
});
