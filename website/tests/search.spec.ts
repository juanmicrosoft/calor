import { expect, test } from '@playwright/test';

test('failed search loading is explained and can be retried', async ({ page }) => {
  await page.route('https://**/*', route => route.abort());
  await page.route('**/search-index.json', route => route.fulfill({ status: 503, body: 'Unavailable' }));
  await page.goto(`${process.env.NEXT_PUBLIC_BASE_PATH || ''}/docs/`);
  const input = page.getByRole('searchbox', { name: 'Search documentation' });
  await input.fill('--verify');
  await expect(page.getByRole('status')).toContainText('Search could not load');
  await page.unroute('**/search-index.json');
  await input.press('Tab');
  await input.focus();
  await expect(page.getByRole('list', { name: 'Search results' })).toBeVisible();
});

for (const width of [1366, 390]) {
  test(`local cross-document keyboard search at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 844 });
    await page.route('https://**/*', route => route.abort());
    const indexRequests: string[] = [];
    page.on('request', request => { if (request.url().includes('search-index')) indexRequests.push(request.url()); });
    const root = `${process.env.NEXT_PUBLIC_BASE_PATH || ''}/docs/`;
    await page.goto(root);
    const input = page.getByRole('searchbox', { name: 'Search documentation' });
    for (const query of ['Calor0410', '--verify', '§F']) {
      await input.fill(query);
      const results = page.getByRole('list', { name: 'Search results' });
      await expect(results.getByRole('link').first()).toBeVisible();
      expect(indexRequests.every(url => new URL(url).hostname === '127.0.0.1')).toBe(true);
      expect(indexRequests.every(url => !url.includes(query))).toBe(true);
      const first = results.getByRole('link').first();
      const target = await first.getAttribute('href');
      await input.press('ArrowDown');
      await expect(first).toBeFocused();
      await page.keyboard.press('Enter');
      await expect(page).toHaveURL(`http://127.0.0.1:4173${target}`);
      await expect(page.locator('article')).toContainText(query);
      await expect(input).toBeVisible();
    }

    await input.fill('no-such-diagnostic-987654321');
    await expect(page.getByRole('status')).toContainText('No matching pages');
    await input.press('Escape');
    await expect(input).toHaveValue('');
    await expect(input).toBeFocused();
    await page.goto(root);
    await page.getByRole('link', { name: 'Hello World', exact: true }).last().click();
    await expect(page).toHaveURL(/\/hello-world\/$/);
    await expect(page.locator('article')).toContainText('without an AI subscription');
  });
}
