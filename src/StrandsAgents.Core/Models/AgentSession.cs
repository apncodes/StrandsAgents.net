namespace StrandsAgents.Core;

/// <summary>Persisted agent session — conversation history + state.</summary>
/// <param name="SessionId">Unique identifier for this session.</param>
/// <param name="Messages">Full conversation history snapshot.</param>
/// <param name="State">Key-value state bag snapshot.</param>
/// <param name="LastUpdated">UTC timestamp of the last save.</param>
/// <param name="ExpiresAt">
/// Optional UTC expiry time. When set, session managers will treat the session as
/// deleted once <c>DateTimeOffset.UtcNow &gt;= ExpiresAt</c>. <c>null</c> means no expiry.
/// Must be strictly greater than <paramref name="LastUpdated"/> when provided.
/// </param>
/// <exception cref="ArgumentException">
/// Thrown when <paramref name="ExpiresAt"/> is not null and is not strictly greater than
/// <paramref name="LastUpdated"/>.
/// </exception>
public record AgentSession(
    string SessionId,
    IReadOnlyList<Message> Messages,
    IReadOnlyDictionary<string, object?> State,
    DateTimeOffset LastUpdated,
    DateTimeOffset? ExpiresAt = null)
{
    /// <summary>
    /// Validates that <see cref="ExpiresAt"/>, when set, is strictly after <see cref="LastUpdated"/>.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; } = ExpiresAt is { } exp && exp <= LastUpdated
        ? throw new ArgumentException(
            $"ExpiresAt ({exp:O}) must be strictly greater than LastUpdated ({LastUpdated:O}).",
            nameof(ExpiresAt))
        : ExpiresAt;
}
