import { expect, test } from '@playwright/test';
import { stat } from 'node:fs/promises';
import { readFile } from 'node:fs/promises';
import { createServer } from 'node:http';
import type { AddressInfo } from 'node:net';

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

// The decorative video plays by default on an unconstrained desktop, and is never
// requested otherwise. This changed from opt-in on 2026-09-09 at the maintainer's
// direction: the gate below (desktop, no reduced-motion, no Save-Data, not 2g/3g) was
// already doing the protective work, and requiring a click on top of it meant the
// animation effectively never ran. The cost accepted with that decision is the ~2 MB
// fetch on every visit that passes the gate; the constrained branches below are what
// keep it from reaching anyone else, and they are unchanged.
test('video plays by default on unconstrained desktop, and is never requested under motion or data constraints', async ({ browser }) => {
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
      // Plays without interaction, and the control offers the opposite action.
      await expect(page.locator('video')).toBeVisible();
      await expect(page.getByRole('button', { name: 'Pause background animation' })).toBeVisible();
      await expect.poll(() => videos.length).toBeGreaterThan(0);
      // It must actually be PLAYING, not merely mounted and fetched. Both assertions
      // above held in a real Chrome while the video sat at paused/currentTime 0 at
      // readyState 4 — the `autoPlay` attribute is skipped when the window is not
      // OS-focused — so on their own they do not observe the thing this test is named
      // for. Advancing currentTime is what does.
      //
      // Scope, stated plainly: Playwright always runs a focused page, so this cannot
      // reproduce the unfocused-window case itself. That case was verified by hand in
      // Chrome (document.hasFocus() === false, currentTime advancing) after Hero.tsx
      // stopped relying on the attribute alone. What CI pins is the weaker but still
      // useful claim: a regression to "mounts but never starts" fails here.
      const video = page.locator('video');
      await expect.poll(() => video.evaluate(element => !(element as HTMLVideoElement).paused),
        { message: 'video must start on its own' }).toBe(true);
      await expect.poll(() => video.evaluate(element => (element as HTMLVideoElement).currentTime),
        { message: 'playback must actually advance' }).toBeGreaterThan(0);
      // Pausing is honoured. Once the whole file has arrived the element is RETAINED and
      // simply paused: resuming must be immediate and must not re-fetch. It used to
      // unmount unconditionally, so every resume re-downloaded the entire 2 MB —
      // measured as a second request for all 2,093,841 bytes — which on a real
      // connection is seconds of frozen poster after pressing play. The other case,
      // pausing while bytes are still in flight, still tears down and abandons the
      // transfer; that is what the abort test below covers.
      // Wait for the transfer to actually finish first — that is the case being
      // described. Pausing before it does is the OTHER case, and is meant to tear down.
      await expect.poll(() => video.evaluate(element => {
        const media = element as HTMLVideoElement;
        return media.buffered.length > 0 && Number.isFinite(media.duration)
          && media.buffered.end(media.buffered.length - 1) >= media.duration - 0.25;
      }), { message: 'the video must finish downloading before this case applies' }).toBe(true);
      await page.getByRole('button', { name: 'Pause background animation' }).click();
      await expect.poll(() => video.evaluate(element => (element as HTMLVideoElement).paused),
        { message: 'pause must stop playback' }).toBe(true);
      const fetchedBeforeResume = videos.length;
      await page.getByRole('button', { name: 'Play background animation' }).click();
      await expect.poll(() => video.evaluate(element => !(element as HTMLVideoElement).paused),
        { message: 'resume must play again' }).toBe(true);
      await expect.poll(() => video.evaluate(element => (element as HTMLVideoElement).currentTime),
        { message: 'resumed playback must advance' }).toBeGreaterThan(0);
      expect(videos.length, 'resuming must not re-download the video').toBe(fetchedBeforeResume);
      // And a deliberate pause STAYS paused. Drive the gate away and back — this
      // branch has no navigator.connection to dispatch on, so use the media query the
      // effect actually listens to; a connection event here would be a no-op and the
      // assertion would pass without testing anything.
      await page.emulateMedia({ reducedMotion: 'reduce' });
      await expect(play).toHaveCount(0);
      // A constraint discards the element outright, retained-and-paused or not.
      await expect(page.locator('video')).toHaveCount(0);
      await page.emulateMedia({ reducedMotion: 'no-preference' });
      await expect(play).toBeVisible();
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

test('pausing or activating constraints aborts an in-flight media download', async ({ browser }) => {
  const media = await readFile('public/calor-lava.mp4');
  for (const action of ['pause', 'motion', 'narrow', 'save-data']) {
    let sent = 0;
    let aborted = false;
    const server = createServer((request, response) => {
      response.writeHead(200, { 'Content-Type': 'video/mp4', 'Content-Length': media.length });
      const interval = setInterval(() => {
        const end = Math.min(sent + 32768, media.length);
        response.write(media.subarray(sent, end));
        sent = end;
        if (sent === media.length) { clearInterval(interval); response.end(); }
      }, 30);
      response.on('close', () => { clearInterval(interval); aborted = sent < media.length; });
    });
    await new Promise<void>(resolve => server.listen(0, '127.0.0.1', resolve));
    const context = await browser.newContext({ viewport: { width: 1366, height: 768 }, reducedMotion: 'no-preference' });
    try {
      await context.route('https://**/*', route => route.abort());
      await context.addInitScript(() => {
        const connection = Object.assign(new EventTarget(), { saveData: false, effectiveType: '4g' });
        Object.defineProperty(navigator, 'connection', { value: connection });
      });
      await context.route('**/calor-lava.mp4', route =>
        route.continue({ url: `http://127.0.0.1:${(server.address() as AddressInfo).port}/video.mp4` }));
      const page = await context.newPage();
      await page.goto(`http://127.0.0.1:4173${base}/`);
      // No click: the video autoplays on an unconstrained desktop, so the download is
      // already in flight. What this test protects is unchanged — that pausing, or any
      // constraint appearing mid-download, aborts it rather than letting it complete.
      await expect.poll(() => sent).toBeGreaterThan(65536);
      if (action === 'pause') await page.getByRole('button', { name: 'Pause background animation' }).click();
      if (action === 'motion') await page.emulateMedia({ reducedMotion: 'reduce' });
      if (action === 'narrow') await page.setViewportSize({ width: 390, height: 844 });
      if (action === 'save-data') await page.evaluate(() => {
        const connection = (navigator as Navigator & { connection: EventTarget & { saveData: boolean } }).connection;
        connection.saveData = true;
        connection.dispatchEvent(new Event('change'));
      });
      await expect(page.locator('video')).toHaveCount(0);
      await expect.poll(() => aborted, { message: `${action} must abort the outstanding download` }).toBe(true);
      expect(sent).toBeLessThan(media.length);
    } finally {
      await context.close();
      server.closeAllConnections();
      await new Promise<void>(resolve => server.close(() => resolve()));
    }
  }
});
