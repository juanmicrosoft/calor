import { expect, test } from '@playwright/test';
import { stat } from 'node:fs/promises';

const base = process.env.NEXT_PUBLIC_BASE_PATH || '';
test('cold mobile brand transfer stays within explicit asset budgets', async ({ browser }, testInfo) => {
  const context = await browser.newContext({ viewport: { width: 390, height: 844 } });
  await context.route('https://**/*', route => route.abort());
  const page = await context.newPage();
  const session = await context.newCDPSession(page);
  await session.send('Network.enable');
  await session.send('Network.setCacheDisabled', { cacheDisabled: true });
  await session.send('Network.emulateNetworkConditions', {
    offline: false, latency: 20, downloadThroughput: 5 * 1024 * 1024, uploadThroughput: 1024 * 1024,
  });
  await page.goto(`http://127.0.0.1:4173${base}/`, { waitUntil: 'networkidle' });
  const transfers = await page.evaluate(() => performance.getEntriesByType('resource')
    .filter(entry => /calor-logo|calor-lava|og-image|favicon/.test(entry.name))
    .map(entry => ({ name: new URL(entry.name).pathname, bytes: (entry as PerformanceResourceTiming).transferSize })));
  console.log(JSON.stringify({ coldMobileBrandTransfers: transfers }));
  await testInfo.attach('cold-mobile-transfers', { body: JSON.stringify(transfers, null, 2), contentType: 'application/json' });
  await page.screenshot({ path: testInfo.outputPath('mobile-brand.png') });
  if (!process.env.ASSET_BASELINE) {
    expect(transfers.some(entry => /calor-logo\.png|\.mp4/.test(entry.name))).toBe(false);
    expect(transfers.filter(entry => /logo/.test(entry.name)).reduce((total, entry) => total + entry.bytes, 0)).toBeLessThan(50_000);
    expect((await stat('public/calor-logo-64.webp')).size).toBeLessThan(8_000);
    expect((await stat('public/calor-logo-256.webp')).size).toBeLessThan(40_000);
    expect((await stat('src/app/favicon.ico')).size).toBeLessThan(8_000);
    expect((await stat('public/calor-lava-poster.jpg')).size).toBeLessThan(50_000);
    const logos = page.getByRole('img', { name: 'Calor logo', exact: true });
    for (const logo of await logos.all()) {
      expect(await logo.evaluate(image => (image as HTMLImageElement).naturalWidth)).toBeGreaterThan(0);
    }
  }
  await context.close();
});

test('video is an opt-in enhancement and never requested under motion or data constraints', async ({ browser }) => {
  for (const constrained of ['none', 'motion', 'save-data', 'slow-network']) {
    const context = await browser.newContext({ viewport: { width: 1366, height: 768 },
      reducedMotion: constrained === 'motion' ? 'reduce' : 'no-preference' });
    await context.route('https://**/*', route => route.abort());
    if (constrained === 'save-data' || constrained === 'slow-network') {
      await context.addInitScript(value => Object.defineProperty(navigator, 'connection', {
        value: { saveData: value === 'save-data', effectiveType: value === 'slow-network' ? '2g' : '4g',
          addEventListener() {}, removeEventListener() {} },
      }), constrained);
    }
    const page = await context.newPage();
    const videos: string[] = [];
    page.on('request', request => { if (request.url().endsWith('.mp4')) videos.push(request.url()); });
    await page.goto(`http://127.0.0.1:4173${base}/`);
    const play = page.getByRole('button', { name: 'Play background animation' });
    if (constrained === 'none') {
      await expect(play).toBeVisible();
      expect(videos).toEqual([]);
      await play.click();
      await expect(page.locator('video')).toBeVisible();
      await expect.poll(() => videos.length).toBeGreaterThan(0);
      await page.getByRole('button', { name: 'Pause background animation' }).click();
      await expect(page.locator('video')).toHaveCount(0);
    } else {
      await expect(play).toHaveCount(0);
      // Hydrated menu is available while decoration remains absent.
      await page.locator('header').getByRole('button', { name: 'Dark mode' }).click();
      expect(videos).toEqual([]);
    }
    await context.close();
  }
});
