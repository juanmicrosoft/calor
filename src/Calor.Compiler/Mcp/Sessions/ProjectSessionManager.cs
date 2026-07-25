namespace Calor.Compiler.Mcp.Sessions;

/// <summary>
/// Owns the live <see cref="ProjectSession"/> instances for one MCP server
/// process (loop plan WS2 D2.1). The stdio server has a single client, but
/// tool calls can run concurrently under the handler's semaphore, so access
/// is locked.
/// </summary>
internal sealed class ProjectSessionManager
{
    internal const int MaxSessions = 16;

    private readonly Dictionary<string, ProjectSession> _sessions = new(StringComparer.Ordinal);
    private readonly object _sync = new();

    /// <summary>
    /// Opens a session over a directory. Throws
    /// <see cref="InvalidOperationException"/> when the session cap or the
    /// per-session file cap is exceeded.
    /// </summary>
    public ProjectSession Open(string rootDirectory)
    {
        lock (_sync)
        {
            if (_sessions.Count >= MaxSessions)
                throw new InvalidOperationException(
                    $"Session limit of {MaxSessions} reached — close a session with calor_session_close first");

            var id = $"cs-{Guid.NewGuid():N}"[..15];
            var session = ProjectSession.Open(id, rootDirectory);
            _sessions[session.Id] = session;
            return session;
        }
    }

    public ProjectSession? Get(string sessionId)
    {
        lock (_sync)
        {
            return _sessions.TryGetValue(sessionId, out var session) ? session : null;
        }
    }

    public bool Close(string sessionId)
    {
        lock (_sync)
        {
            return _sessions.Remove(sessionId);
        }
    }

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _sessions.Count;
            }
        }
    }
}
