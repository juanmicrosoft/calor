#!/usr/bin/env node
/**
 * #1157 defect 2 — refuse to publish a weaker measurement under the same key.
 *
 * The benchmark workflow writes `website/public/data/benchmark-results.json` from a STATIC run
 * (`statisticalRunCount: 0`) and only writes the 30-run result when `statistical_runs` is set, to a
 * DIFFERENT file. So an ordinary bot run overwrites a 30-run figure with a single-run one under the
 * same `overallAdvantage` key, and the PR body says "Updated benchmark-results.json with latest
 * metrics". A reviewer reads 1.32 → 1.28 as a regression in Calor's advantage. It is not: the two
 * numbers are not measuring the same thing.
 *
 * Measured at 74ba4973: committed = 30 runs / 1.32, a fresh generator run = 0 runs / 1.28, same
 * eight metrics. Nothing in the pipeline noticed.
 *
 * This script compares the candidate file against the committed one and exits non-zero when the
 * candidate is WEAKER — fewer statistical runs, or a changed metric set — so the swap has to be
 * deliberate. `--allow-weaker` performs it anyway and prints what is being given up, which is the
 * escape hatch for a deliberate methodology change.
 *
 *   node scripts/check-benchmark-methodology.js <candidate.json> [--baseline <path>] [--allow-weaker]
 */

const fs = require('fs');
const path = require('path');

const DEFAULT_BASELINE = path.join(
  __dirname, '..', 'website', 'public', 'data', 'benchmark-results.json');

function read(file) {
  if (!fs.existsSync(file)) return null;
  return JSON.parse(fs.readFileSync(file, 'utf-8'));
}

function summarise(doc) {
  const s = (doc && doc.summary) || {};
  return {
    runs: s.statisticalRunCount ?? 0,
    advantage: s.overallAdvantage,
    metrics: Object.keys((doc && doc.metrics) || {}).sort(),
  };
}

function main() {
  const args = process.argv.slice(2);
  const allowWeaker = args.includes('--allow-weaker');
  const baselineIndex = args.indexOf('--baseline');
  const baselinePath = baselineIndex >= 0 ? args[baselineIndex + 1] : DEFAULT_BASELINE;
  const candidatePath = args.find((a, i) =>
    !a.startsWith('--') && (baselineIndex < 0 || i !== baselineIndex + 1));

  if (!candidatePath) {
    console.error('usage: check-benchmark-methodology.js <candidate.json> [--baseline <path>] [--allow-weaker]');
    process.exit(2);
  }

  const candidate = read(candidatePath);
  if (!candidate) {
    console.error(`candidate not found: ${candidatePath}`);
    process.exit(2);
  }

  const baseline = read(baselinePath);
  if (!baseline) {
    console.log(`no baseline at ${baselinePath}; nothing to compare against.`);
    process.exit(0);
  }

  const a = summarise(baseline);
  const b = summarise(candidate);
  const problems = [];

  if (b.runs < a.runs) {
    problems.push(
      `statisticalRunCount would go ${a.runs} -> ${b.runs}. The published overallAdvantage `
      + `(${a.advantage} -> ${b.advantage}) would then be a different KIND of measurement under the `
      + `same key, and the change would read as a movement in Calor's advantage.`);
  }

  const added = b.metrics.filter(m => !a.metrics.includes(m));
  const removed = a.metrics.filter(m => !b.metrics.includes(m));
  if (added.length || removed.length) {
    problems.push(
      `the metric set changed (${a.metrics.length} -> ${b.metrics.length}`
      + `${added.length ? `; added ${added.join(', ')}` : ''}`
      + `${removed.length ? `; removed ${removed.join(', ')}` : ''}). `
      + `overallAdvantage is a geometric mean over these, so the two figures are not comparable.`);
  }

  console.log(`baseline : ${a.runs} run(s), advantage ${a.advantage}, ${a.metrics.length} metrics`);
  console.log(`candidate: ${b.runs} run(s), advantage ${b.advantage}, ${b.metrics.length} metrics`);

  if (problems.length === 0) {
    console.log('OK: the candidate is not a weaker measurement.');
    process.exit(0);
  }

  const verb = allowWeaker ? 'ALLOWED (--allow-weaker)' : 'REFUSED';
  console.error(`\n${verb}: ${problems.length} problem(s) — #1157\n`);
  for (const problem of problems) console.error(`  - ${problem}`);
  if (allowWeaker) {
    console.error(
      '\nProceeding because --allow-weaker was passed. State the before/after '
      + 'statisticalRunCount and metric set in the PR body, or gate 16 will read the website and '
      + 'the changelog as disagreeing.');
    process.exit(0);
  }
  console.error(
    '\nRe-run with --statistical --runs 30 to produce a comparable result, or pass --allow-weaker '
    + 'if the methodology change is deliberate and will be stated in the PR body and CHANGELOG.');
  process.exit(1);
}

main();
