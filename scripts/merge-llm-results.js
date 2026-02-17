#!/usr/bin/env node
/**
 * Merges LLM benchmark results into the main benchmark-results.json
 *
 * This script reads the output from:
 * - llm-tasks (TaskCompletion)
 * - safety-benchmark (Safety)
 * - effect-discipline (EffectDiscipline)
 *
 * And updates the metrics in benchmark-results.json
 */

const fs = require('fs');
const path = require('path');

const BENCHMARK_RESULTS_PATH = path.join(__dirname, '../website/public/data/benchmark-results.json');
const LLM_RESULTS_DIR = path.join(__dirname, '../website/public/data');

// Map of LLM result files to metric keys
const LLM_RESULT_FILES = {
  'llm-results.json': 'TaskCompletion',
  'safety-results.json': 'Safety',
  'effect-discipline-results.json': 'EffectDiscipline',
};

function loadJson(filePath) {
  if (!fs.existsSync(filePath)) {
    return null;
  }
  const content = fs.readFileSync(filePath, 'utf-8');
  return JSON.parse(content);
}

function extractMetricFromLlmResults(results, metricKey) {
  if (!results || !results.summary) {
    return null;
  }

  const summary = results.summary;

  // Calculate advantage ratio based on the metric
  let ratio;
  let winner;

  if (metricKey === 'TaskCompletion') {
    const calorScore = summary.averageCalorScore || summary.AverageCalorScore || 0;
    const csharpScore = summary.averageCSharpScore || summary.AverageCSharpScore || 0;
    ratio = csharpScore > 0 ? calorScore / csharpScore : 1.0;
    winner = ratio >= 1.0 ? 'calor' : 'csharp';
  } else if (metricKey === 'Safety') {
    const calorScore = summary.averageCalorSafetyScore || summary.AverageCalorSafetyScore || 0;
    const csharpScore = summary.averageCSharpSafetyScore || summary.AverageCSharpSafetyScore || 0;
    ratio = csharpScore > 0 ? calorScore / csharpScore : 1.0;
    winner = ratio >= 1.0 ? 'calor' : 'csharp';
  } else if (metricKey === 'EffectDiscipline') {
    const calorScore = summary.averageCalorDisciplineScore || summary.AverageCalorDisciplineScore || 0;
    const csharpScore = summary.averageCSharpDisciplineScore || summary.AverageCSharpDisciplineScore || 0;
    ratio = csharpScore > 0 ? calorScore / csharpScore : 1.0;
    // Effect discipline is outcome-based, so close to 1.0 is a tie
    if (Math.abs(ratio - 1.0) < 0.05) {
      winner = 'tie';
      ratio = 1.0;
    } else {
      winner = ratio >= 1.0 ? 'calor' : 'csharp';
    }
  }

  return {
    ratio: Math.round(ratio * 100) / 100, // Round to 2 decimal places
    winner,
  };
}

function updateSummary(benchmarkResults) {
  const metrics = benchmarkResults.metrics;
  let calorWins = 0;
  let csharpWins = 0;
  let ties = 0;

  for (const [key, value] of Object.entries(metrics)) {
    if (value.winner === 'calor') calorWins++;
    else if (value.winner === 'csharp') csharpWins++;
    else ties++;
  }

  // Calculate overall advantage (geometric mean of ratios)
  const ratios = Object.values(metrics).map(m => m.ratio).filter(r => r > 0);
  const geoMean = Math.pow(ratios.reduce((a, b) => a * b, 1), 1 / ratios.length);

  benchmarkResults.summary.calorWins = calorWins;
  benchmarkResults.summary.cSharpWins = csharpWins;
  benchmarkResults.summary.overallAdvantage = Math.round(geoMean * 100) / 100;
  benchmarkResults.summary.metricCount = Object.keys(metrics).length;

  return benchmarkResults;
}

function main() {
  console.log('Merging LLM benchmark results into benchmark-results.json...');

  // Load main benchmark results
  const benchmarkResults = loadJson(BENCHMARK_RESULTS_PATH);
  if (!benchmarkResults) {
    console.error(`Error: ${BENCHMARK_RESULTS_PATH} not found`);
    process.exit(1);
  }

  let updatedCount = 0;

  // Process each LLM result file
  for (const [filename, metricKey] of Object.entries(LLM_RESULT_FILES)) {
    const filePath = path.join(LLM_RESULTS_DIR, filename);
    const llmResults = loadJson(filePath);

    if (!llmResults) {
      console.log(`  - ${filename}: Not found, skipping ${metricKey}`);
      continue;
    }

    const metric = extractMetricFromLlmResults(llmResults, metricKey);
    if (!metric) {
      console.log(`  - ${filename}: Could not extract ${metricKey} metric`);
      continue;
    }

    // Update the metric in benchmark results
    benchmarkResults.metrics[metricKey] = metric;
    console.log(`  - ${metricKey}: ratio=${metric.ratio}, winner=${metric.winner}`);
    updatedCount++;
  }

  if (updatedCount === 0) {
    console.log('No LLM results found to merge.');
    process.exit(0);
  }

  // Update summary statistics
  updateSummary(benchmarkResults);

  // Update timestamp
  benchmarkResults.timestamp = new Date().toISOString();

  // Write back
  fs.writeFileSync(BENCHMARK_RESULTS_PATH, JSON.stringify(benchmarkResults, null, 2), 'utf-8');
  console.log(`\nUpdated ${BENCHMARK_RESULTS_PATH}`);
  console.log(`  - ${updatedCount} LLM metrics merged`);
  console.log(`  - Overall advantage: ${benchmarkResults.summary.overallAdvantage}x`);
  console.log(`  - Calor wins: ${benchmarkResults.summary.calorWins}`);
  console.log(`  - C# wins: ${benchmarkResults.summary.cSharpWins}`);
}

main();
