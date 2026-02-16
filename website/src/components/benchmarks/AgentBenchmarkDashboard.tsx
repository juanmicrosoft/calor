'use client';

import { cn } from '@/lib/utils';
import { Bot, CheckCircle, XCircle, Target, Layers, TrendingUp } from 'lucide-react';
import { useEffect } from 'react';

// Build-time import of agent benchmark data
import agentBenchmarkData from '../../../public/data/agent-benchmark-results.json';

interface CategoryResult {
  name?: string;
  description?: string;
  passed: number;
  total: number;
  rate: number;
}

interface AgentBenchmarkData {
  version: string;
  timestamp: string;
  commit: string;
  testMode: string;
  description?: string;
  summary: {
    totalTasks: number;
    passed: number;
    failed: number;
    passRate: number;
    categoryCount: number;
    threshold: number;
  };
  categories: Record<string, CategoryResult>;
  highlights?: {
    perfectCategories: string[];
    challengingAreas: Array<{ category: string; issue: string }>;
  };
  methodology?: {
    description: string;
    votingMode: string;
    threshold: string;
  };
}

const data = agentBenchmarkData as AgentBenchmarkData;

function formatDate(isoString: string): string {
  try {
    const date = new Date(isoString);
    return date.toLocaleDateString('en-US', {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
    });
  } catch {
    return 'Unknown';
  }
}

interface SummaryCardProps {
  icon: React.ReactNode;
  label: string;
  value: string | number;
  subtext?: string;
  status?: 'success' | 'warning' | 'neutral';
}

function SummaryCard({ icon, label, value, subtext, status = 'neutral' }: SummaryCardProps) {
  return (
    <div
      className={cn(
        'p-4 rounded-lg border',
        status === 'success' && 'border-green-500/50 bg-green-500/5',
        status === 'warning' && 'border-yellow-500/50 bg-yellow-500/5',
        status === 'neutral' && 'border-border bg-muted/30'
      )}
    >
      <div className="flex items-center gap-2 text-muted-foreground mb-2">
        {icon}
        <span className="text-sm">{label}</span>
      </div>
      <div className="text-2xl font-bold">{value}</div>
      {subtext && <div className="text-xs text-muted-foreground mt-1">{subtext}</div>}
    </div>
  );
}

interface CategoryBarProps {
  name: string;
  displayName: string;
  description?: string;
  passed: number;
  total: number;
  rate: number;
}

function CategoryBar({ name, displayName, description, passed, total, rate }: CategoryBarProps) {
  const isPerfect = rate === 100;
  const isGood = rate >= 80;

  return (
    <div className="py-3 border-b border-border/50 last:border-0">
      <div className="flex items-center justify-between mb-2">
        <div>
          <span className="font-medium">{displayName}</span>
          {description && (
            <span className="text-xs text-muted-foreground ml-2">— {description}</span>
          )}
        </div>
        <div className="flex items-center gap-2">
          <span className={cn(
            "text-sm font-mono",
            isPerfect && "text-green-600",
            !isPerfect && isGood && "text-blue-600",
            !isGood && "text-yellow-600"
          )}>
            {passed}/{total}
          </span>
          <span className={cn(
            "text-sm font-bold",
            isPerfect && "text-green-600",
            !isPerfect && isGood && "text-blue-600",
            !isGood && "text-yellow-600"
          )}>
            {rate.toFixed(0)}%
          </span>
        </div>
      </div>
      <div className="h-2 bg-muted rounded-full overflow-hidden">
        <div
          className={cn(
            "h-full rounded-full transition-all",
            isPerfect && "bg-green-500",
            !isPerfect && isGood && "bg-blue-500",
            !isGood && "bg-yellow-500"
          )}
          style={{ width: `${rate}%` }}
        />
      </div>
    </div>
  );
}

