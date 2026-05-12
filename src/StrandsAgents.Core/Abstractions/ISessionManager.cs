namespace StrandsAgents.Core;

/// <summary>Persists and restores agent sessions across process restarts.</summary>
public interface ISessionManager
{
    /// <summary>Loads a session by ID. Returns <c>null</c> when not found or expired.</summary>
    Task<AgentSession?> LoadAsync(string sessionId, CancellationToken ct = default);

    /// <summary>Saves (creates or overwrites) a session.</summary>
    Task SaveAsync(string sessionId, AgentSession session, CancellationToken ct = default);

    /// <summary>
    /// Deletes a session by ID. Idempotent — does not throw when the session does not exist.
    /// </summary>
    Task DeleteAsync(string sessionId, CancellationToken ct = default);
}
