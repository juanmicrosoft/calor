import { expect, test } from '@playwright/test';
import { readFile } from 'node:fs/promises';
import { SITE_VERSION } from '../src/lib/version';
import data from '../public/data/benchmark-results.json';
import agents from '../public/data/agent-benchmark-results.json';
import refactoring from '../public/data/agent-refactoring-results.json';
import provenance from '../public/data/benchmark-provenance.json';

test('current version and explicitly historical result provenance cannot silently drift', async () => {
  const props = await readFile('../Directory.Build.props', 'utf8');
  expect(SITE_VERSION).toBe(props.match(/<Version>(.*?)<\/Version>/)![1]);
  expect(provenance.sourceCommit).toBe(data.commit);
  expect(provenance.programCount).toBe(data.programs.length);
  expect([...provenance.metricNames].sort()).toEqual(Object.keys(data.metrics).sort());
  expect(provenance.agentTasks.sourceCommit).toBe(agents.commit);
  expect(provenance.agentRefactoring.sourceCommit).toBe(refactoring.commit);
  const installation = await readFile('content/getting-started/installation.mdx', 'utf8');
  expect(installation).not.toContain('Install or update to v0.12.1');
  const results = await readFile('content/benchmarking/results.mdx', 'utf8');
  expect(results).toContain(provenance.sourceCommit);
  expect(results).toContain(provenance.sourceDeclaredVersion);
  // Historical changelog versions remain valid; this check is deliberately scoped.
  expect(await readFile('content/changelog.mdx', 'utf8')).toContain('0.12');
});

test('readers can distinguish runtime modes, optional proofs and historical measurements', async ({ page }) => {
  await page.route('https://**/*', route => route.abort());
  const base = process.env.NEXT_PUBLIC_BASE_PATH || '';
  await page.goto(`${base}/`);
  const contract = page.getByRole('heading', { name: 'Explicit Contracts', exact: true }).locator('..');
  await expect(contract).toContainText('Optional --verify');
  await expect(contract).toContainText('Runtime checks depend on contract mode');
  await contract.getByRole('link', { name: 'Learn more' }).click();
  await expect(page).toHaveURL(/\/verification-guarantees\/$/);
  const article = page.locator('article');
  for (const text of ['--contract-mode debug', '--contract-mode release', '--contract-mode off',
    '--keep-proven-guards', 'unsupported', 'Parameter mutation and numeric limits', 'NaN']) {
    await expect(article).toContainText(text);
  }
  await page.goto(`${base}/docs/benchmarking/results/`);
  const note = page.getByRole('note', { name: 'Benchmark provenance' });
  await expect(note).toContainText(provenance.sourceCommit);
  await expect(note).toContainText(`v${provenance.sourceDeclaredVersion}`);
  await expect(note).toContainText('not independently recorded');
  await expect(page.getByRole('heading', { name: 'Historical Benchmark Snapshot' })).toBeVisible();
  expect(data.metrics.InformationDensity.winner).toBe('csharp');
  expect(data.metrics.InformationDensity.ratio).toBeLessThan(1);
  await expect(page.locator('article')).toContainText('where C# leads');
});
