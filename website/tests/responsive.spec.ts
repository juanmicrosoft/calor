import { expect, test } from '@playwright/test';

for (const viewport of [{ width: 1366, height: 768 }, { width: 390, height: 844 }, { width: 320, height: 740 }]) {
  test(`first action and example at ${viewport.width}x${viewport.height}`, async ({ page }, testInfo) => {
    await page.setViewportSize(viewport);
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await page.route('https://**/*', route => route.abort());
    await page.goto(`${process.env.NEXT_PUBLIC_BASE_PATH || ''}/`);
    const hero = page.locator('section').filter({ has: page.getByRole('heading', { name: 'Calor', exact: true }) });
    const action = hero.getByRole('link', { name: 'Get Started' });
    await expect(hero.getByText('A language for coding agents, compiled to C# and .NET.')).toBeInViewport();
    const box = await action.boundingBox();
    expect(box).not.toBeNull();
    expect(box!.y + box!.height).toBeLessThanOrEqual(viewport.height);
    expect(box!.x).toBeGreaterThanOrEqual(0);
    expect(box!.x + box!.width).toBeLessThanOrEqual(viewport.width);
    const example = page.getByRole('region', { name: 'From Calor to a .NET program' });
    const exampleBox = await example.boundingBox();
    expect(exampleBox!.y + exampleBox!.height).toBeLessThanOrEqual(viewport.height * 2);
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBe(viewport.width);
    await page.screenshot({ path: testInfo.outputPath(`${viewport.width}-first-screen.png`) });
    await action.click();
    await expect(page).toHaveURL(/\/docs\/getting-started\/hello-world\/$/);
    await expect(page.getByRole('heading', { name: 'Prerequisites', exact: true })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Run without a project', exact: true })).toBeVisible();
  });
}
