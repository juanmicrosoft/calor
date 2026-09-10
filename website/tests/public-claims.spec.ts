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
const currentRelease = '0.19.0';

test('effect-rows outcome publishes a no-run disposition without substituting historical data', async ({ page }) => {
  const ledger = JSON.parse(await readFile('../bench/phase0-agent-native/effect-rows-benefit-ledger.json', 'utf8'));
  expect(ledger.epochRun).toBe(false);
  expect(ledger.verdict).toBe('UNDERPOWERED');
  expect(ledger.legA).toBeNull();
  expect(ledger.legB).toBeNull();
  const base = process.env.NEXT_PUBLIC_BASE_PATH || '';
  await page.route('https://**/*', route => route.abort());
  for (const path of ['benchmarking', 'benchmarking/agent-tasks', 'benchmarking/metrics/effect-discipline']) {
    await page.goto(`${base}/docs/${path}/`);
    await page.locator('article a[href$="/docs/benchmarking/results/#redesigned-pp-w-rows-deferred"]').click();
    await expect(page.getByRole('heading', { name: 'Redesigned PP-W-rows: deferred' })).toBeInViewport();
    await expect(page.locator('article')).toContainText('no redesigned confirmatory result exists');
    await expect(page.locator('article')).toContainText('Null means uncollected');
    await expect(page.locator('article')).toContainText('two cost-eligible task pairs (12 runs)');
  }
});

test('effect-rows methodology separates registration, tooling, observations and approval', async ({ page }) => {
  const source = await readFile('content/benchmarking/effect-rows-study.mdx', 'utf8');
  expect(source).toContain('16880d006db760d7b47d829fd4282b033c3158a0');
  for (const rule of ['R1', 'R2', 'R3', 'R4', 'R5', 'R6', 'R7', 'R8']) {
    expect(source).toContain(`| ${rule} |`);
  }
  await page.route('https://**/*', route => route.abort());
  const base = process.env.NEXT_PUBLIC_BASE_PATH || '';
  for (const path of ['benchmarking', 'benchmarking/methodology', 'benchmarking/metrics/effect-discipline']) {
    await page.goto(`${base}/docs/${path}/`);
    await page.locator('article a[href$="/docs/benchmarking/effect-rows-study/"]').click();
    await expect(page).toHaveURL(/\/effect-rows-study\/$/);
    for (const text of ['UNADJUDICATED', 'administrative stop', 'No redesigned confirmatory result',
      'below 50%', 'UNDERPOWERED-CARRIED', 'not implemented evidence']) {
      await expect(page.locator('article')).toContainText(text);
    }
  }
});

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
  expect(results).toContain('not all behaviorally equivalent');
  for (const path of ['philosophy/index', 'philosophy/tradeoffs']) {
    const source = await readFile(`content/${path}.mdx`, 'utf8');
    const densityClaims = source.split('\n').filter(line =>
      line.includes('Information Density') && /\d+\.\d+x/.test(line));
    expect(densityClaims.length).toBeGreaterThan(0);
    for (const claim of densityClaims) {
      expect(claim).toContain(`${data.metrics.InformationDensity.ratio.toFixed(2)}x`);
    }
    expect(source.replace(/\s+/g, ' ')).toContain('not all behaviorally equivalent');
  }
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

