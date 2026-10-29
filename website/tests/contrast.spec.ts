import { expect, test } from '@playwright/test';

for (const dark of [false, true]) {
  test(`getting-started text exceeds 4.5:1 contrast (${dark ? 'dark' : 'light'})`, async ({ page }) => {
    await page.route('https://**/*', route => route.abort());
    await page.goto(`${process.env.NEXT_PUBLIC_BASE_PATH || ''}/`);
    await page.evaluate(dark => document.documentElement.classList.toggle('dark', dark), dark);
    const choices = page.getByRole('heading', { name: 'Choose how you want to explore Calor' }).locator('..').locator('..');
    const contrast = () => choices.locator('p, a').evaluateAll(nodes => {
      const channels = (color: string) => (color.match(/[\d.]+/g) || []).map(Number);
      const over = (foreground: number[], background: number[]) =>
        background.slice(0, 3).map((channel, index) =>
          foreground[index] * (foreground[3] ?? 1) + channel * (1 - (foreground[3] ?? 1)));
      const luminance = (rgb: number[]) => rgb.map(channel => channel / 255)
        .map(channel => channel <= .04045 ? channel / 12.92 : ((channel + .055) / 1.055) ** 2.4)
        .reduce((sum, channel, index) => sum + channel * [.2126, .7152, .0722][index], 0);

      return nodes.filter(node => node.textContent?.trim()).map(node => {
        const foreground = channels(getComputedStyle(node).color);
        let background = [255, 255, 255, 1];
        for (let ancestor: Element | null = node; ancestor; ancestor = ancestor.parentElement) {
          const candidate = channels(getComputedStyle(ancestor).backgroundColor);
          if ((candidate[3] ?? 1) > 0) {
            background = candidate;
            break;
          }
        }
        const visibleForeground = over(foreground, background);
        const foregroundLuminance = luminance(visibleForeground);
        const backgroundLuminance = luminance(background.slice(0, 3));
        return {
          text: node.textContent,
          ratio: (Math.max(foregroundLuminance, backgroundLuminance) + .05)
            / (Math.min(foregroundLuminance, backgroundLuminance) + .05),
        };
      });
    });

    const link = choices.getByRole('link').first();
    for (const state of ['default', 'hover', 'focus']) {
      if (state === 'hover') await link.hover();
      if (state === 'focus') await link.focus();
      for (const result of await contrast()) {
        expect(result.ratio, `${state}: ${result.text}`).toBeGreaterThanOrEqual(4.5);
      }
    }
  });
}
