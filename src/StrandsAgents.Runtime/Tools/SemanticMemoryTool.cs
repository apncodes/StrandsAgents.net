using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using StrandsAgents.Core;

namespace StrandsAgents.Runtime.Tools;

/// <summary>
/// Agent-initiated semantic (vector) memory operations via Amazon Bedrock AgentCore Memory.
///
/// <para>
/// Unlike <see cref="AgentCoreMemoryTool"/> which requires an exact key, this tool exposes
/// a <c>search_memory</c> operation that retrieves memories by semantic similarity — the
/// agent describes what it is looking for in natural language and receives the closest
/// matches ranked by cosine similarity score.
/// </para>
///
/// <para>
/// All HTTP requests are signed with AWS SigV4. Pass a <paramref name="clientOverride"/>
/// to inject a plain <see cref="HttpClient"/> for unit testing (bypasses SigV4).
/// </para>
/// </summary>
public sealed class SemanticMemoryTool : ITool, IAsyncDisposable
{
    private const string ToolName = "agentcore_semantic_memory";
    private const int DefaultTopK = 5;
    private const int MinTopK = 1;
    private const int MaxTopK = 100;

    private static readonly ToolDefinition _definition = new(
        Name: ToolName,
        Description: """
            Stores, searches, or deletes memories in Amazon Bedrock AgentCore Memory using
            semantic (vector) search. Use search_memory to retrieve memories by meaning
            rather than exact key — describe what you are looking for in natural language.

            Operations:
            - search_memory: Find memories semantically similar to a query string.
                             Returns a ranked list of { key, value, score } objects.
            - store_memory:  Save a key/value pair. Optionally set ttl_seconds for automatic expiry.
            - delete_memory: Remove a memory entry by key.
            """,
        InputSchema: JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "operation": {
                  "type": "string",
                  "enum": ["search_memory", "store_memory", "delete_memory"],
                  "description": "The memory operation to perform."
                },
                "query": {
                  "type": "string",
                  "description": "Natural-language search query. Required for search_memory."
                },
                "top_k": {
                  "type": "integer",
                  "description": "Maximum number of results to return for search_memory. Range: 1–100. Default: 5."
                },
                "key": {
                  "type": "string",
                  "description": "Unique identifier for the memory entry. Required for store_memory and delete_memory."
                },
                "value": {
                  "type": "string",
                  "description": "The value to store. Required for store_memory."
                },
                "ttl_seconds": {
                  "type": "integer",
                  "description": "Optional TTL in seconds for store_memory. Must be a positive integer."
                }
              },
              "required": ["operation"]
            }
            """).RootElement.Clone());

    private readonly HttpClient _http;
    private readonly string _memoryId;
    private readonly bool _ownsClient;

    /// <summary>
    /// Initialises a new <see cref="SemanticMemoryTool"/>.
    /// </summary>
    /// <param name="memoryId">The AgentCore memory resource ID.</param>
    /// <param name="region">AWS region. Default: <c>us-east-1</c>.</param>
    /// <param name="clientOverride">
    /// Optional pre-configured <see cref="HttpClient"/>. When provided, the tool does
    /// not own the client and will not dispose it. Intended for testing — bypasses SigV4.
    /// </param>
    public SemanticMemoryTool(
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
    public ToolDefinition Definition => _definition;

    /// <inheritdoc/>
    public async Task<ToolResult> InvokeAsync(JsonElement input, CancellationToken ct = default)
    {
        if (!input.TryGetProperty("operation", out var opEl))
            return ToolResult.Failure(ToolName, "Missing required field: operation.");

        var operation = opEl.GetString();

        return operation switch
        {
            "search_memory" => await HandleSearchAsync(input, ct).ConfigureAwait(false),
            "store_memory"  => await HandleStoreAsync(input, ct).ConfigureAwait(false),
            "delete_memory" => await HandleDeleteAsync(input, ct).ConfigureAwait(false),
            _ => ToolResult.Failure(ToolName,
                $"Unknown operation '{operation}'. Supported: search_memory, store_memory, delete_memory."),
        };
    }

    // ── search_memory ─────────────────────────────────────────────────────────

    private async Task<ToolResult> HandleSearchAsync(JsonElement input, CancellationToken ct)
    {
        if (!input.TryGetProperty("query", out var queryEl) ||
            queryEl.GetString() is not { Length: > 0 } query)
            return ToolResult.Failure(ToolName, "query must be a non-empty string.");

        var topK = DefaultTopK;
        if (input.TryGetProperty("top_k", out var topKEl) && topKEl.ValueKind == JsonValueKind.Number)
        {
            topK = topKEl.GetInt32();
            if (topK < MinTopK || topK > MaxTopK)
                return ToolResult.Failure(ToolName,
                    $"top_k must be between {MinTopK} and {MaxTopK}. Got: {topK}.");
        }

        return await SearchAsync(query, topK, ct).ConfigureAwait(false);
    }

    private async Task<ToolResult> SearchAsync(string query, int topK, CancellationToken ct)
    {
        var payload = new { query, topK };
        var path = $"/memories/{Uri.EscapeDataString(_memoryId)}/search";
        using var response = await _http.PostAsJsonAsync(path, payload, ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            return ToolResult.Failure(ToolName,
                $"search_memory failed. Status: {(int)response.StatusCode}");

        var results = await response.Content
            .ReadFromJsonAsync<List<SemanticSearchResult>>(_jsonOptions, ct)
            .ConfigureAwait(false) ?? [];

        // Sort descending by score (API may already do this, but enforce it client-side).
        results.Sort((a, b) => b.Score.CompareTo(a.Score));

        var json = JsonSerializer.Serialize(results, _jsonOptions);
        return ToolResult.Success(ToolName, json);
    }

    // ── store_memory ──────────────────────────────────────────────────────────

    private async Task<ToolResult> HandleStoreAsync(JsonElement input, CancellationToken ct)
    {
        if (!input.TryGetProperty("key", out var keyEl) ||
            keyEl.GetString() is not { Length: > 0 } key)
            return ToolResult.Failure(ToolName, "key must be a non-empty string.");

        if (!input.TryGetProperty("value", out var valueEl) ||
            valueEl.GetString() is not { } value)
            return ToolResult.Failure(ToolName, "value is required for store_memory.");

        int? ttlSeconds = null;
        if (input.TryGetProperty("ttl_seconds", out var ttlEl) && ttlEl.ValueKind == JsonValueKind.Number)
        {
            var ttl = ttlEl.GetInt32();
            if (ttl <= 0)
                return ToolResult.Failure(ToolName, "ttl_seconds must be a positive integer.");
            ttlSeconds = ttl;
        }

        var payload = ttlSeconds.HasValue
            ? (object)new { key, value, ttlSeconds = ttlSeconds.Value }
            : new { key, value };

        var path = $"/memories/{Uri.EscapeDataString(_memoryId)}/records";
        using var response = await _http.PostAsJsonAsync(path, payload, ct).ConfigureAwait(false);

        return response.IsSuccessStatusCode
            ? ToolResult.Success(ToolName, $"Stored memory: {key}")
            : ToolResult.Failure(ToolName, $"store_memory failed. Status: {(int)response.StatusCode}");
    }

    // ── delete_memory ─────────────────────────────────────────────────────────

    private async Task<ToolResult> HandleDeleteAsync(JsonElement input, CancellationToken ct)
    {
        if (!input.TryGetProperty("key", out var keyEl) ||
            keyEl.GetString() is not { Length: > 0 } key)
            return ToolResult.Failure(ToolName, "key must be a non-empty string.");

        var path = $"/memories/{Uri.EscapeDataString(_memoryId)}/records/{Uri.EscapeDataString(key)}";
        using var response = await _http.DeleteAsync(path, ct).ConfigureAwait(false);

        return response.IsSuccessStatusCode
            ? ToolResult.Success(ToolName, $"Deleted memory: {key}")
            : ToolResult.Failure(ToolName, $"delete_memory failed. Status: {(int)response.StatusCode}");
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_ownsClient)
            _http.Dispose();

        await ValueTask.CompletedTask.ConfigureAwait(false);
    }

    // ── Internal wire types ───────────────────────────────────────────────────

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record SemanticSearchResult(
        [property: JsonPropertyName("key")]   string Key,
        [property: JsonPropertyName("value")] string Value,
        [property: JsonPropertyName("score")] double Score);
}
