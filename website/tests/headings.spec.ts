import { expect, test } from '@playwright/test';
import { readFile, readdir } from 'node:fs/promises';
import path from 'node:path';
import { Heading, remarkHeadings } from '../src/lib/headings';
import type { Root } from 'mdast';

const base = process.env.NEXT_PUBLIC_BASE_PATH || '';
test('inline markup, repeated text and colliding suffixes share assigned IDs', () => {
  const headings: Heading[] = [];
  const tree: Root = { type: 'root', children: [
    { type: 'heading', depth: 2, children: [{ type: 'text', value: 'Basic ' }, { type: 'inlineCode', value: 'init' }] },
    { type: 'heading', depth: 2, children: [{ type: 'emphasis', children: [{ type: 'text', value: 'Basic init' }] }] },
    { type: 'heading', depth: 2, children: [{ type: 'text', value: 'Basic init-1' }] },
    { type: 'code', value: '## Not a heading' },
  ] };
  remarkHeadings(headings)()(tree);
  expect(headings.map(h => h.id)).toEqual(['basic-init', 'basic-init-1', 'basic-init-1-1']);
  expect(tree.children.slice(0, 3).map(h => h.data?.hProperties?.id)).toEqual(headings.map(h => h.id));
});

test('every exported documentation TOC resolves to exactly one heading', async ({ page }) => {
  const files = await readdir('out/docs', { recursive: true });
  const failures: string[] = [];
  for (const file of files.filter(file => file.endsWith('.html'))) {
    const html = await readFile(path.join('out/docs', file), 'utf8');
    const broken = await page.evaluate(html => {
      const doc = new DOMParser().parseFromString(html, 'text/html');
      return Array.from(doc.querySelectorAll('nav[aria-label="On this page"] a')).flatMap(link => {
        const id = link.getAttribute('href')!.slice(1);
        const matches = doc.querySelectorAll(`[id="${CSS.escape(id)}"]`);
        return matches.length !== 1 || !/^H[1-6]$/.test(matches[0].tagName) ? [id] : [];
      });
    }, html);
    failures.push(...broken.map(id => `${file}#${id}`));
  }
  expect(files.filter(file => file.endsWith('.html')).length).toBeGreaterThan(90);
  expect(failures).toEqual([]);
});

test('native fragments support click, new tab, reload and back/forward', async ({ page, context }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.route('https://**/*', route => route.abort());
  await page.goto(`${base}/docs/getting-started/`);
  const toc = page.getByRole('navigation', { name: 'On this page' });
  const basic = toc.getByRole('link', { name: 'Basic Init (calor init)', exact: true });
  await basic.click();
  await expect(page).toHaveURL(/#basic-init-calor-init$/);
  await expect(page.locator('#basic-init-calor-init')).toBeInViewport();
  const fragment = page.url();
  const other = await context.newPage();
  await other.goto(fragment);
  await expect(other.locator('#basic-init-calor-init')).toBeInViewport();
  await other.close();
  await toc.getByRole('link', { name: 'With Claude (calor init --ai claude)', exact: true }).click();
  await page.goBack();
  await expect(page).toHaveURL(fragment);
  await page.goForward();
  await expect(page).toHaveURL(/#with-claude-calor-init-ai-claude$/);
  await page.reload();
  await expect(page.locator('#with-claude-calor-init-ai-claude')).toBeInViewport();
  await page.goto(`${base}/docs/getting-started/installation/`);
  await page.getByRole('navigation', { name: 'On this page' }).getByRole('link', { name: 'Next Steps', exact: true }).click();
  await expect(page).toHaveURL(/#next-steps$/);
  await expect(page.locator('#next-steps')).toBeInViewport();
});
