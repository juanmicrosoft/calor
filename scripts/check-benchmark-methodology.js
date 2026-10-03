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
 * deliberate.
 *
 * #1422 (0.24 B2): benchmark.yml no longer publishes this file; scripts/benchmark_publication_gate.py
 * gates the only published headline. The former `--allow-weaker` override is removed: a method
 * change needs a merged #1407 amendment, never a flag.
 *
 *   node scripts/check-benchmark-methodology.js <candidate.json> [--baseline <path>]
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
  const baselineIndex = args.indexOf('--baseline');
  const baselinePath = baselineIndex >= 0 ? args[baselineIndex + 1] : DEFAULT_BASELINE;
  const candidatePath = args.find((a, i) =>
    !a.startsWith('--') && (baselineIndex < 0 || i !== baselineIndex + 1));

  if (!candidatePath) {
    console.error('usage: check-benchmark-methodology.js <candidate.json> [--baseline <path>]');
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

  console.error(`\nREFUSED: ${problems.length} problem(s) — #1157\n`);
  for (const problem of problems) console.error(`  - ${problem}`);
  console.error(
    '\nRe-run with --statistical --runs 30 to produce a comparable result. A deliberate method '
    + 'change needs a merged #1407 amendment first (#1422); there is no override.');
  process.exit(1);
}

main();
