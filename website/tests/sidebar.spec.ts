import { expect, test } from '@playwright/test';
import { normalizePathname } from '../src/lib/utils';

test('normalization accepts framework-stripped or configured base paths', () => {
  for (const base of ['', '/calor']) {
    expect(normalizePathname(`${base}/docs/getting-started/installation/`, base)).toBe('/docs/getting-started/installation');
    expect(normalizePathname('/docs/getting-started/installation/', base)).toBe('/docs/getting-started/installation');
  }
  expect(normalizePathname('/calorie/docs/', '/calor')).toBe('/calorie/docs');
});

for (const mobile of [false, true]) {
  test(`${mobile ? 'mobile' : 'desktop'} reveals current section on direct and client navigation`, async ({ page }) => {
    await page.setViewportSize({ width: mobile ? 390 : 1366, height: 844 });
    await page.route('https://**/*', route => route.abort());
    await page.goto(`${process.env.NEXT_PUBLIC_BASE_PATH || ''}/docs/getting-started/installation/`);
    const sidebar = mobile
      ? page.getByRole('dialog', { name: 'Documentation navigation' })
      : page.getByRole('navigation', { name: 'Documentation', exact: true });
    const open = async () => {
      if (mobile) await page.getByRole('button', { name: 'Menu', exact: true }).click();
    };
    await open();
    await expect(sidebar.getByRole('button', { name: 'Getting Started', exact: true })).toHaveAttribute('aria-expanded', 'true');
    await expect(sidebar.getByRole('link', { name: 'Installation', exact: true })).toHaveAttribute('aria-current', 'page');
    await expect(sidebar.locator('[aria-current="page"]')).toHaveCount(1);
    await sidebar.getByRole('link', { name: 'Hello World', exact: true }).click();
    await expect(page).toHaveURL(/\/docs\/getting-started\/hello-world\/$/);
    if (mobile) await open();
    await expect(sidebar.getByRole('link', { name: 'Hello World', exact: true })).toHaveAttribute('aria-current', 'page');
    if (mobile) await page.keyboard.press('Escape');
    // Header client navigation to a different section must reveal it, not retain only initial state.
    if (mobile) await page.getByRole('button', { name: 'Open main menu' }).click();
    const headerNavigation = mobile ? page.getByRole('dialog', { name: 'Main navigation' }) : page.locator('header');
    await headerNavigation.getByRole('link', { name: 'Benchmarks', exact: true }).click();
    await expect(page).toHaveURL(/\/docs\/benchmarking\/$/);
    await open();
    await expect(sidebar.getByRole('button', { name: 'Benchmarking', exact: true })).toHaveAttribute('aria-expanded', 'true');
    await expect(sidebar.locator('[aria-current="page"]')).toHaveCount(1);
    if (mobile) await page.keyboard.press('Escape');
    else await expect(page.locator('header [aria-current="location"]')).toHaveCount(1);
  });
}
