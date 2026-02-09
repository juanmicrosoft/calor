#!/usr/bin/env node
/**
 * Generates docs/benchmarking/results.md from benchmark-results.json
 * This script is run by CI/CD after benchmark evaluation
 */

const fs = require('fs');
const path = require('path');

const JSON_PATH = path.join(__dirname, '../website/public/data/benchmark-results.json');
const OUTPUT_PATH = path.join(__dirname, '../docs/benchmarking/results.md');

// Human-readable metric names
const METRIC_NAMES = {
  Comprehension: 'Comprehension',
  ErrorDetection: 'Error Detection',
  EditPrecision: 'Edit Precision',
  RefactoringStability: 'Refactoring Stability',
  GenerationAccuracy: 'Generation Accuracy',
  TaskCompletion: 'Task Completion',
  TokenEconomics: 'Token Economics',
  InformationDensity: 'Information Density',
};

// Metric interpretations
const METRIC_INTERPRETATIONS = {
  Comprehension: { calor: 'Explicit structure aids understanding', csharp: 'Familiar syntax easier to follow' },
  ErrorDetection: { calor: 'Contracts surface invariant violations', csharp: 'Mature error handling ecosystem' },
  EditPrecision: { calor: 'Unique IDs enable targeted changes', csharp: 'Established editing patterns' },
  RefactoringStability: { calor: 'Structural IDs preserve refactoring intent', csharp: 'Better refactoring support' },
  GenerationAccuracy: { calor: 'Better code generation from prompts', csharp: 'Mature tooling, familiar patterns' },
  TaskCompletion: { calor: 'Better task completion rate', csharp: 'Ecosystem maturity advantage' },
  TokenEconomics: { calor: 'More compact representation', csharp: "Calor's explicit syntax uses more tokens" },
  InformationDensity: { calor: 'More semantic content per token', csharp: 'Calor trades density for explicitness' },
};

// Preferred metric order (Calor wins first, then C# wins)
const METRIC_ORDER = [
  'Comprehension',
  'ErrorDetection',
  'EditPrecision',
  'RefactoringStability',
  'GenerationAccuracy',
  'TaskCompletion',
  'TokenEconomics',
  'InformationDensity',
];

function formatRatio(ratio) {
  return ratio.toFixed(2) + 'x';
}

function formatDate(isoString) {
  const date = new Date(isoString);
  return date.toLocaleDateString('en-US', {
    year: 'numeric',
    month: 'long',
    day: 'numeric',
  });
}

function generateMarkdown(data) {
  const { timestamp, commit, summary, metrics, programs } = data;

  // Sort metrics by our preferred order
  const sortedMetrics = METRIC_ORDER
    .filter(key => metrics[key])
    .map(key => ({
      key,
      name: METRIC_NAMES[key] || key,
      ratio: metrics[key].ratio,
      winner: metrics[key].winner,
      interpretation: METRIC_INTERPRETATIONS[key]?.[metrics[key].winner] || '',
    }));

  // Separate Calor wins and C# wins
  const calorWins = sortedMetrics.filter(m => m.winner === 'calor');
  const csharpWins = sortedMetrics.filter(m => m.winner === 'csharp');

  let md = `<!-- THIS FILE IS AUTO-GENERATED. DO NOT EDIT MANUALLY. -->
<!-- Generated from website/public/data/benchmark-results.json by CI/CD -->
<!-- Last generated: ${new Date().toISOString()} -->

---
layout: default
title: Results
parent: Benchmarking
nav_order: 2
---

# Benchmark Results

Evaluated across ${summary.programCount} paired Calor/C# programs.

**Last updated:** ${formatDate(timestamp)}${commit ? ` (commit: ${commit.slice(0, 7)})` : ''}

---

## Summary Table

| Category | Calor vs C# | Winner | Interpretation |
|:---------|:-----------|:-------|:---------------|
`;

  for (const m of sortedMetrics) {
    const ratioStr = m.winner === 'calor' ? `**${formatRatio(m.ratio)}**` : formatRatio(m.ratio);
    const winnerStr = m.winner === 'calor' ? 'Calor' : 'C#';
    md += `| ${m.name} | ${ratioStr} | ${winnerStr} | ${m.interpretation} |\n`;
  }

  md += `
---

## Category Breakdown

### Where Calor Wins

`;

  for (const m of calorWins) {
    md += `#### ${m.name} (${formatRatio(m.ratio)})

${getMetricDescription(m.key, 'calor')}

`;
  }

  md += `---

### Where C# Wins

`;

  for (const m of csharpWins) {
    md += `#### ${m.name} (${formatRatio(m.ratio)})

${getMetricDescription(m.key, 'csharp')}

`;
  }

  md += `---

## The Tradeoff Visualized

\`\`\`
                    Calor better <-  -> C# better
                         |
`;

  for (const m of sortedMetrics) {
    const barWidth = getBarVisualization(m.ratio);
    const nameStr = m.name.padEnd(18);
    const ratioStr = formatRatio(m.ratio);
    md += `${nameStr}${barWidth}  ${ratioStr}\n`;
    if (m.key === 'RefactoringStability' || m.key === 'TaskCompletion') {
      md += `                         |\n`;
    }
  }

  md += `\`\`\`

---

## Key Findings

### 1. Explicitness Has Value

Calor's comprehension advantage suggests explicit structure genuinely aids understanding, even at token cost.

### 2. Contracts Matter

The error detection advantage comes directly from first-class contracts—not from implementation complexity.

### 3. IDs Enable Precision

The edit precision advantage validates the unique ID design decision.

### 4. The Cost is Real

The token economics ratio means Calor consumes more context window. This is the price of explicitness.

### 5. Not a Universal Win

C# still wins on generation and completion metrics, reflecting ecosystem maturity and LLM training data bias toward familiar languages.

---

## When to Use Calor

Based on results, Calor is most valuable when:

- Agent comprehension is critical
- Contract verification matters
- Edit precision is important
- Token budget is flexible

Use C# when:

- Token efficiency is paramount
- Ecosystem libraries are needed
- Human readability is priority

---

## Next

- [Methodology](/calor/benchmarking/methodology/) - How these were measured
- [Individual Metrics](/calor/benchmarking/metrics/comprehension/) - Deep dive into each metric
`;

  return md;
}

