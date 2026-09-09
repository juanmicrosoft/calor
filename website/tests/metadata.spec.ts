import { expect, test } from '@playwright/test';
import { getDocSlugs } from '../src/lib/docs';

test('sitemap, robots and canonical metadata match the generated route inventory', async ({ page, request }) => {
  const base = process.env.NEXT_PUBLIC_BASE_PATH || '';
  const origin = process.env.NEXT_PUBLIC_SITE_ORIGIN || 'https://calor.dev';
  const prefix = `${origin}${base}`;
  const sitemap = await request.get(`${base}/sitemap.xml`);
  expect(sitemap.ok()).toBe(true);
  const urls = [...(await sitemap.text()).matchAll(/<loc>(.*?)<\/loc>/g)].map(match => match[1]);
  const expected = [`${prefix}/`, ...getDocSlugs().map(slug => `${prefix}/docs/${slug ? `${slug}/` : ''}`)];
  expect([...urls].sort()).toEqual(expected.sort());
  expect(new Set(urls).size).toBe(urls.length);
  const robots = await request.get(`${base}/robots.txt`);
  expect(robots.ok()).toBe(true);
  expect(await robots.text()).toContain('User-Agent: *');
  expect(await robots.text()).toContain('Allow: /');
  expect(await robots.text()).toContain(`Sitemap: ${prefix}/sitemap.xml`);
  for (const url of urls) {
    const route = new URL(url).pathname;
    const response = await request.get(route);
    expect(response.status(), route).toBe(200);
    const html = await response.text();
    const metadata = await page.evaluate(html => {
      const document = new DOMParser().parseFromString(html, 'text/html');
      return { canonical: Array.from(document.querySelectorAll('link[rel="canonical"]')).map(link => link.getAttribute('href')),
        og: document.querySelector('meta[property="og:url"]')?.getAttribute('content') };
    }, html);
    expect(metadata.canonical, route).toEqual([url]);
    expect(metadata.og, route).toBe(url);
  }
});
