import { Hero } from '@/components/landing/Hero';
import { CodeComparison } from '@/components/landing/CodeComparison';
import { CatchBugs } from '@/components/landing/CatchBugs';
import { FeatureGrid } from '@/components/landing/FeatureGrid';
import { BenchmarkChart } from '@/components/landing/BenchmarkChart';
import { QuickStart } from '@/components/landing/QuickStart';
import { ProjectStatus } from '@/components/landing/ProjectStatus';

export default function HomePage() {
  return (
    <div className="flex flex-col">
      <Hero />
      <CodeComparison />
      <CatchBugs />
      <FeatureGrid />
      <BenchmarkChart />
      <QuickStart />
      <ProjectStatus />
    </div>
  );
}
