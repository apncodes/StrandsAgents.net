using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using StrandsAgents.Core;

namespace StrandsAgents.Runtime.Session;

/// <summary>
/// <see cref="ISessionManager"/> implementation backed by Amazon Bedrock AgentCore Memory.
///
/// <para>
/// Automatically persists full conversation history and agent state to AgentCore Memory
/// on every <see cref="SaveAsync"/> call, and restores it on <see cref="LoadAsync"/>.
/// Expired sessions (where <c>ExpiresAt &lt;= UtcNow</c>) are deleted and treated as
/// not found.
/// </para>
///
/// <para>
/// All HTTP requests are signed with AWS SigV4 using credentials resolved from the
/// standard AWS credential chain. Pass a <paramref name="clientOverride"/> to inject a
/// plain <see cref="HttpClient"/> for unit testing (bypasses SigV4).
/// </para>
/// </summary>
public sealed class AgentCoreSessionManager : ISessionManager, IAsyncDisposable
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly string _memoryId;
    private readonly bool _ownsClient;

    /// <summary>
    /// Initialises a new <see cref="AgentCoreSessionManager"/>.
    /// </summary>
    /// <param name="memoryId">The AgentCore memory resource ID for session storage.</param>
    /// <param name="region">AWS region. Default: <c>us-east-1</c>.</param>
    /// <param name="clientOverride">
    /// Optional pre-configured <see cref="HttpClient"/>. When provided, the manager does
    /// not own the client and will not dispose it. Intended for testing — bypasses SigV4.
    /// </param>
    public AgentCoreSessionManager(
        string memoryId,
        string region = "us-east-1",
        HttpClient? clientOverride = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memoryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(region);

        _memoryId = memoryId;
        _ownsClient = clientOverride is null;
        _http = clientOverride ?? AgentCoreHttpClientFactory.CreateSigned(region);
    }

    /// <inheritdoc/>
    /// <remarks>Returns <c>null</c> and deletes the remote record when the session has expired.</remarks>
    public async Task<AgentSession?> LoadAsync(string sessionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var path = $"/memories/{Uri.EscapeDataString(_memoryId)}/sessions/{Uri.EscapeDataString(sessionId)}";
        using var response = await _http.GetAsync(path, ct).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var record = await response.Content
            .ReadFromJsonAsync<SessionRecord>(_json, ct)
            .ConfigureAwait(false);

        if (record is null) return null;

        var messages = JsonSerializer.Deserialize<List<Message>>(record.MessagesJson, _json)
            ?? [];

        var state = string.IsNullOrEmpty(record.StateJson)
            ? new Dictionary<string, object?>()
            : JsonSerializer.Deserialize<Dictionary<string, object?>>(record.StateJson, _json)
              ?? [];

        var session = new AgentSession(
            SessionId: sessionId,
            Messages: messages,
            State: state,
            LastUpdated: record.LastUpdated,
            ExpiresAt: record.ExpiresAt);

        // Enforce expiry: treat expired sessions as not found and clean up.
        if (session.ExpiresAt.HasValue && session.ExpiresAt.Value <= DateTimeOffset.UtcNow)
        {
            await DeleteAsync(sessionId, ct).ConfigureAwait(false);
            return null;
        }

        return session;
    }

    /// <inheritdoc/>
    public async Task SaveAsync(string sessionId, AgentSession session, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(session);

        var record = new SessionRecord(
            SessionId: sessionId,
            MessagesJson: JsonSerializer.Serialize(session.Messages, _json),
            StateJson: JsonSerializer.Serialize(session.State, _json),
            LastUpdated: session.LastUpdated,
            ExpiresAt: session.ExpiresAt);

        var path = $"/memories/{Uri.EscapeDataString(_memoryId)}/sessions/{Uri.EscapeDataString(sessionId)}";
        using var response = await _http.PutAsJsonAsync(path, record, _json, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string sessionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var path = $"/memories/{Uri.EscapeDataString(_memoryId)}/sessions/{Uri.EscapeDataString(sessionId)}";
        using var response = await _http.DeleteAsync(path, ct).ConfigureAwait(false);

        // 404 is acceptable — idempotent delete.
        if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
            response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_ownsClient)
            _http.Dispose();

        await ValueTask.CompletedTask.ConfigureAwait(false);
    }

    // Wire format stored in AgentCore Memory.
    private sealed record SessionRecord(
        [property: JsonPropertyName("sessionId")]    string SessionId,
        [property: JsonPropertyName("messagesJson")] string MessagesJson,
        [property: JsonPropertyName("stateJson")]    string StateJson,
        [property: JsonPropertyName("lastUpdated")]  DateTimeOffset LastUpdated,
        [property: JsonPropertyName("expiresAt")]    DateTimeOffset? ExpiresAt = null);
}
