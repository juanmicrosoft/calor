import { expect, test } from '@playwright/test';
import { readFile } from 'node:fs/promises';
import { SITE_VERSION } from '../src/lib/version';
import data from '../public/data/benchmark-results.json';
import agents from '../public/data/agent-benchmark-results.json';
import refactoring from '../public/data/agent-refactoring-results.json';
import provenance from '../public/data/benchmark-provenance.json';

const metricPages = ['comprehension', 'correctness', 'edit-precision', 'error-detection',
  'generation-accuracy', 'information-density', 'refactoring-stability', 'token-economics'];
const verificationPages = ['philosophy/static-verification', 'syntax-reference/contracts',
  'cli/compile', 'cli/verify', 'benchmarking/metrics/contract-verification'];

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
  for (const path of ['methodology', ...metricPages.map(name => `metrics/${name}`)]) {
    const source = await readFile(`content/benchmarking/${path}.mdx`, 'utf8');
    expect(source).not.toMatch(/v0\.12(?:\.1)?\s+(?:dashboard|result|aggregate|ratio|corpus)/i);
    expect(source).toContain('/docs/benchmarking/results/#read-the-results-carefully');
  }
  for (const path of verificationPages) {
    const source = await readFile(`content/${path}.mdx`, 'utf8');
    expect(source).not.toMatch(/always keep|every guard|all other guards remain|always kept/i);
    expect(source).toContain('--contract-mode off');
  }
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
    '--keep-proven-guards', 'unsupported', 'Parameter mutation and numeric limits', 'NaN',
    'early and nested returns', 'shared postcondition exit', 'Calor1001', 'Calor1004',
    'preserves unknown operands', 'Runtime quantifier limits', 'Calor0326',
    'no silent static-only fallback', 'Contract proofs do not establish effect completeness',
    'including compound assignments, charge mut', 'not universally complete effect checking']) {
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

for (const width of [1366, 390]) {
  test(`linked current references preserve safety boundaries and attribution at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 844 });
    await page.route('https://**/*', route => route.abort());
    const base = process.env.NEXT_PUBLIC_BASE_PATH || '';
    await page.goto(`${base}/docs/benchmarking/results/`);
    await page.locator('article a[href$="/docs/benchmarking/methodology/"]').click();
    await expect(page).toHaveURL(/\/methodology\/$/);
    await expect(page.locator('article')).toContainText(provenance.sourceCommit);
    await expect(page.locator('article')).toContainText(provenance.sourceDeclaredVersion);
    await expect(page.locator('article')).toContainText('not independently recorded');
    for (const name of metricPages) {
      await page.goto(`${base}/docs/benchmarking/metrics/${name}/`);
      await page.locator('article').getByRole('link', { name: 'snapshot provenance and limits', exact: true }).click();
      await expect(page).toHaveURL(/\/results\/#read-the-results-carefully$/);
      await expect(page.getByRole('heading', { name: 'Read the Results Carefully', exact: true })).toBeInViewport();
      await expect(page.locator('article')).toContainText('where C# leads');
    }
    for (const path of verificationPages) {
      await page.goto(`${base}/docs/${path}/`);
      await expect(page.locator('article')).toContainText('--contract-mode off');
      if (path.endsWith('static-verification') || path.endsWith('contract-verification')) {
        await expect(page.locator('article')).toContainText('46341');
        await expect(page.locator('article')).toContainText('-2147479015');
        await expect(page.locator('article')).toContainText('Calor1004');
      }
    }
  });
}
