import { expect, test } from '@playwright/test';

for (const viewport of [{ width: 1366, height: 768 }, { width: 390, height: 844 }, { width: 320, height: 740 }]) {
  test(`first action and benefit summary at ${viewport.width}x${viewport.height}`, async ({ page }, testInfo) => {
    await page.setViewportSize(viewport);
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await page.route('https://**/*', route => route.abort());
    await page.goto(`${process.env.NEXT_PUBLIC_BASE_PATH || ''}/`);
    const hero = page.locator('section').filter({ has: page.getByRole('heading', { name: 'Calor', exact: true }) });
    const action = hero.getByRole('link', { name: 'Try a guided example' });
    await expect(hero.getByText('Make AI-written .NET code easier to check and review.')).toBeInViewport();
    const box = await action.boundingBox();
    expect(box).not.toBeNull();
    expect(box!.y + box!.height).toBeLessThanOrEqual(viewport.height);
    expect(box!.x).toBeGreaterThanOrEqual(0);
    expect(box!.x + box!.width).toBeLessThanOrEqual(viewport.width);
    const benefits = page.getByRole('heading', { name: 'A clearer way to work with AI-written code' });
    const benefitsBox = await benefits.boundingBox();
    expect(benefitsBox!.y).toBeLessThanOrEqual(viewport.height * 2);
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBe(viewport.width);
    await page.screenshot({ path: testInfo.outputPath(`${viewport.width}-first-screen.png`) });
    await action.click();
    await expect(page).toHaveURL(/\/docs\/getting-started\/hello-world\/$/);
    await expect(page.getByRole('heading', { name: 'Prerequisites', exact: true })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Run without a project', exact: true })).toBeVisible();
  });
}

for (const width of [1366, 390, 320]) {
  test(`homepage sections stay task-oriented and secondary help stays distinct at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 844 });
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await page.route('https://**/*', route => route.abort());
    await page.goto(`${process.env.NEXT_PUBLIC_BASE_PATH || ''}/`);
    for (const name of ['What changes for your team?', 'Catch mismatches before you rely on a change',
      'An early project, with honest limits', 'Choose how you want to explore Calor', 'Keep exploring']) {
      await expect(page.getByRole('heading', { name, exact: true })).toBeVisible();
    }
    await expect(page.getByRole('heading', { name: 'Built for How AI Actually Writes Code' })).toHaveCount(0);
    await expect(page.getByText('Verified findings default', { exact: true })).toHaveCount(0);
    await expect(page.locator('.animate-pulse-glow')).toHaveCount(0);
    const example = page.getByRole('region', { name: 'Get started: run your first Calor program', exact: true });
    await expect(example).toContainText('dotnet tool install -g calor');
    await expect(example).toContainText('calor run Program.calr');
    await expect(example).toContainText('Hello from Calor!');
    await expect(example).toContainText('§F{');
    await expect(page.locator('main')).not.toContainText('generated C#');
    await page.getByRole('link', { name: 'Follow the phased conversion guide' }).click();
    await expect(page.getByRole('heading', { name: 'Your journey, one phase at a time' })).toBeVisible();
    await expect(page.locator('article')).toContainText('no requirement to convert the project in one pass');
    await page.goBack();
    const help = page.getByRole('region', { name: 'Keep exploring' });
    await expect(help).toContainText('may require a ChatGPT account or plan');
    await expect(help.getByRole('link', { name: 'Ask Calor on ChatGPT (external)' }))
      .toHaveAttribute('rel', 'noopener noreferrer');
    await help.getByRole('link', { name: 'Browse all guides and resources' }).click();
    await expect(page).toHaveURL(/\/docs\/$/);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
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