function getMetricDescription(key, winner) {
  const descriptions = {
    Comprehension: {
      calor: `Calor's explicit structure provides clear signals for understanding:

| Factor | Calor | C# |
|:-------|:-----|:---|
| Module boundaries | \`§M{id:name}...§/M{id}\` | \`namespace Name { }\` |
| Function signatures | \`§F{id:name:vis}\` with \`§I\`, \`§O\` | Method declarations |
| Side effects | Explicit \`§E{cw,db:rw}\` | Must infer from code |
| Contracts | First-class \`§Q\`, \`§S\` | Comments or assertions |`,
      csharp: 'C# benefits from familiar syntax patterns.',
    },
    ErrorDetection: {
      calor: `Contracts make invariants explicit:

\`\`\`
// Calor: Contracts are syntax
§Q (>= x 0)
§S (>= result 0)

// C#: Contracts are implementation detail
if (x < 0) throw new ArgumentException();
Debug.Assert(result >= 0);
\`\`\``,
      csharp: 'C# benefits from mature error handling patterns.',
    },
    EditPrecision: {
      calor: `Unique IDs enable precise targeting:

\`\`\`
// "Modify loop for1" - unambiguous
§L{for1:i:1:100:1}

// "Modify the for loop" - which one?
for (int i = 0; i < 100; i++)
\`\`\``,
      csharp: 'C# benefits from established editing patterns.',
    },
    RefactoringStability: {
      calor: 'Structural IDs maintain references across refactoring operations, enabling reliable multi-step transformations.',
      csharp: 'C# benefits from mature IDE refactoring support.',
    },
    GenerationAccuracy: {
      calor: 'Calor benefits from explicit structure that reduces generation ambiguity.',
      csharp: 'C# benefits from extensive LLM training data and familiar patterns.',
    },
    TaskCompletion: {
      calor: 'Calor benefits when tasks require understanding code behavior through contracts.',
      csharp: 'C# benefits from ecosystem maturity and token efficiency.',
    },
    TokenEconomics: {
      calor: 'Calor achieves compact representation through Lisp-style expressions.',
      csharp: `C# is more compact:

| Operation | Calor | C# |
|:----------|:-----|:---|
| Return sum | \`§R (+ a b)\` | \`return a + b;\` |
| Print value | \`§P x\` | \`Console.WriteLine(x);\` |
| Function def | 5-7 lines | 3-5 lines |

Average: Calor uses ~1.5x more tokens than C#.`,
    },
    InformationDensity: {
      calor: 'Calor achieves higher density through semantic annotations.',
      csharp: `C# packs more semantic content per token by using implicit information and familiar shorthand.`,
    },
  };

  return descriptions[key]?.[winner] || '';
}

function getBarVisualization(ratio) {
  // Generate a simple ASCII bar chart
  // ratio > 1 means Calor wins, ratio < 1 means C# wins
  const maxBars = 16;
  const center = 8;

  if (ratio >= 1) {
    const bars = Math.min(Math.round((ratio - 1) * 8 + center), maxBars);
    return '█'.repeat(bars) + '░'.repeat(maxBars - bars);
  } else {
    const bars = Math.max(Math.round(ratio * center), 2);
    return '░'.repeat(maxBars - bars) + '█'.repeat(bars);
  }
}

// Main execution
function main() {
  console.log('Generating docs/benchmarking/results.md from benchmark-results.json...');

  if (!fs.existsSync(JSON_PATH)) {
    console.error(`Error: ${JSON_PATH} not found`);
    process.exit(1);
  }

  const jsonContent = fs.readFileSync(JSON_PATH, 'utf-8');
  const data = JSON.parse(jsonContent);

  const markdown = generateMarkdown(data);

  fs.writeFileSync(OUTPUT_PATH, markdown, 'utf-8');
  console.log(`Generated: ${OUTPUT_PATH}`);
  console.log(`  - ${Object.keys(data.metrics).length} metrics`);
  console.log(`  - ${data.summary.programCount} programs`);
  console.log(`  - Timestamp: ${data.timestamp}`);
}

main();
