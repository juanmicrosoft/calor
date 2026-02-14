#!/usr/bin/env python3
"""
Compare benchmark results against a baseline.

Usage:
    python compare-benchmarks.py --baseline results.json --current new-results.json
    python compare-benchmarks.py --baseline results.json --current new-results.json --fail-on-regression 0.05
"""

import argparse
import json
import sys
from pathlib import Path


def load_json(path: str) -> dict:
    """Load JSON from a file path."""
    with open(path, 'r') as f:
        return json.load(f)


def get_metric_ratio(data: dict, metric: str) -> float | None:
    """Extract the ratio for a specific metric from benchmark results."""
    # Handle effect discipline results format
    if 'summary' in data:
        if metric == 'EffectDiscipline':
            return data['summary'].get('disciplineAdvantageRatio')
        elif metric == 'Safety':
            return data['summary'].get('safetyAdvantageRatio')

    # Handle standard benchmark results format
    if 'metrics' in data:
        metric_data = data['metrics'].get(metric, {})
        if isinstance(metric_data, dict):
            return metric_data.get('ratio')
        return metric_data

    return None


def compare_results(baseline: dict, current: dict, metric: str | None = None) -> dict:
    """Compare current results against baseline."""
    comparisons = {}

    if metric:
        # Compare specific metric
        baseline_ratio = get_metric_ratio(baseline, metric)
        current_ratio = get_metric_ratio(current, metric)

        if baseline_ratio is not None and current_ratio is not None:
            change = current_ratio - baseline_ratio
            change_pct = (change / baseline_ratio) * 100 if baseline_ratio != 0 else 0

            comparisons[metric] = {
                'baseline': baseline_ratio,
                'current': current_ratio,
                'change': change,
                'change_pct': change_pct,
                'improved': change > 0
            }
    else:
        # Compare all metrics
        if 'metrics' in baseline and 'metrics' in current:
            for metric_name in baseline['metrics']:
                baseline_ratio = get_metric_ratio(baseline, metric_name)
                current_ratio = get_metric_ratio(current, metric_name)

                if baseline_ratio is not None and current_ratio is not None:
                    change = current_ratio - baseline_ratio
                    change_pct = (change / baseline_ratio) * 100 if baseline_ratio != 0 else 0

                    comparisons[metric_name] = {
                        'baseline': baseline_ratio,
                        'current': current_ratio,
                        'change': change,
                        'change_pct': change_pct,
                        'improved': change > 0
                    }

    return comparisons


def print_comparison(comparisons: dict):
    """Print comparison results in a readable format."""
    print("\nBenchmark Comparison Results")
    print("=" * 60)
    print(f"{'Metric':<25} {'Baseline':>10} {'Current':>10} {'Change':>10}")
    print("-" * 60)

    for metric, data in comparisons.items():
        change_str = f"{data['change']:+.3f}"
        if data['change_pct'] != 0:
            change_str += f" ({data['change_pct']:+.1f}%)"

        indicator = "+" if data['improved'] else "-" if data['change'] < 0 else " "

        print(f"{indicator} {metric:<23} {data['baseline']:>10.3f} {data['current']:>10.3f} {change_str:>10}")

    print("-" * 60)


def check_regression(comparisons: dict, threshold: float) -> list[str]:
    """Check if any metrics have regressed beyond the threshold."""
    regressions = []

    for metric, data in comparisons.items():
        # A regression means the ratio dropped (Calor advantage decreased)
        if data['change'] < 0 and abs(data['change_pct']) > threshold * 100:
            regressions.append(f"{metric}: {data['baseline']:.3f} -> {data['current']:.3f} ({data['change_pct']:.1f}%)")

    return regressions


def main():
    parser = argparse.ArgumentParser(description='Compare benchmark results against a baseline')
    parser.add_argument('--baseline', required=True, help='Path to baseline results JSON')
    parser.add_argument('--current', required=True, help='Path to current results JSON')
    parser.add_argument('--metric', help='Specific metric to compare (default: all)')
    parser.add_argument('--fail-on-regression', type=float, default=None,
                       help='Fail if any metric regresses by more than this percentage (e.g., 0.05 for 5%%)')
    parser.add_argument('--output', help='Output file for comparison JSON')

    args = parser.parse_args()

    # Load data
    try:
        baseline = load_json(args.baseline)
    except FileNotFoundError:
        print(f"Error: Baseline file not found: {args.baseline}")
        sys.exit(1)
    except json.JSONDecodeError as e:
        print(f"Error: Invalid JSON in baseline file: {e}")
        sys.exit(1)

    try:
        current = load_json(args.current)
    except FileNotFoundError:
        print(f"Error: Current file not found: {args.current}")
        sys.exit(1)
    except json.JSONDecodeError as e:
        print(f"Error: Invalid JSON in current file: {e}")
        sys.exit(1)

    # Compare
    comparisons = compare_results(baseline, current, args.metric)

    if not comparisons:
        print("No comparable metrics found")
        sys.exit(1)

    # Print results
    print_comparison(comparisons)

    # Output to file if requested
    if args.output:
        with open(args.output, 'w') as f:
            json.dump(comparisons, f, indent=2)
        print(f"\nComparison saved to: {args.output}")

    # Check for regressions
    if args.fail_on_regression is not None:
        regressions = check_regression(comparisons, args.fail_on_regression)
        if regressions:
            print(f"\nERROR: Regressions detected (threshold: {args.fail_on_regression * 100:.1f}%):")
            for regression in regressions:
                print(f"  - {regression}")
            sys.exit(1)
        else:
            print(f"\nNo regressions detected (threshold: {args.fail_on_regression * 100:.1f}%)")

    sys.exit(0)


if __name__ == '__main__':
    main()
