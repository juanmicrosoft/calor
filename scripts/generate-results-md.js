#!/usr/bin/env node
/**
 * Generates docs/benchmarking/results.md from the checked-in website artifact.
 */

const fs = require('fs');
const path = require('path');

const JSON_PATH = path.join(__dirname, '../website/public/data/benchmark-results.json');
const OUTPUT_PATH = path.join(__dirname, '../docs/benchmarking/results.md');

const METRIC_NAMES = {
  Comprehension: 'Comprehension',
  ErrorDetection: 'Error Detection',
  EditPrecision: 'Edit Precision',
  RefactoringStability: 'Refactoring Stability',
  GenerationAccuracy: 'Generation Accuracy',
  TaskCompletion: 'Task Completion',
  TokenEconomics: 'Token Economics',
  InformationDensity: 'Information Density',
  Correctness: 'Correctness',
};

function formatDate(isoString) {
  return new Date(isoString).toISOString().slice(0, 10);
}

function generateMarkdown(data) {
  const metrics = Object.entries(data.metrics);
  const calorParseCount = data.programs.filter(program => program.calorSuccess).length;
  const cSharpParseCount = data.programs.filter(program => program.cSharpSuccess).length;

  let markdown = `<!-- THIS FILE IS AUTO-GENERATED. DO NOT EDIT MANUALLY. -->
<!-- Generated from website/public/data/benchmark-results.json by CI/CD -->

---
layout: default
title: Results
parent: Benchmarking
nav_order: 2
---

# Benchmark Results

This fixed-source artifact contains ${data.summary.programCount} paired programs
and ${data.summary.metricCount} deterministic metrics. It was recorded on
${formatDate(data.timestamp)} from source \`${data.commit}\` with
${data.summary.statisticalRunCount} repetitions.

**Legacy composite direction-normalized ratio:** ${data.summary.overallAdvantage.toFixed(2)}x

Each metric is normalized so values above 1 favor Calor and values below 1
favor C#. Lower-is-better metrics invert their raw score ratio. These static
calculator outputs do not establish a language, coding-agent productivity,
correctness, or safety advantage.

## Metric Ratios

| Metric | Direction-normalized ratio | Favored language | Reported 95% interval |
|:-------|:---------------------------|:-----------------|:----------------------|
`;

  for (const [key, metric] of metrics) {
    const favored = metric.ratio > 1 ? 'Calor' : metric.ratio < 1 ? 'C#' : 'Tie';
    const interval = metric.ci95
      ? `[${metric.ci95[0].toFixed(3)}, ${metric.ci95[1].toFixed(3)}]`
      : 'Not recorded';
    markdown += `| ${METRIC_NAMES[key] || key} | ${metric.ratio.toFixed(2)}x | ${favored} | ${interval} |\n`;
  }

  markdown += `
## Parse Checks

- Calor parser accepted: ${calorParseCount}
- Roslyn syntax parser accepted: ${cSharpParseCount}
- Parse acceptance does not establish generated-code build success or runtime correctness.

## Interpretation Limits

- The repetitions repeat deterministic observations over a fixed corpus; they
  are not independent program samples.
- The source pairs are not all behaviorally equivalent.
- Direction-normalized ratios are not raw Calor/C# score ratios.
- No agent executed these programs as part of this static artifact.

## Next

- [Website methodology](../../website/content/benchmarking/methodology.mdx)
- [Website results and provenance](../../website/content/benchmarking/results.mdx)
`;

  return markdown;
}

function main() {
  if (!fs.existsSync(JSON_PATH)) {
    console.error(`Error: ${JSON_PATH} not found`);
    process.exit(1);
  }

  const data = JSON.parse(fs.readFileSync(JSON_PATH, 'utf8'));
  fs.writeFileSync(OUTPUT_PATH, generateMarkdown(data), 'utf8');
  console.log(`Generated: ${OUTPUT_PATH}`);
  console.log(`  - ${Object.keys(data.metrics).length} metrics`);
  console.log(`  - ${data.summary.programCount} programs`);
  console.log(`  - Timestamp: ${data.timestamp}`);
}

main();
