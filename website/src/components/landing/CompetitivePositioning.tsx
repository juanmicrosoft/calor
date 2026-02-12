const comparisons = [
  {
    title: 'vs C# / Java / TypeScript',
    subtitle: 'General-purpose languages',
    rows: [
      {
        feature: 'Contracts',
        calor: 'First-class syntax (§Q, §S)',
        others: 'Deprecated / Comments / None',
      },
      {
        feature: 'Side Effects',
        calor: 'Declared explicitly (§E)',
        others: 'Implicit, inferred from I/O',
      },
      {
        feature: 'References',
        calor: 'Unique IDs (f001, m001)',
        others: 'Line numbers, fragile paths',
      },
      {
        feature: 'Scope Boundaries',
        calor: 'Matched open/close tags',
        others: 'Braces, indentation rules',
      },
      {
        feature: 'Agent Optimization',
        calor: 'Primary design goal',
        others: 'Not considered',
      },
    ],
  },
  {
    title: 'vs Rust',
    subtitle: 'Systems language with safety guarantees',
    rows: [
      {
        feature: 'Primary Focus',
        calor: 'AI agent collaboration',
        others: 'Memory safety, zero-cost abstractions',
      },
      {
        feature: 'Learning Curve',
        calor: '.NET ecosystem, familiar concepts',
        others: 'Ownership model, lifetimes, borrowing',
      },
      {
        feature: 'Contracts',
        calor: 'Native preconditions/postconditions',
        others: 'Via debug_assert! or external crates',
      },
      {
        feature: 'Target Use Case',
        calor: 'AI-generated business logic',
        others: 'Systems programming, performance-critical',
      },
    ],
  },
  {
    title: 'vs Eiffel / Ada / SPARK',
    subtitle: 'Design-by-Contract languages',
    rows: [
      {
        feature: 'Modern Ecosystem',
        calor: '.NET, NuGet, full interop',
        others: 'Limited package ecosystem',
      },
      {
        feature: 'Adoption Path',
        calor: 'Interop with existing C# code',
        others: 'Full rewrite typically required',
      },
      {
        feature: 'AI Optimization',
        calor: 'Unique IDs, explicit structure',
        others: 'Human-readable syntax only',
      },
      {
        feature: 'Industry Adoption',
        calor: 'Emerging, .NET compatible',
        others: 'Niche, specialized domains',
      },
    ],
  },
  {
    title: 'vs LEAN / Isabelle / Rocq',
    subtitle: 'Proof assistants for theorem proving',
    rows: [
      {
        feature: 'Primary Purpose',
        calor: 'Verified software engineering',
        others: 'Mathematical theorem proving',
      },
      {
        feature: 'Developer Effort',
        calor: 'Write contracts, Z3 verifies automatically',
        others: 'Write proofs (tactics, lemmas, induction)',
      },
      {
        feature: 'Target Domain',
        calor: 'Business logic, APIs, .NET apps',
        others: 'Math libraries, cryptography, compilers',
      },
      {
        feature: 'When Verification Fails',
        calor: 'Falls back to runtime check (safe)',
        others: 'Blocks compilation until proof complete',
      },
      {
        feature: 'Learning Curve',
        calor: 'Familiar .NET concepts',
        others: 'Type theory, proof tactics',
      },
    ],
  },
  {
    title: 'vs Constrained Decoding (LLGuidance)',
    subtitle: 'Grammar-based generation vs semantic verification',
    rows: [
      {
        feature: 'Output Guarantee',
        calor: 'Syntactically and semantically correct',
        others: 'Syntactically correct only',
      },
      {
        feature: 'Contract Verification',
        calor: 'Z3 proves contracts at compile time',
        others: 'No contract awareness',
      },
      {
        feature: 'Effect Tracking',
        calor: 'Side effects declared and enforced',
        others: 'Side effects implicit',
      },
      {
        feature: 'Division by Zero',
        calor: 'Caught at compile time via §Q',
        others: 'Runtime crash',
      },
      {
        feature: 'Invariant Violations',
        calor: 'Counterexample from Z3',
        others: 'Silent bug in production',
      },
    ],
  },
];

export function CompetitivePositioning() {
  return (
    <section className="py-24">
      <div className="mx-auto max-w-7xl px-6 lg:px-8">
        <div className="mx-auto max-w-2xl text-center mb-16">
          <h2 className="text-3xl font-bold tracking-tight sm:text-4xl">
            How Calor Compares
          </h2>
          <p className="mt-4 text-lg text-muted-foreground">
            See how Calor stacks up against existing solutions
          </p>
        </div>

        <div className="space-y-12">
          {comparisons.map((comparison, idx) => (
            <div
              key={idx}
              className="rounded-lg border bg-card overflow-hidden"
            >
              {/* Header */}
              <div className="border-b px-6 py-4">
                <h3 className="text-xl font-semibold">
                  {comparison.title}
                </h3>
                <p className="text-sm text-muted-foreground mt-1">
                  {comparison.subtitle}
                </p>
              </div>

              {/* Table */}
              <div className="overflow-x-auto">
                <table className="w-full">
                  <thead>
                    <tr className="border-b bg-muted/50">
                      <th className="px-6 py-3 text-left text-sm font-medium text-muted-foreground">
                        Feature
                      </th>
                      <th className="px-6 py-3 text-left text-sm font-medium text-primary">
                        Calor
                      </th>
                      <th className="px-6 py-3 text-left text-sm font-medium text-muted-foreground">
                        Others
                      </th>
                    </tr>
                  </thead>
                  <tbody className="divide-y">
                    {comparison.rows.map((row, rowIdx) => (
                      <tr key={rowIdx}>
                        <td className="px-6 py-3 text-sm font-medium">
                          {row.feature}
                        </td>
                        <td className="px-6 py-3 text-sm text-foreground">
                          {row.calor}
                        </td>
                        <td className="px-6 py-3 text-sm text-muted-foreground">
                          {row.others}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}
