import { expect, test } from '@playwright/test';

for (const dark of [false, true]) {
  test(`quickstart meaningful text exceeds 4.5:1 (${dark ? 'dark' : 'light'})`, async ({ page, context }) => {
    await context.grantPermissions(['clipboard-read', 'clipboard-write']);
    await page.route('https://**/*', route => route.abort());
    await page.goto(`${process.env.NEXT_PUBLIC_BASE_PATH || ''}/`);
    await page.evaluate(dark => document.documentElement.classList.toggle('dark', dark), dark);
    const terminal = page.getByRole('region', { name: 'Existing-project commands' });
    await terminal.scrollIntoViewIfNeeded();
    const contrast = async () => terminal.evaluate(element => {
      const rgba = (text: string) => (text.match(/[\d.]+/g) || []).map(Number);
      const over = (fg: number[], bg: number[]) => bg.slice(0, 3).map((c, i) => fg[i] * (fg[3] ?? 1) + c * (1 - (fg[3] ?? 1)));
      const lum = (rgb: number[]) => rgb.map(c => c / 255).map(c => c <= .04045 ? c / 12.92 : ((c + .055) / 1.055) ** 2.4)
        .reduce((sum, c, i) => sum + c * [.2126, .7152, .0722][i], 0);
      const navy = rgba(getComputedStyle(element).backgroundColor);
      // Conservatively include the brightest possible cyan inset glow beneath text.
      const background = over([61, 223, 231, .1], navy);
      return Array.from(element.querySelectorAll('p, pre, button, span')).filter(node => node.textContent?.trim()).map(node => {
        const style = getComputedStyle(node);
        const bg = over(rgba(style.backgroundColor), background);
        const fg = over(rgba(style.color), bg);
        // Scanlines darken foreground and background together by up to 15%.
        const ratios = [1, .85].map(factor => (lum(fg.map(c => c * factor)) + .05) / (lum(bg.map(c => c * factor)) + .05));
        return { text: node.textContent, ratio: Math.min(...ratios) };
      });
    });
    for (const state of ['default', 'hover', 'focus', 'copied']) {
      const copy = terminal.getByRole('button').first();
      if (state === 'hover') await copy.hover();
      if (state === 'focus') await copy.focus();
      if (state === 'copied') {
        await copy.click();
        await expect(copy).toHaveText('Copied');
      }
      for (const result of await contrast()) {
        expect(result.ratio, `${state}: ${result.text}`).toBeGreaterThanOrEqual(4.5);
      }
    }
  });
}
