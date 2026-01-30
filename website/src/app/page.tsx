import Link from 'next/link';
import { Button } from '@/components/ui/button';
import { Hero } from '@/components/landing/Hero';
import { CodeComparison } from '@/components/landing/CodeComparison';
import { BenchmarkChart } from '@/components/landing/BenchmarkChart';
import { FeatureGrid } from '@/components/landing/FeatureGrid';
import { QuickStart } from '@/components/landing/QuickStart';
import { ProjectStatus } from '@/components/landing/ProjectStatus';
import { getBasePath } from '@/lib/utils';

export default function HomePage() {
  return (
    <div className="flex flex-col">
      <Hero />
      <CodeComparison />
      <BenchmarkChart />
      <FeatureGrid />
      <QuickStart />
      <ProjectStatus />
    </div>
  );
}
