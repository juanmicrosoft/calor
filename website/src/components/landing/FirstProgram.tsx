export function FirstProgram() {
  return (
    <section aria-labelledby="first-program-title" className="mx-auto w-full max-w-5xl px-6 py-12 lg:px-8">
      <h2 id="first-program-title" className="text-2xl font-bold">A clearer way to work with AI-written code</h2>
      <p className="mt-3 max-w-3xl text-muted-foreground">
        Calor is a programming language for .NET teams that use AI coding assistants. It makes expected behavior easier to see and gives you checks to review before you rely on a change.
      </p>
      <div className="mt-6 grid gap-4 sm:grid-cols-3">
        <div className="rounded-lg border p-5">
          <h3 className="font-semibold">Start with your goal</h3>
          <p className="mt-2 text-sm text-muted-foreground">Explain the change you want in everyday language. You do not need to start by learning Calor’s notation.</p>
        </div>
        <div className="rounded-lg border p-5">
          <h3 className="font-semibold">Make expectations visible</h3>
          <p className="mt-2 text-sm text-muted-foreground">Calor lets a project record important rules, such as what a function needs and what it promises to return.</p>
        </div>
        <div className="rounded-lg border p-5">
          <h3 className="font-semibold">Use familiar .NET tools</h3>
          <p className="mt-2 text-sm text-muted-foreground">Calor applications run on the .NET runtime. Build and test them with familiar .NET tools.</p>
        </div>
      </div>
    </section>
  );
}
