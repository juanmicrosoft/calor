import { expect, test } from '@playwright/test';
import fixture from '../public/data/benchmark-results.json';
import { identifyPrograms } from '../src/lib/benchmark-identity';
import { staticMetricOrder, staticMetricLabels } from '../src/lib/benchmark-labels';

for (const width of [1366, 390]) {
  test(`static evidence labels and provenance remain neutral at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 844 });
    await page.route('https://**/*', route => route.abort());
    const base = process.env.NEXT_PUBLIC_BASE_PATH || '';
    await page.goto(`${base}/`);
    const summary = page.getByRole('region', { name: 'Evidence and its limits' });
    await expect(summary).toContainText('not all behaviorally equivalent');
    await expect(summary).toContainText('not independently recorded');
    await expect(summary).toContainText(String(fixture.programs.length));
    await expect(summary).not.toContainText('wins');
    await summary.getByRole('link', { name: 'Read results and provenance' }).click();
    const article = page.locator('article');
    await expect(article).not.toContainText('demonstrating advantages');
    await expect(article).not.toContainText('Calor Wins');
    await expect(article).not.toContainText('C# better');
    await expect(page.getByRole('columnheader', { name: 'Static-score composite', exact: true })).toBeVisible();
    await expect(page.getByRole('columnheader', { name: 'Token Economics', exact: true })).toHaveCount(1);
    await expect(page.getByRole('columnheader', { name: 'Tokens', exact: true })).toHaveCount(0);
    await expect(article).toContainText('not raw-token savings');
    await expect(page.locator('[data-static-metric]')).toHaveCount(Object.keys(fixture.metrics).length);
    expect([...staticMetricOrder].sort()).toEqual(Object.keys(fixture.metrics).sort());
    expect(await page.locator('[data-static-metric]').evaluateAll(nodes =>
      nodes.map(node => node.getAttribute('data-static-metric')))).toEqual(staticMetricOrder);
    for (const name of staticMetricOrder) {
      const metric = fixture.metrics[name as keyof typeof fixture.metrics];
      const row = page.locator(`[data-static-metric="${name}"]`);
      await expect(row).toContainText(`${metric.ratio.toFixed(2)}x Calor/C#`);
      await expect(row).toContainText(`${staticMetricLabels[name].name} static score`);
    }
    await expect(page.getByRole('note', { name: 'Benchmark provenance' })).toContainText(fixture.commit);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  });
}

test('assembly validates identity without changing corpus IDs or metrics', () => {
  const programs = identifyPrograms(fixture.programs);
  expect(new Set(programs.map(p => p.identity)).size).toBe(217);
  expect(programs.map(({ identity, ...program }) => program)).toEqual(fixture.programs);
  expect(() => identifyPrograms([fixture.programs[0], fixture.programs[0]])).toThrow('Duplicate benchmark program identity');
  expect(identifyPrograms([...fixture.programs].reverse()).map(p => p.identity).reverse())
    .toEqual(programs.map(p => p.identity));
});

test('duplicate corpus IDs preserve exact table membership through sort/filter cycles', async ({ page }) => {
  expect(fixture.programs).toHaveLength(217);
  expect(fixture.programs.filter(p => p.level === 1)).toHaveLength(14);
  for (let id = 50; id <= 59; id++) {
    expect(fixture.programs.filter(p => p.id === String(id).padStart(3, '0'))).toHaveLength(2);
  }
  await page.route('https://**/*', route => route.abort());
  await page.goto(`${process.env.NEXT_PUBLIC_BASE_PATH || ''}/docs/benchmarking/results/`);
  const table = page.locator('table').filter({ has: page.getByRole('columnheader', { name: 'Program', exact: true }) });
  const rows = table.locator('tbody tr');
  for (let cycle = 0; cycle < 4; cycle++) {
    await page.getByRole('button', { name: 'Program', exact: true }).click();
    for (const level of [1, null]) {
      await page.getByRole('button', { name: level === 1 ? 'L1' : 'All', exact: true }).click();
      const expected = fixture.programs.filter(p => level === null || p.level === level)
        .sort((a, b) => cycle % 2 === 0 ? b.name.localeCompare(a.name) : a.name.localeCompare(b.name));
      await expect(rows).toHaveCount(expected.length);
      await expect(rows.locator('td:first-child')).toHaveText(expected.map(p => p.name));
      await expect(rows.locator('td:nth-child(2)')).toHaveText(expected.map(p => String(p.level)));
      await expect(page.getByText(`Showing ${expected.length} of 217 programs.`)).toBeVisible();
    }
  }
});

test('keyboard sort and filter expose state while preserving focus and membership', async ({ page }) => {
  await page.route('https://**/*', route => route.abort());
  await page.goto(`${process.env.NEXT_PUBLIC_BASE_PATH || ''}/docs/benchmarking/results/`);
  const header = page.getByRole('columnheader', { name: 'Program', exact: true });
  const sort = header.getByRole('button');
  await expect(header).toHaveAttribute('aria-sort', 'ascending');
  await sort.focus();
  await page.keyboard.press('Enter');
  await expect(header).toHaveAttribute('aria-sort', 'descending');
  await expect(sort).toBeFocused();
  await page.keyboard.press('Space');
  await expect(header).toHaveAttribute('aria-sort', 'ascending');
  await expect(sort).toBeFocused();
  const l1 = page.getByRole('button', { name: 'L1', exact: true });
  await l1.focus();
  await page.keyboard.press('Space');
  await expect(l1).toHaveAttribute('aria-pressed', 'true');
  await expect(page.getByRole('button', { name: 'All', exact: true })).toHaveAttribute('aria-pressed', 'false');
  const rows = page.locator('table').filter({ has: header }).locator('tbody tr');
  await expect(rows).toHaveCount(14);
  await expect(rows.locator('td:first-child')).toHaveText(
    fixture.programs.filter(p => p.level === 1).sort((a, b) => a.name.localeCompare(b.name)).map(p => p.name)
  );
  await page.getByRole('button', { name: 'Static-score composite', exact: true }).press('Enter');
  await expect(page.getByRole('columnheader', { name: 'Static-score composite', exact: true })).toHaveAttribute('aria-sort', 'ascending');
  await expect(header).not.toHaveAttribute('aria-sort');
});

test('comparison buttons expose exclusive pressed state through keyboard operation', async ({ page }) => {
  await page.route('https://**/*', route => route.abort());
  await page.goto(`${process.env.NEXT_PUBLIC_BASE_PATH || ''}/`);
  const calor = page.getByRole('button', { name: 'Calor - Rules Are Visible' });
  const csharp = page.getByRole('button', { name: 'C# - Rules in Control Flow' });
  await expect(calor).toHaveAttribute('aria-pressed', 'true');
  await csharp.press('Enter');
  await expect(csharp).toHaveAttribute('aria-pressed', 'true');
  await expect(calor).toHaveAttribute('aria-pressed', 'false');
  await expect(page.getByText('Program.cs', { exact: true })).toBeVisible();
  await calor.press('Space');
  await expect(calor).toHaveAttribute('aria-pressed', 'true');
  await expect(csharp).toHaveAttribute('aria-pressed', 'false');
  await expect(page.getByText('program.calr', { exact: true })).toBeVisible();
});