test('research milestones stay distinct from software releases', async () => {
  const status = await readFile('content/benchmarking/evidence-status.mdx', 'utf8');
  const props = await readFile('../Directory.Build.props', 'utf8');
  const packageJson = JSON.parse(await readFile('package.json', 'utf8')) as { version: string };
  const banner = await readFile('src/components/landing/WhatsNewBanner.tsx', 'utf8');
  const changelog = await readFile('content/changelog.mdx', 'utf8');
  expect(status).toContain('UNADJUDICATED');
  expect(status).toContain('administrative stop');
  expect(status).toContain('did not itself change `Directory.Build.props`');
  expect(status).toContain('At the M0 closeout, the current compiler remained v0.19.0');
  expect(status).toContain('/docs/changelog/');
  expect(props).toContain(`<Version>${currentRelease}</Version>`);
  expect(SITE_VERSION).toBe(currentRelease);
  expect(packageJson.version).toBe(currentRelease);
  expect(banner).toContain(`v${currentRelease}`);
  expect(changelog.match(/^## \[([^\]]+)\]/m)?.[1]).toBe('Unreleased');
  expect(changelog.match(/^## \[(\d+\.\d+\.\d+)\]/m)?.[1]).toBe(currentRelease);
  for (const path of ['benchmarking/index', 'benchmarking/results', 'guides/adoption-playbook']) {
    const source = await readFile(`content/${path}.mdx`, 'utf8');
    expect(source).toContain('/docs/benchmarking/evidence-status/');
  }
});

test('adoption guidance separates compiler behavior from workflow evidence', async () => {
  const howItWorks = await readFile('content/getting-started/how-it-works.mdx', 'utf8');
  const adoption = await readFile('content/guides/adoption-playbook.mdx', 'utf8');
  const philosophy = await readFile('content/philosophy/index.mdx', 'utf8');
  const tradeoffs = await readFile('content/philosophy/tradeoffs.mdx', 'utf8');
  const normalizedHow = howItWorks.replace(/\s+/g, ' ');
  const normalizedAdoption = adoption.replace(/\s+/g, ' ');
  const normalizedPhilosophy = philosophy.replace(/\s+/g, ' ');
  const normalizedTradeoffs = tradeoffs.replace(/\s+/g, ' ');
  expect(howItWorks).not.toContain('creates a reliable system, not a hopeful one');
  expect(howItWorks).not.toContain('typically writes Calor correctly');
  expect(howItWorks).not.toContain('automatically retries with the correct');
  expect(howItWorks).not.toContain('AI Models Are Excellent Learners');
  expect(howItWorks).not.toContain('more robust than either alone');
  expect(normalizedHow).toContain('did not test the full feedback loop');
  expect(normalizedHow).toContain('not a model-wide reliability result');
  expect(normalizedHow).toContain('does not guarantee a correct retry');
  expect(normalizedAdoption).toContain('Workflow savings are unmeasured');
  expect(normalizedAdoption).toContain('not established a qualifying adopter');
  expect(normalizedAdoption).toContain('Only a clean, non-vacuous, assumption-free `proven`');
  expect(normalizedPhilosophy).toContain('No qualifying adopter, independent adopter handoff');
  expect(normalizedPhilosophy).toContain('economic advantage');
  expect(philosophy).not.toContain('Interoperate seamlessly');
  expect(philosophy).not.toContain('existing CI/CD pipeline works');
  expect(philosophy).not.toContain('reliable AI workflows');
  expect(normalizedTradeoffs).toContain('not evidence that it reduces total cost or defects');
  expect(normalizedTradeoffs).toContain('Whether that improves real editing accuracy remains a workload-dependent hypothesis');
  expect(tradeoffs).not.toContain('tradeoff pays off');
  for (const source of [howItWorks, adoption, philosophy, tradeoffs]) {
    expect(source).toContain('/docs/benchmarking/evidence-status/');
  }
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
  await expect(page.getByRole('heading', { name: 'Static Benchmark Snapshot' })).toBeVisible();
  await expect(note).toContainText('not an agent-productivity measurement');
  await expect(note).toContainText('not all behaviorally equivalent');
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
        await expect(page.locator('article')).toContainText('2147395600');
        await expect(page.locator('article')).toContainText('OverflowException');
        await expect(page.locator('article')).not.toContainText('-2147479015');
        await expect(page.locator('article')).toContainText('Calor1004');
        if (path.endsWith('static-verification')) {
          await expect(page.locator('article')).toContainText('module overflow policy');
        } else {
          await expect(page.locator('article')).toContainText('not a new measurement of historical benchmarks');
        }
        await page.locator('article').getByRole('link', { name: 'integer overflow policy', exact: true }).click();
        await expect(page).toHaveURL(/\/verification-guarantees\/#integer-overflow-policy$/);
        await expect(page.getByRole('heading', { name: 'Integer overflow policy', exact: true })).toBeInViewport();
        await expect(page.locator('article')).toContainText('overflow=unchecked');
        await expect(page.locator('article')).toContainText('checked-arithmetic');
        await expect(page.locator('article')).toContainText('disables contract checks, not this overflow exception');
      }
    }
  });
}
