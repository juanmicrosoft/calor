import { expect, test } from '@playwright/test';

const base = process.env.NEXT_PUBLIC_BASE_PATH || '';
for (const colorScheme of ['dark', 'light'] as const) {
  test(`theme honors ${colorScheme} system and retains explicit override`, async ({ browser }) => {
    const context = await browser.newContext({ colorScheme, viewport: { width: 1366, height: 768 } });
    await context.route('https://**/*', route => route.abort());
    // Block framework scripts: the head initializer alone must choose the first painted theme.
    await context.route('**/_next/static/**/*.js', route => route.abort());
    const page = await context.newPage();
    await page.goto(`http://127.0.0.1:4173${base}/`);
    expect(await page.locator('html').evaluate(el => el.classList.contains('dark'))).toBe(colorScheme === 'dark');
    await context.unroute('**/_next/static/**/*.js');
    await page.reload();
    const button = page.locator('header').getByRole('button', { name: 'Dark mode', exact: true });
    await expect(button).toHaveAttribute('aria-pressed', String(colorScheme === 'dark'));
    await button.click();
    const chosenDark = colorScheme !== 'dark';
    await expect(button).toHaveAttribute('aria-pressed', String(chosenDark));
    await page.reload();
    await expect(button).toHaveAttribute('aria-pressed', String(chosenDark));
    expect(await page.evaluate(() => localStorage.getItem('calor-theme'))).toBe(chosenDark ? 'dark' : 'light');
    await page.locator('header').getByRole('link', { name: 'Docs', exact: true }).click();
    await expect(page).toHaveURL(/\/docs\/$/);
    await expect(button).toHaveAttribute('aria-pressed', String(chosenDark));
    await page.emulateMedia({ colorScheme });
    expect(await page.locator('html').evaluate(el => el.classList.contains('dark'))).toBe(chosenDark);
    await page.setViewportSize({ width: 390, height: 844 });
    await page.getByRole('button', { name: 'Open main menu' }).click();
    const mobile = page.getByRole('dialog').getByRole('button', { name: 'Dark mode', exact: true });
    await expect(mobile).toHaveAttribute('aria-pressed', String(chosenDark));
    await mobile.click();
    await expect(mobile).toHaveAttribute('aria-pressed', String(!chosenDark));
    await page.keyboard.press('Escape');
    await page.reload();
    expect(await page.locator('html').evaluate(el => el.classList.contains('dark'))).toBe(!chosenDark);
    await context.close();
  });
}

test('system changes apply without an override, and blocked storage does not break toggling', async ({ page }) => {
  await page.route('https://**/*', route => route.abort());
  await page.setViewportSize({ width: 1366, height: 768 });
  await page.emulateMedia({ colorScheme: 'light' });
  await page.addInitScript(() => {
    Object.defineProperty(window, 'localStorage', { get() { throw new DOMException('Blocked', 'SecurityError'); } });
  });
  await page.goto(`${base}/`);
  const button = page.locator('header').getByRole('button', { name: 'Dark mode', exact: true });
  await expect(button).toHaveAttribute('aria-pressed', 'false');
  await page.emulateMedia({ colorScheme: 'dark' });
  await expect(button).toHaveAttribute('aria-pressed', 'true');
  await button.click();
  await expect(button).toHaveAttribute('aria-pressed', 'false');
});
