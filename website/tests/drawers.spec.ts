import { expect, test } from '@playwright/test';

const base = process.env.NEXT_PUBLIC_BASE_PATH || '';
for (const drawer of [
  { route: '/', trigger: 'Open main menu', label: 'Main navigation', close: 'Close menu' },
  { route: '/docs/getting-started/installation/', trigger: 'Menu', label: 'Documentation navigation', close: 'Close documentation menu' },
]) {
  test(`${drawer.label}: keyboard containment, names, Escape and focus restoration`, async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.route('https://**/*', route => route.abort());
    await page.goto(`${base}${drawer.route}`);
    const trigger = page.getByRole('button', { name: drawer.trigger, exact: true });
    await trigger.focus();
    await page.keyboard.press('Enter');
    const dialog = page.getByRole('dialog', { name: drawer.label });
    await expect(dialog).toBeVisible();
    await expect(trigger).toHaveAttribute('aria-expanded', 'true');
    await expect(dialog).toHaveAttribute('id', await trigger.getAttribute('aria-controls') || '');
    await expect(dialog).toHaveAttribute('aria-modal', 'true');
    expect(await page.evaluate(() => document.body.style.overflow)).toBe('hidden');
    const controls = dialog.locator('a[href],button');
    for (const control of await controls.all()) {
      await expect(control).toHaveAccessibleName(/\S/);
    }
    for (const key of ['Tab', 'Shift+Tab']) {
      for (let i = 0; i < await controls.count() + 2; i++) {
        expect(await dialog.evaluate(element => element.contains(document.activeElement))).toBe(true);
        await page.keyboard.press(key);
      }
    }
    // Native modal inertness must prevent covered content receiving even explicit focus.
    await trigger.evaluate(element => element.focus());
    expect(await dialog.evaluate(element => element.contains(document.activeElement))).toBe(true);
    await page.keyboard.press('Escape');
    await expect(dialog).not.toBeVisible();
    await expect(trigger).toBeFocused();
    await expect(trigger).toHaveAttribute('aria-expanded', 'false');
    expect(await page.evaluate(() => document.body.style.overflow)).not.toBe('hidden');
    await trigger.press('Enter');
    await dialog.getByRole('button', { name: drawer.close, exact: true }).click();
    await expect(trigger).toBeFocused();
    await trigger.press('Enter');
    await expect(dialog).toBeVisible();
    await page.setViewportSize({ width: 1366, height: 768 });
    await expect(dialog).not.toBeVisible();
    await expect.poll(() => page.evaluate(() => document.body.style.overflow)).not.toBe('hidden');
  });
}
