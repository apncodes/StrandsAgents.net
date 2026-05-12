using System.Net.Http.Json;
using System.Text.Json;
using StrandsAgents.Core;

namespace StrandsAgents.Runtime.Tools;

/// <summary>
/// Agent-initiated explicit memory operations via Amazon Bedrock AgentCore Memory.
///
/// <para>
/// This tool enables the agent to explicitly store, retrieve, and delete memories.
/// It is different from <c>AgentCoreSessionManager</c>, which handles automatic
/// persistence of the full conversation history on every call.
/// </para>
///
/// <para>
/// Use this tool when the agent needs to remember specific facts across sessions
/// that are not naturally captured in conversation history — e.g. user preferences,
/// domain knowledge, or task-specific state.
/// </para>
///
/// <para>
/// All HTTP requests are signed with AWS SigV4. Pass a <paramref name="clientOverride"/>
/// to inject a plain <see cref="HttpClient"/> for unit testing (bypasses SigV4).
/// </para>
/// </summary>
public sealed class AgentCoreMemoryTool : ITool, IAsyncDisposable
{
    private static readonly ToolDefinition _definition = new(
        Name: "agentcore_memory",
        Description: """
            Stores, retrieves, or deletes memories in Amazon Bedrock AgentCore Memory.
            Use this to persist specific facts, preferences, or knowledge across sessions.
            Prefer conversation history for short-term context; use this tool for facts
            that must survive beyond the current conversation.

            Operations:
            - store_memory: Save a key/value pair to long-term memory. Optionally set ttl_seconds for automatic expiry.
            - retrieve_memory: Fetch a stored memory by key.
            - delete_memory: Remove a memory entry by key.
            """,
        InputSchema: JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "operation": {
                  "type": "string",
                  "enum": ["store_memory", "retrieve_memory", "delete_memory"],
                  "description": "The memory operation to perform."
                },
                "key": {
                  "type": "string",
                  "description": "Unique identifier for the memory entry."
                },
                "value": {
                  "type": "string",
                  "description": "The value to store. Required for store_memory."
                },
                "ttl_seconds": {
                  "type": "integer",
                  "description": "Optional TTL in seconds for store_memory. The memory entry will be automatically deleted after this many seconds. Must be a positive integer."
                }
              },
              "required": ["operation", "key"]
            }
            """).RootElement.Clone());

    private readonly HttpClient _http;
    private readonly string _memoryId;
    private readonly bool _ownsClient;

    /// <summary>
    /// Initialises a new <see cref="AgentCoreMemoryTool"/>.
    /// </summary>
    /// <param name="memoryId">The AgentCore memory resource ID.</param>
    /// <param name="region">AWS region. Default: <c>us-east-1</c>.</param>
    /// <param name="clientOverride">
    /// Optional pre-configured <see cref="HttpClient"/>. When provided, the tool does
    /// not own the client and will not dispose it. Intended for testing — bypasses SigV4.
    /// </param>
    public AgentCoreMemoryTool(
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
        if (!input.TryGetProperty("operation", out var opEl) ||
            !input.TryGetProperty("key", out var keyEl))
            return ToolResult.Failure("agentcore_memory", "Missing required fields: operation, key.");

        var operation = opEl.GetString();
        var key = keyEl.GetString();

        if (string.IsNullOrWhiteSpace(key))
            return ToolResult.Failure("agentcore_memory", "key must be a non-empty string.");

        return operation switch
        {
            "store_memory"    => await StoreAsync(key, input, ct).ConfigureAwait(false),
            "retrieve_memory" => await RetrieveAsync(key, ct).ConfigureAwait(false),
            "delete_memory"   => await DeleteAsync(key, ct).ConfigureAwait(false),
            _ => ToolResult.Failure("agentcore_memory",
                $"Unknown operation '{operation}'. Supported: store_memory, retrieve_memory, delete_memory."),
        };
    }

    private async Task<ToolResult> StoreAsync(string key, JsonElement input, CancellationToken ct)
    {
        if (!input.TryGetProperty("value", out var valueEl) ||
            valueEl.GetString() is not { } value)
            return ToolResult.Failure("agentcore_memory", "Missing required field: value (required for store_memory).");

        // Validate optional ttl_seconds.
        int? ttlSeconds = null;
        if (input.TryGetProperty("ttl_seconds", out var ttlEl) && ttlEl.ValueKind == JsonValueKind.Number)
        {
            var ttl = ttlEl.GetInt32();
            if (ttl <= 0)
                return ToolResult.Failure("agentcore_memory", "ttl_seconds must be a positive integer.");
            ttlSeconds = ttl;
        }

        var payload = ttlSeconds.HasValue
            ? (object)new { key, value, ttlSeconds = ttlSeconds.Value }
            : new { key, value };

        var path = $"/memories/{Uri.EscapeDataString(_memoryId)}/records";
        using var response = await _http.PostAsJsonAsync(path, payload, ct).ConfigureAwait(false);

        return response.IsSuccessStatusCode
            ? ToolResult.Success("agentcore_memory", $"Stored memory: {key}")
            : ToolResult.Failure("agentcore_memory",
                $"Failed to store memory. Status: {(int)response.StatusCode}");
    }

    private async Task<ToolResult> RetrieveAsync(string key, CancellationToken ct)
    {
        var path = $"/memories/{Uri.EscapeDataString(_memoryId)}/records/{Uri.EscapeDataString(key)}";
        using var response = await _http.GetAsync(path, ct).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return ToolResult.Success("agentcore_memory", $"No memory found for key: {key}");

        if (!response.IsSuccessStatusCode)
            return ToolResult.Failure("agentcore_memory",
                $"Failed to retrieve memory. Status: {(int)response.StatusCode}");

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ToolResult.Success("agentcore_memory", json);
    }

    private async Task<ToolResult> DeleteAsync(string key, CancellationToken ct)
    {
        var path = $"/memories/{Uri.EscapeDataString(_memoryId)}/records/{Uri.EscapeDataString(key)}";
        using var response = await _http.DeleteAsync(path, ct).ConfigureAwait(false);

        return response.IsSuccessStatusCode
            ? ToolResult.Success("agentcore_memory", $"Deleted memory: {key}")
            : ToolResult.Failure("agentcore_memory",
                $"Failed to delete memory. Status: {(int)response.StatusCode}");
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_ownsClient)
            _http.Dispose();

        await ValueTask.CompletedTask.ConfigureAwait(false);
    }
}