export function AgentBenchmarkDashboard() {
  const passRateSuccess = data.summary.passRate >= data.summary.threshold;

  // Sort categories by rate descending
  const sortedCategories = Object.entries(data.categories).sort(
    ([, a], [, b]) => b.rate - a.rate
  );

  // Count perfect and struggling categories
  const perfectCount = sortedCategories.filter(([, c]) => c.rate === 100).length;
  const strugglingCount = sortedCategories.filter(([, c]) => c.rate < 80).length;

  return (
    <div className="space-y-8">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-2xl font-bold flex items-center gap-2">
            <Bot className="h-6 w-6 text-calor-pink" />
            Agent Task Benchmark
          </h2>
          <p className="text-muted-foreground mt-1">
            Testing Claude&apos;s ability to generate correct Calor code from natural language
          </p>
        </div>
        <div className="text-right text-sm text-muted-foreground">
          <div>Last run: {formatDate(data.timestamp)}</div>
          <div>Commit: <code className="text-xs">{data.commit}</code></div>
        </div>
      </div>

      {/* Summary Cards */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
        <SummaryCard
          icon={<Target className="h-4 w-4" />}
          label="Pass Rate"
          value={`${data.summary.passRate.toFixed(1)}%`}
          subtext={passRateSuccess ? "Meets 80% threshold" : "Below 80% threshold"}
          status={passRateSuccess ? 'success' : 'warning'}
        />
        <SummaryCard
          icon={<CheckCircle className="h-4 w-4" />}
          label="Tests Passed"
          value={data.summary.passed}
          subtext={`of ${data.summary.totalTasks} total`}
          status="success"
        />
        <SummaryCard
          icon={<XCircle className="h-4 w-4" />}
          label="Tests Failed"
          value={data.summary.failed}
          status={data.summary.failed > 0 ? 'warning' : 'success'}
        />
        <SummaryCard
          icon={<Layers className="h-4 w-4" />}
          label="Categories"
          value={data.summary.categoryCount}
          subtext={`${perfectCount} at 100%`}
        />
      </div>

      {/* Overall Progress Bar */}
      <div className="p-6 rounded-lg border bg-card">
        <div className="flex items-center justify-between mb-4">
          <h3 className="font-semibold flex items-center gap-2">
            <TrendingUp className="h-5 w-5" />
            Overall Progress
          </h3>
          <div className="flex items-center gap-4">
            <div className="flex items-center gap-1">
              <div className="w-3 h-3 rounded bg-green-500" />
              <span className="text-sm">Passed</span>
            </div>
            <div className="flex items-center gap-1">
              <div className="w-3 h-3 rounded bg-red-400" />
              <span className="text-sm">Failed</span>
            </div>
          </div>
        </div>
        <div className="h-8 bg-muted rounded-lg overflow-hidden flex">
          <div
            className="h-full bg-green-500 transition-all"
            style={{ width: `${data.summary.passRate}%` }}
          />
          <div
            className="h-full bg-red-400"
            style={{ width: `${100 - data.summary.passRate}%` }}
          />
        </div>
        <div className="flex justify-between mt-2 text-sm text-muted-foreground">
          <span>{data.summary.passed} passed</span>
          <span>{data.summary.failed} failed</span>
        </div>
      </div>

      {/* Category Breakdown */}
      <div className="p-6 rounded-lg border bg-card">
        <h3 className="font-semibold mb-4">Results by Category</h3>
        <div className="space-y-1">
          {sortedCategories.map(([key, cat]) => (
            <CategoryBar
              key={key}
              name={key}
              displayName={cat.name || key.replace(/-/g, ' ').replace(/\b\w/g, c => c.toUpperCase())}
              description={cat.description}
              passed={cat.passed}
              total={cat.total}
              rate={cat.rate}
            />
          ))}
        </div>
      </div>

      {/* Highlights */}
      {data.highlights && (
        <div className="grid md:grid-cols-2 gap-4">
          {/* Perfect Categories */}
          <div className="p-6 rounded-lg border bg-green-500/5 border-green-500/20">
            <h3 className="font-semibold text-green-700 dark:text-green-400 mb-3 flex items-center gap-2">
              <CheckCircle className="h-5 w-5" />
              Perfect Score Categories ({data.highlights.perfectCategories.length})
            </h3>
            <div className="flex flex-wrap gap-2">
              {data.highlights.perfectCategories.map(cat => (
                <span
                  key={cat}
                  className="px-2 py-1 bg-green-500/10 text-green-700 dark:text-green-400 rounded text-sm"
                >
                  {cat.replace(/-/g, ' ')}
                </span>
              ))}
            </div>
          </div>

          {/* Challenging Areas */}
          <div className="p-6 rounded-lg border bg-yellow-500/5 border-yellow-500/20">
            <h3 className="font-semibold text-yellow-700 dark:text-yellow-400 mb-3 flex items-center gap-2">
              <XCircle className="h-5 w-5" />
              Areas for Improvement
            </h3>
            <div className="space-y-2">
              {data.highlights.challengingAreas.map(({ category, issue }) => (
                <div key={category} className="text-sm">
                  <span className="font-medium">{category.replace(/-/g, ' ')}</span>
                  <span className="text-muted-foreground"> — {issue}</span>
                </div>
              ))}
            </div>
          </div>
        </div>
      )}

      {/* Methodology */}
      {data.methodology && (
        <div className="p-6 rounded-lg border bg-muted/30">
          <h3 className="font-semibold mb-3">Methodology</h3>
          <p className="text-sm text-muted-foreground mb-4">{data.methodology.description}</p>
          <div className="flex flex-wrap gap-4 text-sm">
            <div>
              <span className="text-muted-foreground">Mode:</span>{' '}
              <span className="font-medium">{data.methodology.votingMode}</span>
            </div>
            <div>
              <span className="text-muted-foreground">Threshold:</span>{' '}
              <span className="font-medium">{data.methodology.threshold}</span>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
