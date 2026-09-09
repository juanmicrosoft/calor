import { expect, test } from '@playwright/test';
import { readFile, readdir } from 'node:fs/promises';

test('source fences identify Calor examples rather than defaulting to plain text', async () => {
  const files = await readdir('content', { recursive: true });
  const missing: string[] = [];
  for (const file of files.filter(file => file.endsWith('.mdx'))) {
    const content = await readFile(`content/${file}`, 'utf8');
    for (const match of content.matchAll(/^```([^\n]*)\n([\s\S]*?)^```\s*$/gm)) {
      const firstCodeLine = match[2].split('\n').map(line => line.trim())
        .find(line => line && !line.startsWith('//')) || '';
      if (!match[1].trim() && /^(§|\([+\-*/%<>=!&|])/.test(firstCodeLine)) missing.push(file);
    }
  }
  expect(missing).toEqual([]);
});

test('comment-prefixed source examples render with Calor labels', async ({ page }) => {
  await page.route('https://**/*', route => route.abort());
  const base = process.env.NEXT_PUBLIC_BASE_PATH || '';
  for (const [route, comment] of [
    ['expressions', '// Check if list contains element'],
    ['expressions', '// Get count'],
    ['effects', '// Read-only file operation'],
  ]) {
    await page.goto(`${base}/docs/syntax-reference/${route}/`);
    await expect(page.getByRole('group', { name: 'Calor code example' }).filter({ hasText: comment })).toBeVisible();
  }
});

for (const dark of [false, true]) {
  test(`Calor labels and exact clipboard payload in ${dark ? 'dark' : 'light'} theme`, async ({ page, context }) => {
    await context.grantPermissions(['clipboard-read', 'clipboard-write']);
    await context.route('https://**/*', route => route.abort());
    await page.goto(`${process.env.NEXT_PUBLIC_BASE_PATH || ''}/docs/getting-started/hello-world/`);
    await page.evaluate(dark => document.documentElement.classList.toggle('dark', dark), dark);
    const block = page.getByRole('group', { name: 'Calor code example' }).first();
    await expect(block.getByText('Calor', { exact: true })).toBeVisible();
    const doc = await readFile('content/getting-started/hello-world.mdx', 'utf8');
    const source = doc.match(/```calor\n([\s\S]*?)```/)![1];
    await expect(block.locator('pre code')).toHaveText(source);
    await block.getByRole('button', { name: 'Copy', exact: true }).click();
    await expect(block.getByRole('button')).toHaveText('Copied');
    expect(await page.evaluate(() => navigator.clipboard.readText())).toBe(source);
    const colors = await block.evaluate(element => ({
      label: getComputedStyle(element.querySelector('span')!).color,
      code: getComputedStyle(element.querySelector('code')!).color,
      background: getComputedStyle(element).backgroundColor,
    }));
    const rgb = (color: string) => (color.match(/[\d.]+/g) || []).slice(0, 3).map(Number);
    const luminance = (color: string) => rgb(color).map(c => c / 255).map(c => c <= .04045 ? c / 12.92 : ((c + .055) / 1.055) ** 2.4)
      .reduce((sum, c, i) => sum + c * [.2126, .7152, .0722][i], 0);
    for (const color of [colors.label, colors.code]) {
      expect((luminance(color) + .05) / (luminance(colors.background) + .05)).toBeGreaterThanOrEqual(4.5);
    }
  });
}
