import { expect, test } from '@playwright/test';
import { formatTimestamp } from '../src/lib/timestamps';

test('timestamp formatter labels UTC and handles invalid data explicitly', () => {
  expect(formatTimestamp('2026-09-01T22:18:14.678262Z')).toBe('2026-09-01 22:18 UTC');
  expect(formatTimestamp('2026-09-01T15:18:00-07:00')).toBe('2026-09-01 22:18 UTC');
  expect(formatTimestamp('invalid')).toBe('Unknown timestamp');
});

for (const timezoneId of ['UTC', 'America/Los_Angeles', 'Asia/Tokyo']) {
  test(`static timestamps hydrate without exceptions in ${timezoneId}`, async ({ browser }) => {
    const context = await browser.newContext({ timezoneId });
    await context.route('https://**/*', route => route.abort());
    const page = await context.newPage();
    const errors: string[] = [];
    page.on('pageerror', error => errors.push(error.message));
    page.on('console', message => {
      if (message.type() === 'error' && /hydration|mismatch|React error/i.test(message.text())) errors.push(message.text());
    });
    for (const route of ['/', '/docs/benchmarking/results/', '/docs/benchmarking/agent-tasks/']) {
      await page.goto(`http://127.0.0.1:4173${process.env.NEXT_PUBLIC_BASE_PATH || ''}${route}`);
      const initial = await page.locator('time').allTextContents();
      expect(initial.length).toBeGreaterThan(0);
      for (const time of await page.locator('time').all()) {
        await expect(time).toHaveText(formatTimestamp(await time.getAttribute('datetime') || ''));
      }
      // A React interaction confirms hydration completed; no arbitrary sleep.
      await page.setViewportSize({ width: 390, height: 844 });
      await page.getByRole('button', { name: 'Open main menu' }).click();
      await expect(page.getByRole('dialog', { name: 'Main navigation' })).toBeVisible();
      await page.keyboard.press('Escape');
      expect(await page.locator('time').allTextContents()).toEqual(initial);
    }
    expect(errors).toEqual([]);
    await context.close();
  });
}
