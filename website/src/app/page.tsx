import { Hero } from '@/components/landing/Hero';
import { FirstProgram } from '@/components/landing/FirstProgram';
import { CodeComparison } from '@/components/landing/CodeComparison';
import { CatchBugs } from '@/components/landing/CatchBugs';
import { BenchmarkChart } from '@/components/landing/BenchmarkChart';
import { QuickStart } from '@/components/landing/QuickStart';
import { AskCalor } from '@/components/landing/AskCalor';
import { ScrollDepthTracker } from '@/components/landing/ScrollDepthTracker';

export default function HomePage() {
  return (
    <div className="flex flex-col">
      <ScrollDepthTracker />
      <Hero />
      <FirstProgram />
      <CodeComparison />
      <CatchBugs />
      <BenchmarkChart />
      <QuickStart />
      <AskCalor />
    </div>
  );
}
