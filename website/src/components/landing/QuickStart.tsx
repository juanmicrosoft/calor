import Link from 'next/link';

const agents = [
  { name: 'Claude Code', command: 'calor init --ai claude' },
  { name: 'Codex', command: 'calor init --ai codex' },
  { name: 'Gemini CLI', command: 'calor init --ai gemini' },
  { name: 'GitHub Copilot', command: 'calor init --ai github' },
];

export function QuickStart() {
  return (
    <section className="py-16" aria-labelledby="quick-start-title">
      <div className="mx-auto max-w-7xl px-6 lg:px-8">
        <div className="mx-auto max-w-3xl">
          <h2 id="quick-start-title" className="text-3xl font-bold tracking-tight sm:text-4xl">
            Get started with your coding agent
          </h2>
          <p className="mt-3 text-muted-foreground font-body">
            Start a new .NET project or open one you already have. Set up Calor,
            then describe what you want your coding agent to build. The agent
            writes the code; you review the result.
          </p>
          <div className="mt-6 rounded-lg border p-5" role="region" aria-labelledby="try-calor-title">
            <h3 id="try-calor-title" className="text-xl font-semibold">Set up once, then ask your agent</h3>
            <p className="mt-3 text-sm text-muted-foreground">
              1. Install Microsoft's .NET 10 developer tools (the SDK) and your
              chosen coding agent. Then install Calor in a terminal:
            </p>
            <pre className="mt-3 overflow-x-auto rounded-lg bg-calor-navy p-4 text-sm text-white"><code>dotnet tool install -g calor</code></pre>
            <p className="mt-4 text-sm text-muted-foreground">
              2. Open a terminal in your existing project folder. Starting fresh?
              Create a project first with these commands, using a new folder name:
            </p>
            <pre className="mt-3 overflow-x-auto rounded-lg bg-calor-navy p-4 text-sm text-white"><code>{`dotnet new console -o MyApp
cd MyApp`}</code></pre>
            <p className="mt-4 text-sm text-muted-foreground">
              3. Run <code>calor init</code> for your agent in that project folder.
              Choose one command below. It sets up Calor's build support and
              gives the agent project instructions and tools.
            </p>
            <div className="mt-3 grid min-w-0 gap-3 sm:grid-cols-2">
              {agents.map(agent => (
                <div key={agent.name} className="min-w-0">
                  <p className="mb-1 text-sm font-medium">{agent.name}</p>
                  <pre className="overflow-x-auto rounded-lg bg-calor-navy p-4 text-sm text-white"><code>{agent.command}</code></pre>
                </div>
              ))}
            </div>
            <p className="mt-4 text-sm text-muted-foreground">
              4. Open your coding agent in the same folder and follow its setup
              and trust prompts. Then ask it for a small change, for example:
            </p>
            <blockquote className="mt-3 border-l-2 border-primary pl-4 text-sm">
              Use Calor to add a function that calculates an order total from
              item prices and quantities. Reject negative prices and quantities.
              Add tests and run the project's build and tests. Leave unrelated
              code unchanged.
            </blockquote>
            <p className="mt-4 text-sm text-muted-foreground">
              You do not need to type Calor code yourself. Review what the
              agent changed, the rules it wrote, and the build and test results.
              Setup guides the agent; it does not guarantee correct code.
            </p>
            <Link className="mt-4 inline-block text-primary underline" href="/docs/getting-started/">
              Follow the complete agent setup
            </Link>
          </div>
        </div>

        <div className="mt-6 mx-auto grid max-w-3xl gap-4 sm:grid-cols-2">
          <div className="rounded-lg border p-5">
            <h3 className="font-semibold">Just getting to know Calor?</h3>
            <p className="mt-2 text-sm text-muted-foreground">
              Learn how setup prepares your coding agent and what to review
              after it makes a change.
            </p>
            <Link className="mt-4 inline-block text-primary underline" href="/docs/getting-started/how-it-works/">
              See how the workflow works
            </Link>
          </div>
          <div className="rounded-lg border p-5">
            <h3 className="font-semibold">Already have a C# project?</h3>
            <p className="mt-2 text-sm text-muted-foreground">
              Keep your project running while you convert to Calor in phases.
              Start with one file; there is no need to convert everything at once.
            </p>
            <Link className="mt-4 inline-block text-primary underline" href="/docs/guides/adding-calor-to-existing-projects/">
              Follow the phased conversion guide
            </Link>
          </div>
        </div>
      </div>
    </section>
  );
}
