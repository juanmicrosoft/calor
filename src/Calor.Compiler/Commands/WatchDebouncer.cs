using System.Threading.Channels;

namespace Calor.Compiler.Commands;

/// <summary>
/// Debounce logic for <c>calor watch</c>, factored out of the session loop so it is
/// unit-testable without a real <see cref="FileSystemWatcher"/>: editors typically
/// produce bursts of change events per save (truncate + write + metadata), and one
/// rebuild per burst is wanted, not one per event.
/// </summary>
internal static class WatchDebouncer
{
    /// <summary>
    /// Waits for the first change event, then keeps draining events until
    /// <paramref name="quietPeriod"/> elapses with no new ones, and returns the
    /// distinct set of changed paths. Returns null when the channel completes or
    /// <paramref name="cancellationToken"/> is cancelled (clean shutdown).
    /// </summary>
    public static async Task<IReadOnlyCollection<string>?> ReadBatchAsync(
        ChannelReader<string> reader,
        TimeSpan quietPeriod,
        CancellationToken cancellationToken,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        // The quiet-period timer is injectable so tests can drive batch boundaries
        // deterministically instead of racing a real wall clock (see #714); production
        // uses Task.Delay.
        delay ??= static (period, ct) => Task.Delay(period, ct);

        var batch = new HashSet<string>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        try
        {
            // Block until the first event of a burst arrives.
            if (!await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }
            while (reader.TryRead(out var path))
            {
                batch.Add(path);
            }

            // Drain follow-up events until a full quiet period passes.
            while (true)
            {
                var quiet = delay(quietPeriod, cancellationToken);
                var more = reader.WaitToReadAsync(cancellationToken).AsTask();
                var completed = await Task.WhenAny(quiet, more).ConfigureAwait(false);
                if (completed == quiet)
                {
                    await quiet.ConfigureAwait(false); // surface cancellation
                    return batch;
                }

                if (!await more.ConfigureAwait(false))
                {
                    // Channel completed — deliver what we have (or shut down cleanly).
                    return batch.Count > 0 ? batch : null;
                }

                while (reader.TryRead(out var path))
                {
                    batch.Add(path);
                }
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }
}
