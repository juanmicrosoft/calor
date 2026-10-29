import Link from 'next/link';

export function QuickStart() {
  return (
    <section className="py-16">
      <div className="mx-auto max-w-7xl px-6 lg:px-8">
        <div className="mx-auto max-w-3xl">
          <h2 className="text-3xl font-bold tracking-tight sm:text-4xl">
            Choose how you want to explore Calor
          </h2>
          <p className="mt-3 text-muted-foreground font-body">
            You can learn what Calor does before installing anything. To run it
            on your computer, you need Microsoft's .NET 10 developer tools (the
            SDK).
          </p>
        </div>

        <div className="mt-6 mx-auto grid max-w-3xl gap-4 sm:grid-cols-2">
          <div className="rounded-lg border p-5">
            <h3 className="font-semibold">Just getting to know Calor?</h3>
            <p className="mt-2 text-sm text-muted-foreground">
              See what happens when you run a small example. No AI account or
              existing project is needed.
            </p>
            <Link className="mt-4 inline-block text-primary underline" href="/docs/getting-started/hello-world/">
              Follow the guided example
            </Link>
          </div>
          <div className="rounded-lg border p-5">
            <h3 className="font-semibold">Already use .NET?</h3>
            <p className="mt-2 text-sm text-muted-foreground">
              Learn how to add Calor to a project one small piece at a time,
              while keeping your existing C# code.
            </p>
            <Link className="mt-4 inline-block text-primary underline" href="/docs/guides/adding-calor-to-existing-projects/">
              Explore the gradual setup
            </Link>
          </div>
        </div>
      </div>
    </section>
  );
}
