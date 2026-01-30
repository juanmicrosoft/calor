import { Hero } from '@/components/landing/Hero';
import { Story } from '@/components/landing/Story';
import { CodeComparison } from '@/components/landing/CodeComparison';
import { BenchmarkChart } from '@/components/landing/BenchmarkChart';
import { CompetitivePositioning } from '@/components/landing/CompetitivePositioning';
import { FeatureGrid } from '@/components/landing/FeatureGrid';
import { QuickStart } from '@/components/landing/QuickStart';
import { ProjectStatus } from '@/components/landing/ProjectStatus';

export default function HomePage() {
  return (
    <div className="flex flex-col">
      <Hero />
      <Story />
      <CodeComparison />
      <BenchmarkChart />
      <CompetitivePositioning />
      <FeatureGrid />
      <QuickStart />
      <ProjectStatus />
    </div>
  );
}
