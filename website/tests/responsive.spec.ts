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

// The header's bottom border sits flush on the hero video's first row, so at the top
// of the landing page it drew a slate line straight across the footage and defeated
// the fade there. It is suppressed while the header is at rest and restored once it is
// actually floating over content — that second half matters, because a permanently
// borderless translucent header does not separate from the sections running under it.
//
// Asserted on COLOUR, not on the presence of `border-b`: the 1px box is kept at all
// times so the page cannot shift by a pixel when the border appears.
test('the header draws no seam over the hero at rest, and separates from content once scrolled', async ({ page }) => {
  await page.setViewportSize({ width: 1366, height: 768 });
  await page.route('https://**/*', route => route.abort());
  await page.goto(`${process.env.NEXT_PUBLIC_BASE_PATH || ''}/`);
  const header = page.locator('header');
  const border = () => header.evaluate(element => {
    const style = getComputedStyle(element);
    return { color: style.borderBottomColor, width: style.borderBottomWidth };
  });

  // At rest: the border box is still there (no layout shift) but paints nothing.
  await expect.poll(async () => (await border()).color).toBe('rgba(0, 0, 0, 0)');
  expect((await border()).width).toBe('1px');

  // The header must be flush against the hero for that to have mattered at all.
  const headerBottom = (await header.boundingBox())!.y + (await header.boundingBox())!.height;
  const hero = page.locator('section').filter({ has: page.getByRole('heading', { name: 'Calor', exact: true }) });
  expect(Math.abs((await hero.boundingBox())!.y - headerBottom)).toBeLessThan(1);

  // Scrolled: a visible border returns.
  await page.evaluate(() => window.scrollTo(0, 900));
  await expect.poll(async () => (await border()).color).not.toBe('rgba(0, 0, 0, 0)');
  expect((await border()).width).toBe('1px');
});
