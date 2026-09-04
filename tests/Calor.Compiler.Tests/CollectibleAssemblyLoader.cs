using System.Reflection;
using System.Runtime.Loader;

namespace Calor.Compiler.Tests;

/// <summary>
/// <para>
/// Loads generated assemblies into <b>collectible</b> <see cref="AssemblyLoadContext"/>s
/// and unloads them when the owning test class instance is disposed.
/// </para>
/// <para>
/// <b>Why this exists — issue #1150.</b> Several test classes compile Calor to C#,
/// emit it, and execute the result. They did that with
/// <c>Assembly.Load(stream.ToArray())</c>, which loads into the <b>default</b>
/// <see cref="AssemblyLoadContext"/>. The default context is <b>never collectible</b>,
/// so every such load permanently added the assembly's metadata, its loader-heap
/// allocations, and the JIT-compiled code for every method executed. None of that is on
/// the GC heap, none of it is reclaimable, and all of it is anonymous memory.
/// </para>
/// <para>
/// Measured consequence: the <c>tests (compiler)</c> CI shard's test host grew
/// monotonically to <b>9.5 GB</b> on a ~16 GB runner, and the job was killed
/// (<c>exit 143</c>) at a <b>14.7 %</b> rate over one 27-hour window. The split that
/// identified it is in <c>docs/plans/2026-09-03-issue-1150-kill-rate-measurement.md</c>
/// §12: <c>RssAnon</c> 57 MB → 9,260 MB while <c>RssFile</c> moved 89 MB → 106 MB.
/// Four earlier hypotheses — GC policy, a managed reference leak, Z3, and Roslyn's
/// memory-mapped metadata — were each tested and refuted before this one.
/// </para>
/// <para>
/// <b>Lifetime.</b> xUnit constructs a fresh test-class instance per test method and
/// disposes it afterwards, so an instance of this loader unloads each test's assemblies
/// as soon as that test finishes — which is the shortest correct lifetime available
/// without rewriting every call site. <see cref="AssemblyLoadContext.Unload"/> is
/// cooperative rather than immediate: the context is reclaimed once nothing references
/// it. The test method's locals are out of scope by then, so anything a test holds —
/// a <see cref="Type"/>, a <see cref="MethodInfo"/>, an instance — must not outlive the
/// test that created it. Nothing in these suites does.
/// </para>
/// <para>
/// Modelled on <c>Issue769NamespaceTopologyTests</c>'s <c>LoadedAssembly</c>, which
/// already did this correctly in this same project, and on the collectible contexts in
/// <c>Calor.Verification.Tests</c> and <c>Calor.Evaluation</c>.
/// </para>
/// </summary>
internal sealed class CollectibleAssemblyLoader : IDisposable
{
    private readonly List<AssemblyLoadContext> _contexts = [];
    private bool _disposed;

    /// <summary>
    /// Loads <paramref name="image"/> into a fresh collectible context. The context is
    /// unloaded by <see cref="Dispose"/>; callers keep using the returned
    /// <see cref="Assembly"/> exactly as they did with <c>Assembly.Load(byte[])</c>.
    /// </summary>
    /// <param name="image">The emitted assembly image.</param>
    /// <param name="name">
    /// A context name, for debugging only. Callers pass the compilation's assembly name,
    /// which already carries a GUID.
    /// </param>
    public Assembly Load(byte[] image, string name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var context = new AssemblyLoadContext(name, isCollectible: true);
        _contexts.Add(context);

        // LoadFromStream reads from the current position, unlike Assembly.Load(byte[]),
        // so the stream is built here rather than handed in already-consumed.
        using var stream = new MemoryStream(image, writable: false);
        return context.LoadFromStream(stream);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Unload every context this test created. Failures are swallowed deliberately:
        // a test that has already failed must not have its real assertion replaced by a
        // teardown exception, and an un-unloadable context is a leak, not a wrong answer.
        foreach (var context in _contexts)
        {
            try
            {
                context.Unload();
            }
            catch (InvalidOperationException)
            {
                // Not collectible, or already unloaded. Nothing useful to do here.
            }
        }

        _contexts.Clear();
    }
}
