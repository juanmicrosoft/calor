import { FileCode, Shield, Fingerprint, Layers } from 'lucide-react';

const features = [
  {
    name: 'First-Class Contracts',
    description:
      'Preconditions and postconditions are syntax, not comments. Agents see requirements and guarantees directly.',
    icon: Shield,
    code: '§Q (>= x 0)\n§S (>= result 0)',
  },
  {
    name: 'Explicit Effects',
    description:
      'Declare side effects upfront. No guessing whether a function touches the file system or network.',
    icon: FileCode,
    code: '§E[cw,fr,net]',
  },
  {
    name: 'Unique Identifiers',
    description:
      'Every element has a stable ID. Refactoring changes names, not references.',
    icon: Fingerprint,
    code: '§F[f001:Process:pub]',
  },
  {
    name: 'Explicit Structure',
    description:
      'Matched opening and closing tags. No ambiguous scope boundaries for agents to parse.',
    icon: Layers,
    code: '§M[m001:App]\n  ...\n§/M[m001]',
  },
];

export function FeatureGrid() {
  return (
    <section className="py-24 bg-muted/30">
      <div className="mx-auto max-w-7xl px-6 lg:px-8">
        <div className="mx-auto max-w-2xl text-center">
          <h2 className="text-3xl font-bold tracking-tight sm:text-4xl">
            Designed for Agent Reasoning
          </h2>
          <p className="mt-4 text-lg text-muted-foreground">
            Every design decision optimizes for machine comprehension
          </p>
        </div>

        <div className="mt-16 grid gap-8 sm:grid-cols-2 lg:grid-cols-4">
          {features.map((feature) => {
            const Icon = feature.icon;
            return (
              <div
                key={feature.name}
                className="relative rounded-lg border bg-background p-6 hover:shadow-md transition-shadow"
              >
                <div className="flex h-10 w-10 items-center justify-center rounded-lg bg-primary/10">
                  <Icon className="h-5 w-5 text-primary" />
                </div>
                <h3 className="mt-4 font-semibold">{feature.name}</h3>
                <p className="mt-2 text-sm text-muted-foreground">
                  {feature.description}
                </p>
                <div className="mt-4 rounded bg-zinc-950 p-3">
                  <code className="text-xs text-zinc-300 whitespace-pre">
                    {feature.code}
                  </code>
                </div>
              </div>
            );
          })}
        </div>
      </div>
    </section>
  );
}
