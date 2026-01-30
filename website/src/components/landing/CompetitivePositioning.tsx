const comparisons = [
  {
    title: 'vs C# / Java / TypeScript',
    subtitle: 'General-purpose languages',
    rows: [
      {
        feature: 'Contracts',
        opal: 'First-class syntax (§Q, §S)',
        others: 'Deprecated / Comments / None',
      },
      {
        feature: 'Side Effects',
        opal: 'Declared explicitly (§E)',
        others: 'Implicit, inferred from I/O',
      },
      {
        feature: 'References',
        opal: 'Unique IDs (f001, m001)',
        others: 'Line numbers, fragile paths',
      },
      {
        feature: 'Scope Boundaries',
        opal: 'Matched open/close tags',
        others: 'Braces, indentation rules',
      },
      {
        feature: 'Agent Optimization',
        opal: 'Primary design goal',
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
        opal: 'AI agent collaboration',
        others: 'Memory safety, zero-cost abstractions',
      },
      {
        feature: 'Learning Curve',
        opal: '.NET ecosystem, familiar concepts',
        others: 'Ownership model, lifetimes, borrowing',
      },
      {
        feature: 'Contracts',
        opal: 'Native preconditions/postconditions',
        others: 'Via debug_assert! or external crates',
      },
      {
        feature: 'Target Use Case',
        opal: 'AI-generated business logic',
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
        opal: '.NET, NuGet, full interop',
        others: 'Limited package ecosystem',
      },
      {
        feature: 'Adoption Path',
        opal: 'Interop with existing C# code',
        others: 'Full rewrite typically required',
      },
      {
        feature: 'AI Optimization',
        opal: 'Unique IDs, explicit structure',
        others: 'Human-readable syntax only',
      },
      {
        feature: 'Industry Adoption',
        opal: 'Emerging, .NET compatible',
        others: 'Niche, specialized domains',
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
            How OPAL Compares
          </h2>
          <p className="mt-4 text-lg text-muted-foreground">
            See how OPAL stacks up against existing solutions
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
                        OPAL
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
                          {row.opal}
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
