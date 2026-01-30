const chapters = [
  {
    number: 1,
    title: 'The Problem',
    message:
      'AI agents are transforming software development. But they\'re forced to adapt to languages designed for human cognition—inferring intent from syntax that was never meant for them. They make mistakes because the language doesn\'t tell them what they need to know.',
    highlights: ['side effects unclear', 'scope boundaries ambiguous', 'cannot determine invariants'],
  },
  {
    number: 2,
    title: 'The Insight',
    message:
      'What if we stopped expecting AI to adapt to our languages? What if the language itself told agents exactly what they need: explicit contracts, declared effects, unique identifiers, unambiguous scope boundaries?',
    highlights: ['explicit > implicit', 'declared > inferred', 'structured > freeform'],
  },
  {
    number: 3,
    title: 'The Solution',
    message:
      'OPAL: the first programming language built from the ground up for AI agent workflows. Contracts are syntax, not comments. Effects are declared, not guessed. Every element has a unique ID. And it compiles to standard .NET—no runtime penalty, full ecosystem access.',
    highlights: ['first-class contracts', 'declared effects', 'unique identifiers'],
  },
  {
    number: 4,
    title: 'The Future',
    message:
      'Languages have always evolved with their users. Assembly gave way to C. C gave way to high-level languages. Now, as AI becomes a primary code author, languages will evolve again—optimized not just for human reading, but for human-AI collaboration.',
    highlights: ['AI-native languages', 'explicit semantics', 'human-AI collaboration'],
  },
];

export function Story() {
  return (
    <section className="py-24 bg-muted/30">
      <div className="mx-auto max-w-7xl px-6 lg:px-8">
        <div className="mx-auto max-w-2xl text-center mb-16">
          <h2 className="text-3xl font-bold tracking-tight sm:text-4xl">
            The Story
          </h2>
          <p className="mt-4 text-lg text-muted-foreground">
            Why we built a language for AI agents
          </p>
        </div>

        <div className="space-y-16">
          {chapters.map((chapter) => (
            <div
              key={chapter.number}
              className="grid grid-cols-1 lg:grid-cols-2 gap-8 items-center"
            >
              {/* Chapter content */}
              <div className={chapter.number % 2 === 0 ? 'lg:order-2' : ''}>
                <div className="flex items-center gap-4 mb-4">
                  <span className="text-sm font-medium text-muted-foreground">
                    Chapter {chapter.number}
                  </span>
                  <div className="h-px flex-1 bg-border" />
                </div>
                <h3 className="text-2xl font-semibold mb-4">
                  {chapter.title}
                </h3>
                <p className="text-muted-foreground leading-relaxed">
                  {chapter.message}
                </p>
              </div>

              {/* Highlights card */}
              <div className={chapter.number % 2 === 0 ? 'lg:order-1' : ''}>
                <div className="rounded-lg border bg-card p-6">
                  <div className="space-y-3">
                    {chapter.highlights.map((highlight, idx) => (
                      <div
                        key={idx}
                        className="flex items-center gap-3"
                      >
                        <div className="h-2 w-2 rounded-full bg-primary" />
                        <span className="font-mono text-sm text-muted-foreground">
                          {highlight}
                        </span>
                      </div>
                    ))}
                  </div>
                </div>
              </div>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}
