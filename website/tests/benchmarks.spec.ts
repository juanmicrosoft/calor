import { expect, test } from '@playwright/test';
import fixture from '../public/data/benchmark-results.json';
import { identifyPrograms } from '../src/lib/benchmark-identity';

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
    await page.getByRole('columnheader', { name: 'Program', exact: true }).click();
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
